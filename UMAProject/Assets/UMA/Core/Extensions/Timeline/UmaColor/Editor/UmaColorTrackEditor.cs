using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(UmaColorTrack))]
public class UmaColorTrackEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty timeStep = serializedObject.FindProperty("timeStep");

        EditorGUILayout.PropertyField(timeStep);

        serializedObject.ApplyModifiedProperties();
    }

}
