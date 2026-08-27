using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>
    /// A production-style weathering generator used to exercise Plugin API v2 mesh maps,
    /// multi-channel output, immutable texture parameters, cancellation, and persistence.
    /// </summary>
    public sealed class AgifyGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        private const string DirtTexture = "dirtTexture";
        private const string DirtMask = "dirtMask";
        private const string WearTexture = "wearTexture";
        private const string WearMask = "wearMask";

        private static readonly TexturePaintPluginDescriptor descriptor = new TexturePaintPluginDescriptor
        {
            id = "com.uma.texturepaint.agify",
            displayName = "Agify — Dirt & Edge Wear",
            description = "Builds cavity dirt and convex edge wear across the complete paint target using signed mesh curvature, composed normal detail, AO, world-space projection, and optional texture masks.",
            pluginVersion = "1.1.0",
            capabilities = TexturePaintPluginCapability.Generator |
                           TexturePaintPluginCapability.ReadsMeshMaps |
                           TexturePaintPluginCapability.LongRunning,
            declaredChannels = TexturePaintChannelMask.Albedo |
                               TexturePaintChannelMask.Metallic |
                               TexturePaintChannelMask.Roughness |
                               TexturePaintChannelMask.AmbientOcclusion |
                               TexturePaintChannelMask.NormalControl,
            readChannels = TexturePaintChannelMask.Normal |
                           TexturePaintChannelMask.AmbientOcclusion,
            channelSnapshotMaximumResolution = 2048,
            requiredMeshMaps = TexturePaintMeshMapMask.WorldPosition |
                               TexturePaintMeshMapMask.WorldNormal |
                               TexturePaintMeshMapMask.SignedCurvature |
                               TexturePaintMeshMapMask.AmbientOcclusion |
                               TexturePaintMeshMapMask.SurfaceId,
            parameters = new List<TexturePaintPluginParameterDefinition>
            {
                Enum("projection", "Projection", new[] { "UV", "World Triplanar" }, 1,
                    "Projects optional dirt, wear, and mask textures in UV or seamless world triplanar space."),
                Float("textureScale", "Texture Frequency", 0.05f, 100f, 4f,
                    "Optional-texture repetitions per UV tile, or per meter in World Triplanar mode (Unity 1 unit = 1 meter)."),
                Integer("seed", "Seed", 0, 100000, 173,
                    "Changes deterministic breakup without changing curvature or AO."),
                Float("curvatureContrast", "Curvature Contrast", 0.1f, 8f, 2.5f,
                    "Sharpens or broadens both concave dirt and convex wear selection."),
                Float("normalCurvature", "Normal Detail Influence", 0f, 4f, 1f,
                    "Adds signed high-frequency curvature derived from the composed tangent-space normal map."),
                Float("aoInfluence", "AO / Cavity Influence", 0f, 2f, 0.8f,
                    "Adds source AO and generated cavity accessibility to dirt accumulation."),
                Float("breakup", "Procedural Breakup", 0f, 1f, 0.55f,
                    "Breaks up otherwise uniform dirt and wear coverage."),
                Float("breakupScale", "Breakup Frequency", 0.25f, 256f, 28f,
                    "Noise repetitions per UV tile, or per meter in World Triplanar mode."),
                Float("fractalEdge", "Fractal Edge", 0f, 1f, 0f,
                    "Displaces dirt and wear boundaries with multi-level fractal noise; zero preserves the original smooth edge."),
                Integer("fractalLevels", "Fractal Levels", 1, 8, 4,
                    "Number of edge-noise octaves; more levels add progressively smaller edge damage."),
                Float("fractalPersistence", "Fractal Level Strength", 0.1f, 0.9f, 0.5f,
                    "How strongly each finer fractal edge level contributes."),

                Float("dirtAmount", "Dirt Amount", 0f, 1f, 0.7f,
                    "Overall concave dirt coverage."),
                ColorParameter("dirtColor", "Dirt Color", new Color(0.16f, 0.105f, 0.055f, 1f),
                    "Color multiplied by the optional dirt texture."),
                Texture(DirtTexture, "Dirt Texture",
                    "Optional color texture projected into concave and occluded regions."),
                Texture(DirtMask, "Dirt Mask",
                    "Optional grayscale mask multiplied into the generated dirt selection."),
                Float("dirtRoughness", "Dirt Roughness", 0f, 1f, 0.88f,
                    "Roughness written beneath generated dirt coverage."),
                Float("dirtAO", "Dirt AO", 0f, 1f, 0.45f,
                    "Ambient-occlusion value written beneath generated dirt coverage."),
                Float("dirtHeight", "Dirt Height", 0f, 0.5f, 0.045f,
                    "Raises dirt through Normal Control; zero leaves normals unchanged."),

                Float("wearAmount", "Wear Amount", 0f, 1f, 0.55f,
                    "Overall convex edge-wear and chipping coverage."),
                ColorParameter("wearColor", "Exposed / Wear Color", new Color(0.58f, 0.54f, 0.46f, 1f),
                    "Color multiplied by the optional wear texture."),
                Texture(WearTexture, "Wear Texture",
                    "Optional chipped-paint or exposed-material color texture."),
                Texture(WearMask, "Wear Mask",
                    "Optional grayscale mask multiplied into convex wear selection."),
                Float("wearRoughness", "Wear Roughness", 0f, 1f, 0.38f,
                    "Roughness written beneath generated wear coverage."),
                Float("exposedMetallic", "Exposed Metallic", 0f, 1f, 0f,
                    "Metallic value revealed in worn regions; raise for chipped painted metal."),
                Float("wearDepth", "Chip Depth", 0f, 0.5f, 0.065f,
                    "Recesses wear through Normal Control; zero leaves normals unchanged.")
            }
        };

        public TexturePaintPluginDescriptor Descriptor => descriptor;

        public Task ExecuteAsync(TexturePaintCommandContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Task.Run(() => Execute(context), context.cancellationToken);
        }

        private static void Execute(TexturePaintCommandContextV2 context)
        {
            Settings settings = Settings.From(context.parameters);
            TexturePaintReadOnlyParameterTexture dirtTexture = context.GetTextureParameter(DirtTexture);
            TexturePaintReadOnlyParameterTexture dirtMask = context.GetTextureParameter(DirtMask);
            TexturePaintReadOnlyParameterTexture wearTexture = context.GetTextureParameter(WearTexture);
            TexturePaintReadOnlyParameterTexture wearMask = context.GetTextureParameter(WearMask);

            for (int surfaceIndex = 0; surfaceIndex < context.source.surfaceIds.Count; surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];
                SurfaceInputs inputs = new SurfaceInputs(context, surfaceId);
                List<OutputTarget> targets = OutputTarget.Find(context.source, surfaceId);
                if (targets.Count == 0) continue;

                var targetGroups = new Dictionary<long, List<OutputTarget>>();
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    OutputTarget target = targets[targetIndex];
                    long key = ((long)target.width << 32) | (uint)target.height;
                    if (!targetGroups.TryGetValue(key, out List<OutputTarget> group))
                        targetGroups.Add(key, group = new List<OutputTarget>());
                    group.Add(target);
                }

                foreach (KeyValuePair<long, List<OutputTarget>> pair in targetGroups)
                {
                    List<OutputTarget> group = pair.Value;
                    int width = group[0].width;
                    int height = group[0].height;
                    const int RowsPerTile = 256;
                    for (int y = 0; y < height; y += RowsPerTile)
                    {
                        int rows = Mathf.Min(RowsPerTile, height - y);
                        OutputBuffers buffers = Generate(inputs, settings, width, height, y, rows,
                            dirtTexture, dirtMask, wearTexture, wearMask,
                            context, surfaceIndex, context.source.surfaceIds.Count);
                        if (!buffers.anyCoverage) continue;
                        for (int targetIndex = 0; targetIndex < group.Count; targetIndex++)
                            Write(context, surfaceId, group[targetIndex], y, rows, buffers);
                    }
                }
            }

            context.progress?.Report(1f);
        }

        public static float CalculateNormalCurvature(TexturePaintReadOnlyImage normal, float u, float v)
        {
            if (normal == null || normal.width < 2 || normal.height < 2) return 0f;
            float du = 1f / normal.width;
            float dv = 1f / normal.height;
            Vector3 left = DecodeNormal(normal.GetPixelBilinear(Repeat01(u - du), Repeat01(v)));
            Vector3 right = DecodeNormal(normal.GetPixelBilinear(Repeat01(u + du), Repeat01(v)));
            Vector3 down = DecodeNormal(normal.GetPixelBilinear(Repeat01(u), Repeat01(v - dv)));
            Vector3 up = DecodeNormal(normal.GetPixelBilinear(Repeat01(u), Repeat01(v + dv)));
            // Tangent normals approximate (-dh/dx,-dh/dy,1). Their divergence is positive over a
            // convex height maximum and negative in a concave minimum.
            return Mathf.Clamp(((right.x - left.x) + (up.y - down.y)) * 0.5f, -1f, 1f);
        }

        private static OutputBuffers Generate(SurfaceInputs inputs, Settings settings, int width, int height,
            int yStart, int rowCount,
            TexturePaintReadOnlyParameterTexture dirtTexture,
            TexturePaintReadOnlyParameterTexture dirtMask,
            TexturePaintReadOnlyParameterTexture wearTexture,
            TexturePaintReadOnlyParameterTexture wearMask,
            TexturePaintCommandContextV2 context, int surfaceIndex, int surfaceCount)
        {
            var output = new OutputBuffers(width * rowCount);
            Parallel.For(0, rowCount, new ParallelOptions
                { CancellationToken = context.cancellationToken }, localY =>
            {
                int y = yStart + localY;
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    int index = localY * width + x;
                    Vector3 position = inputs.WorldPosition(u, v);
                    Vector3 worldNormal = inputs.WorldNormal(u, v);
                    float signed = inputs.SignedCurvature(u, v);
                    signed = Mathf.Clamp(signed +
                        CalculateNormalCurvature(inputs.normal, inputs.meshId, u, v) *
                        settings.normalCurvature, -1f, 1f);

                    float concave = ShapeCurvature(Mathf.Max(0f, -signed),
                        settings.curvatureContrast);
                    float convex = ShapeCurvature(Mathf.Max(0f, signed),
                        settings.curvatureContrast);
                    float sourceCavity = inputs.ambientOcclusion != null
                        ? 1f - Luminance(inputs.ambientOcclusion.GetPixelBilinear(u, v)) : 0f;
                    float meshCavity = inputs.meshAmbientOcclusion != null
                        ? 1f - inputs.meshAmbientOcclusion.GetPixelBilinear(u, v).r : 0f;
                    float cavity = Mathf.Clamp01(Mathf.Max(sourceCavity, meshCavity) * settings.aoInfluence);
                    float noise = Breakup(position, u, v, settings);
                    if (settings.fractalEdge > 0f)
                    {
                        float fractal = WeatheringFractal.Sample(position, u, v,
                            settings.triplanar, settings.breakupScale, settings.seed,
                            settings.fractalLevels, settings.fractalPersistence);
                        concave = WeatheringFractal.DistortEdge(
                            concave, fractal, settings.fractalEdge);
                        convex = WeatheringFractal.DistortEdge(
                            convex, 1f - fractal, settings.fractalEdge);
                    }

                    Color dirtTexel = SampleProjected(dirtTexture, position, worldNormal, u, v, settings);
                    Color wearTexel = SampleProjected(wearTexture, position, worldNormal, u, v, settings);
                    float dirtUserMask = SampleMask(dirtMask, position, worldNormal, u, v, settings);
                    float wearUserMask = SampleMask(wearMask, position, worldNormal, u, v, settings);
                    float dirtCoverage = Mathf.Clamp01((concave + cavity * (1f - concave)) *
                        settings.dirtAmount * noise * dirtUserMask * dirtTexel.a);
                    float accessibility = 1f - Mathf.Clamp01(Mathf.Max(sourceCavity, meshCavity));
                    float wearCoverage = Mathf.Clamp01(convex * settings.wearAmount * noise *
                        wearUserMask * wearTexel.a * accessibility);
                    float coverage = 1f - (1f - dirtCoverage) * (1f - wearCoverage);
                    if (coverage <= 0.0001f) continue;
                    output.anyCoverage = true;

                    Color dirtColor = MultiplyRGB(settings.dirtColor, dirtTexel);
                    Color wearColor = MultiplyRGB(settings.wearColor, wearTexel);
                    output.albedo[index] = ResolveColor(dirtColor, dirtCoverage,
                        wearColor, wearCoverage, coverage);
                    output.roughness[index] = ScalarColor(ResolveScalar(settings.dirtRoughness,
                        dirtCoverage, settings.wearRoughness, wearCoverage, coverage), coverage);
                    output.ambientOcclusion[index] = ScalarColor(ResolveScalar(settings.dirtAO,
                        dirtCoverage, 1f, wearCoverage, coverage), coverage);
                    output.metallic[index] = ScalarColor(ResolveScalar(0f, dirtCoverage,
                        settings.exposedMetallic, wearCoverage, coverage), coverage);
                    float normalControlHeight = ResolveScalar(0.5f + settings.dirtHeight, dirtCoverage,
                        0.5f - settings.wearDepth, wearCoverage, coverage);
                    output.normalControl[index] = ScalarColor(Mathf.Clamp01(normalControlHeight), coverage);
                }
            });
            return output;
        }

        private static void Write(TexturePaintCommandContextV2 context, string surfaceId,
            OutputTarget target, int yStart, int rowCount, OutputBuffers output)
        {
            Color32[] pixels = target.channel switch
            {
                TexturePaintChannel.Albedo => output.albedo,
                TexturePaintChannel.Roughness => output.roughness,
                TexturePaintChannel.AmbientOcclusion => output.ambientOcclusion,
                TexturePaintChannel.Metallic => output.metallic,
                TexturePaintChannel.NormalControl => output.normalControl,
                _ => null
            };
            if (pixels == null) return;
            TexturePaintPluginColorSpace colorSpace = target.channel == TexturePaintChannel.Albedo
                ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data;
            context.WriteTileCompactOwned(surfaceId, target.channel,
                new RectInt(0, yStart, target.width, rowCount),
                pixels, colorSpace, TexturePaintPluginBlend.Normal, 1f);
        }

        private static Color SampleProjected(TexturePaintReadOnlyParameterTexture texture,
            Vector3 position, Vector3 normal, float u, float v, Settings settings)
        {
            if (texture == null) return Color.white;
            float scale = settings.textureScale;
            if (!settings.triplanar)
                return texture.GetPixelBilinear(Repeat01(u * scale), Repeat01(v * scale));
            Vector3 weight = new Vector3(Mathf.Pow(Mathf.Abs(normal.x), 4f),
                Mathf.Pow(Mathf.Abs(normal.y), 4f), Mathf.Pow(Mathf.Abs(normal.z), 4f));
            float sum = weight.x + weight.y + weight.z;
            if (sum <= 0.00001f) weight = new Vector3(0f, 0f, 1f);
            else weight /= sum;
            Color x = texture.GetPixelBilinear(Repeat01(position.z * scale), Repeat01(position.y * scale));
            Color y = texture.GetPixelBilinear(Repeat01(position.x * scale), Repeat01(position.z * scale));
            Color z = texture.GetPixelBilinear(Repeat01(position.x * scale), Repeat01(position.y * scale));
            return x * weight.x + y * weight.y + z * weight.z;
        }

        private static float SampleMask(TexturePaintReadOnlyParameterTexture texture,
            Vector3 position, Vector3 normal, float u, float v, Settings settings)
        {
            if (texture == null) return 1f;
            Color sample = SampleProjected(texture, position, normal, u, v, settings);
            return Mathf.Clamp01(Luminance(sample) * sample.a);
        }

        private static float Breakup(Vector3 position, float u, float v, Settings settings)
        {
            if (settings.breakup <= 0f) return 1f;
            float offset = settings.seed * 0.013731f;
            float scale = settings.breakupScale;
            float noise;
            if (settings.triplanar)
            {
                float xy = Mathf.PerlinNoise(position.x * scale + offset, position.y * scale - offset);
                float yz = Mathf.PerlinNoise(position.y * scale + offset * 2f, position.z * scale - offset);
                float zx = Mathf.PerlinNoise(position.z * scale + offset, position.x * scale - offset * 2f);
                noise = (xy + yz + zx) / 3f;
            }
            else noise = Mathf.PerlinNoise(u * scale + offset, v * scale - offset);
            noise = SmoothStep(0.2f, 0.8f, noise);
            return Mathf.Lerp(1f, noise, settings.breakup);
        }

        public static float ShapeCurvature(float value, float contrast)
        {
            value = Mathf.Clamp01(value);
            // Contrast values above one are documented as narrowing the selection to stronger
            // curvature. The inverse exponent did the opposite, turning weak curvature across
            // broad surface areas into strong dirt/wear coverage.
            return Mathf.Pow(value, Mathf.Max(0.1f, contrast));
        }

        private static Color ResolveColor(Color dirt, float dirtCoverage, Color wear,
            float wearCoverage, float coverage)
        {
            float dirtWeight = dirtCoverage * (1f - wearCoverage);
            float wearWeight = wearCoverage;
            float weight = Mathf.Max(0.0001f, dirtWeight + wearWeight);
            return new Color((dirt.r * dirtWeight + wear.r * wearWeight) / weight,
                (dirt.g * dirtWeight + wear.g * wearWeight) / weight,
                (dirt.b * dirtWeight + wear.b * wearWeight) / weight, coverage);
        }

        private static float ResolveScalar(float dirt, float dirtCoverage, float wear,
            float wearCoverage, float coverage)
        {
            float dirtWeight = dirtCoverage * (1f - wearCoverage);
            float wearWeight = wearCoverage;
            float weight = dirtWeight + wearWeight;
            return weight <= 0.0001f ? 0f : (dirt * dirtWeight + wear * wearWeight) / weight;
        }

        private static Color ScalarColor(float value, float alpha) =>
            new Color(value, value, value, alpha);

        private static Color MultiplyRGB(Color first, Color second) =>
            new Color(first.r * second.r, first.g * second.g, first.b * second.b, first.a * second.a);

        private static float Luminance(Color value) =>
            value.r * 0.2126f + value.g * 0.7152f + value.b * 0.0722f;

        private static Vector3 DecodeNormal(Color value)
        {
            Vector3 normal = new Vector3(value.r * 2f - 1f, value.g * 2f - 1f,
                value.b * 2f - 1f);
            return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.forward;
        }

        private static Vector3 DecodeWorldNormal(Color value) => DecodeNormal(value);
        private static float Repeat01(float value) => value - Mathf.Floor(value);

        private static float SmoothStep(float minimum, float maximum, float value)
        {
            float t = Mathf.Clamp01((value - minimum) / Mathf.Max(0.00001f, maximum - minimum));
            return t * t * (3f - 2f * t);
        }

        private static float CalculateNormalCurvature(TexturePaintReadOnlyImage normal,
            TexturePaintReadOnlyMeshMap meshId, float u, float v)
        {
            if (meshId == null) return CalculateNormalCurvature(normal, u, v);
            if (normal == null || normal.width < 2 || normal.height < 2) return 0f;
            float du = 1f / normal.width;
            float dv = 1f / normal.height;
            Color centerId = meshId.GetPixelBilinear(u, v);
            Vector3 center = DecodeNormal(normal.GetPixelBilinear(u, v));
            Vector3 left = SampleNormalOnIsland(normal, meshId, centerId, center, u - du, v);
            Vector3 right = SampleNormalOnIsland(normal, meshId, centerId, center, u + du, v);
            Vector3 down = SampleNormalOnIsland(normal, meshId, centerId, center, u, v - dv);
            Vector3 up = SampleNormalOnIsland(normal, meshId, centerId, center, u, v + dv);
            return Mathf.Clamp(((right.x - left.x) + (up.y - down.y)) * 0.5f, -1f, 1f);
        }

        private static Vector3 SampleNormalOnIsland(TexturePaintReadOnlyImage normal,
            TexturePaintReadOnlyMeshMap meshId, Color centerId, Vector3 fallback, float u, float v)
        {
            u = Repeat01(u);
            v = Repeat01(v);
            Color sampleId = meshId.GetPixelBilinear(u, v);
            if (centerId.a < 0.5f || sampleId.a < 0.5f ||
                Mathf.Abs(centerId.g - sampleId.g) > 0.1f ||
                Mathf.Abs(centerId.b - sampleId.b) > 0.1f) return fallback;
            return DecodeNormal(normal.GetPixelBilinear(u, v));
        }

        private sealed class SurfaceInputs
        {
            public readonly TexturePaintReadOnlyImage normal;
            public readonly TexturePaintReadOnlyImage ambientOcclusion;
            public readonly TexturePaintReadOnlyMeshMap position;
            public readonly TexturePaintReadOnlyMeshMap worldNormal;
            public readonly TexturePaintReadOnlyMeshMap signedCurvature;
            public readonly TexturePaintReadOnlyMeshMap meshAmbientOcclusion;
            public readonly TexturePaintReadOnlyMeshMap meshId;

            public SurfaceInputs(TexturePaintCommandContextV2 context, string surfaceId)
            {
                normal = context.source.Get(surfaceId, TexturePaintChannel.Normal);
                ambientOcclusion = context.source.Get(surfaceId, TexturePaintChannel.AmbientOcclusion);
                position = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldPosition);
                worldNormal = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldNormal);
                signedCurvature = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SignedCurvature);
                meshAmbientOcclusion = context.GetMeshMap(surfaceId, TexturePaintMeshMap.AmbientOcclusion);
                meshId = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SurfaceId);
            }

            public Vector3 WorldPosition(float u, float v)
            {
                Color value = position?.GetPixelBilinear(u, v) ?? new Color(u, v, 0f, 1f);
                return new Vector3(value.r, value.g, value.b);
            }

            public Vector3 WorldNormal(float u, float v) => worldNormal != null
                ? DecodeWorldNormal(worldNormal.GetPixelBilinear(u, v)) : Vector3.forward;

            public float SignedCurvature(float u, float v) => signedCurvature != null
                ? signedCurvature.GetPixelBilinear(u, v).r * 2f - 1f : 0f;
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

            public static List<OutputTarget> Find(TexturePaintReadContextV2 source, string surfaceId)
            {
                var result = new List<OutputTarget>();
                Add(TexturePaintChannel.Albedo);
                Add(TexturePaintChannel.Roughness);
                Add(TexturePaintChannel.AmbientOcclusion);
                Add(TexturePaintChannel.Metallic);
                Add(TexturePaintChannel.NormalControl);
                return result;

                void Add(TexturePaintChannel channel)
                {
                    TexturePaintReadOnlyChannelInfo info = source.GetChannelInfo(surfaceId, channel);
                    if (info != null) result.Add(new OutputTarget(channel, info));
                }
            }
        }

        private sealed class OutputBuffers
        {
            public readonly Color32[] albedo;
            public readonly Color32[] roughness;
            public readonly Color32[] ambientOcclusion;
            public readonly Color32[] metallic;
            public readonly Color32[] normalControl;
            public bool anyCoverage;

            public OutputBuffers(int count)
            {
                albedo = new Color32[count];
                roughness = new Color32[count];
                ambientOcclusion = new Color32[count];
                metallic = new Color32[count];
                normalControl = new Color32[count];
            }
        }

        private readonly struct Settings
        {
            public readonly bool triplanar;
            public readonly float textureScale;
            public readonly int seed;
            public readonly float curvatureContrast;
            public readonly float normalCurvature;
            public readonly float aoInfluence;
            public readonly float breakup;
            public readonly float breakupScale;
            public readonly float fractalEdge;
            public readonly int fractalLevels;
            public readonly float fractalPersistence;
            public readonly float dirtAmount;
            public readonly Color dirtColor;
            public readonly float dirtRoughness;
            public readonly float dirtAO;
            public readonly float dirtHeight;
            public readonly float wearAmount;
            public readonly Color wearColor;
            public readonly float wearRoughness;
            public readonly float exposedMetallic;
            public readonly float wearDepth;

            private Settings(TexturePaintPluginParameterSet values)
            {
                triplanar = values.Integer("projection", 1) == 1;
                textureScale = Mathf.Max(0.05f, values.Float("textureScale", 4f));
                seed = values.Integer("seed", 173);
                curvatureContrast = Mathf.Max(0.1f, values.Float("curvatureContrast", 2.5f));
                normalCurvature = Mathf.Max(0f, values.Float("normalCurvature", 1f));
                aoInfluence = Mathf.Max(0f, values.Float("aoInfluence", 0.8f));
                breakup = Mathf.Clamp01(values.Float("breakup", 0.55f));
                breakupScale = Mathf.Max(0.25f, values.Float("breakupScale", 28f));
                fractalEdge = Mathf.Clamp01(values.Float("fractalEdge", 0f));
                fractalLevels = Mathf.Clamp(values.Integer("fractalLevels", 4), 1, 8);
                fractalPersistence = Mathf.Clamp(values.Float("fractalPersistence", 0.5f),
                    0.1f, 0.9f);
                dirtAmount = Mathf.Clamp01(values.Float("dirtAmount", 0.7f));
                dirtColor = values.Color("dirtColor", new Color(0.16f, 0.105f, 0.055f, 1f));
                dirtRoughness = Mathf.Clamp01(values.Float("dirtRoughness", 0.88f));
                dirtAO = Mathf.Clamp01(values.Float("dirtAO", 0.45f));
                dirtHeight = Mathf.Clamp(values.Float("dirtHeight", 0.045f), 0f, 0.5f);
                wearAmount = Mathf.Clamp01(values.Float("wearAmount", 0.55f));
                wearColor = values.Color("wearColor", new Color(0.58f, 0.54f, 0.46f, 1f));
                wearRoughness = Mathf.Clamp01(values.Float("wearRoughness", 0.38f));
                exposedMetallic = Mathf.Clamp01(values.Float("exposedMetallic", 0f));
                wearDepth = Mathf.Clamp(values.Float("wearDepth", 0.065f), 0f, 0.5f);
            }

            public static Settings From(TexturePaintPluginParameterSet values) =>
                new Settings(values ?? new TexturePaintPluginParameterSet());
        }

        private static TexturePaintPluginParameterDefinition Float(string id, string name,
            float minimum, float maximum, float value, string description) =>
            new TexturePaintPluginParameterDefinition
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Float,
                minimum = minimum, maximum = maximum, defaultNumber = value, description = description
            };

        private static TexturePaintPluginParameterDefinition Integer(string id, string name,
            int minimum, int maximum, int value, string description) =>
            new TexturePaintPluginParameterDefinition
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Integer,
                minimum = minimum, maximum = maximum, defaultNumber = value, description = description
            };

        private static TexturePaintPluginParameterDefinition Enum(string id, string name,
            string[] options, int value, string description) =>
            new TexturePaintPluginParameterDefinition
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Enum,
                minimum = 0f, maximum = options.Length - 1, defaultNumber = value,
                enumOptions = options, description = description
            };

        private static TexturePaintPluginParameterDefinition ColorParameter(string id, string name,
            Color value, string description) => new TexturePaintPluginParameterDefinition
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Color,
                defaultColor = value, description = description
            };

        private static TexturePaintPluginParameterDefinition Texture(string id, string name,
            string description) => new TexturePaintPluginParameterDefinition
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Texture,
                description = description
            };
    }
}
