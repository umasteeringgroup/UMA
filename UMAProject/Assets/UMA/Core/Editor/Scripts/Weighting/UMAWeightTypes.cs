using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>How <see cref="UMAWeightTransfer"/> gives a mesh (clothes) bone weights from a body.</summary>
    public enum UMAWeightMethod
    {
        /// <summary>Tries Tight Surface, Robust Inpaint and Volumetric, poses the character, and keeps the one that pokes through and stretches least.</summary>
        Auto = 0,
        /// <summary>For clothes cut from the body mesh: vertices at a body vertex's place (or its UV) copy it; the rest are weighted as Tight Surface.</summary>
        ExactMatch = 1,
        /// <summary>Every vertex copies the closest point of the body. Gloves, socks, leggings, tight sleeves.</summary>
        TightSurface = 2,
        /// <summary>Copies the body only where the cloth clearly lies on it (close, facing the same way, not deep inside); fills the rest smoothly over the cloth.</summary>
        RobustInpaint = 3,
        /// <summary>The body's weights spread through the space round it; cloth far from the body samples that. Long skirts, gowns, capes, loose parts.</summary>
        Volumetric = 4,
        /// <summary>Starts from Robust Inpaint, then fits weights that reproduce how the cloth hangs in several simulated poses. Needs poses with draped positions.</summary>
        SimulatedFit = 5
    }

    /// <summary>How the cloth away from the body is filled in from where it lies on the body.</summary>
    public enum UMAInpaintEnergy
    {
        /// <summary>Each vertex the average of its neighbours: the simplest fill, with a crease where it meets the copied weights.</summary>
        Harmonic = 0,
        /// <summary>The fill of Abdrashitov et al. 2023: it joins the copied weights smoothly and carries their trend on.</summary>
        Biharmonic = 1
    }

    /// <summary>What happened to one vertex.</summary>
    public enum UMAWeightVertexState : byte
    {
        /// <summary>No body was found; it took the fallback bone.</summary>
        Fallback = 0,
        /// <summary>Copied from the body vertex at its place (or UV).</summary>
        Exact = 1,
        /// <summary>Copied from the closest point of the body.</summary>
        Matched = 2,
        /// <summary>Filled in over the cloth from the matched vertices.</summary>
        Inpainted = 3,
        /// <summary>Sampled (or partly sampled) from the weight field round the body.</summary>
        Field = 4,
        /// <summary>Fitted to the simulated poses.</summary>
        Fitted = 5,
        /// <summary>Kept as it was (locked by the caller).</summary>
        Locked = 6,
        /// <summary>No matched vertex is connected to it: it kept the closest point's weights.</summary>
        Unreached = 7
    }

    /// <summary>The settings for one weight transfer. The defaults suit UMA humanoids in metres.</summary>
    [Serializable]
    public sealed class UMAWeightSettings
    {
        public UMAWeightMethod Method = UMAWeightMethod.RobustInpaint;
        /// <summary>The most bones a vertex keeps (Unity's default skin quality is 4).</summary>
        [Range(1, 8)] public int MaxBones = 4;
        /// <summary>Weights below this share of a vertex's total are dropped.</summary>
        [Range(0f, 0.2f)] public float MinWeight = 0.01f;

        /// <summary>Exact Match: how close a vertex must be to a body vertex to copy it (m).</summary>
        public float ExactTolerance = 0.0002f;
        /// <summary>Exact Match by UV: the body point found in UV space must be this close in space too (m).</summary>
        public float ExactUvMaxDistance = 0.03f;
        /// <summary>Exact Match: also match vertices by UV when both meshes have UVs.</summary>
        public bool ExactUseUV = true;

        /// <summary>Robust Inpaint: cloth within this distance of the body may copy it (m).</summary>
        public float MatchDistance = 0.04f;
        /// <summary>Robust Inpaint: the cloth's normal and the body's must be within this angle (degrees).</summary>
        [Range(5f, 90f)] public float MatchAngleDegrees = 35f;
        /// <summary>Also accept a normal facing the other way (an inner layer, a facing, a double-sided mesh).</summary>
        public bool AllowFlippedNormals = true;
        /// <summary>Cloth deeper inside the body than this is not copied but filled in (m). Shallow poke-through still copies.</summary>
        public float InsideDepth = 0.015f;
        /// <summary>The cloth's normals are averaged over this many rings of neighbours before comparing (folds make raw normals noisy).</summary>
        [Range(0, 6)] public int NormalSmoothingRings = 2;
        public UMAInpaintEnergy Energy = UMAInpaintEnergy.Biharmonic;
        /// <summary>Smoothing passes over the filled region and its border, after the fill.</summary>
        [Range(0, 50)] public int SmoothIterations = 10;
        [Range(0f, 1f)] public float SmoothAlpha = 0.2f;

        /// <summary>Volumetric: the grid's cell size (m).</summary>
        public float FieldCellSize = 0.025f;
        /// <summary>Volumetric: how far round the clothes the grid reaches (m).</summary>
        public float FieldMargin = 0.25f;
        /// <summary>Volumetric: cloth this close to the body keeps the body's own weights (m).</summary>
        public float FieldAnchor = 0.02f;
        /// <summary>Volumetric: over how much further out the weights change to the field's (m).</summary>
        public float FieldBlend = 0.04f;
        /// <summary>Volumetric: run the field on the GPU when the machine can; the CPU gives the same result.</summary>
        public bool PreferGpu = true;

        /// <summary>Make the two sides match: each vertex averages with its mirror image, Left and Right bones swapped by name.</summary>
        public bool MirrorLeftRight;
        /// <summary>The mirror plane: the axis (0 = x, 1 = y, 2 = z) and its position along it.</summary>
        public int MirrorAxis;
        public float MirrorPlane;
        /// <summary>How close a vertex's mirror image must be to another vertex (m).</summary>
        public float MirrorTolerance = 0.01f;

        /// <summary>
        /// Body regions: for each region, which donor triangles it may copy from (null = all). A target vertex's
        /// region is <see cref="UMAWeightTarget.Region"/>. With no regions every vertex uses the whole body.
        /// </summary>
        public List<bool[]> RegionTriangles;

        /// <summary>Poses the character can be put in: for Auto's scoring, and with draped positions for Simulated Fit.</summary>
        [NonSerialized] public IList<UMAWeightPose> Poses;
        /// <summary>The methods Auto tries.</summary>
        public UMAWeightMethod[] AutoCandidates = { UMAWeightMethod.TightSurface, UMAWeightMethod.RobustInpaint, UMAWeightMethod.Volumetric };
        /// <summary>Simulated Fit: how strongly each vertex is drawn to its neighbours' weights.</summary>
        public float FitSmoothness = 1f;
        /// <summary>Simulated Fit: how strongly each vertex stays near its starting (Robust Inpaint) weights.</summary>
        public float FitPrior = 0.05f;
        /// <summary>Simulated Fit: rounds of fitting, each with the neighbours' new weights.</summary>
        [Range(1, 10)] public int FitIterations = 3;
        /// <summary>Scoring: cloth deeper in the posed body than this counts as poking through (m).</summary>
        public float ScoreInsideTolerance = 0.003f;

        /// <summary>
        /// Sets the mirror plane to a character's middle: the plane through its root, square to its right, in the
        /// space the donor and target are in (usually the renderer's).
        /// </summary>
        public void SetMirrorFrom(Transform space, Transform character)
        {
            if (space == null || character == null) return;
            Vector3 right = space.InverseTransformDirection(character.right);
            Vector3 a = new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z));
            MirrorAxis = a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
            MirrorPlane = space.InverseTransformPoint(character.position)[MirrorAxis];
        }

        public UMAWeightSettings Clone()
        {
            var c = (UMAWeightSettings)MemberwiseClone();
            c.AutoCandidates = AutoCandidates != null ? (UMAWeightMethod[])AutoCandidates.Clone() : null;
            c.RegionTriangles = RegionTriangles != null ? new List<bool[]>(RegionTriangles) : null;
            return c;
        }

        /// <summary>The method's name as the tools show it.</summary>
        public static string DisplayName(UMAWeightMethod method)
        {
            switch (method)
            {
                case UMAWeightMethod.Auto: return "Auto (try and score)";
                case UMAWeightMethod.ExactMatch: return "Exact Match";
                case UMAWeightMethod.TightSurface: return "Tight Surface";
                case UMAWeightMethod.RobustInpaint: return "Robust Inpaint";
                case UMAWeightMethod.Volumetric: return "Volumetric (loose)";
                case UMAWeightMethod.SimulatedFit: return "Simulated Fit";
            }
            return method.ToString();
        }

        /// <summary>One line on when to use the method, for tooltips.</summary>
        public static string Describe(UMAWeightMethod method)
        {
            switch (method)
            {
                case UMAWeightMethod.Auto: return "Weights the clothes Tight Surface, Robust Inpaint and Volumetric, bends the character into test poses, and keeps the one whose cloth pokes into the body and stretches least.";
                case UMAWeightMethod.ExactMatch: return "For clothes cut from the body mesh: each vertex at a body vertex's place, or at its UV, copies that vertex exactly. Others are weighted as Tight Surface.";
                case UMAWeightMethod.TightSurface: return "Every vertex copies the closest point of the body. For gloves, socks, leggings and other clothes that lie on the skin.";
                case UMAWeightMethod.RobustInpaint: return "Copies the body only where the cloth clearly lies on it (close, facing the same way, not deep inside it), and fills the rest in smoothly over the cloth. Right for most clothes.";
                case UMAWeightMethod.Volumetric: return "Spreads the body's weights through the space round it, so cloth far from the body (a long skirt between the legs, a cape, a loose part) blends the bones near it smoothly.";
                case UMAWeightMethod.SimulatedFit: return "Starts from Robust Inpaint, drapes the clothes on the character in several poses, and fits the weights that best reproduce how the cloth hangs. The best for loose clothes; the slowest.";
            }
            return string.Empty;
        }
    }

    /// <summary>
    /// The body weights are copied from: its surface (positions and triangles) and each vertex's bone weights.
    /// Bone indices are into one bone table (<see cref="BoneCount"/>); the transferred weights use the same table.
    /// </summary>
    public sealed class UMAWeightDonor
    {
        public Vector3[] Positions;
        /// <summary>Three vertex indices per triangle.</summary>
        public int[] Triangles;
        /// <summary>Per vertex, normalized. Null or empty for vertices no triangle uses.</summary>
        public BoneWeight1[][] Weights;
        public int BoneCount;
        /// <summary>Bone names (for Mirror Left/Right). Optional.</summary>
        public string[] BoneNames;
        /// <summary>Per vertex UVs, for Exact Match by UV. Optional.</summary>
        public Vector2[] UVs;
        /// <summary>The bone given to vertices no body is found for.</summary>
        public int FallbackBone;
        /// <summary>The bones' skin matrices in the pose the surface was captured in (bind space to this space). Needed for poses.</summary>
        public Matrix4x4[] SkinMatrices;

        private Vector3[] vertexNormals;

        /// <summary>The body's vertex normals, vertices at one place sharing one (battle buddies). Worked out once.</summary>
        public Vector3[] VertexNormals
        {
            get
            {
                if (vertexNormals == null || vertexNormals.Length != Positions.Length) vertexNormals = ComputeNormals(Positions, Triangles);
                return vertexNormals;
            }
        }

        /// <summary>Area-weighted vertex normals for a surface; vertices at the same place share one.</summary>
        public static Vector3[] ComputeNormals(Vector3[] positions, int[] triangles)
        {
            var normals = new Vector3[positions.Length];
            var key = new Dictionary<Vector3Int, int>();
            var group = new int[positions.Length];
            var used = new bool[positions.Length];
            for (int t = 0; t + 2 < triangles.Length; t += 3) { used[triangles[t]] = used[triangles[t + 1]] = used[triangles[t + 2]] = true; }
            for (int v = 0; v < positions.Length; v++)
            {
                if (!used[v]) { group[v] = v; continue; }
                Vector3Int k = UMAWeightGraph.PositionKey(positions[v]);
                if (key.TryGetValue(k, out int g)) group[v] = g; else { key[k] = v; group[v] = v; }
            }
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                normals[group[a]] += n; normals[group[b]] += n; normals[group[c]] += n;
            }
            for (int v = 0; v < positions.Length; v++)
            {
                Vector3 n = normals[group[v]];
                normals[v] = n.sqrMagnitude > 1e-24f ? n.normalized : Vector3.zero;
            }
            return normals;
        }

        /// <summary>
        /// A donor from a captured character surface (<see cref="UMASlotSkinTransfer.BuildTargetSurface"/>): bone
        /// indices are into <paramref name="skin"/>'s bones, as the slot builders use them.
        /// </summary>
        /// <param name="keep">Optional: which triangles to keep (for example, leave out the slot being weighted).</param>
        public static UMAWeightDonor FromSurface(UMASlotSkinTransfer.TargetSurface surface, UMASlotSkinTransfer.TargetData skin, int fallbackBone, Func<UMASlotSkinTransfer.TriangleInfo, bool> keep = null)
        {
            if (surface == null || surface.Triangles == null) throw new ArgumentNullException(nameof(surface));
            int vertexCount = surface.VertexWeights != null ? surface.VertexWeights.Length : 0;
            var donor = new UMAWeightDonor
            {
                Positions = new Vector3[vertexCount],
                Weights = new BoneWeight1[vertexCount][],
                BoneCount = skin != null && skin.Bones != null ? skin.Bones.Length : 0,
                FallbackBone = fallbackBone,
                SkinMatrices = surface.CurrentSkinMatrices
            };
            var triangles = new List<int>(surface.Triangles.Length * 3);
            foreach (UMASlotSkinTransfer.TriangleInfo t in surface.Triangles)
            {
                donor.Positions[t.Index0] = t.A; donor.Positions[t.Index1] = t.B; donor.Positions[t.Index2] = t.C;
                if (keep != null && !keep(t)) continue;
                triangles.Add(t.Index0); triangles.Add(t.Index1); triangles.Add(t.Index2);
            }
            donor.Triangles = triangles.ToArray();
            int maxBone = donor.BoneCount;
            for (int v = 0; v < vertexCount; v++)
            {
                List<BoneWeight1> w = surface.VertexWeights[v];
                donor.Weights[v] = w != null ? w.ToArray() : new BoneWeight1[0];
                foreach (BoneWeight1 bw in donor.Weights[v]) maxBone = Mathf.Max(maxBone, bw.boneIndex + 1);
            }
            donor.BoneCount = maxBone;
            if (skin != null && skin.Bones != null)
            {
                donor.BoneNames = new string[donor.BoneCount];
                for (int b = 0; b < skin.Bones.Length && b < donor.BoneCount; b++) donor.BoneNames[b] = skin.Bones[b] != null ? skin.Bones[b].name : null;
            }
            return donor;
        }

        /// <summary>
        /// Gives each vertex the UV it has in its own slot (not the character's atlas), for Exact Match by UV: clothes cut
        /// from a body slot keep that slot's UVs. <paramref name="surface"/> must be the one this donor was made from.
        /// </summary>
        public void SetSlotUVs(UMASlotSkinTransfer.TargetData skin, UMASlotSkinTransfer.TargetSurface surface)
        {
            SlotData[] slots = skin != null && skin.UmaData != null && skin.UmaData.umaRecipe != null ? skin.UmaData.umaRecipe.slotDataList : null;
            UVs = new Vector2[Positions.Length];
            if (slots == null || surface == null) return;
            var bases = new Dictionary<int, int>();
            SkinnedMeshRenderer[] renderers = skin.SurfaceRenderers != null && skin.SurfaceRenderers.Length > 0 ? skin.SurfaceRenderers : new[] { skin.Renderer };
            for (int r = 0; r < renderers.Length; r++) bases[r] = UMASlotSkinTransfer.RendererVertexBase(skin, renderers[r]);
            void Put(int index, int slotIndex, int rendererIndex)
            {
                if (slotIndex < 0 || slotIndex >= slots.Length || slots[slotIndex] == null || slots[slotIndex].asset == null) return;
                if (!bases.TryGetValue(rendererIndex, out int b) || b < 0) return;
                Vector2[] slotUv = slots[slotIndex].asset.meshData != null ? slots[slotIndex].asset.meshData.uv : null;
                int local = index - b - slots[slotIndex].vertexOffset;
                if (slotUv != null && local >= 0 && local < slotUv.Length && index < UVs.Length) UVs[index] = slotUv[local];
            }
            foreach (UMASlotSkinTransfer.TriangleInfo t in surface.Triangles)
            {
                Put(t.Index0, t.SlotIndex, t.RendererIndex);
                Put(t.Index1, t.SlotIndex, t.RendererIndex);
                Put(t.Index2, t.SlotIndex, t.RendererIndex);
            }
        }

        /// <summary>The same body with only some triangles (the vertex arrays are shared).</summary>
        public UMAWeightDonor WithTriangles(Func<int, bool> keepTriangle)
        {
            var kept = new List<int>(Triangles.Length);
            for (int t = 0; t * 3 + 2 < Triangles.Length; t++)
            {
                if (!keepTriangle(t)) continue;
                kept.Add(Triangles[t * 3]); kept.Add(Triangles[t * 3 + 1]); kept.Add(Triangles[t * 3 + 2]);
            }
            var copy = (UMAWeightDonor)MemberwiseClone();
            copy.Triangles = kept.ToArray();
            copy.vertexNormals = vertexNormals;
            return copy;
        }

        public int TriangleCount => Triangles != null ? Triangles.Length / 3 : 0;
    }

    /// <summary>The mesh that receives weights (the clothes), in the donor's space.</summary>
    public sealed class UMAWeightTarget
    {
        public Vector3[] Positions;
        /// <summary>Three vertex indices per triangle.</summary>
        public int[] Triangles;
        /// <summary>Optional normals; worked out from the triangles when null.</summary>
        public Vector3[] Normals;
        /// <summary>Optional UVs, for Exact Match by UV.</summary>
        public Vector2[] UVs;
        /// <summary>
        /// Optional: vertices that are one point (sewn seams), as a representative vertex index per vertex.
        /// Vertices at the same position are always one point (battle buddies) whether listed or not.
        /// </summary>
        public int[] Groups;
        /// <summary>Optional weights the vertices have now (donor bone indices), for <see cref="Locked"/>.</summary>
        public BoneWeight1[][] CurrentWeights;
        /// <summary>Optional: vertices that keep <see cref="CurrentWeights"/>. Locked vertices are fixed points for the fill.</summary>
        public bool[] Locked;
        /// <summary>Optional: each vertex's region (index into <see cref="UMAWeightSettings.RegionTriangles"/>).</summary>
        public int[] Region;

        public int VertexCount => Positions != null ? Positions.Length : 0;
    }

    /// <summary>
    /// One pose of the character: how each bone moves from the pose the donor was captured in, where the body's
    /// vertices are then, and optionally where the clothes hang then (simulated).
    /// </summary>
    public sealed class UMAWeightPose
    {
        public string Name;
        /// <summary>Per donor bone: from the capture pose's space to this pose's space.</summary>
        public Matrix4x4[] BoneMatrices;
        /// <summary>The donor's vertices in this pose (same order as <see cref="UMAWeightDonor.Positions"/>).</summary>
        public Vector3[] DonorPositions;
        /// <summary>Optional: the target's vertices draped in this pose, for <see cref="UMAWeightMethod.SimulatedFit"/>.</summary>
        public Vector3[] TargetPositions;

        internal UMAWeightSurface ScoringSurface;
        internal Vector3[] ScoringNormals;
    }

    /// <summary>How well a set of weights does over the poses: lower is better.</summary>
    public struct UMAWeightScore
    {
        public UMAWeightMethod Method;
        /// <summary>Share of cloth vertices inside the posed body, averaged over the poses.</summary>
        public float InsideShare;
        /// <summary>The deepest a vertex went into the body in any pose (m).</summary>
        public float MaxInsideDepth;
        /// <summary>Mean edge stretch or squash over the poses (0.05 = 5%).</summary>
        public float MeanStretch;
        /// <summary>The most any edge stretched or squashed in any pose.</summary>
        public float MaxStretch;
        public int Poses;
        /// <summary>The number Auto compares: poke-through counts ten times stretch.</summary>
        public float Total => InsideShare * 10f + MeanStretch;

        public override string ToString() =>
            UMAWeightSettings.DisplayName(Method) + ": " + (InsideShare * 100f).ToString("0.0") + "% inside (deepest " + (MaxInsideDepth * 1000f).ToString("0") + " mm), stretch " + (MeanStretch * 100f).ToString("0.0") + "% mean, " + (MaxStretch * 100f).ToString("0") + "% most";
    }

    /// <summary>What a transfer did, for the tools to show.</summary>
    public sealed class UMAWeightReport
    {
        public UMAWeightMethod Requested;
        /// <summary>The method whose weights were kept (Auto's choice, or the requested one).</summary>
        public UMAWeightMethod Used;
        /// <summary>Per target vertex.</summary>
        public UMAWeightVertexState[] States;
        /// <summary>Per target vertex: deeper than <see cref="UMAWeightSettings.InsideDepth"/> inside the body.</summary>
        public bool[] DeepInside;
        public int Exact, Matched, Inpainted, Field, Fitted, Locked, Unreached, Fallback, Inside;
        public bool UsedGpu;
        public readonly List<UMAWeightScore> Scores = new List<UMAWeightScore>();
        public readonly List<string> Messages = new List<string>();
        public readonly List<string> Timings = new List<string>();

        internal void Count()
        {
            Exact = Matched = Inpainted = Field = Fitted = Locked = Unreached = Fallback = Inside = 0;
            if (States == null) return;
            for (int v = 0; v < States.Length; v++)
            {
                switch (States[v])
                {
                    case UMAWeightVertexState.Exact: Exact++; break;
                    case UMAWeightVertexState.Matched: Matched++; break;
                    case UMAWeightVertexState.Inpainted: Inpainted++; break;
                    case UMAWeightVertexState.Field: Field++; break;
                    case UMAWeightVertexState.Fitted: Fitted++; break;
                    case UMAWeightVertexState.Locked: Locked++; break;
                    case UMAWeightVertexState.Unreached: Unreached++; break;
                    default: Fallback++; break;
                }
                if (DeepInside != null && DeepInside[v]) Inside++;
            }
        }

        /// <summary>One line for the tools: what the vertices got.</summary>
        public string Summary()
        {
            var parts = new List<string>();
            void Add(int n, string what) { if (n > 0) parts.Add(n + " " + what); }
            Add(Exact, "exact");
            Add(Matched, "copied from the body");
            Add(Inpainted, "filled in");
            Add(Field, "from the field");
            Add(Fitted, "fitted to the poses");
            Add(Locked, "kept");
            Add(Unreached, "not connected to the body (closest point kept)");
            Add(Fallback, "with no body near (fallback bone)");
            string s = UMAWeightSettings.DisplayName(Used) + (Requested == UMAWeightMethod.Auto ? " (chosen by Auto)" : "") + ": " + string.Join(", ", parts) + ".";
            if (Inside > 0) s += " " + Inside + " vertices were deep inside the body.";
            return s;
        }
    }

    /// <summary>The transferred weights: per vertex counts and the weights, strongest first, in the donor's bone indices.</summary>
    public sealed class UMAWeightResult
    {
        public byte[] BonesPerVertex;
        public BoneWeight1[] BoneWeights;
        public UMAWeightReport Report;

        /// <summary>The weights of one vertex.</summary>
        public BoneWeight1[] VertexWeights(int vertex)
        {
            int offset = 0;
            for (int v = 0; v < vertex; v++) offset += BonesPerVertex[v];
            var w = new BoneWeight1[BonesPerVertex[vertex]];
            Array.Copy(BoneWeights, offset, w, 0, w.Length);
            return w;
        }

        /// <summary>All vertices' weights as one array per vertex.</summary>
        public BoneWeight1[][] PerVertex()
        {
            var result = new BoneWeight1[BonesPerVertex.Length][];
            int offset = 0;
            for (int v = 0; v < BonesPerVertex.Length; v++)
            {
                result[v] = new BoneWeight1[BonesPerVertex[v]];
                Array.Copy(BoneWeights, offset, result[v], 0, BonesPerVertex[v]);
                offset += BonesPerVertex[v];
            }
            return result;
        }

        /// <summary>The weights in the form the slot builders take.</summary>
        public UMASlotSkinTransfer.WeightTransferResult ToTransferResult() => new UMASlotSkinTransfer.WeightTransferResult
        {
            BonesPerVertex = BonesPerVertex,
            BoneWeights = BoneWeights,
            FallbackVertices = Report != null ? Report.Fallback : 0
        };
    }
}
