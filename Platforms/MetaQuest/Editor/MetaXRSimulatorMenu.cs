using ImmersiveX.Editor;
using UnityEditor;

namespace ImmersiveX.Platforms.MetaQuest.Editor
{
    /// <summary>
    /// <b>ImmersiveX ▸ Play Mode ▸ Meta XR Simulator (Quest)</b>: run Play mode on the Meta XR Simulator, so the
    /// Quest path (passthrough over a simulated room, Space Setup data, anchors, hands and controllers) can be tested without a headset.
    /// </summary>
    public static class MetaXRSimulatorMenu
    {
        const string MenuPath = "ImmersiveX/Play Mode/Meta XR Simulator (Quest)";
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem(MenuPath, false, 21)]
        public static void UseMetaXRSimulator()
        {
            if (!MetaXRSimulatorRuntime.IsInstalled)
            {
                ImmersiveXLog.Error($"Meta XR Simulator not found at {MetaXRSimulatorRuntime.RuntimeJson}. Install it from Meta, or use ImmersiveX ▸ Play Mode ▸ Locate Meta XR Simulator…");
                return;
            }

            XrLoaderSetup.UseOnlyLoader(BuildTargetGroup.Standalone, OpenXRLoader);
            MetaQuestBuildConfigurator.ConfigureOpenXRFeatures(BuildTargetGroup.Standalone);
            AssetDatabase.SaveAssets();
            EditorPlayMode.Current = MetaXRSimulatorRuntime.PlayModeId;
            ImmersiveXLog.Info("Play mode now runs on the Meta XR Simulator. Press Play; the simulator window opens with a simulated room.");
        }

        [MenuItem(MenuPath, true)]
        static bool UseMetaXRSimulatorValidate()
        {
            Menu.SetChecked(MenuPath, EditorPlayMode.Current == MetaXRSimulatorRuntime.PlayModeId);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem("ImmersiveX/Play Mode/Locate Meta XR Simulator…", false, 40)]
        static void Locate()
        {
            var path = EditorUtility.OpenFilePanel("Select meta_openxr_simulator.json", "/Applications", "json");
            if (string.IsNullOrEmpty(path))
                return;
            MetaXRSimulatorRuntime.RuntimeJson = path;
            ImmersiveXLog.Info($"Meta XR Simulator runtime set to {path}.");
        }
    }
}
