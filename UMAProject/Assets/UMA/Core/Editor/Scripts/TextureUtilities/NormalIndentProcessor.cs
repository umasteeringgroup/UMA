using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.TextureUtilities
{
    public enum NormalMapDecodeMode
    {
        Auto,
        RawRgb,
        UnityNormal,
        Dxt5nm,
    }

    public struct NormalIndentPathSettings
    {
        public IList<Bezier2DPoint> points;
        public float pressure;
        public float widthPixels;
        public float endFadePixels;
        public float endTaperPixels;
        public float leftProfileSoftness;
        public float rightProfileSoftness;
        public int samplesPerSegment;
    }

    public struct NormalIndentNoiseSettings
    {
        public IList<Bezier2DPoint> shapePoints;
        public float pressure;
        public float boundaryFalloffPixels;
        public float noiseScalePixels;
        public int noiseOctaves;
        public int seed;
        public int samplesPerSegment;
        public bool mirrorNoiseAcrossX;
    }

    public static class NormalIndentProcessor
    {
        private const float Inv255 = 1f / 255f;

        public static Color32[] DecodeToRawNormals(Color32[] sourcePixels, NormalMapDecodeMode decodeMode)
        {
            if (sourcePixels == null)
            {
                return null;
            }

            NormalMapDecodeMode resolvedMode = decodeMode == NormalMapDecodeMode.Auto ? DetectDecodeMode(sourcePixels) : decodeMode;
            Color32[] rawPixels = new Color32[sourcePixels.Length];
            for (int pixelIndex = 0; pixelIndex < sourcePixels.Length; pixelIndex++)
            {
                rawPixels[pixelIndex] = EncodeRawNormal(DecodeSourceNormal(sourcePixels[pixelIndex], resolvedMode));
            }

            return rawPixels;
        }

        public static bool ApplyPathIndent(Color32[] rawNormalPixels, int width, int height, NormalIndentPathSettings settings)
        {
            if (rawNormalPixels == null || rawNormalPixels.Length != width * height || width <= 0 || height <= 0)
            {
                return false;
            }

            if (settings.points == null || settings.points.Count < 2)
            {
                return false;
            }

            float widthPixels = Mathf.Max(1f, settings.widthPixels);
            float halfWidth = widthPixels * 0.5f;
            float maxHalfWidth = Mathf.Max(1f, halfWidth);
            List<Bezier2DPathSample> samples = Bezier2DPath.SamplePixels(settings.points, false, width, height, Mathf.Max(4, settings.samplesPerSegment));
            if (samples.Count < 2)
            {
                return false;
            }

            float pathLength = Mathf.Max(0.001f, Bezier2DPath.GetSampledLength(samples));
            Bounds bounds = Bezier2DPath.GetPixelBounds(samples, maxHalfWidth + 3f, width, height);
            int minX = Mathf.Clamp(Mathf.FloorToInt(bounds.min.x), 0, width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(bounds.max.x), 0, width - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(bounds.min.y), 0, height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(bounds.max.y), 0, height - 1);
            int boxWidth = maxX - minX + 1;
            int boxHeight = maxY - minY + 1;
            if (boxWidth <= 0 || boxHeight <= 0)
            {
                return false;
            }

            float[] heights = new float[boxWidth * boxHeight];
            bool hasHeight = false;
            float clampedPressure = Mathf.Clamp(settings.pressure, 0f, 4f);
            float endFade = Mathf.Max(0f, settings.endFadePixels);
            float endTaper = Mathf.Max(0f, settings.endTaperPixels);
            float leftSoftness = Mathf.Clamp01(settings.leftProfileSoftness);
            float rightSoftness = Mathf.Clamp01(settings.rightProfileSoftness);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 pixelCenter = new Vector2(x + 0.5f, y + 0.5f);
                    if (!Bezier2DPath.TryFindClosestSampleSegment(samples, pixelCenter, out float distanceSquared, out float distanceAlong, out float signedDistance))
                    {
                        continue;
                    }

                    float distanceFromEnd = Mathf.Min(distanceAlong, pathLength - distanceAlong);
                    float widthTaper = endTaper <= 0.001f ? 1f : SmoothStep01(Mathf.Clamp01(distanceFromEnd / endTaper));
                    float localHalfWidth = Mathf.Lerp(Mathf.Max(0.75f, halfWidth * 0.2f), halfWidth, widthTaper);
                    if (distanceSquared > localHalfWidth * localHalfWidth)
                    {
                        continue;
                    }

                    float normalizedDistance = localHalfWidth <= 0.001f ? 1f : Mathf.Abs(signedDistance) / localHalfWidth;
                    float sideSoftness = signedDistance >= 0f ? leftSoftness : rightSoftness;
                    float profile = EvaluateSideProfile(normalizedDistance, sideSoftness);
                    float endpointFade = endFade <= 0.001f ? 1f : SmoothStep01(Mathf.Clamp01(distanceFromEnd / endFade));
                    float heightValue = -profile * endpointFade * clampedPressure;
                    if (Mathf.Abs(heightValue) <= 0.0001f)
                    {
                        continue;
                    }

                    heights[((y - minY) * boxWidth) + (x - minX)] = heightValue;
                    hasHeight = true;
                }
            }

            if (!hasHeight)
            {
                return false;
            }

            ApplyHeightDeltas(rawNormalPixels, width, height, heights, minX, maxX, minY, maxY, Mathf.Lerp(0f, 14f, clampedPressure));
            return true;
        }

        private static float EvaluateSideProfile(float normalizedDistance, float softness)
        {
            float distance = Mathf.Clamp01(normalizedDistance);
            float clampedSoftness = Mathf.Clamp01(softness);
            if (clampedSoftness <= 0.001f)
            {
                return 1f;
            }

            float edgeStart = Mathf.Lerp(0.98f, 0f, clampedSoftness);
            float edgeRange = Mathf.Max(0.001f, 1f - edgeStart);
            float edgeFade = 1f - SmoothStep01((distance - edgeStart) / edgeRange);
            float sideVisibility = Mathf.Lerp(1f, 0.05f, clampedSoftness * clampedSoftness);
            return edgeFade * sideVisibility;
        }

        public static bool ApplyFilledNoise(Color32[] rawNormalPixels, int width, int height, NormalIndentNoiseSettings settings)
        {
            if (rawNormalPixels == null || rawNormalPixels.Length != width * height || width <= 0 || height <= 0)
            {
                return false;
            }

            if (settings.shapePoints == null || settings.shapePoints.Count < 3)
            {
                return false;
            }

            Bezier2DMask mask = Bezier2DMask.Build(settings.shapePoints, width, height, Mathf.Max(0f, settings.boundaryFalloffPixels), Mathf.Max(4, settings.samplesPerSegment));
            if (mask.strengths == null || mask.strengths.Length == 0 || mask.insidePixelCount == 0)
            {
                return false;
            }

            float[] heights = new float[mask.boxWidth * mask.boxHeight];
            float pressure = Mathf.Clamp(settings.pressure, 0f, 4f);
            float noiseScale = Mathf.Max(1f, settings.noiseScalePixels);
            int octaves = Mathf.Clamp(settings.noiseOctaves, 1, 6);
            bool hasHeight = false;

            for (int y = mask.minY; y <= mask.maxY; y++)
            {
                for (int x = mask.minX; x <= mask.maxX; x++)
                {
                    float strength = mask.GetStrength(x, y);
                    if (strength <= 0f)
                    {
                        continue;
                    }

                    float noiseX = settings.mirrorNoiseAcrossX ? (width - 1 - x) / noiseScale : x / noiseScale;
                    float noise = SampleFractalNoise(noiseX, y / noiseScale, octaves, settings.seed) - 0.5f;
                    float heightValue = noise * strength * pressure * 2f;
                    if (Mathf.Abs(heightValue) <= 0.0001f)
                    {
                        continue;
                    }

                    heights[((y - mask.minY) * mask.boxWidth) + (x - mask.minX)] = heightValue;
                    hasHeight = true;
                }
            }

            if (!hasHeight)
            {
                return false;
            }

            ApplyHeightDeltas(rawNormalPixels, width, height, heights, mask.minX, mask.maxX, mask.minY, mask.maxY, Mathf.Lerp(0f, 10f, pressure));
            return true;
        }

        private static void ApplyHeightDeltas(Color32[] rawNormalPixels, int textureWidth, int textureHeight, float[] heights, int minX, int maxX, int minY, int maxY, float normalScale)
        {
            int boxWidth = maxX - minX + 1;
            int boxHeight = maxY - minY + 1;
            if (boxWidth <= 0 || boxHeight <= 0)
            {
                return;
            }

            Color32[] originalNormals = new Color32[rawNormalPixels.Length];
            System.Array.Copy(rawNormalPixels, originalNormals, rawNormalPixels.Length);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float center = GetHeightClamped(heights, boxWidth, boxHeight, x - minX, y - minY);
                    if (Mathf.Abs(center) <= 0.00001f)
                    {
                        continue;
                    }

                    float left = GetHeightClamped(heights, boxWidth, boxHeight, x - minX - 1, y - minY);
                    float right = GetHeightClamped(heights, boxWidth, boxHeight, x - minX + 1, y - minY);
                    float down = GetHeightClamped(heights, boxWidth, boxHeight, x - minX, y - minY - 1);
                    float up = GetHeightClamped(heights, boxWidth, boxHeight, x - minX, y - minY + 1);
                    float deltaX = (right - left) * normalScale;
                    float deltaY = (up - down) * normalScale;

                    int pixelIndex = (y * textureWidth) + x;
                    Vector3 sourceNormal = DecodeRawNormal(originalNormals[pixelIndex]);
                    Vector3 blended = new Vector3(sourceNormal.x - deltaX, sourceNormal.y - deltaY, sourceNormal.z).normalized;
                    rawNormalPixels[pixelIndex] = EncodeRawNormal(blended);
                }
            }
        }

        private static float GetHeightClamped(float[] heights, int width, int height, int x, int y)
        {
            int clampedX = Mathf.Clamp(x, 0, width - 1);
            int clampedY = Mathf.Clamp(y, 0, height - 1);
            return heights[(clampedY * width) + clampedX];
        }

        private static NormalMapDecodeMode DetectDecodeMode(Color32[] sourcePixels)
        {
            if (sourcePixels == null || sourcePixels.Length == 0)
            {
                return NormalMapDecodeMode.RawRgb;
            }

            int sampleCount = Mathf.Min(sourcePixels.Length, 4096);
            int step = Mathf.Max(1, sourcePixels.Length / sampleCount);
            int usefulAlphaCount = 0;
            int redVarianceSum = 0;
            byte previousRed = sourcePixels[0].r;
            for (int pixelIndex = 0; pixelIndex < sourcePixels.Length; pixelIndex += step)
            {
                Color32 pixel = sourcePixels[pixelIndex];
                if (pixel.a > 8 && pixel.a < 247)
                {
                    usefulAlphaCount++;
                }

                redVarianceSum += Mathf.Abs(pixel.r - previousRed);
                previousRed = pixel.r;
            }

            return usefulAlphaCount > sampleCount / 8 && redVarianceSum < sampleCount * 18
                ? NormalMapDecodeMode.Dxt5nm
                : NormalMapDecodeMode.RawRgb;
        }

        private static Vector3 DecodeSourceNormal(Color32 pixel, NormalMapDecodeMode decodeMode)
        {
            switch (decodeMode)
            {
                case NormalMapDecodeMode.Dxt5nm:
                    return ReconstructNormal((pixel.a * Inv255 * 2f) - 1f, (pixel.g * Inv255 * 2f) - 1f);
                case NormalMapDecodeMode.UnityNormal:
                    return ReconstructNormal((pixel.r * Inv255 * 2f) - 1f, (pixel.g * Inv255 * 2f) - 1f);
                case NormalMapDecodeMode.RawRgb:
                default:
                    Vector3 raw = new Vector3((pixel.r * Inv255 * 2f) - 1f, (pixel.g * Inv255 * 2f) - 1f, (pixel.b * Inv255 * 2f) - 1f);
                    return raw.sqrMagnitude <= 0.0001f ? Vector3.forward : raw.normalized;
            }
        }

        private static Vector3 DecodeRawNormal(Color32 pixel)
        {
            Vector3 raw = new Vector3((pixel.r * Inv255 * 2f) - 1f, (pixel.g * Inv255 * 2f) - 1f, (pixel.b * Inv255 * 2f) - 1f);
            return raw.sqrMagnitude <= 0.0001f ? Vector3.forward : raw.normalized;
        }

        private static Vector3 ReconstructNormal(float x, float y)
        {
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - (x * x) - (y * y)));
            Vector3 normal = new Vector3(x, y, z);
            return normal.sqrMagnitude <= 0.0001f ? Vector3.forward : normal.normalized;
        }

        private static Color32 EncodeRawNormal(Vector3 normal)
        {
            Vector3 normalized = normal.sqrMagnitude <= 0.0001f ? Vector3.forward : normal.normalized;
            return new Color32(
                FloatToByte((normalized.x * 0.5f) + 0.5f),
                FloatToByte((normalized.y * 0.5f) + 0.5f),
                FloatToByte((normalized.z * 0.5f) + 0.5f),
                255);
        }

        private static float SampleFractalNoise(float x, float y, int octaves, int seed)
        {
            float amplitude = 1f;
            float frequency = 1f;
            float value = 0f;
            float amplitudeSum = 0f;
            float offsetX = Hash01(seed, 17, 31) * 1024f;
            float offsetY = Hash01(seed, 41, 73) * 1024f;

            for (int octave = 0; octave < octaves; octave++)
            {
                value += Mathf.PerlinNoise((x + offsetX) * frequency, (y + offsetY) * frequency) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return amplitudeSum <= 0f ? 0.5f : Mathf.Clamp01(value / amplitudeSum);
        }

        private static float Hash01(int first, int second, int third)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)first) * 16777619u;
                hash = (hash ^ (uint)second) * 16777619u;
                hash = (hash ^ (uint)third) * 16777619u;
                hash ^= hash >> 13;
                hash *= 1274126177u;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) * (1f / 16777215f);
            }
        }

        private static float SmoothStep01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - (2f * t));
        }

        private static byte FloatToByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);
        }
    }
}
