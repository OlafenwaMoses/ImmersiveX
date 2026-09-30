using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// Stops a build that would ship Immersive Media whose content won't play on the device. It checks every Immersive
    /// Media in the build's scenes for:
    /// <list type="bullet">
    /// <item>a Source that isn't in StreamingAssets (large content is git-ignored, so a fresh clone may lack it);</item>
    /// <item>a stream or sequence with frames missing;</item>
    /// <item>a path on this computer, which the device won't have.</item>
    /// </list>
    /// Plain-http URLs get a warning. Also on <b>ImmersiveX ▸ Media ▸ Check Content in the Build</b>.
    /// </summary>
    public sealed class MediaBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var problems = Run(out var summary);
            if (problems > 0)
                throw new BuildFailedException(summary);
        }

        [MenuItem("ImmersiveX/Media/Check Content in the Build", false, 31)]
        static void CheckMenu()
        {
            var problems = Run(out var summary);
            EditorUtility.DisplayDialog("Immersive Media content", problems > 0 ? summary : summary.Length > 0 ? summary : "Every Immersive Media in the build's scenes has its content.", "OK");
        }

        /// <summary>Check the enabled build scenes. Returns how many problems there are; <paramref name="summary"/> lists them and any warnings.</summary>
        public static int Run(out string summary)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path);
            var problems = new List<string>();
            var warnings = new List<string>();
            foreach (var media in MediaIn(scenes))
            {
                var problem = Check(media.Source, media.Quality, out var warning);
                var where = $"{Path.GetFileNameWithoutExtension(media.Scene)} ▸ {media.Name}";
                if (problem != null)
                    problems.Add($"• {where}: {problem}");
                if (warning != null)
                    warnings.Add($"• {where}: {warning}");
            }

            var text = new StringBuilder();
            if (problems.Count > 0)
            {
                text.AppendLine("Immersive Media content is missing, so it wouldn't play in the app:");
                problems.ForEach(p => text.AppendLine(p));
                text.AppendLine("Add content with ImmersiveX ▸ Media ▸ Add Media… (it copies it into StreamingAssets), or fix the Source.");
            }

            if (warnings.Count > 0)
            {
                text.AppendLine("Warnings:");
                warnings.ForEach(w => text.AppendLine(w));
            }

            summary = text.ToString().TrimEnd();
            if (warnings.Count > 0 && problems.Count == 0)
                ImmersiveXLog.Warn(summary);
            return problems.Count;
        }

        public struct SavedMedia
        {
            public string Scene;
            public string Name;
            public string Source;
            public string Quality;
        }

        /// <summary>Every Immersive Media saved in <paramref name="scenes"/> (read from the scene files, without opening them).</summary>
        public static IEnumerable<SavedMedia> MediaIn(IEnumerable<string> scenes)
        {
            var script = AssetDatabase.FindAssets("ImmersiveMedia t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileName(p) == "ImmersiveMedia.cs");
            if (script == null)
                yield break;
            var guid = AssetDatabase.AssetPathToGUID(script);
            foreach (var scene in scenes)
            {
                var documents = Regex.Split(File.ReadAllText(scene), @"^--- !u!", RegexOptions.Multiline);
                var names = new Dictionary<string, string>();
                foreach (var document in documents)
                {
                    var header = Regex.Match(document, @"^1 &(\d+)");
                    if (header.Success)
                        names[header.Groups[1].Value] = Field(document, "m_Name") ?? string.Empty;
                }

                foreach (var document in documents)
                {
                    if (!document.Contains($"guid: {guid}"))
                        continue;
                    var owner = Regex.Match(document, @"m_GameObject: \{fileID: (\d+)\}");
                    yield return new SavedMedia
                    {
                        Scene = scene,
                        Name = owner.Success && names.TryGetValue(owner.Groups[1].Value, out var name) ? name : "Immersive Media",
                        Source = Field(document, "_source") ?? string.Empty,
                        Quality = Field(document, "_quality") ?? "base",
                    };
                }
            }
        }

        /// <summary>Why <paramref name="source"/> won't play from inside the app, or null when it will.</summary>
        public static string Check(string source, string quality, out string warning)
        {
            warning = null;
            if (string.IsNullOrWhiteSpace(source))
                return "it has no Source.";
            if (source.Contains("://"))
            {
                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    warning = $"'{source}' is plain http, which Android blocks unless the project allows it; use https.";
                return null;
            }

            if (Path.IsPathRooted(source))
                return $"'{source}' is a path on this computer, which the device won't have.";

            var path = Path.Combine(Application.streamingAssetsPath, source);
            if (Directory.Exists(path))
            {
                var inFolder = new[] { "stream.json", "sequence.json" }.Select(f => Path.Combine(path, f)).FirstOrDefault(File.Exists);
                if (inFolder == null)
                {
                    try
                    {
                        MediaSequence.FromFolder(path);
                        return null;
                    }
                    catch (FormatException exception)
                    {
                        return exception.Message;
                    }
                }

                path = inFolder;
            }

            if (!File.Exists(path))
                return $"'{source}' isn't in StreamingAssets.";
            if (Path.GetExtension(path).ToLowerInvariant() != ".json")
                return null;

            try
            {
                var text = File.ReadAllText(path);
                var url = new Uri(path).AbsoluteUri;
                if (text.Contains("\"tiers\""))
                {
                    var manifest = HologramManifest.Parse(text, url);
                    var tier = manifest.FindTier(quality);
                    var missing = Enumerable.Range(0, manifest.FrameCount).Count(i => !File.Exists(new Uri(manifest.FrameUrl(tier, i)).LocalPath));
                    return missing > 0 ? $"'{source}' is missing {missing} of its {manifest.FrameCount} '{tier.Name}' frames." : null;
                }

                var sequence = MediaSequence.FromJson(text, url);
                var absent = sequence.Frames.Count(f => !File.Exists(new Uri(f).LocalPath));
                return absent > 0 ? $"'{source}' is missing {absent} of its {sequence.Frames.Length} frames." : null;
            }
            catch (FormatException exception)
            {
                return $"'{source}' can't be read ({exception.Message}).";
            }
        }

        /// <summary>A single-line YAML value, unquoted.</summary>
        static string Field(string document, string name)
        {
            var match = Regex.Match(document, $@"^\s*{name}: ?(.*)$", RegexOptions.Multiline);
            if (!match.Success)
                return null;
            var value = match.Groups[1].Value.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return Regex.Unescape(value.Substring(1, value.Length - 2));
            return value;
        }
    }
}
