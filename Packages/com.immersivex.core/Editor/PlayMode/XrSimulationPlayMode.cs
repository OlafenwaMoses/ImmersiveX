using UnityEditor;

namespace ImmersiveX.Editor
{
    /// <summary><b>ImmersiveX ▸ Play Mode ▸ XR Simulation (any device)</b>: run Play mode on AR Foundation's XR Simulation (the default).</summary>
    public static class XrSimulationPlayMode
    {
        const string MenuPath = "ImmersiveX/Play Mode/XR Simulation (any device)";
        const string SimulationLoader = "UnityEngine.XR.Simulation.SimulationLoader";

        [MenuItem(MenuPath, false, 20)]
        public static void Use()
        {
            XrLoaderSetup.UseOnlyLoader(BuildTargetGroup.Standalone, SimulationLoader);
            AssetDatabase.SaveAssets();
            EditorPlayMode.Current = EditorPlayMode.XRSimulation;
            ImmersiveXLog.Info("Play mode now runs on XR Simulation.");
        }

        [MenuItem(MenuPath, true)]
        static bool UseValidate()
        {
            Menu.SetChecked(MenuPath, EditorPlayMode.Current == EditorPlayMode.XRSimulation);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
