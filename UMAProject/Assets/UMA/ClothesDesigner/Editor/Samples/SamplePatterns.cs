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
            ("Socks", Socks),
            ("Beanie", Beanie),
        };

        public static ClothesPattern Create(string name)
        {
            if (name == "Sleeveless Vest") return VestPattern.Create();
            foreach (var sample in All) if (sample.name == name) return sample.make();
            return null;
        }

        /// <summary>
        /// Writes every sample as a pattern asset in a folder (Clothes Designer Examples ships them in
        /// Assets/UMA/ClothesDesignerExamples/Patterns). An existing asset is updated in place and keeps its
        /// GUID. Saves only the samples. Returns the paths written.
        /// </summary>
        public static List<string> SaveAll(string folder)
        {
            var written = new List<string>();
            foreach (var sample in All)
            {
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
            var b = new PatternBuilder("Polo Shirt", "Short-sleeved cotton polo with a stand collar. The placket and buttons are left out: a pattern piece cannot yet be sewn onto the middle of another.");
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
            var (lf, rf, lb, rb) = Trousers(b, g, "Cotton", false);
            // The rib band is gathered to 70%: tight enough at the hips to hold up the legs' weight.
            Waistband(b, lf, rf, lb, rb, 5f, "Elastic", 0.7f);
            // Ankle cuffs, centred on the inside of each leg, sewn along the front hem then the back hem.
            float cuffLength = (lf.Length("Hem") + lb.Length("Hem")) * 0.8f, along = TrouserTop - g.inseam - g.rise;
            PieceBuilder leftCuff = Band(b, "Left Cuff", cuffLength, 7f, "Elastic").Opening("Bottom").Place(BodyPart.LeftLeg, -90f, along);
            PieceBuilder rightCuff = b.Mirror(leftCuff, "Right Cuff").Place(BodyPart.RightLeg, 90f, along);
            b.SewAlong(leftCuff, "Top", (lf, "Hem"), (lb, "Hem"));
            b.SewAlong(rightCuff, "Top", (rb, "Hem"), (rf, "Hem"));
            foreach (PieceBuilder cuff in new[] { leftCuff, rightCuff }) b.Sew(cuff, "Right end", cuff, "Left end");
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

        // ---- Accessories ---------------------------------------------------------------------------

        public static ClothesPattern Socks()
        {
            var b = new PatternBuilder("Socks", "A pair of knitted socks to mid-calf. Each sock is two halves laid flat beside the foot, outside and inside, sewn all round but the top.");
            // The outer half of the left sock, seen from the body's left: the toe points to the left as drawn.
            // Drawn for a 28 cm foot and a 19 cm ankle: each half reaches from under the sole to over the
            // instep (about half the foot's girth). The top sits 30 cm above the floor, 101 cm below the waist.
            const float top = -101f;
            PieceBuilder leftOuter = b.Piece("Left Sock Outside")
                .Point(-25f, 0f, "Sole")
                .Point(8f, 0f, "Heel")
                .Point(5.5f, 13f, "Back")
                .Point(5.5f, 30f, "Top", true)
                .Point(-5f, 30f, "Front")
                .Point(-6f, 16f, "Instep")
                .Point(-25f, 8f, "Toe")
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
    }
}
