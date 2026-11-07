using System;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    /// <summary>Source-local strand deformation. Scratch storage belongs to the evaluator, not each card.</summary>
    internal sealed class HairStrandDeformation
    {
        private Vector3[] tangents = Array.Empty<Vector3>(), sides = Array.Empty<Vector3>(), normals = Array.Empty<Vector3>();
        private static readonly ProfilerMarker NoiseMarker = new ProfilerMarker("UMA.HairCards.StrandNoise2D");
        private static readonly ProfilerMarker BendMarker = new ProfilerMarker("UMA.HairCards.SurfaceBend");

        public void Clear() { tangents = sides = normals = Array.Empty<Vector3>(); }

        private void Prepare(HairEvaluatedCurve curve)
        {
            if (tangents.Length < curve.points.Count)
            {
                int count = Mathf.NextPowerOfTwo(curve.points.Count);
                tangents = new Vector3[count]; sides = new Vector3[count]; normals = new Vector3[count];
            }
            // Geometry follows the strand, not card roll/facing: editing ribbon orientation
            // must not unexpectedly reshape the groom. Transport avoids a global-axis bias.
            HairCurveUtility.BuildRotationMinimizingFrames(curve.points, curve.rootNormal,
                tangents, sides, normals, out _, false);
        }

        private static float Weight(HairModifierSettings modifier, float t, HairCurvePoint point)
        {
            float ramp = modifier.rootToTip?.Evaluate(t) ?? 1f;
            return float.IsFinite(ramp) ? Mathf.Clamp01(modifier.weight * ramp) *
                HairGuideShapeUtility.RootInfluence(t, modifier.rootInfluence) * (1f - Mathf.Clamp01(point.stiffness)) : 0f;
        }

        public void Noise(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            using var scope = NoiseMarker.Auto();
            if (curve.points.Count < 2) return;
            float length = curve.Length;
            if (length <= 1e-8f) return;
            Prepare(curve);
            float frequency = float.IsFinite(modifier.noiseFrequency) ? Mathf.Max(.01f, modifier.noiseFrequency) : 18f;
            float outward = float.IsFinite(modifier.noiseNormalAmplitude) ? Mathf.Max(0, modifier.noiseNormalAmplitude) : 0;
            float coherence = float.IsFinite(modifier.noiseParentCoherence) ? Mathf.Clamp01(modifier.noiseParentCoherence) : 0;
            float fade = float.IsFinite(modifier.noiseRootFade) ? Mathf.Clamp01(modifier.noiseRootFade) : .2f;
            float distance = 0;
            Vector3 previous = curve.points[0].position;
            for (int i = 1; i < curve.points.Count; i++)
            {
                var point = curve.points[i];
                distance += Vector3.Distance(previous, point.position); previous = point.position;
                float t = Mathf.Clamp01(distance / length);
                Vector3 noise = Vector3.Lerp(HairStrandNoise.Sample(distance * frequency, modifier.seed ^ curve.seed),
                    HairStrandNoise.Sample(distance * frequency, modifier.seed ^ curve.clumpSeed), coherence);
                float envelope = fade > 0 ? Mathf.SmoothStep(0, 1, t / fade) : 1;
                point.position += (sides[i] * (noise.x * modifier.amount) + normals[i] * (noise.y * outward)) *
                    (envelope * Weight(modifier, t, point));
                curve.points[i] = point;
            }
            // The common modifier finalizer pins roots/frozen points and restores segment lengths.
        }

        public void Bend(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            using var scope = BendMarker.Auto();
            if (curve.points.Count < 2 || curve.Length <= 1e-8f) return;
            Prepare(curve);
            float length = curve.Length, distance = 0;
            Vector3 previous = curve.points[0].position;
            for (int i = 1; i < curve.points.Count; i++)
            {
                var point = curve.points[i];
                Vector3 segment = point.position - previous; previous = point.position;
                float segmentLength = segment.magnitude;
                distance += segmentLength;
                if (segmentLength <= 1e-8f) continue;
                float t = Mathf.Clamp01(distance / length);
                float profile = modifier.bendProfile?.Evaluate(t) ?? 1f - t;
                float angle = float.IsFinite(profile) ? Mathf.Clamp(modifier.amount * profile, -85f, 85f) * Weight(modifier, t, point) : 0;
                Vector3 axis = Vector3.Cross(segment, normals[i]);
                if (axis.sqrMagnitude < 1e-12f) axis = sides[i];
                float radians = angle * Mathf.Deg2Rad;
                point.position = curve.points[i - 1].position + segment * Mathf.Cos(radians) +
                    Vector3.Cross(axis.normalized, segment) * Mathf.Sin(radians);
                curve.points[i] = point;
            }
        }
    }
}
