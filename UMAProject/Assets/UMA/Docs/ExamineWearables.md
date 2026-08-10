# Examine Wearables

The Examine Wearables window is a batch inspection and editing tool for `UMAWardrobeRecipe` assets. Use it to review wardrobe-region assignments, assign several recipes at once, add base-slot hides or wardrobe-region suppressions, repair packed slot references, update materials, add an overlay, and validate that recipe dependencies live under an expected project folder.

For single-recipe authoring and definitions of wardrobe settings, see [Wardrobe Recipe Editor](WardrobeRecipeEditor.md).

--------------------------------------------------------------------------------

## Opening the Window

1. Select one or more `UMAWardrobeRecipe` assets in the Project window.
2. Choose `Assets > UMA > Examine Wearables`.

The menu item is available only while at least one wardrobe recipe is selected. The window keeps the recipe set that was selected when it opened. Changing the Project selection does not replace that set.

Click `Refresh` after changing compatible races, race wardrobe regions, or base race recipes. Refresh rebuilds the choices derived from those assets; it does not load a new Project selection. Close and reopen the window to examine a different set of recipes.

--------------------------------------------------------------------------------

## Selection and Filtering

The left column lists the recipes loaded into the window. Each row contains:

- A checkbox that includes the recipe in batch editing operations.
- An inspect button that selects the recipe in the Inspector.
- The recipe asset and its current wardrobe region.
- A basic packed-slot status.
- A `Repair Slots` button.

Recipes are unchecked when the window first opens. Check the recipes you intend to modify.

Two filters control which recipe rows are visible:

- `Assignment`: show all recipes, only recipes with a wardrobe region, or only unassigned recipes.
- `Wardrobe Region`: show all regions or one region currently assigned in the loaded recipe set.

The filters combine. `All Visible` and `None Visible` change only the checkboxes of rows that pass the current filters. Hidden checked recipes are not changed by these buttons.

Batch editing actions operate only on recipes that are both checked and currently visible. This prevents a filtered-out recipe from being modified accidentally. `Validate Assets` is the exception: it validates every recipe loaded into the window, regardless of its checkbox or current filters.

--------------------------------------------------------------------------------

## Assigning a Wardrobe Region

The right column shows the union of wardrobe regions declared by all compatible races used by the loaded recipes.

To assign recipes:

1. Use the filters to display the intended recipes.
2. Check those recipes, or click `All Visible`.
3. Select one wardrobe region in the right column.
4. Click `Assign`.

The tool writes the selected name to each target recipe's `wardrobeSlot` and saves the assets. It does not change compatible races or recipe contents.

If the right column is empty, verify that each recipe has a compatible `RaceData`, that the race can be resolved by the UMA Global Library, and that the race declares wardrobe regions. Then click `Refresh`.

--------------------------------------------------------------------------------

## Utilities

Open the `Utilities` foldout for batch content operations. Unless noted otherwise, utilities operate only on checked recipes that remain visible under the current filters.

### Update Hides or Suppresses on Selected Items

This utility adds visibility rules without removing any existing rules.

- `Hide Slot` adds a base slot name to each recipe's `Hides` list. The choices are collected from the base recipes of the selected recipes' compatible races. Use this when the wearable replaces or completely covers a base mesh slot.
- `Suppress Region` adds a wardrobe region name to each recipe's `suppressWardrobeSlots` list. The choices are collected from compatible races. Use this when equipping the wearable should prevent another wearable region from participating in the build.

Choose the update type and slot or region, then click `Add to Selected Recipes`.

The operation is strictly additive:

- Existing hides and suppressions are preserved.
- A value that is already present is not duplicated.
- No asset is removed from a recipe.
- The result dialog reports updated recipes and values that were already present.

Examples:

- Add the base torso slot to `Hides` for a full-body mesh that replaces the torso.
- Add `Legs` to `suppressWardrobeSlots` for a long dress that should prevent pants from being equipped underneath it.

If no base slots are available, confirm that the compatible races have valid base race recipes. If no suppressible regions are available, confirm that the compatible races declare wardrobe regions. Click `Refresh` after correcting either source.

### Set UMAMaterial by Overlay Texture Name

This utility finds overlays whose first texture name matches text you provide. Matching is case-insensitive and can use `Contains`, `Starts With`, or `Ends With`.

For every matching overlay it:

- Sets the referenced `OverlayDataAsset` to the selected `UMAMaterial`.
- Sets the containing recipe slot's alternate material to the same `UMAMaterial`.

Select an `UMAMaterial`, choose the match mode, enter `Texture[0] Match Text`, and click `Process`.

Important: an `OverlayDataAsset` is a shared project asset. Changing its material affects every recipe that uses that overlay, including recipes that were not opened in this window. Review the result dialog and use Unity Undo immediately if the match was too broad.

### Add Overlay to First Slot

Select an `OverlayDataAsset` and click `Add overlay to first slot` to append a new overlay instance to the first slot in each target recipe. Recipes with no slot are skipped and reported.

Enable `Use Shared Color` to assign the new overlay to a named shared color. Enter the shared color name and a channel count of at least one. The utility reuses an existing shared color with the exact name or creates one when needed.

This operation changes the packed recipe data and saves the recipe assets. It does not change the source `OverlayDataAsset`.

--------------------------------------------------------------------------------

## Packed Slot Inspection and Repair

Each recipe row inspects the packed slot identifiers without loading or applying the recipe. It reports `Slots look OK`, `Warning - no slots`, or the first missing slot identifier. Hover a missing status to see every missing identifier on that recipe. This inspection does not instantiate slots, so a stale recipe does not emit repeated Global Library errors merely because its row is visible.

The row belongs to the recipe asset shown immediately to its left. Recipes do not have to be checked for this read-only status inspection; the window examines every visible row. Click `Repair Slots` for a detailed view of that recipe's packed slot entries.

The repair window shows each packed slot's identifier, disabled state, placeholder state, and whether the UMA Global Library resolves the identifier to a `SlotDataAsset`.

For a missing identifier:

1. Click `Repair`.
2. If one similar indexed slot name is found, it is applied automatically.
3. If several candidates are found, select the correct slot in the candidate window.
4. Review all rows and click `Save`.

The repair window can also edit a packed slot identifier and its disabled or placeholder flags directly. These are low-level recipe fields; verify the repaired wearable on a generated avatar afterward.

`Slots look OK` means every non-placeholder packed slot identifier currently resolves through the UMA Global Library. It is not a full recipe validation: overlays, materials, textures, compatibility, and generated-avatar behavior are outside this row-level check. Use the repair window and test the wearable when a reference may have been renamed or removed.

--------------------------------------------------------------------------------

## Validate Assets

The `Validate Assets` foldout checks that dependencies referenced by the loaded recipes are stored under a chosen folder beneath `Assets`.

1. Enter an `Assets/...` folder or click `Browse`.
2. Click `Validate Assets`.
3. Review the results window if any dependency is outside the chosen folder.

The validation checks recipe references including slots, slot prefabs, overlays, overlay textures, `MeshHideAsset` assets, and `MeshModifier` assets. This is useful before exporting a package or moving a self-contained content set.

Validation always examines every recipe loaded into the Examine Wearables window. Checkboxes and the Assignment or Wardrobe Region filters do not limit validation.

An asset outside the selected folder is reported; it is not moved, copied, or deleted by this window.

--------------------------------------------------------------------------------

## Undo, Saving, and Shared Assets

Editing operations register Unity Undo, mark changed assets dirty, and save them through the Asset Database. Use Undo before making unrelated edits if a batch operation produced the wrong result.

Most actions modify only the checked recipe assets. The material-matching utility is different because it also modifies matching shared `OverlayDataAsset` assets. Source-control review is recommended after broad batch operations.

--------------------------------------------------------------------------------

## Recommended Batch Workflow

1. Select a focused set of wardrobe recipes and open Examine Wearables.
2. Filter by current assignment or wardrobe region.
3. Check only the visible recipes to change.
4. Assign the intended wardrobe region.
5. Add required base-slot hides or suppressed wardrobe regions.
6. Repair any missing packed slot identifiers.
7. Validate dependencies against the content package folder.
8. Review the changed assets in source control and test representative recipes on every compatible race.

--------------------------------------------------------------------------------

## Troubleshooting

### The Examine Wearables Menu Is Disabled

Select one or more `UMAWardrobeRecipe` assets in the Project window. Other recipe types do not enable this menu item.

### A Recipe Is Missing from the List

Set `Assignment` to `All` and `Wardrobe Region` to `All Regions`. If the recipe was not part of the Project selection used to open the window, close the window, select the intended recipes, and open it again.

### A Checked Recipe Was Not Updated

Batch edits require a recipe to be checked and visible. Clear or change the filters so the recipe is visible, then run the action again.

### Region or Base-Slot Choices Are Missing

Confirm that compatible race names resolve through the UMA Global Library. Wardrobe regions come from `RaceData.wardrobeSlots`; hideable base slots come from each race's base race recipe. Correct the race data and click `Refresh`.

### Repair Cannot Find a Replacement Slot

Make sure the intended `SlotDataAsset` is indexed in the UMA Global Library and has a recognizable name. If automatic similarity search still finds no candidate, enter the correct packed slot identifier manually and save.

### Validation Reports Dependencies Outside the Folder

The validator checks project locations, not runtime availability. Move or duplicate the dependency intentionally with the normal Unity Project workflow, update references as needed, and run validation again.
