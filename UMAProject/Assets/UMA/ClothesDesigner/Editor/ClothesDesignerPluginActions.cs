using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The Clothes Designer's commands under Editors > Plugins in the race, character, slot and
    /// wardrobe recipe inspectors.
    /// </summary>
    internal static class ClothesDesignerPluginActions
    {
        private const string Label = "Clothes Designer";

        [UMAPlugin(typeof(RaceData), Label, Order = 120,
            Tooltip = "Create clothes for this race from a sewing pattern.", ValidateMethod = nameof(CanOpenRace))]
        private static void OpenRace(UnityEditor.Editor editor)
        {
            ClothesDesignerLauncher.CreateClothesOnRace((RaceData)editor.target);
        }

        private static bool CanOpenRace(UnityEditor.Editor editor) =>
            editor.targets.Length == 1 && editor.target is RaceData race && race.baseRaceRecipe != null;

        [UMAPlugin(typeof(DynamicCharacterAvatar), Label, Order = 120,
            Tooltip = "Create clothes for this character's race. A default body of the race is built to fit them on.",
            ValidateMethod = nameof(CanOpenAvatar))]
        private static void OpenAvatar(UnityEditor.Editor editor)
        {
            ClothesDesignerLauncher.CreateClothesOnRace(ClothesDesignerLauncher.GetRace((DynamicCharacterAvatar)editor.target));
        }

        private static bool CanOpenAvatar(UnityEditor.Editor editor) =>
            editor.targets.Length == 1 && editor.target is DynamicCharacterAvatar avatar &&
            !EditorUtility.IsPersistent(avatar) && avatar.gameObject.scene.IsValid() &&
            PrefabStageUtility.GetPrefabStage(avatar.gameObject) == null &&
            ClothesDesignerLauncher.GetRace(avatar) != null;

        [UMAPlugin(typeof(SlotDataAsset), Label, Order = 120,
            Tooltip = "Fit this clothing slot again on a race that wears it.", ValidateMethod = nameof(CanOpenSlot))]
        private static void OpenSlot(UnityEditor.Editor editor)
        {
            var slot = (SlotDataAsset)editor.target;
            RaceData race = ClothesDesignerLauncher.FirstKnownRace(slot.Races);
            if (race != null)
            {
                ClothesDesignerLauncher.FitExistingSlots(race, new[] { slot });
                return;
            }
            // The slot does not say which race wears it: ask.
            ClothesDesignerRacePicker.Show(null, picked => ClothesDesignerLauncher.FitExistingSlots(picked, new[] { slot }));
        }

        private static bool CanOpenSlot(UnityEditor.Editor editor) =>
            editor.targets.Length == 1 && editor.target is SlotDataAsset slot && !UMAMeshData.IsNullOrEmptyMeshData(slot.meshData);

        [UMAPlugin(typeof(UMAWardrobeRecipe), Label, Order = 120,
            Tooltip = "Fit the clothing slots of this wardrobe recipe again on a race that wears it.",
            ValidateMethod = nameof(CanOpenRecipe))]
        private static void OpenRecipe(UnityEditor.Editor editor)
        {
            var recipe = (UMAWardrobeRecipe)editor.target;
            List<SlotDataAsset> slots = GetSlots(recipe);
            if (slots.Count == 0)
            {
                EditorUtility.DisplayDialog(Label, "'" + recipe.name + "' has no slot with a mesh to fit.", "OK");
                return;
            }
            RaceData race = ClothesDesignerLauncher.FirstKnownRace(recipe.compatibleRaces);
            if (race != null)
            {
                ClothesDesignerLauncher.FitExistingSlots(race, slots);
                return;
            }
            ClothesDesignerRacePicker.Show(null, picked => ClothesDesignerLauncher.FitExistingSlots(picked, slots));
        }

        private static bool CanOpenRecipe(UnityEditor.Editor editor) =>
            editor.targets.Length == 1 && editor.target is UMAWardrobeRecipe;

        /// <summary>The slots a wardrobe recipe adds that have a mesh.</summary>
        private static List<SlotDataAsset> GetSlots(UMAWardrobeRecipe recipe)
        {
            var slots = new List<SlotDataAsset>();
            try
            {
                var loaded = new UMAData.UMARecipe();
                recipe.Load(loaded);
                if (loaded.slotDataList != null)
                {
                    foreach (SlotData slot in loaded.slotDataList)
                    {
                        if (slot != null && slot.asset != null && !UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData) && !slots.Contains(slot.asset))
                        {
                            slots.Add(slot.asset);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Clothes Designer] '" + recipe.name + "' could not be read: " + ex.Message);
            }
            return slots;
        }
    }
}
