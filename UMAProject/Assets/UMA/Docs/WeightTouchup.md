# Weight Touchup

Weight Touchup corrects a `SlotDataAsset`'s skinning weights directly on a generated UMA character in its current pose. Use it when a body seam opens, clothing collapses around a joint, or transferred weights need a small correction that is easiest to judge on the assembled character.

The tool edits the source slot asset. Every recipe and character that uses that asset receives the saved weights.

## Before You Begin

1. Open a scene containing a built `DynamicCharacterAvatar`.
2. Put the character in a pose that exposes the deformation problem.
3. Select the avatar or one of its children in the Hierarchy.
4. In the Dynamic Character Avatar inspector, open **Utilities > Skinning Weights**.
5. Click **Touchup Weights**.

The character must have a generated `SkinnedMeshRenderer` and at least one visible, weighted, non-utility slot. Work under source control so a saved slot can be restored if the result is unsuitable.

## Choose a Slot and Bone

The **Touchup Weights** window lists the character's visible slots with editable mesh data. Choose one **Slot** before selecting or painting vertices. Only that slot is changed.

Use the **Bones** list to choose the influence used by Paint mode or added during numeric editing. Search filters the list. Vertex colors in the Scene view visualize the selected bone's weight from 0 to 1.

You cannot change slots while unsaved painted weights are pending. Save or revert those changes first.

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

Every painted result is normalized. **Replace** and **Add** can bind the selected bone to the slot when needed.

Enable **Selected Vertices Only** to use the Select mode selection as a paint mask. Enable **Auto-mask Connected Vertices** to protect co-located vertices on other visible slots; this helps prevent a correction from opening a cross-slot seam.

## Preview Controls

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
