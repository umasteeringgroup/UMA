using UnityEngine;
using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UMA;

[CustomEditor(typeof(DynamicDNAConverterBehaviour), true)]
public class DynamicDNAConverterBehaviourEditor : Editor
{
	private DynamicDNAConverterBehaviour thisDDCB;
	private SkeletonModifierPropertyDrawer _skelModPropDrawer = null;
	private List<string> hashNames = new List<string>();
	private List<int> hashes = new List<int>();
	private string newHashName = "";
	private int selectedAddHash = 0;
	private string addSkelBoneName = "";
	private int addSkelBoneHash = 0;
	private int selectedAddProp = 0;
	bool canAddSkel = false;
	bool alreadyExistedSkel = false;
	private Dictionary<string, Vector3> skelAddDefaults = new Dictionary<string, Vector3>
	{
		{"Position", new Vector3(0f,-0.1f, 0.1f) },
		{"Rotation", new Vector3(0f,-360f, 360f) },
		{"Scale",  new Vector3(1f,0f, 5f) }
	};
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

	private void Init()
	{
		thisDDCB = target as DynamicDNAConverterBehaviour;
		if (_skelModPropDrawer == null)
			_skelModPropDrawer = new SkeletonModifierPropertyDrawer();

		if (minimalMode == false)
		{
			bool doUpdate = thisDDCB.SetCurrentAssetPath();
			if (doUpdate)
			{
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssets();
			}
		}
		//
		hashNames.Clear();
		hashes.Clear();
		SerializedProperty hashList = serializedObject.FindProperty("hashList");
		for (int i = 0; i < hashList.arraySize; i++)
		{
			hashNames.Add(hashList.GetArrayElementAtIndex(i).FindPropertyRelative("hashName").stringValue);
			hashes.Add(hashList.GetArrayElementAtIndex(i).FindPropertyRelative("hash").intValue);
		}
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

	}
	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		if (!initialized)
			this.Init();
		EditorGUI.BeginDisabledGroup(true);
		//REMOVE FOR RELEASE- this currently outputs the hidden fields used to check the DNATypeHash is being set correctly- the methods in the converter make it so the typehash only changes if the asset is duplicated
		if (!minimalMode)
			Editor.DrawPropertiesExcluding(serializedObject, new string[] { "dnaAsset", "startingPose", "startingPoseWeight", "hashList", "skeletonModifiers", "overallModifiersEnabled", "overallScale", "heightModifiers", "radiusModifier", "massModifiers" });
		EditorGUI.EndDisabledGroup();
		//Style for Tips
		var foldoutTipStyle = new GUIStyle(EditorStyles.foldout);
		foldoutTipStyle.fontStyle = FontStyle.Bold;
		//=============================================//
		//===========BONEPOSE ASSET AND EDITOR===========//
		serializedObject.FindProperty("startingPoseWeight").isExpanded = EditorGUILayout.Foldout(serializedObject.FindProperty("startingPoseWeight").isExpanded, "Starting Pose", foldoutTipStyle);
		if (serializedObject.FindProperty("startingPoseWeight").isExpanded)
		{
			EditorGUILayout.HelpBox("The 'Starting Pose'is the initial position/rotation/scale of all the bones in this Avatar's skeleton. Use this to completely transform the mesh of your character. You could (for example) transform standard UMA characters into a backwards compatible 'Short Squat Dwarf' or a 'Bobble- headded Toon'. Optionally, you can create an UMABonePose asset from an FBX model using the UMA > Pose Tools > Bone Pose Builder and add the resulting asset here. After you have added or created a UMABonePose asset, you can add and edit the position, rotation and scale settings for any bone in the active character's skeleton in the 'Bone Poses' section. You can also create bone poses automatically from the Avatar's current dna modified state using the 'Create poses from Current DNA state button'.", MessageType.Info);
		}
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(serializedObject.FindProperty("startingPose"), new GUIContent("Starting UMABonePose", "Define an asset that will set the starting bone poses of any Avatar using this converter"));
		if (EditorGUI.EndChangeCheck())
		{
			serializedObject.ApplyModifiedProperties();
		}
		//Draw the poses array from the Asset if set or show controls to create a new asset.
		SerializedProperty bonePoseAsset = serializedObject.FindProperty("startingPose");
		EditorGUI.indentLevel++;
		if (bonePoseAsset.objectReferenceValue != null)
		{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("startingPoseWeight"));
			if (thisUBP == null)
			{
				thisUBP = Editor.CreateEditor((UMA.PoseTools.UMABonePose)bonePoseAsset.objectReferenceValue, typeof(UMA.PoseTools.UMABonePoseEditor));
				((UMA.PoseTools.UMABonePoseEditor)thisUBP).Init();
				((UMA.PoseTools.UMABonePoseEditor)thisUBP).minimalMode = true;
				if (umaData != null)
					((UMA.PoseTools.UMABonePoseEditor)thisUBP).umaData = umaData;
			}
			else if (thisUBP.target != (UMA.PoseTools.UMABonePose)bonePoseAsset.objectReferenceValue)
			{
				thisUBP = Editor.CreateEditor((UMA.PoseTools.UMABonePose)bonePoseAsset.objectReferenceValue, typeof(UMA.PoseTools.UMABonePoseEditor));
				((UMA.PoseTools.UMABonePoseEditor)thisUBP).Init();
				((UMA.PoseTools.UMABonePoseEditor)thisUBP).minimalMode = true;
				if (umaData != null)
					((UMA.PoseTools.UMABonePoseEditor)thisUBP).umaData = umaData;
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
				EditorGUI.LabelField(createPoseAssetRLabel, new GUIContent("Create BonePose Asset", "Create a new empty UMABonePose with the name of your choosing."));
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
		if (minimalMode && umaData.skeleton != null)
		{
			var createFromDnaButR = EditorGUILayout.GetControlRect(false);
			if (GUI.Button(createFromDnaButR, "Create poses from Current DNA state"))
			{
				if (thisDDCC != null)
				{
					if (thisDDCC.CreateBonePosesFromCurrentDna(createBonePoseAssetName))
					{
						serializedObject.Update();
					}
				}
			}

		}
		EditorGUI.indentLevel--;
		serializedObject.ApplyModifiedProperties();
		//=============END BONEPOSE ASSET AND EDITOR============//
		//
		EditorGUILayout.Space();
		//
		//=============DNA ASSET AND EDITOR============//
		serializedObject.FindProperty("dnaAsset").isExpanded = EditorGUILayout.Foldout(serializedObject.FindProperty("dnaAsset").isExpanded, "Dynamic DNA", foldoutTipStyle);
		if (serializedObject.FindProperty("dnaAsset").isExpanded)
		{
			EditorGUILayout.HelpBox("The DynmicDNAAsset specifies what dna sliders will be shown for this Avatar. How the values from these sliders affect the UMA's skelton is defined in the 'DNA Converter Settings' section below. You can create, assign and edit a DynamicDNAAsset here and edit the list of names which will show as sliders using the 'DNA Slider Names' section below. This gives you the power to add any extra DNA sliders you want, like a 'fingerLength' slider or an 'Eye Symetry' slider.", MessageType.Info);
		}
		EditorGUILayout.PropertyField(serializedObject.FindProperty("dnaAsset"), new GUIContent("Dynamic UMA DNA Asset", "A DynamicUMADnaAsset contains a list of names that define the dna sliders that an Avatar has."));
		serializedObject.ApplyModifiedProperties();
		SerializedProperty dnaAsset = serializedObject.FindProperty("dnaAsset");
		EditorGUI.indentLevel++;
		if (dnaAsset.objectReferenceValue != null)
		{
			if (thisDUDA == null)
			{
				thisDUDA = Editor.CreateEditor((DynamicUMADnaAsset)dnaAsset.objectReferenceValue, typeof(UMAEditor.DynamicUMADnaAssetEditor));
			}
			else if (thisDUDA.target != (DynamicUMADnaAsset)dnaAsset.objectReferenceValue)
			{
				thisDUDA = Editor.CreateEditor((DynamicUMADnaAsset)dnaAsset.objectReferenceValue, typeof(UMAEditor.DynamicUMADnaAssetEditor));
			}
			EditorGUI.BeginChangeCheck();
			thisDUDA.OnInspectorGUI();
			if (EditorGUI.EndChangeCheck())
			{
				UpdateDnaNames();
			}
		}
		else
		{
			if (thisDDCC != null)
			{
				var createDnaAssetR = EditorGUILayout.GetControlRect(false);
				var createDnaAssetRLabel = createDnaAssetR;
				var createDnaAssetRField = createDnaAssetR;
				var createDnaAssetRButton = createDnaAssetR;
				createDnaAssetRLabel.width = createDnaAssetRLabel.width / 3 + 7;
				createDnaAssetRButton.width = 70f;
				createDnaAssetRField.width = ((createDnaAssetRField.width / 3) * 2) - 82;
				createDnaAssetRField.x = createDnaAssetRLabel.xMax;
				createDnaAssetRButton.x = createDnaAssetRField.xMax + 5;
				EditorGUI.LabelField(createDnaAssetRLabel, new GUIContent("Create DNA Asset", "Create a new DynamicUMADnaAsset with the name of your choosing and default UMADnaHumanoid sliders."));
				createDnaAssetName = EditorGUI.TextField(createDnaAssetRField, createDnaAssetName);
				if (GUI.Button(createDnaAssetRButton, "Create It"))//need to do the button enabled thing here
				{
					var newDnaAsset = thisDDCC.CreateDNAAsset("", createDnaAssetName);
					if (newDnaAsset != null)
					{
						//set this asset as the used asset
						dnaAsset.objectReferenceValue = newDnaAsset;
						serializedObject.ApplyModifiedProperties();
						createDnaAssetName = "";
					}
					UpdateDnaNames();
				}
			}
			else
			{
				EditorGUILayout.HelpBox("Edit a character that uses in the 'DynamicDna Converter Behaviour Customizer' scene and you can create a DynamicDNAAsset automatically here", MessageType.Info);
			}
		}
		EditorGUI.indentLevel--;
		serializedObject.ApplyModifiedProperties();
		//===========END DNA ASSET AND EDITOR============//
		//
		EditorGUILayout.Space();
		//
		//=============CONVERTER VALUES AND EDITOR=============//
		SerializedProperty hashList = serializedObject.FindProperty("hashList");
		SerializedProperty skeletonModifiers = serializedObject.FindProperty("skeletonModifiers");
		EditorGUI.indentLevel++;
		string converterTips = "";
		if (minimalMode)
		{
			converterTips = "Skeleton Modifiers control how the values of any DNA Sliders are applied to the skeleton. So for example 'Upper Weight' affects the scale of the Spine, breast, belly and shoulder bones in different ways. The best way to edit these modifiers is to set the DNA slider you want to adjust in the game view, to either its minimum or maximum position. Then add or edit the skeleton modifiers in the list below that use that value to modify the skeleton by the Min and Max values on the bone as a whole and how the incoming is multiplied. Avoid changing the starting 'Value' as changes to this will persist even if the dna is not itself applied (if you want to do this edit the starting pose above) instead.";
		}
		else
		{
			converterTips = "In this section you can control how the values of any DNA Sliders are applied to the skeleton. So for example 'Upper Weight' affects the scale of the Spine, breast, belly and shoulder bones in different ways. Add bones you wish to make available to modify in the 'BoneHashes' section. Then add or edit the skeleton modifiers in the 'Skeleton Modifiers' list to adjust how the bones are affected by that dna setting. Avoid changing the 'Value' as changes to this will persist even if the dna is not itself applied (if you want to do this edit the starting pose above) instead edit what the the Min and Max values an what any added dna is multiplied by.";
		}
		EditorGUI.indentLevel--;
		serializedObject.FindProperty("heightModifiers").isExpanded = EditorGUILayout.Foldout(serializedObject.FindProperty("heightModifiers").isExpanded, "DNA Converter Settings", foldoutTipStyle);
		if (serializedObject.FindProperty("heightModifiers").isExpanded)
		{
			EditorGUILayout.HelpBox(converterTips, MessageType.Info);
		}
		EditorGUI.indentLevel++;
		if (!minimalMode)//in minimal mode we dont need to show these because we will have a skeleton that we can get the bonehash list from
		{
			int hashListCount = hashList.arraySize;
			hashList.isExpanded = EditorGUILayout.Foldout(hashList.isExpanded, "Bone Hash List (" + hashListCount + ")");
			if (hashList.isExpanded)
			{
				EditorGUI.indentLevel++;
				//Search Bone Hashes Controls
				boneHashFilter = EditorGUILayout.TextField("Search Bones", boneHashFilter);
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
				//create an add field for adding new hashes
				EditorGUILayout.BeginHorizontal();
				var buttonDisabled = newHashName == "";
				bool canAdd = true;
				bool notFoundInSkeleton = false;
				bool didAdd = false;
				EditorGUI.BeginChangeCheck();
				newHashName = EditorGUILayout.TextField(newHashName);
				if (EditorGUI.EndChangeCheck())
				{
					if (newHashName != "" && canAdd)
					{
						buttonDisabled = false;
					}
				}
				if (newHashName != "")
				{
					for (int ni = 0; ni < hashList.arraySize; ni++)
					{
						if (hashList.GetArrayElementAtIndex(ni).FindPropertyRelative("hashName").stringValue == newHashName)
						{
							canAdd = false;
							buttonDisabled = true;
						}
					}
					//if we have a skeleton available we can also check that the bone the user is trying to add exists
					if (umaData.skeleton != null)
					{
						if (umaData.skeleton.HasBone(UMAUtils.StringToHash(newHashName)) == false)
						{
							canAdd = false;
							buttonDisabled = true;
							notFoundInSkeleton = true;
						}
					}
				}
				if (buttonDisabled)
				{
					EditorGUI.BeginDisabledGroup(true);
				}

				if (GUILayout.Button("Add Bone Hash"))
				{
					if (canAdd)
					{
						var newHash = UMAUtils.StringToHash(newHashName);
						var numhashes = hashList.arraySize;
						hashList.InsertArrayElementAtIndex(numhashes);
						hashList.serializedObject.ApplyModifiedProperties();
						hashList.GetArrayElementAtIndex(numhashes).FindPropertyRelative("hashName").stringValue = newHashName;
						hashList.GetArrayElementAtIndex(numhashes).FindPropertyRelative("hash").intValue = newHash;
						hashList.serializedObject.ApplyModifiedProperties();
						didAdd = true;
						UpdateHashNames();
					}
				}
				if (buttonDisabled)
				{
					EditorGUI.EndDisabledGroup();
				}
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
				EditorGUI.indentLevel--;
			}
		}
		int skeletonModifiersCount = skeletonModifiers.arraySize;
		skeletonModifiers.isExpanded = EditorGUILayout.Foldout(skeletonModifiers.isExpanded, "Skeleton Modifiers (" + skeletonModifiersCount + ")");
		if (skeletonModifiers.isExpanded)
		{
			EditorGUI.indentLevel++;
			//Search Filters Controls
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
			searchTF.width = (searchR.width / 3) - searchTL.width - 5;
			searchTF.x = searchTL.xMax;
			EditorGUI.LabelField(searchL, "Search Modifiers");
			EditorGUI.indentLevel--;
			skeletonModifiersFilter = EditorGUI.TextField(searchF, skeletonModifiersFilter);
			EditorGUI.LabelField(searchTL, "By");
			skeletonModifiersFilterType = EditorGUI.Popup(searchTF, skeletonModifiersFilterType, skeletonModifiersFilterTypeList);
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
			//we want to discourage users from using the starting values to customise their models
			_skelModPropDrawer.enableSkelModValueEditing = enableSkelModValueEditing = EditorGUILayout.ToggleLeft("Enable editing of starting Value (not reccommended)", enableSkelModValueEditing);
			//and make it easy to set the starting values back to the defaults
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
			//Add a new Skeleton Modifier 
			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.LabelField("Add Modifier");
			EditorGUI.EndDisabledGroup();
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
			EditorGUI.LabelField(addSkelLabel, "Bone Name");
			EditorGUI.indentLevel--;
			List<string> thisBoneNames = new List<string>(0);
			EditorGUI.BeginChangeCheck();
			if (minimalMode == false)
			{
				selectedAddHash = EditorGUI.Popup(addSkelBone, selectedAddHash, hashNames.ToArray());
			}
			else
			{
				var boneNames = umaData.skeleton.BoneNames;
				Array.Sort(boneNames);
				thisBoneNames = new List<string>(boneNames);
				thisBoneNames.Insert(0, "ChooseBone");
				selectedAddHash = EditorGUI.Popup(addSkelBone, selectedAddHash, thisBoneNames.ToArray());
			}
			string[] propertyArray = new string[] { "Position", "Rotation", "Scale" };
			selectedAddProp = EditorGUI.Popup(addSkelProp, selectedAddProp, propertyArray);
			if (EditorGUI.EndChangeCheck())
			{
				if (minimalMode == false)
				{
					//we will have an int that relates to a hashName and hash value
					addSkelBoneName = hashNames[selectedAddHash];
					addSkelBoneHash = hashes[selectedAddHash];
				}
				else
				{
					//otherwise we will have selected a bone from the bone names popup
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
						var numhashes = hashList.arraySize;
						hashList.InsertArrayElementAtIndex(numhashes);
						hashList.serializedObject.ApplyModifiedProperties();
						hashList.GetArrayElementAtIndex(numhashes).FindPropertyRelative("hashName").stringValue = addSkelBoneName;
						hashList.GetArrayElementAtIndex(numhashes).FindPropertyRelative("hash").intValue = addSkelBoneHash;
						hashList.serializedObject.ApplyModifiedProperties();
						UpdateHashNames();
					}
				}
				var numSkelMods = skeletonModifiers.arraySize;
				skeletonModifiers.InsertArrayElementAtIndex(numSkelMods);
				var thisSkelMod = skeletonModifiers.GetArrayElementAtIndex(numSkelMods);
				thisSkelMod.FindPropertyRelative("property").enumValueIndex = selectedAddProp;
				thisSkelMod.FindPropertyRelative("hashName").stringValue = addSkelBoneName;
				thisSkelMod.FindPropertyRelative("hash").intValue = addSkelBoneHash;
				thisSkelMod.FindPropertyRelative("valuesX").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].x;
				thisSkelMod.FindPropertyRelative("valuesX").FindPropertyRelative("val").FindPropertyRelative("modifiers").ClearArray();
				thisSkelMod.FindPropertyRelative("valuesX").FindPropertyRelative("min").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].y;
				thisSkelMod.FindPropertyRelative("valuesX").FindPropertyRelative("max").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].z;
				//
				thisSkelMod.FindPropertyRelative("valuesY").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].x;
				thisSkelMod.FindPropertyRelative("valuesY").FindPropertyRelative("val").FindPropertyRelative("modifiers").ClearArray();
				thisSkelMod.FindPropertyRelative("valuesY").FindPropertyRelative("min").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].y;
				thisSkelMod.FindPropertyRelative("valuesY").FindPropertyRelative("max").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].z;
				//
				thisSkelMod.FindPropertyRelative("valuesZ").FindPropertyRelative("val").FindPropertyRelative("value").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].x;
				thisSkelMod.FindPropertyRelative("valuesZ").FindPropertyRelative("val").FindPropertyRelative("modifiers").ClearArray();
				thisSkelMod.FindPropertyRelative("valuesZ").FindPropertyRelative("min").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].y;
				thisSkelMod.FindPropertyRelative("valuesZ").FindPropertyRelative("max").floatValue = skelAddDefaults[propertyArray[selectedAddProp]].z;
				skeletonModifiers.serializedObject.ApplyModifiedProperties();
				addSkelBoneHash = 0;
				addSkelBoneName = "";
			}
			if (canAddSkel == false)
			{
				EditorGUI.EndDisabledGroup();
			}
			if (alreadyExistedSkel == true)
			{
				EditorGUILayout.HelpBox("There was already a modifier for that bone with that property. You can filter the existing modifiers at the top of the list to find it.", MessageType.Warning);
			}
			EditorGUILayout.Space();
		}
		EditorGUILayout.Space();
		serializedObject.FindProperty("overallModifiersEnabled").boolValue = EditorGUILayout.ToggleLeft("Enable Overall Modifiers", serializedObject.FindProperty("overallModifiersEnabled").boolValue);
		SerializedProperty overallModifiersEnabledProp = serializedObject.FindProperty("overallModifiersEnabled");
		bool overallModifiersEnabled = overallModifiersEnabledProp.boolValue;
		if (overallModifiersEnabled)
		{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("overallScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("heightModifiers"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("radiusModifier"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("massModifiers"));
		}
		serializedObject.ApplyModifiedProperties();

	}
}
