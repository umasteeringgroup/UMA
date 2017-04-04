#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
    [CustomEditor(typeof(DynamicUMADnaAsset))]
    public class DynamicUMADnaAssetEditor : Editor
	{
		public string newDNAName = "";

		int DNAAssetPickerID = 0;

		List<string> dnaNamesAddOpts = new List<string> { "Add", "Replace" };

		int selectedAddMethod = 0;

		bool editTypeHashEnabled = false;
		bool otherAddOptionsOpen = false;

		bool initialized = false;

		DynamicUMADnaAsset thisDUDA;

		public void Init()
		{
			thisDUDA = target as DynamicUMADnaAsset;
			//check the ID and paths
			bool doUpdate = thisDUDA.SetCurrentAssetPath();
			if (doUpdate)
			{
				serializedObject.Update();
			}
			initialized = true;
		}

		private void ImportDNADropArea(Rect dropArea, List<string> dnaNames, int addMethod)
		{
			Event evt = Event.current;
			//make the box clickable so that the user can select DynamicUMADnaAsset assets from the asset selection window
			if (evt.type == EventType.MouseUp)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DNAAssetPickerID = EditorGUIUtility.GetControlID(new GUIContent("DNAAssetPicker"), FocusType.Passive);
					EditorGUIUtility.ShowObjectPicker<DynamicUMADnaAsset>(null, false, "", DNAAssetPickerID);
					Event.current.Use();//stops the Mismatched LayoutGroup errors
					return;
				}
			}
			if (evt.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == DNAAssetPickerID)
			{
				DynamicUMADnaAsset tempDnaAsset = EditorGUIUtility.GetObjectPickerObject() as DynamicUMADnaAsset;
				if (tempDnaAsset)
				{
					AddDNANames(tempDnaAsset, dnaNames, addMethod);
				}
				Event.current.Use();//stops the Mismatched LayoutGroup errors
				return;
			}
			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
			}
			if (evt.type == EventType.DragPerform)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.AcceptDrag();

					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
					for (int i = 0; i < draggedObjects.Length; i++)
					{
						if (draggedObjects[i])
						{
							DynamicUMADnaAsset tempDnaAsset = draggedObjects[i] as DynamicUMADnaAsset;
							if (tempDnaAsset)
							{
								AddDNANames(tempDnaAsset, dnaNames, addMethod);
								continue;
							}

							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (System.IO.Directory.Exists(path))
							{
								RecursiveScanFoldersForAssets(path, dnaNames, addMethod);
							}
						}
					}
				}
			}
		}

		private void RecursiveScanFoldersForAssets(string path, List<string> dnaNames, int addMethod)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				var tempDNAAsset = AssetDatabase.LoadAssetAtPath(assetFile, typeof(DynamicUMADnaAsset)) as DynamicUMADnaAsset;
				if (tempDNAAsset)
				{
					AddDNANames(tempDNAAsset, dnaNames, addMethod);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), dnaNames, addMethod);
			}
		}

		private void AddDNANames(DynamicUMADnaAsset tempDNAAsset, List<string> dnaNames, int addMethod)
		{
			List<string> newNames = addMethod == 0 ? new List<string>(dnaNames) : new List<string>();
			for(int i = 0; i < tempDNAAsset.Names.Length; i++)
			{
				if (!newNames.Contains(tempDNAAsset.Names[i]))
					newNames.Add(tempDNAAsset.Names[i]);
			}
			dnaNames = newNames;
			(target as DynamicUMADnaAsset).Names = dnaNames.ToArray();
        }

		public override void OnInspectorGUI()
        {
			if (!initialized)
				Init();

            serializedObject.Update();
			/*
			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("lastKnownAssetPath"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("lastKnownDuplicateAssetPath"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("lastKnownInstanceID"));
			EditorGUI.EndDisabledGroup();*/

			SerializedProperty dnaTypeHash = serializedObject.FindProperty ("dnaTypeHash");

			Rect hashEditorRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
			var hashLabelRect = hashEditorRect;
			hashLabelRect.xMax = hashEditorRect.xMax / 3;
			var hashBtnRect = hashEditorRect;
			hashBtnRect.xMin = hashLabelRect.xMax + (EditorGUI.indentLevel * 20);
			hashBtnRect.xMax = hashBtnRect.xMin + 50 + (EditorGUI.indentLevel * 20);
			var hashFieldRect = hashEditorRect;
			hashFieldRect.xMin = hashBtnRect.xMax - ((EditorGUI.indentLevel * 20)-10);
            if (editTypeHashEnabled)
			{
				//EditorGUILayout.BeginHorizontal();
				EditorGUI.LabelField(hashLabelRect, new GUIContent(dnaTypeHash.displayName,dnaTypeHash.tooltip));
				if(GUI.Button(hashBtnRect,"Save")){
					editTypeHashEnabled = false;
                }
				var originalDnaTypeHash = dnaTypeHash;
				EditorGUI.BeginChangeCheck();
				EditorGUI.PropertyField(hashFieldRect, dnaTypeHash, new GUIContent(""));
				if (EditorGUI.EndChangeCheck())
				{
					//we MUST NOT let this have the same TypeHash as UMADnaHumanoid or UMADnaTutorial, so if people randomly choose that value- dont assign it
					if(dnaTypeHash.intValue == UMAUtils.StringToHash("UMADnaHumanoid") || dnaTypeHash.intValue == UMAUtils.StringToHash("UMADnaTutorial"))
					{
						Debug.LogWarning("You are trying to set a DynamicDNA to the same hash as a UMADnaHumanoid or UMADnaTutorial dna- this is not allowed");
						dnaTypeHash = originalDnaTypeHash;
					}
					else
					{
						serializedObject.ApplyModifiedProperties();
					}
				}
				//EditorGUILayout.EndHorizontal();
			}
			else
			{
				//EditorGUILayout.BeginHorizontal();
				EditorGUI.LabelField(hashLabelRect, new GUIContent(dnaTypeHash.displayName, dnaTypeHash.tooltip));
				if (GUI.Button(hashBtnRect,"Edit"))
				{
					if(EditorUtility.DisplayDialog("Really Change the Hash?", "If you change the DNA Assets hash, any recipes that use this DNA will need to be inspected so they update to the new value. Are you sure?","Yes", "Cancel"))
					editTypeHashEnabled = true;
				}
				EditorGUI.BeginDisabledGroup(true);
				EditorGUI.PropertyField(hashFieldRect, dnaTypeHash, new GUIContent(""));
				EditorGUI.EndDisabledGroup();
				//EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.Space();
			SerializedProperty Names = serializedObject.FindProperty("Names");

			if (Names.arraySize == 0)
			{
				EditorGUILayout.HelpBox("Define your the names for you dna by adding them below", MessageType.Info);
			}
			//OTHER OPTIONS FOR ADDING/DELETING NAMES - show in a foldout
			EditorGUI.indentLevel++;
			otherAddOptionsOpen = EditorGUILayout.Foldout(otherAddOptionsOpen, "Add/Delete Names Options");
			EditorGUI.indentLevel--;
			//
			if (otherAddOptionsOpen)
			{
				//drop area for importing names from other dna assets
				var dropArea = GUILayoutUtility.GetRect(0.0f, 60.0f, GUILayout.ExpandWidth(true));
				dropArea.xMin = dropArea.xMin + (EditorGUI.indentLevel * 15);
				GUI.Box(dropArea, "Drag DynamicUMADNAAssets here to import their names. Click to pick.");
				var AddMethods = new GUIContent[dnaNamesAddOpts.Count];
				for (int i = 0; i < dnaNamesAddOpts.Count; i++)
					AddMethods[i] = new GUIContent(dnaNamesAddOpts[i]);
				Rect selectedAddMethodRect = dropArea;
				selectedAddMethodRect.yMin = dropArea.yMax - EditorGUIUtility.singleLineHeight - 5;
				selectedAddMethodRect.xMin = dropArea.xMin - ((EditorGUI.indentLevel * 10) - 10);
				selectedAddMethodRect.xMax = dropArea.xMax - ((EditorGUI.indentLevel * 10) + 10);
				selectedAddMethod = EditorGUI.Popup(selectedAddMethodRect, new GUIContent("On Import", "Choose whether to 'Add' the names to the current list, or 'Replace' the names with the new list"), selectedAddMethod, AddMethods);

				var namesList = new List<string>(Names.arraySize);
				for (int i = 0; i < Names.arraySize; i++)
					namesList.Add(Names.GetArrayElementAtIndex(i).stringValue);

				ImportDNADropArea(dropArea, namesList, selectedAddMethod);

				EditorGUILayout.Space();

				//Clear all and Add Defaults Buttons
				Rect clearAndDefaultsRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
				clearAndDefaultsRect.xMin = clearAndDefaultsRect.xMin + (EditorGUI.indentLevel * 15);
				var defaultsButRect = clearAndDefaultsRect;
				var clearButRect = clearAndDefaultsRect;
				defaultsButRect.width = clearAndDefaultsRect.width / 2;
				clearButRect.xMin = defaultsButRect.xMax;
				clearButRect.width = clearAndDefaultsRect.width / 2;
				if (GUI.Button(defaultsButRect, new GUIContent("Add Default Names", "Adds the default names as used by UMA Human Male DNA")))
				{
					AddDefaultNames();
				}
				EditorGUI.BeginDisabledGroup(Names.arraySize == 0);
				if (GUI.Button(clearButRect, new GUIContent("Clear All Names", "Clears the current names. Cannot be undone.")))
				{
					if (EditorUtility.DisplayDialog("Really Clear All Names?", "This will delete all the names in the list and cannot be undone. Are you sure?", "Yes", "Cancel"))
						(target as DynamicUMADnaAsset).Names = new string[0];
				}
				EditorGUI.EndDisabledGroup();
				EditorGUILayout.Space();
			}
			//ADD NEW NAME BUTTON
			EditorGUILayout.BeginHorizontal();
			bool canAdd = true;
			EditorGUI.BeginChangeCheck();
			newDNAName = EditorGUILayout.TextField(newDNAName);//this wont bloody clear after the name is added
			if (EditorGUI.EndChangeCheck())
			{
				//checking the text field seems to only work if its done OUTSIDE this change check ?!?!
			}
			//check the name is unique
			if (newDNAName != "")
			{
				for (int ni = 0; ni < Names.arraySize; ni++)
				{
					if (Names.GetArrayElementAtIndex(ni).stringValue == newDNAName)
					{
						canAdd = false;
					}
				}
			}
			if (GUILayout.Button("Add DNA Name"))
			{
				if (newDNAName == "")
					return;
				if (canAdd)
				{
					//var numNames = Names.arraySize;
					Names.InsertArrayElementAtIndex(0);
					Names.GetArrayElementAtIndex(0).stringValue = newDNAName;
					Names.serializedObject.ApplyModifiedProperties();
					newDNAName = "";
					EditorGUIUtility.keyboardControl = 0;
				}
			}
			EditorGUILayout.EndHorizontal();
			//message that the name exists
			if (canAdd == false)
			{
				EditorGUILayout.HelpBox("That name is already in use.", MessageType.Warning);
			}
			//ACTUAL NAMES LIST
			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUILayout.LabelField("DNA Names List (" + Names.arraySize + ")", EditorStyles.helpBox);
			if (Names.arraySize > 0)
			{
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
					newName = EditorGUI.TextField(fieldRect, "", newName);
					if (EditorGUI.EndChangeCheck())
					{
						if (newName != origName && newName != "")
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
				Names.serializedObject.ApplyModifiedProperties();
			}
			GUIHelper.EndVerticalPadded(3);
        }
		protected void AddDefaultNames()
		{
			string[] defaultNames = new string[]
			{
				"height",
					"headSize",
					"headWidth",
					"neckThickness",
					"armLength",
					"forearmLength",
					"armWidth",
					"forearmWidth",
					"handsSize",
					"feetSize",
					"legSeparation",
					"upperMuscle",
					"lowerMuscle",
					"upperWeight",
					"lowerWeight",
					"legsSize",
					"belly",
					"waist",
					"gluteusSize",
					"earsSize",
					"earsPosition",
					"earsRotation",
					"noseSize",
					"noseCurve",
					"noseWidth",
					"noseInclination",
					"nosePosition",
					"nosePronounced",
					"noseFlatten",
					"chinSize",
					"chinPronounced",
					"chinPosition",
					"mandibleSize",
					"jawsSize",
					"jawsPosition",
					"cheekSize",
					"cheekPosition",
					"lowCheekPronounced",
					"lowCheekPosition",
					"foreheadSize",
					"foreheadPosition",
					"lipsSize",
					"mouthSize",
					"eyeRotation",
					"eyeSize",
					"breastSize"
			};
			List<string> currentNames = new List<string>((target as DynamicUMADnaAsset).Names);
			for(int i = 0; i < defaultNames.Length; i++)
			{
				if (!currentNames.Contains(defaultNames[i]))
					currentNames.Add(defaultNames[i]);
			}
			(target as DynamicUMADnaAsset).Names = currentNames.ToArray();
        }
    }
}
#endif
