using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
	[CustomEditor(typeof(DynamicDNAConverterBehaviour), true)]
	public class DynamicDNAConverterBehaviourEditor : Editor
	{

		[MenuItem("Assets/Create/UMA/DNA/Dynamic DNA Converter")]
		public static void CreateDynamicDNAConverterBehaviour()
		{
			CustomAssetUtility.CreatePrefab("DynamicDNAConverterBehaviour", typeof(DynamicDNAConverterBehaviour));
		}

		private SkeletonModifierPropertyDrawer _skelModPropDrawer = null;
		private List<string> hashNames = new List<string>();
		private List<int> hashes = new List<int>();
		private string newHashName = "";
		private int selectedAddHash = 0;

		private List<string> bonesInSkeleton = new List<string>();

		private string addSkelBoneName = "";
		private int addSkelBoneHash = 0;
		private int selectedAddProp = 0;
		bool canAddSkel = false;
		bool alreadyExistedSkel = false;
		private string boneHashFilter = "";
		private string skeletonModifiersFilter = "";
		private int skeletonModifiersFilterType = 0;
		private string[] skeletonModifiersFilterTypeList = new string[] { "Bone Name", "Position Modifiers", "Rotation Modifiers", "Scale Modifiers", "DNA", "Adjust Bones", "Non-Adjust Bones" };
		public bool enableSkelModValueEditing = false;
		//minimalMode is the mode that is used when a DynamicDnaConverterBehaviour is shown in a DynamicDnaConverterCustomizer rather than when its inspected directly
		public bool minimalMode = false;
		//
		//Optional component refrences
		public UMAData umaData = null;
		//DynamicDNAConverterCustomizer
		public DynamicDNAConverterCustomizer thisDDCC = null;
		//UMABonePose Editor
		private Editor thisUBP = null;
		public string createBonePoseAssetName = "";
		//DynamicUMADNAAsset Editor
		private Editor thisDUDA = null;
		public string createDnaAssetName = "";
		//
		[System.NonSerialized]
		public bool initialized = false;

		//Foldouts Expanded bools
		bool dnaAssetInfoExpanded = false;
		bool skeletonModifiersExpanded = true;
		bool skeletonModifiersInfoExpanded = false;
		bool extraBonesAddDelOptsExpanded = false;
		bool extraSkelAddDelOptsExpanded = false;
		bool startingPoseExpanded = false;
		bool startingPoseInfoExpanded = false;

		//for the DnaConverter picker
		//int DNAConverterPickerID = 0;//cant click to pick because its a prefab so we see all GameObjects in the selector, not just DynamicDNAConverterBahaviours
		List<string> dnaNamesAddOpts = new List<string> { "Add", "Replace" };
		int selectedBonesAddMethod = 0;
		int selectedModifiersAddMethod = 0;

		private void Init()
		{
			if (_skelModPropDrawer == null)
				_skelModPropDrawer = new SkeletonModifierPropertyDrawer();

			hashNames.Clear();
			hashes.Clear();
			SerializedProperty hashList = serializedObject.FindProperty("hashList");
			for (int i = 0; i < hashList.arraySize; i++)
			{
				hashNames.Add(hashList.GetArrayElementAtIndex(i).FindPropertyRelative("hashName").stringValue);
				hashes.Add(hashList.GetArrayElementAtIndex(i).FindPropertyRelative("hash").intValue);
			}
			if (minimalMode == false)
			{
				bonesInSkeleton = new List<string>(hashNames.ToArray());
			}
			else
			{
				bonesInSkeleton = new List<string>(umaData.skeleton.BoneNames);
			}
			bonesInSkeleton.Sort();
			UpdateDnaNames();
			initialized = true;
		}

		void UpdateHashNames()
		{
			hashNames.Clear();
			hashes.Clear();
			SerializedProperty hashList = serializedObject.FindProperty("hashList");
			for (int i = 0; i < hashList.arraySize; i++)
			{
				hashNames.Add(hashList.GetArrayElementAtIndex(i).FindPropertyRelative("hashName").stringValue);
				hashes.Add(hashList.GetArrayElementAtIndex(i).FindPropertyRelative("hash").intValue);
			}
			_skelModPropDrawer.UpdateHashNames(hashNames, hashes);
		}
		void UpdateDnaNames()
		{
			string[] dnaNames = null;
			SerializedProperty dnaAssetProp = serializedObject.FindProperty("dnaAsset");
			if (dnaAssetProp != null)
			{
				DynamicUMADnaAsset dnaAsset = dnaAssetProp.objectReferenceValue as DynamicUMADnaAsset;
				if (dnaAsset != null)
					_skelModPropDrawer.Init(hashNames, hashes, dnaAsset.Names);
				else
					_skelModPropDrawer.Init(hashNames, hashes, dnaNames);
			}
			else
			{
				_skelModPropDrawer.Init(hashNames, hashes, dnaNames);
			}
			if (minimalMode)
			_skelModPropDrawer.bonesInSkeleton = bonesInSkeleton;
		}

		//drop area for importing from another DynamicUMADnaConverterBehaviour
		private void ImportConverterDropArea(Rect dropArea, int addMethod, Action<DynamicDNAConverterBehaviour, int> callback )
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
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), callback,  addMethod);
			}
		}

		private void AddDNAConverterHashes(DynamicDNAConverterBehaviour tempDNAAsset, int addMethod)
		{
			bool addedHashes = false;
			var currentHashes = addMethod == 0 ? (target as DynamicDNAConverterBehaviour).hashList : new List<DynamicDNAConverterBehaviour.HashListItem>();
			for(int i = 0; i < tempDNAAsset.hashList.Count; i++)
			{
				bool existed = false;
				for (int ci = 0; ci < currentHashes.Count; ci++)
				{
					if(currentHashes[ci].hash == tempDNAAsset.hashList[i].hash)
					{
						existed = true;
						break;
					}
				}
				if (!existed)
				{
					var canAdd = !minimalMode ? true : (bonesInSkeleton.Contains(tempDNAAsset.hashList[i].hashName) ? true : false);
					if (canAdd)
					{
						addedHashes = true;
						currentHashes.Add(new DynamicDNAConverterBehaviour.HashListItem(tempDNAAsset.hashList[i].hashName, tempDNAAsset.hashList[i].hash));
					}
				}
			}
			(target as DynamicDNAConverterBehaviour).hashList = currentHashes;
			serializedObject.Update();
			if (addedHashes)
				UpdateHashNames();

		}

		private void AddDNAConverterModifiers(DynamicDNAConverterBehaviour tempDNAAsset, int addMethod)
		{
			//Do we need to make sure the dna names are there as well? What is there is no dna asset?
			//Make sure all the bone hashes are there
			AddDNAConverterHashes(tempDNAAsset, 0);
			//now add the modifiers
			var currentModifiers = addMethod == 0 ? (target as DynamicDNAConverterBehaviour).skeletonModifiers : new List<DynamicDNAConverterBehaviour.SkeletonModifier>();
			for(int i = 0; i < tempDNAAsset.skeletonModifiers.Count; i++)
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
					currentModifiers.Add(new DynamicDNAConverterBehaviour.SkeletonModifier(tempDNAAsset.skeletonModifiers[i]));
				}
			}
			(target as DynamicDNAConverterBehaviour).skeletonModifiers = currentModifiers;
			serializedObject.Update();
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			if (!initialized)
				this.Init();

			//Style for Tips
			var foldoutTipStyle = new GUIStyle(EditorStyles.foldout);
			foldoutTipStyle.fontStyle = FontStyle.Bold;
			//DISPLAY VALUE
			EditorGUILayout.PropertyField(serializedObject.FindProperty("DisplayValue"));
			//
			//=============DNA ASSET AND EDITOR============//
			SerializedProperty dnaAsset = serializedObject.FindProperty("dnaAsset");
			dnaAsset.isExpanded = EditorGUILayout.Foldout(dnaAsset.isExpanded, "Dynamic DNA", foldoutTipStyle);
			if (dnaAsset.isExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUI.indentLevel++;
				dnaAssetInfoExpanded = EditorGUILayout.Foldout(dnaAssetInfoExpanded, "INFO");
				if (dnaAssetInfoExpanded)
					EditorGUILayout.HelpBox("The DynmicDNAAsset is the DNA this converter will apply to the skeleton. The DNA consists of names and associated values. Often you display these names as 'sliders'. The values set by these sliders change an Avatar's body proportions by modifying its skeleton bones by the dna value, according to the 'DNA Converter Settings' you set in the 'DNA Converter Settings' section.", MessageType.Info);
				
				if(dnaAsset.objectReferenceValue == null)
				{
					//show a tip that people need to create or assign a dna asset
					EditorGUILayout.HelpBox("Create or assign a DNA Asset this converter will use", MessageType.Info);
				}
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(dnaAsset, new GUIContent("DNA Asset", "A DynamicUMADnaAsset contains a list of names that define the 'DNA' that will be used to modify the Avatars Skeleton. Often displayed in the UI as 'sliders'"));
				if (EditorGUI.EndChangeCheck())
				{
					UpdateDnaNames();
					serializedObject.ApplyModifiedProperties();
					serializedObject.Update();//?
					if (minimalMode)
					{
						//force the Avatar to update its dna and dnaconverter dictionaries
						umaData.umaRecipe.ClearDna();
						umaData.umaRecipe.ClearDNAConverters();
					}
				}
				//If there is no dna asset assigned show a button to make one
				if (dnaAsset.objectReferenceValue == null)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space(EditorGUI.indentLevel * 15);
					if (GUILayout.Button(new GUIContent("Create Dynamic DNA Asset")))
					{
						var suggestedPath = AssetDatabase.GetAssetPath(target);
						var suggestedName = target.name + "DNAAsset";
						var path = EditorUtility.SaveFilePanelInProject("Create a new Dynamic DNA Asset", suggestedName, "asset", "some message", suggestedPath);
						if (path != "")
						{
							var newDnaAsset = CustomAssetUtility.CreateAsset<DynamicUMADnaAsset>(path, false);
							if (newDnaAsset != null)
							{
								//set this asset as the used asset
								dnaAsset.objectReferenceValue = newDnaAsset;
								serializedObject.ApplyModifiedProperties();
								createDnaAssetName = "";
							}
							UpdateDnaNames();
							if (minimalMode)
							{
								//force the Avatar to update its dna and dnaconverter dictionaries
								umaData.umaRecipe.ClearDna();
								umaData.umaRecipe.ClearDNAConverters();
							}
						}
					}
					GUILayout.EndHorizontal();
				}
				//Otherwise show the DNA Assets Editor
				else
				{
					if (thisDUDA == null)
					{
						thisDUDA = Editor.CreateEditor((DynamicUMADnaAsset)dnaAsset.objectReferenceValue, typeof(UMA.CharacterSystem.Editors.DynamicUMADnaAssetEditor));
					}
					else if (thisDUDA.target != (DynamicUMADnaAsset)dnaAsset.objectReferenceValue)
					{
						thisDUDA = Editor.CreateEditor((DynamicUMADnaAsset)dnaAsset.objectReferenceValue, typeof(UMA.CharacterSystem.Editors.DynamicUMADnaAssetEditor));
					}
					EditorGUI.BeginChangeCheck();
					thisDUDA.OnInspectorGUI();
					if (EditorGUI.EndChangeCheck())
					{
						UpdateDnaNames();
					}
				}
				EditorGUI.indentLevel--;
				GUIHelper.EndVerticalPadded(3);
			}
			serializedObject.ApplyModifiedProperties();
			//===========END DNA ASSET AND EDITOR============//
			//
			EditorGUILayout.Space();
			//
			//=============CONVERTER VALUES AND EDITOR=============//
			SerializedProperty hashList = serializedObject.FindProperty("hashList");
			SerializedProperty skeletonModifiers = serializedObject.FindProperty("skeletonModifiers");
			string converterTips = "";
			if (minimalMode)
			{
				converterTips = "Skeleton Modifiers control how the values of the DNA you set above are applied to the skeleton. So for example 'Upper Weight' affects the scale of the Spine, breast, belly and shoulder bones in different ways. The best way to edit these modifiers is to set the DNA slider you want to adjust in the game view, to either its minimum or maximum position. Then add or edit a skeleton modifier in the list below to use that value to modify the skeleton. The 'Value Modifiers' part of a Skeleton Modifier, takes the incoming value, modifies it by the settings and applies it to the bone. The Min and Max values are what that result will be 'clamped' to. Avoid changing the starting 'Value' as changes to this will persist even if the dna is not itself applied (if you want to do this use the starting pose below instead).";
			}
			else
			{
				converterTips = "Skeleton Modifiers control how the values of the DNA you set above are applied to the skeleton.  So for example 'Upper Weight' affects the scale of the Spine, breast, belly and shoulder bones in different ways. Add bones you wish to make available to modify in the 'BoneHashes' section. Then add or edit a skeleton modifier in the list below to use a given dna value to modify the skeleton. The 'Value Modifiers' part of a Skeleton Modifier, takes the incoming value, modifies it by the settings and applies it to the bone. The Min and Max values are what that result will be 'clamped' to. Avoid changing the starting 'Value' as changes to this will persist even if the dna is not itself applied (if you want to do this use the starting pose below instead).";
			}
			skeletonModifiersExpanded = EditorGUILayout.Foldout(skeletonModifiersExpanded, "DNA Converter Settings", foldoutTipStyle);
			if (skeletonModifiersExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				if (!minimalMode)
				{
					EditorGUILayout.HelpBox("TIP: Setting up your DNA Converter's Skeleton Modifiers is much easier if you use the 'DNA Converter Bahaviour Customizer' scene as it can automatically populate the list of available bones with the ones in the generated Avatar's skeleton.", MessageType.Info);
				}
				EditorGUI.indentLevel++;
				skeletonModifiersInfoExpanded = EditorGUILayout.Foldout(skeletonModifiersInfoExpanded, "INFO");
				if (skeletonModifiersInfoExpanded)
					EditorGUILayout.HelpBox(converterTips, MessageType.Info);
				if (!minimalMode)//in minimal mode we dont need to show these because we will have a skeleton that we can get the bonehash list from
				{
					int hashListCount = hashList.arraySize;
					hashList.isExpanded = EditorGUILayout.Foldout(hashList.isExpanded, new GUIContent("Bone Hashes", "These are the bones you have identified that the converter will work on. If you use the Dynamic DNA Customizer Scene these can be set automatically when you add Modifiers") );
					if (hashList.isExpanded)
					{
						EditorGUI.indentLevel++;
						extraBonesAddDelOptsExpanded = EditorGUILayout.Foldout(extraBonesAddDelOptsExpanded, "Add/Delete Bones Options");
						EditorGUI.indentLevel--;
						if (extraBonesAddDelOptsExpanded)
						{
							//make a drop area for importing bone hashes from another DynamicDnaConverter
							var dropArea = GUILayoutUtility.GetRect(0.0f, 60.0f, GUILayout.ExpandWidth(true));
							dropArea.xMin = dropArea.xMin + (EditorGUI.indentLevel * 15);
							GUI.Box(dropArea, "Drag DynamicDNAConverterBahaviours here to import their names");//cant click to pick unfortunately because this is a prefab
							var AddMethods = new GUIContent[dnaNamesAddOpts.Count];
							for (int i = 0; i < dnaNamesAddOpts.Count; i++)
								AddMethods[i] = new GUIContent(dnaNamesAddOpts[i]);
							Rect selectedAddMethodRect = dropArea;
							selectedAddMethodRect.yMin = dropArea.yMax - EditorGUIUtility.singleLineHeight - 5;
							selectedAddMethodRect.xMin = dropArea.xMin - ((EditorGUI.indentLevel * 10) - 10);
							selectedAddMethodRect.xMax = dropArea.xMax - ((EditorGUI.indentLevel * 10) + 10);
							selectedBonesAddMethod = EditorGUI.Popup(selectedAddMethodRect, new GUIContent("On Import", "Choose whether to 'Add' the bones to the current list, or 'Replace' them with the imported  ones"), selectedBonesAddMethod, AddMethods);

							ImportConverterDropArea(dropArea, selectedBonesAddMethod, AddDNAConverterHashes);

							EditorGUILayout.Space();

							//Clear all and Add Defaults Buttons
							Rect clearAndDefaultsRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
							clearAndDefaultsRect.xMin = clearAndDefaultsRect.xMin + (EditorGUI.indentLevel * 15);
							var defaultsButRect = clearAndDefaultsRect;
							var clearButRect = clearAndDefaultsRect;
							defaultsButRect.width = clearAndDefaultsRect.width / 2;
							clearButRect.xMin = defaultsButRect.xMax;
							clearButRect.width = clearAndDefaultsRect.width / 2;
							if (GUI.Button(defaultsButRect, new GUIContent("Add Default Hashes", "Adds the default bone hashes as used by UMA Human Male DNA")))
							{
								AddDefaultBoneHashes();
								//once we add these we need to update the hashnames so the dropdown has the right stuff in
								hashList.serializedObject.ApplyModifiedProperties();
								serializedObject.Update();
								UpdateHashNames();
							}
							EditorGUI.BeginDisabledGroup(hashList.arraySize == 0);
							if (GUI.Button(clearButRect, new GUIContent("Clear All Bone Hashes", "Clears the current Bone Hashes. Cannot be undone.")))
							{
								bool proceed = true;
								//if there are any skeleton modifiers that are using these bone hashes warn the user that clearing this list will break them
								if (skeletonModifiers.arraySize > 0)
									proceed = EditorUtility.DisplayDialog("Really Clear All Bone Hashes?", "This will delete all the bone hashes in the list and make any skeleton modifiers you have added not work. Are you sure?", "Yes", "Cancel");
								if (proceed)
								{
									(target as DynamicDNAConverterBehaviour).hashList = new List<DynamicDNAConverterBehaviour.HashListItem>();
									hashList.serializedObject.ApplyModifiedProperties();
									serializedObject.Update();
									UpdateHashNames();
								}
							}
							EditorGUI.EndDisabledGroup();
							EditorGUILayout.Space();
						}
						//create an add field for adding new hashes
						EditorGUILayout.BeginHorizontal();
						//var buttonDisabled = newHashName == "";
						bool canAdd = true;
						bool notFoundInSkeleton = false;
						bool didAdd = false;
						EditorGUI.BeginChangeCheck();
						newHashName = EditorGUILayout.TextField(newHashName);
						if (EditorGUI.EndChangeCheck())
						{
							if (newHashName != "" && canAdd)
							{
								//buttonDisabled = false;
							}
						}
						if (newHashName != "")
						{
							for (int ni = 0; ni < hashList.arraySize; ni++)
							{
								if (hashList.GetArrayElementAtIndex(ni).FindPropertyRelative("hashName").stringValue == newHashName)
								{
									canAdd = false;
									//buttonDisabled = true;
								}
							}
							//if we have a skeleton available we can also check that the bone the user is trying to add exists
							if (umaData)
								if (umaData.skeleton != null)
								{
									if (umaData.skeleton.HasBone(UMAUtils.StringToHash(newHashName)) == false)
									{
										canAdd = false;
										//buttonDisabled = true;
										notFoundInSkeleton = true;
									}
								}
						}
						//Dont disable because it stops looking like what you want to do, just make it do nothing if nothing is entered
						/*if (buttonDisabled)
						{
							EditorGUI.BeginDisabledGroup(true);
						}*/

						if (GUILayout.Button("Add Bone Hash"))
						{
							if (canAdd)
							{
								var newHash = UMAUtils.StringToHash(newHashName);
								(target as DynamicDNAConverterBehaviour).hashList.Insert(0, new DynamicDNAConverterBehaviour.HashListItem(newHashName, newHash));
								hashList.serializedObject.ApplyModifiedProperties();
								serializedObject.Update();
								didAdd = true;
								UpdateHashNames();
								//reset the bloody text field!
								EditorGUIUtility.keyboardControl = 0;
							}
						}
						/*if (buttonDisabled)
						{
							EditorGUI.EndDisabledGroup();
						}*/
						EditorGUILayout.EndHorizontal();
						if (canAdd == false)
						{
							if (notFoundInSkeleton == true)
							{
								EditorGUILayout.HelpBox("That name was not found in the skeleton. (Standard Bone names start with a capital letter in CamelCase)", MessageType.Warning);
							}
							else
							{
								EditorGUILayout.HelpBox("That name is already in use.", MessageType.Warning);
							}
						}
						if (didAdd)
						{
							newHashName = "";
						}

						//THE ACTUAL BONE HASH LIST
						GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
						EditorGUILayout.LabelField("Bone Hash List (" + hashListCount + ")", EditorStyles.helpBox);
						//Search Bone Hashes Controls
						if (hashList.arraySize > 0)
							boneHashFilter = EditorGUILayout.TextField("Search Bones", boneHashFilter);
						EditorGUI.indentLevel++;
						if (hashList.arraySize > 0)
						{
							for (int i = 0; i < hashList.arraySize; i++)
							{
								var thisHashEl = hashList.GetArrayElementAtIndex(i);
								//Search Bone Hashes Method
								if (boneHashFilter.Length >= 3)
								{
									if (thisHashEl.displayName.IndexOf(boneHashFilter, StringComparison.CurrentCultureIgnoreCase) == -1)
										continue;
								}
								EditorGUILayout.BeginHorizontal();
								thisHashEl.isExpanded = EditorGUILayout.Foldout(thisHashEl.isExpanded, thisHashEl.displayName);
								//DeleteButton
								Rect hDelButR = EditorGUILayout.GetControlRect(false);
								hDelButR.x = hDelButR.x + hDelButR.width - 100f;
								hDelButR.width = 100f;
								if (GUI.Button(hDelButR, "Delete"))
								{
									hashList.DeleteArrayElementAtIndex(i);
									continue;
								}
								EditorGUILayout.EndHorizontal();
								if (thisHashEl.isExpanded)
								{
									EditorGUI.indentLevel++;
									string origName = thisHashEl.FindPropertyRelative("hashName").stringValue;
									string newName = origName;
									EditorGUI.BeginChangeCheck();
									newName = EditorGUILayout.TextField("Hash Name", origName);
									if (EditorGUI.EndChangeCheck())
									{
										if (newName != origName && newName != "")
										{
											thisHashEl.FindPropertyRelative("hashName").stringValue = newName;
											int newHash = UMAUtils.StringToHash(newName);
											thisHashEl.FindPropertyRelative("hash").intValue = newHash;
											serializedObject.ApplyModifiedProperties();
										}
									}
									EditorGUI.BeginDisabledGroup(true);
									EditorGUILayout.IntField("Hash", thisHashEl.FindPropertyRelative("hash").intValue);
									EditorGUI.EndDisabledGroup();
									EditorGUI.indentLevel--;
								}
							}
							hashList.serializedObject.ApplyModifiedProperties();
							EditorGUILayout.Space();
						}	
						EditorGUI.indentLevel--;
						GUIHelper.EndVerticalPadded(3);
					}
				}
				//SKELETON MODIFIERS SECTION
				skeletonModifiers.isExpanded = EditorGUILayout.Foldout(skeletonModifiers.isExpanded, "Skeleton Modifiers");
				if (skeletonModifiers.isExpanded)
				{
					//If dnaNames is null or empty show a warning
					bool showDNANamesWarning = false;
					if (serializedObject.FindProperty("dnaAsset").objectReferenceValue == null)
						showDNANamesWarning = true;
					else if((serializedObject.FindProperty("dnaAsset").objectReferenceValue as DynamicUMADnaAsset).Names.Length == 0)
						showDNANamesWarning = true;
					if(showDNANamesWarning)
						EditorGUILayout.HelpBox("You need to have your DNA Names set up above in order for the Skeleton Modifiers to make any modifications", MessageType.Warning);
					//If bone hashes is empty show a warning
					if(hashList.arraySize == 0 && !minimalMode)
						EditorGUILayout.HelpBox("You need to add the bones you want the Skeleton Modifiers to be able to modify to the 'Bone Hashes' section above.", MessageType.Warning);

					EditorGUI.indentLevel++;
					extraSkelAddDelOptsExpanded = EditorGUILayout.Foldout(extraSkelAddDelOptsExpanded, "Add/Delete Modifier Options");
					EditorGUI.indentLevel--;
					if (extraSkelAddDelOptsExpanded)
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
								(target as DynamicDNAConverterBehaviour).skeletonModifiers = new List<DynamicDNAConverterBehaviour.SkeletonModifier>();
								skeletonModifiers.serializedObject.ApplyModifiedProperties();
								serializedObject.Update();
							}
						}
						EditorGUI.EndDisabledGroup();
						GUILayout.EndHorizontal();
						EditorGUILayout.Space();
					}


					//Add new Skeleton Modifier UI
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
					//string[] boneNames = new string[0];
					/*if(minimalMode == false)
					{
						bonesInSkeleton = new List<string>( hashNames.ToArray());
					}
					else
					{
						bonesInSkeleton = new List<string>(umaData.skeleton.BoneNames);
					}
					bonesInSkeleton.Sort();*/
					//Array.Sort(boneNames);
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
							if (thisSkelMod.FindPropertyRelative("property").enumValueIndex == selectedAddProp && thisSkelMod.FindPropertyRelative("hash").intValue == addSkelBoneHash)
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
						if (minimalMode)
						{
							if (!hashes.Contains(addSkelBoneHash))
							{
								(target as DynamicDNAConverterBehaviour).hashList.Insert(0, new DynamicDNAConverterBehaviour.HashListItem(addSkelBoneName, addSkelBoneHash));
								hashList.serializedObject.ApplyModifiedProperties();
								serializedObject.Update();
								UpdateHashNames();
							}
						}
						(target as DynamicDNAConverterBehaviour).skeletonModifiers.Insert(0, new DynamicDNAConverterBehaviour.SkeletonModifier(addSkelBoneName, addSkelBoneHash, (DynamicDNAConverterBehaviour.SkeletonModifier.SkeletonPropType)selectedAddProp));
						skeletonModifiers.serializedObject.ApplyModifiedProperties();
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
					EditorGUI.indentLevel++;
					//Search Filters Controls- dont show if we dont have any modifiers
					EditorGUILayout.Space();

					//THE ACTUAL MODIFIER LIST
					GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
					EditorGUILayout.LabelField("Skeleton Modifiers (" + skeletonModifiers.arraySize + ")", EditorStyles.helpBox);
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
								string thisProperty = thisSkelEl.FindPropertyRelative("property").enumNames[thisSkelEl.FindPropertyRelative("property").enumValueIndex];
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
									mods = thisSkelEl.FindPropertyRelative("values" + xyz).FindPropertyRelative("val").FindPropertyRelative("modifiers");
									for (int mi = 0; mi < mods.arraySize; mi++)
									{
										thisMod = mods.GetArrayElementAtIndex(mi);
										modsi = thisMod.FindPropertyRelative("modifier").enumValueIndex;
										if (modsi > 3)
										{
											if (thisMod.FindPropertyRelative("DNATypeName").stringValue.IndexOf(skeletonModifiersFilter, StringComparison.CurrentCultureIgnoreCase) > -1)
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
					//we want to discourage users from using the starting values to customise their models (if they have any modifiers set up
					if (skeletonModifiers.arraySize > 0)
					{
						_skelModPropDrawer.enableSkelModValueEditing = enableSkelModValueEditing = EditorGUILayout.ToggleLeft("Enable editing of starting Value (not reccommended)", enableSkelModValueEditing);
						//and make it easy to set the starting values back to the defaults
						GUILayout.BeginHorizontal();
						GUILayout.Space(EditorGUI.indentLevel * 15);
						if (GUILayout.Button("Reset All Starting Values to Default"))
						{
							for (int i = 0; i < skeletonModifiers.arraySize; i++)
							{
								var thisSkeModProp = skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("property").enumNames[skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("property").enumValueIndex];
								if (thisSkeModProp != "")
								{
									if (thisSkeModProp == "Position" || thisSkeModProp == "Rotation")
									{
										skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("valuesX").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = 0f;
										skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("valuesY").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = 0f;
										skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("valuesZ").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = 0f;
									}
									if (thisSkeModProp == "Scale")
									{
										skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("valuesX").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = 1f;
										skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("valuesY").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = 1f;
										skeletonModifiers.GetArrayElementAtIndex(i).FindPropertyRelative("valuesZ").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = 1f;
									}
								}
							}
							skeletonModifiers.serializedObject.ApplyModifiedProperties();
						}
						GUILayout.EndHorizontal();
					}
					EditorGUILayout.Space();
				}
				EditorGUILayout.Space();
				serializedObject.FindProperty("overallModifiersEnabled").boolValue = EditorGUILayout.ToggleLeft("Enable Overall Modifiers", serializedObject.FindProperty("overallModifiersEnabled").boolValue);
				SerializedProperty overallModifiersEnabledProp = serializedObject.FindProperty("overallModifiersEnabled");
				bool overallModifiersEnabled = overallModifiersEnabledProp.boolValue;
				if (overallModifiersEnabled)
				{
					EditorGUI.BeginChangeCheck();
					EditorGUILayout.PropertyField(serializedObject.FindProperty("overallScale"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("tightenBounds"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("boundsAdjust"));
					//EditorGUILayout.PropertyField(serializedObject.FindProperty("heightModifiers"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("radiusAdjust"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("massModifiers"));
					if (EditorGUI.EndChangeCheck())
					{
						serializedObject.ApplyModifiedProperties();
					}
				}
				EditorGUI.indentLevel--;
				GUIHelper.EndVerticalPadded(3);
			}
			serializedObject.ApplyModifiedProperties();
			//===========END CONVERTER VALUES AND EDITOR============//
			//
			EditorGUILayout.Space();
			//
			//===========BONEPOSE ASSET AND EDITOR===========//
			startingPoseExpanded = EditorGUILayout.Foldout(startingPoseExpanded, "Starting Pose", foldoutTipStyle);
			if (startingPoseExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUI.indentLevel++;
				startingPoseInfoExpanded = EditorGUILayout.Foldout(startingPoseInfoExpanded, "INFO");
				if (startingPoseInfoExpanded)
					EditorGUILayout.HelpBox("The 'Starting Pose'is the initial position/rotation/scale of all the bones in this Avatar's skeleton. Use this to completely transform the mesh of your character. You could (for example) transform standard UMA characters into a backwards compatible 'Short Squat Dwarf' or a 'Bobble- headded Toon'. Optionally, you can create an UMABonePose asset from an FBX model using the UMA > Pose Tools > Bone Pose Builder and add the resulting asset here. After you have added or created a UMABonePose asset, you can add and edit the position, rotation and scale settings for any bone in the active character's skeleton in the 'Bone Poses' section.'.", MessageType.Info);
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(serializedObject.FindProperty("startingPose"), new GUIContent("Starting UMABonePose", "Define an asset that will set the starting bone poses of any Avatar using this converter"));
				if (EditorGUI.EndChangeCheck())
				{
					serializedObject.ApplyModifiedProperties();
					//If this gets set we need to back it up
					thisDDCC.BackupConverter();
				}
				//Draw the poses array from the Asset if set or show controls to create a new asset.
				SerializedProperty bonePoseAsset = serializedObject.FindProperty("startingPose");
				if (bonePoseAsset.objectReferenceValue != null)
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("startingPoseWeight"));
					if (thisUBP == null)
					{
						thisUBP = Editor.CreateEditor((UMA.PoseTools.UMABonePose)bonePoseAsset.objectReferenceValue, typeof(UMA.PoseTools.UMABonePoseEditor));
						((UMA.PoseTools.UMABonePoseEditor)thisUBP).dynamicDNAConverterMode = true;
						if (umaData != null)
						{
							((UMA.PoseTools.UMABonePoseEditor)thisUBP).context = new UMA.PoseTools.UMABonePoseEditorContext();
							((UMA.PoseTools.UMABonePoseEditor)thisUBP).context.activeUMA = umaData;
						}
					}
					else if (thisUBP.target != (UMA.PoseTools.UMABonePose)bonePoseAsset.objectReferenceValue)
					{
						thisUBP = Editor.CreateEditor((UMA.PoseTools.UMABonePose)bonePoseAsset.objectReferenceValue, typeof(UMA.PoseTools.UMABonePoseEditor));
						((UMA.PoseTools.UMABonePoseEditor)thisUBP).dynamicDNAConverterMode = true;
						if (umaData != null)
						{
							((UMA.PoseTools.UMABonePoseEditor)thisUBP).context = new UMA.PoseTools.UMABonePoseEditorContext();
							((UMA.PoseTools.UMABonePoseEditor)thisUBP).context.activeUMA = umaData;
						}
					}
					EditorGUI.BeginChangeCheck();
					thisUBP.OnInspectorGUI();
					if (EditorGUI.EndChangeCheck())
					{
						//Currently we dont need to do anything here as the change is picked up by DynamicDNAConverterCustomizer an this triggers an UMA update
						//this may change though if we have a method in future for modifying the TPose
					}
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
				if (minimalMode && umaData.skeleton != null && bonePoseAsset.objectReferenceValue == null)
				{
					EditorGUILayout.Space();
					EditorGUILayout.LabelField("Create Poses from Current DNA state");
					EditorGUILayout.HelpBox("Create bone poses from Avatar's current dna modified state. Applies the pose and sets DNA values back to 0. Smaller margin of error equals greater accuracy but more poses to apply on DNA Update.", MessageType.Info);
					if (thisDDCC != null)
					{
						//[Range(0.000005f, 0.0005f)]
						EditorGUI.BeginChangeCheck();
						var thisAccuracy = EditorGUILayout.Slider(new GUIContent("Margin Of Error","The smaller the margin of error, the more accurate the Pose will be, but it will also have more bonePoses to apply when DNA is updated"), thisDDCC.bonePoseAccuracy * 1000, 0.5f, 0.005f);
						if (EditorGUI.EndChangeCheck())
						{
							thisDDCC.bonePoseAccuracy = thisAccuracy / 1000;
							GUI.changed = false;
						}
					}
					GUILayout.BeginHorizontal();
					GUILayout.Space(EditorGUI.indentLevel * 20);
					if (GUILayout.Button(/*createFromDnaButR, */"Create Poses"))
					{
						if (thisDDCC != null)
						{
							if (thisDDCC.CreateBonePosesFromCurrentDna(createBonePoseAssetName))
							{
								serializedObject.Update();
							}
						}
					}
					GUILayout.EndHorizontal();

				}
				EditorGUI.indentLevel--;
				GUIHelper.EndVerticalPadded(3);
			}
			serializedObject.ApplyModifiedProperties();
			//=============END BONEPOSE ASSET AND EDITOR============//
			//
			EditorGUILayout.Space();

		}
		protected void AddDefaultBoneHashes()
		{
			List<string> defaultHashNames = new List<string>
			{
			"HeadAdjust",
			"NeckAdjust",
			"LeftOuterBreast",
			"RightOuterBreast",
			"LeftEye",
			"RightEye",
			"LeftEyeAdjust",
			"RightEyeAdjust",
			"Spine1Adjust",
			"SpineAdjust",
			"LowerBackBelly",
			"LowerBackAdjust",
			"LeftTrapezius",
			"RightTrapezius",
			"LeftArmAdjust",
			"RightArmAdjust",
			"LeftForeArmAdjust",
			"RightForeArmAdjust",
			"LeftForeArmTwistAdjust",
			"RightForeArmTwistAdjust",
			"LeftShoulderAdjust",
			"RightShoulderAdjust",
			"LeftUpLegAdjust",
			"RightUpLegAdjust",
			"LeftLegAdjust",
			"RightLegAdjust",
			"LeftGluteus",
			"RightGluteus",
			"LeftEarAdjust",
			"RightEarAdjust",
			"NoseBaseAdjust",
			"NoseMiddleAdjust",
			"LeftNoseAdjust",
			"RightNoseAdjust",
			"UpperLipsAdjust",
			"MandibleAdjust",
			"LeftLowMaxilarAdjust",
			"RightLowMaxilarAdjust",
			"LeftCheekAdjust",
			"RightCheekAdjust",
			"LeftLowCheekAdjust",
			"RightLowCheekAdjust",
			"NoseTopAdjust",
			"LeftEyebrowLowAdjust",
			"RightEyebrowLowAdjust",
			"LeftEyebrowMiddleAdjust",
			"RightEyebrowMiddleAdjust",
			"LeftEyebrowUpAdjust",
			"RightEyebrowUpAdjust",
			"LipsSuperiorAdjust",
			"LipsInferiorAdjust",
			"LeftLipsSuperiorMiddleAdjust",
			"RightLipsSuperiorMiddleAdjust",
			"LeftLipsInferiorAdjust",
			"RightLipsInferiorAdjust",
			"LeftLipsAdjust",
			"RightLipsAdjust",
			"Global",
			"Position",
			"LowerBack",
			"Head",
			"LeftArm",
			"RightArm",
			"LeftForeArm",
			"RightForeArm",
			"LeftHand",
			"RightHand",
			"LeftFoot",
			"RightFoot",
			"LeftUpLeg",
			"RightUpLeg",
			"LeftShoulder",
			"RightShoulder",
			"Mandible"
			};
			List<DynamicDNAConverterBehaviour.HashListItem> currentHashes = new List<DynamicDNAConverterBehaviour.HashListItem>((target as DynamicDNAConverterBehaviour).hashList);
			for (int i = 0; i < defaultHashNames.Count; i++)
			{
				bool existed = false;
				for(int ci = 0; ci < currentHashes.Count; ci++)
				{
					if(currentHashes[ci].hashName == defaultHashNames[i])
					{
						existed = true;
						break;
					}
				}
				if (!existed)
				{
					currentHashes.Add(new DynamicDNAConverterBehaviour.HashListItem(defaultHashNames[i]));
				}
			}
			(target as DynamicDNAConverterBehaviour).hashList = currentHashes;
		}
	}
}
