using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A sequence of frame files played in order: point clouds, splats or meshes (volumetric video as files). Described by
    /// a small JSON file, or a folder of numbered files (see <see cref="MediaFolders"/>):
    /// <code>
    /// { "type": "sequence", "fps": 30, "frames": ["frame_0001.ply", "frame_0002.ply"], "audio": "audio.mp3", "up": "+y" }
    /// { "type": "sequence", "fps": 30, "pattern": "frames/frame_{0:D4}.obj", "start": 1, "count": 300 }
    /// </code>
    /// Frame paths are relative to the JSON file. "up" is +y, -y, +z or -z (optional).
    /// </summary>
    public sealed class MediaSequence
    {
        public string[] Frames;
        public float Fps = 30f;
        public string AudioUrl;
        public UpAxis Up = UpAxis.Auto;

        /// <summary>The frames' file extension (".ply", ".obj", ".splat" …).</summary>
        public string Extension => Frames.Length > 0 ? MediaSource.Extension(Frames[0]) : string.Empty;

        public double Duration => Frames.Length / (double)Fps;

        public static MediaSequence Single(string url) => new MediaSequence { Frames = new[] { url }, Fps = 1f };

        public static MediaSequence FromJson(string json, string url)
        {
            if (!(Json.Parse(json) is Dictionary<string, object> root))
                throw new FormatException("A sequence file is a JSON object.");

            var sequence = new MediaSequence();
            if (root.TryGetValue("fps", out var fps) && fps is double rate && rate > 0d)
                sequence.Fps = (float)rate;
            if (root.TryGetValue("audio", out var audio) && audio is string audioPath && audioPath.Length > 0)
                sequence.AudioUrl = MediaSource.Relative(url, audioPath);
            if (root.TryGetValue("up", out var up) && up is string axis)
                sequence.Up = ParseUp(axis);

            if (root.TryGetValue("frames", out var frames) && frames is List<object> list)
            {
                sequence.Frames = list.Select(f => MediaSource.Relative(url, (string)f)).ToArray();
            }
            else if (root.TryGetValue("pattern", out var pattern) && pattern is string format)
            {
                var start = root.TryGetValue("start", out var s) && s is double first ? (int)first : 0;
                var count = root.TryGetValue("count", out var c) && c is double n ? (int)n : 0;
                if (count <= 0)
                    throw new FormatException("A sequence with a pattern needs a count.");
                sequence.Frames = Enumerable.Range(start, count)
                    .Select(i => MediaSource.Relative(url, string.Format(CultureInfo.InvariantCulture, format, i)))
                    .ToArray();
            }
            else
            {
                throw new FormatException("A sequence needs a \"frames\" list or a \"pattern\" with a \"count\".");
            }

            if (sequence.Frames.Length == 0)
                throw new FormatException("The sequence has no frames.");
            return sequence;
        }

        /// <summary>Frame extensions a folder sequence picks up.</summary>
        public static readonly string[] FrameExtensions = { ".ply", ".obj", ".stl", ".splat", ".spz", ".ksplat" };

        static readonly string[] AudioExtensions = { ".mp3", ".ogg", ".wav", ".m4a", ".aac" };

        /// <summary>Numbered .ply/.obj/.stl/.splat/.spz/.ksplat files in a local folder, in natural order.</summary>
        public static MediaSequence FromFolder(string folder) =>
            FromFiles(new Uri(folder.TrimEnd('/', '\\') + "/").AbsoluteUri, Directory.GetFiles(folder).Select(Path.GetFileName));

        /// <summary>
        /// The numbered frames among <paramref name="names"/> (files in the folder at <paramref name="folderUrl"/>) in
        /// natural order (frame_2 before frame_10), with the folder's first sound file as the soundtrack. 30 fps.
        /// </summary>
        public static MediaSequence FromFiles(string folderUrl, IEnumerable<string> names)
        {
            var all = names.ToArray();
            var frames = all.Where(n => FrameExtensions.Contains(Path.GetExtension(n).ToLowerInvariant()))
                .OrderBy(NaturalKey, StringComparer.Ordinal)
                .ToArray();
            if (frames.Length == 0)
                throw new FormatException($"'{MediaSource.FileName(folderUrl)}' has no {string.Join(", ", FrameExtensions)} frames.");
            var types = frames.Select(f => Path.GetExtension(f).ToLowerInvariant()).Distinct().ToArray();
            if (types.Length > 1)
                throw new FormatException($"'{MediaSource.FileName(folderUrl)}' mixes frame types ({string.Join(", ", types)}); keep one kind of frame per folder.");

            var folder = folderUrl.TrimEnd('/') + "/";
            var sequence = new MediaSequence { Frames = frames.Select(f => folder + Uri.EscapeDataString(f)).ToArray() };
            var audio = all.FirstOrDefault(n => AudioExtensions.Contains(Path.GetExtension(n).ToLowerInvariant()));
            if (audio != null)
                sequence.AudioUrl = folder + Uri.EscapeDataString(audio);
            return sequence;
        }

        static string NaturalKey(string name) => Regex.Replace(name, @"\d+", m => m.Value.PadLeft(12, '0'));

        public static UpAxis ParseUp(string axis)
        {
            switch (axis.Trim().ToLowerInvariant())
            {
                case "+y":
                case "y": return UpAxis.PositiveY;
                case "-y": return UpAxis.NegativeY;
                case "+z":
                case "z": return UpAxis.PositiveZ;
                case "-z": return UpAxis.NegativeZ;
                default: return UpAxis.Auto;
            }
        }
    }
}
