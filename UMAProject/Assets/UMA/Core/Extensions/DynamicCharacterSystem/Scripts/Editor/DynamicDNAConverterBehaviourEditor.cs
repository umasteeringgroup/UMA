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

		DynamicDNAConverterBehaviour _target;

		//Set by the customizer in play mode
		public UMAData umaData = null;
		//Set by the customizer in play mode
		public DynamicDNAConverterCustomizer thisDDCC = null;
		
		//Set by the customizer in play mode
		//DECIDE ON THE FREAKING NAME FOR THIS
		//public bool playMode = false;
		//minimalMode is the mode that is used when a DynamicDnaConverterBehaviour is shown in a DynamicDnaConverterCustomizer rather than when its inspected directly
		public bool minimalMode = false;

		private LegacyDynamicDNAConverterGUIDrawer _legacyDrawer = null;

		//moved into legacyDrawer- may still be useful here
		private List<string> bonesInSkeleton = new List<string>();

		//Other Editors
		//DynamicUMADNAAsset Editor
		private Editor thisDUDA = null;
		public string createDnaAssetName = "";
		//the DynamicDNAConverterAssetInspector
		private Editor DDCCAEditor = null;

		//Foldouts Expanded bools
		bool dnaAssetInfoExpanded = false;
		bool upgradeInfoExpanded = false;

		string upgradeInfo1 = "DynamicDNAConverters now give you the ability to multiple kinds of converters in the same behaviour! This means the same dna can control your SkeletonModifers, Blendshapes, BonePoses etc";
		string upgradeInfo2 = "The system also comes with a simple API so you can add your own plugins to the system, allowing you to make dna make any changes you can imagine.";
		string upgradeInfo3 = "Clicking the 'Backup & Upgrade' button below will make a backup of this behaviour and then transfer all its settings over to the new system.";

		//post upgrade info? Explain where starting poses are now? Or show a Wiki?

		GUIStyle foldoutTipStyle;

		//Referenced by the customizer in play mode
		[System.NonSerialized]
		public bool initialized = false;

		private void Init()
		{
			if (minimalMode)
			{
				bonesInSkeleton = new List<string>(umaData.skeleton.BoneNames);
			}
			bonesInSkeleton.Sort();
			UpdateNames();

			//Style for Tips
			foldoutTipStyle = new GUIStyle(EditorStyles.foldout);
			foldoutTipStyle.fontStyle = FontStyle.Bold;

			_target = target as DynamicDNAConverterBehaviour;

			initialized = true;
		}

		void UpdateNames()
		{
			if (_legacyDrawer != null)
			{
				_legacyDrawer.UpdateNames();
			}
			else
			{
				var converterControllerProp = serializedObject.FindProperty("_converterController");
				if (converterControllerProp.objectReferenceValue == null)
				{
					if (_legacyDrawer == null)
					{
						_legacyDrawer = new LegacyDynamicDNAConverterGUIDrawer();
						_legacyDrawer.Init(target, serializedObject, umaData, thisDDCC, bonesInSkeleton, minimalMode);
					}
				}
			}
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			if (!initialized)
				this.Init();

			//DISPLAY VALUE
			EditorGUILayout.Space();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("DisplayValue"));
			EditorGUILayout.Space();

			DrawDNAAssetGUI();

			EditorGUILayout.Space();

			DrawDNAConvertersGUI();

			EditorGUILayout.Space();

			var converterControllerProp = serializedObject.FindProperty("_converterController");
			if(converterControllerProp.objectReferenceValue == null)
			{
				if (_legacyDrawer == null)
				{
					_legacyDrawer = new LegacyDynamicDNAConverterGUIDrawer();
					_legacyDrawer.Init(target, serializedObject, umaData, thisDDCC, bonesInSkeleton, minimalMode);
				}
				_legacyDrawer.DrawLegacyStartingPoseGUI();
				EditorGUILayout.Space();
			}

			DrawOverallModifiersGUI();

			EditorGUILayout.Space();
			serializedObject.ApplyModifiedProperties();

		}
		
		private void DrawDNAAssetGUI()
		{
			SerializedProperty dnaAsset = serializedObject.FindProperty("dnaAsset");
			dnaAsset.isExpanded = EditorGUILayout.Foldout(dnaAsset.isExpanded, "Dynamic DNA Asset", foldoutTipStyle);
			if (dnaAsset.isExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUI.indentLevel++;
				dnaAssetInfoExpanded = EditorGUILayout.Foldout(dnaAssetInfoExpanded, "INFO");
				if (dnaAssetInfoExpanded)
					EditorGUILayout.HelpBox("The DynmicDNAAsset is the DNA this converter will apply to the skeleton. The DNA consists of names and associated values. Often you display these names as 'sliders'. The values set by these sliders change an Avatar's body proportions by modifying its skeleton bones by the dna value, according to the 'DNA Converter Settings' you set in the 'DNA Converter Settings' section.", MessageType.Info);

				if (dnaAsset.objectReferenceValue == null)
				{
					//show a tip that people need to create or assign a dna asset
					EditorGUILayout.HelpBox("Create or assign a DNA Asset this converter will use", MessageType.Info);
				}
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(dnaAsset, new GUIContent("DNA Asset", "A DynamicUMADnaAsset contains a list of names that define the 'DNA' that will be used to modify the Avatars Skeleton. Often displayed in the UI as 'sliders'"));
				if (EditorGUI.EndChangeCheck())
				{
					UpdateNames();
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
							UpdateNames();
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
						UpdateNames();
					}
				}
				EditorGUI.indentLevel--;
				GUIHelper.EndVerticalPadded(3);
			}
			serializedObject.ApplyModifiedProperties();
		}

		private void DrawDNAConvertersGUI()
		{
			var converterControllerProp = serializedObject.FindProperty("_converterController");
			converterControllerProp.isExpanded = EditorGUILayout.Foldout(converterControllerProp.isExpanded, "DNA Converter Settings", foldoutTipStyle);
			if (converterControllerProp.isExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(converterControllerProp);
				if (EditorGUI.EndChangeCheck())
				{
					//Make sure the converterController has this target as its converterBehaviour value
					if(converterControllerProp.objectReferenceValue != null)
					{
						((DynamicDNAConverterAsset)converterControllerProp.objectReferenceValue).converterBehaviour = target as DynamicDNAConverterBehaviour;
						//MakeDirty and save? 
						//I think ScriptableObjects just change anyway when you set values direct
						//TODO CONFIRM
					}
				}

				if (converterControllerProp.objectReferenceValue != null)
				{
					if (DDCCAEditor == null)
						DDCCAEditor = Editor.CreateEditor((DynamicDNAConverterAsset)converterControllerProp.objectReferenceValue, typeof(DynamicDNAConverterAssetInspector));
					else if (DDCCAEditor.target != (DynamicDNAConverterAsset)converterControllerProp.objectReferenceValue)
						DDCCAEditor = Editor.CreateEditor((DynamicDNAConverterAsset)converterControllerProp.objectReferenceValue, typeof(DynamicDNAConverterAssetInspector));

					DDCCAEditor.OnInspectorGUI();
				}
				else
				{
					DrawUpgradeTools();
					if(_legacyDrawer == null)
					{
						_legacyDrawer = new LegacyDynamicDNAConverterGUIDrawer();
						_legacyDrawer.Init(target, serializedObject, umaData, thisDDCC, bonesInSkeleton, minimalMode);
					}
					_legacyDrawer.DrawLegacySkeletonModifiersGUI();
				}
				GUIHelper.EndVerticalPadded(3);
			}
		}

		private void DrawUpgradeTools()
		{
			//We only need to draw this if the skeletonModifiers and startingPose arrays are not empty

			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUILayout.LabelField("Upgrade Available!", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Please click the 'Backup & Upgrade' button to upgrade this ConverterBehaviour", MessageType.Info);
			upgradeInfoExpanded = EditorGUILayout.Foldout(upgradeInfoExpanded, "more");
			if (upgradeInfoExpanded)
			{
				EditorGUILayout.HelpBox(upgradeInfo1, MessageType.None);
				EditorGUILayout.HelpBox(upgradeInfo2, MessageType.None);
				EditorGUILayout.HelpBox(upgradeInfo3, MessageType.None);
			}
			GUILayout.BeginHorizontal();
			GUILayout.Space(10);
			if (GUILayout.Button("Backup & Upgrade"))
			{
				DoBackupAndUpgrade();
			}
			GUILayout.Space(10);
			GUILayout.EndHorizontal();
			EditorGUILayout.Space();
			GUIHelper.EndVerticalPadded(3);
		}

		private void DoBackupAndUpgrade()
		{
			if (_target.BackupAndUpgrade())
			{
				//show wiki for the new world?
			}
		}

		public void DrawOverallModifiersGUI()
		{
			/*var updateCharProp = serializedObject.FindProperty("overallModifiersEnabled");
			updateCharProp.isExpanded = EditorGUILayout.Foldout(updateCharProp.isExpanded, "Overall Modifiers", foldoutTipStyle);
			if (updateCharProp.isExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUI.indentLevel++;
				var overallScaleBoneHashProp = serializedObject.FindProperty("overallScaleBoneHash");
				EditorGUILayout.PropertyField(serializedObject.FindProperty("overallScale"));
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(serializedObject.FindProperty("overallScaleBone"));
				if (EditorGUI.EndChangeCheck())
				{
					overallScaleBoneHashProp.intValue = UMAUtils.StringToHash(serializedObject.FindProperty("overallScaleBone").stringValue);
				}

				EditorGUILayout.Space();
				var updateCharLabel = new GUIContent("Update CharacterHeight/Radius/Mass", "Allow this converter to update the CharacterHeight/Radius/Mass? Usually only your primary converter will make any changes here.");
				updateCharProp.boolValue = EditorGUILayout.ToggleLeft(updateCharLabel, updateCharProp.boolValue);
				if (updateCharProp.boolValue)
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("_headRatio"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("_heightDebugToolsEnabled"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("radiusAdjust"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("massModifiers"));

					EditorGUILayout.Space();
					EditorGUILayout.PropertyField(serializedObject.FindProperty("_updateBounds"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("tightenBounds"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("_adjustBounds"));
					if (serializedObject.FindProperty("_adjustBounds").boolValue == true)
						EditorGUILayout.PropertyField(serializedObject.FindProperty("boundsAdjust"));
				}
				EditorGUILayout.Space();

				EditorGUI.indentLevel--;
				GUIHelper.EndVerticalPadded(3);
			}*/
			var newOverallModifiersProp = serializedObject.FindProperty("_overallModifiers");
			newOverallModifiersProp.isExpanded = EditorGUILayout.Foldout(newOverallModifiersProp.isExpanded, "Overall Modifiers", foldoutTipStyle);
			if (newOverallModifiersProp.isExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUILayout.PropertyField(newOverallModifiersProp, new GUIContent("New " + newOverallModifiersProp.displayName));
				GUIHelper.EndVerticalPadded(3);
			}
		}
	}
}
