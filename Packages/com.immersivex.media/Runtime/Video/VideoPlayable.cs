using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Video with Unity's VideoPlayer (MP4 H.264/H.265, WebM; files or HTTPS). Flat video is an upright screen standing in
    /// the room; 360° and 180° video (mono, top-bottom or side-by-side stereo) is drawn on a sphere around the viewer.
    /// See <see cref="Projection.Classify"/> for how the kind and layout are worked out.
    /// </summary>
    sealed class VideoPlayable : IMediaPlayable
    {
        const float PrepareTimeoutSeconds = 30f;

        readonly MediaFormat _requested;
        MediaContext _context;
        VideoPlayer _player;
        RenderTexture _texture;
        GameObject _surface;
        Material _material;
        Mesh _mesh;
        float _coverage;
        VideoLayout _layout;
        bool _wantPlaying;
        double _lastTime;
        float _stuckFor;
        Bounds _bounds;
        bool _facingSet;

        public VideoPlayable(MediaFormat requested) => _requested = requested;

        public string Description => _player == null || !_player.isPrepared
            ? "Video"
            : $"{(_coverage > 0f ? $"{_coverage:0}° video" : "Video")} · {_player.width}×{_player.height}" +
              $"{(_layout != VideoLayout.Mono ? $" · {_layout} stereo" : string.Empty)} · {_player.frameRate:0.#} fps";

        public MediaPresentation Presentation => _coverage > 0f ? MediaPresentation.Surround : MediaPresentation.Screen;
        public bool IsLoaded => _surface != null;
        public string Error { get; private set; }
        public double Duration => _player != null && _player.isPrepared ? _player.length : 0d;
        public double Position => _player != null ? _player.time : 0d;
        public bool IsBuffering => _wantPlaying && (!_player.isPlaying || _stuckFor > 0.5f);
        public float BufferProgress => 0.5f;
        public bool HasAudio => _player != null && _player.audioTrackCount > 0;
        public Bounds Bounds => _bounds;

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            _player = context.Host.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.Url;
            _player.url = context.Url;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.Direct;
            _player.controlledAudioTrackCount = 1;
            _player.EnableAudioTrack(0, true);
            _player.isLooping = context.Loop;
            _player.skipOnDrop = true;
            _player.waitForFirstFrame = true;
            _player.errorReceived += (_, message) => Error = $"The video couldn't play ({message.TrimEnd('.')}). " +
                "H.264 MP4 plays everywhere; WebM must be VP8; HEVC must be tagged hvc1 for the Mac editor (ffmpeg -tag:v hvc1).";
            _player.Prepare();

            var waited = 0f;
            while (!_player.isPrepared && Error == null && waited < PrepareTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (Error != null)
                yield break;
            if (!_player.isPrepared)
            {
                Error = "The video didn't load in time.";
                yield break;
            }

            var width = (int)_player.width;
            var height = (int)_player.height;
            if (width <= 0 || height <= 0)
            {
                Error = "The file has no video track.";
                yield break;
            }

            (_coverage, _layout) = Projection.Classify(_requested, context.Stereo, MediaSource.FileName(context.Url), width, height);
            _texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Video" };
            _texture.Create();
            _player.targetTexture = _texture;

            if (_coverage > 0f)
                BuildSphere();
            else
                BuildScreen(width, height);

            // Show the first frame while paused: play silently until a frame arrives, then stop at the start.
            _player.SetDirectAudioVolume(0, 0f);
            _player.Play();
            waited = 0f;
            while (_player.frame < 1 && waited < 3f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!_wantPlaying)
            {
                _player.Pause();
                _player.time = 0d;
            }
        }

        void BuildScreen(int width, int height)
        {
            _material = RuntimeMaterials.Create(Color.white);
            _material.mainTexture = _texture;
            _surface = Projection.BuildScreen(_context.Root, _material, width, height, _context.ScreenHeight, out _mesh, out _bounds);
        }

        void BuildSphere() => _surface = Projection.BuildSphere(_texture, _coverage, _layout, out _mesh, out _material, out _bounds);

        public void Play()
        {
            _wantPlaying = true;
            _stuckFor = 0f;
            _player?.Play();
        }

        public void Pause()
        {
            _wantPlaying = false;
            _player?.Pause();
        }

        public void Seek(double seconds)
        {
            if (_player != null && _player.isPrepared)
                _player.time = seconds;
        }

        public void SetVolume(float volume)
        {
            if (_player != null)
                _player.SetDirectAudioVolume(0, Mathf.Clamp01(volume));
        }

        public void Tick(float deltaTime)
        {
            if (_player == null || !_wantPlaying)
                return;
            // A playing video whose time stops moving is waiting for data.
            var time = _player.time;
            _stuckFor = Mathf.Abs((float)(time - _lastTime)) < 1e-4 ? _stuckFor + deltaTime : 0f;
            _lastTime = time;
        }

        public void LateTick(Transform viewer)
        {
            if (_coverage > 0f)
                _facingSet = Projection.FollowViewer(_surface, viewer, _facingSet); // "forward" is where they faced when it opened
        }

        public void Dispose()
        {
            if (_player != null)
            {
                _player.Stop();
                Object.Destroy(_player);
            }

            if (_surface != null)
            {
                _surface.SetActive(false); // stop drawing before the texture is released (Destroy waits for the frame's end)
                Object.Destroy(_surface);
            }

            if (_texture != null)
            {
                _texture.Release();
                Object.Destroy(_texture);
            }

            if (_material != null)
                Object.Destroy(_material);
            if (_mesh != null)
                Object.Destroy(_mesh);
        }
    }
}
