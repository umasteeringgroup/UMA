using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// What one piece of cloth is made of. Each pattern piece has its own, so a cotton shirt can have an
    /// elastic band sewn on.
    /// </summary>
    [Serializable]
    public sealed class FabricSettings
    {
        /// <summary>The preset these values came from, or <see cref="FabricPresets.Custom"/> after a change.</summary>
        public string preset = FabricPresets.DefaultName;
        /// <summary>How easily the cloth stretches (XPBD compliance of an edge). Below about 0.03 the cloth is as firm as the solver can hold it.</summary>
        [Min(0f)] public float stretchCompliance = 1e-8f;
        /// <summary>How easily the cloth bends (XPBD compliance across neighbouring triangles). Higher folds more softly.</summary>
        [Min(0f)] public float bendCompliance = 2e-5f;
        /// <summary>Kilograms per square metre. Heavier cloth hangs and stretches more under its own weight.</summary>
        [Min(0.01f)] public float density = 0.2f;
        /// <summary>How much the cloth grips the body: 0 slides, 1 grips.</summary>
        [Range(0f, 1f)] public float friction = 0.35f;

        public FabricSettings Clone() => (FabricSettings)MemberwiseClone();

        public bool SameValues(FabricSettings other) =>
            other != null && Mathf.Approximately(stretchCompliance, other.stretchCompliance) && Mathf.Approximately(bendCompliance, other.bendCompliance)
            && Mathf.Approximately(density, other.density) && Mathf.Approximately(friction, other.friction);

        public void CopyValuesFrom(FabricSettings other)
        {
            preset = other.preset;
            stretchCompliance = other.stretchCompliance;
            bendCompliance = other.bendCompliance;
            density = other.density;
            friction = other.friction;
        }

        // ---- The artist's sliders: 0 to 1 over the range the solver responds to (measured 2026-11-22) ----

        private const float StretchMin = 0.03f, StretchDecades = 1f;
        private const float BendMin = 0.01f, BendDecades = 4f;

        /// <summary>
        /// 0 firm (cotton, denim) to 1 very elastic. Compliance 0.03 to 0.3 m/N, logarithmic; spandex is about
        /// 0.5. Above about 0.1 a garment hanging from the hips stretches past them under its own weight.
        /// </summary>
        public float Stretch
        {
            get => stretchCompliance <= StretchMin ? 0f : Mathf.Clamp01(Mathf.Log10(stretchCompliance / StretchMin) / StretchDecades);
            set => stretchCompliance = value <= 0.001f ? 1e-8f : StretchMin * Mathf.Pow(10f, Mathf.Clamp01(value) * StretchDecades);
        }

        /// <summary>0 stiff (leather, cotton) to 1 limp. Compliance 0.01 to 100, logarithmic; below 0.01 the solver shows no difference.</summary>
        public float Softness
        {
            get => bendCompliance <= BendMin ? 0f : Mathf.Clamp01(Mathf.Log10(bendCompliance / BendMin) / BendDecades);
            set => bendCompliance = BendMin * Mathf.Pow(10f, Mathf.Clamp01(value) * BendDecades);
        }

        /// <summary>Grams per square metre, as fabric is sold.</summary>
        public float GramsPerSquareMetre
        {
            get => density * 1000f;
            set => density = Mathf.Max(10f, value) / 1000f;
        }
    }

    /// <summary>A named fabric to start from.</summary>
    public sealed class FabricPreset
    {
        public string Name;
        public string Description;
        public FabricSettings Settings;
    }

    /// <summary>
    /// The fabric presets, chosen from measured drapes of the example vest on Human Female 3.0
    /// (2026-11-22, 300 frames). Mean fold angle: cotton 6.6, wool 8.3, silk 9.2 degrees. Below a stretch
    /// compliance of about 0.03 and a bend compliance of about 0.3 the cloth is as firm as the solver can
    /// hold it, so cotton, denim and leather differ mainly in weight and grip. The knits were first set
    /// 5 to 10 times softer (jersey 0.1, elastic 0.3, spandex 0.4), which looked right on the vest but let
    /// leggings, sweatpants and their waistbands stretch past the hips and slide off under their own
    /// weight; at 0.1 and below they hold (measured on the sample leggings, 2026-11-22).
    /// </summary>
    public static class FabricPresets
    {
        public const string DefaultName = "Cotton";
        public const string Custom = "Custom";

        public static readonly IReadOnlyList<FabricPreset> All = new List<FabricPreset>
        {
            Make("Cotton", "Shirting and plain weaves: firm, medium weight. The default.", 1e-8f, 2e-5f, 0.2f, 0.35f),
            Make("Denim", "Heavy twill: firm, stiff and heavy.", 1e-8f, 1e-5f, 0.45f, 0.45f),
            Make("Leather", "Very stiff and heavy, grips the body.", 1e-8f, 1e-5f, 0.8f, 0.6f),
            Make("Wool", "Suiting and knitwear: firm, folds softly, fairly heavy.", 1e-8f, 1f, 0.3f, 0.45f),
            Make("Silk", "Light, limp and slippery: drapes in fine folds.", 1e-8f, 30f, 0.08f, 0.15f),
            Make("Jersey", "T-shirt knit: stretches a little, soft.", 0.05f, 3f, 0.18f, 0.4f),
            Make("Elastic", "Bands, cuffs and ribbing: stretches to fit and grips.", 0.08f, 1f, 0.3f, 0.6f),
            Make("Spandex", "Very elastic and soft: for tight-fitting pieces.", 0.1f, 10f, 0.2f, 0.5f),
        };

        private static FabricPreset Make(string name, string description, float stretch, float bend, float density, float friction) => new FabricPreset
        {
            Name = name,
            Description = description,
            Settings = new FabricSettings { preset = name, stretchCompliance = stretch, bendCompliance = bend, density = density, friction = friction }
        };

        public static FabricPreset Find(string name)
        {
            foreach (FabricPreset p in All) if (p.Name == name) return p;
            return null;
        }

        /// <summary>A new fabric with a preset's values.</summary>
        public static FabricSettings Create(string name)
        {
            FabricPreset p = Find(name) ?? Find(DefaultName);
            return p.Settings.Clone();
        }

        /// <summary>The names for a popup: every preset, then Custom.</summary>
        public static string[] Names()
        {
            var names = new string[All.Count + 1];
            for (int i = 0; i < All.Count; i++) names[i] = All[i].Name;
            names[All.Count] = Custom;
            return names;
        }

        /// <summary>The popup index of a fabric: its preset while the values still match it, otherwise Custom.</summary>
        public static int IndexOf(FabricSettings fabric)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Name == fabric.preset && All[i].Settings.SameValues(fabric)) return i;
            }
            return All.Count;
        }
    }
}
