using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// Automation commands for the host that bare metal content uses (see <see cref="EditorAutomation"/>), in Play mode:
    /// <list type="bullet">
    /// <item><c>host status</c>: the session, the plan, room access, the camera, anchors, depth ray casts and the view</item>
    /// <item><c>host camera start|stop</c>: the headset's camera (the simulated one in the editor)</item>
    /// <item><c>host capture [max size]</c>: a picture from the camera, saved as <c>host-camera.jpg</c> beside <c>result.txt</c></item>
    /// <item><c>host raycast [u v] [real|virtual|all]</c>: a world ray cast through a camera viewport point (default the middle;
    /// from the head when the camera is off)</item>
    /// <item><c>host map</c>: map the room · <c>host view mr|vr</c>: switch the view</item>
    /// <item><c>host target x y z [size]</c>: a test cube · <c>host swipe right|left [back]</c>: a swipe, to the app's swipe watchers · <c>host press &lt;label&gt;</c>: click a panel button · <c>host anchors</c>: add, save, load and erase an anchor</item>
    /// </list>
    /// In Edit mode, <c>host scene &lt;path&gt; [component …] [+camera] [+map] [+menu] [-room] [name=App name]</c> creates a bare
    /// metal scene whose app object has the named components and options (<see cref="BareMetalScene"/>), and leaves it open.
    /// </summary>
    [InitializeOnLoad]
    static class HostAutomation
    {
        const string CaptureFile = EditorAutomation.Folder + "/host-camera.jpg";

        static HostAutomation() => EditorAutomation.RegisterCommand("host", Run);

        static void Run(string command, string argument)
        {
            var parts = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            if (verb == "scene")
            {
                CreateScene(command, argument, parts);
                return;
            }

            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("'host' commands run in Play mode ('play 0' first).");

            switch (verb)
            {
                case "status":
                    EditorAutomation.Report(command, "ok", ImmersiveHost.Describe());
                    break;

                case "camera":
                    if (parts.Length > 1 && parts[1] == "stop")
                    {
                        ImmersiveHost.PassthroughCamera.Stop();
                        EditorAutomation.Report(command, "ok", ImmersiveHost.PassthroughCamera.Status);
                    }
                    else
                        StartCamera(command);
                    break;

                case "capture":
                    Capture(command, parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1024);
                    break;

                case "raycast":
                {
                    var viewport = parts.Length > 2
                        ? new Vector2(float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture))
                        : new Vector2(0.5f, 0.5f);
                    var surfaces = parts.Length > 0 && parts[parts.Length - 1] == "virtual" ? WorldSurfaces.Virtual
                        : parts.Length > 0 && parts[parts.Length - 1] == "real" ? WorldSurfaces.Real
                        : WorldSurfaces.All;
                    var camera = ImmersiveHost.PassthroughCamera;
                    var ray = camera.IsRunning ? camera.ViewportToRay(viewport) : new Ray(ImmersiveHost.HeadPose.position, ImmersiveHost.HeadPose.forward);
                    var hit = ImmersiveHost.Raycast(ray, out var found, 10f, surfaces);
                    EditorAutomation.Report(command, "ok", $"Ray from {ray.origin:F2} towards {ray.direction:F2} ({(camera.IsRunning ? "camera" : "head")}, {surfaces}): " +
                                                         (hit ? $"{found}{(found.Collider != null ? $" · {found.Collider.name}" : string.Empty)}." : "nothing within 10 m."));
                    break;
                }

                case "map":
                    var started = ImmersiveHost.MapRoom();
                    EditorAutomation.Report(command, started ? "ok" : "error", started ? "Room mapping started (see the play log)."
                        : "Room mapping didn't start: no room access, a platform without room data, or it's already mapping.");
                    break;

                case "view":
                    ImmersiveHost.SetView(parts.Length > 1 && parts[1] == "vr" ? ViewMode.Virtual : ViewMode.Mixed);
                    EditorAutomation.Report(command, "ok", $"View: {ImmersiveHost.View}.");
                    break;

                case "target":
                {
                    // "host target x y z [size]": a test cube in the room (world metres), for the simulated camera and ray casts
                    if (parts.Length < 4)
                        throw new ArgumentException("Usage: host target <x> <y> <z> [size]");
                    var position = new Vector3(float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture));
                    var size = parts.Length > 4 ? float.Parse(parts[4], CultureInfo.InvariantCulture) : 0.3f;
                    var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    target.name = "Host Test Target";
                    target.transform.position = position;
                    target.transform.localScale = Vector3.one * size;
                    target.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Create(new Color(0.2f, 0.6f, 0.95f));
                    EditorAutomation.Report(command, "ok", $"A {size:0.##} m test cube at {position:F2}.");
                    break;
                }

                case "anchors":
                    AnchorCycle(command);
                    break;

                case "swipe":
                {
                    // "host swipe right|left [back]": the swipe, to the app's own swipe watchers (ImmersiveHost.CreateSwipes)
                    var hand = parts.Length > 1 && parts[1] == "left"
                        ? UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left
                        : UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right;
                    var way = parts.Length > 2 && parts[2] == "back" ? SwipeDirection.Outward : SwipeDirection.Inward;
                    var watchers = UnityEngine.Object.FindObjectsByType<HandSwipes>(FindObjectsSortMode.None)
                        .Where(s => s.GetComponentInParent<ContentMenu>() == null).ToList();
                    var recognised = watchers.Count(s => s.Simulate(hand, way));
                    EditorAutomation.Report(command, watchers.Count > 0 ? "ok" : "error",
                        $"{hand} hand swiped {(way == SwipeDirection.Outward ? "back" : "across")}: recognised by {recognised} of {watchers.Count} app swipe watcher(s).");
                    break;
                }

                case "press":
                {
                    // "host press <label>": click the app's panel button with this label, as the ray or a finger does
                    var label = argument.Substring(argument.IndexOf("press", StringComparison.Ordinal) + 5).Trim();
                    var button = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
                        .FirstOrDefault(b => b.isActiveAndEnabled && b.GetComponentsInChildren<UnityEngine.UI.Text>()
                            .Any(t => string.Equals(t.text, label, StringComparison.OrdinalIgnoreCase)));
                    if (button == null)
                        throw new ArgumentException($"No active button labelled '{label}'.");
                    button.onClick.Invoke();
                    EditorAutomation.Report(command, "ok", $"Pressed '{label}' on {button.transform.root.name}.");
                    break;
                }

                default:
                    throw new ArgumentException("Usage: host status|camera start|camera stop|capture [size]|raycast [u v] [real|virtual|all]|map|view mr|vr|" +
                                                "target x y z [size]|swipe right|left [back]|press <label>");
            }
        }

        static void CreateScene(string command, string argument, string[] parts)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play mode first ('stop').");
            if (parts.Length < 2)
                throw new ArgumentException("Usage: host scene <path> [component …] [name=App name]");
            var named = argument.IndexOf("name=", StringComparison.Ordinal);
            var name = named >= 0 ? argument.Substring(named + 5).Trim() : System.IO.Path.GetFileNameWithoutExtension(parts[1]);
            var app = BareMetalScene.Create(parts[1], name, addToBuild: false);
            if (app == null)
            {
                EditorAutomation.Report(command, "retry", "The rig samples were just imported; run the same command again once scripts compile.");
                return;
            }

            var added = new System.Collections.Generic.List<string>();
            var content = app.GetComponent<BareMetalContent>();
            foreach (var typeName in parts.Skip(2).TakeWhile(p => !p.StartsWith("name=", StringComparison.Ordinal)))
            {
                // options: +camera (Passthrough Camera), +map (Map Room At Start), +menu (Content Menu), -room (no Room Access)
                switch (typeName)
                {
                    case "+camera": content.PassthroughCamera = true; added.Add("cameras on"); continue;
                    case "+map": content.MapRoomAtStart = true; added.Add("room mapped at start"); continue;
                    case "+menu": content.ContentMenu = true; added.Add("content menu on"); continue;
                    case "-room": content.RoomAccess = false; added.Add("no room access"); continue;
                }

                var type = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
                if (type == null)
                    throw new ArgumentException($"No component called '{typeName}'.");
                app.AddComponent(type);
                added.Add(type.Name);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(app.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(app.scene);
            EditorAutomation.Report(command, "ok", $"Created {parts[1]}: '{app.name}' with Bare Metal Content{(added.Count > 0 ? ", " + string.Join(", ", added) : string.Empty)}.");
        }

        /// <summary>Add an anchor a metre ahead, save it, load it back and erase it, reporting each step.</summary>
        static async void AnchorCycle(string command)
        {
            EditorAutomation.Report(command, "running", "Anchoring…");
            var steps = new System.Text.StringBuilder();
            try
            {
                var anchors = ImmersiveHost.Anchors;
                var user = ImmersiveHost.UserPose;
                var anchor = await anchors.AddAsync(UserRelativePlacement.Apply(user, new Vector3(0f, -0.5f, 1f)));
                var added = anchor != null;
                steps.Append($"add: {(added ? $"ok at {anchor.transform.position:F2}" : "failed")}");
                var id = added ? await anchors.SaveAsync(anchor) : null;
                steps.Append($" · save: {(id != null ? $"ok, {ContentAnchor.Short(id)}" : anchors.CanPersist ? "failed" : "not supported")}");
                if (id != null)
                {
                    anchors.Remove(anchor);
                    var loaded = await anchors.LoadAsync(id);
                    steps.Append($" · load: {(loaded != null ? $"ok at {loaded.transform.position:F2}" : "not found")}");
                    steps.Append($" · erase: {(await anchors.EraseAsync(id) ? "ok" : "failed")}");
                    anchors.Remove(loaded);
                }
                else
                    anchors.Remove(anchor);

                EditorAutomation.Report(command, added ? "ok" : "error", steps.ToString());
            }
            catch (Exception exception)
            {
                EditorAutomation.Report(command, "error", $"{steps} · {exception.GetBaseException().Message}");
            }
        }

        static async void StartCamera(string command)
        {
            EditorAutomation.Report(command, "running", "Starting the camera…");
            var started = await ImmersiveHost.PassthroughCamera.StartAsync();
            EditorAutomation.Report(command, started ? "ok" : "error", ImmersiveHost.PassthroughCamera.Status);
        }

        static async void Capture(string command, int maxSize)
        {
            EditorAutomation.Report(command, "running", "Taking a picture…");
            try
            {
                var camera = ImmersiveHost.PassthroughCamera;
                if (!camera.IsRunning && !await camera.StartAsync())
                {
                    EditorAutomation.Report(command, "error", camera.Status);
                    return;
                }

                using (var frame = await camera.CaptureAsync(maxSize))
                {
                    if (frame == null)
                    {
                        EditorAutomation.Report(command, "error", "No picture.");
                        return;
                    }

                    File.WriteAllBytes(CaptureFile, frame.EncodeToJpg());
                    EditorAutomation.Report(command, "ok", $"{frame.Width}×{frame.Height} picture from {frame.Pose.position:F2} facing " +
                                                           $"{frame.Pose.rotation * Vector3.forward:F2}; {frame.Intrinsics}: {CaptureFile}");
                }
            }
            catch (Exception exception)
            {
                EditorAutomation.Report(command, "error", exception.GetBaseException().Message);
            }
        }
    }
}
