using System;
using UnityEngine;
using UnityEngine.Video;

namespace ImmersiveX
{
    /// <summary>
    /// A streamed audio track that drives a media clock (a hologram's soundtrack, for example).
    /// Get one with <see cref="StreamAudio.Open"/>; platforms can supply their own through <see cref="IStreamAudioProvider"/>.
    /// </summary>
    public interface IStreamAudio : IDisposable
    {
        /// <summary>Prepared and ready to play.</summary>
        bool IsReady { get; }

        /// <summary>The track couldn't be loaded; playback continues silently.</summary>
        bool Failed { get; }

        bool IsPlaying { get; }

        /// <summary>Playback position in seconds.</summary>
        double Time { get; }

        void Play();
        void Pause();
        void Seek(double seconds);

        /// <summary>0 (silent) to 1.</summary>
        void SetVolume(float volume);
    }

    /// <summary>A platform's way of playing a streamed audio track, registered in <see cref="Services"/> by its adapter.</summary>
    public interface IStreamAudioProvider
    {
        IStreamAudio Open(string url, bool loop, GameObject host);
    }

    public static class StreamAudio
    {
        /// <summary>Open <paramref name="url"/> with the platform's provider, or Unity's VideoPlayer when there's none.</summary>
        public static IStreamAudio Open(string url, bool loop, GameObject host)
        {
            if (Services.TryGet<IStreamAudioProvider>(out var provider))
                return provider.Open(url, loop, host);

            if (IsAudioOnlyAac(url))
            {
                // Unity can't decode audio-only AAC itself (VideoPlayer wants a video track). Platform adapters that can
                // (Meta Quest, through Android's MediaPlayer) register an IStreamAudioProvider.
                ImmersiveXLog.Info("This platform has no AAC audio decoder registered, so the track plays silently here (it has sound on Meta Quest).");
                return new SilentAudio();
            }

            return new VideoPlayerAudio(url, loop, host);
        }

        static bool IsAudioOnlyAac(string url)
        {
            var path = url.Split('?')[0];
            return path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".aac", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>No sound: media using it keeps its own clock.</summary>
    sealed class SilentAudio : IStreamAudio
    {
        public bool IsReady => false;
        public bool Failed => true;
        public bool IsPlaying => false;
        public double Time => 0d;
        public void Play() { }
        public void Pause() { }
        public void Seek(double seconds) { }
        public void SetVolume(float volume) { }
        public void Dispose() { }
    }

    /// <summary>Plays a streamed audio track with Unity's <see cref="VideoPlayer"/>.</summary>
    sealed class VideoPlayerAudio : IStreamAudio
    {
        readonly VideoPlayer _player;

        public VideoPlayerAudio(string url, bool loop, GameObject host)
        {
            _player = host.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.Url;
            _player.url = url;
            _player.renderMode = VideoRenderMode.APIOnly;
            _player.audioOutputMode = VideoAudioOutputMode.Direct;
            _player.controlledAudioTrackCount = 1;
            _player.EnableAudioTrack(0, true);
            _player.isLooping = loop;
            _player.skipOnDrop = true;
            _player.errorReceived += (_, message) =>
            {
                Failed = true;
                ImmersiveXLog.Warn($"Audio track couldn't play ({message}). The hologram plays without sound.");
            };
            _player.prepareCompleted += _ => IsReady = true;
            _player.Prepare();
        }

        public bool IsReady { get; private set; }
        public bool Failed { get; private set; }
        public bool IsPlaying => _player != null && _player.isPlaying;
        public double Time => _player != null ? _player.time : 0d;

        public void Play()
        {
            if (_player != null && IsReady)
                _player.Play();
        }

        public void Pause()
        {
            if (_player != null && IsReady)
                _player.Pause();
        }

        public void Seek(double seconds)
        {
            if (_player != null && IsReady)
                _player.time = seconds;
        }

        public void SetVolume(float volume)
        {
            if (_player != null)
                _player.SetDirectAudioVolume(0, Mathf.Clamp01(volume));
        }

        public void Dispose()
        {
            if (_player != null)
                UnityEngine.Object.Destroy(_player);
        }
    }
}
