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
        Dictionary<DynamicDNAConverterBehaviour, Editor> SDCBs = new Dictionary<DynamicDNAConverterBehaviour, Editor>();
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
                SerializedProperty availableConvertersProp = serializedObject.FindProperty("availableConverters");
                SerializedProperty selectedConverterProp = serializedObject.FindProperty("selectedConverter");
                List<string> availableConvertersPopup = new List<string>();
                availableConvertersPopup.Add("None Selected");
                int selectedConverterIndex = 0;
                int newSelectedConverterIndex = 0;
                for (int i = 0; i < availableConvertersProp.arraySize; i++)
                {
                    availableConvertersPopup.Add(availableConvertersProp.GetArrayElementAtIndex(i).objectReferenceValue.name);
                    if (selectedConverterProp.objectReferenceValue != null)
                        if (availableConvertersProp.GetArrayElementAtIndex(i).objectReferenceValue.name == selectedConverterProp.objectReferenceValue.name)
                        {
                            selectedConverterIndex = i + 1;
                        }
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
                            selectedConverterProp.objectReferenceValue = null;
                        }
                        else
                        {
                            selectedConverterProp.objectReferenceValue = availableConvertersProp.GetArrayElementAtIndex(newSelectedConverterIndex - 1).objectReferenceValue;
                        }
                        serializedObject.ApplyModifiedProperties();
                        thisDDCC.BackupConverter();
                    }
                }
            }
            if (serializedObject.FindProperty("selectedConverter").objectReferenceValue != null)
            {
				thisDDCC.StartListeningForUndo();
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
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
                //
                Editor thisSDCB;
                if(SDCBs.TryGetValue((DynamicDNAConverterBehaviour)serializedObject.FindProperty("selectedConverter").objectReferenceValue, out thisSDCB))
                {
                    ((DynamicDNAConverterBehaviourEditor)thisSDCB).initialized = true;
                }
                else
                {
                    thisSDCB = Editor.CreateEditor((DynamicDNAConverterBehaviour)serializedObject.FindProperty("selectedConverter").objectReferenceValue, typeof(DynamicDNAConverterBehaviourEditor));
                    SDCBs.Add((DynamicDNAConverterBehaviour)serializedObject.FindProperty("selectedConverter").objectReferenceValue, thisSDCB);
                }
                ((DynamicDNAConverterBehaviourEditor)thisSDCB).minimalMode = true;
                ((DynamicDNAConverterBehaviourEditor)thisSDCB).thisDDCC = thisDDCC;
                ((DynamicDNAConverterBehaviourEditor)thisSDCB).umaData = thisDDCC.targetUMA.umaData;
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                EditorGUILayout.LabelField("Edit Values", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                thisSDCB.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck())
                {
                    thisDDCC.UpdateUMA();
                }
                GUIHelper.EndVerticalPadded(10);
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
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
			else
			{
				thisDDCC.StopListeningForUndo();
			}
			serializedObject.ApplyModifiedProperties();
        }
   }

}
