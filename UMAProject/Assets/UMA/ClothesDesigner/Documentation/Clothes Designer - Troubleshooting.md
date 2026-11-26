# Clothes Designer: troubleshooting

Find what you see, then the fix. Messages from the designer are shown in *italics* as they appear on screen.

Other guides: [Start Here](!Clothes%20Designer%20-%20Start%20Here.md), [Pattern Canvas](Clothes%20Designer%20-%20Pattern%20Canvas.md), [Fitting and Publishing](Clothes%20Designer%20-%20Fitting%20and%20Publishing.md).

## First, look here

Most problems show up in one of four places:

| Place | What it tells you |
| --- | --- |
| **Checks**, at the bottom of the Pattern Canvas side panel | What is wrong with the pattern: unsewn edges, uneven seams, crossing outlines. *Ready to simulate* means none. |
| The messages at the top of the **Fit** section | What happened when the cloth was built: pieces moved out of the body, sleeves laid badly, textures that left their place. |
| **Fit Report** | Open seams, cloth in the body and stretch, as numbers. |
| **Show** set to *Stretch* or *Body Distance* | Where on the cloth the problem is. |

## Opening the designer

| What you see | Why | Fix |
| --- | --- | --- |
| *A body for '...' could not be built.* | The race has no base recipe, or the recipe uses slots that are not in the UMA Global Library. | Give the race a base recipe and add its slots to the Global Library. |
| *No race with a base recipe was found in the UMA Global Library.* | The race picker lists races from the library. | Add your race to the Global Library (**UMA > Global Library**), or drag a RaceData asset into the **Race** field of the picker. |
| A warning that *This is a character from your scene, in its own pose and shape.* | The designer was opened on a scene character directly. | Use **UMA > Clothes Designer > Create Clothes on Race...** to fit on the race's default body. |
| The body's height warning in the Vertex Editor window | The race is built at an unusual scale. | Pattern sizes will be off by the same amount. Check the race's **Race Height**. |
| The **Clothes Designer** entry is missing | The plugin is not installed. | Install it from **UMA > Welcome to UMA > Plugins**. |

## Starting the drape

| What you see | Why | Fix |
| --- | --- | --- |
| *The pieces were moved ... further out so that none starts inside the body.* | Normal. The pieces were laid a little further from the body to clear it. | Nothing. |
| A message naming pieces that still start inside the body | Moving them out did not free them. Often a piece has a negative **Distance**. | Move those pieces with the **Place** tool, or raise their **Above Shoulders** or **Distance**. |
| Both pieces are on the front of the body | A copied piece kept the original's placement. | Select the piece and set **Faces (deg)**: 180 for a back. |
| A piece is on the torso but belongs on an arm, a leg or the head | Its **Body Part** is *Torso*. | Set **Body Part** under **Placement on the Body**, or under **Piece Placement** in the Vertex Editor window. |
| The Fit section says a sleeve's edges would meet above the arm | The sleeve is laid too far from the arm. | Bring its **End Distance** or **Distance** in, or tilt it down. |
| Every piece is laid round the torso, with a message | The race has no humanoid skeleton, so the designer cannot find its arms, legs and head. | Place the pieces by hand with the **Place** tool. |

## While it simulates

| What you see | Why | Fix |
| --- | --- | --- |
| The cloth twists across the body and bunches | A seam joins the wrong edges: a front's right side sewn to the back's right side. | On the Pattern Canvas, select the edge, press **Unsew**, and sew it to the opposite side. Pieces are drawn from outside, so a front's right meets a back's left. |
| One seam twists like a propeller, or gathers | Its ends are swapped. In the canvas its two dotted lines cross; in the Scene view its threads cross. | Select either edge and press **Reverse**. |
| A seam gathers along its length | Its two sides differ in length. **Checks** shows by how much. | Lengthen the shorter edge, or leave it: the longer side gathers, as in real sewing. |
| An edge hangs loose that should be closed | It is neither sewn nor an opening. **Checks** counts these. | Sew it, or mark it **Opening**. |
| A seam stays open by centimetres | The pieces cannot reach each other round the body, or a band cut in one piece has to be dragged round. | Place the pieces closer to where they join. Cut bands in a front and a back half. See *Collars, cuffs and bands*. |
| Trousers or a skirt slide down the hips | Nothing holds them: the waist is as wide as the hips, or the fabric stretches under its weight. | Cut the waist smaller than the hips, add a **Rib** waistband cut shorter than the waist, or **Pin** the waistband with **Pins Follow** on *Body*. |
| Limp cloth slides off or stretches long | Very soft, slippery cloth (*Silk*) cannot hold a heavy garment at the waist. | Use a firmer fabric, or hang the garment from the shoulders. |
| The cloth never settles and trembles | Rare: very stiff fabric on a very fine mesh. | Lower the Mesh Level while shaping, and raise it at the end. |
| A collar, cuff or pocket sinks into the cloth under it | Both pieces are on one layer, or it is a folded seam the simulation does not keep apart. | Give the upper piece a higher **Layer** on the Pattern Canvas. For the last millimetres, use the **Roll** tool as a final step, then **Keep Drape**. |
| A pleat's folds do not hold | The mesh is too coarse for the pleat. | Pieces with pleats are meshed finer automatically; if folds still open, raise the piece's **Mesh Detail**. |
| Cloth is inside the body (**Fit Report**, or red in *Body Distance*) | The cloth is cut too small there, or the body touches itself (an arm against the side). | Add ease in **Sizes**, or **Grab** the cloth out. Where the body touches itself, a few points cannot be freed. |
| Red in the *Stretch* view | The cloth is cut smaller than the body there and is not a stretch fabric. | Add ease, widen the piece with **Size**, or choose *Jersey* or *Spandex*. |

## Speed

| What you see | Why | Fix |
| --- | --- | --- |
| **Solver** reads *CPU*, and simulating is slow | The graphics card cannot run compute shaders, or **Use CPU Solver** is on. | Turn **Use CPU Solver** off if you can. Otherwise fit at *Draft* or *Medium* and raise the **Mesh Level** before baking. |
| *The CPU solver is slow at this many vertices.* | More than about 6,000 vertices on the CPU. | Lower the **Mesh Level** while fitting. |
| The Editor waits after each canvas change | A large garment is rebuilt after every change. A progress bar shows it. | Work at a coarser **Mesh Level** while drawing. |
| Garments with layers simulate slower | Keeping layers in order costs about a fifth more. | Expected. |

## Pattern Canvas

| What you see | Why | Fix |
| --- | --- | --- |
| *Open the Clothes Designer in the Vertex Editor and choose a pattern ...* | The canvas has no designer to edit for. | Open the designer, choose a pattern and press **Pattern Canvas** there. |
| Pressing inside a narrow piece moves the piece, not the edge | From inside, an edge is picked only very close to it. | Click the edge from outside the piece, or zoom in. |
| A click moves the whole piece when you wanted a point | **Shift** was held as you pressed. | Press Shift after you start dragging, to snap to whole centimetres. |
| **Opening** is greyed out | The edge is sewn. | **Unsew** it first. |
| *An opening cannot be sewn.* | One of the two edges is marked **Opening**. | Turn **Opening** off first. |
| *One of these edges is already sewn all along.* | Two seams cannot share the same stretch of an edge. | **Unsew** it, or shorten its seam with *Along this edge (%)*. |
| A dart or pleat is listed in **Checks** | It leaves the piece, runs past the end of its edge, or overlaps another. | Drag it back inside, or shorten it. |
| A size changes nothing | No piece is ticked for it, or its **At height** line misses the pieces. | Tick the pieces, and set **At height (cm)** to a height on the canvas that crosses them. |
| Dragging a piece's outline wider changes nothing on the body | A size sets that piece's width, and sizes it back. | Change the size's **Allowance**, or use the **Size** tool, which changes the size for you. |
| A linked mirror will not change | It follows its original. | Edit the original, or untick **Linked to ...** in the piece panel. |

## Cloth tools

| What you see | Why | Fix |
| --- | --- | --- |
| The **Cloth Tools** section and toolbar are missing | The cloth is not built yet. | Press **Build Cloth** in the Fit section; if it will not build, read the messages there. |
| The toolbar is gone from the Scene view | It was closed from the Scene view's overlay menu. | Turn **Cloth Tools** back on in that menu. |
| Moving a piece with **Place** undoes the drape | Moving a piece lays every piece out again. | Place pieces before simulating, and simulate afterwards. |
| **Grab** pulls a belt along with the dress | A plain grab reaches other layers through seams and tacks. | Turn on **Grab One Layer**. |
| A kept drape was dropped, with a message | The pattern or the Mesh Level changed since **Keep Drape**. | Simulate and press **Keep Drape** again. |
| *... body pins point at a part of the body that no longer exists.* | The body changed since the pins were made. | Erase those pins and pin again. |
| **Draw Piece**, **Dart** and **Pleat** do nothing | These tools need cloth on the body. | Start with at least one piece and build the cloth. |

## Publishing and baking

| What you see | Why | Fix |
| --- | --- | --- |
| *Simulate until the seams are sewn and the cloth hangs, then publish.* | Publish needs a drape. | Press **Simulate** and wait for *Settled*. |
| The bake buttons are greyed out | Something stops the bake. It is listed under the buttons. | Fix each line: usually a missing **Material**, or UV problems. |
| *... leaves the texture* or *... overlap in the texture* | A piece was redrawn larger than its place in the texture. | In the Pattern Canvas, **UVs** tab, press **Repack UVs**. A texture painted for the old layout will no longer line up. |
| *Choose a wardrobe region.* | The Publish window needs one until the clothes have been published once. | Choose the **Wardrobe Region**. |
| You can see through the clothes at the neck, armholes or hem | The material hides back faces. | Turn on **Inside Facing** (and **Rim**), use **Full Thickness**, or choose a double-sided material. |
| Skin pokes through when the character moves | The body under the clothes is still drawn. | Turn on **Hide Body Underneath** (with **Add to Global Library** on), and try another **Weighting** method. |
| A hem follows the wrong leg, or cloth stretches badly in a pose | The weighting method does not suit the garment. | In **Fabric > Bone Weights**, try **Volumetric** for long skirts, or **Auto**. Check single vertices in the Slot Weight Editor. |
| A vest's side lifts with the arm | The vest took weights from the arm beside it. | Turn on **Weights from Torso Only**. |
| The normal map was not put on the overlay | The overlay already has a normal map you chose. It is never replaced. | The new map is saved beside the slot; assign it yourself if you want it. |
| **Normal Map from Drape** is greyed out | The slot is not coarser than the drape. | Simulate at a finer **Mesh Level**, or set a coarser **Slot Mesh Level**. |
| **Publish and Open in Overlay Painter** is not in the Publish window | The Overlay Painter is not installed. | Install it from **UMA > Welcome to UMA > Plugins**. |
| Sculpted changes were lost | Baking again replaces the slot's mesh. | Sculpt last, after the final bake. |
| The clothes do not appear on your character | The recipe is not in the Global Library, or it is not compatible with that race. | Leave **Add to Global Library** on. A recipe is made compatible with the race it was fitted on; add other races to it yourself. |
| Hair pokes through a hat | The hair slot is not smooshable. | With **Holds Hair Down** on, press **Check Hair...** and let it fix the listed slots. |
| The clothes do not move in the Editor | Unity Cloth simulates only in Play mode. | Press Play. Add **Physics Avatar** to the character for collisions. |

## Saving and reopening

| What you see | Why | Fix |
| --- | --- | --- |
| A `*` beside **Pattern**, **Save Pattern** or **Save Fit** | There are unsaved changes. | Save. Closing the Vertex Editor with unsaved work asks first. |
| A fit opens without its folds | The drape was not kept, or the pattern changed since. | Simulate again. Press **Keep Drape** before **Save Fit** to store the folds. |
| *This fit has its own placement for this piece* | The fit was made before placements were kept on the pattern. | Press **Use the Pattern's Placement**, unless you want this fit to differ. |
| A pattern or fit will not open | The plugin is not installed. | Patterns and fits need the plugin. The published clothes do not. |
| The Generator replaced your edits | **Generate** drafts from the style, not from your edited pattern. It asks first. | Set the style first and edit by hand afterwards. Use **Save Pattern As** to keep an edited copy. |

## Still stuck?

1. Load the **Sleeveless Vest** from **Samples** on the race's default body. If it drapes and publishes, the plugin and the race are fine, and the problem is in your pattern.
2. Compare your pattern with a sample of the same kind on the Pattern Canvas: its placements, sizes, fabrics and layers.
3. Look in the Console for messages starting `[Clothes Designer]` or `[UMA]`.
