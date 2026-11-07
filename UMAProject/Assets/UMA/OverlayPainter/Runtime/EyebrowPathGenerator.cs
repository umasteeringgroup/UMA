using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Deterministic hair geometry in base-path-width units. Geometry never depends on pixels.</summary>
    public sealed class EyebrowPathGenerator : ITexturePaintContextualPathGenerator
    {
        public const string GeneratorId="com.uma.path.eyebrow";
        public string Id=>GeneratorId;
        public string MenuPath=>"Hair/Eyebrows";
        public TexturePaintPathGeneratorSettings CreateSettings()=>new() {enabled=true,generatorId=Id,extensionJson=new EyebrowSettings().Save()};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()=>TexturePaintPathGenerators.Register(new EyebrowPathGenerator());
        public IDisposable Prepare(TexturePaintPathGeneratorSettings settings)=>Prepare(settings,new TexturePaintPathGeneratorContext());
        public IDisposable Prepare(TexturePaintPathGeneratorSettings settings,TexturePaintPathGeneratorContext context)
        {
            context??=new TexturePaintPathGeneratorContext();
            if(context.closed)throw new InvalidOperationException("Eyebrows need an open path. Turn off Closed; the first point is the inner head.");
            if(!float.IsFinite(context.size.x+context.size.y)||context.size.x<=0||context.size.y<=0)
                throw new InvalidOperationException("Eyebrows need two distinct points and a positive Path Width.");
            context.cancellation.ThrowIfCancellationRequested();
            var s=EyebrowSettings.Load(settings.extensionJson);
            // Resolve on the source path once: reflected ribbons already preserve its across axis.
            // Manual Flip Across remains an explicit override; saved settings are never rewritten.
            s.flipAcross ^= context.acrossPointsDown && !context.directUV;
            LastWarning=context.sharedUV ? "Coincident UV triangles detected: overlapping surface regions share texture detail. Use unique UVs for independently groomed left and right brows." : null;
            float aspect=Mathf.Clamp(context.size.y/context.size.x,.05f,100);
            var timer=Stopwatch.StartNew();
            var primary=s.Clone();primary.symmetry=BrowSymmetryMode.ExactMirror;
            var groom=GetGroom(primary,aspect,context);
            var alternate=s.symmetry==BrowSymmetryMode.ExactMirror?null:GetGroom(s,aspect,context);
            if(alternate!=null)
            {
                var padding=Vector2.Max(groom.padding,alternate.padding);
                groom=WithPadding(groom,padding);alternate=WithPadding(alternate,padding);
            }
            double groomMs=timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            int width=Mathf.Clamp(Mathf.CeilToInt(context.footprintPixels*(1+2*groom.padding.x)*(context.draft||!s.finalQuality?1:2)),64,
                context.draft?384:s.maximumRaster);
            int height=Mathf.Clamp(Mathf.CeilToInt(width*groom.bounds.height/groom.bounds.width),16,s.maximumRaster);
            if((long)width*height>4194304) {float reduction=Mathf.Sqrt(4194304f/(width*(float)height));width=(int)(width*reduction);height=(int)(height*reduction);}
            Sampled Sample(Groom g,EyebrowSettings settings)
            {
                string key=GeometryKey(settings,aspect)+":"+width+":"+height+":"+g.bounds;
                foreach(var cached in rasterCache)if(cached.key==key)return cached.sampled;
                var result=Rasterize(g,settings,width,height,context);
                rasterCache.AddFirst((key,result));while(rasterCache.Count>2)rasterCache.RemoveLast();return result;
            }
            var sampled=Sample(groom,primary);
            var other=alternate==null?null:Sample(alternate,s);
            double rasterMs=timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var prepared=new Prepared(groom,sampled,s);
            try
            {
                if(other!=null)using(var second=new Prepared(alternate,other,s))prepared.AddVariant(second);
            }
            catch{prepared.Dispose();throw;}
            LastStatistics=$"{groom.strands.Count} strands; {width} x {height} local raster; destination {context.resolution}; groom {groomMs:F2} ms, raster {rasterMs:F2} ms, shading/upload {timer.Elapsed.TotalMilliseconds:F2} ms";
            return prepared;
        }
        public static string LastStatistics {get;private set;}
        public static string LastWarning {get;private set;}
        public void Bind(MaterialPropertyBlock properties,TexturePaintPathGeneratorSettings settings,IDisposable prepared,
            TexturePaintChannel channel,Color channelColor,Vector2 size)
        {
            if(prepared is not Prepared p || p.disposed)throw new ObjectDisposedException("Eyebrow prepared output");
            p.raster.Bind(properties,channel,channelColor);
            properties.SetFloat("_PathRasterVariants",p.variants);
        }
        public sealed class Strand
        {
            public int id,population,clump;
            public float u,v,length,width,opacity,depth;
            public Vector2[] points;
        }
        public sealed class Groom
        {
            public readonly List<Strand> strands=new();
            public Rect bounds;
            public Vector2 padding;
            public float aspect;
        }
        private sealed class CurveTable
        {
            private readonly Dictionary<BrowParameter,float[]> tables=new();
            public void Warm(params BrowParameter[] parameters)
            { foreach(var p in parameters) At(p); }
            public float At(BrowParameter p,float t=0)
            {
                if(p==null)return 0;
                if(!tables.TryGetValue(p,out var table))tables.Add(p,table=p.Lookup());
                float index=Mathf.Clamp01(t)*(table.Length-1);int a=(int)index;
                return Mathf.Lerp(table[a],table[Math.Min(a+1,table.Length-1)],index-a);
            }
        }
        private static readonly LinkedList<(string key,Groom groom)> groomCache=new();
        private static readonly LinkedList<(string key,Sampled sampled)> rasterCache=new();
        public static void ClearCaches(){groomCache.Clear();rasterCache.Clear();}
        private static Groom WithPadding(Groom original,Vector2 padding)
        {
            var copy=new Groom {aspect=original.aspect,padding=padding,bounds=new Rect(-padding.x*original.aspect,-.5f-padding.y,
                original.aspect*(1+2*padding.x),1+2*padding.y)};copy.strands.AddRange(original.strands);return copy;
        }
        private static string GeometryKey(EyebrowSettings s,float aspect)
        {
            var g=s.Clone();
            g.color=g.tipColor=g.grayColor=Color.white;g.colorVariation=new(0);g.grayFraction=new(0);
            g.colorAlongStrand=new(0);g.roughness=new(0);g.roughnessVariation=new(0);g.relief=new(0);g.ao=new(0);
            g.layerDepthContrast=new(0);
            g.diagnostic=0;g.advanced=false;g.finalQuality=false;g.maximumRaster=128;
            return aspect.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+g.Save();
        }
        private static Groom GetGroom(EyebrowSettings s,float aspect,TexturePaintPathGeneratorContext context)
        {
            string key=GeometryKey(s,aspect);
            foreach(var entry in groomCache)if(entry.key==key)return entry.groom;
            var result=GenerateGroom(s,aspect,context);
            groomCache.AddFirst((key,result));while(groomCache.Count>3)groomCache.RemoveLast();return result;
        }
        // Independent stream hashes: no UnityEngine.Random, shared state or object identities.
        public static float Random(int seed,int id,int stream)
        {
            unchecked {uint h=(uint)seed ^ (uint)id*0x9e3779b9u ^ (uint)stream*0x85ebca6bu;
                h^=h>>16;h*=0x7feb352du;h^=h>>15;h*=0x846ca68bu;h^=h>>16;return (h>>8)*(1f/16777216f);}
        }
        private static float Signed(int seed,int id,int stream)=>Random(seed,id,stream)*2-1;
        private static float Smooth(float a,float b,float x) {float t=Mathf.Clamp01((x-a)/Mathf.Max(.00001f,b-a));return t*t*(3-2*t);}
        private static float Correlated(int seed,float u,int stream)
        {float p=u*7;int i=(int)p;return Mathf.Lerp(Signed(seed,i,stream),Signed(seed,i+1,stream),Smooth(0,1,p-i));}
        private static float Halton(int n,int radix)
        {float f=1,r=0;while(n>0){f/=radix;r+=f*(n%radix);n/=radix;}return r;}

        public static Groom GenerateGroom(EyebrowSettings s,float aspect,TexturePaintPathGeneratorContext context=null)
        {
            var c=new CurveTable();var groom=new Groom {aspect=aspect};
            int seed=s.seed;
            float Amount(float u)=>s.symmetry==BrowSymmetryMode.ExactMirror?0:s.symmetry==BrowSymmetryMode.Independent?1:Mathf.Clamp01(c.At(s.asymmetry,u));
            float R(int id,int stream)=>Mathf.Lerp(Random(seed,id,stream),Random(seed^s.sideSeed,id,stream),Amount(Halton(id%100000+1,2)));
            float S(int id,int stream)=>R(id,stream)*2-1;
            float C(float u,int stream)=>Mathf.Lerp(Correlated(seed,u,stream),Correlated(seed^s.sideSeed,u,stream),Amount(u));
            int target=s.distribution==BrowPopulationMode.TargetCount?s.count:
                Mathf.RoundToInt(Mathf.Max(0,c.At(s.areaDensity))*aspect*Mathf.Max(0,c.At(s.fullness)));
            target=Mathf.Clamp(target,0,6000);
            for(int population=0;population<3;population++)
            {
                int wanted=Mathf.Clamp(Mathf.RoundToInt(target*(population==0?1:population==1?c.At(s.secondary):c.At(s.baby))),0,6000-groom.strands.Count);
                int accepted=0;
                for(int n=0;n<Math.Min(60000,wanted*24)&&accepted<wanted;n++)
                {
                    if((n&127)==0)context?.cancellation.ThrowIfCancellationRequested();
                    int id=n+population*100000;
                    float u=Mathf.Repeat(Halton(n+1,2)+S(id,1)*.016f*c.At(s.rootJitter),1);
                    float v=Mathf.Repeat(Halton(n+1,3)+S(id,2)*.07f*c.At(s.rootJitter),1);
                    if(population==2)v=R(id,3)<.5f?v*.16f:1-v*.16f;
                    if(u<Mathf.Clamp01(s.distributionStart)||u>Mathf.Clamp01(s.distributionEnd))continue;
                    float density=Mathf.Max(0,c.At(s.density,u))*Mathf.Max(0,c.At(s.densityAcross,v));
                    float edge=Mathf.Max(.001f,c.At(s.edgeSoftness,u));
                    density*=Smooth(0,edge,Mathf.Min(v,1-v));
                    density*=1-Mathf.Clamp01(c.At(s.thinning,u))*(.5f+.5f*C(u,60));
                    density*=1+c.At(s.densityVariation,u)*C(u,61);
                    if(R(id,4)>Mathf.Clamp01(density))continue;
                    float head=Smooth(0,Mathf.Max(.001f,c.At(s.headTaper,u)),u);
                    float tail=Smooth(0,Mathf.Max(.001f,c.At(s.tailTaper,u)),1-u);
                    float upper=Mathf.Max(0,c.At(s.upper,u))*c.At(s.fullness,u)*.5f*head*tail;
                    float lower=Mathf.Max(0,c.At(s.lower,u))*c.At(s.fullness,u)*.5f*head*tail;
                    float arch=Mathf.Sin(Mathf.PI*(u<s.archPosition ? u/Mathf.Max(.01f,s.archPosition)*.5f : .5f+(u-s.archPosition)/Mathf.Max(.01f,1-s.archPosition)*.5f));
                    float center=c.At(s.centerOffset,u)+c.At(s.archHeight,u)*arch-c.At(s.tailDrop,u)*Smooth(s.tailStart,1,u);
                    Vector2 root=new Vector2(u*aspect,center+Mathf.Lerp(-lower,upper,v));
                    if(Cut(s,root,u,aspect)<R(id,6))continue;
                    float angle=Mathf.Lerp(c.At(s.headAngle,u),c.At(s.bodyAngle,u),Smooth(0,s.headEnd,u));
                    angle=Mathf.Lerp(angle,c.At(s.archAngle,u),Smooth(s.headEnd,s.archPosition,u));
                    angle=Mathf.Lerp(angle,c.At(s.tailAngle,u),Smooth(s.tailStart,1,u));
                    angle+=Mathf.Lerp(c.At(s.lowerAngle,u),c.At(s.upperAngle,u),v)+c.At(s.hairSlant,u);
                    angle+=S(id,7)*c.At(s.directionVariation,u)+C(u,62)*c.At(s.broadVariation,u)*30;
                    float length=Mathf.Clamp(c.At(s.length,u)*c.At(s.lengthAcross,v)*(1+S(id,8)*c.At(s.lengthVariation,u)),.001f,4);
                    float width=Mathf.Clamp(c.At(s.thickness,u)*c.At(s.thicknessAcross,v)*(1+S(id,9)*c.At(s.thicknessVariation,u)),.00005f,.15f);
                    if(population==1){length*=Mathf.Max(.01f,c.At(s.secondaryLength,u));width*=Mathf.Max(.01f,c.At(s.secondaryThickness,u));}
                    if(population==2){length*=Mathf.Max(.01f,c.At(s.babyLength,u));width*=Mathf.Max(.01f,c.At(s.babyThickness,u));}
                    bool stray=R(id,10)<Mathf.Clamp01(c.At(s.flyaways,u));
                    if(stray){length*=1+Mathf.Clamp(c.At(s.strayAllowance,u),0,2)*3;angle+=S(id,11)*35;}
                    float rad=angle*Mathf.Deg2Rad;var direction=new Vector2(Mathf.Cos(rad),Mathf.Sin(rad));
                    var normal=new Vector2(-direction.y,direction.x);
                    float bend=c.At(s.bend,u)*(1+S(id,12)*c.At(s.bendVariation,u));
                    Vector2 tip=root+direction*length;
                    tip.y=Mathf.Lerp(tip.y,Mathf.Lerp(-lower,upper,Mathf.Clamp01(c.At(s.convergence,u)))+center,
                        Mathf.Clamp01(c.At(s.convergenceStrength,u))*.65f);
                    if(!stray)tip.y=Mathf.Lerp(tip.y,Mathf.Clamp(tip.y,center-lower,center+upper),Mathf.Clamp01(c.At(s.containment,u)));
                    float spacing=Mathf.Max(.02f,c.At(s.clumpSpacing,u));int clump=Mathf.FloorToInt(root.x/spacing);
                    Vector2 guide=root+new Vector2(Mathf.Cos((angle+S(clump,20)*c.At(s.guideVariation,u))*Mathf.Deg2Rad),
                        Mathf.Sin((angle+S(clump,20)*c.At(s.guideVariation,u))*Mathf.Deg2Rad))*length;
                    guide.x=(clump+.5f)*spacing+length*.6f;
                    float proximity=1-Mathf.Clamp01(Mathf.Abs(root.x-(clump+.5f)*spacing)/Mathf.Max(.001f,c.At(s.clumpSize,u)));
                    float clumping=Mathf.Clamp01(c.At(s.clumpStrength,u)*proximity*(1+S(id,21)*c.At(s.clumpVariation,u)));
                    Vector2 p1=root+direction*length*.33f+normal*bend*length*.2f;
                    Vector2 p2=tip-direction*length*.28f+normal*bend*length;
                    const int segments=48;var points=new Vector2[segments+1];
                    for(int j=0;j<=segments;j++)
                    {
                        float t=j/(float)segments,q=1-t;
                        Vector2 p=q*q*q*root+3*q*q*t*p1+3*q*t*t*p2+t*t*t*tip;
                        p+=(guide-tip)*clumping*Mathf.Clamp01(c.At(s.strandClump,t))*t;
                        p+=normal*S(id,22)*c.At(s.tipSpread,u)*t*t*length;
                        p+=normal*Mathf.Sin(t*Mathf.PI*2+R(id,23)*6.28f)*Mathf.Sin(t*Mathf.PI)*c.At(s.waviness,u)*length*(1+S(id,24)*c.At(s.waveVariation,u));
                        points[j]=p;
                    }
                    // Arc-length resampling gives s its literal root-to-tip meaning.
                    float[] cumulative=new float[points.Length];for(int j=1;j<points.Length;j++)cumulative[j]=cumulative[j-1]+Vector2.Distance(points[j],points[j-1]);
                    float total=Mathf.Max(.00001f,cumulative[^1]);var evenly=new Vector2[points.Length];int cursor=1;
                    for(int j=0;j<points.Length;j++)
                    {
                        float d=j/(float)segments*total;while(cursor<segments&&cumulative[cursor]<d)cursor++;
                        evenly[j]=Vector2.Lerp(points[cursor-1],points[cursor],(d-cumulative[cursor-1])/Mathf.Max(.000001f,cumulative[cursor]-cumulative[cursor-1]));
                        if(s.preserveLength)evenly[j]=root+(evenly[j]-root)*(length/total);
                        float x=(evenly[j].x/aspect-.5f)*c.At(s.lengthScale,u)*aspect;
                        float y=evenly[j].y+x*Mathf.Tan(Mathf.Clamp(c.At(s.slant,u),-60,60)*Mathf.Deg2Rad);
                        float rotation=Mathf.Clamp(c.At(s.rotation,u),-85,85)*Mathf.Deg2Rad;
                        evenly[j]=new Vector2(x*Mathf.Cos(rotation)-y*Mathf.Sin(rotation)+aspect*.5f,x*Mathf.Sin(rotation)+y*Mathf.Cos(rotation));
                        if(s.reverse)evenly[j].x=aspect-evenly[j].x;
                        if(s.flipAcross)evenly[j].y=-evenly[j].y;
                    }
                    groom.strands.Add(new Strand {id=id,population=population,clump=clump,u=u,v=v,length=length,width=width,
                        opacity=Mathf.Clamp01(c.At(s.opacity,u)*(1+S(id,25)*c.At(s.opacityVariation,u))),
                        depth=R(id,70)*(population==0?1:population==1?.65f:.4f),points=evenly});
                    accepted++;
                }
            }
            groom.strands.Sort((a,b)=>{int depth=a.depth.CompareTo(b.depth);return depth!=0?depth:a.id.CompareTo(b.id);});
            float padX=.03f*aspect,padY=.08f;
            foreach(var hair in groom.strands)foreach(var p in hair.points)
            {padX=Mathf.Max(padX,-p.x+hair.width,p.x-aspect+hair.width);padY=Mathf.Max(padY,Mathf.Abs(p.y)-.5f+hair.width);}
            groom.padding=new Vector2(padX/aspect+.005f,padY+.005f);
            groom.bounds=new Rect(-groom.padding.x*aspect,-.5f-groom.padding.y,aspect*(1+2*groom.padding.x),1+2*groom.padding.y);
            return groom;
        }
        private static float Cut(EyebrowSettings s,Vector2 p,float u,float aspect)
        {
            float result=1;
            foreach(var slit in s.slits)
            {
                if(slit==null||!slit.enabled)continue;
                float d=Mathf.Abs(u-slit.position-p.y/aspect*Mathf.Tan(Mathf.Clamp(slit.angle,-80,80)*Mathf.Deg2Rad));
                result*=Smooth(Mathf.Max(0,slit.width)*.5f,Mathf.Max(0,slit.width)*.5f+Mathf.Max(.0001f,slit.feather),d);
            }
            return result;
        }

        public sealed class Sampled
        {
            public int width,height;
            public float[] coverage,roundness,along;
            public int[] strand;
            public readonly List<CoverageHit> hits=new();
        }
        public struct CoverageHit {public int pixel,strand;public float coverage,along,roundness;}
        private struct Hit {public float coverage,roundness,along;}
        public static Sampled Rasterize(Groom g,EyebrowSettings s,int width,int height,TexturePaintPathGeneratorContext context=null)
        {
            var output=new Sampled {width=width,height=height,coverage=new float[width*height],roundness=new float[width*height],along=new float[width*height],strand=new int[width*height]};
            Array.Fill(output.strand,-1);var curves=new CurveTable();
            float px=g.bounds.width/width,py=g.bounds.height/height,aa=Mathf.Max(px,py)*.65f;
            // Snapshot Unity curves on the calling thread. Workers only read managed lookup
            // tables and immutable groom/settings data; texture uploads stay on the main thread.
            curves.Warm(s.strandWidth,s.strandOpacity,s.rootTaper,s.tipTaper,s.sharpness,s.rootFade,s.tipFade);
            float[] Snapshot(AnimationCurve curve)
            {var values=new float[1025];for(int i=0;i<values.Length;i++)values[i]=curve.Evaluate(i/(float)(values.Length-1));return values;}
            float Sample(float[] values,float t)
            {float f=Mathf.Clamp01(t)*(values.Length-1);int i=(int)f;return Mathf.Lerp(values[i],values[Math.Min(i+1,values.Length-1)],f-i);}
            var rootTaper=Snapshot(s.rootTaperCurve);var tipTaper=Snapshot(s.tipTaperCurve);
            var rootFade=Snapshot(s.rootFadeCurve);var tipFade=Snapshot(s.tipFadeCurve);
            // Batches bound temporary memory. Merge in strand order, never worker completion
            // order, to retain identical overlap, IDs and darker-below/lighter-above shading.
            const int batchSize=32;
            var options=new ParallelOptions {MaxDegreeOfParallelism=Math.Max(1,Math.Min(8,Environment.ProcessorCount-1)),
                CancellationToken=context?.cancellation??default};
            for(int batch=0;batch<g.strands.Count;batch+=batchSize)
            {
                int start=batch, count=Math.Min(batchSize,g.strands.Count-start);
                var fragments=new Dictionary<int,Hit>[count];
                Parallel.For(0,count,options,local=>
                {
                    int hi=start+local;
                    var hair=g.strands[hi];var hits=new Dictionary<int,Hit>();fragments[local]=hits;
                    float rootTaperDistance=Mathf.Max(.001f,curves.At(s.rootTaper,hair.u));
                    float tipTaperDistance=Mathf.Max(.001f,curves.At(s.tipTaper,hair.u));
                    float tipSharpness=Mathf.Clamp(curves.At(s.sharpness,hair.u),.1f,8);
                    float rootFadeDistance=Mathf.Max(.001f,curves.At(s.rootFade,hair.u));
                    float tipFadeDistance=Mathf.Max(.001f,curves.At(s.tipFade,hair.u));
                    for(int segment=0;segment<hair.points.Length-1;segment++)
                    {
                        options.CancellationToken.ThrowIfCancellationRequested();
                        var a=hair.points[segment];var b=hair.points[segment+1];var delta=b-a;
                        float s0=segment/(float)(hair.points.Length-1),s1=(segment+1)/(float)(hair.points.Length-1);
                        float radius=hair.width*.5f*Mathf.Max(0,curves.At(s.strandWidth,s0),curves.At(s.strandWidth,s1));
                        float padding=radius+aa;
                        int x0=Mathf.Clamp((int)((Mathf.Min(a.x,b.x)-padding-g.bounds.x)/px),0,width-1);
                        int x1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x,b.x)+padding-g.bounds.x)/px),0,width-1);
                        int y0=Mathf.Clamp((int)((Mathf.Min(a.y,b.y)-padding-g.bounds.y)/py),0,height-1);
                        int y1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y,b.y)+padding-g.bounds.y)/py),0,height-1);
                        for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                        {
                            Vector2 p=new Vector2(g.bounds.x+(x+.5f)*px,g.bounds.y+(y+.5f)*py);
                            float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(1e-12f,delta.sqrMagnitude));float along=Mathf.Lerp(s0,s1,t);
                            float taper=Mathf.Max(0,curves.At(s.strandWidth,along));
                            taper*=Mathf.Max(0,Sample(rootTaper,along/rootTaperDistance));
                            if(!s.trimmedEnds)taper*=Mathf.Pow(Mathf.Max(0,Sample(tipTaper,(1-along)/tipTaperDistance)),tipSharpness);
                            float r=hair.width*.5f*taper;if(r<1e-8f)continue;
                            float distance=Vector2.Distance(p,a+delta*t);
                            // Integrate the strand cross-section against the pixel filter. Multiplying
                            // a soft edge by a second subpixel factor loses fine hairs at face scale.
                            float coverage=Mathf.Clamp01((Mathf.Min(r,distance+aa)-Mathf.Max(-r,distance-aa))/(2*aa))*hair.opacity;
                            coverage*=Mathf.Clamp01(curves.At(s.strandOpacity,along));
                            coverage*=Mathf.Clamp01(Sample(rootFade,along/rootFadeDistance));
                            coverage*=Mathf.Clamp01(Sample(tipFade,(1-along)/tipFadeDistance));
                            float originalX=s.reverse?g.aspect-p.x:p.x;
                            coverage*=Cut(s,new Vector2(originalX,s.flipAcross?-p.y:p.y),originalX/g.aspect,g.aspect);
                            if(coverage<=0)continue;
                            int index=y*width+x;
                            if(!hits.TryGetValue(index,out var hit)||coverage>hit.coverage)
                                hits[index]=new Hit {coverage=coverage,along=along,roundness=Mathf.Sqrt(Mathf.Clamp01(1-distance*distance/(r*r)))};
                        }
                    }
                });
                for(int local=0;local<count;local++)
                {
                    int hi=start+local;
                    foreach(var pair in fragments[local])
                    {
                        int i=pair.Key;var hit=pair.Value;
                        if(output.hits.Count>=4000000)throw new InvalidOperationException("Eyebrow raster work limit reached. Reduce strand count, thickness, or maximum raster resolution.");
                        output.hits.Add(new CoverageHit {pixel=i,strand=hi,coverage=hit.coverage,along=hit.along,roundness=hit.roundness});
                        // Coverage union, with one surface height rather than summed overlap relief.
                        output.coverage[i]=1-(1-output.coverage[i])*(1-hit.coverage);
                        if(output.strand[i]<0 || hit.coverage>=.2f || output.roundness[i]<hit.roundness)
                        {output.strand[i]=hi;output.along[i]=hit.along;}
                        output.roundness[i]=Mathf.Max(output.roundness[i],hit.roundness);
                    }
                }
            }
            return output;
        }

        public sealed class Prepared : IDisposable,ITexturePaintPaddedPathRaster
        {
            public readonly TexturePaintPathGeneratorRaster raster=new();
            public readonly Groom groom;
            public bool disposed;
            public int variants=1;
            public Vector2 Padding=>groom.padding;
            public void AddVariant(Prepared other)
            {
                var merged=new TexturePaintPathGeneratorRaster();
                try
                {
                    foreach(var pair in raster.Channels)
                    {
                        var a=pair.Value;var b=other.raster.Channels[pair.Key];
                        bool linear=pair.Key!=TexturePaintChannel.Albedo;
                        var atlas=new Texture2D(a.width*2,a.height,TextureFormat.RGBA32,true,linear)
                            {name=a.name+" mirrored variants",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};
                        merged.Channels[pair.Key]=atlas;atlas.SetPixels(0,0,a.width,a.height,a.GetPixels());atlas.SetPixels(a.width,0,b.width,b.height,b.GetPixels());
                        SetCoverageMips(atlas,atlas.GetPixels(),linear);atlas.Apply(false,false);
                    }
                    raster.Dispose();foreach(var pair in merged.Channels)raster.Channels[pair.Key]=pair.Value;
                    raster.Coverage=raster.Channels[TexturePaintChannel.Albedo];merged.OwnsTextures=false;variants=2;
                }
                finally{merged.Dispose();}
            }
            public Prepared(Groom groom,Sampled samples,EyebrowSettings s)
            {
                this.groom=groom;
                try
                {
                    var c=new CurveTable();int count=samples.coverage.Length;
                    var albedo=new Color[count];var rough=new Color[count];var height=new Color[count];var ao=new Color[count];
                    // Shade the cached strand fragments back-to-front. Color edits reuse roots,
                    // curves and coverage. RGB accumulation is premultiplied in linear light;
                    // unpremultiply once into the host's straight-color texture convention.
                    c.Warm(s.colorAlongStrand,s.grayFraction,s.colorVariation,s.layerDepthContrast,
                        s.relief,s.roughness,s.roughnessVariation,s.ao,s.density,s.densityAcross);
                    const int tileCount=8;
                    var tiles=new List<CoverageHit>[tileCount];
                    for(int tile=0;tile<tileCount;tile++)tiles[tile]=new List<CoverageHit>();
                    foreach(var hit in samples.hits)tiles[(int)((long)hit.pixel*tileCount/count)].Add(hit);
                    Parallel.For(0,tileCount,new ParallelOptions {MaxDegreeOfParallelism=Math.Max(1,Math.Min(8,Environment.ProcessorCount-1))},tile=>
                    {
                    foreach(var hit in tiles[tile])
                    {
                        int i=hit.pixel;var hair=groom.strands[hit.strand];
                        float alpha=hit.coverage*Mathf.Clamp01(s.color.a);
                        float u=hair.u,t=hit.along;int id=hair.id;
                        Color ink=Color.Lerp(s.color,s.tipColor,Mathf.Clamp01(c.At(s.colorAlongStrand,t)));
                        if(Random(s.seed,id,50)<Mathf.Clamp01(c.At(s.grayFraction,u)))ink=s.grayColor;
                        ink*=Mathf.Max(0,1+Signed(s.seed,id,51)*c.At(s.colorVariation,u));
                        ink*=Mathf.Max(0,1+(hair.depth*2-1)*Mathf.Clamp(c.At(s.layerDepthContrast,u),0,.8f));
                        float h=Mathf.Clamp01(.5f+hit.roundness*Mathf.Clamp(c.At(s.relief,u),0,.49f));
                        float r=Mathf.Clamp01(c.At(s.roughness,u)+Signed(s.seed,id,52)*c.At(s.roughnessVariation,u));
                        if(s.diagnostic!=BrowDiagnostic.Material)
                        {
                            switch(s.diagnostic)
                            {
                                case BrowDiagnostic.Coverage:ink=Color.white;break;
                                case BrowDiagnostic.Height:ink=new Color(h,h,h);break;
                                case BrowDiagnostic.RootToTip:ink=new Color(t,t,t);break;
                                case BrowDiagnostic.StrandIds:ink=Color.HSVToRGB(Random(s.seed,id,53),.65f,1);break;
                                case BrowDiagnostic.Clumps:ink=Color.HSVToRGB(Random(s.seed,hair?.clump??0,54),.65f,1);break;
                                case BrowDiagnostic.Roots:alpha*=1-Smooth(.02f,.1f,t);ink=Color.cyan;break;
                                case BrowDiagnostic.Density:float d=Mathf.Clamp01(c.At(s.density,u)*c.At(s.densityAcross,hair?.v??0));ink=new Color(d,d,d);break;
                                case BrowDiagnostic.RootRegion:ink=hair.population==2?Color.yellow:Color.green;break;
                                case BrowDiagnostic.Growth:
                                    Vector2 direction=hair==null?Vector2.right:(hair.points[^1]-hair.points[0]).normalized;
                                    ink=new Color(direction.x*.5f+.5f,direction.y*.5f+.5f,0);break;
                            }
                        }
                        ink=ink.linear;float keep=1-alpha;
                        var previous=albedo[i];albedo[i]=new Color(ink.r*alpha+previous.r*keep,ink.g*alpha+previous.g*keep,
                            ink.b*alpha+previous.b*keep,alpha+previous.a*keep);
                        rough[i].r=r*alpha+rough[i].r*keep;
                        height[i].r=Mathf.Max(height[i].r,h);
                        ao[i].r=Mathf.Max(ao[i].r,Mathf.Clamp01(c.At(s.ao,u))*hit.roundness);
                    }
                    // Each pixel belongs to exactly one tile; the ordered fragment list keeps
                    // compositing deterministic without pixel locks or reordered hair overlaps.
                    int first=(int)(((long)tile*count+tileCount-1)/tileCount);
                    int last=(int)(((long)(tile+1)*count+tileCount-1)/tileCount);
                    for(int i=first;i<last;i++)
                    {
                        float alpha=albedo[i].a;
                        var ink=alpha>1e-7f?(albedo[i]/alpha).gamma:s.color;
                        ink.a=alpha;albedo[i]=ink;
                        float r=alpha>1e-7f?rough[i].r/alpha:1;
                        float h=alpha>0?Mathf.Max(.5f,height[i].r):.5f;
                        rough[i]=new Color(r,r,r,alpha);height[i]=new Color(h,h,h,alpha);
                        float a=1-ao[i].r;ao[i]=new Color(a,a,a,alpha);
                    }
                    });
                    if(s.diagnostic==BrowDiagnostic.RootRegion||s.diagnostic==BrowDiagnostic.Density)
                    {
                        float aa=groom.bounds.height/samples.height*1.5f;
                        for(int y=0;y<samples.height;y++)for(int x=0;x<samples.width;x++)
                        {
                            int i=y*samples.width+x;float px=groom.bounds.x+(x+.5f)/samples.width*groom.bounds.width;
                            float py=groom.bounds.y+(y+.5f)/samples.height*groom.bounds.height;
                            if(s.reverse)px=groom.aspect-px;if(s.flipAcross)py=-py;
                            float u=Mathf.Clamp01(px/groom.aspect),r=-Mathf.Clamp(c.At(s.rotation,u),-85,85)*Mathf.Deg2Rad;
                            px-=groom.aspect*.5f;float rx=px*Mathf.Cos(r)-py*Mathf.Sin(r),ry=px*Mathf.Sin(r)+py*Mathf.Cos(r);
                            py=ry-rx*Mathf.Tan(Mathf.Clamp(c.At(s.slant,u),-60,60)*Mathf.Deg2Rad);
                            u=rx/(groom.aspect*Mathf.Max(.001f,c.At(s.lengthScale,u)))+.5f;
                            float taper=Smooth(0,Mathf.Max(.001f,c.At(s.headTaper,u)),u)*Smooth(0,Mathf.Max(.001f,c.At(s.tailTaper,u)),1-u);
                            float upper=Mathf.Max(0,c.At(s.upper,u)*c.At(s.fullness,u))*.5f*taper;
                            float lower=Mathf.Max(0,c.At(s.lower,u)*c.At(s.fullness,u))*.5f*taper;
                            float arch=Mathf.Sin(Mathf.PI*(u<s.archPosition?u/s.archPosition*.5f:.5f+(u-s.archPosition)/(1-s.archPosition)*.5f));
                            float center=c.At(s.centerOffset,u)+c.At(s.archHeight,u)*arch-c.At(s.tailDrop,u)*Smooth(s.tailStart,1,u);
                            float edge=Mathf.Min(center+upper-py,py-center+lower);
                            float coverage=u>=s.distributionStart&&u<=s.distributionEnd&&u>=0&&u<=1?Smooth(-aa,aa,edge):0;
                            float v=(py-center+lower)/Mathf.Max(.00001f,upper+lower);
                            Color diagnostic=s.diagnostic==BrowDiagnostic.Density?Color.Lerp(Color.blue,Color.red,Mathf.Clamp01(c.At(s.density,u)*c.At(s.densityAcross,v))):
                                Color.Lerp(Color.cyan,Color.white,1-Smooth(aa,aa*3,Mathf.Abs(edge)));
                            diagnostic.a=coverage*(s.diagnostic==BrowDiagnostic.Density?.8f:Mathf.Lerp(1,.22f,Smooth(aa,aa*3,edge)));
                            albedo[i]=diagnostic;rough[i]=new Color(1,1,1,diagnostic.a);height[i]=new Color(.5f,.5f,.5f,diagnostic.a);ao[i]=rough[i];
                        }
                    }
                    raster.Channels[TexturePaintChannel.Albedo]=Make("Eyebrow albedo",albedo,samples,false);
                    raster.Coverage=raster.Channels[TexturePaintChannel.Albedo];
                    raster.Channels[TexturePaintChannel.Roughness]=Make("Eyebrow roughness",rough,samples,true);
                    raster.Channels[TexturePaintChannel.NormalControl]=Make("Eyebrow relief",height,samples,true);
                    raster.Channels[TexturePaintChannel.AmbientOcclusion]=Make("Eyebrow AO",ao,samples,true);
                }
                catch {Dispose();throw;}
            }
            private static Texture2D Make(string name,Color[] pixels,Sampled samples,bool linear)
            {
                var texture=new Texture2D(samples.width,samples.height,TextureFormat.RGBA32,true,linear)
                    {name=name,hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};
                try {SetCoverageMips(texture,pixels,linear);texture.Apply(false,false);return texture;}
                catch {UnityEngine.Object.DestroyImmediate(texture);throw;}
            }
            // Filter premultiplied linear color, then store straight color at every level.
            // Ordinary straight-RGBA mip generation darkens fine hairs against transparent pixels.
            // A real shell can map a tall source raster into only a few dozen destination rows.
            private static void SetCoverageMips(Texture2D texture,Color[] pixels,bool linear)
            {
                int width=texture.width,height=texture.height;
                texture.SetPixels(pixels,0);
                for(int level=1;level<texture.mipmapCount;level++)
                {
                    int nextWidth=Math.Max(1,width/2),nextHeight=Math.Max(1,height/2);
                    var next=new Color[nextWidth*nextHeight];
                    for(int y=0;y<nextHeight;y++)for(int x=0;x<nextWidth;x++)
                    {
                        int x0=x*width/nextWidth,x1=(x+1)*width/nextWidth;
                        int y0=y*height/nextHeight,y1=(y+1)*height/nextHeight;
                        Color sum=Color.clear;float alpha=0;int count=0;
                        for(int sy=y0;sy<y1;sy++)for(int sx=x0;sx<x1;sx++)
                        {
                            Color sample=pixels[sy*width+sx];float a=sample.a;
                            if(!linear)sample=sample.linear;
                            sum+=sample*a;alpha+=a;count++;
                        }
                        Color color=alpha>0?sum/alpha:pixels[y0*width+x0];
                        if(!linear&&alpha>0)color=color.gamma;
                        color.a=alpha/count;next[y*nextWidth+x]=color;
                    }
                    texture.SetPixels(next,level);pixels=next;width=nextWidth;height=nextHeight;
                }
            }
            public void Dispose(){if(disposed)return;disposed=true;raster.Dispose();}
        }
    }
}
