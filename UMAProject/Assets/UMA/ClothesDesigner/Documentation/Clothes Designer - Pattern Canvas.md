# Clothes Designer: the Pattern Canvas

Press **Pattern Canvas** in the designer's Pattern section. The canvas edits the pattern open in the designer; after each change the cloth on the body is rebuilt from it.

Pieces are drawn in centimetres, as seen from **outside** the garment. A front piece's right-hand side is the wearer's left.

## Moving around

- Scroll to zoom, middle-drag (or Alt-drag) to pan, **Frame All** to see every piece.
- The grid is 1 cm, with a stronger line every 10 cm.

## Edit mode

- **Add Piece** adds a rectangle beside the others. Shape it from there.
- Click a **point** and drag it (hold Shift to snap to whole centimetres). Delete removes it.
- Click an **edge** to select it. **Double-click** an edge to add a point there.
- With an edge selected, the side panel shows its **Length** (type a new length to move its end point), **Curved** (drag the two blue handles in the canvas), and **Opening** (a neckline, armhole or hem that is never sewn; drawn dotted orange).
- Drag inside a piece to move the whole piece. Where a piece lies on the canvas does not change the cloth.
- **Mirror** adds a left-right mirrored copy of the selected piece, linked to it: the copy follows every change to the original (its outline, darts, pleats and lines) and keeps its own place on the canvas, placement and fabric. Untick *Linked to ...* in the piece panel to change it on its own. **Duplicate** adds a copy, **Delete Piece** removes it and its seams.
- The piece panel sets the name, the **Grain** (the lengthwise direction of the cloth, drawn as the white arrow), the piece's **Fabric** (a preset, or open *Details* for stretch, softness, weight and friction) and its **Placement on the Body**: where it **Faces** round the body (0 front, 180 back, 90 the body's left), how far **Above Shoulders** it starts and its **Distance** out from the other pieces. The **Place** tool in the Scene view sets the placement by dragging. See *Clothes Designer - Fitting and Publishing*.
- **Interfacing** stiffens a piece, as fused interfacing does: *Firm* for collar stands, cuffs and waistbands, *Light* for collar leaves and plackets. It resists folding across the piece's height as drawn, but the piece can still curve round the neck or the wrist, so draw stiffened pieces with their length across.
- **Mesh Detail** meshes the piece finer than the pattern's Mesh Level (2 is twice as fine). Small pieces need it to hold a curve or a point: the sample shirt's collar uses 3, its cuffs 2. Seams between pieces of different detail still pair point to point; the finer side sets how many points.
- **Layer**: where pieces cross, a higher layer is kept outside a lower one, whichever side it is on when sewing ends: a pocket over its panel, a collar over the shirt front, a jacket front over a shirt. Leave pieces sewn edge to edge (a cuff to its sleeve) on one layer. Measured on the sample oxford: with its collar on layer 1, no triangle of the collar crosses the front (25 crossing triangle pairs without).
- **Pressure** pushes the piece out from the body, like padding or the air in a puffer jacket. It shows most on stretchy fabric or a piece cut with room to spare; firm cotton with a vest's ease bulges about 2 mm.
- **Sew onto Another Piece** sews the selected piece on like a patch pocket: choose the piece it goes on and where its middle goes (that piece's centimetres), then **Sew On**. Every free edge (not an opening, not sewn) is sewn onto a line drawn on the other piece where it lies, and the piece is laid out over it on the next layer up.
- **Merge with ... along ...** joins the selected piece and another into one along a seam that runs the whole length of both edges, side by side (not folded). The seam goes; the other piece's seams, darts, pleats, lines and tacks come with it. The UVs tab may then ask to Repack UVs.

## Darts

A dart takes fabric out of a piece and sews the gap shut, so flat cloth curves round the bust, waist, hips, shoulder blades or elbow. The canvas draws darts in pink.

- **Edge dart** (straight or curved): select an edge and press **Add Dart**. A V opens on that edge with its point 10 cm into the piece. This one tool makes bust, waist, French (from the side seam, angled up to the bust), shoulder, neckline and elbow darts: it is only a matter of which edge it opens on and where its point is.
  - Drag the **point** to aim and lengthen it; drag a **leg** to slide the dart along its edge.
  - In the side panel: **Along Edge**, **Width** (the intake, how much fabric it takes out at the edge), **Length**, **Point**, and **Curve**. A positive curve bows the legs towards each other, taking out less near the point (for belly and hip shaping); negative bows them out. The legs always meet only at the point.
- **Diamond dart** (double-pointed, also called fish-eye or contour dart): select a piece and press **Add Diamond Dart**. A diamond appears in the middle of the piece. Drag either point, or the diamond itself. Set **Width** (at the widest point), **Widest At** (put it on the waist line) and **Roundness** (0 straight-sided, 1 a fish-eye). A diamond dart must lie inside the piece; for one that reaches an edge, use an edge dart.

When the cloth is built, an edge dart's legs are sewn to each other up to the point and a diamond's sides are sewn shut. A seam on an edge with a dart skips the dart's opening: what is left of the edge is sewn to the other side. The seam check shows the difference in length, which you can take up by lengthening that edge or let the other side gather. Sizes measured across a dart count only the fabric left, so a 52 cm front with a 2 cm dart across the chest line is widened as if it were 50 cm.

## Lines, folds and holes inside a piece

Choose **Draw** in the toolbar and the kind of line beside it, then click points on a piece. Double-click or press Enter to finish, Escape to cancel; Shift snaps to whole centimetres. A point clicked on the outline lies exactly on it, so a line can start or end there (only a line's two ends may touch the outline).

- **Seam Line**: a line other pieces are sewn onto: where a pocket, a patch, a strap or a yoke goes. In Sew mode, click an edge of the other piece, then the line. A line's ends on the outline become points of the edges they meet, and seams on those edges still pair.
- **Fold Line**: the cloth creases along it, like a trouser crease or a lapel's roll line. Set **Fold Angle** (0 flat, 90 a right angle, 170 folded back on itself) and **Folds Inward** for a valley instead of a ridge.
- **Hole**: a closed shape cut out of the piece (a keyhole, a cut-out, an eyelet). Its rim is an opening: the facing and the mesh hide treat it like the neckline.

Lines are drawn green (seam lines), dashed blue (folds) and orange (holes); sewn lines take their seam's colour. In Edit mode click a line to select it: drag a point to move it, drag the line to move all of it, Delete removes it. The side panel sets its name, kind, whether it is closed, the fold angle, and lists what is sewn to it. Moving a piece moves what is drawn on it.

## Tacks, buttons and parts

- **Tack / Button** (Draw): click a point, then the point it is held to, on the same piece or another. A tack is one stitch holding those two points together (a tacked pleat, a belt loop's end). With **Button** on, it is a button sewn on the first point and buttoned through the second: two fronts held shut. The bake adds the button (set its diameter in the side panel).
- **Part** (Draw): click on a piece to put a small hard part there: a round **Button**, a flat **Plate** (a buckle plate, a label) or a **Mesh** of your own (turn on Read/Write in its import settings; its z axis points out of the cloth). Set its size, how far it stands out and its rotation in the side panel. The bake adds it to the slot, moving with the cloth under it and textured from the cloth's texture where it sits, so painting that spot paints the part.

## Cutting a piece

**Cut Piece** (Draw): click two points; the cut runs straight through them across the whole piece, which it must cross exactly twice. The piece becomes two, with **Sew Back** on sewn back together along the cut (Cut & Sew), so the cloth drapes as before and the new seam can be unsewn, topstitched or given a different fabric on one side. Seams across the cut are cut in two with it; darts, pleats, lines and tacks go with the half they are on. Both halves keep the original's UVs (painted textures still line up) and its placement: pieces with the same placement that touch on the canvas are laid out round the body as the piece they were cut from. Sizes treat pieces stacked on the canvas as one, so a front cut across still takes its width and length from the size it had.

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

Drag a fold line to slide the pleat along its edge. Pleats are laid out already folded, so the simulation starts from the right shape. A seam on a pleated edge, a waistband for example, is sewn to what is left after the pleats take up their fabric. A pleat deeper than about two thirds of the Mesh Level's edge length cannot be shown; the designer says so, and a finer Mesh Level fixes it.

Select a dart or a pleat and press Delete (or **Delete Dart** / **Delete Pleat**) to remove it. Darts and pleats move with their edge when it is split, are copied by Duplicate and Mirror (a mirrored knife pleat faces the other way), and go when their edge is deleted.

## Sew mode

1. Choose **Sew** in the toolbar (or select an edge and press **Sew to...**).
2. Click the first edge, then the edge it is sewn to.
3. Dotted lines join the ends that meet. If they cross, the seam is twisted: select either edge and press **Reverse**.

- **Several edges onto one**: click the first edge, then Shift-click the edges it is sewn to, in order, and press Enter. The first edge is shared out between them by their lengths, as a sleeve cap is sewn to a front and a back armhole or a waistband to the pieces of a skirt.
- **Part of an edge**: a seam can use part of an edge; the edge's panel shows *Along this edge (%)* for each of its seams. Sewing an edge that is already partly sewn uses the longest part still free. The Checks list edges left partly open.
- **Onto a line**: click an edge, then a seam line drawn on another piece.
- Splitting a sewn edge (double-click) cuts its seam in two at the split, so both halves stay sewn.

**Folded (turned over)**, under a sewn edge, makes the seam a fold: the second piece is turned over the first along it, like a collar's leaf over its stand or a cuff turned back. Draw the turned piece as it looks once turned, seen from outside (a collar leaf with its neck edge along the top and its points hanging down); its edge then runs the same way as the edge it turns over, so a folded seam is not reversed. Lay the turned piece out already turned, at the same place with a **Distance** of about 1 cm, and the simulation keeps it turned.

Sewn edges are drawn in the seam's colour; a seam on part of an edge is drawn thick along that part. Seams whose sides differ in length by more than half a centimetre show the difference; the longer side will gather. Openings cannot be sewn, and two seams cannot share the same stretch of an edge. **Unsew** removes a seam.

Each seam can also have **Topstitch** (Single or Double rows, and how far from the seam): stitching drawn into the clothes' texture when they are baked.

## Edges

Besides **Length**, **Curved** and **Opening**, an edge can have **Elastic**: elastic sewn along it draws the edge in to **Drawn In To** percent of its length and stretches back, like a waistband, a cuff or a gathered hem. It changes how the cloth moves, not the pattern, so a kept drape stays.

## Sizes

A size makes pieces follow the body:

- **Width**: a body measurement (chest, waist or hip) plus an **Allowance** (the ease), shared by the pieces that go round the body at that line. For a front and a back, *Shared by* is 2. *At height* is the line on the pieces where the width is measured.
- **Length**: a measurement (shoulder to waist) plus an allowance, applied to the pieces' height.
- With *Follows* set to None, the size is a fixed length.

The designer's window shows what each size works out to on the current body. An allowance can also be changed per fit there, without touching the pattern.

Example: a 96 cm chest with 8 cm of ease is 104 cm round. A front and a back share it, so each is 52 cm wide at the chest line. On a 108 cm chest the same pattern is 58 cm per piece.

## UVs tab

Each piece has its own island in the slot's texture, all at one texel density, grain up, with a gutter between them. The layout is made from the pieces **as drawn**, so draping, posing, remeshing, reopening and fitting on another body size never move a print; a larger body spreads the same texture over more cloth.

When you redraw a piece larger, it can leave its place; the tab lists the pieces. **Repack UVs** lays them out again (and asks first: a texture painted for the old layout will no longer line up). *Texture* and *Gutter* set the bake resolution and the space between islands.

## Checks

The side panel lists what keeps the pattern from simulating: outlines that cross themselves, seams on edges that no longer exist, seams of very different lengths, edges that are neither sewn nor openings, and darts or pleats that leave the piece, run past the end of their edge or overlap each other.

## Reference image

Under the Sizes, **Reference Image** shows a picture behind the pieces to trace: a flat sketch or a photographed pattern. Set where its lower left corner is, how tall it is in centimetres and how strongly it shows. It is saved with the pattern and never baked.

## Saving

**Save Pattern** writes the pattern asset (the first time, you choose where). **Save As** makes an independent copy with a new asset; fits keep pointing at the original. Every change is one Undo step.
