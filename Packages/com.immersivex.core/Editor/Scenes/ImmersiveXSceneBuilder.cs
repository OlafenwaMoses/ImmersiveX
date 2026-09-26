using System.IO;
using System.Linq;
using ImmersiveX.Diagnostics;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ImmersiveX.Editor
{
    /// <summary>Creates scenes that are ready for ImmersiveX: XR rig with hands and controllers, AR session and the ImmersiveX Session.</summary>
    public static class ImmersiveXSceneBuilder
    {
        public const string StarterScenePath = "Assets/Scenes/Main.unity";
        const string StarterMaterialPath = "Assets/Content/HelloHologram/HelloHologram.mat";

        [MenuItem("ImmersiveX/New Scene", false, 0)]
        static void NewSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var path = EditorUtility.SaveFilePanelInProject("New ImmersiveX scene", "ImmersiveScene", "unity", "Choose where to save the new scene.", "Assets/Scenes");
            if (!string.IsNullOrEmpty(path))
                CreateScene(path, withStarterContent: false, addToBuild: false);
        }

        /// <summary>Rebuild <c>Assets/Scenes/Main.unity</c>: Hello Hologram cube, device-check panel, first scene in the build.</summary>
        [MenuItem("ImmersiveX/Maintenance/Recreate Starter Scene", false, 100)]
        public static void CreateStarterScene() => TryCreateStarterScene();

        /// <summary>Same as <see cref="CreateStarterScene"/>; returns false when samples were just imported and it must run again.</summary>
        public static bool TryCreateStarterScene() => CreateScene(StarterScenePath, withStarterContent: true, addToBuild: true);

        public static bool CreateScene(string path, bool withStarterContent, bool addToBuild)
        {
            if (!RigSamples.EnsureImported())
            {
                ImmersiveXLog.Warn("Imported the XR rig samples. Wait for scripts to finish compiling, then run the same command again.");
                return false;
            }

            var rigPrefab = RigSamples.FindRigPrefab();
            if (rigPrefab == null)
            {
                ImmersiveXLog.Error($"Couldn't find the '{RigSamples.RigPrefabName}' prefab under Assets/Samples.");
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            var origin = rig.GetComponentInChildren<XROrigin>();
            var camera = origin.Camera;
            camera.clearFlags = CameraClearFlags.SolidColor; // see-through needs a transparent clear; VR platforms switch to skybox at runtime
            camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            origin.gameObject.AddComponent<ARAnchorManager>();
            origin.gameObject.AddComponent<ARPlaneManager>().enabled = false;       // turned on once room access is granted
            origin.gameObject.AddComponent<ARBoundingBoxManager>().enabled = false;

            new GameObject("AR Session", typeof(ARSession));
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            new GameObject("ImmersiveX", typeof(ImmersiveXSession));

            if (withStarterContent)
                AddStarterContent();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            EditorSceneManager.SaveScene(scene, path);
            if (addToBuild)
                PutFirstInBuild(path);
            ImmersiveXLog.Info($"Created {path}.");
            return true;
        }

        static void AddStarterContent()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Hello Hologram";
            cube.transform.localScale = Vector3.one * 0.2f;
            cube.GetComponent<Renderer>().sharedMaterial = StarterMaterial();
            MakeImmersive.Apply(cube);

            new GameObject("Device Check", typeof(DeviceCheckPanel));
        }

        static Material StarterMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(StarterMaterialPath);
            if (material != null)
                return material;

            var shader = GraphicsSettings.defaultRenderPipeline != null ? GraphicsSettings.defaultRenderPipeline.defaultShader : Shader.Find("Standard");
            material = new Material(shader) { name = "HelloHologram", color = new Color(0.2f, 0.75f, 0.95f) };
            Directory.CreateDirectory(Path.GetDirectoryName(StarterMaterialPath));
            AssetDatabase.CreateAsset(material, StarterMaterialPath);
            return material;
        }

        static void PutFirstInBuild(string path)
        {
            var others = EditorBuildSettings.scenes.Where(scene => scene.path != path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) }.Concat(others).ToArray();
        }
    }
}
