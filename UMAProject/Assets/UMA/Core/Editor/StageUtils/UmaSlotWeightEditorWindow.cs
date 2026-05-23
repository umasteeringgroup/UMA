using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    internal class UmaSlotWeightEditorWindow : EditorWindow
    {
        private VertexEditorStage stage;
        private SlotDataAsset slotAsset;
        private bool currentCharacterMode;
        private VertexEditorStage.VertexSelection selectedVertex;
        private List<VertexEditorStage.VertexWeightEntry> editableSlotWeights = new List<VertexEditorStage.VertexWeightEntry>();
        private List<VertexEditorStage.VertexWeightEntry> skinnedWeights = new List<VertexEditorStage.VertexWeightEntry>();
        private List<VertexEditorStage.BoneOption> boneOptions = new List<VertexEditorStage.BoneOption>();
        private Vector2 scrollPosition;
        private string slotStatusMessage;
        private string skinnedStatusMessage;
        private string actionStatusMessage;
        private string boneFilter = string.Empty;
        private int filteredBoneIndex;
        private float newBoneWeight;

        public static UmaSlotWeightEditorWindow Open(VertexEditorStage stage, SlotDataAsset slotAsset)
        {
            UmaSlotWeightEditorWindow window = GetWindow<UmaSlotWeightEditorWindow>(true, "Slot Weights", true);
            window.minSize = new Vector2(600f, 520f);
            window.Initialize(stage, slotAsset);
            window.Show();
            window.Focus();
            return window;
        }

        public static void Open(DynamicCharacterAvatar avatar)
        {
            VertexEditorStage.OpenCurrentCharacterWeightViewer(avatar);
        }

        [MenuItem("UMA/View Current Character Weights", priority = 23)]
        private static void OpenSelectedCurrentCharacterWeights()
        {
            DynamicCharacterAvatar avatar = GetSelectedDynamicCharacterAvatar();
            if (avatar == null)
            {
                EditorUtility.DisplayDialog("View Current Character Weights", "Select a DynamicCharacterAvatar, or one of its children, in the Hierarchy.", "OK");
                return;
            }

            Open(avatar);
        }

        [MenuItem("UMA/View Current Character Weights", true)]
        private static bool OpenSelectedCurrentCharacterWeights_Validate()
        {
            return GetSelectedDynamicCharacterAvatar() != null;
        }

        [MenuItem("CONTEXT/DynamicCharacterAvatar/View Current Character Weights")]
        private static void OpenContextCurrentCharacterWeights(MenuCommand command)
        {
            Open(command.context as DynamicCharacterAvatar);
        }

        [MenuItem("CONTEXT/DynamicCharacterAvatar/View Current Character Weights", true)]
        private static bool OpenContextCurrentCharacterWeights_Validate(MenuCommand command)
        {
            return command.context is DynamicCharacterAvatar;
        }

        private static DynamicCharacterAvatar GetSelectedDynamicCharacterAvatar()
        {
            DynamicCharacterAvatar selectedAvatar = Selection.activeObject as DynamicCharacterAvatar;
            if (selectedAvatar != null)
            {
                return selectedAvatar;
            }

            GameObject selectedObject = Selection.activeGameObject;
            return selectedObject != null ? selectedObject.GetComponentInParent<DynamicCharacterAvatar>() : null;
        }

        private void Initialize(VertexEditorStage stage, SlotDataAsset slotAsset)
        {
            this.stage = stage;
            this.slotAsset = slotAsset;
            currentCharacterMode = slotAsset == null || (stage != null && stage.IsSlotWeightEditorReadOnly);
            titleContent = new GUIContent(currentCharacterMode ? "Character Weights" : "Slot Weights");
            RefreshFromStageSelection(true);
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (stage == null)
            {
                EditorGUILayout.HelpBox("The weight editor stage is no longer available.", MessageType.Warning);
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
                return;
            }

            RefreshFromStageSelection(false);

            DrawHeader();

            if (selectedVertex == null || selectedVertex.slot == null)
            {
                EditorGUILayout.HelpBox(currentCharacterMode ? "Select a vertex in the stage to view the current character weights." : "Select a vertex in the stage to view and edit its weights.", MessageType.Info);
                DrawFooterButtons(false);
                return;
            }

            EditorGUILayout.LabelField("Selected Slot", selectedVertex.slot.slotName);
            if (selectedVertex.slot.asset != null)
            {
                EditorGUILayout.LabelField("Slot Asset", selectedVertex.slot.asset.slotName);
            }
            EditorGUILayout.LabelField("Vertex", selectedVertex.vertexIndexOnSlot.ToString());

            List<VertexEditorStage.VertexWeightComparison> comparisons = stage.BuildWeightComparisons(editableSlotWeights, skinnedWeights);
            bool hasMismatch = HasMismatch(comparisons);
            string matchMessage = currentCharacterMode
                ? (hasMismatch ? "Mismatch: SlotDataAsset weights do not match the current character SkinnedMesh weights." : "OK: SlotDataAsset weights match the current character SkinnedMesh weights.")
                : (hasMismatch ? "Mismatch: SlotDataAsset weights do not match the current SkinnedMesh weights." : "OK: SlotDataAsset weights match the current SkinnedMesh weights.");
            EditorGUILayout.HelpBox(matchMessage, hasMismatch ? MessageType.Warning : MessageType.Info);

            if (!string.IsNullOrEmpty(slotStatusMessage))
            {
                EditorGUILayout.HelpBox(slotStatusMessage, MessageType.Info);
            }
            if (!string.IsNullOrEmpty(skinnedStatusMessage))
            {
                EditorGUILayout.HelpBox(skinnedStatusMessage, MessageType.Info);
            }
            if (!string.IsNullOrEmpty(actionStatusMessage))
            {
                EditorGUILayout.HelpBox(actionStatusMessage, MessageType.Info);
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawComparison(comparisons);
            EditorGUILayout.Space();
            if (currentCharacterMode)
            {
                DrawReadonlyWeights("SlotDataAsset Weights", editableSlotWeights, slotStatusMessage);
                EditorGUILayout.Space();
                DrawReadonlyWeights("Current SkinnedMesh Weights", skinnedWeights, skinnedStatusMessage);
            }
            else
            {
                DrawEditableWeights();
                EditorGUILayout.Space();
                DrawAddWeight();
            }
            EditorGUILayout.EndScrollView();

            DrawFooterButtons(true);
        }

        private void DrawHeader()
        {
            if (currentCharacterMode)
            {
                EditorGUILayout.ObjectField("Avatar", stage.thisDCA, typeof(DynamicCharacterAvatar), true);
            }
            else if (slotAsset != null)
            {
                EditorGUILayout.LabelField("Target Slot", slotAsset.slotName);
            }

            if (stage.SlotWeightEditorRace != null)
            {
                EditorGUILayout.LabelField("Race", stage.SlotWeightEditorRace.raceName);
            }
        }

        private void RefreshFromStageSelection(bool force)
        {
            if (stage == null)
            {
                return;
            }

            VertexEditorStage.VertexSelection currentVertex = stage.GetVertexForWeightPopup();
            if (!force && IsSameVertex(selectedVertex, currentVertex))
            {
                return;
            }

            selectedVertex = currentVertex;
            RefreshData();
        }

        private bool IsSameVertex(VertexEditorStage.VertexSelection left, VertexEditorStage.VertexSelection right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return left.slot == right.slot && left.vertexIndexOnSlot == right.vertexIndexOnSlot;
        }

        private void RefreshData()
        {
            editableSlotWeights.Clear();
            skinnedWeights.Clear();
            boneOptions.Clear();
            slotStatusMessage = string.Empty;
            skinnedStatusMessage = string.Empty;
            actionStatusMessage = string.Empty;

            if (stage == null || selectedVertex == null)
            {
                return;
            }

            List<VertexEditorStage.VertexWeightEntry> slotWeights = stage.GetSlotAssetVertexWeights(selectedVertex, out slotStatusMessage);
            for (int i = 0; i < slotWeights.Count; i++)
            {
                editableSlotWeights.Add(slotWeights[i].Clone());
            }

            skinnedWeights = stage.GetSkinnedMeshVertexWeights(selectedVertex, out skinnedStatusMessage);
            if (!currentCharacterMode)
            {
                boneOptions = stage.GetEditableBoneOptions(selectedVertex);
            }
        }

        private bool HasMismatch(List<VertexEditorStage.VertexWeightComparison> comparisons)
        {
            for (int i = 0; i < comparisons.Count; i++)
            {
                if (comparisons[i].mismatch)
                {
                    return true;
                }
            }
            return false;
        }

        private void DrawComparison(List<VertexEditorStage.VertexWeightComparison> comparisons)
        {
            EditorGUILayout.LabelField(currentCharacterMode ? "SlotDataAsset vs Current SkinnedMesh" : "SlotDataAsset vs SkinnedMesh", EditorStyles.boldLabel);
            if (comparisons.Count == 0)
            {
                EditorGUILayout.HelpBox("No weights are available to compare.", MessageType.Info);
                return;
            }

            for (int i = 0; i < comparisons.Count; i++)
            {
                VertexEditorStage.VertexWeightComparison comparison = comparisons[i];
                string message = (comparison.mismatch ? "Mismatch" : "OK") + " - " + comparison.boneName + " | SlotDataAsset: " + FormatWeight(comparison.slotWeight) + " | SkinnedMesh: " + FormatWeight(comparison.skinnedWeight);
                EditorGUILayout.HelpBox(message, comparison.mismatch ? MessageType.Warning : MessageType.None);
            }
        }

        private void DrawEditableWeights()
        {
            EditorGUILayout.LabelField("Edit SlotDataAsset Weights", EditorStyles.boldLabel);
            if (editableSlotWeights.Count == 0)
            {
                EditorGUILayout.HelpBox("No SlotDataAsset weights are currently assigned to this vertex.", MessageType.Info);
            }

            for (int i = 0; i < editableSlotWeights.Count; i++)
            {
                VertexEditorStage.VertexWeightEntry weight = editableSlotWeights[i];
                EditorGUILayout.BeginHorizontal();
                string label = weight.boneName + " (" + (weight.boneIndex >= 0 ? "index " + weight.boneIndex : "new binding") + ")";
                EditorGUILayout.LabelField(label, GUILayout.MinWidth(240f));
                weight.weight = Mathf.Clamp01(EditorGUILayout.FloatField(weight.weight, GUILayout.Width(72f)));
                bool removeWeight = GUILayout.Button("Remove", GUILayout.Width(76f));
                EditorGUILayout.EndHorizontal();

                if (removeWeight)
                {
                    editableSlotWeights.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
            }

            float total = GetEditableWeightTotal();
            EditorGUILayout.LabelField("Total", FormatWeight(total));
            EditorGUI.BeginDisabledGroup(total <= 0f);
            if (GUILayout.Button("Normalize"))
            {
                NormalizeEditableWeights(total);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawAddWeight()
        {
            EditorGUILayout.LabelField("Add Bone Weight", EditorStyles.boldLabel);
            boneFilter = EditorGUILayout.TextField("Bone Filter", boneFilter);
            List<VertexEditorStage.BoneOption> filteredOptions = GetFilteredBoneOptions();
            if (filteredOptions.Count == 0)
            {
                EditorGUILayout.HelpBox("No matching bones are available to add.", MessageType.Info);
                return;
            }

            if (filteredBoneIndex >= filteredOptions.Count)
            {
                filteredBoneIndex = 0;
            }

            string[] optionNames = new string[filteredOptions.Count];
            for (int i = 0; i < filteredOptions.Count; i++)
            {
                optionNames[i] = filteredOptions[i].displayName;
            }

            filteredBoneIndex = EditorGUILayout.Popup("Bone", filteredBoneIndex, optionNames);
            newBoneWeight = Mathf.Clamp01(EditorGUILayout.FloatField("Weight", newBoneWeight));
            if (GUILayout.Button("Add"))
            {
                VertexEditorStage.BoneOption option = filteredOptions[filteredBoneIndex];
                editableSlotWeights.Add(new VertexEditorStage.VertexWeightEntry()
                {
                    boneIndex = option.boneIndex,
                    boneHash = option.boneHash,
                    boneName = option.boneName,
                    weight = newBoneWeight
                });
                newBoneWeight = 0f;
            }
        }

        private void DrawReadonlyWeights(string title, List<VertexEditorStage.VertexWeightEntry> weights, string emptyMessage)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (weights == null || weights.Count == 0)
            {
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(emptyMessage) ? "No weights are available." : emptyMessage, MessageType.Info);
                return;
            }

            for (int i = 0; i < weights.Count; i++)
            {
                VertexEditorStage.VertexWeightEntry weight = weights[i];
                if (weight == null)
                {
                    continue;
                }

                string label = weight.boneName + " (" + (weight.boneIndex >= 0 ? "index " + weight.boneIndex : "new binding") + ")";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(label, GUILayout.MinWidth(240f));
                EditorGUILayout.LabelField(FormatWeight(weight.weight), GUILayout.Width(90f));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFooterButtons(bool canApply)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh"))
            {
                RefreshFromStageSelection(true);
            }
            EditorGUI.BeginDisabledGroup(!canApply);
            if (!currentCharacterMode && GUILayout.Button("Apply to SlotDataAsset"))
            {
                if (stage.TryApplySlotAssetVertexWeights(selectedVertex, editableSlotWeights, out actionStatusMessage))
                {
                    string applyStatusMessage = actionStatusMessage;
                    stage.RebuildMesh(true);
                    RefreshFromStageSelection(true);
                    actionStatusMessage = applyStatusMessage;
                }
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Close"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        private List<VertexEditorStage.BoneOption> GetFilteredBoneOptions()
        {
            List<VertexEditorStage.BoneOption> filteredOptions = new List<VertexEditorStage.BoneOption>();
            string normalizedFilter = string.IsNullOrWhiteSpace(boneFilter) ? string.Empty : boneFilter.Trim();
            for (int i = 0; i < boneOptions.Count; i++)
            {
                VertexEditorStage.BoneOption option = boneOptions[i];
                if (HasEditableBone(option.boneHash))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(normalizedFilter) && option.displayName.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                filteredOptions.Add(option);
            }
            return filteredOptions;
        }

        private bool HasEditableBone(int boneHash)
        {
            for (int i = 0; i < editableSlotWeights.Count; i++)
            {
                if (editableSlotWeights[i].boneHash == boneHash)
                {
                    return true;
                }
            }
            return false;
        }

        private float GetEditableWeightTotal()
        {
            float total = 0f;
            for (int i = 0; i < editableSlotWeights.Count; i++)
            {
                total += editableSlotWeights[i].weight;
            }
            return total;
        }

        private void NormalizeEditableWeights(float total)
        {
            if (total <= 0f)
            {
                return;
            }

            for (int i = 0; i < editableSlotWeights.Count; i++)
            {
                editableSlotWeights[i].weight /= total;
            }
        }

        private string FormatWeight(float weight)
        {
            return weight.ToString("0.######");
        }
    }
}
