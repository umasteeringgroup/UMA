# Creating Mesh Hide Assets from a DynamicCharacterAvatar

Mesh Hide Assets let UMA hide selected triangles on one or more slots when a wardrobe recipe is active. They are most often used to prevent poke-through, where the body or an inner garment sticks through clothing.

This guide uses the current DynamicCharacterAvatar workflow: build a character, open the Face Editor Stage from the DCA inspector, paint the faces to hide, then save Mesh Hide Assets or a Mesh Hide Asset Collection.

## Terms Used in This Guide

- `DynamicCharacterAvatar` or `DCA`: the UMA component that builds the character from a race, wardrobe recipes, colors, and DNA.
- Slot: a mesh part in the UMA recipe, such as a body, torso, legs, gloves, or a clothing mesh. The asset type is `SlotDataAsset`.
- Wardrobe item: a recipe that adds or replaces slots on the character, such as a shirt, pants, armor, or shoes.
- Face or triangle: one triangle in a slot mesh. Mesh hiding is stored per triangle.
- Mesh Hide Asset: a per-slot asset that records which triangles on that slot should be hidden.
- Mesh Hide Asset Collection: an asset that groups several Mesh Hide Assets together. The Face Editor Stage creates this when you save hidden triangles for more than one slot.
- Shown slot: a slot currently displayed in the Face Editor Stage so you can see it while working (the **Show** column).
- Editable slot: a slot whose faces the tools may hide (the **Edit** column). Only editable slots are painted and saved.

## 1. Set Up the Character

1. Open a scene with the character you want to use as the authoring preview.
2. Select the GameObject with the `DynamicCharacterAvatar` component.
3. Set the DCA to the race you want to support.
4. Add the wardrobe items that should drive the mesh hide behavior. For example, if you are creating hides for a jacket, make the jacket visible on the character.
5. Make sure the slots you want to hide are also present in the built character. For a jacket, this usually means the body, torso, arms, or any inner clothing slots that may poke through the jacket.

The Mesh Hide Asset is saved against the slot being hidden, not against the clothing mesh that is causing the hide. A jacket recipe can therefore contain Mesh Hide Assets for body or shirt slots. When the jacket is worn and those slots are present, UMA applies the hide masks to them.

## 2. Put the Character in a Neutral Pose

Use an A-pose or T-pose before opening the editor stage. A neutral pose makes it easier to see shoulder, arm, torso, leg, and underarm areas without animation folding the mesh over itself.

Use the pose setup your project already uses for DCA previewing. Common approaches are:

- Temporarily assign an Animator state or preview clip that holds the character in A-pose or T-pose. UMA includes `UMA_APose_Controller` and `UMA_TPose_Controller` in `Assets/UMA/UMA3/Animation` for Mecanim avatars.
- Use the race's default pose if it is already a neutral authoring pose.
- Disable gameplay animation while authoring, then force the character to rebuild in that pose.

After changing pose, confirm the Scene view shows the character in the intended pose. If the character is still mid-animation, pause or disable the animator before continuing. If the race overrides the Animator, update that override as well.

## 3. Make the Character Build

The Face Editor Stage works from the DCA's built mesh. Before opening it, make sure the avatar has generated successfully.

1. Select the DCA.
2. Confirm the race, base recipe, and wardrobe recipes are assigned.
3. Turn on `Editor Time Generation` if it is not already enabled.
4. Use the DCA inspector controls to build or regenerate the character. If your setup builds automatically, wait until the generated skinned mesh is visible.
5. Check the character in the Scene view. The clothing you are using as reference should be visible, and the slots you plan to hide should exist under it.

If the generated character is missing slots, fix the DCA recipe first. The Face Editor Stage can only save hides for slots that exist in the current built recipe.

## 4. Open the Face Editor Stage

1. In the DCA inspector, open `Utilities`.
2. Under the Mesh Hide Assets help text, click `Create New Mesh Hide Asset`.
3. Unity opens the `Mesh Hide Editor` stage.

To edit an existing Mesh Hide Asset or Mesh Hide Asset Collection, drag it onto the DCA inspector drop area labeled `Drag & Drop a MeshModifier, Mesh Hide Asset, or Mesh Hide Collection here to edit`. Dropping a collection opens the same Face Editor Stage with the existing selections loaded.

Older race-baked masks using an asset filename such as `Body_slot_baked_Race` also load onto the corresponding generated `Body_baked_Race` slot. The editor uses that generated slot's mesh for LOD calculations and records its current name when you save. If a target slot is missing, hidden, or incompatible, the **Mesh Hide Assets** panel shows a warning instead of silently loading an empty selection.

The stage creates a temporary baked mesh for editing and disables the character renderer while the stage is open. When you close the stage, the DCA is restored and rebuilt. The UMA Toolbar overlay is hidden while the stage is open, because its commands act on the main scene; it returns when you close the stage.

The stage has three collapsible panels on the left of the Scene view and a floating toolbar beside them:

- **Face Tools**: the LOD selector, the active tool and its options, the hidden-face count and the save buttons.
- **Slots**: which slots and wearables are shown, and which slots are editable.
- **Mesh Hide Assets**: Recalculate From UV and Raycast Occlusion.
- **Floating toolbar**: the tools (Select, Point Brush, Circle Brush, Square Brush, Texture Brush), **Hide Faces / Unhide Faces**, **Mirror X**, **Cull Backfaces**, **Frame Slots**, **Hide All** and **Clear**. Hover a button for its name and what it does. Drag the toolbar's grip to move it, or double-click the grip to dock it beside the panels again. Its position is remembered.

Use the `LOD Level` slider at the top of the **Face Tools** panel to edit a specific mesh LOD. The preview, paint tools, and raycast selection use that level's triangles. The slider is disabled when the character has only one LOD. Slots with fewer levels use their last available geometry.

Switch freely between LOD levels: the stage remembers each level's unsaved face selection, including cleared selections, without prompting or saving to disk. `Save Mesh Hide Assets (All Edited LODs)` saves those edits together. Unedited saved LOD masks are preserved. The close prompt checks every level for unsaved changes, and closing the stage restores the character's original LOD. `Create MeshHideAssets (Split by Slot)` saves the collected LOD drafts to new assets, splitting by slot when needed.

To regenerate a level, select LOD 1 or higher, choose `Copy LOD Mode`, and click `Recalculate LOD`. The red face selection updates immediately using LOD 0, including its unsaved draft edits. `Strict` (the default) requires all three vertices to belong to hidden base faces; `Weighted` requires two and `Conservative` requires one. Recalculation replaces only the current level's preview selection, supports Undo, and is saved with `Save Mesh Hide Assets (All Edited LODs)`. Create the initial asset first to enable recalculation.

The selected mode is stored with each asset and used for automatic generation of missing levels. Merely previewing a level does not save it as an override. Changes to the base mask or generation mode refresh automatic masks while preserving explicitly saved LOD edits. For older saved masks, use `Recalculate LOD` to replace the displayed level; the Global Library's generation controls also honor `Copy LOD Mode` and its Replace/Missing policy.

## 5. Choose What Is Shown

Use the **Slots** panel to decide what you can see while painting. Its header reads `Editing N of M shown`.

- **Wearables (N)** lists the active wardrobe items. Toggle a wardrobe item on when you need it as a reference shape, or off when it blocks your view.
- Each slot row has a **Show** checkbox. Turn slots on or off to isolate the body, an inner garment, or the clothing mesh.
- The **Show** row of quick actions above the list has **All** (show every slot), **Editable Only** (hide every slot that is not editable) and **Invert** (swap shown and hidden slots and wearables). Invert is useful to quickly swap between the garment and the body under it.

Keep the clothing item shown while deciding where the body or inner garment should be hidden. If the clothing blocks your brush completely, temporarily hide the clothing, paint the underlying body slot, then show the clothing again to check coverage.

The stage keeps at least one slot shown. If you try to hide everything, it leaves a slot shown so the editor still has geometry to display.

## 6. Choose the Slots That Will Receive Mesh Hides

Use the **Edit** column of the **Slots** panel to decide which slots are editable and will be saved.

1. Tick **Edit** only on the slots whose triangles should be hidden. Clicking a slot's name toggles Edit too.
2. Click **S** on a row to solo it: it becomes the only editable slot and is shown if it was hidden.
3. The **Edit** quick actions above the list are **All** (every shown slot), **None** and **Invert**.

A hidden slot cannot be edited; show it first. Only editable slots can be painted, and the wireframe and red hidden-face overlay are drawn for editable slots only. This separation is important: a slot can be shown as reference without being edited for output.

Example: for a jacket, leave the jacket shown as reference, but make the body torso and arms editable. Paint the body triangles hidden under the jacket. Do not make the jacket editable unless you actually want to hide parts of the jacket mesh.

## 7. Seed an Initial Hidden-Face Selection with Raycast Occlusion

Use the `Raycast Occlusion` section of the **Mesh Hide Assets** panel to project an initial set of obscured faces before manual painting. This provides a fast first pass for areas that are clearly covered by the active wardrobe item and reduces the amount of hand-editing required.

1. In the **Slots** panel, make the body or inner-garment slots that should receive the hides editable. These are the **target** slots.
2. Keep both the target slots and the clothing that covers them shown. Leave clothing that is only acting as the cover **not editable**, so it is used as an occluder rather than a target. Hide any other shown slot that should not influence the raycast.
3. Go to the `Raycast Occlusion` section. The tool tests every vertex on each editable target slot both outward along its normal and inward against the other shown slots.
4. Start with the default distances (`0.1` outward and `0.02` inward) unless your character scale or mesh spacing calls for a wider or narrower search range.
5. Choose a `Triangle Strategy`:
   - `Strict`: hide a triangle only when all three of its vertices are occluded.
   - `Weighted`: hide a triangle when at least two vertices are occluded.
   - `Conservative`: hide a triangle when any vertex is occluded. This is the default and is usually the best first pass for avoiding poke-through.
6. Leave `Add to existing hides (otherwise replace)` off for a new pass; it replaces the current raycast result for the processed target slots. Turn it on when you want to keep the current selection and add more faces.
7. Click `Raycast Occlusion To MeshHideAssets` to generate the initial hidden-face selection. Expand **Debug** and use `Draw debug rays` only when you need to diagnose an unexpected result. The button is disabled until at least one slot is editable.
8. Review the result, then refine it manually with the face tools. The raycast updates the stage selection; save the Mesh Hide Assets after the manual review.

## 8. Paint the Triangles to Hide

Pick a tool on the floating toolbar. The **Tool** row of the **Face Tools** panel shows the active tool, whether it hides or unhides, and what it does. **Hidden Faces** counts the triangles currently marked for hiding.

### Hide or unhide

**Hide Faces** and **Unhide Faces** on the toolbar choose what every tool does: hide triangles (shown in red) or unhide them.

### Select tool

- Click a triangle, or drag a rectangle, to hide or unhide the faces under it.
- **Cull Backfaces** makes the rectangle (and painting) ignore triangles that face away from the camera.
- Hold `Shift` and drag to hide faces one at a time, or `Ctrl` and drag to unhide them. This is useful for a quick touch-up without switching tools.

### Brushes

- **Point Brush** paints the single triangle under the cursor.
- **Circle Brush** and **Square Brush** paint every triangle inside the brush.
- **Texture Brush** paints with a grayscale or alpha **Brush Texture** mask, chosen in the Face Tools panel.
- For the circle, square and texture brushes, set **Radius (mm)**. The radius is measured on the model in millimeters, so the brush looks larger on screen as you zoom in.
- **Mirror X** mirrors strokes across the mesh's local X = 0 plane.

Left-drag over the mesh to paint continuously.

### Toolbar actions

- **Frame Slots** frames the editable slots in the Scene view. Use it if the camera is not focused on the mesh.
- **Hide All** hides every triangle on the editable slots.
- **Clear** unhides every triangle.

Hidden triangles are shown with a red overlay. Unhidden editable triangles show as wireframe. Orbit, pan, and zoom the Scene view as usual, then continue painting.

## 9. Check the Result Against the Clothing

Before saving, switch visibility back and forth to confirm the hide mask is doing the intended job.

1. Show the clothing item that causes the poke-through.
2. Show the body or inner garment slots that are being hidden.
3. Rotate around the character and check shoulders, elbows, hips, knees, and any tight seams.
4. Remove hidden triangles that are visible outside the clothing.
5. Add hidden triangles anywhere the covered slot still pokes through.

Prefer hiding slightly more covered geometry rather than leaving tiny poke-through islands, but avoid hiding triangles that can be seen during normal poses. If triangles poke through a seam, fix the source mesh when possible. Otherwise, consider a separate Mesh Modifier that moves the vertices inward, then add that modifier to the recipe.

## 10. Save the Mesh Hide Assets

When the selection is ready, click `Create MeshHideAssets (Split by Slot)` at the bottom of the **Face Tools** panel. If you opened the stage from existing assets, `Save Mesh Hide Assets (All Edited LODs)` above it writes back into those assets instead. Both buttons stay visible however far the panel is scrolled.

Unity opens a `Save MeshHideAssets` save dialog. Choose a folder and base name.

- If selected hidden triangles belong to one slot, the stage saves one `MeshHideAsset` at the chosen path.
- If selected hidden triangles belong to more than one slot, the stage saves a `MeshHideAssetCollection` at the chosen path and creates one `MeshHideAsset` per slot next to it.
- Per-slot assets are named from the base name plus the slot name.
- The collection is updated to reference the generated per-slot Mesh Hide Assets.

For example, saving `Jacket_BodyHides.asset` with hidden faces on `HumanMaleTorso` and `HumanMaleArms` creates a collection named `Jacket_BodyHides.asset` plus per-slot assets such as `Jacket_BodyHides_HumanMaleTorso.asset` and `Jacket_BodyHides_HumanMaleArms.asset`.

If the stage was opened by dropping an existing collection, saving updates that collection. If you save over existing generated assets, Unity asks before overwriting.

Close the Face Editor stage using the `Close` button at the top right of the Scene view.

## 11. Add the Collection to the Wardrobe Recipe

Mesh hides usually belong on the wardrobe recipe that causes the hide. For example, body hides for a jacket should usually be assigned to the jacket wardrobe recipe.

1. Select the wardrobe recipe asset in the Project window.
2. In the inspector, find `Mesh Modifications`.
3. Drag the saved `MeshHideAssetCollection` into the drop area labeled `Drag Mesh Hide or Modifier Assets or collections here, or use buttons above to select.`
4. Confirm it appears under `Mesh Hide Asset Collections`.

You can also add individual per-slot `MeshHideAsset` assets under `Mesh Hide Assets`, but a collection is easier to manage when one clothing item hides several slots.

## 12. Test the Wardrobe Item

1. Return to the normal scene stage.
2. Rebuild the DCA with the wardrobe item active.
3. Check the character in A-pose or T-pose first.
4. Test a few animation poses that commonly expose poke-through.
5. If the hide is too aggressive or not strong enough, drag the Mesh Hide Asset Collection back onto the DCA Utilities drop area and edit it in the Face Editor Stage.

The Face Editor Stage temporarily ignores existing mesh hides while authoring so you can see and edit the source geometry. Runtime and normal DCA builds apply the Mesh Hide Assets from active recipes.

## Creating Mesh Hides Automatically

The **Rig Profile Editor** (`UMA > Tools > Slot Tools > Rig Profile Editor`, or the button on a race) can create the Mesh Hide Assets for a wardrobe recipe without painting anything. It is meant for clothing brought over from an older rig, where a whole wardrobe needs hides at once, but it works for any recipe the race can wear.

1. Open the editor for the race and add the wardrobe recipes under **Preview character**.
2. Open **Mesh hides** and press **Create mesh hides for the preview recipes**.

For each recipe it works out which triangles of the race's body the recipe's clothing covers, saves one Mesh Hide Asset per body slot that has something to hide in a `MeshHides` folder next to the recipe (named `<recipe>_<slot>`), and adds those assets to the recipe's **Mesh Hide Assets** list. Running it again updates the same assets; a hide that no longer hides anything is taken off the recipe and its file is left in place. Clothing built for the race's legacy rig is converted with the profile first, so run it again after changing the fit.

The test is the same idea as **Raycast Occlusion** in the Face Editor: a ray outward along each body vertex's normal, then a short one inward, then a per-triangle strategy. The settings are:

- **Triangle strategy.** `Strict` (the default here) hides a triangle only when all three of its vertices are covered, so nothing that can be seen is removed. `Weighted` and `Conservative` hide more and leave less to poke through, but can open gaps at collars and cuffs. Because nobody reviews the result before it is saved, the default is the cautious one.
- **Outward distance** and **Inward distance**, in metres, as in the Face Editor (0.1 and 0.02).
- **Only clothing lying over it.** Clothing counts only where its surface faces the same way as the body under it. This keeps, for example, the inside of a bare arm from being hidden because it faces the side of a vest.

**Show in preview** applies the recipes' hides to the preview character, as a built character does; turn it off to see the whole body and look for poke-through. The result is an ordinary Mesh Hide Asset, so it can be opened and refined in the Face Editor Stage afterwards.

### Checking and correcting the result

The automatic test is cautious, but it does not know what you can see. It can hide skin behind an open collar or a loose cuff, and it can leave skin that pokes through when the character moves. Both are quick to put right on the preview character.

- **Mark hidden in red** draws every body triangle the preview's recipes hide in red in the Scene view. **Red through clothing** draws the marks over the clothing, so you can see what is hidden under a garment without taking it off. **Show clothing**, turned off, takes the clothing off the preview character instead and leaves the body, with its hides still applied.
- **Paint** switches on a small correction brush. **Show** makes the red triangles you drag over visible again; **Hide** hides the body triangles you drag over, in the hide of the recipe chosen under **Hide for recipe** (a hide for that body slot is created and added to the recipe if there is none yet). **Brush radius** is in metres; at zero the brush paints the single triangle under the cursor. Only triangles facing the view are painted, so the brush does not reach the far side of a thin part. Alt-drag still turns the view, and the bone handles step aside while the brush is on. A stroke is one undo step, and it is saved to the Mesh Hide Asset when you release the mouse button.

What you paint is stored twice: in the hide's triangles, and as a **never hide** or **always hide** mark on each painted triangle. When the hides are created again, the generator works out its own result and then applies the marks, always-hide first and never-hide last. So corrections survive every regeneration, whatever the settings, and you can change the fit and regenerate without repainting. **Forget** drops a hide's marks; its triangles stay as they are until the next regeneration.

The list under the brush shows each hide the preview's recipes apply to the body, how many triangles it hides and how many carry a mark.

- **Lock** marks a hide as finished by hand. The generator then leaves it exactly as it is: not recomputed, not emptied, not taken off the recipe. The brush does not change a locked hide either. The tick is also on the Mesh Hide Asset itself (*Auto Generation Locked*).
- **Face Editor** opens the hide in the [Face Editor Stage](#4-open-the-face-editor-stage) on the preview character, for work the brush is too small for: lasso and box selection, mirrored painting, per-LOD masks. The preview is rebuilt with every triangle present first, as the Face Editor expects. Save and leave the stage as usual, then **Lock** the hide so a later regeneration does not replace what you did there. Edits made in the Face Editor are not recorded as marks, which is what the lock is for.

The brush is for corrections, a few triangles at a collar or a wrist. For a hide that needs real authoring, use the Face Editor and lock it.

### Base slots and baked slots

A race with prebaked blend shapes does not build its characters from the body slots in the project. It builds from copies with the blend shapes baked in, named `<slot>_baked_<race>`. A Mesh Hide Asset names one slot, and the automatic hides name the **base** slot. UMA applies a hide that names a base slot to that slot's baked copy as well (the triangles are the same), so one hide serves every race that bakes the slot and any that does not. Hides saved from the Face Editor on a baked character name the baked copy and keep working as before.

## Troubleshooting

- The editor opens, but the mesh is missing: rebuild the DCA first and confirm the character has a visible generated skinned mesh.
- The slot you want is not listed: the slot is not present in the current built recipe. Add the wardrobe or base recipe that contains it, then rebuild and reopen the stage.
- Painting does nothing: make sure the target slot has **Edit** ticked in the **Slots** panel. Showing a slot does not make it editable.
- The clothing is in the way: untick its **Show** checkbox, or its wearable, in the **Slots** panel, paint the covered slot, then show the clothing again to check the result.
- A saved asset hides the wrong mesh: verify the per-slot Mesh Hide Asset was created for the slot you intended. The `AssetSlotName` must match the slot that should be hidden.
- The collection is not applied in game: make sure the collection is added to the active wardrobe recipe under `Mesh Hide Asset Collections`, then rebuild the avatar.
