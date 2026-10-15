"""Arrange Unity-rendered validation panels into the example material catalogs.
Run RenderMailCatalog.cs.txt in Unity first. These are labeled contact sheets,
not source maps; lighting exists only in these preview images.
"""
import json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
root=Path('Assets/UMA/OverlayPainterExamples/Tileable Materials')
catalog=json.loads(Path('Assets/UMA/OverlayPainter/Textures/Tileable Materials/Catalog.json').read_text(encoding='utf-8'))
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
heading=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',23)
for family in catalog['families']:
    if family['name'] not in ('Chainmail and Scale Armor','Interlinked Chain Mail'): continue
    names=family.get('displayNames',family['names'])
    cols=family.get('grid',3); rows=(len(names)+cols-1)//cols
    image=Image.new('RGB',(cols*344,64+rows*370),(24,27,31))
    draw=ImageDraw.Draw(image)
    draw.text((12,12),family['name']+' — Unity URP Lit',font=heading,fill='white')
    for i,name in enumerate(names):
        panel=Image.open(Path('Build/Validation/TileableMaterials/Renders')/(family['name']+'_'+str(i)+'.png'))
        x=(i%cols)*344; y=64+(i//cols)*370
        image.paste(panel.resize((320,320),Image.Resampling.LANCZOS),(x+12,y))
        draw.text((x+12,y+326),name,font=font,fill=(230,230,230))
    image.save(root/(family['name']+' - PBR Catalog.png'))
    image.save(root/(family['name']+' - Catalog.png'))
families=catalog['families'][4:]
overview=Image.new('RGB',(2064,((len(families)+3)//4)*587),(24,27,31))
for i,family in enumerate(families):
    path=root/(family['name']+' - PBR Catalog.png')
    if not path.exists(): continue
    panel=Image.open(path); panel.thumbnail((516,587),Image.Resampling.LANCZOS)
    overview.paste(panel,((i%4)*516,(i//4)*587))
overview.save(root/'Expansion PBR Overview.png')
overview.save(root/'Expansion Library Overview.png')
print('Updated ring-mail and chain-mail catalogs and library overviews from Unity renders.')
