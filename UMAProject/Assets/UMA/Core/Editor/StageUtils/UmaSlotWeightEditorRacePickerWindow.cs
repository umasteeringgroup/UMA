using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    internal class UmaSlotWeightEditorRacePickerWindow : EditorWindow
    {
        private SlotDataAsset slotAsset;
        private List<RaceData> races = new List<RaceData>();
        private int selectedRaceIndex;
        private string helpMessage;

        public static void Open(SlotDataAsset slotAsset, List<RaceData> races, string helpMessage = null)
        {
            UmaSlotWeightEditorRacePickerWindow window = CreateInstance<UmaSlotWeightEditorRacePickerWindow>();
            window.titleContent = new GUIContent("Select Preview Race");
            window.minSize = new Vector2(420f, 130f);
            window.Initialize(slotAsset, races, helpMessage);
            window.ShowUtility();
            window.Focus();
        }

        private void Initialize(SlotDataAsset slotAsset, List<RaceData> races, string helpMessage)
        {
            this.slotAsset = slotAsset;
            this.helpMessage = helpMessage;
            this.races.Clear();
            if (races != null)
            {
                for (int i = 0; i < races.Count; i++)
                {
                    if (races[i] != null)
                    {
                        this.races.Add(races[i]);
                    }
                }
            }
            selectedRaceIndex = 0;
        }

        private void OnGUI()
        {
            if (slotAsset == null || races.Count == 0)
            {
                EditorGUILayout.HelpBox("No compatible preview races are available for this slot.", MessageType.Warning);
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
                return;
            }

            EditorGUILayout.LabelField("Slot", slotAsset.slotName);
            if (!string.IsNullOrEmpty(helpMessage))
            {
                EditorGUILayout.HelpBox(helpMessage, MessageType.Info);
            }
            string[] raceNames = new string[races.Count];
            for (int i = 0; i < races.Count; i++)
            {
                raceNames[i] = races[i].raceName;
            }

            selectedRaceIndex = EditorGUILayout.Popup("Preview Race", selectedRaceIndex, raceNames);
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open"))
            {
                VertexEditorStage.ShowSlotWeightEditorStage(slotAsset, races[selectedRaceIndex]);
                Close();
            }
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
