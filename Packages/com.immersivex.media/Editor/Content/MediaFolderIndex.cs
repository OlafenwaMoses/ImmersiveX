#if UNITY_ANDROID
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// Writes the index of StreamingAssets folders (<see cref="MediaFolders.IndexFile"/>) into Android builds. StreamingAssets
    /// is inside the APK there and can't be listed, so this is how a folder of frames can be a Source on the headset.
    /// </summary>
    sealed class MediaFolderIndex : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
                return;
            var assets = Path.Combine(path, "src", "main", "assets");
            Directory.CreateDirectory(assets);
            File.WriteAllText(Path.Combine(assets, MediaFolders.IndexFile), MediaFolders.BuildIndex(Application.streamingAssetsPath));
        }
    }
}
#endif
