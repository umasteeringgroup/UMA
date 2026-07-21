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
2. In the avatar Inspector, find **Utilities > Mesh Modifier**.
3. Select **Create New Mesh Modifier**.
4. Unity opens the dedicated Mesh Modifier editing stage and the Mesh Modifiers window.
5. The editor opens in **Sculpt** mode by default. You can return to it at any time from the authoring workflow toolbar.

To inspect an existing Mesh Modifier, drag the asset onto the Mesh Modifier drop area in the avatar Inspector. **Save MeshModifier** can contain changes for one or several slots; it does not destructively edit their source meshes.

### Scene display

The vertex editor opens with its existing pastel materials and wireframe enabled. Two independent toggles below the mode toolbar let you change the preview without affecting saved data:

- **Original Materials** switches between the pastel editor materials and the materials captured from the generated UMA renderer.
- **Wireframe** shows or hides Unity's selected-mesh wireframe overlay.

You can therefore use pastel with or without wireframe, or inspect the original textured materials with or without wireframe. These options affect only the editing-stage display and remain active across preview mesh rebuilds.

## Quick Start

1. Select **Sculpt**.
2. Choose the slot you want to change from **Slot**.
3. Choose a sculpt tool. **Add**, **Remove**, and **Smooth** are the easiest tools to learn; **Grab**, **Crease**, **Pinch**, **Plane**, **Boundary**, and **Elastic** provide more specialized deformation.
4. Start with a modest **Radius**, an **Effect %** around 10–25, and **Smooth** falloff.
5. Move the pointer over the selected slot. A 3D ring appears on a valid surface.
6. Hold the left mouse button and drag across the surface.
7. Release the button to finish the stroke and recalculate the mesh normals.
8. Repeat with smaller brushes and lower strength for refinement.
9. Enter a descriptive **Modifier Name**, then select **Save MeshModifier**.

The Save button is disabled until at least one vertex differs from the original preview state.

## Reading the 3D Brush

The sculpt brush is drawn in world space rather than as a flat circle on the screen.

- The ring is tangent to the surface under the pointer.
- The short line through its center indicates the averaged surface normal and therefore the direction used by Add and Remove.
- The ring continually changes position and orientation as it crosses the mesh.
- The brush ring and normal indicator are drawn in thick, high-visibility red in every sculpt and mask mode.
- The brush ignores the Scene depth buffer, preventing nearby triangles or overlapping clothing from clipping portions of the indicator.
- Every masked vertex on the selected slot is shown persistently as a red square. Larger, more opaque squares indicate stronger mask weights.

The brush appears only when the pointer raycasts against the selected slot. If clothing overlaps a body slot, selecting the body does not allow a stroke to begin on the clothing. Hide or suppress an obstructing slot temporarily if you need access to a covered surface.

Hold **Alt** to use normal Scene navigation. Sculpting is suspended during Alt navigation and while the pointer is over the tools panel.

## Sculpt Controls

### Slot

The **Slot** menu contains **All Slots** followed by every visible editable slot. Choose a specific slot when you want the brush and raycast to ignore everything else. Choose **All Slots** when a session must cross garments or body parts: the slot under the pointer becomes the target for that stroke, and UMA retains changes made to every touched slot.

In **All Slots** mode, co-located boundary vertices are treated as welded across slots. Position, mask, and normal changes are synchronized at those seams, which helps keep connected pieces together. A Boundary stroke ignores a per-slot open edge when UMA recognizes it as a seam joined to another visible slot.

When Sculpt mode is opened for the first time, UMA defaults to **All Slots**. The panel reports the current target as the pointer moves over editable geometry.

Select the small target button beside **Slot** to calculate the selected slot's world-space bounds and frame them in the Scene view. With **All Slots** selected, it frames all editable slots. Framing does not lock the camera.

Changing the selected slot ends the current stroke but retains that slot's edits in the sculpt session. **Save MeshModifier** includes all changed slots, even if you later select a different slot. The commands that overwrite a base slot or create a new slot require one specific slot and are disabled while **All Slots** is selected.

The saved modifier uses the slot's source-slot key when one is available. This allows the modifier to target the correct UMA slot rather than an incidental generated instance name.

### Add

Add moves affected vertices outward along the averaged normal of the surface covered by the brush. Use it to build volume, round a silhouette, or push a garment away from the body.

On a sharply curved surface, the averaged normal produces a stable regional direction instead of moving every vertex along an unrelated screen-facing direction. Use a smaller radius near corners or thin structures when you need more local control.

### Remove

Remove moves affected vertices inward, opposite the averaged brush normal. Use it to reduce volume, create shallow depressions, or pull a garment closer to the body.

Remove is not a deletion tool. It changes vertex positions but does not remove triangles, vertices, UVs, skinning, or materials.

### Smooth

Smooth relaxes each affected vertex toward the average position of its directly connected one-ring neighbors. It operates on the selected slot's topology and does not average against vertices from other slots. Vertices at the same position are treated as one welded sculpt vertex, including splits created for UV islands, hard normals, or material boundaries.

Use Smooth to soften lumpy strokes, blend a sculpt into the surrounding mesh, or reduce small surface irregularities. Several light strokes generally preserve the intended silhouette better than one very strong stroke. Open borders and seams have fewer connected neighbors, so inspect them from multiple angles after smoothing.

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

The panel warns when the selected slot has no open boundary. If a stroke will not start, move closer to an exposed edge, increase Radius, or check whether **Connected Only** excludes that edge.

### Elastic

Elastic Deform makes broad, smoothly distributed changes from an anchored drag. It is intended for large organic or cloth-like adjustments where a rigid Grab would create an obvious stretched transition.

- **Grab** follows the pointer like a soft grab. **Preserve Volume** adds a small perpendicular bulge through the transition region to reduce visible volume loss.
- **Scale** expands or contracts the captured region. The drag direction along the surface tangent determines the sign and amount.
- **Twist** rotates the captured region around the surface normal. The tangential drag direction determines the rotation.

For Scale and Twist, begin with a large Radius, low Effect %, and a smooth falloff. The camera angle and the tangent captured at mouse-down affect how the drag is interpreted.

### Radius

**Radius** is the brush radius in world units. It is not measured in screen pixels, so zooming the Scene camera does not change the physical area affected.

- Use a large radius for broad proportional changes.
- Use a small radius for folds, edges, and cleanup.
- If the ring is difficult to see, check the model's scale and adjust Radius accordingly.

Only vertices whose current positions are inside the radius are candidates for the stroke. Falloff, strength, and masking then determine how much each candidate moves.

For Boundary, Radius is also the maximum surface distance over which the edge deformation propagates. For AutoSculpt, Radius is the maximum distance searched between a target vertex and the occluder.

### Effect Percentage

**Effect %** controls the maximum influence a vertex can receive during one mouse-down stroke. At 0%, a stroke has no effect. At 100%, the maximum displacement is one brush radius at the center of Add or Remove, subject to falloff and masking.

A vertex cannot keep accumulating displacement simply because the mouse is held still over it. UMA tracks the maximum influence received by each vertex for the current stroke. Moving closer to a vertex may increase it up to the stronger applicable limit, but repeatedly sampling the same point cannot exceed that limit. Release the mouse to reset the limits and begin another pass.

Grab, Boundary, and Elastic are anchored drag tools rather than sampled paint strokes. For them, Effect % scales the captured weights and therefore how strongly the region follows the drag.

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

## Connected Only

Enable **Connected Only** to restrict a stroke to the connected surface component beneath the brush center. This prevents the brush from changing a nearby but disconnected layer in the same slot—for example, the back of a pocket or the opposite side of thin cloth that also falls inside the 3D radius.

Disable it when you intentionally want the brush to affect multiple disconnected pieces within the selected slot.

## X Symmetry

Enable **X Symmetry** to apply a corresponding stroke across the character's local X axis. UMA reflects the brush position and surface direction, so the mirrored sample affects vertices inside the reflected brush rather than requiring identical vertex indices.

Symmetry works best when the slot was authored symmetrically around local X = 0. Centerline samples are not applied twice.

Inspect asymmetric clothing, offset accessories, and deliberately asymmetric topology carefully before relying on symmetry. Disable it when sculpting details intended for only one side.

## Live Normal Updates

Enable **Live Normal Updates** to recalculate the preview mesh normals after every brush sample. This lets the 3D brush orientation and original-material lighting respond immediately to the changing surface during a stroke.

The option is disabled by default because repeated normal calculation costs additional CPU time, especially on dense meshes and fast strokes. When disabled, normals are still recalculated automatically when the mouse button is released and the stroke ends.

## Sculpt Masks

A sculpt mask protects vertices from every sculpt tool and AutoSculpt. Masks are editing aids and are not included in the saved runtime Mesh Modifier.

The Mask toolbar has three states:

- **Off** uses the current mask while sculpting. Fully masked vertices do not move; partially masked vertices receive proportionally less influence.
- **Paint** increases mask strength under the brush.
- **Erase** decreases mask strength under the brush.

Mask painting uses the same Radius, Effect %, falloff, stroke limits, and optional X symmetry as sculpting.

Additional commands:

- **Clear Mask** removes all protection from the current sculpt slot.
- **Invert Mask** changes every mask weight to its complement: protected areas become editable and editable areas become protected.

A useful workflow for changing a small feature while protecting everything else is:

1. Clear the mask.
2. Paint the feature that should remain fixed, or paint the area you want to edit and then invert the mask.
3. Set Mask to Off.
4. Sculpt the unmasked region.
5. Smooth the transition with a soft falloff and low Effect %.

## AutoSculpt

AutoSculpt automatically moves target geometry away from an unchanged source or occluder. It is useful for pulling a body inward under clothing, creating clearance between layered garments, or correcting many small intersections before manual cleanup.

For each target vertex, UMA casts outward in the positive or negative direction of the selected local model axis. If that ray reaches the occluder within the current Radius, the vertex moves in the opposite direction, toward the model center and away from the occluder. This is an axis projection, not a full physics or collision simulation.

### How to use AutoSculpt

1. Generate the avatar with the target and occluder slots visible, open Sculpt mode, and expand **Autosculpt**.
2. In **Source / Occluder**, select the slot that must remain unchanged. For example, choose a shirt to move the body or an under-layer away from it.
3. Select the target from the main **Slot** menu. Choose a specific slot for a focused operation. With **All Slots** selected, **Autosculpt Current Slot** uses the current target reported in the Sculpt panel, while **Autosculpt All Slots** processes every visible editable slot except the occluder.
4. Choose the **Axis** that best passes from the model center through the overlap: **X** for left/right projection, **Y** for up/down, or **Z** for front/back. **Y** is the default, but the best choice depends on the garment and intersection.
5. Set **Radius** slightly larger than the clearance or overlap AutoSculpt must find. Vertices whose rays do not reach the occluder within this distance are unchanged.
6. Set **Effect %** for the maximum pull. The maximum displacement is Radius multiplied by Effect %. Start low and run additional passes if necessary.
7. Choose a **Falloff**. It is evaluated from the target-to-occluder distance: vertices close to the occluder receive more influence with the normal decreasing falloffs, while hits near the edge of Radius receive less.
8. Optionally paint a mask to protect target areas. AutoSculpt honors each target slot's mask. **X Symmetry** also makes the mirrored occluder surface available to the projection.
9. Optionally enable **Clothify** and set **Cloth Effect**. Clothify adds deterministic variation to the pull of vertices AutoSculpt already found; it does not move otherwise unaffected vertices or simulate cloth.
10. Select **Autosculpt Current Slot** to change only the current specific slot, or **Autosculpt All Slots** to change every visible editable slot except the occluder.
11. Read the result message, inspect from several angles, and use Undo if needed. Continue with manual brushes, then save through the normal Mesh Modifier or direct single-slot mesh workflow.

**Autosculpt Current Slot** is disabled when there is no current target or when the current slot is also the occluder. In the latter case, select another target or use **Autosculpt All Slots**.

One AutoSculpt run is one Unity Undo operation. Normals, bounds, and the picking collider are refreshed after it finishes.

## Strokes, Sampling, and Undo

UMA samples points between drag events based on a fraction of the brush radius. This fills gaps when the pointer moves quickly, although slow, deliberate strokes still produce the most predictable result.

One complete mouse-down to mouse-up gesture is registered as one Unity Undo operation. Use **Edit > Undo** or the normal Undo shortcut to restore the state before that stroke. Redo reapplies it. Grab, Boundary, and Elastic freeze their affected area, working plane, and target slot at mouse-down. Normals, bounds, and the picking collider are refreshed when a stroke finishes so the next stroke follows the updated surface.

Ending or interrupting an interaction safely resets per-stroke tracking. This includes mouse-up, leaving the Scene view, changing tool modes, changing slots, or closing the editing stage.

## Save the Sculpt as a Mesh Modifier

1. Finish the current stroke by releasing the mouse.
2. Inspect the result from several angles and use Undo or Smooth where needed.
3. Set **Modifier Name**. The default is `<SlotName> Sculpt` for a specific slot or `All Slots Sculpt` in **All Slots** mode.
4. Select **Save MeshModifier**.
5. Choose a location under the project's `Assets` folder.

The selected save folder is remembered for the project. Saving to an existing path replaces the Mesh Modifier asset at that path.

The saved asset contains one modifier stack for each changed slot. Each stack uses a `VertexDeltaAdjustmentCollection` containing only that slot's nonzero vertex deltas. This means an **All Slots** sculpt or **Autosculpt All Slots** result can be kept together in one Mesh Modifier asset. It does not include:

- Selection state.
- The sculpt mask.
- Brush radius, strength, falloff, or symmetry settings.
- Unchanged vertices.

Saving does not overwrite any source slot mesh. Keep the asset in the project and assign it through the normal UMA recipe, DNA, or Mesh Modifier workflow appropriate to your character.

## Save the Sculpt as a Blendshape

Sculpt mode can store the current sculpt deltas as a blendshape directly on every changed slot's source `SlotDataAsset`. This preserves the base vertex positions and creates one frame at 100% weight. The frame contains the sculpt session's vertex and normal deltas; tangent deltas are left empty.

1. Finish the current stroke and inspect the result.
2. Enter a shared **Blendshape Name**. UMA uses this name on every slot changed during the sculpt session.
3. Select **Save Sculpt as Blendshape**.
4. If that name already exists on any target slot, choose **Replace** to replace its existing blendshape data or **Cancel** to leave every target unchanged.
5. Regenerate the avatar before testing the saved blendshape.

The operation supports a specific slot, **All Slots**, and **Autosculpt All Slots**. Only slots with nonzero sculpt deltas are changed. Saving is registered as one Unity Undo operation and writes the affected `SlotDataAsset` assets immediately.

Two changed slot instances cannot save different results to the same source `SlotDataAsset`. UMA stops the entire operation in that ambiguous case; give the instances independent slot assets and save again.

The blendshape stores only deformation introduced during the current sculpt session. Generated DNA and previously applied Mesh Modifiers are not folded into its deltas.

## Save the Sculpt into a Slot

Sculpt mode also offers two ways to bake the current specific slot's sculpted vertex and normal changes directly into slot `MeshData`. These commands are disabled in **All Slots** mode. Select the slot you intend to save first. The operations apply only the delta created during the sculpt session, so unrelated generated DNA and Mesh Modifier deformation are not baked into the source mesh.

### Overwrite the base slot

Select **Save slot modifications to base slot** to replace the selected `SlotDataAsset`'s `MeshData`. UMA displays this warning before changing the asset:

> Warning, this will overwrite the MeshData on the slot with the new values!

Choose **Cancel** to return to sculpting without changing the asset. Choose **Overwrite MeshData** to record the asset for Unity Undo, write a deep-copied mesh containing the sculpt deltas, save the asset, and exit the Mesh Modifier editing stage.

This is a destructive content-authoring operation. Commit or back up the original slot before overwriting it if you may need to recover the source shape later.

### Create a new slot

Use **New Slot Name** to name a derived slot. Its default is `<SlotName>_modified`. Select **Save slot modifications to a new slot**, choose an asset location, and UMA creates a new `SlotDataAsset` before exiting the editor.

The new asset receives:

- A deep copy of the sculpted `UMAMeshData`.
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
- Confirm the desired slot is selected in **Slot**.
- Make sure the slot is visible, not suppressed, and contains editable mesh data.
- Move the pointer off the tools panel and onto the selected mesh.
- Release Alt after navigating the Scene view.
- If another slot covers the selected surface, hide that obstructing slot or choose it instead.

### The brush appears on one slot but not another

This is intentional when a specific slot is selected. The raycast must hit that slot. Select the correct slot, choose **All Slots**, or temporarily hide overlapping geometry to expose the target.

### Nothing moves

- Ensure Mask is set to **Off** rather than Paint or Erase.
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

### Save MeshModifier is disabled

No vertex currently differs from the sculpt session's original preview. Perform a sculpt stroke or AutoSculpt operation with nonzero Effect %, and ensure the region is not fully masked.

### A Boundary stroke will not start

- Confirm the selected slot has an exposed open edge.
- Start closer to the hem, cuff, collar, or other opening; the nearest valid boundary must be close to the brush.
- Increase Radius so the edge is within range.
- In **All Slots** mode, remember that a co-located edge joined to another visible slot is treated as a welded seam rather than an exposed boundary.

### AutoSculpt moves no vertices

- Select a valid visible **Source / Occluder**.
- For **Autosculpt Current Slot**, select or hover a target slot that is not the occluder.
- Increase Radius so outward rays can reach the occluder.
- Try the axis that actually crosses the overlap; AutoSculpt tests only the selected local X, Y, or Z direction.
- Increase Effect % above zero and check whether the target area is masked.
- Confirm the occluder has visible triangles and surrounds the target on the relevant side of the model.

### The saved modifier looks different on another character

Mesh Modifiers address source-slot vertex indices. Confirm that the other character uses the same compatible source slot and topology. Also test skinning, pose, DNA, and modifier scaling, since the final generated shape can be influenced by all of them.

## Technical Notes

- Add and Remove use the weighted average normal of selected-slot vertices covered by the brush.
- Smooth uses directly connected triangle neighbors from the selected slot only, while coincident split vertices share one averaged target and displacement.
- Grab, Boundary, and Elastic use an anchored camera-plane drag and do not resample the affected region after mouse-down.
- Plane uses the first sample's point and surface normal for the complete stroke.
- Connected Only uses the topology component beneath the brush center, not spatial proximity alone.
- AutoSculpt uses axis-aligned ray/triangle tests against the chosen occluder; Radius is its search distance and Radius × Effect % is its maximum pull.
- **All Slots** synchronizes recognized co-located seam vertices and saves every changed slot as its own modifier stack.
- The preview mesh collider is refreshed during sculpting so later samples follow the changed surface.
- Normals and bounds are recalculated when the stroke ends.
- The modifier is stored using `VertexDeltaAdjustmentCollection`; no sculpt-specific runtime data format is required.
- The source mesh topology must remain compatible with the saved vertex indices.
