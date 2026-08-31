using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>Editable multi-channel text with block placement and Custom-channel ribbon warping.</summary>
    public sealed class TextGeneratorPlugin : ScriptableObject, ITexturePaintGeneratorV2,
        ITexturePaintDynamicChannelUsageV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor = TextGeneratorEngine.Descriptor();
        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public TexturePaintChannelMask ResolveReadChannels(TexturePaintPluginParameterSet parameters) =>
            parameters.Integer("layout", 0) == 1 ? TexturePaintChannelMask.Custom : TexturePaintChannelMask.None;
        public Task ExecuteAsync(TexturePaintCommandContextV2 context) => TextGeneratorEngine.Execute(context);
    }

    internal static class TextGeneratorEngine
    {
        private const int Rows = 64;

        public static TexturePaintPluginDescriptor Descriptor() => new TexturePaintPluginDescriptor
        {
            id = "com.uma.texturepaint.text-generator", displayName = "Text",
            description = "Renders editable text into coordinated material channels or a grayscale layer/group mask; block text may be warped along a Custom-channel ribbon guide.",
            pluginVersion = "1.0.0", capabilities = TexturePaintPluginCapability.Generator | TexturePaintPluginCapability.LongRunning,
            declaredChannels = TexturePaintChannelMask.Albedo | TexturePaintChannelMask.NormalControl |
                               TexturePaintChannelMask.Roughness | TexturePaintChannelMask.Metallic,
            readChannels = TexturePaintChannelMask.Custom, supportedTargets = TexturePaintPluginTarget.All,
            channelSnapshotMaximumResolution = 4096, parameters = Parameters()
        };

        public static Task Execute(TexturePaintCommandContextV2 c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var s = new Settings(c.parameters);
            if (string.IsNullOrEmpty(s.text)) throw new InvalidOperationException("Enter text before generating.");
            if (c.target == TexturePaintPluginTarget.LayerContent && !s.AnyOutput)
                throw new InvalidOperationException("Enable at least one material output, or run Text while editing a layer/group mask.");
            // Font atlas and glyph metrics are Unity objects and are therefore captured on the main thread.
            TextBitmap bitmap = Rasterize(s);
            return Task.Run(() => Generate(c, s, bitmap), c.cancellationToken);
        }

        private static TextBitmap Rasterize(Settings s)
        {
            Font font = s.font != null ? s.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("No Font was assigned and Unity's LegacyRuntime font was unavailable.");
            font.RequestCharactersInTexture(s.text, s.fontSize, s.style);
            Texture2D atlas = font.material != null ? font.material.mainTexture as Texture2D : null;
            if (atlas == null) throw new InvalidOperationException("The selected Font did not provide a glyph atlas.");
            Color[] atlasPixels = ReadTexture(atlas);
            var lines = s.text.Replace("\r", string.Empty).Split('\n');
            var infos = new List<CharacterInfo[]>(); var widths = new List<int>();
            int lineHeight = Math.Max(s.fontSize, font.lineHeight > 0 ? font.lineHeight : s.fontSize);
            int maxWidth = 1;
            for (int li=0;li<lines.Length;li++)
            {
                CharacterInfo[] glyphs=new CharacterInfo[lines[li].Length];int width=0;
                for(int i=0;i<glyphs.Length;i++)
                {
                    if(!font.GetCharacterInfo(lines[li][i],out glyphs[i],s.fontSize,s.style)) continue;
                    width+=Math.Max(0,glyphs[i].advance)+s.letterSpacing;
                }
                width=Math.Max(1,width-Math.Max(0,s.letterSpacing));infos.Add(glyphs);widths.Add(width);maxWidth=Math.Max(maxWidth,width);
            }
            int height=Math.Max(1,lines.Length*lineHeight+(lines.Length-1)*s.lineSpacing);var pixels=new float[maxWidth*height];
            for(int li=0;li<lines.Length;li++)
            {
                int pen=s.alignment==0?0:s.alignment==1?(maxWidth-widths[li])/2:maxWidth-widths[li];
                int baseline=height-(li* (lineHeight+s.lineSpacing)+lineHeight*3/4);
                for(int gi=0;gi<infos[li].Length;gi++)
                {
                    CharacterInfo g=infos[li][gi];int gw=Math.Max(0,g.maxX-g.minX),gh=Math.Max(0,g.maxY-g.minY);
                    for(int y=0;y<gh;y++)for(int x=0;x<gw;x++)
                    {
                        float tx=(x+.5f)/Math.Max(1,gw),ty=(y+.5f)/Math.Max(1,gh);
                        Vector2 bottom=Vector2.Lerp(g.uvBottomLeft,g.uvBottomRight,tx),top=Vector2.Lerp(g.uvTopLeft,g.uvTopRight,tx);
                        Vector2 uv=Vector2.Lerp(bottom,top,ty);float a=Sample(atlasPixels,atlas.width,atlas.height,uv);
                        int dx=pen+g.minX+x,dy=baseline+g.minY+y;
                        if(dx>=0&&dx<maxWidth&&dy>=0&&dy<height)pixels[dy*maxWidth+dx]=Mathf.Max(pixels[dy*maxWidth+dx],a);
                    }
                    pen+=Math.Max(0,g.advance)+s.letterSpacing;
                }
            }
            if(s.boldAmount>0) Dilate(pixels,maxWidth,height,Mathf.RoundToInt(s.boldAmount));
            if(s.outlineWidth>0) AddOutline(pixels,maxWidth,height,Mathf.RoundToInt(s.outlineWidth),s.outlineOpacity);
            return new TextBitmap(maxWidth,height,pixels);
        }

        private static Color[] ReadTexture(Texture2D source)
        {
            RenderTexture rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            RenderTexture previous=RenderTexture.active;Texture2D copy=null;
            try{Graphics.Blit(source,rt);RenderTexture.active=rt;copy=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);copy.Apply(false,false);return copy.GetPixels();}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(copy!=null)UnityEngine.Object.DestroyImmediate(copy);}
        }

        private static void Generate(TexturePaintCommandContextV2 c,Settings s,TextBitmap bitmap)
        {
            int surfaces=Math.Max(1,c.source.surfaceIds.Count);
            for(int si=0;si<c.source.surfaceIds.Count;si++)
            {
                c.cancellationToken.ThrowIfCancellationRequested();string id=c.source.surfaceIds[si];
                if(c.target==TexturePaintPluginTarget.LayerMask)
                {
                    TexturePaintReadOnlyMask mask=c.source.GetMask(id);if(mask==null)continue;
                    GenerateSize(c,s,bitmap,id,mask.width,mask.height,si,surfaces,true,null,c.source.Get(id,TexturePaintChannel.Custom));continue;
                }
                var groups=new Dictionary<long,List<TexturePaintChannel>>();
                Add(TexturePaintChannel.Albedo,s.albedo);Add(TexturePaintChannel.NormalControl,s.normal);Add(TexturePaintChannel.Roughness,s.roughness);Add(TexturePaintChannel.Metallic,s.metallic);
                foreach(KeyValuePair<long,List<TexturePaintChannel>> pair in groups)
                {int w=(int)(pair.Key>>32),h=(int)pair.Key;GenerateSize(c,s,bitmap,id,w,h,si,surfaces,false,pair.Value,c.source.Get(id,TexturePaintChannel.Custom));}
                void Add(TexturePaintChannel channel,bool enabled){if(!enabled)return;TexturePaintReadOnlyChannelInfo info=c.source.GetChannelInfo(id,channel);if(info==null)return;long key=((long)info.width<<32)|(uint)info.height;if(!groups.TryGetValue(key,out List<TexturePaintChannel> list))groups.Add(key,list=new List<TexturePaintChannel>());list.Add(channel);}
            }
        }

        private static void GenerateSize(TexturePaintCommandContextV2 c,Settings s,TextBitmap bitmap,string id,int width,int height,int si,int surfaces,bool mask,List<TexturePaintChannel> channels,TexturePaintReadOnlyPixels guide)
        {
            RibbonMap ribbon=s.layout==1&&guide!=null?RibbonMap.Build(guide,width,height,s.guideThreshold,s.ribbonPadding):null;
            for(int y0=0;y0<height;y0+=Rows)
            {
                c.cancellationToken.ThrowIfCancellationRequested();int rows=Math.Min(Rows,height-y0);Color32[] maskPixels=mask?new Color32[width*rows]:null;var buffers=new Dictionary<TexturePaintChannel,Color32[]>();if(!mask)for(int i=0;i<channels.Count;i++)buffers[channels[i]]=new Color32[width*rows];
                Parallel.For(0,rows,new ParallelOptions{CancellationToken=c.cancellationToken},ly => {for(int x=0;x<width;x++)
                {
                    float coverage=ribbon!=null?ribbon.SampleText(x,y0+ly,bitmap,s):BlockText(x,y0+ly,width,height,bitmap,s);
                    if(s.shadowOpacity>0)coverage=Mathf.Max(coverage,Shadow(x,y0+ly,width,height,bitmap,s,ribbon)*s.shadowOpacity);
                    int index=ly*width+x;if(mask){byte b=B(coverage);maskPixels[index]=new Color32(b,b,b,255);continue;}
                    for(int i=0;i<channels.Count;i++){TexturePaintChannel ch=channels[i];buffers[ch][index]=Output(ch,s,coverage);}
                }});
                RectInt rect=new RectInt(0,y0,width,rows);if(mask)c.WriteMaskTileCompactOwned(id,rect,maskPixels,TexturePaintPluginBlend.Replace);else foreach(KeyValuePair<TexturePaintChannel,Color32[]> pair in buffers)c.WriteTileCompactOwned(id,pair.Key,rect,pair.Value,pair.Key==TexturePaintChannel.Albedo?TexturePaintPluginColorSpace.Linear:TexturePaintPluginColorSpace.Data,TexturePaintPluginBlend.Replace);
                c.progress?.Report((si+(y0+rows)/(float)height)/surfaces);
            }
        }

        private static float BlockText(float x,float y,int width,int height,TextBitmap b,Settings s)
        {
            float a=-s.rotation*Mathf.Deg2Rad,co=Mathf.Cos(a),si=Mathf.Sin(a);float dx=x-width*s.positionX,dy=y-height*s.positionY;
            float lx=dx*co-dy*si+b.width*.5f,ly=dx*si+dy*co+b.height*.5f;return b.Sample(lx,ly);
        }
        private static float Shadow(float x,float y,int width,int height,TextBitmap b,Settings s,RibbonMap ribbon)
        {return ribbon!=null?ribbon.SampleText(x-s.shadowX,y-s.shadowY,b,s):BlockText(x-s.shadowX,y-s.shadowY,width,height,b,s);}
        private static Color32 Output(TexturePaintChannel channel,Settings s,float coverage)
        {
            byte alpha=B(coverage);if(channel==TexturePaintChannel.Albedo){Color32 c=s.color;c.a=(byte)(alpha*c.a/255);return c;}
            float value=channel==TexturePaintChannel.NormalControl?s.heightValue:channel==TexturePaintChannel.Roughness?s.roughnessValue:s.metallicValue;byte g=B(value);return new Color32(g,g,g,alpha);
        }

        private sealed class RibbonMap
        {
            private readonly Vector2[] centers;private readonly float[] halfWidths;private readonly Vector2 axis,mean;private readonly float min,max;private readonly float padding;
            private RibbonMap(Vector2[] c,float[] w,Vector2 a,Vector2 mean,float min,float max,float p){centers=c;halfWidths=w;axis=a;this.mean=mean;this.min=min;this.max=max;padding=p;}
            public static RibbonMap Build(TexturePaintReadOnlyPixels guide,int width,int height,float threshold,float padding)
            {
                Vector2 mean=Vector2.zero;float count=0;int step=Math.Max(1,Math.Min(width,height)/256);
                for(int y=0;y<height;y+=step)for(int x=0;x<width;x+=step){float g=Luma(guide.GetPixelBilinear((x+.5f)/width,(y+.5f)/height));if(g<threshold)continue;mean+=new Vector2(x,y);count++;}
                if(count<4)return null;mean/=count;float xx=0,xy=0,yy=0;
                for(int y=0;y<height;y+=step)for(int x=0;x<width;x+=step){float g=Luma(guide.GetPixelBilinear((x+.5f)/width,(y+.5f)/height));if(g<threshold)continue;Vector2 d=new Vector2(x,y)-mean;xx+=d.x*d.x;xy+=d.x*d.y;yy+=d.y*d.y;}
                float angle=.5f*Mathf.Atan2(2*xy,xx-yy);Vector2 axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)),normal=new Vector2(-axis.y,axis.x);float min=float.MaxValue,max=float.MinValue;
                for(int y=0;y<height;y+=step)for(int x=0;x<width;x+=step){if(Luma(guide.GetPixelBilinear((x+.5f)/width,(y+.5f)/height))<threshold)continue;float t=Vector2.Dot(new Vector2(x,y)-mean,axis);min=Math.Min(min,t);max=Math.Max(max,t);}
                const int bins=256;var sum=new Vector2[bins];var n=new int[bins];var lo=new float[bins];var hi=new float[bins];for(int i=0;i<bins;i++){lo[i]=float.MaxValue;hi[i]=float.MinValue;}
                for(int y=0;y<height;y+=step)for(int x=0;x<width;x+=step){if(Luma(guide.GetPixelBilinear((x+.5f)/width,(y+.5f)/height))<threshold)continue;Vector2 p=new Vector2(x,y);float t=Vector2.Dot(p-mean,axis);int b=Mathf.Clamp(Mathf.FloorToInt((t-min)/Math.Max(1,max-min)*(bins-1)),0,bins-1);float side=Vector2.Dot(p-mean,normal);sum[b]+=p;n[b]++;lo[b]=Math.Min(lo[b],side);hi[b]=Math.Max(hi[b],side);}
                var centers=new Vector2[bins];var widths=new float[bins];for(int i=0;i<bins;i++){if(n[i]>0){centers[i]=sum[i]/n[i];widths[i]=Math.Max(1,(hi[i]-lo[i])*.5f);}else{int near=Nearest(n,i);centers[i]=near>=0?sum[near]/n[near]:mean;widths[i]=near>=0?Math.Max(1,(hi[near]-lo[near])*.5f):1;}}
                // Light smoothing keeps raster noise from making glyph baselines vibrate.
                for(int pass=0;pass<3;pass++){var copy=(Vector2[])centers.Clone();for(int i=1;i<bins-1;i++)centers[i]=(copy[i-1]+copy[i]*2+copy[i+1])*.25f;}
                return new RibbonMap(centers,widths,axis,mean,min,max,padding);
            }
            public float SampleText(float x,float y,TextBitmap text,Settings s)
            {
                Vector2 p=new Vector2(x,y);float projection=Vector2.Dot(p-mean,axis);float approximate=(projection-min)/Math.Max(1,max-min);int bin=Mathf.Clamp(Mathf.RoundToInt(approximate*(centers.Length-1)),0,centers.Length-1);
                // Refine locally because the smoothed centerline may bend away from the PCA axis.
                int best=bin;float bestD=float.MaxValue;for(int i=Math.Max(0,bin-4);i<=Math.Min(centers.Length-1,bin+4);i++){float d=(centers[i]-p).sqrMagnitude;if(d<bestD){bestD=d;best=i;}}
                Vector2 tangent=centers[Math.Min(centers.Length-1,best+1)]-centers[Math.Max(0,best-1)];if(tangent.sqrMagnitude<.001f)tangent=axis;tangent.Normalize();Vector2 normal=new Vector2(-tangent.y,tangent.x);float side=Vector2.Dot(p-centers[best],normal);float usable=halfWidths[best]*Mathf.Clamp01(1-padding);
                if(Math.Abs(side)>usable)return 0;float available=(max-min)*Mathf.Clamp01(1-padding*2f);float scale=s.fitRibbon?Math.Min(1f,available/Math.Max(1,text.width)):1f;scale=Math.Max(.05f,scale);float along=(best/(float)(centers.Length-1)-.5f)*(max-min);float tx=text.width*.5f+along/scale;float ty=text.height*.5f+side/scale;return text.Sample(tx,ty);
            }
            private static int Nearest(int[] n,int at){for(int d=1;d<n.Length;d++){int a=at-d,b=at+d;if(a>=0&&n[a]>0)return a;if(b<n.Length&&n[b]>0)return b;}return-1;}
        }

        private sealed class TextBitmap
        {
            public readonly int width,height;private readonly float[] pixels;public TextBitmap(int w,int h,float[] p){width=w;height=h;pixels=p;}
            public float Sample(float x,float y){if(x<0||y<0||x>=width-1||y>=height-1)return 0;int x0=(int)x,y0=(int)y;float tx=x-x0,ty=y-y0;float a=Mathf.Lerp(pixels[y0*width+x0],pixels[y0*width+x0+1],tx),b=Mathf.Lerp(pixels[(y0+1)*width+x0],pixels[(y0+1)*width+x0+1],tx);return Mathf.Lerp(a,b,ty);}
        }
        private sealed class Settings
        {
            public readonly string text;public readonly Font font;public readonly int fontSize,letterSpacing,lineSpacing,alignment;public readonly FontStyle style;public readonly float boldAmount,outlineWidth,outlineOpacity,positionX,positionY,rotation,shadowX,shadowY,shadowOpacity,guideThreshold,ribbonPadding,heightValue,roughnessValue,metallicValue;public readonly Color color;public readonly int layout;public readonly bool fitRibbon,albedo,normal,roughness,metallic;public bool AnyOutput=>albedo||normal||roughness||metallic;
            public Settings(TexturePaintPluginParameterSet p){text=p.String("text","UMA");font=p.Font("font");fontSize=p.Integer("fontSize",64);style=(FontStyle)Mathf.Clamp(p.Integer("fontStyle"),0,3);letterSpacing=p.Integer("letterSpacing",0);lineSpacing=p.Integer("lineSpacing",0);alignment=p.Integer("alignment",1);boldAmount=p.Float("boldAmount",0);outlineWidth=p.Float("outlineWidth",0);outlineOpacity=p.Float("outlineOpacity",1);layout=p.Integer("layout",0);positionX=p.Float("positionX",.5f);positionY=p.Float("positionY",.5f);rotation=p.Float("rotation",0);fitRibbon=p.Boolean("fitRibbon",true);guideThreshold=p.Float("guideThreshold",.1f);ribbonPadding=p.Float("ribbonPadding",.12f);color=p.Color("color",Color.white);shadowX=p.Float("shadowX",2);shadowY=p.Float("shadowY",-2);shadowOpacity=p.Float("shadowOpacity",0);albedo=p.Boolean("outputAlbedo",true);normal=p.Boolean("outputNormalControl",false);roughness=p.Boolean("outputRoughness",false);metallic=p.Boolean("outputMetallic",false);heightValue=Mathf.Clamp01(.5f+p.Float("normalHeight",.18f));roughnessValue=p.Float("roughnessValue",.42f);metallicValue=p.Float("metallicValue",0);}
        }

        private static List<TexturePaintPluginParameterDefinition> Parameters()=>new List<TexturePaintPluginParameterDefinition>{
            H("content","Text","Text settings remain editable on the Plugin layer or mask."),S("text","Text","UMA","Text; line breaks are supported in Block mode."),O("font","Font","Optional Unity Font asset. Empty uses LegacyRuntime.ttf."),I("fontSize","Font Size (px)",4,512,64,"Requested glyph size."),E("fontStyle","Font Style",new[]{"Normal","Bold","Italic","Bold + Italic"},0,"Font face/style."),C("color","Color",Color.white,"Albedo text color."),I("letterSpacing","Letter Spacing (px)",-32,128,0,"Additional advance per glyph."),I("lineSpacing","Line Spacing (px)",-32,256,0,"Additional baseline gap."),E("alignment","Alignment",new[]{"Left","Center","Right"},1,"Multiline alignment."),F("boldAmount","Extra Weight (px)",0,12,0,"Optional dilation beyond the selected face."),F("outlineWidth","Outline Width (px)",0,32,0,"Expands glyph coverage."),F("outlineOpacity","Outline Opacity",0,1,1,"Outline coverage."),
            H("placement","Placement / Ribbon","Ribbon mode reads a white/gray ribbon from the composed Custom channel. Put an editable Path/Ribbon layer below this Plugin layer and make it write Custom."),E("layout","Layout",new[]{"Block","Follow Custom Ribbon"},0,"Flat placement or ribbon warp."),F("positionX","Block X",0,1,.5f,"Block center in UV."),F("positionY","Block Y",0,1,.5f,"Block center in UV."),F("rotation","Block Rotation",-180,180,0,"Block angle."),B("fitRibbon","Fit To Ribbon Length",true,"Shrinks text to available guide length."),F("guideThreshold","Guide Threshold",0,1,.1f,"Minimum Custom luminance included in ribbon."),F("ribbonPadding","Ribbon Padding",0,.49f,.12f,"Inset from ribbon ends and sides."),F("shadowX","Shadow X (px)",-64,64,2,"Block/ribbon shadow offset."),F("shadowY","Shadow Y (px)",-64,64,-2,"Block/ribbon shadow offset."),F("shadowOpacity","Shadow Opacity",0,1,0,"Coverage-only shadow; for colored shadows use a separate Text layer."),
            H("outputs","Material Outputs / Mask","When run in mask mode, Text writes grayscale coverage only and these channel toggles are ignored."),B("outputAlbedo","Albedo",true,"Colored text."),B("outputNormalControl","Normal Control",false,"Raised/recessed text height."),F("normalHeight","Height",-.5f,.5f,.18f,"Offset from neutral gray."),B("outputRoughness","Roughness",false,"Text roughness."),F("roughnessValue","Roughness Value",0,1,.42f,"Value under glyphs."),B("outputMetallic","Metallic",false,"Text metallic."),F("metallicValue","Metallic Value",0,1,0,"Value under glyphs.")};
        private static TexturePaintPluginParameterDefinition H(string id,string n,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Header};private static TexturePaintPluginParameterDefinition F(string id,string n,float min,float max,float v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Float,minimum=min,maximum=max,defaultNumber=v};private static TexturePaintPluginParameterDefinition I(string id,string n,int min,int max,int v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Integer,minimum=min,maximum=max,defaultNumber=v};private static TexturePaintPluginParameterDefinition B(string id,string n,bool v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Boolean,defaultBoolean=v};private static TexturePaintPluginParameterDefinition C(string id,string n,Color v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Color,defaultColor=v};private static TexturePaintPluginParameterDefinition E(string id,string n,string[] o,int v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Enum,minimum=0,maximum=o.Length-1,defaultNumber=v,enumOptions=o};private static TexturePaintPluginParameterDefinition S(string id,string n,string v,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.MultilineString,defaultText=v};private static TexturePaintPluginParameterDefinition O(string id,string n,string d)=>new TexturePaintPluginParameterDefinition{id=id,displayName=n,description=d,type=TexturePaintPluginParameterType.Font};
        private static float Sample(Color[] p,int w,int h,Vector2 uv){float x=Mathf.Clamp01(uv.x)*(w-1),y=Mathf.Clamp01(uv.y)*(h-1);int x0=(int)x,y0=(int)y,x1=Math.Min(w-1,x0+1),y1=Math.Min(h-1,y0+1);float tx=x-x0,ty=y-y0;Color a=Color.Lerp(p[y0*w+x0],p[y0*w+x1],tx),b=Color.Lerp(p[y1*w+x0],p[y1*w+x1],tx),c=Color.Lerp(a,b,ty);return Math.Max(c.a,Math.Max(c.r,Math.Max(c.g,c.b)));}
        private static void Dilate(float[] p,int w,int h,int r){if(r<=0)return;float[] src=(float[])p.Clone(),horizontal=new float[p.Length];for(int y=0;y<h;y++)for(int x=0;x<w;x++){float m=0;for(int xx=Math.Max(0,x-r);xx<=Math.Min(w-1,x+r);xx++)m=Math.Max(m,src[y*w+xx]);horizontal[y*w+x]=m;}for(int y=0;y<h;y++)for(int x=0;x<w;x++){float m=0;for(int yy=Math.Max(0,y-r);yy<=Math.Min(h-1,y+r);yy++)m=Math.Max(m,horizontal[yy*w+x]);p[y*w+x]=m;}}
        private static void AddOutline(float[] p,int w,int h,int r,float opacity){float[] original=(float[])p.Clone();Dilate(p,w,h,r);for(int i=0;i<p.Length;i++)p[i]=Math.Max(original[i],p[i]*opacity);}
        private static float Luma(Color c)=>c.r*.2126f+c.g*.7152f+c.b*.0722f;private static byte B(float v)=>(byte)Mathf.RoundToInt(Mathf.Clamp01(v)*255);
    }
}
