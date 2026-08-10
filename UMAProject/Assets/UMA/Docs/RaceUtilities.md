# Race Utilities

UMA includes several batch and migration tools for `RaceData` assets and compatible wardrobe recipes. For normal race authoring fields, see [RaceData](RaceData.md) and [Creating a New Race](CreatingANewRace.md).

## Duplicate Race

Select a `RaceData` and choose `Assets > UMA > Duplicate Race`.

The four-step wizard can create or overwrite:

- A duplicated `RaceData` with a new runtime race name.
- A duplicated base race recipe when the source has one.
- A generated T-pose derived from the source T-pose.

The wizard scans source base slots for unique blendshape names. Selected names and non-zero defaults are stored in the duplicated race's prebaked blendshape data.

When generating a T-pose, checked mixer-enabled `UMABonePose` assets are applied in list order using their percentages. The source T-pose remains unchanged. When generation is disabled or unavailable, the duplicated race keeps the copied source T-pose reference.

Cross Compatibility writes the chosen race names into the duplicate's cross-compatibility settings. Selecting a race does not generate slot conversion data by itself; verify compatible slot mappings separately.

The Summary page previews output paths and overwrite warnings. Existing compatible target assets require confirmation. Newly created race, recipe, and T-pose assets are added to the UMA Global Library when the indexer is available.

Recommended workflow:

1. Commit the source race and recipe.
2. Use unique destination names.
3. Select only blendshapes intended as defaults.
4. Apply T-pose mixers in deliberate order.
5. Review cross-compatibility as metadata, not proof of mesh compatibility.
6. Run the Race Smoke Test on the duplicate.

## Add Races to Selected Recipes

Select one or more `UMAWardrobeRecipe` assets and choose `Assets > UMA > Add Race(s) to Selected Recipes`.

The window lists indexed `RaceData` assets. Checked races are added to every target recipe's `compatibleRaces` list. Existing entries are preserved and duplicate race names are skipped. The operation does not remove compatibility, assign wardrobe regions, or convert slot geometry.

If the list is empty, rebuild or repair the Global Library.

## Race Updater

Open `UMA > Editors > Race Updater`.

Select a race and target `UMAMaterial`. The tool loads the base recipe and lists its slots. `Change Materials` changes the material on each overlay asset used by checked base slots.

This edits shared `OverlayDataAsset` assets, not only the recipe instances. Any other race or wardrobe recipe using those overlays sees the material change. The slot row's displayed material is informational; the operation updates overlay assets.

Use this tool only when the base overlay collection should migrate as a unit. For recipe-specific overrides, edit the recipe instead.

## Validation After Race Utilities

After duplication or migration:

1. Confirm the race resolves from the Global Library by its new runtime name.
2. Inspect the base recipe, T-pose, DNA converters, expression data, and renderer settings.
3. Run `UMA > Testing > Race Smoke Test...`. Use `Test All Indexed Races` when validating the full
   indexed race library rather than one selected RaceData.
4. Generate the race with and without wardrobe.
5. Run [Release Asset Validation](ReleaseAssetValidation.md) before packaging.
