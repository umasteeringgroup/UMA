using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    [System.Serializable]
    public class DNAEffect_SharedColorProperty : DNAEffect
    {
        public enum ParameterType
        {
            Color,
            Float,
            Both = Color | Float
        }

        public string sharedColorName;
        public string propertyName;
        public ParameterType parameterType = ParameterType.Color;
        public float floatValue = 0.0f;
        public Color zeroColorValue;
        public Color oneColorValue;

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Texture;

        public override string Description => "Sets a shared color property for the avatar. This can be used to modify shader properties or even the UV location of an overlay.";

#if UNITY_EDITOR
        public override void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription, showHelp);
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

        public override void AfterRecipeGenerated(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            base.AfterRecipeGenerated(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(sharedColorName))
            {
                value = GetMappedValue(value);

                // get the shared color from the DynamicCharacterAvatar, if it exists
                OverlayColorData sharedColor = avatar.GetColor(sharedColorName);
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