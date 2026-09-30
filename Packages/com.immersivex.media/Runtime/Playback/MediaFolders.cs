using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Lists the files in a folder of media, so a folder can be a Source. On desktop the folder is read from disk. On
    /// Android, StreamingAssets is inside the APK and can't be listed, so the build writes an index of every folder there
    /// (<see cref="IndexFile"/>) and it's read from that.
    /// </summary>
    public static class MediaFolders
    {
        /// <summary>The index of StreamingAssets folders, at the root of StreamingAssets in Android builds.</summary>
        public const string IndexFile = "ImmersiveXFolders.json";

        static Dictionary<string, string[]> _index;
        static bool _indexRead;

        /// <summary>
        /// The names of the files in the folder at <paramref name="url"/> (a file URL, or a StreamingAssets URL on Android),
        /// or null when it isn't a folder there.
        /// </summary>
        public static IEnumerator List(string url, Action<string[]> done)
        {
            if (MediaSource.TryLocalFolder(url, out var folder))
            {
                done(Directory.GetFiles(folder).Select(Path.GetFileName).Where(name => !name.EndsWith(".meta", StringComparison.Ordinal)).ToArray());
                yield break;
            }

            var root = UnityEngine.Application.streamingAssetsPath.TrimEnd('/');
            if (!url.StartsWith(root + "/", StringComparison.Ordinal))
            {
                done(null);
                yield break;
            }

            if (!_indexRead)
            {
                string json = null;
                yield return MediaSource.FetchText(root + "/" + IndexFile, (text, error) => json = text);
                _index = json != null ? ParseIndex(json) : null;
                _indexRead = true;
            }

            var relative = Uri.UnescapeDataString(url.Substring(root.Length).Trim('/'));
            done(_index != null && _index.TryGetValue(relative, out var files) ? files : null);
        }

        /// <summary>The index of every folder under <paramref name="root"/>: relative path (with /) → file names.</summary>
        public static string BuildIndex(string root)
        {
            var json = new StringBuilder("{\"folders\":{");
            var first = true;
            foreach (var folder in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var files = Directory.GetFiles(folder).Select(Path.GetFileName)
                    .Where(name => !name.EndsWith(".meta", StringComparison.Ordinal) && !name.StartsWith(".", StringComparison.Ordinal))
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
                if (files.Length == 0)
                    continue;
                var relative = folder.Substring(root.Length).Replace('\\', '/').Trim('/');
                json.Append(first ? string.Empty : ",").Append(Quote(relative)).Append(":[").Append(string.Join(",", files.Select(Quote))).Append(']');
                first = false;
            }

            return json.Append("}}").ToString();
        }

        public static Dictionary<string, string[]> ParseIndex(string json)
        {
            var index = new Dictionary<string, string[]>();
            if (Json.Parse(json) is Dictionary<string, object> root && root.TryGetValue("folders", out var folders) && folders is Dictionary<string, object> map)
                foreach (var pair in map)
                    if (pair.Value is List<object> names)
                        index[pair.Key] = names.OfType<string>().ToArray();
            return index;
        }

        /// <summary>A JSON string literal.</summary>
        internal static string Quote(string text)
        {
            var quoted = new StringBuilder("\"");
            foreach (var c in text)
            {
                if (c == '"' || c == '\\')
                    quoted.Append('\\').Append(c);
                else if (c < ' ')
                    quoted.Append("\\u").Append(((int)c).ToString("x4"));
                else
                    quoted.Append(c);
            }

            return quoted.Append('"').ToString();
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _index = null;
            _indexRead = false;
        }
    }
}
