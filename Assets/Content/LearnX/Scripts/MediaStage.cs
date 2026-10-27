using ImmersiveX.Media;
using UnityEngine;

namespace LearnX
{
    /// <summary>
    /// Plays a lesson's immersive media in the room through ImmersiveX's Immersive Media: standing on the floor in front of
    /// the learner at the lesson's height, with its sound, captions and cards, and the media's own controls. One lesson's
    /// media at a time; it's taken away when the learner leaves that lesson.
    /// </summary>
    public sealed class MediaStage : MonoBehaviour
    {
        ImmersiveMedia _media;
        bool _playWhenLoaded;

        public Lesson Lesson { get; private set; }
        public ImmersiveMedia Media => _media;
        public bool IsLoaded => _media != null && _media.HasTimeline;
        public bool IsPlaying => _media != null && _media.IsPlaying;

        /// <summary>It played to the end (Immersive Media pauses itself there).</summary>
        public bool HasFinished => IsLoaded && !_media.IsPlaying && _media.Position >= _media.Duration - 0.1;

        public string Status => _media == null ? "no media" : _media.DescribeStatus();

        /// <summary>Play <paramref name="lesson"/>'s media from the start, or carry on where it was paused.</summary>
        public void Play(Lesson lesson)
        {
            if (Lesson != lesson)
                Stop();
            if (_media == null)
            {
                var go = new GameObject($"Lesson media: {lesson.title}");
                go.transform.SetParent(transform, false);
                _media = go.AddComponent<ImmersiveMedia>();   // placed in front of the learner when it starts (next frame)
                _media.Source = lesson.media.file;
                _media.Title = lesson.title; // the controls' tab says what's playing
                _media.Format = MediaFormat.Mesh4D;
                _media.Height = lesson.media.height;
                Lesson = lesson;
                LearnXContent.Log($"Playing '{lesson.title}' in the room: {lesson.media.file}, {lesson.media.height:0.0} m tall.");
            }

            // Immersive Media forgets a Play asked for before it has loaded (loading starts by unloading), so a new one is
            // played as soon as it's ready; a loaded one plays now, from the start again if it has finished
            if (_media.HasTimeline)
                _media.Play();
            else
                _playWhenLoaded = true;
        }

        void Update()
        {
            if (!_playWhenLoaded || _media == null || !_media.HasTimeline)
                return;
            _playWhenLoaded = false;
            _media.Play();
        }

        public void Pause()
        {
            _playWhenLoaded = false;
            _media?.Pause();
        }

        public void Stop()
        {
            if (_media != null)
                Destroy(_media.gameObject);
            _media = null;
            _playWhenLoaded = false;
            Lesson = null;
        }
    }
}
