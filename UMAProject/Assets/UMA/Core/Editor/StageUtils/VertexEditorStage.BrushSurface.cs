using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Where the Sculpt and Vertex Paint brushes meet the mesh, and which way they face there.
    /// </summary>
    /// <remarks>
    /// The stage's collider is one mesh for the whole character, and a physics raycast returns one
    /// hit per collider, so a locked slot in front of an editable one hid it from the brush. With
    /// <b>Ignore Locked Slots</b> on (the default) the brush ray is tested against the editable slots'
    /// own triangles, using the current (sculpted) vertices, and passes through every other slot.
    /// Off, the first surface the ray meets decides, as before: a locked slot in front blocks the brush.
    /// <para>
    /// The brush faces the way the vertex normals under it face, counting only the normals on the
    /// side of the surface the ray hit. Counting every normal within the radius cancelled out on
    /// two-layer garments (an outer and an inner shell a few millimetres apart, facing opposite ways)
    /// and turned the brush sideways or inward.
    /// </para>
    /// </remarks>
    public partial class VertexEditorStage
    {
        // Remembered with the other tool choices (VertexEditorToolSettings).
        private bool brushIgnoresLockedSlots = true;

        /// <summary>
        /// Sculpt brushes move only vertices whose normal faces the brush: a stroke on the outside of a
        /// two-layer garment leaves its inner layer alone, and the far side of a thin part is not pushed.
        /// </summary>
        private bool sculptFrontFacesOnly;

        // The baked normals while a sculpt sample is applied with Front Faces Only on; null otherwise.
        private readonly List<Vector3> sculptFacingNormals = new List<Vector3>();
        private bool sculptFacingNormalsValid;

        /// <summary>The Ignore Locked Slots toggle, shared by the Sculpt and Vertex Paint brushes.</summary>
        private void DrawIgnoreLockedSlotsOption()
        {
            bool ignore = UnityEditor.EditorGUILayout.Toggle(
                new GUIContent(
                    "Ignore Locked Slots",
                    "The brush passes through slots that are not editable (locked or not soloed in the Slots panel) and reaches the editable slot behind them. " +
                    "Off: a locked slot in front blocks the brush. Shared by Sculpt and Vertex Paint."),
                brushIgnoresLockedSlots);
            if (ignore != brushIgnoresLockedSlots)
            {
                brushIgnoresLockedSlots = ignore;
                UnityEditor.SceneView.RepaintAll();
            }
        }

        private bool IsSculptBrushTarget(SlotData slot)
        {
            return slot != null && IsSculptSlotIncludedInTarget(slot) &&
                   (IsSculptMultiSlotMode || (sculptSlot != null && slot.slotName == sculptSlot.slotName));
        }

        private bool IsVertexPaintBrushTarget(SlotData slot)
        {
            return slot != null && IsSlotEditable(slot) &&
                   (IsVertexPaintAllSlotsMode || (vertexPaintSlot != null && slot.slotName == vertexPaintSlot.slotName));
        }

        /// <summary>Reads the baked normals once for the samples of one mouse event, when Front Faces Only needs them.</summary>
        private void PrepareSculptFacingNormals()
        {
            sculptFacingNormalsValid = false;
            if (!sculptFrontFacesOnly || BakedMesh == null)
            {
                return;
            }
            BakedMesh.GetNormals(sculptFacingNormals);
            sculptFacingNormalsValid = sculptFacingNormals.Count == BakedMesh.vertexCount;
        }

        /// <summary>False when Front Faces Only is on and the baked vertex faces away from <paramref name="brushNormal"/>.</summary>
        private bool SculptVertexFacesBrush(int bakedIndex, Vector3 brushNormal)
        {
            if (!sculptFrontFacesOnly || !sculptFacingNormalsValid || bakedIndex < 0 || bakedIndex >= sculptFacingNormals.Count)
            {
                return true;
            }
            return Vector3.Dot(sculptFacingNormals[bakedIndex], brushNormal) > 0f;
        }

        /// <summary>A brush ray's hit, in the baked mesh's (VertexObject's) space.</summary>
        private struct BrushSurfaceHit
        {
            public int triangleIndex;
            public SlotData slot;
            public Vector3 localPoint;
            /// <summary>The hit triangle's own normal, facing the viewer.</summary>
            public Vector3 localFaceNormal;
        }

        // Each slot's triangles (start indices into the baked triangle list) and vertex range,
        // rebuilt when the baked mesh is replaced.
        private int[] brushTriangleCacheSource;
        private readonly Dictionary<SlotData, int[]> brushTrianglesBySlot = new Dictionary<SlotData, int[]>();
        private readonly Dictionary<SlotData, Vector2Int> brushVertexRangeBySlot = new Dictionary<SlotData, Vector2Int>();

        /// <summary>
        /// The surface a brush ray meets on a slot that <paramref name="isTarget"/> accepts.
        /// <paramref name="vertices"/> are the baked mesh's current vertices.
        /// </summary>
        private bool TryFindBrushSurface(Ray worldRay, Func<SlotData, bool> isTarget, List<Vector3> vertices, out BrushSurfaceHit hit)
        {
            hit = default;
            if (VertexObject == null || BakedMesh == null || vertices == null || vertices.Count == 0)
            {
                return false;
            }
            Transform space = VertexObject.transform;
            Vector3 origin = space.InverseTransformPoint(worldRay.origin);
            Vector3 direction = space.InverseTransformDirection(worldRay.direction);
            if (direction.sqrMagnitude < 1e-12f)
            {
                return false;
            }
            direction.Normalize();
            int[] triangles = GetBakedTriangles();

            // The first surface the ray meets, of any slot. When that is an editable slot it is also
            // the nearest editable surface, so the triangle search below is needed only when a locked
            // slot is in front (or the collider has no hit).
            if (phyScene.IsValid())
            {
                RaycastHit[] hits = HitTestBuffer;
                int count = phyScene.Raycast(worldRay.origin, worldRay.direction, hits, 10000f);
                Array.Sort(hits, 0, count, RaycastHitDistanceComparer.Instance);
                for (int i = 0; i < count; i++)
                {
                    if (hits[i].collider == null || hits[i].collider.gameObject != VertexObject)
                    {
                        continue;
                    }
                    SlotData slot = GetSlotForTriangle(hits[i].triangleIndex);
                    int first = hits[i].triangleIndex * 3;
                    if (slot != null && isTarget(slot) && first >= 0 && first + 2 < triangles.Length)
                    {
                        hit.triangleIndex = hits[i].triangleIndex;
                        hit.slot = slot;
                        hit.localPoint = space.InverseTransformPoint(hits[i].point);
                        hit.localFaceNormal = FaceNormalTowardRay(vertices, triangles, first, direction);
                        return true;
                    }
                    break;
                }
            }
            if (!brushIgnoresLockedSlots)
            {
                // A locked slot in front blocks the brush.
                return false;
            }

            // The Scene GUI asks for the same ray several times a frame (Layout, Repaint, the mouse
            // event). While no stroke is moving vertices, the last search answers it again.
            bool cacheable = !sculpting && !vertexPainting;
            if (cacheable && brushSearchCacheValid && brushSearchCacheMesh == BakedMesh &&
                brushSearchCacheOrigin == origin && brushSearchCacheDirection == direction &&
                brushSearchCacheTargetsVersion == slotTargets.Version &&
                brushSearchCacheTargetMethod == isTarget.Method && ReferenceEquals(brushSearchCacheSculptSlot, sculptSlot) &&
                ReferenceEquals(brushSearchCachePaintSlot, vertexPaintSlot))
            {
                hit = brushSearchCacheHit;
                return brushSearchCacheFound;
            }
            bool found = SearchEditableTriangles(origin, direction, isTarget, vertices, triangles, out hit);
            brushSearchCacheValid = cacheable;
            brushSearchCacheMesh = BakedMesh;
            brushSearchCacheOrigin = origin;
            brushSearchCacheDirection = direction;
            brushSearchCacheTargetsVersion = slotTargets.Version;
            brushSearchCacheTargetMethod = isTarget.Method;
            brushSearchCacheSculptSlot = sculptSlot;
            brushSearchCachePaintSlot = vertexPaintSlot;
            brushSearchCacheHit = hit;
            brushSearchCacheFound = found;
            return found;
        }

        private bool brushSearchCacheValid;
        private Mesh brushSearchCacheMesh;
        private Vector3 brushSearchCacheOrigin;
        private Vector3 brushSearchCacheDirection;
        private int brushSearchCacheTargetsVersion;
        private System.Reflection.MethodInfo brushSearchCacheTargetMethod;
        private SlotData brushSearchCacheSculptSlot;
        private SlotData brushSearchCachePaintSlot;
        private BrushSurfaceHit brushSearchCacheHit;
        private bool brushSearchCacheFound;

        /// <summary>The nearest front-facing triangle of an accepted slot along the ray (local space).</summary>
        private bool SearchEditableTriangles(
            Vector3 origin, Vector3 direction, Func<SlotData, bool> isTarget, List<Vector3> vertices, int[] triangles,
            out BrushSurfaceHit hit)
        {
            hit = default;
            EnsureBrushTriangleCache(triangles);
            float nearest = float.PositiveInfinity;
            foreach (KeyValuePair<SlotData, int[]> entry in brushTrianglesBySlot)
            {
                SlotData slot = entry.Key;
                if (!isTarget(slot) || !brushVertexRangeBySlot.TryGetValue(slot, out Vector2Int range) ||
                    range.x < 0 || range.x + range.y > vertices.Count)
                {
                    continue;
                }
                // Skip slots the ray cannot reach.
                Bounds bounds = new Bounds(vertices[range.x], Vector3.zero);
                for (int v = 1; v < range.y; v++)
                {
                    bounds.Encapsulate(vertices[range.x + v]);
                }
                bounds.Expand(0.0001f);
                if (!bounds.IntersectRay(new Ray(origin, direction), out float boundsDistance) || boundsDistance > nearest)
                {
                    continue;
                }
                int[] slotTriangles = entry.Value;
                for (int t = 0; t < slotTriangles.Length; t++)
                {
                    int first = slotTriangles[t];
                    if (RayHitsFrontOfTriangle(origin, direction,
                            vertices[triangles[first]], vertices[triangles[first + 1]], vertices[triangles[first + 2]],
                            out float distance) && distance < nearest)
                    {
                        nearest = distance;
                        hit.triangleIndex = first / 3;
                        hit.slot = slot;
                    }
                }
            }
            if (float.IsPositiveInfinity(nearest))
            {
                return false;
            }
            hit.localPoint = origin + (direction * nearest);
            hit.localFaceNormal = FaceNormalTowardRay(vertices, triangles, hit.triangleIndex * 3, direction);
            return true;
        }

        private void EnsureBrushTriangleCache(int[] triangles)
        {
            if (ReferenceEquals(brushTriangleCacheSource, triangles) && brushTrianglesBySlot.Count > 0)
            {
                return;
            }
            brushTriangleCacheSource = triangles;
            brushTrianglesBySlot.Clear();
            brushVertexRangeBySlot.Clear();
            int vertexCount = BakedMesh.vertexCount;
            var owners = new int[vertexCount];
            for (int i = 0; i < owners.Length; i++) owners[i] = -1;
            var slots = new List<SlotData>();
            foreach (SlotData slot in RecipeSlots)
            {
                if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    continue;
                }
                int start = GetVisibleBakedVertexIndex(slot, 0);
                int count = slot.asset.meshData.vertexCount;
                if (start < 0 || start + count > vertexCount)
                {
                    continue;
                }
                for (int v = 0; v < count; v++) owners[start + v] = slots.Count;
                brushVertexRangeBySlot[slot] = new Vector2Int(start, count);
                slots.Add(slot);
            }
            var lists = new List<int>[slots.Count];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<int>();
            for (int first = 0; first + 2 < triangles.Length; first += 3)
            {
                int owner = triangles[first] < vertexCount ? owners[triangles[first]] : -1;
                if (owner >= 0) lists[owner].Add(first);
            }
            for (int i = 0; i < slots.Count; i++)
            {
                brushTrianglesBySlot[slots[i]] = lists[i].ToArray();
            }
        }

        /// <summary>Möller–Trumbore, front faces only (Unity's winding, as the physics raycasts use).</summary>
        private static bool RayHitsFrontOfTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            // Positive when the triangle faces the ray: its normal, Cross(edge1, edge2), points back along it.
            if (determinant < 1e-12f)
            {
                return false;
            }
            float inverse = 1f / determinant;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }
            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance > 0f;
        }

        private static Vector3 FaceNormalTowardRay(List<Vector3> vertices, int[] triangles, int first, Vector3 rayDirection)
        {
            if (first < 0 || first + 2 >= triangles.Length)
            {
                return -rayDirection;
            }
            Vector3 a = vertices[triangles[first]];
            Vector3 normal = Vector3.Cross(vertices[triangles[first + 1]] - a, vertices[triangles[first + 2]] - a);
            if (normal.sqrMagnitude < 1e-20f)
            {
                return -rayDirection;
            }
            normal.Normalize();
            return Vector3.Dot(normal, rayDirection) > 0f ? -normal : normal;
        }

        /// <summary>
        /// The brush direction: the falloff-weighted average of the vertex normals within
        /// <paramref name="radius"/> of <paramref name="localPoint"/>, counting only normals on the hit
        /// face's side. Falls back to the face normal when none qualify.
        /// </summary>
        private static Vector3 AverageFacingNormal(
            List<Vector3> vertices, List<Vector3> normals, int start, int count, Vector3 localPoint,
            float radius, Vector3 faceNormal, Func<float, float> falloff)
        {
            if (radius <= 0f || start < 0 || start + count > vertices.Count || start + count > normals.Count)
            {
                return faceNormal;
            }
            Vector3 sum = Vector3.zero;
            float radiusSquared = radius * radius;
            for (int i = start; i < start + count; i++)
            {
                float distanceSquared = (vertices[i] - localPoint).sqrMagnitude;
                if (distanceSquared > radiusSquared) continue;
                Vector3 normal = normals[i];
                if (Vector3.Dot(normal, faceNormal) <= 0f) continue;
                sum += normal * falloff(Mathf.Sqrt(distanceSquared) / radius);
            }
            return sum.sqrMagnitude > 1e-12f ? sum.normalized : faceNormal;
        }
    }
}
