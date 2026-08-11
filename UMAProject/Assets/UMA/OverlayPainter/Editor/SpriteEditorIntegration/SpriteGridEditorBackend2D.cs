using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    [InitializeOnLoad]
    internal sealed class SpriteGridEditorBackend2D : ISpriteGridEditorBackend
    {
        static SpriteGridEditorBackend2D()
        {
            SpriteGridEditorBackend.Register(new SpriteGridEditorBackend2D());
        }

        public bool TryReadSpriteRects(
            TextureImporter importer,
            out RectInt[] rects,
            out string errorMessage)
        {
            rects = Array.Empty<RectInt>();
            errorMessage = null;
            if (importer == null)
            {
                errorMessage = "The selected source does not use a TextureImporter.";
                return false;
            }

            ISpriteEditorDataProvider provider = CreateProvider(importer);
            if (provider == null)
            {
                errorMessage = "The selected source has no readable sprite setup.";
                return false;
            }

            SpriteRect[] spriteRects = provider.GetSpriteRects() ?? Array.Empty<SpriteRect>();
            rects = spriteRects.Select(item => new RectInt(
                Mathf.RoundToInt(item.rect.x),
                Mathf.RoundToInt(item.rect.y),
                Mathf.RoundToInt(item.rect.width),
                Mathf.RoundToInt(item.rect.height))).ToArray();
            return true;
        }

        public void ApplySpriteRects(
            TextureImporter importer,
            string baseName,
            RectInt[] rects)
        {
            if (importer == null)
                throw new ArgumentNullException(nameof(importer));
            if (rects == null)
                throw new ArgumentNullException(nameof(rects));

            ISpriteEditorDataProvider provider = CreateProvider(importer);
            if (provider == null)
                throw new InvalidOperationException("Unity sprite data provider is unavailable.");

            SpriteRect[] existing = provider.GetSpriteRects() ?? Array.Empty<SpriteRect>();
            Dictionary<string, GUID> idsByName = existing
                .GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.First().spriteID);
            var spriteRects = new SpriteRect[rects.Length];
            var nameIdPairs = new SpriteNameFileIdPair[rects.Length];
            for (int i = 0; i < rects.Length; i++)
            {
                string spriteName = $"{baseName}_{i}";
                GUID spriteId = idsByName.TryGetValue(spriteName, out GUID existingId)
                    ? existingId
                    : i < existing.Length ? existing[i].spriteID : GUID.Generate();
                spriteRects[i] = new SpriteRect
                {
                    name = spriteName,
                    rect = new Rect(rects[i].x, rects[i].y, rects[i].width, rects[i].height),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = spriteId
                };
                nameIdPairs[i] = new SpriteNameFileIdPair(spriteName, spriteId);
            }

            provider.SetSpriteRects(spriteRects);
            ISpriteNameFileIdDataProvider nameProvider =
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameProvider?.SetNameFileIdPairs(nameIdPairs);
            provider.Apply();
        }

        private static ISpriteEditorDataProvider CreateProvider(TextureImporter importer)
        {
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider provider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            provider?.InitSpriteEditorDataProvider();
            return provider;
        }
    }
}
