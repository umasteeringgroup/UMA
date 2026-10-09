#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class PluginClothColorTests
    {
        [TestCase(.03f, .05f, .07f)]
        [TestCase(.15f, .4f, .7f)]
        public async Task DyedClothKeepsAuthoredColorThroughCompactUpload(float r, float g, float b)
        {
            Color authored = new Color(r, g, b, 1);
            var pixels = await Render(authored, 0f);
            foreach (Color pixel in pixels)
            {
                Assert.That(pixel.r, Is.EqualTo(authored.linear.r).Within(.002f * Mathf.Max(.1f, r)));
                Assert.That(pixel.g, Is.EqualTo(authored.linear.g).Within(.003f * Mathf.Max(.1f, g)));
                Assert.That(pixel.b, Is.EqualTo(authored.linear.b).Within(.004f * Mathf.Max(.1f, b)));
            }
        }

        [Test]
        public async Task FiberVariationPreservesSaturatedDyeInsteadOfAddingGray()
        {
            var pixels = await Render(new Color(.25f, 0f, 0f, 1f), .5f);
            float minimum = 1f, maximum = 0f;
            foreach (Color pixel in pixels)
            {
                Assert.That(pixel.g, Is.Zero); Assert.That(pixel.b, Is.Zero);
                minimum = Mathf.Min(minimum, pixel.r); maximum = Mathf.Max(maximum, pixel.r);
            }
            Assert.That(maximum - minimum, Is.GreaterThan(.001f), "Fiber brightness should still vary.");
        }

        private static async Task<Color[]> Render(Color color, float variation)
        {
            var plugin = ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>();
            try
            {
                var p = new TexturePaintPluginParameterSet(); p.ResetToDefaults(plugin.Descriptor);
                p.Get("baseColor").color = color; p.Get("threadColorAmount").number = 0;
                p.Get("fiberColorVariation").number = variation; p.Get("shadingStrength").number = 0;
                p.Get("weaveScale").number = 8;
                var info = new Dictionary<string, TexturePaintReadOnlyChannelInfo>
                {
                    [TexturePaintReadContextV2.Key("cloth", TexturePaintChannel.Albedo)] =
                        new TexturePaintReadOnlyChannelInfo("cloth", TexturePaintChannel.Albedo, 64, 64, true)
                };
                var source = new TexturePaintReadContextV2(null, info, null, null, new List<string> { "cloth" });
                var context = new TexturePaintCommandContextV2(plugin.Descriptor, source, p, CancellationToken.None, null, 1024 * 1024);
                await plugin.ExecuteAsync(context);
                var result = new Color[64 * 64];
                foreach (var command in context.SealAndSnapshot())
                {
                    command.MaterializeCompactPixels();
                    for (int i = 0; i < result.Length; i++)
                    {
                        Color pixel = command.GetPixel(i);
                        result[i] = command.colorSpace == TexturePaintPluginColorSpace.SRGB ? pixel.linear : pixel;
                    }
                    command.ReleaseMaterializedCompactPixels();
                }
                return result;
            }
            finally { Object.DestroyImmediate(plugin); }
        }
    }
}
#endif
