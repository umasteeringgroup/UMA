using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    /// <summary>Final source-local mesh constraint. Runs after sampling, width, roll and
    /// embedding; never writes back to the authored/evaluated curves. Cross-sections move
    /// together to retain width/camber. No additional vertices or triangles are generated.</summary>
    internal sealed class HairCardSurfaceClearance
    {
        private static readonly ProfilerMarker Marker = new ProfilerMarker("HairCards.CardSurfaceClearance");
        private float[] distances = Array.Empty<float>();
        private Vector3[] normalSums = Array.Empty<Vector3>();
        private readonly List<int> candidates = new List<int>();
        private readonly List<HairMeshRaycaster.SurfaceTriangle> patch = new List<HairMeshRaycaster.SurfaceTriangle>();
        private Vector3 patchCenter;
        private float patchRadius;
        private const float Tolerance = 0.00001f;
        private readonly Dictionary<string, CachedCard> cards = new Dictionary<string, CachedCard>(StringComparer.Ordinal);
        private readonly List<string> staleCards = new List<string>();
        private HairMeshRaycaster cachedSurface;
        private int cachedVersion;
        private uint build;
        private struct VertexSnapshot
        {
            internal Vector3 position, normal;
            internal Vector4 tangent;
            internal Vector2 uv;
        }
        private sealed class CachedCard
        {
            internal VertexSnapshot[] input, output;
            internal int[] indices;
            internal int rows;
            internal float clearance, embed;
            internal bool changed;
            internal uint build;
        }

        internal void Begin() { unchecked { build++; } }
        internal void End()
        {
            staleCards.Clear();
            foreach (var card in cards) if (card.Value.build != build) staleCards.Add(card.Key);
            foreach (string key in staleCards) cards.Remove(key);
            staleCards.Clear();
        }

        internal void Clear()
        {
            distances = Array.Empty<float>(); normalSums = Array.Empty<Vector3>();
            candidates.Clear(); candidates.Capacity = 0; patch.Clear(); patch.Capacity = 0;
            cards.Clear(); cards.TrimExcess(); staleCards.Clear(); staleCards.Capacity = 0; cachedSurface = null;
        }

        internal bool Apply(HairEvaluatedCurve curve, HairMeshRaycaster surface, HairMeshBuildWorkspace workspace,
            int start, List<int> indices, int firstIndex)
        {
            if (surface == null || workspace.sampled.Count < 2) return false;
            using var marker = Marker.Auto();
            var vertices = workspace.vertices;
            int count = vertices.Count - start, rows = workspace.sampled.Count, columns = count / rows;
            if (columns < 2) return false;
            if (distances.Length < count)
            {
                Array.Resize(ref distances, Mathf.NextPowerOfTwo(count));
                Array.Resize(ref normalSums, distances.Length);
            }
            float clearance = float.IsFinite(curve.cardSurfaceClearance) ? Mathf.Clamp(curve.cardSurfaceClearance, 0f, .005f) : .0005f;
            float embed = float.IsFinite(curve.rootEmbedDepth) ? Mathf.Clamp(curve.rootEmbedDepth, 0f, .02f) : 0f;
            bool tube = curve.profile != null && curve.profile.Shape == HairCardShape.TaperedTube;
            int step = !tube && (curve.profile == null || curve.profile.DoubleSided) ? 6 : 3;
            if (!ReferenceEquals(cachedSurface, surface) || cachedVersion != surface.GeometryVersion)
            {
                cards.Clear(); cachedSurface = surface; cachedVersion = surface.GeometryVersion;
            }
            CachedCard cache = null;
            if (!string.IsNullOrEmpty(curve.curveId))
            {
                if (!cards.TryGetValue(curve.curveId, out cache))
                { cache = new CachedCard(); cards.Add(curve.curveId, cache); }
                cache.build = build;
                bool matches = cache.input != null && cache.input.Length == count && cache.rows == rows &&
                    cache.clearance == clearance && cache.embed == embed && cache.indices.Length == indices.Count - firstIndex;
                if (matches)
                    for (int i = 0; i < cache.indices.Length; i++)
                        if (cache.indices[i] != indices[firstIndex + i] - start) { matches = false; break; }
                if (matches)
                    for (int i = 0; i < count; i++)
                    {
                        var v = cache.input[i]; int index = start + i;
                        if (!v.position.Equals(vertices[index]) || !v.normal.Equals(workspace.normals[index]) ||
                            !v.tangent.Equals(workspace.tangents[index]) || !v.uv.Equals(workspace.uvs[index]))
                        { matches = false; break; }
                    }
                if (matches)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var v = cache.output[i]; int index = start + i;
                        vertices[index] = v.position; workspace.normals[index] = v.normal; workspace.tangents[index] = v.tangent;
                    }
                    return cache.changed;
                }
                if (cache.input == null || cache.input.Length != count)
                { cache.input = new VertexSnapshot[count]; cache.output = new VertexSnapshot[count]; }
                if (cache.indices == null || cache.indices.Length != indices.Count - firstIndex)
                    cache.indices = new int[indices.Count - firstIndex];
                for (int i = 0; i < cache.indices.Length; i++) cache.indices[i] = indices[firstIndex + i] - start;
                cache.rows = rows; cache.clearance = clearance; cache.embed = embed;
                Capture(cache.input);
            }
            bool changed = false;
            // Usually exits after the second pass. Recheck after each pass: pushing one
            // edge out can expose an adjacent face near concave areas such as the ear.
            for (int pass = 0; pass < 8; pass++)
            {
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    if (i % columns == 0) PreparePatch(start + i, start + i + columns);
                    int vertex = start + i;
                    float minimum = Minimum(workspace.uvs[vertex].y, clearance, embed);
                    if (Correction(vertices[vertex], minimum, surface, out var push, out float distance))
                    { MoveRow(i / columns, push); moved = true; }
                    distances[i] = distance;
                }
                int patchRow = -1;
                for (int i = firstIndex; i < indices.Count; i += step)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    float diameter = Mathf.Max((vertices[a] - vertices[b]).magnitude,
                        Mathf.Max((vertices[b] - vertices[c]).magnitude, (vertices[c] - vertices[a]).magnitude));
                    // Distance to a surface is 1-Lipschitz: a face this far away cannot
                    // intersect it. Avoid interior BVH queries for the bulk of long hair.
                    if (Mathf.Min(distances[a - start], Mathf.Min(distances[b - start], distances[c - start])) > diameter + clearance) continue;
                    int row = (Mathf.Min(a, Mathf.Min(b, c)) - start) / columns;
                    if (patchRow != row)
                    {
                        PreparePatch(start + row * columns, start + (row + 2) * columns);
                        patchRow = row;
                    }
                    Check(a, b, c, new Vector3(.5f, .5f, 0f));
                    Check(a, b, c, new Vector3(0f, .5f, .5f));
                    Check(a, b, c, new Vector3(.5f, 0f, .5f));
                    Check(a, b, c, Vector3.one / 3f);
                }
                changed |= moved;
                if (!moved) break;

                void Check(int a, int b, int c, Vector3 weights)
                {
                    Vector3 point = vertices[a] * weights.x + vertices[b] * weights.y + vertices[c] * weights.z;
                    float t = workspace.uvs[a].y * weights.x + workspace.uvs[b].y * weights.y + workspace.uvs[c].y * weights.z;
                    if (!Correction(point, Minimum(t, clearance, embed), surface, out var push, out _)) return;
                    // Translate both cross-sections rather than widening individual edges.
                    int rowA = (a - start) / columns, rowB = (b - start) / columns, rowC = (c - start) / columns;
                    MoveRow(rowA, push);
                    if (rowB != rowA) MoveRow(rowB, push);
                    if (rowC != rowA && rowC != rowB) MoveRow(rowC, push);
                    moved = true;
                }
            }
            if (changed) RefreshFrames();
            if (cache != null) { Capture(cache.output); cache.changed = changed; }
            return changed;

            void Capture(VertexSnapshot[] snapshot)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = start + i;
                    snapshot[i] = new VertexSnapshot { position = vertices[index], normal = workspace.normals[index],
                        tangent = workspace.tangents[index], uv = workspace.uvs[index] };
                }
            }

            void PreparePatch(int first, int end)
            {
                var bounds = new Bounds(vertices[first], Vector3.zero);
                for (int i = first + 1; i < end; i++) bounds.Encapsulate(vertices[i]);
                patchCenter = bounds.center; patchRadius = bounds.extents.magnitude + .006f;
                surface.GetSurfacePatch(patchCenter, patchRadius, candidates, patch);
            }

            void MoveRow(int row, Vector3 push)
            {
                for (int column = 0; column < columns; column++)
                {
                    int offset = row * columns + column;
                    vertices[start + offset] += push;
                    distances[offset] = 0f; // Invalidates the face broad-phase after movement.
                }
            }

            void RefreshFrames()
            {
                Array.Clear(normalSums, 0, count);
                // Explicit backfaces share vertices; don't cancel their front-face normals.
                for (int i = firstIndex; i < indices.Count; i += step)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    normalSums[a - start] += normal; normalSums[b - start] += normal; normalSums[c - start] += normal;
                }
                for (int row = 0; row < rows; row++)
                {
                    if (tube)
                    {
                        int first = row * columns, last = first + columns - 1;
                        var seam = normalSums[first] + normalSums[last];
                        normalSums[first] = normalSums[last] = seam;
                    }
                    for (int column = 0; column < columns; column++)
                    {
                        int offset = row * columns + column, vertex = start + offset;
                        float square = normalSums[offset].sqrMagnitude;
                        Vector3 normal = square > 1e-20f ? normalSums[offset] / Mathf.Sqrt(square) : workspace.normals[vertex];
                        Vector3 along = vertices[start + Mathf.Min(rows - 1, row + 1) * columns + column] -
                            vertices[start + Mathf.Max(0, row - 1) * columns + column];
                        Vector3 tangent = Vector3.ProjectOnPlane(along, normal).normalized;
                        if (tangent.sqrMagnitude < 1e-12f) tangent = Vector3.ProjectOnPlane(workspace.tangents[vertex], normal).normalized;
                        workspace.normals[vertex] = normal;
                        workspace.tangents[vertex] = new Vector4(tangent.x, tangent.y, tangent.z, workspace.tangents[vertex].w);
                    }
                }
            }
        }

        private static float Minimum(float t, float clearance, float embed)
        {
            if (embed <= 0f) return clearance;
            float root = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .2f));
            return Mathf.Lerp(clearance, -embed, root);
        }

        private bool Correction(Vector3 point, float minimum, HairMeshRaycaster surface, out Vector3 push, out float distance)
        {
            push = Vector3.zero; distance = float.PositiveInfinity;
            if (!surface.ClosestPointInPatch(point, patchCenter, patchRadius, patch, out var hit)) return false;
            float signed = Vector3.Dot(point - hit.Point, hit.Normal);
            distance = signed < 0f ? -hit.Distance : hit.Distance;
            float depth = minimum - signed;
            if (depth <= Tolerance) return false;
            push = hit.Normal * (depth + Tolerance);
            return true;
        }
    }
}
