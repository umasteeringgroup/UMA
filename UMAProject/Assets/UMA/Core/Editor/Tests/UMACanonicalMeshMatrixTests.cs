#if UNITY_EDITOR

using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Editors.Tests
{
    // SlotDataAsset.TryGetCanonicalMeshFromRootMatrix works out one mesh-to-root transform for a slot (for previews and
    // standalone tools) and warns about bones whose bind poses disagree with it. Only bones with skin weight count:
    // weightless bones, such as "_end" leaf bones with blank bind poses, cannot change anything.
    public sealed class UMACanonicalMeshMatrixTests
    {
        private const string Root = "Global";

        // A slot whose mesh space is the root's space: Head and Arm carry the vertices, Head_end is a leaf bone
        // with a blank (identity) bind pose, as exporters add them.
        private static UMAMeshData CreateMeshData(bool weighted, Quaternion armBindPoseError)
        {
            var root = UMAUtils.StringToHash(Root);
            var head = new UMATransform { name = "Head", hash = UMAUtils.StringToHash("Head"), parent = root, position = new Vector3(0f, 1.7f, 0f), rotation = Quaternion.identity, scale = Vector3.one };
            var headEnd = new UMATransform { name = "Head_end", hash = UMAUtils.StringToHash("Head_end"), parent = head.hash, position = new Vector3(0f, 0.1f, 0f), rotation = Quaternion.identity, scale = Vector3.one };
            var arm = new UMATransform { name = "Arm", hash = UMAUtils.StringToHash("Arm"), parent = root, position = new Vector3(0.3f, 1.4f, 0f), rotation = Quaternion.Euler(0f, 0f, -80f), scale = Vector3.one };

            Matrix4x4 headFromRoot = Matrix4x4.TRS(head.position, head.rotation, head.scale);
            Matrix4x4 armFromRoot = Matrix4x4.TRS(arm.position, arm.rotation, arm.scale);
            var data = new UMAMeshData
            {
                RootBoneName = Root,
                rootBoneHash = root,
                umaBones = new[] { head, headEnd, arm },
                boneNameHashes = new[] { head.hash, headEnd.hash, arm.hash },
                // meshFromRoot = boneFromRoot * bindPose is the identity for Head and, unless it is given an error, Arm.
                bindPoses = new[]
                {
                    headFromRoot.inverse,
                    Matrix4x4.identity,
                    (armFromRoot * Matrix4x4.Rotate(armBindPoseError)).inverse,
                },
                vertices = new[] { new Vector3(0f, 1.8f, 0f), new Vector3(0.1f, 1.8f, 0f), new Vector3(0.5f, 1.4f, 0f) },
                vertexCount = 3,
            };
            if (weighted)
            {
                data.ManagedBoneWeights = new[]
                {
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 0, weight = 1f },
                    new BoneWeight1 { boneIndex = 2, weight = 1f },
                };
            }
            return data;
        }

        private static void AssertIdentity(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++)
            {
                Assert.That(matrix[i], Is.EqualTo(Matrix4x4.identity[i]).Within(1e-4f), "element " + i);
            }
        }

        [Test]
        public void AWeightlessLeafBoneWithABlankBindPoseIsNotReported()
        {
            UMAMeshData data = CreateMeshData(true, Quaternion.identity);
            Assert.IsTrue(SlotDataAsset.TryGetCanonicalMeshFromRootMatrix(data, "Test hair", out Matrix4x4 meshFromRoot));
            AssertIdentity(meshFromRoot);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AWeightedBoneThatDisagreesIsStillReported()
        {
            UMAMeshData data = CreateMeshData(true, Quaternion.Euler(0f, 0f, 30f));
            LogAssert.Expect(LogType.Warning, new Regex(@"Slot 'Test jacket' produced 1 canonical matrix mismatches"));
            Assert.IsTrue(SlotDataAsset.TryGetCanonicalMeshFromRootMatrix(data, "Test jacket", out Matrix4x4 meshFromRoot));
            // Head carries the most weight, so its transform is the one used.
            AssertIdentity(meshFromRoot);
        }

        [Test]
        public void WithoutAnyWeightsEveryBoneIsStillCompared()
        {
            // Nothing tells which bones matter, so the leaf bone is reported as before.
            UMAMeshData data = CreateMeshData(false, Quaternion.identity);
            LogAssert.Expect(LogType.Warning, new Regex(@"Slot 'Unweighted' produced 1 canonical matrix mismatches"));
            Assert.IsTrue(SlotDataAsset.TryGetCanonicalMeshFromRootMatrix(data, "Unweighted", out _));
        }
    }
}

#endif
