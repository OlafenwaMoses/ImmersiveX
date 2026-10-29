using System.Linq;
using NUnit.Framework;

namespace LearnX.Tests
{
    public sealed class CatalogueTests
    {
        static readonly string[] Names =
            { "Nursery", "Year 1", "Year 2", "Year 3", "Year 4", "Year 5", "Year 6", "Year 7", "Year 8", "Year 9", "Year 10", "Year 11", "Year 12" };

        [Test]
        public void EveryClassFromNurseryToYear12IsThereInOrder()
        {
            CollectionAssert.AreEqual(Names, Catalogue.Classes.Select(c => c.name).ToArray());
        }

        [Test]
        public void EachClassIsInItsStageOfEducationInEngland()
        {
            string StageOf(string name) => Catalogue.Find(name).stage;
            Assert.AreEqual("Early years", StageOf("Nursery"));
            foreach (var year in new[] { 1, 2 })
                Assert.AreEqual("Key stage 1", StageOf($"Year {year}"));
            foreach (var year in new[] { 3, 4, 5, 6 })
                Assert.AreEqual("Key stage 2", StageOf($"Year {year}"));
            foreach (var year in new[] { 7, 8, 9 })
                Assert.AreEqual("Key stage 3", StageOf($"Year {year}"));
            foreach (var year in new[] { 10, 11 })
                Assert.AreEqual("Key stage 4", StageOf($"Year {year}"));
            Assert.AreEqual("Sixth form", StageOf("Year 12"));
        }

        [Test]
        public void TheAgesFollowTheYears()
        {
            Assert.AreEqual("3 to 4", Catalogue.Find("Nursery").ages);
            for (var year = 1; year <= 12; year++)
                Assert.AreEqual($"{year + 4} to {year + 5}", Catalogue.Find($"Year {year}").ages, $"Year {year}");
        }

        [Test]
        public void EachClassHasItsOwnLessons()
        {
            foreach (var schoolClass in Catalogue.Classes)
                Assert.GreaterOrEqual(schoolClass.lessons.Length, ContentStyle.MinLessons, schoolClass.name);
            var ids = Catalogue.Classes.SelectMany(c => c.lessons).Select(l => l.id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "lesson ids are unique");
        }

        [Test]
        public void TheMenuHasARowForEachStageYoungestFirst()
        {
            var rows = Catalogue.ByStage(Catalogue.Classes).ToList();
            CollectionAssert.AreEqual(Catalogue.Stages, rows.Select(r => r.stage).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 3, 2, 1 }, rows.Select(r => r.classes.Length).ToArray());
            Assert.LessOrEqual(rows.Max(r => r.classes.Length), 4, "a row holds 4 classes");
        }

        [Test]
        public void NurseryIsCountingTo10InTheRoom()
        {
            var lessons = Catalogue.Find("Nursery").lessons;
            Assert.AreEqual(1, lessons.Length);
            Assert.AreEqual("Counting to 10", lessons[0].title);
            Assert.IsTrue(lessons[0].HasMedia);
            Assert.IsEmpty(lessons[0].pages, "the 3D piece is the lesson");
            StringAssert.EndsWith(".sp4m", lessons[0].media.file);
        }

        [Test]
        public void EveryLessonsMediaIsInStreamingAssets()
        {
            // The media is kept out of git (Assets/StreamingAssets/ImmersiveXContent): LearnX/README.md says where to get it.
            foreach (var lesson in Catalogue.Classes.SelectMany(c => c.lessons).Where(l => l.HasMedia))
            {
                var path = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, lesson.media.file);
                Assert.IsTrue(System.IO.File.Exists(path), $"{lesson.id}: {path} is missing");
            }
        }

        [Test]
        public void FindsAClassByItsIdOrName()
        {
            Assert.AreSame(Catalogue.Find("year-7"), Catalogue.Find("Year 7"));
            Assert.AreSame(Catalogue.Find("nursery"), Catalogue.Find("NURSERY"));
            Assert.IsNull(Catalogue.Find("Year 13"));
        }

        [Test]
        public void ParsingLeavesNoNullLists()
        {
            var parsed = Catalogue.Parse("{\"id\":\"x\",\"lessons\":[{\"id\":\"a\",\"check\":{\"question\":\"q\"}}]}");
            Assert.IsNotNull(parsed.lessons[0].pages);
            Assert.IsNotNull(parsed.lessons[0].check.options);
            Assert.IsNotNull(Catalogue.Parse("{}").lessons);
        }
    }
}
