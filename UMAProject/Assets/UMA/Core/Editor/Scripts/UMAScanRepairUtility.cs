using System;
using System.IO;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>Targeted asset repairs shared by the Welcome scan controls.</summary>
    public static class UMAScanRepairUtility
    {
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public static bool HasValidManualBounds(RaceData race) => race != null && race.useManualRendererBounds &&
            Finite(race.manualRendererBounds) && Finite(race.manualRendererBoundsCenter) &&
            race.manualRendererBounds.x > 0 && race.manualRendererBounds.y > 0 && race.manualRendererBounds.z > 0;

        public static bool NeedsBoundsFix(RaceData race) => race != null && race.useManualRendererBounds && !HasValidManualBounds(race);

        public static bool NeedsBlendModeFix(OverlayDataAsset overlay) => overlay != null && overlay.textureList != null &&
            (overlay.overlayBlend == null || overlay.overlayBlend.Length != overlay.textureList.Length);

        public static OverlayDataAsset.OverlayBlend[] ResizeBlendModes(OverlayDataAsset.OverlayBlend[] existing, int textureCount)
        {
            if (textureCount < 0) throw new ArgumentOutOfRangeException(nameof(textureCount));
            var modes = new OverlayDataAsset.OverlayBlend[textureCount];
            for (int i = 0; i < modes.Length; i++) modes[i] = OverlayDataAsset.OverlayBlend.Normal;
            if (existing != null) Array.Copy(existing, modes, Math.Min(existing.Length, modes.Length));
            return modes;
        }

        public static bool FixBoundsAndSave(RaceData target, RaceData source)
        {
            if (!HasValidManualBounds(source)) throw new InvalidOperationException("Choose a race with enabled, finite, positive manual bounds and a finite center.");
            if (!NeedsBoundsFix(target)) return false;
            Vector3 extents = target.manualRendererBounds, center = target.manualRendererBoundsCenter;
            SaveRepair(target, "Fix race manual renderer bounds", () =>
            {
                target.manualRendererBounds = source.manualRendererBounds;
                target.manualRendererBoundsCenter = source.manualRendererBoundsCenter;
            }, () => { target.manualRendererBounds = extents; target.manualRendererBoundsCenter = center; });
            return true;
        }

        public static bool FixBlendModesAndSave(OverlayDataAsset overlay)
        {
            if (!NeedsBlendModeFix(overlay)) return false;
            var previous = overlay.overlayBlend;
            var resized = ResizeBlendModes(previous, overlay.textureList.Length);
            SaveRepair(overlay, "Fix overlay blend-mode count", () => overlay.overlayBlend = resized, () => overlay.overlayBlend = previous);
            return true;
        }

        public static bool IsEmptySlotEntry(UMAPackedRecipeBase.PackedSlotDataV3 slot) =>
            slot == null || string.IsNullOrWhiteSpace(slot.id);

        // Legacy entries are projected only for validation; never save these partial records.
        public static UMAPackedRecipeBase.PackedSlotDataV3[] GetRecipeSlotsForScan(UMAPackedRecipeBase.UMAPackRecipe packed)
        {
            if (packed == null) return null;
            if (packed.version == 3) return packed.slotsV3;
            if (packed.version == 2)
                return packed.slotsV2 == null ? null : Array.ConvertAll(packed.slotsV2, slot => slot == null ? null :
                    new UMAPackedRecipeBase.PackedSlotDataV3
                    {
                        id = slot.id, copyIdx = slot.copyIdx,
                        overlays = slot.overlays == null ? null : Array.ConvertAll(slot.overlays, overlay => overlay == null ? null :
                            new UMAPackedRecipeBase.PackedOverlayDataV3 { id = overlay.id })
                    });
            return packed.packedSlotDataList == null ? null : Array.ConvertAll(packed.packedSlotDataList, slot => slot == null ? null :
                new UMAPackedRecipeBase.PackedSlotDataV3
                {
                    id = slot.slotID, copyIdx = slot.copyOverlayIndex,
                    overlays = slot.OverlayDataList == null ? null : Array.ConvertAll(slot.OverlayDataList, overlay => overlay == null ? null :
                        new UMAPackedRecipeBase.PackedOverlayDataV3 { id = overlay.overlayID })
                });
        }

        public static bool FixMissingRecipeRaceAndSave(UMATextRecipe recipe, string expectedRace,
            RaceData replacement, Func<string, bool> raceExists)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (raceExists == null) throw new ArgumentNullException(nameof(raceExists));
            if (replacement != null && (string.IsNullOrWhiteSpace(replacement.raceName) || !raceExists(replacement.raceName)))
                throw new InvalidOperationException("Choose a replacement race that is present in the UMA library.");
            string previous = recipe.recipeString;
            if (string.IsNullOrWhiteSpace(previous)) throw new InvalidOperationException("Recipe '" + recipe.name + "' no longer has readable recipe data. Run the scan again.");
            var packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(previous);
            if (packed == null) throw new InvalidOperationException("Recipe '" + recipe.name + "' could not be read.");
            if (string.IsNullOrWhiteSpace(packed.race) || raceExists(packed.race)) return false;
            if (!string.Equals(packed.race, expectedRace, StringComparison.Ordinal))
                throw new InvalidOperationException("Recipe '" + recipe.name + "' has a different missing race now. Run the scan again.");
            string repaired = ReplaceRecipeRace(previous, replacement != null ? replacement.raceName : "");
            SaveRepair(recipe, "Fix missing character recipe race", () => recipe.recipeString = repaired,
                () => recipe.recipeString = previous);
            return true;
        }

        [Serializable]
        private sealed class JsonStringValue { public string value; }

        private static string ReplaceRecipeRace(string json, string race)
        {
            int depth = 0, valueStart = -1, valueEnd = -1;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == '"')
                {
                    int start = i;
                    i = JsonStringEnd(json, i);
                    if (depth != 1) continue;
                    int colon = i + 1;
                    while (colon < json.Length && char.IsWhiteSpace(json[colon])) colon++;
                    if (colon >= json.Length || json[colon] != ':') continue;
                    string key = JsonUtility.FromJson<JsonStringValue>("{\"value\":" + json.Substring(start, i - start + 1) + "}").value;
                    if (key != "race") continue;
                    if (valueStart >= 0) throw new InvalidOperationException("Recipe contains duplicate race fields. Resolve them before repairing the race.");
                    valueStart = colon + 1;
                    while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;
                    if (valueStart >= json.Length || json[valueStart] != '"')
                        throw new InvalidOperationException("Recipe race must be a JSON string.");
                    valueEnd = JsonStringEnd(json, valueStart);
                    i = valueEnd;
                }
            }
            if (valueStart < 0) throw new InvalidOperationException("No serialized race reference was found. Run the scan again.");
            string encoded = JsonUtility.ToJson(new JsonStringValue { value = race });
            encoded = encoded.Substring(encoded.IndexOf(':') + 1);
            encoded = encoded.Substring(0, encoded.Length - 1);
            return json.Substring(0, valueStart) + encoded + json.Substring(valueEnd + 1);
        }

        private static int JsonStringEnd(string json, int start)
        {
            for (int i = start + 1; i < json.Length; i++)
            {
                if (json[i] == '\\') i++;
                else if (json[i] == '"') return i;
            }
            throw new InvalidOperationException("Recipe contains an unterminated JSON string.");
        }

        public static bool IsRaceIndependentRecipe(UMAPackedRecipeBase.UMAPackRecipe packed,
            Func<string, SlotDataAsset> resolveSlot)
        {
            if (packed == null) return false;
            bool hasSlot = false;
            bool AcceptSlot(string name, bool placeholder = false)
            {
                if (string.IsNullOrWhiteSpace(name)) return true;
                hasSlot = true;
                if (placeholder) return true;
                var asset = resolveSlot(name);
                return asset != null && (asset.isUtilitySlot || asset.isClippingPlane || asset.isWildCardSlot);
            }

            var slots = GetRecipeSlotsForScan(packed);
            if (slots != null)
                foreach (var slot in slots)
                    if (slot != null && !AcceptSlot(slot.id, slot.isPlaceholderSlot)) return false;
            return hasSlot;
        }

        public static int FixEmptySlotsAndSave(UMAWardrobeRecipe recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            string previous = recipe.recipeString;
            if (string.IsNullOrWhiteSpace(previous)) return 0;
            var packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(previous);
            var slots = packed?.slotsV3;
            if (slots == null) return 0;
            int removed = 0;
            var map = new int[slots.Length];
            var retained = new List<UMAPackedRecipeBase.PackedSlotDataV3>();
            for (int i = 0; i < slots.Length; i++)
            {
                map[i] = -1;
                if (IsEmptySlotEntry(slots[i])) { removed++; continue; }
                map[i] = retained.Count;
                retained.Add(slots[i]);
            }
            if (removed == 0) return 0;
            if (packed.version != 3)
                throw new InvalidOperationException("Recipe '" + recipe.name + "' must use packed recipe version 3 before removing empty slots.");

            var copies = new int[retained.Count];
            var overlays = new UMAPackedRecipeBase.PackedOverlayDataV3[retained.Count][];
            for (int i = 0; i < retained.Count; i++)
            {
                int source = retained[i].copyIdx;
                copies[i] = -1;
                overlays[i] = retained[i].overlays;
                if (source == -1) continue;
                if (source < 0 || source >= slots.Length)
                    throw new InvalidOperationException("Recipe '" + recipe.name + "' has an invalid shared-overlay slot index. Repair that reference first.");
                if (map[source] >= 0) { copies[i] = map[source]; continue; }

                var visited = new HashSet<int>();
                while (true)
                {
                    if (source < 0 || source >= slots.Length || slots[source] == null || !visited.Add(source))
                        throw new InvalidOperationException("Recipe '" + recipe.name + "' shares overlays with an unreadable or circular empty slot. Repair that reference first.");
                    var donor = slots[source];
                    if (donor.copyIdx == -1) { overlays[i] = donor.overlays; break; }
                    source = donor.copyIdx;
                }
            }
            for (int i = 0; i < retained.Count; i++)
            {
                retained[i].copyIdx = copies[i];
                retained[i].overlays = overlays[i];
            }
            packed.slotsV3 = retained.ToArray();
            string repaired = JsonUtility.ToJson(packed);
            SaveRepair(recipe, "Remove empty wardrobe recipe slots", () => recipe.recipeString = repaired,
                () => recipe.recipeString = previous);
            return removed;
        }

        private static void SaveRepair(UnityEngine.Object asset, string label, Action apply, Action rollback)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsOpenForEdit(asset) ||
                (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new InvalidOperationException("Cannot save '" + asset.name + "' at '" + path + "'. Make the asset writable first.");
            bool wasDirty = EditorUtility.IsDirty(asset);
            Undo.RegisterCompleteObjectUndo(asset, label);
            try
            {
                apply(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
                if (EditorUtility.IsDirty(asset)) throw new IOException("Unity did not finish saving the asset.");
            }
            catch (Exception error)
            {
                rollback(); EditorUtility.SetDirty(asset);
                try { AssetDatabase.SaveAssetIfDirty(asset); }
                catch (Exception restore) { Debug.LogError("Could not restore '" + path + "': " + restore.Message); }
                if (wasDirty) EditorUtility.SetDirty(asset);
                throw new IOException("Repair could not be saved to '" + path + "': " + error.Message, error);
            }
        }
    }
}
