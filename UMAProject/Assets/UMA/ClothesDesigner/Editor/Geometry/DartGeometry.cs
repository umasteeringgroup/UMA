using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The shapes of darts and pleats, worked out once for the mesher, the sizes, the checks and the
    /// Pattern Canvas. Pass <see cref="PieceScale.Identity"/> for the pattern as drawn, or a piece's
    /// scale for the cloth as sized; fractions along edges are the same in both.
    /// </summary>
    public static class DartGeometry
    {
        /// <summary>How far apart the folded layers of a pleat start (cm), so the solver sees which lies on which.</summary>
        public const float PleatLayerCm = 0.25f;
        /// <summary>Openings closer than this to an edge's ends are refused (fraction of the edge).</summary>
        private const float EdgeMargin = 0.01f;

        // ---- Edge darts ------------------------------------------------------------------------

        public struct EdgeDartShape
        {
            public int edge;
            /// <summary>The opening, as fractions of the edge: the first leg starts at <see cref="f0"/>, the second ends at <see cref="f1"/>.</summary>
            public float f0, f1;
            public Vector2 m0, tip, m1;
            /// <summary>
            /// How far each leg is drawn towards the dart's centre line, as a share of its distance from it
            /// that grows towards the point: 0 straight legs, positive curved in (less taken out near the
            /// point), negative curved out. Below 1 the legs can never cross.
            /// </summary>
            public float bend;

            private Vector2 Mouth => (m0 + m1) * 0.5f;

            /// <summary>The first leg, from the opening to the point (t 0 to 1).</summary>
            public Vector2 LegA(float t) => Vector2.Lerp(Vector2.Lerp(m0, tip, t), Vector2.Lerp(Mouth, tip, t), bend * t);
            /// <summary>The second leg, from the point back to the opening (t 0 to 1).</summary>
            public Vector2 LegB(float t) => Vector2.Lerp(Vector2.Lerp(tip, m1, t), Vector2.Lerp(tip, Mouth, t), bend * (1f - t));

            public float LegLength
            {
                get
                {
                    float a = 0f, b = 0f;
                    for (int i = 1; i <= 16; i++)
                    {
                        a += (LegA(i / 16f) - LegA((i - 1) / 16f)).magnitude;
                        b += (LegB(i / 16f) - LegB((i - 1) / 16f)).magnitude;
                    }
                    return Mathf.Max(a, b);
                }
            }
        }

        public static bool EdgeDart(PatternPiece piece, PatternDart dart, PieceScale scale, out EdgeDartShape shape)
        {
            shape = default;
            if (dart == null || dart.kind != DartKind.Edge) return false;
            int e = piece.IndexOfEdge(dart.edgeId);
            if (e < 0) return false;
            var drawnCurve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
            if (drawnCurve.Length <= 1e-4f) return false;
            float half = Mathf.Max(0.05f, dart.widthCm) * 0.5f / drawnCurve.Length;
            shape.edge = e;
            shape.f0 = dart.position - half;
            shape.f1 = dart.position + half;
            var curve = new PatternGeometry.EdgeCurve(piece, e, scale);
            shape.m0 = curve.At(shape.f0);
            shape.m1 = curve.At(shape.f1);
            shape.tip = scale.Apply(dart.tip);
            // curveCm is how far each leg moves at its middle. There a straight leg is a quarter of the
            // width from the centre line and the leg is drawn bend/2 of the way in, so bend = 8 * curve / width.
            shape.bend = Mathf.Clamp(8f * dart.curveCm / Mathf.Max(0.05f, dart.widthCm), -3f, 0.9f);
            return true;
        }

        /// <summary>The most an edge dart's legs can curve towards each other (cm), so they still meet only at the point.</summary>
        public static float MaxInwardCurve(PatternDart dart) => 0.9f * Mathf.Max(0.05f, dart.widthCm) / 8f;

        // ---- Double-pointed darts ------------------------------------------------------------------

        /// <summary>A point on one side of a double dart: <paramref name="s"/> 0 at the first point to 1 at the second; side +1 or -1.</summary>
        public static Vector2 DoublePoint(PatternDart dart, float s, int side, PieceScale scale)
        {
            Vector2 a = dart.tip, b = dart.tip2;
            Vector2 axis = b - a;
            Vector2 perp = new Vector2(-axis.y, axis.x).normalized;
            float w = Mathf.Clamp(dart.widestAt, 0.1f, 0.9f);
            float tri = s <= w ? s / w : (1f - s) / (1f - w);
            float round = 1f - (1f - tri) * (1f - tri);
            float offset = Mathf.Max(0.05f, dart.widthCm) * 0.5f * Mathf.Lerp(tri, round, Mathf.Clamp01(dart.roundness));
            return scale.Apply(a + axis * s + perp * (offset * side));
        }

        /// <summary>The closed outline of a double dart, about <paramref name="spacing"/> cm between points: side +1 from the first point to the second, then side -1 back.</summary>
        public static List<Vector2> DoubleOutline(PatternDart dart, float spacing, PieceScale scale, out int perSide)
        {
            float length = (scale.Apply(dart.tip2) - scale.Apply(dart.tip)).magnitude;
            perSide = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.05f, spacing)));
            var points = new List<Vector2>(perSide * 2);
            for (int k = 0; k < perSide; k++) points.Add(DoublePoint(dart, k / (float)perSide, 1, scale));
            for (int k = 0; k < perSide; k++) points.Add(DoublePoint(dart, 1f - k / (float)perSide, -1, scale));
            return points;
        }

        // ---- Pleats -------------------------------------------------------------------------------

        public sealed class PleatShape
        {
            public int edge;
            /// <summary>Where each fold line meets the edge (fractions of the edge), in order along the pleat.</summary>
            public float[] lineFractions;
            /// <summary>Lines that are folds (creased); the others are where the fabric is placed.</summary>
            public bool[] crease;
            /// <summary>Pairs of lines sewn together at the edge (and down the stitched length).</summary>
            public Vector2Int[] stitchedLines;
            /// <summary>How the fabric between line i and i+1 is laid: +1 forward, -1 folded back; and its layer (0 on top).</summary>
            public int[] direction, layer;
            /// <summary>The parts of the edge taken up by the pleat (fractions), which a seam on the edge skips.</summary>
            public Vector2[] intakes;
            /// <summary>Along the pleat, left to right as drawn.</summary>
            public Vector2 tangent;
            /// <summary>Into the piece, the direction the fold lines run.</summary>
            public Vector2 inward;
            public float intakeCm;
        }

        public static bool Pleat(PatternPiece piece, PatternPleat pleat, PieceScale scale, out PleatShape shape)
        {
            shape = null;
            if (pleat == null) return false;
            int e = piece.IndexOfEdge(pleat.edgeId);
            if (e < 0) return false;
            var drawnCurve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
            float length = drawnCurve.Length;
            if (length <= 1e-4f) return false;
            float d = Mathf.Max(0.1f, pleat.depthCm), w = Mathf.Max(0f, pleat.boxWidthCm);
            float[] offsets; int[] dir, lay; bool[] crease; Vector2Int[] stitched;
            switch (pleat.kind)
            {
                case PleatKind.KnifeLeft:
                    offsets = new[] { 0f, d, 2f * d }; dir = new[] { 1, -1 }; lay = new[] { 2, 1 };
                    crease = new[] { false, true, true }; stitched = new[] { new Vector2Int(0, 2) };
                    break;
                case PleatKind.Box:
                    offsets = new[] { 0f, d, 2f * d, 2f * d + w, 3f * d + w, 4f * d + w }; dir = new[] { 1, -1, 1, -1, 1 }; lay = new[] { 2, 1, 0, 1, 2 };
                    crease = new[] { false, true, true, true, true, false }; stitched = new[] { new Vector2Int(0, 2), new Vector2Int(3, 5) };
                    break;
                case PleatKind.Inverted:
                    offsets = new[] { 0f, d, 2f * d, 3f * d, 4f * d }; dir = new[] { -1, 1, 1, -1 }; lay = new[] { 1, 2, 2, 1 };
                    crease = new[] { true, true, false, true, true }; stitched = new[] { new Vector2Int(0, 2), new Vector2Int(2, 4) };
                    break;
                default: // KnifeRight
                    offsets = new[] { 0f, d, 2f * d }; dir = new[] { -1, 1 }; lay = new[] { 1, 2 };
                    crease = new[] { true, true, false }; stitched = new[] { new Vector2Int(0, 2) };
                    break;
            }
            float span = offsets[offsets.Length - 1];
            // "Left to right as drawn": along +x, or up for an upright edge.
            Vector2 drawnTangent = (drawnCurve.At(Mathf.Clamp01(pleat.position + 0.01f)) - drawnCurve.At(Mathf.Clamp01(pleat.position - 0.01f))).normalized;
            bool edgeRuns = Mathf.Abs(drawnTangent.x) > 0.3f ? drawnTangent.x > 0f : drawnTangent.y > 0f;
            float sign = edgeRuns ? 1f : -1f;
            var fractions = new float[offsets.Length];
            for (int i = 0; i < offsets.Length; i++) fractions[i] = pleat.position + sign * (offsets[i] - span * 0.5f) / length;
            var curve = new PatternGeometry.EdgeCurve(piece, e, scale);
            Vector2 t = (curve.At(Mathf.Clamp01(pleat.position + 0.01f)) - curve.At(Mathf.Clamp01(pleat.position - 0.01f))).normalized;
            // Outlines run counter-clockwise, so the inside is on the left of the edge's direction.
            Vector2 inward = new Vector2(-t.y, t.x);
            shape = new PleatShape
            {
                edge = e,
                lineFractions = fractions,
                crease = crease,
                stitchedLines = stitched,
                direction = dir,
                layer = lay,
                tangent = t * sign,
                inward = inward,
                intakeCm = pleat.kind == PleatKind.KnifeLeft || pleat.kind == PleatKind.KnifeRight ? 2f * d : 4f * d
            };
            var intakes = new List<Vector2>();
            foreach (Vector2Int pair in stitched)
            {
                float a = fractions[pair.x], b = fractions[pair.y];
                intakes.Add(new Vector2(Mathf.Min(a, b), Mathf.Max(a, b)));
            }
            shape.intakes = intakes.ToArray();
            return true;
        }

        /// <summary>
        /// The points of one fold line inside the piece, after the edge point: <paramref name="spacing"/>
        /// apart, stopping before the outline (or at the pleat's length).
        /// </summary>
        public static List<Vector2> PleatLine(PatternPleat pleat, PleatShape shape, Vector2 start, IList<Vector2> outline, float spacing)
        {
            var points = new List<Vector2>();
            float limit = pleat.lengthCm > 0f ? pleat.lengthCm : float.MaxValue;
            const int MaxSteps = 1000;
            float stop = spacing * 0.5f;
            // Only outline segments that come within the stopping distance of the whole line can stop it by
            // distance; the rest are left out of that test. Same answer, without measuring every point of the
            // line against the whole outline (tens of milliseconds a frame on a long box pleat).
            Vector2 last = start + shape.inward * Mathf.Min(limit + 1e-3f, spacing * MaxSteps);
            var near = new List<Vector2>();
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
                if (PatternGeometry.SegmentDistance(start, last, a, b) < stop + 0.01f) { near.Add(a); near.Add(b); }
            }
            for (int j = 1; j < MaxSteps; j++)
            {
                float along = spacing * j;
                if (along > limit + 1e-3f) break;
                Vector2 q = start + shape.inward * along;
                if (CloserThan(near, q, stop) || !PatternGeometry.PointInPolygon(outline, q)) break;
                points.Add(q);
            }
            return points;
        }

        /// <summary>Whether a point is closer than <paramref name="distance"/> to any of the segments (pairs of points).</summary>
        private static bool CloserThan(List<Vector2> segments, Vector2 p, float distance)
        {
            // The same sums as PatternGeometry.DistanceToPolyline, so the answer matches it exactly.
            float best = float.MaxValue;
            for (int i = 0; i + 1 < segments.Count; i += 2)
            {
                Vector2 a = segments[i], ab = segments[i + 1] - a;
                float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
            }
            return Mathf.Sqrt(best) < distance;
        }

        // ---- What the edges lose -----------------------------------------------------------------

        /// <summary>
        /// The parts of an edge taken out by darts and pleats (fractions, sorted). A seam on the edge
        /// skips them: what is left is sewn to the other side.
        /// </summary>
        public static List<Vector2> Intakes(PatternPiece piece, int edgeIndex)
        {
            var result = new List<Vector2>();
            foreach (PatternDart dart in piece.darts)
            {
                if (EdgeDart(piece, dart, PieceScale.Identity, out EdgeDartShape s) && s.edge == edgeIndex) result.Add(new Vector2(s.f0, s.f1));
            }
            foreach (PatternPleat pleat in piece.pleats)
            {
                if (Pleat(piece, pleat, PieceScale.Identity, out PleatShape s) && s.edge == edgeIndex) result.AddRange(s.intakes);
            }
            result.Sort((x, y) => x.x.CompareTo(y.x));
            return result;
        }

        /// <summary>The length of a span of an edge that is left once darts and pleats are taken out (cm, as drawn).</summary>
        public static float EffectiveLength(PatternPiece piece, int edgeIndex, float start, float end)
        {
            float length = PatternGeometry.EdgeLength(piece, edgeIndex, PieceScale.Identity);
            float covered = end - start;
            foreach (Vector2 i in Intakes(piece, edgeIndex))
            {
                covered -= Mathf.Max(0f, Mathf.Min(end, i.y) - Mathf.Max(start, i.x));
            }
            return Mathf.Max(0f, covered) * length;
        }

        /// <summary>
        /// The width darts and pleats take out of a piece along a horizontal line (cm, as drawn), so a
        /// size measured across the piece counts only the fabric that is left. A pleat takes out its
        /// fabric only where it is sewn shut; below that its folds open with the body.
        /// </summary>
        public static float IntakeAcross(PatternPiece piece, float y)
        {
            float total = 0f;
            foreach (PatternDart dart in piece.darts)
            {
                List<Vector2> polygon = DartPolygon(piece, dart, 0.5f);
                if (polygon != null && PatternGeometry.WidthAt(polygon, y, out float minX, out float maxX)) total += maxX - minX;
            }
            List<Vector2> outline = null;
            foreach (PatternPleat pleat in piece.pleats)
            {
                if (!Pleat(piece, pleat, PieceScale.Identity, out PleatShape s)) continue;
                outline = outline ?? PatternGeometry.SampleOutline(piece, 1f, null);
                var curve = new PatternGeometry.EdgeCurve(piece, s.edge, PieceScale.Identity);
                Vector2 start = curve.At(s.lineFractions[0]);
                List<Vector2> line = PleatLine(pleat, s, start, outline, 1f);
                Vector2 reach = line.Count > 0 ? line[line.Count - 1] : start;
                Vector2 heldTo = start + s.inward * Mathf.Min(pleat.stitchedCm, (reach - start).magnitude);
                float low = Mathf.Min(start.y, heldTo.y) - 1e-3f, high = Mathf.Max(start.y, heldTo.y) + 1e-3f;
                if (y >= low && y <= high) total += s.intakeCm;
            }
            return total;
        }

        /// <summary>The region a dart takes out, as a polygon (as drawn). Null when it does not resolve.</summary>
        public static List<Vector2> DartPolygon(PatternPiece piece, PatternDart dart, float spacing)
        {
            if (dart == null) return null;
            if (dart.kind == DartKind.Double) return DoubleOutline(dart, spacing, PieceScale.Identity, out _);
            if (!EdgeDart(piece, dart, PieceScale.Identity, out EdgeDartShape s)) return null;
            int n = Mathf.Max(2, Mathf.CeilToInt(s.LegLength / Mathf.Max(0.05f, spacing)));
            var points = new List<Vector2>();
            for (int k = 0; k < n; k++) points.Add(s.LegA(k / (float)n));
            for (int k = 0; k < n; k++) points.Add(s.LegB(k / (float)n));
            points.Add(s.m1);
            return points;
        }

        // ---- Checks ---------------------------------------------------------------------------

        /// <summary>What keeps a piece's darts and pleats from being made, in words for the artist.</summary>
        public static List<string> FindProblems(PatternPiece piece)
        {
            var problems = new List<string>();
            if (piece.darts == null || piece.pleats == null || piece.points.Count < 3) return problems;
            List<Vector2> outline = PatternGeometry.SampleOutline(piece, 0.5f, null);
            var taken = new Dictionary<int, List<Vector2>>();
            void Take(int edge, Vector2 range, string what)
            {
                if (!taken.TryGetValue(edge, out List<Vector2> list)) taken[edge] = list = new List<Vector2>();
                foreach (Vector2 r in list)
                {
                    if (range.x < r.y && r.x < range.y) { problems.Add("On '" + piece.name + "', " + what + " overlaps another dart or pleat on the same edge."); return; }
                }
                list.Add(range);
            }
            for (int i = 0; i < piece.darts.Count; i++)
            {
                PatternDart dart = piece.darts[i];
                string what = "dart " + (i + 1);
                if (dart.widthCm < 0.2f) { problems.Add("On '" + piece.name + "', " + what + " has no width."); continue; }
                if (dart.kind == DartKind.Edge)
                {
                    if (!EdgeDart(piece, dart, PieceScale.Identity, out EdgeDartShape s)) { problems.Add("On '" + piece.name + "', " + what + " is on an edge that no longer exists. Delete it and add it again."); continue; }
                    if (s.f0 < EdgeMargin || s.f1 > 1f - EdgeMargin) { problems.Add("On '" + piece.name + "', " + what + "'s opening runs past the end of its edge. Move it or make it narrower."); continue; }
                    if (!PatternGeometry.PointInPolygon(outline, s.tip)) { problems.Add("On '" + piece.name + "', the point of " + what + " is outside the piece."); continue; }
                    for (int k = 1; k < 8; k++)
                    {
                        if (!PatternGeometry.PointInPolygon(outline, s.LegA(k / 8f)) || !PatternGeometry.PointInPolygon(outline, s.LegB(k / 8f)))
                        {
                            problems.Add("On '" + piece.name + "', a leg of " + what + " leaves the piece. Lessen its curve or move its point.");
                            break;
                        }
                    }
                    Take(s.edge, new Vector2(s.f0, s.f1), what);
                }
                else
                {
                    if ((dart.tip2 - dart.tip).magnitude < 1f) { problems.Add("On '" + piece.name + "', " + what + " is too short."); continue; }
                    List<Vector2> hole = DoubleOutline(dart, 0.5f, PieceScale.Identity, out _);
                    foreach (Vector2 q in hole)
                    {
                        if (!PatternGeometry.PointInPolygon(outline, q) || PatternGeometry.DistanceToPolyline(outline, q) < 0.5f)
                        {
                            problems.Add("On '" + piece.name + "', " + what + " reaches the edge of the piece. A double-pointed dart must lie inside; use an edge dart for one that opens on an edge.");
                            break;
                        }
                    }
                }
            }
            for (int i = 0; i < piece.pleats.Count; i++)
            {
                PatternPleat pleat = piece.pleats[i];
                string what = "pleat " + (i + 1);
                if (!Pleat(piece, pleat, PieceScale.Identity, out PleatShape s)) { problems.Add("On '" + piece.name + "', " + what + " is on an edge that no longer exists. Delete it and add it again."); continue; }
                float first = Mathf.Min(s.lineFractions[0], s.lineFractions[s.lineFractions.Length - 1]);
                float last = Mathf.Max(s.lineFractions[0], s.lineFractions[s.lineFractions.Length - 1]);
                if (first < EdgeMargin || last > 1f - EdgeMargin) { problems.Add("On '" + piece.name + "', " + what + " runs past the end of its edge. Move it or make it shallower."); continue; }
                Take(s.edge, new Vector2(first, last), what);
            }
            return problems;
        }
    }
}
