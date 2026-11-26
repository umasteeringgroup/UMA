using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    public static partial class GarmentGenerator
    {
        /// <summary>How far the neck points sit above the top of the shoulders (cm): where torso pieces are placed from.</summary>
        private const float NeckAbove = 4f;

        /// <summary>A drafted bodice: its pieces and what they were cut to, for sleeves, collars, bands and a jumpsuit's legs.</summary>
        private sealed class Bodice
        {
            /// <summary>The front in one piece, or null when it is in two halves.</summary>
            public PieceBuilder front;
            /// <summary>The halves of an open or wrapped front: the body's left and right.</summary>
            public PieceBuilder leftFront, rightFront;
            public PieceBuilder back;
            /// <summary>Piece height (neck point to hem), the hem's height (cm below the shoulders), the underarm on the piece.</summary>
            public float length, hemY, underarmY;
            public float shoulderX, shoulderY, neckX, chestHalf;
            /// <summary>Where a half front's centre edge is (0 open, negative past the centre for a wrap).</summary>
            public float centreX;
            public bool sleeveless, square, hemOpen;
            public float hemHalfFront, hemHalfBack;
            public bool Split => front == null;
            public IEnumerable<PieceBuilder> Fronts => Split ? new[] { leftFront, rightFront } : new[] { front };
            public IEnumerable<PieceBuilder> All => Split ? new[] { leftFront, rightFront, back } : new[] { front, back };
        }

        /// <summary>
        /// A top, a dress, a kimono, or a jumpsuit's top. <paramref name="hemOverrideY"/> (cm below the shoulders) and
        /// <paramref name="hemHalves"/> set the hem for a jumpsuit, where the legs are sewn on; -1 and null for the style's own.
        /// </summary>
        private static Bodice Top(Draft d, float hemOverrideY, Vector2? hemHalves)
        {
            GarmentStyle s = d.s;
            GarmentBody body = d.body;
            var g = new Bodice { sleeveless = s.sleeve < 0.04f, square = s.neckline == NecklineShape.Square };
            float chest = body.chest + s.easeCm;
            bool banded = s.hemBand != BandStyle.None && hemOverrideY < 0f;
            g.hemOpen = !banded && hemOverrideY < 0f;
            // The armhole deepens with the ease, and for wide sleeves (a kimono's), so their caps can sew in.
            float underarmBst = body.chestY + 2f + Mathf.Max(0f, s.easeCm) * 0.08f
                + (g.sleeveless ? 1.5f : Mathf.Max(0f, s.sleeveEaseCm - 10f) * 0.2f);
            float hemBst = hemOverrideY >= 0f ? hemOverrideY : LengthY(body, s.length);
            float bandHeight = banded ? Mathf.Min(s.bandHeightCm, Mathf.Max(2f, (hemBst - underarmBst) * 0.3f)) : 0f;
            float clothHem = Mathf.Max(hemBst - bandHeight, underarmBst + 6f);
            g.hemY = clothHem;
            g.length = clothHem + NeckAbove;
            float Y(float bst) => clothHem - bst;

            g.chestHalf = chest * 0.25f;
            g.underarmY = Y(underarmBst);
            float neckR = body.neck / (2f * Mathf.PI);
            g.shoulderX = Mathf.Min(body.shoulderX + 0.5f + s.shoulderCm, g.chestHalf - 1f);
            // At its closest the neckline clears a round neck by 2 cm: necks are deeper than wide, and a turtleneck cut
            // 1.5 cm off sank 38 mm into the neck at the front (Human Female 3.0).
            g.neckX = Mathf.Min(Mathf.Lerp(neckR + 2f, g.shoulderX - 3f, s.neckWidth), g.shoulderX - 2.5f);
            g.neckX = Mathf.Max(g.neckX, 3f);
            if (g.sleeveless) g.shoulderX = Mathf.Min(g.shoulderX, g.neckX + s.strapCm);
            float drop = g.sleeveless ? 2f : 4.5f + Mathf.Max(0f, s.shoulderCm) * 0.3f;
            g.shoulderY = g.length - drop;
            g.underarmY = Mathf.Min(g.underarmY, g.shoulderY - 6f);
            float frontDepth = Mathf.Clamp(s.neckDepthCm, 1f, g.length - 4f);
            float backDepth = Mathf.Clamp(s.backNeckDepthCm, 1f, g.length - 4f);

            // Round the body at each level down the side: the chest plus ease at the underarm; the waist and the hips
            // as wide as the body there needs, or as the chest (a straight side), by the waist shaping.
            float localEase = s.easeCm >= 0f ? Mathf.Max(2f, s.easeCm * 0.6f) : s.easeCm * 0.8f;
            float Need(float bst) => body.TorsoGirth(bst) + localEase;
            float Width(float bst) => Mathf.Lerp(Mathf.Max(chest, Need(bst)), Need(bst), s.waistShaping);
            var levelsBst = new List<float>();
            levelsBst.Add(clothHem);
            bool hip = clothHem > body.hipY + 4f && body.hipY > underarmBst + 8f;
            bool waist = clothHem > body.waistY + 4f && body.waistY > underarmBst + 5f;
            if (hip) levelsBst.Add(body.hipY);
            if (waist) levelsBst.Add(body.waistY);
            levelsBst.Add(underarmBst);
            var circ = new float[levelsBst.Count];
            for (int i = 0; i < circ.Length; i++)
            {
                float bst = levelsBst[i];
                circ[i] = i == circ.Length - 1 ? chest : Width(bst);
            }
            // Below the hips the hem flares out as far as the style asks: at full flare each side seam slopes out 0.8 cm
            // for every centimetre down (steeper, the hem curve would turn back on itself).
            if (hip) circ[0] = Mathf.Max(circ[1], Need(clothHem)) + s.flare * (clothHem - body.hipY) * 3.2f;

            var levels = new List<Level>();
            for (int i = 0; i < circ.Length; i++) levels.Add(new Level(i == circ.Length - 1 ? g.underarmY : Y(levelsBst[i]), circ[i] * 0.25f));
            List<Level> backLevels = new List<Level>(levels);
            if (hemHalves.HasValue)
            {
                levels[0] = new Level(0f, hemHalves.Value.x);
                backLevels[0] = new Level(0f, hemHalves.Value.y);
            }
            g.hemHalfFront = levels[0].half;
            g.hemHalfBack = backLevels[0].half;

            // Waist darts in woven cloth fitted at the waist: half the shaping in each, the rest in the sides.
            float dartWidth = 0f, dartTop = 0f, dartBottom = 0f, dartWidest = 0.5f;
            int waistLevel = waist ? (hip ? 2 : 1) : -1;
            if (waistLevel > 0 && !Stretchy(s.fabric) && s.waistShaping > 0.3f && clothHem > body.waistY + 8f)
            {
                float reduction = g.chestHalf - levels[waistLevel].half;
                dartTop = g.underarmY - 3f;
                dartBottom = hip ? Mathf.Max(levels[1].y + 6f, 2f) : Mathf.Max(2f, levels[waistLevel].y - 10f);
                if (reduction > 3f && dartTop - dartBottom > 12f)
                {
                    dartWidth = Mathf.Min(3f, reduction * 0.4f);
                    dartWidest = Mathf.Clamp((dartTop - levels[waistLevel].y) / (dartTop - dartBottom), 0.2f, 0.8f);
                    levels[waistLevel] = new Level(levels[waistLevel].y, levels[waistLevel].half + dartWidth);
                    backLevels[waistLevel] = new Level(backLevels[waistLevel].y, backLevels[waistLevel].half + dartWidth);
                }
            }

            PatternBuilder b = d.b;
            if (s.front == FrontStyle.Closed)
            {
                g.front = TorsoPiece(d, g, "Front", levels, frontDepth, 0f);
                if (dartWidth > 0f) { AddDart(g.front, g.chestHalf * 0.45f, dartTop, dartBottom, dartWidth, dartWidest); AddDart(g.front, -g.chestHalf * 0.45f, dartTop, dartBottom, dartWidth, dartWidest); }
            }
            else
            {
                g.centreX = s.front == FrontStyle.Wrap ? -s.overlapCm : 0f;
                g.leftFront = HalfFront(d, g, levels, s.front == FrontStyle.Wrap ? Mathf.Max(frontDepth, 8f) : frontDepth);
                if (dartWidth > 0f) AddDart(g.leftFront, g.chestHalf * 0.45f, dartTop, dartBottom, dartWidth, dartWidest);
                g.rightFront = b.Mirror(g.leftFront, "Right Front");
                Rect bounds = g.leftFront.Piece.Bounds();
                float across = bounds.center.x;
                g.leftFront.Place(BodyPart.Torso, 0f, NeckAbove, s.front == FrontStyle.Wrap ? 0.8f : 0f, false, across).Fabric(s.fabric);
                g.rightFront.Place(BodyPart.Torso, 0f, NeckAbove, 0f, false, -across).Fabric(s.fabric);
                // Left over right, as a kimono is worn: the left front is kept outside where they cross.
                if (s.front == FrontStyle.Wrap) g.leftFront.Layer(1);
            }
            g.back = TorsoPiece(d, g, "Back", backLevels, backDepth, 180f);
            if (dartWidth > 0f) { AddDart(g.back, g.chestHalf * 0.45f, dartTop, dartBottom, dartWidth, dartWidest); AddDart(g.back, -g.chestHalf * 0.45f, dartTop, dartBottom, dartWidth, dartWidest); }

            // Sides and shoulders.
            int sides = levels.Count - 1;
            for (int i = 0; i < sides; i++)
            {
                if (g.Split)
                {
                    b.Sew(g.leftFront, SideName("", i), g.back, SideName("Left", i));
                    b.Sew(g.rightFront, SideName("", i), g.back, SideName("Right", i));
                }
                else
                {
                    b.Sew(g.front, SideName("Right", i), g.back, SideName("Left", i));
                    b.Sew(g.front, SideName("Left", i), g.back, SideName("Right", i));
                }
            }
            if (g.Split)
            {
                b.Sew(g.leftFront, "Shoulder", g.back, "Left shoulder");
                b.Sew(g.rightFront, "Shoulder", g.back, "Right shoulder");
            }
            else
            {
                b.Sew(g.front, "Right shoulder", g.back, "Left shoulder");
                b.Sew(g.front, "Left shoulder", g.back, "Right shoulder");
            }

            if (s.front == FrontStyle.Wrap) WrapTie(d, g, body.waistY);
            if (!g.sleeveless) Sleeves(d, g);
            Collar(d, g);
            if (banded) HemBand(d, g, bandHeight);

            // Sizes that follow the body: the chest round the underarm, the length from the shoulders to the waist.
            float line = g.underarmY - 3f;
            if (g.Split)
            {
                WidthRule(b, "Chest (fronts)", BodyMeasurement.Chest, body.chest, 4, line, g.leftFront, g.rightFront);
                WidthRule(b, "Chest (back)", BodyMeasurement.Chest, body.chest, 2, line, g.back);
            }
            else WidthRule(b, "Chest", BodyMeasurement.Chest, body.chest, 2, line, g.front, g.back);
            LengthRule(b, "Length", BodyMeasurement.ShoulderToWaist, body.waistY, new List<PieceBuilder>(g.All));
            return g;
        }

        /// <summary>The width of a piece across a line, as the sizes measure it: the outline's, less what darts take in.</summary>
        private static float DrawnWidth(PieceBuilder p, float y)
        {
            List<Vector2> outline = PatternGeometry.SampleOutline(p.Piece, 0.5f, null);
            if (!PatternGeometry.WidthAt(outline, y, out float minX, out float maxX)) return 0f;
            return maxX - minX - DartGeometry.IntakeAcross(p.Piece, y);
        }

        /// <summary>
        /// A width that follows a body measurement, its allowance set so the pieces keep the width they were drafted
        /// at on this body (the drafting already measured it) and change with it on another.
        /// </summary>
        private static void WidthRule(PatternBuilder b, string name, BodyMeasurement m, float measured, int share, float y, params PieceBuilder[] pieces)
        {
            // Every piece a rule lists is sized to the same width, so pieces drafted wider or narrower (a jumpsuit's
            // front and back, cut to the trousers under them) each get their own.
            var groups = new Dictionary<int, List<PieceBuilder>>();
            var widths = new Dictionary<int, float>();
            foreach (PieceBuilder p in pieces)
            {
                float drawn = DrawnWidth(p, y);
                if (drawn <= 0.5f) continue;
                int key = Mathf.RoundToInt(drawn * 10f);
                if (!groups.TryGetValue(key, out List<PieceBuilder> list)) { groups[key] = list = new List<PieceBuilder>(); widths[key] = drawn; }
                list.Add(p);
            }
            foreach (var kv in groups)
            {
                float drawn = widths[kv.Key];
                b.Width(name, m, drawn * share, drawn * share - measured, share, y, kv.Value.ToArray());
            }
        }

        /// <summary>A length that follows a body measurement; pieces of different heights each keep their own.</summary>
        private static void LengthRule(PatternBuilder b, string name, BodyMeasurement m, float measured, List<PieceBuilder> pieces)
        {
            var groups = new Dictionary<int, List<PieceBuilder>>();
            foreach (PieceBuilder p in pieces)
            {
                int key = Mathf.RoundToInt(p.Piece.Bounds().height * 10f);
                if (!groups.TryGetValue(key, out List<PieceBuilder> list)) groups[key] = list = new List<PieceBuilder>();
                list.Add(p);
            }
            foreach (List<PieceBuilder> list in groups.Values)
            {
                float h = list[0].Piece.Bounds().height;
                b.Length(name, m, h, h - measured, list.ToArray());
            }
        }

        private static void AddDart(PieceBuilder p, float x, float top, float bottom, float width, float widest)
        {
            p.DoubleDart(x, top, bottom, width);
            p.Piece.darts[p.Piece.darts.Count - 1].widestAt = widest;
        }

        /// <summary>A front or a back in one piece. Edges: Hem, Right side..., Right armhole, Right shoulder, the neckline, Left shoulder, Left armhole, Left side...</summary>
        private static PieceBuilder TorsoPiece(Draft d, Bodice g, string name, List<Level> levels, float depth, float angle)
        {
            float L = g.length;
            PieceBuilder p = d.b.Piece(name).Point(-levels[0].half, 0f, "Hem", g.hemOpen);
            var right = new List<string>();
            for (int i = 0; i < levels.Count; i++)
            {
                bool armhole = i == levels.Count - 1;
                string edge = armhole ? "Right armhole" : SideName("Right", i);
                p.Point(levels[i].half, levels[i].y, edge, armhole && g.sleeveless);
                if (!armhole) right.Add(edge);
            }
            p.Point(g.shoulderX, g.shoulderY, "Right shoulder");
            p.Point(g.neckX, L, g.square ? "Right neck side" : "Right neckline", true);
            if (g.square) p.Point(g.neckX, L - depth, "Right neckline", true);
            p.Point(0f, L - depth, "Left neckline", true);
            if (g.square) p.Point(-g.neckX, L - depth, "Left neck side", true);
            p.Point(-g.neckX, L, "Left shoulder");
            p.Point(-g.shoulderX, g.shoulderY, "Left armhole", g.sleeveless);
            var left = new List<string>();
            for (int i = levels.Count - 1; i >= 1; i--)
            {
                string edge = SideName("Left", i - 1);
                p.Point(-levels[i].half, levels[i].y, edge);
                left.Add(edge);
            }
            float h = g.shoulderY - g.underarmY, w = g.chestHalf - g.shoulderX;
            p.Curve("Right armhole", -0.7f * w, 0.1f * h, 0f, -0.4f * h);
            p.Curve("Left armhole", 0f, -0.4f * h, 0.7f * w, 0.1f * h);
            CurveNeckline(p, d.s.neckline, g.neckX, depth, "Right neckline", "Left neckline");
            HemCurve(p, d.s, levels);
            Smooth(p, right);
            Smooth(p, left);
            p.Place(BodyPart.Torso, angle, NeckAbove).Fabric(d.s.fabric);
            return p;
        }

        /// <summary>
        /// The left half of an open or wrapped front, drawn as the right half of a front (the body's left).
        /// Edges: Hem, Side..., Armhole, Shoulder, the neckline, Front edge.
        /// </summary>
        private static PieceBuilder HalfFront(Draft d, Bodice g, List<Level> levels, float depth)
        {
            float L = g.length, xc = g.centreX;
            PieceBuilder p = d.b.Piece("Left Front").Point(xc, 0f, "Hem", g.hemOpen);
            var side = new List<string>();
            for (int i = 0; i < levels.Count; i++)
            {
                bool armhole = i == levels.Count - 1;
                string edge = armhole ? "Armhole" : SideName("", i);
                p.Point(levels[i].half, levels[i].y, edge, armhole && g.sleeveless);
                if (!armhole) side.Add(edge);
            }
            p.Point(g.shoulderX, g.shoulderY, "Shoulder");
            bool wrap = d.s.front == FrontStyle.Wrap;
            p.Point(g.neckX, L, g.square && !wrap ? "Neck side" : "Neckline", true);
            if (g.square && !wrap) p.Point(g.neckX, L - depth, "Neckline", true);
            p.Point(xc, L - depth, "Front edge", true);
            float h = g.shoulderY - g.underarmY, w = g.chestHalf - g.shoulderX;
            p.Curve("Armhole", -0.7f * w, 0.1f * h, 0f, -0.4f * h);
            // A wrap's neckline runs straight down to where the fronts cross; an open front's has its own shape.
            if (!wrap) CurveNeckline(p, d.s.neckline, g.neckX - xc, depth, "Neckline", null);
            HemCurve(p, d.s, levels, true);
            Smooth(p, side);
            return p;
        }

        private static void CurveNeckline(PieceBuilder p, NecklineShape shape, float nx, float depth, string right, string left)
        {
            switch (shape)
            {
                case NecklineShape.Crew:
                case NecklineShape.Scoop:
                    p.Curve(right, -0.1f * nx, -0.55f * depth, 0.55f * nx, 0f);
                    if (left != null) p.Curve(left, -0.55f * nx, 0f, 0.1f * nx, -0.55f * depth);
                    break;
                case NecklineShape.Boat:
                    p.Curve(right, -0.05f * nx, -0.6f * depth, 0.6f * nx, 0f);
                    if (left != null) p.Curve(left, -0.6f * nx, 0f, 0.05f * nx, -0.6f * depth);
                    break;
            }
        }

        /// <summary>
        /// The hem of a piece that flares below its last level: its side corners rise until the side seam is no longer
        /// than the middle, and the hem curves down between them. Flared cloth hangs in folds that fall straight, so
        /// every point of the hem then hangs as low as the middle (with the corners left down, a gown flared from the
        /// hips hung 29 cm below the floor). A shirt tail drops the middle below the sides as well. On a half front
        /// the middle is the front edge.
        /// </summary>
        private static void HemCurve(PieceBuilder p, GarmentStyle s, List<Level> levels, bool half = false)
        {
            float rise = 0f;
            if (levels.Count > 1)
            {
                float dx = levels[0].half - levels[1].half, drop = levels[1].y - levels[0].y;
                if (dx > 0f && drop > 0f) rise = drop - Mathf.Sqrt(Mathf.Max(0f, drop * drop - dx * dx));
            }
            float tail = s.hemDropCm;
            if (rise + tail < 0.1f) return;
            int i = p.Index("Hem"), j = (i + 1) % p.Piece.points.Count;
            Vector2 a = p.Piece.points[i].position, c = p.Piece.points[j].position;
            float span = c.x - a.x;
            if (half)
            {
                // From the front edge (the middle, dropped by the tail) to the side corner (raised).
                p.Piece.points[i].position = new Vector2(a.x, a.y - tail);
                p.Piece.points[j].position = new Vector2(c.x, c.y + rise);
                p.Curve("Hem", span * 0.4f, 0f, -span * 0.3f, -(rise + tail) * 0.3f);
                return;
            }
            p.Piece.points[i].position = new Vector2(a.x, a.y + rise);
            p.Piece.points[j].position = new Vector2(c.x, c.y + rise);
            p.Curve("Hem", span * 0.2f, -(rise + tail) * 1.33f, -span * 0.2f, -(rise + tail) * 1.33f);
        }

        /// <summary>A wrapped front held closed at the waist with a tack under the outer front's edge.</summary>
        private static void WrapTie(Draft d, Bodice g, float waistBst)
        {
            float y = g.hemY - waistBst;
            if (y < 4f || y > g.length - 6f || d.s.overlapCm < 2f) return;
            PatternPiece outer = g.leftFront.Piece, inner = g.rightFront.Piece;
            float overlap = d.s.overlapCm;
            d.after.Add(pattern =>
            {
                // The outer front's edge is its leftmost side; on the inner (mirrored) front the same spot is
                // twice the overlap in from its edge, its rightmost side.
                Rect o = outer.Bounds(), n = inner.Bounds();
                PatternEditing.AddTack(pattern, outer, new Vector2(o.xMin + 1f, y), inner, new Vector2(n.xMax - (2f * overlap - 1f), y));
            });
        }

        // ---- Sleeves ----------------------------------------------------------------------------

        /// <summary>The length of a sleeve cap (both halves) of a half width and a height, as <see cref="Sleeves"/> draws it.</summary>
        private static float CapLength(float capHalf, float capHeight)
        {
            Vector2 p0 = new Vector2(capHalf, 0f), p3 = new Vector2(0f, capHeight);
            return 2f * CubicLength(p0, p0 + new Vector2(-0.25f * capHalf, 0.45f * capHeight), p3 + new Vector2(0.45f * capHalf, 0f), p3);
        }

        /// <summary>
        /// Set-in sleeves, their caps cut to the armholes' length plus a little ease (so they sew in flat), their hems
        /// round the arm where they end.
        /// </summary>
        private static void Sleeves(Draft d, Bodice g)
        {
            GarmentStyle s = d.s;
            GarmentBody body = d.body;
            PatternBuilder b = d.b;
            float capWidth = body.upperArm + s.sleeveEaseCm, capHalf = capWidth * 0.5f;
            PieceBuilder frontForRight = g.Split ? g.rightFront : g.front, frontForLeft = g.Split ? g.leftFront : g.front;
            string frontArmhole = g.Split ? "Armhole" : "Left armhole";
            float armhole = frontForRight.Length(frontArmhole) + g.back.Length("Right armhole");
            float target = armhole * 1.04f;
            float capHeight = 3f;
            if (CapLength(capHalf, capHeight) < target)
            {
                float lo = 3f, hi = 40f;
                for (int i = 0; i < 24; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (CapLength(capHalf, mid) < target) lo = mid; else hi = mid;
                }
                capHeight = 0.5f * (lo + hi);
            }
            float end = s.sleeve * body.armLength;
            float cuffHeight = s.cuffs != BandStyle.None ? Mathf.Min(s.bandHeightCm, Mathf.Max(2f, end * 0.3f)) : 0f;
            float total = Mathf.Max(capHeight + 2f, end + 3f - cuffHeight);
            float underarm = Mathf.Max(2f, total - capHeight);
            total = underarm + capHeight;
            float girth = body.ArmGirth(Mathf.Max(0f, total - 3f));
            float fitted = girth + 4f + 0.3f * s.sleeveEaseCm;
            float hem = Mathf.Lerp(fitted, capWidth * 1.7f, s.sleeveFlare);
            hem = Mathf.Clamp(hem, girth + 2f, capWidth * 1.8f);
            float hemHalf = hem * 0.5f;

            PieceBuilder right = b.Piece("Right Sleeve")
                .Point(-hemHalf, 0f, "Hem", s.cuffs == BandStyle.None)
                .Point(hemHalf, 0f, "Right underarm")
                .Point(capHalf, underarm, "Right cap")
                .Point(0f, underarm + capHeight, "Left cap")
                .Point(-capHalf, underarm, "Left underarm");
            right.Curve("Right cap", -0.25f * capHalf, 0.45f * capHeight, 0.45f * capHalf, 0f);
            right.Curve("Left cap", -0.45f * capHalf, 0f, 0.25f * capHalf, 0.45f * capHeight);
            right.Place(BodyPart.RightArm, 0f, 3f).Fabric(s.fabric);
            PieceBuilder left = b.Mirror(right, "Left Sleeve").Place(BodyPart.LeftArm, 0f, 3f);
            b.Sew(right, "Right underarm", right, "Left underarm");
            b.Sew(left, "Right underarm", left, "Left underarm");
            b.Sew(right, "Right cap", frontForRight, frontArmhole);
            b.Sew(right, "Left cap", g.back, "Right armhole");
            b.Sew(left, "Right cap", frontForLeft, g.Split ? "Armhole" : "Right armhole");
            b.Sew(left, "Left cap", g.back, "Left armhole");
            foreach (PieceBuilder f in g.Fronts)
            {
                if (g.Split) f.Opening("Armhole", false);
                else f.Opening("Left armhole", false).Opening("Right armhole", false);
            }
            g.back.Opening("Left armhole", false).Opening("Right armhole", false);
            WidthRule(b, "Sleeve", BodyMeasurement.UpperArm, body.upperArm, 1, underarm - 0.5f, right, left);
            if (s.sleeve >= 0.5f) b.Length("Sleeve length", BodyMeasurement.ArmLength, total, total - body.armLength, right, left);

            if (s.cuffs == BandStyle.None) return;
            float cuffLength = hem * (s.cuffs == BandStyle.Rib ? 0.82f : 1f);
            string fabric = BandFabric(s, s.cuffs);
            PieceBuilder rightCuff = Band(b, "Right Cuff", cuffLength, cuffHeight, fabric).Opening("Bottom").Place(BodyPart.RightArm, 0f, -(total - 3f));
            PieceBuilder leftCuff = b.Mirror(rightCuff, "Left Cuff").Place(BodyPart.LeftArm, 0f, -(total - 3f));
            foreach ((PieceBuilder cuff, PieceBuilder sleeve) in new[] { (rightCuff, right), (leftCuff, left) })
            {
                b.Sew(cuff, "Top", sleeve, "Hem");
                b.Sew(cuff, "Right end", cuff, "Left end");
            }
        }

        // ---- Collars and hoods -----------------------------------------------------------------------

        /// <summary>
        /// The neckline's edges in the order a collar runs along them (as the samples sew a collar): from the
        /// front's body-left half round the back to its body-right half, each half's edges taken from its far end.
        /// </summary>
        private static List<(PieceBuilder piece, string edge)> NecklinePath(Bodice g, bool wrap)
        {
            var path = new List<(PieceBuilder, string)>();
            bool square = g.square;
            bool halfSquare = square && !wrap;
            if (g.Split)
            {
                path.Add((g.leftFront, "Neckline"));
                if (halfSquare) path.Add((g.leftFront, "Neck side"));
            }
            else
            {
                path.Add((g.front, "Right neckline"));
                if (square) path.Add((g.front, "Right neck side"));
            }
            if (square) path.Add((g.back, "Left neck side"));
            path.Add((g.back, "Left neckline"));
            path.Add((g.back, "Right neckline"));
            if (square) path.Add((g.back, "Right neck side"));
            if (g.Split)
            {
                if (halfSquare) path.Add((g.rightFront, "Neck side"));
                path.Add((g.rightFront, "Neckline"));
            }
            else
            {
                if (square) path.Add((g.front, "Left neck side"));
                path.Add((g.front, "Left neckline"));
            }
            return path;
        }

        private static void Collar(Draft d, Bodice g)
        {
            GarmentStyle s = d.s;
            PatternBuilder b = d.b;
            if (s.collar == CollarStyle.None) return;
            bool wrap = s.front == FrontStyle.Wrap;
            List<(PieceBuilder piece, string edge)> path = NecklinePath(g, wrap);
            float length = 0f;
            foreach (var (piece, edge) in path) length += piece.Length(edge);
            if (s.collar == CollarStyle.Hood) { Hood(d, g, path); return; }
            if (s.collar == CollarStyle.Shirt) { ShirtCollar(d, g, path, length); return; }

            bool turtle = s.collar == CollarStyle.Turtleneck;
            float height = s.collarHeightCm;
            bool closed = turtle && !g.Split;
            string fabric = turtle && Stretchy(s.trimFabric) ? s.trimFabric : s.fabric;
            // A turtleneck is cut a little shorter than the neckline, so it stands close round the neck.
            float top = turtle ? Mathf.Max(d.body.neck + 4f, length * 0.9f) : length;
            float half = length * 0.5f, halfTop = Mathf.Min(top, length) * 0.5f;
            PieceBuilder collar = b.Piece("Collar")
                .Point(-half, 0f, "Neck")
                .Point(half, 0f, "Right end", !closed)
                .Point(halfTop, height, "Top", true)
                .Point(-halfTop, height, "Left end", !closed)
                .Fabric(fabric);
            // Open, laid round the back of the neck above the shoulders. Closed (a turtleneck), laid as a ring round the
            // neck and head, its ends close together at the front: laid round the back of the torso, sewing drew its
            // ends together through the throat and they stayed 44 mm inside it (Human Female 3.0).
            if (closed) collar.Place(BodyPart.Head, 180f, NeckAbove + height - d.body.headTopAbove);
            else collar.Place(BodyPart.Torso, 180f, 6f + height);
            b.SewAlong(collar, "Neck", path.ToArray());
            if (closed) b.Sew(collar, "Right end", collar, "Left end");
            foreach (var (piece, edge) in path) piece.Opening(edge, false);
        }

        /// <summary>A shirt collar in two parts, as <see cref="SamplePatterns"/>' Oxford shirt has it: a firm stand on the neckline and a leaf turned down over it.</summary>
        private static void ShirtCollar(Draft d, Bodice g, List<(PieceBuilder piece, string edge)> path, float length)
        {
            PatternBuilder b = d.b;
            // The leaf hangs 2.5 cm below the stand at the back. 1 cm, as a tailor would cut it, rode up on the cloth: over
            // the shirt-collar presets on both default bodies, 1.8 cm of stand showed below it on average and 3 of 15 drapes
            // flipped it up; with 2.5 cm, 0.3 cm and 1 of 17 (2026-11-24). The points hang at least 1 cm lower still.
            float standHeight = Mathf.Clamp(d.s.collarHeightCm * 0.6f, 2.5f, 5f), leafDepth = standHeight + 2.5f, pointLength = Mathf.Max(standHeight * 2f, leafDepth + 1f);
            const float rise = 2.5f;
            float half0 = length * 0.5f;
            float radius = (half0 * half0 + rise * rise) / (2f * rise);
            float phi = half0 / radius;
            Vector2 centre = new Vector2(0f, radius);
            Vector2 OnArc(float r, float a) => new Vector2(r * Mathf.Sin(a), radius - r * Mathf.Cos(a));
            float rt = radius - standHeight;
            PieceBuilder stand = b.Piece("Collar Stand")
                .Point(OnArc(radius, -phi).x, OnArc(radius, -phi).y, "Neck")
                .Point(OnArc(radius, phi).x, OnArc(radius, phi).y, "Right end", true)
                .Point(OnArc(rt, phi).x, OnArc(rt, phi).y, "Top")
                .Point(OnArc(rt, -phi).x, OnArc(rt, -phi).y, "Left end", true)
                .Arc("Neck", centre).Arc("Top", centre)
                .Place(BodyPart.Torso, 180f, 6f + standHeight + rise).Fabric(d.s.fabric)
                .Stiffen(InterfacingLevel.Firm, 3f);
            b.SewAlong(stand, "Neck", path.ToArray());
            foreach (var (piece, edge) in path) piece.Opening(edge, false);
            float half = stand.Length("Top") * 0.5f, flare = 1.5f;
            PieceBuilder leaf = b.Piece("Collar")
                .Point(-half - flare, -pointLength, "Outer edge", true)
                .Point(half + flare, -pointLength, "Right point", true)
                .Point(half, 0f, "Neck edge")
                .Point(-half, 0f, "Left point", true)
                .Curve("Outer edge", (half + flare) * 0.7f, (pointLength - leafDepth) * 1.33f, -(half + flare) * 0.7f, (pointLength - leafDepth) * 1.33f)
                .Place(BodyPart.Torso, 180f, stand.Piece.placement.topAboveShoulderCm, 1f)
                .Fabric(d.s.fabric)
                .Stiffen(InterfacingLevel.Light, 3f)
                .Layer(1);
            b.SewFolded(stand, "Top", leaf, "Neck edge");
            stand.Opening("Top", false);
        }

        /// <summary>A two-panel hood, each panel's neck cut to its half of the neckline and its height to the head.</summary>
        private static void Hood(Draft d, Bodice g, List<(PieceBuilder piece, string edge)> path)
        {
            PatternBuilder b = d.b;
            // The path runs body-left front, body-left back, body-right back, body-right front.
            int middle = path.Count / 2;
            var leftPath = path.GetRange(0, middle);
            var rightPath = path.GetRange(middle, path.Count - middle);
            float neck = 0f;
            foreach (var (piece, edge) in leftPath) neck += piece.Length(edge);
            // Drawn for a 24 cm neck edge and Human Female 3.0's head; sized to both.
            float sx = neck / 24f, sy = Mathf.Clamp(d.body.headHeight / 29.8f, 0.7f, 1.4f) * Mathf.Clamp(d.body.head / 63.6f, 0.8f, 1.3f);
            PieceBuilder hoodLeft = b.Piece("Hood Left")
                .Point(-12f * sx, 0f, "Neck")
                .Point(12f * sx, 0f, "Back seam")
                .Point(8f * sx, 30f * sy, "Crown")
                .Point(-13f * sx, 34f * sy, "Face", true)
                .Curve("Back seam", 6f * sx, 10f * sy, 6f * sx, -6f * sy)
                .Curve("Crown", -2f * sx, 8f * sy, 8f * sx, 4f * sy)
                .Curve("Face", 2f * sx, -10f * sy, -1f * sx, 10f * sy)
                .Place(BodyPart.Head, 90f, 4f).Fabric(d.s.fabric);
            PieceBuilder hoodRight = b.Mirror(hoodLeft, "Hood Right").Place(BodyPart.Head, -90f, 4f);
            b.Sew(hoodLeft, "Back seam", hoodRight, "Back seam");
            b.Sew(hoodLeft, "Crown", hoodRight, "Crown");
            b.SewAlong(hoodLeft, "Neck", leftPath.ToArray());
            b.SewAlong(hoodRight, "Neck", rightPath.ToArray());
            foreach (var (piece, edge) in path) piece.Opening(edge, false);
        }

        // ---- Hem band ---------------------------------------------------------------------------------

        private static void HemBand(Draft d, Bodice g, float height)
        {
            GarmentStyle s = d.s;
            PatternBuilder b = d.b;
            string fabric = BandFabric(s, s.hemBand);
            float gather = Gather(s.hemBand, true);
            float top = -g.hemY;
            PieceBuilder back = Band(b, "Back Band", g.back.Length("Hem") * gather, height, fabric).Opening("Bottom").Place(BodyPart.Torso, 180f, top);
            b.Sew(back, "Top", g.back, "Hem");
            if (g.Split)
            {
                float across = 0.5f * (g.centreX + g.hemHalfFront);
                PieceBuilder left = Band(b, "Left Front Band", g.leftFront.Length("Hem") * gather, height, fabric).Opening("Bottom").Opening("Left end")
                    .Place(BodyPart.Torso, 0f, top, 0f, false, across);
                PieceBuilder right = b.Mirror(left, "Right Front Band").Place(BodyPart.Torso, 0f, top, 0f, false, -across);
                if (s.front == FrontStyle.Wrap) left.Layer(1);
                b.Sew(left, "Top", g.leftFront, "Hem");
                b.Sew(right, "Top", g.rightFront, "Hem");
                b.Sew(left, "Right end", back, "Left end");
                b.Sew(right, "Right end", back, "Right end");
            }
            else
            {
                PieceBuilder front = Band(b, "Front Band", g.front.Length("Hem") * gather, height, fabric).Opening("Bottom").Place(BodyPart.Torso, 0f, top);
                b.Sew(front, "Top", g.front, "Hem");
                b.Sew(front, "Right end", back, "Left end");
                b.Sew(front, "Left end", back, "Right end");
            }
        }
    }
}
