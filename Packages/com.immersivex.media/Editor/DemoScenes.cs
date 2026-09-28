using ImmersiveX.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImmersiveX.Media.Editor
{
    /// <summary>Ready-made demo scenes built on the standard ImmersiveX rig.</summary>
    public static class DemoScenes
    {
        public const string XperienceScenePath = "Assets/Demos/Xperience35D/Xperience35D.unity";

        /// <summary>GenXR's 3.5D Xperience: a music video reconstructed as a streamed Gaussian-splat hologram.</summary>
        public const string XperienceStreamUrl = "https://genxr-streaming-media.sfo3.cdn.digitaloceanspaces.com/demos/3-5d-xperience/stream.json";

        [MenuItem("ImmersiveX/Demos/3.5D Xperience (streamed hologram)", false, 20)]
        static void CreateXperienceMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                TryCreateXperience();
        }

        /// <summary>
        /// Build <c>Assets/Demos/Xperience35D/Xperience35D.unity</c>: the ImmersiveX rig plus Immersive Media streaming the
        /// 3.5D Xperience at base quality, 1.60 m tall, at the centre of the room, with media controls. It waits paused for
        /// the Play button and plays once. The scene becomes the first in the build. Add more media with Add Media.
        /// </summary>
        public static bool TryCreateXperience()
        {
            if (!ImmersiveXSceneBuilder.CreateScene(XperienceScenePath, withStarterContent: false, addToBuild: true))
                return false;

            var media = new GameObject("3.5D Xperience").AddComponent<ImmersiveMedia>();
            var serialized = new SerializedObject(media);
            serialized.FindProperty("_source").stringValue = XperienceStreamUrl;
            serialized.FindProperty("_quality").stringValue = "base";
            serialized.FindProperty("_title").stringValue = "3.5D Xperience";
            serialized.FindProperty("_height").floatValue = 1.6f;
            serialized.FindProperty("_playOnStart").boolValue = false; // waits for the Play button
            serialized.FindProperty("_loop").boolValue = false;        // plays once; Play starts it again
            serialized.FindProperty("_spot").intValue = 0;             // the centre of the room
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), XperienceScenePath);
            ImmersiveXLog.Info($"Created {XperienceScenePath} (first scene in the build).");
            return true;
        }
    }
}
