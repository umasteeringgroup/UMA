using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA
{
    internal class UmaTouchupWeightsWindow : EditorWindow
    {
        private const float BonePanelWidth = 220f;

        private VertexEditorStage stage;
        private List<SlotData> slots = new List<SlotData>();
        private List<VertexEditorStage.BoneOption> bones = new List<VertexEditorStage.BoneOption>();
        private List<VertexEditorStage.VertexWeightEntry> editableWeights = new List<VertexEditorStage.VertexWeightEntry>();
        private Vector2 boneScroll;
        private Vector2 weightScroll;
        private int slotIndex;
        private int selectedBoneHash;
        private string boneFilter = string.Empty;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private int selectionSignature = int.MinValue;
        private bool weightsDirty;

        internal static UmaTouchupWeightsWindow Open(VertexEditorStage stage)
        {
            UmaTouchupWeightsWindow window = GetWindow<UmaTouchupWeightsWindow>(false, "Touchup Weights", true);
            window.minSize = new Vector2(660f, 520f);
            window.Initialize(stage);
            window.Show();
            window.Focus();
            return window;
        }

        private void Initialize(VertexEditorStage owner)
        {
            stage = owner;
            titleContent = new GUIContent("Touchup Weights", EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image);
            RefreshSlots();
            RefreshSelection(true);
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (stage == null || !stage.IsTouchupWeightsMode)
            {
                EditorGUILayout.HelpBox("The Touchup Weights stage is no longer available.", MessageType.Warning);
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
                return;
            }

            RefreshSlots();
            RefreshSelection(false);

            EditorGUILayout.HelpBox(
                "Choose one SlotDataAsset, select vertices on the posed character with the circle brush, then edit their bone weights. " +
                "When several vertices are selected, the first selected vertex supplies the values and Save writes those values to every selected vertex.",
                MessageType.Info);

            DrawSlotPicker();
            DrawSelectionSummary();

            EditorGUILayout.Space(3f);
            EditorGUILayout.BeginHorizontal();
            DrawBonePanel();
            DrawWeightPanel();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            DrawFooter();
        }

        private void DrawSlotPicker()
        {
            if (slots.Count == 0)
            {
                EditorGUILayout.HelpBox("This character has no visible slots with editable mesh data.", MessageType.Warning);
                return;
            }

            string[] labels = new string[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                SlotData slot = slots[i];
                string assetName = slot.asset != null ? slot.asset.name : "Missing Asset";
                labels[i] = slot.slotName + "  [" + assetName + "]";
            }

            using (new EditorGUI.DisabledScope(stage.HasPendingTouchupPaintWeights))
            {
                EditorGUI.BeginChangeCheck();
                slotIndex = EditorGUILayout.Popup(new GUIContent("Slot", "Only this slot can be selected and edited."), slotIndex, labels);
                if (EditorGUI.EndChangeCheck())
                {
                    stage.SetTouchupWeightSlot(slots[slotIndex]);
                    selectedBoneHash = stage.TouchupWeightBoneHash;
                    bones = stage.GetTouchupBoneOptions();
                    editableWeights.Clear();
                    selectionSignature = int.MinValue;
                    weightsDirty = false;
                    statusMessage = string.Empty;
                }
            }
            if (stage.HasPendingTouchupPaintWeights)
            {
                EditorGUILayout.HelpBox(
                    "Save or revert the painted weights before changing slots.",
                    MessageType.Info);
            }
        }

        private void DrawSelectionSummary()
        {
            int count = stage.TouchupSelectionCount;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Selected Vertices", count.ToString(), GUILayout.MaxWidth(240f));
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("Clear Selection", GUILayout.Width(120f)))
                {
                    stage.ClearTouchupSelection();
                    RefreshSelection(true);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBonePanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(BonePanelWidth));
            EditorGUILayout.LabelField("Bones", EditorStyles.boldLabel);
            boneFilter = EditorGUILayout.TextField(new GUIContent("Search"), boneFilter);
            DrawWeightLegend();

            boneScroll = EditorGUILayout.BeginScrollView(boneScroll);
            bool foundBone = false;
            for (int i = 0; i < bones.Count; i++)
            {
                VertexEditorStage.BoneOption bone = bones[i];
                if (!string.IsNullOrWhiteSpace(boneFilter) &&
                    bone.displayName.IndexOf(boneFilter.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                foundBone = true;
                bool selected = bone.boneHash == selectedBoneHash;
                Color previousBackground = GUI.backgroundColor;
                if (selected)
                {
                    GUI.backgroundColor = new Color(0.45f, 0.72f, 1f);
                }
                if (GUILayout.Button(new GUIContent(bone.boneName, bone.displayName), EditorStyles.miniButton))
                {
                    selectedBoneHash = bone.boneHash;
                    stage.TouchupWeightBoneHash = selectedBoneHash;
                    GUI.FocusControl(null);
                }
                GUI.backgroundColor = previousBackground;
            }
            if (!foundBone)
            {
                EditorGUILayout.HelpBox("No bones match the search.", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawWeightLegend()
        {
            Rect legendRect = GUILayoutUtility.GetRect(1f, 10f, GUILayout.ExpandWidth(true));
            float sectionWidth = legendRect.width / 4f;
            EditorGUI.DrawRect(new Rect(legendRect.x, legendRect.y, sectionWidth, legendRect.height), Color.blue);
            EditorGUI.DrawRect(new Rect(legendRect.x + sectionWidth, legendRect.y, sectionWidth, legendRect.height), Color.cyan);
            EditorGUI.DrawRect(new Rect(legendRect.x + sectionWidth * 2f, legendRect.y, sectionWidth, legendRect.height), Color.green);
            EditorGUI.DrawRect(new Rect(legendRect.x + sectionWidth * 3f, legendRect.y, sectionWidth, legendRect.height), Color.red);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("0", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Weight", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(60f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("1", EditorStyles.miniLabel, GUILayout.Width(10f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWeightPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Weights", EditorStyles.boldLabel);

            if (stage.HasPendingTouchupPaintWeights)
            {
                EditorGUILayout.HelpBox(
                    "Painted weight changes are pending. Save or revert them before editing individual numeric weights.",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            VertexEditorStage.VertexSelection first = stage.GetFirstTouchupSelectedVertex();
            if (first == null)
            {
                EditorGUILayout.HelpBox(
                    "Use the Select Brush in the Scene view to choose one or more vertices. Shift adds and Ctrl removes while brushing.",
                    MessageType.Info);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField("Reference Vertex", first.vertexIndexOnSlot.ToString());
            if (stage.TouchupSelectionCount > 1)
            {
                EditorGUILayout.HelpBox(
                    "Editing the first selected vertex's values. Save applies this same set of weights to all " +
                    stage.TouchupSelectionCount + " selected vertices.",
                    MessageType.None);
            }

            weightScroll = EditorGUILayout.BeginScrollView(weightScroll);
            int removeIndex = -1;
            for (int i = 0; i < editableWeights.Count; i++)
            {
                VertexEditorStage.VertexWeightEntry entry = editableWeights[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.boneName, GUILayout.MinWidth(120f));
                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.Slider(entry.weight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    entry.weight = newWeight;
                    weightsDirty = true;
                    stage.SetTouchupWeightPreview(editableWeights);
                }
                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Minus"), GUILayout.Width(28f)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (removeIndex >= 0)
            {
                editableWeights.RemoveAt(removeIndex);
                weightsDirty = true;
                stage.SetTouchupWeightPreview(editableWeights);
            }

            if (editableWeights.Count == 0)
            {
                EditorGUILayout.HelpBox("This vertex has no weights. Select a bone and add it below.", MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();

            float total = GetWeightTotal();
            EditorGUILayout.LabelField("Total", total.ToString("0.0000"));
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(selectedBoneHash == 0))
            {
                if (GUILayout.Button("Add Selected Bone"))
                {
                    AddSelectedBone();
                }
            }
            using (new EditorGUI.DisabledScope(total <= Mathf.Epsilon))
            {
                if (GUILayout.Button("Normalize"))
                {
                    NormalizeWeights(total);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();
            bool hasPendingPaint = stage.HasPendingTouchupPaintWeights;
            bool canSaveNumeric =
                stage.TouchupSelectionCount > 0 && editableWeights.Count > 0;
            using (new EditorGUI.DisabledScope(!hasPendingPaint && !canSaveNumeric))
            {
                if (GUILayout.Button(
                        weightsDirty || hasPendingPaint ? "Save Weights *" : "Save Weights",
                        GUILayout.Height(26f)))
                {
                    bool saved = hasPendingPaint
                        ? stage.TrySavePendingTouchupPaintWeights(out statusMessage)
                        : stage.TrySaveTouchupWeights(editableWeights, out statusMessage);
                    if (saved)
                    {
                        statusType = MessageType.Info;
                        weightsDirty = false;
                        RefreshSelection(true);
                    }
                    else
                    {
                        statusType = MessageType.Error;
                    }
                }
            }
            using (new EditorGUI.DisabledScope(!weightsDirty && !hasPendingPaint))
            {
                if (GUILayout.Button("Revert", GUILayout.Height(26f), GUILayout.Width(72f)))
                {
                    if (hasPendingPaint)
                    {
                        stage.RevertPendingTouchupPaintWeights();
                    }
                    else
                    {
                        stage.ClearTouchupWeightPreview();
                    }
                    RefreshSelection(true);
                    statusMessage = hasPendingPaint
                        ? "Reverted unsaved painted weight edits."
                        : "Reverted unsaved numeric weight edits.";
                    statusType = MessageType.Info;
                }
            }
            if (GUILayout.Button("Close Touchup Weights", GUILayout.Height(26f), GUILayout.Width(170f)))
            {
                if (TryResolveUnsavedChangesBeforeClose())
                {
                    StageUtility.GoBackToPreviousStage();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        internal bool TryResolveUnsavedChangesBeforeClose()
        {
            if (stage == null ||
                (!weightsDirty && !stage.HasPendingTouchupPaintWeights))
            {
                return true;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Touchup Weights",
                stage.HasPendingTouchupPaintWeights
                    ? "Painted weight changes have not been saved."
                    : "Numeric weight changes have not been saved.",
                "Save",
                "Discard",
                "Cancel");
            if (choice == 2)
            {
                return false;
            }

            if (choice == 1)
            {
                if (stage.HasPendingTouchupPaintWeights)
                {
                    stage.RevertPendingTouchupPaintWeights();
                }
                else
                {
                    stage.ClearTouchupWeightPreview();
                }
                weightsDirty = false;
                return true;
            }

            bool saved = stage.HasPendingTouchupPaintWeights
                ? stage.TrySavePendingTouchupPaintWeights(out statusMessage)
                : stage.TrySaveTouchupWeights(editableWeights, out statusMessage);
            statusType = saved ? MessageType.Info : MessageType.Error;
            if (saved)
            {
                weightsDirty = false;
            }
            return saved;
        }

        private void RefreshSlots()
        {
            if (stage == null)
            {
                return;
            }

            List<SlotData> available = stage.GetTouchupWeightSlots();
            slots = available;
            if (slots.Count == 0)
            {
                slotIndex = 0;
                bones.Clear();
                return;
            }

            SlotData active = stage.TouchupWeightSlot;
            slotIndex = Mathf.Clamp(slotIndex, 0, slots.Count - 1);
            for (int i = 0; i < slots.Count; i++)
            {
                if (ReferenceEquals(slots[i], active))
                {
                    slotIndex = i;
                    break;
                }
            }

            bones = stage.GetTouchupBoneOptions();
            selectedBoneHash = stage.TouchupWeightBoneHash;
        }

        private void RefreshSelection(bool force)
        {
            if (stage == null)
            {
                return;
            }

            List<VertexEditorStage.VertexSelection> selected = stage.GetTouchupSelectedVertices();
            int signature = 17;
            unchecked
            {
                signature = signature * 31 + stage.TouchupWeightsRevision;
            }
            for (int i = 0; i < selected.Count; i++)
            {
                unchecked
                {
                    signature = signature * 31 + selected[i].vertexIndexOnSlot;
                    signature = signature * 31 + (selected[i].slot != null ? selected[i].slot.slotName.GetHashCode() : 0);
                }
            }

            if (!force && signature == selectionSignature)
            {
                return;
            }

            selectionSignature = signature;
            stage.ClearTouchupWeightPreview();
            editableWeights.Clear();
            VertexEditorStage.VertexSelection first = stage.GetFirstTouchupSelectedVertex();
            if (first != null)
            {
                List<VertexEditorStage.VertexWeightEntry> source = stage.GetSlotAssetVertexWeights(first, out string message);
                for (int i = 0; i < source.Count; i++)
                {
                    editableWeights.Add(source[i].Clone());
                }
                if (!string.IsNullOrEmpty(message))
                {
                    statusMessage = message;
                    statusType = MessageType.Info;
                }
            }
            weightsDirty = false;
        }

        private void AddSelectedBone()
        {
            for (int i = 0; i < editableWeights.Count; i++)
            {
                if (editableWeights[i].boneHash == selectedBoneHash)
                {
                    editableWeights[i].weight = Mathf.Max(editableWeights[i].weight, 0.1f);
                    weightsDirty = true;
                    stage.SetTouchupWeightPreview(editableWeights);
                    return;
                }
            }

            for (int i = 0; i < bones.Count; i++)
            {
                VertexEditorStage.BoneOption bone = bones[i];
                if (bone.boneHash != selectedBoneHash)
                {
                    continue;
                }
                editableWeights.Add(new VertexEditorStage.VertexWeightEntry
                {
                    boneIndex = bone.boneIndex,
                    boneHash = bone.boneHash,
                    boneName = bone.boneName,
                    weight = 0.1f
                });
                weightsDirty = true;
                stage.SetTouchupWeightPreview(editableWeights);
                return;
            }
        }

        private float GetWeightTotal()
        {
            float total = 0f;
            for (int i = 0; i < editableWeights.Count; i++)
            {
                total += Mathf.Clamp01(editableWeights[i].weight);
            }
            return total;
        }

        private void NormalizeWeights(float total)
        {
            if (total <= Mathf.Epsilon)
            {
                return;
            }
            for (int i = 0; i < editableWeights.Count; i++)
            {
                editableWeights[i].weight = Mathf.Clamp01(editableWeights[i].weight) / total;
            }
            weightsDirty = true;
            stage.SetTouchupWeightPreview(editableWeights);
        }
    }
}
