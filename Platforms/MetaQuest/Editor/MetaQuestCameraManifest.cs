using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ImmersiveX.Editor;
using UnityEditor;
using UnityEditor.Android;

namespace ImmersiveX.Platforms.MetaQuest.Editor
{
    /// <summary>
    /// The Quest camera permissions, only in apps that read the passthrough cameras: bare metal content in the build's
    /// scenes with <c>Passthrough Camera</c> on (see <see cref="BareMetalBuild"/>).
    /// <list type="bullet">
    /// <item>Such an app gets <c>android.permission.CAMERA</c> and <c>horizonos.permission.HEADSET_CAMERA</c> (the Passthrough
    /// Camera API), and any camera feature is marked not required, since Quest lists none.</item>
    /// <item>Every other app loses the camera permission and features Unity adds because the Quest adapter can use a
    /// <c>WebCamTexture</c>, so it never asks for, or is reviewed for, a camera it doesn't use.</item>
    /// </list>
    /// </summary>
    sealed class MetaQuestCameraManifest : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        static readonly string[] Permissions = { "android.permission.CAMERA", "horizonos.permission.HEADSET_CAMERA" };

        // After Unity OpenXR's manifest changes and the store manifest's.
        public int callbackOrder => 10010;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                return;
            var camera = BareMetalBuild.UsesPassthroughCamera();
            var main = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var others = new[]
            {
                Path.Combine(path, "xrmanifest.androidlib", "AndroidManifest.xml"),
                Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"),
            };
            foreach (var file in new[] { main }.Concat(others).Where(File.Exists))
                Apply(file, camera, addPermissions: file == main);
        }

        static void Apply(string file, bool camera, bool addPermissions)
        {
            var xml = new XmlDocument { PreserveWhitespace = true };
            xml.Load(file);
            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("android", AndroidNs);
            var manifest = (XmlElement)xml.SelectSingleNode("/manifest");
            if (manifest == null)
                return;
            var changes = new List<string>();

            foreach (var permission in Permissions)
            {
                var present = xml.SelectNodes($"/manifest/uses-permission[@android:name='{permission}']", ns).Cast<XmlElement>().ToList();
                if (camera && present.Count == 0 && addPermissions)
                {
                    var entry = xml.CreateElement("uses-permission");
                    entry.SetAttribute("name", AndroidNs, permission);
                    manifest.AppendChild(entry);
                    changes.Add($"+{permission}");
                }
                else if (!camera)
                    foreach (var entry in present)
                    {
                        manifest.RemoveChild(entry);
                        changes.Add($"-{permission}");
                    }
            }

            foreach (var feature in xml.SelectNodes("/manifest/uses-feature", ns).Cast<XmlElement>().ToList())
            {
                var name = feature.GetAttribute("name", AndroidNs);
                if (!name.StartsWith("android.hardware.camera"))
                    continue;
                if (!camera)
                {
                    manifest.RemoveChild(feature);
                    changes.Add($"-{name}");
                }
                else if (feature.GetAttribute("required", AndroidNs) != "false")
                {
                    feature.SetAttribute("required", AndroidNs, "false");
                    changes.Add($"{name} not required");
                }
            }

            if (changes.Count == 0)
                return;
            xml.Save(file);
            ImmersiveXLog.Info($"Meta Quest camera manifest ({(camera ? "the app reads the passthrough cameras" : "no camera in this app")}): {string.Join("; ", changes)}.");
        }
    }
}
