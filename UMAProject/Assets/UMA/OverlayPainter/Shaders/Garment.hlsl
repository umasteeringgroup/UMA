#ifndef UMA_GARMENT_INCLUDED
#define UMA_GARMENT_INCLUDED
int _GarmentEnabled,_GarmentKind,_GarmentVariant,_GarmentChannel,_GarmentHasMotif;
float4 _GarmentSize,_GarmentShape,_GarmentFold,_GarmentAging,_GarmentStitch,_GarmentFinish,_GarmentDetail,_GarmentPrint;
float4 _GarmentThread,_GarmentCloth,_GarmentAccent,_GarmentFoldRead,_GarmentProtectionRead;
float4 _GarmentEnds;
float _GarmentZipperWidth,_GarmentZipperDepth;
float _GarmentDepth,_GarmentShading;
sampler2D _GarmentMotif,_GarmentFoldInput,_GarmentProtectionInput;
#define GARMENT_TAU 6.28318530718
float GarmentHash(float2 p)
{
    uint h=(uint)(int)p.x*0x8da6b343u ^ (uint)(int)p.y*0xd8163841u ^ (uint)_GarmentFinish.w;
    h^=h>>16;h*=0x7feb352du;h^=h>>15;h*=0x846ca68bu;h^=h>>16;
    return (h & 0x00ffffffu)/16777215.0;
}
float GarmentNoise(float2 p)
{
    float2 i=floor(p),t=frac(p);t=t*t*(3-2*t);
    return lerp(lerp(GarmentHash(i),GarmentHash(i+float2(1,0)),t.x),
        lerp(GarmentHash(i+float2(0,1)),GarmentHash(i+1),t.x),t.y);
}
float GarmentBell(float x,float width){x/=max(width,.00001);return exp(-x*x*2);}
float GarmentBox(float2 p,float2 size,float radius)
{float2 q=abs(p)-size+radius;return length(max(q,0))+min(max(q.x,q.y),0)-radius;}
// Evaluate longitudinal end distances in the same ribbon-width units as side
// distances. Otherwise a long path stretches its end stitches and AO into bars.
float GarmentStrip(float2 p,float2 size,float radius,float4 openEdges,out float along)
{
    float width=max(_GarmentSize.x,.0001),height=max(_GarmentSize.y,.0001);
    float cs=cos(_GarmentShape.y),sn=sin(_GarmentShape.y);
    float2 units=float2(rsqrt(cs*cs/(width*width)+sn*sn/(height*height)),
        rsqrt(sn*sn/(width*width)+cs*cs/(height*height)));
    float2 negative=(-p-size)*units/width,positive=(p-size)*units/width;
    negative=float2(openEdges.x>.5 ? -100000 : negative.x,openEdges.z>.5 ? -100000 : negative.y);
    positive=float2(openEdges.y>.5 ? -100000 : positive.x,openEdges.w>.5 ? -100000 : positive.y);
    float2 edge=max(negative,positive);
    along=edge.x>edge.y ? p.y*units.y : p.x*units.x;
    float2 q=edge+radius;
    return length(max(q,0))+min(max(q.x,q.y),0)-radius;
}
float GarmentLine(float2 p,float2 a,float2 b)
{float2 d=b-a;return length(p-a-d*saturate(dot(p-a,d)/max(dot(d,d),1e-12)));}
float GarmentCoverage(float distance,float aa){return 1-smoothstep(-aa,aa,distance);}
// Contact belongs on the receiving surface just beyond the object, with only a
// narrow dark lip on its bevel. Filter the footprint before subpixel teeth or
// threads lose their seating shadow. Negative distances are inside the object.
float GarmentContact(float distance,float radius,float coverage,float aa)
{
    float spread=sqrt(radius*radius+aa*aa*.64);
    float outside=GarmentBell(max(distance,0),spread)*(1-coverage);
    float lip=GarmentBell(distance,max(radius*.65,aa*.7))*coverage*.2;
    return saturate(outside+lip);
}
// Fade unresolved fibers instead of turning subpixel threads into bright, solid stripes.
float GarmentWave(float phase)
{return cos(phase*GARMENT_TAU)*(1-smoothstep(.2,.55,fwidth(phase)));}
float GarmentYarn(float phase)
{return lerp(.19638,pow(saturate(.5+.5*cos(phase*GARMENT_TAU)),8),1-smoothstep(.2,.55,fwidth(phase)));}
float GarmentWeave(float2 world,float pitch)
{
    float2 phase=world/max(pitch,.00005);
    return GarmentWave(phase.x)*GarmentWave(phase.y)*.5+GarmentWave(phase.x+phase.y*.5)*.25;
}
float GarmentLoopWeave(float2 world,float pitch,float2 period,float blend)
{return lerp(GarmentWeave(world,pitch),GarmentWeave(world-period,pitch),blend);}
float GarmentRead(float4 pixel,float4 settings,float neutral)
{
    if(settings.x<.5)return neutral;
    int component=(int)settings.y;
    float v=component==1 ? pixel.a : component==3 ? pixel.r : component==4 ? pixel.g : component==5 ? pixel.b :
        component==6 ? pixel.r : dot(pixel.rgb,float3(.2126,.7152,.0722));
    if(settings.z>.5)v=1-v;
    return lerp(neutral,v,saturate(settings.w*(component==1 ? 1 : pixel.a)));
}
struct GarmentSample
{float height,coverage,dark,wear,material,roughness,metal,ao,protection;float3 color;};
bool GarmentIsZipper(){return _GarmentKind==3 && _GarmentVariant<=1;}
float GarmentDepthScale(){return max(0,_GarmentDepth);}
float GarmentDepthShading()
{
    float depth=GarmentDepthScale();
    // Preserve authored zipper depth's original combined response. The shared
    // controls on every other construction keep lighting and height independent.
    return (GarmentIsZipper() ? 2*depth/(1+depth) : 1)*_GarmentShading;
}
float GarmentTone(float unshadedTone){return max(.08,1+(unshadedTone-1)*GarmentDepthShading());}
float GarmentRoundedEdge(float distance,float width)
{float t=saturate(-distance/max(width,.00001));return sqrt(saturate(t*(2-t)));}
float GarmentRelief()
{
    // Washes and printing modify the existing material, not the fabric's shape.
    if(_GarmentKind==1 || (_GarmentKind==5 && _GarmentVariant==2))return 0;
    float requested=_GarmentSize.w*GarmentDepthScale();
    // Limit the base amplitude before shaping the hardware, not the finished
    // pixels. Large authored heights otherwise clip every crown to flat white.
    // Leave room for the pull (2.65x) and up to three overlapping thread rows.
    float fraction=_GarmentKind==0 ? .24 : _GarmentKind==2 ? .12 :
        _GarmentKind==4 ? .15 : _GarmentKind==5 ? .075 : .11;
    float budget=max(_GarmentSize.x,.0001)*fraction;
    return budget*requested/(budget+requested);
}
void GarmentColor(inout GarmentSample s,float coverage,float3 color,float roughness,float metallic)
{
    coverage=saturate(coverage);float combined=coverage+s.material*(1-coverage);
    s.color=(color*coverage+s.color*s.material*(1-coverage))/max(combined,1e-6);
    s.material=combined;s.roughness=lerp(s.roughness,roughness,coverage);s.metal=lerp(s.metal,metallic,coverage);
    s.coverage=max(s.coverage,coverage);
}
void GarmentBorder(inout GarmentSample s,float distance,float along,float aa,bool closed)
{
    float depth=GarmentDepthScale(),depthShading=GarmentDepthShading();
    float contactShading=GarmentIsZipper() ? depthShading : 1;
    float width=max(_GarmentSize.x,.0001),halfWidth=_GarmentStitch.x*.5/width;
    float pitch=max(_GarmentStitch.y,_GarmentStitch.x*3);
    float count=max(1,round(_GarmentSize.y/pitch));
    if(closed)pitch=_GarmentSize.y/count;
    float halfLength=pitch*.32/width;
    float phase=along/pitch,cell=floor(phase),local=(frac(phase)-.5)*pitch/width;
    if(closed)cell=cell-floor(cell/count)*count;
    float jitter=(GarmentHash(float2(cell,31))-.5)*_GarmentShape.z*halfWidth*.65;
    [unroll]for(int row=0;row<3;row++)
    {
        if(row>=_GarmentStitch.z)break;
        float across=distance+_GarmentStitch.w*(row+1)+jitter;
        float d=length(float2(across,max(abs(local)-halfLength,0)));
        float resolved=saturate(halfWidth/max(aa,.00001));
        float threadCoverage=GarmentCoverage(d-halfWidth,aa)*resolved;
        float stitch=threadCoverage*_GarmentThread.a;
        float needleDistance=length(float2(across,abs(local)-halfLength));
        float endContact=1-smoothstep(halfLength+halfWidth*.5,halfLength+halfWidth*1.5,abs(local));
        float needle=GarmentBell(needleDistance,max(halfWidth*1.65,aa*.7))*_GarmentThread.a*endContact;
        float contact=GarmentContact(d-halfWidth,halfWidth*1.6,threadCoverage,aa)*_GarmentThread.a*endContact;
        float shadow=saturate(contact*.72+needle*.58);
        s.dark=max(s.dark,shadow*_GarmentAging.y*1.3*contactShading);
        s.ao=max(s.ao,shadow*contactShading);s.coverage=max(s.coverage,shadow);
        float roundness=sqrt(saturate(1-d*d/max(halfWidth*halfWidth,1e-10)));
        roundness=lerp(.7,roundness,resolved);
        float twistPitch=max(_GarmentStitch.x*2,.0001);
        if(closed)twistPitch=_GarmentSize.y/max(1,round(_GarmentSize.y/twistPitch));
        float twist=GarmentWave(along/twistPitch+across/max(halfWidth,.00001)*.4);
        float seated=1-needle*.24;
        float threadTone=(.68+.32*roundness)*(1+twist*.045)*seated;
        threadTone=max(.1,1+(threadTone-1)*depthShading);
        GarmentColor(s,stitch,_GarmentThread.rgb*threadTone,.66,0);
        float arch=1-smoothstep(halfLength*.6,halfLength+halfWidth,abs(local));
        float threadRelief=min(_GarmentStitch.x*.65*depth,GarmentRelief()*.5);
        s.height+=threadRelief*(stitch*roundness*arch-needle*.18);
        s.protection=max(s.protection,contact*.8);
    }
}
void GarmentHardware(inout GarmentSample s,float distance,float bevel,float elevation,float3 color,float roughness,float metal,float aa)
{
    bool zipper=GarmentIsZipper();
    float depthShading=GarmentDepthShading();
    float contactShading=zipper ? depthShading : 1;
    float coverage=GarmentCoverage(distance,aa),top=smoothstep(0,bevel,-distance);
    if(zipper)
    {
        float bevelPosition=saturate(-distance/max(bevel*1.8,.00001));
        top=sqrt(saturate(bevelPosition*(2-bevelPosition)));
        // Ordered hardware covers the color and contact shadows below it.
        // In particular, a stop may show through the pull's hole, not its metal.
        s.dark*=1-coverage;s.ao*=1-coverage;s.wear*=1-coverage;
    }
    float contact=GarmentContact(distance,bevel*1.1,coverage,aa);
    // The bevel remains visibly darker than its crown even when the receiving
    // material has no AO texture slot. Its height still supplies actual lighting.
    float crownTone=zipper ? .48+.52*top : .58+.42*GarmentRoundedEdge(distance,bevel*1.5);
    crownTone=max(.1,1+(crownTone-1)*depthShading);
    GarmentColor(s,coverage,color*crownTone,lerp(saturate(roughness+.18*depthShading),roughness,top),metal);
    float shapeHeight=zipper ? max(0,elevation-GarmentRelief()*.65)+GarmentRelief()*.65*top :
        max(s.height,elevation*(.22+.78*GarmentRoundedEdge(distance,bevel*1.5)));
    s.height=lerp(s.height,shapeHeight,coverage);
    s.ao=max(s.ao,contact*.88*contactShading);s.dark=max(s.dark,contact*_GarmentAging.y*1.45*contactShading);
    s.coverage=max(s.coverage,contact*.88);
}
GarmentSample EvaluateGarment(float2 uv,float2 destinationUV,float aa,bool closed)
{
    GarmentSample s=(GarmentSample)0;s.roughness=_GarmentFinish.x;
    float relief=GarmentRelief();
    float2 p=uv-.5;float cs=cos(_GarmentShape.y),sn=sin(_GarmentShape.y);
    p=float2(p.x*cs-p.y*sn,p.x*sn+p.y*cs);
    float2 world=p*_GarmentSize.xy;
    float2 loopPeriod=float2(-sn*_GarmentSize.x,cs*_GarmentSize.y);
    float loopBlend=closed?smoothstep(0,1,uv.y):0;
    float size=max(_GarmentSize.z,.0001);
    bool openStart=closed || _GarmentEnds.x>.5,openEnd=closed || _GarmentEnds.y>.5;
    // A quarter-turn swaps the pattern's axes, but Start and End still refer to
    // the ribbon's first and last points. The dominant rotated edge is the cap.
    float4 openEdges=0;
    if(abs(sn)>abs(cs))
        openEdges.xy=sn>0 ? float2(openEnd,openStart) : float2(openStart,openEnd);
    else
        openEdges.zw=cs>0 ? float2(openStart,openEnd) : float2(openEnd,openStart);
    float edge=min(min(openEdges.x>.5 ? 1 : p.x+.5,openEdges.y>.5 ? 1 : .5-p.x),
        min(openEdges.z>.5 ? 1 : p.y+.5,openEdges.w>.5 ? 1 : .5-p.y));
    float envelope=smoothstep(0,_GarmentShape.w,edge);
    float noise=GarmentNoise(world/max(size*.7,.0001));
    if(closed)noise=lerp(noise,GarmentNoise((world-loopPeriod)/max(size*.7,.0001)),loopBlend);
    float variation=lerp(1,.55+noise,_GarmentShape.z);
    int variant=_GarmentVariant;
    float inputFold=GarmentRead(tex2Dlod(_GarmentFoldInput,float4(destinationUV,0,0)),_GarmentFoldRead,.5);
    float protectedInput=GarmentRead(tex2Dlod(_GarmentProtectionInput,float4(destinationUV,0,0)),_GarmentProtectionRead,0);
    if(_GarmentKind==0)
    {
        float2 delta=(p+.5-_GarmentFold.xy)*_GarmentSize.xy;
        float phase=world.y/size;
        float fade=envelope;
        if(variant==0)
        {
            float angle=atan2(delta.x,max(.00001,delta.y));
            phase=angle/max(.04,_GarmentFold.z)*3;
            fade*=smoothstep(0,size,length(delta))*saturate(1-abs(angle)/max(.05,_GarmentFold.z));
            fade*=lerp(1,saturate(1-length(delta)/max(_GarmentSize.y,.001)),_GarmentFold.w);
        }
        else if(variant==1 || variant==2)
        {
            phase+=p.x*p.x*(variant==2 ? 6 : 2)*_GarmentFold.z;
            fade*=lerp(1,GarmentBell(p.y,.32),_GarmentFold.w);
            if(variant==2)phase+=sin(p.x*9)*.3;
        }
        else
        {
            phase=world.x/size;
            if(variant==3)
            {
                phase+=sin(p.y*5+p.x*3)*_GarmentShape.z*.45;
                if(!closed)fade*=lerp(1,1-smoothstep(-.45,.5,p.y),_GarmentFold.w*.8);
            }
        }
        if(closed)phase=uv.y*max(1,round(_GarmentSize.y/size))+p.x*p.x*2;
        phase+=(noise-.5)*_GarmentShape.z*.6;
        float wave=sin(phase*GARMENT_TAU);
        // Broad cloth ridges roll into narrower creases; avoid uniform corrugated sine waves.
        if(variant==4)
        {
            float pleat=frac(phase);
            wave=smoothstep(.08,.28,pleat)-smoothstep(.70,.94,pleat);
            wave=wave*2-1;
        }
        else
        {
            wave=wave>0 ? pow(wave,.72) : -pow(-wave,1.7);
            float modulation=lerp(GarmentNoise(float2(world.x/size*.4,world.y/size*.17)),
                GarmentNoise((world-loopPeriod)/size*float2(.4,.17)),loopBlend);
            variation*=lerp(1,.68+modulation*.55,_GarmentShape.z);
        }
        s.height=wave*relief*variation*fade;
        // Contact belongs in compressed valleys, not across the whole shadowed
        // half of a fold. Rounded crowns remain available for material lighting.
        float crease=pow(saturate(-wave),1.65);
        s.coverage=fade;s.dark=crease*_GarmentAging.y*.65;
        s.wear=max(0,wave)*_GarmentAging.x*.3;s.ao=crease*.55*fade;s.protection=crease*fade;
    }
    else if(_GarmentKind==1)
    {
        float pattern=GarmentBell(p.x,.3)*GarmentBell(p.y,.45);
        float washPhase=world.y/size+(noise-.5)*_GarmentShape.z*.4;
        if(variant==1)pattern=pow(saturate(.5+.5*GarmentWave(washPhase+p.x*p.x*5)),5)*GarmentBell(p.x,.34)*GarmentBell(p.y,.38);
        if(variant==2)
        {
            float left=pow(saturate(.5+.5*GarmentWave(washPhase+abs(p.x)*3)),7);
            float right=pow(saturate(.5+.5*GarmentWave(washPhase-abs(p.x)*3+.5)),7);
            pattern=max(left,right*.75)*GarmentBell(p.x,.36)*GarmentBell(p.y,.4);
        }
        if(variant==3)pattern=GarmentBell(GarmentBox(p,float2(.32,.35),.045),.03)*(.35+noise*.65);
        if(variant==4)pattern=saturate(.5-p.y)*(.4+.6*noise);
        float raised=saturate((inputFold-.5)*14),valley=saturate((.5-inputFold)*14);
        float protectedArea=saturate(protectedInput*_GarmentAging.w+valley*.6);
        s.wear=saturate((pattern*.3+raised*.7)*_GarmentAging.x*(1-protectedArea)*variation);
        s.dark=saturate((valley*_GarmentAging.y*_GarmentShading+_GarmentAging.z*noise*(variant==4 ? pattern : .18))*(1-s.wear));
        s.coverage=envelope;s.ao=valley*_GarmentAging.y*_GarmentShading;s.protection=protectedArea;
        float grain=GarmentLoopWeave(world,max(size*.09,.0002),loopPeriod,loopBlend);
        s.wear*=1+grain*.12;s.dark*=1-grain*.08;
        s.roughness=saturate(_GarmentFinish.x+s.wear*.15-_GarmentAging.z*noise*.15);
    }
    else if(_GarmentKind==2)
    {
        float along;
        float d=GarmentStrip(p,float2(.39,.4),.035,0,along);
        if(variant==0 || variant==2)d=max(d,(-p.y-.35+abs(p.x)*.4)*.9);
        if(variant==1)d=GarmentStrip(p,float2(.4,.09),.01,0,along);
        if(variant==3)d=GarmentStrip(p,float2(.48,.32),.008,openEdges,along);
        if(variant==4)d=GarmentStrip(p,float2(.39,.4),.035,openEdges,along);
        float body=GarmentCoverage(d,aa),bevel=GarmentBell(d,.025);
        s.coverage=saturate(body+GarmentBell(d-.016,.018)*.8)*envelope;
        float grain=GarmentLoopWeave(world,max(_GarmentStitch.x*2,.0002),loopPeriod,loopBlend);
        float billow=GarmentBell(p.x,.36)*GarmentBell(p.y,.38);
        s.height=body*relief*(.18+.82*GarmentRoundedEdge(d,.045)+billow*.15+grain*.025);
        float contact=GarmentContact(d,.021,body,aa);
        s.dark=contact*_GarmentAging.y;
        s.wear=bevel*_GarmentAging.x*.4;s.ao=contact*.7;
        float opening=(variant==1 ? GarmentBox(p,float2(.35,_GarmentDetail.x*.5),.008) : abs(p.y-.33+p.x*p.x*.12)-_GarmentDetail.x*.1);
        float mouth=GarmentCoverage(opening,aa)*step(abs(p.x),.35)*body;
        if(variant==0 || variant==1){s.dark=max(s.dark,mouth*.8);s.height-=mouth*relief;s.ao=max(s.ao,mouth*.9);}
        if(variant==2)
        {
            float lip=(-p.y-.35+abs(p.x)*.4)*.9;
            float underside=GarmentBell(lip-.013,.025)*step(abs(p.x),.39)*(1-body*.65);
            s.dark=max(s.dark,underside*_GarmentAging.y*1.6);s.ao=max(s.ao,underside*.85);
            s.height+=body*GarmentBell(lip,.06)*relief*.3;
        }
        GarmentColor(s,body*_GarmentPrint.w*_GarmentCloth.a,_GarmentCloth.rgb*(1+grain*.045),_GarmentFinish.x,0);
        GarmentBorder(s,d,along,aa,closed);s.protection=max(s.protection,bevel*.7);
    }
    else if(_GarmentKind==3)
    {
        if(variant<=1)
        {
            float width=max(_GarmentSize.x,.0001);
            float depthShading=GarmentDepthShading();
            // Hardware has its own physical scale. The tape, thread rows and
            // longitudinal repeat spacing continue to use the ribbon width.
            float hardwareScale=max(_GarmentZipperWidth,.1),hardwareAA=aa/hardwareScale;
            float zipperX=p.x/hardwareScale;
            float sliderAlong=(p.y-(_GarmentDetail.y-.5))*_GarmentSize.y/(width*hardwareScale);
            float gap=closed?0:min(_GarmentDetail.x*.9,.2)*smoothstep(.15,.65,sliderAlong);
            float tapeDistance=abs(p.x)-.36,tape=GarmentCoverage(tapeDistance,aa);
            float weave=GarmentLoopWeave(world,max(_GarmentStitch.x*1.6,.0002),loopPeriod,loopBlend);
            s.coverage=tape*envelope;s.height=tape*relief*(.12+weave*.025);
            GarmentColor(s,tape,_GarmentCloth.rgb*(1+weave*.075),_GarmentFinish.x,0);
            float tapeLip=GarmentBell(tapeDistance,max(.008,aa));
            s.dark=tapeLip*_GarmentAging.y*.45*depthShading;s.ao=tapeLip*.3*depthShading;
            float trench=GarmentBell(zipperX,max(gap,.012));
            s.dark=max(s.dark,trench*.75*depthShading);s.ao=max(s.ao,trench*.8*depthShading);
            float rows=max(1,round(_GarmentSize.y/size)),pitch=(closed?_GarmentSize.y/rows:size)/(width*hardwareScale);
            float toothDistance=1,toothCrease=0;
            [unroll] for(int bank=0;bank<2;bank++)
            {
                float side=bank==0?-1:1;
                float row=(closed ? uv.y*rows : world.y/size)+bank*.5;
                float2 toothPoint=float2(zipperX*side-gap,(frac(row)-.5)*pitch);
                float bankDistance;
                if(variant==0)
                {
                    // Both banks extend past the center, so alternating heads really interlock.
                    float root=GarmentBox(toothPoint-float2(.087,0),float2(.062,pitch*.32),min(.012,pitch*.13));
                    float head=GarmentBox(toothPoint-float2(.018,0),float2(.039,pitch*.22),min(.014,pitch*.16));
                    bankDistance=min(root,head);
                }
                else
                {
                    // Continuous flattened coil turns with a return on the tape side.
                    float2 coil=toothPoint-float2(.047,0);
                    float ring=abs(GarmentBox(coil,float2(.056,pitch*.32),min(.032,pitch*.25)))-min(.012,pitch*.12);
                    float join=GarmentLine(coil,float2(.05,-pitch*.5),float2(.05,pitch*.5))-min(.01,pitch*.1);
                    bankDistance=min(ring,join);
                }
                toothDistance=min(toothDistance,bankDistance);
                // Retain each bank's contact where interlocking teeth overlap;
                // the union silhouette alone would erase these small creases.
                toothCrease=max(toothCrease,GarmentContact(bankDistance,min(.012,pitch*.14),GarmentCoverage(bankDistance,hardwareAA),hardwareAA));
            }
            float tooth=GarmentCoverage(toothDistance,hardwareAA);
            s.dark*=1-tooth*.9;s.ao*=1-tooth*.8;
            float machining=GarmentWave((closed?uv.y*rows:world.y/size)*7)*.025*depthShading;
            GarmentHardware(s,toothDistance,min(.018,pitch*.14),relief,
                _GarmentAccent.rgb*(1+machining),_GarmentFinish.y,variant==0?1:0,hardwareAA);
            s.dark=max(s.dark,toothCrease*_GarmentAging.y*.9*depthShading);
            s.ao=max(s.ao,toothCrease*.72*depthShading);
            float3 sliderColor=variant==0?_GarmentAccent.rgb:lerp(_GarmentAccent.rgb,float3(.34,.35,.36),.55);
            if(!closed && ((p.y<0 && openEdges.z<.5) || (p.y>=0 && openEdges.w<.5)))
            {
                // The outer edge, rather than the center, sits at the actual
                // path boundary. Stop thickness stays physical on long ribbons.
                float stopHalfHeight=.032;
                float fromEnd=(.5-abs(p.y))*_GarmentSize.y/(width*hardwareScale);
                float endY=fromEnd-stopHalfHeight;
                float stop=p.y>0 ? GarmentBox(float2(abs(zipperX)-gap-.085,endY),float2(.045,stopHalfHeight),.012) :
                    GarmentBox(float2(zipperX,endY),float2(.12,stopHalfHeight),.012);
                GarmentHardware(s,stop,.01,relief*1.1,sliderColor,_GarmentFinish.y,1,hardwareAA);
                // Area Edge Fade softens cloth; a finished metal stop must still
                // reach the end. User-authored path fades apply after generation.
                float stopCoverage=GarmentCoverage(stop,hardwareAA);
                float sideEnvelope=smoothstep(0,_GarmentShape.w,.5-abs(p.x));
                envelope=max(envelope,stopCoverage*sideEnvelope);
            }
            // All hardware dimensions are measured in ribbon-width units. Length only places it.
            float2 sliderPoint=float2(zipperX,sliderAlong);
            float bodyHalfWidth=lerp(.14,.175,saturate((sliderPoint.y+.18)/.36));
            float sliderDistance=GarmentBox(sliderPoint,float2(bodyHalfWidth,.19),.035);
            float slider=GarmentCoverage(sliderDistance,hardwareAA);
            s.dark*=1-slider*.9;s.ao*=1-slider*.85;
            GarmentHardware(s,sliderDistance,.032,relief*1.8,sliderColor,_GarmentFinish.y,1,hardwareAA);
            float inset=GarmentCoverage(GarmentBox(sliderPoint-float2(0,.025),float2(bodyHalfWidth-.035,.115),.022),hardwareAA)*slider;
            s.height-=inset*relief*.15;s.dark=max(s.dark,inset*_GarmentAging.y*.12*depthShading);
            float insetEdge=GarmentBox(sliderPoint-float2(0,.025),float2(bodyHalfWidth-.035,.115),.022);
            float insetContact=GarmentBell(insetEdge,max(.012,hardwareAA))*inset;
            s.dark=max(s.dark,insetContact*_GarmentAging.y*.65*depthShading);s.ao=max(s.ao,insetContact*.4*depthShading);
            // Raised attachment bridge and a hanging stamped pull with a genuine open cutout.
            float bridge=GarmentBox(sliderPoint-float2(0,.055),float2(.05,.055),.018);
            GarmentHardware(s,bridge,.012,relief*2.5,sliderColor,_GarmentFinish.y,1,hardwareAA);
            float2 pullPoint=sliderPoint-float2(.015,-.205);
            pullPoint=float2(pullPoint.x*.992+pullPoint.y*.126,-pullPoint.x*.126+pullPoint.y*.992);
            float pullOuter=GarmentBox(pullPoint,float2(.085,.235),.048);
            float pullHole=GarmentBox(pullPoint-float2(0,-.052),float2(.045,.108),.031);
            float pullDistance=max(pullOuter,-pullHole);
            float pull=GarmentCoverage(pullDistance,hardwareAA);
            float castDistance=max(GarmentBox(pullPoint-float2(.012,-.014),float2(.085,.235),.048),
                -GarmentBox(pullPoint-float2(.012,-.066),float2(.045,.108),.031));
            // A small soft seating shadow reaches both the outside and the open
            // cutout, without painting a filled silhouette over the hole.
            float pullShadow=max(GarmentCoverage(castDistance,hardwareAA*1.5),
                GarmentBell(max(castDistance,0),sqrt(.016*.016+hardwareAA*hardwareAA))*.65)*(1-pull);
            s.dark=max(s.dark,pullShadow*_GarmentAging.y*1.4*depthShading);s.ao=max(s.ao,pullShadow*.85*depthShading);
            s.dark*=1-pull*.9;s.ao*=1-pull*.85;
            GarmentHardware(s,pullDistance,.014,relief*2.65,sliderColor,_GarmentFinish.y,1,hardwareAA);
            GarmentBorder(s,abs(p.x)-.33,closed?uv.y*_GarmentSize.y:world.y,aa,closed);
        }
        else
        {
            float2 q=float2(p.x,(frac((p.y+.5)*_GarmentDetail.z)-.5));
            float ratio=_GarmentSize.y/(_GarmentSize.x*_GarmentDetail.z);
            q.y*=ratio;
            // Shrink repeated fasteners only when their physical cells cannot fit the diameter.
            q/=min(1,ratio/.55);
            float radius=.22;
            float d=variant==2 ? GarmentBox(q,float2(.055,.28),.055) : length(q)-radius;
            float body=GarmentCoverage(d,aa);
            float bevel=GarmentBell(d,.025);
            s.coverage=saturate(body+GarmentBell(d-.01,.02)*.5)*envelope;
            float dome=sqrt(saturate(1-dot(q,q)/(radius*radius)));
            s.height=body*relief*(.22+.78*dome);
            s.ao=GarmentBell(d-.008,.018)*.7;s.dark=s.ao*_GarmentAging.y;
            float hole=variant==6 ? GarmentCoverage(length(q)-.12,aa) : 0;
            if(variant==3)
            {
                float2 hq=abs(q)-.052;hole=GarmentCoverage(length(hq)-.026,aa);
                float rim=GarmentBell(length(q)-.177,.018),dish=GarmentCoverage(length(q)-.145,aa);
                GarmentColor(s,body,_GarmentAccent.rgb*GarmentTone(.86+.14*dome-rim*.12-dish*.06),_GarmentFinish.y,0);
                s.height+=rim*relief*.15-dish*relief*.25;
                float threadDistance=min(GarmentLine(q,float2(-.052,-.052),float2(.052,.052)),
                    GarmentLine(q,float2(-.052,.052),float2(.052,-.052)));
                float threadRadius=max(.008,_GarmentStitch.x/_GarmentSize.x*.5);
                float thread=GarmentCoverage(threadDistance-threadRadius,aa)*_GarmentThread.a;
                float contact=GarmentBell(threadDistance,max(threadRadius*2.5,aa))*(1-thread*.65)*_GarmentThread.a;
                s.dark=max(s.dark,contact*_GarmentAging.y);s.ao=max(s.ao,contact*.65);
                s.color*=GarmentTone(1-hole*.85);s.height-=hole*relief*.6;
                GarmentColor(s,thread,_GarmentThread.rgb*GarmentTone(.7+.3*GarmentBell(threadDistance,threadRadius)),.68,0);
                s.height+=thread*relief*.35;hole*=1-thread;
            }
            else if(variant==2)
            {
                float border=GarmentCoverage(abs(d+_GarmentStitch.w*.5)-.025,aa)*_GarmentThread.a*saturate(_GarmentStitch.z);
                float stitchPhase=(abs(q.y)>.23?atan2(q.x,abs(q.y)-.23)*.055:q.y)/max(_GarmentStitch.y/_GarmentSize.x,.008);
                float density=saturate(_GarmentStitch.x/max(_GarmentStitch.y,.0001)*2);
                float satin=smoothstep(1-max(density,.01),1,.5+.5*cos(stitchPhase*GARMENT_TAU));
                satin=lerp(density*.5,satin,1-smoothstep(.2,.55,fwidth(stitchPhase)));
                border*=.35+.65*satin;
                float rimRound=sqrt(saturate(1-pow((d+_GarmentStitch.w*.5)/.025,2)));
                float sewn=(.7+.3*rimRound)*(.9+satin*.1);
                GarmentColor(s,border,_GarmentThread.rgb*GarmentTone(sewn),.72,0);
                hole=GarmentCoverage(GarmentBox(q,float2(.013,.23),.013),aa);
                s.height=border*relief*(.15+.45*rimRound)*(.8+.2*satin);
                s.ao=max(s.ao,GarmentBell(d+_GarmentStitch.w*.5,.046)*.45*_GarmentThread.a*saturate(_GarmentStitch.z));
            }
            else
            {
                GarmentHardware(s,max(d,-(variant==6 ? length(q)-.12 : 1)),.025,relief,_GarmentAccent.rgb,_GarmentFinish.y,1,aa);
                if(variant==4)
                {
                    float rim=GarmentBell(length(q)-.162,.012);
                    float cap=GarmentCoverage(length(q)-.143,aa);
                    s.height+=cap*relief*.18-rim*relief*.18;
                    s.dark=max(s.dark,rim*_GarmentAging.y*.6);s.ao=max(s.ao,rim*.4);
                }
                if(variant==5)
                {
                    float peen=GarmentCoverage(length(q)-.08,aa),rim=GarmentBell(length(q)-.09,.014);
                    float peenDome=sqrt(saturate(1-dot(q,q)/(.08*.08)));
                    s.height+=peen*peenDome*relief*.4;s.dark=max(s.dark,rim*_GarmentAging.y*.5);
                    s.roughness=lerp(s.roughness,min(1,_GarmentFinish.y+.15),peen);
                }
            }
            s.dark=max(s.dark,hole*.95);s.ao=max(s.ao,hole*.95);s.height-=hole*relief*1.2;
            s.color*=GarmentTone(1-hole*.9);s.wear=bevel*_GarmentAging.x*.2;
        }
    }
    else if(_GarmentKind==4)
    {
        float2 patchSize=variant==2?float2(.41,.13):variant==1?float2(.4,.25):float2(.4,.34);
        float distance=length(p/patchSize)-1+(noise-.5)*_GarmentShape.z*.5;
        float body=GarmentCoverage(distance,aa*3)*envelope;
        float edge=GarmentBell(distance,.12);
        float broken=smoothstep(.7-_GarmentDetail.w,.9-_GarmentDetail.w,noise);
        float2 yarn=world/size;
        yarn+=float2(sin(yarn.y*.73),sin(yarn.x*.61))*_GarmentShape.z*.11;
        float warp=GarmentYarn(yarn.x);
        float weft=GarmentYarn(yarn.y);
        float fibers=(variant==1||variant==2 ? max(weft,warp*.2) : max(warp,weft*.35))*lerp(1,broken,_GarmentDetail.w);
        s.coverage=saturate(body+edge*_GarmentPrint.x*.4);
        s.wear=body*_GarmentAging.x*(.2+noise*.5)*.4;
        s.dark=body*_GarmentAging.y*(1-fibers)*_GarmentDetail.w;
        s.ao=body*(1-fibers)*_GarmentDetail.w*.7;s.height=(fibers*.2-_GarmentDetail.w*.6)*body*relief;
        s.roughness=saturate(_GarmentFinish.x+body*(1-fibers)*.09);
        if(variant==1 || variant==2)
        {
            float loose=edge*_GarmentPrint.x*warp*smoothstep(.3,.7,noise);
            float thread=max(body*fibers,loose);
            float fiberShade=.68+.32*sqrt(saturate(fibers));
            GarmentColor(s,thread,_GarmentCloth.rgb*GarmentTone(fiberShade),.92,0);
            s.coverage=max(s.coverage,loose);
            s.dark=max(s.dark,body*(1-fibers)*_GarmentDetail.w*(variant==2 ?.9:.4));
            s.height+=loose*relief*.35;
            float yarnContact=body*sqrt(saturate(fibers))*(1-fibers);
            s.dark=max(s.dark,yarnContact*_GarmentAging.y*.85);s.ao=max(s.ao,yarnContact*.75);
        }
        if(variant==3)
        {
            float over=step(.5,frac((floor(yarn.x+.5)+floor(yarn.y+.5))*.5));
            float top=lerp(warp,weft,over),under=lerp(weft,warp,over);
            float thread=max(warp,weft),crossing=warp*weft;
            float contact=body*pow(saturate(thread),.3)*(1-thread*.7);
            GarmentColor(s,body*thread,_GarmentThread.rgb*GarmentTone((.65+.35*sqrt(thread))*(1-crossing*.1)),.8,0);
            s.height=body*(top*.8+under*.45)*(1-crossing*.1)*min(relief,size*.22*GarmentDepthScale());
            s.dark=max(s.dark,contact*_GarmentAging.y*.7);s.ao=max(s.ao,contact*.55);
            s.wear=edge*_GarmentAging.x*.03;
        }
        if(variant==4)
        {
            float along;
            float d=GarmentStrip(p,float2(.37,.34),.025,openEdges,along),patch=GarmentCoverage(d,aa);
            float contact=GarmentContact(d,.021,patch,aa);
            float weave=GarmentLoopWeave(world,max(_GarmentStitch.x*2,.0002),loopPeriod,loopBlend);
            s.coverage=max(patch,contact);s.dark=contact*_GarmentAging.y;s.ao=contact*.65;
            s.wear=GarmentBell(d,.025)*_GarmentAging.x*.3;
            GarmentColor(s,patch,_GarmentCloth.rgb*(1+weave*.055),.85,0);
            s.height=patch*relief*(.2+.8*GarmentRoundedEdge(d,.035)+weave*.025);
            GarmentBorder(s,d,along,aa,closed);
        }
        if(variant<=2)s.wear=max(s.wear,edge*_GarmentPrint.x*noise*.12);
        s.protection=edge*.5;
    }
    else
    {
        float along=0;
        float d=variant==2 ? GarmentBox(p,float2(.42,.36),.03) :
            GarmentStrip(p,float2(.42,.36),.03,openEdges,along);
        float body=GarmentCoverage(d,aa);
        float2 motifUV=p/max(_GarmentPrint.y,.05)+.5;
        float4 motif=tex2Dlod(_GarmentMotif,float4(saturate(motifUV),0,0));
        motif.a*=all(motifUV>=0)&&all(motifUV<=1) ? 1 : 0;
        if(_GarmentHasMotif==0){motif=float4(_GarmentAccent.rgb,GarmentCoverage(abs(p.x)+abs(p.y)-.2,aa));}
        float cracking=smoothstep(.025,.06,abs(noise-.5));
        float printMask=motif.a*body*lerp(1,cracking,_GarmentDetail.w*_GarmentDetail.w*.7);
        s.coverage=(variant==2 ? printMask : saturate(body+GarmentBell(d-.01,.022)*.4))*envelope;
        s.height=body*relief*(.2+.8*GarmentRoundedEdge(d,.028));
        float contact=variant==2 ? 0 : GarmentContact(d,.02,body,aa);
        s.dark=contact*_GarmentAging.y;s.ao=contact*.7;
        s.wear=GarmentBell(d,.04)*_GarmentAging.x*noise;
        float weave=GarmentLoopWeave(world,max(_GarmentStitch.x*1.5,.0002),loopPeriod,loopBlend);
        float leather=lerp(GarmentNoise(world/max(_GarmentStitch.x*3,.0003)),
            GarmentNoise((world-loopPeriod)/max(_GarmentStitch.x*3,.0003)),loopBlend);
        if(variant!=2)
        {
            float grain=variant==1 ? (leather-.5)*.1 : variant==3 ? (leather-.5)*.025 : weave*.065;
            GarmentColor(s,body*_GarmentCloth.a,_GarmentCloth.rgb*(1+grain),saturate(_GarmentFinish.x-grain*.8),0);
            s.height+=body*relief*grain*.2;
        }
        float3 ink=_GarmentPrint.z>.5 && _GarmentHasMotif!=0 ? motif.rgb : _GarmentAccent.rgb;
        float fiber=.5+.5*GarmentWave((world.x+world.y*.25)/max(size,.0001));
        if(variant==4)
        {
            float direction=step(.5,frac(floor(world.y/max(size*7,.001))*.5));
            fiber=.5+.5*GarmentWave(lerp(world.x+world.y*.18,world.y-world.x*.18,direction)/max(size,.0001));
            float fiberRound=sqrt(saturate(fiber));
            ink*=GarmentTone(.65+fiberRound*.35);s.height+=printMask*relief*(.35+fiberRound*.5);
            float embroideryContact=printMask*(1-fiberRound)*.32;
            s.ao=max(s.ao,embroideryContact);s.dark=max(s.dark,embroideryContact*_GarmentAging.y);
        }
        if(variant==0){ink*=1+weave*.06;s.height+=body*relief*.06*weave;}
        float motifEdge=saturate(length(float2(ddx_fine(printMask),ddy_fine(printMask)))*2);
        if(variant==1)
        {
            // A leather mark is pressed into the hide. Occlusion seats its
            // perimeter instead of merely laying a dark rectangle on the face.
            s.height-=printMask*relief*.35;
            s.ao=max(s.ao,printMask*.12+motifEdge*.3);
            s.dark=max(s.dark,(printMask*.12+motifEdge*.4)*_GarmentAging.y);
        }
        if(variant==3)
        {
            // Molded rubber has raised ink and a narrow seat around that relief.
            s.height+=smoothstep(.08,.92,printMask)*relief*.8;
            ink*=GarmentTone(1-motifEdge*.22);
            s.ao=max(s.ao,motifEdge*.3);s.dark=max(s.dark,motifEdge*_GarmentAging.y*.4);
        }
        GarmentColor(s,printMask,ink,variant==3 ?.55:.7,0);
        if(variant!=2)GarmentBorder(s,d,along,aa,closed);
        if(variant==0 || variant==1 || variant==4)
        {
            float peel=GarmentBell(length(p-float2(.38,.32)),.085)*_GarmentDetail.w;
            s.height+=peel*body*relief*.8;
            float underCorner=peel*GarmentContact(d,.03,body,aa);
            s.ao=max(s.ao,underCorner*.75);s.dark=max(s.dark,underCorner*_GarmentAging.y);
        }
        s.protection=GarmentBell(d,.04);
    }
    // The zipper uses ordered hardware/shadow compositing and applies this
    // strength as it builds each level. Other families apply it once here.
    // Wear dirt is pigment; only its separately evaluated relief inputs shade.
    if(!GarmentIsZipper() && _GarmentKind!=1)
    {
        float shading=GarmentDepthShading();s.dark*=shading;s.ao*=shading;
    }
    s.coverage=min(saturate(s.coverage),envelope)*_GarmentShape.x;
    s.wear=saturate(s.wear);s.dark=saturate(s.dark);s.ao=saturate(s.ao);
    s.protection=saturate(s.protection);s.roughness=saturate(s.roughness);
    return s;
}
float4 ShadeGarment(float2 uv,float2 destinationUV,float aa,bool closed,float3 position,float3 normal,float4 tangent)
{
    GarmentSample s=EvaluateGarment(uv,destinationUV,aa,closed);
    if(_GarmentChannel==0)
    {
        float alpha=1-(1-s.dark)*(1-s.wear)*(1-s.material);
        // Contact shading must also reach opaque generated cloth, thread and metal.
        float3 added=(s.wear*(1-s.material)+s.color*s.material)*(1-s.dark);
        return float4(added/max(alpha,1e-6),alpha*s.coverage);
    }
    if(_GarmentChannel==2)return float4(s.metal.xxx,s.coverage);
    if(_GarmentChannel==3)return float4(s.roughness.xxx,s.coverage);
    if(_GarmentChannel==4)return float4(0,0,0,saturate(s.ao)*s.coverage);
    if(_GarmentChannel==10)return float4(saturate(.5+s.height/max(_GarmentSize.x,.0001)).xxx,s.coverage);
    if(_GarmentChannel==6)return float4(s.wear,s.protection,s.ao,s.coverage);
    if(_GarmentChannel==1)
    {
        float3 n=normalize(normal),dx=ddx_fine(position),dy=ddy_fine(position);
        float3 rx=cross(dy,n),ry=cross(n,dx);float det=dot(dx,rx);
        float3 gradient=(ddx_fine(s.height)*rx+ddy_fine(s.height)*ry)*(abs(det)>1e-12?rcp(det):0)*_GarmentFinish.z;
        float3 bumped=normalize(n-gradient),t=tangent.xyz;float handed=tangent.w;
        if(dot(t,t)<.01)
        {
            float2 ux=ddx_fine(destinationUV),uy=ddy_fine(destinationUV);float determinant=ux.x*uy.y-ux.y*uy.x;
            t=(dx*uy.y-dy*ux.y)*(determinant<0?-1:1);
            float3 b=(dy*ux.x-dx*uy.x)*(determinant<0?-1:1);handed=dot(cross(n,t),b)<0?-1:1;
        }
        t=normalize(t-n*dot(t,n));float3 b=normalize(cross(n,t))*handed;
        return float4(normalize(float3(dot(bumped,t),dot(bumped,b),dot(bumped,n)))*.5+.5,s.coverage);
    }
    return 0;
}
#endif
