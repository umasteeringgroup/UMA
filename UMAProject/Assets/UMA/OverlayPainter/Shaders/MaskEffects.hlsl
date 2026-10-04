// Ordered grayscale mask sources and filters. Intermediates stay linear and half-float.
SamplerState sampler_linear_clamp;
Texture2D<float4> _StackInput, _StackSource, _StackPaint, _StackFiltered, _StackCurve;
Texture2D<float2> _StackSeedsRead;
RWTexture2D<float2> _StackSeedsWrite;
RWTexture2D<float4> _StackOutput;
int _StackKind, _StackBlend, _StackChannel, _StackInvert, _StackPass, _StackJump;
float _StackOpacity, _StackAngle;
float4 _StackParams, _StackRange, _StackMisc, _StackTransform, _StackDirection, _StackAxis;

float StackSample(float2 uv)
{
    return _StackMisc.w > .5 ? _StackInput.SampleLevel(sampler_linear_repeat, uv, 0).r :
        _StackInput.SampleLevel(sampler_linear_clamp, uv, 0).r;
}
float StackComponent(float4 c)
{
    if (_StackChannel == 1) return c.r;
    if (_StackChannel == 2) return c.g;
    if (_StackChannel == 3) return c.b;
    if (_StackChannel == 4) return c.a;
    return dot(c.rgb, float3(.2126, .7152, .0722));
}
float StackBlend(float a, float b)
{
    if (_StackBlend == 6) return min(a, b);
    if (_StackBlend == 7) return max(a, b);
    if (_StackBlend == 8) return abs(a-b);
    if (_StackBlend == 9) return b < .5 ? a-(1-2*b)*a*(1-a) : a+(2*b-1)*(sqrt(max(0,a))-a);
    if (_StackBlend == 10) return saturate(a/max(.0001,b));
    return BlendMask(a, b, _StackBlend);
}
float2 StackUV(float2 uv)
{
    float s, c; sincos(_StackAngle, s, c); float2 p = uv-.5;
    return float2(c*p.x-s*p.y,s*p.x+c*p.y)*_StackTransform.xy+.5+_StackTransform.zw;
}
float2 StackCell(float2 p)
{
    float2 cell = floor(p); float nearest = 100; float id = 0;
    [unroll] for (int y=-1;y<=1;y++) [unroll] for (int x=-1;x<=1;x++)
    {
        float2 c=cell+float2(x,y);
        float2 q=c+float2(MaskHash(c),MaskHash(c+19.19)); float d=length(q-p);
        if(d<nearest) {nearest=d;id=MaskHash(c+83.17);}
    }
    return float2(saturate(nearest), id);
}
[numthreads(16,16,1)]
void CSMaskStack(uint3 dispatchID : SV_DispatchThreadID)
{
    int2 pixel=dispatchID.xy; if(any(pixel>=_TextureSize)) return;
    float2 uv=(pixel+.5)/_TextureSize, tuv=StackUV(uv);
    float original=_StackInput.Load(int3(pixel,0)).r, value=original;
    float2 sourceUV = _StackKind >= 35 ? uv : tuv;
    float4 source=_StackMisc.w>.5 ? _StackSource.SampleLevel(sampler_linear_repeat,sourceUV,0) :
        _StackSource.SampleLevel(sampler_linear_clamp,sourceUV,0);
    float filtered=_StackFiltered.Load(int3(pixel,0)).r;
    float a=_StackParams.x, amount=_StackParams.y, radius=max(.001,_StackParams.z), threshold=_StackParams.w;
    float gamma=_StackMisc.x, soft=max(.00001,_StackMisc.y);
    int kind=_StackKind;
    if(kind==0) value=a;
    else if(kind==1 || kind==3) value=StackComponent(source);
    else if(kind==2) value=_StackPaint.SampleLevel(sampler_linear_clamp,uv,0).r;
    else if(kind==4) value=saturate((MaskFbm(tuv)-_StackRange.x)/max(.00001,_StackRange.y-_StackRange.x));
    else if(kind==5) value=abs(MaskFbm(tuv)*2-1);
    else if(kind==6) value=StackCell(tuv).x;
    else if(kind==7) value=StackCell(tuv).y;
    else if(kind==8) value=tuv.y;
    else if(kind==9) value=1-length(tuv-.5)*2;
    else if(kind==10) value=smoothstep(threshold-soft,threshold+soft,.5+.5*sin(tuv.x*6.2831853));
    else if(kind==11) value=fmod(abs(floor(tuv.x)+floor(tuv.y)),2);
    else if(kind==12) value=1-smoothstep(threshold-soft,threshold+soft,length(frac(tuv)-.5)*2);
    else if(kind==13) value=1-original;
    else if(kind==14 || kind==21) value=lerp(_StackRange.z,_StackRange.w,pow(saturate((original-_StackRange.x)/max(.00001,_StackRange.y-_StackRange.x)),1/gamma));
    else if(kind==15) value=_StackCurve.SampleLevel(sampler_linear_clamp,float2(original,.5),0).r;
    else if(kind==16) value=(original-.5)*amount+.5+a;
    else if(kind==17) value=pow(saturate(original),1/gamma);
    else if(kind==18) value=smoothstep(threshold-soft,threshold+soft,original);
    else if(kind==19) value=round(original*(_StackMisc.z-1))/(_StackMisc.z-1);
    else if(kind==20) value=clamp(original,_StackRange.x,_StackRange.y);
    else if(kind==22) value=smoothstep(_StackRange.x,_StackRange.y,original);
    else if(kind==23 || kind==24 || kind==27 || kind==28) value=filtered;
    else if(kind==25) value=original+(original-filtered)*amount;
    else if(kind==26) value=.5+(original-filtered)*amount;
    else if(kind==29) value=max(0,filtered-original);
    else if(kind==30)
    {
        float2 d=radius/_TextureSize;
        float dx=StackSample(uv+float2(d.x,0))-StackSample(uv-float2(d.x,0));
        float dy=StackSample(uv+float2(0,d.y))-StackSample(uv-float2(0,d.y));
        value=saturate(length(float2(dx,dy))*amount);
    }
    else if(kind==31 || kind==32)
    {
        float2 seed=_StackSeedsRead.Load(int3(pixel,0));
        float distance=seed.x<0 ? radius*2 : length(seed-pixel);
        float signedDistance=(original>=threshold ? 1 : -1)*distance;
        value=kind==31 ? .5+signedDistance/(radius*2) : smoothstep(-radius,radius,signedDistance);
    }
    else if(kind==33) value=StackSample(tuv);
    else if(kind==34)
    {
        float2 distortion=float2(MaskFbm(tuv),MaskFbm(tuv+31.19))*2-1;
        value=StackSample(uv+distortion*radius/_TextureSize);
    }
    else if(kind==35 || kind==36) value=source.r;
    else if(kind==37) value=source.r*amount;
    else if(kind==38) value=dot(source.xyz*2-1,normalize(_StackDirection.xyz+1e-15))*.5+.5;
    else if(kind==39) value=(dot(source.xyz,_StackDirection.xyz)-_StackRange.x)/max(.00001,_StackRange.y-_StackRange.x);
    else if(kind==40) value=abs(StackComponent(source)-a)<.5 ? 1 : 0;
    else if(kind==41) value=saturate((source.r-.5)*2*amount)*lerp(1,MaskFbm(tuv),threshold);
    else if(kind==42) value=saturate((1-source.r)*amount)*lerp(1,MaskFbm(tuv),threshold);
    else if(kind==43) value=saturate(dot(source.xyz*2-1,normalize(_StackDirection.xyz+1e-15))*amount)*lerp(1,MaskFbm(tuv),threshold);
    value=saturate(value); if(_StackInvert!=0) value=1-value;
    value=saturate(lerp(original,StackBlend(original,value),_StackOpacity));
    _StackOutput[pixel]=float4(value,value,value,1);
}
[numthreads(16,16,1)]
void CSMaskSpatial(uint3 dispatchID : SV_DispatchThreadID)
{
    int2 pixel=dispatchID.xy; if(any(pixel>=_TextureSize)) return;
    float2 uv=(pixel+.5)/_TextureSize;
    int radius=(int)ceil(_StackParams.z); float sum=0, weight=0;
    bool erosion=_StackKind==28, morphology=_StackKind==27 || erosion || _StackKind==29;
    float value=erosion ? 1 : 0;
    float sigma=max(.5,_StackParams.z/3);
    [loop] for(int i=-radius;i<=radius;i++)
    {
        float sample=StackSample(uv+_StackAxis.xy*i/_TextureSize);
        if(morphology) value=erosion ? min(value,sample) : max(value,sample);
        else {float w=exp(-.5*i*i/(sigma*sigma));sum+=sample*w;weight+=w;}
    }
    if(!morphology) value=sum/max(weight,.00001);
    _StackOutput[pixel]=float4(value,value,value,1);
}
[numthreads(16,16,1)]
void CSMaskSeeds(uint3 dispatchID : SV_DispatchThreadID)
{
    int2 p=dispatchID.xy; if(any(p>=_TextureSize)) return;
    bool inside=_StackInput.Load(int3(p,0)).r>=_StackParams.w, edge=false;
    [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
    {int2 q=clamp(p+int2(x,y),0,_TextureSize-1); edge = edge || ((_StackInput.Load(int3(q,0)).r>=_StackParams.w)!=inside);}
    _StackSeedsWrite[p]=edge ? float2(p) : float2(-1,-1);
}
[numthreads(16,16,1)]
void CSMaskJump(uint3 dispatchID : SV_DispatchThreadID)
{
    int2 p=dispatchID.xy; if(any(p>=_TextureSize)) return;
    float best=1e20; float2 nearest=-1;
    [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
    {
        int2 q=p+int2(x,y)*_StackJump; if(any(q<0)||any(q>=_TextureSize)) continue;
        float2 candidate=_StackSeedsRead.Load(int3(q,0)); if(candidate.x<0) continue;
        float2 delta=candidate-p; float d=dot(delta,delta);
        if(d<best) {best=d;nearest=candidate;}
    }
    _StackSeedsWrite[p]=nearest;
}
