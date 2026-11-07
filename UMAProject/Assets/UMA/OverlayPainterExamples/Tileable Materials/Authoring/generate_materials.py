"""Original periodic PBR library. Python 3 + numpy + Pillow; run from project root.

All randomness is seeded per material. Fields are evaluated on a torus; channel
maps share the same fields. This is source authoring, not a runtime dependency.
PNG normals use tangent-space +Y (OpenGL), height is an optional 16-bit source.
"""
import argparse
import json
import time
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path("Assets/UMA/OverlayPainter/Textures/Tileable Materials")
EXAMPLES = Path("Assets/UMA/OverlayPainterExamples/Tileable Materials")
FAMILIES = {
    "Aged Metals": ["Flaking Iron Rust", "Blackened Forged Steel", "Pitted Steel", "Tarnished Brass", "Verdigris Copper", "Oxidized Bronze", "Weathered Zinc", "Chipped Industrial Paint", "Oiled Brushed Steel"],
    "Woven Fabrics": ["Natural Slub Linen", "Heavy Duck Canvas", "Navy Cotton Twill", "Olive Herringbone", "Charcoal Wool Tweed", "Taupe Basket Weave", "Red Heritage Tartan", "Black Ripstop", "Cream Rib Knit"],
    "Aged Leather": ["Saddle Pebble Leather", "Black Fine Grain", "Worn Cognac Hide", "Cracked Oxblood", "Sand Suede", "Black Patent", "Distressed Buffalo Hide", "Peeling Coated Leather", "Embossed Reptile Leather"],
    "Denim": ["Raw Indigo Twill", "Washed Mid Blue", "Light Stonewash", "Acid Wash Indigo", "Black Denim", "Faded Charcoal", "Ecru Denim", "Heavy Slub Indigo", "Abraded Denim"],
}

def sat(x): return np.clip(x, 0, 1)
def smooth(a, b, x):
    t = sat((x-a)/(b-a))
    return t*t*(3-2*t)
def mix(a, b, t): return np.asarray(a)*(1-t[..., None])+np.asarray(b)*t[..., None]
def rgb(value): return np.asarray(value, dtype=np.float32)/255

def micro_wear(f, stream):
    """Sparse sub-cell specks, never broad damage islands (about 1–4px at 1K)."""
    return smooth(.84,.98,f.noise(128,stream))*smooth(.45,.75,f.noise(12,stream+1))

class Fields:
    def __init__(self, n, seed):
        self.n, self.seed = n, seed
        self.y, self.x = np.mgrid[0:n,0:n].astype(np.float32)/n
        self.cache = {}

    def noise(self, cells, stream=0, cy=None):
        cy = cells if cy is None else cy
        key = cells, cy, stream
        if key in self.cache: return self.cache[key]
        rng = np.random.default_rng(self.seed*1009+stream)
        grid = rng.random((cy,cells),dtype=np.float32)
        x,y = self.x*cells,self.y*cy
        ix,iy = x.astype(int),y.astype(int)
        fx,fy = x-ix,y-iy
        fx,fy = fx*fx*(3-2*fx),fy*fy*(3-2*fy)
        z = ((grid[iy%cy,ix%cells]*(1-fx)+grid[iy%cy,(ix+1)%cells]*fx)*(1-fy)
             +(grid[(iy+1)%cy,ix%cells]*(1-fx)+grid[(iy+1)%cy,(ix+1)%cells]*fx)*fy)
        self.cache[key] = z
        return z

    def fbm(self, cells=4, stream=0, octaves=5):
        weights = [0.5**i for i in range(octaves)]
        return sum(w*self.noise(cells*2**i,stream+i*11) for i,w in enumerate(weights))/sum(weights)

    def cells(self, count=32, stream=0, stretch=1):
        """Periodic jittered Voronoi; nearest/second distances form leather grain."""
        nx,ny = count,max(2,int(count*stretch))
        rng = np.random.default_rng(self.seed*1009+stream)
        jitter = rng.uniform(.08,.92,(ny,nx,2)).astype(np.float32)
        x=self.x*nx+.65*(self.noise(nx*2,stream+901)-.5)
        y=self.y*ny+.65*(self.noise(nx*2,stream+902)-.5)
        ix,iy = np.floor(x).astype(int),np.floor(y).astype(int)
        a,b = np.full_like(x,9),np.full_like(x,9)
        for dy in (-1,0,1):
            for dx in (-1,0,1):
                p=jitter[(iy+dy)%ny,(ix+dx)%nx]
                d=(x-ix-dx-p[...,0])**2+(y-iy-dy-p[...,1])**2
                b=np.minimum(b,np.maximum(a,d));a=np.minimum(a,d)
        return np.sqrt(a),np.sqrt(b)-np.sqrt(a)

def woven(f, style, denim=False):
    n=f.n; x,y=f.x,f.y
    broad=f.fbm(3,21);fine=f.noise(384,7); slub=f.noise(14,18,128)
    if denim:
        colors=[(22,40,65),(49,79,111),(123,151,171),(56,81,113),(24,25,29),(71,73,79),(206,198,176),(29,53,77),(60,86,111)]
        base=rgb(colors[style]); count=192 if style!=7 else 144
        warp=y*count+.17*(f.noise(16,3,2)-.5)
        weft=x*count+.12*(f.noise(2,4,16)-.5)
        ix,iy=np.floor(weft).astype(int),np.floor(warp).astype(int)
        over=((ix+iy)%4)<3
        wash=[.003,.012,.018,.025,.003,.012,.003,.006,.012][style]
    else:
        colors=[(174,154,122),(117,103,77),(27,44,63),(71,79,48),(54,53,52),(137,122,103),(112,29,27),(23,27,29),(211,202,179)]
        base=rgb(colors[style]);count=[112,128,160,160,192,112,176,128,96][style]
        warp=y*count+.18*(f.noise(16,3,2)-.5)
        weft=x*count+.15*(f.noise(2,4,16)-.5)
        ix,iy=np.floor(weft).astype(int),np.floor(warp).astype(int)
        if style in (2,3):
            direction=np.where((ix//10)%2==0,iy,-iy) if style==3 else iy
            over=((ix+direction)%4)<3
        elif style==5: over=((ix//2+iy//2)%2)==0
        else: over=((ix+iy)%2)==0
        wash=0
    a=np.maximum(0,np.sin(np.pi*(warp%1)))**.55
    b=np.maximum(0,np.sin(np.pi*(weft%1)))**.55
    thread=np.where(over,a,b)
    h=.30+.22*thread+.016*(fine-.5)
    yarn=f.noise(count,20,3)*.5+f.noise(3,22,count)*.5
    h+=.035*(yarn-.5)
    color=np.broadcast_to(base,(n,n,3)).copy()
    variation=.94+.06*yarn+.03*thread
    color*=variation[...,None]
    if denim:
        # Indigo warp with a separately visible undyed weft; wash follows yarn crowns.
        pale=rgb((125,141,146)) if style not in (4,5) else rgb((66,68,72))
        exposed=(~over)*(.12+.33*thread)*(.7+.3*f.noise(192,113))
        color=mix(color,pale,exposed.astype(np.float32))
        wash_field=smooth(.25,.72,broad)*wash*(.4+.6*thread)
        if style==3: wash_field=f.fbm(12,97)*.025+micro_wear(f,197)*.08
        color=mix(color,rgb((187,194,193)),wash_field)
        if style==7:
            thick=smooth(.60,.77,slub)
            h+=thick*.045;color=mix(color,rgb((136,151,162)),thick*.025)
        if style==8:
            wear=micro_wear(f,85)*.18
            fiber=np.maximum(0,np.sin(2*np.pi*(x*256+.5*f.noise(8,90))))**10
            color=mix(color,rgb((186,188,178)),wear*(.65+.3*fiber))
            h+=wear*fiber*.015
        rough=.82+.02*(broad-.5)+.06*(1-thread)
    else:
        if style==0:
            thicken=smooth(.60,.80,slub);h+=.045*thicken
            color=mix(color,rgb((214,200,172)),thicken*.025)
        if style==4:
            fleck=smooth(.80,.96,f.noise(256,88))
            color=mix(color,rgb((136,125,106)),fleck*.10)
            h+=f.fbm(64,93)*.035
        if style==6:
            # Woven tartan: crossing yarn colors, no baked directional light.
            xx=(x*4)%1; yy=(y*4)%1
            darkx=((xx>.18)&(xx<.46)).astype(float);darky=((yy>.18)&(yy<.46)).astype(float)
            color=mix(color,rgb((26,38,31)),np.where(over,darky,darkx)*.90)
            goldx=((xx>.67)&(xx<.70)).astype(float);goldy=((yy>.67)&(yy<.70)).astype(float)
            color=mix(color,rgb((180,159,112)),np.where(over,goldy,goldx)*.8)
        if style==7:
            grid=np.maximum(np.exp(-((x*16+.5)%1-.5)**2/.005),np.exp(-((y*16+.5)%1-.5)**2/.005))
            h+=grid*.14;color*=1+grid[...,None]*.15
        if style==8:
            # Interlocking knitted loops, with staggered opposing legs.
            phase=x*32+.19*np.cos(2*np.pi*y*48)
            loop=np.exp(-((phase+.5)%1-.5)**2/.033)
            h=.22+.32*loop+.025*fine
            color*= (.97+.035*f.noise(32,98,96))[...,None]
        rough=.87+.025*(broad-.5)
    ao=.75+.25*thread
    return color,h,sat(rough),np.zeros_like(h),ao,.0028 if denim else .0035

def leather(f, style):
    n=f.n; broad=f.fbm(4,10); micro=f.fbm(96,37,3)
    count=[60,110,76,96,160,90,80,76,24][style]
    d,gap=f.cells(count,18,.67 if style in (6,8) else 1)
    grain=smooth(.0,.18,gap)
    creases=1-smooth(.005,.075,gap)
    h=.38+.18*grain+.025*(micro-.5)+.008*(broad-.5)
    colors=[(109,56,28),(24,23,22),(133,69,31),(73,25,27),(165,135,100),(17,16,19),(83,49,30),(53,64,55),(63,36,23)]
    color=np.broadcast_to(rgb(colors[style]),(n,n,3)).copy()
    color*= (.975+.04*(broad-.5)+.035*(micro-.5))[...,None]
    rough=.56+.12*(1-grain)+.015*(broad-.5)
    ao=.82+.18*grain
    if style==2:
        wear=.025*broad*grain+micro_wear(f,121)*.08
        color=mix(color,rgb((181,118,61)),wear*.60);rough+=wear*.12
    if style==3:
        cracks=(1-smooth(.004,.035,gap))*micro_wear(f,199)
        h-=.045*cracks;color=mix(color,rgb((35,19,16)),cracks*.18);rough+=cracks*.035
    if style==4:
        nap=f.noise(400,76)
        color*= (.98+.04*(nap-.5)+.02*(broad-.5))[...,None]
        h=.4+.05*nap+.008*broad;rough=np.full_like(h,.94);ao=.96+.04*nap
    if style==5:
        h=.48+.015*(micro-.5)+.008*(broad-.5);rough=.12+.015*broad;ao=np.ones_like(h)
    if style==6:
        wear=.025*broad*grain+micro_wear(f,131)*.08
        color=mix(color,rgb((156,116,73)),wear*.5);h+=.008*broad;rough+=.035*wear
    if style==7:
        damage=micro_wear(f,65)*.7
        rim=4*damage*(1-damage)
        color=mix(color,rgb((123,100,69)),damage)
        h-=.05*damage;h+=.015*rim;rough=.40+.20*damage+.015*(micro-.5)
        ao*=1-.035*rim
    if style==8:
        # Irregular embossed reptile scales; finer pores sit inside each scale.
        _,edge=f.cells(22,43,.6)
        scales=smooth(.012,.14,edge)
        h=.35+.18*scales+.02*micro;color*= (.94+.04*scales+.02*(broad-.5))[...,None]
        rough=.43+.15*(1-scales)+.015*(broad-.5);ao=.82+.18*scales
    return color,h,sat(rough),np.zeros_like(h),ao,.0035

def metal(f,style):
    n=f.n; broad=f.fbm(3,5); meso=f.fbm(18,27); micro=f.fbm(128,42,3)
    pits=micro_wear(f,64)
    substrate=[(105,105,98),(42,44,43),(137,143,145),(170,137,65),(162,100,62),(129,98,56),(148,158,162),(47,80,82),(101,115,123)][style]
    color=np.broadcast_to(rgb(substrate),(n,n,3)).copy()
    color*= (.975+.035*(broad-.5)+.025*(micro-.5))[...,None]
    h=.50+.015*(micro-.5)-.02*pits
    rough=.42+.04*(meso-.5); metallic=np.full_like(h,.97);ao=1-.06*pits
    if style in (0,3,4,5,7):
        # Only a faint surface film plus tiny isolated corrosion/chip specks.
        damage=.025*meso+micro_wear(f,227)*.65
        if style==7: damage=micro_wear(f,237)*.85
        oxides=[(130,53,19),(0,0,0),(0,0,0),(72,59,29),(48,113,90),(60,84,61),(0,0,0),(139,68,27)]
        oxide=np.broadcast_to(rgb(oxides[style]),(n,n,3)).copy()
        oxide*= (.65+.65*meso+.18*(micro-.5))[...,None]
        if style==0:
            oxide=mix(oxide,rgb((191,104,39)),smooth(.56,.70,meso)*.5)
        color=mix(color,oxide,damage)
        metallic=.97-.94*damage;rough=.42+.35*damage+.025*(meso-.5)
        rim=4*damage*(1-damage)
        h+=(.018 if style!=7 else -.025)*damage+.008*rim+damage*(micro-.5)*.02
        ao*=1-.035*rim
        if style==7:
            # Intact painted areas are dielectric too; exposed small polished flecks.
            metallic=damage*.12;rough=.41+.25*damage+.015*(meso-.5)
            fresh=smooth(.66,.74,meso)*damage*.5
            color=mix(color,rgb((122,121,109)),fresh);metallic+=fresh*.8
    if style==1:
        hammered=smooth(.15,.72,meso)
        h+=.035*hammered;rough=.54+.025*(broad-.5);metallic=.70+.025*(meso-.5)
    if style==2:
        pits=micro_wear(f,164);h-=.035*pits;ao=1-.08*pits
        color=mix(color,rgb((64,64,58)),pits*.08);rough=.43+.10*pits+.02*(meso-.5)
    if style==6:
        d,g=f.cells(32,70)
        crystal=smooth(.06,.3,g)
        color*= (.94+.035*crystal+.035*meso)[...,None]
        h+=.012*crystal;rough=.55+.025*(broad-.5);metallic=.77+.035*(meso-.5)
    if style==8:
        brushing=f.noise(512,88,4)
        oil=smooth(.43,.70,broad)
        h+=.012*(brushing-.5);color*= (.98+.035*(brushing-.5))[...,None]
        rough=.30+.08*(brushing-.5)-.012*oil;ao=np.ones_like(h)
    return color,h,sat(rough),sat(metallic),sat(ao),.004

def encode(f, family, index):
    result=(metal(f,index) if family=="Aged Metals" else leather(f,index) if family=="Aged Leather"
            else woven(f,index,family=="Denim"))
    color,h,rough,metallic,ao,relief=result
    # Y in the image points down; tangent +Y points up. Wrapped derivatives.
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*f.n*.5*relief
    dy=(np.roll(h,-1,0)-np.roll(h,1,0))*f.n*.5*relief
    norm=np.stack((-dx,dy,np.ones_like(h)),axis=-1)
    norm/=np.linalg.norm(norm,axis=-1)[...,None]
    u8=lambda a: np.round(sat(a)*255).astype(np.uint8)
    gray=lambda a: np.repeat(u8(a)[...,None],3,axis=-1)
    return {"Albedo":u8(color),"Normal":u8(norm*.5+.5),"Roughness":gray(rough),
            "Metallic":gray(metallic),"AmbientOcclusion":gray(ao),"Height":np.round(sat(h)*65535).astype(np.uint16)}

def generate(size=1024):
    start=time.perf_counter();ROOT.mkdir(parents=True,exist_ok=True);EXAMPLES.mkdir(parents=True,exist_ok=True)
    catalog={"version":2,"wearProfile":"very minor aging; sparse micro-chips","tileSize":size,"padding":8,"grid":3,"normalEncoding":"tangent +Y / OpenGL","families":[]}
    report=[]; cards=[]
    try: font=ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf",20)
    except OSError: font=ImageFont.load_default()
    for fi,(family,names) in enumerate(FAMILIES.items()):
        folder=ROOT/family;folder.mkdir(exist_ok=True)
        p=8;stride=size+p*2;atlases={}; thumbs=[]
        family_start=time.perf_counter()
        for i,name in enumerate(names):
            maps=encode(Fields(size,41000+fi*100+i),family,i)
            for channel,im in maps.items():
                if channel not in atlases: atlases[channel]=np.zeros((stride*3,stride*3)+im.shape[2:],im.dtype)
                padded=np.pad(im,((p,p),(p,p))+((0,0),)*(im.ndim-2),mode="wrap")
                row,col=divmod(i,3);atlases[channel][row*stride:(row+1)*stride,col*stride:(col+1)*stride]=padded
                # A legitimate periodic field needn't have identical first/last texels.
                # Compare wrap derivative with interior derivatives, recording each axis.
                v=im.astype(float)
                for axis in (0,1):
                    interior=np.mean(np.abs(np.diff(v,axis=axis)))
                    seam=np.mean(np.abs(np.take(v,0,axis)-np.take(v,-1,axis)))
                    report.append({"family":family,"material":name,"channel":channel,"axis":axis,"seamMean":float(seam),"interiorMean":float(interior),"ratio":float(seam/max(interior,.001))})
            thumb=Image.fromarray(maps["Albedo"]).resize((320,320),Image.Resampling.LANCZOS)
            card=Image.new("RGB",(344,370),(24,27,31));card.paste(thumb,(12,12));ImageDraw.Draw(card).text((12,340),name,font=font,fill=(230,230,230));thumbs.append(card)
            # One repeat demonstration per family: true wrap, not offset painting.
            if i==0:
                tile=Image.fromarray(maps["Albedo"]).resize((384,384),Image.Resampling.LANCZOS)
                repeat=Image.new("RGB",(768,768))
                for yy in (0,384):
                    for xx in (0,384):repeat.paste(tile,(xx,yy))
                repeat.save(EXAMPLES/(family+" - Repeat.png"))
        for channel,atlas in atlases.items(): Image.fromarray(atlas).save(folder/(channel+".png"))
        sheet=Image.new("RGB",(1032,1110),(24,27,31))
        for i,card in enumerate(thumbs):sheet.paste(card,((i%3)*344,(i//3)*370))
        sheet.save(EXAMPLES/(family+" - Catalog.png"));cards.append(sheet.resize((516,555)))
        catalog["families"].append({"name":family,"names":names,"seedBase":41000+fi*100,"channels":list(atlases),"seconds":round(time.perf_counter()-family_start,2)})
        print(f"Generated {family}: 9 materials, {size}px, {time.perf_counter()-family_start:.1f}s",flush=True)
    montage=Image.new("RGB",(1032,1110))
    for i,card in enumerate(cards):montage.paste(card,((i%2)*516,(i//2)*555))
    montage.save(EXAMPLES/"Material Library Overview.png")
    if (ROOT/"Catalog.json").exists():
        previous=json.loads((ROOT/"Catalog.json").read_text(encoding="utf-8"))
        extras=[f for f in previous['families'] if f['name'] not in FAMILIES]
        if extras:
            if previous['tileSize']!=size: raise ValueError('Tile size must match the installed expansion.')
            catalog['families'].extend(extras)
            catalog['version']=max(catalog['version'],previous['version'])
            catalog['wearProfile']=previous['wearProfile']
    (ROOT/"Catalog.json").write_text(json.dumps(catalog,indent=2),encoding="utf-8")
    (EXAMPLES/"Seam Measurements.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print(f"Total {time.perf_counter()-start:.1f}s",flush=True)

if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--size",type=int,default=1024)
    generate(parser.parse_args().size)
