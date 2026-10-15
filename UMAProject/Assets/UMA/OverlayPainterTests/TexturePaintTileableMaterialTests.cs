#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace UMA.TexturePaint.Editor.Tests
{
    [Category("TileableMaterials")]
    public sealed class TexturePaintTileableMaterialTests
    {
        const string Root = "Assets/UMA/OverlayPainter/Textures/Tileable Materials/";

        [TestCase("Aged Metals")]
        [TestCase("Woven Fabrics")]
        [TestCase("Aged Leather")]
        [TestCase("Denim")]
        [TestCase("Rubber and Elastomers")]
        [TestCase("Plastics and Coatings")]
        [TestCase("Knits and Soft Clothing")]
        [TestCase("Chainmail and Scale Armor")]
        [TestCase("Technical Weaves and Composites")]
        [TestCase("Brocade and Decorative Fabrics")]
        [TestCase("Wood and Natural Materials")]
        [TestCase("Bone Horn and Keratin")]
        [TestCase("Interlinked Chain Mail")]
        public void SpriteSetHasAlignedLinearDataAndFullResolution(string family)
        {
            var set = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(Root + family + " Sprite Set.asset");
            Assert.That(set, Is.Not.Null);
            var channels = TexturePaintSpriteSetSource.Resolve(set);
            Assert.That(channels.Count, Is.EqualTo(5));
            Assert.That(channels.ContainsKey(TexturePaintChannel.NormalControl), Is.False, "Do not double-apply supplied normal and height.");
            int count = family == "Interlinked Chain Mail" ? 4 : 9;
            int atlasSize = family == "Interlinked Chain Mail" ? 2080 : 3120;
            Assert.That(set.spriteNames.Distinct().Count(), Is.EqualTo(count));
            foreach (var sheet in set.spriteSheets)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sheet.spriteSheet));
                Assert.That(importer.sRGBTexture, Is.EqualTo(sheet.channel == TexturePaintChannel.Albedo));
                Assert.That(sheet.spriteSheet.width, Is.EqualTo(atlasSize));
                Assert.That(sheet.spriteSheet.height, Is.EqualTo(atlasSize));
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                var sprites = channels[sheet.channel];
                Assert.That(sprites.Count, Is.EqualTo(count));
                for (int i = 0; i < sprites.Count; i++)
                {
                    Assert.That(sprites[i].rect, Is.EqualTo(channels[TexturePaintChannel.Albedo][i].rect));
                    Assert.That(sprites[i].rect.size, Is.EqualTo(new Vector2(1024, 1024)));
                    // Replacement ring-mail tiles retain their original imported names and IDs.
                    if (family == "Chainmail and Scale Armor" && i < 4)
                        Assert.That(sprites[i].name, Is.EqualTo(new[] { "European Round Mail_0", "Riveted Round Mail_1", "Dense Six Ring Mail_2", "Flat Link Mail_3" }[i]));
                    else Assert.That(sprites[i].name, Is.EqualTo(set.spriteNames[i] + "_" + i));
                }
            }
        }

        [TestCase("Aged Metals")]
        [TestCase("Woven Fabrics")]
        [TestCase("Aged Leather")]
        [TestCase("Denim")]
        [TestCase("Rubber and Elastomers")]
        [TestCase("Plastics and Coatings")]
        [TestCase("Knits and Soft Clothing")]
        [TestCase("Chainmail and Scale Armor")]
        [TestCase("Technical Weaves and Composites")]
        [TestCase("Brocade and Decorative Fabrics")]
        [TestCase("Wood and Natural Materials")]
        [TestCase("Bone Horn and Keratin")]
        [TestCase("Interlinked Chain Mail")]
        public void AtlasGuttersRepeatTheirOwnTileAcrossEveryChannel(string family)
        {
            var set = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(Root + family + " Sprite Set.asset");
            foreach (var sheet in set.spriteSheets)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    Assert.That(texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sheet.spriteSheet))), Is.True);
                    foreach (var sprite in TexturePaintSpriteSetSource.GetOrderedSprites(sheet.spriteSheet))
                    {
                        int x = (int)sprite.rect.x, y = (int)sprite.rect.y;
                        for (int offset = 0; offset < 8; offset++)
                        for (int along = 0; along < 1024; along += 31)
                        {
                            Assert.That(texture.GetPixel(x - 1 - offset, y + along), Is.EqualTo(texture.GetPixel(x + 1023 - offset, y + along)));
                            Assert.That(texture.GetPixel(x + 1024 + offset, y + along), Is.EqualTo(texture.GetPixel(x + offset, y + along)));
                            Assert.That(texture.GetPixel(x + along, y - 1 - offset), Is.EqualTo(texture.GetPixel(x + along, y + 1023 - offset)));
                            Assert.That(texture.GetPixel(x + along, y + 1024 + offset), Is.EqualTo(texture.GetPixel(x + along, y + offset)));
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
        }

        [TestCase("Interlinked Chain Mail")]
        [TestCase("Technical Weaves and Composites")]
        public void OpenMaterialsPreserveIdenticalCoverageAcrossChannels(string family)
        {
            var set = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(Root + family + " Sprite Set.asset");
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                reference.LoadImage(File.ReadAllBytes(Root + family + "/Albedo.png"));
                bool foundOpening = false, foundSolid = false;
                foreach (var sheet in set.spriteSheets)
                {
                    string path = AssetDatabase.GetAssetPath(sheet.spriteSheet);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
                    Assert.That(importer.alphaIsTransparency, Is.False, "Keep straight color and linear data intact.");
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    try
                    {
                        texture.LoadImage(File.ReadAllBytes(path));
                        for (int y = 8; y < reference.height - 8; y += 17)
                        for (int x = 8; x < reference.width - 8; x += 17)
                        {
                            float alpha = reference.GetPixel(x, y).a;
                            foundOpening |= alpha == 0; foundSolid |= alpha == 1;
                            Assert.That(texture.GetPixel(x, y).a, Is.EqualTo(alpha), path);
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
                Assert.That(foundOpening && foundSolid, Is.True, "Openwork needs both openings and solid material.");
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }
    }
}
#endif
