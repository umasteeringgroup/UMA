#if UNITY_INCLUDE_TESTS
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class PerformancePrecisionTests
    {
        [Test]
        public void HistoryStoresOnlyTouchedFormatPreservingTiles()
        {
            EditableTextureTarget target = new EditableTextureTarget("Sparse History", 512, 512,
                RenderTextureFormat.ARGBHalf, null, Color.black);
            StrokeHistory history = new StrokeHistory { TileSize = 64, MemoryBudgetBytes = 32 * 1024 * 1024 };
            history.Begin("Small Mark", target, new RectInt(250, 250, 4, 4));
            Clear(target.Front, new Color(0.125f, 0.375f, 0.625f, 0.875f));
            history.Commit();

            Assert.That(history.UndoTileCount, Is.EqualTo(1));
            Assert.That(history.EstimatedMemoryBytes, Is.LessThan(512L * 512L * 8L * 2L));
            Assert.That(history.Undo(), Is.True);
            Assert.That(Read(target.Front, 251, 251).r, Is.LessThan(0.01f));
            Assert.That(history.Redo(), Is.True);
            Color restored = Read(target.Front, 251, 251);
            Assert.That(restored.r, Is.EqualTo(0.125f).Within(0.002f));
            Assert.That(restored.g, Is.EqualTo(0.375f).Within(0.002f));
            Assert.That(restored.b, Is.EqualTo(0.625f).Within(0.002f));
            history.Dispose(); target.Dispose();
        }

        [Test]
        public void DirtyTileSynchronizationPreservesEarlierTiles()
        {
            EditableTextureTarget target = new EditableTextureTarget("Dirty Sync", 16, 16,
                RenderTextureFormat.ARGB32, null, Color.black);
            ClearRect(target.Back, new RectInt(0, 0, 4, 4), Color.red);
            target.SwapAndSynchronize(new RectInt(0, 0, 4, 4));
            ClearRect(target.Back, new RectInt(12, 12, 4, 4), Color.green);
            target.SwapAndSynchronize(new RectInt(12, 12, 4, 4));

            Assert.That(Read(target.Front, 1, 1).r, Is.GreaterThan(0.9f));
            Assert.That(Read(target.Front, 14, 14).g, Is.GreaterThan(0.9f));
            target.Dispose();
        }

        [Test]
        public void TangentMapsAreSharedByMeshUvHashAndReferenceCounted()
        {
            int baseline = TangentSpaceMapBuilder.CachedMapCount;
            Mesh mesh = TriangleMesh();
            TangentSpaceMaps first = TangentSpaceMapBuilder.Build(mesh, 16, 16);
            TangentSpaceMaps second = TangentSpaceMapBuilder.Build(mesh, 16, 16);

            Assert.That(second.vertexNormals, Is.SameAs(first.vertexNormals));
            Assert.That(TangentSpaceMapBuilder.CachedMapCount, Is.EqualTo(baseline + 1));
            first.Dispose();
            Assert.That(TangentSpaceMapBuilder.CachedMapCount, Is.EqualTo(baseline + 1));
            second.Dispose();
            Assert.That(TangentSpaceMapBuilder.CachedMapCount, Is.EqualTo(baseline));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MapBuildHonorsCancellationBeforeAllocating()
        {
            Mesh mesh = TriangleMesh();
            CancellationTokenSource source = new CancellationTokenSource();
            source.Cancel();
            Assert.Throws<System.OperationCanceledException>(() => TangentSpaceMapBuilder.Build(mesh, 16, 16, 2,
                new TexturePaintOperationContext(source.Token)));
            source.Dispose(); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MetricsExposeRollingP95AndMaximum()
        {
            TexturePaintPerformanceMetrics metrics = new TexturePaintPerformanceMetrics();
            for (int i = 1; i <= 100; i++) metrics.RecordPreview(i);
            Assert.That(metrics.PreviewP95Milliseconds, Is.EqualTo(95d));
            Assert.That(metrics.MaximumPreviewMilliseconds, Is.EqualTo(100d));
        }

        private static Mesh TriangleMesh()
        {
            Mesh mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
                triangles = new[] { 0, 1, 2 }
            };
            return mesh;
        }

        private static void Clear(RenderTexture target, Color color)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            GL.Clear(false, true, color); RenderTexture.active = previous;
        }

        private static void ClearRect(RenderTexture target, RectInt rect, Color color)
        {
            Texture2D patch = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[rect.width * rect.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            patch.SetPixels(pixels); patch.Apply(false, false);
            Graphics.CopyTexture(patch, 0, 0, 0, 0, rect.width, rect.height, target, 0, 0, rect.x, rect.y);
            Object.DestroyImmediate(patch);
        }

        private static Color Read(RenderTexture target, int x, int y)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D readback = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);
            readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0); readback.Apply(false, false);
            Color result = readback.GetPixel(0, 0);
            Object.DestroyImmediate(readback); RenderTexture.active = previous; return result;
        }
    }
}
#endif
