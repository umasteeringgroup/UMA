# Wardrobe Recipe Editor

The Wardrobe Recipe Editor is the inspector used for `UMAWardrobeRecipe` assets. A wardrobe recipe is a wearable UMA item: clothing, hair, beard, armor, makeup, tattoos, accessories, body-part replacements, or any other recipe that a `DynamicCharacterAvatar` can equip.

This guide is written for users who are new to UMA. It explains what each part of the editor does, how slots and overlays fit together, how shared colors work, and how the matching criteria are used during a character build.

Related docs:
- [DynamicCharacterAvatar.md](DynamicCharacterAvatar.md) for how recipes are applied at runtime.
- [SlotDataAsset.md](SlotDataAsset.md) for slot asset structure.
- [OverlayDataAsset.md](OverlayDataAsset.md) for overlay asset structure.
- [MeshHideAssets.md](MeshHideAssets.md) for triangle-level mesh hiding.
- [UMAAssetIndexer.md](UMAAssetIndexer.md) for indexing and asset lookup.

--------------------------------------------------------------------------------

## Basic Terms

- `DynamicCharacterAvatar` or `DCA`: the component that builds a UMA character from a race, wardrobe recipes, DNA, colors, and generator settings.
- `RaceData`: defines a race such as HumanMale or HumanFemale. It contains wardrobe slot names, a base race recipe, DNA information, cross-compatibility settings, and other race data.
- `UMAWardrobeRecipe`: the asset edited by this inspector. It stores wardrobe metadata plus a packed UMA recipe containing slots, overlays, shared colors, and optional DNA.
- `Wardrobe Slot`: a named region on a race, such as Hair, Beard, Chest, Hands, Legs, or Feet. DCA normally allows one non-appended wardrobe recipe per wardrobe slot.
- `SlotDataAsset`: the mesh part of a recipe. A shirt, pants mesh, hair mesh, body slot, head slot, or utility slot is represented by a slot.
- `OverlayDataAsset`: the texture/material layer applied to a slot. Overlays are stacked on slots and merged into UMA atlases or assigned as existing textures depending on the UMAMaterial.
- `UMAMaterial`: describes the texture channels and shader mapping used by slots and overlays. Slot and overlay materials should normally match.
- `Shared Color`: a named color or property definition, such as Hair, Skin, or Eyes, used by one or more overlays and optionally controlled by the DCA at runtime.
- `Matching Criteria`: slot tags, race restrictions, and swap settings used by wildcard or placeholder slots, hide tags, and swap-slot behavior.

--------------------------------------------------------------------------------

## Creating and Opening a Wardrobe Recipe

1. In the Project window, choose `Assets > Create > UMA > DCS > Wardrobe Recipe`.
2. Name the asset clearly, for example `HumanFemale_CasualShirt_Red`.
3. Select the asset to open the Wardrobe Recipe Editor in the Inspector.
4. Make sure the UMA Asset Indexer knows about your RaceData, SlotDataAsset, OverlayDataAsset, and UMAMaterial assets. If the editor shows an indexing warning, use the provided `Add to Index` button or open the UMA Global Library and rebuild/index your assets.

The editor is easiest to use when the recipe has at least one compatible race. Without a compatible race, the editor cannot build the race-specific wardrobe-slot and base-slot menus. Runtime code may allow recipes with no compatible races in some paths, but for normal authoring you should add the intended RaceData assets.

--------------------------------------------------------------------------------

## Top-Level Inspector Controls

### Save As

`Save As` duplicates the currently selected wardrobe recipe into a new `.asset` file. It copies the serialized recipe data, saves the new asset, runs UMA's recipe update step for text recipes, then selects and pings the new recipe.

Use `Save As` when you want to make a variation without modifying the original, such as a different colorway or a slightly different overlay stack.

### Recipe Type

`Recipe Type` is shown as `Wardrobe` and is read-only in this editor. It confirms that the selected recipe is a wardrobe recipe rather than a standard recipe or DCA save recipe.

### Show Help

`Show Help` toggles inline help boxes in the inspector. It is useful while learning, but the help text is short. This document explains the same controls in more detail.

### Automatic Updates

The base recipe editor shows `Automatic Updates`. When it is enabled, changes are saved back to the recipe asset as you edit. When it is disabled, the inspector shows `Save Recipe`, and you need to click it to commit changes.

--------------------------------------------------------------------------------

## Race Settings

Open the `Race Settings` foldout to define which races can use the recipe and which thumbnail to show for each race.

### Compatible Races

The compatible race list stores race names, not direct object references. The editor resolves those names through the UMA Asset Indexer.

Ways to add races:
- Drag a `RaceData` asset onto the compatible races drop area.
- Drag a folder containing RaceData assets onto the drop area; the editor scans `.asset` files in that folder and subfolders.
- Click the drop area and choose a RaceData asset from the object picker.

For each race in the list, the editor shows a read-only text field and an `X` button. The text field is read-only to avoid typos. Use `X` to remove that race from the recipe.

What compatible races do:
- They restrict which active races can equip the recipe through normal DCA wardrobe loading.
- They populate the Wardrobe Slot menu from each RaceData's `wardrobeSlots` list.
- They populate Hide Base Slots from each race's base recipe slots.
- They let the editor show race-specific thumbnails.
- They participate in cross-compatible race handling at build time.

If a compatible race is missing or not indexed, the editor shows a warning icon. A missing race means the named RaceData could not be found. An unindexed race means the RaceData exists but is not known to the UMA Asset Indexer. Use `Add to Index` for that race or fix your index setup.

### Thumbnails

Each compatible race can have a thumbnail sprite. If the recipe supports more than one race, use the thumbnail race popup to choose which race's thumbnail you are editing, then assign a `Sprite` in the object field.

Thumbnails are not required for character generation. They are intended for game UI, inventory UI, character creators, and selection screens.

--------------------------------------------------------------------------------

## Wardrobe Settings

Open the `Wardrobe Settings` foldout to configure how the item appears in the DCA wardrobe system and how it affects other slots.

### Display Value

`Display Value` is a user-facing name for the recipe. UMA does not use it to build the character. Your application can read it from the recipe and show it in UI.

Examples:
- `Casual Shirt`
- `Leather Boots`
- `Long Hair - Side Part`

### User Field

`User Field` is free-form data for your own application. UMA ignores it.

Common uses:
- Store category tags such as `rare`, `armor`, `formal`, or `shop-only`.
- Store a content pack identifier.
- Store a localization key.
- Store a gameplay item id that your inventory system understands.

### Is Appended

`Is Appended` controls whether the recipe replaces the current item in its wardrobe slot or stacks with other items in the same wardrobe slot.

When `Is Appended` is off:
- DCA stores the recipe as the active recipe for its `wardrobeSlot`.
- Equipping another non-appended recipe in the same wardrobe slot replaces it.
- This is the normal behavior for shirts, pants, shoes, hair, beards, helmets, and similar single-choice wardrobe regions.

When `Is Appended` is on:
- DCA stores the recipe in the additive stack for that wardrobe slot.
- Multiple appended recipes can be active in the same wardrobe slot.
- Removing the base item for that slot does not automatically mean every appended item is removed unless your code does that.

Good appended use cases:
- Tattoos or decals.
- Makeup overlays.
- Scars, dirt, blood, body paint, or skin details.
- Jewelry or accessories that should stack.
- Small add-on pieces that should not replace the main wardrobe item.

Be careful using appended recipes for full clothing meshes. If two appended items include overlapping meshes, both meshes can render and cause poke-through or duplicated geometry.

### Wardrobe Slot

`Wardrobe Slot` assigns the recipe to a race-defined wardrobe region. The available values come from the compatible RaceData assets.

Typical examples:
- `Hair`
- `Beard`
- `Helmet`
- `Chest`
- `Hands`
- `Legs`
- `Feet`

If more than one compatible race is selected, the editor merges their wardrobe slot lists. Slots that are available only on some of the compatible races are labeled with the race names, for example `Tail (Dragon Only)`. A recipe assigned to a slot that a race does not have will not work as expected for that race.

For normal wardrobe items, do not leave this as `None`. DCA refuses to set recipes whose wardrobe slot is `None` through the normal `SetSlot` path.

### Hide Base Slots

`Hide Base Slots` hides entire base recipe slots when this wardrobe recipe is active. The list is generated from the compatible races' base recipes.

Use this to prevent base-body poke-through when the clothing fully covers a body part.

Examples:
- Gloves hide the base hand slots.
- Boots hide the base foot slots.
- A helmet hides or replaces a hair slot.
- A full-body suit hides torso, arms, and legs base slots.

Controls:
- Toggle a generated base slot to add or remove it from the recipe's `Hides` list.
- Use `Add Base Slot Asset > Select` to add a SlotDataAsset by object picker when the slot is not in the generated race list.
- The `Selected` field shows the stored slot names.

Important distinction:
- `Hide Base Slots` hides whole slots by slot name.
- `MeshHideAsset` hides selected triangles within slots.
- `Tags to Hide` hides slots or overlays by tag.

Use whole-slot hides when the entire base slot should disappear. Use Mesh Hide Assets when only part of the slot should be hidden.

### Replaces

`Replaces` replaces one base slot with a slot from this recipe while preserving the original base slot's overlays.

This is different from `Hide Base Slots`:
- `Hide Base Slots` removes the base slot completely.
- `Replaces` swaps the base slot geometry but keeps its existing overlays and then adds the replacement slot's overlays.

Good use cases:
- Replace the default head with a high-poly head while keeping skin, face, or complexion overlays.
- Replace a base body part with alternate geometry that should still use the base color and overlay stack.

Guidelines:
- A replacement recipe should usually contain one SlotDataAsset: the replacement slot.
- Pick the base slot you want to replace from the popup.
- Do not use `Replaces` for normal clothing that sits over the body. Use a regular wardrobe slot plus hides or Mesh Hide Assets instead.

For cross-compatible recipes, DCA can look up equivalent base slots on the active race before replacing.

### Mesh Modifications

`Mesh Modifications` adds more advanced mesh changes that should be active while this wardrobe recipe is equipped.

Controls:
- `Add Mesh Hide Asset`: pick an individual `MeshHideAsset`.
- `Add Mesh Modifier`: pick a `MeshModifier` asset.
- Drop area: drag Mesh Hide Assets, Mesh Hide Asset Collections, Mesh Modifiers, or folders containing mesh hide assets.
- Lists: inspect or remove assigned Mesh Hide Assets, Mesh Hide Asset Collections, and Mesh Modifiers.

Mesh Hide Assets:
- Hide selected triangles on a specific slot.
- Are usually used to prevent body or inner clothing poke-through under a garment.
- Can be grouped in a Mesh Hide Asset Collection for multi-slot garments.
- Are processed during DCA build unless `ignoreMeshHideAssets` is enabled on the avatar.

Mesh Modifiers:
- Apply mesh modifier runtime data by slot name.
- Are accumulated from the base recipe and active wardrobe recipes.
- Should match the slot they are meant to modify.

For detailed Mesh Hide authoring, see [MeshHideAssets.md](MeshHideAssets.md).

### Wardrobe Slots To Suppress

`Wardrobe Slots to Suppress` hides other wardrobe recipes while this recipe is active.

This is wardrobe-level suppression, not mesh-level hiding. It prevents recipes assigned to other wardrobe slots from being included in the build.

Example uses:
- A long dress assigned to `Chest` suppresses `Legs` so pants or shorts are not shown underneath.
- A full outfit suppresses `Chest`, `Waist`, `Legs`, `Hands`, and `Feet` items.
- A helmet suppresses `Hair`.

Controls:
- Existing suppressed slots are listed with `X` buttons.
- Use `Add Wardrobe Slot` to choose a race wardrobe slot and click `Add Slot`.

Suppression is based on wardrobe slot names. If a race does not define that wardrobe slot, suppressing it has no effect for that race.

### Override DNA

`Override DNA` applies temporary DNA values while this wardrobe recipe is equipped.

How it works:
- Pick a compatible race in the first popup.
- Pick one DNA name from that race in the second popup.
- Click `Add DNA`.
- Adjust the slider from `0.0` to `1.0`.
- During DCA build, active wardrobe Override DNA is accumulated and applied.
- After the build's DNA update sequence, the previous DNA values are restored.

Use cases:
- A bulky armor piece slightly changes shoulder or chest proportions.
- A high heel shoe changes foot pose or leg proportions.
- A helmet squashes or hides a hair shape through DNA.

Override DNA is not a substitute for mesh hiding or correct mesh fitting. Use it when the race's DNA is designed to support the shape change.

### Tags To Hide

`Tags to Hide` stores a list of tags that are removed or hidden while this wardrobe recipe is active.

At build time:
- DCA removes overlays whose overlay tags match any tag in this list.
- DCA hides slots whose slot tags match any tag in this list.

Example uses:
- A full-face mask hides overlays tagged `makeup` or `facial_hair_detail`.
- Gloves hide overlays tagged `hand_tattoo`.
- A garment hides a body detail slot tagged `body_detail`.
- A Helmet that covers the whole head will hide all slots with the "Head" tag - eyes, head, face, inner mouth, beard

Tag matching is exact. Keep spelling and capitalization consistent across SlotDataAsset tags, OverlayDataAsset tags, and wardrobe recipe hide tags.

### Incompatible Recipes

`Incompatible Recipes` is a manual list of other `UMAWardrobeRecipe` assets that should not be used with this recipe.

Important: UMA does not automatically enforce this list in the wardrobe build. The tooltip and help text in the editor are literal: it is up to your application, UI, inventory system, or game rules to check this list and prevent incompatible combinations.

Use it as metadata for systems such as:
- Character creator UI filtering.
- Inventory equip validation.
- Random outfit generation rules.
- Mod/content validation tools.

--------------------------------------------------------------------------------

## Slots Tab Overview

The lower part of the inspector uses the standard recipe toolbar. For wardrobe authoring, most work happens on the `Slots` tab.

The `Slots` tab contains:
- `Shared Colors & Properties`.
- A main drag/drop area for slots, overlays, and recipes.
- `Add Base slot` popup when compatible race base slots are known.
- `Add Slot` object field.
- `Add Placeholder Slot` button.
- Recipe utilities such as `Clear Recipe`, `Remove Nulls`, `Collapse All`, `Expand All`, `Select All Slots`, and `Select All Overlays`.
- One foldout per slot in the recipe.

The `DNA` tab is the standard recipe DNA editor. For most wardrobe work, use `Override DNA` in Wardrobe Settings instead of putting general DNA data into the wardrobe recipe.

--------------------------------------------------------------------------------

## Adding SlotDataAssets

A SlotDataAsset contributes mesh geometry to the recipe. A clothing item with geometry needs at least one slot.

### Method 1: Drag Into The Main Drop Area

1. Open the `Slots` tab.
2. Drag one or more `SlotDataAsset` assets into the drop area labeled `Drag Slots, Overlays or Recipes here. Click to pick`.
3. The editor creates `SlotData` instances and merges them into the recipe.

If you drag a folder, the editor scans `.asset` files under that folder and subfolders for SlotDataAssets and OverlayDataAssets.

### Method 2: Click The Main Drop Area

1. Click the main slot/overlay/recipe drop area.
2. Choose a `SlotDataAsset` in the object picker.
3. The editor adds that slot to the recipe.

### Method 3: Use Add Slot

1. Open the `Slots` tab.
2. Use the `Add Slot` object field.
3. Assign a `SlotDataAsset`.

This is the most explicit way to add one slot.

### Method 4: Use Add Base Slot

When compatible races are set, the editor can show `Add Base slot`. This popup lists base recipe slots from the compatible races.

Use it when:
- You want to include a race base slot in the wardrobe recipe.
- You are building a replacement-style recipe.
- You need a known base slot as the target for overlays or testing.

For baked blendshape races, the editor may resolve or generate a baked version of the base slot when needed.

### Method 5: Add A Placeholder Slot

`Add Placeholder Slot` creates a slot with no backing SlotDataAsset. It exists only as a wildcard carrier for overlays.

Use placeholder slots for overlay-only recipes that should attach to matching tagged slots at build time.

Example:
1. Add a placeholder slot.
2. Expand it.
3. In `Matching Criteria`, add a tag that also exists on the target body slot, such as `Body` or your project's equivalent.
4. Add one or more OverlayDataAssets to the placeholder.
5. At build time, the placeholder's overlays are added to final slots whose tags match.

Placeholder slots with no tags are highlighted because they cannot match anything useful.

--------------------------------------------------------------------------------

## Slot Foldout Controls

Each slot appears as a foldout bar. For normal slots, the bar shows the slot name and backing asset name. For placeholder slots, it shows that the slot is a placeholder wildcard.

Slot header controls:
- `Asset`: pings and inspects the SlotDataAsset. Placeholder slots do not have a backing asset.
- `x`: removes the slot from the recipe.

### Disable In Recipe

`Disable in recipe` keeps the slot data in the recipe but marks it disabled. Disabled slots are treated as hidden during DCA post-processing.

Use this when you want to keep work in progress in the recipe without letting it render.

### Utilities

The `Utilities` foldout appears for normal slots.

`View copied data` shows data copied from the SlotDataAsset into this recipe instance:
- Overlay scale.
- Matching tags.
- Matching races.

`Refresh slot from Asset` reloads the SlotDataAsset and updates the recipe's SlotData from the asset. Use this after editing the SlotDataAsset if the recipe still has old copied data.

`Update Overlay UMA Material` lets you drag a `UMAMaterial` onto a drop area to assign that UMAMaterial to the slot's overlays and resize overlay texture lists to match its channel count. This changes the underlying OverlayDataAsset material data, so use it carefully if the overlay asset is shared by other recipes.

### Clipping Parameters

If the slot asset is a clipping plane, the editor shows clipping parameters.

Controls include:
- `Smoosh Amount`: distance used to push target geometry.
- `Smoosh Buffer`: ease-in distance for smoothing.
- Invert toggles for distance and axes.
- Override target tags for finding target and smooshable slots.

Clipping plane slots are utility slots and are not rendered normally.

### Additional Blendshape Slots

This foldout lets you add additional slots that provide blendshapes for the current slot.

The added slot must:
- Have the same vertex count as the target slot.
- Contain blendshapes.
- Not already be listed as an additional blendshape slot for this target.

This is an advanced workflow for slots that need extra blendshape sources.

### Expand Along Normal

`Expand Along Normal` offsets a slot along its normals in very small units. Use it to reduce z-fighting when two surfaces are very close.

Use small values. If a slot floats visibly above the body or clothing, the value is too high or the mesh needs adjustment.

### Add Slot Inside A Slot

The `Add Slot` field inside a slot foldout creates another SlotDataAsset entry that shares the current slot's overlay list. This is useful when multiple mesh slots should use the same overlays.

When overlay lists are shared, the editor may show `Shared Overlays` instead of individual overlay editors for one of the slots.

### Remap UV To Main

`Remap UV to Main` can remap a slot to use an alternate UV set (`UV Set 2`, `UV Set 3`, or `UV Set 4`) as the main UV set for this recipe instance.

Use this only when the mesh and overlays were authored for that alternate UV workflow.

--------------------------------------------------------------------------------

## Adding And Stacking OverlayDataAssets

An OverlayDataAsset provides one or more textures and material data for a slot. Overlays are stored in a list on each slot.

### Add An Overlay To A Specific Slot

1. Open the `Slots` tab.
2. Expand the slot that should receive the overlay.
3. Use the slot's `Add Overlay` object field.
4. Assign an `OverlayDataAsset`.
5. The overlay appears as a foldout under the slot.

This is the safest and clearest method because you decide exactly which slot receives the overlay.

### Drag Overlays Into The Main Drop Area

You can drag OverlayDataAssets into the main drop area.

Behavior:
- If you drag one overlay together with one or more slots, the editor adds each dragged slot and gives each slot that overlay.
- If you drag overlays but no slots, the editor adds the overlays to the first non-null slot in the recipe.
- If the recipe has no slots, the editor cannot apply the overlay.

For beginners, add the slot first, expand it, then use the slot's `Add Overlay` field. It avoids accidentally adding an overlay to the wrong slot.

### Stack Multiple Overlays

Add more than one overlay to the same slot to stack them.

Overlay order matters:
- The first overlay is normally the base overlay for that slot.
- Later overlays are layered over earlier overlays.
- Texture blending uses each overlay's per-channel blend mode when the UMAMaterial type supports blending.
- The up and down arrow buttons in the overlay header move overlays in the stack.

Typical stack examples:
- Body slot: base skin overlay, then tattoo overlay, then dirt overlay.
- Face slot: base complexion, then makeup, then scar.
- Clothing slot: base fabric, then decal, then damage/wear overlay.

### Overlay Header Controls

Each overlay foldout header shows the overlay name, UMAMaterial name, and material render queue.

Controls:
- `Inspect`: select and inspect the OverlayDataAsset.
- `Mat`: select and inspect the Unity material used by the overlay's UMAMaterial.
- `UMat`: select and inspect the UMAMaterial asset.
- Up/down arrow buttons: move the overlay earlier or later in the stack.
- `x`: remove the overlay from this slot.

### Position Overlay

`Position Overlay...` opens the Overlay Positioner. It lets you adjust an overlay's atlas rectangle and instance transform against a selected base overlay preview.

Use it when:
- A tattoo, decal, makeup texture, or detail overlay is not aligned with the target slot.
- An overlay needs per-recipe positioning rather than a global OverlayDataAsset rect change.

Be careful with rotation, scale, and translation. Overlay transforms can write outside expected texture bounds if moved too far.

### Overlay Material Mismatch Warnings

The overlay editor compares the overlay UMAMaterial to the slot material.

If the UMAMaterial names differ but channel counts match, the editor offers `Copy Slot Material to Overlay`. This changes the overlay asset's UMAMaterial to match the slot.

If channel counts do not match, fix the SlotDataAsset, OverlayDataAsset, or UMAMaterial manually. A channel mismatch means the overlay's textures and color data do not line up with the slot's material channels.

### Overlay Tags

Each overlay has a `Tags` section.

Use overlay tags when you want another recipe's `Tags to Hide` list to remove specific overlays without hiding the whole slot.

Example:
- A face overlay is tagged `makeup`.
- A mask wardrobe recipe has `makeup` in `Tags to Hide`.
- When the mask is equipped, DCA removes the tagged makeup overlay from its slot during post-processing.

### Overlay Textures

The overlay editor shows a texture preview for each UMAMaterial channel.

You can:
- Drag a texture onto a preview.
- Click `Select` on a preview and choose a texture.
- Click an existing texture preview to ping the texture in the Project window.

The label above each texture preview comes from the UMAMaterial channel's material property name. For example, a channel named `_BaseMap` or `_MainTex` is shown without underscores in the compact texture preview UI.

Important: changing a texture here persists the change to the underlying OverlayDataAsset. If that OverlayDataAsset is used by other recipes, they will see the same texture change. Duplicate the OverlayDataAsset first when you need recipe-specific textures.

### Blend Modes And Tiling

For `Atlas` and `NoAtlas` UMAMaterials, each channel preview shows an overlay blend mode. Blend modes include `Normal`, `Multiply`, `Overlay`, `Screen`, `Darken`, `Lighten`, `ColorDodge`, `ColorBurn`, `SoftLight`, `HardLight`, and `Subtract`.

For `UseExistingTextures` UMAMaterials, each channel preview shows a `Tile` toggle, and the overlay editor can show `UV Set for this overlay`.

Use blend modes for texture layering. Use tiling and UV set controls only when the material and mesh were authored for existing-texture workflows.

--------------------------------------------------------------------------------

## Shared Colors And Properties

`Shared Colors & Properties` is at the top of the `Slots` tab. It defines named colors and material property data used by overlays in the recipe.

Shared colors are one of UMA's most important concepts. A shared color lets several overlays use one named color definition. DCA can then set that color by name at runtime.

Example:
- The hair overlay uses shared color `Hair`.
- Eyebrow and beard overlays also use shared color `Hair`.
- The DCA or UI sets `Hair` to black.
- Hair, eyebrows, and beard update together.

### Add Shared Color

`Add Shared Color` creates a named `OverlayColorData` with color channels.

If the recipe has no shared colors yet, choose the channel count from the `Channels` popup before clicking the button. If the recipe already has shared colors, new shared colors use the first shared color's channel count.

The new shared color is named `Shared Color N`. Rename it to something meaningful, such as `Hair`, `Skin`, `Eyes`, `Cloth`, `Leather`, or `MetalTrim`.

### Add Shared Color Parms

`Add Shared Color Parms` creates a zero-channel shared color. It has no color multiplier/additive channels. It is used as a named material-property carrier.

Use this when an overlay has no textures but should carry shader properties or runtime material parameters through a named shared-color entry.

### Quick Pick

The `Quick pick` row can add common shared color names:
- `Hair`
- `Skin`
- `Eyes`

These are only convenience names. Your project can use any consistent shared color names, but spelling and capitalization must match between the recipe, overlays, DCA color list, and code.

### Save Collection

`Save Collection` marks the recipe changed so the current shared color collection is saved with the recipe.

### Shared Color Fields

Each shared color has a foldout with these fields.

`Name`:
- The shared color key.
- This is how overlays and DCA runtime color APIs find the color.
- Names are case-sensitive in practical use. Treat `Hair`, `hair`, and `HAIR` as different names.

`Channels`:
- The number of UMA material channels controlled by this color entry.
- Valid editor values are 1 through 16.
- For normal overlays, this should match the UMAMaterial channel count used by the overlay.

`Color Multiplier`:
- The multiplier/tint for channel 0.
- White means unchanged.
- Black removes that channel's contribution.
- Tinted colors multiply the source texture or material channel by that color.

`Color Additive`:
- The additive color for channel 0.
- Transparent black means no additive contribution.
- Use additive color sparingly to brighten or shift a channel after multiplication.

`Texture N multiplier` and `Texture N additive`:
- Per-channel multiplier and additive colors for channel indexes after channel 0.
- `Texture 1` means channel index 1, `Texture 2` means channel index 2, and so on.
- The channel meaning comes from the UMAMaterial channel list. For one material, channel 1 might be a normal map; for another, it might be metallic or mask data.

Do not assume every channel should be tinted. Normal maps and packed mask textures usually should stay at their defaults unless the material was designed for channel tinting.

### Color Channels In Plain Language

Each overlay has two color arrays:
- `channelMask`: the multiplier colors.
- `channelAdditiveMask`: the additive colors.

The merge process uses these per channel. A simple mental model is:

```text
final channel color = source texture color * multiplier + additive
```

The actual shader and merge path can be more complex, especially for normal maps, material colors, and custom UMAMaterials, but this model is useful for authoring.

Good defaults:
- Multiplier: white.
- Additive: transparent black.

Common choices:
- Skin, hair, cloth, leather, and eye color usually use multiplier colors.
- Glow, makeup brightness, or stylized effects may use additive colors if the material supports that look.
- Packed masks and normal maps usually keep defaults.

### Per-Overlay Color vs Shared Color

When an overlay is expanded, the color section shows either shared color selection or direct color fields.

If `Use Shared Color` is enabled:
- The overlay points at one shared color from the recipe.
- The overlay no longer stores independent color values.
- Any overlay using the same shared color changes together.
- DCA can override that shared color by name at runtime.

If `Use Shared Color` is disabled:
- The overlay uses its own color data.
- It is saved only with that overlay instance in the recipe.
- DCA runtime shared color changes will not affect it by name.

Use shared colors for things players or code should control, such as hair, skin, eyes, fabric color, dye channels, or decal color. Use per-overlay colors for fixed details that should not be changed globally.

### Show Extended Ranges

For non-shared overlay colors, `Show Extended Ranges` changes multiplier color editing from color pickers to vector fields for multiplier channels. This allows values outside the usual color picker range.

Use extended ranges only for materials that intentionally support values above 1.0 or below the usual range, such as special effects or emission-style workflows. For ordinary tinting, leave this off.

### Shader Property Blocks

A shared color can include a `Shader Property Block`. This stores material properties by shader property name.

Add it by expanding a shared color and clicking `Add Shader Property Block`. Remove it with `Remove Shader Property Block`.

The property block editor includes:
- `Shader Properties`: the list of property entries.
- `Always Update`: tells DCA to copy this recipe color/property data into the avatar color list during color updates instead of letting an existing avatar color override it.
- `Parms Only`: tells DCA to update property block parameters from the recipe while preserving the avatar's color channel values.
- `Template Material`: lets you drop a Unity `Material` or `SkinnedMeshRenderer` and copy supported material properties.
- `Add Type`: adds a new property of the selected UMA property type.

Property types include:
- `UMAFloatProperty`: sets a shader float.
- `UMAIntProperty`: sets a shader int.
- `UMAColorProperty`: sets a shader color.
- `UMAVectorProperty`: sets a shader vector.
- `UMATextureProperty`: stores a texture property, but in the editor its value is not directly edited here.
- `UMAOverlayTransformProperty`: stores overlay translation, rotation, and scale. DCA applies these back to overlay instance transform fields during color updates.
- Array, matrix, and compute-buffer properties exist, but many are intended to be set programmatically.

Shader property names are exact and case-sensitive. If a shader expects `_Color`, use `_Color`, not `_color` or `Color`. This is especially important for UMA's cross-compatible shaders and custom URP/Built-in shaders.

Use the `Template Material` section when possible. It can copy Color, Vector, Float, Range, and Int properties from an existing Unity Material and avoids typing mistakes.

### Empty Overlays And Property Names

Some OverlayDataAssets have no textures. In code, these are `isEmpty` overlays. They do not add textures to an atlas, but they can carry named shader properties.

When an empty overlay is expanded, the color UI looks for zero-channel shared colors created with `Add Shared Color Parms`. If any exist, the overlay shows `Select property name` so you can associate that empty overlay with one of those property-only shared colors.

Use this for workflows where a wardrobe recipe needs to drive shader properties without adding a texture overlay.

--------------------------------------------------------------------------------

## Matching Criteria

The `Matching Criteria` foldout appears inside each slot foldout. It stores per-recipe slot tags, optional race restrictions, and swap-slot behavior.

Matching criteria are not the same thing as the top-level `Compatible Races` list:
- Top-level compatible races say which races can equip the wardrobe recipe.
- Slot matching criteria say how a specific slot behaves during DCA post-processing.

### Slot Tags

For normal slots, the foldout says `Edit tags for this slot`. For wildcard or placeholder slots, it says `Match Tags`.

Slot tags are copied from the SlotDataAsset when the slot is added, but the recipe stores its own instance copy. Editing tags here changes this recipe's SlotData, not necessarily the original SlotDataAsset.

Slot tags are used by:
- Placeholder and wildcard slots to find target slots for overlays.
- `Tags to Hide` to hide whole slots by tag.
- Swap slots to find slots with a swap tag.
- Your own code or tools if they inspect recipe slot tags.

Tag matching is exact. Establish project naming conventions early. For example, use either `Body` or `body`, not both.

### Placeholder And Wildcard Matching

A placeholder slot has no mesh. A wildcard slot is a slot asset marked as a wildcard. Both are treated as overlay carriers during DCA post-processing.

Build behavior:
- DCA collects placeholder and wildcard slots.
- If a wildcard or placeholder has race restrictions, DCA only processes it for a matching active race.
- DCA compares its match tags against normal slots in the final recipe.
- If a normal slot has one of the tags, the wildcard or placeholder overlays are added to that slot.
- The placeholder or wildcard carrier slot itself is not kept as a normal rendered mesh slot.

Use this for overlay-only recipes that should work across several races or slot assets.

Example: tattoo recipe
1. Add a placeholder slot.
2. Add match tag `Body`.
3. Add a tattoo OverlayDataAsset to the placeholder.
4. Make sure each race's body slot has tag `Body` or add the correct tags in this recipe.
5. Equip the wardrobe recipe. The tattoo overlay is added to matching body slots.

If a placeholder slot has no tags, it has nothing to match. The editor highlights this condition.

### Only Add For These Races

The race list in Matching Criteria restricts the slot's wildcard/placeholder behavior to specific active races.

Controls:
- Choose a race from the popup of indexed RaceData names.
- Click `Add Race`.
- Remove race entries with `X`.

If the race list is empty, there is no per-slot race restriction.

This is most useful for wildcard and placeholder slots. It prevents one overlay carrier from applying to races where its target tags mean something different or where the overlay texture does not fit.

### This Is A Swap Slot

`This is a swap slot` marks a slot as a conditional replacement.

Build behavior:
- Swap slots are hidden by default.
- DCA searches for another slot with the configured `swapTag`.
- If a target slot has that tag, the target slot is hidden and the swap slot is shown.
- Overlays that came from the swap slot can be unsuppressed when the target is swapped.

Use swap slots for advanced replacement logic where the presence of a tagged slot should activate an alternate slot.

For beginners, prefer ordinary wardrobe slot replacement, `Hide Base Slots`, or `Replaces` unless you specifically need tag-driven conditional swapping.

--------------------------------------------------------------------------------

## Common Authoring Workflows

### A Mesh Clothing Item

Use this for shirts, pants, boots, gloves, armor, helmets, and similar items.

1. Create a wardrobe recipe asset.
2. Add the compatible RaceData assets in `Race Settings`.
3. Set a useful `Display Value`.
4. Set the `Wardrobe Slot`, for example `Chest` or `Feet`.
5. Leave `Is Appended` off unless the item is meant to stack.
6. In `Hide Base Slots`, hide base body slots covered by the item.
7. Add Mesh Hide Assets if only parts of base slots should be hidden.
8. Open the `Slots` tab and add the clothing SlotDataAsset.
9. Expand the slot and add its base OverlayDataAsset.
10. Add any additional detail overlays and order them correctly.
11. Set shared colors or per-overlay colors.
12. Test on a DCA with the target race and animations that stress the clothing fit.

### An Overlay-Only Tattoo Or Makeup Recipe

Use this when no new mesh is needed.

1. Create a wardrobe recipe asset.
2. Add compatible races.
3. Set an appropriate wardrobe slot. For stacked tattoos or makeup, consider setting `Is Appended` on.
4. Open the `Slots` tab.
5. Click `Add Placeholder Slot`.
6. Expand the placeholder.
7. In `Matching Criteria`, add a tag that exists on the target slot, such as your project's body, face, or head tag.
8. Add the tattoo or makeup OverlayDataAsset to the placeholder.
9. Use `Position Overlay...` if the overlay needs alignment.
10. Use shared colors if players should recolor it.
11. Test across all compatible races.

### A Color-Variant Recipe

Use this when the mesh and textures are the same, but the colors differ.

1. Use `Save As` to duplicate a working recipe.
2. Rename the duplicate.
3. Change shared color default values or per-overlay colors.
4. Keep shared color names consistent if DCA runtime color controls should continue to work.
5. Change `Display Value` and thumbnail to reflect the variant.

If the texture itself changes, duplicate the OverlayDataAsset before editing texture previews unless every recipe using that overlay should change.

### A Full Outfit That Suppresses Other Items

Use this for dresses, robes, full armor sets, or one-piece outfits.

1. Assign the recipe to a primary wardrobe slot, such as `Chest` or a custom `FullOutfit` slot.
2. Add every mesh slot and overlay needed by the outfit.
3. Add relevant base slot hides and Mesh Hide Assets.
4. In `Wardrobe Slots to Suppress`, add the wardrobe slots that should not render with this outfit, such as `Legs`, `Waist`, `Hands`, or `Feet`.
5. Test with other wardrobe items equipped to make sure they are suppressed as expected.

### A Base Slot Replacement

Use this for replacing a body part while preserving the original overlays.

1. Add compatible races.
2. Set `Replaces` to the base slot name to replace.
3. Add one replacement SlotDataAsset to the recipe.
4. Add overlays that belong specifically to the replacement slot.
5. Do not hide the same base slot unless you have a specific reason. `Replaces` handles the swap.
6. Test skin, complexion, and other base overlays to confirm they remain intact.

--------------------------------------------------------------------------------

## Testing A Wardrobe Recipe

1. Make sure all RaceData, SlotDataAsset, OverlayDataAsset, UMAMaterial, texture, Mesh Hide, and Mesh Modifier assets are indexed or otherwise loadable.
2. Add the recipe to a DCA through the DCA inspector, a wardrobe collection, or code.
3. Rebuild the DCA.
4. Check the console for missing asset, material mismatch, or channel mismatch warnings.
5. Inspect the generated character for:
   - Missing mesh slots.
   - Wrong overlay order.
   - Texture channel mismatch.
   - Poke-through.
   - Hidden slots that should still be visible.
   - Suppressed wardrobe items that should still be active.
   - Shared colors not responding to DCA color controls.
6. Test all compatible races, especially if the recipe uses cross-compatible race data, placeholder slots, or race-specific wardrobe slots.
7. Test animations, not only the bind pose. Poke-through often appears only when joints bend.

--------------------------------------------------------------------------------

## Troubleshooting

### The Wardrobe Slot Menu Is Empty Or Says No Compatible Races

Add at least one compatible RaceData in `Race Settings`. The editor gets wardrobe slot names from compatible races.

### A Race Shows A Warning Icon

The RaceData is missing or not indexed. If the asset exists, click `Add to Index` or rebuild the UMA Asset Indexer. If it was deleted or renamed, remove the old race entry and add the correct RaceData.

### The Recipe Will Not Equip On A DCA

Check:
- `wardrobeSlot` is not `None`.
- The active DCA race is in `compatibleRaces`, or the active RaceData is cross-compatible with one of those races.
- The recipe is indexed or otherwise loadable.
- Your code is not blocking it through incompatible-item logic.

### The Clothing Appears But The Body Pokes Through

Use one or more of these:
- Hide entire covered base slots in `Hide Base Slots`.
- Add Mesh Hide Assets for triangle-level body hiding.
- Adjust the clothing mesh or skin weights.
- Use small `Expand Along Normal` values only for minor z-fighting.

### An Overlay Is On The Wrong Slot

If you dragged overlays into the main drop area, they may have gone to the first slot. Remove the overlay, expand the intended slot, and add it with that slot's `Add Overlay` field.

For placeholder overlays, verify that the placeholder match tags match the target slot tags.

### Shared Color Changes Do Nothing

Check:
- The overlay is set to `Use Shared Color`.
- The selected shared color name matches the DCA color name exactly.
- The shared color has the correct channel count.
- The overlay is not using an independent per-overlay color.
- Your code calls `UpdateColors(true)` or otherwise triggers a texture update when changing colors at runtime.

### A Texture Change Affected Other Recipes

Texture previews edit the OverlayDataAsset itself. If several recipes use that overlay asset, all of them see the change. Duplicate the OverlayDataAsset for recipe-specific texture variants.

### Incompatible Recipes Are Still Equipping Together

The `Incompatible Recipes` list is metadata only. Add enforcement in your UI, inventory, randomizer, or equip code.

### Hide Tags Removed Too Much

Tags are exact, but they can be reused in many assets. Inspect slot tags and overlay tags in the affected recipe. Use more specific tag names if needed, such as `face_makeup` instead of `detail`.

### Overlay Material Does Not Match Slot Material

The overlay's UMAMaterial should usually match the slot's UMAMaterial. If the channel count is the same, the editor can copy the slot material to the overlay. If the channel count differs, fix the assets manually so textures, color channels, and materials line up.

--------------------------------------------------------------------------------

## Quick Reference

- Compatible Races: which races can use this recipe and which race data populates editor menus.
- Display Value: friendly UI name.
- User Field: application-defined metadata.
- Is Appended: stack instead of replacing the active recipe in the same wardrobe slot.
- Wardrobe Slot: DCA wardrobe region this recipe occupies.
- Hide Base Slots: hide whole base recipe slots by slot name.
- Replaces: swap one base slot for this recipe's slot while preserving base overlays.
- Mesh Modifications: triangle hides and mesh modifiers active with this recipe.
- Wardrobe Slots to Suppress: prevent other wardrobe regions from rendering while this recipe is active.
- Override DNA: temporary DNA values applied while this recipe is equipped.
- Tags to Hide: hide matching slot tags and remove matching overlay tags.
- Incompatible Recipes: manual metadata for your own enforcement.
- Add Slot: add mesh geometry to the recipe.
- Add Placeholder Slot: add an overlay carrier with no mesh.
- Add Overlay: add a texture/material layer to a slot.
- Shared Colors: named color/property definitions that overlays can share and DCA can control.
- Matching Criteria: slot tags, race filters, and swap behavior used during build post-processing.