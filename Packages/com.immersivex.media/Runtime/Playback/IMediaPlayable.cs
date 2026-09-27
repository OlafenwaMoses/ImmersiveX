using System;
using System.Collections;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>How a piece of media sits in the room.</summary>
    public enum MediaPresentation
    {
        /// <summary>Stands on the floor, fitted to the media's height (holograms, splats, models, mesh sequences).</summary>
        Standing,

        /// <summary>An upright screen (flat video).</summary>
        Screen,

        /// <summary>All around the viewer (360° and 180° video).</summary>
        Surround,
    }

    /// <summary>Which way is up in the media's own coordinates. Auto uses the format's usual convention.</summary>
    public enum UpAxis
    {
        Auto,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ,
    }

    /// <summary>What a player needs from <see cref="ImmersiveMedia"/>: where to draw, how big, and the user's settings.</summary>
    public sealed class MediaContext
    {
        /// <summary>The media's address: an https/file/jar URL.</summary>
        public string Url;

        public string Title;

        /// <summary>Draw under this: its origin is on the floor where the media stands, and +Z points away from the user.</summary>
        public Transform Root;

        /// <summary>The GameObject to add components (video, audio) to.</summary>
        public GameObject Host;

        /// <summary>Height of standing media in metres.</summary>
        public float Height = 1.6f;

        /// <summary>Height of a video screen in metres.</summary>
        public float ScreenHeight = 1f;

        public bool Loop;
        public string Quality = "base";
        public UpAxis Up = UpAxis.Auto;

        /// <summary>Most Gaussians or points drawn at once; larger files keep the most visible ones.</summary>
        public int MaxGaussians = 400_000;

        public int ParallelDownloads = 24;
        public float BufferSeconds = 3f;
        public float PrerollSeconds = 1f;

        /// <summary>The file's bytes when detection already downloaded them (a PLY, say), or null.</summary>
        public byte[] Prefetched;

        /// <summary>The viewer's head (camera) transform.</summary>
        public Func<Transform> Viewer;

        /// <summary>Runs a coroutine on the host (players aren't MonoBehaviours).</summary>
        public Func<IEnumerator, Coroutine> StartRoutine;
    }

    /// <summary>
    /// Loads, plays and draws one kind of media inside an <see cref="ImmersiveMedia"/>. The host does placement, grabbing,
    /// controls and the Play/Pause state; a player only has to load, draw and keep time.
    /// </summary>
    public interface IMediaPlayable : IDisposable
    {
        /// <summary>One line for logs and the panel, e.g. "Gaussian splats · 400,000 of 1,204,113".</summary>
        string Description { get; }

        MediaPresentation Presentation { get; }

        /// <summary>Loaded and drawable.</summary>
        bool IsLoaded { get; }

        /// <summary>Why it can't play, or null.</summary>
        string Error { get; }

        /// <summary>Seconds; 0 for a still (nothing to play).</summary>
        double Duration { get; }

        /// <summary>Seconds.</summary>
        double Position { get; }

        /// <summary>Asked to play but waiting for data.</summary>
        bool IsBuffering { get; }

        /// <summary>0–1 while buffering.</summary>
        float BufferProgress { get; }

        bool HasAudio { get; }

        /// <summary>The visible content's bounds in <see cref="MediaContext.Root"/> space, for grabbing.</summary>
        Bounds Bounds { get; }

        IEnumerator Load(MediaContext context);
        void Play();
        void Pause();
        void Seek(double seconds);
        void SetVolume(float volume);

        /// <summary>Every frame: stream, decode and keep time.</summary>
        void Tick(float deltaTime);

        /// <summary>After everything moved: fit to size and sort for <paramref name="viewer"/>.</summary>
        void LateTick(Transform viewer);
    }
}
