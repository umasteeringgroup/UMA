using UnityEditor;
using UnityEngine;
using UMA;
using System;
using System.Collections.Generic;

[CustomEditor(typeof(DNAGroup))]
public class DNAGroupEditor : Editor
{
    private SerializedProperty dnaAreaProp;
    private SerializedProperty dnaListProp;

    private void OnEnable()
    {
        dnaAreaProp = serializedObject.FindProperty("DNAArea");
        dnaListProp = serializedObject.FindProperty("dnaList");
    }

    public override void OnInspectorGUI()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorGUILayout.HelpBox("Editor is compiling or updating. Please wait.", MessageType.Info);
            return;
        }

        serializedObject.Update();

        EditorGUILayout.PropertyField(dnaAreaProp, new GUIContent("DNA Area"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("DNA List", EditorStyles.boldLabel);

        for (int i = 0; i < dnaListProp.arraySize; i++)
        {
            SerializedProperty dnaProp = dnaListProp.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = dnaProp.FindPropertyRelative("name");
            SerializedProperty descriptionProp = dnaProp.FindPropertyRelative("description");
            SerializedProperty defaultValueProp = dnaProp.FindPropertyRelative("defaultValue");
            SerializedProperty effectsProp = dnaProp.FindPropertyRelative("effects");

            if (nameProp == null) continue;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"DNA {i + 1}", EditorStyles.boldLabel);
            if (GUILayout.Button("Remove DNA", GUILayout.Width(90)))
            {
                dnaListProp.DeleteArrayElementAtIndex(i);
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(nameProp, new GUIContent("name"));
            EditorGUILayout.PropertyField(descriptionProp, new GUIContent("Description"));
            EditorGUILayout.Slider(defaultValueProp, 0f, 1f, new GUIContent("Default Value"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("DNA Effects", EditorStyles.boldLabel);

            for (int j = 0; j < effectsProp.arraySize; j++)
            {
                SerializedProperty effectProp = effectsProp.GetArrayElementAtIndex(j);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(effectProp, new GUIContent($"Effect {j + 1}"), true);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    effectsProp.DeleteArrayElementAtIndex(j);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add DNAEffect"))
            {
                effectsProp.arraySize++;
                effectsProp.GetArrayElementAtIndex(effectsProp.arraySize - 1).objectReferenceValue = null;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        if (GUILayout.Button("Add DNA"))
        {
            dnaListProp.arraySize++;
            var newDNA = dnaListProp.GetArrayElementAtIndex(dnaListProp.arraySize - 1);
            newDNA.FindPropertyRelative("name").stringValue = "";
            newDNA.FindPropertyRelative("description").stringValue = "";
            newDNA.FindPropertyRelative("defaultValue").floatValue = 0.5f;
            newDNA.FindPropertyRelative("effects").ClearArray();
        }

        serializedObject.ApplyModifiedProperties();
    }
}
