using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UMA.TexturePaint
{
    public readonly struct TexturePaintOperationContext
    {
        public readonly CancellationToken cancellationToken;
        public readonly IProgress<float> progress;

        public TexturePaintOperationContext(CancellationToken cancellationToken, IProgress<float> progress = null)
        {
            this.cancellationToken = cancellationToken;
            this.progress = progress;
        }

        public void ThrowIfCancellationRequested() => cancellationToken.ThrowIfCancellationRequested();
        public void Report(float value) => progress?.Report(Mathf.Clamp01(value));
    }

    public sealed class TexturePaintPerformanceMetrics
    {
        private readonly Queue<double> previewMilliseconds = new Queue<double>();
        private const int WindowSize = 256;
        public long copiedPixels;
        public long composedPixels;
        public int computeDispatches;
        public int cpuFallbacks;
        public int budgetFallbacks;
        public int geometryMaskBuilds;
        public double LastPreviewMilliseconds { get; private set; }
        public double MaximumPreviewMilliseconds { get; private set; }

        public void RecordPreview(double milliseconds)
        {
            LastPreviewMilliseconds = milliseconds;
            MaximumPreviewMilliseconds = Math.Max(MaximumPreviewMilliseconds, milliseconds);
            previewMilliseconds.Enqueue(milliseconds);
            while (previewMilliseconds.Count > WindowSize) previewMilliseconds.Dequeue();
        }

        public double PreviewP95Milliseconds
        {
            get
            {
                if (previewMilliseconds.Count == 0) return 0d;
                double[] values = previewMilliseconds.ToArray();
                Array.Sort(values);
                return values[Mathf.Clamp(Mathf.CeilToInt(values.Length * 0.95f) - 1, 0, values.Length - 1)];
            }
        }

        public void Reset()
        {
            previewMilliseconds.Clear(); copiedPixels = composedPixels = 0L;
            computeDispatches = cpuFallbacks = budgetFallbacks = geometryMaskBuilds = 0;
            LastPreviewMilliseconds = MaximumPreviewMilliseconds = 0d;
        }
    }

    public readonly struct TexturePaintResourceSnapshot
    {
        public readonly int renderTextures;
        public readonly int textures;

        public TexturePaintResourceSnapshot(int renderTextures, int textures)
        {
            this.renderTextures = renderTextures;
            this.textures = textures;
        }

        public int RenderTextureDelta(TexturePaintResourceSnapshot before) => renderTextures - before.renderTextures;
        public int TextureDelta(TexturePaintResourceSnapshot before) => textures - before.textures;
    }

    public static class TexturePaintResourceDiagnostics
    {
        public static TexturePaintResourceSnapshot Capture()
        {
            int renderTextures = 0, textures = 0;
            RenderTexture[] allRenderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>();
            for (int i = 0; i < allRenderTextures.Length; i++)
                if (IsOwned(allRenderTextures[i])) renderTextures++;
            Texture2D[] allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int i = 0; i < allTextures.Length; i++)
                if (IsOwned(allTextures[i])) textures++;
            return new TexturePaintResourceSnapshot(renderTextures, textures);
        }

        private static bool IsOwned(UnityEngine.Object value)
        {
            if (value == null || (value.hideFlags & HideFlags.HideAndDontSave) == 0) return false;
            string name = value.name ?? string.Empty;
            return name.Contains("Texture Paint") || name.Contains("Stroke") || name.Contains("Coverage") ||
                name.Contains("Vertex Normal Map") || name.Contains("Vertex Tangent Map") || name.Contains("UV Seam Lookup");
        }
    }
}
