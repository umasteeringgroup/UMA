using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public sealed class SlotBlendshapeNameRenamer : EditorWindow
    {
        private const string WindowTitle = "Slot Blendshape Renamer";
        private const string UndoLabel = "Remove Text from Blendshape Names";

        [SerializeField] private SlotDataAsset slotDataAsset;
        [SerializeField] private string textToRemove = "BrowsBS.";

        private Vector2 previewScroll;
        private string resultMessage;
        private string[] observedBlendshapeNames = Array.Empty<string>();

        [MenuItem("UMA/Rename Slot Blendshapes...", false, 2000)]
        private static void OpenFromAssetsMenu()
        {
            SlotBlendshapeNameRenamer window = GetWindow<SlotBlendshapeNameRenamer>(true, WindowTitle);
            window.minSize = new Vector2(480f, 360f);
            window.SetSlot(GetSelectedSlot());
            window.Show();
        }

        [MenuItem("Window/NF3D/Slot Blendshape Renamer")]
        private static void OpenFromWindowMenu()
        {
            SlotBlendshapeNameRenamer window = GetWindow<SlotBlendshapeNameRenamer>(WindowTitle);
            window.minSize = new Vector2(480f, 360f);
            if (window.slotDataAsset == null)
            {
                window.SetSlot(GetSelectedSlot());
            }
            window.Show();
        }

        private static SlotDataAsset GetSelectedSlot()
        {
            UnityEngine.Object[] selectedSlots = Selection.GetFiltered(
                typeof(SlotDataAsset), SelectionMode.Assets);
            return selectedSlots.Length > 0 ? selectedSlots[0] as SlotDataAsset : null;
        }

        private void OnEnable()
        {
            observedBlendshapeNames = CaptureBlendshapeNames(slotDataAsset);
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Remove exact, case-sensitive text from every matching blendshape name stored in a Slot Data Asset.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            SlotDataAsset selectedSlot = (SlotDataAsset)EditorGUILayout.ObjectField(
                "Slot Data Asset", slotDataAsset, typeof(SlotDataAsset), false);
            string removalText = EditorGUILayout.TextField("Text to Remove", textToRemove);
            if (EditorGUI.EndChangeCheck())
            {
                if (selectedSlot != slotDataAsset)
                {
                    SetSlot(selectedSlot);
                }
                textToRemove = removalText;
                resultMessage = null;
            }

            EditorGUILayout.Space();

            List<RenamePreview> previews = BuildPreview(slotDataAsset, textToRemove);
            string validationError = ValidateResult(slotDataAsset, textToRemove);

            EditorGUILayout.LabelField(
                $"Preview ({previews.Count} change{(previews.Count == 1 ? string.Empty : "s")})",
                EditorStyles.boldLabel);
            previewScroll = EditorGUILayout.BeginScrollView(
                previewScroll, GUILayout.MinHeight(150f));
            if (previews.Count == 0)
            {
                EditorGUILayout.LabelField(
                    GetEmptyPreviewMessage(slotDataAsset, textToRemove), EditorStyles.miniLabel);
            }
            else
            {
                foreach (RenamePreview preview in previews)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.SelectableLabel(
                        preview.Original, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    GUILayout.Label("->", GUILayout.Width(20f));
                    EditorGUILayout.SelectableLabel(
                        preview.Renamed, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(validationError))
            {
                EditorGUILayout.HelpBox(validationError, MessageType.Warning);
            }
            else if (!string.IsNullOrEmpty(resultMessage))
            {
                EditorGUILayout.HelpBox(resultMessage, MessageType.Info);
            }

            EditorGUILayout.HelpBox(
                "Only the names stored in this Slot Data Asset are changed. Update any recipes, expression controls, or custom code that refer to the old names.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(
                       previews.Count == 0 || !string.IsNullOrEmpty(validationError)))
            {
                if (GUILayout.Button(
                        $"Remove Text from {previews.Count} Blendshape Name{(previews.Count == 1 ? string.Empty : "s")}",
                        GUILayout.Height(30f)))
                {
                    ApplyRename();
                }
            }
        }

        private void SetSlot(SlotDataAsset slot)
        {
            slotDataAsset = slot;
            observedBlendshapeNames = CaptureBlendshapeNames(slotDataAsset);
            resultMessage = null;
        }

        private static string GetEmptyPreviewMessage(SlotDataAsset slot, string removalText)
        {
            if (slot == null)
            {
                return "Choose a Slot Data Asset to preview its blendshape names.";
            }
            if (slot.meshData?.blendShapes == null || slot.meshData.blendShapes.Length == 0)
            {
                return "The selected slot has no blendshapes.";
            }
            if (string.IsNullOrEmpty(removalText))
            {
                return "Enter the exact text to remove.";
            }
            return "No blendshape names contain the requested text.";
        }

        private static List<RenamePreview> BuildPreview(
            SlotDataAsset slot, string removalText)
        {
            var previews = new List<RenamePreview>();
            UMABlendShape[] blendShapes = slot?.meshData?.blendShapes;
            if (blendShapes == null || string.IsNullOrEmpty(removalText))
            {
                return previews;
            }

            for (int index = 0; index < blendShapes.Length; index++)
            {
                UMABlendShape blendShape = blendShapes[index];
                string originalName = blendShape?.shapeName;
                if (string.IsNullOrEmpty(originalName) ||
                    originalName.IndexOf(removalText, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                previews.Add(new RenamePreview(
                    index,
                    originalName,
                    originalName.Replace(removalText, string.Empty)));
            }

            return previews;
        }

        private static string ValidateResult(SlotDataAsset slot, string removalText)
        {
            string nameError = ValidateNames(slot, removalText);
            if (!string.IsNullOrEmpty(nameError))
            {
                return nameError;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return "Wait for Unity to finish compiling or importing assets.";
            }

            string assetPath = AssetDatabase.GetAssetPath(slot.GetEntityId());
            if (string.IsNullOrEmpty(assetPath))
            {
                return "The selected Slot Data Asset is not a persistent project asset.";
            }
            if (!AssetDatabase.IsOpenForEdit(
                    assetPath, out string editMessage,
                    StatusQueryOptions.UseCachedIfPossible))
            {
                return string.IsNullOrEmpty(editMessage)
                    ? $"The Slot Data Asset is read-only or not checked out: '{assetPath}'."
                    : editMessage;
            }

            return null;
        }

        private static string ValidateNames(SlotDataAsset slot, string removalText)
        {
            if (slot == null)
            {
                return "Choose a Slot Data Asset.";
            }
            if (slot.meshData?.blendShapes == null || slot.meshData.blendShapes.Length == 0)
            {
                return "The selected Slot Data Asset has no blendshape data.";
            }
            if (string.IsNullOrEmpty(removalText))
            {
                return "Enter the text to remove.";
            }

            var resultingNames = new Dictionary<string, OriginalName>(StringComparer.Ordinal);
            for (int index = 0; index < slot.meshData.blendShapes.Length; index++)
            {
                UMABlendShape blendShape = slot.meshData.blendShapes[index];
                if (blendShape == null)
                {
                    continue;
                }

                string originalName = blendShape.shapeName ?? string.Empty;
                string renamedName = originalName.Replace(removalText, string.Empty);
                bool changed = !string.Equals(originalName, renamedName, StringComparison.Ordinal);

                if (string.IsNullOrWhiteSpace(renamedName))
                {
                    return changed
                        ? $"Removing that text would make blendshape '{originalName}' at index {index} empty."
                        : $"Blendshape at index {index} already has an empty name. Fix it before renaming this slot.";
                }

                if (resultingNames.TryGetValue(renamedName, out OriginalName first))
                {
                    bool duplicateAlreadyExisted = string.Equals(
                        first.Name, originalName, StringComparison.Ordinal);
                    return duplicateAlreadyExisted
                        ? $"The slot already contains duplicate blendshape name '{originalName}' at indices {first.Index} and {index}. Fix it before renaming this slot."
                        : $"Removing that text would create duplicate blendshape name '{renamedName}' from indices {first.Index} and {index}.";
                }

                resultingNames.Add(renamedName, new OriginalName(index, originalName));
            }

            return null;
        }

        private void ApplyRename()
        {
            string validationError = ValidateResult(slotDataAsset, textToRemove);
            List<RenamePreview> previews = BuildPreview(slotDataAsset, textToRemove);
            if (!string.IsNullOrEmpty(validationError) || previews.Count == 0)
            {
                resultMessage = validationError ?? "No matching blendshape names were found.";
                Repaint();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Rename Blendshapes",
                    $"Remove '{textToRemove}' from {previews.Count} blendshape name{(previews.Count == 1 ? string.Empty : "s")} in '{slotDataAsset.name}'?\n\nThis operation can be undone.",
                    "Rename",
                    "Cancel"))
            {
                return;
            }

            // Rebuild and validate after the modal dialog so the operation never applies
            // a stale preview if another editor changed the asset while it was open.
            validationError = ValidateResult(slotDataAsset, textToRemove);
            previews = BuildPreview(slotDataAsset, textToRemove);
            if (!string.IsNullOrEmpty(validationError) || previews.Count == 0)
            {
                resultMessage = validationError ?? "The slot changed and no matching names remain.";
                Repaint();
                return;
            }

            Undo.RecordObject(slotDataAsset, UndoLabel);
            foreach (RenamePreview preview in previews)
            {
                slotDataAsset.meshData.blendShapes[preview.Index].shapeName = preview.Renamed;
            }
            Undo.FlushUndoRecordObjects();

            EditorUtility.SetDirty(slotDataAsset);
            AssetDatabase.SaveAssetIfDirty(slotDataAsset);
            observedBlendshapeNames = CaptureBlendshapeNames(slotDataAsset);
            RefreshSlotUsers(slotDataAsset);

            resultMessage =
                $"Renamed and saved {previews.Count} blendshape name{(previews.Count == 1 ? string.Empty : "s")}.";
            Repaint();
        }

        private void OnUndoRedoPerformed()
        {
            string[] currentNames = CaptureBlendshapeNames(slotDataAsset);
            if (BlendshapeNamesEqual(observedBlendshapeNames, currentNames))
            {
                return;
            }

            observedBlendshapeNames = currentNames;
            resultMessage = "Undo/redo changed the slot's blendshape names.";
            RefreshSlotUsers(slotDataAsset);
            Repaint();
        }

        private static void RefreshSlotUsers(SlotDataAsset slot)
        {
            if (slot == null)
            {
                return;
            }

            UMAUpdateProcessor.UpdateSlot(slot, false);
            SceneView.RepaintAll();
        }

        private static string[] CaptureBlendshapeNames(SlotDataAsset slot)
        {
            UMABlendShape[] blendShapes = slot?.meshData?.blendShapes;
            if (blendShapes == null || blendShapes.Length == 0)
            {
                return Array.Empty<string>();
            }

            var names = new string[blendShapes.Length];
            for (int index = 0; index < blendShapes.Length; index++)
            {
                names[index] = blendShapes[index]?.shapeName;
            }
            return names;
        }

        private static bool BlendshapeNamesEqual(string[] left, string[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private readonly struct OriginalName
        {
            public readonly int Index;
            public readonly string Name;

            public OriginalName(int index, string name)
            {
                Index = index;
                Name = name;
            }
        }

        private sealed class RenamePreview
        {
            public readonly int Index;
            public readonly string Original;
            public readonly string Renamed;

            public RenamePreview(int index, string original, string renamed)
            {
                Index = index;
                Original = original;
                Renamed = renamed;
            }
        }
    }
}
