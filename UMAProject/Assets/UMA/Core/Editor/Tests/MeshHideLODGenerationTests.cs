#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class MeshHideLODGenerationTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> objects = new List<Object>();
        private SlotDataAsset slot;
        private MeshHideAsset mask;
        private Dictionary<string, AssetItem> slotIndex;

        [SetUp]
        public void SetUp()
        {
            slot = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
            slot.name = "MeshHideLODTest_" + Guid.NewGuid().ToString("N");
            slot.meshData = new UMAMeshData
            {
                vertexCount = 7,
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one, Vector3.forward, Vector3.left, Vector3.down },
                subMeshCount = 1,
                submeshes = new[] { CreateTriangles() }
            };
            // This entry exists only in memory for this test; never dirty or save the production index.
            slotIndex = UMAAssetIndexer.Instance.GetAssetDictionary(typeof(SlotDataAsset));
            slotIndex.Add(slot.slotName, new AssetItem(typeof(SlotDataAsset), slot.slotName, string.Empty, slot));
            mask = Track(ScriptableObject.CreateInstance<MeshHideAsset>());
            mask.asset = slot;
            mask.SaveSelectionForLOD(new[] { new BitArray(new[] { true, false }) }, 0);
        }

        [TearDown]
        public void TearDown()
        {
            if (slot != null)
            {
                slotIndex?.Remove(slot.slotName);
                foreach (var submesh in slot.meshData.submeshes) submesh.DisposeNativeTriangles(true);
            }
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Undo.ClearUndo(objects[i]);
                    Object.DestroyImmediate(objects[i]);
                }
            }
            objects.Clear();
        }

        private static SubMeshTriangles CreateTriangles() => new SubMeshTriangles(new[]
        {
            0, 1, 2, 3, 4, 5,                 // LOD 0: only the first face is hidden.
            0, 1, 2, 0, 1, 3, 0, 3, 4, 3, 4, 5, // LOD 1: 3, 2, 1, 0 hidden vertices.
            3, 4, 6                           // LOD 2: no LOD-0 hidden vertices, but overlaps LOD-1 expansion.
        }) { lodRanges = new List<UMALodRange> { new UMALodRange(0, 6), new UMALodRange(6, 12), new UMALodRange(18, 3) } };

        [Test]
        public void AutomaticGenerationDefaultsToStrict()
        {
            Assert.That(mask.LODGenerationStrategy, Is.EqualTo(MeshHideAsset.TriangleHideStrategy.Strict));
            AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, false, false, false);
        }

        [TestCase(MeshHideAsset.TriangleHideStrategy.Strict, 1)]
        [TestCase(MeshHideAsset.TriangleHideStrategy.Weighted, 2)]
        [TestCase(MeshHideAsset.TriangleHideStrategy.Conservative, 3)]
        public void AutomaticAndExplicitGenerationHonorSelectedMode(MeshHideAsset.TriangleHideStrategy mode, int hiddenCount)
        {
            mask.LODGenerationStrategy = mode;
            var automatic = MeshHideAsset.GenerateMask(new List<MeshHideAsset> { mask }, 1)[0];
            for (int i = 0; i < automatic.Length; i++) Assert.That(automatic[i], Is.EqualTo(i < hiddenCount));
            mask.CopyLODMask(0, 1, true, mode);
            CollectionAssert.AreEqual(automatic, mask.GetTriangleFlagsForLOD(1)[0]);
            Assert.That(mask.LODGenerationStrategy, Is.EqualTo(mode));
        }

        [Test]
        public void LowestLODAlwaysUsesBaseMaskRatherThanPreviousLOD()
        {
            mask.LODGenerationStrategy = MeshHideAsset.TriangleHideStrategy.Conservative;
            AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, true, true, false);
            mask.SaveSelectionForLOD(new[] { new BitArray(4, true) }, 1);
            AssertMask(mask.GetTriangleFlagsForLOD(2)[0], false);
        }

        [Test]
        public void ModeAndBaseEditsInvalidatePreviewButPreserveSavedOverrides()
        {
            mask.LODGenerationStrategy = MeshHideAsset.TriangleHideStrategy.Conservative;
            mask.GetTriangleFlagsForLOD(1);
            mask.SaveSelectionForLOD(new[] { new BitArray(new[] { true }) }, 2);
            mask.LODGenerationStrategy = MeshHideAsset.TriangleHideStrategy.Strict;
            AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, false, false, false);
            mask.SaveSelectionForLOD(new[] { new BitArray(2) }, 0);
            AssertMask(mask.GetTriangleFlagsForLOD(1)[0], false, false, false, false);
            AssertMask(mask.GetTriangleFlagsForLOD(2)[0], true);
        }

        [Test]
        public void PreviewIsNotStoredOrSerializedAndStrategyPersists()
        {
            mask.LODGenerationStrategy = MeshHideAsset.TriangleHideStrategy.Weighted;
            mask.GetTriangleFlagsForLOD(1);
            Assert.That(mask.HasStoredLODMask(1), Is.False);
            var restored = Track(ScriptableObject.CreateInstance<MeshHideAsset>());
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(mask), restored);
            Assert.That(restored.HasStoredLODMask(1), Is.False);
            Assert.That(restored.LODGenerationStrategy, Is.EqualTo(MeshHideAsset.TriangleHideStrategy.Weighted));
            AssertMask(restored.GetTriangleFlagsForLOD(1)[0], true, true, false, false);
        }

        [Test]
        public void StrictCopyDoesNotMergeAnEarlierConservativePreview()
        {
            mask.LODGenerationStrategy = MeshHideAsset.TriangleHideStrategy.Conservative;
            mask.GetTriangleFlagsForLOD(1);
            Assert.That(mask.HasStoredLODMask(1), Is.False, "The bulk Missing policy must still process this LOD.");
            mask.CopyLODMask(0, 1, false, MeshHideAsset.TriangleHideStrategy.Strict);
            AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, false, false, false);
            Assert.That(mask.HasStoredLODMask(1), Is.True);
        }

        [Test]
        public void CopyWithoutModeUsesTheAssetsSelectedMode()
        {
            mask.LODGenerationStrategy = MeshHideAsset.TriangleHideStrategy.Weighted;
            mask.CopyLODMask(0, 1, true);
            AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, true, false, false);
        }

        [Test]
        public void GenerationHandlesEverySubmesh()
        {
            slot.meshData.subMeshCount = 2;
            slot.meshData.submeshes = new[] { slot.meshData.submeshes[0], CreateTriangles() };
            mask.SaveSelectionForLOD(new[] { new BitArray(new[] { true, false }), new BitArray(new[] { false, true }) }, 0);
            mask.CopyLODMask(0, 1, true, MeshHideAsset.TriangleHideStrategy.Strict);
            AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, false, false, false);
            AssertMask(mask.GetTriangleFlagsForLOD(1)[1], false, false, false, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RecalculateButtonRefreshesPreviewWithUndoAndSavesOnlyOnRequest(bool useCollection)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = Track(new GameObject("LOD recalculation test"));
                go.SetActive(false);
                SceneManager.MoveGameObjectToScene(go, preview);
                var avatar = go.AddComponent<DynamicCharacterAvatar>();
                avatar.editorTimeGeneration = false;
                var slotData = new SlotData(slot);
                avatar.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { slotData } };
                var stage = Track(ScriptableObject.CreateInstance<FaceEditorStage>());
                stage.thisDCA = avatar;
                stage.BakedMesh = Track(new Mesh { vertices = slot.meshData.vertices, triangles = slot.meshData.submeshes[0].getManagedTriangles(1) });
                if (useCollection)
                {
                    stage.CurrentHideCollection = Track(ScriptableObject.CreateInstance<MeshHideAssetCollection>());
                    stage.CurrentHideCollection.AddOrUpdate(mask);
                }
                else stage.CurrentHideAsset = mask;
                SetStageField(stage, "selectedLOD", 1);
                SetStageField(stage, "lodGenerationStrategy", MeshHideAsset.TriangleHideStrategy.Strict);
                mask.SaveSelectionForLOD(new[] { new BitArray(4, true) }, 1);
                InvokeStage(stage, "RefreshSlotSelectionEntries");
                InvokeStage(stage, "LoadSelections");
                InvokeStage(stage, "CaptureSavedSelectionSnapshot");
                Assert.That(StageSelection(stage).Count, Is.EqualTo(4));

                Undo.IncrementCurrentGroup();
                InvokeStage(stage, "RecalculateLOD");
                Assert.That(StageSelection(stage).Count, Is.EqualTo(1));
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.True);
                AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, true, true, true);
                Undo.PerformUndo();
                InvokeStage(stage, "OnUndoRedoSelection");
                Assert.That(StageSelection(stage).Count, Is.EqualTo(4));

                SetStageField(stage, "lodGenerationStrategy", MeshHideAsset.TriangleHideStrategy.Weighted);
                InvokeStage(stage, "RecalculateLOD");
                Assert.That(StageSelection(stage).Count, Is.EqualTo(2));
                InvokeStage(stage, "WriteSelectionToAsset", mask, slotData);
                Assert.That(mask.LODGenerationStrategy, Is.EqualTo(MeshHideAsset.TriangleHideStrategy.Weighted));
                AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, true, false, false);
                AssertMask(mask.triangleFlags[0], true, false);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        [Test]
        public void SwitchingDraftsRetainsIndependentSelectionsAndClearsWithoutSaving()
        {
            WithDraftStage((stage, slotData) =>
            {
                string before = EditorJsonUtility.ToJson(mask);
                SetStageSelection(stage, slot.slotName, 1);
                SwitchDraftLevel(stage, 1);
                SetStageSelection(stage, slot.slotName); // An empty draft must not reload the stored hides.
                SwitchDraftLevel(stage, 2);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.True, "Closing must see edits on other levels.");
                SwitchDraftLevel(stage, 0);
                AssertStageTriangles(stage, 1);
                SwitchDraftLevel(stage, 1);
                AssertStageTriangles(stage);
                Assert.That(EditorJsonUtility.ToJson(mask), Is.EqualTo(before), "Switching must not save or modify the asset.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SaveIncludesOffscreenDraftsAndClearsWhilePreservingOtherLODs(bool useCollection)
        {
            WithDraftStage((stage, slotData) =>
            {
                if (useCollection)
                {
                    stage.CurrentHideCollection = Track(ScriptableObject.CreateInstance<MeshHideAssetCollection>());
                    stage.CurrentHideCollection.AddOrUpdate(mask);
                    stage.CurrentHideAsset = null;
                }
                SetStageSelection(stage, slot.slotName, 1);
                SwitchDraftLevel(stage, 1);
                SetStageSelection(stage, slot.slotName);
                SwitchDraftLevel(stage, 0);
                InvokeStage(stage, "SaveSelections", false);
                Assert.That(stage.hasSaved, Is.True);
                AssertMask(mask.triangleFlags[0], false, true);
                AssertMask(mask.GetTriangleFlagsForLOD(1)[0], false, false, false, false);
                AssertMask(mask.GetTriangleFlagsForLOD(2)[0], true);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.False);
                SwitchDraftLevel(stage, 1);
                AssertStageTriangles(stage);
            });
        }

        [Test]
        public void SavingSuppressedSlotsDoesNotEraseTheirStoredMasks()
        {
            WithDraftStage((stage, slotData) =>
            {
                slotData.Suppressed = true;
                SetStageSelection(stage, slot.slotName); // Visibility rebuilds prune hidden faces from the preview.
                InvokeStage(stage, "SaveSelections", false);
                AssertMask(mask.triangleFlags[0], true, false);
                Assert.That(stage.hasSaved, Is.False);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.True);
            });
        }

        [Test]
        public void ReturningAnOffscreenDraftToItsBaselineClearsUnsavedState()
        {
            WithDraftStage((stage, slotData) =>
            {
                SetStageSelection(stage, slot.slotName, 1);
                SwitchDraftLevel(stage, 1);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.True);
                SwitchDraftLevel(stage, 0);
                SetStageSelection(stage, slot.slotName, 0);
                SwitchDraftLevel(stage, 1);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.False);
            });
        }

        [Test]
        public void NewAssetsCollectFacesFromAllDraftsEvenWhenCurrentLevelIsEmpty()
        {
            WithDraftStage((stage, slotData) =>
            {
                stage.CurrentHideAsset = null;
                SetStageSelection(stage, slot.slotName);
                InvokeStage(stage, "CaptureSavedSelectionSnapshot");
                SetStageSelection(stage, slot.slotName, 0);
                SwitchDraftLevel(stage, 1);
                SetStageSelection(stage, slot.slotName, 2);
                SwitchDraftLevel(stage, 2);
                AssertStageTriangles(stage);
                var names = (HashSet<string>)InvokeStage(stage, "GetDraftSlotNames");
                Assert.That(names, Does.Contain(slot.slotName));
                var newMask = Track(ScriptableObject.CreateInstance<MeshHideAsset>());
                InvokeStage(stage, "WriteLODDraftsToAsset", newMask, slotData, true);
                AssertMask(newMask.triangleFlags[0], true, false);
                AssertMask(newMask.GetTriangleFlagsForLOD(1)[0], false, false, true, false);
                AssertMask(newMask.GetTriangleFlagsForLOD(2)[0], false);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.True, "Drafts remain dirty until their save completes.");
            });
        }

        [Test]
        public void RecalculateUsesUnsavedBaseDraftAndPreservesOtherDrafts()
        {
            WithDraftStage((stage, slotData) =>
            {
                SetStageSelection(stage, slot.slotName, 1);
                SwitchDraftLevel(stage, 1);
                InvokeStage(stage, "RecalculateLOD");
                AssertStageTriangles(stage, 3); // All three vertices of this face belong to the LOD-0 draft.
                SwitchDraftLevel(stage, 0);
                AssertStageTriangles(stage, 1);
                SwitchDraftLevel(stage, 1);
                AssertStageTriangles(stage, 3);
                AssertMask(mask.triangleFlags[0], true, false);
                AssertMask(mask.GetTriangleFlagsForLOD(1)[0], true, true, false, false);
            });
        }

        [Test]
        public void UndoBeforeSwitchingDoesNotResurrectDiscardedDraftChanges()
        {
            WithDraftStage((stage, slotData) =>
            {
                Undo.IncrementCurrentGroup();
                InvokeStage(stage, "RecordSelectionUndo", "Test LOD edit");
                SetStageSelection(stage, slot.slotName, 1);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.True);
                Undo.PerformUndo();
                InvokeStage(stage, "OnUndoRedoSelection");
                SwitchDraftLevel(stage, 1);
                SwitchDraftLevel(stage, 0);
                AssertStageTriangles(stage, 0);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.False);
            });
        }

        [Test]
        public void GenerationModeIsRetainedAcrossSwitchesAndSavedExplicitly()
        {
            WithDraftStage((stage, slotData) =>
            {
                SetStageField(stage, "lodGenerationStrategy", MeshHideAsset.TriangleHideStrategy.Weighted);
                SwitchDraftLevel(stage, 1);
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.True);
                InvokeStage(stage, "SaveSelections", false);
                Assert.That(mask.LODGenerationStrategy, Is.EqualTo(MeshHideAsset.TriangleHideStrategy.Weighted));
                Assert.That(InvokeStage(stage, "HasUnsavedSelectionChanges"), Is.False);
            });
        }

        private SlotDataAsset CreateBakedSlot(string race = "TestRace")
        {
            string logicalName = slot.slotName;
            slot.name = logicalName + "_slot";
            slot._oldSlotName = logicalName;
            var baked = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
            baked.name = logicalName + "_baked_" + race;
            baked.isBaked = true;
            typeof(SlotDataAsset).GetField("_sourceSlotName", PrivateInstance).SetValue(baked, logicalName);
            baked.meshData = slot.meshData;
            return baked;
        }

        [Test]
        public void LegacyBakedNameMatchesOnlyItsSourceAndRace()
        {
            var baked = CreateBakedSlot();
            var wrongRace = CreateBakedSlot("OtherRace");
            var target = new SlotData(baked);
            mask.AssetSlotName = slot.name + "_baked_TestRace";
            Assert.That(mask.asset, Is.Null, "Generated baked slots are not in the asset index.");
            Assert.That(mask.FindMatchingSlot(new[] { new SlotData(slot), new SlotData(wrongRace), target }), Is.SameAs(target));
            Assert.That(mask.FindMatchingSlot(new[] { new SlotData(slot), new SlotData(wrongRace) }), Is.Null);
            var exact = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
            exact.name = mask.AssetSlotName;
            var exactSlot = new SlotData(exact);
            Assert.That(mask.FindMatchingSlot(new[] { target, exactSlot }), Is.SameAs(exactSlot));
        }

        [Test]
        public void BaseSlotNameMatchesItsBakedCopy()
        {
            // A hide is authored against the base slot. A race with prebaked blend shapes builds its
            // characters from baked copies with another slot name, and the hide must still apply.
            var baked = CreateBakedSlot();
            var target = new SlotData(baked);
            var unrelated = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
            unrelated.name = "SomethingElse";
            mask.AssetSlotName = slot.slotName;
            Assert.That(baked.slotName, Is.Not.EqualTo(slot.slotName), "the baked copy has its own slot name");
            Assert.That(baked.sourceSlot, Is.EqualTo(slot.slotName));

            Assert.That(mask.FindMatchingSlot(new[] { new SlotData(unrelated), target }), Is.SameAs(target));
            // The base slot itself, when it is in the recipe, is the exact match.
            var baseSlot = new SlotData(slot);
            Assert.That(mask.FindMatchingSlot(new[] { target, baseSlot }), Is.SameAs(baseSlot));
            // Two baked copies of the same slot: no guessing.
            Assert.That(mask.FindMatchingSlot(new[] { target, new SlotData(CreateBakedSlot("OtherRace")) }), Is.Null);
            Assert.That(mask.FindMatchingSlot(new[] { new SlotData(unrelated) }), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacyBakedMaskLoadsDraftsAndSavesCanonicalSlotWithoutLosingFaces(bool useCollection)
        {
            WithDraftStage((stage, slotData) =>
            {
                var baked = CreateBakedSlot();
                slotData.asset = baked;
                string legacyName = slot.name + "_baked_TestRace";
                mask.AssetSlotName = legacyName;
                if (useCollection)
                {
                    var collection = Track(ScriptableObject.CreateInstance<MeshHideAssetCollection>());
                    collection.AddOrUpdate(mask);
                    stage.CurrentHideAsset = null;
                    stage.CurrentHideCollection = collection;
                }
                InvokeStage(stage, "RefreshSlotSelectionEntries");
                InvokeStage(stage, "LoadSelections");
                InvokeStage(stage, "CaptureSavedSelectionSnapshot");
                AssertStageTriangles(stage, 0);
                Assert.That(((IDictionary)typeof(FaceEditorStage).GetField("meshHideLoadWarnings", PrivateInstance).GetValue(stage)).Count, Is.Zero);
                Assert.That(mask.AssetSlotName, Is.EqualTo(legacyName), "Loading must not rewrite the asset.");
                SetStageSelection(stage, baked.slotName, 1);
                SwitchDraftLevel(stage, 1);
                InvokeStage(stage, "RecalculateLOD");
                AssertStageTriangles(stage, 3);
                SwitchDraftLevel(stage, 0);
                AssertStageTriangles(stage, 1);
                InvokeStage(stage, "SaveSelections", false);
                Assert.That(stage.hasSaved, Is.True);
                Assert.That(mask.AssetSlotName, Is.EqualTo(baked.slotName));
                AssertMask(mask.triangleFlags[0], false, true);
                AssertMask(mask.GetTriangleFlagsForLOD(1, baked)[0], false, false, false, true);
                AssertMask(mask.GetTriangleFlagsForLOD(2, baked)[0], true);
                Assert.That(mask.AssetHash, Is.EqualTo(baked.meshData.CalculateHashCode()));
            });
        }

        [Test]
        public void GeneratedBakedSlotSuppliesLODTopologyAtRuntime()
        {
            var baked = CreateBakedSlot();
            mask.AssetSlotName = slot.name + "_baked_TestRace";
            var recipe = Track(ScriptableObject.CreateInstance<UMAWardrobeRecipe>());
            recipe.MeshHideAssets.Add(mask);
            var target = new SlotData(baked);
            var build = new UMAData.UMARecipe { slotDataList = new[] { target } };
            var hides = new Dictionary<string, List<MeshHideAsset>>();
            typeof(DynamicCharacterAvatar).GetMethod("ProcessMeshHides", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { hides, new List<UMATextRecipe> { recipe }, build });
            Assert.That(hides.ContainsKey(baked.slotName), Is.True);
            build.MeshHideDictionary = hides;
            build.UpdateMeshHideMasks(1);
            AssertMask(target.meshHideMask[0], true, false, false, false);
            build.UpdateMeshHideMasks(2);
            AssertMask(target.meshHideMask[0], false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnmatchedOrIncompatibleMasksShowWarningAndAreNotOverwritten(bool incompatible)
        {
            WithDraftStage((stage, slotData) =>
            {
                if (incompatible) mask.SaveSelectionForLOD(new[] { new BitArray(new[] { true }) }, 0);
                else mask.AssetSlotName = "MissingSlot_" + Guid.NewGuid().ToString("N");
                var original = new BitArray(mask.triangleFlags[0]);
                InvokeStage(stage, "LoadSelections");
                InvokeStage(stage, "CaptureSavedSelectionSnapshot");
                AssertStageTriangles(stage);
                var warnings = (IDictionary)typeof(FaceEditorStage).GetField("meshHideLoadWarnings", PrivateInstance).GetValue(stage);
                Assert.That(warnings.Contains(mask), Is.True);
                Assert.That((string)warnings[mask], Does.Contain(incompatible ? "triangle count mismatch" : "could not be matched"));
                InvokeStage(stage, "SaveSelections", false);
                CollectionAssert.AreEqual(original, mask.triangleFlags[0]);
            });
        }

        private void WithDraftStage(Action<FaceEditorStage, SlotData> test)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = Track(new GameObject("LOD draft test"));
                go.SetActive(false);
                SceneManager.MoveGameObjectToScene(go, preview);
                var avatar = go.AddComponent<DynamicCharacterAvatar>();
                avatar.editorTimeGeneration = false;
                var slotData = new SlotData(slot);
                avatar.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { slotData } };
                var stage = Track(ScriptableObject.CreateInstance<FaceEditorStage>());
                stage.thisDCA = avatar;
                stage.CurrentHideAsset = mask;
                stage.BakedMesh = Track(new Mesh { vertices = slot.meshData.vertices, triangles = slot.meshData.submeshes[0].getManagedTriangles(0) });
                mask.SaveSelectionForLOD(new[] { new BitArray(new[] { true, true, false, false }) }, 1);
                mask.SaveSelectionForLOD(new[] { new BitArray(new[] { true }) }, 2);
                InvokeStage(stage, "RefreshSlotSelectionEntries");
                InvokeStage(stage, "LoadSelections");
                InvokeStage(stage, "CaptureSavedSelectionSnapshot");
                test(stage, slotData);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void SwitchDraftLevel(FaceEditorStage stage, int lod)
        {
            InvokeStage(stage, "StoreCurrentLODDraft");
            SetStageField(stage, "selectedLOD", lod);
            // Rebuilding the mesh can prune the previous topology's active selection.
            SetStageSelection(stage, string.Empty);
            InvokeStage(stage, "RestoreOrLoadLODDraft");
        }

        private static void SetStageSelection(FaceEditorStage stage, string slotName, params int[] triangles)
        {
            StageSelection(stage).Clear();
            InvokeStage(stage, "RebuildSelectedSlotTrianglesFromSerialized");
            var keyType = typeof(FaceEditorStage).GetNestedType("SlotTriangleKey", BindingFlags.NonPublic);
            foreach (int triangle in triangles)
                InvokeStage(stage, "AddSelectedSlotTriangle", Activator.CreateInstance(keyType, slotName, 0, triangle));
        }

        private static void AssertStageTriangles(FaceEditorStage stage, params int[] expected)
        {
            var actual = new List<int>();
            foreach (var key in StageSelection(stage)) actual.Add((int)key.GetType().GetField("slotTriangleIndex").GetValue(key));
            CollectionAssert.AreEquivalent(expected, actual);
        }

        private static void AssertMask(BitArray actual, params bool[] expected) => CollectionAssert.AreEqual(expected, actual);
        private static IList StageSelection(FaceEditorStage stage) => (IList)typeof(FaceEditorStage).GetField("selectedSlotTrianglesSerialized", PrivateInstance).GetValue(stage);
        private static void SetStageField(FaceEditorStage stage, string field, object value) => typeof(FaceEditorStage).GetField(field, PrivateInstance).SetValue(stage, value);
        private static object InvokeStage(FaceEditorStage stage, string method, params object[] args) => typeof(FaceEditorStage).GetMethod(method, PrivateInstance).Invoke(stage, args);
        private T Track<T>(T value) where T : Object { objects.Add(value); return value; }
    }
}
#endif
