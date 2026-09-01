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
        LodReduction
    }

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
        BoneChainPreview
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
        public string name = "Growth Area";
        public HairMapKind kind = HairMapKind.GrowthArea;
        public bool visible = true;
        public bool locked;
        public float defaultValue;
        public Vector2 valueRange = new Vector2(0f, 1f);
        public float[] values = Array.Empty<float>();

        public string Id => id;

        public void EnsureIntegrity(int vertexCount)
        {
            HairStableId.Ensure(ref id);
            int count = Mathf.Max(0, vertexCount);
            if (values == null || values.Length != count)
            {
                float[] resized = new float[count];
                if (values != null)
                {
                    Array.Copy(values, resized, Mathf.Min(values.Length, resized.Length));
                }
                if (values == null || values.Length == 0)
                {
                    for (int i = 0; i < resized.Length; i++)
                    {
                        resized[i] = defaultValue;
                    }
                }
                values = resized;
            }

            if (valueRange.x > valueRange.y)
            {
                (valueRange.x, valueRange.y) = (valueRange.y, valueRange.x);
            }
        }

        public float SampleVertex(int index)
        {
            if (values == null || (uint)index >= (uint)values.Length)
            {
                return defaultValue;
            }
            return values[index];
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
        public string maskMapId;
        public List<HairGuideDelta> deltas = new List<HairGuideDelta>();

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            deltas ??= new List<HairGuideDelta>();
            opacity = Mathf.Clamp01(opacity);
        }
    }

    [Serializable]
    public sealed class HairModifierSettings
    {
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
        public string helperId;

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            rootToTip ??= AnimationCurve.Linear(0f, 1f, 1f, 1f);
            weight = Mathf.Clamp01(weight);
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
        [SerializeField] private string id;
        public string name = "Helper";
        public HairHelperType type = HairHelperType.CurveRail;
        public bool visible = true;
        public bool locked;
        public bool embedded = true;
        public string externalGlobalId;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public float radius = 0.1f;
        public Vector3 size = Vector3.one;
        public List<Vector3> points = new List<Vector3>();

        public string Id => id;

        public void EnsureIntegrity()
        {
            HairStableId.Ensure(ref id);
            points ??= new List<Vector3>();
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
        public List<HairGrowthMap> maps = new List<HairGrowthMap>();
        public List<HairGuide> guides = new List<HairGuide>();
        public List<HairSculptLayer> sculptLayers = new List<HairSculptLayer>();
        public List<HairModifierSettings> modifiers = new List<HairModifierSettings>();
        public List<HairConstraintSettings> constraints = new List<HairConstraintSettings>();
        public HairCardProfileAsset profile;
        public HairAtlasProfileAsset atlas;

        public string Id => id;

        public void EnsureIntegrity(int sourceVertexCount)
        {
            HairStableId.Ensure(ref id);
            children ??= new HairChildSettings();
            maps ??= new List<HairGrowthMap>();
            guides ??= new List<HairGuide>();
            sculptLayers ??= new List<HairSculptLayer>();
            modifiers ??= new List<HairModifierSettings>();
            constraints ??= new List<HairConstraintSettings>();
            children.EnsureIntegrity();
            lodImportance = Mathf.Clamp01(lodImportance);

            EnsureDefaultMap(HairMapKind.GrowthArea, "Growth Area", 0f, sourceVertexCount);
            EnsureDefaultMap(HairMapKind.Density, "Density", 1f, sourceVertexCount);
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
            return maps?.Find(map => map != null && map.kind == kind);
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
    }
}
