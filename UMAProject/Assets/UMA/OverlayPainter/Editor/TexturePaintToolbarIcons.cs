using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    /// <summary>Shared, non-interactive icon treatment for every painter toolbar.</summary>
    internal static class TexturePaintToolbarIcons
    {
        internal static VisualElement Create(Sprite sprite)
        {
            return CreateLayers(sprite, null);
        }

        internal static void Add(VisualElement toolbarElement, Texture texture)
        {
            if (texture == null) return;
            toolbarElement.style.flexDirection = FlexDirection.Row;
            toolbarElement.style.alignItems = Align.Center;
            toolbarElement.Insert(0, CreateLayers(null, texture));
        }

        private static VisualElement CreateLayers(Sprite sprite, Texture texture)
        {
            var container = new VisualElement { pickingMode = PickingMode.Ignore };
            container.AddToClassList("uma-toolbar-icon");
            container.style.width = 18f;
            container.style.height = 18f;
            container.style.alignSelf = Align.Center;
            container.style.flexShrink = 0f;
            container.style.overflow = Overflow.Visible;

            // Tinting multiplies RGB but preserves the source alpha, so the effect follows
            // the artwork's silhouette rather than drawing a box around its sprite rectangle.
            AddLayer(2f, 3f, new Color(0f, 0f, 0f, 0.18f));
            AddLayer(1f, 2f, new Color(0f, 0f, 0f, 0.35f));
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0)
                        AddLayer(x * 0.75f, y * 0.75f, new Color(0f, 0f, 0f, 0.8f));
            AddLayer(0f, 0f, Color.white);
            return container;

            void AddLayer(float x, float y, Color tint)
            {
                var image = new Image
                {
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore,
                    tintColor = tint
                };
                if (sprite != null) image.sprite = sprite;
                else image.image = texture;
                image.style.position = Position.Absolute;
                image.style.left = x;
                image.style.top = y;
                image.style.width = 18f;
                image.style.height = 18f;
                container.Add(image);
            }
        }
    }
}
