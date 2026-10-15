using UnityEngine;

namespace UMA.TexturePaint
{
    public readonly struct TexturePaintSurfaceFingerprint
    {
        public readonly string geometry;
        public readonly string topology;
        public readonly string uv;
        public readonly string uvLayout;

        public TexturePaintSurfaceFingerprint(string geometry, string topology, string uv, string uvLayout = "")
        {
            this.geometry = geometry;
            this.topology = topology;
            this.uv = uv;
            this.uvLayout = uvLayout;
        }
    }

    public static class TexturePaintSurfaceFingerprintUtility
    {
        public static TexturePaintSurfaceFingerprint Compute(Mesh mesh, bool includeUvLayout = false)
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
            return new TexturePaintSurfaceFingerprint(Hex(geometry), Hex(topology), Hex(uv), includeUvLayout ? UvLayout(uvs,indices) : string.Empty);
        }

        // Reconstruction may reorder vertices and triangles while preserving the complete UV
        // triangle layout. Raster artwork remains portable between a slot and its equipped mesh.
        private static string UvLayout(Vector2[] uvs,int[] indices)
        {
            var triangles=new ulong[indices.Length/3];var corners=new ulong[3];
            for(int i=0;i<triangles.Length;i++)
            {
                for(int j=0;j<3;j++)
                {
                    int index=indices[i*3+j];if((uint)index>=(uint)uvs.Length)return string.Empty;
                    var point=uvs[index];if(!float.IsFinite(point.x+point.y))return string.Empty;
                    ulong corner=Begin();Add(ref corner,Mathf.RoundToInt(point.x*1000000));Add(ref corner,Mathf.RoundToInt(point.y*1000000));corners[j]=corner;
                }
                System.Array.Sort(corners);ulong triangle=Begin();
                foreach(ulong corner in corners){Add(ref triangle,(int)corner);Add(ref triangle,(int)(corner>>32));}
                triangles[i]=triangle;
            }
            System.Array.Sort(triangles);ulong layout=Begin();Add(ref layout,triangles.Length);
            foreach(ulong triangle in triangles){Add(ref layout,(int)triangle);Add(ref layout,(int)(triangle>>32));}
            return Hex(layout);
        }

        private static ulong Begin() => 1469598103934665603UL;
        private static void Add(ref ulong hash, int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }
        private static void Add(ref ulong hash, float value) => Add(ref hash, value.GetHashCode());
        private static string Hex(ulong value) => value.ToString("X16");
    }
}
