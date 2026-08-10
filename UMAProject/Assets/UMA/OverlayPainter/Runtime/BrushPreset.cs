using UnityEngine;
using UnityEngine.Serialization;

namespace UMA.TexturePaint
{
    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Brush Preset", fileName = "Overlay Painter Brush")]
    public sealed class BrushPreset : ScriptableObject
    {
        public enum Shape { Circle, Square, Stamp }

        public Shape shape = Shape.Circle;
        public Texture2D stampTexture;
        public Sprite stampSprite;
        [Min(0.0001f)] public float size = 0.05f;
        [Range(0f, 1f)] public float hardness = 0.75f;
        [Range(0f, 1f)] public float flow = 1f;
        [Range(0.01f, 10f)] public float spacing = 0.2f;
        [Range(-180f, 180f)] public float rotation;
        public TexturePaintBlendMode blendMode = TexturePaintBlendMode.Normal;
        public bool mirrorStroke;
        public bool alignToStroke;
        public bool randomRotation;
        [FormerlySerializedAs("randomHeightVariation")]
        public bool randomSizeVariation;
        [FormerlySerializedAs("randomHeightShrink"), Range(0f, 1f)]
        public float randomSizeShrink = 0.3f;
        [FormerlySerializedAs("randomHeightGrow"), Range(0f, 1f)]
        public float randomSizeGrow = 0.3f;
        public bool splatter;
        [Range(0.01f, 2f)] public float splatterDistance = 1f;
        [Tooltip("Randomly varies each splatter stamp from zero to the current paint strength.")]
        public bool randomStrength;
        public bool fade;
        public bool taper;
        [Tooltip("World-space distance over which Fade and Taper reach zero. Zero uses three times the brush size.")]
        [Min(0f)] public float fadeTaperLength;
        [Tooltip("Comma-separated search tags used by the Overlay Painter asset shelf.")]
        public string tags;

        public float StampSpacing => Mathf.Max(0.0001f, size * spacing);
        public float ResolvedFadeTaperLength => fadeTaperLength > 0.000001f
            ? fadeTaperLength : Mathf.Max(0.0001f, size * 3f);
        public Texture2D ResolvedStampTexture => TexturePaintSpriteSource.Resolve(
            stampSprite == null ? stampTexture : null, stampSprite);

        /// <summary>
        /// Copies the paint-producing settings from another preset. Shelf metadata such as tags
        /// intentionally remains owned by the destination asset.
        /// </summary>
        public void CopyPaintSettingsFrom(BrushPreset source)
        {
            if (source == null) return;
            shape = source.shape;
            stampTexture = source.stampSprite == null ? source.stampTexture : null;
            stampSprite = source.stampSprite;
            size = source.size;
            hardness = source.hardness;
            flow = source.flow;
            spacing = source.spacing;
            rotation = source.rotation;
            blendMode = source.blendMode;
            mirrorStroke = source.mirrorStroke;
            alignToStroke = source.alignToStroke;
            randomRotation = source.randomRotation;
            randomSizeVariation = source.randomSizeVariation;
            randomSizeShrink = source.randomSizeShrink;
            randomSizeGrow = source.randomSizeGrow;
            splatter = source.splatter;
            splatterDistance = source.splatterDistance;
            randomStrength = source.randomStrength;
            fade = source.fade;
            taper = source.taper;
            fadeTaperLength = source.fadeTaperLength;
        }

        private void OnValidate()
        {
            randomSizeShrink = Mathf.Clamp01(randomSizeShrink);
            randomSizeGrow = Mathf.Clamp01(randomSizeGrow);
            splatterDistance = Mathf.Clamp(splatterDistance, 0.01f, 2f);
            fadeTaperLength = Mathf.Max(0f, fadeTaperLength);
        }
    }
}
