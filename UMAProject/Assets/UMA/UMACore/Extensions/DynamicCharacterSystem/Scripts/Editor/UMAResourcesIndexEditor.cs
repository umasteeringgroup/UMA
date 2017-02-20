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

		//Temporarily show The UMAAssetIndex in here
		Editor UAIE;
		UMAAssetIndex UAI;

		public override void OnInspectorGUI()
		{
			EditorGUILayout.HelpBox("TEMPORARY: For the time being I am showing the UMAAssetIndex in here. Once we have confirmed we want to use this, I will replace the UMAResourcesIndex MB with a UMAAssetIndexViewer or similar", MessageType.Warning);
			if (UAI == null)
				UAI = UMAAssetIndex.Instance;
			Editor.CreateCachedEditor(UAI, typeof(UMAAssetIndexEditor), ref UAIE);
			UAIE.OnInspectorGUI();

			/*thisURI = target as UMAResourcesIndex;
			//we need to get the inspector to update to show any data that was added while the game was running?
			thisURI.LoadOrCreateData();
			serializedObject.Update();
			DrawPropertiesExcluding(serializedObject, new string[] { "enableDynamicIndexing", "Index", "duplicateNamesIndex" });
			info = thisURI.GetIndexInfo();
			EditorGUILayout.HelpBox(info, MessageType.Info);
			//give the user info about assets with duplicate names
			var duplicateNamesIndex = serializedObject.FindProperty("duplicateNamesIndex");
			var duplicateNamesData = duplicateNamesIndex.FindPropertyRelative("data");
			//var windowWidth = EditorGUILayout.
			if (duplicateNamesData.arraySize > 0)
			{
				EditorGUILayout.HelpBox("You have duplicate asset names in your Resources folders.", MessageType.Warning);
				duplicateNamesIndex.isExpanded = EditorGUILayout.Foldout(duplicateNamesIndex.isExpanded, "Duplicate Names Info");
				if (duplicateNamesIndex.isExpanded)
				{
					EditorGUI.indentLevel++;
					//do we have UMA asset duplicate names- if we do show the umaAsset duplicate names info box
					bool haveUMADupes = false;
					for(int i = 0; i < duplicateNamesData.arraySize; i++)
					{
						//data is an array of TypeIndex
						string thisType = (string)duplicateNamesData.GetArrayElementAtIndex(i).FindPropertyRelative("type").stringValue;
						if(thisType.IndexOf("RaceData") > 0 || thisType.IndexOf("SlotDataAsset") > 0 || thisType.IndexOf("OverlayDataAsset") > 0)
						{
							haveUMADupes = true;
						}				
					}
					if (haveUMADupes)
					{
						EditorGUILayout.HelpBox("With UMA RaceData/Slot/Overlay assets you *can* give assets the same name but you need to ensure that the 'Racename/SlotName/OverlayName', that you see when you inspect those assets, is unique. Recipes need unique asset names.", MessageType.Info);
					}
					//then show the path to each of the assets that have an issue
					for (int i = 0; i < duplicateNamesData.arraySize; i++)
					{
						var thisDuplicateNamesData = duplicateNamesData.GetArrayElementAtIndex(i);
						//type 
                        string thisType = (string)thisDuplicateNamesData.FindPropertyRelative("type").stringValue;
						string thisTypeLabel = thisType;
						string namePrefix = "asset ";
						if (haveUMADupes)
						{
							if (thisType.IndexOf("RaceData")>-1)
							{
								thisTypeLabel = "RaceData";
								namePrefix = "race";
							}
							if (thisType.IndexOf("SlotDataAsset") > -1)
							{
								thisTypeLabel = "SlotData";
								namePrefix = "slot";
							}
							if (thisType.IndexOf("OverlayDataAsset") > -1)
							{
								thisTypeLabel = "OverlayData";
								namePrefix = "overlay";
							}
						}
                        EditorGUILayout.LabelField(thisTypeLabel + " duplicated "+namePrefix+"names:");
						//now we need typeFiles
						for (int ti = 0; ti < thisDuplicateNamesData.FindPropertyRelative("typeFiles").arraySize; ti++)
						{
							GUILayout.BeginHorizontal();
							GUILayout.Space(15);
							GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));
							var thisNameHash = thisDuplicateNamesData.FindPropertyRelative("typeFiles").GetArrayElementAtIndex(ti).FindPropertyRelative("nameHash").intValue;
							var thispath = thisDuplicateNamesData.FindPropertyRelative("typeFiles").GetArrayElementAtIndex(ti).FindPropertyRelative("fullPath").stringValue;
							//we want to say that the indexed item at index path has the same name as the item at thispath
							var origWordWrap = EditorStyles.label.wordWrap;
							EditorStyles.label.wordWrap = true;
                            EditorGUILayout.HelpBox(thispath,MessageType.None);
							EditorGUILayout.LabelField("Has the same "+namePrefix+"name as");
							//we cant get this info
							var dupePath = (target as UMAResourcesIndex).Index.GetPath(thisType, thisNameHash, true);
							EditorGUILayout.HelpBox(dupePath +" (wont be used)", MessageType.None);
							EditorStyles.label.wordWrap = origWordWrap;
                            GUIHelper.EndVerticalPadded(5);
							GUILayout.EndHorizontal();
						}
						EditorGUILayout.Space();
					}
					EditorGUI.indentLevel--;
				}
            }
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
			//if (GUILayout.Button("Clear Index"))
			//{
			//	thisURI.ClearIndex();
			//	serializedObject.Update();
			//	info = thisURI.GetIndexInfo();
			//	GUI.changed = true;
			//}
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
			*/
		}
	}
}
