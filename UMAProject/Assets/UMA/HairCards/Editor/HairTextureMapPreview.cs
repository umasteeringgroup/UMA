using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.HairCards.Editor
{
    /// <summary>Disposable GPU view of serialized face texels. No generated mesh subdivision
    /// and no readback: the shader uses the exact same triangular filter as CPU placement.</summary>
    internal sealed class HairTextureMapPreview : IDisposable
    {
        internal Mesh Mesh { get; private set; }
        internal Material Material { get; private set; }
        internal Texture2D Texture { get; private set; }
        private Mesh source, posed;
        private HairGrowthMap activeMap;
        private int revision, size;
        private int tileCount = -1, detail;
        private float[] pixels;
        private readonly List<HairTexturePaintSurface.Face> faces = new List<HairTexturePaintSurface.Face>();
        private readonly List<Vector4> coords = new List<Vector4>();
        private readonly List<Vector2> field = new List<Vector2>();
        private readonly Dictionary<long, int> slots = new Dictionary<long, int>();

        private static Vector4 MapRange(HairGrowthMap map) => new Vector4(map.valueRange.x,
            1f / Mathf.Max(.000001f, map.valueRange.y - map.valueRange.x), 0, 0);

        // Preview textures deliberately have DontSave flags. Unity can restore a
        // material's serialized state without restoring that temporary texture
        // reference. An unchanged paint revision is not proof the GPU view is valid.
        internal bool NeedsRefresh(HairGrowthMap map) => map?.UsesTexture == true &&
            (activeMap != map || revision != map.TextureRevision || Mesh == null || Material == null || Texture == null ||
             Material.GetTexture("_Map") != Texture || Material.GetFloat("_InvAtlasSize") != 1f / size ||
             Material.GetFloat("_Resolution") != map.texture.resolution || Material.GetVector("_Range") != MapRange(map));

        private void BindMaterial(HairGrowthMap map)
        {
            Material.SetTexture("_Map", Texture); Material.SetFloat("_InvAtlasSize", 1f / size);
            Material.SetFloat("_Resolution", map.texture.resolution);
            Material.SetVector("_Range", MapRange(map));
        }

        internal void Update(Mesh sourceMesh, Mesh paintMesh, HairGrowthMap map, Predicate<int> visible, bool force)
        {
            if (Material == null)
            {
                var shader = Shader.Find("Hidden/UMA/Hair Texture Map");
                if (shader == null) throw new InvalidOperationException("Hair Texture Map preview shader was not imported.");
                Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3100 };
            }
            if (Mesh == null || source != sourceMesh || posed != paintMesh)
            {
                if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
                source = sourceMesh; posed = paintMesh; faces.Clear();
                var positions = paintMesh.vertices; var normals = paintMesh.normals;
                var points = new List<Vector3>(); var indices = new List<int>();
                float offset = Mathf.Max(.00001f, paintMesh.bounds.size.magnitude * .00015f);
                for (int sub = 0; sub < sourceMesh.subMeshCount; sub++)
                {
                    var ts = sourceMesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < ts.Length; i += 3)
                    {
                        int a = ts[i], b = ts[i + 1], c = ts[i + 2];
                        if (visible != null && (!visible(a) || !visible(b) || !visible(c))) continue;
                        faces.Add(new HairTexturePaintSurface.Face(sub, i / 3, a, b, c));
                        for (int k = 0; k < 3; k++)
                        { int vertex = ts[i + k]; indices.Add(points.Count); points.Add(positions[vertex] + (normals.Length == positions.Length ? normals[vertex] * offset : Vector3.zero)); }
                    }
                }
                Mesh = new Mesh { name = "Hair Texture Paint Preview", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                Mesh.MarkDynamic(); Mesh.SetVertices(points); Mesh.SetTriangles(indices, 0); Mesh.RecalculateBounds(); force = true;
            }
            if (!force && activeMap == map && revision == map.TextureRevision && Texture != null)
            {
                BindMaterial(map);
                return;
            }
            bool updateCoordinates = force || activeMap != map || tileCount != map.texture.tiles.Count || detail != map.texture.resolution;
            activeMap = map; revision = map.TextureRevision;
            tileCount = map.texture.tiles.Count; detail = map.texture.resolution;
            int stride = map.texture.resolution + 1;
            int desired = Mathf.NextPowerOfTwo(Mathf.Max(stride, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, map.texture.tiles.Count))) * stride));
            // Allow for integral tile rows when the power-of-two atlas isn't divisible by stride.
            while ((desired / stride) * (desired / stride) < map.texture.tiles.Count) desired *= 2;
            if (desired > SystemInfo.maxTextureSize) throw new InvalidOperationException("Texture paint preview exceeds this GPU's maximum atlas size. Reduce texels per face edge.");
            if (Texture == null || size < desired || size > desired * 2)
            {
                if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture);
                updateCoordinates = true;
                size = desired; pixels = new float[size * size];
                Texture = new Texture2D(size, size, TextureFormat.RFloat, false, true)
                    { name = "Hair Paint Texels (Preview)", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            slots.Clear(); int columns = size / stride;
            for (int i = 0; i < map.texture.tiles.Count; i++)
            {
                var tile = map.texture.tiles[i]; slots[tile.Key] = i;
                int ox = i % columns * stride, oy = i / columns * stride;
                for (int y = 0; y < stride; y++) Array.Copy(tile.pixels, y * stride, pixels, (oy + y) * size + ox, stride);
            }
            Texture.SetPixelData(pixels, 0); Texture.Apply(false, false);
            if (updateCoordinates)
            {
                coords.Clear(); field.Clear();
                foreach (var face in faces)
                {
                    bool found = slots.TryGetValue(HairTextureMap.FaceKey(face.submesh, face.triangle), out int slot);
                    int x = slot % columns * stride, y = slot / columns * stride;
                    coords.Add(new Vector4(0, 0, x, y)); coords.Add(new Vector4(1, 0, x, y)); coords.Add(new Vector4(0, 1, x, y));
                    field.Add(new Vector2(map.BaseVertex(face.a), found ? 1 : 0));
                    field.Add(new Vector2(map.BaseVertex(face.b), found ? 1 : 0));
                    field.Add(new Vector2(map.BaseVertex(face.c), found ? 1 : 0));
                }
                Mesh.SetUVs(0, coords); Mesh.SetUVs(1, field);
            }
            BindMaterial(map);
        }
        public void Dispose()
        {
            if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
            if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
            if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture);
            Mesh = null; Material = null; Texture = null;
        }
    }
}
