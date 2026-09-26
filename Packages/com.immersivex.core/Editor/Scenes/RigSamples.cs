using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// The XR rig comes from the XR Interaction Toolkit's samples: "Starter Assets" and "Hands Interaction Demo"
    /// (hands and controllers), plus XR Hands' "HandVisualizer". This imports them into Assets/Samples when missing.
    /// </summary>
    public static class RigSamples
    {
        public const string RigPrefabName = "XR Origin Hands (XR Rig)";

        static readonly (string package, string sample)[] Required =
        {
            ("com.unity.xr.interaction.toolkit", "Starter Assets"),
            ("com.unity.xr.hands", "HandVisualizer"),
            ("com.unity.xr.interaction.toolkit", "Hands Interaction Demo"),
        };

        /// <summary>
        /// True when every sample is already imported. Otherwise imports the missing ones and returns false:
        /// their scripts compile after this call returns, so run the calling command again afterwards.
        /// </summary>
        public static bool EnsureImported()
        {
            var allPresent = true;
            foreach (var (package, sampleName) in Required)
            {
                var info = PackageInfo.FindForPackageName(package);
                if (info == null)
                {
                    ImmersiveXLog.Error($"Package {package} isn't installed; can't import the '{sampleName}' sample.");
                    return false;
                }

                var sample = Sample.FindByPackage(package, info.version).FirstOrDefault(s => s.displayName == sampleName);
                if (sample.displayName == null)
                {
                    ImmersiveXLog.Error($"Sample '{sampleName}' wasn't found in {package} {info.version}.");
                    return false;
                }

                if (sample.isImported)
                    continue;

                allPresent = false;
                ImmersiveXLog.Info($"Importing sample '{sampleName}' from {package} {info.version}.");
                sample.Import(Sample.ImportOptions.HideImportWindow);
            }

            return allPresent;
        }

        /// <summary>The rig prefab, found by name under Assets/Samples. Null if the samples aren't imported.</summary>
        public static GameObject FindRigPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Samples"))
                return null;

            var path = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Samples" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == RigPrefabName);
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        }
    }
}
