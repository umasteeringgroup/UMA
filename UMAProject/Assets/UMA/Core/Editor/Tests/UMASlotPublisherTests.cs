#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Publishing a fitted mesh as a slot: a request is checked without writing, a slot that cannot
    /// be built leaves the project untouched, and a publish can be taken back.
    /// </summary>
    public sealed class UMASlotPublisherTests
    {
        private const string Folder = "Assets/UMASlotPublisherTests_Temp";

        private GameObject rig;
        private Mesh mesh;

        [SetUp]
        public void CreateFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "UMASlotPublisherTests_Temp");
            }
        }

        [TearDown]
        public void Clean()
        {
            if (rig != null) UnityEngine.Object.DestroyImmediate(rig);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        private static UMASlotPublisher.Request ValidRequest() => new UMASlotPublisher.Request
        {
            SlotName = "PublisherTestSlot",
            FolderPath = Folder,
            AddToGlobalLibrary = false
        };

        [Test]
        public void Validate_AcceptsASlotOnlyRequest()
        {
            Assert.That(UMASlotPublisher.Validate(ValidRequest()), Is.Null);
        }

        [Test]
        public void Validate_SaysWhatIsMissing()
        {
            Assert.That(UMASlotPublisher.Validate(null), Is.Not.Null);

            UMASlotPublisher.Request request = ValidRequest();
            request.SlotName = " ";
            Assert.That(UMASlotPublisher.Validate(request), Does.Contain("Slot Name"));

            request = ValidRequest();
            request.FolderPath = "Assets/NoSuchFolder_UMASlotPublisherTests";
            Assert.That(UMASlotPublisher.Validate(request), Does.Contain("folder"));

            request = ValidRequest();
            request.CreateWardrobeRecipe = true;
            Assert.That(UMASlotPublisher.Validate(request), Does.Contain("race"));
        }

        [Test]
        public void Validate_ARecipeNeedsAWardrobeSlotAndAnOverlay()
        {
            var race = ScriptableObject.CreateInstance<RaceData>();
            try
            {
                UMASlotPublisher.Request request = ValidRequest();
                request.CreateWardrobeRecipe = true;
                request.Race = race;
                request.WardrobeSlot = "None";
                Assert.That(UMASlotPublisher.Validate(request), Does.Contain("Wardrobe Slot"));
                request.WardrobeSlot = "Chest";
                Assert.That(UMASlotPublisher.Validate(request), Does.Contain("Overlay"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(race);
            }
        }

        [Test]
        public void Publish_WithAnInvalidRequest_WritesNothing()
        {
            UMASlotPublisher.Request request = ValidRequest();
            request.SlotName = string.Empty;
            Assert.Throws<InvalidOperationException>(() => UMASlotPublisher.Publish(request, null, null));
            Assert.That(AssetDatabase.FindAssets(string.Empty, new[] { Folder }), Is.Empty);
        }

        [Test]
        public void Publish_ThenRollback_LeavesNoAsset()
        {
            SkinnedMeshRenderer renderer = BuildRig(out UMASlotSkinTransfer.TargetData target);
            UMASlotPublisher.Result result = UMASlotPublisher.Publish(ValidRequest(), renderer, target);

            Assert.That(result.Slot, Is.Not.Null);
            Assert.That(result.SlotPath, Does.StartWith(Folder + "/"));
            Assert.That(AssetDatabase.LoadAssetAtPath<SlotDataAsset>(result.SlotPath), Is.Not.Null);
            Assert.That(result.Slot.meshData.vertexCount, Is.EqualTo(3));
            Assert.That(result.CreatedPaths, Is.EquivalentTo(new[] { result.SlotPath }));

            string path = result.SlotPath;
            UMASlotPublisher.Rollback(result);
            Assert.That(AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path), Is.Null);
            Assert.That(result.CreatedPaths, Is.Empty);
        }

        [Test]
        public void Publish_Twice_KeepsBothUnlessOverwriting()
        {
            SkinnedMeshRenderer renderer = BuildRig(out UMASlotSkinTransfer.TargetData target);
            UMASlotPublisher.Result first = UMASlotPublisher.Publish(ValidRequest(), renderer, target);
            UMASlotPublisher.Result second = UMASlotPublisher.Publish(ValidRequest(), renderer, target);
            Assert.That(second.SlotPath, Is.Not.EqualTo(first.SlotPath));

            UMASlotPublisher.Request overwrite = ValidRequest();
            overwrite.OverwriteExisting = true;
            UMASlotPublisher.Result third = UMASlotPublisher.Publish(overwrite, renderer, target);
            Assert.That(third.SlotPath, Is.EqualTo(first.SlotPath));
        }

        /// <summary>One triangle skinned to a root and one child bone.</summary>
        private SkinnedMeshRenderer BuildRig(out UMASlotSkinTransfer.TargetData target)
        {
            rig = new GameObject("PublisherTestRig") { hideFlags = HideFlags.HideAndDontSave };
            var root = new GameObject("Root").transform;
            root.SetParent(rig.transform, false);
            var child = new GameObject("Child").transform;
            child.SetParent(root, false);
            child.localPosition = new Vector3(0f, 1f, 0f);
            Transform[] bones = { root, child };

            mesh = new Mesh { name = "PublisherTestMesh" };
            mesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            var tangent = new Vector4(1f, 0f, 0f, 1f);
            mesh.tangents = new[] { tangent, tangent, tangent };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f }
            };
            mesh.bindposes = new[]
            {
                root.worldToLocalMatrix * rig.transform.localToWorldMatrix,
                child.worldToLocalMatrix * rig.transform.localToWorldMatrix
            };

            var renderer = rig.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = root;

            target = new UMASlotSkinTransfer.TargetData
            {
                Renderer = renderer,
                Bones = bones,
                BindPoses = mesh.bindposes,
                BoneNameHashes = new[] { UMAUtils.StringToHash(root.name), UMAUtils.StringToHash(child.name) },
                RootBone = root,
                RootBoneName = root.name,
                RootBoneHash = UMAUtils.StringToHash(root.name)
            };
            return renderer;
        }
    }
}
#endif
