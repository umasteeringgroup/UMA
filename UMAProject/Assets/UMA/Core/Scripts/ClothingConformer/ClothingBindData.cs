using System;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Persistent surface binding for one UMA clothing slot. Positions are stored in the
    /// UMA root's local space so bindings remain valid when a renderer is placed below it.
    /// </summary>
    [CreateAssetMenu(menuName = "UMA/Clothing Bind Data", fileName = "ClothingBindData")]
    public class ClothingBindData : ScriptableObject
    {
        [Tooltip("Name of the clothing slot this binding belongs to.")]
        public string sourceSlotName;

        public SlotDataAsset sourceSlotAsset;
        public Mesh clothingMeshOriginal;
        public UMAMaterial sourceMaterial;
        public Vector2[] originalUv;
        public int vertexCount;
        public BindVertexData[] vertices;
        public int[] triangles;

        [Tooltip("Split-vertex seam groups. Vertices in a group retain their original relative position while conforming.")]
        public int[] weldedVertexGroups;
        [HideInInspector] public float weldedSeamTolerance;

        [Tooltip("The ordered set of active slots used as the body surface.")]
        public string[] baseSlotNames;

        [Tooltip("Hash of the body-surface slot topology at bind time.")]
        public int baseTopologyHash;

        [Tooltip("Hash of the clothing topology at bind time.")]
        public int clothingTopologyHash;

        public Bounds sourceBounds;
        public string umaVersion;
        public bool isComplete;

        public int BoundVertexCount
        {
            get
            {
                if (vertices == null) return 0;
                int count = 0;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (vertices[i].mappedTriangleIndex >= 0 || vertices[i].HasNearestVertexFallback)
                        count++;
                }
                return count;
            }
        }
    }

    [Serializable]
    public struct BindVertexData
    {
        [Tooltip("Original clothing position in UMA-root local space.")]
        public Vector3 localPosition;
        public Vector3 localNormal;
        public Vector4 localTangent;

        [Tooltip("Triangle index in the flattened bind surface, or -1 when no triangle was found.")]
        public int mappedTriangleIndex;
        public Vector3 barycentric;

        [Tooltip("Up to four nearest vertices in the flattened bind surface.")]
        public int[] nearestBaseVertexIndices;
        public float[] nearestBaseVertexWeights;

        public float signedDistance;
        public Vector3 mappedNormal;

        public bool HasNearestVertexFallback
        {
            get
            {
                return nearestBaseVertexIndices != null && nearestBaseVertexWeights != null &&
                       nearestBaseVertexIndices.Length > 0 &&
                       nearestBaseVertexIndices.Length == nearestBaseVertexWeights.Length;
            }
        }
    }

    [Serializable]
    public class ClothingConformerSettings
    {
        [Min(0.001f)] public float maxSearchRadius = 0.2f;
        [Min(0.001f)] public float maxTriangleDistance = 0.5f;

        [Tooltip("Signed extra distance along the side of the body surface where this clothing was bound. Positive moves clothing outward; negative moves it inward.")]
        public float additionalNormalOffset = 0f;
        [Min(0f)] public float normalOffsetEpsilon = 0.001f;

        public bool enableCollisionCorrection = true;
        [Min(0f)] public float collisionPushDistance = 0.002f;
        [Min(0f)] public float maxCollisionDisplacement = 0.05f;

        public bool enableSmoothing = true;
        public SmoothingAlgorithm smoothingAlgorithm = SmoothingAlgorithm.HC;
        [Range(1, 64)] public int smoothingIterations = 8;
        [Range(0f, 1f)] public float smoothingStrength = 0.5f;
        public bool smoothOnlyMovedVertices = false;
        [Range(0f, 1f)] public float hcAlpha = 0.5f;
        [Range(0f, 1f)] public float hcBeta = 0.5f;

        [Tooltip("Keep UV-split clothing seams closed by applying a shared displacement to nearly coincident, non-connected vertices.")]
        public bool preserveWeldedSeams = true;
        [Min(0.000001f)]
        [Tooltip("Maximum original-space separation for recognizing a split-vertex seam. Keep this small to avoid joining intentional layered geometry.")]
        public float weldedSeamTolerance = 0.0001f;

        [Tooltip("When no body slots are explicitly selected, all active slots other than clothing are used.")]
        public bool useUnselectedSlotsAsBase = true;

        [Tooltip("Re-evaluate the preview when body blendshape or skeleton state changes.")]
        public bool livePreview = true;
    }

    public enum SmoothingAlgorithm
    {
        Laplacian,
        Taubin,
        HC
    }
}
