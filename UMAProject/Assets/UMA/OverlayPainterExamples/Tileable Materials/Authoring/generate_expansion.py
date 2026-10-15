"""72 original clean PBR surfaces; run from the Unity project root.

Uses the library's periodic field utilities. No photographs, licensed textures,
or AI images are used. Geometry, pigment, roughness and coverage share fields.
The first six families have NO wear. Wood and keratin have sparse micro wear.
Run after generate_materials.py when rebuilding the whole library.
"""
import argparse
import json
import time
import sys
sys.dont_write_bytecode = True
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from generate_materials import Fields, sat, smooth, mix, rgb, ROOT, EXAMPLES

TAU = 2*np.pi
FAMILIES = {
    "Rubber and Elastomers": ["Smooth Vulcanized Rubber", "Fine Pebble Rubber", "Stipple Grip Rubber", "Rounded Rib Rubber", "Diamond Grip Rubber", "Smooth Silicone", "Neoprene Foam", "Closed Cell EVA", "Crosshatch Elastomer"],
    "Plastics and Coatings": ["Smooth ABS", "Molded PBT Grain", "Gloss Polycarbonate", "Satin Polypropylene", "Soft Touch Polymer", "Fine Powder Coat", "Gloss Enamel", "Satin Ceramic Coating", "Textured Molded Nylon"],
    "Knits and Soft Clothing": ["Fine Cotton Jersey", "Classic Stockinette", "Chunky Wool Knit", "Braided Cable Knit", "Double Rib Knit", "Waffle Knit", "Interlock Cotton", "Moss Stitch", "Fisherman Rib"],
    "Chainmail and Scale Armor": ["European Round Mail", "Riveted Round Mail", "Dense Six Ring Mail", "Flat Link Mail", "Fine Fish Scale", "Pointed Scale Armor", "Scalloped Scale Armor", "Laced Lamellar", "Riveted Brigandine"],
    "Technical Weaves and Composites": ["Carbon Twill", "Plain Carbon Weave", "Forged Carbon Composite", "Aramid Plain Weave", "Carbon Aramid Hybrid", "Ballistic Nylon", "Seatbelt Webbing", "Spacer Sports Mesh", "Coated Glass Fiber"],
    "Brocade and Decorative Fabrics": ["Burgundy Damask", "Navy Floral Jacquard", "Olive Diamond Brocade", "Ivory Paisley", "Teal Rosette Brocade", "Climbing Vine Jacquard", "Gold Thread Arabesque", "Silver Geometric Brocade", "Indigo Wave Brocade"],
    "Wood and Natural Materials": ["Straight Grain Oak", "Oiled Walnut", "Open Grain Ash", "Pale Maple", "Ribbon Mahogany", "Natural Bamboo", "Dark Stained Oak", "Natural Cork", "Satin Beech"],
    "Bone Horn and Keratin": ["Clean Ivory Bone", "Porous Bone", "Warm Aged Bone", "Amber Polished Horn", "Black Horn", "Striated Ivory", "Dark Claw Keratin", "Hoof Keratin", "Pearly Enamel"],
}

def periodic_delta(a): return (a+.5)%1-.5
def ridge(a, width): return np.exp(-(periodic_delta(a)/width)**2)
def base_color(f, value): return np.broadcast_to(rgb(value), (f.n,f.n,3)).copy()
def result(f,c,h,r,m=0,ao=1,relief=.0035,alpha=1,wear=None):
    field=lambda v: np.broadcast_to(v,(f.n,f.n)).astype(np.float32)
    return dict(color=c,height=field(h),roughness=field(r),metallic=field(m),ao=field(ao),
                relief=relief,alpha=field(alpha),wear=field(0 if wear is None else wear))

def rubber(f,i):
    x,y=f.x,f.y; micro=f.noise(384,1); broad=f.fbm(12,2,3)
    c=base_color(f,[(33,35,37),(42,43,41),(28,32,33),(36,38,40),(31,36,40),
                    (161,169,175),(34,41,46),(72,77,82),(53,57,50)][i])
    h=.5+.012*(micro-.5); r=.68+.025*(micro-.5); ao=np.ones_like(x)
    if i==1:
        _,gap=f.cells(140,5); grain=smooth(.005,.13,gap)
        h+=.13*grain;r=.72+.04*(1-grain);ao=.94+.06*grain
    if i==2:
        px=periodic_delta(x*40+.5*(np.floor(y*40+.5)%2));py=periodic_delta(y*40)
        d=np.sqrt(px*px+py*py); bump=np.sqrt(sat(1-(d/.34)**2))
        h+=.22*bump;r=.63+.04*(1-bump);ao=.90+.10*bump
    if i==3:
        ribs=np.maximum(0,np.cos(TAU*x*36))**.55
        h+=.27*ribs;r=.60+.04*(1-ribs);ao=.88+.12*ribs
    if i==4:
        gx=abs(periodic_delta((x+y)*24));gy=abs(periodic_delta((x-y)*24))
        diamond=smooth(.025,.13,np.minimum(gx,gy))
        h+=.22*diamond;r=.66+.035*(1-diamond);ao=.88+.12*diamond
    if i==5: h=.5+.006*(micro-.5);r=.37+.02*(broad-.5)
    if i in (6,7):
        d,gap=f.cells(100 if i==6 else 66,18)
        pore=(1-smooth(.055,.16,d))*.7
        grain=smooth(.015,.12,gap)
        h=.48+.035*grain-.065*pore+.012*(micro-.5)
        r=.88+.045*pore;ao=1-.13*pore
    if i==8:
        ribs=np.maximum(ridge((x+y)*64,.14),ridge((x-y)*64,.14))
        h+=.10*ribs;r=.72;ao=.96+.04*ribs
    c*= (1+.02*(micro-.5))[...,None]
    return result(f,c,h,r,ao=ao,relief=.003 if i in (0,5,6,7) else .007)

def plastic(f,i):
    micro=f.noise(384,4); broad=f.fbm(16,7,3)
    c=base_color(f,[(177,180,175),(52,59,65),(158,33,26),(39,72,106),(34,35,38),
                    (46,76,69),(216,199,155),(187,191,191),(52,57,48)][i])
    h=.5+.009*(micro-.5); r=[.32,.54,.10,.43,.72,.48,.14,.34,.62][i]+.018*(micro-.5)
    ao=np.ones_like(micro)
    if i in (1,8):
        _,g=f.cells(130 if i==1 else 92,19)
        grain=smooth(.012,.13,g);h+=.065*grain;ao=.96+.04*grain
    if i==3: h+=.012*(f.noise(384,14,12)-.5)
    if i==4: h+=.012*(broad-.5)
    if i==5: h+=.04*(f.noise(192,21)-.5);r+=.03*(broad-.5)
    if i==6: h+=.025*(f.fbm(48,32,3)-.5) # Factory orange peel, not wear.
    if i==7: h+=.01*(f.noise(256,37)-.5)
    c*= (1+.01*(micro-.5))[...,None]
    return result(f,c,h,r,ao=ao,relief=.0035)

def yarn_loop(f,cols,rows,warp=None,width=.115):
    """Interlocking pairs of curved yarn legs and a return loop at each course."""
    x=f.x if warp is None else warp
    qx=x*cols; qy=f.y*rows
    yy=qy%1;xx=qx%1
    left=.16+.34*(yy**1.3)
    right=1-left
    dl=abs(xx-left)/np.sqrt(1+(.44*cols/rows)**2)
    dr=abs(xx-right)/np.sqrt(1+(.44*cols/rows)**2)
    d=np.minimum(dl,dr)
    # Curved return across the top of the stitch, partially underneath its legs.
    ellipse=np.sqrt(((xx-.5)/.35)**2+((yy+.045)/.23)**2)
    ret=abs(ellipse-1)*.23
    strand=np.sqrt(sat(1-(d/width)**2))
    underneath=np.sqrt(sat(1-(ret/(width*.72))**2))*.66
    shape=np.maximum(strand,underneath)
    twist=np.sin(TAU*(qy*5+qx*2+.12*np.sin(TAU*qy)))
    return shape,twist,d

def knit(f,i):
    cols=[80,40,22,32,40,64,96,40,32][i];rows=[96,56,28,48,56,64,112,48,48][i]
    x,y=f.x,f.y;warp=x
    if i==3: warp=x+.045*np.sin(TAU*y*4)
    if i==7: warp=x+.5/cols*(np.floor(y*rows)%2)
    loop,twist,d=yarn_loop(f,cols,rows,warp,.14 if i in (2,8) else .12)
    micro=f.noise(512,17); yarn=f.noise(cols,30,4)*.5+f.noise(4,31,rows)*.5
    h=.31+.24*loop+.008*twist*loop+.008*(micro-.5)
    if i==3:
        cable=.5+.5*np.cos(TAU*x*8+.5*np.sin(TAU*y*4))
        h+=.08*cable
    if i in (4,8):
        ribs=.5+.5*np.cos(TAU*x*(cols//(4 if i==4 else 2)))
        h+=.11*ribs
    if i==5:
        waffle=np.maximum(ridge(x*16,.15),ridge(y*16,.15))
        h+=.10*waffle
    if i==7:h+=.035*np.cos(TAU*(x*20+y*24))*loop
    colors=[(163,168,172),(166,72,57),(202,189,161),(169,150,120),(58,77,90),
            (174,162,139),(194,195,176),(111,128,96),(70,77,81)]
    c=base_color(f,colors[i]);c*=(.94+.08*yarn+.025*twist*loop+.02*(micro-.5))[...,None]
    # Yarn-to-yarn pigment variation is restrained; lighting belongs in the normals.
    r=.86+.06*(1-loop)+.02*(micro-.5);ao=.65+.35*loop
    return result(f,c,h,r,ao=ao,relief=.0045 if i in (0,6) else .010)

def armor(f,i):
    if i < 4:
        from mail_geometry import bake
        return dict(bakedMaps=bake(f.n,i,seed=76000),relief=1.,wear=np.zeros_like(f.x),
                    alpha=np.ones_like(f.x),metallic=np.ones_like(f.x))
    x,y=f.x,f.y; micro=f.noise(384,71)
    color=base_color(f,[(159,165,169),(177,172,157),(101,110,119),(187,188,177),
                       (170,181,188),(172,138,67),(145,152,163),(98,107,116),(66,37,26)][i])
    h=np.full_like(x,.25); coverage=np.zeros_like(x);ao=np.ones_like(x);metal=np.full_like(x,.98)
    if i<7:
        nx,ny=[(16,20),(8,12),(10,14)][i-4]
        row=np.floor(y*ny)
        px=abs(periodic_delta(x*nx+.5*(row%2)))*2;py=(y*ny)%1
        border=(np.sqrt(sat(1-px*px)) if i!=5 else 1-px)*.48+.48
        edge=border-py
        plate=smooth(-.01,.045,edge)
        crown=np.sqrt(sat(1-px*px))
        bevel=smooth(0,.08,np.minimum(edge,.5-px*.5))
        h=.34+.16*plate+.10*crown*plate+.025*bevel
        coverage=np.ones_like(x);ao=.80+.20*bevel
        r=.31+.025*(micro-.5)
        color*= (1+.015*np.sin(TAU*(x*nx+row*.5)))[...,None]
    elif i==7:
        xx=(x*8)%1;yy=(y*12)%1
        edge=np.minimum(np.minimum(xx,1-xx),np.minimum(yy,1-yy))
        plate=smooth(.025,.07,edge)
        lace=np.maximum(ridge(x*16-.18,.11),ridge(x*16+.18,.11))*ridge(y*12-.18,.10)
        h=.30+.22*plate+.10*lace;coverage=np.ones_like(x)
        color=mix(color,rgb((111,72,42)),lace);metal=.98*(1-lace)
        r=.34+.35*lace;ao=.79+.21*plate
    else:
        xx=periodic_delta(x*16);yy=periodic_delta(y*20)
        stud=np.sqrt(sat(1-(np.sqrt(xx*xx+yy*yy)/.095)**2))
        _,grain=f.cells(110,83)
        h=.43+.04*smooth(.01,.12,grain)+.18*stud
        color=mix(color,rgb((182,166,117)),smooth(0,.3,stud))
        metal=smooth(0,.3,stud)*.98;r=.61-.30*stud;ao=.98
        coverage=np.ones_like(x)
    h+=.003*(micro-.5)*coverage
    return result(f,color,h,r,metal,ao,.009 if i<4 else .007,coverage)

def weave_fields(f,count,twill=False,group=1):
    x=f.x*count;y=f.y*count
    ix=np.floor(x).astype(int);iy=np.floor(y).astype(int)
    over=((ix+iy)%4)<2 if twill else ((ix//group+iy//group)%2)==0
    a=np.maximum(0,np.sin(np.pi*(x%1)))**.58
    b=np.maximum(0,np.sin(np.pi*(y%1)))**.58
    strand=np.where(over,a,b)
    filaments=np.where(over,np.sin(TAU*x*7),np.sin(TAU*y*7))
    return over,strand,filaments

def technical(f,i):
    x,y=f.x,f.y;micro=f.noise(384,60)
    over,thread,filament=weave_fields(f,[32,40,32,72,40,128,112,80,64][i],i in (0,4),2 if i==5 else 1)
    c=base_color(f,[(40,44,48),(44,47,49),(36,40,43),(173,136,56),(46,48,45),
                    (54,67,52),(46,50,53),(63,93,111),(194,189,168)][i])
    h=.35+.15*thread+.007*filament*thread
    c*=(.89+.10*thread+.025*filament+.025*(micro-.5))[...,None]
    r=[.21,.28,.19,.65,.26,.78,.55,.84,.36][i]+.035*(1-thread)
    ao=.83+.17*thread;alpha=np.ones_like(x)
    if i==2:
        # Chopped composite flakes are constituent fibers, not chips or damage.
        _,gap=f.cells(36,76,.65);flakes=smooth(.025,.18,gap)
        mottling=f.noise(48,80)
        c=mix(c,rgb((70,75,78)),flakes*mottling*.60)
        h=.48+.018*flakes+.006*(micro-.5);r=.20+.045*(mottling-.5);ao=1
    if i==4:
        gold=(np.floor(x*40).astype(int)%4<2)&over
        c=mix(c,rgb((165,133,58)),gold.astype(float)*(.88+.08*thread))
    if i==6:
        ridges=ridge(x*28,.17);h+=.06*ridges
        c*=(1+.04*ridges)[...,None]
    if i==7:
        px=periodic_delta(x*24+.5*(np.floor(y*28)%2));py=periodic_delta(y*28)
        d=np.sqrt((px/.31)**2+(py/.36)**2)
        alpha=smooth(.93,1.07,d)
        edge=np.exp(-((d-1.12)/.20)**2)
        h=.39+.05*thread+.14*edge;ao=.83+.17*thread
    return result(f,c,h,r,ao=ao,relief=.0045,alpha=alpha)

def brocade_motif(f,i):
    x,y=f.x,f.y
    xx=periodic_delta(x*4+.5*(np.floor(y*4+.5)%2));yy=periodic_delta(y*4)
    rr=np.sqrt(xx*xx+yy*yy);angle=np.arctan2(yy,xx)
    rosette=1-smooth(.012,.038,abs(rr-(.21+.065*np.cos(angle*6))))
    leaf=1-smooth(.012,.045,abs((xx/.23)**2+(yy/.36)**2-1)*.14)
    vine=1-smooth(.011,.031,abs(periodic_delta(x*4+.18*np.sin(TAU*y*4))))
    if i==0:
        petals=1-smooth(.008,.03,abs(rr-(.15+.08*np.cos(angle*4))))
        return np.maximum(petals* (1-smooth(.28,.36,rr)),leaf*.85)
    if i==1: return np.maximum(rosette,ridge(x*4+.12*np.sin(TAU*y*4),.020)*.75)
    if i==2:
        d=abs(xx)+abs(yy)
        return np.maximum(1-smooth(.015,.04,abs(d-.37)),1-smooth(.05,.10,rr))
    if i==3:
        qx=xx+.13*np.sin(yy*6);qy=yy
        d=np.sqrt((qx/.22)**2+(qy/.35)**2)
        outer=1-smooth(.05,.12,abs(d-1))
        inner=1-smooth(.05,.11,abs(d-.68))
        return np.maximum(outer,inner*.8)
    if i==4: return np.maximum(rosette,1-smooth(.025,.07,rr))
    if i==5:
        leaves=(1-smooth(.8,1.1,((xx-.16*np.sin(TAU*y*8))/.14)**2+(yy/.22)**2))
        return np.maximum(vine,leaves)
    if i==6:
        a=ridge((x+y)*4+.16*np.sin(TAU*(x-y)*4),.035)
        b=ridge((x-y)*4+.16*np.sin(TAU*(x+y)*4),.035)
        return np.maximum(a,b)
    if i==7:
        return np.maximum(ridge((x+y)*8,.055),ridge((x-y)*8,.055))
    # Repeated semicircular wave fans with alternating rows.
    qx=periodic_delta(x*4+.5*(np.floor(y*8)%2));qy=(y*8)%1
    radial=np.sqrt(qx*qx+(.46*qy)**2)
    return np.maximum.reduce([1-smooth(.007,.025,abs(radial-radius)) for radius in (.16,.27,.38,.49)])

def brocade(f,i):
    motif=brocade_motif(f,i);over,thread,filament=weave_fields(f,192)
    bg=[(91,24,37),(24,40,64),(62,74,43),(202,191,164),(28,93,91),(138,73,41),(51,25,69),(30,33,39),(30,57,89)][i]
    fg=[(159,69,76),(153,161,170),(142,133,73),(68,91,124),(123,181,161),(189,156,92),(194,155,65),(181,190,203),(110,143,163)][i]
    c=mix(base_color(f,bg),rgb(fg),motif*.90)
    c*=(.96+.035*filament*thread+.015*(f.noise(384,16)-.5))[...,None]
    h=.38+.06*thread+.10*motif+.008*filament*motif
    r=.73-.12*motif;metal=motif*.88 if i in (6,7) else 0
    if i in (6,7):r=.73-.39*motif
    return result(f,c,h,r,metal,.88+.12*thread,.0045)

def minor_wear(f):
    # Sparse 1–3 texel marks, with no broad discoloration layer.
    return smooth(.975,.999,f.noise(384,977))*smooth(.65,.90,f.noise(16,978))

def wood(f,i):
    x,y=f.x,f.y;micro=f.noise(384,114)
    warp=f.noise(8,13,2)-.5;long=f.noise(64,22,4)
    cycles=[22,18,16,32,26,36,22,24,28][i]
    phase=x*cycles+2.4*warp+.3*np.sin(TAU*y*2)
    grain=(.5+.5*np.cos(TAU*phase))**8
    fibers=(.5+.5*np.sin(TAU*(x*192+2*warp)))**9
    colors=[(156,113,65),(91,57,34),(184,151,106),(205,182,141),(121,60,37),
            (182,157,91),(54,37,26),(164,126,82),(174,134,94)]
    c=base_color(f,colors[i]);c*=(1-.19*grain+.08*(long-.5)-.045*fibers)[...,None]
    h=.5-.045*grain-.012*fibers+.008*(micro-.5)
    r=[.49,.30,.58,.37,.34,.46,.40,.88,.42][i]+.06*grain+.02*(micro-.5)
    ao=1-.07*grain
    if i==3: c=base_color(f,colors[i])*(1-.07*grain+.045*(long-.5))[...,None];h=.5-.015*grain
    if i==4:
        ribbon=.5+.5*np.sin(TAU*x*8+.7*np.sin(TAU*y))
        c*=(.92+.13*ribbon)[...,None]
    if i==5:
        node=ridge(y*2+.018*np.sin(TAU*x*8),.026)
        c*=(1-.09*node)[...,None];h+=.025*node
    if i==7:
        _,gap=f.cells(54,46)
        pieces=smooth(.01,.10,gap);pigment=f.noise(72,65)
        c=base_color(f,colors[i])*(.88+.22*pigment)[...,None]
        h=.45+.08*pieces+.025*(pigment-.5);r=.89+.02*(micro-.5);ao=.9+.1*pieces
    wear=minor_wear(f)
    c=mix(c,rgb((196,176,143)),wear*.12);h-=wear*.008;r+=wear*.035
    return result(f,c,h,r,ao=ao,relief=.0045,wear=wear)

def bone(f,i):
    x,y=f.x,f.y;micro=f.noise(384,151);cloud=f.fbm(8,153,3)
    grain=f.noise(160,165,6)
    colors=[(213,201,170),(187,173,139),(184,165,124),(111,73,35),(36,32,28),
            (216,204,170),(42,37,32),(79,66,47),(224,218,197)]
    c=base_color(f,colors[i]);h=.5+.01*(micro-.5);r=[.45,.75,.59,.23,.29,.30,.32,.56,.19][i]+.025*(micro-.5)
    ao=np.ones_like(x)
    if i in (0,2,5,8):
        c*=(.98+.035*cloud+.018*(grain-.5))[...,None]
        h+=.01*(grain-.5)
    if i==1:
        d,gap=f.cells(74,182)
        pore=1-smooth(.045,.22,d)
        h-=.12*pore;ao=1-.23*pore;r+=.09*pore
        c*=(1-.035*pore)[...,None]
    if i in (3,4,6,7):
        striation=(.5+.5*np.sin(TAU*(x*80+.4*np.sin(TAU*y*2))))**6
        bands=.5+.5*np.sin(TAU*(x*9+.15*np.sin(TAU*y)))
        c*=(.89+.15*grain+.075*bands)[...,None]
        h+=.02*(grain-.5)+.012*striation
        if i==7:h+=.025*ridge(y*14,.10)
    wear=minor_wear(f)
    c=mix(c,rgb((145,132,104)),wear*.06);h-=wear*.006;r+=wear*.025
    return result(f,c,h,r,ao=ao,relief=.0035,wear=wear)

BUILDERS=[rubber,plastic,knit,armor,technical,brocade,wood,bone]

def encode(f,r):
    if 'bakedMaps' in r: return r['bakedMaps']
    h=sat(r['height']);alpha=sat(r['alpha'])
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*f.n*.5*r['relief']
    dy=(np.roll(h,-1,0)-np.roll(h,1,0))*f.n*.5*r['relief']
    normal=np.stack((-dx,dy,np.ones_like(h)),axis=-1)
    normal/=np.linalg.norm(normal,axis=-1)[...,None]
    u8=lambda a: np.round(sat(a)*255).astype(np.uint8)
    rgba=lambda a: np.concatenate((a,alpha[...,None]),axis=-1)
    gray=lambda a: np.repeat(sat(a)[...,None],3,axis=-1)
    return {'Albedo':u8(rgba(r['color'])),'Normal':u8(rgba(normal*.5+.5)),
            'Roughness':u8(rgba(gray(r['roughness']))),'Metallic':u8(rgba(gray(r['metallic']))),
            'AmbientOcclusion':u8(rgba(gray(r['ao']))),'Height':np.round(h*65535).astype(np.uint16)}

def generate(size=1024,only=None):
    start=time.perf_counter();EXAMPLES.mkdir(parents=True,exist_ok=True)
    catalog=json.loads((ROOT/'Catalog.json').read_text(encoding='utf-8'))
    if size != catalog['tileSize']:
        raise ValueError('Expansion tile size must match the existing catalog.')
    catalog.update(version=3,wearProfile='Original sets: very minor aging. Expansion sets 1–6: no wear. Expansion sets 7–8: minor micro wear only.')
    measurements=[];cards=[]
    try:font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
    except OSError:font=ImageFont.load_default()
    for fi,(family,names) in enumerate(FAMILIES.items()):
        if only and family!=only:continue
        folder=ROOT/family;folder.mkdir(exist_ok=True);atlases={};tiles=[];metadata=[]
        t=time.perf_counter();p=8;stride=size+2*p
        for i,name in enumerate(names):
            f=Fields(size,52000+fi*100+i);r=BUILDERS[fi](f,i);maps=encode(f,r)
            assert all(np.isfinite(a).all() for a in maps.values())
            wearArea=float(np.mean(r['wear']>.01));assert wearArea<.002
            if fi<6:assert np.count_nonzero(r['wear'])==0
            metadata.append(dict(name=name,normalStrength=r['relief'],wearArea=wearArea,
                transparent=bool(np.any(r['alpha']<.99)),dielectric=bool(np.max(r['metallic'])==0)))
            if 'bakedMaps' in r: metadata[-1]['normalMode']='analyticSurface'
            for channel,im in maps.items():
                if channel not in atlases:atlases[channel]=np.zeros((stride*3,stride*3)+im.shape[2:],im.dtype)
                padded=np.pad(im,((p,p),(p,p))+((0,0),)*(im.ndim-2),mode='wrap')
                row,col=divmod(i,3);atlases[channel][row*stride:(row+1)*stride,col*stride:(col+1)*stride]=padded
                for axis in (0,1):
                    a=im.astype(np.float32);seam=np.mean(abs(np.take(a,0,axis)-np.take(a,-1,axis)))
                    measurements.append(dict(family=family,material=name,channel=channel,axis=axis,seamMean=float(seam),interiorMean=float(abs(np.diff(a,axis=axis)).mean())))
            thumb=Image.fromarray(maps['Albedo']).resize((320,320),Image.Resampling.LANCZOS)
            checker=((np.indices((320,320))[0]//16+np.indices((320,320))[1]//16)%2)*24+60
            card=Image.new('RGB',(344,370),(24,27,31));bg=Image.fromarray(np.repeat(checker[...,None],3,axis=2).astype(np.uint8))
            bg.paste(thumb,(0,0),thumb.getchannel('A'));card.paste(bg,(12,12))
            ImageDraw.Draw(card).text((12,340),name,font=font,fill=(230,230,230));tiles.append(card)
            if i==0:
                tile=Image.fromarray(maps['Albedo']).resize((384,384),Image.Resampling.LANCZOS)
                repeat=Image.new('RGBA',(768,768))
                for yy in (0,384):
                    for xx in (0,384):repeat.paste(tile,(xx,yy))
                repeat.save(EXAMPLES/(family+' - Repeat.png'))
            del f,r,maps
        for channel,atlas in atlases.items():Image.fromarray(atlas).save(folder/(channel+'.png'))
        sheet=Image.new('RGB',(1032,1110),(24,27,31))
        for i,card in enumerate(tiles):sheet.paste(card,((i%3)*344,(i//3)*370))
        sheet.save(EXAMPLES/(family+' - Catalog.png'));cards.append((fi,sheet.resize((516,555))))
        entry=dict(name=family,names=names,seedBase=52000+fi*100,channels=list(atlases),
                   wearProfile='none' if fi<6 else 'minor micro wear',materials=metadata,seconds=round(time.perf_counter()-t,2))
        if family=='Chainmail and Scale Armor':
            from mail_geometry import RING_NAMES
            entry['displayNames']=RING_NAMES+names[4:]
        catalog['families']=[fam for fam in catalog['families'] if fam['name']!=family]+[entry]
        (ROOT/'Catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf-8')
        print(f'Generated {family}: 9 materials at {size}px, {time.perf_counter()-t:.1f}s',flush=True)
        del atlases
    if not only:
        overview=Image.new('RGB',(2064,1110))
        for i,card in cards:overview.paste(card,((i%4)*516,(i//4)*555))
        overview.save(EXAMPLES/'Expansion Library Overview.png')
    (EXAMPLES/('Expansion Seam Measurements'+(' - '+only if only else '')+'.json')).write_text(json.dumps(measurements,indent=2),encoding='utf-8')
    print(f'Total {time.perf_counter()-start:.1f}s',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--size',type=int,default=1024)
    parser.add_argument('--family',choices=FAMILIES)
    args=parser.parse_args();generate(args.size,args.family)
