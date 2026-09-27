using System.Collections;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A sequence of splat or point-cloud frames (<c>.ply .splat .spz .ksplat</c>): 4D Gaussian splatting exports and
    /// point-cloud volumetric video. Each frame is decoded, trimmed to the most visible splats and sorted on a worker thread;
    /// the first frame sets the scale and position, so a performer can move within the capture.
    /// </summary>
    sealed class SplatSequencePlayable : IMediaPlayable
    {
        const float ResortDistance = 0.05f;

        /// <summary>A packed frame and its draw order for where the viewer was when it was decoded.</summary>
        sealed class Frame
        {
            public PackedSplats Packed;
            public uint[] Order;
        }

        readonly MediaSequence _sequence;
        MediaContext _context;
        HologramRenderer _renderer;
        FrameWindow<Frame> _window;
        SequenceClock _clock;
        IStreamAudio _audio;
        SplatSorter _sorter;
        Frame _current;
        int _shown = -1;
        bool _fitted;
        Bounds _bounds;
        Vector3 _viewerInSplats;
        Vector3 _sortedFrom;
        int _capacity;

        public SplatSequencePlayable(MediaSequence sequence) => _sequence = sequence;

        public string Description =>
            $"Splat sequence · {_sequence.Frames.Length} frames at {_sequence.Fps:0.#} fps · {_current?.Packed.Header.Count ?? 0:N0} splats";

        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => _shown >= 0;
        public string Error { get; private set; }
        public double Duration => _sequence.Frames.Length > 1 ? _sequence.Duration : 0d;
        public double Position => _clock?.Time ?? 0d;
        public bool IsBuffering => _clock != null && _clock.WantPlaying && !_clock.Running;
        public float BufferProgress => _window == null ? 1f : Mathf.Clamp01(_window.BufferedAhead(FrameAt(Position), _context.Loop) / Mathf.Max(1f, Preroll));
        public bool HasAudio => _audio != null && !_audio.Failed;
        public Bounds Bounds => _bounds;

        float Preroll => Mathf.Min(_context.PrerollSeconds * _sequence.Fps, _sequence.Frames.Length - FrameAt(Position));

        int FrameAt(double seconds) => Mathf.Clamp((int)(seconds * _sequence.Fps), 0, _sequence.Frames.Length - 1);

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            _capacity = context.MaxGaussians;
            var up = context.Up != UpAxis.Auto ? context.Up : _sequence.Up;
            var rows = (_capacity + SplatPacker.TextureWidth - 1) / SplatPacker.TextureWidth;
            _renderer = new HologramRenderer(context.Root, MediaAssets.SplatMaterial, _capacity,
                HologramFrame.ExpectedBytes(rows, SplatPacker.TextureWidth) / 4, new Vector2(-9f, 1f));

            var ahead = Mathf.CeilToInt(Mathf.Min(context.BufferSeconds, 2f) * _sequence.Fps);
            _window = new FrameWindow<Frame>(_sequence.Frames, (bytes, index) => Decode(bytes, index, up), ahead, Mathf.Min(context.ParallelDownloads, 8));
            if (!string.IsNullOrEmpty(_sequence.AudioUrl))
                _audio = StreamAudio.Open(_sequence.AudioUrl, context.Loop, context.Host);
            _clock = new SequenceClock(_sequence.Duration, context.Loop, _audio);

            var waited = 0f;
            while (_shown < 0 && waited < 60f)
            {
                _window.Update(0, context.Loop);
                if (_window.TryGet(0, out var first))
                    Show(0, first);
                else if (_window.Failures > 0 && waited > 10f)
                    break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_shown < 0)
                Error = $"The sequence's first frame didn't load ({_window.LastError ?? "timed out"}).";
        }

        /// <summary>On a worker thread: decode, turn y-down, keep the most visible splats, pack, and sort for the viewer.</summary>
        Frame Decode(byte[] bytes, int index, UpAxis up)
        {
            var cloud = SplatDecoders.Decode(bytes, MediaSource.Extension(_sequence.Frames[index]));
            var total = cloud.Count;
            cloud.ToYDown(up);
            cloud = cloud.Strongest(_capacity);
            var data = SplatPacker.Pack(cloud, out var header, out var range);
            var order = new uint[cloud.Count];
            HologramFrame.SortBackToFront(cloud.Positions, cloud.Count, _viewerInSplats, new int[cloud.Count], new int[65536], order);
            return new Frame { Packed = new PackedSplats(data, header, range, cloud.Positions, total), Order = order };
        }

        public void Play() => _clock?.Play();
        public void Pause() => _clock?.Pause();

        public void Seek(double seconds)
        {
            if (_clock == null)
                return;
            _clock.Seek(seconds);
            _window.Retarget(FrameAt(_clock.Time), _context.Loop);
        }

        public void SetVolume(float volume) => _audio?.SetVolume(volume);

        public void Tick(float deltaTime)
        {
            if (_clock == null)
                return;
            var frame = FrameAt(_clock.Time);
            _window.Update(frame, _context.Loop);
            if (_clock.WantPlaying && !_clock.Running && _window.BufferedAhead(frame, _context.Loop) >= Mathf.Max(1f, Preroll))
                _clock.Resume();
            _clock.Advance(deltaTime);
            frame = FrameAt(_clock.Time);
            if (frame == _shown)
                return;
            if (_window.TryGet(frame, out var next))
                Show(frame, next);
            else if (_clock.Running)
                _clock.Halt();
        }

        void Show(int index, Frame frame)
        {
            var header = frame.Packed.Header;
            _shown = index;
            _current = frame;
            _renderer.SetScaleRange(frame.Packed.ScaleRange);
            _renderer.UploadFrame(frame.Packed.Data, frame.Packed.Data.Length, header, HologramFrame.AlphaStart(header, SplatPacker.TextureWidth));
            _renderer.UploadOrder(frame.Order, header.Count);
            _sorter = new SplatSorter(frame.Packed.Positions, header.Count);
            if (_fitted || header.Count == 0)
                return;

            var fit = HologramFrame.Fit(frame.Packed.Positions, header.Count, header.Min.y, header.Max.y, new int[256]);
            SplatFit.Apply(_renderer.Transform, fit, _context.Height);
            var scale = _context.Height / Mathf.Max(fit.Height, 0.05f);
            var size = (header.Max - header.Min) * scale;
            _bounds = new Bounds(new Vector3(0f, _context.Height * 0.5f, 0f), new Vector3(Mathf.Min(size.x, 4f), _context.Height, Mathf.Min(size.z, 4f)));
            _fitted = true;
        }

        public void LateTick(Transform viewer)
        {
            if (viewer == null || _renderer == null)
                return;
            _viewerInSplats = _renderer.Transform.InverseTransformPoint(viewer.position);

            // While paused (no new frames), re-sort the frame on screen when the viewer moves.
            if (_sorter == null || _current == null)
                return;
            if (_sorter.TryFinish(out var order))
                _renderer.UploadOrder(order, _current.Packed.Header.Count);
            var fromHere = _context.Root.InverseTransformPoint(viewer.position);
            if (_clock != null && !_clock.Running && !_sorter.Busy && (fromHere - _sortedFrom).sqrMagnitude > ResortDistance * ResortDistance)
            {
                _sorter.Start(_viewerInSplats);
                _sortedFrom = fromHere;
            }
        }

        public void Dispose()
        {
            _window?.Dispose();
            _audio?.Dispose();
            _renderer?.Dispose();
        }
    }
}
