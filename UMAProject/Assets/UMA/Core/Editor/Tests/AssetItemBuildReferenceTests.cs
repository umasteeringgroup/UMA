using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class AssetItemBuildReferenceTests
    {
        private string assetFolder;
        private SlotDataAsset slot;
        private UMAAssetIndexer indexer;

        [SetUp]
        public void SetUp()
        {
            assetFolder = "Assets/__UMAAssetItemBuildTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", assetFolder.Substring("Assets/".Length));
            slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.name = "BuildReferenceSlot";
            AssetDatabase.CreateAsset(slot, assetFolder + "/Slot.asset");
            indexer = ScriptableObject.CreateInstance<UMAAssetIndexer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(indexer);
            AssetDatabase.DeleteAsset(assetFolder);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BuildReferencesSkipNullAndEmptyRecordsAndRetainValidAssets(bool addReferences)
        {
            var valid = CreateSlotItem();
            var addressable = CreateSlotItem();
            addressable.IsAddressable = true;
            addressable._SerializedItem = slot;
            var ignored = CreateSlotItem();
            ignored.Ignore = true;
            ignored._SerializedItem = slot;
            var empty = new AssetItem(null, null, null, null);
            indexer.SerializedItems.Add(empty);
            indexer.SerializedItems.Add(null);
            indexer.SerializedItems.Add(valid);
            indexer.SerializedItems.Add(addressable);
            indexer.SerializedItems.Add(ignored);

            if (addReferences) indexer.AddReferences();
            else indexer.UpdateReferences();

            Assert.That(valid._SerializedItem, Is.SameAs(slot));
            Assert.That(addressable._SerializedItem, Is.Null);
            Assert.That(ignored._SerializedItem, Is.Null);
            Assert.That(indexer.SerializedItems.Count, Is.EqualTo(5), "Preparing a build must preserve index positions.");
            Assert.That(indexer.SerializedItems[2], Is.SameAs(valid));
        }

        [TestCase(null)]
        [TestCase("")]
        public void MissingTypeNameCanRecoverFromStoredPath(string typeName)
        {
            var item = CreateSlotItem();
            item._BaseTypeName = typeName;
            ClearCachedType(item);
            indexer.SerializedItems.Add(item);

            indexer.UpdateReferences();

            Assert.That(item._SerializedItem, Is.SameAs(slot));
            Assert.That(item._Type, Is.EqualTo(typeof(SlotDataAsset)));
        }

        [Test]
        public void PreprocessBuildHandlesDeserializedIndex()
        {
            indexer.SerializedItems.Add(null);
            indexer.SerializedItems.Add(new AssetItem(null, null, null, null));
            indexer.SerializedItems.Add(CreateSlotItem());
            var reloaded = Object.Instantiate(indexer);
            var singleton = typeof(UMAAssetIndexer).GetField("theIndexer", BindingFlags.Static | BindingFlags.NonPublic);
            var previousIndexer = singleton.GetValue(null);
            try
            {
                singleton.SetValue(null, reloaded);

                new BuildPreprocessor().OnPreprocessBuild(null);

                Assert.That(reloaded.SerializedItems[2]._SerializedItem, Is.SameAs(slot));
                Assert.That(reloaded.SerializedItems[2]._Type, Is.EqualTo(typeof(SlotDataAsset)));
            }
            finally
            {
                singleton.SetValue(null, previousIndexer);
                Object.DestroyImmediate(reloaded);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        public void MissingTypeNameCanRecoverFromSerializedObject(string typeName)
        {
            var item = CreateSlotItem();
            item._SerializedItem = slot;
            item._BaseTypeName = typeName;
            ClearCachedType(item);

            Assert.That(item._Type, Is.EqualTo(typeof(SlotDataAsset)));
        }

        [Test]
        public void StoredLookupTypeIsPreservedWhenObjectHasDerivedType()
        {
            var controller = new AnimatorOverrideController();
            string typeName = nameof(RuntimeAnimatorController);
            bool hadLookup = UMAAssetIndexer.TypeFromString.TryGetValue(typeName, out var previousType);
            try
            {
                UMAAssetIndexer.TypeFromString[typeName] = typeof(RuntimeAnimatorController);
                var item = new AssetItem(typeof(RuntimeAnimatorController), "Controller", "", controller);
                ClearCachedType(item);
                Assert.That(item._Type, Is.EqualTo(typeof(RuntimeAnimatorController)));
            }
            finally
            {
                if (hadLookup) UMAAssetIndexer.TypeFromString[typeName] = previousType;
                else UMAAssetIndexer.TypeFromString.Remove(typeName);
                Object.DestroyImmediate(controller);
            }
        }

        private AssetItem CreateSlotItem()
        {
            return new AssetItem(typeof(SlotDataAsset), slot.name, AssetDatabase.GetAssetPath(slot), null);
        }

        private static void ClearCachedType(AssetItem item)
        {
            typeof(AssetItem).GetField("_TheType", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(item, null);
        }
    }
}
