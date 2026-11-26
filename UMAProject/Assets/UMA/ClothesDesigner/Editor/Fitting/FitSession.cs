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
        /// <summary>The Fit Brush's paint at each point (<see cref="FitPaint"/>): hug and smooth 0 to 1, inflate -1 to 1.</summary>
        public float[] PaintHug = new float[0], PaintSmooth = new float[0], PaintInflate = new float[0], PaintStiff = new float[0], PaintWeight = new float[0];
        private int[] hugged, smoothed;
        private List<int>[] trianglesAround;
        private readonly List<SolverPin> paintPins = new List<SolverPin>();
        private readonly List<SolverPin> allPins = new List<SolverPin>();
        private int paintFrame;
        /// <summary>True when any point is painted to hug or smooth: each frame then pins it (the solver's positions are read every frame).</summary>
        public bool HasFramePaint => (hugged != null && hugged.Length > 0) || (smoothed != null && smoothed.Length > 0);

        public float Closure => Mathf.Clamp01(1f - SimTime / SewSeconds);
        /// <summary>
        /// Seconds over which a free piece's late seams close once the rest is sewn (a belt tightening round a dress, see
        /// <see cref="ConstraintKind.LateStitch"/>). Slow enough that the cloth under it is pushed in, not passed through.
        /// </summary>
        public const float LateSewSeconds = 1.2f;
        /// <summary>True when free pieces have seams that close late.</summary>
        public bool HasLateSeams => Model != null && Model.LateStitchCount > 0;
        /// <summary>1 while the late seams are as laid out, 0 once they are closed.</summary>
        public float LateClosure => HasLateSeams ? Mathf.Clamp01(1f - (SimTime - SewSeconds) / LateSewSeconds) : 0f;
        /// <summary>When gravity starts to come in: once everything is sewn, the late seams included.</summary>
        public float GravityStart => SewSeconds + (HasLateSeams ? LateSewSeconds : 0f);
        public float GravityScale => Mathf.Clamp01((SimTime - GravityStart) / GravityRampSeconds);
        /// <summary>The seams are closed and the pieces keep apart (free pieces' late seams may still be closing).</summary>
        public bool Sewn => SimTime >= SewSeconds;
        /// <summary>Every seam is closed, free pieces' late seams too: the cloth now hangs and can settle.</summary>
        public bool AllSewn => SimTime >= GravityStart;
        /// <summary>The time a drape that has settled is restored at (gravity fully in).</summary>
        public float SettledTime => GravityStart + GravityRampSeconds;
        public bool HasCloth => Mesh != null && Model != null && Solver != null;
        public IReadOnlyList<SolverPin> ActivePins => activePins;

        /// <summary>
        /// Builds everything from the pattern and the fit: dimensions, mesh, arrangement, constraints and
        /// solver. A kept drape that still matches the pattern and mesh level is restored, paused.
        /// </summary>
        public void Rebuild()
        {
            IEnumerator<bool> steps = RebuildInSteps();
            while (steps.MoveNext()) { }
        }

        /// <summary>
        /// <see cref="Rebuild"/> in three parts, for work spread over Editor updates (the sample browser's pictures):
        /// the mesh, then the arrangement round the body, then the constraints and the solver. Each MoveNext does one.
        /// </summary>
        public IEnumerator<bool> RebuildInSteps()
        {
            Messages.Clear();
            DisposeSolver();
            Mesh = null; Model = null;
            if (Pattern == null || Body == null) yield break;
            List<string> problems = Pattern.FindProblems();
            if (problems.Count > 0) { Messages.AddRange(problems); yield break; }
            Dimensions.Clear();
            PieceScale[] scales = PatternGeometry.Evaluate(Pattern, Body.Landmarks, Fit, Dimensions);
            Mesh = PatternMesher.Build(Pattern, scales, Fit.EdgeLengthCm());
            Messages.AddRange(Mesh.warnings);
            yield return true;
            Arranged = Arrangement.Arrange(Pattern, Fit, Mesh, Body);
            // Free pieces (a belt over a dress) start outside the cloth under them: laid out again that much farther out, at
            // the gap the first layout found. Moving pieces out cannot bring a point into the body, and the search for that
            // gap (every point checked against the body at up to eight gaps) is most of the work: on a Belted Dress of 8254
            // points it ran all eight and took 0.9 s each time. The layout is the same to the millimetre; the messages are
            // the first layout's (the second, at a fixed gap, does not report how far the pieces were moved out).
            float[] clearance = Arrangement.FreePieceClearance(Pattern, Fit, Mesh, Arranged.positions, Body);
            if (clearance != null)
            {
                Arrangement.Result first = Arranged;
                Arranged = Arrangement.Arrange(Pattern, Fit, Mesh, Body, first.gap, clearance);
                Arranged.messages.Clear();
                Arranged.messages.AddRange(first.messages);
            }
            Messages.AddRange(Arranged.messages);
            yield return true;
            ResolvePaint();
            Model = ClothModel.Build(Mesh, PieceFabrics(), Arranged.positions, ModelOptions());
            UpdatePaintLists();
            Positions = (Vector3[])Arranged.positions.Clone();
            SimTime = 0f;
            if (Fit.drape != null && Fit.drape.Matches(Mesh.revision, Mesh.VertexCount))
            {
                Positions = (Vector3[])Fit.drape.positions.Clone();
                SimTime = SettledTime;
                Messages.Add("Restored the kept drape.");
            }
            else if (Fit.drape != null && Fit.drape.vertexCount > 0)
            {
                Messages.Add("The kept drape belongs to an older version of the pattern or another mesh level; drape again and keep the new one.");
            }
            CreateSolver();
            RefreshPins();
        }

        /// <summary>What the model is built with: interfacing, the pattern's own settings, and the fit's frozen pieces.</summary>
        private ClothModelOptions ModelOptions()
        {
            var frozen = new HashSet<int>();
            if (Fit != null && Fit.frozenPieces != null)
            {
                foreach (string id in Fit.frozenPieces)
                {
                    int index = Pattern.IndexOfPiece(id);
                    if (index >= 0) frozen.Add(index);
                }
            }
            return new ClothModelOptions
            {
                Interfacing = PieceInterfacing(),
                Pattern = Pattern,
                FrozenPieces = frozen,
                SelfWithinPieces = Fit != null && Fit.selfCollideWithinPieces,
                TrianglesFaceIn = Arranged != null && Arranged.flipTriangles,
                Hug = Mesh != null && PaintHug.Length == Mesh.VertexCount ? PaintHug : null,
                Inflate = Mesh != null && PaintInflate.Length == Mesh.VertexCount ? PaintInflate : null,
                Stiff = Mesh != null && PaintStiff.Length == Mesh.VertexCount ? PaintStiff : null,
                Weight = Mesh != null && PaintWeight.Length == Mesh.VertexCount ? PaintWeight : null
            };
        }

        // ---- Fit Brush paint ----------------------------------------------------------------------

        /// <summary>Reads the fit's paint onto this mesh's points.</summary>
        public void ResolvePaint()
        {
            FitPaint.Resolve(Pattern, Mesh, Fit != null ? Fit.paint : null, out PaintHug, out PaintSmooth, out PaintInflate, out PaintStiff, out PaintWeight);
        }

        /// <summary>Keeps the points' paint in the fit (on the flat pattern).</summary>
        public void StorePaint()
        {
            if (Fit == null || Mesh == null) return;
            FitPaint.Store(Pattern, Mesh, PaintHug, PaintSmooth, PaintInflate, PaintStiff, PaintWeight, Fit.paint);
        }

        private void UpdatePaintLists()
        {
            var h = new List<int>();
            var s = new List<int>();
            for (int i = 0; i < PaintHug.Length; i++)
            {
                if (PaintHug[i] > 0f) h.Add(i);
                if (PaintSmooth[i] > 0f) s.Add(i);
            }
            hugged = h.ToArray();
            smoothed = s.ToArray();
            if (smoothed.Length > 0 && Model != null && (trianglesAround == null || trianglesAround.Length != Model.ParticleCount)) trianglesAround = FitPaint.TrianglesAround(Model.ParticleCount, Model.Triangles);
            paintPins.Clear();
        }

        /// <summary>
        /// Takes up painted points: hug's shorter rest lengths and the pressure are built into the cloth (its shape is
        /// kept), and the hug and smooth pins start with the next frame.
        /// </summary>
        public void ApplyPaint()
        {
            if (!HasCloth) return;
            ApplyFabric();
            UpdatePaintLists();
            RefreshPins();
        }

        /// <summary>This frame's hug and smooth pins, from where the cloth is now.</summary>
        private void UpdateFramePins()
        {
            SyncPositions();
            FitPaint.FramePins(Positions, Model, Body.Collider, Fit.clearanceCm * 0.01f, PaintHug, PaintSmooth, hugged, smoothed, trianglesAround, paintPins);
            PushPins();
        }

        private void PushPins()
        {
            allPins.Clear();
            allPins.AddRange(activePins);
            allPins.AddRange(GrabPins);
            allPins.AddRange(paintPins);
            Solver?.SetPins(allPins);
        }

        private List<InterfacingLevel> PieceInterfacing()
        {
            var list = new List<InterfacingLevel>(Pattern.pieces.Count);
            foreach (PatternPiece piece in Pattern.pieces) list.Add(piece != null ? piece.interfacing : InterfacingLevel.None);
            return list;
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
            Model = ClothModel.Build(Mesh, PieceFabrics(), Arranged.positions, ModelOptions());
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
            // Damped until everything is sewn and gravity comes in (free pieces' late seams too).
            if (!AllSewn) p.damping = Mathf.Max(p.damping, 6f);
            p.lateClosure = LateClosure;
            // Pieces keep apart once sewn: laid out, pieces may overlap (a full skirt's panels) and must pass to sew.
            p.thickness = Fit.clothThicknessMm * 0.001f;
            p.selfCollide = Sewn && Fit.clothThicknessMm > 0f ? 1 : 0;
            return p;
        }

        /// <summary>Advances the simulation by whole frames, one sewing-schedule value per frame.</summary>
        public void Step(int frames)
        {
            if (!HasCloth) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                // Painted hug and smooth: soft pins worked out from where the cloth is, once it is sewn, every other
                // frame (reading the GPU's positions back is most of their cost; the targets move slowly).
                if (HasFramePaint && Sewn) { if ((paintFrame++ & 1) == 0) UpdateFramePins(); }
                else if (paintPins.Count > 0) { paintPins.Clear(); PushPins(); }
                EasePreview();
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
            PushPins();
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
        public Dictionary<int, float> ConnectedWithin(int start, float radius) => ConnectedWithin(start, radius, -1);

        /// <summary>
        /// The cloth within <paramref name="radius"/> of a particle, along the cloth, each with a weight falling off to 0 at
        /// the radius. With a <paramref name="layer"/> of 0 or more, only cloth on that layer (its piece's
        /// <see cref="PatternPiece.layer"/>) is taken, and the search does not cross onto another layer's piece.
        /// </summary>
        public Dictionary<int, float> ConnectedWithin(int start, float radius, int layer)
        {
            var result = new Dictionary<int, float>();
            if (Model == null || start < 0) return result;
            int[] layers = Model.Layer;
            bool limited = layer >= 0 && layers != null;
            if (limited && (start >= layers.Length || layers[start] != layer)) return result;
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
                    if (limited && (j >= layers.Length || layers[j] != layer)) continue;
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

        /// <summary>The particles whose piece is on another layer than <paramref name="layer"/>: the cloth a one-layer grab holds still.</summary>
        public List<int> ParticlesOffLayer(int layer)
        {
            var result = new List<int>();
            if (Model == null || Model.Layer == null) return result;
            for (int i = 0; i < Model.Layer.Length; i++) if (Model.Layer[i] != layer) result.Add(i);
            return result;
        }

        public void DisposeSolver()
        {
            Solver?.Dispose();
            Solver = null;
            // A new model and solver have their own rest lengths.
            previewRestBase = null;
            previewNow = previewTarget = null;
        }

        // ---- Live resizing (the Size tool's drag) --------------------------------------------------

        private float[] previewRestBase;
        private Vector2[] previewNow, previewTarget;

        /// <summary>
        /// How far a piece's scale moves towards a live resize's each frame. Measured on a free-hanging tube: its rest
        /// lengths changed by a fifth at once blew it apart (its points ended a metre out); the same change over 60 frames
        /// (a third of a percent a frame) took it to the size asked for, smoothly. Cloth on the body took either.
        /// </summary>
        public const float PreviewScalePerFrame = 0.003f;

        /// <summary>A live resize is shown: some pieces' rest lengths are scaled from the model's own.</summary>
        public bool PreviewingScale => previewRestBase != null;

        /// <summary>A shown resize has not been eased all the way in yet.</summary>
        public bool PreviewEasing
        {
            get
            {
                if (previewNow == null || previewTarget == null) return false;
                for (int i = 0; i < previewNow.Length; i++) if (previewNow[i].x != previewTarget[i].x || previewNow[i].y != previewTarget[i].y) return true;
                return false;
            }
        }

        /// <summary>Each piece's scale shown now (one for a piece not resized), or null when no resize is shown.</summary>
        public Vector2[] PreviewScaleNow => previewNow != null ? (Vector2[])previewNow.Clone() : null;

        /// <summary>
        /// Shows a resize while the cloth simulates, before the pattern is changed: each piece's own stretch and bend
        /// constraints come to rest longer or shorter by its factor across and down (as drawn), so the cloth grows or
        /// shrinks where it hangs. The factors are eased in over the following frames (<see cref="PreviewScalePerFrame"/>).
        /// Null, or factors of one, put the model's own rest lengths back at once.
        /// </summary>
        public void PreviewPieceScale(Vector2[] factors)
        {
            if (Model == null || Solver == null || Mesh == null) return;
            bool any = false;
            if (factors != null) foreach (Vector2 k in factors) if (k != Vector2.one) any = true;
            if (!any)
            {
                if (previewRestBase == null) return;
                for (int i = 0; i < Model.Constraints.Length; i++) Model.Constraints[i].rest = previewRestBase[i];
                previewRestBase = null;
                previewNow = previewTarget = null;
                Solver.SetConstraints(Model.Constraints);
                return;
            }
            StartPreview();
            previewTarget = ForEveryPiece(factors);
        }

        /// <summary>
        /// After the cloth is built again at a new size from a drape that had not caught up with it (released before the
        /// live resize was eased in, or a size typed in): the pieces start as large as the drape is, each piece's
        /// <paramref name="start"/> factor of the new rest lengths, and ease to the new size.
        /// </summary>
        public void EaseFrom(Vector2[] start)
        {
            if (Model == null || Solver == null || Mesh == null || start == null) return;
            bool any = false;
            foreach (Vector2 k in start) if (k != Vector2.one) any = true;
            if (!any) return;
            previewRestBase = null;
            StartPreview();
            previewNow = ForEveryPiece(start);
            previewTarget = ForEveryPiece(null);
            ScaleRests(Model.Constraints, previewRestBase, Mesh.piece, Model.Flat, previewNow);
            Solver.SetConstraints(Model.Constraints);
        }

        private void StartPreview()
        {
            if (previewRestBase != null) return;
            previewRestBase = new float[Model.Constraints.Length];
            for (int i = 0; i < previewRestBase.Length; i++) previewRestBase[i] = Model.Constraints[i].rest;
            previewNow = ForEveryPiece(null);
        }

        /// <summary>Factors for every piece of the mesh: those given, one for the rest.</summary>
        private Vector2[] ForEveryPiece(Vector2[] factors)
        {
            int pieces = Mesh.pieceStart != null ? Mesh.pieceStart.Length : 0;
            var fitted = new Vector2[pieces];
            for (int i = 0; i < pieces; i++) fitted[i] = factors != null && i < factors.Length ? factors[i] : Vector2.one;
            return fitted;
        }

        /// <summary>One frame of a live resize: each piece's scale a step nearer its target; done when all are there.</summary>
        private void EasePreview()
        {
            if (previewTarget == null) return;
            bool moved = false, done = true;
            for (int i = 0; i < previewNow.Length; i++)
            {
                Vector2 now = previewNow[i], to = previewTarget[i];
                if (now.x == to.x && now.y == to.y) continue;
                previewNow[i] = new Vector2(Mathf.MoveTowards(now.x, to.x, PreviewScalePerFrame), Mathf.MoveTowards(now.y, to.y, PreviewScalePerFrame));
                moved = true;
                if (previewNow[i].x != to.x || previewNow[i].y != to.y) done = false;
            }
            if (!moved) return;
            ScaleRests(Model.Constraints, previewRestBase, Mesh.piece, Model.Flat, previewNow);
            Solver.SetConstraints(Model.Constraints);
            // Eased all the way back to the model's own rest lengths (after a rebuild at the new size): nothing is shown.
            bool atOne = true;
            foreach (Vector2 k in previewTarget) if (k.x != 1f || k.y != 1f) atOne = false;
            if (done && atOne) { previewRestBase = null; previewNow = previewTarget = null; }
        }

        /// <summary>
        /// Rest lengths for pieces scaled across and down (<paramref name="factors"/>, per piece): a stretch or bend
        /// constraint between two points of one piece rests as long as the line between them on the flat pattern would be
        /// with its piece scaled; stitches, and constraints between pieces or of an unscaled piece, keep their own.
        /// </summary>
        public static void ScaleRests(ClothConstraint[] constraints, float[] baseRest, IList<int> piece, Vector2[] flat, Vector2[] factors)
        {
            for (int i = 0; i < constraints.Length; i++)
            {
                ClothConstraint c = constraints[i];
                c.rest = baseRest[i];
                int pa = piece[c.a];
                bool own = (c.kind == (int)ConstraintKind.Stretch || c.kind == (int)ConstraintKind.Bend) && pa == piece[c.b];
                if (own && factors != null && pa < factors.Length && factors[pa] != Vector2.one)
                {
                    Vector2 d = flat[c.b] - flat[c.a];
                    float length = d.magnitude;
                    if (length > 1e-7f) c.rest = baseRest[i] * new Vector2(d.x * factors[pa].x, d.y * factors[pa].y).magnitude / length;
                }
                constraints[i] = c;
            }
        }

        public void Dispose() => DisposeSolver();
    }
}
