using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// Applies the project-wide settings every ImmersiveX project relies on:
    /// the Universal Render Pipeline with XR-friendly defaults, linear colour,
    /// the Input System as the only input backend, and XR Simulation for Play mode.
    /// </summary>
    /// <remarks>
    /// Safe to run any number of times. Missing assets are created with ImmersiveX defaults;
    /// assets that already exist are assigned but never re-tuned, so your own changes survive.
    /// Platform settings (build target, XR loaders, graphics APIs) belong to platform adapters, not here.
    /// <para>Menu: <b>ImmersiveX ▸ Maintenance ▸ Apply Project Baseline</b></para>
    /// <para>Command line: <c>-batchmode -quit -executeMethod ImmersiveX.Editor.ProjectBaseline.Apply</c></para>
    /// </remarks>
    public static class ProjectBaseline
    {
        const string RenderingFolder = "Assets/Settings/Rendering";
        const string PipelinePath = RenderingFolder + "/ImmersiveX_URP.asset";
        const string RendererPath = RenderingFolder + "/ImmersiveX_Renderer.asset";
        const string SettingsPath = "Assets/Settings/Resources/ImmersiveXSettings.asset";

        const string EditorSimulationLoader = "UnityEngine.XR.Simulation.SimulationLoader";

        // Values of the "activeInputHandler" field in ProjectSettings.asset: 0 = legacy, 1 = Input System, 2 = both.
        const int InputSystemOnly = 1;

        [MenuItem("ImmersiveX/Maintenance/Apply Project Baseline")]
        public static void Apply()
        {
            var pipeline = EnsureRenderPipeline();
            if (GraphicsSettings.defaultRenderPipeline != pipeline)
                GraphicsSettings.defaultRenderPipeline = pipeline;

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                PlayerSettings.colorSpace = ColorSpace.Linear;

            UseInputSystemOnly();
            EnsureSettingsAsset();

            // Play mode in the editor runs on AR Foundation's XR Simulation (see docs/spikes/S4).
            XrLoaderSetup.UseOnlyLoader(BuildTargetGroup.Standalone, EditorSimulationLoader);

            AssetDatabase.SaveAssets();
            Debug.Log($"[ImmersiveX] Project baseline applied: URP ({PipelinePath}), linear colour, Input System, XR Simulation for Play mode.");
        }

        static UniversalRenderPipelineAsset EnsureRenderPipeline()
        {
            EnsureFolder(RenderingFolder);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline != null)
                return pipeline;

            pipeline = UniversalRenderPipelineAsset.Create(renderer);

            // XR-friendly defaults. Every extra full-screen pass is paid twice in stereo,
            // so HDR and the depth/opaque copies stay off until content needs them.
            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.useSRPBatcher = true;
            pipeline.shadowDistance = 20f;

            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            return pipeline;
        }

        static void EnsureSettingsAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<ImmersiveXSettings>(SettingsPath) != null)
                return;
            EnsureFolder(Path.GetDirectoryName(SettingsPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ImmersiveXSettings>(), SettingsPath);
        }

        static void UseInputSystemOnly()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets.Length == 0)
                return;

            var settings = new SerializedObject(assets[0]);
            var handler = settings.FindProperty("activeInputHandler");
            if (handler == null || handler.intValue == InputSystemOnly)
                return;

            handler.intValue = InputSystemOnly;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Debug.LogWarning("[ImmersiveX] Switched input handling to the Input System. Restart the editor for it to take effect.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
