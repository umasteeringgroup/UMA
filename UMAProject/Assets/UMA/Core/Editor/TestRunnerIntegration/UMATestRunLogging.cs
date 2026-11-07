using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.Editors
{
    [InitializeOnLoad]
    internal sealed class UMATestRunLogging : ICallbacks
    {
        private const string ActiveKey = "UMA.TestLog.ActiveRun";
        private static readonly UMATestRunLogging Listener = new UMATestRunLogging();
        private readonly UMATestRunLog log = new UMATestRunLog(
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "UMA-TestLog.xml"));
        private bool failed;

        static UMATestRunLogging()
        {
            // Register again after reload, including when a run was started manually in
            // Test Runner. Start records are flushed before any lower-priority callback.
            TestRunnerApi.RegisterTestCallback(Listener, int.MaxValue);
            AssemblyReloadEvents.beforeAssemblyReload += () => Listener.Record("domain-reload-start");
            Listener.Record("domain-reload-complete");
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            failed = false;
            SessionState.SetBool(ActiveKey, false);
            TryWrite(() =>
            {
                var settings = UMASettings.GetSettingsFromResources();
                log.BeginRun(new Dictionary<string, string>
                {
                    ["name"] = testsToRun.FullName,
                    ["mode"] = testsToRun.TestMode.ToString(),
                    ["project"] = Path.GetDirectoryName(Application.dataPath),
                    ["unity-version"] = Application.unityVersion,
                    ["uma-version"] = settings != null ? settings.UMAVersion : "unavailable"
                });
                SessionState.SetBool(ActiveKey, true);
            });
        }

        public void TestStarted(ITestAdaptor test)
        {
            Record(test.IsSuite ? "suite-start" : "test-start", Details(test));
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            var details = Details(result.Test);
            details["result"] = result.ResultState;
            details["duration-seconds"] = result.Duration.ToString("R", CultureInfo.InvariantCulture);
            Record(result.Test.IsSuite ? "suite-finish" : "test-finish", details, result.Message, result.StackTrace);
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            Record("run-finish", new Dictionary<string, string>
            {
                ["result"] = result.ResultState,
                ["duration-seconds"] = result.Duration.ToString("R", CultureInfo.InvariantCulture),
                ["passed"] = result.PassCount.ToString(CultureInfo.InvariantCulture),
                ["failed"] = result.FailCount.ToString(CultureInfo.InvariantCulture),
                ["skipped"] = result.SkipCount.ToString(CultureInfo.InvariantCulture),
                ["inconclusive"] = result.InconclusiveCount.ToString(CultureInfo.InvariantCulture)
            }, result.Message, result.StackTrace);
            SessionState.SetBool(ActiveKey, false);
        }

        private static Dictionary<string, string> Details(ITestAdaptor test)
        {
            var scene = SceneManager.GetActiveScene();
            return new Dictionary<string, string>
            {
                ["id"] = test.Id, ["name"] = test.FullName,
                ["scene"] = scene.IsValid() ? scene.path : "invalid",
                ["scene-name"] = scene.IsValid() ? scene.name : "invalid"
            };
        }

        private void Record(string eventName, IReadOnlyDictionary<string, string> details = null,
            string message = null, string stackTrace = null)
        {
            if (SessionState.GetBool(ActiveKey, false))
                TryWrite(() => log.Append(eventName, details, message, stackTrace));
        }

        private void TryWrite(Action write)
        {
            if (failed) return;
            try { write(); }
            catch (Exception exception)
            {
                failed = true;
                Debug.LogWarning("UMA test logging stopped for this run: " + log.FilePath + "\n" + exception.Message);
            }
        }
    }
}
