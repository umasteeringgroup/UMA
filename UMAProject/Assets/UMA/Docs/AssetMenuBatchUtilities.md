# Project Asset Batch Utilities

The `Assets > UMA` context menu exposes commands based on the selected asset types. Many commands modify every selected asset or generate adjacent assets without opening a normal inspector.

## Wardrobe Recipe Commands

### Add Races to Selected Recipes

Adds checked indexed race names to every selected `UMAWardrobeRecipe`. Existing compatible races are preserved. See [Race Utilities](RaceUtilities.md).

### Enable Thumbnail From Texture

Sets `thumbnailFromTexture` on every selected wardrobe recipe. It does not generate the thumbnail immediately; runtime or editor consumers use the flag when resolving recipe imagery.

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
- `Convert selected textures to PNG`: see [Asset Consolidation and Repair](AssetConsolidationAndRepair.md).

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
