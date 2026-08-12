using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    public sealed class DirtifyGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            WeatheringGeneratorEngine.CreateDescriptor(WeatheringMode.Dirt);

        public TexturePaintPluginDescriptor Descriptor => descriptor;

        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            WeatheringGeneratorEngine.ExecuteAsync(context, WeatheringMode.Dirt);
    }

    public sealed class EdgeWearGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            WeatheringGeneratorEngine.CreateDescriptor(WeatheringMode.EdgeWear);

        public TexturePaintPluginDescriptor Descriptor => descriptor;

        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            WeatheringGeneratorEngine.ExecuteAsync(context, WeatheringMode.EdgeWear);
    }

    internal enum WeatheringMode
    {
        Dirt,
        EdgeWear
    }

    internal static class WeatheringFractal
    {
        public static float Sample(Vector3 position, float u, float v, bool triplanar,
            float scale, int seed, int levels, float persistence)
        {
            levels = Mathf.Clamp(levels, 1, 8);
            persistence = Mathf.Clamp(persistence, 0.1f, 0.9f);
            float frequency = Mathf.Max(0.01f, scale);
            float amplitude = 1f;
            float total = 0f;
            float weight = 0f;
            float offset = seed * 0.013731f;
            for (int octave = 0; octave < levels; octave++)
            {
                float octaveOffset = offset * (octave + 1f);
                float sample;
                if (triplanar)
                {
                    float xy = Mathf.PerlinNoise(position.x * frequency + octaveOffset,
                        position.y * frequency - octaveOffset);
                    float yz = Mathf.PerlinNoise(position.y * frequency + octaveOffset * 2f,
                        position.z * frequency - octaveOffset);
                    float zx = Mathf.PerlinNoise(position.z * frequency + octaveOffset,
                        position.x * frequency - octaveOffset * 2f);
                    sample = (xy + yz + zx) / 3f;
                }
                else
                {
                    sample = Mathf.PerlinNoise(u * frequency + octaveOffset,
                        v * frequency - octaveOffset);
                }
                total += sample * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= 2f;
            }
            return weight <= 0.00001f ? 0.5f : Mathf.Clamp01(total / weight);
        }

        public static float DistortEdge(float selection, float noise, float strength)
        {
            selection = Mathf.Clamp01(selection);
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f || selection <= 0f || selection >= 1f) return selection;
            float edgeWeight = 4f * selection * (1f - selection);
            return Mathf.Clamp01(selection + (noise - 0.5f) * strength * edgeWeight);
        }

        public static float Breakup(float noise, float amount)
        {
            if (amount <= 0f) return 1f;
            float shaped = SmoothStep(0.2f, 0.8f, noise);
            return Mathf.Lerp(1f, shaped, Mathf.Clamp01(amount));
        }

        private static float SmoothStep(float minimum, float maximum, float value)
        {
            float t = Mathf.Clamp01((value - minimum) /
                Mathf.Max(0.00001f, maximum - minimum));
            return t * t * (3f - 2f * t);
        }
    }

    internal static class WeatheringGeneratorEngine
    {
        private const string SurfaceTexture = "surfaceTexture";
        private const string SurfaceMask = "surfaceMask";
        private const int RowsPerTile = 256;

        public static TexturePaintPluginDescriptor CreateDescriptor(WeatheringMode mode)
        {
            bool dirt = mode == WeatheringMode.Dirt;
            return new TexturePaintPluginDescriptor
            {
                id = dirt ? "com.uma.texturepaint.dirtify" : "com.uma.texturepaint.edgewear",
                displayName = dirt ? "Dirtify — Gap Dirt" : "Edge Wear",
                description = dirt
                    ? "Accumulates controllable, fractally broken dirt in concave gaps and occluded cavities, with explicit gap size and outward spread."
                    : "Creates controllable, fractally broken wear on convex edges, with explicit edge size and outward spread.",
                pluginVersion = "1.0.0",
                capabilities = TexturePaintPluginCapability.Generator |
                               TexturePaintPluginCapability.ReadsMeshMaps |
                               TexturePaintPluginCapability.LongRunning,
                declaredChannels = dirt
                    ? TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Roughness |
                      TexturePaintChannelMask.AmbientOcclusion |
                      TexturePaintChannelMask.NormalControl
                    : TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Roughness |
                      TexturePaintChannelMask.Metallic | TexturePaintChannelMask.NormalControl,
                readChannels = TexturePaintChannelMask.Normal |
                               TexturePaintChannelMask.AmbientOcclusion,
                channelSnapshotMaximumResolution = 2048,
                requiredMeshMaps = TexturePaintMeshMapMask.WorldPosition |
                                   TexturePaintMeshMapMask.WorldNormal |
                                   TexturePaintMeshMapMask.SignedCurvature |
                                   TexturePaintMeshMapMask.AmbientOcclusion |
                                   TexturePaintMeshMapMask.SurfaceId,
                parameters = BuildParameters(mode)
            };
        }

        public static Task ExecuteAsync(TexturePaintCommandContextV2 context, WeatheringMode mode)
        {
            Settings settings = new Settings(context.parameters, mode);
            TexturePaintReadOnlyParameterTexture texture =
                context.GetTextureParameter(SurfaceTexture);
            TexturePaintReadOnlyParameterTexture mask =
                context.GetTextureParameter(SurfaceMask);

            for (int surfaceIndex = 0; surfaceIndex < context.source.surfaceIds.Count;
                surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];
                var inputs = new SurfaceInputs(context, surfaceId);
                List<OutputTarget> targets = OutputTarget.Find(context.source, surfaceId, mode);
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

                foreach (KeyValuePair<long, List<OutputTarget>> pair in groups)
                {
                    List<OutputTarget> group = pair.Value;
                    int width = group[0].width;
                    int height = group[0].height;
                    for (int y = 0; y < height; y += RowsPerTile)
                    {
                        int rows = Mathf.Min(RowsPerTile, height - y);
                        OutputBuffers buffers = Generate(inputs, settings, width, height,
                            y, rows, texture, mask, context, surfaceIndex,
                            context.source.surfaceIds.Count);
                        if (!buffers.anyCoverage) continue;
                        for (int targetIndex = 0; targetIndex < group.Count; targetIndex++)
                            Write(context, surfaceId, group[targetIndex], y, rows, buffers);
                    }
                }
            }

            context.progress?.Report(1f);
            return Task.CompletedTask;
        }

        private static OutputBuffers Generate(SurfaceInputs inputs, Settings settings,
            int width, int height, int yStart, int rowCount,
            TexturePaintReadOnlyParameterTexture texture,
            TexturePaintReadOnlyParameterTexture mask,
            TexturePaintCommandContextV2 context, int surfaceIndex, int surfaceCount)
        {
            var output = new OutputBuffers(width * rowCount);
            float radiusU = settings.featureSize / Mathf.Max(1f, width);
            float radiusV = settings.featureSize / Mathf.Max(1f, height);
            for (int localY = 0; localY < rowCount; localY++)
            {
                int y = yStart + localY;
                if ((y & 31) == 0)
                {
                    context.cancellationToken.ThrowIfCancellationRequested();
                    context.progress?.Report(0.25f + 0.7f *
                        ((surfaceIndex + y / (float)Mathf.Max(1, height)) /
                         Mathf.Max(1, surfaceCount)));
                }

                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    if (!inputs.IsCovered(u, v)) continue;
                    int index = localY * width + x;
                    Vector3 position = inputs.WorldPosition(u, v);
                    Vector3 worldNormal = inputs.WorldNormal(u, v);

                    float selection = Select(inputs, settings, u, v, true);
                    if (settings.featureSize > 0.001f && settings.spread > 0f)
                    {
                        Color centerId = inputs.MeshId(u, v);
                        float nearby = 0f;
                        nearby = Mathf.Max(nearby, SelectNeighbor(inputs, settings,
                            centerId, u - radiusU, v));
                        nearby = Mathf.Max(nearby, SelectNeighbor(inputs, settings,
                            centerId, u + radiusU, v));
                        nearby = Mathf.Max(nearby, SelectNeighbor(inputs, settings,
                            centerId, u, v - radiusV));
                        nearby = Mathf.Max(nearby, SelectNeighbor(inputs, settings,
                            centerId, u, v + radiusV));
                        selection = Mathf.Max(selection, nearby * settings.spread);
                    }

                    float fractal = WeatheringFractal.Sample(position, u, v,
                        settings.triplanar, settings.breakupScale, settings.seed,
                        settings.fractalLevels, settings.fractalPersistence);
                    selection = WeatheringFractal.DistortEdge(
                        selection, fractal, settings.fractalEdge);
                    selection *= WeatheringFractal.Breakup(fractal, settings.breakup);

                    Color texel = SampleProjected(texture, position, worldNormal, u, v, settings);
                    float userMask = SampleMask(mask, position, worldNormal, u, v, settings);
                    float coverage = Mathf.Clamp01(selection * settings.amount *
                        userMask * texel.a);
                    if (coverage <= 0.0001f) continue;
                    output.anyCoverage = true;

                    Color materialColor = MultiplyRGB(settings.color, texel);
                    output.albedo[index] = new Color(materialColor.r, materialColor.g,
                        materialColor.b, coverage);
                    output.roughness[index] = Scalar(settings.roughness, coverage);
                    output.normalControl[index] = Scalar(Mathf.Clamp01(settings.normalValue), coverage);
                    if (settings.mode == WeatheringMode.Dirt)
                        output.ambientOcclusion[index] = Scalar(settings.ambientOcclusion, coverage);
                    else
                        output.metallic[index] = Scalar(settings.metallic, coverage);
                }
            }
            return output;
        }

        private static float Select(SurfaceInputs inputs, Settings settings,
            float u, float v, bool includeNormalDetail)
        {
            float signed = inputs.SignedCurvature(u, v);
            if (includeNormalDetail)
                signed = Mathf.Clamp(signed +
                    inputs.NormalCurvature(u, v) *
                    settings.normalCurvature, -1f, 1f);
            float sourceCavity = inputs.SourceCavity(u, v);
            float meshCavity = inputs.MeshCavity(u, v);
            float cavity = Mathf.Max(sourceCavity, meshCavity);
            float feature = settings.mode == WeatheringMode.Dirt
                ? Mathf.Max(Mathf.Max(0f, -signed), cavity * settings.cavityInfluence)
                : Mathf.Max(0f, signed) *
                  (1f - Mathf.Clamp01(cavity * settings.cavityInfluence));
            return SmoothStep(settings.detectionLevel, 1f, feature);
        }

        private static float SelectNeighbor(SurfaceInputs inputs, Settings settings,
            Color centerId, float u, float v)
        {
            u = Repeat01(u);
            v = Repeat01(v);
            if (!inputs.SameIsland(centerId, inputs.MeshId(u, v))) return 0f;
            return Select(inputs, settings, u, v, false);
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
            context.WriteTileCompact(surfaceId, target.channel,
                new RectInt(0, yStart, target.width, rowCount), pixels, colorSpace,
                TexturePaintPluginBlend.Normal, 1f);
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
            weight = sum <= 0.00001f ? new Vector3(0f, 0f, 1f) : weight / sum;
            Color x = texture.GetPixelBilinear(Repeat01(position.z * scale),
                Repeat01(position.y * scale));
            Color y = texture.GetPixelBilinear(Repeat01(position.x * scale),
                Repeat01(position.z * scale));
            Color z = texture.GetPixelBilinear(Repeat01(position.x * scale),
                Repeat01(position.y * scale));
            return x * weight.x + y * weight.y + z * weight.z;
        }

        private static float SampleMask(TexturePaintReadOnlyParameterTexture texture,
            Vector3 position, Vector3 normal, float u, float v, Settings settings)
        {
            if (texture == null) return 1f;
            Color sample = SampleProjected(texture, position, normal, u, v, settings);
            return Mathf.Clamp01(Luminance(sample) * sample.a);
        }

        private static List<TexturePaintPluginParameterDefinition> BuildParameters(
            WeatheringMode mode)
        {
            bool dirt = mode == WeatheringMode.Dirt;
            string feature = dirt ? "Gap" : "Edge";
            string result = dirt ? "Dirt" : "Wear";
            var parameters = new List<TexturePaintPluginParameterDefinition>
            {
                Enum("projection", "Projection", new[] { "UV", "World Triplanar" }, 1,
                    "Projection used by optional textures, masks, and fractal breakup."),
                Float("textureScale", "Texture Scale", 0.05f, 100f, 4f,
                    "UV repeats or world-space repeats per model unit for optional textures."),
                Integer("seed", "Seed", 0, 100000, dirt ? 317 : 719,
                    "Changes the deterministic fractal pattern."),
                Float("normalCurvature", "Normal Detail Influence", 0f, 4f, 1f,
                    "Adds small curvature features read from the composed Normal channel."),
                Float("featureSize", feature + " Size (px)", 0f, 64f, dirt ? 8f : 5f,
                    "Sampling radius used to find nearby " + feature.ToLowerInvariant() +
                    " features at the current output resolution."),
                Float("detectionLevel", feature + " Detection Level", 0f, 0.95f,
                    dirt ? 0.12f : 0.1f,
                    "Raises the threshold to restrict the effect to stronger " +
                    feature.ToLowerInvariant() + " features."),
                Float("spread", result + " Spread", 0f, 1f, dirt ? 0.75f : 0.6f,
                    "Controls how strongly detected " + feature.ToLowerInvariant() +
                    " features spread across the configured size radius."),
                Float("amount", result + " Level", 0f, 1f, dirt ? 0.72f : 0.58f,
                    "Overall generated " + result.ToLowerInvariant() + " coverage."),
                Float("cavityInfluence", dirt ? "AO / Cavity Level" : "Cavity Exclusion",
                    0f, 2f, dirt ? 0.85f : 1f,
                    dirt ? "Adds source and generated AO to gap detection."
                         : "Prevents edge wear from spreading into occluded cavities."),
                Float("breakup", "Fractal Breakup", 0f, 1f, dirt ? 0.62f : 0.52f,
                    "Breaks coverage into irregular islands using multi-level fractal noise."),
                Float("breakupScale", "Fractal Scale", 0.25f, 256f, dirt ? 22f : 34f,
                    "Base frequency of the fractal breakup."),
                Integer("fractalLevels", "Fractal Levels", 1, 8, 4,
                    "Number of noise octaves; more levels add progressively smaller breakup."),
                Float("fractalPersistence", "Fractal Level Strength", 0.1f, 0.9f, 0.5f,
                    "How strongly each finer fractal level contributes."),
                Float("fractalEdge", "Fractal Edge", 0f, 1f, 0.65f,
                    "Displaces only the boundary of the generated selection for broken edges."),
                ColorParameter("surfaceColor", dirt ? "Dirt Color" : "Exposed / Wear Color",
                    dirt ? new Color(0.16f, 0.105f, 0.055f, 1f)
                         : new Color(0.58f, 0.54f, 0.46f, 1f),
                    "Color multiplied by the optional projected texture."),
                Texture(SurfaceTexture, dirt ? "Dirt Texture" : "Wear Texture",
                    "Optional projected material color and alpha."),
                Texture(SurfaceMask, dirt ? "Dirt Mask" : "Wear Mask",
                    "Optional projected grayscale control mask."),
                Float("roughness", result + " Roughness", 0f, 1f, dirt ? 0.88f : 0.38f,
                    "Roughness beneath generated coverage.")
            };
            if (dirt)
            {
                parameters.Add(Float("ambientOcclusion", "Dirt AO", 0f, 1f, 0.45f,
                    "Ambient-occlusion value beneath generated dirt."));
                parameters.Add(Float("normalAmount", "Dirt Height", 0f, 0.5f, 0.045f,
                    "Raises dirt through Normal Control."));
            }
            else
            {
                parameters.Add(Float("metallic", "Exposed Metallic", 0f, 1f, 0f,
                    "Metallic value beneath worn regions."));
                parameters.Add(Float("normalAmount", "Wear Depth", 0f, 0.5f, 0.065f,
                    "Recesses wear through Normal Control."));
            }
            return parameters;
        }

        private static TexturePaintPluginParameterDefinition Float(string id, string name,
            float minimum, float maximum, float value, string description) => new()
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Float,
                minimum = minimum, maximum = maximum, defaultNumber = value,
                description = description
            };

        private static TexturePaintPluginParameterDefinition Integer(string id, string name,
            int minimum, int maximum, int value, string description) => new()
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Integer,
                minimum = minimum, maximum = maximum, defaultNumber = value,
                description = description
            };

        private static TexturePaintPluginParameterDefinition Enum(string id, string name,
            string[] options, int value, string description) => new()
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Enum,
                minimum = 0f, maximum = options.Length - 1, defaultNumber = value,
                enumOptions = options, description = description
            };

        private static TexturePaintPluginParameterDefinition ColorParameter(string id,
            string name, Color value, string description) => new()
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Color,
                defaultColor = value, description = description
            };

        private static TexturePaintPluginParameterDefinition Texture(string id, string name,
            string description) => new()
            {
                id = id, displayName = name, type = TexturePaintPluginParameterType.Texture,
                description = description
            };

        private static float SmoothStep(float minimum, float maximum, float value)
        {
            float t = Mathf.Clamp01((value - minimum) /
                Mathf.Max(0.00001f, maximum - minimum));
            return t * t * (3f - 2f * t);
        }

        private static float Repeat01(float value) => value - Mathf.Floor(value);
        private static float Luminance(Color value) =>
            value.r * 0.2126f + value.g * 0.7152f + value.b * 0.0722f;
        private static Color MultiplyRGB(Color first, Color second) =>
            new(first.r * second.r, first.g * second.g, first.b * second.b,
                first.a * second.a);
        private static Color Scalar(float value, float alpha) =>
            new(value, value, value, alpha);

        private static Vector3 DecodeNormal(Color value)
        {
            Vector3 normal = new(value.r * 2f - 1f, value.g * 2f - 1f,
                value.b * 2f - 1f);
            return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.forward;
        }

        private sealed class SurfaceInputs
        {
            public readonly TexturePaintReadOnlyImage normal;
            private readonly TexturePaintReadOnlyImage ambientOcclusion;
            private readonly TexturePaintReadOnlyMeshMap position;
            private readonly TexturePaintReadOnlyMeshMap worldNormal;
            private readonly TexturePaintReadOnlyMeshMap signedCurvature;
            private readonly TexturePaintReadOnlyMeshMap meshAmbientOcclusion;
            private readonly TexturePaintReadOnlyMeshMap meshId;

            public SurfaceInputs(TexturePaintCommandContextV2 context, string surfaceId)
            {
                normal = context.source.Get(surfaceId, TexturePaintChannel.Normal);
                ambientOcclusion = context.source.Get(surfaceId,
                    TexturePaintChannel.AmbientOcclusion);
                position = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldPosition);
                worldNormal = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldNormal);
                signedCurvature = context.GetMeshMap(surfaceId,
                    TexturePaintMeshMap.SignedCurvature);
                meshAmbientOcclusion = context.GetMeshMap(surfaceId,
                    TexturePaintMeshMap.AmbientOcclusion);
                meshId = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SurfaceId);
            }

            public bool IsCovered(float u, float v) => meshId == null || MeshId(u, v).a >= 0.5f;
            public Color MeshId(float u, float v) =>
                meshId?.GetPixelBilinear(u, v) ?? Color.white;
            public bool SameIsland(Color first, Color second) => first.a >= 0.5f &&
                second.a >= 0.5f && Mathf.Abs(first.g - second.g) <= 0.1f &&
                Mathf.Abs(first.b - second.b) <= 0.1f;
            public Vector3 WorldPosition(float u, float v)
            {
                Color value = position?.GetPixelBilinear(u, v) ?? new Color(u, v, 0f, 1f);
                return new Vector3(value.r, value.g, value.b);
            }
            public Vector3 WorldNormal(float u, float v) => worldNormal != null
                ? DecodeNormal(worldNormal.GetPixelBilinear(u, v)) : Vector3.forward;
            public float SignedCurvature(float u, float v) => signedCurvature != null
                ? signedCurvature.GetPixelBilinear(u, v).r * 2f - 1f : 0f;
            public float NormalCurvature(float u, float v)
            {
                if (normal == null || normal.width < 2 || normal.height < 2) return 0f;
                float du = 1f / normal.width;
                float dv = 1f / normal.height;
                Color centerId = MeshId(u, v);
                Vector3 center = DecodeNormal(normal.GetPixelBilinear(u, v));
                Vector3 left = SampleNormal(centerId, center, u - du, v);
                Vector3 right = SampleNormal(centerId, center, u + du, v);
                Vector3 down = SampleNormal(centerId, center, u, v - dv);
                Vector3 up = SampleNormal(centerId, center, u, v + dv);
                return Mathf.Clamp(((right.x - left.x) + (up.y - down.y)) * 0.5f,
                    -1f, 1f);
            }
            public float SourceCavity(float u, float v) => ambientOcclusion == null ? 0f
                : 1f - Luminance(ambientOcclusion.GetPixelBilinear(u, v));
            public float MeshCavity(float u, float v) => meshAmbientOcclusion == null ? 0f
                : 1f - meshAmbientOcclusion.GetPixelBilinear(u, v).r;

            private Vector3 SampleNormal(Color centerId, Vector3 fallback, float u, float v)
            {
                u = Repeat01(u);
                v = Repeat01(v);
                if (!SameIsland(centerId, MeshId(u, v))) return fallback;
                return DecodeNormal(normal.GetPixelBilinear(u, v));
            }
        }

        private readonly struct OutputTarget
        {
            public readonly TexturePaintChannel channel;
            public readonly int width;
            public readonly int height;

            private OutputTarget(TexturePaintChannel channel,
                TexturePaintReadOnlyChannelInfo info)
            {
                this.channel = channel;
                width = info.width;
                height = info.height;
            }

            public static List<OutputTarget> Find(TexturePaintReadContextV2 source,
                string surfaceId, WeatheringMode mode)
            {
                var result = new List<OutputTarget>();
                Add(TexturePaintChannel.Albedo);
                Add(TexturePaintChannel.Roughness);
                Add(mode == WeatheringMode.Dirt
                    ? TexturePaintChannel.AmbientOcclusion
                    : TexturePaintChannel.Metallic);
                Add(TexturePaintChannel.NormalControl);
                return result;

                void Add(TexturePaintChannel channel)
                {
                    TexturePaintReadOnlyChannelInfo info =
                        source.GetChannelInfo(surfaceId, channel);
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
            public readonly WeatheringMode mode;
            public readonly bool triplanar;
            public readonly float textureScale;
            public readonly int seed;
            public readonly float normalCurvature;
            public readonly float featureSize;
            public readonly float detectionLevel;
            public readonly float spread;
            public readonly float amount;
            public readonly float cavityInfluence;
            public readonly float breakup;
            public readonly float breakupScale;
            public readonly int fractalLevels;
            public readonly float fractalPersistence;
            public readonly float fractalEdge;
            public readonly Color color;
            public readonly float roughness;
            public readonly float ambientOcclusion;
            public readonly float metallic;
            public readonly float normalValue;

            public Settings(TexturePaintPluginParameterSet values, WeatheringMode mode)
            {
                values ??= new TexturePaintPluginParameterSet();
                this.mode = mode;
                triplanar = values.Integer("projection", 1) == 1;
                textureScale = Mathf.Max(0.05f, values.Float("textureScale", 4f));
                seed = values.Integer("seed", mode == WeatheringMode.Dirt ? 317 : 719);
                normalCurvature = Mathf.Max(0f, values.Float("normalCurvature", 1f));
                featureSize = Mathf.Clamp(values.Float("featureSize",
                    mode == WeatheringMode.Dirt ? 8f : 5f), 0f, 64f);
                detectionLevel = Mathf.Clamp(values.Float("detectionLevel",
                    mode == WeatheringMode.Dirt ? 0.12f : 0.1f), 0f, 0.95f);
                spread = Mathf.Clamp01(values.Float("spread",
                    mode == WeatheringMode.Dirt ? 0.75f : 0.6f));
                amount = Mathf.Clamp01(values.Float("amount",
                    mode == WeatheringMode.Dirt ? 0.72f : 0.58f));
                cavityInfluence = Mathf.Max(0f, values.Float("cavityInfluence",
                    mode == WeatheringMode.Dirt ? 0.85f : 1f));
                breakup = Mathf.Clamp01(values.Float("breakup",
                    mode == WeatheringMode.Dirt ? 0.62f : 0.52f));
                breakupScale = Mathf.Max(0.25f, values.Float("breakupScale",
                    mode == WeatheringMode.Dirt ? 22f : 34f));
                fractalLevels = Mathf.Clamp(values.Integer("fractalLevels", 4), 1, 8);
                fractalPersistence = Mathf.Clamp(values.Float("fractalPersistence", 0.5f),
                    0.1f, 0.9f);
                fractalEdge = Mathf.Clamp01(values.Float("fractalEdge", 0.65f));
                color = values.Color("surfaceColor", mode == WeatheringMode.Dirt
                    ? new Color(0.16f, 0.105f, 0.055f, 1f)
                    : new Color(0.58f, 0.54f, 0.46f, 1f));
                roughness = Mathf.Clamp01(values.Float("roughness",
                    mode == WeatheringMode.Dirt ? 0.88f : 0.38f));
                ambientOcclusion = Mathf.Clamp01(values.Float("ambientOcclusion", 0.45f));
                metallic = Mathf.Clamp01(values.Float("metallic", 0f));
                float normalAmount = Mathf.Clamp(values.Float("normalAmount",
                    mode == WeatheringMode.Dirt ? 0.045f : 0.065f), 0f, 0.5f);
                normalValue = mode == WeatheringMode.Dirt
                    ? 0.5f + normalAmount : 0.5f - normalAmount;
            }
        }
    }
}
