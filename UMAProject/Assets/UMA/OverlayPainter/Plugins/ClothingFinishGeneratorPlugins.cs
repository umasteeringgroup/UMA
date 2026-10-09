using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    public sealed class WetnessSweatGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        public TexturePaintPluginDescriptor Descriptor => ClothingFinishGenerator.Describe(ClothingFinish.Wetness);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) => ClothingFinishGenerator.Execute(context, ClothingFinish.Wetness);
    }
    public sealed class LeatherCoatedFabricGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        public TexturePaintPluginDescriptor Descriptor => ClothingFinishGenerator.Describe(ClothingFinish.Leather);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) => ClothingFinishGenerator.Execute(context, ClothingFinish.Leather);
    }
    public sealed class PrintAgingGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        public TexturePaintPluginDescriptor Descriptor => ClothingFinishGenerator.Describe(ClothingFinish.PrintAging);
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) => ClothingFinishGenerator.Execute(context, ClothingFinish.PrintAging);
    }

    internal enum ClothingFinish { Wetness, Leather, PrintAging }

    /// <summary>Shared coverage and linear-color contract; material-specific responses remain separate.</summary>
    internal static class ClothingFinishGenerator
    {
        private static readonly TexturePaintChannel[] Channels = { TexturePaintChannel.Albedo,
            TexturePaintChannel.Roughness, TexturePaintChannel.Metallic, TexturePaintChannel.NormalControl };

        public static TexturePaintPluginDescriptor Describe(ClothingFinish mode)
        {
            string[] ids = { "wetness-sweat", "leather-coated-fabric", "print-aging" };
            string[] names = { "Fabric — Wetness & Sweat", "Fabric — Leather & Coated Fabric", "Fabric — Print Aging" };
            var p = new List<TexturePaintPluginParameterDefinition>
            {
                new() { id="profile", displayName="Material Preset", type=TexturePaintPluginParameterType.Enum,
                    minimum=0, maximum=mode==ClothingFinish.Leather ? 3 : 2,
                    enumOptions=mode==ClothingFinish.Wetness ? new[]{"Rain-soaked cloth","Sweat patches","Dry salt residue"} :
                        mode==ClothingFinish.Leather ? new[]{"Soft leather","Worn leather","Suede","Coated fabric"} : new[]{"Cracked screen print","Faded transfer","Peeling rubber print"} },
                F("amount","Effect Amount",0,1,.65f), F("scale","Pattern Scale",1,128,12),
                F("rotation","Rotation",-180,180,0), F("seed","Seed",0,65535,1731),
                new() { id="projection",displayName="Mapping",type=TexturePaintPluginParameterType.Enum,enumOptions=new[]{"Flat (UV)","World"} },
                new() { id="controlMask",displayName=mode==ClothingFinish.PrintAging ? "Print Coverage Mask (required)" : "Control Mask",
                    description="White applies the effect; black protects the material. Texture alpha is also respected.",type=TexturePaintPluginParameterType.Texture },
                new() { id="color",displayName=mode==ClothingFinish.PrintAging ? "Exposed Fabric Color" : mode==ClothingFinish.Leather ? "Leather / Coating Color" : "Residue Color",
                    type=TexturePaintPluginParameterType.Color,defaultColor=mode==ClothingFinish.Leather ? new Color(.22f,.095f,.045f,1) : new Color(.72f,.7f,.65f,1) },
                F("roughness","Surface Roughness",0,1,mode==ClothingFinish.Wetness ? .18f : .58f),
                F("relief","Relief Strength",0,.3f,.06f),
                F("cavity","Cavity Influence",0,1,.45f),
                F("edge","Edge Influence",0,1,.45f)
            };
            if(mode==ClothingFinish.Wetness)
            { p.Add(F("drying","Drying",0,1,.15f)); p.Add(F("absorption","Absorption / Darkening",0,1,.55f)); p.Add(F("wicking","Fiber Wicking",0,1,.35f)); }
            else if(mode==ClothingFinish.Leather)
            { p.Add(F("grain","Grain Size",8,256,80)); p.Add(F("crease","Creases",0,1,.35f)); p.Add(F("wear","Dye / Coating Wear",0,1,.22f)); }
            else
            { p.Add(F("crack","Crack Width",.005f,.3f,.065f)); p.Add(F("fade","Color Fade",0,1,.25f)); }
            foreach(var channel in Channels)
                p.Add(new TexturePaintPluginParameterDefinition { id="output"+channel,displayName="Output "+channel,
                    type=TexturePaintPluginParameterType.Boolean, defaultBoolean=channel!=TexturePaintChannel.Metallic });
            return new TexturePaintPluginDescriptor
            {
                id="com.uma.texturepaint."+ids[(int)mode], displayName=names[(int)mode], pluginVersion="1.0.0",
                description=mode==ClothingFinish.PrintAging
                    ? "Ages artwork inside an explicit print mask. Exposed Fabric Color defines the revealed backing; add a matching fabric layer beneath the original print."
                    : "Coordinated color, roughness and additive relief. Preserves lower-layer normals and respects the control mask.",
                capabilities=TexturePaintPluginCapability.Generator|TexturePaintPluginCapability.ReadsMeshMaps|TexturePaintPluginCapability.LongRunning,
                declaredChannels=TexturePaintChannelMask.Albedo|TexturePaintChannelMask.Roughness|TexturePaintChannelMask.Metallic|TexturePaintChannelMask.NormalControl,
                readChannels=TexturePaintChannelMask.Albedo|TexturePaintChannelMask.Roughness|TexturePaintChannelMask.Normal,
                channelSnapshotMaximumResolution=2048,
                requiredMeshMaps=TexturePaintMeshMapMask.SurfaceId|TexturePaintMeshMapMask.WorldPosition|TexturePaintMeshMapMask.SignedCurvature|TexturePaintMeshMapMask.AmbientOcclusion,
                parameters=p
            };
        }

        private static TexturePaintPluginParameterDefinition F(string id,string name,float min,float max,float value) =>
            new() { id=id,displayName=name,type=TexturePaintPluginParameterType.Float,minimum=min,maximum=max,defaultNumber=value };

        public static Task Execute(TexturePaintCommandContextV2 context, ClothingFinish mode)
        {
            var mask=context.GetTextureParameter("controlMask");
            if(mode==ClothingFinish.PrintAging && mask==null)
                throw new InvalidOperationException("Choose a Print Coverage Mask: white artwork on transparent or black background. Aging is restricted to that artwork.");
            var p=context.parameters.Clone();
            bool anyOutput=false;
            foreach(var channel in Channels)anyOutput|=p.Boolean("output"+channel,channel!=TexturePaintChannel.Metallic);
            if(!anyOutput)throw new InvalidOperationException("Enable at least one output channel before generating the finish.");
            // Resolve Unity picker colors on the calling thread. Texture snapshots are already linear.
            Color tint=p.LinearColor("color",mode==ClothingFinish.Leather ? new Color(.22f,.095f,.045f) : new Color(.72f,.7f,.65f));
            return Task.Run(()=>
            {
                int surfaceIndex=0;
                foreach(string surface in context.source.surfaceIds)
                {
                    var albedo=context.source.Get(surface,TexturePaintChannel.Albedo);
                    var rough=context.source.Get(surface,TexturePaintChannel.Roughness);
                    var normals=context.source.Get(surface,TexturePaintChannel.Normal);
                    var ids=context.GetMeshMap(surface,TexturePaintMeshMap.SurfaceId);
                    var position=context.GetMeshMap(surface,TexturePaintMeshMap.WorldPosition);
                    var curvature=context.GetMeshMap(surface,TexturePaintMeshMap.SignedCurvature);
                    var ao=context.GetMeshMap(surface,TexturePaintMeshMap.AmbientOcclusion);
                    foreach(var channel in Channels)
                    {
                        if(!p.Boolean("output"+channel,channel!=TexturePaintChannel.Metallic))continue;
                        var info=context.source.GetChannelInfo(surface,channel); if(info==null)continue;
                        for(int y0=0;y0<info.height;y0+=64)
                        {
                            context.cancellationToken.ThrowIfCancellationRequested(); int rows=Math.Min(64,info.height-y0);
                            var pixels=new Color[info.width*rows];
                            Parallel.For(0,rows,new ParallelOptions{CancellationToken=context.cancellationToken},row=>
                            {
                                for(int x=0;x<info.width;x++)
                                {
                                    float u=(x+.5f)/info.width,v=(y0+row+.5f)/info.height;
                                    if(ids!=null && ids.GetPixelBilinear(u,v).a<.5f)continue;
                                    Color maskPixel=mask?.GetPixelBilinear(u,v)??Color.white;
                                    float coverage=Mathf.Clamp01(maskPixel.r*maskPixel.a); if(coverage<=0)continue;
                                    Color pos=position?.GetPixelBilinear(u,v)??new Color(u,v,0,1);
                                    Vector3 q=p.Integer("projection")==0 ? new Vector3(u,v,0) : new Vector3(pos.r,pos.g,pos.b);
                                    float radians=p.Float("rotation")*Mathf.Deg2Rad;
                                    q=new Vector3(q.x*Mathf.Cos(radians)-q.y*Mathf.Sin(radians),q.x*Mathf.Sin(radians)+q.y*Mathf.Cos(radians),q.z);
                                    float curve=curvature==null ? 0 : curvature.GetPixelBilinear(u,v).r*2-1;
                                    curve+=SurfaceNormalDetail.Curvature(normals,ids,u,v,3);
                                    float cavity=Mathf.Clamp01(-curve)+(ao==null?0:1-ao.GetPixelBilinear(u,v).r);
                                    Color baseColor=albedo?.GetPixelBilinear(u,v)??new Color(.18f,.18f,.18f,1);
                                    float baseRough=rough?.GetPixelBilinear(u,v).r??.65f;
                                    Sample(mode,p,q,tint,baseColor,baseRough,cavity,Mathf.Max(0,curve),out Color color,out float roughness,out float height,out float effect);
                                    float alpha=coverage*effect;
                                    float value=channel==TexturePaintChannel.Roughness ? roughness : channel==TexturePaintChannel.NormalControl ? Mathf.Clamp01(.5f+height) : 0;
                                    pixels[row*info.width+x]=channel==TexturePaintChannel.Albedo ? new Color(color.r,color.g,color.b,alpha) : new Color(value,value,value,alpha);
                                }
                            });
                            var rect=new RectInt(0,y0,info.width,rows);
                            if(channel==TexturePaintChannel.NormalControl)
                                context.WriteTile(surface,channel,rect,pixels,TexturePaintPluginColorSpace.Data);
                            else
                            {
                                var compact=new Color32[pixels.Length];for(int i=0;i<pixels.Length;i++)compact[i]=pixels[i];
                                context.WriteTileCompactOwned(surface,channel,rect,compact,channel==TexturePaintChannel.Albedo?TexturePaintPluginColorSpace.Linear:TexturePaintPluginColorSpace.Data);
                            }
                            context.progress?.Report((surfaceIndex+(Array.IndexOf(Channels,channel)+(y0+rows)/(float)info.height)/Channels.Length)/context.source.surfaceIds.Count);
                        }
                    }
                    surfaceIndex++;
                }
                context.progress?.Report(1);
            },context.cancellationToken);
        }

        private static float Noise(Vector3 q,float scale,float seed) =>
            (Mathf.PerlinNoise(q.x*scale+seed,q.y*scale-seed)+Mathf.PerlinNoise(q.y*scale+seed+31,q.z*scale+q.x*scale*.31f))/2;
        private static float Smooth(float a,float b,float value) { float t=Mathf.Clamp01((value-a)/Mathf.Max(.00001f,b-a));return t*t*(3-2*t); }

        private static void Sample(ClothingFinish mode,TexturePaintPluginParameterSet p,Vector3 q,Color tint,Color original,float baseRough,
            float cavity,float edge,out Color color,out float roughness,out float height,out float effect)
        {
            float amount=p.Float("amount",.65f),scale=p.Float("scale",12),seed=p.Float("seed",1731)*.0137f;
            float n=Noise(q,scale,seed),micro=Noise(q,scale*17,seed+19),relief=p.Float("relief",.06f);
            int profile=p.Integer("profile"); float rough=p.Float("roughness",.58f);
            color=original;roughness=baseRough;height=0;effect=1;
            if(mode==ClothingFinish.Wetness)
            {
                float drying=profile==2 ? Mathf.Max(.85f,p.Float("drying",.15f)) : p.Float("drying",.15f);
                float field=n+(cavity*p.Float("cavity",.45f))*.2f+(micro-.5f)*p.Float("wicking",.35f)*.15f;
                float boundary=1-amount; float wet=Smooth(boundary-.08f,boundary+.08f,field);
                float rim=Mathf.Exp(-Mathf.Pow((field-boundary)/.04f,2))*drying;
                float saturation=wet*(1-drying);
                color=original*Mathf.Lerp(1,1-p.Float("absorption",.55f),saturation);
                color=Color.Lerp(color,tint,rim*(profile==1?.55f:.85f));
                roughness=Mathf.Lerp(baseRough,profile==1?Mathf.Max(.3f,rough):rough,saturation);
                roughness=Mathf.Lerp(roughness,.85f,rim);
                height=(rim*.12f-saturation*.035f)*relief;
                effect=Mathf.Max(wet,rim);
            }
            else if(mode==ClothingFinish.Leather)
            {
                float grain=Noise(q,p.Float("grain",80),seed+3);
                float creases=Mathf.Pow(1-Mathf.Abs(n*2-1),18)*p.Float("crease",.35f);
                float wear=Mathf.Clamp01((edge*p.Float("edge",.45f)+Smooth(.55f,.85f,n)) * p.Float("wear",.22f)*(profile==1?2:1));
                float texture=profile==2 ? micro : grain;
                color=tint*Mathf.Lerp(.72f,1.2f,texture);
                color=Color.Lerp(color,profile==3 ? new Color(.18f,.14f,.09f,1) : tint*1.8f,wear);
                color*=1-creases*.35f;
                roughness=Mathf.Clamp01((profile==2?.88f:profile==3?.32f:rough)+(texture-.5f)*.16f-wear*.16f);
                height=((texture-.5f)*(profile==2?.25f:.7f)-creases)*relief;
                color=Color.Lerp(original,color,amount);roughness=Mathf.Lerp(baseRough,roughness,amount);height*=amount;
            }
            else
            {
                // Warped fracture bands, broken by noise rather than a regular crossing grid.
                float ridge=Mathf.Abs(Noise(q,scale*2,seed+7)-.5f);
                float crack=(1-Smooth(p.Float("crack",.065f)*.25f,p.Float("crack",.065f),ridge))*Smooth(.25f,.65f,n);
                float peeled=profile==2?Smooth(.57f,.78f,n):0;
                float damage=Mathf.Clamp01((crack+peeled+edge*p.Float("edge",.45f)+cavity*p.Float("cavity",.45f)*.25f)*amount);
                float fade=p.Float("fade",.25f)*amount*(profile==1?1.8f:1);
                color=Color.Lerp(original,tint,Mathf.Clamp01(damage+fade*.35f));
                roughness=Mathf.Lerp(baseRough,rough,Mathf.Clamp01(damage+fade));
                height=(-damage*.35f+(profile==2?Mathf.Exp(-Mathf.Pow((n-.57f)/.025f,2))*.15f:0))*relief;
            }
        }
    }
}
