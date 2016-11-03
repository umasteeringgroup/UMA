#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;


namespace UMAEditor
{
    [CustomEditor(typeof(DynamicUMADnaAsset))]
    public class DynamicUMADnaAssetEditor : Editor {

        [MenuItem("Assets/Create/Dynamic UMA DNA Asset")]
        public static void CreateDynamicUMADnaMenuItem()
        {
            CustomAssetUtility.CreateAsset<DynamicUMADnaAsset>();
        }

        public string newDNAName = "";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty Names = serializedObject.FindProperty("Names");
            Names.isExpanded = EditorGUILayout.Foldout(Names.isExpanded, "DNA Slider Names ("+ Names.arraySize+")");
            if (Names.isExpanded)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < Names.arraySize; i++)
                {
                    var origName = Names.GetArrayElementAtIndex(i).stringValue;
                    var newName = origName;
                    Rect propRect = EditorGUILayout.GetControlRect(false);
                    Rect fieldRect = propRect;
                    Rect delRect = propRect;
                    fieldRect.width = fieldRect.width - 80f;
                    delRect.x = delRect.x + fieldRect.width + 5f;
                    delRect.width = 75f;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    newName = EditorGUI.TextField(fieldRect,"",newName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if(newName != origName && newName != "")
                        {
                            Names.GetArrayElementAtIndex(i).stringValue = newName;
                            serializedObject.ApplyModifiedProperties();
                        }
                    }
                    if (GUI.Button(delRect, "Delete"))
                    {
                        Names.DeleteArrayElementAtIndex(i);
                        continue;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space();
                //add new button
                EditorGUILayout.BeginHorizontal();
                var buttonDisabled = newDNAName == "";
                bool canAdd = true;
                EditorGUI.BeginChangeCheck();
                newDNAName = EditorGUILayout.TextField(newDNAName);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newDNAName != "" && canAdd)
                    {
                        buttonDisabled = false;
                    }
                }
                //check the name is unique
                if (newDNAName != "")
                {
                    for (int ni = 0; ni < Names.arraySize; ni++)
                    {
                        if (Names.GetArrayElementAtIndex(ni).stringValue == newDNAName)
                        {
                            canAdd = false;
                            buttonDisabled = true;
                        }
                    }
                }
                if (buttonDisabled)
                {
                    EditorGUI.BeginDisabledGroup(true);
                }
                if (GUILayout.Button("Add DNA Name"))
                {
                    if (canAdd)
                    {
                        var numNames = Names.arraySize;
                        Names.InsertArrayElementAtIndex(numNames);
                        Names.GetArrayElementAtIndex(numNames).stringValue = newDNAName;
                        Names.serializedObject.ApplyModifiedProperties();
                        newDNAName = "";
                    }
                }
                if (buttonDisabled)
                {
                    EditorGUI.EndDisabledGroup();
                }
                EditorGUILayout.EndHorizontal();
                if (canAdd == false)
                {
                    EditorGUILayout.HelpBox("That name is already in use.", MessageType.Warning);
                }
                Names.serializedObject.ApplyModifiedProperties();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }
        }
    }
}
#endif
