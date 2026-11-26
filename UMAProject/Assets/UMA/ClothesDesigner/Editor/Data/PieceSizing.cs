using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Resizes a piece to a width and length as cut on a body (the Scene view's Size tool, and its panel fields).
    /// Where one of the pattern's sizes sets the piece's width or length, a scaled outline would only be sized back by
    /// it, so that size is changed instead (its allowance, or its length when it is a fixed length) and every piece it
    /// sizes changes with it: both sleeves, a front and a back. Otherwise the piece's outline is scaled, with what is
    /// drawn on it, about its middle across and its top (the top is where it is laid on the body, so the hem moves);
    /// on a linked mirror its original is scaled and the mirror follows, and the pieces that share a size with it (the
    /// other half of a belt, the other sleeve) are scaled the same way, so the seams between them still match.
    /// </summary>
    public static class PieceSizing
    {
        /// <summary>The most a piece is made larger, or smaller, in one change.</summary>
        public const float MaxFactor = 4f;
        /// <summary>The narrowest or shortest a piece is cut (cm).</summary>
        public const float MinCm = 1f;

        /// <summary>What a resize changed.</summary>
        public sealed class Result
        {
            public bool changed;
            public string message;
            /// <summary>Pieces whose outline was scaled, by index: their bounds as drawn before and after.</summary>
            public readonly Dictionary<int, (Rect before, Rect after)> rescaled = new Dictionary<int, (Rect, Rect)>();
            /// <summary>The sizes changed instead of an outline.</summary>
            public readonly List<PatternDimension> sizes = new List<PatternDimension>();

            /// <summary>Where a point of a piece, as drawn now, was drawn before the resize.</summary>
            public Vector2 Before(int piece, Vector2 drawn) =>
                rescaled.TryGetValue(piece, out (Rect before, Rect after) b) ? MapBounds(b.after, b.before, drawn) : drawn;
        }

        /// <summary>A point in one rectangle moved to the same place in another, across and up separately.</summary>
        public static Vector2 MapBounds(Rect from, Rect to, Vector2 p) =>
            new Vector2(to.xMin + (p.x - from.xMin) * (from.width > 1e-6f ? to.width / from.width : 1f),
                        to.yMin + (p.y - from.yMin) * (from.height > 1e-6f ? to.height / from.height : 1f));

        /// <summary>A piece's width and length as cut on a body (cm): its outline's bounds, sized by the pattern's sizes.</summary>
        public static Vector2 CutSize(ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, int pieceIndex) =>
            CutSize(pattern, pattern != null ? PatternGeometry.Evaluate(pattern, body, fit, null) : null, pieceIndex);

        /// <summary>A piece's width and length as cut (cm), with the pieces' scales already worked out (a built mesh's).</summary>
        public static Vector2 CutSize(ClothesPattern pattern, PieceScale[] scales, int pieceIndex)
        {
            if (pattern == null || pieceIndex < 0 || pieceIndex >= pattern.pieces.Count) return Vector2.zero;
            Rect b = pattern.pieces[pieceIndex].Bounds();
            PieceScale s = scales != null && pieceIndex < scales.Length ? scales[pieceIndex] : PieceScale.Identity;
            return new Vector2(b.width * s.sx, b.height * s.sy);
        }

        /// <summary>
        /// The size of a kind that sets a piece's width or length: the last one listing it, as sizes of a kind are applied
        /// in order and the last wins. Null when none does.
        /// </summary>
        public static PatternDimension SizeFor(ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, int pieceIndex, DimensionKind kind)
        {
            if (pattern == null || pieceIndex < 0 || pieceIndex >= pattern.pieces.Count) return null;
            string id = pattern.pieces[pieceIndex].id;
            PatternDimension found = null;
            foreach (PatternDimension d in pattern.dimensions)
            {
                if (d == null || d.kind != kind || d.pieceIds == null || !d.pieceIds.Contains(id)) continue;
                if (Total(d, fit, body) <= 0f) continue;
                found = d;
            }
            return found;
        }

        /// <summary>The other pieces a size sets, by name.</summary>
        public static List<string> OthersSizedBy(ClothesPattern pattern, PatternDimension size, int pieceIndex)
        {
            var names = new List<string>();
            if (pattern == null || size == null) return names;
            string id = pieceIndex >= 0 && pieceIndex < pattern.pieces.Count ? pattern.pieces[pieceIndex].id : null;
            foreach (string other in size.pieceIds)
            {
                if (other == id) continue;
                PatternPiece piece = pattern.FindPiece(other);
                if (piece != null) names.Add(piece.name);
            }
            return names;
        }

        /// <summary>
        /// The pieces resized with this one when its outline is scaled across (<see cref="DimensionKind.Width"/>) or down
        /// (<see cref="DimensionKind.Length"/>): the ones that share a size with it (both sleeves, a belt's two halves),
        /// that no size sets that way themselves, and that are not this piece's own linked mirror or original. By index.
        /// </summary>
        public static List<int> MatchingPieces(ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, int pieceIndex, DimensionKind kind)
        {
            var found = new List<int>();
            if (pattern == null || pieceIndex < 0 || pieceIndex >= pattern.pieces.Count) return found;
            PatternPiece piece = pattern.pieces[pieceIndex];
            PatternPiece original = Original(pattern, piece);
            foreach (PatternDimension d in pattern.dimensions)
            {
                if (d == null || d.pieceIds == null || !d.pieceIds.Contains(piece.id)) continue;
                foreach (string id in d.pieceIds)
                {
                    int other = pattern.IndexOfPiece(id);
                    if (other < 0 || other == pieceIndex || found.Contains(other)) continue;
                    if (Original(pattern, pattern.pieces[other]) == original) continue;
                    if (SizeFor(pattern, fit, body, other, kind) != null) continue;
                    found.Add(other);
                }
            }
            return found;
        }

        /// <summary>The piece a linked mirror follows, or the piece itself.</summary>
        private static PatternPiece Original(ClothesPattern pattern, PatternPiece piece)
        {
            if (piece == null || !piece.mirrorLinked || string.IsNullOrEmpty(piece.mirrorOf)) return piece;
            PatternPiece original = pattern.FindPiece(piece.mirrorOf);
            return original != null ? original : piece;
        }

        /// <summary>
        /// Makes a piece <paramref name="widthCm"/> wide and <paramref name="lengthCm"/> long as cut on the body (pass its
        /// current width or length to leave that alone). With <paramref name="matching"/>, a scaled outline scales the
        /// <see cref="MatchingPieces"/> the same way, so a set's seams still match. The caller records Undo for the pattern
        /// and the fit first, and builds the cloth again after: <see cref="Result.Before"/> carries a drape over.
        /// </summary>
        public static Result Resize(ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, int pieceIndex, float widthCm, float lengthCm, bool matching = true)
        {
            var result = new Result();
            if (pattern == null || pieceIndex < 0 || pieceIndex >= pattern.pieces.Count) { result.message = "Choose a piece first."; return result; }
            PatternPiece piece = pattern.pieces[pieceIndex];
            PieceScale[] scales = PatternGeometry.Evaluate(pattern, body, fit, null);
            Vector2 was = CutSize(pattern, scales, pieceIndex);
            if (was.x <= 1e-3f || was.y <= 1e-3f) { result.message = "'" + piece.name + "' has no outline to resize."; return result; }
            float kx = Factor(was.x, widthCm), ky = Factor(was.y, lengthCm);
            var notes = new List<string>();
            float outlineX = 1f, outlineY = 1f;
            foreach (DimensionKind kind in new[] { DimensionKind.Width, DimensionKind.Length })
            {
                float k = kind == DimensionKind.Width ? kx : ky;
                if (Mathf.Abs(k - 1f) < 1e-4f) continue;
                PatternDimension size = SizeFor(pattern, fit, body, pieceIndex, kind);
                if (size != null && ScaleSize(pattern, fit, body, size, pieceIndex, kind, k, scales, out string note))
                {
                    result.sizes.Add(size);
                    notes.Add(note);
                }
                else if (kind == DimensionKind.Width) outlineX = k;
                else outlineY = k;
            }
            if (outlineX != 1f || outlineY != 1f) ScaleOutline(pattern, fit, body, pieceIndex, outlineX, outlineY, matching, result, notes);
            result.changed = result.sizes.Count > 0 || result.rescaled.Count > 0;
            if (!result.changed) { result.message = "'" + piece.name + "' is already " + Cm(was.x) + " x " + Cm(was.y) + " cm."; return result; }
            Vector2 now = CutSize(pattern, fit, body, pieceIndex);
            result.message = "'" + piece.name + "' is now " + Cm(now.x) + " cm wide and " + Cm(now.y) + " cm long (it was " + Cm(was.x) + " x " + Cm(was.y) + " cm). "
                + string.Join(" ", notes);
            result.message = result.message.TrimEnd();
            return result;
        }

        /// <summary>
        /// What <see cref="Resize"/> would do, without changing anything (it is worked out on copies of the pattern and the
        /// fit, in 2 to 4 ms for the samples): each piece's factor across and down as cut, exactly one where it would not
        /// change, and the message it would give. The Size tool shows it on the cloth while dragging.
        /// </summary>
        public static Vector2[] Preview(ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, int pieceIndex, float widthCm, float lengthCm, bool matching, out string message)
        {
            message = null;
            if (pattern == null) return null;
            ClothesPattern copy = Object.Instantiate(pattern);
            copy.hideFlags = HideFlags.HideAndDontSave;
            ClothesFit fitCopy = null;
            if (fit != null)
            {
                fitCopy = Object.Instantiate(fit);
                fitCopy.hideFlags = HideFlags.HideAndDontSave;
                fitCopy.pattern = copy;
            }
            try
            {
                PieceScale[] before = PatternGeometry.Evaluate(pattern, body, fit, null);
                Result result = Resize(copy, fitCopy, body, pieceIndex, widthCm, lengthCm, matching);
                message = result.message;
                PieceScale[] after = PatternGeometry.Evaluate(copy, body, fitCopy, null);
                var factors = new Vector2[pattern.pieces.Count];
                for (int i = 0; i < factors.Length; i++)
                {
                    Rect b0 = pattern.pieces[i].Bounds(), b1 = copy.pieces[i].Bounds();
                    float w0 = b0.width * before[i].sx, h0 = b0.height * before[i].sy;
                    float kx = w0 > 1e-6f ? b1.width * after[i].sx / w0 : 1f, ky = h0 > 1e-6f ? b1.height * after[i].sy / h0 : 1f;
                    factors[i] = new Vector2(Mathf.Abs(kx - 1f) < 1e-5f ? 1f : kx, Mathf.Abs(ky - 1f) < 1e-5f ? 1f : ky);
                }
                return factors;
            }
            finally
            {
                Object.DestroyImmediate(copy);
                if (fitCopy != null) Object.DestroyImmediate(fitCopy);
            }
        }

        private static string Cm(float v) => v.ToString("0.0");

        /// <summary>How much larger a size becomes to reach a target: kept within <see cref="MaxFactor"/> and <see cref="MinCm"/>; 1 for no target.</summary>
        private static float Factor(float now, float target)
        {
            if (!(target > 0f) || now <= 1e-3f) return 1f;
            float k = Mathf.Max(target, MinCm) / now;
            return Mathf.Clamp(k, 1f / MaxFactor, MaxFactor);
        }

        /// <summary>A size's whole length or girth on the body (cm): the measurement plus the allowance in effect, or its fixed length.</summary>
        public static float Total(PatternDimension size, ClothesFit fit, BodyLandmarks body)
        {
            float measured = PatternGeometry.MeasurementCm(body, size.measurement);
            bool fromBody = size.measurement != BodyMeasurement.None && measured > 0f;
            float? own = fit != null ? fit.AllowanceFor(size.id) : null;
            return fromBody ? measured + (own ?? size.allowanceCm) : size.fixedCm;
        }

        /// <summary>
        /// Scales a size so the piece's cut width or length grows by <paramref name="k"/>: the allowance in effect (the fit's
        /// own when it has one, else the pattern's), or the fixed length. Undone, and false, when the size turns out not to
        /// set this piece (a width line that misses it).
        /// </summary>
        private static bool ScaleSize(ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, PatternDimension size, int pieceIndex, DimensionKind kind, float k, PieceScale[] scales, out string note)
        {
            note = null;
            float measured = PatternGeometry.MeasurementCm(body, size.measurement);
            bool fromBody = size.measurement != BodyMeasurement.None && measured > 0f;
            AllowanceOverride own = null;
            if (fit != null) foreach (AllowanceOverride o in fit.allowances) if (o != null && o.dimensionId == size.id) own = o;
            float total = Total(size, fit, body);
            float wanted = total * k;
            if (wanted <= 0.5f) return false;
            float oldAllowance = own != null ? own.allowanceCm : size.allowanceCm, oldFixed = size.fixedCm;
            if (fromBody)
            {
                float allowance = Mathf.Round((wanted - measured) * 10f) / 10f;
                if (own != null) own.allowanceCm = allowance; else size.allowanceCm = allowance;
            }
            else size.fixedCm = Mathf.Round(wanted * 10f) / 10f;
            PieceScale before = scales[pieceIndex];
            PieceScale after = PatternGeometry.Evaluate(pattern, body, fit, null)[pieceIndex];
            float grew = kind == DimensionKind.Width ? after.sx / Mathf.Max(1e-6f, before.sx) : after.sy / Mathf.Max(1e-6f, before.sy);
            if (Mathf.Abs(grew - k) > 0.02f * k)
            {
                if (own != null) own.allowanceCm = oldAllowance; else size.allowanceCm = oldAllowance;
                size.fixedCm = oldFixed;
                return false;
            }
            List<string> others = OthersSizedBy(pattern, size, pieceIndex);
            string what = fromBody
                ? (own != null ? "this fit's allowance for it went from " : "its allowance went from ") + Cm(oldAllowance) + " to " + Cm(own != null ? own.allowanceCm : size.allowanceCm) + " cm"
                : "its fixed size went from " + Cm(oldFixed) + " to " + Cm(size.fixedCm) + " cm";
            note = "Its " + (kind == DimensionKind.Width ? "width" : "length") + " is set by the size '" + size.name + "', so " + what
                + (others.Count > 0 ? ", and " + string.Join(", ", others) + " changed with it." : ".");
            return true;
        }

        /// <summary>
        /// Scales a piece's outline and what is drawn on it (lines, darts, pleats, tacks, parts, and the fit's pins and
        /// paint on it) about its middle across and its top. A linked mirror's original is scaled, and the mirror follows.
        /// With <paramref name="matching"/>, so are the <see cref="MatchingPieces"/>, each the ways no size sets it.
        /// </summary>
        private static void ScaleOutline(ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, int pieceIndex, float kx, float ky, bool matching, Result result, List<string> notes)
        {
            PatternPiece resized = pattern.pieces[pieceIndex];
            PatternPiece target = Original(pattern, resized);
            if (target != resized) notes.Add("It is a linked mirror of '" + target.name + "', so that was resized and it follows.");
            // Each piece to scale (a mirror's original), and how much across and down.
            var targets = new List<(PatternPiece piece, float kx, float ky)> { (target, kx, ky) };
            var matched = new List<string>();
            if (matching)
            {
                List<int> across = kx != 1f ? MatchingPieces(pattern, fit, body, pieceIndex, DimensionKind.Width) : new List<int>();
                List<int> down = ky != 1f ? MatchingPieces(pattern, fit, body, pieceIndex, DimensionKind.Length) : new List<int>();
                var others = new List<int>(across);
                foreach (int i in down) if (!others.Contains(i)) others.Add(i);
                foreach (int i in others)
                {
                    PatternPiece other = Original(pattern, pattern.pieces[i]);
                    if (targets.Exists(t => t.piece == other)) continue;
                    targets.Add((other, across.Contains(i) ? kx : 1f, down.Contains(i) ? ky : 1f));
                    matched.Add(pattern.pieces[i].name);
                }
            }
            if (matched.Count > 0)
                notes.Add((matched.Count == 1 ? "'" + matched[0] + "' shares a size with it and was" : string.Join(", ", matched.ConvertAll(m => "'" + m + "'")) + " share a size with it and were")
                    + " resized the same way, so their seams still match.");
            var before = new Rect[pattern.pieces.Count];
            for (int i = 0; i < before.Length; i++) before[i] = pattern.pieces[i].Bounds();
            var seamBefore = new float[pattern.seams.Count];
            for (int s = 0; s < seamBefore.Length; s++) seamBefore[s] = PatternEditing.SeamMismatchCm(pattern, pattern.seams[s]);

            foreach ((PatternPiece scaled, float sx, float sy) in targets) ScalePieceGeometry(scaled, sx, sy);
            PatternEditing.UpdateLinkedMirrors(pattern);

            for (int i = 0; i < before.Length; i++)
            {
                PatternPiece piece = pattern.pieces[i];
                Rect after = piece.Bounds();
                if (SameRect(before[i], after)) continue;
                result.rescaled[i] = (before[i], after);
                Rect was = before[i];
                Vector2 Move(Vector2 p) => MapBounds(was, after, p);
                foreach (PatternTack t in pattern.tacks)
                {
                    if (t == null) continue;
                    if (t.pieceA == piece.id) t.a = Move(t.a);
                    if (t.pieceB == piece.id) t.b = Move(t.b);
                }
                foreach (PatternPart part in pattern.parts) if (part != null && part.pieceId == piece.id) part.position = Move(part.position);
                if (fit == null) continue;
                foreach (ClothPin pin in fit.pins) if (pin != null && pin.pieceId == piece.id) pin.flatPosition = Move(pin.flatPosition);
                foreach (FitPaintPoint paint in fit.paint) if (paint != null && paint.pieceId == piece.id) paint.flatPosition = Move(paint.flatPosition);
            }

            // Seams this made unequal, by the pieces they join (a belt's two side seams are one entry): the longer side gathers.
            var uneven = new List<(string key, string pair, float worst, int count)>();
            for (int s = 0; s < seamBefore.Length; s++)
            {
                PatternSeam seam = pattern.seams[s];
                float now = PatternEditing.SeamMismatchCm(pattern, seam);
                if (Mathf.Abs(now) < 0.5f || Mathf.Abs(now - seamBefore[s]) < 0.3f) continue;
                PatternPiece a = pattern.FindPiece(seam.a.pieceId), b = pattern.FindPiece(seam.b.pieceId);
                if (a == null || b == null) continue;
                // The resized piece named first, whichever side of the seam it is on.
                if (result.rescaled.ContainsKey(pattern.pieces.IndexOf(b)) && !result.rescaled.ContainsKey(pattern.pieces.IndexOf(a))) (a, b) = (b, a);
                string key = string.CompareOrdinal(a.id, b.id) < 0 ? a.id + "|" + b.id : b.id + "|" + a.id;
                int k = uneven.FindIndex(u => u.key == key);
                if (k < 0) uneven.Add((key, a.name + " to " + b.name, Mathf.Abs(now), 1));
                else uneven[k] = (key, uneven[k].pair, Mathf.Max(uneven[k].worst, Mathf.Abs(now)), uneven[k].count + 1);
            }
            if (uneven.Count > 0)
            {
                var listed = new List<string>();
                for (int u = 0; u < uneven.Count && u < 3; u++)
                    listed.Add(uneven[u].pair + " (" + Cm(uneven[u].worst) + " cm" + (uneven[u].count > 1 ? ", " + uneven[u].count + " seams" : "") + ")");
                if (uneven.Count > 3) listed.Add("more");
                notes.Add("These seams' sides now differ in length: " + string.Join(", ", listed) + ". The longer side gathers when sewn; change the other piece to match if it should not.");
            }
        }

        private static bool SameRect(Rect a, Rect b) =>
            Mathf.Abs(a.xMin - b.xMin) < 1e-4f && Mathf.Abs(a.yMin - b.yMin) < 1e-4f && Mathf.Abs(a.xMax - b.xMax) < 1e-4f && Mathf.Abs(a.yMax - b.yMax) < 1e-4f;

        /// <summary>How much a length running in a direction grows when x is scaled by kx and y by ky.</summary>
        private static float Along(Vector2 direction, float kx, float ky)
        {
            if (direction.sqrMagnitude < 1e-12f) return Mathf.Sqrt(kx * ky);
            Vector2 d = direction.normalized;
            return new Vector2(d.x * kx, d.y * ky).magnitude;
        }

        /// <summary>
        /// Scales a piece's own geometry about its middle across and its top: its points, curve handles, lines, darts
        /// (their points, and their widths along their edge or across them) and pleats (depths along their edge, lengths
        /// into the piece).
        /// </summary>
        public static void ScalePieceGeometry(PatternPiece piece, float kx, float ky)
        {
            Rect bounds = piece.Bounds();
            float cx = bounds.center.x, top = bounds.yMax;
            Vector2 At(Vector2 p) => new Vector2(cx + (p.x - cx) * kx, top + (p.y - top) * ky);
            int n = piece.points.Count;
            // The directions of the edges as they were, for the darts and pleats on them.
            var edgeDirection = new Dictionary<string, Vector2>();
            for (int e = 0; e < piece.edges.Count && n > 1; e++)
            {
                PatternEdge edge = piece.edges[e];
                if (edge != null && edge.id != null) edgeDirection[edge.id] = piece.points[(e + 1) % n].position - piece.points[e].position;
            }
            foreach (PatternPoint p in piece.points) p.position = At(p.position);
            foreach (PatternEdge e in piece.edges)
            {
                e.handleA = new Vector2(e.handleA.x * kx, e.handleA.y * ky);
                e.handleB = new Vector2(e.handleB.x * kx, e.handleB.y * ky);
            }
            foreach (PatternLine line in piece.lines)
            {
                for (int i = 0; i < line.points.Count; i++) line.points[i] = At(line.points[i]);
            }
            foreach (PatternDart d in piece.darts)
            {
                if (d.kind == DartKind.Edge && d.edgeId != null && edgeDirection.TryGetValue(d.edgeId, out Vector2 along))
                {
                    d.widthCm *= Along(along, kx, ky);
                    d.curveCm *= Along(new Vector2(-along.y, along.x), kx, ky);
                }
                else if (d.kind != DartKind.Edge)
                {
                    Vector2 axis = d.tip2 - d.tip;
                    d.widthCm *= Along(new Vector2(-axis.y, axis.x), kx, ky);
                }
                d.tip = At(d.tip);
                d.tip2 = At(d.tip2);
            }
            foreach (PatternPleat pl in piece.pleats)
            {
                if (pl.edgeId == null || !edgeDirection.TryGetValue(pl.edgeId, out Vector2 along)) continue;
                float alongK = Along(along, kx, ky), intoK = Along(new Vector2(-along.y, along.x), kx, ky);
                pl.depthCm *= alongK;
                pl.boxWidthCm *= alongK;
                pl.stitchedCm *= intoK;
                pl.lengthCm *= intoK;
            }
        }
    }
}
