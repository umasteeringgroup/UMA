using System;
using UnityEngine;

namespace UMA.Dismemberment
{
    /// <summary>
    /// Durable UV-space description of one closed boundary produced by a successful cut.
    /// Arrays are owned by the result and remain valid after the temporary mesh builder exits.
    /// </summary>
    [Serializable]
    public sealed class DismembermentCutSurface
    {
        public SkinnedMeshRenderer sourceRenderer;
        public int sourceSubmeshIndex = -1;
        public Material sourceMaterial;
        public int[] sourceVertexIndices = Array.Empty<int>();
        public Vector2[] boundaryUV = Array.Empty<Vector2>();
        public Vector3[] boundaryLocalPositions = Array.Empty<Vector3>();
        public int[] loopStarts = Array.Empty<int>();
        public int[] loopCounts = Array.Empty<int>();
        public bool boundaryClosed = true;
        public Rect uvBounds;
        public Vector3 localCenter;
        public Vector3 localNormal;
        public string slotName;
        public string slotGroup;
        public string overlayGroup;
        public string[] overlayGroups = Array.Empty<string>();
        public string umaMaterialName;

        public bool IsValid => sourceRenderer != null && boundaryUV != null &&
            boundaryUV.Length >= (boundaryClosed ? 3 : 2) && loopStarts != null &&
            loopStarts.Length > 0;

        public Vector3 WorldCenter => sourceRenderer != null
            ? sourceRenderer.transform.TransformPoint(localCenter) : localCenter;

        public Vector3 WorldNormal => sourceRenderer != null
            ? sourceRenderer.transform.TransformDirection(localNormal).normalized
            : localNormal.normalized;
    }
}
