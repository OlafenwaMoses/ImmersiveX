using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LearnX
{
    /// <summary>
    /// Checks lessons against GOV.UK's writing standards (guidance.publishing.service.gov.uk, "Writing to GOV.UK
    /// standards": the A to Z style guide, "Use clear language", "Use the right tone", "Write clear titles" and "Write clear
    /// summaries"), and against a reading level for each stage of education:
    /// <list type="bullet">
    /// <item>short sentences: GOV.UK checks any over 25 words; younger classes get fewer (<see cref="MaxSentenceWords"/>);</item>
    /// <item>"for example" or "such as", not eg, ie or etc; "and", not &amp;; no "please";</item>
    /// <item>no negative contractions (cannot, do not), nor should've, could've, would've or they've;</item>
    /// <item>numerals for numbers, except "one"; first to ninth spelt out; no ordinals in dates; "to" in date ranges;</item>
    /// <item>"per cent", or % with a number;</item>
    /// <item>single curly quotes for words, terms, titles and made-up examples, and double curly quotes only for what
    /// someone said or wrote; quoted text is left out of the word rules (a lesson on contractions quotes ‘don't’);</item>
    /// <item>titles of 65 characters or fewer, not questions, with no full stop, dash or slash; summaries of 160 characters
    /// or fewer, ending with a full stop;</item>
    /// <item>no exclamation marks, from GOV.UK's tone: specific, clear and calm, not emotive.</item>
    /// </list>
    /// Apostrophes in words are straight (it's, Scrooge's), so a curly ’ only ever closes a quote.
    /// </summary>
    public static class ContentStyle
    {
        public const int MaxTitleCharacters = 65;
        public const int MaxSummaryCharacters = 160;

        /// <summary>A lesson's part of the curriculum fits 2 lines at the bottom of the panel.</summary>
        public const int MaxCurriculumCharacters = 140;
        public const int MinLessons = 1;
        public const int MaxOptions = 4;

        /// <summary>The longest sentence for a stage, in words.</summary>
        public static int MaxSentenceWords(string stage) => stage switch
        {
            "Early years" => 12,
            "Key stage 1" => 14,
            "Key stage 2" => 18,
            "Key stage 3" => 22,
            _ => 25,
        };

        const RegexOptions IgnoreCase = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex Quoted = new Regex("‘[^‘’]*’|“[^“”]*”");
        static readonly Regex Word = new Regex(@"[\p{L}\p{N}]+(?:'\p{L}+|(?<=\d)[.,]\d+)*"); // it's, 0.25 and 57,000 are a word each
        static readonly Regex SentenceBreak = new Regex(@"(?<=[.?!][’”]?)\s+|\n+");

        static readonly (Regex pattern, string rule)[] Rules =
        {
            (new Regex(@"\b(?:e\.g|i\.e)\.|\b(?:eg|ie|etc)\b", IgnoreCase), "write ‘for example’ or ‘such as’, not eg, ie or etc"),
            (new Regex("&"), "write ‘and’, not &"),
            (new Regex(@"\bplease\b", IgnoreCase), "leave out ‘please’"),
            (new Regex(@"\b(?:can|don|won|isn|aren|doesn|didn|wasn|weren|haven|hasn|hadn|shouldn|couldn|wouldn|mustn|needn)['’]t\b|\b(?:should|could|would|might|must|they)['’]ve\b", IgnoreCase),
                "no negative or ’ve contractions: write ‘cannot’, ‘do not’, ‘should have’"),
            (new Regex(@"\b(?:two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b", IgnoreCase), "write numbers as numerals, except ‘one’"),
            (new Regex(@"\b[1-9](?:st|nd|rd|th)\b"), "spell out first to ninth"),
            (new Regex(@"\b\d{1,2}(?:st|nd|rd|th) (?:January|February|March|April|May|June|July|August|September|October|November|December)\b"),
                "no ordinals in dates: 2 June, not 2nd June"),
            (new Regex(@"\b(?:1\d{3}|20\d{2}) ?[-–—] ?(?:1\d{3}|20\d{2})\b"), "use ‘to’ in date ranges, not a dash"),
            (new Regex(@"\bpercent\b", IgnoreCase), "write ‘per cent’, or % with a number"),
            (new Regex("!"), "no exclamation marks"),
            (new Regex("\""), "use curly quotes: ‘ ’ for words and examples, “ ” for what someone said or wrote"),
        };

        /// <summary>Every problem with the classes, one line each; none when they all follow the style.</summary>
        public static List<string> Problems(IEnumerable<SchoolClass> classes)
        {
            var list = classes.ToList();
            var problems = list.SelectMany(Problems).ToList();
            problems.AddRange(list.GroupBy(c => c.id).Where(g => g.Count() > 1).Select(g => $"class id {g.Key} is used {g.Count()} times"));
            problems.AddRange(list.GroupBy(c => c.order).Where(g => g.Count() > 1).Select(g => $"class order {g.Key} is used {g.Count()} times"));
            problems.AddRange(list.SelectMany(c => c.lessons).GroupBy(l => l.id).Where(g => g.Count() > 1)
                .Select(g => $"lesson id {g.Key} is used {g.Count()} times"));
            return problems;
        }

        /// <summary>Every problem with one class and its lessons.</summary>
        public static IEnumerable<string> Problems(SchoolClass schoolClass)
        {
            var where = schoolClass.name ?? schoolClass.id ?? "a class";
            if (string.IsNullOrWhiteSpace(schoolClass.id) || string.IsNullOrWhiteSpace(schoolClass.name) || string.IsNullOrWhiteSpace(schoolClass.ages))
                yield return $"{where}: needs an id, a name and ages";
            if (Array.IndexOf(Catalogue.Stages, schoolClass.stage) < 0)
                yield return $"{where}: stage ‘{schoolClass.stage}’ isn't one of {string.Join(", ", Catalogue.Stages)}";
            if (schoolClass.lessons.Length < MinLessons)
                yield return $"{where}: {schoolClass.lessons.Length} lessons (at least {MinLessons})";
            var words = MaxSentenceWords(schoolClass.stage);
            foreach (var problem in TextProblems(schoolClass.note, words))
                yield return $"{where}, note: {problem}";
            foreach (var lesson in schoolClass.lessons)
            foreach (var problem in LessonProblems(lesson, words))
                yield return $"{where}, {lesson.id ?? "a lesson"}: {problem}";
        }

        static IEnumerable<string> LessonProblems(Lesson lesson, int words)
        {
            if (string.IsNullOrWhiteSpace(lesson.id) || string.IsNullOrWhiteSpace(lesson.subject) || string.IsNullOrWhiteSpace(lesson.curriculum))
                yield return "needs an id, a subject and its part of the curriculum";
            else if (lesson.curriculum.Length > MaxCurriculumCharacters)
                yield return $"curriculum: {lesson.curriculum.Length} characters (at most {MaxCurriculumCharacters})";
            foreach (var problem in TitleProblems(lesson.title))
                yield return $"title: {problem}";
            if (string.IsNullOrWhiteSpace(lesson.summary))
                yield return "needs a summary";
            else
            {
                if (lesson.summary.Length > MaxSummaryCharacters)
                    yield return $"summary: {lesson.summary.Length} characters (at most {MaxSummaryCharacters})";
                if (!lesson.summary.TrimEnd().EndsWith("."))
                    yield return "summary: end it with a full stop";
                foreach (var problem in TextProblems(lesson.summary, words))
                    yield return $"summary: {problem}";
            }

            // a lesson with media is the media: it plays as soon as the lesson opens, with no pages to read first
            if (lesson.HasMedia && lesson.pages.Length > 0)
                yield return $"{lesson.pages.Length} page(s): a lesson with media has none, so it opens on its media";
            else if (!lesson.HasMedia && lesson.pages.Length < 2)
                yield return $"{lesson.pages.Length} page(s) (at least 2)";
            for (var i = 0; i < lesson.pages.Length; i++)
            foreach (var problem in TextProblems(lesson.pages[i], words))
                yield return $"page {i + 1}: {problem}";

            if (lesson.HasMedia)
            {
                var m = lesson.media;
                if (m.file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    yield return "media: never name it .gz (Android's packer drops the extension)";
                if (m.height < 0.3f || m.height > 4f)
                    yield return $"media: height {m.height} m (0.3 to 4)";
                if (string.IsNullOrWhiteSpace(m.text))
                    yield return "media: say what to do while it plays";
                foreach (var problem in TextProblems(m.text, words))
                    yield return $"media: {problem}";
            }

            var check = lesson.check;
            if (check == null || string.IsNullOrWhiteSpace(check.question) || string.IsNullOrWhiteSpace(check.explanation))
            {
                yield return "needs a check with a question and an explanation";
                yield break;
            }

            foreach (var problem in TextProblems(check.question, words))
                yield return $"check: {problem}";
            foreach (var problem in TextProblems(check.explanation, words))
                yield return $"explanation: {problem}";
            if (check.options.Length < 2 || check.options.Length > MaxOptions)
                yield return $"check: {check.options.Length} options (2 to {MaxOptions})";
            if (check.options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != check.options.Length || check.options.Any(string.IsNullOrWhiteSpace))
                yield return "check: every option must be different and not empty";
            if (check.answer < 0 || check.answer >= check.options.Length)
                yield return $"check: answer {check.answer} isn't one of the options";
            foreach (var option in check.options)
            foreach (var problem in TextProblems(option, int.MaxValue))
                yield return $"option ‘{option}’: {problem}";
        }

        /// <summary>What's wrong with a lesson's title: GOV.UK's rules for titles.</summary>
        public static IEnumerable<string> TitleProblems(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                yield return "needs a title";
                yield break;
            }

            if (title.Length > MaxTitleCharacters)
                yield return $"{title.Length} characters (at most {MaxTitleCharacters})";
            if (title.TrimEnd().EndsWith("."))
                yield return "no full stop at the end";
            if (title.Contains("?"))
                yield return "not a question";
            if (title.Contains(" - ") || title.Contains("–") || title.Contains("—") || title.Contains("/"))
                yield return "no dashes or slashes";
            foreach (var problem in TextProblems(title, int.MaxValue))
                yield return problem;
        }

        /// <summary>
        /// What's wrong with a piece of text: unmatched quotes, the word rules (outside quotes) and sentences longer than
        /// <paramref name="maxWords"/>. Nothing for empty text.
        /// </summary>
        public static IEnumerable<string> TextProblems(string text, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;
            if (text.Count(c => c == '‘') != text.Count(c => c == '’') || text.Count(c => c == '“') != text.Count(c => c == '”'))
                yield return $"unmatched quotes (apostrophes in words are straight: it's): {text}";
            var outsideQuotes = Quoted.Replace(text, " … ");
            foreach (var (pattern, rule) in Rules)
            {
                var match = pattern.Match(outsideQuotes);
                if (match.Success)
                    yield return $"{rule} (‘{match.Value}’ in: {text})";
            }

            foreach (var sentence in SentenceBreak.Split(text.Trim()))
            {
                var count = WordCount(sentence);
                if (count > maxWords)
                    yield return $"{count}-word sentence (at most {maxWords} at this stage): {sentence}";
            }
        }

        public static int WordCount(string text) => string.IsNullOrEmpty(text) ? 0 : Word.Matches(text).Count;
    }
}
