using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    internal static partial class StubbleMakerGeneratorEngine
    {
        // All positions and lengths are in reference-texture pixels. No Unity objects or
        // mutable random state are touched by construction or parallel sampling.
        private sealed class NaturalHairField
        {
            private const int BinSize = 12;
            public readonly int width, height;
            private readonly int columns, rows;
            private readonly List<int>[] bins;
            private readonly List<NaturalStrand> strands = new();
            private readonly TexturePaintReadOnlyMeshMap ids;

            public NaturalHairField(Settings s, TexturePaintCommandContextV2 context,
                string surface, int outputWidth, int outputHeight)
            {
                float scale = s.referenceResolution / (float)Math.Max(outputWidth, outputHeight);
                width = Math.Max(1, Mathf.RoundToInt(outputWidth * scale));
                height = Math.Max(1, Mathf.RoundToInt(outputHeight * scale));
                columns = (width + BinSize - 1) / BinSize;
                rows = (height + BinSize - 1) / BinSize;
                bins = new List<int>[columns * rows];
                if (s.density <= 0f) return;
                TexturePaintReadOnlyMeshMap position = null, normal = null;
                MakeupFaceFrame frame = null;
                if (s.directionSpace != 1)
                {
                    position = context.GetMeshMap(surface, TexturePaintMeshMap.WorldPosition);
                    normal = context.GetMeshMap(surface, TexturePaintMeshMap.WorldNormal);
                    ids = context.GetMeshMap(surface, TexturePaintMeshMap.SurfaceId);
                    if (position == null || normal == null || ids == null)
                        throw new InvalidOperationException("Natural Stubble requires mesh position, normal and island maps. Choose UV Direction for a texture-only surface.");
                    if (s.profile == 0 && s.anatomicalFlow > 0f)
                    {
                        var anatomy = context.source.GetAnatomy(surface);
                        if (anatomy != null)
                        {
                            try { frame = MakeupFaceFrame.Resolve(anatomy); }
                            catch (InvalidOperationException) { /* Non-human or incomplete rig: gravity. */ }
                        }
                    }
                }
                float spacing = s.rootSpacing;
                int nx = Mathf.CeilToInt(width / spacing), ny = Mathf.CeilToInt(height / spacing);
                if ((long)nx * ny > 750000)
                    throw new InvalidOperationException("Natural Stubble exceeds 750,000 potential follicles. Increase Root Spacing or reduce Reference Resolution; output resolution can remain high.");
                for (int cy = 0; cy < ny; cy++)
                {
                    context.cancellationToken.ThrowIfCancellationRequested();
                    for (int cx = 0; cx < nx; cx++)
                    {
                        Vector2 root = Root(s, cx, cy);
                        if (root.x < 0 || root.y < 0 || root.x >= width || root.y >= height) continue;
                        float regional = ValueNoise(root / s.flowScale, s.seed + 701);
                        float density = s.density * Mathf.Lerp(1f, .2f + .8f * regional, s.patchiness);
                        if (Hash(cx, cy, s.seed + 3) >= density) continue;
                        // Priority-based exclusion removes near neighbours without a regular grid.
                        // Compare the potential population, not density-selected survivors.
                        float priority = Hash(cx, cy, s.seed + 709);
                        bool crowded = false;
                        for (int dy = -1; dy <= 1 && !crowded; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                if (Hash(cx + dx, cy + dy, s.seed + 709) < priority &&
                                    (Root(s, cx + dx, cy + dy) - root).sqrMagnitude < spacing * spacing * .16f)
                                { crowded = true; break; }
                            }
                        if (crowded) continue;
                        Vector2 uv = new(root.x / width, root.y / height);
                        float degrees = s.directionDegrees + SignedHash(cx, cy, s.seed + 23) * s.directionVariation
                            + (ValueNoise(root / s.flowScale, s.seed + 719) * 2f - 1f) * s.flowVariation;
                        Vector2 direction;
                        Color island = Color.white;
                        if (position != null)
                        {
                            Vector3 growth = Vector3.down;
                            if (frame != null)
                            {
                                Color p = position.GetPixel((int)(uv.x * position.width), (int)(uv.y * position.height));
                                growth = Vector3.Lerp(-frame.up, FacialGrowth(frame, new Vector3(p.r, p.g, p.b)), s.anatomicalFlow).normalized;
                            }
                            if (!TrySurfaceGrowthDirection(position, normal, ids, uv, width, height, growth, degrees, out direction)) continue;
                            island = ids.GetPixel((int)(uv.x * ids.width), (int)(uv.y * ids.height));
                        }
                        else direction = Rotate(Vector2.down, degrees * Mathf.Deg2Rad);

                        var strand = new NaturalStrand(s, cx, cy, root, direction, island);
                        int index = strands.Count;
                        strands.Add(strand);
                        Vector2 low = root, high = root;
                        foreach (var point in strand.points) { low = Vector2.Min(low, point); high = Vector2.Max(high, point); }
                        // Include filtering at 256px as well as the requested output size.
                        float padding = Mathf.Max(RootRadius(s), strand.width + s.shadowSpread + Mathf.Abs(s.shadowOffset))
                            + Mathf.Max(s.referenceResolution / 256f, scale);
                        low -= Vector2.one * padding; high += Vector2.one * padding;
                        for (int by = Mathf.Max(0, Mathf.FloorToInt(low.y / BinSize)); by <= Mathf.Min(rows - 1, Mathf.FloorToInt(high.y / BinSize)); by++)
                            for (int bx = Mathf.Max(0, Mathf.FloorToInt(low.x / BinSize)); bx <= Mathf.Min(columns - 1, Mathf.FloorToInt(high.x / BinSize)); bx++)
                                (bins[by * columns + bx] ??= new List<int>()).Add(index);
                    }
                }
            }

            private static Vector2 Root(Settings s, int x, int y) => new(
                (x + .5f + SignedHash(x, y, s.seed + 11) * .49f) * s.rootSpacing,
                (y + .5f + SignedHash(x, y, s.seed + 17) * .49f) * s.rootSpacing);

            private static Vector3 FacialGrowth(MakeupFaceFrame f, Vector3 point)
            {
                Vector3 p = f.Project(point) - f.mouth;
                float side = p.x / Mathf.Sqrt(p.x * p.x + .035f);
                float moustache = SmoothStep(-.06f, .12f, p.y) * (1f - SmoothStep(.45f, .9f, Mathf.Abs(p.x)));
                float neck = 1f - SmoothStep(-1.5f, -.8f, p.y);
                Vector3 cheek = -f.up + f.right * (side * .25f);
                Vector3 lip = -f.up * .35f + f.right * (side * .95f);
                Vector3 belowJaw = -f.up * .35f + f.right * (side * .8f);
                return Vector3.Lerp(Vector3.Lerp(cheek, lip, moustache), belowJaw, neck).normalized;
            }

            public HairSample Sample(Settings s, Vector2 pixel, float footprint)
            {
                var result = new HairSample();
                var candidates = bins[(int)pixel.y / BinSize * columns + (int)pixel.x / BinSize];
                if (candidates == null) return result;
                Color island = ids == null ? Color.white : ids.GetPixel((int)(pixel.x / width * ids.width), (int)(pixel.y / height * ids.height));
                foreach (int index in candidates)
                {
                    NaturalStrand strand = strands[index];
                    if (ids != null && !SameIsland(island, strand.island)) continue;
                    float reach = Mathf.Max(RootRadius(s), strand.width * .5f +
                        (s.shadowAmount > 0f ? s.shadowSpread + Mathf.Abs(s.shadowOffset) : 0f)) + footprint;
                    if (pixel.x < strand.low.x - reach || pixel.x > strand.high.x + reach ||
                        pixel.y < strand.low.y - reach || pixel.y > strand.high.y + reach) continue;
                    strand.Distance(pixel, out float distance, out float along);
                    float radius = strand.width * .5f;
                    float profile = Mathf.Pow(Mathf.Max(0f, 1f - along), s.taper);
                    float localRadius = radius * (strand.clipped ? Mathf.Lerp(1f, .8f, along) : profile);
                    // Integral of a box filter across a thin strand, preserving subpixel energy.
                    float coverage = StripCoverage(distance, localRadius, footprint);
                    float rootFade = Mathf.Lerp(.55f, 1f, SmoothStep(0f, .12f, along));
                    float opacity = coverage * strand.opacity * rootFade;
                    if (opacity > 0f)
                    {
                        Color color = strand.color * Mathf.Lerp(.85f, 1.12f, along);
                        float combined = opacity + result.coverage * (1f - opacity);
                        result.color = (color * opacity + result.color * (result.coverage * (1f - opacity))) / combined;
                        result.roughness = (strand.roughness * opacity + result.roughness * result.coverage * (1f - opacity)) / combined;
                        result.coverage = combined;
                        // Bound overlap relief by the tallest strand, rather than adding heights.
                        float rounded = Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow(distance / Mathf.Max(localRadius, footprint * .5f), 2)));
                        result.height = Mathf.Max(result.height, s.hairHeight * opacity * rounded);
                    }
                    if (s.shadowAmount > 0f)
                    {
                        strand.Distance(pixel - strand.direction * s.shadowOffset, out float shadowDistance, out _);
                        float shadow = StripCoverage(shadowDistance, radius + s.shadowSpread, footprint)
                            * s.shadowAmount * strand.rootVariation;
                        result.shadow = Mathf.Max(result.shadow, shadow);
                    }
                    float rootDistance = Vector2.Distance(pixel, strand.points[0]);
                    result.redness = Mathf.Max(result.redness, (1f - SmoothStep(0f, s.rednessRadius, rootDistance)) * s.rednessAmount * strand.rootVariation);
                    if (strand.pimple)
                    {
                        float core = Mathf.Clamp01(1f - rootDistance / s.pimpleSize);
                        result.pimpleCore = Mathf.Max(result.pimpleCore, core);
                        result.pimple = Mathf.Max(result.pimple, SmoothStep(0f, .3f, core));
                    }
                    else if (s.follicleDepth > 0f)
                        result.follicle = Mathf.Max(result.follicle, (1f - SmoothStep(0f, s.follicleRadius, rootDistance)) * strand.rootVariation);
                }
                return result;
            }
        }

        private static float StripCoverage(float distance, float radius, float footprint)
        {
            float half = Mathf.Max(.01f, footprint * .5f);
            return Mathf.Clamp01((Mathf.Min(radius, distance + half) - Mathf.Max(-radius, distance - half)) / (2f * half));
        }

        private sealed class NaturalStrand
        {
            public readonly Vector2[] points;
            public readonly Vector2 direction;
            public readonly Vector2 low, high;
            public readonly Color color, island;
            public readonly float width, opacity, roughness, rootVariation;
            public readonly bool clipped, pimple;

            public NaturalStrand(Settings s, int cx, int cy, Vector2 root, Vector2 direction, Color island)
            {
                this.direction = direction; this.island = island;
                bool fine = Hash(cx, cy, s.seed + 727) < s.fineFraction;
                clipped = !fine && Hash(cx, cy, s.seed + 733) < s.clippedFraction;
                float length = s.hairLength * (s.profile == 1 ? .55f : 1f) * (fine ? .6f : 1f)
                    * (1f + SignedHash(cx, cy, s.seed + 31) * s.lengthVariation);
                width = s.hairWidth * (s.profile == 1 ? .8f : 1f) * (fine ? .5f : 1f)
                    * (1f + SignedHash(cx, cy, s.seed + 37) * s.widthVariation);
                opacity = Mathf.Clamp01(s.hairOpacity * (fine ? .65f : 1f) * (1f + SignedHash(cx, cy, s.seed + 739) * s.opacityVariation));
                roughness = Mathf.Clamp01(s.hairRoughness + SignedHash(cx, cy, s.seed + 743) * s.roughnessVariation);
                rootVariation = Mathf.Lerp(.3f, 1f, Hash(cx, cy, s.seed + 751));
                pimple = PimpleAtRoot(s, cx, cy, s.rootSpacing * s.rootSpacing);
                Color baseColor = Hash(cx, cy, s.seed + 757) < s.grayFraction ? s.grayColor : s.hairColor;
                // Relative exposure keeps a dark brown brown. Additive linear RGB crushed
                // nearly half the old default hairs to black and lifted the rest to gray.
                color = baseColor * Mathf.Pow(2f, SignedHash(cx, cy, s.seed + 53) * s.hairColorVariation * 4f) * (fine ? 1.4f : 1f);
                color.a = 1f;
                float bend = SignedHash(cx, cy, s.seed + 43) * s.curvature * length;
                Vector2 across = new(-direction.y, direction.x);
                Vector2 control = root + direction * (length * .5f) + across * (bend * 2f);
                Vector2 end = root + direction * length + across * (bend * .3f);
                int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Abs(bend) / .08f)), 2, 24);
                points = new Vector2[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments, inv = 1f - t;
                    points[i] = root * (inv * inv) + control * (2f * inv * t) + end * (t * t);
                }
                low = high = root;
                foreach (var point in points) { low = Vector2.Min(low, point); high = Vector2.Max(high, point); }
            }

            public void Distance(Vector2 pixel, out float distance, out float along)
            {
                distance = float.MaxValue; along = 0f;
                for (int i = 1; i < points.Length; i++)
                {
                    SegmentDistance(pixel, points[i - 1], points[i], out float d, out float t);
                    if (d >= distance) continue;
                    distance = d; along = (i - 1 + t) / (points.Length - 1);
                }
            }
        }
    }
}
