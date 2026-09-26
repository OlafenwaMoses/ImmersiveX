using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ImmersiveX.Editor
{
    /// <summary>
    /// Runs Unity Test Framework tests for <see cref="EditorAutomation"/>'s <c>test</c> command and reports
    /// pass/fail counts and failure messages. Only compiled when the Test Framework package is installed.
    /// </summary>
    [InitializeOnLoad]
    static class AutomationTestRunner
    {
        static AutomationTestRunner() => EditorAutomation.TestRunner = Run;

        static void Run(string mode, System.Action<string, string> report)
        {
            var testMode = mode == "playmode" ? TestMode.PlayMode : TestMode.EditMode;
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(report, api));
            api.Execute(new ExecutionSettings(new Filter { testMode = testMode }));
        }

        sealed class Callbacks : ICallbacks
        {
            readonly System.Action<string, string> _report;
            readonly TestRunnerApi _api;
            readonly List<string> _failures = new List<string>();

            public Callbacks(System.Action<string, string> report, TestRunnerApi api)
            {
                _report = report;
                _api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                    _failures.Add($"✗ {result.FullName}\n    {result.Message?.Trim()}");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var summary = new StringBuilder();
                summary.AppendLine($"Passed: {result.PassCount} · Failed: {result.FailCount} · Skipped: {result.SkipCount + result.InconclusiveCount} · {result.Duration:0.0}s");
                foreach (var failure in _failures)
                    summary.AppendLine(failure);
                _report(result.FailCount == 0 ? "ok" : "failed", summary.ToString());
                _api.UnregisterCallbacks(this);
            }
        }
    }
}
