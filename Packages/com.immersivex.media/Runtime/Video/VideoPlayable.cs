using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace ImmersiveX.Media
{
    /// <summary>Stereo arrangement of a video frame.</summary>
    public enum VideoLayout
    {
        Mono,
        TopBottom,
        SideBySide,
    }

    /// <summary>
    /// Video with Unity's VideoPlayer (MP4 H.264/H.265, WebM; files or HTTPS). Flat video is an upright screen standing in
    /// the room; 360° and 180° video (mono, top-bottom or side-by-side stereo) is drawn on a sphere around the viewer.
    /// The kind is taken from the requested format, else from hints in the file name (360, 180, _tb/_ou, _sbs/_lr), else
    /// from the shape (2:1 is a 360° panorama).
    /// </summary>
    sealed class VideoPlayable : IMediaPlayable
    {
        const float ScreenBottom = 0.8f;
        const float SphereRadius = 20f;
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
            _player.errorReceived += (_, message) => Error = $"The video couldn't play ({message}).";
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

            (_coverage, _layout) = Classify(_requested, MediaSource.FileName(context.Url), width, height);
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

        /// <summary>
        /// 360°/180° and stereo layout from the requested format, then name hints, then shape. Returns coverage 0 for flat.
        /// </summary>
        public static (float coverage, VideoLayout layout) Classify(MediaFormat requested, string fileName, int width, int height)
        {
            var name = fileName.ToLowerInvariant();
            var layout = Has(name, "_tb", "-tb", "_ou", "-ou", "topbottom", "top_bottom", "overunder", "_3dv") ? VideoLayout.TopBottom
                : Has(name, "_sbs", "-sbs", "_lr", "-lr", "sidebyside", "side_by_side", "_3dh") ? VideoLayout.SideBySide
                : VideoLayout.Mono;

            float coverage;
            if (requested == MediaFormat.Video360)
                coverage = 360f;
            else if (requested == MediaFormat.Video180)
                coverage = 180f;
            else if (requested == MediaFormat.Video)
                coverage = 0f;
            else if (Has(name, "180"))
                coverage = 180f;
            else if (Has(name, "360", "equirect", "vr_"))
                coverage = 360f;
            else
            {
                // A mono 360° panorama is 2:1; top-bottom stereo 360° is 1:1; side-by-side 180° is 2:1.
                var aspect = width / (float)height;
                var eyeAspect = layout == VideoLayout.TopBottom ? aspect * 2f : layout == VideoLayout.SideBySide ? aspect / 2f : aspect;
                coverage = Mathf.Abs(eyeAspect - 2f) < 0.02f ? 360f : layout == VideoLayout.SideBySide && Mathf.Abs(eyeAspect - 1f) < 0.02f ? 180f : 0f;
            }

            if (coverage <= 0f && requested != MediaFormat.Video360 && requested != MediaFormat.Video180)
                layout = VideoLayout.Mono; // flat stereo 3D isn't supported: show the whole frame
            return (coverage, layout);
        }

        static bool Has(string name, params string[] hints)
        {
            foreach (var hint in hints)
                if (name.Contains(hint))
                    return true;
            return false;
        }

        void BuildScreen(int width, int height)
        {
            var screenHeight = _context.ScreenHeight;
            var screenWidth = screenHeight * width / height;
            float left = -screenWidth * 0.5f, right = screenWidth * 0.5f, bottom = ScreenBottom, top = ScreenBottom + screenHeight;
            _mesh = new Mesh { name = "Video Screen" };
            _mesh.vertices = new[] { new Vector3(left, bottom, 0f), new Vector3(right, bottom, 0f), new Vector3(left, top, 0f), new Vector3(right, top, 0f) };
            _mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; // front faces the user (on the −Z side)
            _mesh.RecalculateBounds();

            _material = RuntimeMaterials.Create(Color.white);
            _material.mainTexture = _texture;
            _surface = new GameObject("Video Screen", typeof(MeshFilter), typeof(MeshRenderer));
            _surface.transform.SetParent(_context.Root, false);
            _surface.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bounds = new Bounds(new Vector3(0f, bottom + screenHeight * 0.5f, 0f), new Vector3(screenWidth, screenHeight, 0.05f));
        }

        void BuildSphere()
        {
            _mesh = Sphere(64, 32);
            _material = new Material(MediaAssets.PanoramaShader) { name = "Panorama (instance)" };
            _material.mainTexture = _texture;
            _material.SetFloat("_Layout", (int)_layout);
            _material.SetFloat("_Coverage", _coverage);
            _surface = new GameObject("Video Sphere", typeof(MeshFilter), typeof(MeshRenderer));
            _surface.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _surface.transform.localScale = Vector3.one * SphereRadius;
            _bounds = new Bounds(Vector3.zero, Vector3.one * SphereRadius * 2f);
        }

        /// <summary>A unit UV sphere (positions only; the shader works out the video lookup).</summary>
        static Mesh Sphere(int longitudes, int latitudes)
        {
            var vertices = new Vector3[(longitudes + 1) * (latitudes + 1)];
            for (int lat = 0, v = 0; lat <= latitudes; lat++)
            {
                var theta = Mathf.PI * lat / latitudes;
                for (var lon = 0; lon <= longitudes; lon++, v++)
                {
                    var phi = 2f * Mathf.PI * lon / longitudes;
                    vertices[v] = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
                }
            }

            var triangles = new int[longitudes * latitudes * 6];
            for (int lat = 0, t = 0; lat < latitudes; lat++)
            for (var lon = 0; lon < longitudes; lon++, t += 6)
            {
                var a = lat * (longitudes + 1) + lon;
                var b = a + longitudes + 1;
                triangles[t] = a;
                triangles[t + 1] = b;
                triangles[t + 2] = a + 1;
                triangles[t + 3] = a + 1;
                triangles[t + 4] = b;
                triangles[t + 5] = b + 1;
            }

            var mesh = new Mesh { name = "Panorama Sphere", vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

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
            if (_coverage <= 0f || _surface == null || viewer == null)
                return;
            // The sphere stays centred on the viewer's head; "forward" in the video is where they faced when it opened.
            _surface.transform.position = viewer.position;
            if (!_facingSet)
            {
                var forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up);
                _surface.transform.rotation = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
                _facingSet = true;
            }
        }

        public void Dispose()
        {
            if (_player != null)
            {
                _player.Stop();
                Object.Destroy(_player);
            }

            if (_surface != null)
                Object.Destroy(_surface);
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
