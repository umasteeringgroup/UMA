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

                Color[] expected = ReferencePaint(Base, Paint.linear, blendMode);
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

        [TestCase(0, false, 30f)] [TestCase(1, false, 30f)] [TestCase(2, false, 30f)]
        [TestCase(0, true, 30f)] [TestCase(1, true, 30f)] [TestCase(2, true, 30f)]
        [TestCase(0, false, 0f)] [TestCase(1, false, 0f)] [TestCase(2, false, 0f)]
        public void SurfaceAngleCoverageMatchesAcrossCpuSingleAndBatchedStamps(
            int backend, bool backfaces, float fade)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            fixture.mesh.normals = new[] { Vector3.forward, Vector3.back, Vector3.back, Vector3.forward };
            using var engine = backend == 0 ? new PaintingEngine(null, null, null) : TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1f);
                context.limitStrokeCoverage = true;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                var samples = new List<StrokeDispatchSample>();
                for (int triangle = 0; triangle < 2; triangle++)
                {
                    var sample = TexturePaintGpuTestFixture.CenterSample();
                    sample.triangleIndex = triangle;
                    var projection = fixture.set.surface.CalculateBrushProjection(triangle, 10f,
                        Vector3.right, Vector3.up, true, Vector3.forward, 90f, backfaces, fade);
                    samples.Add(new StrokeDispatchSample(sample, 10f, projection));
                }
                if (backend == 2) Assert.That(engine.ApplySamples(samples), Is.True);
                else foreach (var dispatch in samples)
                    Assert.That(engine.ApplySample(dispatch.sample, dispatch.uvRadius, dispatch.projection), Is.True);
                engine.EndStroke();
                Color[] pixels = fixture.ReadPixels();
                for (int y = 0; y < TexturePaintGpuTestFixture.Size; y++)
                for (int x = 0; x < TexturePaintGpuTestFixture.Size; x++)
                {
                    float facing = 1f - 2f * (x + .5f) / TexturePaintGpuTestFixture.Size;
                    if (backfaces) facing = Mathf.Abs(facing);
                    float t = Mathf.Clamp01(facing / .5f);
                    float expected = fade == 0f ? (facing > 0f ? 1f : 0f) : t * t * (3f - 2f * t);
                    Assert.That(pixels[y * TexturePaintGpuTestFixture.Size + x].a,
                        Is.EqualTo(expected).Within(.003f), $"Angle coverage at ({x}, {y}), backend {backend}");
                }
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void ProjectionDepthFadesPerPixelAcrossCpuSingleAndBatchedStamps(int backend)
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            fixture.mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 1.2f),
                new Vector3(1, 1, 1.2f), new Vector3(0, 1, 0) };
            fixture.mesh.RecalculateBounds();
            using var engine = backend == 0 ? new PaintingEngine(null, null, null) : TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1f);
                context.limitStrokeCoverage = true;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                var samples = new List<StrokeDispatchSample>();
                for (int triangle = 0; triangle < 2; triangle++)
                {
                    var sample = TexturePaintGpuTestFixture.CenterSample();
                    sample.triangleIndex = triangle;
                    var projection = fixture.set.surface.CalculateBrushProjection(triangle, 10f,
                        Vector3.right, Vector3.up, true, Vector3.forward, 90f, false, 30f, Vector3.zero, 1f);
                    samples.Add(new StrokeDispatchSample(sample, 10f, projection));
                }
                if (backend == 2) Assert.That(engine.ApplySamples(samples), Is.True);
                else foreach (var dispatch in samples)
                    Assert.That(engine.ApplySample(dispatch.sample, dispatch.uvRadius, dispatch.projection), Is.True);
                engine.EndStroke();
                Color[] pixels = fixture.ReadPixels();
                for (int y = 0; y < TexturePaintGpuTestFixture.Size; y++)
                for (int x = 0; x < TexturePaintGpuTestFixture.Size; x++)
                {
                    float depth = 1.2f * (x + .5f) / TexturePaintGpuTestFixture.Size;
                    float expected = 1f - Mathf.SmoothStep(0f, 1f, (depth - .8f) / .2f);
                    Assert.That(pixels[y * TexturePaintGpuTestFixture.Size + x].a,
                        Is.EqualTo(expected).Within(.003f), $"Depth coverage at ({x}, {y}), backend {backend}");
                }
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void TriangleRestrictedBatchPreservesSharedEdgeOwnership()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture =
                new TexturePaintGpuTestFixture(Color.clear);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 0.25f,
                TexturePaintBlendMode.Normal);
            try
            {
                StrokeContext context = fixture.CreateContext(brush,
                    TexturePaintTool.Paint, Color.green, strength: 1f);
                Assert.That(engine.BeginStroke(context,
                    TexturePaintSourceMode.SourceTexture), Is.True);
                BrushProjection firstProjection = fixture.set.surface
                    .CalculateBrushProjection(0, 10f, Vector3.right, Vector3.up, true);
                BrushProjection secondProjection = fixture.set.surface
                    .CalculateBrushProjection(1, 10f, Vector3.right, Vector3.up, true);
                StrokeSample first = TexturePaintGpuTestFixture.CenterSample();
                first.triangleIndex = 0;
                StrokeSample second = TexturePaintGpuTestFixture.CenterSample();
                second.triangleIndex = 1;
                var samples = new List<StrokeDispatchSample>
                {
                    new StrokeDispatchSample(first, 10f, firstProjection),
                    new StrokeDispatchSample(second, 10f, secondProjection)
                };

                Assert.That(engine.ApplySamples(samples), Is.True);
                engine.EndStroke();

                Assert.That(engine.Performance.computeDispatches, Is.EqualTo(1),
                    "Both owned triangles fit in one destination tile and should share a batch.");
                Color[] pixels = fixture.ReadPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Assert.That(pixels[i].g, Is.EqualTo(1f).Within(0.003f));
                    Assert.That(pixels[i].a, Is.EqualTo(0.25f).Within(0.003f),
                        $"Pixel {i} received a different number of batched triangle contributions.");
                }
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void InputEventBatchCoalescesHistoryRasterAndNotifications()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture =
                new TexturePaintGpuTestFixture(Color.clear);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 0.5f,
                TexturePaintBlendMode.Normal);
            int notifications = 0;
            engine.TextureChanged += (_, __) => notifications++;
            try
            {
                StrokeContext context = fixture.CreateContext(brush,
                    TexturePaintTool.Paint, Color.green, strength: 1f);
                Assert.That(engine.BeginStroke(context,
                    TexturePaintSourceMode.SourceTexture), Is.True);
                engine.BeginSampleBatch();
                for (int i = 0; i < 20; i++)
                {
                    StrokeSample sample = TexturePaintGpuTestFixture.CenterSample();
                    sample.uv.x = Mathf.Lerp(0.42f, 0.58f, i / 19f);
                    Assert.That(engine.ApplySample(sample, 0.08f), Is.True);
                }
                Assert.That(notifications, Is.Zero,
                    "Interactive refresh must wait until the input event is complete.");
                Assert.That(engine.EndSampleBatch(), Is.True);

                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(engine.History.PendingTileCount, Is.EqualTo(1));
                Assert.That(engine.Performance.computeDispatches, Is.EqualTo(1));
                engine.EndStroke();
                Assert.That(engine.History.UndoTileCount, Is.EqualTo(1));
                Assert.That(engine.Undo(), Is.True);
                Assert.That(fixture.ReadPixels()[32 * TexturePaintGpuTestFixture.Size + 32].a,
                    Is.LessThan(0.001f));
                Assert.That(engine.Redo(), Is.True);
                Assert.That(fixture.ReadPixels()[32 * TexturePaintGpuTestFixture.Size + 32].a,
                    Is.GreaterThan(0.1f));
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

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void SoftBrushCoverageMatchesAcrossCpuSingleAndBatchedTiles(int backend)
        {
            const int size = 256;
            const float radius = .22f;
            const float hardness = .75f;
            using var fixture = new TexturePaintGpuTestFixture(Color.clear, size: size);
            using var engine = backend == 0 ? new PaintingEngine(null, null, null) : TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(hardness, 1f);
            var centers = new[] { new Vector2(.35f, .4f), new Vector2(.4f, .42f), new Vector2(.42f, .45f) };
            try
            {
                var context = fixture.CreateContext(brush, TexturePaintTool.Paint, Color.white, strength: 1f);
                context.limitStrokeCoverage = true;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                foreach (Vector2 center in centers)
                {
                    var samples = new List<StrokeDispatchSample>();
                    for (int triangle = 0; triangle < 2; triangle++)
                    {
                        var sample = TexturePaintGpuTestFixture.CenterSample();
                        sample.uv = center;
                        sample.triangleIndex = triangle;
                        var projection = fixture.set.surface.CalculateBrushProjection(triangle, radius,
                            Vector3.right, Vector3.up, true);
                        samples.Add(new StrokeDispatchSample(sample, radius, projection));
                    }
                    if (backend == 2) Assert.That(engine.ApplySamples(samples), Is.True);
                    else foreach (var dispatch in samples)
                        Assert.That(engine.ApplySample(dispatch.sample, dispatch.uvRadius, dispatch.projection), Is.True);
                }
                Color[] live = fixture.ReadPixels();
                engine.EndStroke();
                Color[] released = fixture.ReadPixels();
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + .5f) / size, (y + .5f) / size);
                    float expected = 0f;
                    foreach (Vector2 center in centers)
                        expected = Mathf.Max(expected, Mathf.Clamp01((1f - Vector2.Distance(uv, center) / radius) / (1f - hardness)));
                    Assert.That(live[y * size + x].a, Is.EqualTo(expected).Within(.004f),
                        $"Live soft edge at ({x}, {y}), backend {backend}");
                    Assert.That(released[y * size + x].a, Is.EqualTo(expected).Within(.004f),
                        $"Released soft edge at ({x}, {y}), backend {backend}");
                }
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [TestCase(TexturePaintChannel.Normal)]
        [TestCase(TexturePaintChannel.NormalControl)]
        [TestCase(TexturePaintChannel.Albedo)]
        [TestCase(TexturePaintChannel.Metallic)]
        [TestCase(TexturePaintChannel.Roughness)]
        [TestCase(TexturePaintChannel.AmbientOcclusion)]
        [TestCase(TexturePaintChannel.Emission)]
        [TestCase(TexturePaintChannel.Custom)]
        [TestCase(TexturePaintChannel.SkinColorMask)]
        [TestCase(TexturePaintChannel.Thickness)]
        [TestCase(TexturePaintChannel.DetailMask)]
        public void BatchedPreviewRefreshesEveryDirtyTile(TexturePaintChannel paintedChannel)
        {
            const int size = 256;
            bool normalChannel = paintedChannel == TexturePaintChannel.Normal ||
                paintedChannel == TexturePaintChannel.NormalControl;
            TexturePaintChannel visibleChannel = normalChannel ? TexturePaintChannel.Normal : paintedChannel;
            Color neutral = normalChannel ? new Color(0.5f, 0.5f, 1f, 1f) : Color.black;
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(
                neutral, visibleChannel, size: size);
            fixture.set.channelPackShader = TexturePaintGpuTestFixture.LoadShader("ChannelPack.compute");
            if (normalChannel)
                fixture.set.channels.Add(TexturePaintChannel.NormalControl, new TextureChannelTarget
                {
                    channel = TexturePaintChannel.NormalControl,
                    format = RenderTextureFormat.ARGBHalf,
                    editable = new EditableTextureTarget("Batched Height", size, size,
                        RenderTextureFormat.ARGBHalf, null, new Color(0.5f, 0.5f, 0.5f, 1f))
                });
            var physical = new TexturePhysicalChannelGroup
            {
                materialProperty = "_TestPacked",
                source = Texture2D.whiteTexture,
                packed = EditableTextureTarget.Create("Batched Packed Preview", size, size,
                    RenderTextureFormat.ARGBHalf)
            };
            for (int component = 0; component < 4; component++)
            {
                physical.componentTargets[component] = fixture.set.GetChannel(visibleChannel);
                physical.sourceComponents[component] = component;
            }
            // Exercise the roughness-to-smoothness conversion used by packed material maps.
            if (paintedChannel == TexturePaintChannel.Roughness)
            {
                physical.sourceComponents[3] = 0;
                physical.inverted[3] = true;
            }
            fixture.set.physicalChannelGroups.Add(physical.materialProperty, physical);
            using var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            fixture.set.compositor = compositor;
            foreach (var channel in fixture.set.channels.Values)
                channel.composite = EditableTextureTarget.Create("Soft stroke composite", size, size,
                    RenderTextureFormat.ARGBHalf);
            fixture.set.AddLayer("Soft stroke across tiles");
            // Warm the cache: the first stroke in a fresh preview takes the full-update path.
            fixture.set.BindPreviewTextures();
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(.75f, .4f);
            try
            {
                Color paint = paintedChannel == TexturePaintChannel.Normal
                    ? new Color(0.8f, 0.5f, 0.9f, 1f) : Color.white;
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Paint,
                    paint, paintedChannel, 1f);
                context.limitStrokeCoverage = true;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                for (int step = 0; step < 3; step++)
                {
                    engine.BeginSampleBatch();
                    var sample = TexturePaintGpuTestFixture.CenterSample();
                    sample.uv += new Vector2((step - 1) * .04f, (step - 1) * .03f);
                    Assert.That(engine.ApplySample(sample, 0.2f), Is.True);
                    Assert.That(engine.EndSampleBatch(), Is.True);
                }

                Color[] tiled = TexturePaintGpuTestFixture.ReadPixels(
                    fixture.set.GetVisibleTexture(visibleChannel));
                Color[] packed = TexturePaintGpuTestFixture.ReadPixels(physical.packed);
                engine.EndStroke();
                Color[] released = TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(visibleChannel));
                // Compare the interactive four-tile refresh against a full recomputation
                // of the same authored data, before EndStroke can hide a preview defect.
                fixture.set.CompositeChannel(paintedChannel);
                fixture.set.BindPreviewTextures(false);
                Color[] full = TexturePaintGpuTestFixture.ReadPixels(
                    fixture.set.GetVisibleTexture(visibleChannel));
                for (int tileY = 0; tileY < 2; tileY++)
                for (int tileX = 0; tileX < 2; tileX++)
                {
                    float maximumError = 0f;
                    float maximumPackedError = 0f;
                    float maximumChange = 0f;
                    for (int y = tileY * 128; y < (tileY + 1) * 128; y++)
                    for (int x = tileX * 128; x < (tileX + 1) * 128; x++)
                    {
                        int index = y * size + x;
                        maximumError = Mathf.Max(maximumError, ColorDistance(tiled[index], full[index]));
                        maximumError = Mathf.Max(maximumError, ColorDistance(tiled[index], released[index]));
                        Color expectedPacked = full[index];
                        if (paintedChannel == TexturePaintChannel.Roughness)
                            expectedPacked.a = 1f - expectedPacked.r;
                        maximumPackedError = Mathf.Max(maximumPackedError,
                            ColorDistance(packed[index], expectedPacked));
                        maximumChange = Mathf.Max(maximumChange, ColorDistance(full[index], neutral));
                    }
                    Assert.That(maximumChange, Is.GreaterThan(0.01f), "Each tile must contain painted detail.");
                    Assert.That(maximumError, Is.LessThan(0.004f),
                        $"Preview skipped dirty tile ({tileX}, {tileY}) for {paintedChannel}.");
                    Assert.That(maximumPackedError, Is.LessThan(0.004f),
                        $"Packed preview skipped dirty tile ({tileX}, {tileY}) for {paintedChannel}.");
                }
                engine.EndStroke();
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
                    ReferencePaint(Base, correctedColor.linear, TexturePaintBlendMode.Normal), fixture.ReadPixels());
                Assert.That(engine.Undo(), Is.True);
                Color[] restored = fixture.ReadPixels();
                for (int i = 0; i < restored.Length; i++)
                    Assert.That(ColorDistance(restored[i], Base), Is.LessThan(0.002f));
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [TestCase(TexturePaintTool.Dodge)]
        [TestCase(TexturePaintTool.Burn)]
        public void TonalToolsMatchReferenceImage(TexturePaintTool tool)
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

        // ---- Brush tools on a Paint layer. A layer is transparent where nothing was painted, so the tools must
        // respect its coverage. Each case runs on the GPU and on the CPU fallback, with and without Cap Per Stroke.

        private static readonly Color LayerRed = new Color(0.8f, 0.1f, 0.1f, 1f);

        /// <summary>One stamp of a tool at the centre of a Paint layer; returns the layer pixels.</summary>
        private static Color[] StampOnLayer(TexturePaintTool tool, System.Func<float, float, Color> layerPaint,
            bool gpu, bool capPerStroke, float strength = 1f)
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(new Color(0.2f, 0.3f, 0.4f, 1f));
            int size = TexturePaintGpuTestFixture.Size;
            fixture.set.AddLayer("Paint");
            EditableTextureTarget layer = fixture.set.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = layerPaint((x + 0.5f) / size, (y + 0.5f) / size);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            Graphics.CopyTexture(texture, layer.Front);
            Graphics.CopyTexture(texture, layer.Back);
            Object.DestroyImmediate(texture);

            using PaintingEngine engine = gpu ? TexturePaintGpuTestFixture.CreateEngine() : new PaintingEngine(null, null, null);
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, tool, Color.white, strength: strength);
                context.limitStrokeCoverage = capPerStroke;
                context.cloneSourceUV = new Vector2(0.25f, 0.5f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay), Is.True, engine.LastStrokeError);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(new Vector2(0.4f, 0.5f)), Radius), Is.True);
                engine.EndStroke();
                if (gpu) Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0));
                else Assert.That(engine.Performance.cpuFallbacks, Is.GreaterThan(0));
                return TexturePaintGpuTestFixture.ReadPixels(layer.Front);
            }
            finally { Object.DestroyImmediate(brush); }
        }

        private static Color LayerPixel(Color[] pixels, float u, float v = 0.5f)
        {
            int size = TexturePaintGpuTestFixture.Size;
            return pixels[Mathf.FloorToInt(v * size) * size + Mathf.FloorToInt(u * size)];
        }

        [Test]
        public void EraseOnTheEditableBaseBringsBackTheOriginal([Values] bool gpu, [Values] bool capPerStroke)
        {
            if (gpu) TexturePaintGpuTestFixture.RequireComputeShaders();
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black);
            int size = TexturePaintGpuTestFixture.Size;
            // An original with detail, so a restore is not the same as painting one colour.
            Color[] original = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                original[y * size + x] = new Color(x / (float)size, y / (float)size, 0.35f, 1f);
            fixture.SetPixels(original);

            using PaintingEngine engine = gpu ? TexturePaintGpuTestFixture.CreateEngine() : new PaintingEngine(null, null, null);
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            try
            {
                // Erasing a base that was never painted changes nothing.
                Stroke(TexturePaintTool.Erase, Color.white, 1f);
                TexturePaintGpuTestFixture.AssertImage("untouched base", original, fixture.ReadPixels(), 0.002f, 0.0005f);

                // Paint the base, then erase it at half strength: halfway back.
                Color paint = new Color(0.9f, 0.1f, 0.6f, 1f);
                Stroke(TexturePaintTool.Paint, paint, 1f);
                Color painted = fixture.ReadPixels()[size / 2 * size + size / 2];
                // Albedo is painted in linear space, on the GPU and on the CPU fallback alike.
                Color working = TexturePaintChannelUtility.WorkingColor(TexturePaintChannel.Albedo, paint);
                Assert.That(painted.r, Is.EqualTo(working.r).Within(0.01f), "the base was painted");
                Assert.That(painted.b, Is.EqualTo(working.b).Within(0.01f));
                Stroke(TexturePaintTool.Erase, Color.white, 0.5f);
                Color half = fixture.ReadPixels()[size / 2 * size + size / 2];
                Color expected = Color.Lerp(painted, original[size / 2 * size + size / 2], 0.5f);
                Assert.That(half.r, Is.EqualTo(expected.r).Within(0.01f), "half strength goes halfway back");
                Assert.That(half.b, Is.EqualTo(expected.b).Within(0.01f));

                // A full-strength Erase brings the original back where the brush is solid.
                Stroke(TexturePaintTool.Erase, Color.white, 1f);
                Color[] restored = fixture.ReadPixels();
                for (int y = size / 2 - 4; y <= size / 2 + 4; y++)
                for (int x = size / 2 - 4; x <= size / 2 + 4; x++)
                {
                    Color a = restored[y * size + x], b = original[y * size + x];
                    Assert.That(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)),
                        Is.LessThan(0.01f), $"original restored at {x},{y}");
                }

                // Clearing the session also returns to the original copy.
                Stroke(TexturePaintTool.Paint, new Color(0.9f, 0.1f, 0.6f, 1f), 1f);
                fixture.set.ClearModifications();
                TexturePaintGpuTestFixture.AssertImage("cleared base", original, fixture.ReadPixels(), 0.002f, 0.0005f);
                Assert.That(fixture.set.GetChannel(TexturePaintChannel.Albedo).original, Is.Null, "the copy is freed after clearing");
            }
            finally { Object.DestroyImmediate(brush); }

            void Stroke(TexturePaintTool tool, Color color, float strength)
            {
                StrokeContext context = fixture.CreateContext(brush, tool, color, strength: strength);
                context.limitStrokeCoverage = capPerStroke;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True, engine.LastStrokeError);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), Radius), Is.True);
                engine.EndStroke();
            }
        }

        [Test]
        public void EraseOnAPaintLayerTakesThePaintAway([Values] bool gpu, [Values] bool capPerStroke)
        {
            if (gpu) TexturePaintGpuTestFixture.RequireComputeShaders();
            Color erased = LayerPixel(StampOnLayer(TexturePaintTool.Erase, (u, v) => LayerRed, gpu, capPerStroke), 0.5f);
            Assert.That(erased.a, Is.EqualTo(0f).Within(0.005f), "A full-strength Erase removes the paint.");

            Color half = LayerPixel(StampOnLayer(TexturePaintTool.Erase, (u, v) => LayerRed, gpu, capPerStroke, 0.5f), 0.5f);
            Assert.That(half.a, Is.EqualTo(0.5f).Within(0.01f), "Half strength removes half the coverage.");
            Assert.That(half.r, Is.EqualTo(LayerRed.r).Within(0.01f), "Erase leaves the colour of what remains.");
            Assert.That(half.g, Is.EqualTo(LayerRed.g).Within(0.01f));

            Color[] untouched = StampOnLayer(TexturePaintTool.Erase, (u, v) => LayerRed, gpu, capPerStroke);
            Assert.That(LayerPixel(untouched, 0.05f).a, Is.EqualTo(1f).Within(0.005f), "Paint outside the brush stays.");

            Color empty = LayerPixel(StampOnLayer(TexturePaintTool.Erase, (u, v) => Color.clear, gpu, capPerStroke), 0.5f);
            Assert.That(empty.a, Is.EqualTo(0f).Within(0.005f), "Erasing an empty layer must not paint on it.");
        }

        [Test]
        public void DodgeAndBurnOnAPaintLayerKeepItsCoverage([Values] bool gpu, [Values] bool capPerStroke)
        {
            if (gpu) TexturePaintGpuTestFixture.RequireComputeShaders();
            Color half = new Color(LayerRed.r, LayerRed.g, LayerRed.b, 0.5f);
            Color dodged = LayerPixel(StampOnLayer(TexturePaintTool.Dodge, (u, v) => half, gpu, capPerStroke), 0.5f);
            Assert.That(dodged.a, Is.EqualTo(0.5f).Within(0.005f), "Dodge does not change coverage.");
            Assert.That(dodged.g, Is.GreaterThan(0.95f), "Dodge lightens the paint.");

            Color burned = LayerPixel(StampOnLayer(TexturePaintTool.Burn, (u, v) => half, gpu, capPerStroke), 0.5f);
            Assert.That(burned.a, Is.EqualTo(0.5f).Within(0.005f), "Burn does not change coverage.");
            Assert.That(burned.r, Is.LessThan(0.05f), "Burn darkens the paint.");

            foreach (TexturePaintTool tool in new[] { TexturePaintTool.Dodge, TexturePaintTool.Burn })
                Assert.That(LayerPixel(StampOnLayer(tool, (u, v) => Color.clear, gpu, capPerStroke), 0.5f).a,
                    Is.EqualTo(0f).Within(0.005f), tool + " on an empty layer must not paint on it.");
        }

        [Test]
        public void SmearAndCloneOnAPaintLayerCarryCoverageWithTheColour([Values] bool gpu, [Values] bool capPerStroke)
        {
            if (gpu) TexturePaintGpuTestFixture.RequireComputeShaders();
            // The left half of the layer is painted red; the right half is empty.
            System.Func<float, float, Color> halfPainted = (u, v) => u < 0.5f ? LayerRed : Color.clear;

            // Clone copies from a quarter of the texture to the left of each pixel.
            Color[] cloned = StampOnLayer(TexturePaintTool.Clone, halfPainted, gpu, capPerStroke);
            Color clonedPaint = LayerPixel(cloned, 0.62f);
            Assert.That(clonedPaint.a, Is.EqualTo(1f).Within(0.01f), "Cloned paint arrives with its coverage.");
            Assert.That(clonedPaint.r, Is.EqualTo(LayerRed.r).Within(0.02f));
            Assert.That(clonedPaint.g, Is.EqualTo(LayerRed.g).Within(0.02f), "The cloned colour is not darkened.");
            Assert.That(LayerPixel(cloned, 0.78f).a, Is.EqualTo(0f).Within(0.01f),
                "Cloning an empty area leaves the layer empty there.");
            Assert.That(LayerPixel(cloned, 0.32f).a, Is.EqualTo(1f).Within(0.01f), "Paint cloned onto paint stays opaque.");

            // Smear drags from a tenth of the texture to the left.
            Color[] smeared = StampOnLayer(TexturePaintTool.Smear, halfPainted, gpu, capPerStroke);
            Color smearedPaint = LayerPixel(smeared, 0.56f);
            Assert.That(smearedPaint.a, Is.GreaterThan(0.95f), "Smeared paint arrives with its coverage.");
            Assert.That(smearedPaint.g, Is.EqualTo(LayerRed.g).Within(0.03f), "The smeared colour is not darkened.");
            Assert.That(LayerPixel(smeared, 0.72f).a, Is.EqualTo(0f).Within(0.01f),
                "Smearing an empty area leaves the layer empty there.");
        }

        [Test]
        public void AStrokeSkipsTargetMembersWhoseMaterialLacksTheChannel()
        {
            // The tiles of an avatar can use different materials: only the head tile has the wrinkle channels.
            using TexturePaintGpuTestFixture head = new TexturePaintGpuTestFixture(Color.black, TexturePaintChannel.WrinkleHeight);
            using TexturePaintGpuTestFixture torso = new TexturePaintGpuTestFixture(Color.black, TexturePaintChannel.Albedo);
            torso.set.persistentId = "golden-torso";
            torso.set.surface.index = 1;
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = head.CreateBrush(1f, 1f);
            try
            {
                StrokeContext context = head.CreateContext(brush, TexturePaintTool.Paint, Color.white,
                    TexturePaintChannel.WrinkleHeight, 1f);
                var sets = new List<TextureSet> { head.set, torso.set };
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture, sets), Is.True,
                    engine.LastStrokeError);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), Radius), Is.True);
                engine.EndStroke();
                int centre = TexturePaintGpuTestFixture.Size / 2 * TexturePaintGpuTestFixture.Size + TexturePaintGpuTestFixture.Size / 2;
                Assert.That(head.ReadPixels()[centre].r, Is.GreaterThan(0.9f), "The member with the channel is painted.");
                Assert.That(torso.ReadPixels()[centre].r, Is.EqualTo(0f).Within(0.001f), "The member without it is left alone.");

                // A target where no member has the channel still refuses, with the reason.
                StrokeContext missing = torso.CreateContext(brush, TexturePaintTool.Paint, Color.white,
                    TexturePaintChannel.Roughness, 1f);
                Assert.That(engine.BeginStroke(missing, TexturePaintSourceMode.SourceTexture, sets), Is.False);
                Assert.That(engine.LastStrokeError, Is.Not.Null.And.Not.Empty);
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [TestCase(512, 1f, 1)]
        [TestCase(1024, 1f, 1)]
        [TestCase(2048, 2f, 3)]
        [TestCase(4096, 4f, 6)]
        public void BlurReachFollowsTheTextureSize(int size, float scale, int radius)
        {
            var stamp = new RectInt(0, 0, 128, 128);
            Assert.That(PaintingEngine.BlurScale(size, size, stamp, long.MaxValue), Is.EqualTo(scale));
            Assert.That(PaintingEngine.BlurRadius(scale), Is.EqualTo(radius));
            // A stamp too large for the budget falls back to the 3x3 kernel instead of stalling the stroke.
            Assert.That(PaintingEngine.BlurScale(size, size, new RectInt(0, 0, size, size), 1L), Is.EqualTo(1f));
        }

        [Test]
        public void OneBlurStampSoftensAnEdgeTheSameDistanceAt1024And2048()
        {
            float small = BlurredEdgeSpread(1024);
            float large = BlurredEdgeSpread(2048);
            Assert.That(small, Is.GreaterThan(0.0003f), "The stamp softened the edge.");
            Assert.That(large / small, Is.InRange(0.8f, 1.25f),
                "The same stamp must soften an edge the same distance on the surface at 1024 and 2048 (" +
                small + " and " + large + " of the texture width).");
        }

        /// <summary>The area between a step edge after one Blur stamp and the sharp edge, along the middle row, in UV units.</summary>
        private static float BlurredEdgeSpread(int size)
        {
            using TexturePaintGpuTestFixture fixture = new TexturePaintGpuTestFixture(Color.black, size: size);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = x >= size / 2 ? Color.white : Color.black;
            fixture.SetPixels(pixels);
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = fixture.CreateBrush(1f, 1f);
            try
            {
                StrokeContext context = fixture.CreateContext(brush, TexturePaintTool.Blur, Color.white, strength: 1f);
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceTexture), Is.True);
                Assert.That(engine.ApplySample(TexturePaintGpuTestFixture.CenterSample(), 0.1f), Is.True);
                engine.EndStroke();
                Assert.That(engine.Performance.computeDispatches, Is.GreaterThan(0));
                Color[] result = fixture.ReadPixels();
                int row = size / 2;
                int reach = size / 20;
                float spread = 0f;
                for (int x = size / 2 - reach; x < size / 2 + reach; x++)
                    spread += Mathf.Abs(result[row * size + x].r - (x >= size / 2 ? 1f : 0f));
                return spread / size;
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
                engine.BeginSampleBatch();
                Assert.That(engine.ApplySample(torsoSample, Radius), Is.True);
                Assert.That(engine.ApplySample(legSample, Radius), Is.True);
                Assert.That(engine.EndSampleBatch(), Is.True);
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
        public void LinkedStrokeFallsBackFromNullMemberTextureSource()
        {
            using TexturePaintGpuTestFixture torso = new TexturePaintGpuTestFixture(Base);
            using TexturePaintGpuTestFixture arms = new TexturePaintGpuTestFixture(Base);
            arms.set.persistentId = "golden-arms";
            arms.set.surface.index = 1;
            arms.set.surface.slotName = "Arms";
            arms.set.surface.slotNames[0] = "Arms";
            arms.set.surface.triangleSlotNames = new[] { "Arms", "Arms" };
            TexturePaintLayer torsoLayer = torso.set.AddLayer("Linked Paint");
            TexturePaintLayer armsLayer = arms.set.AddLayer("Linked Paint");
            var validSource = new TexturePaintChannelSourceSettings
            {
                source = TexturePaintBrushSource.Color,
                color = Paint
            };
            torsoLayer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings = validSource.Clone();
            armsLayer.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings =
                new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Texture,
                    sourceTexture = null
                };
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = torso.CreateBrush(Hardness, Flow);
            try
            {
                StrokeContext context = torso.CreateContext(brush, TexturePaintTool.Paint, Paint,
                    strength: Strength);
                context.channelSources[TexturePaintChannel.Albedo] = validSource;
                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay,
                    new List<TextureSet> { torso.set, arms.set }), Is.True, engine.LastStrokeError);
                StrokeSample torsoSample = TexturePaintGpuTestFixture.CenterSample();
                StrokeSample armSample = TexturePaintGpuTestFixture.CenterSample();
                armSample.surfaceIndex = 1;
                armSample.slotName = "Arms";
                Assert.That(engine.ApplySample(torsoSample, Radius), Is.True);
                Assert.That(engine.ApplySample(armSample, Radius), Is.True);
                engine.EndStroke();

                Color torsoCenter = TexturePaintGpuTestFixture.ReadPixels(
                    torsoLayer.channels[TexturePaintChannel.Albedo].Front)
                    [32 * TexturePaintGpuTestFixture.Size + 32];
                Color armCenter = TexturePaintGpuTestFixture.ReadPixels(
                    armsLayer.channels[TexturePaintChannel.Albedo].Front)
                    [32 * TexturePaintGpuTestFixture.Size + 32];
                Assert.That(ColorDistance(torsoCenter, armCenter), Is.LessThan(0.002f));
                Assert.That(ColorDistance(torsoCenter, Color.clear), Is.GreaterThan(0.1f));
            }
            finally { Object.DestroyImmediate(brush); }
        }

        [Test]
        public void LinkedStrokeReportsMemberWithoutWritableRasterTarget()
        {
            using TexturePaintGpuTestFixture torso = new TexturePaintGpuTestFixture(Base);
            using TexturePaintGpuTestFixture arms = new TexturePaintGpuTestFixture(Base);
            arms.set.persistentId = "golden-arms";
            torso.set.AddLayer("Linked Paint");
            TexturePaintLayer armsLayer = arms.set.AddLayer("Linked Paint");
            armsLayer.GetChannelSettings(TexturePaintChannel.Albedo).locked = true;
            using PaintingEngine engine = TexturePaintGpuTestFixture.CreateEngine();
            BrushPreset brush = torso.CreateBrush(Hardness, Flow);
            try
            {
                StrokeContext context = torso.CreateContext(brush, TexturePaintTool.Paint, Paint,
                    strength: Strength);
                context.channelSources[TexturePaintChannel.Albedo] =
                    new TexturePaintChannelSourceSettings
                    {
                        source = TexturePaintBrushSource.Color,
                        color = Paint
                    };

                Assert.That(engine.BeginStroke(context, TexturePaintSourceMode.SourceOverlay,
                    new List<TextureSet> { torso.set, arms.set }), Is.False);
                Assert.That(engine.LastStrokeError, Does.Contain("golden-arms"));
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
