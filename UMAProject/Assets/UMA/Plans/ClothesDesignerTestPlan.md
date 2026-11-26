# Clothes Designer test plan

Drafted 2026-11-24 for the maintainer. It lists what has been built for the Clothes Designer but not yet validated by hand, or not tested at all, and says what to do and what to expect for each. Most items below were checked by script in the open Editor; that proves the code paths and the numbers, not that the tools feel right in use. Sources: `ClothesDesignerPlan.md` (milestones 1 to 5, gap work, open gaps) and `ClothesDesignerV2Plan.md` (V2), plus the fixes made on 2026-11-24.

## How to use it

- Work top down: Priority 1 is new and the least seen, Priority 3 is older work that has had a first look.
- Write **Pass**, **Fail** or a note in the Result column. A failure needs only what you did and what you saw; a screenshot helps.
- Expected numbers come from the scripted checks. Treat them as a guide (within about 10%), not exact.
- An item marked *(script)* has a scripted or automated check behind it already; an item marked *(none)* has never been run at all.

## Setup

- Unity 6000.3.26f1, UMA 3.1.6, this checkout (`C:/GitHub/UMA/UMAProject`), URP. Record the GPU and graphics API if you test on another machine.
- Races: **Human Female 3.0** unless a test says otherwise, then **Human Male 3.0**.
- Save the open scene first. Open the designer from the Vertex Editor's **Clothes Designer** workflow (empty scene, or a scene character).
- Keep an eye on the Console: any error or warning during a test is a failure even if the result looks right.

## Status at a glance

| Area | Automated tests | Scripted checks | Hands-on |
| --- | --- | --- | --- |
| Milestone 0 (core, plugin skeleton) | Yes | Yes | Validated 2026-11-21 |
| Milestones 1 to 5 (vest, canvas, fitting, refit, release) | Yes | Yes | **Pending** (Priority 2) |
| Gap work, remaining gaps, O1, O2 (2026-11-23) | Yes | Yes | **Pending** (Priority 3) |
| V2: samples, browser, Stiffen and Weight, Body Region, Holds Hair Down, sharp edges | Yes (DesignerV2Tests, 14) | Yes | S1, S2, S5 to S8 and every F check validated 2026-11-24; **Pending** (S3, S4) |
| Designer speed with the Publish section open (UV check cached) | No | Yes | Validated 2026-11-24 ("Fast now") |
| Fit Brush shows the cloth lit | No | Yes (one-frame trial) | Validated 2026-11-24 ("Looks right") |
| Fit Tools toolbar | Yes (ClothesDesignerWorkflowTests, 2) | No | Validated 2026-11-24 ("Works", F4, F5) |
| Workflow bars hidden in the designer | Yes (VertexEditorWorkflowTests 2, ClothesDesignerWorkflowTests 1) | No | Validated 2026-11-24 (F8) |
| Publish button (name, wardrobe region, Global Library, compatible race) | Yes (ClothesDesignerWorkflowTests, 3) | No | Validated 2026-11-24 ("working great") |
| Piece thickness, Elastic Waist Dress, Grab One Layer | Yes (BeltAndLayerTests) | Yes | Validated 2026-11-24 (F12 to F14, F16) |
| Free pieces and the Belted Dress (loose panels, free belt) | Yes (BeltAndLayerTests) | Yes (both bodies) | Validated 2026-11-24 ("Worked great", with Grab One Layer) |
| One-way layers (cloth under a higher layer pushed back in) | Yes (BeltAndLayerTests, CPU and GPU) | Yes (belted dresses, collars, Leather Armor, Elastic Waist Dress) | Validated 2026-11-24 (F15, F17, F18) |
| Size tool (a piece wider or longer on the body, shown while dragging, handles) | Yes (PieceSizingTests, 22, CPU and GPU) | Yes (T-Shirt and Belted Dress in a live designer: carried and live drapes against fresh ones, release mid-ease, typed size, pictures of the preview) | Validated 2026-11-25 (F26) |
| Trims in a Trim group; the painter's Edit Paths Together | Yes (TexturePaintGroupPathTests 3, TexturePaintScriptedSessionTests 2) | Yes | Validated 2026-11-25 (F27) |
| Pattern Canvas editing aids (Snap, Horizontal and Vertical, Mirror X with darts on the middle, mirrored dart pairs, Duplicate and Ctrl+D, nudge, half-typed fields); tape-measure icon | Yes (CanvasEditingTests, 7) | Yes (live canvas) | Validated 2026-11-25 (F28) |
| Pattern tools: knife path and along a line, snapping while drawing, Delete and Merge Points keeping seams, Duplicate with Seams and Sewn Round, Symmetrize, Cut in Half | Yes (PatternToolsTests, 9) | Yes (live canvas and drapes) | **Pending** (F29) |
| Plugin install and remove, player build, reload with the designer open | Partly | Partly | **Never run** (Priority 2) |
| HDRP, built-in pipeline, other graphics APIs, Unity 6.4 and later | No | No | **Never run** (Platforms) |
| PlayMode suites | Not run in the 2026-11-24 regression (skipped by choice) | | |

## Priority 1: V2 features (2026-11-24)

### Samples and the sample browser

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| S1 | Press **Samples** in the Pattern section. | The browser opens at once (well under a second), with a picture of each pattern and of its drape, grouped by category, and a search box. | Pass 2026-11-24 |
| S2 | Type "arm" in the search, then clear it; click each category. | Leather Armor shows for "arm"; categories filter; clearing brings everything back. | Pass 2026-11-24 ("ar" also listed the Underwear and Swim group: the search matched group names; it now matches sample names only) |
| S3 | Load each new sample in turn: Bra, Swimsuit, Briefs, Face Mask, Hauberk, Leather Armor, Tunic, Coif. Simulate until still. | Each drapes with no cloth in the body and closed seams. The Coif's chin strap holds it on. Underarm corners of the Hauberk and Tunic may stay open about 3 mm (as the T-Shirt does; the bake welds them). | |
| S4 | Repeat S3 on Human Male 3.0 for Briefs, Face Mask, Leather Armor and Coif, then Hauberk and Tunic. | The first four drape cleanly. Hauberk and Tunic may leave 4 to 8 points under the arms (his arms touch his sides), as the T-Shirt does. | |
| S5 | In the browser, **Render on This Body** with a male or a DNA-changed body open. | The pictures are redrawn for this body, one at a time, and the Editor stays usable while they draw (short hitches of about a quarter second are known). | Pass 2026-11-24 |
| S6 | Load a sample with unsaved changes in the designer. | You are asked to save, discard or cancel first. | Pass 2026-11-24 |
| S7 | Hauberk: check the **Mail** fabric. | Heavy (4 kg per m²), limp, does not stretch. | Pass 2026-11-24 |
| S8 | Bake and try on two of the new samples (one male, one female). | The baked slot equips and looks like the drape. | Pass 2026-11-24 |

### Painted stiffness and weight

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| P1 | Sundress (silk): Fit Brush, **Stiffen**, paint the whole skirt; simulate. | The hem rises about 5 cm and widens about 1.5 cm: it holds an A-line. Teal where painted. | |
| P2 | Undo, then Stiffen only the hem band. | Fewer, larger folds at the hem. | |
| P3 | Undo, then **Weight** on the lowest 15 cm. | The hem hangs about 5 cm lower in straighter folds. Brown where painted. | |
| P4 | Stiffen a cotton piece. | Little visible change (cotton bending is already at its stable limit). Known, not a failure. | |
| P5 | Ctrl-drag over painted cloth; then **Erase**. | Ctrl takes off the current mode's paint; Erase takes off every kind. | |
| P6 | Save, close, reopen the fit; change the Mesh Level. | Stiffen and Weight paint come back and survive the new level. | |
| P7 | Bake the stiffened or weighted dress. | The baked drape matches the painted one. | |
| P8 | Repeat P1 and P3 with **Use CPU Solver**. | Same behaviour as the GPU solver. | |

### Body Region (cloth from a body region anywhere)

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| B1 | **Body Region** tool: paint a band round the upper arm with **Mirror** on; **Make Pieces**. | Two sleeve pieces, about 3% stretch, laid about 1 cm off the arms; simulate: seams close, nothing in the body. | |
| B2 | Shift-drag to take some region off; change Radius. | The region shrinks where you Shift-drag; the brush size follows Radius. | |
| B3 | Paint a skull cap; Make Pieces. | About six gores of 3 to 5% stretch. | |
| B4 | Paint a short unitard (torso, tops of arms and legs); Make Pieces; simulate. | About ten pieces; seams within 0.7 mm; no cloth in the body. The bust gores stretch up to about 11% (known). | |
| B5 | Change **Ease** to -10% and +10%, and **Fabric** from Jersey to another; Make Pieces again. | Tighter or looser pieces; the new fabric on them. | |
| B6 | With an existing pattern open, Make Pieces. | The pieces are added to it; with none open, a new pattern starts. UVs are repacked without overlaps. | |
| B7 | Paint scattered dabs that do not join. | Small isolated scraps are left out, and the panel says how many. | |
| B8 | Repeat B1 on Human Male 3.0. | As B1. | |
| B9 | Open the region pattern on another race. | The pieces are laid round the body parts (the exact painted layout belongs to the race it was painted on). | |
| B10 | Undo after Make Pieces. | The pattern returns to before. | |

### Holds Hair Down (smoosh plane, build time)

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| H1 | Beanie: Publish section shows **Holds Hair Down** (on by default for head pieces); bake. | A `<slot>_SmooshPlane` slot is made beside the slot, and the slot is tagged `Smooshtarget`. | |
| H2 | Try on with long smooshable hair. | Hair stays inside the beanie (scripted: points outside fell from about 1,100 to 2,100 down to 86 to 141). | |
| H3 | **Check Hair...** | Lists the Global Library hair slots that UMA will not hold down; offering to fix saves only those slots. Run it once and check the Global Library and hair slots in git afterwards. | |
| H4 | Turn Holds Hair Down off and bake again. | No smoosh plane slot or tag; nothing left over from the previous bake. | |
| H5 | A pattern with no head pieces. | The toggle does not show. | |
| H6 | Equip the baked beanie in Play mode and in a player build. | Hair held down at run time with UMA's `SmooshSlotPhysics`. *(none)* | |

### Sharp seams and sharp edges

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| E1 | Pattern Canvas: turn on **Sharp Folds** on the Pleated Skirt's pleats and **Sharp** on a side seam; bake. | Crisp creases along the folds and the seam on the baked slot; the rest shaded smoothly. | |
| E2 | Bake the same skirt without Sharp. | All smooth, as before. | |
| E3 | Draw a fold line inside a piece and turn on its **Sharp**; bake. | A crease along the line. | |
| E4 | Open the baked sharp slot in the Vertex Editor; Sculpt a little near the crease; save. | The crease stays sharp; the two sides never separate. | |
| E5 | Slot inspector: **Recalculate Normals** on the baked sharp slot. | The crease stays sharp (the slot's `normalSides` are used). | |
| E6 | Equip the sharp slot on a character with other clothes and build it. | The crease survives slot processing and the character build; no seams appear elsewhere. | |
| E7 | Sharp skirt with LOD Slots 2, Inside Facing and Rim. | LODs and facing keep the creases; no cracks at the rim. *(none)* | |
| E8 | Sharp skirt baked coarse with a normal map from a fine drape. | Creases sharp on the coarse slot; the map adds the small folds. *(none)* | |
| E9 | Note: the live cloth in the designer is still shaded smooth while the baked slot is sharp. | Known; decide if it matters. | |

### Fit Brush view and Fit Tools toolbar (2026-11-24)

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| F1 | Pick the Fit Brush. | The cloth stays lit with its folds; painted areas tinted (validated once on the Oxford Shirt). | Pass 2026-11-24 |
| F2 | Turn on back faces with the Fit Brush active. | The inside is lit and tinted too. | Pass 2026-11-24 |
| F3 | Switch to Stretch and Body Distance views. | Unchanged unlit colour maps. | Pass 2026-11-24 |
| F4 | Pick the Fit Brush. | A small **Fit Tools** toolbar appears just above Cloth Tools, with Hug, Smooth, Puff, Slim, Stiffen, Weight and Erase; it follows the panel and the panel follows it; it hides when another tool is picked. | Pass 2026-11-24 |
| F5 | Move the Cloth Tools toolbar, pick another tool and the Fit Brush again. | Fit Tools reappears above it. | Pass 2026-11-24 |
| F6 | With the Publish section open, orbit the Scene view. | Smooth (validated once). | Pass 2026-11-24 |
| F7 | Pattern Canvas, Edit mode: drag an edge of a piece (or a point) outward by several centimetres, then open the **UVs** view; then **Repack UVs**. | The UVs view lists the piece at once ("has grown out of its place in the texture", or "leaves the texture"); Repack UVs asks first, then the warning clears. Islands are placed by the packing: they cannot be dragged in the UVs view. | Pass 2026-11-24 (the long pause after a big edit is the cloth rebuild, about 2.3 s on the Belted Dress: a progress bar now shows it, F23) |
| F8 | Open the designer, then look at the Vertex Editor window and the top of the Scene view's tools panel. Then close it and use **Open Vertex Editor** on a scene character. | In the designer: no Authoring Workflow row (Sculpt, Vertex Paint, Advanced, Blendshapes) and no Clothes Designer button. In the plain Vertex Editor both rows are back. | Pass 2026-11-24 |
| F9 | **Samples** > Polo Shirt on Human Male 3.0, Simulate until it settles, look from the front and the back. Then Forte Shirt. | Polo: the collar is turned down over its stand, points spread at the front, the leaf reaching the neck seam at the back (a few spots of stand may show through it, as on the Oxford Shirt: the Roll tool lifts them). Forte: the band stands up round the neck. | Pass 2026-11-24 |
| F10 | **Generator** > Polo Shirt preset, then the Shirt preset; let each drape. | Both collars turned down over the stand, the leaf reaching the neck seam at the back (spots of stand may show through, as F9). The **Collar** list still offers **Band** for a stand collar. | Pass 2026-11-24 |
| F11 | Drape a sample, then press **Publish** at the end of the Cloth Tools toolbar. Leave Wardrobe Region on "Choose...", then pick one; press Publish. Then Publish again. | The Publish window asks for Name, Wardrobe Region (the race's regions), Material and Folder, and shows the character's race; Publish stays disabled until a region is chosen. After Publish: slot, overlay and recipe in the folder and in the Global Library, the recipe's compatible race is the character's and its wardrobe slot the region chosen, the pattern and fit are saved, the try-on wears the clothes. The second time the region is already filled in and the note says the same assets are updated. | Pass 2026-11-24 |
| F12 | **Samples** > Elastic Waist Dress; Simulate until it settles; Publish and look at the try-on. Repeat on Human Male 3.0. | The bodice and skirt are gathered into the band at the waist; the band stays outside the gathers. Baked, the band stands about 4 mm out from the dress with crisp top and bottom edges. | Pass 2026-11-24 |
| F13 | **Samples** > Elastic Waist Dress (its band is sewn to the bodice). Grab tool, **Grab One Layer** on: press on the bodice just above the band and drag up; release. Then the same with it off. | On: only the bodice moves, the band stays exactly where it was; after release the bodice settles with slack over the band. Off: the grab carries on across the seam, so the band comes up with the bodice. (On the Belted Dress both look alike: the belt is joined only by its belt loops near the side seams, about 18 cm along the cloth from the front, beyond a 6 cm brush, so a plain grab there takes no belt either; One Layer only holds the belt still.) | Pass 2026-11-24 (on the Elastic Waist Dress) |
| F14 | Pattern Canvas: give any piece a **Thickness** (for example 5 mm on a pocket), bake. Mirror a thick piece. | The baked piece stands out by that much with crisp edges; the drape in the designer is unchanged; the mirror keeps the thickness. | Pass 2026-11-24 |
| F15 | **Samples** > Belted Dress; Simulate. Watch the Fit status, then look at the waist from the front and side; repeat on Human Male 3.0. | The belt starts outside the dress, the status says *Tightening free pieces* for about a second, the belt closes round the loose dress and gathers it in at the waist to nearly half, and stays there once gravity comes in. The gathers stay under the belt; a few folds may show at its top and bottom edges. | Pass 2026-11-24 ("Worked great"; Grab One Layer worked well with it) |
| F16 | With the Elastic Waist Dress or the Belted Dress draped, look at the band or belt in the designer; change a piece's **Thickness** on the Pattern Canvas. | The band stands out by its thickness on the live cloth (not only after baking); a new thickness is drawn at once, without laying the cloth out again. | Pass 2026-11-24 |
| F17 | **Samples** > Polo Shirt, then Oxford Shirt; Simulate on Human Female 3.0 and Human Male 3.0; look at the collar from the front, side and back. | The collar's leaf lies over its stand and turns down (on the male the polo leaf no longer flips up); little or none of the stand shows through the leaf. | Pass 2026-11-24 |
| F18 | **Samples** > Leather Armor and Elastic Waist Dress; Simulate. Note how long the Belted Dress takes to settle against a one-layer sample of similar size. | Both drape as before. Garments with more than one layer settle about a fifth slower; one-layer garments as fast as before. | 2026-11-24: the Elastic Waist Dress's seam lines flashed at the band's edges and spiked out at the body's outline while orbiting (fixed: F19). Pass 2026-11-24 |
| F19 | **Samples** > Elastic Waist Dress; Simulate; orbit round the waist, especially side on. | The band's seam lines sit steady along its top and bottom outer edges (no flashing), and no coloured spikes stick out at the body's outline. Other seams look as before. | Pass 2026-11-24 |
| F20 | Pattern Canvas: pan so that pieces run under the side panel and up under the toolbar. | The pieces stop at the canvas's edge: the side panel and the toolbar stay clean and usable. | Pass 2026-11-24 |
| F21 | Pattern Canvas on the Belted Dress, **Frame All**: Shift-drag a belt from anywhere on it (its middle, an edge, a corner, a belt-loop dot). Then without Shift, drag an edge of a dress panel, and a curved edge (a sleeve cap). Undo each. | Shift: the whole belt moves every time. An edge drag moves that edge (both its ends), the edges beside it stretch, a curve keeps its shape; the status line mentions Shift. Each drag is one Undo step. A plain click on an edge still only selects it, and a double-click still adds a point. | Pass 2026-11-24 |
| F22 | Pattern Canvas on the Belted Dress, **Frame All**, Edit mode: press and drag inside a belt a little away from its edges (no Shift). Then press just inside an edge and drag; then just outside it. Switch to Sew and click just inside an edge. | Inside, a few pixels from the edges, the whole belt moves. Right at the edge (within about 3 pixels inside, or 8 outside) the edge moves. In Sew mode an edge is picked from as far inside as before. | Pass 2026-11-24 |
| F23 | **Samples** > Belted Dress; on the Pattern Canvas drag the Front's side edge out by 20 cm or more and release; repeat on a larger garment (or after a few such edits). Then the same on the Sheath Dress. | A rebuild long enough to notice (the 8254-point dress that showed the pause) shows a *Clothes Designer* progress bar at once (*Laying the pieces round the body...*, then *Building the cloth...*), and the belt still starts outside the dress and tightens round it. Short rebuilds (the samples, well under a second) show no bar and no flash of one. | Pass 2026-11-24 |
| F24 | Open any dress; in a front piece's placement set **Distance** to -20 cm (it then lies against the body however far out the rest are laid). Let the cloth rebuild; read the Fit messages. Set Distance back. | The rebuild takes about as long as usual, not seconds longer. The message says how many points start inside the body, names that piece, and says laying the pieces further out did not free them. Other samples lay out and drape as before. | Pass 2026-11-24 |
| F25 | Drape a sample with no trims and press **Publish**; then a pattern with trims (add one with **Add Trim** on the Pattern Canvas). Look at the panel's Publish section too. | The Publish window has **Publish and Open in Overlay Painter** beside Publish (only with the Overlay Painter installed); its note says what it does and how many trims come along. It publishes as Publish does (slot, overlay and recipe in the Global Library, region, race), closes the designer and opens the slot in the Overlay Painter: with no path layers for the first, one per trim for the second. The panel no longer has *Publish and Paint Trims*. In the Overlay Painter, the designer's Cloth Tools (and Fit Tools) toolbars are gone. | Pass 2026-11-25 (after the toolbar fix) |
| F26 | **Size** tool (Cloth Tools, next to Place). On a draped **T-Shirt**: click the front; drag its red right handle out, its left handle out, its green hem handle down; drag inside the front across it; Shift-drag a handle; type a Width in the panel; Undo. Drag quickly and let go at once. Press Escape during a drag. On the **Belted Dress**: drag the front belt's hem handle down; again with **Resize Matching Pieces** off. Drag down a sleeve. On a freshly laid-out (not simulated) sample, drag a handle. | The selected piece shows red handles at its sides and a green one at its hem (the pointer becomes a scale arrow over them). While dragging, the cloth itself shows the new size at once (the front and back further out round the body, a longer hem lower, the belt taller), with the handles and outline moving with it, and the drag stays smooth; a label shows the new width and length and what else changes (the Chest size, the back). On release the cloth holds that shape a moment, then settles into the simulated result without jumping back. A side handle changes only the width, the hem handle only the length; Shift both. On release the cloth carries on from there and settles, seams closed, none in the body; nothing jumps or blows apart, even after a quick drag or a typed size. Escape puts the cloth back as it was. The belt's halves grow together; with Matching off only the front belt does and the panel names the uneven seams. A sleeve lengthens along the arm. Laid-out cloth is laid out again at the new size on release. Each change is one Undo step. | Pass 2026-11-25 |
| F27 | Add two or three trims on the Pattern Canvas (**Add Trim** on different edges), drape, then **Publish** > **Publish and Open in Overlay Painter**. In the painter, look at the Layers panel and the group's properties; then click each trim in the Scene view and in the UV window; drag a point of the selected one; Shift-click to add a point. Turn **Edit Paths Together** off on the group, and back on (Undo too). | The trims are path layers inside a group named **Trim**, which is selected: every trim is drawn, faintly, in both views, the one under the pointer brighter. Clicking a trim selects its layer (the Layers panel follows) and shows its points; the other trims stay drawn and clicking one switches to it. Dragging and Shift-clicking work on the selected trim as before. With the option off, only the selected path is drawn and clicking another does nothing; Undo turns it back. || Pass 2026-11-25 |
| F28 | Pattern Canvas, Edit mode, on a sample: drag a corner close to another piece's corner height and across from a point; turn **Snap** off and drag again. Choose **Horizontal**, then **Vertical**, and drag; back on **Free**, drag holding Ctrl. Turn **Mirror X** on and drag a front's side point, its side edge, a shoulder curve's handle and the centre-front neck point; press Add Diamond Dart, drag one of the pair, change its Width in the side panel, and press Ctrl+D on it. With Mirror X still on, turn it off, add a single diamond dart (on the middle), turn Mirror X on and drag the dart a little across, then well across, with Free, Horizontal and Vertical. Press Escape mid-drag. Nudge with the arrow keys (and Shift). Select a diamond dart, type a Width without Return, pick the other dart, then type its width and press Return. Look at the Cloth Tools toolbar. | Snapped moves line up exactly with a dotted pink guide; with Snap off, no guides. Horizontal and Vertical (and Ctrl) keep the move to one axis, with a faint axis line; a label shows the move. With Mirror X the mirror point, edge and handle follow exactly and the neck point moves only up and down; Add Diamond Dart gives a mirrored pair inside the piece; the other dart follows the drag and takes the new width; Ctrl+D on a dart of the pair gives a mirror copy, not a new piece. The single dart on the middle stays on it for a small wobble (pink axis), then comes off and moves as the toolbar allows (Horizontal: across only); the status line suggests Ctrl+D for its twin; a point of it, or the neck point, still moves only up and down. Escape puts it back. Nudges move a millimetre (a centimetre with Shift) and the cloth rebuilds after a moment. The second dart gets exactly the width typed for it; the first keeps its own. Measure shows a tape measure, clearly unlike Size. || Pass 2026-11-25 |
| F29 | Pattern Canvas on a sample. Draw mode, Seam Line: click near two corners of a piece (a few pixels off), then draw with Ctrl held, and from a point inside to near a side edge with Ctrl. Cut Piece with **Path** on: click three points down a front, double-click. Select a seam line whose ends meet the outline and press **Cut Piece Along Line**. Edit mode: split a sewn edge, then delete the point the split made; select an edge and press **Merge Its Points**. Piece panel: **Symmetrize** a front with one shoulder point dragged up 1 cm; **Cut in Half** a back; **Duplicate Sewn Round** the Oxford collar or both Belted Dress belts; **Duplicate with Seams** a front and then a back. Simulate after each. | Clicks land exactly on the corners (pink ring); Ctrl keeps the line level, and onto the side edge exactly. The path cut splits the piece through the clicked points, sewn back; the line cut follows the line, which goes. Deleting the split point gives the one seam back; merging points leaves the neighbours sewn. Symmetrize makes the front symmetric and keeps its seams; Cut in Half leaves a linked mirror that follows edits to the kept half. The sewn-round copy lies on its original on the body, a layer outside it; the two-ply belts still close round the dress. The front and back copies come out sewn to each other as the originals are. | |

## Priority 2: milestones 1 to 5 (2026-11-22)

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| M1 | Vest on Human Female 3.0: sew, drape, bake, equip on a fresh avatar of the race. | Equips; looks like the drape; arms raised: no cloth in the body, seams closed. | |
| M2 | Repeat the bake with a posed, DNA-changed scene avatar selected. | Same slot within tolerance (it is baked against the race's reference). | |
| M3 | Draw the vest on a blank canvas (split, move, curve, opening, mirror, sew); save unfinished; restart the Editor; finish; save a round-neck variant. | Both reopen on their own with the same pieces, seams and layout. | |
| M4 | Move a pattern asset in the Project; open its fit. | The fit finds the moved pattern. | |
| M5 | Fitting: unsew and resew a side, reverse a seam, paint a soft pin, lock a shoulder, grab the front and undo. | Each works and undoes; the hem stays open; the back does not move with a local front grab. | |
| M6 | Change the Mesh Level on a kept drape; **Keep Drape**; close and reopen. | Pins, seams and folds carry over; the same folds come back. | |
| M7 | Pause, Step, Reset; start a simulation and close the designer mid-way. | Each does what its tooltip says; closing mid-simulation leaves nothing in the scene. | |
| M8 | **Use CPU Solver** for a whole session; bake. | About 20 ms a frame on the vest; the bake passes the same checks. | |
| M9 | Fit the vest on two DNA shapes of the race (Body Shapes). | Re-sizes and re-lays automatically; no weights from the wrong limb. | |
| M10 | **Fit Existing Slot** on a slot from its source; save a copy and a corrective blendshape. | Keeps UVs, weights, triangles; the blendshape copies the change. | |
| M11 | **Hide Body Underneath** on, Add to Global Library on; bake; pose neutral, arms raised, elbows bent. | No skin missing at the neck, armholes or hem. | |
| M12 | Bake, then bake again after editing. | The same slot, overlay and recipe (same GUIDs); hand-made recipe changes and painted overlay textures are kept. | |
| M13 | Bake with a problem (no name, no material). | Refused with the reasons listed; nothing written. | |
| M14 | Check the Global Library after a bake (git diff or the library window). | Only the new slot, overlay and recipe added. Note: the recipe is saved with `UMAAssetIndexer.ForceSave`, which writes the library's whole in-memory state; on 2026-11-24 a plain save of that state was seen writing 41 run-time baked body slots into `AssetIndexer.asset`. Look for `*_baked_*` entries without a GUID. | |
| M15 | Leave the stage with unsaved pattern and fit; try **Save**, **Discard** and **Cancel** in turn. | Each does what it says. *(none: the dialog's choices were never exercised)* | |
| M16 | Edit a script while the designer is open (assembly reload). | The designer closes cleanly; GPU buffers released; no cloth left in the scene; unsaved work is not silently lost. *(none)* | |
| M17 | Choose the race with the race picker window, by clicking. | Opens on that race. *(none)* | |
| M18 | **Publish and Sculpt**; sculpt the baked slot a little; save. | Sculpt opens on a character wearing the clothes; the sculpted slot saves. *(the baked slot in Sculpt was not checked)* | |
| M19 | Plugins page: remove Clothes Designer, then install it again with its Examples and Tests. | Examples and Tests are listed under Clothes Designer, indented. After removal, patterns and fits stay on disk and baked clothes still equip; reinstalling reopens the fits. *(none: only the package builds were checked)* | |
| M20 | Build a player with the plugin installed; equip a baked recipe in it. | The player has no Clothes Designer type, compute shader or authoring asset, and the clothes equip. *(none: only dependencies were checked)* | |
| M21 | Documentation Browser. | The Clothes Designer pages show, including Body Region, Stiffen and Weight, Hats and hoods, Sharp seams. | |

## Priority 3: gap work and later additions (2026-11-23)

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| G1 | Lines inside a piece: a seam line, a fold line, a hole; tacks and buttons. | Lines stay as mesh edges; holes cut out with facing; buttons sit on the cloth after the bake. | |
| G2 | Freer seams: sew part of an edge; Shift-click to share one edge among several; split a sewn edge. | Spans sew; shared edges split by length; splitting keeps both halves sewn. | |
| G3 | **Worn Underneath** a medieval armour recipe; **Size over Them**; **Freeze** a piece. | Cloth lies over the worn clothes; sizes measured over them; a frozen piece does not move. | |
| G4 | Self-collision: a long skirt folded onto itself; pieces on different layers crossing. | No cloth through itself; the higher layer stays outside. | |
| G5 | Cut a piece (sewn back and not), merge two pieces, edit a linked mirror. | Seams, UVs and placement follow. | |
| G6 | **Show**: Stretch, Body Distance; **Measure** tool. | Colours a few times a second; girths of cloth and body at a height. | |
| G7 | A fabric with different stretch across the grain. | The cloth stretches more across than along. | |
| G8 | **LOD Slots** 2, **Full Thickness**, **Topstitching**. | `Name_LOD1`, `_LOD2` slots; thickness at every opening; stitches beside the seams on the overlay. | |
| G9 | Parts and buttons; a try-on animation, scrubbed and played. | Parts follow the cloth; the clothes move with the animation. | |
| G10 | Piece looks (a material per piece) and trims (Publish window > Publish and Open in Overlay Painter, with the Overlay Painter installed). | Each piece's look on its own overlay; trims open as path layers in the Overlay Painter. | |
| G11 | Loose clothes weights: a long skirt or coat, walk animation. | The hem does not follow the wrong leg. | |
| G12 | Body Shapes: grade the garment over several DNA shapes. | Fits each shape; blendshapes or sizes as set. | |
| G13 | **Cloth Motion** on a skirt; Physics Avatar on the character; Play mode. | The skirt swings, the waistband stays. | |
| G14 | Draw Piece, Dart and Pleat tools on the character. | A piece unrolled and laid where drawn; a dart or pleat sewn on the body. | |
| G15 | Mesh Topology: Grain Grid (quad remesh). | An even lattice along the grain; seams pair; UVs kept. | |
| G16 | **Garment generator** (O1): each type and the style values. | A finished pattern that drapes on the body (male: skirts and wool trousers may settle 7 to 20 cm low, known). | |
| G17 | Fit Brush Hug, Smooth, Puff, Slim (O2) on a loose tee. | Hug shrink-wraps; Smooth flattens; Puff and Slim push out and in. | |
| G18 | Sleeves: Tilt and End settings on the T-Shirt; Close button while the Place tool is on. | Sleeves close round the arms; Close works. | |
| G19 | Place tool: layout guide; select a piece in the Scene view, then in the Pattern Canvas. | The same piece is selected in both. | |
| G20 | Pattern Canvas: Undo moving a point, a handle and a whole piece; speed with diamond darts on the Oxford Shirt. | Each is one Undo step; the canvas stays responsive. | |
| G21 | **Roll** tool on the Oxford Shirt collar and box pleat. | The collar and pleat are lifted clear; no slit showing the pleat's inside. | |
| G22 | Converter: convert a modelled garment (UMA 3 skirt, hoodie) to a pattern. | Pieces at their 3D size, sewn where they share points; some tidying expected. | |
| G23 | Collars: Oxford Shirt collar (firm stand, light leaf on a folded seam). | The leaf turns down over the stand. | |
| G24 | Fine drape, coarse slot, normal map; Inside Facing and Rim; darts and pleats; Show Seams. | As described in the first plan's results; the rim reaches every corner. | |

## Platforms and pipelines not yet tried

| ID | Do | Expect | Result |
| --- | --- | --- | --- |
| X1 | HDRP (the copy at `C:/GitHub/UMA_HDRPCheck`): open the designer, simulate, Fit Brush view, Stretch view, Body Region overlay, sample browser pictures; bake and equip. | Everything draws (the Fit Brush view relies on a second material and `Hidden/Internal-Colored`; the maps on `Sprites/Default`). *(none)* | |
| X2 | Built-in pipeline: the same as X1. | As X1. *(none)* | |
| X3 | Other graphics APIs: Direct3D 12, Vulkan (and Metal on a Mac). | The GPU solver drapes like the CPU solver (all GPU numbers so far are Direct3D 11 on an RTX 5070 Ti). *(none)* | |
| X4 | Compute shaders unavailable (or forced off). | Falls back to the CPU solver, and the Fit section's Solver row says the GPU cannot run compute; so far checked only through Use CPU Solver. | |
| X5 | Unity 6.4 or later (test project `F:/UMA3TestProjects/u66urptest`): bake a sample and equip it. | Correct vertex count (6.6 changed how submesh ranges are kept). *(none)* | |
| X6 | Other races (an Elf, a Half-Orc, a Sprite race): drape a tee, a skirt and a Body Region sleeve. | Pieces laid round the body; seams close. *(none)* | Pass 2026-11-25 |

## Known limitations to confirm or accept

- Edge stretch above 5% at the armhole corners (vest; up to 48% next to the shoulder seam's ends at 1 cm).
- Contact can leave a fraction of a millimetre at sharp body edges.
- The live cloth in the designer is shaded smooth where the baked slot is sharp.
- Body Region unitard: bust gores stretch about 11% (darts would take it up).
- Render on This Body: single steps of about a quarter second.
- A weighted hem can pull the friction-held Circle Skirt off the hips.
- Pin layers (the rest of O15) are not built; run-time hood smooshing is only planned (`RealtimeHoodSmooshPlan.md`).
- Two Clothes Designer tests hold the Editor 20 to 30 seconds each during a test run (`DartsAndPleatsTests.PleatLine_StopsWhere...` and `SamplePatternsTests.SaveAll...`, both go over all 28 samples).

## Automated tests to add

- `UvLayout.FindProblems` cache: the same answers as the full check, and a new answer after moving a UV, shrinking an island's reserved space or flipping a triangle (checked by script on 2026-11-24, 11 cases).
- Fit Brush view: two materials (lit, then the paint pass) while the Fit Brush is the tool, one otherwise; both destroyed with the preview.
- Sample browser: opens, filters by search and category, loads a sample.
- Holds Hair Down: `HairNotSmooshable` lists only slots that are not smooshable or not tagged; the fix writes only those.
- Sharp edges through a character build (slot processing and mesh combining keep split normals): a PlayMode or runtime test.
- Smoosh plane at run time: a PlayMode test that builds a character with the beanie and smooshable hair.
- A bake does not change `AssetIndexer.asset` beyond its own slot, overlay and recipe.

## Outside the designer (from the 2026-11-24 regression)

Not Clothes Designer items, listed so they are not lost: `ModelToRaceProjectMenuTests` calls the old menu path; `GeneratorQualityTests` (3) fail outside the Test Runner's empty scene and should be checked in it; the PlayMode suites and five play-mode tests were not run; several fixtures leave test folders in `Assets` when a file is briefly locked by another program. Details in `ClothesDesignerV2Plan.md`, Results, Full regression.
