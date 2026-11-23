# Sculpting Mesh Modifiers

UMA's Mesh Modifier sculpting tools let you reshape one or more currently generated slots directly in the Scene view and save the result as a standard `MeshModifier` asset. Sculpting changes the preview mesh only; it does not overwrite a source `SlotDataAsset`, imported model, or original mesh.

The saved result is a sparse collection of vertex deltas for every slot changed during the session. Vertices that did not move are not stored. The resulting asset works through the normal UMA Mesh Modifier pipeline and can be assigned or driven in the same ways as other Mesh Modifiers.

## Before You Begin

You need:

- A `DynamicCharacterAvatar` that has generated successfully.
- At least one visible, editable slot containing mesh data.
- A Scene view in which the part of the character you want to sculpt is visible.

For the clearest and most reusable result, place the avatar in a neutral pose before opening the editor. Sculpting records vertex deltas against the generated slot shown in the editor. Extreme animation poses can make it harder to judge the intended shape.

## Open the Mesh Modifier Editor

1. Select the GameObject containing the `DynamicCharacterAvatar`.
2. In the avatar Inspector, find the **Editing tools** section, just below **Character & race** (in Standard and Advanced view).
3. Select **Open Vertex Editor (Sculpt, Paint, Mesh Modifiers)**.
4. Unity opens the Vertex Editor in the Scene view and its companion **Vertex Editor** window (it used to be called Mesh Modifiers), which holds the workflows, the **Save** panel, the build options, and the Advanced and Blendshapes editors.
5. The editor opens in **Sculpt** mode the first time. You can return to it at any time with the **Sculpt** button at the top of the tools panel; the other buttons there are **Vertex Paint**, **Advanced** and **Blendshapes**.

The editor remembers your tool choices between sessions, per user: the mode, the display options (**Original Materials**, **Vertex Colors** and **Wireframe**), each mode's brush with its radius, effect, falloff and options, and the Advanced selection tool, tab and action. It opens on them again next time, also after Unity restarts. A remembered choice that no longer exists falls back to its default. **Sculpt '\<shape\>'** in Transfer Blend Shapes always opens in Sculpt.

To inspect an existing Mesh Modifier, drag the asset onto the Mesh Modifier drop area in the avatar Inspector. A Mesh Modifier can contain changes for one or several slots; it does not destructively edit their source meshes.

### The stage layout

- The **tools panel** on the left of the Scene view holds the mode buttons, the display toggles and the options for the current mode. Its last line, always in view, says whether anything is unsaved; its **Save...** button opens the Save panel. All saving is done in the [Save panel](#save-your-work).
- The **Slots** panel chooses which slots are shown and which the tools may edit. See [Choose the slots to sculpt](#choose-the-slots-to-sculpt).
- The **floating toolbar** beside the panels holds the nine sculpt brushes, **Mask Paint**, **Mask Erase** and **Frame**, which frames the character from the starting camera angle. Hover a button for its name and what it does. Drag the toolbar's grip to move it, or double-click the grip to dock it beside the panels again. Its position is remembered.
- **Close** at the top right of the Scene view leaves the stage.

The UMA Toolbar overlay is hidden while the stage is open, because its commands act on the main scene. It returns when you close the stage.

### Scene display

The vertex editor opens with its existing pastel materials and wireframe enabled. Two independent toggles below the mode buttons let you change the preview without affecting saved data:

- **Original Materials** switches between the pastel editor materials and the materials captured from the generated UMA renderer. The setting is remembered between sessions, and Undo does not change it.
- **Wireframe** shows or hides the wireframe. It is drawn on the editable slots only; slots that are read-only in the Slots panel are drawn without it, so the wires show what the tools can change. The setting is remembered between sessions. The Scene view's own **Shaded Wireframe** draw mode still draws wires on everything, if you want that.

You can therefore use pastel with or without wireframe, or inspect the original textured materials with or without wireframe. These options affect only the editing-stage display and remain active across preview mesh rebuilds.

### Lighting

The stage lights the model with three point lighting: a key light, a fill light and a rim light, with matching ambient light and reflections. The same setup is used in the Built-in pipeline, URP and HDRP; the active pipeline is detected when the editor opens. The **Lighting** section below the display toggles controls it:

- **Scenario** picks the lighting condition: **Studio** (neutral, the default), **Outdoors - Sunny**, **Outdoors - Overcast**, **Outdoors - Sunset**, **Indoors** and **Night**. Hover over the field for a description of the current one.
- **Rotation** turns all three lights around the model, 0 to 360 degrees, so you can see a shape lit from any side without moving the camera.
- **Brightness** makes the lights and the ambient light brighter or dimmer. 1 is the scenario as designed.
- **Shadows** lets the key light cast shadows. The fill and rim lights never do.

The lighting applies to the pastel materials and to **Original Materials** alike, is remembered between sessions, and only exists in the editing stage: the scene's own lights, ambient settings and volumes are not used and not changed. The Scene view's background keeps its normal color in every scenario. To see the model without any shading, as before, turn off the Scene view's own lighting button in its toolbar; it is on again the next time the editor opens.

## Quick Start

1. Select **Sculpt**.
2. In the **Slots** panel, click **S** beside the slot you want to change. It becomes the only editable slot.
3. Choose a sculpt brush on the floating toolbar. **Add**, **Remove**, and **Smooth** are the easiest tools to learn; **Heal**, **Beautify**, **Grab**, **Crease**, **Pinch**, **Plane**, **Boundary**, and **Elastic Deform** provide more specialized deformation. The last group on the toolbar changes what is on the surface instead of its shape: **Comb Normals**, **Smooth Normals** and **UV Sculpt**. The **Tool** row of the panel names the active brush and what it does.
4. In the **Brush** section, start with a modest **Radius (mm)**, an **Effect %** around 10–25, and **Smooth** falloff.
5. Move the pointer over the editable slot. A 3D ring appears on a valid surface.
6. Hold the left mouse button and drag across the surface.
7. Release the button to finish the stroke. The normals around the vertices that moved are brought up to date.
8. Repeat with smaller brushes and lower strength for refinement.
9. In the **Save** panel of the Vertex Editor window, choose how to keep it. **Save as Mesh Modifier** is the usual choice; see [Save your work](#save-your-work).

Each save button is disabled, with the reason under it, until there is something it can save.

## Reading the 3D Brush

The sculpt brush is drawn in world space rather than as a flat circle on the screen.

- The ring is tangent to the surface under the pointer.
- The short line through its center indicates the averaged surface normal and therefore the direction used by Add and Remove. Only the normals on the side of the surface you are pointing at count, so on double-sided geometry (a reversed copy of the surface at the same positions, common on UMA 2 clothing) and two-layer garments the brush still points out of the surface.
- The ring continually changes position and orientation as it crosses the mesh.
- The brush ring and normal indicator are drawn in thick, high-visibility red in every sculpt and mask mode.
- The brush ignores the Scene depth buffer, preventing nearby triangles or overlapping clothing from clipping portions of the indicator.
- The mask is shown as a colored wash over the mesh while **Show** is on in the **Mask** section. Stronger mask weights are tinted more strongly.

The brush appears only when the pointer is over an editable slot. With **Ignore Locked Slots** on (the default, in the Brush section), the brush passes through slots that are not editable and reaches the editable slot behind them: solo the inside of a garment, or a slot covered by hair or a beard, and sculpt it through what is in front. Turn it off to have a locked slot in front block the brush. The option is shared with Vertex Paint and remembered.

Hold **Alt** to use normal Scene navigation. Sculpting is suspended during Alt navigation and while the pointer is over a panel or the floating toolbar.

## Sculpt Controls

### Choose the slots to sculpt

The **Slots** panel decides what the brushes can change. Each slot row has two checkboxes:

- **Show** displays the slot in the stage. A hidden slot cannot be edited.
- **Edit** lets the tools change the slot. Clicking a slot's name toggles Edit too.

The quick actions above the list are:

- **Edit: All / None / Invert / No Base**. **No Base** makes only the slots that are not part of the race's base recipe editable, for example every clothing slot. Base slots are marked `base` in the list.
- **Show: All / Editable Only / Invert**. Invert also swaps the wearables listed under **Wearables (N)**.
- **S** on a row solos that slot: it becomes the only editable slot and is shown if it was hidden.

Every shown slot with mesh data is editable until you lock it, so slots added to the recipe later are editable too. The same Slots panel drives Sculpt, Vertex Paint and Advanced selection.

The **Editing** row at the top of the Sculpt options names the editable slot, or says how many are editable (hover it for the list). Its **◎** button frames the editable slots in the Scene view; framing does not lock the camera.

With one editable slot, the brush and raycast ignore everything else. With several editable slots, the slot under the pointer becomes the target for that stroke and UMA keeps the changes made to every touched slot. When Sculpt opens, the first target is the first non-base editable slot (clothing before the body).

With several editable slots, co-located boundary vertices are treated as welded across them. Position, mask, and normal changes are synchronized at those seams, which helps keep connected pieces together. A Boundary stroke ignores a per-slot open edge when UMA recognizes it as a seam joined to another editable slot.

Locking or hiding a slot ends the current stroke but keeps that slot's edits in the sculpt session. **Save as Mesh Modifier** includes all changed slots, even ones you have since locked. **Save to Base Slot** and **Save as New Slot** need exactly one editable slot; solo the slot first.

The saved modifier uses the slot's source-slot key when one is available. This allows the modifier to target the correct UMA slot rather than an incidental generated instance name.

### Add

Add moves affected vertices outward along the averaged normal of the surface covered by the brush. Use it to build volume, round a silhouette, or push a garment away from the body.

On a sharply curved surface, the averaged normal produces a stable regional direction instead of moving every vertex along an unrelated screen-facing direction. Use a smaller radius near corners or thin structures when you need more local control.

### Remove

Remove moves affected vertices inward, opposite the averaged brush normal. Use it to reduce volume, create shallow depressions, or pull a garment closer to the body.

Remove is not a deletion tool. It changes vertex positions but does not remove triangles, vertices, UVs, skinning, or materials.

### Smooth

Smooth relaxes each affected vertex toward the average position of its directly connected one-ring neighbors. It operates on the target slot's topology and does not average against vertices from other slots. Vertices at the same position are treated as one welded sculpt vertex, including splits created for UV islands, hard normals, or material boundaries.

Use Smooth to soften lumpy strokes, blend a sculpt into the surrounding mesh, or reduce small surface irregularities. Several light strokes generally preserve the intended silhouette better than one very strong stroke. Open borders and seams have fewer connected neighbors, so inspect them from multiple angles after smoothing.

### Heal

Heal evens out how far the vertices have been moved, where Smooth evens out where they are. Every vertex has a movement: its position now, less its position in the base shape. While a blendshape is loaded for editing, the base is the character at rest, so the movement is the blendshape. Otherwise the base is the mesh as it was when the editor opened, so the movement is your sculpt. Heal replaces a vertex's movement with a blend of its neighbors' movements.

Where the movement becomes even, the surface is the base shape again, carried along with its surroundings. A panel that was modelled flat and evenly spaced becomes flat and evenly spaced again, with the vertex spacing the mesh was modelled with. Smooth cannot do that: it pulls vertices to the average position of their neighbors, which shrinks the area and leaves a pinch bunched together.

Use Heal for a pinch, fold, or stretched patch left by a blendshape fit (see [Blendshape Transfer](BlendshapeTransfer.md)), where neighboring vertices followed different parts of the body, and to soften an over-strong sculpt without flattening the detail under it.

- **The brush** works outward from good areas: start on healthy surface and brush into the damage. One stroke blends each vertex once, by Effect % and falloff; turn on **Constant Application**, or use several strokes, to build it up. Brushing healthy surface next to the damage softens the fit there slightly, so keep the radius close to the size of the damage.
- **Heal Masked** solves a whole area in one step. Paint the mask over the damage, select the Heal tool, and press **Heal Masked** in the Brush section. The masked vertices take their movement from the unmasked vertices around them. This is the quickest fix for one bad spot.
- **Heal Unmasked** is the opposite: everything not masked takes its movement from the masked vertices around it. Mask what must stay first. It refuses to run when nothing is masked, because that would even out the whole slot.

Both buttons work on every editable slot, keep vertices at the same position together (also across slots), leave points shared with a read-only slot where they are, and are one Undo step. A partly masked vertex is healed in proportion to its mask. An area with nothing around it to take a movement from is left alone and reported.

Heal has nothing to do when the movement is already even. If **Heal Masked** reports that it moved vertices by a fraction of a millimeter, the fold you see is not in the blendshape or the sculpt. It is usually the pose: a generated character's limbs can be rolled about their length compared with the pose the slot was modelled in, and clothing weighted to two limbs folds between them. Check the slot in the pose it was modelled in before sculpting it (the Transfer Blend Shapes preview draws that pose).

Heal makes an area move like its surroundings. If the surroundings are pulled in two directions, such as cloth spanning a gap, the healed area settles between them and may still need a Grab stroke.

### Beautify

Beautify straightens the lines of the mesh. Each vertex under the brush slides across the surface until it is in line with its neighbors on either side, along each edge loop through it. Use it where polygons are crooked: a wobbling edge loop around a joint, quads pulled into kites by an earlier sculpt, a patch that Smooth would shrink.

Slots are stored as triangles, so Beautify first works out the modelled quads: an edge that is the longest edge of both triangles it joins is a quad's diagonal and is not treated as a line of the mesh. A regular vertex then has four edges, two each way, and belongs where the two lines through its opposite neighbors meet. A vertex with six edges (a triangle mesh) has three such lines. Any other vertex (a pole) moves to the middle of its neighbors.

- **Even Spacing** also evens out the distances between the vertices along each line. At 0 (the default) the spacing stays as modelled, so the closer loops at a joint stay closer and only the wobble goes. At 1 a vertex goes to the middle of its neighbors on those lines.
- **Keep Shape** (on by default) moves vertices only across the surface and puts each one back on the triangles it left, so the silhouette does not change. Turn it off to straighten the lines in depth as well, which flattens the surface under the brush.
- **Keep Texture** (on by default) gives each vertex that moves the UV of the place it moved to, so the texture stays where it is on the body. A vertex on a UV seam has no single UV to take and is left where it is. With it off the texture moves with the vertices. It is not used while a blendshape is loaded.

Open edges never move, and that includes the edges a slot shares with its neighbors (the body's UDIM borders), so seams between slots stay closed. **Effect %** sets how far one stroke goes; brush again, or turn on **Constant Application**, for more. The mask, **Connected Only**, **Front Faces Only** and **X Symmetry** apply as for Smooth.

With **Keep Texture** on, the changed UVs are saved with the sculpt, as [UV Sculpt](#uv-sculpt) describes.

### Grab

Grab freezes the affected area when you press the mouse button, then moves it parallel to the Scene camera's view plane as you drag. Radius determines the area captured at mouse-down; Effect % determines how closely its center follows the pointer; falloff controls the transition to the untouched surface.

Use Grab for silhouette changes, repositioning a feature, or quickly pulling cloth away from an intersection. Choose the camera angle before starting the stroke because the working plane and target slot stay fixed until mouse-up.

### Crease

Crease combines two movements: vertices converge across the surface toward the stroke, then move along the surface normal. Its extra controls are:

- **Pinch** controls how strongly vertices converge toward the stroke.
- **Depth** cuts an indentation with negative values and raises a ridge with positive values.

Use a small radius and low Effect % for seams, folds, and narrow ridges. A high Pinch or abrupt falloff can collapse nearby vertices into a sharp line.

### Pinch

Pinch draws vertices together across the surface without deliberately adding or removing depth. The **Pinch** control sets the convergence strength. It is useful for tightening an existing fold, sharpening a soft feature, or narrowing a broad ridge before smoothing around it.

### Plane

Plane captures a working plane from the surface point and normal at the start of each stroke. It then moves affected vertices toward that fixed plane. **Plane Mode** controls which side is affected:

- **Flatten** moves vertices from both sides toward the plane.
- **Fill** raises only vertices recessed below the plane.
- **Scrape** lowers only vertices protruding above the plane.

Set the camera so you can place the first point accurately. Use Flatten for broad planar areas, Fill for shallow cavities, and Scrape for high spots.

### Boundary

Boundary finds the nearest exposed mesh edge—such as a hem, cuff, collar, sleeve, or skirt opening—and propagates the deformation inward across connected topology. Start within 1.5 × Radius of the edge. Radius controls how far the change travels over the surface.

Boundary is an anchored drag tool with these modes:

- **Grab** moves the edge with the pointer in the camera view plane.
- **Bend** rotates the affected edge region.
- **Expand** widens or narrows the opening.
- **Inflate** pushes the edge region outward or inward along the captured surface normal.
- **Twist** rotates the region around the captured surface normal.
- **Smooth** relaxes the edge toward its connected neighbors; drag farther for a stronger blend.

The panel warns when the target slot has no open boundary. If a stroke will not start, move closer to an exposed edge, increase Radius, or check whether **Connected Only** excludes that edge.

### Elastic

Elastic Deform makes broad, smoothly distributed changes from an anchored drag. It is intended for large organic or cloth-like adjustments where a rigid Grab would create an obvious stretched transition.

- **Grab** follows the pointer like a soft grab. **Preserve Volume** adds a small perpendicular bulge through the transition region to reduce visible volume loss.
- **Scale** expands or contracts the captured region. The drag direction along the surface tangent determines the sign and amount.
- **Twist** rotates the captured region around the surface normal. The tangential drag direction determines the rotation.

For Scale and Twist, begin with a large Radius, low Effect %, and a smooth falloff. The camera angle and the tangent captured at mouse-down affect how the drag is interpreted.

### Radius

**Radius (mm)** is the brush radius on the model, in millimeters, from 1 to 250 mm. It is not measured in screen pixels, so zooming the Scene camera does not change the physical area affected; the ring looks larger on screen as you zoom in.

- Use a large radius for broad proportional changes.
- Use a small radius for folds, edges, and cleanup.
- If the ring is difficult to see, check the model's scale and adjust Radius accordingly.

Only vertices whose current positions are inside the radius are candidates for the stroke. Falloff, strength, and masking then determine how much each candidate moves.

For Boundary, Radius is also the maximum surface distance over which the edge deformation propagates. For AutoSculpt, Radius is the maximum distance searched between a target vertex and the occluder.

### Effect Percentage

**Effect %** controls the maximum influence a vertex can receive during one mouse-down stroke. At 0%, a stroke has no effect. At 100%, the maximum displacement is one brush radius at the center of Add or Remove, subject to falloff and masking.

A vertex cannot keep accumulating displacement simply because the mouse is held still over it. UMA tracks the maximum influence received by each vertex for the current stroke. Moving closer to a vertex may increase it up to the stronger applicable limit, but repeatedly sampling the same point cannot exceed that limit. Release the mouse to reset the limits and begin another pass.

Grab, Boundary, Elastic, and UV Sculpt are anchored drag tools rather than sampled paint strokes. For them, Effect % scales the captured weights and therefore how strongly the region follows the drag.

For controlled work, prefer several strokes at a low percentage. This makes it easier to undo a single pass and avoids abrupt changes.

## Falloff

Falloff controls how influence changes from the brush center to its edge. The center is distance 0 and the outer ring is distance 1.

- **Constant** applies full influence throughout the radius and stops abruptly at the edge. It is useful for uniformly moving a region but can create a visible boundary.
- **Linear** decreases influence evenly from full strength at the center to zero at the edge.
- **Smooth** uses a smooth S-shaped transition. This is the recommended general-purpose default.
- **Ease In** stays stronger across more of the brush before tapering near the edge.
- **Ease Out** concentrates influence closer to the center and falls away more quickly.
- **Ease In Out** creates a balanced smooth transition with softened behavior near both ends.
- **Sharp** concentrates the effect strongly at the center for more focused shaping.
- **User Defined** evaluates the displayed `AnimationCurve` across the normalized radius.

For a user-defined curve, the horizontal axis runs from center (`0`) to edge (`1`), and the vertical axis is influence from none (`0`) to full (`1`). Curve results are clamped to this influence range. A conventional sculpt falloff starts near `(0, 1)` and ends near `(1, 0)`. Raising the right side makes the edge remain active; lowering the left side weakens the center.

The Brush section also holds the tool-specific options (**Pinch**, **Depth**, **Plane Mode**, **Boundary Mode**, **Elastic Mode**, **Preserve Volume**) and the three toggles below.

## Connected Only

Enable **Connected Only** to restrict a stroke to the connected surface component beneath the brush center. This prevents the brush from changing a nearby but disconnected layer in the same slot—for example, the back of a pocket or the opposite side of thin cloth that also falls inside the 3D radius.

Disable it when you intentionally want the brush to affect multiple disconnected pieces within the target slot.

## Front Faces Only

Enable **Front Faces Only** to change only vertices whose normal faces the brush. A stroke on the outside of a garment with two separate layers (an outer shell and a lining a few millimetres in) then leaves the lining alone, and the far side of a thin part inside the radius is not pushed. It applies to the brushes that work under the pointer and to Grab and Elastic; the mask brushes and Boundary ignore it.

Double-sided geometry is different: its two sides share their vertex positions, and vertices at one position always move together so the surface does not split. Front Faces Only does not separate them, and does not need to.

## X Symmetry

Enable **X Symmetry** to apply a corresponding stroke across the character's local X axis. UMA reflects the brush position and surface direction, so the mirrored sample affects vertices inside the reflected brush rather than requiring identical vertex indices.

Symmetry works best when the slot was authored symmetrically around local X = 0. Centerline samples are not applied twice.

Inspect asymmetric clothing, offset accessories, and deliberately asymmetric topology carefully before relying on symmetry. Disable it when sculpting details intended for only one side.

## Live Normal Updates

Enable **Live Normal Updates** to bring the preview mesh normals up to date after every brush sample. This lets the 3D brush orientation and original-material lighting respond immediately to the changing surface during a stroke.

The option is disabled by default because repeated normal calculation costs additional CPU time, especially on dense meshes and fast strokes. When disabled, normals are still brought up to date automatically when the mouse button is released and the stroke ends.

## Constant Application

Normally one stroke moves each vertex at most once to the brush's full effect, however long the pointer rests on it or however often it passes over. That keeps a stroke even, and to do more you release and stroke again.

Enable **Constant Application** to keep applying the brush while the mouse button is held, also when the pointer does not move. **Every (ms)** sets how often the brush is applied again; the default is 100 ms, and a smaller value builds up faster. Each application starts a new dab at the pointer, so holding still keeps pushing, smoothing or flattening the area, and a slow drag does more than a fast one.

- It applies to the sculpt brushes that work under the pointer (Add, Remove, Smooth, Beautify, Crease, Pinch, Plane) and to the mask brushes. Grab, Boundary, Elastic and UV Sculpt are anchored drags and are not affected.
- The whole press is still one stroke: one Undo step puts everything it did back.
- With the option off the brushes behave exactly as described above.
- The setting and its interval are remembered between sessions.

Start with a low **Effect %** when using it; the effect of one application is repeated ten times a second at the default interval.

## Normals

The **Normals** section, and the **Comb Normals** and **Smooth Normals** brushes in the last group on the toolbar, change vertex normals without moving any vertex. Use them to fix shading after sculpting, or the dark or light patches left by a poke-through fix.

- **Show Normals** draws the editable slots' normals as short lines, **Length (mm)** long. Normals changed by a normals tool are orange. On large selections only the normals near the brush are drawn.
- **Recalculate** works out the editable slots' normals again from their current shape, by the [battle buddy rule](#battle-buddies) below. One Undo step.
- **Reset** puts the editable slots' normals back to the slot's own. One Undo step.
- **Comb Normals** (brush): drag across the surface and the normals under the brush tilt toward the direction of the stroke, at most 80 degrees in one stroke. **Effect %**, **Radius** and **Falloff** work as for the other brushes; the mask, **Connected Only** and **X Symmetry** apply.
- **Smooth Normals** (brush): the normals under the brush are evened out with their neighbors, across UV seams.

The normals brushes change only normals that face the brush, so the reversed side of double-sided geometry is left as it is. Tangents are kept perpendicular to the changed normals, so normal maps still shade correctly.

When a shape stroke ends (and during the stroke with **Live Normal Updates**), the normals around the vertices that moved turn by as much as the faces there turned. Every other normal is left exactly as it was, and normals you combed or smoothed, or that came with the slot, keep their character and turn with the surface. The same holds for Heal, AutoSculpt and loading a blendshape to edit.

### Battle buddies

Vertices at the same position are battle buddies: the copies a mesh has along the edge of every UV island, and the copies where two slots connect. They are one point of the surface, so whenever the editor works out normals it treats them as a single vertex and gives them one normal. This holds for **Recalculate**, for shape strokes, and for **Recalculate Normals** in the Mesh Modifier editor and on a Slot Data Asset. A different normal on each copy shows as a line in the shading along the seam.

- A buddy in a slot that is not editable keeps its normal, and its buddies in editable slots take that normal. The connection then still has one normal, and what the editor shows is what the saved slots give. To recalculate a connection from the shape, make the slots on both sides editable.
- A shape stroke that reaches buddies whose normals differ gives them one normal.
- The one exception is geometry that is double sided by having a reversed copy at the same positions. Its two sides face opposite ways and cannot share a normal, so each side keeps its own.

A Slot Data Asset is one slot, so its own **Recalculate Normals and Tangents** can only join the buddies inside that slot. Where the slot connects to another slot, the normals come from that slot's faces alone; recalculate connections in the Vertex Editor with both slots editable.

Changed normals are saved with the sculpt: **Save as Mesh Modifier** adds a **Vertex Normal** modifier for each slot whose normals you changed, and **Save to Base Slot** and **Save as New Slot** write them into the slot's MeshData. A slot whose only change is its normals can be saved too.

## UV Sculpt

**UV Sculpt**, the last brush on the toolbar, drags the texture across the surface. No vertex moves; the UVs of the vertices under the brush change. Use it to put a painted feature where the shape is: a knee, an elbow crease, a nipple or a navel that sits a little off on a body texture shared between meshes.

Choosing the brush turns on **Original Materials**, because the editor's pastel materials have no texture to see.

1. Set **Radius** generously. The texture cannot be carried out of the brush, so the radius should be well over the distance you want to move it; about three times is comfortable.
2. Set **Effect %**. At 100 the texture under the pointer stays under the pointer. Lower values make it follow part of the way, for small corrections.
3. Press on the feature and drag it to where it belongs. The texture under the middle of the brush follows the pointer and fades out to nothing at the brush's edge by the **Falloff**. Release to finish; the whole drag is one Undo step.

It is an anchored drag like Grab: the brush stays where you pressed, and the drag is measured on the Scene camera's view plane, so look at the surface roughly face on. On a surface that leans away from the camera the texture still stays under the pointer.

- The texture stops where a triangle would fold over itself in the texture, and a drag longer than the radius bunches it up against the brush's edge. The panel says so when that happens: undo, and use a larger radius.
- **Lock Borders** leaves the UVs on the edges of UV islands (the UV seams) and on open edges as they are. The islands then keep their outline in the texture, and the texture next to a seam is squeezed or stretched instead of moving across it. With it off, the islands either side of a seam each move by their own layout, which keeps a texture that is continuous across the seam continuous.
- **Reset UVs** puts the editable slots' UVs back to what they were when the editor opened, or were last saved into the slot. One Undo step.
- The mask, **Connected Only**, **Front Faces Only** and **X Symmetry** apply. With several editable slots, one drag moves the texture on every editable slot inside the brush, so it does not stop at the seam between two slots.
- Copies of a vertex that share a UV (hard edges) keep sharing it, so no new seam appears.
- Tangents are turned with the texture when the stroke ends, so normal maps still shade the right way.

Moving a UV moves every texture the slot uses, on every overlay, together. It does not change the texture files, and other meshes that use the same textures are not affected.

Changed UVs are saved with the sculpt: **Save as Mesh Modifier** adds a **Vertex UV** modifier for each slot whose UVs you changed (and a Vertex Normal modifier that carries the turned tangents), and **Save to Base Slot** and **Save as New Slot** write the UVs and tangents into the slot's MeshData. A blendshape holds movement only, so **Save as Blendshape** does not keep UV changes, and UV Sculpt is not available while a blendshape is loaded with **Edit Blendshape**. To fix where a feature sits on a base body for every character, solo the slot and use **Save to Base Slot**.

## Sculpt Masks

A sculpt mask protects vertices from every sculpt tool and AutoSculpt. Masks are editing aids and are not included in the saved runtime Mesh Modifier.

The mask brushes are on the floating toolbar:

- With neither mask brush active, the sculpt brushes use the current mask. Fully masked vertices do not move; partially masked vertices receive proportionally less influence.
- **Mask Paint** increases mask strength under the brush.
- **Mask Erase** decreases mask strength under the brush.

Click an active mask brush again, or pick a sculpt brush, to return to sculpting. Mask painting uses the same Radius, Effect %, falloff, stroke limits, and optional X symmetry as sculpting.

The **Mask** section of the panel has:

- **Show** to display the mask as a colored wash, and **Mask Color** to set its color and opacity.
- **Clear** to remove the mask from every slot.
- **Invert** to change every mask weight on the editable slots to its complement: protected areas become editable and editable areas become protected.

A useful workflow for changing a small feature while protecting everything else is:

1. Clear the mask.
2. Paint the feature that should remain fixed, or paint the area you want to edit and then invert the mask.
3. Pick a sculpt brush on the toolbar.
4. Sculpt the unmasked region.
5. Smooth the transition with a soft falloff and low Effect %.

## AutoSculpt

AutoSculpt automatically moves target geometry away from an unchanged source or occluder. It is useful for pulling a body inward under clothing, creating clearance between layered garments, or correcting many small intersections before manual cleanup.

For each target vertex, UMA casts outward in the positive or negative direction of the selected local model axis. If that ray reaches the occluder within the current Radius, the vertex moves in the opposite direction, toward the model center and away from the occluder. This is an axis projection, not a full physics or collision simulation.

### How to use AutoSculpt

1. Generate the avatar with the target and occluder slots shown, open Sculpt mode, and expand **Autosculpt**.
2. In **Source / Occluder**, select the slot that must remain unchanged. For example, choose a shirt to move the body or an under-layer away from it. The occluder only needs to be shown; lock it in the Slots panel to protect it from the brushes.
3. Choose the targets in the **Slots** panel. Solo one slot for a focused operation. With several editable slots, **Autosculpt Current Slot** uses the current target named in the Sculpt panel, while **Autosculpt Editable Slots** processes every editable slot except the occluder.
4. Choose the **Axis** that best passes from the model center through the overlap: **X** for left/right projection, **Y** for up/down, or **Z** for front/back. **Y** is the default, but the best choice depends on the garment and intersection.
5. Set **Radius** slightly larger than the clearance or overlap AutoSculpt must find. Vertices whose rays do not reach the occluder within this distance are unchanged.
6. Set **Effect %** for the maximum pull. The maximum displacement is Radius multiplied by Effect %. Start low and run additional passes if necessary.
7. Choose a **Falloff**. It is evaluated from the target-to-occluder distance: vertices close to the occluder receive more influence with the normal decreasing falloffs, while hits near the edge of Radius receive less.
8. Optionally paint a mask to protect target areas. AutoSculpt honors each target slot's mask. **X Symmetry** also makes the mirrored occluder surface available to the projection.
9. Optionally enable **Clothify** and set **Cloth Effect**. Clothify adds deterministic variation to the pull of vertices AutoSculpt already found; it does not move otherwise unaffected vertices or simulate cloth.
10. Select **Autosculpt Current Slot** to change only the current slot, or **Autosculpt Editable Slots** to change every editable slot except the occluder.
11. Read the result message, inspect from several angles, and use Undo if needed. Continue with manual brushes, then save through the normal Mesh Modifier or direct single-slot mesh workflow.

**Autosculpt Current Slot** is disabled when there is no current target or when the current slot is also the occluder. In the latter case, make a different slot editable or use **Autosculpt Editable Slots**.

One AutoSculpt run is one Unity Undo operation. Normals, bounds, and the picking collider are refreshed after it finishes.

## Strokes, Sampling, and Undo

UMA samples points between drag events based on a fraction of the brush radius. This fills gaps when the pointer moves quickly, although slow, deliberate strokes still produce the most predictable result.

One complete mouse-down to mouse-up gesture is registered as one Unity Undo operation. Use **Edit > Undo** or the normal Undo shortcut to restore the state before that stroke. Redo reapplies it. Grab, Boundary, Elastic, and UV Sculpt freeze their affected area, working plane, and target slot at mouse-down. Normals, bounds, and the picking collider are refreshed when a stroke finishes so the next stroke follows the updated surface.

Undo steps back one stroke at a time and leaves earlier strokes, and a blendshape loaded for editing, in place. The character is rebuilt after an Undo or Redo only when that step changed what it is built from: a Mesh Modifier, a vertex adjustment, or a **Rebuild Now**, **Rebuild to T-Pose** or **Reset Build**. A rebuild replaces the sculpted mesh, so undoing one of those steps drops strokes that were not saved.

Ending or interrupting an interaction safely resets per-stroke tracking. This includes mouse-up, leaving the Scene view, changing tool modes, changing slots, or closing the editing stage.

## Save Your Work

Every way of saving is in the **Save** panel at the top of the **Vertex Editor** window; the Scene view panel has no save buttons. The panel starts with what is not saved yet, then lists each way of saving with what it writes. A button that cannot be used now is disabled, with the reason under it (for example, that one slot must be soloed). After a save the panel says what was written, with **Show** to find the asset in the Project window.

| Save | What it writes | Changes the project's slots? |
| --- | --- | --- |
| **Save as Mesh Modifier** | One MeshModifier asset with everything from the session: sculpt, changed normals and UVs, vertex colors, Advanced adjustments and vertex-set modifiers. | No |
| **Save to '\<name\>'** | The strokes, into a blendshape loaded with **Edit Blendshape**. Shown only while one is loaded. | Yes, the blendshape |
| **Save to Base Slot** | The soloed slot's sculpt (positions, normals and UVs) into its SlotDataAsset. | Yes |
| **Save as New Slot** | A new SlotDataAsset: a copy of the soloed slot with the sculpt. | No, adds one |
| **Save as Blendshape** | The sculpt as a 100% blendshape on every slot it changed. Movement only: UV changes are not in it. | Yes, a blendshape |
| **Save Vertex Colors to Slot** | The soloed slot's painted colors into its SlotDataAsset. | Yes |
| **Save Vertex Selection** | The selected vertices to a JSON file (load it again in Advanced). | No |

Saving into a slot asset does not close the editor. What was written becomes the new starting point, so a later save adds only what you sculpt after it, and nothing is applied twice.

### Leaving with unsaved work

When you close the Vertex Editor (its **Close** button, or the Vertex Editor window) with unsaved work, a **Leave the Vertex Editor** dialog says what is not saved and offers the ways of saving that fit it, each with a line on what it does. Choose one; the dialog comes back with whatever is still unsaved, until everything is saved or you choose **Leave Without Saving** (or **Stay**, from the Close button). Closing because scripts recompile or Play mode starts cannot ask, and drops unsaved work as before.

### Save as Mesh Modifier

1. Finish the current stroke by releasing the mouse.
2. Inspect the result from several angles and use Undo or Smooth where needed.
3. Set **Name** in the Save panel. It is the suggested file name and the name of the sculpt modifiers. The default is `<SlotName> Sculpt` with one editable slot, or `Multi-Slot Sculpt` with several. With painted colors, **Paint Name** names their modifier.
4. Select **Save as Mesh Modifier...** and choose a location under the project's `Assets` folder.

The selected save folder is remembered for the project. Saving to an existing path replaces the Mesh Modifier asset at that path.

The saved asset contains one modifier stack for each changed slot. Each stack uses a `VertexDeltaAdjustmentCollection` containing only that slot's nonzero vertex deltas. A slot whose normals or UVs were changed also gets a `VertexNormalAdjustmentCollection` or a `VertexUVAdjustmentCollection` with the changed vertices. This means a multi-slot or **Autosculpt Editable Slots** result can be kept together in one Mesh Modifier asset. It does not include:

- Selection state.
- The sculpt mask.
- Brush radius, strength, falloff, or symmetry settings.
- Unchanged vertices.

Saving does not overwrite any source slot mesh. Keep the asset in the project and assign it through the normal UMA recipe, DNA, or Mesh Modifier workflow appropriate to your character.

### Save as Blendshape

**Save as Blendshape** stores the current sculpt deltas as a blendshape directly on every changed slot's source `SlotDataAsset`. This preserves the base vertex positions and creates one frame at 100% weight. The frame contains the sculpt session's vertex deltas, with normal and tangent deltas worked out from how the shape bends the slot.

The deltas are stored in the slot's own mesh space. The stage sculpts the character as it stands, skinned and with its DNA, so each vertex's movement is taken back through the character's skinning before it is stored; dialling the saved shape in on a rebuilt character then puts the vertices where they were sculpted. The shape is written to the slot's asset in the project: the base slot when the character wears a copy the race baked blendshapes into, and the original slot when it wears a UMA 2 slot through [legacy rig conversion](SlotRigConversion.md#automatic-conversion).

1. Finish the current stroke and inspect the result.
2. Enter a shared **Blendshape Name**. UMA uses this name on every slot changed during the sculpt session.
3. Select **Save as Blendshape**.
4. If that name already exists on any target slot, choose **Replace** to replace its existing blendshape data or **Cancel** to leave every target unchanged.
5. Regenerate the avatar before testing the saved blendshape.

The operation supports one or several editable slots and **Autosculpt Editable Slots** results. Only slots with nonzero sculpt deltas are changed. Saving is registered as one Unity Undo operation and writes only the affected `SlotDataAsset` assets.

Two changed slot instances cannot save different results to the same source `SlotDataAsset`. UMA stops the entire operation in that ambiguous case; give the instances independent slot assets and save again.

The blendshape stores only deformation introduced during the current sculpt session. Generated DNA and previously applied Mesh Modifiers are not folded into its deltas.

## Edit an Existing Blendshape

**Edit Blendshape**, in the Sculpt panel, loads a blendshape the character's slots already have, so the brushes change that shape. Use it to finish by hand what [Blendshape Transfer](BlendshapeTransfer.md) made: pull a collar back out, flatten a fold, give a cuff more room.

1. Choose the **Blendshape**. The list holds every blendshape the character's slots have in the project.
2. Select **Edit '\<name\>'**. Every slot that has the shape, the body included, is shown with it fully dialled in. Finish or discard any other sculpt first; the button is disabled while there are unsaved strokes.
3. Make the slots to change editable in the **Slots** panel and sculpt as usual. Lock the body to sculpt clothing against it. Masks, X Symmetry and AutoSculpt work as they do for any sculpt.
4. Select **Save to '\<name\>'** in the Save panel of the Vertex Editor window. The strokes made since loading are added to the blendshape on every slot they touched. A slot that did not have the shape gets it.
5. Keep sculpting and save again, or select **Stop Editing** to show the character at rest. Strokes that were not saved are dropped, after a confirmation.

What is saved:

- The strokes are added to the shape's last frame in full, and to earlier frames of a multi-frame shape in proportion to their weight, so the shape still grows evenly.
- Normal and tangent deltas are worked out again for the whole shape.
- Slots worn through legacy rig conversion are edited like any other; their strokes are written to the original UMA 2 slot.
- Saving is one Unity Undo operation and writes the affected `SlotDataAsset` assets immediately. Regenerate characters to load the changed shape.

While a blendshape is loaded, **Save as Mesh Modifier** and **Save as Blendshape** under another name store only the strokes made since loading. Loading or saving a blendshape clears the stroke Undo history, because earlier strokes belong to another starting shape. Rebuilding the character ends the edit; load the blendshape again to continue.

The Blendshape Transfer dialog opens this mode directly with **Sculpt '\<shape\>'**: on a temporary character wearing the dialog's target slots, with only those slots editable. A shape the dialog has only previewed is loaded too; the panel says how many slots show a shape that is not on them yet, and **Save to '\<shape\>'** writes it with the strokes. The temporary character goes when the editor closes.

## Save the Sculpt into a Slot

**Save to Base Slot** and **Save as New Slot**, in the Save panel, bake the current slot's sculpted vertex, normal and UV changes directly into slot `MeshData`. They need exactly one editable slot; solo the slot you intend to save first. The operations apply only the delta created during the sculpt session, so unrelated generated DNA and Mesh Modifier deformation are not baked into the source mesh.

As for a blendshape, the sculpt is taken back through the character's skinning before it is stored, so the saved mesh, worn by a rebuilt character, sits where it was sculpted. Both are disabled while a blendshape is loaded with **Edit Blendshape**, when strokes are changes to that shape and not to the slot's rest mesh.

### Overwrite the base slot

Select **Save to Base Slot** to replace the `MeshData` of the slot's asset in the project. That is the selected slot's own asset, with two exceptions: when the character wears a copy the race baked blendshapes into, the sculpt goes into the base slot behind it, and when it wears a UMA 2 slot through legacy rig conversion, into the original slot. UMA names the asset in a warning before changing it.

Choose **Cancel** to return to sculpting without changing the asset. Choose **Overwrite MeshData** to record the asset for Unity Undo, write a deep-copied mesh containing the sculpt deltas, and save only that asset. The editor stays open: keep sculpting, or save other work, and the character shows the new slot when it is rebuilt after you leave.

Saving a slot also releases it from the Global Library and drops the slot's race-baked copies. A race with prebaked blendshapes wears a copy of each base slot with those shapes baked in, made the first time it is needed and reused after that; the next character build makes a new copy from the slot as saved. This holds for every save in the editor that writes a slot: the sculpt, a blendshape, vertex colors and weights.

This is a destructive content-authoring operation. Commit or back up the original slot before overwriting it if you may need to recover the source shape later.

### Create a new slot

Use **New Slot Name** to name a derived slot. Its default is `<SlotName>_modified`. Select **Save as New Slot...**, choose an asset location, and UMA creates a new `SlotDataAsset`. The editor stays open.

The new asset receives:

- A deep copy of the sculpted `UMAMeshData`: the slot as the character wears it, with the sculpt. From a UMA 2 slot worn through legacy rig conversion that is the converted mesh, so the new slot is native to the character's race and is not converted again.
- The new slot and mesh-data name.
- Copies of the original slot's materials, renderer settings, tags, race restrictions, LOD settings, animated-bone settings, slot DNA, and other serialized configuration.
- A new independent source-slot identity matching the new name.

If an asset already exists at the chosen path, UMA generates a unique path rather than overwriting it. The new slot is also registered with the UMA Asset Indexer when one is available.

## Recommended Workflow

For clean, production-friendly results:

1. Start with the broadest form using Add, Remove, Grab, or Elastic, a large radius, Smooth falloff, and low Effect %.
2. Rotate the camera frequently. A change that looks correct from the front can distort the side silhouette.
3. Reduce Radius as the form becomes more detailed.
4. Use masks to protect borders, seams, or finished regions.
5. Use Smooth lightly between shaping passes rather than only at the end.
6. Verify symmetry before committing to many mirrored strokes.
7. Save meaningful milestones as separate Mesh Modifier assets when experimenting.
8. Apply the saved modifier to a generated avatar and inspect it with animation, different DNA values, and relevant wardrobe combinations.
9. For clothing intersections, use AutoSculpt for the first clearance pass, then inspect along the projection axis and finish with Grab, Smooth, or a mask-protected Plane stroke.

## Troubleshooting

### The brush does not appear

- Confirm **Sculpt** is selected.
- Confirm the desired slot has **Edit** ticked in the **Slots** panel.
- Make sure the slot is shown and contains editable mesh data.
- Move the pointer off the panels and floating toolbar and onto the editable mesh.
- Release Alt after navigating the Scene view.
- If another slot covers the editable surface, hide that obstructing slot or make it editable instead.

### The brush appears on one slot but not another

This is intentional: the raycast must hit an editable slot. Make the slot editable in the **Slots** panel, or temporarily hide overlapping geometry to expose the target.

### Nothing moves

- Make sure **Mask Paint** or **Mask Erase** is not active on the toolbar.
- Increase **Effect %** above zero.
- Clear or erase the mask if the region is protected.
- Increase Radius if it contains too few vertices.
- Release the mouse and start a new stroke if the vertices have already reached their current per-stroke limit.

### A stroke is too strong or creates a ridge

Undo the stroke, lower Effect %, and choose Smooth, Linear, or Ease Out falloff. A Constant falloff has an abrupt edge and is normally unsuitable for subtle organic shaping.

### Smooth changes a seam or border unexpectedly

Topology boundaries have fewer neighbors. Use a smaller brush and lower strength, or paint a mask over the seam before smoothing. Coincident split vertices move together, but a true open border still has fewer surrounding triangles than an interior vertex.

### Symmetry misses vertices

The slot may not be geometrically symmetric around local X = 0, or the mirrored vertex may be outside the matching tolerance. Disable symmetry and sculpt the other side manually.

### A save button is disabled

The reason is under the button. For the sculpt saves it is usually that no vertex differs from the session's starting shape yet (perform a stroke with nonzero Effect %, and check the region is not fully masked), or that the save needs one soloed slot.

### A Boundary stroke will not start

- Confirm the target slot has an exposed open edge.
- Start closer to the hem, cuff, collar, or other opening; the nearest valid boundary must be close to the brush.
- Increase Radius so the edge is within range.
- With several editable slots, remember that a co-located edge joined to another editable slot is treated as a welded seam rather than an exposed boundary.

### AutoSculpt moves no vertices

- Select a valid, shown **Source / Occluder**.
- For **Autosculpt Current Slot**, make sure the current target is not the occluder.
- Increase Radius so outward rays can reach the occluder.
- Try the axis that actually crosses the overlap; AutoSculpt tests only the selected local X, Y, or Z direction.
- Increase Effect % above zero and check whether the target area is masked.
- Confirm the occluder has visible triangles and surrounds the target on the relevant side of the model.

### The saved modifier looks different on another character

Mesh Modifiers address source-slot vertex indices. Confirm that the other character uses the same compatible source slot and topology. Also test skinning, pose, DNA, and modifier scaling, since the final generated shape can be influenced by all of them.

## Technical Notes

- Add and Remove use the weighted average normal of the target slot's vertices covered by the brush.
- Smooth uses directly connected triangle neighbors from the target slot only, while coincident split vertices share one averaged target and displacement.
- Grab, Boundary, and Elastic use an anchored camera-plane drag and do not resample the affected region after mouse-down.
- Plane uses the first sample's point and surface normal for the complete stroke.
- Connected Only uses the topology component beneath the brush center, not spatial proximity alone.
- AutoSculpt uses axis-aligned ray/triangle tests against the chosen occluder; Radius is its search distance and Radius × Effect % is its maximum pull.
- With several editable slots, recognized co-located seam vertices are synchronized across them, and every changed slot is saved as its own modifier stack.
- The preview mesh collider is refreshed during sculpting so later samples follow the changed surface.
- The normals around moved vertices, and the bounds, are brought up to date when the stroke ends.
- The modifier is stored using `VertexDeltaAdjustmentCollection`; no sculpt-specific runtime data format is required.
- The source mesh topology must remain compatible with the saved vertex indices.
