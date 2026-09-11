using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public static class HairGuideShapeUtility
    {
        // Rotate in the gravity/world frame, then reconstruct at the authored segment length.
        // MultiplyVector (not MultiplyPoint) and the inverse handle rotated/scaled parents.
        public static Vector3 SettleSegment(Vector3 segment, Vector3 targetWorldDirection, float settle,
            Matrix4x4 toWorld, Matrix4x4 fromWorld)
        {
            Vector3 worldDirection = toWorld.MultiplyVector(segment).normalized;
            Vector3 rotated = Vector3.Slerp(worldDirection, targetWorldDirection, settle);
            return fromWorld.MultiplyVector(rotated).normalized * segment.magnitude;
        }
        public static void ConstrainToSurface(IReadOnlyList<Vector3> originalPoints, IList<Vector3> targetPoints,
            IReadOnlyList<HairGuidePoint> controls, HairMeshRaycaster surface, float surfaceClearance,
            Matrix4x4 toSurface, Matrix4x4 fromSurface)
        {
            if (surface == null) return;
            for (int i = 1; i < targetPoints.Count; i++)
            {
                if (controls[i].freeze >= 0.999f) continue;
                Vector3 original = toSurface.MultiplyPoint3x4(originalPoints[i]);
                Vector3 target = toSurface.MultiplyPoint3x4(targetPoints[i]);
                Vector3 travel = target - original;
                if (travel.sqrMagnitude > 1e-12f && surface.Raycast(new Ray(original, travel.normalized), out HairMeshRaycastHit swept) && swept.Distance < travel.magnitude)
                    targetPoints[i] = fromSurface.MultiplyPoint3x4(swept.Point + swept.Normal * surfaceClearance);
            }
            // Alternating surface and length projections: pinned roots/frozen anchors win if the
            // constraints conflict. Never stretch a strand to force it out of a surface.
            for (int iteration = 0; iteration < 8; iteration++)
            {
                bool moved = false;
                float arcLength = 0f;
                for (int i = 1; i < targetPoints.Count; i++)
                {
                    arcLength += Vector3.Distance(originalPoints[i - 1], originalPoints[i]);
                    if (controls[i].freeze >= 0.999f) continue;
                    for (int sample = 1; sample <= 4; sample++)
                    {
                        float t = sample * 0.25f;
                        Vector3 posed = toSurface.MultiplyPoint3x4(Vector3.Lerp(targetPoints[i - 1], targetPoints[i], t));
                        float clearance = Mathf.Min(surfaceClearance, arcLength * t * 0.2f);
                        if (!surface.ClosestPoint(posed, out HairMeshRaycastHit nearest)) continue;
                        float signed = Vector3.Dot(posed - nearest.Point, nearest.Normal);
                        if (signed >= clearance - 0.00001f) continue;
                        Vector3 correction = fromSurface.MultiplyVector(nearest.Normal * (clearance - signed));
                        targetPoints[i] += correction / t;
                        moved = true;
                    }
                }
                HairGuideShapeUtility.PreserveSegmentLengths(originalPoints, targetPoints, controls);
                if (!moved) break;
            }
            for (int i = 1; i < targetPoints.Count; i++)
            {
                Vector3 start = toSurface.MultiplyPoint3x4(targetPoints[i - 1]);
                Vector3 end = toSurface.MultiplyPoint3x4(targetPoints[i]);
                Vector3 segment = end - start;
                if (segment.sqrMagnitude > 1e-12f && surface.Raycast(new Ray(start, segment.normalized), out HairMeshRaycastHit crossing) &&
                    crossing.Distance > 0.00005f && crossing.Distance < segment.magnitude - 0.00005f)
                { for (int p = 0; p < targetPoints.Count; p++) targetPoints[p] = originalPoints[p]; return; }
                for (int sample = 1; sample <= 4; sample++)
                {
                    float t = sample * 0.25f;
                    Vector3 target = toSurface.MultiplyPoint3x4(Vector3.Lerp(targetPoints[i - 1], targetPoints[i], t));
                    Vector3 original = toSurface.MultiplyPoint3x4(Vector3.Lerp(originalPoints[i - 1], originalPoints[i], t));
                    if (!surface.ClosestPoint(target, out HairMeshRaycastHit nearest)) continue;
                    float signed = Vector3.Dot(target - nearest.Point, nearest.Normal);
                    float oldSigned = surface.ClosestPoint(original, out HairMeshRaycastHit oldHit) ?
                        Vector3.Dot(original - oldHit.Point, oldHit.Normal) : surfaceClearance;
                    if (signed < Mathf.Min(oldSigned, surfaceClearance) - 0.00005f)
                    { for (int p = 0; p < targetPoints.Count; p++) targetPoints[p] = originalPoints[p]; return; }
                }
            }
        }

        public static float GravityResponse(float t, float rootInfluence, float stiffness, float freeze,
            float strength, float deltaTime)
        {
            float mobility = Mathf.Lerp(0.2f, 1f, t) * RootInfluence(t, rootInfluence) *
                (1f - Mathf.Clamp01(stiffness) * 0.85f) * (1f - Mathf.Clamp01(freeze));
            return 1f - Mathf.Exp(-Mathf.Max(0f, strength) * Mathf.Max(0f, deltaTime) * mobility);
        }
        // Root position stays pinned. This adjusts bending response, smoothly increasing to
        // full brush strength at the tip; it is not used by Length, Cut, Width or Freeze.
        public static float RootInfluence(float normalizedPosition, float rootInfluence)
            => Mathf.Lerp(Mathf.Clamp01(rootInfluence), 1f, Mathf.Clamp01(normalizedPosition));

        public static void PreserveSegmentLengths(
            IReadOnlyList<Vector3> original,
            IList<Vector3> target,
            IReadOnlyList<HairGuidePoint> controls = null)
        {
            if (original == null || target == null || original.Count < 2 ||
                target.Count != original.Count) return;

            target[0] = original[0];
            for (int pointIndex = 1; pointIndex < original.Count; pointIndex++)
            {
                Vector3 originalSegment = original[pointIndex] - original[pointIndex - 1];
                float segmentLength = originalSegment.magnitude;
                if (segmentLength <= 1e-8f)
                {
                    target[pointIndex] = target[pointIndex - 1];
                    continue;
                }

                Vector3 targetSegment = target[pointIndex] - target[pointIndex - 1];
                Vector3 direction = targetSegment.sqrMagnitude > 1e-12f
                    ? targetSegment.normalized
                    : originalSegment / segmentLength;
                target[pointIndex] = target[pointIndex - 1] + direction * segmentLength;
            }
            if (controls == null) return;

            // Solve each span between pinned anchors independently. A frozen point must not
            // be dragged along by the length constraint on its preceding segment.
            int start = 0;
            for (int end = 1; end < original.Count; end++)
            {
                if (end >= controls.Count || controls[end].freeze < 0.999f) continue;
                target[start] = original[start];
                target[end] = original[end];
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    for (int i = end - 1; i > start; i--)
                        target[i] = AtLength(target[i + 1], target[i],
                            Vector3.Distance(original[i], original[i + 1]), original[i] - original[i + 1]);
                    for (int i = start + 1; i < end; i++)
                        target[i] = AtLength(target[i - 1], target[i],
                            Vector3.Distance(original[i], original[i - 1]), original[i] - original[i - 1]);
                }
                bool valid = true;
                for (int i = start + 1; i <= end; i++)
                {
                    float length = Vector3.Distance(original[i - 1], original[i]);
                    if (Mathf.Abs(Vector3.Distance(target[i - 1], target[i]) - length) >
                        Mathf.Max(0.000001f, length * 0.0001f)) valid = false;
                }
                // A straight, tightly pinned span may have no freedom to bend. Keep it intact
                // rather than sacrifice either its anchors or its length to the requested edit.
                if (!valid)
                    for (int i = start; i <= end; i++) target[i] = original[i];
                start = end;
            }
            for (int i = start + 1; i < original.Count; i++)
                target[i] = AtLength(target[i - 1], target[i],
                    Vector3.Distance(original[i - 1], original[i]), original[i] - original[i - 1]);
        }

        private static Vector3 AtLength(Vector3 anchor, Vector3 target, float length, Vector3 fallback)
        {
            Vector3 direction = target - anchor;
            if (direction.sqrMagnitude < 1e-12f) direction = fallback;
            return anchor + direction.normalized * length;
        }

        public static void RestoreReferenceSegmentLengths(
            IReadOnlyList<Vector3> reference,
            IReadOnlyList<Vector3> current,
            IList<Vector3> target)
        {
            if (reference == null || current == null || target == null || reference.Count < 2 ||
                current.Count != reference.Count || target.Count != reference.Count) return;

            target[0] = current[0];
            for (int pointIndex = 1; pointIndex < reference.Count; pointIndex++)
            {
                Vector3 referenceSegment = reference[pointIndex] - reference[pointIndex - 1];
                float segmentLength = referenceSegment.magnitude;
                Vector3 currentSegment = current[pointIndex] - current[pointIndex - 1];
                Vector3 direction = currentSegment.sqrMagnitude > 1e-12f
                    ? currentSegment.normalized
                    : referenceSegment.sqrMagnitude > 1e-12f
                        ? referenceSegment.normalized
                        : Vector3.up;
                target[pointIndex] = target[pointIndex - 1] + direction * segmentLength;
            }
        }

        public static Vector3 StableGravityDirection(
            Vector3 gravity,
            Vector3 rootNormal,
            int seed,
            float separation)
        {
            Vector3 down = gravity.sqrMagnitude > 1e-12f ? gravity.normalized : Vector3.down;
            Vector3 outward = Vector3.ProjectOnPlane(rootNormal, down);
            if (outward.sqrMagnitude > 1e-12f) outward.Normalize();

            Vector3 reference = Mathf.Abs(Vector3.Dot(down, Vector3.up)) < 0.92f
                ? Vector3.up
                : Vector3.right;
            Vector3 tangent = Vector3.Cross(down, reference).normalized;
            Vector3 bitangent = Vector3.Cross(down, tangent).normalized;
            float angle = StableUnit(seed) * Mathf.PI * 2f;
            Vector3 jitter = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
            Vector3 fan = outward.sqrMagnitude > 1e-12f
                ? (outward * 0.82f + jitter * 0.18f).normalized
                : jitter;
            return (down + fan * (Mathf.Clamp01(separation) * 0.45f)).normalized;
        }

        private static float StableUnit(int seed)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return (value & 0x00ffffffu) / 16777216f;
            }
        }
    }

    public static class HairCurveUtility
    {
        public static float CalculateLength(IReadOnlyList<HairCurvePoint> points)
        {
            if (points == null || points.Count < 2) return 0f;
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                length += Vector3.Distance(points[i - 1].position, points[i].position);
            }
            return length;
        }

        public static List<HairCurvePoint> Resample(IReadOnlyList<HairCurvePoint> source, int sampleCount)
        {
            List<HairCurvePoint> result = new List<HairCurvePoint>(Mathf.Clamp(sampleCount, 2, 256));
            float[] cumulative = System.Array.Empty<float>();
            ResampleInto(source, sampleCount, result, ref cumulative);
            return result;
        }

        /// <summary>Resample into caller-owned scratch storage without allocating per curve.</summary>
        public static void ResampleInto(IReadOnlyList<HairCurvePoint> source, int sampleCount,
            List<HairCurvePoint> result, ref float[] cumulative)
        {
            if (result == null) throw new System.ArgumentNullException(nameof(result));
            if (ReferenceEquals(source, result)) throw new System.ArgumentException("Resampling requires a separate destination.", nameof(result));
            result.Clear();
            int count = Mathf.Clamp(sampleCount, 2, 256);
            if (result.Capacity < count) result.Capacity = count;
            if (source == null || source.Count == 0)
            {
                result.Add(new HairCurvePoint(Vector3.zero, 0f, 0f));
                result.Add(new HairCurvePoint(Vector3.up * 0.01f, 0f, 0f));
                return;
            }
            if (source.Count == 1)
            {
                result.Add(source[0]);
                result.Add(new HairCurvePoint(source[0].position + Vector3.up * 0.01f,
                    source[0].width, source[0].roll, source[0].widthBaseline, source[0].profileScale));
                return;
            }

            if (cumulative == null || cumulative.Length < source.Count) System.Array.Resize(ref cumulative, source.Count);
            cumulative[0] = 0f;
            float total = 0f;
            for (int i = 1; i < source.Count; i++)
            {
                total += Vector3.Distance(source[i - 1].position, source[i].position);
                cumulative[i] = total;
            }
            if (total < 1e-7f)
            {
                HairCurvePoint first = source[0];
                for (int i = 0; i < count; i++) result.Add(first);
                return;
            }

            int segment = 0;
            for (int sample = 0; sample < count; sample++)
            {
                float target = total * sample / (count - 1f);
                while (segment + 1 < source.Count - 1 && cumulative[segment + 1] < target)
                {
                    segment++;
                }
                float start = cumulative[segment];
                float end = cumulative[segment + 1];
                float t = end - start > 1e-7f ? (target - start) / (end - start) : 0f;
                HairCurvePoint left = source[segment];
                HairCurvePoint right = source[segment + 1];
                result.Add(new HairCurvePoint(
                    Vector3.LerpUnclamped(left.position, right.position, t),
                    Mathf.LerpUnclamped(left.width, right.width, t),
                    Mathf.LerpAngle(left.roll, right.roll, t),
                    left.widthBaseline >= 0f && right.widthBaseline >= 0f
                        ? Mathf.LerpUnclamped(left.widthBaseline, right.widthBaseline, t) : -1f,
                    Mathf.LerpUnclamped(left.profileScale, right.profileScale, t),
                    Mathf.LerpUnclamped(left.stiffness, right.stiffness, t),
                    Mathf.LerpUnclamped(left.freeze, right.freeze, t)));
            }
        }

        public static void Smooth(List<HairCurvePoint> points, float amount, int iterations, bool lockRoot)
        {
            HairCurvePoint[] buffer = System.Array.Empty<HairCurvePoint>();
            Smooth(points, amount, iterations, lockRoot, ref buffer);
        }

        internal static void Smooth(List<HairCurvePoint> points, float amount, int iterations, bool lockRoot, ref HairCurvePoint[] buffer)
        {
            if (points == null || points.Count < 3) return;
            float weight = Mathf.Clamp01(amount);
            int passCount = Mathf.Clamp(iterations, 1, 32);
            if (buffer.Length < points.Count) System.Array.Resize(ref buffer, points.Count);
            for (int pass = 0; pass < passCount; pass++)
            {
                points.CopyTo(buffer);
                int start = lockRoot ? 1 : 0;
                for (int i = Mathf.Max(1, start); i < points.Count - 1; i++)
                {
                    HairCurvePoint current = buffer[i];
                    Vector3 average = (buffer[i - 1].position + buffer[i + 1].position) * 0.5f;
                    current.position = Vector3.Lerp(current.position, average, weight);
                    current.width = Mathf.Lerp(current.width,
                        (buffer[i - 1].width + buffer[i + 1].width) * 0.5f, weight);
                    if (current.widthBaseline >= 0f && buffer[i - 1].widthBaseline >= 0f && buffer[i + 1].widthBaseline >= 0f)
                        current.widthBaseline = Mathf.Lerp(current.widthBaseline,
                            (buffer[i - 1].widthBaseline + buffer[i + 1].widthBaseline) * 0.5f, weight);
                    current.profileScale = Mathf.Lerp(current.profileScale,
                        (buffer[i - 1].profileScale + buffer[i + 1].profileScale) * 0.5f, weight);
                    current.roll = Mathf.LerpAngle(current.roll,
                        Mathf.LerpAngle(buffer[i - 1].roll, buffer[i + 1].roll, 0.5f), weight);
                    points[i] = current;
                }
            }
        }

        public static void ScaleLength(List<HairCurvePoint> points, float multiplier)
        {
            if (points == null || points.Count < 2) return;
            float scale = Mathf.Max(0f, multiplier);
            Vector3 root = points[0].position;
            for (int i = 1; i < points.Count; i++)
            {
                HairCurvePoint point = points[i];
                point.position = root + (point.position - root) * scale;
                points[i] = point;
            }
        }

        public static Vector3 CalculateTangent(IReadOnlyList<HairCurvePoint> points, int index)
        {
            if (points == null || points.Count < 2) return Vector3.up;
            int previous = Mathf.Max(0, index - 1);
            int next = Mathf.Min(points.Count - 1, index + 1);
            Vector3 tangent = points[next].position - points[previous].position;
            if (tangent.sqrMagnitude > 1e-10f) return tangent.normalized;
            for (int radius = 1; radius < points.Count; radius++)
            {
                previous = Mathf.Max(0, index - radius);
                next = Mathf.Min(points.Count - 1, index + radius);
                tangent = points[next].position - points[previous].position;
                if (tangent.sqrMagnitude > 1e-10f) return tangent.normalized;
            }
            return Vector3.up;
        }

        public static void BuildRotationMinimizingFrames(
            IReadOnlyList<HairCurvePoint> points,
            Vector3 rootNormal,
            Vector3[] tangents,
            Vector3[] sides,
            Vector3[] normals,
            out int flipCount)
        {
            flipCount = 0;
            if (points == null || points.Count == 0) return;
            Vector3 firstTangent = CalculateTangent(points, 0);
            Vector3 initialNormal = Vector3.ProjectOnPlane(rootNormal, firstTangent);
            if (initialNormal.sqrMagnitude < 1e-8f)
            {
                Vector3 fallback = Mathf.Abs(Vector3.Dot(firstTangent, Vector3.up)) < 0.95f
                    ? Vector3.up
                    : Vector3.right;
                initialNormal = Vector3.ProjectOnPlane(fallback, firstTangent);
            }
            initialNormal.Normalize();
            Vector3 side = Vector3.Cross(firstTangent, initialNormal).normalized;
            initialNormal = Vector3.Cross(side, firstTangent).normalized;

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 tangent = CalculateTangent(points, i);
                if (i > 0)
                {
                    Quaternion transport = Quaternion.FromToRotation(tangents[i - 1], tangent);
                    side = transport * side;
                    side = Vector3.ProjectOnPlane(side, tangent).normalized;
                    if (side.sqrMagnitude < 1e-8f)
                    {
                        side = Vector3.Cross(tangent, normals[i - 1]).normalized;
                    }
                }
                Quaternion roll = Quaternion.AngleAxis(points[i].roll, tangent);
                Vector3 rolledSide = roll * side;
                Vector3 normal = Vector3.Cross(rolledSide, tangent).normalized;
                if (i > 0 && Vector3.Dot(rolledSide, sides[i - 1]) < -0.25f) flipCount++;
                tangents[i] = tangent;
                sides[i] = rolledSide;
                normals[i] = normal;
                side = rolledSide;
            }
        }
    }
}
