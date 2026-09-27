namespace ImmersiveX.Media
{
    /// <summary>
    /// What media controls need from a player: state to show and commands to send. <see cref="ImmersiveMedia"/>
    /// implements it for every format, so they all share <see cref="MediaControls"/>.
    /// </summary>
    public interface IMediaTransport
    {
        /// <summary>Playing, or about to once enough is buffered.</summary>
        bool IsPlaying { get; }

        bool IsBuffering { get; }

        /// <summary>0–1 while buffering.</summary>
        float BufferProgress { get; }

        /// <summary>Seconds.</summary>
        double Position { get; }

        /// <summary>Seconds; 0 until known.</summary>
        double Duration { get; }

        /// <summary>0–1, kept while muted.</summary>
        float Volume { get; }

        bool Muted { get; }

        /// <summary>A message to show instead of the time (loading, an error, or what a still is), or null.</summary>
        string Status { get; }

        /// <summary>False for stills (a splat scene or model with no animation): nothing to play or seek.</summary>
        bool HasTimeline { get; }

        /// <summary>False when there's no sound to mute or turn up.</summary>
        bool HasAudio { get; }

        void TogglePlay();
        void Seek(double seconds);
        void SetVolume(float volume);
        void SetMuted(bool muted);
    }
}
