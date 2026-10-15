using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    public sealed class MakeupFacePaintGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        public TexturePaintPluginDescriptor Descriptor => Describe();
        public static TexturePaintPluginDescriptor Describe()
        {
            var p=new List<TexturePaintPluginParameterDefinition>
            {
                Header("placement","Face Placement","Uses posed UMA/humanoid eye and lip landmarks. Select the skin paint target. Paint masks refine the boundary for each face."),
                EyeIrisGeneratorPlugin.E("style","Makeup",new[]{"Lipstick","Eyeliner","Eyeshadow","Blush","War paint","Masquerade","Festival stripes"},0),
                EyeIrisGeneratorPlugin.E("side","Symmetry / Side",new[]{"Both (mirrored)","Character left","Character right"},0),
                EyeIrisGeneratorPlugin.F("offsetX","Outward Offset / Eye Distance",-.5f,.5f,0),
                EyeIrisGeneratorPlugin.F("offsetY","Vertical Offset / Eye Distance",-.5f,.5f,0),
                EyeIrisGeneratorPlugin.F("width","Boundary Width",.25f,2,1),EyeIrisGeneratorPlugin.F("height","Boundary Height",.25f,2,1),
                EyeIrisGeneratorPlugin.F("rotation","Outward Tilt (degrees)",-45,45,0),
                EyeIrisGeneratorPlugin.F("feather","Boundary Softness",.001f,1,.12f),
                EyeIrisGeneratorPlugin.F("asymmetry","Bounded Asymmetry",0,.3f,0),
                new(){id="controlMask",displayName="Refinement Mask",description="Optional white applies, black protects, multiplied by alpha. Use to refine lip and eyelid boundaries on a particular face.",type=TexturePaintPluginParameterType.Texture},
                Header("shape","Shape","Offsets and sizes are relative to the distance between the eyes, not UV coordinates."),
                EyeIrisGeneratorPlugin.F("lipBow","Cupid Bow",0,1,.55f),EyeIrisGeneratorPlugin.F("lipGap","Mouth Opening Protection",0,.15f,0),
                EyeIrisGeneratorPlugin.F("linerWidth","Eyeliner Thickness / Eye Distance",.005f,.1f,.025f),
                EyeIrisGeneratorPlugin.F("wing","Eyeliner Wing Length",0,.4f,.12f),
                EyeIrisGeneratorPlugin.F("lowerLiner","Lower Eyeliner",0,1,.15f),
                EyeIrisGeneratorPlugin.F("crease","Eyelid Arc / Eye Distance",.04f,.3f,.12f),
                EyeIrisGeneratorPlugin.F("shadowReach","Eyeshadow Reach",.05f,.5f,.19f),
                Header("appearance","Pigment & Finish","Pigment does not replace the skin's normal map. Optional micro-relief goes only to Normal Control."),
                EyeIrisGeneratorPlugin.C("color","Pigment",new Color(.46f,.035f,.08f,1)),
                EyeIrisGeneratorPlugin.C("edgeColor","Outer / Secondary Pigment",new Color(.2f,.018f,.03f,1)),
                EyeIrisGeneratorPlugin.F("gradient","Pigment Gradient",0,1,.2f),EyeIrisGeneratorPlugin.F("opacity","Opacity",0,1,.85f),
                EyeIrisGeneratorPlugin.E("finish","Finish",new[]{"Matte","Satin","Gloss","Custom roughness"},1),
                EyeIrisGeneratorPlugin.F("roughness","Custom Roughness",0,1,.4f),
                EyeIrisGeneratorPlugin.F("pigmentVariation","Pigment Variation",0,1,.12f),
                EyeIrisGeneratorPlugin.F("grain","Pigment Grain / Eye Distance",30,600,210),
                EyeIrisGeneratorPlugin.F("relief","Micro Relief",0,.025f,.001f),
                new(){id="seed",displayName="Seed",type=TexturePaintPluginParameterType.Integer,minimum=0,maximum=999999,defaultNumber=5183},
                EyeIrisGeneratorPlugin.B("outputAlbedo","Output Albedo",true),EyeIrisGeneratorPlugin.B("outputRoughness","Output Roughness",true),
                EyeIrisGeneratorPlugin.B("outputNormalControl","Output Normal Control",false)
            };
            return new TexturePaintPluginDescriptor{id="com.uma.texturepaint.makeup-face-paint",displayName="Face — Makeup & Face Paint",pluginVersion="1.0.0",
                description="Anatomical lipstick, liner, shadow, blush and face paint. Posed bones locate the features independently of UV layout. Shared UVs cannot encode different sides. Use a separate layer per cosmetic; presets supply finished stacks.",
                capabilities=TexturePaintPluginCapability.Generator|TexturePaintPluginCapability.ReadsMeshMaps|TexturePaintPluginCapability.LongRunning,
                supportedTargets=TexturePaintPluginTarget.All,declaredChannels=TexturePaintChannelMask.Albedo|TexturePaintChannelMask.Roughness|TexturePaintChannelMask.NormalControl,
                readChannels=TexturePaintChannelMask.None,requiredMeshMaps=TexturePaintMeshMapMask.WorldPosition|TexturePaintMeshMapMask.WorldNormal|TexturePaintMeshMapMask.SurfaceId,parameters=p};
        }
        internal static TexturePaintPluginParameterDefinition Header(string id,string name,string help)=>new(){id=id,displayName=name,description=help,type=TexturePaintPluginParameterType.Header};
        public Task ExecuteAsync(TexturePaintCommandContextV2 c)
        {
            var material=new MakeupMaterial(c.parameters);var mask=c.GetTextureParameter("controlMask");
            var frames=new Dictionary<string,MakeupFaceFrame>();
            foreach(var id in c.source.surfaceIds)frames[id]=MakeupFaceFrame.Resolve(c.source.GetAnatomy(id));
            var channels=new List<TexturePaintChannel>();foreach(var ch in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})
                if(c.parameters.Boolean("output"+ch,ch!=TexturePaintChannel.NormalControl))channels.Add(ch);
            bool maskTarget=c.target==TexturePaintPluginTarget.LayerMask;
            if(channels.Count==0&&!maskTarget)throw new InvalidOperationException("Enable at least one makeup output.");
            return Task.Run(()=>
            {
                int si=0;
                foreach(var id in c.source.surfaceIds)
                {
                    var frame=frames[id];var position=c.GetMeshMap(id,TexturePaintMeshMap.WorldPosition);var normals=c.GetMeshMap(id,TexturePaintMeshMap.WorldNormal);var ids=c.GetMeshMap(id,TexturePaintMeshMap.SurfaceId);
                    if(position==null||normals==null||ids==null)throw new InvalidOperationException("Makeup requires World Position, World Normal and Surface ID mesh maps.");
                    var sizes=new Dictionary<Vector2Int,List<TexturePaintChannel>>();
                    if(maskTarget){var m=c.source.GetMask(id);if(m!=null)sizes[new(m.width,m.height)]=new();}
                    else foreach(var ch in channels){var info=c.source.GetChannelInfo(id,ch);if(info==null)continue;var size=new Vector2Int(info.width,info.height);if(!sizes.TryGetValue(size,out var list))sizes[size]=list=new();list.Add(ch);}
                    foreach(var size in sizes)
                    {
                        int w=size.Key.x,h=size.Key.y;
                        for(int start=0;start<h;start+=32)
                        {
                            c.cancellationToken.ThrowIfCancellationRequested();int rows=Math.Min(32,h-start);var buffers=new Dictionary<TexturePaintChannel,Color[]>();
                            foreach(var ch in size.Value)buffers[ch]=new Color[w*rows];var masks=maskTarget?new Color32[w*rows]:null;
                            Parallel.For(0,rows,new ParallelOptions{CancellationToken=c.cancellationToken},y=>
                            {
                                for(int x=0;x<w;x++)
                                {
                                    float u=(x+.5f)/w,v=(start+y+.5f)/h;int i=y*w+x;
                                    if(ids.GetPixelBilinear(u,v).a<.5f){if(maskTarget)masks[i]=Color.black;continue;}
                                    var p=position.GetPixelBilinear(u,v);var n=normals.GetPixelBilinear(u,v);
                                    Vector3 point=new(p.r,p.g,p.b),normal=new(n.r*2-1,n.g*2-1,n.b*2-1);
                                    var dx=position.GetPixelBilinear(u+1f/w,v);var dy=position.GetPixelBilinear(u,v+1f/h);
                                    float footprint=Mathf.Clamp(Mathf.Max(Vector3.Distance(point,new(dx.r,dx.g,dx.b)),Vector3.Distance(point,new(dy.r,dy.g,dy.b)))/frame.scale,.0001f,.03f);
                                    var s=material.Sample(frame,point,normal,footprint);
                                    if(mask!=null){var m=mask.GetPixelBilinear(u,v);s.coverage*=m.r*m.a;}
                                    if(maskTarget)masks[i]=new Color(s.coverage,s.coverage,s.coverage,1);
                                    else foreach(var pair in buffers)pair.Value[i]=s.Channel(pair.Key);
                                }
                            });
                            var rect=new RectInt(0,start,w,rows);
                            if(maskTarget)c.WriteMaskTileCompactOwned(id,rect,masks,TexturePaintPluginBlend.Replace);
                            else foreach(var pair in buffers)
                            {
                                if(pair.Key==TexturePaintChannel.NormalControl)
                                    c.WriteTile(id,pair.Key,rect,pair.Value,TexturePaintPluginColorSpace.Data,TexturePaintPluginBlend.Replace);
                                else
                                {
                                    var compact=new Color32[pair.Value.Length];
                                    for(int i=0;i<compact.Length;i++)compact[i]=pair.Value[i];
                                    c.WriteTileCompactOwned(id,pair.Key,rect,compact,pair.Key==TexturePaintChannel.Albedo?TexturePaintPluginColorSpace.Linear:TexturePaintPluginColorSpace.Data,TexturePaintPluginBlend.Replace);
                                }
                            }
                            c.progress?.Report((si+(start+rows)/(float)h)/Math.Max(1,c.source.surfaceIds.Count));
                        }
                    }
                    si++;
                }
            },c.cancellationToken);
        }
    }

    /// <summary>Shared anatomical snapshot, expressed in eye-distance units. No live transforms.</summary>
    public sealed class MakeupFaceFrame
    {
        public Vector3 origin,right,up,forward,mouth;
        public float scale,lipWidth,lipUpper,lipLower;
        public Vector3 Project(Vector3 point){var d=(point-origin)/scale;return new(Vector3.Dot(d,right),Vector3.Dot(d,up),Vector3.Dot(d,forward));}
        public static MakeupFaceFrame Resolve(TexturePaintAnatomy rig)
        {
            var l=rig?.Find(HumanBodyBones.LeftEye);var r=rig?.Find(HumanBodyBones.RightEye);var head=rig?.Find(HumanBodyBones.Head);
            if(l==null||r==null||head==null||Vector3.Distance(l.position,r.position)<.0001f)
                throw new InvalidOperationException("Makeup requires Head, LeftEye and RightEye bones. Open Overlay Painter from a built UMA character or a slot containing these landmarks.");
            var f=new MakeupFaceFrame{origin=(l.position+r.position)*.5f,scale=Vector3.Distance(l.position,r.position),right=(r.position-l.position).normalized};
            f.up=Vector3.ProjectOnPlane(head.up,f.right).normalized;f.forward=Vector3.Cross(f.right,f.up).normalized;
            if(Vector3.Dot(f.forward,head.forward)<0)f.forward=-f.forward;
            if(f.up.sqrMagnitude<.5f)throw new InvalidOperationException("The face landmarks have an invalid orientation.");
            var top=rig.Find(HumanBodyBones.LastBone,"LipsSuperior");var bottom=rig.Find(HumanBodyBones.LastBone,"LipsInferior");
            var left=rig.Find(HumanBodyBones.LastBone,"LeftLips");var right=rig.Find(HumanBodyBones.LastBone,"RightLips");
            if(top!=null&&bottom!=null)
            {f.mouth=f.Project((top.position+bottom.position)*.5f);float height=Mathf.Max(.055f,Vector3.Distance(top.position,bottom.position)/f.scale*.65f);f.lipUpper=height;f.lipLower=height*1.3f;}
            else{f.mouth=new Vector3(0,-1,.4f);f.lipUpper=.09f;f.lipLower=.085f;}
            f.lipWidth=left!=null&&right!=null?Vector3.Distance(left.position,right.position)/f.scale*.5f:.42f;
            return f;
        }
    }

    public sealed class MakeupMaterial
    {
        public struct SampleResult
        {
            public Color color;public float coverage,roughness,height;
            public Color Channel(TexturePaintChannel ch)=>ch==TexturePaintChannel.Albedo?new(color.r,color.g,color.b,coverage):
                ch==TexturePaintChannel.NormalControl?new(.5f+height,.5f+height,.5f+height,coverage):new(roughness,roughness,roughness,coverage);
        }
        readonly int style,side,seed;readonly float ox,oy,width,height,rotation,feather,asymmetry,bow,gap,line,wing,lower,arc,reach,gradient,opacity,roughness,variation,grain,relief;
        readonly Color color,edge;
        public MakeupMaterial(TexturePaintPluginParameterSet p)
        {
            float F(string id,float value,float min,float max)=>SurfaceDepositionMaterial.F(p,id,value,min,max);
            style=Mathf.Clamp(p.Integer("style"),0,6);side=Mathf.Clamp(p.Integer("side"),0,2);seed=p.Integer("seed",5183);
            ox=F("offsetX",0,-.5f,.5f);oy=F("offsetY",0,-.5f,.5f);width=F("width",1,.25f,2);height=F("height",1,.25f,2);
            rotation=F("rotation",0,-45,45)*Mathf.Deg2Rad;feather=F("feather",.12f,.001f,1);asymmetry=F("asymmetry",0,0,.3f);
            bow=F("lipBow",.55f,0,1);gap=F("lipGap",0,0,.15f);line=F("linerWidth",.025f,.005f,.1f);wing=F("wing",.12f,0,.4f);lower=F("lowerLiner",.15f,0,1);
            arc=F("crease",.12f,.04f,.3f);reach=F("shadowReach",.19f,.05f,.5f);gradient=F("gradient",.2f,0,1);opacity=F("opacity",.85f,0,1);
            roughness=p.Integer("finish",1) switch{0=>.8f,1=>.42f,2=>.12f,_=>F("roughness",.4f,0,1)};
            variation=F("pigmentVariation",.12f,0,1);grain=F("grain",210,30,600);relief=F("relief",.001f,0,.025f);
            color=p.LinearColor("color",new Color(.46f,.035f,.08f));edge=p.LinearColor("edgeColor",new Color(.2f,.018f,.03f));
        }
        static float S(float a,float b,float x)=>EyeIrisMaterial.Smooth(a,b,x);
        public SampleResult Sample(MakeupFaceFrame frame,Vector3 world,Vector3 normal,float aa)
        {
            Vector3 point=frame.Project(world);float front=S(.05f,.35f,Vector3.Dot(normal.normalized,frame.forward));
            if(front<=0||point.z<-.2f||point.z>1.8f||Mathf.Abs(point.x)>1.7f||point.y>1||point.y<-1.9f)return default;
            bool right=point.x>=0;if((side==1&&right)||(side==2&&!right))return default;
            float x=Mathf.Abs(point.x),y=point.y,asym=right?asymmetry:0;float cover=0,t=0;
            Vector2 q=new(x-ox,y-oy-asym*.12f);
            if(style==0)
            {
                Vector2 local=new(Mathf.Abs(point.x-frame.mouth.x)-ox,point.y-frame.mouth.y-oy);
                float cs=Mathf.Cos(rotation),sn=Mathf.Sin(rotation);
                float lx=(local.x*cs+local.y*sn)/(Mathf.Max(.05f,frame.lipWidth)*width);
                float ly=(-local.x*sn+local.y*cs)/height;
                float arch=Mathf.Sqrt(Mathf.Clamp01(1-lx*lx));
                float upper=frame.lipUpper*arch*(1+bow*(Mathf.Exp(-Mathf.Pow((lx-.32f)/.2f,2))*.6f-.22f*Mathf.Exp(-Mathf.Pow(lx/.16f,2))));
                float bottom=-frame.lipLower*Mathf.Pow(Mathf.Clamp01(1-lx*lx),.8f);
                float soft=Mathf.Max(aa,feather*.035f);cover=(1-S(1-soft,1+soft,lx))*S(bottom-soft,bottom+soft,ly)*(1-S(upper-soft,upper+soft,ly));
                if(gap>0)cover*=S(gap*.5f-aa,gap*.5f+aa,Mathf.Abs(ly));t=Mathf.Clamp01(Mathf.Abs(ly)/Mathf.Max(.001f,ly>0?upper:-bottom));
            }
            else
            {
                Vector2 center=style==3?new(.68f,-.52f):new(.5f,0);
                q-=center;float c=Mathf.Cos(rotation),s=Mathf.Sin(rotation);q=new Vector2((q.x*c+q.y*s)/width,(-q.x*s+q.y*c)/height);
                float ex=q.x/.31f,curve=arc*Mathf.Sqrt(Mathf.Clamp01(1-ex*ex))+.025f*ex;
                float soft=Mathf.Max(aa,feather*.035f);
                if(style==1)
                {
                    float upper=1-S(line*.5f,line*.5f+soft,Mathf.Abs(q.y-curve));upper*=1-S(.96f,1.02f,Mathf.Abs(ex));
                    float bottom=(1-S(line*.32f,line*.32f+soft,Mathf.Abs(q.y+curve*.75f)))*(1-S(.94f,1.03f,Mathf.Abs(ex)))*lower;
                    float along=(q.x-.27f)/Mathf.Max(wing,.0001f),wingY=arc*.49f+.022f+along*.07f;
                    float wingMask=wing>0?(1-S(line*(1-Mathf.Clamp01(along)),line*(1-Mathf.Clamp01(along))+soft,Mathf.Abs(q.y-wingY)))*S(-.1f,.05f,along)*(1-S(.85f,1,along)):0;
                    cover=Mathf.Max(Mathf.Max(upper,bottom),wingMask);t=Mathf.Clamp01(ex*.5f+.5f);
                }
                else if(style==2)
                {
                    float d=(q.y-curve)/reach;cover=S(-soft,soft,q.y-curve)*(1-S(.1f,1,d))*(1-S(.8f,1.3f,Mathf.Abs(ex)));t=Mathf.Clamp01(d);
                }
                else if(style==3){float r=new Vector2(q.x/.36f,q.y/.22f).magnitude;cover=1-S(.05f,1,r);t=r;}
                else if(style==4)
                {
                    float px=q.x+.5f,py=q.y;
                    float stripe=Mathf.Abs(Mathf.Repeat((py+.46f)/.16f+.5f,1)-.5f)*.16f;
                    cover=(1-S(.035f,.035f+soft,stripe))*(1-S(.62f,.85f,px))*S(.21f,.3f,px)*S(-.9f,-.82f,py)*(1-S(-.34f,-.27f,py));t=px;
                }
                else if(style==5)
                {float outer=new Vector2(q.x/.45f,q.y/.28f).magnitude;float inner=new Vector2(q.x/.32f,q.y/.14f).magnitude;cover=(1-S(.88f,1,outer))*S(1,1.1f,inner);t=Mathf.Clamp01(outer);}
                else
                {float stripe=Mathf.Abs(Mathf.Repeat((q.x+q.y*.4f)/.16f+.5f,1)-.5f)*.16f;cover=(1-S(.04f,.04f+soft,stripe))*(1-S(.18f,.25f,Mathf.Abs(q.y+.43f)))*(1-S(.7f,.95f,q.x+.5f))*S(.1f,.23f,q.x+.5f);t=Mathf.Repeat((q.x+q.y*.4f)/.16f,1);}
            }
            // Mirror the pigment field exactly at zero asymmetry; no independent UV noise.
            Vector2 noisePoint=new(x,point.y);float noise=EyeIrisMaterial.Noise(noisePoint*grain,seed+(right&&asym>0?173:0));
            noise=Mathf.Lerp(noise,.5f,S(.4f,1,aa*grain));
            cover=Mathf.Clamp01(cover*front*opacity*color.a*(1-variation*.22f*noise));
            var pigment=Color.Lerp(color,edge,Mathf.Clamp01(t*gradient));pigment*=1+(noise-.5f)*variation*.22f;
            return new SampleResult{color=pigment,coverage=cover,roughness=Mathf.Clamp01(roughness+(noise-.5f)*variation*.08f),height=(noise-.5f)*relief};
        }
    }
}
