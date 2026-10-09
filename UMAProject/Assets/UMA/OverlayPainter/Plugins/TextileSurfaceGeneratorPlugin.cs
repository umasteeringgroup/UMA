using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>Quilting, embroidery, perforation and sprite-atlas scattering in one coordinated material generator.</summary>
    public sealed class TextileSurfaceGeneratorPlugin : ScriptableObject,
        ITexturePaintGeneratorV2, ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor = TextileSurfaceEngine.Descriptor();
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            TexturePaintChannelMask.None;
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) => TextileSurfaceEngine.Execute(context);
    }

    internal static class TextileSurfaceEngine
    {
        private const int Rows = 96;
        private enum Mode { Quilt, Embroidery, Perforation, AtlasScatter }

        public static TexturePaintPluginDescriptor Descriptor() => new TexturePaintPluginDescriptor
        {
            id = "com.uma.texturepaint.textile-surface",
            displayName = "Quilt, Embroidery, Perforation & Atlas Scatter",
            description = "Builds coordinated stitched, padded, punched, embroidered, or atlas-scattered material detail.",
            pluginVersion = "1.3.0",
            capabilities = TexturePaintPluginCapability.Generator | TexturePaintPluginCapability.LongRunning,
            declaredChannels = TexturePaintChannelMask.All,
            readChannels = TexturePaintChannelMask.All,
            supportedTargets = TexturePaintPluginTarget.All,
            channelSnapshotMaximumResolution = 4096,
            parameters = Parameters()
        };

        public static Task Execute(TexturePaintCommandContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var s = new Settings(context.parameters);
            var spriteSet = s.mode == Mode.Quilt ? context.source.GetParameterSpriteSet("quiltSpriteSet") : null;
            if (context.target == TexturePaintPluginTarget.LayerContent && !s.AnyOutput &&
                (spriteSet == null || !s.spriteChannels))
                throw new InvalidOperationException("Enable at least one output channel.");
            TexturePaintReadOnlyParameterTexture pattern = context.GetTextureParameter("pattern");
            TexturePaintReadOnlyParameterTexture atlas = context.GetTextureParameter("atlas");
            if (s.mode == Mode.AtlasScatter && atlas == null)
                throw new InvalidOperationException("Atlas Scatter requires an Atlas Texture. " +
                    "Assign a regular grid atlas in the Atlas Fabric section and set Atlas Columns and Atlas Rows " +
                    "to match it (use 1 by 1 for a single image). Alpha defines the stamp shape.");
            return Task.Run(() => Generate(context, s, pattern, atlas), context.cancellationToken);
        }

        private static void Generate(TexturePaintCommandContextV2 c, Settings s,
            TexturePaintReadOnlyParameterTexture pattern, TexturePaintReadOnlyParameterTexture atlas)
        {
            int surfaces = Math.Max(1, c.source.surfaceIds.Count);
            for (int si = 0; si < c.source.surfaceIds.Count; si++)
            {
                c.cancellationToken.ThrowIfCancellationRequested();
                string id = c.source.surfaceIds[si];
                if (c.target == TexturePaintPluginTarget.LayerMask)
                {
                    TexturePaintReadOnlyMask mask = c.source.GetMask(id);
                    if (mask == null) continue;
                    GenerateSize(c, s, pattern, atlas, id, mask.width, mask.height, si, surfaces, true);
                    continue;
                }
                var groups = new Dictionary<long, List<TexturePaintChannel>>();
                Add(TexturePaintChannel.Albedo, s.albedo); Add(TexturePaintChannel.Roughness, s.roughness);
                Add(TexturePaintChannel.Metallic, s.metallic); Add(TexturePaintChannel.AmbientOcclusion, s.ao);
                Add(TexturePaintChannel.NormalControl, s.normalControl);
                var spriteSet = s.mode == Mode.Quilt ? c.source.GetParameterSpriteSet("quiltSpriteSet") : null;
                if (spriteSet != null && s.spriteChannels)
                    foreach (var channel in spriteSet.channels)
                        if (channel != TexturePaintChannel.Albedo && channel != TexturePaintChannel.Roughness &&
                            channel != TexturePaintChannel.Metallic && channel != TexturePaintChannel.AmbientOcclusion &&
                            channel != TexturePaintChannel.NormalControl) Add(channel, true);
                foreach (KeyValuePair<long, List<TexturePaintChannel>> pair in groups)
                {
                    int width = (int)(pair.Key >> 32), height = (int)pair.Key;
                    GenerateSize(c, s, pattern, atlas, id, width, height, si, surfaces, false, pair.Value);
                }

                void Add(TexturePaintChannel channel, bool enabled)
                {
                    if (!enabled) return;
                    TexturePaintReadOnlyChannelInfo info = c.source.GetChannelInfo(id, channel);
                    if (info == null) return;
                    long key = ((long)info.width << 32) | (uint)info.height;
                    if (!groups.TryGetValue(key, out List<TexturePaintChannel> list))
                        groups.Add(key, list = new List<TexturePaintChannel>());
                    list.Add(channel);
                }
            }
        }

        private static void GenerateSize(TexturePaintCommandContextV2 c, Settings s,
            TexturePaintReadOnlyParameterTexture pattern, TexturePaintReadOnlyParameterTexture atlas,
            string id, int width, int height, int si, int surfaces, bool mask,
            List<TexturePaintChannel> channels = null)
        {
            var spriteSet = s.mode == Mode.Quilt ? c.source.GetParameterSpriteSet("quiltSpriteSet") : null;
            for (int y0 = 0; y0 < height; y0 += Rows)
            {
                c.cancellationToken.ThrowIfCancellationRequested();
                int rows = Math.Min(Rows, height - y0);
                var buffers = new Dictionary<TexturePaintChannel, Color32[]>();
                if (!mask) for (int i = 0; i < channels.Count; i++)
                    buffers[channels[i]] = new Color32[width * rows];
                Color32[] maskPixels = mask ? new Color32[width * rows] : null;
                Parallel.For(0, rows, new ParallelOptions
                    { CancellationToken = c.cancellationToken }, ly =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector2 uv = Rotate(new Vector2((x + .5f) / width,
                            (y0 + ly + .5f) / height), s.rotation);
                        Sample sample = SampleMode(s, uv, pattern, spriteSet == null ? atlas : null, new Vector2(1f / width, 1f / height));
                        float occlusion = s.shadingStrength > 0f
                            ? Mathf.Pow(Mathf.Max(.025f, sample.ao), s.shadingStrength) : 1f;
                        Color shaded = sample.color;
                        float contactShade = Mathf.Sqrt(occlusion);
                        shaded.r *= contactShade; shaded.g *= contactShade; shaded.b *= contactShade;
                        float relief = sample.height * s.depthStrength;
                        int index = ly * width + x;
                        if (mask)
                        {
                            byte m = B(sample.coverage);
                            maskPixels[index] = new Color32(m, m, m, 255);
                        }
                        else for (int i = 0; i < channels.Count; i++)
                        {
                            TexturePaintChannel channel = channels[i];
                            Color result = channel switch
                            {
                                TexturePaintChannel.Albedo => (Color32)shaded,
                                TexturePaintChannel.Roughness => Gray(sample.roughness),
                                TexturePaintChannel.Metallic => Gray(sample.metallic),
                                TexturePaintChannel.AmbientOcclusion => Gray(occlusion),
                                TexturePaintChannel.NormalControl => Gray(.5f + .45f * relief / (.45f + Mathf.Abs(relief))),
                                TexturePaintChannel.Normal => new Color(.5f, .5f, 1f, 1f),
                                _ => new Color(0f, 0f, 0f, 1f)
                            };
                            if (spriteSet != null)
                                result = SpriteSetPanel(s, spriteSet, uv, channel, result, sample.fabricWeight, contactShade);
                            buffers[channel][index] = (Color32)result;
                        }
                    }
                });
                RectInt rect = new RectInt(0, y0, width, rows);
                if (mask) c.WriteMaskTileCompactOwned(id, rect, maskPixels, TexturePaintPluginBlend.Replace);
                else foreach (KeyValuePair<TexturePaintChannel, Color32[]> pair in buffers)
                    c.WriteTileCompactOwned(id, pair.Key, rect, pair.Value,
                        TexturePaintChannelUtility.IsColor(pair.Key) ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data,
                        TexturePaintPluginBlend.Replace);
                c.progress?.Report((si + (y0 + rows) / (float)height) / surfaces);
            }
        }

        private static Sample SampleMode(Settings s, Vector2 uv,
            TexturePaintReadOnlyParameterTexture pattern, TexturePaintReadOnlyParameterTexture atlas, Vector2 pixelSize)
        {
            float noise = Fbm(uv * s.breakupScale, s.seed);
            switch (s.mode)
            {
                case Mode.Quilt:
                {
                    Vector2 q = uv * new Vector2(s.scale * s.aspect, s.scale);
                    // Rotate the lattice before wrapping so diamond seams stay continuous between cells.
                    if (s.quiltPattern == 1) q = new Vector2(q.x + q.y, q.y - q.x) * .70710678f;
                    float fx = Frac(q.x) - .5f, fy = Frac(q.y) - .5f;
                    float seam = .5f - Math.Max(Math.Abs(fx), Math.Abs(fy));
                    if (s.quiltPattern == 2) seam = Math.Abs(Mathf.Sin((fx + fy) * Mathf.PI)) * .5f;
                    float cellPixel = s.scale * Mathf.Max(s.aspect * pixelSize.x, pixelSize.y) * 1.42f;
                    float stitchWidth = Mathf.Max(s.stitchWidth, cellPixel * .65f);
                    float channel = 1f - Smooth(0f, stitchWidth * 2.5f, seam);
                    float across = Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow(seam / stitchWidth, 2f)));
                    float phase = Frac((q.x + q.y) * s.stitchDensity);
                    float along = Mathf.Sin(Mathf.Clamp01(phase / Mathf.Max(.01f, s.stitchLength)) * Mathf.PI);
                    float resolved = 1f - Smooth(.35f, .75f, cellPixel * s.stitchDensity);
                    float stitch = across * Mathf.Lerp(s.stitchLength * .637f, along, resolved);
                    // Padding bends in both cell directions. Nearest-edge distance creates
                    // pyramid faces and diagonal creases, even with a smoothed crown.
                    float puff = s.quiltPattern == 2
                        ? CushionAxis(Mathf.Cos((fx + fy) * Mathf.PI), s.puffExponent)
                        : CushionAxis(fx * 2f, s.puffExponent) * CushionAxis(fy * 2f, s.puffExponent);
                    float cover = Mathf.Clamp01(Mathf.Max(puff * .35f, stitch));
                    Color fabric = QuiltPanelFabric(s, q, atlas);
                    Color color = Color.Lerp(fabric, s.accentColor, stitch * s.colorAmount);
                    return new Sample(color, Mathf.Clamp01(s.baseRoughness + stitch * .12f - puff * .08f),
                        s.baseMetallic, 1f - s.aoStrength * Mathf.Clamp01(channel * .65f + (channel - stitch) * .2f),
                        (puff * s.puffHeight - channel * s.stitchDepth + stitch * s.stitchDepth * .65f) * (.85f + noise * .15f), cover,
                        1f - Mathf.Clamp01(stitch * s.colorAmount));
                }
                case Mode.Embroidery:
                {
                    float motif = Pattern(pattern, uv * s.patternTiling + s.offset, s.patternThreshold);
                    if (pattern == null) motif = Mathf.Clamp01(.5f + .5f * Mathf.Sin((uv.x + uv.y) * s.scale * 6.283f));
                    float angle = s.fiberDirection * Mathf.Deg2Rad;
                    float along = uv.x * Mathf.Cos(angle) + uv.y * Mathf.Sin(angle);
                    float thread = Mathf.Pow(.5f + .5f * Mathf.Cos(along * s.threadDensity * 6.283f), 3f);
                    float footprint = s.threadDensity * (Mathf.Abs(Mathf.Cos(angle)) * pixelSize.x + Mathf.Abs(Mathf.Sin(angle)) * pixelSize.y);
                    thread = Mathf.Lerp(.3125f, thread, 1f - Smooth(.35f, .75f, footprint));
                    float broken = Smooth(s.breakup - .2f, s.breakup + .2f, noise);
                    float cover = motif * Mathf.Lerp(1f, broken, s.breakup);
                    Color color = Color.Lerp(s.baseColor, s.accentColor,
                        cover * (.65f + thread * .35f) * s.colorAmount);
                    return new Sample(color, Mathf.Clamp01(s.baseRoughness - cover * s.sheen + thread * .08f),
                        s.baseMetallic, Mathf.Clamp01(1f - cover * s.aoStrength * (.12f + (1f - thread) * .42f)),
                        cover * s.embroideryHeight * (.65f + thread * .35f), cover);
                }
                case Mode.Perforation:
                {
                    Vector2 p = uv * new Vector2(s.scale * s.aspect, s.scale);
                    int iy = Mathf.FloorToInt(p.y); float px = Frac(p.x) - .5f, py = Frac(p.y) - .5f;
                    if (s.perforationPattern == 1 && (iy & 1) != 0) px = Frac(p.x + .5f) - .5f;
                    if (s.perforationPattern == 2) { px += (Hash(Mathf.FloorToInt(p.x), iy, s.seed) - .5f) * s.jitter; py += (Hash(iy, Mathf.FloorToInt(p.x), s.seed + 19) - .5f) * s.jitter; }
                    float d = Mathf.Sqrt(px * px + py * py);
                    float hole = 1f - Smooth(s.holeRadius, s.holeRadius + s.edgeSoftness, d);
                    float bevel = Smooth(s.holeRadius, s.holeRadius + s.bevelWidth, d) *
                                  (1f - Smooth(s.holeRadius + s.bevelWidth, s.holeRadius + s.bevelWidth * 2f, d));
                    float cover = Mathf.Clamp01(hole * (.65f + noise * .35f));
                    Color color = Color.Lerp(s.baseColor, s.holeColor, cover);
                    return new Sample(color, Mathf.Lerp(s.baseRoughness, s.holeRoughness, cover),
                        Mathf.Lerp(s.baseMetallic, 0f, cover), Mathf.Clamp01(1f - (cover + bevel * .22f) * s.aoStrength),
                        bevel * s.bevelHeight - cover * s.holeDepth, cover);
                }
                default:
                    return Atlas(s, uv, atlas);
            }
        }

        private static Sample Atlas(Settings s, Vector2 uv, TexturePaintReadOnlyParameterTexture atlas)
        {
            if (atlas == null) return new Sample(s.baseColor, s.baseRoughness, s.baseMetallic, 1f, 0f, 0f);
            Vector2 grid = uv * new Vector2(s.scatterGridX, s.scatterGridY);
            int cx = Mathf.FloorToInt(grid.x), cy = Mathf.FloorToInt(grid.y);
            float presence = Hash(cx, cy, s.seed);
            if (presence > s.density) return new Sample(s.baseColor, s.baseRoughness, s.baseMetallic, 1f, 0f, 0f);
            float h1 = Hash(cx, cy, s.seed + 37), h2 = Hash(cx, cy, s.seed + 91);
            Vector2 local = new Vector2(Frac(grid.x), Frac(grid.y)) - new Vector2(.5f + (h1 - .5f) * s.jitter, .5f + (h2 - .5f) * s.jitter);
            float size = s.scatterSize * Mathf.Lerp(1f - s.sizeVariation, 1f + s.sizeVariation, Hash(cx, cy, s.seed + 131));
            local /= Math.Max(.02f, size);
            float angle = (Hash(cx, cy, s.seed + 211) - .5f) * s.rotationVariation * Mathf.Deg2Rad;
            local = new Vector2(local.x * Mathf.Cos(angle) - local.y * Mathf.Sin(angle), local.x * Mathf.Sin(angle) + local.y * Mathf.Cos(angle));
            if (Math.Abs(local.x) > .5f || Math.Abs(local.y) > .5f)
                return new Sample(s.baseColor, s.baseRoughness, s.baseMetallic, 1f, 0f, 0f);
            int cell = Mathf.FloorToInt(Hash(cx, cy, s.seed + 313) * s.atlasColumns * s.atlasRows);
            int ax = cell % s.atlasColumns, ay = cell / s.atlasColumns;
            Vector2 auv = new Vector2((ax + local.x + .5f) / s.atlasColumns, (ay + local.y + .5f) / s.atlasRows);
            Color stamp = atlas.GetPixelBilinear(auv.x, auv.y);
            float cover = stamp.a * Mathf.Clamp01((stamp.r + stamp.g + stamp.b) / 3f * s.luminanceMask + (1f - s.luminanceMask));
            // A short contact halo seats the stamp on the cloth, while its center
            // retains the atlas color instead of receiving a flat black silhouette.
            float nearby = cover;
            Vector2 step = new Vector2(1f / atlas.width, 1f / atlas.height);
            for (int i = 0; i < 4; i++)
            {
                Vector2 offset = i < 2 ? new Vector2(i == 0 ? step.x : -step.x, 0)
                    : new Vector2(0, i == 2 ? step.y : -step.y);
                Vector2 tap = auv + offset;
                if (tap.x < ax / (float)s.atlasColumns || tap.x >= (ax + 1f) / s.atlasColumns ||
                    tap.y < ay / (float)s.atlasRows || tap.y >= (ay + 1f) / s.atlasRows) continue;
                Color neighbour = atlas.GetPixelBilinear(tap.x, tap.y);
                float coverage = neighbour.a * Mathf.Clamp01((neighbour.r + neighbour.g + neighbour.b) / 3f * s.luminanceMask + 1f - s.luminanceMask);
                nearby = Mathf.Max(nearby, coverage);
            }
            float contact = Mathf.Clamp01(cover * .12f + (nearby - cover) * .7f + cover * (1f - cover) * .45f);
            float tint = Mathf.Lerp(1f - s.tintVariation, 1f + s.tintVariation, h2);
            Color stamped = s.useAtlasColor ? new Color(stamp.r * tint, stamp.g * tint, stamp.b * tint, 1f) : s.accentColor * tint;
            Color color = Color.Lerp(s.baseColor, stamped, cover * s.colorAmount);
            return new Sample(color, Mathf.Clamp01(s.baseRoughness + cover * s.scatterRoughness),
                Mathf.Clamp01(s.baseMetallic + cover * s.scatterMetallic), Mathf.Clamp01(1f - contact * s.aoStrength),
                cover * s.scatterHeight, cover);
        }

        private static Color QuiltPanelFabric(Settings s, Vector2 lattice, TexturePaintReadOnlyParameterTexture atlas)
        {
            if (atlas == null || s.quiltAtlasAmount <= 0f) return s.baseColor;
            // The same lattice defines padding, seams, and fabric. One cell chooses one tile;
            // scatter density, size and placement must never create extra stamps inside a panel.
            if (s.quiltPattern == 2)
                lattice = new Vector2(lattice.x + lattice.y, (lattice.y - lattice.x) * .5f);
            int count = s.atlasColumns * s.atlasRows;
            int cell = s.quiltAtlasRandom
                ? Mathf.Min(count - 1, Mathf.FloorToInt(Hash(Mathf.FloorToInt(lattice.x),
                    Mathf.FloorToInt(lattice.y), s.seed + 313) * count))
                : Mathf.Clamp(s.quiltAtlasCell - 1, 0, count - 1);
            int column = cell % s.atlasColumns, row = cell / s.atlasColumns;
            // Clamp to this tile's texel centers, so a neighboring atlas tile cannot bleed
            // into the panel even when an image reaches all the way to its stitched edge.
            float insetX = Mathf.Min(.5f / atlas.width, .5f / s.atlasColumns);
            float insetY = Mathf.Min(.5f / atlas.height, .5f / s.atlasRows);
            float u = Mathf.Lerp(column / (float)s.atlasColumns + insetX,
                (column + 1f) / s.atlasColumns - insetX, Frac(lattice.x));
            float v = Mathf.Lerp(row / (float)s.atlasRows + insetY,
                (row + 1f) / s.atlasRows - insetY, Frac(lattice.y));
            Color image = atlas.GetPixelBilinear(u, v);
            Color fabric = Color.Lerp(s.baseColor, image, Mathf.Clamp01(image.a * s.quiltAtlasAmount));
            fabric.a = s.baseColor.a;
            return fabric;
        }

        private static Color SpriteSetPanel(Settings s, TexturePaintReadOnlySpriteSet source, Vector2 uv,
            TexturePaintChannel channel, Color background, float fabricWeight, float contactShade)
        {
            if (s.quiltAtlasAmount <= 0f) return background;
            Vector2 q = uv * new Vector2(s.scale * s.aspect, s.scale);
            if (s.quiltPattern == 1) q = new Vector2(q.x + q.y, q.y - q.x) * .70710678f;
            if (s.quiltPattern == 2) q = new Vector2(q.x + q.y, (q.y - q.x) * .5f);
            int choice = s.quiltAtlasRandom
                ? Mathf.Min(source.enabledIndices.Count - 1, Mathf.FloorToInt(Hash(Mathf.FloorToInt(q.x),
                    Mathf.FloorToInt(q.y), s.seed + 313) * source.enabledIndices.Count)) : 0;
            int tile = source.enabledIndices[choice];
            // A disabled fixed tile falls back to the first enabled tile, never to a disabled image.
            if (!s.quiltAtlasRandom)
                for (int i = 0; i < source.enabledIndices.Count; i++)
                    if (source.enabledIndices[i] == s.quiltAtlasCell - 1) { tile = s.quiltAtlasCell - 1; break; }
            var image = source.GetTile(tile, channel);
            if (image == null) return background;
            Color Read(TexturePaintReadOnlyParameterTexture texture) => texture.GetPixelBilinear(
                Mathf.Lerp(.5f / texture.width, 1f - .5f / texture.width, Frac(q.x)),
                Mathf.Lerp(.5f / texture.height, 1f - .5f / texture.height, Frac(q.y)));
            Color pixel = Read(image);
            float coverage = Mathf.Clamp01(Read(source.GetTile(tile, TexturePaintChannel.Albedo)).a * s.quiltAtlasAmount) * fabricWeight;
            if (channel == TexturePaintChannel.Albedo)
            {
                pixel.r *= contactShade; pixel.g *= contactShade; pixel.b *= contactShade;
                pixel.a = background.a;
            }
            else
            {
                coverage *= pixel.a;
                if (channel == TexturePaintChannel.Normal)
                {
                    Vector3 normal = new Vector3(pixel.r * 2f - 1f, pixel.g * 2f - 1f, pixel.b * 2f - 1f);
                    // Transform the tile's tangent frame back into the garment UV frame.
                    float angle = (s.rotation - (s.quiltPattern == 0 ? 0f : 45f)) * Mathf.Deg2Rad;
                    float x = normal.x * Mathf.Cos(angle) + normal.y * Mathf.Sin(angle);
                    float y = -normal.x * Mathf.Sin(angle) + normal.y * Mathf.Cos(angle);
                    normal = Vector3.Lerp(Vector3.forward, new Vector3(x, y, normal.z), coverage).normalized;
                    return new Color(normal.x * .5f + .5f, normal.y * .5f + .5f, normal.z * .5f + .5f, 1f);
                }
                if (channel == TexturePaintChannel.NormalControl)
                    return Gray(background.r + (pixel.r - .5f) * coverage);
                if (channel == TexturePaintChannel.AmbientOcclusion)
                    return Gray(background.r * Mathf.Lerp(1f, pixel.r, coverage));
                if (TexturePaintChannelUtility.IsGrayscale(channel))
                    pixel = new Color(pixel.r, pixel.r, pixel.r, 1f);
                else pixel.a = 1f;
            }
            return Color.Lerp(background, pixel, coverage);
        }

        private static float Pattern(TexturePaintReadOnlyParameterTexture texture, Vector2 uv, float threshold)
        {
            if (texture == null) return 0f;
            Color c = texture.GetPixelBilinear(Frac(uv.x), Frac(uv.y));
            float l = c.a * (c.r * .2126f + c.g * .7152f + c.b * .0722f);
            return Smooth(threshold - .08f, threshold + .08f, l);
        }

        private static float CushionAxis(float position, float exponent)
        {
            // Zero slope at the crown and stitched edges keeps the whole puff rounded.
            // A larger exponent broadens the top without reducing the padding height.
            float shoulder = 1f - Mathf.Pow(Mathf.Clamp01(Mathf.Abs(position)), exponent);
            return shoulder * shoulder;
        }

        private readonly struct Sample
        {
            public readonly Color color; public readonly float roughness, metallic, ao, height, coverage, fabricWeight;
            public Sample(Color color, float roughness, float metallic, float ao, float height, float coverage, float fabricWeight = 1f)
            { this.color = color; this.roughness = roughness; this.metallic = metallic; this.ao = ao; this.height = height; this.coverage = coverage; this.fabricWeight = fabricWeight; }
        }

        private sealed class Settings
        {
            public readonly Mode mode; public readonly bool albedo, roughness, metallic, ao, normalControl;
            public readonly Color baseColor, accentColor, holeColor; public readonly float baseRoughness, baseMetallic,
                scale, aspect, rotation, breakupScale, colorAmount, stitchWidth, stitchDensity, stitchLength,
                puffHeight, puffExponent, stitchDepth, aoStrength, patternTiling, patternThreshold, fiberDirection,
                threadDensity, breakup, sheen, embroideryHeight, holeRadius, edgeSoftness, bevelWidth, bevelHeight,
                holeDepth, holeRoughness, jitter, density, scatterSize, sizeVariation, rotationVariation,
                luminanceMask, tintVariation, scatterRoughness, scatterMetallic, scatterHeight, depthStrength, shadingStrength;
            public readonly int seed, quiltPattern, perforationPattern, atlasColumns, atlasRows, scatterGridX, scatterGridY;
            public readonly bool useAtlasColor, quiltAtlasRandom; public readonly Vector2 offset;
            public readonly int quiltAtlasCell;
            public readonly float quiltAtlasAmount;
            public readonly bool spriteChannels;
            public bool AnyOutput => albedo || roughness || metallic || ao || normalControl;
            public Settings(TexturePaintPluginParameterSet p)
            {
                mode = (Mode)Mathf.Clamp(p.Integer("mode"), 0, 3); albedo = p.Boolean("outputAlbedo", true);
                roughness = p.Boolean("outputRoughness", true); metallic = p.Boolean("outputMetallic");
                ao = p.Boolean("outputAO", true); normalControl = p.Boolean("outputNormalControl", true);
                baseColor = p.LinearColor("baseColor", new Color(.35f,.28f,.22f,1)); accentColor = p.LinearColor("accentColor", Color.white);
                holeColor = p.LinearColor("holeColor", new Color(.025f,.02f,.015f,1)); baseRoughness=p.Float("baseRoughness",.65f);
                baseMetallic=p.Float("baseMetallic",0); scale=p.Float("scale",12); aspect=p.Float("aspect",1); rotation=p.Float("rotation",0);
                breakupScale=p.Float("breakupScale",8); seed=p.Integer("seed",1731); colorAmount=p.Float("colorAmount",.7f);
                quiltPattern=p.Integer("quiltPattern",1); stitchWidth=p.Float("stitchWidth",.035f); stitchDensity=p.Float("stitchDensity",10);
                stitchLength=p.Float("stitchLength",.6f); puffHeight=p.Float("puffHeight",.16f);
                puffExponent=Mathf.Lerp(2f,4f,Mathf.InverseLerp(.25f,8f,p.Float("puffRoundness",1.7f)));
                stitchDepth=p.Float("stitchDepth",.12f); aoStrength=p.Float("aoStrength",.7f); patternTiling=p.Float("patternTiling",3);
                patternThreshold=p.Float("patternThreshold",.4f); fiberDirection=p.Float("fiberDirection",45); threadDensity=p.Float("threadDensity",180);
                breakup=p.Float("breakup",.18f); sheen=p.Float("sheen",.22f); embroideryHeight=p.Float("embroideryHeight",.18f);
                offset=new Vector2(p.Float("offsetX"),p.Float("offsetY")); perforationPattern=p.Integer("perforationPattern",1);
                holeRadius=p.Float("holeRadius",.24f); edgeSoftness=p.Float("edgeSoftness",.018f); bevelWidth=p.Float("bevelWidth",.09f);
                bevelHeight=p.Float("bevelHeight",.1f); holeDepth=p.Float("holeDepth",.28f); holeRoughness=p.Float("holeRoughness",.86f);
                jitter=p.Float("jitter",.18f); atlasColumns=Mathf.Max(1,p.Integer("atlasColumns",4)); atlasRows=Mathf.Max(1,p.Integer("atlasRows",4));
                scatterGridX=Mathf.Max(1,p.Integer("scatterGridX",8)); scatterGridY=Mathf.Max(1,p.Integer("scatterGridY",8)); density=p.Float("density",.6f);
                scatterSize=p.Float("scatterSize",.75f); sizeVariation=p.Float("sizeVariation",.3f); rotationVariation=p.Float("rotationVariation",180);
                luminanceMask=p.Float("luminanceMask",0); useAtlasColor=p.Boolean("useAtlasColor",true); tintVariation=p.Float("tintVariation",.12f);
                quiltAtlasRandom=p.Integer("quiltAtlasSelection",1)==1;
                quiltAtlasCell=p.Integer("quiltAtlasCell",1);
                quiltAtlasAmount=Mathf.Clamp01(p.Float("quiltAtlasAmount",1));
                spriteChannels=p.Boolean("outputSpriteChannels",true);
                scatterRoughness=p.Float("scatterRoughness",-.1f); scatterMetallic=p.Float("scatterMetallic",0); scatterHeight=p.Float("scatterHeight",.12f);
                depthStrength=Mathf.Clamp(p.Float("depthStrength",1),0,4);
                shadingStrength=Mathf.Clamp(p.Float("shadingStrength",1),0,3);
            }
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters() => WithPanelAtlas(new List<TexturePaintPluginParameterDefinition>
        {
            F("depthStrength","3D Depth",0,4,1,"Scales raised and recessed relief without clipping. Zero produces a flat height map."),
            F("shadingStrength","Relief Shading",0,3,1,"Contact shading in albedo and ambient occlusion, independent of height."),
            H("modeHeader","Surface System","Choose one production surface system; settings remain stored when switching modes."), E("mode","Mode",new[]{"Quilt","Embroidery","Perforation","Atlas Scatter"},0,"Generator mode."),
            H("outputs","Output Channels","In mask mode these material outputs are ignored and Coverage is written as grayscale."), B("outputAlbedo","Albedo",true,"Color output."), B("outputRoughness","Roughness",true,"Surface roughness."), B("outputMetallic","Metallic",false,"Metal response."), B("outputAO","Ambient Occlusion",true,"Crease/recess occlusion."), B("outputNormalControl","Normal Control",true,"Raised and recessed height."),
            H("common","Common Material","Shared scale, orientation, color and breakup."), C("baseColor","Base Color",new Color(.35f,.28f,.22f,1),"Underlying material."), C("accentColor","Thread / Stamp Color",Color.white,"Stitches, embroidery, or colorized atlas stamps."), F("baseRoughness","Base Roughness",0,1,.65f,"Underlying roughness."), F("baseMetallic","Base Metallic",0,1,0,"Underlying metallic."), F("scale","Pattern Scale",1,256,12,"Pattern repetitions per UV tile."), F("aspect","Aspect",.1f,10,1,"Horizontal pattern aspect."), F("rotation","Rotation",-180,180,0,"Pattern rotation."), I("seed","Seed",0,999999,1731,"Repeatable variation."), F("breakupScale","Breakup Scale",.1f,128,8,"Fractal breakup frequency."), F("colorAmount","Accent Color Amount",0,1,.7f,"Color contribution in Quilt, Embroidery and Atlas Scatter, including atlas RGB. Perforation uses its separate Recess Color."), F("aoStrength","AO Strength",0,1,.7f,"Recess darkening."),
            H("quilt","Quilt","Padded cells, seam channels, and individual stitches."), E("quiltPattern","Pattern",new[]{"Square Channels","Diamond Channels","Wave Channels"},1,"Quilting layout."), F("stitchWidth","Stitch Width",.002f,.15f,.035f,"Stitch/seam width within a cell."), F("stitchDensity","Stitches / Cell",1,64,10,"Individual stitch frequency."), F("stitchLength","Stitch Duty",.05f,.95f,.6f,"Thread length versus gap."), F("puffHeight","Puff Height",0,.5f,.16f,"Raised padding."), F("puffRoundness","Puff Roundness",.25f,8,1.7f,"Higher values broaden the cushion top with rounded shoulders; padding height stays constant."), F("stitchDepth","Seam Depth",0,.5f,.12f,"Recessed seam height."),
            H("embroidery","Embroidery","Sprite-defined motifs filled with directional thread."), T("pattern","Pattern Texture","Alpha/luminance defines embroidered coverage."), F("patternTiling","Pattern Repeats",.1f,64,3,"Motif repetitions."), F("patternThreshold","Pattern Threshold",0,1,.4f,"Coverage cutoff."), F("offsetX","Offset X",-16,16,0,"Motif offset."), F("offsetY","Offset Y",-16,16,0,"Motif offset."), F("fiberDirection","Thread Direction",-180,180,45,"Satin stitch direction."), F("threadDensity","Thread Density",4,1024,180,"Visible thread ridges."), F("breakup","Thread Breakup",0,1,.18f,"Natural incomplete fibers."), F("sheen","Thread Sheen",0,1,.22f,"Roughness reduction on thread crowns."), F("embroideryHeight","Embroidery Height",0,.5f,.18f,"Raised thread height."),
            H("perforation","Perforation","Punched holes with bevel, depth and controllable spread. This shades holes; it does not alter mesh topology."), E("perforationPattern","Hole Layout",new[]{"Grid","Hex / Staggered","Organic Jitter"},1,"Hole distribution."), C("holeColor","Recess Color",new Color(.025f,.02f,.015f,1),"Interior/recess color."), F("holeRadius","Hole Radius",.01f,.48f,.24f,"Hole size within each repeat."), F("edgeSoftness","Edge Softness",.001f,.2f,.018f,"Antialias/edge wear."), F("bevelWidth","Bevel Width",.005f,.3f,.09f,"Rolled edge spread."), F("bevelHeight","Bevel Height",0,.5f,.1f,"Raised lip."), F("holeDepth","Hole Depth",0,.5f,.28f,"Normal Control recess."), F("holeRoughness","Interior Roughness",0,1,.86f,"Roughness inside holes."), F("jitter","Position Jitter",0,.8f,.18f,"Organic displacement."),
            H("atlasHeader","Atlas Scatter","Random cells from a regular texture atlas, with deterministic transform variation."), T("atlas","Atlas Texture","A regular grid atlas; alpha defines each stamp."), I("atlasColumns","Atlas Columns",1,64,4,"Cells across."), I("atlasRows","Atlas Rows",1,64,4,"Cells down."), I("scatterGridX","Scatter Columns",1,256,8,"Candidate stamps across UV."), I("scatterGridY","Scatter Rows",1,256,8,"Candidate stamps down UV."), F("density","Density",0,1,.6f,"Occupied candidates."), F("scatterSize","Stamp Size",.02f,2,.75f,"Size relative to candidate cell."), F("sizeVariation","Size Variation",0,1,.3f,"Random shrink/grow."), F("rotationVariation","Rotation Variation",0,360,180,"Random angle range."), F("luminanceMask","Use Luminance as Mask",0,1,0,"Blends alpha-only and alpha-times-luminance coverage."), B("useAtlasColor","Use Atlas Color",true,"Uses atlas RGB rather than Accent Color."), F("tintVariation","Tint Variation",0,1,.12f,"Per-stamp brightness variation."), F("scatterRoughness","Roughness Change",-1,1,-.1f,"Stamp roughness delta."), F("scatterMetallic","Metallic Change",-1,1,0,"Stamp metallic delta."), F("scatterHeight","Stamp Height",-.5f,.5f,.12f,"Normal Control height."),
        });

        private static List<TexturePaintPluginParameterDefinition> WithPanelAtlas(List<TexturePaintPluginParameterDefinition> parameters)
        {
            var source = new List<TexturePaintPluginParameterDefinition>
            {
                H("atlasSource", "Atlas Fabric", "Quilt fits one fabric tile to each panel and retains its stitches and relief. Use a Sprite Set for all material channels, or a texture atlas for color. Atlas Scatter uses the texture for separate stamps.")
            };
            // Reuse the saved atlas reference/grid for both modes, without duplicate IDs.
            foreach (string id in new[] { "atlas", "atlasColumns", "atlasRows" })
            {
                var parameter = parameters.Find(p => p.id == id);
                parameters.Remove(parameter);
                if (id == "atlas") parameter.description = "A regular grid atlas. Quilt fits one tile to each panel; " +
                    "Atlas Scatter places stamps. Transparent areas reveal Base Color in Quilt.";
                source.Add(parameter);
            }
            source.Add(E("quiltAtlasSelection", "Panel Tile Selection", new[] { "Selected Cell", "Random per Panel" }, 1,
                "Each quilt panel contains exactly one image. Random selection is stable for its panel and Seed."));
            source.Add(I("quiltAtlasCell", "Selected Cell", 1, 4096, 1,
                "Sprite Set: use the number in Select Tiles; a disabled cell falls back to the first enabled tile. Texture: count from 1, left to right, starting at the bottom row."));
            source.Add(F("quiltAtlasAmount", "Fabric Amount", 0, 1, 1,
                "Blend fabric channels into each panel. Zero keeps the plain quilt. Albedo alpha reveals the underlying quilt."));
            source.Insert(1, new TexturePaintPluginParameterDefinition { id = "quiltSpriteSet", displayName = "Sprite Set",
                description = "Optional multi-channel fabric. Takes priority over Atlas Texture in Quilt. Choose enabled tiles below.",
                type = TexturePaintPluginParameterType.SpriteSet });
            source.Add(B("outputSpriteChannels", "Additional Sprite Channels", true,
                "Include Normal, Emission, Custom and mask channels supplied by the Sprite Set. The output toggles above control its other channels."));
            parameters.InsertRange(parameters.FindIndex(p => p.id == "quilt"), source);
            return parameters;
        }

        private static TexturePaintPluginParameterDefinition H(string id,string n,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Header};
        private static TexturePaintPluginParameterDefinition F(string id,string n,float min,float max,float v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Float,minimum=min,maximum=max,defaultNumber=v};
        private static TexturePaintPluginParameterDefinition I(string id,string n,int min,int max,int v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Integer,minimum=min,maximum=max,defaultNumber=v};
        private static TexturePaintPluginParameterDefinition B(string id,string n,bool v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Boolean,defaultBoolean=v};
        private static TexturePaintPluginParameterDefinition C(string id,string n,Color v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Color,defaultColor=v};
        private static TexturePaintPluginParameterDefinition T(string id,string n,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Texture};
        private static TexturePaintPluginParameterDefinition E(string id,string n,string[] o,int v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Enum,minimum=0,maximum=o.Length-1,defaultNumber=v,enumOptions=o};
        private static Vector2 Rotate(Vector2 uv,float degrees){float a=degrees*Mathf.Deg2Rad,c=Mathf.Cos(a),s=Mathf.Sin(a);uv-=Vector2.one*.5f;return new Vector2(uv.x*c-uv.y*s,uv.x*s+uv.y*c)+Vector2.one*.5f;}
        private static float Fbm(Vector2 p,int seed){float v=0,a=.55f;for(int i=0;i<5;i++){v+=Noise(p,seed+i*47)*a;p=p*2.03f+new Vector2(17.1f,9.7f);a*=.48f;}return Mathf.Clamp01(v*.94f);}private static float Noise(Vector2 p,int seed){int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y);float tx=Frac(p.x),ty=Frac(p.y);tx=tx*tx*(3-2*tx);ty=ty*ty*(3-2*ty);return Mathf.Lerp(Mathf.Lerp(Hash(x,y,seed),Hash(x+1,y,seed),tx),Mathf.Lerp(Hash(x,y+1,seed),Hash(x+1,y+1,seed),tx),ty);}
        private static float Hash(int x,int y,int seed){unchecked{uint h=(uint)(x*374761393+y*668265263+seed*1442695041);h=(h^(h>>13))*1274126177;return (h^(h>>16))/4294967295f;}}
        private static float Frac(float v)=>v-Mathf.Floor(v); private static float Step(float v,float edge)=>v<=edge?1f:0f;
        private static float Smooth(float a,float b,float v){float t=Mathf.Clamp01((v-a)/Math.Max(.000001f,b-a));return t*t*(3-2*t);}
        private static byte B(float v)=>(byte)Mathf.RoundToInt(Mathf.Clamp01(v)*255); private static Color32 Gray(float v){byte b=B(v);return new Color32(b,b,b,255);}
    }
}
