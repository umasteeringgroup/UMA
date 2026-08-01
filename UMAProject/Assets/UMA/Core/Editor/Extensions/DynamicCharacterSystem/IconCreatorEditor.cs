#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IconCreator))]
public class IconCreatorEditor : Editor
{
    private SerializedProperty rootFolderProperty;
    private SerializedProperty regionToCameraListProperty;
    private readonly List<bool> cameraRegionFoldouts = new List<bool>();

    private void OnEnable()
    {
        rootFolderProperty = serializedObject.FindProperty("rootFolder");
        regionToCameraListProperty = serializedObject.FindProperty("regionToCameraList");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawRootFolder();
        EditorGUILayout.Space();
        DrawCameraRegions();
        EditorGUILayout.Space();
        DrawPropertiesExcluding(serializedObject, "m_Script", "rootFolder", "regionToCameraList");
        EditorGUILayout.Space();
        DrawSpriteAtlasControls();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSpriteAtlasControls()
    {
        EditorGUILayout.LabelField("Thumbnail Sprite Atlases", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Rebuilds atlases from the Sprites referenced by wardrobe recipes, grouped by race and wardrobe region.",
            MessageType.Info);

        string atlasFolder;
        try
        {
            atlasFolder = IconCreatorSpriteAtlasUtility.GetAtlasFolder(rootFolderProperty.stringValue);
        }
        catch (ArgumentException exception)
        {
            EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            return;
        }
        EditorGUILayout.LabelField("Output Folder", atlasFolder);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Rebuild Thumbnail Atlases"))
            {
                serializedObject.ApplyModifiedProperties();
                RebuildThumbnailAtlases(rootFolderProperty.stringValue);
            }
        }
    }

    private static void RebuildThumbnailAtlases(string rootFolder)
    {
        try
        {
            IconCreatorSpriteAtlasUtility.RebuildResult result =
                IconCreatorSpriteAtlasUtility.Rebuild(rootFolder);
            string message =
                "Rebuilt " + result.AtlasCount + " atlases from " + result.SpriteCount +
                " referenced Sprites across " + result.RecipeCount + " wardrobe recipes.";
            if (result.ClearedAtlasCount > 0)
            {
                message += "\n\nCleared packables from " + result.ClearedAtlasCount + " obsolete atlases.";
            }
            if (result.WarningCount > 0)
            {
                message += "\n\n" + result.WarningCount + " conflicts were logged as warnings.";
            }
            message += "\n\nOutput: " + result.OutputFolder;
            EditorUtility.DisplayDialog("Thumbnail Sprite Atlases", message, "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Thumbnail Sprite Atlases", exception.Message, "OK");
        }
    }

    private void DrawRootFolder()
    {
        EditorGUILayout.LabelField("Root Folder");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(rootFolderProperty, GUIContent.none);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("Select Root Folder", GetInitialFolder(rootFolderProperty.stringValue), string.Empty);
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    rootFolderProperty.stringValue = selectedFolder;
                }
            }
        }
    }

    private void DrawCameraRegions()
    {
        EditorGUILayout.LabelField("Camera Regions", EditorStyles.boldLabel);
        EnsureFoldoutState();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Expand All", GUILayout.Width(100f)))
        {
            for (int i = 0; i < cameraRegionFoldouts.Count; i++)
            {
                cameraRegionFoldouts[i] = true;
            }
        }
        if (GUILayout.Button("Collapse All", GUILayout.Width(100f)))
        {
            for (int i = 0; i < cameraRegionFoldouts.Count; i++)
            {
                cameraRegionFoldouts[i] = false;
            }
        }
        if (GUILayout.Button("Validate Regions", GUILayout.Width(120f)))
        {
            ValidateRegions();
        }
        GUILayout.EndHorizontal();
        for (int i = 0; i < regionToCameraListProperty.arraySize; i++)
        {
            SerializedProperty elementProperty = regionToCameraListProperty.GetArrayElementAtIndex(i);
            SerializedProperty cameraProperty = elementProperty.FindPropertyRelative("camera");
            SerializedProperty regionsProperty = elementProperty.FindPropertyRelative("regions");
            string cameraName = cameraProperty.objectReferenceValue != null ? cameraProperty.objectReferenceValue.name : "Unassigned";

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    cameraRegionFoldouts[i] = EditorGUILayout.Foldout(cameraRegionFoldouts[i], $"Camera {i + 1}: {cameraName}", true);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                    {
                        regionToCameraListProperty.DeleteArrayElementAtIndex(i);
                        cameraRegionFoldouts.RemoveAt(i);
                        break;
                    }
                }

                if (cameraRegionFoldouts[i])
                {
                    EditorGUILayout.PropertyField(cameraProperty);
                    DrawAssignedRegions(regionsProperty);
                    DrawAddRegionButton(i, regionsProperty);
                }
            }
        }

        if (GUILayout.Button("Add Camera Region"))
        {
            AddCameraRegion();
        }
    }

    private void ValidateRegions()
    {
        IconCreator iconCreator = (IconCreator)target;
        List<string> rRegions = GetRaceRegions();
        List<string> raceRegions = new List<string>(rRegions);

        if (raceRegions == null)
        {
            EditorUtility.DisplayDialog("Validation Result", "No race regions found.", "OK");
            return;
        }
        IconCreator ic = target as IconCreator;
        foreach (var cmregion in ic.regionToCameraList)
        {
            foreach (var region in cmregion.regions)
            {
                raceRegions.Remove(region);
            }
        }
        // notify that the remaining regions are not assigned to any camera
        string message = raceRegions.Count > 0 ? $"The following regions are not assigned to any camera:\n- {string.Join("\n- ", raceRegions)}" : "All regions are assigned to cameras.";
        EditorUtility.DisplayDialog("Validation Result", message, "OK");
    }

    private void DrawAssignedRegions(SerializedProperty regionsProperty)
    {
        EditorGUILayout.LabelField("Regions");

        if (regionsProperty.arraySize == 0)
        {
            EditorGUILayout.HelpBox("No regions assigned.", MessageType.Info);
            return;
        }

        for (int i = 0; i < regionsProperty.arraySize; i++)
        {
            SerializedProperty regionProperty = regionsProperty.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(regionProperty.stringValue);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    regionsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }
    }

    private void DrawAddRegionButton(int cameraIndex, SerializedProperty regionsProperty)
    {
        List<string> availableRegions = GetAvailableRegions(regionsProperty);
        string unavailableMessage = GetUnavailableRegionsMessage();

        using (new EditorGUI.DisabledScope(availableRegions.Count == 0))
        {
            if (GUILayout.Button(availableRegions.Count == 0 ? "No Regions Available" : "Add Region"))
            {
                GenericMenu menu = new GenericMenu();
                for (int i = 0; i < availableRegions.Count; i++)
                {
                    string regionName = availableRegions[i];
                    menu.AddItem(new GUIContent(regionName), false, OnAddRegionSelected, new AddRegionSelection(cameraIndex, regionName));
                }

                menu.ShowAsContext();
            }
        }

        if (availableRegions.Count == 0)
        {
            EditorGUILayout.HelpBox(unavailableMessage, MessageType.Info);
        }
    }

    private List<string> GetAvailableRegions(SerializedProperty regionsProperty)
    {
        HashSet<string> assignedRegions = new HashSet<string>();
        List<string> availableRegions = new List<string>();
        List<string> raceRegions = GetRaceRegions();

        for (int i = 0; i < regionsProperty.arraySize; i++)
        {
            string regionName = regionsProperty.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(regionName))
            {
                assignedRegions.Add(regionName);
            }
        }

        if (raceRegions == null)
        {
            return availableRegions;
        }

        for (int i = 0; i < raceRegions.Count; i++)
        {
            string regionName = raceRegions[i];
            if (string.IsNullOrEmpty(regionName) || assignedRegions.Contains(regionName) || availableRegions.Contains(regionName))
            {
                continue;
            }

            availableRegions.Add(regionName);
        }

        return availableRegions;
    }

    private List<string> GetRaceRegions()
    {
        IconCreator iconCreator = (IconCreator)target;
        RaceData raceData = iconCreator.avatar != null ? iconCreator.avatar.activeRace.data : null;
        if (raceData != null)
        {
            return new List<string>(raceData.Regions);
        }
        else
        {
            return null;
        }
    }

    private string GetUnavailableRegionsMessage()
    {
        IconCreator iconCreator = (IconCreator)target;
        if (iconCreator.avatar == null)
        {
            return "Assign an Avatar to load regions from its race data.";
        }

        if (iconCreator.avatar.activeRace.data == null)
        {
            return "The assigned Avatar does not have active race data yet.";
        }

        return "All race regions are already assigned to this camera.";
    }

    private void AddCameraRegion()
    {
        int newIndex = regionToCameraListProperty.arraySize;
        regionToCameraListProperty.InsertArrayElementAtIndex(newIndex);

        SerializedProperty elementProperty = regionToCameraListProperty.GetArrayElementAtIndex(newIndex);
        elementProperty.FindPropertyRelative("camera").objectReferenceValue = null;
        elementProperty.FindPropertyRelative("regions").ClearArray();
        cameraRegionFoldouts.Add(true);
    }

    private void EnsureFoldoutState()
    {
        while (cameraRegionFoldouts.Count < regionToCameraListProperty.arraySize)
        {
            cameraRegionFoldouts.Add(true);
        }

        while (cameraRegionFoldouts.Count > regionToCameraListProperty.arraySize)
        {
            cameraRegionFoldouts.RemoveAt(cameraRegionFoldouts.Count - 1);
        }
    }

    private void OnAddRegionSelected(object userData)
    {
        AddRegionSelection selection = (AddRegionSelection)userData;

        serializedObject.Update();

        if (selection.CameraIndex < 0 || selection.CameraIndex >= regionToCameraListProperty.arraySize)
        {
            return;
        }

        SerializedProperty elementProperty = regionToCameraListProperty.GetArrayElementAtIndex(selection.CameraIndex);
        SerializedProperty regionsProperty = elementProperty.FindPropertyRelative("regions");
        int newIndex = regionsProperty.arraySize;

        regionsProperty.InsertArrayElementAtIndex(newIndex);
        regionsProperty.GetArrayElementAtIndex(newIndex).stringValue = selection.RegionName;

        serializedObject.ApplyModifiedProperties();
    }

    private static string GetInitialFolder(string currentFolder)
    {
        if (!string.IsNullOrEmpty(currentFolder))
        {
            if (Directory.Exists(currentFolder))
            {
                return currentFolder;
            }

            string projectFolder = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                string combinedPath = Path.GetFullPath(Path.Combine(projectFolder, currentFolder));
                if (Directory.Exists(combinedPath))
                {
                    return combinedPath;
                }
            }
        }

        return Application.dataPath;
    }

    private class AddRegionSelection
    {
        public int CameraIndex { get; }
        public string RegionName { get; }

        public AddRegionSelection(int cameraIndex, string regionName)
        {
            CameraIndex = cameraIndex;
            RegionName = regionName;
        }
    }
}
#endif
