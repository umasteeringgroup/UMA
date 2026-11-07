#ifndef UMA_PATH_GENERATOR_INCLUDED
#define UMA_PATH_GENERATOR_INCLUDED
int _PathGeneratorEnabled, _PathChannel;
int _PathWoundType;
float4 _PathColor, _PathHealedColor, _PathRimColor, _PathThreadColor, _PathChannelColor;
float4 _PathShape, _PathRelief, _PathFinish, _PathStitches, _PathTextLayout;
float _PathStitchSlant;
float4 _PathStitchVariation;
float4 _PathWoundNoise, _PathWoundFinish, _PathSutureOffsets;
float4 _PathSutureRelief, _PathPuncture;
Texture2D<float4> _PathText, _PathRaster, _PathRasterCoverage;
SamplerState path_linear_clamp_sampler;
float4 _PathRasterColor;
float _PathRasterVariants;

float PathBell(float d,float r) { return exp(-d*d/max(r*r,1e-8)); }
float PathWoundHash(float2 cell)
{
    return frac(sin(dot(cell,float2(127.1,311.7))+_PathFinish.z*17.13)*43758.5453);
}
float PathWoundNoise(float2 p)
{
    float2 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(PathWoundHash(cell),PathWoundHash(cell+float2(1,0)),f.x),
        lerp(PathWoundHash(cell+float2(0,1)),PathWoundHash(cell+1),f.x),f.y)*2-1;
}
float PathSegment(float2 p,float2 a,float2 b)
{ float2 v=b-a;return length(p-a-v*saturate(dot(p-a,v)/max(dot(v,v),1e-8))); }

float4 PathMaterial(float3 color,float coverage,float height,float roughness,float recess,
    float3 position,float3 normal,float4 tangent,float2 uv,float width)
{
    if(_PathChannel==0)return float4(color,coverage);
    if(_PathChannel==3)return float4(roughness.xxx,coverage);
    if(_PathChannel==4)return float4((1-saturate(recess)).xxx,coverage);
    if(_PathChannel==10)return float4(saturate(.5+height).xxx,coverage);
    if(_PathChannel!=1)return float4(_PathChannelColor.rgb,coverage);
    float3 n=normalize(normal),dx=ddx(position),dy=ddy(position);
    float3 rx=cross(dy,n),ry=cross(n,dx);
    float det=dot(dx,rx),scale=abs(det)>1e-12?rcp(det):0;
    float3 bumped=normalize(n-(ddx(height)*rx+ddy(height)*ry)*scale*width*_PathFinish.y);
    float3 t=tangent.xyz;float signT=tangent.w;
    if(dot(t,t)<.01)
    {
        float2 ux=ddx(uv),uy=ddy(uv);float d=ux.x*uy.y-ux.y*uy.x;
        t=(dx*uy.y-dy*ux.y)*(d<0?-1:1);
        float3 b=(dy*ux.x-dx*uy.x)*(d<0?-1:1);
        signT=dot(cross(n,t),b)<0?-1:1;
    }
    t=normalize(t-n*dot(t,n));float3 b=normalize(cross(n,t))*signT;
    return float4(normalize(float3(dot(bumped,t),dot(bumped,b),dot(bumped,n)))*.5+.5,coverage);
}

float4 ShadePathGenerator(float across,float along,float span,bool closed,float aa,
    float3 position,float3 normal,float4 tangent,float2 uv,float width,float mirrored)
{
    if(_PathGeneratorEnabled==5)
    {
        float2 q=float2(along/span,across);
        float variants=max(1,_PathRasterVariants);
        q.x=(q.x+(variants>1.5?mirrored:0))/variants;
        float4 raster=_PathRaster.Sample(path_linear_clamp_sampler,q)*_PathRasterColor;
        raster.a=_PathRasterCoverage.Sample(path_linear_clamp_sampler,q).a;return raster;
    }
    if(_PathGeneratorEnabled==3)
    {
        // Existing seam shading preserves underlying fabric; additional channels use its footprint.
        if(_PathChannel==0||_PathChannel==1||_PathChannel==3||_PathChannel==4||_PathChannel==6||_PathChannel==10)
            return ShadeHem(across,along,span,closed,aa,position,normal,tangent,uv,width);
        HemSample seam=EvaluateHem(across,along,span,closed,aa);
        return float4(_PathChannelColor.rgb,seam.coverage);
    }
    if(_PathGeneratorEnabled==4)
    {
        if(_PathChannel==0||_PathChannel==1||_PathChannel==2||_PathChannel==3||_PathChannel==4||_PathChannel==6||_PathChannel==10)
            return ShadeGarment(float2(across,along/span),uv,aa,closed,position,normal,tangent);
        GarmentSample garment=EvaluateGarment(float2(across,along/span),uv,aa,closed);
        return float4(_PathChannelColor.rgb,garment.coverage);
    }
    if(_PathGeneratorEnabled==2 || _PathGeneratorEnabled==6)
    {
        float repeat=along/span*_PathTextLayout.z;
        // Differentiate the unwrapped coordinate before wrapping repeated text.
        float2 coord=float2(repeat,across);
        float2 dx=ddx(coord)/_PathTextLayout.xy,dy=ddy(coord)/_PathTextLayout.xy;
        float2 textUV=(float2(frac(repeat),across)-.5)/_PathTextLayout.xy+.5;
        float alpha=_PathText.SampleGrad(path_linear_clamp_sampler,textUV,dx,dy).a;
        alpha*=step(0,textUV.x)*step(textUV.x,1)*step(0,textUV.y)*step(textUV.y,1)*_PathColor.a;
        float height=alpha*_PathRelief.w;
        float roughness=_PathChannelColor.r;
        return PathMaterial(_PathColor.rgb,alpha,height,roughness,1-_PathChannelColor.r,position,normal,tangent,uv,width);
    }
    float u=saturate(along/span),x=across-.5,heal=_PathShape.x;
    float tip=closed?1:pow(max(0,sin(u*3.14159265)),_PathShape.z);
    float noise=sin(u*37.6991118+_PathFinish.z)*sin(u*75.3982237+_PathFinish.z*.31);
    x-=noise*_PathShape.w*.025*tip;
    float halfOpening=lerp(_PathShape.y,.015,heal)*.5*tip;
    // Sewing closes the opening while leaving the raised, inflamed rim visible.
    if(_PathStitches.x==1)halfOpening*=.22;
    float edge=abs(x)-halfOpening;
    float cavity=(1-smoothstep(-aa,aa,edge))*tip;
    float rim=PathBell(edge,.024+_PathShape.w*.012)*tip;
    float halo=PathBell(x,halfOpening+.07)*_PathRelief.z*(1-heal)*tip;
    float coverage=max(cavity,max(rim*.8,halo*.5));
    float height=-cavity*_PathRelief.x*(1-heal)+rim*_PathRelief.y*lerp(1,.35,heal);
    float3 color=lerp(_PathColor.rgb,_PathHealedColor.rgb,heal);
    color=lerp(color,_PathRimColor.rgb,saturate(rim*.75+halo*.35)*(1-heal));
    float recess=cavity*(1-heal)*.6,thread=0;
    if(_PathWoundType==2)
    {
        float grain=sin(along*83+sin(x*127))*sin(x*97+_PathFinish.z);
        float skin=PathBell(x,halfOpening+.035)*tip;
        coverage=skin*(.78+.22*grain);
        height=(grain*.3-.35)*skin*_PathRelief.x*(1-heal)+rim*_PathRelief.y*.3;
        color=lerp(color,_PathRimColor.rgb,saturate(grain*.5+.5)*(1-heal));
        recess=skin*(1-heal)*.25;
    }
    else if(_PathWoundType==3)
    {
        float stretchCoverage=PathBell(x,max(.006,halfOpening*.5))*tip;
        coverage=stretchCoverage;height=-stretchCoverage*_PathRelief.x*.4;
        color=lerp(_PathRimColor.rgb,_PathHealedColor.rgb,heal);recess=stretchCoverage*.12;
    }
    float tissue=saturate(cavity*(1-rim));
    if(_PathWoundType==2 || _PathWoundType==3)tissue=saturate(coverage*(1-rim));
    if(_PathWoundNoise.x>0 || _PathWoundNoise.y>0)
    {
        float2 q=float2(along,x)*_PathWoundNoise.z;
        float mottling=PathWoundNoise(q);
        if(closed)mottling=lerp(mottling,PathWoundNoise(q-float2(span*_PathWoundNoise.z,0)),smoothstep(0,1,u));
        color=saturate(color*(1+mottling*_PathWoundNoise.x*tissue));
        height+=mottling*_PathWoundNoise.y*tissue;
    }
    float roughness=lerp(_PathWoundFinish.x,_PathWoundFinish.y,saturate(rim+halo));
    float sutureRange=1-_PathSutureOffsets.x-_PathSutureOffsets.y;
    if(_PathStitches.x>0 && sutureRange>1e-5)
    {
        float count=_PathStitches.y,stepSize=span*sutureRange/count;
        float stitchIndex=clamp(floor((u-_PathSutureOffsets.x)/sutureRange*count),0,count-1);
        float y=(u-_PathSutureOffsets.x)*span-(stitchIndex+.5)*stepSize;
        float halfSpan=_PathStitches.w*.5;
        float2 a=float2(-halfSpan,-halfSpan*_PathStitchSlant),b=-a;
        float d=PathSegment(float2(x,y),a,b);
        float holeDistance=min(length(float2(x,y)-a),length(float2(x,y)-b));
        float3 segment=float3(b-a,dot(b-a,b-a));
        float t=saturate(dot(float2(x,y)-a,segment.xy)/max(segment.z,1e-8));
        if(_PathStitchVariation.x>0 || _PathStitchVariation.y>0)
        {
            // Search neighboring stitches too: displaced/slanted threads must not be
            // clipped at the original spacing-cell boundary. Zero keeps legacy output.
            float coordinate=(u-_PathSutureOffsets.x)*span;
            float maxSlant=tan(radians(min(75,abs(_PathStitchVariation.z)+_PathStitchVariation.y)));
            float support=max(_PathStitches.z+aa,3*max(_PathStitches.z*_PathPuncture.y,_PathPuncture.w));
            float reach=halfSpan*maxSlant+support+stepSize*.45*_PathStitchVariation.x;
            int first=(int)clamp(ceil((coordinate-reach)/stepSize-.5),0,count-1);
            int last=(int)clamp(floor((coordinate+reach)/stepSize-.5),0,count-1);
            d=1e6;holeDistance=1e6;
            [loop] for(int stitch=first;stitch<=last;++stitch)
            {
                // Separate random streams keep angle and distance controls independent.
                float jitter=(PathWoundHash(float2(stitch,113))- .5)*.9*_PathStitchVariation.x;
                float angle=clamp(_PathStitchVariation.z+(PathWoundHash(float2(stitch,257))*2-1)*_PathStitchVariation.y,-75,75);
                float2 samplePoint=float2(x,coordinate-(stitch+.5+jitter)*stepSize);
                float2 start=float2(-halfSpan,-halfSpan*tan(radians(angle))),end=-start;
                float distance=PathSegment(samplePoint,start,end);
                if(distance<d)
                {
                    d=distance;
                    float2 direction=end-start;
                    t=saturate(dot(samplePoint-start,direction)/max(dot(direction,direction),1e-8));
                }
                holeDistance=min(holeDistance,min(length(samplePoint-start),length(samplePoint-end)));
            }
        }
        float hole=PathBell(holeDistance,_PathStitches.z*_PathPuncture.y);
        float puckerCenter=PathBell(holeDistance,_PathPuncture.w*.65);
        float puckerRidge=PathBell(holeDistance-_PathPuncture.w*.65,_PathPuncture.w*.3);
        float pucker=max(puckerCenter,puckerRidge)*_PathFinish.w;
        float endDistance=min(t,1-t);
        float arch=_PathSutureRelief.y>0 ? pow(smoothstep(0,_PathSutureRelief.y,endDistance),_PathSutureRelief.z) : 1;
        thread=_PathStitches.x==1 ? (1-smoothstep(_PathStitches.z-aa,_PathStitches.z+aa,d))*_PathThreadColor.a : 0;
        float roundThread=sqrt(saturate(1-pow(d/max(_PathStitches.z,.002),2)))*thread;
        height+=roundThread*_PathSutureRelief.x*arch-hole*_PathSutureRelief.w;
        height+=(puckerRidge*.65-puckerCenter*.55)*_PathFinish.w*_PathPuncture.z;
        coverage=max(coverage,max(thread,max(hole,pucker)));
        color*=1-hole*_PathPuncture.x;
        float3 threadColor=_PathThreadColor.rgb*(.65+.35*roundThread)*(1-hole*_PathPuncture.x*(1-arch));
        color=lerp(color,threadColor,thread);
        recess=max(recess,hole*_PathPuncture.x+puckerCenter*_PathFinish.w*.25);
    }
    coverage*=closed?1:smoothstep(0,.012,u)*smoothstep(0,.012,1-u);
    coverage*=1-smoothstep(.46,.5,abs(x));
    return PathMaterial(color,coverage*_PathColor.a,height,roughness,recess,
        position,normal,tangent,uv,width);
}
#endif
