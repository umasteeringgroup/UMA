using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>
    /// Multi-color procedural noise generator with independent X/Y axis scales and a
    /// direction angle. The noise value drives a 2-4 stop color gradient plus optional
    /// roughness, metallic, AO, Normal Control and Detail Mask responses.
    /// </summary>
    public sealed class NoiseGeneratorPlugin : ScriptableObject,
        ITexturePaintGeneratorV2, ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            NoiseGeneratorEngine.CreateDescriptor();

        public TexturePaintPluginDescriptor Descriptor => descriptor;

        // Write-only procedural generator: only destination dimensions are required.
        public TexturePaintChannelMask ResolveReadChannels(
            TexturePaintPluginParameterSet parameters) => TexturePaintChannelMask.None;

        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            NoiseGeneratorEngine.ExecuteAsync(context);
    }

    internal static class NoiseGeneratorEngine
    {
        private const int RowsPerTile = 128;
        private const string ControlMask = "controlMask";

        private enum NoiseType
        {
            PerlinFbm,
            ValueFbm,
            Ridged,
            Cellular,
            Marble
        }

        public static TexturePaintPluginDescriptor CreateDescriptor() =>
            new TexturePaintPluginDescriptor
            {
                id = "com.uma.texturepaint.noise-generator",
                displayName = "Noise",
                description = "Maps multi-octave procedural noise through a 2-4 color gradient. " +
                              "Independent X/Y axis scales plus a direction angle stretch and " +
                              "orient the noise field across the texture.",
                pluginVersion = "1.0.0",
                capabilities = TexturePaintPluginCapability.Generator |
                               TexturePaintPluginCapability.LongRunning,
                declaredChannels = TexturePaintChannelMask.Albedo |
                                   TexturePaintChannelMask.Roughness |
                                   TexturePaintChannelMask.Metallic |
                                   TexturePaintChannelMask.AmbientOcclusion |
                                   TexturePaintChannelMask.NormalControl |
                                   TexturePaintChannelMask.DetailMask,
                // Dynamic channel usage narrows this write-only generator to dimensions only.
                readChannels = TexturePaintChannelMask.All,
                supportedTargets = TexturePaintPluginTarget.All,
                channelSnapshotMaximumResolution = 4096,
                parameters = Parameters()
            };

        public static Task ExecuteAsync(TexturePaintCommandContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var settings = new Settings(context.parameters);
            if (context.target == TexturePaintPluginTarget.LayerContent && !settings.AnyOutput)
                throw new InvalidOperationException(
                    "Noise requires at least one enabled output channel.");
            TexturePaintReadOnlyParameterTexture mask = context.GetTextureParameter(ControlMask);
            return Task.Run(() => Execute(context, settings, mask), context.cancellationToken);
        }

        private static void Execute(TexturePaintCommandContextV2 context, Settings settings,
            TexturePaintReadOnlyParameterTexture mask)
        {
            int surfaceCount = Math.Max(1, context.source.surfaceIds.Count);
            for (int surfaceIndex = 0;
                 surfaceIndex < context.source.surfaceIds.Count;
                 surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];

                if (context.target == TexturePaintPluginTarget.LayerMask)
                {
                    TexturePaintReadOnlyMask layerMask = context.source.GetMask(surfaceId);
                    if (layerMask == null) continue;
                    GenerateMask(context, settings, mask, surfaceId,
                        layerMask.width, layerMask.height, surfaceIndex, surfaceCount);
                    continue;
                }

                List<OutputTarget> targets = OutputTarget.Find(context.source, surfaceId, settings);
                if (targets.Count == 0) continue;

                var groups = new Dictionary<long, List<OutputTarget>>();
                for (int i = 0; i < targets.Count; i++)
                {
                    OutputTarget target = targets[i];
                    long key = ((long)target.width << 32) | (uint)target.height;
                    if (!groups.TryGetValue(key, out List<OutputTarget> group))
                        groups.Add(key, group = new List<OutputTarget>());
                    group.Add(target);
                }

                foreach (List<OutputTarget> group in groups.Values)
                {
                    int width = group[0].width;
                    int height = group[0].height;
                    TexturePaintChannelMask channels = TexturePaintChannelMask.None;
                    for (int i = 0; i < group.Count; i++)
                        channels |= TexturePaintExportTemplate.ToMask(group[i].channel);

                    for (int y0 = 0; y0 < height; y0 += RowsPerTile)
                    {
                        context.cancellationToken.ThrowIfCancellationRequested();
                        int rows = Math.Min(RowsPerTile, height - y0);
                        OutputBuffers output = Generate(settings, mask, width, height, y0, rows,
                            channels, context);
                        if (!output.any) continue;
                        for (int i = 0; i < group.Count; i++)
                            Write(context, surfaceId, group[i], y0, rows, output);
                        context.progress?.Report((surfaceIndex + (y0 + rows) / (float)height) /
                                                 surfaceCount);
                    }
                }
            }

            context.progress?.Report(1f);
        }

        private static void GenerateMask(TexturePaintCommandContextV2 context, Settings settings,
            TexturePaintReadOnlyParameterTexture mask, string surfaceId,
            int width, int height, int surfaceIndex, int surfaceCount)
        {
            for (int y0 = 0; y0 < height; y0 += RowsPerTile)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                int rows = Math.Min(RowsPerTile, height - y0);
                var pixels = new Color32[width * rows];
                Parallel.For(0, rows, new ParallelOptions
                    { CancellationToken = context.cancellationToken }, localY =>
                {
                    int y = y0 + localY;
                    for (int x = 0; x < width; x++)
                    {
                        float u = (x + 0.5f) / width;
                        float v = (y + 0.5f) / height;
                        float n = SampleNoise(settings, u, v);
                        n *= ControlCoverage(mask, u, v) * settings.opacity;
                        byte b = ToByte(n);
                        pixels[localY * width + x] = new Color32(b, b, b, 255);
                    }
                });
                context.WriteMaskTileCompactOwned(surfaceId, new RectInt(0, y0, width, rows),
                    pixels, TexturePaintPluginBlend.Replace, 1f);
                context.progress?.Report((surfaceIndex + (y0 + rows) / (float)height) /
                                         surfaceCount);
            }
        }

        private static OutputBuffers Generate(Settings s,
            TexturePaintReadOnlyParameterTexture mask, int width, int height,
            int y0, int rows, TexturePaintChannelMask channels,
            TexturePaintCommandContextV2 context)
        {
            var output = new OutputBuffers(width * rows, channels);
            Parallel.For(0, rows, new ParallelOptions
                { CancellationToken = context.cancellationToken }, localY =>
            {
                int y = y0 + localY;
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float n = SampleNoise(s, u, v);
                    float coverage = ControlCoverage(mask, u, v) * s.opacity;
                    int index = localY * width + x;
                    output.Set(index, BuildPixel(s, n, coverage, u, v));
                }
            });
            return output;
        }

        private readonly struct GeneratedPixel
        {
            public readonly Color32 albedo;
            public readonly Color32 roughness;
            public readonly Color32 metallic;
            public readonly Color32 ao;
            public readonly Color32 normalControl;
            public readonly Color32 detailMask;
            public readonly bool hasAlbedo;
            public readonly bool hasRoughness;
            public readonly bool hasMetallic;
            public readonly bool hasAo;
            public readonly bool hasNormal;
            public readonly bool hasDetail;

            public GeneratedPixel(Color32 albedo, Color32 roughness, Color32 metallic,
                Color32 ao, Color32 normalControl, Color32 detailMask,
                bool hasAlbedo, bool hasRoughness, bool hasMetallic,
                bool hasAo, bool hasNormal, bool hasDetail)
            {
                this.albedo = albedo;
                this.roughness = roughness;
                this.metallic = metallic;
                this.ao = ao;
                this.normalControl = normalControl;
                this.detailMask = detailMask;
                this.hasAlbedo = hasAlbedo;
                this.hasRoughness = hasRoughness;
                this.hasMetallic = hasMetallic;
                this.hasAo = hasAo;
                this.hasNormal = hasNormal;
                this.hasDetail = hasDetail;
            }
        }

        private static GeneratedPixel BuildPixel(Settings s, float n, float coverage, float u, float v)
        {
            Color gradient = MapGradient(s, n);
            if (s.colorVariation > 0f)
            {
                float variation = (Hash2(
                    Mathf.FloorToInt(u * 257f), Mathf.FloorToInt(v * 257f), s.seed + 911) - 0.5f) *
                    2f * s.colorVariation;
                gradient = AddRgb(gradient, variation);
            }

            float alpha = Mathf.Clamp01(coverage * (s.alphaFromNoise ? n : 1f));
            var albedo = new Color32(
                ToByte(gradient.r), ToByte(gradient.g), ToByte(gradient.b), ToByte(alpha));

            float roughness = Mathf.Clamp01(s.roughnessBase + (n - 0.5f) * 2f * s.roughnessRange);
            float metallic = Mathf.Clamp01(s.metallicBase + (n - 0.5f) * 2f * s.metallicRange);
            // AO keeps recesses (low noise) darker and exposes peaks toward white.
            float ao = Mathf.Clamp01(1f - (1f - n) * s.aoStrength);
            float normal = Mathf.Clamp01(0.5f + (n - 0.5f) * s.heightStrength);
            float detail = Mathf.Clamp01(n * s.detailStrength);

            return new GeneratedPixel(
                albedo, Gray(roughness, coverage), Gray(metallic, coverage),
                Gray(ao, coverage), Gray(normal, coverage), Gray(detail, coverage),
                s.outputAlbedo, s.outputRoughness, s.outputMetallic,
                s.outputAo, s.outputNormalControl, s.outputDetailMask);
        }

        private static Color MapGradient(Settings s, float t)
        {
            t = Mathf.Clamp01(t);
            if (s.posterizeSteps > 1)
            {
                float steps = s.posterizeSteps;
                t = Mathf.Floor(t * steps + 0.5f) / steps;
                t = Mathf.Clamp01(t);
            }

            int stops = s.colorCount + 1; // enum 0..2 -> 2..4 colors
            float scaled = t * (stops - 1);
            int segment = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, stops - 2);
            float local = Mathf.Clamp01(scaled - segment);
            // Blend softness mixes linear ramps with smoothstep easing at stop boundaries.
            float smooth = local * local * (3f - 2f * local);
            local = Mathf.Lerp(local, smooth, s.colorBlend);

            Color a = StopColor(s, segment);
            Color b = StopColor(s, segment + 1);
            return Color.Lerp(a, b, local);
        }

        private static Color StopColor(Settings s, int index) => index switch
        {
            0 => s.colorA,
            1 => s.colorB,
            2 => s.colorC,
            _ => s.colorD
        };

        /// <summary>
        /// Samples the configured noise at normalized UV. Offset, rotation (angle) and
        /// independent X/Y scales shape an anisotropic sampling field before the fractal.
        /// </summary>
        private static float SampleNoise(Settings s, float u, float v)
        {
            Vector2 uv = new Vector2(u + s.offsetX, v + s.offsetY);
            uv = RotateCentered(uv, s.angleDegrees * Mathf.Deg2Rad);
            Vector2 p = new Vector2(uv.x * Mathf.Max(0.001f, s.scaleX),
                uv.y * Mathf.Max(0.001f, s.scaleY));

            if (s.warpAmount > 0f)
            {
                float warpX = ValueNoise(p + new Vector2(13.7f, 91.2f), s.seed + 501) - 0.5f;
                float warpY = ValueNoise(p + new Vector2(47.3f, 7.9f), s.seed + 617) - 0.5f;
                p += new Vector2(warpX, warpY) * s.warpAmount * 2f;
            }

            float n = s.noiseType switch
            {
                1 => ValueFbm(p, s),
                2 => RidgedFbm(p, s),
                3 => 1f - WorleyF1(p, s.seed),
                4 => Marble(p, s),
                _ => PerlinFbm(p, s)
            };

            if (s.upperBound > s.lowerBound)
                n = Mathf.Clamp01((n - s.lowerBound) /
                    Mathf.Max(0.0001f, s.upperBound - s.lowerBound));
            else
                n = n >= s.lowerBound ? 1f : 0f;
            if (s.invert) n = 1f - n;
            return Mathf.Clamp01(n * s.brightness);
        }

        private static float PerlinFbm(Vector2 p, Settings s)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            for (int i = 0; i < s.octaves; i++)
            {
                sum += PerlinNoise(p, s.seed + i * 131) * amplitude;
                total += amplitude;
                amplitude *= s.persistence;
                p = p * s.lacunarity + new Vector2(19.7f, 7.3f);
            }
            return total > 0f ? Mathf.Clamp01(sum / total) : 0.5f;
        }

        private static float ValueFbm(Vector2 p, Settings s)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            for (int i = 0; i < s.octaves; i++)
            {
                sum += ValueNoise(p, s.seed + i * 131) * amplitude;
                total += amplitude;
                amplitude *= s.persistence;
                p = p * s.lacunarity + new Vector2(11.3f, 5.1f);
            }
            return total > 0f ? Mathf.Clamp01(sum / total) : 0.5f;
        }

        private static float RidgedFbm(Vector2 p, Settings s)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            for (int i = 0; i < s.octaves; i++)
            {
                float ridge = 1f - Mathf.Abs(PerlinNoise(p, s.seed + i * 131) * 2f - 1f);
                sum += ridge * amplitude;
                total += amplitude;
                amplitude *= s.persistence;
                p = p * s.lacunarity + new Vector2(23.1f, 3.7f);
            }
            return total > 0f ? Mathf.Clamp01(sum / total) : 0.5f;
        }

        private static float Marble(Vector2 p, Settings s)
        {
            float turbulence = PerlinFbm(p, s);
            float veins = (p.x + p.y) * 0.5f + turbulence * s.marbleTurbulence;
            return Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(veins * Mathf.PI * 2f));
        }

        private static float PerlinNoise(Vector2 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y);
            float tx = p.x - x0, ty = p.y - y0;
            float ux = Fade(tx), uy = Fade(ty);
            float a = GradDot(Hash2(x0, y0, seed), tx, ty);
            float b = GradDot(Hash2(x0 + 1, y0, seed), tx - 1f, ty);
            float c = GradDot(Hash2(x0, y0 + 1, seed), tx, ty - 1f);
            float d = GradDot(Hash2(x0 + 1, y0 + 1, seed), tx - 1f, ty - 1f);
            return Mathf.Clamp01(0.5f + Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy) * 0.75f);
        }

        private static float ValueNoise(Vector2 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y);
            float tx = p.x - x0, ty = p.y - y0;
            float ux = Fade(tx), uy = Fade(ty);
            float a = Hash2(x0, y0, seed);
            float b = Hash2(x0 + 1, y0, seed);
            float c = Hash2(x0, y0 + 1, seed);
            float d = Hash2(x0 + 1, y0 + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        private static float WorleyF1(Vector2 p, int seed)
        {
            int bx = Mathf.FloorToInt(p.x), by = Mathf.FloorToInt(p.y);
            float best = 8f;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                int cx = bx + x, cy = by + y;
                Vector2 point = new Vector2(
                    cx + Hash2(cx, cy, seed),
                    cy + Hash2(cx, cy, seed + 37));
                float d = (point - p).sqrMagnitude;
                if (d < best) best = d;
            }
            return Mathf.Clamp01(Mathf.Sqrt(best));
        }

        private static float GradDot(float hash, float x, float y)
        {
            float angle = hash * Mathf.PI * 2f;
            return Mathf.Cos(angle) * x + Mathf.Sin(angle) * y;
        }

        private static float Hash2(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 69069);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00ffffffu) / 16777215f;
            }
        }

        private static float ControlCoverage(TexturePaintReadOnlyParameterTexture mask, float u, float v)
        {
            if (mask == null) return 1f;
            Color sample = mask.GetPixelBilinear(u, v);
            return Mathf.Clamp01(Luminance(sample) * sample.a);
        }

        private static void Write(TexturePaintCommandContextV2 context, string surfaceId,
            OutputTarget target, int y0, int rows, OutputBuffers output)
        {
            Color32[] pixels = output.For(target.channel);
            if (pixels == null) return;
            TexturePaintPluginColorSpace colorSpace =
                TexturePaintChannelUtility.IsColor(target.channel)
                    ? TexturePaintPluginColorSpace.Linear
                    : TexturePaintPluginColorSpace.Data;
            context.WriteTileCompactOwned(surfaceId, target.channel,
                new RectInt(0, y0, target.width, rows), pixels, colorSpace,
                TexturePaintPluginBlend.Replace, 1f);
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters() => new()
        {
            Header("outputsHeader", "Output Channels", "Enabled material channels are generated; the mask target always writes grayscale noise."),
            Bool("outputAlbedo", "Albedo", true, "Writes the multi-color gradient result."),
            Bool("outputRoughness", "Roughness", false, "Maps noise to surface roughness."),
            Bool("outputMetallic", "Metallic", false, "Maps noise to metal response."),
            Bool("outputAO", "Ambient Occlusion", false, "Darkens recesses with low noise values."),
            Bool("outputNormalControl", "Normal Control", false, "Writes noise as neutral-gray centered height."),
            Bool("outputDetailMask", "Detail Mask", false, "Writes remapped noise as detail selection."),
            Float("opacity", "Opacity", 0f, 1f, 1f, "Global multiplier applied to noise coverage."),
            Bool("alphaFromNoise", "Albedo Alpha From Noise", false, "Stores remapped noise in albedo alpha; off keeps albedo opaque."),

            Header("colorsHeader", "Noise Colors", "Low noise maps to Color A and high noise maps to the last enabled color."),
            Enum("colorCount", "Color Count", new[] { "2 Colors", "3 Colors", "4 Colors" }, 1, "Number of gradient stops sampled from the colors below."),
            ColorParam("colorA", "Color A (Low)", new Color(0.09f, 0.11f, 0.14f, 1f), "Gradient color at noise value 0."),
            ColorParam("colorB", "Color B", new Color(0.42f, 0.48f, 0.55f, 1f), "Gradient color at the first interior stop."),
            ColorParam("colorC", "Color C", new Color(0.85f, 0.70f, 0.42f, 1f), "Gradient color at the second interior stop (3-4 colors)."),
            ColorParam("colorD", "Color D (High)", new Color(0.95f, 0.93f, 0.88f, 1f), "Gradient color at noise value 1 (4 colors)."),
            Float("colorBlend", "Color Blend Softness", 0f, 1f, 1f, "Blends linear stop transitions toward smooth easing."),
            Int("posterizeSteps", "Posterize Steps", 0, 16, 0, "Quantizes the noise ramp before color mapping; 0 disables banding."),
            Float("colorVariation", "Color Variation", 0f, 0.5f, 0.04f, "Adds per-texel brightness variation to the mapped gradient."),
            TextureParam(ControlMask, "Control Mask", "Optional grayscale texture multiplied with noise coverage."),

            Header("axisHeader", "Axis & Direction", "Independent X/Y scales stretch the field; Angle rotates that axis across the texture."),
            Float("scaleX", "Scale X", 0.1f, 256f, 8f, "Noise repetitions along the rotated texture X axis."),
            Float("scaleY", "Scale Y", 0.1f, 256f, 8f, "Noise repetitions along the rotated texture Y axis."),
            Float("angle", "Angle", -180f, 180f, 0f, "Rotates the X/Y sampling axis in degrees before scaling."),
            Float("offsetX", "Offset X", -16f, 16f, 0f, "Shifts the sampling field horizontally in UV tiles."),
            Float("offsetY", "Offset Y", -16f, 16f, 0f, "Shifts the sampling field vertically in UV tiles."),

            Header("noiseHeader", "Noise Parameters", "Fractal construction and remapping of the 0-1 noise field."),
            Enum("noiseType", "Noise Type", new[] { "Perlin FBM", "Value FBM", "Ridged FBM", "Cellular (Worley)", "Marble" }, 0, "Procedural noise construction."),
            Int("seed", "Seed", 0, 999999, 1337, "Deterministically changes the noise field."),
            Int("octaves", "Octaves", 1, 8, 4, "Number of fractal detail layers."),
            Float("persistence", "Persistence", 0f, 1f, 0.5f, "Amplitude retained by each successive octave."),
            Float("lacunarity", "Lacunarity", 1f, 4f, 2.03f, "Frequency growth between octaves."),
            Float("warpAmount", "Domain Warp", 0f, 1f, 0f, "Organic distortion applied before noise sampling."),
            Float("marbleTurbulence", "Marble Turbulence", 0f, 8f, 3f, "Vein distortion used by the Marble noise type."),
            Float("lowerBound", "Remap Low", 0f, 1f, 0f, "Noise values at or below this map to zero."),
            Float("upperBound", "Remap High", 0f, 1f, 1f, "Noise values at or above this map to one."),
            Bool("invert", "Invert", false, "Flips the remapped noise field."),
            Float("brightness", "Brightness", 0f, 2f, 1f, "Final multiplier on the remapped noise value."),

            Header("materialHeader", "Material Response", "How the shared noise value converts to scalar channels."),
            Float("roughnessBase", "Roughness Base", 0f, 1f, 0.6f, "Center roughness value."),
            Float("roughnessRange", "Roughness Range", 0f, 0.5f, 0.25f, "Noise-driven deviation around the base roughness."),
            Float("metallicBase", "Metallic Base", 0f, 1f, 0f, "Center metallic value."),
            Float("metallicRange", "Metallic Range", 0f, 0.5f, 0f, "Noise-driven deviation around the base metallic."),
            Float("aoStrength", "AO Strength", 0f, 1f, 0.7f, "Recess darkening applied to low noise values."),
            Float("heightStrength", "Height Strength", 0f, 1f, 0.3f, "Normal Control deviation around neutral gray."),
            Float("detailStrength", "Detail Strength", 0f, 1f, 1f, "Multiplier for the Detail Mask output."),
        };

        private readonly struct Settings
        {
            public readonly bool outputAlbedo;
            public readonly bool outputRoughness;
            public readonly bool outputMetallic;
            public readonly bool outputAo;
            public readonly bool outputNormalControl;
            public readonly bool outputDetailMask;
            public readonly float opacity;
            public readonly bool alphaFromNoise;
            public readonly int colorCount;
            public readonly Color colorA;
            public readonly Color colorB;
            public readonly Color colorC;
            public readonly Color colorD;
            public readonly float colorBlend;
            public readonly int posterizeSteps;
            public readonly float colorVariation;
            public readonly float scaleX;
            public readonly float scaleY;
            public readonly float angleDegrees;
            public readonly float offsetX;
            public readonly float offsetY;
            public readonly int noiseType;
            public readonly int seed;
            public readonly int octaves;
            public readonly float persistence;
            public readonly float lacunarity;
            public readonly float warpAmount;
            public readonly float marbleTurbulence;
            public readonly float lowerBound;
            public readonly float upperBound;
            public readonly bool invert;
            public readonly float brightness;
            public readonly float roughnessBase;
            public readonly float roughnessRange;
            public readonly float metallicBase;
            public readonly float metallicRange;
            public readonly float aoStrength;
            public readonly float heightStrength;
            public readonly float detailStrength;

            public bool AnyOutput => outputAlbedo || outputRoughness || outputMetallic ||
                                     outputAo || outputNormalControl || outputDetailMask;

            public Settings(TexturePaintPluginParameterSet values)
            {
                values ??= new TexturePaintPluginParameterSet();
                outputAlbedo = values.Boolean("outputAlbedo", true);
                outputRoughness = values.Boolean("outputRoughness", false);
                outputMetallic = values.Boolean("outputMetallic", false);
                outputAo = values.Boolean("outputAO", false);
                outputNormalControl = values.Boolean("outputNormalControl", false);
                outputDetailMask = values.Boolean("outputDetailMask", false);
                opacity = Mathf.Clamp01(values.Float("opacity", 1f));
                alphaFromNoise = values.Boolean("alphaFromNoise", false);
                colorCount = Mathf.Clamp(values.Integer("colorCount", 1), 0, 2);
                colorA = values.Color("colorA", new Color(0.09f, 0.11f, 0.14f, 1f));
                colorB = values.Color("colorB", new Color(0.42f, 0.48f, 0.55f, 1f));
                colorC = values.Color("colorC", new Color(0.85f, 0.70f, 0.42f, 1f));
                colorD = values.Color("colorD", new Color(0.95f, 0.93f, 0.88f, 1f));
                colorBlend = Mathf.Clamp01(values.Float("colorBlend", 1f));
                posterizeSteps = Mathf.Clamp(values.Integer("posterizeSteps", 0), 0, 16);
                colorVariation = Mathf.Clamp(values.Float("colorVariation", 0.04f), 0f, 0.5f);
                scaleX = Mathf.Clamp(values.Float("scaleX", 8f), 0.1f, 256f);
                scaleY = Mathf.Clamp(values.Float("scaleY", 8f), 0.1f, 256f);
                angleDegrees = values.Float("angle", 0f);
                offsetX = values.Float("offsetX", 0f);
                offsetY = values.Float("offsetY", 0f);
                noiseType = Mathf.Clamp(values.Integer("noiseType", 0), 0, 4);
                seed = values.Integer("seed", 1337);
                octaves = Mathf.Clamp(values.Integer("octaves", 4), 1, 8);
                persistence = Mathf.Clamp01(values.Float("persistence", 0.5f));
                lacunarity = Mathf.Clamp(values.Float("lacunarity", 2.03f), 1f, 4f);
                warpAmount = Mathf.Clamp01(values.Float("warpAmount", 0f));
                marbleTurbulence = Mathf.Clamp(values.Float("marbleTurbulence", 3f), 0f, 8f);
                lowerBound = Mathf.Clamp01(values.Float("lowerBound", 0f));
                upperBound = Mathf.Clamp01(values.Float("upperBound", 1f));
                invert = values.Boolean("invert", false);
                brightness = Mathf.Clamp(values.Float("brightness", 1f), 0f, 2f);
                roughnessBase = Mathf.Clamp01(values.Float("roughnessBase", 0.6f));
                roughnessRange = Mathf.Clamp(values.Float("roughnessRange", 0.25f), 0f, 0.5f);
                metallicBase = Mathf.Clamp01(values.Float("metallicBase", 0f));
                metallicRange = Mathf.Clamp(values.Float("metallicRange", 0f), 0f, 0.5f);
                aoStrength = Mathf.Clamp01(values.Float("aoStrength", 0.7f));
                heightStrength = Mathf.Clamp(values.Float("heightStrength", 0.3f), 0f, 1f);
                detailStrength = Mathf.Clamp01(values.Float("detailStrength", 1f));
            }
        }

        private readonly struct OutputTarget
        {
            public readonly TexturePaintChannel channel;
            public readonly int width;
            public readonly int height;

            private OutputTarget(TexturePaintChannel channel, TexturePaintReadOnlyChannelInfo info)
            {
                this.channel = channel;
                width = info.width;
                height = info.height;
            }

            public static List<OutputTarget> Find(TexturePaintReadContextV2 source,
                string surfaceId, Settings settings)
            {
                var result = new List<OutputTarget>();
                Add(result, source, surfaceId, TexturePaintChannel.Albedo, settings.outputAlbedo);
                Add(result, source, surfaceId, TexturePaintChannel.Roughness, settings.outputRoughness);
                Add(result, source, surfaceId, TexturePaintChannel.Metallic, settings.outputMetallic);
                Add(result, source, surfaceId, TexturePaintChannel.AmbientOcclusion, settings.outputAo);
                Add(result, source, surfaceId, TexturePaintChannel.NormalControl,
                    settings.outputNormalControl);
                Add(result, source, surfaceId, TexturePaintChannel.DetailMask,
                    settings.outputDetailMask);
                return result;
            }

            private static void Add(List<OutputTarget> result, TexturePaintReadContextV2 source,
                string surfaceId, TexturePaintChannel channel, bool enabled)
            {
                if (!enabled) return;
                TexturePaintReadOnlyChannelInfo info = source.GetChannelInfo(surfaceId, channel);
                if (info != null) result.Add(new OutputTarget(channel, info));
            }
        }

        private sealed class OutputBuffers
        {
            private readonly Color32[] albedo;
            private readonly Color32[] roughness;
            private readonly Color32[] metallic;
            private readonly Color32[] ao;
            private readonly Color32[] normalControl;
            private readonly Color32[] detailMask;
            public bool any;

            public OutputBuffers(int count, TexturePaintChannelMask channels)
            {
                albedo = Has(channels, TexturePaintChannel.Albedo) ? new Color32[count] : null;
                roughness = Has(channels, TexturePaintChannel.Roughness) ? new Color32[count] : null;
                metallic = Has(channels, TexturePaintChannel.Metallic) ? new Color32[count] : null;
                ao = Has(channels, TexturePaintChannel.AmbientOcclusion) ? new Color32[count] : null;
                normalControl = Has(channels, TexturePaintChannel.NormalControl)
                    ? new Color32[count]
                    : null;
                detailMask = Has(channels, TexturePaintChannel.DetailMask) ? new Color32[count] : null;
            }

            public void Set(int index, GeneratedPixel pixel)
            {
                if (pixel.hasAlbedo && albedo != null) albedo[index] = pixel.albedo;
                if (pixel.hasRoughness && roughness != null) roughness[index] = pixel.roughness;
                if (pixel.hasMetallic && metallic != null) metallic[index] = pixel.metallic;
                if (pixel.hasAo && ao != null) ao[index] = pixel.ao;
                if (pixel.hasNormal && normalControl != null) normalControl[index] = pixel.normalControl;
                if (pixel.hasDetail && detailMask != null) detailMask[index] = pixel.detailMask;
                any = true;
            }

            public Color32[] For(TexturePaintChannel channel) => channel switch
            {
                TexturePaintChannel.Albedo => albedo,
                TexturePaintChannel.Roughness => roughness,
                TexturePaintChannel.Metallic => metallic,
                TexturePaintChannel.AmbientOcclusion => ao,
                TexturePaintChannel.NormalControl => normalControl,
                TexturePaintChannel.DetailMask => detailMask,
                _ => null
            };

            private static bool Has(TexturePaintChannelMask channels, TexturePaintChannel channel) =>
                (channels & TexturePaintExportTemplate.ToMask(channel)) != 0;
        }

        private static TexturePaintPluginParameterDefinition Header(string id, string name,
            string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Header
            };

        private static TexturePaintPluginParameterDefinition Float(string id, string name,
            float min, float max, float value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Float, minimum = min, maximum = max,
                defaultNumber = value
            };

        private static TexturePaintPluginParameterDefinition Int(string id, string name,
            int min, int max, int value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Integer, minimum = min, maximum = max,
                defaultNumber = value
            };

        private static TexturePaintPluginParameterDefinition Bool(string id, string name,
            bool value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Boolean, defaultBoolean = value
            };

        private static TexturePaintPluginParameterDefinition ColorParam(string id, string name,
            Color value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Color, defaultColor = value
            };

        private static TexturePaintPluginParameterDefinition TextureParam(string id, string name,
            string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Texture
            };

        private static TexturePaintPluginParameterDefinition Enum(string id, string name,
            string[] options, int value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Enum, minimum = 0,
                maximum = options.Length - 1, defaultNumber = value, enumOptions = options
            };

        private static Vector2 RotateCentered(Vector2 uv, float radians)
        {
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            uv -= Vector2.one * 0.5f;
            return new Vector2(uv.x * cos - uv.y * sin, uv.x * sin + uv.y * cos) +
                   Vector2.one * 0.5f;
        }

        private static float Fade(float value) => value * value * (3f - 2f * value);
        private static byte ToByte(float value) =>
            (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        private static Color32 Gray(float value, float alpha)
        {
            byte b = ToByte(value);
            return new Color32(b, b, b, ToByte(alpha));
        }
        private static float Luminance(Color color) =>
            color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        private static Color AddRgb(Color value, float amount) => new(
            Mathf.Clamp01(value.r + amount), Mathf.Clamp01(value.g + amount),
            Mathf.Clamp01(value.b + amount), value.a);
    }
}
