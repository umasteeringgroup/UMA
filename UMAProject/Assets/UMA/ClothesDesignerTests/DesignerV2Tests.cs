#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>
    /// Clothes Designer V2 (Plans/ClothesDesignerV2Plan.md): the new samples and their pictures, painted stiffness and
    /// weight, cloth from a body region (unrolled from synthetic surfaces here), the hair plane, and sharp edges.
    /// </summary>
    public sealed class DesignerV2Tests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void Clean()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private T Keep<T>(T o) where T : Object { made.Add(o); return o; }

        private static readonly string[] NewSamples = { "Bra", "Swimsuit", "Briefs", "Face Mask", "Hauberk", "Leather Armor", "Tunic", "Coif" };

        // ---- Samples and pictures ----------------------------------------------------------------------

        [Test]
        public void NewSamples_BuildWithoutProblems_InTheirGroups()
        {
            foreach (string name in NewSamples)
            {
                ClothesPattern p = Keep(SamplePatterns.Create(name));
                Assert.That(p, Is.Not.Null, name);
                Assert.That(p.FindProblems(), Is.Empty, name);
                Assert.That(p.pieces.Count, Is.GreaterThan(1), name);
                Assert.That(PatternMesher.Build(p, PatternGeometry.Evaluate(p, null, null, null), 3f).VertexCount, Is.GreaterThan(30), name);
            }
            Assert.That(SamplePatterns.Category("Bra"), Is.EqualTo("Underwear and Swim"));
            Assert.That(SamplePatterns.Category("Face Mask"), Is.EqualTo("Accessories"));
            Assert.That(SamplePatterns.Category("Coif"), Is.EqualTo("Medieval"));
            Assert.That(SamplePatterns.Category("T-Shirt"), Is.EqualTo("Tops"));
            Assert.That(FabricPresets.Find("Mail"), Is.Not.Null);
            Assert.That(SamplePatterns.All.Length, Is.EqualTo(31));
        }

        [Test]
        public void CreateQuick_PacksByBoundingBox_AndPutsTheSwitchBack()
        {
            ClothesPattern quick = Keep(SamplePatterns.CreateQuick("Tunic"));
            Assert.That(quick.uvLayout.quickPacked, Is.True);
            Assert.That(PatternBuilder.QuickUvs, Is.False);
            Assert.That(Keep(SamplePatterns.Create("Tunic")).uvLayout.quickPacked, Is.False);
        }

        [Test]
        public void PatternPicture_FillsThePieces()
        {
            ClothesPattern p = Keep(SamplePatterns.CreateQuick("Bra"));
            Texture2D picture = Keep(SamplePictures.DrawPattern(p, 80, 100));
            int filled = 0;
            foreach (Color32 c in picture.GetPixels32()) if (c.a > 0) filled++;
            Assert.That(filled, Is.GreaterThan(80 * 100 / 20), "pieces cover more than 5% of the picture");
            Assert.That(SamplePictures.PieceColor(0), Is.Not.EqualTo(SamplePictures.PieceColor(1)));
        }

        [Test]
        public void BrowserLists_TheVestAndEverySample()
        {
            List<string> names = SampleBrowserWindow.SampleNames();
            Assert.That(names[0], Is.EqualTo("Sleeveless Vest"));
            Assert.That(names.Count, Is.EqualTo(SamplePatterns.All.Length + 1));
            Assert.That(SampleBrowserWindow.CategoryOf("Sleeveless Vest"), Is.EqualTo("Tops"));
        }

        [Test]
        public void BrowserSearch_MatchesTheSamplesNameOnly_NotItsGroup()
        {
            List<string> Found(string text) => SampleBrowserWindow.SampleNames().FindAll(n => SampleBrowserWindow.MatchesSearch(n, text));
            Assert.That(Found("ar"), Is.EqualTo(new[] { "Leather Armor" }), "not the whole Underwear and Swim group (\"ar\" is in Underwear)");
            Assert.That(Found("ARM"), Is.EqualTo(new[] { "Leather Armor" }), "any case");
            Assert.That(Found(" swim "), Is.EqualTo(new[] { "Swimsuit" }), "spaces round it ignored");
            Assert.That(Found("").Count, Is.EqualTo(SampleBrowserWindow.SampleNames().Count), "nothing typed: every sample");
            Assert.That(Found("Medieval"), Is.Empty, "a group is picked with its button, not the search");
        }

        // ---- Painted stiffness and weight --------------------------------------------------------------

        private static (PatternMesh mesh, List<FabricSettings> fabrics) VestMesh(ClothesPattern p)
        {
            PatternMesh mesh = PatternMesher.Build(p, PatternGeometry.Evaluate(p, null, null, null), 3f);
            var fabrics = new List<FabricSettings>();
            foreach (PatternPiece piece in p.pieces) fabrics.Add(piece.fabric);
            return (mesh, fabrics);
        }

        [Test]
        public void Weight_MakesTheClothHeavier_UpToFourTimes()
        {
            ClothesPattern p = Keep(VestPattern.Create());
            var (mesh, fabrics) = VestMesh(p);
            int n = mesh.VertexCount;
            float Mass(ClothModel m) { float s = 0f; foreach (float w in m.InverseMass) if (w > 0f) s += 1f / w; return s; }
            float plain = Mass(ClothModel.Build(mesh, fabrics, null, new ClothModelOptions { Pattern = p }));
            var weight = new float[n];
            for (int i = 0; i < n; i++) weight[i] = 1f;
            float heavy = Mass(ClothModel.Build(mesh, fabrics, null, new ClothModelOptions { Pattern = p, Weight = weight }));
            Assert.That(heavy / plain, Is.EqualTo(1f + FitPaint.MaxExtraWeight).Within(0.01f));
        }

        [Test]
        public void Stiffen_TiesThePaintedCloth_AndFirmsItsStretch()
        {
            ClothesPattern p = Keep(VestPattern.Create());
            foreach (PatternPiece piece in p.pieces) piece.fabric = FabricPresets.Create("Jersey");
            var (mesh, fabrics) = VestMesh(p);
            int n = mesh.VertexCount;
            var stiff = new float[n];
            for (int i = 0; i < n; i++) stiff[i] = 1f;
            ClothModel plain = ClothModel.Build(mesh, fabrics, null, new ClothModelOptions { Pattern = p });
            ClothModel firm = ClothModel.Build(mesh, fabrics, null, new ClothModelOptions { Pattern = p, Stiff = stiff });
            Assert.That(plain.StiffenedConstraints, Is.EqualTo(0));
            Assert.That(firm.StiffenedConstraints, Is.GreaterThan(n), "ties two edges apart, every way");
            float Stretch(ClothModel m) { foreach (ClothConstraint c in m.Constraints) if (c.kind == (int)ConstraintKind.Stretch) return c.compliance; return -1f; }
            Assert.That(Stretch(firm), Is.EqualTo(Stretch(plain) * 0.01f).Within(Stretch(plain) * 0.001f), "two decades firmer");
            Assert.That(FitPaint.StiffTieCompliance(1f), Is.EqualTo(ClothModel.FirmInterfacingCompliance).Within(1e-7f));
            Assert.That(FitPaint.StiffTieCompliance(0.5f), Is.EqualTo(ClothModel.LightInterfacingCompliance).Within(1e-4f));
        }

        [Test]
        public void StiffAndWeight_AreKeptOnThePattern_AndBrushed()
        {
            ClothesPattern p = Keep(VestPattern.Create());
            var (mesh, _) = VestMesh(p);
            int n = mesh.VertexCount;
            float[] hug = new float[n], smooth = new float[n], inflate = new float[n], stiff = new float[n], weight = new float[n];
            for (int i = 0; i < n; i += 3) { stiff[i] = 0.8f; weight[i] = 0.5f; }
            var kept = new List<FitPaintPoint>();
            FitPaint.Store(p, mesh, hug, smooth, inflate, stiff, weight, kept);
            FitPaint.Resolve(p, mesh, kept, out _, out _, out _, out float[] stiffBack, out float[] weightBack);
            for (int i = 0; i < n; i += 3)
            {
                Assert.That(stiffBack[i], Is.EqualTo(0.8f).Within(0.02f), "point " + i);
                Assert.That(weightBack[i], Is.EqualTo(0.5f).Within(0.02f), "point " + i);
            }
            float h = 0f, s = 0f, f = 0f, st = 0f, w = 0f;
            FitPaint.Brush(FitPaintMode.Stiffen, false, 0.7f, ref h, ref s, ref f, ref st, ref w);
            FitPaint.Brush(FitPaintMode.Weight, false, 0.4f, ref h, ref s, ref f, ref st, ref w);
            Assert.That(st, Is.EqualTo(0.7f)); Assert.That(w, Is.EqualTo(0.4f));
            FitPaint.Brush(FitPaintMode.Stiffen, true, 1f, ref h, ref s, ref f, ref st, ref w);
            Assert.That(st, Is.EqualTo(0f)); Assert.That(w, Is.EqualTo(0.4f), "Ctrl takes off only the mode's own paint");
            FitPaint.Brush(FitPaintMode.Erase, false, 1f, ref h, ref s, ref f, ref st, ref w);
            Assert.That(w, Is.EqualTo(0f));
        }

        // ---- Cloth from a body region ------------------------------------------------------------------

        /// <summary>A surface of rings round the y axis: radius r(t) and height y(t) for t from 0 to 1, faces out.</summary>
        private static RegionPatch Revolve(string name, int around, int rings, System.Func<float, float> radius, System.Func<float, float> height, bool pole)
        {
            var patch = new RegionPatch { Name = name, Part = BodyPart.Torso, Up = Vector3.up, AxisPoint = Vector3.zero, CutSide = Vector3.back, Front = Vector3.forward, Ninety = Vector3.left };
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings;
                for (int i = 0; i < around; i++)
                {
                    float a = i / (float)around * 2f * Mathf.PI;
                    patch.Positions.Add(new Vector3(Mathf.Cos(a) * radius(t), height(t), Mathf.Sin(a) * radius(t)));
                    patch.Ids.Add(patch.Ids.Count);
                }
            }
            for (int j = 0; j < rings; j++)
            {
                for (int i = 0; i < around; i++)
                {
                    int a = j * around + i, c = j * around + (i + 1) % around, b = a + around, d = c + around;
                    patch.Triangles.AddRange(new[] { a, b, c, c, b, d });
                }
            }
            if (pole)
            {
                int top = patch.Positions.Count;
                patch.Positions.Add(new Vector3(0f, height(1f) + radius(1f) * 0.5f, 0f));
                patch.Ids.Add(top);
                int last = rings * around;
                for (int i = 0; i < around; i++) patch.Triangles.AddRange(new[] { last + i, top, last + (i + 1) % around });
            }
            return patch;
        }

        [Test]
        public void Tube_IsCutOpen_IntoOnePieceSewnToItself()
        {
            RegionPatch tube = Revolve("Tube", 32, 10, t => 0.05f, t => t * 0.2f, false);
            List<FlatPiece> pieces = RegionUnfold.Unfold(tube, out string problem);
            Assert.That(problem, Is.Null);
            Assert.That(pieces.Count, Is.EqualTo(1));
            Assert.That(pieces[0].WentRound, Is.True);
            Assert.That(pieces[0].Distortion, Is.LessThan(0.03f), "a cylinder unrolls almost exactly");
            ClothesPattern pattern = Keep(ScriptableObject.CreateInstance<ClothesPattern>());
            List<PatternPiece> added = RegionPieces.AddToPattern(pattern, pieces, new BodyLandmarks { shoulderTopY = 0.2f }, 0f, "Jersey", "Test Race");
            pattern.Migrate();
            Assert.That(added.Count, Is.EqualTo(1));
            Assert.That(pattern.seams.Count, Is.GreaterThanOrEqualTo(1), "the cut is sewn back together");
            Assert.That(pattern.FindProblems(), Is.Empty);
            // Unrolled, its width is the tube's girth (2 pi r = 31.4 cm).
            Rect bounds = added[0].Bounds();
            Assert.That(Mathf.Max(bounds.width, bounds.height), Is.EqualTo(31.4f).Within(1.5f));
        }

        [Test]
        public void Dome_IsSplitIntoGores_EachFlatEnough()
        {
            RegionPatch dome = Revolve("Dome", 32, 12, t => 0.1f * Mathf.Cos(t * 1.35f), t => 0.1f * Mathf.Sin(t * 1.35f), true);
            List<FlatPiece> pieces = RegionUnfold.Unfold(dome, out _);
            Assert.That(pieces, Is.Not.Null);
            Assert.That(pieces.Count, Is.GreaterThanOrEqualTo(2), "a cap cannot lie flat in one piece");
            foreach (FlatPiece f in pieces) Assert.That(f.Distortion, Is.LessThanOrEqualTo(RegionUnfold.MaxDistortion + 0.02f), f.Name);
            ClothesPattern pattern = Keep(ScriptableObject.CreateInstance<ClothesPattern>());
            RegionPieces.AddToPattern(pattern, pieces, new BodyLandmarks(), 0f, "Cotton");
            pattern.Migrate();
            Assert.That(pattern.FindProblems(), Is.Empty);
            Assert.That(pattern.seams.Count, Is.GreaterThanOrEqualTo(pieces.Count - 1), "the gores are sewn together");
        }

        [Test]
        public void FlatPatch_UnrollsAsItIs_WithOnlyOpenings()
        {
            var patch = new RegionPatch { Name = "Flat", Up = Vector3.up, CutSide = Vector3.back };
            for (int j = 0; j <= 8; j++) for (int i = 0; i <= 8; i++) { patch.Positions.Add(new Vector3(i * 0.02f, j * 0.02f, 0f)); patch.Ids.Add(patch.Ids.Count); }
            for (int j = 0; j < 8; j++) for (int i = 0; i < 8; i++)
            {
                int a = j * 9 + i, b = a + 1, c = a + 9, d = c + 1;
                patch.Triangles.AddRange(new[] { a, b, c, b, d, c });
            }
            List<FlatPiece> pieces = RegionUnfold.Unfold(patch, out string problem);
            Assert.That(problem, Is.Null);
            Assert.That(pieces.Count, Is.EqualTo(1));
            Assert.That(pieces[0].Distortion, Is.LessThan(0.01f));
            ClothesPattern pattern = Keep(ScriptableObject.CreateInstance<ClothesPattern>());
            List<PatternPiece> added = RegionPieces.AddToPattern(pattern, pieces, new BodyLandmarks(), 0f, "Cotton");
            Assert.That(pattern.seams, Is.Empty);
            foreach (PatternEdge e in added[0].edges) Assert.That(e.opening, Is.True);
            Rect bounds = added[0].Bounds();
            Assert.That(bounds.width, Is.EqualTo(16f).Within(1.2f), "smoothing rounds the corners a little");
            Assert.That(bounds.height, Is.EqualTo(16f).Within(1.2f));
        }

        [Test]
        public void BodyLayout_TakesTheDrawnPieceBackToWhereItWasPainted_OnItsRaceOnly()
        {
            RegionPatch tube = Revolve("Tube", 32, 10, t => 0.05f, t => t * 0.2f, false);
            List<FlatPiece> pieces = RegionUnfold.Unfold(tube, out _);
            ClothesPattern pattern = Keep(ScriptableObject.CreateInstance<ClothesPattern>());
            PatternPiece piece = RegionPieces.AddToPattern(pattern, pieces, new BodyLandmarks(), 0f, "Jersey", "Test Race")[0];
            Assert.That(piece.bodyLayout.Valid, Is.True);
            Assert.That(BodyLayoutMap.For(piece, "Another Race"), Is.Null);
            BodyLayoutMap map = BodyLayoutMap.For(piece, "Test Race");
            Assert.That(map, Is.Not.Null);
            foreach (PatternPoint point in piece.points)
            {
                Vector3 onBody = map.Position(point.position, out Vector3 normal);
                Assert.That(new Vector2(onBody.x, onBody.z).magnitude, Is.EqualTo(0.05f).Within(0.004f), "on the tube");
                Assert.That(Vector3.Dot(normal, new Vector3(onBody.x, 0f, onBody.z).normalized), Is.GreaterThan(0.9f), "facing out");
            }
            // Moved on the canvas, the piece still maps (it follows its anchor point).
            foreach (PatternPoint point in piece.points) point.position += new Vector2(40f, 0f);
            Vector3 moved = BodyLayoutMap.For(piece, "Test Race").Position(piece.points[0].position, out _);
            Assert.That(new Vector2(moved.x, moved.z).magnitude, Is.EqualTo(0.05f).Within(0.004f));
        }

        // ---- Hair plane and sharp edges ----------------------------------------------------------------

        [Test]
        public void HairPlane_FacesIntoTheGarment_AsUmaReadsIt()
        {
            Vector3 inward = new Vector3(0f, 0.3f, 1f).normalized;
            Vector3 u = Vector3.Cross(inward, Vector3.up).normalized * 0.1f;
            Vector3 v = Vector3.Cross(u, inward).normalized * 0.08f;
            Vector3[] c = HairSmoosh.PlaneCorners(new Vector3(0f, 1.7f, 0f), inward, u, v);
            // UMA builds the smoosh plane from corners 0, 2 and 1.
            var plane = new Plane(c[0], c[2], c[1]);
            Assert.That(Vector3.Dot(plane.normal, inward), Is.GreaterThan(0.999f));
            Assert.That(plane.GetDistanceToPoint(new Vector3(0f, 1.7f, 0f) + inward * 0.05f), Is.GreaterThan(0f), "hair inside the hat is on the smooshed side");
        }

        [Test]
        public void SharpFold_SplitsTheSlotAlongIt_SharpSeam_SidesItsCopies()
        {
            ClothesPattern p = Keep(VestPattern.Create());
            PatternPiece front = p.pieces[0];
            // Across the front from side to side, its ends on the outline (as PatternFeatureTests draws it).
            PatternLine fold = PatternEditing.AddLine(front, PatternLineKind.Fold, new Vector2(-26f, 20f), new Vector2(26f, 20f));
            fold.foldAngle = 90f;
            fold.sharp = true;
            p.seams[0].sharp = true;
            Assert.That(p.FindProblems(), Is.Empty);
            PatternMesh mesh = PatternMesher.Build(p, PatternGeometry.Evaluate(p, null, null, null), 3f);
            Assert.That(mesh.creaseSharp.Contains(true), Is.True);
            Assert.That(SharpEdges.Any(p, mesh), Is.True);
            int n = mesh.VertexCount;
            var positions = new Vector3[n];
            for (int i = 0; i < n; i++) positions[i] = new Vector3(mesh.flat[i].x, mesh.flat[i].y, 0f) * 0.01f;
            int[] triangles = mesh.triangles.ToArray();
            IList<Vector2> uv = new List<Vector2>(mesh.uv);
            int[] sourceOf = null;
            int[] sides = SharpEdges.Apply(p, mesh, ref positions, ref triangles, ref uv, ref sourceOf, out int split);
            Assert.That(split, Is.GreaterThan(5), "the fold's points are split");
            Assert.That(positions.Length, Is.EqualTo(n + split));
            Assert.That(sides.Length, Is.EqualTo(positions.Length));
            for (int v = n; v < positions.Length; v++)
            {
                Assert.That(positions[v], Is.EqualTo(positions[sourceOf[v]]), "a copy is at its original's position");
                Assert.That(sides[v], Is.Not.EqualTo(sides[sourceOf[v]]), "and on another side");
            }
            bool seamSided = false;
            for (int i = 0; i < mesh.stitches.Count; i++)
                if (mesh.stitchSeam[i] == 0 && sides[mesh.stitches[i].x] != 0 && sides[mesh.stitches[i].x] != sides[mesh.stitches[i].y]) seamSided = true;
            Assert.That(seamSided, Is.True, "the sharp seam's two copies are on different sides");
        }
    }
}
#endif
