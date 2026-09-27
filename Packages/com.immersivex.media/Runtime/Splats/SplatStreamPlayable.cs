using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A GenXR 3.5D hologram stream: <c>stream.json</c> plus one file per frame per quality tier, and a soundtrack.
    /// Frames stream into a few seconds of buffer (waiting, like a video player, when it runs dry); the soundtrack is the
    /// clock. Every frame is fitted to the media's height with its feet on the floor; the fit glides within a shot and
    /// jumps at camera cuts. A stream with one fit for the whole clip (a 4D capture of one character) keeps that fit.
    /// </summary>
    sealed class SplatStreamPlayable : IMediaPlayable
    {
        const float FitGlideSeconds = 0.5f;
        const float ResortDistance = 0.05f;

        MediaContext _context;
        HologramManifest _manifest;
        HologramTier _tier;
        HologramLoader _loader;
        HologramRenderer _renderer;
        IStreamAudio _audio;

        float[] _positions;
        int[] _keys;
        uint[] _order;
        readonly int[] _buckets = new int[65536];
        readonly int[] _histogram = new int[256];

        HologramFrameHeader _header;
        int _shownFrame = -1;
        bool _hasFit;
        HologramFit _fitTarget;
        HologramFit _fitShown;
        Vector3 _sortedFrom;
        bool _needsSort;

        double _clock;
        bool _wantPlaying;
        bool _running;
        float _audioRetryAt;
        int _stalls;
        int _badFrames;

        public string Description => _tier == null
            ? "3.5D hologram stream"
            : $"3.5D hologram · {_tier.Name} quality · {_renderer?.Count ?? 0:N0} Gaussians · buffer {BufferedSeconds:0.0} s · " +
              $"{_loader.MegabytesPerSecond:0.0} MB/s · stalls {_stalls} · audio {AudioState}";

        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => _renderer != null;
        public string Error { get; private set; }
        public double Duration => _manifest?.Duration ?? 0d;
        public double Position => _clock;
        public bool IsBuffering => _wantPlaying && !_running;
        public float BufferProgress => _loader == null ? 0f : Mathf.Clamp01(_loader.BufferedAhead(FrameAt(_clock), Loop) / (float)PrerollAt(FrameAt(_clock)));
        public bool HasAudio => _audio != null && !_audio.Failed;

        public Bounds Bounds
        {
            get
            {
                var width = Mathf.Clamp(_context.Height * 0.5f, 0.4f, 1.2f);
                return new Bounds(new Vector3(0f, _context.Height * 0.5f, 0f), new Vector3(width, _context.Height, width));
            }
        }

        bool Loop => _context.Loop;
        float BufferedSeconds => _loader == null ? 0f : _loader.BufferedAhead(FrameAt(_clock), Loop) / _manifest.Fps;
        string AudioState => _audio == null ? "none" : _audio.Failed ? "unavailable" : !_audio.IsReady ? "loading" : _audio.IsPlaying ? "playing" : "ready";

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            string json = null;
            if (context.Prefetched != null)
                json = System.Text.Encoding.UTF8.GetString(context.Prefetched);
            else
                yield return MediaSource.FetchText(context.Url, (text, error) =>
                {
                    json = text;
                    Error = error;
                });
            if (Error != null)
                yield break;

            try
            {
                _manifest = HologramManifest.Parse(json, context.Url);
            }
            catch (Exception exception)
            {
                Error = $"Couldn't read the stream ({exception.Message}).";
                yield break;
            }

            _tier = _manifest.FindTier(context.Quality);
            if (!string.Equals(_tier.Name, context.Quality, StringComparison.OrdinalIgnoreCase))
                ImmersiveXLog.Warn($"Hologram: no '{context.Quality}' quality in this stream; using '{_tier.Name}'.");

            _loader = new HologramLoader(_manifest, _tier, Mathf.CeilToInt(context.BufferSeconds * _manifest.Fps), context.ParallelDownloads);
            _renderer = new HologramRenderer(context.Root, MediaAssets.SplatMaterial, _tier.MaxCount, (_tier.MaxBytes + 3) / 4, _manifest.ScaleRange);
            _positions = new float[_tier.MaxCount * 3];
            _keys = new int[_tier.MaxCount];
            _order = new uint[_tier.MaxCount];
            if (!string.IsNullOrEmpty(_manifest.AudioUrl))
                _audio = StreamAudio.Open(_manifest.AudioUrl, Loop, context.Host);
            if (_manifest.Fit.HasValue)
            {
                _fitShown = _fitTarget = _manifest.Fit.Value;
                _hasFit = true;
            }

            ImmersiveXLog.Info($"Hologram: {_manifest.FrameCount} frames at {_manifest.Fps:0.##} fps ({MediaControls.FormatTime(Duration)}) · " +
                               $"{_tier.Name} quality, up to {_tier.MaxCount:N0} Gaussians · needs {_tier.RateMegabytesPerSecond:0.#} MB/s · " +
                               $"{context.ParallelDownloads} parallel downloads · {(_manifest.Fit.HasValue ? "one fit for the clip" : "fitted per frame")}");
        }

        public void Play()
        {
            _wantPlaying = true;
        }

        public void Pause()
        {
            _wantPlaying = false;
            Halt();
        }

        public void Seek(double seconds)
        {
            if (_manifest == null)
                return;
            _clock = Math.Min(Math.Max(0d, seconds), Duration - 1e-3);
            _shownFrame = -1;
            _loader.Retarget(FrameAt(_clock), Loop);
            Halt(); // resumes once the new position is buffered
            _audio?.Seek(_clock);
        }

        public void SetVolume(float volume) => _audio?.SetVolume(volume);

        public void Dispose()
        {
            _loader?.Dispose();
            _renderer?.Dispose();
            _audio?.Dispose();
        }

        int Preroll => Mathf.Max(1, Mathf.CeilToInt(_context.PrerollSeconds * (_manifest?.Fps ?? 24f)));

        /// <summary>Frames needed before playing from <paramref name="frame"/>: the preroll, or what's left near the end.</summary>
        int PrerollAt(int frame) => Loop || _manifest == null ? Preroll : Mathf.Max(1, Mathf.Min(Preroll, _manifest.FrameCount - frame));

        int FrameAt(double seconds) =>
            _manifest == null ? 0 : Mathf.Clamp((int)(seconds * _manifest.Fps), 0, _manifest.FrameCount - 1);

        public void Tick(float deltaTime)
        {
            if (_loader == null)
                return;

            var frame = FrameAt(_clock);
            _loader.Update(frame, Loop);
            if (_wantPlaying && !_running && _loader.BufferedAhead(frame, Loop) >= PrerollAt(frame))
            {
                _running = true;
                _audioRetryAt = 0f; // start the soundtrack on the next sync
            }

            if (_running)
            {
                Advance(deltaTime);
                frame = FrameAt(_clock);
            }

            if (frame != _shownFrame)
            {
                if (_loader.TryGet(frame, out var data))
                    Show(frame, data);
                else if (_running)
                    Stall();
            }
        }

        void Halt()
        {
            _running = false;
            _audio?.Pause();
        }

        void Stall()
        {
            _stalls++;
            if (_stalls <= 3 || _stalls % 20 == 0)
                ImmersiveXLog.Info($"Hologram: buffering at {MediaControls.FormatTime(_clock)} (stall {_stalls}; downloading at {_loader.MegabytesPerSecond:0.0} MB/s).");
            Halt();
        }

        /// <summary>Move the clock on; the soundtrack, when it plays, is the reference.</summary>
        void Advance(float deltaTime)
        {
            _clock += deltaTime;
            SyncAudio();
            if (_clock >= Duration)
            {
                if (Loop)
                {
                    _clock -= Duration;
                }
                else
                {
                    _clock = Duration - 1e-3;
                    _wantPlaying = false;
                    Halt();
                }
            }
        }

        /// <summary>Start the soundtrack where the clock is, then follow it: small drift is eased out, large drift jumps.</summary>
        void SyncAudio()
        {
            if (_audio == null || !_audio.IsReady || _audio.Failed)
                return;

            if (!_audio.IsPlaying)
            {
                if (Time.unscaledTime < _audioRetryAt)
                    return; // it was just told to play; give it a moment to start
                _audioRetryAt = Time.unscaledTime + 0.5f;
                if (Math.Abs(_audio.Time - _clock) > 0.1)
                    _audio.Seek(_clock);
                _audio.Play();
                return;
            }

            var drift = _audio.Time - _clock;
            if (drift > Duration * 0.5)
                drift -= Duration; // the track has looped and the clock hasn't yet (or the other way round)
            else if (drift < -Duration * 0.5)
                drift += Duration;
            _clock += Math.Abs(drift) > 0.25 ? drift : drift * 0.05;
        }

        void Show(int frame, uint[] data)
        {
            _shownFrame = frame;
            var header = HologramFrame.ReadHeader(data);
            var bytes = _tier.Bytes[frame];
            if (!HologramFrame.IsValid(header, _manifest.TextureWidth, bytes) || header.Count > _tier.MaxCount)
            {
                if (++_badFrames <= 3)
                    ImmersiveXLog.Warn($"Hologram frame {frame} doesn't match the manifest ({header.Count} Gaussians, {header.Rows} rows, {bytes} bytes); skipped.");
                return;
            }

            if (!_renderer.UploadFrame(data, bytes / 4, header, HologramFrame.AlphaStart(header, _manifest.TextureWidth)))
                return;

            _header = header;
            _needsSort = true;
            if (header.Count == 0)
                return; // an empty frame (the source has a few): nothing to draw, keep the current fit

            HologramFrame.DecodePositions(data, header, _positions);
            if (_manifest.Fit.HasValue)
                return; // one fit for the whole clip

            var fit = HologramFrame.Fit(_positions, header.Count, header.Min.y, header.Max.y, _histogram);
            if (!_hasFit || HologramFit.IsCut(_fitTarget, fit))
                _fitShown = fit; // a new shot: jump rather than glide
            _fitTarget = fit;
            _hasFit = true;
        }

        public void LateTick(Transform viewer)
        {
            if (_renderer == null || !_hasFit)
                return;

            var glide = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FitGlideSeconds);
            _fitShown = HologramFit.Lerp(_fitShown, _fitTarget, glide);
            SplatFit.Apply(_renderer.Transform, _fitShown, _context.Height);

            if (viewer == null)
                return;
            var fromHere = _context.Root.InverseTransformPoint(viewer.position);
            if (_needsSort || (fromHere - _sortedFrom).sqrMagnitude > ResortDistance * ResortDistance)
            {
                HologramFrame.SortBackToFront(_positions, _header.Count, _renderer.Transform.InverseTransformPoint(viewer.position), _keys, _buckets, _order);
                _renderer.UploadOrder(_order, _header.Count);
                _sortedFrom = fromHere;
                _needsSort = false;
            }
        }
    }

    /// <summary>Scales and places y-down splats so they stand on the floor at a fixed height.</summary>
    static class SplatFit
    {
        public static void Apply(Transform splats, HologramFit fit, float height)
        {
            var scale = height / Mathf.Max(fit.Height, 0.05f);
            splats.localScale = new Vector3(scale, -scale, scale); // the source frame is y-down
            splats.localPosition = new Vector3(-scale * fit.CentreX, scale * fit.Bottom, -scale * fit.CentreZ);
            splats.localRotation = Quaternion.identity;
        }
    }
}
