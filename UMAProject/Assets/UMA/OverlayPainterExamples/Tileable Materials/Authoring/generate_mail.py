"""Rebuild leather-backed ring mail and add four interlinked chain-mail materials.
Run from the project root. Requires numpy and Pillow.
The four existing atlas positions retain their imported sprite identities.
"""
import sys
sys.dont_write_bytecode=True
import json
import time
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from generate_materials import ROOT, EXAMPLES
from generate_expansion import generate
from mail_geometry import bake, RING_NAMES, CHAIN_NAMES

def main():
    started=time.perf_counter()
    generate(1024,'Chainmail and Scale Armor')
    catalog=json.loads((ROOT/'Catalog.json').read_text(encoding='utf-8'))
    old=next(f for f in catalog['families'] if f['name']=='Chainmail and Scale Armor')
    old['displayNames']=RING_NAMES+old['names'][4:]
    old['construction']='First four tiles: rings stitched to opaque leather. Remaining scale and lamellar tiles unchanged.'
    folder=ROOT/'Interlinked Chain Mail'; folder.mkdir(exist_ok=True)
    atlases={}; size=1024; padding=8; stride=size+2*padding; diagnostics=[]
    for i,name in enumerate(CHAIN_NAMES):
        begin=time.perf_counter(); maps=bake(size,i,chain=True)
        for channel,tile in maps.items():
            if channel not in atlases: atlases[channel]=np.zeros((stride*2,stride*2)+tile.shape[2:],tile.dtype)
            padded=np.pad(tile,((padding,padding),(padding,padding))+((0,0),)*(tile.ndim-2),mode='wrap')
            row,col=divmod(i,2); atlases[channel][row*stride:(row+1)*stride,col*stride:(col+1)*stride]=padded
            assert np.isfinite(tile).all()
            if channel!='Height': assert np.array_equal(tile[...,3],maps['Albedo'][...,3])
            for axis in (0,1):
                a=tile.astype(float)
                diagnostics.append(dict(material=name,channel=channel,axis=axis,
                    seam=float(np.abs(np.take(a,0,axis)-np.take(a,-1,axis)).mean()),
                    interior=float(np.abs(np.diff(a,axis=axis)).mean())))
        # Relit diagnostic made from the authored normals, not baked into the maps.
        normal=maps['Normal'][...,:3]/255.*2-1
        light=np.array([-.4,.5,.77]); light/=np.linalg.norm(light)
        diffuse=np.maximum(0,np.sum(normal*light,axis=-1))
        spec=np.maximum(0,np.sum(normal*np.array([-.21,.27,.94]),axis=-1))**28
        color=maps['Albedo'][...,:3]/255.*(.25+.7*diffuse[...,None])+spec[...,None]*.6
        alpha=maps['Albedo'][...,3:4]/255.
        color=color*alpha+np.array([.065,.07,.08])*(1-alpha)
        Image.fromarray(np.uint8(np.clip(color,0,1)*255)).save(EXAMPLES/(name+' - Relief.png'))
        print(name+': '+str(round(time.perf_counter()-begin,2))+'s',flush=True)
    for channel,atlas in atlases.items(): Image.fromarray(atlas).save(folder/(channel+'.png'))
    entry=dict(name='Interlinked Chain Mail',names=CHAIN_NAMES,grid=2,tileSize=size,padding=padding,
               channels=list(atlases),wearProfile='none',construction='Alternating tilted interlinked rings; transparent openings; four-in-one arrangement',
               materials=[dict(name=name,normalStrength=1.,normalMode='analyticSurface',wearArea=0.,transparent=True,dielectric=False) for name in CHAIN_NAMES])
    catalog['families']=[f for f in catalog['families'] if f['name']!=entry['name']]+[entry]
    (ROOT/'Catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf-8')
    (EXAMPLES/'Mail Seam Measurements.json').write_text(json.dumps(diagnostics,indent=2),encoding='utf-8')
    print('Mail generation completed in '+str(round(time.perf_counter()-started,2))+'s',flush=True)

if __name__=='__main__': main()
