#if UNITY_EDITOR

using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAAssetIndexerGeneratorTests
    {
        [Test]
        [Category("UMA")]
        [Category("GeneratorBootstrap")]
        public void GeneratorCacheIsNotSerialized()
        {
            FieldInfo generatorField = typeof(UMAAssetIndexer).GetField(
                nameof(UMAAssetIndexer.generator),
                BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(generatorField);
            Assert.IsTrue(
                generatorField.IsNotSerialized,
                "The scene generator cache must never be persisted in the AssetIndexer asset.");
        }

        [Test]
        [Category("UMA")]
        [Category("GeneratorBootstrap")]
        public void GeneratorPropertyRejectsPrefabAssetComponent()
        {
            UMAAssetIndexer indexer = null;
            UMAGenerator resolvedGenerator = null;
            UMAGenerator existingGenerator = null;
            try
            {
                UMASettings settings = UMASettings.GetSettingsFromResources();
                Assert.NotNull(settings);
                Assert.NotNull(settings.generatorPrefab);

                UMAGenerator prefabGenerator =
                    settings.generatorPrefab.GetComponent<UMAGenerator>();
                Assert.NotNull(prefabGenerator);
                Assert.IsTrue(
                    EditorUtility.IsPersistent(prefabGenerator),
                    "The fixture must use the generator component on the prefab asset.");
                Assert.IsFalse(prefabGenerator.gameObject.scene.IsValid());

                existingGenerator =
                    UnityEngine.Object.FindFirstObjectByType<UMAGenerator>(
                        FindObjectsInactive.Include);
                indexer = ScriptableObject.CreateInstance<UMAAssetIndexer>();
                indexer.generator = prefabGenerator;

                resolvedGenerator = indexer.Generator;

                Assert.NotNull(resolvedGenerator);
                Assert.AreNotSame(prefabGenerator, resolvedGenerator);
                Assert.IsTrue(resolvedGenerator.gameObject.scene.IsValid());
                Assert.IsTrue(resolvedGenerator.gameObject.scene.isLoaded);
            }
            finally
            {
                if (resolvedGenerator != null &&
                    resolvedGenerator != existingGenerator)
                {
                    UnityEngine.Object.DestroyImmediate(
                        resolvedGenerator.gameObject);
                }
                if (indexer != null)
                {
                    UnityEngine.Object.DestroyImmediate(indexer);
                }
            }
        }

        [Test]
        [Category("UMA")]
        [Category("GeneratorBootstrap")]
        public void UmaDataDestructionDoesNotCreateIndexerOrGenerator()
        {
            FieldInfo indexerInstanceField =
                typeof(UMAAssetIndexer).GetField(
                    "theIndexer",
                    BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo onDestroy = typeof(UMAData).GetMethod(
                "OnDestroy",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(indexerInstanceField);
            Assert.NotNull(onDestroy);

            object originalIndexer = indexerInstanceField.GetValue(null);
            GameObject dataObject = null;
            UMAData data = null;
            try
            {
                indexerInstanceField.SetValue(null, null);
                dataObject =
                    new GameObject("UMAData teardown bootstrap fixture");
                data = dataObject.AddComponent<UMAData>();

                onDestroy.Invoke(data, null);

                Assert.IsNull(
                    UMAAssetIndexer.bareInstance,
                    "UMAData teardown must not load the indexer or create a generator.");
            }
            finally
            {
                if (data != null)
                {
                    // The teardown method was invoked explicitly above.
                    data.staticCharacter = true;
                }
                if (dataObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(dataObject);
                }
                indexerInstanceField.SetValue(null, originalIndexer);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("GeneratorOverrideEditor")]
        public void GeneratorOverridePickerUsesOnlyCombinersOnCurrentGameObject()
        {
            GameObject overrideObject = null;
            try
            {
                overrideObject =
                    new GameObject("Generator override editor fixture");
                overrideObject.SetActive(false);

                UMAGeneratorOverride generatorOverride =
                    overrideObject.AddComponent<UMAGeneratorOverride>();
                UMADefaultMeshCombiner defaultCombiner =
                    overrideObject.AddComponent<UMADefaultMeshCombiner>();
                UMAIncrementalMeshCombiner incrementalCombiner =
                    overrideObject.AddComponent<UMAIncrementalMeshCombiner>();

                GameObject child = new GameObject("Child combiner");
                child.transform.SetParent(overrideObject.transform, false);
                UMADefaultBoneBakingMeshCombiner childCombiner =
                    child.AddComponent<UMADefaultBoneBakingMeshCombiner>();

                MethodInfo getAttachedCombiners =
                    typeof(UMAGeneratorOverrideEditor).GetMethod(
                        "GetAttachedMeshCombiners",
                        BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(getAttachedCombiners);

                var attachedCombiners =
                    (UMAMeshCombiner[])getAttachedCombiners.Invoke(
                        null,
                        new object[] { generatorOverride });

                CollectionAssert.AreEquivalent(
                    new UMAMeshCombiner[]
                    {
                        defaultCombiner,
                        incrementalCombiner
                    },
                    attachedCombiners);
                CollectionAssert.DoesNotContain(
                    attachedCombiners,
                    childCombiner);
            }
            finally
            {
                if (overrideObject != null)
                {
                    Object.DestroyImmediate(overrideObject);
                }
            }
        }

    }
}

#endif
