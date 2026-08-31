using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>
    /// Physically-scaled edge/valley corrosion with gravity-driven trails. Overlay Painter uses
    /// CSDrippingCorrosion when available; this implementation remains as the portable CPU fallback.
    /// </summary>
    public sealed class DrippingCorrosionGeneratorPlugin : ScriptableObject,
        ITexturePaintGeneratorV2, ITexturePaintGpuGeneratorV2
    {
        private const int RowsPerTile = 128;
        private static readonly TexturePaintPluginDescriptor descriptor = CreateDescriptor();

        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public string GpuKernelName => "CSDrippingCorrosion";

        public Task ExecuteAsync(TexturePaintCommandContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Settings settings = new Settings(context.parameters);
            return Task.Run(() => Generate(context, settings), context.cancellationToken);
        }

        private static TexturePaintPluginDescriptor CreateDescriptor() => new()
        {
            id = "com.uma.texturepaint.dripping-corrosion",
            displayName = "Dripping Corrosion",
            description = "Realistic corrosion seeded by exposed edges, concave valleys, and " +
                          "occlusion, with gravity-driven drips, pits, crust, and fractal breakup. " +
                          "Physical sizes use Unity's 1 unit = 1 meter convention.",
            pluginVersion = "1.0.0",
            capabilities = TexturePaintPluginCapability.Generator |
                           TexturePaintPluginCapability.ReadsMeshMaps |
                           TexturePaintPluginCapability.LongRunning |
                           TexturePaintPluginCapability.GpuAccelerated,
            declaredChannels = TexturePaintChannelMask.Albedo |
                               TexturePaintChannelMask.Roughness |
                               TexturePaintChannelMask.Metallic |
                               TexturePaintChannelMask.AmbientOcclusion |
                               TexturePaintChannelMask.NormalControl,
            readChannels = TexturePaintChannelMask.AmbientOcclusion,
            channelSnapshotMaximumResolution = 1024,
            requiredMeshMaps = TexturePaintMeshMapMask.WorldPosition |
                               TexturePaintMeshMapMask.WorldNormal |
                               TexturePaintMeshMapMask.SignedCurvature |
                               TexturePaintMeshMapMask.AmbientOcclusion |
                               TexturePaintMeshMapMask.SurfaceId,
            parameters = new List<TexturePaintPluginParameterDefinition>
            {
                Header("placement", "Corrosion Placement",
                    "Seed corrosion from convex edges, concave valleys, and sheltered surface regions."),
                Float("amount", "Overall Amount", 0f, 2f, 0.85f,
                    "Overall corrosion coverage."),
                Float("edgeAmount", "Exposed Edge Amount", 0f, 2f, 0.75f,
                    "Corrosion seeded on convex exposed edges."),
                Float("valleyAmount", "Valley Amount", 0f, 2f, 1.1f,
                    "Corrosion accumulated in concave valleys and occluded recesses."),
                Float("detectionLevel", "Feature Threshold", 0f, 0.95f, 0.08f,
                    "Restricts corrosion to progressively stronger edges and valleys."),
                Float("corrosionSpreadMeters", "Corrosion Spread (m)", 0f, 0.25f, 0.012f,
                    "World-space spread away from source edges and valleys."),

                Header("drips", "Gravity Drips",
                    "Trace corrosion downward from detected sources using world-space gravity."),
                Float("dripAmount", "Drip Amount", 0f, 2f, 0.9f,
                    "Strength of downward trails beneath corrosion sources."),
                Float("dripLengthMeters", "Drip Length (m)", 0f, 2f, 0.22f,
                    "Maximum gravity-driven trail length in meters."),
                Float("dripWidthMeters", "Drip Width (m)", 0.0005f, 0.2f, 0.006f,
                    "Typical width of individual corrosion trails in meters."),
                Float("dripDensity", "Drip Density", 0f, 1f, 0.62f,
                    "Fraction of eligible source regions that produce a visible drip."),
                Float("gravityX", "Gravity X", -1f, 1f, 0f,
                    "World-space gravity direction X component."),
                Float("gravityY", "Gravity Y", -1f, 1f, -1f,
                    "World-space gravity direction Y component; Unity gravity normally points down."),
                Float("gravityZ", "Gravity Z", -1f, 1f, 0f,
                    "World-space gravity direction Z component."),

                Header("breakupSection", "Fractal Breakup & Surface Damage",
                    "Layered breakup prevents uniform procedural borders and produces crust and pits."),
                Integer("seed", "Seed", 0, 100000, 1847,
                    "Changes the deterministic corrosion pattern."),
                Float("breakupSizeMeters", "Breakup Size (m)", 0.001f, 1f, 0.035f,
                    "World-space size of the broadest fractal breakup features."),
                Integer("fractalLevels", "Fractal Levels", 1, 7, 5,
                    "Number of progressively finer breakup octaves."),
                Float("fractalPersistence", "Fractal Persistence", 0.1f, 0.9f, 0.52f,
                    "Strength retained by each finer octave."),
                Float("breakup", "Breakup Amount", 0f, 1f, 0.72f,
                    "Removes portions of otherwise uniform corrosion."),
                Float("pitSizeMeters", "Pit Size (m)", 0.0005f, 0.1f, 0.004f,
                    "Typical world-space pitting size."),
                Float("pitDepth", "Pit Depth", 0f, 0.5f, 0.09f,
                    "Recess applied through Normal Control."),
                Float("crustHeight", "Crust Height", 0f, 0.5f, 0.055f,
                    "Raised flaky corrosion applied through Normal Control."),

                Header("material", "Corroded Material",
                    "Material response written under generated coverage."),
                ColorParameter("freshColor", "Fresh Corrosion", new Color(0.34f, 0.075f, 0.018f, 1f),
                    "Dark red-brown corrosion in pits and wet valleys."),
                ColorParameter("dryColor", "Dry Corrosion", new Color(0.72f, 0.23f, 0.045f, 1f),
                    "Orange dry oxide on exposed crust."),
                ColorParameter("streakColor", "Drip Color", new Color(0.24f, 0.055f, 0.018f, 1f),
                    "Color of gravity-driven corrosion streaks."),
                Float("roughness", "Corrosion Roughness", 0f, 1f, 0.88f,
                    "Roughness of oxidized material."),
                Float("metallic", "Remaining Metallic", 0f, 1f, 0.04f,
                    "Metallic response remaining beneath oxidation."),
                Float("ambientOcclusion", "Pit AO", 0f, 1f, 0.32f,
                    "Ambient-occlusion value in corroded pits and valleys.")
            }
        };

        private static void Generate(TexturePaintCommandContextV2 context, Settings settings)
        {
            int surfaces = Mathf.Max(1, context.source.surfaceIds.Count);
            for (int surfaceIndex = 0; surfaceIndex < context.source.surfaceIds.Count; surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];
                var input = new Inputs(context, surfaceId);
                List<Target> targets = Target.Find(context.source, surfaceId);
                if (targets.Count == 0) continue;
                var groups = new Dictionary<long, List<Target>>();
                for (int i = 0; i < targets.Count; i++)
                {
                    long key = ((long)targets[i].width << 32) | (uint)targets[i].height;
                    if (!groups.TryGetValue(key, out List<Target> group))
                        groups.Add(key, group = new List<Target>());
                    group.Add(targets[i]);
                }

                foreach (List<Target> group in groups.Values)
                {
                    int width = group[0].width, height = group[0].height;
                    for (int y0 = 0; y0 < height; y0 += RowsPerTile)
                    {
                        int rows = Mathf.Min(RowsPerTile, height - y0);
                        Buffers output = GenerateTile(input, settings, width, height, y0, rows,
                            context);
                        if (output.any)
                            for (int i = 0; i < group.Count; i++)
                                Write(context, surfaceId, group[i], y0, rows, output);
                        context.progress?.Report((surfaceIndex + (y0 + rows) / (float)height) /
                                                 surfaces);
                    }
                }
            }
            context.progress?.Report(1f);
        }

        private static Buffers GenerateTile(Inputs input, Settings s, int width, int height,
            int y0, int rows, TexturePaintCommandContextV2 context)
        {
            var output = new Buffers(width * rows);
            var options = new ParallelOptions { CancellationToken = context.cancellationToken };
            Parallel.For(0, rows, options, localY =>
            {
                int y = y0 + localY;
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    if (!input.Covered(u, v)) continue;
                    Sample sample = Evaluate(input, s, u, v, width, height);
                    if (sample.coverage <= 0.0001f) continue;
                    output.any = true;
                    int index = localY * width + x;
                    output.albedo[index] = To32(WithAlpha(sample.color, sample.coverage));
                    output.roughness[index] = To32(Scalar(s.roughness, sample.coverage));
                    output.metallic[index] = To32(Scalar(s.metallic, sample.coverage));
                    output.ambientOcclusion[index] = To32(Scalar(
                        Mathf.Lerp(1f, s.ambientOcclusion, sample.damage), sample.coverage));
                    output.normalControl[index] = To32(Scalar(Mathf.Clamp01(0.5f +
                        sample.crust * s.crustHeight - sample.pits * s.pitDepth), sample.coverage));
                }
            });
            return output;
        }

        private static Sample Evaluate(Inputs input, Settings s, float u, float v,
            int width, int height)
        {
            Vector3 position = input.Position(u, v);
            float curvature = input.Curvature(u, v);
            float cavity = input.Cavity(u, v);
            float edge = Mathf.Max(0f, curvature) * s.edgeAmount;
            float valley = Mathf.Max(Mathf.Max(0f, -curvature), cavity) * s.valleyAmount;
            float source = SmoothStep(s.detectionLevel, 1f, Mathf.Max(edge, valley));

            float du = 1f / Mathf.Max(1, width), dv = 1f / Mathf.Max(1, height);
            Vector3 dpdu = (input.Position(u + du, v) - input.Position(u - du, v)) /
                            Mathf.Max(0.00001f, 2f * du);
            Vector3 dpdv = (input.Position(u, v + dv) - input.Position(u, v - dv)) /
                            Mathf.Max(0.00001f, 2f * dv);
            float metersPerU = Mathf.Max(0.00001f, dpdu.magnitude);
            float metersPerV = Mathf.Max(0.00001f, dpdv.magnitude);
            if (s.corrosionSpreadMeters > 0f)
            {
                float spreadU = s.corrosionSpreadMeters / metersPerU;
                float spreadV = s.corrosionSpreadMeters / metersPerV;
                source = Mathf.Max(source, input.Source(u - spreadU, v, s) * 0.72f);
                source = Mathf.Max(source, input.Source(u + spreadU, v, s) * 0.72f);
                source = Mathf.Max(source, input.Source(u, v - spreadV, s) * 0.72f);
                source = Mathf.Max(source, input.Source(u, v + spreadV, s) * 0.72f);
            }

            float broad = WeatheringFractal.Sample(position, u, v, true,
                1f / s.breakupSizeMeters, s.seed, s.fractalLevels, s.fractalPersistence);
            float breakupMask = WeatheringFractal.Breakup(broad, s.breakup);
            float drip = TraceDrip(input, s, position, u, v, dpdu, dpdv);
            float coverage = Mathf.Clamp01(Mathf.Max(source * breakupMask, drip) * s.amount);
            float pitNoise = WeatheringFractal.Sample(position, u, v, true,
                1f / s.pitSizeMeters, s.seed + 911, 3, 0.56f);
            float pits = SmoothStep(0.68f, 0.94f, pitNoise) * coverage;
            float crust = SmoothStep(0.42f, 0.82f, 1f - pitNoise) * source * breakupMask;
            float damage = Mathf.Clamp01(Mathf.Max(valley, Mathf.Max(pits, drip)));
            Color color = Color.Lerp(s.freshColor, s.dryColor,
                Mathf.Clamp01(crust + Mathf.Max(0f, curvature)));
            color = Color.Lerp(color, s.streakColor, Mathf.Clamp01(drip));
            return new Sample
            {
                coverage = coverage, pits = pits, crust = crust, damage = damage, color = color
            };
        }

        private static float TraceDrip(Inputs input, Settings s, Vector3 position, float u,
            float v, Vector3 dpdu, Vector3 dpdv)
        {
            if (s.dripAmount <= 0f || s.dripLengthMeters <= 0f) return 0f;
            Vector2 gravityUv = new Vector2(Vector3.Dot(s.gravity, dpdu) /
                Mathf.Max(0.000001f, dpdu.sqrMagnitude), Vector3.Dot(s.gravity, dpdv) /
                Mathf.Max(0.000001f, dpdv.sqrMagnitude));
            if (gravityUv.sqrMagnitude <= 0.000001f) return 0f;
            Vector2 direction = gravityUv.normalized;
            float metersPerUv = (dpdu * direction.x + dpdv * direction.y).magnitude;
            if (metersPerUv <= 0.00001f) return 0f;
            float uvLength = s.dripLengthMeters / metersPerUv;
            Color island = input.Id(u, v);
            float best = 0f;
            const int Steps = 20;
            for (int step = 1; step <= Steps; step++)
            {
                float t = step / (float)Steps;
                float su = u - direction.x * uvLength * t;
                float sv = v - direction.y * uvLength * t;
                if (!input.SameIsland(island, input.Id(su, sv))) continue;
                Vector3 origin = input.Position(su, sv);
                float descent = Vector3.Dot(position - origin, s.gravity);
                if (descent <= 0f || descent > s.dripLengthMeters * 1.15f) continue;
                float seed = input.Source(su, sv, s);
                if (seed <= 0f) continue;
                float line = WeatheringFractal.Sample(Vector3.Scale(position,
                        new Vector3(1f, 0.18f, 1f)),
                    u, v, true, 1f / s.dripWidthMeters, s.seed + 431, 3, 0.58f);
                line = SmoothStep(1f - s.dripDensity, 1f, line);
                best = Mathf.Max(best, seed * line * (1f - t * 0.72f));
            }
            return Mathf.Clamp01(best * s.dripAmount);
        }

        private static void Write(TexturePaintCommandContextV2 context, string surfaceId,
            Target target, int y, int rows, Buffers output)
        {
            Color32[] pixels = target.channel switch
            {
                TexturePaintChannel.Albedo => output.albedo,
                TexturePaintChannel.Roughness => output.roughness,
                TexturePaintChannel.Metallic => output.metallic,
                TexturePaintChannel.AmbientOcclusion => output.ambientOcclusion,
                TexturePaintChannel.NormalControl => output.normalControl,
                _ => null
            };
            if (pixels == null) return;
            context.WriteTileCompactOwned(surfaceId, target.channel,
                new RectInt(0, y, target.width, rows), pixels,
                target.channel == TexturePaintChannel.Albedo
                    ? TexturePaintPluginColorSpace.Linear : TexturePaintPluginColorSpace.Data,
                TexturePaintPluginBlend.Normal);
        }

        private sealed class Inputs
        {
            private readonly TexturePaintReadOnlyMeshMap position, curvature, ao, id;
            private readonly TexturePaintReadOnlyImage sourceAo;

            public Inputs(TexturePaintCommandContextV2 context, string surfaceId)
            {
                position = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldPosition);
                curvature = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SignedCurvature);
                ao = context.GetMeshMap(surfaceId, TexturePaintMeshMap.AmbientOcclusion);
                id = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SurfaceId);
                sourceAo = context.source.Get(surfaceId, TexturePaintChannel.AmbientOcclusion);
            }

            public bool Covered(float u, float v) => id == null || Id(u, v).a >= 0.5f;
            public Color Id(float u, float v) => id?.GetPixelBilinear(Repeat(u), Repeat(v)) ?? Color.white;
            public Vector3 Position(float u, float v)
            {
                Color c = position?.GetPixelBilinear(Repeat(u), Repeat(v)) ??
                          new Color(Repeat(u), Repeat(v), 0f, 1f);
                return new Vector3(c.r, c.g, c.b);
            }
            public float Curvature(float u, float v) => curvature == null ? 0f :
                curvature.GetPixelBilinear(Repeat(u), Repeat(v)).r * 2f - 1f;
            public float Cavity(float u, float v)
            {
                float mesh = ao == null ? 0f : 1f - ao.GetPixelBilinear(Repeat(u), Repeat(v)).r;
                float source = sourceAo == null ? 0f : 1f -
                    sourceAo.GetPixelBilinear(Repeat(u), Repeat(v)).r;
                return Mathf.Max(mesh, source);
            }
            public float Source(float u, float v, Settings s)
            {
                float c = Curvature(u, v);
                return SmoothStep(s.detectionLevel, 1f, Mathf.Max(
                    Mathf.Max(0f, c) * s.edgeAmount,
                    Mathf.Max(Mathf.Max(0f, -c), Cavity(u, v)) * s.valleyAmount));
            }
            public bool SameIsland(Color a, Color b) => a.a >= 0.5f && b.a >= 0.5f &&
                Mathf.Abs(a.g - b.g) <= 0.1f && Mathf.Abs(a.b - b.b) <= 0.1f;
        }

        private readonly struct Target
        {
            public readonly TexturePaintChannel channel;
            public readonly int width, height;
            private Target(TexturePaintChannel channel, TexturePaintReadOnlyChannelInfo info)
            { this.channel = channel; width = info.width; height = info.height; }
            public static List<Target> Find(TexturePaintReadContextV2 source, string surfaceId)
            {
                var result = new List<Target>();
                Add(TexturePaintChannel.Albedo); Add(TexturePaintChannel.Roughness);
                Add(TexturePaintChannel.Metallic); Add(TexturePaintChannel.AmbientOcclusion);
                Add(TexturePaintChannel.NormalControl);
                return result;
                void Add(TexturePaintChannel channel)
                {
                    TexturePaintReadOnlyChannelInfo info = source.GetChannelInfo(surfaceId, channel);
                    if (info != null) result.Add(new Target(channel, info));
                }
            }
        }

        private sealed class Buffers
        {
            public readonly Color32[] albedo, roughness, metallic, ambientOcclusion, normalControl;
            public bool any;
            public Buffers(int count)
            {
                albedo = new Color32[count]; roughness = new Color32[count];
                metallic = new Color32[count]; ambientOcclusion = new Color32[count];
                normalControl = new Color32[count];
            }
        }

        private struct Sample
        { public float coverage, pits, crust, damage; public Color color; }

        private readonly struct Settings
        {
            public readonly float amount, edgeAmount, valleyAmount, detectionLevel;
            public readonly float corrosionSpreadMeters, dripAmount, dripLengthMeters;
            public readonly float dripWidthMeters, dripDensity, breakupSizeMeters;
            public readonly int seed, fractalLevels;
            public readonly float fractalPersistence, breakup, pitSizeMeters, pitDepth, crustHeight;
            public readonly Vector3 gravity;
            public readonly Color freshColor, dryColor, streakColor;
            public readonly float roughness, metallic, ambientOcclusion;
            public Settings(TexturePaintPluginParameterSet p)
            {
                p ??= new TexturePaintPluginParameterSet();
                amount = Pos(p, "amount", 0.85f); edgeAmount = Pos(p, "edgeAmount", 0.75f);
                valleyAmount = Pos(p, "valleyAmount", 1.1f);
                detectionLevel = Mathf.Clamp(p.Float("detectionLevel", 0.08f), 0f, 0.95f);
                corrosionSpreadMeters = Pos(p, "corrosionSpreadMeters", 0.012f);
                dripAmount = Pos(p, "dripAmount", 0.9f);
                dripLengthMeters = Pos(p, "dripLengthMeters", 0.22f);
                dripWidthMeters = Mathf.Max(0.0005f, p.Float("dripWidthMeters", 0.006f));
                dripDensity = Mathf.Clamp01(p.Float("dripDensity", 0.62f));
                gravity = new Vector3(p.Float("gravityX", 0f), p.Float("gravityY", -1f),
                    p.Float("gravityZ", 0f));
                gravity = gravity.sqrMagnitude > 0.000001f ? gravity.normalized : Vector3.down;
                seed = p.Integer("seed", 1847);
                breakupSizeMeters = Mathf.Max(0.001f, p.Float("breakupSizeMeters", 0.035f));
                fractalLevels = Mathf.Clamp(p.Integer("fractalLevels", 5), 1, 7);
                fractalPersistence = Mathf.Clamp(p.Float("fractalPersistence", 0.52f), 0.1f, 0.9f);
                breakup = Mathf.Clamp01(p.Float("breakup", 0.72f));
                pitSizeMeters = Mathf.Max(0.0005f, p.Float("pitSizeMeters", 0.004f));
                pitDepth = Pos(p, "pitDepth", 0.09f); crustHeight = Pos(p, "crustHeight", 0.055f);
                freshColor = p.Color("freshColor", new Color(0.34f, 0.075f, 0.018f, 1f));
                dryColor = p.Color("dryColor", new Color(0.72f, 0.23f, 0.045f, 1f));
                streakColor = p.Color("streakColor", new Color(0.24f, 0.055f, 0.018f, 1f));
                roughness = Mathf.Clamp01(p.Float("roughness", 0.88f));
                metallic = Mathf.Clamp01(p.Float("metallic", 0.04f));
                ambientOcclusion = Mathf.Clamp01(p.Float("ambientOcclusion", 0.32f));
            }
        }

        private static TexturePaintPluginParameterDefinition Header(string id, string name,
            string description) => new() { id = id, displayName = name, description = description,
                type = TexturePaintPluginParameterType.Header };
        private static TexturePaintPluginParameterDefinition Float(string id, string name,
            float min, float max, float value, string description) => new()
            { id = id, displayName = name, type = TexturePaintPluginParameterType.Float,
                minimum = min, maximum = max, defaultNumber = value, description = description };
        private static TexturePaintPluginParameterDefinition Integer(string id, string name,
            int min, int max, int value, string description) => new()
            { id = id, displayName = name, type = TexturePaintPluginParameterType.Integer,
                minimum = min, maximum = max, defaultNumber = value, description = description };
        private static TexturePaintPluginParameterDefinition ColorParameter(string id, string name,
            Color value, string description) => new()
            { id = id, displayName = name, type = TexturePaintPluginParameterType.Color,
                defaultColor = value, description = description };
        private static float Pos(TexturePaintPluginParameterSet p, string id, float fallback) =>
            Mathf.Max(0f, p.Float(id, fallback));
        private static float SmoothStep(float a, float b, float value)
        { float t = Mathf.Clamp01((value - a) / Mathf.Max(0.00001f, b - a)); return t * t * (3f - 2f * t); }
        private static float Repeat(float value) => value - Mathf.Floor(value);
        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
        private static Color Scalar(float value, float alpha) => new(value, value, value, alpha);
        private static Color32 To32(Color value) => value;
    }
}
