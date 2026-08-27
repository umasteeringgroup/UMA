#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UMA.TexturePaint.Examples;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.TexturePaint.Tests
{
    public sealed class PluginApiV2Tests
    {
        private TextureStore store;
        private TextureSet set;
        private Mesh mesh;
        private PluginHost host;

        [SetUp]
        public void SetUp()
        {
            mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 }
            };
            set = new TextureSet
            {
                persistentId = "surface",
                surface = new ReconstructedSurface { index = 0, mesh = mesh, triangleIslands = new[] { 0 } }
            };
            set.channels.Add(TexturePaintChannel.Albedo, MakeChannel(TexturePaintChannel.Albedo, true, Color.black));
            set.channels.Add(TexturePaintChannel.Roughness, MakeChannel(TexturePaintChannel.Roughness, false, Color.black));
            set.channels.Add(TexturePaintChannel.Normal, MakeChannel(TexturePaintChannel.Normal, false, new Color(0.5f, 0.5f, 1f, 1f)));
            set.channels.Add(TexturePaintChannel.Metallic, MakeChannel(TexturePaintChannel.Metallic, false, Color.black));
            set.channels.Add(TexturePaintChannel.AmbientOcclusion, MakeChannel(TexturePaintChannel.AmbientOcclusion, false, Color.white));
            set.channels.Add(TexturePaintChannel.Custom, MakeChannel(TexturePaintChannel.Custom, false, Color.black));
            set.channels.Add(TexturePaintChannel.SkinColorMask, MakeChannel(TexturePaintChannel.SkinColorMask, true, Color.clear));
            set.channels.Add(TexturePaintChannel.Thickness, MakeChannel(TexturePaintChannel.Thickness, false, Color.black));
            set.channels.Add(TexturePaintChannel.DetailMask, MakeChannel(TexturePaintChannel.DetailMask, false, Color.black));
            set.channels.Add(TexturePaintChannel.NormalControl, MakeChannel(TexturePaintChannel.NormalControl, false,
                new Color(0.5f, 0.5f, 0.5f, 1f)));
            store = new TextureStore();
            FieldInfo field = typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic);
            ((List<TextureSet>)field.GetValue(store)).Add(set);
            host = new PluginHost();
        }

        [TearDown]
        public void TearDown()
        {
            host?.Dispose(); store?.Dispose();
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public async Task SuccessfulCommandIsGeometryClippedDirtyAndUndoable()
        {
            await host.ExecuteCommandAsync(new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear,
                new RectInt(0, 0, 4, 4)), store, null, null, CancellationToken.None);
            Assert.That(set.layers.Count, Is.EqualTo(1));
            Color inside = Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front, 1, 1);
            Color outside = Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front, 7, 7);
            Assert.That(inside.r, Is.GreaterThan(0.9f));
            Assert.That(outside.a, Is.LessThan(0.01f));
            Assert.That(host.CanUndo, Is.True);
            Assert.That(host.Undo(), Is.True); Assert.That(set.layers.Count, Is.Zero);
            Assert.That(host.Redo(), Is.True); Assert.That(set.layers.Count, Is.EqualTo(1));
        }

        [Test]
        public void UndeclaredChannelFailsBeforeCommit()
        {
            SolidPlugin plugin = new SolidPlugin(TexturePaintChannel.Roughness, TexturePaintPluginColorSpace.Data,
                new RectInt(0, 0, 2, 2), TexturePaintChannelMask.Albedo);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Overlay Painter plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(plugin, store,
                null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
            Assert.That(host.Diagnostics[host.Diagnostics.Count - 1].severity, Is.EqualTo(TexturePaintPluginDiagnosticSeverity.Error));
        }

        [Test]
        public void DataChannelRejectsColorEncodedPayload()
        {
            SolidPlugin plugin = new SolidPlugin(TexturePaintChannel.Roughness, TexturePaintPluginColorSpace.SRGB,
                new RectInt(0, 0, 2, 2));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Overlay Painter plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(plugin, store,
                null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
        }

        [Test]
        public void CancellationBeforeSnapshotCannotCommit()
        {
            CancellationTokenSource source = new CancellationTokenSource(); source.Cancel();
            Assert.That(async () => await host.ExecuteCommandAsync(
                new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 2, 2)),
                store, null, null, source.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(set.layers.Count, Is.Zero); source.Dispose();
        }

        [Test]
        public void CancellationDuringPluginExecutionCannotCommitQueuedCommands()
        {
            CancellationTokenSource source = new CancellationTokenSource();
            WaitingPlugin plugin = new WaitingPlugin();
            Task execution = host.ExecuteCommandAsync(plugin, store, null, null, source.Token);
            Assert.That(plugin.started.Wait(2000), Is.True, "Plugin did not reach its cancellable work phase.");
            source.Cancel();
            Assert.That(async () => await execution, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(set.layers.Count, Is.Zero, "A cancelled plugin transaction committed a layer.");
            Assert.That(host.CanUndo, Is.False);
            source.Dispose();
            plugin.started.Dispose();
        }

        [Test]
        public void ParameterProfilesRoundTripWithoutSharingInstances()
        {
            SolidPlugin plugin = new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 2, 2));
            plugin.Descriptor.parameters.Add(new TexturePaintPluginParameterDefinition
            { id = "strength", type = TexturePaintPluginParameterType.Float, minimum = 0f, maximum = 1f, defaultNumber = 0.5f });
            host.GetParameters(plugin).Get("strength").number = 0.75f;
            List<TexturePaintPluginProfile> saved = host.CaptureProfiles();
            host.GetParameters(plugin).Get("strength").number = 0.1f;
            host.RestoreProfiles(saved);
            Assert.That(host.GetParameters(plugin).Float("strength"), Is.EqualTo(0.75f));
        }

        [Test]
        public void ResetToDefaultsRestoresEveryParameterKindWithoutSharingMutableDefaults()
        {
            var defaultCurve = new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(1f, 0.8f));
            var defaultStripes = new List<TexturePaintStripeDefinition>
            {
                new TexturePaintStripeDefinition
                {
                    direction = TexturePaintStripeDirection.Horizontal,
                    width = 0.17f,
                    color = Color.cyan
                }
            };
            var descriptor = new TexturePaintPluginDescriptor
            {
                parameters = new List<TexturePaintPluginParameterDefinition>
                {
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "section", type = TexturePaintPluginParameterType.Header
                    },
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "amount", type = TexturePaintPluginParameterType.Float,
                        minimum = 0f, maximum = 1f, defaultNumber = 0.35f
                    },
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "enabled", type = TexturePaintPluginParameterType.Boolean,
                        defaultBoolean = true
                    },
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "tint", type = TexturePaintPluginParameterType.Color,
                        defaultColor = Color.magenta
                    },
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "label", type = TexturePaintPluginParameterType.String,
                        defaultText = "Default"
                    },
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "curve", type = TexturePaintPluginParameterType.Curve,
                        defaultCurve = defaultCurve
                    },
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "stripes", type = TexturePaintPluginParameterType.StripeList,
                        defaultStripes = defaultStripes
                    },
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "texture", type = TexturePaintPluginParameterType.Texture
                    }
                }
            };
            var values = new TexturePaintPluginParameterSet();
            values.ResetToDefaults(descriptor);
            values.Get("amount").number = 0.9f;
            values.Get("enabled").boolean = false;
            values.Get("tint").color = Color.green;
            values.Get("label").text = "Changed";
            values.Get("curve").curve.MoveKey(0, new Keyframe(0f, 1f));
            values.Get("stripes").stripes[0].width = 0.9f;

            values.ResetToDefaults(descriptor);

            Assert.That(values.values, Has.Count.EqualTo(7), "Headers are not parameter values.");
            Assert.That(values.Float("amount"), Is.EqualTo(0.35f));
            Assert.That(values.Boolean("enabled"), Is.True);
            Assert.That(values.Color("tint", Color.clear), Is.EqualTo(Color.magenta));
            Assert.That(values.String("label"), Is.EqualTo("Default"));
            Assert.That(values.Curve("curve").Evaluate(0f), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(values.Stripes("stripes")[0].width, Is.EqualTo(0.17f).Within(0.001f));
            Assert.That(values.Texture("texture"), Is.Null);
            Assert.That(values.Curve("curve"), Is.Not.SameAs(defaultCurve));
            Assert.That(values.Stripes("stripes")[0], Is.Not.SameAs(defaultStripes[0]));
        }

        [Test]
        public void CommandMemoryBudgetRejectsPayloadBeforeCommit()
        {
            host.CommandMemoryBudgetBytes = 1;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Overlay Painter plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(
                new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 2, 2)),
                store, null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
        }

        [Test]
        public void SnapshotMemoryBudgetRejectsReadBeforePluginRuns()
        {
            host.SnapshotMemoryBudgetBytes = 1;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Overlay Painter plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(
                new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 2, 2)),
                store, null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
        }

        [Test]
        public async Task ExplicitReadChannelsKeepWriteMetadataWithoutCopyingWritePixels()
        {
            host.CommandMemoryBudgetBytes = 20;
            ReadWriteProbePlugin plugin = new ReadWriteProbePlugin();

            await host.ExecuteCommandAsync(plugin, store, null, null, CancellationToken.None);

            Assert.That(plugin.roughness, Is.Not.Null);
            Assert.That(plugin.roughness.width, Is.EqualTo(4),
                "Read snapshots must honor the descriptor's resolution cap.");
            Assert.That(plugin.albedoPixels, Is.Null,
                "A write-only channel must not consume immutable snapshot memory.");
            Assert.That(plugin.albedoInfo, Is.Not.Null);
            Assert.That(plugin.albedoInfo.width, Is.EqualTo(8));
            Assert.That(set.layers, Has.Count.EqualTo(1));
            Assert.That(Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front, 1, 1).g,
                Is.GreaterThan(0.9f));
        }

        [Test]
        public void DiscoveryIgnoresPrivatePluginTestHelpers()
        {
            host.Discover();

            for (int i = 0; i < host.Diagnostics.Count; i++)
            {
                Assert.That(host.Diagnostics[i].pluginId, Is.Not.EqualTo(typeof(SolidPlugin).FullName));
                Assert.That(host.Diagnostics[i].pluginId, Is.Not.EqualTo(typeof(WaitingPlugin).FullName));
            }
        }

        [Test]
        public async Task NormalCommandsAreRenormalized()
        {
            await host.ExecuteCommandAsync(new SolidPlugin(TexturePaintChannel.Normal, TexturePaintPluginColorSpace.Data,
                new RectInt(0, 0, 2, 2)), store, null, null, CancellationToken.None);
            Color encoded = Read(set.layers[0].channels[TexturePaintChannel.Normal].Front, 1, 1);
            Vector3 decoded = new Vector3(encoded.r * 2f - 1f, encoded.g * 2f - 1f, encoded.b * 2f - 1f);
            Assert.That(decoded.magnitude, Is.EqualTo(1f).Within(0.02f));
        }

        [Test]
        public async Task RequestedMeshMapsAreImmutableAndAvailableToGenerator()
        {
            MeshMapProbePlugin plugin = new MeshMapProbePlugin();
            await host.ExecuteCommandAsync(plugin, store, null, null, CancellationToken.None);

            Assert.That(plugin.curvature, Is.Not.Null);
            Assert.That(plugin.worldNormal, Is.Not.Null);
            Assert.That(plugin.worldPosition, Is.Not.Null);
            Assert.That(plugin.ambientOcclusion, Is.Not.Null);
            Assert.That(plugin.meshThickness, Is.Not.Null);
            Assert.That(plugin.surfaceId, Is.Not.Null);
            Assert.That(plugin.thicknessChannel, Is.Not.Null,
                "Plugin snapshots must include channels added after the original API v2 implementation.");
            Assert.That(plugin.normalControlChannel, Is.Not.Null);
            Assert.That(plugin.curvature.map, Is.EqualTo(TexturePaintMeshMap.SignedCurvature));
            Assert.That(plugin.curvature.GetPixelBilinear(0.25f, 0.25f).r,
                Is.EqualTo(0.5f).Within(0.02f), "A flat mesh must have neutral signed curvature.");
            Assert.That(plugin.curvature.CopyPixels(), Is.Not.SameAs(plugin.curvature.CopyPixels()));
        }

        [Test]
        public async Task NonReadableTextureParameterIsCapturedAsImmutablePixels()
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            texture.Apply(false, true);
            TextureParameterProbePlugin plugin = new TextureParameterProbePlugin();
            TexturePaintPluginParameterSet parameters = host.GetParameters(plugin);
            parameters.Get("source").texture = texture;
            try
            {
                await host.ExecuteCommandAsync(plugin, store, parameters, null, CancellationToken.None);
                Assert.That(plugin.snapshot, Is.Not.Null);
                Assert.That(plugin.snapshot.width, Is.EqualTo(2));
                Assert.That(plugin.snapshot.CopyPixels(), Has.Length.EqualTo(4));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void SignedCurvatureSeparatesConvexAndConcaveBends()
        {
            Vector3[] vertices = { Vector3.zero, Vector3.right, Vector3.up };
            int[] triangles = { 0, 1, 2 };
            Vector3 forward = Vector3.forward;
            Vector3[] convex =
            {
                forward,
                (forward + Vector3.right * 0.35f).normalized,
                (forward + Vector3.up * 0.35f).normalized
            };
            Vector3[] concave =
            {
                forward,
                (forward - Vector3.right * 0.35f).normalized,
                (forward - Vector3.up * 0.35f).normalized
            };

            float[] convexValues = ProceduralMeshMapBuilder.BuildVertexSignedCurvature(
                vertices, convex, triangles);
            float[] concaveValues = ProceduralMeshMapBuilder.BuildVertexSignedCurvature(
                vertices, concave, triangles);

            Assert.That(convexValues[0], Is.GreaterThan(0.05f));
            Assert.That(concaveValues[0], Is.LessThan(-0.05f));
        }

        [Test]
        public void PluginPixelSamplingUsesTexelCentersWithoutSofteningEdges()
        {
            Color[] pixels =
            {
                Color.red, Color.green,
                Color.blue, Color.white
            };
            var image = new TexturePaintReadOnlyImage("surface", TexturePaintChannel.Albedo,
                2, 2, false, pixels);

            Assert.That(image.GetPixelBilinear(0.25f, 0.25f), Is.EqualTo(Color.red));
            Assert.That(image.GetPixelBilinear(0.75f, 0.25f), Is.EqualTo(Color.green));
            Assert.That(image.GetPixelBilinear(0.25f, 0.75f), Is.EqualTo(Color.blue));
            Assert.That(image.GetPixelBilinear(0.75f, 0.75f), Is.EqualTo(Color.white));
        }

        [Test]
        public void PluginPixelSamplingInterpolatesBetweenTexelCenters()
        {
            var image = new TexturePaintReadOnlyImage("surface", TexturePaintChannel.Albedo,
                2, 1, false, new[] { Color.black, Color.white });

            Color center = image.GetPixelBilinear(0.5f, 0.5f);
            Assert.That(center.r, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(center.g, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(center.b, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(center.a, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void AgifyCurvatureContrastRestrictsWeakEdgeCoverage()
        {
            const float weakCurvature = 0.25f;
            float sharpened = AgifyGeneratorPlugin.ShapeCurvature(weakCurvature, 2.5f);
            float broadened = AgifyGeneratorPlugin.ShapeCurvature(weakCurvature, 0.5f);

            Assert.That(sharpened, Is.LessThan(weakCurvature));
            Assert.That(broadened, Is.GreaterThan(weakCurvature));
        }

        [Test]
        public void AgifyReadsTangentNormalDetailAsSignedCurvature()
        {
            const int size = 5;
            Color flat = new Color(0.5f, 0.5f, 1f, 1f);
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = flat;
            int center = 2;
            pixels[center * size + center - 1] = EncodeNormal(new Vector3(-0.5f, 0f, 1f));
            pixels[center * size + center + 1] = EncodeNormal(new Vector3(0.5f, 0f, 1f));
            pixels[(center - 1) * size + center] = EncodeNormal(new Vector3(0f, -0.5f, 1f));
            pixels[(center + 1) * size + center] = EncodeNormal(new Vector3(0f, 0.5f, 1f));
            var normal = new TexturePaintReadOnlyImage("surface", TexturePaintChannel.Normal,
                size, size, false, pixels);

            Assert.That(AgifyGeneratorPlugin.CalculateNormalCurvature(normal, 0.5f, 0.5f),
                Is.GreaterThan(0.2f));
        }

        [Test]
        public async Task AgifyCreatesUndoableWeatheringLayerFromConvexGeometry()
        {
            mesh.normals = new[]
            {
                Vector3.forward,
                (Vector3.forward + Vector3.right * 0.35f).normalized,
                (Vector3.forward + Vector3.up * 0.35f).normalized
            };
            AgifyGeneratorPlugin plugin = ScriptableObject.CreateInstance<AgifyGeneratorPlugin>();
            try
            {
                TexturePaintPluginParameterSet parameters = host.GetParameters(plugin);
                parameters.Get("breakup").number = 0f;
                parameters.Get("aoInfluence").number = 0f;
                parameters.Get("normalCurvature").number = 0f;
                parameters.Get("dirtAmount").number = 0f;
                parameters.Get("wearAmount").number = 1f;
                await host.ExecuteCommandAsync(plugin, store, parameters, null, CancellationToken.None);

                Assert.That(set.layers.Count, Is.EqualTo(1));
                Assert.That(set.layers[0].pluginId, Is.EqualTo("com.uma.texturepaint.agify"));
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Albedo), Is.True);
                Assert.That(set.layers[0].channels.ContainsKey(TexturePaintChannel.Roughness), Is.True);
                Assert.That(Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front, 1, 1).a,
                    Is.GreaterThan(0.01f));
                Assert.That(host.Undo(), Is.True);
                Assert.That(set.layers.Count, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public void WeatheringPluginsExposeIndependentSpreadAndFractalControls()
        {
            DirtifyGeneratorPlugin dirt =
                ScriptableObject.CreateInstance<DirtifyGeneratorPlugin>();
            EdgeWearGeneratorPlugin wear =
                ScriptableObject.CreateInstance<EdgeWearGeneratorPlugin>();
            AgifyGeneratorPlugin agify =
                ScriptableObject.CreateInstance<AgifyGeneratorPlugin>();
            try
            {
                Assert.That(dirt.Descriptor.id, Is.EqualTo("com.uma.texturepaint.dirtify"));
                Assert.That(wear.Descriptor.id, Is.EqualTo("com.uma.texturepaint.edgewear"));
                Assert.That(dirt.Descriptor.parameters.Exists(p => p.id == "featureSize"), Is.True);
                Assert.That(dirt.Descriptor.parameters.Exists(p => p.id == "spread"), Is.True);
                Assert.That(dirt.Descriptor.parameters.Exists(p => p.id == "detectionLevel"), Is.True);
                Assert.That(dirt.Descriptor.parameters.Exists(p => p.id == "fractalLevels"), Is.True);
                Assert.That(dirt.Descriptor.parameters.Exists(p => p.id == "fractalPersistence"), Is.True);
                Assert.That(dirt.Descriptor.parameters.Exists(p => p.id == "fractalEdge"), Is.True);
                Assert.That(wear.Descriptor.parameters.Exists(p => p.id == "featureSize"), Is.True);
                Assert.That(wear.Descriptor.parameters.Exists(p => p.id == "spread"), Is.True);
                Assert.That(agify.Descriptor.parameters.Exists(p => p.id == "fractalEdge"), Is.True);
                Assert.That(agify.Descriptor.parameters.Exists(p => p.id == "fractalLevels"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(agify);
                UnityEngine.Object.DestroyImmediate(wear);
                UnityEngine.Object.DestroyImmediate(dirt);
            }
        }

        [Test]
        public async Task DirtifyAndEdgeWearGenerateFromOppositeSignedCurvature()
        {
            DirtifyGeneratorPlugin dirt =
                ScriptableObject.CreateInstance<DirtifyGeneratorPlugin>();
            EdgeWearGeneratorPlugin wear =
                ScriptableObject.CreateInstance<EdgeWearGeneratorPlugin>();
            try
            {
                mesh.normals = new[]
                {
                    Vector3.forward,
                    (Vector3.forward - Vector3.right * 0.35f).normalized,
                    (Vector3.forward - Vector3.up * 0.35f).normalized
                };
                TexturePaintPluginParameterSet dirtParameters = host.GetParameters(dirt);
                ConfigureDeterministicWeathering(dirtParameters);
                await host.ExecuteCommandAsync(dirt, store, dirtParameters, null,
                    CancellationToken.None);
                Assert.That(set.layers, Has.Count.EqualTo(1));
                Assert.That(set.layers[0].pluginId,
                    Is.EqualTo("com.uma.texturepaint.dirtify"));
                Assert.That(Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front,
                    1, 1).a, Is.GreaterThan(0.01f));
                Assert.That(host.Undo(), Is.True);

                mesh.normals = new[]
                {
                    Vector3.forward,
                    (Vector3.forward + Vector3.right * 0.35f).normalized,
                    (Vector3.forward + Vector3.up * 0.35f).normalized
                };
                // This test mutates mesh geometry in place. Production reconstruction replaces the
                // surface/cache as a unit, so explicitly invalidate the prior concave mesh maps
                // before asking the second generator to observe the new convex normals.
                set.proceduralMeshMaps?.Dispose();
                set.proceduralMeshMaps = null;
                TexturePaintPluginParameterSet wearParameters = host.GetParameters(wear);
                ConfigureDeterministicWeathering(wearParameters);
                await host.ExecuteCommandAsync(wear, store, wearParameters, null,
                    CancellationToken.None);
                Assert.That(set.layers, Has.Count.EqualTo(1));
                Assert.That(set.layers[0].pluginId,
                    Is.EqualTo("com.uma.texturepaint.edgewear"));
                Assert.That(Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front,
                    1, 1).a, Is.GreaterThan(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wear);
                UnityEngine.Object.DestroyImmediate(dirt);
            }
        }

        [Test]
        public async Task AAAOrganicGeneratorsExposeProductionSchemasAndGenerateUndoably()
        {
            var plugins = new ITexturePaintGeneratorV2[]
            {
                ScriptableObject.CreateInstance<FabricFuzzGeneratorPlugin>(),
                ScriptableObject.CreateInstance<RustCorrosionGeneratorPlugin>(),
                ScriptableObject.CreateInstance<SurfaceMicroDetailGeneratorPlugin>(),
                ScriptableObject.CreateInstance<VeinsSubdermalGeneratorPlugin>(),
                ScriptableObject.CreateInstance<ScarWoundGeneratorPlugin>(),
                ScriptableObject.CreateInstance<CreatureSkinGeneratorPlugin>(),
                ScriptableObject.CreateInstance<ScratchDentGeneratorPlugin>()
            };
            Texture2D guide = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            guide.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            guide.Apply(false, false);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < plugins.Length; i++)
                {
                    ITexturePaintGeneratorV2 plugin = plugins[i];
                    TexturePaintPluginDescriptor descriptor = plugin.Descriptor;
                    Assert.That(ids.Add(descriptor.id), Is.True,
                        "AAA generator ids must be unique and stable.");
                    Assert.That(descriptor.capabilities.HasFlag(
                        TexturePaintPluginCapability.ReadsMeshMaps), Is.True);
                    Assert.That(descriptor.parameters.Exists(parameter =>
                        parameter.type == TexturePaintPluginParameterType.Header), Is.True,
                        descriptor.displayName + " must organize its production controls into sections.");
                    Assert.That(descriptor.parameters.Exists(parameter =>
                        parameter.id == "controlMask"), Is.True);
                    var parameterIds = new HashSet<string>(StringComparer.Ordinal);
                    for (int parameterIndex = 0; parameterIndex < descriptor.parameters.Count;
                         parameterIndex++)
                        Assert.That(parameterIds.Add(descriptor.parameters[parameterIndex].id),
                            Is.True, descriptor.displayName + " has a duplicate parameter id: " +
                                     descriptor.parameters[parameterIndex].id);

                    TexturePaintPluginParameterSet parameters = host.GetParameters(plugin);
                    if (plugin is FabricFuzzGeneratorPlugin)
                    {
                        parameters.Get("density").number = 1f;
                        parameters.Get("edgeAmount").number = 0f;
                    }
                    else if (plugin is RustCorrosionGeneratorPlugin)
                    {
                        parameters.Get("spread").number = 1f;
                        parameters.Get("pitting").number = 0f;
                    }
                    else if (plugin is SurfaceMicroDetailGeneratorPlugin)
                    {
                        parameters.Get("noiseAmount").number = 1f;
                    }
                    else if (plugin is VeinsSubdermalGeneratorPlugin ||
                             plugin is CreatureSkinGeneratorPlugin)
                    {
                        parameters.Get("surfaceMode").number = 1f;
                    }
                    else if (plugin is ScarWoundGeneratorPlugin)
                    {
                        parameters.Get("guideSource").number = 2f;
                        parameters.Get("guideTexture").texture = guide;
                        parameters.Get("guideThreshold").number = 0f;
                    }
                    else if (plugin is ScratchDentGeneratorPlugin)
                    {
                        parameters.Get("preset").number = 3f;
                        parameters.Get("scale").number = 0.05f;
                        parameters.Get("dentAmount").number = 1f;
                        parameters.Get("dentDensity").number = 1f;
                        parameters.Get("dentSizeMin").number = 0.9f;
                        parameters.Get("dentSizeMax").number = 1.25f;
                    }

                    await host.ExecuteCommandAsync(plugin, store, parameters, null,
                        CancellationToken.None);
                    Assert.That(set.layers, Has.Count.EqualTo(1), descriptor.displayName);
                    TexturePaintLayer generated = set.layers[0];
                    Assert.That(generated.pluginId, Is.EqualTo(descriptor.id));
                    Assert.That(generated.channels.Count, Is.GreaterThanOrEqualTo(2));
                    Assert.That(HasVisibleOutput(generated), Is.True,
                        descriptor.displayName + " produced no visible pixels.");
                    Assert.That(host.Undo(), Is.True);
                    Assert.That(set.layers, Is.Empty);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(guide);
                for (int i = 0; i < plugins.Length; i++)
                    UnityEngine.Object.DestroyImmediate((ScriptableObject)plugins[i]);
            }
        }

        [Test]
        public void ScarGeneratorDocumentsAndReadsTheCustomRibbonGuide()
        {
            ScarWoundGeneratorPlugin plugin =
                ScriptableObject.CreateInstance<ScarWoundGeneratorPlugin>();
            try
            {
                Assert.That(plugin.Descriptor.readChannels.HasFlag(TexturePaintChannelMask.Custom),
                    Is.True);
                TexturePaintPluginParameterDefinition source =
                    plugin.Descriptor.parameters.Find(parameter => parameter.id == "guideSource");
                Assert.NotNull(source);
                Assert.That(source.enumOptions, Does.Contain("Custom Ribbon Channel"));
                Assert.That(plugin.Descriptor.description, Does.Contain("ribbon").IgnoreCase);
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public void ScratchDentGeneratorExposesCombatMorphologyAndPbrChannels()
        {
            ScratchDentGeneratorPlugin plugin =
                ScriptableObject.CreateInstance<ScratchDentGeneratorPlugin>();
            try
            {
                TexturePaintPluginDescriptor descriptor = plugin.Descriptor;
                Assert.That(descriptor.id, Is.EqualTo("com.uma.texturepaint.scratch-dent"));
                Assert.That(descriptor.declaredChannels.HasFlag(TexturePaintChannelMask.Albedo), Is.True);
                Assert.That(descriptor.declaredChannels.HasFlag(TexturePaintChannelMask.Roughness), Is.True);
                Assert.That(descriptor.declaredChannels.HasFlag(TexturePaintChannelMask.Metallic), Is.True);
                Assert.That(descriptor.declaredChannels.HasFlag(TexturePaintChannelMask.AmbientOcclusion), Is.True);
                Assert.That(descriptor.declaredChannels.HasFlag(TexturePaintChannelMask.NormalControl), Is.True);
                string[] requiredControls =
                {
                    "dentDepth", "dentRimHeight", "pingDepth", "pingRimHeight",
                    "scratchLengthMin", "scratchLengthMax", "scratchLipHeight",
                    "scrapeCount", "edgeBias", "chipAmount", "armorFinish"
                };
                for (int i = 0; i < requiredControls.Length; i++)
                    Assert.That(descriptor.parameters.Exists(parameter =>
                        parameter.id == requiredControls[i]), Is.True, requiredControls[i]);
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public async Task ClothTextureGeneratorCreatesOrderedPlaidAndOptionalMaterialChannels()
        {
            ClothTextureGeneratorPlugin plugin =
                ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>();
            try
            {
                TexturePaintPluginDescriptor descriptor = plugin.Descriptor;
                Assert.That(descriptor.id, Is.EqualTo("com.uma.texturepaint.cloth-texture"));
                Assert.That(descriptor.declaredChannels, Is.EqualTo(
                    TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Roughness |
                    TexturePaintChannelMask.NormalControl));
                TexturePaintPluginParameterDefinition weave = descriptor.parameters.Find(
                    parameter => parameter.id == "weave");
                Assert.That(weave.enumOptions, Is.EqualTo(new[]
                {
                    "Cotton / Plain", "Knit", "Twill", "Corduroy", "Herringbone",
                    "Denim", "Canvas", "Linen", "Satin", "Basket", "Houndstooth",
                    "Leno", "Dobby", "Pile", "Crepe", "Jacquard"
                }));
                Assert.That(descriptor.parameters.Exists(parameter =>
                    parameter.type == TexturePaintPluginParameterType.StripeList), Is.True);
                Assert.That(descriptor.parameters.Exists(parameter =>
                    parameter.type == TexturePaintPluginParameterType.Sprite), Is.True);

                TexturePaintPluginParameterSet parameters = host.CreateParameters(plugin);
                parameters.Get("baseColor").color = new Color(.15f, .15f, .15f, 1f);
                parameters.Get("threadColorAmount").number = 0f;
                parameters.Get("fiberColorVariation").number = 0f;
                parameters.Get("wearAmount").number = 0f;
                parameters.Get("stripeRepeatX").number = 1f;
                parameters.Get("stripeRepeatY").number = 1f;
                List<TexturePaintStripeDefinition> stripes = parameters.Stripes("stripeList");
                stripes.Add(new TexturePaintStripeDefinition
                {
                    direction = TexturePaintStripeDirection.Vertical, position = .25f,
                    width = .25f, softness = .001f, color = Color.red
                });
                stripes.Add(new TexturePaintStripeDefinition
                {
                    direction = TexturePaintStripeDirection.Vertical, position = .6875f,
                    width = .1f, softness = .001f, color = Color.black
                });
                stripes.Add(new TexturePaintStripeDefinition
                {
                    direction = TexturePaintStripeDirection.Horizontal, position = .4375f,
                    width = .2f, softness = .001f, color = Color.blue
                });

                host.SnapshotMemoryBudgetBytes = 1;
                await host.ExecuteCommandAsync(plugin, store, parameters, null,
                    CancellationToken.None);

                Assert.That(set.layers, Has.Count.EqualTo(1));
                TexturePaintLayer generated = set.layers[0];
                Assert.That(generated.channels.Keys, Is.EquivalentTo(new[]
                {
                    TexturePaintChannel.Albedo, TexturePaintChannel.Roughness,
                    TexturePaintChannel.NormalControl
                }));
                Color red = Read(generated.channels[TexturePaintChannel.Albedo].Front, 1, 0);
                Color black = Read(generated.channels[TexturePaintChannel.Albedo].Front, 5, 0);
                Color blueOverRed = Read(generated.channels[TexturePaintChannel.Albedo].Front, 1, 3);
                Assert.That(red.r, Is.GreaterThan(red.b + .5f), "The thick vertical stripe was not generated.");
                Assert.That(black.maxColorComponent, Is.LessThan(.08f), "The thin black stripe was not generated.");
                Assert.That(blueOverRed.b, Is.GreaterThan(blueOverRed.r + .5f),
                    "Later horizontal stripes must layer over earlier vertical stripes for plaid.");
                Assert.That(Read(generated.channels[TexturePaintChannel.Roughness].Front, 1, 1).a,
                    Is.GreaterThan(.9f));
                Color height = Read(generated.channels[TexturePaintChannel.NormalControl].Front, 1, 1);
                Assert.That(height.r, Is.EqualTo(height.g).Within(.01f));
                Assert.That(height.g, Is.EqualTo(height.b).Within(.01f));

                Assert.That(host.Undo(), Is.True);
                parameters.Get("outputRoughness").boolean = false;
                parameters.Get("outputNormalControl").boolean = false;
                await host.ExecuteCommandAsync(plugin, store, parameters, null,
                    CancellationToken.None);
                Assert.That(set.layers[0].channels.Keys,
                    Is.EquivalentTo(new[] { TexturePaintChannel.Albedo }),
                    "Disabled cloth outputs must not create hidden channel payloads.");
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public async Task ClothTexturePatternSpriteUsesOnlyItsAtlasRect()
        {
            ClothTextureGeneratorPlugin plugin =
                ScriptableObject.CreateInstance<ClothTextureGeneratorPlugin>();
            Texture2D atlas = new Texture2D(4, 2, TextureFormat.RGBA32, false, true);
            Sprite sprite = null;
            try
            {
                atlas.SetPixels(new[]
                {
                    Color.green, Color.green, Color.magenta, Color.magenta,
                    Color.green, Color.green, Color.magenta, Color.magenta
                });
                atlas.Apply(false, false);
                sprite = Sprite.Create(atlas, new Rect(2f, 0f, 2f, 2f),
                    new Vector2(.5f, .5f), 100f);
                TexturePaintPluginParameterSet parameters = host.CreateParameters(plugin);
                parameters.Get("outputRoughness").boolean = false;
                parameters.Get("outputNormalControl").boolean = false;
                parameters.Get("patternSprite").sprite = sprite;
                parameters.Get("patternOpacity").number = 1f;
                parameters.Get("usePatternColor").boolean = true;
                parameters.Get("patternTiling").number = 1f;

                await host.ExecuteCommandAsync(plugin, store, parameters, null,
                    CancellationToken.None);

                Color result = Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front,
                    1, 1);
                Assert.That(result.r, Is.GreaterThan(.8f));
                Assert.That(result.b, Is.GreaterThan(.8f));
                Assert.That(result.g, Is.LessThan(.2f),
                    "The sprite parameter must crop its atlas rect instead of sampling neighboring sprites.");
            }
            finally
            {
                if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(plugin);
            }
        }

        private static void ConfigureDeterministicWeathering(
            TexturePaintPluginParameterSet parameters)
        {
            parameters.Get("normalCurvature").number = 0f;
            parameters.Get("featureSize").number = 0f;
            parameters.Get("detectionLevel").number = 0f;
            parameters.Get("spread").number = 0f;
            parameters.Get("amount").number = 1f;
            parameters.Get("cavityInfluence").number = 0f;
            parameters.Get("breakup").number = 0f;
            parameters.Get("fractalEdge").number = 0f;
        }

        [Test]
        public async Task PluginLayerRegenerationAtomicallyReplacesCachedOutputAndIsUndoable()
        {
            TexturePaintLayer cached = set.AddPluginLayer("Weathering");
            cached.pluginId = "com.uma.tests.solid";
            cached.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget(
                "Cached Albedo", 8, 8, RenderTextureFormat.ARGB32, null, Color.blue);
            cached.GetChannelSettings(TexturePaintChannel.Albedo);
            var destinations = new Dictionary<TextureSet, TexturePaintLayer> { { set, cached } };
            SolidPlugin plugin = new SolidPlugin(TexturePaintChannel.Albedo,
                TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 8, 8));

            await host.ExecutePluginLayerAsync(plugin, store, new TexturePaintPluginParameterSet(),
                destinations, null, CancellationToken.None);

            Assert.That(set.layers, Has.Count.EqualTo(1));
            TexturePaintLayer generated = set.layers[0];
            Assert.That(generated, Is.Not.SameAs(cached));
            Assert.That(generated.kind, Is.EqualTo(TexturePaintLayerKind.Plugin));
            Assert.That(generated.pluginStale, Is.False);
            Assert.That(Read(generated.channels[TexturePaintChannel.Albedo].Front, 1, 1).r,
                Is.GreaterThan(0.9f));
            Assert.That(host.Undo(), Is.True);
            Assert.That(set.layers[0], Is.SameAs(cached));
            Assert.That(Read(cached.channels[TexturePaintChannel.Albedo].Front, 1, 1).b,
                Is.GreaterThan(0.9f));
            Assert.That(host.Redo(), Is.True);
            Assert.That(set.layers[0], Is.SameAs(generated));
        }

        [Test]
        public void FailedPluginLayerRegenerationRetainsPreviousCachedLayer()
        {
            TexturePaintLayer cached = set.AddPluginLayer("Cached");
            cached.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget(
                "Cached Albedo", 8, 8, RenderTextureFormat.ARGB32, null, Color.blue);
            cached.GetChannelSettings(TexturePaintChannel.Albedo);
            var destinations = new Dictionary<TextureSet, TexturePaintLayer> { { set, cached } };
            ThrowingLayerPlugin plugin = new ThrowingLayerPlugin();
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("Overlay Painter plugin com\\.uma\\.tests\\.throwing-layer"));

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await host.ExecutePluginLayerAsync(plugin, store,
                    new TexturePaintPluginParameterSet(), destinations, null,
                    CancellationToken.None));

            Assert.That(set.layers[0], Is.SameAs(cached));
            Assert.That(Read(cached.channels[TexturePaintChannel.Albedo].Front, 1, 1).b,
                Is.GreaterThan(0.9f));
            Assert.That(cached.pluginStale, Is.True);
            Assert.That(cached.pluginLastError, Does.Contain("Deliberate"));
            Assert.That(host.CanUndo, Is.False);
        }

        [Test]
        public void DrippingCorrosionUsesGpuContractAndMeterScaledDefaults()
        {
            DrippingCorrosionGeneratorPlugin plugin =
                ScriptableObject.CreateInstance<DrippingCorrosionGeneratorPlugin>();
            try
            {
                Assert.That(plugin, Is.InstanceOf<ITexturePaintGpuGeneratorV2>());
                Assert.That(((ITexturePaintGpuGeneratorV2)plugin).GpuKernelName,
                    Is.EqualTo("CSDrippingCorrosion"));
                var parameterIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (TexturePaintPluginParameterDefinition parameter in
                         plugin.Descriptor.parameters)
                {
                    Assert.That(parameter.id, Is.Not.Null.And.Not.Empty);
                    Assert.That(parameterIds.Add(parameter.id), Is.True,
                        $"Duplicate Dripping Corrosion parameter id: {parameter.id}");
                }
                host.Discover();
                Assert.That(host.FindCommand(plugin.Descriptor.id), Is.Not.Null,
                    "Dripping Corrosion must pass normal PluginHost registration.");
                Assert.That((plugin.Descriptor.requiredMeshMaps &
                    TexturePaintMeshMapMask.WorldPosition) != 0, Is.True);
                TexturePaintPluginParameterSet values = host.CreateParameters(plugin);
                Assert.That(values.Float("dripLengthMeters"), Is.EqualTo(0.22f).Within(0.0001f));
                Assert.That(values.Float("dripWidthMeters"), Is.EqualTo(0.006f).Within(0.0001f));
                Assert.That(values.Float("corrosionSpreadMeters"),
                    Is.EqualTo(0.012f).Within(0.0001f));
                Assert.That(values.Float("gravityY"), Is.EqualTo(-1f));
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public void OrganicGeneratorsAvoidUnneededChannelSnapshots()
        {
            ScriptableObject[] generators =
            {
                ScriptableObject.CreateInstance<FabricFuzzGeneratorPlugin>(),
                ScriptableObject.CreateInstance<RustCorrosionGeneratorPlugin>(),
                ScriptableObject.CreateInstance<SurfaceMicroDetailGeneratorPlugin>(),
                ScriptableObject.CreateInstance<VeinsSubdermalGeneratorPlugin>(),
                ScriptableObject.CreateInstance<CreatureSkinGeneratorPlugin>(),
                ScriptableObject.CreateInstance<ScratchDentGeneratorPlugin>()
            };
            try
            {
                for (int i = 0; i < generators.Length; i++)
                {
                    var usage = generators[i] as ITexturePaintDynamicChannelUsageV2;
                    Assert.That(usage, Is.Not.Null);
                    Assert.That(usage.ResolveReadChannels(new TexturePaintPluginParameterSet()),
                        Is.EqualTo(TexturePaintChannelMask.None));
                }
            }
            finally
            {
                for (int i = 0; i < generators.Length; i++)
                    UnityEngine.Object.DestroyImmediate(generators[i]);
            }
        }

        [Test]
        public void ProductionFiltersExposeStableContractsAndMaskCompatibility()
        {
            ScriptableObject[] filters =
            {
                ScriptableObject.CreateInstance<LevelsCurvesFilterPlugin>(),
                ScriptableObject.CreateInstance<NormalHeightFilterPlugin>(),
                ScriptableObject.CreateInstance<BlurSharpenDetailFilterPlugin>(),
                ScriptableObject.CreateInstance<ChannelOperationsFilterPlugin>(),
                ScriptableObject.CreateInstance<MorphologyDistanceFilterPlugin>()
            };
            try
            {
                var ids = new HashSet<string>();
                for (int i = 0; i < filters.Length; i++)
                {
                    ITexturePaintFilterV2 filter = (ITexturePaintFilterV2)filters[i];
                    Assert.That(ids.Add(filter.Descriptor.id), Is.True);
                    Assert.That(filter.Descriptor.id, Does.StartWith("com.uma.texturepaint.filter."));
                    Assert.That(filter.Descriptor.declaredChannels,
                        Is.EqualTo(TexturePaintChannelMask.All));
                    Assert.That(filter.Descriptor.parameters.Exists(p => p.id == "sourceChannel"),
                        Is.True);
                    Assert.That(filter is ITexturePaintDynamicChannelUsageV2, Is.True);
                }
                Assert.That((((ITexturePaintFilterV2)filters[0]).Descriptor.supportedTargets &
                    TexturePaintPluginTarget.LayerMask) != 0, Is.True);
                Assert.That(((ITexturePaintFilterV2)filters[1]).Descriptor.supportedTargets,
                    Is.EqualTo(TexturePaintPluginTarget.LayerContent));
                Assert.That((((ITexturePaintFilterV2)filters[2]).Descriptor.supportedTargets &
                    TexturePaintPluginTarget.LayerMask) != 0, Is.True);
                Assert.That((((ITexturePaintFilterV2)filters[3]).Descriptor.supportedTargets &
                    TexturePaintPluginTarget.LayerMask) != 0, Is.True);
                Assert.That(((ITexturePaintFilterV2)filters[4]).Descriptor.requiredMeshMaps,
                    Is.EqualTo(TexturePaintMeshMapMask.SurfaceId));
            }
            finally
            {
                for (int i = 0; i < filters.Length; i++)
                    UnityEngine.Object.DestroyImmediate(filters[i]);
            }
        }

        [Test]
        public async Task TextileSurfaceModesGenerateCoordinatedPbrAndMaskCoverage()
        {
            var plugin = ScriptableObject.CreateInstance<TextileSurfaceGeneratorPlugin>();
            Texture2D atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                atlas.SetPixels(new[] { Color.white, Color.clear, Color.white, Color.clear });
                atlas.Apply(false, false);
                Assert.That(plugin.Descriptor.id, Is.EqualTo("com.uma.texturepaint.textile-surface"));
                Assert.That(plugin.Descriptor.supportedTargets, Is.EqualTo(TexturePaintPluginTarget.All));
                string[] modes = { "Quilt", "Embroidery", "Perforation", "Atlas Scatter" };
                Assert.That(plugin.Descriptor.parameters.Find(p => p.id == "mode").enumOptions,
                    Is.EqualTo(modes));

                for (int mode = 0; mode < modes.Length; mode++)
                {
                    TexturePaintPluginParameterSet p = host.CreateParameters(plugin);
                    p.Get("mode").number = mode;
                    if (mode == 3) p.Get("atlas").texture = atlas;
                    await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                    TexturePaintLayer layer = set.layers[0];
                    Assert.That(layer.channels.Keys, Does.Contain(TexturePaintChannel.Albedo));
                    Assert.That(layer.channels.Keys, Does.Contain(TexturePaintChannel.NormalControl));
                    Assert.That(HasVisibleOutput(layer), Is.True, modes[mode]);
                    Assert.That(host.Undo(), Is.True);
                }

                TexturePaintLayer masked = set.AddLayer("Textile Mask");
                set.AddLayerMask(masked, 0f);
                var destinations = new Dictionary<TextureSet, TexturePaintLayer> { { set, masked } };
                TexturePaintPluginParameterSet maskParameters = host.CreateParameters(plugin);
                maskParameters.Get("mode").number = 2;
                await host.ExecuteLayerMaskAsync(plugin, store, maskParameters, destinations,
                    null, CancellationToken.None);
                Color mask = Read(set.layers[0].layerMask.target.Front, 4, 4);
                Assert.That(mask.r, Is.EqualTo(mask.g).Within(.001f));
                Assert.That(mask.g, Is.EqualTo(mask.b).Within(.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(plugin);
            }
        }

        [Test]
        public async Task StylizationFilterQuantizesAndExposesKuwaharaProductionControls()
        {
            var plugin = ScriptableObject.CreateInstance<StylizationFilterPlugin>();
            Texture2D gradient = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color[64];
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    pixels[y * 8 + x] = new Color(x / 7f, y / 7f, (x + y) / 14f, 1f);
                gradient.SetPixels(pixels); gradient.Apply(false, false);
                set.GetChannel(TexturePaintChannel.Albedo).editable.Reset(gradient, Color.black);
                Assert.That(plugin.Descriptor.id, Is.EqualTo(
                    "com.uma.texturepaint.filter.stylization"));
                Assert.That(plugin.Descriptor.parameters.Exists(p => p.id == "radius"), Is.True);
                Assert.That(plugin.Descriptor.parameters.Exists(p => p.id == "quality"), Is.True);
                Assert.That(plugin.Descriptor.parameters.Find(p => p.id == "operation").enumOptions,
                    Does.Contain("Custom Palette"));

                TexturePaintPluginParameterSet p = host.CreateParameters(plugin);
                p.Get("operation").number = 1; p.Get("levels").number = 2;
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                Color result = Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front, 3, 5);
                Assert.That(Mathf.Min(Mathf.Abs(result.r), Mathf.Abs(result.r - 1f)), Is.LessThan(.03f));
                Assert.That(Mathf.Min(Mathf.Abs(result.g), Mathf.Abs(result.g - 1f)), Is.LessThan(.03f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gradient);
                UnityEngine.Object.DestroyImmediate(plugin);
            }
        }

        [Test]
        public async Task TextGeneratorSupportsFontStyleMaterialOutputsRibbonGuideAndGroupMask()
        {
            var plugin = ScriptableObject.CreateInstance<TextGeneratorPlugin>();
            Texture2D guide = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            try
            {
                TexturePaintPluginDescriptor descriptor = plugin.Descriptor;
                Assert.That(descriptor.parameters.Exists(p => p.type ==
                    TexturePaintPluginParameterType.Font), Is.True);
                Assert.That(descriptor.parameters.Find(p => p.id == "text").type,
                    Is.EqualTo(TexturePaintPluginParameterType.MultilineString));
                Assert.That(descriptor.parameters.Find(p => p.id == "fontStyle").enumOptions,
                    Is.EqualTo(new[] { "Normal", "Bold", "Italic", "Bold + Italic" }));
                Assert.That(descriptor.declaredChannels, Is.EqualTo(
                    TexturePaintChannelMask.Albedo | TexturePaintChannelMask.NormalControl |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.Metallic));
                Assert.That(descriptor.readChannels, Is.EqualTo(TexturePaintChannelMask.Custom));

                TexturePaintPluginParameterSet p = host.CreateParameters(plugin);
                p.Get("text").text = "A"; p.Get("fontSize").number = 6;
                p.Get("outputNormalControl").boolean = true;
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                Assert.That(set.layers[0].channels.Keys, Does.Contain(TexturePaintChannel.Albedo));
                Assert.That(set.layers[0].channels.Keys, Does.Contain(TexturePaintChannel.NormalControl));
                Assert.That(HasVisibleOutput(set.layers[0]), Is.True);
                Assert.That(host.Undo(), Is.True);

                var guidePixels = new Color[64];
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    guidePixels[y * 8 + x] = y >= 2 && y <= 5 ? Color.white : Color.black;
                guide.SetPixels(guidePixels); guide.Apply(false, false);
                set.GetChannel(TexturePaintChannel.Custom).editable.Reset(guide, Color.black);
                p.Get("layout").number = 1;
                await host.ExecuteCommandAsync(plugin, store, p, null, CancellationToken.None);
                Assert.That(HasVisibleOutput(set.layers[0]), Is.True,
                    "Ribbon text did not follow the composed Custom guide.");
                Assert.That(host.Undo(), Is.True);
                p.Get("layout").number = 0;

                TexturePaintLayer masked = set.AddGroup("Text Group");
                set.AddLayerMask(masked, 0f);
                var destinations = new Dictionary<TextureSet, TexturePaintLayer> { { set, masked } };
                await host.ExecuteLayerMaskAsync(plugin, store, p, destinations, null,
                    CancellationToken.None);
                Color sample = Read(set.layers[0].layerMask.target.Front, 4, 4);
                Assert.That(sample.r, Is.EqualTo(sample.g).Within(.001f));
                Assert.That(sample.g, Is.EqualTo(sample.b).Within(.001f));
                Assert.That(set.layers[0].layerMask.pluginId, Is.EqualTo(descriptor.id));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(guide);
                UnityEngine.Object.DestroyImmediate(plugin);
            }
        }

        [Test]
        public void ComplexParametersCloneWithoutSharingCurvesOrStripeEntries()
        {
            var parameters = new TexturePaintPluginParameterSet();
            TexturePaintPluginParameterValue value = parameters.Get("curve", true);
            value.curve = new AnimationCurve(new Keyframe(0f, 0f),
                new Keyframe(0.5f, 0.8f), new Keyframe(1f, 1f));
            parameters.Stripes("stripes").Add(new TexturePaintStripeDefinition
            {
                position = .25f, width = .2f, color = Color.red
            });
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            parameters.Get("font", true).font = font;
            TexturePaintPluginParameterSet clone = parameters.Clone();
            clone.Curve("curve").MoveKey(1, new Keyframe(0.5f, 0.2f));
            clone.Stripes("stripes")[0].width = .75f;
            clone.Stripes("stripes")[0].color = Color.blue;

            Assert.That(parameters.Curve("curve").Evaluate(0.5f), Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(clone.Curve("curve").Evaluate(0.5f), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(parameters.Stripes("stripes")[0].width, Is.EqualTo(.2f));
            Assert.That(parameters.Stripes("stripes")[0].color, Is.EqualTo(Color.red));
            Assert.That(clone.Font("font"), Is.SameAs(font));
        }

        [Test]
        public async Task ChannelOperationsCanAtomicallyGenerateEditableGrayscaleLayerMask()
        {
            TexturePaintLayer layer = set.AddLayer("Masked Paint");
            set.AddLayerMask(layer, 0f);
            var destinations = new Dictionary<TextureSet, TexturePaintLayer> { { set, layer } };
            var plugin = ScriptableObject.CreateInstance<ChannelOperationsFilterPlugin>();
            try
            {
                TexturePaintPluginParameterSet parameters = host.CreateParameters(plugin);
                parameters.Get("operation").number = 0f; // Invert the black mask to white.
                await host.ExecuteLayerMaskAsync(plugin, store, parameters, destinations,
                    null, CancellationToken.None);

                TexturePaintLayer generated = set.layers[0];
                Assert.That(generated, Is.Not.SameAs(layer));
                Assert.That(generated.layerMask.pluginId, Is.EqualTo(plugin.Descriptor.id));
                Assert.That(generated.layerMask.pluginStale, Is.False);
                Color inside = Read(generated.layerMask.target.Front, 1, 1);
                Assert.That(inside.r, Is.GreaterThan(0.95f));
                Assert.That(inside.r, Is.EqualTo(inside.g).Within(0.001f));
                Assert.That(inside.g, Is.EqualTo(inside.b).Within(0.001f));
                Assert.That(host.Undo(), Is.True);
                Assert.That(Read(set.layers[0].layerMask.target.Front, 1, 1).r,
                    Is.LessThan(0.05f));
                Assert.That(host.Redo(), Is.True);
                Assert.That(Read(set.layers[0].layerMask.target.Front, 1, 1).r,
                    Is.GreaterThan(0.95f));
            }
            finally { UnityEngine.Object.DestroyImmediate(plugin); }
        }

        [Test]
        public async Task MorphologyDilatesWithinUvIslandAndWritesScalarData()
        {
            Texture2D seed = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.black;
            pixels[2 * 8 + 2] = Color.white;
            seed.SetPixels(pixels); seed.Apply(false, false);
            TexturePaintLayer destination = set.AddPluginLayer("Morphology");
            var destinations = new Dictionary<TextureSet, TexturePaintLayer> { { set, destination } };
            var plugin = ScriptableObject.CreateInstance<MorphologyDistanceFilterPlugin>();
            try
            {
                set.GetChannel(TexturePaintChannel.Albedo).editable.Reset(seed, Color.black);
                set.RecomposeAll();
                TexturePaintPluginParameterSet parameters = host.CreateParameters(plugin);
                parameters.Get("sourceChannel").number = (int)TexturePaintChannel.Albedo;
                parameters.Get("destinationChannel").number = (int)TexturePaintChannel.NormalControl;
                parameters.Get("operation").number = 0f;
                parameters.Get("radius").number = 1.5f;
                parameters.Get("softness").number = 0.01f;
                await host.ExecutePluginLayerAsync(plugin, store, parameters, destinations,
                    null, CancellationToken.None);

                EditableTextureTarget output = set.layers[0].channels[TexturePaintChannel.NormalControl];
                Assert.That(Read(output.Front, 2, 2).r, Is.GreaterThan(0.95f));
                Assert.That(Read(output.Front, 3, 2).r, Is.GreaterThan(0.95f),
                    "Dilation should cross triangle texels that belong to the same UV island.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plugin);
                UnityEngine.Object.DestroyImmediate(seed);
            }
        }

        private static TextureChannelTarget MakeChannel(TexturePaintChannel channel, bool sRGB, Color clear)
        {
            return new TextureChannelTarget
            {
                channel = channel, sRGB = sRGB, format = RenderTextureFormat.ARGB32,
                editable = new EditableTextureTarget("Plugin Test " + channel, 8, 8, RenderTextureFormat.ARGB32, null, clear)
            };
        }

        private static Color Read(RenderTexture target, int x, int y)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            texture.ReadPixels(new Rect(x, y, 1, 1), 0, 0); texture.Apply(false, false);
            Color result = texture.GetPixel(0, 0); UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active = previous; return result;
        }

        private static bool HasVisibleOutput(TexturePaintLayer layer)
        {
            foreach (EditableTextureTarget channel in layer.channels.Values)
            {
                RenderTexture target = channel?.Front;
                if (target == null) continue;
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                Texture2D texture = new Texture2D(target.width, target.height,
                    TextureFormat.RGBA32, false, true);
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply(false, false);
                Color[] pixels = texture.GetPixels();
                UnityEngine.Object.DestroyImmediate(texture);
                RenderTexture.active = previous;
                for (int i = 0; i < pixels.Length; i++)
                    if (pixels[i].a > 0.01f) return true;
            }
            return false;
        }

        private static Color EncodeNormal(Vector3 value)
        {
            value.Normalize();
            return new Color(value.x * 0.5f + 0.5f, value.y * 0.5f + 0.5f,
                value.z * 0.5f + 0.5f, 1f);
        }

        private sealed class SolidPlugin : ITexturePaintGeneratorV2
        {
            private readonly TexturePaintChannel channel;
            private readonly TexturePaintPluginColorSpace colorSpace;
            private readonly RectInt rect;
            public TexturePaintPluginDescriptor Descriptor { get; }

            public SolidPlugin(TexturePaintChannel channel, TexturePaintPluginColorSpace colorSpace, RectInt rect,
                TexturePaintChannelMask declared = TexturePaintChannelMask.All)
            {
                this.channel = channel; this.colorSpace = colorSpace; this.rect = rect;
                Descriptor = new TexturePaintPluginDescriptor
                {
                    id = "com.uma.tests.solid", displayName = "Solid", capabilities = TexturePaintPluginCapability.Generator,
                    declaredChannels = declared
                };
            }

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                Color[] pixels = new Color[rect.width * rect.height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.red;
                context.WriteTile("surface", channel, rect, pixels, colorSpace, TexturePaintPluginBlend.Replace);
                return Task.CompletedTask;
            }
        }

        private sealed class WaitingPlugin : ITexturePaintGeneratorV2
        {
            public readonly ManualResetEventSlim started = new ManualResetEventSlim();
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id = "com.uma.tests.waiting",
                displayName = "Waiting",
                capabilities = TexturePaintPluginCapability.Generator,
                declaredChannels = TexturePaintChannelMask.Albedo
            };

            public async Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                Color[] pixels = { Color.magenta };
                context.WriteTile("surface", TexturePaintChannel.Albedo, new RectInt(0, 0, 1, 1), pixels,
                    TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                started.Set();
                await Task.Delay(Timeout.Infinite, context.cancellationToken);
            }
        }

        private sealed class ReadWriteProbePlugin : ITexturePaintGeneratorV2
        {
            public TexturePaintReadOnlyImage roughness;
            public TexturePaintReadOnlyImage albedoPixels;
            public TexturePaintReadOnlyChannelInfo albedoInfo;
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id = "com.uma.tests.read-write-probe",
                displayName = "Read Write Probe",
                capabilities = TexturePaintPluginCapability.Generator,
                declaredChannels = TexturePaintChannelMask.Albedo,
                readChannels = TexturePaintChannelMask.Roughness,
                channelSnapshotMaximumResolution = 4
            };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                roughness = context.source.Get("surface", TexturePaintChannel.Roughness);
                albedoPixels = context.source.Get("surface", TexturePaintChannel.Albedo);
                albedoInfo = context.source.GetChannelInfo("surface", TexturePaintChannel.Albedo);
                Color32[] pixels =
                {
                    new Color32(0, 255, 0, 255), new Color32(0, 255, 0, 255),
                    new Color32(0, 255, 0, 255), new Color32(0, 255, 0, 255)
                };
                context.WriteTileCompact("surface", TexturePaintChannel.Albedo,
                    new RectInt(0, 0, 2, 2), pixels, TexturePaintPluginColorSpace.Linear,
                    TexturePaintPluginBlend.Replace);
                return Task.CompletedTask;
            }
        }

        private sealed class MeshMapProbePlugin : ITexturePaintGeneratorV2
        {
            public TexturePaintReadOnlyMeshMap curvature;
            public TexturePaintReadOnlyMeshMap worldNormal;
            public TexturePaintReadOnlyMeshMap worldPosition;
            public TexturePaintReadOnlyMeshMap ambientOcclusion;
            public TexturePaintReadOnlyMeshMap meshThickness;
            public TexturePaintReadOnlyMeshMap surfaceId;
            public TexturePaintReadOnlyImage thicknessChannel;
            public TexturePaintReadOnlyImage normalControlChannel;
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id = "com.uma.tests.mesh-map-probe",
                displayName = "Mesh Map Probe",
                capabilities = TexturePaintPluginCapability.Generator |
                               TexturePaintPluginCapability.ReadsMeshMaps,
                declaredChannels = TexturePaintChannelMask.Albedo |
                                   TexturePaintChannelMask.Thickness |
                                   TexturePaintChannelMask.NormalControl,
                requiredMeshMaps = TexturePaintMeshMapMask.All
            };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                curvature = context.GetMeshMap("surface", TexturePaintMeshMap.SignedCurvature);
                worldNormal = context.GetMeshMap("surface", TexturePaintMeshMap.WorldNormal);
                worldPosition = context.GetMeshMap("surface", TexturePaintMeshMap.WorldPosition);
                ambientOcclusion = context.GetMeshMap("surface", TexturePaintMeshMap.AmbientOcclusion);
                meshThickness = context.GetMeshMap("surface", TexturePaintMeshMap.Thickness);
                surfaceId = context.GetMeshMap("surface", TexturePaintMeshMap.SurfaceId);
                thicknessChannel = context.source.Get("surface", TexturePaintChannel.Thickness);
                normalControlChannel = context.source.Get("surface", TexturePaintChannel.NormalControl);
                return Task.CompletedTask;
            }
        }

        private sealed class TextureParameterProbePlugin : ITexturePaintGeneratorV2
        {
            public TexturePaintReadOnlyParameterTexture snapshot;
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id = "com.uma.tests.parameter-texture-probe",
                displayName = "Parameter Texture Probe",
                capabilities = TexturePaintPluginCapability.Generator,
                declaredChannels = TexturePaintChannelMask.Albedo,
                parameters = new List<TexturePaintPluginParameterDefinition>
                {
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "source", displayName = "Source",
                        type = TexturePaintPluginParameterType.Texture
                    }
                }
            };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                snapshot = context.GetTextureParameter("source");
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingLayerPlugin : ITexturePaintGeneratorV2
        {
            public TexturePaintPluginDescriptor Descriptor { get; } = new TexturePaintPluginDescriptor
            {
                id = "com.uma.tests.throwing-layer",
                displayName = "Throwing Layer",
                capabilities = TexturePaintPluginCapability.Generator,
                declaredChannels = TexturePaintChannelMask.Albedo
            };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
                throw new InvalidOperationException("Deliberate generation failure.");
        }
    }
}
#endif
