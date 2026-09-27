using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Keeps a window of decoded frames ahead of the playhead, like a video player's buffer: frame files download in
    /// parallel and decode on worker threads; frames behind the playhead are dropped.
    /// </summary>
    sealed class FrameWindow<T> : IDisposable where T : class
    {
        readonly string[] _urls;
        readonly Func<byte[], int, T> _decode;
        readonly int _ahead;
        readonly int _parallel;
        readonly Dictionary<int, T> _ready = new Dictionary<int, T>();
        readonly Dictionary<int, UnityWebRequest> _downloads = new Dictionary<int, UnityWebRequest>();
        readonly Dictionary<int, Task<T>> _decoding = new Dictionary<int, Task<T>>();
        readonly List<int> _scratch = new List<int>();
        float _retryAt;

        public FrameWindow(string[] urls, Func<byte[], int, T> decode, int aheadFrames, int parallel)
        {
            _urls = urls;
            _decode = decode;
            _ahead = Mathf.Max(1, aheadFrames);
            _parallel = Mathf.Max(1, parallel);
        }

        public int Count => _urls.Length;
        public int Failures { get; private set; }
        public string LastError { get; private set; }

        public void Update(int playhead, bool loop)
        {
            CollectDownloads();
            CollectDecodes();
            Evict(playhead, loop);
            if (Time.unscaledTime < _retryAt)
                return;
            for (var k = 0; k <= _ahead && _downloads.Count + _decoding.Count < _parallel; k++)
            {
                var frame = HologramLoader.WindowFrame(playhead, k, _urls.Length, loop);
                if (frame < 0)
                    break;
                if (!_ready.ContainsKey(frame) && !_downloads.ContainsKey(frame) && !_decoding.ContainsKey(frame))
                    Start(frame);
            }
        }

        public bool TryGet(int frame, out T value) => _ready.TryGetValue(frame, out value);

        public int BufferedAhead(int playhead, bool loop)
        {
            var n = 0;
            while (n <= _ahead)
            {
                var frame = HologramLoader.WindowFrame(playhead, n, _urls.Length, loop);
                if (frame < 0 || !_ready.ContainsKey(frame))
                    break;
                n++;
            }

            return n;
        }

        /// <summary>After a seek: drop downloads the new window doesn't need.</summary>
        public void Retarget(int playhead, bool loop)
        {
            _scratch.Clear();
            foreach (var frame in _downloads.Keys)
                if (!InWindow(frame, playhead, loop))
                    _scratch.Add(frame);
            foreach (var frame in _scratch)
            {
                _downloads[frame].Abort();
                _downloads[frame].Dispose();
                _downloads.Remove(frame);
            }

            Evict(playhead, loop);
        }

        void Start(int frame)
        {
            var request = UnityWebRequest.Get(_urls[frame]);
            request.timeout = 60;
            request.SendWebRequest();
            _downloads.Add(frame, request);
        }

        void CollectDownloads()
        {
            _scratch.Clear();
            foreach (var pair in _downloads)
                if (pair.Value.isDone)
                    _scratch.Add(pair.Key);
            foreach (var frame in _scratch)
            {
                var request = _downloads[frame];
                _downloads.Remove(frame);
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var bytes = request.downloadHandler.data;
                    _decoding.Add(frame, Task.Run(() => _decode(bytes, frame)));
                }
                else
                {
                    Fail(frame, request.error);
                }

                request.Dispose();
            }
        }

        void CollectDecodes()
        {
            _scratch.Clear();
            foreach (var pair in _decoding)
                if (pair.Value.IsCompleted)
                    _scratch.Add(pair.Key);
            foreach (var frame in _scratch)
            {
                var task = _decoding[frame];
                _decoding.Remove(frame);
                if (task.IsFaulted)
                    Fail(frame, task.Exception?.GetBaseException().Message);
                else
                    _ready[frame] = task.Result;
            }
        }

        void Fail(int frame, string error)
        {
            Failures++;
            LastError = $"frame {frame} ({MediaSource.FileName(_urls[frame])}): {error}";
            if (Failures <= 3 || Failures % 50 == 0)
                ImmersiveXLog.Warn($"Sequence {LastError}. Retrying.");
            _retryAt = Time.unscaledTime + 1f;
        }

        void Evict(int playhead, bool loop)
        {
            _scratch.Clear();
            foreach (var frame in _ready.Keys)
                if (!InWindow(frame, playhead, loop))
                    _scratch.Add(frame);
            foreach (var frame in _scratch)
                _ready.Remove(frame);
        }

        bool InWindow(int frame, int playhead, bool loop)
        {
            var ahead = frame - playhead;
            if (loop && ahead < 0)
                ahead += _urls.Length;
            return ahead >= -2 && ahead <= _ahead;
        }

        public void Dispose()
        {
            foreach (var request in _downloads.Values)
            {
                request.Abort();
                request.Dispose();
            }

            _downloads.Clear();
            _decoding.Clear();
            _ready.Clear();
        }
    }
}
