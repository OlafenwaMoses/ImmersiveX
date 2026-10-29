using System.Linq;
using NUnit.Framework;

namespace LearnX.Tests
{
    public sealed class ContentStyleTests
    {
        [Test]
        public void EveryLessonFollowsTheStyleAndItsReadingLevel()
        {
            var problems = ContentStyle.Problems(Catalogue.Classes);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        static string[] Problems(string text, int words = 25) => ContentStyle.TextProblems(text, words).ToArray();

        [TestCase("Bring a pencil, a ruler, etc.")]
        [TestCase("Use a fruit, e.g. an apple.")]
        [TestCase("Salt & pepper.")]
        [TestCase("Please read the page.")]
        [TestCase("You can't divide by 0.")]
        [TestCase("You don't need a calculator.")]
        [TestCase("They should've waited.")]
        [TestCase("There are three ducks.")]
        [TestCase("It's the 2nd step.")]
        [TestCase("It started on 2nd September 1666.")]
        [TestCase("The war lasted from 1914-1918.")]
        [TestCase("Half is 50 percent.")]
        [TestCase("Well done!")]
        [TestCase("He said \"hello\".")]
        public void FlagsWhatGovUkStyleLeavesOut(string text)
        {
            Assert.IsNotEmpty(Problems(text), text);
        }

        [TestCase("There are 3 ducks and one goose.")]
        [TestCase("‘Do not’ becomes ‘don't’, and ‘three’ is a word.")]
        [TestCase("Romeo says “Two households” in the prologue.")]
        [TestCase("It's 50% of 100, which is 0.5 of it.")]
        [TestCase("The war lasted from 1914 to 1918, and the second year was hard.")]
        [TestCase("Scrooge's partner, Jacob Marley, warns him.")]
        public void LeavesGoodTextAlone(string text)
        {
            Assert.IsEmpty(Problems(text), string.Join("\n", Problems(text)));
        }

        [Test]
        public void QuotesMustBeClosed()
        {
            Assert.IsNotEmpty(Problems("He wrote ‘hello."));
            Assert.IsNotEmpty(Problems("It’s raining.")); // a curly apostrophe reads as a quote that never opened
        }

        [Test]
        public void YoungerClassesHaveShorterSentences()
        {
            const string sentence = "The fox ran across the field and jumped over the old wooden gate.";
            Assert.AreEqual(13, ContentStyle.WordCount(sentence));
            Assert.IsNotEmpty(ContentStyle.TextProblems(sentence, ContentStyle.MaxSentenceWords("Early years")));
            Assert.IsEmpty(ContentStyle.TextProblems(sentence, ContentStyle.MaxSentenceWords("Key stage 1")));
            Assert.Less(ContentStyle.MaxSentenceWords("Key stage 2"), ContentStyle.MaxSentenceWords("Sixth form"));
            Assert.AreEqual(25, ContentStyle.MaxSentenceWords("Sixth form"), "GOV.UK checks sentences over 25 words");
        }

        [Test]
        public void SentencesBreakAtTheirEndsNotAtDecimals()
        {
            Assert.IsEmpty(ContentStyle.TextProblems("It is 0.25 of 1. That is 25%.", 5));
            Assert.IsNotEmpty(ContentStyle.TextProblems("It is 0.25 of the whole cake.", 5));
        }

        [TestCase("What is a mammal?")]
        [TestCase("Mammals.")]
        [TestCase("Mammals - and birds")]
        [TestCase("Mammals/birds")]
        [TestCase("A title that goes on and on and on, far past the 65 characters GOV.UK allows")]
        public void TitlesAreShortStatementsWithoutDashesOrSlashes(string title)
        {
            Assert.IsNotEmpty(ContentStyle.TitleProblems(title), title);
        }

        [Test]
        public void ATitleCanHaveAColon()
        {
            Assert.IsEmpty(ContentStyle.TitleProblems("Romeo and Juliet: the prologue"));
        }

        [Test]
        public void ALessonWithMediaHasNoPagesAndOneWithoutHasAtLeast2()
        {
            string[] ProblemsWith(LessonMedia media, params string[] pages) => ContentStyle.Problems(new SchoolClass
            {
                id = "x", name = "X", stage = "Early years", ages = "3 to 4",
                lessons = new[]
                {
                    new Lesson
                    {
                        id = "a", subject = "Mathematics", title = "Counting", summary = "Count.", curriculum = "EYFS mathematics", pages = pages, media = media,
                        check = new Check { question = "Which?", options = new[] { "1", "2" }, answer = 0, explanation = "It is 1." },
                    },
                },
            }).ToArray();

            var media = new LessonMedia { file = "ImmersiveXContent/Test/test.sp4m", height = 1.8f, text = "Watch it." };
            Assert.IsEmpty(ProblemsWith(media), "the media is the lesson");
            Assert.IsNotEmpty(ProblemsWith(media, "Page 1.", "Page 2."), "pages would stand before the media");
            Assert.IsEmpty(ProblemsWith(null, "Page 1.", "Page 2."));
            Assert.IsNotEmpty(ProblemsWith(null, "Page 1."));
        }
    }
}
