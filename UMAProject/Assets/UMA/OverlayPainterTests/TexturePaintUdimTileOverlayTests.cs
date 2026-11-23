#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    /// <summary>
    /// Fill > From Overlay on a UDIM target: each tile gets its own overlay (UMA30_Body_UDIM1001 -> ..._UDIM1002 ...).
    /// Uses the shipped UMA 3 body overlays.
    /// </summary>
    public sealed class TexturePaintUdimTileOverlayTests
    {
        private const string Folder = "Assets/UMA/UMA3/Overlays/";

        private static OverlayDataAsset Load(string name)
        {
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(Folder + name + ".asset");
            Assert.That(overlay, Is.Not.Null, name);
            return overlay;
        }

        private static List<TexturePaintUdimTileOverlays.Tile> Tiles(params IReadOnlyList<OverlayDataAsset>[] own)
        {
            var tiles = new List<TexturePaintUdimTileOverlays.Tile>();
            for (int i = 0; i < 5; i++)
                tiles.Add(new TexturePaintUdimTileOverlays.Tile
                {
                    number = 1001 + i,
                    ownOverlays = own != null && i < own.Length ? own[i] : new OverlayDataAsset[0]
                });
            return tiles;
        }

        [Test]
        public void TileOverlaysAreFoundByTheTileNumberInTheName()
        {
            OverlayDataAsset picked = Load("UMA30_Body_UDIM1001");
            var tiles = Tiles();
            Assert.That(TexturePaintUdimTileOverlays.TileOf(picked, tiles), Is.EqualTo(1001));
            Assert.That(TexturePaintUdimTileOverlays.NameForTile(picked, 1001, 1004), Is.EqualTo("UMA30_Body_UDIM1004"));
            for (int i = 1; i < 5; i++)
                Assert.That(TexturePaintUdimTileOverlays.Find(picked, 1001, tiles[i]),
                    Is.SameAs(Load("UMA30_Body_UDIM100" + (i + 1))), "tile " + tiles[i].number);

            // Starting from another tile works the same way, in both directions.
            OverlayDataAsset third = Load("UMA30_MaleBody_UDIM1003");
            Assert.That(TexturePaintUdimTileOverlays.TileOf(third, tiles), Is.EqualTo(1003));
            Assert.That(TexturePaintUdimTileOverlays.Find(third, 1003, tiles[0]), Is.SameAs(Load("UMA30_MaleBody_UDIM1001")));
        }

        [Test]
        public void TheCharactersOwnTileOverlaysComeFirst()
        {
            // A character that puts the male body on tile 1002 gets that overlay, found among its own, by name.
            OverlayDataAsset picked = Load("UMA30_MaleBody_UDIM1001");
            OverlayDataAsset own1002 = Load("UMA30_MaleBody_UDIM1002");
            var tiles = Tiles(new[] { picked }, new[] { Load("UMA30_Body_UDIM1002"), own1002 });
            Assert.That(TexturePaintUdimTileOverlays.TileOf(picked, tiles), Is.EqualTo(1001), "the character puts it on 1001");
            Assert.That(TexturePaintUdimTileOverlays.Find(picked, 1001, tiles[1]), Is.SameAs(own1002));
        }

        [Test]
        public void OverlaysWithoutTileSiblingsOrNumbersAreNotMatched()
        {
            // The wrinkle overlay exists only for the head: the other tiles find nothing (they get No contribution).
            OverlayDataAsset wrinkles = Load("UMA30_ExpressionWrinkles_UDIM1001");
            var tiles = Tiles();
            Assert.That(TexturePaintUdimTileOverlays.TileOf(wrinkles, tiles), Is.EqualTo(1001));
            for (int i = 1; i < 5; i++)
                Assert.That(TexturePaintUdimTileOverlays.Find(wrinkles, 1001, tiles[i]), Is.Null, "tile " + tiles[i].number);

            // An overlay without a tile number belongs to no tile: it stays a shared source.
            OverlayDataAsset plain = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                plain.name = "Freckles";
                Assert.That(TexturePaintUdimTileOverlays.TileOf(plain, tiles), Is.EqualTo(0));
                Assert.That(TexturePaintUdimTileOverlays.NameForTile(plain, 1001, 1002), Is.Null);
            }
            finally { Object.DestroyImmediate(plain); }
        }
    }
}
#endif
