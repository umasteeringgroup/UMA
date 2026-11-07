using UnityEngine;

namespace UMA.TexturePaint.Editor.TileProcessing.AntiGrid
{
    /// <summary>
    /// Post-correction passes that perturb repetitive structure without changing a tile's DC
    /// luminance. Noise and sampling fields are periodic, so the passes do not reopen tile seams.
    /// </summary>
    internal static class AntiGridTileProcessor
    {
        private const int BlueNoiseSize = 64;
        private const int MaximumDctSize = 32;
        private const float RedLuminance = 0.2126f;
        private const float GreenLuminance = 0.7152f;
        private const float BlueLuminance = 0.0722f;
        private static readonly float[] BlueNoise = GenerateBlueNoise();

        public static void Process(TileImageProcessor.TileBuffer tile, SpriteGridOptions options)
        {
            uint seed = TileSeed(tile.rect);
            if (options.applyBlueNoiseDithering && options.blueNoiseStrength > 0f)
                ApplyBlueNoise(tile, options.blueNoiseStrength, seed);
            if (options.applyMultiOctaveNoise && options.noiseStrength > 0f)
                ApplyMultiOctaveNoise(tile, options.noiseStrength, options.noiseFrequency,
                    Mix(seed ^ 0x75B29D31u));
            if (options.applyMicroWarping && options.warpStrength > 0f)
                ApplyMicroWarp(tile, options.warpStrength, options.warpFrequency,
                    Mix(seed ^ 0xC13FA9A9u));
            if (options.applyFrequencyScrambling && options.scrambleStrength > 0f)
                ApplyFrequencyScrambling(tile, options.scrambleStrength,
                    Mix(seed ^ 0x91E10DA5u));
        }

        private static void ApplyBlueNoise(TileImageProcessor.TileBuffer tile, float strength,
            uint seed)
        {
            int offsetX = (int)(seed & (BlueNoiseSize - 1));
            int offsetY = (int)((seed >> 8) & (BlueNoiseSize - 1));
            int cyclesX = Mathf.Max(1, Mathf.RoundToInt((tile.width - 1f) / BlueNoiseSize));
            int cyclesY = Mathf.Max(1, Mathf.RoundToInt((tile.height - 1f) / BlueNoiseSize));
            float amplitude = 0.015f * strength;
            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                if (tile.alpha[index] <= 0f) continue;
                float sampleX = tile.width > 1
                    ? x * BlueNoiseSize * cyclesX / (float)(tile.width - 1) + offsetX
                    : offsetX;
                float sampleY = tile.height > 1
                    ? y * BlueNoiseSize * cyclesY / (float)(tile.height - 1) + offsetY
                    : offsetY;
                AddLuminance(tile, index, SampleBlueNoise(sampleX, sampleY) * amplitude);
            }
        }

        private static void ApplyMultiOctaveNoise(TileImageProcessor.TileBuffer tile,
            float strength, float frequency, uint seed)
        {
            var field = new float[tile.Length];
            int basePeriod = Mathf.RoundToInt(Mathf.Lerp(2f, 14f, frequency));
            double weightedMean = 0d;
            double totalAlpha = 0d;
            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                float value = 0f;
                float weight = 1f;
                float weightSum = 0f;
                int period = basePeriod;
                for (int octave = 0; octave < 3; octave++)
                {
                    value += PeriodicValueNoise(x, y, tile.width, tile.height, period,
                        Mix(seed + (uint)(octave * 0x9E3779B9))) * weight;
                    weightSum += weight;
                    weight *= 0.5f;
                    period = Mathf.Min(64, period * 2);
                }
                value /= weightSum;
                field[index] = value;
                if (tile.alpha[index] <= 0f) continue;
                weightedMean += value * tile.alpha[index];
                totalAlpha += tile.alpha[index];
            }

            float mean = totalAlpha > 1e-8d ? (float)(weightedMean / totalAlpha) : 0f;
            float amplitude = 0.025f * strength;
            for (int i = 0; i < tile.Length; i++)
                if (tile.alpha[i] > 0f) AddLuminance(tile, i, (field[i] - mean) * amplitude);
        }

        private static void ApplyMicroWarp(TileImageProcessor.TileBuffer tile, float strength,
            float frequency, uint seed)
        {
            if (tile.width < 3 || tile.height < 3) return;
            int period = Mathf.RoundToInt(Mathf.Lerp(2f, 12f, frequency));
            float maximumPixels = Mathf.Clamp(Mathf.Min(tile.width, tile.height) * 0.015f, 0.5f, 3f);
            float amplitude = maximumPixels * strength;
            var red = new float[tile.Length];
            var green = new float[tile.Length];
            var blue = new float[tile.Length];
            var alpha = new float[tile.Length];

            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                float offsetX = FractalPeriodicNoise(x, y, tile.width, tile.height, period,
                    seed) * amplitude;
                float offsetY = FractalPeriodicNoise(x, y, tile.width, tile.height, period,
                    Mix(seed ^ 0xA511E9B3u)) * amplitude;
                float sampleX = x + offsetX;
                float sampleY = y + offsetY;
                red[index] = SamplePeriodic(tile.red, tile.width, tile.height, sampleX, sampleY);
                green[index] = SamplePeriodic(tile.green, tile.width, tile.height, sampleX, sampleY);
                blue[index] = SamplePeriodic(tile.blue, tile.width, tile.height, sampleX, sampleY);
                alpha[index] = SamplePeriodic(tile.alpha, tile.width, tile.height, sampleX, sampleY);
            }
            tile.red = red;
            tile.green = green;
            tile.blue = blue;
            tile.alpha = alpha;
        }

        private static void ApplyFrequencyScrambling(TileImageProcessor.TileBuffer tile,
            float strength, uint seed)
        {
            int sampleWidth = Mathf.Min(MaximumDctSize, tile.width);
            int sampleHeight = Mathf.Min(MaximumDctSize, tile.height);
            if (sampleWidth < 2 || sampleHeight < 2) return;
            var samples = new float[sampleWidth * sampleHeight];
            var intermediate = new float[samples.Length];
            var coefficients = new float[samples.Length];
            var reconstructed = new float[samples.Length];
            float[] horizontalBasis = GenerateDctBasis(sampleWidth);
            float[] verticalBasis = GenerateDctBasis(sampleHeight);

            for (int y = 0; y < sampleHeight; y++)
            for (int x = 0; x < sampleWidth; x++)
            {
                float sourceX = x * (tile.width - 1f) / (sampleWidth - 1f);
                float sourceY = y * (tile.height - 1f) / (sampleHeight - 1f);
                samples[y * sampleWidth + x] = SampleLuminance(tile, sourceX, sourceY);
            }

            ForwardDct(samples, intermediate, coefficients, sampleWidth, sampleHeight,
                horizontalBasis, verticalBasis);
            int maximumU = Mathf.Max(1, Mathf.FloorToInt(sampleWidth * 0.3f));
            int maximumV = Mathf.Max(1, Mathf.FloorToInt(sampleHeight * 0.3f));
            for (int v = 0; v < sampleHeight; v++)
            for (int u = 0; u < sampleWidth; u++)
            {
                if (u == 0 && v == 0) continue;
                float normalizedU = u / (float)sampleWidth;
                float normalizedV = v / (float)sampleHeight;
                float radius = Mathf.Sqrt(normalizedU * normalizedU + normalizedV * normalizedV);
                if (u > maximumU || v > maximumV || radius > 0.32f) continue;
                int coefficient = v * sampleWidth + u;
                float random = SignedHash(Mix(seed ^ (uint)(coefficient * 83492791)));
                float lowFrequencyWeight = 1f - radius / 0.32f;
                coefficients[coefficient] *= 1f + random * strength *
                    lowFrequencyWeight * 0.18f;
            }
            InverseDct(coefficients, intermediate, reconstructed, sampleWidth, sampleHeight,
                horizontalBasis, verticalBasis);

            var adjustments = new float[tile.Length];
            double weightedAdjustment = 0d;
            double weightedMask = 0d;
            float edgeFadeDistance = Mathf.Clamp(Mathf.Min(tile.width, tile.height) * 0.04f, 1f, 8f);
            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                float sampleX = x * (sampleWidth - 1f) / (tile.width - 1f);
                float sampleY = y * (sampleHeight - 1f) / (tile.height - 1f);
                float original = SampleGrid(samples, sampleWidth, sampleHeight, sampleX, sampleY);
                float changed = SampleGrid(reconstructed, sampleWidth, sampleHeight, sampleX, sampleY);
                float edgeDistance = Mathf.Min(Mathf.Min(x, tile.width - 1 - x),
                    Mathf.Min(y, tile.height - 1 - y));
                float mask = Mathf.Clamp01(edgeDistance / edgeFadeDistance);
                adjustments[index] = (changed - original) * mask;
                if (tile.alpha[index] <= 0f) continue;
                weightedAdjustment += adjustments[index] * tile.alpha[index];
                weightedMask += mask * tile.alpha[index];
            }

            float meanCorrection = weightedMask > 1e-8d
                ? (float)(weightedAdjustment / weightedMask) : 0f;
            for (int y = 0; y < tile.height; y++)
            for (int x = 0; x < tile.width; x++)
            {
                int index = y * tile.width + x;
                if (tile.alpha[index] <= 0f) continue;
                float edgeDistance = Mathf.Min(Mathf.Min(x, tile.width - 1 - x),
                    Mathf.Min(y, tile.height - 1 - y));
                float mask = Mathf.Clamp01(edgeDistance / edgeFadeDistance);
                AddLuminance(tile, index, adjustments[index] - meanCorrection * mask);
            }
        }

        private static float FractalPeriodicNoise(int x, int y, int width, int height,
            int period, uint seed)
        {
            float first = PeriodicValueNoise(x, y, width, height, period, seed);
            float second = PeriodicValueNoise(x, y, width, height,
                Mathf.Min(64, period * 2), Mix(seed ^ 0x68E31DA4u));
            return (first + second * 0.5f) / 1.5f;
        }

        private static float PeriodicValueNoise(int x, int y, int width, int height,
            int period, uint seed)
        {
            float sampleX = width > 1 ? x * period / (float)(width - 1) : 0f;
            float sampleY = height > 1 ? y * period / (float)(height - 1) : 0f;
            int x0 = Mathf.FloorToInt(sampleX);
            int y0 = Mathf.FloorToInt(sampleY);
            float tx = Smooth(sampleX - x0);
            float ty = Smooth(sampleY - y0);
            int ix0 = PositiveModulo(x0, period);
            int iy0 = PositiveModulo(y0, period);
            int ix1 = (ix0 + 1) % period;
            int iy1 = (iy0 + 1) % period;
            float bottom = Mathf.Lerp(Lattice(ix0, iy0, seed), Lattice(ix1, iy0, seed), tx);
            float top = Mathf.Lerp(Lattice(ix0, iy1, seed), Lattice(ix1, iy1, seed), tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float SamplePeriodic(float[] values, int width, int height, float x, float y)
        {
            float periodX = Mathf.Max(1, width - 1);
            float periodY = Mathf.Max(1, height - 1);
            float wrappedX = Mathf.Repeat(x, periodX);
            float wrappedY = Mathf.Repeat(y, periodY);
            int x0 = Mathf.FloorToInt(wrappedX);
            int y0 = Mathf.FloorToInt(wrappedY);
            int x1 = (x0 + 1) % Mathf.Max(1, width - 1);
            int y1 = (y0 + 1) % Mathf.Max(1, height - 1);
            float tx = wrappedX - x0;
            float ty = wrappedY - y0;
            float bottom = Mathf.Lerp(values[y0 * width + x0], values[y0 * width + x1], tx);
            float top = Mathf.Lerp(values[y1 * width + x0], values[y1 * width + x1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float SampleBlueNoise(float x, float y)
        {
            int x0 = PositiveModulo(Mathf.FloorToInt(x), BlueNoiseSize);
            int y0 = PositiveModulo(Mathf.FloorToInt(y), BlueNoiseSize);
            int x1 = (x0 + 1) & (BlueNoiseSize - 1);
            int y1 = (y0 + 1) & (BlueNoiseSize - 1);
            float tx = x - Mathf.Floor(x);
            float ty = y - Mathf.Floor(y);
            float bottom = Mathf.Lerp(BlueNoise[y0 * BlueNoiseSize + x0],
                BlueNoise[y0 * BlueNoiseSize + x1], tx);
            float top = Mathf.Lerp(BlueNoise[y1 * BlueNoiseSize + x0],
                BlueNoise[y1 * BlueNoiseSize + x1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float[] GenerateBlueNoise()
        {
            int length = BlueNoiseSize * BlueNoiseSize;
            var white = new float[length];
            var highPass = new float[length];
            for (int i = 0; i < length; i++) white[i] = SignedHash(Mix((uint)i + 0xB5297A4Du));

            float maximum = 0f;
            for (int y = 0; y < BlueNoiseSize; y++)
            for (int x = 0; x < BlueNoiseSize; x++)
            {
                float neighborhood = 0f;
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int sampleX = (x + offsetX) & (BlueNoiseSize - 1);
                    int sampleY = (y + offsetY) & (BlueNoiseSize - 1);
                    neighborhood += white[sampleY * BlueNoiseSize + sampleX];
                }
                int index = y * BlueNoiseSize + x;
                highPass[index] = white[index] - neighborhood / 9f;
                maximum = Mathf.Max(maximum, Mathf.Abs(highPass[index]));
            }
            if (maximum <= 1e-6f) return highPass;
            float inverse = 1f / maximum;
            for (int i = 0; i < length; i++) highPass[i] *= inverse;
            return highPass;
        }

        private static float[] GenerateDctBasis(int size)
        {
            var basis = new float[size * size];
            for (int frequency = 0; frequency < size; frequency++)
            for (int sample = 0; sample < size; sample++)
            {
                float scale = frequency == 0 ? Mathf.Sqrt(1f / size) : Mathf.Sqrt(2f / size);
                basis[frequency * size + sample] = scale *
                    Mathf.Cos((2f * sample + 1f) * frequency * Mathf.PI / (2f * size));
            }
            return basis;
        }

        private static void ForwardDct(float[] samples, float[] intermediate,
            float[] coefficients, int width, int height, float[] horizontalBasis,
            float[] verticalBasis)
        {
            for (int y = 0; y < height; y++)
            for (int u = 0; u < width; u++)
            {
                float sum = 0f;
                for (int x = 0; x < width; x++)
                    sum += samples[y * width + x] * horizontalBasis[u * width + x];
                intermediate[y * width + u] = sum;
            }
            for (int v = 0; v < height; v++)
            for (int u = 0; u < width; u++)
            {
                float sum = 0f;
                for (int y = 0; y < height; y++)
                    sum += intermediate[y * width + u] * verticalBasis[v * height + y];
                coefficients[v * width + u] = sum;
            }
        }

        private static void InverseDct(float[] coefficients, float[] intermediate,
            float[] samples, int width, int height, float[] horizontalBasis,
            float[] verticalBasis)
        {
            for (int y = 0; y < height; y++)
            for (int u = 0; u < width; u++)
            {
                float sum = 0f;
                for (int v = 0; v < height; v++)
                    sum += coefficients[v * width + u] * verticalBasis[v * height + y];
                intermediate[y * width + u] = sum;
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                for (int u = 0; u < width; u++)
                    sum += intermediate[y * width + u] * horizontalBasis[u * width + x];
                samples[y * width + x] = sum;
            }
        }

        private static float SampleLuminance(TileImageProcessor.TileBuffer tile, float x, float y)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, tile.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, tile.height - 1);
            int x1 = Mathf.Min(x0 + 1, tile.width - 1);
            int y1 = Mathf.Min(y0 + 1, tile.height - 1);
            float tx = x - x0;
            float ty = y - y0;
            float bottom = Mathf.Lerp(Luminance(tile, y0 * tile.width + x0),
                Luminance(tile, y0 * tile.width + x1), tx);
            float top = Mathf.Lerp(Luminance(tile, y1 * tile.width + x0),
                Luminance(tile, y1 * tile.width + x1), tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float SampleGrid(float[] values, int width, int height, float x, float y)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, height - 1);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = x - x0;
            float ty = y - y0;
            float bottom = Mathf.Lerp(values[y0 * width + x0], values[y0 * width + x1], tx);
            float top = Mathf.Lerp(values[y1 * width + x0], values[y1 * width + x1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float Lattice(int x, int y, uint seed)
            => SignedHash(Mix(seed ^ (uint)(x * 73856093) ^ (uint)(y * 19349663)));

        private static float Luminance(TileImageProcessor.TileBuffer tile, int index)
            => tile.red[index] * RedLuminance + tile.green[index] * GreenLuminance +
               tile.blue[index] * BlueLuminance;

        private static void AddLuminance(TileImageProcessor.TileBuffer tile, int index,
            float adjustment)
        {
            tile.red[index] = Mathf.Clamp01(tile.red[index] + adjustment);
            tile.green[index] = Mathf.Clamp01(tile.green[index] + adjustment);
            tile.blue[index] = Mathf.Clamp01(tile.blue[index] + adjustment);
        }

        private static float Smooth(float value) => value * value * (3f - 2f * value);

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static uint TileSeed(RectInt rect)
            => Mix((uint)rect.x * 0x8DA6B343u ^ (uint)rect.y * 0xD8163841u ^
                (uint)rect.width * 0xCB1AB31Fu ^ (uint)rect.height * 0x165667B1u);

        private static float SignedHash(uint value)
            => (Mix(value) & 0x00FFFFFFu) / 8388607.5f - 1f;

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ value >> 16;
        }
    }
}
