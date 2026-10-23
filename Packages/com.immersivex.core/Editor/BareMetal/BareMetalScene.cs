using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// <b>ImmersiveX ▸ New Bare Metal Scene</b>: a scene for an app that owns everything, with ImmersiveX only as its host. It has
    /// the XR rig, the AR session and the ImmersiveX Session, and an app object with <see cref="BareMetalContent"/>, where the
    /// app's own components go.
    /// </summary>
    public static class BareMetalScene
    {
        [MenuItem("ImmersiveX/New Bare Metal Scene", false, 1)]
        static void NewSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var path = EditorUtility.SaveFilePanelInProject("New bare metal scene", "App", "unity", "Choose where to save the new scene.", "Assets/Scenes");
            if (!string.IsNullOrEmpty(path))
                Create(path, System.IO.Path.GetFileNameWithoutExtension(path), addToBuild: false);
        }

        /// <summary>
        /// Create the scene at <paramref name="path"/> with an app object called <paramref name="appName"/>, and return that
        /// object (null when the rig samples were just imported and it must run again). The scene stays open.
        /// </summary>
        public static GameObject Create(string path, string appName, bool addToBuild)
        {
            if (!ImmersiveXSceneBuilder.CreateScene(path, withStarterContent: false, addToBuild: addToBuild))
                return null;
            var app = new GameObject(string.IsNullOrEmpty(appName) ? "App" : appName, typeof(BareMetalContent));
            EditorSceneManager.MarkSceneDirty(app.scene);
            EditorSceneManager.SaveScene(app.scene);
            Selection.activeGameObject = app;
            ImmersiveXLog.Info($"Bare metal: '{app.name}' in {path} owns the app; put its components on it.");
            return app;
        }
    }
}
