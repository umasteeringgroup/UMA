using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>Edge-preserving painterly abstraction and production color/value quantization.</summary>
    public sealed class StylizationFilterPlugin : ScriptableObject, ITexturePaintFilterV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor = StylizationFilterEngine.Descriptor();
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            TexturePaintExportTemplate.ToMask((TexturePaintChannel)Mathf.Clamp(parameters.Integer("sourceChannel"), 0, 10));
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) => StylizationFilterEngine.Execute(context);
    }

    internal static class StylizationFilterEngine
    {
        private const int Rows = 64;
        private static readonly string[] Channels = { "Albedo", "Normal", "Metallic", "Roughness", "Ambient Occlusion", "Emission", "Custom", "Skin Color Mask", "Thickness", "Detail Mask", "Normal Control" };
        private enum Operation { Kuwahara, RGBQuantize, LuminanceQuantize, Palette, Dithered, Toon }

        public static TexturePaintPluginDescriptor Descriptor() => new TexturePaintPluginDescriptor
        {
            id = "com.uma.texturepaint.filter.stylization",
            displayName = "Stylization, Kuwahara & Quantization",
            description = "Edge-preserving painterly Kuwahara, posterization, palette reduction, dithering and toon-band filtering.",
            pluginVersion = "1.0.0",
            capabilities = TexturePaintPluginCapability.Filter | TexturePaintPluginCapability.LongRunning,
            declaredChannels = TexturePaintChannelMask.All, readChannels = TexturePaintChannelMask.All,
            supportedTargets = TexturePaintPluginTarget.All, channelSnapshotMaximumResolution = 4096,
            parameters = Parameters()
        };

        public static Task Execute(TexturePaintCommandContextV2 c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var s = new Settings(c.parameters);
            return Task.Run(() =>
            {
                int count = Math.Max(1, c.source.surfaceIds.Count);
                for (int si = 0; si < c.source.surfaceIds.Count; si++)
                {
                    c.cancellationToken.ThrowIfCancellationRequested();
                    string id = c.source.surfaceIds[si];
                    TexturePaintReadOnlyPixels source = c.target == TexturePaintPluginTarget.LayerMask
                        ? (TexturePaintReadOnlyPixels)c.source.GetMask(id) : c.source.Get(id, s.source);
                    TexturePaintReadOnlyChannelInfo info = c.target == TexturePaintPluginTarget.LayerMask
                        ? null : c.source.GetChannelInfo(id, s.destination);
                    if (source == null || (info == null && c.target != TexturePaintPluginTarget.LayerMask)) continue;
                    int width = info?.width ?? source.width, height = info?.height ?? source.height;
                    for (int y0 = 0; y0 < height; y0 += Rows)
                    {
                        c.cancellationToken.ThrowIfCancellationRequested();
                        int rows = Math.Min(Rows, height - y0); var pixels = new Color32[width * rows];
                        KuwaharaIntegral integral = s.operation == Operation.Kuwahara
                            ? new KuwaharaIntegral(source, width, height, y0, rows,
                                Mathf.CeilToInt(s.radius)) : null;
                        Parallel.For(0,rows,new ParallelOptions{CancellationToken=c.cancellationToken},ly => { for (int x = 0; x < width; x++)
                        {
                            float u=(x+.5f)/width,v=(y0+ly+.5f)/height;
                            Color original=source.GetPixelBilinear(u,v), filtered=Filter(source,u,v,x,y0+ly,width,height,s,integral);
                            Color result=Color.Lerp(original,filtered,s.amount); result.a=s.preserveAlpha?original.a:filtered.a;
                            if (c.target==TexturePaintPluginTarget.LayerMask) { float g=Luma(result); result=new Color(g,g,g,1); }
                            else result=TexturePaintChannelUtility.ConstrainColor(s.destination,result);
                            pixels[ly*width+x]=result;
                        }});
                        RectInt rect=new RectInt(0,y0,width,rows);
                        if(c.target==TexturePaintPluginTarget.LayerMask)c.WriteMaskTileCompactOwned(id,rect,pixels,TexturePaintPluginBlend.Replace);
                        else c.WriteTileCompactOwned(id,s.destination,rect,pixels,TexturePaintChannelUtility.IsColor(s.destination)?TexturePaintPluginColorSpace.Linear:TexturePaintPluginColorSpace.Data,TexturePaintPluginBlend.Replace);
                        c.progress?.Report((si+(y0+rows)/(float)height)/count);
                    }
                }
            }, c.cancellationToken);
        }

        private static Color Filter(TexturePaintReadOnlyPixels source,float u,float v,int x,int y,int width,int height,Settings s,KuwaharaIntegral integral)
        {
            Color input=source.GetPixelBilinear(u,v);
            switch(s.operation)
            {
                case Operation.Kuwahara:return Kuwahara(source,u,v,x,y,s,integral);
                case Operation.RGBQuantize:return QuantizeRgb(input,s.levels,s.gamma,s.edgeBias);
                case Operation.LuminanceQuantize:return QuantizeLuminance(input,s.levels,s.gamma,s.preserveHue);
                case Operation.Palette:return Palette(input,s);
                case Operation.Dithered:
                {
                    float d=(Bayer(x,y)-.5f)*s.ditherStrength/Math.Max(2,s.levels-1);
                    return QuantizeRgb(new Color(input.r+d,input.g+d,input.b+d,input.a),s.levels,s.gamma,s.edgeBias);
                }
                default:return Toon(source,input,u,v,width,height,s);
            }
        }

        private static Color Kuwahara(TexturePaintReadOnlyPixels src,float u,float v,int x,int y,Settings s,KuwaharaIntegral integral)
        {
            if(integral==null)return src.GetPixelBilinear(u,v);
            int r=Math.Max(1,Mathf.RoundToInt(s.radius));float best=float.MaxValue;Color original=src.GetPixelBilinear(u,v),bestMean=original;
            Test(x-r,y-r,x,y);Test(x,y-r,x+r,y);Test(x-r,y,x,y+r);Test(x,y,x+r,y+r);
            if(s.quality>=1){int h=Math.Max(1,r/2);Test(x-r,y-h,x+r,y);Test(x-r,y,x+r,y+h);Test(x-h,y-r,x,y+r);Test(x,y-r,x+h,y+r);}
            if(s.quality>=2){int q=Math.Max(1,Mathf.RoundToInt(r*.7f));Test(x-q,y-q,x,y);Test(x,y-q,x+q,y);Test(x-q,y,x,y+q);Test(x,y,x+q,y+q);}
            return Color.Lerp(bestMean,src.GetPixelBilinear(u,v),s.detailPreservation);

            void Test(int x0,int y0,int x1,int y1)
            {
                integral.Statistics(x0,y0,x1,y1,out Color mean,out float variance);
                float score=variance*(1+s.edgeSensitivity*Math.Abs(Luma(mean)-Luma(original)));
                if(score<best){best=score;bestMean=mean;}
            }
        }

        /// <summary>Tile-local summed areas keep Kuwahara cost bounded at native 4K resolution.</summary>
        private sealed class KuwaharaIntegral
        {
            private readonly int width,stride,yMin,localHeight;private readonly Color[] sum;private readonly float[] luma,lumaSquared;
            public KuwaharaIntegral(TexturePaintReadOnlyPixels source,int width,int height,int y0,int rows,int radius)
            {
                this.width=width;yMin=Math.Max(0,y0-radius);int yMax=Math.Min(height,y0+rows+radius);localHeight=Math.Max(1,yMax-yMin);stride=width+1;
                int count=checked(stride*(localHeight+1));sum=new Color[count];luma=new float[count];lumaSquared=new float[count];
                for(int ly=1;ly<=localHeight;ly++)
                {
                    int gy=yMin+ly-1;Color row=Color.clear;float rowL=0,rowSq=0;
                    for(int xx=1;xx<=width;xx++)
                    {
                        Color c=source.GetPixelBilinear((xx-.5f)/width,(gy+.5f)/height);float value=Luma(c);row+=c;rowL+=value;rowSq+=value*value;int at=ly*stride+xx,above=(ly-1)*stride+xx;sum[at]=sum[above]+row;luma[at]=luma[above]+rowL;lumaSquared[at]=lumaSquared[above]+rowSq;
                    }
                }
            }
            public void Statistics(int x0,int y0,int x1,int y1,out Color mean,out float variance)
            {
                x0=Mathf.Clamp(x0,0,width-1);x1=Mathf.Clamp(x1,0,width-1);y0=Mathf.Clamp(y0,yMin,yMin+localHeight-1);y1=Mathf.Clamp(y1,yMin,yMin+localHeight-1);if(x1<x0)(x0,x1)=(x1,x0);if(y1<y0)(y0,y1)=(y1,y0);
                int ax=x0,bx=x1+1,ay=y0-yMin,by=y1-yMin+1;int a=ay*stride+ax,b=ay*stride+bx,c=by*stride+ax,d=by*stride+bx;int n=Math.Max(1,(bx-ax)*(by-ay));Color total=sum[d]-sum[b]-sum[c]+sum[a];float totalL=luma[d]-luma[b]-luma[c]+luma[a],totalSq=lumaSquared[d]-lumaSquared[b]-lumaSquared[c]+lumaSquared[a];mean=total/n;float m=totalL/n;variance=Math.Max(0,totalSq/n-m*m);
            }
        }

        private static Color Toon(TexturePaintReadOnlyPixels src,Color input,float u,float v,int width,int height,Settings s)
        {
            Color band=QuantizeLuminance(input,s.levels,s.gamma,true);
            float sx=s.edgeWidth/width,sy=s.edgeWidth/height;
            float gx=Math.Abs(Luma(src.GetPixelBilinear(u+sx,v))-Luma(src.GetPixelBilinear(u-sx,v)));
            float gy=Math.Abs(Luma(src.GetPixelBilinear(u,v+sy))-Luma(src.GetPixelBilinear(u,v-sy)));
            float edge=Smooth(s.edgeThreshold,s.edgeThreshold+s.edgeSoftness,Mathf.Sqrt(gx*gx+gy*gy));
            return Color.Lerp(band,s.edgeColor,edge*s.edgeOpacity);
        }

        private static Color QuantizeRgb(Color c,int levels,float gamma,float bias)
        {
            float Q(float x){x=Mathf.Pow(Mathf.Clamp01(x),gamma);float n=Math.Max(1,levels-1);x=Mathf.Floor(x*n+bias)/n;return Mathf.Pow(Mathf.Clamp01(x),1f/gamma);}
            return new Color(Q(c.r),Q(c.g),Q(c.b),c.a);
        }
        private static Color QuantizeLuminance(Color c,int levels,float gamma,bool preserveHue)
        {
            float old=Math.Max(.00001f,Luma(c)),n=Math.Max(1,levels-1);float encoded=Mathf.Pow(Mathf.Clamp01(old),gamma);
            float q=Mathf.Pow(Mathf.Round(encoded*n)/n,1f/gamma);
            if(!preserveHue)return new Color(q,q,q,c.a);float k=q/old;return new Color(c.r*k,c.g*k,c.b*k,c.a);
        }
        private static Color Palette(Color c,Settings s)
        {
            Color[] palette={s.palette1,s.palette2,s.palette3,s.palette4,s.palette5,s.palette6,s.palette7,s.palette8};
            float best=float.MaxValue;Color result=palette[0];Vector3 lab=ToPerceptual(c);
            for(int i=0;i<s.paletteCount;i++){Vector3 p=ToPerceptual(palette[i]);float d=(lab-p).sqrMagnitude;if(d<best){best=d;result=palette[i];}}
            result.a=c.a;return result;
        }
        private static Vector3 ToPerceptual(Color c){float l=Luma(c);return new Vector3(l,(c.r-c.g)*.5f,(c.b-c.g)*.5f);}
        private static float Bayer(int x,int y){int[,]b={{0,8,2,10},{12,4,14,6},{3,11,1,9},{15,7,13,5}};return(b[y&3,x&3]+.5f)/16f;}
        private static float Luma(Color c)=>c.r*.2126f+c.g*.7152f+c.b*.0722f;
        private static float Smooth(float a,float b,float x){float t=Mathf.Clamp01((x-a)/Math.Max(.000001f,b-a));return t*t*(3-2*t);}

        private sealed class Settings
        {
            public readonly TexturePaintChannel source,destination;public readonly Operation operation;public readonly float amount,radius,detailPreservation,edgeSensitivity,gamma,edgeBias,ditherStrength,edgeWidth,edgeThreshold,edgeSoftness,edgeOpacity;public readonly int levels,quality,paletteCount;public readonly bool preserveAlpha,preserveHue;public readonly Color palette1,palette2,palette3,palette4,palette5,palette6,palette7,palette8,edgeColor;
            public Settings(TexturePaintPluginParameterSet p){source=(TexturePaintChannel)Mathf.Clamp(p.Integer("sourceChannel"),0,10);destination=(TexturePaintChannel)Mathf.Clamp(p.Integer("destinationChannel"),0,10);operation=(Operation)Mathf.Clamp(p.Integer("operation"),0,5);amount=p.Float("amount",1);radius=p.Float("radius",5);quality=p.Integer("quality",1);detailPreservation=p.Float("detailPreservation",.08f);edgeSensitivity=p.Float("edgeSensitivity",1);levels=p.Integer("levels",6);gamma=p.Float("gamma",1);edgeBias=p.Float("edgeBias",.5f);preserveHue=p.Boolean("preserveHue",true);preserveAlpha=p.Boolean("preserveAlpha",true);ditherStrength=p.Float("ditherStrength",.65f);paletteCount=p.Integer("paletteCount",4);palette1=p.Color("palette1",Color.black);palette2=p.Color("palette2",new Color(.25f,.2f,.18f,1));palette3=p.Color("palette3",new Color(.72f,.6f,.45f,1));palette4=p.Color("palette4",Color.white);palette5=p.Color("palette5",Color.red);palette6=p.Color("palette6",Color.green);palette7=p.Color("palette7",Color.blue);palette8=p.Color("palette8",Color.gray);edgeWidth=p.Float("edgeWidth",1);edgeThreshold=p.Float("edgeThreshold",.08f);edgeSoftness=p.Float("edgeSoftness",.08f);edgeOpacity=p.Float("edgeOpacity",.75f);edgeColor=p.Color("edgeColor",Color.black);}
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters()=>new List<TexturePaintPluginParameterDefinition>{
            H("io","Input / Output","Filters read the composed source and replace the selected destination. In mask mode both are the mask."),E("sourceChannel","Source Channel",Channels,0,"Channel to sample."),E("destinationChannel","Destination Channel",Channels,0,"Channel to write."),E("operation","Operation",new[]{"Kuwahara Painterly","RGB Quantization","Luminance Quantization","Custom Palette","Dithered Quantization","Toon Bands + Edges"},0,"Stylization method."),F("amount","Amount",0,1,1,"Blend with original."),B("preserveAlpha","Preserve Alpha",true,"Keep input alpha."),
            H("kuwahara","Kuwahara","Edge-preserving painterly abstraction using tile-local summed-area sectors."),F("radius","Radius (px)",1,32,5,"Sector radius."),E("quality","Quality",new[]{"Preview (4 sectors)","Production (8 sectors)","Ultra (12 sectors)"},1,"Directional sectors tested per pixel; cost remains independent of radius."),F("detailPreservation","Detail Preservation",0,1,.08f,"Mix original micro-detail back."),F("edgeSensitivity","Edge Sensitivity",0,4,1,"Protects strong value boundaries."),
            H("quantize","Quantization","Reduce continuous values to intentional graphic bands."),I("levels","Levels",2,64,6,"Number of bands per component/value."),F("gamma","Gamma",.1f,4,1,"Moves band distribution toward shadows or highlights."),F("edgeBias","RGB Band Bias",0,1,.5f,"Floor/round bias for RGB quantization."),B("preserveHue","Preserve Hue",true,"Scale RGB when quantizing luminance."),F("ditherStrength","Dither Strength",0,2,.65f,"Ordered dither amplitude."),
            H("palette","Custom Palette","Nearest-color reduction uses perceptual luminance plus opponent color distance."),I("paletteCount","Active Colors",2,8,4,"Number of palette slots used."),C("palette1","Color 1",Color.black,"Palette color."),C("palette2","Color 2",new Color(.25f,.2f,.18f,1),"Palette color."),C("palette3","Color 3",new Color(.72f,.6f,.45f,1),"Palette color."),C("palette4","Color 4",Color.white,"Palette color."),C("palette5","Color 5",Color.red,"Palette color."),C("palette6","Color 6",Color.green,"Palette color."),C("palette7","Color 7",Color.blue,"Palette color."),C("palette8","Color 8",Color.gray,"Palette color."),
            H("toon","Toon Edges","Value bands plus source-gradient linework."),F("edgeWidth","Edge Width (px)",.5f,8,1,"Gradient sample distance."),F("edgeThreshold","Edge Threshold",0,1,.08f,"Minimum contrast for ink."),F("edgeSoftness","Edge Softness",.001f,.5f,.08f,"Ink transition."),F("edgeOpacity","Edge Opacity",0,1,.75f,"Ink contribution."),C("edgeColor","Edge Color",Color.black,"Ink color.")};
        private static TexturePaintPluginParameterDefinition H(string id,string n,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Header};private static TexturePaintPluginParameterDefinition F(string id,string n,float min,float max,float v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Float,minimum=min,maximum=max,defaultNumber=v};private static TexturePaintPluginParameterDefinition I(string id,string n,int min,int max,int v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Integer,minimum=min,maximum=max,defaultNumber=v};private static TexturePaintPluginParameterDefinition B(string id,string n,bool v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Boolean,defaultBoolean=v};private static TexturePaintPluginParameterDefinition C(string id,string n,Color v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Color,defaultColor=v};private static TexturePaintPluginParameterDefinition E(string id,string n,string[] o,int v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Enum,minimum=0,maximum=o.Length-1,defaultNumber=v,enumOptions=o};
    }
}
