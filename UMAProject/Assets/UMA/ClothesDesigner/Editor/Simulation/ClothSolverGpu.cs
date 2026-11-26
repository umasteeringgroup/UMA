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
        private int kSelfClear, kSelfCount, kSelfScan, kSelfInsert, kSelfCandidates, kSelfPush, kSelfApply;
        private ComputeBuffer x, prev, v, w, fric, partner, constraints, pins, colorRanges;
        private ComputeBuffer tris, piece, weld, nearStart, near, selfCandidates, hashCount, hashStart, hashTris, selfDelta, selfTarget;
        private ComputeBuffer flatBuffer, layerBuffer, pressureBuffer, layerCandBuffer;
        private bool layered;
        private bool selfWithin;
        private int outSign = 1;
        private int triCount;
        private bool hasSelf;
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
                foreach (ComputeBuffer b in new[] { x, prev, v, w, fric, partner, constraints, pins, colorRanges, ta, tb, tc, tn, cellStart, cellTris, candidates, tris, piece, weld, nearStart, near, selfCandidates, hashCount, hashStart, hashTris, selfDelta, selfTarget, flatBuffer, layerBuffer, pressureBuffer, layerCandBuffer })
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
            kSelfClear = shader.FindKernel("CSSelfClear");
            kSelfCount = shader.FindKernel("CSSelfCount");
            kSelfScan = shader.FindKernel("CSSelfScan");
            kSelfInsert = shader.FindKernel("CSSelfInsert");
            kSelfCandidates = shader.FindKernel("CSSelfCandidates");
            kSelfPush = shader.FindKernel("CSSelfPush");
            kSelfApply = shader.FindKernel("CSSelfApply");
            count = model.ParticleCount;
            hasSelf = model.Piece != null && model.WeldGroup != null && model.NearStart != null && model.Triangles != null && model.Triangles.Length > 0;
            triCount = hasSelf ? model.Triangles.Length / 3 : 0;
            tris = new ComputeBuffer(Math.Max(3, triCount * 3), 4);
            if (hasSelf) tris.SetData(model.Triangles);
            piece = new ComputeBuffer(Math.Max(1, count), 4);
            weld = new ComputeBuffer(Math.Max(1, count), 4);
            if (hasSelf) { piece.SetData(model.Piece); weld.SetData(model.WeldGroup); }
            nearStart = new ComputeBuffer(Math.Max(1, count) + 1, 4);
            near = new ComputeBuffer(hasSelf ? Math.Max(1, model.Near.Length) : 1, 4);
            if (hasSelf) { nearStart.SetData(model.NearStart); near.SetData(model.Near); }
            else nearStart.SetData(new int[Math.Max(1, count) + 1]);
            selfCandidates = new ComputeBuffer(Math.Max(1, count) * ClothSolverShared.SelfCandidates, 4);
            selfCandidates.SetData(new int[Math.Max(1, count) * ClothSolverShared.SelfCandidates]);
            hashCount = new ComputeBuffer(hasSelf ? ClothSolverShared.SelfHashSize : 1, 4);
            hashStart = new ComputeBuffer(hasSelf ? ClothSolverShared.SelfHashSize + 1 : 1, 4);
            hashTris = new ComputeBuffer(hasSelf ? ClothSolverShared.SelfListCapacity(triCount) : 1, 4);
            selfDelta = new ComputeBuffer(Math.Max(1, count), 12);
            selfDelta.SetData(new Vector3[Math.Max(1, count)]);
            selfTarget = new ComputeBuffer(Math.Max(1, count), 4);
            var flatData = new Vector2[Math.Max(1, count)];
            var layerData = new int[Math.Max(1, count)];
            var pressureData = new float[Math.Max(1, count)];
            for (int i = 0; i < count; i++)
            {
                if (model.Flat != null && i < model.Flat.Length) flatData[i] = model.Flat[i];
                if (model.Layer != null && i < model.Layer.Length) layerData[i] = model.Layer[i];
                if (model.Pressure != null && i < model.Pressure.Length) pressureData[i] = model.Pressure[i];
            }
            flatBuffer = new ComputeBuffer(flatData.Length, 8); flatBuffer.SetData(flatData);
            layerBuffer = new ComputeBuffer(layerData.Length, 4); layerBuffer.SetData(layerData);
            // Each particle's higher-layer triangle (ClothSolverShared.LayerReach), or -1; found once a frame.
            var noLayerCand = new int[layerData.Length];
            layered = false;
            for (int i = 0; i < noLayerCand.Length; i++) { noLayerCand[i] = 0; if (i > 0 && i < count && layerData[i] != layerData[0]) layered = ClothSolverShared.OneWayLayers; }
            layerCandBuffer = new ComputeBuffer(noLayerCand.Length, 4); layerCandBuffer.SetData(noLayerCand);
            pressureBuffer = new ComputeBuffer(pressureData.Length, 4); pressureBuffer.SetData(pressureData);
            selfWithin = model.SelfWithinPieces && model.Flat != null;
            outSign = model.OutSign >= 0 ? 1 : -1;
            x = new ComputeBuffer(count, 12);
            prev = new ComputeBuffer(count, 12);
            v = new ComputeBuffer(count, 12);
            w = new ComputeBuffer(count, 4);
            candidates = new ComputeBuffer(count * ClothSolverShared.Candidates, 4);
            w.SetData(model.InverseMass);
            fric = new ComputeBuffer(count, 4);
            fric.SetData(model.Friction);
            partner = new ComputeBuffer(count, 8);
            partner.SetData(ClothSolverShared.SeamPairs(model));
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

        public void SetConstraints(ClothConstraint[] list)
        {
            // The buffer holds at least one entry even for a model without constraints.
            if (constraints == null || list == null || list.Length == 0 || list.Length != constraints.count) return;
            constraints.SetData(list);
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
            shader.SetFloat("_LateClosure", p.lateClosure);
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
            // Pressure in the prediction (nearest body triangle's normal), the push-speed limit in the velocity step.
            shader.SetBuffer(kPredict, "_Pressure", pressureBuffer); shader.SetBuffer(kPredict, "_Cand", candidates); shader.SetBuffer(kPredict, "_TN", tn);
            shader.SetBuffer(kVelocity, "_SelfDelta", selfDelta);
            shader.SetInt("_SelfWithin", selfWithin ? 1 : 0);
            shader.SetInt("_OutSign", outSign);
            shader.SetFloat("_MinFlat", ClothSolverShared.SelfMinFlatDistance);
            shader.SetBuffer(kSolve, "_X", x); shader.SetBuffer(kSolve, "_W", w); shader.SetBuffer(kSolve, "_Constraints", constraints);
            shader.SetBuffer(kPins, "_X", x); shader.SetBuffer(kPins, "_Pins", pins);
            shader.SetBuffer(kCollide, "_TA", ta); shader.SetBuffer(kCollide, "_TB", tb); shader.SetBuffer(kCollide, "_TC", tc); shader.SetBuffer(kCollide, "_TN", tn);
            shader.SetBuffer(kCollide, "_Cand", candidates);
            shader.SetBuffer(kCollide, "_Fric", fric);
            shader.SetBuffer(kCollide, "_Partner", partner);
            shader.SetBuffer(kCandidates, "_X", x);
            shader.SetBuffer(kCandidates, "_TA", ta); shader.SetBuffer(kCandidates, "_TB", tb); shader.SetBuffer(kCandidates, "_TC", tc); shader.SetBuffer(kCandidates, "_TN", tn);
            shader.SetBuffer(kCandidates, "_CellStart", cellStart); shader.SetBuffer(kCandidates, "_CellTris", cellTris);
            shader.SetBuffer(kCandidates, "_Cand", candidates);

            int particleGroups = (count + 63) / 64;
            bool self = hasSelf && p.selfCollide != 0 && p.thickness > 0f;
            shader.SetInt("_SelfCollide", self ? 1 : 0);
            shader.SetFloat("_Thickness", p.thickness);
            shader.SetInt("_TriCount", triCount);
            shader.SetBuffer(kSelfClear, "_HashCount", hashCount);
            shader.SetBuffer(kSelfCount, "_X", x); shader.SetBuffer(kSelfCount, "_Tris", tris); shader.SetBuffer(kSelfCount, "_HashCount", hashCount);
            shader.SetBuffer(kSelfScan, "_HashCount", hashCount); shader.SetBuffer(kSelfScan, "_HashStart", hashStart);
            shader.SetBuffer(kSelfInsert, "_X", x); shader.SetBuffer(kSelfInsert, "_Tris", tris); shader.SetBuffer(kSelfInsert, "_HashCount", hashCount); shader.SetBuffer(kSelfInsert, "_HashTris", hashTris);
            shader.SetBuffer(kSelfCandidates, "_X", x); shader.SetBuffer(kSelfCandidates, "_W", w); shader.SetBuffer(kSelfCandidates, "_Tris", tris);
            shader.SetBuffer(kSelfCandidates, "_Piece", piece); shader.SetBuffer(kSelfCandidates, "_Weld", weld); shader.SetBuffer(kSelfCandidates, "_NearStart", nearStart); shader.SetBuffer(kSelfCandidates, "_Near", near);
            shader.SetBuffer(kSelfCandidates, "_HashStart", hashStart); shader.SetBuffer(kSelfCandidates, "_HashTris", hashTris); shader.SetBuffer(kSelfCandidates, "_SelfCand", selfCandidates); shader.SetBuffer(kSelfCandidates, "_SelfTarget", selfTarget);
            shader.SetBuffer(kSelfCandidates, "_Flat", flatBuffer); shader.SetBuffer(kSelfCandidates, "_Layer", layerBuffer);
            shader.SetInt("_Layered", layered ? 1 : 0);
            shader.SetBuffer(kSelfCandidates, "_LayerCand", layerCandBuffer); shader.SetBuffer(kSelfCandidates, "_Cand", candidates);
            shader.SetBuffer(kSelfCandidates, "_TA", ta); shader.SetBuffer(kSelfCandidates, "_TB", tb); shader.SetBuffer(kSelfCandidates, "_TC", tc); shader.SetBuffer(kSelfCandidates, "_TN", tn);
            shader.SetBuffer(kSelfPush, "_LayerCand", layerCandBuffer); shader.SetBuffer(kSelfPush, "_Cand", candidates);
            shader.SetBuffer(kSelfPush, "_TA", ta); shader.SetBuffer(kSelfPush, "_TB", tb); shader.SetBuffer(kSelfPush, "_TC", tc); shader.SetBuffer(kSelfPush, "_TN", tn);
            shader.SetBuffer(kSelfPush, "_X", x); shader.SetBuffer(kSelfPush, "_W", w); shader.SetBuffer(kSelfPush, "_Tris", tris); shader.SetBuffer(kSelfPush, "_SelfCand", selfCandidates); shader.SetBuffer(kSelfPush, "_SelfDelta", selfDelta); shader.SetBuffer(kSelfPush, "_SelfTarget", selfTarget);
            shader.SetBuffer(kSelfApply, "_X", x); shader.SetBuffer(kSelfApply, "_SelfDelta", selfDelta);
            void ClothCandidates()
            {
                if (!self) return;
                shader.Dispatch(kSelfClear, ClothSolverShared.SelfHashSize / 64, 1, 1);
                shader.Dispatch(kSelfCount, Math.Max(1, (triCount + 63) / 64), 1, 1);
                shader.Dispatch(kSelfScan, 1, 1, 1);
                shader.Dispatch(kSelfInsert, Math.Max(1, (triCount + 63) / 64), 1, 1);
                shader.Dispatch(kSelfCandidates, particleGroups, 1, 1);
            }
            SingleGroup = count <= SingleGroupMaxParticles;
            if (SingleGroup)
            {
                shader.SetInt("_Substeps", sub);
                shader.SetInt("_ColorCount", colors.Length);
                shader.SetBuffer(kFrame, "_X", x); shader.SetBuffer(kFrame, "_Prev", prev); shader.SetBuffer(kFrame, "_V", v); shader.SetBuffer(kFrame, "_W", w);
                shader.SetBuffer(kFrame, "_Fric", fric); shader.SetBuffer(kFrame, "_Partner", partner); shader.SetBuffer(kFrame, "_Constraints", constraints); shader.SetBuffer(kFrame, "_Colors", colorRanges);
                shader.SetBuffer(kFrame, "_Pins", pins); shader.SetBuffer(kFrame, "_Cand", candidates);
                shader.SetBuffer(kFrame, "_TA", ta); shader.SetBuffer(kFrame, "_TB", tb); shader.SetBuffer(kFrame, "_TC", tc); shader.SetBuffer(kFrame, "_TN", tn);
                shader.SetBuffer(kFrame, "_Tris", tris); shader.SetBuffer(kFrame, "_SelfCand", selfCandidates); shader.SetBuffer(kFrame, "_SelfDelta", selfDelta); shader.SetBuffer(kFrame, "_SelfTarget", selfTarget);
                shader.SetBuffer(kFrame, "_Pressure", pressureBuffer); shader.SetBuffer(kFrame, "_LayerCand", layerCandBuffer);
                for (int f = 0; f < frames; f++)
                {
                    shader.Dispatch(kCandidates, particleGroups, 1, 1);
                    ClothCandidates();
                    shader.Dispatch(kFrame, 1, 1, 1);
                }
                return;
            }
            for (int f = 0; f < frames; f++)
            {
                shader.Dispatch(kCandidates, particleGroups, 1, 1);
                ClothCandidates();
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
                    if (self)
                    {
                        shader.Dispatch(kSelfPush, particleGroups, 1, 1);
                        shader.Dispatch(kSelfApply, particleGroups, 1, 1);
                    }
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
            foreach (ComputeBuffer b in new[] { x, prev, v, w, fric, partner, constraints, pins, colorRanges, ta, tb, tc, tn, cellStart, cellTris, candidates, tris, piece, weld, nearStart, near, selfCandidates, hashCount, hashStart, hashTris, selfDelta, selfTarget, flatBuffer, layerBuffer, pressureBuffer, layerCandBuffer }) b?.Release();
            x = prev = v = w = fric = partner = constraints = colorRanges = pins = ta = tb = tc = tn = cellStart = cellTris = candidates = null;
            tris = piece = weld = nearStart = near = selfCandidates = hashCount = hashStart = hashTris = selfDelta = selfTarget = null;
            flatBuffer = layerBuffer = pressureBuffer = layerCandBuffer = null;
        }
    }
}
