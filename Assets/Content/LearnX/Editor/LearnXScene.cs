using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LearnX.Editor
{
    /// <summary>
    /// <b>ImmersiveX ▸ Content ▸ Add LearnX</b>: LearnX at the top of the open scene's content, so it's the first row of the
    /// content menu after anything the app adds itself. The scene is saved.
    /// </summary>
    static class LearnXScene
    {
        [MenuItem("ImmersiveX/Content/Add LearnX", false, 40)]
        static void AddMenu() => Debug.Log("[LearnX] " + Add());

        /// <summary>Add LearnX to the open scene (once) and save it; says what it did.</summary>
        public static string Add()
        {
            var scene = SceneManager.GetActiveScene();
            var existing = Object.FindAnyObjectByType<LearnXContent>(FindObjectsInactive.Include);
            if (existing != null)
                return $"LearnX is already in {scene.path} ('{existing.name}').";
            var app = new GameObject(LearnXContent.DefaultTitle).AddComponent<LearnXContent>();
            app.Title = LearnXContent.DefaultTitle;
            app.Kind = LearnXContent.DefaultKind;
            app.transform.SetAsFirstSibling();
            Undo.RegisterCreatedObjectUndo(app.gameObject, "Add LearnX");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Added LearnX to {scene.path}, first in its content.";
        }
    }
}
