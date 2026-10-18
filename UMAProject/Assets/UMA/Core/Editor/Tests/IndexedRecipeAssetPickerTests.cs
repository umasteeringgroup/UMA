using System;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class IndexedRecipeAssetPickerTests
    {
        private static Type Picker(Type assetType) => typeof(SlotMasterEditor).Assembly
            .GetType("UMA.Editors.IndexedRecipeAssetPicker`1").MakeGenericType(assetType);

        [TestCase(false)]
        [TestCase(true)]
        public void ChoicesOnlyIncludeAssetsResolvableByTheirInternalName(bool overlays)
        {
            var type = overlays ? typeof(OverlayDataAsset) : typeof(SlotDataAsset);
            var index = ScriptableObject.CreateInstance<UMAAssetIndexer>();
            var valid = ScriptableObject.CreateInstance(type);
            var stale = ScriptableObject.CreateInstance(type);
            var unindexed = ScriptableObject.CreateInstance(type);
            try
            {
                valid.name = "Filename_With_Suffix";
                stale.name = "Stale_Filename";
                if (overlays)
                {
                    ((OverlayDataAsset)valid)._oldOverlayName = "InternalOverlay";
                    ((OverlayDataAsset)stale)._oldOverlayName = "OldName";
                    ((OverlayDataAsset)unindexed)._oldOverlayName = "UnindexedOverlay";
                }
                else
                {
                    ((SlotDataAsset)valid)._oldSlotName = "InternalSlot";
                    ((SlotDataAsset)stale)._oldSlotName = "OldName";
                    ((SlotDataAsset)unindexed)._oldSlotName = "UnindexedSlot";
                }
                var name = overlays ? "InternalOverlay" : "InternalSlot";
                index.SerializedItems.Add(new AssetItem(type, name, "", valid));
                index.SerializedItems.Add(new AssetItem(type, "OldName", "", stale));
                index.DoInitialDictionaryLoad();
                if (overlays) ((OverlayDataAsset)stale)._oldOverlayName = "RenamedOverlay";
                else ((SlotDataAsset)stale)._oldSlotName = "RenamedSlot";
                var choices = (Array)Picker(type).GetMethod("GetChoices", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { index });
                CollectionAssert.AreEqual(new[] { valid }, choices);
                Assert.That(index.GetAssetItem(type, name).Item, Is.SameAs(valid));
            }
            finally
            {
                Object.DestroyImmediate(index);
                Object.DestroyImmediate(valid);
                Object.DestroyImmediate(stale);
                Object.DestroyImmediate(unindexed);
            }
        }

        [Test]
        public void RecipePackingUsesInternalNamesWhenAssetNamesDiffer()
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                slot.name = "Body_slot";
                slot._oldSlotName = "Body";
                overlay.name = "Skin_overlay";
                overlay._oldOverlayName = "Skin";
                var data = new SlotData(slot);
                data.AddOverlay(new OverlayData(overlay));
                var recipe = new UMAData.UMARecipe { slotDataList = new[] { data } };
                var packed = UMAPackedRecipeBase.PackRecipeV3(recipe);
                Assert.That(packed.slotsV3[0].id, Is.EqualTo("Body"));
                Assert.That(packed.slotsV3[0].overlays[0].id, Is.EqualTo("Skin"));
            }
            finally { Object.DestroyImmediate(slot); Object.DestroyImmediate(overlay); }
        }
    }
}
