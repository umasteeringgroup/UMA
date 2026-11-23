using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace UMA.Editors
{
    /// <summary>
    /// The hand-editable sources of a wrinkle map: wrinkle height in millimetres, crease depth and flush, one value per
    /// texel, row 0 at the top of the texture. <see cref="Pack"/> turns them into the wrinkle map the shader reads. Each
    /// texel's normal is taken along its own tangent frame, as the procedural generator does, so a painted height reads
    /// the same on every UV island, mirrored or not.
    /// Pure C#: no UnityEngine types.
    /// </summary>
    public static class UMAWrinkleSourceMaps
    {
        /// <summary>Height range of the height source: black is -HeightRangeMM, mid gray flat, white +HeightRangeMM.</summary>
        public const float HeightRangeMM = 2.5f;

        public static float HeightToGray(float heightMM) { return Clamp01(0.5f + heightMM / (2f * HeightRangeMM)); }
        public static float GrayToHeight(float gray) { return (gray - 0.5f) * 2f * HeightRangeMM; }

        /// <summary>
        /// Builds the RGBA wrinkle map (0..1 per component, row 0 at the top): RG the wrinkle normal from the height's
        /// slope, B the crease, A the flush. Padding texels outside the UV islands copy their nearest covered texel,
        /// like the procedural bake, so mipmaps and the atlas see no edge.
        /// </summary>
        /// <param name="clampedSlopes">Covered texels whose slope was steeper than the map can store.</param>
        public static float[] Pack(UMAWrinkleSurfaceMap map, float[] heightMM, float[] crease, float[] flush, out int clampedSlopes)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int size = map.Size, n = size * size;
            if (heightMM == null || heightMM.Length != n) throw new ArgumentException("The height source must have one value per texel.", nameof(heightMM));
            if (crease == null || crease.Length != n) throw new ArgumentException("The crease source must have one value per texel.", nameof(crease));
            if (flush == null || flush.Length != n) throw new ArgumentException("The flush source must have one value per texel.", nameof(flush));

            var result = new float[n * 4];
            for (int i = 0; i < n; i++)
            {
                result[i * 4] = 0.5f; result[i * 4 + 1] = 0.5f;
                result[i * 4 + 2] = Clamp01(crease[i]);
                result[i * 4 + 3] = Clamp01(flush[i]);
            }
            clampedSlopes = AddSlopes(map, BuildSlopeFrame(map), heightMM, result);
            // Crease and flush padding follows the nearest covered texel too (hand-edited sources rarely paint it).
            for (int i = 0; i < n; i++)
            {
                int s = map.Source[i];
                if (map.Covered[i] || s < 0) continue;
                result[i * 4 + 2] = result[s * 4 + 2];
                result[i * 4 + 3] = result[s * 4 + 3];
            }
            return result;
        }

        /// <summary>
        /// Adds the slope of a height change (mm per texel, row 0 at the top) to the RG of an existing wrinkle map
        /// (RGBA 0..1, row 0 at the top), in place. A zero change leaves the map exactly as it was, so painting only a
        /// part of a map never alters the rest. B and A are not touched. Padding texels copy their nearest covered texel
        /// where that texel changed.
        /// This is the reference for the GPU version in Overlay Painter (ChannelPack.compute, CSApplyWrinkleHeight).
        /// </summary>
        /// <returns>Covered texels whose total slope was steeper than the map can store.</returns>
        public static int AddSlopes(UMAWrinkleSurfaceMap map, float[] frame, float[] heightDeltaMM, float[] wrinkleRGBA)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int size = map.Size, n = size * size;
            if (frame == null || frame.Length != n * 4) throw new ArgumentException("The slope frame must have four values per texel.", nameof(frame));
            if (heightDeltaMM == null || heightDeltaMM.Length != n) throw new ArgumentException("The height change must have one value per texel.", nameof(heightDeltaMM));
            if (wrinkleRGBA == null || wrinkleRGBA.Length != n * 4) throw new ArgumentException("The wrinkle map must have four values per texel.", nameof(wrinkleRGBA));
            int clamped = 0;
            var changed = new bool[n];
            Parallel.For(0, size, () => 0, (y, state, local) =>
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    if (!map.Covered[i]) continue;
                    // Height change per texel to the right and per texel down, from covered neighbours only.
                    double gx = Derivative(map, heightDeltaMM, x, y, 1, 0), gy = Derivative(map, heightDeltaMM, x, y, 0, 1);
                    if (gx == 0 && gy == 0) continue;
                    changed[i] = true;
                    DecodeSlope(wrinkleRGBA[i * 4], wrinkleRGBA[i * 4 + 1], out float sx, out float sy);
                    sx += (float)(frame[i * 4] * gx + frame[i * 4 + 1] * gy);
                    sy += (float)(frame[i * 4 + 2] * gx + frame[i * 4 + 3] * gy);
                    if (EncodeSlope(sx, sy, out wrinkleRGBA[i * 4], out wrinkleRGBA[i * 4 + 1])) local++;
                }
                return local;
            }, local => Interlocked.Add(ref clamped, local));
            // Padding texels follow their covered texel only where it changed, so an untouched map stays exactly as it was.
            for (int i = 0; i < n; i++)
            {
                int s = map.Source[i];
                if (map.Covered[i] || s < 0 || !changed[s]) continue;
                wrinkleRGBA[i * 4] = wrinkleRGBA[s * 4];
                wrinkleRGBA[i * 4 + 1] = wrinkleRGBA[s * 4 + 1];
            }
            return clamped;
        }

        /// <summary>
        /// Per texel (row 0 at the top), how the height change across one texel turns into the wrinkle slope:
        /// slope along the tangent = f0 * gx + f1 * gy and along the bitangent = f2 * gx + f3 * gy, where gx and gy are
        /// the height changes in mm for one texel to the right and one texel down. 0 where the texel is not covered.
        /// </summary>
        public static float[] BuildSlopeFrame(UMAWrinkleSurfaceMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int n = map.Size * map.Size;
            var frame = new float[n * 4];
            Parallel.For(0, n, i =>
            {
                if (!map.Covered[i]) return;
                Vector3 r = map.TexelRight[i], d = map.TexelDown[i];
                double rr = Vector3.Dot(r, r), rd = Vector3.Dot(r, d), dd = Vector3.Dot(d, d), det = rr * dd - rd * rd;
                if (det <= 0) return;
                // Write each frame axis as a texel offset, a * right + b * down; mm per mesh unit to mm per mm.
                for (int axis = 0; axis < 2; axis++)
                {
                    Vector3 t = axis == 0 ? map.Tangent[i] : map.Bitangent[i];
                    double rt = Vector3.Dot(r, t), dt = Vector3.Dot(d, t);
                    frame[i * 4 + axis * 2] = (float)((dd * rt - rd * dt) / det / 1000.0);
                    frame[i * 4 + axis * 2 + 1] = (float)((rr * dt - rd * rt) / det / 1000.0);
                }
            });
            return frame;
        }

        /// <summary>The slope stored in a wrinkle map's red and green (0.5 = flat).</summary>
        public static void DecodeSlope(float red, float green, out float slopeX, out float slopeY)
        {
            float x = red * 2f - 1f, y = green * 2f - 1f;
            float z = (float)Math.Sqrt(Math.Max(1e-8f, 1f - x * x - y * y));
            slopeX = -x / z; slopeY = -y / z;
        }

        /// <summary>Stores a slope as the wrinkle map's red and green. Returns true when it had to be clamped.</summary>
        public static bool EncodeSlope(float slopeX, float slopeY, out float red, out float green)
        {
            float max = UMAWrinkleProceduralGenerator.MaxSlope;
            bool clamped = Math.Abs(slopeX) > max || Math.Abs(slopeY) > max;
            slopeX = Math.Max(-max, Math.Min(max, slopeX)); slopeY = Math.Max(-max, Math.Min(max, slopeY));
            var normal = Vector3.Normalize(new Vector3(-slopeX, -slopeY, 1));
            red = normal.X * 0.5f + 0.5f; green = normal.Y * 0.5f + 0.5f;
            return clamped;
        }

        // Central difference where both neighbours are on the mesh, one-sided at an island edge, 0 on a lone texel.
        static double Derivative(UMAWrinkleSurfaceMap map, float[] height, int x, int y, int dx, int dy)
        {
            int size = map.Size, i = y * size + x;
            int xp = x + dx, yp = y + dy, xm = x - dx, ym = y - dy;
            bool plus = xp < size && yp < size && map.Covered[yp * size + xp];
            bool minus = xm >= 0 && ym >= 0 && map.Covered[ym * size + xm];
            if (plus && minus) return (height[yp * size + xp] - height[ym * size + xm]) * 0.5;
            if (plus) return height[yp * size + xp] - height[i];
            if (minus) return height[i] - height[ym * size + xm];
            return 0;
        }

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    }
}
