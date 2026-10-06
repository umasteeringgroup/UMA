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
    public sealed class TexturePaintStubbleReliefTests
    {
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public async Task StubbleReliefRaisesHairRecessesFolliclesAndBuildsConicalPimples(int direction, bool gpuUpload)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            const int size = 128;
            using var fixture = new TexturePaintGpuTestFixture(Color.clear, size: size);
            var set = fixture.set;
            using var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            var pack = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            set.compositor = compositor;
            var baseVector = new Vector3(.3f, -.2f, .93f).normalized;
            var baseNormal = new Color(baseVector.x * .5f + .5f, baseVector.y * .5f + .5f, baseVector.z * .5f + .5f, 1);
            foreach (var channel in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness,
                TexturePaintChannel.Normal, TexturePaintChannel.NormalControl, TexturePaintChannel.SkinColorMask,
                TexturePaintChannel.DetailMask })
            {
                if (!set.channels.ContainsKey(channel))
                    set.channels[channel] = new TextureChannelTarget
                    {
                        channel = channel, format = RenderTextureFormat.ARGBHalf,
                        editable = new EditableTextureTarget("Stubble base", size, size, RenderTextureFormat.ARGBHalf,
                            null, channel == TexturePaintChannel.Normal ? baseNormal : channel == TexturePaintChannel.NormalControl
                                ? new Color(.5f, .5f, .5f, 1) : Color.clear)
                    };
                set.channels[channel].composite = EditableTextureTarget.Create("Stubble composite", size, size, RenderTextureFormat.ARGBHalf);
            }
            var store = new TextureStore();
            var sets = (List<TextureSet>)typeof(TextureStore).GetField("sets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(store);
            sets.Add(set);
            set.layers.Add(new TexturePaintLayer { name = "Stubble", kind = TexturePaintLayerKind.Plugin });
            using var host = new PluginHost();
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UMA.TexturePaint.Examples.StubbleMakerGeneratorPlugin")).First(t => t != null);
            var asset = ScriptableObject.CreateInstance(type);
            try
            {
                var plugin = (ITexturePaintCommandExtensionV2)asset;
                var p = host.CreateParameters(plugin);
                p.Get("directionSpace").number = direction;
                p.Get("density").number = 1;
                p.Get("hairLength").number = 24;
                p.Get("hairWidth").number = 8;
                p.Get("hairOpacity").number = 1;
                foreach (string id in new[] { "curvature", "directionVariation", "randomPositionX", "randomPositionY",
                    "lengthVariation", "widthVariation", "shadowAmount", "rednessAmount", "hairHeight", "follicleDepth" })
                    p.Get(id).number = 0;
                async Task Generate()
                {
                    set.channelPackShader = gpuUpload ? pack : null;
                    await host.ExecutePluginLayerAsync(plugin, store, p,
                        new Dictionary<TextureSet, TexturePaintLayer> { { set, set.layers[0] } }, null, CancellationToken.None);
                    set.channelPackShader = pack;
                    set.BindPreviewTextures();
                }
                Color[] Read(TexturePaintChannel channel) => TexturePaintGpuTestFixture.ReadPixels(set.GetVisibleTexture(channel));
                await Generate();
                var baseline = Read(TexturePaintChannel.Normal);
                p.Get("hairHeight").number = .12f;
                await Generate();
                var hair = Read(TexturePaintChannel.NormalControl);
                var hairNormals = Read(TexturePaintChannel.Normal);
                Assert.That(hair.Count(c => c.r > .53f), Is.GreaterThan(100), "Stubble shafts must rise above neutral height.");
                Assert.That(hairNormals.Zip(baseline, (a, b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g)).Count(d => d > .001f),
                    Is.GreaterThan(100), "Raised hairs must perturb the final normal map.");

                p.Get("hairHeight").number = 0;
                p.Get("hairOpacity").number = 0;
                p.Get("follicleDepth").number = .015f;
                p.Get("follicleRadius").number = 3;
                await Generate();
                var follicle = Read(TexturePaintChannel.NormalControl);
                // Roots in both modes fall at x=(2+.5)*20, y=(2+.5)*27.6.
                Assert.That(follicle[69 * size + 50].r, Is.InRange(.482f, .495f));
                Assert.That(follicle[69 * size + 55].r, Is.EqualTo(.5f).Within(.001f));
                Assert.That(follicle.Count(c => c.r > .499f && c.r < .4998f), Is.GreaterThan(10),
                    "Sub-byte follicle relief must survive float generation and composition.");

                p.Get("pimpleAmount").number = 1;
                p.Get("pimpleSpacing").number = 2;
                p.Get("pimpleSize").number = 8;
                p.Get("pimpleHeight").number = .12f;
                p.Get("pimpleColor").color = new Color(.35f, .05f, .03f, 1);
                p.Get("pimpleCenterColor").color = Color.white;
                await Generate();
                var cone = Read(TexturePaintChannel.NormalControl);
                var color = TexturePaintGpuTestFixture.ReadPixels(set.layers[0].channels[TexturePaintChannel.Albedo].Front);
                for (int x = 50; x <= 56; x += 2)
                {
                    float distance = Vector2.Distance(new Vector2(x + .5f, 69.5f), new Vector2(50, 69));
                    Assert.That(cone[69 * size + x].r, Is.EqualTo(.5f + .12f * (1 - distance / 8)).Within(.0015f),
                        "A pimple must replace the recessed root with a cone falling linearly to skin level.");
                }
                Assert.That(color[69 * size + 50].g, Is.GreaterThan(color[69 * size + 54].g + .5f),
                    "The pimple tip must be lighter than the inflamed surrounding skin.");
                Assert.That(cone[69 * size + 60].r, Is.EqualTo(.5f).Within(.001f),
                    "Pimples belong at follicles, not at independently scattered positions.");
                p.Get("overallAmount").number = .5f;
                await Generate();
                var half = Read(TexturePaintChannel.NormalControl);
                Assert.That(half[69 * size + 50].r - .5f, Is.EqualTo((cone[69 * size + 50].r - .5f) * .5f).Within(.001f),
                    "Coverage must attenuate signed displacement exactly once.");
                var rawBase = TexturePaintGpuTestFixture.ReadPixels(set.GetChannel(TexturePaintChannel.Normal).editable.Front);
                Assert.That(rawBase[50].r, Is.EqualTo(baseNormal.r).Within(.001f), "Underlying normals must remain intact.");
                p.Get("pimpleHeight").number = 0;
                p.Get("follicleDepth").number = 0;
                await Generate();
                var disabled = Read(TexturePaintChannel.Normal);
                Assert.That(disabled.Zip(baseline, (a, b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g)).Max(), Is.LessThan(.001f),
                    "Disabling all relief must restore the existing normal map.");
            }
            finally
            {
                sets.Clear(); store.Dispose(); set.compositor = null;
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
#endif
