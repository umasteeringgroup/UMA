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
- Visible slot: a slot currently shown in the Face Editor Stage so you can see it while working.
- Selectable slot: a slot that can receive painted hidden faces. Only selected slots are edited and saved.

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

- Temporarily assign an Animator state or preview clip that holds the character in A-pose or T-pose.
- Use the race's default pose if it is already a neutral authoring pose.
- Disable gameplay animation while authoring, then force the character to rebuild in that pose.

After changing pose, confirm the Scene view shows the character in the intended pose. If the character is still mid-animation, pause or disable the animator before continuing.

## 3. Make the Character Build

The Face Editor Stage works from the DCA's built mesh. Before opening it, make sure the avatar has generated successfully.

1. Select the DCA.
2. Confirm the race, base recipe, and wardrobe recipes are assigned.
3. Use the DCA inspector controls to build or regenerate the character. If your setup builds automatically, wait until the generated skinned mesh is visible.
4. Check the character in the Scene view. The clothing you are using as reference should be visible, and the slots you plan to hide should exist under it.

If the generated character is missing slots, fix the DCA recipe first. The Face Editor Stage can only save hides for slots that exist in the current built recipe.

## 4. Open the Face Editor Stage

1. In the DCA inspector, open `Utilities`.
2. Under the Mesh Hide Assets help text, click `Create New Mesh Hide Asset`.
3. Unity opens the `Mesh Hide Editor` stage.

To edit an existing Mesh Hide Asset or Mesh Hide Asset Collection, drag it onto the DCA inspector drop area labeled `Drag & Drop a MeshModifier, Mesh Hide Asset, or Mesh Hide Collection here to edit`. Dropping a collection opens the same Face Editor Stage with the existing selections loaded.

The stage creates a temporary baked mesh for editing and disables the character renderer while the stage is open. When you close the stage, the DCA is restored and rebuilt.

## 5. Choose What Is Visible

Use the `Visibility` panel to decide what you can see while painting.

- `Visible Wearables` lists the active wardrobe items. Toggle a wardrobe item on when you need it as a reference shape, or off when it blocks your view.
- `Visible Slots` lists the built slots. Toggle slots on or off to isolate the body, an inner garment, or the clothing mesh.
- `Invert Visiblity` flips the current visible state. This is useful when you want to quickly swap between the garment and the body under it.

Keep the clothing item visible while deciding where the body or inner garment should be hidden. If the clothing blocks your brush completely, temporarily hide the clothing, paint the underlying body slot, then show the clothing again to check coverage.

The stage keeps at least one slot visible. If you try to hide everything, it will leave a slot visible so the editor still has geometry to display.

## 6. Choose the Slots That Will Receive Mesh Hides

Use the `Mesh Hide Assets` panel to decide which slots are editable and will be saved.

1. In `Selectable Slots`, enable only the slots whose triangles should be hidden.
2. Use `Select Visible` to make every currently visible slot selectable.
3. Use `Clear` to clear the selectable slot list.

Only selectable slots can be painted. The wireframe and red hidden-face overlay are drawn for selectable slots only. This separation is important: a slot can be visible as reference without being selected for output.

Example: for a jacket, leave the jacket visible as reference, but make the body torso and arms selectable. Paint the body triangles hidden under the jacket. Do not make the jacket selectable unless you actually want to hide parts of the jacket mesh.

## 7. Seed an Initial Hidden-Face Selection with Raycast Occlusion

Use the `Raycast Occlusion` tools to project an initial set of obscured faces before manual painting. This provides a fast first pass for areas that are clearly covered by the active wardrobe item and reduces the amount of hand-editing required.

1. In the visibility section, ensure that the meshes you want to use are visible - the occluding slots, and the slots you are calculating. 
2. Go to the `Raycast Occlusion` section. 
3. Adjust the outward and inward distances if your meshes need a wider or narrower search range.
4. Click `Raycast Occlusion To MeshHideAssets` to generate the initial hidden-face selection.
5. Review the result, then refine it manually using the Face Tools controls.

## 8. Paint the Triangles to Hide

Use the `Face Tools` panel to control selection.

### Basic Selection

- `Operation`: choose `Add` to mark triangles as hidden, or `Remove` to unmark triangles.
- Click a triangle to add or remove it, depending on the current operation.
- Drag without Paint Mode to draw a rectangle selection.
- `Rubber Band Cull Backfaces` makes rectangle selection ignore back-facing triangles.
- `Clear` removes all currently selected hidden faces.
- `Select All` marks all triangles on the currently selectable and visible slots.
- `Selected Faces` shows how many triangles are currently marked for hiding.
- `Reset Camera` frames the currently selectable slots in the Scene view.

### Modifier Painting Without Paint Mode

You can temporarily paint while `Paint Mode` is off:

- `Shift` + left-drag adds hidden triangles.
- `Ctrl` + left-drag removes hidden triangles.

This is useful when you normally use rectangle selection but need to touch up a small area quickly. Note: Paint mod is much easier for touchup.

### Paint Mode

Enable `Paint Mode` for continuous brush painting.

- Set `Operation` to `Add` to paint hidden triangles.
- Set `Operation` to `Remove` to erase hidden triangles.
- Left-drag over the mesh to paint continuously.
- Use `Point` for single-triangle picking.
- Use `Circle` for a circular brush.
- Use `Square` for a square brush.
- Use `Load` to paint with a grayscale or alpha `Brush Texture` mask.
- For `Circle`, `Square`, and `Load`, adjust `Radius` to control brush size.

Hidden triangles are shown with a red overlay. Unhidden selectable triangles show as wireframe. Orbit, pan, and zoom the Scene view as usual, then continue painting. If the camera is not focuse on the mesh, press the `Reset Camera` button to focus it.

## 9. Check the Result Against the Clothing

Before saving, switch visibility back and forth to confirm the hide mask is doing the intended job.

1. Show the clothing item that causes the poke-through.
2. Show the body or inner garment slots that are being hidden.
3. Rotate around the character and check shoulders, elbows, hips, knees, and any tight seams.
4. Remove hidden triangles that are visible outside the clothing.
5. Add hidden triangles anywhere the covered slot still pokes through.

Prefer hiding slightly more covered geometry rather than leaving tiny poke-through islands, but avoid hiding triangles that can be seen during normal poses. Note: If you have triangles that poke through a seam, it's best to fix in the source. If that's not possible, consider createing a Mesh Modifier to move the vertexes inward, and add that to the recipe also (separate process).

## 10. Save the Mesh Hide Assets

When the selection is ready, click `Create MeshHideAssets (Split by Slot)`. This button appears in the `Face Tools` panel and the `Mesh Hide Assets` panel.

Unity opens a `Save MeshHideAssets` save dialog. Choose a folder and base name.

- If selected hidden triangles belong to one slot, the stage saves one `MeshHideAsset` at the chosen path.
- If selected hidden triangles belong to more than one slot, the stage saves a `MeshHideAssetCollection` at the chosen path and creates one `MeshHideAsset` per slot next to it.
- Per-slot assets are named from the base name plus the slot name.
- The collection is updated to reference the generated per-slot Mesh Hide Assets.

For example, saving `Jacket_BodyHides.asset` with hidden faces on `HumanMaleTorso` and `HumanMaleArms` creates a collection named `Jacket_BodyHides.asset` plus per-slot assets such as `Jacket_BodyHides_HumanMaleTorso.asset` and `Jacket_BodyHides_HumanMaleArms.asset`.

If the stage was opened by dropping an existing collection, saving updates that collection. If you save over existing generated assets, Unity asks before overwriting.

Close the FaceEditor stage using the `Close` button at the top right of the panel in the scene view.

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

## Troubleshooting

- The editor opens, but the mesh is missing: rebuild the DCA first and confirm the character has a visible generated skinned mesh.
- The slot you want is not listed: the slot is not present in the current built recipe. Add the wardrobe or base recipe that contains it, then rebuild and reopen the stage.
- Painting does nothing: make sure the target slot is enabled in `Selectable Slots`. Visibility alone does not make a slot editable.
- The clothing is in the way: hide it in `Visible Wearables` or `Visible Slots`, paint the covered slot, then show the clothing again to check the result.
- A saved asset hides the wrong mesh: verify the per-slot Mesh Hide Asset was created for the slot you intended. The `AssetSlotName` must match the slot that should be hidden.
- The collection is not applied in game: make sure the collection is added to the active wardrobe recipe under `Mesh Hide Asset Collections`, then rebuild the avatar.