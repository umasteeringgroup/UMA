using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    // Values are serialized: append new kinds instead of changing existing ordinals.
    public enum TexturePaintMaskEffectKind
    {
        Fill, Texture, PaintedMask, LayerReference, Noise, Turbulence, Voronoi, Cells,
        Gradient, RadialGradient, Stripes, Checker, Dots,
        Invert, Levels, Curves, BrightnessContrast, Gamma, Threshold, Posterize, Clamp, Remap, Smoothstep,
        Blur, DirectionalBlur, Sharpen, HighPass, Dilate, Erode, Outline, EdgeDetect,
        Distance, Feather, Transform, Warp,
        Curvature, AmbientOcclusion, Thickness, WorldNormal, WorldPosition, MeshID,
        EdgeWear, CavityDirt, Dust, AnatomicalRegion
    }
    [Serializable]
    public sealed class TexturePaintMaskReferenceCache
    {
        public string effectId;
        public TexturePaintRegion pixels;
        public static List<TexturePaintMaskReferenceCache> CloneList(List<TexturePaintMaskReferenceCache> source)
        {
            var result = new List<TexturePaintMaskReferenceCache>();
            if (source != null) foreach (var item in source)
                if (item != null) result.Add(new TexturePaintMaskReferenceCache { effectId = item.effectId, pixels = item.pixels?.Clone() });
            return result;
        }
    }
    public enum TexturePaintMaskBlend { Replace, Multiply, Add, Subtract, Screen, Overlay, Min, Max, Difference, SoftLight, Divide }

    [Serializable]
    public sealed class TexturePaintMaskEffect
    {
        public string id = Guid.NewGuid().ToString("N"), name;
        public TexturePaintMaskEffectKind kind;
        public bool enabled = true, expanded = true, invert, repeat = true;
        [Range(0, 1)] public float opacity = 1;
        public TexturePaintMaskBlend blend;
        public Texture2D texture;
        public TexturePaintLayerMaskTextureChannel channel;
        public TexturePaintLayerReference reference = new TexturePaintLayerReference();
        public TexturePaintRegionProfile regionProfile;
        public TexturePaintAnatomicalSettings regionSettings = new TexturePaintAnatomicalSettings();
        // Union members are evaluated together before this effect restricts the existing mask.
        public List<TexturePaintAnatomicalSettings> regionUnion = new List<TexturePaintAnatomicalSettings>();

        public List<TexturePaintAnatomicalSettings> ResolveRegions()
        {
            var result = new List<TexturePaintAnatomicalSettings>();
            Add(regionSettings ?? new TexturePaintAnatomicalSettings());
            if (regionUnion != null) foreach (var entry in regionUnion) if (entry != null) Add(entry);
            return result;
            void Add(TexturePaintAnatomicalSettings entry)
            {
                var settings = (regionProfile?.Get(entry.region) ?? entry).Clone();
                settings.side = entry.side;
                settings.Normalize();
                result.Add(settings);
            }
        }
        public Vector2 tiling = Vector2.one, offset;
        public float rotation;
        public int seed, octaves = 4, steps = 4;
        public float value = 1, amount = 1, radius = 4, threshold = .5f, softness = .1f;
        public float inputMin, inputMax = 1, outputMin, outputMax = 1, gamma = 1;
        public Vector3 direction = Vector3.up;
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
        public TexturePaintMaskEffect Clone()
        {
            var copy = (TexturePaintMaskEffect)MemberwiseClone();
            copy.reference = reference?.Clone() ?? new TexturePaintLayerReference();
            copy.regionSettings = regionSettings?.Clone() ?? new TexturePaintAnatomicalSettings();
            copy.regionUnion = new List<TexturePaintAnatomicalSettings>();
            if (regionUnion != null) foreach (var entry in regionUnion) if (entry != null) copy.regionUnion.Add(entry.Clone());
            copy.curve = curve == null ? AnimationCurve.Linear(0, 0, 1, 1) :
                new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
            return copy;
        }
        private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
        public void Normalize()
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            opacity = Mathf.Clamp01(Finite(opacity, 1)); radius = Mathf.Clamp(Finite(radius, 4), 0, 128);
            value = Finite(value, 1); amount = Mathf.Clamp(Finite(amount, 1), -100, 100);
            threshold = Mathf.Clamp01(Finite(threshold, .5f)); softness = Mathf.Clamp(Finite(softness, .1f), 0, 1);
            inputMin = Finite(inputMin, 0); inputMax = Mathf.Max(inputMin + .00001f, Finite(inputMax, 1));
            // Voronoi uses the existing union fields for black expansion and contrast.
            if (kind == TexturePaintMaskEffectKind.Voronoi)
            { inputMin = Mathf.Clamp01(inputMin); amount = Mathf.Clamp(amount, 1, 8); }
            outputMin = Finite(outputMin, 0); outputMax = Finite(outputMax, 1);
            gamma = Mathf.Clamp(Finite(gamma, 1), .01f, 10);
            octaves = Mathf.Clamp(octaves, 1, 8); steps = Mathf.Clamp(steps, 2, 256);
            tiling = new Vector2(Finite(tiling.x, 1), Finite(tiling.y, 1));
            offset = new Vector2(Finite(offset.x, 0), Finite(offset.y, 0)); rotation = Finite(rotation, 0);
            direction = new Vector3(Finite(direction.x, 0), Finite(direction.y, 1), Finite(direction.z, 0));
            curve ??= AnimationCurve.Linear(0, 0, 1, 1);
            reference ??= new TexturePaintLayerReference();
            regionSettings ??= new TexturePaintAnatomicalSettings();
            regionSettings.Normalize();
            regionUnion ??= new List<TexturePaintAnatomicalSettings>();
            regionUnion.RemoveAll(entry => entry == null);
            foreach (var entry in regionUnion) entry.Normalize();
        }
        public static TexturePaintMaskEffect Create(TexturePaintMaskEffectKind kind)
        {
            var effect = new TexturePaintMaskEffect { kind = kind, enabled = true, opacity = 1f,
                repeat = kind < TexturePaintMaskEffectKind.Invert };
            if (kind == TexturePaintMaskEffectKind.MeshID) effect.channel = TexturePaintLayerMaskTextureChannel.Red;
            if (kind == TexturePaintMaskEffectKind.PaintedMask || kind == TexturePaintMaskEffectKind.AnatomicalRegion)
                effect.blend = TexturePaintMaskBlend.Multiply;
            if (kind >= TexturePaintMaskEffectKind.Noise && kind <= TexturePaintMaskEffectKind.Dots)
                effect.tiling = new Vector2(8, 8);
            if (kind == TexturePaintMaskEffectKind.Gradient || kind == TexturePaintMaskEffectKind.RadialGradient)
                effect.tiling = Vector2.one;
            if (kind == TexturePaintMaskEffectKind.BrightnessContrast) { effect.value = 0; effect.amount = 1; }
            return effect;
        }
    }
}
