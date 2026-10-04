#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class PluginProjectionTests
    {
        private const int Size = 32;
        private ClothTextureGeneratorPlugin cloth;
        private PluginHost host;

        [SetUp]
        public void SetUp()
        {
            cloth = ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>();
            host = new PluginHost();
        }

        [TearDown]
        public void TearDown()
        {
            host?.Dispose();
            if (cloth != null) UnityEngine.Object.DestroyImmediate(cloth);
        }

        [Test]
        public void ClothKeepsColorControlsTogetherAndDefaultsToFlatMapping()
        {
            string section = null;
            var colorControls = new HashSet<string>
            {
                "baseColor", "threadColor", "threadColorAmount", "fiberColorVariation",
                "wearColor", "wearAmount", "wearThreadContrast", "usePatternColor",
                "patternColor", "patternOpacity"
            };
            var ids = new HashSet<string>();
            foreach (TexturePaintPluginParameterDefinition parameter in cloth.Descriptor.parameters)
            {
                Assert.That(ids.Add(parameter.id), Is.True, "Duplicate parameter " + parameter.id);
                if (parameter.type == TexturePaintPluginParameterType.Header) section = parameter.id;
                if (colorControls.Contains(parameter.id))
                {
                    Assert.That(section, Is.EqualTo("colors"), parameter.id);
                    colorControls.Remove(parameter.id);
                }
            }
            Assert.That(colorControls, Is.Empty);
            Assert.That(host.CreateParameters(cloth).Integer("projection"), Is.Zero);
            Assert.That(cloth.Descriptor.Requires(TexturePaintMeshMap.WorldPosition), Is.True);
            Assert.That(cloth.Descriptor.Requires(TexturePaintMeshMap.WorldNormal), Is.True);
        }

        [Test]
        public async Task ClothHostSkipsMapSnapshotsForFlatAndCapturesThemForTriplanar()
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(1, 0, 0), new Vector3(1, 0, 3),
                    new Vector3(1, 2, 3), new Vector3(1, 2, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateNormals();
            var set = new TextureSet
            {
                persistentId = "cloth-projection",
                surface = new ReconstructedSurface { index = 0, mesh = mesh,
                    triangleIslands = new[] { 0, 0 } }
            };
            foreach (TexturePaintChannel channel in new[] { TexturePaintChannel.Albedo,
                         TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl })
                set.channels.Add(channel, new TextureChannelTarget
                {
                    channel = channel, sRGB = channel == TexturePaintChannel.Albedo,
                    format = RenderTextureFormat.ARGB32,
                    editable = new EditableTextureTarget("Cloth mapping test " + channel,
                        Size, Size, RenderTextureFormat.ARGB32, null, Color.clear)
                });
            var store = new TextureStore();
            ((List<TextureSet>)typeof(TextureStore).GetField("sets",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store)).Add(set);
            var executionHost = new PluginHost { SnapshotMemoryBudgetBytes = 1 };
            try
            {
                TexturePaintPluginParameterSet parameters = executionHost.CreateParameters(cloth);
                parameters.Get("weaveScale").number = 8;
                await executionHost.ExecuteCommandAsync(cloth, store, parameters, null, CancellationToken.None);
                Assert.That(set.layers.Count, Is.EqualTo(1), "Flat mapping must fit without any pixel snapshots.");
                parameters.Get("projection").number = 1;
                executionHost.SnapshotMemoryBudgetBytes = 64L * 1024 * 1024;
                await executionHost.ExecuteCommandAsync(cloth, store, parameters, null, CancellationToken.None);
                Assert.That(set.layers.Count, Is.EqualTo(2), "Triplanar needs both captured world mesh maps.");
                Assert.That(set.layers[1].channels.ContainsKey(TexturePaintChannel.NormalControl), Is.True);
            }
            finally
            {
                executionHost.Dispose();
                store.Dispose();
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(0, 23, 11)]
        [TestCase(1, 5, 23)]
        [TestCase(2, 5, 11)]
        public async Task ClothTriplanarUsesTheCorrectWorldPlaneForEveryChannel(int axis, int x, int y)
        {
            TexturePaintPluginParameterSet parameters = ClothParameters();
            Dictionary<TexturePaintChannel, Color32[]> flat = await Generate(cloth, parameters, null);
            parameters.Get("projection").number = 1;
            Vector3 normal = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Dictionary<TexturePaintChannel, Color32[]> projected = await Generate(cloth, parameters,
                new Vector3(5.5f / Size, 11.5f / Size, 23.5f / Size), normal);
            foreach (TexturePaintChannel channel in flat.Keys)
            {
                Color32 expected = flat[channel][y * Size + x];
                // The same world point must produce the same cloth across different UV texels.
                foreach (Color32 actual in projected[channel]) AssertColor(actual, expected, 1, channel.ToString());
            }
        }

        [Test]
        public async Task ClothTriplanarBlendsAllThreeAxesUsingWorldNormals()
        {
            TexturePaintPluginParameterSet parameters = ClothParameters();
            Dictionary<TexturePaintChannel, Color32[]> flat = await Generate(cloth, parameters, null);
            parameters.Get("projection").number = 1;
            Dictionary<TexturePaintChannel, Color32[]> projected = await Generate(cloth, parameters,
                new Vector3(5.5f / Size, 11.5f / Size, 23.5f / Size), new Vector3(1, 2, 3).normalized);
            foreach (TexturePaintChannel channel in flat.Keys)
            {
                Color expected = ((Color)flat[channel][11 * Size + 23] +
                    (Color)flat[channel][23 * Size + 5] * 16f +
                    (Color)flat[channel][11 * Size + 5] * 81f) / 98f;
                AssertColor(projected[channel][0], expected, 2, channel.ToString());
            }
        }

        [Test]
        public async Task ClothFlatModeIgnoresWorldMapsWhileTriplanarRespondsToWorldPosition()
        {
            TexturePaintPluginParameterSet parameters = ClothParameters();
            Vector3 first = new Vector3(.13f, .39f, .67f), second = new Vector3(.62f, .81f, .21f);
            Dictionary<TexturePaintChannel, Color32[]> flatA = await Generate(cloth, parameters, first);
            Dictionary<TexturePaintChannel, Color32[]> flatB = await Generate(cloth, parameters, second);
            AssertSame(flatA, flatB);
            parameters.Get("projection").number = 1;
            Dictionary<TexturePaintChannel, Color32[]> worldA = await Generate(cloth, parameters, first);
            Dictionary<TexturePaintChannel, Color32[]> worldB = await Generate(cloth, parameters, second);
            Assert.That(AnyDifference(worldA, worldB), Is.True, "Triplanar ignored world position.");
        }

        [Test]
        public async Task ClothTriplanarReportsMissingMeshMapsInsteadOfFallingBackToUvs()
        {
            TexturePaintPluginParameterSet parameters = ClothParameters();
            parameters.Get("projection").number = 1;
            InvalidOperationException exception = null;
            try { await Generate(cloth, parameters, null); }
            catch (InvalidOperationException caught) { exception = caught; }
            Assert.That(exception, Is.Not.Null, "Triplanar generation should reject missing mesh maps.");
            Assert.That(exception.Message, Does.Contain("World Position"));
        }

        [Test]
        public async Task RustProjectionSettingActuallySwitchesBetweenUvAndWorldCoordinates()
        {
            var rust = ScriptableObject.CreateInstance<RustCorrosionGeneratorPlugin>();
            try
            {
                TexturePaintPluginParameterSet parameters = host.CreateParameters(rust);
                parameters.Get("spread").number = 1;
                parameters.Get("projection").number = 0;
                Vector3 first = new Vector3(.13f, .39f, .67f), second = new Vector3(2.62f, 4.81f, 1.21f);
                Dictionary<TexturePaintChannel, Color32[]> flatA = await Generate(rust, parameters, first);
                Dictionary<TexturePaintChannel, Color32[]> flatB = await Generate(rust, parameters, second);
                AssertSame(flatA, flatB);
                parameters.Get("projection").number = 1;
                Dictionary<TexturePaintChannel, Color32[]> worldA = await Generate(rust, parameters, first);
                Dictionary<TexturePaintChannel, Color32[]> worldB = await Generate(rust, parameters, second);
                Assert.That(AnyDifference(worldA, worldB), Is.True, "Rust ignored the selected world projection.");
            }
            finally { UnityEngine.Object.DestroyImmediate(rust); }
        }

        private TexturePaintPluginParameterSet ClothParameters()
        {
            TexturePaintPluginParameterSet parameters = host.CreateParameters(cloth);
            parameters.Get("weaveScale").number = 8;
            parameters.Get("wearAmount").number = .55f;
            parameters.Get("wearThreshold").number = .3f;
            parameters.Get("patternOpacity").number = .65f;
            parameters.Get("patternEmboss").number = .2f;
            parameters.Get("patternRoughness").number = .3f;
            parameters.Get("stripeList").stripes = new List<TexturePaintStripeDefinition>
            {
                new TexturePaintStripeDefinition { direction = TexturePaintStripeDirection.Vertical,
                    position = .25f, width = .3f, color = Color.red, opacity = .7f }
            };
            return parameters;
        }

        private static async Task<Dictionary<TexturePaintChannel, Color32[]>> Generate(
            ITexturePaintGeneratorV2 plugin, TexturePaintPluginParameterSet parameters,
            Vector3? position, Vector3? normal = null)
        {
            var info = new Dictionary<string, TexturePaintReadOnlyChannelInfo>();
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                if (plugin.Descriptor.Declares(channel))
                    info.Add(TexturePaintReadContextV2.Key("test", channel),
                        new TexturePaintReadOnlyChannelInfo("test", channel, Size, Size,
                            channel == TexturePaintChannel.Albedo));
            var maps = new Dictionary<string, TexturePaintReadOnlyMeshMap>();
            if (position.HasValue)
            {
                Vector3 point = position.Value, direction = normal ?? Vector3.forward;
                AddMap(TexturePaintMeshMap.WorldPosition, new Color(point.x, point.y, point.z, 1));
                AddMap(TexturePaintMeshMap.WorldNormal, new Color(direction.x * .5f + .5f,
                    direction.y * .5f + .5f, direction.z * .5f + .5f, 1));
            }
            var textures = new Dictionary<string, TexturePaintReadOnlyParameterTexture>
            {
                ["patternSprite"] = new TexturePaintReadOnlyParameterTexture("patternSprite", 2, 2,
                    false, new[] { Color.white, Color.clear, Color.gray, Color.white })
            };
            var input = new TexturePaintReadContextV2(null, info, maps, textures, new List<string> { "test" });
            var context = new TexturePaintCommandContextV2(plugin.Descriptor, input, parameters,
                CancellationToken.None, null, 64L * 1024 * 1024);
            await plugin.ExecuteAsync(context);
            var result = new Dictionary<TexturePaintChannel, Color32[]>();
            foreach (TexturePaintPluginTileCommand command in context.SealAndSnapshot())
            {
                command.MaterializeCompactPixels();
                var pixels = new Color32[Size * Size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = command.GetPixel(i);
                result.Add(command.channel, pixels);
                command.ReleaseMaterializedCompactPixels();
            }
            return result;

            void AddMap(TexturePaintMeshMap map, Color value)
            {
                maps.Add(TexturePaintReadContextV2.MeshKey("test", map),
                    new TexturePaintReadOnlyMeshMap("test", map, 2, 2, new[] { value, value, value, value }));
            }
        }

        private static void AssertSame(Dictionary<TexturePaintChannel, Color32[]> a,
            Dictionary<TexturePaintChannel, Color32[]> b)
        {
            CollectionAssert.AreEquivalent(a.Keys, b.Keys);
            foreach (TexturePaintChannel channel in a.Keys) CollectionAssert.AreEqual(a[channel], b[channel], channel.ToString());
        }

        private static bool AnyDifference(Dictionary<TexturePaintChannel, Color32[]> a,
            Dictionary<TexturePaintChannel, Color32[]> b)
        {
            foreach (TexturePaintChannel channel in a.Keys)
                for (int i = 0; i < a[channel].Length; i++)
                    if (!a[channel][i].Equals(b[channel][i])) return true;
            return false;
        }

        private static void AssertColor(Color32 actual, Color32 expected, int tolerance, string message)
        {
            Assert.That((int)actual.r, Is.EqualTo((int)expected.r).Within(tolerance), message + " R");
            Assert.That((int)actual.g, Is.EqualTo((int)expected.g).Within(tolerance), message + " G");
            Assert.That((int)actual.b, Is.EqualTo((int)expected.b).Within(tolerance), message + " B");
            Assert.That((int)actual.a, Is.EqualTo((int)expected.a).Within(tolerance), message + " A");
        }
    }
}
#endif
