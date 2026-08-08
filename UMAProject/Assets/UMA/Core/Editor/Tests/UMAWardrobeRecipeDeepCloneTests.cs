using System;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Tests
{
    public class UMAWardrobeRecipeDeepCloneTests
    {
        [Test]
        public void DeepCloneCreatesIndependentManagedRecipeData()
        {
            SlotDataAsset slotAsset =
                ScriptableObject.CreateInstance<SlotDataAsset>();
            OverlayDataAsset overlayAsset =
                ScriptableObject.CreateInstance<OverlayDataAsset>();
            MeshHideAsset meshHideAsset =
                ScriptableObject.CreateInstance<MeshHideAsset>();
            MeshModifier meshModifier =
                ScriptableObject.CreateInstance<MeshModifier>();
            UMAWardrobeRecipe incompatibleRecipe =
                ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            UMAWardrobeRecipe source =
                ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            UMAWardrobeRecipe clone = null;

            try
            {
                slotAsset._oldSlotName = "DeepCloneSlot";
                overlayAsset._oldOverlayName = "DeepCloneOverlay";
                overlayAsset.textureList = Array.Empty<Texture>();

                var slot = new SlotData(slotAsset);
                slot.AddOverlay(new OverlayData(overlayAsset));
                var umaRecipe = new UMAData.UMARecipe
                {
                    slotDataList = new[] { slot },
                    sharedColors = Array.Empty<OverlayColorData>()
                };
                umaRecipe.ClearDna();

                source.name = "DeepCloneSource";
                source.label = "Clone Label";
                source.resourcesOnly = true;
                source.DisplayValue = "Clone Display";
                source.wardrobeSlot = "Chest";
                source.compatibleRaces.Add("HumanMale");
                source.Hides.Add("Torso");
                source.HideTags.Add("Armor");
                source.suppressWardrobeSlots.Add("Underwear");
                source.wardrobeRecipeThumbs.Add(
                    new WardrobeRecipeThumb
                    {
                        race = "HumanMale",
                        filename = "thumb.png"
                    });
                source.activeWardrobeSet.Add(
                    new WardrobeSettings("Chest", "ArmorRecipe"));
                source.OverrideDNA.AddDNA("height", 0.65f);
                source.MeshHideAssets.Add(meshHideAsset);
                source.MeshModifiers.Add(meshModifier);
                source.IncompatibleRecipes.Add(incompatibleRecipe);
                source.Save(umaRecipe);

                clone = source.DeepClone();

                Assert.AreNotSame(source, clone);
                Assert.AreEqual(source.name, clone.name);
                Assert.AreEqual(source.label, clone.label);
                Assert.AreEqual(source.resourcesOnly, clone.resourcesOnly);
                Assert.AreEqual(source.recipeString, clone.recipeString);

                Assert.AreNotSame(
                    source.compatibleRaces,
                    clone.compatibleRaces);
                Assert.AreNotSame(source.Hides, clone.Hides);
                Assert.AreNotSame(source.HideTags, clone.HideTags);
                Assert.AreNotSame(
                    source.suppressWardrobeSlots,
                    clone.suppressWardrobeSlots);
                Assert.AreNotSame(
                    source.wardrobeRecipeThumbs,
                    clone.wardrobeRecipeThumbs);
                Assert.AreNotSame(
                    source.wardrobeRecipeThumbs[0],
                    clone.wardrobeRecipeThumbs[0]);
                Assert.AreNotSame(
                    source.activeWardrobeSet,
                    clone.activeWardrobeSet);
                Assert.AreNotSame(
                    source.activeWardrobeSet[0],
                    clone.activeWardrobeSet[0]);
                Assert.AreNotSame(source.OverrideDNA, clone.OverrideDNA);
                Assert.AreNotSame(
                    source.OverrideDNA.PreloadValues,
                    clone.OverrideDNA.PreloadValues);
                Assert.AreNotSame(
                    source.OverrideDNA.PreloadValues[0],
                    clone.OverrideDNA.PreloadValues[0]);

                Assert.AreNotSame(
                    source.MeshHideAssets,
                    clone.MeshHideAssets);
                Assert.AreSame(
                    source.MeshHideAssets[0],
                    clone.MeshHideAssets[0]);
                Assert.AreNotSame(
                    source.MeshModifiers,
                    clone.MeshModifiers);
                Assert.AreSame(
                    source.MeshModifiers[0],
                    clone.MeshModifiers[0]);
                Assert.AreNotSame(
                    source.IncompatibleRecipes,
                    clone.IncompatibleRecipes);
                Assert.AreSame(
                    source.IncompatibleRecipes[0],
                    clone.IncompatibleRecipes[0]);

                clone.compatibleRaces[0] = "HumanFemale";
                clone.Hides[0] = "CloneTorso";
                clone.wardrobeRecipeThumbs[0].race = "HumanFemale";
                clone.activeWardrobeSet[0].slot = "Waist";
                clone.OverrideDNA.PreloadValues[0].Value = 0.2f;

                Assert.AreEqual("HumanMale", source.compatibleRaces[0]);
                Assert.AreEqual("Torso", source.Hides[0]);
                Assert.AreEqual(
                    "HumanMale",
                    source.wardrobeRecipeThumbs[0].race);
                Assert.AreEqual(
                    "Chest",
                    source.activeWardrobeSet[0].slot);
                Assert.AreEqual(
                    0.65f,
                    source.OverrideDNA.PreloadValues[0].Value,
                    0.0001f);

                UMAPackedRecipeBase.UMAPackRecipe clonePacked =
                    clone.PackedLoad();
                clonePacked.slotsV3[0].id = "ChangedCloneSlot";
                clone.PackedSave(clonePacked);

                Assert.AreEqual(
                    "DeepCloneSlot",
                    source.PackedLoad().slotsV3[0].id);
                Assert.AreEqual(
                    "ChangedCloneSlot",
                    clone.PackedLoad().slotsV3[0].id);
            }
            finally
            {
                if (clone != null)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(incompatibleRecipe);
                UnityEngine.Object.DestroyImmediate(meshModifier);
                UnityEngine.Object.DestroyImmediate(meshHideAsset);
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                UnityEngine.Object.DestroyImmediate(slotAsset);
            }
        }
    }
}
