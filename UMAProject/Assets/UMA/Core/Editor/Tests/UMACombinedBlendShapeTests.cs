#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    // The jobified combiner fills one set of buffers for all blendshape frames and clears only what the previous
    // frame wrote. Every frame of every shape must still come out exactly: the deltas of the slots that have the
    // shape, and zeros everywhere else.
    public class UMACombinedBlendShapeTests
    {
        private static UMABlendFrame Frame(float weight, Vector3 delta, Vector3? normal, Vector3? tangent)
        {
            return new UMABlendFrame
            {
                frameWeight = weight,
                deltaVertices = Enumerable.Repeat(delta, 3).ToArray(),
                deltaNormals = normal.HasValue ? Enumerable.Repeat(normal.Value, 3).ToArray() : new Vector3[0],
                deltaTangents = tangent.HasValue ? Enumerable.Repeat(tangent.Value, 3).ToArray() : new Vector3[0]
            };
        }

        private static void AssertFrame(Mesh mesh, string shape, int frame, float weight, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
        {
            int index = mesh.GetBlendShapeIndex(shape);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Missing blendshape " + shape);
            Assert.That(mesh.GetBlendShapeFrameWeight(index, frame), Is.EqualTo(weight), shape + " frame " + frame);
            var dv = new Vector3[mesh.vertexCount]; var dn = new Vector3[mesh.vertexCount]; var dt = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(index, frame, dv, dn, dt);
            Assert.That(dv, Is.EqualTo(vertices), shape + " frame " + frame + " vertices");
            Assert.That(dn, Is.EqualTo(normals), shape + " frame " + frame + " normals");
            Assert.That(dt, Is.EqualTo(tangents), shape + " frame " + frame + " tangents");
        }

        // First three entries belong to the first slot, the last three to the second.
        private static Vector3[] Six(Vector3 first, Vector3 second)
        {
            return new[] { first, first, first, second, second, second };
        }

        [Test]
        public void EveryFrameHoldsOnlyTheDeltasOfTheSlotsThatHaveTheShape()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                Vector3 x = Vector3.right, y = Vector3.up, z = Vector3.forward, none = Vector3.zero;
                // First slot: a two-frame shape with normals and tangents, then a shape only it has.
                f.Slot.meshData.blendShapes = new[]
                {
                    new UMABlendShape { shapeName = "Both", frames = new[] { Frame(50, x, y, z), Frame(100, x * 2, y * 2, z * 2) } },
                    new UMABlendShape { shapeName = "OnlyFirst", frames = new[] { Frame(100, y * 3, x * 3, null) } },
                    new UMABlendShape { shapeName = "LateFirst", frames = new[] { Frame(100, z * 7, null, null) } }
                };

                // Second slot: the shared shape without normals, a shape only it has, and one with tangents only.
                var secondAsset = f.Keep(ScriptableObject.CreateInstance<SlotDataAsset>());
                secondAsset.name = "CacheTestSlotB";
                secondAsset.meshData = f.Slot.meshData.ShallowCopy(null);
                secondAsset.meshData.SlotName = secondAsset.slotName;
                secondAsset.meshData.blendShapes = new[]
                {
                    new UMABlendShape { shapeName = "OnlySecond", frames = new[] { Frame(100, z * 4, null, null) } },
                    new UMABlendShape { shapeName = "Both", frames = new[] { Frame(50, x * 5, null, null), Frame(100, x * 6, null, null) } },
                    new UMABlendShape { shapeName = "TangentsSecond", frames = new[] { Frame(100, y * 8, null, x * 9) } }
                };

                var data = f.Avatar(false);
                var secondSlot = new SlotData(secondAsset);
                data.umaRecipe.slotDataList = new[] { data.umaRecipe.slotDataList[0], secondSlot };
                data.generatedMaterials.materials[0].materialFragments.Add(new UMAData.MaterialFragment
                {
                    slotData = secondSlot, atlasRegion = new Rect(0, 0, 256, 256), overlayList = secondSlot.GetOverlayList()
                });

                // Twice: the second build reuses the renderer's mesh object and pooled buffers that hold old data.
                for (int build = 0; build < 2; build++)
                {
                    f.BuildJobified(data);
                    Mesh mesh = data.GetRenderer(0).sharedMesh;
                    Assert.That(mesh.vertexCount, Is.EqualTo(6));
                    Assert.That(mesh.blendShapeCount, Is.EqualTo(5));
                    Assert.That(mesh.GetBlendShapeFrameCount(mesh.GetBlendShapeIndex("Both")), Is.EqualTo(2));

                    AssertFrame(mesh, "Both", 0, 50, Six(x, x * 5), Six(y, none), Six(z, none));
                    AssertFrame(mesh, "Both", 1, 100, Six(x * 2, x * 6), Six(y * 2, none), Six(z * 2, none));
                    AssertFrame(mesh, "OnlyFirst", 0, 100, Six(y * 3, none), Six(x * 3, none), Six(none, none));
                    AssertFrame(mesh, "LateFirst", 0, 100, Six(z * 7, none), Six(none, none), Six(none, none));
                    AssertFrame(mesh, "OnlySecond", 0, 100, Six(none, z * 4), Six(none, none), Six(none, none));
                    AssertFrame(mesh, "TangentsSecond", 0, 100, Six(none, y * 8), Six(none, none), Six(none, x * 9));
                }
            }
        }
    }
}
#endif
