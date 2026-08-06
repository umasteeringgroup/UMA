using UnityEngine;

namespace UMA.TexturePaint
{
    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Brush Preset", fileName = "Overlay Painter Brush")]
    public sealed class BrushPreset : ScriptableObject
    {
        public enum Shape { Circle, Square, Stamp }

        public Shape shape = Shape.Circle;
        public Texture2D stampTexture;
        [Min(0.0001f)] public float size = 0.05f;
        [Range(0f, 1f)] public float hardness = 0.75f;
        [Range(0f, 1f)] public float flow = 1f;
        [Range(0.01f, 10f)] public float spacing = 0.2f;
        [Range(-180f, 180f)] public float rotation;
        public TexturePaintBlendMode blendMode = TexturePaintBlendMode.Normal;
        public bool mirrorStroke;
        public bool alignToStroke;
        [Tooltip("Comma-separated search tags used by the Overlay Painter asset shelf.")]
        public string tags;

        public float StampSpacing => Mathf.Max(0.0001f, size * spacing);
    }
}
