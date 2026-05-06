#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    public static class SlotLodGenerator
    {
        public class LodGenOptions
        {
            public int MaxLodLevels = 8;
            public int MinTriangles = 256;
            public float TargetReductionPerLevel = 0.5f;
            public bool PreserveBoundaryEdges = true;
            public float BoundaryWeight = 10f;
            public bool PreserveVolume = true;
            public float VolumeWeight = 1.0f;
            public bool useUnityLodGenerator = false; // When true, then only MaxLodLevels are used.
        }

        public static bool GenerateAndApplyUnityLods(SlotDataAsset slot, LodGenOptions options)
        {
#if UNITY_6000_2_OR_NEWER
            if (slot == null)
            {
                Debug.LogError("Slot is null.");
                return false;
            }
            if (UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.submeshes == null || slot.meshData.submeshes.Length == 0)
            {
                Debug.LogError("Slot mesh data is missing or has no submeshes.");
                return false;
            }
            if (options == null)
            {
                options = new LodGenOptions();
            }
            if (options.MaxLodLevels < 1)
            {
                Debug.LogError("MaxLodLevels must be at least 1.");
                return false;
            }

            int sub = slot.subMeshIndex;
            if (sub < 0 || sub >= slot.meshData.submeshes.Length)
            {
                sub = 0;
            }

            var smt = slot.meshData.submeshes[sub];
            if (smt == null)
            {
                Debug.LogError("SubMeshTriangles is null.");
                return false;
            }

            Mesh tempMesh = null;
            try
            {
                // Build a temp Unity mesh from slot meshData (vertices + base triangles)
                tempMesh = new Mesh();
                tempMesh.indexFormat = (slot.meshData.vertexCount > 65535) ? IndexFormat.UInt32 : IndexFormat.UInt16;
                tempMesh.vertices = slot.meshData.vertices;
                if (slot.meshData.normals != null && slot.meshData.normals.Length == slot.meshData.vertexCount)
                {
                    tempMesh.normals = slot.meshData.normals;
                }
                if (slot.meshData.tangents != null && slot.meshData.tangents.Length == slot.meshData.vertexCount)
                {
                    tempMesh.tangents = slot.meshData.tangents;
                }
                if (slot.meshData.uv != null && slot.meshData.uv.Length == slot.meshData.vertexCount)
                {
                    tempMesh.uv = slot.meshData.uv;
                }

                tempMesh.subMeshCount = 1;
                int[] baseTris = smt.GetBaseTriangles();
                if (baseTris == null || baseTris.Length < 3)
                {
                    return false;
                }
                tempMesh.SetTriangles(baseTris, 0, true);
                tempMesh.RecalculateBounds();

                // Generate mesh LODs using Unity
                MeshLodUtility.GenerateMeshLods(tempMesh, options.MaxLodLevels);

                int lodCount = tempMesh.lodCount;
                if (lodCount <= 1)
                {
                    Debug.LogWarning("Unity LOD generator did not produce multiple LOD levels.");
                    return false;
                }

                // Collect triangles by slicing the base triangle buffer using LOD ranges.
                // Unity stores LOD ranges as (indexStart,indexCount) segments over the base index buffer.
                int[] allTris = tempMesh.GetTriangles(0, -1, false);
                if (allTris == null || allTris.Length < 3)
                {
                    Debug.LogError("Failed to get triangles from temp mesh after LOD generation.");
                    return false;
                }

                var appended = new List<int>(allTris.Length * lodCount);
                var ranges = new List<UMALodRange>(lodCount);

                Debug.Log($"Slot has {slot.meshData.submeshes[0].GetBaseTriangles().Length} triangles");
                Debug.Log($"Generated {lodCount} LODs using Unity LOD generator:");
                Debug.Log($" Total triangles after LOD generation: {allTris.Length}");

                for (int l = 0; l < lodCount; l++)
                {
                    var lor = tempMesh.GetLod(0, l);
                    Debug.Log($" LOD {l}: start={lor.indexStart} count={lor.indexCount}");
                }

                for (int l = 0; l < lodCount; l++)
                {
                    var lor = tempMesh.GetLod(0, l);
                    int start = (int)lor.indexStart;
                    int count = (int)lor.indexCount;
                    if (start < 0 || count <= 0)
                    {
                        break;
                    }
                    if ((start + count) > allTris.Length)
                    {
                        break;
                    }
                    if ((count % 3) != 0)
                    {
                        break;
                    }

                    uint offset = (uint)appended.Count;
                    appended.AddRange(new ArraySegment<int>(allTris, start, count));
                    ranges.Add(new UMALodRange(offset, (uint)count));
                }

                if (ranges.Count <= 1)
                {
                    return false;
                }

                  Undo.RecordObject(slot, "Generate Slot LODs (Unity)");
                smt.SetTriangles(appended.ToArray());
                smt.SetLodRanges(ranges);
                EditorUtility.SetDirty(slot);
                 Debug.Log($"Applied {ranges.Count} LODs to slot. smt.LODCount={smt.LODCount()} triLen={(appended != null ? appended.Count : 0)}");

                AssetDatabase.SaveAssetIfDirty(slot);
#if UNITY_6000_3_OR_NEWER
                string path = AssetDatabase.GetAssetPath(slot.GetEntityId());
#else
                string path = AssetDatabase.GetAssetPath(slot.GetEntityId());
#endif
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(path);
                }
                 try
                    {
                        int verify = (!UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) && slot.meshData.submeshes != null && slot.meshData.submeshes.Length > sub && slot.meshData.submeshes[sub] != null)
                            ? slot.meshData.submeshes[sub].LODCount()
                            : -1;
                        Debug.Log($"[SlotLOD][Unity] VERIFY slot='{slot.slotName}' lodCount={verify}");
                    }
                    catch { }
                    UMAUpdateProcessor.UpdateSlot(slot, false);

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tempMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(tempMesh);
                }
            }
#else
            return false;
#endif
            }

        public static bool GenerateAndApplyLods(SlotDataAsset slot, LodGenOptions options = null)
        {
            if (slot == null)
            {
                return false;
            }
            if (UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.submeshes == null || slot.meshData.submeshes.Length == 0)
            {
                return false;
            }

            if (options == null)
            {
                options = new LodGenOptions();
            }

            if (options.MaxLodLevels < 1)
            {
                return false;
            }

            if (options.useUnityLodGenerator)
            {
                Debug.Log("Generating LODs using Unity's built-in LOD generator.");
                return GenerateAndApplyUnityLods(slot, options);
            }
            else
            {
                Debug.Log("Generating LODs using custom edge-collapse simplification.");
                return GenerateAndApplyCustomLods(slot, options);
            }
        }

        public static bool GenerateAndApplyCustomLods(SlotDataAsset slot, LodGenOptions options)
        {
            int sub = slot.subMeshIndex;
            if (sub < 0 || sub >= slot.meshData.submeshes.Length)
            {
                sub = 0;
            }

            var smt = slot.meshData.submeshes[sub];
            if (smt == null)
            {
                return false;
            }

            int[] baseTris = smt.GetBaseTriangles();
            if (baseTris == null || baseTris.Length < 3)
            {
                return false;
            }

            int baseTriCount = baseTris.Length / 3;
            if (baseTriCount <= options.MinTriangles)
            {
                return false;
            }

            Vector3[] positions = slot.meshData.vertices;
            if (positions == null || positions.Length == 0)
            {
                return false;
            }

            // Build boundary edge set from base triangles (edges referenced by only a single triangle)
            HashSet<ulong> boundaryEdges = null;
            if (options.PreserveBoundaryEdges)
            {
                boundaryEdges = BuildBoundaryEdgeSet(baseTris);
            }

            // Ensure LOD0 range exists and is correct
            var lodRanges = new List<UMALodRange>();
            lodRanges.Add(new UMALodRange(0u, (uint)baseTris.Length));

            var appended = new List<int>(baseTris.Length * 2);
            appended.AddRange(baseTris);

            int[] current = baseTris;
            int currentCount = baseTriCount;

            int levelsMade = 0;
            for (int level = 1; level < options.MaxLodLevels; level++)
            {
                int target = Mathf.Max(options.MinTriangles, Mathf.RoundToInt(currentCount * options.TargetReductionPerLevel));
                if (target >= currentCount)
                {
                    break;
                }
                if (currentCount <= options.MinTriangles)
                {
                    break;
                }

                var reduced = SimplifyMesh(current, positions, target, boundaryEdges, options);
                if (reduced == null || reduced.Length < 3)
                {
                    break;
                }

                int reducedCount = reduced.Length / 3;
                if (reducedCount >= currentCount)
                {
                    break;
                }

                uint offset = (uint)appended.Count;
                appended.AddRange(reduced);
                lodRanges.Add(new UMALodRange(offset, (uint)reduced.Length));

                current = reduced;
                currentCount = reducedCount;
                levelsMade++;

                // Rebuild boundary edges for the reduced mesh so next level respects new boundaries
                if (options.PreserveBoundaryEdges)
                {
                    boundaryEdges = BuildBoundaryEdgeSet(reduced);
                }

                if (currentCount <= options.MinTriangles)
                {
                    break;
                }
            }

            if (levelsMade == 0)
            {
                return false;
            }

            Undo.RecordObject(slot, "Generate Slot LODs");
            smt.SetTriangles(appended.ToArray());
            smt.SetLodRanges(lodRanges);
            EditorUtility.SetDirty(slot);
            return true;
        }

        /// <summary>
        /// Simplify a mesh by edge collapse using quadric error metrics.
        /// Only modifies triangle indices; vertices are never changed.
        /// Attempts to preserve X-axis symmetry and distribute collapses evenly.
        /// </summary>
        private static int[] SimplifyMesh(int[] triangles, Vector3[] positions, int targetTriCount, HashSet<ulong> boundaryEdges, LodGenOptions options)
        {
            int triCount = triangles.Length / 3;
            if (triCount <= targetTriCount)
            {
                return triangles;
            }
            if (targetTriCount <= 0)
            {
                return Array.Empty<int>();
            }

            int vCount = positions.Length;

            // Build triangle structures
            var tris = new List<SimplifyTriangle>(triCount);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (a < 0 || a >= vCount || b < 0 || b >= vCount || c < 0 || c >= vCount)
                {
                    continue;
                }
                tris.Add(new SimplifyTriangle { v0 = a, v1 = b, v2 = c, deleted = false, collapseCount = 0 });
            }

            // Build per-vertex data
            var vertexData = new SimplifyVertex[vCount];
            for (int i = 0; i < vCount; i++)
            {
                vertexData[i] = new SimplifyVertex
                {
                    position = positions[i],
                    triangleStart = 0,
                    triangleCount = 0,
                    isBoundary = false,
                    collapsed = false,
                    collapseTo = -1,
                    mirrorVertex = -1,
                    collapseCount = 0
                };
            }

            // Build X-symmetry map: find vertices that mirror each other across X=0
            // Use a spatial hash for efficiency
            const float symmetryTolerance = 0.0001f;
            var symmetryMap = new Dictionary<int, int>(vCount);
            var positionHash = new Dictionary<long, List<int>>(vCount);

            // Hash vertices by their mirrored position (for lookup)
            for (int i = 0; i < vCount; i++)
            {
                Vector3 p = positions[i];
                // Create hash key from mirrored position (-x, y, z)
                long key = HashPosition(-p.x, p.y, p.z, symmetryTolerance);
                if (!positionHash.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    positionHash[key] = list;
                }
                list.Add(i);
            }

            // Find mirror pairs
            for (int i = 0; i < vCount; i++)
            {
                Vector3 p = positions[i];
                // Skip vertices on the center line (X � 0)
                if (Mathf.Abs(p.x) < symmetryTolerance)
                {
                    continue;
                }

                // Look for a vertex at the original position in the hash (which was stored with mirrored coords)
                long key = HashPosition(p.x, p.y, p.z, symmetryTolerance);
                if (positionHash.TryGetValue(key, out var candidates))
                {
                    foreach (int j in candidates)
                    {
                        if (i == j)
                        {
                            continue;
                        }
                        Vector3 pj = positions[j];
                        // Check if j is the mirror of i
                        if (Mathf.Abs(pj.x + p.x) < symmetryTolerance &&
                            Mathf.Abs(pj.y - p.y) < symmetryTolerance &&
                            Mathf.Abs(pj.z - p.z) < symmetryTolerance)
                        {
                            vertexData[i].mirrorVertex = j;
                            break;
                        }
                    }
                }
            }

            // Build vertex-to-triangle references
            var vertexTriangles = new List<int>[vCount];
            for (int i = 0; i < vCount; i++)
            {
                vertexTriangles[i] = new List<int>(8);
            }
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                vertexTriangles[t.v0].Add(ti);
                vertexTriangles[t.v1].Add(ti);
                vertexTriangles[t.v2].Add(ti);
            }

            // Mark boundary vertices
            if (options.PreserveBoundaryEdges && boundaryEdges != null)
            {
                foreach (var edgeKey in boundaryEdges)
                {
                    int a = (int)(edgeKey >> 32);
                    int b = (int)(edgeKey & 0xFFFFFFFF);
                    if (a >= 0 && a < vCount)
                    {
                        vertexData[a].isBoundary = true;
                    }
                    if (b >= 0 && b < vCount)
                    {
                        vertexData[b].isBoundary = true;
                    }
                }
            }

            // Compute per-vertex quadrics from incident triangles
            var quadrics = new SimplifyQuadric[vCount];
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                Vector3 p0 = positions[t.v0];
                Vector3 p1 = positions[t.v1];
                Vector3 p2 = positions[t.v2];

                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                float area = n.magnitude * 0.5f;
                if (area < 1e-10f)
                {
                    continue;
                }
                n.Normalize();
                float d = -Vector3.Dot(n, p0);

                var q = SimplifyQuadric.FromPlane(n.x, n.y, n.z, d, area);
                quadrics[t.v0].Add(q);
                quadrics[t.v1].Add(q);
                quadrics[t.v2].Add(q);
            }

            // Build edge list with collapse costs
            var edges = new Dictionary<ulong, SimplifyEdge>(triCount * 3);
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                AddOrUpdateEdge(edges, t.v0, t.v1, positions, quadrics, vertexData, boundaryEdges, options);
                AddOrUpdateEdge(edges, t.v1, t.v2, positions, quadrics, vertexData, boundaryEdges, options);
                AddOrUpdateEdge(edges, t.v2, t.v0, positions, quadrics, vertexData, boundaryEdges, options);
            }

            // Build priority queue (min-heap by cost)
            var edgeList = new List<SimplifyEdge>(edges.Values);
            edgeList.Sort((a, b) => a.cost.CompareTo(b.cost));

            int aliveTriangles = tris.Count;
            int collapsesMade = 0;
            int maxCollapses = triCount * 2;

            // Main collapse loop
            while (aliveTriangles > targetTriCount && collapsesMade < maxCollapses)
            {
                // Find best valid edge to collapse
                SimplifyEdge bestEdge = default;
                bool foundEdge = false;

                for (int i = 0; i < edgeList.Count; i++)
                {
                    var e = edgeList[i];
                    if (e.cost < 0)
                    {
                        continue;
                    }

                    int v0 = GetCollapsedVertex(vertexData, e.v0);
                    int v1 = GetCollapsedVertex(vertexData, e.v1);

                    if (v0 == v1)
                    {
                        continue;
                    }
                    if (vertexData[v0].collapsed || vertexData[v1].collapsed)
                    {
                        continue;
                    }

                    // Check if collapse is valid
                    if (!IsCollapseValid(v0, v1, tris, vertexTriangles, vertexData, positions, boundaryEdges, options))
                    {
                        continue;
                    }

                    bestEdge = e;
                    bestEdge.v0 = v0;
                    bestEdge.v1 = v1;
                    foundEdge = true;
                    edgeList.RemoveAt(i);
                    break;
                }

                if (!foundEdge)
                {
                    break;
                }

                // Collapse v1 into v0 (v0 survives, v1 is removed)
                int keepV = bestEdge.v0;
                int removeV = bestEdge.v1;

                // Choose the vertex with lower boundary priority to remove
                if (options.PreserveBoundaryEdges)
                {
                    if (vertexData[removeV].isBoundary && !vertexData[keepV].isBoundary)
                    {
                        int tmp = keepV;
                        keepV = removeV;
                        removeV = tmp;
                    }
                }

                // Mark vertex as collapsed
                vertexData[removeV].collapsed = true;
                vertexData[removeV].collapseTo = keepV;

                // Merge quadrics
                quadrics[keepV].Add(quadrics[removeV]);

                // Update boundary status
                if (vertexData[removeV].isBoundary)
                {
                    vertexData[keepV].isBoundary = true;
                }

                // Increment collapse count for the surviving vertex
                vertexData[keepV].collapseCount++;

                // Update triangles - replace removeV with keepV
                var affectedTris = vertexTriangles[removeV];
                for (int i = 0; i < affectedTris.Count; i++)
                {
                    int ti = affectedTris[i];
                    if (ti < 0 || ti >= tris.Count)
                    {
                        continue;
                    }
                    var t = tris[ti];
                    if (t.deleted)
                    {
                        continue;
                    }

                    // Increment collapse count for this triangle
                    t.collapseCount++;

                    // Replace removeV with keepV
                    if (t.v0 == removeV) t.v0 = keepV;
                    if (t.v1 == removeV) t.v1 = keepV;
                    if (t.v2 == removeV) t.v2 = keepV;

                    // Check if triangle became degenerate
                    if (t.v0 == t.v1 || t.v1 == t.v2 || t.v2 == t.v0)
                    {
                        t.deleted = true;
                        aliveTriangles--;
                    }
                    else
                    {
                        // Check if triangle has valid area
                        Vector3 p0 = positions[t.v0];
                        Vector3 p1 = positions[t.v1];
                        Vector3 p2 = positions[t.v2];
                        float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude;
                        if (area < 1e-10f)
                        {
                            t.deleted = true;
                            aliveTriangles--;
                        }
                        else
                        {
                            // Add to keepV's triangle list if not already there
                            if (!vertexTriangles[keepV].Contains(ti))
                            {
                                vertexTriangles[keepV].Add(ti);
                            }
                        }
                    }

                    tris[ti] = t;
                }

                collapsesMade++;

                // Re-add edges around keepV with updated costs
                var keepTris = vertexTriangles[keepV];
                for (int i = 0; i < keepTris.Count; i++)
                {
                    int ti = keepTris[i];
                    if (ti < 0 || ti >= tris.Count)
                    {
                        continue;
                    }
                    var t = tris[ti];
                    if (t.deleted)
                    {
                        continue;
                    }

                    if (t.v0 != keepV)
                    {
                        var newEdge = CreateEdge(keepV, t.v0, positions, quadrics, vertexData, boundaryEdges, options);
                        if (newEdge.cost >= 0)
                        {
                            InsertEdgeSorted(edgeList, newEdge);
                        }
                    }
                    if (t.v1 != keepV)
                    {
                        var newEdge = CreateEdge(keepV, t.v1, positions, quadrics, vertexData, boundaryEdges, options);
                        if (newEdge.cost >= 0)
                        {
                            InsertEdgeSorted(edgeList, newEdge);
                        }
                    }
                    if (t.v2 != keepV)
                    {
                        var newEdge = CreateEdge(keepV, t.v2, positions, quadrics, vertexData, boundaryEdges, options);
                        if (newEdge.cost >= 0)
                        {
                            InsertEdgeSorted(edgeList, newEdge);
                        }
                    }
                }
            }

            // Build output triangle list
            var result = new List<int>(aliveTriangles * 3);
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                if (t.deleted)
                {
                    continue;
                }

                // Resolve any collapsed vertices
                int v0 = GetCollapsedVertex(vertexData, t.v0);
                int v1 = GetCollapsedVertex(vertexData, t.v1);
                int v2 = GetCollapsedVertex(vertexData, t.v2);

                if (v0 == v1 || v1 == v2 || v2 == v0)
                {
                    continue;
                }

                result.Add(v0);
                result.Add(v1);
                result.Add(v2);
            }

            return result.Count >= 3 ? result.ToArray() : null;
        }

        private static int GetCollapsedVertex(SimplifyVertex[] vertexData, int v)
        {
            int maxIter = 100;
            int iter = 0;
            while (vertexData[v].collapsed && vertexData[v].collapseTo >= 0 && iter < maxIter)
            {
                v = vertexData[v].collapseTo;
                iter++;
            }
            return v;
        }

        private static void AddOrUpdateEdge(Dictionary<ulong, SimplifyEdge> edges, int v0, int v1, Vector3[] positions, SimplifyQuadric[] quadrics, SimplifyVertex[] vertexData, HashSet<ulong> boundaryEdges, LodGenOptions options)
        {
            if (v0 == v1)
            {
                return;
            }
            ulong key = MakeUndirectedEdgeKey(v0, v1);
            if (!edges.ContainsKey(key))
            {
                var e = CreateEdge(v0, v1, positions, quadrics, vertexData, boundaryEdges, options);
                edges[key] = e;
            }
        }

        private static SimplifyEdge CreateEdge(int v0, int v1, Vector3[] positions, SimplifyQuadric[] quadrics, SimplifyVertex[] vertexData, HashSet<ulong> boundaryEdges, LodGenOptions options)
        {
            if (v0 > v1)
            {
                int tmp = v0;
                v0 = v1;
                v1 = tmp;
            }

            float cost = float.MaxValue;

            // Skip if either vertex is already collapsed
            if (vertexData[v0].collapsed || vertexData[v1].collapsed)
            {
                return new SimplifyEdge { v0 = v0, v1 = v1, cost = -1 };
            }

            // Skip boundary edges if preserving boundaries
            if (options.PreserveBoundaryEdges && boundaryEdges != null)
            {
                ulong key = MakeUndirectedEdgeKey(v0, v1);
                if (boundaryEdges.Contains(key))
                {
                    return new SimplifyEdge { v0 = v0, v1 = v1, cost = -1 };
                }
            }

            // Compute collapse cost using quadric error
            var q = quadrics[v0];
            q.Add(quadrics[v1]);

            // We collapse to one of the existing vertices (no new vertex positions)
            float cost0 = q.Evaluate(positions[v0]);
            float cost1 = q.Evaluate(positions[v1]);
            float baseCost = Mathf.Min(cost0, cost1);

            // Normalize the base cost to make penalties relative
            // Add a small epsilon to avoid division by zero
            float costScale = Mathf.Max(0.0001f, baseCost);
            cost = baseCost;

            // Add edge length as a small tie-breaker (relative to edge length scale)
            float edgeLen = (positions[v0] - positions[v1]).magnitude;
            cost += edgeLen * 0.001f;

            // Penalize boundary vertices
            if (options.PreserveBoundaryEdges)
            {
                if (vertexData[v0].isBoundary || vertexData[v1].isBoundary)
                {
                    cost += options.BoundaryWeight * costScale;
                }
            }

            // Small penalty for vertices that have already been affected by collapses
            // This helps distribute collapses more evenly across the mesh
            int totalCollapseCount = vertexData[v0].collapseCount + vertexData[v1].collapseCount;
            if (totalCollapseCount > 0)
            {
                // Very small relative penalty - just enough to break ties
                cost += costScale * 0.01f * totalCollapseCount;
            }

            // Penalize asymmetric collapses (when one vertex has a mirror but would collapse asymmetrically)
            int mirror0 = vertexData[v0].mirrorVertex;
            int mirror1 = vertexData[v1].mirrorVertex;
            if (mirror0 >= 0 || mirror1 >= 0)
            {
                // Check if this edge has a symmetric counterpart
                bool hasSymmetricEdge = false;
                if (mirror0 >= 0 && mirror1 >= 0)
                {
                    // Both vertices have mirrors - check if the mirror edge exists and is valid
                    if (!vertexData[mirror0].collapsed && !vertexData[mirror1].collapsed)
                    {
                        hasSymmetricEdge = true;
                    }
                }

                if (!hasSymmetricEdge)
                {
                                // This collapse would break symmetry - add small relative penalty
                                cost += costScale * 0.1f;
                            }
                        }

                        // Volume preservation: penalize collapses that would flatten thin features like arms
                        // We detect this by checking if the edge crosses a high-curvature region
                        if (options.PreserveVolume && options.VolumeWeight > 0)
                        {
                            // Use quadric eigenvalues as a proxy for local curvature
                            // High curvature relative to edge length indicates thin features
                            float edgeLenSq = (positions[v1] - positions[v0]).sqrMagnitude;
                            if (edgeLenSq > 1e-10f)
                            {
                                float avgEdgeLen = Mathf.Sqrt(edgeLenSq);

                                // The quadric error gives us a measure of how much the surface curves
                                // Dividing by edge length gives us curvature density
                                float curvatureEstimate = Mathf.Sqrt(baseCost) / avgEdgeLen;

                                // If curvature is high relative to edge length, this is likely a thin feature
                                // Penalize collapsing these edges to preserve volume
                                if (curvatureEstimate > 0.5f)
                                {
                                    float volumePenalty = options.VolumeWeight * costScale * Mathf.Min(curvatureEstimate, 5.0f);
                                    cost += volumePenalty;
                                }
                            }
                        }

                        return new SimplifyEdge { v0 = v0, v1 = v1, cost = cost };
                    }

        private static void InsertEdgeSorted(List<SimplifyEdge> edgeList, SimplifyEdge edge)
        {
            // Simple insertion for sorted list
            int insertIdx = 0;
            for (int i = 0; i < edgeList.Count; i++)
            {
                if (edge.cost < edgeList[i].cost)
                {
                    insertIdx = i;
                    break;
                }
                insertIdx = i + 1;
            }
            edgeList.Insert(insertIdx, edge);
        }

        private static bool IsCollapseValid(int v0, int v1, List<SimplifyTriangle> tris, List<int>[] vertexTriangles, SimplifyVertex[] vertexData, Vector3[] positions, HashSet<ulong> boundaryEdges, LodGenOptions options)
        {
            // Don't collapse if both are boundary vertices and they share a boundary edge
            if (options.PreserveBoundaryEdges && boundaryEdges != null)
            {
                ulong key = MakeUndirectedEdgeKey(v0, v1);
                if (boundaryEdges.Contains(key))
                {
                    return false;
                }

                // Don't collapse a boundary vertex into a non-boundary vertex
                if (vertexData[v0].isBoundary != vertexData[v1].isBoundary)
                {
                    return false;
                }
            }

            // Check that collapsing doesn't flip any triangle normals
            var affectedTris = vertexTriangles[v1];
            for (int i = 0; i < affectedTris.Count; i++)
            {
                int ti = affectedTris[i];
                if (ti < 0 || ti >= tris.Count)
                {
                    continue;
                }
                var t = tris[ti];
                if (t.deleted)
                {
                    continue;
                }

                // Skip triangles that would become degenerate
                int a = t.v0 == v1 ? v0 : t.v0;
                int b = t.v1 == v1 ? v0 : t.v1;
                int c = t.v2 == v1 ? v0 : t.v2;

                if (a == b || b == c || c == a)
                {
                    continue;
                }

                Vector3 p0 = positions[a];
                Vector3 p1 = positions[b];
                Vector3 p2 = positions[c];

                Vector3 newNormal = Vector3.Cross(p1 - p0, p2 - p0);
                if (newNormal.sqrMagnitude < 1e-10f)
                {
                    return false;
                }

                // Check for normal flip
                Vector3 oldP0 = positions[t.v0];
                Vector3 oldP1 = positions[t.v1];
                Vector3 oldP2 = positions[t.v2];
                Vector3 oldNormal = Vector3.Cross(oldP1 - oldP0, oldP2 - oldP0);

                if (Vector3.Dot(newNormal, oldNormal) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private struct SimplifyTriangle
        {
            public int v0, v1, v2;
            public bool deleted;
            public int collapseCount; // Track how many times this triangle's vertices have been affected
        }

        private struct SimplifyVertex
        {
            public Vector3 position;
            public int triangleStart;
            public int triangleCount;
            public bool isBoundary;
            public bool collapsed;
            public int collapseTo;
            public int mirrorVertex; // Index of the X-symmetric vertex, or -1 if none
            public int collapseCount; // How many collapses have affected this vertex's neighborhood
        }

        private struct SimplifyEdge
        {
            public int v0, v1;
            public float cost;
        }

        private struct SimplifyQuadric
        {
            public float a00, a01, a02, a03;
            public float a11, a12, a13;
            public float a22, a23;
            public float a33;

            public static SimplifyQuadric FromPlane(float a, float b, float c, float d, float weight)
            {
                float w = weight;
                return new SimplifyQuadric
                {
                    a00 = w * a * a,
                    a01 = w * a * b,
                    a02 = w * a * c,
                    a03 = w * a * d,
                    a11 = w * b * b,
                    a12 = w * b * c,
                    a13 = w * b * d,
                    a22 = w * c * c,
                    a23 = w * c * d,
                    a33 = w * d * d
                };
            }

            public void Add(SimplifyQuadric other)
            {
                a00 += other.a00;
                a01 += other.a01;
                a02 += other.a02;
                a03 += other.a03;
                a11 += other.a11;
                a12 += other.a12;
                a13 += other.a13;
                a22 += other.a22;
                a23 += other.a23;
                a33 += other.a33;
            }

            public float Evaluate(Vector3 p)
            {
                float x = p.x, y = p.y, z = p.z, w = 1f;
                return a00 * x * x + 2 * a01 * x * y + 2 * a02 * x * z + 2 * a03 * x * w
                     + a11 * y * y + 2 * a12 * y * z + 2 * a13 * y * w
                     + a22 * z * z + 2 * a23 * z * w
                     + a33 * w * w;
            }
        }

        /// <summary>
        /// Create a spatial hash key for a position, used for finding symmetric vertices.
        /// </summary>
        private static long HashPosition(float x, float y, float z, float cellSize)
        {
            int ix = Mathf.RoundToInt(x / cellSize);
            int iy = Mathf.RoundToInt(y / cellSize);
            int iz = Mathf.RoundToInt(z / cellSize);
            // Pack into a long (21 bits per component, supports ~2 million cells per axis)
            long hash = ((long)(ix + 1048576) & 0x1FFFFF);
            hash |= ((long)(iy + 1048576) & 0x1FFFFF) << 21;
            hash |= ((long)(iz + 1048576) & 0x1FFFFF) << 42;
            return hash;
        }

        public static bool ValidateInternalLods(SlotDataAsset slot)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.submeshes == null || slot.meshData.submeshes.Length == 0)
            {
                return false;
            }
            int sub = slot.subMeshIndex;
            if (sub < 0 || sub >= slot.meshData.submeshes.Length)
            {
                sub = 0;
            }
            var smt = slot.meshData.submeshes[sub];
            if (smt == null)
            {
                return false;
            }

            var baseTris = smt.GetBaseTriangles();
            if (baseTris == null)
            {
                return false;
            }

            var lodRanges = smt.lodRanges;
            if (lodRanges == null || lodRanges.Count == 0)
            {
                return true;
            }

            int lastCount = int.MaxValue;
            for (int i = 0; i < lodRanges.Count; i++)
            {
                var r = lodRanges[i];
                if ((int)r.offset < 0 || (int)r.count < 0)
                {
                    return false;
                }
                if ((long)r.offset + (long)r.count > baseTris.Length)
                {
                    return false;
                }
                if (r.count % 3 != 0)
                {
                    return false;
                }
                int triCount = (int)r.count / 3;
                if (triCount > lastCount)
                {
                    // Should be non-increasing
                    return false;
                }
                lastCount = triCount;
            }

            return true;
        }

        private static HashSet<ulong> BuildBoundaryEdgeSet(int[] triangles)
        {
            var edgeCounts = new Dictionary<ulong, int>(triangles.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];

                AddEdgeCount(edgeCounts, a, b);
                AddEdgeCount(edgeCounts, b, c);
                AddEdgeCount(edgeCounts, c, a);
            }

            var res = new HashSet<ulong>();
            foreach (var kv in edgeCounts)
            {
                if (kv.Value == 1)
                {
                    res.Add(kv.Key);
                }
            }
            return res;
        }

        private static void AddEdgeCount(Dictionary<ulong, int> edgeCounts, int v0, int v1)
        {
            ulong key = MakeUndirectedEdgeKey(v0, v1);
            int count;
            if (edgeCounts.TryGetValue(key, out count))
            {
                edgeCounts[key] = count + 1;
            }
            else
            {
                edgeCounts.Add(key, 1);
            }
        }

        private static ulong MakeUndirectedEdgeKey(int v0, int v1)
        {
            uint a = (uint)Mathf.Min(v0, v1);
            uint b = (uint)Mathf.Max(v0, v1);
            return ((ulong)a << 32) | (ulong)b;
        }
    }
}

#endif