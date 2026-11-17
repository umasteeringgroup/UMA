# Project Asset Batch Utilities

The `Assets > UMA` context menu exposes commands based on the selected asset types. Many commands modify every selected asset or generate adjacent assets without opening a normal inspector.

## Wardrobe Recipe Commands

### Add Races to Selected Recipes

Adds checked indexed race names to every selected `UMAWardrobeRecipe`. Existing compatible races are preserved. See [Race Utilities](RaceUtilities.md).

### Add a slot to selected recipes

Select one or more wardrobe recipes or text recipes and choose `Assets > UMA > Add a slot to selected recipes`. The dialog adds one slot to every selected recipe, with the same settings the recipe editor has for a slot. Wardrobe collections and saved characters are left out of the selection, because they hold no slots of their own.

1. **Slot**: the `SlotDataAsset` to add. Its tags and races are filled in below as a start, the way the recipe editor does when a slot is added.
2. **Matching criteria**:
   - **Tags for this slot** (shown as **Match tags** for a wildcard slot, whose overlays go to every slot or overlay with one of these tags). Type a new tag, or pick one of the project's tags.
   - **Only add for these races**: the slot is used only when the character is one of these races. Leave the list empty for every race.
   - **This is a swap slot**: the slot is added only when the character already has a slot with the given tag, which it replaces.
3. **Overlays (optional)**: the overlays the slot wears, in order. Each takes **Its own color** (set with the swatch) or follows one of the shared colors listed below.
4. **Shared colors (optional)**: a name and a starting color for each. A recipe that already has a shared color of that name keeps its own, and the overlays follow that one. **Quick add** offers Hair, Skin and Eyes.
5. Press **Add to N recipes**.

A recipe that already has the slot is left alone and listed in the result, so the command can be run again safely. Recipes in the older stored formats (before version 3) are refused with a note; open such a recipe in the recipe editor and save it once first. The dialog warns when the slot or an overlay is not in the Global Library (a recipe finds its slots and overlays there by name) and when the overlays are made for different UMAMaterials.

The addition is one undo step, and only the recipes that changed are saved. Everything a recipe already holds is kept as it is, including slots and overlays the Global Library cannot find at the moment: the slot is added to the recipe's stored data directly, not by loading and saving the recipe, which would drop them.

From code: `UMARecipeSlotAdder.AddToRecipes(recipes, addition)` with a `UMARecipeSlotAddition`.

### Enable Thumbnail From Texture

Sets `thumbnailFromTexture` on every selected wardrobe recipe. It does not generate the thumbnail immediately; runtime or editor consumers use the flag when resolving recipe imagery.

### Update blank display names

Select one or more `UMAWardrobeRecipe` assets and right-click **UMA > Update blank display names**.
This fills empty or whitespace-only display names from each object's name, preserving existing display names.
Changes are saved and can be undone together. Asset filenames and recipe identifiers are unchanged.

Cleanup ignores case when removing `bb`, gender/race markers ending in `male` (including `female`),
standalone `Hair`, a leading `Hair` prefix, and trailing `_Recipe` markers. Remaining underscores become
word separators, and Unity's name formatting splits CamelCase. Gender is retained as a fallback if no
descriptive words remain; otherwise a hair-only name falls back to `Hair`.

| Object name | Display name |
| --- | --- |
| `ShortHair_Recipe` | Short Hair |
| `Hair_Straight_Recipe` | Straight |
| `bb_female_hair_recipe` | Female |
| `bb_Male_Military_Hair_Recipe` | Military |
| `female_Long_SleeveShirt_Recipe` | Long Sleeve Shirt |

Read-only assets and names with no usable words after cleanup are skipped.

### Examine Wearables and Consolidate Textures

See [Examine Wearables](ExamineWearables.md) and [Asset Consolidation and Repair](AssetConsolidationAndRepair.md).

## Create Overlay and Recipe for Base Alternates

Select one or more textures and choose `Assets > UMA > Create overlay and recipe for base alternates`.

For each texture, the tool creates an `OverlayDataAsset` and `UMAWardrobeRecipe` beside the texture. Choose:

- The `UMAMaterial` and its channel count.
- A race and wardrobe region.
- A specific base slot or a base-slot tag target.
- Shared color name.
- Optional alpha mask.

Only channel zero receives the selected texture; additional material channels are created as empty entries. Slot mode copies the selected base slot instance into the recipe. Tag mode creates a placeholder slot restricted by the selected tag and race.

Existing expected overlay or recipe filenames cause that texture to be skipped rather than overwritten.

## Manage Color Parameters

Select one or more Shared Color Table assets and choose `Assets > UMA > Manage Color Parameters`. The window adds a shader parameter to every color in those tables, or updates it where a color already has one. Use it when a shader gains a parameter, or to retune one parameter across a whole palette, so no color is left with an old value or none at all.

1. Check the tables. **Add Table** and **Add Selected** add more; **Remove** drops one.
2. Set **Material** to a material whose shader has the parameter. The window remembers it for this project (in `UserSettings`, so it is not shared through version control). **Show Hidden Parameters** also lists parameters the shader hides from the material inspector.
3. Choose the **Parameter**. Float, Range, Int, Color and Vector parameters are listed with their type.
4. Choose where the **New Value** comes from:
   - **Fixed Value**: the same value on every color. **From Material** copies the material's current value.
   - **Existing Parameter x Multiplier**: each color's own value of the **Source Parameter**, times **Multiplier**. The source is usually the parameter itself; for example `_SpecularMultiplier` x 1.2 raises every color's highlight by a fifth. A color is multiplied in red, green and blue; its alpha is kept. Float and Int parameters can feed each other; a Color or Vector needs a source of the same type.
5. **If a Color Lacks It** decides what a color without the source parameter gets: **Use Material Value x Multiplier** (the value it showed until now, since it came from the material), **Use Fixed Value** (not multiplied), or **Skip the Color**.
6. Check the **Preview**, which lists every color's current and new value and why a color is skipped, then press **Apply**.

Apply can be undone with **Edit > Undo**. It replaces every entry of the parameter on a color with a single one, leaves the color's other parameters alone, and saves only the tables that changed. Color values are stored as the tables store them, in gamma space, so a multiplier scales the value you see in the color picker.

## Create UMAMaterial from Material

Select one Unity `Material` and choose `Assets > UMA > Create UMAMaterial from Material`.

The wizard configures material type, generated texture settings, and which shader texture properties become UMA channels. The first selected texture property should normally be base color. The new `UMAMaterial` is created beside the Unity material and opened for inspection.

Use this wizard when channel selection and material type matter.

## Create UMAMaterials for Selected Materials

This bulk command creates an `UMAMaterial_...` asset beside each selected Unity Material, defaults it to Atlas, and infers channels from shader properties.

It provides no per-material review. Use the single-material wizard for custom material types, carefully ordered channels, or specialized packed maps. Always inspect inferred channel meanings afterward.

See [UMA Materials](UMAMaterial.md).

## Create DNA for Selected Modifiers

Select `MeshModifier` assets and choose `Assets > UMA > Create DNA for selected Modifiers`.

For each modifier, the command derives a DNA name, writes that name into its runtime modifiers, and creates a `DNA` asset containing a linear `DNAEffect_MeshModifier` mapping from 0 to 1.

This modifies the source MeshModifier as well as creating an asset. Check for naming collisions and inspect min/max behavior before assigning the DNA to a race. See [Mesh Modifiers](MeshModifiers.md).

## Update Selected Physics Elements

Select `UMAPhysicsElement` assets and choose `Assets > UMA > Update Selected Physics Elements`.

The window batch-remaps collider centers between axes, optionally inverts each destination axis, changes capsule alignment, and can remap box dimensions, joint axes, and swing axes. A filename prepend can rename assets; collisions are reported and skipped.

This is a coordinate-system migration tool. Test one physics asset before processing a collection, and validate collider orientation in Play Mode.

## Duplicate Race

See [Race Utilities](RaceUtilities.md). The wizard can duplicate RaceData, base recipes, blendshape defaults, T-poses, and compatibility settings.

## Convert UMAExpressionSet to UMAExpressionGroup

Converts selected legacy expression-set assets into the newer expression-group form. Keep the original until runtime expressions and network/save compatibility have been tested. See [Dynamic Expression Player](DynamicExpressionPlayer.md).

## Texture Commands

- `Open in Texture Utilities`: loads selected textures into [Texture Utilities](TextureUtilities.md).
- `Texture Tools`: opens the selected textures in [Texture Tools](TextureTools.md), which lists how overlays use each texture (by UMA Material keyword) and batch-changes import settings, checks them against the UMA Material, lowers Max Size and converts to PNG.

## Slot and Pose Commands

- `Examine Slots`: see [Examine Slots](ExamineSlots.md).
- `View and Edit weights`: opens the selected slot in Weight Touchup; process one slot at a time. See [Weight Touchup](WeightTouchup.md).
- `Extract T-Pose`: extracts pose data from selected models. See [Pose Tools](PoseTools.md).
- `Repair Text Recipe`: see [Asset Consolidation and Repair](AssetConsolidationAndRepair.md).

## General Safety Rules

1. Confirm the Project selection before opening the context menu.
2. Assume a plural command touches every compatible selected asset.
3. Commit before coordinate conversion, material migration, or generated-asset batches.
4. Inspect generated assets rather than relying only on the completion count.
5. Rebuild the UMA Global Library when new lookup assets were created.
6. Generate representative characters and run release validation.
