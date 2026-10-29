using NUnit.Framework;

namespace LearnX.Tests
{
    public sealed class LessonProgressTests
    {
        static LessonProgress NewLesson() => new LessonProgress(new Lesson
        {
            id = "test",
            pages = new[] { "Page 1.", "Page 2." },
            check = new Check { question = "Which?", options = new[] { "A", "B", "C" }, answer = 1, explanation = "B." },
        });

        [Test]
        public void ThePagesComeOneAtATimeThenTheCheck()
        {
            var progress = NewLesson();
            Assert.AreEqual(0, progress.Page);
            Assert.IsFalse(progress.CanGoBack);
            Assert.IsTrue(progress.Next());
            Assert.IsFalse(progress.OnCheck);
            Assert.IsTrue(progress.Next());
            Assert.IsTrue(progress.OnCheck);
            Assert.IsFalse(progress.Next(), "nothing after the check");
        }

        [Test]
        public void AnAnswerIsMarkedAndCanBeTriedAgain()
        {
            var progress = NewLesson();
            Assert.IsFalse(progress.Choose(1), "no answering before the check");
            progress.Next();
            progress.Next();
            Assert.IsTrue(progress.Choose(0));
            Assert.IsTrue(progress.Answered);
            Assert.IsFalse(progress.Correct);
            Assert.IsFalse(progress.Choose(1), "one answer until Try again");
            progress.TryAgain();
            Assert.IsTrue(progress.Choose(1));
            Assert.IsTrue(progress.Correct);
        }

        [Test]
        public void GoingBackFromTheCheckClearsTheAnswer()
        {
            var progress = NewLesson();
            progress.Next();
            progress.Next();
            progress.Choose(2);
            Assert.IsTrue(progress.Previous());
            Assert.IsFalse(progress.Answered);
            Assert.AreEqual(1, progress.Page);
        }

        [Test]
        public void OptionsOutOfRangeAreIgnored()
        {
            var progress = NewLesson();
            progress.Next();
            progress.Next();
            Assert.IsFalse(progress.Choose(-1));
            Assert.IsFalse(progress.Choose(3));
            Assert.IsFalse(progress.Answered);
        }

        static LessonProgress NewMediaLesson(bool check = true) => new LessonProgress(new Lesson
        {
            id = "media",
            media = new LessonMedia { file = "ImmersiveXContent/Test/test.sp4m", height = 1.8f, text = "Watch it." },
            check = check ? new Check { question = "Which?", options = new[] { "A", "B" }, answer = 0, explanation = "A." } : null,
        });

        [Test]
        public void ALessonWithMediaStartsOnTheWatchStepThenTheCheck()
        {
            var progress = NewMediaLesson();
            Assert.IsTrue(progress.HasMedia);
            Assert.IsTrue(progress.OnWatch, "the media is the lesson, so it opens there");
            Assert.IsFalse(progress.CanGoBack);
            Assert.IsFalse(progress.OnCheck);
            Assert.IsFalse(progress.Choose(0), "no answering on the watch step");
            Assert.IsTrue(progress.Next());
            Assert.IsTrue(progress.OnCheck);
            Assert.IsFalse(progress.Next(), "nothing after the check");
            Assert.IsTrue(progress.Previous());
            Assert.IsTrue(progress.OnWatch, "back from the check is the watch step");
        }

        [Test]
        public void ALessonWithMediaAndNoCheckIsOnlyTheWatchStep()
        {
            var progress = NewMediaLesson(check: false);
            Assert.IsTrue(progress.OnWatch);
            Assert.IsFalse(progress.CanGoOn);
            Assert.IsFalse(progress.CanGoBack);
        }

        [Test]
        public void AnEmptyMediaEntryIsNoMedia()
        {
            Assert.IsFalse(new Lesson { media = new LessonMedia() }.HasMedia, "JsonUtility fills a missing media field with an empty one");
            Assert.IsFalse(new Lesson().HasMedia);
        }

        [Test]
        public void ALessonWithoutACheckEndsOnItsLastPage()
        {
            var progress = new LessonProgress(new Lesson { id = "x", pages = new[] { "One.", "Two." } });
            Assert.IsTrue(progress.Next());
            Assert.IsFalse(progress.CanGoOn);
            Assert.IsFalse(progress.OnCheck);
        }
    }
}
