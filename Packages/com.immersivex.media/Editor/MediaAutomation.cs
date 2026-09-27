using System;
using System.Globalization;
using ImmersiveX.Editor;
using UnityEditor;
using UnityEngine;

namespace ImmersiveX.Media.Editor
{
    /// <summary>
    /// Automation commands for ImmersiveX Media (see <see cref="EditorAutomation"/>):
    /// <c>media status|play|pause|toggle|seek &lt;s&gt;|mute|unmute|speaker|volume &lt;0-1&gt;|turn &lt;deg&gt;|move &lt;x&gt; &lt;z&gt;|height &lt;m&gt;|open &lt;source&gt; [format]</c>
    /// (<c>hologram</c> is the same command) and <c>demo</c> (build the 3.5D Xperience demo scene).
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

        static void Run(string command, string argument)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Not in Play mode. Send 'play 0' first.");
            var parts = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
                case "seek": media.Seek(Number(1)); break;
                case "mute": media.SetMuted(true); break;
                case "unmute": media.SetMuted(false); break;
                case "volume": media.SetVolume(Number(1)); break;
                case "turn": media.Turn(Number(1)); media.SavePlacement(); break; // as if the user let go of it
                case "move": media.MoveBy(new Vector3(Number(1), 0f, Number(2))); media.SavePlacement(); break;
                case "height": media.Height = Number(1); media.SavePlacement(); break;
                case "open":
                    if (parts.Length < 2)
                        throw new ArgumentException("Usage: media open <source> [format]");
                    var format = parts.Length > 2 && Enum.TryParse(parts[2], true, out MediaFormat parsed) ? parsed : MediaFormat.Auto;
                    media.Open(parts[1], format);
                    EditorAutomation.Report(command, "ok", $"Opening {parts[1]} ({format}). Send 'media status' to follow it.");
                    return;
                default: throw new ArgumentException($"Unknown media action '{action}'.");
            }

            var panel = controls != null
                ? $" · controls at {controls.transform.position:F2} showing {(controls.ShowsPause ? "pause" : "play")} and {(controls.ShowsMuted ? "muted" : "speaker")}"
                : string.Empty;
            EditorAutomation.Report(command, "ok", media.DescribeStatus() + panel);
        }
    }
}
