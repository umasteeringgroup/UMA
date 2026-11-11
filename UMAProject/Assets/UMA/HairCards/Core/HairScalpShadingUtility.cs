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
            Evaluate(groom, baseline, output, group =>
            {
                if (!fields.TryGetValue(group.Id, out var surface)) fields.Add(group.Id, surface = new HairSurfaceFields());
                return surface;
            }, onlyGroup);
        }

        /// <summary>Shade with the evaluation workspace's own surface fields, so a preview refresh
        /// builds each painted distance graph once rather than once for cards and again here.</summary>
        public static void Evaluate(HairGroomAsset groom, IReadOnlyList<Color32> baseline,
            List<Color32> output, HairEvaluationWorkspace workspace, HairGroup onlyGroup = null)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            Evaluate(groom, baseline, output, workspace.SurfaceFields, onlyGroup);
        }

        private static void Evaluate(HairGroomAsset groom, IReadOnlyList<Color32> baseline,
            List<Color32> output, Func<HairGroup, HairSurfaceFields> fieldsFor, HairGroup onlyGroup)
        {
            output.Clear(); if (groom?.SourceMesh == null || !groom.SourceMesh.isReadable) return;
            int count = groom.SourceVertexCount;
            for (int i = 0; i < count; i++) output.Add(baseline != null && baseline.Count == count ? baseline[i] : new Color32(255, 255, 255, 255));
            foreach (var group in groom.Groups)
            {
                if (group == null || !group.enabled || (onlyGroup != null && group != onlyGroup)) continue;
                var scalp = group.generation?.scalp;
                if (scalp == null || !scalp.enabled) continue;
                var surface = fieldsFor(group);
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
