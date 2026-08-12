using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UMA.Editors
{
    [InitializeOnLoad]
    internal sealed class UMATestRunnerBackendUnity : IUMATestRunnerBackend
    {
        static UMATestRunnerBackendUnity()
        {
            UMATestRunnerBackend.Register(new UMATestRunnerBackendUnity());
        }

        public void RunEditModeCategory(string categoryName)
        {
            TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                categoryNames = new[] { categoryName }
            }));
        }
    }
}
