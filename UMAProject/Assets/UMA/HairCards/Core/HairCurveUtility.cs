using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
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
            int count = Mathf.Clamp(sampleCount, 2, 256);
            List<HairCurvePoint> result = new List<HairCurvePoint>(count);
            if (source == null || source.Count == 0)
            {
                result.Add(new HairCurvePoint(Vector3.zero, 0f, 0f));
                result.Add(new HairCurvePoint(Vector3.up * 0.01f, 0f, 0f));
                return result;
            }
            if (source.Count == 1)
            {
                result.Add(source[0]);
                result.Add(new HairCurvePoint(source[0].position + Vector3.up * 0.01f,
                    source[0].width, source[0].roll));
                return result;
            }

            float[] cumulative = new float[source.Count];
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
                return result;
            }

            int segment = 0;
            for (int sample = 0; sample < count; sample++)
            {
                float target = total * sample / (count - 1f);
                while (segment + 1 < cumulative.Length - 1 && cumulative[segment + 1] < target)
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
                    Mathf.LerpAngle(left.roll, right.roll, t)));
            }
            return result;
        }

        public static void Smooth(List<HairCurvePoint> points, float amount, int iterations, bool lockRoot)
        {
            if (points == null || points.Count < 3) return;
            float weight = Mathf.Clamp01(amount);
            int passCount = Mathf.Clamp(iterations, 1, 32);
            HairCurvePoint[] buffer = new HairCurvePoint[points.Count];
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
