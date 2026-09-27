using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>Streams audio tracks (AAC/M4A included) with Android's own MediaPlayer on the headset.</summary>
    [Preserve]
    sealed class AndroidStreamAudioProvider : IStreamAudioProvider
    {
        public IStreamAudio Open(string url, bool loop, GameObject host) => new AndroidStreamAudio(url, loop);
    }

    /// <summary>
    /// A streamed track played by <c>android.media.MediaPlayer</c>, which decodes every format Android supports and
    /// streams over HTTPS. MediaPlayer reports back on the UI thread, so its callbacks only set flags.
    /// </summary>
    sealed class AndroidStreamAudio : IStreamAudio
    {
        const int UsageMedia = 1;
        const int ContentTypeMusic = 2;
        const int SeekClosest = 3;

        AndroidJavaObject _player;
        volatile bool _prepared;
        volatile bool _failed;

        public AndroidStreamAudio(string url, bool loop)
        {
            try
            {
                _player = new AndroidJavaObject("android.media.MediaPlayer");
                using (var builder = new AndroidJavaObject("android.media.AudioAttributes$Builder"))
                {
                    builder.Call<AndroidJavaObject>("setUsage", UsageMedia).Dispose();
                    builder.Call<AndroidJavaObject>("setContentType", ContentTypeMusic).Dispose();
                    using (var attributes = builder.Call<AndroidJavaObject>("build"))
                        _player.Call("setAudioAttributes", attributes);
                }

                _player.Call("setDataSource", url);
                _player.Call("setLooping", loop);
                _player.Call("setOnPreparedListener", new PreparedListener(this));
                _player.Call("setOnErrorListener", new ErrorListener(this));
                _player.Call("prepareAsync");
            }
            catch (Exception exception)
            {
                _failed = true;
                ImmersiveXLog.Warn($"Audio track couldn't start ({exception.Message}). The hologram plays without sound.");
            }
        }

        public bool IsReady => _prepared && !_failed;
        public bool Failed => _failed;
        public bool IsPlaying => IsReady && _player.Call<bool>("isPlaying");
        public double Time => IsReady ? _player.Call<int>("getCurrentPosition") / 1000d : 0d;

        public void Play()
        {
            if (IsReady)
                _player.Call("start");
        }

        public void Pause()
        {
            if (IsReady && _player.Call<bool>("isPlaying"))
                _player.Call("pause");
        }

        public void Seek(double seconds)
        {
            if (IsReady)
                _player.Call("seekTo", (long)(seconds * 1000d), SeekClosest);
        }

        public void SetVolume(float volume)
        {
            if (_player != null && !_failed)
                _player.Call("setVolume", Mathf.Clamp01(volume), Mathf.Clamp01(volume));
        }

        public void Dispose()
        {
            if (_player == null)
                return;
            _prepared = false;
            _player.Call("release");
            _player.Dispose();
            _player = null;
        }

        [Preserve]
        sealed class PreparedListener : AndroidJavaProxy
        {
            readonly AndroidStreamAudio _owner;

            public PreparedListener(AndroidStreamAudio owner) : base("android.media.MediaPlayer$OnPreparedListener") => _owner = owner;

            [Preserve]
            public void onPrepared(AndroidJavaObject player) => _owner._prepared = true;
        }

        [Preserve]
        sealed class ErrorListener : AndroidJavaProxy
        {
            readonly AndroidStreamAudio _owner;

            public ErrorListener(AndroidStreamAudio owner) : base("android.media.MediaPlayer$OnErrorListener") => _owner = owner;

            [Preserve]
            public bool onError(AndroidJavaObject player, int what, int extra)
            {
                _owner._failed = true;
                Debug.LogWarning($"[ImmersiveX] Audio track failed (MediaPlayer error {what}, {extra}). The hologram plays without sound.");
                return true;
            }
        }
    }
}
