using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMAEditor
{
	[CustomEditor(typeof(UMAResourcesIndex), true)]
	public class UMAResourcesIndexEditor : Editor
	{
		UMAResourcesIndex thisURI;

		string info;

		public override void OnInspectorGUI()
		{
			thisURI = target as UMAResourcesIndex;
			//we need to get the inspector to update to show any data that was added while the game was running?
			thisURI.LoadOrCreateData();
			serializedObject.Update();
			DrawPropertiesExcluding(serializedObject, new string[] { "enableDynamicIndexing", "Index" });
			info = thisURI.GetIndexInfo();
			EditorGUILayout.HelpBox(info, MessageType.Info);
			EditorGUILayout.HelpBox("Dynamic Indexing will automatically add any new assets you add to your Resources folders to the index as they are requested. But this is slow. Before you build, click the 'Create/Update Index' button below and turn off Dynamic Indexing.", MessageType.Info);
			EditorGUI.BeginChangeCheck();
			serializedObject.FindProperty("enableDynamicIndexing").boolValue = EditorGUILayout.ToggleLeft("Enable Dynamic Indexing", serializedObject.FindProperty("enableDynamicIndexing").boolValue);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				info = thisURI.GetIndexInfo();
			}
			EditorGUILayout.BeginHorizontal();
			EditorGUI.BeginChangeCheck();
			if (GUILayout.Button("Create/Update Index"))
			{
				thisURI.IndexAllResources();
				serializedObject.Update();
				info = thisURI.GetIndexInfo();
				GUI.changed = true;
			}
			if (GUILayout.Button("Clear Index"))
			{
				thisURI.ClearIndex();
				serializedObject.Update();
				info = thisURI.GetIndexInfo();
				GUI.changed = true;
			}
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				info = thisURI.GetIndexInfo();
			}
			EditorGUILayout.EndHorizontal();
			serializedObject.ApplyModifiedProperties();

			if (GUI.changed)
			{
				EditorUtility.SetDirty(target);
				//serializedObject.ApplyModifiedProperties();
			}
		}
	}
}
