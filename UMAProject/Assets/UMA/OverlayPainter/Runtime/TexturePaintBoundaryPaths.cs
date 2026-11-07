using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Extracts ordered editable contours. Shared UV edges cancel even when mesh
    /// vertices are duplicated for hard normals. Different UV islands remain separate.</summary>
    public static class TexturePaintBoundaryPaths
    {
        public sealed class Contour
        {
            public readonly List<Vector2> points = new List<Vector2>();
            public bool closed;
        }
        private readonly struct Edge
        {
            public readonly Vector2Int a,b;
            public Edge(Vector2Int a,Vector2Int b) { this.a=a;this.b=b; }
        }
        private const float Precision=1000000;
        private static Vector2Int Key(Vector2 uv)=>new Vector2Int(Mathf.RoundToInt(uv.x*Precision),Mathf.RoundToInt(uv.y*Precision));
        private static (Vector2Int,Vector2Int) Pair(Vector2Int a,Vector2Int b)=>a.x<b.x||a.x==b.x&&a.y<b.y?(a,b):(b,a);

        public static List<Contour> FromMesh(Vector2[] uv,int[] triangles)
        {
            var edges=new Dictionary<(Vector2Int,Vector2Int),(Edge edge,int count)>();
            var trianglesSeen=new HashSet<(Vector2Int,Vector2Int,Vector2Int)>();
            for(int i=0;i+2<triangles.Length;i+=3)
            {
                Vector2 a=uv[triangles[i]],b=uv[triangles[i+1]],c=uv[triangles[i+2]];
                float area=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
                if(Mathf.Abs(area)<1e-12f)continue;
                if(area<0)(b,c)=(c,b);
                var keys=new[]{Key(a),Key(b),Key(c)};
                Array.Sort(keys,(left,right)=>left.x!=right.x?left.x.CompareTo(right.x):left.y.CompareTo(right.y));
                // Mirrored or stacked geometry can share the same complete UV triangle.
                // Trace that occupied UV region once rather than canceling its perimeter.
                if(!trianglesSeen.Add((keys[0],keys[1],keys[2])))continue;
                Add(Key(a),Key(b));Add(Key(b),Key(c));Add(Key(c),Key(a));
            }
            var boundary=new List<Edge>();foreach(var value in edges.Values)if(value.count==1)boundary.Add(value.edge);
            return Connect(boundary);
            void Add(Vector2Int a,Vector2Int b)
            { var pair=Pair(a,b);edges.TryGetValue(pair,out var old);edges[pair]=(new Edge(a,b),old.count+1); }
        }

        public static List<Contour> FromMask(Color[] pixels,int width,int height,float threshold)
        {
            if(pixels==null||pixels.Length!=width*height)throw new ArgumentException("Mask dimensions must match pixels.");
            var edges=new List<Edge>();
            bool Filled(int x,int y)=>x>=0&&y>=0&&x<width&&y<height&&pixels[y*width+x].r*pixels[y*width+x].a>=threshold;
            Vector2Int Point(int x,int y)=>Key(new Vector2(x/(float)width,y/(float)height));
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                if(!Filled(x,y))continue;
                if(!Filled(x,y-1))edges.Add(new Edge(Point(x,y),Point(x+1,y)));
                if(!Filled(x+1,y))edges.Add(new Edge(Point(x+1,y),Point(x+1,y+1)));
                if(!Filled(x,y+1))edges.Add(new Edge(Point(x+1,y+1),Point(x,y+1)));
                if(!Filled(x-1,y))edges.Add(new Edge(Point(x,y+1),Point(x,y)));
            }
            return Connect(edges);
        }

        private static List<Contour> Connect(List<Edge> edges)
        {
            var outgoing=new Dictionary<Vector2Int,List<int>>();
            for(int i=0;i<edges.Count;i++) { if(!outgoing.TryGetValue(edges[i].a,out var list))outgoing.Add(edges[i].a,list=new List<int>());list.Add(i); }
            var used=new bool[edges.Count];var result=new List<Contour>();
            for(int i=0;i<edges.Count;i++)
            {
                if(used[i])continue; var path=new Contour(); int current=i; Vector2Int first=edges[i].a;
                while(!used[current])
                {
                    Edge edge=edges[current];used[current]=true;path.points.Add((Vector2)edge.a/Precision);
                    if(edge.b==first){path.closed=true;break;}
                    int next=-1;float best=-float.MaxValue;
                    if(outgoing.TryGetValue(edge.b,out var candidates))foreach(int candidate in candidates)
                    {
                        if(used[candidate])continue;
                        Vector2 d=(Vector2)(edge.b-edge.a),e=(Vector2)(edges[candidate].b-edge.b);
                        float turn=Mathf.Atan2(d.x*e.y-d.y*e.x,Vector2.Dot(d,e));
                        if(turn>best){best=turn;next=candidate;}
                    }
                    if(next<0){path.points.Add((Vector2)edge.b/Precision);break;}current=next;
                }
                if(path.points.Count>=2)result.Add(path);
            }
            return result;
        }

        public static List<Vector2> Prepare(Contour contour,float inset,float tolerance)
        {
            var points=new List<Vector2>(contour.points);
            // Remove redundant vertices without rounding corners. Remaining points are ordinary
            // editable path controls, not an opaque baked stitch image.
            bool changed=true;
            while(changed&&points.Count>(contour.closed?3:2))
            {
                changed=false;
                for(int i=contour.closed?0:1;i<(contour.closed?points.Count:points.Count-1);i++)
                {
                    Vector2 a=points[(i+points.Count-1)%points.Count],b=points[(i+1)%points.Count],d=b-a;
                    float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(points[i]-a,d)/d.sqrMagnitude):0;
                    if(Vector2.Distance(points[i],a+d*t)<=tolerance){points.RemoveAt(i);changed=true;break;}
                }
            }
            var result=new List<Vector2>(points.Count);
            for(int i=0;i<points.Count;i++)
            {
                Vector2 before=(points[i]-points[(i+points.Count-1)%points.Count]).normalized;
                Vector2 after=(points[(i+1)%points.Count]-points[i]).normalized;
                if(!contour.closed){if(i==0)before=after;if(i==points.Count-1)after=before;}
                Vector2 n1=new Vector2(-before.y,before.x),n2=new Vector2(-after.y,after.x),normal=(n1+n2).normalized;
                // Limit miters so acute UV corners cannot generate huge spikes.
                float length=inset/Mathf.Max(.25f,Vector2.Dot(normal,n2));
                result.Add(points[i]+normal*length);
            }
            return result;
        }
    }
}
