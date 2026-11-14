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
        /// <paramref name="surfacePerUV"/> mesh units of surface per UV unit. No coordinate is (0,0): the
        /// shaders treat (0,0) as "this mesh has no detail coordinates".
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
                // Starting every island at (1,1) keeps all coordinates positive, so none is (0,0).
                result[i] = (uv[i] - origin[root]) * scale + Vector2.one;
            }
            return result;
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

            mesh.uv4 = ComputeDetailUVs(mesh.vertices, mesh.uv, triangleLists, surfacePerUV, out int islands);
            EditorUtility.SetDirty(slot);
            return islands;
        }

        /// <summary>Removes the slot's fourth UV channel and marks the slot dirty. Returns false when it had none.</summary>
        public static bool ClearSlot(SlotDataAsset slot)
        {
            if (slot == null || slot.meshData == null || slot.meshData.uv4 == null || slot.meshData.uv4.Length == 0) return false;
            slot.meshData.uv4 = new Vector2[0];
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
        private const string ClearMenu = "Assets/UMA/Skin/Remove Even Detail UVs From Selected Slots";

        private static List<SlotDataAsset> SelectedSlots()
        {
            var slots = new List<SlotDataAsset>();
            foreach (var selected in Selection.objects)
                if (selected is SlotDataAsset slot && AssetDatabase.Contains(slot)) slots.Add(slot);
            return slots;
        }

        [MenuItem(BakeMenu, true)]
        [MenuItem(ClearMenu, true)]
        private static bool CanChangeSelectedSlots() { return SelectedSlots().Count > 0; }

        [MenuItem(BakeMenu)]
        private static void BakeSelectedSlots()
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
                    BakeSlot(slot);
                    // Only the changed slot is written; other unsaved work in the project is left as it is.
                    AssetDatabase.SaveAssetIfDirty(slot);
                    baked++;
                }
                catch (ArgumentException e)
                {
                    Debug.LogWarning(e.Message, slot);
                }
            }
            Debug.Log("Baked even detail UVs into the fourth UV channel of " + baked + " of " + slots.Count + " selected slots. Rebuild characters that use them.");
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
