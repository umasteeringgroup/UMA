#ifndef UMA_PATH_GENERATOR_INCLUDED
#define UMA_PATH_GENERATOR_INCLUDED
int _PathGeneratorEnabled, _PathChannel;
int _PathWoundType;
float4 _PathColor, _PathHealedColor, _PathRimColor, _PathThreadColor, _PathChannelColor;
float4 _PathShape, _PathRelief, _PathFinish, _PathStitches, _PathTextLayout;
float _PathStitchSlant;
Texture2D<float4> _PathText, _PathRaster, _PathRasterCoverage;
SamplerState path_linear_clamp_sampler;
float4 _PathRasterColor;

float PathBell(float d,float r) { return exp(-d*d/max(r*r,1e-8)); }
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
    float3 position,float3 normal,float4 tangent,float2 uv,float width)
{
    if(_PathGeneratorEnabled==5)
    {
        float2 q=float2(along/span,across);
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
    if(_PathStitches.x>0)
    {
        float count=_PathStitches.y,stepSize=span/count;
        float y=(frac(u*count)-.5)*stepSize;
        float halfSpan=_PathStitches.w*.5;
        float2 a=float2(-halfSpan,-halfSpan*_PathStitchSlant),b=-a;
        float d=PathSegment(float2(x,y),a,b);
        float holeDistance=min(length(float2(x,y)-a),length(float2(x,y)-b));
        float hole=PathBell(holeDistance,_PathStitches.z*1.7);
        float pucker=PathBell(holeDistance,.045)*_PathFinish.w;
        thread=_PathStitches.x==1 ? (1-smoothstep(_PathStitches.z-aa,_PathStitches.z+aa,d))*_PathThreadColor.a : 0;
        float roundThread=sqrt(saturate(1-pow(d/max(_PathStitches.z,.002),2)))*thread;
        height+=roundThread*.05-hole*.025-pucker*.015;
        coverage=max(coverage,max(thread,max(hole,pucker*.5)));
        color=lerp(color,color*.3,hole);color=lerp(color,_PathThreadColor.rgb*(.65+.35*roundThread),thread);
        recess=max(recess,hole*.7+pucker*.25);
    }
    coverage*=closed?1:smoothstep(0,.012,u)*smoothstep(0,.012,1-u);
    coverage*=1-smoothstep(.46,.5,abs(x));
    return PathMaterial(color,coverage*_PathColor.a,height,lerp(_PathFinish.x,.75,heal),recess,
        position,normal,tangent,uv,width);
}
#endif
