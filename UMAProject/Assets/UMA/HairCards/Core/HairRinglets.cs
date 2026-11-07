using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public enum HairRingletLengthMode { KeepEnvelope, PreserveStrandLength }
    public enum HairRingletSpacingMode { TurnsPerStrand, DistanceBetweenTurns }

    [Serializable]
    public sealed class HairRingletSettings
    {
        [Range(0.25f, 12f)] public float turns = 3f;
        public HairRingletSpacingMode spacingMode;
        [Range(.003f, .15f)] public float turnSpacing = .02666667f;
        [Range(0f, 1f)] public float radiusVariation = 0.15f;
        [Range(0f, 0.75f)] public float turnVariation = 0.2f;
        [Range(0f, 1f)] public float phaseVariation = 1f;
        [Range(0f, 1f)] public float clumpCoherence = 0.4f;
        [Range(0.01f, 0.5f)] public float rootRamp = 0.12f;
        [Range(0f, 2f)] public float tipRadius = 0.65f;
        [Range(0f, 1f)] public float reverseFraction = 0.2f;
        [Range(8, 20)] public int pointsPerTurn = 12;
        [Range(0f, 1f)] public float radialFacing = 1f;
        [Range(0f, 1f)] public float centerlineSmoothing = 1f;
        public HairRingletLengthMode lengthMode;
        public bool optimizeCards = true;
        [Range(.0001f, .01f)] public float cardShapeError = .002f;
        [Range(2f, 45f)] public float cardFacingError = 18f;
        [Range(4, 256)] public int cardMaxSamples = 64;

        public void EnsureIntegrity()
        {
            turns = Finite(turns, .25f, 12f); radiusVariation = Finite(radiusVariation, 0f, 1f);
            turnSpacing = Finite(turnSpacing, .003f, .15f);
            if (!Enum.IsDefined(typeof(HairRingletSpacingMode), spacingMode)) spacingMode = HairRingletSpacingMode.TurnsPerStrand;
            turnVariation = Finite(turnVariation, 0f, .75f); phaseVariation = Finite(phaseVariation, 0f, 1f);
            clumpCoherence = Finite(clumpCoherence, 0f, 1f); rootRamp = Finite(rootRamp, .01f, .5f);
            tipRadius = Finite(tipRadius, 0f, 2f); reverseFraction = Finite(reverseFraction, 0f, 1f);
            radialFacing = Finite(radialFacing, 0f, 1f); pointsPerTurn = Mathf.Clamp(pointsPerTurn, 8, 20);
            centerlineSmoothing = Finite(centerlineSmoothing, 0f, 1f);
            cardShapeError = Finite(cardShapeError, .0001f, .01f);
            cardFacingError = Finite(cardFacingError, 2f, 45f); cardMaxSamples = Mathf.Clamp(cardMaxSamples, 4, 256);
            if (!Enum.IsDefined(typeof(HairRingletLengthMode), lengthMode)) lengthMode = HairRingletLengthMode.KeepEnvelope;
        }
        private static float Finite(float v, float min, float max) => float.IsFinite(v) ? Mathf.Clamp(v, min, max) : min;
        public HairRingletSettings Duplicate() => (HairRingletSettings)MemberwiseClone();
    }

    /// <summary>Orbit the incoming centerline in a parallel-transport frame. This is a
    /// source-local detail generator, independent of object pose, render LOD and evaluation order.</summary>
    internal sealed class HairRingletWorkspace
    {
        private readonly List<HairCurvePoint> source = new List<HairCurvePoint>();
        private readonly List<HairCurvePoint> centers = new List<HairCurvePoint>();
        private readonly List<Vector3> original = new List<Vector3>(), target = new List<Vector3>();
        private readonly List<HairGuidePoint> controls = new List<HairGuidePoint>();

        internal void Clear()
        {
            source.Clear(); source.Capacity = 0; centers.Clear(); centers.Capacity = 0;
            original.Clear(); original.Capacity = 0; target.Clear(); target.Capacity = 0;
            controls.Clear(); controls.Capacity = 0;
        }

        internal void Apply(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            var settings = modifier.ringlets;
            if (settings == null || curve.points.Count < 2 || modifier.amount <= 0f || modifier.weight <= 0f) return;
            float length = curve.Length;
            if (!float.IsFinite(length) || length < 1e-7f) return;
            bool movable = false;
            for (int i = 1; i < curve.points.Count; i++) movable |= curve.points[i].freeze < .999f;
            if (!movable) return;
            // A value snapshot travels with this curve; render LOD never changes its shape.
            // The last applied Ringlets modifier owns the ribbon reduction settings.
            curve.ribbonReduction = new HairRibbonReductionSettings { enabled = settings.optimizeCards,
                shapeError = settings.cardShapeError, facingError = settings.cardFacingError, maximumSamples = settings.cardMaxSamples };
            var random = new HairDeterministicRandom(curve.seed ^ modifier.seed ^ 0x5a3c7d19);
            var parent = new HairDeterministicRandom(curve.clumpSeed ^ modifier.seed ^ 0x5a3c7d19);
            float Random() => Mathf.Lerp(random.NextSigned(), parent.NextSigned(), settings.clumpCoherence);
            float baseTurns = settings.spacingMode == HairRingletSpacingMode.DistanceBetweenTurns ? length / Mathf.Max(.003f, settings.turnSpacing) : settings.turns;
            float turns = Mathf.Clamp(baseTurns * (1f + Random() * settings.turnVariation), .1f, 20f);
            float radius = Mathf.Max(0f, modifier.amount * (1f + Random() * settings.radiusVariation));
            float phase = Random() * Mathf.PI * settings.phaseVariation;
            float directionSample = random.Next01() < settings.clumpCoherence ? parent.Next01() : random.Next01();
            float handedness = directionSample < settings.reverseFraction ? -1f : 1f;

            source.Clear(); source.AddRange(curve.points); centers.Clear(); centers.Add(source[0]);
            // Retain EVERY incoming point (including exact frozen anchors). Distribute extra
            // samples by arc length, with a hard cap; no topology/seed changes with render LOD.
            int budget = Mathf.Max(source.Count, Mathf.Clamp(Mathf.CeilToInt(turns * settings.pointsPerTurn) + 1, 2, 256));
            int extras = budget - source.Count, used = 0; float distance = 0f;
            for (int i = 1; i < source.Count; i++)
            {
                distance += Vector3.Distance(source[i - 1].position, source[i].position);
                int allocated = i == source.Count - 1 ? extras : Mathf.FloorToInt(extras * distance / length);
                int divisions = 1 + Mathf.Max(0, allocated - used); used = allocated;
                for (int p = 1; p < divisions; p++)
                {
                    float t = p / (float)divisions;
                    var sample = Interpolate(source[i - 1], source[i], t);
                    if (settings.lengthMode == HairRingletLengthMode.KeepEnvelope && settings.centerlineSmoothing > 0f)
                    {
                        Vector3 a = source[i - 1].position, b = source[i].position;
                        Vector3 before = i > 1 ? source[i - 2].position : a * 2 - b;
                        Vector3 after = i + 1 < source.Count ? source[i + 1].position : b * 2 - a;
                        float span = Vector3.Distance(a, b), t2 = t * t, t3 = t2 * t;
                        Vector3 smooth = (2 * t3 - 3 * t2 + 1) * a + (t3 - 2 * t2 + t) * (b - before).normalized * span +
                            (-2 * t3 + 3 * t2) * b + (t3 - t2) * (after - a).normalized * span;
                        sample.position = Vector3.Lerp(sample.position, smooth, settings.centerlineSmoothing);
                    }
                    centers.Add(sample);
                }
                centers.Add(source[i]);
            }
            length = HairCurveUtility.CalculateLength(centers);
            Vector3 tangent = HairCurveUtility.CalculateTangent(centers, 0);
            Vector3 normal = Vector3.ProjectOnPlane(curve.rootNormal, tangent).normalized;
            if (normal.sqrMagnitude < 1e-8f)
                normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < .9f ? Vector3.up : Vector3.right).normalized;
            Vector3 previousTangent = tangent;
            curve.points.Clear(); distance = 0f;
            original.Clear(); target.Clear();
            while (controls.Count < centers.Count) controls.Add(new HairGuidePoint());
            for (int i = 0; i < centers.Count; i++)
            {
                var point = centers[i];
                if (i > 0) distance += Vector3.Distance(centers[i - 1].position, point.position);
                float t = Mathf.Clamp01(distance / length);
                tangent = HairCurveUtility.CalculateTangent(centers, i);
                normal = Quaternion.FromToRotation(previousTangent, tangent) * normal;
                normal = Vector3.ProjectOnPlane(normal, tangent).normalized; previousTangent = tangent;
                Vector3 side = Vector3.Cross(tangent, normal).normalized;
                float angle = phase + handedness * t * turns * 2f * Mathf.PI;
                Vector3 radial = normal * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                float ramp = modifier.rootToTip?.Evaluate(t) ?? 1f;
                float weight = float.IsFinite(ramp) ? Mathf.Clamp01(modifier.weight * ramp) : 0f;
                weight *= 1f - Mathf.Clamp01(point.freeze);
                float root = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(.01f, settings.rootRamp)) *
                    HairGuideShapeUtility.RootInfluence(t, modifier.rootInfluence);
                original.Add(point.position); controls[i].freeze = point.freeze;
                if (i > 0 && point.freeze < .999f)
                {
                    point.position += radial * (radius * Mathf.Lerp(1f, settings.tipRadius, t) * root * weight);
                }
                if (settings.radialFacing > 0f && point.freeze < .999f)
                {
                    // Start the ribbon in the curl's phase frame, including at the pinned
                    // root. Fading orientation with the radius can cross the +/-180-degree
                    // shortest-rotation seam and create a sudden half-twist near the root.
                    Vector3 incoming = point.facingWeight > 0f ? point.facingNormal : curve.rootNormal;
                    point.facingNormal = Vector3.Slerp(incoming, radial, settings.radialFacing * weight).normalized;
                    point.facingWeight = Mathf.Lerp(point.facingWeight, 1f, settings.radialFacing * weight);
                }
                target.Add(point.position); curve.points.Add(point);
            }
            // Envelope mode deliberately adds arc length while keeping the authored silhouette.
            // Length mode contracts the envelope, honoring exact root and frozen anchors.
            if (settings.lengthMode == HairRingletLengthMode.PreserveStrandLength)
            {
                HairGuideShapeUtility.PreserveSegmentLengths(original, target, controls);
                for (int i = 0; i < curve.points.Count; i++)
                { var p = curve.points[i]; p.position = target[i]; curve.points[i] = p; }
            }
        }

        private static HairCurvePoint Interpolate(HairCurvePoint a, HairCurvePoint b, float t) => new HairCurvePoint(
            Vector3.Lerp(a.position, b.position, t), Mathf.Lerp(a.width, b.width, t), Mathf.LerpAngle(a.roll, b.roll, t),
            a.widthBaseline >= 0f && b.widthBaseline >= 0f ? Mathf.Lerp(a.widthBaseline, b.widthBaseline, t) : -1f,
            Mathf.Lerp(a.profileScale, b.profileScale, t), Mathf.Lerp(a.stiffness, b.stiffness, t), Mathf.Lerp(a.freeze, b.freeze, t))
        { facingNormal = Vector3.Lerp(a.facingNormal, b.facingNormal, t).normalized, facingWeight = Mathf.Lerp(a.facingWeight, b.facingWeight, t) };
    }
}
