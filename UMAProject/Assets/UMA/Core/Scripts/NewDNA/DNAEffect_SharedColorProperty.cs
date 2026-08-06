using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// DNA effect that writes a color and/or float material property value into a named SharedColor's PropertyBlock.
    /// Useful for shader customization driven by DNA without additional converters.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_SharedColorProperty : DNAEffect
    {
        [System.Flags]
        public enum ParameterType
        {
            Color = 1,
            Float = 2,
            Both = Color | Float
        }

        /// <summary>
        /// Name of the shared color to modify.
        /// </summary>
        public string sharedColorName;
        /// <summary>
        /// Property name inside the material property block.
        /// </summary>
        public string propertyName;
        public ParameterType parameterType = ParameterType.Color;
        /// <summary>
        /// Base float multiplier applied to mapped DNA value when Float is selected.
        /// </summary>
        public float floatValue = 0.0f;
        /// <summary>
        /// Color used when mapped value is 0.
        /// </summary>
        public Color zeroColorValue;
        /// <summary>
        /// Color used when mapped value is 1.
        /// </summary>
        public Color oneColorValue;

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Texture;
        public override ExpressionEffectPhase ExpressionPhases =>
            ExpressionEffectPhase.BuildAfterRecipe;

        public override string Description => "Sets a shared color property for the avatar. This can be used to modify shader properties or even the UV location of an overlay.";

#if UNITY_EDITOR
        /// <inheritdoc />
        public override void DoGui(bool showDescription, bool showHelp, out AnimationCurve curveToCopy)
        {
            base.DoGui(showDescription, showHelp, out curveToCopy);
            sharedColorName = UnityEditor.EditorGUILayout.TextField("Shared Color Name", sharedColorName);
            if (string.IsNullOrEmpty(sharedColorName))
            {
                UnityEditor.EditorGUILayout.HelpBox("Shared Color Name is required.", UnityEditor.MessageType.Error);
            }
            propertyName = UnityEditor.EditorGUILayout.TextField("Property Name", propertyName);
            if (string.IsNullOrEmpty(propertyName))
            {
                UnityEditor.EditorGUILayout.HelpBox("Property Name is required.", UnityEditor.MessageType.Error);
            }
            parameterType = (ParameterType)UnityEditor.EditorGUILayout.EnumFlagsField("Parameter Type", parameterType);
            if (parameterType.HasFlag(ParameterType.Color))
            {
                zeroColorValue = UnityEditor.EditorGUILayout.ColorField("Zero Color Value", zeroColorValue);
                oneColorValue = UnityEditor.EditorGUILayout.ColorField("One Color Value", oneColorValue);
            }
            if (parameterType.HasFlag(ParameterType.Float))
            {
                floatValue = UnityEditor.EditorGUILayout.FloatField("Zero Float Value", floatValue);
            }
        }
#endif

        /// <inheritdoc />
        public override void AfterRecipeGenerated(UMAData avatar, DNA dna, float value)
        {
            if (avatar is not DynamicCharacterAvatar)
            {
                Debug.LogError("DNAEffect_SharedColorChannel: Avatar is not a DynamicCharacterAvatar.");
                return;
            }
            base.AfterRecipeGenerated(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(sharedColorName))
            {
                value = GetMappedValue(value);

                // get the shared color from the DynamicCharacterAvatar, if it exists
                OverlayColorData sharedColor = (avatar as DynamicCharacterAvatar).GetColor(sharedColorName);
                if (sharedColor == null)
                {
                    return;
                }
                if (sharedColor.PropertyBlock == null)
                {
                    sharedColor.PropertyBlock = new UMAMaterialPropertyBlock();
                }
                if (parameterType.HasFlag(ParameterType.Color))
                {
                    Color colorValue = Color.Lerp(zeroColorValue, oneColorValue, value);
                    UMAColorProperty colorProp = (UMAColorProperty)sharedColor.PropertyBlock.GetProperty<UMAColorProperty>(propertyName);
                    if (colorProp == null)
                    {
                        colorProp = new UMAColorProperty();
                        colorProp.name = propertyName;
                        sharedColor.PropertyBlock.AddProperty(colorProp);
                    }
                    colorProp.Value = colorValue;
                }
                if (parameterType.HasFlag(ParameterType.Float))
                {
                    UMAFloatProperty floatProp = (UMAFloatProperty)sharedColor.PropertyBlock.GetProperty<UMAFloatProperty>(propertyName);
                    if (floatProp == null)
                    {
                        floatProp = new UMAFloatProperty();
                        floatProp.name = propertyName;
                        sharedColor.PropertyBlock.AddProperty(floatProp);
                    }
                    floatProp.Value = value * floatValue;
                }
            }
        }
    }
}
