using System;
using System.Collections.Generic;
using UMA.TexturePaint.Editor.TileProcessing.AntiGrid;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    /// <summary>
    /// CPU tile-processing pipeline used by Set Sprite Grid Options. A tile is copied into a
    /// compact float working buffer once, processed without per-pixel allocations, and committed
    /// only after the complete sheet succeeds.
    /// </summary>
    internal static class TileImageProcessor
    {
        private const float RedLuminance = 0.2126f;
        private const float GreenLuminance = 0.7152f;
        private const float BlueLuminance = 0.0722f;
        private const int HistogramBins = 256;
        private const int ClaheRegionsPerAxis = 8;

        internal sealed class TileBuffer
        {
            public readonly RectInt rect;
            public readonly int width;
            public readonly int height;
            public float[] red;
            public float[] green;
            public float[] blue;
            public float[] alpha;

            public int Length => red.Length;

            public TileBuffer(Color32[] source, int sourceWidth, RectInt rect, bool linear)
            {
                this.rect = rect;
                width = rect.width;
                height = rect.height;
                int length = width * height;
                red = new float[length];
                green = new float[length];
                blue = new float[length];
                alpha = new float[length];
                int destination = 0;
                for (int y = 0; y < height; y++)
                {
                    int sourceIndex = (rect.y + y) * sourceWidth + rect.x;
                    for (int x = 0; x < width; x++, sourceIndex++, destination++)
                    {
                        Color32 color = source[sourceIndex];
                        float r = color.r / 255f;
                        float g = color.g / 255f;
                        float b = color.b / 255f;
                        red[destination] = linear ? SrgbToLinear(r) : r;
                        green[destination] = linear ? SrgbToLinear(g) : g;
                        blue[destination] = linear ? SrgbToLinear(b) : b;
                        alpha[destination] = color.a / 255f;
                    }
                }
            }

            public void Write(Color32[] destination, int destinationWidth, bool linear)
            {
                int sourceIndex = 0;
                for (int y = 0; y < height; y++)
                {
                    int destinationIndex = (rect.y + y) * destinationWidth + rect.x;
                    for (int x = 0; x < width; x++, sourceIndex++, destinationIndex++)
                    {
                        float r = Mathf.Clamp01(red[sourceIndex]);
                        float g = Mathf.Clamp01(green[sourceIndex]);
                        float b = Mathf.Clamp01(blue[sourceIndex]);
                        if (linear)
                        {
                            r = LinearToSrgb(r);
                            g = LinearToSrgb(g);
                            b = LinearToSrgb(b);
                        }
                        destination[destinationIndex] = new Color32(
                            ToByte(r), ToByte(g), ToByte(b), ToByte(alpha[sourceIndex]));
                    }
                }
            }
        }

        public static void Process(Color32[] pixels, int textureWidth, RectInt[] rects,
            SpriteGridOptions options)
        {
            var spriteOptions = new SpriteGridOptions[rects?.Length ?? 0];
            for (int i = 0; i < spriteOptions.Length; i++) spriteOptions[i] = options;
            Process(pixels, textureWidth, rects, spriteOptions);
        }

        public static void Process(Color32[] pixels, int textureWidth, RectInt[] rects,
            IReadOnlyList<SpriteGridOptions> spriteOptions)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (textureWidth <= 0 || pixels.Length % textureWidth != 0)
                throw new ArgumentException("Pixel data does not match the texture width.");
            if (rects == null) throw new ArgumentNullException(nameof(rects));
            if (spriteOptions == null) throw new ArgumentNullException(nameof(spriteOptions));
            if (spriteOptions.Count != rects.Length)
                throw new ArgumentException("Every sprite rectangle must have one settings profile.");
            int textureHeight = pixels.Length / textureWidth;
            var tiles = new TileBuffer[rects.Length];

            for (int i = 0; i < rects.Length; i++)
            {
                SpriteGridOptions options = spriteOptions[i];
                ValidateRect(rects[i], textureWidth, textureHeight);
                TileBuffer tile = new TileBuffer(pixels, textureWidth, rects[i],
                    options.processInLinearSpace);
                tiles[i] = tile;

                if (options.fixBrightnessGradient) RemoveFittedGradient(tile, false);
                // The quadratic fit intentionally follows the planar fit, so enabling both first
                // removes the dominant plane and then removes curvature from the residual.
                if (options.removePolynomialGradient) RemoveFittedGradient(tile, true);
                if (options.applyClahe && options.claheStrength > 0f)
                    ApplyClahe(tile, options.claheStrength);
                if (options.applyBilateralFilter && options.bilateralStrength > 0f)
                    ApplyBilateralFilter(tile, options.bilateralStrength);
                if (options.makeTileable)
                    MakeSeamlesslyTileable(tile, options.seamBlendFraction);
                if (options.poissonSeamlessBlend) ApplyPoissonSeamlessBlend(tile);
            }

            NormalizeTileLuminance(tiles, spriteOptions);

            for (int i = 0; i < tiles.Length; i++)
                AntiGridTileProcessor.Process(tiles[i], spriteOptions[i]);

            for (int i = 0; i < tiles.Length; i++)
                tiles[i].Write(pixels, textureWidth, spriteOptions[i].processInLinearSpace);
        }

        internal static float SrgbToLinear(float value)
            => value <= 0.04045f ? value / 12.92f :
                Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);

        internal static float LinearToSrgb(float value)
            => value <= 0.0031308f ? value * 12.92f :
                1.055f * Mathf.Pow(value, 1f / 2.4f) - 0.055f;

        private static void RemoveFittedGradient(TileBuffer tile, bool quadratic)
        {
            int coefficientCount = quadratic ? 6 : 3;
            var normal = new double[coefficientCount, coefficientCount];
            var rightHandSide = new double[coefficientCount];
            var basis = new double[coefficientCount];
            double totalWeight = 0d;

            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                double weight = tile.alpha[index];
                if (weight <= 0d) continue;
                double nx = tile.width > 1 ? x * 2d / (tile.width - 1d) - 1d : 0d;
                double ny = tile.height > 1 ? y * 2d / (tile.height - 1d) - 1d : 0d;
                FillBasis(basis, nx, ny, quadratic);
                double luminance = Luminance(tile, index);
                totalWeight += weight;
                for (int row = 0; row < coefficientCount; row++)
                {
                    rightHandSide[row] += weight * basis[row] * luminance;
                    for (int column = 0; column < coefficientCount; column++)
                        normal[row, column] += weight * basis[row] * basis[column];
                }
            }
            if (totalWeight <= 1e-8d || !TrySolve(normal, rightHandSide, out double[] coefficients)) return;

            double meanFit = 0d;
            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                double weight = tile.alpha[index];
                if (weight <= 0d) continue;
                double nx = tile.width > 1 ? x * 2d / (tile.width - 1d) - 1d : 0d;
                double ny = tile.height > 1 ? y * 2d / (tile.height - 1d) - 1d : 0d;
                FillBasis(basis, nx, ny, quadratic);
                meanFit += weight * Evaluate(coefficients, basis);
            }
            meanFit /= totalWeight;

            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                if (tile.alpha[index] <= 0f) continue;
                double nx = tile.width > 1 ? x * 2d / (tile.width - 1d) - 1d : 0d;
                double ny = tile.height > 1 ? y * 2d / (tile.height - 1d) - 1d : 0d;
                FillBasis(basis, nx, ny, quadratic);
                float adjustment = (float)(meanFit - Evaluate(coefficients, basis));
                AddLuminance(tile, index, adjustment);
            }
        }

        private static void ApplyClahe(TileBuffer tile, float strength)
        {
            int regionsX = Mathf.Min(ClaheRegionsPerAxis, tile.width);
            int regionsY = Mathf.Min(ClaheRegionsPerAxis, tile.height);
            int regionCount = regionsX * regionsY;
            var mappings = new float[regionCount * HistogramBins];
            var histogram = new int[HistogramBins];

            for (int regionY = 0; regionY < regionsY; regionY++)
            for (int regionX = 0; regionX < regionsX; regionX++)
            {
                Array.Clear(histogram, 0, histogram.Length);
                int xMin = regionX * tile.width / regionsX;
                int xMax = (regionX + 1) * tile.width / regionsX;
                int yMin = regionY * tile.height / regionsY;
                int yMax = (regionY + 1) * tile.height / regionsY;
                int samples = 0;
                for (int y = yMin; y < yMax; y++)
                for (int x = xMin; x < xMax; x++)
                {
                    int index = y * tile.width + x;
                    if (tile.alpha[index] <= 0f) continue;
                    histogram[LuminanceBin(Luminance(tile, index))]++;
                    samples++;
                }

                int mappingOffset = (regionY * regionsX + regionX) * HistogramBins;
                if (samples == 0)
                {
                    for (int bin = 0; bin < HistogramBins; bin++)
                        mappings[mappingOffset + bin] = bin / 255f;
                    continue;
                }

                int clipLimit = Mathf.Max(1, Mathf.CeilToInt(samples * 4f / HistogramBins));
                int clipped = 0;
                for (int bin = 0; bin < HistogramBins; bin++)
                {
                    if (histogram[bin] <= clipLimit) continue;
                    clipped += histogram[bin] - clipLimit;
                    histogram[bin] = clipLimit;
                }
                int uniform = clipped / HistogramBins;
                int remainder = clipped % HistogramBins;
                for (int bin = 0; bin < HistogramBins; bin++)
                    histogram[bin] += uniform + (bin < remainder ? 1 : 0);

                int cumulative = 0;
                int first = -1;
                for (int bin = 0; bin < HistogramBins; bin++)
                {
                    cumulative += histogram[bin];
                    if (first < 0 && cumulative > 0) first = cumulative;
                    int denominator = Mathf.Max(1, samples - first);
                    mappings[mappingOffset + bin] = Mathf.Clamp01((cumulative - first) / (float)denominator);
                }
            }

            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                if (tile.alpha[index] <= 0f) continue;
                float regionX = (x + 0.5f) * regionsX / tile.width - 0.5f;
                float regionY = (y + 0.5f) * regionsY / tile.height - 0.5f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(regionX), 0, regionsX - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(regionY), 0, regionsY - 1);
                int x1 = Mathf.Min(x0 + 1, regionsX - 1);
                int y1 = Mathf.Min(y0 + 1, regionsY - 1);
                float tx = Mathf.Clamp01(regionX - Mathf.Floor(regionX));
                float ty = Mathf.Clamp01(regionY - Mathf.Floor(regionY));
                float luminance = Luminance(tile, index);
                int bin = LuminanceBin(luminance);
                float bottom = Mathf.Lerp(mappings[(y0 * regionsX + x0) * HistogramBins + bin],
                    mappings[(y0 * regionsX + x1) * HistogramBins + bin], tx);
                float top = Mathf.Lerp(mappings[(y1 * regionsX + x0) * HistogramBins + bin],
                    mappings[(y1 * regionsX + x1) * HistogramBins + bin], tx);
                float equalized = Mathf.Lerp(bottom, top, ty);
                SetLuminance(tile, index, Mathf.Lerp(luminance, equalized, strength));
            }
        }

        private static void ApplyBilateralFilter(TileBuffer tile, float strength)
        {
            int radius = Mathf.Clamp(Mathf.CeilToInt(Mathf.Lerp(1f, 3f, strength)), 1, 3);
            float spatialSigma = Mathf.Lerp(0.8f, 2.2f, strength);
            float rangeSigma = Mathf.Lerp(0.035f, 0.14f, strength);
            float inverseSpatial = 1f / (2f * spatialSigma * spatialSigma);
            float inverseRange = 1f / (2f * rangeSigma * rangeSigma);
            var outputRed = new float[tile.Length];
            var outputGreen = new float[tile.Length];
            var outputBlue = new float[tile.Length];

            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                if (tile.alpha[index] <= 0f)
                {
                    outputRed[index] = tile.red[index];
                    outputGreen[index] = tile.green[index];
                    outputBlue[index] = tile.blue[index];
                    continue;
                }
                float centerLuminance = Luminance(tile, index);
                float weightSum = 0f, redSum = 0f, greenSum = 0f, blueSum = 0f;
                int minY = Mathf.Max(0, y - radius);
                int maxY = Mathf.Min(tile.height - 1, y + radius);
                int minX = Mathf.Max(0, x - radius);
                int maxX = Mathf.Min(tile.width - 1, x + radius);
                for (int sampleY = minY; sampleY <= maxY; sampleY++)
                for (int sampleX = minX; sampleX <= maxX; sampleX++)
                {
                    int sample = sampleY * tile.width + sampleX;
                    if (tile.alpha[sample] <= 0f) continue;
                    float dx = sampleX - x;
                    float dy = sampleY - y;
                    float luminanceDelta = Luminance(tile, sample) - centerLuminance;
                    float weight = Mathf.Exp(-(dx * dx + dy * dy) * inverseSpatial -
                        luminanceDelta * luminanceDelta * inverseRange);
                    weightSum += weight;
                    redSum += tile.red[sample] * weight;
                    greenSum += tile.green[sample] * weight;
                    blueSum += tile.blue[sample] * weight;
                }
                float inverseWeight = weightSum > 1e-8f ? 1f / weightSum : 1f;
                outputRed[index] = Mathf.Lerp(tile.red[index], redSum * inverseWeight, strength);
                outputGreen[index] = Mathf.Lerp(tile.green[index], greenSum * inverseWeight, strength);
                outputBlue[index] = Mathf.Lerp(tile.blue[index], blueSum * inverseWeight, strength);
            }
            tile.red = outputRed;
            tile.green = outputGreen;
            tile.blue = outputBlue;
        }

        private static void MakeSeamlesslyTileable(TileBuffer tile, float blendFraction = 0.25f)
        {
            int horizontalBlend = SpriteGridProcessor.CalculateSeamBlendPixels(
                tile.width, blendFraction);
            for (int y = 0; y < tile.height; y++)
            for (int offset = 0; offset < horizontalBlend; offset++)
            {
                int left = y * tile.width + offset;
                int right = y * tile.width + tile.width - 1 - offset;
                float amount = EdgeBlendAmount(offset, horizontalBlend);
                BlendPair(tile, left, right, amount, true);
            }

            int verticalBlend = SpriteGridProcessor.CalculateSeamBlendPixels(
                tile.height, blendFraction);
            for (int x = 0; x < tile.width; x++)
            for (int offset = 0; offset < verticalBlend; offset++)
            {
                int bottom = offset * tile.width + x;
                int top = (tile.height - 1 - offset) * tile.width + x;
                float amount = EdgeBlendAmount(offset, verticalBlend);
                BlendPair(tile, bottom, top, amount, true);
            }
        }

        private static void ApplyPoissonSeamlessBlend(TileBuffer tile)
        {
            if (tile.width < 3 || tile.height < 3)
            {
                MakeSeamlesslyTileable(tile);
                return;
            }

            float[] sourceRed = (float[])tile.red.Clone();
            float[] sourceGreen = (float[])tile.green.Clone();
            float[] sourceBlue = (float[])tile.blue.Clone();
            float[] currentRed = (float[])tile.red.Clone();
            float[] currentGreen = (float[])tile.green.Clone();
            float[] currentBlue = (float[])tile.blue.Clone();
            var nextRed = new float[tile.Length];
            var nextGreen = new float[tile.Length];
            var nextBlue = new float[tile.Length];
            SetPeriodicBoundary(currentRed, sourceRed, tile.width, tile.height);
            SetPeriodicBoundary(currentGreen, sourceGreen, tile.width, tile.height);
            SetPeriodicBoundary(currentBlue, sourceBlue, tile.width, tile.height);

            int iterations = Mathf.Clamp(48 + Mathf.Max(tile.width, tile.height) / 4, 48, 160);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                SetPeriodicBoundary(nextRed, sourceRed, tile.width, tile.height);
                SetPeriodicBoundary(nextGreen, sourceGreen, tile.width, tile.height);
                SetPeriodicBoundary(nextBlue, sourceBlue, tile.width, tile.height);
                for (int y = 1; y < tile.height - 1; y++)
                for (int x = 1; x < tile.width - 1; x++)
                {
                    int index = y * tile.width + x;
                    nextRed[index] = PoissonStep(currentRed, sourceRed, index, tile.width);
                    nextGreen[index] = PoissonStep(currentGreen, sourceGreen, index, tile.width);
                    nextBlue[index] = PoissonStep(currentBlue, sourceBlue, index, tile.width);
                }
                Swap(ref currentRed, ref nextRed);
                Swap(ref currentGreen, ref nextGreen);
                Swap(ref currentBlue, ref nextBlue);
            }
            tile.red = currentRed;
            tile.green = currentGreen;
            tile.blue = currentBlue;
            MatchAlphaEdges(tile.alpha, tile.width, tile.height);
        }

        private static void NormalizeTileLuminance(TileBuffer[] tiles, float strength)
        {
            var strengths = new float[tiles.Length];
            for (int i = 0; i < strengths.Length; i++) strengths[i] = strength;
            NormalizeTileLuminance(tiles, strengths);
        }

        private static void NormalizeTileLuminance(TileBuffer[] tiles,
            IReadOnlyList<SpriteGridOptions> options)
        {
            var strengths = new float[tiles.Length];
            bool any = false;
            for (int i = 0; i < strengths.Length; i++)
            {
                strengths[i] = Mathf.Clamp01(options[i].normalizationStrength);
                any |= strengths[i] > 0f;
            }
            if (any) NormalizeTileLuminance(tiles, strengths);
        }

        private static void NormalizeTileLuminance(TileBuffer[] tiles, IReadOnlyList<float> strengths)
        {
            var averages = new float[tiles.Length];
            float global = 0f;
            int validTiles = 0;
            for (int i = 0; i < tiles.Length; i++)
            {
                averages[i] = AverageLuminance(tiles[i]);
                if (averages[i] < 0f) continue;
                global += averages[i];
                validTiles++;
            }
            if (validTiles == 0) return;
            global /= validTiles;

            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
            {
                TileBuffer tile = tiles[tileIndex];
                if (averages[tileIndex] < 0f) continue;
                float adjustment = (global - averages[tileIndex]) * strengths[tileIndex];
                for (int pixel = 0; pixel < tile.Length; pixel++)
                    if (tile.alpha[pixel] > 0f) AddLuminance(tile, pixel, adjustment);
            }
        }

        private static float AverageLuminance(TileBuffer tile)
        {
            double sum = 0d, weight = 0d;
            for (int i = 0; i < tile.Length; i++)
            {
                if (tile.alpha[i] <= 0f) continue;
                sum += Luminance(tile, i) * tile.alpha[i];
                weight += tile.alpha[i];
            }
            return weight > 1e-8d ? (float)(sum / weight) : -1f;
        }

        private static void FillBasis(double[] basis, double x, double y, bool quadratic)
        {
            if (quadratic)
            {
                basis[0] = x * x;
                basis[1] = y * y;
                basis[2] = x * y;
                basis[3] = x;
                basis[4] = y;
                basis[5] = 1d;
            }
            else
            {
                basis[0] = x;
                basis[1] = y;
                basis[2] = 1d;
            }
        }

        private static bool TrySolve(double[,] matrix, double[] vector, out double[] solution)
        {
            int size = vector.Length;
            var augmented = new double[size, size + 1];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                    augmented[row, column] = matrix[row, column];
                augmented[row, size] = vector[row];
            }

            for (int pivot = 0; pivot < size; pivot++)
            {
                int bestRow = pivot;
                double bestValue = Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < size; row++)
                {
                    double value = Math.Abs(augmented[row, pivot]);
                    if (value <= bestValue) continue;
                    bestValue = value;
                    bestRow = row;
                }
                if (bestValue <= 1e-10d)
                {
                    solution = null;
                    return false;
                }
                if (bestRow != pivot)
                    for (int column = pivot; column <= size; column++)
                    {
                        double temporary = augmented[pivot, column];
                        augmented[pivot, column] = augmented[bestRow, column];
                        augmented[bestRow, column] = temporary;
                    }

                double inversePivot = 1d / augmented[pivot, pivot];
                for (int column = pivot; column <= size; column++)
                    augmented[pivot, column] *= inversePivot;
                for (int row = 0; row < size; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    if (Math.Abs(factor) <= 1e-14d) continue;
                    for (int column = pivot; column <= size; column++)
                        augmented[row, column] -= factor * augmented[pivot, column];
                }
            }

            solution = new double[size];
            for (int row = 0; row < size; row++) solution[row] = augmented[row, size];
            return true;
        }

        private static double Evaluate(double[] coefficients, double[] basis)
        {
            double result = 0d;
            for (int i = 0; i < coefficients.Length; i++) result += coefficients[i] * basis[i];
            return result;
        }

        private static float Luminance(TileBuffer tile, int index)
            => tile.red[index] * RedLuminance + tile.green[index] * GreenLuminance +
               tile.blue[index] * BlueLuminance;

        private static int LuminanceBin(float luminance)
            => Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(luminance) * 255f), 0, 255);

        private static void SetLuminance(TileBuffer tile, int index, float target)
        {
            float current = Luminance(tile, index);
            target = Mathf.Clamp01(target);
            if (current > 1e-5f)
            {
                float scale = target / current;
                tile.red[index] = Mathf.Clamp01(tile.red[index] * scale);
                tile.green[index] = Mathf.Clamp01(tile.green[index] * scale);
                tile.blue[index] = Mathf.Clamp01(tile.blue[index] * scale);
            }
            else
            {
                tile.red[index] = target;
                tile.green[index] = target;
                tile.blue[index] = target;
            }
        }

        private static void AddLuminance(TileBuffer tile, int index, float adjustment)
        {
            tile.red[index] = Mathf.Clamp01(tile.red[index] + adjustment);
            tile.green[index] = Mathf.Clamp01(tile.green[index] + adjustment);
            tile.blue[index] = Mathf.Clamp01(tile.blue[index] + adjustment);
        }

        private static void BlendPair(TileBuffer tile, int first, int second, float amount, bool alpha)
        {
            float red = (tile.red[first] + tile.red[second]) * 0.5f;
            float green = (tile.green[first] + tile.green[second]) * 0.5f;
            float blue = (tile.blue[first] + tile.blue[second]) * 0.5f;
            tile.red[first] = Mathf.Lerp(tile.red[first], red, amount);
            tile.red[second] = Mathf.Lerp(tile.red[second], red, amount);
            tile.green[first] = Mathf.Lerp(tile.green[first], green, amount);
            tile.green[second] = Mathf.Lerp(tile.green[second], green, amount);
            tile.blue[first] = Mathf.Lerp(tile.blue[first], blue, amount);
            tile.blue[second] = Mathf.Lerp(tile.blue[second], blue, amount);
            if (!alpha) return;
            float averageAlpha = (tile.alpha[first] + tile.alpha[second]) * 0.5f;
            tile.alpha[first] = Mathf.Lerp(tile.alpha[first], averageAlpha, amount);
            tile.alpha[second] = Mathf.Lerp(tile.alpha[second], averageAlpha, amount);
        }

        private static float EdgeBlendAmount(int offset, int blendWidth)
            => blendWidth <= 1 ? 1f : 1f - offset / (float)(blendWidth - 1);

        private static float PoissonStep(float[] current, float[] source, int index, int width)
        {
            float sourceLaplacian = source[index - 1] + source[index + 1] +
                source[index - width] + source[index + width] - 4f * source[index];
            return Mathf.Clamp01((current[index - 1] + current[index + 1] +
                current[index - width] + current[index + width] - sourceLaplacian) * 0.25f);
        }

        private static void SetPeriodicBoundary(float[] destination, float[] source, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                int left = y * width;
                int right = left + width - 1;
                float average = (source[left] + source[right]) * 0.5f;
                destination[left] = average;
                destination[right] = average;
            }
            for (int x = 0; x < width; x++)
            {
                int bottom = x;
                int top = (height - 1) * width + x;
                float average = (source[bottom] + source[top]) * 0.5f;
                destination[bottom] = average;
                destination[top] = average;
            }
            float corner = (source[0] + source[width - 1] + source[(height - 1) * width] +
                source[height * width - 1]) * 0.25f;
            destination[0] = corner;
            destination[width - 1] = corner;
            destination[(height - 1) * width] = corner;
            destination[height * width - 1] = corner;
        }

        private static void MatchAlphaEdges(float[] alpha, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                int left = y * width;
                int right = left + width - 1;
                alpha[left] = alpha[right] = (alpha[left] + alpha[right]) * 0.5f;
            }
            for (int x = 0; x < width; x++)
            {
                int bottom = x;
                int top = (height - 1) * width + x;
                alpha[bottom] = alpha[top] = (alpha[bottom] + alpha[top]) * 0.5f;
            }
        }

        private static void Swap(ref float[] first, ref float[] second)
        {
            float[] temporary = first;
            first = second;
            second = temporary;
        }

        private static void ValidateRect(RectInt rect, int textureWidth, int textureHeight)
        {
            if (rect.width <= 0 || rect.height <= 0 || rect.xMin < 0 || rect.yMin < 0 ||
                rect.xMax > textureWidth || rect.yMax > textureHeight)
                throw new ArgumentOutOfRangeException(nameof(rect), $"Tile rectangle {rect} is outside the texture.");
        }

        private static byte ToByte(float value)
            => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);
    }
}
