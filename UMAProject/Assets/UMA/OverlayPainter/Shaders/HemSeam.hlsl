// Procedural garment detail in intrinsic ribbon coordinates. No destination/fabric
// sampling: the albedo stores an affine grayscale adjustment plus colored thread.
#ifndef UMA_HEM_SEAM_INCLUDED
#define UMA_HEM_SEAM_INCLUDED
int _HemEnabled, _HemChannel, _HemRowCount;
float4 _HemShape, _HemRope, _HemCloth, _HemFinish, _HemDetails;
float4 _HemResponse;
float4 _HemRowsA[8], _HemRowsB[8], _HemRowColors[8];
#define HEM_TAU 6.28318530718
float HemBell(float x, float width) { x /= max(width, .0001); return exp(-x*x*2.0); }
float HemSegment(float2 p, float2 a, float2 b)
{
    float2 v=b-a;
    return length(p-a-v*saturate(dot(p-a,v)/max(dot(v,v),1e-8)));
}
// Positive distance is the exposed cloth beneath a folded edge. The shadow is
// strongest immediately below the lip and falls away on that side only.
float HemOverlap(float distance, float width, float aa)
{
    return HemBell(max(distance,0),width)*smoothstep(-aa,aa,distance);
}
float HemFrequency(float frequency, float span, bool closed)
{
    return closed ? max(1.0,round(frequency*span))/span : frequency;
}
float HemHash(float index)
{
    uint value=(uint)(int)index ^ (uint)_HemFinish.w;
    value^=value>>16;value*=0x7feb352du;value^=value>>15;value*=0x846ca68bu;value^=value>>16;
    return (value & 0x00ffffffu)/16777215.0;
}
float HemNoise(float along,float frequency,float span,bool closed)
{
    float cells=max(1.0,round(frequency*span));
    float position=closed ? along*cells/span : along*frequency;
    float index=floor(position),next=index+1;
    if(closed) { index=index-floor(index/cells)*cells;next=next-floor(next/cells)*cells; }
    float fraction=frac(position);fraction=fraction*fraction*(3-2*fraction);
    return lerp(HemHash(index),HemHash(next),fraction);
}
struct HemSample
{
    float height, coverage, thread, recess, wear, protectedCloth, roughness, surfaceShade;
    float3 threadColor;
};
HemSample EvaluateHem(float across, float along, float span, bool closed, float pixelWidth)
{
    HemSample o=(HemSample)0;
    float x=(across-.5)*_HemDetails.z-_HemShape.z;
    float halfBand=_HemShape.y*.5;
    float edge=abs(x)-halfBand;
    float aa=max(pixelWidth,.0005);
    float band=1-smoothstep(-.018-aa,.018+aa,edge);
    float seed=_HemFinish.w*.1237;
    // Integer harmonics on closed paths join without a random-noise discontinuity.
    float noise=sin(along*HEM_TAU*HemFrequency(.37,span,closed)+seed)*.55 +
        sin(along*HEM_TAU*HemFrequency(.91,span,closed)+seed*2.37)*.3;
    float bunch=HemNoise(along,_HemRope.y*.7,span,closed);
    float rope=sin(HEM_TAU*(along*HemFrequency(_HemRope.y,span,closed)+x*_HemRope.z)+
        (noise*2.0+(bunch-.5)*3.0)*_HemRope.w+seed);
    rope*=lerp(1.0,.5+bunch,_HemRope.w);
    float fold=0, recess=0;
    int profile=(int)_HemShape.x;
    if(profile==1)
    {
        // Cloth rolls gradually into the turn on the left. The tucked-under edge
        // on the right has a small rounded lip and a contact shadow below it.
        float turn=smoothstep(-halfBand-.025,-halfBand+.06,x);
        float lip=1-smoothstep(halfBand-.018-aa,halfBand+.008+aa,x);
        fold=(.36+.09*HemBell(x-halfBand+.027,.045))*turn*lip;
        recess=HemOverlap(x-halfBand,.04,aa)*.8;
        o.surfaceShade=(HemBell(x+halfBand,.045)*.13+HemBell(x-halfBand+.012,.022)*.18)*band;
    }
    else if(profile==2)
    {
        // Right-hand panel laps over the left at x=0. Its far side blends back
        // into the original fabric rather than acquiring a second black rim.
        fold=.42*smoothstep(-.012-aa,.014+aa,x)*band;
        recess=HemOverlap(-x,.043,aa)*.9;
        o.surfaceShade=HemBell(x-.012,.024)*.2*band;
    }
    else if(profile==3)
    {
        fold=(.16*HemBell(abs(x)-.085,.09)-.2*HemBell(x,.023))*band;
        recess=HemBell(x,.027)*.7;
        o.surfaceShade=HemBell(abs(x)-.045,.027)*.11*band;
    }
    else if(profile==4)
    {
        fold=.65*HemBell(x,halfBand*.95);
        recess=HemOverlap(x-halfBand*.65,.038,aa)*.6;
        o.surfaceShade=(1-HemBell(x,halfBand*.85))*.22*band;
    }
    else if(profile==5)
    {
        // A cord is rounded on both sides, unlike an overlapped panel.
        fold=sqrt(saturate(1-pow(x/max(halfBand,.001),2)))*band;
        recess=HemOverlap(edge,.03,aa)*.7;
        o.surfaceShade=(1-sqrt(saturate(1-pow(x/max(halfBand,.001),2))))*.38*band;
    }
    else if(profile==6)
    {
        fold=.1*band;
        recess=HemOverlap(x-halfBand,.023,aa)*.25;
    }
    float cloth=profile==0 ? 0 : band;
    // Tension bunches cloth near the folded/stitched edge. A full-width sine
    // produces painted diagonal stripes, especially on dark denim.
    float ropeAxis=(profile==1 || profile==6) ? halfBand-.055 : 0;
    float ropeEnvelope=cloth*(.2+.8*HemBell(x-ropeAxis,.12));
    float ropeHeight=rope*_HemRope.x*.28*ropeEnvelope;
    o.height=fold+ropeHeight;
    o.coverage=saturate(cloth+recess);
    o.recess=saturate(recess+max(0,-rope)*_HemRope.x*ropeEnvelope*.55);
    // White is an affine wear contribution: even a small amount strongly lifts
    // a dark fabric. Reserve it for raised fibers instead of bleaching ridges.
    o.wear=saturate(max(0,rope)*_HemRope.x*.18*ropeEnvelope+
        max(0,fold)*.15*cloth+HemBell(edge+.012,.024)*cloth*.08);
    o.protectedCloth=saturate(o.recess);
    [loop] for(int i=0;i<_HemRowCount;i++)
    {
        float4 a=_HemRowsA[i], b=_HemRowsB[i];
        float pitch=closed ? span/max(1.0,round(span/a.z)) : a.z;
        float phase=along/pitch+b.z;
        float v=(frac(phase)-.5)*pitch;
        float rx=x-a.x;
        float2 p=float2(rx,v);
        float len=pitch*a.w*.5;
        float distance=HemSegment(p,float2(0,-len),float2(0,len));
        float needleDistance=min(length(p-float2(0,-len)),length(p-float2(0,len)));
        int kind=(int)b.x;
        if(kind==1)
        {
            // Overlapping loops narrow where the next needle catches them. Both
            // neighboring loops participate at the repeat boundary.
            float loopWidth=max(b.y*.5,.005)*(.8-.2*cos(HEM_TAU*v/pitch)), loopLength=pitch*.56;
            float2 q=float2(rx/loopWidth,v/loopLength);
            float ellipse=length(q);
            distance=abs(ellipse-1)/max(length(q/float2(loopWidth,loopLength))/max(ellipse,.001),.001);
            float neighborV=v-(v<0 ? -pitch : pitch);
            q=float2(rx/loopWidth,neighborV/loopLength);
            ellipse=length(q);
            distance=min(distance,abs(ellipse-1)/max(length(q/float2(loopWidth,loopLength))/max(ellipse,.001),.001));
            distance=min(distance,HemSegment(p,float2(0,-pitch*.5),float2(0,-pitch*.3)));
            distance=min(distance,HemSegment(p,float2(0,pitch*.3),float2(0,pitch*.5)));
            needleDistance=length(float2(rx,abs(v)-pitch*.5));
        }
        else if(kind==2 || kind==3 || kind==4)
        {
            float halfSpan=b.y*.5;
            distance=min(HemSegment(p,float2(-halfSpan,-pitch*.5),float2(halfSpan,0)),
                HemSegment(p,float2(halfSpan,0),float2(-halfSpan,pitch*.5)));
            needleDistance=min(length(p-float2(halfSpan,0)),length(float2(rx+halfSpan,abs(v)-pitch*.5)));
            if(kind==3)
            {
                // Short needle bites anchor the looper; a solid center stripe
                // would look like a second cord rather than overlock stitching.
                distance=min(distance,HemSegment(p,float2(-halfSpan,-len),float2(-halfSpan,len)));
                needleDistance=min(needleDistance,length(float2(rx+halfSpan,abs(v)-len)));
            }
            if(kind==4) distance=min(distance,min(HemSegment(p,float2(halfSpan,-pitch*.5),float2(-halfSpan,0)),
                HemSegment(p,float2(-halfSpan,0),float2(halfSpan,pitch*.5))));
            if(kind==4) needleDistance=min(needleDistance,min(length(p-float2(-halfSpan,0)),length(float2(rx-halfSpan,abs(v)-pitch*.5))));
        }
        else if(kind==5)
        {
            distance=HemSegment(p,float2(0,-pitch*.08),float2(b.y*.2,pitch*.08));
            needleDistance=min(length(p-float2(0,-pitch*.08)),length(p-float2(b.y*.2,pitch*.08)));
        }
        else if(kind==6)
        {
            distance=HemSegment(p,float2(-b.y*.5,0),float2(b.y*.5,0));
            needleDistance=min(length(p-float2(-b.y*.5,0)),length(p-float2(b.y*.5,0)));
        }
        float opacity=_HemRowColors[i].a;
        float radius=max(a.y*.5,.0001);
        float seated=smoothstep(0,radius*3,needleDistance);
        float threadRadius=radius*lerp(.7,1,seated);
        float threadMask=(1-smoothstep(threadRadius-aa*.65,threadRadius+aa*.65,distance))*saturate(threadRadius/aa);
        float thread=threadMask*opacity;
        float roundThread=sqrt(saturate(1-pow(distance/threadRadius,2)));
        // Preserve sub-pixel thread volume rather than letting its height pop
        // on/off when a narrow stitch moves between destination texels.
        roundThread=lerp(threadMask*.7,roundThread,saturate(threadRadius/(aa*1.6)));
        float hole=HemBell(needleDistance,radius*1.8+aa*.45)*opacity;
        float contact=HemBell(max(distance-threadRadius*.75,0),radius*.9+aa*.55)*(1-threadMask*.72)*opacity;
        float puckerEnvelope=HemBell(needleDistance,.055+radius*2)*opacity;
        float pucker=(HemBell(needleDistance,.09+radius*2)*.3*opacity-puckerEnvelope)*_HemDetails.x;
        o.height+=pucker*.09+roundThread*b.w*.22*lerp(.3,1,seated)*opacity-hole*.075;
        o.recess=max(o.recess,saturate(hole*.8+contact*.55+max(0,-pucker)*.3));
        o.protectedCloth=max(o.protectedCloth,saturate(hole+contact*.75));
        o.coverage=max(o.coverage,saturate(thread+hole+contact+puckerEnvelope*_HemDetails.x));
        // Tiny twisted-fiber variation, filtered before it can alias. All along
        // frequencies fit whole cycles on a closed ribbon.
        float fiberFrequency=HemFrequency(150,span,closed);
        float fiberFilter=saturate(1-fiberFrequency*aa*.8);
        float strand=sin(HEM_TAU*(along*fiberFrequency+rx/max(a.y,.002)*.65)+seed+i)*fiberFilter;
        float threadTone=(.65+.35*roundThread)*(.96+.04*strand)*lerp(.68,1,seated);
        float3 threadColor=_HemRowColors[i].rgb*pow(max(threadTone,.1),_HemResponse.y);
        // Ordered rows permit a contrasting looper under a needle/topstitch row.
        float combined=thread+o.thread*(1-thread);
        o.threadColor=(threadColor*thread+o.threadColor*o.thread*(1-thread))/max(combined,1e-6);
        o.thread=combined;
    }
    float frayFrequency=HemFrequency(75,span,closed);
    float fiberPhase=along*frayFrequency, fiberIndex=floor(fiberPhase);
    if(closed)
    {
        float fiberCount=max(1,round(frayFrequency*span));
        fiberIndex-=floor(fiberIndex/fiberCount)*fiberCount;
    }
    float fiberNoise=HemHash(fiberIndex+917);
    float fiberLength=.016+.065*fiberNoise;
    float fiberLean=(HemHash(fiberIndex+1237)-.5)/frayFrequency;
    float fiberDistance=HemSegment(float2(x-halfBand,(frac(fiberPhase)-.5)/frayFrequency),
        float2(-.004,0),float2(fiberLength,fiberLean));
    float fibers=(1-smoothstep(.001,.001+aa*.8,fiberDistance))*saturate(.002/aa)*_HemDetails.y*(.4+.6*fiberNoise);
    // Fray remains inside the ribbon envelope; it does not alter the mesh silhouette.
    o.wear=max(o.wear,fibers);o.height+=fibers*.08;o.coverage=max(o.coverage,fibers);
    o.wear*=_HemCloth.y*(1-saturate(o.protectedCloth*_HemCloth.z))*lerp(1,.6+bunch*.6,_HemRope.w);
    o.roughness=saturate(lerp(_HemFinish.x+noise*.025+o.recess*.035-o.wear*.06,_HemFinish.y,o.thread));
    // Reserve space for every enabled row before shaping. High relief and
    // crossing stitches must not clip the resulting height into a white plateau.
    float maximumProfile=1.5;
    [loop] for(int row=0;row<_HemRowCount;row++)maximumProfile+=_HemRowsB[row].w*.22+.16;
    float budget=.44/maximumProfile;
    float requested=_HemShape.w*_HemResponse.x;
    o.height*=budget*requested/(budget+requested);
    o.coverage*=1-smoothstep(.46,.5,abs(across-.5));
    return o;
}
float4 ShadeHem(float across, float along, float span, bool closed, float pixelWidth,
    float3 worldPosition, float3 worldNormal, float4 worldTangent, float2 uv, float ribbonWidth)
{
    HemSample s=EvaluateHem(across,along,span,closed,pixelWidth);
        float dark=_HemResponse.y>0 ? 1-pow(1-saturate((s.recess+s.surfaceShade)*_HemCloth.x+s.protectedCloth*_HemCloth.z*.12),_HemResponse.y) : 0;
    float wear=saturate(s.wear);
    if(_HemChannel==0)
    {
        // Underlying*(1-dark)*(1-wear)*(1-thread) + white*wear*(1-thread) + threadColor*thread.
        // Encode as straight RGBA for ordinary Normal layer compositing. Neutral is transparent.
        float alpha=1-(1-dark)*(1-wear)*(1-s.thread);
        float3 added=wear*(1-s.thread)+s.threadColor*s.thread;
        return float4(added/max(alpha,1e-6),alpha*s.coverage);
    }
    if(_HemChannel==4) return float4(0,0,0,saturate(s.recess*_HemCloth.w*_HemResponse.y)*s.coverage);
    if(_HemChannel==3) return float4(s.roughness.xxx,s.coverage);
    if(_HemChannel==10) return float4(saturate(.5+s.height).xxx,s.coverage);
    if(_HemChannel==6) return float4(s.wear,s.protectedCloth,saturate(s.recess*_HemResponse.y),s.coverage);
    if(_HemChannel==1)
    {
        // A scalar height field on the destination surface, expressed in its actual tangent
        // frame. This handles curved ribbons, rotated UV islands and mirrored tangents.
        float3 n=normalize(worldNormal), dx=ddx(worldPosition), dy=ddy(worldPosition);
        float3 rx=cross(dy,n), ry=cross(n,dx);
        float determinant=dot(dx,rx);
        float scale=abs(determinant)>1e-12 ? rcp(determinant) : 0;
        float3 gradient=(ddx(s.height)*rx+ddy(s.height)*ry)*scale*ribbonWidth*_HemFinish.z;
        float3 bumped=normalize(n-gradient);
        float3 t=worldTangent.xyz;
        float signT=worldTangent.w;
        if(dot(t,t)<.01)
        {
            float2 uvx=ddx(uv), uvy=ddy(uv);
            float d=uvx.x*uvy.y-uvx.y*uvy.x;
            t=(dx*uvy.y-dy*uvx.y)*(d<0 ? -1 : 1);
            float3 bitangent=(dy*uvx.x-dx*uvy.x)*(d<0 ? -1 : 1);
            signT=dot(cross(n,t),bitangent)<0 ? -1 : 1;
        }
        t=normalize(t-n*dot(t,n));
        float3 b=normalize(cross(n,t))*signT;
        float3 tangentNormal=normalize(float3(dot(bumped,t),dot(bumped,b),dot(bumped,n)));
        return float4(tangentNormal*.5+.5,s.coverage);
    }
    return 0;
}
#endif
