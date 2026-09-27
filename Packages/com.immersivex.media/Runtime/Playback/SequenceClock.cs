using System;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Playback time for frame sequences: runs only while playing and not waiting for frames, follows the soundtrack when
    /// there is one (small drift eased out, large drift jumps), and stops or loops at the end.
    /// </summary>
    sealed class SequenceClock
    {
        readonly double _duration;
        readonly bool _loop;
        readonly IStreamAudio _audio;
        float _audioRetryAt;

        public SequenceClock(double duration, bool loop, IStreamAudio audio)
        {
            _duration = duration;
            _loop = loop;
            _audio = audio;
        }

        public double Time { get; private set; }
        public bool WantPlaying { get; private set; }

        /// <summary>Playing and not waiting for frames.</summary>
        public bool Running { get; private set; }

        public void Play() => WantPlaying = true;

        public void Pause()
        {
            WantPlaying = false;
            Halt();
        }

        public void Seek(double seconds)
        {
            Time = Math.Max(0d, Math.Min(seconds, _duration - 1e-3));
            Halt();
            _audio?.Seek(Time);
        }

        /// <summary>Called when enough frames are buffered to (re)start.</summary>
        public void Resume()
        {
            if (!WantPlaying || Running)
                return;
            Running = true;
            _audioRetryAt = 0f;
        }

        /// <summary>Called when the next frame isn't ready yet.</summary>
        public void Halt()
        {
            Running = false;
            if (_audio != null && _audio.IsPlaying)
                _audio.Pause();
        }

        public void Advance(float deltaTime)
        {
            if (!Running)
                return;
            Time += deltaTime;
            SyncAudio();
            if (Time < _duration)
                return;
            if (_loop)
            {
                Time -= _duration;
            }
            else
            {
                Time = _duration - 1e-3;
                WantPlaying = false;
                Halt();
            }
        }

        void SyncAudio()
        {
            if (_audio == null || !_audio.IsReady || _audio.Failed)
                return;
            if (!_audio.IsPlaying)
            {
                if (UnityEngine.Time.unscaledTime < _audioRetryAt)
                    return;
                _audioRetryAt = UnityEngine.Time.unscaledTime + 0.5f;
                if (Math.Abs(_audio.Time - Time) > 0.1)
                    _audio.Seek(Time);
                _audio.Play();
                return;
            }

            var drift = _audio.Time - Time;
            if (drift > _duration * 0.5)
                drift -= _duration;
            else if (drift < -_duration * 0.5)
                drift += _duration;
            Time += Math.Abs(drift) > 0.25 ? drift : drift * 0.05;
        }
    }
}
