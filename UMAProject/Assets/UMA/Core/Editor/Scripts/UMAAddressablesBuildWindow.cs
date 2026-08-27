using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA
{
    public class UMAAddressablesBuildWindow : EditorWindow
    {
        private const string PlayerBuildMenu =
            "UMA/Build/Non-Addressables Build Sample";
        private const string AddressablesBuildMenu =
            "UMA/Build/Addressables Build Sample";

        private static bool developmentBuild = true;
        private bool useAddressables;

        private static string DestinationFolder
        {
            get
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                return EditorPrefs.GetString("UMABuildPath",
                    Path.Combine(projectRoot, "UMATestBuild"));
            }
            set => EditorPrefs.SetString("UMABuildPath", value);
        }

        private static string ApplicationName
        {
            get => EditorPrefs.GetString("UMAAppName", "UMASample.exe");
            set => EditorPrefs.SetString("UMAAppName", value);
        }

        [MenuItem(PlayerBuildMenu, false, 100)]
        public static void OpenPlayerBuildWindow()
        {
            OpenWindow(false);
        }

        [MenuItem(AddressablesBuildMenu, false, 110)]
        public static void OpenAddressablesBuildWindow()
        {
#if UMA_ADDRESSABLES
            if (UMAAddressablesBuildSample.IsAvailable)
                OpenWindow(true);
#endif
        }

        // Retained for callers of the original sample window API.
        public static void OpenWindow()
        {
            OpenAddressablesBuildWindow();
        }

        [MenuItem(AddressablesBuildMenu, true)]
        public static bool ValidateOpenAddressablesBuildWindow()
        {
#if UMA_ADDRESSABLES
            return UMAAddressablesBuildSample.IsAvailable;
#else
            return false;
#endif
        }

        private static void OpenWindow(bool addressables)
        {
            UMAAddressablesBuildWindow window =
                GetWindow<UMAAddressablesBuildWindow>();
            window.useAddressables = addressables;
            window.titleContent = new GUIContent(addressables
                ? "UMA Addressables Build Sample"
                : "UMA Non-Addressables Build Sample");
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(useAddressables
                    ? "UMA Addressables Build Sample"
                    : "UMA Non-Addressables Build Sample",
                EditorStyles.boldLabel);

            string dirtySceneMessage = GetDirtySceneMessage();
            if (!string.IsNullOrEmpty(dirtySceneMessage))
            {
                EditorGUILayout.HelpBox(
                    "Please save all scenes before building to avoid a mid-build " +
                    "dialog.\n" + dirtySceneMessage, MessageType.Error);
            }
            if (useAddressables && !UMAAddressablesBuildSample.IsAvailable)
            {
                EditorGUILayout.HelpBox(
                    "Addressables support is unavailable. Install Addressables and " +
                    "enable UMA_ADDRESSABLES.", MessageType.Error);
            }

            EditorGUILayout.Space(20);
            developmentBuild = EditorGUILayout.Toggle("Development Build",
                developmentBuild);
            ApplicationName = EditorGUILayout.TextField("App Name", ApplicationName);

            EditorGUILayout.BeginHorizontal();
            DestinationFolder = EditorGUILayout.TextField("Build Path",
                DestinationFolder);
            if (GUILayout.Button("Browse"))
            {
                string selected = EditorUtility.OpenFolderPanel("Output Folder",
                    DestinationFolder, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                    DestinationFolder = selected;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(20);
            EditorGUI.BeginDisabledGroup(useAddressables &&
                !UMAAddressablesBuildSample.IsAvailable);
            if (GUILayout.Button(useAddressables
                    ? "Build Addressables and Player"
                    : "Build Player"))
            {
                if (useAddressables)
                {
                    UMAAddressablesBuildSample.Build(DestinationFolder,
                        developmentBuild, ApplicationName);
                }
                else
                {
                    UMABuildSample.Build(DestinationFolder, developmentBuild,
                        ApplicationName);
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private static string GetDirtySceneMessage()
        {
            var message = new System.Text.StringBuilder();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (scene.isDirty)
                    message.AppendLine("Scene " + scene.name + " is dirty.");
            }
            return message.ToString().TrimEnd();
        }
    }
}
