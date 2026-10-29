using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// Lets scripts, CI and coding agents drive an <b>open</b> editor: write one command to
    /// <c>Library/ImmersiveX/Automation/command.txt</c>; the editor runs it and writes <c>result.txt</c>
    /// (plus <c>play-log.txt</c> and <c>play.png</c> for Play-mode runs). Everything runs through the same
    /// code as the ImmersiveX menus, so you can watch it happen in the editor.
    /// </summary>
    /// <remarks>
    /// Commands:
    /// <list type="bullet">
    /// <item><c>refresh</c>: import changed files and recompile · <c>resolve</c>: re-resolve packages · <c>test [editmode|playmode]</c>: run the Unity tests</item>
    /// <item><c>play [seconds]</c>: enter Play mode, record the log, take a screenshot, stop after the given time (default 20; 0 = keep playing until <c>stop</c>)</item>
    /// <item><c>capture</c>: screenshot the Game view now</item>
    /// <item><c>invoke rescan|forget|showarea|saveanchor|loadanchor|eraseanchor|scandone|scanskip</c>: press a Device Check or scan button</item>
    /// <item><c>invoke vr|mr|view</c>: switch to VR or MR as the content menu's buttons do, or say which is on</item>
    /// <item><c>invoke reach</c>: what each hand and controller could grab from where it is now (rays only click) · <c>invoke uirays</c>: can a ray click every panel button</item>
    /// <item><c>contentmenu status|open|close|toggle|select &lt;n&gt;|scroll up|down [rows]|space|back|recreate|device|swipe right|left|both</c> (or <c>cm …</c>): the content menu (<c>select</c> an app to open its page; <c>back</c> from Space or an app's page)</item>
    /// <item><c>prompt primary|secondary</c>: press a button on the ImmersiveX prompt that's showing</item>
    /// <item><c>turn &lt;degrees&gt; &lt;seconds&gt;</c>: turn the rig on the spot (Play mode) to test the guided look-around's coverage and coaching</item>
    /// <item>Commands other packages register with <see cref="RegisterCommand"/>, e.g. ImmersiveX Media's <c>media …</c> and <c>demo</c></item>
    /// <item><c>stop</c>: leave Play mode</item>
    /// <item><c>playmode xr-simulation|metaquest-simulator</c>: choose the Play-mode runtime</item>
    /// <item><c>configure &lt;platform&gt;</c>, <c>build &lt;platform&gt;</c>, <c>buildrun &lt;platform&gt;</c>: Platform Setup actions</item>
    /// <item><c>open &lt;scene path&gt;</c>: open a scene · <c>scene</c>: recreate the starter scene · <c>baseline</c>: apply the project baseline</item>
    /// <item><c>menu &lt;path&gt;</c>: run any editor menu item</item>
    /// </list>
    /// </remarks>
    [InitializeOnLoad]
    public static class EditorAutomation
    {
        public const string Folder = "Library/ImmersiveX/Automation";
        const string CommandFile = Folder + "/command.txt";
        const string ResultFile = Folder + "/result.txt";
        const string PlayLogFile = Folder + "/play-log.txt";
        const string ScreenshotFile = Folder + "/play.png";

        // Survive the domain reload that happens when Play mode starts.
        const string PlayRunKey = "ImmersiveX.Automation.PlayRun";
        const string PlaySecondsKey = "ImmersiveX.Automation.PlaySeconds";
        const string PlayStartKey = "ImmersiveX.Automation.PlayStart";
        const string PlayStageKey = "ImmersiveX.Automation.PlayStage";

        /// <summary>Set by the Test Framework integration: runs "editmode" or "playmode" tests and reports (status, details).</summary>
        internal static System.Action<string, System.Action<string, string>> TestRunner;

        static readonly object LogLock = new object();
        static float _turnRemaining;
        static float _turnSpeed;
        static double _lastTurnTime;
        static Vector3 _walkRemaining;
        static float _walkSpeed;
        static double _lastWalkTime;
        static int _errors;
        static int _warnings;

        static EditorAutomation()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (UnityEditor.SessionState.GetBool(PlayRunKey, false) && EditorApplication.isPlaying)
                Application.logMessageReceivedThreaded += RecordLog;
        }

        static readonly Dictionary<string, Action<string, string>> Commands = new Dictionary<string, Action<string, string>>();

        /// <summary>
        /// Add a command (other packages do this, e.g. ImmersiveX Media's <c>media</c> and <c>demo</c>). The handler gets the
        /// whole command line and its argument, and reports with <see cref="Report"/>; exceptions become an error result.
        /// </summary>
        public static void RegisterCommand(string verb, Action<string, string> handler) => Commands[verb.ToLowerInvariant()] = handler;

        /// <summary>Write the result of a command to <c>result.txt</c>: status is "ok", "error" or "running".</summary>
        public static void Report(string command, string status, string details) => WriteResult(command, status, details);

        static void Poll()
        {
            if (UnityEditor.SessionState.GetBool(PlayRunKey, false))
                AdvancePlayRun();
            AdvanceTurn();
            AdvanceWalk();

            if (!File.Exists(CommandFile) || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var command = File.ReadAllText(CommandFile).Trim();
            if (command.Length == 0)
                return; // caught between the writer creating the file and writing it; read it next time
            File.Delete(CommandFile);
            try
            {
                Run(command);
            }
            catch (Exception exception)
            {
                WriteResult(command, "error", exception.GetBaseException().Message);
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Render the scene from a camera at x y z looking at tx ty tz (world metres, optional vertical field of view) into
        /// play.png: an overview of how content sits in the room, which the headset's own view can't give.
        /// </summary>
        static void CaptureFrom(string command, string argument)
        {
            var numbers = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => float.Parse(n, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (numbers.Length < 6)
                throw new ArgumentException("Usage: capture <x> <y> <z> <target x> <target y> <target z> [field of view]");
            var position = new Vector3(numbers[0], numbers[1], numbers[2]);
            var target = new Vector3(numbers[3], numbers[4], numbers[5]);
            var holder = new GameObject("Automation Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
            var texture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            try
            {
                var camera = holder.AddComponent<Camera>();
                if (Camera.main != null)
                    camera.CopyFrom(Camera.main); // the same renderer and layers as the headset's view
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.fieldOfView = numbers.Length > 6 ? numbers[6] : 60f;
                camera.nearClipPlane = 0.02f;
                camera.farClipPlane = 200f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
                var up = Mathf.Abs(Vector3.Dot((target - position).normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                holder.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, up));
                camera.targetTexture = texture;
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = texture;
                var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.GetFullPath(ScreenshotFile), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }

            WriteResult(command, "ok", $"Captured from {position:F2} towards {target:F2}: {ScreenshotFile}");
        }

        /// <summary>
        /// Every near-far and ray interactor: on, its hand, and whether it can point. With <paramref name="poke"/>, the hands'
        /// interactors get far casting back first (as the XRI hands rig does when a poke gesture ends), to show it's undone.
        /// </summary>
        static string DescribePointers(bool poke)
        {
            var origin = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null)
                return "No XR Origin.";
            var text = new StringBuilder();
            var guard = origin.GetComponent<HandPointers>();
            text.AppendLine(guard != null ? "Hand pointers: off for tracked hands (rule active)." : "Hand pointers: on (no rule).");
            foreach (var interactor in origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(true))
            {
                if (poke && interactor.transform.GetComponentsInParent<Transform>(true).Any(t => t.name.Contains("Hand")))
                    interactor.enableFarCasting = true;
                text.AppendLine($"{PathOf(interactor.transform)} · {interactor.handedness} · {(interactor.isActiveAndEnabled ? "on" : "off")} · far casting {(interactor.enableFarCasting ? "ON" : "off")}");
            }

            foreach (var ray in origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true))
                text.AppendLine($"{PathOf(ray.transform)} · {ray.handedness} · ray {(ray.enabled ? "ON" : "off")} · object {(ray.gameObject.activeInHierarchy ? "active" : "inactive")}");
            return text.ToString();
        }

        static string PathOf(Transform transform)
        {
            var path = transform.name;
            for (var t = transform.parent; t != null && t.parent != null; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }

        static void Run(string command)
        {
            var parts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            switch (verb)
            {
                case "refresh":
                    AssetDatabase.Refresh();
                    WriteResult(command, "ok", "Refresh requested; scripts recompile if anything changed.");
                    break;
                case "resolve":
                    UnityEditor.PackageManager.Client.Resolve();
                    WriteResult(command, "ok", "Package resolve requested (picks up new or changed packages and manifest.json).");
                    break;
                case "play":
                    StartPlayRun(command, float.TryParse(argument, out var seconds) ? seconds : 20f);
                    break;
                case "test":
                    if (TestRunner == null)
                        throw new InvalidOperationException("The Unity Test Framework package isn't installed.");
                    WriteResult(command, "running", "Running tests…");
                    TestRunner(string.IsNullOrEmpty(argument) ? "editmode" : argument, (status, details) => WriteResult(command, status, details));
                    break;
                case "capture":
                    if (argument.Length > 0)
                    {
                        CaptureFrom(command, argument); // "capture x y z tx ty tz [fov]": a still from another viewpoint
                        break;
                    }

                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(ScreenshotFile));
                    WriteResult(command, "ok", $"Screenshot requested: {ScreenshotFile}");
                    break;
                case "invoke":
                    Invoke(command, argument);
                    break;
                case "contentmenu":
                case "cm":
                    ContentMenuCommand(command, argument);
                    break;
                case "parts":
                    PartsCommand(command, argument);
                    break;
                case "prompt":
                    var prompt = PromptPanel.Active;
                    if (prompt == null)
                        throw new InvalidOperationException("No ImmersiveX prompt is showing.");
                    if (argument == "secondary")
                        prompt.PressSecondary();
                    else
                        prompt.PressPrimary();
                    WriteResult(command, "ok", $"Pressed the {(argument == "secondary" ? "secondary" : "primary")} button.");
                    break;
                case "turn":
                    StartTurn(command, argument);
                    break;
                case "walk":
                    StartWalk(command, argument);
                    break;
                case "stop":
                    UnityEditor.SessionState.SetBool(PlayRunKey, false);
                    EditorApplication.isPlaying = false;
                    WriteResult(command, "ok", "Play mode stopped.");
                    break;
                case "playmode":
                    var menu = argument == EditorPlayMode.XRSimulation
                        ? "ImmersiveX/Play Mode/XR Simulation (any device)"
                        : "ImmersiveX/Play Mode/Meta XR Simulator (Quest)";
                    var ran = EditorApplication.ExecuteMenuItem(menu);
                    WriteResult(command, ran && EditorPlayMode.Current == argument ? "ok" : "error", $"Play-mode runtime: {EditorPlayMode.Current}");
                    break;
                case "configure":
                    BuildCli.ConfigurePlatform(argument);
                    WriteResult(command, "ok", string.Join("\n", BuildCli.Find(argument).Validate()));
                    break;
                case "build":
                case "buildrun":
                    var report = BuildCli.BuildPlatform(argument, run: verb == "buildrun");
                    WriteResult(command, "ok", $"{report.summary.result} · {report.summary.outputPath}");
                    break;
                case "open":
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(argument);
                    WriteResult(command, "ok", $"Opened {argument}.");
                    break;
                case "scene":
                    WriteResult(command, ImmersiveXSceneBuilder.TryCreateStarterScene() ? "ok" : "retry", "See the Console for details.");
                    break;
                case "baseline":
                    ProjectBaseline.Apply();
                    WriteResult(command, "ok", "Project baseline applied.");
                    break;
                case "menu":
                    WriteResult(command, EditorApplication.ExecuteMenuItem(argument) ? "ok" : "error", argument);
                    break;
                default:
                    if (Commands.TryGetValue(verb, out var handler))
                        handler(command, argument);
                    else
                        WriteResult(command, "error", "Unknown command. See EditorAutomation's documentation for the list.");
                    break;
            }
        }

        static void Invoke(string command, string action)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Not in Play mode. Send 'play 0' first.");
            var session = ImmersiveXSession.Instance;
            if (session == null)
                throw new InvalidOperationException("No ImmersiveX Session in the scene.");

            if (action.StartsWith("pointers", StringComparison.Ordinal))
            {
                // "invoke pointers [poke]": every interactor's pointer; poke turns the hands' back on, as the rig's poke gesture does
                WriteResult(command, "ok", DescribePointers(action.Contains("poke")));
                return;
            }

            switch (action)
            {
                case "rescan":
                case "spacesetup": session.RescanRoom(); break;
                case "forget": session.ForgetRoom(); break;
                case "showarea": session.ShowWalkableArea(); break;
                case "scandone": session.FinishGuidedScan(); break;
                case "scanskip": session.SkipGuidedScan(); break;
                case "saveanchor": Panel().SaveAnchor(); break;
                case "loadanchor": Panel().LoadAnchor(); break;
                case "eraseanchor": Panel().EraseAnchor(); break;
                case "reach":
                    WriteResult(command, "ok", DescribeReach());
                    return;
                case "uirays":
                    var blocked = DescribeUiRays(out var report);
                    WriteResult(command, blocked == 0 ? "ok" : "error", report);
                    return;
                case "vr":
                case "mr":
                case "view": // "invoke vr|mr": switch the view mode, as the menu's VR and MR buttons do; "invoke view": which one
                    var modes = ViewModes.Instance ?? throw new InvalidOperationException("No view modes (this platform doesn't show the real world).");
                    if (action != "view")
                        modes.Set(action == "vr" ? ViewMode.Virtual : ViewMode.Mixed);
                    WriteResult(command, "ok", modes.Describe());
                    return;

                default: throw new ArgumentException($"Unknown action '{action}'.");
            }

            WriteResult(command, "ok", $"Invoked {action}. Session: {ImmersiveXSession.Instance?.State}. Room: {ImmersiveXSession.Instance?.RoomStatus}");
        }

        /// <summary>
        /// For every hand and controller interactor: which grabbable things it could select from where it is now. With the
        /// ray-only-clicks rule (<see cref="DirectGrabOnly"/>), only things within a hand's reach are listed.
        /// </summary>
        static string DescribeReach()
        {
            var text = new StringBuilder();
            var interactables = UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>(FindObjectsSortMode.None);
            var interactors = UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(FindObjectsSortMode.None);
            text.AppendLine($"{interactors.Length} interactor(s), {interactables.Length} interactable(s); click raycaster: " +
                            $"{(UnityEngine.Object.FindAnyObjectByType<ContentClickRaycaster>() != null ? "yes" : "no")}, click targets: {ContentClickTarget.Count}");
            foreach (var interactor in interactors)
            {
                if (!(interactor is UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor selector) || !interactor.isActiveAndEnabled)
                    continue;
                var where = $"• {interactor.name} ({interactor.GetType().Name}) at {HandPose.Position(selector):F2}: ";
                if (DirectGrabOnly.Touches(selector))
                {
                    text.AppendLine(where + "grabs only what it touches");
                    continue;
                }

                var reachable = interactables.Where(i => i is UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable s && i.isActiveAndEnabled && s.IsSelectableBy(selector))
                    .Select(i => i.name).ToArray();
                text.AppendLine(where + (reachable.Length > 0 ? "can grab " + string.Join(", ", reachable) : "can't grab anything from here"));
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Aims a ray from the user's head at every button on a world-space panel, as a controller's ray would: the rig's ray
        /// mask finds what's in the way, and the Event System says which button the click reaches. Returns how many buttons
        /// a ray can't click.
        /// </summary>
        static int DescribeUiRays(out string report)
        {
            var text = new StringBuilder();
            var head = ImmersiveXSession.Instance?.Origin?.Camera?.transform;
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (head == null || events == null)
            {
                report = "Needs Play mode with an ImmersiveX Session and an Event System.";
                return 1;
            }

            var far = UnityEngine.Object.FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>()?.farInteractionCaster
                as UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.CurveInteractionCaster;
            var mask = far != null ? (int)far.raycastMask : Physics.DefaultRaycastLayers;
            var blocked = 0;
            var results = new List<UnityEngine.EventSystems.RaycastResult>();
            foreach (var button in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
            {
                var canvas = button.GetComponentInParent<Canvas>();
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace || !button.isActiveAndEnabled)
                    continue;
                var target = button.transform.position;
                var direction = (target - head.position).normalized;
                var distance = Vector3.Distance(head.position, target);

                // What the controller's ray would hit before the button (anything an interactable owns blocks the click).
                var blockers = Physics.RaycastAll(head.position, direction, distance - 0.01f, mask, QueryTriggerInteraction.Ignore)
                    .Select(h => h.collider.name + (h.collider.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>() != null ? " (interactable)" : string.Empty))
                    .ToArray();

                var data = new UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceEventData(events)
                {
                    rayPoints = new List<Vector3> { head.position, head.position + direction * (distance + 1f) },
                    layerMask = mask,
                };
                results.Clear();
                events.RaycastAll(data, results);
                var first = results.Count > 0 ? results[0].gameObject : null;
                var reaches = first != null && UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(first) == button.gameObject;
                var interactableInWay = blockers.Any(b => b.EndsWith("(interactable)"));
                if (!reaches || interactableInWay)
                    blocked++;
                text.AppendLine($"• {canvas.transform.root.name} ▸ {button.name}: {(reaches && !interactableInWay ? "clickable" : "BLOCKED")}" +
                                $" · first UI hit: {(first != null ? first.name : "none")}" +
                                (blockers.Length > 0 ? $" · in the ray's way: {string.Join(", ", blockers)}" : string.Empty));
            }

            text.Insert(0, $"{blocked} button(s) a ray can't click. Ray mask: {mask}\n");
            report = text.ToString().TrimEnd();
            return blocked;
        }

        /// <summary>
        /// The content menu: <c>contentmenu status|open|close|toggle|select &lt;n&gt;|scroll up|down [rows]|device</c> (or <c>cm …</c>), and
        /// <c>contentmenu swipe right|left|both</c>, which plays a real swipe's path through the hand-swipe detector (both = a
        /// clap, which must not count), and <c>contentmenu button [ray|click]</c>, the menu button at the user's stomach.
        /// </summary>
        /// <summary>
        /// "parts [list|select &lt;n|title&gt;|move &lt;dx&gt; &lt;dy&gt; &lt;dz&gt;|press &lt;button&gt;|ray &lt;button&gt;|click &lt;button&gt;]": the selectable parts of the
        /// content that's showing. <c>press</c> invokes a button of the selected part's label directly; <c>ray</c> casts a ray
        /// from the right controller (else the head) at that button through the Event System and lists what it hits, nearest
        /// first, as XR UI would see it; <c>click</c> sends that ray's click (press and release) to whatever it hits first.
        /// </summary>
        static void PartsCommand(string command, string argument)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Not in Play mode. Send 'play 0' first.");
            var parts = UnityEngine.Object.FindObjectsByType<ContentParts>(FindObjectsSortMode.None).FirstOrDefault(p => p.isActiveAndEnabled && p.gameObject.activeInHierarchy)
                        ?? throw new InvalidOperationException("No content with selectable parts is showing.");
            var words = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var action = words.Length > 0 ? words[0].ToLowerInvariant() : "list";
            float Number(int i) => float.Parse(words[i], System.Globalization.CultureInfo.InvariantCulture);
            var text = new StringBuilder();
            switch (action)
            {
                case "list":
                    for (var i = 0; i < parts.Parts.Count; i++)
                    {
                        var p = parts.Parts[i];
                        text.AppendLine($"{i}. {p.Trail}{(p.IsRoot ? " (root)" : string.Empty)}{(p.IsMoved ? " · moved" : string.Empty)}{(p.IsHeld ? " · held" : string.Empty)}{(p == parts.Selected ? " · SELECTED" : string.Empty)}");
                    }

                    break;
                case "select":
                    var pick = words.Length > 1 ? string.Join(" ", words.Skip(1)) : string.Empty;
                    parts.Select(int.TryParse(pick, out var index) && index >= 0 && index < parts.Parts.Count ? parts.Parts[index] : parts.Find(pick));
                    break;
                case "move":
                    var moving = parts.Selected ?? throw new InvalidOperationException("Select a part first.");
                    parts.MoveTo(moving, new Pose(moving.transform.position + new Vector3(Number(1), Number(2), Number(3)), moving.transform.rotation));
                    break;
                case "press":
                case "ray":
                case "click":
                    var name = words.Length > 1 ? string.Join(" ", words.Skip(1)) : "Put back";
                    var button = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
                        .FirstOrDefault(b => b.isActiveAndEnabled && b.GetComponentInParent<Canvas>() != null && b.transform.root.name == "Part Label" &&
                                             b.GetComponentInChildren<UnityEngine.UI.Text>()?.text == name)
                        ?? throw new InvalidOperationException($"No '{name}' button on a part label is showing.");
                    text.AppendLine($"'{name}' at {button.transform.position:F3}, {(button.interactable ? "enabled" : "DISABLED")}.");
                    if (action == "press")
                    {
                        button.onClick.Invoke();
                        break;
                    }

                    text.Append(RayAt(button.transform, action == "click"));
                    break;
                default:
                    throw new ArgumentException($"Unknown parts action '{action}'.");
            }

            var selected = parts.Selected;
            text.Append($"{parts.Parts.Count} parts · any moved: {parts.AnyMoved} · selected: {(selected != null ? selected.Trail + (selected.IsMoved ? " (moved)" : string.Empty) : "none")}");
            WriteResult(command, "ok", text.ToString());
        }

        /// <summary>What a ray from the right controller (else the head) at <paramref name="target"/> hits through the Event System, nearest first.</summary>
        static string RayAt(Transform target, bool click)
        {
            var events = UnityEngine.EventSystems.EventSystem.current ?? throw new InvalidOperationException("No Event System.");
            var origin = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            var rightHand = UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(FindObjectsSortMode.None)
                .FirstOrDefault(i => i.isActiveAndEnabled && i.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right);
            if (rightHand != null)
                origin = rightHand.transform.position;
            var direction = (target.position - origin).normalized;
            var data = new UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceEventData(events)
            {
                rayPoints = new List<Vector3> { origin, origin + direction * 10f },
                layerMask = ~0,
            };
            var results = new List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(data, results);
            var text = new StringBuilder($"Ray from {origin:F2} towards it hits {results.Count}:\n");
            foreach (var hit in results.Take(8))
                text.AppendLine($"  {PathOf(hit.gameObject.transform)} · {hit.module.GetType().Name} · {hit.distance:F3} m");
            if (click && results.Count > 0)
            {
                var first = results[0];
                data.pointerCurrentRaycast = first;
                data.pointerPressRaycast = first;
                var handler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(first.gameObject);
                text.AppendLine($"Clicked: {(handler != null ? PathOf(handler.transform) : "nothing clickable")}.");
                if (handler != null)
                {
                    data.pointerPress = handler;
                    data.rawPointerPress = first.gameObject;
                    UnityEngine.EventSystems.ExecuteEvents.Execute(handler, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                    UnityEngine.EventSystems.ExecuteEvents.Execute(handler, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                    UnityEngine.EventSystems.ExecuteEvents.Execute(handler, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                }
            }

            return text.ToString();
        }

        static void ContentMenuCommand(string command, string argument)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Not in Play mode. Send 'play 0' first.");
            var menu = ContentMenu.Instance;
            if (menu == null)
                throw new InvalidOperationException("No content menu (the session isn't ready, or Settings ▸ Content Menu is off).");
            var parts = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var action = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            var note = string.Empty;
            switch (action)
            {
                case "status": break;
                case "open": menu.Open(); break;
                case "close": menu.Close(); break;
                case "toggle": menu.Toggle(); break;
                case "device": menu.OpenDeviceMenu(); break;
                case "button": // "contentmenu button [ray|click]": press the menu button, or cast the right hand's ray at it (and click)
                    var how = parts.Length > 1 ? parts[1].ToLowerInvariant() : "press";
                    if (how == "press")
                        menu.Button.Button.onClick.Invoke();
                    else
                        note = RayAt(menu.Button.transform, how == "click") + "\n";
                    break;
                case "move": // "contentmenu move x y z": put the open menu somewhere else (as if the user moved it there)
                    if (!menu.IsOpen || parts.Length < 4)
                        throw new ArgumentException("Usage: contentmenu move <x> <y> <z> (with the menu open)");
                    menu.Panel.position = new Vector3(float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "space": menu.OpenSpace(); note = $"Space panel: {menu.Space.Says}. "; break; // the footer's Space button
                case "back": // the Space panel's Back, or an app page's Content (back to the list)
                    if (menu.Space != null && menu.Space.IsShowing)
                        menu.BackFromSpace();
                    else
                        menu.BackToContent();
                    break;
                case "recreate": menu.RecreateSpace(); break; // the Space panel's Re-create space
                case "scroll": // "contentmenu scroll <up|down> [rows]": what the scroll buttons do
                    if (parts.Length < 2 || (parts[1] != "up" && parts[1] != "down"))
                        throw new ArgumentException("Usage: contentmenu scroll <up|down> [rows]");
                    menu.Scroll((parts[1] == "up" ? -1 : 1) * (parts.Length > 2 && int.TryParse(parts[2], out var by) ? by : 1));
                    break;
                case "select":
                    if (parts.Length < 2 || !int.TryParse(parts[1], out var n) || menu.ItemAt(n - 1) == null)
                        throw new ArgumentException($"Usage: menu select <1-{menu.Items.Count}>");
                    menu.Pick(menu.ItemAt(n - 1)); // as its row does: an app opens its page
                    break;
                case "swipe": // "contentmenu swipe <right|left|both> [back]": the opening swipe, or its reverse with "back"
                    var which = parts.Length > 1 ? parts[1].ToLowerInvariant() : "right";
                    var way = parts.Length > 2 && parts[2].ToLowerInvariant() == "back" ? SwipeDirection.Outward : SwipeDirection.Inward;
                    var right = which == "right" || which == "both" ? menu.Swipes.Simulate(UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right, way) : (bool?)null;
                    var left = which == "left" || which == "both" ? menu.Swipes.Simulate(UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left, way) : (bool?)null;
                    note = $"swipe {(way == SwipeDirection.Outward ? "back" : "across")} recognised: right {(right.HasValue ? right.Value ? "yes" : "no" : "-")}, left {(left.HasValue ? left.Value ? "yes" : "no" : "-")}" +
                           " (the menu reacts 0.15 s later, unless both hands swiped). ";
                    break;
                default: throw new ArgumentException($"Unknown menu action '{action}'.");
            }

            var where = menu.IsOpen ? $"at {menu.Panel.position:F2}, {(menu.IsInFront() ? "in front of the user" : "away from the user")} · " : string.Empty;
            WriteResult(command, "ok", note + where + menu.Describe());
        }

        static Diagnostics.DeviceCheckPanel Panel()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<Diagnostics.DeviceCheckPanel>();
            if (panel == null)
                throw new InvalidOperationException("No Device Check Panel in the scene.");
            return panel;
        }

        static void StartTurn(string command, string argument)
        {
            if (!EditorApplication.isPlaying || ImmersiveXSession.Instance == null || ImmersiveXSession.Instance.Origin == null)
                throw new InvalidOperationException("Turning needs Play mode with an ImmersiveX Session.");
            var parts = argument.Split(' ');
            if (parts.Length < 2 || !float.TryParse(parts[0], out var degrees) || !float.TryParse(parts[1], out var seconds) || seconds <= 0f)
                throw new ArgumentException("Usage: turn <degrees> <seconds>");
            _turnRemaining = degrees;
            _turnSpeed = degrees / seconds;
            _lastTurnTime = EditorApplication.timeSinceStartup;
            WriteResult(command, "ok", $"Turning {degrees:0}° over {seconds:0.#}s.");
        }

        static void AdvanceTurn()
        {
            if (Mathf.Approximately(_turnRemaining, 0f))
                return;
            var session = ImmersiveXSession.Instance;
            if (!EditorApplication.isPlaying || session == null || session.Origin == null)
            {
                _turnRemaining = 0f;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var step = _turnSpeed * (float)(now - _lastTurnTime);
            _lastTurnTime = now;
            if (Mathf.Abs(step) > Mathf.Abs(_turnRemaining))
                step = _turnRemaining;
            _turnRemaining -= step;

            // Turn the rig around the user's head, as if they turned on the spot. This exercises the guided
            // look-around (coverage, coaching, prompt follow). XR Simulation's simulated room scanner only follows
            // the simulated device, which is moved with the mouse and keyboard in the Game view.
            session.Origin.transform.RotateAround(session.Origin.Camera.transform.position, Vector3.up, step);
        }

        static void StartWalk(string command, string argument)
        {
            if (!EditorApplication.isPlaying || ImmersiveXSession.Instance == null || ImmersiveXSession.Instance.Origin == null)
                throw new InvalidOperationException("Walking needs Play mode with an ImmersiveX Session.");
            var parts = argument.Split(' ');
            if (parts.Length < 3 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0f)
                throw new ArgumentException("Usage: walk <metres right> <metres forward> <seconds>");
            _walkRemaining = new Vector3(x, 0f, z);
            _walkSpeed = _walkRemaining.magnitude / seconds;
            _lastWalkTime = EditorApplication.timeSinceStartup;
            WriteResult(command, "ok", $"Walking {_walkRemaining.magnitude:0.##} m over {seconds:0.#}s.");
        }

        /// <summary>Slide the rig across the floor in world space, as if the user walked (X right, Z forward in the world).</summary>
        static void AdvanceWalk()
        {
            if (_walkRemaining == Vector3.zero)
                return;
            var session = ImmersiveXSession.Instance;
            if (!EditorApplication.isPlaying || session == null || session.Origin == null)
            {
                _walkRemaining = Vector3.zero;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var step = Vector3.ClampMagnitude(_walkRemaining.normalized * _walkSpeed * (float)(now - _lastWalkTime), _walkRemaining.magnitude);
            _lastWalkTime = now;
            _walkRemaining -= step;
            if (_walkRemaining.sqrMagnitude < 1e-8f)
                _walkRemaining = Vector3.zero;
            session.Origin.transform.position += step;
        }

        // ---------------------------------------------------------------- Play-mode runs

        static void StartPlayRun(string command, float seconds)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Already in Play mode. Send 'stop' first.");

            Directory.CreateDirectory(Folder);
            File.WriteAllText(PlayLogFile, $"# {command} · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · runtime: {EditorPlayMode.Current}\n");
            File.Delete(ScreenshotFile);
            UnityEditor.SessionState.SetBool(PlayRunKey, true);
            UnityEditor.SessionState.SetFloat(PlaySecondsKey, seconds);
            UnityEditor.SessionState.SetInt(PlayStageKey, 0);
            UnityEditor.SessionState.SetString(PlayStartKey, string.Empty);
            WriteResult(command, "running", $"Play mode for {seconds:0}s on {EditorPlayMode.Current}…");
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!UnityEditor.SessionState.GetBool(PlayRunKey, false))
                return;

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                _errors = 0;
                _warnings = 0;
                Application.logMessageReceivedThreaded -= RecordLog;
                Application.logMessageReceivedThreaded += RecordLog;
                UnityEditor.SessionState.SetString(PlayStartKey, EditorApplication.timeSinceStartup.ToString("R"));
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                Application.logMessageReceivedThreaded -= RecordLog;
                UnityEditor.SessionState.SetBool(PlayRunKey, false);
            }
        }

        static void AdvancePlayRun()
        {
            if (!EditorApplication.isPlaying || !double.TryParse(UnityEditor.SessionState.GetString(PlayStartKey, string.Empty), out var start))
                return;

            var elapsed = EditorApplication.timeSinceStartup - start;
            var stage = UnityEditor.SessionState.GetInt(PlayStageKey, 0);
            var seconds = UnityEditor.SessionState.GetFloat(PlaySecondsKey, 20f);
            if (seconds <= 0f)
            {
                // Open-ended run: report once when the session is ready, then keep playing until 'stop'.
                var session = ImmersiveXSession.Instance;
                if (stage == 0 && (session == null || session.State == SessionState.Ready || session.State == SessionState.Failed || elapsed > 30))
                {
                    WriteResult("play 0", _errors > 0 ? "errors" : "ok", DescribeSession());
                    UnityEditor.SessionState.SetInt(PlayStageKey, 1);
                }
                return;
            }

            if (stage == 0 && elapsed >= seconds)
            {
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(ScreenshotFile));
                UnityEditor.SessionState.SetInt(PlayStageKey, 1);
            }
            else if (stage == 1 && elapsed >= seconds + 1.5)
            {
                WriteResult($"play {seconds:0}", _errors > 0 ? "errors" : "ok", DescribeSession());
                UnityEditor.SessionState.SetInt(PlayStageKey, 2);
                EditorApplication.isPlaying = false;
            }
        }

        static string DescribeSession()
        {
            var text = new StringBuilder();
            var session = ImmersiveXSession.Instance;
            if (session == null)
            {
                text.AppendLine("No ImmersiveX Session in the scene.");
            }
            else
            {
                text.AppendLine($"Session state: {session.State}{(session.FailureReason != null ? " — " + session.FailureReason : string.Empty)}");
                text.AppendLine($"Platform: {session.Platform?.DisplayName} ({session.Platform?.Id})");
                text.AppendLine($"Capabilities: {session.Platform?.Capabilities}");
                text.AppendLine($"Room access: {session.HasRoomAccess}");
            }

            text.AppendLine($"Play-mode runtime: {EditorPlayMode.Current}");
            text.AppendLine($"Console: {_errors} error(s), {_warnings} warning(s). Full log: {PlayLogFile}");
            text.Append($"Screenshot: {ScreenshotFile}");
            return text.ToString();
        }

        static void RecordLog(string message, string stackTrace, LogType type)
        {
            lock (LogLock)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    _errors++;
                else if (type == LogType.Warning)
                    _warnings++;

                var entry = $"[{DateTime.Now:HH:mm:ss.fff}] [{type}] {message}\n";
                if (type == LogType.Exception || type == LogType.Error)
                    entry += stackTrace;
                File.AppendAllText(PlayLogFile, entry);
            }
        }

        static void WriteResult(string command, string status, string details)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ResultFile, $"command: {command}\nstatus: {status}\ntime: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{details}\n");
            ImmersiveXLog.Info($"Automation '{command}': {status}");
        }
    }
}
