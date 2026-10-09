using UnityEngine;
namespace UMA.TexturePaint
{
    [CreateAssetMenu(menuName="UMA/Overlay Painter/Garment Generator Preset")]
    public sealed class TexturePaintGarmentPresetAsset : ScriptableObject
    { public TexturePaintGarmentSettings settings = TexturePaintGarmentSettings.Create(TexturePaintGarmentPreset.TensionFolds); }

}
