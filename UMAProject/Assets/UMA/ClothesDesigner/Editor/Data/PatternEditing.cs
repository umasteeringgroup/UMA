using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Edits on a pattern, shared by the canvas and the tests. Every edit keeps ids stable and keeps seams
    /// pointing at the right edges: splitting an edge moves a seam onto the half it covers, deleting an
    /// edge removes its seams.
    /// </summary>
    public static class PatternEditing
    {
        public static PatternPiece NewRectanglePiece(string name, Rect rect)
        {
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = name };
            Vector2[] corners = { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) };
            foreach (Vector2 c in corners)
            {
                piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = c });
                piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId() });
            }
            return piece;
        }

        /// <summary>Splits an edge at a fraction of its length. Returns the new point's index.</summary>
        public static int SplitEdge(ClothesPattern pattern, PatternPiece piece, int edgeIndex, float fraction)
        {
            fraction = Mathf.Clamp(fraction, 0.02f, 0.98f);
            var curve = new PatternGeometry.EdgeCurve(piece, edgeIndex, PieceScale.Identity);
            Vector2 at = curve.At(fraction);
            PatternEdge old = piece.edges[edgeIndex];
            var second = new PatternEdge { id = ClothesPattern.NewId(), name = old.name, opening = old.opening };
            if (old.curved)
            {
                // Keep the shape roughly: shorten each handle to its half and give the new point a tangent.
                Vector2 tangent = (curve.At(Mathf.Min(1f, fraction + 0.01f)) - curve.At(Mathf.Max(0f, fraction - 0.01f))).normalized;
                float firstLength = curve.Length * fraction / 3f, secondLength = curve.Length * (1f - fraction) / 3f;
                second.curved = true;
                second.handleA = tangent * secondLength;
                second.handleB = old.handleB * (1f - fraction);
                old.handleA *= fraction;
                old.handleB = -tangent * firstLength;
            }
            piece.points.Insert(edgeIndex + 1, new PatternPoint { id = ClothesPattern.NewId(), position = at });
            piece.edges.Insert(edgeIndex + 1, second);
            // A seam on the split edge: keep it on the part it covered.
            foreach (PatternSeam seam in pattern.seams)
            {
                RemapSpanAfterSplit(ref seam.a, piece.id, old.id, second.id, fraction);
                RemapSpanAfterSplit(ref seam.b, piece.id, old.id, second.id, fraction);
            }
            // Darts and pleats on the split edge stay where they are, on whichever half holds them.
            foreach (PatternDart dart in piece.darts)
            {
                if (dart.kind != DartKind.Edge || dart.edgeId != old.id) continue;
                if (dart.position >= fraction) { dart.edgeId = second.id; dart.position = (dart.position - fraction) / (1f - fraction); }
                else dart.position /= fraction;
            }
            foreach (PatternPleat pleat in piece.pleats)
            {
                if (pleat.edgeId != old.id) continue;
                if (pleat.position >= fraction) { pleat.edgeId = second.id; pleat.position = (pleat.position - fraction) / (1f - fraction); }
                else pleat.position /= fraction;
            }
            return edgeIndex + 1;
        }

        private static void RemapSpanAfterSplit(ref SeamSpan span, string pieceId, string firstId, string secondId, float fraction)
        {
            if (span.pieceId != pieceId || span.edgeId != firstId) return;
            if (span.start >= fraction)
            {
                span.edgeId = secondId;
                span.start = (span.start - fraction) / (1f - fraction);
                span.end = (span.end - fraction) / (1f - fraction);
            }
            else
            {
                span.end = Mathf.Min(span.end, fraction) / fraction;
                span.start /= fraction;
            }
        }

        /// <summary>Deletes a point, joining its two edges. Seams on the removed edge go too.</summary>
        public static bool DeletePoint(ClothesPattern pattern, PatternPiece piece, int pointIndex)
        {
            if (piece.points.Count <= 3) return false;
            PatternEdge removed = piece.edges[pointIndex];
            pattern.seams.RemoveAll(s => (s.a.pieceId == piece.id && s.a.edgeId == removed.id) || (s.b.pieceId == piece.id && s.b.edgeId == removed.id));
            piece.darts.RemoveAll(d => d.kind == DartKind.Edge && d.edgeId == removed.id);
            piece.pleats.RemoveAll(p => p.edgeId == removed.id);
            piece.points.RemoveAt(pointIndex);
            piece.edges.RemoveAt(pointIndex);
            return true;
        }

        /// <summary>Sets an edge's straight length by moving its end point along the edge's direction.</summary>
        public static void SetEdgeLength(PatternPiece piece, int edgeIndex, float lengthCm)
        {
            int n = piece.points.Count;
            Vector2 a = piece.points[edgeIndex].position, b = piece.points[(edgeIndex + 1) % n].position;
            Vector2 dir = (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : Vector2.right;
            piece.points[(edgeIndex + 1) % n].position = a + dir * Mathf.Max(0.1f, lengthCm);
        }

        public static void ToggleCurve(PatternPiece piece, int edgeIndex)
        {
            PatternEdge e = piece.edges[edgeIndex];
            if (e.curved) { e.curved = false; return; }
            int n = piece.points.Count;
            Vector2 a = piece.points[edgeIndex].position, b = piece.points[(edgeIndex + 1) % n].position;
            e.curved = true;
            e.handleA = (b - a) / 3f;
            e.handleB = (a - b) / 3f;
        }

        /// <summary>A mirrored copy of a piece (left-right), with new ids. Seams are not copied.</summary>
        public static PatternPiece MirrorCopy(PatternPiece source, string name)
        {
            var copy = new PatternPiece
            {
                id = ClothesPattern.NewId(),
                name = name,
                grainAngle = -source.grainAngle,
                mirrorOf = source.id,
                placement = source.placement.Clone(),
                fabric = source.fabric != null ? source.fabric.Clone() : new FabricSettings()
            };
            Rect bounds = source.Bounds();
            float axis = bounds.center.x;
            int n = source.points.Count;
            var edgeMap = new Dictionary<string, string>();
            // Mirroring reverses the winding; walk backwards so the outline stays counter-clockwise.
            for (int k = 0; k < n; k++)
            {
                int i = (n - k) % n;
                Vector2 p = source.points[i].position;
                copy.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = new Vector2(2f * axis - p.x, p.y) });
            }
            for (int k = 0; k < n; k++)
            {
                // New edge k runs from new point k to new point k+1: the old edge from point (n-k-1) to (n-k), reversed.
                int oldEdge = (n - k - 1 + n) % n;
                PatternEdge e = source.edges[oldEdge];
                string newId = ClothesPattern.NewId();
                edgeMap[e.id] = newId;
                copy.edges.Add(new PatternEdge
                {
                    id = newId,
                    name = e.name,
                    opening = e.opening,
                    curved = e.curved,
                    handleA = new Vector2(-e.handleB.x, e.handleB.y),
                    handleB = new Vector2(-e.handleA.x, e.handleA.y)
                });
            }
            // A mirrored edge runs the other way, so positions along it turn round.
            Vector2 Mirror(Vector2 p) => new Vector2(2f * axis - p.x, p.y);
            foreach (PatternDart d in source.darts)
            {
                var m = JsonUtility.FromJson<PatternDart>(JsonUtility.ToJson(d));
                m.id = ClothesPattern.NewId();
                m.tip = Mirror(d.tip);
                m.tip2 = Mirror(d.tip2);
                if (d.kind == DartKind.Edge)
                {
                    if (d.edgeId == null || !edgeMap.TryGetValue(d.edgeId, out string mapped)) continue;
                    m.edgeId = mapped;
                    m.position = 1f - d.position;
                }
                copy.darts.Add(m);
            }
            foreach (PatternPleat pl in source.pleats)
            {
                if (pl.edgeId == null || !edgeMap.TryGetValue(pl.edgeId, out string mapped)) continue;
                var m = JsonUtility.FromJson<PatternPleat>(JsonUtility.ToJson(pl));
                m.id = ClothesPattern.NewId();
                m.edgeId = mapped;
                m.position = 1f - pl.position;
                m.kind = pl.kind == PleatKind.KnifeLeft ? PleatKind.KnifeRight : pl.kind == PleatKind.KnifeRight ? PleatKind.KnifeLeft : pl.kind;
                copy.pleats.Add(m);
            }
            return copy;
        }

        /// <summary>A copy of a piece with new ids, moved sideways.</summary>
        public static PatternPiece Duplicate(PatternPiece source, string name, Vector2 offset)
        {
            var copy = JsonUtility.FromJson<PatternPiece>(JsonUtility.ToJson(source));
            copy.id = ClothesPattern.NewId();
            copy.name = name;
            foreach (PatternPoint p in copy.points) { p.id = ClothesPattern.NewId(); p.position += offset; }
            var edgeMap = new Dictionary<string, string>();
            foreach (PatternEdge e in copy.edges) { string newId = ClothesPattern.NewId(); edgeMap[e.id] = newId; e.id = newId; }
            foreach (PatternDart d in copy.darts)
            {
                d.id = ClothesPattern.NewId();
                d.tip += offset;
                d.tip2 += offset;
                if (d.edgeId != null && edgeMap.TryGetValue(d.edgeId, out string mapped)) d.edgeId = mapped;
            }
            foreach (PatternPleat pl in copy.pleats)
            {
                pl.id = ClothesPattern.NewId();
                if (pl.edgeId != null && edgeMap.TryGetValue(pl.edgeId, out string mapped)) pl.edgeId = mapped;
            }
            copy.uvIsland = new UvIsland();
            return copy;
        }

        /// <summary>Sews two whole edges. Returns the seam, or null when either is an opening or already sewn.</summary>
        public static PatternSeam Sew(ClothesPattern pattern, PatternPiece pieceA, int edgeA, PatternPiece pieceB, int edgeB, out string problem)
        {
            problem = null;
            PatternEdge ea = pieceA.edges[edgeA], eb = pieceB.edges[edgeB];
            if (pieceA == pieceB && edgeA == edgeB) { problem = "Choose two different edges."; return null; }
            if (ea.opening || eb.opening) { problem = "An opening cannot be sewn. Turn Opening off first."; return null; }
            if (pattern.FindSeamOn(pieceA.id, ea.id) != null || pattern.FindSeamOn(pieceB.id, eb.id) != null) { problem = "One of these edges is already sewn. Unsew it first."; return null; }
            // Pieces are drawn as seen from outside, so two pieces facing out meet edge-ends reversed.
            var seam = new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(pieceA.id, ea.id), b = new SeamSpan(pieceB.id, eb.id), reversed = true };
            pattern.seams.Add(seam);
            return seam;
        }

        /// <summary>The colour a seam is drawn in, the same on the Pattern Canvas and in the Scene view.</summary>
        public static Color SeamColor(int seamIndex)
        {
            return Color.HSVToRGB((Mathf.Max(0, seamIndex) * 0.17f + 0.45f) % 1f, 0.55f, 1f);
        }

        /// <summary>Length difference of a seam's two sides (cm), on the drawn pattern.</summary>
        public static float SeamMismatchCm(ClothesPattern pattern, PatternSeam seam)
        {
            PatternPiece a = pattern.FindPiece(seam.a.pieceId), b = pattern.FindPiece(seam.b.pieceId);
            if (a == null || b == null) return 0f;
            int ea = a.IndexOfEdge(seam.a.edgeId), eb = b.IndexOfEdge(seam.b.edgeId);
            if (ea < 0 || eb < 0) return 0f;
            // What is left of each side once darts and pleats are sewn.
            float la = DartGeometry.EffectiveLength(a, ea, seam.a.start, seam.a.end);
            float lb = DartGeometry.EffectiveLength(b, eb, seam.b.start, seam.b.end);
            return la - lb;
        }

        /// <summary>
        /// Adds an edge dart opening on an edge at a fraction of its length: 3 cm wide, its point 10 cm into
        /// the piece (less if the piece is smaller). Returns it.
        /// </summary>
        public static PatternDart AddEdgeDart(PatternPiece piece, int edgeIndex, float fraction)
        {
            var curve = new PatternGeometry.EdgeCurve(piece, edgeIndex, PieceScale.Identity);
            float width = Mathf.Min(3f, curve.Length * 0.4f);
            float half = width * 0.5f / Mathf.Max(0.01f, curve.Length);
            fraction = Mathf.Clamp(fraction, half + 0.02f, 1f - half - 0.02f);
            Vector2 at = curve.At(fraction);
            Vector2 tangent = (curve.At(Mathf.Min(1f, fraction + 0.01f)) - curve.At(Mathf.Max(0f, fraction - 0.01f))).normalized;
            Vector2 inward = new Vector2(-tangent.y, tangent.x);
            List<Vector2> outline = PatternGeometry.SampleOutline(piece, 0.5f, null);
            float depth = 10f;
            while (depth > 1f && (!PatternGeometry.PointInPolygon(outline, at + inward * depth) || PatternGeometry.DistanceToPolyline(outline, at + inward * depth) < 1.5f)) depth *= 0.8f;
            var dart = new PatternDart
            {
                id = ClothesPattern.NewId(),
                kind = DartKind.Edge,
                edgeId = piece.edges[edgeIndex].id,
                position = fraction,
                widthCm = width,
                tip = at + inward * depth
            };
            piece.darts.Add(dart);
            return dart;
        }

        /// <summary>Adds an upright double-pointed dart in the middle of a piece: 3 cm wide, 60% of the piece's height. Returns it.</summary>
        public static PatternDart AddDoubleDart(PatternPiece piece, Vector2? center = null)
        {
            Rect b = piece.Bounds();
            Vector2 c = center ?? b.center;
            float half = b.height * 0.3f;
            var dart = new PatternDart
            {
                id = ClothesPattern.NewId(),
                kind = DartKind.Double,
                widthCm = Mathf.Min(3f, b.width * 0.2f),
                tip = c + new Vector2(0f, half),
                tip2 = c - new Vector2(0f, half),
                widestAt = 0.5f,
                roundness = 0.5f
            };
            piece.darts.Add(dart);
            return dart;
        }

        /// <summary>Adds a pressed knife pleat on an edge at a fraction of its length, 2 cm deep. Returns it.</summary>
        public static PatternPleat AddPleat(PatternPiece piece, int edgeIndex, float fraction)
        {
            var pleat = new PatternPleat
            {
                id = ClothesPattern.NewId(),
                kind = PleatKind.KnifeRight,
                edgeId = piece.edges[edgeIndex].id,
                position = Mathf.Clamp(fraction, 0.1f, 0.9f),
                depthCm = 2f,
                pressed = true
            };
            piece.pleats.Add(pleat);
            return pleat;
        }

        public static void DeletePiece(ClothesPattern pattern, PatternPiece piece)
        {
            pattern.seams.RemoveAll(s => s.a.pieceId == piece.id || s.b.pieceId == piece.id);
            foreach (PatternDimension d in pattern.dimensions) d.pieceIds.Remove(piece.id);
            pattern.pieces.Remove(piece);
        }
    }
}
