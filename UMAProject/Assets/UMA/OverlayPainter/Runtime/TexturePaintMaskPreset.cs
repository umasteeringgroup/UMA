using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintSmartMask { CavityDirt, EdgeWear, Dust, PositionGradient, Grunge, SoftBorder, Spots, Scratches }

    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Smart Mask", fileName = "Smart Mask")]
    public sealed class TexturePaintMaskPreset : ScriptableObject
    {
        public int version = 1;
        [TextArea] public string description;
        public TexturePaintLayerMaskEffects effects = new TexturePaintLayerMaskEffects();
        public static TexturePaintLayerMaskEffects CreateRecipe(TexturePaintSmartMask kind)
        {
            var recipe = new TexturePaintLayerMaskEffects { startFromPaint = false, initialValue = 1 };
            var source = TexturePaintMaskEffect.Create(kind switch
            {
                TexturePaintSmartMask.CavityDirt => TexturePaintMaskEffectKind.CavityDirt,
                TexturePaintSmartMask.EdgeWear => TexturePaintMaskEffectKind.EdgeWear,
                TexturePaintSmartMask.Dust => TexturePaintMaskEffectKind.Dust,
                TexturePaintSmartMask.PositionGradient => TexturePaintMaskEffectKind.WorldPosition,
                TexturePaintSmartMask.SoftBorder => TexturePaintMaskEffectKind.RadialGradient,
                TexturePaintSmartMask.Spots => TexturePaintMaskEffectKind.Voronoi,
                TexturePaintSmartMask.Scratches => TexturePaintMaskEffectKind.Noise,
                _ => TexturePaintMaskEffectKind.Noise
            });
            if (kind <= TexturePaintSmartMask.Dust) { source.amount = 3; source.tiling = new Vector2(24, 24); }
            if (kind == TexturePaintSmartMask.Scratches) source.tiling = new Vector2(96, 2);
            recipe.stack.Add(source);
            var curves = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Curves);
            curves.curve = AnimationCurve.EaseInOut(0, 0, 1, 1); recipe.stack.Add(curves);
            recipe.stack.Add(TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.PaintedMask));
            return recipe;
        }
    }
}
