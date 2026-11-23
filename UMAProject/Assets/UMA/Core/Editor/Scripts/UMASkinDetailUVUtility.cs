using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Gives a slot detail texture coordinates that cover the same amount of surface everywhere, so a tiled
    /// detail map (skin pores) keeps one size across slots whose textures are laid out at different scales.
    /// The coordinates go in the mesh's fourth UV channel (uv4, TEXCOORD3), which the UMA URP Realistic Skin
    /// shaders read when it is present. The first three channels stay free for the texture atlas and lightmaps.
    /// </summary>
    public static class UMASkinDetailUVUtility
    {
        /// <summary>
        /// Mesh units of surface covered by one detail UV unit. The UMA 3 face texture covers 0.33 mesh units
        /// per UV unit, so with this value a face keeps the pore size it had from its first UV channel.
        /// </summary>
        public const float DefaultSurfacePerUV = 0.33f;

        /// <summary>
        /// Returns detail coordinates for a mesh. Every UV island of <paramref name="uv"/> is scaled as a whole,
        /// so no new seams appear and the layout inside an island keeps its shape; only its size changes, to
        /// <paramref name="surfacePerUV"/> mesh units of surface per UV unit. Every coordinate is 1 or more: the
        /// shaders treat anything below 1, such as the (0,0) of a mesh without the channel or an ordinary
        /// 0..1 layout left in it, as "this mesh has no detail coordinates".
        /// </summary>
        /// <param name="triangleLists">Index buffers that use the vertices, for example one per submesh and LOD.</param>
        public static Vector2[] ComputeDetailUVs(Vector3[] vertices, Vector2[] uv, IReadOnlyList<int[]> triangleLists,
            float surfacePerUV, out int islandCount)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (uv == null || uv.Length != vertices.Length)
                throw new ArgumentException("The mesh needs one UV coordinate per vertex.", nameof(uv));
            if (!(surfacePerUV > 0f)) throw new ArgumentOutOfRangeException(nameof(surfacePerUV));

            int count = vertices.Length;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;

            // Vertices split for normals or tangents share a position and a UV: they belong to one island.
            var welded = new Dictionary<(int, int, int, int, int), int>(count);
            for (int i = 0; i < count; i++)
            {
                var key = (Quantize(vertices[i].x), Quantize(vertices[i].y), Quantize(vertices[i].z), Quantize(uv[i].x), Quantize(uv[i].y));
                if (welded.TryGetValue(key, out int other)) Union(parent, i, other);
                else welded.Add(key, i);
            }

            if (triangleLists != null)
            {
                for (int l = 0; l < triangleLists.Count; l++)
                {
                    int[] triangles = triangleLists[l];
                    if (triangles == null) continue;
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        if (!ValidTriangle(triangles, t, count)) continue;
                        Union(parent, triangles[t], triangles[t + 1]);
                        Union(parent, triangles[t], triangles[t + 2]);
                    }
                }
            }

            var surface = new double[count];
            var uvArea = new double[count];
            double meshSurface = 0, meshUVArea = 0;
            if (triangleLists != null)
            {
                for (int l = 0; l < triangleLists.Count; l++)
                {
                    int[] triangles = triangleLists[l];
                    if (triangles == null) continue;
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        if (!ValidTriangle(triangles, t, count)) continue;
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        double s = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5;
                        Vector2 e1 = uv[b] - uv[a], e2 = uv[c] - uv[a];
                        double u = Math.Abs((double)e1.x * e2.y - (double)e1.y * e2.x) * 0.5;
                        int root = Find(parent, a);
                        surface[root] += s; uvArea[root] += u;
                        meshSurface += s; meshUVArea += u;
                    }
                }
            }

            var origin = new Vector2[count];
            for (int i = 0; i < count; i++) origin[i] = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            for (int i = 0; i < count; i++)
            {
                int root = Find(parent, i);
                origin[root] = Vector2.Min(origin[root], uv[i]);
            }

            // An island without measurable area (loose vertices, collapsed triangles) uses the mesh's own scale.
            float meshScale = Scale(meshSurface, meshUVArea, surfacePerUV, 1f);
            islandCount = 0;
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int root = Find(parent, i);
                if (root == i && uvArea[root] > 0) islandCount++;
                float scale = Scale(surface[root], uvArea[root], surfacePerUV, meshScale);
                // Every island starts at (1,1): no coordinate is below 1, which is how the shaders know them.
                result[i] = (uv[i] - origin[root]) * scale + Vector2.one;
            }
            return result;
        }

        /// <summary>Relaxation passes used by <see cref="ComputeRelaxedDetailUVs"/> unless told otherwise.</summary>
        public const int DefaultRelaxIterations = 200;

        /// <summary>
        /// Returns detail coordinates in which the detail map has one size and little stretch everywhere, also
        /// inside a UV island. <see cref="ComputeDetailUVs"/> scales each island as a whole, which is enough when
        /// the texture layout is even; a layout that gives the face more texture than the neck, or squeezes the
        /// sides of the nose, keeps that unevenness. Here every island is also unfolded: its triangles are moved
        /// until each is as close as possible to its shape and size on the mesh (as-rigid-as-possible), with
        /// densely modeled areas such as the nose, lips and eyes counting for more than their surface. The
        /// island keeps its seams and its vertices, so no new seam appears, but the result no longer follows
        /// the texture layout and islands may overlap, which a tiled detail map does not show. A curved surface
        /// cannot be laid flat exactly: what remains is spread out, and is largest on small bulges such as a nose.
        /// The first UV channel is only read. Every coordinate is 1 or more.
        /// </summary>
        /// <param name="triangleLists">Index buffers that use the vertices, for example one per submesh and LOD.</param>
        /// <param name="iterations">Relaxation passes; the layout stops changing visibly well before 200.</param>
        public static Vector2[] ComputeRelaxedDetailUVs(Vector3[] vertices, Vector2[] uv, IReadOnlyList<int[]> triangleLists,
            float surfacePerUV, out int islandCount, int iterations = DefaultRelaxIterations)
        {
            // Starting point, and the answer for whatever cannot be relaxed: every island at the right overall size.
            Vector2[] start = ComputeDetailUVs(vertices, uv, triangleLists, surfacePerUV, out islandCount);
            int count = vertices.Length;
            if (iterations <= 0 || triangleLists == null || count == 0) return start;

            // Vertices split for normals or tangents share a position and a UV: they are one point of the layout.
            var point = new int[count];
            var welded = new Dictionary<(int, int, int, int, int), int>(count);
            for (int i = 0; i < count; i++)
            {
                var key = (Quantize(vertices[i].x), Quantize(vertices[i].y), Quantize(vertices[i].z), Quantize(uv[i].x), Quantize(uv[i].y));
                if (welded.TryGetValue(key, out int other)) point[i] = other;
                else { welded.Add(key, i); point[i] = i; }
            }

            // Islands, to give every island the orientation its texture layout has (mirrored layouts stay mirrored).
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = point[i];
            var corners = new List<int>();
            for (int l = 0; l < triangleLists.Count; l++)
            {
                int[] triangles = triangleLists[l];
                if (triangles == null) continue;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    if (!ValidTriangle(triangles, t, count)) continue;
                    Union(parent, triangles[t], triangles[t + 1]);
                    Union(parent, triangles[t], triangles[t + 2]);
                    corners.Add(point[triangles[t]]); corners.Add(point[triangles[t + 1]]); corners.Add(point[triangles[t + 2]]);
                }
            }

            // Each triangle as it lies on the mesh, flattened on its own, in detail UV units.
            int triangleCount = corners.Count / 3;
            var rest = new double[triangleCount * 6];
            var weight = new double[triangleCount * 3];
            var area = new double[triangleCount];
            var usable = new bool[triangleCount];
            var signedUVArea = new double[count];
            var areas = new List<double>(triangleCount);
            for (int t = 0; t < triangleCount; t++)
            {
                int a = corners[t * 3], b = corners[t * 3 + 1], c = corners[t * 3 + 2];
                if (a == b || b == c || a == c) continue;
                Vector3 e1 = vertices[b] - vertices[a], e2 = vertices[c] - vertices[a];
                double length = e1.magnitude, cross = Vector3.Cross(e1, e2).magnitude;
                if (length < 1e-9 || cross < 1e-12) continue;
                usable[t] = true;
                area[t] = cross * 0.5;
                areas.Add(area[t]);
                rest[t * 6 + 2] = length / surfacePerUV;
                rest[t * 6 + 4] = Vector3.Dot(e2, e1) / length / surfacePerUV;
                rest[t * 6 + 5] = cross / length / surfacePerUV;
                signedUVArea[Find(parent, a)] += ((double)(uv[b].x - uv[a].x) * (uv[c].y - uv[a].y) - (double)(uv[b].y - uv[a].y) * (uv[c].x - uv[a].x)) * 0.5;
                // Cotangent of the angle opposite each edge; edge k joins corners k and k + 1.
                for (int k = 0; k < 3; k++)
                {
                    Vector3 opposite = vertices[corners[t * 3 + (k + 2) % 3]];
                    Vector3 d1 = vertices[corners[t * 3 + k]] - opposite, d2 = vertices[corners[t * 3 + (k + 1) % 3]] - opposite;
                    weight[t * 3 + k] = Math.Max(0.02, Vector3.Dot(d1, d2) / Math.Max(1e-12, Vector3.Cross(d1, d2).magnitude));
                }
            }
            if (areas.Count == 0) return start;
            areas.Sort();
            double medianArea = areas[areas.Count / 2];
            for (int t = 0; t < triangleCount; t++)
            {
                if (!usable[t]) continue;
                if (signedUVArea[Find(parent, corners[t * 3])] < 0) rest[t * 6 + 5] = -rest[t * 6 + 5];
                // Small triangles mark the areas that were modeled with care. Without this the nose and lips,
                // which are little surface, would take whatever distortion the cheeks and scalp pass on to them.
                double importance = Math.Min(20.0, Math.Max(0.05, Math.Sqrt(medianArea / area[t])));
                for (int k = 0; k < 3; k++) weight[t * 3 + k] *= importance;
            }

            // The layout's stiffness matrix: a diagonal and one entry per edge, in a fixed order.
            var diagonal = new double[count];
            var edgeWeights = new SortedDictionary<long, double>();
            for (int t = 0; t < triangleCount; t++)
            {
                if (!usable[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int i = corners[t * 3 + k], j = corners[t * 3 + (k + 1) % 3];
                    double w = weight[t * 3 + k];
                    diagonal[i] += w; diagonal[j] += w;
                    long key = i < j ? (long)i * count + j : (long)j * count + i;
                    edgeWeights.TryGetValue(key, out double sum);
                    edgeWeights[key] = sum + w;
                }
            }
            var edgeA = new int[edgeWeights.Count]; var edgeB = new int[edgeWeights.Count]; var edgeW = new double[edgeWeights.Count];
            int edgeIndex = 0;
            foreach (var pair in edgeWeights) { edgeA[edgeIndex] = (int)(pair.Key / count); edgeB[edgeIndex] = (int)(pair.Key % count); edgeW[edgeIndex] = pair.Value; edgeIndex++; }

            var x = new double[count]; var y = new double[count];
            for (int i = 0; i < count; i++) { x[i] = start[point[i]].x; y[i] = start[point[i]].y; }
            var targetX = new double[count]; var targetY = new double[count];
            var residual = new double[count]; var direction = new double[count]; var product = new double[count];

            for (int pass = 0; pass < iterations; pass++)
            {
                // Local step: turn every flattened triangle to where it best matches the current layout.
                Array.Clear(targetX, 0, count); Array.Clear(targetY, 0, count);
                for (int t = 0; t < triangleCount; t++)
                {
                    if (!usable[t]) continue;
                    double sxx = 0, sxy = 0, syx = 0, syy = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        int i = corners[t * 3 + k], j = corners[t * 3 + (k + 1) % 3], k2 = (k + 1) % 3;
                        double ux = x[i] - x[j], uy = y[i] - y[j];
                        double rx = rest[t * 6 + k * 2] - rest[t * 6 + k2 * 2], ry = rest[t * 6 + k * 2 + 1] - rest[t * 6 + k2 * 2 + 1];
                        double w = weight[t * 3 + k];
                        sxx += w * ux * rx; sxy += w * ux * ry; syx += w * uy * rx; syy += w * uy * ry;
                    }
                    double cos = sxx + syy, sin = syx - sxy, norm = Math.Sqrt(cos * cos + sin * sin);
                    if (norm < 1e-18) { cos = 1; sin = 0; } else { cos /= norm; sin /= norm; }
                    for (int k = 0; k < 3; k++)
                    {
                        int i = corners[t * 3 + k], j = corners[t * 3 + (k + 1) % 3], k2 = (k + 1) % 3;
                        double rx = rest[t * 6 + k * 2] - rest[t * 6 + k2 * 2], ry = rest[t * 6 + k * 2 + 1] - rest[t * 6 + k2 * 2 + 1];
                        double w = weight[t * 3 + k];
                        double tx = w * (cos * rx - sin * ry), ty = w * (sin * rx + cos * ry);
                        targetX[i] += tx; targetX[j] -= tx; targetY[i] += ty; targetY[j] -= ty;
                    }
                }
                // Global step: the layout that fits those triangles best (conjugate gradients, from the current layout).
                SolveLayout(x, targetX, point, diagonal, edgeA, edgeB, edgeW, residual, direction, product);
                SolveLayout(y, targetY, point, diagonal, edgeA, edgeB, edgeW, residual, direction, product);
            }

            // Every island starts at (1,1) again: no coordinate is below 1, which is how the shaders know them.
            var origin = new Vector2[count];
            for (int i = 0; i < count; i++) origin[i] = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                double px = x[point[i]], py = y[point[i]];
                if (double.IsNaN(px) || double.IsInfinity(px) || double.IsNaN(py) || double.IsInfinity(py)) return start;
                result[i] = new Vector2((float)px, (float)py);
                int root = Find(parent, i);
                origin[root] = Vector2.Min(origin[root], result[i]);
            }
            for (int i = 0; i < count; i++) result[i] = result[i] - origin[Find(parent, i)] + Vector2.one;
            return result;
        }

        /// <summary>A few conjugate gradient steps on the layout matrix, for one coordinate. Only welded points are unknowns.</summary>
        private static void SolveLayout(double[] value, double[] target, int[] point, double[] diagonal, int[] edgeA, int[] edgeB, double[] edgeW,
            double[] residual, double[] direction, double[] product)
        {
            int count = value.Length;
            Multiply(value, product, diagonal, edgeA, edgeB, edgeW);
            double rr = 0;
            for (int i = 0; i < count; i++)
            {
                residual[i] = point[i] == i ? target[i] - product[i] : 0;
                direction[i] = residual[i];
                rr += residual[i] * residual[i];
            }
            for (int step = 0; step < 40 && rr > 1e-22; step++)
            {
                Multiply(direction, product, diagonal, edgeA, edgeB, edgeW);
                double dp = 0;
                for (int i = 0; i < count; i++) if (point[i] == i) dp += direction[i] * product[i];
                if (dp <= 1e-30) break;
                double alpha = rr / dp, next = 0;
                for (int i = 0; i < count; i++)
                {
                    if (point[i] != i) continue;
                    value[i] += alpha * direction[i];
                    residual[i] -= alpha * product[i];
                    next += residual[i] * residual[i];
                }
                double beta = next / rr;
                rr = next;
                for (int i = 0; i < count; i++) if (point[i] == i) direction[i] = residual[i] + beta * direction[i];
            }
        }

        private static void Multiply(double[] input, double[] output, double[] diagonal, int[] edgeA, int[] edgeB, double[] edgeW)
        {
            for (int i = 0; i < input.Length; i++) output[i] = diagonal[i] * input[i];
            for (int e = 0; e < edgeA.Length; e++)
            {
                output[edgeA[e]] -= edgeW[e] * input[edgeB[e]];
                output[edgeB[e]] -= edgeW[e] * input[edgeA[e]];
            }
        }

        /// <summary>True when the slot's mesh has a fourth UV channel with one coordinate per vertex.</summary>
        public static bool HasDetailUVs(SlotDataAsset slot)
        {
            var mesh = slot != null ? slot.meshData : null;
            return mesh != null && mesh.vertices != null && mesh.uv4 != null && mesh.uv4.Length == mesh.vertices.Length && mesh.uv4.Length > 0;
        }

        /// <summary>
        /// Writes detail coordinates into the slot's fourth UV channel and marks the slot dirty. Nothing is saved.
        /// Returns the number of UV islands. Every submesh and every LOD of the slot is taken into account.
        /// </summary>
        public static int BakeSlot(SlotDataAsset slot, float surfacePerUV = DefaultSurfacePerUV)
        {
            return BakeSlot(slot, surfacePerUV, 0);
        }

        /// <summary>
        /// As <see cref="BakeSlot(SlotDataAsset, float)"/>, but also unfolds every island so the detail map keeps one
        /// size and little stretch inside it (<see cref="ComputeRelaxedDetailUVs"/>). Use it for slots whose texture
        /// layout is uneven, such as a head that gives the face more texture than the neck.
        /// </summary>
        public static int BakeSlotRelaxed(SlotDataAsset slot, float surfacePerUV = DefaultSurfacePerUV, int iterations = DefaultRelaxIterations)
        {
            return BakeSlot(slot, surfacePerUV, Math.Max(1, iterations));
        }

        private static int BakeSlot(SlotDataAsset slot, float surfacePerUV, int relaxIterations)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));
            var mesh = slot.meshData;
            if (mesh == null || mesh.vertices == null || mesh.vertices.Length == 0 || mesh.uv == null || mesh.uv.Length != mesh.vertices.Length)
                throw new ArgumentException("The slot '" + slot.name + "' has no mesh with a first UV channel.", nameof(slot));

            var triangleLists = new List<int[]>();
            if (mesh.submeshes != null)
            {
                foreach (var submesh in mesh.submeshes)
                {
                    if (submesh == null) continue;
                    int lods = Math.Max(1, submesh.LODCount());
                    for (int lod = 0; lod < lods; lod++)
                    {
                        int[] triangles = submesh.getManagedTriangles(lod);
                        if (triangles != null && triangles.Length > 0) triangleLists.Add(triangles);
                    }
                }
            }

            int islands;
            mesh.uv4 = relaxIterations > 0
                ? ComputeRelaxedDetailUVs(mesh.vertices, mesh.uv, triangleLists, surfacePerUV, out islands, relaxIterations)
                : ComputeDetailUVs(mesh.vertices, mesh.uv, triangleLists, surfacePerUV, out islands);
            // Characters built earlier in this session must not reuse buffers made from the old channel.
            mesh.InvalidateBuildData();
            EditorUtility.SetDirty(slot);
            return islands;
        }

        /// <summary>Removes the slot's fourth UV channel and marks the slot dirty. Returns false when it had none.</summary>
        public static bool ClearSlot(SlotDataAsset slot)
        {
            if (slot == null || slot.meshData == null || slot.meshData.uv4 == null || slot.meshData.uv4.Length == 0) return false;
            slot.meshData.uv4 = new Vector2[0];
            slot.meshData.InvalidateBuildData();
            EditorUtility.SetDirty(slot);
            return true;
        }

        private static int Quantize(float value) { return Mathf.RoundToInt(value * 100000f); }

        private static bool ValidTriangle(int[] triangles, int start, int vertexCount)
        {
            return (uint)triangles[start] < (uint)vertexCount && (uint)triangles[start + 1] < (uint)vertexCount && (uint)triangles[start + 2] < (uint)vertexCount;
        }

        private static float Scale(double surface, double uvArea, float surfacePerUV, float fallback)
        {
            return surface > 1e-12 && uvArea > 1e-12 ? (float)(Math.Sqrt(surface / uvArea) / surfacePerUV) : fallback;
        }

        private static int Find(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }
            return index;
        }

        private static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a); b = Find(parent, b);
            // The lower index stays the root, so results do not depend on the order of the triangles.
            if (a < b) parent[b] = a; else if (b < a) parent[a] = b;
        }

        private const string BakeMenu = "Assets/UMA/Skin/Bake Even Detail UVs On Selected Slots";
        private const string BakeRelaxedMenu = "Assets/UMA/Skin/Bake Even Detail UVs On Selected Slots (Unfold Islands)";
        private const string ClearMenu = "Assets/UMA/Skin/Remove Even Detail UVs From Selected Slots";

        private static List<SlotDataAsset> SelectedSlots()
        {
            var slots = new List<SlotDataAsset>();
            foreach (var selected in Selection.objects)
                if (selected is SlotDataAsset slot && AssetDatabase.Contains(slot)) slots.Add(slot);
            return slots;
        }

        [MenuItem(BakeMenu, true)]
        [MenuItem(BakeRelaxedMenu, true)]
        [MenuItem(ClearMenu, true)]
        private static bool CanChangeSelectedSlots() { return SelectedSlots().Count > 0; }

        [MenuItem(BakeMenu)]
        private static void BakeSelectedSlots() { BakeSelectedSlots(false); }

        [MenuItem(BakeRelaxedMenu)]
        private static void BakeSelectedSlotsRelaxed() { BakeSelectedSlots(true); }

        private static void BakeSelectedSlots(bool relaxed)
        {
            var slots = SelectedSlots();
            int replaced = 0;
            foreach (var slot in slots) if (slot.meshData != null && slot.meshData.uv4 != null && slot.meshData.uv4.Length > 0) replaced++;
            if (replaced > 0 && !EditorUtility.DisplayDialog("Bake Even Detail UVs",
                replaced + " of the selected slots already have a fourth UV channel. It will be replaced.", "Replace", "Cancel"))
                return;

            int baked = 0;
            foreach (var slot in slots)
            {
                try
                {
                    if (relaxed) BakeSlotRelaxed(slot); else BakeSlot(slot);
                    // Only the changed slot is written; other unsaved work in the project is left as it is.
                    AssetDatabase.SaveAssetIfDirty(slot);
                    baked++;
                }
                catch (ArgumentException e)
                {
                    Debug.LogWarning(e.Message, slot);
                }
            }
            Debug.Log("Baked even detail UVs" + (relaxed ? ", with unfolded islands," : "") + " into the fourth UV channel of " + baked + " of " + slots.Count + " selected slots. Rebuild characters that use them.");
        }

        [MenuItem(ClearMenu)]
        private static void ClearSelectedSlots()
        {
            int cleared = 0;
            foreach (var slot in SelectedSlots())
            {
                if (!ClearSlot(slot)) continue;
                AssetDatabase.SaveAssetIfDirty(slot);
                cleared++;
            }
            Debug.Log("Removed the fourth UV channel from " + cleared + " selected slots. Rebuild characters that use them.");
        }
    }
}
