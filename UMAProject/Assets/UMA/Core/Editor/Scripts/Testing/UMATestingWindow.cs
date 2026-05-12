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
        [SerializeField] private Vector2 scrollPosition;

        private UMATestReport lastReport;

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
            selectedRace = (RaceData)EditorGUILayout.ObjectField("Race", selectedRace, typeof(RaceData), false);

            using (new EditorGUI.DisabledScope(selectedRace == null))
            {
                if (GUILayout.Button("Run Race Smoke Test", GUILayout.Height(30f)))
                {
                    RunSelectedRaceSmokeTest();
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
            lastReport = UMARaceSmokeTestRunner.Run(selectedRace);
            if (lastReport.HasErrors)
            {
                Debug.LogError(lastReport.ToLogString(), selectedRace);
            }
            else if (lastReport.HasWarnings)
            {
                Debug.LogWarning(lastReport.ToLogString(), selectedRace);
            }
            else
            {
                Debug.Log(lastReport.ToLogString(), selectedRace);
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

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Results"))
            {
                EditorGUIUtility.systemCopyBuffer = lastReport.ToLogString();
            }
            if (GUILayout.Button("Log Results"))
            {
                Debug.Log(lastReport.ToLogString(), selectedRace);
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