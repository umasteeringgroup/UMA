using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>UV-space eye material. Geometry and randomness are shared by every output.</summary>
    public sealed class EyeIrisGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2, ITexturePaintDynamicChannelUsageV2
    {
        public TexturePaintPluginDescriptor Descriptor => Describe();
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) => TexturePaintChannelMask.None;
        public static TexturePaintPluginDescriptor Describe() => new()
        {
            id="com.uma.texturepaint.eye-iris", displayName="Eyes — Iris & Sclera", pluginVersion="1.0.0",
            description="Procedural iris fibers, pupils and sclera. UMA30_Eyes uses overlapping eye UVs centered at (0.5, 0.5): both eyes share the same texture. Paired UV mode is for separately unwrapped eyes only. Apply to the eye slot, never the cornea shell.",
            capabilities=TexturePaintPluginCapability.Generator|TexturePaintPluginCapability.LongRunning,
            declaredChannels=TexturePaintChannelMask.Albedo|TexturePaintChannelMask.Roughness|TexturePaintChannelMask.NormalControl,
            readChannels=TexturePaintChannelMask.None, supportedTargets=TexturePaintPluginTarget.All,
            parameters=new List<TexturePaintPluginParameterDefinition>
            {
                E("look","Color Preset",new[]{"Blue","Brown","Green","Hazel","Gray"},0),
                E("layout","Placement",new[]{"UMA 3 Eyes / shared centered UV","Custom centered UV","Custom paired UV (left / right halves)"},0),
                F("centerX","Center X",0,1,.5f),F("centerY","Center Y",0,1,.5f),
                F("radius","Iris Radius (local UV)",.02f,.48f,.22f),F("aspect","Iris Aspect",.25f,4,1),
                B("irisOnly","Transparent Outside Iris",false),
                E("pupilShape","Pupil Shape",new[]{"Round","Vertical slit","Horizontal slit"},0),
                F("pupil","Pupil Radius / Iris",.04f,.75f,.32f), F("slit","Slit Width",.05f,1,.22f),
                F("fibers","Fiber Detail",16,512,180), F("fiberContrast","Fiber Contrast",0,1,.58f),
                F("collarette","Inner Color Ring",0,1,.38f),F("limbal","Limbal Ring Width",0,.3f,.085f),
                F("limbalDarkness","Limbal Ring Darkness",0,1,.75f),
                C("tint","Iris Color Multiplier",Color.white), C("sclera","Sclera Color",new Color(.91f,.86f,.8f)),
                C("veinColor","Vein Color",new Color(.5f,.08f,.065f)), F("veins","Sclera Veins",0,1,.28f),
                F("veinWidth","Vein Width (local UV)",.0003f,.006f,.001f),
                F("roughness","Iris Roughness",0,1,.55f),F("scleraRoughness","Sclera Roughness",0,1,.4f),
                F("relief","Iris Relief",0,.15f,.018f),F("asymmetry","Paired Eye Variation",0,1,.12f),
                new() {id="seed",displayName="Seed",type=TexturePaintPluginParameterType.Integer,minimum=0,maximum=999999,defaultNumber=1731},
                B("outputAlbedo","Output Albedo",true),B("outputRoughness","Output Roughness",true),B("outputNormalControl","Output Normal Control",true)
            }
        };
        internal static TexturePaintPluginParameterDefinition F(string id,string name,float min,float max,float v)=>new(){id=id,displayName=name,type=TexturePaintPluginParameterType.Float,minimum=min,maximum=max,defaultNumber=v};
        internal static TexturePaintPluginParameterDefinition B(string id,string name,bool v)=>new(){id=id,displayName=name,type=TexturePaintPluginParameterType.Boolean,defaultBoolean=v};
        internal static TexturePaintPluginParameterDefinition C(string id,string name,Color v)=>new(){id=id,displayName=name,type=TexturePaintPluginParameterType.Color,defaultColor=v};
        internal static TexturePaintPluginParameterDefinition E(string id,string name,string[] options,int v)=>new(){id=id,displayName=name,type=TexturePaintPluginParameterType.Enum,enumOptions=options,minimum=0,maximum=options.Length-1,defaultNumber=v};
        public Task ExecuteAsync(TexturePaintCommandContextV2 c)
        {
            if(c==null)throw new ArgumentNullException(nameof(c));
            // Picker conversion and parameter snapshot happen on the main thread.
            var settings=new EyeIrisMaterial(c.parameters);
            var channels=new List<TexturePaintChannel>();
            foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})
                if(c.parameters.Boolean("output"+channel,true))channels.Add(channel);
            if(channels.Count==0 && c.target!=TexturePaintPluginTarget.LayerMask)throw new InvalidOperationException("Enable an eye output channel.");
            return Task.Run(()=>
            {
                int si=0;
                foreach(string id in c.source.surfaceIds)
                {
                    var groups=new Dictionary<Vector2Int,List<TexturePaintChannel>>();
                    bool mask=c.target==TexturePaintPluginTarget.LayerMask;
                    if(mask){var m=c.source.GetMask(id);if(m!=null)groups[new Vector2Int(m.width,m.height)]=new();}
                    else foreach(var channel in channels)
                    {
                        var info=c.source.GetChannelInfo(id,channel);if(info==null)continue;
                        var key=new Vector2Int(info.width,info.height);
                        if(!groups.TryGetValue(key,out var list))groups[key]=list=new();list.Add(channel);
                    }
                    foreach(var group in groups)
                    {
                        int w=group.Key.x,h=group.Key.y;
                        for(int y0=0;y0<h;y0+=64)
                        {
                            c.cancellationToken.ThrowIfCancellationRequested();int rows=Math.Min(64,h-y0);
                            var buffers=new Dictionary<TexturePaintChannel,Color32[]>();
                            foreach(var channel in group.Value)buffers[channel]=new Color32[w*rows];
                            var masks=mask?new Color32[w*rows]:null;
                            Parallel.For(0,rows,new ParallelOptions{CancellationToken=c.cancellationToken},y=>
                            {
                                for(int x=0;x<w;x++)
                                {
                                    var sample=settings.Sample(new Vector2((x+.5f)/w,(y0+y+.5f)/h),1f/Math.Min(w,h));
                                    int i=y*w+x;
                                    if(mask)masks[i]=new Color(sample.coverage,sample.coverage,sample.coverage,1);
                                    foreach(var channel in group.Value)buffers[channel][i]=sample.Channel(channel);
                                }
                            });
                            var rect=new RectInt(0,y0,w,rows);
                            if(mask)c.WriteMaskTileCompactOwned(id,rect,masks,TexturePaintPluginBlend.Replace);
                            else foreach(var pair in buffers)c.WriteTileCompactOwned(id,pair.Key,rect,pair.Value,
                                pair.Key==TexturePaintChannel.Albedo?TexturePaintPluginColorSpace.Linear:TexturePaintPluginColorSpace.Data,TexturePaintPluginBlend.Replace);
                            c.progress?.Report((si+(y0+rows)/(float)h)/Math.Max(1,c.source.surfaceIds.Count));
                        }
                    }
                    si++;
                }
            },c.cancellationToken);
        }
    }

    public sealed class EyeIrisMaterial
    {
        public readonly struct SampleResult
        {
            public readonly Color color;public readonly float coverage,roughness,height;
            public SampleResult(Color c,float a,float r,float h){color=c;coverage=a;roughness=r;height=h;}
            public Color Channel(TexturePaintChannel channel)=>channel==TexturePaintChannel.Albedo ? new Color(color.r,color.g,color.b,coverage) :
                channel==TexturePaintChannel.NormalControl ? new Color(height,height,height,coverage) : new Color(roughness,roughness,roughness,coverage);
        }
        private readonly int layout,shape,seed;
        private readonly bool irisOnly;
        private readonly Vector2 center;
        private readonly float radius,aspect,pupil,slit,fibers,contrast,ring,limbal,limbalDark,veins,veinWidth,rough,scleraRough,relief,asymmetry;
        private readonly Color iris,inner,sclera,vein;
        public EyeIrisMaterial(TexturePaintPluginParameterSet p)
        {
            float F(string id,float d,float a,float b){float v=p.Float(id,d);return float.IsFinite(v)?Mathf.Clamp(v,a,b):d;}
            layout=Mathf.Clamp(p.Integer("layout"),0,2);shape=Mathf.Clamp(p.Integer("pupilShape"),0,2);seed=p.Integer("seed",1731);
            center=layout==0?new Vector2(.5f,.5f):new Vector2(F("centerX",.5f,0,1),F("centerY",.5f,0,1));
            radius=F("radius",.22f,.02f,.48f);aspect=F("aspect",1,.25f,4);pupil=F("pupil",.32f,.04f,.75f);slit=F("slit",.22f,.05f,1);
            fibers=F("fibers",180,16,512);contrast=F("fiberContrast",.58f,0,1);ring=F("collarette",.38f,0,1);
            limbal=F("limbal",.085f,0,.3f);limbalDark=F("limbalDarkness",.75f,0,1);veins=F("veins",.28f,0,1);veinWidth=F("veinWidth",.001f,.0003f,.006f);
            rough=F("roughness",.55f,0,1);scleraRough=F("scleraRoughness",.4f,0,1);relief=F("relief",.018f,0,.15f);asymmetry=F("asymmetry",.12f,0,1);irisOnly=p.Boolean("irisOnly");
            Color[] colors={new(.19f,.39f,.53f),new(.29f,.15f,.065f),new(.27f,.38f,.19f),new(.4f,.32f,.13f),new(.38f,.4f,.41f)};
            iris=colors[Mathf.Clamp(p.Integer("look"),0,4)].linear*p.LinearColor("tint",Color.white);
            inner=new Color(.5f,.29f,.095f).linear*p.LinearColor("tint",Color.white);
            sclera=p.LinearColor("sclera",new Color(.91f,.86f,.8f));vein=p.LinearColor("veinColor",new Color(.5f,.08f,.065f));
        }
        public SampleResult Sample(Vector2 uv,float pixel)
        {
            float variation=0;
            if(layout==2){variation=uv.x>=.5f?asymmetry:0;uv.x=uv.x*2-Mathf.Floor(uv.x*2);pixel*=2;}
            Vector2 q=uv-center;q.x/=aspect;
            float r=q.magnitude/radius, angle=Mathf.Atan2(q.y,q.x), aa=Mathf.Max(.00001f,pixel/radius);
            // Periodic circular coordinates avoid a seam at atan2's branch cut.
            float Warp(float frequency,float radial,int stream)=>Noise(new Vector2(Mathf.Cos(angle)*frequency+radial,Mathf.Sin(angle)*frequency+radial*.37f),seed+stream);
            float broad=Warp(7,r*2,31);
            float Radial(float count,int stream)
            {
                int n=Mathf.RoundToInt(count);
                float phase=(angle+Mathf.PI+.018f*Mathf.Sin(r*13+broad*4))/(Mathf.PI*2)*n;
                int cell=Mathf.FloorToInt(phase);float t=phase-cell;t=t*t*(3-2*t);
                int a=(cell%n+n)%n,b=(a+1)%n;
                return Mathf.Lerp(Hash(a,stream,seed),Hash(b,stream,seed),t);
            }
            float fine=Radial(fibers,67)*.7f+Radial(fibers*.43f,91)*.3f;
            fine=Mathf.Lerp(fine,.5f,Smooth(.4f,1.2f,aa*fibers/(6.283185f*Mathf.Max(r,.1f))));
            float fiber=(fine-.5f)*contrast;
            float rings=.5f+.5f*Mathf.Sin(r*36+broad*6+fine*2);
            Color color=Color.Lerp(iris,inner,ring*(1-Smooth(pupil+.08f,pupil+.4f,r+broad*.055f)));
            float crypt=Mathf.Pow(1-fine,3)*Smooth(pupil+.04f,pupil+.18f,r)*(1-Smooth(.55f,.85f,r));
            color*=Mathf.Max(.1f,1+fiber*2.4f+(rings-.5f)*.06f-crypt*.55f+variation*(broad-.5f));
            float outer=limbal>0?Smooth(1-limbal,1,r)*limbalDark:0;color*=1-outer;
            Vector2 pq=q/radius;if(shape==1)pq.x/=slit;else if(shape==2)pq.y/=slit;
            float pupilMask=1-Smooth(pupil-aa,pupil+aa,pq.magnitude);
            color=Color.Lerp(color,new Color(.001f,.001f,.001f),pupilMask);
            float irisMask=1-Smooth(1-aa,1+aa,r);
            float vessel=0;
            if(veins>0 && r>.95f)
            {
                float theta=(angle+Mathf.PI)/(Mathf.PI*2)*17;
                for(int k=-1;k<=1;k++)
                {
                    int cell=Mathf.FloorToInt(theta)+k;int key=(cell%17+17)%17;
                    float end=1.05f+Hash(key,7,seed)*.65f;
                    float wave=Mathf.Sin(r*6+Hash(key,1,seed)*6)*.16f+Mathf.Sin(r*15+key)*.045f;
                    float d=Mathf.Abs(theta-(cell+.5f+wave));
                    float branch=Mathf.Abs(theta-(cell+.5f+wave+Mathf.Max(0,r-1.7f)*.6f));
                    float width=veinWidth/radius/Mathf.Max(r,.1f)*17/6.283185f;
                    float angularAA=aa/Mathf.Max(r,.1f)*17/6.283185f;
                    float line=1-Smooth(width,width+angularAA,Mathf.Min(d,branch));
                    vessel=Mathf.Max(vessel,line*Smooth(end,end+.3f,r));
                }
            }
            Color white=Color.Lerp(sclera,vein,vessel*veins);
            float height=.5f+relief*(fiber+(rings-.5f)*.2f)*irisMask*(1-pupilMask)*Smooth(pupil,pupil+.1f,r)*(1-Smooth(.85f,1,r));
            return new SampleResult(Color.Lerp(white,color,irisMask),irisOnly?irisMask:1,Mathf.Lerp(scleraRough,rough,irisMask),Mathf.Clamp01(height));
        }
        internal static float Smooth(float a,float b,float v){float t=Mathf.Clamp01((v-a)/Mathf.Max(1e-7f,b-a));return t*t*(3-2*t);}
        internal static float Hash(int x,int y,int seed){unchecked{uint h=(uint)(x*374761393+y*668265263+seed*1442695041);h=(h^(h>>13))*1274126177;return (h^(h>>16))/4294967295f;}}
        internal static float Noise(Vector2 p,int seed){int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y);float a=p.x-x,b=p.y-y;a=a*a*(3-2*a);b=b*b*(3-2*b);return Mathf.Lerp(Mathf.Lerp(Hash(x,y,seed),Hash(x+1,y,seed),a),Mathf.Lerp(Hash(x,y+1,seed),Hash(x+1,y+1,seed),a),b);}
    }
}
