#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMASlotProcessingUtilBlendShapePrefixTests
    {
        private string testFolder;
        private GameObject sourceObject;
        private Mesh sourceMesh;

        [SetUp]
        public void SetUp()
        {
            string folderName = "__UMASlotBlendShapePrefixTests_" + Guid.NewGuid().ToString("N");
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

        [TestCase("Head.BigNose", "BigNose")]
        [TestCase("BigNose", "BigNose")]
        [TestCase("Head.Smile.L", "Smile.L")]
        [TestCase(".BigNose", "BigNose")]
        [TestCase("Head.", "Head.")]
        [TestCase(".", ".")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void OnlyThePartUpToTheFirstDotIsRemoved(string shapeName, string expected)
        {
            Assert.That(UMASlotProcessingUtil.StripBlendShapePrefix(shapeName), Is.EqualTo(expected));
        }

        [Test]
        public void BlendShapesThatWouldShareANameKeepTheirFullNames()
        {
            var meshData = new UMAMeshData
            {
                blendShapes = new[]
                {
                    new UMABlendShape { shapeName = "Head.BigNose" },
                    new UMABlendShape { shapeName = "Head.Smile" },
                    new UMABlendShape { shapeName = "Body.Smile" },
                    new UMABlendShape { shapeName = "Frown" },
                    new UMABlendShape { shapeName = "Face.Frown" }
                }
            };

            int renamed = UMASlotProcessingUtil.StripBlendShapePrefixes(meshData, "Test Slot");

            Assert.That(renamed, Is.EqualTo(1));
            Assert.That(
                meshData.blendShapes.Select(shape => shape.shapeName),
                Is.EqualTo(new[] { "BigNose", "Head.Smile", "Body.Smile", "Frown", "Face.Frown" }));
        }

        [Test]
        public void MeshDataWithoutBlendShapesIsLeftAlone()
        {
            Assert.That(UMASlotProcessingUtil.StripBlendShapePrefixes(null), Is.Zero);
            Assert.That(UMASlotProcessingUtil.StripBlendShapePrefixes(new UMAMeshData()), Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CreateSlotStripsThePrefixOnEverySubmeshSlotOnlyWhenTheOptionIsOn(bool stripPrefix)
        {
            SkinnedMeshRenderer renderer = BuildSource(secondTriangleInNextUdimTile: false, submeshes: 2);
            SlotBuilderParameters parameters = Parameters(renderer);
            parameters.stripBlendShapePrefix = stripPrefix;

            var result = UMASlotProcessingUtil.CreateSlotData(parameters);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Slots, Has.Count.EqualTo(2));
            string[] expected = stripPrefix
                ? new[] { "BigNose", "Smile" }
                : new[] { "Head.BigNose", "Smile" };
            foreach (SlotDataAsset slot in result.Slots)
            {
                Assert.That(slot.meshData.blendShapes.Select(shape => shape.shapeName), Is.EqualTo(expected), slot.slotName);
                // Only the name changes; the frame data is the same either way.
                Assert.That(slot.meshData.blendShapes[0].frames[0].deltaVertices.All(delta => delta == Vector3.up), Is.True);
            }
            // The source mesh is only read.
            Assert.That(sourceMesh.GetBlendShapeName(0), Is.EqualTo("Head.BigNose"));
        }

        [Test]
        public void CreateSlotStripsThePrefixOnUdimTileSlots()
        {
            SkinnedMeshRenderer renderer = BuildSource(secondTriangleInNextUdimTile: true, submeshes: 1);
            SlotBuilderParameters parameters = Parameters(renderer);
            parameters.stripBlendShapePrefix = true;
            parameters.udimAdjustment = true;
            parameters.udimTilesU = 10;
            parameters.udimTilesV = 10;
            parameters.addUDIMTileNumbers = true;
            parameters.appendTypeToName = true;

            var result = UMASlotProcessingUtil.CreateSlotData(parameters);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsUDIM, Is.True);
            Assert.That(result.Slots, Has.Count.EqualTo(2));
            foreach (SlotDataAsset slot in result.Slots)
            {
                Assert.That(
                    slot.meshData.blendShapes.Select(shape => shape.shapeName),
                    Is.EqualTo(new[] { "BigNose", "Smile" }),
                    slot.slotName);
            }
        }

        // This is the path the SlotDataAsset inspector uses when a mesh is dropped on an existing slot.
        [TestCase(true)]
        [TestCase(false)]
        public void UpdatingAnExistingSlotStripsThePrefixOnlyWhenAsked(bool stripPrefix)
        {
            SkinnedMeshRenderer renderer = BuildSource(secondTriangleInNextUdimTile: false, submeshes: 1);
            string slotPath = testFolder + "/Face_slot.asset";
            SlotDataAsset slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.PrepareForAssetPath(slotPath, "Face");
            AssetDatabase.CreateAsset(slot, slotPath);

            UMASlotProcessingUtil.UpdateSlotData(slot, renderer, null, null, "Global", false, false, false, stripPrefix);

            string[] expected = stripPrefix
                ? new[] { "BigNose", "Smile" }
                : new[] { "Head.BigNose", "Smile" };
            Assert.That(slot.meshData.blendShapes.Select(shape => shape.shapeName), Is.EqualTo(expected));
            Assert.That(slot.meshData.vertexCount, Is.EqualTo(6));
        }

        private SlotBuilderParameters Parameters(SkinnedMeshRenderer renderer)
        {
            return new SlotBuilderParameters
            {
                slotMesh = renderer,
                rootBone = "Global",
                slotName = "Face",
                assetName = "Face",
                assetFolder = "Face",
                slotFolder = testFolder,
                useRootFolder = true,
                keepList = new List<string>(),
                // Batch mode keeps the temporary mesh in memory and does not save other unsaved assets.
                batchMode = true
            };
        }

        private SkinnedMeshRenderer BuildSource(bool secondTriangleInNextUdimTile, int submeshes)
        {
            sourceObject = new GameObject("Imported Model");
            Transform global = AddChild(sourceObject.transform, "Global");
            Transform head = AddChild(global, "Head");
            head.localPosition = Vector3.up;

            var renderer = AddChild(sourceObject.transform, "Face").gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = global;
            renderer.bones = new[] { head };

            Vector2 tile = secondTriangleInNextUdimTile ? Vector2.right : Vector2.zero;
            sourceMesh = new Mesh
            {
                name = "Face mesh",
                vertices = new[]
                {
                    Vector3.zero, Vector3.right, Vector3.up,
                    Vector3.forward, Vector3.forward + Vector3.right, Vector3.forward + Vector3.up
                },
                uv = new[]
                {
                    new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.1f, 0.9f),
                    new Vector2(0.1f, 0.1f) + tile, new Vector2(0.9f, 0.1f) + tile, new Vector2(0.1f, 0.9f) + tile
                },
                bindposes = new[] { head.worldToLocalMatrix * renderer.transform.localToWorldMatrix },
                boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 6).ToArray()
            };
            if (submeshes == 2)
            {
                sourceMesh.subMeshCount = 2;
                sourceMesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                sourceMesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            }
            else
            {
                sourceMesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            }
            sourceMesh.RecalculateNormals();
            sourceMesh.RecalculateTangents();

            Vector3[] up = Enumerable.Repeat(Vector3.up, 6).ToArray();
            Vector3[] right = Enumerable.Repeat(Vector3.right, 6).ToArray();
            sourceMesh.AddBlendShapeFrame("Head.BigNose", 100f, up, null, null);
            sourceMesh.AddBlendShapeFrame("Smile", 100f, right, null, null);

            renderer.sharedMesh = sourceMesh;
            renderer.sharedMaterials = new Material[submeshes];
            return renderer;
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
