using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>One entry from a <c>Platforms/&lt;Name&gt;/platform.json</c> file.</summary>
    [Serializable]
    public sealed class PlatformInfo
    {
        public string id;
        public string displayName;
        public string status;
        public string package;
        public string summary;
        public string docs;

        [NonSerialized] public string Folder;

        /// <summary>Manifest reference, relative to the Packages folder.</summary>
        public string FileReference => "file:../Platforms/" + Folder;
    }

    /// <summary>Reads the platform catalogue and adds adapters to <c>Packages/manifest.json</c>.</summary>
    public static class PlatformCatalog
    {
        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string ManifestPath => Path.Combine(ProjectRoot, "Packages", "manifest.json");

        public static List<PlatformInfo> Load()
        {
            var platforms = new List<PlatformInfo>();
            var root = Path.Combine(ProjectRoot, "Platforms");
            if (!Directory.Exists(root))
                return platforms;

            foreach (var folder in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(folder);
                var file = Path.Combine(folder, "platform.json");
                if (name.StartsWith("_") || !File.Exists(file))
                    continue;

                var info = JsonUtility.FromJson<PlatformInfo>(File.ReadAllText(file));
                info.Folder = name;
                platforms.Add(info);
            }

            platforms.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.Ordinal));
            return platforms;
        }

        public static bool IsInstalled(PlatformInfo platform) =>
            File.ReadAllText(ManifestPath).Contains($"\"{platform.package}\"");

        /// <summary>Add the adapter to the project's manifest and let the Package Manager resolve it.</summary>
        public static void Install(PlatformInfo platform)
        {
            if (IsInstalled(platform))
                return;

            var manifest = File.ReadAllText(ManifestPath);
            var dependencies = new Regex("\"dependencies\"\\s*:\\s*\\{");
            var match = dependencies.Match(manifest);
            if (!match.Success)
                throw new InvalidOperationException("Packages/manifest.json has no \"dependencies\" block.");

            var entry = $"\n    \"{platform.package}\": \"{platform.FileReference}\",";
            File.WriteAllText(ManifestPath, manifest.Insert(match.Index + match.Length, entry));
            ImmersiveXLog.Info($"Added {platform.displayName} ({platform.package}) to Packages/manifest.json.");
            Client.Resolve();
        }
    }
}
