#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Releasing a slot from the Global Library also drops the slot's race-baked copies, so a race with
    /// prebaked blendshapes does not keep wearing the slot's old mesh after the slot is saved.
    /// </summary>
    /// <remarks>
    /// The tests add items to the library in memory only and take them out again. They never save the
    /// library and check that they did not mark it as changed.
    /// </remarks>
    public sealed class UMABakedCopyReleaseTests
    {
        private const string SourceName = "UMABakedCopyReleaseTests_Source";

        private readonly List<Object> objects = new List<Object>();
        private UMAAssetIndexer indexer;
        private int itemCount;
        private int dirtyCount;

        [SetUp]
        public void SetUp()
        {
            indexer = UMAAssetIndexer.Instance;
            Assume.That(indexer, Is.Not.Null, "No Global Library.");
            itemCount = indexer.SerializedItems.Count;
            dirtyCount = EditorUtility.GetDirtyCount(indexer);
        }

        [TearDown]
        public void TearDown()
        {
            if (indexer != null)
            {
                // Whatever a failed test left behind.
                Dictionary<string, AssetItem> slots = indexer.GetAssetDictionary(typeof(SlotDataAsset));
                var mine = new List<string>();
                foreach (string name in slots.Keys)
                {
                    if (name.StartsWith(SourceName, System.StringComparison.Ordinal)) mine.Add(name);
                }
                MethodInfo remove = typeof(UMAAssetIndexer).GetMethod("RemoveInMemoryItem", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (string name in mine)
                {
                    AssetItem item = slots[name];
                    slots.Remove(name);
                    remove.Invoke(indexer, new object[] { item });
                }
            }
            foreach (Object o in objects)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            objects.Clear();
        }

        private SlotDataAsset NewSlot(string slotName, string bakedFrom = null)
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.name = slotName;
            typeof(SlotDataAsset).GetMethod("SetSlotName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(slot, new object[] { slotName });
            if (bakedFrom != null)
            {
                slot.isBaked = true;
                typeof(SlotDataAsset).GetField("_sourceSlotName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(slot, bakedFrom);
            }
            objects.Add(slot);
            return slot;
        }

        private void AssertLibraryAsBefore()
        {
            Assert.That(indexer.SerializedItems.Count, Is.EqualTo(itemCount), "The item list is as long as it was.");
            for (int i = 0; i < indexer.SerializedItems.Count; i++)
            {
                AssetItem item = indexer.SerializedItems[i];
                if (item != null && item.Index != i)
                {
                    Assert.Fail("Item " + i + " (" + item._Name + ") says it is at " + item.Index + ".");
                }
            }
            Assert.That(EditorUtility.GetDirtyCount(indexer), Is.EqualTo(dirtyCount), "The library was not marked as changed.");
        }

        [Test]
        public void ReleasingASlotDropsItsBakedCopiesAndNoOthers()
        {
            SlotDataAsset source = NewSlot(SourceName);
            SlotDataAsset male = NewSlot(SourceName + "_baked_RaceA", SourceName);
            SlotDataAsset female = NewSlot(SourceName + "_baked_RaceB", SourceName);
            SlotDataAsset other = NewSlot(SourceName + "Other_baked_RaceA", SourceName + "Other");
            indexer.ProcessNewItem(male, false, false);
            indexer.ProcessNewItem(other, false, false);
            indexer.ProcessNewItem(female, false, false);
            Assert.That(indexer.HasAsset<SlotDataAsset>(male.slotName), Is.True);

            indexer.ReleaseReference(source);

            Assert.That(indexer.HasAsset<SlotDataAsset>(male.slotName), Is.False);
            Assert.That(indexer.HasAsset<SlotDataAsset>(female.slotName), Is.False);
            Assert.That(indexer.HasAsset<SlotDataAsset>(other.slotName), Is.True, "Another slot's baked copy stays.");
            Assert.That(male != null && female != null, Is.True, "The copies are not destroyed: built characters still wear them.");
            Assert.That(indexer.DropBakedCopies(NewSlot(SourceName + "Other")), Is.EqualTo(1));
            AssertLibraryAsBefore();
        }

        [Test]
        public void ABakedCopyWhoseReferenceWasReleasedIsStillDropped()
        {
            SlotDataAsset source = NewSlot(SourceName);
            SlotDataAsset copy = NewSlot(SourceName + "_baked_RaceA", SourceName);
            indexer.ProcessNewItem(copy, false, false);
            indexer.GetAssetDictionary(typeof(SlotDataAsset))[copy.slotName].FreeReference();

            Assert.That(indexer.DropBakedCopies(source), Is.EqualTo(1));
            Assert.That(indexer.HasAsset<SlotDataAsset>(copy.slotName), Is.False);
            AssertLibraryAsBefore();
        }

        [Test]
        public void ASlotWithoutBakedCopiesChangesNothing()
        {
            Assert.That(indexer.DropBakedCopies(NewSlot(SourceName)), Is.EqualTo(0));
            Assert.That(indexer.DropBakedCopies(null), Is.EqualTo(0));
            AssertLibraryAsBefore();
        }
    }
}
#endif
