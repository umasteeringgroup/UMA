#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class FabricFuzzGeneratorTests
    {
        private static readonly TexturePaintChannel[] Outputs =
        {
            TexturePaintChannel.Albedo, TexturePaintChannel.Roughness,
            TexturePaintChannel.NormalControl, TexturePaintChannel.DetailMask,
            TexturePaintChannel.AmbientOcclusion
        };
        private TextureStore store;
        private TextureSet set;
        private Mesh mesh;
        private PluginHost host;
        private FabricFuzzGeneratorPlugin plugin;

        [SetUp]
        public void SetUp()
        {
            mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateNormals();
            set = new TextureSet
            {
                persistentId = "fabric-fuzz",
                surface = new ReconstructedSurface { index = 0, mesh = mesh,
                    triangleIslands = new[] { 0, 0 } }
            };
            foreach (TexturePaintChannel channel in Outputs)
                set.channels.Add(channel, new TextureChannelTarget
                {
                    channel = channel, sRGB = channel == TexturePaintChannel.Albedo,
                    format = RenderTextureFormat.ARGB32,
                    editable = new EditableTextureTarget("Fabric test " + channel, 64, 64,
                        RenderTextureFormat.ARGB32, null, Color.clear)
                });
            store = new TextureStore();
            var sets = (List<TextureSet>)typeof(TextureStore).GetField("sets",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);
            sets.Add(set);
            host = new PluginHost();
            plugin = ScriptableObject.CreateInstance<FabricFuzzGeneratorPlugin>();
        }

        [TearDown]
        public void TearDown()
        {
            host?.Dispose();
            store?.Dispose();
            if (plugin != null) Object.DestroyImmediate(plugin);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ReliefControlsAndAoAreDeclaredOnlyForFabric()
        {
            TexturePaintPluginDescriptor descriptor = plugin.Descriptor;
            TexturePaintPluginParameterDefinition depth = descriptor.parameters.Find(p => p.id == "depthStrength");
            TexturePaintPluginParameterDefinition shading = descriptor.parameters.Find(p => p.id == "shadingStrength");
            Assert.That(depth, Is.Not.Null);
            Assert.That(shading, Is.Not.Null);
            Assert.That(depth.displayName, Is.EqualTo("3D Depth"));
            Assert.That(depth.minimum, Is.Zero);
            Assert.That(depth.maximum, Is.EqualTo(4f));
            Assert.That(depth.defaultNumber, Is.EqualTo(1f));
            Assert.That(shading.displayName, Is.EqualTo("Relief Shading"));
            Assert.That(shading.minimum, Is.Zero);
            Assert.That(shading.maximum, Is.EqualTo(3f));
            Assert.That(shading.defaultNumber, Is.EqualTo(1f));
            Assert.That(descriptor.Declares(TexturePaintChannel.AmbientOcclusion), Is.True);
            var other = ScriptableObject.CreateInstance<SurfaceMicroDetailGeneratorPlugin>();
            try
            {
                Assert.That(other.Descriptor.parameters.Exists(p => p.id == "depthStrength" ||
                    p.id == "shadingStrength"), Is.False);
                Assert.That(other.Descriptor.Declares(TexturePaintChannel.AmbientOcclusion), Is.False);
            }
            finally { Object.DestroyImmediate(other); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task DepthOnlyChangesHeightAndRetainsRoundedUnclippedPeaks(int family)
        {
            TexturePaintPluginParameterSet settings = Settings(family);
            settings.Get("height").number = .35f;
            settings.Get("depthStrength").number = 0;
            Dictionary<TexturePaintChannel, Color32[]> flat = await Generate(settings);
            settings.Get("depthStrength").number = 4;
            Dictionary<TexturePaintChannel, Color32[]> raised = await Generate(settings);
            foreach (TexturePaintChannel channel in Outputs)
                if (channel != TexturePaintChannel.NormalControl)
                    CollectionAssert.AreEqual(flat[channel], raised[channel], channel.ToString());
            var heights = new HashSet<byte>();
            int raisedPixels = 0;
            Color32[] before = flat[TexturePaintChannel.NormalControl];
            Color32[] after = raised[TexturePaintChannel.NormalControl];
            for (int i = 0; i < after.Length; i++)
            {
                Assert.That(after[i].a, Is.EqualTo(before[i].a), "Depth changed coverage.");
                if (after[i].a < 16) continue;
                Assert.That(before[i].r, Is.InRange(127, 128), "Zero depth must be neutral height.");
                Assert.That(after[i].r, Is.InRange(127, 253), "Depth clipped or inverted a peak.");
                heights.Add(after[i].r);
                if (after[i].r > 136) raisedPixels++;
            }
            Assert.That(raisedPixels, Is.GreaterThan(10));
            Assert.That(heights.Count, Is.GreaterThan(8), "Maximum depth flattened the relief.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task ShadingOnlyChangesAlbedoAndContactOcclusion(int family)
        {
            TexturePaintPluginParameterSet settings = Settings(family);
            settings.Get("shadingStrength").number = 0;
            Dictionary<TexturePaintChannel, Color32[]> plain = await Generate(settings);
            settings.Get("shadingStrength").number = 3;
            Dictionary<TexturePaintChannel, Color32[]> shaded = await Generate(settings);
            foreach (TexturePaintChannel channel in new[] { TexturePaintChannel.NormalControl,
                         TexturePaintChannel.Roughness, TexturePaintChannel.DetailMask })
                CollectionAssert.AreEqual(plain[channel], shaded[channel], channel.ToString());
            int contactPixels = 0, albedoChanges = 0;
            for (int i = 0; i < plain[TexturePaintChannel.Albedo].Length; i++)
            {
                Color32 albedo = plain[TexturePaintChannel.Albedo][i];
                Color32 shadedAlbedo = shaded[TexturePaintChannel.Albedo][i];
                Color32 ao = plain[TexturePaintChannel.AmbientOcclusion][i];
                Color32 shadedAo = shaded[TexturePaintChannel.AmbientOcclusion][i];
                Assert.That(shadedAlbedo.a, Is.EqualTo(albedo.a));
                Assert.That(shadedAo.a, Is.EqualTo(ao.a));
                if (ao.a < 16) continue;
                Assert.That(ao.r, Is.EqualTo(255), "Zero shading must leave AO neutral.");
                if (shadedAo.r < 240) contactPixels++;
                if (albedo.r != shadedAlbedo.r || albedo.g != shadedAlbedo.g || albedo.b != shadedAlbedo.b)
                    albedoChanges++;
            }
            Assert.That(contactPixels, Is.GreaterThan(10), "Contact shadows did not reach the AO output.");
            Assert.That(albedoChanges, Is.GreaterThan(10), "Relief shading did not affect albedo.");
        }

        [Test]
        public async Task FixedSeedProducesIdenticalReliefAndShading()
        {
            TexturePaintPluginParameterSet settings = Settings(1);
            Dictionary<TexturePaintChannel, Color32[]> first = await Generate(settings);
            Dictionary<TexturePaintChannel, Color32[]> second = await Generate(settings);
            foreach (TexturePaintChannel channel in Outputs)
                CollectionAssert.AreEqual(first[channel], second[channel], channel.ToString());
        }

        [Test]
        public async Task DisablingEveryFeatureLeavesNoContactShadowLayer()
        {
            TexturePaintPluginParameterSet settings = Settings(0);
            settings.Get("density").number = 0;
            settings.Get("pilling").number = 0;
            settings.Get("edgeAmount").number = 0;
            await host.ExecuteCommandAsync(plugin, store, settings, null, CancellationToken.None);
            Assert.That(set.layers, Is.Empty);
        }

        private TexturePaintPluginParameterSet Settings(int family)
        {
            TexturePaintPluginParameterSet settings = host.CreateParameters(plugin);
            settings.Get("projection").number = 0;
            settings.Get("preset").number = family;
            settings.Get("scale").number = 2;
            settings.Get("fiberFrequency").number = 4;
            settings.Get("density").number = 1;
            settings.Get("pilling").number = .8f;
            settings.Get("pillScale").number = 3;
            settings.Get("edgeAmount").number = 0;
            settings.Get("colorStrength").number = 1;
            settings.Get("seed").number = 731;
            return settings;
        }

        private async Task<Dictionary<TexturePaintChannel, Color32[]>> Generate(
            TexturePaintPluginParameterSet settings)
        {
            int layerCount = set.layers.Count;
            await host.ExecuteCommandAsync(plugin, store, settings, null, CancellationToken.None);
            Assert.That(set.layers.Count, Is.EqualTo(layerCount + 1));
            TexturePaintLayer layer = set.layers[set.layers.Count - 1];
            var result = new Dictionary<TexturePaintChannel, Color32[]>();
            foreach (TexturePaintChannel channel in Outputs)
            {
                Assert.That(layer.channels.ContainsKey(channel), Is.True, channel.ToString());
                RenderTexture target = layer.channels[channel].Front;
                RenderTexture previous = RenderTexture.active;
                Texture2D readback = null;
                try
                {
                    RenderTexture.active = target;
                    readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
                    readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                    readback.Apply(false, false);
                    result[channel] = readback.GetPixels32();
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (readback != null) Object.DestroyImmediate(readback);
                }
            }
            return result;
        }
    }
}
#endif
