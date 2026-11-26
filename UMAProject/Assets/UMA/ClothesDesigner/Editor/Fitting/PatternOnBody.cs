using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Pattern work on the character: darts and pleats put where the cloth is worn, and pieces drawn on the body.
    /// Points picked on the draped cloth are mapped back to the pieces as drawn (each cloth vertex knows where it is
    /// on its piece), so what is added is ordinary pattern data, edited on the Pattern Canvas like any other.
    /// </summary>
    public static class PatternOnBody
    {
        /// <summary>The edge of a piece nearest a point as drawn: its index, the fraction along it and the distance (cm).</summary>
        public static bool NearestEdge(PatternPiece piece, Vector2 drawn, out int edgeIndex, out float fraction, out float distanceCm)
        {
            edgeIndex = -1; fraction = 0f; distanceCm = float.MaxValue;
            if (piece == null || piece.points.Count < 3) return false;
            for (int e = 0; e < piece.edges.Count; e++)
            {
                var curve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
                const int steps = 64;
                for (int s = 0; s <= steps; s++)
                {
                    float f = s / (float)steps;
                    float d = Vector2.Distance(curve.At(f), drawn);
                    if (d < distanceCm) { distanceCm = d; edgeIndex = e; fraction = f; }
                }
            }
            if (edgeIndex < 0) return false;
            // Finer, round the best sample.
            var best = new PatternGeometry.EdgeCurve(piece, edgeIndex, PieceScale.Identity);
            float lo = Mathf.Max(0f, fraction - 1f / 64f), hi = Mathf.Min(1f, fraction + 1f / 64f);
            for (int s = 0; s <= 32; s++)
            {
                float f = Mathf.Lerp(lo, hi, s / 32f);
                float d = Vector2.Distance(best.At(f), drawn);
                if (d < distanceCm) { distanceCm = d; fraction = f; }
            }
            return true;
        }

        /// <summary>
        /// The piece to edit for a point picked on a piece: the piece itself, or for a linked mirror its original and
        /// the point mirrored onto it (the mirror then follows, as it follows every edit of the original).
        /// </summary>
        public static PatternPiece Original(ClothesPattern pattern, PatternPiece piece, Vector2 drawn, out Vector2 onOriginal)
        {
            onOriginal = drawn;
            PatternPiece source = piece != null && piece.mirrorLinked && !string.IsNullOrEmpty(piece.mirrorOf) ? pattern.FindPiece(piece.mirrorOf) : null;
            if (source == null || source == piece) return piece;
            Rect sb = source.Bounds(), cb = piece.Bounds();
            onOriginal = new Vector2(sb.center.x + cb.center.x - drawn.x, drawn.y - (cb.yMin - sb.yMin));
            return source;
        }

        /// <summary>
        /// Adds a dart on a piece: its point at <paramref name="tip"/> (as drawn), opening on the edge nearest
        /// <paramref name="toward"/>; with <paramref name="doublePointed"/>, a diamond dart from one to the other.
        /// Null, with the reason, when it does not fit.
        /// </summary>
        public static PatternDart AddDartAt(ClothesPattern pattern, int pieceIndex, Vector2 tip, Vector2 toward, bool doublePointed, out string message)
        {
            message = null;
            if (pattern == null || pieceIndex < 0 || pieceIndex >= pattern.pieces.Count) { message = "Press on the cloth."; return null; }
            PatternPiece picked = pattern.pieces[pieceIndex];
            PatternPiece piece = Original(pattern, picked, tip, out tip);
            Original(pattern, picked, toward, out toward);
            List<Vector2> outline = PatternGeometry.SampleOutline(piece, 0.5f, null);
            if (doublePointed)
            {
                if (Vector2.Distance(tip, toward) < 2f) { message = "Drag at least 2 cm along the cloth for a double-pointed dart: from one point to the other."; return null; }
                if (!PatternGeometry.PointInPolygon(outline, tip) || !PatternGeometry.PointInPolygon(outline, toward)) { message = "Both points of a double-pointed dart must be on '" + piece.name + "'."; return null; }
                PatternDart diamond = PatternEditing.AddDoubleDart(piece, (tip + toward) * 0.5f);
                diamond.tip = tip; diamond.tip2 = toward;
                message = "Added a double-pointed dart on '" + piece.name + "', " + Vector2.Distance(tip, toward).ToString("0") + " cm long. Set its width on the Pattern Canvas.";
                return diamond;
            }
            if (!NearestEdge(piece, toward, out int edge, out float fraction, out _)) { message = "'" + piece.name + "' has no edges."; return null; }
            PatternDart dart = PatternEditing.AddEdgeDart(piece, edge, fraction);
            // A press without a drag keeps the dart's own point; a drag puts the point where it started.
            if (Vector2.Distance(tip, toward) >= 1f && PatternGeometry.PointInPolygon(outline, tip) && PatternGeometry.DistanceToPolyline(outline, tip) >= 1f) dart.tip = tip;
            message = "Added a dart on '" + piece.name + "', opening on " + EdgeName(piece, edge) + ". Its width is 3 cm; change it on the Pattern Canvas.";
            return dart;
        }

        /// <summary>Adds a knife pleat on the edge of a piece nearest a point (as drawn). Null, with the reason, when none fits.</summary>
        public static PatternPleat AddPleatAt(ClothesPattern pattern, int pieceIndex, Vector2 at, out string message)
        {
            message = null;
            if (pattern == null || pieceIndex < 0 || pieceIndex >= pattern.pieces.Count) { message = "Click on the cloth."; return null; }
            PatternPiece piece = Original(pattern, pattern.pieces[pieceIndex], at, out at);
            if (!NearestEdge(piece, at, out int edge, out float fraction, out _)) { message = "'" + piece.name + "' has no edges."; return null; }
            PatternPleat pleat = PatternEditing.AddPleat(piece, edge, fraction);
            message = "Added a 2 cm knife pleat on '" + piece.name + "', folding from " + EdgeName(piece, edge) + ". Change its kind and depth on the Pattern Canvas.";
            return pleat;
        }

        private static string EdgeName(PatternPiece piece, int edge) =>
            !string.IsNullOrEmpty(piece.edges[edge].name) ? "'" + piece.edges[edge].name + "'" : "edge " + (edge + 1);

        // The arrangement's torso: angle 0 faces the front (+z), 90 the body's left (-x).
        private static readonly Vector3 Front = Vector3.forward, BodyLeft = Vector3.left;

        /// <summary>
        /// Unrolls points on the torso (renderer space, metres) onto a pattern outline (cm, y up), on a cylinder round
        /// the torso's centre <paramref name="outFromBody"/> further out than the points: across the body by arc length,
        /// up by height. <paramref name="angleDeg"/> and <paramref name="topAboveShoulderCm"/> are the placement that
        /// lays the piece where it was drawn.
        /// </summary>
        public static List<Vector2> UnrollOnTorso(IList<Vector3> points, BodyLandmarks marks, float outFromBody, out float angleDeg, out float topAboveShoulderCm)
        {
            var outline = new List<Vector2>();
            angleDeg = 0f; topAboveShoulderCm = 0f;
            if (points == null || points.Count < 3 || marks == null) return outline;
            var angles = new float[points.Count];
            float radius = 0f, top = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 d = points[i] - new Vector3(marks.torsoCenter.x, points[i].y, marks.torsoCenter.y);
                float a = Mathf.Atan2(Vector3.Dot(d, BodyLeft), Vector3.Dot(d, Front));
                // Keep the angles continuous round the back, where they would jump from 180 to -180 degrees.
                if (i > 0) while (a - angles[i - 1] > Mathf.PI) a -= 2f * Mathf.PI;
                if (i > 0) while (a - angles[i - 1] < -Mathf.PI) a += 2f * Mathf.PI;
                angles[i] = a;
                radius += new Vector2(d.x, d.z).magnitude;
                top = Mathf.Max(top, points[i].y);
            }
            radius = radius / points.Count + Mathf.Max(0f, outFromBody);
            float min = float.MaxValue, max = float.MinValue;
            foreach (float a in angles) { min = Mathf.Min(min, a); max = Mathf.Max(max, a); }
            float middle = (min + max) * 0.5f;
            for (int i = 0; i < points.Count; i++) outline.Add(new Vector2((angles[i] - middle) * radius * 100f, points[i].y * 100f));
            if (PatternGeometry.SignedArea(outline) < 0f) outline.Reverse();
            angleDeg = Mathf.Repeat(middle * Mathf.Rad2Deg + 180f, 360f) - 180f;
            topAboveShoulderCm = (top - marks.shoulderTopY) * 100f;
            return outline;
        }

        /// <summary>
        /// A new torso piece from an outline drawn on the body, placed where it was drawn and set beside the other pieces
        /// on the canvas. Null, with the reason, when the outline is too small or crosses itself.
        /// </summary>
        public static PatternPiece PieceFromBody(ClothesPattern pattern, IList<Vector3> points, BodyLandmarks marks, float outFromBody, out string message)
        {
            message = null;
            if (marks == null || !marks.valid) { message = "The body has not been measured yet."; return null; }
            List<Vector2> outline = UnrollOnTorso(points, marks, outFromBody, out float angle, out float top);
            if (outline.Count < 3) { message = "Click at least three points on the body, then click the first again (or press Enter)."; return null; }
            for (int i = 0; i < outline.Count; i++)
            {
                for (int j = i + 2; j < outline.Count; j++)
                {
                    if (i == 0 && j == outline.Count - 1) continue;
                    if (Cross(outline[i], outline[(i + 1) % outline.Count], outline[j], outline[(j + 1) % outline.Count])) { message = "The outline crosses itself: draw round the piece in one direction."; return null; }
                }
            }
            if (Mathf.Abs(PatternGeometry.SignedArea(outline)) < 4f) { message = "The outline is too small: draw round at least 2 x 2 cm."; return null; }
            // Beside the other pieces on the canvas: where a piece is drawn does not move it on the body.
            float right = 0f;
            bool any = false;
            foreach (PatternPiece p in pattern.pieces) if (p != null && p.points.Count > 0) { Rect b = p.Bounds(); right = any ? Mathf.Max(right, b.xMax) : b.xMax; any = true; }
            float minX = float.MaxValue;
            foreach (Vector2 q in outline) minX = Mathf.Min(minX, q.x);
            float shift = any ? right + 10f - minX : 0f;
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = "Drawn Piece " + (pattern.pieces.Count + 1) };
            foreach (Vector2 q in outline)
            {
                piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = new Vector2(q.x + shift, q.y) });
                piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId() });
            }
            piece.placement.part = BodyPart.Torso;
            piece.placement.angle = Mathf.Round(angle);
            piece.placement.topAboveShoulderCm = Mathf.Round(top * 10f) / 10f;
            Rect bounds = piece.Bounds();
            message = "Added '" + piece.name + "', " + bounds.width.ToString("0") + " x " + bounds.height.ToString("0") + " cm, facing " + piece.placement.angle.ToString("0") + " degrees round the body. Sew it and shape its edges on the Pattern Canvas.";
            return piece;
        }

        private static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Side(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            float d1 = Side(c, d, a), d2 = Side(c, d, b), d3 = Side(a, b, c), d4 = Side(a, b, d);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }
    }
}
