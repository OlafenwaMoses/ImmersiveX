using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Streams hologram frames into a rolling window ahead of the playhead, like a video player's buffer.
    /// Requests run in parallel (one connection is latency-bound, so throughput comes from many) and write straight
    /// into pooled <c>uint[]</c> frames, so streaming allocates nothing per frame.
    /// </summary>
    sealed class HologramLoader : IDisposable
    {
        const int ChunkBytes = 64 * 1024;
        const float RetryDelaySeconds = 1f;

        readonly HologramManifest _manifest;
        readonly HologramTier _tier;
        readonly int _ahead;
        readonly int _parallel;
        readonly int _frameUints;
        readonly Stack<uint[]> _pool = new Stack<uint[]>();
        readonly Stack<byte[]> _chunks = new Stack<byte[]>();
        readonly Dictionary<int, uint[]> _ready = new Dictionary<int, uint[]>();
        readonly Dictionary<int, Download> _inflight = new Dictionary<int, Download>();
        readonly List<int> _scratch = new List<int>();
        float _retryAt;
        long _windowBytes;
        float _windowStart;
        float _rate;

        public HologramLoader(HologramManifest manifest, HologramTier tier, int aheadFrames, int parallel)
        {
            _manifest = manifest;
            _tier = tier;
            _ahead = Mathf.Max(1, aheadFrames);
            _parallel = Mathf.Max(1, parallel);
            _frameUints = (tier.MaxBytes + 3) / 4;
        }

        public HologramTier Tier => _tier;

        /// <summary>Frames downloaded but not yet shown or evicted.</summary>
        public int ReadyCount => _ready.Count;

        /// <summary>Failed requests so far (they're retried).</summary>
        public int Failures { get; private set; }

        public string LastError { get; private set; }

        /// <summary>Recent download rate in megabytes per second (it falls when the buffer is full and waiting).</summary>
        public float MegabytesPerSecond => _rate;

        /// <summary>Collect finished downloads, drop frames already played, and request what the window is missing.</summary>
        public void Update(int playhead, bool loop)
        {
            Collect();
            MeasureRate();
            Evict(playhead, loop);

            if (Time.unscaledTime < _retryAt)
                return;
            for (var k = 0; k <= _ahead && _inflight.Count < _parallel; k++)
            {
                var frame = WindowFrame(playhead, k, _manifest.FrameCount, loop);
                if (frame < 0)
                    break;
                if (!_ready.ContainsKey(frame) && !_inflight.ContainsKey(frame))
                    Start(frame);
            }
        }

        public bool TryGet(int frame, out uint[] data) => _ready.TryGetValue(frame, out data);

        /// <summary>Consecutive frames ready from <paramref name="playhead"/> on.</summary>
        public int BufferedAhead(int playhead, bool loop)
        {
            var n = 0;
            while (n <= _ahead)
            {
                var frame = WindowFrame(playhead, n, _manifest.FrameCount, loop);
                if (frame < 0 || !_ready.ContainsKey(frame))
                    break;
                n++;
            }

            return n;
        }

        /// <summary>After a seek: cancel downloads the new window doesn't need.</summary>
        public void Retarget(int playhead, bool loop)
        {
            _scratch.Clear();
            foreach (var frame in _inflight.Keys)
                if (!InWindow(frame, playhead, loop))
                    _scratch.Add(frame);
            foreach (var frame in _scratch)
            {
                var download = _inflight[frame];
                _inflight.Remove(frame);
                download.Request.Abort();
                Recycle(download);
            }

            Evict(playhead, loop);
        }

        /// <summary>
        /// The frame <paramref name="offset"/> frames after <paramref name="playhead"/>, wrapping when looping;
        /// -1 past the end.
        /// </summary>
        public static int WindowFrame(int playhead, int offset, int frameCount, bool loop)
        {
            var frame = playhead + offset;
            if (frame < frameCount)
                return frame;
            return loop ? frame % frameCount : -1;
        }

        bool InWindow(int frame, int playhead, bool loop)
        {
            var ahead = frame - playhead;
            if (loop && ahead < 0)
                ahead += _manifest.FrameCount;
            return ahead >= -2 && ahead <= _ahead; // keep a couple of frames behind for the one on screen
        }

        void Start(int frame)
        {
            var bytes = _tier.Bytes[frame];
            var target = _pool.Count > 0 ? _pool.Pop() : new uint[_frameUints];
            var chunk = _chunks.Count > 0 ? _chunks.Pop() : new byte[ChunkBytes];
            var handler = new FrameHandler(chunk, target, bytes);
            var request = new UnityWebRequest(_manifest.FrameUrl(_tier, frame), UnityWebRequest.kHttpVerbGET, handler, null)
            {
                timeout = 30,
                disposeDownloadHandlerOnDispose = true,
            };
            request.SendWebRequest();
            _inflight.Add(frame, new Download(request, handler, target, chunk));
        }

        void Collect()
        {
            _scratch.Clear();
            foreach (var pair in _inflight)
                if (pair.Value.Request.isDone)
                    _scratch.Add(pair.Key);

            foreach (var frame in _scratch)
            {
                var download = _inflight[frame];
                _inflight.Remove(frame);
                if (download.Request.result == UnityWebRequest.Result.Success && download.Handler.Complete)
                {
                    _windowBytes += download.Handler.Received;
                    _ready[frame] = download.Frame;
                    download.Request.Dispose();
                    _chunks.Push(download.Chunk);
                }
                else
                {
                    Failures++;
                    LastError = download.Request.error ?? $"frame {frame}: {download.Handler.Received} of {download.Handler.Expected} bytes";
                    if (Failures <= 3 || Failures % 50 == 0)
                        ImmersiveXLog.Warn($"Hologram frame {frame} failed ({LastError}). Retrying.");
                    _retryAt = Time.unscaledTime + RetryDelaySeconds;
                    Recycle(download);
                }
            }
        }

        void MeasureRate()
        {
            var elapsed = Time.unscaledTime - _windowStart;
            if (elapsed < 1f)
                return;
            var rate = (float)(_windowBytes / 1e6 / elapsed);
            _rate = _rate <= 0f ? rate : Mathf.Lerp(_rate, rate, 0.5f);
            _windowBytes = 0;
            _windowStart = Time.unscaledTime;
        }

        void Evict(int playhead, bool loop)
        {
            _scratch.Clear();
            foreach (var frame in _ready.Keys)
                if (!InWindow(frame, playhead, loop))
                    _scratch.Add(frame);
            foreach (var frame in _scratch)
            {
                _pool.Push(_ready[frame]);
                _ready.Remove(frame);
            }
        }

        void Recycle(Download download)
        {
            download.Request.Dispose();
            _pool.Push(download.Frame);
            _chunks.Push(download.Chunk);
        }

        public void Dispose()
        {
            foreach (var download in _inflight.Values)
            {
                download.Request.Abort();
                download.Request.Dispose();
            }

            _inflight.Clear();
            _ready.Clear();
            _pool.Clear();
            _chunks.Clear();
        }

        readonly struct Download
        {
            public readonly UnityWebRequest Request;
            public readonly FrameHandler Handler;
            public readonly uint[] Frame;
            public readonly byte[] Chunk;

            public Download(UnityWebRequest request, FrameHandler handler, uint[] frame, byte[] chunk)
            {
                Request = request;
                Handler = handler;
                Frame = frame;
                Chunk = chunk;
            }
        }

        /// <summary>Copies the response straight into a pooled frame as it arrives.</summary>
        sealed class FrameHandler : DownloadHandlerScript
        {
            readonly uint[] _target;
            bool _overflow;

            public FrameHandler(byte[] chunk, uint[] target, int expected) : base(chunk)
            {
                _target = target;
                Expected = expected;
            }

            public int Expected { get; }
            public int Received { get; private set; }
            public bool Complete => !_overflow && Received == Expected;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                    return false;
                if (Received + dataLength > Expected)
                {
                    _overflow = true;
                    return false;
                }

                Buffer.BlockCopy(data, 0, _target, Received, dataLength);
                Received += dataLength;
                return true;
            }
        }
    }
}
