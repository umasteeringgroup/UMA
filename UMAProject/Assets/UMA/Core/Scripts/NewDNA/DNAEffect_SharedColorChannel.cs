using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Shared color channel for DNA effects.
    /// This class is a placeholder for shared color channel functionality.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_SharedColorChannel : DNAEffect
    {
        public override string Description => "Sets a shared color on the avatar. This can be used to modify the color of specific color components (Red, Green, Blue, Alpha) for a specific texture channel in a shared color.";
        public enum ColorComponent
        {
            Red,
            Green,
            Blue,
            Alpha
        }

        public enum ColorType
        {
            BaseMultiplier,
            Additive
        }

        [Tooltip("The value to set for the color channel. This is a multiplier applied to the mapped value. For example, if ChannelValue is 1.0, the mapped value will be used directly. If ChannelValue is 0.5, the mapped value will be halved.")]
        public float ChannelValue = 1.0f;
        [Tooltip("The name of the shared color to modify. This should match the name used in the character colors.")]
        public string SharedColorName = "Skin";
        [Tooltip("The color channel to modify. This can be Red, Green, Blue, or Alpha.")]
        public ColorComponent colorComponent = ColorComponent.Red;

        [Tooltip("The texture number in the overlay to apply this to. This is used to identify which texture number to modify in the shared color.")]
        public int TextureNumber; // the number of the texture in the overlay to apply this to.
        [Tooltip("The type of color application. OverlayColorDatas have both multipliers, and additive colors. BaseMultiplier applies the value as a multiplier to the texture color (Colorizes or darkens), while Additive adds the value to the texture color (brightens)")]
        public ColorType colorType = ColorType.BaseMultiplier; // The type of color application, either BaseMultiplier or Additive.


        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Texture;

        public override void AfterRecipeGenerated(UMAData avatar, DNA dna, float value)
        {
            base.PreApply(avatar, dna, value);
            if (avatar is not DynamicCharacterAvatar)
            {
                Debug.LogError("DNAEffect_SharedColorChannel: Avatar is not a DynamicCharacterAvatar.");
                return;
            }
            if (avatar != null && !string.IsNullOrEmpty(SharedColorName))
            {
                value = GetMappedValue(value);

                OverlayColorData sharedColor = ((DynamicCharacterAvatar)avatar).GetColor(SharedColorName);
                if (sharedColor == null)
                {
                    return;
                }

                if (sharedColor.channelCount < TextureNumber + 1)
                {
                    sharedColor.EnsureChannels(TextureNumber + 1);
                }

                Color col = sharedColor.GetColor(TextureNumber, colorType == ColorType.Additive);

                // replace the color channel based on the selected component

                switch (colorComponent)
                {
                    case ColorComponent.Red:
                        col.r = value * ChannelValue;
                        break;
                    case ColorComponent.Green:
                        col.g = value * ChannelValue;
                        break;
                    case ColorComponent.Blue:
                        col.b = value * ChannelValue;
                        break;
                    case ColorComponent.Alpha:
                        col.a = value * ChannelValue;
                        break;
                }
                sharedColor.SetColor(TextureNumber, colorType == ColorType.Additive, col);
            }
        }
#if UNITY_EDITOR
        public override void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription, showHelp);
            SharedColorName = UnityEditor.EditorGUILayout.TextField("Shared Color Name", SharedColorName);
            ChannelValue = UnityEditor.EditorGUILayout.Slider("Component Value", ChannelValue, 0.0f, 1.0f);
            colorComponent = (ColorComponent)UnityEditor.EditorGUILayout.EnumPopup("Color Component", colorComponent);
            TextureNumber = UnityEditor.EditorGUILayout.IntField("Texture Number", TextureNumber);
            colorType = (ColorType)UnityEditor.EditorGUILayout.EnumPopup("Color Type", colorType);
        }
#endif
    }
}
