#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed partial class PluginApiV2Tests
    {
        private void ResizeGarmentMaterialFixture()
        {
            foreach (var channel in set.channels.Values)
            {
                channel.editable.Dispose();
                channel.editable = new EditableTextureTarget("Garment material " + channel.channel,
                    128, 128, RenderTextureFormat.ARGB32, null, Color.clear);
            }
        }

        private static Color[] MaterialPixels(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(readback); }
        }

        private async Task<Color[][]> RenderGarmentMaterial(ITexturePaintGeneratorV2 plugin,
            TexturePaintPluginParameterSet parameters)
        {
            await host.ExecuteCommandAsync(plugin, store, parameters, null, CancellationToken.None);
            var channels = new[] { TexturePaintChannel.Albedo, TexturePaintChannel.NormalControl,
                TexturePaintChannel.AmbientOcclusion };
            var result = channels.Select(channel => set.layers[0].channels.TryGetValue(channel, out var target)
                ? MaterialPixels(target.Front) : null).ToArray();
            Assert.That(host.Undo(), Is.True);
            return result;
        }

        [Test]
        public async Task AllSixteenClothWeavesHaveDistinctRenderedProfiles()
        {
            ResizeGarmentMaterialFixture();
            var plugin = ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>();
            try
            {
                var p = host.CreateParameters(plugin);
                p.Get("weaveScale").number = 7;
                p.Get("wearAmount").number = 0;
                var signatures = new System.Collections.Generic.List<Color[]>();
                for (int weave = 0; weave < 16; weave++)
                {
                    p.Get("weave").number = weave;
                    var rendered = await RenderGarmentMaterial(plugin, p);
                    Assert.That(signatures.All(previous => !previous.SequenceEqual(rendered[1])), Is.True,
                        "Weave " + weave + " collapsed to another pattern.");
                    signatures.Add(rendered[1]);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task GarmentMaterialDepthAndShadingRemainIndependent(int textileMode)
        {
            ResizeGarmentMaterialFixture();
            ScriptableObject instance = textileMode < 0
                ? (ScriptableObject)ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>()
                : ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                atlas.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); atlas.Apply();
                var plugin = (ITexturePaintGeneratorV2)instance;
                var p = host.CreateParameters(plugin);
                if (textileMode < 0) p.Get("weaveScale").number = 6;
                else
                {
                    p.Get("mode").number = textileMode;
                    p.Get("scale").number = 4;
                    p.Get("threadDensity").number = 20;
                    p.Get("atlas").texture = atlas;
                    p.Get("density").number = 1;
                }
                var baseline = await RenderGarmentMaterial(plugin, p);
                p.Get("depthStrength").number = 0;
                var flat = await RenderGarmentMaterial(plugin, p);
                Assert.That(flat[0], Is.EqualTo(baseline[0]), "Depth changed albedo.");
                Assert.That(flat[1].Where(c => c.a > .99f).All(c => Mathf.Abs(c.r - .5f) < .005f), Is.True);
                p.Get("depthStrength").number = 4;
                var deep = await RenderGarmentMaterial(plugin, p);
                Assert.That(deep[1].Where(c => c.a > .99f).All(c => c.r > .015f && c.r < .985f), Is.True,
                    "Extreme relief must retain headroom, without clipped plateaus.");
                Assert.That(deep[1].Where(c => c.a > .99f).Max(c => Mathf.Abs(c.r - .5f)),
                    Is.GreaterThan(flat[1].Max(c => c.a > .99f ? Mathf.Abs(c.r - .5f) : 0) + .005f));
                p.Get("shadingStrength").number = 0;
                var unshaded = await RenderGarmentMaterial(plugin, p);
                Assert.That(unshaded[1], Is.EqualTo(deep[1]), "Shading changed physical relief.");
                Assert.That(unshaded[0], Is.Not.EqualTo(deep[0]), "Contact shading had no visible effect.");
                if (textileMode >= 0)
                    Assert.That(unshaded[2].Where(c => c.a > .99f).All(c => c.r > .99f), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); UnityEngine.Object.DestroyImmediate(atlas); }
        }

        [Test]
        public async Task UnresolvedClothThreadsDoNotProduceAliasedHeightOrColorNoise()
        {
            ResizeGarmentMaterialFixture();
            var plugin = ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>();
            try
            {
                var p = host.CreateParameters(plugin);
                p.Get("weaveScale").number = 512;
                p.Get("wearAmount").number = 0;
                var output = await RenderGarmentMaterial(plugin, p);
                Assert.That(output[1].Where(c => c.a > .99f).All(c => Mathf.Abs(c.r - .5f) < .005f), Is.True);
                Assert.That(output[0].Where(c => c.a > .99f).Select(c => c.r).Distinct().Count(), Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [TestCase(1, false)]
        [TestCase(3, false)]
        [TestCase(3, true)]
        public async Task TextileAccentAmountChangesOnlyColorInEmbroideryAndAtlas(int mode, bool atlasColor)
        {
            ResizeGarmentMaterialFixture();
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                atlas.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                atlas.Apply();
                var p = host.CreateParameters(plugin);
                p.Get("mode").number = mode;
                p.Get("baseColor").color = new Color(.2f, .4f, .6f, 1f);
                p.Get("accentColor").color = Color.red;
                p.Get("colorAmount").number = 1;
                p.Get("shadingStrength").number = 0;
                p.Get("pattern").texture = atlas;
                p.Get("breakup").number = 0;
                p.Get("atlas").texture = atlas;
                p.Get("atlasColumns").number = 1;
                p.Get("atlasRows").number = 1;
                p.Get("density").number = 1;
                p.Get("scatterSize").number = 1;
                p.Get("jitter").number = 0;
                p.Get("sizeVariation").number = 0;
                p.Get("rotationVariation").number = 0;
                p.Get("tintVariation").number = 0;
                p.Get("useAtlasColor").boolean = atlasColor;
                var accented = await RenderGarmentMaterial(plugin, p);
                p.Get("colorAmount").number = 0;
                var plain = await RenderGarmentMaterial(plugin, p);
                Assert.That(plain[0], Is.Not.EqualTo(accented[0]), "Accent Color Amount had no effect.");
                Assert.That(plain[1], Is.EqualTo(accented[1]), "Accent Color Amount changed height.");
                Assert.That(plain[2], Is.EqualTo(accented[2]), "Accent Color Amount changed AO.");
                Assert.That(plain[0].Select(c => c.a), Is.EqualTo(accented[0].Select(c => c.a)),
                    "Accent Color Amount changed coverage.");
                var visible = plain[0].Where(c => c.a > .99f).ToArray();
                Assert.That(visible, Is.Not.Empty);
                Assert.That(visible.All(c => Mathf.Abs(c.r - .03310477f) < .005f &&
                    Mathf.Abs(c.g - .13286832f) < .005f && Mathf.Abs(c.b - .31854678f) < .005f), Is.True,
                    "Zero accent amount should retain the base color in linear working space.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plugin);
                UnityEngine.Object.DestroyImmediate(atlas);
            }
        }
    }
}
#endif
