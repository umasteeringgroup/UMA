#if UNITY_EDITOR

using System.Text;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UMA.Editors
{
    public sealed class UMATestingWindow : EditorWindow
    {
        private const string WindowTitle = "UMA Testing";

        [SerializeField] private RaceData selectedRace;
        [SerializeField] private bool testAllIndexedRaces;
        [SerializeField] private Vector2 scrollPosition;

        private UMATestReport lastReport;
        private int lastRunRaceCount;
        private bool lastRunCancelled;

        [MenuItem("UMA/Testing/Race Smoke Test...", priority = 2000)]
        public static void OpenWindow()
        {
            UMATestingWindow window = GetWindow<UMATestingWindow>(WindowTitle);
            window.minSize = new Vector2(560f, 420f);
            window.TryPrefillRaceFromSelection();
            window.Show();
        }

        [MenuItem("UMA/Testing/Run UMA Editor Tests", priority = 2001)]
        public static void RunUmaEditorTests()
        {
            TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
            ExecutionSettings settings = new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                categoryNames = new[] { "UMA" }
            });

            api.Execute(settings);
            Debug.Log("[UMA] Started UMA EditMode tests through Unity Test Runner.");
        }

        [MenuItem("UMA/Testing/Open Unity Test Runner", priority = 2002)]
        public static void OpenUnityTestRunner()
        {
            if (!EditorApplication.ExecuteMenuItem("Window/General/Test Runner"))
            {
                EditorApplication.ExecuteMenuItem("Window/Analysis/Test Runner");
            }
        }

        private void OnEnable()
        {
            TryPrefillRaceFromSelection();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Race Smoke Test", EditorStyles.boldLabel);
            testAllIndexedRaces = EditorGUILayout.ToggleLeft(
                "Test All Indexed Races", testAllIndexedRaces);
            using (new EditorGUI.DisabledScope(testAllIndexedRaces))
            {
                selectedRace = (RaceData)EditorGUILayout.ObjectField("Race", selectedRace,
                    typeof(RaceData), false);
            }

            if (testAllIndexedRaces)
            {
                EditorGUILayout.HelpBox(
                    "Runs every RaceData returned by the UMA Asset Index. Failures are grouped by race, and the run can be cancelled between races.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!testAllIndexedRaces && selectedRace == null))
            {
                string buttonLabel = testAllIndexedRaces
                    ? "Run All Indexed Race Smoke Tests" : "Run Race Smoke Test";
                if (GUILayout.Button(buttonLabel, GUILayout.Height(30f)))
                {
                    if (testAllIndexedRaces) RunAllIndexedRaceSmokeTests();
                    else RunSelectedRaceSmokeTest();
                }
            }

            EditorGUILayout.Space();
            DrawReport();
        }

        private void TryPrefillRaceFromSelection()
        {
            if (selectedRace != null)
            {
                return;
            }

            selectedRace = Selection.activeObject as RaceData;
            if (selectedRace == null && Selection.activeGameObject != null)
            {
                DynamicCharacterAvatar avatar = Selection.activeGameObject.GetComponentInParent<DynamicCharacterAvatar>();
                if (avatar != null && avatar.activeRace != null)
                {
                    selectedRace = avatar.activeRace.data;
                }
            }
        }

        private void RunSelectedRaceSmokeTest()
        {
            lastRunRaceCount = selectedRace != null ? 1 : 0;
            lastRunCancelled = false;
            lastReport = UMARaceSmokeTestRunner.Run(selectedRace);
            LogLastReport();
        }

        private void RunAllIndexedRaceSmokeTests()
        {
            lastRunRaceCount = 0;
            lastRunCancelled = false;
            try
            {
                lastReport = UMARaceSmokeTestRunner.RunAllIndexed(null,
                    (index, count, race) =>
                    {
                        bool keepGoing = !EditorUtility.DisplayCancelableProgressBar(
                            "UMA Race Smoke Test",
                            "Testing " + (race != null && !string.IsNullOrEmpty(race.raceName)
                                ? race.raceName : "indexed race " + (index + 1)) +
                            " (" + (index + 1) + " of " + count + ")",
                            count > 0 ? index / (float)count : 0f);
                        if (keepGoing) lastRunRaceCount = index + 1;
                        else lastRunCancelled = true;
                        return keepGoing;
                    });
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            LogLastReport();
        }

        private void LogLastReport()
        {
            if (lastReport == null) return;
            Object context = lastReport.Race;
            if (lastReport.HasErrors)
            {
                Debug.LogError(lastReport.ToLogString(), context);
            }
            else if (lastReport.HasWarnings)
            {
                Debug.LogWarning(lastReport.ToLogString(), context);
            }
            else
            {
                Debug.Log(lastReport.ToLogString(), context);
            }
        }

        private void DrawReport()
        {
            if (lastReport == null)
            {
                EditorGUILayout.HelpBox("Choose a RaceData asset and run the smoke test.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Errors", lastReport.ErrorCount.ToString());
            EditorGUILayout.LabelField("Warnings", lastReport.WarningCount.ToString());
            EditorGUILayout.LabelField("Passes", lastReport.PassCount.ToString());
            if (testAllIndexedRaces || lastReport.Race == null)
            {
                EditorGUILayout.LabelField("Races Processed", lastRunRaceCount.ToString());
                if (lastRunCancelled)
                    EditorGUILayout.HelpBox("The all-races run was cancelled between races.",
                        MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Results"))
            {
                EditorGUIUtility.systemCopyBuffer = lastReport.ToLogString();
            }
            if (GUILayout.Button("Log Results"))
            {
                Object context = lastReport.Race;
                Debug.Log(lastReport.ToLogString(), context);
            }
            EditorGUILayout.EndHorizontal();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < lastReport.Messages.Count; i++)
            {
                UMATestMessage message = lastReport.Messages[i];
                EditorGUILayout.HelpBox(FormatMessage(message), GetMessageType(message.Severity));
            }
            EditorGUILayout.EndScrollView();
        }

        private static string FormatMessage(UMATestMessage message)
        {
            StringBuilder builder = new StringBuilder();
            if (!string.IsNullOrEmpty(message.Category))
            {
                builder.Append(message.Category).Append(": ");
            }

            builder.Append(message.Message);
            return builder.ToString();
        }

        private static MessageType GetMessageType(UMATestSeverity severity)
        {
            switch (severity)
            {
                case UMATestSeverity.Error:
                    return MessageType.Error;
                case UMATestSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}

#endif
