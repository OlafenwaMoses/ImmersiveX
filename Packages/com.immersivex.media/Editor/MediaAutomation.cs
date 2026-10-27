using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ImmersiveX.Editor;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// Automation commands for ImmersiveX Media (see <see cref="EditorAutomation"/>):
    /// <list type="bullet">
    /// <item>In Play mode: <c>media status|play|pause|toggle|replay [on|off]|seek &lt;s&gt;|mute|unmute|speaker|volume &lt;0-1&gt;|turn &lt;deg&gt;|move &lt;x&gt; &lt;z&gt;|height &lt;m&gt;|open &lt;source&gt; [format]</c>
    /// (<c>hologram</c> is the same command). Web pages: <c>media inspect</c> (the mirrored scene), <c>media node &lt;name&gt;</c> (where its
    /// objects with that name are), <c>media click &lt;x&gt; &lt;y&gt;</c> (press on the page's panel, in page pixels) and <c>media js &lt;code&gt;</c>
    /// (run JavaScript in the page; what it returns is logged).</item>
    /// <item>In Edit mode: <c>media add [fps] &lt;file, folder or URL&gt;</c> (Add Media), <c>media pack [fps] [title=Name] &lt;folder&gt;</c>
    /// (pack splat frames into StreamingAssets without touching the scene) and <c>media check</c> (the build's content check).</item>
    /// <item><c>demo</c>: build the 3.5D Xperience demo scene.</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    static class MediaAutomation
    {
        static MediaAutomation()
        {
            EditorAutomation.RegisterCommand("media", Run);
            EditorAutomation.RegisterCommand("hologram", Run);
            EditorAutomation.RegisterCommand("demo", (command, argument) =>
                EditorAutomation.Report(command, DemoScenes.TryCreateXperience() ? "ok" : "retry", "See the Console for details."));
        }

        /// <summary>"media add [fps] &lt;source&gt;", "media check", "media scene", "media remove &lt;name&gt;" and "media first &lt;name&gt;".</summary>
        static void RunInEditMode(string command, string argument, string[] parts)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play mode first ('stop').");
            if (parts[0] == "scene")
            {
                // the open scene's media, as saved: name, source, height
                var lines = new System.Text.StringBuilder();
                foreach (var m in UnityEngine.Object.FindObjectsByType<ImmersiveMedia>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
                {
                    var so = new SerializedObject(m);
                    lines.AppendLine($"{m.name} · {so.FindProperty("_source").stringValue} · height {so.FindProperty("_height").floatValue:0.##} m · {(m.gameObject.activeInHierarchy ? "active" : "inactive")}");
                }

                EditorAutomation.Report(command, "ok", lines.Length > 0 ? lines.ToString() : "No Immersive Media in the open scene.");
                return;
            }

            if (parts[0] == "remove")
            {
                // "media remove <name>": delete the Immersive Media objects with this name (or starting with it) and save
                var name = argument.Substring(argument.IndexOf("remove", StringComparison.Ordinal) + 6).Trim().Trim('"');
                if (name.Length == 0)
                    throw new ArgumentException("'media remove' needs a name.");
                var removed = new List<string>();
                foreach (var m in UnityEngine.Object.FindObjectsByType<ImmersiveMedia>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
                {
                    if (!m.name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    removed.Add(m.name);
                    var scene = m.gameObject.scene;
                    Undo.DestroyObjectImmediate(m.gameObject);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                }

                EditorAutomation.Report(command, removed.Count > 0 ? "ok" : "error", removed.Count > 0 ? $"Removed {string.Join(", ", removed)} and saved the scene." : $"No Immersive Media named '{name}'.");
                return;
            }

            if (parts[0] == "first")
            {
                // "media first <name>": put this Immersive Media first in the scene, so it leads the content menu
                var name = argument.Substring(argument.IndexOf("first", StringComparison.Ordinal) + 5).Trim().Trim('"');
                var first = Array.Find(UnityEngine.Object.FindObjectsByType<ImmersiveMedia>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID),
                    m => string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"No Immersive Media named '{name}'.");
                Undo.SetTransformParent(first.transform, first.transform.parent, "Move media");
                first.transform.SetSiblingIndex(0);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(first.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(first.gameObject.scene);
                EditorAutomation.Report(command, "ok", $"'{first.name}' is first in {first.gameObject.scene.name} now (saved).");
                return;
            }

            if (parts[0] == "check")
            {
                var problems = MediaBuildCheck.Run(out var summary);
                EditorAutomation.Report(command, problems > 0 ? "error" : "ok", summary.Length > 0 ? summary : "Every Immersive Media in the build's scenes has its content.");
                return;
            }

            var rest = argument.Substring(argument.IndexOf(parts[0], StringComparison.Ordinal) + parts[0].Length).Trim();
            var options = new MediaImport.Options();
            var space = rest.IndexOf(' ');
            if (space > 0 && float.TryParse(rest.Substring(0, space), NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
            {
                options.FrameRate = fps;
                rest = rest.Substring(space + 1).Trim();
            }

            if (rest.StartsWith("title=\"", StringComparison.Ordinal) && rest.IndexOf('"', 7) > 7)
            {
                var end = rest.IndexOf('"', 7); // title="My clip"
                options.Title = rest.Substring(7, end - 7);
                rest = rest.Substring(end + 1).Trim();
            }
            else if (rest.StartsWith("title=", StringComparison.Ordinal))
            {
                space = rest.IndexOf(' ');
                options.Title = rest.Substring(6, (space > 0 ? space : rest.Length) - 6);
                rest = space > 0 ? rest.Substring(space + 1).Trim() : string.Empty;
            }

            if (parts[0] == "pack")
            {
                var folder = System.IO.Path.GetFullPath(rest);
                var frames = SplatSequencePacker.Frames(folder);
                if (frames.Length == 0)
                    throw new ArgumentException($"'{rest}' has no splat frames to pack.");
                var title = string.IsNullOrWhiteSpace(options.Title) ? System.IO.Path.GetFileName(folder.TrimEnd('/')) : options.Title;
                var output = System.IO.Path.Combine(Application.streamingAssetsPath, MediaImport.ContentFolder, MediaImport.SafeName(title));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = SplatSequencePacker.Pack(frames, output, options.FrameRate, UpAxis.Auto, 0, title);
                AssetDatabase.Refresh();
                EditorAutomation.Report(command, "ok", $"Packed {result.Frames} frames in {watch.Elapsed.TotalSeconds:0.0} s → {result.Manifest}: {result.MaxGaussians:N0} Gaussians at most, " +
                                                       $"{result.Bytes / 1e6:0} MB, {result.MegabytesPerSecond:0.#} MB/s, fit {result.Fit.Height:0.00} tall, " +
                                                       $"{result.LeftOut:N0} far splats left out (in {result.FramesLeftOutOf} frames).");
                return;
            }

            var media = MediaImport.Add(rest, options);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(media.gameObject.scene);
            var added = new SerializedObject(media);
            EditorAutomation.Report(command, "ok", $"Added '{media.name}' (Source: {added.FindProperty("_source").stringValue}, " +
                                                   $"height {added.FindProperty("_height").floatValue:0.##} m) and saved {media.gameObject.scene.name}.");
        }

        static void Run(string command, string argument)
        {
            var parts = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && (parts[0] == "add" || parts[0] == "pack" || parts[0] == "check" || parts[0] == "scene" || parts[0] == "remove" || parts[0] == "first"))
            {
                RunInEditMode(command, argument, parts);
                return;
            }

            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Not in Play mode. Send 'play 0' first.");
            var all = UnityEngine.Object.FindObjectsByType<ImmersiveMedia>(FindObjectsSortMode.InstanceID);
            if (all.Length == 0)
                throw new InvalidOperationException("No Immersive Media in the scene.");

            // "media list" describes them all; "media @<n> <action>" (1-based) or "media @<name> <action>" picks one.
            if (parts.Length > 0 && parts[0] == "list")
            {
                var lines = new System.Text.StringBuilder();
                for (var i = 0; i < all.Length; i++)
                    lines.AppendLine($"@{i + 1} {all[i].DescribeStatus()}");
                EditorAutomation.Report(command, "ok", lines.ToString());
                return;
            }

            var media = all[0];
            if (parts.Length > 0 && parts[0].StartsWith("@"))
            {
                var pick = parts[0].Substring(1);
                media = int.TryParse(pick, out var n) && n >= 1 && n <= all.Length
                    ? all[n - 1]
                    : Array.Find(all, m => m.name.StartsWith(pick, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"No media '{pick}'.");
                parts = parts.Length > 1 ? parts[1..] : new string[0];
            }

            var controls = media.Controls;
            var action = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            float Number(int index)
            {
                if (parts.Length > index && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    return value;
                throw new ArgumentException($"'media {action}' needs a number.");
            }

            switch (action)
            {
                case "status": break;
                case "play": media.Play(); break;
                case "pause": media.Pause(); break;
                case "toggle": // the play/pause button
                    if (controls != null)
                        controls.TogglePlay();
                    else
                        media.TogglePlay();
                    break;
                case "speaker": // the speaker button
                    if (controls != null)
                        controls.ToggleMute();
                    else
                        media.SetMuted(!media.Muted);
                    break;
                case "replay": // the Replay switch: "media replay" flips it, "media replay on|off" sets it
                    var on = parts.Length > 1 ? parts[1].ToLowerInvariant() switch
                    {
                        "on" => true,
                        "off" => false,
                        _ => throw new ArgumentException("Usage: media replay [on|off]"),
                    } : !media.Replay;
                    if (controls != null && on != media.Replay)
                        controls.ToggleReplay();
                    else
                        media.SetReplay(on);
                    break;
                case "seek": media.Seek(Number(1)); break;
                case "mute": media.SetMuted(true); break;
                case "unmute": media.SetMuted(false); break;
                case "volume": media.SetVolume(Number(1)); break;
                case "turn": media.Turn(Number(1)); media.SavePlacement(); break; // as if the user let go of it
                case "move": media.MoveBy(new Vector3(Number(1), 0f, Number(2))); media.SavePlacement(); break;
                case "height": media.Height = Number(1); media.SavePlacement(); break;
                case "parts": // a web page's parts: their colliders and how near they come to the user's reach to the panel
                    EditorAutomation.Report(command, "ok", media.Playable is WebPlayable withParts
                        ? withParts.DescribeParts(Camera.main != null ? Camera.main.transform : null) : "Not a web page.");
                    return;
                case "refit": // place a web page's scene again as it stands now (diagnostics)
                    EditorAutomation.Report(command, "ok", media.Playable is WebPlayable placed ? placed.Refit() : "Not a web page.");
                    return;
                case "inspect":
                    EditorAutomation.Report(command, "ok", media.Playable is WebPlayable web ? web.Inspect() : media.DescribeStatus());
                    return;
                case "node": // where the web page's mirrored objects with this name are
                    if (!(media.Playable is WebPlayable mirrored) || parts.Length < 2)
                        throw new ArgumentException("Usage: media @<n> node <name>  (web pages)");
                    var camera = Camera.main != null ? $" · camera at {Camera.main.transform.position:F2} looking {Camera.main.transform.forward:F2}" : string.Empty;
                    EditorAutomation.Report(command, "ok", mirrored.InspectNode(parts[1]) + camera);
                    return;
                case "js": // run JavaScript in its web page; the result is logged ("Web page: eval: …")
                    if (!(media.Playable is WebPlayable scripted))
                        throw new ArgumentException("'media js' is for web pages.");
                    scripted.Script(argument.Substring(argument.IndexOf("js", StringComparison.Ordinal) + 2).Trim());
                    break;
                case "click": // press and release on its web page's panel at page pixels x y (top left origin)
                    if (!(media.Playable is WebPlayable page))
                        throw new ArgumentException("'media click' is for web pages.");
                    page.Click(new Vector2(Number(1), Number(2)));
                    break;
                case "open":
                    if (parts.Length < 2)
                        throw new ArgumentException("Usage: media open <source> [format]  (quote a source with spaces)");
                    var source = parts[1];
                    var formatAt = 2;
                    var quote = argument.IndexOf('"');
                    if (quote >= 0 && argument.IndexOf('"', quote + 1) > quote)
                    {
                        var end = argument.IndexOf('"', quote + 1);
                        source = argument.Substring(quote + 1, end - quote - 1);
                        var after = argument.Substring(end + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        parts = new[] { "open", source }.Concat(after).ToArray();
                    }

                    var format = parts.Length > formatAt && Enum.TryParse(parts[formatAt], true, out MediaFormat parsed) ? parsed : MediaFormat.Auto;
                    media.Open(source, format);
                    EditorAutomation.Report(command, "ok", $"Opening {source} ({format}). Send 'media status' to follow it.");
                    return;
                default: throw new ArgumentException($"Unknown media action '{action}'.");
            }

            var panel = controls != null
                ? $" · controls at {controls.Panel.position:F2} ('{controls.Title}') showing {(controls.ShowsPause ? "pause" : "play")}, {(controls.ShowsMuted ? "muted" : "speaker")} and replay {(controls.ShowsReplay ? "on" : "off")}" +
                  (controls.Caption != null ? $" · caption at {controls.CaptionPosition:F2}" : string.Empty)
                : string.Empty;
            EditorAutomation.Report(command, "ok", media.DescribeStatus() + panel);
        }
    }
}
