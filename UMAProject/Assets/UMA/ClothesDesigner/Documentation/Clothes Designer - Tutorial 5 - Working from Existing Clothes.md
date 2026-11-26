# Tutorial 5: working from existing clothes

You do not have to start from nothing. The designer can take clothes you already have in two ways, and they do very different jobs.

**Time:** about 20 minutes. **You need:** a wardrobe recipe the race can wear (any clothing from UMA 3 Content will do), and [Tutorial 1](Clothes%20Designer%20-%20Tutorial%201%20-%20Your%20First%20Garment.md).

Other guides: [Start Here](!Clothes%20Designer%20-%20Start%20Here.md), [Fit Existing Slot](Clothes%20Designer%20-%20Fit%20Existing%20Slot.md), [Pattern Canvas](Clothes%20Designer%20-%20Pattern%20Canvas.md).

## Which one do I need?

| Question | **From Clothes** | **Fit Existing Slot** |
| --- | --- | --- |
| What it does | Turns a modelled garment into a sewing pattern you can simulate and change. | Moves a garment onto a different body shape and changes nothing else. |
| The result | A new pattern, then new clothes baked from it. | A copy of the slot, or a blendshape on a copy. |
| Vertex order, weights, blendshapes | New: the clothes are meshed and weighted again. | Kept exactly. |
| UVs | Kept: each UV island becomes a piece in the same place. | Kept exactly. |
| Use it to | Loosen, lengthen, recut or re-drape modelled clothes; learn how a garment is cut. | Fit clothing to a heavier, thinner or more muscular body. |
| Good for loose clothes | Yes. They are draped by the simulation. | No. Loose parts keep their distance from the body instead of draping. |

## Part A: a pattern from modelled clothes

### Step 1: convert

1. Choose **UMA > Clothes Designer > Create Clothes on Race...** and open the designer on the race the clothes are worn by.
2. In the panel's **Pattern** section, drag the wardrobe recipe into **From Clothes**.
3. Press **Convert**.

A message under the row says what was found, for example *6 pieces and 9 seams from 6 UV islands.* Other lines may follow:

| Message | What it means |
| --- | --- |
| *Folds: the pieces were made ...% smaller in area on average* | Folds modelled into the mesh were measured and taken out of the pieces' size, so the simulation can make its own. |
| *... tiny islands were left out* | Scraps too small to be pieces were dropped. |
| *... holes inside islands were left out* | A piece has one outline. Cut the hole again with a **Hole** line if you need it. |
| *... islands are mirrored in the slot's texture* | Those pieces keep their place in the texture, but a print on them reads mirrored. |
| *... stretches of outline ... could not pair; they were left open* | The converter found edges that meet but could not sew them. You sew them in Step 2. |
| *The outline of '...' crosses itself* | That UV island folds over itself. Fix the outline on the Pattern Canvas before simulating. |

Press **Dismiss** when you have read them.

### How the conversion works

- Each **UV island** becomes a piece, at its true size, turned upright and placed round the body part it is worn on.
- Islands are **sewn where they meet** on the mesh. Free edges become openings.
- The pieces **keep the clothes' UVs**, so the original textures still fit the new clothes.

Game clothes are not cut like sewing patterns. Expect slivers, islands that do not follow real seams, and some open edges. A pattern made by the Clothes Designer itself converts back to the same pieces and seams.

### Step 2: tidy the pattern

1. Press **Pattern Canvas**.
2. Read **Checks** at the bottom of the side panel. It lists what needs attention: edges that are neither sewn nor openings, seams whose sides differ in length, outlines that cross themselves.
3. Fix them:
   - **An open edge that should be sewn:** choose **Sew**, click it, then click the edge it joins.
   - **An open edge that really is an opening** (a cuff, a hem): select it and turn on **Opening**.
   - **A sliver or a scrap:** select it and press **Delete Piece**, or select it and press **Merge with ... along ...** in the piece panel to join it to its neighbour along the seam between them.
   - **A twisted seam** (its dotted lines cross): select either edge and press **Reverse**.
4. Stop when **Checks** says *Ready to simulate.*

> **Tip:** the Scene view shows the pieces laid round the body as you work. A piece laid on the wrong part can be moved with the **Place** tool, or by setting its **Body Part** and **Faces** under **Placement on the Body**.

### Step 3: drape, change and publish

1. Press **Simulate**. The clothes now hang by the simulation, not as they were modelled.
2. Change them: add ease in **Sizes**, lengthen a piece with the **Size** tool, change the **Fabric**, add darts or pleats.
3. **Publish** under a **new name**, so the original clothes stay as they are.

To use the original textures on the new clothes, put them on the published overlay: the pieces are in the same place in the texture as the islands they came from.

> **Note:** do not press **Repack UVs** on a converted pattern if you want to keep the original textures. Repacking moves the islands.

## Part B: refit clothing to another body shape

**Fit Existing Slot** keeps a garment exactly as it is (vertex order, UVs, bone weights, blendshapes) and only moves its vertices to follow a changed body.

### Step 1: open the clothing

1. Select the clothing's slot or its wardrobe recipe in the Project window.
2. Choose **Editors > Plugins > Clothes Designer** above its inspector.

The Vertex Editor opens with the clothing on a default body of a race that wears it. The panel is titled **Clothes Designer: Fit Existing Slot**. You are asked for the race when the clothing does not say.

### Step 2: bind

Press **Bind to Body**. Every vertex remembers the body triangle under it, its place on it and its distance from it.

Bind on the body shape the clothing was made for, which is the default shape unless you know otherwise. Vertices more than 15 cm from the body keep their place and are drawn in magenta.

### Step 3: change the body

In the Vertex Editor window, open **Body Shape (DNA)** and move a slider, for example *belly* or *upperWeight*.

The clothing follows straight away, drawn in blue over the body. A short relaxation removes kinks and keeps the cloth out of the body. Press **Refit to Current Body** to do it again on request.

### Step 4: save

| Button | What you get | Use it for |
| --- | --- | --- |
| **Save as New Slot** | A copy of the slot beside the original, in the refitted shape. | Clothing for one body type, such as a heavy character. |
| **Save as Blendshape** | A copy of the slot with the refit as a blendshape named *Refit*. | Dialling the fit in together with the matching DNA. |

The original slot is never changed.

> **Note:** if binding says the slot's mesh *does not match how it is worn here*, the clothing was probably converted from another rig. A refit saved from it may be offset.

## What you learned

- **From Clothes** makes a pattern from a garment's UV islands. Tidy it on the Pattern Canvas until **Checks** says *Ready to simulate*, then treat it like any pattern.
- **Fit Existing Slot** keeps the garment and moves it onto another body shape: **Bind to Body**, change the DNA, **Save as New Slot** or **Save as Blendshape**.
- Use the pattern route for loose clothes; they need the simulation to hang right.

## Next

- Make new clothes follow every body shape automatically: **Body Shapes** in [Tutorial 6: Game-Ready Clothes](Clothes%20Designer%20-%20Tutorial%206%20-%20Game-Ready%20Clothes.md).
- If the conversion gives trouble: [Troubleshooting](Clothes%20Designer%20-%20Troubleshooting.md).
