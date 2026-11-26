#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The garment generator: a style and a body in, a sewn pattern sized to the body out.</summary>
    public sealed class GarmentGeneratorTests
    {
        private const string Folder = "Assets/UMAProjectData/ClothesDesigner/Tests_Temp_Generator";

        /// <summary>Landmarks with the built-in body's measurements, for checking the sizes keep their drafted widths on it.</summary>
        private static BodyLandmarks Landmarks(GarmentBody b) => new BodyLandmarks
        {
            valid = true, chestCm = b.chest, waistCm = b.waist, hipCm = b.hip, shoulderTopY = 1.68f, waistY = 1.68f - b.waistY * 0.01f,
            armLengthCm = b.armLength, inseamCm = b.inseam, upperArmCm = b.upperArm
        };

        private static void AssertSound(ClothesPattern pattern, string label)
        {
            PatternMesh mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2.5f);
            Assert.That(mesh.warnings, Is.Empty, label);
            Assert.That(mesh.unrecovered, Is.Empty, label);
            Assert.That(mesh.VertexCount, Is.GreaterThan(100), label);
            Assert.That(UvLayout.FindProblems(pattern, mesh), Is.Empty, label);
            foreach (PatternSeam seam in pattern.seams)
            {
                Assert.That(pattern.FindPiece(seam.a.pieceId), Is.Not.Null, label);
                Assert.That(pattern.FindPiece(seam.b.pieceId), Is.Not.Null, label);
            }
        }

        [Test]
        public void EveryPreset_DraftsQuickly_MeshesCleanly_AndKeepsItsSizesOnItsBody()
        {
            GarmentBody body = GarmentBody.Default;
            BodyLandmarks marks = Landmarks(body);
            var clock = new System.Diagnostics.Stopwatch();
            long slowest = 0;
            string slowestName = null;
            foreach (GarmentPreset preset in GarmentPresets.All)
            {
                clock.Restart();
                ClothesPattern pattern = GarmentGenerator.Generate(preset.Make(), body, false);
                clock.Stop();
                if (clock.ElapsedMilliseconds > slowest) { slowest = clock.ElapsedMilliseconds; slowestName = preset.Name; }
                try
                {
                    Assert.That(pattern.name, Is.EqualTo(preset.Name));
                    Assert.That(GarmentStyle.FromJson(pattern.generatorStyle).preset, Is.EqualTo(preset.Name), "the style is kept on the pattern");
                    Assert.That(pattern.uvLayout.quickPacked, Is.True);
                    AssertSound(pattern, preset.Name);
                    // Drafted to this body's measurements, so its sizes leave every piece as drawn on it.
                    foreach (PieceScale s in PatternGeometry.Evaluate(pattern, marks, null, null))
                    {
                        Assert.That(s.sx, Is.EqualTo(1f).Within(0.002f), preset.Name);
                        Assert.That(s.sy, Is.EqualTo(1f).Within(0.002f), preset.Name);
                    }
                }
                finally { Object.DestroyImmediate(pattern); }
            }
            // Measured 5-26 ms each (2026-11-23); a generous limit, for slow machines.
            Assert.That(slowest, Is.LessThan(250), "slowest: " + slowestName);
        }

        [Test]
        public void PoloPreset_TurnsItsCollarDown_TheLeafHangingPastTheStand()
        {
            // A polo collar falls over a firm stand. The leaf hangs 2.5 cm past the stand at the back: 1 cm, as a tailor
            // cuts it, rode up on the cloth and showed the stand.
            ClothesPattern polo = GarmentGenerator.Generate(GarmentPresets.Find("Polo Shirt").Make(), GarmentBody.Default, false);
            try
            {
                PatternPiece stand = polo.pieces.Find(p => p.name == "Collar Stand"), leaf = polo.pieces.Find(p => p.name == "Collar");
                Assert.That(stand, Is.Not.Null);
                Assert.That(leaf, Is.Not.Null);
                PatternSeam fold = polo.seams.Find(s => s.folded);
                Assert.That(fold, Is.Not.Null);
                Assert.That(new[] { fold.a.pieceId, fold.b.pieceId }, Is.EquivalentTo(new[] { stand.id, leaf.id }));
                Assert.That(leaf.layer, Is.GreaterThan(stand.layer));
                // The outer edge's middle (the back): a Bezier from point 0 to point 1 with the edge's two handles.
                PatternEdge outer = leaf.edges[0];
                float ends = leaf.points[0].position.y;
                float back = ends + 3f * (outer.handleA.y + outer.handleB.y) / 8f;
                const float standHeight = 3f; // a 5 cm collar: 60% of it
                Assert.That(-back, Is.EqualTo(standHeight + 2.5f).Within(0.05f), "the leaf's depth at the back");
                Assert.That(-ends, Is.GreaterThanOrEqualTo(-back + 1f - 0.01f), "the points hang lower than the back");
                AssertSound(polo, "Polo Shirt");
            }
            finally { Object.DestroyImmediate(polo); }
        }

        [Test]
        public void RandomStyles_OfEveryType_DraftPatternsThatMesh()
        {
            var random = new System.Random(1234);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            GarmentBody body = GarmentBody.Default;
            for (int i = 0; i < 90; i++)
            {
                var type = (GarmentType)(i % 6);
                GarmentStyle s = GarmentPresets.Default(type);
                s.length = R(0f, 9f); s.easeCm = R(-10f, 40f); s.waistShaping = R(0f, 1f); s.flare = R(0f, 1f); s.hemDropCm = R(0f, 10f);
                s.shoulderCm = R(-3f, 8f); s.neckline = (NecklineShape)random.Next(5); s.neckWidth = R(0f, 1f); s.neckDepthCm = R(2f, 30f);
                s.backNeckDepthCm = R(1f, 15f); s.collar = (CollarStyle)random.Next(5); s.collarHeightCm = R(3f, 12f); s.front = (FrontStyle)random.Next(3);
                s.overlapCm = R(0f, 20f); s.sleeve = random.Next(4) == 0 ? 0f : R(0.1f, 1.1f); s.sleeveEaseCm = R(0f, 30f); s.sleeveFlare = R(0f, 1f);
                s.strapCm = R(3f, 12f); s.cuffs = (BandStyle)random.Next(3); s.hemBand = (BandStyle)random.Next(3); s.bandHeightCm = R(3f, 10f);
                s.rise = R(0f, 1f); s.legLength = R(0f, 5f); s.thighEaseCm = R(-10f, 30f); s.hemWidth = R(0f, 1.6f); s.waistband = (BandStyle)random.Next(3);
                s.legCuffs = (BandStyle)random.Next(3); s.pleats = random.Next(3) == 0 ? random.Next(1, 10) : 0;
                string label = "style " + i + ": " + s.ToJson();
                ClothesPattern pattern = GarmentGenerator.Generate(s, body, false);
                try { AssertSound(pattern, label); }
                finally { Object.DestroyImmediate(pattern); }
            }
        }

        [Test]
        public void Sizes_FollowTheBody_ItIsDraftedTo()
        {
            GarmentBody small = GarmentBody.Default, big = GarmentBody.Default;
            big.chest += 20f; big.waist += 20f; big.hip += 20f;
            float Width(ClothesPattern p, string piece, float y)
            {
                List<Vector2> outline = PatternGeometry.SampleOutline(p.pieces.Find(x => x.name == piece), 0.5f, null);
                Assert.That(PatternGeometry.WidthAt(outline, y, out float min, out float max), Is.True);
                return max - min;
            }
            GarmentStyle tee = GarmentPresets.Find("T-Shirt").Make();
            ClothesPattern a = GarmentGenerator.Generate(tee, small, false), b = GarmentGenerator.Generate(tee, big, false);
            try
            {
                // A quarter of the extra chest on each of front and back's halves: 10 cm across a piece.
                float line = 40f;
                Assert.That(Width(b, "Front", line) - Width(a, "Front", line), Is.EqualTo(10f).Within(0.5f));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }

            GarmentBody tall = GarmentBody.Default;
            tall.ankleY += 10f; tall.floorY += 10f; tall.kneeY += 5f;
            GarmentStyle jeans = GarmentPresets.Find("Jeans").Make();
            a = GarmentGenerator.Generate(jeans, small, false); b = GarmentGenerator.Generate(jeans, tall, false);
            try
            {
                float h(ClothesPattern p) => p.pieces.Find(x => x.name == "Left Front").Bounds().height;
                Assert.That(h(b) - h(a), Is.EqualTo(10f).Within(0.5f), "ankle-length legs grow with the legs");
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void Style_SurvivesJson_AndIsClampedToItsRanges()
        {
            GarmentStyle s = GarmentPresets.Find("Kimono").Make();
            GarmentStyle back = GarmentStyle.FromJson(s.ToJson());
            Assert.That(back.ToJson(), Is.EqualTo(s.ToJson()));
            s.easeCm = 500f; s.sleeve = -3f; s.length = 99f; s.pleats = 40; s.fabric = "No Such Cloth";
            s.Clamp();
            Assert.That(s.easeCm, Is.EqualTo(60f));
            Assert.That(s.sleeve, Is.EqualTo(0f));
            Assert.That(s.length, Is.EqualTo(GarmentGenerator.LengthStops.Length - 1));
            Assert.That(s.pleats, Is.EqualTo(12));
            Assert.That(s.fabric, Is.EqualTo(FabricPresets.DefaultName));
            Assert.That(GarmentStyle.FromJson("not json"), Is.Null);
            Assert.That(GarmentStyle.FromJson(null), Is.Null);
        }

        [Test]
        public void LengthStops_RunDownTheBody()
        {
            GarmentBody b = GarmentBody.Default;
            for (int i = 1; i < GarmentGenerator.LengthStops.Length; i++) Assert.That(GarmentGenerator.LengthY(b, i), Is.GreaterThan(GarmentGenerator.LengthY(b, i - 1)), GarmentGenerator.LengthStops[i]);
            for (int i = 1; i < GarmentGenerator.LegStops.Length; i++) Assert.That(GarmentGenerator.LegY(b, i), Is.GreaterThan(GarmentGenerator.LegY(b, i - 1)), GarmentGenerator.LegStops[i]);
            Assert.That(GarmentGenerator.LengthY(b, 1), Is.EqualTo(b.waistY));
            Assert.That(GarmentGenerator.StopName(GarmentGenerator.LengthStops, 6.2f), Is.EqualTo("Knee"));
            Assert.That(GarmentGenerator.SleeveName(0f), Is.EqualTo("Sleeveless"));
            Assert.That(GarmentGenerator.SleeveName(1f), Is.EqualTo("Long"));
        }

        [Test]
        public void Body_IsReadFromTheBodyInTheDesigner()
        {
            var capsule = new CapsuleBodyFixture();
            try
            {
                BodyLandmarks m = capsule.Body.Landmarks;
                m.chestCm = 101f; m.waistCm = 99f; m.hipCm = 100f; m.neckCm = 30f;
                GarmentBody b = GarmentBody.From(capsule.Body);
                Assert.That(b.measured, Is.True);
                Assert.That(b.chest, Is.EqualTo(101f));
                Assert.That(b.waist, Is.EqualTo(99f));
                Assert.That(b.neck, Is.EqualTo(30f));
                Assert.That(b.waistY, Is.EqualTo(40f).Within(0.01f));
                Assert.That(b.hipY, Is.EqualTo(65f).Within(0.01f));
                // Measured round the cylinder (radius 16 cm) where the body gives a section, its girths elsewhere.
                Assert.That(b.TorsoGirth(30f), Is.InRange(95f, 102f));
                Assert.That(GarmentBody.From(null).measured, Is.False);
            }
            finally { capsule.Dispose(); }
        }

        [Test]
        public void GeneratedClothes_Drape_AndBake_WithTheUvsPackedTightlyFirst()
        {
            var capsule = new CapsuleBodyFixture();
            GarmentBody body = GarmentBody.From(capsule.Body);
            var made = new List<Object>();
            try
            {
                foreach (string name in new[] { "Tank Top", "Mini Skirt" })
                {
                    ClothesPattern pattern = GarmentGenerator.Generate(GarmentPresets.Find(name).Make(), body, false);
                    ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
                    made.Add(pattern); made.Add(fit);
                    fit.pattern = pattern;
                    fit.overrideMeshLevel = true;
                    fit.meshLevel = MeshLevel.Draft;
                    var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
                    try
                    {
                        session.Rebuild();
                        Assert.That(session.HasCloth, Is.True, name + ": " + string.Join(" ", session.Messages));
                        session.Step(150);
                        FitMetrics m = session.Measure();
                        Assert.That(m.maxStitchGapMm, Is.LessThan(6f), name + " " + m);
                        Assert.That(m.particlesInBody, Is.Zero, name + " " + m);
                        if (name != "Mini Skirt") continue;

                        GarmentBaker.EnsureFolder(Folder);
                        PublishSettings p = fit.publish;
                        p.slotName = "GeneratorTest"; p.folder = Folder; p.material = DesignerSession.DefaultMaterial();
                        p.createWardrobeRecipe = false; p.addToGlobalLibrary = false; p.makeUvCheckTexture = false; p.bakeNormalMap = false; p.addTopstitch = false;
                        Assert.That(pattern.uvLayout.quickPacked, Is.True);
                        GarmentBaker.Result result = GarmentBaker.Bake(session);
                        Assert.That(result.Published.Slot, Is.Not.Null);
                        Assert.That(pattern.uvLayout.quickPacked, Is.False, "the bake packs a quick layout tightly");
                        Assert.That(result.Messages, Has.Some.Contains("Packed the UVs tightly"));
                        Assert.That(result.SeamWeightsMatch, Is.True);
                    }
                    finally { session.Dispose(); }
                }
            }
            finally
            {
                foreach (Object o in made) Object.DestroyImmediate(o);
                capsule.Dispose();
                if (AssetDatabase.IsValidFolder(Folder))
                {
                    AssetDatabase.DeleteAsset(Folder);
                    if (AssetDatabase.FindAssets("", new[] { "Assets/UMAProjectData/ClothesDesigner" }).Length == 0) AssetDatabase.DeleteAsset("Assets/UMAProjectData/ClothesDesigner");
                }
            }
        }

        [Test]
        public void QuickPack_IsFast_AndRepackIsTighter()
        {
            ClothesPattern pattern = GarmentGenerator.Generate(GarmentPresets.Find("Shirt").Make(), GarmentBody.Default, false);
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                UvLayout.QuickPack(pattern);
                long quick = clock.ElapsedMilliseconds;
                Assert.That(pattern.uvLayout.quickPacked, Is.True);
                float quickDensity = UvLayout.QuickPack(pattern);
                float tightDensity = UvLayout.Repack(pattern);
                Assert.That(pattern.uvLayout.quickPacked, Is.False);
                Assert.That(tightDensity, Is.GreaterThanOrEqualTo(quickDensity * 0.999f), "outline packing is never looser than boxes");
                Assert.That(quick, Is.LessThan(100));
            }
            finally { Object.DestroyImmediate(pattern); }
        }
    }
}
#endif
