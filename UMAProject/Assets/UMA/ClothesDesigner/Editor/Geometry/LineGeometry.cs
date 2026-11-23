using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Lines drawn inside a piece (seam lines, folds, holes) and single stitches (tacks): their curves,
    /// where they meet the outline, and the checks that keep them meshable.
    /// </summary>
    public static class LineGeometry
    {
        /// <summary>A line's end this close to the outline (cm, as drawn) starts on the outline.</summary>
        public const float SnapCm = 0.15f;
        /// <summary>How far a fold's line is laid out from flat, to give the crease its direction (cm).</summary>
        public const float FoldNudgeCm = 0.3f;

        /// <summary>The line as a curve, sized like its piece.</summary>
        public static PatternGeometry.EdgeCurve Curve(PatternLine line, PieceScale scale)
        {
            var points = new List<Vector2>(line.points.Count);
            foreach (Vector2 p in line.points) points.Add(scale.Apply(p));
            return new PatternGeometry.EdgeCurve(points, IsClosed(line));
        }

        public static bool IsClosed(PatternLine line) => line != null && (line.closed || line.kind == PatternLineKind.Hole) && line.points.Count >= 3;

        /// <summary>The line's length (cm, as drawn).</summary>
        public static float Length(PatternLine line) => Curve(line, PieceScale.Identity).Length;

        /// <summary>The length of an edge of a piece, or of a line inside it, by id (cm, as drawn). 0 when neither exists.</summary>
        public static float LengthOf(PatternPiece piece, string edgeOrLineId)
        {
            int edge = piece.IndexOfEdge(edgeOrLineId);
            if (edge >= 0) return PatternGeometry.EdgeLength(piece, edge, PieceScale.Identity);
            PatternLine line = piece.FindLine(edgeOrLineId);
            return line != null ? Length(line) : 0f;
        }

        /// <summary>The curve of an edge of a piece, or of a line inside it, by id. Null when neither exists.</summary>
        public static PatternGeometry.EdgeCurve CurveOf(PatternPiece piece, string edgeOrLineId, PieceScale scale)
        {
            int edge = piece.IndexOfEdge(edgeOrLineId);
            if (edge >= 0) return new PatternGeometry.EdgeCurve(piece, edge, scale);
            PatternLine line = piece.FindLine(edgeOrLineId);
            return line != null && line.points.Count >= 2 ? Curve(line, scale) : null;
        }

        public static bool Inside(PatternPiece piece, Vector2 point)
        {
            if (piece == null || piece.points.Count < 3) return false;
            return PatternGeometry.PointInPolygon(PatternGeometry.SampleOutline(piece, 0.5f, null), point);
        }

        /// <summary>The edge and the fraction along it nearest a point, when the point is within <paramref name="tolerance"/> of the outline.</summary>
        public static bool OnOutline(PatternPiece piece, Vector2 point, float tolerance, out int edge, out float fraction)
        {
            edge = -1; fraction = 0f;
            float best = tolerance;
            for (int e = 0; e < piece.points.Count; e++)
            {
                var curve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
                float f = curve.Nearest(point, out float d);
                if (d <= best) { best = d; edge = e; fraction = f; }
            }
            return edge >= 0;
        }

        /// <summary>
        /// The distance between the two points opposite a crease when the cloth is folded by
        /// <paramref name="foldDegrees"/> along it (0 flat): the rest length of the bend across the crease.
        /// All points are on the flat pattern.
        /// </summary>
        public static float FoldedDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float foldDegrees)
        {
            Vector2 along = b - a;
            float length = along.magnitude;
            if (length < 1e-6f) return (c - d).magnitude;
            along /= length;
            Vector2 across = new Vector2(-along.y, along.x);
            float tc = Vector2.Dot(c - a, along), td = Vector2.Dot(d - a, along);
            float hc = Mathf.Abs(Vector2.Dot(c - a, across)), hd = Mathf.Abs(Vector2.Dot(d - a, across));
            float cos = Mathf.Cos(Mathf.Clamp(foldDegrees, 0f, 180f) * Mathf.Deg2Rad);
            return Mathf.Sqrt(Mathf.Max(0f, (tc - td) * (tc - td) + hc * hc + hd * hd + 2f * hc * hd * cos));
        }

        /// <summary>What keeps a piece's lines from being made, in words for the artist.</summary>
        public static List<string> FindProblems(PatternPiece piece)
        {
            var problems = new List<string>();
            if (piece.lines == null || piece.lines.Count == 0 || piece.points.Count < 3) return problems;
            List<Vector2> outline = PatternGeometry.SampleOutline(piece, 0.5f, null);
            for (int i = 0; i < piece.lines.Count; i++)
            {
                PatternLine line = piece.lines[i];
                string what = "On '" + piece.name + "', " + Describe(line, i);
                int least = IsClosed(line) || line.kind == PatternLineKind.Hole ? 3 : 2;
                if (line.points.Count < least) { problems.Add(what + " needs at least " + least + " points."); continue; }
                bool outside = false, short_ = false;
                for (int k = 0; k < line.points.Count; k++)
                {
                    Vector2 p = line.points[k];
                    bool end = !IsClosed(line) && (k == 0 || k == line.points.Count - 1);
                    bool onOutline = PatternGeometry.DistanceToPolyline(outline, p) <= SnapCm;
                    if (!PatternGeometry.PointInPolygon(outline, p) && !(end && onOutline)) outside = true;
                    // A point in the middle of a line, or any point of a hole, has to stay clear of the outline.
                    if (!end && PatternGeometry.DistanceToPolyline(outline, p) < 0.5f) outside = true;
                    if (k > 0 && (p - line.points[k - 1]).magnitude < 0.2f) short_ = true;
                }
                if (outside) problems.Add(what + " leaves the piece or runs along its edge. Only the two ends of a line may touch the outline.");
                if (short_) problems.Add(what + " has two points on top of each other.");
                if (line.kind == PatternLineKind.Hole && Mathf.Abs(PatternGeometry.SignedArea(line.points)) < 0.5f) problems.Add(what + " has no area.");
                if (SelfCrosses(line)) problems.Add(what + " crosses itself.");
                for (int j = 0; j < i; j++)
                {
                    if (Cross(line, piece.lines[j])) problems.Add(what + " crosses " + Describe(piece.lines[j], j) + ". Lines inside a piece may meet only at their ends.");
                }
            }
            return problems;
        }

        public static string Describe(PatternLine line, int index)
        {
            if (!string.IsNullOrEmpty(line.name)) return "'" + line.name + "'";
            return (line.kind == PatternLineKind.Hole ? "hole " : line.kind == PatternLineKind.Fold ? "fold " : "line ") + (index + 1);
        }

        private static int SegmentCount(PatternLine line) => IsClosed(line) ? line.points.Count : line.points.Count - 1;

        private static bool SelfCrosses(PatternLine line)
        {
            int n = SegmentCount(line), count = line.points.Count;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 2; j < n; j++)
                {
                    if (IsClosed(line) && i == 0 && j == n - 1) continue;
                    if (SegmentsCross(line.points[i], line.points[(i + 1) % count], line.points[j], line.points[(j + 1) % count])) return true;
                }
            }
            return false;
        }

        private static bool Cross(PatternLine x, PatternLine y)
        {
            int nx = SegmentCount(x), ny = SegmentCount(y);
            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < ny; j++)
                {
                    if (SegmentsCross(x.points[i], x.points[(i + 1) % x.points.Count], y.points[j], y.points[(j + 1) % y.points.Count])) return true;
                }
            }
            return false;
        }

        public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Side(c, d, a), d2 = Side(c, d, b), d3 = Side(a, b, c), d4 = Side(a, b, d);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        /// <summary>Where two segments cross, as a fraction along the first. False when they do not.</summary>
        public static bool SegmentIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float alongAb, out float alongCd)
        {
            alongAb = alongCd = 0f;
            Vector2 r = b - a, s = d - c;
            float denominator = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denominator) < 1e-9f) return false;
            Vector2 ac = c - a;
            alongAb = (ac.x * s.y - ac.y * s.x) / denominator;
            alongCd = (ac.x * r.y - ac.y * r.x) / denominator;
            return alongAb >= 0f && alongAb <= 1f && alongCd >= 0f && alongCd <= 1f;
        }

        private static float Side(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
