#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMATextureToolsTests
    {
        private string folder;
        private string albedo, normal, metallic, mask, unused;
        private string bodyOverlayPath, glowOverlayPath;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/UMATextureToolsTests-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(folder);

        private static Color32 PixelAt(int x, int y) => new Color32((byte)(x * 4), (byte)(y * 8), (byte)(17 + x + y), (byte)(255 - x * 3));

        private string CreateImage(string name, string extension = ".tga", int width = 64, int height = 32, Action<TextureImporter> configure = null)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        pixels[y * width + x] = PixelAt(x, y);
                texture.SetPixels32(pixels);
                texture.Apply();
                string path = folder + "/" + name + extension;
                File.WriteAllBytes(path, extension == ".png" ? texture.EncodeToPNG() : texture.EncodeToTGA());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                configure?.Invoke(importer);
                importer.SaveAndReimport();
                return AssetDatabase.AssetPathToGUID(path);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private Texture2D Load(string guid) => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));

        private UMAMaterial CreateMaterial(string name, params (UMAMaterial.ChannelType type, string property)[] channels)
        {
            var material = ScriptableObject.CreateInstance<UMAMaterial>();
            material.channels = channels.Select(c => new UMAMaterial.MaterialChannel
            {
                channelType = c.type,
                materialPropertyName = c.property,
                textureFormat = RenderTextureFormat.ARGB32,
                DownSample = 1
            }).ToArray();
            AssetDatabase.CreateAsset(material, folder + "/" + name + ".asset");
            return material;
        }

        private string CreateOverlay(string name, UMAMaterial material, Texture alphaMask, params Texture[] textures)
        {
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            overlay.material = material;
            overlay.textureList = textures;
            overlay.textureNames = textures.Select(t => t.name).ToArray();
            overlay.overlayBlend = new OverlayDataAsset.OverlayBlend[textures.Length];
            overlay.alphaMask = alphaMask;
            string path = folder + "/" + name + ".asset";
            AssetDatabase.CreateAsset(overlay, path);
            AssetDatabase.SaveAssetIfDirty(overlay);
            return path;
        }

        /// <summary>
        /// Body overlay: _BaseMap, _BumpMap, _MetallicGlossMap and an alpha mask.
        /// Glow overlay: the same metallic texture in slot 0, which is _EmissionMap there.
        /// </summary>
        private void CreateScene(bool glowAlsoUsesMetallic = true)
        {
            albedo = CreateImage("albedo");
            normal = CreateImage("normal");
            metallic = CreateImage("metallic");
            mask = CreateImage("mask", configure: i => i.sRGBTexture = false);
            unused = CreateImage("unused");
            UMAMaterial body = CreateMaterial("BodyMaterial",
                (UMAMaterial.ChannelType.Texture, "_BaseMap"),
                (UMAMaterial.ChannelType.NormalMap, "_BumpMap"),
                (UMAMaterial.ChannelType.Texture, "_MetallicGlossMap"));
            bodyOverlayPath = CreateOverlay("BodyOverlay", body, Load(mask), Load(albedo), Load(normal), Load(metallic));
            if (glowAlsoUsesMetallic)
            {
                UMAMaterial glow = CreateMaterial("GlowMaterial", (UMAMaterial.ChannelType.Texture, "_EmissionMap"));
                glowOverlayPath = CreateOverlay("GlowOverlay", glow, null, Load(metallic));
            }
        }

        private List<string> AllGuids => new List<string> { albedo, normal, metallic, mask, unused };

        [Test]
        public void ScanMapsOverlaySlotsToMaterialKeywordsIncludingSeveralPerTexture()
        {
            CreateScene();
            Dictionary<string, List<UMATextureUsage>> usages = UMATextureUsageScanner.Scan(AllGuids);
            Assert.That(usages[albedo].Select(u => u.Keyword), Is.EquivalentTo(new[] { "_BaseMap" }));
            Assert.That(usages[normal].Select(u => u.Keyword), Is.EquivalentTo(new[] { "_BumpMap" }));
            Assert.That(usages[metallic].Select(u => u.Keyword), Is.EquivalentTo(new[] { "_MetallicGlossMap", "_EmissionMap" }));
            Assert.That(usages[mask].Select(u => u.Keyword), Is.EquivalentTo(new[] { UMATextureUsageScanner.AlphaMaskKeyword }));
            Assert.That(usages[unused], Is.Empty);
            Assert.That(usages[metallic].Single(u => u.Keyword == "_MetallicGlossMap").ChannelIndex, Is.EqualTo(2));
            Assert.That(usages[metallic].Single(u => u.Keyword == "_EmissionMap").OverlayPath, Is.EqualTo(glowOverlayPath));
        }

        [Test]
        public void MaterialCheckFindsDataMapInSrgbAndNormalMapNotImportedAsNormal_AndFixesOnlyImportSettings()
        {
            CreateScene(glowAlsoUsesMetallic: false);
            Dictionary<string, List<UMATextureUsage>> usages = UMATextureUsageScanner.Scan(AllGuids);
            var metallicImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(metallic));
            Assert.That(metallicImporter.sRGBTexture, Is.True, "Unity imports new images as sRGB.");

            List<UMATextureSettingIssue> issues = UMATextureSettingsChecker.Check(metallicImporter, usages[metallic]);
            UMATextureSettingIssue srgb = issues.Single(i => i.Setting == "sRGB");
            Assert.That(srgb.Level, Is.EqualTo(UMATextureSettingIssue.Severity.Error));
            Assert.That(srgb.Fix, Is.Not.Null);
            var normalImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(normal));
            Assert.That(UMATextureSettingsChecker.Check(normalImporter, usages[normal]).Any(i => i.Setting == "Texture Type" && i.Fix != null));
            var albedoImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(albedo));
            Assert.That(UMATextureSettingsChecker.Check(albedoImporter, usages[albedo]).Where(i => i.Level != UMATextureSettingIssue.Severity.Info), Is.Empty,
                "An sRGB albedo with alpha is already right.");

            string metallicPath = AssetDatabase.GUIDToAssetPath(metallic);
            byte[] source = File.ReadAllBytes(metallicPath);
            List<UMATextureToolsOperations.Result> results = UMATextureToolsOperations.ApplyMaterialFixes(AllGuids, usages);
            Assert.That(results.Single(r => r.Guid == metallic).Changed, Is.True, results.Single(r => r.Guid == metallic).Message);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(metallicPath)).sRGBTexture, Is.False);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(normal))).textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(results.Single(r => r.Guid == albedo).Changed, Is.False);
            Assert.That(File.ReadAllBytes(metallicPath), Is.EqualTo(source), "Source images are never modified.");
        }

        [Test]
        public void TextureUsedAsColorAndAsDataIsReportedButNotAutoFixed()
        {
            CreateScene();
            Dictionary<string, List<UMATextureUsage>> usages = UMATextureUsageScanner.Scan(AllGuids);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(metallic));
            UMATextureSettingIssue srgb = UMATextureSettingsChecker.Check(importer, usages[metallic]).Single(i => i.Setting == "sRGB");
            Assert.That(srgb.Fix, Is.Null);
            Assert.That(srgb.Message, Does.Contain("_EmissionMap").And.Contain("_MetallicGlossMap"));
        }

        [Test]
        public void AlphaSourceNoneOnASmoothnessMapIsAnError()
        {
            CreateScene(glowAlsoUsesMetallic: false);
            string path = AssetDatabase.GUIDToAssetPath(metallic);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
            Dictionary<string, List<UMATextureUsage>> usages = UMATextureUsageScanner.Scan(new[] { metallic });
            UMATextureSettingIssue alpha = UMATextureSettingsChecker.Check(importer, usages[metallic]).Single(i => i.Setting == "Alpha Source");
            Assert.That(alpha.Level, Is.EqualTo(UMATextureSettingIssue.Severity.Error));
            Assert.That(alpha.Fix, Is.Not.Null);
        }

        [Test]
        public void AlphaMaskIsNotCheckedForSrgbBecauseOnlyItsAlphaIsRead()
        {
            CreateScene(glowAlsoUsesMetallic: false);
            string path = AssetDatabase.GUIDToAssetPath(mask);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            Dictionary<string, List<UMATextureUsage>> usages = UMATextureUsageScanner.Scan(new[] { mask });
            Assert.That(UMATextureSettingsChecker.Check(importer, usages[mask]).Where(i => i.Setting == "sRGB"), Is.Empty);
        }

        [Test]
        public void ImportEditChangesOnlyTickedSettingsAndSkipsTexturesThatAlreadyMatch()
        {
            string guid = CreateImage("edit", configure: i => { i.filterMode = FilterMode.Point; i.wrapMode = TextureWrapMode.Clamp; });
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var edit = new UMATextureImportEdit
            {
                setSRGB = true, sRGB = false,
                setMaxSize = true, maxSize = 32,
                mipmaps = true, filter = FilterMode.Trilinear // values without ticks are ignored
            };
            List<UMATextureToolsOperations.Result> first = UMATextureToolsOperations.ApplyImportEdit(new[] { guid }, edit);
            Assert.That(first[0].Changed, Is.True, first[0].Message);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.maxTextureSize, Is.EqualTo(32));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(Load(guid).width, Is.EqualTo(32), "The texture was reimported.");

            byte[] meta = File.ReadAllBytes(path + ".meta");
            List<UMATextureToolsOperations.Result> second = UMATextureToolsOperations.ApplyImportEdit(new[] { guid }, edit);
            Assert.That(second[0].Changed, Is.False);
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(meta));
        }

        [Test]
        public void PlatformSettingsAreWrittenAsAnOverrideAndCanBeRemoved()
        {
            string guid = CreateImage("platform");
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var edit = new UMATextureImportEdit { platform = "Android", setMaxSize = true, maxSize = 32 };
            UMATextureToolsOperations.ApplyImportEdit(new[] { guid }, edit);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.maxTextureSize, Is.EqualTo(32));
            Assert.That(importer.GetDefaultPlatformTextureSettings().maxTextureSize, Is.Not.EqualTo(32));

            UMATextureToolsOperations.ApplyImportEdit(new[] { guid }, new UMATextureImportEdit { platform = "Android", removeOverride = true });
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).GetPlatformTextureSettings("Android").overridden, Is.False);
        }

        [Test]
        public void PngReplaceInPlaceKeepsGuidAndImportSettingsAndWritesFullResolutionSourcePixels()
        {
            string guid = CreateImage("data", configure: i =>
            {
                i.sRGBTexture = false;
                i.maxTextureSize = 32; // imported smaller than the 64x32 source
                i.textureCompression = TextureImporterCompression.Compressed;
                i.wrapMode = TextureWrapMode.Clamp;
            });
            string source = AssetDatabase.GUIDToAssetPath(guid);

            List<UMATexturePngConverter.Result> results = UMATexturePngConverter.Convert(new[] { guid }, UMATexturePngConverter.Mode.ReplaceInPlace, false);
            Assert.That(results[0].Converted, Is.True, results[0].Message);
            string png = Path.ChangeExtension(source, ".png").Replace('\\', '/');
            Assert.That(File.Exists(source), Is.False);
            Assert.That(AssetDatabase.AssetPathToGUID(png), Is.EqualTo(guid));
            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.maxTextureSize, Is.EqualTo(32));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Compressed));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(decoded.LoadImage(File.ReadAllBytes(png)), Is.True);
                Assert.That(decoded.width, Is.EqualTo(64));
                Assert.That(decoded.height, Is.EqualTo(32));
                Color32[] pixels = decoded.GetPixels32();
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 64; x++)
                        Assert.That(pixels[y * 64 + x], Is.EqualTo(PixelAt(x, y)), $"Pixel {x},{y} differs from the source.");
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        [Test]
        public void PngCopyCopiesImportSettingsAndRelinksOverlays()
        {
            CreateScene(glowAlsoUsesMetallic: false);
            string source = AssetDatabase.GUIDToAssetPath(metallic);
            var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(source);
            sourceImporter.sRGBTexture = false;
            sourceImporter.anisoLevel = 7;
            sourceImporter.SaveAndReimport();

            List<UMATexturePngConverter.Result> results = UMATexturePngConverter.Convert(new[] { metallic }, UMATexturePngConverter.Mode.CreateCopy, true);
            Assert.That(results[0].Converted, Is.True, results[0].Message);
            Assert.That(File.Exists(source), Is.True, "The original is kept.");
            var copyImporter = (TextureImporter)AssetImporter.GetAtPath(results[0].OutputPath);
            Assert.That(copyImporter.sRGBTexture, Is.False);
            Assert.That(copyImporter.anisoLevel, Is.EqualTo(7));
            var overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(bodyOverlayPath);
            Assert.That(AssetDatabase.GetAssetPath(overlay.textureList[2]), Is.EqualTo(results[0].OutputPath));
        }

        [Test]
        public void PngConversionSkipsPngAndHdrFiles()
        {
            string png = CreateImage("already", ".png");
            Assert.That(UMATexturePngConverter.GetSkipReason(AssetDatabase.GUIDToAssetPath(png), UMATexturePngConverter.Mode.ReplaceInPlace), Does.Contain("PNG"));
            Assert.That(UMATexturePngConverter.GetSkipReason("Packages/com.example/tex.tga", UMATexturePngConverter.Mode.CreateCopy), Is.Not.Null);
        }

        [Test]
        public void WindowFiltersByKeywordAndOperatesOnTickedVisibleTextures()
        {
            CreateScene();
            var window = ScriptableObject.CreateInstance<UMATextureToolsWindow>();
            try
            {
                window.SetTextures(AllGuids, true);
                window.RescanUsage(false);
                Assert.That(window.VisibleGuids, Is.EquivalentTo(AllGuids));
                Assert.That(window.KeywordsOf(metallic), Is.EquivalentTo(new[] { "_MetallicGlossMap", "_EmissionMap" }));
                Assert.That(window.KeywordsOf(unused), Is.EquivalentTo(new[] { UMATextureToolsWindow.UnusedKeyword }));

                window.ShowOnlyKeywords("_EmissionMap");
                Assert.That(window.VisibleGuids, Is.EquivalentTo(new[] { metallic }));

                window.ShowOnlyKeywords("_BumpMap", "_BaseMap");
                Assert.That(window.VisibleGuids, Is.EquivalentTo(new[] { albedo, normal }));
                window.SetChecked(normal, false);
                Assert.That(window.TargetGuids, Is.EquivalentTo(new[] { albedo }));

                window.ShowOnlyKeywords(UMATextureToolsWindow.UnusedKeyword);
                Assert.That(window.VisibleGuids, Is.EquivalentTo(new[] { unused }));

                window.SetKeywordShown("_BaseMap", true);
                window.SetSearch("albedo");
                Assert.That(window.VisibleGuids, Is.EquivalentTo(new[] { albedo }));
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
#endif
