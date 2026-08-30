using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>
    /// Generates a transparent skin overlay containing downward-growing, tapered stubble and
    /// optional irritation details. Every output uses authored coverage as alpha, so no base skin
    /// is baked into the layer.
    /// </summary>
    public sealed class StubbleMakerGeneratorPlugin : ScriptableObject,
        ITexturePaintGeneratorV2, ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            StubbleMakerGeneratorEngine.CreateDescriptor();

        public TexturePaintPluginDescriptor Descriptor => descriptor;

        public TexturePaintChannelMask ResolveReadChannels(
            TexturePaintPluginParameterSet parameters) => TexturePaintChannelMask.None;

        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            StubbleMakerGeneratorEngine.ExecuteAsync(context);
    }

    internal static class StubbleMakerGeneratorEngine
    {
        private const int RowsPerTile = 128;
        private const string ControlMask = "controlMask";
        private const TexturePaintChannelMask OutputChannels =
            TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Roughness |
            TexturePaintChannelMask.NormalControl | TexturePaintChannelMask.SkinColorMask |
            TexturePaintChannelMask.DetailMask;

        public static TexturePaintPluginDescriptor CreateDescriptor() =>
            new TexturePaintPluginDescriptor
            {
                id = "com.uma.texturepaint.stubble-maker",
                displayName = "Skin — Stubble Maker",
                description = "Creates a transparent overlay of tapered hair stubble growing " +
                              "downward, with controllable length, width, color, placement and " +
                              "deterministic position/direction variation. Optional rash, pimples " +
                              "and pigment spots add coordinated skin shading and relief.",
                pluginVersion = "1.0.0",
                capabilities = TexturePaintPluginCapability.Generator |
                               TexturePaintPluginCapability.LongRunning,
                declaredChannels = OutputChannels,
                // Dynamic channel usage narrows this write-only generator to dimension metadata.
                readChannels = TexturePaintChannelMask.All,
                channelSnapshotMaximumResolution = 4096,
                parameters = Parameters()
            };

        public static Task ExecuteAsync(TexturePaintCommandContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var settings = new Settings(context.parameters);
            TexturePaintReadOnlyParameterTexture mask = context.GetTextureParameter(ControlMask);
            return Task.Run(() => Execute(context, settings, mask), context.cancellationToken);
        }

        private static void Execute(TexturePaintCommandContextV2 context, Settings settings,
            TexturePaintReadOnlyParameterTexture mask)
        {
            int surfaceCount = Math.Max(1, context.source.surfaceIds.Count);
            for (int surfaceIndex = 0; surfaceIndex < context.source.surfaceIds.Count; surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];
                List<OutputTarget> targets = OutputTarget.Find(context.source, surfaceId);
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
                        if (output.any)
                            for (int i = 0; i < group.Count; i++)
                                Write(context, surfaceId, group[i], y0, rows, output);
                        context.progress?.Report((surfaceIndex + (y0 + rows) / (float)height) /
                                                 surfaceCount);
                    }
                }
            }
            context.progress?.Report(1f);
        }

        private static OutputBuffers Generate(Settings s,
            TexturePaintReadOnlyParameterTexture mask, int width, int height, int y0, int rows,
            TexturePaintChannelMask channels, TexturePaintCommandContextV2 context)
        {
            var output = new OutputBuffers(width * rows, channels);
            Parallel.For(0, rows, new ParallelOptions
            {
                CancellationToken = context.cancellationToken
            }, localY =>
            {
                int y = y0 + localY;
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float placement = PlacementCoverage(s, u, v, width, height);
                    if (placement <= 0f) continue;
                    if (mask != null)
                    {
                        Color sample = mask.GetPixelBilinear(u, v);
                        placement *= Mathf.Clamp01(Luminance(sample) * sample.a);
                    }
                    placement *= s.overallAmount;
                    if (placement <= 0.0001f) continue;

                    Vector2 pixel = new Vector2(x + 0.5f, y + 0.5f);
                    HairSample hair = SampleHair(s, pixel);
                    SkinSample skin = SampleSkin(s, pixel);
                    GeneratedPixel generated = Combine(s, hair, skin, placement);
                    if (!generated.Any) continue;
                    output.Set(localY * width + x, generated);
                }
            });
            return output;
        }

        private static HairSample SampleHair(Settings s, Vector2 pixel)
        {
            float angle = s.directionDegrees * Mathf.Deg2Rad;
            Vector2 down = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
            Vector2 across = new Vector2(-down.y, down.x);
            float acrossPosition = Vector2.Dot(pixel, across);
            float alongPosition = Vector2.Dot(pixel, down);
            float profileLength = s.profile == 1 ? 0.55f : 1f;
            float profileWidth = s.profile == 1 ? 0.8f : 1f;
            float profileDensity = s.profile == 1
                ? Mathf.Clamp01(s.density + 0.15f) : s.density;
            float baseLength = s.hairLength * profileLength;
            float baseWidth = s.hairWidth * profileWidth;
            float acrossSpacing = Mathf.Max(baseWidth * 2.5f,
                Mathf.Lerp(20f, 3f, profileDensity));
            float alongSpacing = Mathf.Max(baseLength * 1.15f,
                Mathf.Lerp(28f, 5f, profileDensity));
            int baseX = Mathf.FloorToInt(acrossPosition / acrossSpacing);
            int baseY = Mathf.FloorToInt(alongPosition / alongSpacing);
            HairSample best = default;

            for (int cy = baseY - 2; cy <= baseY + 1; cy++)
            for (int cx = baseX - 1; cx <= baseX + 1; cx++)
            {
                float existence = Hash(cx, cy, s.seed + 3);
                if (existence > profileDensity) continue;
                float rx = SignedHash(cx, cy, s.seed + 11) * s.randomPositionX;
                float ry = SignedHash(cx, cy, s.seed + 17) * s.randomPositionY;
                Vector2 root = across * ((cx + 0.5f) * acrossSpacing + rx) +
                               down * ((cy + 0.5f) * alongSpacing + ry);
                float strandAngle = SignedHash(cx, cy, s.seed + 23) *
                                    s.directionVariation * (s.profile == 1 ? 0.65f : 1f) *
                                    Mathf.Deg2Rad;
                Vector2 strandDown = Rotate(down, strandAngle);
                Vector2 strandAcross = new Vector2(-strandDown.y, strandDown.x);
                float length = Mathf.Max(0.5f, baseLength *
                    (1f + SignedHash(cx, cy, s.seed + 31) * s.lengthVariation));
                float width = Mathf.Max(0.2f, baseWidth *
                    (1f + SignedHash(cx, cy, s.seed + 37) * s.widthVariation));
                float bend = SignedHash(cx, cy, s.seed + 43) * s.curvature *
                             (s.profile == 1 ? 0.6f : 1f) * length;
                Vector2 p0 = root;
                Vector2 p1 = root + strandDown * (length * 0.5f) + strandAcross * bend;
                Vector2 p2 = root + strandDown * length;

                SegmentDistance(pixel, p0, p1, out float d0, out float t0);
                SegmentDistance(pixel, p1, p2, out float d1, out float t1);
                float distance;
                float along;
                if (d0 <= d1) { distance = d0; along = t0 * 0.5f; }
                else { distance = d1; along = 0.5f + t1 * 0.5f; }
                float radius = width * 0.5f * Mathf.Lerp(1f, 0.12f,
                    Mathf.Pow(along, s.taper));
                float coverage = 1f - SmoothStep(Mathf.Max(0f, radius - 0.75f),
                    radius + 0.75f, distance);
                coverage *= SmoothStep(1f, 0.72f, along);
                float shadowDistance = Vector2.Distance(pixel - down * s.shadowOffset, root +
                    strandDown * Mathf.Clamp(Vector2.Dot(pixel - root, strandDown), 0f, length));
                float shadow = 1f - SmoothStep(radius + s.shadowSpread * 0.35f,
                    radius + s.shadowSpread, shadowDistance);
                shadow *= SmoothStep(1f, 0.72f, along) * s.shadowAmount;
                float follicle = (1f - SmoothStep(s.rednessRadius * 0.25f,
                    s.rednessRadius, Vector2.Distance(pixel, root))) * s.rednessAmount;
                best.shadow = Mathf.Max(best.shadow, shadow);
                best.redness = Mathf.Max(best.redness, follicle);
                if (coverage <= best.coverage) continue;

                float colorVariation = SignedHash(cx, cy, s.seed + 53) *
                                       s.hairColorVariation;
                best.coverage = coverage * s.hairOpacity;
                best.color = AddRgb(s.hairColor, colorVariation);
                best.height = Mathf.Clamp01(0.5f + s.hairHeight *
                    coverage * Mathf.Sin(Mathf.Clamp01(along) * Mathf.PI));
            }
            return best;
        }

        private static SkinSample SampleSkin(Settings s, Vector2 pixel)
        {
            var sample = new SkinSample();
            if (s.rashAmount > 0f)
            {
                float broad = ValueNoise(pixel / Math.Max(1f, s.rashScale), s.seed + 101);
                float fine = ValueNoise(pixel / Math.Max(1f, s.rashScale * 0.28f), s.seed + 107);
                sample.rash = SmoothStep(1f - s.rashAmount, 1f,
                    broad * 0.72f + fine * 0.28f) * s.rashOpacity;
            }
            if (s.pimpleAmount > 0f)
                sample.pimple = SpotField(pixel, s.pimpleSpacing, s.pimpleSize,
                    s.pimpleAmount, s.seed + 211, out sample.pimpleCore);
            if (s.spotAmount > 0f)
                sample.spot = SpotField(pixel, s.spotSpacing, s.spotSize,
                    s.spotAmount, s.seed + 307, out sample.spotCore);
            return sample;
        }

        private static GeneratedPixel Combine(Settings s, HairSample hair, SkinSample skin,
            float placement)
        {
            float skinAlpha = Mathf.Clamp01(Mathf.Max(Mathf.Max(skin.rash, hair.redness),
                Mathf.Max(skin.pimple, skin.spot)));
            Color skinColor = Color.clear;
            float weights = 0f;
            Accumulate(ref skinColor, ref weights, s.rashColor, skin.rash);
            Accumulate(ref skinColor, ref weights, s.rednessColor, hair.redness);
            Accumulate(ref skinColor, ref weights, s.pimpleColor, skin.pimple);
            Accumulate(ref skinColor, ref weights, s.spotColor, skin.spot);
            if (weights > 0f) skinColor /= weights;

            float hairAlpha = Mathf.Clamp01(hair.coverage);
            Color albedo = AlphaOver(WithAlpha(skinColor, skinAlpha),
                WithAlpha(s.shadowColor, hair.shadow));
            albedo = AlphaOver(albedo,
                WithAlpha(hair.color, hairAlpha));
            albedo.a *= placement;

            float relief = Mathf.Max(skin.pimpleCore * s.pimpleHeight,
                skin.spotCore * s.spotHeight);
            float normalAlpha = Mathf.Clamp01(Mathf.Max(hairAlpha,
                Mathf.Max(skin.pimple, skin.spot))) * placement;
            float normal = hairAlpha > relief ? hair.height : Mathf.Clamp01(0.5f + relief);
            float roughness = Mathf.Lerp(s.skinRoughness,
                s.hairRoughness, hairAlpha);
            roughness = Mathf.Lerp(roughness, s.rashRoughness, skin.rash);
            float materialAlpha = Mathf.Clamp01(Mathf.Max(Mathf.Max(hairAlpha, hair.shadow),
                skinAlpha)) * placement;
            float skinMaskAlpha = skinAlpha * s.skinMaskStrength * placement;
            float detail = Mathf.Clamp01(Mathf.Max(hairAlpha,
                Mathf.Max(skin.pimple, skin.spot)));

            return new GeneratedPixel
            {
                albedo = albedo,
                roughness = Scalar(roughness, materialAlpha),
                normalControl = Scalar(normal, normalAlpha),
                skinColorMask = WithAlpha(skinColor, skinMaskAlpha),
                detailMask = Scalar(detail, detail * placement)
            };
        }

        private static float PlacementCoverage(Settings s, float u, float v, int width, int height)
        {
            Vector2 delta = new Vector2((u - s.placementX) * width,
                (v - s.placementY) * height);
            delta = Rotate(delta, -s.placementRotation * Mathf.Deg2Rad);
            float halfWidth = s.placementWidth * width * 0.5f;
            float halfHeight = s.placementHeight * height * 0.5f;
            float distance;
            if (s.placementShape == 1)
            {
                float normalized = Mathf.Sqrt(delta.x * delta.x / Math.Max(1f, halfWidth * halfWidth) +
                                              delta.y * delta.y / Math.Max(1f, halfHeight * halfHeight));
                distance = (1f - normalized) * Math.Min(halfWidth, halfHeight);
            }
            else
                distance = Math.Min(halfWidth - Math.Abs(delta.x), halfHeight - Math.Abs(delta.y));
            if (distance <= 0f) return 0f;
            return s.edgeFeather <= 0f ? 1f : SmoothStep(0f, s.edgeFeather, distance);
        }

        private static float SpotField(Vector2 pixel, float spacing, float radius, float amount,
            int seed, out float core)
        {
            spacing = Mathf.Max(1f, spacing);
            int bx = Mathf.FloorToInt(pixel.x / spacing);
            int by = Mathf.FloorToInt(pixel.y / spacing);
            float best = 0f;
            core = 0f;
            for (int y = by - 1; y <= by + 1; y++)
            for (int x = bx - 1; x <= bx + 1; x++)
            {
                if (Hash(x, y, seed) > amount) continue;
                Vector2 center = new Vector2((x + 0.15f + Hash(x, y, seed + 7) * 0.7f) * spacing,
                    (y + 0.15f + Hash(x, y, seed + 13) * 0.7f) * spacing);
                float variedRadius = radius * Mathf.Lerp(0.65f, 1.35f, Hash(x, y, seed + 19));
                float normalized = Vector2.Distance(pixel, center) / Math.Max(0.25f, variedRadius);
                float coverage = 1f - SmoothStep(0.55f, 1f, normalized);
                if (coverage <= best) continue;
                best = coverage;
                core = Mathf.Clamp01(1f - normalized);
            }
            return best;
        }

        private static void SegmentDistance(Vector2 point, Vector2 a, Vector2 b,
            out float distance, out float t)
        {
            Vector2 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            t = lengthSquared > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / lengthSquared) : 0f;
            distance = Vector2.Distance(point, a + segment * t);
        }

        private static void Write(TexturePaintCommandContextV2 context, string surfaceId,
            OutputTarget target, int y0, int rows, OutputBuffers output)
        {
            Color32[] pixels = output.For(target.channel);
            if (pixels == null) return;
            TexturePaintPluginColorSpace colorSpace = TexturePaintChannelUtility.IsColor(target.channel)
                ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data;
            context.WriteTileCompactOwned(surfaceId, target.channel,
                new RectInt(0, y0, target.width, rows), pixels, colorSpace,
                TexturePaintPluginBlend.Normal, 1f);
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters() => new()
        {
            Header("placementHeader", "Placement & Coverage", "Bounds the transparent overlay and controls its deterministic layout."),
            Enum("placementShape", "Placement Shape", new[] { "Rectangle", "Ellipse" }, 1, "Shape of the generated region."),
            Float("placementX", "Center X", 0f, 1f, 0.5f, "Horizontal center in normalized texture coordinates."),
            Float("placementY", "Center Y", 0f, 1f, 0.5f, "Vertical center in normalized texture coordinates."),
            Float("placementWidth", "Placement Width", 0.001f, 1f, 0.55f, "Width as a fraction of the texture."),
            Float("placementHeight", "Placement Height", 0.001f, 1f, 0.35f, "Height as a fraction of the texture."),
            Float("placementRotation", "Placement Rotation", -180f, 180f, 0f, "Rotates the placement shape in degrees."),
            Float("edgeFeather", "Edge Feather (px)", 0f, 256f, 24f, "Softens overlay alpha at placement boundaries."),
            Integer("seed", "Seed", 0, 100000, 941, "Deterministically changes strand and skin-detail positions."),
            Float("overallAmount", "Overall Amount", 0f, 1f, 1f, "Final alpha multiplier for all generated channels."),
            Texture(ControlMask, "Control Mask", "Optional grayscale texture multiplied with placement coverage."),

            Header("hairHeader", "Stubble Strands", "Tapered hairs grow toward texture-space down by default."),
            Enum("profile", "Stubble Profile", new[] { "Facial Hair", "Shaved Head", "Custom / Neutral" }, 0, "Facial Hair keeps the authored measurements; Shaved Head produces shorter, denser, straighter follicles from the same controls."),
            ColorParameter("hairColor", "Hair Color", new Color(0.055f, 0.035f, 0.025f, 1f), "Base stubble color."),
            Float("hairColorVariation", "Color Variation", 0f, 0.35f, 0.055f, "Per-strand light/dark variation."),
            Float("hairLength", "Length (px)", 0.5f, 128f, 9f, "Average visible strand length in destination pixels."),
            Float("hairWidth", "Width (px)", 0.2f, 16f, 1.35f, "Average strand width in destination pixels."),
            Float("density", "Density", 0f, 1f, 0.72f, "Strand population and spacing."),
            Float("hairOpacity", "Hair Opacity", 0f, 1f, 0.88f, "Maximum strand alpha."),
            Float("taper", "Tip Taper", 0.2f, 5f, 1.5f, "Controls how quickly strands narrow toward their tips."),
            Float("directionDegrees", "Direction from Down", -180f, 180f, 0f, "Zero grows down; positive values rotate clockwise in texture space."),
            Float("directionVariation", "Direction Variation", 0f, 60f, 7f, "Maximum random angular deviation per strand."),
            Float("curvature", "Curvature", 0f, 0.5f, 0.055f, "Maximum sideways bend as a fraction of strand length."),
            Float("randomPositionX", "Random Position X (px)", 0f, 64f, 2f, "Maximum per-strand horizontal root offset."),
            Float("randomPositionY", "Random Position Y (px)", 0f, 64f, 2f, "Maximum per-strand vertical root offset."),
            Float("lengthVariation", "Length Variation", 0f, 0.95f, 0.28f, "Random fractional length variation."),
            Float("widthVariation", "Width Variation", 0f, 0.95f, 0.18f, "Random fractional width variation."),
            Float("hairRoughness", "Hair Roughness", 0f, 1f, 0.46f, "Roughness written under strands; lower values read as shinier."),
            Float("hairHeight", "Hair Height", 0f, 0.49f, 0.12f, "Raised Normal Control response at strand centers."),

            Header("shadingHeader", "Shaving Redness & Shadows", "Adds transparent follicle irritation and soft contact shadow beneath the stubble."),
            Float("shadowAmount", "Stubble Shadow", 0f, 1f, 0.3f, "Opacity of the soft shadow under and around each strand."),
            ColorParameter("shadowColor", "Shadow Color", new Color(0.055f, 0.075f, 0.095f, 1f), "Cool beard-shadow or shaved-scalp shadow tint."),
            Float("shadowSpread", "Shadow Spread (px)", 0f, 16f, 1.8f, "Soft shadow width beyond each strand."),
            Float("shadowOffset", "Shadow Offset Down (px)", -8f, 8f, 0.65f, "Moves the contact shadow in the strand-growth direction."),
            Float("rednessAmount", "Shaving Redness", 0f, 1f, 0.08f, "Localized irritation around hair roots."),
            Float("rednessRadius", "Redness Radius (px)", 0.25f, 32f, 2.4f, "Radius of follicle redness."),
            ColorParameter("rednessColor", "Redness Color", new Color(0.68f, 0.105f, 0.085f, 1f), "Freshly shaved skin tint."),

            Header("skinHeader", "Skin Irritation & Blemishes", "Optional transparent skin shading around and beneath the stubble."),
            Float("rashAmount", "Rash Amount", 0f, 1f, 0f, "Coverage of softly clustered irritation."),
            Float("rashScale", "Rash Cluster Size (px)", 2f, 512f, 72f, "Average size of rash clusters."),
            Float("rashOpacity", "Rash Opacity", 0f, 1f, 0.34f, "Maximum rash alpha."),
            ColorParameter("rashColor", "Rash Color", new Color(0.72f, 0.12f, 0.105f, 1f), "Irritated skin tint."),
            Float("rashRoughness", "Rash Roughness", 0f, 1f, 0.68f, "Roughness of irritated skin."),
            Float("pimpleAmount", "Pimple Amount", 0f, 1f, 0f, "Probability of raised pimple cells."),
            Float("pimpleSpacing", "Pimple Spacing (px)", 2f, 512f, 46f, "Average distance between possible pimples."),
            Float("pimpleSize", "Pimple Size (px)", 0.5f, 64f, 4f, "Average pimple radius."),
            Float("pimpleHeight", "Pimple Height", 0f, 0.49f, 0.11f, "Raised Normal Control response."),
            ColorParameter("pimpleColor", "Pimple Color", new Color(0.68f, 0.11f, 0.09f, 1f), "Inflamed pimple tint."),
            Float("spotAmount", "Spot Amount", 0f, 1f, 0f, "Probability of pigment spots."),
            Float("spotSpacing", "Spot Spacing (px)", 2f, 512f, 28f, "Average distance between possible spots."),
            Float("spotSize", "Spot Size (px)", 0.5f, 64f, 2.5f, "Average pigment spot radius."),
            Float("spotHeight", "Spot Height", 0f, 0.49f, 0.015f, "Subtle raised Normal Control response."),
            ColorParameter("spotColor", "Spot Color", new Color(0.24f, 0.075f, 0.035f, 1f), "Pigment spot tint."),
            Float("skinRoughness", "Base Blemish Roughness", 0f, 1f, 0.58f, "Roughness used by pimples and pigment spots."),
            Float("skinMaskStrength", "Skin Color Mask Strength", 0f, 1f, 0.65f, "Alpha multiplier for irritation and blemishes in Skin Color Mask.")
        };

        private readonly struct Settings
        {
            public readonly int placementShape, profile, seed;
            public readonly float placementX, placementY, placementWidth, placementHeight,
                placementRotation, edgeFeather, overallAmount, hairColorVariation, hairLength,
                hairWidth, density, hairOpacity, taper, directionDegrees, directionVariation,
                curvature, randomPositionX, randomPositionY, lengthVariation, widthVariation,
                hairRoughness, hairHeight, rashAmount, rashScale, rashOpacity, rashRoughness,
                pimpleAmount, pimpleSpacing, pimpleSize, pimpleHeight, spotAmount, spotSpacing,
                spotSize, spotHeight, skinRoughness, skinMaskStrength, shadowAmount,
                shadowSpread, shadowOffset, rednessAmount, rednessRadius;
            public readonly Color hairColor, shadowColor, rednessColor, rashColor, pimpleColor,
                spotColor;

            public Settings(TexturePaintPluginParameterSet values)
            {
                values ??= new TexturePaintPluginParameterSet();
                placementShape = values.Integer("placementShape", 1);
                profile = values.Integer("profile", 0);
                placementX = Clamp01(values.Float("placementX", 0.5f));
                placementY = Clamp01(values.Float("placementY", 0.5f));
                placementWidth = Mathf.Clamp(values.Float("placementWidth", 0.55f), 0.001f, 1f);
                placementHeight = Mathf.Clamp(values.Float("placementHeight", 0.35f), 0.001f, 1f);
                placementRotation = values.Float("placementRotation", 0f);
                edgeFeather = Pos(values.Float("edgeFeather", 24f));
                seed = values.Integer("seed", 941);
                overallAmount = Clamp01(values.Float("overallAmount", 1f));
                hairColor = values.Color("hairColor", new Color(0.055f, 0.035f, 0.025f, 1f));
                hairColorVariation = Clamp01(values.Float("hairColorVariation", 0.055f));
                hairLength = Mathf.Clamp(values.Float("hairLength", 9f), 0.5f, 128f);
                hairWidth = Mathf.Clamp(values.Float("hairWidth", 1.35f), 0.2f, 16f);
                density = Clamp01(values.Float("density", 0.72f));
                hairOpacity = Clamp01(values.Float("hairOpacity", 0.88f));
                taper = Mathf.Clamp(values.Float("taper", 1.5f), 0.2f, 5f);
                directionDegrees = values.Float("directionDegrees", 0f);
                directionVariation = Mathf.Clamp(values.Float("directionVariation", 7f), 0f, 60f);
                curvature = Mathf.Clamp(values.Float("curvature", 0.055f), 0f, 0.5f);
                randomPositionX = Pos(values.Float("randomPositionX", 2f));
                randomPositionY = Pos(values.Float("randomPositionY", 2f));
                lengthVariation = Mathf.Clamp(values.Float("lengthVariation", 0.28f), 0f, 0.95f);
                widthVariation = Mathf.Clamp(values.Float("widthVariation", 0.18f), 0f, 0.95f);
                hairRoughness = Clamp01(values.Float("hairRoughness", 0.46f));
                hairHeight = Mathf.Clamp(values.Float("hairHeight", 0.12f), 0f, 0.49f);
                shadowAmount = Clamp01(values.Float("shadowAmount", 0.3f));
                shadowColor = values.Color("shadowColor", new Color(0.055f, 0.075f, 0.095f, 1f));
                shadowSpread = Pos(values.Float("shadowSpread", 1.8f));
                shadowOffset = values.Float("shadowOffset", 0.65f);
                rednessAmount = Clamp01(values.Float("rednessAmount", 0.08f));
                rednessRadius = Mathf.Max(0.25f, values.Float("rednessRadius", 2.4f));
                rednessColor = values.Color("rednessColor", new Color(0.68f, 0.105f, 0.085f, 1f));
                rashAmount = Clamp01(values.Float("rashAmount", 0f));
                rashScale = Mathf.Max(2f, values.Float("rashScale", 72f));
                rashOpacity = Clamp01(values.Float("rashOpacity", 0.34f));
                rashColor = values.Color("rashColor", new Color(0.72f, 0.12f, 0.105f, 1f));
                rashRoughness = Clamp01(values.Float("rashRoughness", 0.68f));
                pimpleAmount = Clamp01(values.Float("pimpleAmount", 0f));
                pimpleSpacing = Mathf.Max(2f, values.Float("pimpleSpacing", 46f));
                pimpleSize = Mathf.Max(0.5f, values.Float("pimpleSize", 4f));
                pimpleHeight = Mathf.Clamp(values.Float("pimpleHeight", 0.11f), 0f, 0.49f);
                pimpleColor = values.Color("pimpleColor", new Color(0.68f, 0.11f, 0.09f, 1f));
                spotAmount = Clamp01(values.Float("spotAmount", 0f));
                spotSpacing = Mathf.Max(2f, values.Float("spotSpacing", 28f));
                spotSize = Mathf.Max(0.5f, values.Float("spotSize", 2.5f));
                spotHeight = Mathf.Clamp(values.Float("spotHeight", 0.015f), 0f, 0.49f);
                spotColor = values.Color("spotColor", new Color(0.24f, 0.075f, 0.035f, 1f));
                skinRoughness = Clamp01(values.Float("skinRoughness", 0.58f));
                skinMaskStrength = Clamp01(values.Float("skinMaskStrength", 0.65f));
            }
        }

        private readonly struct OutputTarget
        {
            public readonly TexturePaintChannel channel;
            public readonly int width, height;
            private OutputTarget(TexturePaintChannel channel, TexturePaintReadOnlyChannelInfo info)
            { this.channel = channel; width = info.width; height = info.height; }

            public static List<OutputTarget> Find(TexturePaintReadContextV2 source, string surfaceId)
            {
                var result = new List<OutputTarget>();
                foreach (TexturePaintChannel channel in System.Enum.GetValues(
                             typeof(TexturePaintChannel)))
                {
                    if ((OutputChannels & TexturePaintExportTemplate.ToMask(channel)) == 0) continue;
                    TexturePaintReadOnlyChannelInfo info = source.GetChannelInfo(surfaceId, channel);
                    if (info != null) result.Add(new OutputTarget(channel, info));
                }
                return result;
            }
        }

        private sealed class OutputBuffers
        {
            private readonly Color32[] albedo, roughness, normalControl, skinColorMask, detailMask;
            public bool any;
            public OutputBuffers(int count, TexturePaintChannelMask channels)
            {
                albedo = Has(channels, TexturePaintChannel.Albedo) ? new Color32[count] : null;
                roughness = Has(channels, TexturePaintChannel.Roughness) ? new Color32[count] : null;
                normalControl = Has(channels, TexturePaintChannel.NormalControl) ? new Color32[count] : null;
                skinColorMask = Has(channels, TexturePaintChannel.SkinColorMask) ? new Color32[count] : null;
                detailMask = Has(channels, TexturePaintChannel.DetailMask) ? new Color32[count] : null;
            }
            public void Set(int index, GeneratedPixel pixel)
            {
                if (albedo != null) albedo[index] = pixel.albedo;
                if (roughness != null) roughness[index] = pixel.roughness;
                if (normalControl != null) normalControl[index] = pixel.normalControl;
                if (skinColorMask != null) skinColorMask[index] = pixel.skinColorMask;
                if (detailMask != null) detailMask[index] = pixel.detailMask;
                any = true;
            }
            public Color32[] For(TexturePaintChannel channel) => channel switch
            {
                TexturePaintChannel.Albedo => albedo,
                TexturePaintChannel.Roughness => roughness,
                TexturePaintChannel.NormalControl => normalControl,
                TexturePaintChannel.SkinColorMask => skinColorMask,
                TexturePaintChannel.DetailMask => detailMask,
                _ => null
            };
            private static bool Has(TexturePaintChannelMask channels, TexturePaintChannel channel) =>
                (channels & TexturePaintExportTemplate.ToMask(channel)) != 0;
        }

        private struct GeneratedPixel
        {
            public Color albedo, roughness, normalControl, skinColorMask, detailMask;
            public bool Any => albedo.a > 0.0001f || roughness.a > 0.0001f ||
                               normalControl.a > 0.0001f || skinColorMask.a > 0.0001f ||
                               detailMask.a > 0.0001f;
        }
        private struct HairSample
        {
            public float coverage, height, shadow, redness;
            public Color color;
        }
        private struct SkinSample
        {
            public float rash, pimple, pimpleCore, spot, spotCore;
        }

        private static TexturePaintPluginParameterDefinition Header(string id, string name,
            string description) => new() { id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Header };
        private static TexturePaintPluginParameterDefinition Float(string id, string name,
            float min, float max, float value, string description) => new()
            { id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Float, minimum = min, maximum = max,
                defaultNumber = value };
        private static TexturePaintPluginParameterDefinition Integer(string id, string name,
            int min, int max, int value, string description) => new()
            { id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Integer, minimum = min, maximum = max,
                defaultNumber = value };
        private static TexturePaintPluginParameterDefinition ColorParameter(string id, string name,
            Color value, string description) => new()
            { id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Color, defaultColor = value };
        private static TexturePaintPluginParameterDefinition Texture(string id, string name,
            string description) => new() { id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Texture };
        private static TexturePaintPluginParameterDefinition Enum(string id, string name,
            string[] options, int value, string description) => new()
            { id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Enum, minimum = 0,
                maximum = options.Length - 1, defaultNumber = value, enumOptions = options };

        private static float ValueNoise(Vector2 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            float tx = Fade(p.x - x), ty = Fade(p.y - y);
            float a = Mathf.Lerp(Hash(x, y, seed), Hash(x + 1, y, seed), tx);
            float b = Mathf.Lerp(Hash(x, y + 1, seed), Hash(x + 1, y + 1, seed), tx);
            return Mathf.Lerp(a, b, ty);
        }
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 69069);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00ffffffu) / 16777215f;
            }
        }
        private static float SignedHash(int x, int y, int seed) => Hash(x, y, seed) * 2f - 1f;
        private static float Fade(float value) => value * value * (3f - 2f * value);
        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            return new Vector2(value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }
        private static float SmoothStep(float min, float max, float value)
        {
            if (Mathf.Abs(max - min) < 0.00001f) return value >= max ? 1f : 0f;
            float t = Mathf.Clamp01((value - min) / (max - min));
            return t * t * (3f - 2f * t);
        }
        private static float Luminance(Color color) =>
            color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        private static float Clamp01(float value) => Mathf.Clamp01(value);
        private static float Pos(float value) => Mathf.Max(0f, value);
        private static Color Scalar(float value, float alpha) =>
            new(value, value, value, Mathf.Clamp01(alpha));
        private static Color WithAlpha(Color value, float alpha) =>
            new(value.r, value.g, value.b, Mathf.Clamp01(alpha));
        private static Color AddRgb(Color value, float amount) =>
            new(Mathf.Clamp01(value.r + amount), Mathf.Clamp01(value.g + amount),
                Mathf.Clamp01(value.b + amount), value.a);
        private static Color AlphaOver(Color bottom, Color top)
        {
            float alpha = top.a + bottom.a * (1f - top.a);
            if (alpha <= 0.0001f) return Color.clear;
            return new Color((top.r * top.a + bottom.r * bottom.a * (1f - top.a)) / alpha,
                (top.g * top.a + bottom.g * bottom.a * (1f - top.a)) / alpha,
                (top.b * top.a + bottom.b * bottom.a * (1f - top.a)) / alpha, alpha);
        }
        private static void Accumulate(ref Color sum, ref float weights, Color color, float weight)
        {
            if (weight <= 0f) return;
            sum += color * weight;
            weights += weight;
        }
    }
}
