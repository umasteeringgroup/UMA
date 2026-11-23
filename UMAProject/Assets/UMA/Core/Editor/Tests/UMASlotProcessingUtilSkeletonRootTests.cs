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

        // A weapon or prop skinned to a hand bone, in a skeleton without the Root Bone ("Global"): the builder
        // kept only the Root Bone's hierarchy, lost the hand bone, and failed in UMAMeshData.FindRoot.
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void AWeaponSkinnedToAHandBoneWithoutTheRootBoneStillBuilds(bool batchMode, bool withArmature)
        {
            sourceObject = new GameObject("Sword Model");
            Transform top = withArmature ? AddChild(sourceObject.transform, "Armature") : sourceObject.transform;
            Transform hand = AddChild(top, "hand_R");
            hand.localPosition = Vector3.up;

            var renderer = AddChild(sourceObject.transform, "Sword").gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = hand;
            renderer.bones = new[] { hand };
            sourceMesh = new Mesh
            {
                name = "Sword mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = new[] { hand.worldToLocalMatrix * renderer.transform.localToWorldMatrix },
                boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 3).ToArray()
            };
            sourceMesh.RecalculateNormals();
            sourceMesh.RecalculateTangents();
            renderer.sharedMesh = sourceMesh;

            var parameters = new SlotBuilderParameters
            {
                slotMesh = renderer,
                rootBone = "Global",
                slotName = "Sword",
                assetName = "Sword",
                assetFolder = "Sword",
                slotFolder = testFolder,
                useRootFolder = true,
                keepList = new List<string>(),
                batchMode = batchMode
            };

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Root Bone 'Global' is not in"));
            var result = UMASlotProcessingUtil.CreateSlotData(parameters);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Slots, Has.Count.EqualTo(1));
            var data = result.Slots[0].meshData;
            Assert.That(data.boneNameHashes, Is.EqualTo(new[] { UMAUtils.StringToHash("hand_R") }), "the sword follows the character's hand bone");
            Assert.That(data.rootBoneHash, Is.EqualTo(UMAUtils.StringToHash(withArmature ? "Armature" : "hand_R")));
            Assert.That(data.ManagedBoneWeights.All(w => w.boneIndex == 0 && w.weight == 1f), Is.True);
            // Only the temporary clone was changed.
            Assert.That(hand.parent, Is.SameAs(top));
            Assert.That(renderer.bones, Is.EqualTo(new[] { hand }));
        }

        // A user's weapon (M4) from Blender: armature object "Root" holding Global > Position > Base > AimTransform,
        // the mesh weighted to Base and AimTransform. "Global" is also UMA's default Root Bone name.
        [TestCase("Global", true)]
        [TestCase("Root", true)]
        [TestCase("Global", false)]
        [TestCase("Root", false)]
        public void AWeaponWithItsOwnGlobalPositionBaseChainBuilds(string rootBoneName, bool batchMode)
        {
            sourceObject = new GameObject("M4 Model");
            Transform armature = AddChild(sourceObject.transform, "Root");
            Transform global = AddChild(armature, "Global");
            Transform position = AddChild(global, "Position");
            Transform weaponBase = AddChild(position, "Base");
            weaponBase.localPosition = new Vector3(0f, 0.2f, 0.1f);
            Transform aim = AddChild(weaponBase, "AimTransform");
            aim.localPosition = new Vector3(0f, 0.05f, 0.4f);

            var renderer = AddChild(sourceObject.transform, "M4").gameObject.AddComponent<SkinnedMeshRenderer>();
            Transform[] bones = { global, position, weaponBase, aim };
            renderer.rootBone = global;
            renderer.bones = bones;
            sourceMesh = new Mesh
            {
                name = "M4 mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = bones.Select(b => b.worldToLocalMatrix * renderer.transform.localToWorldMatrix).ToArray(),
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 3, weight0 = 1f },
                }
            };
            sourceMesh.RecalculateNormals();
            sourceMesh.RecalculateTangents();
            renderer.sharedMesh = sourceMesh;

            var parameters = new SlotBuilderParameters
            {
                slotMesh = renderer,
                rootBone = rootBoneName,
                slotName = "M4",
                assetName = "M4",
                assetFolder = "M4",
                slotFolder = testFolder,
                useRootFolder = true,
                keepList = new List<string>(),
                batchMode = batchMode
            };

            var result = UMASlotProcessingUtil.CreateSlotData(parameters);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Slots, Has.Count.EqualTo(1));
            var data = result.Slots[0].meshData;
            int baseHash = UMAUtils.StringToHash("Base");
            int aimHash = UMAUtils.StringToHash("AimTransform");
            Assert.That(data.boneNameHashes, Does.Contain(baseHash));
            Assert.That(data.boneNameHashes, Does.Contain(aimHash));
            int baseIndex = System.Array.IndexOf(data.boneNameHashes, baseHash);
            int aimIndex = System.Array.IndexOf(data.boneNameHashes, aimHash);
            Assert.That(data.ManagedBoneWeights.Select(w => w.boneIndex).ToArray(), Is.EqualTo(new[] { baseIndex, baseIndex, aimIndex }),
                "each vertex still follows its own weapon bone");
            Assert.That(renderer.bones, Is.EqualTo(bones), "only the temporary clone was changed");
        }

        // Bones stripped on import (Optimize Game Objects with an Avatar): Unity leaves the renderer an empty bone
        // list (measured); a list of empty entries fails the same way.
        [TestCase(true)]
        [TestCase(false)]
        public void AMeshWhoseBonesWereStrippedOnImportReportsWhy(bool emptyList)
        {
            sourceObject = new GameObject("Stripped Model");
            var renderer = AddChild(sourceObject.transform, "Stripped").gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = emptyList ? new Transform[0] : new Transform[] { null };
            sourceMesh = new Mesh
            {
                name = "Stripped mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = new[] { Matrix4x4.identity },
                boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 3).ToArray()
            };
            sourceMesh.RecalculateNormals();
            renderer.sharedMesh = sourceMesh;
            var parameters = new SlotBuilderParameters
            {
                slotMesh = renderer,
                rootBone = "Global",
                slotName = "Stripped",
                assetName = "Stripped",
                assetFolder = "Stripped",
                slotFolder = testFolder,
                useRootFolder = true,
                keepList = new List<string>(),
                batchMode = true
            };

            // It threw a NullReferenceException with no hint of the cause.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Optimize Game Objects"));
            Assert.That(UMASlotProcessingUtil.CreateSlotData(parameters), Is.Null);
        }

        // The Slot Builder warns when a model is imported with Optimize Game Objects (it removes the bones).
        [Test]
        public void AModelImportedWithOptimizeGameObjectsIsFound()
        {
            const string source = "Assets/UMA/InternalDataStore/InGame/Resources/PlaceholderAssets/bonemesh.fbx";
            if (AssetImporter.GetAtPath(source) == null) Assert.Ignore("Requires " + source);
            string path = testFolder + "/OptimizedModel.fbx";
            Assert.That(AssetDatabase.CopyAsset(source, path), Is.True);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.That(UMASlotProcessingUtil.ModelsWithOptimizedGameObjects(new[] { model }), Is.Empty);

            importer.optimizeGameObjects = true;
            importer.SaveAndReimport();
            model = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.That(UMASlotProcessingUtil.ModelsWithOptimizedGameObjects(new[] { model }), Is.EqualTo(new[] { path }));
            var folder = AssetDatabase.LoadMainAssetAtPath(testFolder);
            Assert.That(UMASlotProcessingUtil.ModelsWithOptimizedGameObjects(new[] { folder, model }), Is.EqualTo(new[] { path }), "a dropped folder is searched, and each model listed once");

            // Without an Avatar Unity does not apply the setting, so there is nothing to warn about.
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.SaveAndReimport();
            Assert.That(UMASlotProcessingUtil.ModelsWithOptimizedGameObjects(new[] { AssetDatabase.LoadMainAssetAtPath(path) }), Is.Empty);
            importer.animationType = ModelImporterAnimationType.None;
            importer.SaveAndReimport();
            Assert.That(UMASlotProcessingUtil.ModelsWithOptimizedGameObjects(new[] { AssetDatabase.LoadMainAssetAtPath(path) }), Is.Empty);
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
