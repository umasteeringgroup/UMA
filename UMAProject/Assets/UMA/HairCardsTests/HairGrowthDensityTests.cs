#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairGuideGenerationResult GenerateDensityTest(float paint, float multiplier = 1f, int budget = 100)
        {
            HairGroup group = groom.Groups[0];
            Array.Fill(group.FindMap(HairMapKind.GrowthArea).values, paint);
            Array.Fill(group.FindMap(HairMapKind.Density).values, multiplier);
            return HairGuideGenerator.Generate(groom, group, new HairGuideGenerationSettings
            { guideCount = budget, minimumRootSpacing = 0f, rootUniformity = 0f, pointsPerGuide = 2, seed = 7831 });
        }

        [TestCase(0f, 0)] [TestCase(0.25f, 25)] [TestCase(0.5f, 50)] [TestCase(1f, 100)]
        public void GrowthDensityPaintScalesActualGuideCount(float paint, int expected)
        {
            HairGuide[] authored = groom.Groups[0].guides.ToArray();
            HairGuideGenerationResult result = GenerateDensityTest(paint);
            Assert.That(result.fullDensityGuideCount, Is.EqualTo(100));
            Assert.That(result.averagePaintedDensity, Is.EqualTo(paint).Within(1e-6f));
            Assert.That(result.densityAdjustedGuideCount, Is.EqualTo(expected));
            Assert.That(result.guides.Count, Is.EqualTo(expected));
            Assert.That(groom.Groups[0].guides, Is.EqualTo(authored), "Painting/preview never replace authored guides implicitly.");
            if (expected > 0) Assert.That(result.warnings, Is.Empty);
        }

        [TestCase(1f, 0f, 0)] [TestCase(1f, 0.5f, 50)] [TestCase(0.5f, 0.5f, 25)] [TestCase(0.25f, 1f, 25)]
        public void GrowthDensityOptionalMultiplierScalesBothBudgetAndPlacement(float paint, float multiplier, int expected)
        {
            HairGuideGenerationResult result = GenerateDensityTest(paint, multiplier);
            Assert.That(result.guides.Count, Is.EqualTo(expected));
            Assert.That(result.averagePaintedDensity, Is.EqualTo(paint * multiplier).Within(1e-6f));
            if (multiplier == 0f) Assert.That(result.warnings[0], Does.Contain("Density Multiplier"));
        }

        [Test]
        public void GrowthDensityLowTargetsRoundOnceAndSpacingReportsAdjustedTarget()
        {
            HairGuideGenerationResult tiny = GenerateDensityTest(0.1f, budget: 1);
            Assert.That(tiny.guides, Is.Empty); Assert.That(tiny.warnings[0], Does.Contain("below one guide"));
            Assert.That(GenerateDensityTest(0.5f, budget: 1).guides.Count, Is.EqualTo(1));
            Array.Fill(groom.Groups[0].FindMap(HairMapKind.GrowthArea).values, 0.5f);
            HairGuideGenerationResult spaced = HairGuideGenerator.Generate(groom, groom.Groups[0],
                new HairGuideGenerationSettings { guideCount = 10, minimumRootSpacing = 10f, rootUniformity = 0f });
            Assert.That(spaced.densityAdjustedGuideCount, Is.EqualTo(5));
            Assert.That(spaced.guides.Count, Is.EqualTo(1));
            Assert.That(spaced.warnings[0], Does.Contain("1 of 5 density-adjusted"));
        }

        [Test]
        public void GrowthDensityUsesSurfaceAreaNotVertexCountAndIgnoresUnpaintedBody()
        {
            // Areas 0.5, 1.5, and 5000: only the first two belong to the painted footprint.
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward,
                new Vector3(4f, 0f, 0f), new Vector3(7f, 0f, 0f), new Vector3(4f, 0f, 1f),
                new Vector3(20f, 0f, 0f), new Vector3(120f, 0f, 0f), new Vector3(20f, 0f, 100f) };
            sourceMesh.triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            sourceMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            groom.SetSource(sourceMesh, "mesh:test", "TestRace", "TestScalp");
            HairGroup group = groom.Groups[0];
            group.FindMap(HairMapKind.GrowthArea).values = new[] { 0.25f, 0.25f, 0.25f, 1f, 1f, 1f, 0f, 0f, 0f };
            Array.Fill(group.FindMap(HairMapKind.Density).values, 1f);
            HairGuideGenerationResult result = HairGuideGenerator.Generate(groom, group,
                new HairGuideGenerationSettings { guideCount = 160, minimumRootSpacing = 0f, rootUniformity = 0f, pointsPerGuide = 2 });
            Assert.That(result.paintedSurfaceArea, Is.EqualTo(2d).Within(1e-6));
            Assert.That(result.averagePaintedDensity, Is.EqualTo(0.8125f).Within(1e-6f));
            Assert.That(result.guides.Count, Is.EqualTo(130));
            foreach (HairGuide guide in result.guides) Assert.That(guide.root.CachedLocalPosition.x, Is.LessThan(10f));
        }

        [Test]
        public void GrowthDensityComplementaryMasksStillGenerateInsideTriangles()
        {
            HairGroup group = groom.Groups[0];
            group.FindMap(HairMapKind.GrowthArea).values = new[] { 1f, 0f, 0f };
            group.FindMap(HairMapKind.Density).values = new[] { 0f, 1f, 0f };
            HairGuideGenerationResult result = HairGuideGenerator.Generate(groom, group,
                new HairGuideGenerationSettings { guideCount = 120, minimumRootSpacing = 0f, rootUniformity = 0f, pointsPerGuide = 2 });
            Assert.That(result.averagePaintedDensity, Is.EqualTo(1f / 12f).Within(1e-6f));
            Assert.That(result.guides.Count, Is.EqualTo(10), "The map product is nonzero inside despite zero at every vertex.");
            foreach (HairGuide guide in result.guides)
                Assert.That(guide.root.Barycentric.x * guide.root.Barycentric.y, Is.GreaterThan(0f));
        }

        [TestCase(1)] [TestCase(4)]
        public void GrowthDensityIntegralIsIndependentOfSurfaceTessellation(int divisions)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var triangles = new List<int>();
            var growth = new List<float>(); var multiplier = new List<float>();
            for (int z = 0; z <= divisions; z++) for (int x = 0; x <= divisions; x++)
            {
                float t = x / (float)divisions;
                vertices.Add(new Vector3(t, 0f, z / (float)divisions)); normals.Add(Vector3.up);
                growth.Add(t); multiplier.Add(1f - t);
            }
            for (int z = 0; z < divisions; z++) for (int x = 0; x < divisions; x++)
            {
                int a = z * (divisions + 1) + x, b = a + 1, c = a + divisions + 1, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            sourceMesh.Clear(); sourceMesh.SetVertices(vertices); sourceMesh.SetNormals(normals); sourceMesh.SetTriangles(triangles, 0);
            groom.SetSource(sourceMesh, "mesh:test", "TestRace", "TestScalp");
            HairGroup group = groom.Groups[0]; group.FindMap(HairMapKind.GrowthArea).values = growth.ToArray();
            group.FindMap(HairMapKind.Density).values = multiplier.ToArray();
            HairGuideGenerationResult result = HairGuideGenerator.Generate(groom, group,
                new HairGuideGenerationSettings { guideCount = 600, minimumRootSpacing = 0f, rootUniformity = 0f, pointsPerGuide = 2 });
            Assert.That(result.averagePaintedDensity, Is.EqualTo(1f / 6f).Within(1e-6f));
            Assert.That(result.guides.Count, Is.EqualTo(100));
        }

        [Test]
        public void GrowthDensitySeedAndUniformPaintProduceStablePositions()
        {
            HairGuideGenerationResult full = GenerateDensityTest(1f);
            HairGuideGenerationResult half = GenerateDensityTest(0.5f);
            HairGuideGenerationResult repeat = GenerateDensityTest(0.5f);
            for (int i = 0; i < half.guides.Count; i++)
            {
                Assert.That(Vector3.Distance(full.guides[i].root.CachedLocalPosition, half.guides[i].root.CachedLocalPosition), Is.LessThan(1e-6f));
                Assert.That(repeat.guides[i].root.CachedLocalPosition, Is.EqualTo(half.guides[i].root.CachedLocalPosition));
            }
        }

        [Test]
        public void GrowthDensityInvalidValuesCannotGenerateInvalidGeometry()
        {
            Assert.That(GenerateDensityTest(float.NaN).guides, Is.Empty);
            Assert.That(GenerateDensityTest(1f, float.PositiveInfinity).guides, Is.Empty);
            Assert.That(GenerateDensityTest(-1f).guides, Is.Empty);
            Assert.That(GenerateDensityTest(2f).guides.Count, Is.EqualTo(100), "Primary density is bounded at 1.");
        }

        [Test]
        public void GrowthDensityPreviewInvalidatesAfterPaintAndNeverReplacesGuidesImplicitly()
        {
            GenerateDensityTest(0.5f);
            HairCardStage stage = CreateEditingStage();
            FieldInfo activeStage = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = activeStage.GetValue(null);
            try
            {
                activeStage.SetValue(null, stage);
                stage.GuideGeneration.guideCount = 20; stage.GuideGeneration.minimumRootSpacing = 0f;
                stage.GenerateGuidePreview();
                Assert.That(stage.GenerationPreview.guides.Count, Is.EqualTo(10));
                Assert.That(groom.Groups[0].guides.Count, Is.EqualTo(1));
                HairGroomCommands.FillMap(groom, groom.Groups[0].FindMap(HairMapKind.GrowthArea), 1f);
                Assert.That(stage.GenerationPreview, Is.Null, "Do not allow accepting a stale density result after map operations.");
                Assert.That(groom.Groups[0].guides.Count, Is.EqualTo(1));
                stage.GenerateGuidePreview();
                Assert.That(stage.GenerationPreview.guides.Count, Is.EqualTo(20));
                stage.AcceptGuidePreview();
                Assert.That(groom.Groups[0].guides.Count, Is.EqualTo(21));
            }
            finally { activeStage.SetValue(null, previous); DestroyEditingStage(stage); }
        }

        [Test]
        public void GrowthDensityAdvancedDefaultsResetAndNeutralMultiplierDetection()
        {
            HairGroomNodeWindow first = ScriptableObject.CreateInstance<HairGroomNodeWindow>(), second = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            FieldInfo advanced = typeof(HairGroomNodeWindow).GetField("expandedOptionalMapKeys", BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                Assert.That((System.Collections.ICollection)advanced.GetValue(first), Is.Empty);
                ((System.Collections.Generic.List<string>)advanced.GetValue(first)).Add(HairGroomNodes.Key(HairGroomNodeKind.OptionalMaps, groom.Groups[0].Id));
                HairPreferenceCodec.Restore(second, HairPreferenceCodec.Capture(first));
                CollectionAssert.AreEqual((System.Collections.ICollection)advanced.GetValue(first), (System.Collections.ICollection)advanced.GetValue(second));
                HairPreferenceCodec.Reset(second); Assert.That((System.Collections.ICollection)advanced.GetValue(second), Is.Empty);
                HairGroup group = groom.Groups[0];
                Assert.That(group.FindMap(HairMapKind.GrowthArea).DisplayName, Is.EqualTo("Growth / Density"));
                Assert.That(group.FindMap(HairMapKind.Density).DisplayName, Does.Contain("optional"));
                Assert.That(HairGroomWorkspace.HasPaintedDensityMultiplier(group), Is.False);
                group.FindMap(HairMapKind.Density).values[0] = 0.5f;
                Assert.That(HairGroomWorkspace.HasPaintedDensityMultiplier(group), Is.True);
                Undo.ClearUndo(groom); Undo.IncrementCurrentGroup();
                HairGroomCommands.FillMap(groom, group.FindMap(HairMapKind.Density), 1f); Undo.FlushUndoRecordObjects();
                Assert.That(HairGroomWorkspace.HasPaintedDensityMultiplier(group), Is.False);
                Undo.PerformUndo(); Assert.That(HairGroomWorkspace.HasPaintedDensityMultiplier(groom.Groups[0]), Is.True);
            }
            finally { Undo.ClearUndo(groom); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
    }
}
#endif
