#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    // The jobified combiner turns a slot's triangle mask into one byte per triangle in a single pass that also
    // counts the hidden triangles. Every mask must hide exactly the triangles it flags, in order, whatever its
    // length and wherever its flags fall inside the 32-flag words it is copied out in.
    public class UMAMeshHideMaskCombineTests
    {
        private const int TriangleCount = 150;

        // A strip of separate triangles, so every triangle is recognisable by its three vertex indices.
        private static void GiveStrip(UMAResourceReuseMeshTests.Fixture f)
        {
            int vertexCount = TriangleCount * 3;
            var mesh = f.Slot.meshData;
            mesh.vertexCount = vertexCount;
            mesh.vertices = Enumerable.Range(0, vertexCount).Select(i => new Vector3(i / 3, i % 3 == 1 ? 1 : 0, i % 3 == 2 ? 1 : 0)).ToArray();
            mesh.normals = Enumerable.Repeat(Vector3.forward, vertexCount).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), vertexCount).ToArray();
            mesh.uv = Enumerable.Range(0, vertexCount).Select(i => new Vector2(i % 3 == 1 ? 1 : 0, i % 3 == 2 ? 1 : 0)).ToArray();
            mesh.submeshes = new[] { new SubMeshTriangles(Enumerable.Range(0, vertexCount).ToArray()) };
            mesh.ManagedBonesPerVertex = Enumerable.Repeat((byte)1, vertexCount).ToArray();
            mesh.ManagedBoneWeights = Enumerable.Repeat(new BoneWeight1 { boneIndex = 0, weight = 1 }, vertexCount).ToArray();
            mesh.blendShapes = new UMABlendShape[0];
        }

        private static BitArray Mask(int length, System.Func<int, bool> hidden)
        {
            var mask = new BitArray(length);
            for (int i = 0; i < length; i++) mask[i] = hidden(i);
            return mask;
        }

        private static IEnumerable<TestCaseData> Masks()
        {
            var random = new System.Random(4242);
            var noise = Enumerable.Range(0, 400).Select(_ => random.Next(0, 3) == 0).ToArray();
            yield return new TestCaseData(Mask(TriangleCount, i => false)).SetName("nothing hidden");
            yield return new TestCaseData(Mask(TriangleCount, i => i == 0)).SetName("first triangle");
            yield return new TestCaseData(Mask(TriangleCount, i => i == TriangleCount - 1)).SetName("last triangle");
            yield return new TestCaseData(Mask(TriangleCount, i => i == 31 || i == 32 || i == 63 || i == 64 || i == 127 || i == 128)).SetName("word borders");
            yield return new TestCaseData(Mask(TriangleCount, i => i % 2 == 0)).SetName("every other");
            yield return new TestCaseData(Mask(TriangleCount, i => noise[i])).SetName("scattered");
            yield return new TestCaseData(Mask(TriangleCount, i => i >= 32 && i < 96)).SetName("two whole words");
            yield return new TestCaseData(Mask(TriangleCount, i => i != 77)).SetName("all but one");
            // A mask shorter than the mesh hides nothing past its end; a longer one ignores its extra flags.
            yield return new TestCaseData(Mask(40, i => noise[i])).SetName("shorter than the mesh");
            yield return new TestCaseData(Mask(33, i => true)).SetName("short and all set");
            yield return new TestCaseData(Mask(400, i => noise[i])).SetName("longer than the mesh");
            yield return new TestCaseData(Mask(1, i => true)).SetName("one flag");
        }

        [TestCaseSource(nameof(Masks))]
        public void ExactlyTheFlaggedTrianglesAreLeftOut(BitArray mask)
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                GiveStrip(f);
                var hide = f.Keep(ScriptableObject.CreateInstance<MeshHideAsset>());
                typeof(MeshHideAsset).GetField("_triangleFlags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(hide, new[] { mask });
                var data = f.Avatar(false);
                var slot = data.umaRecipe.slotDataList[0];
                data.umaRecipe.MeshHideDictionary[slot.slotName] = new List<MeshHideAsset> { hide };

                f.BuildJobified(data);

                // What the combiner was given for this build.
                BitArray applied = slot.meshHideMask != null && slot.meshHideMask.Length > 0 ? slot.meshHideMask[0] : null;
                Assert.That(applied, Is.Not.Null, "The slot must have received a mask.");
                var expected = new List<int>();
                for (int triangle = 0; triangle < TriangleCount; triangle++)
                {
                    if (triangle < applied.Length && applied[triangle]) continue;
                    expected.Add(triangle * 3); expected.Add(triangle * 3 + 1); expected.Add(triangle * 3 + 2);
                }
                Assert.That(expected.Count, Is.GreaterThan(0), "Sanity: this case leaves something visible.");
                Assert.That(data.GetRenderer(0).sharedMesh.triangles, Is.EqualTo(expected.ToArray()));
                Assert.That(data.GetRenderer(0).sharedMesh.vertexCount, Is.EqualTo(TriangleCount * 3), "Hidden triangles keep their vertices.");
            }
        }

        [Test]
        public void ASlotWithEveryTriangleHiddenContributesNoTriangles()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                GiveStrip(f);
                var hide = f.Keep(ScriptableObject.CreateInstance<MeshHideAsset>());
                typeof(MeshHideAsset).GetField("_triangleFlags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(hide, new[] { Mask(TriangleCount, i => true) });
                var data = f.Avatar(false);
                data.umaRecipe.MeshHideDictionary[data.umaRecipe.slotDataList[0].slotName] = new List<MeshHideAsset> { hide };
                f.BuildJobified(data);
                Assert.That(data.GetRenderer(0).sharedMesh.triangles, Is.Empty);
            }
        }
    }
}
#endif
