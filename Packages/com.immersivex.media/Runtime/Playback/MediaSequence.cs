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
    /// a small JSON file, or (on desktop) a folder of numbered files:
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

        /// <summary>Numbered .ply/.obj/.splat/.spz/.ksplat files in a local folder, in natural order (desktop only).</summary>
        public static MediaSequence FromFolder(string folder)
        {
            var extensions = new[] { ".ply", ".obj", ".splat", ".spz", ".ksplat" };
            var files = Directory.GetFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => NaturalKey(Path.GetFileName(f)), StringComparer.Ordinal)
                .ToArray();
            if (files.Length == 0)
                throw new FormatException($"'{folder}' has no .ply, .obj, .splat, .spz or .ksplat frames.");

            var sequence = new MediaSequence { Frames = files.Select(f => new Uri(f).AbsoluteUri).ToArray() };
            var audio = Directory.GetFiles(folder).FirstOrDefault(f => new[] { ".mp3", ".ogg", ".wav", ".m4a" }.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (audio != null)
                sequence.AudioUrl = new Uri(audio).AbsoluteUri;
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
