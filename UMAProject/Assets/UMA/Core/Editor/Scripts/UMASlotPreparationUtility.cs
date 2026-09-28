using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>All changes use Unity serialization; native/binary slots are supported.</summary>
    public static class UMASlotPreparationUtility
    {
        [MenuItem("UMA/Slot Preparation/Upgrade Selected Slots")]
        private static void UpgradeSelected() => Upgrade(Selection.GetFiltered<SlotDataAsset>(SelectionMode.DeepAssets));

        [MenuItem("UMA/Slot Preparation/Upgrade All Project Slots...")]
        private static void UpgradeAll()
        {
            if (!EditorUtility.DisplayDialog("Prepare UMA slots", "Update derived build data and remove exactly-zero blendshape normal/tangent channels in project slots? Geometry, blendshape names and weights are preserved. Use source control to revert saved upgrades.", "Upgrade", "Cancel")) return;
            var slots = new List<SlotDataAsset>();
            foreach (var guid in AssetDatabase.FindAssets("t:SlotDataAsset", new[] { "Assets" }))
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (asset is SlotDataAsset slot) slots.Add(slot);
            Upgrade(slots);
        }

        public static bool PrepareSlot(SlotDataAsset slot)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData)) return false;
            try
            {
                slot.EnsureBoneWeights();
                UMAMeshPreparation.RegisterOwner(slot);
                slot.meshData.PrepareBuildData();
                EditorUtility.SetDirty(slot);
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException ||
                exception is NullReferenceException || exception is IndexOutOfRangeException)
            {
                Debug.LogWarning($"Slot preparation skipped '{slot.slotName}': {exception.Message}. The existing build path remains available.", slot);
                return false;
            }
        }

        private static void Upgrade(IEnumerable<SlotDataAsset> slots)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Exit Play mode before upgrading slots."); return; }
            var unique = new List<SlotDataAsset>(new HashSet<SlotDataAsset>(slots));
            int updated = 0, skipped = 0;
            try
            {
                for (int i = 0; i < unique.Count; i++)
                {
                    var slot = unique[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Preparing UMA slots", slot != null ? slot.name : "Missing slot", (float)i / unique.Count)) break;
                    string path = AssetDatabase.GetAssetPath(slot);
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsOpenForEdit(path)) { skipped++; continue; }
                    Undo.RegisterCompleteObjectUndo(slot, "Prepare UMA slot");
                    if (PrepareSlot(slot)) { AssetDatabase.SaveAssetIfDirty(slot); updated++; } else skipped++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            Debug.Log($"UMA slot preparation: {updated} upgraded, {skipped} skipped (of {unique.Count}).");
        }
    }

    // Persist preparation on normal saves, including older slots. SaveAssetIfDirty
    // bypasses this hook; stale metadata still converts automatically on first use.
    internal sealed class UMASlotPreparationSaveProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (var path in paths)
            {
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is SlotDataAsset slot)
                        UMASlotPreparationUtility.PrepareSlot(slot);
            }
            return paths;
        }
    }
}
