using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The GPU cloth solver: <c>ClothesDesignerClothSolver.compute</c>, dispatched colour by colour.
    /// Positions come back by asynchronous readback for the preview, and by a bounded synchronous read
    /// when a definite state is needed (Undo, Keep Drape, bake).
    /// </summary>
    public sealed class ClothSolverGpu : IClothSolver
    {
        private const string ShaderName = "ClothesDesignerClothSolver";
        private ComputeShader shader;
        private int kPredict, kSolve, kPins, kCollide, kVelocity, kCandidates, kFrame;
        private ComputeBuffer x, prev, v, w, fric, constraints, pins, colorRanges;
        private ComputeBuffer ta, tb, tc, tn, cellStart, cellTris, candidates;
        private int2[] colors;
        private int count, pinCount;
        private Vector3 gridOrigin;
        private float cellSize;
        private Vector3Int dims;
        private int readbackGeneration;
        private bool readbackPending;

        public string Name => "GPU";
        public bool IsGpu => true;

        public long GpuBytes
        {
            get
            {
                long total = 0;
                foreach (ComputeBuffer b in new[] { x, prev, v, w, fric, constraints, pins, colorRanges, ta, tb, tc, tn, cellStart, cellTris, candidates })
                {
                    if (b != null && b.IsValid()) total += (long)b.count * b.stride;
                }
                return total;
            }
        }

        /// <summary>
        /// Cloth up to this many points runs each frame as one dispatch of one workgroup (CSFrame); larger
        /// cloth runs a dispatch per stage across the whole GPU. A frame is hundreds of tiny stages (one per
        /// constraint colour per substep), and for small and medium cloth launching them costs more than
        /// their work.
        /// </summary>
        public static int SingleGroupMaxParticles = 2500;

        /// <summary>True when the last frames ran as one dispatch each.</summary>
        public bool SingleGroup { get; private set; }

        /// <summary>True when this machine can run the GPU solver.</summary>
        public static bool Supported => SystemInfo.supportsComputeShaders && LoadShader() != null;

        // The designer's panel asks Supported on every GUI event. A project search costs about 45 ms,
        // so the shader is looked up once and searched for again only after the project changes.
        private static ComputeShader cachedShader;
        private static string cachedShaderPath;
        private static bool searched;

        static ClothSolverGpu()
        {
            EditorApplication.projectChanged += () => searched = false;
        }

        public static ComputeShader LoadShader()
        {
            if (cachedShader != null) return cachedShader;
            if (cachedShaderPath != null)
            {
                // Unloaded since (a character build unloads unused assets): loading by path is cheap.
                cachedShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(cachedShaderPath);
                if (cachedShader != null) return cachedShader;
            }
            if (searched) return null;
            searched = true;
            cachedShaderPath = null;
            foreach (string guid in AssetDatabase.FindAssets(ShaderName + " t:ComputeShader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/" + ShaderName + ".compute", StringComparison.Ordinal)) continue;
                cachedShaderPath = path;
                cachedShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                return cachedShader;
            }
            return null;
        }

        public void Initialize(ClothModel model, BodyCollider body, Vector3[] positions)
        {
            Dispose();
            shader = LoadShader();
            if (shader == null) throw new InvalidOperationException("The Clothes Designer's cloth compute shader is missing. Reinstall the Clothes Designer.");
            kPredict = shader.FindKernel("CSPredict");
            kSolve = shader.FindKernel("CSSolve");
            kPins = shader.FindKernel("CSPins");
            kCollide = shader.FindKernel("CSCollide");
            kVelocity = shader.FindKernel("CSVelocity");
            kCandidates = shader.FindKernel("CSCandidates");
            kFrame = shader.FindKernel("CSFrame");
            count = model.ParticleCount;
            x = new ComputeBuffer(count, 12);
            prev = new ComputeBuffer(count, 12);
            v = new ComputeBuffer(count, 12);
            w = new ComputeBuffer(count, 4);
            candidates = new ComputeBuffer(count * ClothSolverShared.Candidates, 4);
            w.SetData(model.InverseMass);
            fric = new ComputeBuffer(count, 4);
            fric.SetData(model.Friction);
            // a, b, rest, kind, compliance: 20 bytes, the layout of ClothConstraint and the shader's Constraint.
            constraints = new ComputeBuffer(Math.Max(1, model.Constraints.Length), 20);
            if (model.Constraints.Length > 0) constraints.SetData(model.Constraints);
            colors = model.Colors;
            colorRanges = new ComputeBuffer(Math.Max(1, colors.Length), 8);
            if (colors.Length > 0) colorRanges.SetData(colors);
            pins = new ComputeBuffer(1, 20);
            pinCount = 0;
            SetPositions(positions);
            SetBody(body);
        }

        public void SetBody(BodyCollider body)
        {
            foreach (ComputeBuffer b in new[] { ta, tb, tc, tn, cellStart, cellTris }) b?.Release();
            int t = Math.Max(1, body.TriangleCount);
            ta = new ComputeBuffer(t, 12); tb = new ComputeBuffer(t, 12); tc = new ComputeBuffer(t, 12); tn = new ComputeBuffer(t, 12);
            if (body.TriangleCount > 0) { ta.SetData(body.A); tb.SetData(body.B); tc.SetData(body.C); tn.SetData(body.N); }
            cellStart = new ComputeBuffer(body.CellStart.Length, 4);
            cellStart.SetData(body.CellStart);
            cellTris = new ComputeBuffer(Math.Max(1, body.CellTriangles.Length), 4);
            if (body.CellTriangles.Length > 0) cellTris.SetData(body.CellTriangles);
            gridOrigin = body.GridOrigin;
            cellSize = body.CellSize;
            dims = body.Dims;
        }

        public void SetPins(IList<SolverPin> list)
        {
            pinCount = list != null ? list.Count : 0;
            if (pins == null || pins.count < Math.Max(1, pinCount))
            {
                pins?.Release();
                pins = new ComputeBuffer(Math.Max(1, pinCount), 20);
            }
            if (pinCount > 0)
            {
                var array = new SolverPin[pinCount];
                for (int i = 0; i < pinCount; i++) array[i] = list[i];
                pins.SetData(array);
            }
        }

        public void SetPositions(Vector3[] positions)
        {
            x.SetData(positions);
            prev.SetData(positions);
            v.SetData(new Vector3[count]);
            readbackGeneration++;
        }

        public void Step(SimParams p, int frames)
        {
            if (shader == null || frames <= 0) return;
            int sub = Math.Max(1, p.substeps);
            float h = p.frameTime / sub;
            shader.SetInt("_Count", count);
            shader.SetInt("_PinCount", pinCount);
            shader.SetFloat("_H", h);
            shader.SetFloat("_Keep", Mathf.Max(0f, 1f - p.damping * h));
            shader.SetFloat("_Closure", p.closure);
            shader.SetFloat("_InvH2", 1f / (h * h));
            shader.SetFloat("_AlphaStitch", p.stitchCompliance / (h * h));
            shader.SetFloat("_MaxSpeed", p.maxSpeed);
            shader.SetFloat("_Clearance", p.clearance);
            shader.SetFloat("_Range", p.collisionRange);
            shader.SetFloat("_CandidateRange", p.collisionRange + ClothSolverShared.CandidateMargin);
            shader.SetFloat("_CellSize", cellSize);
            float3 g = p.gravity * p.gravityScale;
            shader.SetVector("_G", new Vector4(g.x, g.y, g.z, 0f));
            shader.SetVector("_GridOrigin", new Vector4(gridOrigin.x, gridOrigin.y, gridOrigin.z, 0f));
            shader.SetInts("_Dims", dims.x, dims.y, dims.z, 0);

            foreach (int k in new[] { kPredict, kCollide, kVelocity })
            {
                shader.SetBuffer(k, "_X", x); shader.SetBuffer(k, "_Prev", prev); shader.SetBuffer(k, "_V", v); shader.SetBuffer(k, "_W", w);
            }
            shader.SetBuffer(kSolve, "_X", x); shader.SetBuffer(kSolve, "_W", w); shader.SetBuffer(kSolve, "_Constraints", constraints);
            shader.SetBuffer(kPins, "_X", x); shader.SetBuffer(kPins, "_Pins", pins);
            shader.SetBuffer(kCollide, "_TA", ta); shader.SetBuffer(kCollide, "_TB", tb); shader.SetBuffer(kCollide, "_TC", tc); shader.SetBuffer(kCollide, "_TN", tn);
            shader.SetBuffer(kCollide, "_Cand", candidates);
            shader.SetBuffer(kCollide, "_Fric", fric);
            shader.SetBuffer(kCandidates, "_X", x);
            shader.SetBuffer(kCandidates, "_TA", ta); shader.SetBuffer(kCandidates, "_TB", tb); shader.SetBuffer(kCandidates, "_TC", tc); shader.SetBuffer(kCandidates, "_TN", tn);
            shader.SetBuffer(kCandidates, "_CellStart", cellStart); shader.SetBuffer(kCandidates, "_CellTris", cellTris);
            shader.SetBuffer(kCandidates, "_Cand", candidates);

            int particleGroups = (count + 63) / 64;
            SingleGroup = count <= SingleGroupMaxParticles;
            if (SingleGroup)
            {
                shader.SetInt("_Substeps", sub);
                shader.SetInt("_ColorCount", colors.Length);
                shader.SetBuffer(kFrame, "_X", x); shader.SetBuffer(kFrame, "_Prev", prev); shader.SetBuffer(kFrame, "_V", v); shader.SetBuffer(kFrame, "_W", w);
                shader.SetBuffer(kFrame, "_Fric", fric); shader.SetBuffer(kFrame, "_Constraints", constraints); shader.SetBuffer(kFrame, "_Colors", colorRanges);
                shader.SetBuffer(kFrame, "_Pins", pins); shader.SetBuffer(kFrame, "_Cand", candidates);
                shader.SetBuffer(kFrame, "_TA", ta); shader.SetBuffer(kFrame, "_TB", tb); shader.SetBuffer(kFrame, "_TC", tc); shader.SetBuffer(kFrame, "_TN", tn);
                for (int f = 0; f < frames; f++)
                {
                    shader.Dispatch(kCandidates, particleGroups, 1, 1);
                    shader.Dispatch(kFrame, 1, 1, 1);
                }
                return;
            }
            for (int f = 0; f < frames; f++)
            {
                shader.Dispatch(kCandidates, particleGroups, 1, 1);
                for (int s = 0; s < sub; s++)
                {
                    shader.Dispatch(kPredict, particleGroups, 1, 1);
                    for (int c = 0; c < colors.Length; c++)
                    {
                        if (colors[c].y <= 0) continue;
                        shader.SetInt("_Offset", colors[c].x);
                        shader.SetInt("_RangeCount", colors[c].y);
                        shader.Dispatch(kSolve, (colors[c].y + 63) / 64, 1, 1);
                    }
                    if (pinCount > 0) shader.Dispatch(kPins, (pinCount + 63) / 64, 1, 1);
                    shader.Dispatch(kCollide, particleGroups, 1, 1);
                    shader.Dispatch(kVelocity, particleGroups, 1, 1);
                }
            }
        }

        public void GetPositions(Vector3[] destination)
        {
            x.GetData(destination, 0, 0, Math.Min(count, destination.Length));
        }

        /// <summary>
        /// Asks for the positions without waiting. The callback runs later on the main thread, and not at
        /// all when the positions were reset or the solver disposed meanwhile (a stale answer).
        /// </summary>
        public void RequestPositions(Action<Vector3[]> onDone)
        {
            if (readbackPending || x == null || !SystemInfo.supportsAsyncGPUReadback) return;
            int generation = readbackGeneration;
            readbackPending = true;
            AsyncGPUReadback.Request(x, request =>
            {
                readbackPending = false;
                if (request.hasError || generation != readbackGeneration || x == null) return;
                var data = request.GetData<Vector3>();
                var result = new Vector3[data.Length];
                data.CopyTo(result);
                onDone(result);
            });
        }

        public void Dispose()
        {
            readbackGeneration++;
            foreach (ComputeBuffer b in new[] { x, prev, v, w, fric, constraints, pins, colorRanges, ta, tb, tc, tn, cellStart, cellTris, candidates }) b?.Release();
            x = prev = v = w = fric = constraints = colorRanges = pins = ta = tb = tc = tn = cellStart = cellTris = candidates = null;
        }
    }
}
