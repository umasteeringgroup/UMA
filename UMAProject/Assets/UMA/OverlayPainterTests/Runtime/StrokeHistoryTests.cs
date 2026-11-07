#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class StrokeHistoryTests
    {
        [Test]
        public void UndoAndRedoRestoreGpuTextureTile()
        {
            EditableTextureTarget target = new EditableTextureTarget("History Test", 8, 8, RenderTextureFormat.ARGB32, null, Color.black);
            StrokeHistory history = new StrokeHistory();
            history.Begin("Paint", target, new RectInt(0, 0, 8, 8));
            Clear(target.Front, Color.red);
            history.Commit();
            Assert.That(history.Undo(), Is.True);
            Assert.That(ReadCenter(target.Front).r, Is.LessThan(0.1f));
            Assert.That(history.Redo(), Is.True);
            Assert.That(ReadCenter(target.Front).r, Is.GreaterThan(0.9f));
            history.Dispose(); target.Dispose();
        }

        [Test]
        public void CommitVersionAdvancesAndNewEditCanDiscardRedo()
        {
            EditableTextureTarget target = new EditableTextureTarget("History Version Test", 8, 8,
                RenderTextureFormat.ARGB32, null, Color.black);
            StrokeHistory history = new StrokeHistory();
            long initialVersion = history.CommitVersion;
            history.Begin("Paint", target, new RectInt(0, 0, 8, 8));
            Clear(target.Front, Color.blue);
            history.Commit();

            Assert.That(history.CommitVersion, Is.GreaterThan(initialVersion));
            Assert.That(history.Undo(), Is.True);
            Assert.That(history.CanRedo, Is.True);
            history.ClearRedo();
            Assert.That(history.CanRedo, Is.False);

            history.Dispose(); target.Dispose();
        }

        [Test]
        public void RevertLatestReplacesAProceduralHistoryGroupWithoutCreatingRedo()
        {
            EditableTextureTarget target = new EditableTextureTarget("Procedural History Test", 8, 8,
                RenderTextureFormat.ARGB32, null, Color.black);
            StrokeHistory history = new StrokeHistory();
            history.BeginGroup("spline:layer-1");
            history.Include("Apply Spline", target, new RectInt(0, 0, 8, 8));
            Clear(target.Front, Color.green);
            history.Commit();

            Assert.That(history.RevertLatest("different-spline"), Is.False);
            Assert.That(history.RevertLatest("spline:layer-1"), Is.True);
            Assert.That(ReadCenter(target.Front).g, Is.LessThan(0.1f));
            Assert.That(history.CanUndo, Is.False);
            Assert.That(history.CanRedo, Is.False);

            history.Dispose(); target.Dispose();
        }

        private static void Clear(RenderTexture target, Color color)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            GL.Clear(false, true, color); RenderTexture.active = previous;
        }

        private static Color ReadCenter(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(4, 4, 1, 1), 0, 0); readback.Apply();
            Color result = readback.GetPixel(0, 0); Object.DestroyImmediate(readback); RenderTexture.active = previous; return result;
        }
    }
}
#endif
