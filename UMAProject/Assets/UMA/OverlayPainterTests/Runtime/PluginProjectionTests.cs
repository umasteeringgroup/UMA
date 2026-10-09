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
        private Texture2D patternTexture;
        private Sprite patternSprite;

        [SetUp]
        public void SetUp()
        {
            cloth = ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>();
            host = new PluginHost();
            patternTexture = new Texture2D(2, 2);
            patternSprite = Sprite.Create(patternTexture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        }

        [TearDown]
        public void TearDown()
        {
            host?.Dispose();
            if (patternSprite != null) UnityEngine.Object.DestroyImmediate(patternSprite);
            if (patternTexture != null) UnityEngine.Object.DestroyImmediate(patternTexture);
            if (cloth != null) UnityEngine.Object.DestroyImmediate(cloth);
        }

        [Test]
        public void ClothKeepsColorControlsTogetherAndDefaultsToFlatMapping()
        {
            string section = null;
            var colorControls = new HashSet<string>
            {
                "baseColor", "threadColor", "threadColorAmount", "fiberColorVariation",
                "wearColor", "wearAmount", "wearThreadContrast"
            };
            var ids = new HashSet<string>();
            foreach (TexturePaintPluginParameterDefinition parameter in cloth.Descriptor.parameters)
            {
                Assert.That(ids.Add(parameter.id), Is.True, "Duplicate parameter " + parameter.id);
                if (parameter.type == TexturePaintPluginParameterType.Header) section = parameter.id;
                if (parameter.id == "usePatternColor" || parameter.id == "patternColor" || parameter.id == "patternOpacity")
                    Assert.That(section, Is.EqualTo("pattern"), "Pattern appearance controls must be beside the sprite.");
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
        public async Task ClothOverallRotationTurnsEveryOutputIncludingStripesMotifsAndWear()
        {
            var parameters = ClothParameters();
            var original = await Generate(cloth, parameters, null);
            parameters.Get("clothRotation").number = 90;
            var rotated = await Generate(cloth, parameters, null);
            foreach (var channel in original.Keys)
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    AssertColor(rotated[channel][y * Size + x],
                        original[channel][x * Size + Size - 1 - y], 2, channel.ToString());

            parameters.Get("projection").number = 1;
            parameters.Get("clothRotation").number = 45;
            var point = new Vector3(.31f, .63f, .77f);
            rotated = await Generate(cloth, parameters, point, Vector3.forward);
            var rotatedPoint = Quaternion.Euler(0, 0, 45) * (point - new Vector3(.5f, .5f, 0)) +
                new Vector3(.5f, .5f, 0);
            parameters.Get("clothRotation").number = 0;
            original = await Generate(cloth, parameters, rotatedPoint, Vector3.forward);
            foreach (var channel in original.Keys)
                AssertColor(rotated[channel][0], original[channel][0], 1, "World-plane rotation: " + channel);
        }

        [TestCase(45f, 8, 23, 8, 8)]
        [TestCase(-45f, 8, 8, 8, 23)]
        [TestCase(0f, 8, 15, 8, 3)]
        [TestCase(90f, 15, 8, 3, 8)]
        public async Task ClothStripeAnglesRenderIndependentlyOfDirectionPreset(float angle,
            int insideX, int insideY, int outsideX, int outsideY)
        {
            var parameters = host.CreateParameters(cloth);
            parameters.Get("baseColor").color = Color.black;
            parameters.Get("threadColorAmount").number = 0;
            parameters.Get("shadingStrength").number = 0;
            parameters.Get("stripeRepeatX").number = 1;
            parameters.Get("stripeRepeatY").number = 1;
            var stripe = new TexturePaintStripeDefinition { direction = TexturePaintStripeDirection.Horizontal,
                Rotation = angle, position = .5f, width = .05f, softness = .001f, color = Color.white };
            parameters.Stripes("stripeList").Add(stripe);
            var output = await Generate(cloth, parameters, null);
            Assert.That((int)output[TexturePaintChannel.Albedo][insideY * Size + insideX].r, Is.EqualTo(255));
            Assert.That((int)output[TexturePaintChannel.Albedo][outsideY * Size + outsideX].r, Is.Zero);
            stripe.direction = TexturePaintStripeDirection.Vertical;
            AssertSame(output, await Generate(cloth, parameters, null));
            var restored = JsonUtility.FromJson<TexturePaintPluginParameterSet>(JsonUtility.ToJson(parameters));
            AssertSame(output, await Generate(cloth, restored, null));
            var clone = restored.Clone();
            clone.Stripes("stripeList")[0].Rotation = angle + 20;
            Assert.That(restored.Stripes("stripeList")[0].Rotation, Is.EqualTo(angle));
        }

        [Test]
        public void LegacyStripeDirectionsRetainAnglesAndPresetsResetCustomAngles()
        {
            var vertical = JsonUtility.FromJson<TexturePaintStripeDefinition>("{\"direction\":0}");
            var horizontal = JsonUtility.FromJson<TexturePaintStripeDefinition>("{\"direction\":1}");
            Assert.That(vertical.Rotation, Is.EqualTo(90));
            Assert.That(horizontal.Rotation, Is.Zero);
            vertical.Rotation = 45;
            vertical.SetDirection(TexturePaintStripeDirection.Horizontal);
            Assert.That(vertical.Rotation, Is.Zero);
            vertical.Rotation = -45;
            vertical.SetDirection(TexturePaintStripeDirection.Vertical);
            Assert.That(vertical.Rotation, Is.EqualTo(90));
        }

        [Test]
        public async Task ClothCrispProjectionRemovesGhostStripesAcrossACurvedTransition()
        {
            var parameters = host.CreateParameters(cloth);
            parameters.Get("projection").number = 1;
            parameters.Get("baseColor").color = Color.black;
            parameters.Get("threadColorAmount").number = 0;
            parameters.Get("shadingStrength").number = 0;
            parameters.Get("stripeRepeatX").number = 1;
            parameters.Get("stripeList").stripes = new List<TexturePaintStripeDefinition>
            {
                new TexturePaintStripeDefinition { direction = TexturePaintStripeDirection.Vertical,
                    position = .75f, width = .2f, color = Color.white, opacity = 1f }
            };
            var point = new Vector3(.25f, .35f, .75f);
            var blended = await Generate(cloth, parameters, point, new Vector3(.6f, 0f, .8f));
            Assert.That((int)blended[TexturePaintChannel.Albedo][0].r, Is.InRange(50, 75),
                "Cross Fade reproduces the unwanted translucent white stripe on the dark plane.");
            parameters.Get("triplanarBlend").number = 1;
            foreach (float degrees in new[] { 15f, 30f, 44f, 46f, 60f, 75f })
            {
                float radians = degrees * Mathf.Deg2Rad;
                var normal = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                var crisp = await Generate(cloth, parameters, point, normal);
                Assert.That((int)crisp[TexturePaintChannel.Albedo][0].r,
                    Is.EqualTo(degrees < 45f ? 0 : 255), "No ghost stripe at " + degrees);
                var winner = await Generate(cloth, parameters, point,
                    degrees < 45f ? Vector3.forward : Vector3.right);
                AssertSame(winner, crisp); // Roughness and relief choose the same plane as color.
            }
        }

        [Test]
        public async Task ClothBlendTuningSuppressesMinorPlanesWithoutBreakingDiagonalNormals()
        {
            var parameters = ClothParameters();
            parameters.Get("projection").number = 1;
            var point = new Vector3(5.5f / Size, 11.5f / Size, 23.5f / Size);
            var dominant = await Generate(cloth, parameters, point, Vector3.forward);
            parameters.Get("triplanarBlendOffset").number = .4f;
            AssertSame(dominant, await Generate(cloth, parameters, point, new Vector3(.3f, 0f, Mathf.Sqrt(.91f))));
            parameters.Get("triplanarBlendOffset").number = 0;
            parameters.Get("triplanarBlendSharpness").number = 32;
            AssertSame(dominant, await Generate(cloth, parameters, point, new Vector3(.6f, 0f, .8f)));
            parameters.Get("triplanarBlendOffset").number = .49f;
            var diagonal = await Generate(cloth, parameters, point, Vector3.one.normalized);
            var x = await Generate(cloth, parameters, point, Vector3.right);
            var y = await Generate(cloth, parameters, point, Vector3.up);
            foreach (var channel in dominant.Keys)
                AssertColor(diagonal[channel][0], ((Color)x[channel][0] + (Color)y[channel][0] +
                    (Color)dominant[channel][0]) / 3f, 2, "Equal axes remain equal at maximum sharpness: " + channel);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task ClothMappingTransformMovesCoordinatesAndBlendNormalsTogether(int axis)
        {
            var parameters = ClothParameters();
            parameters.Get("projection").number = 1;
            var point = new Vector3(.171875f, .359375f, .734375f);
            var normal = new Vector3(1f, 2f, 3f).normalized;
            var expected = await Generate(cloth, parameters, point, normal);
            var angles = axis == 0 ? new Vector3(60, 0, 0) : axis == 1 ? new Vector3(0, 60, 0) : new Vector3(0, 0, 60);
            var frame = Quaternion.Euler(angles);
            var origin = new Vector3(1.2f, -.8f, .7f);
            parameters.Get("triplanarSize").number = 2.5f;
            parameters.Get("triplanarOffsetX").number = origin.x;
            parameters.Get("triplanarOffsetY").number = origin.y;
            parameters.Get("triplanarOffsetZ").number = origin.z;
            parameters.Get("triplanarRotationX").number = angles.x;
            parameters.Get("triplanarRotationY").number = angles.y;
            parameters.Get("triplanarRotationZ").number = angles.z;
            var actual = await Generate(cloth, parameters, origin + frame * (point * 2.5f), frame * normal);
            foreach (var channel in expected.Keys)
                AssertColor(actual[channel][0], expected[channel][0], 1, channel.ToString());
            // Saved world mapping adjustments must not change Flat / UV output.
            parameters.Get("projection").number = 0;
            AssertSame(await Generate(cloth, ClothParameters(), null), await Generate(cloth, parameters, null));
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
            parameters.Get("patternSprite").sprite = patternSprite;
            parameters.Get("usePatternColor").boolean = false;
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
            var textures = new Dictionary<string, TexturePaintReadOnlyParameterTexture>();
            if (parameters.Get("patternSprite")?.sprite != null)
                textures["patternSprite"] = new TexturePaintReadOnlyParameterTexture("patternSprite", 2, 2,
                    false, new[] { Color.white, Color.clear, Color.gray, Color.white });
            var input = new TexturePaintReadContextV2(null, info, maps, textures, new List<string> { "test" });
            var context = new TexturePaintCommandContextV2(plugin.Descriptor, input, parameters,
                CancellationToken.None, null, 64L * 1024 * 1024);
            await plugin.ExecuteAsync(context);
            var result = new Dictionary<TexturePaintChannel, Color32[]>();
            foreach (TexturePaintPluginTileCommand command in context.SealAndSnapshot())
            {
                command.MaterializeCompactPixels();
                var pixels = new Color32[Size * Size];
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color pixel = command.GetPixel(i);
                    pixels[i] = command.colorSpace == TexturePaintPluginColorSpace.SRGB ? pixel.linear : pixel;
                }
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
