using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>How a piece is resized by the pattern's dimensions: x' = cx + (x - cx) * sx, y' = y0 + (y - y0) * sy.</summary>
    public struct PieceScale
    {
        public float sx, sy, cx, y0;
        public static PieceScale Identity => new PieceScale { sx = 1f, sy = 1f };
        public Vector2 Apply(Vector2 p) => new Vector2(cx + (p.x - cx) * sx, y0 + (p.y - y0) * sy);
        public Vector2 ApplyVector(Vector2 v) => new Vector2(v.x * sx, v.y * sy);
        public Vector2 Invert(Vector2 p) => new Vector2(cx + (p.x - cx) / sx, y0 + (p.y - y0) / sy);
    }

    /// <summary>A dimension as evaluated on a body, for showing before fitting.</summary>
    public struct EvaluatedDimension
    {
        public PatternDimension dimension;
        public float drawnCm;
        public float targetCm;
        public float measuredCm;
        public bool fromBody;
    }

    /// <summary>Curves, lengths, outlines and the evaluation of dimensions. Pure geometry in centimetres.</summary>
    public static class PatternGeometry
    {
        private const int CurveSteps = 48;

        public static void EdgeControlPoints(PatternPiece piece, int edgeIndex, PieceScale scale, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3, out bool curved)
        {
            int n = piece.points.Count;
            PatternEdge edge = piece.edges[edgeIndex];
            p0 = scale.Apply(piece.points[edgeIndex].position);
            p3 = scale.Apply(piece.points[(edgeIndex + 1) % n].position);
            curved = edge != null && edge.curved;
            p1 = curved ? p0 + scale.ApplyVector(edge.handleA) : p0;
            p2 = curved ? p3 + scale.ApplyVector(edge.handleB) : p3;
        }

        public static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        /// <summary>Points along an edge at evenly spaced fractions of its length (arc length, not curve parameter).</summary>
        public sealed class EdgeCurve
        {
            private readonly Vector2[] samples = new Vector2[CurveSteps + 1];
            private readonly float[] lengths = new float[CurveSteps + 1];
            public float Length => lengths[CurveSteps];

            public EdgeCurve(PatternPiece piece, int edgeIndex, PieceScale scale)
            {
                EdgeControlPoints(piece, edgeIndex, scale, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3, out bool curved);
                for (int i = 0; i <= CurveSteps; i++)
                {
                    float t = i / (float)CurveSteps;
                    samples[i] = curved ? Bezier(p0, p1, p2, p3, t) : Vector2.Lerp(p0, p3, t);
                    lengths[i] = i == 0 ? 0f : lengths[i - 1] + (samples[i] - samples[i - 1]).magnitude;
                }
            }

            /// <summary>The point at a fraction of the edge's length.</summary>
            public Vector2 At(float fraction)
            {
                float target = Mathf.Clamp01(fraction) * Length;
                if (Length <= 0f) return samples[0];
                int lo = 0, hi = CurveSteps;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (lengths[mid] < target) lo = mid; else hi = mid;
                }
                float span = lengths[hi] - lengths[lo];
                float s = span > 1e-9f ? (target - lengths[lo]) / span : 0f;
                return Vector2.Lerp(samples[lo], samples[hi], s);
            }

            /// <summary>The fraction of the edge's length nearest to a point, and the distance to it.</summary>
            public float Nearest(Vector2 point, out float distance)
            {
                float best = float.MaxValue, bestFraction = 0f;
                for (int i = 0; i < CurveSteps; i++)
                {
                    Vector2 a = samples[i], b = samples[i + 1];
                    Vector2 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
                    float d = (a + ab * t - point).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        bestFraction = Length > 0f ? (lengths[i] + (lengths[i + 1] - lengths[i]) * t) / Length : 0f;
                    }
                }
                distance = Mathf.Sqrt(best);
                return bestFraction;
            }
        }

        public static float EdgeLength(PatternPiece piece, int edgeIndex, PieceScale scale) => new EdgeCurve(piece, edgeIndex, scale).Length;

        /// <summary>The closed outline as a polyline, about <paramref name="spacing"/> cm between points.</summary>
        public static List<Vector2> SampleOutline(PatternPiece piece, float spacing, PieceScale? scale)
        {
            var result = new List<Vector2>();
            PieceScale s = scale ?? PieceScale.Identity;
            for (int e = 0; e < piece.points.Count; e++)
            {
                var curve = new EdgeCurve(piece, e, s);
                int count = Mathf.Max(1, Mathf.CeilToInt(curve.Length / Mathf.Max(0.05f, spacing)));
                for (int i = 0; i < count; i++) result.Add(curve.At(i / (float)count));
            }
            return result;
        }

        public static float SignedArea(IList<Vector2> polygon)
        {
            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += (double)a.x * b.y - (double)b.x * a.y;
            }
            return (float)(area * 0.5);
        }

        public static bool PointInPolygon(IList<Vector2> polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        public static float DistanceToPolyline(IList<Vector2> polygon, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        public static bool OutlineSelfIntersects(PatternPiece piece)
        {
            List<Vector2> poly = SampleOutline(piece, 1f, null);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    Vector2 c = poly[j], d = poly[(j + 1) % n];
                    if (SegmentsCross(a, b, c, d)) return true;
                }
            }
            return false;
        }

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>Where the outline crosses a horizontal line: the leftmost and rightmost x. False when it does not.</summary>
        public static bool WidthAt(IList<Vector2> polygon, float y, out float minX, out float maxX)
        {
            minX = float.MaxValue; maxX = float.MinValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                if ((a.y - y) * (b.y - y) > 0f || a.y == b.y) continue;
                float x = a.x + (b.x - a.x) * (y - a.y) / (b.y - a.y);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            }
            return maxX > minX;
        }

        public static float MeasurementCm(BodyLandmarks body, BodyMeasurement measurement)
        {
            if (body == null || !body.valid) return 0f;
            switch (measurement)
            {
                case BodyMeasurement.Chest: return body.chestCm;
                case BodyMeasurement.Waist: return body.waistCm;
                case BodyMeasurement.Hip: return body.hipCm;
                case BodyMeasurement.ShoulderToWaist: return (body.shoulderTopY - body.waistY) * 100f;
                case BodyMeasurement.ArmLength: return body.armLengthCm;
                case BodyMeasurement.WaistToAnkle: return body.waistToAnkleCm;
                case BodyMeasurement.Inseam: return body.inseamCm;
                case BodyMeasurement.Neck: return body.neckCm;
                case BodyMeasurement.Head: return body.headCm;
                case BodyMeasurement.Thigh: return body.thighCm;
                case BodyMeasurement.UpperArm: return body.upperArmCm;
                default: return 0f;
            }
        }

        /// <summary>
        /// Works out each piece's scale from the pattern's dimensions on a body (or on their fixed lengths
        /// without one). Widths are applied first, then lengths.
        /// </summary>
        public static PieceScale[] Evaluate(ClothesPattern pattern, BodyLandmarks body, ClothesFit fit, List<EvaluatedDimension> report)
        {
            var scales = new PieceScale[pattern.pieces.Count];
            for (int i = 0; i < scales.Length; i++)
            {
                Rect bounds = pattern.pieces[i].Bounds();
                scales[i] = new PieceScale { sx = 1f, sy = 1f, cx = bounds.center.x, y0 = bounds.yMin };
            }
            foreach (DimensionKind pass in new[] { DimensionKind.Width, DimensionKind.Length })
            {
                foreach (PatternDimension dim in pattern.dimensions)
                {
                    if (dim == null || dim.kind != pass) continue;
                    float measured = MeasurementCm(body, dim.measurement);
                    bool fromBody = dim.measurement != BodyMeasurement.None && measured > 0f;
                    float allowance = fit != null && fit.AllowanceFor(dim.id).HasValue ? fit.AllowanceFor(dim.id).Value : dim.allowanceCm;
                    float total = fromBody ? measured + allowance : dim.fixedCm;
                    if (total <= 0f) continue;
                    float target = dim.kind == DimensionKind.Width ? total / Mathf.Max(1, dim.shareCount) : total;
                    float drawn = 0f;
                    foreach (string pieceId in dim.pieceIds)
                    {
                        int index = pattern.IndexOfPiece(pieceId);
                        if (index < 0) continue;
                        PatternPiece piece = pattern.pieces[index];
                        List<Vector2> outline = SampleOutline(piece, 0.5f, null);
                        if (dim.kind == DimensionKind.Width)
                        {
                            if (!WidthAt(outline, dim.lineY, out float minX, out float maxX)) continue;
                            // Only the fabric left once darts and pleats are sewn goes round the body.
                            drawn = maxX - minX - DartGeometry.IntakeAcross(piece, dim.lineY);
                            if (drawn <= 0.5f) continue;
                            scales[index].sx = target / drawn;
                        }
                        else
                        {
                            Rect b = piece.Bounds();
                            drawn = b.height;
                            if (drawn > 0f) scales[index].sy = target / drawn;
                        }
                    }
                    report?.Add(new EvaluatedDimension { dimension = dim, drawnCm = drawn, targetCm = target, measuredCm = measured, fromBody = fromBody });
                }
            }
            return scales;
        }
    }
}
