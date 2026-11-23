using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Mesh modifiers saved before slots had a separate logical name (October 2026) name their slot by
    /// the slot's asset name ("Body_slot"). Slots now answer to their logical name ("Body"), and race-baked
    /// copies only keep that logical source name. These keys must still reach the slot.
    /// </summary>
    public sealed class UMAMeshModifierSlotKeyTests
    {
        private readonly List<Object> objects = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private SlotDataAsset Slot(string assetName, string logicalName, string sourceName = null)
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            objects.Add(slot);
            slot.name = assetName;
            slot._oldSlotName = logicalName;
            if (sourceName != null)
                typeof(SlotDataAsset).GetField("_sourceSlotName", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(slot, sourceName);
            slot.meshData = new UMAMeshData { vertexCount = 1, vertices = new[] { Vector3.zero } };
            return slot;
        }

        private UMAData Data(params SlotDataAsset[] slots)
        {
            var go = new GameObject("Modifier slot key test");
            objects.Add(go);
            go.SetActive(false);
            var data = go.AddComponent<UMAData>();
            var slotData = new SlotData[slots.Length];
            for (int i = 0; i < slots.Length; i++) slotData[i] = new SlotData(slots[i]);
            data.umaRecipe = new UMAData.UMARecipe { slotDataList = slotData };
            return data;
        }

        private static MeshModifier.Modifier Modifier(UMAData data, string slotName)
        {
            var modifier = new MeshModifier.Modifier { SlotName = slotName, Scale = 1f, adjustments = new VertexColorAdjustmentCollection() };
            data.AddMeshModifier(modifier);
            return modifier;
        }

        [Test]
        public void LegacyAssetNameKeyReachesTheLogicalSlot()
        {
            var data = Data(Slot("Body_slot", "Body"));
            var legacy = Modifier(data, "Body_slot");
            data.BuildActiveModifiers();
            CollectionAssert.AreEqual(new[] { legacy }, data.umaRecipe.slotDataList[0].meshModifiers);
        }

        [Test]
        public void LegacyAssetNameKeyReachesARaceBakedCopy()
        {
            var data = Data(Slot("Body_baked_TestRace", "Body_baked_TestRace", "Body"));
            var legacy = Modifier(data, "Body_slot");
            data.BuildActiveModifiers();
            CollectionAssert.AreEqual(new[] { legacy }, data.umaRecipe.slotDataList[0].meshModifiers);
        }

        [Test]
        public void LogicalAndLegacyKeysBothApplyOnce()
        {
            var data = Data(Slot("Body_slot", "Body"));
            var logical = Modifier(data, "Body");
            var legacy = Modifier(data, "Body_slot");
            data.BuildActiveModifiers();
            CollectionAssert.AreEquivalent(new[] { logical, legacy }, data.umaRecipe.slotDataList[0].meshModifiers);
            data.BuildActiveModifiers();
            Assert.That(data.umaRecipe.slotDataList[0].meshModifiers, Has.Count.EqualTo(2), "a rebuild must not duplicate modifiers");
        }

        [Test]
        public void KeysForOtherSlotsDoNotMatch()
        {
            var data = Data(Slot("Body_slot", "Body"), Slot("BodyArmor_slot", "BodyArmor"), Slot("Hair_slot", "Hair_slot"));
            Modifier(data, "BodyArmor_slot");
            var hair = Modifier(data, "Hair_slot");
            data.BuildActiveModifiers();
            Assert.That(data.umaRecipe.slotDataList[0].meshModifiers, Is.Empty);
            Assert.That(data.umaRecipe.slotDataList[1].meshModifiers, Has.Count.EqualTo(1));
            CollectionAssert.AreEqual(new[] { hair }, data.umaRecipe.slotDataList[2].meshModifiers,
                "a slot whose logical name ends in _slot still matches exactly once");
        }
    }
}
