#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
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
        public async Task SuccessfulCommandIsMaskedDirtyAndUndoable()
        {
            TexturePaintMaskStack masks = new TexturePaintMaskStack();
            await host.ExecuteCommandAsync(new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear,
                new RectInt(0, 0, 4, 4)), store, masks, null, null, CancellationToken.None);
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
        public async Task BlackMaskCannotBeBypassedByCommandPlugin()
        {
            TexturePaintMaskStack masks = new TexturePaintMaskStack();
            masks.Add(new TexturePaintMask { kind = TexturePaintMaskKind.Black });
            await host.ExecuteCommandAsync(new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear,
                new RectInt(0, 0, 8, 8)), store, masks, null, null, CancellationToken.None);
            Color pixel = Read(set.layers[0].channels[TexturePaintChannel.Albedo].Front, 1, 1);
            Assert.That(pixel.a, Is.LessThan(0.01f));
        }

        [Test]
        public void UndeclaredChannelFailsBeforeCommit()
        {
            SolidPlugin plugin = new SolidPlugin(TexturePaintChannel.Roughness, TexturePaintPluginColorSpace.Data,
                new RectInt(0, 0, 2, 2), TexturePaintChannelMask.Albedo);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Texture Paint plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(plugin, store,
                new TexturePaintMaskStack(), null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
            Assert.That(host.Diagnostics[host.Diagnostics.Count - 1].severity, Is.EqualTo(TexturePaintPluginDiagnosticSeverity.Error));
        }

        [Test]
        public void DataChannelRejectsColorEncodedPayload()
        {
            SolidPlugin plugin = new SolidPlugin(TexturePaintChannel.Roughness, TexturePaintPluginColorSpace.SRGB,
                new RectInt(0, 0, 2, 2));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Texture Paint plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(plugin, store,
                new TexturePaintMaskStack(), null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
        }

        [Test]
        public void CancellationBeforeSnapshotCannotCommit()
        {
            CancellationTokenSource source = new CancellationTokenSource(); source.Cancel();
            Assert.That(async () => await host.ExecuteCommandAsync(
                new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 2, 2)),
                store, new TexturePaintMaskStack(), null, null, source.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(set.layers.Count, Is.Zero); source.Dispose();
        }

        [Test]
        public void CancellationDuringPluginExecutionCannotCommitQueuedCommands()
        {
            CancellationTokenSource source = new CancellationTokenSource();
            WaitingPlugin plugin = new WaitingPlugin();
            Task execution = host.ExecuteCommandAsync(plugin, store, new TexturePaintMaskStack(), null, null, source.Token);
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
        public void CommandMemoryBudgetRejectsPayloadBeforeCommit()
        {
            host.CommandMemoryBudgetBytes = 1;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Texture Paint plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(
                new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 2, 2)),
                store, new TexturePaintMaskStack(), null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
        }

        [Test]
        public void SnapshotMemoryBudgetRejectsReadBeforePluginRuns()
        {
            host.SnapshotMemoryBudgetBytes = 1;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Texture Paint plugin com\\.uma\\.tests\\.solid: Transaction failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await host.ExecuteCommandAsync(
                new SolidPlugin(TexturePaintChannel.Albedo, TexturePaintPluginColorSpace.Linear, new RectInt(0, 0, 2, 2)),
                store, new TexturePaintMaskStack(), null, null, CancellationToken.None));
            Assert.That(set.layers.Count, Is.Zero);
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
                new RectInt(0, 0, 2, 2)), store, new TexturePaintMaskStack(), null, null, CancellationToken.None);
            Color encoded = Read(set.layers[0].channels[TexturePaintChannel.Normal].Front, 1, 1);
            Vector3 decoded = new Vector3(encoded.r * 2f - 1f, encoded.g * 2f - 1f, encoded.b * 2f - 1f);
            Assert.That(decoded.magnitude, Is.EqualTo(1f).Within(0.02f));
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
    }
}
#endif
