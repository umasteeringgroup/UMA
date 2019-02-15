#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
	[CustomEditor(typeof(DynamicUMADnaAsset))]
	public class DynamicUMADnaAssetEditor : Editor
	{
		//The DNAAsset field now has an 'Inspect' button, that shows the asset in a popup window. This makes it easier for users to change their dna names
		//without loosing track of the behaviour they were inspecting that uses the DNA Asset (particularly helpful when customizing in playmode)
		private static DynamicUMADnaAssetEditor _livePopupEditor;

		private string newDNAName = "";
		private bool canAddNewDNAName = false;


		int DNAAssetPickerID = 0;

		List<string> dnaNamesAddOpts = new List<string> { "Add", "Replace" };

		int selectedAddMethod = 0;

		bool editTypeHashEnabled = false;
		bool _importToolsExpanded = false;

		bool _helpIsExpanded = false;

		private Texture2D _importIcon;
		private GUIStyle _importStyle;
		private GUIContent _importContent;

		private Texture2D importIcon
		{
			get
			{
				if (_importIcon != null)
					return _importIcon;
				//Chek EditorStyles has been set up
				if (EditorStyles.label == null)
					return new Texture2D(4, 4);
				_importIcon = EditorGUIUtility.FindTexture("CollabPull");
				return _importIcon;
			}
		}

		private GUIContent importContent
		{
			get
			{
				_importStyle= new GUIStyle();
				if (_importContent != null && _importIcon != null)
					return _importContent;
				//Check EditorStyles has been set up
				if (EditorStyles.label == null)
					return new GUIContent("", "Show Import Names Tools");
				_importContent = new GUIContent("", "Show Import Names Tools");
				_importStyle = new GUIStyle(EditorStyles.label);
				_importStyle.fixedHeight = importIcon.height + 4f;
				_importStyle.contentOffset = new Vector2(-4f, -0f);
				_importContent.image = _importIcon;
				return _importContent;
			}
		}

		//this is an array because the help drawer takes an array so it can draw multiple paragraphs
		string[] _help = new string[]
		{
			"The 'Dynamic DNA Asset' defines the dna 'names' that 'DNA Converters' can use to make changes to your character.",
		};

		string _dnaTypehashTooltip = "The 'DNA Type Hash' is a unique identifier for this collection of names, you should only edit it if you find that you are having dna collisions that you will be notified about.";

		bool initialized = false;

		ReorderableList _dnaNameList;
		List<int> _removeList = new List<int>();

		DynamicUMADnaAsset thisDUDA;

		public static DynamicUMADnaAssetEditor livePopupEditor
		{
			get { return _livePopupEditor; }
		}

		public static void SetLivePopupEditor(DynamicUMADnaAssetEditor liveDUDAEditor)
		{
			if (Application.isPlaying)
				_livePopupEditor = liveDUDAEditor;
		}

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

		private void OnEnable()
		{
			_dnaNameList = new ReorderableList(serializedObject, serializedObject.FindProperty("Names"), true, true, false, false);
			_dnaNameList.drawElementCallback = DrawElementCallback;
			_dnaNameList.drawHeaderCallback = DrawHeaderCallback;
			_dnaNameList.headerHeight = 0f;
			_dnaNameList.footerHeight = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2);
			//we really want the add names controls to go in a modified footer for the list for consistancy
			_dnaNameList.drawFooterCallback = DrawFooterCallback;
		}

		public override void OnInspectorGUI()
		{
			if (!initialized)
				Init();

			serializedObject.Update();
			GUILayout.Space(5);
			var dnaHeaderRect = EditorGUILayout.GetControlRect();

			GUIHelper.ToolbarStyleHeader(dnaHeaderRect, new GUIContent("DYNAMIC DNA"), _help, ref _helpIsExpanded);

			var importBtnRect = new Rect(dnaHeaderRect.xMax -40f, dnaHeaderRect.yMin + 2f, 20f, EditorGUIUtility.singleLineHeight);
			_importToolsExpanded = GUI.Toggle(importBtnRect, _importToolsExpanded, importContent, _importStyle);

			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));

			SerializedProperty Names = serializedObject.FindProperty("Names");

			if (_importToolsExpanded)
			{
				DrawImportNamesTools();
			}

			SerializedProperty dnaTypeHash = serializedObject.FindProperty("dnaTypeHash");

			Rect hashEditorRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
			var hashLabelRect = hashEditorRect;
			hashLabelRect.xMax = hashEditorRect.xMax / 3;
			var hashBtnRect = hashEditorRect;
			hashBtnRect.xMin = hashLabelRect.xMax + (EditorGUI.indentLevel * 20);
			hashBtnRect.xMax = hashBtnRect.xMin + 50 + (EditorGUI.indentLevel * 20);
			var hashFieldRect = hashEditorRect;
			hashFieldRect.xMin = hashBtnRect.xMax - ((EditorGUI.indentLevel * 20) - 10);
			if (editTypeHashEnabled)
			{
				EditorGUI.LabelField(hashLabelRect, new GUIContent(dnaTypeHash.displayName, _dnaTypehashTooltip));
				if (GUI.Button(hashBtnRect, new GUIContent("Save", _dnaTypehashTooltip)))
				{
					editTypeHashEnabled = false;
				}
				var originalDnaTypeHash = dnaTypeHash;
				EditorGUI.BeginChangeCheck();
				EditorGUI.PropertyField(hashFieldRect, dnaTypeHash, new GUIContent(""));
				if (EditorGUI.EndChangeCheck())
				{
					//we MUST NOT let this have the same TypeHash as UMADnaHumanoid or UMADnaTutorial, so if people randomly choose that value- dont assign it
					if (dnaTypeHash.intValue == UMAUtils.StringToHash("UMADnaHumanoid") || dnaTypeHash.intValue == UMAUtils.StringToHash("UMADnaTutorial"))
					{
						Debug.LogWarning("You are trying to set a DynamicDNA to the same hash as a UMADnaHumanoid or UMADnaTutorial dna- this is not allowed");
						dnaTypeHash = originalDnaTypeHash;
					}
					else
					{
						serializedObject.ApplyModifiedProperties();
					}
				}
			}
			else
			{
				EditorGUI.LabelField(hashLabelRect, new GUIContent(dnaTypeHash.displayName, _dnaTypehashTooltip));
				if (GUI.Button(hashBtnRect, new GUIContent("Edit", _dnaTypehashTooltip)))
				{
					if (EditorUtility.DisplayDialog("Really Change the Hash?", "If you change the DNA Assets hash, any recipes that use this DNA will need to be inspected so they update to the new value. Are you sure?", "Yes", "Cancel"))
						editTypeHashEnabled = true;
				}
				EditorGUI.BeginDisabledGroup(true);
				EditorGUI.PropertyField(hashFieldRect, dnaTypeHash, new GUIContent(""));
				EditorGUI.EndDisabledGroup();
			}
			EditorGUILayout.Space();

			if (Names.arraySize == 0)
			{
				EditorGUILayout.HelpBox("Define your the names for your dna by adding them below", MessageType.Info);
			}
			//ACTUAL NAMES LIST
			_dnaNameList.DoLayoutList();
			//ADD NEW NAME BUTTON
			//Moved Add controls into the Reorderable list footer for consistancy

			//message that the name exists
			if (!string.IsNullOrEmpty(newDNAName) && canAddNewDNAName == false)
			{
				EditorGUILayout.HelpBox("That name is already in use.", MessageType.Warning);
			}

			//Clear out indices that have been added to the remove list.
			for (int i = _dnaNameList.serializedProperty.arraySize - 1; i >= 0; i--)
			{
				if (_removeList.Contains(i))
					_dnaNameList.serializedProperty.DeleteArrayElementAtIndex(i);
			}
			_removeList.Clear();


			serializedObject.ApplyModifiedProperties();

			GUIHelper.EndVerticalPadded(3);
			GUILayout.Space(5);
		}

		private void DrawImportNamesTools()
		{
			SerializedProperty Names = serializedObject.FindProperty("Names");
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

			//Add Defaults Buttons
			Rect defaultsButRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
			defaultsButRect.xMin = defaultsButRect.xMin + (EditorGUI.indentLevel * 15);
			if (GUI.Button(defaultsButRect, new GUIContent("Import Default Names", "Adds the default names as used by UMA Human Male DNA")))
			{
				AddDefaultNames();
			}
			EditorGUILayout.Space();
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
			for (int i = 0; i < tempDNAAsset.Names.Length; i++)
			{
				if (!newNames.Contains(tempDNAAsset.Names[i]))
					newNames.Add(tempDNAAsset.Names[i]);
			}
			dnaNames = newNames;
			(target as DynamicUMADnaAsset).Names = dnaNames.ToArray();
		}

		private void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
		{
			SerializedProperty element = _dnaNameList.serializedProperty.GetArrayElementAtIndex(index);
			rect.y += 2;
			EditorGUI.LabelField(new Rect(rect.x, rect.y, 25, EditorGUIUtility.singleLineHeight), index.ToString());
			var fieldRect = new Rect(rect.x + 25, rect.y, rect.width - 50, EditorGUIUtility.singleLineHeight);
			var delRect = new Rect(fieldRect.xMax + 5, rect.y, 20, EditorGUIUtility.singleLineHeight);
			EditorGUI.PropertyField(fieldRect, element, GUIContent.none);
			if (GUI.Button(delRect, "x"))
			{
				_removeList.Add(index);
			}
		}

		private void DrawHeaderCallback(Rect rect)
		{
			//EditorGUI.LabelField(rect, "DNA Names List (" + _dnaNameList.serializedProperty.arraySize + ")", EditorStyles.helpBox);
		}

		private void DrawFooterCallback(Rect rect)
		{
			var Names = _dnaNameList.serializedProperty;
			var ROLDefaults = new ReorderableList.Defaults();
			var padding = 4f;
			var _addBtnWidth = 100f + padding;
			var _labelWidth = 68f;

			Rect addRect = rect;

			addRect.xMin = addRect.xMax - 420 > addRect.xMin ? addRect.xMax - 420 : addRect.xMin;
			addRect.height = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2);
			var addBtnRect = new Rect(rect.xMax - (_addBtnWidth) - (padding * 2), addRect.yMin, ((_addBtnWidth / 5) * 2) - padding, EditorGUIUtility.singleLineHeight);
			var fieldRect = new Rect(addRect.xMin + (padding * 2), addRect.yMin, addRect.width - _addBtnWidth - (padding * 6), EditorGUIUtility.singleLineHeight);
			var labelRect = new Rect(fieldRect.xMin, fieldRect.yMin, _labelWidth, fieldRect.height);
			fieldRect.xMin = labelRect.xMax + (padding);

			var clearBtnRect = new Rect(addBtnRect.xMax + padding, addRect.yMin, ((_addBtnWidth / 5) * 3), EditorGUIUtility.singleLineHeight);

			if (Event.current.type == EventType.Repaint)
			{
				var prevFooterFixedHeight = ROLDefaults.footerBackground.fixedHeight;
				ROLDefaults.footerBackground.fixedHeight = addRect.height;
				ROLDefaults.footerBackground.Draw(addRect, false, false, false, false);
				ROLDefaults.footerBackground.fixedHeight = prevFooterFixedHeight;
			}
			EditorGUI.LabelField(labelRect, new GUIContent("Add Name:", "Add a DNA Name to the list"));
			newDNAName = EditorGUI.TextField(fieldRect, newDNAName);
			if (newDNAName != "")
			{
				canAddNewDNAName = true;
				for (int ni = 0; ni < Names.arraySize; ni++)
				{
					if (Names.GetArrayElementAtIndex(ni).stringValue == newDNAName)
					{
						canAddNewDNAName = false;
					}
				}
			}
			if (GUI.Button(addBtnRect,"Add"))
			{
				if (newDNAName == "")
					return;
				if (canAddNewDNAName)
				{
					Names.arraySize = Names.arraySize + 1;
					Names.GetArrayElementAtIndex(Names.arraySize - 1).stringValue = newDNAName;
					Names.serializedObject.ApplyModifiedProperties();
					newDNAName = "";
					EditorGUIUtility.keyboardControl = 0;
				}
			}
			EditorGUI.BeginDisabledGroup(Names.arraySize == 0);
			if (GUI.Button(clearBtnRect, new GUIContent("Clear All", "Clears all the names from the list. Cannot be undone")))
			{
				if (EditorUtility.DisplayDialog("Really Clear All Names?", "This will delete all the names in the list and cannot be undone. Are you sure?", "Yes", "Cancel"))
				{
					Names.arraySize = 0;
					Names.serializedObject.ApplyModifiedProperties();

				}
			}
			EditorGUI.EndDisabledGroup();

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
