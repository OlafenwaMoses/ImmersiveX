using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>ImmersiveX ▸ Platform Setup: install a platform adapter, configure the project for it, and build.</summary>
    public sealed class PlatformSetupWindow : EditorWindow
    {
        List<PlatformInfo> _platforms = new List<PlatformInfo>();
        readonly Dictionary<string, string[]> _issues = new Dictionary<string, string[]>();
        Vector2 _scroll;

        [MenuItem("ImmersiveX/Platform Setup", false, -100)]
        static void Open() => GetWindow<PlatformSetupWindow>("Platform Setup").minSize = new Vector2(460f, 300f);

        void OnEnable() => Refresh();

        void OnFocus() => Refresh();

        void Refresh() => _platforms = PlatformCatalog.Load();

        void OnGUI()
        {
            EditorGUILayout.LabelField("Platforms", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. Install the adapter for your device.  2. Configure applies its settings.  3. Build writes the app to Builds/<platform>/;\n" +
                "Build And Run also installs and launches it on the connected device.", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var platform in _platforms)
                DrawPlatform(platform);
            if (_platforms.Count == 0)
                EditorGUILayout.HelpBox("No platforms found in Platforms/. Each adapter folder needs a platform.json.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        void DrawPlatform(PlatformInfo platform)
        {
            var installed = PlatformCatalog.IsInstalled(platform);
            var configurator = installed ? BuildCli.Find(platform.id) : null;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{platform.displayName}   ·   {platform.status}", EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(platform.summary))
                    EditorGUILayout.LabelField(platform.summary, EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(installed || platform.status == "stub"))
                    {
                        if (GUILayout.Button(installed ? "Installed" : "Install"))
                            PlatformCatalog.Install(platform);
                    }

                    using (new EditorGUI.DisabledScope(configurator == null || EditorApplication.isCompiling))
                    {
                        if (GUILayout.Button("Configure"))
                        {
                            BuildCli.ConfigurePlatform(platform.id);
                            _issues[platform.id] = configurator.Validate().ToArray();
                        }

                        if (GUILayout.Button("Build"))
                            EditorApplication.delayCall += () => BuildCli.BuildPlatform(platform.id);
                        if (GUILayout.Button(new GUIContent("Build And Run", "Build, install on the connected device and launch it")))
                            EditorApplication.delayCall += () => BuildCli.BuildPlatform(platform.id, run: true);
                    }

                    if (!string.IsNullOrEmpty(platform.docs) && GUILayout.Button("Guide"))
                        EditorUtility.OpenWithDefaultApp(Path.Combine(Directory.GetParent(Application.dataPath).FullName, platform.docs));
                }

                if (installed && configurator == null)
                    EditorGUILayout.HelpBox("Installed. Waiting for the Package Manager and compilation to finish…", MessageType.Info);

                if (_issues.TryGetValue(platform.id, out var issues))
                {
                    if (issues.Length == 0)
                        EditorGUILayout.HelpBox("Configured. No remaining issues.", MessageType.Info);
                    foreach (var issue in issues)
                        EditorGUILayout.HelpBox(issue, MessageType.Warning);
                }
            }
        }
    }
}
