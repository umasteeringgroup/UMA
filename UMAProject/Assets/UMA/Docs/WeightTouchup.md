# Weight Touchup

Weight Touchup corrects a `SlotDataAsset`'s skinning weights directly on a generated UMA character in its current pose. Use it when a body seam opens, clothing collapses around a joint, or transferred weights need a small correction that is easiest to judge on the assembled character.

The tool edits the source slot asset. Every recipe and character that uses that asset receives the saved weights.

## Before You Begin

1. Open a scene containing a built `DynamicCharacterAvatar`.
2. Put the character in a pose that exposes the deformation problem, or pose it afterwards with **Pose Mode** (see below).
3. Select the avatar or one of its children in the Hierarchy.
4. In the Dynamic Character Avatar inspector, find the **Editing tools** section, just below **Character & race**.
5. Click **Touchup Weights**.

The character must have a generated `SkinnedMeshRenderer` and at least one visible, weighted, non-utility slot. Work under source control so a saved slot can be restored if the result is unsuitable.

## Choose a Slot and Bone

The **Touchup Weights** window lists the character's visible slots with editable mesh data. Choose one **Slot** before selecting or painting vertices. Only that slot is changed.

Use the **Bones** list to choose the influence used by Paint mode or added during numeric editing. Search filters the list. Vertex colors in the Scene view visualize the selected bone's weight from 0 to 1.

You cannot change slots while unsaved painted weights are pending. Save or revert those changes first.

## Vertices at the Same Position

A mesh splits one point of its surface into several vertices wherever UVs, normals, or materials change. Those copies sit at the same position and carry the same weights. If only one of them were painted, the surface would tear open along the split as soon as a bone moved.

Touchup Weights therefore treats the vertices of the slot that are at the same position (within 0.1 mm) and have the same weights as a single vertex:

- Selecting one of them selects all of them.
- Painting or smoothing one gives all of them the same result, also the copies that face away from the camera or are hidden.
- Smoothing uses the connected vertices of every copy, so a seam smooths across both of its sides.

Vertices at the same position with different weights are left separate. They were weighted apart on purpose, as on two layers of cloth that lie on each other.

## Select Mode

Use **Select Mode** to choose vertices and edit a complete set of influences numerically.

- Drag the circle brush to replace the selection.
- Hold **Shift** while dragging to add vertices.
- Hold **Ctrl** while dragging to remove vertices.
- Adjust **Brush Radius** for the area being selected.
- Enable **Select Obscured** to include vertices hidden behind the visible surface.
- Enable **Select Backfacing** when the far-facing side should also be selectable.

The Weights panel uses the first selected vertex as the reference. Change its sliders, remove influences, or use **Add Selected Bone**. When several vertices are selected, **Save Weights** writes that same normalized influence set to every selected vertex.

Use this workflow when a group of vertices should share exact weights. Select only one vertex when making an isolated correction.

### Smooth a selection

Set **Smooth Percentage** and click **Smooth Vertex Weights** to blend each selected vertex's complete influence set toward the average of its connected vertices. The result remains staged until it is saved.

## Paint Mode

Use **Paint Mode** to modify weights continuously with the Scene view brush. Select the target bone in the Touchup Weights window, set **Amount**, and choose an operation:

| Operation | Result |
|---|---|
| **Replace** | Sets the selected bone toward Amount and proportionally scales the other influences. |
| **Add** | Increases the selected bone by Amount, then normalizes all influences. |
| **Remove** | Decreases the selected bone by Amount, then normalizes all influences. |
| **Smooth** | Moves weights toward the average of connected vertices by Amount. It can smooth only the selected bone or all influences. |
| **Smear** | Pulls the selected bone's weights from the previous brush position along the stroke direction. |

With **Smooth** selected, **Constant Application** keeps smoothing while the mouse button is held, also when the pointer does not move. The brush is applied again every **Every (ms)** milliseconds (100 by default), so the weights under it keep evening out, as with Constant Application on the sculpt brushes. Without it, one stroke smooths each vertex once. The whole press is one stroke and one Undo step, and the setting is remembered. The other paint operations are not affected.

Every painted result is normalized. **Replace** and **Add** can bind the selected bone to the slot when needed.

Enable **Selected Vertices Only** to use the Select mode selection as a paint mask. Enable **Auto-mask Connected Vertices** to protect co-located vertices on other visible slots; this helps prevent a correction from opening a cross-slot seam.

## Pose Mode and the Skeleton

Weights can only be judged in a pose that bends the joint in question. **Pose Mode** turns the bones of the character you are editing, without leaving Touchup Weights.

1. Choose **Pose Mode** in the mode bar. The skeleton is drawn over the character.
2. Click a bone in the Scene view. It turns orange, and rotation rings appear at its joint.
3. Drag a ring, or use the **Turn X**, **Turn Y**, and **Turn Z** sliders. They show how far the bone is turned from the character's own pose, about the bone's own axes.
4. Switch to **Select Mode** or **Paint Mode** and work on the weights. The pose stays.

- **Mirror Left/Right** turns the bone of the same name on the other side by the mirrored amount, for example both thighs at once. It is off unless you turn it on; the setting is remembered.
- **Pose Weight Bone** chooses the bone selected in the Touchup Weights window as the bone to turn. **Weight This Bone** does the opposite: it makes the bone you are turning the one that Paint mode works on.
- **Reset Bone** gives the chosen bone its own rotation back. **Reset Pose** does that for every turned bone, and is also offered in the other modes while a pose is active.

**Show Bones**, with the other preview controls, draws the skeleton in Select and Paint modes as well. The bone being weighted is cyan, the bone Pose mode turns is orange, and other turned bones are lighter orange. **Slot Bones Only** limits the drawing to the bones the current slot is weighted to and the bones above them; turn it off to see and pose every bone of the character.

The mesh is skinned again each time a bone turns, with the weights you are editing, so painted and previewed weights show their effect in the pose immediately.

The pose is a working aid only. It is not saved and it is not part of Undo. The character gets its own pose back when Touchup Weights closes or scripts reload. A rebuild of the character while Touchup Weights is open keeps the pose.

## Preview Controls

- **Weight Colors** colors the slot being weighted by the selected bone's weight, as Blender does: red is full weight, through yellow and green, to blue for none. It is on by default and the setting is remembered. Turn it off to see the slot with its own material while judging a deformation. A slot that is blue all over means the selected bone has no weight on it; choose another bone in the Touchup Weights window.
- **Live update** recalculates edited vertex positions from the current skeleton every editor frame. Enable it when the deformation should respond immediately in the posed character.
- **Handle Size** changes the displayed vertex-handle size but not the edited area.
- **Reset Camera** frames the active slot again.
- Changing the selected bone updates the weight-color visualization.

Orbit or zoom with the normal Scene view navigation controls. Avoid painting while navigating.

## Save or Revert

**Save Weights** writes the staged changes to the selected source `SlotDataAsset` and saves the asset. Numeric edits are normalized before saving. Paint and selection-smoothing changes remain reversible until this button is used.

**Revert** discards unsaved numeric previews or painted changes. Closing the stage with pending changes asks whether to save, discard, or cancel.

After saving:

1. Rebuild the avatar if another view does not refresh automatically.
2. Test the corrected pose and neighboring animation frames.
3. Test a neutral pose and the opposite extreme of the joint.
4. Check adjacent slots for gaps.
5. Test representative DNA extremes and LODs.

## Artist Guidance

- Make small changes and verify them through motion rather than judging one frame.
- Prefer smoothing or small Add/Remove strokes before replacing a broad area.
- Keep seam vertices compatible with the neighboring body or clothing slot.
- Avoid adding bones that are absent from the target race skeleton.
- Use numeric editing when several vertices require one exact influence set.
- Use paint masking when a correction must stop at a deliberate boundary.

## Troubleshooting

### Touchup Weights cannot open

Build the selected `DynamicCharacterAvatar` first. Confirm that it has a generated skinned mesh and at least one visible slot with weighted mesh data.

### The wrong mesh is being edited

Check the **Slot** field in both the window and Scene view tool panel. Only the active slot accepts selections and brush strokes.

### Painting changes vertices behind the surface

Disable **Select Obscured**. If back-facing vertices are also included, disable **Select Backfacing**.

### A seam opens after painting

Revert the stroke if it is still pending. Enable **Auto-mask Connected Vertices**, use a smaller brush, and compare the border weights on both slots.

### Every selected vertex receives identical weights

This is expected for numeric editing. The first selected vertex supplies the values and Save applies that complete set to all selected vertices. Use Paint mode or select one vertex at a time when weights should vary.

### The change affects other characters

Weight Touchup saves to the shared source `SlotDataAsset`. This is intentional. Duplicate the slot and update the relevant recipes first when the correction should apply only to a content variant.

See also [Content Creation](ContentCreation.md) and [SlotDataAsset](SlotDataAsset.md).
