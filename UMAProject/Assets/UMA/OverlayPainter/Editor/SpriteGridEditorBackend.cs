using System;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    /// <summary>
    /// Dependency-neutral contract for Unity's optional 2D Sprite editor package.
    /// Implementations live in a package-constrained assembly so Overlay Painter remains
    /// usable when com.unity.2d.sprite is not installed.
    /// </summary>
    public interface ISpriteGridEditorBackend
    {
        bool TryReadSpriteRects(
            TextureImporter importer,
            out RectInt[] rects,
            out string errorMessage);

        void ApplySpriteRects(
            TextureImporter importer,
            string baseName,
            RectInt[] rects);
    }

    public static class SpriteGridEditorBackend
    {
        private static ISpriteGridEditorBackend backend;

        public static bool IsAvailable => backend != null;

        public static ISpriteGridEditorBackend Current => backend;

        public static void Register(ISpriteGridEditorBackend implementation)
        {
            backend = implementation ?? throw new ArgumentNullException(nameof(implementation));
        }
    }
}
