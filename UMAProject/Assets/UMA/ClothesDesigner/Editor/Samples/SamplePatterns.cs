using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The sample patterns shipped in Clothes Designer Examples, built in code. Each is drafted in
    /// centimetres for the default Human Female 3.0 body (about 2 m tall: chest 100, waist 67, hip 117) and
    /// sized by the pattern's dimensions on other bodies. Pieces are drawn as seen from outside, counter-
    /// clockwise; seams join edges by name.
    /// </summary>
    public static class SamplePatterns
    {
        /// <summary>Every sample: its name and how to make it.</summary>
        public static readonly (string name, Func<ClothesPattern> make)[] All =
        {
            ("T-Shirt", TShirt),
            ("Long-Sleeve Tee", LongSleeveTee),
            ("Tank Top", TankTop),
            ("Crop Top", CropTop),
            ("Polo Shirt", PoloShirt),
            ("Forte Shirt", ForteShirt),
            ("Oxford Shirt", OxfordShirt),
            ("Hoodie", Hoodie),
            ("Short Pants", ShortPants),
            ("Jeans", Jeans),
            ("Sweatpants", Sweatpants),
            ("Leggings", Leggings),
            ("Boxer Shorts", BoxerShorts),
            ("Pleated Skirt", PleatedSkirt),
            ("A-Line Skirt", ALineSkirt),
            ("Pencil Skirt", PencilSkirt),
            ("Circle Skirt", CircleSkirt),
            ("Sheath Dress", SheathDress),
            ("Sundress", Sundress),
            ("Elastic Waist Dress", ElasticWaistDress),
            ("Belted Dress", BeltedDress),
            ("Socks", Socks),
            ("Beanie", Beanie),
            ("Bra", Bra),
            ("Swimsuit", Swimsuit),
            ("Briefs", Briefs),
            ("Face Mask", FaceMask),
            ("Hauberk", Hauberk),
            ("Leather Armor", LeatherArmor),
            ("Tunic", Tunic),
            ("Coif", Coif),
        };

        /// <summary>The sample browser's groups, in the order they are shown.</summary>
        public static readonly string[] Categories = { "Tops", "Bottoms", "Dresses and Skirts", "Underwear and Swim", "Accessories", "Medieval" };

        /// <summary>The group a sample is shown in.</summary>
        public static string Category(string name)
        {
            switch (name)
            {
                case "Short Pants": case "Jeans": case "Sweatpants": case "Leggings": return "Bottoms";
                case "Pleated Skirt": case "A-Line Skirt": case "Pencil Skirt": case "Circle Skirt": case "Sheath Dress": case "Sundress": case "Elastic Waist Dress": case "Belted Dress": return "Dresses and Skirts";
                case "Boxer Shorts": case "Bra": case "Swimsuit": case "Briefs": return "Underwear and Swim";
                case "Socks": case "Beanie": case "Face Mask": return "Accessories";
                case "Hauberk": case "Leather Armor": case "Tunic": case "Coif": return "Medieval";
                default: return "Tops";
            }
        }

        public static ClothesPattern Create(string name)
        {
            if (name == "Sleeveless Vest") return VestPattern.Create();
            foreach (var sample in All) if (sample.name == name) return sample.make();
            return null;
        }

        /// <summary>A sample with its UVs laid out quickly (by bounding box): for pictures and information, not to edit.</summary>
        public static ClothesPattern CreateQuick(string name)
        {
            bool was = PatternBuilder.QuickUvs;
            PatternBuilder.QuickUvs = true;
            try { return Create(name); }
            finally { PatternBuilder.QuickUvs = was; }
        }

        /// <summary>
        /// Writes every sample as a pattern asset in a folder (Clothes Designer Examples ships them in
        /// Assets/UMA/ClothesDesignerExamples/Patterns). An existing asset is updated in place and keeps its
        /// GUID. Saves only the samples. Returns the paths written.
        /// </summary>
        /// <param name="only">The samples to write, by name; null for all of them.</param>
        public static List<string> SaveAll(string folder, ICollection<string> only = null)
        {
            var written = new List<string>();
            foreach (var sample in All)
            {
                if (only != null && !only.Contains(sample.name)) continue;
                ClothesPattern made = sample.make();
                string path = folder.TrimEnd('/') + "/" + sample.name + ".asset";
                var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<ClothesPattern>(path);
                if (existing == null)
                {
                    UnityEditor.AssetDatabase.CreateAsset(made, path);
                }
                else
                {
                    UnityEditor.EditorUtility.CopySerialized(made, existing);
                    existing.name = sample.name;
                    UnityEditor.EditorUtility.SetDirty(existing);
                    UnityEditor.AssetDatabase.SaveAssetIfDirty(existing);
                    UnityEngine.Object.DestroyImmediate(made);
                }
                written.Add(path);
            }
            return written;
        }

        // ---- Shared blocks ---------------------------------------------------------------------

        /// <summary>The measurements of a front or back bodice (cm).</summary>
        private struct Bodice
        {
            /// <summary>Half the width at the hem and up to the underarm.</summary>
            public float halfWidth, hemHalfWidth;
            /// <summary>From the hem to the neck point.</summary>
            public float length;
            public float shoulderX, shoulderDrop, neckX, neckDepth, underarmY;
            /// <summary>How far the middle of the hem drops below its ends (a shirt tail).</summary>
            public float hemDrop;
        }

        /// <summary>
        /// A front or back: hem, sides, armholes, shoulders and a neckline in two halves (so a collar or a
        /// hood can follow it). Edges: Hem, Right side, Right armhole, Right shoulder, Right neckline,
        /// Left neckline, Left shoulder, Left armhole, Left side. "Right" is the right as drawn: the body's
        /// left on a front, the body's right on a back.
        /// </summary>
        private static PieceBuilder BodicePiece(PatternBuilder b, string name, Bodice d, float angle, string fabric)
        {
            float top = d.length, shoulderY = d.length - d.shoulderDrop;
            PieceBuilder p = b.Piece(name)
                .Point(-d.hemHalfWidth, 0f, "Hem", true)
                .Point(d.hemHalfWidth, 0f, "Right side")
                .Point(d.halfWidth, d.underarmY, "Right armhole", true)
                .Point(d.shoulderX, shoulderY, "Right shoulder")
                .Point(d.neckX, top, "Right neckline", true)
                .Point(0f, top - d.neckDepth, "Left neckline", true)
                .Point(-d.neckX, top, "Left shoulder")
                .Point(-d.shoulderX, shoulderY, "Left armhole", true)
                .Point(-d.halfWidth, d.underarmY, "Left side");
            float h = shoulderY - d.underarmY, w = d.halfWidth - d.shoulderX;
            p.Curve("Right armhole", -0.7f * w, 0.1f * h, 0f, -0.4f * h);
            p.Curve("Left armhole", 0f, -0.4f * h, 0.7f * w, 0.1f * h);
            p.Curve("Right neckline", -0.1f * d.neckX, -0.55f * d.neckDepth, 0.55f * d.neckX, 0f);
            p.Curve("Left neckline", -0.55f * d.neckX, 0f, 0.1f * d.neckX, -0.55f * d.neckDepth);
            if (d.hemDrop > 0f) p.Curve("Hem", d.hemHalfWidth * 0.4f, -d.hemDrop * 1.3f, -d.hemHalfWidth * 0.4f, -d.hemDrop * 1.3f);
            p.Place(BodyPart.Torso, angle, 4f).Fabric(fabric);
            return p;
        }

        private static void SewBodice(PatternBuilder b, PieceBuilder front, PieceBuilder back)
        {
            b.Sew(front, "Right side", back, "Left side");
            b.Sew(front, "Left side", back, "Right side");
            b.Sew(front, "Right shoulder", back, "Left shoulder");
            b.Sew(front, "Left shoulder", back, "Right shoulder");
        }

        /// <summary>
        /// A set-in sleeve for the body's right arm, its top along the top of the arm: Hem, Right underarm,
        /// Right cap (the front of the right arm), Left cap (its back), Left underarm.
        /// </summary>
        private static PieceBuilder SleevePiece(PatternBuilder b, string name, float capHalfWidth, float capHeight, float underarmLength, float hemHalfWidth, string fabric)
        {
            PieceBuilder s = b.Piece(name)
                .Point(-hemHalfWidth, 0f, "Hem", true)
                .Point(hemHalfWidth, 0f, "Right underarm")
                .Point(capHalfWidth, underarmLength, "Right cap")
                .Point(0f, underarmLength + capHeight, "Left cap")
                .Point(-capHalfWidth, underarmLength, "Left underarm");
            s.Curve("Right cap", -0.25f * capHalfWidth, 0.45f * capHeight, 0.45f * capHalfWidth, 0f);
            s.Curve("Left cap", -0.45f * capHalfWidth, 0f, 0.25f * capHalfWidth, 0.45f * capHeight);
            b.Sew(s, "Right underarm", s, "Left underarm");
            s.Place(BodyPart.RightArm, 0f, 3f).Fabric(fabric);
            return s;
        }

        /// <summary>Sleeves on both arms: the right one as drawn, the left its mirror image.</summary>
        private static (PieceBuilder right, PieceBuilder left) Sleeves(PatternBuilder b, PieceBuilder front, PieceBuilder back,
            float capHalfWidth, float capHeight, float underarmLength, float hemHalfWidth, string fabric)
        {
            PieceBuilder right = SleevePiece(b, "Right Sleeve", capHalfWidth, capHeight, underarmLength, hemHalfWidth, fabric);
            PieceBuilder left = b.Mirror(right, "Left Sleeve").Place(BodyPart.LeftArm, 0f, 3f);
            b.Sew(left, "Right underarm", left, "Left underarm");
            if (front != null)
            {
                b.Sew(right, "Right cap", front, "Left armhole");
                b.Sew(right, "Left cap", back, "Right armhole");
                b.Sew(left, "Right cap", front, "Right armhole");
                b.Sew(left, "Left cap", back, "Left armhole");
                front.Opening("Left armhole", false).Opening("Right armhole", false);
                back.Opening("Left armhole", false).Opening("Right armhole", false);
            }
            SleeveWidth(b, right, left, capHalfWidth, underarmLength);
            return (right, left);
        }

        /// <summary>The upper arm (30.4 cm on the default female) plus the ease the sleeve is drawn with.</summary>
        private static void SleeveWidth(PatternBuilder b, PieceBuilder right, PieceBuilder left, float capHalfWidth, float underarmLength)
        {
            b.Width("Sleeve", BodyMeasurement.UpperArm, 2f * capHalfWidth, 2f * capHalfWidth - 30.4f, 1, underarmLength - 0.5f, right, left);
        }

        /// <summary>A band: a rectangle with edges Bottom, Right end, Top, Left end.</summary>
        private static PieceBuilder Band(PatternBuilder b, string name, float length, float height, string fabric)
        {
            return b.Piece(name)
                .Point(-length * 0.5f, 0f, "Bottom")
                .Point(length * 0.5f, 0f, "Right end")
                .Point(length * 0.5f, height, "Top")
                .Point(-length * 0.5f, height, "Left end")
                .Fabric(fabric);
        }

        /// <summary>A cuff for each sleeve: a band sewn into a ring, its top along the sleeve's hem.</summary>
        private static void Cuffs(PatternBuilder b, PieceBuilder rightSleeve, PieceBuilder leftSleeve, float length, float height, float alongArm, string fabric)
        {
            PieceBuilder right = Band(b, "Right Cuff", length, height, fabric).Opening("Bottom").Place(BodyPart.RightArm, 0f, -alongArm);
            PieceBuilder left = b.Mirror(right, "Left Cuff").Place(BodyPart.LeftArm, 0f, -alongArm);
            foreach ((PieceBuilder cuff, PieceBuilder sleeve) in new[] { (right, rightSleeve), (left, leftSleeve) })
            {
                b.Sew(cuff, "Top", sleeve, "Hem");
                b.Sew(cuff, "Right end", cuff, "Left end");
                sleeve.Opening("Hem", false);
            }
        }

        /// <summary>
        /// A shirt collar in two parts, as a shirt maker cuts it: a stand sewn along the neckline (firmly
        /// interfaced, so it stands round the neck) and a leaf on the stand's top edge, turned down over it on
        /// a folded seam (lightly interfaced, so it holds its points). The leaf is drawn as it looks turned
        /// down, seen from behind: its neck edge along the top, its points hanging at both ends.
        /// </summary>
        private static (PieceBuilder stand, PieceBuilder leaf) ShirtCollar(PatternBuilder b, PieceBuilder front, PieceBuilder back,
            float standHeight, float leafDepth, float pointLength, string fabric)
        {
            // The band is cut as an arc, its ends rising 2.5 cm, as shirt bands are: its top edge is a few
            // percent shorter than its neck edge, so sewn on it is a cone that hugs the neck and stands.
            float length = front.Length("Right neckline") + back.Length("Left neckline") + back.Length("Right neckline") + front.Length("Left neckline");
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
                .Place(BodyPart.Torso, 180f, 6f + standHeight + rise).Fabric(fabric)
                .Stiffen(InterfacingLevel.Firm, 3f);
            b.SewAlong(stand, "Neck", (front, "Right neckline"), (back, "Left neckline"), (back, "Right neckline"), (front, "Left neckline"));
            foreach (PieceBuilder p in new[] { front, back }) p.Opening("Right neckline", false).Opening("Left neckline", false);
            float half = stand.Length("Top") * 0.5f, flare = 1.5f;
            PieceBuilder leaf = b.Piece("Collar")
                .Point(-half - flare, -pointLength, "Outer edge", true)
                .Point(half + flare, -pointLength, "Right point", true)
                .Point(half, 0f, "Neck edge")
                .Point(-half, 0f, "Left point", true)
                // The outer edge rises from the points to the leaf's depth at the back.
                .Curve("Outer edge", (half + flare) * 0.7f, (pointLength - leafDepth) * 1.33f, -(half + flare) * 0.7f, (pointLength - leafDepth) * 1.33f)
                .Place(BodyPart.Torso, 180f, stand.Piece.placement.topAboveShoulderCm, 1f)
                .Fabric(fabric)
                .Stiffen(InterfacingLevel.Light, 3f)
                // The leaf lies over the shirt front: with no layer it poked through it (25 crossing triangle
                // pairs after 480 frames, none with it; measured 2026-11-23). The cuffs are sewn edge to edge
                // and are left on the shirt's layer: layering them made crossings worse.
                .Layer(1);
            b.SewFolded(stand, "Top", leaf, "Neck edge");
            stand.Opening("Top", false);
            return (stand, leaf);
        }

        /// <summary>A stand collar along the neckline, laid round the back above the shoulders.</summary>
        private static PieceBuilder Collar(PatternBuilder b, PieceBuilder front, PieceBuilder back, float height, string fabric, bool closedFront)
        {
            float length = front.Length("Right neckline") + back.Length("Left neckline") + back.Length("Right neckline") + front.Length("Left neckline");
            PieceBuilder collar = b.Piece("Collar")
                .Point(-length * 0.5f, 0f, "Neck")
                .Point(length * 0.5f, 0f, "Right end", !closedFront)
                .Point(length * 0.5f, height, "Top", true)
                .Point(-length * 0.5f, height, "Left end", !closedFront)
                .Place(BodyPart.Torso, 180f, 6f + height).Fabric(fabric);
            // Seen from behind, the collar runs from the body's left front round the back to its right front.
            b.SewAlong(collar, "Neck", (front, "Right neckline"), (back, "Left neckline"), (back, "Right neckline"), (front, "Left neckline"));
            if (closedFront) b.Sew(collar, "Right end", collar, "Left end");
            foreach (PieceBuilder p in new[] { front, back }) p.Opening("Right neckline", false).Opening("Left neckline", false);
            return collar;
        }

        private static Bodice TeeBodice(bool front) => new Bodice
        {
            halfWidth = 27f, hemHalfWidth = 27f, length = 70f, shoulderX = 18f, shoulderDrop = 4.5f,
            neckX = 8.5f, neckDepth = front ? 9f : 2.5f, underarmY = 45.5f
        };

        private static void TopSizes(PatternBuilder b, PieceBuilder front, PieceBuilder back, Bodice d, float ease, float lengthAllowance)
        {
            b.Width("Chest", BodyMeasurement.Chest, 100f + ease, ease, 2, d.underarmY - 3f, front, back);
            b.Length("Length", BodyMeasurement.ShoulderToWaist, d.length, lengthAllowance, front, back);
        }

        // ---- Tops ------------------------------------------------------------------------------

        public static ClothesPattern TShirt()
        {
            var b = new PatternBuilder("T-Shirt", "Crew-neck T-shirt in jersey: front, back and two short set-in sleeves. Chest follows the body plus 10 cm of ease; length follows shoulder-to-waist plus 33 cm.");
            Bodice d = TeeBodice(true), bd = TeeBodice(false);
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Jersey");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Jersey");
            SewBodice(b, front, back);
            Sleeves(b, front, back, 19.5f, 13f, 9f, 17.5f, "Jersey");
            TopSizes(b, front, back, d, 10f, 33f);
            return b.Finish();
        }

        public static ClothesPattern LongSleeveTee()
        {
            var b = new PatternBuilder("Long-Sleeve Tee", "Crew-neck jersey top with long sleeves to the wrist. Sleeve length follows the arm.");
            Bodice d = TeeBodice(true), bd = TeeBodice(false);
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Jersey");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Jersey");
            SewBodice(b, front, back);
            var (right, left) = Sleeves(b, front, back, 19.5f, 13f, 45f, 10f, "Jersey");
            TopSizes(b, front, back, d, 10f, 33f);
            b.Length("Sleeve length", BodyMeasurement.ArmLength, 58f, 1f, right, left);
            return b.Finish();
        }

        public static ClothesPattern TankTop()
        {
            var b = new PatternBuilder("Tank Top", "Sleeveless jersey tank with a scoop neck and narrow straps.");
            var d = new Bodice { halfWidth = 26f, hemHalfWidth = 26f, length = 66f, shoulderX = 12f, shoulderDrop = 2f, neckX = 7f, neckDepth = 16f, underarmY = 41f };
            Bodice bd = d; bd.neckDepth = 9f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Jersey");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Jersey");
            SewBodice(b, front, back);
            TopSizes(b, front, back, d, 6f, 29f);
            return b.Finish();
        }

        public static ClothesPattern CropTop()
        {
            var b = new PatternBuilder("Crop Top", "Fitted sleeveless top in spandex, ending at the waist.");
            var d = new Bodice { halfWidth = 24f, hemHalfWidth = 23f, length = 40f, shoulderX = 13f, shoulderDrop = 2f, neckX = 7.5f, neckDepth = 11f, underarmY = 17f };
            Bodice bd = d; bd.neckDepth = 5f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Spandex");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Spandex");
            SewBodice(b, front, back);
            b.Width("Chest", BodyMeasurement.Chest, 96f, -4f, 2, d.underarmY - 3f, front, back);
            return b.Finish();
        }

        public static ClothesPattern PoloShirt()
        {
            var b = new PatternBuilder("Polo Shirt", "Short-sleeved cotton polo: front, back, two sleeves and a collar turned down over a stand, as a knit polo collar folds (a firm stand round the neck and a leaf over it on a folded seam). The placket and buttons are left out.");
            Bodice d = TeeBodice(true), bd = TeeBodice(false);
            d.neckDepth = 7f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Cotton");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Cotton");
            SewBodice(b, front, back);
            Sleeves(b, front, back, 19.5f, 13f, 9f, 17f, "Cotton");
            // A polo collar is one knit band folded about 3 cm up; cut here as the stand and the leaf, so the
            // fold is held as laid out. The leaf is 5.5 cm deep at the back to reach the neck seam: 4.5 cm rode
            // up on the default male (its edge 7 cm above the seam) and showed the stand (measured 2026-11-24).
            ShirtCollar(b, front, back, 3f, 5.5f, 6.5f, "Cotton");
            TopSizes(b, front, back, d, 12f, 35f);
            return b.Finish();
        }

        public static ClothesPattern ForteShirt()
        {
            var b = new PatternBuilder("Forte Shirt", "Short-sleeved cotton shirt with a stand collar: one straight band that stands up round the neck and is not turned down (the Polo Shirt's collar is). Front, back and two sleeves.");
            Bodice d = TeeBodice(true), bd = TeeBodice(false);
            d.neckDepth = 7f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Cotton");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Cotton");
            SewBodice(b, front, back);
            Sleeves(b, front, back, 19.5f, 13f, 9f, 17f, "Cotton");
            Collar(b, front, back, 6f, "Cotton", false);
            TopSizes(b, front, back, d, 12f, 35f);
            return b.Finish();
        }

        public static ClothesPattern OxfordShirt()
        {
            var b = new PatternBuilder("Oxford Shirt", "Long-sleeved cotton shirt: front, back with a box pleat under a shoulder yoke, sleeves with interfaced cuffs, a two-part collar (a firm stand and a leaf turned down over it on a folded seam) and a curved shirt-tail hem. Buttons and the front placket are left out.");
            var d = new Bodice { halfWidth = 28f, hemHalfWidth = 28f, length = 78f, shoulderX = 19f, shoulderDrop = 4.5f, neckX = 8.5f, neckDepth = 8f, underarmY = 53.5f, hemDrop = 5f };
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Cotton");
            // The back below the yoke: its top is the yoke seam, 6 cm below the shoulder point, with a box
            // pleat at the centre (the edge is drawn 8 cm longer for it).
            float yokeY = d.length - d.shoulderDrop - 6f, yokeX = d.shoulderX + 1f, pleat = 2f;
            PieceBuilder back = b.Piece("Back")
                .Point(-d.hemHalfWidth, 0f, "Hem", true)
                .Point(d.hemHalfWidth, 0f, "Right side")
                .Point(d.halfWidth, d.underarmY, "Right armhole")
                .Point(yokeX + 2f * pleat, yokeY, "Yoke seam")
                .Point(-yokeX - 2f * pleat, yokeY, "Left armhole")
                .Point(-d.halfWidth, d.underarmY, "Left side")
                .Curve("Hem", d.hemHalfWidth * 0.4f, -6.5f, -d.hemHalfWidth * 0.4f, -6.5f)
                .Curve("Right armhole", -4f, 1f, 1f, -3f)
                .Curve("Left armhole", -1f, -3f, 4f, 1f)
                .Pleat("Yoke seam", PleatKind.Box, 0.5f, pleat, 4f)
                .Place(BodyPart.Torso, 180f, 4f - (d.length - yokeY)).Fabric("Cotton");
            float shoulderY = d.length - d.shoulderDrop;
            PieceBuilder yoke = b.Piece("Yoke")
                .Point(-yokeX, yokeY, "Yoke seam")
                .Point(yokeX, yokeY, "Right armhole")
                .Point(d.shoulderX, shoulderY, "Right shoulder")
                .Point(d.neckX, d.length, "Right neckline")
                .Point(0f, d.length - 2.5f, "Left neckline")
                .Point(-d.neckX, d.length, "Left shoulder")
                .Point(-d.shoulderX, shoulderY, "Left armhole")
                .Curve("Right neckline", -0.85f, -1.4f, 4.7f, 0f)
                .Curve("Left neckline", -4.7f, 0f, 0.85f, -1.4f)
                .Place(BodyPart.Torso, 180f, 4f).Fabric("Cotton");
            b.Sew(back, "Yoke seam", yoke, "Yoke seam");
            b.Sew(front, "Right side", back, "Left side");
            b.Sew(front, "Left side", back, "Right side");
            b.Sew(front, "Right shoulder", yoke, "Left shoulder");
            b.Sew(front, "Left shoulder", yoke, "Right shoulder");
            PieceBuilder right = SleevePiece(b, "Right Sleeve", 20f, 13f, 39f, 13f, "Cotton");
            PieceBuilder left = b.Mirror(right, "Left Sleeve").Place(BodyPart.LeftArm, 0f, 3f);
            b.Sew(left, "Right underarm", left, "Left underarm");
            SleeveWidth(b, right, left, 20f, 39f);
            b.Sew(right, "Right cap", front, "Left armhole");
            b.Sew(left, "Right cap", front, "Right armhole");
            // The back of each cap runs from the top of the shoulder: first along the yoke, then the back.
            b.SewAlong(right, "Left cap", (yoke, "Right armhole"), (back, "Right armhole"));
            b.SewAlong(left, "Left cap", (back, "Left armhole"), (yoke, "Left armhole"));
            front.Opening("Left armhole", false).Opening("Right armhole", false);
            Cuffs(b, right, left, 23f, 6f, 49f, "Cotton");
            foreach (PatternPiece cuff in b.Pattern.pieces) if (cuff.name.EndsWith("Cuff")) { cuff.interfacing = InterfacingLevel.Firm; cuff.meshDetail = 2f; }
            ShirtCollar(b, front, yoke, 3.5f, 4.5f, 7f, "Cotton");
            b.Width("Chest", BodyMeasurement.Chest, 112f, 12f, 2, d.underarmY - 3f, front, back);
            b.Length("Sleeve length", BodyMeasurement.ArmLength, 52f, -5f, right, left);
            return b.Finish();
        }

        public static ClothesPattern Hoodie()
        {
            var b = new PatternBuilder("Hoodie", "Pullover hoodie in jersey: front, back, long sleeves with rib cuffs, a rib waistband and a two-panel hood. The kangaroo pocket is left out: it would be sewn onto the middle of the front.");
            var d = new Bodice { halfWidth = 29f, hemHalfWidth = 29f, length = 64f, shoulderX = 19.5f, shoulderDrop = 4f, neckX = 10f, neckDepth = 10f, underarmY = 39f };
            Bodice bd = d; bd.neckDepth = 3f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Jersey").Opening("Hem", false);
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Jersey").Opening("Hem", false);
            SewBodice(b, front, back);
            var (right, left) = Sleeves(b, front, back, 22f, 12f, 38f, 13f, "Jersey");
            Cuffs(b, right, left, 20f, 8f, 47f, "Elastic");
            // Rib waistband, a little shorter than the hem so it gathers it in.
            PieceBuilder bandFront = Band(b, "Front Band", 52f, 8f, "Elastic").Opening("Bottom").Opening("Top", false).Place(BodyPart.Torso, 0f, 4f - d.length);
            PieceBuilder bandBack = Band(b, "Back Band", 52f, 8f, "Elastic").Opening("Bottom").Opening("Top", false).Place(BodyPart.Torso, 180f, 4f - d.length);
            b.Sew(bandFront, "Top", front, "Hem");
            b.Sew(bandBack, "Top", back, "Hem");
            b.Sew(bandFront, "Right end", bandBack, "Left end");
            b.Sew(bandFront, "Left end", bandBack, "Right end");
            // Hood: the left panel (seen from the body's left, front to the left), the right its mirror.
            PieceBuilder hoodLeft = b.Piece("Hood Left")
                .Point(-12f, 0f, "Neck")
                .Point(12f, 0f, "Back seam")
                .Point(8f, 30f, "Crown")
                .Point(-13f, 34f, "Face", true)
                .Curve("Back seam", 6f, 10f, 6f, -6f)
                .Curve("Crown", -2f, 8f, 8f, 4f)
                .Curve("Face", 2f, -10f, -1f, 10f)
                .Place(BodyPart.Head, 90f, 4f).Fabric("Jersey");
            PieceBuilder hoodRight = b.Mirror(hoodLeft, "Hood Right").Place(BodyPart.Head, -90f, 4f);
            b.Sew(hoodLeft, "Back seam", hoodRight, "Back seam");
            b.Sew(hoodLeft, "Crown", hoodRight, "Crown");
            b.SewAlong(hoodLeft, "Neck", (front, "Right neckline"), (back, "Left neckline"));
            b.SewAlong(hoodRight, "Neck", (back, "Right neckline"), (front, "Left neckline"));
            foreach (PieceBuilder p in new[] { front, back }) p.Opening("Right neckline", false).Opening("Left neckline", false);
            TopSizes(b, front, back, d, 16f, 27f);
            b.Length("Sleeve length", BodyMeasurement.ArmLength, 50f, -7f, right, left);
            return b.Finish();
        }

        // ---- Trousers ----------------------------------------------------------------------------

        /// <summary>The measurements of a trouser leg (cm). The top sits 11 cm below the waist line.</summary>
        private struct Leg
        {
            /// <summary>From the hem to the crotch line, and from there to the top at the side.</summary>
            public float inseam, rise;
            public float frontTop, frontHip, frontCrotch, frontHem;
            public float backTop, backHip, backCrotch, backHem, backRise;
        }

        private const float HipBelowTop = 18f;
        private const float TrouserTop = -11f;

        /// <summary>
        /// The four pieces of a pair of trousers, each laid round its leg: the left front and back as drawn,
        /// the right ones their mirror images. Edges: Hem, Outseam, Hip curve, Waist, Center, Crotch curve, Inseam.
        /// </summary>
        private static (PieceBuilder lf, PieceBuilder rf, PieceBuilder lb, PieceBuilder rb) Trousers(PatternBuilder b, Leg g, string fabric, bool waistOpen)
        {
            float top = g.inseam + g.rise, hip = top - HipBelowTop;
            // Left front: the outside of the leg is to the right as drawn (the body's left).
            PieceBuilder lf = b.Piece("Left Front")
                .Point(-g.frontHem, 0f, "Hem", true)
                .Point(g.frontHem, 0f, "Outseam")
                .Point(g.frontHip, hip, "Hip curve")
                .Point(g.frontTop, top, "Waist", waistOpen)
                .Point(-g.frontTop, top, "Center")
                .Point(-g.frontHip, hip, "Crotch curve")
                .Point(-g.frontHip - g.frontCrotch, g.inseam, "Inseam")
                .Curve("Hip curve", 0f, 8f, 1.5f, -6f)
                .Curve("Crotch curve", 0f, -6f, 3f, 0.5f)
                .Place(BodyPart.LeftLeg, 0f, TrouserTop).Fabric(fabric);
            // Left back, seen from behind: the inside of the leg is to the right.
            PieceBuilder lb = b.Piece("Left Back")
                .Point(-g.backHem, 0f, "Hem", true)
                .Point(g.backHem, 0f, "Inseam")
                .Point(g.backHip + g.backCrotch, g.inseam, "Crotch curve")
                .Point(g.backHip, hip, "Center")
                .Point(g.backTop, top + g.backRise, "Waist", waistOpen)
                .Point(-g.backTop, top, "Hip curve")
                .Point(-g.backHip, hip, "Outseam")
                .Curve("Crotch curve", -6f, 0.5f, 0f, -7f)
                .Curve("Hip curve", -1.5f, -6f, 0f, 8f)
                .Place(BodyPart.LeftLeg, 180f, TrouserTop + g.backRise).Fabric(fabric);
            PieceBuilder rf = b.Mirror(lf, "Right Front").Place(BodyPart.RightLeg, 0f, TrouserTop);
            PieceBuilder rb = b.Mirror(lb, "Right Back").Place(BodyPart.RightLeg, 180f, TrouserTop + g.backRise);
            foreach ((PieceBuilder f, PieceBuilder k) in new[] { (lf, lb), (rf, rb) })
            {
                b.Sew(f, "Outseam", k, "Outseam");
                b.Sew(f, "Hip curve", k, "Hip curve");
                b.Sew(f, "Inseam", k, "Inseam");
            }
            b.Sew(lf, "Center", rf, "Center");
            b.Sew(lf, "Crotch curve", rf, "Crotch curve");
            b.Sew(lb, "Center", rb, "Center");
            b.Sew(lb, "Crotch curve", rb, "Crotch curve");
            return (lf, rf, lb, rb);
        }

        private static void TrouserSizes(PatternBuilder b, Leg g, float frontEase, float backEase, bool lengthFromInseam, PieceBuilder lf, PieceBuilder rf, PieceBuilder lb, PieceBuilder rb)
        {
            float hip = g.inseam + g.rise - HipBelowTop;
            // The front and back share the hips unequally: each quarter is a quarter of the hips plus its own ease.
            // Without a body each piece keeps its drawn width (twice its half width, four pieces round).
            b.Width("Hip (front)", BodyMeasurement.Hip, 8f * g.frontHip, frontEase, 4, hip, lf, rf);
            b.Width("Hip (back)", BodyMeasurement.Hip, 8f * g.backHip, backEase, 4, hip, lb, rb);
            if (lengthFromInseam)
            {
                b.Length("Leg length (front)", BodyMeasurement.Inseam, g.inseam + g.rise, g.rise - 2f + (g.inseam - 86f), lf, rf);
                b.Length("Leg length (back)", BodyMeasurement.Inseam, g.inseam + g.rise + g.backRise, g.rise + g.backRise - 2f + (g.inseam - 86f), lb, rb);
            }
        }

        /// <summary>A waistband in two halves, front and back, along the tops of the trousers.</summary>
        private static void Waistband(PatternBuilder b, PieceBuilder lf, PieceBuilder rf, PieceBuilder lb, PieceBuilder rb, float height, string fabric, float gather)
        {
            float front = (lf.Length("Waist") + rf.Length("Waist")) * gather, back = (lb.Length("Waist") + rb.Length("Waist")) * gather;
            // The trousers' top sits 11 cm below the waist line, about 48 cm below the top of the shoulders.
            float top = TrouserTop - 37f + height;
            PieceBuilder bandFront = Band(b, "Front Waistband", front, height, fabric).Opening("Top").Place(BodyPart.Torso, 0f, top);
            PieceBuilder bandBack = Band(b, "Back Waistband", back, height, fabric).Opening("Top").Place(BodyPart.Torso, 180f, top + 2f);
            // Seen from the front the band runs from the body's right to its left; seen from behind, left to right.
            b.SewAlong(bandFront, "Bottom", (rf, "Waist"), (lf, "Waist"));
            b.SewAlong(bandBack, "Bottom", (lb, "Waist"), (rb, "Waist"));
            b.Sew(bandFront, "Right end", bandBack, "Left end");
            b.Sew(bandFront, "Left end", bandBack, "Right end");
            foreach (PieceBuilder p in new[] { lf, rf, lb, rb }) p.Opening("Waist", false);
        }

        private static Leg Classic(float inseam, float frontHem, float backHem) => new Leg
        {
            inseam = inseam, rise = 25f,
            frontTop = 11f, frontHip = 14.5f, frontCrotch = 6f, frontHem = frontHem,
            backTop = 13f, backHip = 16.25f, backCrotch = 11f, backHem = backHem, backRise = 3f
        };

        public static ClothesPattern ShortPants()
        {
            var b = new PatternBuilder("Short Pants", "Cotton shorts with an 18 cm inseam, sitting on the hips. Four pieces, each laid round its leg; the hips follow the body.");
            Leg g = Classic(18f, 15f, 16f);
            var (lf, rf, lb, rb) = Trousers(b, g, "Cotton", true);
            TrouserSizes(b, g, -1f, 13f, false, lf, rf, lb, rb);
            return b.Finish();
        }

        public static ClothesPattern Jeans()
        {
            var b = new PatternBuilder("Jeans", "Straight-leg denim jeans to the ankle with a waistband, sitting on the hips. Pockets, belt loops and the fly are left out. Hips and length follow the body.");
            Leg g = Classic(86f, 10f, 11f);
            var (lf, rf, lb, rb) = Trousers(b, g, "Denim", false);
            Waistband(b, lf, rf, lb, rb, 4f, "Denim", 1f);
            TrouserSizes(b, g, -1f, 13f, true, lf, rf, lb, rb);
            return b.Finish();
        }

        public static ClothesPattern Sweatpants()
        {
            var b = new PatternBuilder("Sweatpants", "Loose joggers in cotton fleece with a rib waistband and rib ankle cuffs. (In jersey the legs hang heavier than the band holds.)");
            Leg g = Classic(78f, 11f, 12f);
            g.frontHip = 16f; g.backHip = 17.5f; g.frontTop = 13f; g.backTop = 15f;
            var (lf, rf, lb, rb) = Trousers(b, g, "Fleece", false);
            // The rib band is cut to 70% of the waist: firm and gripping, it holds up the legs' weight (an Elastic band
            // stretched over the hips and slid 11 cm).
            Waistband(b, lf, rf, lb, rb, 5f, "Rib", 0.7f);
            // Ankle cuffs gathered to 80% of the hem, each cut as a front and a back half (like the waistband), laid under
            // the hem it is sewn to and joined at the inseam and outseam. A cuff cut in one piece was laid as a short arc
            // inside the leg, its ends far from the outseam where they meet; sewing dragged them round the leg and they
            // stopped 33 mm apart (Human Female 3.0, 2026-11-23).
            float along = TrouserTop - g.inseam - g.rise;
            foreach ((PieceBuilder front, PieceBuilder back, BodyPart leg, string side) in new[] { (lf, lb, BodyPart.LeftLeg, "Left"), (rf, rb, BodyPart.RightLeg, "Right") })
            {
                PieceBuilder cuffFront = Band(b, side + " Front Cuff", front.Length("Hem") * 0.8f, 7f, "Elastic").Opening("Bottom").Place(leg, 0f, along);
                PieceBuilder cuffBack = Band(b, side + " Back Cuff", back.Length("Hem") * 0.8f, 7f, "Elastic").Opening("Bottom").Place(leg, 180f, along);
                b.Sew(cuffFront, "Top", front, "Hem");
                b.Sew(cuffBack, "Top", back, "Hem");
                b.Sew(cuffFront, "Right end", cuffBack, "Left end");
                b.Sew(cuffFront, "Left end", cuffBack, "Right end");
            }
            foreach (PieceBuilder p in new[] { lf, rf, lb, rb }) p.Opening("Hem", false);
            TrouserSizes(b, g, 7f, 19f, true, lf, rf, lb, rb);
            return b.Finish();
        }

        public static ClothesPattern Leggings()
        {
            var b = new PatternBuilder("Leggings", "Close-fitting spandex leggings to the ankle, about 12% smaller round than the hips so they stretch on and grip (drawn at body size they slide off under their own weight).");
            Leg g = Classic(86f, 6f, 6.5f);
            g.frontCrotch = 5f; g.backCrotch = 9f;
            var (lf, rf, lb, rb) = Trousers(b, g, "Spandex", true);
            TrouserSizes(b, g, -20f, -8f, true, lf, rf, lb, rb);
            return b.Finish();
        }

        public static ClothesPattern BoxerShorts()
        {
            var b = new PatternBuilder("Boxer Shorts", "Loose cotton boxer shorts with an elastic waistband.");
            Leg g = Classic(12f, 16f, 17f);
            var (lf, rf, lb, rb) = Trousers(b, g, "Cotton", false);
            // Elastic holds these: light legs (a Rib band measured no better, 3.0 against 3.3 cm of slide).
            Waistband(b, lf, rf, lb, rb, 4f, "Elastic", 0.85f);
            TrouserSizes(b, g, 3f, 17f, false, lf, rf, lb, rb);
            return b.Finish();
        }

        // ---- Skirts and dresses --------------------------------------------------------------------

        /// <summary>A skirt panel's top at the waist line: 37 cm below the top of the shoulders.</summary>
        private const float SkirtTop = -37f;

        private static (PieceBuilder front, PieceBuilder back) SkirtPanels(PatternBuilder b, float topHalf, float hemHalf, float length, string fabric)
        {
            PieceBuilder front = b.Piece("Front")
                .Point(-hemHalf, 0f, "Hem", true)
                .Point(hemHalf, 0f, "Right side")
                .Point(topHalf, length, "Waist", true)
                .Point(-topHalf, length, "Left side")
                .Place(BodyPart.Torso, 0f, SkirtTop).Fabric(fabric);
            PieceBuilder back = b.Piece("Back")
                .Point(-hemHalf, 0f, "Hem", true)
                .Point(hemHalf, 0f, "Right side")
                .Point(topHalf, length, "Waist", true)
                .Point(-topHalf, length, "Left side")
                .Place(BodyPart.Torso, 180f, SkirtTop).Fabric(fabric);
            b.Sew(front, "Right side", back, "Left side");
            b.Sew(front, "Left side", back, "Right side");
            return (front, back);
        }

        public static ClothesPattern PleatedSkirt()
        {
            var b = new PatternBuilder("Pleated Skirt", "Wool skirt with eight pressed knife pleats on each panel, stitched down 6 cm from the waist and flaring to the hem, so the pleats open over the hips. The waist follows the body plus 4 cm.");
            const int pleats = 8;
            const float depth = 3f, topHalf = 18f + pleats * depth;
            // The pleats are laid out folded all the way down, so the hem is drawn wide enough to clear the legs folded.
            var (front, back) = SkirtPanels(b, topHalf, topHalf + 28f, 55f, "Wool");
            foreach (PieceBuilder p in new[] { front, back })
            {
                for (int i = 0; i < pleats; i++) p.Pleat("Waist", PleatKind.KnifeRight, (i + 0.5f) / pleats, depth, 6f);
            }
            b.Width("Waist", BodyMeasurement.Waist, 71f, 4f, 2, 54f, front, back);
            return b.Finish();
        }

        public static ClothesPattern ALineSkirt()
        {
            var b = new PatternBuilder("A-Line Skirt", "Cotton skirt flaring from the waist to below the knee.");
            var (front, back) = SkirtPanels(b, 18f, 34f, 60f, "Cotton");
            foreach (PieceBuilder p in new[] { front, back }) p.Curve("Hem", 12f, -3f, -12f, -3f);
            b.Width("Waist", BodyMeasurement.Waist, 72f, 5f, 2, 59f, front, back);
            return b.Finish();
        }

        public static ClothesPattern PencilSkirt()
        {
            var b = new PatternBuilder("Pencil Skirt", "Fitted wool skirt to the knee: two waist darts on each panel, sized by the hips.");
            const float length = 60f, hipY = 40f;
            PieceBuilder Panel(string name, float angle, float dartWidth)
            {
                return b.Piece(name)
                    .Point(-28.5f, 0f, "Hem", true)
                    .Point(28.5f, 0f, "Right side")
                    .Point(30.5f, hipY, "Right hip")
                    .Point(18f + dartWidth, length, "Waist", true)
                    .Point(-18f - dartWidth, length, "Left hip")
                    .Point(-30.5f, hipY, "Left side")
                    .Curve("Right hip", 0.5f, 10f, 3f, -8f)
                    .Curve("Left hip", -3f, -8f, -0.5f, 10f)
                    .Dart("Waist", 0.3f, dartWidth, 8f, length - 13f)
                    .Dart("Waist", 0.7f, dartWidth, -8f, length - 13f)
                    .Place(BodyPart.Torso, angle, SkirtTop).Fabric("Wool");
            }
            PieceBuilder front = Panel("Front", 0f, 2.5f), back = Panel("Back", 180f, 3.5f);
            b.Sew(front, "Right side", back, "Left side");
            b.Sew(front, "Right hip", back, "Left hip");
            b.Sew(front, "Left side", back, "Right side");
            b.Sew(front, "Left hip", back, "Right hip");
            b.Width("Hip", BodyMeasurement.Hip, 122f, 5f, 2, hipY, front, back);
            return b.Finish();
        }

        public static ClothesPattern CircleSkirt()
        {
            var b = new PatternBuilder("Circle Skirt", "Full circle skirt in soft wool, cut as four quarter-circle panels. The waist is drawn for a 67 cm waist plus 3 cm. (In silk it slides over the hips: limp, slippery cloth hanging from the waist is more than the solver holds.)");
            float rw = 70f / (2f * Mathf.PI), rh = rw + 55f;
            float c = Mathf.Sqrt(0.5f);
            var gores = new List<PieceBuilder>();
            string[] names = { "Front", "Left", "Back", "Right" };
            for (int i = 0; i < 4; i++)
            {
                PieceBuilder g = b.Piece(names[i] + " Panel")
                    .Point(-c * rh, -c * rh, "Hem", true)
                    .Point(c * rh, -c * rh, "Right side")
                    .Point(c * rw, -c * rw, "Waist", true)
                    .Point(-c * rw, -c * rw, "Left side")
                    .Arc("Hem", Vector2.zero)
                    .Arc("Waist", Vector2.zero)
                    .Place(BodyPart.Torso, i * 90f, SkirtTop).Fabric("Wool");
                gores.Add(g);
            }
            // Going round the body's left from the front: each panel's right side meets the next one's left.
            for (int i = 0; i < 4; i++) b.Sew(gores[i], "Right side", gores[(i + 1) % 4], "Left side");
            return b.Finish();
        }

        public static ClothesPattern SheathDress()
        {
            var b = new PatternBuilder("Sheath Dress", "Sleeveless knee-length cotton dress shaped by double-pointed waist darts, sized by the hips.");
            const float length = 108f, hipY = 36f, underarmY = 84f;
            PieceBuilder Panel(string name, float angle, float neckDepth)
            {
                return b.Piece(name)
                    .Point(-29.5f, 0f, "Hem", true)
                    .Point(29.5f, 0f, "Right side")
                    .Point(30.75f, hipY, "Right waist side")
                    .Point(27f, underarmY, "Right armhole", true)
                    .Point(16f, length - 3.5f, "Right shoulder")
                    .Point(8.5f, length, "Right neckline", true)
                    .Point(0f, length - neckDepth, "Left neckline", true)
                    .Point(-8.5f, length, "Left shoulder")
                    .Point(-16f, length - 3.5f, "Left armhole", true)
                    .Point(-27f, underarmY, "Left waist side")
                    .Point(-30.75f, hipY, "Left side")
                    .Curve("Right waist side", -2f, 16f, -1f, -10f)
                    .Curve("Left waist side", 1f, -10f, 2f, 16f)
                    .Curve("Right armhole", -8f, 2f, 0f, -8f)
                    .Curve("Left armhole", 0f, -8f, 8f, 2f)
                    .Curve("Right neckline", -1f, -6f, 4.5f, 0f)
                    .Curve("Left neckline", -4.5f, 0f, 1f, -6f)
                    .DoubleDart(9f, 88f, 46f, 3f)
                    .DoubleDart(-9f, 88f, 46f, 3f)
                    .Place(BodyPart.Torso, angle, 4f).Fabric("Cotton");
            }
            PieceBuilder front = Panel("Front", 0f, 11f), back = Panel("Back", 180f, 3f);
            b.Sew(front, "Right side", back, "Left side");
            b.Sew(front, "Right waist side", back, "Left waist side");
            b.Sew(front, "Left side", back, "Right side");
            b.Sew(front, "Left waist side", back, "Right waist side");
            b.Sew(front, "Right shoulder", back, "Left shoulder");
            b.Sew(front, "Left shoulder", back, "Right shoulder");
            b.Width("Hip", BodyMeasurement.Hip, 123f, 6f, 2, hipY, front, back);
            return b.Finish();
        }

        public static ClothesPattern Sundress()
        {
            var b = new PatternBuilder("Sundress", "Flowing silk dress with narrow straps and a square neck, flaring to the knee.");
            var d = new Bodice { halfWidth = 26f, hemHalfWidth = 42f, length = 104f, shoulderX = 12f, shoulderDrop = 1.5f, neckX = 9f, neckDepth = 14f, underarmY = 79f };
            Bodice bd = d; bd.neckDepth = 10f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Silk").Curve("Hem", 14f, -3f, -14f, -3f);
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Silk").Curve("Hem", 14f, -3f, -14f, -3f);
            // A square neck: straight necklines.
            foreach (PieceBuilder p in new[] { front, back }) { p.Edge("Right neckline").curved = false; p.Edge("Left neckline").curved = false; }
            SewBodice(b, front, back);
            b.Width("Chest", BodyMeasurement.Chest, 104f, 4f, 2, d.underarmY - 3f, front, back);
            return b.Finish();
        }

        public static ClothesPattern ElasticWaistDress()
        {
            var b = new PatternBuilder("Elastic Waist Dress", "Short-sleeved jersey dress with a thick elastic waistband: a V-neck bodice and a skirt to above the knee, both cut wider than the band and gathered into it. The band is cut to the waist, kept outside the gathers (a higher layer) and baked 4 mm thick.");
            // The band is 8 cm tall round the waist line (37 cm below the top of the shoulders); the bodice stops at its top.
            const float bandHeight = 8f, bandTop = -33f, skirtLength = 60f, skirtTopHalf = 32f, skirtHemHalf = 34f;
            var d = new Bodice { halfWidth = 26f, hemHalfWidth = 24f, length = 37f, shoulderX = 18f, shoulderDrop = 4.5f, neckX = 8.5f, neckDepth = 12f, underarmY = 12.5f };
            Bodice bd = d; bd.neckDepth = 2.5f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Jersey");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Jersey");
            // A V neck: straight front necklines.
            front.Edge("Right neckline").curved = false;
            front.Edge("Left neckline").curved = false;
            SewBodice(b, front, back);
            Sleeves(b, front, back, 19.5f, 13f, 9f, 17f, "Jersey");
            // The band, in a front and a back half sewn into a ring at the sides: rib, firmly interfaced so it does not
            // roll, laid a centimetre out and kept outside the gathered cloth (layer 1), baked 4 mm thick.
            PieceBuilder Half(string name, float angle) => Band(b, name, 35f, bandHeight, "Rib")
                .Place(BodyPart.Torso, angle, bandTop, 1f).Stiffen(InterfacingLevel.Firm, 2f).Layer(1).Thickness(4f);
            PieceBuilder bandFront = Half("Front Band", 0f), bandBack = Half("Back Band", 180f);
            b.Sew(bandFront, "Right end", bandBack, "Left end");
            b.Sew(bandFront, "Left end", bandBack, "Right end");
            PieceBuilder Skirt(string name, float angle) => b.Piece(name)
                .Point(-skirtHemHalf, 0f, "Hem", true)
                .Point(skirtHemHalf, 0f, "Right side")
                .Point(skirtTopHalf, skirtLength, "Waist")
                .Point(-skirtTopHalf, skirtLength, "Left side")
                .Place(BodyPart.Torso, angle, bandTop - bandHeight).Fabric("Jersey");
            PieceBuilder skirtFront = Skirt("Skirt Front", 0f), skirtBack = Skirt("Skirt Back", 180f);
            b.Sew(skirtFront, "Right side", skirtBack, "Left side");
            b.Sew(skirtFront, "Left side", skirtBack, "Right side");
            // Gathered into the band: the bodice's hem along its top, the skirt's waist along its bottom.
            b.Sew(bandFront, "Top", front, "Hem");
            b.Sew(bandBack, "Top", back, "Hem");
            b.Sew(bandFront, "Bottom", skirtFront, "Waist");
            b.Sew(bandBack, "Bottom", skirtBack, "Waist");
            front.Opening("Hem", false);
            back.Opening("Hem", false);
            b.Width("Chest", BodyMeasurement.Chest, 104f, 4f, 2, d.underarmY - 3f, front, back);
            b.Length("Bodice Length", BodyMeasurement.ShoulderToWaist, d.length, d.length - 35f, front, back);
            b.Width("Waist", BodyMeasurement.Waist, 70f, 3f, 2, bandHeight * 0.5f, bandFront, bandBack);
            // The skirt keeps its ease over the hips, 20 cm below its top.
            float hipLine = skirtLength - 20f, hipCm = 4f * Mathf.Lerp(skirtHemHalf, skirtTopHalf, hipLine / skirtLength);
            b.Width("Hip", BodyMeasurement.Hip, hipCm, hipCm - 117f, 2, hipLine, skirtFront, skirtBack);
            return b.Finish();
        }

        public static ClothesPattern BeltedDress()
        {
            var b = new PatternBuilder("Belted Dress", "Short-sleeved jersey dress in two loose panels, a front and a back from the shoulder to above the knee, gathered in at the waist by a separate thick elastic belt. The belt is on a higher layer and sewn to nothing but two belt loops a side: it is laid outside the dress, tightens round it once the dress is sewn, and keeps the gathered cloth under it. It is baked 5 mm thick.");
            // Cut straight from the chest (108 round) to the hem (124), about 116 round the waist: the belt, cut to the
            // waist, gathers it by nearly half. The layers keep the gathered dress under the belt: the dress is pushed in
            // under it and, where the dress lies against the body, the belt rides on it (ClothSolverShared.LayerReach).
            var d = new Bodice { halfWidth = 27f, hemHalfWidth = 31f, length = 100f, shoulderX = 18f, shoulderDrop = 4.5f, neckX = 8.5f, neckDepth = 12f, underarmY = 75.5f };
            Bodice bd = d; bd.neckDepth = 2.5f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Jersey");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Jersey");
            // A V neck: straight front necklines.
            front.Edge("Right neckline").curved = false;
            front.Edge("Left neckline").curved = false;
            SewBodice(b, front, back);
            Sleeves(b, front, back, 19.5f, 13f, 9f, 17f, "Jersey");
            // The belt: a front and a back half sewn into a ring at the sides, 6 cm tall round the waist line (37 cm below
            // the top of the shoulders), cut 1 cm shorter than the waist so it grips; rib, firmly interfaced so it does not
            // roll. On a higher layer and sewn to nothing on the dress, it is a free piece: laid outside the dress, its own
            // seams and loops close after the dress is sewn, so it tightens round the dress and gathers it.
            const float beltHeight = 6f;
            PieceBuilder Half(string name, float angle) => Band(b, name, 33f, beltHeight, "Rib").Opening("Top").Opening("Bottom")
                .Place(BodyPart.Torso, angle, -37f + beltHeight * 0.5f, 1f).Stiffen(InterfacingLevel.Firm, 2f).Layer(1).Thickness(5f);
            PieceBuilder beltFront = Half("Front Belt", 0f), beltBack = Half("Back Belt", 180f);
            b.Sew(beltFront, "Right end", beltBack, "Left end");
            b.Sew(beltFront, "Left end", beltBack, "Right end");
            b.Width("Chest", BodyMeasurement.Chest, 108f, 8f, 2, d.underarmY - 3f, front, back);
            b.Length("Length", BodyMeasurement.ShoulderToWaist, d.length, d.length - 35f, front, back);
            b.Width("Waist", BodyMeasurement.Waist, 66f, -1f, 2, beltHeight * 0.5f, beltFront, beltBack);
            ClothesPattern pattern = b.Finish();
            // Belt loops: a tack near each end of each half, at the belt's middle, to the dress just inside its side seams
            // at the waist line (41 cm below the dress's top, laid 4 cm above the shoulders). Without them the belt slid
            // 12 to 26 cm down Human Male 3.0, whose waist is nearly as wide as his hips; tacks across the whole belt
            // stopped the dress gathering evenly under it.
            float waistY = d.length - 41f, dressHalf = Mathf.Lerp(d.hemHalfWidth, d.halfWidth, waistY / d.underarmY) - 1f;
            foreach ((PieceBuilder belt, PieceBuilder panel) in new[] { (beltFront, front), (beltBack, back) })
            {
                Rect beltBox = belt.Piece.Bounds(), panelBox = panel.Piece.Bounds();
                foreach (float f in new[] { 0.06f, 0.94f })
                {
                    pattern.tacks.Add(new PatternTack
                    {
                        id = ClothesPattern.NewId(),
                        pieceA = belt.Piece.id, a = new Vector2(Mathf.Lerp(beltBox.xMin, beltBox.xMax, f), beltBox.center.y),
                        pieceB = panel.Piece.id, b = new Vector2(panelBox.center.x + (f - 0.5f) * 2f * dressHalf, waistY)
                    });
                }
            }
            return pattern;
        }

        // ---- Accessories ---------------------------------------------------------------------------

        public static ClothesPattern Socks()
        {
            var b = new PatternBuilder("Socks", "A pair of knitted socks to mid-calf. Each sock is two halves laid flat beside the foot, outside and inside, sewn all round but the top.");
            // The outer half of the left sock, seen from the body's left: the toe points to the left as drawn.
            // Drawn for a 28 cm foot and a 19 cm ankle: each half reaches from under the sole to over the
            // instep (about half the foot's girth). Its top is laid 101 cm below the waist. The foot part is cut
            // snug (13 cm to the instep) and the leg part long, so the halves are laid with their soles below the
            // foot: the sole seam then closes by drawing them in under it. Laid beside the foot instead, each sole
            // edge had to wrap round under the foot, gripped it, and one point stayed 45 mm short of its partner
            // (Human Female 3.0, 2026-11-23); now every seam closes to within 0.6 mm.
            const float top = -101f;
            PieceBuilder leftOuter = b.Piece("Left Sock Outside")
                .Point(-25f, 0f, "Sole")
                .Point(8f, 0f, "Heel")
                .Point(5.5f, 10f, "Back")
                .Point(5.5f, 31f, "Top", true)
                .Point(-5f, 31f, "Front")
                .Point(-6f, 13f, "Instep")
                .Point(-25f, 5f, "Toe")
                .Curve("Heel", 3f, 4f, 0.5f, -4f)
                .Curve("Instep", -6f, -1f, 6f, 2f)
                .Curve("Toe", -5f, 0f, -5f, 0f)
                .Place(BodyPart.LeftLeg, 90f, top, 0f, true, -8.75f).Fabric("Elastic");
            // The outline's middle is 8.75 cm towards the toe from the leg tube: Across puts the tube on the
            // ankle (the mirrored halves the other way).
            PieceBuilder leftInner = b.Mirror(leftOuter, "Left Sock Inside").Place(BodyPart.LeftLeg, -90f, top, 0f, true, 8.75f);
            PieceBuilder rightOuter = b.Mirror(leftOuter, "Right Sock Outside").Place(BodyPart.RightLeg, -90f, top, 0f, true, 8.75f);
            PieceBuilder rightInner = b.Mirror(rightOuter, "Right Sock Inside").Place(BodyPart.RightLeg, 90f, top, 0f, true, -8.75f);
            foreach ((PieceBuilder outer, PieceBuilder inner) in new[] { (leftOuter, leftInner), (rightOuter, rightInner) })
            {
                foreach (string edge in new[] { "Sole", "Heel", "Back", "Front", "Instep", "Toe" }) b.Sew(outer, edge, inner, edge);
            }
            b.Pattern.meshLevel = MeshLevel.Fine;
            return b.Finish();
        }

        public static ClothesPattern Beanie()
        {
            var b = new PatternBuilder("Beanie", "Knitted beanie of four panels meeting at the crown, 4 cm smaller round than the head so it hugs it.");
            var panels = new List<PieceBuilder>();
            string[] names = { "Front", "Left", "Back", "Right" };
            for (int i = 0; i < 4; i++)
            {
                // 14 cm from the brim to the crown: the brim sits on the forehead, above the eyes.
                PieceBuilder p = b.Piece(names[i] + " Panel")
                    .Point(-7.5f, 0f, "Brim", true)
                    .Point(7.5f, 0f, "Right seam")
                    .Point(0f, 14f, "Left seam")
                    .Curve("Right seam", 0f, 7f, 6f, 0f)
                    .Curve("Left seam", -6f, 0f, 0f, 7f)
                    .Place(BodyPart.Head, i * 90f, 2f).Fabric("Elastic");
                panels.Add(p);
            }
            for (int i = 0; i < 4; i++) b.Sew(panels[i], "Right seam", panels[(i + 1) % 4], "Left seam");
            b.Width("Head", BodyMeasurement.Head, 60f, -4f, 4, 2f, panels.ToArray());
            b.Pattern.meshLevel = MeshLevel.Fine;
            return b.Finish();
        }

        public static ClothesPattern FaceMask()
        {
            var b = new PatternBuilder("Face Mask", "Cotton face mask over the nose, mouth and chin: two halves sewn down a curved centre seam that shapes it, held by an elastic band round the back of the head.");
            const float top = -10f;
            // The body's left half, seen from the front: the centre seam on its left as drawn, the ear end on its right.
            // The centre seam bows out at the nose and the chin, so sewn to its mirror the mask stands off the face.
            PieceBuilder left = b.Piece("Left Half")
                .Point(0f, 0f, "Bottom", true)
                .Point(11.5f, 1.5f, "Side", true)
                .Point(11.5f, 11.5f, "Top", true)
                .Point(0f, 14f, "Centre")
                .Curve("Bottom", 4f, -0.5f, -4f, 0f)
                .Curve("Top", -4f, 1.5f, 4f, 0f)
                .Curve("Centre", -2f, -2f, -1.5f, 2.5f)
                .Place(BodyPart.Head, 45f, top).Fabric("Cotton");
            PieceBuilder right = b.Piece("Right Half")
                .Point(-11.5f, 1.5f, "Bottom", true)
                .Point(0f, 0f, "Centre")
                .Point(0f, 14f, "Top", true)
                .Point(-11.5f, 11.5f, "Side", true)
                .Curve("Bottom", 4f, 0f, -4f, -0.5f)
                .Curve("Centre", 1.5f, 2.5f, 2f, -2f)
                .Curve("Top", -4f, 0f, 4f, 1.5f)
                .Place(BodyPart.Head, -45f, top).Fabric("Cotton");
            b.Sew(left, "Centre", right, "Centre");
            // The band runs round the back of the head below the ears, about 10% shorter than that half of the head,
            // its ends sewn to the middle of the mask's sides.
            PieceBuilder band = Band(b, "Band", 24f, 2.5f, "Elastic").Opening("Bottom").Opening("Top").Place(BodyPart.Head, 180f, -16f);
            b.Sew(band, "Right end", 0f, 1f, right, "Side", 0.375f, 0.625f);
            b.Sew(band, "Left end", 0f, 1f, left, "Side", 0.375f, 0.625f);
            b.Pattern.meshLevel = MeshLevel.Fine;
            return b.Finish();
        }

        // ---- Underwear and swim --------------------------------------------------------------------

        public static ClothesPattern Bra()
        {
            var b = new PatternBuilder("Bra", "Triangle bra: two spandex cups shaped by a dart, an elastic band under the bust cut 30 cm shorter than the chest so it grips, and narrow straps from the cups over the shoulders to the back band.");
            const float bandTop = -30f, bandHeight = 4.5f, bandLength = 35f;
            // A cup: a triangle 16 cm across the bottom and 17 cm high, its point cut square for the strap, with a dart
            // in the middle of the bottom edge to shape it over the bust. Both cups are cut alike.
            PieceBuilder Cup(string name, float angle) => b.Piece(name)
                .Point(-8f, 0f, "Bottom")
                .Point(8f, 0f, "Right side", true)
                .Point(0.75f, 17f, "Strap")
                .Point(-0.75f, 17f, "Left side", true)
                .Curve("Right side", -1f, 6f, 2f, -5f)
                .Curve("Left side", -2f, -5f, 1f, 6f)
                .Dart("Bottom", 0.5f, 3f, 0f, 7f)
                .Place(BodyPart.Torso, angle, -13f).Fabric("Spandex");
            PieceBuilder front = Band(b, "Front Band", bandLength, bandHeight, "Elastic").Opening("Bottom").Opening("Top").Place(BodyPart.Torso, 0f, bandTop);
            PieceBuilder back = Band(b, "Back Band", bandLength, bandHeight, "Elastic").Opening("Bottom").Opening("Top").Place(BodyPart.Torso, 180f, bandTop);
            b.Sew(front, "Right end", back, "Left end");
            b.Sew(front, "Left end", back, "Right end");
            // The front band's top runs from the body's left to its right: each cup takes 14 cm of it, 2 cm apart.
            float F(float x) => (bandLength * 0.5f - x) / bandLength;
            PieceBuilder leftCup = Cup("Left Cup", 30f), rightCup = Cup("Right Cup", -30f);
            b.Sew(leftCup, "Bottom", 0f, 1f, front, "Top", F(15f), F(1f));
            b.Sew(rightCup, "Bottom", 0f, 1f, front, "Top", F(-1f), F(-15f));
            // Each strap is a front part from the cup's point to the top of the shoulder and a back part from there down
            // to the back band, sewn over the shoulder (one strap laid in front would have to pass through the shoulder).
            // Seen from behind, the body's left is on the left of the back band.
            foreach ((PieceBuilder cup, float angle, string side, float backX) in new[] { (leftCup, 30f, "Left", -7.5f), (rightCup, -30f, "Right", 7.5f) })
            {
                PieceBuilder strapFront = Band(b, side + " Front Strap", 1.5f, 15f, "Elastic").Opening("Right end").Opening("Left end").Place(BodyPart.Torso, angle, 2f);
                PieceBuilder strapBack = Band(b, side + " Back Strap", 1.5f, 29f, "Elastic").Opening("Right end").Opening("Left end").Place(BodyPart.Torso, angle > 0f ? 147f : 213f, 2f);
                strapFront.Piece.meshDetail = 3f;
                strapBack.Piece.meshDetail = 3f;
                b.Sew(strapFront, "Bottom", cup, "Strap");
                b.Sew(strapFront, "Top", strapBack, "Top");
                b.Sew(strapBack, "Bottom", 0f, 1f, back, "Top", F(backX + 0.75f), F(backX - 0.75f));
            }
            b.Width("Band", BodyMeasurement.Chest, 2f * bandLength, 2f * bandLength - 100f, 2, bandHeight * 0.5f, front, back);
            b.Pattern.meshLevel = MeshLevel.Fine;
            return b.Finish();
        }

        public static ClothesPattern Swimsuit()
        {
            var b = new PatternBuilder("Swimsuit", "One-piece spandex swimsuit with high-cut legs, a scoop neck and a low back, cut about 14% smaller round the chest than the body so it grips. The front and back are sewn at the sides, over the shoulders and under the crotch.");
            // A panel from the crotch (y 0) to the top of the shoulder straps (y 74). The leg openings rise to the hip bone;
            // the front's are cut in (high legs), the back's bow out over the seat.
            PieceBuilder Panel(string name, float angle, float neckDepth, Vector4 leg)
            {
                return b.Piece(name)
                    .Point(-4.5f, 0f, "Crotch")
                    .Point(4.5f, 0f, "Right leg", true)
                    .Point(20.5f, 26f, "Right hip side")
                    .Point(15.5f, 37f, "Right waist side")
                    .Point(22f, 53f, "Right armhole", true)
                    .Point(11f, 74f, "Right strap")
                    .Point(7f, 74f, "Right neckline", true)
                    .Point(0f, 74f - neckDepth, "Left neckline", true)
                    .Point(-7f, 74f, "Left strap")
                    .Point(-11f, 74f, "Left armhole", true)
                    .Point(-22f, 53f, "Left waist side")
                    .Point(-15.5f, 37f, "Left hip side")
                    .Point(-20.5f, 26f, "Left leg", true)
                    .Curve("Right leg", leg.x, leg.y, leg.z, leg.w)
                    .Curve("Left leg", -leg.z, leg.w, -leg.x, leg.y)
                    .Curve("Right waist side", 1.5f, 5f, 1.5f, -5f)
                    .Curve("Left waist side", -1.5f, -5f, -1.5f, 5f)
                    .Curve("Right armhole", -6f, 1f, 0f, -7f)
                    .Curve("Left armhole", 0f, -7f, 6f, 1f)
                    .Curve("Right neckline", -0.7f, -0.55f * neckDepth, 3.85f, 0f)
                    .Curve("Left neckline", -3.85f, 0f, 0.7f, -0.55f * neckDepth)
                    .Place(BodyPart.Torso, angle, 1f).Fabric("Spandex");
            }
            PieceBuilder front = Panel("Front", 0f, 14f, new Vector4(2f, 8f, -8f, -4f));
            PieceBuilder back = Panel("Back", 180f, 30f, new Vector4(6f, 2f, -2f, -9f));
            foreach (string side in new[] { "hip side", "waist side", "strap" })
            {
                b.Sew(front, "Right " + side, back, "Left " + side);
                b.Sew(front, "Left " + side, back, "Right " + side);
            }
            b.Sew(front, "Crotch", back, "Crotch");
            b.Width("Chest", BodyMeasurement.Chest, 86f, -14f, 2, 49f, front, back);
            return b.Finish();
        }

        public static ClothesPattern Briefs()
        {
            var b = new PatternBuilder("Briefs", "Spandex briefs sitting on the hips, with leg openings along the leg crease and an elastic waistband, cut about 10% smaller round than the body so they grip. The back rises 2 cm higher and bows out over the seat.");
            PieceBuilder Panel(string name, float angle, float top, float height, float topHalf, float sideHalf, Vector4 leg)
            {
                return b.Piece(name)
                    .Point(-4.5f, 0f, "Crotch")
                    .Point(4.5f, 0f, "Right leg", true)
                    .Point(sideHalf, height - 6f, "Right side")
                    .Point(topHalf, height, "Waist")
                    .Point(-topHalf, height, "Left side")
                    .Point(-sideHalf, height - 6f, "Left leg", true)
                    .Curve("Right leg", leg.x, leg.y, leg.z, leg.w)
                    .Curve("Left leg", -leg.z, leg.w, -leg.x, leg.y)
                    .Place(BodyPart.Torso, angle, top).Fabric("Spandex");
            }
            const float frontTop = -45f, backTop = -43f, band = 2.5f;
            PieceBuilder front = Panel("Front", 0f, frontTop, 29f, 18f, 19.5f, new Vector4(2f, 9f, -8f, -3f));
            PieceBuilder back = Panel("Back", 180f, backTop, 31f, 21f, 22f, new Vector4(7f, 2f, -1f, -10f));
            b.Sew(front, "Right side", back, "Left side");
            b.Sew(front, "Left side", back, "Right side");
            b.Sew(front, "Crotch", back, "Crotch");
            // An elastic band 2.5 cm high, 85% of the top so it gathers it in.
            PieceBuilder bandFront = Band(b, "Front Waistband", front.Length("Waist") * 0.85f, band, "Elastic").Opening("Top").Place(BodyPart.Torso, 0f, frontTop + band);
            PieceBuilder bandBack = Band(b, "Back Waistband", back.Length("Waist") * 0.85f, band, "Elastic").Opening("Top").Place(BodyPart.Torso, 180f, backTop + band);
            b.Sew(bandFront, "Bottom", front, "Waist");
            b.Sew(bandBack, "Bottom", back, "Waist");
            b.Sew(bandFront, "Right end", bandBack, "Left end");
            b.Sew(bandFront, "Left end", bandBack, "Right end");
            // The front and back go round the hips together: each half of the body round takes its own share.
            b.Width("Hip (front)", BodyMeasurement.Hip, 76f, 76f - 117f, 2, 25f, front);
            b.Width("Hip (back)", BodyMeasurement.Hip, 86.67f, 86.67f - 117f, 2, 27f, back);
            return b.Finish();
        }

        // ---- Medieval ------------------------------------------------------------------------------

        /// <summary>Cuts a slit up from the middle of a piece's hem (a narrow open V), as a riding coat has.</summary>
        private static PieceBuilder Slit(PieceBuilder p, float height, float halfWidth)
        {
            int hem = p.Index("Hem");
            p.Piece.edges[hem].curved = false;
            Vector2[] at = { new Vector2(-halfWidth, 0f), new Vector2(0f, height), new Vector2(halfWidth, 0f) };
            string[] names = { "Slit left", "Slit right", "Hem right" };
            for (int i = 0; i < 3; i++)
            {
                p.Piece.points.Insert(hem + 1 + i, new PatternPoint { id = ClothesPattern.NewId(), position = at[i] });
                p.Piece.edges.Insert(hem + 1 + i, new PatternEdge { id = ClothesPattern.NewId(), name = names[i], opening = true });
            }
            return p;
        }

        public static ClothesPattern Hauberk()
        {
            var b = new PatternBuilder("Hauberk", "Knee-length mail shirt with long sleeves, slit at the front and back from the hem for riding. Mail is very heavy and limp: it hangs straight from the shoulders in narrow folds.");
            var d = new Bodice { halfWidth = 30f, hemHalfWidth = 33f, length = 100f, shoulderX = 20f, shoulderDrop = 4f, neckX = 9f, neckDepth = 8f, underarmY = 75f };
            Bodice bd = d; bd.neckDepth = 3f;
            // The slits reach 3 cm above the crotch.
            PieceBuilder front = Slit(BodicePiece(b, "Front", d, 0f, "Mail"), 26f, 3f);
            PieceBuilder back = Slit(BodicePiece(b, "Back", bd, 180f, "Mail"), 26f, 3f);
            SewBodice(b, front, back);
            var (right, left) = Sleeves(b, front, back, 22f, 12f, 44f, 12f, "Mail");
            TopSizes(b, front, back, d, 20f, d.length - 37f);
            b.Length("Sleeve length", BodyMeasurement.ArmLength, 56f, -1f, right, left);
            return b.Finish();
        }

        public static ClothesPattern LeatherArmor()
        {
            var b = new PatternBuilder("Leather Armor", "A leather jerkin to the hip with shoulder guards and four hanging tassets. The guards are interfaced firm so they keep their curve over the shoulders; the tassets hang from short seams at the hem.");
            var d = new Bodice { halfWidth = 27f, hemHalfWidth = 27f, length = 60f, shoulderX = 18f, shoulderDrop = 4f, neckX = 8.5f, neckDepth = 8f, underarmY = 34f };
            Bodice bd = d; bd.neckDepth = 3f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Leather");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Leather");
            SewBodice(b, front, back);
            // A shoulder guard: a short stiff cap over the top of the arm (drawn like a sleeve for the right arm, its front
            // to the right), sewn along the upper half of the armhole and open underneath.
            PieceBuilder rightGuard = b.Piece("Right Guard")
                .Point(-13f, 0f, "Hem", true)
                .Point(13f, 0f, "Right underarm", true)
                .Point(9f, 5f, "Right cap")
                .Point(0f, 13f, "Left cap")
                .Point(-9f, 5f, "Left underarm", true)
                .Curve("Hem", 6f, -2f, -6f, -2f)
                .Curve("Right cap", -1.5f, 4f, 4f, 0f)
                .Curve("Left cap", -4f, 0f, 1.5f, 4f)
                .Place(BodyPart.RightArm, 0f, 3f).Fabric("Leather").Stiffen(InterfacingLevel.Firm, 2f).Layer(1);
            PieceBuilder leftGuard = b.Mirror(rightGuard, "Left Guard").Place(BodyPart.LeftArm, 0f, 3f);
            // The front's body-right armhole runs down from the shoulder; its body-left one runs up to it (and the
            // back's the other way round), so the upper halves are 0 to 0.5 and 0.5 to 1 in turn.
            b.Sew(rightGuard, "Right cap", 0f, 1f, front, "Left armhole", 0f, 0.5f);
            b.Sew(rightGuard, "Left cap", 0f, 1f, back, "Right armhole", 0.5f, 1f);
            b.Sew(leftGuard, "Right cap", 0f, 1f, front, "Right armhole", 0.5f, 1f);
            b.Sew(leftGuard, "Left cap", 0f, 1f, back, "Left armhole", 0f, 0.5f);
            // Tassets: four strips hanging from the hem, two in front and two behind (seen from behind, the body's left
            // is on the left of the back's hem).
            float hemLength = front.Length("Hem");
            foreach ((string name, PieceBuilder body, float angle, float at) in new[]
                { ("Front Right Tasset", front, -25f, 0.18f), ("Front Left Tasset", front, 25f, 0.58f), ("Back Left Tasset", back, 155f, 0.18f), ("Back Right Tasset", back, 205f, 0.58f) })
            {
                PieceBuilder t = Band(b, name, 13f, 20f, "Leather").Opening("Bottom").Opening("Right end").Opening("Left end").Curve("Bottom", 3f, -2f, -3f, -2f)
                    .Place(BodyPart.Torso, angle, 4f - d.length).Layer(1);
                b.Sew(t, "Top", 0f, 1f, body, "Hem", at, at + 13f / hemLength);
            }
            TopSizes(b, front, back, d, 8f, d.length - 37f);
            return b.Finish();
        }

        public static ClothesPattern Tunic()
        {
            var b = new PatternBuilder("Tunic", "Knee-length wool T-tunic with long sleeves, a narrow V neck slit at the front and a skirt that flares from the hips, as worn through the Middle Ages.");
            var d = new Bodice { halfWidth = 30f, hemHalfWidth = 42f, length = 112f, shoulderX = 20f, shoulderDrop = 4f, neckX = 7.5f, neckDepth = 15f, underarmY = 86f };
            Bodice bd = d; bd.neckDepth = 3f;
            PieceBuilder front = BodicePiece(b, "Front", d, 0f, "Wool");
            PieceBuilder back = BodicePiece(b, "Back", bd, 180f, "Wool");
            foreach (PieceBuilder p in new[] { front, back }) p.Curve("Hem", 14f, -3f, -14f, -3f);
            SewBodice(b, front, back);
            var (right, left) = Sleeves(b, front, back, 22f, 12f, 44f, 11f, "Wool");
            TopSizes(b, front, back, d, 20f, d.length - 37f);
            b.Length("Sleeve length", BodyMeasurement.ArmLength, 56f, -1f, right, left);
            return b.Finish();
        }

        public static ClothesPattern Coif()
        {
            var b = new PatternBuilder("Coif", "Close-fitting linen cap of two halves sewn over the crown from the forehead to the nape, covering the ears and framing the face, held by a strap under the chin.");
            // The body's left half, seen from the body's left with the face to the left: the crown seam runs from the nape
            // up the back of the head, over the top and down to the forehead.
            PieceBuilder left = b.Piece("Left Half")
                .Point(-7f, 0f, "Bottom", true)
                .Point(6f, 0f, "Crown seam")
                .Point(-14f, 29f, "Face", true)
                .Curve("Crown seam", 7.5f, 14f, 14f, 4f)
                .Curve("Face", 3f, -9f, -1f, 10f)
                .Place(BodyPart.Head, 90f, 2f).Fabric("Cotton");
            PieceBuilder right = b.Mirror(left, "Right Half").Place(BodyPart.Head, -90f, 2f);
            b.Sew(left, "Crown seam", right, "Crown seam");
            // Tied under the chin, as a coif is worn: a strap from one lower front corner to the other holds it forward
            // (without it the cap rides back onto the crown). The left half's face edge runs down; its mirror's runs up.
            PieceBuilder strap = Band(b, "Chin Strap", 22f, 2f, "Cotton").Opening("Bottom").Opening("Top").Place(BodyPart.Head, 0f, -25f);
            b.Sew(strap, "Right end", 0f, 1f, left, "Face", 0.9f, 0.965f);
            b.Sew(strap, "Left end", 0f, 1f, right, "Face", 0.035f, 0.1f);
            b.Pattern.meshLevel = MeshLevel.Fine;
            return b.Finish();
        }
    }
}
