"""Periodic mail geometry bake. All channels share one depth-tested surface.

Ring mail: independent metal rings fastened to opaque leather with stitches.
Chain mail: alternating tilted ring planes; four neighbouring links cross each
ring with alternating depth order. Open holes retain coverage in every map.
No lighting is baked into albedo. PNG normals are tangent-space +Y.
"""
import numpy as np
from PIL import Image
from generate_materials import Fields, sat, smooth, rgb

def gaussian_filter(field, sigma, mode='wrap'):
    """Periodic Gaussian convolution; no image borders or optional dependencies."""
    fy=np.fft.fftfreq(field.shape[0])[:,None]
    fx=np.fft.rfftfreq(field.shape[1])[None,:]
    kernel=np.exp(-2*np.pi*np.pi*sigma*sigma*(fx*fx+fy*fy))
    return np.fft.irfft2(np.fft.rfft2(field)*kernel,s=field.shape).astype(np.float32)

RING_NAMES = ["Steel Rings on Chestnut Leather", "Brass Rings on Oxblood Leather",
              "Steel Rings on Black Leather", "Flat Rings on Brown Leather"]
CHAIN_NAMES = ["Interlinked Round Steel", "Riveted Flat Steel Chain",
               "Fine Polished Steel Chain", "Dark Oiled Steel Chain"]


class Surface:
    def __init__(self, n, seed, leather=None):
        self.n = n
        self.z = np.full((n, n), -10., np.float32)
        self.color = np.zeros((n, n, 3), np.float32)
        self.normal = np.zeros_like(self.color); self.normal[..., 2] = 1
        self.rough = np.full((n, n), .4, np.float32)
        self.metal = np.zeros((n, n), np.float32)
        self.alpha = np.zeros((n, n), np.float32)
        if leather is not None:
            f = Fields(n, seed)
            broad = f.fbm(12, 123, 3)
            _, gap = f.cells(170, 35)
            grain = smooth(.005, .09, gap)
            micro = f.noise(400, 811)
            self.z[:] = .025 + .009 * grain + .002 * (micro - .5)
            self.color[:] = rgb(leather) * (.93 + .09*broad + .05*(grain-.5))[...,None]
            self.rough[:] = .65 + .05*(1-grain) + .015*(micro-.5)
            dx = (np.roll(self.z,-1,1)-np.roll(self.z,1,1))*n*.014
            dy = (np.roll(self.z,-1,0)-np.roll(self.z,1,0))*n*.014
            self.normal[:] = np.stack((-dx,dy,np.ones_like(dx)),axis=-1)
            self.normal /= np.linalg.norm(self.normal,axis=-1)[...,None]
            self.alpha[:] = 1

    def ring(self,cx,cy,cz,R,r,tilt,color,rough,flatten=1.):
        # Ray-march the exact torus SDF. This avoids stamped segments, faceted
        # highlights and normal seams on the continuous circular wire.
        co=np.cos(tilt); si=np.sin(tilt); n=self.n
        for oy in (-1,0,1):
            for ox in (-1,0,1):
                x0=max(0,int(np.floor((cx+ox-R-r)*n)))
                x1=min(n,int(np.ceil((cx+ox+R+r)*n)))
                y0=max(0,int(np.floor((cy+oy-R*co-r)*n)))
                y1=min(n,int(np.ceil((cy+oy+R*co+r)*n)))
                if x1<=x0 or y1<=y0: continue
                yy,xx=np.mgrid[y0:y1,x0:x1].astype(np.float32)
                x=(xx+.5)/n-cx-ox; y=(yy+.5)/n-cy-oy
                if abs(si)<1e-6:
                    radial=np.sqrt(x*x+y*y); dr=radial-R
                    hit=abs(dr)<r
                    z=np.sqrt(np.maximum(0,r*r-dr*dr))*flatten
                    nx=dr*x/np.maximum(radial,1e-8)*flatten
                    ny=dr*y/np.maximum(radial,1e-8)*flatten
                    nz=z/max(flatten,1e-4)
                else:
                    z=np.full_like(x,R*abs(si)+r)
                    hit=np.zeros_like(x,dtype=bool)
                    for _ in range(90):
                        ly=y*co+z*si; lz=-y*si+z*co
                        radial=np.sqrt(x*x+ly*ly)
                        distance=(np.sqrt((radial-R)**2+(lz/flatten)**2)-r)*flatten
                        hit |= distance<.00002/8
                        active=(~hit)&(z>-(R*abs(si)+r))
                        if not active.any(): break
                        z=np.where(active,z-np.maximum(distance,1e-7),z)
                    ly=y*co+z*si; lz=-y*si+z*co
                    radial=np.sqrt(x*x+ly*ly)
                    dr=radial-R
                    nx=dr*x/np.maximum(radial,1e-8)
                    gy=dr*ly/np.maximum(radial,1e-8)
                    ny=gy*co-lz*si/(flatten*flatten); nz=gy*si+lz*co/(flatten*flatten)
                region=np.s_[y0:y1,x0:x1]
                take=hit & (cz+z>self.z[region])
                normal=np.stack((nx,-ny,nz),axis=-1)
                normal/=np.maximum(np.linalg.norm(normal,axis=-1)[...,None],1e-8)
                self.z[region][take]=(cz+z)[take]
                self.normal[region][take]=normal[take]
                self.color[region][take]=color
                self.rough[region][take]=rough
                self.metal[region][take]=1
                self.alpha[region][take]=1

    def tube(self, points, radius, color, rough=.3, metal=1., flatten=1.):
        # Periodic copies retain identical ring identities and welds at tile edges.
        low=points[:,:2].min(axis=0)-radius; high=points[:,:2].max(axis=0)+radius
        for oy in (-1,0,1):
            for ox in (-1,0,1):
                if high[0]+ox<0 or low[0]+ox>1 or high[1]+oy<0 or low[1]+oy>1: continue
                p=points+np.array([ox,oy,0])
                for a,b in zip(p[:-1],p[1:]): self.segment(a,b,radius,color,rough,metal,flatten)

    def segment(self,a,b,r,color,rough,metal,flatten):
        n=self.n
        x0=max(0,int(np.floor((min(a[0],b[0])-r)*n)))
        y0=max(0,int(np.floor((min(a[1],b[1])-r)*n)))
        x1=min(n,int(np.ceil((max(a[0],b[0])+r)*n))+1)
        y1=min(n,int(np.ceil((max(a[1],b[1])+r)*n))+1)
        if x1<=x0 or y1<=y0: return
        yy,xx=np.mgrid[y0:y1,x0:x1].astype(np.float32)
        xx=(xx+.5)/n; yy=(yy+.5)/n
        d=b-a; len2=np.dot(d[:2],d[:2])
        t=np.clip(((xx-a[0])*d[0]+(yy-a[1])*d[1])/max(len2,1e-12),0,1)
        qx=xx-a[0]-t*d[0]; qy=yy-a[1]-t*d[1]
        distance2=qx*qx+qy*qy
        cap=np.sqrt(np.maximum(0,r*r-distance2))
        z=a[2]+t*d[2]+cap*flatten
        region=np.s_[y0:y1,x0:x1]
        take=(distance2<r*r)&(z>self.z[region])
        if not take.any(): return
        # Analytic rounded-wire normal, including slope of the tilted ring plane.
        nx=qx*flatten-cap*d[2]*d[0]/max(len2,1e-12)
        ny=qy*flatten-cap*d[2]*d[1]/max(len2,1e-12)
        normal=np.stack((nx,-ny,cap),axis=-1)
        normal/=np.maximum(np.linalg.norm(normal,axis=-1)[...,None],1e-10)
        self.z[region][take]=z[take]
        self.normal[region][take]=normal[take]
        self.color[region][take]=color
        self.rough[region][take]=rough
        self.metal[region][take]=metal
        self.alpha[region][take]=1


def bake(size, index, chain=False, seed=76000):
    # Two samples on each axis; geometry has no resolution-dependent parameters.
    n=size*2
    leather=[(77,42,25),(65,25,22),(20,21,23),(83,52,32)]
    s=Surface(n,seed+index,None if chain else leather[index])
    cols=([8,8,14,10] if chain else [8,8,10,10])[index]
    rows=cols*([3,3,4,4][index]) if chain else cols
    cell=1./cols
    radius=(.450 if chain else .405)*cell
    wire=([.056,.060,.048,.055] if chain else [.043,.059,.032,.050])[index]*cell
    steel=[(184,191,198),(180,183,187),(203,209,214),(100,110,122)]
    colors=steel if chain else [(190,198,204),(190,147,65),(153,162,173),(173,181,188)]
    rng=np.random.default_rng(seed+index+int(chain)*100)
    theta=np.linspace(0,2*np.pi,161)
    for row in range(rows):
        for col in range(cols):
            cx=(col+.5+.5*(row%2))/cols; cy=(row+.5)/rows
            # Bounded, per-ring dimensional irregularity; no surface wear.
            R=radius*(1+rng.uniform(-.012,.012))
            tilt=(np.deg2rad([40,32,55,50][index]+rng.uniform(-1,1))*(1 if row%2 else -1)) if chain else 0
            phase=rng.uniform(-.08,.08)
            t=theta+phase
            zbase=.53*cell if chain else .065*cell
            pts=np.stack((cx+R*np.cos(t),cy+R*np.sin(t)*np.cos(tilt),
                          zbase+R*np.sin(t)*np.sin(tilt)),axis=-1)
            # Leather base is in local height units; offset all metal above it.
            if not chain: pts[:,2]+=.034
            color=rgb(colors[index])*rng.uniform(.975,1.025)
            rough=([.27,.34,.22,.32] if chain else [.27,.30,.34,.32])[index]+rng.uniform(-.012,.012)
            flat=.45 if (chain and index==1) or (not chain and index==3) else 1
            s.ring(cx,cy,zbase if chain else zbase+.034,R,wire,tilt,color,rough,flat)
            if chain and index==1:
                # Overlap tab and small rivet head on each flattened link.
                angle=.25*np.pi+phase
                q=np.array([cx+R*np.cos(angle),cy+R*np.sin(angle)*np.cos(tilt),zbase+R*np.sin(angle)*np.sin(tilt)+wire*.35])
                tangent=np.array([-np.sin(angle),np.cos(angle)*np.cos(tilt),np.cos(angle)*np.sin(tilt)])
                s.tube(np.stack((q-tangent*wire*.9,q+tangent*wire*.9)),wire*1.3,color*.98,rough,1,.4)
                q[2]+=wire*.45
                s.tube(np.stack((q,q+np.array([1e-7,0,0]))),wire*.53,color*1.015,.29,1)
            if not chain:
                # Four radial retaining stitches cross OVER the ring and enter leather.
                for angle in (0,np.pi/2,np.pi,3*np.pi/2):
                    for side in (-1,1):
                        u=np.linspace(-1,1,17)
                        rr=R+u*wire*1.9
                        tangential=side*wire*.38
                        p=np.stack((cx+rr*np.cos(angle)-tangential*np.sin(angle),
                                    cy+rr*np.sin(angle)+tangential*np.cos(angle),
                                    .034+(.065*cell+wire*flat+.003)*np.cos(u*np.pi*.5)),axis=-1)
                        stitch=rgb([(138,103,65),(107,66,40),(75,70,60),(153,120,77)][index])
                        s.tube(p,wire*.15,stitch,.82,0)
    alpha=s.alpha
    # AO comes from neighbouring relief, never from pigment or a fake ring highlight.
    ground=np.where(alpha>0,s.z,0)
    near=gaussian_filter(ground,n*.002,mode='wrap')
    ao=1-np.clip((near-ground)/(cell*.15),0,.28)
    if chain: ao=np.where(alpha>0,np.clip(ao,.72,1),1)
    else: ao-=gaussian_filter((s.metal>.5).astype(float),n*.0015,mode='wrap')*(1-s.metal)*.20
    # NormalControl height is an alternative to Normal, not an extra bump layer.
    height=sat(ground/(cell*.98 if chain else .034+cell*.19))
    def resize(a):
        return np.asarray(Image.fromarray(a.astype(np.float32),'F').resize((size,size),Image.Resampling.BOX))
    coverage=resize(alpha)
    def filtered(a, neutral=0):
        if a.ndim==3: return np.stack([filtered(a[...,i],neutral) for i in range(a.shape[-1])],axis=-1)
        return np.where(coverage>1e-7,resize(a*alpha)/np.maximum(coverage,1e-7),neutral)
    normal=filtered(s.normal); normal/=np.maximum(np.linalg.norm(normal,axis=-1)[...,None],1e-7)
    normal[coverage==0]=(0,0,1)
    u8=lambda a: np.round(sat(a)*255).astype(np.uint8)
    rgba=lambda a: u8(np.concatenate((a,coverage[...,None]),axis=-1))
    gray=lambda a: np.repeat(filtered(a)[...,None],3,axis=-1)
    return {'Albedo':rgba(filtered(s.color)), 'Normal':rgba(normal*.5+.5),
            'Roughness':rgba(gray(s.rough)), 'Metallic':rgba(gray(s.metal)),
            'AmbientOcclusion':rgba(gray(ao)),
            'Height':np.round(filtered(height)*65535).astype(np.uint16)}
