using System.Collections;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>Attaches the right player for a detected source.</summary>
    public static class MediaPlayables
    {
        public static IMediaPlayable Create(MediaDetection detection)
        {
            switch (detection.Kind)
            {
                case MediaKind.HologramStream: return new SplatStreamPlayable();
                case MediaKind.Splats:
                case MediaKind.PointCloud: return new SplatCloudPlayable(detection);
                case MediaKind.Model: return new ModelPlayable();
                case MediaKind.Mesh: return new MeshPlayable(MediaSequence.Single(detection.Url), detection.Bytes);
                case MediaKind.Sequence: return new SequencePlayable(detection);
                case MediaKind.Video: return new VideoPlayable(detection.Requested);
                case MediaKind.Image: return new ImagePlayable(detection.Requested);
                case MediaKind.Codec: return detection.Codec.Create(detection);
                default: return new UnavailablePlayable($"{detection.Kind} playback is coming in the next step.");
            }
        }
    }

    /// <summary>Shows a message instead of media.</summary>
    sealed class UnavailablePlayable : IMediaPlayable
    {
        public UnavailablePlayable(string reason) => Error = reason;

        public string Description => Error;
        public MediaPresentation Presentation => MediaPresentation.Standing;
        public bool IsLoaded => false;
        public string Error { get; }
        public double Duration => 0d;
        public double Position => 0d;
        public bool IsBuffering => false;
        public float BufferProgress => 0f;
        public bool HasAudio => false;
        public Bounds Bounds => default;

        public IEnumerator Load(MediaContext context)
        {
            yield break;
        }

        public void Play() { }
        public void Pause() { }
        public void Seek(double seconds) { }
        public void SetVolume(float volume) { }
        public void Tick(float deltaTime) { }
        public void LateTick(Transform viewer) { }
        public void Dispose() { }
    }
}
