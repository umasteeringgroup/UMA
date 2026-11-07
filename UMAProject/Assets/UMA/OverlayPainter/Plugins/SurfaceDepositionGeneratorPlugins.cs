using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    public abstract class SurfaceDepositionPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2, ITexturePaintDynamicMeshMapUsageV2
    {
        protected abstract bool Splash { get; }
        public TexturePaintPluginDescriptor Descriptor => SurfaceDepositionEngine.Describe(Splash);
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet p) => TexturePaintChannelMask.None;
        public TexturePaintMeshMapMask ResolveMeshMaps(TexturePaintPluginParameterSet p) => p.Integer("mapping",0)>0
            ? TexturePaintMeshMapMask.WorldPosition|TexturePaintMeshMapMask.SurfaceId : TexturePaintMeshMapMask.None;
        public Task ExecuteAsync(TexturePaintCommandContextV2 c) => SurfaceDepositionEngine.Execute(c,Splash);
    }
    public sealed class SurfaceResidueGeneratorPlugin : SurfaceDepositionPlugin { protected override bool Splash => false; }
    public sealed class SplashDepositionGeneratorPlugin : SurfaceDepositionPlugin { protected override bool Splash => true; }

    internal static class SurfaceDepositionEngine
    {
        internal static readonly TexturePaintChannel[] Channels={TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.Metallic,TexturePaintChannel.NormalControl};
        internal static TexturePaintPluginDescriptor Describe(bool splash)
        {
            var p=new List<TexturePaintPluginParameterDefinition>
            {
                EyeIrisGeneratorPlugin.E("style",splash?"Material Preset":"Residue Type",splash?new[]{"Blood","Mud","Paint","Oil"}:new[]{"Oily fingerprints","Wiped smudges","Dried water rings","Salt deposits","Adhesive residue"},0),
                EyeIrisGeneratorPlugin.E("mapping","Mapping",new[]{"UV patch","World XY (gravity -Y)","World ZY (gravity -Y)","World XZ (planar runoff)"},0),
                EyeIrisGeneratorPlugin.F("worldSpan","World Patch Span (meters)",.01f,10,.25f),
                EyeIrisGeneratorPlugin.F("offsetX","Patch Offset X",-20,20,0),EyeIrisGeneratorPlugin.F("offsetY","Patch Offset Y",-20,20,0),
                EyeIrisGeneratorPlugin.F("centerX","Center X / Patch",0,1,.5f),EyeIrisGeneratorPlugin.F("centerY","Center Y / Patch",0,1,.6f),
                new(){id="count",displayName=splash?"Impact Count":"Mark Count",type=TexturePaintPluginParameterType.Integer,minimum=1,maximum=64,defaultNumber=splash?1:3},
                EyeIrisGeneratorPlugin.F("scatter","Placement Scatter",0,.5f,splash?0:.27f),
                EyeIrisGeneratorPlugin.F("size","Mark Radius / Patch",.015f,.4f,splash?.17f:.15f),
                EyeIrisGeneratorPlugin.F("rotation",splash?"Impact Direction (degrees)":"Mark Rotation (degrees)",-180,180,0),
                EyeIrisGeneratorPlugin.F("variation","Shape Variation",0,1,.35f),
                EyeIrisGeneratorPlugin.F("amount","Coverage",0,1,splash?1:.72f),
                new(){id="seed",displayName="Seed",type=TexturePaintPluginParameterType.Integer,minimum=0,maximum=999999,defaultNumber=3721},
                new(){id="controlMask",displayName="Placement Mask",description="Optional white-on-black mask; alpha is respected. Layer masks also remain available.",type=TexturePaintPluginParameterType.Texture}
            };
            if(splash)p.AddRange(new[]{
                EyeIrisGeneratorPlugin.F("impact","Impact Stretch",0,1,.35f),
                EyeIrisGeneratorPlugin.F("droplets","Satellite Droplets",0,1,.6f),
                EyeIrisGeneratorPlugin.F("smear","Smear Length / Radius",0,2,.15f),
                EyeIrisGeneratorPlugin.F("smearAngle","Smear Direction (degrees)",-180,180,15),
                EyeIrisGeneratorPlugin.F("gravity","Drip Length / Radius",0,3,1.15f),
                EyeIrisGeneratorPlugin.F("gravityAngle","Runoff Direction (0 = down)",-180,180,0),
                EyeIrisGeneratorPlugin.F("drying","Drying",0,1,.15f),
                EyeIrisGeneratorPlugin.F("dryRoughness","Dry Roughness",0,1,.73f)});
            else p.AddRange(new[]{
                EyeIrisGeneratorPlugin.E("fingerprint","Fingerprint Pattern",new[]{"Whorl","Loop","Arch"},1),
                EyeIrisGeneratorPlugin.F("ridgeCount","Fingerprint Ridge Count",10,65,32),
                EyeIrisGeneratorPlugin.F("breakup","Contact Breakup",0,1,.4f),
                EyeIrisGeneratorPlugin.F("wipe","Wipe Stretch",0,2,.8f)});
            p.Add(EyeIrisGeneratorPlugin.C("tint",splash?"Material Color Multiplier":"Residue Color",splash?Color.white:new Color(.76f,.72f,.64f)));
            p.Add(EyeIrisGeneratorPlugin.F("roughness",splash?"Wet Roughness":"Residue Roughness",0,1,splash?.14f:.24f));
            p.Add(EyeIrisGeneratorPlugin.F("relief","Normal Control Relief",0,.3f,splash?.008f:.004f));
            foreach(var c in Channels)p.Add(EyeIrisGeneratorPlugin.B("output"+c,"Output "+c,c==TexturePaintChannel.Roughness||splash));
            return new TexturePaintPluginDescriptor
            {
                id="com.uma.texturepaint."+(splash?"splash-deposition":"surface-residue"),
                displayName=splash?"Splashes, Smears & Drips":"Fingerprints, Smudges & Surface Residue",pluginVersion="1.0.0",
                description=splash?"Structured impact pools, satellite drops, directional smears and runoff, with wet centers and dry rims. World XY/ZY use real world -Y, independent of UV orientation. XZ runoff is planar, not gravity simulation. Masks control placement.":
                    "Roughness-first surface marks: friction ridges, wiped films, evaporation rings, salt crystals and torn adhesive. Enable optional color and relief. Mark size uses the chosen patch metric.",
                capabilities=TexturePaintPluginCapability.Generator|TexturePaintPluginCapability.LongRunning|TexturePaintPluginCapability.ReadsMeshMaps,
                declaredChannels=TexturePaintChannelMask.Albedo|TexturePaintChannelMask.Roughness|TexturePaintChannelMask.Metallic|TexturePaintChannelMask.NormalControl,
                readChannels=TexturePaintChannelMask.None,supportedTargets=TexturePaintPluginTarget.All,parameters=p
            };
        }
        internal static Task Execute(TexturePaintCommandContextV2 c,bool splash)
        {
            var p=c.parameters.Clone();var material=new SurfaceDepositionMaterial(p,splash);var mask=c.GetTextureParameter("controlMask");
            int mapping=p.Integer("mapping");float span=SurfaceDepositionMaterial.F(p,"worldSpan",.25f,.01f,10);
            Vector2 offset=new(SurfaceDepositionMaterial.F(p,"offsetX",0,-20,20),SurfaceDepositionMaterial.F(p,"offsetY",0,-20,20));
            var enabled=new List<TexturePaintChannel>();foreach(var channel in Channels)if(p.Boolean("output"+channel,channel==TexturePaintChannel.Roughness||splash))enabled.Add(channel);
            if(enabled.Count==0 && c.target!=TexturePaintPluginTarget.LayerMask)throw new InvalidOperationException("Enable at least one output channel.");
            return Task.Run(()=>
            {
                int surfaceIndex=0;
                foreach(string id in c.source.surfaceIds)
                {
                    var position=c.GetMeshMap(id,TexturePaintMeshMap.WorldPosition);var ids=c.GetMeshMap(id,TexturePaintMeshMap.SurfaceId);
                    if(mapping>0 && (position==null||ids==null))throw new InvalidOperationException("World mapping requires baked World Position and Surface ID maps.");
                    bool maskTarget=c.target==TexturePaintPluginTarget.LayerMask;
                    var groups=new Dictionary<Vector2Int,List<TexturePaintChannel>>();
                    if(maskTarget){var m=c.source.GetMask(id);if(m!=null)groups[new(m.width,m.height)]=new();}
                    else foreach(var channel in enabled){var info=c.source.GetChannelInfo(id,channel);if(info==null)continue;var size=new Vector2Int(info.width,info.height);if(!groups.TryGetValue(size,out var list))groups[size]=list=new();list.Add(channel);}
                    foreach(var group in groups)
                    {
                        int width=group.Key.x,height=group.Key.y;
                        for(int y0=0;y0<height;y0+=48)
                        {
                            c.cancellationToken.ThrowIfCancellationRequested();int rows=Math.Min(48,height-y0);
                            var buffers=new Dictionary<TexturePaintChannel,Color32[]>();Color[] relief=null;
                            foreach(var channel in group.Value)if(channel==TexturePaintChannel.NormalControl)relief=new Color[width*rows];else buffers[channel]=new Color32[width*rows];
                            var maskPixels=maskTarget?new Color32[width*rows]:null;
                            Parallel.For(0,rows,new ParallelOptions{CancellationToken=c.cancellationToken},row=>
                            {
                                for(int x=0;x<width;x++)
                                {
                                    Vector2 uv=new((x+.5f)/width,(y0+row+.5f)/height),q=uv-offset;
                                    float aa=1f/Math.Min(width,height),coverage=1;
                                    if(mapping>0)
                                    {
                                        var pos=position.GetPixelBilinear(uv.x,uv.y);coverage=ids.GetPixelBilinear(uv.x,uv.y).a>=.5f?1:0;
                                        Vector2 Plane(Color v)=>mapping==1?new(v.r,v.g):mapping==2?new(v.b,v.g):new(v.r,v.b);
                                        q=Plane(pos)/span-offset;
                                        // Filter from the world footprint. Limit seam discontinuities instead of blurring an entire island.
                                        var dx=Plane(position.GetPixelBilinear(uv.x+1f/width,uv.y))-Plane(pos);
                                        var dy=Plane(position.GetPixelBilinear(uv.x,uv.y+1f/height))-Plane(pos);
                                        aa=Mathf.Clamp(Mathf.Max(dx.magnitude,dy.magnitude)/span,.00001f,.02f);
                                    }
                                    if(mask!=null){var m=mask.GetPixelBilinear(uv.x,uv.y);coverage*=m.a*(m.r*.2126f+m.g*.7152f+m.b*.0722f);}
                                    var sample=material.Sample(q,aa);sample.coverage*=coverage;int index=row*width+x;
                                    if(maskTarget)maskPixels[index]=new Color(sample.coverage,sample.coverage,sample.coverage,1);
                                    else
                                    {
                                        foreach(var pair in buffers)pair.Value[index]=sample.Channel(pair.Key);
                                        if(relief!=null)relief[index]=sample.Channel(TexturePaintChannel.NormalControl);
                                    }
                                }
                            });
                            var rect=new RectInt(0,y0,width,rows);
                            if(maskTarget)c.WriteMaskTileCompactOwned(id,rect,maskPixels,TexturePaintPluginBlend.Replace);
                            else
                            {
                                foreach(var pair in buffers)c.WriteTileCompactOwned(id,pair.Key,rect,pair.Value,pair.Key==TexturePaintChannel.Albedo?TexturePaintPluginColorSpace.Linear:TexturePaintPluginColorSpace.Data,TexturePaintPluginBlend.Replace);
                                if(relief!=null)c.WriteTile(id,TexturePaintChannel.NormalControl,rect,relief,TexturePaintPluginColorSpace.Data,TexturePaintPluginBlend.Replace);
                            }
                            c.progress?.Report((surfaceIndex+(y0+rows)/(float)height)/Math.Max(1,c.source.surfaceIds.Count));
                        }
                    }
                    surfaceIndex++;
                }
            },c.cancellationToken);
        }
    }

    /// <summary>Resolution-independent marks, binned spatially once. All channels use one coverage/height sample.</summary>
    public sealed class SurfaceDepositionMaterial
    {
        public struct SampleResult
        {
            public Color color;public float coverage,height,roughness;
            public Color Channel(TexturePaintChannel c)=>c==TexturePaintChannel.Albedo?new(color.r,color.g,color.b,coverage):
                c==TexturePaintChannel.NormalControl?new(.5f+height,.5f+height,.5f+height,coverage):
                c==TexturePaintChannel.Metallic?new(0,0,0,coverage):new(roughness,roughness,roughness,coverage);
        }
        private readonly struct Mark
        {
            public readonly Vector2 center;public readonly float size,angle;public readonly int id;
            public readonly Vector3[] drops,drips,rays;
            public Mark(Vector2 c,float s,float a,int i,Vector3[] drops,Vector3[] drips,Vector3[] rays)
            {center=c;size=s;angle=a;id=i;this.drops=drops;this.drips=drips;this.rays=rays;}
        }
        private readonly List<Mark>[] bins=new List<Mark>[256];
        private readonly bool splash;private readonly int style,seed,fingerprint;
        private readonly float variation,amount,ridges,breakup,wipe,impact,droplets,smear,smearAngle,gravity,gravityAngle,drying,dryRoughness,roughness,relief;
        private readonly Color tint,wetColor,dryColor;
        public static float F(TexturePaintPluginParameterSet p,string id,float fallback,float min,float max){float v=p.Float(id,fallback);return float.IsFinite(v)?Mathf.Clamp(v,min,max):fallback;}
        public SurfaceDepositionMaterial(TexturePaintPluginParameterSet p,bool splash)
        {
            this.splash=splash;style=Mathf.Clamp(p.Integer("style"),0,splash?3:4);seed=p.Integer("seed",3721);fingerprint=Mathf.Clamp(p.Integer("fingerprint",1),0,2);
            variation=F(p,"variation",.35f,0,1);amount=F(p,"amount",1,0,1);ridges=F(p,"ridgeCount",32,10,65);breakup=F(p,"breakup",.4f,0,1);wipe=F(p,"wipe",.8f,0,2);
            impact=F(p,"impact",.35f,0,1);droplets=F(p,"droplets",.6f,0,1);smear=F(p,"smear",.15f,0,2);smearAngle=F(p,"smearAngle",15,-180,180)*Mathf.Deg2Rad;
            gravity=F(p,"gravity",1.15f,0,3);gravityAngle=F(p,"gravityAngle",0,-180,180)*Mathf.Deg2Rad;
            drying=F(p,"drying",.15f,0,1);dryRoughness=F(p,"dryRoughness",.73f,0,1);roughness=F(p,"roughness",.14f,0,1);relief=F(p,"relief",.008f,0,.3f);
            tint=p.LinearColor("tint",Color.white);
            Color[] wet={new(.3f,.012f,.008f),new(.16f,.085f,.032f),new(.055f,.22f,.52f),new(.03f,.024f,.014f)};
            Color[] dried={new(.085f,.009f,.006f),new(.36f,.24f,.12f),new(.095f,.29f,.6f),new(.08f,.06f,.025f)};
            wetColor=wet[Math.Min(style,3)].linear;dryColor=dried[Math.Min(style,3)].linear;
            int count=Mathf.Clamp(p.Integer("count",splash?1:3),1,64);float scatter=F(p,"scatter",splash?0:.27f,0,.5f),size=F(p,"size",.15f,.015f,.4f),angle=F(p,"rotation",0,-180,180)*Mathf.Deg2Rad;
            Vector2 center=new(F(p,"centerX",.5f,0,1),F(p,"centerY",.6f,0,1));
            for(int i=0;i<count;i++)
            {
                float a=H(i,1)*Mathf.PI*2,r=Mathf.Sqrt(H(i,2))*scatter;
                Vector3[] dropData=null,dripData=null,rayData=null;
                if(splash)
                {
                    var drops=new List<Vector3>();
                    for(int k=0;k<28;k++)if(H(i*37+k,131)<=droplets)
                    {
                        float theta=H(i*37+k,132)*Mathf.PI*2,distance=.82f+Mathf.Pow(H(i*37+k,133),.7f)*1.5f;
                        drops.Add(new Vector3(Mathf.Cos(theta)*distance*(1+impact),Mathf.Sin(theta)*distance,.02f+Mathf.Pow(H(i*37+k,134),3)*.14f));
                    }
                    dropData=drops.ToArray();dripData=new Vector3[5];rayData=new Vector3[13];
                    for(int k=0;k<5;k++)dripData[k]=new Vector3((H(i*7+k,161)-.5f)*.95f,gravity*(.25f+H(i*7+k,162)*.75f),.022f+H(i*7+k,163)*.025f);
                    for(int k=0;k<13;k++)rayData[k]=new Vector3(.3f+H(i*17+k,124)*.4f,.08f+H(i*17+k,125)*.16f,.12f+H(i*17+k,126)*.48f);
                }
                var mark=new Mark(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,size*(1+(H(i,3)-.5f)*variation),angle+(H(i,4)-.5f)*variation*1.8f,i,dropData,dripData,rayData);
                // Include stretched satellite drops and long wipes in every intersecting bin.
                // An underestimated bound produces straight cutoffs at acceleration-cell edges.
                float bound=mark.size*(splash?Mathf.Max(5.2f,gravity+1.5f,smear+2):Mathf.Max(2.5f,1.2f+wipe));
                int xmin=Mathf.Clamp(Mathf.FloorToInt((mark.center.x-bound)*16),0,15),xmax=Mathf.Clamp(Mathf.FloorToInt((mark.center.x+bound)*16),0,15);
                int ymin=Mathf.Clamp(Mathf.FloorToInt((mark.center.y-bound)*16),0,15),ymax=Mathf.Clamp(Mathf.FloorToInt((mark.center.y+bound)*16),0,15);
                for(int y=ymin;y<=ymax;y++)for(int x=xmin;x<=xmax;x++)(bins[y*16+x]??=new()).Add(mark);
            }
        }
        private float H(int i,int stream)=>EyeIrisMaterial.Hash(i,stream,seed);
        private float N(Vector2 p,int stream)=>EyeIrisMaterial.Noise(p,seed+stream);
        private static float S(float a,float b,float x)=>EyeIrisMaterial.Smooth(a,b,x);
        private static Vector2 Rotate(Vector2 p,float a)=>new(p.x*Mathf.Cos(a)+p.y*Mathf.Sin(a),-p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));
        private static float Segment(Vector2 p,Vector2 a,Vector2 b){var d=b-a;return (p-a-d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(d.sqrMagnitude,1e-8f))).magnitude;}
        public SampleResult Sample(Vector2 uv,float pixel)
        {
            var result=new SampleResult{roughness=roughness};if(uv.x<0||uv.x>=1||uv.y<0||uv.y>=1)return result;
            var list=bins[(int)(uv.y*16)*16+(int)(uv.x*16)];if(list==null)return result;
            foreach(var mark in list)
            {
                Vector2 delta=(uv-mark.center)/mark.size,q=Rotate(delta,mark.angle);float aa=Mathf.Max(pixel/mark.size,.0001f);
                var sample=splash?Splash(q,delta,aa,mark):Residue(q,aa,mark.id);
                float a=sample.coverage,combined=a+result.coverage*(1-a);
                if(combined>0){result.color=(sample.color*a+result.color*(result.coverage*(1-a)))/combined;result.roughness=(sample.roughness*a+result.roughness*result.coverage*(1-a))/combined;}
                result.coverage=combined;result.height=Mathf.Max(result.height,sample.height);
            }
            result.coverage=Mathf.Clamp01(result.coverage*amount);return result;
        }
        private SampleResult Residue(Vector2 q,float aa,int id)
        {
            q+=new Vector2(N(q*2+Vector2.one*id,5)-.5f,N(q*2+Vector2.one*id,9)-.5f)*.12f*variation;
            float noise=N(q*18+Vector2.one*id,29),broad=N(q*3+Vector2.one*id,32),cover=0,h=0;
            if(style==0)
            {
                Vector2 f=new(q.x,q.y*.72f);float boundary=1-S(.8f,1.05f,f.magnitude);
                float field=f.magnitude;
                if(fingerprint==1)field=Mathf.Sqrt(f.x*f.x+Mathf.Pow(f.y-.24f,2))+.18f*Mathf.Atan2(f.y-.24f,Mathf.Abs(f.x)+.18f);
                if(fingerprint==2)field=(f.y+.6f-f.x*f.x*.8f)*.7f;
                float phase=field*ridges+(N(q*4,41)-.5f)*.3f;
                float ridge=.5f+.5f*Mathf.Cos(phase*Mathf.PI*2);
                float filtered=Mathf.Lerp(S(.35f,.65f,ridge),.5f,S(.3f,.9f,aa*ridges));
                float pressure=S(breakup*.65f,breakup*.65f+.35f,noise*.3f+broad*.7f);
                float pores=1-S(.76f,.9f,N(q*150+Vector2.one*id,49))*.55f;
                cover=boundary*filtered*pressure*pores;h=cover*.3f;
            }
            else if(style==1)
            {
                Vector2 f=new(q.x/(1+wipe),q.y);float edge=1-S(.6f,1,f.magnitude);
                float phase=(q.y+.12f*Mathf.Sin(q.x*2))*85;
                float lines=Mathf.Lerp(.5f+.5f*Mathf.Cos(phase),.5f,S(.3f,1,aa*14));
                cover=edge*(.2f+lines*.8f)*S(breakup*.5f,.8f,broad);h=cover*.2f;
            }
            else if(style==2||style==3)
            {
                float angle=Mathf.Atan2(q.y,q.x),radius=.8f+(N(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*4+Vector2.one*id,61)-.5f)*.15f;
                float d=Mathf.Abs(q.magnitude-radius+(N(q*90,63)-.5f)*.012f);
                float ring=1-S(.009f,.025f+aa,d);
                ring=Mathf.Max(ring,(1-S(.005f,.012f+aa,Mathf.Abs(q.magnitude-radius+.045f)))*.25f);
                float crystals=S(.55f,.85f,N(q*120+Vector2.one*id,65))* (1-S(.015f,.075f+aa,d));
                cover=(style==2?ring*(.25f+broad*.6f)+crystals*.2f:crystals*.85f+ring*.3f)*(1-breakup*S(.5f,.9f,broad));
                h=style==2?ring*.12f:crystals;
            }
            else
            {
                float edge=Mathf.Max(Mathf.Abs(q.x)-.68f,Mathf.Abs(q.y)-.82f)+(N(q*8,72)-.5f)*.14f;
                float patch=1-S(-aa,aa,edge),peel=S(.3f,.65f,broad+q.x*.15f);
                cover=patch*peel*(.35f+noise*.65f);h=cover*(.25f+S(-.08f,0,edge)*.6f);
            }
            return new SampleResult{color=tint,coverage=Mathf.Clamp01(cover),height=h*relief,roughness=roughness};
        }
        private SampleResult Splash(Vector2 q,Vector2 gravityQ,float aa,Mark mark)
        {
            int id=mark.id;
            float angle=Mathf.Atan2(q.y,q.x),n=N(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*5+Vector2.one*id,121);
            Vector2 impactQ=new(q.x/(1+impact*1.3f),q.y);
            float radius=.62f+(n-.5f)*variation*.4f;
            float d=impactQ.magnitude-radius;
            // Scalloped impact crown with continuous tendrils, not a collection of disconnected stamps.
            float rayPhase=(angle+Mathf.PI)/(Mathf.PI*2)*13;int ray=Mathf.FloorToInt(rayPhase)%13;
            var rayData=mark.rays[ray];float local=rayPhase-Mathf.Floor(rayPhase)-rayData.x;
            float rayWidth=rayData.y;
            float rayShape=Mathf.Exp(-local*local/(rayWidth*rayWidth));
            d-=rayShape*droplets*rayData.z;
            float pool=1-S(-aa,aa,d);
            float height=S(0,.12f,-d)*(.16f+.13f*Mathf.Sqrt(Mathf.Clamp01(1-impactQ.sqrMagnitude)));
            float satellite=0;
            foreach(var drop in mark.drops)
            {
                Vector2 center=new(drop.x,drop.y);float dropRadius=drop.z;
                float dd=(q-center).magnitude;
                satellite=Mathf.Max(satellite,(1-S(dropRadius-aa,dropRadius+aa,dd)));
                height=Mathf.Max(height,Mathf.Sqrt(Mathf.Clamp01(1-dd*dd/(dropRadius*dropRadius)))*.45f);
            }
            Vector2 sm=Rotate(gravityQ,smearAngle);float smearShape=0;
            if(smear>0)
            {
                float distance=Segment(sm,Vector2.zero,new Vector2(smear,0));
                float streak=.36f*(1-S(0,smear+.4f,sm.x));
                float breakup=N(new Vector2(sm.x*3,sm.y*80),151);
                smearShape=(1-S(streak-aa,streak+aa,distance))*S(.12f,.55f,breakup);
                height=Mathf.Max(height,smearShape*.35f);
            }
            Vector2 g=Rotate(gravityQ,gravityAngle);float drips=0;
            if(gravity>0)for(int k=0;k<5;k++)
            {
                var drip=mark.drips[k];float x=drip.x,len=drip.y;
                float y=Mathf.Clamp(-g.y,0,len),width=drip.z;
                float bend=x+.035f*Mathf.Sin(y*5+H(k,164)*6);
                float dr=Segment(g,new Vector2(x,0),new Vector2(bend,-len));
                float body=1-S(width-aa,width+aa,dr);
                float bulb=1-S(width*1.6f-aa,width*1.6f+aa,(g-new Vector2(bend,-len)).magnitude);
                drips=Mathf.Max(drips,Mathf.Max(body,bulb));
                float rounded=Mathf.Sqrt(Mathf.Clamp01(1-dr*dr/(width*width)));
                height=Mathf.Max(height,Mathf.Max(rounded,bulb*.75f)*.55f);
            }
            float coverage=Mathf.Max(Mathf.Max(pool,satellite),Mathf.Max(drips,smearShape));
            float dry=Mathf.Clamp01(drying+(1-height)*.42f*drying);
            Color color=Color.Lerp(wetColor,dryColor,dry)*tint;
            if(style==1)color*=.82f+N(q*45,181)*.3f;
            float rim=pool*(1-S(0,.08f,Mathf.Abs(d)))*dry;
            return new SampleResult{color=color*(1-rim*.22f),coverage=coverage,
                height=relief*Mathf.Clamp01(height*(1-dry*.65f)+rim*.15f),roughness=Mathf.Lerp(roughness,dryRoughness,dry)};
        }
    }
}
