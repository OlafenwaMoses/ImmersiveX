using System;
using System.IO;
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
    /// <item><c>refresh</c>: import changed files and recompile · <c>test [editmode|playmode]</c>: run the Unity tests</item>
    /// <item><c>play [seconds]</c>: enter Play mode, record the log, take a screenshot, stop after the given time (default 20; 0 = keep playing until <c>stop</c>)</item>
    /// <item><c>capture</c>: screenshot the Game view now · <c>invoke spacesetup|saveanchor|loadanchor|eraseanchor</c>: press a Device Check button</item>
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
        static int _errors;
        static int _warnings;

        static EditorAutomation()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (UnityEditor.SessionState.GetBool(PlayRunKey, false) && EditorApplication.isPlaying)
                Application.logMessageReceivedThreaded += RecordLog;
        }

        static void Poll()
        {
            if (UnityEditor.SessionState.GetBool(PlayRunKey, false))
                AdvancePlayRun();

            if (!File.Exists(CommandFile) || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var command = File.ReadAllText(CommandFile).Trim();
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
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(ScreenshotFile));
                    WriteResult(command, "ok", $"Screenshot requested: {ScreenshotFile}");
                    break;
                case "invoke":
                    Invoke(command, argument);
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
                    WriteResult(command, "error", "Unknown command. See EditorAutomation's documentation for the list.");
                    break;
            }
        }

        static void Invoke(string command, string action)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Not in Play mode. Send 'play 0' first.");
            var panel = UnityEngine.Object.FindAnyObjectByType<Diagnostics.DeviceCheckPanel>();
            if (panel == null)
                throw new InvalidOperationException("No Device Check Panel in the scene.");

            switch (action)
            {
                case "spacesetup": panel.RunSpaceSetup(); break;
                case "saveanchor": panel.SaveAnchor(); break;
                case "loadanchor": panel.LoadAnchor(); break;
                case "eraseanchor": panel.EraseAnchor(); break;
                default: throw new ArgumentException($"Unknown action '{action}'.");
            }

            WriteResult(command, "ok", $"Invoked {action}. Session: {ImmersiveXSession.Instance?.State}");
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
