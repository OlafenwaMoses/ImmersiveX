using System.Collections.Generic;
using System.Linq;
using ImmersiveX;
using UnityEngine;
using UnityEngine.UI;

namespace LearnX
{
    /// <summary>
    /// LearnX's views on its page in ImmersiveX's content menu (<see cref="ContentAppPage"/>): the menu's header with the
    /// view's title, and its footer with the view's back button (<b>Content</b> on the classes, back to the menu's list).
    /// Between them, one of 3 views:
    /// <list type="bullet">
    /// <item><b>Classes</b>: Nursery and Years 1 to 12, a row for each stage (early years, key stages 1 to 4, sixth form).</item>
    /// <item><b>A class</b>: its lessons as rows, 5 at a time (<b>‹</b> and <b>›</b> page through more), with <b>All classes</b>
    /// at the bottom.</item>
    /// <item><b>A lesson</b>: its pages one at a time (<b>Previous</b>, <b>Next</b>), or, for a lesson with immersive media,
    /// the media itself: it plays in the room as soon as the lesson opens (<see cref="MediaStage"/>), and the menu comes
    /// back on this page at its end with <b>Watch again</b>; then <b>Check what you know</b>: a
    /// question with 2 to 4 options. Choosing one says whether it's right; <b>Try again</b> clears a wrong answer,
    /// <b>Finish</b> goes back to the class, and <b>All lessons</b> at the bottom goes back at any time. The part of the
    /// curriculum it teaches is above the buttons, for teachers and parents.</item>
    /// </list>
    /// Each view is drawn afresh when it's shown.
    /// </summary>
    public sealed class LearnPages
    {
        const float Inset = 16f;
        public const int LessonsPerPage = 5;
        const float RowHeight = 62f;
        const float RowGap = 8f;

        const float BodyTop = ContentAppPage.BodyTop;
        const float BodyBottom = ContentAppPage.BodyBottom;
        const float Inner = ContentAppPage.Width - 2f * Inset;

        public enum View
        {
            Classes,
            Class,
            Lesson,
        }

        readonly IReadOnlyList<SchoolClass> _classes;
        readonly MediaStage _stage;
        ContentAppPage _page;
        bool _watching;

        /// <summary>The texts that must fit their boxes, checked by <see cref="Overflowing"/>.</summary>
        readonly List<Text> _fitted = new List<Text>();

        public LearnPages(IReadOnlyList<SchoolClass> classes, MediaStage stage)
        {
            _classes = classes;
            _stage = stage;
        }

        public View Showing { get; private set; }

        /// <summary>The class open (or whose lesson is open), or null.</summary>
        public SchoolClass Class { get; private set; }

        /// <summary>The lesson open and where the learner is in it, or null.</summary>
        public LessonProgress Progress { get; private set; }

        /// <summary>Which page of the class's lessons shows, from 0.</summary>
        public int ListPage { get; private set; }

        public int ListPages => Class == null ? 0 : Mathf.Max(1, Mathf.CeilToInt(Class.lessons.Length / (float)LessonsPerPage));

        /// <summary>The menu's page LearnX draws on, once the menu has turned to LearnX.</summary>
        public ContentAppPage Page => _page;

        /// <summary>True while the menu is open on LearnX's page.</summary>
        public bool IsOpen => _page != null && _page.IsOpen;

        /// <summary>The view showing (automation finds its buttons there).</summary>
        public RectTransform ViewShowing => _page?.View;

        /// <summary>Draw on <paramref name="page"/> from now on, starting with the classes.</summary>
        public void Attach(ContentAppPage page)
        {
            _page = page;
            ShowClasses();
        }

        /// <summary>Bring the menu to the learner on LearnX's page.</summary>
        public void Open() => _page?.Open();

        /// <summary>Put the menu away.</summary>
        public void Close() => _page?.Close();

        public void ShowClasses()
        {
            LeaveLesson();
            Showing = View.Classes;
            Class = null;
            Progress = null;
            var view = NewView("Choose your class", "LEARNX");
            if (view == null)
                return;
            const float pitch = 69f, columns = 4f, gap = 8f;
            var width = (Inner - (columns - 1f) * gap) / columns;
            var rows = Catalogue.ByStage(_classes).ToList();
            for (var row = 0; row < rows.Count; row++)
            {
                var top = BodyTop - 4f - row * pitch;
                Fit(LearnUi.Label(view, "Stage", rows[row].stage, 14, new Vector2(0f, top - 9f), new Vector2(Inner, 18f), Brand.Ink3));
                for (var i = 0; i < rows[row].classes.Length; i++)
                {
                    var schoolClass = rows[row].classes[i];
                    Brand.Pill(view, schoolClass.name, new Vector2(-Inner / 2f + width / 2f + i * (width + gap), top - 44f), new Vector2(width, 44f), false,
                        () => ShowClass(schoolClass), 18);
                }
            }
        }

        public void ShowClass(SchoolClass schoolClass, int page = 0)
        {
            LeaveLesson();
            Showing = View.Class;
            Class = schoolClass;
            Progress = null;
            ListPage = Mathf.Clamp(page, 0, ListPages - 1);
            var view = NewView(schoolClass.name, $"{schoolClass.stage} · ages {schoolClass.ages}", "All classes", ShowClasses);
            if (view == null)
                return;
            var top = BodyTop - 4f;
            if (!string.IsNullOrWhiteSpace(schoolClass.note))
            {
                Fit(LearnUi.Label(view, "Note", schoolClass.note, 16, new Vector2(0f, top - 12f), new Vector2(Inner, 24f), Brand.GoldLight));
                top -= 32f;
            }

            var lessons = schoolClass.lessons.Skip(ListPage * LessonsPerPage).Take(LessonsPerPage).ToArray();
            for (var i = 0; i < lessons.Length; i++)
            {
                var lesson = lessons[i];
                var card = LearnUi.Card(view, lesson.id, lesson.subject, lesson.title, new Vector2(0f, top - RowHeight / 2f - i * (RowHeight + RowGap)),
                    new Vector2(Inner, RowHeight), () => ShowLesson(lesson));
                foreach (var text in card.GetComponentsInChildren<Text>().Where(t => !t.resizeTextForBestFit))
                    Fit(text); // a long title shrinks to fit instead
            }

            if (ListPages > 1)
            {
                var y = BodyBottom + 26f;
                if (ListPage > 0)
                    Brand.Pill(view, "‹", new Vector2(-Inner / 2f + 24f, y), new Vector2(48f, 44f), false, () => ShowClass(schoolClass, ListPage - 1), 22);
                LearnUi.Label(view, "Page", $"{ListPage + 1} of {ListPages}", 15, new Vector2(0f, y), new Vector2(120f, 44f), Brand.Ink3,
                    alignment: TextAnchor.MiddleCenter);
                if (ListPage < ListPages - 1)
                    Brand.Pill(view, "›", new Vector2(Inner / 2f - 24f, y), new Vector2(48f, 44f), false, () => ShowClass(schoolClass, ListPage + 1), 22);
            }
        }

        /// <summary>
        /// Open a lesson. A lesson with media is the media, so it plays in the room straight away (unless
        /// <paramref name="play"/> is false: the automation's tour only looks at the views).
        /// </summary>
        public void ShowLesson(Lesson lesson, bool play = true)
        {
            Progress = new LessonProgress(lesson);
            ShowProgress();
            if (play && Progress.OnWatch)
                Watch(lesson);
        }

        public void Next()
        {
            if (Progress != null && Progress.Next())
                ShowProgress();
        }

        public void Previous()
        {
            if (Progress != null && Progress.Previous())
                ShowProgress();
        }

        public void Choose(int option)
        {
            if (Progress != null && Progress.Choose(option))
                ShowProgress();
        }

        public void TryAgain()
        {
            if (Progress == null)
                return;
            Progress.TryAgain();
            ShowProgress();
        }

        /// <summary>The lesson where the learner is in it: a page, or the check.</summary>
        void ShowProgress()
        {
            Showing = View.Lesson;
            var lesson = Progress.Lesson;
            var page = ListPageOf(lesson);
            var view = NewView(lesson.title, Class != null ? $"{Class.name} · {lesson.subject}" : lesson.subject, "All lessons", () => ShowClass(Class, page));
            if (view == null)
                return;
            var stage = Class?.stage;
            var navigation = BodyBottom + 28f;
            if (Progress.OnCheck)
                BuildCheck(view, lesson.check, stage, navigation);
            else if (Progress.OnWatch)
                BuildWatch(view, lesson, stage, navigation);
            else
            {
                Fit(LearnUi.Label(view, "Page text", lesson.pages[Progress.Page], LearnUi.ReadingSize(stage), new Vector2(0f, BodyTop - 8f - 161f),
                    new Vector2(Inner, 322f), Brand.Ink));
                LearnUi.Label(view, "Page", $"Page {Progress.Page + 1} of {Progress.PageCount}", 15, new Vector2(-20f, navigation), new Vector2(96f, 48f),
                    Brand.Ink3, alignment: TextAnchor.MiddleCenter);
                var last = Progress.Page == Progress.PageCount - 1 && !Progress.HasMedia;   // with media, the watch step comes next
                if (Progress.CanGoOn)
                    Fit(LearnUi.Key(view, last ? "Check what you know" : "Next", new Vector2(Inner / 2f - 85f, navigation), new Vector2(170f, 48f),
                        LearnUi.KeyStyle.Gold, Next, last ? 15 : 17).GetComponentInChildren<Text>());   // one line
            }

            if (Progress.CanGoBack)
                LearnUi.Key(view, "Previous", new Vector2(-Inner / 2f + 68f, navigation), new Vector2(136f, 48f), LearnUi.KeyStyle.Plain, Previous, 17);
            Fit(LearnUi.Label(view, "Curriculum", $"Curriculum: {lesson.curriculum}", 12, new Vector2(0f, navigation + 43f), new Vector2(Inner, 30f), Brand.Ink3));
        }

        /// <summary>
        /// The watch step: what to do while the media plays, and <b>Watch</b> (<b>Watch again</b> once it has finished),
        /// which plays it in the room and puts the menu away so it doesn't stand in front of it. The menu comes back on
        /// this page by itself at the end (<see cref="CheckMediaFinished"/>).
        /// </summary>
        void BuildWatch(Transform view, Lesson lesson, string stage, float navigation)
        {
            Fit(LearnUi.Label(view, "Watch text", lesson.media.text, LearnUi.ReadingSize(stage), new Vector2(0f, BodyTop - 8f - 90f), new Vector2(Inner, 180f), Brand.Ink));
            var finished = _stage != null && _stage.Lesson == lesson && _stage.HasFinished;
            Fit(LearnUi.Key(view, finished ? "Watch again" : "Watch", new Vector2(0f, BodyTop - 230f), new Vector2(240f, 64f), LearnUi.KeyStyle.Gold, () => Watch(lesson), 24)
                .GetComponentInChildren<Text>());
            if (finished)
                Fit(LearnUi.Label(view, "Watched", "Well done. Watch it again, or go on.", 16, new Vector2(0f, BodyTop - 290f), new Vector2(Inner, 26f), LearnUi.Right,
                    alignment: TextAnchor.MiddleCenter));
            if (Progress.CanGoOn)
                Fit(LearnUi.Key(view, Progress.HasCheck ? "Check what you know" : "Next", new Vector2(Inner / 2f - 85f, navigation), new Vector2(170f, 48f),
                    LearnUi.KeyStyle.Plain, Next, Progress.HasCheck ? 15 : 17).GetComponentInChildren<Text>());
        }

        /// <summary>Play the lesson's media in the room and put the menu away while it plays.</summary>
        public void Watch(Lesson lesson)
        {
            if (_stage == null)
                return;
            _stage.Play(lesson);
            _watching = true;
            Close();
        }

        /// <summary>
        /// Called every frame by LearnX: once the media has played to the end, the menu comes back to the learner on the
        /// watch step.
        /// </summary>
        public void CheckMediaFinished()
        {
            if (!_watching || _stage == null || !_stage.HasFinished)
                return;
            _watching = false;
            if (Progress == null || !Progress.OnWatch)
                return;
            Open();
            ShowProgress();
        }

        /// <summary>LearnX is hidden (the menu shows other content): no waiting for its media to end.</summary>
        public void StopWatching() => _watching = false;

        void LeaveLesson()
        {
            _watching = false;
            _stage?.Stop();
        }

        void BuildCheck(Transform view, Check check, string stage, float navigation)
        {
            // the question (up to 3 lines), up to 4 options, then whether the answer is right, above the buttons
            var size = Mathf.Min(LearnUi.ReadingSize(stage), 24);
            Fit(LearnUi.Label(view, "Question", check.question, size, new Vector2(0f, BodyTop - 6f - 46f), new Vector2(Inner, 92f), Brand.Ink,
                FontStyle.Bold, TextAnchor.MiddleCenter));
            for (var i = 0; i < check.options.Length; i++)
            {
                var option = i;
                // a wrong choice doesn't give the answer away: only the chosen option is marked
                var style = !Progress.Answered ? LearnUi.KeyStyle.Plain
                    : i != Progress.Chosen ? LearnUi.KeyStyle.Off
                    : Progress.Correct ? LearnUi.KeyStyle.Right : LearnUi.KeyStyle.Wrong;
                var key = LearnUi.Key(view, check.options[i], new Vector2(0f, BodyTop - 120f - i * 50f), new Vector2(Inner, 44f), style,
                    () => Choose(option), 19);
                key.interactable = !Progress.Answered;
                Fit(key.GetComponentInChildren<Text>());
            }

            if (!Progress.Answered)
                return;
            var feedback = Progress.Correct ? $"Correct. {check.explanation}"
                : Progress.HasMedia ? "Not quite. Try again, or watch it again with Previous."
                : "Not quite. Try again, or read the pages again with Previous.";
            Fit(LearnUi.Label(view, "Feedback", feedback, 16, new Vector2(0f, navigation + 78f), new Vector2(Inner, 40f),
                Progress.Correct ? LearnUi.Right : LearnUi.Wrong, alignment: TextAnchor.MiddleCenter));
            if (Progress.Correct)
                LearnUi.Key(view, "Finish", new Vector2(Inner / 2f - 68f, navigation), new Vector2(136f, 48f), LearnUi.KeyStyle.Gold,
                    () => ShowClass(Class, ListPageOf(Progress.Lesson)), 17);
            else
                LearnUi.Key(view, "Try again", new Vector2(Inner / 2f - 68f, navigation), new Vector2(136f, 48f), LearnUi.KeyStyle.Gold, TryAgain, 17);
        }

        int ListPageOf(Lesson lesson)
        {
            var index = Class != null ? System.Array.IndexOf(Class.lessons, lesson) : 0;
            return Mathf.Max(0, index) / LessonsPerPage;
        }

        /// <summary>
        /// The texts that don't fit their boxes in the view showing now, with how much they need (the automation's tour
        /// checks every lesson's view).
        /// </summary>
        public List<string> Overflowing()
        {
            Canvas.ForceUpdateCanvases();
            var problems = new List<string>();
            foreach (var text in _fitted)
            {
                if (text == null)
                    continue;
                var box = text.rectTransform.rect.size;
                var height = text.preferredHeight;
                if (height > box.y + 1f)
                    problems.Add($"{text.name} needs {height:0} mm of {box.y:0} mm: {text.text}");
            }

            return problems;
        }

        /// <summary>A fresh view on the menu's page with its header and footer, or null before LearnX has a page.</summary>
        Transform NewView(string title, string eyebrow, string back = null, UnityEngine.Events.UnityAction onBack = null)
        {
            _fitted.Clear();
            return _page?.NewView(title, eyebrow, back, onBack);
        }

        void Fit(Text text) => _fitted.Add(text);
    }
}
