using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Optional extension. Existing generators continue to use Prepare(settings).</summary>
    public interface ITexturePaintContextualPathGenerator : ITexturePaintPathGenerator
    {
        IDisposable Prepare(TexturePaintPathGeneratorSettings settings, TexturePaintPathGeneratorContext context);
    }

    /// <summary>Sampling information, supplied before replacing the last valid layer output.
    /// Size is (full base width, arc length), in world units or normalized UV units.</summary>
    public sealed class TexturePaintPathGeneratorContext
    {
        public Vector2 size = new Vector2(.01f, .05f);
        public int resolution = 2048;
        public float footprintPixels = 512;
        public bool draft, closed, directUV;
        public bool sharedUV;
        // True when increasing ribbon Y points below world up. UV paths keep their authored
        // orientation. Anatomical generators can compensate without changing the path or its roll.
        public bool acrossPointsDown;
        public CancellationToken cancellation;
        private sealed class Overlap {public bool value;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mesh,Overlap> overlapCache=new();
        public static bool HasSharedUV(Mesh mesh)
        {
            if(mesh==null)return false;
            return overlapCache.GetValue(mesh,FindSharedUV).value;
        }
        private static Overlap FindSharedUV(Mesh mesh)
        {
            var triangles=mesh.triangles;var uv=mesh.uv;var vertices=mesh.vertices;
            var owners=new Dictionary<string,Vector3>();
            string Key(Vector2 p)=>Mathf.RoundToInt(p.x*100000)+","+Mathf.RoundToInt(p.y*100000);
            for(int i=0;i+2<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];if(a>=uv.Length||b>=uv.Length||c>=uv.Length)continue;
                if(Mathf.Abs((uv[b].x-uv[a].x)*(uv[c].y-uv[a].y)-(uv[b].y-uv[a].y)*(uv[c].x-uv[a].x))<1e-10f)continue;
                var keys=new[]{Key(uv[a]),Key(uv[b]),Key(uv[c])};Array.Sort(keys,StringComparer.Ordinal);string key=string.Join("/",keys);
                Vector3 center=(vertices[a]+vertices[b]+vertices[c])/3;
                if(owners.TryGetValue(key,out var other)&&(center-other).sqrMagnitude>1e-8f)return new Overlap {value=true};
                owners[key]=center;
            }
            return new Overlap();
        }

        public static TexturePaintPathGeneratorContext FromSamples(IReadOnlyList<StrokeSample> samples,
            float width, int resolution, bool closed, bool directUV)
        {
            float length = 0, uvLength = 0, acrossUp = 0;
            for (int i = 1; samples != null && i < samples.Count; i++)
            {
                if (!directUV)
                    acrossUp += Vector3.Dot(Vector3.Cross(samples[i-1].worldNormal + samples[i].worldNormal,
                        samples[i].worldPosition - samples[i-1].worldPosition), Vector3.up);
                length += directUV ? Vector2.Distance(samples[i-1].uv, samples[i].uv) :
                    Vector3.Distance(samples[i-1].worldPosition, samples[i].worldPosition);
                if (samples[i-1].uvIsland == samples[i].uvIsland)
                    uvLength += Vector2.Distance(samples[i-1].uv, samples[i].uv);
            }
            return new TexturePaintPathGeneratorContext { size = new Vector2(width, length),
                resolution = resolution, footprintPixels = Mathf.Clamp(uvLength * resolution, 32, resolution),
                closed = closed, directUV = directUV, acrossPointsDown = !directUV && acrossUp < -1e-8f };
        }
    }

    /// <summary>Prepared raster may extend beyond its root ribbon without shrinking its groom.
    /// Padding is a fraction of full length (X) and full width (Y), on each side.</summary>
    public interface ITexturePaintPaddedPathRaster { Vector2 Padding { get; } }

    public static class TexturePaintPathRasterPadding
    {
        public static TexturePaintRibbonSegment[] Expand(TexturePaintRibbonSegment[] source, Vector2 padding)
        {
            if (source.Length == 0 || padding == Vector2.zero) return source;
            float first = float.PositiveInfinity, last = float.NegativeInfinity;
            foreach (var s in source) { first = Mathf.Min(first, s.leftStartAlong.w); last = Mathf.Max(last, s.leftEndAlong.w); }
            var result = new List<TexturePaintRibbonSegment>(source.Length + 4);
            foreach (var original in source)
            {
                var s = original;
                Vector3 w0 = ((Vector3)s.rightStartFlow - (Vector3)s.leftStartAlong) * padding.y;
                Vector3 w1 = ((Vector3)s.rightEndFlow - (Vector3)s.leftEndAlong) * padding.y;
                s.leftStartAlong -= new Vector4(w0.x,w0.y,w0.z,0); s.rightStartFlow += new Vector4(w0.x,w0.y,w0.z,0);
                s.leftEndAlong -= new Vector4(w1.x,w1.y,w1.z,0); s.rightEndFlow += new Vector4(w1.x,w1.y,w1.z,0);
                Vector3 delta = ((Vector3)s.leftEndAlong + (Vector3)s.rightEndFlow - (Vector3)s.leftStartAlong - (Vector3)s.rightStartFlow)*.5f;
                float interval = s.leftEndAlong.w - s.leftStartAlong.w;
                Vector3 extension = interval > 1e-8f ? delta / interval * ((last-first)*padding.x) : Vector3.zero;
                var d = new Vector4(extension.x,extension.y,extension.z,0);
                if (Mathf.Abs(s.leftStartAlong.w-first)<1e-5f && padding.x>0)
                {
                    var cap = s; cap.leftEndAlong = s.leftStartAlong; cap.rightEndFlow = s.rightStartFlow;
                    cap.normalEndPressure = cap.normalStartPressure; cap.colorEnd = cap.colorStart;
                    cap.leftStartAlong -= d; cap.leftStartAlong.w = first-(last-first)*padding.x; cap.rightStartFlow -= d;
                    result.Add(cap);
                }
                result.Add(s);
                if (Mathf.Abs(s.leftEndAlong.w-last)<1e-5f && padding.x>0)
                {
                    var cap = s; cap.leftStartAlong = s.leftEndAlong; cap.rightStartFlow = s.rightEndFlow;
                    cap.normalStartPressure = cap.normalEndPressure; cap.colorStart = cap.colorEnd;
                    cap.leftEndAlong += d; cap.leftEndAlong.w = last+(last-first)*padding.x; cap.rightEndFlow += d;
                    result.Add(cap);
                }
            }
            return result.ToArray();
        }
    }
}
