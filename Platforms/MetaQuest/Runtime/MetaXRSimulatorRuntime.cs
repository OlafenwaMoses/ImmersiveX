#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>
    /// Points the editor's OpenXR loader at the Meta XR Simulator when Play mode is set to it
    /// (<b>ImmersiveX ▸ Play Mode ▸ Meta XR Simulator (Quest)</b>). Runs before XR starts, so it wins over
    /// the OpenXR plugin's own runtime picker, which doesn't list the standalone simulator app.
    /// </summary>
    public static class MetaXRSimulatorRuntime
    {
        /// <summary>The <see cref="EditorPlayMode"/> value for the Meta XR Simulator.</summary>
        public const string PlayModeId = "metaquest-simulator";

        const string JsonPreferenceKey = "ImmersiveX.MetaXRSimulator.RuntimeJson";
        const string MacDefaultJson = "/Applications/MetaXRSimulator.app/Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json";

        /// <summary>The simulator's OpenXR runtime manifest (defaults to the macOS app install location).</summary>
        public static string RuntimeJson
        {
            get => EditorPrefs.GetString(JsonPreferenceKey, MacDefaultJson);
            set => EditorPrefs.SetString(JsonPreferenceKey, value);
        }

        public static bool IsInstalled => File.Exists(RuntimeJson);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SelectRuntimeForPlayMode()
        {
            if (EditorPlayMode.Current != PlayModeId)
                return;

            if (!IsInstalled)
            {
                ImmersiveXLog.Error($"Meta XR Simulator not found at {RuntimeJson}. Install it, or pick its meta_openxr_simulator.json with ImmersiveX ▸ Play Mode ▸ Locate Meta XR Simulator…");
                return;
            }

            Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", RuntimeJson);
            ImmersiveXLog.Info($"Play mode runs on the Meta XR Simulator ({RuntimeJson}).");
        }
    }
}
#endif
