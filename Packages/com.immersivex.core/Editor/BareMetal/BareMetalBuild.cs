using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// What the build's scenes ask of the host, read from the saved scene files without opening them. Platform adapters use
    /// it for what bare metal content needs in the build (Quest: the camera permissions).
    /// </summary>
    public static class BareMetalBuild
    {
        /// <summary>The enabled scenes in the build.</summary>
        public static IEnumerable<string> BuildScenes() =>
            EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path);

        /// <summary>True when bare metal content in <paramref name="scenes"/> (default: the build's) reads the headset's cameras.</summary>
        public static bool UsesPassthroughCamera(IEnumerable<string> scenes = null) =>
            Components(scenes ?? BuildScenes()).Any(document => Regex.IsMatch(document, @"^\s*_passthroughCamera: 1\s*$", RegexOptions.Multiline));

        /// <summary>True when any of <paramref name="scenes"/> (default: the build's) has bare metal content.</summary>
        public static bool HasBareMetalContent(IEnumerable<string> scenes = null) => Components(scenes ?? BuildScenes()).Any();

        /// <summary>Every saved Bare Metal Content component in <paramref name="scenes"/>, as its YAML.</summary>
        static IEnumerable<string> Components(IEnumerable<string> scenes)
        {
            var script = AssetDatabase.FindAssets("BareMetalContent t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileName(p) == "BareMetalContent.cs");
            if (script == null)
                yield break;
            var guid = AssetDatabase.AssetPathToGUID(script);
            foreach (var scene in scenes)
                foreach (var document in Regex.Split(File.ReadAllText(scene), @"^--- !u!", RegexOptions.Multiline))
                    if (document.Contains($"guid: {guid}"))
                        yield return document;
        }
    }
}
