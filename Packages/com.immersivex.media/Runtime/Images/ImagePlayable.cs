using System.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A photo (JPG or PNG). A flat photo is an upright screen standing in the room (a PNG's transparency is kept); a
    /// 360° or 180° photo, mono or stereo, surrounds the viewer. It's a still: there's nothing to play. See
    /// <see cref="Projection.Classify"/> for how the kind and layout are worked out.
    /// </summary>
    sealed class ImagePlayable : IMediaPlayable
    {
        readonly MediaFormat _requested;
        MediaContext _context;
        Texture2D _texture;
        GameObject _surface;
        Material _material;
        Mesh _mesh;
        float _coverage;
        VideoLayout _layout;
        Bounds _bounds;
        bool _facingSet;

        public ImagePlayable(MediaFormat requested) => _requested = requested;

        public string Description => _texture == null
            ? "Photo"
            : $"{(_coverage > 0f ? $"{_coverage:0}° photo" : "Photo")} · {_texture.width}×{_texture.height}" +
              (_layout != VideoLayout.Mono ? $" · {_layout} stereo" : string.Empty);

        public MediaPresentation Presentation => _coverage > 0f ? MediaPresentation.Surround : MediaPresentation.Screen;
        public bool IsLoaded => _surface != null;
        public string Error { get; private set; }
        public double Duration => 0d;
        public double Position => 0d;
        public bool IsBuffering => false;
        public float BufferProgress => 1f;
        public bool HasAudio => false;
        public Bounds Bounds => _bounds;

        public IEnumerator Load(MediaContext context)
        {
            _context = context;
            string error = null;
            yield return MediaSource.FetchTexture(context.Url, (texture, e) =>
            {
                _texture = texture;
                error = e;
            }, mipmaps: true);
            if (_texture == null)
            {
                Error = $"Couldn't load the photo ({error ?? "not a JPG or PNG"}).";
                yield break;
            }

            _texture.name = MediaSource.FileName(context.Url);
            _texture.wrapMode = TextureWrapMode.Clamp;
            _texture.filterMode = FilterMode.Trilinear;
            _texture.anisoLevel = 4;
            (_coverage, _layout) = Projection.Classify(_requested, context.Stereo, _texture.name, _texture.width, _texture.height);

            if (_coverage > 0f)
            {
                _surface = Projection.BuildSphere(_texture, _coverage, _layout, out _mesh, out _material, out _bounds);
                yield break;
            }

            var transparent = GraphicsFormatUtility.HasAlphaChannel(_texture.graphicsFormat) && MediaAssets.PhotoTransparentMaterial != null;
            _material = transparent ? new Material(MediaAssets.PhotoTransparentMaterial) { name = "Photo (transparent)" } : RuntimeMaterials.Create(Color.white);
            _material.mainTexture = _texture;
            _surface = Projection.BuildScreen(context.Root, _material, _texture.width, _texture.height, context.ScreenHeight, out _mesh, out _bounds);
        }

        public void Play() { }
        public void Pause() { }
        public void Seek(double seconds) { }
        public void SetVolume(float volume) { }
        public void Tick(float deltaTime) { }

        public void LateTick(Transform viewer)
        {
            if (_coverage > 0f)
                _facingSet = Projection.FollowViewer(_surface, viewer, _facingSet);
        }

        public void Dispose()
        {
            if (_surface != null)
                Object.Destroy(_surface);
            if (_material != null)
                Object.Destroy(_material);
            if (_mesh != null)
                Object.Destroy(_mesh);
            if (_texture != null)
                Object.Destroy(_texture);
        }
    }
}
