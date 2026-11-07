#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPreviewRenderingTests
    {
        private static IEnumerable<TestCaseData> Garments()
        {
            foreach (TexturePaintGarmentPreset preset in Enum.GetValues(typeof(TexturePaintGarmentPreset)))
                yield return new TestCaseData(preset);
        }
        private static IEnumerable<TestCaseData> Hems()
        {
            foreach (TexturePaintSeamPreset preset in Enum.GetValues(typeof(TexturePaintSeamPreset)))
                yield return new TestCaseData(preset);
        }

        [TestCaseSource(nameof(Garments))]
        public void GarmentSamplesUseTheProductionShaderWithoutEditingSettings(TexturePaintGarmentPreset preset)
        {
            var settings = TexturePaintGarmentSettings.Create(preset);
            string before = JsonUtility.ToJson(settings);
            var pixels = TexturePaintGarmentPreview.Generate(settings, null, new Vector2(.06f, .18f));
            AssertPreview(pixels);
            Assert.That(JsonUtility.ToJson(settings), Is.EqualTo(before));
        }

        [TestCaseSource(nameof(Hems))]
        public void EveryHemConstructionRendersAVisibleFiniteSample(TexturePaintSeamPreset preset)
        {
            var settings = TexturePaintHemSeamSettings.Create(preset);
            string before = JsonUtility.ToJson(settings);
            var pixels = TexturePaintGarmentPreview.Generate(null, settings, new Vector2(.025f, .075f));
            AssertPreview(pixels);
            Assert.That(JsonUtility.ToJson(settings), Is.EqualTo(before));
        }

        private static void AssertPreview(Dictionary<TexturePaintChannel, Color[]> images)
        {
            var shader = Shader.Find("Hidden/UMA/TexturePaint/GarmentPreview");
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False,
                string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(message => message.message)));
            Assert.That(images, Is.Not.Empty);
            bool visible = false;
            foreach (var colors in images.Values)
            {
                Assert.That(colors, Has.Length.EqualTo(128 * 128));
                foreach (Color pixel in colors)
                {
                    Assert.That(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b) && float.IsFinite(pixel.a), Is.True);
                    visible |= pixel.a > .01f;
                }
            }
            Assert.That(visible, Is.True);
        }

        [Test]
        public void GarmentWidthOpenEndsAndReliefChangeTheSample()
        {
            var settings = TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.Waistband);
            var original = TexturePaintGarmentPreview.Generate(settings, null, new Vector2(.06f, .18f));
            settings.openStart = settings.openEnd = true;
            var open = TexturePaintGarmentPreview.Generate(settings, null, new Vector2(.06f, .18f));
            Assert.That(Difference(original[TexturePaintChannel.Albedo], open[TexturePaintChannel.Albedo]), Is.GreaterThan(.00001f));
            settings.depthStrength = 0;
            var flat = TexturePaintGarmentPreview.Generate(settings, null, new Vector2(.06f, .18f));
            Assert.That(Difference(open[TexturePaintChannel.NormalControl], flat[TexturePaintChannel.NormalControl]), Is.GreaterThan(.00001f));
            var wider = TexturePaintGarmentPreview.Generate(settings, null, new Vector2(.12f, .18f));
            Assert.That(Difference(flat[TexturePaintChannel.Albedo], wider[TexturePaintChannel.Albedo]), Is.GreaterThan(.00001f));
        }

        [Test]
        public void FilterComparisonUsesTheSameCoordinatesAndCompositesTransparentOutput()
        {
            var before = Image(TexturePaintChannel.Albedo, Color.red);
            var output = Image(TexturePaintChannel.Albedo, new Color(0, 0, 1, .5f));
            Color[] result = TexturePaintPreviewDisplay.ComposeView(output, before,
                TexturePaintChannel.Albedo, TexturePaintPreviewView.Split);
            Assert.That(result[32], Is.EqualTo(Color.red));
            Assert.That(result[96], Is.EqualTo(new Color(.5f, 0, .5f, 1)));
            Assert.That(before[TexturePaintChannel.Albedo][96], Is.EqualTo(Color.red));
        }

        [Test]
        public void LitViewRespondsToHeightNormalsRoughnessAndOcclusion()
        {
            var output = Image(TexturePaintChannel.Albedo, new Color(.4f, .3f, .2f, 1));
            var original = TexturePaintPreviewDisplay.Shade(output);
            output[TexturePaintChannel.NormalControl] = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++) output[TexturePaintChannel.NormalControl][y * 128 + x] =
                new Color(.5f + Mathf.Sin(x * .2f) * .03f, 0, 0, 1);
            Assert.That(Difference(original, TexturePaintPreviewDisplay.Shade(output)), Is.GreaterThan(.001f));
            output.Remove(TexturePaintChannel.NormalControl);
            output[TexturePaintChannel.Normal] = Image(TexturePaintChannel.Normal, new Color(.8f, .5f, .9f, 1))[TexturePaintChannel.Normal];
            Assert.That(Difference(original, TexturePaintPreviewDisplay.Shade(output)), Is.GreaterThan(.001f));
            output.Remove(TexturePaintChannel.Normal);
            output[TexturePaintChannel.Roughness] = Image(TexturePaintChannel.Roughness, Color.black)[TexturePaintChannel.Roughness];
            Assert.That(Difference(original, TexturePaintPreviewDisplay.Shade(output)), Is.GreaterThan(.0001f));
            output[TexturePaintChannel.AmbientOcclusion] = Image(TexturePaintChannel.AmbientOcclusion, Color.black)[TexturePaintChannel.AmbientOcclusion];
            var dark = TexturePaintPreviewDisplay.Shade(output);
            Assert.That(dark[0].maxColorComponent, Is.LessThan(original[0].maxColorComponent));
            Assert.That(dark.All(color => float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b)), Is.True);
        }

        [Test]
        public void DisplayDestroysTexturesOnReplacementAndDisposal()
        {
            var display = new TexturePaintPreviewDisplay();
            display.Set(Image(TexturePaintChannel.Albedo, Color.red));
            Texture2D previous = display.Texture;
            display.SelectView(TexturePaintPreviewView.LitSurface);
            Assert.That(previous == null, Is.True);
            Texture2D current = display.Texture;
            display.Dispose();
            Assert.That(current == null, Is.True);
        }

        [Test]
        public void BrushSampleHonorsSpacingAndKeepsTheOriginalPluginAndParametersUntouched()
        {
            var plugin = ScriptableObject.CreateInstance<PreviewTestBrush>();
            var brush = ScriptableObject.CreateInstance<BrushPreset>();
            try
            {
                var values = new TexturePaintPluginParameterSet(); values.Get("opacity", true).number = .15f;
                string before = JsonUtility.ToJson(values);
                brush.spacing = .1f;
                var dense = TexturePaintBrushPreview.Generate(plugin, values, brush, TexturePaintChannel.Albedo, Color.white);
                brush.spacing = 2;
                var sparse = TexturePaintBrushPreview.Generate(plugin, values, brush, TexturePaintChannel.Albedo, Color.white);
                Assert.That(dense[TexturePaintChannel.Albedo].Sum(color => color.a),
                    Is.GreaterThan(sparse[TexturePaintChannel.Albedo].Sum(color => color.a)));
                Assert.That(plugin.calls, Is.Zero, "A preview must not reset an active brush plugin.");
                Assert.That(JsonUtility.ToJson(values), Is.EqualTo(before));
                Assert.That(brush.spacing, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); UnityEngine.Object.DestroyImmediate(brush); }
        }

        private sealed class PreviewTestBrush : ScriptableObject, ITexturePaintBrushV2
        {
            public int calls;
            public TexturePaintPluginDescriptor Descriptor => new TexturePaintPluginDescriptor {
                id = "com.uma.tests.preview-brush", declaredChannels = TexturePaintChannelMask.All };
            public void OnStrokeStart(TexturePaintBrushContextV2 context) { calls++; }
            public void EvaluateSample(TexturePaintBrushContextV2 context, StrokeSample input, ref TexturePaintBrushSampleV2 output)
            { calls++; output.opacityMultiplier = context.parameters.Float("opacity"); context.parameters.Get("private", true).number = 1; }
            public void OnStrokeEnd(TexturePaintBrushContextV2 context, bool committed) { calls++; }
        }

        private static Dictionary<TexturePaintChannel, Color[]> Image(TexturePaintChannel channel, Color color) =>
            new Dictionary<TexturePaintChannel, Color[]> { [channel] = Enumerable.Repeat(color, 128 * 128).ToArray() };
        private static float Difference(Color[] a, Color[] b) => a.Zip(b, (x, y) =>
            Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b) + Mathf.Abs(x.a - y.a)).Average();
    }
}
#endif
