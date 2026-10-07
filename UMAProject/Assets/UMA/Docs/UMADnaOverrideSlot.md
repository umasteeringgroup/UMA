# DNA Override Utility Slot

`UMADnaOverrideSlot` is a utility slot script for assigning DNA values and adding named blendshapes, with individual values, to a character's baked blendshape settings. It also supports weighted `UMABonePose` assets.

## Ready-to-use example

The configured example is at:

`Assets/UMA/SRP/Samples/UtilitySlots/DnaOverrides/DnaOverridesExample_slot.asset`

Its matching prefab contains the settings component. The slot's **Recipe Prepared** event calls **OnRecipePrepared**, and **DNA Applied** calls **OnDnaApplied** on that prefab component. Both listeners use **Editor And Runtime** so they also run in editor previews. You do not need to instantiate the prefab in the scene.

The example targets the supplied Human Male 3.0 content: `height = 0.6`, `OrcEars = 0.25` baked, and a subtle shoulder bone pose at weight `0.25`. Include the slot in a recipe used by the character. Enable the avatar's normal blendshape-loading option when using the bake entry; the utility does not change that policy. Other races must supply the same DNA, shape and bone names, or you should replace the example entries using a reference character.

## Create your own

1. Choose **Assets > Create > UMA > DNA Override Utility Slot** and select a location. This creates a meshless `SlotDataAsset` and a settings prefab, with both event handlers already wired.
2. Select the matching prefab to edit its **UMA Dna Override Slot** component. In an existing slot's **Slot Events** section, the persistent event target also points to that component.
3. Drag a built character into **Reference Character**. Override slot inspectors share this selection for the editor session, so adding entries or recreating the inspector retains it. The reference is only used by the editor and is not saved as a dependency. Refresh the choices after changing the character's recipe.
4. Under **Baked Blendshapes** or **DNA Overrides**, select **Add From Character**. Blendshape choices include renderer shapes, source slot shapes and Additional Blendshape Slots, so shapes already baked out of the renderer remain discoverable. Newly selected blendshapes default to `1`; DNA entries copy the reference character's current value.
5. Under **Bone Poses**, use **Add Compatible Bone Pose**, or assign a pose asset directly. The dropdown lists project poses containing bones present in the reference skeleton.
6. Edit each entry's value. UMA uses normalized values: `0.25` means 25%, `1` means 100%. Bone pose weights are clamped to 0–1 when applied. DNA and blendshape values remain editable floats to support authored ranges.
7. Drag rows to reorder them, select rows and press **−** to remove them, or use **+** to add a manual entry. Each section collapses independently. Edits support Unity Undo/Redo and normal prefab editing.
8. Add the utility slot to a base, wardrobe or additional recipe and rebuild the character.

## Event timing

The new `SlotDataAsset.RecipePrepared` event runs after predefined/restored DNA has been applied and before DCA configures DNA-driven MeshModifiers. It is dispatched in recipe slot order and skips suppressed slots. It also runs during the legacy DCA recipe-import path and after recipe assembly in `UMAAvatarBase.Load`.

`RecipeUpdated` retains its existing timing. It occurs before some DNA restoration and therefore cannot reliably enforce the final values by itself. `CharacterBegun` occurs after DCA's MeshModifier configuration, which is too late for DNA values those modifiers have already read.

The script assigns the configured DNA values in `OnRecipePrepared`, replacing previous values of matching DNA names. Both UMA 3 DNA and legacy converter DNA are supported. Unknown DNA names are ignored; the slot does not create new DNA definitions. Duplicate entries or multiple override slots are applied in order, with later assignments winning.

Each configured blendshape adds or replaces one entry in `character.blendShapeSettings.blendShapes`, setting `isBaked = true` and its individual `value`. Unrelated entries are preserved. The character must supply the shape's delta data through its normal source slots or Additional Blendshape Slots.

Bone poses run through the existing `DNAApplied` event, after the skeleton has been updated by DNA. Poses compose in list order. Missing bones are ignored by the existing pose application.

## Existing build behavior

This script changes values at recipe preparation; it does not lock values against later scripts or DNA converters. If a DNA converter also writes the same blendshape, its later assignment still applies. Configure its DNA consistently or choose a shape without a competing controller. The existing blendshape-loading, forced-bake-value and combiner policies continue to apply.

Removing the utility slot stops future assignments; it does not undo values already written to the character. Reset the relevant DNA/bake settings or load a fresh character when returning to the original appearance. Editing the prefab requires rebuilding the character's recipe; a direct DNA-only `Dirty()` update does not assemble a new recipe or fire `RecipePrepared`.

Custom recipe loaders can call `UMAData.FireRecipePreparedEvents()` after final DNA restoration and before consuming DNA for derived build settings. The event is for setting recipe values, not modifying the slot list during dispatch or queuing another build from inside the callback.

Validated against the current UMAProject checkout with **UMA NextGen 3.1f2** and **Unity 6000.3.18f1**.
