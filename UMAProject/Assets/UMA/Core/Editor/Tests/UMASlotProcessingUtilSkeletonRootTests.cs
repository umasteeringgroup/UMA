#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMASlotProcessingUtilSkeletonRootTests
    {
        private string testFolder;
        private GameObject sourceObject;
        private Mesh sourceMesh;

        [SetUp]
        public void SetUp()
        {
            string folderName = "__UMASlotSkeletonRootTests_" + Guid.NewGuid().ToString("N");
            testFolder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
        }

        [TearDown]
        public void TearDown()
        {
            if (sourceObject != null) UnityEngine.Object.DestroyImmediate(sourceObject);
            if (sourceMesh != null) UnityEngine.Object.DestroyImmediate(sourceMesh);
            if (!string.IsNullOrEmpty(testFolder)) AssetDatabase.DeleteAsset(testFolder);
        }

        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void CreateSlotRetainsSkeletonAndWeightsWhenExtractingRoot(bool batchMode, bool rootAboveSkeleton)
        {
            sourceObject = new GameObject("Imported Model");
            Transform armature = AddChild(sourceObject.transform, "Imported_Rig");
            Transform global = AddChild(armature, "Global");
            Transform hips = AddChild(global, "Hips");
            Transform arm = AddChild(hips, "Arm");
            hips.localPosition = Vector3.up;
            arm.localPosition = Vector3.right;

            var renderer = AddChild(sourceObject.transform, "Arm_Left").gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = rootAboveSkeleton ? armature : global;
            // Like the infant FBX, the imported armature is present but has no skin weights.
            renderer.bones = new[] { armature, hips, arm };
            sourceMesh = new Mesh
            {
                name = "Arm mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = renderer.bones.Select(b => b.worldToLocalMatrix * renderer.transform.localToWorldMatrix).ToArray(),
                boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 2, weight0 = 1f }, 3).ToArray()
            };
            sourceMesh.RecalculateNormals();
            sourceMesh.RecalculateTangents();
            renderer.sharedMesh = sourceMesh;

            var parameters = new SlotBuilderParameters
            {
                slotMesh = renderer,
                rootBone = "Global",
                slotName = "Arm_Left",
                assetName = "Arm_Left",
                assetFolder = "Arm_Left",
                slotFolder = testFolder,
                useRootFolder = true,
                keepList = new List<string>(),
                batchMode = batchMode
            };

            var result = UMASlotProcessingUtil.CreateSlotData(parameters);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Slots, Has.Count.EqualTo(1));
            var slot = result.Slots[0];
            var data = slot.meshData;
            Assert.That(AssetDatabase.GetAssetPath(slot), Is.EqualTo(testFolder + "/Arm_Left_slot.asset"));
            Assert.That(data.rootBoneHash, Is.EqualTo(UMAUtils.StringToHash("Global")));
            Assert.That(data.boneNameHashes, Is.EqualTo(new[] { UMAUtils.StringToHash("Arm") }));
            Assert.That(data.bindPoses, Is.EqualTo(new[] { sourceMesh.bindposes[2] }));
            Assert.That(data.ManagedBoneWeights.Length, Is.EqualTo(3));
            Assert.That(data.ManagedBoneWeights.All(w => w.boneIndex == 0 && w.weight == 1f), Is.True);
            Assert.That(data.umaBones.Select(b => b.name), Is.EquivalentTo(new[] { "Hips", "Arm" }));
            Assert.That(data.umaBones.Single(b => b.name == "Hips").parent, Is.EqualTo(UMAUtils.StringToHash("Global")));
            Assert.That(data.umaBones.Single(b => b.name == "Arm").parent, Is.EqualTo(UMAUtils.StringToHash("Hips")));
            Assert.That(data.umaBones.Single(b => b.name == "Arm").position, Is.EqualTo(Vector3.right));
            // Importing must only reparent and change references on the temporary clone.
            Assert.That(renderer.rootBone, Is.SameAs(rootAboveSkeleton ? armature : global));
            Assert.That(global.parent, Is.SameAs(armature));
            Assert.That(renderer.bones, Is.EqualTo(new[] { armature, hips, arm }));
            Assert.That(sourceMesh.boneWeights.All(w => w.boneIndex0 == 2 && w.weight0 == 1f), Is.True);
        }

        private static Transform AddChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
    }
}
#endif
