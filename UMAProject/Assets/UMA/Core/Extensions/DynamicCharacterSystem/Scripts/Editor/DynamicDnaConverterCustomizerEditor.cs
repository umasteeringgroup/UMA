using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.Editors;

namespace UMA.CharacterSystem.Editors
{
    [CustomEditor(typeof(DynamicDNAConverterCustomizer), true)]
    public class DynamicDNAConverterCustomizerEditor : Editor
    {
        DynamicDNAConverterCustomizer thisDDCC;
        Dictionary<IDNAConverter, Editor> SDCBs = new Dictionary<IDNAConverter, Editor>();

		//For BonePose CreationTools
		string createBonePoseAssetName = "";
		bool applyAndResetOnCreateBP = true;

		//With DynamicDNAPlugins Update the editor for the DNA Converters (Skeleton Modifiers etc) no longer displays directly in the ConverterBehaviour
		//but is viewed in its own popup inspector (so it is clear to the user its a seperate asset). We still want to know if anything has been edited in there though
		//so we can do live updates to the avatar in play mode, so subscribe to OnLivePopupEditorChange so we get notified
		private void OnEnable()
		{
			DynamicDNAConverterControllerInspector.OnLivePopupEditorChange.RemoveListener(OnLiveConverterControllerChange);
			DynamicDNAConverterControllerInspector.OnLivePopupEditorChange.AddListener(OnLiveConverterControllerChange);
		}

		public void OnLiveConverterControllerChange()
		{
			thisDDCC = target as DynamicDNAConverterCustomizer;
			thisDDCC.UpdateUMA();
		}

		public override void OnInspectorGUI()
        {
            thisDDCC = target as DynamicDNAConverterCustomizer;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dynamicDnaConverterPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TposeAnimatorController"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AposeAnimatorController"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MovementAnimatorController"));
            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                if (serializedObject.FindProperty("TposeAnimatorController").objectReferenceValue != null)
                {
                    if (GUILayout.Button("Set T-Pose"))
                    {
                        thisDDCC.SetTPoseAni();
                    }
                }
                if (serializedObject.FindProperty("AposeAnimatorController").objectReferenceValue != null)
                {
                    if (GUILayout.Button("Set A-Pose"))
                    {
                        thisDDCC.SetAPoseAni();
                    }
                }
                if (serializedObject.FindProperty("MovementAnimatorController").objectReferenceValue != null)
                {
                    if (GUILayout.Button("Animate UMA"))
                    {
                        thisDDCC.SetMovementAni();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetUMA"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("guideUMA"));
            if(serializedObject.FindProperty("guideUMA").objectReferenceValue != null && Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Align Guide To Target"))
                {
                    thisDDCC.AlignGuideToTarget();
                }
                if (GUILayout.Button("Import Guide DNA Values"))
                {
                    thisDDCC.ImportGuideDNAValues();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (Application.isPlaying)
            {
				//UMA 2.8+ FixDNAPrefabs - this is a runtime only list of IDNAConverters now
                //SerializedProperty availableConvertersProp = serializedObject.FindProperty("availableConverters");
                //SerializedProperty selectedConverterProp = serializedObject.FindProperty("selectedConverter");
                List<string> availableConvertersPopup = new List<string>();
                availableConvertersPopup.Add("None Selected");
                int selectedConverterIndex = 0;
                int newSelectedConverterIndex = 0;
                /*for (int i = 0; i < availableConvertersProp.arraySize; i++)
                {
                    availableConvertersPopup.Add(availableConvertersProp.GetArrayElementAtIndex(i).objectReferenceValue.name);
                    if (selectedConverterProp.objectReferenceValue != null)
                        if (availableConvertersProp.GetArrayElementAtIndex(i).objectReferenceValue.name == selectedConverterProp.objectReferenceValue.name)
                        {
                            selectedConverterIndex = i + 1;
                        }
                }*/
				for(int i = 0; i < thisDDCC.availableConverters.Count; i++)
				{
					if (!(thisDDCC.availableConverters[i] is IDynamicDNAConverter) || thisDDCC.availableConverters[i] == null)
						continue;
					availableConvertersPopup.Add(thisDDCC.availableConverters[i].name);
					if (thisDDCC.selectedConverter != null && thisDDCC.selectedConverter == thisDDCC.availableConverters[i])
						selectedConverterIndex = i + 1;
				}
                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                newSelectedConverterIndex = EditorGUILayout.Popup("Target UMA Converter", selectedConverterIndex, availableConvertersPopup.ToArray());
                if (EditorGUI.EndChangeCheck())
                {
                    if (newSelectedConverterIndex != selectedConverterIndex)
                    {
                        if (newSelectedConverterIndex == 0)
                        {
							thisDDCC.selectedConverter = null;
                        }
                        else
                        {
							thisDDCC.selectedConverter = thisDDCC.availableConverters[newSelectedConverterIndex - 1];
                        }
                        serializedObject.ApplyModifiedProperties();//Doesn't make sense now?
                        thisDDCC.BackupConverter();
                    }
                }
            }
            if (thisDDCC.selectedConverter != null)
            {
				thisDDCC.StartListeningForUndo();
				//import like this makes no sense now
				/*
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUILayout.LabelField("Import Settings from another Converter", EditorStyles.boldLabel);
                var ImportFromConverterR = EditorGUILayout.GetControlRect(false);
                var ImportFromConverterLabelR = ImportFromConverterR;
                var ImportFromConverterFieldR = ImportFromConverterR;
                var ImportFromConverterButR = ImportFromConverterR;
                ImportFromConverterLabelR.width = 140;
                ImportFromConverterButR.width = 70;
                ImportFromConverterFieldR.width = ImportFromConverterFieldR.width - ImportFromConverterLabelR.width - ImportFromConverterButR.width;
                ImportFromConverterFieldR.x = ImportFromConverterLabelR.xMax;
                ImportFromConverterButR.x = ImportFromConverterFieldR.xMax + 5;
                EditorGUI.LabelField(ImportFromConverterLabelR, "Import from Converter");
                EditorGUI.ObjectField(ImportFromConverterFieldR, serializedObject.FindProperty("converterToImport"), GUIContent.none);
                if (serializedObject.FindProperty("converterToImport").objectReferenceValue == null)
                    EditorGUI.BeginDisabledGroup(true);
                if(GUI.Button(ImportFromConverterButR, "Import"))
                {
                    if (thisDDCC.ImportConverterValues())
                    {
                        serializedObject.FindProperty("converterToImport").objectReferenceValue = null;
                    }
                }
                if (serializedObject.FindProperty("converterToImport").objectReferenceValue == null)
                    EditorGUI.EndDisabledGroup();
                GUIHelper.EndVerticalPadded(10);
				*/
                //
                Editor thisSDCB;
                if(SDCBs.TryGetValue(thisDDCC.selectedConverter, out thisSDCB))
                {
					if (thisDDCC.selectedConverter is DynamicDNAConverterBehaviour)
					{
						((DynamicDNAConverterBehaviourEditor)thisSDCB).initialized = true;
					}
					else if (thisDDCC.selectedConverter is DynamicDNAConverterController)
					{
						//Might need UMAData to work
						//((DynamicDNAConverterControllerInspector)thisSDCB)
					}
				}
                else
                {
					if (thisDDCC.selectedConverter is DynamicDNAConverterBehaviour)
					{
						thisSDCB = Editor.CreateEditor((thisDDCC.selectedConverter as DynamicDNAConverterBehaviour), typeof(DynamicDNAConverterBehaviourEditor));
						SDCBs.Add(thisDDCC.selectedConverter, thisSDCB);
					}
					else if(thisDDCC.selectedConverter is DynamicDNAConverterController)
					{
						thisSDCB = Editor.CreateEditor((thisDDCC.selectedConverter as DynamicDNAConverterController), typeof(DynamicDNAConverterControllerInspector));
						SDCBs.Add(thisDDCC.selectedConverter, thisSDCB);
					}
                }
				if (thisDDCC.selectedConverter is DynamicDNAConverterBehaviour)
				{
					((DynamicDNAConverterBehaviourEditor)thisSDCB).thisDDCC = thisDDCC;
					((DynamicDNAConverterBehaviourEditor)thisSDCB).umaData = thisDDCC.targetUMA.umaData;
				}
				else if(thisDDCC.selectedConverter is DynamicDNAConverterController)
				{

				}

				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUILayout.LabelField("Edit Values", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                thisSDCB.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck())
                {
                    thisDDCC.UpdateUMA();
                }
				GUIHelper.EndVerticalPadded(10);

				//The following only makes sense for DynamicDNAConverterBehaviour right now
				//But we want to make them both work the same, and work the default Unity way
				//i.e. now we want to keep the changes by default and revert them if the user requests that
				//Altho changes to a component DONT get changed permanently in Play mode
				//and embedding the editor makes it LOOK LIKE we are editing a component
				if (thisDDCC.selectedConverter is DynamicDNAConverterBehaviour)
				{
					
					GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f, 0.3f));
					EditorGUILayout.LabelField("Save Values", EditorStyles.boldLabel);
					Rect thisR = EditorGUILayout.GetControlRect(false);
					var thisButReset = thisR;
					var thisButSave = thisR;
					var thisButSaveNew = thisR;
					thisButReset.width = thisButSave.width = thisButSaveNew.width = (thisR.width / 3) - 2;
					thisButSave.x = thisButReset.xMax + 5;
					thisButSaveNew.x = thisButSave.xMax + 5;
					if (GUI.Button(thisButReset, new GUIContent("Reset", "Undo your changes to the currently selected converter")))
					{
						thisDDCC.RestoreBackupVersion(serializedObject.FindProperty("selectedConverter").objectReferenceValue.name);
					}
					if (GUI.Button(thisButSave, new GUIContent("Save", "Save your changes to the currently selected converter")))
					{
						thisDDCC.SaveChanges();
					}
					if (GUI.Button(thisButSaveNew, new GUIContent("Save as New", "Save your changes to a new converter instance")))
					{
						thisDDCC.SaveChangesAsNew();
					}
					GUIHelper.EndVerticalPadded(10);
				}
				DrawBonePoseCreationTools();
			}
			else
			{
				thisDDCC.StopListeningForUndo();
			}
			serializedObject.ApplyModifiedProperties();
        }

		private void DrawBonePoseCreationTools()
		{
			if (thisDDCC.targetUMA.umaData.skeleton != null)
			{
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f, 0.3f));
				EditorGUILayout.LabelField("Create Poses from Current DNA state", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Create bone poses from Avatar's current dna modified state. Applies the pose and sets DNA values back to 0.", MessageType.None);
				EditorGUILayout.HelpBox("Tip: Ensure all modifications you do not want included are turned off/set to default. In particular you will probably want to set 'Overall Modifiers' scale to 1 if you are planning to apply this in addition to the Pose later on.", MessageType.Info);
				EditorGUILayout.HelpBox("Smaller margin of error equals greater accuracy but creates more poses to apply on DNA Update.", MessageType.None);
				if (thisDDCC != null)
				{
					//[Range(0.000005f, 0.0005f)]
					EditorGUI.BeginChangeCheck();
					var thisAccuracy = EditorGUILayout.Slider(new GUIContent("Margin Of Error", "The smaller the margin of error, the more accurate the Pose will be, but it will also have more bonePoses to apply when DNA is updated"), thisDDCC.bonePoseAccuracy * 1000, 0.5f, 0.005f);
					if (EditorGUI.EndChangeCheck())
					{
						thisDDCC.bonePoseAccuracy = thisAccuracy / 1000;
						GUI.changed = false;
					}
				}
				createBonePoseAssetName = EditorGUILayout.TextField("New Bone Pose Name",createBonePoseAssetName);
				EditorGUILayout.HelpBox("Should the pose be applied and the dna values be reset to 0?", MessageType.None);
				applyAndResetOnCreateBP = EditorGUILayout.Toggle("Apply and Reset", applyAndResetOnCreateBP);
				GUILayout.BeginHorizontal();
				GUILayout.Space(20);
				if (GUILayout.Button(/*createFromDnaButR, */"Create New BonePose Asset"))
				{
					if (thisDDCC != null)
					{
						if (thisDDCC.CreateBonePosesFromCurrentDna(createBonePoseAssetName, applyAndResetOnCreateBP))
						{
							serializedObject.Update();
							createBonePoseAssetName = "";
							//this needs to repaint the plugins because their height of the reorderable list has changed now
							//cant figure out how to do that though
						}
					}
				}
				GUILayout.Space(20);
				GUILayout.EndHorizontal();
				GUIHelper.EndVerticalPadded(10);
			}
		}
   }

}
