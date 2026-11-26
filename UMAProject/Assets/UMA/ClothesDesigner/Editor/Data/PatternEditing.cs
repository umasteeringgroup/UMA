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

        /// <summary>
        /// Moves an edge: both its end points by the same amount. A curved edge keeps its shape (its handles are kept
        /// relative to its ends); the edges either side stretch to follow, as when a point is dragged.
        /// </summary>
        public static void MoveEdge(PatternPiece piece, int edgeIndex, Vector2 delta)
        {
            int n = piece != null ? piece.points.Count : 0;
            if (n < 2 || edgeIndex < 0 || edgeIndex >= n) return;
            piece.points[edgeIndex].position += delta;
            piece.points[(edgeIndex + 1) % n].position += delta;
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
                sharp = seam.sharp,
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

        /// <summary>
        /// Deletes a point, joining its two edges into one (the edge before it then runs on to the next point). What was
        /// on either edge stays where it was along the joined edge: seams keep their stretch (a seam the point had split
        /// in two becomes one again), darts and pleats keep their places, and the joined edge keeps the curve of both. A
        /// linked mirror's seams are carried over the same way. False when the piece would have fewer than three points.
        /// </summary>
        public static bool DeletePoint(ClothesPattern pattern, PatternPiece piece, int pointIndex)
        {
            int n = piece.points.Count;
            if (n <= 3 || pointIndex < 0 || pointIndex >= n) return false;
            int before = (pointIndex - 1 + n) % n;
            PatternEdge first = piece.edges[before], second = piece.edges[pointIndex];
            float la = PatternGeometry.EdgeLength(piece, before, PieceScale.Identity);
            float lb = PatternGeometry.EdgeLength(piece, pointIndex, PieceScale.Identity);
            float share = la + lb > 1e-6f ? la / (la + lb) : 0.5f;
            JoinEdgeShape(first, second, piece.points[before].position, piece.points[pointIndex].position, piece.points[(pointIndex + 1) % n].position, la, lb, share);
            JoinEdgeSpans(pattern, piece.id, first.id, first.id, second.id, share);
            // A linked mirror runs the other way round: the mirror of the second edge comes first along its joined edge.
            foreach (PatternPiece other in pattern.pieces)
            {
                if (other == null || other == piece || !other.mirrorLinked || other.mirrorOf != piece.id) continue;
                PatternEdge mirrorFirst = other.edges.Find(e => e.mirrorOfEdge == first.id), mirrorSecond = other.edges.Find(e => e.mirrorOfEdge == second.id);
                if (mirrorFirst != null && mirrorSecond != null) JoinEdgeSpans(pattern, other.id, mirrorFirst.id, mirrorSecond.id, mirrorFirst.id, 1f - share);
            }
            foreach (PatternDart dart in piece.darts)
            {
                if (dart.kind != DartKind.Edge) continue;
                if (dart.edgeId == first.id) dart.position *= share;
                else if (dart.edgeId == second.id) { dart.edgeId = first.id; dart.position = share + dart.position * (1f - share); }
            }
            foreach (PatternPleat pleat in piece.pleats)
            {
                if (pleat.edgeId == first.id) pleat.position *= share;
                else if (pleat.edgeId == second.id) { pleat.edgeId = first.id; pleat.position = share + pleat.position * (1f - share); }
            }
            foreach (PatternTrim trim in pattern.trims)
            {
                if (trim == null || trim.pieceId != piece.id) continue;
                if (trim.pathId == first.id) { trim.start *= share; trim.end *= share; }
                else if (trim.pathId == second.id) { trim.pathId = first.id; trim.start = share + trim.start * (1f - share); trim.end = share + trim.end * (1f - share); }
            }
            piece.points.RemoveAt(pointIndex);
            piece.edges.RemoveAt(pointIndex);
            return true;
        }

        /// <summary>
        /// Stitch Points: an edge's two points become one, at its middle. The edge goes, with what was on it (seams,
        /// darts, pleats); the edges either side meet at the new point and keep theirs. False when the piece would have
        /// fewer than three points.
        /// </summary>
        public static bool MergePoints(ClothesPattern pattern, PatternPiece piece, int edgeIndex)
        {
            int n = piece.points.Count;
            if (n <= 3 || edgeIndex < 0 || edgeIndex >= n) return false;
            PatternEdge gone = piece.edges[edgeIndex];
            Vector2 middle = new PatternGeometry.EdgeCurve(piece, edgeIndex, PieceScale.Identity).At(0.5f);
            pattern.seams.RemoveAll(s => (s.a.pieceId == piece.id && s.a.edgeId == gone.id) || (s.b.pieceId == piece.id && s.b.edgeId == gone.id));
            piece.darts.RemoveAll(d => d.kind == DartKind.Edge && d.edgeId == gone.id);
            piece.pleats.RemoveAll(p => p.edgeId == gone.id);
            pattern.trims.RemoveAll(tr => tr != null && tr.pieceId == piece.id && tr.pathId == gone.id);
            piece.points[edgeIndex].position = middle;
            piece.points[(edgeIndex + 1) % n].position = middle;
            // The edge now has no length: deleting its first point joins it to the edge before, which keeps everything.
            return DeletePoint(pattern, piece, edgeIndex);
        }

        /// <summary>The curve of two edges joined into the first (its start, the point between them, the second's end).</summary>
        private static void JoinEdgeShape(PatternEdge first, PatternEdge second, Vector2 start, Vector2 middle, Vector2 end, float la, float lb, float share)
        {
            if (lb < 1e-4f) return;
            if (la < 1e-4f)
            {
                first.curved = second.curved; first.handleA = second.handleA; first.handleB = second.handleB;
                first.opening = second.opening; first.elastic = second.elastic; first.elasticPercent = second.elasticPercent;
                return;
            }
            if (first.curved || second.curved)
            {
                // The outer handles, stretched to the joined length (the inverse of splitting an edge).
                Vector2 handleA = first.curved ? first.handleA : (middle - start) / 3f;
                Vector2 handleB = second.curved ? second.handleB : (middle - end) / 3f;
                first.curved = true;
                first.handleA = handleA / share;
                first.handleB = handleB / (1f - share);
            }
            first.opening = first.opening && second.opening;
            if (!first.elastic && second.elastic) { first.elastic = true; first.elasticPercent = second.elasticPercent; }
        }

        /// <summary>
        /// Two edges of a piece become one with id <paramref name="keepId"/>: seam spans on <paramref name="startId"/> are
        /// squeezed into [0, share] of it, those on <paramref name="endId"/> into [share, 1]. A seam the join had split in
        /// two becomes one again; a seam left with no length, or sewing the joined edge onto itself, goes.
        /// </summary>
        private static void JoinEdgeSpans(ClothesPattern pattern, string pieceId, string keepId, string startId, string endId, float share)
        {
            void Remap(ref SeamSpan s)
            {
                if (s.pieceId != pieceId) return;
                if (s.edgeId == startId) { s.edgeId = keepId; s.start *= share; s.end *= share; }
                else if (s.edgeId == endId) { s.edgeId = keepId; s.start = share + s.start * (1f - share); s.end = share + s.end * (1f - share); }
            }
            foreach (PatternSeam seam in pattern.seams) { Remap(ref seam.a); Remap(ref seam.b); }
            bool OnJoined(SeamSpan s) => s.pieceId == pieceId && s.edgeId == keepId;
            pattern.seams.RemoveAll(s => (OnJoined(s.a) && s.a.end - s.a.start < 1e-4f) || (OnJoined(s.b) && s.b.end - s.b.start < 1e-4f)
                || (OnJoined(s.a) && OnJoined(s.b) && s.a.start < s.b.end && s.b.start < s.a.end));
            bool joined = true;
            while (joined)
            {
                joined = false;
                for (int i = 0; i < pattern.seams.Count && !joined; i++)
                {
                    for (int j = 0; j < pattern.seams.Count && !joined; j++)
                    {
                        if (i != j && TryJoinSeams(pattern.seams[i], pattern.seams[j], pieceId, keepId, share)) { pattern.seams.RemoveAt(j); joined = true; }
                    }
                }
            }
        }

        /// <summary>
        /// When <paramref name="x"/> ends and <paramref name="y"/> starts at <paramref name="at"/> on the same edge, and
        /// their other sides run on from one to the other along one edge, <paramref name="x"/> becomes the whole of both.
        /// </summary>
        private static bool TryJoinSeams(PatternSeam x, PatternSeam y, string pieceId, string edgeId, float at)
        {
            if (x == null || y == null || x.reversed != y.reversed || x.folded != y.folded) return false;
            for (int sx = 0; sx < 2; sx++)
            {
                for (int sy = 0; sy < 2; sy++)
                {
                    SeamSpan h1 = sx == 0 ? x.a : x.b, t1 = sx == 0 ? x.b : x.a;
                    SeamSpan h2 = sy == 0 ? y.a : y.b, t2 = sy == 0 ? y.b : y.a;
                    if (h1.pieceId != pieceId || h1.edgeId != edgeId || h2.pieceId != pieceId || h2.edgeId != edgeId) continue;
                    if (Mathf.Abs(h1.end - at) > 1e-3f || Mathf.Abs(h2.start - at) > 1e-3f) continue;
                    if (t1.pieceId != t2.pieceId || t1.edgeId != t2.edgeId) continue;
                    SeamSpan there = t1;
                    if (!x.reversed)
                    {
                        if (Mathf.Abs(t1.end - t2.start) > 1e-3f) continue;
                        there.end = t2.end;
                    }
                    else
                    {
                        if (Mathf.Abs(t1.start - t2.end) > 1e-3f) continue;
                        there.start = t2.start;
                    }
                    SeamSpan here = h1;
                    here.end = h2.end;
                    if (sx == 0) { x.a = here; x.b = there; } else { x.b = here; x.a = there; }
                    return true;
                }
            }
            return false;
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

        /// <summary>How close a point must be to another's mirror image to count as its partner (cm).</summary>
        public const float MirrorToleranceCm = 0.5f;

        /// <summary>A point reflected across the vertical line x = <paramref name="axisX"/>.</summary>
        public static Vector2 Mirror(Vector2 p, float axisX) => new Vector2(2f * axisX - p.x, p.y);

        /// <summary>The point of a piece nearest another point's mirror image, within <see cref="MirrorToleranceCm"/>; -1 when none.</summary>
        public static int MirrorPartnerPoint(PatternPiece piece, Vector2 p, float axisX)
        {
            Vector2 image = Mirror(p, axisX);
            int best = -1;
            float bestDistance = MirrorToleranceCm;
            for (int i = 0; i < piece.points.Count; i++)
            {
                float d = Vector2.Distance(piece.points[i].position, image);
                if (d <= bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// The edge that mirrors an edge across x = <paramref name="axisX"/> (it runs the other way: from the mirror of the
        /// edge's end to the mirror of its start); the edge itself when it crosses the axis symmetrically; -1 when none.
        /// </summary>
        public static int MirrorEdge(PatternPiece piece, int edgeIndex, float axisX)
        {
            int n = piece.points.Count;
            if (edgeIndex < 0 || edgeIndex >= n) return -1;
            int start = MirrorPartnerPoint(piece, piece.points[edgeIndex].position, axisX);
            int end = MirrorPartnerPoint(piece, piece.points[(edgeIndex + 1) % n].position, axisX);
            if (start < 0 || end < 0) return -1;
            // The mirror edge runs from the end's partner to the start's partner.
            return (end + 1) % n == start ? end : -1;
        }

        /// <summary>
        /// A copy of a dart, added to its piece: beside it (a diamond its width and 2 cm across; an edge dart further along
        /// its edge, or back along it near the end), or with <paramref name="mirrorAxisX"/> reflected across the piece's
        /// middle (an edge dart on the mirror edge). New id. Null when an edge dart's edge is gone.
        /// </summary>
        public static PatternDart DuplicateDart(PatternPiece piece, PatternDart dart, float? mirrorAxisX)
        {
            var copy = JsonUtility.FromJson<PatternDart>(JsonUtility.ToJson(dart));
            copy.id = ClothesPattern.NewId();
            if (dart.kind == DartKind.Double)
            {
                bool onAxis = mirrorAxisX.HasValue && Mathf.Abs(dart.tip.x - mirrorAxisX.Value) <= MirrorToleranceCm && Mathf.Abs(dart.tip2.x - mirrorAxisX.Value) <= MirrorToleranceCm;
                if (mirrorAxisX.HasValue && !onAxis) { copy.tip = Mirror(dart.tip, mirrorAxisX.Value); copy.tip2 = Mirror(dart.tip2, mirrorAxisX.Value); }
                else { var shift = new Vector2(dart.widthCm + 2f, 0f); copy.tip += shift; copy.tip2 += shift; }
                piece.darts.Add(copy);
                return copy;
            }
            int edge = piece.IndexOfEdge(dart.edgeId);
            if (edge < 0) return null;
            if (mirrorAxisX.HasValue)
            {
                int mirror = MirrorEdge(piece, edge, mirrorAxisX.Value);
                if (mirror >= 0)
                {
                    copy.edgeId = piece.edges[mirror].id;
                    copy.position = 1f - dart.position;
                    copy.tip = Mirror(dart.tip, mirrorAxisX.Value);
                    // On an edge that mirrors itself, the copy is the dart's own reflection along it.
                    if (mirror != edge || Mathf.Abs(copy.position - dart.position) > 0.01f)
                    {
                        piece.darts.Add(copy);
                        return copy;
                    }
                }
            }
            var curve = new PatternGeometry.EdgeCurve(piece, edge, PieceScale.Identity);
            float length = Mathf.Max(0.01f, curve.Length);
            float step = (dart.widthCm + 2f) / length, half = dart.widthCm * 0.5f / length;
            float position = dart.position + step <= 1f - half - 0.02f ? dart.position + step : dart.position - step;
            position = Mathf.Clamp(position, half + 0.02f, 1f - half - 0.02f);
            copy.edgeId = dart.edgeId;
            copy.position = position;
            copy.tip = dart.tip + curve.At(position) - curve.At(dart.position);
            piece.darts.Add(copy);
            return copy;
        }

        /// <summary>A copy of a pleat, added to its piece: further along its edge, or mirrored onto the mirror edge (its fold turned the other way). Null when its edge is gone.</summary>
        public static PatternPleat DuplicatePleat(PatternPiece piece, PatternPleat pleat, float? mirrorAxisX)
        {
            int edge = piece.IndexOfEdge(pleat.edgeId);
            if (edge < 0) return null;
            var copy = JsonUtility.FromJson<PatternPleat>(JsonUtility.ToJson(pleat));
            copy.id = ClothesPattern.NewId();
            int mirror = mirrorAxisX.HasValue ? MirrorEdge(piece, edge, mirrorAxisX.Value) : -1;
            if (mirror >= 0 && (mirror != edge || Mathf.Abs(0.5f - pleat.position) > 0.01f))
            {
                copy.edgeId = piece.edges[mirror].id;
                copy.position = 1f - pleat.position;
                copy.kind = pleat.kind == PleatKind.KnifeLeft ? PleatKind.KnifeRight : pleat.kind == PleatKind.KnifeRight ? PleatKind.KnifeLeft : pleat.kind;
            }
            else
            {
                float length = Mathf.Max(0.01f, PatternGeometry.EdgeLength(piece, edge, PieceScale.Identity));
                float step = (pleat.depthCm * 2f + pleat.boxWidthCm + 2f) / length;
                copy.position = Mathf.Clamp(pleat.position + step <= 0.98f ? pleat.position + step : pleat.position - step, 0.02f, 0.98f);
            }
            piece.pleats.Add(copy);
            return copy;
        }

        /// <summary>A copy of a line inside a piece, added to it: 2 cm across, or reflected across the piece's middle. New id; not a linked mirror's line.</summary>
        public static PatternLine DuplicateLine(PatternPiece piece, PatternLine line, float? mirrorAxisX)
        {
            var copy = JsonUtility.FromJson<PatternLine>(JsonUtility.ToJson(line));
            copy.id = ClothesPattern.NewId();
            copy.mirrorOfLine = null;
            // A line that is its own mirror image (on the middle) would be copied onto itself: it goes beside instead.
            bool mirrored = mirrorAxisX.HasValue && !MirrorsItself(line.points, mirrorAxisX.Value);
            for (int i = 0; i < copy.points.Count; i++)
                copy.points[i] = mirrored ? Mirror(copy.points[i], mirrorAxisX.Value) : copy.points[i] + new Vector2(2f, 0f);
            piece.lines.Add(copy);
            return copy;
        }

        /// <summary>Whether a set of points is its own mirror image across <paramref name="axisX"/>: each has a mirror partner among them.</summary>
        private static bool MirrorsItself(List<Vector2> points, float axisX)
        {
            foreach (Vector2 p in points)
            {
                Vector2 image = Mirror(p, axisX);
                bool found = false;
                foreach (Vector2 q in points) if (Vector2.Distance(q, image) <= MirrorToleranceCm) { found = true; break; }
                if (!found) return false;
            }
            return points.Count > 0;
        }

        /// <summary>A copy of a part (a button, a plate), added to the pattern: 2 cm across, or reflected across its piece's middle (beside it when it is on the middle).</summary>
        public static PatternPart DuplicatePart(ClothesPattern pattern, PatternPart part, float? mirrorAxisX)
        {
            var copy = JsonUtility.FromJson<PatternPart>(JsonUtility.ToJson(part));
            copy.id = ClothesPattern.NewId();
            bool mirrored = mirrorAxisX.HasValue && Mathf.Abs(part.position.x - mirrorAxisX.Value) > MirrorToleranceCm;
            copy.position = mirrored ? Mirror(part.position, mirrorAxisX.Value) : part.position + new Vector2(2f, 0f);
            pattern.parts.Add(copy);
            return copy;
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
                textureDetail = source.textureDetail,
                lookId = source.lookId,
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
            copy.thicknessMm = source.thicknessMm;
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
        /// Duplicate with Seams: a copy of a piece (added after it), sewn as the piece is to the copies of the pieces it is
        /// sewn to, and to itself where the piece is. A set of pieces duplicated one by one (a lining, a second layer)
        /// comes out sewn together as the originals are: a seam whose other piece has no copy yet is made when that piece
        /// is duplicated the same way. <paramref name="sewn"/> counts the seams made, <paramref name="waiting"/> those left
        /// for later.
        /// </summary>
        public static PatternPiece DuplicateWithSeams(ClothesPattern pattern, PatternPiece source, string name, Vector2 offset, out int sewn, out int waiting)
        {
            PatternPiece copy = Duplicate(source, name, offset);
            copy.duplicateOf = source.id;
            pattern.pieces.Insert(pattern.pieces.IndexOf(source) + 1, copy);
            sewn = waiting = 0;
            foreach (PatternSeam seam in pattern.seams.ToArray())
            {
                bool onA = seam.a.pieceId == source.id, onB = seam.b.pieceId == source.id;
                if (!onA && !onB) continue;
                SeamSpan a = seam.a, b = seam.b;
                bool ok = true;
                if (onA) ok &= ToCopy(source, copy, ref a);
                if (onB) ok &= ToCopy(source, copy, ref b);
                if (ok && !onA) ok = ToPartnerCopy(pattern, copy, ref a);
                if (ok && !onB) ok = ToPartnerCopy(pattern, copy, ref b);
                if (!ok) { waiting++; continue; }
                pattern.seams.Add(new PatternSeam
                {
                    id = ClothesPattern.NewId(), a = a, b = b, reversed = seam.reversed, folded = seam.folded, sharp = seam.sharp,
                    topstitch = seam.topstitch, topstitchOffsetMm = seam.topstitchOffsetMm
                });
                sewn++;
            }
            return copy;
        }

        /// <summary>
        /// Duplicate Sewn Round: <see cref="DuplicateWithSeams"/>, on the layer above, and sewn to the piece (start to start)
        /// along every stretch of its outline the piece has free, its openings too, which are then no longer openings: a
        /// two-ply collar, cuff, flap or belt; or, with each piece of a garment done the same way, a lined garment (the
        /// copies its outside, the originals its lining). <paramref name="sewnRound"/> counts the seams to the piece.
        /// </summary>
        public static PatternPiece DuplicateSewnRound(ClothesPattern pattern, PatternPiece source, string name, Vector2 offset, out int sewnRound, out int sewn, out int waiting)
        {
            PatternPiece copy = DuplicateWithSeams(pattern, source, name, offset, out sewn, out waiting);
            copy.layer = Mathf.Min(source.layer + 1, 5);
            sewnRound = 0;
            for (int i = 0; i < source.edges.Count && i < copy.edges.Count; i++)
            {
                PatternEdge mine = source.edges[i], theirs = copy.edges[i];
                for (int guard = 0; guard < 16 && pattern.FreeSpan(source.id, mine.id, out float s0, out float s1); guard++)
                {
                    mine.opening = false;
                    theirs.opening = false;
                    pattern.seams.Add(new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(source.id, mine.id, s0, s1), b = new SeamSpan(copy.id, theirs.id, s0, s1) });
                    sewnRound++;
                }
            }
            return copy;
        }

        /// <summary>A seam side on a piece moved to the same edge or line of its copy (made by <see cref="Duplicate"/>, so in the same order).</summary>
        private static bool ToCopy(PatternPiece source, PatternPiece copy, ref SeamSpan span)
        {
            int edge = source.IndexOfEdge(span.edgeId);
            if (edge >= 0 && edge < copy.edges.Count) { span.pieceId = copy.id; span.edgeId = copy.edges[edge].id; return true; }
            string lineId = span.edgeId;
            int line = source.lines.FindIndex(l => l.id == lineId);
            if (line >= 0 && line < copy.lines.Count) { span.pieceId = copy.id; span.edgeId = copy.lines[line].id; return true; }
            return false;
        }

        /// <summary>A seam side moved to a copy (Duplicate with Seams) of its piece, where that copy is still free there; false when there is none.</summary>
        private static bool ToPartnerCopy(ClothesPattern pattern, PatternPiece made, ref SeamSpan span)
        {
            PatternPiece partner = pattern.FindPiece(span.pieceId);
            if (partner == null) return false;
            foreach (PatternPiece candidate in pattern.pieces)
            {
                if (candidate == null || candidate == made || candidate.duplicateOf != partner.id) continue;
                if (candidate.edges.Count != partner.edges.Count || candidate.lines.Count != partner.lines.Count) continue;
                SeamSpan moved = span;
                if (!ToCopy(partner, candidate, ref moved)) continue;
                bool used = false;
                foreach (PatternSeam s in pattern.seams)
                {
                    if (SameStretch(s.a, moved) || SameStretch(s.b, moved)) { used = true; break; }
                }
                if (used) continue;
                span = moved;
                return true;
            }
            return false;
        }

        private static bool SameStretch(SeamSpan x, SeamSpan y) =>
            x.pieceId == y.pieceId && x.edgeId == y.edgeId && x.start < y.end - 1e-4f && y.start < x.end - 1e-4f;

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

        /// <summary>
        /// Adds two diamond darts mirrored across <paramref name="axisX"/>, each halfway between the middle and the side, at
        /// the piece's middle height and as long as fits inside it (up to the length of <see cref="AddDoubleDart"/>'s).
        /// Returns them (the one left of the middle first), or null with nothing added when the piece has no room.
        /// </summary>
        public static PatternDart[] AddDoubleDartPair(PatternPiece piece, float axisX)
        {
            if (piece == null || piece.points.Count < 3) return null;
            Rect b = piece.Bounds();
            float quarter = Mathf.Min(axisX - b.xMin, b.xMax - axisX) * 0.5f;
            if (quarter < 1f) return null;
            List<Vector2> outline = PatternGeometry.SampleOutline(piece, 0.5f, null);
            float width = Mathf.Min(3f, b.width * 0.2f, quarter);
            var left = new Vector2(axisX - quarter, b.center.y);
            Vector2 right = Mirror(left, axisX);
            bool Inside(Vector2 q) => PatternGeometry.PointInPolygon(outline, q) && PatternGeometry.DistanceToPolyline(outline, q) >= 1f;
            bool Fits(Vector2 c, float h) =>
                Inside(c + new Vector2(0f, h)) && Inside(c - new Vector2(0f, h)) && Inside(c + new Vector2(width * 0.5f, 0f)) && Inside(c - new Vector2(width * 0.5f, 0f));
            float half = b.height * 0.3f;
            while (half > 1f && !(Fits(left, half) && Fits(right, half))) half *= 0.85f;
            if (!(Fits(left, half) && Fits(right, half))) return null;
            PatternDart first = AddDoubleDart(piece, left);
            first.widthCm = width;
            first.tip = left + new Vector2(0f, half);
            first.tip2 = left - new Vector2(0f, half);
            PatternDart second = DuplicateDart(piece, first, axisX);
            return new[] { first, second };
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
            Vector2 dir = to - from;
            if (dir.sqrMagnitude < 0.01f) { problem = "Draw the cut longer."; return null; }
            // Straight across the whole piece, endless both ways.
            Vector2 far = dir.normalized * 10000f;
            return CutPieceAlong(pattern, piece, new List<Vector2> { from - far, from + far }, false, sewTogether, out problem);
        }

        /// <summary>
        /// The knife: cuts a piece in two along a path (cm, as drawn), straight from point to point or, with
        /// <paramref name="smooth"/>, curving through them. The path must cross the outline exactly twice; an end inside
        /// the piece runs on in a straight line to the outline. Seams, darts, pleats, lines and tacks go with the half they
        /// are on; the new half keeps the original's UVs and placement and, with <paramref name="sewTogether"/>, is sewn
        /// back along the cut. Returns the new half, or null with a reason.
        /// </summary>
        public static PatternPiece CutPieceAlong(ClothesPattern pattern, PatternPiece piece, IList<Vector2> path, bool smooth, bool sewTogether, out string problem)
        {
            problem = null;
            var route = new List<Vector2>();
            if (path != null) foreach (Vector2 q in path) if (route.Count == 0 || (q - route[route.Count - 1]).sqrMagnitude > 1e-4f) route.Add(q);
            float routeLength = 0f;
            for (int s = 1; s < route.Count; s++) routeLength += (route[s] - route[s - 1]).magnitude;
            if (route.Count < 2 || routeLength < 0.1f) { problem = "Draw the cut longer."; return null; }
            List<Vector2> outlineSamples = PatternGeometry.SampleOutline(piece, 0.5f, null);
            route[0] = RunOnToOutline(outlineSamples, route[0], route[0] - route[1]);
            route[route.Count - 1] = RunOnToOutline(outlineSamples, route[route.Count - 1], route[route.Count - 1] - route[route.Count - 2]);

            // Every crossing of the path with the outline: how far along the path, and the edge and fraction.
            var crossings = new List<(float along, int edge, float fraction, Vector2 at)>();
            for (int e = 0; e < piece.points.Count; e++)
            {
                var curve = new PatternGeometry.EdgeCurve(piece, e, PieceScale.Identity);
                const int steps = 96;
                Vector2 previous = curve.At(0f);
                for (int k = 1; k <= steps; k++)
                {
                    Vector2 next = curve.At(k / (float)steps);
                    for (int s = 0; s + 1 < route.Count; s++)
                    {
                        if (LineGeometry.SegmentIntersection(previous, next, route[s], route[s + 1], out float alongEdge, out float alongPath) && (k < steps || alongEdge < 0.999f))
                        {
                            float fraction = (k - 1 + alongEdge) / steps;
                            crossings.Add((s + alongPath, e, fraction, curve.At(fraction)));
                        }
                    }
                    previous = next;
                }
            }
            // A crossing at a corner is found on both edges that meet there, and at a path point on both its segments.
            for (int i = crossings.Count - 1; i > 0; i--) for (int j = 0; j < i; j++) if ((crossings[i].at - crossings[j].at).sqrMagnitude < 0.0025f) { crossings.RemoveAt(i); break; }
            if (crossings.Count != 2) { problem = "A cut must cross the piece's outline exactly twice; this one crosses it " + crossings.Count + " times."; return null; }
            crossings.Sort((x, y) => x.along.CompareTo(y.along));
            // The path's own points between the two crossings, in order along it.
            var inner = new List<Vector2>();
            for (int s = 1; s + 1 < route.Count; s++)
            {
                if (s <= crossings[0].along + 1e-4f || s >= crossings[1].along - 1e-4f) continue;
                if ((route[s] - crossings[0].at).sqrMagnitude < 0.0025f || (route[s] - crossings[1].at).sqrMagnitude < 0.0025f) continue;
                inner.Add(route[s]);
            }
            Vector2 atFirst = crossings[0].at;

            // Points at the crossings (at a corner, the corner itself); the later edge first, so indices hold.
            var bySplit = new List<(float along, int edge, float fraction, Vector2 at)>(crossings);
            bySplit.Sort((x, y) => y.edge != x.edge ? y.edge.CompareTo(x.edge) : y.fraction.CompareTo(x.fraction));
            string firstId = null, secondId = null;
            int splitEdge = -1;
            float splitAt = 1f;
            foreach (var c in bySplit)
            {
                // Twice on one edge: after the later split the edge's first part is what is left of it.
                float fraction = c.edge == splitEdge ? c.fraction / splitAt : c.fraction;
                int index;
                if (fraction < 0.01f) index = c.edge;
                else if (fraction > 0.99f) index = (c.edge + 1) % piece.points.Count;
                else { index = SplitEdge(pattern, piece, c.edge, fraction); splitEdge = c.edge; splitAt = c.fraction; }
                if ((c.at - atFirst).sqrMagnitude < 1e-8f) firstId = piece.points[index].id; else secondId = piece.points[index].id;
            }
            int i1 = piece.IndexOfPoint(firstId), i2 = piece.IndexOfPoint(secondId);
            // The cut runs from the point at i2 to the point at i1 on this piece: the path's points the other way when
            // its first crossing is at i1.
            if (i1 > i2) { int t = i1; i1 = i2; i2 = t; }
            else inner.Reverse();
            int n = piece.points.Count;
            if (i1 < 0 || i2 - i1 < 1 || i2 - i1 == n || (inner.Count == 0 && (i2 - i1 == 1 || i1 + n - i2 == 1))) { problem = "The cut runs along the outline."; return null; }
            Rect before = piece.Bounds();

            // The new half: points i2 .. i1 (round the end), its edges, then the cut from i1 back to i2.
            var half = new PatternPiece
            {
                id = ClothesPattern.NewId(),
                name = piece.name + " (cut)",
                grainAngle = piece.grainAngle,
                placement = piece.placement.Clone(),
                fabric = piece.fabric != null ? piece.fabric.Clone() : new FabricSettings(),
                meshDetail = piece.meshDetail,
                textureDetail = piece.textureDetail,
                lookId = piece.lookId,
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
            // The cut's edges on this piece: from i2 through the path's points to i1, curving through them when smooth.
            var along = new List<Vector2> { piece.points[i2].position };
            along.AddRange(inner);
            along.Add(piece.points[i1].position);
            int m = along.Count - 1;
            var cutOnPiece = new List<PatternEdge>();
            for (int j = 0; j < m; j++)
            {
                var edge = new PatternEdge { id = ClothesPattern.NewId(), name = "Cut" };
                if (smooth && m > 1)
                {
                    edge.curved = true;
                    edge.handleA = PathTangent(along, j) / 3f;
                    edge.handleB = -PathTangent(along, j + 1) / 3f;
                }
                cutOnPiece.Add(edge);
            }
            for (int j = 1; j < m; j++) keptPoints.Add(new PatternPoint { id = ClothesPattern.NewId(), position = along[j] });
            keptEdges.AddRange(cutOnPiece);
            // On the half the same edges run the other way: from i1 back through the path's points to i2.
            var cutOnHalf = new List<PatternEdge>();
            for (int j = m - 1; j >= 1; j--) half.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = along[j] });
            for (int j = m - 1; j >= 0; j--)
            {
                PatternEdge mine = cutOnPiece[j];
                var edge = new PatternEdge { id = ClothesPattern.NewId(), name = "Cut", curved = mine.curved, handleA = mine.handleB, handleB = mine.handleA };
                half.edges.Add(edge);
                cutOnHalf.Add(edge);
            }
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
                // The two cuts run opposite ways along the same path: the start of each edge meets the end of its twin.
                for (int j = 0; j < m; j++)
                    pattern.seams.Add(new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(piece.id, cutOnPiece[j].id), b = new SeamSpan(half.id, cutOnHalf[m - 1 - j].id), reversed = true });
            }
            return half;
        }

        /// <summary>The direction a path runs at its point <paramref name="j"/> (Catmull-Rom: from the point before to the point after).</summary>
        private static Vector2 PathTangent(List<Vector2> path, int j)
        {
            if (j <= 0) return path[1] - path[0];
            if (j >= path.Count - 1) return path[path.Count - 1] - path[path.Count - 2];
            return (path[j + 1] - path[j - 1]) * 0.5f;
        }

        /// <summary>
        /// An end of a cut: inside the piece it runs on in <paramref name="direction"/> to just past the outline; on the
        /// outline a little past it, so the cut crosses it cleanly; outside it stays.
        /// </summary>
        private static Vector2 RunOnToOutline(List<Vector2> outline, Vector2 end, Vector2 direction)
        {
            Vector2 dir = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector2.right;
            if (PatternGeometry.DistanceToPolyline(outline, end) < 0.05f) return end + dir * 0.5f;
            if (!PatternGeometry.PointInPolygon(outline, end)) return end;
            float nearest = float.MaxValue;
            Vector2 far = end + dir * 10000f;
            for (int i = 0; i < outline.Count; i++)
            {
                if (LineGeometry.SegmentIntersection(end, far, outline[i], outline[(i + 1) % outline.Count], out float t, out _)) nearest = Mathf.Min(nearest, t * 10000f);
            }
            return nearest < float.MaxValue ? end + dir * (nearest + 0.5f) : end;
        }

        /// <summary>
        /// Cuts a piece along one of its open lines (a princess seam or a yoke drawn as a seam line), which then goes; its
        /// ends run on to the outline when short of it. With <paramref name="sewTogether"/> the halves are sewn back along
        /// it. Returns the new half, or null with a reason.
        /// </summary>
        public static PatternPiece CutAlongLine(ClothesPattern pattern, PatternPiece piece, PatternLine line, bool sewTogether, out string problem)
        {
            problem = null;
            if (line == null || line.points.Count < 2 || LineGeometry.IsClosed(line)) { problem = "Only an open line can be cut along."; return null; }
            if (pattern.FindSeamsOn(piece.id, line.id).Count > 0) { problem = "Something is sewn onto this line. Unsew it first."; return null; }
            if (pattern.trims.Exists(tr => tr != null && tr.pieceId == piece.id && tr.pathId == line.id)) { problem = "A trim runs along this line. Delete it first."; return null; }
            int index = piece.lines.IndexOf(line);
            if (index < 0) { problem = "The line is not on this piece."; return null; }
            var path = new List<Vector2>(line.points);
            piece.lines.RemoveAt(index);
            PatternPiece half = CutPieceAlong(pattern, piece, path, false, sewTogether, out problem);
            if (half == null) piece.lines.Insert(index, line);
            return half;
        }

        // ---- Cut in Half and Symmetrize --------------------------------------------------------------

        /// <summary>
        /// Cut in Half: cuts a piece along the upright line x = <paramref name="axisX"/>, keeps one half (with the piece's
        /// id and name) and puts a linked mirror of it where the other half was, sewn to it along the cut. The other half's
        /// seams go to the mirror's edge lying where theirs did; those with none, and those on its lines, are dropped
        /// (<paramref name="dropped"/>). Its darts, pleats and lines give way to the kept half's, mirrored; its tacks and
        /// parts go onto the mirror. Returns the mirror, or null with a reason.
        /// </summary>
        public static PatternPiece CutInHalf(ClothesPattern pattern, PatternPiece piece, float axisX, bool keepLeft, out string problem, out int dropped)
        {
            dropped = 0;
            problem = null;
            if (piece.mirrorLinked) { problem = "'" + piece.name + "' is a linked mirror: work on its original, or unlink it first."; return null; }
            Rect b = piece.Bounds();
            if (axisX <= b.xMin + 0.5f || axisX >= b.xMax - 0.5f) { problem = "The middle line must run through the piece."; return null; }
            string id = piece.id, name = piece.name;
            PatternPiece other = CutPiece(pattern, piece, new Vector2(axisX, b.yMin - 1f), new Vector2(axisX, b.yMax + 1f), true, out problem);
            if (other == null) return null;
            bool pieceIsLeft = piece.Bounds().center.x < axisX;
            PatternPiece kept = pieceIsLeft == keepLeft ? piece : other, gone = kept == piece ? other : piece;

            PatternPiece mirror = MirrorCopy(kept, name + " Mirrored");
            mirror.mirrorLinked = true;
            // MirrorCopy reflects about the kept half's own middle; this puts it about the axis instead.
            float shift = 2f * (axisX - kept.Bounds().center.x);
            foreach (PatternPoint p in mirror.points) p.position.x += shift;
            foreach (PatternDart d in mirror.darts) { d.tip.x += shift; d.tip2.x += shift; }
            foreach (PatternLine l in mirror.lines) for (int i = 0; i < l.points.Count; i++) l.points[i] += new Vector2(shift, 0f);
            pattern.pieces.Insert(pattern.pieces.IndexOf(gone), mirror);

            dropped = MoveSeamsByPlace(pattern, gone, mirror);
            foreach (PatternTack tack in pattern.tacks)
            {
                if (tack.pieceA == gone.id) tack.pieceA = mirror.id;
                if (tack.pieceB == gone.id) tack.pieceB = mirror.id;
            }
            foreach (PatternPart part in pattern.parts) if (part.pieceId == gone.id) part.pieceId = mirror.id;
            foreach (PatternTrim trim in pattern.trims) if (trim != null && trim.pieceId == gone.id) trim.pieceId = null;
            pattern.trims.RemoveAll(tr => tr != null && tr.pieceId == null);
            foreach (PatternDimension dim in pattern.dimensions) if (dim.pieceIds.Remove(gone.id) && !dim.pieceIds.Contains(mirror.id)) dim.pieceIds.Add(mirror.id);
            pattern.pieces.Remove(gone);
            // The kept half carries on as the piece: its id (so linked mirrors of it, and the fit, keep following it) and name.
            if (kept != piece) ReplacePieceId(pattern, kept, id);
            kept.name = name;
            return mirror;
        }

        /// <summary>
        /// Symmetrize: makes a piece the mirror image of one of its halves about x = <paramref name="axisX"/>, still one
        /// piece with its id. The other half's seams go to the edge lying where theirs did (<paramref name="dropped"/>
        /// counts those with none); its darts, pleats and lines give way to the kept half's, mirrored. A point left on the
        /// middle between two straight edges in line (a straight hem) is taken out again. Returns the piece, or null with a
        /// reason; <paramref name="problem"/> may hold a warning even when it succeeds.
        /// </summary>
        public static PatternPiece Symmetrize(ClothesPattern pattern, PatternPiece piece, float axisX, bool keepLeft, out string problem, out int dropped)
        {
            string id = piece.id;
            PatternPiece mirror = CutInHalf(pattern, piece, axisX, keepLeft, out problem, out dropped);
            if (mirror == null) return null;
            PatternPiece kept = pattern.FindPiece(id);
            PatternSeam middle = null;
            foreach (PatternSeam s in pattern.seams)
            {
                bool joins = (s.a.pieceId == kept.id && s.b.pieceId == mirror.id) || (s.a.pieceId == mirror.id && s.b.pieceId == kept.id);
                if (!joins) continue;
                SeamSpan mine = s.a.pieceId == kept.id ? s.a : s.b;
                int edge = kept.IndexOfEdge(mine.edgeId);
                if (edge < 0) continue;
                Vector2 p0 = kept.points[edge].position, p1 = kept.points[(edge + 1) % kept.points.Count].position;
                if (Mathf.Abs(p0.x - axisX) < 0.01f && Mathf.Abs(p1.x - axisX) < 0.01f) { middle = s; break; }
            }
            if (middle == null) { problem = "The halves could not be joined again."; return null; }
            if (middle.a.pieceId != kept.id) { SeamSpan swap = middle.a; middle.a = middle.b; middle.b = swap; }
            mirror.mirrorLinked = false;
            mirror.mirrorOf = null;
            PatternPiece merged = MergePieces(pattern, middle, out problem);
            if (merged == null) return null;
            // The ends of the join: a point between two straight edges in line is not needed.
            for (int i = merged.points.Count - 1; i >= 0 && merged.points.Count > 3; i--)
            {
                if (i >= merged.points.Count || Mathf.Abs(merged.points[i].position.x - axisX) > 0.01f) continue;
                int n = merged.points.Count, prev = (i - 1 + n) % n;
                if (merged.edges[prev].curved || merged.edges[i].curved) continue;
                Vector2 d0 = merged.points[i].position - merged.points[prev].position, d1 = merged.points[(i + 1) % n].position - merged.points[i].position;
                if (d0.sqrMagnitude < 1e-6f || d1.sqrMagnitude < 1e-6f) continue;
                if (Mathf.Abs(d0.normalized.x * d1.normalized.y - d0.normalized.y * d1.normalized.x) < 0.01f && Vector2.Dot(d0, d1) > 0f) DeletePoint(pattern, merged, i);
            }
            return merged;
        }

        /// <summary>
        /// Moves the seams on one piece's edges to the edge of another lying in the same place (either way round, the
        /// stretch turned to match); seams with no edge there, or on the first piece's lines, are removed. Returns how many
        /// were removed.
        /// </summary>
        private static int MoveSeamsByPlace(ClothesPattern pattern, PatternPiece from, PatternPiece to)
        {
            int removed = 0;
            int nf = from.points.Count, nt = to.points.Count;
            foreach (PatternSeam seam in pattern.seams.ToArray())
            {
                bool onA = seam.a.pieceId == from.id, onB = seam.b.pieceId == from.id;
                if (!onA && !onB) continue;
                bool ok = true;
                for (int side = 0; side < 2 && ok; side++)
                {
                    if ((side == 0 && !onA) || (side == 1 && !onB)) continue;
                    SeamSpan span = side == 0 ? seam.a : seam.b;
                    int e = from.IndexOfEdge(span.edgeId);
                    if (e < 0) { ok = false; break; }
                    var curve = new PatternGeometry.EdgeCurve(from, e, PieceScale.Identity);
                    Vector2 e0 = from.points[e].position, e1 = from.points[(e + 1) % nf].position, em = curve.At(0.5f);
                    // Generous: Symmetrize is for pieces that are a little lopsided, a shoulder a centimetre or two off.
                    float tolerance = Mathf.Max(1.5f, curve.Length * 0.25f);
                    int best = -1;
                    bool flipped = false;
                    float bestError = float.MaxValue;
                    for (int f = 0; f < nt; f++)
                    {
                        Vector2 f0 = to.points[f].position, f1 = to.points[(f + 1) % nt].position;
                        Vector2 fm = new PatternGeometry.EdgeCurve(to, f, PieceScale.Identity).At(0.5f);
                        float same = Mathf.Max((f0 - e0).magnitude, (f1 - e1).magnitude), opposite = Mathf.Max((f0 - e1).magnitude, (f1 - e0).magnitude);
                        float error = Mathf.Max(Mathf.Min(same, opposite), (fm - em).magnitude);
                        if (error < bestError) { bestError = error; best = f; flipped = opposite < same; }
                    }
                    if (best < 0 || bestError > tolerance) { ok = false; break; }
                    span.pieceId = to.id;
                    span.edgeId = to.edges[best].id;
                    if (flipped) { float s0 = span.start; span.start = 1f - span.end; span.end = 1f - s0; seam.reversed = !seam.reversed; }
                    if (side == 0) seam.a = span; else seam.b = span;
                }
                if (!ok) { pattern.seams.Remove(seam); removed++; }
            }
            return removed;
        }

        /// <summary>Gives a piece another id, and everything that refers to it (seams, tacks, parts, trims, sizes, mirrors and copies of it).</summary>
        private static void ReplacePieceId(ClothesPattern pattern, PatternPiece piece, string newId)
        {
            string old = piece.id;
            foreach (PatternSeam s in pattern.seams)
            {
                if (s.a.pieceId == old) s.a.pieceId = newId;
                if (s.b.pieceId == old) s.b.pieceId = newId;
            }
            foreach (PatternTack t in pattern.tacks) { if (t.pieceA == old) t.pieceA = newId; if (t.pieceB == old) t.pieceB = newId; }
            foreach (PatternPart part in pattern.parts) if (part.pieceId == old) part.pieceId = newId;
            foreach (PatternTrim trim in pattern.trims) if (trim != null && trim.pieceId == old) trim.pieceId = newId;
            foreach (PatternDimension dim in pattern.dimensions) for (int i = 0; i < dim.pieceIds.Count; i++) if (dim.pieceIds[i] == old) dim.pieceIds[i] = newId;
            foreach (PatternPiece other in pattern.pieces)
            {
                if (other == null) continue;
                if (other.mirrorOf == old) other.mirrorOf = newId;
                if (other.duplicateOf == old) other.duplicateOf = newId;
            }
            piece.id = newId;
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
