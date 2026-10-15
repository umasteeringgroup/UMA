"""Validate source PNGs without modifying Unity assets. Run from project root."""
import json
from pathlib import Path
import numpy as np
from PIL import Image

root=Path("Assets/UMA/OverlayPainter/Textures/Tileable Materials")
output=Path("Assets/UMA/OverlayPainterExamples/Tileable Materials/Source Validation.txt")
catalog=json.loads((root/"Catalog.json").read_text())
n,p,g=catalog["tileSize"],catalog["padding"],catalog["grid"]
stride=n+2*p
lines=[]
for family in catalog["families"]:
    g=family.get('grid',catalog['grid'])
    maps={ch:np.array(Image.open(root/family["name"]/(ch+".png"))) for ch in family["channels"]}
    assert all(im.shape[:2]==(stride*g,stride*g) for im in maps.values())
    for i,name in enumerate(family["names"]):
        row,col=divmod(i,g);ys=slice(row*stride+p,row*stride+p+n);xs=slice(col*stride+p,col*stride+p+n)
        tiles={ch:im[ys,xs] for ch,im in maps.items()}
        for ch,im in maps.items():
            original=im[row*stride:(row+1)*stride,col*stride:(col+1)*stride]
            tile=tiles[ch]
            expected=np.pad(tile,((p,p),(p,p))+((0,0),)*(tile.ndim-2),mode="wrap")
            assert np.array_equal(original,expected), (name,ch,"border contamination")
            # Check seams against the distribution of normal interior transitions,
            # not first==last (which would duplicate a texel and distort sampling).
            a=tile.astype(np.float32)
            for axis in (0,1):
                transitions=np.abs(np.diff(a,axis=axis))
                seam=np.abs(np.take(a,0,axis)-np.take(a,-1,axis))
                # Regular scale/knit row transitions can occupy <1% of pixels.
                # Compare whole seam lines with whole interior lines as well.
                interior_lines=np.moveaxis(transitions,axis,0).reshape(transitions.shape[axis],-1).mean(axis=1)
                assert seam.mean() <= max(np.percentile(transitions,99),interior_lines.max())+1, (name,ch,axis,"seam outlier")
        h=tiles["Height"].astype(np.float32)/65535
        assert tiles["Height"].max()>255, "Height source lost 16-bit precision"
        metadata=family.get('materials', [None]*9)[i]
        strength=metadata['normalStrength'] if metadata else .004 if family["name"]=="Aged Metals" else .0028 if family["name"]=="Denim" else .0035
        dx=(np.roll(h,-1,1)-np.roll(h,1,1))*n*.5*strength
        dy=(np.roll(h,-1,0)-np.roll(h,1,0))*n*.5*strength
        expected=np.stack((-dx,dy,np.ones_like(h)),axis=-1)
        expected/=np.linalg.norm(expected,axis=-1)[...,None]
        normal=tiles["Normal"][...,:3].astype(np.float32)/255*2-1
        error=np.max(np.abs(expected-normal))
        if metadata and metadata.get('normalMode')=='analyticSurface':
            # Geometry normals remain smooth at overlapping link silhouettes;
            # differentiating the visible depth discontinuity would be incorrect.
            error=float(np.max(abs(np.linalg.norm(normal,axis=-1)-1)))
            assert error<.012,(name,'non-unit geometry normal',error)
            assert normal[...,2].min()>-.005,(name,'back-facing geometry normal')
        else: assert error<.0041,(name,"normal not aligned with height",error)
        if metadata:
            assert metadata['wearArea'] < .002, (name, 'excessive wear')
            if family['wearProfile']=='none': assert metadata['wearArea']==0
            alpha=tiles['Albedo'][...,3]
            assert bool(np.any(alpha<252))==metadata['transparent'],(name,'missing transparency')
            for channel in ('Normal','Roughness','Metallic','AmbientOcclusion'):
                assert np.array_equal(alpha,tiles[channel][...,3]),(name,channel,'coverage mismatch')
            dielectric=metadata['dielectric']
        else: dielectric=family['name']!='Aged Metals'
        if dielectric: assert np.max(tiles["Metallic"][...,:3])==0,(name,"dielectric metallic")
        # Base materials should not contain dominant cloudy wear. Tartan's deliberate
        # dyed pattern is exempt; average out the yarn/grain before measuring aging.
        if not metadata and name!="Red Heritage Tartan":
            albedo=tiles["Albedo"].astype(np.float32)/255
            block=n//16
            low=albedo.reshape(16,block,16,block,3).mean(axis=(1,3))
            contrast=float(np.max(np.ptp(low,axis=(0,1))))
            assert contrast<.06,(name,"broad wear overpowers base",contrast)
        lines.append(f"PASS {family['name']} / {name}: six aligned maps; full wrapped gutters; seam transitions within interior range; +Y normal validation error {error:.6f}")
count=sum(len(f['names']) for f in catalog['families'])
lines.append(f"RESULT: {count} materials validated; {count*6} source tiles. No source maps modified by validation.")
output.write_text("\n".join(lines)+"\n",encoding="utf-8")
print(lines[-1])
