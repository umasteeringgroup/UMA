using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

namespace UMA.TexturePaint.Editor
{
    internal readonly struct SpriteGridOptions
    {
        public readonly int columns;
        public readonly int rows;
        public readonly int inset;
        public readonly int insetX1;
        public readonly int insetY1;
        public readonly int insetX2;
        public readonly int insetY2;
        public readonly int horizontalOffset;
        public readonly int verticalOffset;
        public readonly bool makeTileable;
        public readonly float seamBlendFraction;
        public readonly bool fixBrightnessGradient;
        public readonly bool removePolynomialGradient;
        public readonly bool applyClahe;
        public readonly float claheStrength;
        public readonly bool applyBilateralFilter;
        public readonly float bilateralStrength;
        public readonly bool processInLinearSpace;
        public readonly float normalizationStrength;
        public readonly bool poissonSeamlessBlend;
        public readonly bool applyBlueNoiseDithering;
        public readonly float blueNoiseStrength;
        public readonly bool applyMultiOctaveNoise;
        public readonly float noiseStrength;
        public readonly float noiseFrequency;
        public readonly bool applyMicroWarping;
        public readonly float warpStrength;
        public readonly float warpFrequency;
        public readonly bool applyFrequencyScrambling;
        public readonly float scrambleStrength;

        public bool HasPixelFixes => makeTileable || fixBrightnessGradient ||
            removePolynomialGradient || (applyClahe && claheStrength > 0f) ||
            (applyBilateralFilter && bilateralStrength > 0f) || normalizationStrength > 0f ||
            poissonSeamlessBlend || (applyBlueNoiseDithering && blueNoiseStrength > 0f) ||
            (applyMultiOctaveNoise && noiseStrength > 0f) ||
            (applyMicroWarping && warpStrength > 0f) ||
            (applyFrequencyScrambling && scrambleStrength > 0f);

        public SpriteGridOptions(int columns, int rows, int inset, bool makeTileable,
            bool fixBrightnessGradient, bool removePolynomialGradient = false,
            bool applyClahe = false, float claheStrength = 0f,
            bool applyBilateralFilter = false, float bilateralStrength = 0f,
            bool processInLinearSpace = false, float normalizationStrength = 0f,
            bool poissonSeamlessBlend = false,
            bool applyBlueNoiseDithering = false, float blueNoiseStrength = 0f,
            bool applyMultiOctaveNoise = false, float noiseStrength = 0f,
            float noiseFrequency = 0.5f, bool applyMicroWarping = false,
            float warpStrength = 0f, float warpFrequency = 0.5f,
            bool applyFrequencyScrambling = false, float scrambleStrength = 0f,
            int horizontalOffset = 0, int verticalOffset = 0,
            int insetX1 = -1, int insetY1 = -1, int insetX2 = -1, int insetY2 = -1,
            float seamBlendFraction = 0.25f)
        {
            this.columns = columns;
            this.rows = rows;
            this.inset = inset;
            this.insetX1 = insetX1 < 0 ? inset : insetX1;
            this.insetY1 = insetY1 < 0 ? inset : insetY1;
            this.insetX2 = insetX2 < 0 ? inset : insetX2;
            this.insetY2 = insetY2 < 0 ? inset : insetY2;
            this.horizontalOffset = horizontalOffset;
            this.verticalOffset = verticalOffset;
            this.makeTileable = makeTileable;
            this.seamBlendFraction = seamBlendFraction <= 0f
                ? 0.25f
                : Mathf.Clamp(seamBlendFraction, 0.01f, 0.5f);
            this.fixBrightnessGradient = fixBrightnessGradient;
            this.removePolynomialGradient = removePolynomialGradient;
            this.applyClahe = applyClahe;
            this.claheStrength = Mathf.Clamp01(claheStrength);
            this.applyBilateralFilter = applyBilateralFilter;
            this.bilateralStrength = Mathf.Clamp01(bilateralStrength);
            this.processInLinearSpace = processInLinearSpace;
            this.normalizationStrength = Mathf.Clamp01(normalizationStrength);
            this.poissonSeamlessBlend = poissonSeamlessBlend;
            this.applyBlueNoiseDithering = applyBlueNoiseDithering;
            this.blueNoiseStrength = Mathf.Clamp01(blueNoiseStrength);
            this.applyMultiOctaveNoise = applyMultiOctaveNoise;
            this.noiseStrength = Mathf.Clamp01(noiseStrength);
            this.noiseFrequency = Mathf.Clamp01(noiseFrequency);
            this.applyMicroWarping = applyMicroWarping;
            this.warpStrength = Mathf.Clamp01(warpStrength);
            this.warpFrequency = Mathf.Clamp01(warpFrequency);
            this.applyFrequencyScrambling = applyFrequencyScrambling;
            this.scrambleStrength = Mathf.Clamp01(scrambleStrength);
        }
    }

    [Serializable]
    internal sealed class SpriteGridSpriteSettings
    {
        public int insetX1;
        public int insetY1;
        public int insetX2;
        public int insetY2;
        public int horizontalOffset;
        public int verticalOffset;
        public bool makeTileable;
        public float seamBlendFraction = 0.25f;
        public bool fixBrightnessGradient;
        public bool removePolynomialGradient;
        public bool applyClahe;
        public float claheStrength = 0.5f;
        public bool applyBilateralFilter;
        public float bilateralStrength = 0.5f;
        public bool processInLinearSpace;
        public float normalizationStrength;
        public bool poissonSeamlessBlend;
        public bool applyBlueNoiseDithering;
        public float blueNoiseStrength = 0.25f;
        public bool applyMultiOctaveNoise;
        public float noiseStrength = 0.25f;
        public float noiseFrequency = 0.5f;
        public bool applyMicroWarping;
        public float warpStrength = 0.25f;
        public float warpFrequency = 0.5f;
        public bool applyFrequencyScrambling;
        public float scrambleStrength = 0.25f;

        public SpriteGridSpriteSettings() { }

        public SpriteGridSpriteSettings(SpriteGridOptions options) => CopyFrom(options);

        public void CopyFrom(SpriteGridOptions options)
        {
            insetX1 = options.insetX1;
            insetY1 = options.insetY1;
            insetX2 = options.insetX2;
            insetY2 = options.insetY2;
            horizontalOffset = options.horizontalOffset;
            verticalOffset = options.verticalOffset;
            makeTileable = options.makeTileable;
            seamBlendFraction = options.seamBlendFraction;
            fixBrightnessGradient = options.fixBrightnessGradient;
            removePolynomialGradient = options.removePolynomialGradient;
            applyClahe = options.applyClahe;
            claheStrength = options.claheStrength;
            applyBilateralFilter = options.applyBilateralFilter;
            bilateralStrength = options.bilateralStrength;
            processInLinearSpace = options.processInLinearSpace;
            normalizationStrength = options.normalizationStrength;
            poissonSeamlessBlend = options.poissonSeamlessBlend;
            applyBlueNoiseDithering = options.applyBlueNoiseDithering;
            blueNoiseStrength = options.blueNoiseStrength;
            applyMultiOctaveNoise = options.applyMultiOctaveNoise;
            noiseStrength = options.noiseStrength;
            noiseFrequency = options.noiseFrequency;
            applyMicroWarping = options.applyMicroWarping;
            warpStrength = options.warpStrength;
            warpFrequency = options.warpFrequency;
            applyFrequencyScrambling = options.applyFrequencyScrambling;
            scrambleStrength = options.scrambleStrength;
        }

        public SpriteGridOptions ToOptions(int columns, int rows, int inset)
            => new SpriteGridOptions(columns, rows, inset, makeTileable, fixBrightnessGradient,
                removePolynomialGradient, applyClahe, claheStrength, applyBilateralFilter,
                bilateralStrength, processInLinearSpace, normalizationStrength, poissonSeamlessBlend,
                applyBlueNoiseDithering, blueNoiseStrength, applyMultiOctaveNoise, noiseStrength,
                noiseFrequency, applyMicroWarping, warpStrength, warpFrequency,
                applyFrequencyScrambling, scrambleStrength, horizontalOffset, verticalOffset,
                insetX1, insetY1, insetX2, insetY2, seamBlendFraction);
    }

    [Serializable]
    internal sealed class SpriteGridSavedConfiguration
    {
        public int version = 1;
        public int sourceWidth;
        public int sourceHeight;
        public int columns = 1;
        public int rows = 1;
        public int inset;
        public bool editSpritesIndividually;
        public SpriteGridSpriteSettings globalSettings = new SpriteGridSpriteSettings();
        public List<SpriteGridSpriteSettings> spriteSettings = new List<SpriteGridSpriteSettings>();

        public SpriteGridSavedConfiguration Clone()
        {
            var clone = new SpriteGridSavedConfiguration
            {
                version = version,
                sourceWidth = sourceWidth,
                sourceHeight = sourceHeight,
                columns = columns,
                rows = rows,
                inset = inset,
                editSpritesIndividually = editSpritesIndividually,
                globalSettings = globalSettings == null
                    ? new SpriteGridSpriteSettings()
                    : new SpriteGridSpriteSettings(globalSettings.ToOptions(columns, rows, inset))
            };
            if (spriteSettings != null)
                foreach (SpriteGridSpriteSettings settings in spriteSettings)
                    clone.spriteSettings.Add(settings == null
                        ? new SpriteGridSpriteSettings()
                        : new SpriteGridSpriteSettings(settings.ToOptions(columns, rows, inset)));
            return clone;
        }
    }

    internal static class SpriteGridConfigurationStore
    {
        private const string Marker = "UMA.OverlayPainter.SpriteGridOptions:";

        public static bool TryRead(TextureImporter importer, out SpriteGridSavedConfiguration configuration)
        {
            configuration = null;
            if (importer == null || string.IsNullOrEmpty(importer.userData)) return false;
            string[] lines = importer.userData.Replace("\r", string.Empty).Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (!lines[i].StartsWith(Marker, StringComparison.Ordinal)) continue;
                string json = lines[i].Substring(Marker.Length);
                try
                {
                    configuration = JsonUtility.FromJson<SpriteGridSavedConfiguration>(json);
                    return IsValid(configuration);
                }
                catch (ArgumentException)
                {
                    configuration = null;
                    return false;
                }
            }
            return false;
        }

        public static void Write(TextureImporter importer, SpriteGridSavedConfiguration configuration)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            if (!IsValid(configuration))
                throw new ArgumentException("The sprite grid configuration is incomplete.", nameof(configuration));
            var preservedLines = new List<string>();
            if (!string.IsNullOrEmpty(importer.userData))
            {
                string[] lines = importer.userData.Replace("\r", string.Empty).Split('\n');
                foreach (string line in lines)
                    if (!line.StartsWith(Marker, StringComparison.Ordinal) && line.Length > 0)
                        preservedLines.Add(line);
            }
            preservedLines.Add(Marker + JsonUtility.ToJson(configuration));
            importer.userData = string.Join("\n", preservedLines);
        }

        public static bool IsValid(SpriteGridSavedConfiguration configuration)
            => configuration != null && configuration.version == 1 &&
                configuration.columns > 0 && configuration.rows > 0 &&
                configuration.spriteSettings != null &&
                configuration.spriteSettings.Count == configuration.columns * configuration.rows;
    }

    internal static class SpriteGridProcessor
    {
        public static RectInt[] BuildSpriteRects(int textureWidth, int textureHeight,
            int columns, int rows, int inset)
            => BuildSpriteRects(textureWidth, textureHeight, columns, rows, inset, null);

        public static RectInt[] BuildSpriteRects(int textureWidth, int textureHeight,
            int columns, int rows, int inset, IReadOnlyList<SpriteGridOptions> spriteOptions)
        {
            if (textureWidth <= 0 || textureHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(textureWidth), "Texture dimensions must be positive.");
            if (columns <= 0 || rows <= 0)
                throw new ArgumentOutOfRangeException(nameof(columns), "Grid dimensions must be positive.");
            if (inset < 0) throw new ArgumentOutOfRangeException(nameof(inset));
            int spriteCount = columns * rows;
            if (spriteOptions != null && spriteOptions.Count != spriteCount)
                throw new ArgumentException("Every sprite rectangle must have one settings profile.",
                    nameof(spriteOptions));

            var result = new RectInt[spriteCount];
            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                int sourceRow = rows - row - 1;
                int cellYMin = Mathf.RoundToInt(sourceRow * textureHeight / (float)rows);
                int cellYMax = Mathf.RoundToInt((sourceRow + 1) * textureHeight / (float)rows);
                for (int column = 0; column < columns; column++)
                {
                    int cellXMin = Mathf.RoundToInt(column * textureWidth / (float)columns);
                    int cellXMax = Mathf.RoundToInt((column + 1) * textureWidth / (float)columns);
                    SpriteGridOptions tileOptions = spriteOptions != null
                        ? spriteOptions[index] : default;
                    int x1 = spriteOptions == null ? inset : tileOptions.insetX1;
                    int y1 = spriteOptions == null ? inset : tileOptions.insetY1;
                    int x2 = spriteOptions == null ? inset : tileOptions.insetX2;
                    int y2 = spriteOptions == null ? inset : tileOptions.insetY2;
                    if (x1 < 0 || y1 < 0 || x2 < 0 || y2 < 0)
                        throw new ArgumentException($"Sprite {index + 1} insets cannot be negative.");
                    int width = cellXMax - cellXMin - x1 - x2;
                    int height = cellYMax - cellYMin - y1 - y2;
                    if (width <= 0 || height <= 0)
                        throw new ArgumentException($"Sprite {index + 1} insets leave no usable pixels.");
                    int x = cellXMin + x1 + tileOptions.horizontalOffset;
                    int y = cellYMin + y1 + tileOptions.verticalOffset;
                    if (x < 0 || y < 0 || x + width > textureWidth || y + height > textureHeight)
                        throw new ArgumentException($"Sprite {index + 1} offset moves its rectangle " +
                            "outside the source texture.");
                    result[index++] = new RectInt(x, y, width, height);
                }
            }
            return result;
        }

        public static bool TryBuildConfigurationFromRects(int textureWidth, int textureHeight,
            IReadOnlyList<RectInt> sourceRects, int preferredColumns, int preferredRows,
            out SpriteGridSavedConfiguration configuration)
        {
            configuration = null;
            if (textureWidth <= 0 || textureHeight <= 0 || sourceRects == null || sourceRects.Count == 0)
                return false;

            int columns = preferredColumns;
            int rows = preferredRows;
            RectInt[] ordered;
            if (columns <= 0 || rows <= 0 || columns * rows != sourceRects.Count ||
                !TryOrderRectsByGrid(textureWidth, textureHeight, sourceRects,
                    columns, rows, out ordered))
            {
                columns = InferColumnCount(sourceRects);
                if (columns <= 0 || sourceRects.Count % columns != 0) return false;
                rows = sourceRects.Count / columns;
                if (!TryOrderRectsByGrid(textureWidth, textureHeight, sourceRects,
                        columns, rows, out ordered))
                    return false;
            }

            var saved = new SpriteGridSavedConfiguration
            {
                sourceWidth = textureWidth,
                sourceHeight = textureHeight,
                columns = columns,
                rows = rows,
                inset = 0,
                editSpritesIndividually = true
            };
            for (int index = 0; index < ordered.Length; index++)
            {
                int row = index / columns;
                int column = index % columns;
                int sourceRow = rows - row - 1;
                int cellXMin = Mathf.RoundToInt(column * textureWidth / (float)columns);
                int cellXMax = Mathf.RoundToInt((column + 1) * textureWidth / (float)columns);
                int cellYMin = Mathf.RoundToInt(sourceRow * textureHeight / (float)rows);
                int cellYMax = Mathf.RoundToInt((sourceRow + 1) * textureHeight / (float)rows);
                RectInt rect = ordered[index];
                int x1 = rect.xMin - cellXMin;
                int y1 = rect.yMin - cellYMin;
                int x2 = cellXMax - rect.xMax;
                int y2 = cellYMax - rect.yMax;
                if (x1 < 0 || y1 < 0 || x2 < 0 || y2 < 0) return false;
                saved.spriteSettings.Add(new SpriteGridSpriteSettings(new SpriteGridOptions(
                    columns, rows, 0, false, false,
                    insetX1: x1, insetY1: y1, insetX2: x2, insetY2: y2)));
            }
            saved.globalSettings = new SpriteGridSpriteSettings(
                saved.spriteSettings[0].ToOptions(columns, rows, 0));
            configuration = saved;
            return true;
        }

        private static bool TryOrderRectsByGrid(int textureWidth, int textureHeight,
            IReadOnlyList<RectInt> sourceRects, int columns, int rows, out RectInt[] ordered)
        {
            ordered = new RectInt[sourceRects.Count];
            var occupied = new bool[sourceRects.Count];
            foreach (RectInt rect in sourceRects)
            {
                if (rect.width <= 0 || rect.height <= 0 || rect.xMin < 0 || rect.yMin < 0 ||
                    rect.xMax > textureWidth || rect.yMax > textureHeight)
                    return false;
                int column = Mathf.Clamp(Mathf.FloorToInt(rect.center.x * columns / textureWidth),
                    0, columns - 1);
                int sourceRow = Mathf.Clamp(Mathf.FloorToInt(rect.center.y * rows / textureHeight),
                    0, rows - 1);
                int row = rows - sourceRow - 1;
                int index = row * columns + column;
                if ((uint)index >= (uint)ordered.Length || occupied[index]) return false;
                occupied[index] = true;
                ordered[index] = rect;
            }
            return occupied.All(value => value);
        }

        private static int InferColumnCount(IReadOnlyList<RectInt> rects)
        {
            float medianHeight = rects.Select(rect => (float)rect.height).OrderBy(value => value)
                .ElementAt(rects.Count / 2);
            float highestCenter = rects.Max(rect => rect.center.y);
            float rowTolerance = Mathf.Max(1f, medianHeight * 0.4f);
            int columns = rects.Count(rect => Mathf.Abs(rect.center.y - highestCenter) <= rowTolerance);
            return Mathf.Max(1, columns);
        }

        public static void ApplyTileFixes(Color32[] pixels, int textureWidth, RectInt[] rects,
            bool fixBrightnessGradient, bool makeTileable)
        {
            ApplyTileFixes(pixels, textureWidth, rects, new SpriteGridOptions(
                1, 1, 0, makeTileable, fixBrightnessGradient));
        }

        public static void ApplyTileFixes(Color32[] pixels, int textureWidth, RectInt[] rects,
            SpriteGridOptions options) => TileImageProcessor.Process(pixels, textureWidth, rects, options);

        public static void ApplyTileFixes(Color32[] pixels, int textureWidth, RectInt[] rects,
            IReadOnlyList<SpriteGridOptions> spriteOptions)
            => TileImageProcessor.Process(pixels, textureWidth, rects, spriteOptions);

        internal static void MakeSeamlesslyTileable(Color32[] pixels, int textureWidth, RectInt rect)
            => MakeSeamlesslyTileable(pixels, textureWidth, rect, 0.25f);

        internal static void MakeSeamlesslyTileable(Color32[] pixels, int textureWidth, RectInt rect,
            float blendFraction)
        {
            int horizontalBlend = CalculateSeamBlendPixels(rect.width, blendFraction);
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int offset = 0; offset < horizontalBlend; offset++)
            {
                int left = y * textureWidth + rect.xMin + offset;
                int right = y * textureWidth + rect.xMax - 1 - offset;
                float amount = EdgeBlendAmount(offset, horizontalBlend);
                Color32 average = Average(pixels[left], pixels[right]);
                pixels[left] = Lerp(pixels[left], average, amount);
                pixels[right] = Lerp(pixels[right], average, amount);
            }

            int verticalBlend = CalculateSeamBlendPixels(rect.height, blendFraction);
            for (int x = rect.xMin; x < rect.xMax; x++)
            for (int offset = 0; offset < verticalBlend; offset++)
            {
                int bottom = (rect.yMin + offset) * textureWidth + x;
                int top = (rect.yMax - 1 - offset) * textureWidth + x;
                float amount = EdgeBlendAmount(offset, verticalBlend);
                Color32 average = Average(pixels[bottom], pixels[top]);
                pixels[bottom] = Lerp(pixels[bottom], average, amount);
                pixels[top] = Lerp(pixels[top], average, amount);
            }
        }

        internal static int CalculateSeamBlendPixels(int dimension, float blendFraction)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            float clampedFraction = Mathf.Clamp(blendFraction, 0.01f, 0.5f);
            return Mathf.Clamp(Mathf.FloorToInt(dimension * clampedFraction),
                1, Mathf.Max(1, dimension / 2));
        }

        internal static void RemoveBrightnessGradient(Color32[] pixels, int textureWidth, RectInt rect)
        {
            double totalWeight = 0d;
            double meanX = 0d, meanY = 0d, meanLuminance = 0d;
            for (int y = 0; y < rect.height; y++)
            for (int x = 0; x < rect.width; x++)
            {
                Color32 pixel = pixels[(rect.y + y) * textureWidth + rect.x + x];
                double weight = pixel.a / 255d;
                double normalizedX = rect.width > 1 ? x / (double)(rect.width - 1) : 0.5d;
                double normalizedY = rect.height > 1 ? y / (double)(rect.height - 1) : 0.5d;
                totalWeight += weight;
                meanX += normalizedX * weight;
                meanY += normalizedY * weight;
                meanLuminance += Luminance(pixel) * weight;
            }
            if (totalWeight <= 1e-8d) return;
            meanX /= totalWeight;
            meanY /= totalWeight;
            meanLuminance /= totalWeight;

            double covarianceX = 0d, covarianceY = 0d;
            double varianceX = 0d, varianceY = 0d;
            for (int y = 0; y < rect.height; y++)
            for (int x = 0; x < rect.width; x++)
            {
                Color32 pixel = pixels[(rect.y + y) * textureWidth + rect.x + x];
                double weight = pixel.a / 255d;
                double dx = (rect.width > 1 ? x / (double)(rect.width - 1) : 0.5d) - meanX;
                double dy = (rect.height > 1 ? y / (double)(rect.height - 1) : 0.5d) - meanY;
                double luminanceDelta = Luminance(pixel) - meanLuminance;
                covarianceX += weight * dx * luminanceDelta;
                covarianceY += weight * dy * luminanceDelta;
                varianceX += weight * dx * dx;
                varianceY += weight * dy * dy;
            }
            double slopeX = varianceX > 1e-8d ? covarianceX / varianceX : 0d;
            double slopeY = varianceY > 1e-8d ? covarianceY / varianceY : 0d;

            for (int y = 0; y < rect.height; y++)
            for (int x = 0; x < rect.width; x++)
            {
                int pixelIndex = (rect.y + y) * textureWidth + rect.x + x;
                Color32 pixel = pixels[pixelIndex];
                if (pixel.a == 0) continue;
                double dx = (rect.width > 1 ? x / (double)(rect.width - 1) : 0.5d) - meanX;
                double dy = (rect.height > 1 ? y / (double)(rect.height - 1) : 0.5d) - meanY;
                int adjustment = Mathf.RoundToInt((float)(-(slopeX * dx + slopeY * dy) * 255d));
                pixel.r = ClampByte(pixel.r + adjustment);
                pixel.g = ClampByte(pixel.g + adjustment);
                pixel.b = ClampByte(pixel.b + adjustment);
                pixels[pixelIndex] = pixel;
            }
        }

        private static float EdgeBlendAmount(int offset, int blendWidth)
            => blendWidth <= 1 ? 1f : 1f - offset / (float)(blendWidth - 1);

        private static double Luminance(Color32 color)
            => (0.2126d * color.r + 0.7152d * color.g + 0.0722d * color.b) / 255d;

        private static byte ClampByte(int value) => (byte)Mathf.Clamp(value, 0, 255);

        private static Color32 Average(Color32 a, Color32 b)
            => new Color32((byte)((a.r + b.r) / 2), (byte)((a.g + b.g) / 2),
                (byte)((a.b + b.b) / 2), (byte)((a.a + b.a) / 2));

        private static Color32 Lerp(Color32 from, Color32 to, float amount)
            => new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(from.r, to.r, amount)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(from.g, to.g, amount)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(from.b, to.b, amount)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(from.a, to.a, amount)));
    }

    public sealed class SpriteGridOptionsWindow : EditorWindow
    {
        private const string MenuPath = "Assets/UMA/Set Sprite Grid Options...";
        private const float ControlColumnWidth = 410f;
        [SerializeField] private int columns = 4;
        [SerializeField] private int rows = 4;
        [SerializeField] private int inset = 1;
        [SerializeField] private bool editSpritesIndividually;
        [SerializeField] private int editedSpriteIndex;
        [SerializeField] private int insetX1 = 1;
        [SerializeField] private int insetY1 = 1;
        [SerializeField] private int insetX2 = 1;
        [SerializeField] private int insetY2 = 1;
        [SerializeField] private int horizontalOffset;
        [SerializeField] private int verticalOffset;
        [SerializeField] private SpriteGridSpriteSettings globalSettings;
        [SerializeField] private List<SpriteGridSpriteSettings> spriteSettings =
            new List<SpriteGridSpriteSettings>();
        [SerializeField] private bool makeTileable;
        [SerializeField, Range(0.01f, 0.5f)] private float seamBlendFraction = 0.25f;
        [SerializeField] private bool fixBrightnessGradient;
        [SerializeField] private bool advancedTileFixesExpanded;
        [SerializeField] private bool removePolynomialGradient;
        [SerializeField] private bool applyClahe;
        [SerializeField, Range(0f, 1f)] private float claheStrength = 0.5f;
        [SerializeField] private bool applyBilateralFilter;
        [SerializeField, Range(0f, 1f)] private float bilateralStrength = 0.5f;
        [SerializeField] private bool processInLinearSpace;
        [SerializeField, Range(0f, 1f)] private float normalizationStrength;
        [SerializeField] private bool poissonSeamlessBlend;
        [SerializeField] private bool antiGridExpanded;
        [SerializeField] private bool applyBlueNoiseDithering;
        [SerializeField, Range(0f, 1f)] private float blueNoiseStrength = 0.25f;
        [SerializeField] private bool applyMultiOctaveNoise;
        [SerializeField, Range(0f, 1f)] private float noiseStrength = 0.25f;
        [SerializeField, Range(0f, 1f)] private float noiseFrequency = 0.5f;
        [SerializeField] private bool applyMicroWarping;
        [SerializeField, Range(0f, 1f)] private float warpStrength = 0.25f;
        [SerializeField, Range(0f, 1f)] private float warpFrequency = 0.5f;
        [SerializeField] private bool applyFrequencyScrambling;
        [SerializeField, Range(0f, 1f)] private float scrambleStrength = 0.25f;
        [SerializeField] private string[] texturePaths = Array.Empty<string>();
        [SerializeField] private Texture2D copyFromSpriteSheet;
        [SerializeField] private string copyStatus;
        [SerializeField] private int previewTextureIndex;
        [SerializeField] private int previewSpriteIndex;
        [SerializeField] private Vector2 scrollPosition;
        private Texture2D previewTexture;
        private bool previewDirty = true;
        private string previewError;

        [MenuItem(MenuPath, false, 2100)]
        private static void Open()
        {
            string[] paths = GetSelectedTexturePaths();
            if (paths.Length == 0)
            {
                EditorUtility.DisplayDialog("Set Sprite Grid Options",
                    "Select one or more texture or sprite assets first.", "OK");
                return;
            }
            SpriteGridOptionsWindow window = CreateInstance<SpriteGridOptionsWindow>();
            window.titleContent = new GUIContent("Sprite Grid Options");
            window.texturePaths = paths;
            window.minSize = new Vector2(780f, 480f);
            window.maxSize = new Vector2(8192f, 8192f);
            window.ShowUtility();
            window.position = new Rect(window.position.x, window.position.y, 1080f, 740f);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateOpen() => GetSelectedTexturePaths().Length > 0;

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(ControlColumnWidth),
                GUILayout.ExpandHeight(true));
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition,
                GUILayout.Width(ControlColumnWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Set Sprite Grid Options", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Configure {texturePaths.Length} selected texture{(texturePaths.Length == 1 ? string.Empty : "s")} " +
                "as equally divided sprite sheets. Rows are named from the top-left, moving left-to-right.",
                MessageType.Info);

            EditorGUILayout.LabelField("Copy Existing Setup", EditorStyles.boldLabel);
            copyFromSpriteSheet = (Texture2D)EditorGUILayout.ObjectField(
                "Source Sprite Sheet", copyFromSpriteSheet, typeof(Texture2D), false);
            using (new EditorGUI.DisabledScope(copyFromSpriteSheet == null))
                if (GUILayout.Button("Copy from this sprite sheet"))
                    CopyFromSpriteSheet();
            if (!string.IsNullOrEmpty(copyStatus))
                EditorGUILayout.HelpBox(copyStatus, MessageType.Info);

            EditorGUILayout.Space(6f);
            int previousSpriteCount = Mathf.Max(1, columns * rows);
            columns = Mathf.Max(1, EditorGUILayout.IntField("Number of sprite columns", columns));
            rows = Mathf.Max(1, EditorGUILayout.IntField("Number of sprite rows", rows));
            string insetLabel = editSpritesIndividually
                ? "Initial Inset (All Edges)"
                : "Inset (All Edges)";
            inset = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent(insetLabel,
                editSpritesIndividually
                    ? "Starting inset for new sprite profiles. Adjust X1, Y1, X2, and Y2 below for the selected sprite."
                    : "Pixels removed from every side of each cell for both slicing and optional fixes."), inset));

            int spriteCount = Mathf.Max(1, columns * rows);
            if (editSpritesIndividually && spriteCount != previousSpriteCount)
            {
                // Capture before resizing. CaptureCurrentSpriteSettings uses the new grid count,
                // which could otherwise clamp the selection and overwrite a different profile.
                if (spriteSettings != null &&
                    (uint)editedSpriteIndex < (uint)spriteSettings.Count)
                    spriteSettings[editedSpriteIndex] = CaptureVisibleSettings();
                EnsureSpriteSettings(spriteCount, BuildOptions());
                editedSpriteIndex = Mathf.Clamp(editedSpriteIndex, 0, spriteCount - 1);
                previewSpriteIndex = editedSpriteIndex;
                LoadSpriteSettings(editedSpriteIndex);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Adjustment Scope", EditorStyles.boldLabel);
            bool nextIndividualMode = GUILayout.Toolbar(editSpritesIndividually ? 1 : 0,
                new[] { "All Sprites", "Individual Sprites" }) == 1;
            if (nextIndividualMode != editSpritesIndividually)
                SetIndividualMode(nextIndividualMode, spriteCount);
            if (editSpritesIndividually)
            {
                EnsureSpriteSettings(spriteCount, BuildOptions());
                int nextSprite = EditorGUILayout.Popup("Editing Sprite", editedSpriteIndex,
                    BuildSpriteNames(spriteCount));
                if (nextSprite != editedSpriteIndex)
                {
                    CaptureCurrentSpriteSettings();
                    editedSpriteIndex = nextSprite;
                    previewSpriteIndex = nextSprite;
                    LoadSpriteSettings(nextSprite);
                }
                EditorGUILayout.HelpBox(
                    "This sprite retains its edge insets, offsets, and tile-fix settings when you select another sprite.",
                    MessageType.None);
                EditorGUILayout.LabelField("Per-Sprite Insets", EditorStyles.miniBoldLabel);
                insetX1 = Mathf.Max(0, EditorGUILayout.IntField("X1 (Left)", insetX1));
                insetY1 = Mathf.Max(0, EditorGUILayout.IntField("Y1 (Bottom)", insetY1));
                insetX2 = Mathf.Max(0, EditorGUILayout.IntField("X2 (Right)", insetX2));
                insetY2 = Mathf.Max(0, EditorGUILayout.IntField("Y2 (Top)", insetY2));
            }
            EditorGUILayout.LabelField(editSpritesIndividually ? "Per-Sprite Offset" : "Sheet Offset",
                EditorStyles.miniBoldLabel);
            horizontalOffset = EditorGUILayout.IntField("Horizontal Offset", horizontalOffset);
            verticalOffset = EditorGUILayout.IntField("Vertical Offset", verticalOffset);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Tile fixes", EditorStyles.boldLabel);
            makeTileable = EditorGUILayout.ToggleLeft("Make seamlessly tileable", makeTileable);
            using (new EditorGUI.DisabledScope(!makeTileable))
                seamBlendFraction = EditorGUILayout.Slider(new GUIContent("Seam Blend Area (%)",
                    "How far the seamless correction reaches inward from each edge. Smaller values preserve more of the sprite center."),
                    seamBlendFraction * 100f, 1f, 50f) / 100f;
            fixBrightnessGradient = EditorGUILayout.ToggleLeft("Fix tile brightness gradient", fixBrightnessGradient);
            advancedTileFixesExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
                advancedTileFixesExpanded, "Advanced Tile Fixes");
            if (advancedTileFixesExpanded)
            {
                EditorGUI.indentLevel++;
                removePolynomialGradient = EditorGUILayout.ToggleLeft(
                    "Polynomial Gradient Removal (2nd-order)", removePolynomialGradient);
                applyClahe = EditorGUILayout.ToggleLeft("Local Contrast Equalization (CLAHE-style)", applyClahe);
                using (new EditorGUI.DisabledScope(!applyClahe))
                    claheStrength = EditorGUILayout.Slider("CLAHE Strength", claheStrength, 0f, 1f);
                applyBilateralFilter = EditorGUILayout.ToggleLeft("Bilateral Filter Smoothing", applyBilateralFilter);
                using (new EditorGUI.DisabledScope(!applyBilateralFilter))
                    bilateralStrength = EditorGUILayout.Slider("Bilateral Strength", bilateralStrength, 0f, 1f);
                processInLinearSpace = EditorGUILayout.ToggleLeft("Process in Linear Space", processInLinearSpace);
                normalizationStrength = EditorGUILayout.Slider("Normalization Strength",
                    normalizationStrength, 0f, 1f);
                poissonSeamlessBlend = EditorGUILayout.ToggleLeft(
                    "Poisson Seamless Blend (slow)", poissonSeamlessBlend);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            antiGridExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
                antiGridExpanded, "Anti-Grid Artifact Reduction");
            if (antiGridExpanded)
            {
                EditorGUI.indentLevel++;
                applyBlueNoiseDithering = EditorGUILayout.ToggleLeft(
                    "Apply Blue-Noise Dithering", applyBlueNoiseDithering);
                using (new EditorGUI.DisabledScope(!applyBlueNoiseDithering))
                    blueNoiseStrength = EditorGUILayout.Slider("Blue-Noise Strength",
                        blueNoiseStrength, 0f, 1f);

                applyMultiOctaveNoise = EditorGUILayout.ToggleLeft(
                    "Apply Multi-Octave Noise Perturbation", applyMultiOctaveNoise);
                using (new EditorGUI.DisabledScope(!applyMultiOctaveNoise))
                {
                    noiseStrength = EditorGUILayout.Slider("Noise Strength", noiseStrength, 0f, 1f);
                    noiseFrequency = EditorGUILayout.Slider("Noise Frequency", noiseFrequency, 0f, 1f);
                }

                applyMicroWarping = EditorGUILayout.ToggleLeft("Apply Micro-Warping", applyMicroWarping);
                using (new EditorGUI.DisabledScope(!applyMicroWarping))
                {
                    warpStrength = EditorGUILayout.Slider("Warp Strength", warpStrength, 0f, 1f);
                    warpFrequency = EditorGUILayout.Slider("Warp Frequency", warpFrequency, 0f, 1f);
                }

                applyFrequencyScrambling = EditorGUILayout.ToggleLeft(
                    "Apply Frequency-Domain Scrambling (slow)", applyFrequencyScrambling);
                using (new EditorGUI.DisabledScope(!applyFrequencyScrambling))
                    scrambleStrength = EditorGUILayout.Slider("Scramble Strength",
                        scrambleStrength, 0f, 1f);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (editSpritesIndividually) CaptureCurrentSpriteSettings();
            else globalSettings = CaptureVisibleSettings();
            SpriteGridOptions options = BuildOptions();

            if (BuildSpriteOptions().Any(item => item.HasPixelFixes))
                EditorGUILayout.HelpBox("Tile fixes rewrite the selected source image files. " +
                    "Only the inset sprite area inside each grid cell is changed.", MessageType.Warning);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90f))) Close();
            if (GUILayout.Button("Apply", GUILayout.Width(90f))) Apply();
            GUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            GUILayout.Space(6f);
            DrawPreviewControls(options);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                previewDirty = true;
                Repaint();
            }
        }

        private void Apply()
        {
            if (editSpritesIndividually) CaptureCurrentSpriteSettings();
            SpriteGridOptions options = BuildOptions();
            List<SpriteGridOptions> optionsBySprite = BuildSpriteOptions();
            SpriteGridSavedConfiguration savedConfiguration = BuildSavedConfiguration(optionsBySprite);
            bool hasPixelFixes = optionsBySprite.Any(item => item.HasPixelFixes);
            if (hasPixelFixes && !EditorUtility.DisplayDialog(
                    "Rewrite Selected Sprite Sheets?",
                    "The optional tile fixes modify the source image pixels. Sprite slicing alone only changes import settings.",
                    "Apply", "Cancel"))
                return;

            var failures = new List<string>();
            int completed = 0;
            try
            {
                for (int i = 0; i < texturePaths.Length; i++)
                {
                    string path = texturePaths[i];
                    EditorUtility.DisplayProgressBar("Set Sprite Grid Options", path,
                        i / (float)Mathf.Max(1, texturePaths.Length));
                    try
                    {
                        ConfigureTexture(path, options, optionsBySprite, savedConfiguration);
                        completed++;
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{path}: {exception.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            if (failures.Count == 0)
            {
                EditorUtility.DisplayDialog("Set Sprite Grid Options",
                    $"Configured {completed} sprite sheet{(completed == 1 ? string.Empty : "s")}.", "OK");
                Close();
            }
            else
            {
                Debug.LogError("Set Sprite Grid Options failed for some assets:\n" + string.Join("\n", failures));
                EditorUtility.DisplayDialog("Set Sprite Grid Options",
                    $"Configured {completed} asset(s). {failures.Count} failed. See the Console for details.", "OK");
            }
        }

        private SpriteGridOptions BuildOptions()
            => new SpriteGridOptions(columns, rows, inset, makeTileable, fixBrightnessGradient,
                removePolynomialGradient, applyClahe, claheStrength, applyBilateralFilter,
                bilateralStrength, processInLinearSpace, normalizationStrength, poissonSeamlessBlend,
                applyBlueNoiseDithering, blueNoiseStrength, applyMultiOctaveNoise, noiseStrength,
                noiseFrequency, applyMicroWarping, warpStrength, warpFrequency,
                applyFrequencyScrambling, scrambleStrength, horizontalOffset, verticalOffset,
                editSpritesIndividually ? insetX1 : inset,
                editSpritesIndividually ? insetY1 : inset,
                editSpritesIndividually ? insetX2 : inset,
                editSpritesIndividually ? insetY2 : inset,
                seamBlendFraction);

        private List<SpriteGridOptions> BuildSpriteOptions()
        {
            int count = Mathf.Max(1, columns * rows);
            var result = new List<SpriteGridOptions>(count);
            if (!editSpritesIndividually)
            {
                SpriteGridOptions options = BuildOptions();
                for (int i = 0; i < count; i++) result.Add(options);
                return result;
            }

            EnsureSpriteSettings(count, BuildOptions());
            for (int i = 0; i < count; i++)
                result.Add(spriteSettings[i].ToOptions(columns, rows, inset));
            return result;
        }

        private SpriteGridSavedConfiguration BuildSavedConfiguration(
            IReadOnlyList<SpriteGridOptions> optionsBySprite)
        {
            var configuration = new SpriteGridSavedConfiguration
            {
                columns = columns,
                rows = rows,
                inset = inset,
                editSpritesIndividually = editSpritesIndividually,
                globalSettings = globalSettings == null
                    ? new SpriteGridSpriteSettings(BuildOptions())
                    : new SpriteGridSpriteSettings(globalSettings.ToOptions(columns, rows, inset))
            };
            foreach (SpriteGridOptions options in optionsBySprite)
                configuration.spriteSettings.Add(new SpriteGridSpriteSettings(options));
            return configuration;
        }

        private void CopyFromSpriteSheet()
        {
            copyStatus = null;
            string path = AssetDatabase.GetAssetPath(copyFromSpriteSheet);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                copyStatus = "The selected source does not use a TextureImporter.";
                return;
            }

            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            bool hasSavedSettings = SpriteGridConfigurationStore.TryRead(importer,
                out SpriteGridSavedConfiguration configuration);
            if (!hasSavedSettings)
            {
                var factories = new SpriteDataProviderFactories();
                factories.Init();
                ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                if (provider == null)
                {
                    copyStatus = "The selected source has no readable sprite setup.";
                    return;
                }
                provider.InitSpriteEditorDataProvider();
                SpriteRect[] spriteRects = provider.GetSpriteRects() ?? Array.Empty<SpriteRect>();
                RectInt[] rects = spriteRects.Select(item => new RectInt(
                    Mathf.RoundToInt(item.rect.x), Mathf.RoundToInt(item.rect.y),
                    Mathf.RoundToInt(item.rect.width), Mathf.RoundToInt(item.rect.height))).ToArray();
                if (!SpriteGridProcessor.TryBuildConfigurationFromRects(sourceWidth, sourceHeight,
                        rects, columns, rows, out configuration))
                {
                    copyStatus = "The source has no saved Overlay Painter setup, and its existing " +
                        "sprite rectangles could not be mapped to a regular grid.";
                    return;
                }
            }

            ApplyCopiedConfiguration(configuration);
            bool dimensionsDiffer = texturePaths.Any(targetPath =>
            {
                TextureImporter targetImporter = AssetImporter.GetAtPath(targetPath) as TextureImporter;
                if (targetImporter == null) return false;
                targetImporter.GetSourceTextureWidthAndHeight(out int width, out int height);
                return width != sourceWidth || height != sourceHeight;
            });
            copyStatus = hasSavedSettings
                ? $"Copied all {configuration.spriteSettings.Count} sprite profiles and settings."
                : $"Copied {configuration.spriteSettings.Count} sprite areas from legacy Unity slicing. " +
                    "No saved tile-fix settings were available.";
            if (dimensionsDiffer)
                copyStatus += " One or more target sheets have different dimensions; verify the preview before applying.";
        }

        private void ApplyCopiedConfiguration(SpriteGridSavedConfiguration source)
        {
            SpriteGridSavedConfiguration configuration = source.Clone();
            columns = Mathf.Max(1, configuration.columns);
            rows = Mathf.Max(1, configuration.rows);
            inset = Mathf.Max(0, configuration.inset);
            globalSettings = configuration.globalSettings;
            spriteSettings = configuration.spriteSettings;
            editSpritesIndividually = configuration.editSpritesIndividually;
            editedSpriteIndex = Mathf.Clamp(editedSpriteIndex, 0, spriteSettings.Count - 1);
            previewSpriteIndex = editedSpriteIndex;
            ApplyVisibleSettings(editSpritesIndividually
                ? spriteSettings[editedSpriteIndex]
                : globalSettings);
            previewDirty = true;
            Repaint();
        }

        private void SetIndividualMode(bool enabled, int spriteCount)
        {
            if (enabled == editSpritesIndividually) return;
            if (enabled)
            {
                globalSettings = CaptureVisibleSettings();
                EnsureSpriteSettings(spriteCount, BuildOptions());
                editSpritesIndividually = true;
                editedSpriteIndex = Mathf.Clamp(previewSpriteIndex, 0, spriteCount - 1);
                previewSpriteIndex = editedSpriteIndex;
                LoadSpriteSettings(editedSpriteIndex);
            }
            else
            {
                CaptureCurrentSpriteSettings();
                editSpritesIndividually = false;
                ApplyVisibleSettings(globalSettings ?? new SpriteGridSpriteSettings(BuildOptions()));
            }
            previewDirty = true;
        }

        private void EnsureSpriteSettings(int count, SpriteGridOptions defaults)
        {
            spriteSettings ??= new List<SpriteGridSpriteSettings>();
            while (spriteSettings.Count < count)
                spriteSettings.Add(new SpriteGridSpriteSettings(defaults));
            if (spriteSettings.Count > count)
                spriteSettings.RemoveRange(count, spriteSettings.Count - count);
            for (int i = 0; i < spriteSettings.Count; i++)
                spriteSettings[i] ??= new SpriteGridSpriteSettings(defaults);
        }

        private void CaptureCurrentSpriteSettings()
        {
            if (!editSpritesIndividually) return;
            int count = Mathf.Max(1, columns * rows);
            EnsureSpriteSettings(count, BuildOptions());
            editedSpriteIndex = Mathf.Clamp(editedSpriteIndex, 0, count - 1);
            spriteSettings[editedSpriteIndex] = CaptureVisibleSettings();
        }

        private SpriteGridSpriteSettings CaptureVisibleSettings()
            => new SpriteGridSpriteSettings(BuildOptions());

        private void LoadSpriteSettings(int index)
        {
            if ((uint)index >= (uint)spriteSettings.Count) return;
            ApplyVisibleSettings(spriteSettings[index]);
        }

        private void ApplyVisibleSettings(SpriteGridSpriteSettings settings)
        {
            if (settings == null) return;
            insetX1 = settings.insetX1;
            insetY1 = settings.insetY1;
            insetX2 = settings.insetX2;
            insetY2 = settings.insetY2;
            horizontalOffset = settings.horizontalOffset;
            verticalOffset = settings.verticalOffset;
            makeTileable = settings.makeTileable;
            seamBlendFraction = settings.seamBlendFraction <= 0f
                ? 0.25f
                : Mathf.Clamp(settings.seamBlendFraction, 0.01f, 0.5f);
            fixBrightnessGradient = settings.fixBrightnessGradient;
            removePolynomialGradient = settings.removePolynomialGradient;
            applyClahe = settings.applyClahe;
            claheStrength = settings.claheStrength;
            applyBilateralFilter = settings.applyBilateralFilter;
            bilateralStrength = settings.bilateralStrength;
            processInLinearSpace = settings.processInLinearSpace;
            normalizationStrength = settings.normalizationStrength;
            poissonSeamlessBlend = settings.poissonSeamlessBlend;
            applyBlueNoiseDithering = settings.applyBlueNoiseDithering;
            blueNoiseStrength = settings.blueNoiseStrength;
            applyMultiOctaveNoise = settings.applyMultiOctaveNoise;
            noiseStrength = settings.noiseStrength;
            noiseFrequency = settings.noiseFrequency;
            applyMicroWarping = settings.applyMicroWarping;
            warpStrength = settings.warpStrength;
            warpFrequency = settings.warpFrequency;
            applyFrequencyScrambling = settings.applyFrequencyScrambling;
            scrambleStrength = settings.scrambleStrength;
        }

        private string[] BuildSpriteNames(int spriteCount)
        {
            var names = new string[spriteCount];
            for (int i = 0; i < names.Length; i++)
            {
                int row = i / Mathf.Max(1, columns) + 1;
                int column = i % Mathf.Max(1, columns) + 1;
                names[i] = $"Sprite {i}  (row {row}, column {column})";
            }
            return names;
        }

        private void DrawPreviewControls(SpriteGridOptions options)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Live tile preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Choose a tile to preview it repeated 3 × 3. The source texture is never modified by this preview.",
                MessageType.None);

            if (texturePaths.Length == 0)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }
            previewTextureIndex = Mathf.Clamp(previewTextureIndex, 0, texturePaths.Length - 1);
            previewTextureIndex = EditorGUILayout.Popup("Preview texture", previewTextureIndex,
                texturePaths.Select(Path.GetFileNameWithoutExtension).ToArray());

            int spriteCount = Mathf.Max(1, columns * rows);
            previewSpriteIndex = editSpritesIndividually
                ? Mathf.Clamp(editedSpriteIndex, 0, spriteCount - 1)
                : Mathf.Clamp(previewSpriteIndex, 0, spriteCount - 1);
            if (!editSpritesIndividually)
                previewSpriteIndex = EditorGUILayout.Popup("Preview sprite", previewSpriteIndex,
                    BuildSpriteNames(spriteCount));
            else
                EditorGUILayout.LabelField("Previewing", BuildSpriteNames(spriteCount)[previewSpriteIndex]);

            if (previewDirty) RebuildPreview(options);
            if (!string.IsNullOrEmpty(previewError))
            {
                EditorGUILayout.HelpBox(previewError, MessageType.Info);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }
            if (previewTexture == null)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }

            Rect availableRect = GUILayoutUtility.GetRect(64f, 64f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            Rect previewRect = BestFitRect(availableRect,
                previewTexture.width / (float)Mathf.Max(1, previewTexture.height));
            DrawThreeByThreePreview(previewRect);
            EditorGUILayout.LabelField("Center outlined — adjacent cells show the wrapped edges.",
                EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
        }

        internal static Rect BestFitRect(Rect available, float aspect)
        {
            const float padding = 8f;
            available.xMin += padding;
            available.xMax -= padding;
            available.yMin += padding;
            available.yMax -= padding;
            if (available.width <= 0f || available.height <= 0f || aspect <= 0f)
                return available;

            float availableAspect = available.width / available.height;
            if (availableAspect > aspect)
            {
                float width = available.height * aspect;
                available.x += (available.width - width) * 0.5f;
                available.width = width;
            }
            else
            {
                float height = available.width / aspect;
                available.y += (available.height - height) * 0.5f;
                available.height = height;
            }
            return available;
        }

        private void RebuildPreview(SpriteGridOptions options)
        {
            previewDirty = false;
            previewError = null;
            ReleasePreviewTexture();
            if (texturePaths.Length == 0) return;

            try
            {
                string path = texturePaths[previewTextureIndex];
                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (source == null) throw new InvalidOperationException("The selected preview texture could not be loaded.");

                List<SpriteGridOptions> optionsBySprite = BuildSpriteOptions();
                RectInt[] rects = SpriteGridProcessor.BuildSpriteRects(source.width, source.height,
                    options.columns, options.rows, options.inset, optionsBySprite);
                previewSpriteIndex = Mathf.Clamp(previewSpriteIndex, 0, rects.Length - 1);
                Color32[] pixels = ReadPreviewPixels(source);
                RectInt previewRect = rects[previewSpriteIndex];
                // All fixes are tile-local except normalization. Restricting the preview to the
                // selected tile keeps slider changes responsive while retaining exact normalized output.
                if (optionsBySprite.Any(item => item.normalizationStrength > 0f))
                    SpriteGridProcessor.ApplyTileFixes(pixels, source.width, rects, optionsBySprite);
                else
                    SpriteGridProcessor.ApplyTileFixes(pixels, source.width, new[] { previewRect },
                        new[] { optionsBySprite[previewSpriteIndex] });
                previewTexture = new Texture2D(previewRect.width, previewRect.height,
                    TextureFormat.RGBA32, false, false)
                {
                    name = "Sprite Grid Options Preview",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                var tilePixels = new Color32[previewRect.width * previewRect.height];
                for (int y = 0; y < previewRect.height; y++)
                {
                    int sourceIndex = (previewRect.y + y) * source.width + previewRect.x;
                    Array.Copy(pixels, sourceIndex, tilePixels, y * previewRect.width, previewRect.width);
                }
                previewTexture.SetPixels32(tilePixels);
                previewTexture.Apply(false, true);
            }
            catch (Exception exception)
            {
                previewError = $"Preview unavailable: {exception.Message}";
            }
        }

        private static Color32[] ReadPreviewPixels(Texture2D source)
        {
            if (source.isReadable) return source.GetPixels32();

            RenderTexture previous = RenderTexture.active;
            RenderTexture copyTarget = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
            try
            {
                Graphics.Blit(source, copyTarget);
                RenderTexture.active = copyTarget;
                copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                copy.Apply(false, false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(copyTarget);
                DestroyImmediate(copy);
            }
        }

        private void DrawThreeByThreePreview(Rect previewRect)
        {
            float cellWidth = previewRect.width / 3f;
            float cellHeight = previewRect.height / 3f;
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                var cell = new Rect(previewRect.x + column * cellWidth,
                    previewRect.y + row * cellHeight, cellWidth, cellHeight);
                GUI.DrawTexture(cell, previewTexture, ScaleMode.StretchToFill, true);
            }

            var center = new Rect(previewRect.x + cellWidth, previewRect.y + cellHeight,
                cellWidth, cellHeight);
            Color outline = EditorGUIUtility.isProSkin
                ? new Color(1f, 0.78f, 0.15f, 0.95f)
                : new Color(0.55f, 0.25f, 0f, 0.95f);
            Handles.DrawSolidRectangleWithOutline(center, Color.clear, outline);
        }

        private void OnDisable() => ReleasePreviewTexture();

        private void ReleasePreviewTexture()
        {
            if (previewTexture == null) return;
            DestroyImmediate(previewTexture);
            previewTexture = null;
        }

        private static void ConfigureTexture(string path, SpriteGridOptions options,
            IReadOnlyList<SpriteGridOptions> optionsBySprite,
            SpriteGridSavedConfiguration savedConfiguration)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Asset does not use a TextureImporter.");
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            RectInt[] rects = SpriteGridProcessor.BuildSpriteRects(sourceWidth, sourceHeight,
                options.columns, options.rows, options.inset, optionsBySprite);

            Undo.RegisterCompleteObjectUndo(importer, "Set Sprite Grid Options");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
            SpriteGridSavedConfiguration persistedConfiguration = savedConfiguration.Clone();
            persistedConfiguration.sourceWidth = sourceWidth;
            persistedConfiguration.sourceHeight = sourceHeight;
            SpriteGridConfigurationStore.Write(importer, persistedConfiguration);
            ClearAutomaticSpriteSlicing(importer);
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("Unity sprite data provider is unavailable.");
            provider.InitSpriteEditorDataProvider();
            SpriteRect[] existing = provider.GetSpriteRects() ?? Array.Empty<SpriteRect>();
            var idsByName = existing.GroupBy(item => item.name)
                .ToDictionary(group => group.Key, group => group.First().spriteID);
            string baseName = Path.GetFileNameWithoutExtension(path);
            var spriteRects = new SpriteRect[rects.Length];
            var nameIdPairs = new SpriteNameFileIdPair[rects.Length];
            for (int i = 0; i < rects.Length; i++)
            {
                string spriteName = $"{baseName}_{i}";
                GUID spriteId = idsByName.TryGetValue(spriteName, out GUID existingId)
                    ? existingId
                    : i < existing.Length ? existing[i].spriteID : GUID.Generate();
                spriteRects[i] = new SpriteRect
                {
                    name = spriteName,
                    rect = new Rect(rects[i].x, rects[i].y, rects[i].width, rects[i].height),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = spriteId
                };
                nameIdPairs[i] = new SpriteNameFileIdPair(spriteName, spriteId);
            }
            provider.SetSpriteRects(spriteRects);
            ISpriteNameFileIdDataProvider nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameProvider?.SetNameFileIdPairs(nameIdPairs);
            provider.Apply();
            importer.SaveAndReimport();

            // Pixel work intentionally runs after the sprite rectangles have been applied. The
            // source file is written only after every selected tile has completed processing.
            if (optionsBySprite.Any(item => item.HasPixelFixes))
            {
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                RewriteTexturePixels(path, importer, rects, optionsBySprite);
            }
        }

        private static void ClearAutomaticSpriteSlicing(TextureImporter importer)
        {
            // Unity's Sprite Editor can persist "Slice On Import" settings in hidden custom
            // metadata. If left in place, the next SaveAndReimport silently replaces the exact
            // rectangles above with the Sprite Editor's old size/offset/padding values.
            var serializedImporter = new SerializedObject(importer);
            SerializedProperty entries = serializedImporter.FindProperty(
                "m_SpriteSheet.m_SpriteCustomMetadata.m_Entries");
            if (entries == null || !entries.isArray) return;
            bool changed = false;
            for (int i = entries.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                SerializedProperty key = entry.FindPropertyRelative("m_Key");
                if (key == null || (key.stringValue != "SpriteEditor.SliceOnImport" &&
                    key.stringValue != "SpriteEditor.SliceSettings")) continue;
                entries.DeleteArrayElementAtIndex(i);
                changed = true;
            }
            if (changed) serializedImporter.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RewriteTexturePixels(string path, TextureImporter importer, RectInt[] rects,
            IReadOnlyList<SpriteGridOptions> optionsBySprite)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".tga")
                throw new NotSupportedException("Tile fixes support PNG, JPG, and TGA source files.");
            if (!AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.UseCachedIfPossible))
                throw new IOException("Asset is read-only or not checked out for editing.");

            bool wasReadable = importer.isReadable;
            bool wasCrunched = importer.crunchedCompression;
            int previousMaxSize = importer.maxTextureSize;
            TextureImporterCompression previousCompression = importer.textureCompression;
            TextureImporterNPOTScale previousNpotScale = importer.npotScale;
            try
            {
                importer.isReadable = true;
                importer.crunchedCompression = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 16384;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();

                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (source == null || !source.isReadable)
                    throw new InvalidOperationException("A readable source texture could not be imported.");
                int requiredWidth = rects.Max(rect => rect.xMax);
                int requiredHeight = rects.Max(rect => rect.yMax);
                if (source.width < requiredWidth || source.height < requiredHeight)
                    throw new InvalidOperationException(
                        "The imported texture is smaller than its source image. Remove platform size overrides before applying tile fixes.");
                Color32[] pixels = source.GetPixels32();
                SpriteGridProcessor.ApplyTileFixes(pixels, source.width, rects, optionsBySprite);
                var writable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
                try
                {
                    writable.SetPixels32(pixels);
                    writable.Apply(false, false);
                    byte[] encoded = extension == ".png" ? writable.EncodeToPNG() :
                        extension == ".tga" ? writable.EncodeToTGA() : writable.EncodeToJPG(95);
                    File.WriteAllBytes(Path.GetFullPath(path), encoded);
                }
                finally
                {
                    DestroyImmediate(writable);
                }
            }
            finally
            {
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = wasReadable;
                    importer.crunchedCompression = wasCrunched;
                    importer.maxTextureSize = previousMaxSize;
                    importer.textureCompression = previousCompression;
                    importer.npotScale = previousNpotScale;
                    importer.SaveAndReimport();
                }
            }
        }

        private static string[] GetSelectedTexturePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is TextureImporter)) continue;
                paths.Add(path);
            }
            return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
