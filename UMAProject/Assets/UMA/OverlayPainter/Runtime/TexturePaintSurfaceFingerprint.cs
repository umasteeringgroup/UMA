using UnityEngine;

namespace UMA.TexturePaint
{
    public readonly struct TexturePaintSurfaceFingerprint
    {
        public readonly string geometry;
        public readonly string topology;
        public readonly string uv;

        public TexturePaintSurfaceFingerprint(string geometry, string topology, string uv)
        {
            this.geometry = geometry;
            this.topology = topology;
            this.uv = uv;
        }
    }

    public static class TexturePaintSurfaceFingerprintUtility
    {
        public static TexturePaintSurfaceFingerprint Compute(Mesh mesh)
        {
            if (mesh == null) return new TexturePaintSurfaceFingerprint(string.Empty, string.Empty, string.Empty);
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            int[] indices = mesh.triangles;
            ulong geometry = Begin(), topology = Begin(), uv = Begin();
            Add(ref geometry, vertices.Length); Add(ref topology, vertices.Length); Add(ref uv, uvs.Length);
            Add(ref topology, mesh.subMeshCount); Add(ref topology, indices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Add(ref geometry, vertices[i].x); Add(ref geometry, vertices[i].y); Add(ref geometry, vertices[i].z);
            }
            for (int i = 0; i < indices.Length; i++) Add(ref topology, indices[i]);
            for (int i = 0; i < uvs.Length; i++) { Add(ref uv, uvs[i].x); Add(ref uv, uvs[i].y); }
            return new TexturePaintSurfaceFingerprint(Hex(geometry), Hex(topology), Hex(uv));
        }

        private static ulong Begin() => 1469598103934665603UL;
        private static void Add(ref ulong hash, int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }
        private static void Add(ref ulong hash, float value) => Add(ref hash, value.GetHashCode());
        private static string Hex(ulong value) => value.ToString("X16");
    }
}
