using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
	[CustomEditor(typeof(SharedColorTable))]
	public class SharedColorTableEditor : Editor 
	{
        public override void OnInspectorGUI()
        {
            SharedColorTable sct = target as SharedColorTable;
            if (sct == null)
            {
                return;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("sharedColorName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("channelCount"));
            EditorGUILayout.LabelField("Shared Color Table", EditorStyles.boldLabel);
            if (GUILayout.Button("Add New Color"))
            {
                OverlayColorData newColor = new OverlayColorData(sct.channelCount);
                newColor.name = "New Color";
                sct.colors = sct.colors.Concat(new OverlayColorData[] { newColor }).ToArray();
                serializedObject.Update();
            }
            //
            //EditorGUILayout.PropertyField(serializedObject.FindProperty("colors"), true);
            //
            bool hasDeletes = false;
            for (int i=0; i<sct.colors.Length; i++) 
            {
                var c = serializedObject.FindProperty("colors").GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(c, true);
                var deleteThis = c.FindPropertyRelative("deleteThis");
                if (deleteThis.boolValue == true)
                {
                    hasDeletes = true;
                }
            }
            serializedObject.ApplyModifiedProperties();
            if (hasDeletes)
            {
                serializedObject.Update();
                for (int i = 0; i < sct.colors.Length; i++)
                {
                    var c = serializedObject.FindProperty("colors").GetArrayElementAtIndex(i);
                    var deleteThis = c.FindPropertyRelative("deleteThis");
                    if (deleteThis.boolValue == true)
                    {
                        sct.colors = sct.colors.Where((source, index) => index != i).ToArray();
                        serializedObject.FindProperty("colors").DeleteArrayElementAtIndex(i);
                        i--;
                    }
                }
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}

