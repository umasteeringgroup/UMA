#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>
    /// Cutting, merging and linked mirrors on the pattern, the cloth views and girth measure, and what the
    /// bake adds: LOD slots, a full thickness, buttons and parts, and the topstitching texture.
    /// </summary>
    public sealed class EditingAndBakeExtrasTests
    {
        private const string Folder = "Assets/UMAProjectData/ClothesDesigner/Tests_Temp_Extras";
        private CapsuleBodyFixture capsule;
        private readonly List<Object> made = new List<Object>();
        private readonly List<System.IDisposable> disposables = new List<System.IDisposable>();

        [SetUp]
        public void Make() => capsule = new CapsuleBodyFixture();

        [TearDown]
        public void Clean()
        {
            foreach (System.IDisposable d in disposables) d.Dispose();
            disposables.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            capsule.Dispose();
            if (AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.DeleteAsset(Folder);
                if (AssetDatabase.FindAssets("", new[] { "Assets/UMAProjectData/ClothesDesigner" }).Length == 0) AssetDatabase.DeleteAsset("Assets/UMAProjectData/ClothesDesigner");
            }
        }

        private ClothesPattern Vest()
        {
            ClothesPattern pattern = VestPattern.Create();
            made.Add(pattern);
            return pattern;
        }

        private static PatternMesh Mesh(ClothesPattern pattern, float edgeCm = 2f) =>
            PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), edgeCm);

        private FitSession Session(ClothesPattern pattern, System.Action<ClothesFit> change = null)
        {
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = MeshLevel.Draft;
            change?.Invoke(fit);
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            disposables.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
            return session;
        }

        private static float Area(PatternPiece piece) => Mathf.Abs(PatternGeometry.SignedArea(PatternGeometry.SampleOutline(piece, 0.5f, null)));

        // ---- Step 5: cut, merge, linked mirror ----

        [Test]
        public void Cut_SplitsAPieceInTwo_SewsItBack_AndKeepsItsUVs()
        {
            ClothesPattern pattern = Vest();
            PatternPiece front = pattern.pieces[0];
            float area = Area(front);
            Vector2 uvBefore = UvLayout.ToUV(pattern, 0, new Vector2(-10f, 5f));
            PatternPiece half = PatternEditing.CutPiece(pattern, front, new Vector2(-40f, 20f), new Vector2(40f, 20f), true, out string problem);
            Assert.That(problem, Is.Null);
            Assert.That(half, Is.Not.Null);
            Assert.That(pattern.pieces.Count, Is.EqualTo(3));
            Assert.That(Area(front) + Area(half), Is.EqualTo(area).Within(area * 0.01f));
            // The cut crosses both side seams: each is cut in two at the cut, so both halves stay sewn.
            Assert.That(pattern.seams.Count, Is.EqualTo(7), "the vest's four seams, the side seams cut in two, and the cut");
            Assert.That(pattern.FindProblems(), Is.Empty);
            // The lower half is the one with the hem; its point keeps the same UV.
            PatternPiece lower = front.Bounds().yMin < half.Bounds().yMin ? front : half;
            Assert.That(UvLayout.ToUV(pattern, pattern.pieces.IndexOf(lower), new Vector2(-10f, 5f)), Is.EqualTo(uvBefore));
            PatternMesh mesh = Mesh(pattern);
            Assert.That(mesh.warnings, Is.Empty);
            FitSession session = Session(pattern);
            session.Step(240);
            FitMetrics m = session.Measure();
            // Measured 2026-11-23: every seam closes but one stitch at the right shoulder's armhole corner (5.6 mm),
            // the corner that carries the panel's weight and already stretches most on the uncut vest.
            Assert.That(m.meanStitchGapMm, Is.LessThan(0.5f), m.ToString());
            Assert.That(m.maxStitchGapMm, Is.LessThan(8f), m.ToString());
            Assert.That(m.particlesInBody, Is.EqualTo(0), m.ToString());
        }

        [Test]
        public void Cut_ThatMissesThePiece_IsRefused()
        {
            ClothesPattern pattern = Vest();
            Assert.That(PatternEditing.CutPiece(pattern, pattern.pieces[0], new Vector2(-40f, 90f), new Vector2(40f, 90f), true, out string problem), Is.Null);
            Assert.That(problem, Does.Contain("exactly twice"));
            Assert.That(pattern.pieces.Count, Is.EqualTo(2));
        }

        [Test]
        public void Merge_UndoesACut()
        {
            ClothesPattern pattern = Vest();
            PatternPiece front = pattern.pieces[0];
            float area = Area(front);
            PatternEditing.CutPiece(pattern, front, new Vector2(-40f, 20f), new Vector2(40f, 20f), true, out _);
            PatternSeam cut = pattern.seams[pattern.seams.Count - 1];
            PatternPiece merged = PatternEditing.MergePieces(pattern, cut, out string problem);
            Assert.That(problem, Is.Null);
            Assert.That(merged, Is.SameAs(front));
            Assert.That(pattern.pieces.Count, Is.EqualTo(2));
            Assert.That(Area(merged), Is.EqualTo(area).Within(area * 0.01f));
            Assert.That(pattern.seams.Count, Is.EqualTo(6), "the cut is gone; the side seams stay in two parts");
            Assert.That(pattern.FindProblems(), Is.Empty);
            Assert.That(Mesh(pattern).warnings, Is.Empty);
        }

        [Test]
        public void LinkedMirror_FollowsItsOriginal_AndKeepsItsSeams()
        {
            ClothesPattern pattern = Vest();
            PatternPiece source = PatternEditing.NewRectanglePiece("Sleeve", new Rect(100f, 0f, 20f, 30f));
            pattern.pieces.Add(source);
            PatternPiece copy = PatternEditing.MirrorCopy(source, "Sleeve Mirrored");
            copy.mirrorLinked = true;
            foreach (PatternPoint p in copy.points) p.position.x += 30f;
            pattern.pieces.Add(copy);
            PatternSeam seam = PatternEditing.Sew(pattern, copy, 1, source, 3, out _);
            source.points[2].position += new Vector2(4f, 0f);
            Assert.That(PatternEditing.UpdateLinkedMirrors(pattern), Is.EqualTo(1));
            Assert.That(copy.Bounds().width, Is.EqualTo(source.Bounds().width).Within(1e-3f), "the copy took the original's new shape");
            // Mirrored about the original's middle (110), then moved 30 cm along: its middle stays at 140.
            Assert.That(copy.Bounds().center.x, Is.EqualTo(140f).Within(0.01f), "and stays where it lies on the canvas");
            Assert.That(pattern.seams, Does.Contain(seam));
            Assert.That(copy.IndexOfEdge(seam.a.edgeId), Is.GreaterThanOrEqualTo(0), "the seam's edge kept its id");
            Assert.That(PatternEditing.UpdateLinkedMirrors(pattern), Is.EqualTo(0), "nothing to do once it matches");
            copy.mirrorLinked = false;
            source.points[2].position += new Vector2(4f, 0f);
            Assert.That(PatternEditing.UpdateLinkedMirrors(pattern), Is.EqualTo(0), "an unlinked copy is left alone");
        }

        // ---- Step 6: views and measuring ----

        [Test]
        public void ViewColours_RunFromBlueToRed()
        {
            Assert.That(DesignerSession.StretchColor(0f).b, Is.GreaterThan(0.9f));
            Assert.That(DesignerSession.StretchColor(0.3f).r, Is.GreaterThan(0.9f));
            Assert.That(DesignerSession.StretchColor(0.3f).g, Is.LessThan(0.3f));
            Assert.That(DesignerSession.DistanceColor(-0.001f, 0.006f).r, Is.GreaterThan(0.9f));
            Assert.That(DesignerSession.DistanceColor(0.1f, 0.006f).b, Is.GreaterThan(0.9f));
        }

        // ---- Steps 8 and 9: what the bake adds ----

        [Test]
        public void Bake_AddsLodSlots_AThickness_AndButtons()
        {
            ClothesPattern pattern = Vest();
            PatternEditing.AddTack(pattern, pattern.pieces[0], new Vector2(0f, 20f), pattern.pieces[0], new Vector2(0f, 20.5f), true);
            FitSession session = Session(pattern);
            session.Step(120);
            GarmentBaker.EnsureFolder(Folder);
            PublishSettings p = session.Fit.publish;
            p.slotName = "ExtrasTest";
            p.folder = Folder;
            p.material = DesignerSession.DefaultMaterial();
            Assert.That(p.material, Is.Not.Null, "the project's default UMA material");
            p.createWardrobeRecipe = false;
            p.addToGlobalLibrary = false;
            p.makeUvCheckTexture = false;
            p.addFacing = false;
            p.addRim = false;
            p.bakeNormalMap = false;
            p.lodLevels = 2;
            p.fullThickness = true;
            p.thicknessMm = 3f;
            p.addParts = true;
            Assert.That(GarmentBaker.Validate(session), Is.Empty);
            GarmentBaker.Result result = GarmentBaker.Bake(session);
            Assert.That(result.Published.Slot, Is.Not.Null);
            Assert.That(result.LodSlots.Count, Is.EqualTo(2));
            Assert.That(result.LodSlots[0].slotName, Is.EqualTo("ExtrasTest_LOD1"));
            Assert.That(result.LodTriangles[0], Is.LessThan(result.Triangles));
            Assert.That(result.LodTriangles[1], Is.LessThan(result.LodTriangles[0]));
            Assert.That(result.LodSlots[0].meshData.vertices.Length, Is.LessThan(result.Published.Slot.meshData.vertices.Length));
            // Thickness: a turned-in copy of the whole cloth, so about twice the cloth's triangles.
            int cloth = session.Mesh.TriangleCount;
            Assert.That(result.FacingTriangles, Is.EqualTo(cloth));
            Assert.That(result.RimTriangles, Is.GreaterThan(0));
            Assert.That(result.PartTriangles, Is.EqualTo(16 + 32), "one button: a 16-sided top and its side");
            Assert.That(result.Triangles, Is.EqualTo(cloth * 2 + result.RimTriangles + result.PartTriangles));
            Assert.That(result.MaxWeightSumError, Is.LessThan(1e-4f));
            Assert.That(result.SeamWeightsMatch, Is.True);
            // The button stands out from the cloth, at most its height plus a little.
            UMAMeshData md = result.Published.Slot.meshData;
            Assert.That(md.vertices.Length, Is.EqualTo(result.Vertices));
        }

        [Test]
        public void Button_SitsOnTheCloth_FacingOut_WithTheClothsUVs()
        {
            ClothesPattern pattern = Vest();
            PatternEditing.AddTack(pattern, pattern.pieces[0], new Vector2(0f, 20f), pattern.pieces[0], new Vector2(0f, 20.5f), true).buttonDiameterMm = 15f;
            FitSession session = Session(pattern);
            session.Step(120);
            Vector3[] x = (Vector3[])session.SyncPositions().Clone();
            int[] facingOut = session.Mesh.triangles.ToArray();
            if (session.Arranged.flipTriangles) for (int t = 0; t < facingOut.Length; t += 3) { int s = facingOut[t + 1]; facingOut[t + 1] = facingOut[t + 2]; facingOut[t + 2] = s; }
            var pos = new List<Vector3>(); var tris = new List<int>(); var uv = new List<Vector2>(); var source = new List<int>(); var messages = new List<string>();
            int added = GarmentExtras.AddParts(pattern, session.Mesh, x, facingOut, pos, tris, uv, source, messages);
            Assert.That(added, Is.EqualTo(48), string.Join(" ", messages));
            // The top's middle stands out from the capsule by the button's height; every top triangle faces out.
            Vector3 top = pos[0];
            float radial = new Vector2(top.x, top.z).magnitude;
            Vector3 under = x[source[0]];
            Assert.That(radial - new Vector2(under.x, under.z).magnitude, Is.EqualTo(0.003f).Within(0.0015f), "15 mm button, 3 mm high, just above the cloth");
            for (int t = 0; t < 16 * 3; t += 3)
            {
                Vector3 n = Vector3.Cross(pos[tris[t + 1]] - pos[tris[t]], pos[tris[t + 2]] - pos[tris[t]]);
                Vector3 outward = new Vector3(top.x, 0f, top.z).normalized;
                Assert.That(Vector3.Dot(n.normalized, outward), Is.GreaterThan(0.8f), "top triangle " + t / 3);
            }
            // Its UVs are the cloth's at that spot, spread over the button's size.
            Vector2 clothUv = UvLayout.ToUV(pattern, 0, new Vector2(0f, 20f));
            Assert.That((uv[0] - clothUv).magnitude, Is.LessThan(1e-5f));
            float spread = 0f;
            foreach (Vector2 u in uv) spread = Mathf.Max(spread, (u - clothUv).magnitude);
            Assert.That(spread, Is.EqualTo(0.75f * pattern.pieces[0].uvIsland.scale).Within(0.2f * pattern.pieces[0].uvIsland.scale), "about the button's radius in the texture");
        }

        [Test]
        public void Topstitch_IsDrawnBesideTheSewnEdges_AndNowhereElse()
        {
            ClothesPattern pattern = Vest();
            PatternMesh mesh = Mesh(pattern);
            Texture2D none = GarmentExtras.DrawTopstitch(pattern, 2048, Color.yellow);
            made.Add(none);
            int Inked(Texture2D t) { int k = 0; foreach (Color32 c in t.GetPixels32()) if (c.a > 0) k++; return k; }
            Assert.That(Inked(none), Is.EqualTo(0));
            pattern.seams[0].topstitch = TopstitchKind.Double;
            Texture2D two = GarmentExtras.DrawTopstitch(pattern, 2048, Color.yellow);
            made.Add(two);
            pattern.seams[0].topstitch = TopstitchKind.Single;
            Texture2D one = GarmentExtras.DrawTopstitch(pattern, 2048, Color.yellow);
            made.Add(one);
            Assert.That(Inked(one), Is.GreaterThan(50));
            Assert.That(Inked(two), Is.GreaterThan(Inked(one) * 1.6f));
            // Every stitch lies on a piece's island, near its sewn side.
            Color32[] px = one.GetPixels32();
            var islands = new Rect[2];
            for (int i = 0; i < 2; i++) islands[i] = pattern.pieces[i].uvIsland.reserved;
            for (int y = 0; y < 2048; y++)
                for (int x = 0; x < 2048; x++)
                {
                    if (px[y * 2048 + x].a == 0) continue;
                    var uv = new Vector2((x + 0.5f) / 2048f, (y + 0.5f) / 2048f);
                    Assert.That(islands[0].Contains(uv) || islands[1].Contains(uv), Is.True, "stitch at " + uv);
                }
            Assert.That(mesh.warnings, Is.Empty);
        }
    }
}
#endif
