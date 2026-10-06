using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA.TexturePaint
{
    /// <summary>Immutable, decoded tiles. No Unity assets are touched by generator worker threads.</summary>
    public sealed class TexturePaintReadOnlySpriteSet
    {
        private readonly Dictionary<TexturePaintChannel, Dictionary<int, TexturePaintReadOnlyParameterTexture>> tiles;
        public IReadOnlyList<int> enabledIndices { get; }
        public IReadOnlyList<TexturePaintChannel> channels { get; }
        public int tileCount { get; }

        internal TexturePaintReadOnlySpriteSet(int count, int[] enabled,
            Dictionary<TexturePaintChannel, Dictionary<int, TexturePaintReadOnlyParameterTexture>> tiles)
        {
            tileCount = count;
            enabledIndices = Array.AsReadOnly((int[])enabled.Clone());
            this.tiles = tiles;
            channels = new List<TexturePaintChannel>(tiles.Keys).AsReadOnly();
        }

        public TexturePaintReadOnlyParameterTexture GetTile(int index, TexturePaintChannel channel) =>
            tiles.TryGetValue(channel, out var channelTiles) && channelTiles.TryGetValue(index, out var tile) ? tile : null;
    }

    public static class TexturePaintSpriteSetSource
    {
        public static List<Sprite> GetOrderedSprites(Texture2D texture)
        {
            var result = new List<Sprite>();
            if (texture == null) return result;
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return result;
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (asset is Sprite sprite) result.Add(sprite);
            result.Sort(CompareSprites);
#endif
            return result;
        }

        private static int TrailingIndex(string name)
        {
            int separator = name?.LastIndexOf('_') ?? -1;
            return separator >= 0 && int.TryParse(name.Substring(separator + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out int index) ? index : -1;
        }

        private static int CompareSprites(Sprite left, Sprite right)
        {
            int a = TrailingIndex(left.name), b = TrailingIndex(right.name);
            if (a >= 0 && b >= 0 && a != b) return a.CompareTo(b);
            if ((a >= 0) != (b >= 0)) return a >= 0 ? -1 : 1;
            int row = right.rect.y.CompareTo(left.rect.y);
            if (row != 0) return row;
            int column = left.rect.x.CompareTo(right.rect.x);
            return column != 0 ? column : string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        }

        public static Dictionary<TexturePaintChannel, List<Sprite>> Resolve(OverlayPainterSpriteSet source)
        {
            var result = new Dictionary<TexturePaintChannel, List<Sprite>>();
            if (source == null || source.spriteSheets == null) throw new InvalidOperationException("Assign a Sprite Set.");
            int count = -1;
            foreach (var sheet in source.spriteSheets)
            {
                if (sheet?.spriteSheet == null) throw new InvalidOperationException("The Sprite Set contains a missing channel sheet.");
                if (result.ContainsKey(sheet.channel)) throw new InvalidOperationException("The Sprite Set contains duplicate " + sheet.channel + " sheets.");
                var sprites = GetOrderedSprites(sheet.spriteSheet);
                if (sprites.Count == 0) throw new InvalidOperationException(sheet.SheetName + " has no sliced sprites. Slice its tiles in the Sprite Editor.");
                if (count >= 0 && sprites.Count != count) throw new InvalidOperationException("Sprite Set channel sheets must have matching tile counts.");
                count = sprites.Count;
                result.Add(sheet.channel, sprites);
            }
            if (!result.ContainsKey(TexturePaintChannel.Albedo))
                throw new InvalidOperationException("A quilt Sprite Set needs an Albedo sheet for its panel images and tile picker.");
            return result;
        }

        public static int[] ResolveEnabled(TexturePaintPluginParameterValue value, int count)
        {
            var indices = new SortedSet<int>();
            if (!value.spriteSetSelectionExplicit)
                for (int i = 0; i < count; i++) indices.Add(i);
            else if (value.enabledSpriteIndices != null)
                foreach (int i in value.enabledSpriteIndices)
                {
                    if (i < 0 || i >= count) throw new InvalidOperationException("A selected Sprite Set tile is missing. Open Select Tiles to update the selection.");
                    indices.Add(i);
                }
            if (indices.Count == 0) throw new InvalidOperationException("Enable at least one Sprite Set tile in Select Tiles.");
            var result = new int[indices.Count]; indices.CopyTo(result); return result;
        }
    }
}
