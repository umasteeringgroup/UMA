#if UNITY_EDITOR

using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAAssetIndexerGeneratorTests
    {
        [Test]
        [Category("UMA")]
        [Category("Toolbar")]
        public void ToolbarVisibilityDefaultsToEnabled()
        {
            UMASettings settings = ScriptableObject.CreateInstance<UMASettings>();
            try
            {
                Assert.IsTrue(settings.showToolbar);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

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
        [Category("RendererLifecycle")]
        public void InternalGeneratorCleanupRemovesEveryDuplicateImmediately()
        {
            GameObject keeperObject = null;
            GameObject duplicateObject = null;
            GameObject userGeneratorObject = null;
            try
            {
                keeperObject = CreateGeneratorFixture("UMAGeneratorInternal");
                duplicateObject = CreateGeneratorFixture("UMAGeneratorInternal");
                userGeneratorObject = CreateGeneratorFixture("User Generator");

                UMAGenerator keeper = keeperObject.GetComponent<UMAGenerator>();
                UMAGenerator duplicate =
                    duplicateObject.GetComponent<UMAGenerator>();
                UMAGenerator userGenerator =
                    userGeneratorObject.GetComponent<UMAGenerator>();

                MethodInfo cleanup = typeof(UMAAssetIndexer).GetMethod(
                    "DestroyInternalGeneratorCandidates",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(cleanup);

                cleanup.Invoke(
                    null,
                    new object[]
                    {
                        new[] { keeper, duplicate, userGenerator },
                        keeper
                    });

                Assert.IsTrue(
                    duplicateObject == null,
                    "A retained internal generator must be destroyed before it can run another Update.");
                Assert.IsFalse(
                    keeperObject == null,
                    "The selected internal generator must remain available.");
                Assert.IsFalse(
                    userGeneratorObject == null,
                    "Explicitly named user generators must not be removed.");
            }
            finally
            {
                if (keeperObject != null)
                {
                    Object.DestroyImmediate(keeperObject);
                }
                if (duplicateObject != null)
                {
                    Object.DestroyImmediate(duplicateObject);
                }
                if (userGeneratorObject != null)
                {
                    Object.DestroyImmediate(userGeneratorObject);
                }
            }
        }

        private static GameObject CreateGeneratorFixture(string objectName)
        {
            var result = new GameObject(objectName);
            result.SetActive(false);
            result.hideFlags = HideFlags.DontSave;
            result.AddComponent<UMAGenerator>();
            return result;
        }

        [Test]
        [Category("UMA")]
        [Category("Package Readiness")]
        public void DictionaryLoadPrefersPackagedAssetOverNullLegacyDuplicate()
        {
            UMAAssetIndexer indexer =
                ScriptableObject.CreateInstance<UMAAssetIndexer>();
            SlotDataAsset packagedSlot =
                ScriptableObject.CreateInstance<SlotDataAsset>();

            try
            {
                AssetItem packagedItem = new AssetItem(
                    typeof(SlotDataAsset),
                    "CapsuleCollider",
                    UMAPathUtility.ResolveInstallAssetPath(
                        "Core/Physics/CapsuleCollider/CapsuleColliderSlot.asset"),
                    packagedSlot);
                AssetItem missingLegacyItem = new AssetItem(
                    typeof(SlotDataAsset),
                    "CapsuleCollider",
                    UMAPathUtility.ResolveUma2ContentPath(
                        "Wearables/Example/AdditionalSlots/" +
                        "CapsuleCollider/U2CapsuleColliderSlot.asset"),
                    null);

                indexer.SerializedItems.Add(packagedItem);
                indexer.SerializedItems.Add(missingLegacyItem);
                indexer.DoInitialDictionaryLoad();

                AssetItem resolvedItem =
                    indexer.GetAssetItem<SlotDataAsset>("CapsuleCollider");
                Assert.That(resolvedItem, Is.SameAs(packagedItem));
                Assert.That(resolvedItem.Item, Is.SameAs(packagedSlot));
            }
            finally
            {
                Object.DestroyImmediate(packagedSlot);
                Object.DestroyImmediate(indexer);
            }
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
                    UnityEngine.Object.FindAnyObjectByType<UMAGenerator>(
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

        [Test]
        [Category("UMA")]
        [Category("RendererLifecycle")]
        public void DcaRendererManagerExpansionIsIdempotent()
        {
            GameObject avatarObject = null;
            SlotDataAsset slotAsset = null;
            UMARendererAsset primaryRenderer = null;
            UMARendererAsset secondaryRenderer = null;
            try
            {
                avatarObject = new GameObject(
                    "DCA renderer manager idempotence fixture");
                var avatar =
                    avatarObject.AddComponent<
                        UMA.CharacterSystem.DynamicCharacterAvatar>();
                avatar.InitializeAvatar();
                avatar.isMeshDirty = true;
                var manager =
                    avatarObject.AddComponent<
                        UMA.CharacterSystem.DCARendererManager>();

                slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
                slotAsset.name = "RendererManagerSlot";
                primaryRenderer =
                    ScriptableObject.CreateInstance<UMARendererAsset>();
                secondaryRenderer =
                    ScriptableObject.CreateInstance<UMARendererAsset>();

                manager.RendererElements.Add(
                    new UMA.CharacterSystem.DCARendererManager.RendererElement
                    {
                        rendererAssets = new List<UMARendererAsset>
                        {
                            primaryRenderer,
                            secondaryRenderer
                        },
                        slotAssets = new List<SlotDataAsset> { slotAsset }
                    });
                var sourceSlot = new SlotData(slotAsset)
                {
                    // A source recipe may already select a non-primary
                    // renderer. It must still expand once, then remain stable.
                    rendererAsset = secondaryRenderer
                };
                avatar.umaRecipe.slotDataList = new[] { sourceSlot };

                MethodInfo characterBegun =
                    typeof(UMA.CharacterSystem.DCARendererManager).GetMethod(
                        "CharacterBegun",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(characterBegun);

                characterBegun.Invoke(manager, new object[] { avatar });
                characterBegun.Invoke(manager, new object[] { avatar });

                SlotData[] slots = avatar.umaRecipe.slotDataList;
                Assert.AreEqual(
                    2,
                    slots.Length,
                    "Repeated renderer-manager callbacks must not append another renderer-specific slot copy.");
                Assert.AreEqual(
                    1,
                    System.Array.FindAll(
                        slots,
                        slot => slot.rendererAsset == primaryRenderer).Length);
                Assert.AreEqual(
                    1,
                    System.Array.FindAll(
                        slots,
                        slot => slot.rendererAsset == secondaryRenderer).Length);
            }
            finally
            {
                if (avatarObject != null)
                {
                    Object.DestroyImmediate(avatarObject);
                }
                if (slotAsset != null)
                {
                    Object.DestroyImmediate(slotAsset);
                }
                if (primaryRenderer != null)
                {
                    Object.DestroyImmediate(primaryRenderer);
                }
                if (secondaryRenderer != null)
                {
                    Object.DestroyImmediate(secondaryRenderer);
                }
            }
        }

        [TestCase(typeof(UMADefaultMeshCombiner))]
        [TestCase(typeof(UMAJobifiedMeshCombiner))]
        [TestCase(typeof(UMADefaultBoneBakingMeshCombiner))]
        [TestCase(typeof(UMABoneBakingMeshCombiner))]
        [Category("UMA")]
        [Category("RendererLifecycle")]
        public void SynchronousCombinerPathsReconcileRestoredRenderers(
            System.Type combinerType)
        {
            GameObject avatarObject = null;
            Mesh orphanMesh = null;
            try
            {
                avatarObject = new GameObject(
                    $"{combinerType.Name} reconciliation fixture");
                UMAData data = avatarObject.AddComponent<UMAData>();
                GameObject globalObject = new GameObject("Global");
                globalObject.transform.SetParent(
                    avatarObject.transform,
                    false);
                data.umaRoot = avatarObject;
                data.skeleton = new UMASkeleton(globalObject.transform);
                data.umaRecipe = new UMAData.UMARecipe
                {
                    slotDataList = System.Array.Empty<SlotData>()
                };
                data.SetRenderers(
                    System.Array.Empty<SkinnedMeshRenderer>());
                data.SetRendererAssets(
                    System.Array.Empty<UMARendererAsset>());

                GameObject orphanObject =
                    new GameObject("Custom Restored Renderer");
                orphanObject.transform.SetParent(
                    avatarObject.transform,
                    false);
                SkinnedMeshRenderer orphanRenderer = orphanObject
                    .AddComponent<SkinnedMeshRenderer>();
                orphanMesh = new Mesh { name = "UMAMesh" };
                orphanRenderer.sharedMesh = orphanMesh;

                Component combiner =
                    avatarObject.AddComponent(combinerType);
                System.Type declaringType =
                    combiner is UMAJobifiedMeshCombiner
                        ? typeof(UMAJobifiedMeshCombiner)
                        : typeof(UMADefaultMeshCombiner);
                MethodInfo ensureSetup = declaringType.GetMethod(
                    "EnsureUMADataSetup",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(ensureSetup);

                ensureSetup.Invoke(combiner, new object[] { data });

                Assert.IsTrue(
                    orphanObject == null,
                    $"{combinerType.Name} must reconcile an untracked generated renderer before allocating or reusing output.");
                Assert.IsTrue(
                    orphanMesh == null,
                    $"{combinerType.Name} must release the orphaned generated mesh as well as its renderer object.");
            }
            finally
            {
                if (orphanMesh != null)
                {
                    Object.DestroyImmediate(orphanMesh);
                }
                if (avatarObject != null)
                {
                    Object.DestroyImmediate(avatarObject);
                }
            }
        }

    }
}

#endif
