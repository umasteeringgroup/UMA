using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    public struct SurfaceBrushContact
    {
        public Vector3 worldPoint;
        public Vector3 worldNormal;
        public Vector2 brushCenterUV;
        public int triangleIndex;
        public int uvIsland;
        public string slotName;
        public float distance;
    }

    public sealed class ReconstructedSurface
    {
        public int index;
        public int rendererIndex;
        public int sourceSubmeshIndex;
        public string slotName;
        public List<string> slotNames = new List<string>();
        public List<SlotData> slots = new List<SlotData>();
        public string[] triangleSlotNames;
        public GameObject gameObject;
        public Mesh mesh;
        public MeshCollider collider;
        public Material sourceMaterial;
        public Material previewMaterial;
        public UMAMaterial umaMaterial;
        public UMAData.GeneratedMaterial generatedMaterial;
        public Texture[] standaloneSourceTextures;
        public List<Texture> ownedStandaloneSourceTextures;
        public OverlayDataAsset standaloneSourceOverlay;
        public bool allowMissingSourceTextures;
        public int[] triangleIslands;
        private Dictionary<Vector2Int, List<int>> uvTriangleGrid;
        private readonly List<int> uvCandidates = new List<int>();
        private readonly HashSet<int> uvSeen = new HashSet<int>();
        private Vector3[] cachedVertices;
        private Vector3[] cachedNormals;
        private Vector4[] cachedTangents;
        private Vector2[] cachedUV;
        private int[] cachedTriangles;
        private int[] cachedTriangleBoundaryMasks;
        private Dictionary<Vector3Int, List<int>> spatialTriangleGrid;
        private readonly List<int> spatialCandidates = new List<int>();
        private readonly HashSet<int> spatialSeen = new HashSet<int>();
        private const int UVGridResolution = 32;
        private const int SpatialGridResolution = 16;

        public bool TryRaycast(Ray ray, out RaycastHit hit)
        {
            hit = default;
            return collider != null && collider.Raycast(ray, out hit, float.MaxValue);
        }

        /// <summary>
        /// Projects a canonical world-space path point onto this surface without changing its
        /// coordinates tangent to the supplied normal. This keeps a 3D curve continuous when the
        /// reconstructed mesh is split at a UV seam, slot boundary, or UDIM boundary.
        /// </summary>
        public bool TryProjectAlongNormal(Vector3 worldPoint, Vector3 normalHint, IList<string> allowedSlots,
            out Vector3 surfacePoint, out Vector3 surfaceNormal, out Vector2 surfaceUV,
            out int triangleIndex, out Vector3 barycentric)
        {
            surfacePoint = Vector3.zero; surfaceNormal = Vector3.up; surfaceUV = Vector2.zero;
            triangleIndex = -1; barycentric = Vector3.zero;
            if (mesh == null || gameObject == null || collider == null || normalHint.sqrMagnitude <= 0.00000001f)
                return false;

            Vector3 axis = normalHint.normalized;
            Bounds bounds = collider.bounds;
            // The enclosing-sphere distance guarantees that the ray begins beyond this surface,
            // even when the unprojected Bezier chord temporarily passes inside the character.
            float rayHalfLength = bounds.extents.magnitude + Vector3.Distance(worldPoint, bounds.center) + 0.001f;
            Ray ray = new Ray(worldPoint + axis * rayHalfLength, -axis);
            if (!collider.Raycast(ray, out RaycastHit hit, rayHalfLength * 2f)) return false;
            string hitSlot = GetTriangleSlotName(hit.triangleIndex);
            if (allowedSlots != null && allowedSlots.Count > 0 && !string.IsNullOrEmpty(hitSlot) &&
                !ContainsSlot(allowedSlots, hitSlot)) return false;

            EnsureMeshData();
            int offset = hit.triangleIndex * 3;
            if (offset < 0 || offset + 2 >= cachedTriangles.Length) return false;
            int a = cachedTriangles[offset], b = cachedTriangles[offset + 1], c = cachedTriangles[offset + 2];
            Vector3 hitNormal = cachedNormals != null && cachedNormals.Length == cachedVertices.Length
                ? cachedNormals[a] * hit.barycentricCoordinate.x +
                  cachedNormals[b] * hit.barycentricCoordinate.y +
                  cachedNormals[c] * hit.barycentricCoordinate.z
                : Vector3.Cross(cachedVertices[b] - cachedVertices[a], cachedVertices[c] - cachedVertices[a]);
            hitNormal = gameObject.transform.TransformDirection(hitNormal).normalized;
            if (Vector3.Dot(hitNormal, axis) < -0.1f) return false;

            surfacePoint = hit.point;
            surfaceNormal = hitNormal;
            triangleIndex = hit.triangleIndex;
            barycentric = hit.barycentricCoordinate;
            if (cachedUV != null && cachedUV.Length == cachedVertices.Length)
                surfaceUV = cachedUV[a] * barycentric.x + cachedUV[b] * barycentric.y + cachedUV[c] * barycentric.z;
            return true;
        }

        /// <summary>
        /// Resolves a world-space path sample to the nearest point on this reconstructed mesh.
        /// Unlike UV lookup, this remains continuous across UV seams and UDIM tiles. A normal hint
        /// keeps thin or doubled surfaces from jumping to the opposite side of the character.
        /// </summary>
        public bool TryClosestSurfacePoint(Vector3 worldPoint, Vector3 normalHint, int preferredTriangle,
            out Vector3 surfacePoint, out Vector3 surfaceNormal, out Vector2 surfaceUV,
            out int triangleIndex, out Vector3 barycentric)
        {
            return TryClosestSurfacePoint(worldPoint, normalHint, preferredTriangle, null,
                out surfacePoint, out surfaceNormal, out surfaceUV, out triangleIndex, out barycentric);
        }

        public bool TryClosestSurfacePoint(Vector3 worldPoint, Vector3 normalHint, int preferredTriangle,
            IList<string> allowedSlots, out Vector3 surfacePoint, out Vector3 surfaceNormal,
            out Vector2 surfaceUV, out int triangleIndex, out Vector3 barycentric)
        {
            surfacePoint = Vector3.zero; surfaceNormal = Vector3.up; surfaceUV = Vector2.zero;
            triangleIndex = -1; barycentric = Vector3.zero;
            if (mesh == null || gameObject == null) return false;
            EnsureMeshData();
            EnsureSpatialTriangleGrid();
            if (cachedTriangles == null || cachedTriangles.Length < 3) return false;

            Transform transform = gameObject.transform;
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            Vector3 normalizedHint = normalHint.sqrMagnitude > 0.00000001f
                ? normalHint.normalized : Vector3.zero;
            float bestDistance = float.MaxValue;
            Vector3 bestPoint = Vector3.zero, bestNormal = Vector3.up, bestBarycentric = Vector3.zero;
            int bestTriangle = -1;

            spatialCandidates.Clear();
            spatialSeen.Clear();

            bool Evaluate(int candidate, bool requireNormalAgreement)
            {
                int offset = candidate * 3;
                if (candidate < 0 || offset + 2 >= cachedTriangles.Length) return false;
                string candidateSlot = GetTriangleSlotName(candidate);
                if (allowedSlots != null && allowedSlots.Count > 0 &&
                    !string.IsNullOrEmpty(candidateSlot) && !ContainsSlot(allowedSlots, candidateSlot)) return false;
                int ia = cachedTriangles[offset], ib = cachedTriangles[offset + 1], ic = cachedTriangles[offset + 2];
                Vector3 closestLocal = ClosestPointOnTriangle(localPoint, cachedVertices[ia],
                    cachedVertices[ib], cachedVertices[ic], out Vector3 candidateBarycentric);
                Vector3 localNormal = cachedNormals != null && cachedNormals.Length == cachedVertices.Length
                    ? cachedNormals[ia] * candidateBarycentric.x + cachedNormals[ib] * candidateBarycentric.y +
                      cachedNormals[ic] * candidateBarycentric.z
                    : Vector3.Cross(cachedVertices[ib] - cachedVertices[ia],
                        cachedVertices[ic] - cachedVertices[ia]);
                Vector3 candidateNormal = transform.TransformDirection(localNormal).normalized;
                if (requireNormalAgreement && normalizedHint.sqrMagnitude > 0f &&
                    Vector3.Dot(candidateNormal, normalizedHint) < -0.1f) return false;
                Vector3 candidatePoint = transform.TransformPoint(closestLocal);
                float distance = (candidatePoint - worldPoint).sqrMagnitude;
                if (distance >= bestDistance) return true;
                bestDistance = distance; bestPoint = candidatePoint; bestNormal = candidateNormal;
                bestBarycentric = candidateBarycentric; bestTriangle = candidate;
                return true;
            }

            if (preferredTriangle >= 0 && spatialSeen.Add(preferredTriangle))
            {
                spatialCandidates.Add(preferredTriangle);
                Evaluate(preferredTriangle, true);
            }

            Vector3Int center = SpatialCell(localPoint, mesh.bounds);
            int acceptedRadius = -1;
            for (int radius = 0; radius < SpatialGridResolution; radius++)
            {
                bool acceptedThisRadius = false;
                int minX = Mathf.Max(0, center.x - radius), maxX = Mathf.Min(SpatialGridResolution - 1, center.x + radius);
                int minY = Mathf.Max(0, center.y - radius), maxY = Mathf.Min(SpatialGridResolution - 1, center.y + radius);
                int minZ = Mathf.Max(0, center.z - radius), maxZ = Mathf.Min(SpatialGridResolution - 1, center.z + radius);
                for (int z = minZ; z <= maxZ; z++)
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (radius > 0 && x > minX && x < maxX && y > minY && y < maxY &&
                        z > minZ && z < maxZ) continue;
                    if (!spatialTriangleGrid.TryGetValue(new Vector3Int(x, y, z), out List<int> bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int candidate = bucket[i];
                        if (!spatialSeen.Add(candidate)) continue;
                        spatialCandidates.Add(candidate);
                        acceptedThisRadius |= Evaluate(candidate, true);
                    }
                }
                if (acceptedThisRadius && acceptedRadius < 0) acceptedRadius = radius;
                if (acceptedRadius >= 0 && radius >= acceptedRadius + 1) break;
            }

            // A normal hint is a continuity preference, not a hard failure condition.
            if (bestTriangle < 0)
                for (int i = 0; i < spatialCandidates.Count; i++) Evaluate(spatialCandidates[i], false);
            if (bestTriangle < 0) return false;

            surfacePoint = bestPoint; surfaceNormal = bestNormal; triangleIndex = bestTriangle;
            barycentric = bestBarycentric;
            int bestOffset = bestTriangle * 3;
            int a = cachedTriangles[bestOffset], b = cachedTriangles[bestOffset + 1], c = cachedTriangles[bestOffset + 2];
            if (cachedUV != null && cachedUV.Length == cachedVertices.Length)
                surfaceUV = cachedUV[a] * bestBarycentric.x + cachedUV[b] * bestBarycentric.y +
                    cachedUV[c] * bestBarycentric.z;
            return true;
        }

        public bool ContainsSlot(string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            for (int i = 0; i < slotNames.Count; i++)
                if (string.Equals(slotNames[i], candidate, StringComparison.Ordinal)) return true;
            return false;
        }

        public string GetTriangleSlotName(int triangleIndex)
        {
            if (triangleSlotNames != null && (uint)triangleIndex < (uint)triangleSlotNames.Length &&
                !string.IsNullOrEmpty(triangleSlotNames[triangleIndex]))
                return triangleSlotNames[triangleIndex];
            return slotNames.Count == 1 ? slotNames[0] : null;
        }

        public void CollectBrushContacts(Vector3 worldCenter, float worldRadius, IList<string> allowedSlots,
            List<SurfaceBrushContact> results, Vector3 projectionNormal = default, float projectionDepth = 0f,
            float normalAngleLimit = 180f, bool paintBackfaces = true,
            Vector3 sharedWorldTangent = default, Vector3 sharedWorldBitangent = default)
        {
            if (mesh == null || gameObject == null || results == null || worldRadius <= 0f) return;
            EnsureMeshData();
            EnsureSpatialTriangleGrid();
            Transform transform = gameObject.transform;
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            Vector3 scale = transform.lossyScale;
            float minimumScale = Mathf.Max(0.00001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            float localRadius = worldRadius / minimumScale;
            Bounds bounds = mesh.bounds;
            if (bounds.SqrDistance(localCenter) > localRadius * localRadius) return;

            spatialCandidates.Clear();
            spatialSeen.Clear();
            Vector3Int min = SpatialCell(localCenter - Vector3.one * localRadius, bounds);
            Vector3Int max = SpatialCell(localCenter + Vector3.one * localRadius, bounds);
            for (int z = min.z; z <= max.z; z++)
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!spatialTriangleGrid.TryGetValue(new Vector3Int(x, y, z), out List<int> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++) if (spatialSeen.Add(bucket[i])) spatialCandidates.Add(bucket[i]);
            }

            float radiusSquared = worldRadius * worldRadius;
            for (int i = 0; i < spatialCandidates.Count; i++)
            {
                int triangle = spatialCandidates[i];
                string triangleSlot = GetTriangleSlotName(triangle);
                if (!string.IsNullOrEmpty(triangleSlot) && !ContainsSlot(allowedSlots, triangleSlot)) continue;
                int offset = triangle * 3;
                int ia = cachedTriangles[offset], ib = cachedTriangles[offset + 1], ic = cachedTriangles[offset + 2];
                Vector3 closestLocal = ClosestPointOnTriangle(localCenter, cachedVertices[ia], cachedVertices[ib], cachedVertices[ic], out Vector3 barycentric);
                Vector3 closestWorld = transform.TransformPoint(closestLocal);
                float squareDistance = (closestWorld - worldCenter).sqrMagnitude;
                if (squareDistance > radiusSquared) continue;
                int island = triangleIslands != null && triangle < triangleIslands.Length ? triangleIslands[triangle] : -1;
                Vector3 localNormal = cachedNormals != null && cachedNormals.Length == cachedVertices.Length
                    ? cachedNormals[ia] * barycentric.x + cachedNormals[ib] * barycentric.y + cachedNormals[ic] * barycentric.z
                    : Vector3.Cross(cachedVertices[ib] - cachedVertices[ia], cachedVertices[ic] - cachedVertices[ia]);
                Vector3 worldNormal = transform.TransformDirection(localNormal).normalized;
                Vector3 referenceNormal = projectionNormal.sqrMagnitude > 0.000001f ? projectionNormal.normalized : worldNormal;
                float depth = Mathf.Abs(Vector3.Dot(closestWorld - worldCenter, referenceNormal));
                if (projectionDepth > 0f && depth > projectionDepth) continue;
                if (!paintBackfaces && Vector3.Dot(worldNormal, referenceNormal) < 0f) continue;
                if (normalAngleLimit < 180f && Vector3.Angle(worldNormal, referenceNormal) > normalAngleLimit) continue;
                bool hasSharedFrame = sharedWorldTangent.sqrMagnitude > 0.00000001f &&
                    sharedWorldBitangent.sqrMagnitude > 0.00000001f;
                Vector2 brushCenterUV;
                if (hasSharedFrame)
                {
                    // A projected stroke has one canonical world-space frame. Falling back to a
                    // triangle-local center when this inverse is singular creates one independent
                    // stamp per tiny/grazing polygon (the characteristic radial "explosion").
                    if (!TryProjectWorldPointToUV(triangle, worldCenter, sharedWorldTangent,
                        sharedWorldBitangent, out brushCenterUV)) continue;
                }
                else brushCenterUV = ProjectLocalPointToTriangleUV(localCenter, triangle);
                results.Add(new SurfaceBrushContact
                {
                    worldPoint = closestWorld,
                    worldNormal = worldNormal,
                    brushCenterUV = brushCenterUV,
                    triangleIndex = triangle,
                    uvIsland = island,
                    slotName = triangleSlot,
                    distance = Mathf.Sqrt(squareDistance)
                });
            }
        }

        private static bool ContainsSlot(IList<string> allowedSlots, string candidate)
        {
            if (allowedSlots == null || allowedSlots.Count == 0) return false;
            for (int i = 0; i < allowedSlots.Count; i++)
                if (string.Equals(allowedSlots[i], candidate, StringComparison.Ordinal)) return true;
            return false;
        }

        public float CalculateUVRadius(int triangleIndex, float worldRadius)
        {
            if (mesh == null || triangleIndex < 0) return worldRadius;
            EnsureMeshData();
            int[] triangles = cachedTriangles;
            Vector3[] vertices = cachedVertices;
            Vector2[] uv = cachedUV;
            int offset = triangleIndex * 3;
            if (offset + 2 >= triangles.Length || uv.Length != vertices.Length) return worldRadius;
            int a = triangles[offset], b = triangles[offset + 1], c = triangles[offset + 2];
            float worldLength = (Vector3.Distance(vertices[a], vertices[b]) + Vector3.Distance(vertices[b], vertices[c]) + Vector3.Distance(vertices[c], vertices[a])) / 3f;
            float uvLength = (Vector2.Distance(uv[a], uv[b]) + Vector2.Distance(uv[b], uv[c]) + Vector2.Distance(uv[c], uv[a])) / 3f;
            return worldRadius * uvLength / Mathf.Max(0.00001f, worldLength);
        }

        public BrushProjection CalculateBrushProjection(int triangleIndex, float worldRadius)
        {
            return CalculateBrushProjection(triangleIndex, worldRadius, Vector3.zero, Vector3.zero);
        }

        public BrushProjection CalculateBrushProjection(int triangleIndex, float worldRadius,
            Vector3 sharedWorldTangent, Vector3 sharedWorldBitangent, bool restrictToTriangle = false)
        {
            BrushProjection projection = default;
            if (mesh == null || triangleIndex < 0 || worldRadius <= 0f) return projection;
            EnsureMeshData();
            int offset = triangleIndex * 3;
            if (offset + 2 >= cachedTriangles.Length || cachedUV == null || cachedUV.Length != cachedVertices.Length) return projection;
            int ia = cachedTriangles[offset], ib = cachedTriangles[offset + 1], ic = cachedTriangles[offset + 2];
            projection.triangleUV0 = cachedUV[ia];
            projection.triangleUV1 = cachedUV[ib];
            projection.triangleUV2 = cachedUV[ic];
            projection.triangleBoundaryMask = GetTriangleBoundaryMask(triangleIndex);
            projection.restrictToTriangle = restrictToTriangle;
            Vector2 uv1 = cachedUV[ib] - cachedUV[ia], uv2 = cachedUV[ic] - cachedUV[ia];
            float uvDeterminant = uv1.x * uv2.y - uv1.y * uv2.x;
            Transform transform = gameObject.transform;
            Vector3 worldEdge1 = transform.TransformVector(cachedVertices[ib] - cachedVertices[ia]);
            Vector3 worldEdge2 = transform.TransformVector(cachedVertices[ic] - cachedVertices[ia]);
            Vector3 normal = Vector3.Cross(worldEdge1, worldEdge2).normalized;
            bool validUVTriangle = Mathf.Abs(uvDeterminant) >= 0.00000001f;
            Vector3 worldPerU = validUVTriangle
                ? (worldEdge1 * uv2.y - worldEdge2 * uv1.y) / uvDeterminant : Vector3.zero;
            Vector3 worldPerV = validUVTriangle
                ? (-worldEdge1 * uv2.x + worldEdge2 * uv1.x) / uvDeterminant : Vector3.zero;
            Vector3 tangent;
            Vector3 bitangent;
            bool useSharedFrame = sharedWorldTangent.sqrMagnitude > 0.00000001f &&
                sharedWorldBitangent.sqrMagnitude > 0.00000001f;
            if (useSharedFrame)
            {
                // Keep every physical texture in a logical paint target on the same projection plane.
                // Re-projecting these axes into the contact triangle would recreate the per-tile seam.
                tangent = sharedWorldTangent.normalized;
                bitangent = sharedWorldBitangent.normalized;
            }
            else
            {
                float handedness = 1f;
                if (cachedTangents != null && cachedTangents.Length == cachedVertices.Length)
                {
                    Vector4 sourceTangent = (cachedTangents[ia] + cachedTangents[ib] + cachedTangents[ic]) / 3f;
                    tangent = transform.TransformDirection(new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z));
                    tangent -= normal * Vector3.Dot(tangent, normal);
                    handedness = sourceTangent.w < 0f ? -1f : 1f;
                }
                else
                {
                    Vector3 tangentSource = validUVTriangle ? worldPerU : worldEdge1;
                    tangent = tangentSource - normal * Vector3.Dot(tangentSource, normal);
                }
                if (tangent.sqrMagnitude < 0.00000001f) tangent = worldEdge1;
                tangent.Normalize();
                bitangent = Vector3.Cross(normal, tangent).normalized * handedness;
            }
            projection.worldTangent = tangent;
            projection.worldBitangent = bitangent;
            // Preserve the world frame for callers even if this particular UV triangle cannot be
            // inverted. It lets the center hit establish one global projector for its neighbors.
            if (!validUVTriangle) return projection;
            float inverseRadius = 1f / worldRadius;
            float m00 = Vector3.Dot(worldPerU, tangent) * inverseRadius;
            float m01 = Vector3.Dot(worldPerV, tangent) * inverseRadius;
            float m10 = Vector3.Dot(worldPerU, bitangent) * inverseRadius;
            float m11 = Vector3.Dot(worldPerV, bitangent) * inverseRadius;
            if (!TryProjectionDeterminant(m00, m01, m10, m11, out float determinant)) return projection;
            float inverseDeterminant = 1f / determinant;
            float inverse00 = m11 * inverseDeterminant, inverse01 = -m01 * inverseDeterminant;
            float inverse10 = -m10 * inverseDeterminant, inverse11 = m00 * inverseDeterminant;
            float boundU = Mathf.Sqrt(inverse00 * inverse00 + inverse01 * inverse01);
            float boundV = Mathf.Sqrt(inverse10 * inverse10 + inverse11 * inverse11);
            projection.uvToBrush = new Vector4(m00, m01, m10, m11);
            projection.uvBoundsRadius = Mathf.Max(boundU, boundV);
            projection.valid = float.IsFinite(projection.uvBoundsRadius) && projection.uvBoundsRadius > 0f;
            return projection;
        }

        internal bool TryProjectWorldPointToUV(int triangleIndex, Vector3 worldPoint,
            Vector3 worldTangent, Vector3 worldBitangent, out Vector2 projectedUV)
        {
            projectedUV = Vector2.zero;
            if (mesh == null || triangleIndex < 0 || worldTangent.sqrMagnitude <= 0.00000001f ||
                worldBitangent.sqrMagnitude <= 0.00000001f) return false;
            EnsureMeshData();
            int offset = triangleIndex * 3;
            if (offset + 2 >= cachedTriangles.Length || cachedUV == null || cachedUV.Length != cachedVertices.Length)
                return false;
            int ia = cachedTriangles[offset], ib = cachedTriangles[offset + 1], ic = cachedTriangles[offset + 2];
            Vector2 uvEdge1 = cachedUV[ib] - cachedUV[ia];
            Vector2 uvEdge2 = cachedUV[ic] - cachedUV[ia];
            float uvDeterminant = uvEdge1.x * uvEdge2.y - uvEdge1.y * uvEdge2.x;
            if (Mathf.Abs(uvDeterminant) < 0.00000001f) return false;
            Transform transform = gameObject.transform;
            Vector3 worldOrigin = transform.TransformPoint(cachedVertices[ia]);
            Vector3 worldEdge1 = transform.TransformVector(cachedVertices[ib] - cachedVertices[ia]);
            Vector3 worldEdge2 = transform.TransformVector(cachedVertices[ic] - cachedVertices[ia]);
            Vector3 worldPerU = (worldEdge1 * uvEdge2.y - worldEdge2 * uvEdge1.y) / uvDeterminant;
            Vector3 worldPerV = (-worldEdge1 * uvEdge2.x + worldEdge2 * uvEdge1.x) / uvDeterminant;
            Vector3 tangent = worldTangent.normalized;
            Vector3 bitangent = worldBitangent.normalized;
            float m00 = Vector3.Dot(worldPerU, tangent), m01 = Vector3.Dot(worldPerV, tangent);
            float m10 = Vector3.Dot(worldPerU, bitangent), m11 = Vector3.Dot(worldPerV, bitangent);
            if (!TryProjectionDeterminant(m00, m01, m10, m11, out float determinant)) return false;
            Vector3 worldDelta = worldPoint - worldOrigin;
            float brushX = Vector3.Dot(worldDelta, tangent);
            float brushY = Vector3.Dot(worldDelta, bitangent);
            float inverseDeterminant = 1f / determinant;
            projectedUV = cachedUV[ia] + new Vector2(
                (m11 * brushX - m01 * brushY) * inverseDeterminant,
                (-m10 * brushX + m00 * brushY) * inverseDeterminant);
            return float.IsFinite(projectedUV.x) && float.IsFinite(projectedUV.y);
        }

        internal static bool TryProjectionDeterminant(float m00, float m01, float m10, float m11,
            out float determinant)
        {
            determinant = m00 * m11 - m01 * m10;
            float frobeniusSquared = m00 * m00 + m01 * m01 + m10 * m10 + m11 * m11;
            if (!float.IsFinite(determinant) || !float.IsFinite(frobeniusSquared) || frobeniusSquared <= 0.0000000001f)
                return false;
            // abs(det) / ||M||F^2 is a scale-independent reciprocal condition estimate. Below this
            // point a projected circle expands hundreds of times along one UV axis and ceases to be
            // a meaningful global stamp.
            return Mathf.Abs(determinant) / frobeniusSquared >= 0.005f;
        }

        public bool TryUVToWorld(Vector2 uv, int preferredTriangle, out Vector3 worldPosition,
            out Vector3 worldNormal, out int triangleIndex, out Vector3 barycentric)
        {
            worldPosition = Vector3.zero; worldNormal = Vector3.up; triangleIndex = -1; barycentric = Vector3.zero;
            if (mesh == null) return false;
            EnsureMeshData();
            if (cachedUV == null || cachedUV.Length != cachedVertices.Length) return false;

            int preferredIsland = preferredTriangle >= 0 && triangleIslands != null && preferredTriangle < triangleIslands.Length
                ? triangleIslands[preferredTriangle] : -1;
            if (preferredTriangle >= 0 && preferredTriangle < cachedTriangles.Length / 3 &&
                TryUVBarycentric(preferredTriangle, uv, out Vector3 preferredBarycentric) &&
                preferredBarycentric.x >= -0.0001f && preferredBarycentric.y >= -0.0001f && preferredBarycentric.z >= -0.0001f)
                return MapTriangle(preferredTriangle, preferredBarycentric, out worldPosition, out worldNormal,
                    out triangleIndex, out barycentric);

            EnsureUVTriangleGrid();
            Vector2Int cell = UVCell(uv);
            uvCandidates.Clear();
            uvSeen.Clear();
            for (int radius = 0; radius <= 3 && uvCandidates.Count == 0; radius++)
            {
                for (int y = cell.y - radius; y <= cell.y + radius; y++)
                for (int x = cell.x - radius; x <= cell.x + radius; x++)
                {
                    if (radius > 0 && x > cell.x - radius && x < cell.x + radius && y > cell.y - radius && y < cell.y + radius) continue;
                    if (!uvTriangleGrid.TryGetValue(new Vector2Int(x, y), out List<int> bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++) if (uvSeen.Add(bucket[i])) uvCandidates.Add(bucket[i]);
                }
            }
            if (preferredTriangle >= 0 && uvSeen.Add(preferredTriangle)) uvCandidates.Add(preferredTriangle);
            if (uvCandidates.Count == 0) return false;

            int selected = -1;
            Vector3 selectedBarycentric = Vector3.zero;
            for (int pass = 0; pass < 2 && selected < 0; pass++)
            {
                for (int i = 0; i < uvCandidates.Count; i++)
                {
                    int candidate = uvCandidates[i];
                    if (pass == 0 && preferredIsland >= 0 && triangleIslands[candidate] != preferredIsland) continue;
                    if (TryUVBarycentric(candidate, uv, out Vector3 bary) && bary.x >= -0.0001f && bary.y >= -0.0001f && bary.z >= -0.0001f)
                    {
                        selected = candidate; selectedBarycentric = bary; break;
                    }
                }
            }
            if (selected < 0)
            {
                float closestDistance = float.MaxValue;
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < uvCandidates.Count; i++)
                    {
                        int candidate = uvCandidates[i];
                        if (pass == 0 && preferredIsland >= 0 && triangleIslands[candidate] != preferredIsland) continue;
                        Vector3 bary = ClosestUVBarycentric(candidate, uv, out float distance);
                        if (distance >= closestDistance) continue;
                        closestDistance = distance; selected = candidate; selectedBarycentric = bary;
                    }
                    if (selected >= 0) break;
                }
            }
            if (selected < 0) return false;
            return MapTriangle(selected, selectedBarycentric, out worldPosition, out worldNormal,
                out triangleIndex, out barycentric);
        }

        private void EnsureMeshData()
        {
            if (cachedVertices != null) return;
            cachedVertices = mesh.vertices;
            cachedNormals = mesh.normals;
            cachedTangents = mesh.tangents;
            cachedUV = mesh.uv;
            cachedTriangles = mesh.triangles;
        }

        internal int GetTriangleBoundaryMask(int triangleIndex)
        {
            EnsureTriangleBoundaryMasks();
            return cachedTriangleBoundaryMasks != null && (uint)triangleIndex < (uint)cachedTriangleBoundaryMasks.Length
                ? cachedTriangleBoundaryMasks[triangleIndex]
                : 7;
        }

        private void EnsureTriangleBoundaryMasks()
        {
            if (cachedTriangleBoundaryMasks != null) return;
            EnsureMeshData();
            int triangleCount = cachedTriangles.Length / 3;
            cachedTriangleBoundaryMasks = new int[triangleCount];
            var counts = new Dictionary<PaintEdgeKey, int>();
            var edges = new PaintEdgeKey[triangleCount * 3];
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int offset = triangle * 3;
                string triangleSlot = GetTriangleSlotName(triangle) ?? string.Empty;
                PaintEdgeVertex a = PaintVertex(cachedTriangles[offset]);
                PaintEdgeVertex b = PaintVertex(cachedTriangles[offset + 1]);
                PaintEdgeVertex c = PaintVertex(cachedTriangles[offset + 2]);
                edges[offset] = new PaintEdgeKey(a, b, triangleSlot);
                edges[offset + 1] = new PaintEdgeKey(b, c, triangleSlot);
                edges[offset + 2] = new PaintEdgeKey(c, a, triangleSlot);
                for (int edge = 0; edge < 3; edge++)
                {
                    PaintEdgeKey key = edges[offset + edge];
                    counts.TryGetValue(key, out int count);
                    counts[key] = count + 1;
                }
            }

            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int offset = triangle * 3;
                int mask = 0;
                for (int edge = 0; edge < 3; edge++)
                    if (counts[edges[offset + edge]] == 1) mask |= 1 << edge;
                cachedTriangleBoundaryMasks[triangle] = mask;
            }
        }

        private PaintEdgeVertex PaintVertex(int vertexIndex)
            => new PaintEdgeVertex(cachedVertices[vertexIndex], cachedUV[vertexIndex]);

        private readonly struct PaintEdgeVertex : IEquatable<PaintEdgeVertex>
        {
            private readonly Vector3 position;
            private readonly Vector2 uv;

            public PaintEdgeVertex(Vector3 position, Vector2 uv)
            {
                this.position = position;
                this.uv = uv;
            }

            public bool Equals(PaintEdgeVertex other) =>
                position.Equals(other.position) && uv.Equals(other.uv);
            public override bool Equals(object obj) => obj is PaintEdgeVertex other && Equals(other);
            public override int GetHashCode() => position.GetHashCode() * 397 ^ uv.GetHashCode();
        }

        private readonly struct PaintEdgeKey : IEquatable<PaintEdgeKey>
        {
            private readonly PaintEdgeVertex first;
            private readonly PaintEdgeVertex second;
            private readonly string slot;

            public PaintEdgeKey(PaintEdgeVertex a, PaintEdgeVertex b, string slot)
            {
                first = a;
                second = b;
                this.slot = slot;
            }

            public bool Equals(PaintEdgeKey other) => string.Equals(slot, other.slot, StringComparison.Ordinal) &&
                (first.Equals(other.first) && second.Equals(other.second) ||
                 first.Equals(other.second) && second.Equals(other.first));
            public override bool Equals(object obj) => obj is PaintEdgeKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int endpoints = first.GetHashCode() ^ second.GetHashCode();
                    return endpoints * 397 ^ StringComparer.Ordinal.GetHashCode(slot ?? string.Empty);
                }
            }
        }

        private void EnsureSpatialTriangleGrid()
        {
            if (spatialTriangleGrid != null) return;
            EnsureMeshData();
            spatialTriangleGrid = new Dictionary<Vector3Int, List<int>>();
            Bounds bounds = mesh.bounds;
            for (int triangle = 0; triangle < cachedTriangles.Length / 3; triangle++)
            {
                int offset = triangle * 3;
                Vector3 a = cachedVertices[cachedTriangles[offset]];
                Vector3 b = cachedVertices[cachedTriangles[offset + 1]];
                Vector3 c = cachedVertices[cachedTriangles[offset + 2]];
                Vector3Int min = SpatialCell(Vector3.Min(a, Vector3.Min(b, c)), bounds);
                Vector3Int max = SpatialCell(Vector3.Max(a, Vector3.Max(b, c)), bounds);
                for (int z = min.z; z <= max.z; z++)
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    Vector3Int key = new Vector3Int(x, y, z);
                    if (!spatialTriangleGrid.TryGetValue(key, out List<int> bucket))
                        spatialTriangleGrid.Add(key, bucket = new List<int>());
                    bucket.Add(triangle);
                }
            }
        }

        private Vector2 ProjectLocalPointToTriangleUV(Vector3 localPoint, int triangleIndex)
        {
            int offset = triangleIndex * 3;
            int ia = cachedTriangles[offset], ib = cachedTriangles[offset + 1], ic = cachedTriangles[offset + 2];
            Vector3 a = cachedVertices[ia];
            Vector3 e0 = cachedVertices[ib] - a;
            Vector3 e1 = cachedVertices[ic] - a;
            Vector3 delta = localPoint - a;
            float d00 = Vector3.Dot(e0, e0), d01 = Vector3.Dot(e0, e1), d11 = Vector3.Dot(e1, e1);
            float d20 = Vector3.Dot(delta, e0), d21 = Vector3.Dot(delta, e1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 0.00000001f) return cachedUV[ia];
            float b = (d11 * d20 - d01 * d21) / denominator;
            float c = (d00 * d21 - d01 * d20) / denominator;
            float aWeight = 1f - b - c;
            return cachedUV[ia] * aWeight + cachedUV[ib] * b + cachedUV[ic] * c;
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c, out Vector3 barycentric)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { barycentric = new Vector3(1f, 0f, 0f); return a; }
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { barycentric = new Vector3(0f, 1f, 0f); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3); barycentric = new Vector3(1f - v, v, 0f); return a + ab * v;
            }
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { barycentric = new Vector3(0f, 0f, 1f); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6); barycentric = new Vector3(1f - w, 0f, w); return a + ac * w;
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                barycentric = new Vector3(0f, 1f - w, w); return b + (c - b) * w;
            }
            float inverse = 1f / (va + vb + vc);
            float insideV = vb * inverse, insideW = vc * inverse;
            barycentric = new Vector3(1f - insideV - insideW, insideV, insideW);
            return a + ab * insideV + ac * insideW;
        }

        private static Vector3Int SpatialCell(Vector3 point, Bounds bounds)
        {
            Vector3 size = bounds.size;
            float x = size.x > 0.000001f ? (point.x - bounds.min.x) / size.x : 0f;
            float y = size.y > 0.000001f ? (point.y - bounds.min.y) / size.y : 0f;
            float z = size.z > 0.000001f ? (point.z - bounds.min.z) / size.z : 0f;
            return new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(x * SpatialGridResolution), 0, SpatialGridResolution - 1),
                Mathf.Clamp(Mathf.FloorToInt(y * SpatialGridResolution), 0, SpatialGridResolution - 1),
                Mathf.Clamp(Mathf.FloorToInt(z * SpatialGridResolution), 0, SpatialGridResolution - 1));
        }

        private bool MapTriangle(int selected, Vector3 selectedBarycentric, out Vector3 worldPosition,
            out Vector3 worldNormal, out int triangleIndex, out Vector3 barycentric)
        {
            int offset = selected * 3;
            int ia = cachedTriangles[offset], ib = cachedTriangles[offset + 1], ic = cachedTriangles[offset + 2];
            Vector3 localPoint = cachedVertices[ia] * selectedBarycentric.x + cachedVertices[ib] * selectedBarycentric.y + cachedVertices[ic] * selectedBarycentric.z;
            Vector3 localNormal = cachedNormals != null && cachedNormals.Length == cachedVertices.Length
                ? cachedNormals[ia] * selectedBarycentric.x + cachedNormals[ib] * selectedBarycentric.y + cachedNormals[ic] * selectedBarycentric.z
                : Vector3.up;
            worldPosition = gameObject.transform.TransformPoint(localPoint);
            worldNormal = gameObject.transform.TransformDirection(localNormal).normalized;
            triangleIndex = selected;
            barycentric = selectedBarycentric;
            return true;
        }

        private void EnsureUVTriangleGrid()
        {
            if (uvTriangleGrid != null) return;
            EnsureMeshData();
            uvTriangleGrid = new Dictionary<Vector2Int, List<int>>();
            Vector2[] uv = cachedUV; int[] triangles = cachedTriangles;
            for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            {
                int offset = triangle * 3;
                Vector2 a = uv[triangles[offset]], b = uv[triangles[offset + 1]], c = uv[triangles[offset + 2]];
                Vector2Int min = UVCell(Vector2.Min(a, Vector2.Min(b, c)));
                Vector2Int max = UVCell(Vector2.Max(a, Vector2.Max(b, c)));
                max.x = Mathf.Min(max.x, min.x + 64); max.y = Mathf.Min(max.y, min.y + 64);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    Vector2Int key = new Vector2Int(x, y);
                    if (!uvTriangleGrid.TryGetValue(key, out List<int> bucket)) uvTriangleGrid.Add(key, bucket = new List<int>());
                    bucket.Add(triangle);
                }
            }
        }

        private bool TryUVBarycentric(int triangleIndex, Vector2 point, out Vector3 barycentric)
        {
            int[] triangles = cachedTriangles; Vector2[] uv = cachedUV; int offset = triangleIndex * 3;
            Vector2 a = uv[triangles[offset]], b = uv[triangles[offset + 1]], c = uv[triangles[offset + 2]];
            float denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denominator) < 0.00000001f) { barycentric = Vector3.zero; return false; }
            float x = ((b.y - c.y) * (point.x - c.x) + (c.x - b.x) * (point.y - c.y)) / denominator;
            float y = ((c.y - a.y) * (point.x - c.x) + (a.x - c.x) * (point.y - c.y)) / denominator;
            barycentric = new Vector3(x, y, 1f - x - y); return true;
        }

        private Vector3 ClosestUVBarycentric(int triangleIndex, Vector2 point, out float squareDistance)
        {
            if (TryUVBarycentric(triangleIndex, point, out Vector3 inside) && inside.x >= 0f && inside.y >= 0f && inside.z >= 0f)
            { squareDistance = 0f; return inside; }
            int[] triangles = cachedTriangles; Vector2[] uv = cachedUV; int offset = triangleIndex * 3;
            Vector2 a = uv[triangles[offset]], b = uv[triangles[offset + 1]], c = uv[triangles[offset + 2]];
            Vector3 best = ClosestOnUVEdge(point, a, b, 0, 1, out squareDistance);
            Vector3 candidate = ClosestOnUVEdge(point, b, c, 1, 2, out float distance);
            if (distance < squareDistance) { squareDistance = distance; best = candidate; }
            candidate = ClosestOnUVEdge(point, c, a, 2, 0, out distance);
            if (distance < squareDistance) { squareDistance = distance; best = candidate; }
            return best;
        }

        private static Vector3 ClosestOnUVEdge(Vector2 point, Vector2 a, Vector2 b, int aIndex, int bIndex, out float squareDistance)
        {
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
            Vector2 closest = Vector2.Lerp(a, b, t); squareDistance = (point - closest).sqrMagnitude;
            Vector3 bary = Vector3.zero; bary[aIndex] = 1f - t; bary[bIndex] = t; return bary;
        }

        private static Vector2Int UVCell(Vector2 uv) => new Vector2Int(Mathf.FloorToInt(uv.x * UVGridResolution), Mathf.FloorToInt(uv.y * UVGridResolution));
    }

    public sealed class TexturePaintLogicalTargetMember
    {
        public SlotDataAsset slotAsset;
        public string slotName;
        public int udimTileNumber;
        public readonly List<ReconstructedSurface> surfaces = new List<ReconstructedSurface>();
        public readonly List<TextureSet> textureSets = new List<TextureSet>();
        public readonly List<OverlayData> sourceOverlays = new List<OverlayData>();
        public readonly List<OverlayData> destinationOverlays = new List<OverlayData>();

        internal void AddSurface(ReconstructedSurface surface)
        {
            if (surface != null && !surfaces.Contains(surface)) surfaces.Add(surface);
        }

        internal void AddDestinationOverlay(OverlayData overlay)
        {
            if (overlay != null && !destinationOverlays.Contains(overlay)) destinationOverlays.Add(overlay);
        }

        internal void BindTextureSet(TextureSet set)
        {
            if (set == null || textureSets.Contains(set)) return;
            textureSets.Add(set);
            for (int sourceIndex = 0; sourceIndex < set.sources.Count; sourceIndex++)
            {
                TextureSourceBinding source = set.sources[sourceIndex];
                if (source?.overlay == null) continue;
                if (source.slotNames.Count > 0 && !source.slotNames.Contains(slotName)) continue;
                if (!sourceOverlays.Contains(source.overlay)) sourceOverlays.Add(source.overlay);
            }
        }
    }

    public sealed class TexturePaintLogicalTarget
    {
        public string id;
        public string displayName;
        public bool isUdim;
        public readonly List<TexturePaintLogicalTargetMember> members = new List<TexturePaintLogicalTargetMember>();

        public bool ContainsSlot(string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            for (int i = 0; i < members.Count; i++)
                if (string.Equals(members[i].slotName, candidate, StringComparison.Ordinal)) return true;
            return false;
        }

        public void ExpandSlotNames(List<string> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            for (int i = 0; i < members.Count; i++)
            {
                string memberSlot = members[i].slotName;
                if (!string.IsNullOrEmpty(memberSlot) && !destination.Contains(memberSlot)) destination.Add(memberSlot);
            }
        }

        internal TexturePaintLogicalTargetMember FindMember(string memberSlot)
        {
            for (int i = 0; i < members.Count; i++)
                if (string.Equals(members[i].slotName, memberSlot, StringComparison.Ordinal)) return members[i];
            return null;
        }
    }

    public sealed class TexturePaintLogicalTargetCatalog
    {
        private readonly List<TexturePaintLogicalTarget> targets = new List<TexturePaintLogicalTarget>();
        private readonly Dictionary<string, TexturePaintLogicalTarget> targetsById =
            new Dictionary<string, TexturePaintLogicalTarget>(StringComparer.Ordinal);
        private readonly Dictionary<string, TexturePaintLogicalTarget> targetsBySlot =
            new Dictionary<string, TexturePaintLogicalTarget>(StringComparer.Ordinal);

        public IReadOnlyList<TexturePaintLogicalTarget> Targets => targets;

        public TexturePaintLogicalTarget FindById(string id)
        {
            return !string.IsNullOrEmpty(id) && targetsById.TryGetValue(id, out TexturePaintLogicalTarget target) ? target : null;
        }

        public TexturePaintLogicalTarget FindBySlot(string slotName)
        {
            return !string.IsNullOrEmpty(slotName) && targetsBySlot.TryGetValue(slotName, out TexturePaintLogicalTarget target) ? target : null;
        }

        public void Rebuild(IReadOnlyList<ReconstructedSurface> surfaces)
        {
            Clear();
            if (surfaces == null) return;
            for (int surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
            {
                ReconstructedSurface surface = surfaces[surfaceIndex];
                if (surface == null) continue;
                if (surface.slots != null && surface.slots.Count > 0)
                {
                    for (int slotIndex = 0; slotIndex < surface.slots.Count; slotIndex++)
                        AddSlot(surface.slots[slotIndex], surface);
                }
                else
                {
                    for (int slotIndex = 0; slotIndex < surface.slotNames.Count; slotIndex++)
                        AddSyntheticSlot(surface.slotNames[slotIndex], surface);
                }
            }

            targets.Sort((left, right) => string.Compare(left.displayName, right.displayName, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < targets.Count; i++)
                targets[i].members.Sort(CompareMembers);
        }

        public void BindTextureSets(IReadOnlyList<TextureSet> sets)
        {
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            for (int memberIndex = 0; memberIndex < targets[targetIndex].members.Count; memberIndex++)
            {
                TexturePaintLogicalTargetMember member = targets[targetIndex].members[memberIndex];
                member.textureSets.Clear();
                member.sourceOverlays.Clear();
            }

            if (sets == null) return;
            for (int setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                TextureSet set = sets[setIndex];
                if (set?.surface == null) continue;
                for (int slotIndex = 0; slotIndex < set.surface.slotNames.Count; slotIndex++)
                {
                    TexturePaintLogicalTarget target = FindBySlot(set.surface.slotNames[slotIndex]);
                    target?.FindMember(set.surface.slotNames[slotIndex])?.BindTextureSet(set);
                }
            }
        }

        public void Clear()
        {
            targets.Clear();
            targetsById.Clear();
            targetsBySlot.Clear();
        }

        private void AddSlot(SlotData slot, ReconstructedSurface surface)
        {
            if (slot == null || string.IsNullOrEmpty(slot.slotName)) return;
            SlotDataAsset asset = slot.asset;
            bool isUdim = asset != null && asset.IsUdimMember;
            string targetId = isUdim ? "udim:" + asset.udimGroupId : "slot:" + slot.slotName;
            string displayName = isUdim && !string.IsNullOrWhiteSpace(asset.udimGroupName)
                ? asset.udimGroupName : slot.slotName;
            TexturePaintLogicalTarget target = GetOrCreateTarget(targetId, displayName, isUdim);
            TexturePaintLogicalTargetMember member = target.FindMember(slot.slotName);
            if (member == null)
            {
                member = new TexturePaintLogicalTargetMember
                {
                    slotAsset = asset,
                    slotName = slot.slotName,
                    udimTileNumber = isUdim ? asset.udimTileNumber : 0
                };
                target.members.Add(member);
                targetsBySlot[slot.slotName] = target;
            }
            member.AddSurface(surface);
            AddDestinationOverlays(member, slot, surface.generatedMaterial);
        }

        private void AddSyntheticSlot(string slotName, ReconstructedSurface surface)
        {
            if (string.IsNullOrEmpty(slotName)) return;
            TexturePaintLogicalTarget target = GetOrCreateTarget("slot:" + slotName, slotName, false);
            TexturePaintLogicalTargetMember member = target.FindMember(slotName);
            if (member == null)
            {
                member = new TexturePaintLogicalTargetMember { slotName = slotName };
                target.members.Add(member);
                targetsBySlot[slotName] = target;
            }
            member.AddSurface(surface);
        }

        private TexturePaintLogicalTarget GetOrCreateTarget(string id, string displayName, bool isUdim)
        {
            if (targetsById.TryGetValue(id, out TexturePaintLogicalTarget target)) return target;
            target = new TexturePaintLogicalTarget { id = id, displayName = displayName, isUdim = isUdim };
            targetsById.Add(id, target);
            targets.Add(target);
            return target;
        }

        private static void AddDestinationOverlays(TexturePaintLogicalTargetMember member, SlotData slot,
            UMAData.GeneratedMaterial generated)
        {
            if (generated?.materialFragments == null) return;
            for (int fragmentIndex = 0; fragmentIndex < generated.materialFragments.Count; fragmentIndex++)
            {
                UMAData.MaterialFragment fragment = generated.materialFragments[fragmentIndex];
                if (fragment?.slotData == null || !ReferenceEquals(fragment.slotData, slot) &&
                    !string.Equals(fragment.slotData.slotName, slot.slotName, StringComparison.Ordinal)) continue;
                if (fragment.overlayList == null) continue;
                for (int overlayIndex = 0; overlayIndex < fragment.overlayList.Count; overlayIndex++)
                    member.AddDestinationOverlay(fragment.overlayList[overlayIndex]);
            }
        }

        private static int CompareMembers(TexturePaintLogicalTargetMember left, TexturePaintLogicalTargetMember right)
        {
            int leftTile = left.udimTileNumber > 0 ? left.udimTileNumber : int.MaxValue;
            int rightTile = right.udimTileNumber > 0 ? right.udimTileNumber : int.MaxValue;
            int tileComparison = leftTile.CompareTo(rightTile);
            return tileComparison != 0 ? tileComparison : string.Compare(left.slotName, right.slotName, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class MeshReconstructionResult : IDisposable
    {
        public GameObject root;
        public readonly List<ReconstructedSurface> surfaces = new List<ReconstructedSurface>();
        public readonly TexturePaintLogicalTargetCatalog logicalTargets = new TexturePaintLogicalTargetCatalog();

        public bool Raycast(Ray ray, out ReconstructedSurface surface, out RaycastHit hit)
        {
            surface = null;
            hit = default;
            float closest = float.MaxValue;
            for (int i = 0; i < surfaces.Count; i++)
            {
                if (!surfaces[i].TryRaycast(ray, out RaycastHit candidate) || candidate.distance >= closest) continue;
                closest = candidate.distance;
                hit = candidate;
                surface = surfaces[i];
            }
            return surface != null;
        }

        public bool RaycastMirroredGlobalX(RaycastHit original, ReconstructedSurface originalSurface,
            out ReconstructedSurface surface, out RaycastHit hit)
        {
            Vector3 point = TexturePaintMath.MirrorAcrossGlobalX(original.point);
            Vector3 normal = TexturePaintMath.MirrorDirectionAcrossGlobalX(original.normal).normalized;
            float offset = Mathf.Max(0.01f, originalSurface != null ? originalSurface.mesh.bounds.size.magnitude * 0.01f : 0.01f);
            if (Raycast(new Ray(point + normal * offset, -normal), out surface, out hit)) return true;
            return Raycast(new Ray(point - normal * offset, normal), out surface, out hit);
        }

        public void Dispose()
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                ReconstructedSurface surface = surfaces[i];
                Destroy(surface.previewMaterial);
                if (surface.ownedStandaloneSourceTextures != null)
                    for (int sourceIndex = 0; sourceIndex < surface.ownedStandaloneSourceTextures.Count; sourceIndex++)
                    {
                        if (surface.ownedStandaloneSourceTextures[sourceIndex] is RenderTexture renderTexture)
                        {
                            if (RenderTexture.active == renderTexture) RenderTexture.active = null;
                            renderTexture.Release();
                        }
                        Destroy(surface.ownedStandaloneSourceTextures[sourceIndex]);
                    }
                Destroy(surface.mesh);
            }
            surfaces.Clear();
            logicalTargets.Clear();
            Destroy(root);
            root = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }

    public static class MeshReconstructor
    {
        public static readonly Vector3 DefaultStandaloneSlotRotationEuler = Vector3.zero;

        internal sealed class SurfaceSlice
        {
            public string suffix;
            public int[] triangles;
            public string[] triangleSlotNames;
            public List<string> slotNames;
            public List<SlotData> slots;
        }

        public static MeshReconstructionResult Reconstruct(DynamicCharacterAvatar avatar)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            if (avatar.umaData == null) throw new InvalidOperationException("The DynamicCharacterAvatar has not generated UMAData.");

            MeshReconstructionResult result = new MeshReconstructionResult
            {
                root = new GameObject(avatar.name + " Overlay Painter Preview")
            };
            result.root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            result.root.transform.localScale = Vector3.one;

            SkinnedMeshRenderer[] renderers = avatar.umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                result.Dispose();
                throw new InvalidOperationException("The DynamicCharacterAvatar has no generated SkinnedMeshRenderer.");
            }

            try
            {
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    SkinnedMeshRenderer sourceRenderer = renderers[rendererIndex];
                    if (sourceRenderer == null || sourceRenderer.sharedMesh == null) continue;
                    Mesh baked = new Mesh { name = sourceRenderer.name + " Overlay Painter Bake", indexFormat = sourceRenderer.sharedMesh.indexFormat };
                    sourceRenderer.BakeMesh(baked);
                    Material[] materials = sourceRenderer.sharedMaterials;
                    int submeshCount = Mathf.Min(baked.subMeshCount, materials.Length);
                    Matrix4x4 toAvatar = avatar.transform.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
                    for (int submesh = 0; submesh < submeshCount; submesh++)
                    {
                        Material sourceMaterial = materials[submesh];
                        if (sourceMaterial == null) continue;
                        FindGeneratedMaterial(avatar.umaData, sourceRenderer, sourceMaterial, submesh,
                            out UMAData.GeneratedMaterial generated, out UMAMaterial umaMaterial);
                        List<SlotData> slots = FindSlots(generated);
                        List<string> slotNames = FindSlotNames(slots, submesh);
                        string[] triangleSlotNames = FindTriangleSlotNames(baked, submesh, generated, slotNames);
                        List<SurfaceSlice> slices = BuildSurfaceSlices(baked.GetTriangles(submesh), triangleSlotNames,
                            slotNames, slots);
                        if (FindCollapsedUdimSlotNames(slots).Count > 0 && slices.Count == 1 && slotNames.Count > 1)
                        {
                            Debug.LogWarning($"Overlay Painter kept '{sourceMaterial.name}' as one preview surface because " +
                                "triangle ownership could not safely separate every UDIM member. Rebuild the affected slots " +
                                "and verify their generated vertex ownership before painting across this target.", avatar);
                        }
                        for (int sliceIndex = 0; sliceIndex < slices.Count; sliceIndex++)
                        {
                            SurfaceSlice slice = slices[sliceIndex];
                            Mesh extracted = ExtractTriangles(baked, slice.triangles, toAvatar,
                                $"Material {submesh}{slice.suffix}");
                            GameObject child = new GameObject($"{rendererIndex:D2}_{submesh:D2}_{sourceMaterial.name}{slice.suffix}");
                            child.transform.SetParent(result.root.transform, false);
                            child.AddComponent<MeshFilter>().sharedMesh = extracted;
                            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                            Material preview = new Material(sourceMaterial)
                            {
                                name = sourceMaterial.name + " (Overlay Painter Preview)",
                                hideFlags = HideFlags.HideAndDontSave
                            };
                            renderer.sharedMaterial = preview;
                            MeshCollider collider = child.AddComponent<MeshCollider>();
                            collider.sharedMesh = extracted;
                            ReconstructedSurface surface = new ReconstructedSurface
                            {
                                index = result.surfaces.Count,
                                rendererIndex = rendererIndex,
                                sourceSubmeshIndex = submesh,
                                gameObject = child,
                                mesh = extracted,
                                collider = collider,
                                sourceMaterial = sourceMaterial,
                                previewMaterial = preview,
                                generatedMaterial = generated,
                                umaMaterial = umaMaterial,
                                triangleIslands = UVIslandUtility.BuildTriangleIslands(extracted),
                                slotName = slice.slotNames[0],
                                slotNames = slice.slotNames,
                                slots = slice.slots,
                                triangleSlotNames = slice.triangleSlotNames
                            };
                            result.surfaces.Add(surface);
                        }
                    }
                    Destroy(baked);
                }
                if (result.surfaces.Count == 0) throw new InvalidOperationException("No paintable material submeshes were reconstructed.");
                result.logicalTargets.Rebuild(result.surfaces);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        public static MeshReconstructionResult ReconstructSlotGroup(TexturePaintLaunchContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.IsStandalone) throw new ArgumentException("A standalone slot launch context is required.", nameof(context));
            if (context.umaMaterial == null || context.umaMaterial.material == null)
                throw new InvalidOperationException("The selected UMAMaterial has no active preview material.");
            if (context.members == null || context.members.Count == 0)
                throw new InvalidOperationException("The standalone slot group contains no members.");

            MeshReconstructionResult result = new MeshReconstructionResult
            {
                root = new GameObject((context.selectedSlot != null ? context.selectedSlot.slotName : "Slot") +
                    " Overlay Painter Preview")
            };
            result.root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            result.root.transform.localScale = Vector3.one;

            try
            {
                for (int memberIndex = 0; memberIndex < context.members.Count; memberIndex++)
                {
                    TexturePaintStandaloneMemberContext member = context.members[memberIndex];
                    SlotDataAsset asset = member?.slot;
                    if (asset == null || UMAMeshData.IsNullOrEmptyMeshData(asset.meshData))
                        throw new InvalidOperationException($"Standalone member {memberIndex + 1} has no usable SlotDataAsset mesh data.");

                    Mesh mesh = BuildSlotMesh(asset.meshData, asset.slotName + " Overlay Painter Mesh",
                        context.fixupRotations, context.slotRotationEuler);
                    if (mesh == null)
                        throw new InvalidOperationException($"Standalone member '{asset.slotName}' could not be converted to a preview mesh.");
                    GameObject child = new GameObject($"{memberIndex:D2}_{asset.slotName}");
                    child.transform.SetParent(result.root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;

                    Material source = context.umaMaterial.material;
                    Material preview = new Material(source)
                    {
                        name = source.name + " (" + asset.slotName + " Overlay Painter Preview)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    Texture[] sourceTextures = BuildStandaloneSources(context.umaMaterial, member.overlay,
                        context.resolution, out List<Texture> ownedSources);
                    ApplyStandaloneSources(preview, context.umaMaterial, sourceTextures);

                    MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                    Material[] previewMaterials = new Material[Mathf.Max(1, mesh.subMeshCount)];
                    for (int materialIndex = 0; materialIndex < previewMaterials.Length; materialIndex++)
                        previewMaterials[materialIndex] = preview;
                    renderer.sharedMaterials = previewMaterials;
                    MeshCollider collider = child.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;

                    SlotData slot = new SlotData(asset);
                    result.surfaces.Add(new ReconstructedSurface
                    {
                        index = result.surfaces.Count,
                        rendererIndex = memberIndex,
                        sourceSubmeshIndex = asset.subMeshIndex,
                        gameObject = child,
                        mesh = mesh,
                        collider = collider,
                        sourceMaterial = source,
                        previewMaterial = preview,
                        umaMaterial = context.umaMaterial,
                        standaloneSourceTextures = sourceTextures,
                        ownedStandaloneSourceTextures = ownedSources,
                        standaloneSourceOverlay = member.overlay,
                        allowMissingSourceTextures = true,
                        triangleIslands = UVIslandUtility.BuildTriangleIslands(mesh),
                        slotName = asset.slotName,
                        slotNames = new List<string> { asset.slotName },
                        slots = new List<SlotData> { slot }
                    });
                }
                result.logicalTargets.Rebuild(result.surfaces);
                if (result.logicalTargets.Targets.Count != 1)
                    throw new InvalidOperationException("The selected slot assets did not resolve to one logical paint target.");
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static Mesh BuildSlotMesh(UMAMeshData data, string meshName, bool fixupRotations,
            Vector3 rotationEuler)
        {
            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = data.vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            SlotDataAsset.TryGetCanonicalMeshFromRootMatrix(data, meshName, out Matrix4x4 canonicalMeshFromRoot);
            Matrix4x4 additionalRotation = fixupRotations
                ? Matrix4x4.Rotate(Quaternion.Euler(rotationEuler))
                : Matrix4x4.identity;
            Matrix4x4 meshTransform = additionalRotation * canonicalMeshFromRoot;
            Matrix4x4 normalTransform = meshTransform.inverse.transpose;
            bool flipTangentHandedness = IsMirrored(meshTransform);
            Vector3[] vertices = data.vertices != null ? (Vector3[])data.vertices.Clone() : Array.Empty<Vector3>();
            for (int i = 0; i < vertices.Length; i++) vertices[i] = meshTransform.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            int vertexCount = mesh.vertexCount;
            if (data.normals != null && data.normals.Length == vertexCount)
            {
                Vector3[] normals = (Vector3[])data.normals.Clone();
                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = normalTransform.MultiplyVector(normals[i]);
                    if (normals[i].sqrMagnitude > 0.00000001f) normals[i].Normalize();
                }
                mesh.normals = normals;
            }
            if (data.tangents != null && data.tangents.Length == vertexCount)
            {
                Vector4[] tangents = (Vector4[])data.tangents.Clone();
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 direction = meshTransform.MultiplyVector(
                        new Vector3(tangents[i].x, tangents[i].y, tangents[i].z));
                    if (direction.sqrMagnitude > 0.00000001f) direction.Normalize();
                    float handedness = flipTangentHandedness ? -tangents[i].w : tangents[i].w;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, handedness);
                }
                mesh.tangents = tangents;
            }
            if (data.colors32 != null && data.colors32.Length == vertexCount) mesh.colors32 = (Color32[])data.colors32.Clone();
            if (data.uv != null && data.uv.Length == vertexCount) mesh.uv = (Vector2[])data.uv.Clone();
            if (data.uv2 != null && data.uv2.Length == vertexCount) mesh.uv2 = (Vector2[])data.uv2.Clone();
            if (data.uv3 != null && data.uv3.Length == vertexCount) mesh.uv3 = (Vector2[])data.uv3.Clone();
            if (data.uv4 != null && data.uv4.Length == vertexCount) mesh.uv4 = (Vector2[])data.uv4.Clone();
            int submeshCount = Mathf.Min(data.subMeshCount, data.submeshes != null ? data.submeshes.Length : 0);
            mesh.subMeshCount = Mathf.Max(1, submeshCount);
            for (int submesh = 0; submesh < submeshCount; submesh++)
                mesh.SetIndices(data.submeshes[submesh]?.GetBaseTriangles() ?? Array.Empty<int>(),
                    MeshTopology.Triangles, submesh, false);
            if (submeshCount == 0) mesh.SetIndices(Array.Empty<int>(), MeshTopology.Triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool IsMirrored(Matrix4x4 transform)
        {
            Vector3 xAxis = transform.MultiplyVector(Vector3.right);
            Vector3 yAxis = transform.MultiplyVector(Vector3.up);
            Vector3 zAxis = transform.MultiplyVector(Vector3.forward);
            return Vector3.Dot(Vector3.Cross(xAxis, yAxis), zAxis) < 0f;
        }

        private static Texture[] BuildStandaloneSources(UMAMaterial umaMaterial, OverlayDataAsset overlay,
            int resolution, out List<Texture> ownedSources)
        {
            ownedSources = new List<Texture>();
            int count = umaMaterial.channels != null ? umaMaterial.channels.Length : 0;
            Texture[] sources = new Texture[count];
            Texture[] overlayTextures = overlay != null ? overlay.textureList : null;
            for (int i = 0; i < count; i++)
            {
                sources[i] = overlayTextures != null && i < overlayTextures.Length ? overlayTextures[i] : null;
                if (sources[i] != null) continue;
                Color neutral = NeutralPhysicalColor(umaMaterial.channels[i], umaMaterial.material);
                RenderTexture generated = EditableTextureTarget.Create(
                    $"{umaMaterial.name} Channel {i} Semantic Neutral", Mathf.Clamp(resolution, 128, 4096),
                    Mathf.Clamp(resolution, 128, 4096), RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = generated;
                GL.Clear(false, true, neutral);
                RenderTexture.active = previous;
                sources[i] = generated;
                ownedSources.Add(generated);
            }
            return sources;
        }

        private static Color NeutralPhysicalColor(UMAMaterial.MaterialChannel channel, Material material)
        {
#if UNITY_EDITOR
            UMAMaterial.TextureChannelLayout layout = UMAMaterial.GetTextureChannelLayout(channel, material);
            bool ordinaryNormal = layout.red == UMAMaterial.TextureChannelUsage.Normal &&
                                  layout.green == UMAMaterial.TextureChannelUsage.Normal &&
                                  layout.blue == UMAMaterial.TextureChannelUsage.Normal &&
                                  (layout.alpha == UMAMaterial.TextureChannelUsage.Unused ||
                                   layout.alpha == UMAMaterial.TextureChannelUsage.Opacity);
            if (ordinaryNormal) return new Color(0.5f, 0.5f, 1f, 1f);
            return new Color(NeutralComponent(layout.red, 0), NeutralComponent(layout.green, 1),
                NeutralComponent(layout.blue, 2), NeutralComponent(layout.alpha, 3));
#else
            return Color.black;
#endif
        }

#if UNITY_EDITOR
        private static float NeutralComponent(UMAMaterial.TextureChannelUsage usage, int component)
        {
            if ((usage & UMAMaterial.TextureChannelUsage.Normal) != 0)
                return component == 2 ? 1f : component < 2 ? 0.5f : 1f;
            if ((usage & (UMAMaterial.TextureChannelUsage.Albedo | UMAMaterial.TextureChannelUsage.Opacity |
                          UMAMaterial.TextureChannelUsage.AmbientOcclusion |
                          UMAMaterial.TextureChannelUsage.Roughness)) != 0) return 1f;
            if ((usage & UMAMaterial.TextureChannelUsage.Smoothness) != 0) return 0f;
            if ((usage & (UMAMaterial.TextureChannelUsage.DetailNormalX |
                          UMAMaterial.TextureChannelUsage.DetailNormalY |
                          UMAMaterial.TextureChannelUsage.DetailAlbedo |
                          UMAMaterial.TextureChannelUsage.DetailSmoothness)) != 0) return 0.5f;
            return 0f;
        }
#endif

        private static void ApplyStandaloneSources(Material preview, UMAMaterial umaMaterial, Texture[] sources)
        {
            if (preview == null || umaMaterial?.channels == null) return;
            for (int i = 0; i < umaMaterial.channels.Length; i++)
            {
                string property = umaMaterial.channels[i].materialPropertyName;
                if (!string.IsNullOrEmpty(property) && preview.HasProperty(property))
                    preview.SetTexture(property, sources != null && i < sources.Length ? sources[i] : null);
            }
        }

        public static Vector2 TriangleUV(Mesh mesh, int triangleIndex, Vector3 barycentric)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            int[] triangles = mesh.triangles;
            Vector2[] uv = mesh.uv;
            int offset = triangleIndex * 3;
            if (offset < 0 || offset + 2 >= triangles.Length) throw new ArgumentOutOfRangeException(nameof(triangleIndex));
            return TexturePaintMath.BarycentricToUV(uv[triangles[offset]], uv[triangles[offset + 1]], uv[triangles[offset + 2]], barycentric);
        }

        private static Mesh ExtractTriangles(Mesh source, int[] sourceTriangles, Matrix4x4 transform, string nameSuffix)
        {
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Vector2[] sourceUV = source.uv;
            Color32[] sourceColors = source.colors32;
            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uv = new List<Vector2>();
            List<Color32> colors = new List<Color32>();
            int[] triangles = new int[sourceTriangles.Length];
            Matrix4x4 normalMatrix = transform.inverse.transpose;
            for (int i = 0; i < sourceTriangles.Length; i++)
            {
                int sourceIndex = sourceTriangles[i];
                if (!remap.TryGetValue(sourceIndex, out int destination))
                {
                    destination = vertices.Count;
                    remap.Add(sourceIndex, destination);
                    vertices.Add(transform.MultiplyPoint3x4(sourceVertices[sourceIndex]));
                    if (sourceNormals.Length == sourceVertices.Length)
                        normals.Add(normalMatrix.MultiplyVector(sourceNormals[sourceIndex]).normalized);
                    if (sourceTangents.Length == sourceVertices.Length)
                    {
                        Vector4 st = sourceTangents[sourceIndex];
                        Vector3 t = transform.MultiplyVector(new Vector3(st.x, st.y, st.z)).normalized;
                        tangents.Add(new Vector4(t.x, t.y, t.z, st.w));
                    }
                    if (sourceUV.Length == sourceVertices.Length) uv.Add(sourceUV[sourceIndex]);
                    if (sourceColors.Length == sourceVertices.Length) colors.Add(sourceColors[sourceIndex]);
                }
                triangles[i] = destination;
            }
            Mesh mesh = new Mesh { name = source.name + " " + nameSuffix, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            if (normals.Count == vertices.Count) mesh.SetNormals(normals);
            if (tangents.Count == vertices.Count) mesh.SetTangents(tangents);
            if (uv.Count == vertices.Count) mesh.SetUVs(0, uv);
            if (colors.Count == vertices.Count) mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            if (normals.Count != vertices.Count) mesh.RecalculateNormals();
            if (tangents.Count != vertices.Count && uv.Count == vertices.Count) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static List<SurfaceSlice> BuildSurfaceSlices(int[] sourceTriangles, string[] triangleSlotNames,
            List<string> surfaceSlotNames, List<SlotData> slots)
        {
            HashSet<string> splitSlotNames = FindCollapsedUdimSlotNames(slots);
            if (splitSlotNames.Count == 0)
                return new List<SurfaceSlice> { CreateSurfaceSlice(string.Empty, sourceTriangles, triangleSlotNames, surfaceSlotNames, slots) };

            // Splitting is only safe when every triangle has authoritative slot ownership and every
            // UDIM member we intend to split owns geometry on this surface. Otherwise retain the
            // original surface instead of fabricating or dropping a physical paint endpoint.
            var ownedSplitSlots = new HashSet<string>(StringComparer.Ordinal);
            int expectedTriangleCount = sourceTriangles.Length / 3;
            if (triangleSlotNames == null || triangleSlotNames.Length != expectedTriangleCount)
                return new List<SurfaceSlice> { CreateSurfaceSlice(string.Empty, sourceTriangles, triangleSlotNames, surfaceSlotNames, slots) };
            for (int triangleIndex = 0; triangleIndex < triangleSlotNames.Length; triangleIndex++)
            {
                string owner = triangleSlotNames[triangleIndex];
                if (string.IsNullOrEmpty(owner))
                    return new List<SurfaceSlice> { CreateSurfaceSlice(string.Empty, sourceTriangles, triangleSlotNames, surfaceSlotNames, slots) };
                if (splitSlotNames.Contains(owner)) ownedSplitSlots.Add(owner);
            }
            if (!ownedSplitSlots.SetEquals(splitSlotNames))
                return new List<SurfaceSlice> { CreateSurfaceSlice(string.Empty, sourceTriangles, triangleSlotNames, surfaceSlotNames, slots) };

            var trianglesBySlot = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var ownersBySlot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var residualTriangles = new List<int>();
            var residualOwners = new List<string>();
            int triangleCount = sourceTriangles.Length / 3;
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                string owner = triangleSlotNames != null && triangleIndex < triangleSlotNames.Length
                    ? triangleSlotNames[triangleIndex] : null;
                List<int> destinationTriangles = residualTriangles;
                List<string> destinationOwners = residualOwners;
                if (!string.IsNullOrEmpty(owner) && splitSlotNames.Contains(owner))
                {
                    if (!trianglesBySlot.TryGetValue(owner, out destinationTriangles))
                    {
                        destinationTriangles = new List<int>();
                        trianglesBySlot.Add(owner, destinationTriangles);
                        destinationOwners = new List<string>();
                        ownersBySlot.Add(owner, destinationOwners);
                    }
                    else destinationOwners = ownersBySlot[owner];
                }
                int offset = triangleIndex * 3;
                destinationTriangles.Add(sourceTriangles[offset]);
                destinationTriangles.Add(sourceTriangles[offset + 1]);
                destinationTriangles.Add(sourceTriangles[offset + 2]);
                destinationOwners.Add(owner);
            }

            var result = new List<SurfaceSlice>();
            var emittedSlots = new HashSet<string>(StringComparer.Ordinal);
            List<SlotData> orderedSlots = new List<SlotData>(slots);
            orderedSlots.Sort((left, right) =>
            {
                int leftTile = left?.asset != null ? left.asset.udimTileNumber : int.MaxValue;
                int rightTile = right?.asset != null ? right.asset.udimTileNumber : int.MaxValue;
                int comparison = leftTile.CompareTo(rightTile);
                return comparison != 0 ? comparison : string.Compare(left?.slotName, right?.slotName, StringComparison.OrdinalIgnoreCase);
            });
            for (int slotIndex = 0; slotIndex < orderedSlots.Count; slotIndex++)
            {
                SlotData slot = orderedSlots[slotIndex];
                if (slot == null || !trianglesBySlot.TryGetValue(slot.slotName, out List<int> memberTriangles) ||
                    memberTriangles.Count == 0) continue;
                emittedSlots.Add(slot.slotName);
                result.Add(CreateSurfaceSlice("_" + slot.slotName, memberTriangles.ToArray(),
                    ownersBySlot[slot.slotName].ToArray(), new List<string> { slot.slotName }, new List<SlotData> { slot }));
            }

            if (residualTriangles.Count > 0)
            {
                var residualSlots = new List<SlotData>();
                var residualSlotNames = new List<string>();
                for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    SlotData slot = slots[slotIndex];
                    if (slot == null || emittedSlots.Contains(slot.slotName)) continue;
                    residualSlots.Add(slot);
                    if (!residualSlotNames.Contains(slot.slotName)) residualSlotNames.Add(slot.slotName);
                }
                for (int ownerIndex = 0; ownerIndex < residualOwners.Count; ownerIndex++)
                    if (!string.IsNullOrEmpty(residualOwners[ownerIndex]) && !residualSlotNames.Contains(residualOwners[ownerIndex]))
                        residualSlotNames.Add(residualOwners[ownerIndex]);
                if (residualSlotNames.Count == 0) residualSlotNames.Add(surfaceSlotNames[0]);
                result.Add(CreateSurfaceSlice("_Other", residualTriangles.ToArray(), residualOwners.ToArray(),
                    residualSlotNames, residualSlots));
            }

            return result.Count > 0
                ? result
                : new List<SurfaceSlice> { CreateSurfaceSlice(string.Empty, sourceTriangles, triangleSlotNames, surfaceSlotNames, slots) };
        }

        private static HashSet<string> FindCollapsedUdimSlotNames(List<SlotData> slots)
        {
            var slotsByGroup = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Count; i++)
            {
                SlotData slot = slots[i];
                SlotDataAsset asset = slot?.asset;
                if (asset == null || !asset.IsUdimMember || string.IsNullOrEmpty(slot.slotName)) continue;
                if (!slotsByGroup.TryGetValue(asset.udimGroupId, out HashSet<string> groupSlots))
                {
                    groupSlots = new HashSet<string>(StringComparer.Ordinal);
                    slotsByGroup.Add(asset.udimGroupId, groupSlots);
                }
                groupSlots.Add(slot.slotName);
            }

            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (HashSet<string> groupSlots in slotsByGroup.Values)
                if (groupSlots.Count > 1) result.UnionWith(groupSlots);
            return result;
        }

        private static SurfaceSlice CreateSurfaceSlice(string suffix, int[] triangles, string[] triangleSlotNames,
            List<string> slotNames, List<SlotData> slots)
        {
            return new SurfaceSlice
            {
                suffix = suffix,
                triangles = triangles,
                triangleSlotNames = triangleSlotNames,
                slotNames = new List<string>(slotNames),
                slots = new List<SlotData>(slots)
            };
        }

        private static void FindGeneratedMaterial(UMAData data, SkinnedMeshRenderer renderer, Material material, int materialIndex,
            out UMAData.GeneratedMaterial generated, out UMAMaterial umaMaterial)
        {
            generated = null;
            umaMaterial = null;
            if (data.generatedMaterials == null || data.generatedMaterials.materials == null) return;
            List<UMAData.GeneratedMaterial> candidates = data.generatedMaterials.materials;
            // Prefer exact generated material instances. A material index is only meaningful inside its renderer.
            for (int i = 0; i < candidates.Count; i++)
            {
                UMAData.GeneratedMaterial candidate = candidates[i];
                if (candidate == null) continue;
                if (candidate.material != material || (candidate.skinnedMeshRenderer != null && candidate.skinnedMeshRenderer != renderer)) continue;
                generated = candidate;
                umaMaterial = candidate.umaMaterial;
                return;
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                UMAData.GeneratedMaterial candidate = candidates[i];
                if (candidate == null || candidate.skinnedMeshRenderer != renderer || candidate.materialIndex != materialIndex) continue;
                generated = candidate; umaMaterial = candidate.umaMaterial; return;
            }
        }

        private static List<SlotData> FindSlots(UMAData.GeneratedMaterial generated)
        {
            var result = new List<SlotData>();
            if (generated?.materialFragments == null) return result;
            for (int i = 0; i < generated.materialFragments.Count; i++)
            {
                SlotData slot = generated.materialFragments[i]?.slotData;
                if (slot != null && !result.Contains(slot)) result.Add(slot);
            }
            return result;
        }

        private static List<string> FindSlotNames(List<SlotData> slots, int submesh)
        {
            List<string> result = new List<string>();
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    SlotData slot = slots[i];
                    if (slot != null && !string.IsNullOrEmpty(slot.slotName) && !result.Contains(slot.slotName))
                        result.Add(slot.slotName);
                }
            }
            if (result.Count == 0) result.Add($"Material {submesh}");
            return result;
        }

        private static string[] FindTriangleSlotNames(Mesh source, int submesh, UMAData.GeneratedMaterial generated,
            List<string> surfaceSlotNames)
        {
            int[] triangles = source.GetTriangles(submesh);
            string[] result = new string[triangles.Length / 3];
            if (generated?.materialFragments != null)
            {
                for (int triangle = 0; triangle < result.Length; triangle++)
                {
                    int offset = triangle * 3;
                    for (int fragmentIndex = 0; fragmentIndex < generated.materialFragments.Count; fragmentIndex++)
                    {
                        SlotData slot = generated.materialFragments[fragmentIndex]?.slotData;
                        if (slot?.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData)) continue;
                        if (!slot.OwnsVertex(triangles[offset]) || !slot.OwnsVertex(triangles[offset + 1]) ||
                            !slot.OwnsVertex(triangles[offset + 2])) continue;
                        result[triangle] = slot.slotName;
                        break;
                    }
                }
            }
            if (surfaceSlotNames.Count == 1)
                for (int triangle = 0; triangle < result.Length; triangle++) result[triangle] = surfaceSlotNames[0];
            return result;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
