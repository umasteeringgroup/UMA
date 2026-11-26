using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    public static partial class GarmentGenerator
    {
        /// <summary>How much higher the back of trousers comes up at the centre than the front (cm), for room over the seat.</summary>
        private const float BackRise = 3f;

        /// <summary>Drafted trouser legs: the four pieces and their tops, for a waistband or a jumpsuit's top.</summary>
        private sealed class Legs
        {
            public PieceBuilder lf, rf, lb, rb;
            public float frontTop, backTop;
            /// <summary>The legs' top and hem, cm below the shoulders.</summary>
            public float top, hem;
        }

        /// <summary>
        /// How much longer than the body a top edge that must hold the garment up is cut (cm), at the narrowest below it:
        /// 3 cm shorter, worn snug as a waistband is. Measured on Human Male 3.0, whose waist-to-hip cone is shallow:
        /// at +1 a banded pleated skirt fell off, at -3 it stayed on the hips, and skirts and wool trousers sat 3 to 7 cm
        /// higher; on Human Female 3.0 nothing moved more than a centimetre.
        /// </summary>
        internal const float HoldEase = -3f;

        /// <summary>
        /// How long a top edge that holds a garment up is cut, at a height: 2 cm over the body there, and no longer than
        /// the narrowest the body gets below it (down to the hips) plus <see cref="HoldEase"/>.
        /// </summary>
        private static float Hold(GarmentBody body, float y) => Mathf.Min(body.TorsoGirth(y) + 2f, body.NarrowestBelow(y) + HoldEase);

        private static string[] SeamNames(string first, string second, string upper, int segments)
        {
            if (segments <= 1) return new[] { first };
            if (segments == 2) return new[] { first, upper };
            return new[] { first, second, upper };
        }

        /// <summary>
        /// Trousers: four pieces, each laid round its leg, as <see cref="SamplePatterns"/> cuts them, drafted to the
        /// body's hips, thighs and legs: the hips plus ease shared front and back, the thigh plus its own ease at the
        /// crotch line, narrowing (or flaring) to the hem. <paramref name="topOverride"/> (cm below the shoulders)
        /// sets the top for a jumpsuit, with no waistband; -1 for the style's own rise.
        /// </summary>
        private static Legs Trousers(Draft d, float topOverride)
        {
            GarmentStyle s = d.s;
            GarmentBody body = d.body;
            PatternBuilder b = d.b;
            bool jumpsuit = topOverride >= 0f;
            float garmentTop = jumpsuit ? topOverride : body.waistY + Mathf.Lerp(17f, 0f, s.rise);
            bool band = !jumpsuit && s.waistband != BandStyle.None;
            float bandHeight = band ? s.waistbandCm : 0f;
            float top = garmentTop + bandHeight;
            float hem = Mathf.Max(LegY(body, s.legLength), body.crotchY + 6f);
            bool cuffs = s.legCuffs != BandStyle.None;
            float cuffHeight = cuffs ? Mathf.Min(s.bandHeightCm, Mathf.Max(2f, (hem - body.crotchY) * 0.25f)) : 0f;
            float legHem = Mathf.Max(hem - cuffHeight, body.crotchY + 4f);
            float crotch = Mathf.Min(body.crotchY + 3f + Mathf.Max(0f, s.easeCm) * 0.05f, legHem - 2f);
            float hipLine = Mathf.Clamp(body.hipY, top + 6f, crotch - 1f);
            float Y(float bst) => legHem - bst;
            float inseamY = Y(crotch), topY = Y(top), hipY = Y(hipLine);

            float hipTarget = body.hip + s.easeCm;
            float frontHip = 0.47f * hipTarget * 0.25f, backHip = 0.53f * hipTarget * 0.25f;
            float girthTop = body.TorsoGirth(top);
            // The top: wide enough to pull over the hips under a rib band, which gathers it in; under a band of its own
            // fabric with room (the contour band holds it); with no band, close to the body, as the band would be.
            float topCirc = s.waistband == BandStyle.Rib && !jumpsuit
                ? Mathf.Max(girthTop + 6f, hipTarget * 0.9f)
                : girthTop + (s.easeCm < 0f ? s.easeCm * 0.8f : band || jumpsuit ? Mathf.Max(4f, s.easeCm * 0.5f) : 2f);
            // With no band to hold it, the top itself must be no wider than the body anywhere below it, down to the hips.
            if (!band && !jumpsuit && s.waistband != BandStyle.Rib) topCirc = Mathf.Min(topCirc, Hold(body, top));
            topCirc = Mathf.Min(topCirc, hipTarget);
            float frontTop = 0.47f * topCirc * 0.25f, backTop = 0.53f * topCirc * 0.25f;
            float frontCrotch = 0.05f * hipTarget, backCrotch = 0.09f * hipTarget;

            float thighTarget = body.thigh + s.thighEaseCm;
            float frontOut = 0.445f * thighTarget - (frontHip + frontCrotch);
            float backOut = 0.555f * thighTarget - (backHip + backCrotch);
            float girthHem = body.LegGirth(legHem);
            float skinny = girthHem + (s.thighEaseCm < 0f ? 2f + s.thighEaseCm * 0.4f : 4f + s.thighEaseCm * 0.25f);
            float hemCirc = s.hemWidth <= 1f ? Mathf.Lerp(skinny, Mathf.Max(skinny, thighTarget), s.hemWidth) : thighTarget * (1f + (s.hemWidth - 1f) * 1.2f);
            hemCirc = Mathf.Max(hemCirc, girthHem + (s.thighEaseCm < 0f ? s.thighEaseCm * 0.5f : 2f));
            float frontHem = 0.47f * hemCirc * 0.5f, backHem = 0.53f * hemCirc * 0.5f;

            bool thigh = legHem - crotch > 8f;
            bool knee = legHem > body.kneeY + 8f && body.kneeY > crotch + 10f;
            float kneeY = knee ? Y(body.kneeY) : 0f, kneeFront = 0f, kneeBack = 0f;
            if (knee)
            {
                float f = (body.kneeY - crotch) / (legHem - crotch);
                float kneeCirc = Mathf.Max(body.LegGirth(body.kneeY) + (s.thighEaseCm < 0f ? 2f + s.thighEaseCm * 0.4f : 3f + s.thighEaseCm * 0.4f),
                    Mathf.Lerp(thighTarget, Mathf.Min(hemCirc, thighTarget), f));
                kneeFront = 0.47f * kneeCirc * 0.5f;
                kneeBack = 0.53f * kneeCirc * 0.5f;
            }
            int outSegments = 1 + (knee ? 1 : 0) + (thigh ? 1 : 0);
            string[] outseam = SeamNames("Outseam", "Outseam 2", "Upper outseam", outSegments);
            string[] inseam = SeamNames("Inseam", null, "Upper inseam", knee ? 2 : 1);
            // The lower leg hangs under the middle of the thigh, so the inseam and the outseam slope alike and the
            // front's inseam is as long as the back's (centred on the crease instead, a short leg's back inseam came
            // out nearly twice its front's).
            float frontCentre = 0.5f * (frontOut - frontHip - frontCrotch), backCentre = 0.5f * (backHip + backCrotch - backOut);

            // Left front: the outside of the leg to the right as drawn (the body's left).
            PieceBuilder lf = b.Piece("Left Front").Point(frontCentre - frontHem, 0f, "Hem", !cuffs);
            int o = 0;
            lf.Point(frontCentre + frontHem, 0f, outseam[o++]);
            if (knee) lf.Point(frontCentre + kneeFront, kneeY, outseam[o++]);
            if (thigh) lf.Point(frontOut, inseamY, outseam[o++]);
            lf.Point(frontHip, hipY, "Hip curve")
                .Point(frontTop, topY, "Waist", !band && !jumpsuit)
                .Point(-frontTop, topY, "Center")
                .Point(-frontHip, hipY, "Crotch curve")
                .Point(-frontHip - frontCrotch, inseamY, inseam[inseam.Length - 1]);
            if (knee) lf.Point(frontCentre - kneeFront, kneeY, inseam[0]);
            float hipRise = topY - hipY, crotchDrop = hipY - inseamY;
            lf.Curve("Hip curve", 0f, 0.45f * hipRise, (frontHip - frontTop) * 0.4f, -0.33f * hipRise);
            lf.Curve("Crotch curve", 0f, -0.6f * crotchDrop, 0.5f * frontCrotch, 0.07f * crotchDrop);
            Smooth(lf, outseam);
            if (knee) Smooth(lf, new[] { inseam[1], inseam[0] });
            float topRel = -(top - body.waistY);
            lf.Place(BodyPart.LeftLeg, 0f, topRel).Fabric(s.fabric);

            // Left back, seen from behind: the inside of the leg to the right.
            PieceBuilder lb = b.Piece("Left Back").Point(backCentre - backHem, 0f, "Hem", !cuffs);
            lb.Point(backCentre + backHem, 0f, inseam[0]);
            if (knee) lb.Point(backCentre + kneeBack, kneeY, inseam[1]);
            lb.Point(backHip + backCrotch, inseamY, "Crotch curve")
                .Point(backHip, hipY, "Center")
                .Point(backTop, topY + BackRise, "Waist", !band && !jumpsuit)
                .Point(-backTop, topY, "Hip curve");
            o = outSegments - 1;
            lb.Point(-backHip, hipY, outseam[o--]);
            if (thigh) lb.Point(-backOut, inseamY, outseam[o--]);
            if (knee) lb.Point(backCentre - kneeBack, kneeY, outseam[o--]);
            lb.Curve("Crotch curve", -0.55f * backCrotch, 0.07f * crotchDrop, 0f, -0.6f * crotchDrop);
            lb.Curve("Hip curve", -(backHip - backTop) * 0.4f, -0.33f * hipRise, 0f, 0.45f * hipRise);
            var backOutseam = new List<string>(outseam);
            backOutseam.Reverse();
            Smooth(lb, backOutseam);
            if (knee) Smooth(lb, inseam);
            lb.Place(BodyPart.LeftLeg, 180f, topRel + BackRise).Fabric(s.fabric);

            PieceBuilder rf = b.Mirror(lf, "Right Front").Place(BodyPart.RightLeg, 0f, topRel);
            PieceBuilder rb = b.Mirror(lb, "Right Back").Place(BodyPart.RightLeg, 180f, topRel + BackRise);
            foreach ((PieceBuilder f, PieceBuilder k) in new[] { (lf, lb), (rf, rb) })
            {
                foreach (string edge in outseam) b.Sew(f, edge, k, edge);
                foreach (string edge in inseam) b.Sew(f, edge, k, edge);
                b.Sew(f, "Hip curve", k, "Hip curve");
            }
            b.Sew(lf, "Center", rf, "Center");
            b.Sew(lf, "Crotch curve", rf, "Crotch curve");
            b.Sew(lb, "Center", rb, "Center");
            b.Sew(lb, "Crotch curve", rb, "Crotch curve");

            if (band)
            {
                float gather = s.waistband == BandStyle.Rib ? 0.72f : 1f;
                string fabric = BandFabric(s, s.waistband);
                float front = (lf.Length("Waist") + rf.Length("Waist")) * gather, back = (lb.Length("Waist") + rb.Length("Waist")) * gather;
                // A band in the garment's own fabric is cut to the body at both its edges (a contour waistband), the body
                // being narrower at its top: cut straight it stood 15 cm loose there, and wool trousers stretched it 16%
                // over the hips and fell to the knees (Human Female 3.0). A rib band gathers in instead.
                float topRound = s.waistband == BandStyle.Rib ? front + back : Mathf.Min(front + back, Hold(body, garmentTop));
                float shrink = topRound / (front + back);
                PieceBuilder bandFront = Band(b, "Front Waistband", front, bandHeight, fabric, front * shrink).Opening("Top").Place(BodyPart.Torso, 0f, -garmentTop);
                PieceBuilder bandBack = Band(b, "Back Waistband", back, bandHeight, fabric, back * shrink).Opening("Top").Place(BodyPart.Torso, 180f, -garmentTop + 2f);
                b.SewAlong(bandFront, "Bottom", (rf, "Waist"), (lf, "Waist"));
                b.SewAlong(bandBack, "Bottom", (lb, "Waist"), (rb, "Waist"));
                b.Sew(bandFront, "Right end", bandBack, "Left end");
                b.Sew(bandFront, "Left end", bandBack, "Right end");
            }
            if (cuffs)
            {
                float gather = s.legCuffs == BandStyle.Rib ? 0.8f : 1f;
                string fabric = BandFabric(s, s.legCuffs);
                float along = -(legHem - body.waistY);
                foreach ((PieceBuilder front, PieceBuilder back, BodyPart leg, string side) in new[] { (lf, lb, BodyPart.LeftLeg, "Left"), (rf, rb, BodyPart.RightLeg, "Right") })
                {
                    PieceBuilder cuffFront = Band(b, side + " Front Cuff", front.Length("Hem") * gather, cuffHeight, fabric).Opening("Bottom").Place(leg, 0f, along);
                    PieceBuilder cuffBack = Band(b, side + " Back Cuff", back.Length("Hem") * gather, cuffHeight, fabric).Opening("Bottom").Place(leg, 180f, along);
                    b.Sew(cuffFront, "Top", front, "Hem");
                    b.Sew(cuffBack, "Top", back, "Hem");
                    b.Sew(cuffFront, "Right end", cuffBack, "Left end");
                    b.Sew(cuffFront, "Left end", cuffBack, "Right end");
                }
            }

            WidthRule(b, "Hip (front)", BodyMeasurement.Hip, body.hip, 4, hipY, lf, rf);
            WidthRule(b, "Hip (back)", BodyMeasurement.Hip, body.hip, 4, hipY, lb, rb);
            if (s.legLength >= 2.5f) LengthRule(b, "Leg length", BodyMeasurement.Inseam, body.inseam, new List<PieceBuilder> { lf, rf, lb, rb });
            return new Legs { lf = lf, rf = rf, lb = lb, rb = rb, frontTop = frontTop, backTop = backTop, top = top, hem = legHem };
        }

        /// <summary>
        /// A skirt of two panels from the waist: snug at the waist, over the hips with room, flaring below them as far
        /// as the style asks; waist darts take in a fitted skirt, or pressed knife pleats a pleated one.
        /// </summary>
        private static void Skirt(Draft d)
        {
            GarmentStyle s = d.s;
            GarmentBody body = d.body;
            PatternBuilder b = d.b;
            float garmentTop = body.waistY + Mathf.Lerp(10f, 0f, s.rise);
            bool band = s.waistband != BandStyle.None;
            float bandHeight = band ? s.waistbandCm : 0f;
            float top = garmentTop + bandHeight;
            float hem = Mathf.Max(LengthY(body, s.length), top + 12f);
            float L = hem - top;
            float Y(float bst) => hem - bst;
            float ease = Mathf.Max(s.easeCm, 3f);
            // Snug at the top, and no wider than the body anywhere below it down to the hips, so it stays at the waist.
            float waistTarget = s.waistband == BandStyle.Rib ? body.TorsoGirth(top) + 8f : Hold(body, top);
            float hipTarget = body.hip + ease;
            bool hasHip = body.hipY > top + 5f && body.hipY < hem - 5f;
            float hipY = hasHip ? Y(body.hipY) : 0f;
            float hemCirc = hem > body.hipY
                ? hipTarget + s.flare * (hem - body.hipY) * 3.2f
                : body.TorsoGirth(hem) + ease;
            hemCirc = Mathf.Max(hemCirc, waistTarget);
            // A full skirt in four gores round the body, as the sample circle skirt is cut: in two panels each was so
            // wide that its corners were laid inside the knees and stayed there (Human Female 3.0, 8 points 37 mm deep).
            int count = s.pleats == 0 && s.flare > 0.5f ? 4 : 2;
            float share = 0.5f / count;
            string[] names = count == 4 ? new[] { "Front", "Left", "Back", "Right" } : new[] { "Front", "Back" };

            PieceBuilder Panel(string name, float angle)
            {
                PieceBuilder p;
                if (s.pleats > 0)
                {
                    const float depth = 3f;
                    float topHalf = waistTarget * share + s.pleats * depth;
                    float hemHalf = Mathf.Max(topHalf + L * 0.51f, hemCirc * share);
                    p = b.Piece(name)
                        .Point(-hemHalf, 0f, "Hem", true)
                        .Point(hemHalf, 0f, "Right side")
                        .Point(topHalf, L, "Waist", !band)
                        .Point(-topHalf, L, "Left side");
                    for (int i = 0; i < s.pleats; i++) p.Pleat("Waist", PleatKind.KnifeRight, (i + 0.5f) / s.pleats, depth, Mathf.Min(6f, L * 0.3f));
                }
                else
                {
                    float hipHalf = hipTarget * share;
                    float dart = 0f;
                    if (hasHip && s.flare < 0.4f)
                    {
                        float difference = hipHalf - waistTarget * share;
                        if (difference > 4f) dart = Mathf.Min(3f, difference * 0.35f);
                    }
                    float waistHalf = waistTarget * share + dart;
                    float hemHalf = Mathf.Max(hemCirc * share, hasHip ? 0f : waistHalf);
                    p = b.Piece(name).Point(-hemHalf, 0f, "Hem", true).Point(hemHalf, 0f, "Right side");
                    if (hasHip) p.Point(hipHalf, hipY, "Right hip");
                    p.Point(waistHalf, L, "Waist", !band).Point(-waistHalf, L, hasHip ? "Left hip" : "Left side");
                    if (hasHip)
                    {
                        p.Point(-hipHalf, hipY, "Left side");
                        float dh = L - hipY, dx = Mathf.Max(0f, hipHalf - waistHalf);
                        p.Curve("Right hip", 0f, 0.5f * dh, dx * 0.3f, -0.4f * dh);
                        p.Curve("Left hip", -dx * 0.3f, -0.4f * dh, 0f, 0.5f * dh);
                        HemCurve(p, s, new List<Level> { new Level(0f, hemHalf), new Level(hipY, hipHalf) });
                    }
                    if (dart > 0f)
                    {
                        float dartLength = Mathf.Min(13f, (L - hipY) * 0.75f);
                        p.Dart("Waist", 0.3f, dart, waistHalf * 0.4f, L - dartLength);
                        p.Dart("Waist", 0.7f, dart, -waistHalf * 0.4f, L - dartLength);
                    }
                }
                return p.Place(BodyPart.Torso, angle, -top).Fabric(s.fabric);
            }

            // Going round the body's left from the front: each panel's right side meets the next one's left.
            var panels = new PieceBuilder[count];
            for (int i = 0; i < count; i++) panels[i] = Panel(names[i], i * 360f / count);
            for (int i = 0; i < count; i++)
            {
                PieceBuilder next = panels[(i + 1) % count];
                b.Sew(panels[i], "Right side", next, "Left side");
                if (hasHip && s.pleats == 0) b.Sew(panels[i], "Right hip", next, "Left hip");
            }
            if (band)
            {
                float gather = s.waistband == BandStyle.Rib ? 0.85f : 1f;
                string fabric = BandFabric(s, s.waistband);
                float length = waistTarget * 2f * share * gather;
                float bandTop = s.waistband == BandStyle.Rib ? length : Mathf.Min(length, Hold(body, garmentTop) * 2f * share);
                var bands = new PieceBuilder[count];
                for (int i = 0; i < count; i++)
                {
                    bands[i] = Band(b, names[i] + " Waistband", length, bandHeight, fabric, bandTop).Opening("Top").Place(BodyPart.Torso, i * 360f / count, -garmentTop);
                    b.Sew(bands[i], "Bottom", panels[i], "Waist");
                }
                for (int i = 0; i < count; i++) b.Sew(bands[i], "Right end", bands[(i + 1) % count], "Left end");
            }
            if (hasHip && s.pleats == 0) WidthRule(b, "Hip", BodyMeasurement.Hip, body.hip, count, hipY, panels);
            else WidthRule(b, "Waist", BodyMeasurement.Waist, body.waist, count, L - 1f, panels);
        }

        /// <summary>A top sewn to trousers at the waist: the legs' tops set the top's hem, front and back.</summary>
        private static void Jumpsuit(Draft d)
        {
            GarmentStyle s = d.s;
            s.hemDropCm = 0f;
            s.hemBand = BandStyle.None;
            float waist = d.body.waistY + 2f;
            Legs legs = Trousers(d, waist);
            Bodice top = Top(d, waist, new Vector2(2f * legs.frontTop, 2f * legs.backTop));
            PatternBuilder b = d.b;
            if (top.Split)
            {
                b.Sew(top.leftFront, "Hem", legs.lf, "Waist");
                b.Sew(top.rightFront, "Hem", legs.rf, "Waist");
            }
            else b.SewAlong(top.front, "Hem", (legs.rf, "Waist"), (legs.lf, "Waist"));
            b.SewAlong(top.back, "Hem", (legs.lb, "Waist"), (legs.rb, "Waist"));
        }
    }
}
