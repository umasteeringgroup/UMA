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
            // A seam on the split edge: a seam across the split becomes two, one on each half, sharing out
            // the other side in proportion (it used to keep only the first half, leaving the rest unsewn).
            foreach (PatternSeam seam in pattern.seams.ToArray())
            {
                SplitSeamAt(pattern, seam, piece.id, old.id, fraction);
            }
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

        /// <summary>
        /// When one side of a seam runs across <paramref name="fraction"/> of an edge, cuts the seam there in
        /// two: this seam keeps the part before, a new seam takes the part after, and the other side is
        /// shared out in the same proportion (from its end when the seam is reversed).
        /// </summary>
        private static void SplitSeamAt(ClothesPattern pattern, PatternSeam seam, string pieceId, string edgeId, float fraction)
        {
            bool onA = seam.a.pieceId == pieceId && seam.a.edgeId == edgeId;
            bool onB = !onA && seam.b.pieceId == pieceId && seam.b.edgeId == edgeId;
            if (!onA && !onB) return;
            SeamSpan here = onA ? seam.a : seam.b, there = onA ? seam.b : seam.a;
            if (fraction <= here.start + 1e-4f || fraction >= here.end - 1e-4f) return;
            float u = (fraction - here.start) / (here.end - here.start);
            float cut = seam.reversed ? Mathf.Lerp(there.end, there.start, u) : Mathf.Lerp(there.start, there.end, u);
            SeamSpan hereFirst = here, hereSecond = here, thereFirst = there, thereSecond = there;
            hereFirst.end = fraction; hereSecond.start = fraction;
            if (seam.reversed) { thereFirst.start = cut; thereSecond.end = cut; }
            else { thereFirst.end = cut; thereSecond.start = cut; }
            var second = new PatternSeam
            {
                id = ClothesPattern.NewId(),
                a = onA ? hereSecond : thereSecond,
                b = onA ? thereSecond : hereSecond,
                reversed = seam.reversed,
                folded = seam.folded,
                topstitch = seam.topstitch,
                topstitchOffsetMm = seam.topstitchOffsetMm
            };
            if (onA) { seam.a = hereFirst; seam.b = thereFirst; } else { seam.b = hereFirst; seam.a = thereFirst; }
            pattern.seams.Insert(pattern.seams.IndexOf(seam) + 1, second);
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
            foreach (PatternLine line in source.lines)
            {
                var m = JsonUtility.FromJson<PatternLine>(JsonUtility.ToJson(line));
                m.id = ClothesPattern.NewId();
                m.mirrorOfLine = line.id;
                for (int i = 0; i < m.points.Count; i++) m.points[i] = Mirror(m.points[i]);
                copy.lines.Add(m);
            }
            copy.meshDetail = source.meshDetail;
            copy.interfacing = source.interfacing;
            copy.layer = source.layer;
            copy.pressure = source.pressure;
            foreach (KeyValuePair<string, string> kv in edgeMap) copy.edges[copy.IndexOfEdge(kv.Value)].mirrorOfEdge = kv.Key;
            foreach (PatternEdge e in source.edges)
            {
                PatternEdge m = copy.edges[copy.IndexOfEdge(edgeMap[e.id])];
                m.elastic = e.elastic; m.elasticPercent = e.elasticPercent;
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
            foreach (PatternLine line in copy.lines)
            {
                line.id = ClothesPattern.NewId();
                for (int i = 0; i < line.points.Count; i++) line.points[i] += offset;
            }
            copy.mirrorOf = null;
            copy.mirrorLinked = false;
            copy.uvIsland = new UvIsland();
            return copy;
        }

        /// <summary>
        /// Sews two edges: all of each, or of an edge that is already sewn along part of its length, the
        /// longest part still free. Returns the seam, or null when either is an opening or sewn all along.
        /// </summary>
        public static PatternSeam Sew(ClothesPattern pattern, PatternPiece pieceA, int edgeA, PatternPiece pieceB, int edgeB, out string problem)
        {
            problem = null;
            PatternEdge ea = pieceA.edges[edgeA], eb = pieceB.edges[edgeB];
            if (pieceA == pieceB && edgeA == edgeB) { problem = "Choose two different edges."; return null; }
            if (ea.opening || eb.opening) { problem = "An opening cannot be sewn. Turn Opening off first."; return null; }
            if (!pattern.FreeSpan(pieceA.id, ea.id, out float a0, out float a1) || !pattern.FreeSpan(pieceB.id, eb.id, out float b0, out float b1))
            {
                problem = "One of these edges is already sewn all along. Unsew it, or shorten its seam, first.";
                return null;
            }
            // Pieces are drawn as seen from outside, so two pieces facing out meet edge-ends reversed.
            var seam = new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(pieceA.id, ea.id, a0, a1), b = new SeamSpan(pieceB.id, eb.id, b0, b1), reversed = true };
            pattern.seams.Add(seam);
            return seam;
        }

        /// <summary>
        /// Sews one edge to several others in a row: the first edge is shared out between them by their
        /// lengths, in the order given (from its start, or from its end when <paramref name="reversed"/>,
        /// which is how pieces facing out meet). Returns the seams made.
        /// </summary>
        public static List<PatternSeam> SewOneToMany(ClothesPattern pattern, PatternPiece pieceA, int edgeA, IList<(PatternPiece piece, int edge)> others, bool reversed, out string problem)
        {
            problem = null;
            var made = new List<PatternSeam>();
            PatternEdge ea = pieceA.edges[edgeA];
            if (ea.opening) { problem = "An opening cannot be sewn. Turn Opening off first."; return made; }
            if (others == null || others.Count == 0) { problem = "Choose the edges to sew it to."; return made; }
            if (!pattern.FreeSpan(pieceA.id, ea.id, out float a0, out float a1)) { problem = "The first edge is already sewn all along."; return made; }
            var spans = new List<(PatternPiece piece, PatternEdge edge, float start, float end, float length)>();
            float total = 0f;
            foreach ((PatternPiece piece, int edge) in others)
            {
                PatternEdge e = piece.edges[edge];
                if (piece == pieceA && edge == edgeA) { problem = "An edge cannot be sewn to itself."; return made; }
                if (e.opening) { problem = "An opening cannot be sewn. Turn Opening off first."; return made; }
                if (!pattern.FreeSpan(piece.id, e.id, out float s, out float t)) { problem = "'" + piece.name + "' has an edge that is already sewn all along."; return made; }
                float length = DartGeometry.EffectiveLength(piece, edge, s, t);
                spans.Add((piece, e, s, t, length));
                total += length;
            }
            if (total <= 0f) { problem = "Those edges have no length."; return made; }
            float at = 0f;
            foreach (var span in spans)
            {
                float u0 = at / total, u1 = (at + span.length) / total;
                at += span.length;
                // Along the first edge from its end when the pieces face out, so the edges follow each other round.
                float f0 = reversed ? Mathf.Lerp(a1, a0, u1) : Mathf.Lerp(a0, a1, u0);
                float f1 = reversed ? Mathf.Lerp(a1, a0, u0) : Mathf.Lerp(a0, a1, u1);
                var seam = new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(pieceA.id, ea.id, f0, f1), b = new SeamSpan(span.piece.id, span.edge.id, span.start, span.end), reversed = reversed };
                pattern.seams.Add(seam);
                made.Add(seam);
            }
            return made;
        }

        /// <summary>
        /// Sews an edge onto a line drawn inside a piece (a pocket onto its panel). Both run the same way
        /// when the pieces lie on each other facing out, so the seam is not reversed.
        /// </summary>
        public static PatternSeam SewToLine(ClothesPattern pattern, PatternPiece pieceA, int edgeA, PatternPiece pieceB, PatternLine line, out string problem)
        {
            problem = null;
            PatternEdge ea = pieceA.edges[edgeA];
            if (ea.opening) { problem = "An opening cannot be sewn. Turn Opening off first."; return null; }
            if (line.points.Count < 2) { problem = "That line has no length."; return null; }
            if (!pattern.FreeSpan(pieceA.id, ea.id, out float a0, out float a1) || !pattern.FreeSpan(pieceB.id, line.id, out float b0, out float b1))
            {
                problem = "The edge or the line is already sewn all along. Unsew it first.";
                return null;
            }
            var seam = new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(pieceA.id, ea.id, a0, a1), b = new SeamSpan(pieceB.id, line.id, b0, b1), reversed = false };
            pattern.seams.Add(seam);
            return seam;
        }

        /// <summary>A straight line inside a piece, from one point to another (cm, as drawn).</summary>
        public static PatternLine AddLine(PatternPiece piece, PatternLineKind kind, Vector2 from, Vector2 to)
        {
            var line = new PatternLine { id = ClothesPattern.NewId(), kind = kind == PatternLineKind.Hole ? PatternLineKind.Seam : kind };
            line.points.Add(from);
            line.points.Add(to);
            piece.lines.Add(line);
            return line;
        }

        /// <summary>A closed shape inside a piece (counter-clockwise): a rectangle, or an oval when <paramref name="sides"/> is more than 4.</summary>
        public static PatternLine AddShape(PatternPiece piece, PatternLineKind kind, Rect rect, int sides = 4)
        {
            var line = new PatternLine { id = ClothesPattern.NewId(), kind = kind, closed = true };
            if (sides <= 4)
            {
                line.points.Add(new Vector2(rect.xMin, rect.yMin));
                line.points.Add(new Vector2(rect.xMax, rect.yMin));
                line.points.Add(new Vector2(rect.xMax, rect.yMax));
                line.points.Add(new Vector2(rect.xMin, rect.yMax));
            }
            else
            {
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    line.points.Add(rect.center + new Vector2(Mathf.Cos(a) * rect.width * 0.5f, Mathf.Sin(a) * rect.height * 0.5f));
                }
            }
            piece.lines.Add(line);
            return line;
        }

        /// <summary>Removes a line from a piece, with the seams sewn to it.</summary>
        public static void DeleteLine(ClothesPattern pattern, PatternPiece piece, PatternLine line)
        {
            pattern.seams.RemoveAll(s => (s.a.pieceId == piece.id && s.a.edgeId == line.id) || (s.b.pieceId == piece.id && s.b.edgeId == line.id));
            piece.lines.Remove(line);
        }

        /// <summary>A single stitch between a point on one piece and a point on another (or the same): a tack, or a button.</summary>
        public static PatternTack AddTack(ClothesPattern pattern, PatternPiece pieceA, Vector2 a, PatternPiece pieceB, Vector2 b, bool button = false)
        {
            var tack = new PatternTack { id = ClothesPattern.NewId(), pieceA = pieceA.id, a = a, pieceB = pieceB.id, b = b, button = button };
            pattern.tacks.Add(tack);
            return tack;
        }

        /// <summary>
        /// Sews a piece onto another like a patch pocket: for every edge of the patch that is neither an
        /// opening nor sewn yet, a line is drawn on the target where that edge lies when the patch's
        /// middle is at <paramref name="at"/> (the target's cm, as drawn), and the edge is sewn to it. The
        /// patch is laid out over the same spot, a little farther from the body, a layer above the target.
        /// Returns the seams made.
        /// </summary>
        public static List<PatternSeam> AttachPiece(ClothesPattern pattern, PatternPiece patch, PatternPiece target, Vector2 at, out string problem)
        {
            problem = null;
            var made = new List<PatternSeam>();
            if (patch == target) { problem = "Choose another piece to sew this one onto."; return made; }
            Rect patchBounds = patch.Bounds(), targetBounds = target.Bounds();
            Vector2 offset = at - patchBounds.center;
            List<Vector2> outline = PatternGeometry.SampleOutline(target, 0.5f, null);
            for (int e = 0; e < patch.edges.Count; e++)
            {
                var curve = new PatternGeometry.EdgeCurve(patch, e, PieceScale.Identity);
                for (int k = 0; k <= 4; k++)
                {
                    Vector2 q = curve.At(k / 4f) + offset;
                    if (!PatternGeometry.PointInPolygon(outline, q) || PatternGeometry.DistanceToPolyline(outline, q) < 0.5f)
                    {
                        problem = "'" + patch.name + "' does not fit inside '" + target.name + "' there. Move it in from the edge.";
                        return made;
                    }
                }
            }
            for (int e = 0; e < patch.edges.Count; e++)
            {
                PatternEdge edge = patch.edges[e];
                if (edge.opening || pattern.FindSeamOn(patch.id, edge.id) != null) continue;
                var curve = new PatternGeometry.EdgeCurve(patch, e, PieceScale.Identity);
                var line = new PatternLine { id = ClothesPattern.NewId(), kind = PatternLineKind.Seam, name = patch.name + (string.IsNullOrEmpty(edge.name) ? " " + (e + 1) : " " + edge.name) };
                int steps = edge.curved ? Mathf.Clamp(Mathf.CeilToInt(curve.Length / 2f), 4, 16) : 1;
                for (int k = 0; k <= steps; k++) line.points.Add(curve.At(k / (float)steps) + offset);
                target.lines.Add(line);
                var seam = new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(patch.id, edge.id), b = new SeamSpan(target.id, line.id), reversed = false };
                pattern.seams.Add(seam);
                made.Add(seam);
            }
            if (made.Count == 0) { problem = "'" + patch.name + "' has no free edge to sew on. Its openings and sewn edges are left as they are."; return made; }
            // Laid out over the same spot of the target, a little farther out.
            patch.placement = target.placement.Clone();
            patch.placement.acrossCm = target.placement.acrossCm + (at.x - targetBounds.center.x);
            patch.placement.topAboveShoulderCm = target.placement.topAboveShoulderCm - (targetBounds.yMax - (at.y + patchBounds.height * 0.5f));
            patch.placement.distanceCm = target.placement.distanceCm + 1.5f;
            patch.layer = target.layer + 1;
            return made;
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
            return SpanLengthCm(a, seam.a) - SpanLengthCm(b, seam.b);
        }

        /// <summary>The length of a seam's side (cm, as drawn): what is left of an edge once darts and pleats are sewn, or the stretch of a line.</summary>
        public static float SpanLengthCm(PatternPiece piece, SeamSpan span)
        {
            int edge = piece.IndexOfEdge(span.edgeId);
            if (edge >= 0) return DartGeometry.EffectiveLength(piece, edge, span.start, span.end);
            PatternLine line = piece.FindLine(span.edgeId);
            return line != null ? LineGeometry.Length(line) * Mathf.Max(0f, span.end - span.start) : 0f;
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

        // ---- Cutting, merging and linked mirrors ----------------------------------------------------

        /// <summary>
        /// Cuts a piece in two along the straight line through <paramref name="from"/> and <paramref name="to"/>
        /// (cm, as drawn), which must cross its outline exactly twice. Seams, darts, pleats, lines and tacks
        /// go with the half they are on; the new half is laid out where it was on the body, keeps the same
        /// UVs (painted textures still line up) and, with <paramref name="sewTogether"/>, is sewn back along
        /// the cut. Returns the new half, or null with a reason.
        /// </summary>
        public static PatternPiece CutPiece(ClothesPattern pattern, PatternPiece piece, Vector2 from, Vector2 to, bool sewTogether, out string problem)
        {
            problem = null;
            Vector2 dir = to - from;
            if (dir.sqrMagnitude < 0.01f) { problem = "Draw the cut longer."; return null; }
            // Every crossing of the (endless) line with the outline: edge and fraction.
            var crossings = new List<(int edge, float fraction, Vector2 at)>();
            Vector2 far0 = from - dir.normalized * 10000f, far1 = from + dir.normalized * 10000f;
            for (int e = 0; e < piece.points.Count; e++)
            {
                var curve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
                const int steps = 96;
                Vector2 previous = curve.At(0f);
                for (int k = 1; k <= steps; k++)
                {
                    Vector2 next = curve.At(k / (float)steps);
                    if (LineGeometry.SegmentIntersection(previous, next, far0, far1, out float along, out _) && (k < steps || along < 0.999f))
                    {
                        float fraction = (k - 1 + along) / steps;
                        crossings.Add((e, fraction, curve.At(fraction)));
                    }
                    previous = next;
                }
            }
            // A crossing at a corner is found on both edges that meet there.
            for (int i = crossings.Count - 1; i > 0; i--) for (int j = 0; j < i; j++) if ((crossings[i].at - crossings[j].at).sqrMagnitude < 0.0025f) { crossings.RemoveAt(i); break; }
            if (crossings.Count != 2) { problem = "A cut must cross the piece's outline exactly twice; this one crosses it " + crossings.Count + " times."; return null; }

            // Points at the crossings (at a corner, the corner itself); the later edge first, so indices hold.
            crossings.Sort((x, y) => y.edge != x.edge ? y.edge.CompareTo(x.edge) : y.fraction.CompareTo(x.fraction));
            var cutPointIds = new List<string>();
            int splitEdge = -1;
            float splitAt = 1f;
            foreach (var c in crossings)
            {
                // Twice on one edge: after the later split the edge's first part is what is left of it.
                float fraction = c.edge == splitEdge ? c.fraction / splitAt : c.fraction;
                int index;
                if (fraction < 0.01f) index = c.edge;
                else if (fraction > 0.99f) index = (c.edge + 1) % piece.points.Count;
                else { index = SplitEdge(pattern, piece, c.edge, fraction); splitEdge = c.edge; splitAt = c.fraction; }
                cutPointIds.Add(piece.points[index].id);
            }
            int i1 = piece.IndexOfPoint(cutPointIds[0]), i2 = piece.IndexOfPoint(cutPointIds[1]);
            if (i1 > i2) { int t = i1; i1 = i2; i2 = t; }
            if (i2 - i1 < 1 || i1 + piece.points.Count - i2 < 1 || i2 - i1 == piece.points.Count) { problem = "The cut runs along the outline."; return null; }
            Rect before = piece.Bounds();
            int n = piece.points.Count;

            // The new half: points i2 .. i1 (round the end), its edges, then the cut from i1 back to i2.
            var half = new PatternPiece
            {
                id = ClothesPattern.NewId(),
                name = piece.name + " (cut)",
                grainAngle = piece.grainAngle,
                placement = piece.placement.Clone(),
                fabric = piece.fabric != null ? piece.fabric.Clone() : new FabricSettings(),
                meshDetail = piece.meshDetail,
                interfacing = piece.interfacing,
                layer = piece.layer,
                pressure = piece.pressure,
                uvIsland = JsonUtility.FromJson<UvIsland>(JsonUtility.ToJson(piece.uvIsland))
            };
            var keptPoints = new List<PatternPoint>();
            var keptEdges = new List<PatternEdge>();
            for (int i = i1; i <= i2; i++) { keptPoints.Add(piece.points[i]); if (i < i2) keptEdges.Add(piece.edges[i]); }
            var movedEdgeIds = new HashSet<string>();
            for (int k = 0; k <= n - (i2 - i1); k++)
            {
                int i = (i2 + k) % n;
                half.points.Add(k == 0 || k == n - (i2 - i1) ? new PatternPoint { id = ClothesPattern.NewId(), position = piece.points[i].position } : piece.points[i]);
                if (k < n - (i2 - i1)) { half.edges.Add(piece.edges[i]); movedEdgeIds.Add(piece.edges[i].id); }
            }
            var cutOnHalf = new PatternEdge { id = ClothesPattern.NewId(), name = "Cut" };
            half.edges.Add(cutOnHalf);
            var cutOnPiece = new PatternEdge { id = ClothesPattern.NewId(), name = "Cut" };
            keptEdges.Add(cutOnPiece);
            piece.points = keptPoints;
            piece.edges = keptEdges;

            // What moves with the new half.
            List<Vector2> halfOutline = PatternGeometry.SampleOutline(half, 0.5f, null);
            bool OnHalf(Vector2 p) => PatternGeometry.PointInPolygon(halfOutline, p);
            foreach (PatternSeam seam in pattern.seams)
            {
                if (seam.a.pieceId == piece.id && movedEdgeIds.Contains(seam.a.edgeId)) seam.a.pieceId = half.id;
                if (seam.b.pieceId == piece.id && movedEdgeIds.Contains(seam.b.edgeId)) seam.b.pieceId = half.id;
            }
            for (int d = piece.darts.Count - 1; d >= 0; d--)
            {
                PatternDart dart = piece.darts[d];
                bool moves = dart.kind == DartKind.Edge ? movedEdgeIds.Contains(dart.edgeId) : OnHalf((dart.tip + dart.tip2) * 0.5f);
                if (moves) { half.darts.Add(dart); piece.darts.RemoveAt(d); }
            }
            for (int d = piece.pleats.Count - 1; d >= 0; d--)
            {
                if (movedEdgeIds.Contains(piece.pleats[d].edgeId)) { half.pleats.Add(piece.pleats[d]); piece.pleats.RemoveAt(d); }
            }
            for (int l = piece.lines.Count - 1; l >= 0; l--)
            {
                PatternLine line = piece.lines[l];
                Vector2 mid = Vector2.zero;
                foreach (Vector2 p in line.points) mid += p;
                if (line.points.Count > 0 && OnHalf(mid / line.points.Count))
                {
                    half.lines.Add(line);
                    piece.lines.RemoveAt(l);
                    foreach (PatternSeam seam in pattern.seams)
                    {
                        if (seam.a.pieceId == piece.id && seam.a.edgeId == line.id) seam.a.pieceId = half.id;
                        if (seam.b.pieceId == piece.id && seam.b.edgeId == line.id) seam.b.pieceId = half.id;
                    }
                }
            }
            foreach (PatternTack tack in pattern.tacks)
            {
                if (tack.pieceA == piece.id && OnHalf(tack.a)) tack.pieceA = half.id;
                if (tack.pieceB == piece.id && OnHalf(tack.b)) tack.pieceB = half.id;
            }
            foreach (PatternPart part in pattern.parts) if (part.pieceId == piece.id && OnHalf(part.position)) part.pieceId = half.id;
            foreach (PatternDimension dim in pattern.dimensions) if (dim.pieceIds.Contains(piece.id)) dim.pieceIds.Add(half.id);

            // Each half keeps the original's placement: pieces with the same placement that touch on the
            // canvas are laid out as one (Arrangement), so the halves start where the whole piece did.

            pattern.pieces.Insert(pattern.pieces.IndexOf(piece) + 1, half);
            if (sewTogether)
            {
                // The two cut edges run opposite ways along the same line: the start of one meets the end of the other.
                pattern.seams.Add(new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(piece.id, cutOnPiece.id), b = new SeamSpan(half.id, cutOnHalf.id), reversed = true });
            }
            return half;
        }

        /// <summary>
        /// Joins two pieces sewn edge to edge along a whole seam into one: the second is turned and moved so
        /// its edge lies on the first's, and the seam disappears. Its other seams, darts, pleats, lines and
        /// tacks come with it. Only an ordinary seam (not folded, end meeting end) can be merged.
        /// </summary>
        public static PatternPiece MergePieces(ClothesPattern pattern, PatternSeam seam, out string problem)
        {
            problem = null;
            PatternPiece a = pattern.FindPiece(seam.a.pieceId), b = pattern.FindPiece(seam.b.pieceId);
            if (a == null || b == null || a == b) { problem = "This seam does not join two pieces."; return null; }
            int ea = a.IndexOfEdge(seam.a.edgeId), eb = b.IndexOfEdge(seam.b.edgeId);
            if (ea < 0 || eb < 0) { problem = "Only a seam between two edges can be merged."; return null; }
            if (seam.folded || !seam.reversed) { problem = "Only a seam that joins the pieces side by side (not turned over) can be merged."; return null; }
            if (seam.a.start > 0.001f || seam.a.end < 0.999f || seam.b.start > 0.001f || seam.b.end < 0.999f) { problem = "Only a seam along both edges' whole length can be merged."; return null; }
            if (Mathf.Abs(SeamMismatchCm(pattern, seam)) > 1f) { problem = "The two sides differ in length by more than 1 cm; they cannot lie flat as one piece."; return null; }
            int na = a.points.Count, nb = b.points.Count;
            // b's edge start meets a's edge end, and the other way round: a turn and a shift that lays one on the other.
            Vector2 a0 = a.points[ea].position, a1 = a.points[(ea + 1) % na].position;
            Vector2 b0 = b.points[eb].position, b1 = b.points[(eb + 1) % nb].position;
            float turn = Mathf.Atan2((a0 - a1).y, (a0 - a1).x) - Mathf.Atan2((b1 - b0).y, (b1 - b0).x);
            float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
            Vector2 Rotate(Vector2 v) => new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
            Vector2 shift = (a1 + a0) * 0.5f - Rotate((b0 + b1) * 0.5f);
            Vector2 Move(Vector2 p) => Rotate(p) + shift;

            var points = new List<PatternPoint>();
            var edges = new List<PatternEdge>();
            for (int k = 0; k < na; k++)
            {
                int i = (ea + 1 + k) % na;
                points.Add(a.points[i]);
                edges.Add(k < na - 1 ? a.edges[i] : null);
            }
            // The last point of a (its edge's start) carries on along b's edge after the seam.
            edges[na - 1] = b.edges[(eb + 1) % nb];
            for (int k = 2; k < nb; k++)
            {
                int i = (eb + k) % nb;
                points.Add(new PatternPoint { id = b.points[i].id, position = Move(b.points[i].position) });
                edges.Add(b.edges[i]);
            }
            var movedEdges = new HashSet<string>();
            foreach (PatternEdge e in b.edges)
            {
                if (e.id == seam.b.edgeId) continue;
                movedEdges.Add(e.id);
                e.handleA = Rotate(e.handleA);
                e.handleB = Rotate(e.handleB);
            }
            a.points = points;
            a.edges = edges;
            foreach (PatternDart d in b.darts) { d.tip = Move(d.tip); d.tip2 = Move(d.tip2); a.darts.Add(d); }
            a.pleats.AddRange(b.pleats);
            foreach (PatternLine l in b.lines) { for (int i = 0; i < l.points.Count; i++) l.points[i] = Move(l.points[i]); a.lines.Add(l); }
            pattern.seams.Remove(seam);
            foreach (PatternSeam s in pattern.seams)
            {
                if (s.a.pieceId == b.id) s.a.pieceId = a.id;
                if (s.b.pieceId == b.id) s.b.pieceId = a.id;
            }
            foreach (PatternTack t in pattern.tacks)
            {
                if (t.pieceA == b.id) { t.pieceA = a.id; t.a = Move(t.a); }
                if (t.pieceB == b.id) { t.pieceB = a.id; t.b = Move(t.b); }
            }
            foreach (PatternPart part in pattern.parts) if (part.pieceId == b.id) { part.pieceId = a.id; part.position = Move(part.position); }
            foreach (PatternDimension dim in pattern.dimensions) dim.pieceIds.Remove(b.id);
            foreach (PatternPiece other in pattern.pieces) if (other.mirrorOf == b.id) { other.mirrorOf = null; other.mirrorLinked = false; }
            pattern.pieces.Remove(b);
            if (PatternGeometry.OutlineSelfIntersects(a)) problem = "The merged outline crosses itself; check the piece on the canvas.";
            return a;
        }

        /// <summary>
        /// Makes every linked mirror (a piece mirrored with Mirror, linked) the mirror image of its
        /// original again, keeping where it lies on the canvas and its own placement, name and fabric.
        /// Edges and lines keep their ids, so seams on them stay; seams on edges the original no longer
        /// has are removed. Returns how many pieces changed.
        /// </summary>
        public static int UpdateLinkedMirrors(ClothesPattern pattern)
        {
            int changed = 0;
            foreach (PatternPiece copy in pattern.pieces.ToArray())
            {
                if (copy == null || !copy.mirrorLinked || string.IsNullOrEmpty(copy.mirrorOf)) continue;
                PatternPiece source = pattern.FindPiece(copy.mirrorOf);
                if (source == null || source == copy) { copy.mirrorLinked = false; continue; }
                PatternPiece fresh = MirrorCopy(source, copy.name);
                Rect sourceBounds = source.Bounds(), copyBounds = copy.Bounds();
                Vector2 offset = new Vector2(copyBounds.center.x - sourceBounds.center.x, copyBounds.yMin - sourceBounds.yMin);
                // Keep the ids of the copy's edges and lines that mirror the same edge or line of the original.
                var edgeIdFor = new Dictionary<string, string>();
                foreach (PatternEdge e in copy.edges) if (!string.IsNullOrEmpty(e.mirrorOfEdge)) edgeIdFor[e.mirrorOfEdge] = e.id;
                var lineIdFor = new Dictionary<string, string>();
                foreach (PatternLine l in copy.lines) if (!string.IsNullOrEmpty(l.mirrorOfLine)) lineIdFor[l.mirrorOfLine] = l.id;
                var renamed = new Dictionary<string, string>();
                foreach (PatternEdge e in fresh.edges)
                {
                    if (edgeIdFor.TryGetValue(e.mirrorOfEdge, out string keep)) { renamed[e.id] = keep; e.id = keep; }
                }
                foreach (PatternDart d in fresh.darts) if (d.edgeId != null && renamed.TryGetValue(d.edgeId, out string id)) d.edgeId = id;
                foreach (PatternPleat pl in fresh.pleats) if (pl.edgeId != null && renamed.TryGetValue(pl.edgeId, out string id)) pl.edgeId = id;
                foreach (PatternLine l in fresh.lines) if (lineIdFor.TryGetValue(l.mirrorOfLine, out string keep)) l.id = keep;
                foreach (PatternPoint p in fresh.points) p.position += offset;
                foreach (PatternDart d in fresh.darts) { d.tip += offset; d.tip2 += offset; }
                foreach (PatternLine l in fresh.lines) for (int i = 0; i < l.points.Count; i++) l.points[i] += offset;
                string before = JsonUtility.ToJson(new MirrorShape(copy));
                string after = JsonUtility.ToJson(new MirrorShape(fresh));
                if (before == after) continue;
                copy.points = fresh.points; copy.edges = fresh.edges; copy.darts = fresh.darts; copy.pleats = fresh.pleats; copy.lines = fresh.lines;
                copy.grainAngle = fresh.grainAngle; copy.meshDetail = fresh.meshDetail; copy.interfacing = fresh.interfacing;
                var edgeIds = new HashSet<string>();
                foreach (PatternEdge e in copy.edges) edgeIds.Add(e.id);
                foreach (PatternLine l in copy.lines) edgeIds.Add(l.id);
                pattern.seams.RemoveAll(s => (s.a.pieceId == copy.id && !edgeIds.Contains(s.a.edgeId)) || (s.b.pieceId == copy.id && !edgeIds.Contains(s.b.edgeId)));
                changed++;
            }
            return changed;
        }

        /// <summary>What a linked mirror copies from its original, to tell whether it needs updating.</summary>
        [System.Serializable]
        private sealed class MirrorShape
        {
            public List<PatternPoint> points; public List<PatternEdge> edges; public List<PatternDart> darts; public List<PatternPleat> pleats; public List<PatternLine> lines;
            public float grainAngle, meshDetail; public InterfacingLevel interfacing;
            public MirrorShape(PatternPiece p)
            {
                points = new List<PatternPoint>();
                foreach (PatternPoint q in p.points) points.Add(new PatternPoint { position = q.position });
                edges = p.edges; darts = new List<PatternDart>(); pleats = new List<PatternPleat>(); lines = p.lines;
                foreach (PatternDart d in p.darts) { var c = JsonUtility.FromJson<PatternDart>(JsonUtility.ToJson(d)); c.id = null; darts.Add(c); }
                foreach (PatternPleat d in p.pleats) { var c = JsonUtility.FromJson<PatternPleat>(JsonUtility.ToJson(d)); c.id = null; pleats.Add(c); }
                grainAngle = p.grainAngle; meshDetail = p.meshDetail; interfacing = p.interfacing;
            }
        }

        public static void DeletePiece(ClothesPattern pattern, PatternPiece piece)
        {
            pattern.seams.RemoveAll(s => s.a.pieceId == piece.id || s.b.pieceId == piece.id);
            pattern.tacks.RemoveAll(t => t.pieceA == piece.id || t.pieceB == piece.id);
            pattern.parts.RemoveAll(t => t.pieceId == piece.id);
            foreach (PatternDimension d in pattern.dimensions) d.pieceIds.Remove(piece.id);
            foreach (PatternPiece other in pattern.pieces) if (other.mirrorOf == piece.id) { other.mirrorOf = null; other.mirrorLinked = false; }
            pattern.pieces.Remove(piece);
        }
    }
}
