using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    internal static partial class StubbleMakerGeneratorEngine
    {
        // Solve the local surface metric rather than treating a world vector as a UV vector.
        // Nearest texels and one-sided differences prevent interpolation across island borders.
        internal static bool TryWorldDirection(TexturePaintReadOnlyMeshMap position,
            TexturePaintReadOnlyMeshMap normal, TexturePaintReadOnlyMeshMap ids,
            Vector2 uv, int outputWidth, int outputHeight, float degrees, out Vector2 direction)
        {
            direction = Vector2.down;
            if (position == null || normal == null || ids == null ||
                uv.x < 0 || uv.x >= 1 || uv.y < 0 || uv.y >= 1) return false;
            int x = Mathf.FloorToInt(uv.x * position.width);
            int y = Mathf.FloorToInt(uv.y * position.height);
            Color id = ids.GetPixel(x, y), p = position.GetPixel(x, y);
            if (id.a < .5f || p.a < .5f) return false;

            Vector3 Derivative(int dx, int dy)
            {
                Color lo = position.GetPixel(x - dx, y - dy);
                Color hi = position.GetPixel(x + dx, y + dy);
                bool hasLo = lo.a >= .5f && SameIsland(id, ids.GetPixel(x - dx, y - dy));
                bool hasHi = hi.a >= .5f && SameIsland(id, ids.GetPixel(x + dx, y + dy));
                if (!hasLo && !hasHi) return Vector3.zero;
                Color delta = (hasHi ? hi : p) - (hasLo ? lo : p);
                float scale = (dx != 0 ? position.width / (float)outputWidth
                    : position.height / (float)outputHeight) / (hasLo && hasHi ? 2f : 1f);
                return new Vector3(delta.r, delta.g, delta.b) * scale;
            }

            Vector3 du = Derivative(1, 0), dv = Derivative(0, 1);
            Color encoded = normal.GetPixel(x, y);
            Vector3 n = new Vector3(encoded.r * 2 - 1, encoded.g * 2 - 1, encoded.b * 2 - 1).normalized;
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, n);
            // Gravity has no tangent at a horizontal surface. Use an explicit world axis,
            // never the arbitrary orientation or handedness of its UV island.
            if (down.sqrMagnitude < .000001f)
                down = Vector3.ProjectOnPlane(Vector3.forward, n);
            down = Quaternion.AngleAxis(degrees, n) * down.normalized;
            float uu = Vector3.Dot(du, du), vv = Vector3.Dot(dv, dv), uvDot = Vector3.Dot(du, dv);
            float determinant = uu * vv - uvDot * uvDot;
            if (uu <= 0 || vv <= 0 || determinant <= uu * vv * .000001f) return false;
            float a = Vector3.Dot(du, down), b = Vector3.Dot(dv, down);
            direction = new Vector2((vv * a - uvDot * b) / determinant,
                (uu * b - uvDot * a) / determinant).normalized;
            return direction.sqrMagnitude > .5f;
        }

        private static bool SameIsland(Color a, Color b) => a.a >= .5f && b.a >= .5f &&
            Mathf.Abs(a.g - b.g) < .1f && Mathf.Abs(a.b - b.b) < .1f;

        private readonly struct WorldStrand
        {
            public readonly Vector2 root, down;
            public readonly float length, width, bend, colorVariation;
            public readonly Color island;
            public readonly bool pimple;

            public WorldStrand(Settings s, int cx, int cy, Vector2 root, Vector2 down, Color island, float cellArea)
            {
                pimple = PimpleAtRoot(s, cx, cy, cellArea);
                this.root = root; this.down = down; this.island = island;
                length = Mathf.Max(.5f, s.hairLength * (s.profile == 1 ? .55f : 1f) *
                    (1 + SignedHash(cx, cy, s.seed + 31) * s.lengthVariation));
                width = Mathf.Max(.2f, s.hairWidth * (s.profile == 1 ? .8f : 1f) *
                    (1 + SignedHash(cx, cy, s.seed + 37) * s.widthVariation));
                bend = SignedHash(cx, cy, s.seed + 43) * s.curvature *
                    (s.profile == 1 ? .6f : 1f) * length;
                colorVariation = SignedHash(cx, cy, s.seed + 53) * s.hairColorVariation;
            }
        }

        private sealed class WorldHairField
        {
            private const int BinSize = 16;
            private readonly List<WorldStrand> strands = new();
            private readonly List<int>[] bins;
            private readonly int columns, rows, width, height;
            private readonly TexturePaintReadOnlyMeshMap ids;

            public WorldHairField(Settings s, TexturePaintCommandContextV2 context,
                string surfaceId, int width, int height)
            {
                this.width = width; this.height = height;
                columns = (width + BinSize - 1) / BinSize;
                rows = (height + BinSize - 1) / BinSize;
                bins = new List<int>[columns * rows];
                var position = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldPosition);
                var normal = context.GetMeshMap(surfaceId, TexturePaintMeshMap.WorldNormal);
                ids = context.GetMeshMap(surfaceId, TexturePaintMeshMap.SurfaceId);
                if (position == null || normal == null || ids == null)
                    throw new InvalidOperationException("Stubble Maker World Down requires mesh position, normal and island maps. Use UV Direction for a surface without mesh maps.");

                float density = s.profile == 1 ? Mathf.Clamp01(s.density + .15f) : s.density;
                float sx = Mathf.Max(s.hairWidth * (s.profile == 1 ? .8f : 1f) * 2.5f,
                    Mathf.Lerp(20, 3, density));
                float sy = Mathf.Max(s.hairLength * (s.profile == 1 ? .55f : 1f) * 1.15f,
                    Mathf.Lerp(28, 5, density));
                int minX = Mathf.FloorToInt(-s.randomPositionX / sx) - 1;
                int maxX = Mathf.CeilToInt((width + s.randomPositionX) / sx);
                int minY = Mathf.FloorToInt(-s.randomPositionY / sy) - 1;
                int maxY = Mathf.CeilToInt((height + s.randomPositionY) / sy);
                for (int cy = minY; cy <= maxY; cy++)
                {
                    context.cancellationToken.ThrowIfCancellationRequested();
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        if (Hash(cx, cy, s.seed + 3) > density) continue;
                        var root = new Vector2((cx + .5f) * sx + SignedHash(cx, cy, s.seed + 11) * s.randomPositionX,
                            (cy + .5f) * sy + SignedHash(cx, cy, s.seed + 17) * s.randomPositionY);
                        var uv = new Vector2(root.x / width, root.y / height);
                        float degrees = s.directionDegrees + SignedHash(cx, cy, s.seed + 23) *
                            s.directionVariation * (s.profile == 1 ? .65f : 1f);
                        if (!TryWorldDirection(position, normal, ids, uv, width, height, degrees, out Vector2 down)) continue;
                        Color island = ids.GetPixel(Mathf.FloorToInt(uv.x * ids.width), Mathf.FloorToInt(uv.y * ids.height));
                        var strand = new WorldStrand(s, cx, cy, root, down, island, sx * sy);
                        Vector2 mid = root + down * (strand.length * .5f) +
                            new Vector2(-down.y, down.x) * strand.bend;
                        Vector2 end = root + down * strand.length;
                        float padding = Mathf.Max(RootRadius(s),
                            strand.width * .5f + .75f + s.shadowSpread + Mathf.Abs(s.shadowOffset));
                        Vector2 low = Vector2.Min(root, Vector2.Min(mid, end)) - Vector2.one * padding;
                        Vector2 high = Vector2.Max(root, Vector2.Max(mid, end)) + Vector2.one * padding;
                        int index = strands.Count;
                        strands.Add(strand);
                        for (int by = Mathf.Max(0, Mathf.FloorToInt(low.y / BinSize)); by <= Mathf.Min(rows - 1, Mathf.FloorToInt(high.y / BinSize)); by++)
                            for (int bx = Mathf.Max(0, Mathf.FloorToInt(low.x / BinSize)); bx <= Mathf.Min(columns - 1, Mathf.FloorToInt(high.x / BinSize)); bx++)
                                (bins[by * columns + bx] ??= new List<int>()).Add(index);
                    }
                }
            }

            public HairSample Sample(Settings s, Vector2 pixel)
            {
                var best = new HairSample();
                var candidates = bins[(int)pixel.y / BinSize * columns + (int)pixel.x / BinSize];
                if (candidates == null) return best;
                Color island = ids.GetPixel((int)(pixel.x / width * ids.width), (int)(pixel.y / height * ids.height));
                foreach (int index in candidates)
                {
                    WorldStrand strand = strands[index];
                    if (!SameIsland(island, strand.island)) continue;
                    AccumulateHair(s, pixel, strand.root, strand.down, strand.down,
                        strand.length, strand.width, strand.bend, strand.colorVariation, strand.pimple, ref best);
                }
                return best;
            }
        }
    }
}
