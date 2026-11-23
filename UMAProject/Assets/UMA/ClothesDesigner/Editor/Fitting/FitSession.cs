using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// One pattern being fitted to one body: the mesh made from the pattern, the pieces laid round the
    /// body, the solver, the pins and the drape. The Clothes Designer's panels drive it, and the tests
    /// drive it the same way without a window.
    /// </summary>
    public sealed class FitSession : IDisposable
    {
        /// <summary>Seconds of simulated time over which every seam closes, with gravity off.</summary>
        public const float SewSeconds = 0.8f;
        /// <summary>Seconds over which gravity is then brought in.</summary>
        public const float GravityRampSeconds = 0.4f;

        public ClothesPattern Pattern;
        public ClothesFit Fit;
        public BodyReference Body;
        public PatternMesh Mesh;
        public ClothModel Model;
        public Arrangement.Result Arranged;
        public IClothSolver Solver;
        public Vector3[] Positions = new Vector3[0];
        public List<EvaluatedDimension> Dimensions = new List<EvaluatedDimension>();
        public readonly List<string> Messages = new List<string>();
        public float SimTime;
        public bool PreferCpu;
        public bool FinalQuality;
        public double LastStepMilliseconds;
        /// <summary>Temporary targets while the artist drags the cloth.</summary>
        public readonly List<SolverPin> GrabPins = new List<SolverPin>();
        private readonly List<SolverPin> activePins = new List<SolverPin>();

        public float Closure => Mathf.Clamp01(1f - SimTime / SewSeconds);
        public float GravityScale => Mathf.Clamp01((SimTime - SewSeconds) / GravityRampSeconds);
        public bool Sewn => SimTime >= SewSeconds;
        public bool HasCloth => Mesh != null && Model != null && Solver != null;
        public IReadOnlyList<SolverPin> ActivePins => activePins;

        /// <summary>
        /// Builds everything from the pattern and the fit: dimensions, mesh, arrangement, constraints and
        /// solver. A kept drape that still matches the pattern and mesh level is restored, paused.
        /// </summary>
        public void Rebuild()
        {
            Messages.Clear();
            DisposeSolver();
            Mesh = null; Model = null;
            if (Pattern == null || Body == null) return;
            List<string> problems = Pattern.FindProblems();
            if (problems.Count > 0) { Messages.AddRange(problems); return; }
            Dimensions.Clear();
            PieceScale[] scales = PatternGeometry.Evaluate(Pattern, Body.Landmarks, Fit, Dimensions);
            Mesh = PatternMesher.Build(Pattern, scales, Fit.EdgeLengthCm());
            Messages.AddRange(Mesh.warnings);
            Arranged = Arrangement.Arrange(Pattern, Fit, Mesh, Body);
            Messages.AddRange(Arranged.messages);
            Model = ClothModel.Build(Mesh, PieceFabrics(), Arranged.positions);
            Positions = (Vector3[])Arranged.positions.Clone();
            SimTime = 0f;
            if (Fit.drape != null && Fit.drape.Matches(Mesh.revision, Mesh.VertexCount))
            {
                Positions = (Vector3[])Fit.drape.positions.Clone();
                SimTime = SewSeconds + GravityRampSeconds;
                Messages.Add("Restored the kept drape.");
            }
            else if (Fit.drape != null && Fit.drape.vertexCount > 0)
            {
                Messages.Add("The kept drape belongs to an older version of the pattern or another mesh level; drape again and keep the new one.");
            }
            CreateSolver();
            RefreshPins();
        }

        private List<FabricSettings> PieceFabrics()
        {
            var list = new List<FabricSettings>(Pattern.pieces.Count);
            foreach (PatternPiece piece in Pattern.pieces) list.Add(piece != null ? piece.fabric : null);
            return list;
        }

        /// <summary>
        /// Takes up changed fabrics without laying the pieces out again: the cloth keeps its shape and
        /// carries on from there (it starts from rest, so a moving cloth stops for a moment).
        /// </summary>
        public void ApplyFabric()
        {
            if (!HasCloth) return;
            SyncPositions();
            Vector3[] keep = (Vector3[])Positions.Clone();
            Model = ClothModel.Build(Mesh, PieceFabrics(), Arranged.positions);
            DisposeSolver();
            Positions = keep;
            CreateSolver();
            RefreshPins();
        }

        private void CreateSolver()
        {
            bool gpu = !PreferCpu && ClothSolverGpu.Supported;
            Solver = gpu ? (IClothSolver)new ClothSolverGpu() : new ClothSolverCpu();
            try
            {
                Solver.Initialize(Model, Body.Collider, Positions);
            }
            catch (Exception ex)
            {
                // A GPU that cannot run the shader: carry on with the CPU solver and say so.
                Solver.Dispose();
                Messages.Add("The GPU solver could not start (" + ex.Message + "); using the CPU solver.");
                Solver = new ClothSolverCpu();
                Solver.Initialize(Model, Body.Collider, Positions);
            }
        }

        /// <summary>Switches solver, keeping the current positions.</summary>
        public void SetPreferCpu(bool cpu)
        {
            if (PreferCpu == cpu) return;
            PreferCpu = cpu;
            if (Model == null) return;
            SyncPositions();
            DisposeSolver();
            CreateSolver();
            RefreshPins();
        }

        /// <summary>The body changed shape or pose: collide with the new one. Body pins follow it.</summary>
        public void SetBody(BodyReference body)
        {
            Body = body;
            if (Solver != null) Solver.SetBody(body.Collider);
            RefreshPins();
        }

        public SimParams CurrentParams()
        {
            Vector3 gravity = Body != null && Body.Skin.Renderer != null
                ? Body.Skin.Renderer.transform.InverseTransformDirection(Physics.gravity)
                : Physics.gravity;
            SimParams p = ClothModel.Params(Fit.damping, Fit.clearanceCm * 0.01f, Closure, GravityScale, gravity, FinalQuality, Mesh != null ? Mesh.edgeLengthCm : 2f);
            if (!Sewn) p.damping = Mathf.Max(p.damping, 6f);
            return p;
        }

        /// <summary>Advances the simulation by whole frames, one sewing-schedule value per frame.</summary>
        public void Step(int frames)
        {
            if (!HasCloth) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                Solver.Step(CurrentParams(), 1);
                SimTime += 1f / 60f;
            }
            timer.Stop();
            LastStepMilliseconds = timer.Elapsed.TotalMilliseconds / Math.Max(1, frames);
        }

        /// <summary>Reads the solver's positions now (waits for the GPU).</summary>
        public Vector3[] SyncPositions()
        {
            if (!HasCloth) return Positions;
            if (Positions.Length != Model.ParticleCount) Positions = new Vector3[Model.ParticleCount];
            Solver.GetPositions(Positions);
            return Positions;
        }

        public void ResetToArrangement()
        {
            if (!HasCloth) return;
            Positions = (Vector3[])Arranged.positions.Clone();
            Solver.SetPositions(Positions);
            SimTime = 0f;
        }

        /// <summary>Puts the cloth at the given positions (Undo, Escape during a drag), sewn and settled.</summary>
        public void Restore(Vector3[] positions, float simTime)
        {
            if (!HasCloth || positions == null || positions.Length != Model.ParticleCount) return;
            Positions = (Vector3[])positions.Clone();
            Solver.SetPositions(Positions);
            SimTime = simTime;
        }

        /// <summary>Saves the current shape in the fit, so reopening it restores these folds.</summary>
        public void KeepDrape()
        {
            if (!HasCloth) return;
            SyncPositions();
            Fit.drape = new AcceptedDrape { positions = (Vector3[])Positions.Clone(), sourceRevision = Mesh.revision, vertexCount = Mesh.VertexCount };
        }

        public FitMetrics Measure()
        {
            SyncPositions();
            return FitMetrics.Measure(Model, Positions, Body.Collider, Fit.clearanceCm * 0.01f, activePins);
        }

        /// <summary>The particle of a piece nearest a point on the drawn pattern.</summary>
        public int ParticleAt(string pieceId, Vector2 drawn)
        {
            int piece = Pattern.IndexOfPiece(pieceId);
            if (piece < 0 || Mesh == null) return -1;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int v = Mesh.pieceStart[piece]; v < Mesh.pieceStart[piece] + Mesh.pieceCount[piece]; v++)
            {
                float d = (Mesh.drawn[v] - drawn).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = v; }
            }
            return best;
        }

        /// <summary>Turns the fit's pins (stored on the flat pattern) and the grab targets into solver pins.</summary>
        public void RefreshPins()
        {
            activePins.Clear();
            if (Mesh == null) return;
            foreach (ClothPin pin in Fit.pins)
            {
                int particle = ParticleAt(pin.pieceId, pin.flatPosition);
                if (particle < 0) continue;
                Vector3 target = pin.target;
                if (pin.kind == PinKind.Body)
                {
                    if (!BodyPinTarget(pin, out target)) continue;
                }
                activePins.Add(new SolverPin { particle = particle, target = target, weight = pin.weight });
            }
            var all = new List<SolverPin>(activePins);
            all.AddRange(GrabPins);
            Solver?.SetPins(all);
        }

        /// <summary>Where a body pin holds the cloth on the current body. False when its triangle no longer exists.</summary>
        public bool BodyPinTarget(ClothPin pin, out Vector3 target)
        {
            target = Vector3.zero;
            BodyCollider c = Body.Collider;
            if (pin.bodyTriangle < 0 || pin.bodyTriangle >= c.TriangleCount) return false;
            int t = pin.bodyTriangle;
            target = c.A[t] * pin.barycentric.x + c.B[t] * pin.barycentric.y + c.C[t] * pin.barycentric.z + c.N[t] * pin.normalOffset;
            return true;
        }

        /// <summary>Pins that point at a body triangle the current body does not have.</summary>
        public int CountBrokenBodyPins()
        {
            int broken = 0;
            foreach (ClothPin pin in Fit.pins) if (pin.kind == PinKind.Body && !BodyPinTarget(pin, out _)) broken++;
            return broken;
        }

        /// <summary>Particles reachable along the cloth (and across sewn seams) within a radius, with a smooth falloff weight.</summary>
        public Dictionary<int, float> ConnectedWithin(int start, float radius)
        {
            var result = new Dictionary<int, float>();
            if (Model == null || start < 0) return result;
            var distance = new Dictionary<int, float> { [start] = 0f };
            var queue = new SortedSet<(float, int)> { (0f, start) };
            while (queue.Count > 0)
            {
                (float d, int i) = queue.Min;
                queue.Remove(queue.Min);
                if (d > radius) break;
                if (distance.TryGetValue(i, out float known) && known < d) continue;
                float u = d / Mathf.Max(1e-5f, radius);
                result[i] = 1f - u * u * (3f - 2f * u);
                foreach (int j in Model.Neighbours[i])
                {
                    float nd = d + Vector3.Distance(Positions[i], Positions[j]);
                    if (nd <= radius && (!distance.TryGetValue(j, out float old) || nd < old))
                    {
                        distance[j] = nd;
                        queue.Add((nd, j));
                    }
                }
            }
            return result;
        }

        public void DisposeSolver()
        {
            Solver?.Dispose();
            Solver = null;
        }

        public void Dispose() => DisposeSolver();
    }
}
