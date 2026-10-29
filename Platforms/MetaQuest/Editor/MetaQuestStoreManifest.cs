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
    /// Makes every Quest build's manifest meet Meta's "Application Manifests for Release Builds", so the APK passes the
    /// Meta Horizon Store's upload checks. It runs after Unity and Unity OpenXR: Meta have written the Gradle project:
    /// <list type="bullet">
    /// <item>the launch activity is excluded from recents (<c>android:excludeFromRecents="true"</c>) and landscape
    /// (<c>android:screenOrientation="landscape"</c>);</item>
    /// <item><c>com.oculus.supportedDevices</c> uses Meta's documented names (quest2, questpro, quest3, quest3s) for the
    /// devices Unity OpenXR: Meta lists by codename, without the original Quest, which can't run Android API 32;</item>
    /// <item><c>com.meta.store.defaultDeviceTargets</c> targets the oldest of those and every device after it
    /// (<c>quest2+</c>, <c>questpro+</c> or <c>quest3+</c>), so an upload doesn't default to the original Quest, and
    /// future devices are included (the LIVE channel needs a forward-compatible build). A value already in the
    /// manifest is kept.</item>
    /// <item>the app's data isn't backed up (<c>android:allowBackup="false"</c>), which the store's security
    /// vulnerability review asks for. Nothing ImmersiveX saves is worth restoring elsewhere: placements are held by
    /// spatial anchors in this headset's room.</item>
    /// </list>
    /// The install location ("auto"), the orientation and the SDK levels are also set in <see cref="MetaQuestBuildConfigurator"/>.
    /// </summary>
    sealed class MetaQuestStoreManifest : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string SupportedDevices = "com.oculus.supportedDevices";
        const string DefaultDeviceTargets = "com.meta.store.defaultDeviceTargets";

        // Meta's device identifiers, oldest first, with the device-targeting specifier for "this device and later".
        static readonly (string Device, string AndLater)[] DeviceOrder =
        {
            ("quest2", "quest2+"), ("questpro", "questpro+"), ("quest3", "quest3+"), ("quest3s", "quest3+"),
        };

        // Unity OpenXR: Meta's device values → Meta's documented identifiers (null: not a store target at API 32).
        static readonly Dictionary<string, string> DeviceNames = new Dictionary<string, string>
        {
            { "quest", null },
            { "quest2", "quest2" },
            { "cambria", "questpro" },
            { "questpro", "questpro" },
            { "eureka", "quest3" },
            { "quest3", "quest3" },
            { "quest3s", "quest3s" },
        };

        // After Unity OpenXR's own manifest changes.
        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                return;
            var manifests = new[]
            {
                Path.Combine(path, "src", "main", "AndroidManifest.xml"),
                Path.Combine(path, "xrmanifest.androidlib", "AndroidManifest.xml"), // Unity OpenXR's device and feature entries
                Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"),
            };
            foreach (var file in manifests.Where(File.Exists))
                Apply(file);
        }

        static void Apply(string file)
        {
            var xml = new XmlDocument { PreserveWhitespace = true };
            xml.Load(file);
            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("android", AndroidNs);
            var changes = new List<string>();

            // Every manifest that declares the application says the same, so merging them can't conflict.
            foreach (XmlElement application in xml.SelectNodes("/manifest/application", ns))
            {
                if (application.GetAttribute("allowBackup", AndroidNs) == "false")
                    continue;
                application.SetAttribute("allowBackup", AndroidNs, "false");
                changes.Add("app data not backed up");
            }

            foreach (XmlElement activity in xml.SelectNodes("/manifest/application/activity", ns))
            {
                var launches = activity.SelectSingleNode("intent-filter/action[@android:name='android.intent.action.MAIN']", ns) != null;
                if (!launches)
                    continue;
                if (activity.GetAttribute("excludeFromRecents", AndroidNs) != "true")
                {
                    activity.SetAttribute("excludeFromRecents", AndroidNs, "true");
                    changes.Add("launch activity excluded from recents");
                }

                if (activity.GetAttribute("screenOrientation", AndroidNs) != "landscape")
                {
                    activity.SetAttribute("screenOrientation", AndroidNs, "landscape");
                    changes.Add("launch activity landscape");
                }
            }

            foreach (XmlElement data in xml.SelectNodes($"/manifest/application/meta-data[@android:name='{SupportedDevices}']", ns))
            {
                var current = data.GetAttribute("value", AndroidNs);
                var devices = current.Split('|')
                    .Select(d => d.Trim().ToLowerInvariant())
                    .Select(d => DeviceNames.TryGetValue(d, out var name) ? name : d)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct()
                    .ToArray();
                var value = string.Join("|", devices);
                if (value != current)
                {
                    data.SetAttribute("value", AndroidNs, value);
                    changes.Add($"supported devices {current} → {value}");
                }

                var application = (XmlElement)data.ParentNode;
                var oldest = DeviceOrder.FirstOrDefault(d => devices.Contains(d.Device));
                var targets = application.SelectSingleNode($"meta-data[@android:name='{DefaultDeviceTargets}']", ns);
                if (oldest.AndLater == null || targets != null)
                    continue;
                var entry = xml.CreateElement("meta-data");
                entry.SetAttribute("name", AndroidNs, DefaultDeviceTargets);
                entry.SetAttribute("value", AndroidNs, oldest.AndLater);
                application.InsertAfter(entry, data);
                changes.Add($"default device targets {oldest.AndLater}");
            }

            if (changes.Count == 0)
                return;
            xml.Save(file);
            ImmersiveXLog.Info($"Meta Quest store manifest ({file.Replace('\\', '/').Split('/').Reverse().Skip(1).First(d => d != "main" && d != "src")}): {string.Join("; ", changes)}.");
        }
    }
}
