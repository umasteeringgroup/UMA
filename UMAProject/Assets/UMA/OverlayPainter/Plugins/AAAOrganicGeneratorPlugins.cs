using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    public sealed class FabricFuzzGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            AAAOrganicGeneratorEngine.CreateDescriptor(AAAOrganicGeneratorMode.FabricFuzz);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            AAAOrganicGeneratorEngine.ReadChannels(AAAOrganicGeneratorMode.FabricFuzz);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            AAAOrganicGeneratorEngine.ExecuteAsync(context, AAAOrganicGeneratorMode.FabricFuzz);
    }

    public sealed class RustCorrosionGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            AAAOrganicGeneratorEngine.CreateDescriptor(AAAOrganicGeneratorMode.RustCorrosion);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            AAAOrganicGeneratorEngine.ReadChannels(AAAOrganicGeneratorMode.RustCorrosion);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            AAAOrganicGeneratorEngine.ExecuteAsync(context, AAAOrganicGeneratorMode.RustCorrosion);
    }

    public sealed class SurfaceMicroDetailGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            AAAOrganicGeneratorEngine.CreateDescriptor(AAAOrganicGeneratorMode.SurfaceMicroDetail);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            AAAOrganicGeneratorEngine.ReadChannels(AAAOrganicGeneratorMode.SurfaceMicroDetail);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            AAAOrganicGeneratorEngine.ExecuteAsync(context, AAAOrganicGeneratorMode.SurfaceMicroDetail);
    }

    public sealed class VeinsSubdermalGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            AAAOrganicGeneratorEngine.CreateDescriptor(AAAOrganicGeneratorMode.VeinsSubdermal);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            AAAOrganicGeneratorEngine.ReadChannels(AAAOrganicGeneratorMode.VeinsSubdermal);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            AAAOrganicGeneratorEngine.ExecuteAsync(context, AAAOrganicGeneratorMode.VeinsSubdermal);
    }

    public sealed class ScarWoundGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            AAAOrganicGeneratorEngine.CreateDescriptor(AAAOrganicGeneratorMode.ScarWound);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            AAAOrganicGeneratorEngine.ReadChannels(AAAOrganicGeneratorMode.ScarWound);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            AAAOrganicGeneratorEngine.ExecuteAsync(context, AAAOrganicGeneratorMode.ScarWound);
    }

    public sealed class CreatureSkinGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            AAAOrganicGeneratorEngine.CreateDescriptor(AAAOrganicGeneratorMode.CreatureSkin);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            AAAOrganicGeneratorEngine.ReadChannels(AAAOrganicGeneratorMode.CreatureSkin);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            AAAOrganicGeneratorEngine.ExecuteAsync(context, AAAOrganicGeneratorMode.CreatureSkin);
    }

    public sealed class ScratchDentGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            AAAOrganicGeneratorEngine.CreateDescriptor(AAAOrganicGeneratorMode.ScratchDent);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            AAAOrganicGeneratorEngine.ReadChannels(AAAOrganicGeneratorMode.ScratchDent);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            AAAOrganicGeneratorEngine.ExecuteAsync(context, AAAOrganicGeneratorMode.ScratchDent);
    }

    internal enum AAAOrganicGeneratorMode
    {
        FabricFuzz,
        RustCorrosion,
        SurfaceMicroDetail,
        VeinsSubdermal,
        ScarWound,
        CreatureSkin,
        ScratchDent
    }

    /// <summary>
    /// Shared deterministic, tile-streamed foundation for production material generators.
    /// Algorithms use world-space mesh maps when available so detail remains continuous across
    /// UV seams, while SurfaceId prevents bleeding into empty UV space.
    /// </summary>
    internal static class AAAOrganicGeneratorEngine
    {
        private const int RowsPerTile = 128;
        private const string ControlMask = "controlMask";
        private const string GuideTexture = "guideTexture";

        public static TexturePaintChannelMask ReadChannels(AAAOrganicGeneratorMode mode) =>
            mode == AAAOrganicGeneratorMode.ScarWound
                ? TexturePaintChannelMask.Custom : TexturePaintChannelMask.None;

        public static TexturePaintPluginDescriptor CreateDescriptor(AAAOrganicGeneratorMode mode)
        {
            TexturePaintChannelMask writes = DeclaredChannels(mode);
            TexturePaintChannelMask reads = ReadChannels(mode);

            return new TexturePaintPluginDescriptor
            {
                id = Id(mode),
                displayName = DisplayName(mode),
                description = Description(mode),
                pluginVersion = "1.0.0",
                capabilities = TexturePaintPluginCapability.Generator |
                               TexturePaintPluginCapability.ReadsMeshMaps |
                               TexturePaintPluginCapability.LongRunning,
                declaredChannels = writes,
                readChannels = reads,
                requiredMeshMaps = RequiredMeshMaps(mode),
                channelSnapshotMaximumResolution = 4096,
                parameters = Parameters(mode)
            };
        }

        private static TexturePaintMeshMapMask RequiredMeshMaps(AAAOrganicGeneratorMode mode) =>
            mode switch
            {
                AAAOrganicGeneratorMode.FabricFuzz => TexturePaintMeshMapMask.WorldPosition |
                    TexturePaintMeshMapMask.SignedCurvature | TexturePaintMeshMapMask.SurfaceId,
                AAAOrganicGeneratorMode.RustCorrosion => TexturePaintMeshMapMask.WorldPosition |
                    TexturePaintMeshMapMask.SignedCurvature |
                    TexturePaintMeshMapMask.AmbientOcclusion | TexturePaintMeshMapMask.SurfaceId,
                AAAOrganicGeneratorMode.SurfaceMicroDetail => TexturePaintMeshMapMask.WorldPosition |
                    TexturePaintMeshMapMask.SurfaceId,
                AAAOrganicGeneratorMode.VeinsSubdermal => TexturePaintMeshMapMask.WorldPosition |
                    TexturePaintMeshMapMask.Thickness | TexturePaintMeshMapMask.SurfaceId,
                AAAOrganicGeneratorMode.ScarWound => TexturePaintMeshMapMask.WorldPosition |
                    TexturePaintMeshMapMask.SurfaceId,
                AAAOrganicGeneratorMode.ScratchDent => TexturePaintMeshMapMask.WorldPosition |
                    TexturePaintMeshMapMask.WorldNormal |
                    TexturePaintMeshMapMask.SignedCurvature |
                    TexturePaintMeshMapMask.AmbientOcclusion |
                    TexturePaintMeshMapMask.SurfaceId,
                _ => TexturePaintMeshMapMask.WorldPosition | TexturePaintMeshMapMask.Thickness |
                     TexturePaintMeshMapMask.SurfaceId
            };

        private static TexturePaintChannelMask DeclaredChannels(AAAOrganicGeneratorMode mode) =>
            mode switch
            {
                AAAOrganicGeneratorMode.FabricFuzz => TexturePaintChannelMask.Albedo |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.NormalControl |
                    TexturePaintChannelMask.DetailMask,
                AAAOrganicGeneratorMode.RustCorrosion => TexturePaintChannelMask.Albedo |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.Metallic |
                    TexturePaintChannelMask.AmbientOcclusion | TexturePaintChannelMask.NormalControl,
                AAAOrganicGeneratorMode.SurfaceMicroDetail => TexturePaintChannelMask.Albedo |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.NormalControl |
                    TexturePaintChannelMask.DetailMask,
                AAAOrganicGeneratorMode.VeinsSubdermal => TexturePaintChannelMask.Albedo |
                    TexturePaintChannelMask.SkinColorMask | TexturePaintChannelMask.Roughness |
                    TexturePaintChannelMask.Thickness | TexturePaintChannelMask.NormalControl |
                    TexturePaintChannelMask.DetailMask,
                AAAOrganicGeneratorMode.ScarWound => TexturePaintChannelMask.Albedo |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.Thickness |
                    TexturePaintChannelMask.NormalControl | TexturePaintChannelMask.SkinColorMask,
                AAAOrganicGeneratorMode.ScratchDent => TexturePaintChannelMask.Albedo |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.Metallic |
                    TexturePaintChannelMask.AmbientOcclusion | TexturePaintChannelMask.NormalControl,
                _ => TexturePaintChannelMask.Albedo | TexturePaintChannelMask.SkinColorMask |
                    TexturePaintChannelMask.Roughness | TexturePaintChannelMask.Thickness |
                    TexturePaintChannelMask.NormalControl | TexturePaintChannelMask.DetailMask
            };

        public static Task ExecuteAsync(TexturePaintCommandContextV2 context,
            AAAOrganicGeneratorMode mode)
        {
            Settings settings = new Settings(context.parameters, mode);
            TexturePaintReadOnlyParameterTexture control = context.GetTextureParameter(ControlMask);
            TexturePaintReadOnlyParameterTexture guide = context.GetTextureParameter(GuideTexture);

            // The plugin API exposes immutable input snapshots and a thread-safe command buffer.
            // Keep expensive 2K/4K material synthesis off the editor thread so progress,
            // cancellation, and the rest of Overlay Painter remain responsive.
            return Task.Run(() => Execute(context, mode, settings, control, guide),
                context.cancellationToken);
        }

        private static void Execute(TexturePaintCommandContextV2 context,
            AAAOrganicGeneratorMode mode, Settings settings,
            TexturePaintReadOnlyParameterTexture control,
            TexturePaintReadOnlyParameterTexture guide)
        {
            TexturePaintChannelMask declaredChannels = DeclaredChannels(mode);

            int surfaceCount = Mathf.Max(1, context.source.surfaceIds.Count);
            for (int surfaceIndex = 0; surfaceIndex < context.source.surfaceIds.Count; surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];
                var inputs = new SurfaceInputs(context, surfaceId);
                List<OutputTarget> targets = OutputTarget.Find(context.source, surfaceId,
                    declaredChannels);
                if (targets.Count == 0) continue;

                var groups = new Dictionary<long, List<OutputTarget>>();
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    OutputTarget target = targets[targetIndex];
                    long key = ((long)target.width << 32) | (uint)target.height;
                    if (!groups.TryGetValue(key, out List<OutputTarget> group))
                        groups.Add(key, group = new List<OutputTarget>());
                    group.Add(target);
                }

                foreach (List<OutputTarget> group in groups.Values)
                {
                    int width = group[0].width;
                    int height = group[0].height;
                    TexturePaintChannelMask groupChannels = TexturePaintChannelMask.None;
                    for (int targetIndex = 0; targetIndex < group.Count; targetIndex++)
                        groupChannels |= TexturePaintExportTemplate.ToMask(group[targetIndex].channel);
                    for (int y = 0; y < height; y += RowsPerTile)
                    {
                        int rows = Mathf.Min(RowsPerTile, height - y);
                        OutputBuffers output = Generate(inputs, settings, width, height, y, rows,
                            groupChannels, control, guide, context, surfaceIndex, surfaceCount);
                        if (!output.any) continue;
                        for (int targetIndex = 0; targetIndex < group.Count; targetIndex++)
                            Write(context, surfaceId, group[targetIndex], y, rows, output);
                    }
                }
            }
            context.progress?.Report(1f);
        }

        private static OutputBuffers Generate(SurfaceInputs inputs, Settings settings,
            int width, int height, int yStart, int rowCount,
            TexturePaintChannelMask outputChannels,
            TexturePaintReadOnlyParameterTexture control,
            TexturePaintReadOnlyParameterTexture guide,
            TexturePaintCommandContextV2 context, int surfaceIndex, int surfaceCount)
        {
            var output = new OutputBuffers(width * rowCount, outputChannels);
            Parallel.For(0, rowCount, new ParallelOptions
                { CancellationToken = context.cancellationToken }, localY =>
            {
                int y = yStart + localY;
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    if (!inputs.IsCovered(u, v)) continue;
                    Color controlSample = control?.GetPixelBilinear(u, v) ?? Color.white;
                    float controlValue = control == null ? 1f :
                        Mathf.Clamp01(Luminance(controlSample) * controlSample.a);
                    if (controlValue <= 0.0001f) continue;

                    GeneratedPixel pixel = settings.mode switch
                    {
                        AAAOrganicGeneratorMode.FabricFuzz => Fabric(inputs, settings, u, v),
                        AAAOrganicGeneratorMode.RustCorrosion => Rust(inputs, settings, u, v),
                        AAAOrganicGeneratorMode.SurfaceMicroDetail => MicroDetail(inputs, settings, u, v),
                        AAAOrganicGeneratorMode.VeinsSubdermal => Skin(inputs, settings, u, v,
                            width, height, control),
                        AAAOrganicGeneratorMode.ScarWound => Scar(inputs, settings, guide, u, v,
                            width, height),
                        AAAOrganicGeneratorMode.ScratchDent => ScratchDent(inputs, settings, u, v),
                        _ => Creature(inputs, settings, u, v, width, height, control)
                    };
                    pixel.ApplyMask(controlValue * settings.globalAmount);
                    if (!pixel.Any) continue;
                    output.Set(localY * width + x, pixel);
                }
            });
            return output;
        }

        private static GeneratedPixel Fabric(SurfaceInputs input, Settings s, float u, float v)
        {
            Vector3 p = input.Coordinates(u, v, s.worldProjection) * s.scale;
            float presetDensity = s.preset switch { 1 => 1.28f, 2 => 0.82f, 3 => 1.45f, 4 => 0.62f, _ => 1f };
            float presetPilling = s.preset switch { 1 => 1.35f, 2 => 0.72f, 3 => 0.48f, 4 => 0.55f, _ => 1f };
            float presetHeight = s.preset switch { 1 => 1.32f, 2 => 0.72f, 3 => 1.45f, 4 => 0.48f, _ => 1f };
            float radians = s.direction * Mathf.Deg2Rad;
            float ca = Mathf.Cos(radians), sa = Mathf.Sin(radians);
            float along = p.x * ca + p.y * sa;
            float across = -p.x * sa + p.y * ca;
            float warp = Noise.Fbm(p * 0.37f, s.seed + 19, 3, 0.55f) - 0.5f;
            float fibers = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((across + warp * s.directionVariation) *
                Mathf.PI * Mathf.Max(1f, s.fiberFrequency))), 10f);
            fibers *= Mathf.Lerp(0.45f, 1f, Noise.Fbm(new Vector3(along * 2f, across, p.z),
                s.seed + 31, 3, 0.55f));
            float effectiveDensity = Mathf.Clamp01(s.density * presetDensity);
            float fiberCoverage = SmoothStep(1f - effectiveDensity, 1f, fibers);

            float cell = Noise.Worley(p * s.pillScale, s.seed + 101, out float cellRandom);
            float pill = 1f - SmoothStep(0.08f, 0.42f, cell);
            float effectivePilling = Mathf.Clamp01(s.pilling * presetPilling);
            pill *= SmoothStep(1f - effectivePilling, 1f, cellRandom);
            float curvature = Mathf.Abs(input.Curvature(u, v));
            float edgeFray = SmoothStep(0.08f, 0.65f, curvature) * s.edgeAmount;
            edgeFray *= Mathf.Lerp(0.35f, 1f, Noise.Ridged(p * (s.scale * 0.35f + 2f),
                s.seed + 73, 4));
            float coverage = Mathf.Clamp01(fiberCoverage * effectiveDensity +
                                           pill * effectivePilling + edgeFray);
            Color color = Color.Lerp(s.primaryColor, s.secondaryColor,
                Mathf.Clamp01(pill + Noise.Fbm(p * 2.1f, s.seed + 7, 3, 0.5f) * 0.25f));
            return new GeneratedPixel
            {
                albedo = WithAlpha(color, coverage * s.colorStrength),
                roughness = Scalar(s.roughness, coverage),
                normalControl = Scalar(Mathf.Clamp01(0.5f + s.height * presetHeight *
                    Mathf.Lerp(0.35f, 1f, Mathf.Max(fibers, pill))), coverage),
                detailMask = Scalar(Mathf.Clamp01(fibers + pill), coverage)
            };
        }

        private static GeneratedPixel Rust(SurfaceInputs input, Settings s, float u, float v)
        {
            Vector3 p = input.Coordinates(u, v, true) * s.scale;
            float n = Noise.Fbm(p, s.seed, 5, 0.53f);
            float islands = SmoothStep(1f - s.spread, 0.98f, n);
            float cavity = Mathf.Max(input.Cavity(u, v), Mathf.Max(0f, -input.Curvature(u, v)));
            float edge = Mathf.Max(0f, input.Curvature(u, v));
            float concentration = Mathf.Clamp01(islands + cavity * s.cavityAmount +
                                                edge * s.edgeAmount);

            Vector3 gravity = Gravity(s.gravityDirection);
            Vector3 tangent = Vector3.Cross(gravity, Mathf.Abs(gravity.y) > 0.9f ? Vector3.right : Vector3.up);
            Vector3 bitangent = Vector3.Cross(gravity, tangent);
            float lateral = Vector3.Dot(p, tangent) + Noise.Fbm(p * 0.23f, s.seed + 53, 3, 0.55f);
            float vertical = Vector3.Dot(p, gravity);
            float streakLine = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin(lateral * Mathf.PI *
                Mathf.Max(1f, s.streakFrequency))), 8f);
            float streakBreak = Noise.Fbm(new Vector3(lateral, vertical * s.streakLength,
                Vector3.Dot(p, bitangent)), s.seed + 71, 4, 0.58f);
            float streak = streakLine * SmoothStep(0.35f, 0.8f, streakBreak) * s.streaking;

            float pitCell = Noise.Worley(p * s.pitScale, s.seed + 131, out float pitRandom);
            float pits = (1f - SmoothStep(0.03f, 0.32f, pitCell)) *
                         SmoothStep(1f - s.pitting, 1f, pitRandom);
            float flakes = Noise.Ridged(p * s.flakeScale, s.seed + 211, 4) * s.flaking;
            float coverage = Mathf.Clamp01(concentration + streak * (0.35f + concentration) +
                                           pits * s.pitting);
            float depthMix = Mathf.Clamp01(cavity + pits + flakes * 0.35f);
            Color rustColor = Color.Lerp(s.primaryColor, s.secondaryColor, depthMix);
            rustColor = Color.Lerp(rustColor, s.tertiaryColor, Mathf.Clamp01(streak));
            float height = 0.5f + flakes * s.height - pits * s.depth;
            return new GeneratedPixel
            {
                albedo = WithAlpha(rustColor, coverage),
                roughness = Scalar(Mathf.Lerp(s.roughness * 0.8f, s.roughness, flakes), coverage),
                metallic = Scalar(Mathf.Lerp(s.metallic, 0f, Mathf.Clamp01(coverage + pits)), coverage),
                ambientOcclusion = Scalar(Mathf.Lerp(1f, s.aoValue, depthMix), coverage),
                normalControl = Scalar(Mathf.Clamp01(height), coverage)
            };
        }

        private static GeneratedPixel ScratchDent(SurfaceInputs input, Settings s, float u, float v)
        {
            Vector3 p = input.Coordinates(u, v, s.worldProjection) * s.scale;
            Vector3 n = input.Normal(u, v);
            Vector3 weights = new Vector3(Mathf.Pow(Mathf.Abs(n.x), 6f),
                Mathf.Pow(Mathf.Abs(n.y), 6f), Mathf.Pow(Mathf.Abs(n.z), 6f));
            float weightSum = Mathf.Max(0.0001f, weights.x + weights.y + weights.z);
            weights /= weightSum;

            DamageSample damage = default;
            float retainedWeight = 0f;
            if (weights.x >= 0.035f)
            {
                damage.AddWeighted(CombatField(new Vector2(p.y, p.z), s, s.seed + 17), weights.x);
                retainedWeight += weights.x;
            }
            if (weights.y >= 0.035f)
            {
                damage.AddWeighted(CombatField(new Vector2(p.x, p.z), s, s.seed + 97), weights.y);
                retainedWeight += weights.y;
            }
            if (weights.z >= 0.035f)
            {
                damage.AddWeighted(CombatField(new Vector2(p.x, p.y), s, s.seed + 193), weights.z);
                retainedWeight += weights.z;
            }
            damage.Scale(1f / Mathf.Max(0.0001f, retainedWeight));

            float presetAmount = s.preset switch { 0 => 0.48f, 1 => 0.78f, 3 => 1.28f, _ => 1f };
            float curvature = Mathf.Max(0f, input.Curvature(u, v));
            float edgeWear = SmoothStep(0.05f, 0.72f, curvature) * s.edgeBias *
                             Mathf.Lerp(0.45f, 1f, Noise.Ridged(p * 0.72f,
                                 s.seed + 401, 4));
            float cavity = input.Cavity(u, v);

            float dent = Mathf.Clamp01(damage.dent * s.dentAmount * presetAmount);
            float dentRim = Mathf.Clamp01(damage.dentRim * s.dentRimAmount * presetAmount);
            float ping = Mathf.Clamp01(damage.ping * s.pingAmount * presetAmount);
            float pingRim = Mathf.Clamp01(damage.pingRim * s.pingRimAmount * presetAmount);
            float scratch = Mathf.Clamp01(damage.scratch * s.scratchAmount * presetAmount);
            float scrape = Mathf.Clamp01(damage.scrape * s.scrapeAmount * presetAmount);
            float lips = Mathf.Clamp01(damage.lip * s.scratchLip * presetAmount);
            float chip = Mathf.Clamp01(Mathf.Max(damage.chip * s.chipAmount,
                Mathf.Max(ping, Mathf.Max(scratch, scrape)) * s.chipAmount) + edgeWear);

            float recessed = dent * s.dentDepth + ping * s.pingDepth +
                             scratch * s.scratchDepth + scrape * s.scratchDepth * 0.65f;
            float raised = dentRim * s.dentRimHeight + pingRim * s.pingRimHeight +
                           lips * s.scratchLipHeight;
            float coverage = Mathf.Clamp01(Mathf.Max(Mathf.Max(dent, dentRim),
                Mathf.Max(Mathf.Max(ping, pingRim), Mathf.Max(scratch,
                    Mathf.Max(scrape, Mathf.Max(lips, chip))))));
            if (coverage <= 0.0001f) return default;

            Color damageColor = Color.Lerp(s.exposedColor, s.recessColor,
                Mathf.Clamp01(recessed * 3.5f + cavity * s.cavityAmount));
            damageColor = Color.Lerp(damageColor, s.burrColor,
                Mathf.Clamp01(raised * 5f));
            float roughness = Mathf.Lerp(s.exposedRoughness, s.recessRoughness,
                Mathf.Clamp01(recessed * 4f));
            roughness = Mathf.Lerp(roughness, s.burrRoughness,
                Mathf.Clamp01(raised * 5f));
            float metalCoverage = s.armorFinish == 0 ? coverage : chip;
            float colorCoverage = s.armorFinish == 0 ? coverage : Mathf.Clamp01(chip +
                recessed * 0.2f + cavity * coverage * s.cavityAmount);
            return new GeneratedPixel
            {
                albedo = WithAlpha(damageColor, colorCoverage * s.colorStrength),
                roughness = Scalar(Mathf.Clamp01(roughness), coverage),
                metallic = Scalar(s.metallic, metalCoverage),
                ambientOcclusion = Scalar(Mathf.Lerp(1f, s.aoValue,
                    Mathf.Clamp01(recessed * 4f + cavity * s.cavityAmount)),
                    Mathf.Clamp01(recessed + cavity * coverage)),
                normalControl = Scalar(Mathf.Clamp01(0.5f - recessed + raised), coverage)
            };
        }

        private static DamageSample CombatField(Vector2 q, Settings s, int seed)
        {
            DamageSample result = default;
            int bx = Mathf.FloorToInt(q.x);
            int by = Mathf.FloorToInt(q.y);
            float direction = s.direction * Mathf.Deg2Rad;
            float presetDensity = s.preset switch { 0 => 0.45f, 1 => 0.78f, 3 => 1.35f, _ => 1f };

            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int cx = bx + ox;
                int cy = by + oy;
                float randomA = Noise.RandomCell(cx, cy, seed);
                float randomB = Noise.RandomCell(cx, cy, seed + 29);
                float randomC = Noise.RandomCell(cx, cy, seed + 61);
                Vector2 center = new Vector2(cx + Noise.RandomCell(cx, cy, seed + 101),
                    cy + Noise.RandomCell(cx, cy, seed + 137));
                Vector2 delta = q - center;

                int clusterX = Mathf.FloorToInt(cx / 3f);
                int clusterY = Mathf.FloorToInt(cy / 3f);
                float cluster = Mathf.Lerp(0.3f, 1f,
                    Noise.RandomCell(clusterX, clusterY, seed + 173));
                if (randomA < Mathf.Clamp01(s.dentDensity * presetDensity * cluster))
                {
                    float radius = Mathf.Lerp(s.dentSizeMin, s.dentSizeMax, randomB);
                    float polar = Mathf.Atan2(delta.y, delta.x);
                    float lobes = Mathf.Sin(polar * (3f + Mathf.Floor(randomC * 4f)) +
                        randomB * Mathf.PI * 2f) * 0.65f +
                        Mathf.Sin(polar * 7f - randomA * Mathf.PI * 2f) * 0.35f;
                    float breakup = Mathf.Max(0.35f, 1f + lobes * 0.5f *
                        s.dentIrregularity);
                    float d = delta.magnitude * breakup / Mathf.Max(0.01f, radius);
                    float bowl = 1f - SmoothStep(0.08f, 1f, d);
                    float rim = SmoothStep(0.58f, 0.82f, d) *
                                (1f - SmoothStep(0.82f, 1.18f, d));
                    result.dent = Mathf.Max(result.dent, bowl);
                    result.dentRim = Mathf.Max(result.dentRim, rim);

                    Vector2 pingOffset = new Vector2(randomB - 0.5f, randomC - 0.5f) *
                                         radius * 0.35f;
                    float pingDistance = (delta - pingOffset).magnitude /
                                         Mathf.Max(0.005f, s.pingSize * radius);
                    result.ping = Mathf.Max(result.ping, 1f - SmoothStep(0.05f, 0.72f,
                        pingDistance));
                    result.pingRim = Mathf.Max(result.pingRim,
                        SmoothStep(0.48f, 0.78f, pingDistance) *
                        (1f - SmoothStep(0.78f, 1.18f, pingDistance)));
                }

                float scratchChance = Mathf.Clamp01(s.scratchDensity * presetDensity *
                    Mathf.Lerp(0.55f, 1f, cluster));
                if (randomC < scratchChance)
                {
                    float angle = direction + (randomB - 0.5f) * Mathf.PI * s.randomness;
                    Vector2 tangent = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 side = new Vector2(-tangent.y, tangent.x);
                    float halfLength = Mathf.Lerp(s.scratchLengthMin, s.scratchLengthMax,
                        randomA) * 0.5f;
                    float along = Vector2.Dot(delta, tangent);
                    float across = Vector2.Dot(delta, side);
                    float taper = Mathf.Clamp01(1f - Mathf.Abs(along) /
                        Mathf.Max(0.01f, halfLength));
                    int travelSegment = Mathf.FloorToInt((along + 2f) *
                        s.scratchBreakupScale);
                    float broken = SmoothStep(0.2f, 0.62f, Noise.RandomCell(
                        cx * 31 + travelSegment, cy * 37 - travelSegment, seed + 257));
                    float core = (1f - SmoothStep(s.scratchWidth * 0.18f,
                        s.scratchWidth, Mathf.Abs(across))) * taper * broken;
                    float lip = (1f - SmoothStep(s.scratchWidth * 0.32f,
                        s.scratchWidth * 0.8f, Mathf.Abs(Mathf.Abs(across) -
                        s.scratchWidth * 1.3f))) * taper * broken;
                    result.scratch = Mathf.Max(result.scratch, core);
                    result.lip = Mathf.Max(result.lip, lip);
                    result.chip = Mathf.Max(result.chip, core * Mathf.Lerp(0.45f, 1f, randomB));

                    float bundleWidth = Mathf.Max(s.scratchWidth * 3f, s.scrapeSpread);
                    float inBundle = 1f - SmoothStep(bundleWidth * 0.45f,
                        bundleWidth, Mathf.Abs(across));
                    float stripes = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin(across /
                        Mathf.Max(0.001f, bundleWidth) * Mathf.PI * s.scrapeCount)), 10f);
                    result.scrape = Mathf.Max(result.scrape,
                        stripes * inBundle * taper * broken * randomA);
                }
            }
            return result;
        }

        private struct DamageSample
        {
            public float dent, dentRim, ping, pingRim, scratch, scrape, lip, chip;
            public void AddWeighted(DamageSample other, float weight)
            {
                dent += other.dent * weight; dentRim += other.dentRim * weight;
                ping += other.ping * weight; pingRim += other.pingRim * weight;
                scratch += other.scratch * weight; scrape += other.scrape * weight;
                lip += other.lip * weight; chip += other.chip * weight;
            }
            public void Scale(float value)
            {
                dent *= value; dentRim *= value; ping *= value; pingRim *= value;
                scratch *= value; scrape *= value; lip *= value; chip *= value;
            }
        }

        private static GeneratedPixel MicroDetail(SurfaceInputs input, Settings s, float u, float v)
        {
            Vector3 p = input.Coordinates(u, v, s.worldProjection) * s.scale;
            float baseNoise = s.noiseType switch
            {
                0 => Noise.Fbm(p, s.seed, s.octaves, s.persistence),
                1 => Noise.Cell(p, s.seed),
                2 => 1f - Noise.Worley(p, s.seed, out _),
                3 => Noise.Ridged(p, s.seed, s.octaves),
                _ => Mathf.Lerp(Noise.Fbm(p, s.seed, s.octaves, s.persistence),
                    1f - Noise.Worley(p * 0.7f, s.seed + 47, out _), 0.45f)
            };
            float poreCell = Noise.Worley(p * s.poreScale, s.seed + 83, out float poreRandom);
            float pores = (1f - SmoothStep(0.02f, s.poreSize, poreCell)) *
                          SmoothStep(1f - s.poreDensity, 1f, poreRandom);
            float angle = s.direction * Mathf.Deg2Rad;
            float across = -p.x * Mathf.Sin(angle) + p.y * Mathf.Cos(angle);
            float along = p.x * Mathf.Cos(angle) + p.y * Mathf.Sin(angle);
            float scratchLines = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((across +
                Noise.Fbm(p * 0.2f, s.seed + 109, 2, 0.5f) * s.randomness) *
                Mathf.PI * s.scratchFrequency)), Mathf.Lerp(6f, 24f, 1f - s.scratchWidth));
            float scratchBreak = Noise.Value(new Vector3(along * s.scratchLength,
                across * 0.31f, p.z), s.seed + 137);
            float scratches = scratchLines * SmoothStep(1f - s.scratchDensity, 1f, scratchBreak);
            float coverage = Mathf.Clamp01(Mathf.Max(Mathf.Abs(baseNoise - 0.5f) *
                s.noiseAmount * 2f, Mathf.Max(pores * s.poreAmount, scratches * s.scratchAmount)));
            float displacement = (baseNoise - 0.5f) * s.height +
                                 pores * -s.poreDepth + scratches * -s.scratchDepth;
            float rough = Mathf.Clamp01(s.roughness + (baseNoise - 0.5f) *
                s.roughnessVariation + pores * 0.08f);
            return new GeneratedPixel
            {
                albedo = WithAlpha(Color.Lerp(Color.gray, s.primaryColor, baseNoise),
                    coverage * s.colorStrength),
                roughness = Scalar(rough, Mathf.Clamp01(coverage + s.noiseAmount * 0.2f)),
                normalControl = Scalar(Mathf.Clamp01(0.5f + displacement), coverage),
                detailMask = Scalar(Mathf.Clamp01(baseNoise * 0.5f + pores + scratches), coverage)
            };
        }

        private static GeneratedPixel Skin(SurfaceInputs input, Settings s, float u, float v,
            int width, int height, TexturePaintReadOnlyParameterTexture control)
        {
            Vector3 p = input.Coordinates(u, v, s.worldProjection) * s.scale;
            float low = Noise.Fbm(p * 0.22f, s.seed, 5, 0.58f);
            float warp = Noise.Fbm(p * 0.47f, s.seed + 31, 3, 0.55f) - 0.5f;
            float veinA = LineNetwork(p, s.direction, s.veinScale, warp, s.seed + 53);
            float veinB = LineNetwork(p, s.direction + 67f, s.veinScale * 1.8f,
                -warp, s.seed + 97) * s.branching;
            float veins = s.veinsEnabled ? Mathf.Clamp01(Mathf.Max(veinA, veinB) * s.veinIntensity) : 0f;

            float bruiseCell = Noise.Worley(p * s.bruiseScale, s.seed + 131, out float bruiseRandom);
            float bruises = s.bruisesEnabled
                ? (1f - SmoothStep(s.bruiseSize * 0.4f, s.bruiseSize, bruiseCell)) *
                  SmoothStep(1f - s.bruiseAmount, 1f, bruiseRandom) : 0f;
            Color bruiseColor = BruiseColor(s.bruiseAge);

            float spotCell = Noise.Worley(p * s.spotScale, s.seed + 173, out float spotRandom);
            float spots = (1f - SmoothStep(0.03f, s.spotSize, spotCell)) *
                          SmoothStep(1f - s.spotAmount, 1f, spotRandom);
            float freckleCell = Noise.Worley(p * s.freckleScale, s.seed + 227,
                out float freckleRandom);
            float freckles = (1f - SmoothStep(0.015f, s.freckleSize, freckleCell)) *
                             SmoothStep(1f - s.freckleAmount, 1f, freckleRandom);
            float du = s.edgeFadePixels / Mathf.Max(1f, width);
            float dv = s.edgeFadePixels / Mathf.Max(1f, height);
            freckles *= input.BoundaryFade(u, v, du, dv) *
                        MaskBoundaryFade(control, u, v, du, dv);
            float mottling = s.mottlingEnabled
                ? SmoothStep(0.3f, 0.75f, Mathf.Abs(low - 0.5f) * 2f) * s.mottling : 0f;

            float pores = 1f - Noise.Worley(p * s.poreScale, s.seed + 251, out _);
            pores = SmoothStep(0.74f, 0.96f, pores) * s.poreAmount;
            float redness = SmoothStep(0.4f, 0.8f, Noise.Fbm(p * 0.4f,
                s.seed + 281, 4, 0.55f)) * s.redness;
            float oil = SmoothStep(0.42f, 0.78f, Noise.Fbm(p * s.oilScale,
                s.seed + 307, 4, 0.57f)) * s.oiliness;
            float wrinkleWarp = (Noise.Fbm(p * 0.6f, s.seed + 331, 3, 0.55f) - 0.5f) *
                                s.randomness;
            float wrinkles = LineNetwork(p, s.direction + 90f, s.wrinkleScale,
                wrinkleWarp, s.seed + 347) * s.wrinkleAmount;
            float localized = Mathf.Max(veins, bruises);
            localized = Mathf.Max(localized, spots);
            localized = Mathf.Max(localized, freckles);
            localized = Mathf.Max(localized, mottling);
            localized = Mathf.Max(localized, pores);
            localized = Mathf.Max(localized, redness);
            localized = Mathf.Max(localized, oil);
            localized = Mathf.Max(localized, wrinkles);
            float coverage = s.fullSurface ? 1f : Mathf.Clamp01(localized);

            Color baseColor = s.primaryColor;
            Color color = Color.Lerp(baseColor, s.secondaryColor, mottling);
            color = Color.Lerp(color, s.veinColor, veins);
            color = Color.Lerp(color, bruiseColor, bruises * 0.9f);
            float pigmentRandom = Mathf.Lerp(spotRandom, freckleRandom,
                freckles / Mathf.Max(0.0001f, spots + freckles));
            Color pigment = Color.Lerp(s.spotColor, s.spotSecondaryColor,
                pigmentRandom * s.spotColorVariation);
            color = Color.Lerp(color, pigment, Mathf.Clamp01(spots + freckles));
            color = Color.Lerp(color, s.tertiaryColor, redness);
            float baseThickness = Mathf.Lerp(s.thickness, input.Thickness(u, v), 0.15f);
            float thickness = Mathf.Clamp01(baseThickness - veins * s.veinDepth - bruises * 0.08f +
                                            mottling * 0.03f + (low - 0.5f) *
                                            s.thicknessVariation);
            float normal = 0.5f - pores * s.poreDepth + spots * s.spotHeight -
                           wrinkles * s.wrinkleDepth;
            return new GeneratedPixel
            {
                albedo = WithAlpha(color, coverage * (s.fullSurface ? 1f : s.colorStrength)),
                skinColorMask = WithAlpha(color, coverage * s.skinMaskStrength),
                roughness = Scalar(Mathf.Clamp01(s.roughness + pores * 0.06f -
                    bruises * 0.08f - oil * 0.24f),
                    coverage),
                thickness = Scalar(thickness, coverage),
                normalControl = Scalar(Mathf.Clamp01(normal), Mathf.Clamp01(coverage + pores)),
                detailMask = Scalar(Mathf.Clamp01(pores + freckles + wrinkles +
                    mottling * 0.5f), coverage)
            };
        }

        private static GeneratedPixel Scar(SurfaceInputs input, Settings s,
            TexturePaintReadOnlyParameterTexture guideTexture, float u, float v, int width, int height)
        {
            Vector3 p = input.Coordinates(u, v, s.worldProjection) * s.scale;
            float guide;
            if (s.guideSource == 1)
                guide = GuideValue(input.custom, u, v);
            else if (s.guideSource == 2)
                guide = GuideValue(guideTexture, u, v);
            else
                guide = LineNetwork(p, s.direction, s.scarFrequency,
                    (Noise.Fbm(p * 0.35f, s.seed + 17, 4, 0.55f) - 0.5f) * s.randomness,
                    s.seed + 41);

            float profileWidth = s.woundType switch { 1 => 0.72f, 2 => 1.85f, 3 => 0.52f, _ => 1f };
            float du = s.scarWidth * profileWidth / Mathf.Max(1f, width);
            float dv = s.scarWidth * profileWidth / Mathf.Max(1f, height);
            float nearby = guide;
            float outer = guide;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                float sample = s.guideSource == 1
                    ? GuideValue(input.custom, u + Mathf.Cos(a) * du, v + Mathf.Sin(a) * dv)
                    : s.guideSource == 2
                        ? GuideValue(guideTexture, u + Mathf.Cos(a) * du, v + Mathf.Sin(a) * dv)
                        : LineNetwork(input.Coordinates(u + Mathf.Cos(a) * du,
                            v + Mathf.Sin(a) * dv, s.worldProjection) * s.scale,
                            s.direction, s.scarFrequency, 0f, s.seed + 41);
                nearby = Mathf.Max(nearby, sample);
                float farSample = s.guideSource == 1
                    ? GuideValue(input.custom, u + Mathf.Cos(a) * du * 2f,
                        v + Mathf.Sin(a) * dv * 2f)
                    : s.guideSource == 2
                        ? GuideValue(guideTexture, u + Mathf.Cos(a) * du * 2f,
                            v + Mathf.Sin(a) * dv * 2f)
                        : sample;
                outer = Mathf.Max(outer, farSample);
            }
            float irregular = Mathf.Lerp(0.55f, 1.35f, Noise.Fbm(p * s.irregularityScale,
                s.seed + 83, 4, 0.56f));
            float center = SmoothStep(s.guideThreshold, 1f, guide * irregular);
            float rim = Mathf.Clamp01(SmoothStep(s.guideThreshold * 0.55f, 1f, nearby) - center);
            float inflammation = Mathf.Clamp01(SmoothStep(0.05f, 0.8f, outer) - center) *
                                 s.inflammation;
            if (s.woundType == 1) // Fresh cuts: narrow wet center with a stronger inflamed edge.
            {
                center = Mathf.Pow(center, 0.72f);
                inflammation = Mathf.Clamp01(inflammation * 1.6f + rim * 0.25f);
            }
            else if (s.woundType == 2) // Burns: broad, broken rims and recessed tissue.
            {
                rim = Mathf.Clamp01(rim * 1.55f + center * (1f - irregular) * 0.4f);
                inflammation = Mathf.Clamp01(inflammation * 1.25f);
            }
            else if (s.woundType == 3) // Stretch marks: fine, low-inflammation parallel striae.
            {
                center *= Mathf.Lerp(0.62f, 1f, Noise.Ridged(p * 1.7f, s.seed + 191, 3));
                rim *= 0.45f;
                inflammation *= 0.22f;
            }
            float coverage = Mathf.Clamp01(center + rim + inflammation);
            float healed = Mathf.Clamp01(s.scarAge);
            Color inside = Color.Lerp(s.freshColor, s.insideColor, healed);
            Color side = Color.Lerp(s.inflammationColor, s.sideColor, healed);
            Color color = WeightedColor(inside, center, side, rim + inflammation, coverage);
            float typeHeight = s.woundType switch { 1 => 0.75f, 2 => 0.9f, 3 => 0.35f, _ => 1f };
            float signedHeight = s.raisedRecessed * s.height * typeHeight;
            float normal = 0.5f + center * signedHeight + rim * s.rimHeight;
            float roughness = Mathf.Lerp(s.insideRoughness, s.sideRoughness,
                rim / Mathf.Max(0.0001f, center + rim));
            roughness = Mathf.Lerp(roughness, s.healedRoughness, healed);
            float thickness = Mathf.Clamp01(s.thickness + center * s.thicknessImpact);
            return new GeneratedPixel
            {
                albedo = WithAlpha(color, coverage),
                skinColorMask = WithAlpha(color, coverage * s.skinMaskStrength),
                roughness = Scalar(roughness, coverage),
                thickness = Scalar(thickness, coverage),
                normalControl = Scalar(Mathf.Clamp01(normal), coverage)
            };
        }

        private static GeneratedPixel Creature(SurfaceInputs input, Settings s, float u, float v,
            int width, int height, TexturePaintReadOnlyParameterTexture control)
        {
            Vector3 p = input.Coordinates(u, v, s.worldProjection) * s.scale;
            float cell = Noise.Worley(p * s.scaleSize, s.seed, out float cellRandom);
            float scaleInterior = 1f - SmoothStep(s.scaleBorder, s.scaleBorder + 0.15f, cell);
            if (s.patternType == 1) scaleInterior = SmoothStep(0.12f, 0.48f, cell);
            else if (s.patternType == 2) scaleInterior = Mathf.Pow(1f - cell, 2f);
            else if (s.patternType == 3) scaleInterior = Mathf.Lerp(scaleInterior,
                Noise.Ridged(p * s.scaleSize, s.seed + 19, 3), 0.5f);
            else if (s.patternType == 4) scaleInterior = Mathf.Lerp(
                SmoothStep(0.08f, 0.4f, cell),
                Noise.Fbm(p * 0.65f, s.seed + 29, 4, 0.6f), 0.7f);
            float scaleCoverage = Mathf.Clamp01(scaleInterior * s.scaleAmount);
            float mottle = SmoothStep(0.28f, 0.78f, Noise.Fbm(p * s.mottleScale,
                s.seed + 53, 5, 0.58f)) * s.mottling;
            float blotch = SmoothStep(0.45f, 0.82f, Noise.Fbm(p * s.blotchScale,
                s.seed + 89, 4, 0.6f)) * s.blotches;
            float freckleCell = Noise.Worley(p * s.freckleScale, s.seed + 127,
                out float freckleRandom);
            float freckles = (1f - SmoothStep(0.02f, s.freckleSize, freckleCell)) *
                             SmoothStep(1f - s.freckleAmount, 1f, freckleRandom);
            float du = s.edgeFadePixels / Mathf.Max(1f, width);
            float dv = s.edgeFadePixels / Mathf.Max(1f, height);
            freckles *= input.BoundaryFade(u, v, du, dv) *
                        MaskBoundaryFade(control, u, v, du, dv);
            float ageCell = Noise.Worley(p * s.ageSpotScale, s.seed + 163, out float ageRandom);
            float age = (1f - SmoothStep(0.08f, s.ageSpotSize, ageCell)) *
                        SmoothStep(1f - s.ageSpots, 1f, ageRandom);
            float coverage = s.fullSurface ? 1f : Mathf.Clamp01(Mathf.Max(scaleCoverage,
                Mathf.Max(mottle, Mathf.Max(blotch, Mathf.Max(freckles, age)))));
            Color color = Color.Lerp(s.primaryColor, s.secondaryColor,
                Mathf.Clamp01(mottle + cellRandom * s.colorVariation));
            color = Color.Lerp(color, s.tertiaryColor, blotch);
            Color pigment = Color.Lerp(s.spotColor, s.spotSecondaryColor,
                Mathf.Lerp(freckleRandom, ageRandom, age / Mathf.Max(0.0001f, freckles + age)) *
                s.spotColorVariation);
            color = Color.Lerp(color, pigment, Mathf.Clamp01(freckles + age));
            float border = Mathf.Clamp01(1f - scaleInterior) * s.scaleAmount;
            float normal = 0.5f + scaleInterior * s.height - border * s.depth;
            float roughness = Mathf.Clamp01(s.roughness + border * s.roughnessVariation -
                                            scaleInterior * s.scaleGloss);
            float baseThickness = Mathf.Lerp(s.thickness, input.Thickness(u, v), 0.15f);
            float thickness = Mathf.Clamp01(baseThickness + blotch * s.thicknessVariation -
                                            scaleInterior * s.scaleThickness);
            return new GeneratedPixel
            {
                albedo = WithAlpha(color, coverage),
                skinColorMask = WithAlpha(color, coverage * s.skinMaskStrength),
                roughness = Scalar(roughness, coverage),
                thickness = Scalar(thickness, coverage),
                normalControl = Scalar(Mathf.Clamp01(normal), coverage),
                detailMask = Scalar(Mathf.Clamp01(scaleInterior + freckles + age), coverage)
            };
        }

        private static void Write(TexturePaintCommandContextV2 context, string surfaceId,
            OutputTarget target, int yStart, int rowCount, OutputBuffers output)
        {
            Color32[] pixels = output.For(target.channel);
            if (pixels == null) return;
            TexturePaintPluginColorSpace colorSpace = TexturePaintChannelUtility.IsColor(target.channel)
                ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data;
            context.WriteTileCompactOwned(surfaceId, target.channel,
                new RectInt(0, yStart, target.width, rowCount), pixels, colorSpace,
                TexturePaintPluginBlend.Normal, 1f);
        }

        private static string Id(AAAOrganicGeneratorMode mode) => mode switch
        {
            AAAOrganicGeneratorMode.FabricFuzz => "com.uma.texturepaint.fabric-fuzz",
            AAAOrganicGeneratorMode.RustCorrosion => "com.uma.texturepaint.rust-corrosion",
            AAAOrganicGeneratorMode.SurfaceMicroDetail => "com.uma.texturepaint.surface-micro-detail",
            AAAOrganicGeneratorMode.VeinsSubdermal => "com.uma.texturepaint.veins-subdermal",
            AAAOrganicGeneratorMode.ScarWound => "com.uma.texturepaint.scar-wound",
            AAAOrganicGeneratorMode.ScratchDent => "com.uma.texturepaint.scratch-dent",
            _ => "com.uma.texturepaint.creature-skin"
        };

        private static string DisplayName(AAAOrganicGeneratorMode mode) => mode switch
        {
            AAAOrganicGeneratorMode.FabricFuzz => "Fabric — Fuzz, Fiber & Fray",
            AAAOrganicGeneratorMode.RustCorrosion => "Metal — Rust, Oxidation & Corrosion",
            AAAOrganicGeneratorMode.SurfaceMicroDetail => "Surface — Pores, Scratches & Micro Detail",
            AAAOrganicGeneratorMode.VeinsSubdermal => "Skin — Veins, Bruising & Subdermal Variation",
            AAAOrganicGeneratorMode.ScarWound => "Skin — Scar, Wound & Stretch Marks",
            AAAOrganicGeneratorMode.ScratchDent => "Metal — Combat Scratches & Dents",
            _ => "Creature — Scales & Skin Variation"
        };

        private static string Description(AAAOrganicGeneratorMode mode) => mode switch
        {
            AAAOrganicGeneratorMode.FabricFuzz =>
                "World-continuous micro-fibers, directional weave breakup, pilling clusters and curvature-driven edge fray for cloth.",
            AAAOrganicGeneratorMode.RustCorrosion =>
                "Layered oxidation with cavity and edge concentration, directional gravity streaks, flakes, pitting and exposed-metal response.",
            AAAOrganicGeneratorMode.SurfaceMicroDetail =>
                "Production micro-surface breakup combining selectable procedural noise, pores, directional scratches, roughness and height response.",
            AAAOrganicGeneratorMode.VeinsSubdermal =>
                "Anatomical skin variation with branching veins, staged bruising, capillary redness, mottling, pores, spots and edge-faded freckles; supports overlays or a complete skin base.",
            AAAOrganicGeneratorMode.ScarWound =>
                "Layered scar and wound tissue with independent center/rim response, healing, inflammation, thickness and ribbon guidance through the Custom channel.",
            AAAOrganicGeneratorMode.ScratchDent =>
                "Combat-authentic armor damage with clustered dent bowls, impact pings, raised rims, finite tapered gouges, burr lips, glancing scrape bundles, paint chipping and convex-edge wear.",
            _ =>
                "Creature skin system with multiple cellular scale families, mottling, subsurface blotches, freckles, age spots, thickness and micro-surface response."
        };

        private static List<TexturePaintPluginParameterDefinition> Parameters(AAAOrganicGeneratorMode mode)
        {
            var result = new List<TexturePaintPluginParameterDefinition>
            {
                Header("coverage", "Coverage & Placement",
                    "Controls projection, deterministic variation, total strength, and an optional grayscale texture mask."),
                Enum("projection", "Projection", new[] { "UV", "World Triplanar" }, 1,
                    "World projection keeps procedural features continuous across UV seams."),
                Float("scale", "Pattern Frequency", 0.05f, 512f, DefaultScale(mode),
                    "Base repetitions per UV tile, or per meter in World Triplanar mode (Unity 1 unit = 1 meter)."),
                Integer("seed", "Seed", 0, 100000, 317 + (int)mode * 211,
                    "Deterministically changes the generated pattern."),
                Float("globalAmount", "Overall Amount", 0f, 1f, 1f,
                    "Final opacity multiplier for every generated channel."),
                Texture(ControlMask, "Control Mask", "Optional grayscale mask limiting generation."),
            };
            switch (mode)
            {
                case AAAOrganicGeneratorMode.FabricFuzz: AddFabricParameters(result); break;
                case AAAOrganicGeneratorMode.RustCorrosion: AddRustParameters(result); break;
                case AAAOrganicGeneratorMode.SurfaceMicroDetail: AddMicroParameters(result); break;
                case AAAOrganicGeneratorMode.VeinsSubdermal: AddSkinParameters(result); break;
                case AAAOrganicGeneratorMode.ScarWound: AddScarParameters(result); break;
                case AAAOrganicGeneratorMode.ScratchDent: AddScratchDentParameters(result); break;
                default: AddCreatureParameters(result); break;
            }
            return result;
        }

        private static void AddFabricParameters(List<TexturePaintPluginParameterDefinition> p)
        {
            p.Add(Header("fibers", "Fiber Field", "Directional weave, fuzz coverage and material response."));
            p.Add(Enum("preset", "Fabric Family", new[] { "Cotton", "Wool", "Denim", "Velvet", "Synthetic" }, 0,
                "Biases fiber response while retaining all manual controls."));
            p.Add(Float("density", "Fuzz Density", 0f, 1f, 0.58f, "Surface coverage of fine fibers."));
            p.Add(Float("fiberFrequency", "Fiber Frequency", 1f, 128f, 34f, "Number of fine directional fiber bands."));
            p.Add(Float("direction", "Fiber Direction", -180f, 180f, 0f, "Primary fiber direction in degrees."));
            p.Add(Float("directionVariation", "Direction Variation", 0f, 2f, 0.35f, "Low-frequency waviness and cross-fiber breakup."));
            p.Add(Float("edgeAmount", "Edge Fray Amount", 0f, 2f, 0.8f, "Extra loose fibers on curved and worn edges."));
            p.Add(Header("pillingSection", "Pilling & Clumps", "Randomized fiber balls and clustered wear."));
            p.Add(Float("pilling", "Pilling Clusters", 0f, 1f, 0.35f, "Frequency of random pill clusters."));
            p.Add(Float("pillScale", "Pill Size", 0.25f, 64f, 8f, "Cell frequency controlling pill size."));
            p.Add(Header("fabricMaterial", "Material Response", "Color, roughness, micro-height and detail-mask output."));
            p.Add(ColorParameter("primaryColor", "Fiber Color", new Color(0.62f, 0.62f, 0.6f, 1f), "Primary fiber tint."));
            p.Add(ColorParameter("secondaryColor", "Pill / Fray Color", new Color(0.78f, 0.76f, 0.71f, 1f), "Loose and worn fiber tint."));
            p.Add(Float("colorStrength", "Color Strength", 0f, 1f, 0.22f, "Albedo contribution while retaining base fabric."));
            p.Add(Float("roughness", "Fiber Roughness", 0f, 1f, 0.88f, "Roughness within generated fibers."));
            p.Add(Float("height", "Fiber Height", 0f, 0.35f, 0.035f, "Raised Normal Control response."));
        }

        private static void AddRustParameters(List<TexturePaintPluginParameterDefinition> p)
        {
            p.Add(Header("oxidation", "Oxidation Coverage", "Spread, cavity buildup and exposed-edge concentration."));
            p.Add(Float("spread", "Rust Spread", 0f, 1f, 0.62f, "Broad oxidation coverage."));
            p.Add(Float("cavityAmount", "Cavity Concentration", 0f, 2f, 0.9f, "Rust accumulation in concave and occluded regions."));
            p.Add(Float("edgeAmount", "Edge Concentration", 0f, 2f, 0.32f, "Oxidation around exposed convex edges."));
            p.Add(Header("corrosion", "Pitting, Flakes & Streaks", "Multi-scale material breakup and gravity runoff."));
            p.Add(Float("pitting", "Pitting", 0f, 1f, 0.52f, "Density of recessed corrosion pits."));
            p.Add(Float("pitScale", "Pit Scale", 0.25f, 128f, 18f, "Frequency controlling pit size."));
            p.Add(Float("depth", "Pit Depth", 0f, 0.5f, 0.12f, "Recessed Normal Control response."));
            p.Add(Float("flaking", "Flaking", 0f, 1f, 0.48f, "Raised layered oxide flakes."));
            p.Add(Float("flakeScale", "Flake Scale", 0.25f, 128f, 11f, "Frequency of oxide flakes."));
            p.Add(Float("streaking", "Gravity Streaking", 0f, 1f, 0.55f, "Directional rust runoff."));
            p.Add(Float("streakLength", "Streak Length", 0.1f, 20f, 3.5f, "Elongation along gravity."));
            p.Add(Float("streakFrequency", "Streak Frequency", 0.25f, 64f, 7f, "Number of runoff tracks."));
            p.Add(Enum("gravityDirection", "Gravity Direction", new[] { "World -Y", "World +Y", "World -Z", "World +Z", "World -X", "World +X" }, 0, "Direction used for runoff streaks."));
            p.Add(Header("rustMaterial", "Oxide Material", "Layered oxide colors and PBR response."));
            p.Add(ColorParameter("primaryColor", "Fresh Oxide", new Color(0.52f, 0.17f, 0.045f, 1f), "Orange fresh rust."));
            p.Add(ColorParameter("secondaryColor", "Deep Oxide", new Color(0.16f, 0.035f, 0.012f, 1f), "Dark mature rust and pits."));
            p.Add(ColorParameter("tertiaryColor", "Streak Color", new Color(0.29f, 0.075f, 0.02f, 1f), "Runoff stain color."));
            p.Add(Float("roughness", "Oxide Roughness", 0f, 1f, 0.91f, "Roughness of corroded areas."));
            p.Add(Float("metallic", "Residual Metallic", 0f, 1f, 0.08f, "Metallic response remaining beneath oxidation."));
            p.Add(Float("aoValue", "Pit AO", 0f, 1f, 0.35f, "Ambient occlusion inside deep corrosion."));
            p.Add(Float("height", "Flake Height", 0f, 0.5f, 0.08f, "Raised oxide flake response."));
        }

        private static void AddMicroParameters(List<TexturePaintPluginParameterDefinition> p)
        {
            p.Add(Header("noise", "Base Micro Noise", "Multi-octave material breakup beneath pores and scratches."));
            p.Add(Enum("noiseType", "Noise Type", new[] { "Perlin", "Cell", "Voronoi", "Ridged", "Hybrid" }, 4, "Procedural basis."));
            p.Add(Integer("octaves", "Noise Levels", 1, 8, 5, "Number of fractal detail levels."));
            p.Add(Float("persistence", "Fine Detail Strength", 0.1f, 0.9f, 0.52f, "Contribution of progressively finer noise."));
            p.Add(Float("noiseAmount", "Noise Amount", 0f, 1f, 0.35f, "Base micro-breakup strength."));
            p.Add(Header("pores", "Pores", "Cellular pores suitable for skin, leather and porous materials."));
            p.Add(Float("poreAmount", "Pore Amount", 0f, 1f, 0.58f, "Pore contribution."));
            p.Add(Float("poreDensity", "Pore Density", 0f, 1f, 0.65f, "Number of visible pores."));
            p.Add(Float("poreScale", "Pore Scale", 0.25f, 128f, 21f, "Pore frequency."));
            p.Add(Float("poreSize", "Pore Size", 0.03f, 0.75f, 0.24f, "Radius of individual pores."));
            p.Add(Float("poreDepth", "Pore Depth", 0f, 0.35f, 0.045f, "Recessed pore height."));
            p.Add(Header("scratches", "Directional Scratches", "Anisotropic scratches with broken lengths and directional randomness."));
            p.Add(Float("scratchAmount", "Scratch Amount", 0f, 1f, 0.42f, "Scratch contribution."));
            p.Add(Float("scratchDensity", "Scratch Density", 0f, 1f, 0.35f, "Number of visible scratches."));
            p.Add(Float("scratchFrequency", "Scratch Frequency", 0.25f, 128f, 17f, "Cross-axis scratch spacing."));
            p.Add(Float("scratchLength", "Scratch Length", 0.1f, 30f, 5f, "Directional segment length."));
            p.Add(Float("scratchWidth", "Scratch Width", 0f, 1f, 0.22f, "Width of individual scratches."));
            p.Add(Float("scratchDepth", "Scratch Depth", 0f, 0.35f, 0.055f, "Recessed scratch response."));
            p.Add(Float("direction", "Scratch Direction", -180f, 180f, 20f, "Primary direction in degrees."));
            p.Add(Float("randomness", "Direction Randomness", 0f, 2f, 0.35f, "Waviness and directional breakup."));
            p.Add(Header("microMaterial", "Material Response", "Subtle color, roughness and Normal Control breakup."));
            p.Add(ColorParameter("primaryColor", "Noise Tint", new Color(0.48f, 0.48f, 0.48f, 1f), "Optional micro color tint."));
            p.Add(Float("colorStrength", "Color Strength", 0f, 1f, 0.04f, "Albedo contribution."));
            p.Add(Float("roughness", "Base Roughness", 0f, 1f, 0.55f, "Center roughness value."));
            p.Add(Float("roughnessVariation", "Roughness Variation", 0f, 1f, 0.18f, "Noise-driven roughness range."));
            p.Add(Float("height", "Noise Height", 0f, 0.35f, 0.025f, "Base Normal Control variation."));
        }

        private static void AddSkinParameters(List<TexturePaintPluginParameterDefinition> p)
        {
            p.Add(Header("skinBase", "Skin Foundation", "Generate only variation over existing skin, or author a complete procedural skin layer."));
            p.Add(Enum("surfaceMode", "Skin Mode", new[] { "Overlay Existing Skin", "Full Skin Layer" }, 0, "Full mode supplies continuous skin color, roughness and thickness."));
            p.Add(ColorParameter("primaryColor", "Base Skin Color", new Color(0.58f, 0.31f, 0.22f, 1f), "Foundation skin tone."));
            p.Add(ColorParameter("secondaryColor", "Mottle Color", new Color(0.48f, 0.21f, 0.17f, 1f), "Cool or dark mottling tone."));
            p.Add(ColorParameter("tertiaryColor", "Redness Color", new Color(0.72f, 0.19f, 0.16f, 1f), "Capillary redness and irritation."));
            p.Add(Float("colorStrength", "Albedo Strength", 0f, 1f, 0.72f, "Albedo contribution."));
            p.Add(Float("skinMaskStrength", "Skin Mask Strength", 0f, 1f, 0.65f, "Skin Color Mask contribution."));
            p.Add(Float("roughness", "Skin Roughness", 0f, 1f, 0.56f, "Base skin roughness."));
            p.Add(Float("thickness", "Base Thickness", 0f, 1f, 0.62f, "Base subsurface thickness."));
            p.Add(Header("veins", "Veins & Capillaries", "Branching subdermal vessels with depth and direction controls."));
            p.Add(Boolean("veinsEnabled", "Veins", true, "Enable procedural vein networks."));
            p.Add(Float("veinIntensity", "Vein Intensity", 0f, 1f, 0.55f, "Visibility of veins."));
            p.Add(Float("veinScale", "Vein Thickness", 0.25f, 64f, 7f, "Network frequency and apparent thickness."));
            p.Add(Float("branching", "Vein Branching", 0f, 1f, 0.65f, "Secondary vessel network."));
            p.Add(Float("direction", "Vein Direction", -180f, 180f, 18f, "Primary flow direction."));
            p.Add(ColorParameter("veinColor", "Vein Color", new Color(0.12f, 0.23f, 0.34f, 1f), "Subdermal vessel color."));
            p.Add(Float("veinDepth", "Vein Depth", 0f, 0.5f, 0.08f, "Thickness reduction beneath veins."));
            p.Add(Header("bruising", "Bruising", "Staged bruise coloration and variable clustered sizes."));
            p.Add(Boolean("bruisesEnabled", "Bruises", false, "Enable randomized bruise clusters."));
            p.Add(Float("bruiseAmount", "Bruise Amount", 0f, 1f, 0.22f, "Frequency of bruises."));
            p.Add(Float("bruiseAge", "Bruise Age", 0f, 1f, 0.35f, "Fresh red-purple through healing green-yellow-brown."));
            p.Add(Float("bruiseScale", "Bruise Spacing", 0.1f, 32f, 1.8f, "Cluster frequency."));
            p.Add(Float("bruiseSize", "Bruise Size", 0.05f, 1f, 0.55f, "Radius of bruise clusters."));
            p.Add(Header("skinVariation", "Mottling, Spots & Freckles", "Multi-scale pigment and subdermal breakup."));
            p.Add(Boolean("mottlingEnabled", "Mottling", true, "Enable low-frequency skin mottling."));
            p.Add(Float("mottling", "Mottling Intensity", 0f, 1f, 0.3f, "Mottle visibility."));
            p.Add(Float("spotAmount", "Subdermal Spots", 0f, 1f, 0.22f, "Small surface and subdermal spots."));
            p.Add(Float("spotScale", "Spot Scale", 0.25f, 128f, 12f, "Spot frequency."));
            p.Add(Float("spotSize", "Spot Size", 0.02f, 0.8f, 0.24f, "Spot radius."));
            p.Add(ColorParameter("spotColor", "Spot / Freckle Color", new Color(0.24f, 0.095f, 0.055f, 1f), "Pigment spot color."));
            p.Add(ColorParameter("spotSecondaryColor", "Spot Color Variation", new Color(0.42f, 0.17f, 0.09f, 1f), "Secondary deterministic pigment color."));
            p.Add(Float("spotColorVariation", "Spot Color Randomness", 0f, 1f, 0.35f, "Per-spot blend between the two pigment colors."));
            p.Add(Float("spotHeight", "Spot Height", 0f, 0.2f, 0.008f, "Subtle raised spot response."));
            p.Add(Float("freckleAmount", "Freckles", 0f, 1f, 0.35f, "Freckle density."));
            p.Add(Float("freckleScale", "Freckle Scale", 0.25f, 256f, 28f, "Freckle frequency."));
            p.Add(Float("freckleSize", "Freckle Size", 0.01f, 0.5f, 0.12f, "Freckle radius."));
            p.Add(Float("edgeFadePixels", "Freckle Edge Fade (px)", 0f, 128f, 24f, "Shrinks and fades freckles near the painted/surface boundary."));
            p.Add(Header("skinMicro", "Pores & Circulation", "Fine pore relief and natural capillary redness."));
            p.Add(Float("poreAmount", "Pore Amount", 0f, 1f, 0.45f, "Fine skin pores."));
            p.Add(Float("poreScale", "Pore Scale", 1f, 256f, 42f, "Pore frequency."));
            p.Add(Float("poreDepth", "Pore Depth", 0f, 0.2f, 0.022f, "Recessed pore response."));
            p.Add(Float("redness", "Capillary Redness", 0f, 1f, 0.22f, "Broad circulation variation."));
            p.Add(Float("oiliness", "Oiliness", 0f, 1f, 0.22f, "Localized roughness reduction for natural sebaceous variation."));
            p.Add(Float("oilScale", "Oil Zone Scale", 0.05f, 32f, 0.7f, "Size of oilier skin regions."));
            p.Add(Float("wrinkleAmount", "Fine Wrinkles", 0f, 1f, 0.14f, "Fine directional skin crease contribution."));
            p.Add(Float("wrinkleScale", "Wrinkle Scale", 0.25f, 128f, 18f, "Frequency of fine skin creases."));
            p.Add(Float("wrinkleDepth", "Wrinkle Depth", 0f, 0.2f, 0.012f, "Recessed wrinkle Normal Control response."));
            p.Add(Float("thicknessVariation", "Thickness Variation", 0f, 0.5f, 0.08f, "Low-frequency subsurface thickness breakup."));
        }

        private static void AddScarParameters(List<TexturePaintPluginParameterDefinition> p)
        {
            p.Add(Header("scarGuide", "Scar Placement & Ribbon Guide", "For ribbon control, paint a white Path/Ribbon into the Custom channel below this Plugin layer, then select Custom Ribbon Channel."));
            p.Add(Enum("guideSource", "Guide Source", new[] { "Procedural Scars", "Custom Ribbon Channel", "Guide Texture" }, 0, "Placement source. Custom is intended for editable ribbon/path control."));
            p.Add(Texture(GuideTexture, "Guide Texture", "Optional grayscale scar centerline when Guide Source is Guide Texture."));
            p.Add(Enum("woundType", "Damage Type", new[] { "Healed Scar", "Fresh Cut", "Burn", "Stretch Marks" }, 0, "Shapes material defaults for different tissue damage."));
            p.Add(Float("scarWidth", "Scar Width (px)", 0.5f, 128f, 8f, "Rim sampling radius around the guide."));
            p.Add(Float("guideThreshold", "Guide Threshold", 0f, 1f, 0.2f, "Minimum guide intensity."));
            p.Add(Float("scarFrequency", "Procedural Frequency", 0.25f, 128f, 5f, "Frequency of procedural scars."));
            p.Add(Float("direction", "Direction", -180f, 180f, 12f, "Primary procedural scar direction."));
            p.Add(Float("randomness", "Path Randomness", 0f, 2f, 0.55f, "Irregular wandering of the centerline."));
            p.Add(Float("irregularityScale", "Edge Irregularity", 0.25f, 128f, 14f, "Fractal rim breakup frequency."));
            p.Add(Header("healing", "Age, Tissue & Color", "Independent center, side and inflammation response."));
            p.Add(Float("scarAge", "Scar Age", 0f, 1f, 0.7f, "Fresh wound through mature healed tissue."));
            p.Add(ColorParameter("freshColor", "Fresh Interior", new Color(0.34f, 0.025f, 0.02f, 1f), "Fresh wound center."));
            p.Add(ColorParameter("insideColor", "Healed Interior", new Color(0.58f, 0.28f, 0.26f, 1f), "Mature scar center."));
            p.Add(ColorParameter("sideColor", "Scar Sides", new Color(0.68f, 0.39f, 0.35f, 1f), "Raised side tissue."));
            p.Add(ColorParameter("inflammationColor", "Inflammation", new Color(0.72f, 0.08f, 0.055f, 1f), "Outer irritated tissue."));
            p.Add(Float("inflammation", "Inflammation Amount", 0f, 1f, 0.28f, "Outer redness halo."));
            p.Add(Float("skinMaskStrength", "Skin Mask Strength", 0f, 1f, 0.6f, "Skin Color Mask output."));
            p.Add(Header("scarMaterial", "Depth, Thickness & Surface", "Independent inside shine, rim roughness, raised/recessed tissue and thickness."));
            p.Add(Float("raisedRecessed", "Raised / Recessed", -1f, 1f, 0.35f, "Negative recesses the center; positive raises it."));
            p.Add(Float("height", "Center Height", 0f, 0.5f, 0.12f, "Center displacement magnitude."));
            p.Add(Float("rimHeight", "Rim Height", 0f, 0.5f, 0.08f, "Raised side tissue."));
            p.Add(Float("insideRoughness", "Inside Roughness", 0f, 1f, 0.28f, "Fresh or polished scar center; lower is shinier."));
            p.Add(Float("sideRoughness", "Side Roughness", 0f, 1f, 0.62f, "Rim tissue roughness."));
            p.Add(Float("healedRoughness", "Healed Roughness", 0f, 1f, 0.48f, "Mature scar roughness."));
            p.Add(Float("thickness", "Scar Thickness", 0f, 1f, 0.5f, "Base thickness output."));
            p.Add(Float("thicknessImpact", "Thickness Change", -0.5f, 0.5f, -0.12f, "Center thickness change."));
        }

        private static void AddScratchDentParameters(List<TexturePaintPluginParameterDefinition> p)
        {
            p.Add(Header("combatProfile", "Combat Damage Profile",
                "Feature-based damage distribution with clustered impacts instead of uniform surface noise."));
            p.Add(Enum("preset", "Wear History", new[]
            {
                "Light Skirmish", "Campaign Worn", "Veteran", "Battle-Ruined"
            }, 2, "Scales feature frequency and intensity while retaining all manual controls."));
            p.Add(Enum("armorFinish", "Armor Finish", new[] { "Bare Metal", "Painted / Coated" }, 1,
                "Painted armor exposes metallic response only where impacts chip through the finish."));

            p.Add(Header("dents", "Dents & Displaced Metal",
                "Broad eased impact bowls with irregular silhouettes and physically raised rims."));
            p.Add(Float("dentAmount", "Dent Amount", 0f, 1f, 0.72f, "Strength of broad impact dents."));
            p.Add(Float("dentDensity", "Dent Frequency", 0f, 1f, 0.42f, "Chance of an impact in each procedural region."));
            p.Add(Float("dentSizeMin", "Minimum Dent Size", 0.03f, 0.9f, 0.13f, "Smallest impact radius relative to pattern scale."));
            p.Add(Float("dentSizeMax", "Maximum Dent Size", 0.03f, 1.25f, 0.48f, "Largest impact radius relative to pattern scale."));
            p.Add(Float("dentDepth", "Dent Depth", 0f, 0.45f, 0.16f, "Recessed Normal Control response at the impact center."));
            p.Add(Float("dentIrregularity", "Dent Irregularity", 0f, 1.5f, 0.5f, "Breaks perfect circles into believable displaced sheet metal."));
            p.Add(Float("dentRimAmount", "Raised Dent Rim", 0f, 1f, 0.72f, "Amount of displaced metal around dent shoulders."));
            p.Add(Float("dentRimHeight", "Dent Rim Height", 0f, 0.25f, 0.055f, "Raised Normal Control response around dents."));

            p.Add(Header("pings", "Pings & Point Impacts",
                "Small hard projectile or weapon-tip craters nested naturally inside larger impacts."));
            p.Add(Float("pingAmount", "Ping Amount", 0f, 1f, 0.58f, "Visibility of concentrated impact pings."));
            p.Add(Float("pingSize", "Ping Size", 0.03f, 0.75f, 0.2f, "Ping radius relative to its parent dent."));
            p.Add(Float("pingDepth", "Ping Depth", 0f, 0.5f, 0.2f, "Sharp central recess depth."));
            p.Add(Float("pingRimAmount", "Ping Crater Rim", 0f, 1f, 0.7f, "Raised ring around point impacts."));
            p.Add(Float("pingRimHeight", "Ping Rim Height", 0f, 0.25f, 0.045f, "Raised point-impact rim height."));

            p.Add(Header("combatScratches", "Gouges & Scratches",
                "Finite tapered cuts with broken travel, directional bias, exposed cores and raised burr lips."));
            p.Add(Float("scratchAmount", "Scratch Amount", 0f, 1f, 0.68f, "Overall gouge contribution."));
            p.Add(Float("scratchDensity", "Scratch Frequency", 0f, 1f, 0.52f, "Chance of a scratch in each damage region."));
            p.Add(Float("scratchLengthMin", "Minimum Length", 0.05f, 1.8f, 0.2f, "Shortest scratch length relative to pattern scale."));
            p.Add(Float("scratchLengthMax", "Maximum Length", 0.05f, 1.8f, 1.25f, "Longest scratch length relative to pattern scale."));
            p.Add(Float("scratchWidth", "Gouge Width", 0.002f, 0.2f, 0.018f, "Width of the recessed scratch core."));
            p.Add(Float("scratchDepth", "Gouge Depth", 0f, 0.35f, 0.075f, "Recessed Normal Control response."));
            p.Add(Float("direction", "Dominant Strike Direction", -180f, 180f, -18f, "Dominant travel angle for weapon and glancing strikes."));
            p.Add(Float("randomness", "Direction Variation", 0f, 2f, 0.75f, "Random angular variation around the dominant strike direction."));
            p.Add(Float("scratchBreakupScale", "Travel Breakup", 0.25f, 40f, 7f, "Breaks scratches along their length instead of producing perfect lines."));
            p.Add(Float("scratchLip", "Raised Burr Amount", 0f, 1f, 0.7f, "Displaced metal lips along the sides of deep gouges."));
            p.Add(Float("scratchLipHeight", "Burr Height", 0f, 0.2f, 0.035f, "Raised Normal Control response along scratch sides."));

            p.Add(Header("glancing", "Glancing Scrapes & Edge Damage",
                "Parallel scrape bundles, chipped coatings, and concentrated wear on exposed convex edges."));
            p.Add(Float("scrapeAmount", "Glancing Scrapes", 0f, 1f, 0.35f, "Parallel scratch bundles from sliding impacts."));
            p.Add(Integer("scrapeCount", "Scratches per Scrape", 2, 12, 4, "Number of fine tracks in each glancing bundle."));
            p.Add(Float("scrapeSpread", "Scrape Spread", 0.01f, 0.6f, 0.12f, "Width occupied by a glancing scrape bundle."));
            p.Add(Float("edgeBias", "Convex Edge Concentration", 0f, 2f, 0.55f, "Adds chipped combat wear to exposed armor edges."));
            p.Add(Float("chipAmount", "Paint Chipping", 0f, 1f, 0.7f, "Exposes metal within hard pings, gouges, scrapes and edge wear."));

            p.Add(Header("damageMaterial", "Exposed Metal & Recess Response",
                "Coordinates color, roughness, metallic, AO and Normal Control across every damage feature."));
            p.Add(ColorParameter("exposedColor", "Exposed Metal", new Color(0.42f, 0.45f, 0.48f, 1f), "Fresh metal visible through chipped finish."));
            p.Add(ColorParameter("recessColor", "Recess / Embedded Grime", new Color(0.075f, 0.065f, 0.055f, 1f), "Dark compacted material in deep damage."));
            p.Add(ColorParameter("burrColor", "Polished Burr", new Color(0.68f, 0.7f, 0.72f, 1f), "Bright metal on raised and freshly abraded lips."));
            p.Add(Float("colorStrength", "Color Contribution", 0f, 1f, 0.72f, "Albedo contribution over the existing armor finish."));
            p.Add(Float("exposedRoughness", "Exposed Roughness", 0f, 1f, 0.3f, "Roughness of freshly exposed metal."));
            p.Add(Float("recessRoughness", "Recess Roughness", 0f, 1f, 0.72f, "Roughness inside dents and gouges."));
            p.Add(Float("burrRoughness", "Burr Roughness", 0f, 1f, 0.2f, "Lower values produce polished raised lips."));
            p.Add(Float("metallic", "Exposed Metallic", 0f, 1f, 0.92f, "Metallic value written to exposed regions."));
            p.Add(Float("aoValue", "Deep Damage AO", 0f, 1f, 0.28f, "Ambient occlusion value inside deep impacts."));
            p.Add(Float("cavityAmount", "Embedded Grime", 0f, 2f, 0.35f, "Uses mesh cavities to darken and roughen established damage."));
        }

        private static void AddCreatureParameters(List<TexturePaintPluginParameterDefinition> p)
        {
            p.Add(Header("creatureBase", "Creature Skin Foundation", "Overlay existing material or generate a full creature skin layer."));
            p.Add(Enum("surfaceMode", "Skin Mode", new[] { "Overlay Existing Skin", "Full Skin Layer" }, 0, "Full mode supplies continuous color and material response."));
            p.Add(Enum("patternType", "Scale Family", new[] { "Reptile Plates", "Pebbled", "Overlapping Scales", "Dragon / Armored", "Amphibian" }, 0, "Cellular pattern family."));
            p.Add(Float("scaleAmount", "Scale Amount", 0f, 1f, 0.78f, "Scale visibility."));
            p.Add(Float("scaleSize", "Scale Size", 0.2f, 128f, 7f, "Cell frequency controlling scale size."));
            p.Add(Float("scaleBorder", "Scale Border", 0.01f, 0.8f, 0.2f, "Width of recessed borders."));
            p.Add(Float("colorVariation", "Per-Scale Color Variation", 0f, 1f, 0.28f, "Deterministic cell color variation."));
            p.Add(Header("creatureVariation", "Mottling, Blotches & Spots", "Pigment and subdermal variation at several scales."));
            p.Add(Float("mottling", "Mottling", 0f, 1f, 0.42f, "Medium-scale pigment variation."));
            p.Add(Float("mottleScale", "Mottle Scale", 0.1f, 64f, 1.8f, "Mottle frequency."));
            p.Add(Float("blotches", "Subsurface Blotches", 0f, 1f, 0.35f, "Broad under-skin variation."));
            p.Add(Float("blotchScale", "Blotch Scale", 0.1f, 64f, 0.8f, "Blotch frequency."));
            p.Add(Float("freckleAmount", "Procedural Freckles", 0f, 1f, 0.3f, "Small pigment spot density."));
            p.Add(Float("freckleScale", "Freckle Scale", 0.25f, 256f, 24f, "Freckle frequency."));
            p.Add(Float("freckleSize", "Freckle Size", 0.01f, 0.5f, 0.13f, "Freckle radius."));
            p.Add(Float("ageSpots", "Age Spots", 0f, 1f, 0.2f, "Larger irregular pigment spots."));
            p.Add(Float("ageSpotScale", "Age Spot Scale", 0.1f, 64f, 5f, "Age spot frequency."));
            p.Add(Float("ageSpotSize", "Age Spot Size", 0.03f, 0.8f, 0.3f, "Age spot radius."));
            p.Add(ColorParameter("spotColor", "Spot Color", new Color(0.075f, 0.05f, 0.025f, 1f), "Primary freckle and age-spot pigment."));
            p.Add(ColorParameter("spotSecondaryColor", "Spot Color Variation", new Color(0.42f, 0.17f, 0.09f, 1f), "Secondary deterministic pigment color."));
            p.Add(Float("spotColorVariation", "Spot Color Randomness", 0f, 1f, 0.35f, "Per-spot blend between the pigment colors."));
            p.Add(Float("edgeFadePixels", "Freckle Edge Fade (px)", 0f, 128f, 24f, "Shrinks and fades freckles near surface and painted-mask boundaries."));
            p.Add(Header("creatureMaterial", "Color & PBR Response", "Albedo, roughness, thickness and Normal Control response."));
            p.Add(ColorParameter("primaryColor", "Primary Skin", new Color(0.16f, 0.3f, 0.13f, 1f), "Base creature skin color."));
            p.Add(ColorParameter("secondaryColor", "Scale Variation", new Color(0.3f, 0.42f, 0.18f, 1f), "Per-scale and mottle variation."));
            p.Add(ColorParameter("tertiaryColor", "Subsurface Blotch", new Color(0.18f, 0.12f, 0.22f, 1f), "Subdermal blotch color."));
            p.Add(Float("skinMaskStrength", "Skin Mask Strength", 0f, 1f, 0.65f, "Skin Color Mask output."));
            p.Add(Float("roughness", "Base Roughness", 0f, 1f, 0.58f, "Base surface roughness."));
            p.Add(Float("roughnessVariation", "Border Roughness", 0f, 1f, 0.18f, "Additional border roughness."));
            p.Add(Float("scaleGloss", "Scale Gloss", 0f, 1f, 0.12f, "Reduced roughness on scale faces."));
            p.Add(Float("height", "Scale Height", 0f, 0.5f, 0.1f, "Raised scale faces."));
            p.Add(Float("depth", "Border Depth", 0f, 0.5f, 0.06f, "Recessed scale borders."));
            p.Add(Float("thickness", "Base Thickness", 0f, 1f, 0.55f, "Base subsurface thickness."));
            p.Add(Float("thicknessVariation", "Blotch Thickness", 0f, 0.5f, 0.12f, "Subsurface blotch thickness change."));
            p.Add(Float("scaleThickness", "Scale Thickness", 0f, 0.5f, 0.06f, "Thickness reduction across armored scale faces."));
        }

        private static float DefaultScale(AAAOrganicGeneratorMode mode) => mode switch
        {
            AAAOrganicGeneratorMode.FabricFuzz => 18f,
            AAAOrganicGeneratorMode.RustCorrosion => 4f,
            AAAOrganicGeneratorMode.SurfaceMicroDetail => 24f,
            AAAOrganicGeneratorMode.VeinsSubdermal => 4f,
            AAAOrganicGeneratorMode.ScarWound => 3f,
            AAAOrganicGeneratorMode.ScratchDent => 5f,
            _ => 3f
        };

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
        private static TexturePaintPluginParameterDefinition Integer(string id, string name,
            int min, int max, int value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Integer, minimum = min, maximum = max,
                defaultNumber = value
            };
        private static TexturePaintPluginParameterDefinition Boolean(string id, string name,
            bool value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Boolean, defaultBoolean = value
            };
        private static TexturePaintPluginParameterDefinition ColorParameter(string id, string name,
            Color value, string description) => new()
            {
                id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Color, defaultColor = value
            };
        private static TexturePaintPluginParameterDefinition Texture(string id, string name,
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

        private readonly struct OutputTarget
        {
            public readonly TexturePaintChannel channel;
            public readonly int width;
            public readonly int height;
            private OutputTarget(TexturePaintChannel channel, TexturePaintReadOnlyChannelInfo info)
            { this.channel = channel; width = info.width; height = info.height; }

            public static List<OutputTarget> Find(TexturePaintReadContextV2 source,
                string surfaceId, TexturePaintChannelMask channels)
            {
                var result = new List<OutputTarget>();
                foreach (TexturePaintChannel channel in System.Enum.GetValues(typeof(TexturePaintChannel)))
                {
                    if ((channels & TexturePaintExportTemplate.ToMask(channel)) == 0) continue;
                    TexturePaintReadOnlyChannelInfo info = source.GetChannelInfo(surfaceId, channel);
                    if (info != null) result.Add(new OutputTarget(channel, info));
                }
                return result;
            }
        }

        private sealed class OutputBuffers
        {
            private readonly Color32[] albedo, metallic, roughness, ao, skinColorMask,
                thickness, detailMask, normalControl;
            public bool any;
            public OutputBuffers(int count, TexturePaintChannelMask channels)
            {
                albedo = Has(channels, TexturePaintChannel.Albedo) ? new Color32[count] : null;
                metallic = Has(channels, TexturePaintChannel.Metallic) ? new Color32[count] : null;
                roughness = Has(channels, TexturePaintChannel.Roughness) ? new Color32[count] : null;
                ao = Has(channels, TexturePaintChannel.AmbientOcclusion) ? new Color32[count] : null;
                skinColorMask = Has(channels, TexturePaintChannel.SkinColorMask) ? new Color32[count] : null;
                thickness = Has(channels, TexturePaintChannel.Thickness) ? new Color32[count] : null;
                detailMask = Has(channels, TexturePaintChannel.DetailMask) ? new Color32[count] : null;
                normalControl = Has(channels, TexturePaintChannel.NormalControl) ? new Color32[count] : null;
            }
            public void Set(int index, GeneratedPixel p)
            {
                if (albedo != null) albedo[index] = p.albedo;
                if (metallic != null) metallic[index] = p.metallic;
                if (roughness != null) roughness[index] = p.roughness;
                if (ao != null) ao[index] = p.ambientOcclusion;
                if (skinColorMask != null) skinColorMask[index] = p.skinColorMask;
                if (thickness != null) thickness[index] = p.thickness;
                if (detailMask != null) detailMask[index] = p.detailMask;
                if (normalControl != null) normalControl[index] = p.normalControl;
                any = true;
            }
            private static bool Has(TexturePaintChannelMask channels, TexturePaintChannel channel) =>
                (channels & TexturePaintExportTemplate.ToMask(channel)) != 0;
            public Color32[] For(TexturePaintChannel channel) => channel switch
            {
                TexturePaintChannel.Albedo => albedo,
                TexturePaintChannel.Metallic => metallic,
                TexturePaintChannel.Roughness => roughness,
                TexturePaintChannel.AmbientOcclusion => ao,
                TexturePaintChannel.SkinColorMask => skinColorMask,
                TexturePaintChannel.Thickness => thickness,
                TexturePaintChannel.DetailMask => detailMask,
                TexturePaintChannel.NormalControl => normalControl,
                _ => null
            };
        }

        private struct GeneratedPixel
        {
            public Color albedo, metallic, roughness, ambientOcclusion, skinColorMask,
                thickness, detailMask, normalControl;
            public bool Any => albedo.a > 0.0001f || metallic.a > 0.0001f ||
                roughness.a > 0.0001f || ambientOcclusion.a > 0.0001f ||
                skinColorMask.a > 0.0001f || thickness.a > 0.0001f ||
                detailMask.a > 0.0001f || normalControl.a > 0.0001f;
            public void ApplyMask(float value)
            {
                value = Mathf.Clamp01(value);
                albedo.a *= value; metallic.a *= value; roughness.a *= value;
                ambientOcclusion.a *= value; skinColorMask.a *= value;
                thickness.a *= value; detailMask.a *= value; normalControl.a *= value;
            }
        }

        private sealed class SurfaceInputs
        {
            public readonly TexturePaintReadOnlyImage custom;
            private readonly TexturePaintReadOnlyMeshMap position, normal, curvature, ambientOcclusion,
                thickness, id;
            public SurfaceInputs(TexturePaintCommandContextV2 context, string surfaceId)
            {
                custom = context.source.Get(surfaceId, TexturePaintChannel.Custom);
                position = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldPosition);
                normal = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldNormal);
                curvature = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SignedCurvature);
                ambientOcclusion = context.GetMeshMap(surfaceId, TexturePaintMeshMap.AmbientOcclusion);
                thickness = context.GetMeshMap(surfaceId, TexturePaintMeshMap.Thickness);
                id = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SurfaceId);
            }
            public bool IsCovered(float u, float v) => id == null || id.GetPixelBilinear(u, v).a >= 0.5f;
            public Vector3 Coordinates(float u, float v, bool world)
            {
                if (world && position != null)
                {
                    Color c = position.GetPixelBilinear(Repeat(u), Repeat(v));
                    return new Vector3(c.r, c.g, c.b);
                }
                return new Vector3(Repeat(u), Repeat(v), 0f);
            }
            public float Curvature(float u, float v) => curvature == null ? 0f :
                curvature.GetPixelBilinear(Repeat(u), Repeat(v)).r * 2f - 1f;
            public float Cavity(float u, float v) => ambientOcclusion == null ? 0f :
                1f - ambientOcclusion.GetPixelBilinear(Repeat(u), Repeat(v)).r;
            public Vector3 Normal(float u, float v)
            {
                if (normal == null) return Vector3.forward;
                Color c = normal.GetPixelBilinear(Repeat(u), Repeat(v));
                Vector3 value = new Vector3(c.r * 2f - 1f, c.g * 2f - 1f, c.b * 2f - 1f);
                return value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.forward;
            }
            public float Thickness(float u, float v) => thickness == null ? 0.5f :
                thickness.GetPixelBilinear(Repeat(u), Repeat(v)).r;
            public float BoundaryFade(float u, float v, float du, float dv)
            {
                if (id == null || (du <= 0f && dv <= 0f)) return 1f;
                Color center = id.GetPixelBilinear(u, v);
                float valid = 0f;
                valid += SameIsland(center, id.GetPixelBilinear(Repeat(u - du), v)) ? 1f : 0f;
                valid += SameIsland(center, id.GetPixelBilinear(Repeat(u + du), v)) ? 1f : 0f;
                valid += SameIsland(center, id.GetPixelBilinear(u, Repeat(v - dv))) ? 1f : 0f;
                valid += SameIsland(center, id.GetPixelBilinear(u, Repeat(v + dv))) ? 1f : 0f;
                return valid * 0.25f;
            }
            private static bool SameIsland(Color a, Color b) => a.a >= 0.5f && b.a >= 0.5f &&
                Mathf.Abs(a.g - b.g) < 0.1f && Mathf.Abs(a.b - b.b) < 0.1f;
        }

        private readonly struct Settings
        {
            public readonly AAAOrganicGeneratorMode mode;
            public readonly bool worldProjection, fullSurface, veinsEnabled, bruisesEnabled,
                mottlingEnabled;
            public readonly int seed, preset, noiseType, octaves, gravityDirection,
                guideSource, woundType, patternType, armorFinish, scrapeCount;
            public readonly float scale, globalAmount, density, fiberFrequency, direction,
                directionVariation, edgeAmount, pilling, pillScale, colorStrength, roughness,
                height, spread, cavityAmount, pitting, pitScale, depth, flaking, flakeScale,
                streaking, streakLength, streakFrequency, metallic, aoValue, persistence,
                noiseAmount, poreAmount, poreDensity, poreScale, poreSize, poreDepth,
                scratchAmount, scratchDensity, scratchFrequency, scratchLength, scratchWidth,
                scratchDepth, randomness, roughnessVariation, skinMaskStrength, thickness,
                veinIntensity, veinScale, branching, veinDepth, bruiseAmount, bruiseAge,
                bruiseScale, bruiseSize, mottling, spotAmount, spotScale, spotSize, spotHeight,
                freckleAmount, freckleScale, freckleSize, edgeFadePixels, redness,
                spotColorVariation, oiliness, oilScale, wrinkleAmount, wrinkleScale,
                wrinkleDepth,
                scarWidth, guideThreshold, scarFrequency, irregularityScale, scarAge,
                inflammation, raisedRecessed, rimHeight, insideRoughness, sideRoughness,
                healedRoughness, thicknessImpact, scaleAmount, scaleSize, scaleBorder,
                colorVariation, mottleScale, blotches, blotchScale, ageSpots, ageSpotScale,
                ageSpotSize, scaleGloss, thicknessVariation, scaleThickness,
                dentAmount, dentDensity, dentSizeMin, dentSizeMax, dentDepth,
                dentIrregularity, dentRimAmount, dentRimHeight, pingAmount, pingSize,
                pingDepth, pingRimAmount, pingRimHeight, scratchLengthMin,
                scratchLengthMax, scratchBreakupScale, scratchLip, scratchLipHeight,
                scrapeAmount, scrapeSpread, edgeBias, chipAmount, exposedRoughness,
                recessRoughness, burrRoughness;
            public readonly Color primaryColor, secondaryColor, tertiaryColor, spotColor,
                spotSecondaryColor,
                veinColor, freshColor, insideColor, sideColor, inflammationColor,
                exposedColor, recessColor, burrColor;

            public Settings(TexturePaintPluginParameterSet v, AAAOrganicGeneratorMode mode)
            {
                v ??= new TexturePaintPluginParameterSet(); this.mode = mode;
                worldProjection = v.Integer("projection", 1) == 1;
                fullSurface = v.Integer("surfaceMode", 0) == 1;
                seed = v.Integer("seed", 317 + (int)mode * 211);
                scale = Mathf.Max(0.05f, v.Float("scale", DefaultScale(mode)));
                globalAmount = Mathf.Clamp01(v.Float("globalAmount", 1f));
                preset = v.Integer("preset", 0); noiseType = v.Integer("noiseType", 4);
                octaves = Mathf.Clamp(v.Integer("octaves", 5), 1, 8);
                gravityDirection = v.Integer("gravityDirection", 0);
                guideSource = v.Integer("guideSource", 0); woundType = v.Integer("woundType", 0);
                patternType = v.Integer("patternType", 0);
                armorFinish = v.Integer("armorFinish", 1);
                scrapeCount = Mathf.Clamp(v.Integer("scrapeCount", 4), 2, 12);
                density = Clamp01(v, "density", 0.58f); fiberFrequency = Pos(v, "fiberFrequency", 34f);
                direction = v.Float("direction", mode == AAAOrganicGeneratorMode.VeinsSubdermal ? 18f : 0f);
                directionVariation = Pos(v, "directionVariation", 0.35f);
                edgeAmount = Pos(v, "edgeAmount", mode == AAAOrganicGeneratorMode.RustCorrosion ? 0.32f : 0.8f);
                pilling = Clamp01(v, "pilling", 0.35f); pillScale = Pos(v, "pillScale", 8f);
                colorStrength = Clamp01(v, "colorStrength", mode == AAAOrganicGeneratorMode.SurfaceMicroDetail ? 0.04f : 0.72f);
                roughness = Clamp01(v, "roughness", 0.58f); height = Pos(v, "height", 0.08f);
                spread = Clamp01(v, "spread", 0.62f); cavityAmount = Pos(v, "cavityAmount", 0.9f);
                pitting = Clamp01(v, "pitting", 0.52f); pitScale = Pos(v, "pitScale", 18f);
                depth = Pos(v, "depth", 0.12f); flaking = Clamp01(v, "flaking", 0.48f);
                flakeScale = Pos(v, "flakeScale", 11f); streaking = Clamp01(v, "streaking", 0.55f);
                streakLength = Pos(v, "streakLength", 3.5f); streakFrequency = Pos(v, "streakFrequency", 7f);
                metallic = Clamp01(v, "metallic", 0.08f); aoValue = Clamp01(v, "aoValue", 0.35f);
                persistence = Mathf.Clamp(v.Float("persistence", 0.52f), 0.1f, 0.9f);
                noiseAmount = Clamp01(v, "noiseAmount", 0.35f); poreAmount = Clamp01(v, "poreAmount", 0.45f);
                poreDensity = Clamp01(v, "poreDensity", 0.65f); poreScale = Pos(v, "poreScale", 42f);
                poreSize = Pos(v, "poreSize", 0.24f); poreDepth = Pos(v, "poreDepth", 0.022f);
                scratchAmount = Clamp01(v, "scratchAmount", 0.42f); scratchDensity = Clamp01(v, "scratchDensity", 0.35f);
                scratchFrequency = Pos(v, "scratchFrequency", 17f); scratchLength = Pos(v, "scratchLength", 5f);
                scratchWidth = Clamp01(v, "scratchWidth", 0.22f); scratchDepth = Pos(v, "scratchDepth", 0.055f);
                randomness = Pos(v, "randomness", 0.55f); roughnessVariation = Clamp01(v, "roughnessVariation", 0.18f);
                skinMaskStrength = Clamp01(v, "skinMaskStrength", 0.65f); thickness = Clamp01(v, "thickness", 0.55f);
                veinsEnabled = v.Boolean("veinsEnabled", true); bruisesEnabled = v.Boolean("bruisesEnabled", false);
                mottlingEnabled = v.Boolean("mottlingEnabled", true); veinIntensity = Clamp01(v, "veinIntensity", 0.55f);
                veinScale = Pos(v, "veinScale", 7f); branching = Clamp01(v, "branching", 0.65f);
                veinDepth = Pos(v, "veinDepth", 0.08f); bruiseAmount = Clamp01(v, "bruiseAmount", 0.22f);
                bruiseAge = Clamp01(v, "bruiseAge", 0.35f); bruiseScale = Pos(v, "bruiseScale", 1.8f);
                bruiseSize = Pos(v, "bruiseSize", 0.55f); mottling = Clamp01(v, "mottling", 0.3f);
                spotAmount = Clamp01(v, "spotAmount", 0.22f); spotScale = Pos(v, "spotScale", 12f);
                spotSize = Pos(v, "spotSize", 0.24f); spotHeight = Pos(v, "spotHeight", 0.008f);
                freckleAmount = Clamp01(v, "freckleAmount", 0.35f); freckleScale = Pos(v, "freckleScale", 28f);
                freckleSize = Pos(v, "freckleSize", 0.12f); edgeFadePixels = Pos(v, "edgeFadePixels", 24f);
                redness = Clamp01(v, "redness", 0.22f);
                spotColorVariation = Clamp01(v, "spotColorVariation", 0.35f);
                oiliness = Clamp01(v, "oiliness", 0.22f); oilScale = Pos(v, "oilScale", 0.7f);
                wrinkleAmount = Clamp01(v, "wrinkleAmount", 0.14f);
                wrinkleScale = Pos(v, "wrinkleScale", 18f);
                wrinkleDepth = Pos(v, "wrinkleDepth", 0.012f);
                scarWidth = Pos(v, "scarWidth", 8f);
                guideThreshold = Clamp01(v, "guideThreshold", 0.2f); scarFrequency = Pos(v, "scarFrequency", 5f);
                irregularityScale = Pos(v, "irregularityScale", 14f); scarAge = Clamp01(v, "scarAge", 0.7f);
                inflammation = Clamp01(v, "inflammation", 0.28f); raisedRecessed = Mathf.Clamp(v.Float("raisedRecessed", 0.35f), -1f, 1f);
                rimHeight = Pos(v, "rimHeight", 0.08f); insideRoughness = Clamp01(v, "insideRoughness", 0.28f);
                sideRoughness = Clamp01(v, "sideRoughness", 0.62f); healedRoughness = Clamp01(v, "healedRoughness", 0.48f);
                thicknessImpact = Mathf.Clamp(v.Float("thicknessImpact", -0.12f), -0.5f, 0.5f);
                scaleAmount = Clamp01(v, "scaleAmount", 0.78f); scaleSize = Pos(v, "scaleSize", 7f);
                scaleBorder = Pos(v, "scaleBorder", 0.2f); colorVariation = Clamp01(v, "colorVariation", 0.28f);
                mottleScale = Pos(v, "mottleScale", 1.8f); blotches = Clamp01(v, "blotches", 0.35f);
                blotchScale = Pos(v, "blotchScale", 0.8f); ageSpots = Clamp01(v, "ageSpots", 0.2f);
                ageSpotScale = Pos(v, "ageSpotScale", 5f); ageSpotSize = Pos(v, "ageSpotSize", 0.3f);
                scaleGloss = Clamp01(v, "scaleGloss", 0.12f); thicknessVariation = Pos(v, "thicknessVariation", 0.12f);
                scaleThickness = Pos(v, "scaleThickness", 0.06f);
                dentAmount = Clamp01(v, "dentAmount", 0.72f);
                dentDensity = Clamp01(v, "dentDensity", 0.42f);
                dentSizeMin = Pos(v, "dentSizeMin", 0.13f);
                dentSizeMax = Mathf.Max(dentSizeMin, Pos(v, "dentSizeMax", 0.48f));
                dentDepth = Pos(v, "dentDepth", 0.16f);
                dentIrregularity = Pos(v, "dentIrregularity", 0.5f);
                dentRimAmount = Clamp01(v, "dentRimAmount", 0.72f);
                dentRimHeight = Pos(v, "dentRimHeight", 0.055f);
                pingAmount = Clamp01(v, "pingAmount", 0.58f);
                pingSize = Pos(v, "pingSize", 0.2f);
                pingDepth = Pos(v, "pingDepth", 0.2f);
                pingRimAmount = Clamp01(v, "pingRimAmount", 0.7f);
                pingRimHeight = Pos(v, "pingRimHeight", 0.045f);
                scratchLengthMin = Pos(v, "scratchLengthMin", 0.2f);
                scratchLengthMax = Mathf.Max(scratchLengthMin,
                    Pos(v, "scratchLengthMax", 1.25f));
                scratchBreakupScale = Pos(v, "scratchBreakupScale", 7f);
                scratchLip = Clamp01(v, "scratchLip", 0.7f);
                scratchLipHeight = Pos(v, "scratchLipHeight", 0.035f);
                scrapeAmount = Clamp01(v, "scrapeAmount", 0.35f);
                scrapeSpread = Pos(v, "scrapeSpread", 0.12f);
                edgeBias = Pos(v, "edgeBias", 0.55f);
                chipAmount = Clamp01(v, "chipAmount", 0.7f);
                exposedRoughness = Clamp01(v, "exposedRoughness", 0.3f);
                recessRoughness = Clamp01(v, "recessRoughness", 0.72f);
                burrRoughness = Clamp01(v, "burrRoughness", 0.2f);
                primaryColor = v.Color("primaryColor", Color.gray); secondaryColor = v.Color("secondaryColor", Color.gray);
                tertiaryColor = v.Color("tertiaryColor", new Color(0.72f, 0.19f, 0.16f, 1f));
                spotColor = v.Color("spotColor", new Color(0.24f, 0.095f, 0.055f, 1f));
                spotSecondaryColor = v.Color("spotSecondaryColor",
                    new Color(0.42f, 0.17f, 0.09f, 1f));
                veinColor = v.Color("veinColor", new Color(0.12f, 0.23f, 0.34f, 1f));
                freshColor = v.Color("freshColor", new Color(0.34f, 0.025f, 0.02f, 1f));
                insideColor = v.Color("insideColor", new Color(0.58f, 0.28f, 0.26f, 1f));
                sideColor = v.Color("sideColor", new Color(0.68f, 0.39f, 0.35f, 1f));
                inflammationColor = v.Color("inflammationColor", new Color(0.72f, 0.08f, 0.055f, 1f));
                exposedColor = v.Color("exposedColor", new Color(0.42f, 0.45f, 0.48f, 1f));
                recessColor = v.Color("recessColor", new Color(0.075f, 0.065f, 0.055f, 1f));
                burrColor = v.Color("burrColor", new Color(0.68f, 0.7f, 0.72f, 1f));
            }
            private static float Clamp01(TexturePaintPluginParameterSet v, string id, float fallback) =>
                Mathf.Clamp01(v.Float(id, fallback));
            private static float Pos(TexturePaintPluginParameterSet v, string id, float fallback) =>
                Mathf.Max(0f, v.Float(id, fallback));
        }

        private static class Noise
        {
            public static float RandomCell(int x, int y, int seed) => Hash(x, y, 0, seed);

            public static float Perlin(Vector3 p, int seed)
            {
                int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
                float tx = Fade(p.x - x0), ty = Fade(p.y - y0), tz = Fade(p.z - z0);
                float n000 = GradientDot(x0, y0, z0, p, seed);
                float n100 = GradientDot(x0 + 1, y0, z0, p, seed);
                float n010 = GradientDot(x0, y0 + 1, z0, p, seed);
                float n110 = GradientDot(x0 + 1, y0 + 1, z0, p, seed);
                float n001 = GradientDot(x0, y0, z0 + 1, p, seed);
                float n101 = GradientDot(x0 + 1, y0, z0 + 1, p, seed);
                float n011 = GradientDot(x0, y0 + 1, z0 + 1, p, seed);
                float n111 = GradientDot(x0 + 1, y0 + 1, z0 + 1, p, seed);
                float a = Mathf.Lerp(Mathf.Lerp(n000, n100, tx), Mathf.Lerp(n010, n110, tx), ty);
                float b = Mathf.Lerp(Mathf.Lerp(n001, n101, tx), Mathf.Lerp(n011, n111, tx), ty);
                return Mathf.Clamp01(0.5f + Mathf.Lerp(a, b, tz) * 0.58f);
            }

            public static float Value(Vector3 p, int seed)
            {
                int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
                float tx = Fade(p.x - x0), ty = Fade(p.y - y0), tz = Fade(p.z - z0);
                float c000 = Hash(x0, y0, z0, seed), c100 = Hash(x0 + 1, y0, z0, seed);
                float c010 = Hash(x0, y0 + 1, z0, seed), c110 = Hash(x0 + 1, y0 + 1, z0, seed);
                float c001 = Hash(x0, y0, z0 + 1, seed), c101 = Hash(x0 + 1, y0, z0 + 1, seed);
                float c011 = Hash(x0, y0 + 1, z0 + 1, seed), c111 = Hash(x0 + 1, y0 + 1, z0 + 1, seed);
                float a = Mathf.Lerp(Mathf.Lerp(c000, c100, tx), Mathf.Lerp(c010, c110, tx), ty);
                float b = Mathf.Lerp(Mathf.Lerp(c001, c101, tx), Mathf.Lerp(c011, c111, tx), ty);
                return Mathf.Lerp(a, b, tz);
            }
            public static float Fbm(Vector3 p, int seed, int octaves, float persistence)
            {
                float sum = 0f, weight = 0f, amplitude = 1f;
                for (int i = 0; i < Mathf.Clamp(octaves, 1, 8); i++)
                {
                    sum += Perlin(p, seed + i * 1013) * amplitude; weight += amplitude;
                    p *= 2.03f; amplitude *= persistence;
                }
                return weight > 0f ? sum / weight : 0.5f;
            }
            public static float Ridged(Vector3 p, int seed, int octaves)
            {
                float n = Fbm(p, seed, octaves, 0.52f);
                return 1f - Mathf.Abs(n * 2f - 1f);
            }
            public static float Cell(Vector3 p, int seed)
            {
                Worley(p, seed, out float cellRandom);
                return cellRandom;
            }
            public static float Worley(Vector3 p, int seed, out float cellRandom)
            {
                int bx = Mathf.FloorToInt(p.x), by = Mathf.FloorToInt(p.y), bz = Mathf.FloorToInt(p.z);
                float best = 99f; cellRandom = 0f;
                for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        int cx = bx + x, cy = by + y, cz = bz + z;
                        Vector3 point = new Vector3(cx + Hash(cx, cy, cz, seed),
                            cy + Hash(cx, cy, cz, seed + 17), cz + Hash(cx, cy, cz, seed + 31));
                        float d = (point - p).sqrMagnitude;
                        if (d >= best) continue;
                        best = d; cellRandom = Hash(cx, cy, cz, seed + 67);
                    }
                return Mathf.Clamp01(Mathf.Sqrt(best));
            }
            private static float Hash(int x, int y, int z, int seed)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + z * 1442695041 + seed * 69069);
                    h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                    return (h & 0x00ffffffu) / 16777215f;
                }
            }
            private static float GradientDot(int x, int y, int z, Vector3 p, int seed)
            {
                int index = Mathf.Min(11, Mathf.FloorToInt(Hash(x, y, z, seed) * 12f));
                Vector3 gradient = index switch
                {
                    0 => new Vector3(1f, 1f, 0f), 1 => new Vector3(-1f, 1f, 0f),
                    2 => new Vector3(1f, -1f, 0f), 3 => new Vector3(-1f, -1f, 0f),
                    4 => new Vector3(1f, 0f, 1f), 5 => new Vector3(-1f, 0f, 1f),
                    6 => new Vector3(1f, 0f, -1f), 7 => new Vector3(-1f, 0f, -1f),
                    8 => new Vector3(0f, 1f, 1f), 9 => new Vector3(0f, -1f, 1f),
                    10 => new Vector3(0f, 1f, -1f), _ => new Vector3(0f, -1f, -1f)
                };
                return Vector3.Dot(gradient * 0.70710678f,
                    p - new Vector3(x, y, z));
            }
            private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float LineNetwork(Vector3 p, float degrees, float frequency,
            float warp, int seed)
        {
            float r = degrees * Mathf.Deg2Rad;
            float across = -p.x * Mathf.Sin(r) + p.y * Mathf.Cos(r);
            float along = p.x * Mathf.Cos(r) + p.y * Mathf.Sin(r);
            float path = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((across + warp) *
                Mathf.PI * Mathf.Max(0.1f, frequency))), 18f);
            float breakMask = Noise.Fbm(new Vector3(along * 0.55f, across * 0.15f, p.z),
                seed, 4, 0.57f);
            return path * SmoothStep(0.28f, 0.7f, breakMask);
        }

        private static float GuideValue(TexturePaintReadOnlyPixels image, float u, float v)
        {
            if (image == null) return 0f;
            Color c = image.GetPixelBilinear(Repeat(u), Repeat(v));
            return Mathf.Clamp01(Luminance(c) * c.a);
        }

        private static float MaskBoundaryFade(TexturePaintReadOnlyParameterTexture mask,
            float u, float v, float du, float dv)
        {
            if (mask == null || (du <= 0f && dv <= 0f)) return 1f;
            float center = GuideValue(mask, u, v);
            if (center <= 0.0001f) return 0f;
            float neighbors = GuideValue(mask, u - du, v) + GuideValue(mask, u + du, v) +
                              GuideValue(mask, u, v - dv) + GuideValue(mask, u, v + dv);
            // Both density and alpha soften as freckles approach a painted mask edge.
            return Mathf.Clamp01(neighbors * 0.25f / center);
        }
        private static Vector3 Gravity(int direction) => direction switch
        {
            1 => Vector3.up, 2 => Vector3.back, 3 => Vector3.forward,
            4 => Vector3.left, 5 => Vector3.right, _ => Vector3.down
        };
        private static Color BruiseColor(float age)
        {
            if (age < 0.33f) return Color.Lerp(new Color(0.42f, 0.025f, 0.045f, 1f),
                new Color(0.2f, 0.035f, 0.28f, 1f), age / 0.33f);
            if (age < 0.7f) return Color.Lerp(new Color(0.2f, 0.035f, 0.28f, 1f),
                new Color(0.31f, 0.38f, 0.08f, 1f), (age - 0.33f) / 0.37f);
            return Color.Lerp(new Color(0.31f, 0.38f, 0.08f, 1f),
                new Color(0.48f, 0.35f, 0.08f, 1f), (age - 0.7f) / 0.3f);
        }
        private static Color WeightedColor(Color a, float aw, Color b, float bw, float coverage)
        {
            float total = Mathf.Max(0.0001f, aw + bw);
            return new Color((a.r * aw + b.r * bw) / total, (a.g * aw + b.g * bw) / total,
                (a.b * aw + b.b * bw) / total, coverage);
        }
        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, Mathf.Clamp01(a));
        private static Color Scalar(float value, float alpha) => new(value, value, value, Mathf.Clamp01(alpha));
        private static float Luminance(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
        private static float Repeat(float v) => v - Mathf.Floor(v);
        private static float SmoothStep(float min, float max, float value)
        {
            float t = Mathf.Clamp01((value - min) / Mathf.Max(0.00001f, max - min));
            return t * t * (3f - 2f * t);
        }
    }
}
