# Sculpting Mesh Modifiers

UMA's Mesh Modifier sculpting tools let you reshape one currently generated slot directly in the Scene view and save the result as a standard `MeshModifier` asset. Sculpting changes the preview mesh only; it does not overwrite the source `SlotDataAsset`, imported model, or original mesh.

The saved result is a sparse collection of vertex deltas for one slot. Vertices that did not move are not stored. The resulting asset works through the normal UMA Mesh Modifier pipeline and can be assigned or driven in the same ways as other Mesh Modifiers.

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
5. In the Scene tools panel, select **Sculpt** from the **Select | Paint | Sculpt** toolbar.

To inspect an existing Mesh Modifier, drag the asset onto the Mesh Modifier drop area in the avatar Inspector. Sculpt mode still creates a single-slot sculpt result when you use **Save MeshModifier**; it does not destructively edit the slot's source mesh.

### Scene display

The vertex editor opens with its existing pastel materials and wireframe enabled. Two independent toggles below the mode toolbar let you change the preview without affecting saved data:

- **Original Materials** switches between the pastel editor materials and the materials captured from the generated UMA renderer.
- **Wireframe** shows or hides Unity's selected-mesh wireframe overlay.

You can therefore use pastel with or without wireframe, or inspect the original textured materials with or without wireframe. These options affect only the editing-stage display and remain active across preview mesh rebuilds.

## Quick Start

1. Select **Sculpt**.
2. Choose the slot you want to change from **Slot**.
3. Choose **Add** (`+`), **Remove** (`-`), or **Smooth** (`~`). Hover a button to see its tooltip.
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

Sculpting is always limited to one visible slot. When Sculpt mode is opened for the first time, UMA defaults to the first visible slot that is not part of the active race's base recipe, which normally selects equipped clothing before the body. If every visible slot belongs to the base recipe, it falls back to the first visible slot. The brush ignores every other slot even when another mesh is inside the brush radius.

Select the small target button beside **Slot** to calculate that slot's world-space bounds and frame them in the Scene view. Framing does not lock the camera.

Changing slots starts a new sculpt session for the newly selected slot. If the current slot has unsaved changes, UMA asks whether to discard them. Choose **Keep Editing** to remain on the current slot, or save the Mesh Modifier before switching.

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

### Radius

**Radius** is the brush radius in world units. It is not measured in screen pixels, so zooming the Scene camera does not change the physical area affected.

- Use a large radius for broad proportional changes.
- Use a small radius for folds, edges, and cleanup.
- If the ring is difficult to see, check the model's scale and adjust Radius accordingly.

Only vertices whose current positions are inside the radius are candidates for the stroke. Falloff, strength, and masking then determine how much each candidate moves.

### Effect Percentage

**Effect %** controls the maximum influence a vertex can receive during one mouse-down stroke. At 0%, a stroke has no effect. At 100%, the maximum displacement is one brush radius at the center of Add or Remove, subject to falloff and masking.

A vertex cannot keep accumulating displacement simply because the mouse is held still over it. UMA tracks the maximum influence received by each vertex for the current stroke. Moving closer to a vertex may increase it up to the stronger applicable limit, but repeatedly sampling the same point cannot exceed that limit. Release the mouse to reset the limits and begin another pass.

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

## X Symmetry

Enable **X Symmetry** to apply a corresponding stroke across the slot's local X axis. UMA builds a mirror map by finding vertices near each reflected original position.

Symmetry works best when the slot was authored symmetrically around local X = 0. Vertices without a sufficiently close partner affect only themselves. Centerline vertices map to themselves and are not applied twice.

Inspect asymmetric clothing, offset accessories, and deliberately asymmetric topology carefully before relying on symmetry. Disable it when sculpting details intended for only one side.

## Update Normals While Sculpting

Enable **Update Normals While Sculpting** to recalculate the preview mesh normals after every brush sample. This lets the 3D brush orientation and original-material lighting respond immediately to the changing surface during a stroke.

The option is disabled by default because repeated normal calculation costs additional CPU time, especially on dense meshes and fast strokes. When disabled, normals are still recalculated automatically when the mouse button is released and the stroke ends.

## Sculpt Masks

A sculpt mask protects selected-slot vertices from Add, Remove, and Smooth. Masks are editing aids and are not included in the saved runtime Mesh Modifier.

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

## Strokes, Sampling, and Undo

UMA samples points between drag events based on a fraction of the brush radius. This fills gaps when the pointer moves quickly, although slow, deliberate strokes still produce the most predictable result.

One complete mouse-down to mouse-up gesture is registered as one Unity Undo operation. Use **Edit > Undo** or the normal Undo shortcut to restore the state before that stroke. Redo reapplies it. Normals, bounds, and the picking collider are refreshed when a stroke finishes so the next stroke follows the updated surface.

Ending or interrupting an interaction safely resets per-stroke tracking. This includes mouse-up, leaving the Scene view, changing tool modes, changing slots, or closing the editing stage.

## Save the Sculpt as a Mesh Modifier

1. Finish the current stroke by releasing the mouse.
2. Inspect the result from several angles and use Undo or Smooth where needed.
3. Set **Modifier Name**. The default is `<SlotName> Sculpt`.
4. Select **Save MeshModifier**.
5. Choose a location under the project's `Assets` folder.

The selected save folder is remembered for the project. Saving to an existing path replaces the Mesh Modifier asset at that path.

The saved asset contains exactly one modifier for the selected slot and a `VertexDeltaAdjustmentCollection` containing only nonzero vertex deltas. It does not include:

- Changes to other slots.
- Selection or Paint mode data.
- The sculpt mask.
- Brush radius, strength, falloff, or symmetry settings.
- Unchanged vertices.

Saving does not overwrite the source slot mesh. Keep the asset in the project and assign it through the normal UMA recipe, DNA, or Mesh Modifier workflow appropriate to your character.

## Save the Sculpt into a Slot

Sculpt mode also offers two ways to bake the sculpted vertex and normal changes directly into slot `MeshData`. These operations apply only the delta created during the sculpt session, so unrelated generated DNA and Mesh Modifier deformation are not baked into the source mesh.

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

1. Start with the broadest form using Add or Remove, a large radius, Smooth falloff, and low Effect %.
2. Rotate the camera frequently. A change that looks correct from the front can distort the side silhouette.
3. Reduce Radius as the form becomes more detailed.
4. Use masks to protect borders, seams, or finished regions.
5. Use Smooth lightly between shaping passes rather than only at the end.
6. Verify symmetry before committing to many mirrored strokes.
7. Save meaningful milestones as separate Mesh Modifier assets when experimenting.
8. Apply the saved modifier to a generated avatar and inspect it with animation, different DNA values, and relevant wardrobe combinations.

## Troubleshooting

### The brush does not appear

- Confirm **Sculpt** is selected.
- Confirm the desired slot is selected in **Slot**.
- Make sure the slot is visible, not suppressed, and contains editable mesh data.
- Move the pointer off the tools panel and onto the selected mesh.
- Release Alt after navigating the Scene view.
- If another slot covers the selected surface, hide that obstructing slot or choose it instead.

### The brush appears on one slot but not another

This is intentional slot isolation. The raycast must hit the slot selected in the Sculpt panel. Select the correct slot, or temporarily hide overlapping slots to expose the target.

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

No vertex currently differs from the sculpt session's original preview. Perform an Add, Remove, or Smooth stroke with nonzero Effect %, and ensure the region is not fully masked.

### Changing slots asks to discard changes

Only one slot is exported by a sculpt session. Select **Keep Editing**, save the current slot as a Mesh Modifier, and then switch slots. Choose **Discard and Switch** only when you do not need the current preview changes.

### The saved modifier looks different on another character

Mesh Modifiers address source-slot vertex indices. Confirm that the other character uses the same compatible source slot and topology. Also test skinning, pose, DNA, and modifier scaling, since the final generated shape can be influenced by all of them.

## Technical Notes

- Add and Remove use the weighted average normal of selected-slot vertices covered by the brush.
- Smooth uses directly connected triangle neighbors from the selected slot only, while coincident split vertices share one averaged target and displacement.
- The preview mesh collider is refreshed during sculpting so later samples follow the changed surface.
- Normals and bounds are recalculated when the stroke ends.
- The modifier is stored using `VertexDeltaAdjustmentCollection`; no sculpt-specific runtime data format is required.
- The source mesh topology must remain compatible with the saved vertex indices.
