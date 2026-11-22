using System;
using System.Collections.Generic;

namespace UMA.Editors
{
    /// <summary>
    /// Packs one grayscale mask per <see cref="UMAWrinkleZone"/> into the RGBA control map of the
    /// "UMA/URP/Realistic Skin SSS Wrinkles" shader: R zone id of layer A, G mask of layer A, B zone id of layer B,
    /// A mask of layer B. Ids are stored as id * 17.
    /// <para>
    /// Every zone is given one layer for the whole map, so an id never jumps between layers where a mask is not zero.
    /// Zones that overlap get different layers (a two-colouring of the overlap graph). Each layer's id is then spread
    /// into the empty texels around its zones, so ids only change in the middle of a gap where both masks are zero, and
    /// bilinear filtering, mipmaps and atlas scaling only blend ids where nothing is shown. <see cref="Report"/> says
    /// where that does not hold.
    /// </para>
    /// Pure C#: no UnityEngine types, so the same code runs in an offline tool.
    /// </summary>
    public static class UMAWrinkleControlMapPacker
    {
        /// <summary>Masks at or below this value count as empty.</summary>
        public const float EmptyMask = 0.5f / 255f;

        /// <summary>
        /// Zone pairs that may touch on one layer without an empty gap: the two forehead halves, whose lines run
        /// through the midline. Their ids are consecutive, so a filtered id between them can only decode to one of the
        /// two, and either one is a forehead weight.
        /// </summary>
        public static readonly int[][] TouchingPairs = { new[] { (int)UMAWrinkleZone.ForeheadLeft, (int)UMAWrinkleZone.ForeheadRight } };

        static bool MayTouch(int a, int b)
        {
            foreach (var p in TouchingPairs)
                if ((p[0] == a && p[1] == b) || (p[0] == b && p[1] == a)) return Math.Abs(a - b) == 1;
            return false;
        }

        static bool Differs(int a, int b) { return a != b && !MayTouch(a, b); }

        public sealed class Report
        {
            /// <summary>Layer of each zone id: -1 not present, 0 layer A (red/green), 1 layer B (blue/alpha).</summary>
            public int[] Layer = new int[UMAWrinkleZones.IdLevels];
            /// <summary>Texels where three or more zones overlap, or two zones of one layer; the weakest were dropped.</summary>
            public int ConflictTexels;
            /// <summary>Per layer, the smallest distance in texels from a texel with a mask to a texel with another id.</summary>
            public float[] MinimumGap = { float.PositiveInfinity, float.PositiveInfinity };
            /// <summary>Pairs of zones that overlap, as "a+b".</summary>
            public List<string> Overlaps = new List<string>();
            public List<string> Problems = new List<string>();
            public bool Ok { get { return Problems.Count == 0; } }

            public override string ToString()
            {
                var sb = new System.Text.StringBuilder();
                for (int id = 1; id < Layer.Length; id++)
                    if (Layer[id] >= 0) sb.Append((UMAWrinkleZone)id).Append(Layer[id] == 0 ? "=A " : "=B ");
                sb.Append("| overlaps ").Append(string.Join(", ", Overlaps));
                sb.Append(" | gap A ").Append(MinimumGap[0].ToString("0.#")).Append(" B ").Append(MinimumGap[1].ToString("0.#"));
                sb.Append(" | conflicts ").Append(ConflictTexels);
                foreach (var p in Problems) sb.Append("\n  problem: ").Append(p);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Packs the masks. <paramref name="zoneMasks"/> is indexed by zone id (entry 0 and missing zones may be null);
        /// each mask has width * height values in 0..1, row-major. Returns width * height * 4 bytes in the same order.
        /// </summary>
        /// <param name="requiredGap">Smallest allowed distance, in texels, from a masked texel to a texel of another
        /// id in the same layer. Each mip level halves it.</param>
        public static byte[] Pack(int width, int height, float[][] zoneMasks, out Report report, float requiredGap = 4f)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (zoneMasks == null || zoneMasks.Length > UMAWrinkleZones.IdLevels) throw new ArgumentException("One mask per zone id, at most 16.", nameof(zoneMasks));
            int n = width * height;
            report = new Report();
            for (int id = 0; id < report.Layer.Length; id++) report.Layer[id] = -1;
            int zones = zoneMasks.Length;
            for (int id = 1; id < zones; id++)
                if (zoneMasks[id] != null && zoneMasks[id].Length != n) throw new ArgumentException("Mask " + id + " has the wrong size.", nameof(zoneMasks));

            // Which zones are present, and which overlap.
            var present = new bool[UMAWrinkleZones.IdLevels];
            var overlap = new bool[UMAWrinkleZones.IdLevels, UMAWrinkleZones.IdLevels];
            var active = new int[UMAWrinkleZones.IdLevels];
            int crowded = 0;
            for (int i = 0; i < n; i++)
            {
                int count = 0;
                for (int id = 1; id < zones; id++)
                    if (zoneMasks[id] != null && zoneMasks[id][i] > EmptyMask) active[count++] = id;
                for (int a = 0; a < count; a++)
                {
                    present[active[a]] = true;
                    for (int b = a + 1; b < count; b++) { overlap[active[a], active[b]] = true; overlap[active[b], active[a]] = true; }
                }
                if (count > 2) crowded++;
            }
            for (int a = 1; a < UMAWrinkleZones.IdLevels; a++)
                for (int b = a + 1; b < UMAWrinkleZones.IdLevels; b++)
                    if (overlap[a, b]) report.Overlaps.Add((UMAWrinkleZone)a + "+" + (UMAWrinkleZone)b);

            AssignLayers(present, overlap, report);

            // Strongest zone of each layer per texel.
            var bytes = new byte[n * 4];
            var hasMask = new[] { new bool[n], new bool[n] };
            var ids = new[] { new int[n], new int[n] };
            int sameLayer = 0;
            for (int i = 0; i < n; i++)
            {
                float bestA = 0, bestB = 0; int idA = 0, idB = 0; bool clash = false;
                for (int id = 1; id < zones; id++)
                {
                    if (zoneMasks[id] == null) continue;
                    float m = zoneMasks[id][i];
                    if (m <= EmptyMask) continue;
                    if (report.Layer[id] == 0) { if (idA != 0) clash = true; if (m > bestA) { bestA = m; idA = id; } }
                    else { if (idB != 0) clash = true; if (m > bestB) { bestB = m; idB = id; } }
                }
                if (clash) sameLayer++;
                byte ma = ToByte(bestA), mb = ToByte(bestB);
                bytes[i * 4 + 1] = ma; bytes[i * 4 + 3] = mb;
                if (ma > 0) { hasMask[0][i] = true; ids[0][i] = idA; }
                if (mb > 0) { hasMask[1][i] = true; ids[1][i] = idB; }
            }
            report.ConflictTexels = Math.Max(crowded, sameLayer);
            if (crowded > 0) report.Problems.Add(crowded + " texels have three or more zones; only two can be stored.");
            if (sameLayer > 0) report.Problems.Add(sameLayer + " texels have two zones of the same layer.");

            // Spread ids into the empty texels, then measure how close masked texels come to an id change.
            for (int layer = 0; layer < 2; layer++)
            {
                var nearest = new int[n];
                var distance = new float[n];
                UMAWrinkleDistanceField.NearestSource(width, height, hasMask[layer], nearest, distance);
                int channel = layer * 2;
                for (int i = 0; i < n; i++)
                {
                    int id = nearest[i] >= 0 ? ids[layer][nearest[i]] : 0;
                    ids[layer][i] = id;
                    bytes[i * 4 + channel] = UMAWrinkleZones.EncodeId(id);
                }
                var boundary = new bool[n];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x, id = ids[layer][i];
                        var row = ids[layer];
                        boundary[i] = (x > 0 && Differs(row[i - 1], id)) || (x < width - 1 && Differs(row[i + 1], id))
                            || (y > 0 && Differs(row[i - width], id)) || (y < height - 1 && Differs(row[i + width], id));
                    }
                var toBoundary = new float[n];
                UMAWrinkleDistanceField.NearestSource(width, height, boundary, null, toBoundary);
                float gap = float.PositiveInfinity;
                for (int i = 0; i < n; i++) if (hasMask[layer][i]) gap = Math.Min(gap, toBoundary[i]);
                report.MinimumGap[layer] = gap;
                if (gap < requiredGap)
                    report.Problems.Add("Layer " + (layer == 0 ? "A" : "B") + ": a masked texel is " + gap.ToString("0.#")
                        + " texels from another zone's id (at least " + requiredGap + " needed). Leave a wider empty gap between zones of the same layer.");
            }
            return bytes;
        }

        static byte ToByte(float v)
        {
            int b = (int)Math.Round(v * 255f);
            return (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
        }

        // Two-colouring of the overlap graph by breadth-first search. Zones without overlaps go to layer A.
        static void AssignLayers(bool[] present, bool[,] overlap, Report report)
        {
            int count = present.Length;
            var queue = new Queue<int>();
            for (int start = 1; start < count; start++)
            {
                if (!present[start] || report.Layer[start] >= 0) continue;
                report.Layer[start] = 0;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int a = queue.Dequeue();
                    for (int b = 1; b < count; b++)
                    {
                        if (!overlap[a, b]) continue;
                        if (report.Layer[b] < 0) { report.Layer[b] = 1 - report.Layer[a]; queue.Enqueue(b); }
                        else if (report.Layer[b] == report.Layer[a])
                            report.Problems.Add((UMAWrinkleZone)a + " and " + (UMAWrinkleZone)b
                                + " overlap but must share a layer (overlapping zones form an odd cycle). Separate two of them.");
                    }
                }
            }
        }
    }

    /// <summary>Distance transforms on a texel grid (two-pass chamfer, 1 and sqrt 2).</summary>
    public static class UMAWrinkleDistanceField
    {
        /// <summary>
        /// For every texel, the index of the nearest source texel (-1 when there is none) and the distance to it in
        /// texels. <paramref name="nearest"/> may be null.
        /// </summary>
        public static void NearestSource(int width, int height, bool[] source, int[] nearest, float[] distance)
        {
            int n = width * height;
            var src = nearest ?? new int[n];
            for (int i = 0; i < n; i++) { distance[i] = source[i] ? 0 : float.PositiveInfinity; src[i] = source[i] ? i : -1; }
            const float a = 1f, b = 1.41421356f;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (x > 0) Relax(distance, src, i, i - 1, a);
                    if (y > 0)
                    {
                        Relax(distance, src, i, i - width, a);
                        if (x > 0) Relax(distance, src, i, i - width - 1, b);
                        if (x < width - 1) Relax(distance, src, i, i - width + 1, b);
                    }
                }
            for (int y = height - 1; y >= 0; y--)
                for (int x = width - 1; x >= 0; x--)
                {
                    int i = y * width + x;
                    if (x < width - 1) Relax(distance, src, i, i + 1, a);
                    if (y < height - 1)
                    {
                        Relax(distance, src, i, i + width, a);
                        if (x < width - 1) Relax(distance, src, i, i + width + 1, b);
                        if (x > 0) Relax(distance, src, i, i + width - 1, b);
                    }
                }
        }

        static void Relax(float[] distance, int[] src, int i, int j, float step)
        {
            float d = distance[j] + step;
            if (d < distance[i]) { distance[i] = d; src[i] = src[j]; }
        }
    }
}
