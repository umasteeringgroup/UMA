#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintGpuGoldenTests
    {
        private static readonly Color Base = new Color(0.18f, 0.42f, 0.67f, 0.31f);
        private static readonly Color Paint = new Color(0.82f, 0.27f, 0.11f, 0.63f);
        private const float Radius = 0.3f;
        private const float Hardness = 0.4f;
        private const float Flow = 0.75f;
        private const float Strength = 0.8f;

        [Test]
        public void LayerMaskStrokeIsGrayscaleAndParticipatesInUndoRedo()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal);
            try
            {
                TexturePaintLayer layer = fixture.set.AddLayer("Masked Paint");
                TexturePaintLayerMask mask = fixture.set.AddLayerMask(layer, 0f);
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white,
                    strength: 1f);
                context.editLayerMask = true;
                context.maskValue = 1f;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), 0.2f), Is.True);
                engine.EndStroke();

                Color[] pixels = TexturePaintGpuTestFixture.ReadPixels(mask.target.Front);
                Color center = pixels[32 * TexturePaintGpuTestFixture.Size + 32];
                Assert.That(center.r, Is.GreaterThan(0.98f));
                Assert.That(center.g, Is.EqualTo(center.r).Within(0.001f));
                Assert.That(center.b, Is.EqualTo(center.r).Within(0.001f));
                Assert.That(center.a, Is.EqualTo(1f).Within(0.001f));
                Assert.That(engine.Undo(), Is.True);
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(mask.target.Front)
                    [32 * TexturePaintGpuTestFixture.Size + 32].r, Is.LessThan(0.01f));
                Assert.That(engine.Redo(), Is.True);
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(mask.target.Front)
                    [32 * TexturePaintGpuTestFixture.Size + 32].r, Is.GreaterThan(0.98f));
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void PaintingEngineDefensivelyConvertsTextureInputToGrayscaleForMaskStroke()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal);
            Texture2D source = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                source.SetPixel(0, 0, new Color(0.2f, 0.6f, 0.1f, 1f));
                source.Apply(false, false);
                TexturePaintLayer layer = fixture.set.AddLayer("Texture Mask");
                TexturePaintLayerMask mask = fixture.set.AddLayerMask(layer, 0f);
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white,
                    strength: 1f);
                context.editLayerMask = true;
                // The editor never exposes texture sources while painting a layer mask. Keep the
                // engine defensive if malformed or legacy callers still provide one directly.
                context.paintSource = TexturePaintBrushSource.Texture;
                context.sourceTexture = source;
                context.maskSourceChannel = TexturePaintChannel.Albedo;

                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), 0.2f), Is.True);
                engine.EndStroke();

                Color center = TexturePaintGpuTestFixture.ReadPixels(mask.target.Front)
                    [32 * TexturePaintGpuTestFixture.Size + 32];
                float expected = 0.2f * 0.2126f + 0.6f * 0.7152f + 0.1f * 0.0722f;
                Assert.That(center.r, Is.EqualTo(expected).Within(0.015f));
                Assert.That(center.g, Is.EqualTo(center.r).Within(0.001f));
                Assert.That(center.b, Is.EqualTo(center.r).Within(0.001f));
                Assert.That(center.a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(brush);
            }
        }

        [TestCase(TexturePaintBlendMode.Normal)]
        [TestCase(TexturePaintBlendMode.Multiply)]
        [TestCase(TexturePaintBlendMode.Add)]
        [TestCase(TexturePaintBlendMode.Subtract)]
        [TestCase(TexturePaintBlendMode.Screen)]
        [TestCase(TexturePaintBlendMode.Overlay)]
        public void PaintBlendModeMatchesReferenceImage(TexturePaintBlendMode blendMode)
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Base);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(Hardness, Flow, blendMode);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Paint, strength: Strength);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), Radius), Is.True);
                engine.EndStroke();
                Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0), "Golden test used the CPU fallback.");

                Color[] expected = ReferencePaint(Base, Paint, blendMode);
                TexturePaintGpuTestFixture.AssertImage("paint-" + blendMode, expected, fixture.ReadPixels());
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void SpriteStampUsesOnlyTheAssignedSpriteRegion()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal,
                BrushPreset.Shape.Stamp);
            Texture2D sheet = new Texture2D(8, 4, TextureFormat.RGBA32, false, true);
            Sprite sprite = null;
            try
            {
                Color[] pixels = new Color[32];
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 8; x++)
                    pixels[y * 8 + x] = x < 2 ? Color.white : Color.clear;
                sheet.SetPixels(pixels);
                sheet.Apply(false, false);
                sprite = Sprite.Create(sheet, new Rect(0f, 0f, 2f, 4f), new Vector2(0.5f, 0.5f));
                brush.stampSprite = sprite;

                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), 0.2f), Is.True);
                engine.EndStroke();

                Color center = fixture.ReadPixels()[32 * TexturePaintGpuTestFixture.Size + 32];
                Assert.That(center.g, Is.GreaterThan(0.98f),
                    "The atlas center is transparent; an opaque center proves the Sprite region was extracted.");
            }
            finally
            {
                TexturePaintSpriteSource.ClearCache();
                if (sprite != null) Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(brush);
            }
        }

        [Test]
        public void AnisotropicFootprintScaleExpandsItsGpuDirtyBounds()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                StrokeSample sample = TexturePaintGpuTestFixture.CenterSample();
                sample.footprintScale = new Vector2(1f, 1.3f);
                Assert.That(engine.ApplySample(sample, 0.2f), Is.True);
                engine.EndStroke();

                Color beyondUnscaledBounds = fixture.ReadPixels()[46 * TexturePaintGpuTestFixture.Size + 32];
                Assert.That(beyondUnscaledBounds.g, Is.GreaterThan(0.9f),
                    "A grown stamp must not be clipped to the original unscaled dirty rectangle.");
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void PartialAlphaPaintIsUniformAcrossSharedTriangleEdge()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 0.25f, TexturePaintBlendMode.Normal);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green, strength: 1f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                BrushProjection firstProjection = fixture.set.surface.CalculateBrushProjection(0, 10f,
                    Vector3.right, Vector3.up, true);
                BrushProjection secondProjection = fixture.set.surface.CalculateBrushProjection(1, 10f,
                    Vector3.right, Vector3.up, true);
                StrokeSample first = TexturePaintGpuTestFixture.CenterSample();
                first.triangleIndex = 0;
                StrokeSample second = TexturePaintGpuTestFixture.CenterSample();
                second.triangleIndex = 1;

                Assert.That(engine.ApplySample(first, 10f, firstProjection), Is.True);
                Assert.That(engine.ApplySample(second, 10f, secondProjection), Is.True);
                engine.EndStroke();

                Color[] pixels = fixture.ReadPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Assert.That(pixels[i].g, Is.EqualTo(1f).Within(0.003f));
                    Assert.That(pixels[i].a, Is.EqualTo(0.25f).Within(0.003f),
                        $"Pixel {i} received a different number of triangle contributions.");
                }
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void DirectUVPaintingOnDenseSmallGeometryProducesOneBoundedFootprint()
        {
            const int grid = 33;
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear);
            Vector3[] vertices = new Vector3[grid * grid];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(grid - 1) * (grid - 1) * 6];
            for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
            {
                int vertex = y * grid + x;
                uv[vertex] = new Vector2(x / (float)(grid - 1), y / (float)(grid - 1));
                // Deliberately tiny world geometry. Direct 2D paint must never derive one
                // projected stamp per polygon from these dimensions.
                vertices[vertex] = new Vector3(x * 0.00001f, y * 0.00001f, 0f);
                normals[vertex] = Vector3.forward;
            }
            int write = 0;
            for (int y = 0; y < grid - 1; y++)
            for (int x = 0; x < grid - 1; x++)
            {
                int a = y * grid + x, b = a + 1, c = a + grid, d = c + 1;
                triangles[write++] = a; triangles[write++] = b; triangles[write++] = d;
                triangles[write++] = a; triangles[write++] = d; triangles[write++] = c;
            }
            fixture.mesh.Clear();
            fixture.mesh.vertices = vertices;
            fixture.mesh.normals = normals;
            fixture.mesh.uv = uv;
            fixture.mesh.triangles = triangles;
            fixture.mesh.RecalculateBounds();
            int triangleCount = triangles.Length / 3;
            fixture.set.surface.triangleSlotNames = new string[triangleCount];
            fixture.set.surface.triangleIslands = new int[triangleCount];
            for (int i = 0; i < triangleCount; i++)
                fixture.set.surface.triangleSlotNames[i] = "Body";

            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f, TexturePaintBlendMode.Normal);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    Color.green, strength: 1f);
                context.directUV = true;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                StrokeSample sample = TexturePaintStageWindow.CreateDirectUVSample(fixture.set,
                    new Vector2(0.5f, 0.5f));
                Assert.That(sample.triangleIndex, Is.EqualTo(-1),
                    "The 2D brush must not acquire a triangle projection.");
                Assert.That(engine.ApplySample(sample, 0.08f), Is.True);
                engine.EndStroke();

                Color[] pixels = fixture.ReadPixels();
                Assert.That(pixels[32 * TexturePaintGpuTestFixture.Size + 32].g,
                    Is.GreaterThan(0.98f));
                Assert.That(pixels[32 * TexturePaintGpuTestFixture.Size + 50].a,
                    Is.LessThan(0.01f),
                    "Small polygons must not fan the 2D stamp into a geometry explosion.");
                Assert.That(engine.Performance.geometryMaskBuilds, Is.Zero,
                    "An ordinary 2D brush stroke must not consult mesh coverage.");
                Assert.That(fixture.set.baseStrokes, Has.Count.EqualTo(1));
                Assert.That(fixture.set.baseStrokes[0].directUV, Is.True,
                    "Persisted stroke metadata must retain its texture-space coordinate mode.");
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void PartialAlphaPaintIsUniformAcrossCoverageTileBoundaries()
        {
            const int size = 256;
            const float flow = 0.2f;
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.clear, size: size);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, flow, TexturePaintBlendMode.Normal);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.green, strength: 1f);
                context.limitStrokeCoverage = true;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                StrokeSample sample = TexturePaintGpuTestFixture.CenterSample();
                sample.uv = new Vector2(0.47f, 0.47f);

                Assert.That(engine.ApplySample(sample, 0.16f), Is.True);
                engine.EndStroke();
                Assert.That(engine.Performance.computeDispatches, Is.EqualTo(4),
                    "The regression must exercise all four coverage tiles meeting at (128, 128).");

                Color[] pixels = fixture.ReadPixels();
                for (int y = 116; y <= 140; y++)
                for (int x = 116; x <= 140; x++)
                {
                    Color pixel = pixels[y * size + x];
                    Assert.That(pixel.g, Is.EqualTo(1f).Within(0.003f), $"Green changed at ({x}, {y}).");
                    Assert.That(pixel.a, Is.EqualTo(flow).Within(0.003f),
                        $"Partial-alpha coverage changed at coverage-tile boundary ({x}, {y}).");
                }
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void RewindingProvisionalFirstStampLeavesOnlyCorrectedStamp()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Base);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(Hardness, Flow, TexturePaintBlendMode.Normal);
            Color correctedColor = new Color(0.16f, 0.88f, 0.34f, 0.63f);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Paint, strength: Strength);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                StrokeSample provisional = TexturePaintGpuTestFixture.CenterSample();
                provisional.color = Paint;
                provisional.hasColor = true;
                Assert.That(engine.ApplySample(provisional, Radius), Is.True);

                Assert.That(engine.RewindActiveStroke(), Is.True);
                StrokeSample corrected = TexturePaintGpuTestFixture.CenterSample();
                corrected.color = correctedColor;
                corrected.hasColor = true;
                Assert.That(engine.ApplySample(corrected, Radius), Is.True);
                engine.EndStroke();

                TexturePaintGpuTestFixture.AssertImage("follow-stroke-first-restamp",
                    ReferencePaint(Base, correctedColor, TexturePaintBlendMode.Normal), fixture.ReadPixels());
                Assert.That(engine.Undo(), Is.True);
                Color[] restored = fixture.ReadPixels();
                for (int i = 0; i < restored.Length; i++)
                    Assert.That(ColorDistance(restored[i], Base), Is.LessThan(0.002f));
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [TestCase(TexturePaintTool.Erase)]
        [TestCase(TexturePaintTool.Dodge)]
        [TestCase(TexturePaintTool.Burn)]
        public void TonalAndEraseToolsMatchReferenceImage(TexturePaintTool tool)
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Base);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(Hardness, Flow);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, tool, Paint, strength: Strength);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), Radius), Is.True);
                engine.EndStroke();
                Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0));

                Color desired = tool == TexturePaintTool.Burn ? Color.black : Color.white;
                Color[] expected = ReferenceTool(Base, desired);
                TexturePaintGpuTestFixture.AssertImage("tool-" + tool, expected, fixture.ReadPixels());
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void BlurMatchesGaussianReferenceImage()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            Color[] source = Checkerboard();
            fixture.SetPixels(source);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(Hardness, Flow);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Blur, Color.white, strength: Strength);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), Radius), Is.True);
                engine.EndStroke();
                Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0));

                TexturePaintGpuTestFixture.AssertImage("tool-blur", ReferenceBlur(source), fixture.ReadPixels(),
                    maximumTolerance: 0.006f, meanTolerance: 0.001f);
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [TestCase(TexturePaintTool.Smear, 0.40f)]
        [TestCase(TexturePaintTool.Clone, 0.25f)]
        public void TransferToolsMatchReferenceImage(TexturePaintTool tool, float sourceU)
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            Color[] source = Gradient();
            fixture.SetPixels(source);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(Hardness, Flow);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, tool, Color.white, strength: Strength);
                context.cloneSourceUV = new Vector2(sourceU, 0.5f);
                StrokeSample sample = TexturePaintGpuTestFixture.CenterSample(new Vector2(sourceU, 0.5f));
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(engine.ApplySample(sample, Radius), Is.True);
                engine.EndStroke();
                Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0));

                Color[] expected = ReferenceTransfer(source, sourceU);
                TexturePaintGpuTestFixture.AssertImage("tool-" + tool, expected, fixture.ReadPixels(),
                    maximumTolerance: 0.008f, meanTolerance: 0.0015f);
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void NormalTouchupConvergesTowardSurfaceNormalAndPreservesUnitLength()
        {
            Color encodedTilt = new Color(0.8535534f, 0.5f, 0.8535534f, 1f);
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(encodedTilt, TexturePaintChannel.Normal);
            fixture.set.tangentSpaceMaps = TangentSpaceMapBuilder.Build(fixture.mesh,
                TexturePaintGpuTestFixture.Size, TexturePaintGpuTestFixture.Size);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.NormalTouchup, Color.white,
                    TexturePaintChannel.Normal, 1f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), Radius), Is.True);
                engine.EndStroke();
                Color center = fixture.ReadPixels()[32 * TexturePaintGpuTestFixture.Size + 32];
                Vector3 normal = new Vector3(center.r * 2f - 1f, center.g * 2f - 1f, center.b * 2f - 1f);
                Assert.That(normal.magnitude, Is.EqualTo(1f).Within(0.004f));
                Assert.That(center.r, Is.EqualTo(0.5f).Within(0.006f));
                Assert.That(center.g, Is.EqualTo(0.5f).Within(0.006f));
                Assert.That(center.b, Is.EqualTo(1f).Within(0.006f));
                Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void OneStrokePaintsEverySelectedSlotBackedBySeparateTextureSets()
        {
            using TexturePaintGpuTestFixture torso = new TexturePaintGpuTestFixture(Base);
            using TexturePaintGpuTestFixture legs = new TexturePaintGpuTestFixture(Base);
            legs.set.persistentId = "golden-legs";
            legs.set.surface.index = 1;
            legs.set.surface.slotName = "Legs";
            legs.set.surface.slotNames[0] = "Legs";
            legs.set.surface.triangleSlotNames = new[] { "Legs", "Legs" };
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = torso.CreateBrush(Hardness, Flow);
            try
            {
                StrokeContext context = torso.CreateContext(brush, TexturePaintTool.Paint, Paint, strength: Strength);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture,
                    new List<TextureSet> { torso.set, legs.set }), Is.True);
                StrokeSample torsoSample = TexturePaintGpuTestFixture.CenterSample();
                StrokeSample legSample = TexturePaintGpuTestFixture.CenterSample();
                legSample.surfaceIndex = 1;
                legSample.slotName = "Legs";
                Assert.That(engine.ApplySample(torsoSample, Radius), Is.True);
                Assert.That(engine.ApplySample(legSample, Radius), Is.True);
                engine.EndStroke();

                Color torsoCenter = torso.ReadPixels()[32 * TexturePaintGpuTestFixture.Size + 32];
                Color legCenter = legs.ReadPixels()[32 * TexturePaintGpuTestFixture.Size + 32];
                Assert.That(ColorDistance(torsoCenter, legCenter), Is.LessThan(0.002f));
                Assert.That(ColorDistance(torsoCenter, Base), Is.GreaterThan(0.1f));
                Assert.That(engine.Performance.computeDispatches, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void ContinuousPathBatchUsesBoundedDispatchesAndDirtyPixels()
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Base);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(0.8f, 0.6f);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint, Paint, strength: 0.7f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                List<StrokeDispatchSample> samples = new List<StrokeDispatchSample>();
                for (int i = 0; i < 64; i++)
                {
                    float u = Mathf.Lerp(0.08f, 0.92f, i / 63f);
                    StrokeSample sample = TexturePaintGpuTestFixture.CenterSample();
                    sample.uv = new Vector2(u, 0.5f);
                    sample.worldPosition = new Vector3(u, 0.5f);
                    samples.Add(new StrokeDispatchSample(sample, 0.035f, default));
                }
                Assert.That(engine.ApplySamples(samples), Is.True);
                engine.EndStroke();
                Assert.That(engine.Performance.computeDispatches, Is.LessThanOrEqualTo(2));
                Assert.That(engine.Performance.copiedPixels,
                    Is.LessThan(TexturePaintGpuTestFixture.Size * TexturePaintGpuTestFixture.Size / 2));
                Assert.That(engine.Performance.PreviewP95Milliseconds, Is.LessThan(1000d));
            }
            finally { Object.DestroyImmediate(brush); }
        }

        private static Color[] ReferencePaint(Color destination, Color source, TexturePaintBlendMode mode)
        {
            Color[] output = new Color[TexturePaintGpuTestFixture.Size * TexturePaintGpuTestFixture.Size];
            for (int y = 0; y < TexturePaintGpuTestFixture.Size; y++)
            for (int x = 0; x < TexturePaintGpuTestFixture.Size; x++)
            {
                float weight = Coverage(x, y) * Flow * source.a;
                Color blended = Blend(destination, source, mode);
                output[y * TexturePaintGpuTestFixture.Size + x] =
                    PaintingEngine.CompositeStraightAlpha(destination, blended, weight);
            }
            return output;
        }

        private static Color[] ReferenceTool(Color destination, Color desired)
        {
            Color[] output = new Color[TexturePaintGpuTestFixture.Size * TexturePaintGpuTestFixture.Size];
            for (int y = 0; y < TexturePaintGpuTestFixture.Size; y++)
            for (int x = 0; x < TexturePaintGpuTestFixture.Size; x++)
            {
                float weight = Coverage(x, y) * Flow;
                Color result = Color.Lerp(destination, desired, weight);
                result.a = weight + destination.a * (1f - weight);
                output[y * TexturePaintGpuTestFixture.Size + x] = result;
            }
            return output;
        }

        private static Color[] ReferenceBlur(Color[] source)
        {
            int size = TexturePaintGpuTestFixture.Size;
            Color[] output = new Color[source.Length];
            int[] kernel = { 1, 2, 1 };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color sum = Color.clear;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                    sum += source[Mathf.Clamp(y + oy, 0, size - 1) * size + Mathf.Clamp(x + ox, 0, size - 1)] *
                        (kernel[ox + 1] * kernel[oy + 1]);
                output[y * size + x] = Color.Lerp(source[y * size + x], sum / 16f, Coverage(x, y) * Flow);
            }
            return output;
        }

        private static Color[] ReferenceTransfer(Color[] source, float sourceU)
        {
            int size = TexturePaintGpuTestFixture.Size;
            Color[] output = new Color[source.Length];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                Vector2 sampleUV = new Vector2(sourceU + uv.x - 0.5f, uv.y);
                Color desired = Bilinear(source, sampleUV);
                float weight = Coverage(x, y) * Flow;
                Color result = Color.Lerp(source[y * size + x], desired, weight);
                result.a = weight + source[y * size + x].a * (1f - weight);
                output[y * size + x] = result;
            }
            return output;
        }

        private static Color Bilinear(Color[] source, Vector2 uv)
        {
            int size = TexturePaintGpuTestFixture.Size;
            float fx = Mathf.Clamp01(uv.x) * size - 0.5f;
            float fy = Mathf.Clamp01(uv.y) * size - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, size - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, size - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, size - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, size - 1);
            float tx = Mathf.Clamp01(fx - Mathf.Floor(fx));
            float ty = Mathf.Clamp01(fy - Mathf.Floor(fy));
            return Color.Lerp(Color.Lerp(source[y0 * size + x0], source[y0 * size + x1], tx),
                Color.Lerp(source[y1 * size + x0], source[y1 * size + x1], tx), ty);
        }

        private static float Coverage(int x, int y)
        {
            Vector2 uv = new Vector2((x + 0.5f) / TexturePaintGpuTestFixture.Size,
                (y + 0.5f) / TexturePaintGpuTestFixture.Size);
            float distance = Vector2.Distance(uv, Vector2.one * 0.5f) / Radius;
            if (distance >= 1f) return 0f;
            float falloff = distance <= Hardness ? 1f : 1f - (distance - Hardness) / (1f - Hardness);
            return Mathf.Clamp01(falloff * Strength);
        }

        private static Color Blend(Color destination, Color source, TexturePaintBlendMode mode)
        {
            Color result = source;
            switch (mode)
            {
                case TexturePaintBlendMode.Multiply:
                    result = destination * source;
                    break;
                case TexturePaintBlendMode.Add:
                    result = destination + source;
                    break;
                case TexturePaintBlendMode.Subtract:
                    result = destination - source;
                    break;
                case TexturePaintBlendMode.Screen:
                    result = Color.white - (Color.white - destination) * (Color.white - source);
                    break;
                case TexturePaintBlendMode.Overlay:
                    result = new Color(Overlay(destination.r, source.r), Overlay(destination.g, source.g),
                        Overlay(destination.b, source.b), source.a);
                    break;
            }
            result.a = 1f;
            return result;
        }

        private static float Overlay(float destination, float source) => destination < 0.5f
            ? 2f * destination * source
            : 1f - 2f * (1f - destination) * (1f - source);

        private static float ColorDistance(Color a, Color b)
            => Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)),
                Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));

        private static Color[] Checkerboard()
        {
            int size = TexturePaintGpuTestFixture.Size;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = ((x / 3 + y / 3) & 1) == 0
                    ? new Color(0.1f, 0.3f, 0.9f, 0.2f)
                    : new Color(0.9f, 0.7f, 0.1f, 0.8f);
            return pixels;
        }

        private static Color[] Gradient()
        {
            int size = TexturePaintGpuTestFixture.Size;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                pixels[y * size + x] = new Color(u, v, u * v, 0.35f + 0.5f * u);
            }
            return pixels;
        }
    }
}
#endif
