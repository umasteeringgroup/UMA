using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>
    /// Procedural, repeatable garment fabric with independently optional color, roughness and
    /// Normal Control outputs. The stripe list is part of the typed plugin parameter payload.
    /// </summary>
    public sealed class ClothTextureGeneratorPlugin : ScriptableObject,
        ITexturePaintGeneratorV2, ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor =
            ClothTextureGeneratorEngine.CreateDescriptor();

        public TexturePaintPluginDescriptor Descriptor => descriptor;

        // This generator needs target dimensions but never copies the composed material channels.
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            TexturePaintChannelMask.None;

        public Task ExecuteAsync(TexturePaintCommandContextV2 context) =>
            ClothTextureGeneratorEngine.ExecuteAsync(context);
    }

    internal static class ClothTextureGeneratorEngine
    {
        private const string PatternSprite = "patternSprite";
        private const int RowsPerTile = 128;

        private enum Weave
        {
            Cotton,
            Knit,
            Twill,
            Corduroy,
            Herringbone,
            Denim,
            Canvas,
            Linen,
            Satin,
            Basket,
            Houndstooth,
            Leno,
            Dobby,
            Pile,
            Crepe,
            Jacquard
        }

        public static TexturePaintPluginDescriptor CreateDescriptor() =>
            new TexturePaintPluginDescriptor
            {
                id = "com.uma.texturepaint.cloth-texture",
                displayName = "Cloth Texture",
                description = "Generates production fabric weaves, ordered plaid/stripe layouts, " +
                              "optional sprite motifs and thread-aware color wear.",
                pluginVersion = "1.0.0",
                capabilities = TexturePaintPluginCapability.Generator |
                               TexturePaintPluginCapability.LongRunning,
                declaredChannels = TexturePaintChannelMask.Albedo |
                                   TexturePaintChannelMask.Roughness |
                                   TexturePaintChannelMask.NormalControl,
                // Dynamic channel usage explicitly narrows this to no snapshots.
                readChannels = TexturePaintChannelMask.All,
                channelSnapshotMaximumResolution = 4096,
                parameters = Parameters()
            };

        public static Task ExecuteAsync(TexturePaintCommandContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var settings = new Settings(context.parameters);
            if (!settings.outputAlbedo && !settings.outputRoughness &&
                !settings.outputNormalControl)
                throw new InvalidOperationException(
                    "Cloth Texture requires at least one enabled output channel.");
            TexturePaintReadOnlyParameterTexture pattern =
                context.GetTextureParameter(PatternSprite);
            return Task.Run(() => Execute(context, settings, pattern),
                context.cancellationToken);
        }

        private static void Execute(TexturePaintCommandContextV2 context, Settings settings,
            TexturePaintReadOnlyParameterTexture pattern)
        {
            int surfaceCount = Math.Max(1, context.source.surfaceIds.Count);
            for (int surfaceIndex = 0; surfaceIndex < context.source.surfaceIds.Count; surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];
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
                    int width = group[0].width, height = group[0].height;
                    for (int y0 = 0; y0 < height; y0 += RowsPerTile)
                    {
                        context.cancellationToken.ThrowIfCancellationRequested();
                        int rows = Math.Min(RowsPerTile, height - y0);
                        OutputBuffers output = Generate(settings, pattern, width, height, y0, rows);
                        for (int i = 0; i < group.Count; i++)
                            Write(context, surfaceId, group[i], y0, rows, output);
                        context.progress?.Report((surfaceIndex + (y0 + rows) / (float)height) /
                            surfaceCount);
                    }
                }
            }
        }

        private static OutputBuffers Generate(Settings settings,
            TexturePaintReadOnlyParameterTexture pattern, int width, int height,
            int y0, int rows)
        {
            var output = new OutputBuffers(width * rows, settings);
            float rotation = settings.rotation * Mathf.Deg2Rad;
            float stripeRotation = settings.stripeRotation * Mathf.Deg2Rad;
            Parallel.For(0, rows, localY =>
            {
                float v = (y0 + localY + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    Vector2 fabricUv = RotateCentered(new Vector2(u, v), rotation);
                    WeaveSample weave = SampleWeave(settings, fabricUv);
                    Color albedo = Color.Lerp(settings.baseColor, settings.threadColor,
                        Mathf.Clamp01(weave.threadMix * settings.threadColorAmount));
                    float microTint = (weave.fiber - 0.5f) * settings.fiberColorVariation;
                    albedo = AddRgb(albedo, microTint);

                    Vector2 stripeUv = RotateCentered(new Vector2(u, v), stripeRotation);
                    float stripeCoverage = ApplyStripes(settings, stripeUv, ref albedo);
                    float patternCoverage = ApplyPattern(settings, pattern, new Vector2(u, v),
                        stripeCoverage, ref albedo);

                    float wear = SampleWear(settings, fabricUv, weave);
                    albedo = Color.Lerp(albedo, settings.wearColor,
                        Mathf.Clamp01(wear * settings.wearAmount));
                    // Expose individual worn fibers so the fade follows rather than obscures weave.
                    albedo = AddRgb(albedo, wear * (weave.fiber - 0.5f) *
                        settings.wearThreadContrast);
                    albedo.a = 1f;

                    float roughness = settings.roughness +
                        (weave.roughness - 0.5f) * settings.roughnessVariation +
                        wear * settings.wearAmount * settings.wearRoughnessChange +
                        patternCoverage * settings.patternRoughness;
                    roughness = Mathf.Clamp01(roughness);

                    float heightValue = weave.height * settings.heightStrength;
                    heightValue *= 1f - wear * settings.wearAmount * settings.wearFlattening;
                    heightValue += patternCoverage * settings.patternEmboss;
                    float normalControl = Mathf.Clamp01(0.5f + heightValue);

                    int index = localY * width + x;
                    output.Set(index, albedo, roughness, normalControl);
                }
            });
            return output;
        }

        private static WeaveSample SampleWeave(Settings s, Vector2 uv)
        {
            float scaleX = s.weaveScale * s.weaveAspect;
            float scaleY = s.weaveScale;
            float irregularX = (Fractal(uv.x, uv.y, s.weaveScale * 0.08f, s.seed) - 0.5f) *
                               s.irregularity;
            float irregularY = (Fractal(uv.y, uv.x, s.weaveScale * 0.07f, s.seed + 71) - 0.5f) *
                               s.irregularity;
            float x = uv.x * scaleX + irregularX;
            float y = uv.y * scaleY + irregularY;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = Repeat(x), fy = Repeat(y);
            float warp = ThreadRidge(fx, s.threadRoundness);
            float weft = ThreadRidge(fy, s.threadRoundness);
            float height, mix, roughness;
            switch (s.weave)
            {
                case Weave.Knit:
                {
                    float cellX = Repeat(x * 0.5f) * 2f - 1f;
                    float cellY = Repeat(y * 0.5f) * 2f - 1f;
                    float vLine = 1f - SmoothStep(0.08f, 0.32f,
                        Math.Abs(Math.Abs(cellX) - (0.18f + Math.Abs(cellY) * 0.48f)));
                    float loop = 1f - SmoothStep(0.18f, 0.5f,
                        Math.Abs(cellX * cellX + (cellY + 0.45f) *
                            (cellY + 0.45f) - 0.35f));
                    height = (vLine * 0.85f + loop * 0.45f - 0.45f) * s.weaveContrast;
                    mix = Mathf.Clamp01(vLine * 0.7f + loop * 0.3f);
                    roughness = 0.62f + loop * 0.2f;
                    break;
                }
                case Weave.Twill:
                {
                    bool warpOver = PositiveMod(ix - iy, 4) < 2;
                    height = (warpOver ? warp - weft * 0.45f : weft - warp * 0.45f) *
                             s.weaveContrast;
                    mix = warpOver ? 0.2f : 0.8f;
                    roughness = 0.46f + Math.Abs(warp - weft) * 0.25f;
                    break;
                }
                case Weave.Corduroy:
                {
                    float rib = Mathf.Pow(Mathf.Max(0f,
                        Mathf.Cos(Repeat(x * 0.18f) * Mathf.PI * 2f) * 0.5f + 0.5f), 2.2f);
                    height = (rib * 1.3f + weft * 0.18f - 0.6f) * s.weaveContrast;
                    mix = 0.25f + weft * 0.3f;
                    roughness = 0.55f + (1f - rib) * 0.25f;
                    break;
                }
                case Weave.Herringbone:
                {
                    int block = Mathf.FloorToInt(y / 8f);
                    int direction = (block & 1) == 0 ? 1 : -1;
                    bool warpOver = PositiveMod(ix + direction * iy, 6) < 3;
                    float seam = SmoothStep(0f, 0.22f, Math.Abs(Repeat(y / 8f) - 0.5f));
                    height = (warpOver ? warp - weft * 0.4f : weft - warp * 0.4f) *
                             s.weaveContrast * Mathf.Lerp(0.75f, 1f, seam);
                    mix = warpOver ? 0.28f : 0.72f;
                    roughness = 0.5f + (1f - seam) * 0.15f;
                    break;
                }
                case Weave.Denim:
                {
                    bool warpOver = PositiveMod(ix - iy, 4) < 3;
                    height = (warpOver ? warp * 1.1f - weft * 0.5f :
                        weft * 0.8f - warp * 0.35f) * s.weaveContrast;
                    mix = warpOver ? 0.12f : 0.92f;
                    roughness = 0.48f + (warpOver ? 0.03f : 0.2f);
                    break;
                }
                case Weave.Canvas:
                {
                    bool warpOver = ((ix + iy) & 1) == 0;
                    float coarseWarp = Mathf.Pow(warp, 0.65f);
                    float coarseWeft = Mathf.Pow(weft, 0.65f);
                    height = (warpOver ? coarseWarp - coarseWeft * 0.5f :
                        coarseWeft - coarseWarp * 0.5f) * s.weaveContrast;
                    mix = warpOver ? 0.35f : 0.65f;
                    roughness = 0.58f + Math.Abs(coarseWarp - coarseWeft) * 0.2f;
                    break;
                }
                case Weave.Linen:
                {
                    bool warpOver = ((ix + iy) & 1) == 0;
                    float slub = Fractal(uv.x, uv.y, s.weaveScale * 0.025f, s.seed + 223);
                    float irregularWarp = Mathf.Clamp01(warp * Mathf.Lerp(0.55f, 1.45f, slub));
                    float irregularWeft = Mathf.Clamp01(weft * Mathf.Lerp(1.4f, 0.6f, slub));
                    height = (warpOver ? irregularWarp - irregularWeft * 0.42f :
                        irregularWeft - irregularWarp * 0.42f) * s.weaveContrast;
                    mix = Mathf.Lerp(0.25f, 0.78f, slub);
                    roughness = 0.62f + Math.Abs(slub - 0.5f) * 0.3f;
                    break;
                }
                case Weave.Satin:
                {
                    // Long four-over-one floats create satin's continuous highlights.
                    bool warpOver = PositiveMod(ix - iy * 2, 5) != 0;
                    float floatingThread = warpOver ? Mathf.Pow(warp, .62f) :
                        Mathf.Pow(weft, 1.35f);
                    float buriedThread = warpOver ? weft : warp;
                    height = (floatingThread * 1.08f - buriedThread * .24f - .18f) *
                             s.weaveContrast;
                    mix = warpOver ? .16f : .86f;
                    roughness = warpOver ? .27f : .48f;
                    break;
                }
                case Weave.Basket:
                {
                    bool warpOver = (((ix >> 1) + (iy >> 1)) & 1) == 0;
                    float bundledWarp = Mathf.Pow(warp, .72f);
                    float bundledWeft = Mathf.Pow(weft, .72f);
                    height = (warpOver ? bundledWarp - bundledWeft * .42f :
                        bundledWeft - bundledWarp * .42f) * s.weaveContrast;
                    mix = warpOver ? .27f : .73f;
                    roughness = .54f + Math.Abs(bundledWarp - bundledWeft) * .18f;
                    break;
                }
                case Weave.Houndstooth:
                {
                    int cellX = PositiveMod(ix, 8), cellY = PositiveMod(iy, 8);
                    bool tooth = cellX < 4
                        ? cellY < 4 || cellY == 4 + cellX
                        : cellY >= 4 && cellY == cellX - 4;
                    bool warpOver = PositiveMod(ix - iy, 4) < 2;
                    height = (warpOver ? warp - weft * .42f : weft - warp * .42f) *
                             s.weaveContrast;
                    mix = tooth ? .08f : .92f;
                    roughness = .48f + (tooth ? .04f : .13f);
                    break;
                }
                case Weave.Leno:
                {
                    float crossing = Mathf.Sin(y * Mathf.PI) * .24f;
                    float pairedWarp = Mathf.Max(ThreadRidge(Repeat(fx + crossing),
                            s.threadRoundness),
                        ThreadRidge(Repeat(fx - crossing + .38f), s.threadRoundness));
                    bool warpOver = (iy & 1) == 0;
                    height = (warpOver ? pairedWarp - weft * .32f :
                        weft - pairedWarp * .28f) * s.weaveContrast;
                    mix = Mathf.Lerp(.18f, .78f, weft);
                    roughness = .61f + (1f - Mathf.Max(pairedWarp, weft)) * .17f;
                    break;
                }
                case Weave.Dobby:
                {
                    int motifX = PositiveMod(ix, 6), motifY = PositiveMod(iy, 6);
                    bool raisedMotif = (motifX == motifY) || (motifX + motifY == 5) ||
                                       (motifX >= 2 && motifX <= 3 && motifY >= 2 && motifY <= 3);
                    bool warpOver = raisedMotif ^ (((ix + iy) & 1) == 0);
                    height = (warpOver ? warp - weft * .38f : weft - warp * .38f) *
                             s.weaveContrast * (raisedMotif ? 1.18f : .78f);
                    mix = raisedMotif ? .2f : .7f;
                    roughness = .52f + (raisedMotif ? -.08f : .12f);
                    break;
                }
                case Weave.Pile:
                {
                    float loopX = Repeat(x * .5f) * 2f - 1f;
                    float loopY = Repeat(y * .5f) * 2f - 1f;
                    float ring = 1f - SmoothStep(.18f, .46f,
                        Math.Abs(loopX * loopX + loopY * loopY - .43f));
                    float tuft = Mathf.Pow(Mathf.Max(warp, weft), .45f);
                    height = (ring * .72f + tuft * .68f - .55f) * s.weaveContrast;
                    mix = Mathf.Clamp01(.28f + ring * .44f);
                    roughness = .68f + ring * .18f;
                    break;
                }
                case Weave.Crepe:
                {
                    float crinkle = Fractal(x, y, .82f, s.seed + 619);
                    bool warpOver = Fractal(ix, iy, .37f, s.seed + 673) > .5f;
                    float twistedWarp = Mathf.Pow(warp, Mathf.Lerp(.45f, 2.2f, crinkle));
                    float twistedWeft = Mathf.Pow(weft, Mathf.Lerp(2.1f, .48f, crinkle));
                    height = (warpOver ? twistedWarp - twistedWeft * .5f :
                        twistedWeft - twistedWarp * .5f) * s.weaveContrast;
                    mix = Mathf.Lerp(.18f, .84f, crinkle);
                    roughness = .66f + Math.Abs(crinkle - .5f) * .42f;
                    break;
                }
                case Weave.Jacquard:
                {
                    float motifX = Mathf.Cos(x * Mathf.PI / 6f);
                    float motifY = Mathf.Cos(y * Mathf.PI / 6f);
                    float diamond = Mathf.Cos((x + y) * Mathf.PI / 6f) *
                                    Mathf.Cos((x - y) * Mathf.PI / 6f);
                    bool figured = motifX * motifY + diamond * .7f > .15f;
                    bool warpOver = figured ^ (((ix + iy) & 1) == 0);
                    height = (warpOver ? warp - weft * .34f : weft - warp * .34f) *
                             s.weaveContrast * (figured ? 1.08f : .82f);
                    mix = figured ? .17f : .79f;
                    roughness = figured ? .4f : .59f;
                    break;
                }
                default:
                {
                    bool warpOver = ((ix + iy) & 1) == 0;
                    height = (warpOver ? warp - weft * 0.48f : weft - warp * 0.48f) *
                             s.weaveContrast;
                    mix = warpOver ? 0.32f : 0.68f;
                    roughness = 0.5f + Math.Abs(warp - weft) * 0.18f;
                    break;
                }
            }
            float fiber = Fractal(x, y, 0.32f, s.seed + 401);
            height += (fiber - 0.5f) * s.fiberHeight;
            roughness += (fiber - 0.5f) * s.fiberRoughness;
            return new WeaveSample(Mathf.Clamp(height, -1f, 1f), Mathf.Clamp01(mix),
                Mathf.Clamp01(fiber), Mathf.Clamp01(roughness));
        }

        private static float ApplyStripes(Settings settings, Vector2 uv, ref Color color)
        {
            float combined = 0f;
            for (int i = 0; i < settings.stripes.Count; i++)
            {
                TexturePaintStripeDefinition stripe = settings.stripes[i];
                if (stripe == null || !stripe.enabled || stripe.opacity <= 0f) continue;
                float coordinate = stripe.direction == TexturePaintStripeDirection.Vertical
                    ? uv.x * settings.stripeRepeatX : uv.y * settings.stripeRepeatY;
                float center = Repeat(stripe.position);
                float distance = CircularDistance(Repeat(coordinate), center);
                float halfWidth = stripe.width * 0.5f;
                float coverage = 1f - SmoothStep(halfWidth,
                    halfWidth + Math.Max(0.0001f, stripe.softness), distance);
                coverage *= stripe.opacity * stripe.color.a;
                color = Color.Lerp(color, new Color(stripe.color.r, stripe.color.g,
                    stripe.color.b, 1f), coverage);
                combined = Mathf.Max(combined, coverage);
            }
            return combined;
        }

        private static float ApplyPattern(Settings settings,
            TexturePaintReadOnlyParameterTexture pattern, Vector2 uv, float stripeCoverage,
            ref Color color)
        {
            if (pattern == null || settings.patternOpacity <= 0f) return 0f;
            float direction = settings.patternDirection switch
            {
                1 => 90f,
                2 => 45f,
                3 => -45f,
                _ => 0f
            };
            Vector2 patternUv = RotateCentered(uv,
                (direction + settings.patternRotation) * Mathf.Deg2Rad);
            patternUv.x = Repeat(patternUv.x * settings.patternTiling * settings.patternAspect +
                                 settings.patternOffsetX);
            patternUv.y = Repeat(patternUv.y * settings.patternTiling + settings.patternOffsetY);
            Color sampled = pattern.GetPixelBilinear(patternUv.x, patternUv.y);
            float selection = settings.patternMode switch
            {
                1 => stripeCoverage,
                2 => 1f - stripeCoverage,
                _ => 1f
            };
            float motif = settings.usePatternColor ? sampled.a : sampled.a * Luma(sampled);
            float coverage = Mathf.Clamp01(motif * settings.patternOpacity * selection);
            Color patternColor = settings.usePatternColor
                ? new Color(sampled.r, sampled.g, sampled.b, 1f)
                : settings.patternColor;
            color = Color.Lerp(color, patternColor, coverage);
            return coverage;
        }

        private static float SampleWear(Settings settings, Vector2 uv, WeaveSample weave)
        {
            float directionScaleX = 1f, directionScaleY = 1f;
            switch (settings.wearDirection)
            {
                case 1: directionScaleX = 0.18f; directionScaleY = 1.8f; break;
                case 2: directionScaleX = 1.8f; directionScaleY = 0.18f; break;
                case 3:
                    uv = RotateCentered(uv, 45f * Mathf.Deg2Rad);
                    directionScaleX = 0.2f; directionScaleY = 1.7f; break;
            }
            float broad = Fractal(uv.x * directionScaleX, uv.y * directionScaleY,
                settings.wearScale, settings.seed + 911);
            broad = SmoothStep(settings.wearThreshold - settings.wearSoftness,
                settings.wearThreshold + settings.wearSoftness, broad);
            float threadTop = Mathf.Clamp01(Math.Abs(weave.height));
            float threadFollow = Mathf.Lerp(1f, 0.35f + threadTop * 0.65f,
                settings.wearThreadBias);
            float fibers = Mathf.Lerp(0.72f, 1.28f, weave.fiber);
            return Mathf.Clamp01(broad * threadFollow * fibers);
        }

        private static void Write(TexturePaintCommandContextV2 context, string surfaceId,
            OutputTarget target, int y0, int rows, OutputBuffers output)
        {
            Color32[] pixels = output.Get(target.channel);
            if (pixels == null) return;
            context.WriteTileCompactOwned(surfaceId, target.channel,
                new RectInt(0, y0, target.width, rows), pixels,
                target.channel == TexturePaintChannel.Albedo
                    ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data,
                TexturePaintPluginBlend.Replace);
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters()
        {
            var p = new List<TexturePaintPluginParameterDefinition>
            {
                Header("outputs", "Output Channels", "Every generated material channel is optional."),
                Boolean("outputAlbedo", "Albedo", true, "Generate fabric, stripe, motif and faded colors."),
                Boolean("outputRoughness", "Roughness", true, "Generate weave-scale roughness breakup."),
                Boolean("outputNormalControl", "Normal Control", true, "Generate grayscale raised/recessed weave height."),

                Header("fabric", "Fabric Weave", "Choose the construction and physical thread response."),
                EnumParameter("weave", "Weave", new[] { "Cotton / Plain", "Knit", "Twill", "Corduroy", "Herringbone", "Denim", "Canvas", "Linen", "Satin", "Basket", "Houndstooth", "Leno", "Dobby", "Pile", "Crepe", "Jacquard" }, 0, "Fabric construction."),
                ColorParameter("baseColor", "Base Color", new Color(0.52f, 0.5f, 0.46f, 1f), "Primary garment color."),
                ColorParameter("threadColor", "Cross-Thread Color", new Color(0.64f, 0.62f, 0.58f, 1f), "Secondary warp/weft color."),
                Float("threadColorAmount", "Cross-Thread Amount", 0f, 1f, 0.45f, "How strongly the second thread direction changes Albedo."),
                Float("weaveScale", "Threads / UV", 2f, 512f, 96f, "Thread repetition across one UV tile."),
                Float("weaveAspect", "Thread Aspect", 0.1f, 10f, 1f, "Horizontal versus vertical thread density."),
                Float("rotation", "Fabric Rotation", -180f, 180f, 0f, "Rotates the physical weave."),
                Float("weaveContrast", "Weave Definition", 0f, 2f, 0.85f, "Over-under height separation."),
                Float("threadRoundness", "Thread Roundness", 0.5f, 8f, 2.4f, "Thread crown shape."),
                Float("irregularity", "Thread Irregularity", 0f, 2f, 0.22f, "Natural spacing and tension variation."),
                Integer("seed", "Seed", 0, 999999, 1731, "Repeatable fiber and wear seed."),

                Header("surface", "Surface Response", "Fine fiber, roughness, and Normal Control response."),
                Float("heightStrength", "Normal Control Height", 0f, 0.5f, 0.1f, "Raised/recessed weave amplitude around neutral gray."),
                Float("roughness", "Base Roughness", 0f, 1f, 0.68f, "Mean cloth roughness."),
                Float("roughnessVariation", "Weave Roughness", 0f, 1f, 0.18f, "Roughness change between thread faces and gaps."),
                Float("fiberColorVariation", "Fiber Color Variation", 0f, 0.5f, 0.035f, "Fine thread-aligned Albedo breakup."),
                Float("fiberHeight", "Fiber Height", 0f, 0.5f, 0.035f, "Micro-fiber height variation."),
                Float("fiberRoughness", "Fiber Roughness", 0f, 1f, 0.22f, "Micro-fiber roughness variation."),

                Header("stripes", "Stripes / Plaid", "Add, order, and combine any number of vertical and horizontal stripes."),
                Float("stripeRepeatX", "Vertical Repeats", 0.1f, 128f, 4f, "Plaid cells across the texture."),
                Float("stripeRepeatY", "Horizontal Repeats", 0.1f, 128f, 4f, "Plaid cells down the texture."),
                Float("stripeRotation", "Stripe Rotation", -180f, 180f, 0f, "Rotates the complete stripe/plaid layout independently of the weave."),
                StripeList("stripeList", "Stripe Definitions", "Position and Width are fractions of one repeat cell. Later stripes blend over earlier stripes."),

                Header("pattern", "Pattern Sprite", "Optional repeated motif over the fabric or stripe regions."),
                SpriteParameter(PatternSprite, "Pattern Sprite", "A rectangular sprite captured without requiring Read/Write import."),
                EnumParameter("patternMode", "Apply Pattern", new[] { "Whole Fabric", "Inside Stripes", "Outside Stripes" }, 0, "Limits the motif using combined stripe coverage."),
                EnumParameter("patternDirection", "Direction", new[] { "Warp", "Weft", "Diagonal Right", "Diagonal Left" }, 0, "Principal motif direction."),
                Float("patternTiling", "Pattern Repeats", 0.1f, 128f, 4f, "Motif repetitions per UV tile."),
                Float("patternAspect", "Pattern Aspect", 0.1f, 10f, 1f, "Horizontal motif scale."),
                Float("patternRotation", "Additional Rotation", -180f, 180f, 0f, "Fine motif rotation after direction."),
                Float("patternOffsetX", "Offset X", -16f, 16f, 0f, "Horizontal motif offset."),
                Float("patternOffsetY", "Offset Y", -16f, 16f, 0f, "Vertical motif offset."),
                Boolean("usePatternColor", "Use Sprite Color", false, "Use sprite RGB and alpha instead of colorizing luminance."),
                ColorParameter("patternColor", "Pattern Color", Color.white, "Color used for a grayscale/alpha motif."),
                Float("patternOpacity", "Pattern Opacity", 0f, 1f, 0f, "Motif contribution."),
                Float("patternEmboss", "Pattern Height", -0.5f, 0.5f, 0f, "Optional Normal Control emboss or recess."),
                Float("patternRoughness", "Pattern Roughness", -1f, 1f, 0f, "Optional roughness change inside the motif."),

                Header("wear", "Thread-Aware Color Wear", "Broad faded regions broken up by exposed thread crowns and fibers."),
                Float("wearAmount", "Color Fade", 0f, 1f, 0f, "Overall worn/faded contribution."),
                ColorParameter("wearColor", "Faded Color", new Color(0.72f, 0.7f, 0.66f, 1f), "Color exposed by fading."),
                Float("wearScale", "Wear Region Scale", 0.1f, 128f, 5f, "Size of faded regions."),
                Float("wearThreshold", "Wear Level", 0f, 1f, 0.5f, "How much of the garment becomes worn."),
                Float("wearSoftness", "Wear Breakup", 0.001f, 0.5f, 0.18f, "Fractal boundary softness."),
                EnumParameter("wearDirection", "Wear Direction", new[] { "Isotropic", "Vertical", "Horizontal", "Diagonal" }, 0, "Elongates fading into directional use streaks."),
                Float("wearThreadBias", "Follow Weave", 0f, 1f, 0.72f, "Bias fading toward exposed thread crowns."),
                Float("wearThreadContrast", "Worn Fiber Contrast", 0f, 0.5f, 0.08f, "Reveals individual fibers inside faded regions."),
                Float("wearRoughnessChange", "Worn Roughness Change", -1f, 1f, 0.12f, "Polish or roughen worn areas."),
                Float("wearFlattening", "Worn Thread Flattening", 0f, 1f, 0.35f, "Reduces weave height in worn areas.")
            };
            return p;
        }

        private sealed class Settings
        {
            public readonly bool outputAlbedo, outputRoughness, outputNormalControl;
            public readonly Weave weave;
            public readonly Color baseColor, threadColor, wearColor, patternColor;
            public readonly float threadColorAmount, weaveScale, weaveAspect, rotation,
                weaveContrast, threadRoundness, irregularity, heightStrength, roughness,
                roughnessVariation, fiberColorVariation, fiberHeight, fiberRoughness,
                stripeRepeatX, stripeRepeatY, stripeRotation, patternTiling, patternAspect,
                patternRotation, patternOffsetX, patternOffsetY, patternOpacity,
                patternEmboss, patternRoughness, wearAmount, wearScale, wearThreshold,
                wearSoftness, wearThreadBias, wearThreadContrast, wearRoughnessChange,
                wearFlattening;
            public readonly int seed, patternMode, patternDirection, wearDirection;
            public readonly bool usePatternColor;
            public readonly List<TexturePaintStripeDefinition> stripes;

            public Settings(TexturePaintPluginParameterSet p)
            {
                outputAlbedo = p.Boolean("outputAlbedo", true);
                outputRoughness = p.Boolean("outputRoughness", true);
                outputNormalControl = p.Boolean("outputNormalControl", true);
                weave = (Weave)Mathf.Clamp(p.Integer("weave", 0), 0, 7);
                baseColor = p.Color("baseColor", Color.gray);
                threadColor = p.Color("threadColor", Color.white);
                wearColor = p.Color("wearColor", Color.gray);
                patternColor = p.Color("patternColor", Color.white);
                threadColorAmount = p.Float("threadColorAmount", .45f);
                weaveScale = p.Float("weaveScale", 96f);
                weaveAspect = p.Float("weaveAspect", 1f);
                rotation = p.Float("rotation", 0f);
                weaveContrast = p.Float("weaveContrast", .85f);
                threadRoundness = p.Float("threadRoundness", 2.4f);
                irregularity = p.Float("irregularity", .22f);
                heightStrength = p.Float("heightStrength", .1f);
                roughness = p.Float("roughness", .68f);
                roughnessVariation = p.Float("roughnessVariation", .18f);
                fiberColorVariation = p.Float("fiberColorVariation", .035f);
                fiberHeight = p.Float("fiberHeight", .035f);
                fiberRoughness = p.Float("fiberRoughness", .22f);
                stripeRepeatX = p.Float("stripeRepeatX", 4f);
                stripeRepeatY = p.Float("stripeRepeatY", 4f);
                stripeRotation = p.Float("stripeRotation", 0f);
                patternMode = p.Integer("patternMode", 0);
                patternDirection = p.Integer("patternDirection", 0);
                patternTiling = p.Float("patternTiling", 4f);
                patternAspect = p.Float("patternAspect", 1f);
                patternRotation = p.Float("patternRotation", 0f);
                patternOffsetX = p.Float("patternOffsetX", 0f);
                patternOffsetY = p.Float("patternOffsetY", 0f);
                usePatternColor = p.Boolean("usePatternColor", false);
                patternOpacity = p.Float("patternOpacity", 0f);
                patternEmboss = p.Float("patternEmboss", 0f);
                patternRoughness = p.Float("patternRoughness", 0f);
                wearAmount = p.Float("wearAmount", 0f);
                wearScale = p.Float("wearScale", 5f);
                wearThreshold = p.Float("wearThreshold", .5f);
                wearSoftness = p.Float("wearSoftness", .18f);
                wearDirection = p.Integer("wearDirection", 0);
                wearThreadBias = p.Float("wearThreadBias", .72f);
                wearThreadContrast = p.Float("wearThreadContrast", .08f);
                wearRoughnessChange = p.Float("wearRoughnessChange", .12f);
                wearFlattening = p.Float("wearFlattening", .35f);
                seed = p.Integer("seed", 1731);
                stripes = TexturePaintPluginParameterSet.CloneStripes(
                    p.Stripes("stripeList"));
            }
        }

        private readonly struct WeaveSample
        {
            public readonly float height, threadMix, fiber, roughness;
            public WeaveSample(float height, float threadMix, float fiber, float roughness)
            { this.height = height; this.threadMix = threadMix; this.fiber = fiber; this.roughness = roughness; }
        }

        private readonly struct OutputTarget
        {
            public readonly TexturePaintChannel channel;
            public readonly int width, height;
            private OutputTarget(TexturePaintChannel channel, TexturePaintReadOnlyChannelInfo info)
            { this.channel = channel; width = info.width; height = info.height; }

            public static List<OutputTarget> Find(TexturePaintReadContextV2 source,
                string surfaceId, Settings settings)
            {
                var result = new List<OutputTarget>();
                Add(TexturePaintChannel.Albedo, settings.outputAlbedo);
                Add(TexturePaintChannel.Roughness, settings.outputRoughness);
                Add(TexturePaintChannel.NormalControl, settings.outputNormalControl);
                return result;

                void Add(TexturePaintChannel channel, bool enabled)
                {
                    if (!enabled) return;
                    TexturePaintReadOnlyChannelInfo info = source.GetChannelInfo(surfaceId, channel);
                    if (info != null) result.Add(new OutputTarget(channel, info));
                }
            }
        }

        private sealed class OutputBuffers
        {
            private readonly Color32[] albedo, roughness, normalControl;
            public OutputBuffers(int count, Settings settings)
            {
                if (settings.outputAlbedo) albedo = new Color32[count];
                if (settings.outputRoughness) roughness = new Color32[count];
                if (settings.outputNormalControl) normalControl = new Color32[count];
            }
            public void Set(int index, Color color, float rough, float height)
            {
                if (albedo != null) albedo[index] = color;
                if (roughness != null)
                { byte b = ToByte(rough); roughness[index] = new Color32(b, b, b, 255); }
                if (normalControl != null)
                { byte b = ToByte(height); normalControl[index] = new Color32(b, b, b, 255); }
            }
            public Color32[] Get(TexturePaintChannel channel) => channel switch
            {
                TexturePaintChannel.Albedo => albedo,
                TexturePaintChannel.Roughness => roughness,
                TexturePaintChannel.NormalControl => normalControl,
                _ => null
            };
        }

        private static byte ToByte(float value) =>
            (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        private static Vector2 RotateCentered(Vector2 uv, float radians)
        {
            float x = uv.x - .5f, y = uv.y - .5f;
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(x * c - y * s + .5f, x * s + y * c + .5f);
        }
        private static float ThreadRidge(float value, float roundness) =>
            Mathf.Pow(Mathf.Max(0f, Mathf.Cos((value - .5f) * Mathf.PI)), roundness);
        private static int PositiveMod(int value, int modulus) => (value % modulus + modulus) % modulus;
        private static float Repeat(float value) => value - Mathf.Floor(value);
        private static float CircularDistance(float a, float b)
        { float d = Math.Abs(a - b); return Math.Min(d, 1f - d); }
        private static float Luma(Color c) => Mathf.Clamp01(c.r * .2126f + c.g * .7152f + c.b * .0722f);
        private static Color AddRgb(Color c, float value) =>
            new Color(Mathf.Clamp01(c.r + value), Mathf.Clamp01(c.g + value),
                Mathf.Clamp01(c.b + value), c.a);
        private static float SmoothStep(float minimum, float maximum, float value)
        { float t = Mathf.Clamp01((value - minimum) / Math.Max(.00001f, maximum - minimum)); return t * t * (3f - 2f * t); }
        private static float Fractal(float x, float y, float scale, int seed)
        {
            float sum = 0f, weight = 0f, amplitude = 1f;
            float offset = seed * .001371f;
            scale = Math.Max(.001f, scale);
            for (int octave = 0; octave < 4; octave++)
            {
                sum += Mathf.PerlinNoise(x * scale + offset * (octave + 1),
                    y * scale - offset * (octave + 2)) * amplitude;
                weight += amplitude; amplitude *= .5f; scale *= 2f;
            }
            return sum / Math.Max(.00001f, weight);
        }

        private static TexturePaintPluginParameterDefinition Header(string id, string name,
            string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.Header };
        private static TexturePaintPluginParameterDefinition Float(string id, string name,
            float min, float max, float value, string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.Float, minimum = min, maximum = max, defaultNumber = value };
        private static TexturePaintPluginParameterDefinition Integer(string id, string name,
            int min, int max, int value, string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.Integer, minimum = min, maximum = max, defaultNumber = value };
        private static TexturePaintPluginParameterDefinition Boolean(string id, string name,
            bool value, string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.Boolean, defaultBoolean = value };
        private static TexturePaintPluginParameterDefinition ColorParameter(string id, string name,
            Color value, string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.Color, defaultColor = value };
        private static TexturePaintPluginParameterDefinition EnumParameter(string id, string name,
            string[] options, int value, string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.Enum, minimum = 0, maximum = options.Length - 1, defaultNumber = value, enumOptions = options };
        private static TexturePaintPluginParameterDefinition SpriteParameter(string id, string name,
            string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.Sprite };
        private static TexturePaintPluginParameterDefinition StripeList(string id, string name,
            string description) => new TexturePaintPluginParameterDefinition
            { id = id, displayName = name, description = description, type = TexturePaintPluginParameterType.StripeList };
    }
}
