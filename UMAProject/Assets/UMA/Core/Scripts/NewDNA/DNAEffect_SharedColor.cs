using UMA.CharacterSystem;
using UnityEngine;
using static UMA.DNAEffect_SharedColorChannel;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    public class DNAEffect_SharedColor : DNAEffect
    {
        public enum CombinationMethod
        {
            Additive,
            Subtractive,
            Multiply,
            Replace
        }

        public string sharedColorName = "SharedColor";
        public Color FromColor;
        public Color ToColor;
        public CombinationMethod colorCombineMethod = CombinationMethod.Additive;
        [Tooltip("The texture number in the overlay to apply this to. This is used to identify which texture number to modify in the shared color.")]
        public int TextureNumber; // the number of the texture in the overlay to apply this to.
        [Tooltip("The type of color application. OverlayColorDatas have both multipliers, and additive colors. BaseMultiplier applies the value as a multiplier to the texture color (Colorizes or darkens), while Additive adds the value to the texture color (brightens)")]
        public ColorType colorType = ColorType.BaseMultiplier; // The type of color application, either BaseMultiplier or Additive.

        public override string Description => "Sets a shared color value for the avatar. This can be used to modify the color of specific channels in a shared color, using a combination method to apply the base modifier.";
#if UNITY_EDITOR
        public override void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription, showHelp);
            sharedColorName = UnityEditor.EditorGUILayout.TextField("Shared Color Name", sharedColorName);
            FromColor = UnityEditor.EditorGUILayout.ColorField("From Color", FromColor);
            ToColor = UnityEditor.EditorGUILayout.ColorField("To Color", ToColor);
            colorCombineMethod = (CombinationMethod)UnityEditor.EditorGUILayout.EnumPopup("Combination Method", colorCombineMethod);
            TextureNumber = UnityEditor.EditorGUILayout.IntField("Texture Number", TextureNumber);
            colorType = (ColorType)UnityEditor.EditorGUILayout.EnumPopup("Color Type", colorType);
        }
#endif
        // Updating a sharedcolor only touches textures
        public override DNAInstanceCollection.DNABuildType AreaEffect => DNABuildType.Texture;
        public override void AfterRecipeGenerated(DynamicCharacterAvatar avatar, DNA dna, float value)
        {            
            base.AfterRecipeGenerated(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(sharedColorName))
            {
                value = GetMappedValue(value);
                OverlayColorData sharedColor = avatar.GetColor(sharedColorName);
                if (sharedColor == null)
                {
                    return;
                }
                if (sharedColor.channelCount < TextureNumber + 1)
                {
                    sharedColor.EnsureChannels(TextureNumber + 1);
                }
                Color col = FromColor;
                // Combine the base modifier with the existing color based on the combination method
                switch (colorCombineMethod)
                {
                    case CombinationMethod.Additive:
                        col += ToColor * value;
                        break;
                    case CombinationMethod.Subtractive:
                        col -= ToColor * value;
                        break;
                    case CombinationMethod.Multiply:
                        col *= ToColor * value;
                        break;
                    case CombinationMethod.Replace:
                        col = ToColor * value;
                        break;
                }
                sharedColor.SetColor(TextureNumber, colorType == ColorType.Additive, col);

            }
        }
    }
}
