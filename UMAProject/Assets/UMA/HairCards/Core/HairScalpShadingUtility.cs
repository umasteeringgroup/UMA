using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public static class HairScalpShadingUtility
    {
        /// <summary>Multiply RGB below growth without changing geometry, source colors, or the
        /// hair cards' COLOR channel. Caller owns the reusable output and per-group field caches.</summary>
        public static void Evaluate(HairGroomAsset groom, IReadOnlyList<Color32> baseline,
            List<Color32> output, Dictionary<string, HairSurfaceFields> fields, HairGroup onlyGroup = null)
        {
            output.Clear(); if (groom?.SourceMesh == null || !groom.SourceMesh.isReadable) return;
            int count = groom.SourceVertexCount;
            for (int i = 0; i < count; i++) output.Add(baseline != null && baseline.Count == count ? baseline[i] : new Color32(255, 255, 255, 255));
            foreach (var group in groom.Groups)
            {
                if (group == null || !group.enabled || (onlyGroup != null && group != onlyGroup)) continue;
                var scalp = group.generation?.scalp;
                if (scalp == null || !scalp.enabled) continue;
                if (!fields.TryGetValue(group.Id, out var surface)) fields.Add(group.Id, surface = new HairSurfaceFields());
                surface.Prepare(groom.SourceMesh, group);
                for (int i = 0; i < count; i++)
                {
                    float fade = Mathf.SmoothStep(0f, 1f, surface.Distances[i] / Mathf.Max(0.001f, scalp.edgeFade));
                    float weight = Mathf.Clamp01(surface.PaintedDensity(i) * scalp.strength * fade);
                    Color original = output[i], tint = Color.Lerp(Color.white, scalp.color, weight);
                    Color shaded = original * tint;
                    if (scalp.preserveAlpha) shaded.a = original.a;
                    output[i] = shaded;
                }
            }
        }
    }
}
