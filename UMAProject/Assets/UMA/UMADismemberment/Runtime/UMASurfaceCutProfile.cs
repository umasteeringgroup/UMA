using UnityEngine;
using UnityEngine.Serialization;

namespace UMA.Dismemberment
{
    [CreateAssetMenu(menuName = "UMA/Dismemberment/Surface Cut Profile",
        fileName = "Surface Cut Profile")]
    public sealed class UMASurfaceCutProfile : ScriptableObject
    {
        [Header("Cut Appearance")]
        [Tooltip("Color of the open center of the cut.")]
        public Color centerColor = new Color(0.16f, 0.002f, 0.004f, 1f);
        [Tooltip("Color of the irritated skin along both sides of the cut.")]
        public Color edgeColor = new Color(0.95f, 0.22f, 0.28f, 0.82f);
        [Min(0.0005f), Tooltip("Full cut width in meters. One Unity unit is one meter.")]
        public float widthMeters = 0.008f;
        [Range(0.05f, 0.9f), Tooltip("Fraction of the half-width occupied by the dark center.")]
        public float centerFraction = 0.32f;
        [Range(0.01f, 0.5f), Tooltip("Softness of the outside edge as a fraction of width.")]
        public float edgeSoftness = 0.14f;
        [Range(0.01f, 0.45f), Tooltip("Fraction of the cut length used to taper each end.")]
        public float endTaperFraction = 0.12f;

        [Header("Bleeding")]
        [Min(0f), Tooltip("Average distance in meters between surface-fluid sources along the " +
            "cut. Longer cuts automatically receive more sources. Zero creates a dry cut.")]
        public float bleedSpacingMeters = 0.025f;
        [Range(0f, 0.95f), Tooltip("Random variation applied independently to each spacing. " +
            "For example, 0.3 varies a 2.5 cm spacing between 1.75 and 3.25 cm.")]
        public float bleedSpacingVariation = 0.3f;
        [Tooltip("Seed used by the local spacing randomizer. It does not modify Unity's global " +
            "random state. Successive cuts still receive different patterns.")]
        public int bleedSpacingSeed = 173;
        [Range(0f, 0.95f), Tooltip("Per-drip variation around the Surface Fluid Profile's fall " +
            "speed. For example, 0.25 produces speeds from 75% to 125% of the profile value.")]
        public float bleedSpeedVariation = 0.25f;
        [Range(0f, 0.95f), Tooltip("Per-drip variation around the Surface Fluid Profile's " +
            "emission radius. Larger sources also emit more fluid because they cover more area.")]
        public float bleedSizeVariation = 0.3f;
        [FormerlySerializedAs("bleedSourceCount"), SerializeField, HideInInspector]
        private int legacyBleedSourceCount = -1;
        [Range(0f, 0.45f), Tooltip("Keeps bleed sources away from the tapered endpoints.")]
        public float bleedEndInset = 0.12f;
        [Tooltip("Optional fluid settings for the distributed bleeds. A long-lived blood " +
            "profile is created at runtime when this is empty.")]
        public UMASurfaceFluidProfile bleedProfile;

        private void OnEnable()
        {
            MigrateLegacyBleedCount();
        }

        private void OnValidate()
        {
            MigrateLegacyBleedCount();
            centerColor.a = Mathf.Clamp01(centerColor.a);
            edgeColor.a = Mathf.Clamp01(edgeColor.a);
            widthMeters = Mathf.Max(0.0005f, widthMeters);
            centerFraction = Mathf.Clamp(centerFraction, 0.05f, 0.9f);
            edgeSoftness = Mathf.Clamp(edgeSoftness, 0.01f, 0.5f);
            endTaperFraction = Mathf.Clamp(endTaperFraction, 0.01f, 0.45f);
            bleedSpacingMeters = Mathf.Max(0f, bleedSpacingMeters);
            bleedSpacingVariation = Mathf.Clamp(bleedSpacingVariation, 0f, 0.95f);
            bleedSpeedVariation = Mathf.Clamp(bleedSpeedVariation, 0f, 0.95f);
            bleedSizeVariation = Mathf.Clamp(bleedSizeVariation, 0f, 0.95f);
            bleedEndInset = Mathf.Clamp(bleedEndInset, 0f, 0.45f);
        }

        private void MigrateLegacyBleedCount()
        {
            if (legacyBleedSourceCount < 0) return;
            bleedSpacingMeters = legacyBleedSourceCount == 0
                ? 0f : 0.1f / Mathf.Clamp(legacyBleedSourceCount, 1, 16);
            legacyBleedSourceCount = -1;
        }
    }

    public readonly struct SurfaceCutPoint
    {
        public SkinnedMeshRenderer Renderer { get; }
        public int SubmeshIndex { get; }
        public int VertexA { get; }
        public int VertexB { get; }
        public int VertexC { get; }
        public Vector3 Barycentric { get; }
        public Vector3 WorldPosition { get; }
        public Vector3 WorldNormal { get; }
        public Vector2 AtlasUV { get; }
        public bool IsValid => Renderer != null && SubmeshIndex >= 0 && VertexA >= 0;

        internal SurfaceCutPoint(SkinnedMeshRenderer renderer, int submeshIndex,
            int vertexA, int vertexB, int vertexC, Vector3 barycentric,
            Vector3 worldPosition, Vector3 worldNormal, Vector2 atlasUV)
        {
            Renderer = renderer;
            SubmeshIndex = submeshIndex;
            VertexA = vertexA;
            VertexB = vertexB;
            VertexC = vertexC;
            Barycentric = barycentric;
            WorldPosition = worldPosition;
            WorldNormal = worldNormal;
            AtlasUV = atlasUV;
        }
    }

    public readonly struct SurfaceCutResult
    {
        public RuntimeDecalHandle CutHandle { get; }
        public RuntimeDecalHandle BleedHandle { get; }
        public int BleedSourceCount { get; }
        public float LengthMeters { get; }
        public bool Success => CutHandle.IsValid;

        internal SurfaceCutResult(RuntimeDecalHandle cutHandle,
            RuntimeDecalHandle bleedHandle, int bleedSourceCount, float lengthMeters)
        {
            CutHandle = cutHandle;
            BleedHandle = bleedHandle;
            BleedSourceCount = bleedSourceCount;
            LengthMeters = lengthMeters;
        }
    }
}
