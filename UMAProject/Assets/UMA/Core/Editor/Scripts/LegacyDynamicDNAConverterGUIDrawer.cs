using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
	/// <summary>
	/// DO NOT USE. A utility class for drawing DynamicDNAConverterBehaviours legacy GUI. Will be removed in a future version
	/// </summary>
	public class LegacyDynamicDNAConverterGUIDrawer
	{
		DynamicDNAConverterBehaviour target;
		SerializedObject serializedObject;
		private UMAData umaData = null;
		private DynamicDNAConverterCustomizer thisDDCC = null;

		private bool minimalMode = false;
		private List<string> bonesInSkeleton = new List<string>();

		private string _skelModsTip = "Skeleton Modifiers control how the values of the DNA you set above are applied to the skeleton.  So for example 'Upper Weight' affects the scale of the Spine, breast, belly and shoulder bones in different ways. Add or edit a skeleton modifier in the list below to use a given dna value to modify the skeleton. The 'Value Modifiers' part of a Skeleton Modifier, takes the incoming value, modifies it by the settings and applies it to the bone. The Min and Max values are what that result will be 'clamped' to. Changes to the base 'Value' affect all characters using this converter are are applied regardless of any dna. Consider using a starting 'BonePose' instead for more control.";

		bool skeletonModifiersInfoExpanded = false;
		bool extraSkelAddDelOptsExpanded = false;
		bool startingPoseExpanded = false;
		bool startingPoseInfoExpanded = false;

		private SkeletonModifierPropertyDrawer _skelModPropDrawer = null;
		List<string> dnaNamesAddOpts = new List<string> { "Add", "Replace" };
		int selectedModifiersAddMethod = 0;
		private int selectedAddHash = 0;
		private int selectedAddProp = 0;
		private string addSkelBoneName = "";
		private int addSkelBoneHash = 0;
		bool canAddSkel = false;
		bool alreadyExistedSkel = false;

		private string skeletonModifiersFilter = "";
		private int skeletonModifiersFilterType = 0;
		private string[] skeletonModifiersFilterTypeList = new string[] { "Bone Name", "Position Modifiers", "Rotation Modifiers", "Scale Modifiers", "DNA", "Adjust Bones", "Non-Adjust Bones" };

		private string createBonePoseAssetName = "";

		GUIStyle foldoutTipStyle;

		bool initialized = false;

		public void Init(DynamicDNAConverterBehaviour targetDDCB, SerializedObject DDCBSO, UMAData umaData, DynamicDNAConverterCustomizer DDCC, List<string> bonesList)
		{
			if (_skelModPropDrawer == null)
				_skelModPropDrawer = new SkeletonModifierPropertyDrawer();
			_skelModPropDrawer.AllowLegacyDNADrawer = true;
			target = targetDDCB;
			serializedObject = DDCBSO;
			this.umaData = umaData;
			thisDDCC = DDCC;
			bonesInSkeleton = bonesList;
			this.minimalMode = thisDDCC != null;
			UpdateNames();
			//Style for Tips
			foldoutTipStyle = new GUIStyle(EditorStyles.foldout);
			foldoutTipStyle.fontStyle = FontStyle.Bold;

			initialized = true;
		}

		public void UpdateNames()
		{
			string[] dnaNames = null;
			DynamicUMADnaAsset dnaAsset = null;
			if(target != null)
				dnaAsset = target.dnaAsset;
			if (dnaAsset != null)
				_skelModPropDrawer.Init(dnaAsset.Names);
			else
				_skelModPropDrawer.Init(dnaNames);
			if (minimalMode)
				_skelModPropDrawer.bonesInSkeleton = bonesInSkeleton;
		}

		public void DrawLegacySkeletonModifiersGUI()
		{
			if (!initialized)
			{
				Debug.LogError("Please initialize an instance of LegacySkeletonModifierListDrawer before trying to use it!");
				return;
			}

			var skeletonModifiers = serializedObject.FindProperty("_skeletonModifiers");

			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUI.indentLevel++;

			skeletonModifiers.isExpanded = EditorGUILayout.Foldout(skeletonModifiers.isExpanded, "Skeleton Modifiers");
			if (skeletonModifiers.isExpanded)
			{
				if (!minimalMode)
				{
					EditorGUILayout.HelpBox("TIP: Setting up your DNA Converter's Skeleton Modifiers is much easier if you use the 'DNA Converter Bahaviour Customizer' scene as it can automatically populate the list of available bones with the ones in the generated Avatar's skeleton.", MessageType.Info);
				}

				skeletonModifiersInfoExpanded = EditorGUILayout.Foldout(skeletonModifiersInfoExpanded, "INFO");
				if (skeletonModifiersInfoExpanded)
					EditorGUILayout.HelpBox(_skelModsTip, MessageType.Info);

				//If dnaNames is null or empty show a warning 
				//UMA2.8+ OBSOLETE
				/*bool showDNANamesWarning = false;
				if (skeletonModifiers.serializedObject.FindProperty("dnaAsset").objectReferenceValue == null)
					showDNANamesWarning = true;
				else if ((skeletonModifiers.serializedObject.FindProperty("dnaAsset").objectReferenceValue as DynamicUMADnaAsset).Names.Length == 0)
					showDNANamesWarning = true;
				if (showDNANamesWarning)
					EditorGUILayout.HelpBox("You need to have your DNA Names set up above in order for the Skeleton Modifiers to make any modifications", MessageType.Warning);*/

				EditorGUI.indentLevel++;
				extraSkelAddDelOptsExpanded = EditorGUILayout.Foldout(extraSkelAddDelOptsExpanded, "Add/Delete Modifier Options");
				EditorGUI.indentLevel--;
				if (extraSkelAddDelOptsExpanded)
				{
					DrawLegacySkeletonModifiersAddDeleteTools(skeletonModifiers);
				}
				DrawLegacySkeletonModifiersAddNew(skeletonModifiers);
				EditorGUILayout.Space();
				DrawLegacySkeletonModifiersList(skeletonModifiers);
				DrawLegacySkeletonModifiersResetStartingValues(skeletonModifiers);
				EditorGUILayout.Space();
			}
			EditorGUI.indentLevel--;
			GUIHelper.EndVerticalPadded(3);

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawLegacySkeletonModifiersAddDeleteTools(SerializedProperty skeletonModifiers)
		{
			//make a drop area for importing skeletonModifiers from another DynamicDnaConverter
			var dropArea = GUILayoutUtility.GetRect(0.0f, 60.0f, GUILayout.ExpandWidth(true));
			dropArea.xMin = dropArea.xMin + (EditorGUI.indentLevel * 15);
			GUI.Box(dropArea, "Drag DynamicDNAConverterBahaviours here to import their Skeleton Modifiers");//cant click to pick unfortunately because this is a prefab
			var AddMethods = new GUIContent[dnaNamesAddOpts.Count];
			for (int i = 0; i < dnaNamesAddOpts.Count; i++)
				AddMethods[i] = new GUIContent(dnaNamesAddOpts[i]);
			Rect selectedAddMethodRect = dropArea;
			selectedAddMethodRect.yMin = dropArea.yMax - EditorGUIUtility.singleLineHeight - 5;
			selectedAddMethodRect.xMin = dropArea.xMin - ((EditorGUI.indentLevel * 10) - 10);
			selectedAddMethodRect.xMax = dropArea.xMax - ((EditorGUI.indentLevel * 10) + 10);
			selectedModifiersAddMethod = EditorGUI.Popup(selectedAddMethodRect, new GUIContent("On Import", "Choose whether to 'Add' the modifiers to the current list, or 'Replace' the modifiers with the imported list"), selectedModifiersAddMethod, AddMethods);

			ImportConverterDropArea(dropArea, selectedModifiersAddMethod, AddDNAConverterModifiers);

			//Clear all button
			GUILayout.BeginHorizontal();
			GUILayout.Space(EditorGUI.indentLevel * 15);
			EditorGUI.BeginDisabledGroup(skeletonModifiers.arraySize == 0);
			if (GUILayout.Button("Clear All Modifiers"))
			{
				if (EditorUtility.DisplayDialog("Really Clear All Modifiers?", "This will delete all the skeleton modifiers in the list and cannot be undone. Are you sure?", "Yes", "Cancel"))
				{
					skeletonModifiers.arraySize = 0;
					serializedObject.ApplyModifiedProperties();
				}
			}
			EditorGUI.EndDisabledGroup();
			GUILayout.EndHorizontal();
			EditorGUILayout.Space();
		}
#pragma warning disable 618
		private void DrawLegacySkeletonModifiersAddNew(SerializedProperty skeletonModifiers)
		{
			Rect addSkelButsR = EditorGUILayout.GetControlRect(false);
			var addSkelLabel = addSkelButsR;
			var addSkelBone = addSkelButsR;
			var addSkelProp = addSkelButsR;
			var addSkelAddBut = addSkelButsR;
			addSkelLabel.width = 100;
			addSkelAddBut.width = 70;
			addSkelBone.width = addSkelProp.width = (addSkelButsR.width - (addSkelLabel.width + (addSkelAddBut.width + 5))) / 2;
			addSkelBone.x = addSkelLabel.xMax;
			addSkelProp.x = addSkelBone.xMax;
			addSkelAddBut.x = addSkelProp.xMax + 5;
			EditorGUI.LabelField(addSkelLabel, new GUIContent("Add Modifier", "Add a modifier for the selected bone in the skeleton, that will modify its 'Position', 'Rotation' or 'Scale'"));
			EditorGUI.indentLevel--;
			List<string> thisBoneNames = new List<string>(0);
			thisBoneNames = new List<string>(bonesInSkeleton);
			thisBoneNames.Insert(0, "Choose Bone");

			EditorGUI.BeginChangeCheck();
			selectedAddHash = EditorGUI.Popup(addSkelBone, selectedAddHash, thisBoneNames.ToArray());
			string[] propertyArray = new string[] { "Position", "Rotation", "Scale" };
			selectedAddProp = EditorGUI.Popup(addSkelProp, selectedAddProp, propertyArray);
			if (EditorGUI.EndChangeCheck())
			{
				if (selectedAddHash > 0)
				{
					addSkelBoneName = thisBoneNames[selectedAddHash];
					addSkelBoneHash = UMAUtils.StringToHash(addSkelBoneName);
				}
				else
				{
					addSkelBoneName = "";
					addSkelBoneHash = 0;
					canAddSkel = false;
				}
			}
			if (addSkelBoneName != "" && addSkelBoneHash != 0)
			{
				canAddSkel = true;
				alreadyExistedSkel = false;
				//we need to check if there is already a modifier for that bone for the selected property
				for (int i = 0; i < skeletonModifiers.arraySize; i++)
				{
					var thisSkelMod = skeletonModifiers.GetArrayElementAtIndex(i);
					if (thisSkelMod.FindPropertyRelative("_property").enumValueIndex == selectedAddProp && thisSkelMod.FindPropertyRelative("_hash").intValue == addSkelBoneHash)
					{
						canAddSkel = false;
						alreadyExistedSkel = true;
					}
				}
			}
			if (canAddSkel == false)
			{
				EditorGUI.BeginDisabledGroup(true);
			}
			if (GUI.Button(addSkelAddBut, "Add It!"))
			{
				(target as DynamicDNAConverterBehaviour).skeletonModifiers.Insert(0, new SkeletonModifier(addSkelBoneName, addSkelBoneHash, (SkeletonModifier.SkeletonPropType)selectedAddProp));
				serializedObject.ApplyModifiedProperties();
				serializedObject.Update();
				addSkelBoneHash = 0;
				addSkelBoneName = "";
				selectedAddHash = 0;
				EditorGUIUtility.keyboardControl = 0;
			}
			if (canAddSkel == false)
			{
				EditorGUI.EndDisabledGroup();
			}
			if (alreadyExistedSkel == true)
			{
				EditorGUILayout.HelpBox("There was already a modifier for that bone with that property. You can serach the existing modifiers to find it.", MessageType.Warning);
			}
		}
#pragma warning restore 618

		private void DrawLegacySkeletonModifiersList(SerializedProperty skeletonModifiers)
		{
			//THE ACTUAL MODIFIER LIST
			EditorGUI.indentLevel++;
			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUILayout.LabelField("Skeleton Modifiers (" + skeletonModifiers.arraySize + ")", EditorStyles.helpBox);
			//Search Filters Controls- dont show if we dont have any modifiers
			if (skeletonModifiers.arraySize > 0)
			{
				Rect searchR = EditorGUILayout.GetControlRect();
				var searchL = searchR;
				var searchF = searchR;
				var searchTL = searchR;
				var searchTF = searchR;
				searchL.width = 130;
				searchF.width = (searchR.width / 3) * 2 - searchL.width;
				searchF.x = searchR.x + searchL.width;
				searchTL.width = 35;
				searchTL.x = searchF.xMax;
				searchTF.width = (searchR.width / 3) - searchTL.width + (EditorGUI.indentLevel * 15);
				searchTF.x = searchTL.xMax - (EditorGUI.indentLevel * 15);
				EditorGUI.LabelField(searchL, "Search Modifiers");
				EditorGUI.indentLevel--;
				skeletonModifiersFilter = EditorGUI.TextField(searchF, skeletonModifiersFilter);
				EditorGUI.LabelField(searchTL, "By");
				skeletonModifiersFilterType = EditorGUI.Popup(searchTF, skeletonModifiersFilterType, skeletonModifiersFilterTypeList);
				EditorGUI.indentLevel++;
			}

			EditorGUI.indentLevel++;
			for (int i = 0; i < skeletonModifiers.arraySize; i++)
			{
				var thisSkelEl = skeletonModifiers.GetArrayElementAtIndex(i);
				//Search Filters Method
				if (skeletonModifiersFilterTypeList[skeletonModifiersFilterType] != "DNA")
				{
					if (skeletonModifiersFilterType == 1 || skeletonModifiersFilterType == 2 || skeletonModifiersFilterType == 3)
					{
						string thisProperty = thisSkelEl.FindPropertyRelative("_property").enumNames[thisSkelEl.FindPropertyRelative("_property").enumValueIndex];
						if (skeletonModifiersFilterType == 1)//Position Modifiers
						{
							if (thisProperty.IndexOf("position", StringComparison.CurrentCultureIgnoreCase) == -1)
								continue;
						}
						else if (skeletonModifiersFilterType == 2)//Rotation Modifiers
						{
							if (thisProperty.IndexOf("rotation", StringComparison.CurrentCultureIgnoreCase) == -1)
								continue;
						}
						else if (skeletonModifiersFilterType == 3)//scale Modifiers
						{
							if (thisProperty.IndexOf("scale", StringComparison.CurrentCultureIgnoreCase) == -1)
								continue;
						}
					}
					else if (skeletonModifiersFilterType == 5)//Adjust Bones
					{
						if (thisSkelEl.displayName.IndexOf("adjust", StringComparison.CurrentCultureIgnoreCase) == -1)
							continue;
					}
					else if (skeletonModifiersFilterType == 6)//Non Adjust Bones
					{
						if (thisSkelEl.displayName.IndexOf("adjust", StringComparison.CurrentCultureIgnoreCase) > -1)
							continue;
					}
				}
				if (skeletonModifiersFilter.Length >= 3)
				{
					if (skeletonModifiersFilterTypeList[skeletonModifiersFilterType] != "DNA")
					{
						if (thisSkelEl.displayName.IndexOf(skeletonModifiersFilter, StringComparison.CurrentCultureIgnoreCase) == -1)
							continue;
					}
					else //Searches for Modifiers that use a given DNA Value- slow but super handy
					{
						string[] XYZ = new string[] { "X", "Y", "Z" };
						SerializedProperty mods;
						SerializedProperty thisMod;
						int modsi;
						bool _continue = true;
						foreach (string xyz in XYZ)
						{
							mods = thisSkelEl.FindPropertyRelative("_values" + xyz).FindPropertyRelative("_val").FindPropertyRelative("_modifiers");
							for (int mi = 0; mi < mods.arraySize; mi++)
							{
								thisMod = mods.GetArrayElementAtIndex(mi);
								modsi = thisMod.FindPropertyRelative("_modifier").enumValueIndex;
								if (modsi > 3)
								{
									if (thisMod.FindPropertyRelative("_DNATypeName").stringValue.IndexOf(skeletonModifiersFilter, StringComparison.CurrentCultureIgnoreCase) > -1)
										_continue = false;
								}
							}
						}
						if (_continue)
						{
							continue;
						}
					}
				}
				Rect currentRect = EditorGUILayout.GetControlRect(false, _skelModPropDrawer.GetPropertyHeight(thisSkelEl, GUIContent.none));
				//Delete button
				Rect sDelButR = currentRect;
				sDelButR.x = sDelButR.x + sDelButR.width - 100f;
				sDelButR.width = 100f;
				sDelButR.height = EditorGUIUtility.singleLineHeight;
				if (GUI.Button(sDelButR, "Delete"))
				{
					skeletonModifiers.DeleteArrayElementAtIndex(i);
					continue;
				}
				Rect thisSkelRect = new Rect(currentRect.xMin, currentRect.yMin, currentRect.width, _skelModPropDrawer.GetPropertyHeight(thisSkelEl, GUIContent.none));
				_skelModPropDrawer.OnGUI(thisSkelRect, thisSkelEl, new GUIContent(thisSkelEl.displayName));
			}
			GUIHelper.EndVerticalPadded(3);
			EditorGUI.indentLevel--;
		}

		private void DrawLegacySkeletonModifiersResetStartingValues(SerializedProperty skeletonModifiers)
		{
			//we want to discourage users from using the starting values to customise their models (if they have any modifiers set up
			if (skeletonModifiers.arraySize > 0)
			{
				//and make it easy to set the starting values back to the defaults
				GUILayout.BeginHorizontal();
				GUILayout.Space(EditorGUI.indentLevel * 15);
				if (GUILayout.Button("Reset All Starting Values to Default"))
				{
					for (int i = 0; i < skeletonModifiers.arraySize; i++)
					{
						var thisSkeModProp = skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_property").enumNames[skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_property").enumValueIndex];
						if (thisSkeModProp != "")
						{
							if (thisSkeModProp == "Position" || thisSkeModProp == "Rotation")
							{
								skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_valuesX").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 0f;
								skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_valuesY").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 0f;
								skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_valuesZ").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 0f;
							}
							if (thisSkeModProp == "Scale")
							{
								skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_valuesX").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 1f;
								skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_valuesY").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 1f;
								skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("_valuesZ").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 1f;
							}
						}
					}
					serializedObject.ApplyModifiedProperties();
				}
				GUILayout.EndHorizontal();
			}
		}

		public void DrawLegacyStartingPoseGUI()
		{
			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUI.indentLevel++;
			startingPoseExpanded = EditorGUILayout.Foldout(startingPoseExpanded, "Starting Pose");
			if (startingPoseExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUI.indentLevel++;
				startingPoseInfoExpanded = EditorGUILayout.Foldout(startingPoseInfoExpanded, "INFO");
				if (startingPoseInfoExpanded)
					EditorGUILayout.HelpBox("The 'Starting Pose' is the initial position/rotation/scale of all the bones in this Avatar's skeleton. Use this to completely transform the mesh of your character. You could (for example) transform standard UMA characters into a backwards compatible 'Short Squat Dwarf' or a 'Bobble- headded Toon'. Optionally, you can create an UMABonePose asset from an FBX model using the UMA > Pose Tools > Bone Pose Builder and add the resulting asset here. After you have added or created a UMABonePose asset, you can add and edit the position, rotation and scale settings for any bone in the active character's skeleton in the 'Bone Poses' section.'.", MessageType.Info);
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(serializedObject.FindProperty("_startingPose"), new GUIContent("Starting UMABonePose", "Define an asset that will set the starting bone poses of any Avatar using this converter"));
				if (EditorGUI.EndChangeCheck())
				{
					serializedObject.ApplyModifiedProperties();
					//If this gets set we need to back it up
					if(thisDDCC != null)
					thisDDCC.BackupConverter();
				}
				//Draw the poses array from the Asset if set or show controls to create a new asset.
				SerializedProperty bonePoseAsset = serializedObject.FindProperty("_startingPose");

				//If the asset isn't null and we are in playmode set the context.activeUMA to this umaData for live editing
				if (bonePoseAsset.objectReferenceValue != null && minimalMode)
				{
					//if there is an UMABonePose popup inspector open set the umaData as its sourceUMA
					if (UMA.PoseTools.UMABonePoseEditor.livePopupEditor != null)
					{
						UMA.PoseTools.UMABonePoseEditor.livePopupEditor.sourceUMA = umaData;
						UMA.PoseTools.UMABonePoseEditor.livePopupEditor.dynamicDNAConverterMode = true;
					}
				}

				if (bonePoseAsset.objectReferenceValue != null)
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("startingPoseWeight"));
				}
				else
				{
					if (thisDDCC != null)
					{
						var createPoseAssetR = EditorGUILayout.GetControlRect(false);
						var createPoseAssetRLabel = createPoseAssetR;
						var createPoseAssetRField = createPoseAssetR;
						var createPoseAssetRButton = createPoseAssetR;
						createPoseAssetRLabel.width = createPoseAssetRLabel.width / 3 + 7;
						createPoseAssetRButton.width = 70f;
						createPoseAssetRField.width = ((createPoseAssetRField.width / 3) * 2) - 82;
						createPoseAssetRField.x = createPoseAssetRLabel.xMax;
						createPoseAssetRButton.x = createPoseAssetRField.xMax + 5;
						EditorGUI.LabelField(createPoseAssetRLabel, new GUIContent("New BonePose Asset", "Create a new empty UMABonePose with the name of your choosing."));
						createBonePoseAssetName = EditorGUI.TextField(createPoseAssetRField, createBonePoseAssetName);
						if (GUI.Button(createPoseAssetRButton, "Create It"))//need to do the button enabled thing here
						{
							var newDnaAsset = thisDDCC.CreatePoseAsset("", createBonePoseAssetName);
							if (newDnaAsset != null)
							{
								//set this asset as the used asset
								bonePoseAsset.objectReferenceValue = newDnaAsset;
								serializedObject.ApplyModifiedProperties();
								createBonePoseAssetName = "";
							}
						}
					}
					else
					{
						EditorGUILayout.HelpBox("Edit a character that uses this converter in the 'DynamicDna Converter Behaviour Customizer' scene and you can create a StartingPoseAsset automatically here", MessageType.Info);
					}
				}
				EditorGUI.indentLevel--;
				GUIHelper.EndVerticalPadded(3);
			}
			EditorGUI.indentLevel--;
			GUIHelper.EndVerticalPadded(3);
			serializedObject.ApplyModifiedProperties();
		}

		private void ImportConverterDropArea(Rect dropArea, int addMethod, Action<DynamicDNAConverterBehaviour, int> callback)
		{
			Event evt = Event.current;
			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
				Event.current.Use();
				return;
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
							GameObject tempDnaGO = draggedObjects[i] as GameObject;
							DynamicDNAConverterBehaviour tempDnaAsset = tempDnaGO.GetComponent<DynamicDNAConverterBehaviour>();
							if (tempDnaAsset)
							{
								callback.DynamicInvoke(tempDnaAsset, addMethod);
								continue;
							}

							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (System.IO.Directory.Exists(path))
							{
								RecursiveScanFoldersForAssets(path, callback, addMethod);
							}
						}
					}
				}
				Event.current.Use();
				return;
			}
		}

		private void RecursiveScanFoldersForAssets(string path, Delegate callback, int addMethod)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.prefab");
			foreach (var assetFile in assetFiles)
			{
				var tempDnaGO = AssetDatabase.LoadAssetAtPath(assetFile, typeof(GameObject)) as GameObject;
				DynamicDNAConverterBehaviour tempDnaAsset = tempDnaGO.GetComponent<DynamicDNAConverterBehaviour>();
				if (tempDnaAsset)
				{
					callback.DynamicInvoke(tempDnaAsset, addMethod);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), callback, addMethod);
			}
		}
#pragma warning disable 618
		private void AddDNAConverterModifiers(DynamicDNAConverterBehaviour tempDNAAsset, int addMethod)
		{
			//Do we need to make sure the dna names are there as well? What is there is no dna asset?
			//now add the modifiers
			var currentModifiers = addMethod == 0 ? (target as DynamicDNAConverterBehaviour).skeletonModifiers : new List<SkeletonModifier>();
			for (int i = 0; i < tempDNAAsset.skeletonModifiers.Count; i++)
			{
				bool existed = false;
				for (int ci = 0; ci < currentModifiers.Count; ci++)
				{
					if ((currentModifiers[ci].hash == tempDNAAsset.skeletonModifiers[i].hash) && currentModifiers[ci].property == tempDNAAsset.skeletonModifiers[i].property)
					{
						existed = true;
						break;
					}
				}
				if (!existed)
				{
					currentModifiers.Add(new SkeletonModifier(tempDNAAsset.skeletonModifiers[i]));
				}
			}
			(target as DynamicDNAConverterBehaviour).skeletonModifiers = currentModifiers;
			serializedObject.Update();
		}
#pragma warning restore 618

	}
}
