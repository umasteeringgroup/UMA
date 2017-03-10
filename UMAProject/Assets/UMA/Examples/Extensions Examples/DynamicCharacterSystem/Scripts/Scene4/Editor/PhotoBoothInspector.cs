using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
//using UnityEditorInternal;//Dont think this is needed
using UMA;
using UMACharacterSystem;

[CustomEditor(typeof(PhotoBooth), true)]
public class PhotoBoothEditor : Editor
{
    protected PhotoBooth thisPB;

    public void OnEnable()
    {
        thisPB = target as PhotoBooth;
    }

    public override void OnInspectorGUI()
    {
        //DrawDefaultInspector ();
        Editor.DrawPropertiesExcluding(serializedObject, new string[] {"doingTakePhoto","animationFreezeFrame", "autoPhotosEnabled", "textureToPhoto","dimAllButTarget","dimToColor", "dimToMetallic", "neutralizeTargetColors","neutralizeToColor", "neutralizeToMetallic", "addUnderwearToBasePhoto","overwriteExistingPhotos","destinationFolder" });
        serializedObject.ApplyModifiedProperties();
        bool freezeAnimation = serializedObject.FindProperty("freezeAnimation").boolValue;
        bool doingTakePhoto = serializedObject.FindProperty("doingTakePhoto").boolValue;
        if (freezeAnimation)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animationFreezeFrame"));
        }
        bool autoPhotosEnabled = serializedObject.FindProperty("autoPhotosEnabled").boolValue;
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoPhotosEnabled"));
        if (autoPhotosEnabled)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dimAllButTarget"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dimToColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dimToMetallic"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("neutralizeTargetColors"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("neutralizeToColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("neutralizeToMetallic"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("addUnderwearToBasePhoto"));
        }
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("textureToPhoto"));
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("overwriteExistingPhotos"));
        var destinationFolder = serializedObject.FindProperty("destinationFolder").stringValue;
        var dFolderRect = EditorGUILayout.GetControlRect();
        var dFolderLabel = dFolderRect;
        var dFolderField = dFolderRect;
        dFolderLabel.width = dFolderRect.width / 3;
        dFolderField.width = (dFolderRect.width / 3 * 2);
        dFolderField.x = dFolderField.x + dFolderRect.width / 3;
        EditorGUILayout.BeginHorizontal();
        EditorGUI.LabelField(dFolderLabel,"Destination Folder");
        EditorGUI.BeginDisabledGroup(true);
        destinationFolder = EditorGUI.TextField(dFolderField,destinationFolder);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Choose DestinationFolder"))
        {
            var path = EditorUtility.OpenFolderPanel("Destination Folder for Photos", Application.dataPath+"/"+destinationFolder, "");
            if(path != "")
            {
                if(path.IndexOf(Application.dataPath) == -1)
                {
                    Debug.Log("Folder path must be inside Assets folder");
                }
                else
                {
                    serializedObject.FindProperty("destinationFolder").stringValue = path.Replace(Application.dataPath+"/","");
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
        serializedObject.ApplyModifiedProperties();
        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            if (doingTakePhoto)
            {
                EditorGUI.BeginDisabledGroup(true);
            }
            if (GUILayout.Button("Take Photo(s)"))
            {
                thisPB.TakePhotos();
            }
            if (doingTakePhoto)
            {
                EditorGUI.EndDisabledGroup();
            }
        }
    }

}
