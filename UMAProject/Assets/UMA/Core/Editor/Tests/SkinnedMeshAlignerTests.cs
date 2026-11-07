#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class SkinnedMeshAlignerTests
    {
        private readonly List<Object> objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void NoMatchingBonesPreservesFullyWeightedHair()
        {
            var hair = Renderer("Hair", new[] { "Hair_Back1", "Hair_Back2" });
            SetWeights(hair.sharedMesh, new byte[] { 1, 2, 1 },
                Weight(0, 1), Weight(0, 0.5f), Weight(1, 0.5f), Weight(1, 1));
            var reference = Renderer("Reference", new[] { "Head" });
            AssertUnchangedAfterAlignment(reference, hair);
        }

        [Test]
        public void NoMatchingBonesPreservesMeshWithoutSkinning()
        {
            var hair = Renderer("Hair", new[] { "Hair_Back1" });
            // A mesh without a skinning buffer must be untouched when no conversion applies.
            var reference = Renderer("Reference", new[] { "Head" });
            AssertUnchangedAfterAlignment(reference, hair);
        }

        [Test]
        public void NoMatchingBonesPreservesUnequalMultipleInfluences()
        {
            var hair = Renderer("Hair", new[] { "Hair_Back1", "Hair_Back2", "Hair_Back3", "Hair_Back4" });
            SetWeights(hair.sharedMesh, new byte[] { 4, 4, 4 },
                Weight(0, 0.4f), Weight(1, 0.3f), Weight(2, 0.2f), Weight(3, 0.1f),
                Weight(0, 0.4f), Weight(1, 0.3f), Weight(2, 0.2f), Weight(3, 0.1f),
                Weight(0, 0.4f), Weight(1, 0.3f), Weight(2, 0.2f), Weight(3, 0.1f));
            var reference = Renderer("Reference", new[] { "Head" });
            AssertUnchangedAfterAlignment(reference, hair);
        }

        [Test]
        public void MatchingBonesWithIdenticalBindPosesLeaveMeshUntouched()
        {
            var hair = Renderer("Hair", new[] { "Head" });
            // A mesh without a skinning buffer must be untouched when no conversion applies.
            var reference = Renderer("Reference", new[] { "Head" });
            AssertUnchangedAfterAlignment(reference, hair);
        }

        [Test]
        public void NoMatchingBonesDoesNotRequireNormals()
        {
            var hair = Renderer("Hair", new[] { "Hair_Back1" });
            SetWeights(hair.sharedMesh, new byte[] { 1, 1, 1 }, Weight(0, 1), Weight(0, 1), Weight(0, 1));
            hair.sharedMesh.normals = new Vector3[0];
            var reference = Renderer("Reference", new[] { "Head" });
            AssertUnchangedAfterAlignment(reference, hair);
        }

        [Test]
        public void BoneInReferenceHierarchyButAbsentFromRendererIsNotAligned()
        {
            var hair = Renderer("Hair", new[] { "Hair_Back1" });
            SetWeights(hair.sharedMesh, new byte[] { 1, 1, 1 }, Weight(0, 1), Weight(0, 1), Weight(0, 1));
            var reference = Renderer("Reference", new[] { "Head" });
            new GameObject("Hair_Back1").transform.SetParent(reference.rootBone);
            reference.sharedMesh.bindposes = new[] { Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)) };
            AssertUnchangedAfterAlignment(reference, hair);
        }

        [Test]
        public void PartialMatchTransformsOnlyInfluencedVertices()
        {
            // Missing bones must not shift the matching bone's bind-pose index.
            var hair = Renderer("Hair", new[] { "Hair_Back1", "Hair_Back2", "Hair_Back3", "Hair_Back4", "Head" });
            SetWeights(hair.sharedMesh, new byte[] { 4, 4, 1 },
                Weight(0, 0.4f), Weight(1, 0.3f), Weight(2, 0.2f), Weight(3, 0.1f),
                Weight(0, 0.4f), Weight(1, 0.3f), Weight(2, 0.2f), Weight(3, 0.1f),
                Weight(4, 1));
            var reference = Renderer("Reference", new[] { "Head" });
            Matrix4x4 targetBindPose = Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90));
            reference.sharedMesh.bindposes = new[] { targetBindPose };
            Vector3[] original = hair.sharedMesh.vertices;
            Vector3[] normals = hair.sharedMesh.normals;
            BoneWeight1[] weights = hair.sharedMesh.GetAllBoneWeights().ToArray();

            SkinnedMeshAligner.AlignBindPose(reference, hair);

            Assert.That(hair.sharedMesh.vertices[0], Is.EqualTo(original[0]));
            Assert.That(hair.sharedMesh.vertices[1], Is.EqualTo(original[1]));
            Assert.That(hair.sharedMesh.normals[0], Is.EqualTo(normals[0]));
            Assert.That(hair.sharedMesh.normals[1], Is.EqualTo(normals[1]));
            AssertVector(targetBindPose.inverse.MultiplyPoint3x4(original[2]), hair.sharedMesh.vertices[2]);
            AssertVector(targetBindPose.inverse.MultiplyVector(normals[2]), hair.sharedMesh.normals[2]);
            Assert.That(hair.sharedMesh.bindposes[0], Is.EqualTo(Matrix4x4.identity));
            Assert.That(hair.sharedMesh.bindposes[4], Is.EqualTo(targetBindPose));
            CollectionAssert.AreEqual(weights, hair.sharedMesh.GetAllBoneWeights().ToArray());
        }

        [Test]
        public void MixedInfluencesStillBlendAlignedAndUnmatchedBones()
        {
            var hair = Renderer("Hair", new[] { "Hair_Back1", "Head" });
            SetWeights(hair.sharedMesh, new byte[] { 2, 1, 1 },
                Weight(0, 0.5f), Weight(1, 0.5f), Weight(0, 1), Weight(1, 1));
            var reference = Renderer("Reference", new[] { "Head" });
            Matrix4x4 targetBindPose = Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90));
            reference.sharedMesh.bindposes = new[] { targetBindPose };
            Vector3 original = hair.sharedMesh.vertices[0];
            Vector3 normal = hair.sharedMesh.normals[0];

            SkinnedMeshAligner.AlignBindPose(reference, hair);

            AssertVector(0.5f * (original + targetBindPose.inverse.MultiplyPoint3x4(original)),
                hair.sharedMesh.vertices[0]);
            AssertVector(0.5f * (normal + targetBindPose.inverse.MultiplyVector(normal)),
                hair.sharedMesh.normals[0]);
        }

        [Test]
        public void NormalReferenceWithoutSeamsOrBonesPreservesHairGeometry()
        {
            var hair = Renderer("Hair", new[] { "Hair_Back1" });
            // A mesh without a skinning buffer must be untouched when no conversion applies.
            var reference = Renderer("Reference", new[] { "Head" });
            reference.sharedMesh.vertices = new[] { Vector3.one * 100, Vector3.one * 200, Vector3.one * 300 };
            Vector3[] original = hair.sharedMesh.vertices;
            Vector3[] normals = hair.sharedMesh.normals;

            Mesh seamMesh = SeamRemoval.PerformSeamRemoval(hair, reference, 0.0001f, false);
            objects.Add(seamMesh);
            hair.sharedMesh = seamMesh;
            SkinnedMeshAligner.AlignBindPose(reference, hair);

            CollectionAssert.AreEqual(original, hair.sharedMesh.vertices);
            CollectionAssert.AreEqual(normals, hair.sharedMesh.normals);
        }

        private SkinnedMeshRenderer Renderer(string name, string[] boneNames)
        {
            var root = new GameObject(name);
            objects.Add(root);
            var skeleton = new GameObject("Root").transform;
            skeleton.SetParent(root.transform);
            var bones = new Transform[boneNames.Length];
            var bindPoses = new Matrix4x4[boneNames.Length];
            for (int i = 0; i < boneNames.Length; i++)
            {
                bones[i] = new GameObject(boneNames[i]).transform;
                bones[i].SetParent(skeleton);
                bindPoses[i] = Matrix4x4.identity;
            }
            var mesh = new Mesh
            {
                name = name,
                vertices = new[] { new Vector3(2, 3, 4), new Vector3(5, 6, 7), new Vector3(8, 9, 10) },
                normals = new[] { Vector3.up, Vector3.right, Vector3.forward },
                tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = bindPoses
            };
            objects.Add(mesh);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = skeleton;
            renderer.bones = bones;
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private static BoneWeight1 Weight(int index, float weight)
        {
            return new BoneWeight1 { boneIndex = index, weight = weight };
        }

        private static void SetWeights(Mesh mesh, byte[] counts, params BoneWeight1[] weights)
        {
            using (var nativeCounts = new NativeArray<byte>(counts, Allocator.Temp))
            using (var nativeWeights = new NativeArray<BoneWeight1>(weights, Allocator.Temp))
            {
                mesh.SetBoneWeights(nativeCounts, nativeWeights);
                Assert.That(mesh.GetBonesPerVertex().Length, Is.EqualTo(mesh.vertexCount), "Fixture must have valid skinning data.");
            }
        }

        private static void AssertUnchangedAfterAlignment(SkinnedMeshRenderer reference, SkinnedMeshRenderer hair)
        {
            Mesh mesh = hair.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Matrix4x4[] bindPoses = mesh.bindposes;
            byte[] counts = mesh.GetBonesPerVertex().ToArray();
            BoneWeight1[] weights = mesh.GetAllBoneWeights().ToArray();
            Transform[] bones = hair.bones;

            SkinnedMeshAligner.AlignBindPose(reference, hair);

            CollectionAssert.AreEqual(vertices, mesh.vertices);
            CollectionAssert.AreEqual(normals, mesh.normals);
            CollectionAssert.AreEqual(tangents, mesh.tangents);
            CollectionAssert.AreEqual(bindPoses, mesh.bindposes);
            CollectionAssert.AreEqual(counts, mesh.GetBonesPerVertex().ToArray());
            CollectionAssert.AreEqual(weights, mesh.GetAllBoneWeights().ToArray());
            CollectionAssert.AreEqual(bones, hair.bones);
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.00001f));
        }
    }
}
#endif
