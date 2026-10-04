#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPluginPreviewTests
    {
        private TextureStore store;
        private TextureSet set;
        private TexturePaintLayer layer;
        private Mesh mesh;

        [SetUp]
        public void SetUp()
        {
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up }, triangles = new[] { 0, 1, 2 } };
            set = new TextureSet { persistentId = "preview-test", surface = new ReconstructedSurface
                { mesh = mesh, triangleIslands = new[] { 0 } } };
            foreach (var channel in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness,
                TexturePaintChannel.NormalControl })
                set.channels.Add(channel, new TextureChannelTarget { channel = channel,
                    editable = new EditableTextureTarget("Preview test", 256, 64,
                        RenderTextureFormat.ARGB32, null, Color.black) });
            layer = new TexturePaintLayer { kind = TexturePaintLayerKind.Plugin };
            set.layers.Add(layer);
            store = new TextureStore();
            ((List<TextureSet>)typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(store)).Add(set);
        }

        [TearDown]
        public void TearDown() { store?.Dispose(); UnityEngine.Object.DestroyImmediate(mesh); }

        private Task<Dictionary<TexturePaintChannel, Color[]>> Generate(ITexturePaintCommandExtensionV2 plugin,
            TexturePaintPluginParameterSet values = null, bool mask = false, CancellationToken token = default)
            => TexturePaintPluginPreview.GenerateAsync(store, set, layer, plugin,
                values ?? new TexturePaintPluginParameterSet(), mask, token);

        [Test]
        public async Task DraftUses128DimensionsAndCurrentParametersWithoutCommitting()
        {
            var plugin = new DraftPlugin();
            var values = new TexturePaintPluginParameterSet(); values.Get("color", true).color = Color.red;
            var revision = set.channels[TexturePaintChannel.Albedo].editable.Revision;
            string before = JsonUtility.ToJson(values);
            var first = await Generate(plugin, values);
            values.Get("color").color = Color.blue;
            var second = await Generate(plugin, values);
            Assert.That(first[TexturePaintChannel.Albedo], Has.Length.EqualTo(128 * 128));
            Assert.That(first[TexturePaintChannel.Albedo][0], Is.EqualTo(Color.red));
            Assert.That(second[TexturePaintChannel.Albedo][0], Is.EqualTo(Color.blue));
            Assert.That(set.layers, Has.Count.EqualTo(1));
            Assert.That(layer.channels, Is.Empty);
            Assert.That(set.channels[TexturePaintChannel.Albedo].editable.Revision, Is.EqualTo(revision));
            values.Get("color").color = Color.red;
            Assert.That(JsonUtility.ToJson(values), Is.EqualTo(before), "Plugin edits must affect only its cloned parameters.");
        }

        [Test]
        public async Task RectangularMaskFillsSquareDraftAndRemainsUntouched()
        {
            layer.layerMask = new TexturePaintLayerMask { target = new EditableTextureTarget("Mask test",
                256, 64, RenderTextureFormat.ARGB32, null, Color.white) };
            var revision = layer.layerMask.target.Revision;
            var result = await Generate(new DraftPlugin { maskOpacity = .5f }, mask: true);
            Assert.That(result[TexturePaintChannel.Custom][128 * 128 - 1].r, Is.EqualTo(.7f).Within(.001f));
            Assert.That(result[TexturePaintChannel.Custom][0].a, Is.EqualTo(1));
            Assert.That(layer.layerMask.target.Revision, Is.EqualTo(revision));
            Assert.That(layer.layerMask.target.Width, Is.EqualTo(256));
        }

        [TestCase(0)] [TestCase(1)]
        public async Task RealClothSupportsFlatAndTriplanarWithoutRepairingTheLiveMesh(int projection)
        {
            Type pluginType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                pluginType ??= assembly.GetType("UMA.TexturePaint.Examples.ClothTextureGeneratorPlugin");
            Assert.That(pluginType, Is.Not.Null);
            var asset = ScriptableObject.CreateInstance(pluginType);
            try
            {
                var values = new TexturePaintPluginParameterSet(); values.Get("projection", true).number = projection;
                var result = await Generate((ITexturePaintCommandExtensionV2)asset, values);
                Assert.That(result.ContainsKey(TexturePaintChannel.Albedo), Is.True);
                Assert.That(Array.Exists(result[TexturePaintChannel.Albedo], p => p.a > .1f), Is.True);
                Assert.That(mesh.normals, Is.Empty, "Draft mesh maps must not repair the original mesh.");
                Assert.That(layer.channels, Is.Empty);
                Assert.That(values.values, Has.Count.EqualTo(1), "Hydration must use a clone.");
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public async Task CancellationAfterPluginCompletionDiscardsItsOutput()
        {
            using var cancellation = new CancellationTokenSource();
            var plugin = new DraftPlugin { completed = cancellation.Cancel };
            try { await Generate(plugin, token: cancellation.Token); Assert.Fail("Cancelled output was returned."); }
            catch (OperationCanceledException) { }
            Assert.That(layer.channels, Is.Empty);
        }

        [Test]
        public async Task FilterReadsSquareSnapshotOfRectangularInput()
        {
            var result = await Generate(new InputFilter());
            Assert.That(result[TexturePaintChannel.Albedo][128 * 128 - 1], Is.EqualTo(Color.black));
            Assert.That(layer.channels, Is.Empty);
        }

        private sealed class InputFilter : ITexturePaintFilterV2
        {
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor {
                id = "com.uma.tests.preview-filter", displayName = "Draft filter",
                capabilities = TexturePaintPluginCapability.Filter, declaredChannels = TexturePaintChannelMask.Albedo };
            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                var source = context.source.Get("preview-test", TexturePaintChannel.Albedo);
                Assert.That(source.width, Is.EqualTo(128)); Assert.That(source.height, Is.EqualTo(128));
                var pixels = new Color[source.width * source.height];
                for (int y = 0; y < source.height; y++)
                for (int x = 0; x < source.width; x++) pixels[y * source.width + x] = source.GetPixel(x, y);
                context.WriteTile("preview-test", TexturePaintChannel.Albedo,
                    new RectInt(0, 0, source.width, source.height), pixels,
                    TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                return Task.CompletedTask;
            }
        }

        [Test]
        public void NormalTilesNormalizeVectorsAndMaskTilesUseScalarCoverage()
        {
            var command = new TexturePaintPluginTileCommand { surfaceId = "s", channel = TexturePaintChannel.Normal,
                rect = new RectInt(0, 0, 1, 1), opacity = 1, pixels = new[] { new Color(1, 1, 1, .5f) } };
            Color normal = TexturePaintPluginPreview.Rasterize(new[] { command }, "s", false, default)
                [TexturePaintChannel.Normal][0];
            Assert.That(new Vector3(normal.r * 2 - 1, normal.g * 2 - 1, normal.b * 2 - 1).magnitude,
                Is.EqualTo(1).Within(.001f));
            command.target = TexturePaintPluginTarget.LayerMask; command.opacity = .5f;
            command.pixels[0] = new Color(.4f, .4f, .4f, 0);
            Color mask = TexturePaintPluginPreview.Rasterize(new[] { command }, "s", true, default)
                [TexturePaintChannel.Normal][0];
            Assert.That(mask.r, Is.EqualTo(.2f).Within(.001f)); Assert.That(mask.a, Is.EqualTo(1));
        }

        private sealed class DraftPlugin : ITexturePaintGeneratorV2, ITexturePaintDynamicChannelUsageV2
        {
            public Action completed;
            public float maskOpacity = 1;
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor {
                id = "com.uma.tests.preview", displayName = "Draft", capabilities = TexturePaintPluginCapability.Generator,
                declaredChannels = TexturePaintChannelMask.Albedo, supportedTargets = TexturePaintPluginTarget.All };
            public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) => TexturePaintChannelMask.None;
            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                var mask = context.source.GetMask("preview-test");
                var info = context.source.GetChannelInfo("preview-test", TexturePaintChannel.Albedo);
                int width = mask?.width ?? info.width, height = mask?.height ?? info.height;
                Assert.That(width, Is.EqualTo(128)); Assert.That(height, Is.EqualTo(128));
                var pixels = new Color[width * height];
                Color color = mask != null ? new Color(.4f, .4f, .4f, 0) : context.parameters.Get("color")?.color ?? Color.white;
                for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
                if (mask != null) context.WriteMaskTile("preview-test", new RectInt(0, 0, width, height), pixels,
                    opacity: maskOpacity);
                else context.WriteTile("preview-test", TexturePaintChannel.Albedo, new RectInt(0, 0, width, height),
                    pixels, TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                context.parameters.Get("private", true).number = 123;
                completed?.Invoke();
                return Task.CompletedTask;
            }
        }
    }
}
#endif
