#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public class UMAResourceReuseAtlasTests
    {
        private delegate bool DescribeInputs(UMAData data, TextureMerge merge, UMAData.GeneratedMaterial generated,
            UMAMaterial material, int channel, UMAGeneratorBase generator, int width, int height,
            int outputWidth, int outputHeight, RenderTextureFormat format, bool convert, out UMAGeneratedResourceKey key);

        [Test]
        public void WarmEarlySignaturesAllocateNothingAndDoNotOverwriteStoredKeys()
        {
            var describe = (DescribeInputs)typeof(UMAResourceReuse).GetMethod("TryDescribeAtlasInputs",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .CreateDelegate(typeof(DescribeInputs));
            using (var f = new Fixture())
            {
                var a = f.Avatar(); f.Build(a);
                var gm = a.generatedMaterials.materials[0];
                bool Describe(out UMAGeneratedResourceKey key) => describe(a, f.Generator.textureMerge, gm,
                    f.Meshes.Material, 0, f.Generator, 32, 32, 32, 32, RenderTextureFormat.ARGB32, false, out key);
                Assert.That(Describe(out var probe), Is.True);
                var snapshot = (UMAGeneratedResourceKey)typeof(UMAGeneratedResourceKey)
                    .GetMethod("Freeze", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(probe, null);
                bool success = true;
                long before = GC.GetAllocatedBytesForCurrentThread();
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < 10000; i++) success &= Describe(out probe);
                double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(success, Is.True);
                Assert.That(allocated, Is.Zero, "Warm atlas signatures must not allocate byte arrays, strings or reflection boxes.");
                Assert.That(snapshot.Equals(probe), Is.True);
                f.Source.filterMode = FilterMode.Point;
                Describe(out probe);
                Assert.That(snapshot.Equals(probe), Is.False, "The frozen cache key must not follow changes to the scratch signature.");
                f.Source.filterMode = FilterMode.Bilinear;
                Describe(out probe);
                Assert.That(snapshot.Equals(probe), Is.True);
                Debug.Log($"Warm atlas input signature: {elapsed / 10000:F6} ms/request, {allocated} GC bytes over 10,000 requests.");
                foreach (var fragment in gm.materialFragments) fragment.isRectShared = true;
                Assert.That(Describe(out probe), Is.False, "A layout with no base draws must retain the detailed background-state path.");
            }
        }

        [TestCase("color")] [TestCase("additive")] [TestCase("sampling")]
        [TestCase("pixels")] [TestCase("revision")] [TestCase("invalidation")]
        [TestCase("prefill")] [TestCase("background")] [TestCase("size")]
        public void EarlyAtlasLookupRejectsChangedInputs(string change)
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); f.Build(a);
                var b = f.Avatar();
                switch (change)
                {
                    case "color": b.umaRecipe.slotDataList[0].GetOverlay(0).colorData.color = Color.red; break;
                    case "additive": b.umaRecipe.slotDataList[0].GetOverlay(0).colorData.channelAdditiveMask = new[] { Color.green }; break;
                    case "sampling": f.Source.filterMode = FilterMode.Point; break;
                    case "pixels": f.Source.SetPixel(0, 0, Color.blue); f.Source.Apply(); break;
                    case "revision": UMAResourceReuse.SetDependencyRevision(f.Source, 2); break;
                    case "invalidation": UMAResourceReuse.InvalidateTextureInputs(); break;
                    case "prefill": b.umaRecipe.slotDataList[0].GetOverlay(0).TransparentMultiplier = Color.red; break;
                    case "background": f.Meshes.Material.MaskWithCurrentColor = !f.Meshes.Material.MaskWithCurrentColor; break;
                    case "size": b.atlasResolutionScale = .5f; break;
                }
                f.Build(b);
                Assert.AreNotSame(a.generatedMaterials.materials[0].resultingAtlasList[0], b.generatedMaterials.materials[0].resultingAtlasList[0], change);
                long hits = f.Generator.atlasEarlyHits;
                var c = f.Avatar();
                c.atlasResolutionScale = b.atlasResolutionScale;
                c.umaRecipe.slotDataList[0].SetOverlayList(b.umaRecipe.slotDataList[0].GetOverlayList());
                f.Build(c);
                Assert.That(f.Generator.atlasEarlyHits, Is.EqualTo(hits + 1), "The unchanged repeat must take the early path: " + change);
            }
        }

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)]
        public void LayeredAndTransformedAtlasPixelsMatchReuseOff(bool transform, bool advanced)
        {
            using (var f = new Fixture())
            {
                var merge = f.Generator.textureMerge;
                merge.DiffuseBlendModeShaders.Add(new TextureMerge.BlendModeShaders { Combiner = merge.diffuseShader });
                merge.DiffuseBlendModeShaders.Add(new TextureMerge.BlendModeShaders {
                    BlendMode = (UnityEngine.Rendering.BlendOp)OverlayDataAsset.OverlayBlend.Multiply,
                    Combiner = Shader.Find("UMA/Atlas/AtlasDiffuseShader_Multiply") });
                var a = f.Avatar(); var b = f.Avatar(false); var c = f.Avatar();
                foreach (var avatar in new[] { a, b, c })
                {
                    var overlay = new OverlayData(f.Overlay);
                    overlay.colorData.color = new Color(.3f, .8f, .9f, .7f);
                    overlay.instanceTransformed = transform;
                    overlay.Rotation = 17;
                    overlay.Scale = new Vector3(.8f, .9f, 1);
                    overlay.Translate = new Vector2(.05f, .07f);
                    overlay.TransparentMultiplier = new Color(.2f, .1f, 0, .5f);
                    if (advanced) overlay.SetOverlayBlend(0, OverlayDataAsset.OverlayBlend.Multiply);
                    avatar.umaRecipe.slotDataList[0].AddOverlay(overlay);
                }
                f.Build(a); f.Build(b);
                long early = f.Generator.atlasEarlyHits;
                f.Build(c);
                Assert.That(f.Generator.atlasEarlyHits - early, Is.EqualTo(advanced ? 0 : 1));
                Assert.AreSame(a.generatedMaterials.materials[0].resultingAtlasList[0], c.generatedMaterials.materials[0].resultingAtlasList[0]);
                Color32[] Read(UMAData data)
                {
                    var image = f.Meshes.Keep(TextureMerge.GetRTPixels((RenderTexture)data.generatedMaterials.materials[0].resultingAtlasList[0]));
                    return image.GetPixels32();
                }
                CollectionAssert.AreEqual(Read(b), Read(c));
            }
        }

        private sealed class AtlasListener : ScriptableObject
        {
            public int Calls;
            public void OnAtlas(UMAData data, TextureEventParms parameters) { Calls++; }
        }

        [TestCase(false, false)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(true, true)]
        public void EmptyAtlasEventsShareLikeNullEvents(bool convert, bool removedListener)
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture(convert))
            {
                var first = f.Avatar(); first.AtlasUpdated = null;
                f.Build(first);
                var other = f.Avatar(); other.AtlasUpdated = new UMATextureEvent();
                if (removedListener)
                {
                    Action<UMAData, TextureEventParms> callback = (data, parameters) => Assert.Fail("Removed callback invoked");
                    other.OnAtlasUpdated += callback;
                    Assert.That(other.AtlasUpdated.HasListeners, Is.True);
                    other.OnAtlasUpdated -= callback;
                }
                Assert.That(other.AtlasUpdated.HasListeners, Is.False);
                var before = UMAGeneratedResourceCache.Shared.GetStatistics<UMACachedAtlas>();
                f.Build(other);
                var delta = UMAGeneratedResourceCache.ResourceStatistics.Since(
                    UMAGeneratedResourceCache.Shared.GetStatistics<UMACachedAtlas>(), before);
                Assert.That(delta.Hits, Is.EqualTo(1));
                Assert.That(delta.Bypasses, Is.Zero);
                Assert.AreSame(first.generatedMaterials.materials[0].resultingAtlasList[0],
                    other.generatedMaterials.materials[0].resultingAtlasList[0]);
            }
            Assert.That(UMAGeneratedResourceCache.Shared.EntryCount, Is.EqualTo(entries));
        }

        [TestCase(false)] [TestCase(true)]
        public void RuntimeAtlasListenersStayPrivateAndShareAfterRemoval(bool throughBaseType)
        {
            using (var f = new Fixture())
            {
                var first = f.Avatar(); first.AtlasUpdated = null; f.Build(first);
                var other = f.Avatar(); other.AtlasUpdated = new UMATextureEvent();
                int calls = 0;
                Action<UMAData, TextureEventParms> action = (data, parameters) => calls++;
                UnityAction<UMAData, TextureEventParms> listener = (data, parameters) => calls++;
                UnityEvent<UMAData, TextureEventParms> baseEvent = other.AtlasUpdated;
                if (throughBaseType) baseEvent.AddListener(listener);
                else other.OnAtlasUpdated += action;
                Assert.That(other.AtlasUpdated.GetPersistentEventCount(), Is.Zero);
                Assert.That(other.AtlasUpdated.HasListeners, Is.True);
                f.Build(other);
                Assert.That(calls, Is.GreaterThan(0), "Real callbacks must still execute on their private atlas.");
                Assert.AreNotSame(first.generatedMaterials.materials[0].resultingAtlasList[0],
                    other.generatedMaterials.materials[0].resultingAtlasList[0]);
                if (throughBaseType) baseEvent.RemoveListener(listener);
                else other.OnAtlasUpdated -= action;
                Assert.That(other.AtlasUpdated.HasListeners, Is.False);
                int previousCalls = calls;
                f.Build(other);
                Assert.That(calls, Is.EqualTo(previousCalls));
                Assert.AreSame(first.generatedMaterials.materials[0].resultingAtlasList[0],
                    other.generatedMaterials.materials[0].resultingAtlasList[0]);
            }
        }

        [Test]
        public void PersistentAtlasListenersStayPrivateEvenWhenRuntimeListenersAreCleared()
        {
            using (var f = new Fixture())
            {
                var first = f.Avatar(); first.AtlasUpdated = null; f.Build(first);
                var other = f.Avatar(); other.AtlasUpdated = new UMATextureEvent();
                var receiver = f.Meshes.Keep(ScriptableObject.CreateInstance<AtlasListener>());
                UnityEventTools.AddPersistentListener(other.AtlasUpdated, receiver.OnAtlas);
                other.AtlasUpdated.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
                other.AtlasUpdated.RemoveAllListeners();
                Assert.That(other.AtlasUpdated.HasListeners, Is.True);
                f.Build(other);
                Assert.That(receiver.Calls, Is.GreaterThan(0));
                Assert.AreNotSame(first.generatedMaterials.materials[0].resultingAtlasList[0],
                    other.generatedMaterials.materials[0].resultingAtlasList[0]);
                UnityEventTools.RemovePersistentListener(other.AtlasUpdated, 0);
                Assert.That(other.AtlasUpdated.HasListeners, Is.False);
                f.Build(other);
                Assert.AreSame(first.generatedMaterials.materials[0].resultingAtlasList[0],
                    other.generatedMaterials.materials[0].resultingAtlasList[0]);
            }
        }

        [Test]
        public void EmptyEventCheckDoesNotAllocateOrInvokeListeners()
        {
            var evt = new UMATextureEvent();
            Assert.That(evt.HasListeners, Is.False); // Warm the one-time delegate binding.
            bool any = false;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) any |= evt.HasListeners;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(any, Is.False);
            Assert.That(allocated, Is.Zero);
            evt.AddListener((data, parameters) => Assert.Fail("A listener check must not invoke callbacks"));
            Assert.That(evt.HasListeners, Is.True);
            evt.RemoveAllListeners();
            Assert.That(evt.HasListeners, Is.False);
        }

        internal sealed class Fixture : IDisposable
        {
            internal readonly UMAResourceReuseMeshTests.Fixture Meshes = new UMAResourceReuseMeshTests.Fixture();
            internal readonly UMAGenerator Generator;
            internal readonly OverlayDataAsset Overlay;
            internal readonly Texture2D Source;
            private readonly UMAGenerator previousGenerator;
            internal Fixture(bool convert = false, bool async = false)
            {
                Generator = Meshes.Keep(new GameObject("Atlas cache generator")).AddComponent<UMAGenerator>();
                Generator.enabled = false;
                previousGenerator = UMAAssetIndexer.Instance.bareGenerator;
                UMAAssetIndexer.Instance.generator = Generator;
                Generator.atlasResolution = 32;
                Generator.convertRenderTexture = convert;
                Generator.useAsyncConversion = async;
                Generator.convertMipMaps = false;
                var merge = Meshes.Keep(ScriptableObject.CreateInstance<TextureMerge>());
                Generator.textureMerge = merge;
                merge.diffuseShader = Shader.Find("UMA/Atlas/AtlasDiffuseShader");
                Assert.NotNull(merge.diffuseShader);
                merge.dataShader = merge.diffuseShader;
                merge.material = Meshes.Keep(new Material(merge.diffuseShader));
                Meshes.Material.material = Meshes.Keep(new Material(Shader.Find("Universal Render Pipeline/Lit")));
                Meshes.Material.channels = new[] { new UMAMaterial.MaterialChannel {
                    channelType = UMAMaterial.ChannelType.DiffuseTexture, materialPropertyName = "_BaseMap",
                    textureFormat = RenderTextureFormat.ARGB32 } };
                Meshes.Material.generateMipMaps = false;
                Source = Meshes.Keep(new Texture2D(32, 32, TextureFormat.RGBA32, false, true));
                var pixels = new Color[32 * 32];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color((i % 32) / 31f, .4f, .2f, 1);
                Source.SetPixels(pixels); Source.Apply();
                UMAResourceReuse.SetDependencyRevision(Source, 1);
                Overlay = Meshes.Keep(ScriptableObject.CreateInstance<OverlayDataAsset>());
                Overlay.name = "Cache overlay"; Overlay.material = Meshes.Material;
                Overlay.textureList = new Texture[] { Source };
            }
            internal UMAData Avatar(bool reuse = true, Color? color = null)
            {
                var data = Meshes.Avatar();
                data.generatedMaterials.materials.Clear();
                data.reuseGeneratedTextures = reuse;
                var overlay = new OverlayData(Overlay);
                overlay.colorData.color = color ?? Color.white;
                data.umaRecipe.slotDataList[0].AddOverlay(overlay);
                return data;
            }
            internal void Build(UMAData data) => new UMAGeneratorPro().ProcessTexture(Generator, data, false, 1);
            public void Dispose()
            {
                RenderTexToCPU.CleanupPendingCopies();
                foreach (var avatar in Meshes.Avatars) if (avatar != null) avatar.CleanTextures();
                UMAAssetIndexer.Instance.generator = previousGenerator;
                Meshes.Dispose();
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void IdenticalAtlasesBuildOnceAndSeparateMaterialParametersDoNotPreventSharing(bool convert)
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture(convert))
            {
                var a = f.Avatar(); var b = f.Avatar();
                f.Build(a);
                f.Meshes.Material.material.SetFloat("_Smoothness", .123f);
                long publications = UMAGeneratedResourceCache.Shared.Publications;
                f.Build(b);
                var ma = a.generatedMaterials.materials[0]; var mb = b.generatedMaterials.materials[0];
                Assert.AreSame(ma.resultingAtlasList[0], mb.resultingAtlasList[0], b.resourceReuseStatus);
                Assert.AreNotEqual(ma.material.GetFloat("_Smoothness"), mb.material.GetFloat("_Smoothness"));
                Assert.AreEqual(publications, UMAGeneratedResourceCache.Shared.Publications);
                Texture image = mb.resultingAtlasList[0];
                a.CleanTextures(); Assert.IsTrue(image != null);
                b.CleanTextures(); Assert.IsTrue(image == null || image is RenderTexture && !((RenderTexture)image).IsCreated());
            }
            Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount);
        }

        [Test]
        public void ColorAndSourceRevisionAndOutputSettingsMissButUnchangedRequestsHit()
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture())
            {
                var a = f.Avatar(); f.Build(a); var original = a.generatedMaterials.materials[0].resultingAtlasList[0];
                var b = f.Avatar(color: Color.red); f.Build(b);
                Assert.AreNotSame(original, b.generatedMaterials.materials[0].resultingAtlasList[0]);
                UMAResourceReuse.SetDependencyRevision(f.Source, 2);
                var c = f.Avatar(); f.Build(c);
                Assert.AreNotSame(original, c.generatedMaterials.materials[0].resultingAtlasList[0]);
                f.Meshes.Material.generateMipMaps = true;
                var d = f.Avatar(); f.Build(d);
                Assert.AreNotSame(c.generatedMaterials.materials[0].resultingAtlasList[0], d.generatedMaterials.materials[0].resultingAtlasList[0]);
                var e = f.Avatar(); f.Build(e);
                Assert.AreSame(d.generatedMaterials.materials[0].resultingAtlasList[0], e.generatedMaterials.materials[0].resultingAtlasList[0], e.resourceReuseStatus);
            }
            Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount);
        }

        [Test]
        public void CachedPixelsMatchTheUnmodifiedCompositor()
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); var b = f.Avatar(false);
                f.Build(a); f.Build(b);
                Texture2D Read(UMAData data)
                {
                    var active = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = (RenderTexture)data.generatedMaterials.materials[0].resultingAtlasList[0];
                        var t = f.Meshes.Keep(new Texture2D(32, 32, TextureFormat.RGBA32, false, true));
                        t.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); t.Apply(); return t;
                    }
                    finally { RenderTexture.active = active; }
                }
                CollectionAssert.AreEqual(Read(a).GetPixels32(), Read(b).GetPixels32());
            }
        }

        [Test]
        public void ThirtyIdenticalAvatarsShareOutputsAndMaterialChangesStillShareAtlases()
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture())
            {
                var first = f.Avatar(); f.Build(first); f.Meshes.Build(first);
                var renderer = first.GetRenderer(0);
                long publications = UMAGeneratedResourceCache.Shared.Publications;
                for (int i = 1; i < 30; i++)
                {
                    var other = f.Avatar(); f.Build(other); f.Meshes.Build(other);
                    Assert.AreSame(renderer.sharedMesh, other.GetRenderer(0).sharedMesh);
                    Assert.AreSame(renderer.sharedMaterial, other.GetRenderer(0).sharedMaterial);
                    Assert.AreNotSame(renderer.bones[0], other.GetRenderer(0).bones[0]);
                }
                Assert.AreEqual(publications, UMAGeneratedResourceCache.Shared.Publications, "No additional mesh, atlas, or material publications for matching appearances.");
                f.Meshes.Material.material.SetFloat("_Smoothness", .019f);
                var changed = f.Avatar(); f.Build(changed); f.Meshes.Build(changed);
                Assert.AreNotSame(renderer.sharedMaterial, changed.GetRenderer(0).sharedMaterial);
                Assert.AreSame(renderer.sharedMaterial.GetTexture("_BaseMap"), changed.GetRenderer(0).sharedMaterial.GetTexture("_BaseMap"));
                Assert.AreSame(renderer.sharedMesh, changed.GetRenderer(0).sharedMesh);
                Assert.AreEqual(publications + 1, UMAGeneratedResourceCache.Shared.Publications);
                first.CleanTextures();
                Assert.IsTrue(renderer.sharedMaterial != null, "Visible old renderers retain their resources during a rebuild.");
                Assert.IsTrue(renderer.sharedMaterial.GetTexture("_BaseMap") != null);
            }
            Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
        }

        [Test]
        public void MeshOnlyRebuildDoesNotMutateAnotherAvatarsMaterials()
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); var b = f.Avatar();
                f.Build(a); f.Meshes.Build(a); f.Build(b); f.Meshes.Build(b);
                var original = b.GetRenderer(0).sharedMaterial;
                f.Meshes.Build(a);
                Assert.AreSame(original, a.GetRenderer(0).sharedMaterial,
                    "GM=" + a.generatedMaterials.materials[0].material.name + " owner=" +
                    (a.generatedMaterials.materials[0].skinnedMeshRenderer == a.GetRenderer(0)) + "\n" + UMAGeneratedResourceCache.Shared.DescribeEntries());
                Assert.IsTrue(original != null);
            }
        }

        [UnityTest]
        public IEnumerator PendingReadbackIsSharedAndSurvivesProducerDestruction()
        {
            if (!SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("GPU readback unavailable on this test device.");
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture(true, true))
            {
                var a = f.Avatar(); var b = f.Avatar(); f.Build(a);
                long publications = UMAGeneratedResourceCache.Shared.Publications;
                f.Build(b);
                Assert.AreSame(a.generatedMaterials.materials[0].resultingAtlasList[0], b.generatedMaterials.materials[0].resultingAtlasList[0]);
                Assert.AreEqual(publications, UMAGeneratedResourceCache.Shared.Publications);
                a.CleanTextures(); UnityEngine.Object.DestroyImmediate(a.gameObject);
                AsyncGPUReadback.WaitAllRequests();
                yield return null;
                RenderTexToCPU.ApplyQueuedCopies(0);
                var gm = b.generatedMaterials.materials[0];
                Assert.IsInstanceOf<Texture2D>(gm.resultingAtlasList[0]);
                Assert.AreSame(gm.resultingAtlasList[0], gm.material.GetTexture("_BaseMap"));
                Assert.AreEqual(0, RenderTexToCPU.renderTexturesToCPU.Count);
            }
            Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount);
        }

        [TestCase(false)] [TestCase(true)]
        public void JobifiedBackendSharesOutputsAndDestroyedRenderersReleaseAllReferences(bool reverseDestruction)
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture())
            {
                var a = f.Avatar(); var b = f.Avatar();
                f.Build(a); f.Meshes.BuildJobified(a); f.Build(b); f.Meshes.BuildJobified(b);
                Assert.AreSame(a.GetRenderer(0).sharedMesh, b.GetRenderer(0).sharedMesh);
                Assert.AreSame(a.GetRenderer(0).sharedMaterial, b.GetRenderer(0).sharedMaterial);
                var first = reverseDestruction ? b : a; var last = reverseDestruction ? a : b;
                var material = last.GetRenderer(0).sharedMaterial;
                first.gameObject.SetActive(false); // pooling must retain visible bindings
                Assert.IsTrue(material != null);
                UnityEngine.Object.DestroyImmediate(first.gameObject);
                Assert.IsTrue(material != null);
                Assert.IsTrue(material.GetTexture("_BaseMap") != null);
                UnityEngine.Object.DestroyImmediate(last.gameObject);
                Assert.IsTrue(material == null);
                Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void DefaultCombinerSharesOutputsAndPreservesIndependentBones(bool useMeshAPI)
        {
            var settings = UMASettings.GetSettingsFromResources();
            bool previous = settings.useMeshAPICombiner;
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            try
            {
                settings.useMeshAPICombiner = useMeshAPI;
                using (var f = new Fixture())
                {
                    var a = f.Avatar(); var b = f.Avatar();
                    f.Build(a); a.gameObject.AddComponent<UMADefaultMeshCombiner>().UpdateUMAMesh(true, a, 256);
                    f.Build(b); b.gameObject.AddComponent<UMADefaultMeshCombiner>().UpdateUMAMesh(true, b, 256);
                    Assert.AreSame(a.GetRenderer(0).sharedMesh, b.GetRenderer(0).sharedMesh);
                    Assert.AreSame(a.GetRenderer(0).sharedMaterial, b.GetRenderer(0).sharedMaterial);
                    Assert.AreNotSame(a.GetRenderer(0).bones[0], b.GetRenderer(0).bones[0]);
                }
                Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
            }
            finally { settings.useMeshAPICombiner = previous; }
        }

        [Test]
        public void LostRenderTextureIsRegeneratedWithoutCorruptingOtherOwners()
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); f.Build(a);
                var lost = (RenderTexture)a.generatedMaterials.materials[0].resultingAtlasList[0];
                lost.Release();
                var b = f.Avatar(); f.Build(b);
                var replacement = (RenderTexture)b.generatedMaterials.materials[0].resultingAtlasList[0];
                Assert.AreNotSame(lost, replacement); Assert.IsTrue(replacement.IsCreated());
                a.CleanTextures();
                var c = f.Avatar(); f.Build(c);
                Assert.AreSame(replacement, c.generatedMaterials.materials[0].resultingAtlasList[0]);
            }
        }

        [Test]
        public void ExplicitPerRendererEditsAndStaticConversionLeavePeersUnchanged()
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture())
            {
                var a = f.Avatar(); var b = f.Avatar();
                f.Build(a); f.Meshes.Build(a); f.Build(b); f.Meshes.Build(b);
                var renderer = a.GetRenderer(0); var other = b.GetRenderer(0);
                UMAResourceLeaseOwner.MakeMaterialsUnique(renderer);
                renderer.sharedMaterial.SetFloat("_Smoothness", .009f);
                Assert.AreNotEqual(renderer.sharedMaterial.GetFloat("_Smoothness"), other.sharedMaterial.GetFloat("_Smoothness"));
                var texture = UMAResourceLeaseOwner.MakeTextureUnique(renderer, 0, "_BaseMap");
                Assert.AreNotSame(texture, other.sharedMaterial.GetTexture("_BaseMap"));
                a.staticCharacter = true;
                // Invoke the same destruction callback in EditMode (UMAData is not ExecuteAlways).
                typeof(UMAData).GetMethod("OnDestroy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(a, null);
                UnityEngine.Object.DestroyImmediate(a);
                Assert.IsTrue(renderer.sharedMesh != null);
                Assert.IsTrue(renderer.sharedMaterial != null);
                UnityEngine.Object.DestroyImmediate(renderer.transform.root.gameObject);
                Assert.IsTrue(other.sharedMesh != null);
                Assert.IsTrue(other.sharedMaterial.GetTexture("_BaseMap") != null);
            }
            Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
        }

        [Test]
        public void AssemblyReloadDetachesVisibleResourcesWithoutLeakingCacheEntries()
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture())
            {
                var a = f.Avatar(); f.Build(a); f.Meshes.Build(a);
                var renderer = a.GetRenderer(0);
                var owner = renderer.GetComponent<UMAResourceLeaseOwner>();
                typeof(UMAResourceLeaseOwner).GetMethod("DetachForAssemblyReload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(owner, null);
                Assert.IsFalse(UMAResourceLeaseOwner.IsSharedMesh(renderer));
                Assert.IsTrue(renderer.sharedMesh != null);
                Assert.IsTrue(renderer.sharedMaterial != null);
                Assert.IsTrue(renderer.sharedMaterial.GetTexture("_BaseMap") != null);
                Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ClonedStaticRendererRetainsSharedAtlasWithSharedOrPrivateMaterials(bool privateMaterials)
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new Fixture())
            {
                var a = f.Avatar(); f.Build(a); f.Meshes.Build(a);
                var renderer = a.GetRenderer(0);
                if (privateMaterials) UMAResourceLeaseOwner.MakeMaterialsUnique(renderer);
                var atlas = renderer.sharedMaterial.GetTexture("_BaseMap");
                var clone = f.Meshes.Keep(UnityEngine.Object.Instantiate(renderer.gameObject));
                var other = clone.GetComponent<SkinnedMeshRenderer>();
                Assert.AreSame(renderer.sharedMesh, other.sharedMesh);
                Assert.AreSame(atlas, other.sharedMaterial.GetTexture("_BaseMap"));
                UnityEngine.Object.DestroyImmediate(a.gameObject);
                Assert.IsTrue(atlas != null);
                Assert.IsTrue(other.sharedMesh != null);
                Assert.IsTrue(other.sharedMaterial != null);
                UnityEngine.Object.DestroyImmediate(clone);
                Assert.AreEqual(entries, UMAGeneratedResourceCache.Shared.EntryCount, UMAGeneratedResourceCache.Shared.DescribeEntries());
            }
        }

        [Test]
        public void UnsupportedMaterialPropertiesDoNotPreventAtlasSharing()
        {
            using (var f = new Fixture())
            {
                var a = f.Avatar(); var b = f.Avatar();
                foreach (var data in new[] { a, b })
                    data.umaRecipe.slotDataList[0].GetOverlay(0).colorData.PropertyBlock = new UMAMaterialPropertyBlock
                    {
                        shaderProperties = new System.Collections.Generic.List<UMAProperty> {
                            new UMAComputeBufferProperty { name = "_UnspecifiedCustomBuffer" } }
                    };
                f.Build(a); f.Meshes.Build(a); f.Build(b); f.Meshes.Build(b);
                Assert.AreNotSame(a.GetRenderer(0).sharedMaterial, b.GetRenderer(0).sharedMaterial);
                Assert.AreSame(a.GetRenderer(0).sharedMaterial.GetTexture("_BaseMap"), b.GetRenderer(0).sharedMaterial.GetTexture("_BaseMap"));
            }
        }
    }
}
#endif
