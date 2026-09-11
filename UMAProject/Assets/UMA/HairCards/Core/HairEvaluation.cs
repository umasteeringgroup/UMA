using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    [Serializable]
    public struct HairCurvePoint
    {
        public Vector3 position;
        public float width;
        public float roll;
        public float widthBaseline;
        public float profileScale;
        public float stiffness;
        public float freeze;

        public HairCurvePoint(Vector3 position, float width, float roll, float widthBaseline = -1f, float profileScale = 1f, float stiffness = 0f, float freeze = 0f)
        {
            this.position = position;
            this.width = width;
            this.roll = roll;
            this.widthBaseline = widthBaseline;
            this.profileScale = profileScale;
            this.stiffness = stiffness;
            this.freeze = freeze;
        }
    }

    public sealed class HairEvaluatedCurve
    {
        public string curveId;
        public string parentGuideId;
        public string groupId;
        public bool isChild;
        public int seed;
        internal int childOrdinal;
        public Color groupColor;
        public Vector3 rootNormal = Vector3.up;
        public float rootEmbedDepth;
        public HairCardProfileAsset profile;
        public HairAtlasProfileAsset atlas;
        public HairAtlasRegionSelectionMode atlasRegionSelection;
        public string[] atlasRegionIds = Array.Empty<string>();
        public int samplesPerCardOverride;
        public int tubeSidesOverride;
        public readonly List<HairCurvePoint> points;
        public HairEvaluatedCurve() : this(0) { }
        public HairEvaluatedCurve(int pointCapacity) => points = new List<HairCurvePoint>(Mathf.Max(0, pointCapacity));

        public float Length => HairCurveUtility.CalculateLength(points);

        public HairEvaluatedCurve Clone(string newCurveId = null)
        {
            HairEvaluatedCurve clone = new HairEvaluatedCurve(points.Count);
            CopyTo(clone, newCurveId);
            return clone;
        }

        /// <summary>Copy a snapshot into caller-owned storage without allocating another curve.</summary>
        public void CopyTo(HairEvaluatedCurve target, string newCurveId = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (ReferenceEquals(this, target)) throw new ArgumentException("Copy destination must be a different curve.", nameof(target));
            target.curveId = newCurveId ?? curveId; target.parentGuideId = parentGuideId; target.groupId = groupId;
            target.isChild = isChild; target.seed = seed; target.groupColor = groupColor;
            target.childOrdinal = childOrdinal;
            target.rootNormal = rootNormal; target.rootEmbedDepth = rootEmbedDepth;
            target.profile = profile; target.atlas = atlas; target.atlasRegionSelection = atlasRegionSelection;
            int regionCount = atlasRegionIds?.Length ?? 0;
            if (target.atlasRegionIds == null || target.atlasRegionIds.Length != regionCount ||
                (regionCount > 0 && ReferenceEquals(target.atlasRegionIds, atlasRegionIds)))
                target.atlasRegionIds = regionCount == 0 ? Array.Empty<string>() : new string[regionCount];
            if (regionCount > 0) Array.Copy(atlasRegionIds, target.atlasRegionIds, regionCount);
            target.samplesPerCardOverride = samplesPerCardOverride; target.tubeSidesOverride = tubeSidesOverride;
            target.points.Clear(); target.points.AddRange(points);
        }
    }

    /// <summary>
    /// Caller-owned reusable evaluation scratch. Results never alias these buffers. Use one per
    /// stage/caller (not concurrently), and Clear when closing or releasing a large groom.
    /// Source mesh channels are read once per evaluation so in-place edits cannot leave stale data.
    /// </summary>
    public sealed class HairEvaluationWorkspace
    {
        internal readonly HairGroomEvaluator.SourceMeshReadCache sourceMesh = new HairGroomEvaluator.SourceMeshReadCache();
        internal readonly HairGroomEvaluator.SourceMeshReadCache gravitySurface = new HairGroomEvaluator.SourceMeshReadCache();
        internal HairEvaluationOptions options;
        internal readonly HairChildGenerator.GenerationWorkspace children = new HairChildGenerator.GenerationWorkspace();
        internal readonly List<HairEvaluatedCurve> groupGuides = new List<HairEvaluatedCurve>();
        internal readonly List<HairCurvePoint> resampled = new List<HairCurvePoint>();
        internal float[] cumulative = Array.Empty<float>();
        internal HairCurvePoint[] smoothing = Array.Empty<HairCurvePoint>();
        internal readonly List<Vector3> modifierOriginal = new List<Vector3>();
        internal readonly List<Vector3> modifierTarget = new List<Vector3>();
        internal readonly List<HairGuidePoint> modifierControls = new List<HairGuidePoint>();
        internal readonly List<HairCurvePoint> helperCurve = new List<HairCurvePoint>();
        internal Vector3 groupClumpTip;
        internal bool inUse;

        public void Clear()
        {
            if (inUse) throw new InvalidOperationException("Cannot clear an active hair evaluation workspace.");
            sourceMesh.Clear(); gravitySurface.Clear(); children.Clear(); groupGuides.Clear(); groupGuides.Capacity = 0;
            resampled.Clear(); resampled.Capacity = 0; cumulative = Array.Empty<float>(); smoothing = Array.Empty<HairCurvePoint>();
            modifierOriginal.Clear(); modifierOriginal.Capacity = 0; modifierTarget.Clear(); modifierTarget.Capacity = 0;
            modifierControls.Clear(); modifierControls.Capacity = 0; helperCurve.Clear(); helperCurve.Capacity = 0;
        }
    }

    public sealed class HairEvaluationOptions
    {
        // Direction conversion only: output curves stay in source-local coordinates. A posed
        // editor supplies the same per-root matrix used to display its guides/cards.
        public Matrix4x4 sourceToWorld = Matrix4x4.identity;
        public Func<string, Matrix4x4> guideToSourcePose;
        public Mesh gravityCollisionMesh;
        public Vector3? worldGravity;
        public int lodLevel;
        public bool includeGuideCards = true;
        public bool includeChildren = true;
        public bool applySculptLayers = true;
        public bool applyModifiers = true;
        public bool applyConstraints = true;
        public bool evaluateSurfaceAnchors = true;
        public bool includeHiddenGroups = true;
        public int interactiveSampleLimit;
        public int previewSampleCount;
        public ISet<string> includedGuideIds;
        public string soloLayerId;
    }

    public sealed class HairEvaluationResult
    {
        private List<HairEvaluatedCurve> reusableCurves;
        private int nextCurve;

        internal void BeginUpdate()
        {
            reusableCurves ??= new List<HairEvaluatedCurve>();
            nextCurve = 0;
            curves.Clear(); evaluatedGuides.Clear(); warnings.Clear();
            guideCurveCount = childCurveCount = rejectedCurveCount = revision = 0;
        }

        internal HairEvaluatedCurve RentCurve(int pointCapacity)
        {
            if (reusableCurves == null) return new HairEvaluatedCurve(pointCapacity);
            if (nextCurve == reusableCurves.Count) reusableCurves.Add(new HairEvaluatedCurve(pointCapacity));
            HairEvaluatedCurve curve = reusableCurves[nextCurve++];
            curve.points.Clear();
            return curve;
        }

        internal void EndUpdate()
        {
            // Release removed guides/cards instead of pinning the largest groom forever.
            if (reusableCurves != null && nextCurve < reusableCurves.Count)
                reusableCurves.RemoveRange(nextCurve, reusableCurves.Count - nextCurve);
        }
        public readonly List<HairEvaluatedCurve> evaluatedGuides = new List<HairEvaluatedCurve>();
        public readonly List<HairEvaluatedCurve> curves = new List<HairEvaluatedCurve>();
        public readonly List<string> warnings = new List<string>();
        public int guideCurveCount;
        public int childCurveCount;
        public int rejectedCurveCount;
        public int revision;

        public int CardCount => curves.Count;
    }

    public sealed class HairCardSpan
    {
        public HairEvaluatedCurve curve;
        public string uvSetId;
        public int vertexStart;
        public int vertexCount;
        public int submesh;
        public int triangleStart;
        public int triangleCount;
    }

    public sealed class HairCardMeshBuildResult : IDisposable
    {
        public Mesh mesh;
        public readonly List<Material> materials = new List<Material>();
        // One atlas per submesh: profiles can share a shader material but override its textures.
        public readonly List<HairAtlasProfileAsset> atlases = new List<HairAtlasProfileAsset>();
        // Parallel to materials/atlases. Primary submeshes stay first to preserve editing spans.
        public readonly List<bool> secondPasses = new List<bool>();
        public readonly List<string> materialNames = new List<string>();
        public int cardCount;
        public int vertexCount;
        public int triangleCount;
        public int degenerateTriangleCount;
        public int frameFlipCount;
        public readonly List<HairCardSpan> cards = new List<HairCardSpan>();
        public readonly List<Vector2> localUvs = new List<Vector2>();

        public bool MaterialPassLayoutMatches()
        {
            int expected = 0, actual = 0;
            for (int i = 0; i < atlases.Count; i++)
            {
                if (i < secondPasses.Count && secondPasses[i]) { actual++; continue; }
                if (atlases[i] != null && atlases[i].secondPassMaterial != null) expected++;
            }
            return expected == actual && AllSecondPassesAssigned();
        }

        private bool AllSecondPassesAssigned()
        {
            for (int i = 0; i < secondPasses.Count; i++)
                if (secondPasses[i] && (atlases[i] == null || atlases[i].secondPassMaterial == null)) return false;
            return true;
        }

        public void Dispose()
        {
            if (mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
            mesh = null;
        }
    }

    internal struct HairDeterministicRandom
    {
        private uint state;

        public HairDeterministicRandom(int seed)
        {
            state = (uint)seed;
            if (state == 0) state = 0x9e3779b9u;
        }

        public uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        public float Next01()
        {
            return (NextUInt() & 0x00ffffffu) / 16777215f;
        }

        public float NextSigned()
        {
            return Next01() * 2f - 1f;
        }

        public Vector2 NextInUnitDisk()
        {
            float radius = Mathf.Sqrt(Next01());
            float angle = Next01() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
    }
}
