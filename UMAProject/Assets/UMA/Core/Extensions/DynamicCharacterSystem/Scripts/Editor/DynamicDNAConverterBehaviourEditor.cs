using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
	//UMA 2.8 FixDNAPrefabs: This wont need to show a new editor any more because this will just become a legacy asset
	//But we do need to update the 'Backup and Upgrade' method so it creates the new controller and assigns it everywhere the old behaviour was being used
	[CustomEditor(typeof(DynamicDNAConverterBehaviour), true)]
	public class DynamicDNAConverterBehaviourEditor : Editor
	{

		[MenuItem("Assets/Create/UMA/DNA/Legacy/Dynamic DNA Converter Behaviour")]
		public static void CreateDynamicDNAConverterBehaviour()
		{
			CustomAssetUtility.CreatePrefab("DynamicDNAConverterBehaviour", typeof(DynamicDNAConverterBehaviour));
		}

		DynamicDNAConverterBehaviour _target;
		
		SerializedProperty converterControllerProp;

		//Set by the customizer in play mode
		[System.NonSerialized]
		public UMAData umaData = null;
		//Set by the customizer in play mode
		[System.NonSerialized]
		public DynamicDNAConverterCustomizer thisDDCC = null;
		

		private LegacyDynamicDNAConverterGUIDrawer _legacyDrawer = null;

		//moved into legacyDrawer- may still be useful here
		private List<string> bonesInSkeleton = new List<string>();


		//Upgrade fields and  bools
		private bool _upgradeExpanded = false;
		bool upgradeInfoExpanded = false;

		string upgradeInfo1 = "DNAConverterBehaviours can now be upgraded to the new DNAConverterController asset. This new controller gives you the ability to use multiple kinds of converters! This means the same dna can control your SkeletonModifers, Blendshapes, BonePoses etc";
		string upgradeInfo2 = "Its also not a prefab (no more conflicts with Unity 2018.3+ new prefab system) and the system also comes with a simple API so you can add your own DNA Converter Plugins to the system, allowing you to make dna make any changes you can imagine.";
		string upgradeInfo3 = "Clicking the 'Upgrade' button below will convert this prefab into the new controller asset (and store the old version in a 'Legacy folder'), transfer all its settings over to the new system and replace its usage in all Races and Slots that used it.";


		//UMA 2.8+ FixDNAPrefabs if this converterBahaviour has been updated we want to still draw all the data in a 'Legacy' section
		bool drawAsLegacy = false;
		bool legacySettingsExpanded = false;

		GUIStyle foldoutTipStyle;

		//Referenced by the customizer in play mode
		[System.NonSerialized]
		public bool initialized = false;

#if UNITY_2018_3_OR_NEWER
		[System.NonSerialized]
		private bool _nagPerformed = false;
#endif

#pragma warning disable 618
		private void Init()
		{
			if (!initialized)
			{
				if (umaData != null)
				{
					bonesInSkeleton = new List<string>(umaData.skeleton.BoneNames);
				}
				bonesInSkeleton.Sort();
				UpdateNames();

				//Style for Tips
				foldoutTipStyle = new GUIStyle(EditorStyles.foldout);
				foldoutTipStyle.fontStyle = FontStyle.Bold;

				_target = target as DynamicDNAConverterBehaviour;

				converterControllerProp = serializedObject.FindProperty("_converterController");

				if (converterControllerProp.objectReferenceValue == null)
				{
					drawAsLegacy = false;
				}
				else
				{
					drawAsLegacy = true;
				}

				initialized = true;
			}
		}
#pragma warning restore 618

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
						_legacyDrawer.Init(_target, serializedObject, umaData, thisDDCC, bonesInSkeleton);
					}
				}
			}
		}

		public override void OnInspectorGUI()
		{

			serializedObject.Update();

			this.Init();

			EditorGUILayout.Space();

			if (!drawAsLegacy)
				DrawUpgradeTools();
			else
			{
				EditorGUILayout.HelpBox("This ConverterBehaviour should no longer be used. If it is it will use the settings defined in the 'Converter Controller' below (click the field to highlight it in the project).", MessageType.Warning);
				EditorGUI.BeginDisabledGroup(true);
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(converterControllerProp, new GUIContent("Converter Controller", "The Converter Controller Asset defines the DNA Converters (SkeletonModifiers, BonePoses etc) that will make changes to the Avatar based on the values for the DNA names defined above"));
				if (EditorGUI.EndChangeCheck())
				{
					//UMA2.8+ FixDNAPrefabs DynamicDNAController has no 'converterBehaviour' field now
					//Make sure the converterController has this target as its converterBehaviour value
					/*if (converterControllerProp.objectReferenceValue != null)
					{
						((DynamicDNAConverterController)converterControllerProp.objectReferenceValue).converterBehaviour = target as DynamicDNAConverterBehaviour;
					}*/
					serializedObject.ApplyModifiedProperties();
					serializedObject.Update();
					initialized = false;
					Init();
				}
				EditorGUI.EndDisabledGroup();
				EditorGUILayout.Space();
				DrawUpdateTools();

			}

			if (drawAsLegacy)
			{
				EditorGUILayout.Space();
				legacySettingsExpanded = EditorGUILayout.Foldout(legacySettingsExpanded, "Legacy Settings", foldoutTipStyle);
			}

			if (legacySettingsExpanded || !drawAsLegacy)
			{
				if (drawAsLegacy)
				{
					EditorGUI.indentLevel++;
					var legacyMessage = "These settings are no longer used but are preserved for your convenience.";
					EditorGUILayout.HelpBox(legacyMessage, MessageType.Warning);
					EditorGUILayout.HelpBox("Click the 'Revert to Legacy Settings' below if you need to revert for any reason.", MessageType.Info);
					GUILayout.BeginHorizontal();
					GUILayout.Space(10);
					if(GUILayout.Button("Revert to Legacy Settings"))
					{
						if (EditorUtility.DisplayDialog("Remove Reference", "If you revert to legacy settings you will need to manually assign this behaviour to all the Races and/or Slots that you want to use it. Are you sure?", "Yes, Revert", "Cancel"))
						{
							converterControllerProp.objectReferenceValue = null;
							serializedObject.ApplyModifiedProperties();
							drawAsLegacy = false;
						}
					}
					GUILayout.EndHorizontal();
				}
				//DISPLAY VALUE
				EditorGUILayout.Space();
				EditorGUILayout.PropertyField(serializedObject.FindProperty("_displayValue"));
				EditorGUILayout.Space();

				DrawDNAAssetGUI();

				EditorGUILayout.Space();

				DrawDNAConvertersGUI();

				EditorGUILayout.Space();

				DrawOverallModifiersGUI();

				EditorGUILayout.Space();
				serializedObject.ApplyModifiedProperties();

				if (drawAsLegacy)
				{
					EditorGUI.indentLevel--;
				}
			}

#if UNITY_2018_3_OR_NEWER
			//Do a stronger nag to the user to update the prefab
			//If we are in Unity 2018.3+ we will only see this if the user has inspected the prefab and clicked 'Open Prefab'
			//I that case tell the user that the prefabs are now legacy and that:
			//If there is a converterController asset already assigned they can click 'Find and Replace Usage' to update all races and slots that were using this prefab
			//If there is no converterController asset, they should do 'Upgrade' in the 'Upgrade Available' section
			if (!_nagPerformed && Event.current.type == EventType.Repaint)
			{
				string nagTitle = "";
				string nagMessage = "";
				if (converterControllerProp.objectReferenceValue == null)
				{
					nagTitle = "Obsolete DNA Prefab";
					nagMessage = "UMA no longer needs to use DNAConverter prefabs for DNA. The new DNAConverterController assets offer more flexibility and have been designed to work better with Unity 2018.3+ Please go to the 'Upgrade Available' section in this asset and click 'Upgrade'";
				}
				else
				{
					nagTitle = "Obsolete DNA Prefab";
					nagMessage = "This DNAConverter prefab has already been upgraded to a new DNAConverterController. If some of your Races or Slots are still using this asset directly, please click the 'Find and Replace Usage' button in this asset to update all your assets automatically.";
				}
				_nagPerformed = true;
				//annoyingly this makes the inspector draw twice!!
				EditorUtility.DisplayDialog(nagTitle, nagMessage, "Got it");
			}
#endif

		}

		private void DrawDNAAssetGUI()
		{
			SerializedProperty dnaAsset = serializedObject.FindProperty("_dnaAsset");
			dnaAsset.isExpanded = EditorGUILayout.Foldout(dnaAsset.isExpanded, "Dynamic DNA Asset", foldoutTipStyle);
			if (dnaAsset.isExpanded)
			{
				if (dnaAsset.objectReferenceValue == null)
				{
					//UMA 2.8+ FicDNAPrefabs DONT do this if this is just displaying 'Legacy' Data
					if (!drawAsLegacy)
					{
						//show a tip that people need to create or assign a dna asset
						EditorGUILayout.HelpBox("Assign a DNA Asset this Behaviour will use. This defines the names that will be available to the DNA Converters when modifying the Avatar. Often displayed in the UI as 'sliders'", MessageType.Info);
					}
				}
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(dnaAsset, new GUIContent("DNA Asset", "A DNA Asset defines the names that will be available to the DNA Converters when modifying the Avatar. Often displayed in the UI as 'sliders'"));
				if (EditorGUI.EndChangeCheck())
				{
					UpdateNames();
					serializedObject.ApplyModifiedProperties();
					serializedObject.Update();//?

					//force the Avatar to update its dna and dnaconverter dictionaries
					//UMA 2.8+ FicDNAPrefabs DONT do this if this is just displaying 'Legacy' Data
					if (!drawAsLegacy && umaData != null)
					{
						umaData.umaRecipe.ClearDna();
						umaData.umaRecipe.ClearDNAConverters();
					}
				}
			}
			serializedObject.ApplyModifiedProperties();
		}

		private void DrawDNAConvertersGUI()
		{
			converterControllerProp.isExpanded = EditorGUILayout.Foldout(converterControllerProp.isExpanded, "DNA Converter Settings", foldoutTipStyle);
			if (converterControllerProp.isExpanded)
			{
				//UMA2.8+ FixDNAPrefabs Now we always draw like this
				//If the asset has been updated to a converterController these settings will show in a 'Legacy Settings' area
				/*if (!drawOldGUI)
				{
					//UMA2.8+ FixDNAPrefabs we are not going to use this UI any more because the converterController should be used directly
					if (converterControllerProp.objectReferenceValue == null)
					{
						//show a tip that people need to create or assign a dna asset
						EditorGUILayout.HelpBox("Assign a Converter Controller Asset this Behaviour will use. This asset defines the DNA Converters (SkeletonModifiers, BonePoses etc) that will make changes to the Avatar based on the values for the DNA names defined above", MessageType.Info);
					}
					EditorGUI.BeginDisabledGroup(true);
					EditorGUI.BeginChangeCheck();
					EditorGUILayout.PropertyField(converterControllerProp, new GUIContent("Converter Controller", "The Converter Controller Asset defines the DNA Converters (SkeletonModifiers, BonePoses etc) that will make changes to the Avatar based on the values for the DNA names defined above"));
					if (EditorGUI.EndChangeCheck())
					{
						//UMA2.8+ FixDNAPrefabs DynamicDNAController has no 'converterBehaviour' field now
						//Make sure the converterController has this target as its converterBehaviour value
						if (converterControllerProp.objectReferenceValue != null)
						{
							((DynamicDNAConverterController)converterControllerProp.objectReferenceValue).converterBehaviour = target as DynamicDNAConverterBehaviour;
							//We dont save because the same controller can be used by lots of converters
						}
						serializedObject.ApplyModifiedProperties();
						serializedObject.Update();
						initialized = false;
						Init();
					}
					EditorGUI.EndDisabledGroup();
				}

				if (converterControllerProp.objectReferenceValue != null)
				{
					//UMA2.8+ FixDNAPrefabs- now we can use the ConverterController asset directly, we dont actually want to show the 'old' new UI
					//Instead we want to show UPDATE tools (rather than 'Upgrade Tools') is there is a controller
					//This is because content creators might distribute races or slots that use the old converterBehaviour and we still want users
					//To easily be able to fix this.
					((DynamicDNAConverterController)converterControllerProp.objectReferenceValue).converterBehaviour = target as DynamicDNAConverterBehaviour;
					//DrawUpdateTools();
					//if there is an UMABonePose popup inspector open set the umaData as its sourceUMA
					if (UMA.PoseTools.UMABonePoseEditor.livePopupEditor != null)
					{
						UMA.PoseTools.UMABonePoseEditor.livePopupEditor.sourceUMA = umaData;
						UMA.PoseTools.UMABonePoseEditor.livePopupEditor.dynamicDNAConverterMode = true;
					}
				}
				else
				{
					if (drawOldGUI)
					{
						//DrawUpgradeTools();
						if (_legacyDrawer == null)
						{
							_legacyDrawer = new LegacyDynamicDNAConverterGUIDrawer();
							_legacyDrawer.Init(_target, serializedObject, umaData, thisDDCC, bonesInSkeleton, minimalMode);
						}
						_legacyDrawer.DrawLegacySkeletonModifiersGUI();
						_legacyDrawer.DrawLegacyStartingPoseGUI();
					}
				}*/
				if (_legacyDrawer == null)
				{
					_legacyDrawer = new LegacyDynamicDNAConverterGUIDrawer();
					_legacyDrawer.Init(_target, serializedObject, umaData, thisDDCC, bonesInSkeleton);
				}
				_legacyDrawer.DrawLegacySkeletonModifiersGUI();
				_legacyDrawer.DrawLegacyStartingPoseGUI();
			}
		}

		private void DrawUpdateTools()
		{
			var upgradeRect = EditorGUILayout.GetControlRect();

			GUIHelper.ToolbarStyleHeader(upgradeRect, new GUIContent("Update Tools"), new string[0], ref _upgradeExpanded, null, EditorStyles.boldLabel);
			_upgradeExpanded = true;

			GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			EditorGUILayout.HelpBox("If you have not done so already (or you are seeing console warnings), you should assign the new controller asset directly to all Races and Slots that are using this legacy behaviour. Click 'Find and Replace Usage' to do this automatically.", MessageType.Info);
			//Allows the user to find and replace usage again (if for example they have added races or slots that use the old converters)
			if (GUILayout.Button("Find and Replace Usage"))
			{
				var confrimMsg = "This will replace all usage of " + _target.gameObject.name + " with " + (converterControllerProp.objectReferenceValue as DynamicDNAConverterController).name + " in all RaceDatas and SlotDatas that are using the old DNAConverterBehaviour.";
				if(EditorUtility.DisplayDialog("Replace all usage of " + _target.gameObject.name+"?", confrimMsg, "Proceed", "Cancel"))
					_target.FindAndReplaceUsage(converterControllerProp.objectReferenceValue as DynamicDNAConverterController);
			}
			GUIHelper.EndVerticalPadded(3);
		}

		private void DrawUpgradeTools()
		{
			if (converterControllerProp.objectReferenceValue == null)
			{
				var upgradeRect = EditorGUILayout.GetControlRect();
				var prevColor = GUI.color;
				GUI.color = new Color(1, 0.9f, 0, 1);
				//GUIHelper.ToolbarStyleFoldout(upgradeRect, "Upgrade Available!", ref _upgradeExpanded, null, foldoutTipStyle);
				GUIHelper.ToolbarStyleHeader(upgradeRect, new GUIContent("Upgrade Available!"), new string[0], ref _upgradeExpanded, null, EditorStyles.boldLabel);
				_upgradeExpanded = true;
				GUI.color = prevColor;
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUILayout.HelpBox("Please click the 'Upgrade' button to upgrade this ConverterBehaviour into a ConverterController asset that is better suited for Unity 2018+", MessageType.Info);
				var moreRect = EditorGUILayout.GetControlRect();
				GUIHelper.ToolbarStyleFoldout(moreRect, "More Info", ref upgradeInfoExpanded, GUIStyle.none);
				if (upgradeInfoExpanded)
				{
					EditorGUILayout.HelpBox(upgradeInfo1, MessageType.None);
					EditorGUILayout.HelpBox(upgradeInfo2, MessageType.None);
					EditorGUILayout.HelpBox(upgradeInfo3, MessageType.None);
				}
				GUILayout.BeginHorizontal();
				GUILayout.Space(10);
				if (GUILayout.Button("Upgrade"))
				{
					DoUpgrade();
				}
				GUILayout.Space(10);
				GUILayout.EndHorizontal();
				EditorGUILayout.Space();
				GUIHelper.EndVerticalPadded(3);
			}
		}

		public void DrawOverallModifiersGUI()
		{
			var overallModifiersProp = serializedObject.FindProperty("_overallModifiers");
			var overallModsFoldoutRect = EditorGUILayout.GetControlRect();
			overallModsFoldoutRect.height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			var overallModsLabel = EditorGUI.BeginProperty(overallModsFoldoutRect, new GUIContent(overallModifiersProp.displayName), overallModifiersProp );

			overallModifiersProp.isExpanded = EditorGUI.Foldout(overallModsFoldoutRect, overallModifiersProp.isExpanded, overallModsLabel, true, foldoutTipStyle);

			if (overallModifiersProp.isExpanded)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				GUILayout.Space(5);
				EditorGUILayout.PropertyField(overallModifiersProp);
				GUIHelper.EndVerticalPadded(3);
			}
			EditorGUI.EndProperty();
			GUILayout.Space(5);
			
		}

		private void DoUpgrade()
		{
			var originalName = _target.gameObject.name;
			DynamicDNAConverterController newController = _target.DoUpgrade();
			if (newController != null)
			{
				drawAsLegacy = true;

				EditorGUIUtility.PingObject(newController);
				EditorUtility.DisplayDialog("Upgrade Complete!", "Your SkeletonModifiers and StartingPose (if set) can now be found in the new 'Converter Controller'. The old "+ originalName + " has been stored in a 'LegacyDNA' folder", "Got it!");

				//We need to make the new asset the one that ConverterCustomizer is inspecting if it was inspecting the old one
				if (thisDDCC != null)
				{
					for(int i = 0; i < thisDDCC.availableConverters.Count; i++)
					{
						if (thisDDCC.availableConverters[i] is DynamicDNAConverterBehaviour && (thisDDCC.availableConverters[i] as DynamicDNAConverterBehaviour) == _target)
							thisDDCC.availableConverters[i] = newController;
					}
					if (thisDDCC.selectedConverter is DynamicDNAConverterBehaviour && (thisDDCC.selectedConverter as DynamicDNAConverterBehaviour) == _target)
						thisDDCC.selectedConverter = newController;
				}
				//otherwise select the new controller so it shows in the inspector	
				else
				{
					Selection.activeObject = newController;
				}
			}
		}
	}
}
