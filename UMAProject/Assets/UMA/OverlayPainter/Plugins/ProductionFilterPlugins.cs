using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    public sealed class LevelsCurvesFilterPlugin : ScriptableObject, ITexturePaintFilterV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            ProductionFilterEngine.CreateDescriptor(ProductionFilterKind.LevelsCurves);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            ProductionFilterEngine.SelectedSourceMask(parameters);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            ProductionFilterEngine.ExecuteAsync(context, ProductionFilterKind.LevelsCurves);
    }

    public sealed class NormalHeightFilterPlugin : ScriptableObject, ITexturePaintFilterV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            ProductionFilterEngine.CreateDescriptor(ProductionFilterKind.NormalHeight);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            ProductionFilterEngine.SelectedSourceMask(parameters);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            ProductionFilterEngine.ExecuteAsync(context, ProductionFilterKind.NormalHeight);
    }

    public sealed class BlurSharpenDetailFilterPlugin : ScriptableObject, ITexturePaintFilterV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            ProductionFilterEngine.CreateDescriptor(ProductionFilterKind.BlurSharpenDetail);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            ProductionFilterEngine.SelectedSourceMask(parameters);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            ProductionFilterEngine.ExecuteAsync(context, ProductionFilterKind.BlurSharpenDetail);
    }

    public sealed class ChannelOperationsFilterPlugin : ScriptableObject, ITexturePaintFilterV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            ProductionFilterEngine.CreateDescriptor(ProductionFilterKind.ChannelOperations);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            ProductionFilterEngine.SelectedSourceMask(parameters);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            ProductionFilterEngine.ExecuteAsync(context, ProductionFilterKind.ChannelOperations);
    }

    public sealed class MorphologyDistanceFilterPlugin : ScriptableObject, ITexturePaintFilterV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            ProductionFilterEngine.CreateDescriptor(ProductionFilterKind.MorphologyDistance);
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            ProductionFilterEngine.SelectedSourceMask(parameters);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            ProductionFilterEngine.ExecuteAsync(context, ProductionFilterKind.MorphologyDistance);
    }

    internal enum ProductionFilterKind
    {
        LevelsCurves,
        NormalHeight,
        BlurSharpenDetail,
        ChannelOperations,
        MorphologyDistance
    }

    internal static class ProductionFilterEngine
    {
        private const int RowsPerTile = 128;
        private static readonly TexturePaintChannel[] Channels =
            (TexturePaintChannel[])Enum.GetValues(typeof(TexturePaintChannel));
        private static readonly string[] ChannelNames = BuildChannelNames();

        public static TexturePaintPluginDescriptor CreateDescriptor(ProductionFilterKind kind)
        {
            var descriptor = new TexturePaintPluginDescriptor
            {
                id = Id(kind), displayName = Name(kind), description = Description(kind),
                pluginVersion = "1.0.0", capabilities = TexturePaintPluginCapability.Filter |
                    TexturePaintPluginCapability.LongRunning,
                declaredChannels = TexturePaintChannelMask.All,
                readChannels = TexturePaintChannelMask.All,
                supportedTargets = kind == ProductionFilterKind.NormalHeight
                    ? TexturePaintPluginTarget.LayerContent : TexturePaintPluginTarget.All,
                channelSnapshotMaximumResolution = 4096,
                parameters = Parameters(kind)
            };
            if (kind == ProductionFilterKind.MorphologyDistance)
            {
                descriptor.capabilities |= TexturePaintPluginCapability.ReadsMeshMaps;
                descriptor.requiredMeshMaps = TexturePaintMeshMapMask.SurfaceId;
            }
            return descriptor;
        }

        public static TexturePaintChannelMask SelectedSourceMask(TexturePaintPluginParameterSet parameters)
        {
            TexturePaintChannel channel = Channel(parameters.Integer("sourceChannel", 0));
            return TexturePaintExportTemplate.ToMask(channel);
        }

        public static Task ExecuteAsync(TexturePaintCommandContextV2 context, ProductionFilterKind kind)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            TexturePaintPluginParameterSet p = context.parameters;
            TexturePaintChannel sourceChannel = Channel(p.Integer("sourceChannel", 0));
            TexturePaintChannel destinationChannel = kind == ProductionFilterKind.NormalHeight
                ? NormalDestination(p) : Channel(p.Integer("destinationChannel", (int)sourceChannel));

            float[] curve = kind == ProductionFilterKind.LevelsCurves
                ? BuildCurveLut(p.Curve("curve", AnimationCurve.Linear(0f, 0f, 1f, 1f))) : null;
            TexturePaintReadOnlyParameterTexture detailNormal =
                kind == ProductionFilterKind.NormalHeight
                    ? context.GetTextureParameter("detailNormal") : null;
            return Task.Run(() =>
            {
                int surfaceCount = context.source.surfaceIds.Count;
                for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
                {
                    context.cancellationToken.ThrowIfCancellationRequested();
                    string surfaceId = context.source.surfaceIds[surfaceIndex];
                    TexturePaintReadOnlyPixels source = context.target == TexturePaintPluginTarget.LayerMask
                        ? (TexturePaintReadOnlyPixels)context.source.GetMask(surfaceId)
                        : context.source.Get(surfaceId, sourceChannel);
                    TexturePaintReadOnlyChannelInfo info = context.target == TexturePaintPluginTarget.LayerMask
                        ? null : context.source.GetChannelInfo(surfaceId, destinationChannel);
                    if (source == null || (info == null &&
                        context.target != TexturePaintPluginTarget.LayerMask)) continue;
                    if (kind == ProductionFilterKind.MorphologyDistance)
                        ExecuteMorphology(context, surfaceId, source, destinationChannel, info, p,
                            surfaceIndex, surfaceCount);
                    else
                        ExecuteTiled(context, kind, surfaceId, source, sourceChannel,
                            destinationChannel, info, p, curve, detailNormal,
                            surfaceIndex, surfaceCount);
                }
            }, context.cancellationToken);
        }

        private static void ExecuteTiled(TexturePaintCommandContextV2 context, ProductionFilterKind kind,
            string surfaceId, TexturePaintReadOnlyPixels source, TexturePaintChannel sourceChannel,
            TexturePaintChannel destinationChannel, TexturePaintReadOnlyChannelInfo info,
            TexturePaintPluginParameterSet p, float[] curve,
            TexturePaintReadOnlyParameterTexture detailNormal,
            int surfaceIndex, int surfaceCount)
        {
            int width = info?.width ?? source.width, height = info?.height ?? source.height;
            for (int y0 = 0; y0 < height; y0 += RowsPerTile)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                int rows = Math.Min(RowsPerTile, height - y0);
                var output = new Color32[width * rows];
                Parallel.For(0, rows, new ParallelOptions
                    { CancellationToken = context.cancellationToken }, localY =>
                {
                    int y = y0 + localY;
                    float v = (y + 0.5f) / height;
                    for (int x = 0; x < width; x++)
                    {
                        float u = (x + 0.5f) / width;
                        Color value;
                        switch (kind)
                        {
                            case ProductionFilterKind.LevelsCurves:
                                value = Levels(source, u, v, p, curve); break;
                            case ProductionFilterKind.NormalHeight:
                                value = NormalHeight(source, u, v, width, height, p,
                                    detailNormal); break;
                            case ProductionFilterKind.BlurSharpenDetail:
                                value = BlurDetail(source, u, v, width, height, sourceChannel, p); break;
                            default:
                                value = ChannelOperation(source.GetPixelBilinear(u, v), u, v, p); break;
                        }
                        value = context.target == TexturePaintPluginTarget.LayerMask
                            ? MaskColor(value) : Constrain(destinationChannel, value);
                        output[localY * width + x] = value;
                    }
                });
                if (context.target == TexturePaintPluginTarget.LayerMask)
                    context.WriteMaskTileCompactOwned(surfaceId, new RectInt(0, y0, width, rows),
                        output, TexturePaintPluginBlend.Replace);
                else
                    context.WriteTileCompactOwned(surfaceId, destinationChannel,
                        new RectInt(0, y0, width, rows), output,
                        TexturePaintChannelUtility.IsColor(destinationChannel)
                            ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data,
                        TexturePaintPluginBlend.Replace);
                context.progress?.Report((surfaceIndex + (y0 + rows) / (float)height) /
                    Math.Max(1, surfaceCount));
            }
        }

        private static Color Levels(TexturePaintReadOnlyPixels source, float u, float v,
            TexturePaintPluginParameterSet p, float[] curve)
        {
            Color input = source.GetPixelBilinear(u, v);
            float inBlack = p.Float("inputBlack", 0f), inWhite = p.Float("inputWhite", 1f);
            float gamma = Math.Max(0.01f, p.Float("gamma", 1f));
            float outBlack = p.Float("outputBlack", 0f), outWhite = p.Float("outputWhite", 1f);
            float amount = Mathf.Clamp01(p.Float("amount", 1f));
            bool luminance = p.Boolean("preserveHue", false);
            Color result;
            if (luminance)
            {
                float oldLuma = Math.Max(0.00001f, Luma(input));
                float newLuma = AdjustLevel(oldLuma, inBlack, inWhite, gamma,
                    outBlack, outWhite, curve);
                float scale = newLuma / oldLuma;
                result = new Color(input.r * scale, input.g * scale, input.b * scale, input.a);
            }
            else result = new Color(
                AdjustLevel(input.r, inBlack, inWhite, gamma, outBlack, outWhite, curve),
                AdjustLevel(input.g, inBlack, inWhite, gamma, outBlack, outWhite, curve),
                AdjustLevel(input.b, inBlack, inWhite, gamma, outBlack, outWhite, curve),
                input.a);
            return Color.Lerp(input, result, amount);
        }

        private static float AdjustLevel(float value, float inBlack, float inWhite,
            float gamma, float outBlack, float outWhite, float[] curve)
        {
            float normalized = Mathf.Clamp01((value - inBlack) /
                Math.Max(0.00001f, inWhite - inBlack));
            normalized = Mathf.Pow(normalized, 1f / gamma);
            return Mathf.Lerp(outBlack, outWhite, SampleCurve(curve, normalized));
        }

        private static Color NormalHeight(TexturePaintReadOnlyPixels source, float u, float v,
            int width, int height, TexturePaintPluginParameterSet p,
            TexturePaintReadOnlyParameterTexture detailNormal)
        {
            int operation = p.Integer("operation", 0);
            float strength = p.Float("strength", 1f);
            Color raw = source.GetPixelBilinear(u, v);
            if (operation == 3)
            {
                float stepX = Math.Max(1f, p.Float("sampleRadius", 1f)) / width;
                float stepY = Math.Max(1f, p.Float("sampleRadius", 1f)) / height;
                float left = Luma(source.GetPixelBilinear(u - stepX, v));
                float right = Luma(source.GetPixelBilinear(u + stepX, v));
                float down = Luma(source.GetPixelBilinear(u, v - stepY));
                float up = Luma(source.GetPixelBilinear(u, v + stepY));
                Vector3 n = new Vector3((left - right) * strength,
                    (down - up) * strength, 2f).normalized;
                if (p.Boolean("flipY", false)) n.y = -n.y;
                return EncodeNormal(n);
            }
            if (operation == 5)
            {
                float value = Mathf.Clamp01(0.5f + (Luma(raw) - 0.5f) * strength);
                return new Color(value, value, value, 1f);
            }
            Vector3 normal = DecodeNormal(raw);
            if (operation == 4)
            {
                if (detailNormal == null) return EncodeNormal(normal);
                float tileX = p.Float("detailTilingX", 1f);
                float tileY = p.Float("detailTilingY", 1f);
                float detailU = Repeat(u * tileX + p.Float("detailOffsetX", 0f));
                float detailV = Repeat(v * tileY + p.Float("detailOffsetY", 0f));
                Vector3 detail = DecodeNormal(detailNormal.GetPixelBilinear(detailU, detailV));
                float detailStrength = p.Float("detailStrength", 1f);
                detail = new Vector3(detail.x * detailStrength, detail.y * detailStrength,
                    detail.z).normalized;
                if (p.Boolean("detailFlipY", false)) detail.y = -detail.y;
                return EncodeNormal(BlendRnm(normal, detail));
            }
            switch (operation)
            {
                case 0: normal = new Vector3(normal.x * strength, normal.y * strength, normal.z).normalized; break;
                case 1:
                    normal.z = Mathf.Sqrt(Mathf.Max(0f, 1f - normal.x * normal.x - normal.y * normal.y));
                    normal.Normalize(); break;
                case 2: normal.y = -normal.y; break;
            }
            if (p.Boolean("flipY", false) && operation != 2) normal.y = -normal.y;
            return EncodeNormal(normal);
        }

        private static Color BlurDetail(TexturePaintReadOnlyPixels source, float u, float v,
            int width, int height, TexturePaintChannel channel, TexturePaintPluginParameterSet p)
        {
            int mode = p.Integer("operation", 0);
            float radius = Math.Max(0.25f, p.Float("radius", 3f));
            float amount = p.Float("amount", 1f);
            float edge = Math.Max(0.001f, p.Float("edgeThreshold", 0.1f));
            float angle = p.Float("direction", 0f) * Mathf.Deg2Rad;
            Color center = source.GetPixelBilinear(u, v);
            if (mode == 3) return Median(source, u, v, radius / width, radius / height);
            Color sum = Color.clear;
            float weights = 0f;
            const int samples = 13;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1) * 2f - 1f;
                float ox, oy;
                if (mode == 1)
                {
                    ox = Mathf.Cos(angle) * t * radius / width;
                    oy = Mathf.Sin(angle) * t * radius / height;
                }
                else
                {
                    float a = i * 2.39996323f;
                    float r = Mathf.Sqrt((i + 0.5f) / samples) * radius;
                    ox = Mathf.Cos(a) * r / width; oy = Mathf.Sin(a) * r / height;
                }
                Color sample = source.GetPixelBilinear(u + ox, v + oy);
                float w = Mathf.Exp(-t * t * 2f);
                if (mode == 2)
                    w *= Mathf.Exp(-ColorDistance(center, sample) / edge);
                sum += sample * w; weights += w;
            }
            Color blurred = sum / Math.Max(0.00001f, weights);
            Color result;
            switch (mode)
            {
                case 4: result = center + (center - blurred) * amount; break;
                case 5: result = new Color(0.5f, 0.5f, 0.5f, center.a) + (center - blurred) * amount; break;
                default: result = Color.Lerp(center, blurred, Mathf.Clamp01(amount)); break;
            }
            if (channel == TexturePaintChannel.Normal)
                result = EncodeNormal(DecodeNormal(result));
            result.a = center.a;
            return result;
        }

        private static Color ChannelOperation(Color input, float u, float v,
            TexturePaintPluginParameterSet p)
        {
            int operation = p.Integer("operation", 0);
            float amount = Mathf.Clamp01(p.Float("amount", 1f));
            Color result = input;
            switch (operation)
            {
                case 0: result = new Color(1f - input.r, 1f - input.g, 1f - input.b, input.a); break;
                case 1:
                    result = Remap(input, p.Float("inputMin", 0f), p.Float("inputMax", 1f),
                        p.Float("outputMin", 0f), p.Float("outputMax", 1f)); break;
                case 2:
                    float gray = Luma(input); result = new Color(gray, gray, gray, input.a); break;
                case 3:
                    result = new Color(Component(input, p.Integer("redSource", 0)),
                        Component(input, p.Integer("greenSource", 1)),
                        Component(input, p.Integer("blueSource", 2)),
                        Component(input, p.Integer("alphaSource", 3))); break;
                case 4: result = Gradient(Luma(input), p); result.a = input.a; break;
                case 5:
                    Color find = p.Color("findColor", Color.white);
                    float distance = ColorDistance(input, find);
                    float tolerance = Math.Max(0.0001f, p.Float("tolerance", 0.1f));
                    float softness = Math.Max(0.0001f, p.Float("softness", 0.05f));
                    float match = 1f - SmoothStep(tolerance, tolerance + softness, distance);
                    result = Color.Lerp(input, p.Color("replaceColor", Color.black), match); result.a = input.a;
                    break;
                case 6:
                    float noise = Fractal(u, v, p.Float("variationScale", 16f), p.Integer("seed", 1337));
                    float variation = (noise - 0.5f) * 2f * p.Float("variation", 0.15f);
                    result = new Color(input.r + variation, input.g + variation,
                        input.b + variation, input.a); break;
            }
            return Color.Lerp(input, result, amount);
        }

        private static void ExecuteMorphology(TexturePaintCommandContextV2 context, string surfaceId,
            TexturePaintReadOnlyPixels source, TexturePaintChannel destination,
            TexturePaintReadOnlyChannelInfo info, TexturePaintPluginParameterSet p,
            int surfaceIndex, int surfaceCount)
        {
            int width = info?.width ?? source.width, height = info?.height ?? source.height,
                count = checked(width * height);
            float threshold = p.Float("threshold", 0.5f);
            float[] values = new float[count];
            int[] islands = new int[count];
            TexturePaintReadOnlyMeshMap surfaceMap = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SurfaceId);
            Parallel.For(0, height, new ParallelOptions
                { CancellationToken = context.cancellationToken }, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                    int i = y * width + x;
                    values[i] = Luma(source.GetPixelBilinear(u, v));
                    islands[i] = SurfaceLabel(surfaceMap, u, v);
                }
            });
            float[] toInside = Distance(values, islands, width, height, threshold, true,
                context.cancellationToken);
            float[] toOutside = Distance(values, islands, width, height, threshold, false,
                context.cancellationToken);
            int operation = p.Integer("operation", 0);
            float radius = Math.Max(0.01f, p.Float("radius", 8f));
            float softness = Math.Max(0.01f, p.Float("softness", 2f));
            bool invert = p.Boolean("invert", false);
            for (int y0 = 0; y0 < height; y0 += RowsPerTile)
            {
                int rows = Math.Min(RowsPerTile, height - y0);
                var output = new Color32[width * rows];
                Parallel.For(0, rows, new ParallelOptions
                    { CancellationToken = context.cancellationToken }, localY =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y0 + localY) * width + x;
                        bool inside = values[i] >= threshold;
                        float signed = inside ? -toOutside[i] : toInside[i];
                        float result;
                        switch (operation)
                        {
                            case 0: result = 1f - SmoothStep(radius, radius + softness, signed); break;
                            case 1: result = 1f - SmoothStep(-radius - softness, -radius, signed); break;
                            case 2: result = 1f - SmoothStep(-radius, radius, signed); break;
                            case 3: result = 1f - SmoothStep(-radius - softness, -radius, signed); break;
                            case 4: result = 1f - SmoothStep(radius, radius + softness, Math.Abs(signed)); break;
                            case 5: result = Mathf.Clamp01(0.5f - signed / (radius * 2f)); break;
                            case 6: result = 1f - SmoothStep(0f, Math.Max(1f, softness), Math.Abs(signed)); break;
                            default:
                                float profile = 1f - Mathf.Clamp01(Math.Abs(signed) / radius);
                                result = 0.5f + (inside ? profile : -profile) *
                                    p.Float("bevelStrength", 0.5f); break;
                        }
                        if (islands[i] < 0) result = 0f;
                        if (invert) result = 1f - result;
                        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(result) * 255f);
                        output[localY * width + x] = new Color32(b, b, b, 255);
                    }
                });
                if (context.target == TexturePaintPluginTarget.LayerMask)
                    context.WriteMaskTileCompactOwned(surfaceId, new RectInt(0, y0, width, rows),
                        output, TexturePaintPluginBlend.Replace);
                else
                    context.WriteTileCompactOwned(surfaceId, destination, new RectInt(0, y0, width, rows),
                        output, TexturePaintChannelUtility.IsColor(destination)
                            ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data,
                        TexturePaintPluginBlend.Replace);
                context.progress?.Report((surfaceIndex + (y0 + rows) / (float)height) /
                    Math.Max(1, surfaceCount));
            }
        }

        private static float[] Distance(float[] values, int[] islands, int width, int height,
            float threshold, bool featureInside, System.Threading.CancellationToken token)
        {
            int count = values.Length;
            var distance = new float[count];
            const float infinity = 1e20f, diagonal = 1.41421356f;
            for (int i = 0; i < count; i++)
                distance[i] = islands[i] >= 0 && ((values[i] >= threshold) == featureInside) ? 0f : infinity;
            for (int y = 0; y < height; y++)
            {
                if ((y & 31) == 0) token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x; if (islands[i] < 0) continue;
                    Relax(distance, islands, i, x - 1, y, width, height, 1f);
                    Relax(distance, islands, i, x, y - 1, width, height, 1f);
                    Relax(distance, islands, i, x - 1, y - 1, width, height, diagonal);
                    Relax(distance, islands, i, x + 1, y - 1, width, height, diagonal);
                }
            }
            for (int y = height - 1; y >= 0; y--)
            {
                if ((y & 31) == 0) token.ThrowIfCancellationRequested();
                for (int x = width - 1; x >= 0; x--)
                {
                    int i = y * width + x; if (islands[i] < 0) continue;
                    Relax(distance, islands, i, x + 1, y, width, height, 1f);
                    Relax(distance, islands, i, x, y + 1, width, height, 1f);
                    Relax(distance, islands, i, x + 1, y + 1, width, height, diagonal);
                    Relax(distance, islands, i, x - 1, y + 1, width, height, diagonal);
                }
            }
            return distance;
        }

        private static void Relax(float[] distance, int[] islands, int i, int x, int y,
            int width, int height, float cost)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
            int n = y * width + x;
            if (islands[n] != islands[i]) return;
            distance[i] = Math.Min(distance[i], distance[n] + cost);
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters(ProductionFilterKind kind)
        {
            var result = new List<TexturePaintPluginParameterDefinition>
            {
                EnumParameter("sourceChannel", "Source Channel",
                    kind == ProductionFilterKind.NormalHeight ? (int)TexturePaintChannel.Normal : 0,
                    ChannelNames),
                EnumParameter("destinationChannel", "Output Channel",
                    kind == ProductionFilterKind.NormalHeight ? (int)TexturePaintChannel.Normal :
                    kind == ProductionFilterKind.MorphologyDistance
                        ? (int)TexturePaintChannel.NormalControl : 0,
                    ChannelNames)
            };
            switch (kind)
            {
                case ProductionFilterKind.LevelsCurves:
                    result.AddRange(new[] { Header("levels", "Levels"),
                        Float("inputBlack", "Input Black", 0f, 0f, 1f), Float("inputWhite", "Input White", 1f, 0f, 1f),
                        Float("gamma", "Gamma", 1f, 0.05f, 8f), Float("outputBlack", "Output Black", 0f, 0f, 1f),
                        Float("outputWhite", "Output White", 1f, 0f, 1f), Curve("curve", "Master Curve"),
                        Bool("preserveHue", "Preserve Hue / Adjust Luminance", false), Float("amount", "Amount", 1f, 0f, 1f) });
                    break;
                case ProductionFilterKind.NormalHeight:
                    result.RemoveAt(1);
                    result.AddRange(new[] { EnumParameter("operation", "Operation", 0,
                            new[] { "Normal Strength", "Reconstruct Z", "Flip Green (Y)", "Height to Normal", "Combine Detail Normal (RNM)", "Normal Control Strength" }),
                        Float("strength", "Strength", 1f, -16f, 16f), Float("sampleRadius", "Height Sample Radius", 1f, 0.25f, 16f),
                        Bool("flipY", "Flip Green (Y)", false), Header("detail", "Detail Normal (RNM)"),
                        Texture("detailNormal", "Detail Normal"), Float("detailStrength", "Detail Strength", 1f, 0f, 8f),
                        Float("detailTilingX", "Tiling X", 1f, 0.01f, 256f), Float("detailTilingY", "Tiling Y", 1f, 0.01f, 256f),
                        Float("detailOffsetX", "Offset X", 0f, -16f, 16f), Float("detailOffsetY", "Offset Y", 0f, -16f, 16f),
                        Bool("detailFlipY", "Flip Detail Green (Y)", false) });
                    break;
                case ProductionFilterKind.BlurSharpenDetail:
                    result.AddRange(new[] { EnumParameter("operation", "Operation", 0,
                            new[] { "Gaussian Blur", "Directional Blur", "Bilateral Blur", "Median", "Unsharp Mask", "High Pass" }),
                        Float("radius", "Radius (px)", 3f, 0.25f, 128f), Float("amount", "Amount", 1f, 0f, 4f),
                        Float("direction", "Direction", 0f, -180f, 180f), Float("edgeThreshold", "Edge Preservation", 0.1f, 0.001f, 1f) });
                    break;
                case ProductionFilterKind.ChannelOperations:
                    result.AddRange(new[] { EnumParameter("operation", "Operation", 0,
                            new[] { "Invert", "Clamp / Remap", "Grayscale", "Channel Shuffle", "Gradient Map", "Color Replace", "Color Variation" }),
                        Float("amount", "Amount", 1f, 0f, 1f), Float("inputMin", "Input Minimum", 0f, 0f, 1f),
                        Float("inputMax", "Input Maximum", 1f, 0f, 1f), Float("outputMin", "Output Minimum", 0f, 0f, 1f),
                        Float("outputMax", "Output Maximum", 1f, 0f, 1f), Header("shuffle", "Channel Shuffle"),
                        EnumParameter("redSource", "Red From", 0, Components), EnumParameter("greenSource", "Green From", 1, Components),
                        EnumParameter("blueSource", "Blue From", 2, Components), EnumParameter("alphaSource", "Alpha From", 3, Components),
                        Header("gradient", "Gradient Map"), ColorParameter("gradientLow", "Shadow Color", Color.black),
                        ColorParameter("gradientMid", "Midtone Color", Color.gray), ColorParameter("gradientHigh", "Highlight Color", Color.white),
                        Header("replace", "Color Replace"), ColorParameter("findColor", "Find", Color.white),
                        ColorParameter("replaceColor", "Replace", Color.black), Float("tolerance", "Tolerance", 0.1f, 0f, 1f),
                        Float("softness", "Softness", 0.05f, 0f, 1f), Header("variationHeader", "Color Variation"),
                        Float("variation", "Variation", 0.15f, 0f, 1f), Float("variationScale", "Scale", 16f, 0.1f, 512f),
                        Integer("seed", "Seed", 1337, 0, 999999) });
                    break;
                default:
                    result.AddRange(new[] { EnumParameter("operation", "Operation", 0,
                            new[] { "Dilate", "Erode", "Feather", "Choke", "Outline", "Signed Distance", "Edge Detect", "Bevel Height" }),
                        Float("radius", "Distance / Radius (px)", 8f, 0.1f, 256f), Float("softness", "Softness (px)", 2f, 0.01f, 128f),
                        Float("threshold", "Source Threshold", 0.5f, 0f, 1f), Float("bevelStrength", "Bevel Strength", 0.5f, -1f, 1f),
                        Bool("invert", "Invert Result", false) });
                    break;
            }
            return result;
        }

        private static string Id(ProductionFilterKind kind) => "com.uma.texturepaint.filter." +
            kind.ToString().Replace("LevelsCurves", "levels-curves").Replace("NormalHeight", "normal-height")
                .Replace("BlurSharpenDetail", "blur-sharpen-detail").Replace("ChannelOperations", "channel-operations")
                .Replace("MorphologyDistance", "morphology-distance");
        private static string Name(ProductionFilterKind kind)
        {
            switch (kind) { case ProductionFilterKind.LevelsCurves: return "Levels & Curves";
                case ProductionFilterKind.NormalHeight: return "Normal & Height Toolkit";
                case ProductionFilterKind.BlurSharpenDetail: return "Blur, Sharpen & Detail";
                case ProductionFilterKind.ChannelOperations: return "Channel Operations";
                default: return "Morphology & Distance"; }
        }
        private static string Description(ProductionFilterKind kind)
        {
            switch (kind) { case ProductionFilterKind.LevelsCurves: return "Non-destructive levels, gamma and curve correction for any layer channel.";
                case ProductionFilterKind.NormalHeight: return "Normal strength, reconstruction, convention conversion and grayscale height conversion.";
                case ProductionFilterKind.BlurSharpenDetail: return "Production blur, denoise, edge-preserving detail, sharpen and high-pass operations.";
                case ProductionFilterKind.ChannelOperations: return "Remap, invert, shuffle, grade and transfer information between channels.";
                default: return "Island-safe dilation, erosion, feathering, outlines, distance fields and bevel-height generation."; }
        }
        private static TexturePaintChannel Channel(int value) => Channels[Mathf.Clamp(value, 0, Channels.Length - 1)];
        private static TexturePaintChannel NormalDestination(TexturePaintPluginParameterSet p) =>
            p.Integer("operation", 0) == 5 ? TexturePaintChannel.NormalControl : TexturePaintChannel.Normal;
        private static string[] BuildChannelNames() { var names = new string[Channels.Length]; for (int i = 0; i < names.Length; i++) names[i] = TexturePaintChannelUtility.DisplayName(Channels[i]); return names; }
        private static readonly string[] Components = { "Red", "Green", "Blue", "Alpha", "Luminance", "Zero", "One" };

        private static float[] BuildCurveLut(AnimationCurve curve) { var lut = new float[1024]; curve = curve ?? AnimationCurve.Linear(0, 0, 1, 1); for (int i = 0; i < lut.Length; i++) lut[i] = curve.Evaluate(i / (float)(lut.Length - 1)); return lut; }
        private static float SampleCurve(float[] lut, float value) { float x = Mathf.Clamp01(value) * (lut.Length - 1); int i = Mathf.FloorToInt(x); return Mathf.Lerp(lut[i], lut[Math.Min(lut.Length - 1, i + 1)], x - i); }
        private static Color Constrain(TexturePaintChannel channel, Color c) { c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b); c.a = Mathf.Clamp01(c.a); return TexturePaintChannelUtility.ConstrainColor(channel, c); }
        private static float Luma(Color c) => Mathf.Clamp01(c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f);
        private static Vector3 DecodeNormal(Color c) { var n = new Vector3(c.r * 2f - 1f, c.g * 2f - 1f, c.b * 2f - 1f); return n.sqrMagnitude < 0.000001f ? Vector3.forward : n.normalized; }
        private static Vector3 BlendRnm(Vector3 basis, Vector3 detail) { Vector3 t = basis + Vector3.forward; Vector3 u = new Vector3(-detail.x, -detail.y, detail.z); Vector3 combined = t * Vector3.Dot(t, u) - u * t.z; return combined.sqrMagnitude < .000001f ? basis : combined.normalized; }
        private static Color EncodeNormal(Vector3 n) { n.Normalize(); return new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1f); }
        private static float ColorDistance(Color a, Color b) { float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b; return Mathf.Sqrt(dr * dr + dg * dg + db * db); }
        private static Color Median(TexturePaintReadOnlyPixels source, float u, float v,
            float du, float dv)
        {
            Span<Color> samples = stackalloc Color[9];
            int count = 0;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    samples[count++] = source.GetPixelBilinear(u + x * du, v + y * dv);
            for (int i = 1; i < samples.Length; i++)
            {
                Color value = samples[i];
                float luminance = Luma(value);
                int insertion = i - 1;
                while (insertion >= 0 && Luma(samples[insertion]) > luminance)
                {
                    samples[insertion + 1] = samples[insertion];
                    insertion--;
                }
                samples[insertion + 1] = value;
            }
            return samples[4];
        }
        private static Color MaskColor(Color source) { float value = Luma(source); return new Color(value, value, value, 1f); }
        private static Color Remap(Color c, float inMin, float inMax, float outMin, float outMax) { float d = Math.Max(.00001f, inMax - inMin); Func<float, float> f = x => Mathf.Lerp(outMin, outMax, Mathf.Clamp01((x - inMin) / d)); return new Color(f(c.r), f(c.g), f(c.b), c.a); }
        private static float Component(Color c, int component) { switch (component) { case 0: return c.r; case 1: return c.g; case 2: return c.b; case 3: return c.a; case 4: return Luma(c); case 6: return 1f; default: return 0f; } }
        private static Color Gradient(float t, TexturePaintPluginParameterSet p) { Color low = p.Color("gradientLow", Color.black), mid = p.Color("gradientMid", Color.gray), high = p.Color("gradientHigh", Color.white); return t < .5f ? Color.Lerp(low, mid, t * 2f) : Color.Lerp(mid, high, (t - .5f) * 2f); }
        private static float Fractal(float u, float v, float scale, int seed) { float sum = 0, weight = 0, amp = 1; for (int i = 0; i < 4; i++) { float o = seed * .00137f * (i + 1); sum += Mathf.PerlinNoise(u * scale + o, v * scale - o) * amp; weight += amp; amp *= .5f; scale *= 2f; } return sum / weight; }
        private static float SmoothStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / Math.Max(.00001f, b - a)); return t * t * (3f - 2f * t); }
        private static float Repeat(float value) => value - Mathf.Floor(value);
        private static int SurfaceLabel(TexturePaintReadOnlyMeshMap map, float u, float v) { if (map == null) return 0; Color c = map.GetPixel(Mathf.Clamp(Mathf.FloorToInt(u * map.width), 0, map.width - 1), Mathf.Clamp(Mathf.FloorToInt(v * map.height), 0, map.height - 1)); if (c.a < .01f) return -1; return Mathf.RoundToInt(c.b); }

        private static TexturePaintPluginParameterDefinition Header(string id, string name) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Header };
        private static TexturePaintPluginParameterDefinition Float(string id, string name, float value, float min, float max) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Float, defaultNumber = value, minimum = min, maximum = max };
        private static TexturePaintPluginParameterDefinition Integer(string id, string name, int value, int min, int max) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Integer, defaultNumber = value, minimum = min, maximum = max };
        private static TexturePaintPluginParameterDefinition Bool(string id, string name, bool value) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Boolean, defaultBoolean = value };
        private static TexturePaintPluginParameterDefinition ColorParameter(string id, string name, Color value) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Color, defaultColor = value };
        private static TexturePaintPluginParameterDefinition Texture(string id, string name) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Texture };
        private static TexturePaintPluginParameterDefinition EnumParameter(string id, string name, int value, string[] options) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Enum, defaultNumber = value, minimum = 0, maximum = Math.Max(0, options.Length - 1), enumOptions = options };
        private static TexturePaintPluginParameterDefinition Curve(string id, string name) => new TexturePaintPluginParameterDefinition { id = id, displayName = name, type = TexturePaintPluginParameterType.Curve, defaultCurve = AnimationCurve.Linear(0, 0, 1, 1) };
    }
}
