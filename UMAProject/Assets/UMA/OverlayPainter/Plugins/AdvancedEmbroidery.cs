using System;
using System.Threading;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>Prepared once per command, shared across output channels and destinations.
    /// Coordinates and distances are in one motif tile, independent of destination resolution.</summary>
    internal sealed class AdvancedEmbroidery
    {
        internal readonly struct Result
        {
            public readonly Color color;public readonly float coverage,height,roughness,ao;
            public Result(Color c,float a,float h,float r,float o){color=c;coverage=a;height=h;roughness=r;ao=o;}
        }
        private readonly int size,style,seed;
        private readonly float[] distance,arc;
        private readonly TexturePaintReadOnlyParameterTexture directions;
        private readonly float density,length,borderWidth,height,roughness,variation,angle,redAngle,greenAngle,blueAngle;
        private readonly Color thread,border,red,green,blue;
        public AdvancedEmbroidery(TexturePaintPluginParameterSet p,TexturePaintReadOnlyParameterTexture pattern,
            TexturePaintReadOnlyParameterTexture regions,CancellationToken cancellation)
        {
            style=p.Integer("embroideryStyle",1);seed=p.Integer("seed",1731);directions=regions;
            density=Mathf.Clamp(p.Float("threadDensity",180),4,1024);length=Mathf.Clamp(p.Float("threadLength",.12f),.005f,1);
            borderWidth=Mathf.Clamp(p.Float("borderWidth",.025f),0,.2f);height=Mathf.Clamp(p.Float("embroideryHeight",.18f),0,.5f);
            roughness=Mathf.Clamp01(p.Float("baseRoughness",.65f));variation=Mathf.Clamp01(p.Float("threadVariation",.18f));
            angle=p.Float("fiberDirection",45)*Mathf.Deg2Rad;redAngle=p.Float("redDirection",0)*Mathf.Deg2Rad;
            greenAngle=p.Float("greenDirection",60)*Mathf.Deg2Rad;blueAngle=p.Float("blueDirection",120)*Mathf.Deg2Rad;
            thread=p.LinearColor("accentColor",Color.white);border=p.LinearColor("borderColor",new Color(.65f,.48f,.17f));
            red=p.LinearColor("redThread",new Color(.75f,.16f,.12f));green=p.LinearColor("greenThread",new Color(.18f,.42f,.15f));blue=p.LinearColor("blueThread",new Color(.16f,.3f,.65f));
            size=Mathf.Clamp(Math.Max(pattern?.width??256,pattern?.height??256),64,1024);
            var inside=new bool[size*size];distance=new float[inside.Length];arc=new float[inside.Length];
            float threshold=Mathf.Clamp01(p.Float("patternThreshold",.4f));
            for(int y=0;y<size;y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for(int x=0;x<size;x++)
                {
                    Vector2 uv=new((x+.5f)/size,(y+.5f)/size);
                    Color c=pattern?.GetPixelBilinear(uv.x,uv.y)??Color.white;
                    inside[y*size+x]=pattern!=null?c.a*(c.r*.2126f+c.g*.7152f+c.b*.0722f)>=threshold:
                        (uv-new Vector2(.5f,.5f)).magnitude<.36f;
                }
            }
            var nearest=new int[inside.Length];var next=new int[inside.Length];var boundary=new bool[inside.Length];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                int i=y*size+x;bool v=inside[i];
                boundary[i]=v && (x==0||y==0||x==size-1||y==size-1||inside[i-1]!=v||inside[i+1]!=v||inside[i-size]!=v||inside[i+size]!=v);
                nearest[i]=boundary[i]?i:-1;
            }
            // Trace boundary pixels to give border stitches a local arc coordinate, including holes.
            var seen=new bool[inside.Length];
            for(int start=0;start<seen.Length;start++)
            {
                if(!boundary[start]||seen[start])continue;
                int current=start;float travel=0;
                while(current>=0)
                {
                    seen[current]=true;arc[current]=travel;int candidate=-1;float best=3;
                    int x=current%size,y=current/size;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        int nx=x+dx,ny=y+dy;if(nx<0||ny<0||nx>=size||ny>=size)continue;
                        int j=ny*size+nx;float step=dx*dx+dy*dy;
                        if(boundary[j]&&!seen[j]&&step<best){candidate=j;best=step;}
                    }
                    travel+=Mathf.Sqrt(best)/size;current=candidate;
                }
            }
            // Jump flooding: bounded O(N log N), never a per-pixel search over the whole contour.
            for(int jump=Mathf.NextPowerOfTwo(size)/2;jump>0;jump/=2)
            {
                cancellation.ThrowIfCancellationRequested();
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    int i=y*size+x,best=nearest[i];float bestD=best<0?float.MaxValue:Squared(x,y,best,size);
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        int nx=x+dx*jump,ny=y+dy*jump;if(nx<0||ny<0||nx>=size||ny>=size)continue;
                        int n=nearest[ny*size+nx];if(n<0)continue;float d=Squared(x,y,n,size);
                        if(d<bestD){bestD=d;best=n;}
                    }
                    next[i]=best;
                }
                (nearest,next)=(next,nearest);
            }
            var traced=(float[])arc.Clone();
            for(int i=0;i<inside.Length;i++)
            {
                int n=nearest[i];distance[i]=(n<0?1:Mathf.Sqrt(Squared(i%size,i/size,n,size))/size)*(inside[i]?1:-1);
                arc[i]=n<0?0:traced[n];
            }
        }
        private static float Squared(int x,int y,int index,int size){float dx=x-index%size,dy=y-index/size;return dx*dx+dy*dy;}
        private float Read(float[] field,Vector2 uv)
        {
            float x=Mathf.Clamp(uv.x*size-.5f,0,size-1),y=Mathf.Clamp(uv.y*size-.5f,0,size-1);
            int ix=(int)x,iy=(int)y,jx=Math.Min(ix+1,size-1),jy=Math.Min(iy+1,size-1);
            return Mathf.Lerp(Mathf.Lerp(field[iy*size+ix],field[iy*size+jx],x-ix),Mathf.Lerp(field[jy*size+ix],field[jy*size+jx],x-ix),y-iy);
        }
        public Result Sample(Vector2 uv,float pixel)
        {
            uv=new Vector2(uv.x-Mathf.Floor(uv.x),uv.y-Mathf.Floor(uv.y));
            float d=Read(distance,uv),aa=Mathf.Max(pixel,1f/size),cover=EyeIrisMaterial.Smooth(-aa,aa,d);
            float rim=borderWidth>0 ? 1-EyeIrisMaterial.Smooth(borderWidth-aa,borderWidth+aa,d):0;
            Color weights=directions?.GetPixelBilinear(uv.x,uv.y)??Color.clear;
            float total=weights.r+weights.g+weights.b;float a=angle;Color tint=thread;
            if(total>.001f)
            {
                // Double-angle vector blending treats stitch orientation as an axis (0 == 180 degrees).
                Vector2 dir=new(Mathf.Cos(redAngle*2)*weights.r+Mathf.Cos(greenAngle*2)*weights.g+Mathf.Cos(blueAngle*2)*weights.b,
                    Mathf.Sin(redAngle*2)*weights.r+Mathf.Sin(greenAngle*2)*weights.g+Mathf.Sin(blueAngle*2)*weights.b);
                a=Mathf.Atan2(dir.y,dir.x)*.5f;tint=(red*weights.r+green*weights.g+blue*weights.b)/total;
            }
            float along=uv.x*Mathf.Cos(a)+uv.y*Mathf.Sin(a),across=-uv.x*Mathf.Sin(a)+uv.y*Mathf.Cos(a);
            int row=Mathf.FloorToInt(across*density),segment=Mathf.FloorToInt(along/length+row*.37f);
            float phase=across*density-Mathf.Floor(across*density);
            float crown=Mathf.Sqrt(Mathf.Clamp01(1-Mathf.Pow((phase-.5f)*2,2)));
            float endpoint=along/length+row*.37f;endpoint-=Mathf.Floor(endpoint);
            float join=style==2 ? EyeIrisMaterial.Smooth(0,.12f,Mathf.Min(endpoint,1-endpoint)) : 1;
            float unresolved=EyeIrisMaterial.Smooth(.35f,1,pixel*density);
            crown=Mathf.Lerp(crown*join,.7f,unresolved);
            float borderPhase=Read(arc,uv)*density;
            float borderThread=.5f+.5f*Mathf.Cos(borderPhase*Mathf.PI*2);
            borderThread=Mathf.Lerp(Mathf.Sqrt(borderThread),.7f,unresolved);
            float h=Mathf.Lerp(crown*.75f,borderThread,rim);
            float depth=EyeIrisMaterial.Hash(row,segment,seed);
            Color c=Color.Lerp(tint,border,rim)*(1-variation*.5f+depth*variation);
            // Lighter crowns sit above darker thread gaps; never bake a light direction into albedo.
            c*=.68f+h*.32f;
            return new Result(c,cover,height*(.2f+h*.8f),Mathf.Clamp01(roughness-h*.15f),1-(1-h)*.3f);
        }
    }
}
