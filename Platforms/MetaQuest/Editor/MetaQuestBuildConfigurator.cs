using System.Collections.Generic;
using System.Linq;
using ImmersiveX.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.CompositionLayers;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace ImmersiveX.Platforms.MetaQuest.Editor
{
    /// <summary>
    /// Applies everything a Meta Quest build needs, following Unity OpenXR: Meta's project-setup guide.
    /// Runs from ImmersiveX ▸ Platform Setup ▸ Configure, and before every build.
    /// </summary>
    public sealed class MetaQuestBuildConfigurator : IBuildConfigurator
    {
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

        // Unity OpenXR: Meta's "Meta Quest" feature set. Enabling it (as the Project Settings checkbox does) also
        // enables its required features, including Composition Layers Support, which draws passthrough.
        const string MetaFeatureSetId = "com.unity.openxr.featureset.meta";

        public string PlatformId => "metaquest";
        public string DisplayName => "Meta Quest";
        public BuildTarget BuildTarget => BuildTarget.Android;
        public BuildTargetGroup BuildTargetGroup => BuildTargetGroup.Android;
        public string OutputFileName => "ImmersiveX.apk";

        public void Configure()
        {
            ConfigurePlayer();
            XrLoaderSetup.UseOnlyLoader(BuildTargetGroup.Android, OpenXRLoader);
            ConfigureOpenXRFeatures(BuildTargetGroup.Android);
            ConfigureRenderingForPassthrough();
            RigSamples.EnsureImported();
            AssetDatabase.SaveAssets();
        }

        public IEnumerable<string> Validate()
        {
            var rules = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(rules, BuildTargetGroup.Android);
            var issues = rules.Select(rule => (rule.error ? "Error: " : "Warning: ") + rule.message).ToList();

            // Checks Unity's validation doesn't make when features are enabled from code.
            var featureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(BuildTargetGroup.Android, MetaFeatureSetId);
            if (featureSet == null || !featureSet.isEnabled)
                issues.Add("Error: the Meta Quest OpenXR feature set isn't enabled. Run Configure.");
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var layers = settings.GetFeature<OpenXRCompositionLayersFeature>();
            if (layers == null || !layers.enabled)
                issues.Add("Error: Composition Layers Support is off, so passthrough can't be drawn. Run Configure.");
            return issues;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.buildAppBundle = false;

            // GPU skinning keeps the animated hand meshes off the CPU (on in the Quest-proven VizionEnterprise project).
            var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var gpuSkinning = player.FindProperty("gpuSkinning");
            if (gpuSkinning != null && !gpuSkinning.boolValue)
            {
                gpuSkinning.boolValue = true;
                player.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>Enable the Meta Quest OpenXR features for <paramref name="group"/>: Android for headset builds, Standalone for the Meta XR Simulator.</summary>
        internal static void ConfigureOpenXRFeatures(BuildTargetGroup group)
        {
            FeatureHelpers.RefreshFeatures(group);

            // Turn on the Meta Quest feature set exactly like the Project Settings checkbox, so its required
            // features are applied too. Composition Layers Support is one of them: without it passthrough never draws.
            var featureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(group, MetaFeatureSetId);
            if (featureSet != null && !featureSet.isEnabled)
            {
                featureSet.isEnabled = true;
                OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);
            }

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            Enable<OpenXRCompositionLayersFeature>(settings); // required by passthrough
            if (group == BuildTargetGroup.Android)
                Enable<MetaQuestFeature>(settings); // Android-only: Quest manifest and device support
            Enable<ARSessionFeature>(settings);
            Enable<ARCameraFeature>(settings);        // passthrough
            Enable<ARPlaneFeature>(settings);         // walls, floor, ceiling from Space Setup
            Enable<ARBoundingBoxFeature>(settings);   // furniture from Space Setup
            Enable<ARAnchorFeature>(settings);        // persistent anchors
            Enable<ARRaycastFeature>(settings);
            Enable<BoundaryVisibilityFeature>(settings);
            Enable<HandTracking>(settings);
            Enable<MetaHandTrackingAim>(settings);
            Enable<OculusTouchControllerProfile>(settings);
            Enable<MetaQuestTouchPlusControllerProfile>(settings);
            Enable<HandInteractionProfile>(settings);

            // The feature set also switches on defaults ImmersiveX doesn't use yet. Keep them off until a milestone needs them:
            // each adds runtime cost or an extra permission.
            Disable<AROcclusionFeature>(settings);          // depth occlusion (costly)
            Disable<ColocationDiscoveryFeature>(settings);  // multi-user colocation (post-v1)
            Disable<ARMeshFeature>(settings);               // room mesh (M2/M4)

            // Meta's recommendation: poll input as late as possible so hands and controllers feel responsive.
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;

            // ImmersiveX hides the boundary itself once passthrough is showing (MetaQuestBoundary). The feature's own
            // "Suppress Visibility" fires at XR start, before the session exists, and fails with XR_ERROR_HANDLE_INVALID.
            var boundary = settings.GetFeature<BoundaryVisibilityFeature>();
            if (boundary != null)
            {
                var serialized = new SerializedObject(boundary);
                var suppress = serialized.FindProperty("m_SuppressVisibility");
                if (suppress != null && suppress.boolValue)
                {
                    suppress.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorUtility.SetDirty(settings);
        }

        static void Enable<T>(OpenXRSettings settings) where T : OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null)
            {
                ImmersiveXLog.Warn($"OpenXR feature {typeof(T).Name} isn't available. Check the pinned packages.");
                return;
            }

            if (feature.enabled)
                return;
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        static void Disable<T>(OpenXRSettings settings) where T : OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null || !feature.enabled)
                return;
            feature.enabled = false;
            EditorUtility.SetDirty(feature);
        }

        /// <summary>Unity OpenXR: Meta's recommended URP settings for passthrough: no HDR, no terrain holes, no post-processing, Auto intermediate texture.</summary>
        static void ConfigureRenderingForPassthrough()
        {
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset pipeline))
            {
                ImmersiveXLog.Warn("No URP asset is assigned. Run ImmersiveX ▸ Maintenance ▸ Apply Project Baseline.");
                return;
            }

            var serialized = new SerializedObject(pipeline);
            SetBool(serialized, "m_SupportsHDR", false);
            SetBool(serialized, "m_SupportsTerrainHoles", false);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var renderers = serialized.FindProperty("m_RendererDataList");
            for (var i = 0; i < renderers.arraySize; i++)
            {
                if (!(renderers.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData renderer))
                    continue;
                if (renderer.postProcessData != null || renderer.intermediateTextureMode != IntermediateTextureMode.Auto || !renderer.useNativeRenderPass)
                {
                    renderer.postProcessData = null;
                    renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
                    renderer.useNativeRenderPass = true; // tile-based mobile GPUs (as in the Quest-proven VizionEnterprise project)
                    EditorUtility.SetDirty(renderer);
                }
            }
        }

        static void SetBool(SerializedObject serialized, string property, bool value)
        {
            var field = serialized.FindProperty(property);
            if (field != null)
                field.boolValue = value;
        }
    }
}
