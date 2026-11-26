using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>
    /// The body's bone weights spread through the space round it: a grid whose cells on the body's skin hold
    /// the body's weights there, and whose other cells are relaxed to the average of their neighbours (Laplace's
    /// equation), coarse grids first. Cloth anywhere in the grid then samples a weight that blends the bones near
    /// it: a hem between the legs gets both thighs. After FITE (Lin et al. 2022), which diffuses SMPL's weights the
    /// same way for loose clothing. Runs on the GPU when it can (UMAWeightField.compute); the CPU path does the same
    /// arithmetic in the same order, so both give the same field.
    /// </summary>
    public static class UMAWeightField
    {
        public const string ShaderName = "UMAWeightField";
        /// <summary>Relaxation passes on the coarsest grid and on each finer one.</summary>
        public const int CoarsestPasses = 400, LevelPasses = 60;
        /// <summary>Channels solved together (one GPU buffer's worth).</summary>
        public const int Batch = 16;
        /// <summary>The finest grid is made coarser until it has at most this many cells.</summary>
        public const int MaxCells = 600000;

        private static ComputeShader cachedShader;
        private static bool searched;

        static UMAWeightField()
        {
            EditorApplication.projectChanged += () => searched = false;
        }

        public static ComputeShader LoadShader()
        {
            if (cachedShader != null) return cachedShader;
            cachedShader = UMAPathUtility.LoadInstallAsset<ComputeShader>("Core/Editor/Shaders/" + ShaderName + ".compute");
            if (cachedShader != null) return cachedShader;
            if (searched) return null;
            searched = true;
            foreach (string guid in AssetDatabase.FindAssets(ShaderName + " t:ComputeShader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/" + ShaderName + ".compute", StringComparison.Ordinal)) continue;
                cachedShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (cachedShader != null) return cachedShader;
            }
            return null;
        }

        /// <summary>True when this machine can run the field on the GPU.</summary>
        public static bool GpuSupported => SystemInfo.supportsComputeShaders && LoadShader() != null;

        /// <summary>One grid of the pyramid.</summary>
        public sealed class Level
        {
            public Vector3 Origin;
            public float Cell;
            public Vector3Int Dims;
            public int Cells => Dims.x * Dims.y * Dims.z;
            public uint[] Fixed;
            /// <summary>The body's weights at each fixed cell.</summary>
            public BoneWeight1[][] FixedWeights;
            public Vector3 Position(int cell)
            {
                int x = cell % Dims.x, y = (cell / Dims.x) % Dims.y, z = cell / (Dims.x * Dims.y);
                return Origin + new Vector3(x, y, z) * Cell;
            }
        }

        /// <summary>The grid pyramid over some bounds, finest first, with the body's cells fixed.</summary>
        public static List<Level> BuildLevels(UMAWeightSurface surface, BoneWeight1[][] donorWeights, Bounds bounds, float cellSize)
        {
            float h = Mathf.Max(0.002f, cellSize);
            Vector3 size = bounds.size;
            while ((Mathf.CeilToInt(size.x / h) + 1L) * (Mathf.CeilToInt(size.y / h) + 1L) * (Mathf.CeilToInt(size.z / h) + 1L) > MaxCells) h *= 1.25f;
            // How many levels: halve until the smallest side has a few cells.
            int count = 1;
            while (count < 8 && Mathf.Min(size.x, Mathf.Min(size.y, size.z)) / (h * (1 << (count - 1))) > 5f) count++;
            // Every level spans the same box, centred on the bounds: each side a whole number of the coarsest cells,
            // so the coarse grids reach no further on one side than the other.
            float coarsest = h * (1 << (count - 1));
            var padded = new Vector3(Mathf.Ceil(size.x / coarsest) * coarsest, Mathf.Ceil(size.y / coarsest) * coarsest, Mathf.Ceil(size.z / coarsest) * coarsest);
            Vector3 origin = bounds.center - padded * 0.5f;
            var levels = new List<Level>();
            for (int l = 0; l < count; l++)
            {
                float cell = h * (1 << l);
                var dims = new Vector3Int(Mathf.RoundToInt(padded.x / cell) + 1, Mathf.RoundToInt(padded.y / cell) + 1, Mathf.RoundToInt(padded.z / cell) + 1);
                levels.Add(new Level { Origin = origin, Cell = cell, Dims = dims });
            }
            foreach (Level level in levels) Classify(level, surface, donorWeights);
            return levels;
        }

        private static void Classify(Level level, UMAWeightSurface surface, BoneWeight1[][] donorWeights)
        {
            int cells = level.Cells;
            level.Fixed = new uint[cells];
            level.FixedWeights = new BoneWeight1[cells][];
            float near = level.Cell * 0.75f;
            Parallel.For(0, level.Dims.z, () => (surface.NewQuery(), new List<BoneWeight1>(16)), (z, state, local) =>
            {
                int slab = level.Dims.x * level.Dims.y;
                for (int k = 0; k < slab; k++)
                {
                    int cell = z * slab + k;
                    // Only cells on the skin hold the body's weights. The band they make shuts the body's inside off
                    // from the outside, so the inside is filled by the solve too (cloth poking deep in samples a blend
                    // of the skin round it), and no cell needs a search further than its own reach.
                    UMAWeightHit hit = surface.Closest(level.Position(cell), local.Item1, near);
                    if (!hit.Found) continue;
                    UMAWeightSurface.WeightsAt(hit, surface, donorWeights, local.Item2, 8);
                    if (local.Item2.Count == 0) continue;
                    level.Fixed[cell] = 1;
                    level.FixedWeights[cell] = local.Item2.ToArray();
                }
                return local;
            }, _ => { });
        }

        /// <summary>The bones any fixed cell holds, in a stable order.</summary>
        public static List<int> Columns(List<Level> levels)
        {
            var set = new SortedSet<int>();
            foreach (Level level in levels)
                foreach (BoneWeight1[] w in level.FixedWeights)
                    if (w != null) foreach (BoneWeight1 bw in w) set.Add(bw.boneIndex);
            return new List<int>(set);
        }

        /// <summary>
        /// Solves the field and samples it at the points: per point, one value per column (point-major). The
        /// columns are bone indices (<see cref="Columns"/>).
        /// </summary>
        public static float[] Solve(List<Level> levels, List<int> columns, Vector3[] points, bool preferGpu, out bool usedGpu, List<string> messages)
        {
            usedGpu = false;
            var result = new float[points.Length * columns.Count];
            bool gpu = preferGpu && GpuSupported;
            for (int start = 0; start < columns.Count; start += Batch)
            {
                int count = Math.Min(Batch, columns.Count - start);
                float[] finest = null;
                if (gpu)
                {
                    try
                    {
                        finest = SolveBatchGpu(levels, columns, start, count);
                        usedGpu = true;
                    }
                    catch (Exception e)
                    {
                        gpu = false;
                        messages?.Add("The weight field could not run on the GPU (" + e.Message + "); it ran on the CPU.");
                    }
                }
                if (finest == null) finest = SolveBatchCpu(levels, columns, start, count);
                Level fine = levels[0];
                Parallel.For(0, points.Length, i => SampleInto(fine, finest, count, points[i], result, i * columns.Count + start));
            }
            return result;
        }

        /// <summary>The fixed cells' values for some columns, the free cells zero.</summary>
        private static float[] InitialValues(Level level, List<int> columns, int start, int count)
        {
            var values = new float[level.Cells * count];
            var column = new Dictionary<int, int>(count);
            for (int c = 0; c < count; c++) column[columns[start + c]] = c;
            for (int cell = 0; cell < level.Cells; cell++)
            {
                BoneWeight1[] w = level.FixedWeights[cell];
                if (w == null) continue;
                foreach (BoneWeight1 bw in w)
                {
                    if (column.TryGetValue(bw.boneIndex, out int c)) values[cell * count + c] = bw.weight;
                }
            }
            return values;
        }

        public static float[] SolveBatchCpu(List<Level> levels, List<int> columns, int start, int count)
        {
            float[] coarse = null;
            Level coarseLevel = null;
            for (int l = levels.Count - 1; l >= 0; l--)
            {
                Level level = levels[l];
                float[] a = InitialValues(level, columns, start, count);
                if (coarse != null) ProlongCpu(level, coarseLevel, coarse, a, count);
                float[] b = (float[])a.Clone();
                int passes = l == levels.Count - 1 ? CoarsestPasses : LevelPasses;
                for (int p = 0; p < passes; p++)
                {
                    RelaxCpu(level, a, b, count);
                    float[] t = a; a = b; b = t;
                }
                coarse = a;
                coarseLevel = level;
            }
            return coarse;
        }

        private static void RelaxCpu(Level level, float[] source, float[] target, int channels)
        {
            Vector3Int d = level.Dims;
            uint[] fixedCells = level.Fixed;
            Parallel.For(0, d.z, z =>
            {
                for (int y = 0; y < d.y; y++)
                    for (int x = 0; x < d.x; x++)
                    {
                        int cell = x + d.x * (y + d.y * z);
                        int xm = Math.Max(x - 1, 0) + d.x * (y + d.y * z);
                        int xp = Math.Min(x + 1, d.x - 1) + d.x * (y + d.y * z);
                        int ym = x + d.x * (Math.Max(y - 1, 0) + d.y * z);
                        int yp = x + d.x * (Math.Min(y + 1, d.y - 1) + d.y * z);
                        int zm = x + d.x * (y + d.y * Math.Max(z - 1, 0));
                        int zp = x + d.x * (y + d.y * Math.Min(z + 1, d.z - 1));
                        bool isFixed = fixedCells[cell] != 0;
                        for (int c = 0; c < channels; c++)
                        {
                            float self = source[cell * channels + c];
                            float sum = source[xm * channels + c] + source[xp * channels + c];
                            sum += source[ym * channels + c] + source[yp * channels + c];
                            sum += source[zm * channels + c] + source[zp * channels + c];
                            float relaxed = sum * (1f / 6f);
                            target[cell * channels + c] = isFixed ? self : relaxed;
                        }
                    }
            });
        }

        private static float CoarseValue(Level coarse, float[] values, int x, int y, int z, int channel, int channels)
        {
            x = Mathf.Clamp(x, 0, coarse.Dims.x - 1); y = Mathf.Clamp(y, 0, coarse.Dims.y - 1); z = Mathf.Clamp(z, 0, coarse.Dims.z - 1);
            return values[(x + coarse.Dims.x * (y + coarse.Dims.y * z)) * channels + channel];
        }

        private static void ProlongCpu(Level fine, Level coarse, float[] coarseValues, float[] target, int channels)
        {
            Vector3Int d = fine.Dims;
            Parallel.For(0, d.z, z =>
            {
                for (int y = 0; y < d.y; y++)
                    for (int x = 0; x < d.x; x++)
                    {
                        int cell = x + d.x * (y + d.y * z);
                        if (fine.Fixed[cell] != 0) continue;
                        int lx = x >> 1, ly = y >> 1, lz = z >> 1;
                        float fx = (x & 1) * 0.5f, fy = (y & 1) * 0.5f, fz = (z & 1) * 0.5f;
                        for (int c = 0; c < channels; c++)
                        {
                            float c000 = CoarseValue(coarse, coarseValues, lx, ly, lz, c, channels);
                            float c100 = CoarseValue(coarse, coarseValues, lx + 1, ly, lz, c, channels);
                            float c010 = CoarseValue(coarse, coarseValues, lx, ly + 1, lz, c, channels);
                            float c110 = CoarseValue(coarse, coarseValues, lx + 1, ly + 1, lz, c, channels);
                            float c001 = CoarseValue(coarse, coarseValues, lx, ly, lz + 1, c, channels);
                            float c101 = CoarseValue(coarse, coarseValues, lx + 1, ly, lz + 1, c, channels);
                            float c011 = CoarseValue(coarse, coarseValues, lx, ly + 1, lz + 1, c, channels);
                            float c111 = CoarseValue(coarse, coarseValues, lx + 1, ly + 1, lz + 1, c, channels);
                            float x00 = c000 + (c100 - c000) * fx;
                            float x10 = c010 + (c110 - c010) * fx;
                            float x01 = c001 + (c101 - c001) * fx;
                            float x11 = c011 + (c111 - c011) * fx;
                            float y0 = x00 + (x10 - x00) * fy;
                            float y1 = x01 + (x11 - x01) * fy;
                            target[cell * channels + c] = y0 + (y1 - y0) * fz;
                        }
                    }
            });
        }

        public static float[] SolveBatchGpu(List<Level> levels, List<int> columns, int start, int count)
        {
            ComputeShader shader = LoadShader();
            if (shader == null) throw new InvalidOperationException("the weight field compute shader is missing");
            int kRelax = shader.FindKernel("CSRelax");
            int kProlong = shader.FindKernel("CSProlong");
            var buffers = new List<ComputeBuffer>();
            try
            {
                ComputeBuffer coarse = null;
                Level coarseLevel = null;
                for (int l = levels.Count - 1; l >= 0; l--)
                {
                    Level level = levels[l];
                    float[] initial = InitialValues(level, columns, start, count);
                    var fixedBuffer = new ComputeBuffer(level.Cells, sizeof(uint)); buffers.Add(fixedBuffer);
                    var a = new ComputeBuffer(level.Cells * count, sizeof(float)); buffers.Add(a);
                    var b = new ComputeBuffer(level.Cells * count, sizeof(float)); buffers.Add(b);
                    fixedBuffer.SetData(level.Fixed);
                    a.SetData(initial);
                    b.SetData(initial);
                    int groups = (level.Cells + 63) / 64;
                    shader.SetInts("_Dims", level.Dims.x, level.Dims.y, level.Dims.z);
                    shader.SetInt("_Channels", count);
                    shader.SetInt("_Cells", level.Cells);
                    if (coarse != null)
                    {
                        shader.SetInts("_CoarseDims", coarseLevel.Dims.x, coarseLevel.Dims.y, coarseLevel.Dims.z);
                        shader.SetBuffer(kProlong, "_Coarse", coarse);
                        shader.SetBuffer(kProlong, "_Fixed", fixedBuffer);
                        shader.SetBuffer(kProlong, "_Target", a);
                        shader.Dispatch(kProlong, groups, 1, 1);
                    }
                    int passes = l == levels.Count - 1 ? CoarsestPasses : LevelPasses;
                    shader.SetBuffer(kRelax, "_Fixed", fixedBuffer);
                    for (int p = 0; p < passes; p++)
                    {
                        shader.SetBuffer(kRelax, "_Source", a);
                        shader.SetBuffer(kRelax, "_Target", b);
                        shader.Dispatch(kRelax, groups, 1, 1);
                        ComputeBuffer t = a; a = b; b = t;
                    }
                    coarse = a;
                    coarseLevel = level;
                }
                var finest = new float[levels[0].Cells * count];
                coarse.GetData(finest);
                return finest;
            }
            finally
            {
                foreach (ComputeBuffer buffer in buffers) buffer.Release();
            }
        }

        /// <summary>Trilinear sample of a level's values at a point, written to result[offset..offset+channels).</summary>
        public static void SampleInto(Level level, float[] values, int channels, Vector3 point, float[] result, int offset)
        {
            Vector3 q = (point - level.Origin) / level.Cell;
            q.x = Mathf.Clamp(q.x, 0f, level.Dims.x - 1); q.y = Mathf.Clamp(q.y, 0f, level.Dims.y - 1); q.z = Mathf.Clamp(q.z, 0f, level.Dims.z - 1);
            int x0 = Mathf.Min(Mathf.FloorToInt(q.x), level.Dims.x - 1), y0 = Mathf.Min(Mathf.FloorToInt(q.y), level.Dims.y - 1), z0 = Mathf.Min(Mathf.FloorToInt(q.z), level.Dims.z - 1);
            int x1 = Mathf.Min(x0 + 1, level.Dims.x - 1), y1 = Mathf.Min(y0 + 1, level.Dims.y - 1), z1 = Mathf.Min(z0 + 1, level.Dims.z - 1);
            float fx = q.x - x0, fy = q.y - y0, fz = q.z - z0;
            Vector3Int d = level.Dims;
            int I(int x, int y, int z) => (x + d.x * (y + d.y * z)) * channels;
            int i000 = I(x0, y0, z0), i100 = I(x1, y0, z0), i010 = I(x0, y1, z0), i110 = I(x1, y1, z0);
            int i001 = I(x0, y0, z1), i101 = I(x1, y0, z1), i011 = I(x0, y1, z1), i111 = I(x1, y1, z1);
            for (int c = 0; c < channels; c++)
            {
                float a = Mathf.Lerp(values[i000 + c], values[i100 + c], fx);
                float b = Mathf.Lerp(values[i010 + c], values[i110 + c], fx);
                float e = Mathf.Lerp(values[i001 + c], values[i101 + c], fx);
                float f = Mathf.Lerp(values[i011 + c], values[i111 + c], fx);
                result[offset + c] = Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(e, f, fy), fz);
            }
        }
    }
}
