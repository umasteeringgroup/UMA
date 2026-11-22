#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    /// <summary>
    /// Expression wrinkle maps in Overlay Painter: the wrinkle UMA Material's channels, Wrinkle Height on the UMA 3 head,
    /// and the 'Wrinkle Maps Only' export. Uses the shipped UMA 3 head, wrinkle overlay and maps; writes only under
    /// <see cref="Folder"/> and restores the Global Library file afterwards.
    /// </summary>
    public sealed class TexturePaintWrinkleMapTests
    {
        private const string Folder = "Assets/UMAProjectData/Tests/OverlayPainter/WrinkleTests";
        private const string WrinkleOverlayPath = "Assets/UMA/UMA3/Overlays/UMA30_ExpressionWrinkles_UDIM1001.asset";

        private string indexerPath;
        private byte[] indexerBytes;
        private readonly List<string> exportedOverlayNames = new List<string>();

        [SetUp]
        public void SetUp()
        {
            indexerPath = AssetDatabase.GetAssetPath(UMAAssetIndexer.Instance);
            indexerBytes = !string.IsNullOrEmpty(indexerPath) ? File.ReadAllBytes(Path.GetFullPath(indexerPath)) : null;
        }

        [TearDown]
        public void TearDown()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            for (int i = 0; indexer != null && i < exportedOverlayNames.Count; i++)
                indexer.RemoveAsset(typeof(OverlayDataAsset), exportedOverlayNames[i], false);
            exportedOverlayNames.Clear();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            if (indexerBytes != null)
            {
                File.WriteAllBytes(Path.GetFullPath(indexerPath), indexerBytes);
                AssetDatabase.ImportAsset(indexerPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        [Test]
        public void WrinkleMaterialResolvesWrinkleChannelsAndKeepsZonesAsData()
        {
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(WrinkleOverlayPath);
            Assert.That(overlay, Is.Not.Null, WrinkleOverlayPath);
            TexturePaintMaterialCapabilityDescriptor descriptor = TexturePaintMaterialCapabilityService.Compile(
                overlay.material, overlay.material.material, overlay.textureList, true);
            TexturePaintMaterialChannelCapability map = descriptor.FindChannel("_WrinkleMap");
            TexturePaintMaterialChannelCapability zones = descriptor.FindChannel("_WrinkleControlMap");
            Assert.That(map, Is.Not.Null);
            Assert.That(zones, Is.Not.Null);
            Assert.That(TexturePaintMaterialCapabilityService.IsWrinkleChannel(map), Is.True);
            Assert.That(TexturePaintMaterialCapabilityService.IsWrinkleChannel(zones), Is.True);
            Assert.That(TexturePaintMaterialCapabilityService.HasWrinkleSlope(map), Is.True);
            Assert.That(map.LogicalChannels, Is.EquivalentTo(new[]
                { TexturePaintChannel.WrinkleCrease, TexturePaintChannel.WrinkleFlush, TexturePaintChannel.WrinkleHeight }));
            Assert.That(map.Components[0].editable, Is.False, "the slope is painted as Wrinkle Height, not directly");
            Assert.That(map.Components[0].neutralValue, Is.EqualTo(0.5f), "a flat slope is 0.5");
            Assert.That(map.Components[2].logicalChannel, Is.EqualTo(TexturePaintChannel.WrinkleCrease));
            Assert.That(map.Components[3].logicalChannel, Is.EqualTo(TexturePaintChannel.WrinkleFlush));
            Assert.That(zones.LogicalChannels, Is.Empty, "zone ids are exact data, never painted");
            Assert.That(map.Diagnostics, Has.None.Matches<TexturePaintCapabilityDiagnostic>(d => d.code == "CHN007"));
            Assert.That(zones.Diagnostics, Has.None.Matches<TexturePaintCapabilityDiagnostic>(d => d.code == "CHN007"));
            Assert.That(zones.output.compression, Is.EqualTo(UMAMaterial.TextureChannelImportCompression.Uncompressed));
        }

        [Test]
        public void WrinkleMapsOnlyNeedsAWrinkleMaterial()
        {
            TexturePaintExportTemplate template = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            TextureStore store = new TextureStore();
            try
            {
                template.content = TexturePaintExportContent.WrinkleMaps;
                template.scope = TexturePaintExportScope.AllMaterials;
                template.outputFolder = Folder;
                TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(store, null, "Empty", template, "Wrinkles", null);
                Assert.That(plan.IsValid, Is.False);
                Assert.That(plan.errors, Has.Some.Contains("Nothing here has a wrinkle map"));
            }
            finally
            {
                store.Dispose();
                Object.DestroyImmediate(template);
            }
        }

        // The full artist path on the UMA 3 head: open the wrinkle overlay, paint a crease with Wrinkle Height and
        // Wrinkle Crease, export 'Wrinkle Maps Only', and check every output against the source and the baker's maths.
        [Test]
        public void PaintedWrinkleHeightExportsAWrinkleOverlayMatchingTheBaker()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            SlotDataAsset head = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(UMAWrinkleMapBaker.UMA3HeadSlotPath);
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(WrinkleOverlayPath);
            Assert.That(head, Is.Not.Null);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(UMAWrinkleMapBaker.FindHeightSource(overlay.textureList[4]), Is.Not.Null,
                "the shipped wrinkle map has its height next to it (UMA > Skin > Bake Procedural Wrinkle Maps writes it)");

            TexturePaintStageController controller = OpenHead(head, overlay);
            TexturePaintExportTemplate template = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            try
            {
                TextureSet set = null, other = null;
                foreach (TextureSet candidate in controller.Textures.Sets)
                    if (candidate.HasWrinkleHeight) set = candidate; else other = candidate;
                Assert.That(set, Is.Not.Null, "the head tile edits its wrinkle map");
                Assert.That(other?.WrinkleStatus, Does.Contain("has no wrinkle map"), "torso tiles get no Wrinkle Height");
                Assert.That(set.previewMaterial.GetFloat("_WrinkleZone01"), Is.EqualTo(1f), "wrinkles show in the preview");

                // Untouched, the packed wrinkle map is the source file exactly.
                TexturePhysicalChannelGroup group = set.physicalChannelGroups[set.WrinkleMapProperty];
                UMAPngImage source = UMAPngReader.Read(File.ReadAllBytes(AssetDatabase.GetAssetPath(overlay.textureList[4])));
                int size = source.Width;
                Color32[] packed = ReadPacked(group);
                int differing = 0;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color32 p = packed[(size - 1 - y) * size + x];
                    int i = (y * size + x) * 4;
                    if (p.r != Byte(source.Values[i]) || p.g != Byte(source.Values[i + 1]) ||
                        p.b != Byte(source.Values[i + 2]) || p.a != Byte(source.Values[i + 3])) differing++;
                }
                Assert.That(differing, Is.EqualTo(0), "an untouched session must not change the wrinkle map");

                // Paint: a 1 mm deep crease across the forehead.
                UMAPngImage zones = UMAPngReader.Read(File.ReadAllBytes(AssetDatabase.GetAssetPath(overlay.textureList[5])));
                ForeheadBox(zones, out int row, out int x0, out int x1);
                AddCreaseLayer(set, size, row, x0, x1);
                set.BindPreviewTextures();

                template.outputFolder = Folder;
                template.content = TexturePaintExportContent.WrinkleMaps;
                template.scope = TexturePaintExportScope.CurrentMaterial;
                template.overwritePolicy = TexturePaintOverwritePolicy.Overwrite;
                TexturePaintExportPlan plan = TexturePaintExporter.BuildPlan(controller.Textures, set, "Wrinkles",
                    template, "Crease", controller.LogicalTargets);
                Assert.That(plan.IsValid, Is.True, string.Join("\n", plan.errors));
                Assert.That(plan.entries, Has.Count.EqualTo(2), "only the wrinkle map and the zone map");
                Assert.That(plan.overlays, Has.Count.EqualTo(1), "only the head tile");
                Assert.That(plan.overlays[0].heightSourcePath, Does.EndWith("_height.png"));

                TexturePaintExportResult result = TexturePaintExporter.Export(controller.Textures, set, null, template,
                    null, "Crease", controller.LogicalTargets, false);
                OverlayDataAsset exported = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(result.overlayPaths[0]);
                exportedOverlayNames.Add(exported.overlayName);
                Assert.That(exported.material, Is.SameAs(overlay.material));
                for (int i = 0; i < 4; i++) Assert.That(exported.textureList[i], Is.Null, "skin channel " + i + " stays empty");
                Assert.That(exported.textureList[4], Is.Not.Null);
                Assert.That(exported.textureList[5], Is.Not.Null);
                Assert.That(result.heightSourcePaths, Has.Count.EqualTo(1));
                Texture2D heightAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(result.heightSourcePaths[0]);
                Assert.That(heightAsset.format, Is.EqualTo(TextureFormat.R16));
                Assert.That(UMAWrinkleMapBaker.FindHeightSource(exported.textureList[4]), Is.EqualTo(heightAsset),
                    "re-opening the exported overlay finds its height");

                UMAPngImage outMap = UMAPngReader.Read(File.ReadAllBytes(AssetDatabase.GetAssetPath(exported.textureList[4])));
                UMAPngImage outZones = UMAPngReader.Read(File.ReadAllBytes(AssetDatabase.GetAssetPath(exported.textureList[5])));
                UMAPngImage outHeight = UMAPngReader.Read(File.ReadAllBytes(result.heightSourcePaths[0]));
                UMAPngImage sourceHeight = UMAPngReader.Read(File.ReadAllBytes(
                    UMAWrinkleMapBaker.HeightSourcePath(AssetDatabase.GetAssetPath(overlay.textureList[4]))));
                Assert.That(outZones.Values, Is.EqualTo(zones.Values), "zone ids are exported exactly");

                // The CPU reference: the slope of the exported height change added to the source map.
                int n = size * size;
                float[] delta = new float[n];
                for (int i = 0; i < n; i++)
                    delta[i] = (outHeight.Values[i] - sourceHeight.Values[i]) * 2f * UMAWrinkleSourceMaps.HeightRangeMM;
                UMAWrinkleSurfaceMap surface = UMAWrinkleMapBaker.BuildSurface(head, size);
                float[] reference = (float[])source.Values.Clone();
                UMAWrinkleSourceMaps.AddSlopes(surface, UMAWrinkleSourceMaps.BuildSlopeFrame(surface), delta, reference);
                int changedOutside = 0, changed = 0, flushChanged = 0;
                float worst = 0f;
                for (int i = 0; i < n; i++)
                {
                    int y = i / size;
                    bool rg = Byte(outMap.Values[i * 4]) != Byte(source.Values[i * 4]) ||
                              Byte(outMap.Values[i * 4 + 1]) != Byte(source.Values[i * 4 + 1]);
                    if (rg) { changed++; if (Mathf.Abs(y - row) > 20) changedOutside++; }
                    if (Byte(outMap.Values[i * 4 + 3]) != Byte(source.Values[i * 4 + 3])) flushChanged++;
                    for (int k = 0; k < 2; k++)
                        worst = Mathf.Max(worst, Mathf.Abs(outMap.Values[i * 4 + k] - reference[i * 4 + k]) * 255f);
                }
                Assert.That(changed, Is.GreaterThan(1000), "the crease reaches the wrinkle map");
                Assert.That(changedOutside, Is.EqualTo(0), "nothing away from the crease changes");
                Assert.That(flushChanged, Is.EqualTo(0), "flush was not painted");
                Assert.That(worst, Is.LessThan(4f), "the GPU slope matches UMAWrinkleSourceMaps.AddSlopes");
                int creased = 0;
                for (int x = x0 + 30; x < x1 - 30; x++) if (outMap.Values[(row * size + x) * 4 + 2] > 0.55f) creased++;
                Assert.That(creased, Is.EqualTo(x1 - x0 - 60), "Wrinkle Crease reaches the blue channel");
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void CopyFromBelowOfWrinkleHeightKeepsTheWrinkleMapAndGivesTheBlurBrushPixels()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            SlotDataAsset head = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(UMAWrinkleMapBaker.UMA3HeadSlotPath);
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(WrinkleOverlayPath);
            TexturePaintStageController controller = OpenHead(head, overlay);
            try
            {
                TextureSet set = null;
                foreach (TextureSet candidate in controller.Textures.Sets)
                    if (candidate.HasWrinkleHeight) set = candidate;
                Assert.That(set, Is.Not.Null, "the head tile edits its wrinkle map");
                TexturePhysicalChannelGroup group = set.physicalChannelGroups[set.WrinkleMapProperty];
                Color32[] before = ReadPacked(group);

                // What Copy from Below does: the layer takes the height in effect under it, and replaces it.
                TextureChannelTarget baseHeight = set.GetChannel(TexturePaintChannel.WrinkleHeight);
                TexturePaintLayer layer = set.AddLayer("Height Copy");
                var copy = new EditableTextureTarget("Height Copy", baseHeight.Texture.width, baseHeight.Texture.height,
                    baseHeight.format, null, Color.clear);
                Assert.That(set.CopyBelowLayerInto(TexturePaintChannel.WrinkleHeight, layer, copy), Is.True);
                layer.channels[TexturePaintChannel.WrinkleHeight] = copy;
                layer.GetChannelSettings(TexturePaintChannel.WrinkleHeight).blendMode = TexturePaintBlendMode.Normal;
                set.BindPreviewTextures();

                // The copy holds the relief: it is not flat (mid gray) everywhere, and it is opaque.
                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(copy.Front);
                int relief = 0, transparent = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (Mathf.Abs(pixels[i].r - 0.5f) > 0.02f) relief++;
                    if (pixels[i].a < 0.999f) transparent++;
                }
                Assert.That(relief, Is.GreaterThan(1000), "the copy holds the wrinkle relief");
                Assert.That(transparent, Is.EqualTo(0), "the copy covers the whole tile");

                // The height did not change, so the wrinkle map must not change either.
                Color32[] after = ReadPacked(group);
                int differing = 0;
                for (int i = 0; i < before.Length; i++)
                    if (before[i].r != after[i].r || before[i].g != after[i].g ||
                        before[i].b != after[i].b || before[i].a != after[i].a) differing++;
                Assert.That(differing, Is.EqualTo(0), "copying the height must leave the wrinkle map as it was");
            }
            finally { controller.Dispose(); }
        }

        private static TexturePaintStageController OpenHead(SlotDataAsset head, OverlayDataAsset overlay)
        {
            Assert.That(TexturePaintUdimResolver.TryResolve(head, out List<SlotDataAsset> members, out string error), Is.True, error);
            TexturePaintLaunchContext context = new TexturePaintLaunchContext
            {
                kind = TexturePaintLaunchKind.StandaloneSlot,
                sourceMode = TexturePaintStandaloneSourceMode.OverlayDataAsset,
                selectedSlot = head,
                umaMaterial = overlay.material,
                udimGroupId = head.udimGroupId,
                resolution = 2048
            };
            foreach (SlotDataAsset member in members)
                context.members.Add(new TexturePaintStandaloneMemberContext
                {
                    slot = member, tileNumber = member.udimTileNumber, overlay = member == head ? overlay : null
                });
            TexturePaintStageController controller = new TexturePaintStageController();
            controller.InitializeStandalone(context,
                TexturePaintGpuTestFixture.LoadShader("StrokeRasterize.compute"),
                TexturePaintGpuTestFixture.LoadShader("Blur.compute"),
                TexturePaintGpuTestFixture.LoadShader("NormalTouchup.compute"),
                TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"),
                TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute"), 2048,
                AssetDatabase.LoadAssetAtPath<Shader>(
                    TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/FillLayer.shader")));
            return controller;
        }

        // The middle row of the two forehead zones (layer A of the zone map), top-first rows.
        private static void ForeheadBox(UMAPngImage zones, out int row, out int x0, out int x1)
        {
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (int y = 0; y < zones.Height; y++)
            for (int x = 0; x < zones.Width; x++)
            {
                int i = (y * zones.Width + x) * 4;
                int id = UMAWrinkleZones.DecodeId(zones.Values[i]);
                if ((id == (int)UMAWrinkleZone.ForeheadLeft || id == (int)UMAWrinkleZone.ForeheadRight) && zones.Values[i + 1] > 0.9f)
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            }
            Assert.That(maxX, Is.GreaterThan(minX), "the zone map has forehead zones");
            row = (minY + maxY) / 2;
            x0 = minX + (maxX - minX) / 5;
            x1 = maxX - (maxX - minX) / 5;
        }

        private static void AddCreaseLayer(TextureSet set, int size, int row, int x0, int x1)
        {
            Texture2D height = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
            Texture2D crease = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            try
            {
                Color[] heightPixels = new Color[size * size];
                Color[] creasePixels = new Color[size * size];
                float deep = UMAWrinkleSourceMaps.HeightToGray(-1f);
                for (int y = row - 12; y <= row + 12; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = (y - row) / 4f;
                    float edge = Mathf.Clamp01(Mathf.Min(x - x0, x1 - x) / 20f);
                    int texel = (size - 1 - y) * size + x; // texture rows start at the bottom
                    heightPixels[texel] = new Color(deep, deep, deep, Mathf.Exp(-d * d) * edge);
                    if (Mathf.Abs(y - row) <= 3) creasePixels[texel] = new Color(0.6f, 0.6f, 0.6f, 1f);
                }
                height.SetPixels(heightPixels); height.Apply(false, false);
                crease.SetPixels(creasePixels); crease.Apply(false, false);
                TexturePaintLayer layer = set.AddLayer("Forehead Crease");
                layer.channels[TexturePaintChannel.WrinkleHeight] = new EditableTextureTarget("Crease Height", size, size,
                    RenderTextureFormat.ARGBHalf, height, Color.clear);
                Assert.That(layer.GetChannelSettings(TexturePaintChannel.WrinkleHeight).blendMode,
                    Is.EqualTo(TexturePaintBlendMode.Overlay), "Wrinkle Height layers add to the wrinkles below");
                layer.channels[TexturePaintChannel.WrinkleCrease] = new EditableTextureTarget("Crease Depth", size, size,
                    RenderTextureFormat.ARGB32, crease, Color.clear);
                layer.GetChannelSettings(TexturePaintChannel.WrinkleCrease);
            }
            finally
            {
                Object.DestroyImmediate(height);
                Object.DestroyImmediate(crease);
            }
        }

        private static Color32[] ReadPacked(TexturePhysicalChannelGroup group)
        {
            Texture2D baked = TexturePaintBaker.Bake(group, 0, TexturePaintExportBitDepth.Eight);
            try { return baked.GetPixels32(); }
            finally { Object.DestroyImmediate(baked); }
        }

        private static byte Byte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }
}
#endif
