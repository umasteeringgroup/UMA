using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace UMA.HairCards
{
    /// <summary>An ordered, source-surface-anchored styling path. First point is the flow start.</summary>
    [Serializable]
    public sealed class HairFlowSpline
    {
        [SerializeField] private string id;
        public string name = "Flow spline";
        public bool enabled = true;
        public List<HairSurfaceAnchor> points = new List<HairSurfaceAnchor>();
        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            points ??= new List<HairSurfaceAnchor>();
        }

        public HairFlowSpline Duplicate()
        {
            var copy = new HairFlowSpline { name = name, enabled = enabled,
                points = points == null ? new List<HairSurfaceAnchor>() : new List<HairSurfaceAnchor>(points) };
            copy.EnsureIntegrity();
            return copy;
        }
    }

    // Rebuild each field once per evaluation (including in-place path/source edits); retain storage
    // between previews. No UnityEditor dependency: runtime and baking use precisely the same field.
    internal sealed class HairSplineFlowWorkspace
    {
        private readonly Dictionary<string, HairSplineFlowField> active = new Dictionary<string, HairSplineFlowField>();
        private readonly List<HairSplineFlowField> pool = new List<HairSplineFlowField>();
        private int used;
        internal void Begin() { active.Clear(); used = 0; }
        internal void End() { if (used < pool.Count) pool.RemoveRange(used, pool.Count - used); }
        internal void Clear() { active.Clear(); pool.Clear(); used = 0; }
        internal HairSplineFlowField Get(HairModifierSettings modifier, HairGroomEvaluator.SourceMeshReadCache mesh, string sourceId)
        {
            if (active.TryGetValue(modifier.Id, out HairSplineFlowField field)) return field;
            if (used == pool.Count) pool.Add(new HairSplineFlowField());
            field = pool[used++];
            field.Build(modifier, mesh, sourceId);
            active.Add(modifier.Id, field);
            return field;
        }
    }

    internal sealed class HairSplineFlowField
    {
        private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("HairCards.SplineFlow.BuildField");
        private static readonly ProfilerMarker ApplyMarker = new ProfilerMarker("HairCards.SplineFlow.Apply");
        private struct Sample
        {
            internal Vector3 position, normal, tangent;
            internal float distance;
        }
        private sealed class Path
        {
            internal readonly List<Sample> samples = new List<Sample>();
            internal Bounds bounds;
        }
        private struct Neighbor
        {
            internal int path;
            internal float distance, along, weight;
        }
        private readonly List<Path> paths = new List<Path>();
        private struct FacingTarget
        {
            internal Vector3 normal, forward;
            internal float weight;
        }
        private readonly List<FacingTarget> facingTargets = new List<FacingTarget>();
        private int pathCount;

        internal void Build(HairModifierSettings modifier, HairGroomEvaluator.SourceMeshReadCache mesh, string sourceId)
        {
            using (BuildMarker.Auto())
            {
                pathCount = 0;
                foreach (HairFlowSpline spline in modifier.flowSplines)
                {
                    if (spline == null || !spline.enabled || spline.points == null || spline.points.Count < 2) continue;
                    if (pathCount == paths.Count) paths.Add(new Path());
                    Path path = paths[pathCount];
                    path.samples.Clear();
                    foreach (HairSurfaceAnchor anchor in spline.points)
                    {
                        // Never reinterpret triangle indices from another source mesh. Reject the
                        // whole path rather than bridge across a missing/mismatched anchor.
                        if (!anchor.IsValid || !string.Equals(anchor.SourceMeshId, sourceId, StringComparison.Ordinal))
                        { path.samples.Clear(); break; }
                        mesh.TryEvaluateAnchor(anchor, out Vector3 position, out Vector3 normal);
                        if (!float.IsFinite(position.sqrMagnitude) || !float.IsFinite(normal.sqrMagnitude)) continue;
                        int last = path.samples.Count - 1;
                        float step = last >= 0 ? Vector3.Distance(path.samples[last].position, position) : 0f;
                        if (last >= 0 && step < 0.000001f) continue;
                        if (last < 0) path.bounds = new Bounds(position, Vector3.zero);
                        else path.bounds.Encapsulate(position);
                        path.samples.Add(new Sample { position = position, normal = normal.normalized,
                            distance = last >= 0 ? path.samples[last].distance + step : 0f });
                    }
                    if (path.samples.Count < 2) continue;
                    for (int i = 0; i < path.samples.Count; i++)
                    {
                        Sample sample = path.samples[i];
                        Vector3 before = i > 0 ? (sample.position - path.samples[i - 1].position).normalized : Vector3.zero;
                        Vector3 after = i + 1 < path.samples.Count ? (path.samples[i + 1].position - sample.position).normalized : Vector3.zero;
                        Vector3 tangent = Vector3.ProjectOnPlane(before + after, sample.normal);
                        if (tangent.sqrMagnitude < 1e-10f) tangent = after.sqrMagnitude > 0f ? after : before;
                        sample.tangent = tangent.normalized;
                        path.samples[i] = sample;
                    }
                    pathCount++;
                }
                if (pathCount < paths.Count) paths.RemoveRange(pathCount, paths.Count - pathCount);
            }
        }

        // Select paths, not samples: adding control points does not increase a path's influence.
        // Bounds reject distant paths; nearest search happens once per strand, not per hair point.
        private int FindNeighbors(Vector3 root, Vector3 rootNormal, float radius, int limit, Span<Neighbor> nearest)
        {
            int count = 0;
            float radiusSq = radius * radius;
            for (int p = 0; p < pathCount; p++)
            {
                Path path = paths[p];
                if (path.bounds.SqrDistance(root) >= radiusSq) continue;
                float closest = radiusSq, along = 0f;
                for (int i = 1; i < path.samples.Count; i++)
                {
                    Sample a = path.samples[i - 1], b = path.samples[i];
                    Vector3 segment = b.position - a.position;
                    float t = Mathf.Clamp01(Vector3.Dot(root - a.position, segment) / segment.sqrMagnitude);
                    float distance = (root - (a.position + segment * t)).sqrMagnitude;
                    // Prevent flow painted on the far side of a scalp from steering this side.
                    if (distance >= closest || Vector3.Dot(Vector3.Lerp(a.normal, b.normal, t), rootNormal) <= 0f) continue;
                    closest = distance;
                    along = Mathf.Lerp(a.distance, b.distance, t);
                }
                if (closest >= radiusSq) continue;
                int insert = count;
                while (insert > 0 && nearest[insert - 1].distance > closest) insert--;
                if (insert >= limit) continue;
                int end = Mathf.Min(count, limit - 1);
                for (int i = end; i > insert; i--) nearest[i] = nearest[i - 1];
                nearest[insert] = new Neighbor { path = p, distance = closest, along = along };
                count = Mathf.Min(count + 1, limit);
            }
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                Neighbor n = nearest[i];
                float falloff = 1f - Mathf.Sqrt(n.distance) / radius;
                n.weight = falloff * falloff / Mathf.Max(n.distance, radiusSq * 0.0001f);
                sum += n.weight; nearest[i] = n;
            }
            for (int i = 0; i < count; i++) { Neighbor n = nearest[i]; n.weight /= sum; nearest[i] = n; }
            return count;
        }

        private static Sample AtDistance(Path path, float distance)
        {
            var samples = path.samples;
            if (distance >= samples[samples.Count - 1].distance) return samples[samples.Count - 1];
            int low = 0, high = samples.Count - 1;
            while (high - low > 1)
            { int mid = (low + high) / 2; if (samples[mid].distance < distance) low = mid; else high = mid; }
            Sample a = samples[low], b = samples[high];
            float t = Mathf.InverseLerp(a.distance, b.distance, distance);
            Vector3 normal = Vector3.Slerp(a.normal, b.normal, t).normalized;
            Vector3 tangent = Vector3.ProjectOnPlane(Vector3.Slerp(a.tangent, b.tangent, t), normal).normalized;
            return new Sample { normal = normal, tangent = tangent };
        }

        internal void Apply(HairEvaluatedCurve curve, HairModifierSettings modifier)
        {
            if (pathCount == 0 || curve.points.Count < 2 || (modifier.flowDirection <= 0f && modifier.flowFacing <= 0f)) return;
            using (ApplyMarker.Auto())
            {
                Span<Neighbor> neighbors = stackalloc Neighbor[4];
                int count = FindNeighbors(curve.points[0].position, curve.rootNormal, modifier.flowRadius, modifier.flowNeighbors, neighbors);
                if (count == 0) return;
                facingTargets.Clear();
                float proximity = 1f - Mathf.Sqrt(neighbors[0].distance) / modifier.flowRadius;
                proximity = Mathf.SmoothStep(0f, 1f, proximity);
                Vector3 originalPrevious = curve.points[0].position;
                float travelled = 0f;
                for (int i = 0; i < curve.points.Count; i++)
                {
                    HairCurvePoint point = curve.points[i];
                    Vector3 originalDirection = point.position - originalPrevious;
                    originalPrevious = point.position;
                    float length = originalDirection.magnitude;
                    Vector3 direction = Vector3.zero, normal = Vector3.zero;
                    for (int n = 0; n < count; n++)
                    {
                        Neighbor neighbor = neighbors[n];
                        Sample sample = AtDistance(paths[neighbor.path], neighbor.along + travelled);
                        direction += sample.tangent * neighbor.weight;
                        normal += sample.normal * neighbor.weight;
                    }
                    // At a perfectly opposing part, use the nearest path rather than a zero vector.
                    Sample fallback = AtDistance(paths[neighbors[0].path], neighbors[0].along + travelled);
                    normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : fallback.normal;
                    direction = Vector3.ProjectOnPlane(direction, normal);
                    direction = direction.sqrMagnitude > 1e-10f ? direction.normalized : fallback.tangent;
                    float lift = modifier.flowLift * Mathf.Deg2Rad;
                    direction = (direction * Mathf.Cos(lift) + normal * Mathf.Sin(lift)).normalized;
                    float t = i / (curve.points.Count - 1f);
                    float ramp = modifier.rootToTip?.Evaluate(t) ?? 1f;
                    float influence = float.IsFinite(ramp) ? Mathf.Clamp01(ramp * modifier.weight) * proximity : 0f;
                    if (i > 0 && length > 1e-10f && modifier.flowDirection > 0f)
                    {
                        float shapeWeight = influence * modifier.flowDirection *
                            HairGuideShapeUtility.RootInfluence(t, modifier.rootInfluence) * (1f - Mathf.Clamp01(point.stiffness));
                        Vector3 originalUnit = originalDirection / length;
                        Vector3 steered = (Quaternion.SlerpUnclamped(Quaternion.identity,
                            Quaternion.FromToRotation(originalUnit, direction), shapeWeight) * originalUnit).normalized;
                        point.position = curve.points[i - 1].position + steered * length;
                    }
                    float facing = influence * modifier.flowFacing * (1f - Mathf.Clamp01(point.freeze));
                    facingTargets.Add(new FacingTarget { normal = normal, forward = direction, weight = facing });
                    curve.points[i] = point;
                    travelled += length;
                }
                // An upright strand can be parallel to the scalp normal: use path-forward as
                // the facing reference there. Resolve after shaping so root and tip agree.
                for (int i = 0; i < curve.points.Count; i++)
                {
                    HairCurvePoint point = curve.points[i];
                    FacingTarget target = facingTargets[i];
                    if (target.weight <= 0f) continue;
                    Vector3 tangent = HairCurveUtility.CalculateTangent(curve.points, i);
                    Vector3 desired = target.normal;
                    if (Vector3.ProjectOnPlane(desired, tangent).sqrMagnitude < 1e-10f)
                    {
                        desired = Vector3.ProjectOnPlane(target.forward, tangent).normalized;
                    }
                    if (desired.sqrMagnitude < 1e-10f) continue;
                    if (modifier.flowBank != 0f)
                        desired = Quaternion.AngleAxis(modifier.flowBank, tangent) * Vector3.ProjectOnPlane(desired, tangent).normalized;
                    float retained = point.facingWeight * (1f - target.weight);
                    point.facingNormal = (point.facingNormal * retained + desired * target.weight).normalized;
                    point.facingWeight = retained + target.weight;
                    curve.points[i] = point;
                }
            }
        }
    }
}
