# UMA Mesh Modifiers

Mesh Modifiers store repeatable changes to slot vertices. They can correct clothing, add authored shape detail, adjust normals or colors, reproduce blendshape results, and participate in wardrobe or DNA-driven builds.

Use this guide for the complete Mesh Modifier workflow. For brush sculpting controls, masks, AutoSculpt, and saving sculpts, see [MeshModifierSculpting.md](MeshModifierSculpting.md).

The `Assets > UMA > Create DNA for selected Modifiers` batch command is documented in [Project Asset Batch Utilities](AssetMenuBatchUtilities.md).

## When to Use a Mesh Modifier

Mesh Modifiers are useful when:

- A wearable needs a consistent local fit correction.
- A DNA control needs to move selected slot vertices.
- A body or garment needs a reusable sculpt.
- Vertex colors need to drive a shader effect.
- Normals or tangents need a controlled correction.
- An existing blendshape should be converted into slot-specific vertex changes.
- A build-time adjustment is preferable to keeping a live blendshape.

They are not a replacement for correct modeling and skinning. Fix broad topology, weight, and rest-pose problems in the source mesh.

## Open the Editor

1. Select a built `DynamicCharacterAvatar`.
2. Enable Editor Time Generation when working outside Play mode.
3. In the DCA inspector, find the **Editing tools** section, just below **Character & race**.
4. Click **Open Vertex Editor (Sculpt, Paint, Mesh Modifiers)** to start a new Mesh Modifier, or drop an existing Mesh Modifier on the drop area at the bottom of the section to edit it.

Build the avatar with every slot you intend to modify. A modifier records slot and vertex identity; topology changes can invalidate the stored selection.

## Authoring Workflows

The Mesh Modifiers window provides four workflows:

- `Sculpt`: brush-based position sculpting.
- `Vertex Paint`: brush-based vertex color authoring.
- `Advanced`: precise per-vertex or vertex-set adjustments.
- `Blendshapes`: extract source blendshape frames into Mesh Modifier data.

Choose the simplest workflow that produces the required result. The same four buttons appear at the top of the tools panel in the editing stage.

### The editing stage

The stage shows a **tools panel** and a **Slots** panel on the left of the Scene view, and a **floating toolbar** beside them. The toolbar holds the tools of the current workflow (Sculpt brushes, Vertex Paint brushes, or Advanced selection tools) and ends with **Frame**, which frames the character from the starting camera angle. Hover a button for its name and what it does; the panel's **Tool** row also names the active tool. Drag the toolbar's grip to move it, or double-click the grip to dock it again. **Close** is at the top right of the Scene view.

The **Slots** panel decides what every tool may change. Tick **Show** to display a slot and **Edit** to let the tools change it; **S** solos a slot. The quick actions are **Edit: All / None / Invert / No Base** and **Show: All / Editable Only / Invert**. Every shown slot is editable until you lock it. Commands that write one `SlotDataAsset` need exactly one editable slot, so solo that slot first. See [Choose the slots to sculpt](MeshModifierSculpting.md#choose-the-slots-to-sculpt).

Brush sizes are shown in millimeters on the model (**Radius (mm)**, 1–250 mm). The UMA Toolbar overlay is hidden while the stage is open and returns when you close it.

## Build and Preview Controls

The `Build and Preview Options` foldout controls what is included while authoring:

- `Include Advanced Per-Vertex Adjustments`
- `Include Advanced Vertex-Set Modifiers`
- `Only Active Vertex-Set Modifier`
- `Rebuild on changes`
- `Rebuild Now`
- `Rebuild to T-Pose`
- `Reset Build`
- `Recalculate Normals`

Use `Only Active Vertex-Set Modifier` when isolating one correction. Disable `Rebuild on changes` when a character is expensive to generate, then rebuild manually after several edits.

## Sculpt

Sculpt is the preferred workflow for organic position changes.

It sculpts whichever slots are editable in the Slots panel: one slot, several, or every non-base slot with **No Base**. Brush modes include additive and subtractive movement, smoothing, grab, crease, pinch, plane, boundary-aware, and elastic operations.

Sculpt also supports:

- Falloff control
- Connected-surface filtering
- X symmetry
- Live normal updates
- Sculpt masks
- AutoSculpt
- Saving as a Mesh Modifier
- Saving as a blendshape or slot result

See [MeshModifierSculpting.md](MeshModifierSculpting.md).

## Vertex Paint

Vertex Paint stores color changes on selected slot vertices. Use it when a shader reads vertex colors for:

- Tint or masks
- Dirt or damage
- Wetness
- Material transitions
- Project-specific effects

Confirm that the final shader uses the intended vertex color channels. Painting data that the shader never reads has no visible result.

The floating toolbar holds the nine brushes (**Paint**, **Erase**, **Smear**, **Blur**, **Smooth**, **Burn**, **Dodge**, **Noise**, **Clear Brush**), the brush shapes (**Circle**, **Square**, **Bitmap**), the brush color swatch used by Paint, and **Clear**, which removes all painting from the editable slots and restores their original colors (Undo brings it back).

The panel's **Brush** section has **Radius (mm)**, **Effect %** and **Affect Alpha**, plus **Brush Bitmap** for the Bitmap shape. Paint adds **Blend**: **Replace** (the default) paints the brush color, so white lightens and black darkens; **Multiply** tints the color under the brush by the brush color, so it can only darken. Noise adds **Noise Type** (Per Vertex grain, or Smooth patches sized by **Noise Scale**), **Color Noise**, **Noise Amount** and **Same Pattern Each Stroke**. Noise never changes alpha.

Use the `Smooth` brush to soften blocky vertex-color transitions. `Smooth Amount %` controls how strongly a stroke blends colors, while `Smooth Radius (mm)` controls the neighborhood sampled around each affected vertex. Unlike the topology-based `Blur` behavior, `Smooth` can blend across UV seams and nearby disconnected vertices; oppositely facing surfaces are excluded to reduce color bleeding through thin meshes.

The **Whole-Slot Operations** section has `General Smooth`, which applies one simultaneous smoothing pass to the editable slot (**Smooth Slot**) or to every editable slot (**Smooth Editable Slots**). Its independent `Amount %` and `Radius (mm)` controls set the strength and sample area. `Cutoff %` returns results below the threshold to the slot's unmodified color, so imperceptible color spread does not create extra MeshModifier adjustments. **Fill Slot with White** sets every color on a single soloed slot to white; the toolbar's **Clear** instead restores the original colors.

Save with **Modifier Name** and **Save as MeshModifier** at the bottom of the panel; this keeps sculpt work too. **More Save Options** has **Save Colors to SlotDataAsset**, which bakes the colors into one soloed slot's mesh data.

## Advanced Selection Tools

The `Advanced` workflow selects vertices for the precision and vertex-set editors in the Mesh Modifiers window. Its floating toolbar has three groups:

- Tools: **Select** (click a vertex, or drag a rectangle), **Point Brush** and **Circle Brush** (drag; each vertex changes once per stroke).
- Mode: **Define Set** chooses which vertices belong to the selection; **Define State** marks vertices already in the selection Active or Inactive.
- Action: in Define Set, **Add**, **Remove** or **Invert**; in Define State, **Activate**, **Deactivate** or **Toggle**.

The panel's **Tool** row shows the tool and action. It also has:

- **Obscured** and **Backfacing** to allow selecting hidden and away-facing vertices, and **Default State** for newly added vertices.
- **Select All**, **Invert** and **Clear** for the whole selection, and **Activate All**, **Deactivate All** and **Invert State** for its states.
- **Selection File**: **Save...**, **Load...** (replace) and **Append...** store the selection as JSON.
- **Select by raycasting** (Select tool in Define Set): cast from an editable slot to select the vertices that hit other slots.
- **Weights**: **Smooth Selected Weights** and **View/Edit Vertex Weights**.
- **Display**: **Handle Size**, **Active Color** and **Inactive Color** for the vertex markers.

Hold Alt and use the mouse to move the camera.

## Advanced Per-Vertex Precision

Use `Advanced > Per-Vertex Precision` for exact changes on isolated vertices.

This workflow is appropriate for:

- Small seam corrections
- One-off vertex position repairs
- Exact normal, tangent, UV, reset, or blendshape-related values
- Different values on individual vertices

Select a current vertex in the Scene view, add the required adjustment type, and enter the value. Per-vertex precision can become difficult to maintain when hundreds of vertices need the same change; use a vertex-set modifier instead.

## Advanced Vertex Sets

Use `Advanced > Vertex Sets / Bulk` when the same adjustment should apply to an explicit group.

1. Select vertices in the Scene view with **Define Set**.
2. Mark the intended vertices Active with **Define State** or **Activate All**.
3. Choose the adjustment type.
4. Click `Create Modifier From Active Vertices`.
5. Edit the template value.
6. Use `Edit Current`, `Add to Current`, `Replace Current`, or `Clear Current` to manage membership.

Every vertex in the collection receives the same template adjustment. This makes bulk modifiers easier to tune than hundreds of independent values.

Available adjustment types depend on the installed UMA version and include position, scale-along-normal, normal/tangent, vertex color, reset, and blendshape-related adjustments.

### Normal-reset baseline

`Recalculate Normals to Reset Modifier` captures a controlled normal-rotation correction. Use it only when the workflow needs recalculated normals and verify the result under the final production shader.

## Extract Blendshapes

Blendshape extraction converts a source blendshape result into Mesh Modifier adjustments for selected slots.

1. Build a DCA whose active slots contain the source blendshape.
2. Open the `Blendshapes` workflow.
3. Choose the blendshape.
4. Choose a DNA name, or leave the result for manual use.
5. Select the source slots.
6. Click `Extract Blendshapes`.
7. Review the created modifiers and save the asset.

The extraction uses the stored source slot topology and blendshape frames. Reimporting the slot with changed vertex order can invalidate the result.

You can also extract blendshapes from a `SlotDataAsset` inspector with `Extract to MeshModifier` or `Extract all`.

## Save the Asset

Click `Save to Asset` after authoring. Saving does not automatically decide where the modifier will be used.

Use a clear name that identifies:

- The race or topology family
- The target slot or garment
- The purpose of the correction

For example:

`HumanFemale30_JacketShoulderFit`

## Apply Through a Wardrobe Recipe

For a correction that should exist whenever an item is equipped:

1. Open the `UMAWardrobeRecipe`.
2. Find `Mesh Modifications`.
3. Add the Mesh Modifier.
4. Save the recipe.
5. Equip and remove the item repeatedly to verify the modifier follows the recipe lifecycle.

Keep the correction with the recipe that requires it instead of placing it globally on every character.

## Apply Through DNA

The new DNA system can apply a Mesh Modifier through `DNAEffect_MeshModifier`.

Use this when modifier strength should respond to an artist-authored DNA control:

1. Create or open the DNA asset.
2. Add a Mesh Modifier effect.
3. Assign the modifier.
4. Configure curve, minimum, and maximum mapping.
5. Add the DNA to a group used by the race.
6. Test neutral and extreme values.

See [DNACreationGuide.md](DNACreationGuide.md).

## Manual Testing

The DCA inspector also exposes manual Mesh Modifier testing for editor use. This is useful for previewing an asset before assigning it to a production recipe or DNA effect.

Do not rely on the manual editor test list as the production ownership model. Put the modifier in the wardrobe recipe, DNA, or other intended runtime source.

## Performance

Mesh Modifier cost increases with:

- The number of active modifiers
- The number of adjusted vertices
- The number of slots touched
- Repeated rebuilds while authoring
- Normal and tangent work

The jobified and incremental combiners can process supported Mesh Modifiers away from the main thread. The modifier still needs stable source topology and must be included in the character build plan.

Prefer:

- One well-scoped vertex-set modifier over many duplicate per-vertex edits
- Recipe ownership so unused corrections are not active
- Manual rebuilds while tuning expensive characters
- Source-mesh fixes for broad problems

## Troubleshooting

### The modifier does not appear

- Confirm the target slot is present.
- Confirm the recipe or DNA effect includes the modifier.
- Enable the corresponding preview inclusion toggle.
- Rebuild the character.

### The wrong vertices move

The slot topology or vertex order changed after the modifier was authored. Recreate the modifier from the current slot.

### The result doubles

The modifier may be included through more than one source, such as both manual testing and a wardrobe recipe.

### A clothing correction affects the body

Check the modifier's slot keys and target selection. Reauthor with only the clothing slot editable in the **Slots** panel.

### Normals look incorrect

Recalculate normals only when required, verify tangents, and test with the final normal-mapped material.

### Blendshape extraction finds nothing

Confirm the active source slots contain the blendshape and that the DCA is configured to load blendshapes.

## Related Guides

- [MeshModifierSculpting.md](MeshModifierSculpting.md)
- [WardrobeRecipeEditor.md](WardrobeRecipeEditor.md)
- [DNACreationGuide.md](DNACreationGuide.md)
- [ContentCreation.md](ContentCreation.md)
- [MeshCombiners.md](MeshCombiners.md)
