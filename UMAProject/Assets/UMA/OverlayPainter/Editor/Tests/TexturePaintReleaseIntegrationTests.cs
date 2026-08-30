#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintReleaseIntegrationTests
    {
        private const string Folder = "Assets/UMAProjectData/Tests/OverlayPainter/GeneratedReleaseTests";
        private readonly List<Object> ownedObjects = new List<Object>();
        private readonly List<TextureStore> ownedStores = new List<TextureStore>();
        private string indexerAssetPath;
        private byte[] indexerAssetBytes;

        [SetUp]
        public void SetUp()
        {
            indexerAssetPath = AssetDatabase.GetAssetPath(UMAAssetIndexer.Instance);
            indexerAssetBytes = !string.IsNullOrEmpty(indexerAssetPath)
                ? File.ReadAllBytes(Path.GetFullPath(indexerAssetPath)) : null;
            AssetDatabase.DeleteAsset(Folder);
            EnsureFolder(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            TexturePaintSpriteSource.ClearCache();
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            string[] overlays = AssetDatabase.FindAssets("t:OverlayDataAsset", new[] { Folder });
            for (int i = 0; indexer != null && i < overlays.Length; i++)
            {
                OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(
                    AssetDatabase.GUIDToAssetPath(overlays[i]));
                if (overlay != null) indexer.RemoveAsset(typeof(OverlayDataAsset), overlay.overlayName, false);
            }
            if (indexer != null && overlays.Length > 0)
            {
                indexer.RebuildIndex();
                EditorUtility.SetDirty(indexer);
                AssetDatabase.SaveAssetIfDirty(indexer);
            }
            for (int i = 0; i < ownedStores.Count; i++) ownedStores[i]?.Dispose();
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
                if (ownedObjects[i] != null) Object.DestroyImmediate(ownedObjects[i]);
            ownedStores.Clear();
            ownedObjects.Clear();
            AssetDatabase.DeleteAsset(Folder);
            if (indexerAssetBytes != null && !string.IsNullOrEmpty(indexerAssetPath))
            {
                RestoreAssetBytes(indexerAssetPath, indexerAssetBytes);
                AssetDatabase.ImportAsset(indexerAssetPath, ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }
        }

        [Test]
        public void LayerCompositionMatchesOrderedOpacityAndVisibilityReference()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, new Color(0.1f, 0.2f, 0.3f, 0.25f));
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Composition", 16, RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.IsAvailable, Is.True, "Layer compositor GPU path is unavailable.");

            TexturePaintLayer lower = set.AddFillLayer("Lower", TexturePaintChannel.Albedo,
                new Color(0.9f, 0.1f, 0.2f, 0.5f));
            lower.opacity = 0.5f;
            lower.GetChannelSettings(TexturePaintChannel.Albedo).opacity = 0.5f;
            TexturePaintLayer hidden = set.AddFillLayer("Hidden", TexturePaintChannel.Albedo, Color.green);
            hidden.visible = false;
            AssertColor(ReadCenter(lower.channels[TexturePaintChannel.Albedo].Front),
                new Color(0.9f, 0.1f, 0.2f, 0.5f), 0.004f);
            Assert.That(lower.GetChannelSettings(TexturePaintChannel.Albedo).enabled, Is.True);
            set.RecomposeAll();

            float alpha = 0.5f * 0.5f * 0.5f;
            Color expected = Color.Lerp(new Color(0.1f, 0.2f, 0.3f, 0.25f),
                new Color(0.9f, 0.1f, 0.2f, 0.5f), alpha);
            expected.a = alpha + 0.25f * (1f - alpha);
            Color actual = ReadCenter(channel.composite);
            AssertColor(actual, expected, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void ChannelAdjustmentsAffectOnlyTheirSelectedChannelAndProtectNormals()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo,
                new Color(0.2f, 0.4f, 0.6f, 0.75f));
            CreateStore(set);
            AddChannel(set, TexturePaintChannel.Roughness,
                new Color(0.35f, 0.35f, 0.35f, 1f));
            AddChannel(set, TexturePaintChannel.Normal,
                new Color(0.5f, 0.5f, 1f, 1f));
            foreach (TextureChannelTarget target in set.channels.Values)
                target.composite = CreateRenderTexture("Channel Adjustments " + target.channel,
                    16, RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.IsAvailable, Is.True);

            set.GetChannel(TexturePaintChannel.Albedo).adjustments =
                new TexturePaintChannelAdjustments
                {
                    brightness = 0.05f,
                    colorBalance = new Vector3(0.1f, -0.1f, 0f)
                };
            set.GetChannel(TexturePaintChannel.Normal).adjustments =
                new TexturePaintChannelAdjustments { brightness = 1f, contrast = 1f };
            set.RecomposeAll();

            AssertColor(ReadCenter(set.GetChannel(TexturePaintChannel.Albedo).composite),
                new Color(0.35f, 0.35f, 0.65f, 0.75f), 0.006f);
            AssertColor(ReadCenter(set.GetChannel(TexturePaintChannel.Roughness).composite),
                new Color(0.35f, 0.35f, 0.35f, 1f), 0.004f);
            AssertColor(ReadCenter(set.GetChannel(TexturePaintChannel.Normal).composite),
                new Color(0.5f, 0.5f, 1f, 1f), 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void GrayscaleAdjustmentCurveProtectsBlackAndWeightsBrighterValues()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Roughness, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Roughness);
            channel.composite = CreateRenderTexture("Grayscale Adjustment Curve", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            channel.adjustments = new TexturePaintChannelAdjustments { brightness = 0.5f };

            set.CompositeChannel(TexturePaintChannel.Roughness);
            AssertColor(ReadCenter(channel.composite), Color.black, 0.004f);

            channel.editable.Reset(null, new Color(0.8f, 0.8f, 0.8f, 1f));
            set.CompositeChannel(TexturePaintChannel.Roughness);
            AssertColor(ReadCenter(channel.composite), new Color(0.96f, 0.96f, 0.96f, 1f), 0.008f);
            compositor.Dispose();
        }

        [Test]
        public async Task PluginLayerReadsOnlyCompositeBelowItsStackPosition()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black,
                null, Own(CreateQuadMesh()));
            TextureStore store = CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Plugin Below Composite", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer lower = set.AddFillLayer("Lower Red",
                TexturePaintChannel.Albedo, Color.red);
            TexturePaintLayer pluginLayer = set.AddPluginLayer("Echo Below");
            TexturePaintLayer upper = set.AddFillLayer("Upper Blue",
                TexturePaintChannel.Albedo, Color.blue);
            set.RecomposeAll();
            Assert.That(ReadCenter(channel.composite).b, Is.GreaterThan(0.9f));

            using PluginHost host = new PluginHost();
            var destinations = new Dictionary<TextureSet, TexturePaintLayer>
                { { set, pluginLayer } };
            await host.ExecutePluginLayerAsync(new BelowCompositeEchoPlugin(), store,
                new TexturePaintPluginParameterSet(), destinations, null,
                CancellationToken.None);

            TexturePaintLayer generated = set.layers[set.layers.IndexOf(upper) - 1];
            Color cached = ReadCenter(generated.channels[TexturePaintChannel.Albedo].Front);
            Assert.That(cached.r, Is.GreaterThan(0.9f),
                "The Plugin layer should receive the red layer below it.");
            Assert.That(cached.b, Is.LessThan(0.1f),
                "The blue layer above it must not feed back into the plugin input.");
            Assert.That(ReadCenter(channel.composite).b, Is.GreaterThan(0.9f),
                "The upper layer must continue compositing over generated output.");
            Assert.That(lower.kind, Is.EqualTo(TexturePaintLayerKind.Fill));
            compositor.Dispose();
        }

        [Test]
        public async Task CompactPluginTilesCommitThroughTheGpuKernel()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear,
                null, Own(CreateQuadMesh()));
            set.channelPackShader = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            TextureStore store = CreateStore(set);
            using PluginHost host = new PluginHost();

            await host.ExecuteCommandAsync(new CompactSolidPlugin(), store,
                new TexturePaintPluginParameterSet(), null, CancellationToken.None);

            Assert.That(set.layers.Count, Is.EqualTo(1));
            Color result = ReadCenter(set.layers[0].channels[TexturePaintChannel.Albedo].Front);
            Assert.That(result.r, Is.GreaterThan(0.95f));
            Assert.That(result.g, Is.LessThan(0.05f));
            Assert.That(result.b, Is.LessThan(0.05f));
            Assert.That(result.a, Is.GreaterThan(0.95f));
        }

        [Test]
        public async Task StandardGpuGeneratorPathAvoidsCpuTileFallback()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black,
                null, Own(CreateQuadMesh()));
            AddChannel(set, TexturePaintChannel.AmbientOcclusion, Color.black);
            TextureStore store = CreateStore(set);
            using PluginHost host = new PluginHost
            {
                GpuGeneratorShader = TexturePaintGpuTestFixture.LoadShader(
                    "PluginGenerators.compute")
            };
            var plugin = new GpuDirtProbePlugin();

            await host.ExecuteCommandAsync(plugin, store, host.CreateParameters(plugin),
                null, CancellationToken.None);

            Assert.That(plugin.cpuFallbackInvoked, Is.False);
            Assert.That(set.layers, Has.Count.EqualTo(1));
            Color generated = ReadCenter(
                set.layers[0].channels[TexturePaintChannel.Albedo].Front);
            Assert.That(generated.r, Is.GreaterThan(0.9f));
            Assert.That(generated.g, Is.LessThan(0.1f));
            Assert.That(generated.a, Is.GreaterThan(0.9f));
            Assert.That(host.Diagnostics[host.Diagnostics.Count - 1].message,
                Does.Contain("GPU"));
        }

        [Test]
        public void PluginLayerUsesOrdinaryGroupOpacityAndEditableMask()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black,
                null, Own(CreateQuadMesh()));
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Grouped Plugin Composite", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;

            TexturePaintLayer group = set.AddGroup("Weathering");
            group.opacity = 0.5f;
            TexturePaintLayer plugin = set.AddPluginLayer("Agify Cache");
            Assert.That(plugin.parentId, Is.EqualTo(group.id));
            plugin.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget(
                "Plugin Albedo", channel.Texture.width, channel.Texture.height,
                channel.format, null, Color.red);
            plugin.GetChannelSettings(TexturePaintChannel.Albedo);
            TexturePaintLayerMask mask = set.AddLayerMask(plugin, 1f);
            Assert.That(mask, Is.Not.Null);

            set.RecomposeAll();
            Color grouped = ReadCenter(channel.composite);
            Assert.That(grouped.r, Is.EqualTo(0.5f).Within(0.03f));
            Assert.That(grouped.g, Is.LessThan(0.03f));

            mask.target.Reset(null, TextureSet.MaskColor(0f));
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.black, 0.02f);
            compositor.Dispose();
        }

        [Test]
        public void SynchronizingSplinePeersPreservesTheActiveUVEditingInstance()
        {
            var active = new TexturePaintLayer
            {
                kind = TexturePaintLayerKind.Spline,
                spline = new TexturePaintSpline(),
                splineSettings = new TexturePaintSplineSettings()
            };
            active.spline.AddPoint(Vector3.zero, new Vector2(0.2f, 0.3f), 0, 0, Vector3.forward);
            TexturePaintSpline editingInstance = active.spline;

            TexturePaintStageWindow.SynchronizeSplinePeer(active, active, "path-group");

            Assert.That(active.spline, Is.SameAs(editingInstance),
                "Rendering an active path must not invalidate the UV editor's spline reference.");

            var peer = new TexturePaintLayer
            {
                kind = TexturePaintLayerKind.Spline,
                spline = new TexturePaintSpline(),
                splineSettings = new TexturePaintSplineSettings()
            };
            TexturePaintStageWindow.SynchronizeSplinePeer(active, peer, "path-group");

            Assert.That(peer.spline, Is.Not.SameAs(active.spline),
                "Logical peers still require independent editable spline instances.");
            Assert.That(peer.spline.uvPoints, Is.EqualTo(active.spline.uvPoints));
            Assert.That(active.proceduralGroupKey, Is.EqualTo("path-group"));
            Assert.That(peer.proceduralGroupKey, Is.EqualTo("path-group"));
        }

        [Test]
        public void EditingChannelContributionPreservesItsStoredSourceAcrossUndoRedo()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Normal, Color.clear);
            CreateStore(set);
            TexturePaintLayer layer = set.AddLayer("Multi Channel Paint");
            set.GetPaintTarget(TexturePaintChannel.Normal, TexturePaintSourceMode.SourceOverlay);
            TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(TexturePaintChannel.Normal);
            settings.sourceSettings = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                invert = true,
                tiling = new Vector2(2f, 3f)
            };

            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo change = typeof(TexturePaintStageWindow).GetMethod("ChangeLayerChannel", flags);
            MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);
            MethodInfo redo = typeof(TexturePaintStageWindow).GetMethod("RedoLightweight", flags);
            Assert.That(change, Is.Not.Null);
            change.Invoke(stage, new object[]
            {
                set, layer, TexturePaintChannel.Normal, true, false, 0.35f, 0.8f,
                TexturePaintBlendMode.Normal
            });

            Assert.That(settings, Is.Not.SameAs(layer.GetChannelSettings(TexturePaintChannel.Normal)));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Normal).contribution,
                Is.EqualTo(0.35f));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Normal).sourceSettings, Is.Not.Null);
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Normal).sourceSettings.invert, Is.True);

            Assert.That((bool)undo.Invoke(stage, null), Is.True);
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Normal).sourceSettings.invert, Is.True);
            Assert.That((bool)redo.Invoke(stage, null), Is.True);
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Normal).sourceSettings.invert, Is.True);
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Normal).sourceSettings.tiling,
                Is.EqualTo(new Vector2(2f, 3f)));
        }

        [Test]
        public void LayerMetadataEditsDoNotOverwriteIndependentChannelBlends()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            AddChannel(set, TexturePaintChannel.Roughness, Color.white);
            CreateStore(set);
            TexturePaintLayer layer = set.AddLayer("Material Detail");
            set.GetPaintTarget(TexturePaintChannel.Albedo, TexturePaintSourceMode.SourceOverlay);
            set.GetPaintTarget(TexturePaintChannel.Roughness, TexturePaintSourceMode.SourceOverlay);
            layer.GetChannelSettings(TexturePaintChannel.Albedo).blendMode =
                TexturePaintBlendMode.Screen;
            layer.GetChannelSettings(TexturePaintChannel.Roughness).blendMode =
                TexturePaintBlendMode.Multiply;
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo change = typeof(TexturePaintStageWindow).GetMethod("ChangeLayerMetadata", flags);
            MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);

            change.Invoke(stage, new object[]
                { set, layer, "Renamed Detail", 0.42f, TexturePaintBlendMode.Add });
            change.Invoke(stage, new object[]
                { set, layer, "Final Detail", 0.35f, TexturePaintBlendMode.Screen });

            Assert.That(layer.name, Is.EqualTo("Final Detail"));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Albedo).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Screen));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Roughness).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Multiply));
            Assert.That((bool)undo.Invoke(stage, null), Is.True);
            Assert.That(layer.name, Is.EqualTo("Material Detail"),
                "Continuous metadata edits should coalesce into one undo gesture.");
            Assert.That(layer.opacity, Is.EqualTo(1f));
            Assert.That(layer.blendMode, Is.EqualTo(TexturePaintBlendMode.Normal));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Albedo).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Screen));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Roughness).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Multiply));
        }

        [Test]
        public void LayerBlendUpdatesChannelsThatStillInheritItsBlendMode()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Color baseColor = new Color(0.8f, 0.6f, 0.4f, 1f);
            Color layerColor = new Color(0.5f, 0.25f, 0.75f, 1f);
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, baseColor);
            AddChannel(set, TexturePaintChannel.Roughness, Color.white);
            CreateStore(set);
            TextureChannelTarget albedo = set.GetChannel(TexturePaintChannel.Albedo);
            albedo.composite = CreateRenderTexture("Inherited Layer Blend", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.IsAvailable, Is.True, "Layer compositor GPU path is unavailable.");
            TexturePaintLayer layer = set.AddFillLayer("Material Detail",
                TexturePaintChannel.Albedo, layerColor);
            set.GetPaintTarget(TexturePaintChannel.Roughness, TexturePaintSourceMode.SourceOverlay);
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo change = typeof(TexturePaintStageWindow).GetMethod("ChangeLayerMetadata", flags);
            MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);
            MethodInfo redo = typeof(TexturePaintStageWindow).GetMethod("RedoLightweight", flags);

            Assert.That(change, Is.Not.Null);
            set.RecomposeAll();
            AssertColor(ReadCenter(albedo.composite), layerColor, 0.004f);
            change.Invoke(stage, new object[]
                { set, layer, layer.name, layer.opacity, TexturePaintBlendMode.Multiply });
            set.RecomposeAll();

            Assert.That(layer.blendMode, Is.EqualTo(TexturePaintBlendMode.Multiply));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Albedo).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Multiply));
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Roughness).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Multiply));
            AssertColor(ReadCenter(albedo.composite), new Color(
                baseColor.r * layerColor.r,
                baseColor.g * layerColor.g,
                baseColor.b * layerColor.b,
                1f), 0.004f);
            Assert.That((bool)undo.Invoke(stage, null), Is.True);
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Albedo).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Normal));
            Assert.That((bool)redo.Invoke(stage, null), Is.True);
            Assert.That(layer.GetChannelSettings(TexturePaintChannel.Albedo).blendMode,
                Is.EqualTo(TexturePaintBlendMode.Multiply));
            compositor.Dispose();
        }

        [Test]
        public void ConsecutiveFillEditsKeepIndependentUndoSnapshots()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            Assert.That(set.AddFillLayer("Fill", TexturePaintChannel.Albedo, Color.white), Is.Not.Null);
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo change = typeof(TexturePaintStageWindow).GetMethod("ChangeFillLayer", flags);
            MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);
            MethodInfo redo = typeof(TexturePaintStageWindow).GetMethod("RedoLightweight", flags);
            MethodInfo clear = typeof(TexturePaintStageWindow).GetMethod("ClearLightweightHistory", flags);
            Assert.That(change, Is.Not.Null);

            change.Invoke(stage, new object[]
            {
                set, set.layers[0], TexturePaintChannel.Albedo,
                new TexturePaintFillSettings { source = TexturePaintBrushSource.Color, color = Color.red }
            });
            Assert.That(set.layers, Has.Count.EqualTo(1));
            AssertColor(ReadCenter(set.layers[0].channels[TexturePaintChannel.Albedo].Front),
                Color.red, 0.004f);

            change.Invoke(stage, new object[]
            {
                set, set.layers[0], TexturePaintChannel.Albedo,
                new TexturePaintFillSettings { source = TexturePaintBrushSource.Color, color = Color.green }
            });
            Assert.That(set.layers, Has.Count.EqualTo(1));
            AssertColor(ReadCenter(set.layers[0].channels[TexturePaintChannel.Albedo].Front),
                Color.green, 0.004f);

            Assert.That((bool)undo.Invoke(stage, null), Is.True);
            AssertColor(ReadCenter(set.layers[0].channels[TexturePaintChannel.Albedo].Front),
                Color.red, 0.004f);
            Assert.That((bool)undo.Invoke(stage, null), Is.True);
            AssertColor(ReadCenter(set.layers[0].channels[TexturePaintChannel.Albedo].Front),
                Color.white, 0.004f);
            Assert.That((bool)redo.Invoke(stage, null), Is.True);
            AssertColor(ReadCenter(set.layers[0].channels[TexturePaintChannel.Albedo].Front),
                Color.red, 0.004f);
            Assert.That((bool)redo.Invoke(stage, null), Is.True);
            AssertColor(ReadCenter(set.layers[0].channels[TexturePaintChannel.Albedo].Front),
                Color.green, 0.004f);
            Assert.That(set.layers, Has.Count.EqualTo(1));
            clear.Invoke(stage, null);
        }

        [Test]
        public void RemoveLayerChannelRetargetsEffectsAndSupportsUndoRedo()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            AddChannel(set, TexturePaintChannel.Roughness, Color.white);
            CreateStore(set);
            TexturePaintLayer layer = set.AddLayer("Multi Channel");
            set.GetPaintTarget(TexturePaintChannel.Albedo, TexturePaintSourceMode.SourceOverlay);
            EditableTextureTarget roughness = set.GetPaintTarget(TexturePaintChannel.Roughness,
                TexturePaintSourceMode.SourceOverlay);
            TexturePaintLayerEffectSettings effect = layer.effects.Add(
                TexturePaintLayerEffectKind.ColorOverlay);
            effect.enabled = true;
            effect.channel = TexturePaintChannel.Roughness;
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo remove = typeof(TexturePaintStageWindow).GetMethod(
                "RemoveLayerChannelWithHistory", flags);
            MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);
            MethodInfo redo = typeof(TexturePaintStageWindow).GetMethod("RedoLightweight", flags);

            Assert.That((bool)remove.Invoke(stage,
                new object[] { set, layer, TexturePaintChannel.Roughness }), Is.True);
            Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Roughness), Is.False);
            Assert.That(layer.effects.Stack.Find(item => item.id == effect.id).channel,
                Is.EqualTo(TexturePaintChannel.Albedo));
            Assert.That((bool)undo.Invoke(stage, null), Is.True);
            Assert.That(layer.channels[TexturePaintChannel.Roughness], Is.SameAs(roughness));
            Assert.That(layer.effects.Stack.Find(item => item.id == effect.id).channel,
                Is.EqualTo(TexturePaintChannel.Roughness));
            Assert.That((bool)redo.Invoke(stage, null), Is.True);
            Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Roughness), Is.False);
        }

        [Test]
        public void GroupCompositionKeepsHigherLayersVisibleAndProvidesAnIsolatedPreview()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Group Composition", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.IsAvailable, Is.True, "Layer compositor GPU path is unavailable.");

            TexturePaintLayer group = set.AddGroup("Leather");
            set.activeLayerIndex = set.layers.IndexOf(group);
            TexturePaintLayer lowerChild = set.AddFillLayer("Leather Albedo Lower",
                TexturePaintChannel.Albedo, Color.red);
            set.activeLayerIndex = set.layers.IndexOf(group);
            TexturePaintLayer upperChild = set.AddFillLayer("Leather Albedo Upper",
                TexturePaintChannel.Albedo, new Color(0f, 1f, 0f, 0.5f));
            set.activeLayerIndex = -1;
            TexturePaintLayer above = set.AddFillLayer("Jeans",
                TexturePaintChannel.Albedo, new Color(0f, 0f, 1f, 0.5f));

            // Reproduce the damaged ordering from OstrichPants.asset: a root layer was serialized
            // between a group's child block and its folder row.
            set.layers.Remove(above);
            set.layers.Insert(set.layers.IndexOf(group), above);
            set.RecomposeAll();
            FieldInfo groupOriginalField = typeof(TextureLayerCompositor).GetField("groupOriginal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo groupResultField = typeof(TextureLayerCompositor).GetField("groupResult",
                BindingFlags.Instance | BindingFlags.NonPublic);
            RenderTexture originalScratch = (RenderTexture)groupOriginalField.GetValue(compositor);
            RenderTexture resultScratch = (RenderTexture)groupResultField.GetValue(compositor);
            Assert.That(originalScratch, Is.Not.Null);
            Assert.That(resultScratch, Is.Not.Null);

            Assert.That(set.layers.IndexOf(upperChild), Is.EqualTo(set.layers.IndexOf(lowerChild) + 1));
            Assert.That(set.layers.IndexOf(group), Is.EqualTo(set.layers.IndexOf(upperChild) + 1));
            Assert.That(set.layers.IndexOf(above), Is.EqualTo(set.layers.IndexOf(group) + 1),
                "A root layer must be canonicalized above the complete group block.");
            AssertColor(ReadCenter(channel.composite), new Color(0.25f, 0.25f, 0.5f, 1f), 0.02f);

            set.activeLayerIndex = set.layers.IndexOf(group);
            RenderTexture groupPreview = set.GetSelectedGroupPreview(TexturePaintChannel.Albedo);
            Assert.That(groupPreview, Is.Not.Null);
            AssertColor(ReadCenter(groupPreview), new Color(0.5f, 0.5f, 0f, 1f), 0.02f);

            TexturePaintLayerMask groupMask = set.AddLayerMask(group, 0f);
            Assert.That(groupMask, Is.Not.Null);
            Assert.That(groupMask.sourceSettings.source, Is.EqualTo(TexturePaintBrushSource.Color));
            Assert.That(groupMask.sourceSettings.color.r, Is.EqualTo(1f).Within(0.001f),
                "A black mask should initially paint white so content can be revealed.");
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), new Color(0f, 0f, 0.5f, 1f), 0.02f);
            groupMask.target.Reset(null, TextureSet.MaskColor(1f));
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), new Color(0.25f, 0.25f, 0.5f, 1f), 0.02f);

            group.opacity = 0.5f;
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), new Color(0.125f, 0.125f, 0.5f, 1f), 0.02f);
            Assert.That(groupOriginalField.GetValue(compositor), Is.SameAs(originalScratch));
            Assert.That(groupResultField.GetValue(compositor), Is.SameAs(resultScratch));

            group.opacity = 1f;
            group.blendMode = TexturePaintBlendMode.Multiply;
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), new Color(0f, 0f, 0.5f, 1f), 0.02f);
            compositor.Dispose();
        }

        [Test]
        public void EditableLayerMaskControlsCompositionAndEvaluatesTextureOverlay()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Layer Mask Composition", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer layer = set.AddFillLayer("Masked Red", TexturePaintChannel.Albedo, Color.red);
            TexturePaintLayerMask mask = set.AddLayerMask(layer, 1f);
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.sourceSettings.color.r, Is.EqualTo(0f).Within(0.001f),
                "A white mask should initially paint black so content can be hidden.");

            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.red, 0.02f);
            mask.target.Reset(null, TextureSet.MaskColor(0f));
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.black, 0.02f);

            mask.target.Reset(null, TextureSet.MaskColor(1f));
            Texture2D blackOverlay = Own(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            blackOverlay.SetPixel(0, 0, Color.black);
            blackOverlay.Apply(false, false);
            mask.effects.textureOverlay.enabled = true;
            mask.effects.textureOverlay.texture = blackOverlay;
            mask.effects.textureOverlay.combine = TexturePaintBlendMode.Multiply;
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.black, 0.02f);
            compositor.Dispose();
        }

        [Test]
        public void LayerMaskPaintSourceIsAlwaysScalarGrayscale()
        {
            var mask = new TexturePaintLayerMask
            {
                sourceChannel = TexturePaintChannel.Roughness,
                sourceSettings = new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Overlay,
                    color = new Color(0.25f, 0.25f, 0.25f, 0.4f),
                    invert = true,
                    tiling = new Vector2(3f, 7f)
                }
            };

            mask.NormalizePaintSource();

            Assert.That(mask.PaintValue, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(mask.sourceSettings.source, Is.EqualTo(TexturePaintBrushSource.Color));
            Assert.That(mask.sourceSettings.sourceTexture, Is.Null);
            Assert.That(mask.sourceSettings.sourceSprite, Is.Null);
            Assert.That(mask.sourceSettings.sourceOverlay, Is.Null);
            Assert.That(mask.sourceSettings.invert, Is.False);
            Assert.That(mask.sourceSettings.color,
                Is.EqualTo(new Color(0.25f, 0.25f, 0.25f, 1f)));
            Assert.That(mask.sourceChannel, Is.EqualTo(TexturePaintChannel.Albedo));
        }

        [Test]
        public void CopyAndPasteLayerMaskSnapshotsPixelsSettingsAndSupportsUndoRedo()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TexturePaintLayer source = set.AddLayer("Source Mask");
            TexturePaintLayerMask sourceMask = set.AddLayerMask(source, 0f);
            sourceMask.target.Reset(null, TextureSet.MaskColor(0.25f));
            sourceMask.effects.noise.enabled = true;
            sourceMask.effects.noise.seed = 17;
            sourceMask.sourceSettings = TexturePaintLayerMask.DefaultSourceSettings();
            sourceMask.sourceSettings.color = new Color(0.35f, 0.35f, 0.35f, 1f);
            sourceMask.pluginId = "mask-generator";
            sourceMask.pluginVersion = "2";
            sourceMask.pluginParameters.Get("amount", true).number = 0.45f;

            TexturePaintLayer destination = set.AddLayer("Destination Mask");
            TexturePaintLayerMask originalDestinationMask = set.AddLayerMask(destination, 1f);
            originalDestinationMask.target.Reset(null, TextureSet.MaskColor(0.9f));
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo copy = typeof(TexturePaintStageWindow).GetMethod(
                "CopyLayerMaskToClipboard", flags);
            MethodInfo paste = typeof(TexturePaintStageWindow).GetMethod(
                "PasteLayerMaskFromClipboardWithHistory", flags);
            MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);
            MethodInfo redo = typeof(TexturePaintStageWindow).GetMethod("RedoLightweight", flags);
            MethodInfo clear = typeof(TexturePaintStageWindow).GetMethod("ClearLightweightHistory", flags);

            Assert.That(copy, Is.Not.Null);
            Assert.That(paste, Is.Not.Null);
            Assert.That((bool)copy.Invoke(stage, new object[] { set, source }), Is.True);
            sourceMask.target.Reset(null, TextureSet.MaskColor(0.75f));
            sourceMask.effects.noise.seed = 99;
            Assert.That((bool)paste.Invoke(stage, new object[] { set, destination }), Is.True);

            TexturePaintLayerMask pasted = destination.layerMask;
            Assert.That(pasted, Is.Not.Null);
            Assert.That(pasted, Is.Not.SameAs(sourceMask));
            Assert.That(pasted, Is.Not.SameAs(originalDestinationMask));
            AssertColor(ReadCenter(pasted.target.Front), TextureSet.MaskColor(0.25f), 0.01f);
            Assert.That(pasted.baseValue, Is.EqualTo(0f));
            Assert.That(pasted.effects.noise.enabled, Is.True);
            Assert.That(pasted.effects.noise.seed, Is.EqualTo(17));
            Assert.That(pasted.sourceSettings.color.r, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(pasted.pluginId, Is.EqualTo("mask-generator"));
            Assert.That(pasted.pluginParameters.Float("amount"), Is.EqualTo(0.45f).Within(0.001f));

            Assert.That((bool)undo.Invoke(stage, null), Is.True);
            Assert.That(destination.layerMask, Is.SameAs(originalDestinationMask));
            AssertColor(ReadCenter(destination.layerMask.target.Front), TextureSet.MaskColor(0.9f), 0.01f);
            Assert.That((bool)redo.Invoke(stage, null), Is.True);
            Assert.That(destination.layerMask, Is.SameAs(pasted));
            AssertColor(ReadCenter(destination.layerMask.target.Front), TextureSet.MaskColor(0.25f), 0.01f);
            clear.Invoke(stage, null);
        }

        [Test]
        public void NestedGroupOpacityCompositesItsSubtreeExactlyOnce()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Nested Group", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer outer = set.AddGroup("Outer");
            TexturePaintLayer inner = set.AddGroup("Inner");
            inner.opacity = 0.5f;
            set.AddFillLayer("Nested Red", TexturePaintChannel.Albedo, Color.red);

            set.RecomposeAll();

            Assert.That(inner.parentId, Is.EqualTo(outer.id));
            AssertColor(ReadCenter(channel.composite), new Color(0.5f, 0f, 0f, 1f), 0.025f);
            compositor.Dispose();
        }

        [Test]
        public void MergeDownPreservesTranslucentNormalCompositeAndRejectsBackdropDependentBlend()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Exact Merge", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            set.AddFillLayer("Lower", TexturePaintChannel.Albedo,
                new Color(1f, 0f, 0f, 0.5f));
            TexturePaintLayer upper = set.AddFillLayer("Upper", TexturePaintChannel.Albedo,
                new Color(0f, 1f, 0f, 0.5f));
            set.RecomposeAll();
            Color before = ReadCenter(channel.composite);

            Assert.That(set.MergeLayerDown(set.layers.IndexOf(upper)), Is.True);
            set.RecomposeAll();

            AssertColor(ReadCenter(channel.composite), before, 0.025f);
            TexturePaintLayer extra = set.AddFillLayer("Backdrop Dependent",
                TexturePaintChannel.Albedo, Color.white);
            extra.blendMode = TexturePaintBlendMode.Multiply;
            extra.GetChannelSettings(TexturePaintChannel.Albedo).blendMode =
                TexturePaintBlendMode.Multiply;
            Assert.That(set.CanMergeLayerDown(set.layers.IndexOf(extra), out string reason), Is.False);
            Assert.That(reason, Does.Contain("Normal"));
            compositor.Dispose();
        }

        [Test]
        public void IncrementalDocumentRestorePreservesAChildUntilItsGroupIsAvailable()
        {
            using TextureSet set = new TextureSet();
            TexturePaintLayer child = set.AddLayer("Restored Child");
            child.parentId = "saved-group-id";

            Assert.That(set.NormalizeLayerHierarchy(), Is.False);
            Assert.That(child.parentId, Is.EqualTo("saved-group-id"),
                "Incremental restore must not discard a parent id before its group is materialized.");

            TexturePaintLayer group = set.AddGroup("Restored Group");
            group.id = "saved-group-id";
            Assert.That(set.NormalizeLayerHierarchy(), Is.False);
            Assert.That(set.layers.IndexOf(child), Is.EqualTo(set.layers.IndexOf(group) - 1));
        }

        [Test]
        public void PreviewBindingEnablesThePackedSmoothnessMapKeyword()
        {
            using TextureSet set = new TextureSet();
            Material material = Own(new Material(Shader.Find("Standard")));
            RenderTexture packed = CreateRenderTexture("Packed Metallic Smoothness", 16,
                RenderTextureFormat.ARGB32);
            set.previewMaterial = material;
            set.physicalChannelGroups["_MetallicGlossMap"] = new TexturePhysicalChannelGroup
            {
                materialProperty = "_MetallicGlossMap",
                packed = packed
            };

            material.DisableKeyword("_METALLICGLOSSMAP");
            set.BindPreviewTextures(false);

            Assert.That(material.GetTexture("_MetallicGlossMap"), Is.SameAs(packed));
            Assert.That(material.IsKeywordEnabled("_METALLICGLOSSMAP"), Is.True,
                "The preview shader must sample the packed alpha channel used for smoothness.");
        }

        [Test]
        public void LayerDisplayNameIncludesItsAffectedChannel()
        {
            using TexturePaintLayer paint = new TexturePaintLayer
            {
                name = "Jeans",
                visible = true,
                paintSettings = new TexturePaintLayerSettings { channel = TexturePaintChannel.Roughness }
            };
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo displayName = typeof(TexturePaintStageWindow).GetMethod("LayerDisplayName", flags);

            Assert.That(displayName, Is.Not.Null);
            Assert.That(displayName.Invoke(null, new object[] { paint }), Is.EqualTo("Jeans: Roughness"));
            Assert.That(paint.visible, Is.True, "New layers should be visible unless a saved state says otherwise.");
        }

        [Test]
        public void LayerEffectsCompositeStrokeAndColorOverlayWithoutChangingLayerPixels()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Layer Effects", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.EffectsAvailable, Is.True, "Layer effects GPU path is unavailable.");

            Texture2D source = Own(new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true));
            Color[] pixels = new Color[16 * 16];
            for (int y = 6; y <= 9; y++)
            for (int x = 6; x <= 9; x++) pixels[y * 16 + x] = Color.green;
            source.SetPixels(pixels);
            source.Apply(false, false);

            TexturePaintLayer layer = set.AddLayer("Effect Source");
            EditableTextureTarget layerTarget = set.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            layerTarget.Reset(source, Color.clear);
            layer.effects.stroke.enabled = true;
            layer.effects.stroke.channel = TexturePaintChannel.Albedo;
            layer.effects.stroke.color = Color.red;
            layer.effects.stroke.width = 2f;
            layer.effects.stroke.smoothness = 0f;
            layer.effects.colorOverlay.enabled = true;
            layer.effects.colorOverlay.channel = TexturePaintChannel.Albedo;
            layer.effects.colorOverlay.color = Color.blue;
            layer.effects.colorOverlay.level = 0.5f;
            layer.effects.colorOverlay.blendMode = TexturePaintBlendMode.Normal;

            set.RecomposeAll();

            AssertColor(ReadPixel(channel.composite, 8, 8), new Color(0f, 0.5f, 0.5f, 1f), 0.02f);
            AssertColor(ReadPixel(channel.composite, 4, 8), Color.red, 0.02f);
            AssertColor(ReadPixel(channel.composite, 1, 1), Color.clear, 0.02f);
            AssertColor(ReadPixel(layerTarget.Front, 8, 8), Color.green, 0.004f);
            AssertColor(ReadPixel(layerTarget.Front, 4, 8), Color.clear, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void NegativeStrokeOffsetPullsOutlineAcrossAuthoredBoundary()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Inset Stroke", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;

            Texture2D source = Own(new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true));
            Color[] pixels = new Color[16 * 16];
            for (int y = 4; y <= 11; y++)
            for (int x = 4; x <= 11; x++) pixels[y * 16 + x] = Color.green;
            source.SetPixels(pixels);
            source.Apply(false, false);

            TexturePaintLayer layer = set.AddLayer("Inset Stroke Source");
            set.GetPaintTarget(TexturePaintChannel.Albedo, TexturePaintSourceMode.SourceOverlay)
                .Reset(source, Color.clear);
            TexturePaintLayerEffectSettings stroke = layer.effects.stroke;
            stroke.enabled = true;
            stroke.channel = TexturePaintChannel.Albedo;
            stroke.color = Color.red;
            stroke.width = 2f;
            stroke.offset.x = -1f;
            stroke.smoothness = 0f;

            set.RecomposeAll();

            AssertColor(ReadPixel(channel.composite, 2, 8), Color.clear, 0.02f);
            AssertColor(ReadPixel(channel.composite, 3, 8), Color.red, 0.02f);
            AssertColor(ReadPixel(channel.composite, 4, 8), Color.red, 0.02f);
            AssertColor(ReadPixel(channel.composite, 5, 8), Color.red, 0.02f);
            AssertColor(ReadPixel(channel.composite, 7, 8), Color.green, 0.02f);
            compositor.Dispose();
        }

        [Test]
        public void LayerOpacityIsAppliedOnceToPaintAndTheCompleteEffectStack()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Isolated Layer Opacity", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;

            TexturePaintLayer layer = set.AddFillLayer("Isolated Effects",
                TexturePaintChannel.Albedo, Color.green);
            layer.opacity = 0.5f;
            TexturePaintLayerEffectSettings overlay = layer.effects.colorOverlay;
            overlay.enabled = true;
            overlay.channel = TexturePaintChannel.Albedo;
            overlay.color = Color.blue;
            overlay.level = 1f;
            overlay.blendMode = TexturePaintBlendMode.Normal;

            set.RecomposeAll();

            AssertColor(ReadCenter(channel.composite), new Color(0f, 0f, 0.5f, 1f), 0.025f);
            compositor.Dispose();
        }

        [Test]
        public void MultipleEffectInstancesEvaluateInVisibleStackOrder()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Ordered Effects", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer layer = set.AddFillLayer("Ordered Effects",
                TexturePaintChannel.Albedo, Color.white);
            TexturePaintLayerEffectSettings red = layer.effects.colorOverlay;
            red.enabled = true;
            red.channel = TexturePaintChannel.Albedo;
            red.color = new Color(1f, 0f, 0f, 0.5f);
            TexturePaintLayerEffectSettings blue = layer.effects.Add(
                TexturePaintLayerEffectKind.ColorOverlay);
            blue.enabled = true;
            blue.channel = TexturePaintChannel.Albedo;
            blue.color = new Color(0f, 0f, 1f, 0.5f);

            set.RecomposeAll();

            AssertColor(ReadCenter(channel.composite), new Color(0.5f, 0.25f, 0.75f, 1f),
                0.025f);
            compositor.Dispose();
        }

        [Test]
        public void ImageAdjustmentsApplyHueSaturationBrightnessAndContrastNonDestructively()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Image Adjustments", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer layer = set.AddFillLayer("Adjusted Image",
                TexturePaintChannel.Albedo, Color.red);
            TexturePaintLayerEffectSettings adjustment = layer.effects.imageAdjustments;
            adjustment.enabled = true;
            adjustment.channel = TexturePaintChannel.Albedo;

            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.red, 0.025f);

            adjustment.hue = 120f;
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.green, 0.025f);

            adjustment.hue = 0f;
            adjustment.saturation = 0f;
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite),
                new Color(0.2126f, 0.2126f, 0.2126f, 1f), 0.025f);

            adjustment.saturation = 1f;
            adjustment.brightness = 0.1f;
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), new Color(1f, 0.1f, 0.1f, 1f), 0.025f);

            adjustment.brightness = 0f;
            adjustment.contrast = -1f;
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), new Color(0.5f, 0.5f, 0.5f, 1f), 0.025f);

            AssertColor(ReadCenter(layer.channels[TexturePaintChannel.Albedo].Front),
                Color.red, 0.025f);
            compositor.Dispose();
        }

        [Test]
        public void ImageAdjustmentsDoNotModifyTheBackdropThroughTranslucentLayerPixels()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.blue);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Translucent Image Adjustments", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer layer = set.AddFillLayer("Translucent Adjusted Image",
                TexturePaintChannel.Albedo, new Color(1f, 0f, 0f, 0.5f));
            TexturePaintLayerEffectSettings adjustment = layer.effects.imageAdjustments;
            adjustment.enabled = true;
            adjustment.channel = TexturePaintChannel.Albedo;
            adjustment.brightness = 0.2f;

            set.RecomposeAll();

            AssertColor(ReadCenter(channel.composite), new Color(0.5f, 0.1f, 0.6f, 1f), 0.025f);
            AssertColor(ReadCenter(layer.channels[TexturePaintChannel.Albedo].Front),
                new Color(1f, 0f, 0f, 0.5f), 0.025f);
            compositor.Dispose();
        }

        [Test]
        public void StrokeUsesAuthoredBoundaryAndOuterShadowRetainsItsFade()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Stroke Shadow Boundary", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Texture2D source = Own(new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true));
            Color[] pixels = new Color[16 * 16];
            for (int y = 6; y <= 9; y++)
            for (int x = 6; x <= 9; x++) pixels[y * 16 + x] = Color.green;
            source.SetPixels(pixels);
            source.Apply(false, false);
            TexturePaintLayer layer = set.AddLayer("Stroke and Shadow");
            set.GetPaintTarget(TexturePaintChannel.Albedo, TexturePaintSourceMode.SourceOverlay)
                .Reset(source, Color.clear);
            TexturePaintLayerEffectSettings shadow = layer.effects.outerShadow;
            shadow.enabled = true;
            shadow.channel = TexturePaintChannel.Albedo;
            shadow.color = Color.red;
            shadow.width = 4f;
            shadow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            TexturePaintLayerEffectSettings stroke = layer.effects.stroke;
            stroke.enabled = true;
            stroke.channel = TexturePaintChannel.Albedo;
            stroke.color = Color.black;
            stroke.width = 2f;
            stroke.smoothness = 0f;

            set.RecomposeAll();

            AssertColor(ReadPixel(channel.composite, 4, 8), Color.black, 0.03f);
            Color fadedShadow = ReadPixel(channel.composite, 3, 8);
            Assert.That(fadedShadow.r, Is.GreaterThan(0.05f).And.LessThan(0.6f));
            Assert.That(fadedShadow.a, Is.GreaterThan(0.05f).And.LessThan(0.6f));
            AssertColor(ReadPixel(channel.composite, 8, 8), Color.green, 0.03f);
            compositor.Dispose();
        }

        [Test]
        public void SoftInteriorCoverageDoesNotBecomeADistanceFieldBoundary()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Soft Effect Boundary", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Texture2D source = Own(new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true));
            Color[] pixels = new Color[16 * 16];
            for (int y = 4; y <= 11; y++)
            for (int x = 4; x <= 11; x++) pixels[y * 16 + x] = new Color(0f, 1f, 0f, 0.5f);
            source.SetPixels(pixels);
            source.Apply(false, false);
            TexturePaintLayer layer = set.AddLayer("Soft Interior");
            set.GetPaintTarget(TexturePaintChannel.Albedo, TexturePaintSourceMode.SourceOverlay)
                .Reset(source, Color.clear);
            TexturePaintLayerEffectSettings glow = layer.effects.innerGlow;
            glow.enabled = true;
            glow.channel = TexturePaintChannel.Albedo;
            glow.color = Color.red;
            glow.width = 4f;
            glow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

            set.RecomposeAll();

            Color center = ReadPixel(channel.composite, 8, 8);
            Assert.That(center.r, Is.LessThan(0.3f),
                "Uniform translucent interiors must not seed every pixel as an edge.");
            Assert.That(center.g, Is.GreaterThan(0.35f));
            Assert.That(center.a, Is.EqualTo(0.5f).Within(0.04f));
            compositor.Dispose();
        }

        [Test]
        public void TextureOverlayEffectCombinesTwoTintedSourcesInOrder()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Overlay Combination", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.EffectsAvailable, Is.True, "Layer effects GPU path is unavailable.");

            Texture2D source = Own(new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true));
            Color[] sourcePixels = new Color[16 * 16];
            for (int y = 4; y <= 11; y++)
            for (int x = 4; x <= 11; x++)
                sourcePixels[y * 16 + x] = new Color(0.4f, 0.4f, 0.4f, 1f);
            source.SetPixels(sourcePixels);
            source.Apply(false, false);
            TexturePaintLayer layer = set.AddLayer("Texture Overlay Source");
            EditableTextureTarget layerTarget = set.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            layerTarget.Reset(source, Color.clear);
            TexturePaintLayerEffectSettings effect = layer.effects.textureOverlay;
            effect.enabled = true;
            effect.channel = TexturePaintChannel.Albedo;
            effect.texture1 = CreateSolidTexture(Color.white);
            effect.texture2 = CreateSolidTexture(Color.white);
            effect.blendMode = TexturePaintBlendMode.Normal;
            effect.secondaryBlendMode = TexturePaintBlendMode.Multiply;
            effect.textureOpacity1 = 0.5f;
            effect.textureOpacity2 = 0.5f;
            effect.color = new Color(1f, 0.5f, 0.25f, 0.8f);
            effect.secondaryColor = new Color(0.2f, 0.4f, 1f, 0.5f);

            set.RecomposeAll();

            AssertColor(ReadPixel(channel.composite, 8, 8),
                new Color(0.512f, 0.374f, 0.34f, 1f), 0.015f);
            AssertColor(ReadPixel(layerTarget.Front, 8, 8),
                new Color(0.4f, 0.4f, 0.4f, 1f), 0.004f);
            AssertColor(ReadPixel(channel.composite, 1, 1), Color.clear, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void TextureOverlayEffectHonorsIndependentXYTiling()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Overlay Tiling", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;

            Texture2D source = CreateSolidTexture(Color.white);
            TexturePaintLayer layer = set.AddLayer("Tiled Texture Overlay Source");
            EditableTextureTarget layerTarget = set.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            layerTarget.Reset(source, Color.clear);
            TexturePaintLayerEffectSettings effect = layer.effects.textureOverlay;
            effect.enabled = true;
            effect.channel = TexturePaintChannel.Albedo;
            effect.texture1 = CreateSplitTexture(Color.red, Color.blue, true);
            effect.texture2 = CreateSplitTexture(Color.white, Color.black, false);
            effect.textureTiling1 = new Vector2(2f, 1f);
            effect.textureTiling2 = new Vector2(1f, 2f);
            effect.blendMode = TexturePaintBlendMode.Normal;
            effect.secondaryBlendMode = TexturePaintBlendMode.Multiply;
            effect.textureOpacity1 = 1f;
            effect.textureOpacity2 = 0.5f;
            effect.color = Color.white;
            effect.secondaryColor = Color.white;

            set.RecomposeAll();

            Color first = ReadPixel(channel.composite, 2, 2);
            AssertColor(ReadPixel(channel.composite, 10, 2), first, 0.04f);
            AssertColor(ReadPixel(channel.composite, 2, 10), first, 0.04f);
            Assert.That(RgbDistance(first, ReadPixel(channel.composite, 6, 2)),
                Is.GreaterThan(0.45f), "Texture 1 X tiling should repeat distinct horizontal regions.");
            Assert.That(RgbDistance(first, ReadPixel(channel.composite, 2, 6)),
                Is.GreaterThan(0.2f), "Texture 2 Y tiling should repeat distinct vertical regions.");
            AssertColor(ReadPixel(layerTarget.Front, 2, 2), Color.white, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void SpriteNormalSourceIsChannelConventionAndColorSpaceAware()
        {
            Vector3 authored = new Vector3(0.62f, -0.31f, 0.72f).normalized;
            Color encoded = new Color(authored.x * 0.5f + 0.5f, authored.y * 0.5f + 0.5f,
                authored.z * 0.5f + 0.5f, 0.63f);
            Texture2D sheet = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, false));
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = encoded;
            sheet.SetPixels(pixels);
            sheet.Apply(false, false);
            Sprite sprite = Own(Sprite.Create(sheet, new Rect(0f, 0f, 4f, 4f),
                new Vector2(0.5f, 0.5f)));

            Texture2D colorSource = TexturePaintSpriteSource.Resolve(null, sprite,
                TexturePaintChannel.Albedo, TexturePaintNormalConvention.OpenGL);
            Texture2D openGl = TexturePaintSpriteSource.Resolve(null, sprite,
                TexturePaintChannel.Normal, TexturePaintNormalConvention.OpenGL);
            Texture2D directX = TexturePaintSpriteSource.Resolve(null, sprite,
                TexturePaintChannel.Normal, TexturePaintNormalConvention.DirectX);

            Assert.That(colorSource, Is.Not.SameAs(openGl),
                "Changing from a color channel to Normal must select a different cached extraction.");
            Assert.That(openGl, Is.SameAs(TexturePaintSpriteSource.Resolve(null, sprite,
                TexturePaintChannel.Normal, TexturePaintNormalConvention.OpenGL)));
            Assert.That(openGl.isDataSRGB, Is.False, "Canonical normal sources must be linear data.");
            Assert.That(directX, Is.Not.SameAs(openGl),
                "Normal convention is part of the extracted-source cache key.");

            Color openGlSample = ReadTextureCenter(openGl);
            Color directXSample = ReadTextureCenter(directX);
            AssertColor(openGlSample, encoded, 0.012f);
            Assert.That(directXSample.r, Is.EqualTo(encoded.r).Within(0.012f));
            Assert.That(directXSample.g, Is.EqualTo(1f - encoded.g).Within(0.012f));
            Assert.That(directXSample.b, Is.EqualTo(encoded.b).Within(0.012f));
            Assert.That(directXSample.a, Is.EqualTo(encoded.a).Within(0.012f));
        }

        [Test]
        public void BrushPresetResolvesSpriteRegionAsStampTexture()
        {
            Texture2D sheet = Own(new Texture2D(6, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[24];
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 6; x++)
                pixels[y * 6 + x] = x >= 2 && x < 5 ? Color.white : Color.clear;
            sheet.SetPixels(pixels);
            sheet.Apply(false, false);
            Sprite sprite = Own(Sprite.Create(sheet, new Rect(2f, 0f, 3f, 4f),
                new Vector2(0.5f, 0.5f)));
            BrushPreset preset = Own(ScriptableObject.CreateInstance<BrushPreset>());
            preset.shape = BrushPreset.Shape.Stamp;
            preset.stampSprite = sprite;

            Texture2D resolved = preset.ResolvedStampTexture;

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.width, Is.EqualTo(3));
            Assert.That(resolved.height, Is.EqualTo(4));
            Assert.That(ReadTextureCenter(resolved).a, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void PaintStampRandomizationIsDeterministicBoundedAndDisabledForFollowRotation()
        {
            BrushPreset preset = Own(ScriptableObject.CreateInstance<BrushPreset>());
            preset.randomRotation = true;
            preset.randomSizeVariation = true;
            preset.randomSizeShrink = 0.3f;
            preset.randomSizeGrow = 0.3f;
            preset.splatter = true;
            preset.splatterDistance = 1.5f;
            preset.randomStrength = true;
            preset.size = 0.2f;
            StrokeSample first = new StrokeSample
            {
                footprintScale = Vector2.one,
                worldPosition = new Vector3(2f, 3f, 4f),
                worldNormal = Vector3.forward,
                direction = Vector3.right,
                sizeMultiplier = 1f,
                flowMultiplier = 1f
            };
            StrokeSample repeated = first;
            StrokeSample next = first;
            Vector3 originalPosition = first.worldPosition;

            TexturePaintStageWindow.ApplyPaintRandomVariation(ref first, preset, 147, 9);
            TexturePaintStageWindow.ApplyPaintRandomVariation(ref repeated, preset, 147, 9);
            TexturePaintStageWindow.ApplyPaintRandomVariation(ref next, preset, 147, 10);

            Assert.That(repeated.rotation, Is.EqualTo(first.rotation));
            Assert.That(repeated.sizeMultiplier, Is.EqualTo(first.sizeMultiplier));
            Assert.That(repeated.flowMultiplier, Is.EqualTo(first.flowMultiplier));
            Assert.That(repeated.worldPosition, Is.EqualTo(first.worldPosition));
            Assert.That(first.rotation, Is.GreaterThanOrEqualTo(0f).And.LessThan(360f));
            Assert.That(first.footprintScale, Is.EqualTo(Vector2.one),
                "Random size must not distort the brush footprint.");
            Assert.That(first.sizeMultiplier, Is.InRange(0.7f, 1.3f));
            Assert.That(first.flowMultiplier, Is.InRange(0f, 1f));
            Assert.That(TexturePaintStageWindow.CalculateEffectiveWorldBrushSize(preset, first, false),
                Is.EqualTo(0.2f * first.sizeMultiplier).Within(0.000001f));
            Vector3 splatterOffset = first.worldPosition - originalPosition;
            Assert.That(splatterOffset.magnitude,
                Is.LessThanOrEqualTo(0.2f * first.sizeMultiplier * 1.5f + 0.000001f));
            Assert.That(splatterOffset.z, Is.EqualTo(0f).Within(0.000001f),
                "Splatter must remain in the surface tangent plane.");
            Assert.That(next.rotation, Is.Not.EqualTo(first.rotation));
            Assert.That(next.sizeMultiplier, Is.Not.EqualTo(first.sizeMultiplier));
            Assert.That(next.flowMultiplier, Is.Not.EqualTo(first.flowMultiplier));
            Assert.That(next.worldPosition, Is.Not.EqualTo(first.worldPosition));

            preset.alignToStroke = true;
            StrokeSample follow = new StrokeSample
                { rotation = 23f, footprintScale = Vector2.one, flowMultiplier = 1f };
            TexturePaintStageWindow.ApplyPaintRandomVariation(ref follow, preset, 147, 10);
            Assert.That(follow.rotation, Is.EqualTo(23f),
                "Follow Stroke owns stamp rotation and must suppress random rotation.");
            Assert.That(follow.sizeMultiplier, Is.InRange(0.7f, 1.3f));

            preset.splatter = false;
            StrokeSample ordinary = new StrokeSample { flowMultiplier = 0.67f };
            TexturePaintStageWindow.ApplyPaintRandomVariation(ref ordinary, preset, 147, 11);
            Assert.That(ordinary.flowMultiplier, Is.EqualTo(0.67f),
                "Random Strength is a splatter-only option.");
        }

        [Test]
        public void BrushPresetPaintSettingsCopyIncludesAllBrushFeaturesButPreservesShelfTags()
        {
            BrushPreset source = Own(ScriptableObject.CreateInstance<BrushPreset>());
            BrushPreset destination = Own(ScriptableObject.CreateInstance<BrushPreset>());
            Texture2D stamp = Own(new Texture2D(2, 2));
            source.shape = BrushPreset.Shape.Stamp;
            source.stampTexture = stamp;
            source.size = 0.18f;
            source.hardness = 0.24f;
            source.flow = 0.63f;
            source.spacing = 1.7f;
            source.rotation = 37f;
            source.blendMode = TexturePaintBlendMode.Screen;
            source.mirrorStroke = true;
            source.alignToStroke = true;
            source.randomRotation = true;
            source.randomSizeVariation = true;
            source.randomSizeShrink = 0.12f;
            source.randomSizeGrow = 0.46f;
            source.splatter = true;
            source.splatterDistance = 1.84f;
            source.randomStrength = true;
            source.fade = true;
            source.autoFade = true;
            source.taper = true;
            source.autoTaper = true;
            source.fadeTaperLength = 0.72f;
            source.tags = "source tags";
            destination.tags = "keep these tags";

            destination.CopyPaintSettingsFrom(source);

            Assert.That(destination.shape, Is.EqualTo(source.shape));
            Assert.That(destination.stampTexture, Is.SameAs(stamp));
            Assert.That(destination.stampSprite, Is.Null);
            Assert.That(destination.size, Is.EqualTo(source.size));
            Assert.That(destination.hardness, Is.EqualTo(source.hardness));
            Assert.That(destination.flow, Is.EqualTo(source.flow));
            Assert.That(destination.spacing, Is.EqualTo(source.spacing));
            Assert.That(destination.rotation, Is.EqualTo(source.rotation));
            Assert.That(destination.blendMode, Is.EqualTo(source.blendMode));
            Assert.That(destination.mirrorStroke, Is.True);
            Assert.That(destination.alignToStroke, Is.True);
            Assert.That(destination.randomRotation, Is.True);
            Assert.That(destination.randomSizeVariation, Is.True);
            Assert.That(destination.randomSizeShrink, Is.EqualTo(source.randomSizeShrink));
            Assert.That(destination.randomSizeGrow, Is.EqualTo(source.randomSizeGrow));
            Assert.That(destination.splatter, Is.True);
            Assert.That(destination.splatterDistance, Is.EqualTo(source.splatterDistance));
            Assert.That(destination.randomStrength, Is.True);
            Assert.That(destination.fade, Is.True);
            Assert.That(destination.autoFade, Is.True);
            Assert.That(destination.taper, Is.True);
            Assert.That(destination.autoTaper, Is.True);
            Assert.That(destination.fadeTaperLength, Is.EqualTo(source.fadeTaperLength));
            Assert.That(destination.tags, Is.EqualTo("keep these tags"));
        }

        [Test]
        public void FadeAndTaperUseWorldDistanceAndComposeWithPressureSize()
        {
            BrushPreset preset = Own(ScriptableObject.CreateInstance<BrushPreset>());
            preset.size = 0.2f;
            preset.fade = true;
            preset.taper = true;
            Assert.That(preset.ResolvedFadeTaperLength, Is.EqualTo(0.6f).Within(0.000001f));
            StrokeSample halfway = new StrokeSample(Vector3.zero, Vector3.forward,
                Vector2.zero, 0, 0)
            {
                pressure = 0.4f,
                flowMultiplier = 1f,
                sizeMultiplier = 1f
            };

            TexturePaintStageWindow.ApplyStrokeEvolution(ref halfway, preset, 0.3f);

            Assert.That(halfway.flowMultiplier, Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(halfway.sizeMultiplier, Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(TexturePaintStageWindow.CalculateEffectiveWorldBrushSize(
                    preset, halfway, true),
                Is.EqualTo(0.2f * 0.5f * 0.4f).Within(0.000001f),
                "Pressure-driven size must multiply the taper envelope.");

            StrokeSample finished = new StrokeSample(Vector3.zero, Vector3.forward,
                Vector2.zero, 0, 0);
            TexturePaintStageWindow.ApplyStrokeEvolution(ref finished, preset, 0.6f);
            Assert.That(finished.flowMultiplier, Is.Zero);
            Assert.That(finished.sizeMultiplier, Is.Zero);

            preset.fadeTaperLength = 1.25f;
            Assert.That(preset.ResolvedFadeTaperLength, Is.EqualTo(1.25f));
        }

        [Test]
        public void AutoFadeAndTaperWaitForCompletionThenUseTheWholeStrokeLength()
        {
            BrushPreset preset = Own(ScriptableObject.CreateInstance<BrushPreset>());
            preset.fade = true;
            preset.autoFade = true;
            preset.taper = true;
            preset.autoTaper = true;
            preset.fadeTaperLength = 0.1f;
            StrokeSample live = new StrokeSample(Vector3.zero, Vector3.forward,
                Vector2.zero, 0, 0)
            {
                flowMultiplier = 1f,
                sizeMultiplier = 1f
            };

            TexturePaintStageWindow.ApplyStrokeEvolution(ref live, preset, 5f);

            Assert.That(live.flowMultiplier, Is.EqualTo(1f),
                "Auto Fade must not change the live stroke.");
            Assert.That(live.sizeMultiplier, Is.EqualTo(1f),
                "Auto Taper must not change the live stroke.");

            StrokeSample halfway = live;
            TexturePaintStageWindow.ApplyStrokeEvolution(ref halfway, preset, 5f, 10f, true);
            Assert.That(halfway.flowMultiplier, Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(halfway.sizeMultiplier, Is.EqualTo(0.5f).Within(0.000001f));

            StrokeSample endpoint = live;
            TexturePaintStageWindow.ApplyStrokeEvolution(ref endpoint, preset, 10f, 10f, true);
            Assert.That(endpoint.flowMultiplier, Is.Zero);
            Assert.That(endpoint.sizeMultiplier, Is.Zero);

            StrokeSample click = live;
            TexturePaintStageWindow.ApplyStrokeEvolution(ref click, preset, 0f, 0f, true);
            Assert.That(click.flowMultiplier, Is.EqualTo(1f));
            Assert.That(click.sizeMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void SaveCurrentBrushCreatesNamedAssetBesideLibraryAndAddsItToLibrary()
        {
            string libraryPath = Folder + "/Production Brushes.asset";
            BrushLibrary library = ScriptableObject.CreateInstance<BrushLibrary>();
            AssetDatabase.CreateAsset(library, libraryPath);
            BrushPreset current = Own(ScriptableObject.CreateInstance<BrushPreset>());
            current.shape = BrushPreset.Shape.Stamp;
            current.size = 0.137f;
            current.randomRotation = true;
            current.splatter = true;
            current.splatterDistance = 1.61f;
            current.randomStrength = true;
            current.fade = true;
            current.autoFade = true;
            current.taper = true;
            current.autoTaper = true;
            current.fadeTaperLength = 0.83f;

            BrushPreset created = TexturePaintStageWindow.CreateBrushAssetFromCurrentSettings(
                library, current, "Snow/Brush", out string createdPath, out string error);

            Assert.That(error, Is.Null);
            Assert.That(created, Is.Not.Null);
            Assert.That(createdPath, Is.EqualTo(Folder + "/Snow_Brush.asset"));
            Assert.That(AssetDatabase.GetAssetPath(created), Is.EqualTo(createdPath));
            Assert.That(library.Brushes, Has.Count.EqualTo(1));
            Assert.That(library.Brushes[0], Is.SameAs(created));
            Assert.That(created.shape, Is.EqualTo(current.shape));
            Assert.That(created.size, Is.EqualTo(current.size));
            Assert.That(created.randomRotation, Is.True);
            Assert.That(created.splatter, Is.True);
            Assert.That(created.splatterDistance, Is.EqualTo(current.splatterDistance));
            Assert.That(created.randomStrength, Is.True);
            Assert.That(created.fade, Is.True);
            Assert.That(created.autoFade, Is.True);
            Assert.That(created.taper, Is.True);
            Assert.That(created.autoTaper, Is.True);
            Assert.That(created.fadeTaperLength, Is.EqualTo(current.fadeTaperLength));
        }

        [Test]
        public void SpriteSheetDropCreatesNumberedStampBrushesBesideLibraryAndSkipsDuplicates()
        {
            string libraryPath = Folder + "/Production Brushes.asset";
            BrushLibrary library = ScriptableObject.CreateInstance<BrushLibrary>();
            AssetDatabase.CreateAsset(library, libraryPath);
            Texture2D sheet = new Texture2D(4, 2, TextureFormat.RGBA32, false, true)
            {
                name = "Stitch Sheet"
            };
            string sheetPath = Folder + "/Stitch Sheet.asset";
            AssetDatabase.CreateAsset(sheet, sheetPath);
            Sprite first = Sprite.Create(sheet, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            first.name = "Stitch Sheet_0";
            Sprite second = Sprite.Create(sheet, new Rect(2f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            second.name = "Stitch Sheet_1";
            AssetDatabase.AddObjectToAsset(first, sheet);
            AssetDatabase.AddObjectToAsset(second, sheet);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);
            sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);

            List<Sprite> importedSprites = new List<Sprite>();
            UnityEngine.Object[] sheetAssets = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
            for (int i = 0; i < sheetAssets.Length; i++)
                if (sheetAssets[i] is Sprite sprite) importedSprites.Add(sprite);
            Assert.That(importedSprites, Has.Count.EqualTo(2));

            List<BrushPreset> created = BrushLibrarySpriteSheetUtility.CreateBrushesFromSpriteSheet(
                library, sheet, out int skipped);

            Assert.That(skipped, Is.Zero);
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(library.Brushes, Has.Count.EqualTo(2));
            for (int i = 0; i < created.Count; i++)
            {
                Assert.That(created[i].name, Is.EqualTo("Stitch Sheet " + (i + 1)));
                Assert.That(created[i].shape, Is.EqualTo(BrushPreset.Shape.Stamp));
                Assert.That(created[i].stampTexture, Is.Null);
                Assert.That(created[i].stampSprite, Is.SameAs(importedSprites[i]));
                Assert.That(Path.GetDirectoryName(AssetDatabase.GetAssetPath(created[i]))?.Replace('\\', '/'),
                    Is.EqualTo(Folder));
            }

            List<BrushPreset> duplicatePass = BrushLibrarySpriteSheetUtility.CreateBrushesFromSpriteSheet(
                library, sheet, out skipped);
            Assert.That(duplicatePass, Is.Empty);
            Assert.That(skipped, Is.EqualTo(2));
            Assert.That(library.Brushes, Has.Count.EqualTo(2));
        }

        [Test]
        public void GrayscaleDirectTextureSourceCreatesAndCachesCanonicalTexture()
        {
            Color authored = new Color(0.18f, 0.37f, 0.76f, 0.42f);
            Texture2D source = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = authored;
            source.SetPixels(pixels);
            source.Apply(false, false);

            Texture2D unchanged = TexturePaintSpriteSource.Resolve(source, null,
                TexturePaintChannel.Roughness, TexturePaintNormalConvention.OpenGL, false);
            Texture2D inverted = TexturePaintSpriteSource.Resolve(source, null,
                TexturePaintChannel.Roughness, TexturePaintNormalConvention.OpenGL, true);

            float grayscale = authored.r * 0.2126f + authored.g * 0.7152f + authored.b * 0.0722f;
            Assert.That(unchanged, Is.Not.Null);
            Assert.That(unchanged, Is.Not.SameAs(source),
                "Scalar channels must canonicalize RGB sources to a grayscale paint texture.");
            Assert.That(unchanged, Is.SameAs(TexturePaintSpriteSource.Resolve(source, null,
                TexturePaintChannel.Roughness, TexturePaintNormalConvention.OpenGL, false)));
            AssertColor(ReadTextureCenter(unchanged),
                new Color(grayscale, grayscale, grayscale, authored.a), 0.012f);
            Assert.That(inverted, Is.Not.Null);
            Assert.That(inverted, Is.Not.SameAs(source),
                "Inverting a direct texture must create the same temporary source used for sprite extraction.");
            Assert.That(inverted, Is.SameAs(TexturePaintSpriteSource.Resolve(source, null,
                TexturePaintChannel.Roughness, TexturePaintNormalConvention.OpenGL, true)));
            AssertColor(ReadTextureCenter(inverted),
                new Color(1f - grayscale, 1f - grayscale, 1f - grayscale, authored.a), 0.012f);
        }

        [Test]
        public void PackedOverlaySourcesExtractTheRequestedLogicalComponent()
        {
            Color authored = new Color(0.18f, 0.37f, 0.76f, 0.42f);
            Texture2D source = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = authored;
            source.SetPixels(pixels);
            source.Apply(false, false);

            Texture detail = TexturePaintSpriteSource.ResolveTextureComponent(source,
                TexturePaintChannel.DetailMask, TexturePaintNormalConvention.OpenGL, 2, false);
            Texture roughness = TexturePaintSpriteSource.ResolveTextureComponent(source,
                TexturePaintChannel.Roughness, TexturePaintNormalConvention.OpenGL, 3, true);

            AssertColor(ReadTextureCenter(detail), new Color(authored.b, authored.b, authored.b, 1f),
                0.012f);
            AssertColor(ReadTextureCenter(roughness),
                new Color(1f - authored.a, 1f - authored.a, 1f - authored.a, 1f), 0.012f);
            Assert.That(detail, Is.SameAs(TexturePaintSpriteSource.ResolveTextureComponent(source,
                TexturePaintChannel.DetailMask, TexturePaintNormalConvention.OpenGL, 2, false)));
        }

        [Test]
        public void UnityNormalMapTextureIsCanonicalizedBeforePainting()
        {
            Vector3 authored = new Vector3(-0.46f, 0.27f, 0.84f).normalized;
            Color encoded = new Color(authored.x * 0.5f + 0.5f, authored.y * 0.5f + 0.5f,
                authored.z * 0.5f + 0.5f, 1f);
            Texture2D fileTexture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = encoded;
            fileTexture.SetPixels(pixels);
            fileTexture.Apply(false, false);
            string path = Folder + "/Canonical Normal Source.png";
            File.WriteAllBytes(Path.GetFullPath(path), fileTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            importer.textureType = TextureImporterType.NormalMap;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(imported, Is.Not.Null);

            Texture2D canonical = TexturePaintSpriteSource.Resolve(imported, null,
                TexturePaintChannel.Normal, TexturePaintNormalConvention.OpenGL);

            Assert.That(canonical, Is.Not.Null);
            Assert.That(canonical, Is.Not.SameAs(imported));
            Assert.That(canonical.isDataSRGB, Is.False);
            AssertColor(ReadTextureCenter(canonical), encoded, 0.025f);
        }

        [Test]
        public void UnityNormalMapMaterialChannelIsCanonicalizedBeforeCompositingAndExport()
        {
            Vector3 authored = new Vector3(0.41f, -0.33f, 0.85f).normalized;
            Color encoded = new Color(authored.x * 0.5f + 0.5f, authored.y * 0.5f + 0.5f,
                authored.z * 0.5f + 0.5f, 1f);
            Texture2D fileTexture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = encoded;
            fileTexture.SetPixels(pixels);
            fileTexture.Apply(false, false);
            string path = Folder + "/Material Normal Source.png";
            File.WriteAllBytes(Path.GetFullPath(path), fileTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            importer.textureType = TextureImporterType.NormalMap;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(imported, Is.Not.Null);

            TextureStore store = new TextureStore();
            TextureSet set = new TextureSet();
            AddSet(store, set);
            ownedStores.Add(store);
            MethodInfo addChannel = typeof(TextureStore).GetMethod("AddChannel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(addChannel, Is.Not.Null);
            addChannel.Invoke(store, new object[]
            {
                set, TexturePaintChannel.Normal, "_BumpMap", "Normal", 0, imported,
                RenderTextureFormat.ARGB32, TexturePaintNormalConvention.OpenGL, false
            });

            TextureChannelTarget target = set.GetChannel(TexturePaintChannel.Normal);
            Assert.That(target, Is.Not.Null);
            Assert.That(target.sourceTexture, Is.SameAs(imported),
                "The original material texture must remain available for source bindings and metadata.");
            AssertColor(ReadCenter(target.editable.Front), encoded, 0.025f);

            target.editable.Reset(null, Color.red);
            set.ClearModifications();
            AssertColor(ReadCenter(target.editable.Front), encoded, 0.025f);
        }

        [Test]
        public void NativeUmaPackedNormalCompositeIsDecodedBeforeCompositingAndExport()
        {
            Vector3 authored = new Vector3(-0.38f, 0.29f, 0.88f).normalized;
            Color encoded = new Color(authored.x * 0.5f + 0.5f, authored.y * 0.5f + 0.5f,
                authored.z * 0.5f + 0.5f, 1f);
            RenderTexture packedComposite = Own(CreateRenderTexture("UMA Native Packed Normal Composite", 16,
                RenderTextureFormat.ARGB32));
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = packedComposite;
            // UMA's NormalSwizzle post-process uses Unity's runtime normal representation:
            // green stores Y, alpha stores X, and red/blue are not authored RGB normal data.
            GL.Clear(false, true, new Color(1f, encoded.g, 1f, encoded.r));
            RenderTexture.active = previous;

            TextureStore store = new TextureStore();
            TextureSet set = new TextureSet();
            AddSet(store, set);
            ownedStores.Add(store);
            MethodInfo addChannel = typeof(TextureStore).GetMethod("AddChannel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(addChannel, Is.Not.Null);
            addChannel.Invoke(store, new object[]
            {
                set, TexturePaintChannel.Normal, "_BumpMap", "Normal", 0, packedComposite,
                RenderTextureFormat.ARGB32, TexturePaintNormalConvention.OpenGL, true
            });

            TextureChannelTarget target = set.GetChannel(TexturePaintChannel.Normal);
            Assert.That(target, Is.Not.Null);
            Assert.That(target.sourceNormalIsUnityPacked, Is.True);
            AssertColor(ReadCenter(target.editable.Front), encoded, 0.025f);

            Graphics.Blit(target.editable.Front, target.composite);
            ConfigureNormalExportDescriptor(set, packedComposite);
            TexturePaintExportTemplate template = CreateTemplate(TexturePaintExportBitDepth.Eight);
            TexturePaintExportResult export = TexturePaintExporter.Export(store, set, null, template, null);
            Assert.That(export.texturePaths, Has.Count.EqualTo(1));
            Texture2D rawFile = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            Assert.That(rawFile.LoadImage(File.ReadAllBytes(Path.GetFullPath(export.texturePaths[0])), false),
                Is.True);
            Color exported = rawFile.GetPixel(rawFile.width / 2, rawFile.height / 2);
            AssertColor(exported, new Color(encoded.r, encoded.g, encoded.b, 0f), 0.025f);
        }

        [Test]
        public void ChangingActiveChannelRefreshesSelectedSpriteExtraction()
        {
            Texture2D sheet = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, false));
            sheet.SetPixels(new[]
            {
                new Color(0.75f, 0.25f, 1f, 1f), new Color(0.75f, 0.25f, 1f, 1f),
                new Color(0.75f, 0.25f, 1f, 1f), new Color(0.75f, 0.25f, 1f, 1f)
            });
            sheet.Apply(false, false);
            Sprite sprite = Own(Sprite.Create(sheet, new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f)));
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo spriteField = typeof(TexturePaintStageWindow).GetField("paintSourceSprite", flags);
            FieldInfo textureField = typeof(TexturePaintStageWindow).GetField("paintSourceTexture", flags);
            MethodInfo selectChannel = typeof(TexturePaintStageWindow).GetMethod(
                "SetSelectedChannelAndRefreshSource", flags);
            Assert.That(spriteField, Is.Not.Null);
            Assert.That(textureField, Is.Not.Null);
            Assert.That(selectChannel, Is.Not.Null);
            spriteField.SetValue(stage, sprite);

            selectChannel.Invoke(stage, new object[] { TexturePaintChannel.Albedo });
            Texture2D colorExtraction = textureField.GetValue(stage) as Texture2D;
            selectChannel.Invoke(stage, new object[] { TexturePaintChannel.Normal });
            Texture2D normalExtraction = textureField.GetValue(stage) as Texture2D;

            Assert.That(colorExtraction, Is.Not.Null);
            Assert.That(normalExtraction, Is.Not.Null);
            Assert.That(normalExtraction, Is.Not.SameAs(colorExtraction));
            Assert.That(normalExtraction.isDataSRGB, Is.False);
        }

        [Test]
        public void SpriteSourceDoesNotRequireAPreExtractedTextureToBeginStroke()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Texture2D sheet = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, false));
            sheet.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
            sheet.Apply(false, false);
            Sprite sprite = Own(Sprite.Create(sheet, new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f)));
            BrushPreset brush = Own(fixture.CreateBrush());
            using PaintingEngine engine = new PaintingEngine(null, null, null);
            StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white);
            context.paintSource = TexturePaintBrushSource.Texture;
            context.sourceTexture = null;
            context.sourceSprite = sprite;

            Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True,
                "Sprite-backed spline and runtime contexts must reach channel-aware extraction " +
                "without needing to retain a generated temporary Texture2D.");
            engine.EndStroke(false);
        }

        [Test]
        public void EndingInteractiveReplacementRebuildsDistanceEffectsFromCompletedLayer()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Interactive Effects", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            Assert.That(compositor.EffectsAvailable, Is.True, "Layer effects GPU path is unavailable.");

            TexturePaintLayer layer = set.AddLayer("Interactive Path Result");
            EditableTextureTarget layerTarget = set.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            layer.effects.innerGlow.enabled = true;
            layer.effects.innerGlow.channel = TexturePaintChannel.Albedo;
            layer.effects.innerGlow.color = Color.white;
            layer.effects.innerGlow.width = 1f;
            layer.effects.innerGlow.level = 1f;

            // A procedural reapply clears the old result before entering interactive painting.
            // Cache that empty distance field, exactly as the path replacement workflow does.
            set.RecomposeAll();
            Texture2D replacement = Own(new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true));
            Color[] pixels = new Color[16 * 16];
            for (int y = 2; y <= 13; y++)
            for (int x = 2; x <= 13; x++) pixels[y * 16 + x] = Color.green;
            replacement.SetPixels(pixels);
            replacement.Apply(false, false);
            BrushPreset brush = Own(ScriptableObject.CreateInstance<BrushPreset>());
            using PaintingEngine engine = new PaintingEngine(null, null, null);
            StrokeContext context = new StrokeContext
            {
                textures = set,
                brush = brush,
                tool = TexturePaintTool.Paint,
                channel = TexturePaintChannel.Albedo,
                paintSource = TexturePaintBrushSource.Color,
                color = Color.green,
                strength = 1f
            };

            Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
            layerTarget.Reset(replacement, Color.clear);
            set.RecomposeAll();
            AssertColor(ReadPixel(channel.composite, 8, 8), Color.green, 0.02f);

            engine.EndStroke();

            AssertColor(ReadPixel(channel.composite, 8, 8), Color.green, 0.02f);
            compositor.Dispose();
        }

        [Test]
        public void ActivePaintLayerOwnsStrokeWhenLegacyBaseDestinationIsRequested()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            TexturePaintLayer layer = fixture.set.AddLayer("Only Paint Layer");
            BrushPreset brush = fixture.CreateBrush();
            using PaintingEngine engine = new PaintingEngine(null, null, null);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green);

                Assert.That(PaintingEngine.ResolveDestinationMode(TexturePaintSourceMode.SourceTexture,
                    fixture.set), Is.EqualTo(TexturePaintSourceMode.SourceOverlay));
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(layer.channels.ContainsKey(TexturePaintChannel.Albedo), Is.True,
                    "An active paint layer must receive the editable target.");
                Assert.That(layer.strokes, Has.Count.EqualTo(1),
                    "Stroke metadata must be owned by the active layer.");
                Assert.That(fixture.set.baseStrokes, Is.Empty,
                    "A stale base-destination setting must not silently make a layer stroke destructive.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RemovingOnlyLayerRecomposesUntouchedBaseWithoutPreviewMaterial()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Color baseColor = new Color(0.13f, 0.31f, 0.67f, 1f);
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, baseColor);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("Texture Paint Delete Layer Composite", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;
            TexturePaintLayer layer = set.AddLayer("Only Paint Layer");
            set.GetPaintTarget(TexturePaintChannel.Albedo, TexturePaintSourceMode.SourceOverlay)
                .Reset(null, Color.green);
            set.RecomposeAll();
            AssertColor(ReadCenter(channel.composite), Color.green, 0.004f);

            Assert.That(set.RemoveLayerAt(set.layers.IndexOf(layer)), Is.True);

            Assert.That(set.layers, Is.Empty);
            AssertColor(ReadCenter(channel.composite), baseColor, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void FlatTextureFillGeneratesAndCachesTiledLayerPixels()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader"));
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.filterMode = FilterMode.Point;
            source.SetPixels(new[] { Color.red, Color.blue, Color.red, Color.blue });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat,
                tiling = new Vector2(2f, 1f)
            };

            TexturePaintLayer layer = fixture.set.AddFillLayer("Tiled", TexturePaintChannel.Albedo, settings);

            Assert.That(layer, Is.Not.Null);
            Color[] generated = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front);
            int y = TexturePaintGpuTestFixture.Size / 2;
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 8], Color.red, 0.02f);
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 24], Color.blue, 0.02f);
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 40], Color.red, 0.02f);
            AssertColor(generated[y * TexturePaintGpuTestFixture.Size + 56], Color.blue, 0.02f);

            source.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green });
            source.Apply(false, false);
            Color cached = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front)[y * TexturePaintGpuTestFixture.Size + 8];
            AssertColor(cached, Color.red, 0.02f);
        }

        [Test]
        public void ArbitraryOverlayAssetResolvesEveryCompatibleMaterialChannel()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            AddChannel(set, TexturePaintChannel.Roughness, Color.white);
            CreateStore(set);
            set.GetChannel(TexturePaintChannel.Albedo).umaChannelIndex = 0;
            set.GetChannel(TexturePaintChannel.Roughness).umaChannelIndex = 1;
            Texture2D albedo = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            Texture2D roughness = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            OverlayDataAsset overlay = Own(ScriptableObject.CreateInstance<OverlayDataAsset>());
            overlay.textureList = new Texture[] { albedo, roughness };

            Assert.That(set.TryResolveOverlaySource(overlay, TexturePaintChannel.Albedo,
                TexturePaintNormalConvention.OpenGL, false, out Texture resolvedAlbedo,
                out int albedoIndex), Is.True);
            Assert.That(set.TryResolveOverlaySource(overlay, TexturePaintChannel.Roughness,
                TexturePaintNormalConvention.OpenGL, false, out Texture resolvedRoughness,
                out int roughnessIndex), Is.True);
            Assert.That(resolvedAlbedo, Is.SameAs(albedo));
            Assert.That(resolvedRoughness, Is.Not.Null);
            Assert.That(albedoIndex, Is.EqualTo(0));
            Assert.That(roughnessIndex, Is.EqualTo(1));
        }

        [Test]
        public void FillCanLeaveCoverageToASharedLayerMaskWithoutSquaringSourceAlpha()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader"));
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.SetPixels(new[]
            {
                new Color(1f, 0f, 0f, 0.25f), new Color(1f, 0f, 0f, 0.25f),
                new Color(1f, 0f, 0f, 0.25f), new Color(1f, 0f, 0f, 0.25f)
            });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat,
                ignoreSourceAlpha = true
            };

            TexturePaintLayer layer = fixture.set.AddFillLayer("Shared Coverage",
                TexturePaintChannel.Albedo, settings);

            Assert.That(layer, Is.Not.Null);
            Color center = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front)
                [(TexturePaintGpuTestFixture.Size / 2) * TexturePaintGpuTestFixture.Size +
                 TexturePaintGpuTestFixture.Size / 2];
            Assert.That(center.a, Is.EqualTo(1f).Within(0.02f));
            AssertColor(new Color(center.r, center.g, center.b, 1f), Color.red, 0.02f);
        }

        [Test]
        public void FlatTextureFillAppliesOffsetAndRotationAndRegeneratesPixels()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader"));
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.filterMode = FilterMode.Point;
            source.wrapMode = TextureWrapMode.Repeat;
            source.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.yellow });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat
            };
            TexturePaintLayer layer = fixture.set.AddFillLayer("Transformed",
                TexturePaintChannel.Albedo, settings);
            int sample = 8 * TexturePaintGpuTestFixture.Size + 8;
            AssertColor(TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front)[sample], Color.red, 0.02f);

            settings.offset = new Vector2(0.5f, 0f);
            Assert.That(fixture.set.UpdateFillLayer(layer, TexturePaintChannel.Albedo, settings), Is.True);
            AssertColor(TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front)[sample], Color.green, 0.02f);

            settings.offset = Vector2.zero;
            settings.rotation = 90f;
            Assert.That(fixture.set.UpdateFillLayer(layer, TexturePaintChannel.Albedo, settings), Is.True);
            AssertColor(TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front)[sample], Color.green, 0.02f);
        }

        [Test]
        public void FillCanDriveEveryChannelTransformFromItsFirstChannel()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.clear);
            AddChannel(set, TexturePaintChannel.Roughness, Color.white);
            CreateStore(set);
            TexturePaintFillSettings master = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Color,
                color = Color.red,
                tiling = new Vector2(2.5f, 4f),
                offset = new Vector2(0.2f, -0.35f),
                rotation = 47f,
                useFirstChannelTransform = true
            };
            TexturePaintLayer layer = set.AddFillLayer("Shared Transform",
                TexturePaintChannel.Albedo, master);
            Assert.That(layer, Is.Not.Null);
            TextureChannelTarget roughnessBase = set.GetChannel(TexturePaintChannel.Roughness);
            layer.channels[TexturePaintChannel.Roughness] = new EditableTextureTarget(
                "Shared Transform Roughness", roughnessBase.Texture.width, roughnessBase.Texture.height,
                roughnessBase.format, null, Color.clear);
            layer.GetChannelSettings(TexturePaintChannel.Roughness).sourceSettings =
                new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Color,
                    color = Color.gray,
                    tiling = new Vector2(9f, 11f),
                    offset = new Vector2(-0.8f, 0.7f),
                    rotation = -120f
                };

            Assert.That(set.RegenerateFillLayer(layer), Is.True);
            TexturePaintChannelSourceSettings roughness =
                layer.GetChannelSettings(TexturePaintChannel.Roughness).sourceSettings;
            Assert.That(roughness.tiling, Is.EqualTo(master.tiling));
            Assert.That(roughness.offset, Is.EqualTo(master.offset));
            Assert.That(roughness.rotation, Is.EqualTo(master.rotation));

            TexturePaintFillSettings attemptedSecondaryTransform = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Color,
                color = Color.black,
                tiling = new Vector2(20f, 30f),
                offset = new Vector2(0.9f, 0.8f),
                rotation = 170f,
                useFirstChannelTransform = true
            };
            Assert.That(set.UpdateFillLayer(layer, TexturePaintChannel.Roughness,
                attemptedSecondaryTransform), Is.True);
            roughness = layer.GetChannelSettings(TexturePaintChannel.Roughness).sourceSettings;
            Assert.That(roughness.tiling, Is.EqualTo(master.tiling));
            Assert.That(roughness.offset, Is.EqualTo(master.offset));
            Assert.That(roughness.rotation, Is.EqualTo(master.rotation));
            Assert.That(layer.fillChannel, Is.EqualTo(TexturePaintChannel.Albedo));
            Assert.That(layer.fillSettings.useFirstChannelTransform, Is.True);
        }

        [Test]
        public void ChangingFillSourceInvertRegeneratesLayerPixels()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader"));
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Color authored = new Color(0.16f, 0.39f, 0.72f, 1f);
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.SetPixels(new[] { authored, authored, authored, authored });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source
            };
            TexturePaintLayer layer = fixture.set.AddFillLayer("Gloss To Roughness",
                TexturePaintChannel.Albedo, settings);
            Assert.That(layer, Is.Not.Null);
            AssertColor(ReadCenter(layer.channels[TexturePaintChannel.Albedo].Front), authored, 0.012f);

            settings.invert = true;
            Assert.That(fixture.set.UpdateFillLayer(layer, TexturePaintChannel.Albedo, settings), Is.True);

            Assert.That(layer.fillSettings.invert, Is.True);
            AssertColor(ReadCenter(layer.channels[TexturePaintChannel.Albedo].Front),
                new Color(1f - authored.r, 1f - authored.g, 1f - authored.b, authored.a), 0.012f);
        }

        [Test]
        public void TextureFillGeneratesOpaquePaddingOutsideUVIsland()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            fixture.mesh.uv = new[]
            {
                new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f),
                new Vector2(0.75f, 0.75f), new Vector2(0.25f, 0.75f)
            };
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader"));
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.SetPixels(new[] { Color.blue, Color.blue, Color.blue, Color.blue });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat,
                tiling = Vector2.one
            };

            TexturePaintLayer layer = fixture.set.AddFillLayer("Padded", TexturePaintChannel.Albedo, settings);

            Assert.That(layer, Is.Not.Null);
            Color[] generated = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front);
            int y = TexturePaintGpuTestFixture.Size / 2;
            Color padded = generated[y * TexturePaintGpuTestFixture.Size + 14];
            Assert.That(padded.a, Is.GreaterThan(0.95f),
                "Fill coverage must extend two texels beyond the UV island for bilinear preview filtering.");
            AssertColor(padded, Color.blue, 0.02f);
        }

        [Test]
        public void FlatTextureFillKeepsSourceYOrientation()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader"));
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            source.filterMode = FilterMode.Point;
            // Texture2D pixel arrays are bottom row first: red below, blue above.
            source.SetPixels(new[] { Color.red, Color.red, Color.blue, Color.blue });
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Flat,
                tiling = Vector2.one
            };

            TexturePaintLayer layer = fixture.set.AddFillLayer("Oriented", TexturePaintChannel.Albedo, settings);

            Assert.That(layer, Is.Not.Null);
            Color[] generated = TexturePaintGpuTestFixture.ReadPixels(
                layer.channels[TexturePaintChannel.Albedo].Front);
            int x = TexturePaintGpuTestFixture.Size / 2;
            AssertColor(generated[8 * TexturePaintGpuTestFixture.Size + x], Color.red, 0.02f);
            AssertColor(generated[56 * TexturePaintGpuTestFixture.Size + x], Color.blue, 0.02f);
        }

        [Test]
        public void TriplanarCrossFadeBlendsProjectionAxes()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader"));
            Assert.That(shader, Is.Not.Null, "Missing production Fill generator shader.");
            using TexturePaintFillGenerator generator = new TexturePaintFillGenerator(shader);
            fixture.set.fillGenerator = generator;
            Vector3 diagonal = Vector3.one.normalized;
            fixture.mesh.normals = new[] { diagonal, diagonal, diagonal, diagonal };
            const int sourceSize = 32;
            Texture2D source = Own(new Texture2D(sourceSize, sourceSize, TextureFormat.RGBAFloat, false, true));
            Color[] gradient = new Color[sourceSize * sourceSize];
            for (int y = 0; y < sourceSize; y++)
            for (int x = 0; x < sourceSize; x++)
                gradient[y * sourceSize + x] = new Color(x / (sourceSize - 1f), y / (sourceSize - 1f), 0f, 1f);
            source.SetPixels(gradient);
            source.Apply(false, false);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = source,
                projection = TexturePaintFillProjection.Triplanar,
                tiling = Vector2.one,
                triplanarBlend = TexturePaintTriplanarBlend.Hard
            };
            TexturePaintLayer hard = fixture.set.AddFillLayer("Hard", TexturePaintChannel.Albedo, settings);
            Assert.That(hard, Is.Not.Null);
            // At the exact center, repeat-filtered samples from the three projection axes all
            // evaluate to approximately (0.5, 0.5) for this gradient. Sample away from that
            // symmetry so the assertion actually distinguishes Hard from Cross Fade.
            int sampleX = TexturePaintGpuTestFixture.Size / 2;
            int sampleY = TexturePaintGpuTestFixture.Size / 8;
            Color hardCenter = TexturePaintGpuTestFixture.ReadPixels(
                hard.channels[TexturePaintChannel.Albedo].Front)[sampleY * TexturePaintGpuTestFixture.Size + sampleX];

            settings.triplanarBlend = TexturePaintTriplanarBlend.CrossFade;
            settings.blendSharpness = 1f;
            TexturePaintLayer blended = fixture.set.AddFillLayer("Cross Fade", TexturePaintChannel.Albedo, settings);
            Assert.That(blended, Is.Not.Null);
            Color blendedCenter = TexturePaintGpuTestFixture.ReadPixels(
                blended.channels[TexturePaintChannel.Albedo].Front)[sampleY * TexturePaintGpuTestFixture.Size + sampleX];

            float difference = Mathf.Abs(blendedCenter.r - hardCenter.r) +
                               Mathf.Abs(blendedCenter.g - hardCenter.g);
            Assert.That(difference, Is.GreaterThan(0.1f),
                $"Cross Fade should combine axes instead of selecting the hard dominant projection. " +
                $"Hard={hardCenter}, CrossFade={blendedCenter}.");
        }

        [Test]
        public void FillProjectionSettingsAndCachedPixelsSurviveDocumentRoundTrip()
        {
            Material material = Own(new Material(Shader.Find("Standard")) { name = "Fill Persistence Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet original = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore originalStore = CreateStore(original);
            TexturePaintFillSettings settings = new TexturePaintFillSettings
            {
                source = TexturePaintBrushSource.Color,
                color = new Color(0.31f, 0.57f, 0.83f, 0.72f),
                projection = TexturePaintFillProjection.Triplanar,
                tiling = new Vector2(2.5f, 4.25f),
                offset = new Vector2(0.125f, -0.375f),
                rotation = 37f,
                useFirstChannelTransform = true,
                triplanarBlend = TexturePaintTriplanarBlend.CrossFade,
                blendOffset = 0.17f,
                blendSharpness = 7f
            };
            Assert.That(original.AddFillLayer("Projected Fill", TexturePaintChannel.Albedo, settings), Is.Not.Null);
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            AssetDatabase.CreateAsset(document, Folder + "/Fill Round Trip.asset");
            TexturePaintDocumentStorage.Save(document, originalStore);

            TextureSet restored = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore restoredStore = CreateStore(restored);
            TexturePaintDocumentStorage.Restore(document, restoredStore);

            Assert.That(restored.layers, Has.Count.EqualTo(1));
            TexturePaintLayer fill = restored.layers[0];
            Assert.That(fill.kind, Is.EqualTo(TexturePaintLayerKind.Fill));
            Assert.That(fill.fillSettings.source, Is.EqualTo(TexturePaintBrushSource.Color));
            Assert.That(fill.fillSettings.projection, Is.EqualTo(TexturePaintFillProjection.Triplanar));
            Assert.That(fill.fillSettings.tiling, Is.EqualTo(new Vector2(2.5f, 4.25f)));
            Assert.That(fill.fillSettings.offset, Is.EqualTo(new Vector2(0.125f, -0.375f)));
            Assert.That(fill.fillSettings.rotation, Is.EqualTo(37f));
            Assert.That(fill.fillSettings.useFirstChannelTransform, Is.True);
            Assert.That(fill.fillSettings.triplanarBlend, Is.EqualTo(TexturePaintTriplanarBlend.CrossFade));
            Assert.That(fill.fillSettings.blendOffset, Is.EqualTo(0.17f).Within(0.0001f));
            Assert.That(fill.fillSettings.blendSharpness, Is.EqualTo(7f).Within(0.0001f));
            AssertColor(ReadCenter(fill.channels[TexturePaintChannel.Albedo].Front), settings.color, 0.004f);
        }

        [Test]
        public void PaintLayerToolSettingsSurviveDocumentRoundTrip()
        {
            Material material = Own(new Material(Shader.Find("Standard")) { name = "Paint Settings Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet original = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore originalStore = CreateStore(original);
            TexturePaintLayer layer = original.AddLayer("Remembered Paint");
            layer.paintSettings = new TexturePaintLayerSettings
            {
                tool = TexturePaintTool.Clone,
                channel = TexturePaintChannel.Albedo,
                source = TexturePaintBrushSource.Texture,
                destination = TexturePaintSourceMode.SourceOverlay,
                color = new Color(0.2f, 0.4f, 0.6f, 0.8f),
                strength = 0.37f,
                mirrorX = true,
                stabilization = 0.22f,
                directionSmoothing = 0.63f,
                projectionDepth = 1.25f,
                normalAngleLimit = 54f,
                paintBackfaces = true,
                pressureAffectsFlow = false,
                pressureAffectsSize = true,
                brushSplatter = true,
                brushSplatterDistance = 1.47f,
                brushRandomStrength = true
            };
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            AssetDatabase.CreateAsset(document, Folder + "/Paint Settings Round Trip.asset");
            TexturePaintDocumentStorage.Save(document, originalStore);

            TextureSet restored = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            TextureStore restoredStore = CreateStore(restored);
            TexturePaintDocumentStorage.Restore(document, restoredStore);

            Assert.That(restored.layers, Has.Count.EqualTo(1));
            TexturePaintLayerSettings settings = restored.layers[0].paintSettings;
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.tool, Is.EqualTo(TexturePaintTool.Clone));
            Assert.That(settings.source, Is.EqualTo(TexturePaintBrushSource.Texture));
            Assert.That(settings.strength, Is.EqualTo(0.37f).Within(0.0001f));
            Assert.That(settings.mirrorX, Is.True);
            Assert.That(settings.directionSmoothing, Is.EqualTo(0.63f).Within(0.0001f));
            Assert.That(settings.projectionDepth, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(settings.normalAngleLimit, Is.EqualTo(54f).Within(0.0001f));
            Assert.That(settings.paintBackfaces, Is.True);
            Assert.That(settings.pressureAffectsFlow, Is.False);
            Assert.That(settings.pressureAffectsSize, Is.True);
            Assert.That(settings.brushSplatter, Is.True);
            Assert.That(settings.brushSplatterDistance, Is.EqualTo(1.47f));
            Assert.That(settings.brushRandomStrength, Is.True);
        }

        [Test]
        public void UvLayoutChangeResetsMaskPixelsButRetainsMaskConfiguration()
        {
            Material material = Own(new Material(Shader.Find("Standard")) { name = "UV Mask Material" });
            Mesh originalMesh = Own(CreateQuadMesh());
            TextureSet original = CreateSet(TexturePaintChannel.Albedo, Color.black, material, originalMesh);
            TextureStore originalStore = CreateStore(original);
            TexturePaintLayer layer = original.AddLayer("UV Sensitive Mask");
            TexturePaintLayerMask mask = original.AddLayerMask(layer, 0f);
            mask.target.Reset(null, TextureSet.MaskColor(1f));
            mask.effects.noise.enabled = true;
            mask.effects.noise.seed = 19;
            mask.sourceSettings = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Color,
                color = new Color(0.25f, 0.25f, 0.25f, 1f)
            };

            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            AssetDatabase.CreateAsset(document, Folder + "/UV Mask Reset.asset");
            TexturePaintDocumentStorage.Save(document, originalStore);

            Mesh changedMesh = Own(CreateQuadMesh());
            changedMesh.uv = new[]
            {
                new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f),
                new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f)
            };
            TextureSet restored = CreateSet(TexturePaintChannel.Albedo, Color.black, material, changedMesh);
            TextureStore restoredStore = CreateStore(restored);

            TexturePaintDocumentStorage.Restore(document, restoredStore);

            Assert.That(restored.layers, Has.Count.EqualTo(1));
            TexturePaintLayerMask restoredMask = restored.layers[0].layerMask;
            Assert.That(restoredMask, Is.Not.Null);
            AssertColor(ReadCenter(restoredMask.target.Front), TextureSet.MaskColor(0f), 0.01f);
            Assert.That(restoredMask.effects.noise.enabled, Is.True);
            Assert.That(restoredMask.effects.noise.seed, Is.EqualTo(19));
            Assert.That(restoredMask.sourceSettings.source, Is.EqualTo(TexturePaintBrushSource.Color));
            Assert.That(restoredMask.sourceSettings.color.r, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void SrgbOutputChannelKeepsUmaGeneratedAtlasInLinearWorkingSpace()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Color atlasValue = new Color(0.18f, 0.42f, 0.73f, 0.91f);
            Texture2D generatedAtlas = Own(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
            generatedAtlas.SetPixels(new[] { atlasValue, atlasValue, atlasValue, atlasValue });
            generatedAtlas.Apply(false, false);

            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.sRGB = true;
            channel.sourceTexture = generatedAtlas;
            channel.editable.Reset(generatedAtlas, Color.black);
            channel.composite = CreateRenderTexture("Texture Paint Linear Color Composite", 16,
                RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            set.compositor = compositor;

            set.RecomposeAll();

            Assert.That(channel.sRGB, Is.True, "Albedo should still export/import as sRGB.");
            Assert.That(channel.editable.Front.sRGB, Is.False,
                "Compute-writable paint targets must not use sRGB storage.");
            Assert.That(channel.composite.sRGB, Is.False,
                "The material preview must sample the composited linear values without a second decode.");
            AssertColor(ReadCenter(channel.editable.Front), atlasValue, 0.004f);
            AssertColor(ReadCenter(channel.composite), atlasValue, 0.004f);
            compositor.Dispose();
        }

        [Test]
        public void FirstFollowStrokeStampUsesResolvedWorldDirectionWithoutPreviousUv()
        {
            StrokeSample first = new StrokeSample(Vector3.zero, Vector3.forward, new Vector2(0.5f, 0.5f), 0, 0)
            {
                previousUV = new Vector2(0.5f, 0.5f),
                direction = Vector3.up
            };
            BrushProjection projection = new BrushProjection
            {
                uvToBrush = new Vector4(4f, 0f, 0f, 4f),
                worldTangent = Vector3.right,
                worldBitangent = Vector3.up,
                valid = true
            };

            Vector2 motion = TexturePaintStageWindow.ResolveFollowStrokeMotion(first, projection);

            Assert.That(motion.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(motion.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SplineAuthoringOverlayRequiresAnActiveVisiblePathLayer()
        {
            TextureSet set = new TextureSet();
            TexturePaintLayer path = set.AddSplineLayer("Path");
            path.visible = false;
            TexturePaintLayer paint = set.AddLayer("Paint");
            // Unity can materialize an inline serializable class even when a null spline was
            // saved. Editing mode must remain driven by the explicit layer kind.
            paint.spline = new TexturePaintSpline { name = "Stale Serialized Payload" };

            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 0), Is.False,
                "An inactive path must leave only its rasterized layer result visible.");
            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 1), Is.False);

            set.activeLayerIndex = 0;
            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 0), Is.False,
                "A disabled path must not draw or expose spline authoring controls.");

            path.visible = true;
            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 0), Is.True,
                "An enabled selected path must expose its spline authoring controls.");

            set.activeLayerIndex = set.layers.IndexOf(paint);
            Assert.That(TexturePaintStageWindow.IsActiveSplineAuthoringLayer(set, 0), Is.False);
            Assert.That(paint.IsSplineLayer, Is.False);
        }

        [Test]
        public void ShiftStrokeConstraintChoosesAndRetainsTheInitialDominantViewAxis()
        {
            Vector2 origin = new Vector2(100f, 200f);
            TexturePaintStageWindow.StrokeViewAxis horizontal =
                TexturePaintStageWindow.StrokeViewAxis.Pending;

            Vector2 firstHorizontal = TexturePaintStageWindow.ConstrainStrokeViewPoint(
                origin, new Vector2(112f, 205f), ref horizontal);
            Vector2 laterHorizontal = TexturePaintStageWindow.ConstrainStrokeViewPoint(
                origin, new Vector2(102f, 240f), ref horizontal);

            Assert.That(horizontal, Is.EqualTo(TexturePaintStageWindow.StrokeViewAxis.Horizontal));
            Assert.That(firstHorizontal, Is.EqualTo(new Vector2(112f, 200f)));
            Assert.That(laterHorizontal, Is.EqualTo(new Vector2(102f, 200f)),
                "The axis must not flip when a later delta becomes vertically dominant.");

            TexturePaintStageWindow.StrokeViewAxis vertical =
                TexturePaintStageWindow.StrokeViewAxis.Pending;
            Vector2 firstVertical = TexturePaintStageWindow.ConstrainStrokeViewPoint(
                origin, new Vector2(103f, 215f), ref vertical);

            Assert.That(vertical, Is.EqualTo(TexturePaintStageWindow.StrokeViewAxis.Vertical));
            Assert.That(firstVertical, Is.EqualTo(new Vector2(100f, 215f)));

            TexturePaintStageWindow.StrokeViewAxis pending =
                TexturePaintStageWindow.StrokeViewAxis.Pending;
            Vector2 jitter = TexturePaintStageWindow.ConstrainStrokeViewPoint(
                origin, new Vector2(100.25f, 200.25f), ref pending);
            Assert.That(pending, Is.EqualTo(TexturePaintStageWindow.StrokeViewAxis.Pending));
            Assert.That(jitter, Is.EqualTo(origin));
        }

        [TestCase(TexturePaintPathEditMode.Standard, true, true, true)]
        [TestCase(TexturePaintPathEditMode.Move, false, true, false)]
        [TestCase(TexturePaintPathEditMode.Adjust, false, false, true)]
        public void PathEditModesExposeOnlyTheirIntendedOperations(TexturePaintPathEditMode mode,
            bool topology, bool move, bool adjust)
        {
            Assert.That(TexturePaintStageWindow.PathEditModeAllowsTopology(mode), Is.EqualTo(topology));
            Assert.That(TexturePaintStageWindow.PathEditModeAllowsMove(mode), Is.EqualTo(move));
            Assert.That(TexturePaintStageWindow.PathEditModeAllowsAdjust(mode), Is.EqualTo(adjust));
        }

        [Test]
        public void RerasterizingExistingSplineLayerKeepsPointSelection()
        {
            TextureSet set = new TextureSet();
            TexturePaintLayer path = set.AddSplineLayer("Selected Path");
            for (int point = 0; point < 3; point++)
                path.spline.AddPoint(new Vector3(point, 0f, 0f), new Vector2(point * 0.25f, 0.5f),
                    0, -1, Vector3.forward);
            set.activeLayerIndex = set.layers.IndexOf(path);
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo selectedPoint = typeof(TexturePaintStageWindow).GetField("selectedSplinePoint", flags);
            FieldInfo selectedPoints = typeof(TexturePaintStageWindow).GetField("selectedSplinePoints", flags);
            FieldInfo activeSpline = typeof(TexturePaintStageWindow).GetField("spline", flags);
            MethodInfo ensureLayer = typeof(TexturePaintStageWindow).GetMethod("EnsureSplineLayer", flags);
            Assert.That(selectedPoint, Is.Not.Null);
            Assert.That(selectedPoints, Is.Not.Null);
            Assert.That(activeSpline, Is.Not.Null);
            Assert.That(ensureLayer, Is.Not.Null);

            selectedPoint.SetValue(stage, 2);
            selectedPoints.SetValue(stage, new HashSet<int> { 2 });
            activeSpline.SetValue(stage, path.spline);

            ensureLayer.Invoke(stage, new object[] { set });

            Assert.That(activeSpline.GetValue(stage), Is.SameAs(path.spline));
            Assert.That(selectedPoint.GetValue(stage), Is.EqualTo(2),
                "Rerasterizing a moved or adjusted path must keep its primary point selected.");
            Assert.That((HashSet<int>)selectedPoints.GetValue(stage), Does.Contain(2),
                "Rerasterizing must also preserve the multi-point selection used by the handles.");
        }

        [Test]
        public void EnsuringSplineLayerRepairsSelectionAfterPointDeletion()
        {
            TextureSet set = new TextureSet();
            TexturePaintLayer path = set.AddSplineLayer("Shortened Path");
            for (int point = 0; point < 3; point++)
                path.spline.AddPoint(new Vector3(point, 0f, 0f), new Vector2(point * 0.25f, 0.5f),
                    0, -1, Vector3.forward);
            set.activeLayerIndex = set.layers.IndexOf(path);
            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo selectedPoint = typeof(TexturePaintStageWindow).GetField("selectedSplinePoint", flags);
            FieldInfo selectedPoints = typeof(TexturePaintStageWindow).GetField("selectedSplinePoints", flags);
            MethodInfo ensureLayer = typeof(TexturePaintStageWindow).GetMethod("EnsureSplineLayer", flags);
            Assert.That(selectedPoint, Is.Not.Null);
            Assert.That(selectedPoints, Is.Not.Null);
            Assert.That(ensureLayer, Is.Not.Null);
            selectedPoint.SetValue(stage, 2);
            selectedPoints.SetValue(stage, new HashSet<int> { 2 });

            Assert.That(path.spline.RemovePoint(2), Is.True);
            ensureLayer.Invoke(stage, new object[] { set });

            Assert.That(selectedPoint.GetValue(stage), Is.EqualTo(1),
                "Deleting the final selected point should select its surviving neighbor.");
            Assert.That((HashSet<int>)selectedPoints.GetValue(stage), Is.EquivalentTo(new[] { 1 }));

            path.spline.Clear();
            ensureLayer.Invoke(stage, new object[] { set });

            Assert.That(selectedPoint.GetValue(stage), Is.EqualTo(-1));
            Assert.That((HashSet<int>)selectedPoints.GetValue(stage), Is.Empty,
                "Deleting every point must clear the handle selection.");
        }

        [Test]
        public void ScenePositioningOfTwoDimensionalPointKeepsSplineInTextureSpace()
        {
            TextureSet set = new TextureSet
            {
                persistentId = "scene-position-surface",
                surface = new ReconstructedSurface { index = 7 }
            };
            TexturePaintLayer path = set.AddSplineLayer("2D Positioned Path");
            path.spline.worldSpace = false;
            path.spline.AddPoint(Vector3.zero, new Vector2(0.2f, 0.3f), 7, -1, Vector3.forward);
            path.spline.AddPoint(Vector3.zero, new Vector2(0.4f, 0.5f), 7, -1, Vector3.forward);
            path.spline.AddPoint(Vector3.zero, new Vector2(0.6f, 0.7f), 7, -1, Vector3.forward);
            path.spline.EnsureControlPoints();
            int point = 1;
            Vector2 incomingOffset = path.spline.uvInControls[point] - path.spline.uvPoints[point];
            Vector2 outgoingOffset = path.spline.uvOutControls[point] - path.spline.uvPoints[point];
            path.spline.widths[point] = 1.75f;
            Vector2 positionedUV = new Vector2(0.55f, 0.65f);

            bool moved = TexturePaintStageWindow.MoveTwoDimensionalSplinePoint(set, path.spline,
                point, positionedUV, Vector3.up, 12, new Vector3(0.2f, 0.3f, 0.5f));

            Assert.That(moved, Is.True);
            Assert.That(path.spline.worldSpace, Is.False,
                "The Scene handle is a positioning aid and must not convert a 2D path to 3D.");
            Assert.That(path.spline.uvPoints[point], Is.EqualTo(positionedUV));
            Assert.That(Vector2.Distance(path.spline.uvInControls[point] - positionedUV,
                incomingOffset), Is.LessThan(0.000001f));
            Assert.That(Vector2.Distance(path.spline.uvOutControls[point] - positionedUV,
                outgoingOffset), Is.LessThan(0.000001f));
            Assert.That(path.spline.widths[point], Is.EqualTo(1.75f));
            Assert.That(path.spline.triangleIndices[point], Is.EqualTo(12));
            Assert.That(path.spline.anchors[point].surfaceId, Is.EqualTo(set.persistentId));

            Vector2 nextOutgoing = new Vector2(0.72f, 0.81f);
            Assert.That(TexturePaintStageWindow.SetTwoDimensionalSplineControl(
                path.spline, point, false, nextOutgoing), Is.True);
            Assert.That(path.spline.uvOutControls[point], Is.EqualTo(nextOutgoing));
            Assert.That(path.spline.worldOutControls[point],
                Is.EqualTo(new Vector3(nextOutgoing.x, nextOutgoing.y, 0f)));
            Assert.That(path.spline.worldSpace, Is.False);
        }

        [Test]
        public void RibbonSegmentsShareExactCrossSectionsAtBends()
        {
            List<StrokeSample> centerline = new List<StrokeSample>
            {
                new StrokeSample(new Vector3(0f, 0f, 0f), Vector3.forward, Vector2.zero, 0, 0),
                new StrokeSample(new Vector3(0f, 1f, 0f), Vector3.forward, Vector2.zero, 0, 0),
                new StrokeSample(new Vector3(1f, 1f, 0f), Vector3.forward, Vector2.zero, 0, 0)
            };

            List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                centerline, 0.25f, 0.5f);

            Assert.That(segments, Has.Count.EqualTo(2));
            Assert.That(Vector3.Distance(
                    new Vector3(segments[0].leftEndAlong.x, segments[0].leftEndAlong.y,
                        segments[0].leftEndAlong.z),
                    new Vector3(segments[1].leftStartAlong.x, segments[1].leftStartAlong.y,
                        segments[1].leftStartAlong.z)),
                Is.LessThan(0.000001f));
            Assert.That(Vector3.Distance(
                    new Vector3(segments[0].rightEndFlow.x, segments[0].rightEndFlow.y,
                        segments[0].rightEndFlow.z),
                    new Vector3(segments[1].rightStartFlow.x, segments[1].rightStartFlow.y,
                        segments[1].rightStartFlow.z)),
                Is.LessThan(0.000001f));
            Assert.That(segments[0].leftEndAlong.w,
                Is.EqualTo(segments[1].leftStartAlong.w).Within(0.000001f));
            Assert.That(segments[1].leftEndAlong.w % 1f, Is.EqualTo(0f).Within(0.000001f),
                "The fitted source repeat must terminate on a complete tile boundary.");
        }

        [TestCase(-179f, -180)]
        [TestCase(-100f, -90)]
        [TestCase(-44f, 0)]
        [TestCase(46f, 90)]
        [TestCase(150f, 180)]
        public void RibbonRotationSnapsToTheOrientationsExposedByTheUI(float input, int expected)
        {
            Assert.That(TexturePaintStageWindow.SnapRibbonRotation(input), Is.EqualTo(expected));
        }

        [TestCase(599.9f, false)]
        [TestCase(600f, true)]
        [TestCase(900f, true)]
        public void LayerRowExtendedControlsRespondToAvailableWidth(float width, bool expected)
        {
            Assert.That(TexturePaintStageWindow.ShouldShowLayerRowExtendedControls(width),
                Is.EqualTo(expected));
        }

        [Test]
        public void ChannelDetailsOnlyOffersMaterialChannelsCreatedOnTheLayer()
        {
            using TextureSet set = new TextureSet();
            set.channels[TexturePaintChannel.Albedo] = new TextureChannelTarget
                { channel = TexturePaintChannel.Albedo };
            set.channels[TexturePaintChannel.Normal] = new TextureChannelTarget
                { channel = TexturePaintChannel.Normal };
            var layer = new TexturePaintLayer();
            layer.channels[TexturePaintChannel.Albedo] = null;
            layer.channels[TexturePaintChannel.Emission] = null;

            List<TexturePaintChannel> materialChannels =
                TexturePaintStageWindow.GetSelectableChannels(set);
            List<TexturePaintChannel> layerChannels =
                TexturePaintStageWindow.GetSelectableChannels(set, layer);

            Assert.That(materialChannels,
                Is.EqualTo(new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Normal }));
            Assert.That(layerChannels,
                Is.EqualTo(new[] { TexturePaintChannel.Albedo }),
                "Channel Details must exclude both uncreated material channels and authored channels unsupported by the material.");
            layer.channels.Clear();
        }

        [Test]
        public void LayerThumbnailUsesItsOwnChannelInsteadOfTheSelectedChannel()
        {
            using TextureSet set = new TextureSet();
            TexturePaintLayer albedo = set.AddLayer("Albedo Fill");
            TexturePaintLayer normal = set.AddLayer("Normal Fill");
            TexturePaintLayer emission = set.AddLayer("Emission Fill");
            TexturePaintLayer paint = set.AddLayer("Albedo Paint");
            TexturePaintLayer path = set.AddSplineLayer("Albedo Path");
            albedo.kind = normal.kind = emission.kind = TexturePaintLayerKind.Fill;
            albedo.fillChannel = TexturePaintChannel.Albedo;
            normal.fillChannel = TexturePaintChannel.Normal;
            emission.fillChannel = TexturePaintChannel.Emission;
            albedo.channels[albedo.fillChannel] = new EditableTextureTarget(
                "Albedo Thumbnail", 2, 2, RenderTextureFormat.ARGB32, null, Color.red);
            normal.channels[normal.fillChannel] = new EditableTextureTarget(
                "Normal Thumbnail", 2, 2, RenderTextureFormat.ARGB32, null,
                new Color(0.5f, 0.5f, 1f, 1f));
            emission.channels[emission.fillChannel] = new EditableTextureTarget(
                "Emission Thumbnail", 2, 2, RenderTextureFormat.ARGB32, null, Color.green);
            paint.paintSettings = new TexturePaintLayerSettings { channel = TexturePaintChannel.Albedo };
            paint.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget(
                "Paint Thumbnail", 2, 2, RenderTextureFormat.ARGB32, null, Color.yellow);
            path.splineSettings.channel = TexturePaintChannel.Albedo;
            path.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget(
                "Path Thumbnail", 2, 2, RenderTextureFormat.ARGB32, null, Color.cyan);

            Texture albedoPreview = TexturePaintStageWindow.ResolveLayerThumbnail(
                albedo, TexturePaintChannel.Albedo);
            Texture normalPreview = TexturePaintStageWindow.ResolveLayerThumbnail(
                normal, TexturePaintChannel.Albedo);
            Texture emissionPreview = TexturePaintStageWindow.ResolveLayerThumbnail(
                emission, TexturePaintChannel.Albedo);
            Texture paintPreview = TexturePaintStageWindow.ResolveLayerThumbnail(
                paint, TexturePaintChannel.Roughness);
            Texture pathPreview = TexturePaintStageWindow.ResolveLayerThumbnail(
                path, TexturePaintChannel.Roughness);

            Assert.That(albedoPreview, Is.SameAs(albedo.channels[TexturePaintChannel.Albedo].Front));
            Assert.That(normalPreview, Is.SameAs(normal.channels[TexturePaintChannel.Normal].Front),
                "A fill row must not turn white merely because another channel is selected.");
            Assert.That(emissionPreview, Is.SameAs(emission.channels[TexturePaintChannel.Emission].Front));
            Assert.That(paintPreview, Is.SameAs(paint.channels[TexturePaintChannel.Albedo].Front),
                "Selecting a Roughness layer must not black out an Albedo paint thumbnail.");
            Assert.That(pathPreview, Is.SameAs(path.channels[TexturePaintChannel.Albedo].Front),
                "Selecting a Roughness layer must not black out an Albedo path thumbnail.");
        }

        [Test]
        public void DroppingLayerOnGroupParentsRepositionsAndSupportsUndo()
        {
            TexturePaintStageWindow stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            using TextureSet set = new TextureSet();
            try
            {
                TexturePaintLayer dragged = set.AddLayer("Dragged Paint");
                set.AddLayer("Unrelated Paint");
                TexturePaintLayer group = set.AddGroup("Details");
                int originalIndex = set.layers.IndexOf(dragged);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                MethodInfo moveIntoGroup = typeof(TexturePaintStageWindow).GetMethod(
                    "MoveLayerIntoGroupWithHistory", flags);
                MethodInfo removeFromGroup = typeof(TexturePaintStageWindow).GetMethod(
                    "RemoveLayerFromGroupWithHistory", flags);
                MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);
                MethodInfo redo = typeof(TexturePaintStageWindow).GetMethod("RedoLightweight", flags);
                MethodInfo clear = typeof(TexturePaintStageWindow).GetMethod("ClearLightweightHistory", flags);
                MethodInfo setExpanded = typeof(TexturePaintStageWindow).GetMethod("SetGroupExpanded", flags);
                MethodInfo hiddenByGroup = typeof(TexturePaintStageWindow).GetMethod(
                    "IsLayerHiddenByCollapsedGroup", flags);
                Assert.That(moveIntoGroup, Is.Not.Null);
                Assert.That(removeFromGroup, Is.Not.Null);
                Assert.That(undo, Is.Not.Null);
                Assert.That(redo, Is.Not.Null);
                Assert.That(setExpanded, Is.Not.Null);
                Assert.That(hiddenByGroup, Is.Not.Null);

                Assert.That((bool)moveIntoGroup.Invoke(stage, new object[] { set, dragged, group }), Is.True);
                Assert.That(dragged.parentId, Is.EqualTo(group.id));
                Assert.That(set.layers.IndexOf(dragged), Is.EqualTo(set.layers.IndexOf(group) - 1),
                    "The child should display directly below the folder row.");
                setExpanded.Invoke(stage, new object[] { group, false });
                Assert.That((bool)hiddenByGroup.Invoke(stage, new object[] { set, dragged }), Is.True);
                Assert.That((bool)hiddenByGroup.Invoke(stage, new object[] { set, group }), Is.False,
                    "Collapsing a folder must keep the folder row available for reopening.");
                setExpanded.Invoke(stage, new object[] { group, true });
                Assert.That((bool)hiddenByGroup.Invoke(stage, new object[] { set, dragged }), Is.False);

                Assert.That((bool)undo.Invoke(stage, null), Is.True);
                Assert.That(dragged.parentId, Is.Null);
                Assert.That(set.layers.IndexOf(dragged), Is.EqualTo(originalIndex));

                Assert.That((bool)redo.Invoke(stage, null), Is.True);
                Assert.That(dragged.parentId, Is.EqualTo(group.id));
                Assert.That((bool)removeFromGroup.Invoke(stage, new object[] { set, dragged }), Is.True);
                Assert.That(dragged.parentId, Is.Null);
                Assert.That(set.layers.IndexOf(dragged), Is.EqualTo(set.layers.IndexOf(group) + 1),
                    "An ungrouped layer must move above its former group so it cannot split the child block.");
                Assert.That((bool)undo.Invoke(stage, null), Is.True);
                Assert.That(dragged.parentId, Is.EqualTo(group.id));
                Assert.That(set.layers.IndexOf(dragged), Is.EqualTo(set.layers.IndexOf(group) - 1));

                clear?.Invoke(stage, null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        [Test]
        public void DuplicatingNestedGroupTracksCompleteSubtreeInUndoRedo()
        {
            TexturePaintStageWindow stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            using TextureSet set = new TextureSet();
            try
            {
                TexturePaintLayer outer = set.AddGroup("Outer");
                set.AddGroup("Inner");
                set.AddLayer("Leaf");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                MethodInfo duplicate = typeof(TexturePaintStageWindow).GetMethod(
                    "DuplicateLayerWithHistory", flags);
                MethodInfo undo = typeof(TexturePaintStageWindow).GetMethod("UndoLightweight", flags);
                MethodInfo redo = typeof(TexturePaintStageWindow).GetMethod("RedoLightweight", flags);
                MethodInfo clear = typeof(TexturePaintStageWindow).GetMethod(
                    "ClearLightweightHistory", flags);

                duplicate.Invoke(stage, new object[] { set, set.layers.IndexOf(outer) });

                Assert.That(set.layers, Has.Count.EqualTo(6));
                TexturePaintLayer outerCopy = set.layers.Find(candidate => candidate.name == "Outer Copy");
                Assert.That(outerCopy, Is.Not.Null);
                TexturePaintLayer innerCopy = set.layers.Find(candidate =>
                    candidate.kind == TexturePaintLayerKind.Group && candidate.parentId == outerCopy.id);
                Assert.That(innerCopy, Is.Not.Null);
                Assert.That(set.layers.Exists(candidate => candidate.parentId == innerCopy.id), Is.True);
                Assert.That((bool)undo.Invoke(stage, null), Is.True);
                Assert.That(set.layers, Has.Count.EqualTo(3));
                Assert.That((bool)redo.Invoke(stage, null), Is.True);
                Assert.That(set.layers, Has.Count.EqualTo(6));
                Assert.That(set.layers.Contains(outerCopy), Is.True);
                clear.Invoke(stage, null);
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [Test]
        public void RibbonProjectionRasterizesOneContinuousWorldStripIntoLayerUVs()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            Assert.That(ribbonShader, Is.Not.Null);
            Assert.That(ribbonShader.isSupported, Is.True);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0f), 0, 0) { color = Color.green, hasColor = true },
                    new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                        new Vector2(0.5f, 1f), 0, 1) { color = Color.green, hasColor = true }
                };
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                AssertColor(pixels[(size / 2) * size + size / 2], Color.green, 0.02f);
                Assert.That(pixels[(size / 2) * size + 2].a, Is.LessThan(0.02f));
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonEdgeFadeUsesCrossSectionWithoutFadingPathEnds()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Ribbon Edge Fade Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.4f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            Assert.That(ribbonShader, Is.Not.Null);
            Assert.That(ribbonShader.isSupported, Is.True);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 1f;
                context.ribbonEdgeFadeEnabled = true;
                context.ribbonEdgeFadeStart = 0.5f;
                context.ribbonEdgeFadeSize = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0f), 0, 0) { color = Color.green, hasColor = true },
                    new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                        new Vector2(0.5f, 1f), 0, 1) { color = Color.green, hasColor = true }
                };
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Assert.That(pixels[(size / 2) * size + size / 2].a, Is.GreaterThan(0.98f),
                    "The ribbon centerline must remain opaque.");
                Assert.That(pixels[2 * size + size / 2].a, Is.GreaterThan(0.98f),
                    "Edge Fade must not fade the ribbon's start/end direction.");
                Assert.That(pixels[(size / 2) * size + 8].a, Is.LessThan(0.12f),
                    "The ribbon-local side edge should approach transparency.");
                Assert.That(pixels[(size / 2) * size + 16].a,
                    Is.InRange(0.65f, 0.95f), "The side fade should be gradual when Fade Size is 100.");

                Assert.That(engine.RewindActiveStroke(), Is.True);
                engine.EndStroke(false);
                context.ribbonEdgeFadeSize = 0f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);
                pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                Assert.That(pixels[(size / 2) * size + 24].a, Is.GreaterThan(0.98f),
                    "Pixels inside Fade Begins must remain opaque when Fade Size is zero.");
                Assert.That(pixels[(size / 2) * size + 16].a, Is.LessThan(0.02f),
                    "Fade Size zero must cut out immediately at Fade Begins.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonLocalShadowAffectsOnlySelectedLongEdgeAndNotCaps()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Ribbon Shadow Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.3f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green, strength: 1f);
                context.projectionDepth = 1f;
                context.ribbonEffects = new TexturePaintLayerEffects();
                context.ribbonEffects.innerShadow.enabled = true;
                context.ribbonEffects.innerShadow.channel = TexturePaintChannel.Albedo;
                context.ribbonEffects.innerShadow.ribbonSide = TexturePaintRibbonSide.Left;
                context.ribbonEffects.innerShadow.color = Color.red;
                context.ribbonEffects.innerShadow.width = 10f;
                context.ribbonEffects.innerShadow.level = 1f;
                context.ribbonEffects.innerShadow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = CreateVerticalRibbonSamples();
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);
                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Color left = pixels[(size / 2) * size + 15];
                Color right = pixels[(size / 2) * size + 49];
                Color beginningCenter = pixels[2 * size + size / 2];
                Assert.That(left.r, Is.GreaterThan(left.g + 0.25f), "The left edge should receive the shadow color.");
                Assert.That(right.g, Is.GreaterThan(right.r + 0.6f), "The right edge must remain unshadowed.");
                Assert.That(beginningCenter.g, Is.GreaterThan(0.95f),
                    "A side shadow must not proceed before the ribbon beginning or across its cap.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonStrokeTracksRibbonEdgeWhileOuterGlowFades()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Ribbon Outer Glow Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.25f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 1f;
                context.ribbonEffects = new TexturePaintLayerEffects();
                TexturePaintLayerEffectSettings glow = context.ribbonEffects.outerGlow;
                glow.enabled = true;
                glow.channel = TexturePaintChannel.Albedo;
                glow.ribbonSide = TexturePaintRibbonSide.Right;
                glow.color = Color.blue;
                glow.width = 8f;
                glow.level = 1f;
                glow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                TexturePaintLayerEffectSettings stroke = context.ribbonEffects.stroke;
                stroke.enabled = true;
                stroke.channel = TexturePaintChannel.Albedo;
                stroke.color = Color.black;
                stroke.width = 2f;
                stroke.smoothness = 0f;
                stroke.level = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0.2f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0.2f), 0, 0)
                        { color = Color.green, hasColor = true, pressure = 1f, flowMultiplier = 1f },
                    new StrokeSample(new Vector3(0.5f, 0.8f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0.8f), 0, 1)
                        { color = Color.green, hasColor = true, pressure = 1f, flowMultiplier = 1f }
                };
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);
                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Color strokeOutside = pixels[(size / 2) * size + 49];
                Color rightOutside = pixels[(size / 2) * size + 51];
                Color leftOutside = pixels[(size / 2) * size + 12];
                Color beforeBeginning = pixels[7 * size + 51];
                Assert.That(strokeOutside.r, Is.LessThan(0.05f),
                    "The stroke must sit directly outside the ribbon edge, over the glow.");
                Assert.That(strokeOutside.a, Is.GreaterThan(0.95f),
                    "The configured hard stroke should remain opaque at the ribbon edge.");
                Assert.That(rightOutside.b, Is.GreaterThan(0.35f),
                    "The selected right edge should emit the outer glow.");
                Assert.That(rightOutside.a, Is.LessThan(0.9f),
                    "The outer glow must keep fading beyond the stroke; straight-alpha RGB may remain blue.");
                Assert.That(leftOutside.a, Is.LessThan(0.05f),
                    "The unselected long edge must not receive the outer glow.");
                Assert.That(beforeBeginning.a, Is.LessThan(0.05f),
                    "An outer glow must not proceed before the ribbon beginning.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void NegativeRibbonStrokeOffsetPullsStrokeInsideLongEdges()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Inset Ribbon Stroke Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.25f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                context.projectionDepth = 1f;
                context.ribbonEffects = new TexturePaintLayerEffects();
                TexturePaintLayerEffectSettings stroke = context.ribbonEffects.stroke;
                stroke.enabled = true;
                stroke.channel = TexturePaintChannel.Albedo;
                stroke.color = Color.black;
                stroke.width = 4f;
                stroke.offset.x = -4f;
                stroke.smoothness = 0f;
                stroke.level = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0.2f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0.2f), 0, 0)
                        { color = Color.green, hasColor = true, pressure = 1f, flowMultiplier = 1f },
                    new StrokeSample(new Vector3(0.5f, 0.8f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0.8f), 0, 1)
                        { color = Color.green, hasColor = true, pressure = 1f, flowMultiplier = 1f }
                };
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);
                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Color outside = pixels[(size / 2) * size + 50];
                Color insetEdge = pixels[(size / 2) * size + 47];
                Color center = pixels[(size / 2) * size + size / 2];
                Assert.That(outside.a, Is.LessThan(0.05f),
                    "A fully inset stroke must not expand beyond the ribbon edge.");
                Assert.That(insetEdge.r, Is.LessThan(0.05f));
                Assert.That(insetEdge.g, Is.LessThan(0.05f));
                Assert.That(insetEdge.b, Is.LessThan(0.05f));
                Assert.That(insetEdge.a, Is.GreaterThan(0.95f),
                    "The stroke should move into the ribbon instead of disappearing.");
                Assert.That(center.g, Is.GreaterThan(0.95f),
                    "Pulling in the stroke must not recolor the ribbon center.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [TestCase(TexturePaintLayerEffectKind.Stroke)]
        [TestCase(TexturePaintLayerEffectKind.OuterGlow)]
        [TestCase(TexturePaintLayerEffectKind.OuterShadow)]
        public void RibbonOuterEffectDoesNotLeaveADistantSegmentOwnershipContour(
            TexturePaintLayerEffectKind effectKind)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Curved Ribbon Effect Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.08f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                context.projectionDepth = 1f;
                context.ribbonEffects = new TexturePaintLayerEffects();
                TexturePaintLayerEffectSettings effect = context.ribbonEffects.GetFirst(effectKind);
                effect.enabled = true;
                effect.channel = TexturePaintChannel.Albedo;
                effect.ribbonSide = TexturePaintRibbonSide.Both;
                effect.color = Color.magenta;
                effect.width = 4f;
                effect.smoothness = 0f;
                effect.level = 1f;
                effect.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                var centerline = new List<StrokeSample>();
                Vector2[] points =
                {
                    new Vector2(0.42f, 0.08f), new Vector2(0.48f, 0.25f),
                    new Vector2(0.56f, 0.42f), new Vector2(0.47f, 0.60f),
                    new Vector2(0.40f, 0.78f), new Vector2(0.46f, 0.92f)
                };
                for (int i = 0; i < points.Length; i++)
                    centerline.Add(new StrokeSample(new Vector3(points[i].x, points[i].y, 0f),
                        Vector3.forward, points[i], 0, i)
                        { color = Color.green, hasColor = true, pressure = 1f, flowMultiplier = 1f });
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                int distantPixels = 0;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (pixels[y * size + x].a <= 0.02f) continue;
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    if (DistanceToPolyline(uv, points) > 0.20f) distantPixels++;
                }
                Assert.That(distantPixels, Is.Zero,
                    "Outer ribbon effects must not expose the conservative segment ownership boundary.");
                Assert.That(engine.Performance.copiedPixels, Is.EqualTo((long)size * size * 2L),
                    "One ribbon effect must share the paint pass instead of doubling projection work.");
                Assert.That(engine.Performance.geometryMaskBuilds, Is.Zero,
                    "Mesh-rasterized ribbons must not rebuild an equivalent full-resolution CPU mask.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void DerivedRibbonReapplyReusesGeometryMaskWithoutPixelHistoryAllocation()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddSplineLayer("Interactive Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.12f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                context.projectionDepth = 1f;
                context.historyGroupKey = "interactive-ribbon";
                context.replaceLayer = layer;
                context.replaceHistoryGroup = true;
                context.derivedLayerRaster = true;
                var geometrySelection = new TexturePaintGeometrySelection();
                var geometrySelector = new TexturePaintGeometrySelector
                {
                    kind = TexturePaintGeometrySelectorKind.Polygon,
                    surfaceIndex = fixture.set.surface.index
                };
                geometrySelector.triangleIndices.Add(0);
                geometrySelector.triangleIndices.Add(1);
                geometrySelection.Add(geometrySelector);
                context.geometrySelection = geometrySelection;
                List<StrokeSample> centerline = CreateVerticalRibbonSamples();
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                for (int iteration = 0; iteration < 2; iteration++)
                {
                    Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                    Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);
                    engine.EndStroke(true);
                }

                Assert.That(engine.Performance.geometryMaskBuilds, Is.EqualTo(1),
                    "An unchanged surface mask must survive across procedural ribbon previews.");
                Assert.That(engine.History.UndoTileCount, Is.Zero,
                    "A path-level undo model must not allocate redundant full-resolution pixel history.");
                Assert.That(engine.History.EstimatedMemoryBytes, Is.Zero);
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void DerivedPathReapplyClearsPixelsOutsideLegacyHistoryCapture()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddSplineLayer("Legacy Path Replacement");
            fixture.set.activeLayerIndex = fixture.set.layers.IndexOf(layer);
            EditableTextureTarget target = fixture.set.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            Assert.That(target, Is.Not.Null);
            target.Reset(null, Color.red);
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            brush.size = 0.04f;
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            const string historyKey = "texture-paint-spline:legacy-replacement";
            StrokeSample center = new StrokeSample(new Vector3(0.5f, 0.5f, 0f),
                Vector3.forward, new Vector2(0.5f, 0.5f), 0, -1);
            var sample = new List<StrokeDispatchSample>
            {
                new StrokeDispatchSample(center, brush.size, default)
            };
            try
            {
                // Older path builds used ordinary pixel history. Only the current footprint was
                // captured, so unrelated stale/effect pixels can exist outside that capture.
                StrokeContext legacy = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                legacy.directUV = true;
                legacy.historyGroupKey = historyKey;
                Assert.That(engine.BeginStroke(legacy, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplySamples(sample), Is.True);
                engine.EndStroke(true);
                Assert.That(engine.History.UndoTileCount, Is.GreaterThan(0));

                StrokeContext replacement = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.blue, strength: 1f);
                replacement.directUV = true;
                replacement.historyGroupKey = historyKey;
                replacement.replaceLayer = layer;
                replacement.replaceHistoryGroup = true;
                replacement.derivedLayerRaster = true;
                Assert.That(engine.BeginStroke(replacement, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplySamples(sample), Is.True);
                engine.EndStroke(true);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(target.Front);
                int farPixel = 2 * TexturePaintGpuTestFixture.Size + 2;
                Assert.That(pixels[farPixel].a, Is.LessThan(0.02f),
                    "A path rebuild must clear the complete owned raster even when it retires a legacy history entry.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void SmallerDirectUvSplineReapplyClearsPreviousCompositeBounds()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddSplineLayer("2D Spline Replacement");
            fixture.set.activeLayerIndex = fixture.set.layers.IndexOf(layer);
            TextureChannelTarget channel = fixture.set.GetChannel(TexturePaintChannel.Albedo);
            channel.composite = CreateRenderTexture("2D Spline Replacement Composite",
                TexturePaintGpuTestFixture.Size, RenderTextureFormat.ARGBHalf);
            TextureLayerCompositor compositor = new TextureLayerCompositor(
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            fixture.set.compositor = compositor;
            fixture.set.BindPreviewTextures();
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                context.directUV = true;
                context.historyGroupKey = "texture-paint-spline:" + layer.id;
                context.replaceLayer = layer;
                context.replaceHistoryGroup = true;
                context.derivedLayerRaster = true;
                StrokeSample left = new StrokeSample(new Vector3(0.35f, 0.5f, 0f),
                    Vector3.forward, new Vector2(0.35f, 0.5f), 0, -1);
                StrokeSample right = new StrokeSample(new Vector3(0.65f, 0.5f, 0f),
                    Vector3.forward, new Vector2(0.65f, 0.5f), 0, -1);

                var wide = new List<StrokeDispatchSample>
                {
                    new StrokeDispatchSample(left, 0.2f, default),
                    new StrokeDispatchSample(right, 0.2f, default)
                };
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplySamples(wide), Is.True);
                engine.EndStroke(true);
                Color[] first = TexturePaintGpuTestFixture.ReadPixels(channel.composite);
                int outsideNewSpline = 41 * TexturePaintGpuTestFixture.Size + 41;
                Assert.That(first[outsideNewSpline].a, Is.GreaterThan(0.9f),
                    "The reference pixel must be covered by the initial wide spline.");

                var narrow = new List<StrokeDispatchSample>
                {
                    new StrokeDispatchSample(left, 0.05f, default),
                    new StrokeDispatchSample(right, 0.05f, default)
                };
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplySamples(narrow), Is.True);
                engine.EndStroke(true);
                Color[] second = TexturePaintGpuTestFixture.ReadPixels(channel.composite);
                Assert.That(second[outsideNewSpline].a, Is.LessThan(0.02f),
                    "Shrinking a 2D spline must recompose and clear its previous wider footprint.");
            }
            finally
            {
                engine.EndStroke(false);
                compositor.Dispose();
                UnityEngine.Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonBeginningAndEndTexturesReplaceOnlyEndpointTiles()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Ribbon Endpoint Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            // Three fitted tiles are required so this verifies both endpoint replacements and
            // an untouched middle tile. A 0.2 half-width fits only two tiles on this unit path.
            brush.size = 0.16f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green, strength: 1f);
                context.projectionDepth = 1f;
                context.ribbonBeginningTexture = CreateSolidTexture(Color.red);
                context.ribbonEndTexture = CreateSolidTexture(Color.blue);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = CreateVerticalRibbonSamples();
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);
                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                AssertColor(pixels[8 * size + size / 2], Color.red, 0.03f);
                AssertColor(pixels[(size / 2) * size + size / 2], Color.green, 0.03f);
                AssertColor(pixels[55 * size + size / 2], Color.blue, 0.03f);
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonBevelAndDoubleStitchesFollowBothLongEdges()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddLayer("Ribbon Bevel Stitch Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.35f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    new Color(0.25f, 0.25f, 0.25f, 1f), strength: 1f);
                context.projectionDepth = 1f;
                context.ribbonEffects = new TexturePaintLayerEffects();
                TexturePaintLayerEffectSettings bevel = context.ribbonEffects.bevelEdge;
                bevel.enabled = true;
                bevel.channel = TexturePaintChannel.Albedo;
                bevel.ribbonSide = TexturePaintRibbonSide.Both;
                bevel.ribbonLeftTone = TexturePaintRibbonBevelTone.Light;
                bevel.ribbonRightTone = TexturePaintRibbonBevelTone.Dark;
                bevel.color = Color.white;
                bevel.secondaryColor = Color.black;
                bevel.width = 5f;
                bevel.level = 1f;
                TexturePaintLayerEffectSettings stitch = context.ribbonEffects.proceduralStitch;
                stitch.enabled = true;
                stitch.channel = TexturePaintChannel.Albedo;
                stitch.ribbonSide = TexturePaintRibbonSide.Both;
                stitch.stitchRows = TexturePaintRibbonStitchRows.Double;
                stitch.color = Color.yellow;
                stitch.stitchThreadSize = 0.035f;
                stitch.stitchLength = 0.12f;
                stitch.stitchInset = 0.1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = CreateVerticalRibbonSamples();
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);
                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Assert.That(pixels[(size / 2) * size + 10].r,
                    Is.GreaterThan(pixels[(size / 2) * size + 54].r + 0.2f),
                    "The left bevel should be light and the right bevel dark.");
                int brightLeft = 0, brightRight = 0, darkGaps = 0;
                int[] leftColumnHits = new int[size];
                int[] rightColumnHits = new int[size];
                for (int y = 5; y < size - 5; y++)
                {
                    bool leftHit = false, rightHit = false;
                    for (int x = 9; x <= 22; x++)
                    {
                        Color pixel = pixels[y * size + x];
                        if (pixel.r <= 0.7f || pixel.g <= 0.7f || pixel.b >= 0.35f) continue;
                        leftHit = true;
                        leftColumnHits[x]++;
                    }
                    for (int x = 42; x <= 55; x++)
                    {
                        Color pixel = pixels[y * size + x];
                        if (pixel.r <= 0.7f || pixel.g <= 0.7f || pixel.b >= 0.35f) continue;
                        rightHit = true;
                        rightColumnHits[x]++;
                    }
                    if (leftHit) brightLeft++;
                    if (rightHit) brightRight++;
                    if (!leftHit && !rightHit) darkGaps++;
                }
                Assert.That(brightLeft, Is.GreaterThan(3), "Stitches should repeat along the left side.");
                Assert.That(brightRight, Is.GreaterThan(3), "Stitches should repeat along the right side.");
                Assert.That(darkGaps, Is.GreaterThan(3), "Procedural stitches must include gaps between stitches.");
                Assert.That(CountOccupiedColumnClusters(leftColumnHits, 3), Is.EqualTo(2),
                    "Double stitches should produce two distinct thread rows on the left side.");
                Assert.That(CountOccupiedColumnClusters(rightColumnHits, 3), Is.EqualTo(2),
                    "Double stitches should produce two distinct thread rows on the right side.");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void DirectUvRibbonRendersShadowsBevelsAndStitches()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            TexturePaintLayer layer = fixture.set.AddSplineLayer("2D Ribbon Effects Result");
            layer.spline.worldSpace = false;
            layer.splineSettings.pathMode = TexturePaintPathMode.Ribbon;
            fixture.set.activeLayerIndex = fixture.set.layers.IndexOf(layer);
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.25f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    new Color(0.25f, 0.25f, 0.25f, 1f), strength: 1f);
                context.directUV = true;
                context.projectionDepth = 1f;
                context.ribbonEffects = new TexturePaintLayerEffects();
                TexturePaintLayerEffectSettings shadow = context.ribbonEffects.outerShadow;
                shadow.enabled = true;
                shadow.channel = TexturePaintChannel.Albedo;
                shadow.ribbonSide = TexturePaintRibbonSide.Both;
                shadow.color = Color.red;
                shadow.width = 6f;
                shadow.level = 1f;
                shadow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                TexturePaintLayerEffectSettings bevel = context.ribbonEffects.bevelEdge;
                bevel.enabled = true;
                bevel.channel = TexturePaintChannel.Albedo;
                bevel.ribbonSide = TexturePaintRibbonSide.Both;
                bevel.ribbonLeftTone = TexturePaintRibbonBevelTone.Light;
                bevel.ribbonRightTone = TexturePaintRibbonBevelTone.Dark;
                bevel.color = Color.white;
                bevel.secondaryColor = Color.black;
                bevel.width = 4f;
                bevel.level = 1f;
                TexturePaintLayerEffectSettings stitch = context.ribbonEffects.proceduralStitch;
                stitch.enabled = true;
                stitch.channel = TexturePaintChannel.Albedo;
                stitch.ribbonSide = TexturePaintRibbonSide.Both;
                stitch.color = Color.yellow;
                stitch.stitchThreadSize = 0.04f;
                stitch.stitchLength = 0.1f;
                stitch.stitchInset = 0.1f;
                stitch.level = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = CreateVerticalRibbonSamples();
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false, false, true), Is.True);
                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Color lightEdge = pixels[(size / 2) * size + 17];
                Color darkEdge = pixels[(size / 2) * size + 47];
                Assert.That(lightEdge.r, Is.GreaterThan(darkEdge.r + 0.15f),
                    "The 2D ribbon must evaluate its light and dark bevel edges.");
                int shadowPixels = 0, stitchPixels = 0;
                for (int y = 6; y < size - 6; y++)
                for (int x = 8; x < size - 8; x++)
                {
                    Color pixel = pixels[y * size + x];
                    bool outsideRibbon = x < 16 || x > 48;
                    if (outsideRibbon && pixel.a > 0.05f && pixel.r > pixel.g + 0.2f)
                        shadowPixels++;
                    if (pixel.r > 0.7f && pixel.g > 0.7f && pixel.b < 0.35f)
                        stitchPixels++;
                }
                Assert.That(shadowPixels, Is.GreaterThan(8),
                    "The 2D ribbon must render fading outer-shadow pixels outside both long edges.");
                Assert.That(stitchPixels, Is.GreaterThan(8),
                    "The 2D ribbon must render procedural stitches from intrinsic path coordinates.");
            }
            finally
            {
                engine.EndStroke(false);
                UnityEngine.Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonProjectionPreservesContributionWithSharedMirroredUVs()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);

            // The first quad is beneath the ribbon. The second is far outside its projection
            // depth, but occupies the exact same texels with horizontally mirrored UVs. Pants
            // and other symmetrical slots commonly use this layout. The later non-contributing
            // triangles must not overwrite the first quad's valid paint result.
            fixture.mesh.Clear();
            fixture.mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 2f), new Vector3(1f, 0f, 2f),
                new Vector3(1f, 1f, 2f), new Vector3(0f, 1f, 2f)
            };
            fixture.mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            };
            fixture.mesh.uv = new[]
            {
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.right, Vector2.zero, Vector2.up, Vector2.one
            };
            fixture.mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7
            };
            fixture.mesh.RecalculateBounds();
            fixture.set.surface.triangleSlotNames = new[] { "Body", "Body", "Body", "Body" };
            fixture.set.surface.triangleIslands = new[] { 0, 0, 0, 0 };

            TexturePaintLayer layer = fixture.set.AddLayer("Shared UV Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            Assert.That(ribbonShader, Is.Not.Null);
            Assert.That(ribbonShader.isSupported, Is.True);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 0.25f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0f), 0, 0) { color = Color.green, hasColor = true },
                    new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                        new Vector2(0.5f, 1f), 0, 1) { color = Color.green, hasColor = true }
                };
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Color center = pixels[(size / 2) * size + size / 2];
                Assert.That(center.g, Is.EqualTo(Color.green.g).Within(0.02f),
                    "A mirrored UV owner outside the ribbon must not erase the contributing owner.");
                AssertColor(center, Color.green, 0.02f);
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonProjectionDoesNotSplitTwistedRibbonQuadsIntoPlanarCoverage()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            const int grid = 17;
            Vector3[] vertices = new Vector3[grid * grid];
            Vector3[] normals = new Vector3[grid * grid];
            Vector2[] uvs = new Vector2[grid * grid];
            int[] triangles = new int[(grid - 1) * (grid - 1) * 6];
            for (int y = 0; y < grid; y++)
            {
                float v = y / (float)(grid - 1);
                for (int x = 0; x < grid; x++)
                {
                    float u = x / (float)(grid - 1);
                    int vertex = y * grid + x;
                    float worldX = Mathf.Lerp(0.25f, 0.75f, u);
                    float worldZ = v * Mathf.Lerp(0.08f, -0.08f, u);
                    vertices[vertex] = new Vector3(worldX, v, worldZ);
                    uvs[vertex] = new Vector2(worldX, v);
                    Vector3 derivativeAcross = new Vector3(0.5f, 0f, -0.16f * v);
                    Vector3 derivativeAlong = new Vector3(0f, 1f, Mathf.Lerp(0.08f, -0.08f, u));
                    normals[vertex] = Vector3.Cross(derivativeAcross, derivativeAlong).normalized;
                }
            }
            int triangleOffset = 0;
            for (int y = 0; y < grid - 1; y++)
                for (int x = 0; x < grid - 1; x++)
                {
                    int a = y * grid + x;
                    int b = a + 1;
                    int c = a + grid;
                    int d = c + 1;
                    triangles[triangleOffset++] = a;
                    triangles[triangleOffset++] = b;
                    triangles[triangleOffset++] = d;
                    triangles[triangleOffset++] = a;
                    triangles[triangleOffset++] = d;
                    triangles[triangleOffset++] = c;
                }
            fixture.mesh.Clear();
            fixture.mesh.vertices = vertices;
            fixture.mesh.normals = normals;
            fixture.mesh.uv = uvs;
            fixture.mesh.triangles = triangles;
            fixture.mesh.RecalculateBounds();
            int surfaceTriangleCount = triangles.Length / 3;
            fixture.set.surface.triangleSlotNames = new string[surfaceTriangleCount];
            fixture.set.surface.triangleIslands = new int[surfaceTriangleCount];
            for (int triangle = 0; triangle < surfaceTriangleCount; triangle++)
                fixture.set.surface.triangleSlotNames[triangle] = "Body";

            TexturePaintLayer layer = fixture.set.AddLayer("Twisted Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            Assert.That(ribbonShader, Is.Not.Null);
            Assert.That(ribbonShader.isSupported, Is.True);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 0.025f;
                context.normalAngleLimit = 90f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>
                {
                    new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                        new Vector2(0.5f, 0f), 0, 0) { color = Color.green, hasColor = true },
                    new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                        new Vector2(0.5f, 1f), 0, 1) { color = Color.green, hasColor = true }
                };
                List<TexturePaintRibbonSegment> segments = new List<TexturePaintRibbonSegment>
                {
                    new TexturePaintRibbonSegment
                    {
                        leftStartAlong = new Vector4(0.25f, 0f, 0f, 0f),
                        rightStartFlow = new Vector4(0.75f, 0f, 0f, 1f),
                        leftEndAlong = new Vector4(0.25f, 1f, 0.08f, 1f),
                        rightEndFlow = new Vector4(0.75f, 1f, -0.08f, 1f),
                        normalStartPressure = new Vector4(0f, 0f, 1f, 1f),
                        normalEndPressure = new Vector4(0f, 0f, 1f, 1f),
                        colorStart = Color.green,
                        colorEnd = Color.green
                    }
                };

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                for (int y = 4; y < size - 4; y++)
                    for (int x = 17; x < 47; x++)
                        Assert.That(pixels[y * size + x].a, Is.GreaterThan(0.98f),
                            $"Twisted ribbon coverage hole at ({x}, {y}).");
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void OpenRibbonDoesNotSmearSourceRowsBeyondItsEndpoints()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            fixture.mesh.vertices = new[]
            {
                new Vector3(0f, -0.25f, 0f), new Vector3(1f, -0.25f, 0f),
                new Vector3(1f, 1.25f, 0f), new Vector3(0f, 1.25f, 0f)
            };
            fixture.mesh.RecalculateBounds();

            TexturePaintLayer layer = fixture.set.AddLayer("Open Ribbon Result");
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Square);
            brush.size = 0.2f;
            Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/RibbonProjection.shader"));
            Assert.That(ribbonShader, Is.Not.Null);
            using PaintingEngine engine = new PaintingEngine(null, null, null, ribbonShader);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green,
                    strength: 1f);
                context.projectionDepth = 1f;
                context.normalAngleLimit = 90f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                List<StrokeSample> centerline = new List<StrokeSample>();
                for (int sampleIndex = 0; sampleIndex <= 10; sampleIndex++)
                {
                    float y = sampleIndex * 0.1f;
                    centerline.Add(new StrokeSample(new Vector3(0.5f, y, 0f), Vector3.forward,
                        new Vector2(0.5f, (y + 0.25f) / 1.5f), 0, sampleIndex)
                    { color = Color.green, hasColor = true });
                }
                List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                    centerline, brush.size, brush.size * 2f);

                Assert.That(engine.ApplyRibbon(segments, centerline, false, false), Is.True);

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(
                    layer.channels[TexturePaintChannel.Albedo].Front);
                int size = TexturePaintGpuTestFixture.Size;
                Assert.That(pixels[10 * size + size / 2].a, Is.LessThan(0.02f),
                    "An open ribbon must not extend before its first cross-section.");
                Assert.That(pixels[53 * size + size / 2].a, Is.LessThan(0.02f),
                    "An open ribbon must not extend beyond its final cross-section.");
                Assert.That(pixels[(size / 2) * size + size / 2].a, Is.GreaterThan(0.98f));
            }
            finally
            {
                engine.EndStroke(false);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void RibbonBendRotationBeginsBeforeCornerAcrossThreeTileWindow()
        {
            List<StrokeSample> centerline = new List<StrokeSample>();
            for (int i = 0; i <= 4; i++)
                centerline.Add(new StrokeSample(new Vector3(0f, i * 0.5f, 0f), Vector3.forward,
                    Vector2.zero, 0, 0));
            for (int i = 1; i <= 4; i++)
                centerline.Add(new StrokeSample(new Vector3(i * 0.5f, 2f, 0f), Vector3.forward,
                    Vector2.zero, 0, 0));

            List<TexturePaintRibbonSegment> segments = TexturePaintStageWindow.BuildRibbonSegments(
                centerline, 0.25f, 1f);

            Vector3 preCornerCrossSection = new Vector3(
                segments[2].rightEndFlow.x - segments[2].leftEndAlong.x,
                segments[2].rightEndFlow.y - segments[2].leftEndAlong.y,
                segments[2].rightEndFlow.z - segments[2].leftEndAlong.z).normalized;
            Assert.That(Mathf.Abs(preCornerCrossSection.y), Is.GreaterThan(0.05f),
                "The cross section before the corner should already share the turn deformation.");
        }

        [Test]
        public void ClearModificationsRestoresSourceAndRemovesLayersAndBaseStrokes()
        {
            Color sourceColor = new Color(0.12f, 0.34f, 0.78f, 0.91f);
            Texture2D source = Own(new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true));
            source.SetPixels(new[] { sourceColor, sourceColor, sourceColor, sourceColor });
            source.Apply(false, false);
            TextureSet set = CreateSet(TexturePaintChannel.Albedo, Color.black);
            CreateStore(set);
            TextureChannelTarget channel = set.GetChannel(TexturePaintChannel.Albedo);
            channel.sourceTexture = source;
            channel.editable.Reset(null, Color.red);
            set.baseStrokes.Add(new TexturePaintStrokeRecord());
            TexturePaintLayer paint = set.AddFillLayer("Paint", TexturePaintChannel.Albedo, Color.green);
            paint.strokes.Add(new TexturePaintStrokeRecord());

            set.ClearModifications();

            Assert.That(set.layers, Is.Empty);
            Assert.That(set.activeLayerIndex, Is.EqualTo(-1));
            Assert.That(set.baseStrokes, Is.Empty);
            AssertColor(ReadCenter(channel.editable.Front), sourceColor, 0.004f);
            AssertColor(ReadCenter(channel.editable.Back), sourceColor, 0.004f);
        }

        [Test]
        public void CrossSlotPathCreatesAReusableLinkedResultLayer()
        {
            TextureSet torso = CreateSet(TexturePaintChannel.Albedo, Color.white);
            TextureStore store = CreateStore(torso);
            TextureSet legs = CreateSet(TexturePaintChannel.Albedo, Color.white);
            legs.persistentId = "release-legs";
            AddSet(store, legs);
            TexturePaintLayer path = torso.AddSplineLayer("Seam Path");
            TexturePaintLayer existingLegPaint = legs.AddLayer("Leg Detail");
            int previousLegLayer = legs.activeLayerIndex;
            const string key = "texture-paint-spline:seam-path";

            Dictionary<TextureSet, int> previous = TexturePaintStageWindow.ActivateSplineResultLayers(
                torso, path, key, new[] { torso, legs });

            Assert.That(path.proceduralGroupKey, Is.EqualTo(key));
            Assert.That(previous[legs], Is.EqualTo(previousLegLayer));
            Assert.That(legs.layers[legs.activeLayerIndex], Is.Not.SameAs(existingLegPaint));
            Assert.That(legs.layers[legs.activeLayerIndex].proceduralGroupKey, Is.EqualTo(key));
            int linkedLayerCount = legs.layers.Count;
            TexturePaintStageWindow.ActivateSplineResultLayers(torso, path, key, new[] { torso, legs });
            Assert.That(legs.layers.Count, Is.EqualTo(linkedLayerCount), "Reapplying a path must reuse its linked layer.");
        }

        [Test]
        public void DocumentSaveReopenRestoresPixelsLayersSplinesLayerMasksAndPluginProvenance()
        {
            Material material = Own(new Material(Shader.Find("Standard")) { name = "Persistence Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet originalSet = CreateSet(TexturePaintChannel.Albedo,
                new Color(0.17f, 0.37f, 0.71f, 0.83f), material, mesh);
            AddChannel(originalSet, TexturePaintChannel.Normal,
                new Color(0.5f, 0.5f, 1f, 1f));
            AddChannel(originalSet, TexturePaintChannel.NormalControl,
                new Color(0.5f, 0.5f, 0.5f, 1f));
            originalSet.normalControlStrength = 6.25f;
            originalSet.normalControlRadius = 4;
            originalSet.normalControlInvert = true;
            TextureStore originalStore = CreateStore(originalSet);
            TexturePaintLayer paint = originalSet.AddLayer("Paint Detail");
            paint.logicalLayerId = "logical-skin-detail";
            paint.paintTargetId = "udim:body";
            paint.pluginId = "com.uma.tests.v2";
            paint.pluginVersion = "2.4.1";
            paint.pluginParametersJson = "{\"amount\":0.75}";
            paint.effects.outerShadow.enabled = true;
            paint.effects.outerShadow.channel = TexturePaintChannel.Albedo;
            paint.effects.outerShadow.color = new Color(0.12f, 0.23f, 0.34f, 0.8f);
            paint.effects.outerShadow.width = 17f;
            paint.effects.outerShadow.level = 0.42f;
            paint.effects.outerShadow.offset = new Vector2(3f, -4f);
            paint.effects.outerShadow.curve = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);
            paint.effects.edgeFade.enabled = true;
            paint.effects.edgeFade.edgeFadeStart = 0.64f;
            paint.effects.edgeFade.edgeFadeSize = 0.73f;
            EditableTextureTarget paintPixels = originalSet.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            paintPixels.Reset(null, new Color(0.77f, 0.23f, 0.11f, 0.61f));
            EditableTextureTarget normalControlPixels = originalSet.GetPaintTarget(
                TexturePaintChannel.NormalControl, TexturePaintSourceMode.SourceOverlay);
            normalControlPixels.Reset(null, new Color(0.82f, 0.82f, 0.82f, 0.47f));
            TexturePaintLayerChannelSettings normalControlSettings = paint.GetChannelSettings(
                TexturePaintChannel.NormalControl);
            normalControlSettings.hasNormalControlStrength = true;
            normalControlSettings.normalControlStrength = 9.5f;
            TexturePaintChannelSourceSettings normalControlSource = normalControlSettings.sourceSettings;
            normalControlSource.source = TexturePaintBrushSource.Color;
            normalControlSource.color = new Color(0.21f, 0.21f, 0.21f, 0.73f);
            normalControlSource.tiling = new Vector2(2.5f, 4.5f);
            TexturePaintLayer splineLayer = originalSet.AddSplineLayer("Surface Path");
            splineLayer.spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            splineLayer.spline.AddPoint(Vector3.one, Vector2.one, 0, 1, Vector3.forward);
            splineLayer.spline.SetWorldControl(0, false, new Vector3(0.3f, 0.7f), new Vector2(0.3f, 0.7f));
            TexturePaintLayerMask splineMask = originalSet.AddLayerMask(splineLayer, 0f);
            splineMask.target.Reset(null, TextureSet.MaskColor(0.37f));
            splineMask.effects.noise.enabled = true;
            splineMask.effects.noise.seed = 73;
            splineMask.effects.noise.tiling = new Vector2(2f, 5f);
            splineMask.effects.noise.opacity = 0.42f;
            Texture2D maskSource = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                { name = "Mask Paint Source" };
            maskSource.SetPixels(new[] { Color.black, Color.white, Color.gray, Color.red });
            maskSource.Apply(false, false);
            AssetDatabase.CreateAsset(maskSource, Folder + "/Mask Paint Source.asset");
            splineMask.sourceSettings = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Texture,
                sourceTexture = maskSource,
                invert = true,
                tiling = new Vector2(3f, 7f)
            };
            splineMask.sourceChannel = TexturePaintChannel.Roughness;

            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            AssetDatabase.CreateAsset(document, Folder + "/Round Trip Document.asset");
            TexturePaintDocumentStorage.Save(document, originalStore);
            string documentId = document.documentId;
            string revision = document.revisionId;
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(Folder + "/Round Trip Document.asset", ImportAssetOptions.ForceSynchronousImport);
            TexturePaintDocument reopened = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(
                Folder + "/Round Trip Document.asset");

            TextureSet restoredSet = CreateSet(TexturePaintChannel.Albedo, Color.black, material, mesh);
            AddChannel(restoredSet, TexturePaintChannel.Normal,
                new Color(0.5f, 0.5f, 1f, 1f));
            AddChannel(restoredSet, TexturePaintChannel.NormalControl,
                new Color(0.5f, 0.5f, 0.5f, 1f));
            TextureStore restoredStore = CreateStore(restoredSet);
            TexturePaintDocumentStorage.Restore(reopened, restoredStore);

            Assert.That(reopened.documentId, Is.EqualTo(documentId));
            Assert.That(reopened.revisionId, Is.EqualTo(revision));
            AssertColor(ReadCenter(restoredSet.GetChannel(TexturePaintChannel.Albedo).Texture),
                new Color(0.17f, 0.37f, 0.71f, 0.83f), 0.004f);
            Assert.That(restoredSet.layers, Has.Count.EqualTo(2));
            Assert.That(restoredSet.layers[0].kind, Is.EqualTo(TexturePaintLayerKind.Paint));
            Assert.That(restoredSet.layers[0].IsSplineLayer, Is.False,
                "A domain/asset reload must not classify a Paint layer from an inline spline payload.");
            Assert.That(restoredSet.layers[0].spline, Is.Null);
            Assert.That(restoredSet.layers[0].pluginId, Is.EqualTo("com.uma.tests.v2"));
            Assert.That(restoredSet.layers[0].logicalLayerId, Is.EqualTo("logical-skin-detail"));
            Assert.That(restoredSet.layers[0].paintTargetId, Is.EqualTo("udim:body"));
            Assert.That(restoredSet.layers[0].pluginParametersJson, Does.Contain("0.75"));
            Assert.That(restoredSet.layers[0].effects.outerShadow.enabled, Is.True);
            Assert.That(restoredSet.layers[0].effects.outerShadow.width, Is.EqualTo(17f));
            Assert.That(restoredSet.layers[0].effects.outerShadow.level, Is.EqualTo(0.42f));
            Assert.That(restoredSet.layers[0].effects.outerShadow.offset,
                Is.EqualTo(new Vector2(3f, -4f)));
            Assert.That(restoredSet.layers[0].effects.outerShadow.curve.Evaluate(1f),
                Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(restoredSet.layers[0].effects.edgeFade.enabled, Is.True);
            Assert.That(restoredSet.layers[0].effects.edgeFade.edgeFadeStart, Is.EqualTo(0.64f));
            Assert.That(restoredSet.layers[0].effects.edgeFade.edgeFadeSize, Is.EqualTo(0.73f));
            AssertColor(ReadCenter(restoredSet.layers[0].channels[TexturePaintChannel.Albedo].Front),
                new Color(0.77f, 0.23f, 0.11f, 0.61f), 0.004f);
            Assert.That(restoredSet.normalControlStrength, Is.EqualTo(6.25f));
            Assert.That(restoredSet.normalControlRadius, Is.EqualTo(4));
            Assert.That(restoredSet.normalControlInvert, Is.True);
            AssertColor(ReadCenter(restoredSet.layers[0].channels[TexturePaintChannel.NormalControl].Front),
                new Color(0.82f, 0.82f, 0.82f, 0.47f), 0.004f);
            TexturePaintChannelSourceSettings restoredControlSource = restoredSet.layers[0]
                .GetChannelSettings(TexturePaintChannel.NormalControl).sourceSettings;
            TexturePaintLayerChannelSettings restoredControlSettings = restoredSet.layers[0]
                .GetChannelSettings(TexturePaintChannel.NormalControl);
            Assert.That(restoredControlSettings.hasNormalControlStrength, Is.True);
            Assert.That(restoredControlSettings.normalControlStrength, Is.EqualTo(9.5f));
            Assert.That(restoredControlSource.source, Is.EqualTo(TexturePaintBrushSource.Color));
            AssertColor(restoredControlSource.color,
                new Color(0.21f, 0.21f, 0.21f, 0.73f), 0.0001f);
            Assert.That(restoredControlSource.tiling, Is.EqualTo(new Vector2(2.5f, 4.5f)));
            Assert.That(restoredSet.layers[1].spline.PointCount, Is.EqualTo(2));
            Assert.That(restoredSet.layers[1].spline.worldOutControls[0].y, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(restoredSet.layers[1].layerMask, Is.Not.Null);
            Assert.That(restoredSet.layers[1].layerMask.baseValue, Is.EqualTo(0f));
            AssertColor(ReadCenter(restoredSet.layers[1].layerMask.target.Front),
                TextureSet.MaskColor(0.37f), 0.01f);
            Assert.That(restoredSet.layers[1].layerMask.effects.noise.enabled, Is.True);
            Assert.That(restoredSet.layers[1].layerMask.effects.noise.seed, Is.EqualTo(73));
            Assert.That(restoredSet.layers[1].layerMask.effects.noise.tiling,
                Is.EqualTo(new Vector2(2f, 5f)));
            Assert.That(restoredSet.layers[1].layerMask.effects.noise.opacity,
                Is.EqualTo(0.42f));
            Assert.That(restoredSet.layers[1].layerMask.sourceSettings.source,
                Is.EqualTo(TexturePaintBrushSource.Color),
                "Layer-mask painting must normalize legacy texture or overlay sources to grayscale.");
            Assert.That(restoredSet.layers[1].layerMask.sourceSettings.sourceTexture,
                Is.Null);
            Assert.That(restoredSet.layers[1].layerMask.sourceSettings.sourceOverlay,
                Is.Null);
            Assert.That(restoredSet.layers[1].layerMask.sourceSettings.invert, Is.False);
            Assert.That(restoredSet.layers[1].layerMask.sourceSettings.tiling,
                Is.EqualTo(Vector2.one));
            Assert.That(restoredSet.layers[1].layerMask.sourceChannel,
                Is.EqualTo(TexturePaintChannel.Albedo));
        }

        [TestCase(TexturePaintExportBitDepth.Eight, 0.012f)]
        [TestCase(TexturePaintExportBitDepth.Sixteen, 0.003f)]
        [TestCase(TexturePaintExportBitDepth.HalfFloat, 0.004f)]
        public void ExportRoundTripPreservesDeclaredPrecision(TexturePaintExportBitDepth bitDepth, float tolerance)
        {
            Color source = bitDepth == TexturePaintExportBitDepth.HalfFloat
                ? new Color(1.75f, 0.5678f, 0.8765f, 0.4321f)
                : new Color(0.1234f, 0.5678f, 0.8765f, 0.4321f);
            TextureSet set = CreateSet(TexturePaintChannel.Metallic, source);
            TextureStore store = CreateStore(set);
            TexturePaintExportTemplate template = CreateTemplate(bitDepth);
            ConfigureExportDescriptor(set, bitDepth, new UMAMaterial.TextureChannelLayout
            {
                mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                red = UMAMaterial.TextureChannelUsage.Metallic,
                green = UMAMaterial.TextureChannelUsage.Unused,
                blue = UMAMaterial.TextureChannelUsage.Unused,
                alpha = UMAMaterial.TextureChannelUsage.Unused
            });

            TexturePaintExportResult result = TexturePaintExporter.Export(store, set, null, template, null);
            Assert.That(result.texturePaths, Has.Count.EqualTo(1));
            Assert.That(result.texturePaths[0], Does.EndWith(bitDepth == TexturePaintExportBitDepth.HalfFloat ? ".exr" : ".png"));
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(result.texturePaths[0]);
            Assert.That(imported, Is.Not.Null);
            AssertColor(ReadTextureCenter(imported), new Color(source.r, 0f, 0f, 0f), tolerance);
        }

        [Test]
        public void CustomPackedMapRoundTripPreservesSemanticComponents()
        {
            TextureSet set = CreateSet(TexturePaintChannel.Metallic, new Color(0.2f, 0f, 0f, 1f));
            AddChannel(set, TexturePaintChannel.AmbientOcclusion, new Color(0.65f, 0f, 0f, 1f));
            AddChannel(set, TexturePaintChannel.Roughness, new Color(0.8f, 0f, 0f, 1f));
            TextureStore store = CreateStore(set);
            TexturePaintExportTemplate template = CreateTemplate(TexturePaintExportBitDepth.Sixteen);
            ConfigureExportDescriptor(set, TexturePaintExportBitDepth.Sixteen,
                new UMAMaterial.TextureChannelLayout
                {
                    mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                    red = UMAMaterial.TextureChannelUsage.Metallic,
                    green = UMAMaterial.TextureChannelUsage.Smoothness,
                    blue = UMAMaterial.TextureChannelUsage.AmbientOcclusion,
                    alpha = UMAMaterial.TextureChannelUsage.Unused
                });
            RenderTexture packed = CreateRenderTexture("Release Packed Output", 16,
                RenderTextureFormat.ARGBHalf);
            Graphics.Blit(Texture2D.whiteTexture, packed);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = packed;
            GL.Clear(false, true, new Color(0.2f, 0.2f, 0.65f, 1f));
            RenderTexture.active = previous;
            set.physicalChannelGroups["_MetallicGlossMap"] = new TexturePhysicalChannelGroup
            {
                materialProperty = "_MetallicGlossMap",
                packed = packed
            };

            TexturePaintExportResult result = TexturePaintExporter.Export(store, set, null, template, null);
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(result.texturePaths[0]);
            Color actual = ReadTextureCenter(imported);
            AssertColor(actual, new Color(0.2f, 0.2f, 0.65f, 0f), 0.004f);
        }

        [Test]
        public void UmaSkinMaskMapUnpacksAndRepacksThicknessAoDetailAndSmoothness()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            Material preview = Own(new Material(Shader.Find("Standard")) { name = "Custom Packed Preview" });
            Texture2D source = CreateSolidTexture(new Color(0.2f, 0.65f, 0.37f, 0.25f));
            preview.SetTexture("_MetallicGlossMap", source);
            UMAMaterial umaMaterial = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.material = preview;
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.Texture,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_MetallicGlossMap",
                    sourceTextureName = "_MetallicGlossMap",
                    DownSample = 1,
                    textureChannelLayout = new UMAMaterial.TextureChannelLayout
                    {
                        mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                        red = UMAMaterial.TextureChannelUsage.Thickness,
                        green = UMAMaterial.TextureChannelUsage.AmbientOcclusion,
                        blue = UMAMaterial.TextureChannelUsage.DetailMask,
                        alpha = UMAMaterial.TextureChannelUsage.Smoothness
                    }
                }
            };

            Mesh mesh = Own(CreateQuadMesh());
            MeshReconstructionResult reconstruction = new MeshReconstructionResult();
            reconstruction.surfaces.Add(new ReconstructedSurface
            {
                index = 0,
                mesh = mesh,
                previewMaterial = preview,
                sourceMaterial = preview,
                umaMaterial = umaMaterial,
                slotName = "Body",
                slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" },
                triangleIslands = new[] { 0, 0 }
            });

            TextureStore store = new TextureStore();
            ownedStores.Add(store);
            store.Initialize(reconstruction, 128,
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"),
                TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute"));
            TextureSet set = store.Sets[0];

            Assert.That(set.physicalChannelGroups.ContainsKey("_MetallicGlossMap"), Is.True);
            Assert.That(ReadCenter(set.GetChannel(TexturePaintChannel.Thickness).Texture).r,
                Is.EqualTo(0.2f).Within(0.01f));
            Assert.That(ReadCenter(set.GetChannel(TexturePaintChannel.AmbientOcclusion).Texture).r,
                Is.EqualTo(0.65f).Within(0.01f));
            Assert.That(ReadCenter(set.GetChannel(TexturePaintChannel.DetailMask).Texture).r,
                Is.EqualTo(0.37f).Within(0.01f));
            Assert.That(ReadCenter(set.GetChannel(TexturePaintChannel.Roughness).Texture).r,
                Is.EqualTo(0.75f).Within(0.01f));

            Texture2D thickness = CreateSolidTexture(new Color(0.8f, 0.8f, 0.8f, 1f));
            Texture2D occlusion = CreateSolidTexture(new Color(0.3f, 0.3f, 0.3f, 1f));
            Texture2D detailMask = CreateSolidTexture(new Color(0.9f, 0.9f, 0.9f, 1f));
            Texture2D roughness = CreateSolidTexture(new Color(0.4f, 0.4f, 0.4f, 1f));
            set.GetChannel(TexturePaintChannel.Thickness).editable.Reset(thickness, Color.black);
            set.GetChannel(TexturePaintChannel.AmbientOcclusion).editable.Reset(occlusion, Color.black);
            set.GetChannel(TexturePaintChannel.DetailMask).editable.Reset(detailMask, Color.white);
            set.GetChannel(TexturePaintChannel.Roughness).editable.Reset(roughness, Color.white);
            set.BindPreviewTextures();

            Color packed = ReadCenter(set.physicalChannelGroups["_MetallicGlossMap"].packed);
            AssertColor(packed, new Color(0.8f, 0.3f, 0.9f, 0.6f), 0.01f);
        }

        [Test]
        public void UmaSkinColorMaskRemainsOneEditableRgbaColorChannel()
        {
            Material preview = Own(new Material(Shader.Find("Standard"))
                { name = "Skin Color Mask Preview" });
            Color authored = new Color(0.72f, 0.23f, 0.16f, 0.64f);
            Texture2D source = CreateSolidTexture(authored);
            preview.SetTexture("_MainTex", source);
            UMAMaterial umaMaterial = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.material = preview;
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.Texture,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_MainTex",
                    sourceTextureName = "_Skinmask",
                    DownSample = 1,
                    textureChannelLayout = new UMAMaterial.TextureChannelLayout
                    {
                        mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                        red = UMAMaterial.TextureChannelUsage.SkinColorMask,
                        green = UMAMaterial.TextureChannelUsage.SkinColorMask,
                        blue = UMAMaterial.TextureChannelUsage.SkinColorMask,
                        alpha = UMAMaterial.TextureChannelUsage.SkinColorMask
                    }
                }
            };
            MeshReconstructionResult reconstruction = new MeshReconstructionResult();
            reconstruction.surfaces.Add(new ReconstructedSurface
            {
                index = 0,
                mesh = Own(CreateQuadMesh()),
                previewMaterial = preview,
                sourceMaterial = preview,
                umaMaterial = umaMaterial,
                sourceTextures = new Texture[] { source },
                slotName = "Body",
                slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" },
                triangleIslands = new[] { 0, 0 }
            });

            TextureStore store = new TextureStore();
            ownedStores.Add(store);
            store.Initialize(reconstruction, 128,
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"),
                TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute"));
            TextureSet set = store.Sets[0];
            TextureChannelTarget skin = set.GetChannel(TexturePaintChannel.SkinColorMask);

            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.sRGB, Is.True);
            Assert.That(skin.physicalProperty, Is.Null.Or.Empty,
                "A complete RGBA Skin Color Mask should not be split into packed scalar targets.");
            AssertColor(ReadCenter(skin.Texture), authored, 0.012f);
        }

        [Test]
        public void UdimPreviewMaterialsUseFirstMemberParametersAndKeepPerTileTextures()
        {
            Shader shader = Shader.Find("Standard");
            Material firstSource = Own(new Material(shader) { name = "Tile 1001 Source" });
            Material secondSource = Own(new Material(shader) { name = "Tile 1002 Source" });
            Material firstPreview = Own(new Material(firstSource) { name = "Tile 1001 Preview" });
            Material secondPreview = Own(new Material(secondSource) { name = "Tile 1002 Preview" });
            Texture2D firstTexture = CreateSolidTexture(Color.red);
            Texture2D secondTexture = CreateSolidTexture(Color.green);
            firstSource.SetFloat("_Glossiness", 0.81f);
            secondSource.SetFloat("_Glossiness", 0.17f);
            firstPreview.SetTexture("_MainTex", firstTexture);
            secondPreview.SetTexture("_MainTex", secondTexture);

            UMAMaterial uma = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            uma.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.Texture,
                    materialPropertyName = "_MainTex",
                    sourceTextureName = "_MainTex",
                    textureFormat = RenderTextureFormat.ARGB32,
                    DownSample = 1
                }
            };
            var firstSurface = new ReconstructedSurface
            {
                sourceMaterial = firstSource,
                previewMaterial = firstPreview,
                umaMaterial = uma,
                sourceTextures = new Texture[] { firstTexture }
            };
            var secondSurface = new ReconstructedSurface
            {
                sourceMaterial = secondSource,
                previewMaterial = secondPreview,
                umaMaterial = uma,
                sourceTextures = new Texture[] { secondTexture }
            };
            var target = new TexturePaintLogicalTarget { isUdim = true };
            var firstMember = new TexturePaintLogicalTargetMember { udimTileNumber = 1001 };
            var secondMember = new TexturePaintLogicalTargetMember { udimTileNumber = 1002 };
            firstMember.surfaces.Add(firstSurface);
            secondMember.surfaces.Add(secondSurface);
            target.members.Add(firstMember);
            target.members.Add(secondMember);

            MeshReconstructor.ApplyCanonicalUdimMaterialProperties(target);

            Assert.That(secondPreview.GetFloat("_Glossiness"), Is.EqualTo(0.81f).Within(0.0001f));
            Assert.That(firstPreview.GetTexture("_MainTex"), Is.SameAs(firstTexture));
            Assert.That(secondPreview.GetTexture("_MainTex"), Is.SameAs(secondTexture));
            Assert.That(secondPreview.name, Is.EqualTo("Tile 1002 Preview"));
        }

        [Test]
        public void GeneratedUmaAtlasIsNeverUsedAsAnAuthoringSource()
        {
            Material preview = Own(new Material(Shader.Find("Standard")) { name = "No Atlas Preview" });
            Texture2D nativeSource = CreateSolidTexture(Color.green);
            Texture2D generatedAtlas = CreateSolidTexture(Color.red);
            preview.SetTexture("_MainTex", generatedAtlas);
            UMAMaterial umaMaterial = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.material = preview;
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.DiffuseTexture,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_MainTex",
                    sourceTextureName = "MainTex",
                    DownSample = 1
                }
            };
            UMAData.GeneratedMaterial generated = new UMAData.GeneratedMaterial
            {
                umaMaterial = umaMaterial,
                material = preview,
                resultingAtlasList = new Texture[] { generatedAtlas }
            };
            Mesh mesh = Own(CreateQuadMesh());
            MeshReconstructionResult reconstruction = new MeshReconstructionResult();
            reconstruction.surfaces.Add(new ReconstructedSurface
            {
                index = 0,
                mesh = mesh,
                previewMaterial = preview,
                sourceMaterial = preview,
                umaMaterial = umaMaterial,
                generatedMaterial = generated,
                sourceTextures = new Texture[] { nativeSource },
                sourceNormalIsUnityPacked = new bool[1],
                allowMissingSourceTextures = true,
                slotName = "Body",
                slotNames = new List<string> { "Body" },
                triangleSlotNames = new[] { "Body", "Body" },
                triangleIslands = new[] { 0, 0 }
            });

            TextureStore store = new TextureStore();
            ownedStores.Add(store);
            store.Initialize(reconstruction, 128,
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"),
                TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute"));
            TextureChannelTarget target = store.Sets[0].GetChannel(TexturePaintChannel.Albedo);

            Assert.That(target, Is.Not.Null);
            Assert.That(target.sourceTexture, Is.SameAs(nativeSource));
            Assert.That(target.sourceTexture, Is.Not.SameAs(generatedAtlas));
            AssertColor(ReadCenter(target.editable.Front), Color.green, 0.01f);
        }

        [Test]
        public void NativeOverlayStackReconstructionPreservesSourceResolutionAndPixels()
        {
            Texture2D source = Own(new Texture2D(64, 32, TextureFormat.RGBA32, false, false));
            Color[] pixels = new Color[source.width * source.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.green;
            source.SetPixels(pixels);
            source.Apply(false, false);
            OverlayDataAsset overlayAsset = Own(ScriptableObject.CreateInstance<OverlayDataAsset>());
            overlayAsset.name = "Native Base";
            overlayAsset.textureList = new Texture[] { source };
            OverlayData overlay = new OverlayData(overlayAsset);
            SlotDataAsset slotAsset = Own(ScriptableObject.CreateInstance<SlotDataAsset>());
            slotAsset.name = "Native Body";
            SlotData slot = new SlotData(slotAsset);
            UMAData.MaterialFragment fragment = new UMAData.MaterialFragment
            {
                baseColor = Color.white,
                slotData = slot,
                baseOverlay = new UMAData.textureData
                {
                    textureList = new Texture[] { source },
                    overlayType = OverlayDataAsset.OverlayType.Normal
                },
                AdditionalOverlays = System.Array.Empty<UMAData.textureData>(),
                rects = System.Array.Empty<Rect>(),
                overlayData = new[] { overlay },
                overlayList = new List<OverlayData> { overlay },
                overlayColors = System.Array.Empty<Color32>(),
                channelMask = new[] { new[] { Color.white } },
                channelAdditiveMask = new[] { new[] { Color.clear } }
            };
            UMAMaterial umaMaterial = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.DiffuseTexture,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_MainTex",
                    sourceTextureName = "MainTex"
                }
            };
            // Match UMAGeneratorPro's fragment invariant. Native reconstruction also accepts a
            // detached slot, but this test should exercise a structurally valid UMA fragment.
            slot.altMaterial = umaMaterial;
            overlayAsset.material = umaMaterial;
            fragment.umaMaterial = umaMaterial;
            TextureMerge merge = AssetDatabase.LoadAssetAtPath<TextureMerge>(
                UMAPathUtility.ResolveInstallAssetPath("Core/StandardAssets/UMA/Atlas/TextureMerge.asset"));
            Assert.That(merge, Is.Not.Null);
            MethodInfo reconstruct = typeof(MeshReconstructor).GetMethod("BuildNativeOverlaySources",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(reconstruct, Is.Not.Null);
            object[] arguments = { umaMaterial, fragment, merge, null, null };
            Texture[] reconstructed = null;
            List<Texture> owned = null;
            try
            {
                reconstructed = (Texture[])reconstruct.Invoke(null, arguments);
                owned = arguments[3] as List<Texture>;
                Assert.That(reconstructed, Has.Length.EqualTo(1));
                Assert.That(reconstructed[0].width, Is.EqualTo(64));
                Assert.That(reconstructed[0].height, Is.EqualTo(32));
                AssertColor(ReadTextureCenter(reconstructed[0]), Color.green, 0.02f);
            }
            finally
            {
                for (int i = 0; owned != null && i < owned.Count; i++)
                {
                    if (owned[i] is RenderTexture renderTexture) renderTexture.Release();
                    Object.DestroyImmediate(owned[i]);
                }
            }
        }

        [Test]
        public void StableSurfaceIdentityDistinguishesSharedSlotsAndSeparateMaterials()
        {
            Material shared = Own(new Material(Shader.Find("Standard")) { name = "Shared Material" });
            Material separate = Own(new Material(Shader.Find("Standard")) { name = "Separate Material" });
            Mesh mesh = Own(CreateQuadMesh());
            TextureSet torso = CreateSet(TexturePaintChannel.Albedo, Color.white, shared, mesh);
            ConfigureSlot(torso, "Torso", 0);
            TextureSet legs = CreateSet(TexturePaintChannel.Albedo, Color.white, shared, mesh);
            ConfigureSlot(legs, "Legs", 1);
            TextureSet torsoSeparate = CreateSet(TexturePaintChannel.Albedo, Color.white, separate, mesh);
            ConfigureSlot(torsoSeparate, "Torso", 2);
            TextureStore store = CreateStore(torso);
            AddSet(store, legs);
            AddSet(store, torsoSeparate);

            TexturePaintDocumentStorage.AssignStableSurfaceIds(store);

            Assert.That(torso.persistentId, Is.Not.EqualTo(legs.persistentId));
            Assert.That(torso.persistentId, Is.Not.EqualTo(torsoSeparate.persistentId));
            Assert.That(legs.persistentId, Is.Not.EqualTo(torsoSeparate.persistentId));
            string firstIdentity = torso.persistentId;
            TexturePaintDocumentStorage.AssignStableSurfaceIds(store);
            Assert.That(torso.persistentId, Is.EqualTo(firstIdentity));
        }

        [Test]
        public void StableSurfaceIdentityIgnoresUmaGeneratedMaterialNonce()
        {
            Material material = Own(new Material(Shader.Find("Standard"))
                { name = "UMA3_SkinShader_URP_Genb_123456789_UMA30_Body_UDIM1001" });
            TextureSet body = CreateSet(TexturePaintChannel.Albedo, Color.white, material,
                Own(CreateQuadMesh()));
            ConfigureSlot(body, "UMA30_Body_UDIM1001_slot_baked_Human Male 3.0", 0);
            TextureStore store = CreateStore(body);

            TexturePaintDocumentStorage.AssignStableSurfaceIds(store);
            string firstIdentity = body.persistentId;
            material.name = "UMA3_SkinShader_URP_Genb_987654321_UMA30_Body_UDIM1001";
            TexturePaintDocumentStorage.AssignStableSurfaceIds(store);

            Assert.That(body.persistentId, Is.EqualTo(firstIdentity),
                "UMAGeneratorPro's random material nonce must not become document identity.");
            Assert.That(TexturePaintDocumentStorage.StableMaterialName(material.name),
                Is.EqualTo("UMA3_SkinShader_URP_Genb_UMA30_Body_UDIM1001"));
        }

        [Test]
        public void LegacyGeneratedSurfaceRebindRestoresLayerAndBlackMaskWhenTopologyOrderChanges()
        {
            Material savedMaterial = Own(new Material(Shader.Find("Standard"))
                { name = "UMA3_SkinShader_URP_Genb_123456789_UMA30_Body_UDIM1001" });
            Mesh savedMesh = Own(CreateQuadMesh());
            TextureSet savedSet = CreateSet(TexturePaintChannel.Albedo, Color.black, savedMaterial, savedMesh);
            ConfigureSlot(savedSet, "UMA30_Body_UDIM1001_slot_baked_Human Male 3.0", 0);
            TextureStore savedStore = CreateStore(savedSet);
            TexturePaintLayer savedLayer = savedSet.AddLayer("Chin Stubble");
            Assert.That(savedSet.AddLayerMask(savedLayer, 0f), Is.Not.Null);
            TexturePaintDocument document = Own(ScriptableObject.CreateInstance<TexturePaintDocument>());

            TexturePaintDocumentStorage.Save(document, savedStore);
            Assert.That(document.surfaces, Has.Count.EqualTo(1));
            Assert.That(document.surfaces[0].layers, Has.Count.EqualTo(1));
            Assert.That(document.surfaces[0].layers[0].hasMask, Is.True);
            Assert.That(document.surfaces[0].layers[0].maskBaseValue, Is.Zero);

            Material regeneratedMaterial = Own(new Material(Shader.Find("Standard"))
                { name = "UMA3_SkinShader_URP_Genb_987654321_UMA30_Body_UDIM1001" });
            Mesh regeneratedMesh = Own(CreateQuadMesh());
            regeneratedMesh.triangles = new[] { 0, 2, 3, 0, 1, 2 };
            TextureSet restoredSet = CreateSet(TexturePaintChannel.Albedo, Color.black,
                regeneratedMaterial, regeneratedMesh);
            ConfigureSlot(restoredSet, "UMA30_Body_UDIM1001_slot_baked_Human Male 3.0", 0);
            TextureStore restoredStore = CreateStore(restoredSet);

            TexturePaintDocumentStorage.RestoreReport restore =
                TexturePaintDocumentStorage.Restore(document, restoredStore);

            Assert.That(restore.restoredSurfaces, Is.EqualTo(1));
            Assert.That(restore.restoredLayers, Is.EqualTo(1));
            Assert.That(restore.unboundLayers, Is.Zero);
            Assert.That(restoredSet.layers, Has.Count.EqualTo(1));
            Assert.That(restoredSet.layers[0].name, Is.EqualTo("Chin Stubble"));
            Assert.That(restoredSet.layers[0].layerMask, Is.Not.Null);
            Assert.That(restoredSet.layers[0].layerMask.baseValue, Is.Zero);
            AssertColor(ReadCenter(restoredSet.layers[0].layerMask.target.Front),
                TextureSet.MaskColor(0f), 0.01f);
        }

        [Test]
        public void ToolRailUsesTheFirstThirteenOrderedSpriteSheetSlices()
        {
            for (int index = 0; index < 13; index++)
            {
                Sprite sprite = TexturePaintStageWindow.GetToolRailIcon(index);
                Assert.That(sprite, Is.Not.Null, "Missing TexturePaintIcons sprite at index " + index + ".");
                Assert.That(sprite.name, Is.EqualTo("TexturePaintIcons_" + index));
                Assert.That(sprite.texture, Is.Not.Null);
            }
        }

        [Test]
        public void SceneViewPaintingToolbarOwnsAllThirteenToolControls()
        {
            Assert.That(TexturePaintSceneToolPaletteOverlay.Title,
                Is.EqualTo("Overlay Painter Toolbar"));
            Assert.That(TexturePaintSceneToolPaletteOverlay.ElementIds, Has.Length.EqualTo(13));
            Assert.That(TexturePaintSceneToolPaletteOverlay.ElementIds, Does.Contain(
                TexturePaintScenePaintToolToggle.Id));
            Assert.That(TexturePaintSceneToolPaletteOverlay.ElementIds, Does.Contain(
                TexturePaintScenePolygonFillToggle.Id));
            Assert.That(TexturePaintSceneToolPaletteOverlay.ElementIds, Does.Contain(
                TexturePaintSceneIslandFillToggle.Id));
            Assert.That(TexturePaintSceneToolPaletteOverlay.ElementIds, Does.Contain(
                TexturePaintScenePathToolToggle.Id));
            Assert.That(TexturePaintSceneToolPaletteOverlay.ElementIds, Does.Contain(
                TexturePaintSceneToolHelpButton.Id));

            var paint = new TexturePaintScenePaintToolToggle();
            Assert.That(paint.Q<UnityEngine.UIElements.Image>(), Is.Not.Null,
                "The Scene-view tool must render the sliced Overlay Painter icon, not the full sprite sheet.");
        }

        [TestCase(600f, 178f, true, 178f)]
        [TestCase(300f, 178f, true, 135f)]
        [TestCase(200f, 50f, true, 112f)]
        [TestCase(1000f, 800f, true, 450f)]
        [TestCase(600f, 178f, false, 0f)]
        public void BrushWindowOwnsAClampedOptionalAssetShelf(float windowHeight,
            float requestedHeight, bool visible, float expectedHeight)
        {
            Assert.That(TexturePaintStageWindow.CalculateBrushAssetShelfHeight(
                windowHeight, requestedHeight, visible), Is.EqualTo(expectedHeight));
        }

        [TestCase(-0.1d, 0f)]
        [TestCase(0d, 1f)]
        [TestCase(7.5d, 1f)]
        [TestCase(8.75d, 0.5f)]
        [TestCase(9.9d, 0.04f)]
        [TestCase(10d, 0f)]
        [TestCase(12d, 0f)]
        public void ImportWarningSceneNoticeLastsTenSecondsAndFadesAtTheEnd(double elapsed,
            float expectedAlpha)
        {
            Assert.That(TexturePaintStageWindow.CalculateImportWarningNoticeAlpha(elapsed),
                Is.EqualTo(expectedAlpha).Within(0.0001f));
        }

        [Test]
        public void ObjectPickerCompletionNeverConsumesLayoutOrRepaintEvents()
        {
            var layout = new Event
                { type = EventType.Layout, commandName = "ObjectSelectorClosed" };
            var repaint = new Event
                { type = EventType.Repaint, commandName = "ObjectSelectorSelectionDone" };
            var closed = new Event
                { type = EventType.ExecuteCommand, commandName = "ObjectSelectorClosed" };
            var selected = new Event
                { type = EventType.ExecuteCommand, commandName = "ObjectSelectorSelectionDone" };
            var unrelated = new Event
                { type = EventType.ExecuteCommand, commandName = "UnrelatedCommand" };

            Assert.That(TexturePaintStageWindow.IsObjectPickerCompletionEvent(layout), Is.False);
            Assert.That(TexturePaintStageWindow.IsObjectPickerCompletionEvent(repaint), Is.False);
            Assert.That(TexturePaintStageWindow.IsObjectPickerCompletionEvent(closed), Is.True);
            Assert.That(TexturePaintStageWindow.IsObjectPickerCompletionEvent(selected), Is.True);
            Assert.That(TexturePaintStageWindow.IsObjectPickerCompletionEvent(unrelated), Is.False);
            Assert.That(TexturePaintStageWindow.IsObjectPickerCompletionEvent(null), Is.False);
        }

        [Test]
        public void LayerWindowAndSceneToolOverlayHaveDistinctTitles()
        {
            Assert.That(TexturePaintDockWindow.WindowTitle, Is.EqualTo("Overlay Painter Layers"));
            Assert.That(TexturePaintSceneToolPaletteOverlay.Title,
                Is.EqualTo("Overlay Painter Toolbar"));
        }

        [Test]
        public void EscapeExitsLayerMaskModeBeforeUnityCanCloseTheStage()
        {
            var escape = new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape };
            var otherKey = new Event { type = EventType.KeyDown, keyCode = KeyCode.B };

            Assert.That(TexturePaintStageWindow.ShouldExitLayerMaskMode(escape, true, 0), Is.True);
            Assert.That(TexturePaintStageWindow.ShouldExitLayerMaskMode(escape, false, 0), Is.False);
            Assert.That(TexturePaintStageWindow.ShouldExitLayerMaskMode(escape, true, 1), Is.False,
                "An active Geometry Fill consumes the first Escape before Mask mode exits.");
            Assert.That(TexturePaintStageWindow.ShouldExitLayerMaskMode(otherKey, true, 0), Is.False);

            escape.control = true;
            Assert.That(TexturePaintStageWindow.ShouldExitLayerMaskMode(escape, true, 0), Is.False);

            TexturePaintStageWindow stage = Own(ScriptableObject.CreateInstance<TexturePaintStageWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo maskMode = typeof(TexturePaintStageWindow).GetField("layerMaskMode", flags);
            FieldInfo soloMask = typeof(TexturePaintStageWindow).GetField("soloLayerMask", flags);
            MethodInfo handleEscape = typeof(TexturePaintStageWindow).GetMethod(
                "TryExitLayerMaskModeFromShortcut", flags);
            Assert.That(maskMode, Is.Not.Null);
            Assert.That(soloMask, Is.Not.Null);
            Assert.That(handleEscape, Is.Not.Null);

            maskMode.SetValue(stage, true);
            soloMask.SetValue(stage, true);
            escape.control = false;
            Assert.That((bool)handleEscape.Invoke(stage, new object[] { escape }), Is.True);
            Assert.That(escape.type, Is.EqualTo(EventType.Used),
                "Overlay Painter must consume Escape before Unity's stage navigation sees it.");
            Assert.That((bool)maskMode.GetValue(stage), Is.False);
            Assert.That((bool)soloMask.GetValue(stage), Is.False);
        }

        [Test]
        public void CompactWorkspaceDefinesRequestedTabsAndIsOptional()
        {
            string layout = TexturePaintWorkspaceLayout.CompactLayoutDefinition;
            Assert.That(layout, Does.Contain("\"horizontal\": true"));
            Assert.That(layout, Does.Contain("\"TexturePaintDockWindow\""));
            Assert.That(layout, Does.Contain("\"TexturePaintBrushWindow\""));
            Assert.That(layout, Does.Contain("\"SceneView\""));
            Assert.That(layout, Does.Contain("\"TexturePaintUVWindow\""));
            Assert.That(layout, Does.Contain("\"restore_layout_dimension\": true"));

            SceneView compactScene = Own(ScriptableObject.CreateInstance<SceneView>());
            SceneView otherScene = Own(ScriptableObject.CreateInstance<SceneView>());
            Assert.That(TexturePaintWorkspaceLayout.IsExpectedOverlayHost(false, compactScene,
                otherScene), Is.True, "The ordinary workspace retains its existing Scene-view behavior.");
            Assert.That(TexturePaintWorkspaceLayout.IsExpectedOverlayHost(true, compactScene,
                compactScene), Is.True);
            Assert.That(TexturePaintWorkspaceLayout.IsExpectedOverlayHost(true, compactScene,
                otherScene), Is.False,
                "Compact View toolbars must be hidden from every Scene view except its docked Scene tab.");

            UMASettings previousSettings = UMASettings.instance;
            UMASettings settings = Own(ScriptableObject.CreateInstance<UMASettings>());
            try
            {
                UMASettings.instance = settings;
                Assert.That(settings.texturePaintCompactView, Is.True,
                    "Compact View is the recommended default but must remain optional.");
                Assert.That(UMASettings.TexturePaintCompactView, Is.True);
                settings.texturePaintCompactView = false;
                Assert.That(UMASettings.TexturePaintCompactView, Is.False);
            }
            finally
            {
                UMASettings.instance = previousSettings;
            }
        }

        [Test]
        public void SceneToolbarProvidesAnExplicitFullWidthShutdownButton()
        {
            var button = new TexturePaintSceneShutdownButton();

            Assert.That(button.text, Is.EqualTo("Shutdown Overlay Painter"));
            Assert.That(button.tooltip, Does.Contain("Save, discard, or cancel"));
            Assert.That(button.style.minWidth.value.value, Is.GreaterThanOrEqualTo(170f));
            Assert.That(button.style.flexShrink.value, Is.EqualTo(0f));
        }

        [Test]
        public void FreshCharacterLaunchPrefersBodyAndDefaultsToIsolate()
        {
            var internallyNamedBody = new TexturePaintLogicalTarget
            {
                id = "udim:body-skin",
                displayName = "Skin"
            };
            internallyNamedBody.members.Add(new TexturePaintLogicalTargetMember
            {
                slotName = "UMA30_Body_UDIM1001_slot"
            });
            var bodyHair = new TexturePaintLogicalTarget
            {
                id = "slot:Body_Hair",
                displayName = "Body Hair"
            };
            var head = new TexturePaintLogicalTarget
            {
                id = "slot:Head",
                displayName = "Head"
            };
            var body = new TexturePaintLogicalTarget
            {
                id = "udim:human-body",
                displayName = "Human Body"
            };
            body.members.Add(new TexturePaintLogicalTargetMember
            {
                slotName = "UMA30_Body_UDIM1001_slot"
            });

            Assert.That(TexturePaintStageWindow.FindPreferredCharacterBodyTarget(
                new[] { internallyNamedBody, bodyHair, head, body }), Is.SameAs(body),
                "Only the visible target name may qualify a character Body default.");
            Assert.That(TexturePaintStageWindow.FindPreferredCharacterBodyTarget(
                new[] { internallyNamedBody, head }), Is.Null,
                "An internal ID or member slot containing Body must not qualify a differently named target.");
            Assert.That(TexturePaintStageWindow.ShouldDefaultCharacterIsolate(false, null), Is.True);
            Assert.That(TexturePaintStageWindow.ShouldDefaultCharacterIsolate(true, null), Is.False,
                "Standalone slot launches must retain their existing Isolate default.");
        }

        [Test]
        public void CharacterBodyDefaultTakesPriorityOverRestoredSlotSelection()
        {
            var catalog = new TexturePaintLogicalTargetCatalog();
            catalog.Rebuild(new[]
            {
                new ReconstructedSurface { slotNames = new List<string> { "Head" } },
                new ReconstructedSurface { slotNames = new List<string> { "Human Body" } }
            });

            TexturePaintLogicalTarget target = TexturePaintStageWindow.ResolveInitialLogicalTarget(
                catalog, new[] { "Head" }, "slot:Head", true);

            Assert.That(target, Is.SameAs(catalog.FindBySlot("Human Body")));
            Assert.That(target.displayName, Is.EqualTo("Human Body"));
        }

        [Test]
        public void RestoredCharacterWorkspaceRetainsItsExplicitIsolateChoice()
        {
            var currentState = new TexturePaintStageState
            {
                version = TexturePaintStageState.CurrentVersion,
                isolateSelectedSlots = false
            };
            var legacyState = new TexturePaintStageState { version = 9 };

            Assert.That(TexturePaintStageWindow.ShouldDefaultCharacterIsolate(false, currentState), Is.False);
            Assert.That(TexturePaintStageWindow.ShouldDefaultCharacterIsolate(false, legacyState), Is.True);
        }

        [Test]
        public void AltMouseNavigationTakesPriorityOverPainterHandleCapture()
        {
            Event altDown = new Event
            {
                type = EventType.MouseDown,
                modifiers = EventModifiers.Alt
            };
            Event altDrag = new Event
            {
                type = EventType.MouseDrag,
                modifiers = EventModifiers.Alt
            };
            Event ordinaryDrag = new Event { type = EventType.MouseDrag };
            Event altKey = new Event
            {
                type = EventType.KeyDown,
                modifiers = EventModifiers.Alt,
                keyCode = KeyCode.LeftAlt
            };

            Assert.That(TexturePaintStageWindow.ShouldYieldToSceneNavigation(altDown), Is.True,
                $"type={altDown.type}, rawType={altDown.rawType}, alt={altDown.alt}, modifiers={altDown.modifiers}");
            Assert.That(TexturePaintStageWindow.ShouldYieldToSceneNavigation(altDrag), Is.True);
            Assert.That(TexturePaintStageWindow.ShouldYieldToSceneNavigation(ordinaryDrag), Is.False);
            Assert.That(TexturePaintStageWindow.ShouldYieldToSceneNavigation(altKey), Is.False);
        }

        private TexturePaintExportTemplate CreateTemplate(TexturePaintExportBitDepth bitDepth)
        {
            TexturePaintExportTemplate template = Own(ScriptableObject.CreateInstance<TexturePaintExportTemplate>());
            template.outputFolder = Folder + "/Export";
            template.filenamePattern = "roundtrip_{channel}_{resolution}";
            template.scope = TexturePaintExportScope.AllMaterials;
            template.overwritePolicy = TexturePaintOverwritePolicy.Overwrite;
            template.resolution = 32;
            template.bitDepth = bitDepth;
            template.padding = 0;
            template.exportLogicalChannels = true;
            template.exportMaterialPacking = false;
            template.createOrUpdateOverlay = false;
            template.createMaterialOverride = false;
            template.updateRecipeReferences = false;
            return template;
        }

        private void ConfigureExportDescriptor(TextureSet set, TexturePaintExportBitDepth bitDepth,
            UMAMaterial.TextureChannelLayout layout)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = Own(new Material(shader) { name = "Release Export Material" });
            Texture2D source = CreateSolidTexture(Color.black);
            material.SetTexture("_MetallicGlossMap", source);
            UMAMaterial uma = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            uma.name = "Release Export UMA Material";
            uma.material = material;
            uma.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.Texture,
                    textureFormat = RenderTextureFormat.ARGBHalf,
                    materialPropertyName = "_MetallicGlossMap",
                    sourceTextureName = "MaskMap",
                    DownSample = 1,
                    textureChannelLayout = layout,
                    textureChannelOutput = new UMAMaterial.TextureChannelOutputSettings
                    {
                        mode = UMAMaterial.TextureChannelOutputMode.Custom,
                        encoding = bitDepth == TexturePaintExportBitDepth.HalfFloat
                            ? UMAMaterial.TextureChannelOutputEncoding.ExrHalf
                            : bitDepth == TexturePaintExportBitDepth.Sixteen
                                ? UMAMaterial.TextureChannelOutputEncoding.Png16
                                : UMAMaterial.TextureChannelOutputEncoding.Png8,
                        importerType = UMAMaterial.TextureChannelImporterType.Default,
                        colorSpace = UMAMaterial.TextureChannelColorSpace.Linear,
                        alphaSource = UMAMaterial.TextureChannelAlphaSource.FromInput,
                        compression = UMAMaterial.TextureChannelImportCompression.Uncompressed,
                        normalConvention = UMAMaterial.TextureChannelNormalConvention.OpenGL,
                        generateMipMaps = false,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 1,
                        maxTextureSize = 8192,
                        platformOverrides = System.Array.Empty<UMAMaterial.TextureChannelPlatformOverrideSettings>()
                    }
                }
            };
            set.umaMaterial = uma;
            set.surface.sourceMaterial = material;
            set.surface.previewMaterial = material;
            foreach (TextureChannelTarget target in set.channels.Values)
            {
                target.materialProperty = "_MetallicGlossMap";
                target.umaChannelIndex = 0;
                target.sourceTexture = source;
            }
            set.materialCapability = TexturePaintMaterialCapabilityService.Compile(uma, material,
                new Texture[] { source }, true);
        }

        private void ConfigureNormalExportDescriptor(TextureSet set, Texture source)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Material material = Own(new Material(shader) { name = "Release Normal Export Material" });
            material.SetTexture("_BumpMap", source);
            UMAMaterial uma = Own(ScriptableObject.CreateInstance<UMAMaterial>());
            uma.name = "Release Normal Export UMA Material";
            uma.material = material;
            uma.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.NormalMap,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_BumpMap",
                    sourceTextureName = "BumpMap",
                    DownSample = 1,
                    textureChannelLayout = new UMAMaterial.TextureChannelLayout
                    {
                        mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                        red = UMAMaterial.TextureChannelUsage.Normal,
                        green = UMAMaterial.TextureChannelUsage.Normal,
                        blue = UMAMaterial.TextureChannelUsage.Normal,
                        alpha = UMAMaterial.TextureChannelUsage.Unused
                    },
                    textureChannelOutput = new UMAMaterial.TextureChannelOutputSettings
                    {
                        mode = UMAMaterial.TextureChannelOutputMode.Custom,
                        encoding = UMAMaterial.TextureChannelOutputEncoding.Png8,
                        importerType = UMAMaterial.TextureChannelImporterType.NormalMap,
                        colorSpace = UMAMaterial.TextureChannelColorSpace.Linear,
                        alphaSource = UMAMaterial.TextureChannelAlphaSource.None,
                        compression = UMAMaterial.TextureChannelImportCompression.Uncompressed,
                        normalConvention = UMAMaterial.TextureChannelNormalConvention.OpenGL,
                        generateMipMaps = false,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 1,
                        maxTextureSize = 8192,
                        platformOverrides = System.Array.Empty<UMAMaterial.TextureChannelPlatformOverrideSettings>()
                    }
                }
            };
            set.surface = new ReconstructedSurface
            {
                index = 0,
                sourceMaterial = material,
                previewMaterial = material,
                umaMaterial = uma,
                slotName = "Body",
                slotNames = new List<string> { "Body" }
            };
            set.umaMaterial = uma;
            set.previewMaterial = material;
            TextureChannelTarget target = set.GetChannel(TexturePaintChannel.Normal);
            target.materialProperty = "_BumpMap";
            target.umaChannelIndex = 0;
            set.materialCapability = TexturePaintMaterialCapabilityService.Compile(uma, material,
                new[] { source }, true);
        }

        private TextureSet CreateSet(TexturePaintChannel channel, Color clear, Material material = null, Mesh mesh = null)
        {
            TextureSet set = new TextureSet
            {
                persistentId = "release-surface",
                surface = new ReconstructedSurface
                {
                    index = 0,
                    rendererIndex = 0,
                    sourceSubmeshIndex = 0,
                    sourceMaterial = material,
                    mesh = mesh,
                    slotName = "Body",
                    slotNames = new List<string> { "Body" },
                    triangleSlotNames = new[] { "Body", "Body" },
                    triangleIslands = new[] { 0, 0 }
                }
            };
            AddChannel(set, channel, clear);
            return set;
        }

        private static void AddChannel(TextureSet set, TexturePaintChannel channel, Color clear)
        {
            EditableTextureTarget editable = new EditableTextureTarget("Texture Paint Release " + channel,
                16, 16, RenderTextureFormat.ARGBHalf, null, clear);
            set.channels.Add(channel, new TextureChannelTarget
            {
                channel = channel,
                materialProperty = "_" + channel,
                sourceKeyword = channel.ToString(),
                format = RenderTextureFormat.ARGBHalf,
                sRGB = false,
                editable = editable
            });
        }

        private TextureStore CreateStore(TextureSet set)
        {
            TextureStore store = new TextureStore();
            FieldInfo setsField = typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic);
            ((List<TextureSet>)setsField.GetValue(store)).Add(set);
            ownedStores.Add(store);
            return store;
        }

        private static void AddSet(TextureStore store, TextureSet set)
        {
            FieldInfo setsField = typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic);
            ((List<TextureSet>)setsField.GetValue(store)).Add(set);
        }

        private static void ConfigureSlot(TextureSet set, string slot, int surfaceIndex)
        {
            set.surface.index = surfaceIndex;
            set.surface.slotName = slot;
            set.surface.slotNames = new List<string> { slot };
            set.surface.triangleSlotNames = new[] { slot, slot };
        }

        private static RenderTexture CreateRenderTexture(string name, int size, RenderTextureFormat format)
        {
            RenderTexture texture = new RenderTexture(new RenderTextureDescriptor(size, size, format, 0)
            {
                enableRandomWrite = true,
                sRGB = false
            }) { name = name, hideFlags = HideFlags.HideAndDontSave };
            texture.Create();
            return texture;
        }

        private static Color ReadTextureCenter(Texture texture)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, temporary);
            Color result = ReadCenter(temporary);
            RenderTexture.ReleaseTemporary(temporary);
            return result;
        }

        private static Color ReadCenter(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            int x = texture.width / 2, y = texture.height / 2;
            readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            readback.Apply(false, false);
            Color result = readback.GetPixel(0, 0);
            Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return result;
        }

        private static Color ReadPixel(RenderTexture texture, int x, int y)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            readback.Apply(false, false);
            Color result = readback.GetPixel(0, 0);
            Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return result;
        }

        private static void AssertColor(Color actual, Color expected, float tolerance)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), "red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), "green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), "blue");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), "alpha");
        }

        private static Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Texture Paint Persistence Quad",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[]
                {
                    new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1),
                    new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1)
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<StrokeSample> CreateVerticalRibbonSamples()
        {
            return new List<StrokeSample>
            {
                new StrokeSample(new Vector3(0.5f, 0f, 0f), Vector3.forward,
                    new Vector2(0.5f, 0f), 0, 0)
                    { color = Color.green, hasColor = true, pressure = 1f, flowMultiplier = 1f },
                new StrokeSample(new Vector3(0.5f, 1f, 0f), Vector3.forward,
                    new Vector2(0.5f, 1f), 0, 1)
                    { color = Color.green, hasColor = true, pressure = 1f, flowMultiplier = 1f }
            };
        }

        private static int CountOccupiedColumnClusters(IReadOnlyList<int> hits, int minimumHits)
        {
            int clusters = 0;
            bool occupied = false;
            for (int i = 0; i < hits.Count; i++)
            {
                bool next = hits[i] >= minimumHits;
                if (next && !occupied) clusters++;
                occupied = next;
            }
            return clusters;
        }

        private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> polyline)
        {
            float distance = float.PositiveInfinity;
            for (int i = 1; i < polyline.Count; i++)
            {
                Vector2 from = polyline[i - 1];
                Vector2 delta = polyline[i] - from;
                float lengthSquared = delta.sqrMagnitude;
                float t = lengthSquared > 0.000001f
                    ? Mathf.Clamp01(Vector2.Dot(point - from, delta) / lengthSquared) : 0f;
                distance = Mathf.Min(distance, Vector2.Distance(point, from + delta * t));
            }
            return distance;
        }

        private Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private Texture2D CreateSplitTexture(Color first, Color second, bool splitAlongX)
        {
            Texture2D texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16];
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                pixels[y * 4 + x] = (splitAlongX ? x : y) < 2 ? first : second;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static float RgbDistance(Color a, Color b)
        {
            return Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b));
        }

        private sealed class BelowCompositeEchoPlugin : ITexturePaintFilterV2
        {
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id = "com.uma.tests.below-composite-echo",
                displayName = "Below Composite Echo",
                capabilities = TexturePaintPluginCapability.Filter,
                declaredChannels = TexturePaintChannelMask.Albedo,
                readChannels = TexturePaintChannelMask.Albedo
            };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                for (int i = 0; i < context.source.surfaceIds.Count; i++)
                {
                    string surfaceId = context.source.surfaceIds[i];
                    TexturePaintReadOnlyImage source = context.source.Get(surfaceId,
                        TexturePaintChannel.Albedo);
                    if (source == null) continue;
                    context.WriteTile(surfaceId, TexturePaintChannel.Albedo,
                        new RectInt(0, 0, source.width, source.height), source.CopyPixels(),
                        TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                }
                return Task.CompletedTask;
            }
        }

        private sealed class CompactSolidPlugin : ITexturePaintGeneratorV2
        {
            public TexturePaintPluginDescriptor Descriptor { get; } =
                new TexturePaintPluginDescriptor
                {
                    id = "com.uma.tests.compact-gpu-commit",
                    displayName = "Compact GPU Commit",
                    capabilities = TexturePaintPluginCapability.Generator,
                    declaredChannels = TexturePaintChannelMask.Albedo
                };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                for (int i = 0; i < context.source.surfaceIds.Count; i++)
                {
                    string surfaceId = context.source.surfaceIds[i];
                    TexturePaintReadOnlyChannelInfo info = context.source.GetChannelInfo(surfaceId,
                        TexturePaintChannel.Albedo);
                    if (info == null) continue;
                    var pixels = new Color32[info.width * info.height];
                    for (int pixel = 0; pixel < pixels.Length; pixel++)
                        pixels[pixel] = new Color32(255, 0, 0, 255);
                    context.WriteTileCompact(surfaceId, TexturePaintChannel.Albedo,
                        new RectInt(0, 0, info.width, info.height), pixels,
                        TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                }
                return Task.CompletedTask;
            }
        }

        private sealed class GpuDirtProbePlugin : ITexturePaintGeneratorV2,
            ITexturePaintGpuGeneratorV2
        {
            public bool cpuFallbackInvoked;
            public string GpuKernelName => "CSDirtify";
            public TexturePaintPluginDescriptor Descriptor { get; } =
                new TexturePaintPluginDescriptor
                {
                    id = "com.uma.tests.gpu-dirt-probe",
                    displayName = "GPU Dirt Probe",
                    capabilities = TexturePaintPluginCapability.Generator |
                                   TexturePaintPluginCapability.ReadsMeshMaps |
                                   TexturePaintPluginCapability.GpuAccelerated,
                    declaredChannels = TexturePaintChannelMask.Albedo,
                    readChannels = TexturePaintChannelMask.AmbientOcclusion,
                    requiredMeshMaps = TexturePaintMeshMapMask.WorldPosition |
                                       TexturePaintMeshMapMask.WorldNormal |
                                       TexturePaintMeshMapMask.SignedCurvature |
                                       TexturePaintMeshMapMask.AmbientOcclusion |
                                       TexturePaintMeshMapMask.SurfaceId,
                    parameters = new List<TexturePaintPluginParameterDefinition>
                    {
                        Number("projection", 1f, TexturePaintPluginParameterType.Integer),
                        Number("textureScale", 1f), Number("seed", 1f,
                            TexturePaintPluginParameterType.Integer),
                        Number("normalCurvature", 0f), Number("featureSize", 0f),
                        Number("detectionLevel", 0f), Number("spread", 0f),
                        Number("amount", 1f), Number("cavityInfluence", 1f),
                        Number("breakup", 0f), Number("breakupScale", 1f),
                        Number("fractalLevels", 1f, TexturePaintPluginParameterType.Integer),
                        Number("fractalPersistence", 0.5f), Number("fractalEdge", 0f),
                        new TexturePaintPluginParameterDefinition
                        {
                            id = "surfaceColor", displayName = "Surface Color",
                            type = TexturePaintPluginParameterType.Color,
                            defaultColor = Color.red
                        },
                        Number("roughness", 0.8f), Number("ambientOcclusion", 0.4f),
                        Number("normalAmount", 0.1f)
                    }
                };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                cpuFallbackInvoked = true;
                return Task.CompletedTask;
            }

            private static TexturePaintPluginParameterDefinition Number(string id,
                float value, TexturePaintPluginParameterType type =
                    TexturePaintPluginParameterType.Float) => new()
                {
                    id = id, displayName = id, type = type, minimum = -100000f,
                    maximum = 100000f, defaultNumber = value
                };
        }

        private T Own<T>(T value) where T : Object
        {
            ownedObjects.Add(value);
            return value;
        }

        private static void RestoreAssetBytes(string assetPath, byte[] bytes)
        {
            string fullPath = Path.GetFullPath(assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int attempt = 0; ; attempt++)
            {
                AssetDatabase.ReleaseCachedFileHandles();
                try
                {
                    File.WriteAllBytes(fullPath, bytes);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
