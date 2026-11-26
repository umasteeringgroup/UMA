#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The sample patterns, placing pieces round arms, legs and the head, and converting modelled clothes.</summary>
    public sealed class SamplePatternsTests
    {
        private readonly List<Object> made = new List<Object>();
        private readonly List<FitSession> sessions = new List<FitSession>();

        [TearDown]
        public void Clean()
        {
            foreach (FitSession s in sessions) s.Dispose();
            sessions.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        [Test]
        public void Samples_AreThirtyOne_AndEachMeshesWithEverySeamSewn()
        {
            Assert.That(SamplePatterns.All.Length, Is.EqualTo(31));
            var names = new HashSet<string>();
            foreach (var sample in SamplePatterns.All)
            {
                Assert.That(names.Add(sample.name), Is.True, "two samples are called " + sample.name);
                ClothesPattern pattern = sample.make();
                made.Add(pattern);
                Assert.That(pattern.FindProblems(), Is.Empty, sample.name);
                PatternMesh mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);
                Assert.That(mesh.unrecovered, Is.Empty, sample.name + ": " + string.Join(", ", mesh.unrecovered));
                var sewn = new HashSet<int>(mesh.stitchSeam);
                for (int s = 0; s < pattern.seams.Count; s++) Assert.That(sewn.Contains(s), Is.True, sample.name + ": seam " + s + " has no stitches");
                foreach (PatternSeam seam in pattern.seams)
                {
                    // Rib bands and cuffs are drawn shorter on purpose, to gather what they are sewn to: by up to 12 cm, or a
                    // gathered bodice or skirt taken in by up to half (the Elastic Waist Dress's skirt into its band).
                    float la = PatternEditing.SpanLengthCm(pattern.FindPiece(seam.a.pieceId), seam.a), lb = PatternEditing.SpanLengthCm(pattern.FindPiece(seam.b.pieceId), seam.b);
                    bool gather = Mathf.Abs(la - lb) < 12f || Mathf.Min(la, lb) >= 0.5f * Mathf.Max(la, lb);
                    Assert.That(gather, Is.True, sample.name + ": a seam's sides differ by more than a gather (" + la.ToString("0.#") + " and " + lb.ToString("0.#") + " cm)");
                }
            }
        }

        [Test]
        public void Samples_ArePlacedOnTheirParts()
        {
            ClothesPattern tee = SamplePatterns.Create("T-Shirt"), jeans = SamplePatterns.Create("Jeans"), beanie = SamplePatterns.Create("Beanie"), socks = SamplePatterns.Create("Socks");
            made.AddRange(new Object[] { tee, jeans, beanie, socks });
            Assert.That(tee.FindPiece(tee.pieces[2].id).placement.part, Is.EqualTo(BodyPart.RightArm));
            Assert.That(tee.pieces[3].placement.part, Is.EqualTo(BodyPart.LeftArm));
            foreach (PatternPiece p in jeans.pieces) Assert.That(p.placement.part, Is.EqualTo(p.name.Contains("Waistband") ? BodyPart.Torso : p.name.StartsWith("Left") ? BodyPart.LeftLeg : BodyPart.RightLeg), p.name);
            foreach (PatternPiece p in beanie.pieces) Assert.That(p.placement.part, Is.EqualTo(BodyPart.Head));
            foreach (PatternPiece p in socks.pieces) Assert.That(p.placement.flat, Is.True);
        }

        [Test]
        public void SaveAll_WritesEverySample_AndKeepsGuidsWhenWrittenAgain()
        {
            string folder = "Assets/UMAProjectData/ClothesDesigner/Tests_Temp_Samples";
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/UMAProjectData")) UnityEditor.AssetDatabase.CreateFolder("Assets", "UMAProjectData");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/UMAProjectData/ClothesDesigner")) UnityEditor.AssetDatabase.CreateFolder("Assets/UMAProjectData", "ClothesDesigner");
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/UMAProjectData/ClothesDesigner", "Tests_Temp_Samples");
            try
            {
                List<string> first = SamplePatterns.SaveAll(folder);
                Assert.That(first.Count, Is.EqualTo(31));
                string guid = UnityEditor.AssetDatabase.AssetPathToGUID(first[0]);
                List<string> second = SamplePatterns.SaveAll(folder);
                Assert.That(UnityEditor.AssetDatabase.AssetPathToGUID(second[0]), Is.EqualTo(guid));
                var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<ClothesPattern>(second[0]);
                Assert.That(loaded.pieces.Count, Is.GreaterThan(1));
                Assert.That(loaded.FindProblems(), Is.Empty);
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(folder);
                if (UnityEditor.AssetDatabase.FindAssets("", new[] { "Assets/UMAProjectData/ClothesDesigner" }).Length == 0) UnityEditor.AssetDatabase.DeleteAsset("Assets/UMAProjectData/ClothesDesigner");
            }
        }

        [Test]
        public void TorsoAngleForLeg_SplitsTheHipsAtTheCentreFrontAndBack()
        {
            // The left leg's halves cover the torso's left front and left back quarters.
            Assert.That(Arrangement.TorsoAngleForLeg(true, 0f), Is.EqualTo(45f).Within(1e-4f));
            Assert.That(Arrangement.TorsoAngleForLeg(true, 180f), Is.EqualTo(135f).Within(1e-4f));
            Assert.That(Arrangement.TorsoAngleForLeg(true, -89f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(Arrangement.TorsoAngleForLeg(false, 0f), Is.EqualTo(-45f).Within(1e-4f));
            Assert.That(Arrangement.TorsoAngleForLeg(false, 180f), Is.EqualTo(-135f).Within(1e-4f));
            Assert.That(Arrangement.TorsoAngleForLeg(false, 89f), Is.EqualTo(-0.5f).Within(1e-4f));
        }

        [Test]
        public void Converter_RoundTrip_GivesBackThePiecesTheirSizesAndTheirSeams()
        {
            using var capsule = new CapsuleBodyFixture();
            ClothesPattern vest = VestPattern.Create();
            made.Add(vest);
            var fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = vest;
            var session = new FitSession { Pattern = vest, Fit = fit, Body = capsule.Body, PreferCpu = true };
            sessions.Add(session);
            session.Rebuild();
            session.Step(200);
            PatternMesh mesh = session.Mesh;
            UvLayout.ApplyUVs(vest, mesh);
            Vector3[] p = (Vector3[])session.SyncPositions().Clone();
            int[] group = GarmentBaker.WeldGroups(p.Length, mesh.stitches);
            var sum = new Dictionary<int, Vector3>(); var count = new Dictionary<int, int>();
            for (int v = 0; v < p.Length; v++) { sum[group[v]] = (sum.TryGetValue(group[v], out Vector3 s) ? s : Vector3.zero) + p[v]; count[group[v]] = (count.TryGetValue(group[v], out int c) ? c : 0) + 1; }
            for (int v = 0; v < p.Length; v++) p[v] = sum[group[v]] / count[group[v]];
            int[] triangles = mesh.triangles.ToArray();
            if (session.Arranged.flipTriangles) for (int t = 0; t < triangles.Length; t += 3) { int x = triangles[t + 1]; triangles[t + 1] = triangles[t + 2]; triangles[t + 2] = x; }

            PatternFromMesh.Result result = PatternFromMesh.Convert("Vest Again", p, mesh.uv.ToArray(), triangles, capsule.Body.Landmarks);
            made.Add(result.Pattern);
            Assert.That(result.Pieces, Is.EqualTo(2));
            Assert.That(result.MirroredIslands, Is.EqualTo(0), "drawn as seen from outside");
            Assert.That(result.Seams, Is.GreaterThanOrEqualTo(4));
            Assert.That(result.Pattern.FindProblems(), Is.Empty);
            // The same sizes, less the little the drape's folds took up.
            // The vest was sized for the capsule: 52 x 62 cm drawn, scaled by its dimensions.
            float width = 52f * mesh.scales[0].sx, height = 62f * mesh.scales[0].sy;
            foreach (PatternPiece piece in result.Pattern.pieces)
            {
                Rect b = piece.Bounds();
                Assert.That(b.width, Is.EqualTo(width).Within(width * 0.05f), piece.name);
                Assert.That(b.height, Is.EqualTo(height).Within(height * 0.05f), piece.name);
                Assert.That(piece.placement.part, Is.EqualTo(BodyPart.Torso));
            }
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(result.Pattern.pieces[0].placement.angle, result.Pattern.pieces[1].placement.angle)), Is.GreaterThan(120f), "one front, one back");
            // The openings are where the vest's are: neck, armholes and hem; the sides and shoulders are sewn.
            int openings = 0;
            foreach (PatternPiece piece in result.Pattern.pieces) foreach (PatternEdge e in piece.edges) if (e.opening) openings++;
            Assert.That(openings, Is.GreaterThanOrEqualTo(8));
            // Its UVs come back: a mesh made from the converted pattern lies in the same texture rectangle.
            PatternMesh again = PatternMesher.Build(result.Pattern, PatternGeometry.Evaluate(result.Pattern, null, null, null), 2f);
            UvLayout.ApplyUVs(result.Pattern, again);
            Rect original = UvRect(mesh.uv), converted = UvRect(again.uv);
            Assert.That(Vector2.Distance(original.min, converted.min), Is.LessThan(0.02f));
            Assert.That(Vector2.Distance(original.max, converted.max), Is.LessThan(0.02f));
            // And it sews up again.
            var fit2 = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit2);
            fit2.pattern = result.Pattern;
            var drape = new FitSession { Pattern = result.Pattern, Fit = fit2, Body = capsule.Body, PreferCpu = true };
            sessions.Add(drape);
            drape.Rebuild();
            drape.Step(200);
            Assert.That(drape.Measure().maxStitchGapMm, Is.LessThan(2f), drape.Measure().ToString());
        }

        [Test]
        public void Converter_TakesTheBakedFoldsOut()
        {
            // A flat 40 cm square with folds modelled into it: the piece comes out near the flat cloth's size.
            const int n = 41;
            var positions = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = i / (n - 1f) * 0.4f, y = 1f + j / (n - 1f) * 0.4f;
                    positions[j * n + i] = new Vector3(x, y, 0.2f + 0.012f * Mathf.Sin(x * 2f * Mathf.PI / 0.04f));
                    uv[j * n + i] = new Vector2(i / (n - 1f), j / (n - 1f)) * 0.5f;
                }
            var triangles = new List<int>();
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            PatternFromMesh.Result folded = PatternFromMesh.Convert("Folded", positions, uv, triangles.ToArray(), null);
            PatternFromMesh.Result kept = PatternFromMesh.Convert("Folded", positions, uv, triangles.ToArray(), null, new PatternFromMesh.Settings { FoldSmoothing = 0 });
            made.Add(folded.Pattern); made.Add(kept.Pattern);
            float area(ClothesPattern p) { Rect b = p.pieces[0].Bounds(); return b.width * b.height; }
            Assert.That(area(kept.Pattern), Is.GreaterThan(area(folded.Pattern) * 1.15f), "the folds add surface the converter takes out");
            Assert.That(folded.MeanFoldShrink, Is.GreaterThan(0.1f));
        }

        private static Rect UvRect(List<Vector2> uv)
        {
            Vector2 min = uv[0], max = uv[0];
            foreach (Vector2 q in uv) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
#endif
