using System;
using System.Collections.Generic;
using UnityEditor;

namespace UMA.TexturePaint.Editor
{
    /// <summary>
    /// Finds each UDIM tile's own overlay for a Fill layer made from one tile's overlay. An overlay belongs to a tile when
    /// the character puts it on that tile, or when its name carries the tile number (UMA30_Body_UDIM1001 is tile 1001).
    /// The other tiles' overlays are the same name with their own tile number: first among the overlays the character
    /// puts on that tile, then in the Global Library, then anywhere in the project.
    /// </summary>
    internal static class TexturePaintUdimTileOverlays
    {
        /// <summary>One tile of the UDIM target: its number and the overlays the character puts on it (may be empty).</summary>
        internal struct Tile
        {
            public int number;
            public IReadOnlyList<OverlayDataAsset> ownOverlays;
        }

        /// <summary>The tile the picked overlay belongs to, or 0 when it belongs to none (it then stays a shared source).</summary>
        internal static int TileOf(OverlayDataAsset picked, IReadOnlyList<Tile> tiles)
        {
            if (picked == null || tiles == null) return 0;
            foreach (Tile tile in tiles)
                if (tile.ownOverlays != null && Contains(tile.ownOverlays, picked)) return tile.number;
            string name = NameOf(picked);
            int best = 0, bestIndex = -1;
            foreach (Tile tile in tiles)
            {
                if (tile.number <= 0) continue;
                int index = name.LastIndexOf(tile.number.ToString(), StringComparison.Ordinal);
                if (index > bestIndex) { best = tile.number; bestIndex = index; }
            }
            return best;
        }

        /// <summary>The picked overlay's name with its tile number replaced by another tile's.</summary>
        internal static string NameForTile(OverlayDataAsset picked, int pickedTile, int tile)
        {
            string name = NameOf(picked);
            string from = pickedTile.ToString();
            int index = name.LastIndexOf(from, StringComparison.Ordinal);
            return index < 0 ? null : name.Substring(0, index) + tile + name.Substring(index + from.Length);
        }

        /// <summary>The overlay for <paramref name="tile"/> that matches the picked overlay, or null.</summary>
        internal static OverlayDataAsset Find(OverlayDataAsset picked, int pickedTile, Tile tile)
        {
            string wanted = NameForTile(picked, pickedTile, tile.number);
            if (string.IsNullOrEmpty(wanted)) return null;
            if (tile.ownOverlays != null)
                foreach (OverlayDataAsset own in tile.ownOverlays)
                    if (own != null && Matches(own, wanted)) return own;
            OverlayDataAsset indexed = UMAAssetIndexer.Instance != null
                ? UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>(wanted) : null;
            if (indexed != null) return indexed;
            foreach (string guid in AssetDatabase.FindAssets(wanted + " t:OverlayDataAsset"))
            {
                OverlayDataAsset candidate = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && Matches(candidate, wanted)) return candidate;
            }
            return null;
        }

        private static bool Matches(OverlayDataAsset overlay, string name) =>
            string.Equals(overlay.overlayName, name, StringComparison.Ordinal) ||
            string.Equals(overlay.name, name, StringComparison.Ordinal);

        private static string NameOf(OverlayDataAsset overlay) =>
            !string.IsNullOrEmpty(overlay.overlayName) ? overlay.overlayName : overlay.name;

        private static bool Contains(IReadOnlyList<OverlayDataAsset> overlays, OverlayDataAsset overlay)
        {
            for (int i = 0; i < overlays.Count; i++) if (ReferenceEquals(overlays[i], overlay)) return true;
            return false;
        }
    }
}
