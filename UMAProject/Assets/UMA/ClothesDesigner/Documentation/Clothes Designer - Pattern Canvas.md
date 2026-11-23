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
- **Mirror** adds a left-right mirrored copy of the selected piece, **Duplicate** a copy, **Delete Piece** removes it and its seams.
- The piece panel sets the name, the **Grain** (the lengthwise direction of the cloth, drawn as the white arrow), the piece's **Fabric** (a preset, or open *Details* for stretch, softness, weight and friction) and its **Placement on the Body**: where it **Faces** round the body (0 front, 180 back, 90 the body's left), how far **Above Shoulders** it starts and its **Distance** out from the other pieces. The **Place** tool in the Scene view sets the placement by dragging. See *Clothes Designer - Fitting and Publishing*.

## Darts

A dart takes fabric out of a piece and sews the gap shut, so flat cloth curves round the bust, waist, hips, shoulder blades or elbow. The canvas draws darts in pink.

- **Edge dart** (straight or curved): select an edge and press **Add Dart**. A V opens on that edge with its point 10 cm into the piece. This one tool makes bust, waist, French (from the side seam, angled up to the bust), shoulder, neckline and elbow darts: it is only a matter of which edge it opens on and where its point is.
  - Drag the **point** to aim and lengthen it; drag a **leg** to slide the dart along its edge.
  - In the side panel: **Along Edge**, **Width** (the intake, how much fabric it takes out at the edge), **Length**, **Point**, and **Curve**. A positive curve bows the legs towards each other, taking out less near the point (for belly and hip shaping); negative bows them out. The legs always meet only at the point.
- **Diamond dart** (double-pointed, also called fish-eye or contour dart): select a piece and press **Add Diamond Dart**. A diamond appears in the middle of the piece. Drag either point, or the diamond itself. Set **Width** (at the widest point), **Widest At** (put it on the waist line) and **Roundness** (0 straight-sided, 1 a fish-eye). A diamond dart must lie inside the piece; for one that reaches an edge, use an edge dart.

When the cloth is built, an edge dart's legs are sewn to each other up to the point and a diamond's sides are sewn shut. A seam on an edge with a dart skips the dart's opening: what is left of the edge is sewn to the other side. The seam check shows the difference in length, which you can take up by lengthening that edge or let the other side gather. Sizes measured across a dart count only the fabric left, so a 52 cm front with a 2 cm dart across the chest line is widened as if it were 50 cm.

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

Sewn edges are drawn in the seam's colour. Seams whose sides differ in length by more than half a centimetre show the difference; the longer side will gather. Openings cannot be sewn, and an edge can only be in one seam. **Unsew** removes a seam.

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

## Saving

**Save Pattern** writes the pattern asset (the first time, you choose where). **Save As** makes an independent copy with a new asset; fits keep pointing at the original. Every change is one Undo step.
