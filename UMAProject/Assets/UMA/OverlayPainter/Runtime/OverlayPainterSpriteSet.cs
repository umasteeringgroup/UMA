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

        public string SheetName => spriteSheet != null ? spriteSheet.name : string.Empty;
    }

    [CreateAssetMenu(fileName = "OverlayPainterSpriteSet",
        menuName = "UMA/Overlay Painter/Sprite Set")]
    public sealed class OverlayPainterSpriteSet : ScriptableObject
    {
        public string setName = "Sprite Set";
        public List<OverlayPainterSpriteSheet> spriteSheets = new List<OverlayPainterSpriteSheet>();
        public List<string> spriteNames = new List<string>();

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
