using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public static class HairStableId
    {
        public static string Create()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static bool Ensure(ref string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = Create();
            return true;
        }
    }

    public enum HairGroupRole
    {
        Coverage,
        Mid,
        Detail,
        Flyaway,
        ShortHair,
        FacialHair,
        Brows,
        Lashes,
        Custom
    }

    public enum HairMapKind
    {
        GrowthArea,
        Density,
        FlowX,
        FlowY,
        Length,
        Lift,
        Width,
        Clump,
        ChildCount,
        ProfileBlend,
        LodImportance,
        Custom
    }

    public enum HairGuideInterpolationMode
    {
        Nearest,
        WeightedNearest,
        RegionBarycentric,
        FlowAligned,
        ClumpParent,
        ExplicitParent
    }

    public enum HairCardShape
    {
        Ribbon,
        TaperedTube
    }

    public enum HairAtlasRegionSelectionMode
    {
        All,
        Selected
    }

    public enum HairModifierDomain
    {
        Guides,
        Children,
        GuidesAndChildren
    }

    public enum HairModifierType
    {
        Resample,
        Simplify,
        Length,
        Width,
        Smooth,
        FlowAlign,
        Lift,
        Clump,
        Part,
        Curl,
        Wave,
        Noise,
        Twist,
        Gravity,
        HelperFollow,
        SurfaceProjection,
        Collision,
        PushOut,
        Mirror,
        TrimByMesh,
        LodReduction,
        SplineFlow,
        Ringlets,
        SurfaceBend,
        Gather
    }

    public enum HairLiftNormalMode { RootNormal, ClosestSurfaceNormal }

    public enum HairHelperType
    {
        CurveRail,
        Chain,
        PartLine,
        Surface,
        GuideGrid,
        SculptCage,
        Attractor,
        Repulsor,
        Plane,
        CollisionMesh,
        Sphere,
        Capsule,
        Box,
        VolumeTarget,
        BraidRail,
        BindingRing,
        Symmetry,
        BoneChainPreview,
        Gather,
        Bun
    }

    public enum HairConstraintType
    {
        AttachRoot,
        FollowCurve,
        TrackTip,
        ConformToSurface,
        CageDeform,
        Attract,
        Repel,
        MaintainDistance,
        Aim,
        MatchRoll,
        PreserveLength,
        Collision,
        Mirror
    }

    public enum HairConstraintEvaluation
    {
        Live,
        Cached,
        Baked
    }

    public enum HairSculptBlendMode
    {
        Additive,
        Override
    }

    public enum HairLodReductionMode
    {
        Regenerate,
        RemoveByImportance,
        MergeGroups,
        Impostor
    }

    [Serializable]
    public struct HairSurfaceAnchor
    {
        [SerializeField] private string sourceMeshId;
        [SerializeField] private int submeshIndex;
        [SerializeField] private int triangleIndex;
        [SerializeField] private Vector3 barycentric;
        [SerializeField] private float normalOffset;
        [SerializeField] private Vector3 cachedLocalPosition;
        [SerializeField] private Vector3 cachedLocalNormal;

        public string SourceMeshId => sourceMeshId;
        public int SubmeshIndex => submeshIndex;
        public int TriangleIndex => triangleIndex;
        public Vector3 Barycentric => barycentric;
        public float NormalOffset => normalOffset;
        public Vector3 CachedLocalPosition => cachedLocalPosition;
        public Vector3 CachedLocalNormal => cachedLocalNormal;

        public bool IsValid => !string.IsNullOrEmpty(sourceMeshId) && submeshIndex >= 0 &&
                               triangleIndex >= 0 && IsFinite(barycentric);

        public static HairSurfaceAnchor Create(
            string meshId,
            int sourceSubmeshIndex,
            int sourceTriangleIndex,
            Vector3 sourceBarycentric,
            float sourceNormalOffset,
            Vector3 localPosition,
            Vector3 localNormal)
        {
            float sum = sourceBarycentric.x + sourceBarycentric.y + sourceBarycentric.z;
            Vector3 normalized = Mathf.Abs(sum) > 1e-6f ? sourceBarycentric / sum : new Vector3(1f, 0f, 0f);
            return new HairSurfaceAnchor
            {
                sourceMeshId = meshId,
                submeshIndex = sourceSubmeshIndex,
                triangleIndex = sourceTriangleIndex,
                barycentric = normalized,
                normalOffset = sourceNormalOffset,
                cachedLocalPosition = localPosition,
                cachedLocalNormal = localNormal.sqrMagnitude > 1e-8f ? localNormal.normalized : Vector3.up
            };
        }

        public void SetCachedPose(Vector3 position, Vector3 normal)
        {
            cachedLocalPosition = position;
            cachedLocalNormal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }

    [Serializable]
    public sealed class HairGuidePoint
    {
        public Vector3 position;
        [Min(0f)] public float width = 0.01f;
        // Original automatic guide taper; negative means infer it for a legacy guide.
        public float widthBaseline = -1f;
        public float roll;
        [Range(0f, 1f)] public float stiffness;
        [Range(0f, 1f)] public float freeze;
        [Min(0f)] public float profileScale = 1f;

        public HairGuidePoint Clone()
        {
            return new HairGuidePoint
            {
                position = position,
                width = width,
                widthBaseline = widthBaseline,
                roll = roll,
                stiffness = stiffness,
                freeze = freeze,
                profileScale = profileScale
            };
        }
    }

    [Serializable]
    public sealed class HairGuide
    {
        [SerializeField] private string id;
        public string name = "Guide";
        public bool enabled = true;
        public HairSurfaceAnchor root;
        public List<HairGuidePoint> points = new List<HairGuidePoint>();
        public bool overrideChildCount;
        [Min(0)] public int childCount;
        public bool includeGuideCard = true;
        public int seed;
        [Range(0f, 1f)] public float lodImportance = 1f;

        public string Id => id;

        public void EnsureIntegrity(float defaultWidth)
        {
            HairStableId.Ensure(ref id);
            points ??= new List<HairGuidePoint>();
            while (points.Count < 2)
            {
                Vector3 position = points.Count == 0
                    ? root.CachedLocalPosition
                    : points[0].position + root.CachedLocalNormal * 0.1f;
                points.Add(new HairGuidePoint { position = position, width = defaultWidth });
            }

            for (int i = 0; i < points.Count; i++)
            {
                points[i] ??= new HairGuidePoint { width = defaultWidth };
                points[i].width = Mathf.Max(0f, points[i].width);
                points[i].profileScale = Mathf.Max(0f, points[i].profileScale);
            }

            childCount = Mathf.Max(0, childCount);
            lodImportance = Mathf.Clamp01(lodImportance);
        }

        public float GetWidthBaseline(int pointIndex)
        {
            HairGuidePoint point = points[pointIndex];
            if (point.widthBaseline >= 0f) return point.widthBaseline;
            // Older guides baked a linear root-to-tip width into their control points.
            // Keep departures from that line as authored width, without rewriting the asset.
            return Mathf.Lerp(points[0].width, points[points.Count - 1].width,
                pointIndex / Mathf.Max(1f, points.Count - 1f));
        }

        public HairGuide Clone(bool createNewId = true)
        {
            HairGuide clone = new HairGuide
            {
                id = createNewId ? HairStableId.Create() : id,
                name = name,
                enabled = enabled,
                root = root,
                overrideChildCount = overrideChildCount,
                childCount = childCount,
                includeGuideCard = includeGuideCard,
                seed = seed,
                lodImportance = lodImportance,
                points = new List<HairGuidePoint>(points?.Count ?? 0)
            };
            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    clone.points.Add(points[i]?.Clone() ?? new HairGuidePoint());
                }
            }

            return clone;
        }
    }

    [Serializable]
    public sealed class HairGrowthMap
    {
        [SerializeField] private string id;
        public string name = "Growth / Density";
        public HairMapKind kind = HairMapKind.GrowthArea;
        public bool visible = true;
        public bool locked;
        public float defaultValue;
        public Vector2 valueRange = new Vector2(0f, 1f);
        public float[] values = Array.Empty<float>();
        public HairMapStorage storage;
        public HairTextureMap texture;
        public bool UsesTexture => storage == HairMapStorage.Texture && texture != null;
        public int TextureRevision => UsesTexture ? texture.Revision : 0;

        public string Id => id;
        public string DisplayName => kind == HairMapKind.GrowthArea ? "Growth / Density" :
            kind == HairMapKind.Density ? "Density Multiplier (optional)" : name;

        public void EnsureIntegrity(int vertexCount)
        {
            HairStableId.Ensure(ref id);
            // Negative means the source is temporarily unavailable, not an empty mesh.
            int count = vertexCount < 0 ? values?.Length ?? 0 : vertexCount;
            if (values == null || values.Length != count)
            {
                float[] resized = new float[count];
                Array.Fill(resized, defaultValue);
                if (values != null)
                {
                    Array.Copy(values, resized, Mathf.Min(values.Length, resized.Length));
                }
                values = resized;
            }

            if (valueRange.x > valueRange.y)
            {
                float minimum = valueRange.y;
                valueRange.y = valueRange.x;
                valueRange.x = minimum;
            }
        }

        public float SampleVertex(int index)
        {
            if (UsesTexture && texture.TryCorner(index, out float value)) return value;
            return BaseVertex(index);
        }

        public float BaseVertex(int index)
        {
            if (values == null || (uint)index >= (uint)values.Length)
            {
                return defaultValue;
            }
            return values[index];
        }

        public float SampleTriangle(int submesh, int triangle, int a, int b, int c, Vector3 barycentric)
        {
            if (UsesTexture)
            {
                var tile = texture.Find(submesh, triangle);
                if (texture.Valid(tile) && tile.a == a && tile.b == b && tile.c == c)
                    return texture.Sample(tile, barycentric);
            }
            return BaseVertex(a) * barycentric.x + BaseVertex(b) * barycentric.y + BaseVertex(c) * barycentric.z;
        }
    }

    [Serializable]
    public sealed class HairGuideDelta
    {
        public string guideId;
        public Vector3[] positionOffsets = Array.Empty<Vector3>();
        public float[] widthOffsets = Array.Empty<float>();
        public float[] rollOffsets = Array.Empty<float>();
    }

    [Serializable]
    public sealed class HairSculptLayer
    {
        [SerializeField] private string id;
        public string name = "Sculpt Layer";
        public bool visible = true;
        public bool locked;
        [Range(0f, 1f)] public float opacity = 1f;
        public HairSculptBlendMode blendMode;
        // Finishing passes evaluate after legacy group modifiers and helper constraints.
        // Within each section the authored list order is retained.
        public bool afterGroupOperations;
        public string maskMapId;
        public List<HairGuideDelta> deltas = new List<HairGuideDelta>();
        public List<HairModifierSettings> modifiers = new List<HairModifierSettings>();

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            deltas ??= new List<HairGuideDelta>();
            modifiers ??= new List<HairModifierSettings>();
            foreach (HairModifierSettings modifier in modifiers) modifier?.EnsureIntegrity();
            opacity = Mathf.Clamp01(opacity);
        }
    }

    [Serializable]
    public sealed class HairModifierSettings
    {
        public HairGatherSettings gather = new HairGatherSettings();
        [SerializeField] private string id;
        public string name = "Modifier";
        public HairModifierType type;
        public HairModifierDomain domain;
        public bool enabled = true;
        [Range(0f, 1f)] public float weight = 1f;
        public float amount = 1f;
        public Vector3 vector = Vector3.up;
        public int seed;
        public string maskMapId;
        public AnimationCurve rootToTip = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public HairModifierMask mask = new HairModifierMask();
        public HairRingletSettings ringlets = new HairRingletSettings();
        [Min(0.01f)] public float noiseFrequency = 18f;
        [Range(0f, 1f)] public float noiseParentCoherence;
        // Opt-in: retain the original fixed-root, three-axis Noise for existing grooms.
        public bool strandAlignedNoise;
        [Min(0f)] public float noiseNormalAmplitude = 0.001f;
        [Range(0f, 1f)] public float noiseRootFade = 0.2f;
        public AnimationCurve bendProfile = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        public string helperId;

        public string Id => id;

        [Min(0.001f)] public float clumpRadius = 0.03f;
        [Range(0f, 1f)] public float rootInfluence = 1f;
        public HairLiftNormalMode liftNormalMode = HairLiftNormalMode.RootNormal;
        [Min(0f)] public float gravityStrength = 2.5f;
        [Range(0f, 1f)] public float gravitySeparation = 0.1f;
        public bool gravityCollision = true;
        [Range(0f, 0.05f)] public float gravityClearance = 0.002f;
        public Vector3 gravityDirection = Vector3.down;
        public bool useWorldGravity = true;

        // Surface paths belong to this modifier, not to the generated guides/cards.
        public List<HairFlowSpline> flowSplines = new List<HairFlowSpline>();
        [Min(0.001f)] public float flowRadius = 0.15f;
        [Range(1, 4)] public int flowNeighbors = 4;
        [Range(0f, 1f)] public float flowDirection = 1f;
        [Range(0f, 1f)] public float flowFacing = 1f;
        [Range(-180f, 180f)] public float flowBank;
        [Range(0f, 80f)] public float flowLift = 10f;
        [Range(0.001f, 0.05f)] public float flowPointSpacing = 0.008f;
        public bool flowMirrorDrawing;
        public bool flowShowPaths = true;

        // Evaluation copies share the read-only ramp; authoring duplicates own their keys and ID.
        internal HairModifierSettings WithLayerOpacity(float opacity)
        {
            HairModifierSettings copy = (HairModifierSettings)MemberwiseClone();
            copy.weight *= opacity;
            return copy;
        }

        public HairModifierSettings Duplicate()
        {
            HairModifierSettings copy = (HairModifierSettings)MemberwiseClone();
            copy.id = null;
            copy.ringlets = ringlets?.Duplicate() ?? new HairRingletSettings();
            copy.gather = JsonUtility.FromJson<HairGatherSettings>(JsonUtility.ToJson(gather ?? new HairGatherSettings()));
            copy.rootToTip = rootToTip == null ? null : new AnimationCurve(rootToTip.keys)
            { preWrapMode = rootToTip.preWrapMode, postWrapMode = rootToTip.postWrapMode };
            copy.bendProfile = bendProfile == null ? null : new AnimationCurve(bendProfile.keys)
            { preWrapMode = bendProfile.preWrapMode, postWrapMode = bendProfile.postWrapMode };
            copy.flowSplines = new List<HairFlowSpline>();
            copy.mask = mask == null ? new HairModifierMask() : JsonUtility.FromJson<HairModifierMask>(JsonUtility.ToJson(mask));
            if (flowSplines != null)
                foreach (HairFlowSpline spline in flowSplines)
                    if (spline != null) copy.flowSplines.Add(spline.Duplicate());
            copy.EnsureIntegrity();
            return copy;
        }

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            rootToTip ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
            gather ??= new HairGatherSettings(); gather.EnsureIntegrity();
            mask ??= new HairModifierMask();
            ringlets ??= new HairRingletSettings();
            ringlets.EnsureIntegrity();
            mask.remap ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
            noiseFrequency = float.IsFinite(noiseFrequency) ? Mathf.Max(0.01f, noiseFrequency) : 18f;
            noiseParentCoherence = Mathf.Clamp01(noiseParentCoherence);
            noiseNormalAmplitude = float.IsFinite(noiseNormalAmplitude) ? Mathf.Max(0f, noiseNormalAmplitude) : 0f;
            noiseRootFade = float.IsFinite(noiseRootFade) ? Mathf.Clamp01(noiseRootFade) : 0.2f;
            bendProfile ??= AnimationCurve.Linear(0f, 1f, 1f, 0f);
            weight = Mathf.Clamp01(weight);
            rootInfluence = float.IsFinite(rootInfluence) ? Mathf.Clamp01(rootInfluence) : 0f;
            gravityStrength = float.IsFinite(gravityStrength) ? Mathf.Max(0f, gravityStrength) : 0f;
            gravitySeparation = float.IsFinite(gravitySeparation) ? Mathf.Clamp01(gravitySeparation) : 0f;
            gravityClearance = float.IsFinite(gravityClearance) ? Mathf.Clamp(gravityClearance, 0f, 0.05f) : 0f;
            flowSplines ??= new List<HairFlowSpline>();
            foreach (HairFlowSpline spline in flowSplines) spline?.EnsureIntegrity();
            flowRadius = float.IsFinite(flowRadius) ? Mathf.Max(0.001f, flowRadius) : 0.15f;
            flowNeighbors = Mathf.Clamp(flowNeighbors, 1, 4);
            flowDirection = float.IsFinite(flowDirection) ? Mathf.Clamp01(flowDirection) : 0f;
            flowFacing = float.IsFinite(flowFacing) ? Mathf.Clamp01(flowFacing) : 0f;
            flowBank = float.IsFinite(flowBank) ? Mathf.Clamp(flowBank, -180f, 180f) : 0f;
            flowLift = float.IsFinite(flowLift) ? Mathf.Clamp(flowLift, 0f, 80f) : 0f;
            flowPointSpacing = float.IsFinite(flowPointSpacing) ? Mathf.Clamp(flowPointSpacing, 0.001f, 0.05f) : 0.008f;
        }
    }

    [Serializable]
    public sealed class HairConstraintSettings
    {
        [SerializeField] private string id;
        public string name = "Constraint";
        public HairConstraintType type;
        public string helperId;
        public bool enabled = true;
        [Range(0f, 1f)] public float weight = 1f;
        public AnimationCurve rootToTip = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public string maskMapId;
        public Vector3 offset;
        public float twist;
        public bool localAlignment = true;
        public bool preserveLength = true;
        public HairConstraintEvaluation evaluation;

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            rootToTip ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
            weight = Mathf.Clamp01(weight);
        }
    }

    [Serializable]
    public sealed class HairHelper
    {
        public HairGatherTarget gather = new HairGatherTarget();
        public HairBunSettings bun = new HairBunSettings();
        public HairRailSettings rail = new HairRailSettings();
        internal static HairHelper Derived(string stableId) => new HairHelper { id = stableId };
        [SerializeField] private string id;
        public string name = "Helper";
        public HairHelperType type = HairHelperType.CurveRail;
        public bool visible = true;
        public bool locked;
        public bool embedded = true;
        public string externalGlobalId;
        public string externalHelperId;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public float radius = 0.1f;
        public Vector3 size = Vector3.one;
        public List<Vector3> points = new List<Vector3>();
        [Range(2, 16)] public int gridColumns = 3;
        [Range(2, 32)] public int gridRows = 5;
        [Range(.005f, .2f)] public float coilRadius = .05f;
        [Range(.5f, 3f)] public float coilTurns = 1.5f;
        [Range(-.1f, .1f)] public float coilRise = .035f;
        [Range(.1f, 3f)] public float braidScale = 1f;

        // External transforms can include reflection/shear from scaled parents or an authoring
        // pose. A TRS decomposition cannot represent these exactly. Persist the source-local
        // snapshot so editor, bake and runtime helper collisions use the same volume.
        [SerializeField] private bool hasExternalSourceTransform;
        [SerializeField] private Matrix4x4 externalSourceTransform = Matrix4x4.identity;
        public bool HasExternalSourceTransform => !embedded && hasExternalSourceTransform;
        public Matrix4x4 LocalToSource => HasExternalSourceTransform
            ? externalSourceTransform : Matrix4x4.TRS(position, rotation, scale);

        public bool SetExternalSourceTransform(Matrix4x4 localToSource)
        {
            if (!HairCoordinateUtility.IsInvertibleAffine(localToSource)) return false;
            externalSourceTransform = localToSource;
            hasExternalSourceTransform = true;
            position = localToSource.MultiplyPoint3x4(Vector3.zero);
            Vector3 forward = localToSource.MultiplyVector(Vector3.forward), up = localToSource.MultiplyVector(Vector3.up);
            rotation = Quaternion.LookRotation(forward.normalized, up.normalized);
            scale = new Vector3(localToSource.MultiplyVector(Vector3.right).magnitude, up.magnitude, forward.magnitude);
            return true;
        }

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            points ??= new List<Vector3>();
            gridColumns = Mathf.Clamp(gridColumns, 2, 16); gridRows = Mathf.Clamp(gridRows, 2, 32);
            gather ??= new HairGatherTarget(); gather.EnsureIntegrity();
            bun ??= new HairBunSettings(); bun.EnsureIntegrity();
            rail ??= new HairRailSettings(); rail.EnsureIntegrity();
            braidScale = float.IsFinite(braidScale) ? Mathf.Clamp(braidScale, .1f, 3f) : 1f;
            radius = Mathf.Max(0f, radius);
            scale.x = Mathf.Max(1e-5f, Mathf.Abs(scale.x));
            scale.y = Mathf.Max(1e-5f, Mathf.Abs(scale.y));
            scale.z = Mathf.Max(1e-5f, Mathf.Abs(scale.z));
        }
    }

    [Serializable]
    public sealed class HairChildSettings
    {
        [Min(0)] public int childrenPerGuide = 3;
        public bool includeGuideCard = true;
        [Min(0f)] public float rootSpread = 0.01f;
        [Range(0f, 1f)] public float clump = 0.25f;
        [Range(0f, 1f)] public float lengthVariation = 0.05f;
        [Range(0f, 1f)] public float widthVariation = 0.05f;
        [Range(0f, 1f)] public float rollVariation = 0.1f;
        public int seed = 12345;
        public HairGuideInterpolationMode interpolation = HairGuideInterpolationMode.WeightedNearest;

        public void EnsureIntegrity()
        {
            childrenPerGuide = Mathf.Max(0, childrenPerGuide);
            rootSpread = Mathf.Max(0f, rootSpread);
            clump = Mathf.Clamp01(clump);
            lengthVariation = Mathf.Clamp01(lengthVariation);
            widthVariation = Mathf.Clamp01(widthVariation);
            rollVariation = Mathf.Clamp01(rollVariation);
        }
    }

    [Serializable]
    public sealed class HairGroup
    {
        [SerializeField] private string id;
        public string name = "Coverage";
        public HairGroupRole role = HairGroupRole.Coverage;
        public Color color = new Color(0.22f, 0.65f, 1f, 1f);
        public bool visible = true;
        public bool locked;
        public bool enabled = true;
        [Range(0f, 1f)] public float lodImportance = 1f;
        public HairChildSettings children = new HairChildSettings();
        public HairGenerationPipeline generation = new HairGenerationPipeline();
        [Tooltip("Card-only root inset along the inward surface normal, in meters. Fades over the first 20% of each card; guides remain unchanged.")]
        [Range(0f, 0.02f)] public float rootEmbedDepth;
        [Tooltip("Keep the finished card cross-sections and faces outside the authoring surface. Does not change guides or triangle counts.")]
        public bool preventCardPenetration;
        [Range(0f, 0.005f)] public float cardSurfaceClearance = 0.0005f;
        public List<HairGrowthMap> maps = new List<HairGrowthMap>();
        public List<HairGuide> guides = new List<HairGuide>();
        public List<HairSculptLayer> sculptLayers = new List<HairSculptLayer>();
        public List<HairModifierSettings> modifiers = new List<HairModifierSettings>();
        public List<HairConstraintSettings> constraints = new List<HairConstraintSettings>();
        public HairCardProfileAsset profile;
        public HairAtlasProfileAsset atlas;
        public HairAtlasRegionSelectionMode atlasRegionSelection = HairAtlasRegionSelectionMode.All;
        public List<string> atlasRegionIds = new List<string>();

        public string Id => id;

        public void EnsureIntegrity(int sourceVertexCount)
        {
            HairStableId.Ensure(ref id);
            children ??= new HairChildSettings();
            generation ??= new HairGenerationPipeline();
            generation.EnsureIntegrity();
            maps ??= new List<HairGrowthMap>();
            guides ??= new List<HairGuide>();
            sculptLayers ??= new List<HairSculptLayer>();
            modifiers ??= new List<HairModifierSettings>();
            constraints ??= new List<HairConstraintSettings>();
            atlasRegionIds ??= new List<string>();
            HashSet<string> uniqueAtlasRegionIds = new HashSet<string>(StringComparer.Ordinal);
            atlasRegionIds.RemoveAll(regionId => string.IsNullOrWhiteSpace(regionId) ||
                                                  !uniqueAtlasRegionIds.Add(regionId));
            children.EnsureIntegrity();
            rootEmbedDepth = float.IsFinite(rootEmbedDepth) ? Mathf.Clamp(rootEmbedDepth, 0f, 0.02f) : 0f;
            cardSurfaceClearance = float.IsFinite(cardSurfaceClearance) ? Mathf.Clamp(cardSurfaceClearance, 0f, 0.005f) : 0.0005f;
            lodImportance = Mathf.Clamp01(lodImportance);

            EnsureDefaultMap(HairMapKind.GrowthArea, "Growth / Density", 0f, sourceVertexCount);
            EnsureDefaultMap(HairMapKind.Density, "Density Multiplier (optional)", 1f, sourceVertexCount);
            EnsureDefaultMap(HairMapKind.Length, "Length", 1f, sourceVertexCount);

            for (int i = 0; i < maps.Count; i++) maps[i]?.EnsureIntegrity(sourceVertexCount);
            float width = profile != null ? profile.DefaultWidth : 0.01f;
            for (int i = 0; i < guides.Count; i++) guides[i]?.EnsureIntegrity(width);
            for (int i = 0; i < sculptLayers.Count; i++) sculptLayers[i]?.EnsureIntegrity();
            for (int i = 0; i < modifiers.Count; i++) modifiers[i]?.EnsureIntegrity();
            for (int i = 0; i < constraints.Count; i++) constraints[i]?.EnsureIntegrity();
        }

        public HairGrowthMap FindMap(HairMapKind kind)
        {
            if (maps != null)
                for (int i = 0; i < maps.Count; i++)
                    if (maps[i] != null && maps[i].kind == kind) return maps[i];
            return null;
        }

        private void EnsureDefaultMap(HairMapKind kind, string mapName, float defaultValue, int vertexCount)
        {
            HairGrowthMap map = FindMap(kind);
            if (map == null)
            {
                map = new HairGrowthMap { name = mapName, kind = kind, defaultValue = defaultValue };
                maps.Add(map);
            }
            map.EnsureIntegrity(vertexCount);
        }
    }

    [Serializable]
    public sealed class HairLodSettings
    {
        [SerializeField] private string id;
        public string name = "LOD 0";
        [Min(0)] public int level;
        [Range(0f, 1f)] public float screenRelativeHeight = 0.6f;
        [Range(0f, 1f)] public float cardFraction = 1f;
        [Range(2, 64)] public int samplesPerCard = 12;
        public bool useProfileSamples;
        [Range(3, 12)] public int maximumTubeSides = 8;
        public HairLodReductionMode reductionMode;
        public bool reduceBones;
        public bool locked;

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            level = Mathf.Max(0, level);
            screenRelativeHeight = Mathf.Clamp01(screenRelativeHeight);
            cardFraction = Mathf.Clamp01(cardFraction);
            samplesPerCard = Mathf.Clamp(samplesPerCard, 2, 64);
            maximumTubeSides = Mathf.Clamp(maximumTubeSides, 3, 12);
        }

        public int ResolveSampleCount(HairCardProfileAsset profile) => useProfileSamples && profile != null
            ? profile.SamplesPerCard : Mathf.Clamp(samplesPerCard, 2, 64);
    }

    [Serializable]
    public sealed class HairBakeSettings
    {
        public string outputFolder = "Assets/UMAProjectData/HairCards/Generated";
        public string assetName = "HairCards";
        public bool createMesh = true;
        public bool createSlot = true;
        public bool createOverlay = true;
        public bool createWardrobeRecipe = true;
        public bool updateGlobalLibrary = true;
        public bool overwriteExisting = true;
        [Tooltip("Optional UMA UMAMaterial used when creating an OverlayDataAsset.")]
        public UnityEngine.Object umaMaterial;
        [Tooltip("Optional existing OverlayDataAsset. When assigned it is reused by the generated wardrobe recipe.")]
        public UnityEngine.Object overlayTemplate;
        [Tooltip("Optional RaceData used by the generated wardrobe recipe.")]
        public UnityEngine.Object raceData;
        public string wardrobeSlot = "Hair";
        [Min(1)] public int triangleBudget = 100000;
        [Min(1)] public int cardBudget = 10000;
        public bool requireAtlas;
    }
}
