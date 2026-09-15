using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    /// <summary>Mesh-only quality limits, copied by value with each evaluated curve.</summary>
    public struct HairRibbonReductionSettings
    {
        public bool enabled;
        public float shapeError, facingError;
        public int maximumSamples;
    }

    /// <summary>Best-first ribbon reduction. Measure both edges, camber and shading frames,
    /// not just the centerline. Retained frames come from the detailed curve, never from
    /// the coarse chords. Scratch storage is owned by one mesh workspace and reused.</summary>
    internal sealed class HairRibbonReductionWorkspace
    {
        private static readonly ProfilerMarker ReductionMarker = new ProfilerMarker("HairCards.ReduceRibbon");
        private struct Span { public int a, b, split; public float error; }
        private readonly List<HairCurvePoint> dense = new List<HairCurvePoint>();
        private readonly List<Span> spans = new List<Span>();
        private readonly List<int> rows = new List<int>();
        private float[] progress = Array.Empty<float>(), widths = Array.Empty<float>();
        private float[] cumulative = Array.Empty<float>();
        private Vector3[] tangents = Array.Empty<Vector3>(), sides = Array.Empty<Vector3>(), normals = Array.Empty<Vector3>();
        private Vector3[] left = Array.Empty<Vector3>(), right = Array.Empty<Vector3>(), middle = Array.Empty<Vector3>();
        internal readonly List<float> parameters = new List<float>(), selectedWidths = new List<float>();
        private float positionToleranceSquared, frameTolerance;
        internal bool active;

        internal bool Prepare(HairEvaluatedCurve curve, int lodLimit, HairMeshBuildWorkspace workspace)
        {
            using var marker = ReductionMarker.Auto();
            var settings = curve.ribbonReduction;
            int referenceCount = curve.points.Count;
            dense.Clear(); dense.AddRange(curve.points);
            int referenceFlips = ReferenceFrames(curve);
            // Uneven input spacing can make a tangent unstable at a tight root bend.
            // Refine just those reference curves, not every card's output mesh. All LODs
            // use this same reference and retain its frames, so reduction does not reseed it.
            while (referenceFlips > 0 && referenceCount < 256)
            {
                referenceCount = Mathf.Min(256, referenceCount * 2);
                HairCurveUtility.ResampleInto(curve.points, referenceCount, dense, ref cumulative);
                referenceFlips = ReferenceFrames(curve);
            }
            float shapeError = float.IsFinite(settings.shapeError) ? Mathf.Clamp(settings.shapeError, .0001f, .01f) : .002f;
            float facingError = float.IsFinite(settings.facingError) ? Mathf.Clamp(settings.facingError, 2f, 45f) : 18f;
            positionToleranceSquared = shapeError * shapeError;
            frameTolerance = 1f - Mathf.Cos(facingError * Mathf.Deg2Rad);
            var profile = curve.profile;
            for (int i = 0; i < referenceCount; i++)
            {
                float t = progress[i];
                float width = HairCardMeshGenerator.ResolveCardWidth(dense[i], true, profile.DefaultWidth, profile.EvaluateWidth(t));
                widths[i] = width;
                Vector3 offset = sides[i] * (.5f * width);
                left[i] = dense[i].position - offset; right[i] = dense[i].position + offset;
                middle[i] = dense[i].position + normals[i] * (profile.RibbonSpans > 1 ? profile.RibbonCamber * width : 0f);
            }
            int limit = Mathf.Clamp(Mathf.Min(settings.maximumSamples, lodLimit), 2, referenceCount);
            spans.Clear(); spans.Add(Measure(0, referenceCount - 1));
            while (spans.Count + 1 < limit)
            {
                int best = -1; float error = 1f;
                for (int i = 0; i < spans.Count; i++)
                    if (spans[i].split > spans[i].a && spans[i].error > error) { best = i; error = spans[i].error; }
                if (best < 0) break;
                var span = spans[best]; spans[best] = Measure(span.a, span.split);
                spans.Add(Measure(span.split, span.b));
            }
            bool limited = false;
            rows.Clear(); rows.Add(0);
            foreach (var span in spans) { rows.Add(span.b); limited |= span.error > 1.001f; }
            rows.Sort(); parameters.Clear(); selectedWidths.Clear(); workspace.sampled.Clear(); workspace.EnsureFrames(rows.Count);
            foreach (int i in rows)
            {
                int row = workspace.sampled.Count;
                workspace.sampled.Add(dense[i]); parameters.Add(progress[i]); selectedWidths.Add(widths[i]);
                workspace.curveTangents[row] = tangents[i]; workspace.sides[row] = sides[i]; workspace.frameNormals[row] = normals[i];
            }
            active = true;
            return limited;
        }

        private int ReferenceFrames(HairEvaluatedCurve curve)
        {
            int count = dense.Count; Ensure(count); progress[0] = 0f;
            for (int i = 1; i < count; i++) progress[i] = progress[i - 1] + Vector3.Distance(dense[i - 1].position, dense[i].position);
            float length = progress[count - 1];
            for (int i = 0; i < count; i++) progress[i] = length > 1e-8f ? progress[i] / length : i / (count - 1f);
            HairCardMeshGenerator.EmbedCardRoot(curve, dense, progress);
            HairCurveUtility.BuildRotationMinimizingFrames(dense, curve.rootNormal, tangents, sides, normals, out int flips);
            return flips;
        }

        private Span Measure(int a, int b)
        {
            var result = new Span { a = a, b = b, split = -1 };
            if (b <= a + 1) return result;
            // Do not join opposite ribbon sides even when the centerline is nearly straight.
            // Interior frame error also detects complete loops with identical end frames.
            float sideDot = Vector3.Dot(sides[a], sides[b]);
            if (sideDot < 0f) { result.error = 1.01f - sideDot; result.split = (a + b) / 2; }
            float length = progress[b] - progress[a];
            Vector3 centerA = dense[a].position, centerDelta = dense[b].position - centerA;
            Vector3 leftA = left[a], leftDelta = left[b] - leftA;
            Vector3 rightA = right[a], rightDelta = right[b] - rightA;
            Vector3 middleA = middle[a], middleDelta = middle[b] - middleA;
            Vector3 normalA = normals[a], normalDelta = normals[b] - normalA;
            Vector3 tangentA = tangents[a], tangentDelta = tangents[b] - tangentA;
            for (int i = a + 1; i < b; i++)
            {
                float t = length > 1e-8f ? (progress[i] - progress[a]) / length : (i - a) / (float)(b - a);
                float positionError = Mathf.Max(Mathf.Max(
                    (dense[i].position - (centerA + centerDelta * t)).sqrMagnitude,
                    (left[i] - (leftA + leftDelta * t)).sqrMagnitude), Mathf.Max(
                    (right[i] - (rightA + rightDelta * t)).sqrMagnitude,
                    (middle[i] - (middleA + middleDelta * t)).sqrMagnitude)) / positionToleranceSquared;
                float error = positionError;
                // Frame error cannot exceed 2 / tolerance. Large positional errors already
                // dominate it, so avoid two square roots for those distant candidates.
                if (positionError < 2f / frameTolerance)
                    error = Mathf.Max(error, Mathf.Max(FrameError(normalA + normalDelta * t, normals[i]),
                        FrameError(tangentA + tangentDelta * t, tangents[i])) / frameTolerance);
                if (error > result.error) { result.error = error; result.split = i; }
            }
            return result;
        }

        private static float FrameError(Vector3 interpolated, Vector3 exact)
        {
            float square = interpolated.sqrMagnitude;
            return square > 1e-10f ? 1f - Vector3.Dot(interpolated, exact) / Mathf.Sqrt(square) : 1f;
        }

        private void Ensure(int count)
        {
            if (widths.Length >= count) return;
            int size = Mathf.NextPowerOfTwo(count);
            Array.Resize(ref widths, size); Array.Resize(ref progress, size); Array.Resize(ref tangents, size); Array.Resize(ref sides, size);
            Array.Resize(ref normals, size); Array.Resize(ref left, size); Array.Resize(ref right, size); Array.Resize(ref middle, size);
        }
        internal void Clear()
        {
            dense.Clear(); dense.Capacity = 0; rows.Clear(); rows.Capacity = 0; spans.Clear(); spans.Capacity = 0;
            parameters.Clear(); parameters.Capacity = 0; selectedWidths.Clear(); selectedWidths.Capacity = 0;
            cumulative = progress = widths = Array.Empty<float>(); tangents = sides = normals = left = right = middle = Array.Empty<Vector3>(); active = false;
        }
    }
}
