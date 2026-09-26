using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace ImmersiveX.Editor
{
    /// <summary>Helpers for XR Plug-in Management: make sure settings exist and the right loader is active per build target.</summary>
    public static class XrLoaderSetup
    {
        const string SettingsFolder = "Assets/XR";
        const string SettingsPath = SettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";

        /// <summary>The XR manager settings for a build target, created if missing.</summary>
        public static XRManagerSettings ManagerFor(BuildTargetGroup group)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsPath);
                if (perTarget == null)
                {
                    Directory.CreateDirectory(SettingsFolder);
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, SettingsPath);
                }

                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            EditorUtility.SetDirty(general);
            return perTarget.ManagerSettingsForBuildTarget(group);
        }

        /// <summary>Make <paramref name="loaderTypeName"/> the only active loader for <paramref name="group"/>.</summary>
        public static bool UseOnlyLoader(BuildTargetGroup group, string loaderTypeName)
        {
            var manager = ManagerFor(group);
            foreach (var loader in manager.activeLoaders.ToArray())
            {
                if (loader.GetType().FullName != loaderTypeName)
                    XRPackageMetadataStore.RemoveLoader(manager, loader.GetType().FullName, group);
            }

            if (manager.activeLoaders.Any(loader => loader.GetType().FullName == loaderTypeName))
                return true;

            var assigned = XRPackageMetadataStore.AssignLoader(manager, loaderTypeName, group);
            if (!assigned)
                ImmersiveXLog.Warn($"Couldn't assign XR loader {loaderTypeName} for {group}. Is its package installed?");
            EditorUtility.SetDirty(manager);
            return assigned;
        }
    }
}
