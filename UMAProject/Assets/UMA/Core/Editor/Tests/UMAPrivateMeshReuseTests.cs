#if UNITY_EDITOR && UMA_BURSTCOMPILE
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    // A renderer with Recalculate Normals never shares its mesh. With mesh reuse on, the jobified combiner keeps
    // that renderer's own mesh when a rebuild would produce the same one, and combines again whenever anything
    // that goes into the mesh has changed.
    public class UMAPrivateMeshReuseTests
    {
        private const string KeptStatus = "Kept this avatar's own mesh";
        private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static UMARendererAsset RecalculatingAsset(UMAResourceReuseMeshTests.Fixture f)
        {
            var asset = f.Keep(ScriptableObject.CreateInstance<UMARendererAsset>());
            typeof(UMARendererAsset).GetField("_RecalculateNormals", NonPublic).SetValue(asset, true);
            return asset;
        }

        // The fixture's blendshape moves all three vertices the same way, which leaves the normal alone.
        // This one tilts the triangle.
        private static void TiltingBlendShape(UMAResourceReuseMeshTests.Fixture f)
        {
            f.Slot.meshData.blendShapes[0].frames[0].deltaVertices = new[] { Vector3.zero, Vector3.forward, Vector3.zero };
        }

        // True when the build combined the mesh again; false when it kept the renderer's mesh.
        private static bool BuildCombines(UMAResourceReuseMeshTests.Fixture f, UMAData data)
        {
            long before = SkinnedMeshCombinerMeshAPI.Ticks_CombineInternalTotal;
            data.resourceReuseStatus = null;
            f.BuildJobified(data);
            bool combined = SkinnedMeshCombinerMeshAPI.Ticks_CombineInternalTotal != before;
            Assert.That(data.resourceReuseStatus == KeptStatus, Is.EqualTo(!combined), "Status and combine counter must agree: " + data.resourceReuseStatus);
            return combined;
        }

        [Test]
        public void UnchangedRebuildKeepsTheMeshAndLeavesTheRendererAsACombineWould()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var data = f.Avatar();
                data.defaultRendererAsset = RecalculatingAsset(f);
                Assert.IsTrue(BuildCombines(f, data), "The first build has nothing to keep.");

                var renderer = data.GetRenderer(0);
                Mesh mesh = renderer.sharedMesh;
                Vector3[] vertices = mesh.vertices; Vector3[] normals = mesh.normals; Vector4[] tangents = mesh.tangents;
                Vector2[] uv = mesh.uv; int[] triangles = mesh.triangles; Matrix4x4[] bindposes = mesh.bindposes;
                Transform[] bones = renderer.bones; Bounds bounds = renderer.localBounds; Material[] materials = renderer.sharedMaterials;
                int subMeshes = mesh.subMeshCount; int blendShapes = mesh.blendShapeCount;
                var slot = data.umaRecipe.slotDataList[0];
                slot.vertexOffset = 123; slot.skinnedMeshRenderer = 7; // Per-build values a combine sets again.
                renderer.bones = new Transform[0];

                Assert.IsFalse(BuildCombines(f, data));
                Assert.IsFalse(BuildCombines(f, data));

                Assert.AreSame(mesh, renderer.sharedMesh);
                Assert.IsTrue(mesh.isReadable);
                Assert.IsFalse(UMAResourceLeaseOwner.IsSharedMesh(renderer));
                Assert.That(mesh.vertices, Is.EqualTo(vertices));
                Assert.That(mesh.normals, Is.EqualTo(normals));
                Assert.That(mesh.tangents, Is.EqualTo(tangents));
                Assert.That(mesh.uv, Is.EqualTo(uv));
                Assert.That(mesh.triangles, Is.EqualTo(triangles));
                Assert.That(mesh.bindposes, Is.EqualTo(bindposes));
                Assert.That(mesh.subMeshCount, Is.EqualTo(subMeshes));
                Assert.That(mesh.blendShapeCount, Is.EqualTo(blendShapes));
                Assert.That(renderer.bones, Is.EqualTo(bones));
                Assert.That(renderer.localBounds, Is.EqualTo(bounds));
                Assert.That(renderer.sharedMaterials, Is.EqualTo(materials));
                Assert.That(slot.vertexOffset, Is.EqualTo(0));
                Assert.That(slot.skinnedMeshRenderer, Is.EqualTo(0));
                Assert.That(data.originalMeshBounds, Is.EqualTo(mesh.bounds));
            }
        }

        [Test]
        public void TheMeshIsNeverSharedWithAnotherAvatar()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var asset = RecalculatingAsset(f);
                var first = f.Avatar(); var second = f.Avatar();
                first.defaultRendererAsset = second.defaultRendererAsset = asset;
                Assert.IsTrue(BuildCombines(f, first));
                Assert.IsTrue(BuildCombines(f, second), "An identical avatar still builds its own mesh.");
                Assert.AreNotSame(first.GetRenderer(0).sharedMesh, second.GetRenderer(0).sharedMesh);
                Assert.IsFalse(BuildCombines(f, first));
                Assert.IsFalse(BuildCombines(f, second));
                Assert.AreNotSame(first.GetRenderer(0).sharedMesh, second.GetRenderer(0).sharedMesh);
            }
        }

        [Test]
        public void WithoutMeshReuseEveryBuildCombines()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var data = f.Avatar(false);
                data.defaultRendererAsset = RecalculatingAsset(f);
                Assert.IsTrue(BuildCombines(f, data));
                Assert.IsTrue(BuildCombines(f, data));

                // Turning reuse on later starts from a build that was not recorded, and turning it off again
                // must not leave a record behind that a later build could match.
                data.reuseGeneratedMeshes = true;
                Assert.IsTrue(BuildCombines(f, data));
                Assert.IsFalse(BuildCombines(f, data));
                data.reuseGeneratedMeshes = false;
                Assert.IsTrue(BuildCombines(f, data));
                data.reuseGeneratedMeshes = true;
                Assert.IsTrue(BuildCombines(f, data), "The build made with reuse off replaced the recorded mesh.");
                Assert.IsFalse(BuildCombines(f, data));
            }
        }

        [TestCase("lod level")]
        [TestCase("source invalidated")]
        [TestCase("source mesh replaced")]
        [TestCase("expand along normal")]
        [TestCase("atlas region")]
        [TestCase("blendshapes ignored")]
        [TestCase("generated mesh edited")]
        [TestCase("generated mesh replaced")]
        [TestCase("triangles hidden")]
        public void AnyChangeToWhatGoesIntoTheMeshCombinesAgain(string change)
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var data = f.Avatar();
                data.defaultRendererAsset = RecalculatingAsset(f);
                Assert.IsTrue(BuildCombines(f, data));
                Assert.IsFalse(BuildCombines(f, data), "Sanity: unchanged inputs keep the mesh.");
                var renderer = data.GetRenderer(0);
                var slot = data.umaRecipe.slotDataList[0];
                Vector3[] original = renderer.sharedMesh.vertices;

                switch (change)
                {
                    case "lod level": data.currentLODLevel = 1; break;
                    case "source invalidated":
                        // The documented call after editing a source mesh in place.
                        f.Slot.meshData.vertices[1] = new Vector3(2, 0, 0);
                        UMAResourceReuse.InvalidateMeshSource(f.Slot.meshData);
                        break;
                    case "source mesh replaced":
                    {
                        var copy = f.Slot.meshData.ShallowCopy(null);
                        copy.vertices = new[] { Vector3.zero, new Vector3(3, 0, 0), Vector3.up };
                        f.Slot.meshData = copy;
                        break;
                    }
                    case "expand along normal": slot.expandAlongNormal = 500000; break;
                    case "atlas region": data.generatedMaterials.materials[0].materialFragments[0].atlasRegion = new Rect(0, 0, 128, 128); break;
                    case "blendshapes ignored": data.blendShapeSettings.ignoreBlendShapes = true; break;
                    case "generated mesh edited":
                    {
                        // A tool writing straight into the generated mesh. A rebuild must put the combined result back.
                        var moved = renderer.sharedMesh.vertices;
                        moved[2] += Vector3.up;
                        renderer.sharedMesh.vertices = moved;
                        break;
                    }
                    case "generated mesh replaced":
                        renderer.sharedMesh = f.Keep(UnityEngine.Object.Instantiate(renderer.sharedMesh));
                        break;
                    case "triangles hidden":
                    {
                        // The combiner rebuilds every slot's mask from this dictionary at the start of a build.
                        var hide = f.Keep(ScriptableObject.CreateInstance<MeshHideAsset>());
                        typeof(MeshHideAsset).GetField("_triangleFlags", NonPublic).SetValue(hide, new[] { new System.Collections.BitArray(1, true) });
                        data.umaRecipe.MeshHideDictionary[slot.slotName] = new System.Collections.Generic.List<MeshHideAsset> { hide };
                        break;
                    }
                }

                Assert.IsTrue(BuildCombines(f, data), "The change must be noticed.");
                Mesh rebuilt = renderer.sharedMesh;
                switch (change)
                {
                    case "source invalidated": Assert.That(rebuilt.vertices[1], Is.EqualTo(new Vector3(2, 0, 0))); break;
                    case "source mesh replaced": Assert.That(rebuilt.vertices[1], Is.EqualTo(new Vector3(3, 0, 0))); break;
                    case "expand along normal": Assert.That(rebuilt.vertices, Is.Not.EqualTo(original)); break;
                    case "blendshapes ignored": Assert.That(rebuilt.blendShapeCount, Is.Zero); break;
                    case "generated mesh edited": Assert.That(rebuilt.vertices, Is.EqualTo(original)); break;
                    case "triangles hidden": Assert.That(rebuilt.triangles.Length, Is.Zero); break;
                }
                if (change != "triangles hidden")
                {
                    Assert.IsFalse(BuildCombines(f, data), "The new state is recorded.");
                }
            }
        }

        [Test]
        public void AMeshUploadedAsNotReadableIsCombinedAgain()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var data = f.Avatar();
                data.defaultRendererAsset = RecalculatingAsset(f);
                data.markNotReadable = true;
                Assert.IsTrue(BuildCombines(f, data));
                Assert.IsFalse(data.GetRenderer(0).sharedMesh.isReadable);
                // Without its CPU copy the normals could not be recalculated for new blendshape weights.
                Assert.IsTrue(BuildCombines(f, data));
                Assert.IsTrue(BuildCombines(f, data));
            }
        }

        // The generator's order of events: combine with the upload deferred, DNA sets the blendshape weights,
        // then the normals are finalized. A kept mesh must end up with the same normals and tangents as a mesh
        // combined from scratch for the same weights.
        [Test]
        public void KeptMeshGetsTheSameNormalsAsAFreshCombineForNewBlendshapeWeights()
        {
            var observerField = typeof(UMARendererAsset).GetField("NormalsRecalculatedForTests", NonPublic);
            var finalize = typeof(UMARendererAsset).GetMethod("FinalizeGeneratedNormals", NonPublic);
            var scopeType = typeof(UMARendererAsset).GetNestedType("DeferredMeshUpload", BindingFlags.NonPublic);
            try
            {
                using (var f = new UMAResourceReuseMeshTests.Fixture())
                {
                    TiltingBlendShape(f);
                    var asset = RecalculatingAsset(f);
                    var kept = f.Avatar();
                    var fresh = f.Avatar(false);
                    kept.defaultRendererAsset = fresh.defaultRendererAsset = asset;
                    int recalculations = 0;
                    observerField.SetValue(null, (Action<SkinnedMeshRenderer, Vector3[]>)((smr, normals) => recalculations++));

                    bool GeneratorBuild(UMAData data, float weight)
                    {
                        bool combined;
                        var scope = (IDisposable)Activator.CreateInstance(scopeType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { data }, null);
                        try { combined = BuildCombines(f, data); }
                        finally { scope.Dispose(); }
                        data.GetRenderer(0).SetBlendShapeWeight(0, weight);
                        finalize.Invoke(null, new object[] { data });
                        return combined;
                    }

                    Assert.IsTrue(GeneratorBuild(kept, 100f));
                    Assert.That(recalculations, Is.EqualTo(1));
                    Vector3[] normalsAtFull = kept.GetRenderer(0).sharedMesh.normals;
                    Assert.That(Vector3.Distance(normalsAtFull[0], Vector3.forward), Is.GreaterThan(0.1f), "Sanity: the blendshape tilts the normal.");

                    // Same weights: neither combined nor recalculated.
                    Assert.IsFalse(GeneratorBuild(kept, 100f));
                    Assert.That(recalculations, Is.EqualTo(1));
                    Assert.That(kept.GetRenderer(0).sharedMesh.normals, Is.EqualTo(normalsAtFull));

                    // New weights: the mesh is kept, the normals are recalculated.
                    Assert.IsFalse(GeneratorBuild(kept, 40f));
                    Assert.That(recalculations, Is.EqualTo(2));

                    Assert.IsTrue(GeneratorBuild(fresh, 40f));
                    Assert.That(kept.GetRenderer(0).sharedMesh.normals, Is.EqualTo(fresh.GetRenderer(0).sharedMesh.normals));
                    Assert.That(kept.GetRenderer(0).sharedMesh.tangents, Is.EqualTo(fresh.GetRenderer(0).sharedMesh.tangents));
                    Assert.That(kept.GetRenderer(0).sharedMesh.vertices, Is.EqualTo(fresh.GetRenderer(0).sharedMesh.vertices));

                    // Back to no blendshape: again the same as a fresh combine.
                    Assert.IsFalse(GeneratorBuild(kept, 0f));
                    Assert.IsTrue(GeneratorBuild(fresh, 0f));
                    Assert.That(kept.GetRenderer(0).sharedMesh.normals, Is.EqualTo(fresh.GetRenderer(0).sharedMesh.normals));
                    Assert.That(kept.GetRenderer(0).sharedMesh.tangents, Is.EqualTo(fresh.GetRenderer(0).sharedMesh.tangents));
                }
            }
            finally
            {
                observerField.SetValue(null, null);
            }
        }

        // A direct combiner call (no generator around it) recalculates at once. Keeping the mesh must not skip that.
        [Test]
        public void DirectRebuildOfAKeptMeshStillChecksItsNormals()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                f.Slot.meshData.normals = Enumerable.Repeat(Vector3.right, 3).ToArray();
                var data = f.Avatar();
                data.defaultRendererAsset = RecalculatingAsset(f);
                Assert.IsTrue(BuildCombines(f, data));
                Mesh mesh = data.GetRenderer(0).sharedMesh;
                foreach (var normal in mesh.normals) Assert.That(Vector3.Distance(normal, Vector3.forward), Is.LessThan(.0001f));

                // Something overwrites only the normals. The geometry is untouched, so the mesh is kept, and the
                // recalculation notices the normals are no longer its own.
                mesh.normals = Enumerable.Repeat(Vector3.right, 3).ToArray();
                Assert.IsFalse(BuildCombines(f, data));
                foreach (var normal in mesh.normals) Assert.That(Vector3.Distance(normal, Vector3.forward), Is.LessThan(.0001f));
            }
        }

        // Renderers without Recalculate Normals are unaffected: they keep using the shared cache.
        [Test]
        public void OrdinaryRenderersStillShareThroughTheCache()
        {
            using (var f = new UMAResourceReuseMeshTests.Fixture())
            {
                var first = f.Avatar(); var second = f.Avatar();
                f.BuildJobified(first); f.BuildJobified(second);
                Assert.AreSame(first.GetRenderer(0).sharedMesh, second.GetRenderer(0).sharedMesh);
                Assert.IsTrue(UMAResourceLeaseOwner.IsSharedMesh(first.GetRenderer(0)));
                Assert.That(first.resourceReuseStatus, Is.Not.EqualTo(KeptStatus));
            }
        }
    }
}
#endif
