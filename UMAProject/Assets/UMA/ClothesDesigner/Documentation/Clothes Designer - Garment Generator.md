# Clothes Designer: garment generator

The garment generator drafts a finished, sewn pattern from a garment type and a few style values, measured to the body in the Clothes Designer, and drapes it while you watch. Change a slider and it drafts and drapes again. The result is an ordinary pattern: fit it, edit it on the Pattern Canvas, paint it with the Fit Brush, bake it.

This is the reference for every type, preset and slider. To learn it step by step, do [Tutorial 3: The Garment Generator](Clothes%20Designer%20-%20Tutorial%203%20-%20The%20Garment%20Generator.md). All guides are listed in [Start Here](!Clothes%20Designer%20-%20Start%20Here.md).

## Opening it

In the Clothes Designer's **Pattern** section, press **Generator** (or **UMA > Clothes Designer > Garment Generator**). It works on the body in the open Clothes Designer; with none open it offers **Create Clothes on Race...**.

## Using it

1. Pick a **type** with the tiles at the top: **Top**, **Dress**, **Kimono**, **Trousers**, **Skirt** or **Jumpsuit**.
2. Pick a **Preset** to start from: the type's garments, from a T-shirt to a hoodie, a sundress to a gown, jeans to joggers (the full list is under [Presets](#presets)). Each says what it is under the popup. **Reset** goes back to the preset's values.
3. Press **Generate**. The pattern is drafted to the body (in a few milliseconds), laid round it and draped.
4. Move the sliders. With **Live** on (the default), the garment is drafted and draped again as soon as you let go of a slider. Moving one makes the style *Custom*; the pattern is named after the preset it came from ("Hoodie (Custom)").
5. **Done** packs the UVs tightly (live drafts lay them out quickly; the bake does this too if you skip it). **Save...** saves the pattern and the fit. The pattern keeps its style, so opening it later and pressing Generator picks up where you left off.

Generating replaces the pattern being edited. While it is the generator's own pattern, unchanged since it was drafted, that happens without asking; once you have edited it (on the Pattern Canvas, say), Live stops and **Generate** asks first, offering to save.

## Presets

| Type | Presets |
| --- | --- |
| **Top** | T-Shirt, Long-Sleeve Tee, Tank Top, Crop Top, Polo Shirt, Shirt, Blouse, Hoodie, Turtleneck, Cardigan, Jacket |
| **Dress** | T-Shirt Dress, Sheath Dress, Sundress, Maxi Dress, Gown, Shirt Dress |
| **Kimono** | Kimono, Robe, Haori |
| **Trousers** | Trousers, Jeans, Wide-Leg Trousers, Joggers, Leggings, Shorts, Hot Pants, Bermuda Shorts, Boxer Shorts |
| **Skirt** | Mini Skirt, A-Line Skirt, Pencil Skirt, Pleated Skirt, Full Skirt, Maxi Skirt |
| **Jumpsuit** | Jumpsuit, Romper, Boiler Suit, Catsuit |

## What you can set

**Length and Fit** (tops, dresses, kimonos; skirts)

- **Length**: where the hem falls, on the body's own heights: Crop, Waist, High Hip, Hip, Mini, Above Knee, Knee, Midi, Ankle, Floor. The label under the slider says where that is on this body.
- **Ease**: room round the chest (tops) or the hips (trousers, skirts), in cm. 4 fitted, 10 regular, 20 loose, 30 oversized; below zero the cloth is cut smaller than the body, for stretch fabrics worn tight (leggings, a crop top).
- **Waist Shaping**: how far the sides follow the waist in. Fitted woven cloth also gets waist darts.
- **Flare**: how far the hem flares out below the hips. The side corners rise as the hem flares, so it hangs level and as long as drafted. A full skirt is cut in four gores.
- **Shirt Tail**: how much lower the middle of the hem is than its sides.
- **Shoulders**: wider (dropped shoulders, for oversized tops) or narrower than the body's.
- Skirts: **Waist Height**, **Flare** (0 a pencil skirt, darted at the waist; 0.35 an A-line; 1 a full skirt) and **Pleats** (pressed knife pleats on each panel).

**Legs** (trousers, jumpsuits): **Leg Length** (Hot Pants to Full Length), **Rise** (low on the hips to the waist), **Thigh Width**, **Hem Width** (skinny, straight, flared), **Waistband** and **Ankle Cuffs**.

**Neck and Collar**: the neckline (**Crew**, **Scoop**, **V**, **Square**, **Boat**), its **Width** and **Front** and **Back Depth**; the **Collar**: **Band** (a stand), **Shirt** (a firm stand with a leaf turned down over it, hanging 2.5 cm past the stand at the back so the stand does not show; the **Polo Shirt**, **Shirt**, **Shirt Dress** and **Boiler Suit** presets use it), **Hood** (two panels sized to the head and the neckline) or **Turtleneck**.

**Sleeves**: **Length** as a share of the arm (sleeveless, cap, short, elbow, three-quarter, long), **Width** (room round the upper arm; wide sleeves deepen the armhole), **Opening** (close at the hem to a wide bell), **Cuffs**; sleeveless, the **Strap Width**. Sleeve caps are cut to the armholes' length plus a little ease, so they sew in flat.

**Front and Hem**: **Closed**, **Open** (two fronts meeting at the middle, unsewn) or **Wrap** (two fronts crossing left over right by **Overlap**, tacked together at the waist); a **Hem Band**.

Bands (cuffs, hem bands, waistbands) are **Same Fabric** (the length of what they are sewn to) or **Rib** (cut shorter, so they gather the edge in and grip). A waistband in the garment's own fabric is cut to the body at both its edges, worn snug.

**Fabric**: the garment's cloth, and the **Trim Fabric** for a turtleneck and same-fabric bands. Each piece can be changed afterwards under Fabric in the Clothes Designer.

## Made to measure

Every size is taken from the body: girths round the torso every 2 cm from the chest to the crotch, the arm's girth along its length, the thighs, the neck and the head, and the heights of the waist, hips, crotch, knee, ankle and floor. The pattern also gets sizes (chest, hips, arm length, inseam, shoulder to waist), set so its pieces keep the widths they were drafted at on this body and follow another body when the shape changes (Body Shape, other races, the bake's body shapes).

## Measured

Every preset was drafted and draped on Human Female 3.0 and Human Male 3.0 (400 frames on the GPU, 2026-11-23):

- Drafting takes 5 to 26 ms; laying out and building the cloth 0.2 to 1 second; draping about as long again.
- Human Female 3.0: all 39 presets sew shut (widest gap 0 to 9 mm; the bake welds them), none has cloth in the body, and every waistband stays where it was drafted.
- Human Male 3.0: bottoms stay on, but his waist-to-hip line is gentle, so skirts and wool trousers settle 7 to 20 cm lower than drafted, the pleated skirt down onto the hips; jeans, joggers, shorts and leggings stay where drafted. A rib waistband holds the others higher. His thick arms touch his sides in the modelled pose, so sleeved tops have a few points (4 to 8) pinched under the arms, and the catsuit two between the knees: cloth cannot fit where the body touches itself.

## Good to know

- A wrap in silk crosses through itself where the fronts overlap: silk is too limp to stay layered. Cotton, wool and fleece wrap cleanly (the presets use them).
- The generator's style is kept on the pattern (it is not used by anything else), so a saved generated pattern is still an ordinary pattern for every other tool.
