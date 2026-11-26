# Clothes Designer: the Pattern Canvas

This is the reference for the 2D window where a pattern is drawn and sewn. To learn it step by step, do [Tutorial 2: A Pattern from Scratch](Clothes%20Designer%20-%20Tutorial%202%20-%20A%20Pattern%20from%20Scratch.md). All guides are listed in [Start Here](!Clothes%20Designer%20-%20Start%20Here.md).

| To do this | Go to |
| --- | --- |
| Move, shape, copy and mirror pieces | [Edit mode](#edit-mode) |
| Shape cloth round the body | [Darts](#darts), [Pleats](#pleats) |
| Draw lines, folds and holes; add buttons and parts | [Lines, folds and holes inside a piece](#lines-folds-and-holes-inside-a-piece), [Tacks, buttons and parts](#tacks-buttons-and-parts) |
| Place zippers, stitching and hems for the Overlay Painter | [Trims](#trims-zippers-stitching-piping-and-hems) |
| Cut a piece in two | [Cutting a piece](#cutting-a-piece) |
| Sew edges together | [Sew mode](#sew-mode), [Edges](#edges) |
| Make the pattern follow the body | [Sizes](#sizes) |
| Lay out and repack the texture | [UVs tab](#uvs-tab), [Looks](#looks-a-fabric-per-piece) |
| Find what is wrong with a pattern | [Checks](#checks) |

## The window

The toolbar across the top has the four modes, **Edit**, **Sew**, **Draw** and **UVs**, then the piece buttons (**Add Piece**, **Mirror**, **Duplicate**, **Delete Piece**), **Add Dart**, **Add Diamond Dart** and **Add Pleat**, the move aids (**Snap**, **Free** / **Horizontal** / **Vertical**, **Mirror X**), and **Frame All**, **Save Pattern** and **Save As**. The side panel shows the pattern's name, the list of **Pieces**, what is selected (a piece, an edge, a point, a line, a dart, a pleat, a tack, a part or a trim), then **Sizes**, **Reference Image** and **Checks**.

Press **Pattern Canvas** in the designer's Pattern section. The canvas edits the pattern open in the designer; after each change the cloth on the body is rebuilt from it. The Editor waits while that happens; for a large garment, which can take a second or two, a progress bar shows what it is doing. The samples take well under a second and show none.

Pieces are drawn in centimetres, as seen from **outside** the garment. A front piece's right-hand side is the wearer's left.

## Moving around

- Scroll to zoom, middle-drag (or Alt-drag) to pan, **Frame All** to see every piece.
- The grid is 1 cm, with a stronger line every 10 cm.

## Edit mode

- **Add Piece** adds a rectangle, 40 cm square, beside the others. Shape it from there. (**New** in the designer starts a pattern with one rectangle, 50 cm wide and 60 cm tall.)
- Click a **point** and type its **Position (cm)** in the side panel to place it exactly.
- Click a **point** and drag it (hold Shift to snap to whole centimetres). Delete removes it: its two edges become one, and what was sewn along either stays sewn, in the same place along the joined edge (a seam the point had split in two is one again). Darts, pleats and trims on them stay too.
- Click an **edge** to select it, or drag it to move it: both its end points move together, a curved edge keeps its curve, and the edges either side stretch to follow. **Double-click** an edge to add a point there. From inside a piece an edge is picked only within about 3 pixels of it (8 from outside), so pressing inside a narrow piece moves the piece; Sew and Draw pick edges from as far inside as outside.
- With an edge selected, the side panel shows its **Length** (type a new length to move its end point), **Curved** (drag the two blue handles in the canvas), and **Opening** (a neckline, armhole or hem that is never sewn; drawn dashed orange).
- Drag inside a piece to move the whole piece. Where a piece lies on the canvas does not change the cloth. **Shift**-drag moves the whole piece wherever you press on it, even on an edge, a point, a tack or a line: zoomed out, most of a narrow piece (a belt or a band) is within reach of an edge. Shift held as you press moves the piece; to snap a point to whole centimetres, press Shift after you start dragging it.
- **Moving things**, from a point, an edge or a curve handle to a line, a dart, a tack, a part or a whole piece, has these aids. A label at the pointer shows how far it has moved.
  - **Snap** (toolbar, on by default): a moving point lines up with points nearby, across and in height separately, within about 8 pixels: corners, the middles of edges, line and dart points, tack ends and parts, on its own piece and the others. A dotted pink guide shows each line-up. Shift on a point rounds it to whole centimetres instead.
  - **Horizontal** and **Vertical** (toolbar) keep every move to that axis; with **Free**, hold **Ctrl** (Cmd on a Mac) while dragging to keep to the way you moved further. A faint line shows the axis.
  - **Mirror X** (toolbar): within a piece, moving a point, an edge, a curve handle, a line, a dart or a pleat moves its mirror image across the piece's middle (the dotted blue line) too, so a symmetric front stays symmetric. Partners are found within half a centimetre of each other's mirror image.
    - A point or an edge on the middle itself (the centre-front neck point, a hem across the middle, one point of a diamond dart there) stays on the middle and moves only up and down, so the outline stays symmetric; with **Horizontal** chosen as well, it cannot move at all (the label at the pointer says so).
    - A whole dart or line on the middle is only held there: it stays on the middle (drawn as a pink guide) until you drag it further across than snapping reaches, then comes off, wherever the toolbar allows. The status line then reminds you it has no mirror twin; Ctrl+D adds one.
    - **Add Diamond Dart** adds a mirrored pair, one each side, halfway between the middle and the side, as long as fits inside the piece. With Mirror X off it adds one dart on the middle, as before.
    - A change in the side panel to a dart that has a twin (its width, points, shape) is made to the twin too; the panel says which dart that is.
  - **Escape** during a drag puts everything back where it was.
  - The **arrow keys** nudge what is selected a millimetre, or a centimetre with Shift (mirrored with Mirror X); the cloth is built again once you stop.
- **Duplicate** (or **Ctrl+D**, Cmd+D on a Mac) copies what is selected: a dart (beside it, or further along its edge), a pleat, a line or a part. With Mirror X on, the copy goes mirrored across the piece's middle instead: a dart on the left gives its twin on the right. Something on the middle is copied beside it, not onto itself. With only a piece selected, the piece is copied.
- A value typed in the side panel and not entered (Return) is dropped when you select something else, so it cannot end up in the newly selected dart or edge.
- **Mirror** adds a left-right mirrored copy of the selected piece, linked to it: the copy follows every change to the original (its outline, darts, pleats and lines) and keeps its own place on the canvas, placement and fabric. Untick *Linked to ...* in the piece panel to change it on its own. **Duplicate** adds a copy, **Delete Piece** removes it and its seams.
- The piece panel sets the name, the **Grain** (the lengthwise direction of the cloth, drawn as the white arrow), the piece's **Fabric** (a preset, or open *Details* for stretch, softness, weight and friction) and its **Placement on the Body**: where it **Faces** round the body (0 front, 180 back, 90 the body's left), how far **Above Shoulders** it starts and its **Distance** out from the other pieces. The **Place** tool in the Scene view sets the placement by dragging. See *Clothes Designer - Fitting and Publishing*.
- **Interfacing** stiffens a piece, as fused interfacing does: *Firm* for collar stands, cuffs and waistbands, *Light* for collar leaves and plackets. It resists folding across the piece's height as drawn, but the piece can still curve round the neck or the wrist, so draw stiffened pieces with their length across.
- **Mesh Detail** meshes the piece finer than the pattern's Mesh Level (2 is twice as fine). Small pieces need it to hold a curve or a point: the sample shirt's collar uses 3, its cuffs 2. Seams between pieces of different detail still pair point to point; the finer side sets how many points.
- **Layer**: where pieces cross, a higher layer is kept outside a lower one, whichever side it is on when sewing ends: a pocket over its panel, a collar over the shirt front, a jacket front over a shirt. Leave pieces sewn edge to edge (a cuff to its sleeve) on one layer. Measured on the sample oxford: with its collar on layer 1, no triangle of the collar crosses the front (25 crossing triangle pairs without). Cloth that ends up within 3 cm under a higher layer is pushed back in under it, so a higher layer presses in what is under it (see Fitting and Publishing). A piece on a higher layer sewn to nothing on another layer (a belt over a dress; tacks do not count) is a free piece: it is laid outside the cloth under it and tightens round it (see Fitting and Publishing, Belts and other free pieces).
- **Pressure** pushes the piece out from the body, like padding or the air in a puffer jacket. It shows most on stretchy fabric or a piece cut with room to spare; firm cotton with a vest's ease bulges about 2 mm.
- **Thickness (mm)** bakes the piece that thick: a belt, a waistband, a strap. The thickness is built outward from the draped cloth with crisp edges, so the piece stands out from what it is sewn to. The drape is not changed; the designer draws the thickness on the live cloth, and the bake gives it to the slot (see Fitting and Publishing, Baking).
- **Sew onto Another Piece** sews the selected piece on like a patch pocket: choose the piece it goes on and where its middle goes (that piece's centimetres), then **Sew On**. Every free edge (not an opening, not sewn) is sewn onto a line drawn on the other piece where it lies, and the piece is laid out over it on the next layer up.
- **Merge Its Points** (edge panel): the edge's two points become one, at its middle. The edge goes, with anything sewn to it; the edges either side keep theirs.
- **Piece Tools** (piece panel). Symmetrize and Cut in Half work about the upright line through the selected point, or through the piece's middle when no point is selected (the panel says which):
  - **Symmetrize** (Left onto Right, Right onto Left): the piece becomes the mirror image of one of its halves, still one piece. The other half's seams move to the edge now lying where theirs did (a shoulder a centimetre or two off still finds its seam); its darts, pleats and lines give way to the kept half's, mirrored. A point the cut left in the middle of a straight hem is taken out again.
  - **Cut in Half** (Keep Left, Keep Right): the piece is cut along the line and the other half becomes a linked mirror of the kept one, sewn to it along the middle. Edit the kept half and the other follows. The kept half keeps the piece's name and its placement on the body.
  - **Duplicate with Seams**: a copy sewn as the piece is, to the copies of the pieces it is sewn to. Duplicate a set of pieces one by one this way and the copies come out sewn together as the originals are; a seam whose other piece has no copy yet is made when that piece is duplicated.
  - **Duplicate Sewn Round**: the same, on the layer above, and sewn to the piece all round where it is free, its openings too (they are then no longer openings). It makes a two-ply collar, cuff, flap or belt; done to every piece of a garment, a lined garment (the copies are the outside, the originals the lining). The copy is laid on its original on the body wherever it lies on the canvas and follows its placement; a two-ply belt is still a free piece. Copies go to the right of the pattern, all by the same amount.
- **Merge with ... along ...** joins the selected piece and another into one along a seam that runs the whole length of both edges, side by side (not folded). The seam goes; the other piece's seams, darts, pleats, lines and tacks come with it. The UVs tab may then ask to Repack UVs.

## Darts

A dart takes fabric out of a piece and sews the gap shut, so flat cloth curves round the bust, waist, hips, shoulder blades or elbow. The canvas draws darts in pink.

- **Edge dart** (straight or curved): select an edge and press **Add Dart**. A V opens on that edge with its point 10 cm into the piece. This one tool makes bust, waist, French (from the side seam, angled up to the bust), shoulder, neckline and elbow darts: it is only a matter of which edge it opens on and where its point is.
  - Drag the **point** to aim and lengthen it; drag a **leg** to slide the dart along its edge.
  - In the side panel: **Along Edge**, **Width** (the intake, how much fabric it takes out at the edge), **Length**, **Point**, and **Curve**. A positive curve bows the legs towards each other, taking out less near the point (for belly and hip shaping); negative bows them out. The legs always meet only at the point.
- **Diamond dart** (double-pointed, also called fish-eye or contour dart): select a piece and press **Add Diamond Dart**. A diamond appears in the middle of the piece. Drag either point, or the diamond itself. Set **Width** (at the widest point), **Widest At** (put it on the waist line) and **Roundness** (0 straight-sided, 1 a fish-eye). A diamond dart must lie inside the piece; for one that reaches an edge, use an edge dart.

When the cloth is built, an edge dart's legs are sewn to each other up to the point and a diamond's sides are sewn shut. A seam on an edge with a dart skips the dart's opening: what is left of the edge is sewn to the other side. The seam check shows the difference in length, which you can take up by lengthening that edge or let the other side gather. Sizes measured across a dart count only the fabric left, so a 52 cm front with a 2 cm dart across the chest line is widened as if it were 50 cm.

## Lines, folds and holes inside a piece

Choose **Draw** in the toolbar and the kind of line beside it, then click points on a piece. Double-click or press Enter to finish, Backspace takes the last point back, Escape cancels. A point clicked on the outline lies exactly on it, so a line can start or end there (only a line's two ends may touch the outline).

Clicks snap as moves do (**Snap** on the toolbar, a pink ring shows where a click will land):
- within about 8 pixels of a point on the piece (a corner, the middle of an edge, a line or dart point, a tack end, a part, or a point of the line being drawn), the click lands exactly on it, so a line can join two points exactly;
- otherwise it lines up across and in height with points elsewhere, with dotted pink guides;
- **Ctrl** (Cmd on a Mac), or **Horizontal** / **Vertical** on the toolbar, keeps the new point level with or upright from the last one, onto the outline too (where that line meets the edge under the pointer);
- **Shift** rounds to whole centimetres instead.

- **Seam Line**: a line other pieces are sewn onto: where a pocket, a patch, a strap or a yoke goes. In Sew mode, click an edge of the other piece, then the line. A line's ends on the outline become points of the edges they meet, and seams on those edges still pair.
- **Fold Line**: the cloth creases along it, like a trouser crease or a lapel's roll line. Set **Fold Angle** (0 flat, 90 a right angle, 170 folded back on itself) and **Folds Inward** for a valley instead of a ridge.
- **Hole**: a closed shape cut out of the piece (a keyhole, a cut-out, an eyelet). Its rim is an opening: the facing and the mesh hide treat it like the neckline.

Lines are drawn green (seam lines), dashed blue (folds) and orange (holes); sewn lines take their seam's colour. In Edit mode click a line to select it: drag a point to move it, drag the line to move all of it, Delete removes it. The side panel sets its name, kind, whether it is closed, the fold angle, and lists what is sewn to it. Moving a piece moves what is drawn on it.

## Tacks, buttons and parts

- **Tack / Button** (Draw): click a point, then the point it is held to, on the same piece or another. A tack is one stitch holding those two points together (a tacked pleat, a belt loop's end). With **Button** on, it is a button sewn on the first point and buttoned through the second: two fronts held shut. The bake adds the button (set its diameter in the side panel).
- **Part** (Draw): click on a piece to put a small hard part there: a round **Button**, a flat **Plate** (a buckle plate, a label) or a **Mesh** of your own (turn on Read/Write in its import settings; its z axis points out of the cloth). Set its size, how far it stands out and its rotation in the side panel. The bake adds it to the slot, moving with the cloth under it and textured from the cloth's texture where it sits, so painting that spot paints the part.

## Trims: zippers, stitching, piping and hems

Trims are painted onto the cloth's texture by the **Overlay Painter** (install it from **Welcome to UMA > Plugins**). The pattern says where each one goes; the Overlay Painter draws it, as a layer you can still edit there.

- Select an edge or a line inside a piece and press **Add Trim**. The trim follows that edge or line and is drawn as a pink band. It starts as a zipper.
- In the side panel, under **Trims**: the **Construction** (any of the Overlay Painter's path generators, grouped as in the painter: *Stitching*, *Seams and Trim*, *Fasteners and Distress* for zippers, *Lace and Openwork* and more), its **Width (cm)**, its **Inset (cm)** from an edge (half the width keeps it just inside the edge), and its **Span**, the part of the edge or line it covers.
- **Publish and Open in Overlay Painter** in the Publish window publishes, closes the Clothes Designer and opens the Overlay Painter on the published slot, with one path layer per trim in a group named **Trim**, laid out in the slot's UVs at the trim's width. With the group selected every trim shows; click one in the Scene view or the UV window to edit it. Change each one there (colour, teeth, stitch length...) and save the result from the Overlay Painter as usual.

[Tutorial 4](Clothes%20Designer%20-%20Tutorial%204%20-%20Texturing%20with%20the%20Overlay%20Painter.md) goes through all of this with a hem and a zipper.

Trims are painted, so they follow the UV layout: repacking UVs after painting moves the islands under them. Paint trims after the layout is settled. A trim whose edge or line was removed is listed in a message and left out.

## Cutting a piece

**Cut Piece** (Draw): click two points; the cut runs straight through them across the whole piece, which it must cross exactly twice. With **Path** on (beside Sew Back) the knife follows the points you click instead, for a princess seam or a curved yoke: double-click or Enter cuts, Backspace takes the last point back. An end inside the piece runs on to the outline; with **Smooth** the cut curves through the points, otherwise it runs straight from one to the next. A seam line already drawn can be cut along too: select it and press **Cut Piece Along Line** (sewn back; the line goes, and something sewn onto it must be unsewn first). The piece becomes two, with **Sew Back** on sewn back together along the cut (Cut & Sew), so the cloth drapes as before and the new seam can be unsewn, topstitched or given a different fabric on one side. Seams across the cut are cut in two with it; darts, pleats, lines and tacks go with the half they are on. Both halves keep the original's UVs (painted textures still line up) and its placement: pieces with the same placement that touch on the canvas are laid out round the body as the piece they were cut from. Sizes treat pieces stacked on the canvas as one, so a front cut across still takes its width and length from the size it had.

## Pleats

A pleat folds fabric at an edge, so the edge is shorter than the cloth below it. Select an edge and press **Add Pleat**. The canvas draws the fold lines in blue running into the piece, dotted where the cloth is creased, solid and thick where they are sewn down. An arrow just outside the edge joins the lines that are brought together.

- **Type**:
  - **Knife (fold faces right or left)**: one fold, all the hidden fabric to one side.
  - **Box**: two folds turned away from each other, with a flat panel on top (**Box Width**).
  - **Inverted**: two folds meeting on the outside.
- **Depth**: how deep each fold goes. A knife pleat takes up twice the depth from the edge; box and inverted pleats four times. The panel shows how much.
- **Stitched Down**: how far down from the edge the folds are sewn together; 0 holds them only at the edge.
- **Pressed**: pressed folds keep a crease, so the cloth falls in pleats. They stay folded near the edge and open over the hips where the body needs the fabric. Unpressed pleats open into soft folds below the stitching.
- **Fold Length**: how far the fold lines run into the piece; 0 runs them across the whole piece.

Drag a fold line to slide the pleat along its edge. Pleats are laid out already folded, so the simulation starts from the right shape. A seam on a pleated edge, a waistband for example, is sewn to what is left after the pleats take up their fabric. A piece with pleats is meshed at least as finely as half its shallowest pleat's depth, whatever its Mesh Detail, so the cloth between two folds is at least two triangles across. With only one, the folds do not hold: on the shirt's 2 cm box pleat at a 2 cm mesh, the folds came out over the box and split it down the middle. The finer piece takes longer to simulate.

Select a dart or a pleat and press Delete (or **Delete Dart** / **Delete Pleat**) to remove it. Darts and pleats move with their edge when it is split, are copied by Duplicate and Mirror (a mirrored knife pleat faces the other way), keep their place when a point at either end of their edge is deleted, and go when their edge is merged away.

## Sew mode

1. Choose **Sew** in the toolbar (or select an edge and press **Sew to...**).
2. Click the first edge, then the edge it is sewn to.
3. Dotted lines join the ends that meet. If they cross, the seam is twisted: select either edge and press **Reverse**.

- **Several edges onto one**: click the first edge, then Shift-click the edges it is sewn to, in order, and press Enter. The first edge is shared out between them by their lengths, as a sleeve cap is sewn to a front and a back armhole or a waistband to the pieces of a skirt.
- **Part of an edge**: a seam can use part of an edge; the edge's panel shows *Along this edge (%)* for each of its seams. Sewing an edge that is already partly sewn uses the longest part still free. The Checks list edges left partly open.
- **Onto a line**: click an edge, then a seam line drawn on another piece.
- Splitting a sewn edge (double-click) cuts its seam in two at the split, so both halves stay sewn.

**Sharp**, under a sewn edge, bakes the seam as a crisp edge: the slot keeps a normal for each side of it instead of one shared normal (see Fitting and Publishing, Sharp seams and folds). A fold line and a pleat have the same (**Sharp**, **Sharp Folds**).

**Folded (turned over)**, under a sewn edge, makes the seam a fold: the second piece is turned over the first along it, like a collar's leaf over its stand or a cuff turned back. Draw the turned piece as it looks once turned, seen from outside (a collar leaf with its neck edge along the top and its points hanging down); its edge then runs the same way as the edge it turns over, so a folded seam is not reversed. Lay the turned piece out already turned, at the same place with a **Distance** of about 1 cm, and the simulation keeps it turned.

Sewn edges are drawn in the seam's colour; a seam on part of an edge is drawn thick along that part. Seams whose sides differ in length by more than half a centimetre show the difference; the longer side will gather. Openings cannot be sewn, and two seams cannot share the same stretch of an edge. **Unsew** removes a seam.

Each seam can also have **Topstitch** (Single or Double rows, and how far from the seam): stitching drawn into the clothes' texture when they are baked.

## Edges

Besides **Length**, **Curved** and **Opening**, an edge can have **Elastic**: elastic sewn along it draws the edge in to **Drawn In To** percent of its length and stretches back, like a waistband, a cuff or a gathered hem. It changes how the cloth moves, not the pattern, so a kept drape stays.

## Sizes

A size makes pieces follow the body. Press **Add Size**, name it, choose what it **Sizes** (*Width* or *Length*) and what it **Follows**, and tick the pieces it applies to. *On this body* then shows what it works out to, beside the size as drawn.

- **Width**: a body measurement (chest, waist or hip) plus an **Allowance** (the ease), shared by the pieces that go round the body at that line. For a front and a back, *Shared by* is 2. *At height* is the line on the pieces where the width is measured: a height on the canvas grid, so draw pieces that share a width at the same height.
- **Length**: a measurement (shoulder to waist) plus an allowance, applied to the pieces' height.
- With *Follows* set to None, the size is a fixed length.

The designer's window shows what each size works out to on the current body. An allowance can also be changed per fit there, without touching the pattern. The **Size** tool in the Scene view resizes a piece on the body: where a size sets its width or length, it changes that size's allowance, so every piece of the size follows (see Fitting and Publishing, Shaping the cloth).

Example: a 96 cm chest with 8 cm of ease is 104 cm round. A front and a back share it, so each is 52 cm wide at the chest line. On a 108 cm chest the same pattern is 58 cm per piece.

## UVs tab

Each piece has its own island in the slot's texture, all at one texel density, grain up, with a gutter between them. The layout is made from the pieces **as drawn**, so draping, posing, remeshing, reopening and fitting on another body size never move a print; a larger body spreads the same texture over more cloth.

When you redraw a piece larger, it can leave its place; the tab lists the pieces. **Repack UVs** lays them out again (and asks first: a texture painted for the old layout will no longer line up). *Texture* and *Gutter* set the bake resolution and the space between islands.

Repack UVs tries two ways and keeps whichever gives the cloth the most texture: the pieces' outlines fitted into each other on a fine grid, and their bounding boxes packed by UMA's atlas packer (which suits pieces close to rectangles). Over the 20 samples the cloth covers 63% of the texture on average. The pieces then report how much of the texture they cover.

- **Turn Pieces** (on by default) lets Repack turn a piece a quarter or half turn when that fits better. Turn it off to keep every piece grain up in the texture, for prints that must run one way.
- **Mirrors Share Space** gives a linked mirror (made with Mirror Copy, linked) its original's island, mirrored, so the pair takes the texture of one piece. Both sides then show the same texture, mirrored: leave it off for anything that must differ left to right, such as text or a pocket on one side.
- **Texture Detail** (per piece, 0.25 to 2) gives that piece more or fewer texels per centimetre than the rest: more for a collar or a printed front, fewer for a lining.

## Looks: a fabric per piece

A piece's **Look** (in the side panel, under Fabric) is what it is made of in the texture. *Clothes' Own Overlay* shows the clothes' overlay (the UV check texture, or whatever you painted). **New Look...** makes a look, which any piece can then use:

- **Colour Texture**, **Normal Map** and **Mask Texture**: a fabric's tileable textures (the mask goes in the material's third channel, specular or metallic and smoothness). None of them is required: a look with only a colour paints the piece that colour.
- **Colour**: multiplied with the colour texture, and changeable on a character at run time. **Shared Colour** gives it a shared colour name (for example `Shirt2`), so characters set it by name like any other UMA colour. Looks with the same name change together.
- **Repeat (cm)**: how much cloth one repeat of the textures covers, so a weave is the same size on every piece, whatever its Texture Detail. **Rotation** turns the textures against the grain: 45 for cloth cut on the bias.
- **Use on All Pieces** gives every piece the look; **Delete Look** removes it.

When baked (with **Piece Looks** on in the Bake panel), each look becomes an overlay on the clothes' slot, over the clothes' own overlay: its textures drawn into the slot's texture space, tiled along each piece's grain, with an alpha mask of its pieces' islands. So one slot and one material carry several fabrics. A look's normal map is combined with the drape's baked folds. A mirror sharing its original's place in the texture shows its original's look.

## Checks

The side panel lists what keeps the pattern from simulating: outlines that cross themselves, seams on edges that no longer exist, seams of very different lengths, edges that are neither sewn nor openings, and darts or pleats that leave the piece, run past the end of their edge or overlap each other.

## Reference image

Under the Sizes, **Reference Image** shows a picture behind the pieces to trace: a flat sketch or a photographed pattern. Set where its lower left corner is, how tall it is in centimetres and how strongly it shows. It is saved with the pattern and never baked.

## Saving

**Save Pattern** writes the pattern asset (the first time, you choose where). **Save As** makes an independent copy with a new asset; fits keep pointing at the original. Every change is one Undo step.
