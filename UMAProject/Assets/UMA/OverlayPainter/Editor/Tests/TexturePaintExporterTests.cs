#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintExporterTests
    {
        private const string Folder = "Assets/UMA/OverlayPainter/GeneratedTests";
        private TextureStore store;
        private TextureSet set;
        private Material sourceMaterial;
        private UMAMaterial umaMaterial;
        private TexturePaintExportTemplate template;
        private Texture2D sourceTexture;
        private string indexerAssetPath;
        private byte[] indexerAssetBytes;
        private readonly List<string> indexedOverlayNames = new List<string>();
        private readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            indexerAssetPath = AssetDatabase.GetAssetPath(UMAAssetIndexer.Instance);
            indexerAssetBytes = !string.IsNullOrEmpty(indexerAssetPath)
                ? File.ReadAllBytes(Path.GetFullPath(indexerAssetPath)) : null;
            AssetDatabase.DeleteAsset(Folder);
            EnsureFolder(Folder + "/Source");
            sourceTexture = CreateTextureAsset(Folder + "/Source/Body_Base.png", Color.red);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null, "URP Lit is required by the release export tests.");
            sourceMaterial = new Material(shader) { name = "Test Material" };
            sourceMaterial.SetTexture("_BaseMap", sourceTexture);
            AssetDatabase.CreateAsset(sourceMaterial, Folder + "/Source/Test Material.mat");
            umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
            umaMaterial.name = "Test UMA Material";
            umaMaterial.material = sourceMaterial;
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.DiffuseTexture,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_BaseMap",
                    sourceTextureName = "BaseMap",
                    DownSample = 1
                }
            };
            AssetDatabase.CreateAsset(umaMaterial, Folder + "/Source/Test UMA Material.asset");
            AssetDatabase.SaveAssetIfDirty(umaMaterial);
            set = CreateSet("Torso", sourceTexture);
            store = new TextureStore();
            AddSet(store, set);
            template = Own(ScriptableObject.CreateInstance<TexturePaintExportTemplate>());
            template.outputFolder = Folder + "/Output";
            template.scope = TexturePaintExportScope.AllMaterials;
            template.overwritePolicy = TexturePaintOverwritePolicy.Overwrite;
            template.padding = 0;
        }

        [TearDown]
        public void TearDown()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            for (int i = 0; indexer != null && i < indexedOverlayNames.Count; i++)
                indexer.RemoveAsset(typeof(OverlayDataAsset), indexedOverlayNames[i], false);
            if (indexer != null)
            {
                indexer.RebuildIndex();
                EditorUtility.SetDirty(indexer);
                AssetDatabase.SaveAssetIfDirty(indexer);
            }
            store?.Dispose();
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
                if (ownedObjects[i] != null) UnityEngine.Object.DestroyImmediate(ownedObjects[i]);
            ownedObjects.Clear();
            AssetDatabase.DeleteAsset(Folder);
            if (indexerAssetBytes != null && !string.IsNullOrEmpty(indexerAssetPath))
            {
                RestoreAssetBytes(indexerAssetPath, indexerAssetBytes);
                AssetDatabase.ImportAsset(indexerAssetPath, ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }
        }

        [Test]
        public void PlanUsesPhysicalMaterialOrderAndIdentifierNaming()
        {
            TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(store, set, "Avatar", template,
                "Summer Edit", null);
            Assert.That(plan.IsValid, Is.True, string.Join("\n", plan.errors));
            Assert.That(plan.entries.Count, Is.EqualTo(1));
            Assert.That(plan.entries[0].materialChannelIndex, Is.EqualTo(0));
            Assert.That(plan.entries[0].path, Does.EndWith("Body_Base_Summer Edit.png"));
            Assert.That(plan.overlays.Count, Is.EqualTo(1));
            Assert.That(plan.overlays[0].path, Does.EndWith("Torso_Summer Edit_Overlay.asset"));
        }

#if !UMA_ADDRESSABLES
        [Test]
        public void PlanWarnsWhenStoredTemplateRequestsUnavailableAddressablesIntegration()
        {
            template.markAddressable = true;

            TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(store, set, "Avatar", template,
                "Without Addressables", null);

            Assert.That(plan.IsValid, Is.True, string.Join("\n", plan.errors));
            Assert.That(plan.warnings,
                Has.Some.Contains("UMA_ADDRESSABLES is not enabled"));
        }
#endif

        [Test]
        public void ExportCreatesRecipeReadyIndexedOverlayWithoutChangingStateOrSource()
        {
            byte[] sourceBefore = File.ReadAllBytes(Path.GetFullPath(AssetDatabase.GetAssetPath(sourceTexture)));
            TexturePaintStageState state = new TexturePaintStageState();
            state.exportedTexturePaths.Add("unchanged");
            TexturePaintExportResult result = TexturePaintExporter.Export(store, set, null, template, state,
                "Release", null, false);
            RememberIndexed(result);

            Assert.That(result.texturePaths.Count, Is.EqualTo(1));
            Assert.That(result.overlayPaths.Count, Is.EqualTo(1));
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(result.overlayPaths[0]);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.material, Is.SameAs(umaMaterial));
            Assert.That(overlay.textureList, Has.Length.EqualTo(umaMaterial.channels.Length));
            Assert.That(overlay.textureList[0], Is.Not.Null);
            AssetItem indexed = UMAAssetIndexer.Instance.GetAssetItem<OverlayDataAsset>(overlay.overlayName);
            Assert.That(indexed, Is.Not.Null);
            Assert.That(indexed.Item, Is.SameAs(overlay));
            Assert.That(state.exportedTexturePaths, Is.EqualTo(new[] { "unchanged" }),
                "Export history must not mutate paint document/editor state.");
            Assert.That(File.ReadAllBytes(Path.GetFullPath(AssetDatabase.GetAssetPath(sourceTexture))),
                Is.EqualTo(sourceBefore), "Default export must leave source textures byte-identical.");
        }

        [Test]
        public void AuthoredOverlayExportExcludesBaseAndAssignsGeneratedAlphaMask()
        {
            ComputeShader compositorShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/UMA/OverlayPainter/Shaders/LayerComposite.compute");
            Assert.That(compositorShader, Is.Not.Null);
            set.compositor = new TextureLayerCompositor(compositorShader);

            Texture2D authored = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[32 * 32];
            for (int y = 12; y < 20; y++)
            for (int x = 12; x < 20; x++) pixels[y * 32 + x] = new Color(0f, 1f, 0f, 1f);
            authored.SetPixels(pixels);
            authored.Apply(false, false);
            TexturePaintLayer layer = set.AddLayer("Runtime Marking");
            layer.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget(
                "Runtime Marking Albedo", 32, 32, RenderTextureFormat.ARGB32,
                authored, Color.clear);
            layer.GetChannelSettings(TexturePaintChannel.Albedo);
            long baseRevision = set.GetChannel(TexturePaintChannel.Albedo).editable.Revision;
            long layerRevision = layer.channels[TexturePaintChannel.Albedo].Revision;

            template.content = TexturePaintExportContent.AuthoredOverlay;
            TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(store, set, "Avatar", template,
                "Runtime", null);
            Assert.That(plan.IsValid, Is.True, string.Join("\n", plan.errors));
            Assert.That(plan.overlays[0].alphaMaskPath, Does.EndWith("Torso_Runtime_AlphaMask.png"));

            TexturePaintExportResult result = TexturePaintExporter.Export(store, set, null, template, null,
                "Runtime", null, false);
            RememberIndexed(result);

            Assert.That(set.GetChannel(TexturePaintChannel.Albedo).editable.Revision,
                Is.EqualTo(baseRevision));
            Assert.That(layer.channels[TexturePaintChannel.Albedo].Revision,
                Is.EqualTo(layerRevision), "Overlay-only export must not mutate authored pixels.");

            Assert.That(result.texturePaths, Has.Count.EqualTo(1));
            Assert.That(result.alphaMaskPaths, Has.Count.EqualTo(1),
                "The dedicated alpha mask should be reported separately from material textures.");
            TexturePaintExportResultSet resultSet = result.resultSets[0];
            Assert.That(resultSet.alphaMaskPath, Is.Not.Empty);
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(resultSet.overlayPath);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.overlayType, Is.EqualTo(OverlayDataAsset.OverlayType.Normal));
            Assert.That(AssetDatabase.GetAssetPath(overlay.alphaMask),
                Is.EqualTo(resultSet.alphaMaskPath));
            Assert.That(overlay.textureList[0], Is.Not.Null);

            Texture2D exportedColor = LoadPng(AssetDatabase.GetAssetPath(overlay.textureList[0]));
            Texture2D exportedMask = LoadPng(resultSet.alphaMaskPath);
            try
            {
                Color outside = exportedColor.GetPixel(2, 2);
                Color inside = exportedColor.GetPixel(16, 16);
                Color maskOutside = exportedMask.GetPixel(2, 2);
                Color maskInside = exportedMask.GetPixel(16, 16);
                Assert.That(outside.a, Is.EqualTo(0f).Within(1f / 255f),
                    "The reconstructed red base must not make overlay-only export opaque.");
                Assert.That(inside.g, Is.GreaterThan(0.9f));
                Assert.That(inside.a, Is.GreaterThan(0.99f));
                Assert.That(maskOutside.a, Is.EqualTo(0f).Within(1f / 255f));
                Assert.That(maskInside.a, Is.GreaterThan(0.99f));
                Assert.That(maskInside.r, Is.GreaterThan(0.99f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(exportedColor);
                UnityEngine.Object.DestroyImmediate(exportedMask);
            }
        }

        [Test]
        public void AuthoredNormalControlExportsFlatRelativeNormalDeltaAndCoverage()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            ConfigureNormalControlExportSet();
            set.compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader(
                "LayerComposite.compute"));
            set.channelPackShader = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");

            Texture2D height = Own(new Texture2D(32, 32, TextureFormat.RGBAHalf, false, true));
            Color[] heightPixels = new Color[32 * 32];
            for (int i = 0; i < heightPixels.Length; i++)
                heightPixels[i] = new Color(0.5f, 0.5f, 0.5f, 0f);
            for (int y = 6; y < 26; y++)
                heightPixels[y * 32 + 16] = Color.white;
            height.SetPixels(heightPixels);
            height.Apply(false, false);
            TexturePaintLayer layer = set.AddLayer("Raised Detail");
            layer.channels[TexturePaintChannel.NormalControl] = new EditableTextureTarget(
                "Raised Detail Normal Control", 32, 32, RenderTextureFormat.ARGBHalf,
                height, Color.clear);
            layer.GetChannelSettings(TexturePaintChannel.NormalControl);
            set.normalControlStrength = 4f;

            template.content = TexturePaintExportContent.AuthoredOverlay;
            TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(store, set, "Avatar", template,
                "Height", null);
            Assert.That(plan.IsValid, Is.True, string.Join("\n", plan.errors));
            Assert.That(plan.entries, Has.Count.EqualTo(1),
                "Normal Control must cause the physical normal channel to be exported.");
            Assert.That(plan.entries[0].materialChannel.LogicalChannels,
                Has.Member(TexturePaintChannel.Normal));

            TexturePaintExportResult result = TexturePaintExporter.Export(store, set, null,
                template, null, "Height", null, false);
            RememberIndexed(result);
            Assert.That(result.texturePaths, Has.Count.EqualTo(1));
            Assert.That(result.alphaMaskPaths, Has.Count.EqualTo(1));
            Texture2D normal = LoadPng(result.texturePaths[0]);
            Texture2D coverage = LoadPng(result.alphaMaskPaths[0]);
            try
            {
                Color outside = normal.GetPixel(2, 2);
                Color leftSlope = normal.GetPixel(15, 16);
                Color rightSlope = normal.GetPixel(17, 16);
                Assert.That(outside.r, Is.EqualTo(0.5f).Within(2f / 255f));
                Assert.That(outside.g, Is.EqualTo(0.5f).Within(2f / 255f));
                Assert.That(outside.b, Is.GreaterThan(0.98f));
                Assert.That(leftSlope.r, Is.LessThan(0.48f));
                Assert.That(rightSlope.r, Is.GreaterThan(0.52f));
                Assert.That(coverage.GetPixel(15, 16).r, Is.GreaterThan(0.99f),
                    "The gradient halo must be included in runtime overlay coverage.");
                Assert.That(coverage.GetPixel(2, 2).r, Is.LessThan(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(normal);
                UnityEngine.Object.DestroyImmediate(coverage);
            }
        }

        [Test]
        public void AuthoredOverlayPlanRejectsSourceOverwrite()
        {
            template.content = TexturePaintExportContent.AuthoredOverlay;
            template.overwriteSourceOverlay = true;

            TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(store, set, "Avatar", template,
                "Runtime", null);

            Assert.That(plan.IsValid, Is.False);
            Assert.That(plan.errors, Has.Some.Contains("cannot overwrite a source overlay"));
        }

        [Test]
        public void UdimPlanCreatesOneTileResultSetAndTileSuffixedPathsPerMember()
        {
            TextureSet second = CreateSet("Body_1002", sourceTexture);
            AddSet(store, second);
            ConfigureUdim(set, "Body_1001", 1001);
            ConfigureUdim(second, "Body_1002", 1002);
            template.scope = TexturePaintExportScope.CurrentMaterial;

            TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(store, set, "Avatar", template,
                "Paint", null);
            Assert.That(plan.IsValid, Is.True, string.Join("\n", plan.errors));
            Assert.That(plan.overlays.Count, Is.EqualTo(2));
            Assert.That(plan.entries.Exists(entry => entry.path.EndsWith("_Paint_1001.png")), Is.True);
            Assert.That(plan.entries.Exists(entry => entry.path.EndsWith("_Paint_1002.png")), Is.True);
            Assert.That(plan.overlays.Exists(entry => entry.path.EndsWith("_Paint_1001_Overlay.asset")), Is.True);
            Assert.That(plan.overlays.Exists(entry => entry.path.EndsWith("_Paint_1002_Overlay.asset")), Is.True);
        }

        [Test]
        public void CancelBeforeCommitLeavesFilesAndStateUntouched()
        {
            TexturePaintStageState state = new TexturePaintStageState();
            state.exportedTexturePaths.Add("original");
            var cancellation = new System.Threading.CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => TexturePaintExporter.Export(store, set, null,
                template, state, "Cancelled", null, false,
                new TexturePaintOperationContext(cancellation.Token)));
            Assert.That(AssetDatabase.IsValidFolder(template.outputFolder), Is.False);
            Assert.That(state.exportedTexturePaths, Is.EqualTo(new[] { "original" }));
            cancellation.Dispose();
        }

        [Test]
        public void CancelDuringCommitRollsBackTexturesOverlaysAndIndex()
        {
            var cancellation = new System.Threading.CancellationTokenSource();
            var progress = new CancelProgress(cancellation, 0.7f);
            Assert.Throws<OperationCanceledException>(() => TexturePaintExporter.Export(store, set, null,
                template, null, "Cancelled", null, false,
                new TexturePaintOperationContext(cancellation.Token, progress)));
            Assert.That(AssetDatabase.IsValidFolder(template.outputFolder), Is.False);
            Assert.That(AssetDatabase.FindAssets("Cancelled t:OverlayDataAsset", new[] { Folder }), Is.Empty);
            cancellation.Dispose();
        }

        [Test]
        public void OverwriteSourceModeRestoresSourceBytesWhenCommitIsCancelled()
        {
            OverlayDataAsset sourceOverlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            sourceOverlay.name = "Body Source Overlay";
            sourceOverlay.material = umaMaterial;
            sourceOverlay.materialName = umaMaterial.name;
            sourceOverlay.textureList = new Texture[] { sourceTexture };
            sourceOverlay.textureNames = new[] { sourceTexture.name };
            sourceOverlay.overlayBlend = new[] { OverlayDataAsset.OverlayBlend.Normal };
            AssetDatabase.CreateAsset(sourceOverlay, Folder + "/Source/Body Source Overlay.asset");
            set.surface.standaloneSourceOverlay = sourceOverlay;
            set.GetChannel(TexturePaintChannel.Albedo).editable.Reset(null, Color.green);
            template.overwriteSourceOverlay = true;
            byte[] before = File.ReadAllBytes(Path.GetFullPath(AssetDatabase.GetAssetPath(sourceTexture)));
            var cancellation = new System.Threading.CancellationTokenSource();
            var progress = new CancelProgress(cancellation, 0.7f);

            Assert.Throws<OperationCanceledException>(() => TexturePaintExporter.Export(store, set, null,
                template, null, "Overwrite", null, true,
                new TexturePaintOperationContext(cancellation.Token, progress)));

            Assert.That(File.ReadAllBytes(Path.GetFullPath(AssetDatabase.GetAssetPath(sourceTexture))),
                Is.EqualTo(before));
            Assert.That(sourceOverlay.textureList[0], Is.SameAs(sourceTexture));
            Assert.That(AssetDatabase.IsValidFolder(template.outputFolder), Is.False,
                "Overwrite mode must not create an unused output folder.");
            cancellation.Dispose();
        }

        [Test]
        public void GpuPaddingExpandsRgbWithoutChangingTransparentAlpha()
        {
            Texture2D texture = Own(new Texture2D(16, 16, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[16 * 16];
            pixels[8 * 16 + 8] = new Color(1f, 0.25f, 0.1f, 1f);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            MethodInfo method = typeof(TexturePaintExporter).GetMethod("DilateTransparent",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[]
            {
                texture, 2, true, new TexturePaintOperationContext(), null
            });

            Color adjacent = texture.GetPixel(9, 8);
            Color outside = texture.GetPixel(12, 8);
            Assert.That(adjacent.r, Is.GreaterThan(0.9f), "Padding should copy edge RGB.");
            Assert.That(adjacent.a, Is.EqualTo(0f).Within(1f / 255f),
                "Padding must preserve the original transparent alpha.");
            Assert.That(outside.r, Is.EqualTo(0f).Within(1f / 255f),
                "Padding must remain bounded by the requested pass count.");
        }

        private TextureSet CreateSet(string slotName, Texture source)
        {
            TextureSet created = new TextureSet
            {
                persistentId = Guid.NewGuid().ToString("N"),
                umaMaterial = umaMaterial,
                surface = new ReconstructedSurface
                {
                    sourceMaterial = sourceMaterial,
                    previewMaterial = sourceMaterial,
                    slotName = slotName,
                    slotNames = new List<string> { slotName }
                },
                materialCapability = TexturePaintMaterialCapabilityService.Compile(umaMaterial,
                    sourceMaterial, new[] { source }, true)
            };
            created.channels.Add(TexturePaintChannel.Albedo, new TextureChannelTarget
            {
                channel = TexturePaintChannel.Albedo,
                materialProperty = "_BaseMap",
                umaChannelIndex = 0,
                format = RenderTextureFormat.ARGB32,
                sRGB = true,
                sourceTexture = source,
                editable = new EditableTextureTarget("Export Test", 32, 32,
                    RenderTextureFormat.ARGB32, source, Color.white)
            });
            return created;
        }

        private void ConfigureNormalControlExportSet()
        {
            foreach (TextureChannelTarget target in set.channels.Values) target.Dispose();
            set.channels.Clear();
            Texture2D neutral = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false, true));
            Color[] pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.5f, 0.5f, 1f, 1f);
            neutral.SetPixels(pixels);
            neutral.Apply(false, false);
            sourceMaterial.SetTexture("_BumpMap", neutral);
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.NormalMap,
                    textureFormat = RenderTextureFormat.ARGB32,
                    materialPropertyName = "_BumpMap",
                    sourceTextureName = "Normal",
                    DownSample = 1,
                    textureChannelLayout = new UMAMaterial.TextureChannelLayout
                    {
                        mode = UMAMaterial.TextureChannelLayoutMode.Custom,
                        red = UMAMaterial.TextureChannelUsage.Normal,
                        green = UMAMaterial.TextureChannelUsage.Normal,
                        blue = UMAMaterial.TextureChannelUsage.Normal,
                        alpha = UMAMaterial.TextureChannelUsage.Unused
                    },
                    textureChannelOutput = new UMAMaterial.TextureChannelOutputSettings
                    {
                        mode = UMAMaterial.TextureChannelOutputMode.Custom,
                        encoding = UMAMaterial.TextureChannelOutputEncoding.Png8,
                        importerType = UMAMaterial.TextureChannelImporterType.NormalMap,
                        colorSpace = UMAMaterial.TextureChannelColorSpace.Linear,
                        alphaSource = UMAMaterial.TextureChannelAlphaSource.None,
                        compression = UMAMaterial.TextureChannelImportCompression.Uncompressed,
                        normalConvention = UMAMaterial.TextureChannelNormalConvention.OpenGL,
                        generateMipMaps = false,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 1,
                        maxTextureSize = 8192,
                        platformOverrides = Array.Empty<UMAMaterial.TextureChannelPlatformOverrideSettings>()
                    }
                }
            };
            set.materialCapability = TexturePaintMaterialCapabilityService.Compile(umaMaterial,
                sourceMaterial, new Texture[] { neutral }, true);
            set.channels.Add(TexturePaintChannel.Normal, new TextureChannelTarget
            {
                channel = TexturePaintChannel.Normal,
                materialProperty = "_BumpMap",
                umaChannelIndex = 0,
                format = RenderTextureFormat.ARGBHalf,
                sourceTexture = neutral,
                editable = new EditableTextureTarget("Export Normal", 32, 32,
                    RenderTextureFormat.ARGBHalf, neutral, new Color(0.5f, 0.5f, 1f, 1f))
            });
            set.channels.Add(TexturePaintChannel.NormalControl, new TextureChannelTarget
            {
                channel = TexturePaintChannel.NormalControl,
                materialProperty = null,
                umaChannelIndex = -1,
                format = RenderTextureFormat.ARGBHalf,
                editable = new EditableTextureTarget("Export Normal Control", 32, 32,
                    RenderTextureFormat.ARGBHalf, null, new Color(0.5f, 0.5f, 0.5f, 1f))
            });
        }

        private void ConfigureUdim(TextureSet targetSet, string slotName, int tile)
        {
            SlotDataAsset asset = Own(ScriptableObject.CreateInstance<SlotDataAsset>());
            asset.name = slotName;
            asset.udimGroupId = "body-udim";
            asset.udimGroupName = "Body";
            asset.udimTileNumber = tile;
            targetSet.surface.slotName = slotName;
            targetSet.surface.slotNames.Clear();
            targetSet.surface.slotNames.Add(slotName);
            targetSet.surface.slots = new List<SlotData> { new SlotData(asset) };
        }

        private void RememberIndexed(TexturePaintExportResult result)
        {
            for (int i = 0; i < result.overlayPaths.Count; i++)
            {
                OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(result.overlayPaths[i]);
                if (overlay != null) indexedOverlayNames.Add(overlay.overlayName);
            }
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            ownedObjects.Add(value);
            return value;
        }

        private static void AddSet(TextureStore targetStore, TextureSet targetSet)
        {
            FieldInfo field = typeof(TextureStore).GetField("sets", BindingFlags.Instance | BindingFlags.NonPublic);
            ((List<TextureSet>)field.GetValue(targetStore)).Add(targetSet);
        }

        private static Texture2D CreateTextureAsset(string path, Color color)
        {
            Texture2D temporary = new Texture2D(32, 32, TextureFormat.RGBA32, false, false);
            Color[] pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            temporary.SetPixels(pixels);
            temporary.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), temporary.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(temporary);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D LoadPng(string path)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Assert.That(texture.LoadImage(File.ReadAllBytes(Path.GetFullPath(path)), false), Is.True);
            return texture;
        }

        private static void RestoreAssetBytes(string assetPath, byte[] bytes)
        {
            string fullPath = Path.GetFullPath(assetPath);
            // Save/import can briefly keep a memory-mapped handle alive on Windows even after
            // ReleaseCachedFileHandles. Retrying only this exact test-owned restoration avoids a
            // false release-gate failure without weakening cleanup or leaving the indexer changed.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int attempt = 0; ; attempt++)
            {
                AssetDatabase.ReleaseCachedFileHandles();
                try
                {
                    File.WriteAllBytes(fullPath, bytes);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private sealed class CancelProgress : IProgress<float>
        {
            private readonly System.Threading.CancellationTokenSource source;
            private readonly float threshold;
            public CancelProgress(System.Threading.CancellationTokenSource source, float threshold)
            {
                this.source = source;
                this.threshold = threshold;
            }
            public void Report(float value) { if (value >= threshold) source.Cancel(); }
        }
    }
}
#endif
