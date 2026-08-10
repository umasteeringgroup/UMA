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
            pluginVersion = "1.0.0",
            capabilities = TexturePaintPluginCapability.Generator | TexturePaintPluginCapability.LongRunning,
            declaredChannels = TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Roughness |
                               TexturePaintChannelMask.Metallic | TexturePaintChannelMask.AmbientOcclusion |
                               TexturePaintChannelMask.NormalControl,
            readChannels = TexturePaintChannelMask.All,
            supportedTargets = TexturePaintPluginTarget.All,
            channelSnapshotMaximumResolution = 4096,
            parameters = Parameters()
        };

        public static Task Execute(TexturePaintCommandContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var s = new Settings(context.parameters);
            if (context.target == TexturePaintPluginTarget.LayerContent && !s.AnyOutput)
                throw new InvalidOperationException("Enable at least one output channel.");
            TexturePaintReadOnlyParameterTexture pattern = context.GetTextureParameter("pattern");
            TexturePaintReadOnlyParameterTexture atlas = context.GetTextureParameter("atlas");
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
            for (int y0 = 0; y0 < height; y0 += Rows)
            {
                c.cancellationToken.ThrowIfCancellationRequested();
                int rows = Math.Min(Rows, height - y0);
                var buffers = new Dictionary<TexturePaintChannel, Color32[]>();
                if (!mask) for (int i = 0; i < channels.Count; i++)
                    buffers[channels[i]] = new Color32[width * rows];
                Color32[] maskPixels = mask ? new Color32[width * rows] : null;
                for (int ly = 0; ly < rows; ly++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 uv = Rotate(new Vector2((x + .5f) / width, (y0 + ly + .5f) / height), s.rotation);
                    Sample sample = SampleMode(s, uv, pattern, atlas);
                    int index = ly * width + x;
                    if (mask) { byte m = B(sample.coverage); maskPixels[index] = new Color32(m, m, m, 255); }
                    else for (int i = 0; i < channels.Count; i++)
                    {
                        TexturePaintChannel channel = channels[i];
                        buffers[channel][index] = channel switch
                        {
                            TexturePaintChannel.Albedo => sample.color,
                            TexturePaintChannel.Roughness => Gray(sample.roughness),
                            TexturePaintChannel.Metallic => Gray(sample.metallic),
                            TexturePaintChannel.AmbientOcclusion => Gray(sample.ao),
                            _ => Gray(Mathf.Clamp01(.5f + sample.height))
                        };
                    }
                }
                RectInt rect = new RectInt(0, y0, width, rows);
                if (mask) c.WriteMaskTileCompact(id, rect, maskPixels, TexturePaintPluginBlend.Replace);
                else foreach (KeyValuePair<TexturePaintChannel, Color32[]> pair in buffers)
                    c.WriteTileCompact(id, pair.Key, rect, pair.Value,
                        TexturePaintChannelUtility.IsColor(pair.Key) ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data,
                        TexturePaintPluginBlend.Replace);
                c.progress?.Report((si + (y0 + rows) / (float)height) / surfaces);
            }
        }

        private static Sample SampleMode(Settings s, Vector2 uv,
            TexturePaintReadOnlyParameterTexture pattern, TexturePaintReadOnlyParameterTexture atlas)
        {
            float noise = Fbm(uv * s.breakupScale, s.seed);
            switch (s.mode)
            {
                case Mode.Quilt:
                {
                    Vector2 q = uv * new Vector2(s.scale * s.aspect, s.scale);
                    float fx = Frac(q.x) - .5f, fy = Frac(q.y) - .5f;
                    if (s.quiltPattern == 1) { float a = (fx + fy) * .7071f, b = (fx - fy) * .7071f; fx = a; fy = b; }
                    float seam = .5f - Math.Max(Math.Abs(fx), Math.Abs(fy));
                    if (s.quiltPattern == 2) seam = Math.Abs(Mathf.Sin((fx + fy) * Mathf.PI)) * .5f;
                    float stitch = 1f - Smooth(0f, s.stitchWidth, seam);
                    stitch *= Step(Frac((q.x + q.y) * s.stitchDensity), s.stitchLength);
                    float puff = Mathf.Pow(Mathf.Clamp01(seam * 2f), s.puffRoundness);
                    float cover = Mathf.Clamp01(Mathf.Max(puff * .35f, stitch));
                    Color color = Color.Lerp(s.baseColor, s.accentColor, stitch * s.colorAmount);
                    return new Sample(color, Mathf.Clamp01(s.baseRoughness + stitch * .12f - puff * .08f),
                        s.baseMetallic, Mathf.Clamp01(1f - stitch * s.aoStrength),
                        (puff * s.puffHeight - stitch * s.stitchDepth) * (.7f + noise * .3f), cover);
                }
                case Mode.Embroidery:
                {
                    float motif = Pattern(pattern, uv * s.patternTiling + s.offset, s.patternThreshold);
                    if (pattern == null) motif = Mathf.Clamp01(.5f + .5f * Mathf.Sin((uv.x + uv.y) * s.scale * 6.283f));
                    float angle = s.fiberDirection * Mathf.Deg2Rad;
                    float along = uv.x * Mathf.Cos(angle) + uv.y * Mathf.Sin(angle);
                    float thread = Mathf.Pow(.5f + .5f * Mathf.Cos(along * s.threadDensity * 6.283f), 3f);
                    float broken = Smooth(s.breakup - .2f, s.breakup + .2f, noise);
                    float cover = motif * Mathf.Lerp(1f, broken, s.breakup);
                    Color color = Color.Lerp(s.baseColor, s.accentColor, cover * (.65f + thread * .35f));
                    return new Sample(color, Mathf.Clamp01(s.baseRoughness - cover * s.sheen + thread * .08f),
                        s.baseMetallic, Mathf.Clamp01(1f - cover * s.aoStrength * .2f),
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
                        Mathf.Lerp(s.baseMetallic, 0f, cover), Mathf.Clamp01(1f - cover * s.aoStrength),
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
            float tint = Mathf.Lerp(1f - s.tintVariation, 1f + s.tintVariation, h2);
            Color stamped = s.useAtlasColor ? new Color(stamp.r * tint, stamp.g * tint, stamp.b * tint, 1f) : s.accentColor * tint;
            Color color = Color.Lerp(s.baseColor, stamped, cover);
            return new Sample(color, Mathf.Clamp01(s.baseRoughness + cover * s.scatterRoughness),
                Mathf.Clamp01(s.baseMetallic + cover * s.scatterMetallic), Mathf.Clamp01(1f - cover * s.aoStrength),
                cover * s.scatterHeight, cover);
        }

        private static float Pattern(TexturePaintReadOnlyParameterTexture texture, Vector2 uv, float threshold)
        {
            if (texture == null) return 0f;
            Color c = texture.GetPixelBilinear(Frac(uv.x), Frac(uv.y));
            float l = c.a * (c.r * .2126f + c.g * .7152f + c.b * .0722f);
            return Smooth(threshold - .08f, threshold + .08f, l);
        }

        private readonly struct Sample
        {
            public readonly Color32 color; public readonly float roughness, metallic, ao, height, coverage;
            public Sample(Color color, float roughness, float metallic, float ao, float height, float coverage)
            { this.color = color; this.roughness = roughness; this.metallic = metallic; this.ao = ao; this.height = height; this.coverage = coverage; }
        }

        private sealed class Settings
        {
            public readonly Mode mode; public readonly bool albedo, roughness, metallic, ao, normalControl;
            public readonly Color baseColor, accentColor, holeColor; public readonly float baseRoughness, baseMetallic,
                scale, aspect, rotation, breakupScale, colorAmount, stitchWidth, stitchDensity, stitchLength,
                puffHeight, puffRoundness, stitchDepth, aoStrength, patternTiling, patternThreshold, fiberDirection,
                threadDensity, breakup, sheen, embroideryHeight, holeRadius, edgeSoftness, bevelWidth, bevelHeight,
                holeDepth, holeRoughness, jitter, density, scatterSize, sizeVariation, rotationVariation,
                luminanceMask, tintVariation, scatterRoughness, scatterMetallic, scatterHeight;
            public readonly int seed, quiltPattern, perforationPattern, atlasColumns, atlasRows, scatterGridX, scatterGridY;
            public readonly bool useAtlasColor; public readonly Vector2 offset;
            public bool AnyOutput => albedo || roughness || metallic || ao || normalControl;
            public Settings(TexturePaintPluginParameterSet p)
            {
                mode = (Mode)Mathf.Clamp(p.Integer("mode"), 0, 3); albedo = p.Boolean("outputAlbedo", true);
                roughness = p.Boolean("outputRoughness", true); metallic = p.Boolean("outputMetallic");
                ao = p.Boolean("outputAO", true); normalControl = p.Boolean("outputNormalControl", true);
                baseColor = p.Color("baseColor", new Color(.35f,.28f,.22f,1)); accentColor = p.Color("accentColor", Color.white);
                holeColor = p.Color("holeColor", new Color(.025f,.02f,.015f,1)); baseRoughness=p.Float("baseRoughness",.65f);
                baseMetallic=p.Float("baseMetallic",0); scale=p.Float("scale",12); aspect=p.Float("aspect",1); rotation=p.Float("rotation",0);
                breakupScale=p.Float("breakupScale",8); seed=p.Integer("seed",1731); colorAmount=p.Float("colorAmount",.7f);
                quiltPattern=p.Integer("quiltPattern",1); stitchWidth=p.Float("stitchWidth",.035f); stitchDensity=p.Float("stitchDensity",10);
                stitchLength=p.Float("stitchLength",.6f); puffHeight=p.Float("puffHeight",.16f); puffRoundness=p.Float("puffRoundness",1.7f);
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
                scatterRoughness=p.Float("scatterRoughness",-.1f); scatterMetallic=p.Float("scatterMetallic",0); scatterHeight=p.Float("scatterHeight",.12f);
            }
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters() => new List<TexturePaintPluginParameterDefinition>
        {
            H("modeHeader","Surface System","Choose one production surface system; settings remain stored when switching modes."), E("mode","Mode",new[]{"Quilt","Embroidery","Perforation","Atlas Scatter"},0,"Generator mode."),
            H("outputs","Output Channels","In mask mode these material outputs are ignored and Coverage is written as grayscale."), B("outputAlbedo","Albedo",true,"Color output."), B("outputRoughness","Roughness",true,"Surface roughness."), B("outputMetallic","Metallic",false,"Metal response."), B("outputAO","Ambient Occlusion",true,"Crease/recess occlusion."), B("outputNormalControl","Normal Control",true,"Raised and recessed height."),
            H("common","Common Material","Shared scale, orientation, color and breakup."), C("baseColor","Base Color",new Color(.35f,.28f,.22f,1),"Underlying material."), C("accentColor","Thread / Stamp Color",Color.white,"Stitches, embroidery, or colorized atlas stamps."), F("baseRoughness","Base Roughness",0,1,.65f,"Underlying roughness."), F("baseMetallic","Base Metallic",0,1,0,"Underlying metallic."), F("scale","Pattern Scale",1,256,12,"Pattern repetitions per UV tile."), F("aspect","Aspect",.1f,10,1,"Horizontal pattern aspect."), F("rotation","Rotation",-180,180,0,"Pattern rotation."), I("seed","Seed",0,999999,1731,"Repeatable variation."), F("breakupScale","Breakup Scale",.1f,128,8,"Fractal breakup frequency."), F("colorAmount","Accent Color Amount",0,1,.7f,"Accent color contribution."), F("aoStrength","AO Strength",0,1,.7f,"Recess darkening."),
            H("quilt","Quilt","Padded cells, seam channels, and individual stitches."), E("quiltPattern","Pattern",new[]{"Square Channels","Diamond Channels","Wave Channels"},1,"Quilting layout."), F("stitchWidth","Stitch Width",.002f,.15f,.035f,"Stitch/seam width within a cell."), F("stitchDensity","Stitches / Cell",1,64,10,"Individual stitch frequency."), F("stitchLength","Stitch Duty",.05f,.95f,.6f,"Thread length versus gap."), F("puffHeight","Puff Height",0,.5f,.16f,"Raised padding."), F("puffRoundness","Puff Roundness",.25f,8,1.7f,"Pillow crown profile."), F("stitchDepth","Seam Depth",0,.5f,.12f,"Recessed seam height."),
            H("embroidery","Embroidery","Sprite-defined motifs filled with directional thread."), T("pattern","Pattern Texture","Alpha/luminance defines embroidered coverage."), F("patternTiling","Pattern Repeats",.1f,64,3,"Motif repetitions."), F("patternThreshold","Pattern Threshold",0,1,.4f,"Coverage cutoff."), F("offsetX","Offset X",-16,16,0,"Motif offset."), F("offsetY","Offset Y",-16,16,0,"Motif offset."), F("fiberDirection","Thread Direction",-180,180,45,"Satin stitch direction."), F("threadDensity","Thread Density",4,1024,180,"Visible thread ridges."), F("breakup","Thread Breakup",0,1,.18f,"Natural incomplete fibers."), F("sheen","Thread Sheen",0,1,.22f,"Roughness reduction on thread crowns."), F("embroideryHeight","Embroidery Height",0,.5f,.18f,"Raised thread height."),
            H("perforation","Perforation","Punched holes with bevel, depth and controllable spread. This shades holes; it does not alter mesh topology."), E("perforationPattern","Hole Layout",new[]{"Grid","Hex / Staggered","Organic Jitter"},1,"Hole distribution."), C("holeColor","Recess Color",new Color(.025f,.02f,.015f,1),"Interior/recess color."), F("holeRadius","Hole Radius",.01f,.48f,.24f,"Hole size within each repeat."), F("edgeSoftness","Edge Softness",.001f,.2f,.018f,"Antialias/edge wear."), F("bevelWidth","Bevel Width",.005f,.3f,.09f,"Rolled edge spread."), F("bevelHeight","Bevel Height",0,.5f,.1f,"Raised lip."), F("holeDepth","Hole Depth",0,.5f,.28f,"Normal Control recess."), F("holeRoughness","Interior Roughness",0,1,.86f,"Roughness inside holes."), F("jitter","Position Jitter",0,.8f,.18f,"Organic displacement."),
            H("atlasHeader","Atlas Scatter","Random cells from a regular texture atlas, with deterministic transform variation."), T("atlas","Atlas Texture","A regular grid atlas; alpha defines each stamp."), I("atlasColumns","Atlas Columns",1,64,4,"Cells across."), I("atlasRows","Atlas Rows",1,64,4,"Cells down."), I("scatterGridX","Scatter Columns",1,256,8,"Candidate stamps across UV."), I("scatterGridY","Scatter Rows",1,256,8,"Candidate stamps down UV."), F("density","Density",0,1,.6f,"Occupied candidates."), F("scatterSize","Stamp Size",.02f,2,.75f,"Size relative to candidate cell."), F("sizeVariation","Size Variation",0,1,.3f,"Random shrink/grow."), F("rotationVariation","Rotation Variation",0,360,180,"Random angle range."), F("luminanceMask","Use Luminance as Mask",0,1,0,"Blends alpha-only and alpha-times-luminance coverage."), B("useAtlasColor","Use Atlas Color",true,"Uses atlas RGB rather than Accent Color."), F("tintVariation","Tint Variation",0,1,.12f,"Per-stamp brightness variation."), F("scatterRoughness","Roughness Change",-1,1,-.1f,"Stamp roughness delta."), F("scatterMetallic","Metallic Change",-1,1,0,"Stamp metallic delta."), F("scatterHeight","Stamp Height",-.5f,.5f,.12f,"Normal Control height."),
        };

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
