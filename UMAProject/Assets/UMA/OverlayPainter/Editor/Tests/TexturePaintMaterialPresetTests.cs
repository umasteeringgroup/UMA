#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class OrderedMaterialPresetTestGenerator : ITexturePaintGeneratorV2
    {
        public static readonly List<int> Executions = new List<int>();

        public TexturePaintPluginDescriptor Descriptor { get; } =
            new TexturePaintPluginDescriptor
            {
                id = "org.uma.tests.material-preset-order",
                displayName = "Material Preset Order Test",
                pluginVersion = "1.0.0",
                capabilities = TexturePaintPluginCapability.Generator,
                declaredChannels = TexturePaintChannelMask.Albedo,
                supportedTargets = TexturePaintPluginTarget.LayerContent,
                parameters = new List<TexturePaintPluginParameterDefinition>
                {
                    new TexturePaintPluginParameterDefinition
                    {
                        id = "order",
                        displayName = "Order",
                        type = TexturePaintPluginParameterType.Integer,
                        minimum = 1f,
                        maximum = 2f,
                        defaultNumber = 1f
                    }
                }
            };

        public Task ExecuteAsync(TexturePaintCommandContextV2 context)
        {
            Executions.Add(context.parameters.Integer("order"));
            return Task.CompletedTask;
        }
    }

    public sealed class TexturePaintMaterialPresetTests
    {
        [Test]
        public void CompatibilityReportsRequiredMissingChannel()
        {
            TexturePaintMaterialPreset preset = CreatePreset();
            preset.channels.Add(new TexturePaintMaterialPresetChannel
            {
                channel = TexturePaintChannel.Albedo,
                required = true
            });
            var destination = new TextureSet();

            TexturePaintMaterialPresetCompatibility result =
                TexturePaintMaterialPresetStorage.Evaluate(preset,
                    new[] { destination }, null);

            Assert.That(result.CanApply, Is.False);
            Assert.That(result.Summary(), Does.Contain("Albedo"));
            UnityEngine.Object.DestroyImmediate(preset);
            destination.Dispose();
        }

        [Test]
        public async Task ApplyCreatesFreshIndependentHierarchyEachTime()
        {
            TexturePaintMaterialPreset preset = CreatePreset();
            preset.displayName = "Old Leather";
            preset.layers.Add(new TexturePaintDocumentLayer
            {
                id = "child-template",
                parentId = "group-template",
                name = "Leather Fill",
                kind = TexturePaintLayerKind.Paint
            });
            preset.layers.Add(new TexturePaintDocumentLayer
            {
                id = "group-template",
                name = "Old Leather",
                kind = TexturePaintLayerKind.Group
            });
            var destination = new TextureSet();
            var store = new TextureStore();

            TexturePaintMaterialPresetApplyResult first =
                await TexturePaintMaterialPresetStorage.ApplyAsync(preset, store,
                    new[] { destination }, null, null,
                    new TexturePaintMaterialPresetApplyOptions { wrapInGroup = false },
                    null, CancellationToken.None);
            TexturePaintLayer firstChild = destination.layers.Find(layer => layer.name == "Leather Fill");
            TexturePaintLayer firstGroup = destination.layers.Find(layer => layer.name == "Old Leather");

            Assert.That(first.created.Count, Is.EqualTo(2));
            Assert.That(firstChild.parentId, Is.EqualTo(firstGroup.id));
            Assert.That(firstChild.id, Is.Not.EqualTo("child-template"));
            Assert.That(firstChild.logicalLayerId, Is.Not.Empty);
            Assert.That(firstChild.sourceMaterialPresetId, Is.EqualTo(preset.presetId));
            Assert.That(firstChild.sourceMaterialPresetRevision, Is.EqualTo(preset.revision));
            Assert.That(firstChild.sourceMaterialPresetLayerId, Is.EqualTo("child-template"));

            await TexturePaintMaterialPresetStorage.ApplyAsync(preset, store,
                new[] { destination }, null, null,
                new TexturePaintMaterialPresetApplyOptions { wrapInGroup = false },
                null, CancellationToken.None);
            List<TexturePaintLayer> children = destination.layers.FindAll(layer =>
                layer.name == "Leather Fill");
            Assert.That(children.Count, Is.EqualTo(2));
            Assert.That(children[0].id, Is.Not.EqualTo(children[1].id));
            Assert.That(children[0].logicalLayerId, Is.Not.EqualTo(children[1].logicalLayerId));

            UnityEngine.Object.DestroyImmediate(preset);
            destination.Dispose();
            store.Dispose();
        }

        [Test]
        public void CancelledApplyRollsBackEveryCreatedLayer()
        {
            TexturePaintMaterialPreset preset = CreatePreset();
            preset.layers.Add(new TexturePaintDocumentLayer
            {
                id = "one",
                name = "One",
                kind = TexturePaintLayerKind.Group
            });
            var destination = new TextureSet();
            var store = new TextureStore();
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(async () => await TexturePaintMaterialPresetStorage.ApplyAsync(preset,
                    store, new[] { destination }, null, null,
                    new TexturePaintMaterialPresetApplyOptions(), null, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(destination.layers, Is.Empty);

            cancellation.Dispose();
            UnityEngine.Object.DestroyImmediate(preset);
            destination.Dispose();
            store.Dispose();
        }

        [Test]
        public async Task PluginGeneratorsReplayInSavedCompositionOrder()
        {
            OrderedMaterialPresetTestGenerator.Executions.Clear();
            TexturePaintMaterialPreset preset = CreatePreset();
            preset.layers.Add(PluginLayer("lower", 1));
            preset.layers.Add(PluginLayer("upper", 2));
            var destination = new TextureSet();
            var store = new TextureStore();
            var plugins = new PluginHost();
            plugins.Discover();

            await TexturePaintMaterialPresetStorage.ApplyAsync(preset, store,
                new[] { destination }, plugins, null,
                new TexturePaintMaterialPresetApplyOptions { wrapInGroup = false },
                null, CancellationToken.None);

            Assert.That(OrderedMaterialPresetTestGenerator.Executions,
                Is.EqualTo(new[] { 1, 2 }));
            Assert.That(destination.layers.TrueForAll(layer => !layer.pluginStale), Is.True);

            plugins.Dispose();
            UnityEngine.Object.DestroyImmediate(preset);
            destination.Dispose();
            store.Dispose();
        }

        [Test]
        public async Task CaptureAndApplyRoundTripsCachedChannelsMasksAndPluginDefinitions()
        {
            var source = new TextureSet();
            AddChannel(source, TexturePaintChannel.Albedo, 4);
            TexturePaintLayer sourceLayer = source.AddPluginLayer("Weathered Leather");
            sourceLayer.opacity = 0.42f;
            sourceLayer.blendMode = TexturePaintBlendMode.Multiply;
            sourceLayer.pluginId = "org.uma.tests.missing-preset-plugin";
            sourceLayer.pluginVersion = "3.2.1";
            sourceLayer.pluginParameters.Get("wear", true).number = 0.73f;
            sourceLayer.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget(
                "Preset Source", 4, 4, RenderTextureFormat.ARGB32, null, Color.red);
            sourceLayer.GetChannelSettings(TexturePaintChannel.Albedo).opacity = 0.66f;
            TexturePaintLayerMask sourceMask = source.AddLayerMask(sourceLayer, 0.25f);
            sourceMask.pluginId = "org.uma.tests.missing-mask-plugin";
            sourceMask.pluginVersion = "2.0.0";
            sourceMask.pluginParameters.Get("contrast", true).number = 0.9f;

            TexturePaintMaterialPreset preset = CreatePreset();
            TexturePaintMaterialPresetStorage.Capture(preset, source,
                new[] { sourceLayer }, false, null, true);

            Assert.That(preset.layers[0].channels[0].pixels.HasData, Is.True);
            Assert.That(preset.layers[0].maskPixels.HasData, Is.True);
            Assert.That(preset.layers[0].pluginParameters.Float("wear"), Is.EqualTo(0.73f));
            Assert.That(preset.layers[0].maskPluginParameters.Float("contrast"), Is.EqualTo(0.9f));

            var destination = new TextureSet();
            AddChannel(destination, TexturePaintChannel.Albedo, 8);
            var store = new TextureStore();
            await TexturePaintMaterialPresetStorage.ApplyAsync(preset, store,
                new[] { destination }, null, null,
                new TexturePaintMaterialPresetApplyOptions { wrapInGroup = false },
                null, CancellationToken.None);

            TexturePaintLayer applied = destination.layers[0];
            Assert.That(applied.opacity, Is.EqualTo(0.42f));
            Assert.That(applied.blendMode, Is.EqualTo(TexturePaintBlendMode.Multiply));
            Assert.That(applied.channels[TexturePaintChannel.Albedo].Width, Is.EqualTo(8));
            Assert.That(applied.GetChannelSettings(TexturePaintChannel.Albedo).opacity,
                Is.EqualTo(0.66f));
            Assert.That(applied.layerMask, Is.Not.Null);
            Assert.That(applied.layerMask.target.Width, Is.EqualTo(8));
            Assert.That(applied.pluginId, Is.EqualTo("org.uma.tests.missing-preset-plugin"));
            Assert.That(applied.pluginStale, Is.True);
            Assert.That(applied.layerMask.pluginId, Is.EqualTo("org.uma.tests.missing-mask-plugin"));

            UnityEngine.Object.DestroyImmediate(preset);
            source.Dispose();
            destination.Dispose();
            store.Dispose();
        }

        [Test]
        public async Task PackagedPresetEmbedsLooseTextureAndAppliesAfterSourceDeletion()
        {
            const string testFolder =
                "Assets/UMA/OverlayPainter/Editor/Tests/GeneratedMaterialPresetPackageTests";
            const string texturePath = testFolder + "/Loose Texture.asset";
            const string sourcePath = testFolder + "/Source Preset.asset";
            const string packagePath = testFolder + "/Packaged Preset.asset";
            if (!AssetDatabase.IsValidFolder(testFolder))
                AssetDatabase.CreateFolder("Assets/UMA/OverlayPainter/Editor/Tests",
                    "GeneratedMaterialPresetPackageTests");

            var destination = new TextureSet();
            var store = new TextureStore();
            try
            {
                var looseTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                looseTexture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
                looseTexture.Apply(false, false);
                AssetDatabase.CreateAsset(looseTexture, texturePath);

                TexturePaintMaterialPreset source = CreatePreset();
                source.name = "Source Preset";
                source.displayName = "Packaged Leather";
                source.thumbnail = looseTexture;
                source.layers.Add(new TexturePaintDocumentLayer
                {
                    id = "paint-template",
                    name = "Packaged Paint",
                    kind = TexturePaintLayerKind.Paint,
                    paintSettings = new TexturePaintLayerSettings
                    {
                        source = TexturePaintBrushSource.Texture,
                        sourceTexture = looseTexture,
                        brushStamp = looseTexture
                    }
                });
                AssetDatabase.CreateAsset(source, sourcePath);
                AssetDatabase.SaveAssets();

                TexturePaintMaterialPreset packaged =
                    TexturePaintMaterialPresetPackager.Package(source, packagePath);
                Texture2D embeddedTexture = packaged.layers[0].paintSettings.sourceTexture;

                Assert.That(packaged.packaged, Is.True);
                Assert.That(packaged.packagedFromPresetId, Is.EqualTo(source.presetId));
                Assert.That(packaged.presetId, Is.Not.EqualTo(source.presetId));
                Assert.That(packaged.packagedDependencies.Count, Is.EqualTo(1),
                    "Repeated texture references should share one embedded copy.");
                Assert.That(embeddedTexture, Is.Not.SameAs(looseTexture));
                Assert.That(packaged.thumbnail, Is.SameAs(embeddedTexture));
                Assert.That(AssetDatabase.GetAssetPath(embeddedTexture), Is.EqualTo(packagePath));

                Assert.That(AssetDatabase.DeleteAsset(texturePath), Is.True);
                AssetDatabase.ImportAsset(packagePath, ImportAssetOptions.ForceUpdate);
                packaged = AssetDatabase.LoadAssetAtPath<TexturePaintMaterialPreset>(packagePath);
                embeddedTexture = packaged.layers[0].paintSettings.sourceTexture;
                Assert.That(embeddedTexture, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(embeddedTexture), Is.EqualTo(packagePath));

                await TexturePaintMaterialPresetStorage.ApplyAsync(packaged, store,
                    new[] { destination }, null, null,
                    new TexturePaintMaterialPresetApplyOptions { wrapInGroup = false },
                    null, CancellationToken.None);

                Assert.That(destination.layers.Count, Is.EqualTo(1));
                Assert.That(destination.layers[0].paintSettings.sourceTexture,
                    Is.SameAs(embeddedTexture));
                Assert.That(destination.layers[0].paintSettings.brushStamp,
                    Is.SameAs(embeddedTexture));
                Assert.That(AssetDatabase.GetAssetPath(
                        destination.layers[0].paintSettings.sourceTexture),
                    Is.EqualTo(packagePath));
            }
            finally
            {
                destination.Dispose();
                store.Dispose();
                if (AssetDatabase.IsValidFolder(testFolder))
                    AssetDatabase.DeleteAsset(testFolder);
            }
        }

        private static TexturePaintMaterialPreset CreatePreset()
        {
            TexturePaintMaterialPreset preset =
                ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
            preset.layers = new List<TexturePaintDocumentLayer>();
            preset.channels = new List<TexturePaintMaterialPresetChannel>();
            preset.plugins = new List<TexturePaintMaterialPresetPlugin>();
            return preset;
        }

        private static TexturePaintDocumentLayer PluginLayer(string id, int order)
        {
            var parameters = new TexturePaintPluginParameterSet();
            parameters.Get("order", true).number = order;
            return new TexturePaintDocumentLayer
            {
                id = id,
                name = id,
                kind = TexturePaintLayerKind.Plugin,
                pluginId = "org.uma.tests.material-preset-order",
                pluginVersion = "1.0.0",
                pluginParameters = parameters
            };
        }

        private static void AddChannel(TextureSet set, TexturePaintChannel channel, int resolution)
        {
            set.channels[channel] = new TextureChannelTarget
            {
                channel = channel,
                format = RenderTextureFormat.ARGB32,
                editable = new EditableTextureTarget(channel.ToString(), resolution, resolution,
                    RenderTextureFormat.ARGB32, null, Color.clear)
            };
        }
    }
}
#endif
