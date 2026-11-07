using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [Serializable]
    public sealed class OverlayPainterSpriteSheet
    {
        public TexturePaintChannel channel = TexturePaintChannel.Albedo;
        public Texture2D spriteSheet;
        public bool inverted;
        [Tooltip("Optional individual sprites, in material order. When present these replace the sheet, allowing sets assembled from separate stamp images.")]
        public List<Sprite> sprites = new List<Sprite>();

        public string SheetName => spriteSheet != null ? spriteSheet.name : channel + " Sprites";
        public bool HasSource => spriteSheet != null || sprites?.Count > 0;
    }

    [CreateAssetMenu(fileName = "OverlayPainterSpriteSet",
        menuName = "UMA/Overlay Painter/Sprite Set")]
    public sealed class OverlayPainterSpriteSet : ScriptableObject
    {
        public string setName = "Sprite Set";
        public List<OverlayPainterSpriteSheet> spriteSheets = new List<OverlayPainterSpriteSheet>();
        public List<string> spriteNames = new List<string>();
        public const string FillMaterial = "Fill Material";
        public const string Projection = "Projection";
        public const string Path = "Path";
        public static readonly string[] UsageTypes = { FillMaterial, Projection, Path };
        [Tooltip("A set can belong to several usage categories. The browser category determines the layer created by dragging.")]
        public string[] categories = { FillMaterial };
        [HideInInspector] public List<Texture2D> thumbnails = new List<Texture2D>();
        [HideInInspector] public string thumbnailSourceHash;
        [HideInInspector] public List<Texture2D> cubeThumbnails = new List<Texture2D>();
        [HideInInspector] public string cubeThumbnailSourceHash;

        public bool HasCategory(string category)
        {
            if (categories == null || categories.Length == 0) return category == FillMaterial;
            foreach (string item in categories)
                if (string.Equals(item?.Trim(), category, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public Texture2D GetThumbnail(int index) => thumbnails != null && (uint)index < (uint)thumbnails.Count
            ? thumbnails[index] : null;

        public Texture2D GetThumbnail(int index, string category)
        {
            bool cube = string.Equals(category, Projection, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, Path, StringComparison.OrdinalIgnoreCase);
            return cube ? cubeThumbnails != null && (uint)index < (uint)cubeThumbnails.Count ? cubeThumbnails[index] : null
                : GetThumbnail(index);
        }

        public string DisplayName => string.IsNullOrWhiteSpace(setName) ? name : setName.Trim();

        public string GetSpriteName(int spriteIndex, string fallbackName = null)
        {
            if (spriteNames != null && (uint)spriteIndex < (uint)spriteNames.Count &&
                !string.IsNullOrWhiteSpace(spriteNames[spriteIndex]))
                return spriteNames[spriteIndex].Trim();
            return string.IsNullOrWhiteSpace(fallbackName)
                ? $"Sprite {spriteIndex + 1}"
                : fallbackName;
        }
    }
}
