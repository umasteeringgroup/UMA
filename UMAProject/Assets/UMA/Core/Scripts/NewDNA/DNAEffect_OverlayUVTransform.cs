using UMA.CharacterSystem;
using UnityEngine;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    [System.Serializable]
    public class DNAEffect_OverlayUVTransform : DNAEffect
    {
        public string overlayName;
        public Vector2 offset;
        public Vector2 scale = Vector2.one;
        [Range(0.0f, 360.0f)]
        public float rotation = 0.0f;

        public override DNABuildType AreaEffect
        {
            get
            {
                return DNABuildType.Texture;
            }
        }
        public override string Description
        {
            get
            {
                return "This effect applies a UV transform to the specified overlay.";
            }
        }
#if UNITY_EDITOR
        override public void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription, showHelp);
            overlayName = UnityEditor.EditorGUILayout.TextField("Overlay Name", overlayName);
            if (string.IsNullOrEmpty(overlayName))
            {
                UnityEditor.EditorGUILayout.HelpBox("Overlay Name is required.", UnityEditor.MessageType.Error);
            }
            offset = UnityEditor.EditorGUILayout.Vector2Field("Offset", offset);
            scale = UnityEditor.EditorGUILayout.Vector2Field("Scale", scale);
            rotation = UnityEditor.EditorGUILayout.Slider("Rotation", rotation, 0.0f, 360.0f);
        }
#endif
        public override void AfterRecipeGenerated(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            base.AfterRecipeGenerated(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(overlayName))
            {
                var overlay = avatar.umaData.umaRecipe.FindFirstOverlay(overlayName);
                if (overlay != null)
                {
                    value = GetMappedValue(value);
                    overlay.Translate = offset * value;
                    overlay.Scale = scale * value;
                    overlay.Rotation = rotation * value;
                }
            }
        }
    }
}