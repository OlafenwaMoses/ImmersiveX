using System.Collections;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A still Gaussian-splat scene (<c>.ply .splat .spz .ksplat</c>) or point cloud (<c>.ply</c>), fitted to the media's
    /// height and standing on the floor. Big files keep their most visible splats (see
    /// <see cref="MediaContext.MaxGaussians"/>). Decoding and sorting run on worker threads.
    /// </summary>
    sealed class SplatCloudPlayable : IMediaPlayable
    {
        const float ResortDistance = 0.05f;

        readonly MediaDetection _detection;
        MediaContext _context;
        HologramRenderer _renderer;
        SplatSorter _sorter;
        PackedSplats _packed;
        Vector3 _sortedFrom = new Vector3(float.MaxValue, 0f, 0f);
        Bounds _bounds;

        public SplatCloudPlayable(MediaDetection detection) => _detection = detection;

        public string Description
        {
            get
            {
                var what = _detection.Kind == MediaKind.PointCloud ? "Point cloud" : "Gaussian splats";
                if (_packed == null)
                    return $"{what} ({_detection.Extension})";
                var count = _packed.Header.Count;
                var of = _packed.SourceCount > count ? $" of {_packed.SourceCount:N0}" : string.Empty;
                return $"{what} · {count:N0}{of} ({_detection.Extension})";
            }
        }

        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => _renderer != null;
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
            var bytes = context.Prefetched;
            if (bytes == null)
                yield return MediaSource.Fetch(context.Url, (b, e) =>
                {
                    bytes = b;
                    Error = e;
                });
            if (Error != null)
                yield break;

            var extension = _detection.Extension;
            var task = SplatPacker.PackAsync(() => SplatDecoders.Decode(bytes, extension), context.Up, context.MaxGaussians);
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
            {
                Error = $"Couldn't read '{MediaSource.FileName(context.Url)}': {task.Exception?.GetBaseException().Message}";
                yield break;
            }

            _packed = task.Result;
            var header = _packed.Header;
            if (header.Count == 0)
            {
                Error = $"'{MediaSource.FileName(context.Url)}' has no splats.";
                yield break;
            }

            _renderer = new HologramRenderer(context.Root, MediaAssets.SplatMaterial, header.Count, _packed.Data.Length, _packed.ScaleRange);
            _renderer.UploadFrame(_packed.Data, _packed.Data.Length, header, HologramFrame.AlphaStart(header, SplatPacker.TextureWidth));

            var fit = HologramFrame.Fit(_packed.Positions, header.Count, header.Min.y, header.Max.y, new int[256]);
            SplatFit.Apply(_renderer.Transform, fit, context.Height);
            var scale = context.Height / Mathf.Max(fit.Height, 0.05f);
            var size = (header.Max - header.Min) * scale;
            _bounds = new Bounds(new Vector3(0f, context.Height * 0.5f, 0f), new Vector3(Mathf.Min(size.x, 4f), context.Height, Mathf.Min(size.z, 4f)));

            _sorter = new SplatSorter(_packed.Positions, header.Count);
        }

        public void Play() { }
        public void Pause() { }
        public void Seek(double seconds) { }
        public void SetVolume(float volume) { }
        public void Tick(float deltaTime) { }

        public void LateTick(Transform viewer)
        {
            if (_sorter == null || viewer == null)
                return;
            if (_sorter.TryFinish(out var order))
                _renderer.UploadOrder(order, _packed.Header.Count);

            var fromHere = _context.Root.InverseTransformPoint(viewer.position);
            if (!_sorter.Busy && (fromHere - _sortedFrom).sqrMagnitude > ResortDistance * ResortDistance)
            {
                _sorter.Start(_renderer.Transform.InverseTransformPoint(viewer.position));
                _sortedFrom = fromHere;
            }
        }

        public void Dispose() => _renderer?.Dispose();
    }
}
