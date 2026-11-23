using UMA.CharacterSystem;
using UnityEngine;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    /// <summary>
    /// DNA effect that moves, scales and turns the first overlay with a matching name in the combined
    /// recipe (executed in the AfterRecipeGenerated stage). A mapped value of 0 leaves the overlay
    /// untransformed and 1 gives the full offset, scale and rotation.
    /// The effect sets the overlay's instance transform, so a transform stored in the recipe for that
    /// overlay is replaced. Only overlays drawn on top of a slot's base overlay can be transformed.
    /// </summary>
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
        public override ExpressionEffectPhase ExpressionPhases =>
            ExpressionEffectPhase.BuildAfterRecipe;

        // The transform is written outright, so the default DNA value must write it too.
        // Otherwise the overlay would keep the transform of the last value that was not the default.
        public override bool AppliesAtDefaultValue => true;

        public override string Description
        {
            get
            {
                return "Moves, scales and turns the named overlay. A translated value of 0 leaves the overlay untransformed; 1 gives the full Offset, Scale and Rotation. This replaces any instance transform the recipe stores for that overlay, and it does not work on a slot's base overlay.";
            }
        }
#if UNITY_EDITOR
        /// <inheritdoc />
        public override void DoGui(bool showDescription, bool showHelp, out AnimationCurve curveToCopy)
        {
            base.DoGui(showDescription, showHelp, out curveToCopy);
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
        /// <inheritdoc />
        public override void AfterRecipeGenerated(UMAData avatar, DNA dna, float value)
        {
            base.AfterRecipeGenerated(avatar, dna, value);
            if (avatar == null || avatar.umaRecipe == null || string.IsNullOrEmpty(overlayName))
            {
                return;
            }

            var overlay = avatar.umaRecipe.FindFirstOverlay(overlayName);
            if (overlay == null)
            {
                return;
            }

            GetTransform(GetMappedValue(value), out Vector2 translate, out Vector2 overlayScale, out float overlayRotation);
            bool transformed = translate != Vector2.zero || overlayScale != Vector2.one || overlayRotation != 0f;
            if (!transformed && !overlay.instanceTransformed)
            {
                // Nothing to do, and nothing left over from an earlier value to take back.
                return;
            }

            overlay.Translate = translate;
            overlay.Scale = overlayScale;
            overlay.Rotation = overlayRotation;
            // The texture merge only reads the transform of an overlay that is marked as transformed.
            overlay.instanceTransformed = transformed;
        }

        /// <summary>
        /// The overlay transform for a mapped value: nothing at 0, the full offset, scale and rotation at 1.
        /// </summary>
        public void GetTransform(float mappedValue, out Vector2 translate, out Vector2 overlayScale, out float overlayRotation)
        {
            translate = offset * mappedValue;
            // Scale goes from "unchanged" toward the target, and never down to nothing or inside out.
            overlayScale = Vector2.Max(Vector2.LerpUnclamped(Vector2.one, scale, mappedValue), new Vector2(MinimumScale, MinimumScale));
            overlayRotation = Mathf.Repeat(rotation * mappedValue, 360f);
        }

        private const float MinimumScale = 0.001f;
    }
}
