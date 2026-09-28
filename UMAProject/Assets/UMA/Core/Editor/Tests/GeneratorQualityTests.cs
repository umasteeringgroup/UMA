using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UMA.Editors.Tests;
using UMA.Examples;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class GeneratorQualityTests
    {
        private readonly List<Object> objects = new List<Object>();
        private T Asset<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); objects.Add(value); return value; }
        private static GeneratorQualityValue<T> Override<T>(T value) => new GeneratorQualityValue<T> { overrideValue = true, value = value };
        private SchedulerTestGenerator Generator()
        {
            var go = new GameObject("Quality test generator"); objects.Add(go);
            var generator = go.AddComponent<SchedulerTestGenerator>(); generator.enabled = false;
            generator.atlasResolution = 512; return generator;
        }
        [TearDown] public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }
        [Test]
        public void MappingUsesNamesAndSpecificityWithDeterministicDuplicates()
        {
            var settings = Asset<GeneratorQualitySettings>();
            var fallback = Asset<GeneratorQualityProfile>(); var globalHigh = Asset<GeneratorQualityProfile>();
            var android = Asset<GeneratorQualityProfile>(); var androidHigh = Asset<GeneratorQualityProfile>();
            settings.defaultProfile = fallback;
            settings.entries.Add(new GeneratorQualitySettings.Entry { qualityLevel = "High", profile = globalHigh });
            settings.entries.Add(new GeneratorQualitySettings.Entry { anyPlatform = false, platform = RuntimePlatform.Android, profile = android });
            settings.entries.Add(new GeneratorQualitySettings.Entry { anyPlatform = false, platform = RuntimePlatform.Android, qualityLevel = "High", profile = androidHigh });
            settings.entries.Add(new GeneratorQualitySettings.Entry { anyPlatform = false, platform = RuntimePlatform.Android, qualityLevel = "High", profile = fallback });
            Assert.That(settings.Resolve(RuntimePlatform.Android, "High"), Is.SameAs(androidHigh));
            Assert.That(settings.Resolve(RuntimePlatform.Android, "Low"), Is.SameAs(android));
            Assert.That(settings.Resolve(RuntimePlatform.WindowsPlayer, "High"), Is.SameAs(globalHigh));
            Assert.That(settings.Resolve(RuntimePlatform.WindowsPlayer, "Renamed"), Is.SameAs(fallback));
        }
        [Test]
        public void ProfileSerializationKeepsInheritAndExplicitClearDistinct()
        {
            var a = Asset<GeneratorQualityProfile>(); var b = Asset<GeneratorQualityProfile>();
            a.atlasResolution = Override(2048); a.defaultRendererAsset = Override<UMARendererAsset>(null);
            a.characters.reuseMeshes = Override(true); a.textures.maximumDimension = Override(1024);
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(a), b);
            Assert.That(b.atlasResolution.overrideValue, Is.True); Assert.That(b.atlasResolution.value, Is.EqualTo(2048));
            Assert.That(b.defaultRendererAsset.overrideValue, Is.True); Assert.That(b.defaultRendererAsset.value, Is.Null);
            Assert.That(b.textureMerge.overrideValue, Is.False); Assert.That(b.textures.maximumDimension.value, Is.EqualTo(1024));
            Assert.That(b.characters.reuseMeshes.value, Is.True);
        }
        [Test]
        public void LayersRestoreCurrentLowerPriorityInsteadOfStaleSnapshots()
        {
            var generator = Generator(); var baselineRenderer = Asset<UMARendererAsset>(); generator.defaultRendererAsset = baselineRenderer;
            var lower = Asset<GeneratorQualityProfile>(); lower.atlasResolution = Override(1024);
            var upper = Asset<GeneratorQualityProfile>(); upper.atlasResolution = Override(2048); upper.defaultRendererAsset = Override<UMARendererAsset>(null);
            GeneratorQualityRuntime.SetProfile(generator, lower, lower, 0);
            GeneratorQualityRuntime.SetProfile(generator, upper, upper, 100);
            Assert.That(generator.atlasResolution, Is.EqualTo(2048)); Assert.That(generator.defaultRendererAsset, Is.Null);
            lower.atlasResolution = Override(4096); GeneratorQualityRuntime.SetProfile(generator, lower, lower);
            Assert.That(generator.atlasResolution, Is.EqualTo(2048));
            GeneratorQualityRuntime.Remove(generator, upper);
            Assert.That(generator.atlasResolution, Is.EqualTo(4096)); Assert.That(generator.defaultRendererAsset, Is.SameAs(baselineRenderer));
            GeneratorQualityRuntime.Remove(generator, lower);
            Assert.That(generator.atlasResolution, Is.EqualTo(512)); Assert.That(generator.defaultRendererAsset, Is.SameAs(baselineRenderer));
        }
        [Test]
        public void EqualPrioritiesUseActivationOrderNotRefreshOrder()
        {
            var generator = Generator(); var a = Asset<GeneratorQualityProfile>(); var b = Asset<GeneratorQualityProfile>();
            a.atlasResolution = Override(1024); b.atlasResolution = Override(2048);
            GeneratorQualityRuntime.SetProfile(generator, a, a); GeneratorQualityRuntime.SetProfile(generator, b, b);
            GeneratorQualityRuntime.SetProfile(generator, a, a);
            Assert.That(generator.atlasResolution, Is.EqualTo(2048));
            GeneratorQualityRuntime.Remove(generator, a); Assert.That(generator.atlasResolution, Is.EqualTo(2048));
            GeneratorQualityRuntime.Remove(generator, b); Assert.That(generator.atlasResolution, Is.EqualTo(512));
        }
        [Test]
        public void RequestsAndRestoresWaitForActiveBuildAndOutstandingReadbacks()
        {
            var generator = Generator(); var profile = Asset<GeneratorQualityProfile>(); profile.atlasResolution = Override(2048);
            var depth = typeof(UMAGeneratorBuiltin).GetField("qualityBuildDepth", BindingFlags.NonPublic | BindingFlags.Instance);
            depth.SetValue(generator, 1);
            GeneratorQualityRuntime.SetProfile(generator, profile, profile);
            Assert.That(generator.atlasResolution, Is.EqualTo(512)); Assert.That(GeneratorQualityRuntime.IsPending(generator), Is.True);
            depth.SetValue(generator, 0);
            var key = default(UMAObjectId);
            RenderTexToCPU.renderTexturesToCPU.Add(key, null);
            try { Assert.That(GeneratorQualityRuntime.ApplyPending(generator), Is.False); Assert.That(generator.atlasResolution, Is.EqualTo(512)); }
            finally { RenderTexToCPU.renderTexturesToCPU.Remove(key); }
            Assert.That(GeneratorQualityRuntime.ApplyPending(generator), Is.True); Assert.That(generator.atlasResolution, Is.EqualTo(2048));
            depth.SetValue(generator, 1); GeneratorQualityRuntime.Remove(generator, profile);
            Assert.That(generator.atlasResolution, Is.EqualTo(2048));
            depth.SetValue(generator, 0); GeneratorQualityRuntime.ApplyPending(generator);
            Assert.That(generator.atlasResolution, Is.EqualTo(512));
        }
        [Test]
        public void HardwareScalingIsResolvedOncePerSelectionWithoutChangingProfile()
        {
            var profile = Asset<GeneratorQualityProfile>(); profile.AutomaticScaling = Override(true); profile.atlasResolution = Override(2048);
            var baseline = new GeneratorQualityConfiguration();
            for (int i = 0; i < 3; i++)
            {
                var result = baseline.Clone(); profile.Overlay(result); result.ValidateAndResolveHardware(512, 256);
                Assert.That(result.atlasResolution, Is.EqualTo(1024)); Assert.That(result.InitialScaleFactor, Is.EqualTo(2));
                Assert.That(result.AutomaticScaling, Is.False);
            }
            Assert.That(profile.atlasResolution.value, Is.EqualTo(2048));
            var unknown = baseline.Clone(); profile.Overlay(unknown); unknown.ValidateAndResolveHardware(0, 0);
            Assert.That(unknown.atlasResolution, Is.EqualTo(2048));
        }
        [Test]
        public void SchedulingOnlyChangesDoNotChangeOutputOrMutateAssets()
        {
            var generator = Generator(); var before = GeneratorQualityConfiguration.Capture(generator); var profile = Asset<GeneratorQualityProfile>();
            profile.IterationCount = Override(4); profile.MaxMultiStepWorkMilliseconds = Override(.5f); profile.InterFrameDelay = Override(2);
            var after = before.Clone(); profile.Overlay(after);
            Assert.That(after.SameOutput(before), Is.True);
            profile.enableSourceUVCropping = Override(true); profile.Overlay(after);
            Assert.That(after.SameOutput(before), Is.False);
        }
        [Test]
        public void CharacterPoliciesPreserveExplicitReuseAndRestoreLodAndBlendshapeSettings()
        {
            var generator = Generator(); var profile = Asset<GeneratorQualityProfile>();
            profile.characters.reuseMeshes = Override(false); profile.characters.lodDistance = Override(12f);
            profile.characters.ignoreBlendShapes = Override(true);
            GeneratorQualityRuntime.SetProfile(generator, profile, profile);
            var go = new GameObject("Quality character"); go.SetActive(false); objects.Add(go);
            var data = go.AddComponent<UMAData>(); data.reuseGeneratedMeshes = true;
            var lod = go.AddComponent<UMASimpleLOD>(); float original = lod.lodDistance;
            var beforeBuild = typeof(GeneratorQualityRuntime).GetMethod("BeforeBuild", BindingFlags.NonPublic | BindingFlags.Static);
            beforeBuild.Invoke(null, new object[] { generator, data });
            Assert.That(data.reuseGeneratedMeshes, Is.True); Assert.That(lod.lodDistance, Is.EqualTo(12)); Assert.That(data.blendShapeSettings.ignoreBlendShapes, Is.True);
            data.inheritGeneratorReusePolicy = true; beforeBuild.Invoke(null, new object[] { generator, data });
            Assert.That(data.reuseGeneratedMeshes, Is.False);
            GeneratorQualityRuntime.Remove(generator, profile);
            Assert.That(data.reuseGeneratedMeshes, Is.True); Assert.That(lod.lodDistance, Is.EqualTo(original)); Assert.That(data.blendShapeSettings.ignoreBlendShapes, Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void TexturePoliciesShareEquivalentOutputsAndRejectDifferentSamplingAndSizes(bool converted)
        {
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new UMAResourceReuseAtlasTests.Fixture(converted))
            {
                var profile = Asset<GeneratorQualityProfile>(); profile.textures.filterMode = Override(FilterMode.Point);
                profile.textures.anisotropy = Override(2); profile.textures.mipMaps = Override(true);
                string source = EditorJsonUtility.ToJson(f.Meshes.Material);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile);
                var a = f.Avatar(); f.Build(a); var original = a.generatedMaterials.materials[0].resultingAtlasList[0];
                Assert.That(original.filterMode, Is.EqualTo(FilterMode.Point)); Assert.That(original.anisoLevel, Is.EqualTo(2));
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    // Some devices cannot directly read back the converted BGRA Texture2D.
                    // Sample at half resolution into an RGBA render target (selects mip 1).
                    var previous = RenderTexture.active;
                    var sample = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    try
                    {
                        Graphics.Blit(original, sample);
                        var mipReadback = AsyncGPUReadback.Request(sample, 0, TextureFormat.RGBA32);
                        mipReadback.WaitForCompletion();
                        Assert.That(mipReadback.hasError, Is.False);
                        var pixels = mipReadback.GetData<Color32>(); int green = 0;
                        foreach (var pixel in pixels) green += pixel.g;
                        Assert.That(green / (float)pixels.Length, Is.GreaterThan(10), "Minified atlas sampling must not be uninitialized black.");
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(sample); }
                }
                var equivalent = Asset<GeneratorQualityProfile>(); EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(profile), equivalent); equivalent.name = "Different profile name";
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, equivalent);
                var b = f.Avatar(); f.Build(b); Assert.That(b.generatedMaterials.materials[0].resultingAtlasList[0], Is.SameAs(original));
                equivalent.textures.filterMode = Override(FilterMode.Trilinear);
                equivalent.textures.maximumDimension = Override(16);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, equivalent);
                var c = f.Avatar(); f.Build(c); var changed = c.generatedMaterials.materials[0].resultingAtlasList[0];
                Assert.That(changed, Is.Not.SameAs(original)); Assert.That(changed.width, Is.EqualTo(16));
                Assert.That(changed.filterMode, Is.EqualTo(FilterMode.Trilinear)); Assert.That(original.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(original.width, Is.EqualTo(32)); Assert.That(EditorJsonUtility.ToJson(f.Meshes.Material), Is.EqualTo(source));
            }
            Assert.That(UMAGeneratedResourceCache.Shared.EntryCount, Is.EqualTo(entries));
        }
        [Test]
        public void TextureSizeLimitKeepsAspectRatioAndCompressionFallsBackSafely()
        {
            var policy = new GeneratorTextureQuality { maximumDimension = Override(1024), compression = Override(GeneratorRuntimeCompression.Fast) };
            int width = 4096, height = 2048; policy.LimitSize(ref width, ref height);
            Assert.That(width, Is.EqualTo(1024)); Assert.That(height, Is.EqualTo(512));
            Assert.That(policy.EffectiveCompression(false, RenderTextureFormat.ARGB32, 32, 32), Is.EqualTo(GeneratorRuntimeCompression.Disabled));
            Assert.That(policy.EffectiveCompression(true, RenderTextureFormat.ARGBHalf, 32, 32), Is.EqualTo(GeneratorRuntimeCompression.Disabled));
            Assert.That(policy.EffectiveCompression(true, RenderTextureFormat.ARGB32, 31, 31), Is.EqualTo(GeneratorRuntimeCompression.Disabled));
        }

        private static void BuildPolicy(UMAGenerator generator, UMAData data, string method = "BeforeBuild") =>
            typeof(GeneratorQualityRuntime).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { generator, data });

        [Test]
        public void OptInCompressionProducesReusableCompressedTextureWithoutChangingSource()
        {
            var profile = Asset<GeneratorQualityProfile>(); profile.textures.compression = Override(GeneratorRuntimeCompression.Fast);
            if (profile.textures.EffectiveCompression(true, RenderTextureFormat.ARGB32, 32, 32) == GeneratorRuntimeCompression.Disabled)
                Assert.Ignore("Runtime compression unavailable on this device.");
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true))
            {
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile);
                var a = f.Avatar(); f.Build(a);
                var texture = (Texture2D)a.generatedMaterials.materials[0].resultingAtlasList[0];
                Assert.That(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat), Is.True);
                var b = f.Avatar(); f.Build(b);
                Assert.That(b.generatedMaterials.materials[0].resultingAtlasList[0], Is.SameAs(texture));
                Assert.That(f.Source.format, Is.EqualTo(TextureFormat.RGBA32));
            }
            Assert.That(UMAGeneratedResourceCache.Shared.EntryCount, Is.EqualTo(entries));
        }

        [Test]
        public void ExplicitRendererSettingsArePreservedAndPolicyRestoresWithoutEditingSharedMesh()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture())
            {
                var a = f.Avatar(); f.Build(a); f.Meshes.Build(a);
                var b = f.Avatar(); f.Build(b); f.Meshes.Build(b);
                var renderer = a.GetRenderer(0); var mesh = renderer.sharedMesh;
                Assert.That(b.GetRenderer(0).sharedMesh, Is.SameAs(mesh));
                renderer.shadowCastingMode = ShadowCastingMode.On;
                a.SetRendererAssets(new[] { Asset<UMARendererAsset>() });
                var profile = Asset<GeneratorQualityProfile>(); profile.characters.castShadows = Override(ShadowCastingMode.Off);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile);
                BuildPolicy(f.Generator, a); BuildPolicy(f.Generator, a, "AfterBuild");
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                profile.characters.overrideExplicitRendererSettings = Override(true);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile);
                BuildPolicy(f.Generator, a); BuildPolicy(f.Generator, a, "AfterBuild");
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(renderer.sharedMesh, Is.SameAs(mesh)); Assert.That(b.GetRenderer(0).sharedMesh, Is.SameAs(mesh));
                GeneratorQualityRuntime.Remove(f.Generator, profile);
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
            }
        }

        [UnityTest]
        public IEnumerator AsyncReadbackKeepsCapturedSamplingAndDelaysProfileChanges()
        {
            if (!SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("GPU readback unavailable.");
            int entries = UMAGeneratedResourceCache.Shared.EntryCount;
            using (var f = new UMAResourceReuseAtlasTests.Fixture(true, true))
            {
                var profile = Asset<GeneratorQualityProfile>();
                profile.textures.filterMode = Override(FilterMode.Point); profile.textures.mipMaps = Override(true);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile);
                var a = f.Avatar(); f.Build(a);
                Assert.That(RenderTexToCPU.renderTexturesToCPU.Count, Is.GreaterThan(0));
                profile.textures.filterMode = Override(FilterMode.Trilinear);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile);
                Assert.That(GeneratorQualityRuntime.IsPending(f.Generator), Is.True);
                Assert.That(f.Generator.qualityTextures.filterMode.value, Is.EqualTo(FilterMode.Point));
                AsyncGPUReadback.WaitAllRequests(); yield return null;
                RenderTexToCPU.ApplyQueuedCopies(0);
                var completed = a.generatedMaterials.materials[0].resultingAtlasList[0] as Texture2D;
                Assert.That(completed, Is.Not.Null); Assert.That(completed.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(completed.mipmapCount, Is.GreaterThan(1));
                GeneratorQualityRuntime.ApplyPending(f.Generator);
                Assert.That(f.Generator.qualityTextures.filterMode.value, Is.EqualTo(FilterMode.Trilinear));
                Assert.That(completed.filterMode, Is.EqualTo(FilterMode.Point));
            }
            Assert.That(UMAGeneratedResourceCache.Shared.EntryCount, Is.EqualTo(entries));
        }

        [UnityTest]
        public IEnumerator GradualRestorationRebuildsEveryCharacterInsteadOfDroppingTheQueue()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture())
            {
                var avatars = new List<UMAData>();
                for (int i = 0; i < 3; i++)
                {
                    var data = f.Avatar(); f.Build(data); f.Meshes.Build(data);
                    data.isMeshDirty = data.isTextureDirty = data.dirty = false; avatars.Add(data);
                }
                var profile = Asset<GeneratorQualityProfile>(); profile.atlasResolution = Override(64);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile, rebuild: GeneratorQualityRebuild.NewBuildsOnly);
                Assert.That(avatars.TrueForAll(a => !a.isMeshDirty), Is.True);
                // Scheduling-only edits must not queue an output rebuild.
                profile.IterationCount = Override(2);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile, rebuild: GeneratorQualityRebuild.Gradual);
                Assert.That(avatars.TrueForAll(a => !a.isMeshDirty), Is.True);
                GeneratorQualityRuntime.Remove(f.Generator, profile);
                Assert.That(f.Generator.atlasResolution, Is.EqualTo(32));
                Assert.That(avatars.FindAll(a => a.isMeshDirty).Count, Is.EqualTo(1));
                for (int frame = 0; frame < 5 && !avatars.TrueForAll(a => a.isMeshDirty); frame++)
                {
                    yield return null;
                    // EditMode yields do not advance Time.frameCount as player frames do.
                    var contexts = (IDictionary)typeof(GeneratorQualityRuntime).GetField("contexts", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    var context = contexts[f.Generator];
                    context?.GetType().GetField("LastRebuildFrame").SetValue(context, -1);
                    GeneratorQualityRuntime.ApplyPending(f.Generator);
                }
                Assert.That(avatars.TrueForAll(a => a.isMeshDirty && a.isTextureDirty), Is.True);
                Assert.That(f.Generator.QueueSize(), Is.EqualTo(3));
                f.Generator.ClearAllPending();
            }
        }

        [Test]
        public void DisabledControllerDoesNotAcquireOwnershipAndReenableRestoresIt()
        {
            var generator = Generator(); var profile = Asset<GeneratorQualityProfile>(); profile.atlasResolution = Override(2048);
            var go = new GameObject("Quality controller"); go.SetActive(false); objects.Add(go);
            var controller = go.AddComponent<GeneratorQualityController>(); controller.generator = generator; controller.profile = profile;
            controller.Refresh(); Assert.That(generator.atlasResolution, Is.EqualTo(512));
            // This is intentionally not ExecuteAlways; simulate player lifecycle in EditMode.
            go.SetActive(true); controller.Refresh(); Assert.That(generator.atlasResolution, Is.EqualTo(2048));
            controller.enabled = false;
            typeof(GeneratorQualityController).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
            Assert.That(generator.atlasResolution, Is.EqualTo(512));
            controller.enabled = true; controller.Refresh(); Assert.That(generator.atlasResolution, Is.EqualTo(2048));
            // Simulate the registry reset used with domain reload disabled.
            typeof(GeneratorQualityRuntime).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            typeof(GeneratorQualityController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
            Assert.That(generator.atlasResolution, Is.EqualTo(2048));
        }

        [Test]
        public void ActiveIncrementalOperationCompletesBeforeProfileIsApplied()
        {
            var generator = Generator();
            var combiner = generator.gameObject.AddComponent<FakeMultiStepMeshCombiner>(); combiner.stepsToComplete = 4;
            var fixture = typeof(UMAGeneratorMultiStepTests);
            fixture.GetMethod("ConfigureGenerator", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { generator, combiner });
            var go = new GameObject("Incremental quality avatar"); objects.Add(go);
            var arguments = new object[] { go, null };
            var data = (UMAData)fixture.GetMethod("CreateUmaData", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments);
            objects.Add((RaceData)arguments[1]);
            generator.addDirtyUMA(data); generator.Work();
            var operation = combiner.lastOperation;
            Assert.That(operation, Is.Not.Null); Assert.That(generator.CanApplyQualitySettings, Is.False);
            var profile = Asset<GeneratorQualityProfile>(); profile.atlasResolution = Override(1024);
            GeneratorQualityRuntime.SetProfile(generator, profile, profile);
            Assert.That(generator.atlasResolution, Is.EqualTo(512));
            for (int i = 0; i < 15 && !generator.CanApplyQualitySettings; i++) generator.Work();
            Assert.That(operation.disposed, Is.True);
            GeneratorQualityRuntime.ApplyPending(generator);
            Assert.That(generator.atlasResolution, Is.EqualTo(1024));
            Assert.That(generator.QueueSize(), Is.Zero);
        }

        [Test]
        public void NewBuildsOnlyRestoresCharacterPolicyAndMarksNextBuildWithoutQueueing()
        {
            using (var f = new UMAResourceReuseAtlasTests.Fixture())
            {
                var a = f.Avatar(); f.Build(a); f.Meshes.Build(a);
                var profile = Asset<GeneratorQualityProfile>(); profile.characters.atlasScale = Override(.5f);
                GeneratorQualityRuntime.SetProfile(f.Generator, profile, profile);
                float original = a.atlasResolutionScale;
                BuildPolicy(f.Generator, a);
                Assert.That(a.atlasResolutionScale, Is.EqualTo(.5f));
                a.isMeshDirty = a.isTextureDirty = false;
                GeneratorQualityRuntime.Remove(f.Generator, profile);
                Assert.That(a.atlasResolutionScale, Is.EqualTo(original));
                Assert.That(a.isMeshDirty && a.isTextureDirty, Is.True);
                Assert.That(f.Generator.QueueSize(), Is.Zero);
            }
        }

        [Test]
        public void ImmediatePolicyBuildsExistingCharacterAtSafeBoundary()
        {
            var generator = Generator(); var previous = UMAAssetIndexer.Instance.bareGenerator;
            UMAAssetIndexer.Instance.generator = generator;
            try
            {
                var combiner = generator.gameObject.AddComponent<SynchronousSchedulerTestMeshCombiner>();
                var fixture = typeof(UMAGeneratorMultiStepTests);
                fixture.GetMethod("ConfigureGenerator", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { generator, combiner });
                generator.textureMerge = Asset<TextureMerge>();
                var go = new GameObject("Immediate quality avatar"); objects.Add(go);
                var args = new object[] { go, null };
                var data = (UMAData)fixture.GetMethod("CreateUmaData", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
                objects.Add((RaceData)args[1]);
                data.SetRenderers(new[] { go.AddComponent<SkinnedMeshRenderer>() });
                var profile = Asset<GeneratorQualityProfile>(); profile.atlasResolution = Override(1024);
                GeneratorQualityRuntime.SetProfile(generator, profile, profile, rebuild: GeneratorQualityRebuild.Immediate);
                Assert.That(combiner.updateCalls, Is.EqualTo(1));
                Assert.That(generator.QueueSize(), Is.Zero);
                Assert.That(data.isMeshDirty, Is.False);
                // Removing the profile without queueing still requires matched UVs on the
                // next build, even if the caller only asks for textures and isMeshDirty was
                // already set by restoration.
                GeneratorQualityRuntime.SetProfile(generator, profile, profile, rebuild: GeneratorQualityRebuild.NewBuildsOnly);
                GeneratorQualityRuntime.Remove(generator, profile);
                Assert.That(data.isMeshDirty, Is.True);
                generator.GenerateTexturesOnly(data, false);
                Assert.That(combiner.updateCalls, Is.EqualTo(2));
                Assert.That(data.isMeshDirty, Is.False);
            }
            finally { UMAAssetIndexer.Instance.generator = previous; }
        }
    }
}
