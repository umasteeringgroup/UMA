#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintColorCorrectnessTests
    {
        private static readonly Color Picked = new Color(.5f, .3f, .1f, .65f);
        // IEC sRGB transfer reference values, independent of the rendering implementation.
        private static readonly Color Linear = new Color(.21404114f, .07323896f, .01002283f, .65f);
        private static ScriptableObject Plugin(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UMA.TexturePaint.Examples." + name)).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, name);
            return ScriptableObject.CreateInstance(type);
        }
        private static TextureStore Store(TextureSet set)
        {
            var store = new TextureStore();
            ((List<TextureSet>)typeof(TextureStore).GetField("sets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(store)).Add(set);
            return store;
        }
        private static void AssertRgb(Color actual, Color expected, float tolerance = .003f)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), "red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), "green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), "blue");
        }

        [Test]
        public void AuthoredColorsDecodeWithoutChangingParametersOrAlpha()
        {
            var p = new TexturePaintPluginParameterSet(); p.Get("color", true).color = Picked;
            string original = JsonUtility.ToJson(p);
            Color rendered = p.LinearColor("color", Color.white);
            AssertRgb(rendered, Linear, .00001f);
            Assert.That(rendered.a, Is.EqualTo(.65f));
            AssertRgb(p.LinearColor("missing", Picked), Linear, .00001f);
            Assert.That(p.Color("color", Color.white), Is.EqualTo(Picked));
            Assert.That(JsonUtility.ToJson(p), Is.EqualTo(original));
        }

        [TestCase(TexturePaintChannel.Albedo)] [TestCase(TexturePaintChannel.Emission)]
        [TestCase(TexturePaintChannel.SkinColorMask)]
        public void ColorChannelsDecodePickerRgb(TexturePaintChannel channel)
        { AssertRgb(TexturePaintChannelUtility.WorkingColor(channel, Picked), Linear, .00001f); }

        [TestCase(TexturePaintChannel.Normal)] [TestCase(TexturePaintChannel.NormalControl)]
        [TestCase(TexturePaintChannel.Roughness)] [TestCase(TexturePaintChannel.Metallic)]
        [TestCase(TexturePaintChannel.AmbientOcclusion)] [TestCase(TexturePaintChannel.Thickness)]
        [TestCase(TexturePaintChannel.DetailMask)] [TestCase(TexturePaintChannel.Custom)]
        public void NumericChannelsKeepMidpointAndAlpha(TexturePaintChannel channel)
        {
            var midpoint = new Color(.5f, .5f, .5f, .65f);
            Assert.That(TexturePaintChannelUtility.WorkingColor(channel, midpoint), Is.EqualTo(midpoint));
        }

        [TestCase("ClothTextureGeneratorPlugin")] [TestCase("TextileSurfaceGeneratorPlugin")]
        [TestCase("NoiseGeneratorPlugin")]
        public async Task NeutralGeneratorOutputMatchesPickerAndExportsToSrgb(string pluginName)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear, TexturePaintChannel.Albedo, size: 64);
            using var store = Store(fixture.set); using var host = new PluginHost();
            var asset = Plugin(pluginName);
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = host.CreateParameters(plugin);
                foreach (var definition in plugin.Descriptor.parameters)
                    if (definition.type == TexturePaintPluginParameterType.Color) p.Get(definition.id).color = Picked;
                foreach (string id in new[] { "fiberColorVariation", "wearAmount", "shadingStrength", "colorVariation", "colorAmount", "aoStrength" }) p.Get(id, true).number = 0;
                p.Get("mode", true).number = 0;
                string original = JsonUtility.ToJson(p);
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                var pixels = TexturePaintGpuTestFixture.ReadPixels(fixture.set.layers[0].channels[TexturePaintChannel.Albedo].Front);
                foreach (Color pixel in pixels.Where(c => c.a > .1f)) AssertRgb(pixel, Linear);
                Assert.That(pixels.Count(c => c.a > .1f), Is.GreaterThan(100));
                Assert.That(JsonUtility.ToJson(p), Is.EqualTo(original));
                // Bake the actual generated working target to PNG, then inspect encoded bytes.
                Texture2D encoded = TexturePaintBaker.BakeRenderTexture(fixture.set.layers[0].channels[TexturePaintChannel.Albedo].Front,
                    "Color correctness", 64, TexturePaintExportBitDepth.Eight, false);
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                try
                {
                    Assert.That(decoded.LoadImage(encoded.EncodeToPNG()), Is.True);
                    AssertRgb(decoded.GetPixel(20, 20), Picked, .018f);
                }
                finally { UnityEngine.Object.DestroyImmediate(encoded); UnityEngine.Object.DestroyImmediate(decoded); }
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [TestCase("DirtifyGeneratorPlugin")] [TestCase("EdgeWearGeneratorPlugin")]
        [TestCase("DrippingCorrosionGeneratorPlugin")]
        public async Task CpuAndGpuGeneratorsRenderTheSameLinearPigment(string pluginName)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear, TexturePaintChannel.Albedo, size: 32);
            bool convex = pluginName == "EdgeWearGeneratorPlugin";
            float slope = convex ? .5f : -.5f;
            fixture.mesh.normals = fixture.mesh.vertices.Select(v => (Vector3.forward + new Vector3(v.x * slope, v.y * slope, 0)).normalized).ToArray();
            using var store = Store(fixture.set); using var host = new PluginHost();
            var asset = Plugin(pluginName);
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = host.CreateParameters(plugin);
                foreach (var definition in plugin.Descriptor.parameters)
                    if (definition.type == TexturePaintPluginParameterType.Color) p.Get(definition.id).color = Picked;
                p.Get("detectionLevel", true).number = 0;
                p.Get("breakup", true).number = 0;
                p.Get("normalCurvature", true).number = 0;
                p.Get("cavityInfluence", true).number = 0;
                p.Get("dripAmount", true).number = 0;
                string original = JsonUtility.ToJson(p);
                foreach (bool gpu in new[] { false, true })
                {
                    host.GpuGeneratorShader = gpu ? TexturePaintGpuTestFixture.LoadShader("PluginGenerators.compute") : null;
                    await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                    var pixels = TexturePaintGpuTestFixture.ReadPixels(fixture.set.layers.Last().channels[TexturePaintChannel.Albedo].Front);
                    Assert.That(pixels.Count(c => c.a > .01f), Is.GreaterThan(10), gpu ? "GPU coverage" : "CPU coverage");
                    foreach (Color pixel in pixels.Where(c => c.a > .05f)) AssertRgb(pixel, Linear, .004f);
                    Assert.That(host.Undo(), Is.True);
                }
                Assert.That(JsonUtility.ToJson(p), Is.EqualTo(original));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [TestCase(TexturePaintChannel.Albedo, false)] [TestCase(TexturePaintChannel.Roughness, false)]
        [TestCase(TexturePaintChannel.Albedo, true)]
        public async Task FilterPaletteAndGradientRespectColorAndDataChannels(TexturePaintChannel channel, bool mask)
        {
            using var fixture = new TexturePaintGpuTestFixture(new Color(.5f, .5f, .5f, 1), channel, size: 32);
            using var store = Store(fixture.set); var layer = fixture.set.AddPluginLayer("Color filter");
            layer.layerMask = new TexturePaintLayerMask { target = new EditableTextureTarget("Mask", 32, 32, RenderTextureFormat.ARGB32, null, Color.white) };
            foreach (string name in new[] { "ChannelOperationsFilterPlugin", "StylizationFilterPlugin" })
            {
                var asset = Plugin(name);
                try
                {
                    var plugin = (ITexturePaintCommandExtensionV2)asset;
                    var p = new TexturePaintPluginParameterSet(); p.EnsureDefaults(plugin.Descriptor);
                    p.Get("sourceChannel").number = (int)channel; p.Get("destinationChannel").number = (int)channel;
                    p.Get("operation").number = name == "ChannelOperationsFilterPlugin" ? 4 : 3;
                    foreach (var definition in plugin.Descriptor.parameters)
                        if (definition.type == TexturePaintPluginParameterType.Color) p.Get(definition.id).color = new Color(.5f, .5f, .5f, 1);
                    string original = JsonUtility.ToJson(p);
                    var result = await TexturePaintPluginPreview.GenerateAsync(store, fixture.set, layer, plugin, p, mask, default);
                    Color pixel = result.Values.First()[0];
                    float expected = !mask && TexturePaintChannelUtility.IsColor(channel) ? .21404114f : .5f;
                    Assert.That(pixel.r, Is.EqualTo(expected).Within(.004f), name);
                    Assert.That(JsonUtility.ToJson(p), Is.EqualTo(original));
                }
                finally { UnityEngine.Object.DestroyImmediate(asset); }
            }
        }

        private static IEnumerable<TexturePaintGarmentPreset> Garments() => Enum.GetValues(typeof(TexturePaintGarmentPreset)).Cast<TexturePaintGarmentPreset>();
        private static IEnumerable<TexturePaintSeamPreset> Hems() => Enum.GetValues(typeof(TexturePaintSeamPreset)).Cast<TexturePaintSeamPreset>();
        private static void AssertTransfer(Color[] black, Color[] white, Color[] gray, bool expectColor = true)
        {
            int affected = 0;
            for (int i = 0; i < gray.Length; i++)
            {
                if (gray[i].a < .02f || Math.Abs(white[i].r - black[i].r) < .01f) continue;
                AssertRgb(gray[i], black[i] + (white[i] - black[i]) * .21404114f, .001f);
                Assert.That(gray[i].a, Is.EqualTo(white[i].a).Within(.00001f)); affected++;
            }
            if (expectColor) Assert.That(affected, Is.GreaterThan(0), "No colored material was exercised.");
            else
            {
                Assert.That(affected, Is.Zero);
                Assert.That(gray, Is.EqualTo(black), "Relief-only output must not introduce a pigment tint.");
                Assert.That(white, Is.EqualTo(black));
            }
        }

        [TestCaseSource(nameof(Garments))]
        public void EveryGarmentUsesLinearPickerColors(TexturePaintGarmentPreset preset)
        {
            var settings = TexturePaintGarmentSettings.Create(preset); settings.wear = settings.dirt = 0; settings.colorAmount = 1;
            Color[] Render(Color color)
            {
                settings.threadColor = settings.surfaceColor = settings.accentColor = color;
                string original = JsonUtility.ToJson(settings);
                var output = TexturePaintGarmentPreview.Generate(settings, null, new Vector2(.06f, .18f));
                Assert.That(JsonUtility.ToJson(settings), Is.EqualTo(original));
                return output[TexturePaintChannel.Albedo];
            }
            bool colored = settings.kind != TexturePaintGarmentKind.Wrinkles && settings.kind != TexturePaintGarmentKind.Wear && preset != TexturePaintGarmentPreset.Abrasion;
            AssertTransfer(Render(Color.black), Render(Color.white), Render(new Color(.5f, .5f, .5f, 1)), colored);
        }

        [TestCaseSource(nameof(Hems))]
        public void EveryHemUsesLinearThreadColors(TexturePaintSeamPreset preset)
        {
            var settings = TexturePaintHemSeamSettings.Create(preset); settings.wear = 0;
            if (settings.rows.Count == 0) settings.rows.Add(new TexturePaintSeamStitchRow());
            Color[] Render(Color color)
            {
                foreach (var row in settings.rows) row.color = color;
                string original = JsonUtility.ToJson(settings);
                var output = TexturePaintGarmentPreview.Generate(null, settings, new Vector2(.025f, .075f));
                Assert.That(JsonUtility.ToJson(settings), Is.EqualTo(original));
                return output[TexturePaintChannel.Albedo];
            }
            AssertTransfer(Render(Color.black), Render(Color.white), Render(new Color(.5f, .5f, .5f, 1)));
        }

        [TestCase(TexturePaintChannel.Albedo)] [TestCase(TexturePaintChannel.Roughness)]
        public void DraftTextureStoresDisplayEncodedRgbAndPreservesAlpha(TexturePaintChannel channel)
        {
            bool color = TexturePaintChannelUtility.IsColor(channel);
            Color[] pixels = Enumerable.Repeat(color ? Linear : Picked, 128 * 128).ToArray();
            using var display = new TexturePaintPreviewDisplay(); display.Set(new Dictionary<TexturePaintChannel, Color[]> { [channel] = pixels });
            AssertRgb(display.Texture.GetPixel(64, 64), Picked);
            Assert.That(display.Texture.GetPixel(64, 64).a, Is.EqualTo(.65f).Within(.003f));
            Assert.That(pixels[0], Is.EqualTo(color ? Linear : Picked), "Display must not mutate working pixels.");
        }

        [TestCase(TexturePaintChannel.Albedo)] [TestCase(TexturePaintChannel.Roughness)]
        public void BrushGpuAndDraftDecodeColorsOnlyForRgb(TexturePaintChannel channel)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear, channel, size: 64);
            using var engine = TexturePaintGpuTestFixture.CreateEngine(); var brush = fixture.CreateBrush(1, 1); brush.size = .15f;
            var asset = Plugin("ExampleBrushPlugin");
            try
            {
                var color = new Color(.5f, .5f, .5f, 1); float expected = channel == TexturePaintChannel.Albedo ? .21404114f : .5f;
                Assert.That(engine.BeginStroke(fixture.CreateContext(brush, TexturePaintTool.Paint, color, channel, 1), TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), .15f), Is.True); engine.EndStroke(true);
                Color[] rendered = TexturePaintGpuTestFixture.ReadPixels(fixture.set.layers.Last().channels[channel].Front); Assert.That(rendered.Any(p => p.a > .5f), Is.True);
                foreach (var pixel in rendered.Where(p => p.a > .5f)) Assert.That(pixel.r, Is.EqualTo(expected).Within(.001f));
                var values = new TexturePaintPluginParameterSet(); var plugin = (ITexturePaintBrushV2)asset; values.EnsureDefaults(plugin.Descriptor);
                var draft = TexturePaintBrushPreview.Generate(plugin, values, brush, channel, color)[channel];
                Assert.That(draft.Any(p => p.a > .1f), Is.True);
                foreach (var pixel in draft.Where(p => p.a > .1f)) Assert.That(pixel.r, Is.EqualTo(expected).Within(.001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(brush); UnityEngine.Object.DestroyImmediate(asset); }
        }
    }
}
#endif
