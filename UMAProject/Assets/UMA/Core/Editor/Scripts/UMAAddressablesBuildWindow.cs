using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

#if UMA_ADDRESSABLES
public class UMAAddressablesBuildWindow : EditorWindow
{
    [MenuItem("UMA/Sample Addressables Build")]
    public static void OpenWindow()
    {
        CreateWindow<UMAAddressablesBuildWindow>()
            .Init()
            .Show();
    }

    private static bool dev = true;
    private static string appName = "UMASample.exe";

    private static string DestinationFolder
    {
        get => EditorPrefs.GetString("UMABuildPath", $"{Application.dataPath.TrimEnd("/Assets".ToCharArray())}/UMATestBuild");
        set => EditorPrefs.SetString("UMABuildPath", value);
    }

    private static string AppName
    {
        get => EditorPrefs.GetString("UMAAppName", appName);
        set => EditorPrefs.SetString("UMAAppName", value);
    }

    private UMAAddressablesBuildWindow Init()
    {
        titleContent = new GUIContent("UMA Build Sample");
        return this;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("UMA Addressables Build Sample");
        EditorGUILayout.Space(20);
        dev = EditorGUILayout.Toggle("Development Build", dev);
        AppName = EditorGUILayout.TextField("App Name", AppName);
        EditorGUILayout.BeginHorizontal();

        DestinationFolder = EditorGUILayout.TextField("Build Path", DestinationFolder);
        if (GUILayout.Button("Browse"))
        {
            DestinationFolder = EditorUtility.OpenFolderPanel("Output Folder", DestinationFolder, "");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);
        if (GUILayout.Button("Build Addressables"))
        {
            UMAAddressablesBuildSample.Build(DestinationFolder,dev, AppName);
        }
    }
}



#endif
