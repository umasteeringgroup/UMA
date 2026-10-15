using System;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Finished starting points; placement, masks and seed are deliberately retained.</summary>
    public static class StubbleMakerPresets
    {
        public static readonly string[] Names = { "Five O Clock", "Natural Short", "Coarse Stubble", "Salt and Pepper" };

        public static void Apply(TexturePaintPluginParameterSet p, int index)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (index < 0 || index >= Names.Length) throw new ArgumentOutOfRangeException(nameof(index));
            void Set(string key, float value) => p.Get(key, true).number = value;
            Set("renderModel", 1); Set("profile", 0); Set("directionSpace", 0);
            Set("referenceResolution", 2048); Set("anatomicalFlow", 1);
            Set("rootSpacing", index == 0 ? 4.5f : index == 2 ? 5.2f : 5.5f);
            Set("density", index == 0 ? .82f : index == 2 ? .9f : .78f);
            Set("hairLength", index == 0 ? 1.7f : index == 2 ? 6.8f : 3.8f);
            Set("hairWidth", index == 0 ? .65f : index == 2 ? 1.1f : .75f);
            Set("lengthVariation", .6f); Set("widthVariation", .3f);
            Set("hairOpacity", index == 0 ? .72f : .9f);
            Set("directionDegrees", 0); Set("directionVariation", 24);
            Set("flowVariation", 18); Set("flowScale", 64); Set("curvature", .12f);
            Set("patchiness", .3f); Set("fineFraction", .18f); Set("clippedFraction", .8f);
            Set("hairHeight", index == 0 ? .012f : index == 2 ? .04f : .025f);
            Set("hairRoughness", .46f); Set("roughnessVariation", .06f);
            Set("grayFraction", index == 3 ? .32f : 0);
            Set("hairColorVariation", .12f); Set("opacityVariation", .15f);
            Set("shadowAmount", .06f); Set("shadowSpread", .7f); Set("shadowOffset", .25f);
            Set("rednessAmount", .015f); Set("rednessRadius", 1.1f);
            Set("follicleDepth", .003f); Set("follicleRadius", .75f);
            Set("rashAmount", 0); Set("pimpleAmount", 0); Set("spotAmount", 0);
            p.Get("hairColor", true).color = new Color(.055f, .035f, .025f, 1);
            p.Get("grayColor", true).color = new Color(.42f, .39f, .34f, 1);
        }
    }
}
