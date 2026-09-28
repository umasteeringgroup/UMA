#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Tests
{
    public class UMAResourceReuseMeshTests
    {
        [TestCase(1)] [TestCase(12)]
        public void LargeSourceMeshLookupHasBoundedTimeAndKeySize(int sourceCount)
        {
            using (var f = new Fixture())
            {
                const int vertexCount = 25000;
                var mesh = f.Slot.meshData;
                mesh.vertexCount = vertexCount;
                mesh.vertices = new Vector3[vertexCount];
                mesh.normals = new Vector3[vertexCount];
                mesh.tangents = new Vector4[vertexCount];
                mesh.uv = new Vector2[vertexCount];
                mesh.boneWeights = new UMABoneWeight[vertexCount];
                mesh.ManagedBoneWeights = new BoneWeight1[vertexCount * 4];
                mesh.ManagedBonesPerVertex = new byte[vertexCount];
                mesh.blendShapes = Enumerable.Range(0, 40).Select(i => new UMABlendShape {
                    shapeName = "Shape " + i, frames = new[] { new UMABlendFrame(vertexCount) }
                }).ToArray();
                var data = f.Avatar();
                var sources = Enumerable.Repeat(new SkinnedMeshCombiner.CombineInstance {
                    meshData = mesh, slotData = data.umaRecipe.slotDataList[0], targetSubmeshIndices = new[] { 0 }
                }, sourceCount).ToArray();
                var materials = data.generatedMaterials.materials.ToArray();
                Func<UMAGeneratedResourceKey> lookup = () => UMAResourceReuse.DescribeMesh(data, sources, materials, 256, Quaternion.identity);
                var coldTimer = System.Diagnostics.Stopwatch.StartNew();
                var first = lookup(); // Warm JIT and source fingerprints before measuring repeated requests.
                coldTimer.Stop();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) Assert.That(lookup(), Is.EqualTo(first));
                timer.Stop();
                int descriptionBytes = ((byte[])typeof(UMAGeneratedResourceKey).GetField("description",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(first)).Length;
                Debug.Log($"Mesh lookup: {timer.Elapsed.TotalMilliseconds / 100:F4} ms warm, {coldTimer.Elapsed.TotalMilliseconds:F2} ms first fingerprint, {descriptionBytes:N0} key bytes/request ({sourceCount} source entries, 25k vertices, 40 blendshapes).");
                Assert.That(timer.Elapsed.TotalMilliseconds / 100, Is.LessThan(.5), "Warm mesh lookup must stay below 0.5 ms.");
                Assert.That(descriptionBytes, Is.LessThan(32 * 1024), "Repeated keys must not duplicate source geometry.");
                mesh.blendShapes[39].frames[0].deltaVertices[vertexCount - 1] = Vector3.one;
                UMAResourceReuse.InvalidateMeshSource(mesh);
                Assert.That(lookup(), Is.Not.EqualTo(first), "Invalidated source changes must never reuse stale geometry.");
            }
        }

        [Test]
        public void CompactInputsShareUnchangedBuffersAndDetectMutableInputChanges()
        {
            using (var f = new Fixture())
            {
                var mesh = f.Slot.meshData;
                mesh.vertices = new Vector3[100];
                mesh.ManagedBoneWeights = new BoneWeight1[100];
                mesh.bindPoses = new Matrix4x4[4];
                var data = f.Avatar();
                var source = new SkinnedMeshCombiner.CombineInstance { meshData = mesh,
                    slotData = data.umaRecipe.slotDataList[0], targetSubmeshIndices = new[] { 0 } };
                Func<UMAGeneratedResourceKey> describe = () => UMAResourceReuse.DescribeMesh(data, new[] { source },
                    data.generatedMaterials.materials.ToArray(), 256, Quaternion.identity);
                var first = describe();
                var again = describe();
                var field = typeof(UMAGeneratedResourceKey).GetField("sourceKeys",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(((Array)field.GetValue(first)).GetValue(0), Is.SameAs(((Array)field.GetValue(again)).GetValue(0)));
                mesh.vertices = (Vector3[])mesh.vertices.Clone();
                UMAResourceReuse.InvalidateMeshSource(mesh);
                Assert.That(describe(), Is.EqualTo(first), "Equal contents in a different source buffer still match.");
                mesh.vertices[99] = Vector3.right;
                UMAResourceReuse.InvalidateMeshSource(mesh);
                var moved = describe();
                Assert.That(moved, Is.Not.EqualTo(first));
                Assert.That(again, Is.EqualTo(first), "Previously published keys must remain immutable.");
                mesh.ManagedBoneWeights[99] = new BoneWeight1 { boneIndex = 3, weight = .5f };
                UMAResourceReuse.InvalidateMeshSource(mesh);
                var reweighted = describe();
                Assert.That(reweighted, Is.Not.EqualTo(moved));
                mesh.bindPoses[3] = Matrix4x4.Translate(Vector3.up);
                UMAResourceReuse.InvalidateMeshSource(mesh);
                Assert.That(describe(), Is.Not.EqualTo(reweighted));
                var before = describe();
                data.generatedMaterials.materials[0].materialFragments[0].atlasRegion.width = 64;
                Assert.That(describe(), Is.Not.EqualTo(before));
                before = describe();
                data.currentLODLevel = 1;
                Assert.That(describe(), Is.Not.EqualTo(before));
            }
        }

        [Test]
        public void ModifierClonesReuseFingerprintsButScaleAndInvalidatedEditsDoNot()
        {
            using (var f = new Fixture())
            {
                var asset = f.Keep(ScriptableObject.CreateInstance<MeshModifier>());
                var adjustments = new VertexDeltaAdjustmentCollection();
                for (int i = 0; i < 1000; i++) adjustments.vertexAdjustments.Add(new VertexDeltaAdjustment { vertexIndex = i, delta = Vector3.up });
                asset.runtimeModifiers.Add(new MeshModifier.Modifier { SlotName = f.Slot.slotName, adjustments = adjustments });
                var data = f.Avatar();
                var slot = data.umaRecipe.slotDataList[0];
                var source = new SkinnedMeshCombiner.CombineInstance { meshData = f.Slot.meshData,
                    slotData = slot, targetSubmeshIndices = new[] { 0 }, applyMeshModifiersInJobs = true };
                Func<UMAGeneratedResourceKey> key = () => UMAResourceReuse.DescribeMesh(data, new[] { source },
                    data.generatedMaterials.materials.ToArray(), 256, Quaternion.identity);
                slot.meshModifiers = asset.GetScaledRuntimeModifiers(.25f);
                var quarter = key();
                slot.meshModifiers = asset.GetScaledRuntimeModifiers(.25f);
                Assert.That(key(), Is.EqualTo(quarter));
                var timer = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) key();
                timer.Stop();
                Assert.That(timer.Elapsed.TotalMilliseconds / 100, Is.LessThan(.5));
                slot.meshModifiers[0].Scale = .75f;
                Assert.That(key(), Is.Not.EqualTo(quarter));
                slot.meshModifiers[0].Scale = .25f;
                ((VertexDeltaAdjustment)slot.meshModifiers[0].adjustments.vertexAdjustments[0]).delta = Vector3.right;
                UMAResourceReuse.InvalidateMeshInputs();
                Assert.That(key(), Is.Not.EqualTo(quarter));
                Assert.That(((VertexDeltaAdjustment)adjustments.vertexAdjustments[0]).delta, Is.EqualTo(Vector3.up));
                slot.meshModifiers = asset.GetScaledRuntimeModifiers(.25f);
                var beforeEditorEdit = key();
                ((VertexDeltaAdjustment)adjustments.vertexAdjustments[0]).delta = Vector3.forward;
                UnityEditor.EditorUtility.SetDirty(asset);
                slot.meshModifiers = asset.GetScaledRuntimeModifiers(.25f);
                Assert.That(key(), Is.Not.EqualTo(beforeEditorEdit), "Editor modifier edits must refresh cloned input fingerprints.");
            }
        }

        [Test]
        public void RaceBakeCopiesUseCapturedParametersAndInvalidateSafely()
        {
            using (var f = new Fixture())
            {
                f.Slot.meshData.blendShapes = new[] { f.Slot.meshData.blendShapes[0], new UMABlendShape {
                    shapeName = "Blink", frames = new[] { new UMABlendFrame(3) { frameWeight = 100 } }
                } };
                var parameters = new SlotDataAsset.BakeSlotParams {
                    burnOptions = new List<SlotBurnOptions> { new SlotBurnOptions { BlendShape = "Smile", value = .25f } },
                    copyUnbakedBlendshapes = true, ShapesToInclude = new List<string> { "Blink" },
                    newSlotName = "Test baked slot", smoothingAngleDegrees = -1
                };
                var data = f.Avatar();
                var source = new SkinnedMeshCombiner.CombineInstance { targetSubmeshIndices = new[] { 0 } };
                Func<SlotDataAsset, UMAGeneratedResourceKey> key = slot => {
                    source.slotData = new SlotData(slot); source.meshData = slot.meshData;
                    return UMAResourceReuse.DescribeMesh(data, new[] { source }, data.generatedMaterials.materials.ToArray(), 256, Quaternion.identity);
                };
                var first = f.Keep(f.Slot.BakeNewSlotData(parameters));
                var firstKey = key(first);
                var same = f.Keep(f.Slot.BakeNewSlotData(parameters));
                Assert.That(key(same), Is.EqualTo(firstKey));
                CollectionAssert.AreEqual(first.meshData.vertices, same.meshData.vertices);
                Assert.That(first.meshData.blendShapes.Select(s => s.shapeName), Is.EqualTo(new[] { "Blink" }));
                parameters.burnOptions[0].value = .75f;
                var stronger = f.Keep(f.Slot.BakeNewSlotData(parameters));
                Assert.That(key(stronger), Is.Not.EqualTo(firstKey));
                Assert.That(stronger.meshData.vertices[0], Is.Not.EqualTo(first.meshData.vertices[0]));
                Assert.That(key(first), Is.EqualTo(firstKey), "Caller edits must not change already baked provenance.");
                parameters.burnOptions[0].value = .25f;
                parameters.ShapesToInclude[0] = "DoesNotMatchBlink";
                var filtered = f.Keep(f.Slot.BakeNewSlotData(parameters));
                Assert.That(key(filtered), Is.Not.EqualTo(firstKey));
                Assert.That(filtered.meshData.blendShapes, Is.Empty);
                parameters.ShapesToInclude[0] = "Blink";
                parameters.copyUnbakedBlendshapes = false;
                var stripped = f.Keep(f.Slot.BakeNewSlotData(parameters));
                Assert.That(key(stripped), Is.Not.EqualTo(firstKey));
                Assert.That(stripped.meshData.blendShapes, Is.Empty);
                first.meshData.vertices[0] = Vector3.forward;
                UMAResourceReuse.InvalidateMeshSource(first.meshData);
                var edited = key(first);
                Assert.That(edited, Is.Not.EqualTo(firstKey));
                Assert.That(edited, Is.Not.EqualTo(key(same)), "Invalidation must revoke bake provenance, not overwrite it with source settings.");
                f.Slot.meshData.vertices[0] = Vector3.left;
                UnityEditor.EditorUtility.SetDirty(f.Slot);
                var newBake = f.Keep(f.Slot.BakeNewSlotData(parameters));
                Assert.That(key(newBake), Is.Not.EqualTo(key(stripped)), "Editing the original asset must distinguish new and existing bake results.");
            }
        }

        [Test]
        public void OnlyUVRelevantOverlayChangesAffectMeshKeys()
        {
            using (var f = new Fixture())
            {
                var data = f.Avatar();
                var fragment = data.generatedMaterials.materials[0].materialFragments[0];
                var overlayAsset = f.Keep(ScriptableObject.CreateInstance<OverlayDataAsset>());
                overlayAsset.name = f.Slot.slotName + " overlay";
                overlayAsset.material = f.Material;
                var overlay = new OverlayData(overlayAsset) { rect = new Rect(1, 2, 32, 32) };
                fragment.overlayList.Add(overlay);
                var source = new SkinnedMeshCombiner.CombineInstance { meshData = f.Slot.meshData,
                    slotData = fragment.slotData, targetSubmeshIndices = new[] { 0 } };
                Func<UMAGeneratedResourceKey> key = () => UMAResourceReuse.DescribeMesh(data, new[] { source },
                    data.generatedMaterials.materials.ToArray(), 256, Quaternion.identity);
                var ordinary = key();
                overlay.rect = new Rect(3, 4, 64, 64);
                overlayAsset.name = "Different texture overlay";
                Assert.That(key(), Is.EqualTo(ordinary));
                fragment.isRectShared = true;
                f.Slot.useAtlasOverlay = true;
                var remapped = key();
                overlay.rect = new Rect(4, 5, 64, 64);
                Assert.That(key(), Is.Not.EqualTo(remapped));
                remapped = key();
                overlayAsset.name = f.Slot.slotName;
                Assert.That(key(), Is.Not.EqualTo(remapped));
            }
        }

        [Test]
        public void EditorDirtySourcesAndLiveTriangleMasksInvalidateMatches()
        {
            using (var f = new Fixture())
            {
                var data = f.Avatar();
                var mask = new System.Collections.BitArray(100001);
                var source = new SkinnedMeshCombiner.CombineInstance { meshData = f.Slot.meshData,
                    slotData = data.umaRecipe.slotDataList[0], targetSubmeshIndices = new[] { 0 },
                    triangleMask = new[] { mask } };
                Func<UMAGeneratedResourceKey> key = () => UMAResourceReuse.DescribeMesh(data, new[] { source },
                    data.generatedMaterials.materials.ToArray(), 256, Quaternion.identity);
                var original = key();
                mask[100000] = true;
                Assert.That(key(), Is.Not.EqualTo(original));
                mask[100000] = false;
                Assert.That(key(), Is.EqualTo(original));
                f.Slot.meshData.vertices[0] = Vector3.forward;
                UnityEditor.EditorUtility.SetDirty(f.Slot);
                Assert.That(key(), Is.Not.EqualTo(original), "Editor slot changes must invalidate fingerprints without a runtime API call.");
            }
        }

        internal sealed class Fixture : IDisposable
        {
            internal readonly List<UnityEngine.Object> Objects = new List<UnityEngine.Object>();
            internal readonly List<UMAData> Avatars = new List<UMAData>();
            internal readonly SlotDataAsset Slot;
            internal readonly RaceData Race;
            internal readonly UMAMaterial Material;
            internal Fixture(bool secondPass = false)
            {
                Slot = Keep(ScriptableObject.CreateInstance<SlotDataAsset>());
                Slot.name = "CacheTestSlot";
                Slot.meshData = new UMAMeshData
                {
                    SlotName = Slot.slotName, vertexCount = 3, subMeshCount = 1,
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    normals = Enumerable.Repeat(Vector3.forward, 3).ToArray(),
                    tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 3).ToArray(),
                    uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                    submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) },
                    bindPoses = new[] { Matrix4x4.identity },
                    boneNameHashes = new[] { UMAUtils.StringToHash("Global") },
                    umaBones = new[] { new UMATransform { name = "Global", hash = UMAUtils.StringToHash("Global"), scale = Vector3.one, rotation = Quaternion.identity } },
                    rootBoneHash = UMAUtils.StringToHash("Global"), RootBoneName = "Global", umaBoneCount = 1,
                    ManagedBonesPerVertex = new byte[] { 1, 1, 1 },
                    ManagedBoneWeights = Enumerable.Repeat(new BoneWeight1 { boneIndex = 0, weight = 1 }, 3).ToArray(),
                    blendShapes = new[] { new UMABlendShape { shapeName = "Smile", frames = new[] {
                        new UMABlendFrame { frameWeight = 100, deltaVertices = Enumerable.Repeat(Vector3.up * .1f, 3).ToArray() } } } }
                };
                Race = Keep(ScriptableObject.CreateInstance<RaceData>());
                Race.useNewDNA = false;
                Material = Keep(ScriptableObject.CreateInstance<UMAMaterial>());
                Material.materialType = UMAMaterial.MaterialType.Atlas;
                Material.material = Keep(new Material(Shader.Find("Hidden/InternalErrorShader")));
                if (secondPass)
                {
                    var so = new UnityEditor.SerializedObject(Material);
                    so.FindProperty("_secondPass").objectReferenceValue = Keep(new Material(Material.material));
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            internal T Keep<T>(T obj) where T : UnityEngine.Object { Objects.Add(obj); return obj; }
            internal UMAData Avatar(bool share = true)
            {
                var root = new GameObject("Reuse test avatar");
                var global = new GameObject("Global"); global.transform.SetParent(root.transform, false);
                var data = root.AddComponent<UMAData>();
                Avatars.Add(data);
                data.umaRoot = root; data.skeleton = new UMASkeleton(global.transform);
                data.reuseGeneratedMeshes = share;
                data.markNotReadable = false;
                var slot = new SlotData(Slot);
                data.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { slot } };
                data.umaRecipe.SetRace(Race);
                data.generatedMaterials.rendererAssets.Add(null);
                data.generatedMaterials.materials.Add(new UMAData.GeneratedMaterial
                {
                    umaMaterial = Material, material = Keep(new Material(Material.material)),
                    cropResolution = new Vector2(256, 256), resolutionScale = Vector2.one,
                    materialFragments = new List<UMAData.MaterialFragment> { new UMAData.MaterialFragment {
                        slotData = slot, atlasRegion = new Rect(0, 0, 256, 256), overlayList = slot.GetOverlayList() } }
                });
                root.AddComponent<UMAIncrementalMeshCombiner>();
                return data;
            }
            internal void Build(UMAData data) => data.GetComponent<UMAIncrementalMeshCombiner>().UpdateUMAMesh(true, data, 256);
            internal void BuildJobified(UMAData data)
            {
                var combiner = data.GetComponent<UMAJobifiedMeshCombiner>();
                if (combiner == null) combiner = data.gameObject.AddComponent<UMAJobifiedMeshCombiner>();
                combiner.UpdateUMAMesh(true, data, 256);
            }
            public void Dispose()
            {
                foreach (var avatar in Avatars)
                    if (avatar != null)
                    {
                        foreach (var gm in avatar.generatedMaterials.materials)
                            if (gm?.secondPassMaterial != null) Objects.Add(gm.secondPassMaterial);
                        UnityEngine.Object.DestroyImmediate(avatar.gameObject);
                    }
                foreach (var submesh in Slot.meshData.submeshes)
                    if (submesh.nativeTriangles.IsCreated) submesh.nativeTriangles.Dispose();
                for (int i = Objects.Count - 1; i >= 0; i--) if (Objects[i] != null) UnityEngine.Object.DestroyImmediate(Objects[i]);
                UMAMeshData.CleanupGlobalBuffers();
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void MatchingAvatarsShareMeshButNotBonesOrExpressionWeights(bool secondPass)
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture(secondPass))
            {
                var a = f.Avatar(); var b = f.Avatar();
                f.Build(a); f.Build(b);
                var ar = a.GetRenderer(0); var br = b.GetRenderer(0);
                Assert.AreSame(ar.sharedMesh, br.sharedMesh, b.resourceReuseStatus);
                Assert.AreNotSame(ar.bones[0], br.bones[0]);
                ar.SetBlendShapeWeight(0, 75); br.SetBlendShapeWeight(0, 20);
                Assert.AreEqual(75, ar.GetBlendShapeWeight(0)); Assert.AreEqual(20, br.GetBlendShapeWeight(0));
                Assert.AreEqual(secondPass ? 2 : 1, br.sharedMesh.subMeshCount);
                var mesh = br.sharedMesh;
                UnityEngine.Object.DestroyImmediate(a.gameObject);
                Assert.IsTrue(mesh != null);
                Assert.AreSame(mesh, br.sharedMesh);
            }
            Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
        }

        [Test]
        public void CacheOffAndCacheOnProduceIdenticalGeometryAndSkinning()
        {
            using (var f = new Fixture())
            {
                var baseline = f.Avatar(false); var cached = f.Avatar();
                f.Build(baseline); f.Build(cached);
                var a = baseline.GetRenderer(0).sharedMesh; var b = cached.GetRenderer(0).sharedMesh;
                CollectionAssert.AreEqual(a.vertices, b.vertices); CollectionAssert.AreEqual(a.normals, b.normals);
                CollectionAssert.AreEqual(a.uv, b.uv); CollectionAssert.AreEqual(a.triangles, b.triangles);
                CollectionAssert.AreEqual(a.bindposes, b.bindposes); Assert.AreEqual(a.blendShapeCount, b.blendShapeCount);
            }
        }

        [Test]
        public void ColorOnlyDifferencesShareMeshButAtlasLayoutAndGeometryChangesDoNot()
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); f.Build(a);
                var b = f.Avatar(); b.generatedMaterials.materials[0].material.color = Color.red; f.Build(b);
                Assert.AreSame(a.GetRenderer(0).sharedMesh, b.GetRenderer(0).sharedMesh);
                var c = f.Avatar(); c.generatedMaterials.materials[0].materialFragments[0].atlasRegion.width = 128; f.Build(c);
                Assert.AreNotSame(a.GetRenderer(0).sharedMesh, c.GetRenderer(0).sharedMesh);
                f.Slot.meshData.vertices[0] = Vector3.one;
                UMAResourceReuse.InvalidateMeshSource(f.Slot.meshData);
                var d = f.Avatar(); f.Build(d);
                Assert.AreNotSame(a.GetRenderer(0).sharedMesh, d.GetRenderer(0).sharedMesh);
            }
        }

        [Test]
        public void CopyOnWriteAndRebuildDoNotChangePeer()
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); var b = f.Avatar(); f.Build(a); f.Build(b);
                var shared = b.GetRenderer(0).sharedMesh;
                var unique = UMAResourceLeaseOwner.MakeMeshUnique(a.GetRenderer(0));
                Assert.AreNotSame(shared, unique);
                unique.vertices = Enumerable.Repeat(Vector3.one * 20, 3).ToArray();
                Assert.AreEqual(Vector3.zero, shared.vertices[0]);
                f.Build(a);
                Assert.AreSame(shared, a.GetRenderer(0).sharedMesh);
            }
        }

        [Test]
        public void ConcurrentRequestsGenerateOneMeshAndCancellationPromotesWaitingAvatar()
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); var b = f.Avatar(); var c = f.Avatar();
                long publications = UMAGeneratedResourceCache.Shared.Publications;
                long joins = UMAGeneratedResourceCache.Shared.PendingJoins;
                using (var first = a.GetComponent<UMAIncrementalMeshCombiner>().BeginUpdateUMAMesh(true, a, 256))
                using (var second = b.GetComponent<UMAIncrementalMeshCombiner>().BeginUpdateUMAMesh(true, b, 256))
                using (var third = c.GetComponent<UMAIncrementalMeshCombiner>().BeginUpdateUMAMesh(true, c, 256))
                {
                    for (int i = 0; i < 1000 && UMAGeneratedResourceCache.Shared.PendingJoins == joins; i++) { Step(first); Step(second); Step(third); }
                    Assert.Greater(UMAGeneratedResourceCache.Shared.PendingJoins, joins);
                    first.Cancel(); first.Dispose();
                    bool secondDone = false, thirdDone = false;
                    for (int i = 0; i < 100000 && (!secondDone || !thirdDone); i++)
                    {
                        if (!secondDone) secondDone = Step(second);
                        if (!thirdDone) thirdDone = Step(third);
                    }
                    Assert.IsTrue(secondDone && thirdDone);
                    Assert.AreSame(b.GetRenderer(0).sharedMesh, c.GetRenderer(0).sharedMesh);
                    Assert.AreEqual(publications + 1, UMAGeneratedResourceCache.Shared.Publications);
                }
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void BakedAndIncludedBlendshapesDistinguishMeshesButLiveWeightsDoNot(int backend)
        {
            var previousGenerator = UMAAssetIndexer.Instance.bareGenerator;
            try
            {
            using (var f = new Fixture())
            {
                var generatorObject = f.Keep(new GameObject("Mesh reuse test generator"));
                generatorObject.SetActive(false);
                UMAAssetIndexer.Instance.generator = generatorObject.AddComponent<UMAGenerator>();
                Action<UMAData> build = data =>
                {
                    if (backend == 0) f.Build(data);
                    else if (backend == 1) f.BuildJobified(data);
                    else data.gameObject.AddComponent<UMADefaultMeshCombiner>().UpdateUMAMesh(true, data, 256);
                };
                var a = f.Avatar();
                a.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData { isBaked = true, value = .25f };
                build(a);
                var b = f.Avatar();
                b.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData { isBaked = true, value = .75f };
                build(b);
                Assert.That(b.GetRenderer(0).sharedMesh, Is.Not.SameAs(a.GetRenderer(0).sharedMesh));
                Assert.That(b.GetRenderer(0).sharedMesh.vertices[0], Is.Not.EqualTo(a.GetRenderer(0).sharedMesh.vertices[0]));
                var sameBake = f.Avatar();
                sameBake.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData { isBaked = true, value = .25f };
                build(sameBake);
                Assert.That(sameBake.GetRenderer(0).sharedMesh, Is.SameAs(a.GetRenderer(0).sharedMesh));

                var live = f.Avatar(); var otherLive = f.Avatar();
                live.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData { value = .25f };
                otherLive.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData { value = .75f };
                build(live); build(otherLive);
                Assert.That(live.GetRenderer(0).sharedMesh, Is.SameAs(otherLive.GetRenderer(0).sharedMesh));
                Assert.That(live.GetRenderer(0).sharedMesh, Is.Not.SameAs(a.GetRenderer(0).sharedMesh));
                Assert.That(live.GetRenderer(0).sharedMesh.blendShapeCount, Is.EqualTo(1));

                var extra = new UMAMeshData { blendShapes = new[] { new UMABlendShape { shapeName = "Blink",
                    frames = new[] { new UMABlendFrame { frameWeight = 100, deltaVertices = new Vector3[3] } } } } };
                var included = f.Avatar();
                included.umaRecipe.BlendshapeSlots[f.Slot.slotName] = new List<UMAMeshData> { extra };
                build(included);
                Assert.That(included.GetRenderer(0).sharedMesh.blendShapeCount, Is.EqualTo(2));
                var renamed = f.Avatar();
                extra.blendShapes[0].shapeName = "Squint";
                UMAResourceReuse.InvalidateMeshSource(extra);
                renamed.umaRecipe.BlendshapeSlots[f.Slot.slotName] = new List<UMAMeshData> { extra };
                build(renamed);
                Assert.That(renamed.GetRenderer(0).sharedMesh, Is.Not.SameAs(included.GetRenderer(0).sharedMesh));
                Assert.That(renamed.GetRenderer(0).sharedMesh.GetBlendShapeIndex("Squint"), Is.GreaterThanOrEqualTo(0));
                Assert.That(included.GetRenderer(0).sharedMesh.GetBlendShapeIndex("Blink"), Is.GreaterThanOrEqualTo(0));
            }
            }
            finally { UMAAssetIndexer.Instance.generator = previousGenerator; }
        }

        [Test]
        public void IncludedNamesAndAllBlendshapeBuildOptionsParticipateInLookup()
        {
            using (var f = new Fixture())
            {
                var data = f.Avatar();
                var source = new SkinnedMeshCombiner.CombineInstance { meshData = f.Slot.meshData,
                    slotData = data.umaRecipe.slotDataList[0], targetSubmeshIndices = new[] { 0 } };
                Func<UMAGeneratedResourceKey> key = () => UMAResourceReuse.DescribeMesh(data, new[] { source },
                    data.generatedMaterials.materials.ToArray(), 256, Quaternion.identity);
                data.blendShapeSettings.blendShapes["Smile"] = new BlendShapeData();
                var smile = key();
                data.blendShapeSettings.blendShapes.Clear();
                data.blendShapeSettings.blendShapes["Blink"] = new BlendShapeData();
                Assert.That(key(), Is.Not.EqualTo(smile), "Equal inclusion counts with different names are not equivalent.");
                foreach (Action<BlendShapeSettings> change in new Action<BlendShapeSettings>[] {
                    s => s.ignoreBlendShapes = true, s => s.loadAllFrames = false,
                    s => s.loadNormals = false, s => s.loadTangents = false,
                    s => s.forceBakedBlendShapeValue = true, s => s.forcedBakedBlendShapeValue = .2f,
                    s => s.filteredBlendshapes.Add("Smile") })
                {
                    data.blendShapeSettings = new BlendShapeSettings();
                    var original = key();
                    change(data.blendShapeSettings);
                    Assert.That(key(), Is.Not.EqualTo(original));
                }
            }
        }

        private static bool Step(IUMAMeshCombineOperation operation)
        {
            var result = operation.Step(UMAMeshCombineTimeSlice.Unlimited);
            if (result.Status == UMAMeshCombineStatus.Failed) Assert.Fail(result.Error.ToString());
            return result.Status == UMAMeshCombineStatus.Completed;
        }
    }
}
#endif
