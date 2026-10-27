using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LearnX
{
    /// <summary>One check at the end of a lesson: a question, its options, which one is right, and why.</summary>
    [Serializable]
    public sealed class Check
    {
        public string question;
        public string[] options = Array.Empty<string>();

        /// <summary>The right option, counted from 0.</summary>
        public int answer;

        public string explanation;
    }

    /// <summary>
    /// Immersive media a lesson plays in the room (ImmersiveX's Immersive Media): its file inside StreamingAssets, such as
    /// a 4D mesh (<c>.sp4m</c>) with its soundtrack beside it, how tall it stands, and what the panel says before it plays.
    /// </summary>
    [Serializable]
    public sealed class LessonMedia
    {
        public string file;
        public float height = 1.6f;
        public string text;
    }

    /// <summary>
    /// One lesson: a few short pages to read, or <see cref="media"/> to watch in the room in their place, then a
    /// <see cref="Check"/>. <see cref="curriculum"/> says which part of the national curriculum in England (or the EYFS,
    /// GCSE or A level subject content) it teaches.
    /// </summary>
    [Serializable]
    public sealed class Lesson
    {
        public string id;
        public string subject;
        public string title;
        public string summary;
        public string curriculum;
        public string[] pages = Array.Empty<string>();
        public LessonMedia media;
        public Check check;

        /// <summary>JsonUtility fills a missing <see cref="media"/> with an empty one, so this checks it names a file.</summary>
        public bool HasMedia => media != null && !string.IsNullOrWhiteSpace(media.file);
    }

    /// <summary>
    /// A class (Nursery, Year 1 to Year 12) and its lessons. <see cref="stage"/> is one of <see cref="Stages"/>; it sets
    /// how long the lessons' sentences may be (<see cref="ContentStyle"/>).
    /// </summary>
    [Serializable]
    public sealed class SchoolClass
    {
        public string id;
        public int order;
        public string name;
        public string stage;
        public string ages;

        /// <summary>A line for whoever opens the class, such as "Read these lessons with a grown-up." Optional.</summary>
        public string note;

        public Lesson[] lessons = Array.Empty<Lesson>();
    }

    /// <summary>
    /// Every class and its lessons, read from the JSON files in Resources/LearnX/Classes (one a class, in the order their
    /// <see cref="SchoolClass.order"/> gives). Adding a lesson is adding it to its class's file.
    /// </summary>
    public static class Catalogue
    {
        public const string Folder = "LearnX/Classes";

        /// <summary>The stages of education in England, youngest first, as the classes name them.</summary>
        public static readonly string[] Stages = { "Early years", "Key stage 1", "Key stage 2", "Key stage 3", "Key stage 4", "Sixth form" };

        static List<SchoolClass> _classes;

        /// <summary>The classes in order (loaded once).</summary>
        public static IReadOnlyList<SchoolClass> Classes => _classes ??= Load();

        static List<SchoolClass> Load()
        {
            var classes = new List<SchoolClass>();
            foreach (var file in Resources.LoadAll<TextAsset>(Folder))
            {
                try
                {
                    classes.Add(Parse(file.text));
                }
                catch (ArgumentException exception)
                {
                    Debug.LogError($"[LearnX] {Folder}/{file.name}.json isn't valid: {exception.Message}");
                }
            }

            return classes.OrderBy(c => c.order).ToList();
        }

        /// <summary>A class from its JSON, with every missing list empty rather than null.</summary>
        public static SchoolClass Parse(string json)
        {
            var schoolClass = JsonUtility.FromJson<SchoolClass>(json) ?? new SchoolClass();
            schoolClass.lessons ??= Array.Empty<Lesson>();
            foreach (var lesson in schoolClass.lessons)
            {
                lesson.pages ??= Array.Empty<string>();
                if (lesson.check != null)
                    lesson.check.options ??= Array.Empty<string>();
            }

            return schoolClass;
        }

        public static SchoolClass Find(string idOrName) => Classes.FirstOrDefault(c =>
            string.Equals(c.id, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(c.name, idOrName, StringComparison.OrdinalIgnoreCase));

        /// <summary>The classes grouped by stage, youngest first: what the class menu shows, a row a stage.</summary>
        public static IEnumerable<(string stage, SchoolClass[] classes)> ByStage(IEnumerable<SchoolClass> classes) =>
            classes.GroupBy(c => c.stage).Select(g => (g.Key, g.OrderBy(c => c.order).ToArray())).OrderBy(g => StageIndex(g.Key));

        /// <summary>Where <paramref name="stage"/> comes in <see cref="Stages"/>; an unknown stage comes last.</summary>
        public static int StageIndex(string stage)
        {
            var index = Array.IndexOf(Stages, stage);
            return index >= 0 ? index : Stages.Length;
        }
    }
}
