using System;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// <b>ImmersiveX ▸ Media ▸ Add Media…</b>: pick a file, a folder of frames or a URL, and it's made ready for the headset
    /// and added to the open scene as Immersive Media (see <see cref="MediaImport"/>). No code.
    /// </summary>
    sealed class AddMediaWindow : EditorWindow
    {
        string _source = string.Empty;
        readonly MediaImport.Options _options = new MediaImport.Options();

        [MenuItem("ImmersiveX/Media/Add Media…", false, 30)]
        static void Open()
        {
            var window = GetWindow<AddMediaWindow>(true, "Add Media", true);
            window.minSize = new Vector2(480f, 330f);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Pick a file, a folder of frames, or paste a URL. It's made ready for the headset, copied into StreamingAssets " +
                "(so it ships inside the app) and added to the open scene as Immersive Media. docs/media.md lists what each kind of content needs.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                _source = EditorGUILayout.TextField("Source", _source);
                if (GUILayout.Button("File…", GUILayout.Width(56f)))
                    Pick(EditorUtility.OpenFilePanel("Add Media: pick a file", string.Empty, string.Empty));
                if (GUILayout.Button("Folder…", GUILayout.Width(64f)))
                    Pick(EditorUtility.OpenFolderPanel("Add Media: pick a folder of frames", string.Empty, string.Empty));
            }

            var summary = MediaImport.Describe(_source, out var isFolder, out var isSplatFrames);
            EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            _options.Title = EditorGUILayout.TextField(new GUIContent("Title", "The object's name and what logs call it. Empty: from the file name."), _options.Title);
            _options.Format = (MediaFormat)EditorGUILayout.EnumPopup(new GUIContent("Format", "Auto detects it. Set 360°/180° for a panorama with no hint in its name."), _options.Format);
            _options.Stereo = (StereoLayout)EditorGUILayout.EnumPopup(new GUIContent("Stereo", "360°/180° video and photos: how the frame is split between the eyes."), _options.Stereo);
            _options.Up = (UpAxis)EditorGUILayout.EnumPopup(new GUIContent("Up axis", "Which way is up in the file. Auto uses the format's usual convention."), _options.Up);
            if (isFolder)
                _options.FrameRate = Mathf.Max(1f, EditorGUILayout.FloatField(new GUIContent("Frame rate", "Frames per second. Captures don't always say; 30 is common."), _options.FrameRate));
            if (isSplatFrames)
            {
                _options.PackSplats = EditorGUILayout.Toggle(new GUIContent("Pack for the headset", "17 bytes a Gaussian, uploaded as is. Off copies the raw frames, which only suits small captures."), _options.PackSplats);
                if (_options.PackSplats)
                    _options.MaxGaussians = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Max Gaussians a frame", "0 keeps them all. Fewer means smaller files and faster frames."), _options.MaxGaussians));
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_source.Trim().Length == 0))
            {
                if (GUILayout.Button("Add to Scene", GUILayout.Height(30f)))
                    Add();
            }
        }

        void Pick(string path)
        {
            if (!string.IsNullOrEmpty(path))
                _source = path;
        }

        void Add()
        {
            try
            {
                var media = MediaImport.Add(_source, _options,
                    progress => !EditorUtility.DisplayCancelableProgressBar("Add Media", "Packing splat frames for the headset…", progress));
                ShowNotification(new GUIContent($"Added '{media.name}'"));
            }
            catch (OperationCanceledException)
            {
                ShowNotification(new GUIContent("Cancelled"));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Add Media", exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
