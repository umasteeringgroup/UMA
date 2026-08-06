#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UMA;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.TexturePaint.Tests
{
    public sealed class ReleaseQualityMatrixTests
    {
        [Test]
        public void LongStrokeRemainsGapFreeAndWithinInteractiveSamplingBudget()
        {
            const int inputCount = 10001;
            const float inputStep = 0.001f;
            const float spacing = 0.01f;
            WorldSpaceStrokeSampler sampler = new WorldSpaceStrokeSampler
            {
                Spacing = spacing,
                DirectionSmoothing = 0.25f
            };
            List<StrokeSample> output = new List<StrokeSample>(1100);
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < inputCount; i++)
            {
                float x = i * inputStep;
                sampler.Add(new StrokeSample(new Vector3(x, Mathf.Sin(x) * 0.01f, 0f), Vector3.forward,
                    new Vector2(x * 0.05f, 0.5f), 0, 0), output);
            }
            sampler.Flush(output);
            stopwatch.Stop();

            Assert.That(output.Count, Is.InRange(1000, 1002));
            for (int i = 1; i < output.Count; i++)
                Assert.That(Vector3.Distance(output[i - 1].worldPosition, output[i].worldPosition),
                    Is.LessThanOrEqualTo(spacing * 1.02f));
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000),
                "Sampling 10,001 pointer events exceeded the release budget.");
        }

        [Test]
        public void SharpBezierCurveHasNoGapsAndDirectionsFollowThePath()
        {
            const float spacing = 0.025f;
            TexturePaintSpline spline = new TexturePaintSpline { smoothHandles = false, useBezier = true };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);
            spline.SetWorldControl(0, false, new Vector3(0.02f, 2.5f, 0f), new Vector2(0.02f, 2.5f));
            spline.SetWorldControl(1, true, new Vector3(0.98f, -2.5f, 0f), new Vector2(0.98f, -2.5f));

            List<StrokeSample> samples = spline.Sample(spacing);

            Assert.That(samples.Count, Is.GreaterThan(100));
            for (int i = 1; i < samples.Count; i++)
            {
                Vector3 chord = samples[i].worldPosition - samples[i - 1].worldPosition;
                Assert.That(chord.magnitude, Is.LessThanOrEqualTo(spacing * 1.16f));
                if (samples[i].direction.sqrMagnitude > 0.5f)
                    Assert.That(Vector3.Dot(chord.normalized, samples[i].direction), Is.GreaterThan(0.88f));
            }
        }

        [Test]
        public void PreferredTriangleDisambiguatesMirroredOverlappingUVs()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    Vector3.zero, Vector3.right, Vector3.up,
                    new Vector3(10f, 0f), new Vector3(10f, 1f), new Vector3(11f, 0f)
                },
                normals = new[]
                {
                    Vector3.forward, Vector3.forward, Vector3.forward,
                    Vector3.forward, Vector3.forward, Vector3.forward
                },
                uv = new[]
                {
                    Vector2.zero, Vector2.right, Vector2.up,
                    Vector2.zero, Vector2.up, Vector2.right
                },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            GameObject owner = new GameObject("Overlapping UV Test");
            ReconstructedSurface surface = new ReconstructedSurface
            {
                gameObject = owner,
                mesh = mesh,
                triangleIslands = new[] { 0, 1 }
            };

            Assert.That(surface.TryUVToWorld(new Vector2(0.2f, 0.2f), 0, out Vector3 first,
                out _, out int firstTriangle, out _), Is.True);
            Assert.That(surface.TryUVToWorld(new Vector2(0.2f, 0.2f), 1, out Vector3 second,
                out _, out int secondTriangle, out _), Is.True);
            Assert.That(firstTriangle, Is.EqualTo(0));
            Assert.That(secondTriangle, Is.EqualTo(1));
            Assert.That(first.x, Is.LessThan(1f));
            Assert.That(second.x, Is.GreaterThan(9f));
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(mesh);
        }

        [TestCase(1024)]
        [TestCase(2048)]
        [TestCase(4096)]
        public void ProductionTextureResolutionAllocatesAndReleases(int resolution)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8))
                Assert.Ignore("R8 render targets are not supported on this runner.");
            EditableTextureTarget target = new EditableTextureTarget("Texture Paint Resolution Gate", resolution,
                resolution, RenderTextureFormat.R8, null, Color.black);
            Assert.That(target.Width, Is.EqualTo(resolution));
            Assert.That(target.Height, Is.EqualTo(resolution));
            Assert.That(target.Front.IsCreated() && target.Back.IsCreated(), Is.True);
            target.Dispose();
            Assert.That(target.Front, Is.Null);
            Assert.That(target.Back, Is.Null);
        }

        [TestCase(RenderTextureFormat.ARGB32, 0.006f)]
        [TestCase(RenderTextureFormat.ARGBHalf, 0.002f)]
        [TestCase(RenderTextureFormat.ARGBFloat, 0.00002f)]
        public void EditableTargetPreservesItsDeclaredFormatPrecision(RenderTextureFormat format, float tolerance)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(format))
                Assert.Ignore(format + " is not supported on this runner.");
            Color expected = new Color(0.123456f, 0.456789f, 0.876543f, 0.345678f);
            EditableTextureTarget target = new EditableTextureTarget("Texture Paint Precision Gate", 16, 16,
                format, null, expected);
            Color actual = Read(target.Front, 8, 8);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance));
            target.Dispose();
        }

        [UnityTest]
        public IEnumerator RepeatedResourceLifecyclesReturnToBaseline()
        {
            TexturePaintResourceSnapshot before = TexturePaintResourceDiagnostics.Capture();
            for (int i = 0; i < 12; i++)
            {
                EditableTextureTarget target = new EditableTextureTarget("Texture Paint Lifecycle Gate " + i,
                    64, 64, RenderTextureFormat.ARGBHalf, null, Color.clear);
                target.Dispose();
            }
            // Object.Destroy is intentionally deferred in PlayMode; measure after its frame boundary.
            yield return null;
            TexturePaintResourceSnapshot after = TexturePaintResourceDiagnostics.Capture();
            Assert.That(after.RenderTextureDelta(before), Is.EqualTo(0));
            Assert.That(after.TextureDelta(before), Is.EqualTo(0));
        }

        [Test]
        public void UndoHistoryPrunesToConfiguredMemoryBudget()
        {
            EditableTextureTarget target = new EditableTextureTarget("Texture Paint History Budget Gate",
                512, 512, RenderTextureFormat.ARGBFloat, null, Color.black);
            StrokeHistory history = new StrokeHistory
            {
                TileSize = 128,
                Capacity = 64,
                MemoryBudgetBytes = 1024L * 1024L
            };
            for (int i = 0; i < 10; i++)
            {
                int x = (i % 4) * 128, y = ((i / 4) % 4) * 128;
                history.Begin("Budget " + i, target, new RectInt(x, y, 128, 128));
                history.Commit();
            }
            Assert.That(history.EstimatedMemoryBytes, Is.LessThanOrEqualTo(history.MemoryBudgetBytes));
            Assert.That(history.UndoTileCount, Is.LessThan(10));
            history.Dispose();
            target.Dispose();
        }

        [TestCase("_BaseMap", UMAMaterial.ChannelType.Texture, TexturePaintChannel.Albedo)]
        [TestCase("_MainTex", UMAMaterial.ChannelType.Texture, TexturePaintChannel.Albedo)]
        [TestCase("_BumpMap", UMAMaterial.ChannelType.Texture, TexturePaintChannel.Normal)]
        [TestCase("_NormalMap", UMAMaterial.ChannelType.NormalMap, TexturePaintChannel.Normal)]
        [TestCase("_MaskMap", UMAMaterial.ChannelType.Texture, TexturePaintChannel.Metallic)]
        [TestCase("_MetallicGlossMap", UMAMaterial.ChannelType.Texture, TexturePaintChannel.Metallic)]
        [TestCase("_OcclusionMap", UMAMaterial.ChannelType.Texture, TexturePaintChannel.AmbientOcclusion)]
        [TestCase("_RoughnessMap", UMAMaterial.ChannelType.Texture, TexturePaintChannel.Roughness)]
        [TestCase("_EmissionMap", UMAMaterial.ChannelType.Texture, TexturePaintChannel.Emission)]
        public void SupportedUmaMaterialPipelineKeywordsResolveToLogicalChannels(string property,
            UMAMaterial.ChannelType type, TexturePaintChannel expected)
        {
            Assert.That(TextureStore.ResolveChannel(property, type), Is.EqualTo(expected));
        }

        private static Color Read(RenderTexture target, int x, int y)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            texture.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            texture.Apply(false, false);
            Color result = texture.GetPixel(0, 0);
            Object.DestroyImmediate(texture);
            RenderTexture.active = previous;
            return result;
        }
    }
}
#endif
