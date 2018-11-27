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

		[MenuItem("Assets/Create/UMA/DNA/Dynamic DNA Converter Behaviour")]
		public static void CreateDynamicDNAConverterBehaviour()
		{
			CustomAssetUtility.CreatePrefab("DynamicDNAConverterBehaviour", typeof(DynamicDNAConverterBehaviour));
		}

		DynamicDNAConverterBehaviour _target;
		
		SerializedProperty converterControllerProp;

		//Set by the customizer in play mode
		public UMAData umaData = null;
		//Set by the customizer in play mode
		public DynamicDNAConverterCustomizer thisDDCC = null;
		
		//Set by the customizer in play mode
		//minimalMode is the mode that is used when a DynamicDnaConverterBehaviour is shown in a DynamicDnaConverterCustomizer rather than when its inspected directly
		public bool minimalMode = false;

		private LegacyDynamicDNAConverterGUIDrawer _legacyDrawer = null;

		//moved into legacyDrawer- may still be useful here
		private List<string> bonesInSkeleton = new List<string>();


		//Upgrade fields and  bools
		private bool _upgradeExpanded = false;
		bool upgradeInfoExpanded = false;

		string upgradeInfo1 = "DynamicDNAConverterBehaviours now give you the ability to use multiple kinds of converters! This means the same dna can control your SkeletonModifers, Blendshapes, BonePoses etc";
		string upgradeInfo2 = "The system also comes with a simple API so you can add your own plugins to the system, allowing you to make dna make any changes you can imagine.";
		string upgradeInfo3 = "Clicking the 'Backup & Upgrade' button below will make a backup of this behaviour and then transfer all its settings over to the new system.";

		//We only draw the old GUI if the legacy fields (_skeletonModifiers and _startingPose) have values
		bool drawOldGUI = true;

		//post upgrade info? Explain where starting poses are now? Or show a Wiki?

		GUIStyle foldoutTipStyle;
		bool overallModifiersHelpExpanded = false;

		//Referenced by the customizer in play mode
		[System.NonSerialized]
		public bool initialized = false;

#pragma warning disable 618
		private void Init()
		{
			if (!initialized)
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

				converterControllerProp = serializedObject.FindProperty("_converterController");

				if (converterControllerProp.objectReferenceValue == null)
				{
					if (_target.skeletonModifiers.Count > 0 || _target.startingPose != null)
					{
						drawOldGUI = true;
					}
					else
					{
						drawOldGUI = false;
					}
				}
				else
				{
					drawOldGUI = false;
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
						_legacyDrawer.Init(target, serializedObject, umaData, thisDDCC, bonesInSkeleton, minimalMode);
					}
				}
			}
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			this.Init();

			//DISPLAY VALUE
			EditorGUILayout.Space();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("DisplayValue"));
			EditorGUILayout.Space();

			DrawDNAAssetGUI();

			EditorGUILayout.Space();

			DrawDNAConvertersGUI();

			EditorGUILayout.Space();

			DrawOverallModifiersGUI();

			EditorGUILayout.Space();
			serializedObject.ApplyModifiedProperties();

		}
		
		private void DrawDNAAssetGUI()
		{
			SerializedProperty dnaAsset = serializedObject.FindProperty("dnaAsset");
			if(drawOldGUI)
				dnaAsset.isExpanded = EditorGUILayout.Foldout(dnaAsset.isExpanded, "Dynamic DNA Asset", foldoutTipStyle);
			if (dnaAsset.isExpanded || !drawOldGUI)
			{
				if (dnaAsset.objectReferenceValue == null)
				{
					//show a tip that people need to create or assign a dna asset
					EditorGUILayout.HelpBox("Assign a DNA Asset this Behaviour will use. This defines the names that will be available to the DNA Converters when modifying the Avatar. Often displayed in the UI as 'sliders'", MessageType.Info);
				}
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(dnaAsset, new GUIContent("DNA Asset", "A DNA Asset defines the names that will be available to the DNA Converters when modifying the Avatar. Often displayed in the UI as 'sliders'"));
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
			}
			serializedObject.ApplyModifiedProperties();
		}

		private void DrawDNAConvertersGUI()
		{
			if(drawOldGUI)
				converterControllerProp.isExpanded = EditorGUILayout.Foldout(converterControllerProp.isExpanded, "DNA Converter Settings", foldoutTipStyle);
			if (converterControllerProp.isExpanded || !drawOldGUI)
			{
				if (!drawOldGUI)
				{
					if (converterControllerProp.objectReferenceValue == null)
					{
						//show a tip that people need to create or assign a dna asset
						EditorGUILayout.HelpBox("Assign a Converter Controller Asset this Behaviour will use. This asset defines the DNA Converters (SkeletonModifiers, BonePoses etc) that will make changes to the Avatar based on the values for the DNA names defined above", MessageType.Info);
					}

					EditorGUI.BeginChangeCheck();
					EditorGUILayout.PropertyField(converterControllerProp, new GUIContent("Converter Controller", "The Converter Controller Asset defines the DNA Converters (SkeletonModifiers, BonePoses etc) that will make changes to the Avatar based on the values for the DNA names defined above"));
					if (EditorGUI.EndChangeCheck())
					{
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
				}

				if (converterControllerProp.objectReferenceValue != null)
				{
					((DynamicDNAConverterController)converterControllerProp.objectReferenceValue).converterBehaviour = target as DynamicDNAConverterBehaviour;
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
						DrawUpgradeTools();
						if (_legacyDrawer == null)
						{
							_legacyDrawer = new LegacyDynamicDNAConverterGUIDrawer();
							_legacyDrawer.Init(target, serializedObject, umaData, thisDDCC, bonesInSkeleton, minimalMode);
						}
						_legacyDrawer.DrawLegacySkeletonModifiersGUI();
						_legacyDrawer.DrawLegacyStartingPoseGUI();
					}
				}
			}
		}

		private void DrawUpgradeTools()
		{
			//We only need to draw this if the skeletonModifiers and startingPose arrays are not empty
			if (converterControllerProp.objectReferenceValue == null && drawOldGUI)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				var upgradeRect = EditorGUILayout.GetControlRect();
				var prevColor = GUI.color;
				GUI.color = new Color(1, 0.9f, 0, 1);
				GUIHelper.ToolbarStyleFoldout(upgradeRect, "Upgrade Available!", ref _upgradeExpanded, null, foldoutTipStyle);
				GUI.color = prevColor;
				if (_upgradeExpanded)
				{
					EditorGUILayout.HelpBox("Please click the 'Backup & Upgrade' button to upgrade this ConverterBehaviour", MessageType.Info);
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
					if (GUILayout.Button("Backup & Upgrade"))
					{
						DoBackupAndUpgrade();
					}
					GUILayout.Space(10);
					GUILayout.EndHorizontal();
					EditorGUILayout.Space();
				}
				GUIHelper.EndVerticalPadded(3);
			}
		}

		public void DrawOverallModifiersGUI()
		{
			var newOverallModifiersProp = serializedObject.FindProperty("_overallModifiers");
			bool overallModifiersExpanded = newOverallModifiersProp.isExpanded;
			var overallModsFoldoutRect = EditorGUILayout.GetControlRect();
			overallModsFoldoutRect.height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			var overallModsLabel = EditorGUI.BeginProperty(overallModsFoldoutRect, new GUIContent(newOverallModifiersProp.displayName), newOverallModifiersProp );
			if (drawOldGUI)
			{
				EditorGUI.BeginChangeCheck();
				overallModifiersExpanded = EditorGUI.Foldout(overallModsFoldoutRect,overallModifiersExpanded, overallModsLabel, true, foldoutTipStyle);
				if (EditorGUI.EndChangeCheck())
				{
					newOverallModifiersProp.isExpanded = overallModifiersExpanded;
				}
			}
			if (overallModifiersExpanded || !drawOldGUI)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				if (!drawOldGUI)
				{
					//GUILayout.Space(5);
					EditorGUI.BeginChangeCheck();
					overallModsLabel.text = overallModsLabel.text.ToUpper();
					GUIHelper.ToolbarStyleFoldout(overallModsFoldoutRect, overallModsLabel.text.ToUpper(), new string[] { overallModsLabel.tooltip }, ref overallModifiersExpanded, ref overallModifiersHelpExpanded);
					if (EditorGUI.EndChangeCheck())
					{
						newOverallModifiersProp.isExpanded = overallModifiersExpanded;
					}
					//GUILayout.Space(5);
				}
				if (overallModifiersExpanded)
				{
					GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
					GUILayout.Space(5);
					EditorGUILayout.PropertyField(newOverallModifiersProp);
					GUIHelper.EndVerticalPadded(3);
				}
				GUIHelper.EndVerticalPadded(3);
			}
			EditorGUI.EndProperty();
			GUILayout.Space(5);
			
		}

		private void DoBackupAndUpgrade()
		{
			if (_target.BackupAndUpgrade())
			{
				drawOldGUI = false;
				EditorUtility.DisplayDialog("Upgrade Complete!", "Your SkeletonModifiers and StartingPose (if set) can now be found in the new 'Converter Controller'. The Converter Controller allows you to define many more types of DNA Converters than before. You can inspect it by clicking the 'Inspect' button next to the field", "Got it!");
				
			}
		}
	}
}
