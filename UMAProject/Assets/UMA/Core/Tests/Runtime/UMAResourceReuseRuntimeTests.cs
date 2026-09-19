#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public class UMAResourceReuseRuntimeTests
    {
        [Test]
        public void AtlasEventDetectsBaseTypedRuntimeSubscriptionsAndLastRemoval()
        {
            var evt = new UMATextureEvent();
            Assert.That(evt.HasListeners, Is.False);
            UnityEngine.Events.UnityEvent<UMAData, TextureEventParms> baseEvent = evt;
            int calls = 0;
            UnityEngine.Events.UnityAction<UMAData, TextureEventParms> callback = (data, parameters) => calls++;
            baseEvent.AddListener(callback);
            baseEvent.AddListener(callback);
            Assert.That(evt.HasListeners, Is.True);
            Assert.That(calls, Is.Zero, "Listener inspection must not invoke callbacks.");
            baseEvent.RemoveListener(callback);
            Assert.That(evt.HasListeners, Is.False, "Unity removes all occurrences of the same listener.");
            baseEvent.AddListener(callback);
            baseEvent.RemoveAllListeners();
            Assert.That(evt.HasListeners, Is.False);
        }

        private readonly List<GameObject> avatars = new List<GameObject>();
        private readonly List<Object> assets = new List<Object>();
        private SlotDataAsset slot;
        private RaceData race;
        private UMAMaterial material;
        private int baseline;

        [SetUp]
        public void SetUp()
        {
            baseline = UMAGeneratedResourceCache.Shared.EntryCount;
            slot = Keep(ScriptableObject.CreateInstance<SlotDataAsset>()); slot.name = "RuntimeReuseSlot";
            int global = UMAUtils.StringToHash("Global");
            slot.meshData = new UMAMeshData
            {
                SlotName = slot.slotName, vertexCount = 3, subMeshCount = 1,
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = Enumerable.Repeat(Vector3.forward, 3).ToArray(),
                tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 3).ToArray(),
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2 }) },
                bindPoses = new[] { Matrix4x4.identity }, boneNameHashes = new[] { global },
                umaBones = new[] { new UMATransform { name = "Global", hash = global, scale = Vector3.one, rotation = Quaternion.identity } },
                rootBoneHash = global, RootBoneName = "Global", umaBoneCount = 1,
                ManagedBonesPerVertex = new byte[] { 1, 1, 1 },
                ManagedBoneWeights = Enumerable.Repeat(new BoneWeight1 { boneIndex = 0, weight = 1 }, 3).ToArray()
            };
            race = Keep(ScriptableObject.CreateInstance<RaceData>()); race.useNewDNA = false;
            material = Keep(ScriptableObject.CreateInstance<UMAMaterial>());
            material.materialType = UMAMaterial.MaterialType.Atlas;
            material.material = Keep(new Material(Shader.Find("Hidden/InternalErrorShader")));
        }
        private T Keep<T>(T value) where T : Object { assets.Add(value); return value; }
        private UMAData Avatar()
        {
            var root = new GameObject("Runtime reuse avatar"); avatars.Add(root);
            var global = new GameObject("Global"); global.transform.SetParent(root.transform, false);
            var data = root.AddComponent<UMAData>();
            data.umaRoot = root; data.skeleton = new UMASkeleton(global.transform);
            data.reuseGeneratedMeshes = true; data.markNotReadable = false;
            var instance = new SlotData(slot);
            data.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { instance } }; data.umaRecipe.SetRace(race);
            data.generatedMaterials.rendererAssets.Add(null);
            data.generatedMaterials.materials.Add(new UMAData.GeneratedMaterial
            {
                umaMaterial = material, material = new Material(material.material), resultingAtlasList = new Texture[0],
                cropResolution = new Vector2(32, 32), resolutionScale = Vector2.one,
                materialFragments = new List<UMAData.MaterialFragment> { new UMAData.MaterialFragment
                { slotData = instance, atlasRegion = new Rect(0, 0, 32, 32), overlayList = instance.GetOverlayList() } }
            });
            root.AddComponent<UMAIncrementalMeshCombiner>(); return data;
        }
        private void Build(UMAData data) => data.GetComponent<UMAIncrementalMeshCombiner>().UpdateUMAMesh(true, data, 32);

        [UnityTest]
        public IEnumerator DeferredDestructionAndPoolingRetainPeersUntilLastOwner()
        {
            var a = Avatar(); var b = Avatar(); Build(a); Build(b);
            var shared = a.GetRenderer(0).sharedMesh;
            Assert.AreSame(shared, b.GetRenderer(0).sharedMesh);
            a.gameObject.SetActive(false); yield return null;
            Assert.IsTrue(shared != null);
            Object.Destroy(a.gameObject); yield return null;
            Assert.IsTrue(shared != null);
            b.GetRenderer(0).bones[0].localRotation = Quaternion.Euler(0, 30, 0);
            Assert.AreSame(shared, b.GetRenderer(0).sharedMesh);
            Object.Destroy(b.gameObject); yield return null; yield return null;
            Assert.IsTrue(shared == null);
            Assert.AreEqual(baseline, UMAGeneratedResourceCache.Shared.EntryCount);
        }

        [UnityTest]
        public IEnumerator StaticConversionAndCopyBeforeEditSurviveAcrossFrames()
        {
            var a = Avatar(); var b = Avatar(); Build(a); Build(b);
            var renderer = a.GetRenderer(0); var peerMesh = b.GetRenderer(0).sharedMesh;
            var privateMesh = UMAResourceLeaseOwner.MakeMeshUnique(renderer);
            var vertices = privateMesh.vertices; vertices[0] += Vector3.one; privateMesh.vertices = vertices;
            Assert.AreNotEqual(privateMesh.vertices[0], peerMesh.vertices[0]);
            a.staticCharacter = true; Object.Destroy(a); yield return null;
            Assert.IsTrue(renderer.sharedMesh != null);
            Assert.IsTrue(peerMesh != null);
            Object.Destroy(renderer.transform.root.gameObject); yield return null; yield return null;
            Assert.IsTrue(privateMesh == null);
            Assert.IsTrue(peerMesh != null);
        }

        [UnityTest]
        public IEnumerator InstantiatedStaticNpcRetainsSharedMeshAndOwnsPrivateMaterialCopies()
        {
            var source = Avatar(); Build(source);
            source.staticCharacter = true;
            var sourceRoot = source.gameObject; var originalRenderer = source.GetRenderer(0);
            var originalMaterial = originalRenderer.sharedMaterial;
            var mesh = originalRenderer.sharedMesh;
            Object.Destroy(source); yield return null;
            var clone = Object.Instantiate(sourceRoot); avatars.Add(clone);
            var cloneRenderer = clone.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.AreSame(mesh, cloneRenderer.sharedMesh);
            Assert.AreNotSame(originalMaterial, cloneRenderer.sharedMaterial);
            Object.Destroy(sourceRoot); yield return null; yield return null;
            Assert.IsTrue(mesh != null);
            Assert.IsTrue(cloneRenderer.sharedMaterial != null);
            Object.Destroy(clone); yield return null; yield return null;
            Assert.IsTrue(mesh == null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var avatar in avatars) if (avatar != null) Object.Destroy(avatar);
            avatars.Clear(); yield return null; yield return null;
            foreach (var triangles in slot.meshData.submeshes)
                if (triangles.nativeTriangles.IsCreated) triangles.nativeTriangles.Dispose();
            foreach (var asset in assets) if (asset != null) Object.Destroy(asset);
            assets.Clear(); yield return null;
            Assert.AreEqual(baseline, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
        }
    }
}
#endif
