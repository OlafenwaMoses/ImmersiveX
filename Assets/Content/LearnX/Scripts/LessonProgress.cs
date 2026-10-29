namespace LearnX
{
    /// <summary>
    /// Where the learner is in a lesson: one of its pages, or, for a lesson with immersive media, the step where they
    /// watch it in the room (a lesson with media has no pages, so it starts there); then its check, and the option they
    /// chose there. The lesson panel shows it; the tests drive
    /// it directly.
    /// </summary>
    public sealed class LessonProgress
    {
        public LessonProgress(Lesson lesson) => Lesson = lesson;

        public Lesson Lesson { get; }

        /// <summary>The step showing, from 0: the pages, then the watch step (if any), then the check (if any).</summary>
        public int Page { get; private set; }

        public int PageCount => Lesson.pages.Length;
        public bool HasMedia => Lesson.HasMedia;
        public bool HasCheck => Lesson.check != null && Lesson.check.options.Length > 0;
        public bool OnWatch => HasMedia && Page == PageCount;
        public bool OnCheck => HasCheck && Page == PageCount + (HasMedia ? 1 : 0);
        int LastStep => PageCount - 1 + (HasMedia ? 1 : 0) + (HasCheck ? 1 : 0);

        /// <summary>The option chosen at the check, or −1.</summary>
        public int Chosen { get; private set; } = -1;

        public bool Answered => Chosen >= 0;
        public bool Correct => Answered && Chosen == Lesson.check.answer;
        public bool CanGoBack => Page > 0;
        public bool CanGoOn => Page < LastStep;

        public bool Next()
        {
            if (!CanGoOn)
                return false;
            Page++;
            return true;
        }

        public bool Previous()
        {
            if (!CanGoBack)
                return false;
            Page--;
            Chosen = -1;
            return true;
        }

        /// <summary>Choose an option at the check: once, until <see cref="TryAgain"/>.</summary>
        public bool Choose(int option)
        {
            if (!OnCheck || Answered || option < 0 || option >= Lesson.check.options.Length)
                return false;
            Chosen = option;
            return true;
        }

        public void TryAgain() => Chosen = -1;
    }
}
