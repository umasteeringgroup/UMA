# Clothes Designer Examples

These are the sample patterns of the **Clothes Designer** plugin, saved as assets you can open, copy and change. The plugin's guides and tutorials are in the Documentation Browser under **Clothes Designer**; begin with [Start Here](../ClothesDesigner/Documentation/!Clothes%20Designer%20-%20Start%20Here.md).

## Sample patterns

`Patterns/` holds thirty-one sample patterns besides the vest, to open in the designer (drag one into the **Pattern** field) or copy and change. They are also in the designer's **Samples** browser.

| Pattern | Pieces | Shows |
| --- | --- | --- |
| T-Shirt | front, back, 2 short sleeves | set-in sleeves laid round the arms |
| Long-Sleeve Tee | front, back, 2 long sleeves | sleeve length follows the arm |
| Tank Top | front, back | scoop neck, narrow straps |
| Crop Top | front, back | fitted spandex to the waist |
| Polo Shirt | front, back, 2 sleeves, collar stand, collar | a polo collar: a leaf turned down over a firm stand on a folded seam, deep enough at the back to cover the neck seam |
| Forte Shirt | front, back, 2 sleeves, collar | a stand collar: one band sewn along four neckline edges, standing up round the neck |
| Oxford Shirt | front, back, yoke, 2 sleeves, 2 cuffs, collar stand, collar | two-part collar (firm arc-cut stand, leaf turned down on a folded seam), interfaced cuffs, box pleat under a yoke, shirt-tail hem |
| Hoodie | front, back, 2 sleeves, 2 cuffs, 2 bands, 2 hood panels | rib bands that gather, a hood laid round the head |
| Short Pants | 4 leg pieces | trousers laid round the legs and hips |
| Jeans | 4 leg pieces, 2 waistband halves | denim, length follows the inseam |
| Sweatpants | 4 leg pieces, waistband, 2 ankle cuffs | gathered rib waistband and cuffs |
| Leggings | 4 leg pieces | spandex cut smaller than the body |
| Boxer Shorts | 4 leg pieces, elastic waistband | short and loose |
| Pleated Skirt | front, back | sixteen pressed knife pleats |
| A-Line Skirt | front, back | flare from the waist |
| Pencil Skirt | front, back | waist darts, sized by the hips |
| Circle Skirt | 4 quarter-circle panels | full flare in soft wool |
| Sheath Dress | front, back | double-pointed waist darts |
| Sundress | front, back | silk, narrow straps, square neck |
| Elastic Waist Dress | front, back, 2 sleeves, front and back band, 2 skirt panels | a thick elastic waistband (Rib, firmly interfaced, layer 1, baked 4 mm thick) cut to the waist, gathering a wider V-neck bodice and skirt into it |
| Belted Dress | front, back, 2 sleeves, front and back belt | a separate thick elastic belt on a higher layer (a free piece, tacked like belt loops) gathering a loose two-panel dress in at the waist to nearly half |
| Socks | 2 halves per sock | halves laid flat beside the foot and sewn round it |
| Beanie | 4 panels | pieces laid round the head |
| Face Mask | 2 halves, elastic band | a curved centre seam that shapes it over the nose and chin; a band round the back of the head |
| Bra | 2 cups, front and back band, 4 strap parts | darted spandex cups, an elastic band cut 30 cm shorter than the chest, straps sewn over the shoulders |
| Swimsuit | front, back | one piece through the crotch, high-cut legs, low back, cut smaller than the body so it grips |
| Briefs | front, back, elastic waistband | leg openings along the leg crease, the back bowed out over the seat |
| Hauberk | front, back, 2 sleeves | knee-length mail (the heavy, limp **Mail** fabric), slit front and back from the hem |
| Leather Armor | front, back, 2 shoulder guards, 4 tassets | stiff leather; firm guards sewn along the upper armholes; tassets hanging from short seams at the hem |
| Tunic | front, back, 2 sleeves | a knee-length wool T-tunic with a V neck slit, flaring from the hips |
| Coif | 2 halves, chin strap | a close linen cap sewn over the crown, held forward by a strap under the chin |

Each is drafted for the default *Human Female 3.0* (about 2 m tall: chest 100, waist 67, hip 117 cm) and sized for other bodies by its **Sizes**: tops by the chest and upper arm, skirts by the waist, trousers by the hips and inseam, the beanie by the head. On the default *Human Male 3.0* they drape too, with looser tops and skirts sitting lower. Sleeved tops (T-Shirt, Hoodie, Hauberk, Tunic) leave a few points pinched under his arms, which touch his sides in the modelled pose. Trousers sit 11 cm below the waist line, where both default bodies measure about 90 cm round.

## Sleeveless Vest

`Patterns/Sleeveless Vest.asset` is the Clothes Designer's first example: a two-panel sleeveless vest. Its shoulder and side seams exercise sewing, its neck, armholes and hem stay open, and lifting the arms shows how well the weights follow the body. Its chest follows the body's chest plus 8 cm of ease; its length follows shoulder-to-waist plus 20 cm, so it fits any humanoid race.

## Walkthrough

A short tour with the vest. For a fuller one, do [Tutorial 1: Your First Garment](../ClothesDesigner/Documentation/Clothes%20Designer%20-%20Tutorial%201%20-%20Your%20First%20Garment.md).

1. Choose **UMA > Clothes Designer > Create Clothes on Race...** and pick *Human Male 3.0* or *Human Female 3.0*.
2. Drag `Patterns/Sleeveless Vest` into the **Pattern** field of the designer's panel (or choose it from **Samples**).
3. Press **Pattern Canvas** to look at it: the front (deep V neck) and the back (shallow neck) drawn side by side. Dashed orange edges are openings; the coloured edges are the four seams. Click an edge to see its length and what it is sewn to.
4. In the designer's panel, press **Simulate**. The seams close, then the vest settles onto the shoulders.
5. Choose **Grab** and pull the front away from the chest; let go and it settles again. Press Ctrl+Z to undo the pull.
6. In the Vertex Editor window, open **Sizes** and raise the Chest allowance from 8 to 16 cm: the vest is rebuilt looser. Simulate again.
7. In **Publish**, name it, keep the UV Check Texture on, and press **Bake and Try On**. The try-on character wears it; press **Arms Raised**. The labels FRONT and BACK read the right way round and the checks are square.
8. Press **Save Fit**.

## Make a variation

1. With the vest open, press **Save Pattern As** and name the copy, for example *Round Neck Vest*.
2. In the Pattern Canvas, select the front's neckline centre point and drag it up to about 50 cm.
3. Simulate and bake under a new name. The original vest and its fits are unchanged.

To draw a pattern of your own from an empty canvas, do [Tutorial 2: A Pattern from Scratch](../ClothesDesigner/Documentation/Clothes%20Designer%20-%20Tutorial%202%20-%20A%20Pattern%20from%20Scratch.md).

## Try another body

In the Vertex Editor window, open **Body Shape (DNA)** and raise *belly* and *upperWeight*. The body is rebuilt, the vest's sizes are worked out for it and the pieces laid out again. Simulate, then bake under a new name: these clothes fit that body shape.
