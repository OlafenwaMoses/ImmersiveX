using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ImmersiveX;
using ImmersiveX.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LearnX.Editor
{
    /// <summary>
    /// Automation commands for LearnX, an app in ImmersiveX's content menu (see <see cref="EditorAutomation"/>):
    /// <list type="bullet">
    /// <item>Edit mode: <c>learnx add</c> (ImmersiveX ▸ Content ▸ Add LearnX: LearnX in the open scene's content)</item>
    /// <item>Either mode: <c>learnx check</c> (every lesson against GOV.UK's style and its stage's reading level)</item>
    /// <item>Play mode: <c>learnx status</c> · <c>learnx open|close</c> (the content menu on LearnX's page) · <c>learnx class &lt;name|id&gt;</c> ·
    /// <c>learnx lesson &lt;n|id&gt;</c> (of the class showing) · <c>learnx next|previous</c> · <c>learnx answer &lt;n&gt;</c>
    /// (from 1) · <c>learnx again</c> · <c>learnx home</c> · <c>learnx watch</c> (a lesson's media, in the room) ·
    /// <c>learnx media [seek &lt;s&gt;|end]</c> · <c>learnx fit</c> (texts that don't fit the view showing) ·
    /// <c>learnx tour</c> (every class, lesson, page and check: what doesn't fit, and every right answer marked right)</item>
    /// </list>
    /// ImmersiveX's <c>host press &lt;label&gt;</c> presses the page's buttons as a ray or finger does, and <c>contentmenu …</c>
    /// drives the menu around it.
    /// </summary>
    [InitializeOnLoad]
    static class LearnXAutomation
    {
        static LearnXAutomation() => EditorAutomation.RegisterCommand("learnx", Run);

        static void Run(string command, string argument)
        {
            var parts = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            var rest = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty;
            if (verb == "check")
            {
                var problems = ContentStyle.Problems(Catalogue.Classes);
                var lessons = Catalogue.Classes.Sum(c => c.lessons.Length);
                EditorAutomation.Report(command, problems.Count == 0 ? "ok" : "error",
                    problems.Count == 0 ? $"{Catalogue.Classes.Count} classes and {lessons} lessons follow the style."
                        : $"{problems.Count} problem(s):\n" + string.Join("\n", problems));
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                if (verb != "add")
                    throw new InvalidOperationException($"'learnx {verb}' runs in Play mode ('play 0' first).");
                EditorAutomation.Report(command, "ok", LearnXScene.Add());
                return;
            }

            var app = UnityEngine.Object.FindAnyObjectByType<LearnXContent>();
            if (app == null)
                throw new InvalidOperationException("No LearnX in the scene ('learnx add' in Edit mode).");
            if (verb == "open" || app.Pages.Page == null)
            {
                // the menu's row: LearnX shows, and the menu turns to its page (in front of the user when it's closed)
                var menu = ContentMenu.Instance ?? throw new InvalidOperationException("No content menu yet (the session isn't ready).");
                if (verb == "open" && menu.IsOpen && menu.AppPage == null)
                    menu.OpenApp(app);
                else
                    menu.OpenApp(app, inFront: !menu.IsOpen);
            }

            var panel = app.Pages;
            switch (verb)
            {
                case "status":
                case "open":
                    break;
                case "close":
                    panel.Close();
                    break;
                case "home":
                    panel.ShowClasses();
                    break;
                case "class":
                    panel.ShowClass(Catalogue.Find(rest) ?? throw new ArgumentException($"No class '{rest}'. Classes: {string.Join(", ", Catalogue.Classes.Select(c => c.name))}"));
                    break;
                case "lesson":
                {
                    var schoolClass = panel.Class ?? throw new InvalidOperationException("Open a class first ('learnx class Year 1').");
                    var lesson = int.TryParse(rest, out var n) && n >= 1 && n <= schoolClass.lessons.Length ? schoolClass.lessons[n - 1]
                        : schoolClass.lessons.FirstOrDefault(l => l.id == rest) ?? throw new ArgumentException($"No lesson '{rest}' in {schoolClass.name}.");
                    panel.ShowLesson(lesson);
                    break;
                }
                case "next":
                    panel.Next();
                    break;
                case "previous":
                    panel.Previous();
                    break;
                case "answer":
                    if (panel.Progress == null || !panel.Progress.OnCheck)
                        throw new InvalidOperationException("No check is showing ('learnx next' to the end of a lesson).");
                    panel.Choose(int.Parse(rest) - 1);
                    break;
                case "again":
                    panel.TryAgain();
                    break;
                case "watch":
                    if (panel.Progress == null || !panel.Progress.OnWatch)
                        throw new InvalidOperationException("No watch step is showing (open a lesson with media: 'learnx class Nursery', then 'learnx lesson 1').");
                    panel.Watch(panel.Progress.Lesson);
                    break;
                case "media":
                {
                    // "learnx media" (status) · "learnx media seek <s>" · "learnx media end" (2 s before the end)
                    var media = app.Stage.Media;
                    if (parts.Length > 1 && media == null)
                        throw new InvalidOperationException("No media is playing ('learnx watch' first).");
                    if (parts.Length > 2 && parts[1] == "seek")
                        media.Seek(double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                    else if (parts.Length > 1 && parts[1] == "end")
                        media.Seek(media.Duration - 2.0);
                    EditorAutomation.Report(command, "ok", $"{app.Stage.Status}{(app.Stage.HasFinished ? " · finished" : string.Empty)}");
                    return;
                }
                case "fit":
                {
                    var problems = panel.Overflowing();
                    EditorAutomation.Report(command, problems.Count == 0 ? "ok" : "error",
                        problems.Count == 0 ? "Every text fits." : string.Join("\n", problems));
                    return;
                }
                case "tour":
                    Tour(command, panel);
                    return;
                default:
                    throw new ArgumentException("Usage: learnx add|check|status|open|close|home|class <name>|lesson <n>|next|previous|answer <n>|again|watch|media [seek <s>|end]|fit|tour");
            }

            EditorAutomation.Report(command, "ok", Describe(panel));
        }

        /// <summary>
        /// Every view LearnX can show, one after another: the classes, each class's lessons, and each lesson's pages and
        /// check (answered wrong, then right). Reports the texts that don't fit, and any right answer not marked right.
        /// </summary>
        static void Tour(string command, LearnPages panel)
        {
            panel.Open();
            var problems = new List<string>();
            var views = 0;

            void Look(string where)
            {
                views++;
                problems.AddRange(panel.Overflowing().Select(p => $"{where}: {p}"));
            }

            panel.ShowClasses();
            Look("classes");
            foreach (var schoolClass in Catalogue.Classes)
            {
                panel.ShowClass(schoolClass);
                for (var page = 0; page < panel.ListPages; page++)
                {
                    panel.ShowClass(schoolClass, page);
                    Look($"{schoolClass.name}, lessons page {page + 1}");
                }

                foreach (var lesson in schoolClass.lessons)
                {
                    panel.ShowClass(schoolClass);
                    panel.ShowLesson(lesson, play: false);
                    var progress = panel.Progress;
                    while (!progress.OnCheck)
                    {
                        Look(progress.OnWatch ? $"{lesson.id}, watch step" : $"{lesson.id}, page {progress.Page + 1}");
                        if (!progress.CanGoOn)
                            break;
                        panel.Next();
                    }

                    if (!progress.OnCheck)
                        continue;
                    Look($"{lesson.id}, check");
                    var wrong = Enumerable.Range(0, lesson.check.options.Length).First(i => i != lesson.check.answer);
                    panel.Choose(wrong);
                    if (progress.Correct)
                        problems.Add($"{lesson.id}: option {wrong + 1} marked right");
                    Look($"{lesson.id}, check answered wrong");
                    panel.TryAgain();
                    panel.Choose(lesson.check.answer);
                    if (!progress.Correct)
                        problems.Add($"{lesson.id}: the answer, option {lesson.check.answer + 1}, isn't marked right");
                    Look($"{lesson.id}, check answered right");
                }
            }

            panel.ShowClasses();
            var lessons = Catalogue.Classes.Sum(c => c.lessons.Length);
            EditorAutomation.Report(command, problems.Count == 0 ? "ok" : "error",
                $"{views} views of {Catalogue.Classes.Count} classes and {lessons} lessons. " +
                (problems.Count == 0 ? "Everything fits, and every answer is marked as it should be." : $"{problems.Count} problem(s):\n" + string.Join("\n", problems)));
        }

        static string Describe(LearnPages panel)
        {
            var text = new StringBuilder();
            var menu = ContentMenu.Instance;
            text.AppendLine($"Page {(panel.IsOpen ? "open" : "closed")}{(panel.IsOpen ? $" at {panel.Page.Panel.transform.position:F2}" : string.Empty)}, showing {panel.Showing}" +
                            $" · menu {(menu != null ? menu.Describe() : "none")}.");
            if (panel.Class != null)
                text.AppendLine($"Class: {panel.Class.name} ({panel.Class.stage}, ages {panel.Class.ages}), {panel.Class.lessons.Length} lesson{(panel.Class.lessons.Length == 1 ? "" : "s")}, " +
                                $"list page {panel.ListPage + 1} of {panel.ListPages}.");
            var progress = panel.Progress;
            if (progress != null)
            {
                text.AppendLine($"Lesson: {progress.Lesson.title} ({progress.Lesson.id}), " +
                                (progress.OnCheck ? "the check" : progress.OnWatch ? "the watch step" : $"page {progress.Page + 1} of {progress.PageCount}") +
                                (progress.Answered ? $", option {progress.Chosen + 1} chosen: {(progress.Correct ? "right" : "wrong")}" : string.Empty) + ".");
                text.AppendLine(progress.OnCheck ? $"Question: {progress.Lesson.check.question}" : progress.OnWatch ? $"Watch: {progress.Lesson.media.text}" : $"Text: {progress.Lesson.pages[progress.Page]}");
            }

            // a button is named after its label (a lesson's card after the lesson's id)
            var app = UnityEngine.Object.FindAnyObjectByType<LearnXContent>();
            if (app != null && app.Stage != null && app.Stage.Media != null)
                text.AppendLine($"Media: {app.Stage.Status}{(app.Stage.HasFinished ? " · finished" : string.Empty)}");
            var buttons = panel.ViewShowing != null ? panel.ViewShowing.GetComponentsInChildren<Button>().Where(b => b.isActiveAndEnabled).Select(b => b.name) : Enumerable.Empty<string>();
            text.Append($"Buttons: {string.Join(" · ", buttons)}");
            return text.ToString();
        }
    }
}
