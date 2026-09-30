using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A frame sequence (a sequence JSON or a folder of frames): reads it, looks at the first frame, and hands over to the
    /// mesh player (OBJ, mesh PLY, STL) or the splat player (splat and point-cloud PLY, .splat, .spz, .ksplat).
    /// </summary>
    sealed class SequencePlayable : IMediaPlayable
    {
        readonly MediaDetection _detection;
        IMediaPlayable _inner;
        string _error;

        public SequencePlayable(MediaDetection detection) => _detection = detection;

        public string Description => _inner?.Description ?? "Sequence";
        public MediaPresentation Presentation => _inner?.Presentation ?? MediaPresentation.Standing;
        public bool IsLoaded => _inner != null && _inner.IsLoaded;
        public string Error => _error ?? _inner?.Error;
        public double Duration => _inner?.Duration ?? 0d;
        public double Position => _inner?.Position ?? 0d;
        public bool IsBuffering => _inner != null && _inner.IsBuffering;
        public float BufferProgress => _inner?.BufferProgress ?? 0f;
        public bool HasAudio => _inner != null && _inner.HasAudio;
        public Bounds Bounds => _inner?.Bounds ?? default;

        public IEnumerator Load(MediaContext context)
        {
            MediaSequence sequence;
            try
            {
                sequence = _detection.FolderFiles != null ? MediaSequence.FromFiles(context.Url, _detection.FolderFiles) : null;
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                yield break;
            }

            if (sequence == null)
            {
                var bytes = context.Prefetched;
                if (bytes == null)
                    yield return MediaSource.Fetch(context.Url, (b, e) =>
                    {
                        bytes = b;
                        _error = e;
                    });
                if (_error != null)
                    yield break;
                try
                {
                    sequence = MediaSequence.FromJson(Encoding.UTF8.GetString(bytes), context.Url);
                }
                catch (Exception exception)
                {
                    _error = $"Couldn't read the sequence ({exception.Message}).";
                    yield break;
                }
            }

            if (context.FrameRate > 0f)
                sequence.Fps = context.FrameRate;

            // Meshes or splats? OBJ and STL are meshes; a PLY frame says in its header; the rest are splats.
            var mesh = sequence.Extension == ".obj" || sequence.Extension == ".stl";
            byte[] first = null;
            if (sequence.Extension == ".ply")
            {
                yield return MediaSource.Fetch(sequence.Frames[0], (b, e) =>
                {
                    first = b;
                    _error = e;
                });
                if (_error != null)
                    yield break;
                try
                {
                    mesh = PlyFile.Parse(first).Content == PlyContent.Mesh;
                }
                catch (FormatException exception)
                {
                    _error = exception.Message;
                    yield break;
                }
            }

            _inner = mesh ? new MeshPlayable(sequence, sequence.Frames.Length == 1 ? first : null) : new SplatSequencePlayable(sequence);
            context.Prefetched = null;
            yield return _inner.Load(context);
        }

        public void Play() => _inner?.Play();
        public void Pause() => _inner?.Pause();
        public void Seek(double seconds) => _inner?.Seek(seconds);
        public void SetVolume(float volume) => _inner?.SetVolume(volume);
        public void Tick(float deltaTime) => _inner?.Tick(deltaTime);
        public void LateTick(Transform viewer) => _inner?.LateTick(viewer);
        public void Dispose() => _inner?.Dispose();
    }
}
