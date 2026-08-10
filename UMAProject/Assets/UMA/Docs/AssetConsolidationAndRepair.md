# Asset Consolidation and Repair

UMA provides several asset-moving and repair commands. Their names are similar, but their scopes and reference behavior differ. Use source control before any consolidation operation.

## Consolidate Textures for Wardrobe Recipes

Select one or more `UMAWardrobeRecipe` assets and choose `Assets > UMA > Consolidate Textures`.

The window scans overlay channel textures and alpha masks referenced by the selected recipes. `Move Textures` moves each discovered texture into the chosen folder unless it is already beneath that folder.

Because Asset Database moves preserve GUIDs, project references normally follow moved textures automatically. If a same-filename texture already exists at the destination, channel texture references are changed to that existing asset instead of moving the source. Alpha-mask references are discovered and moved, but the same-filename relinking path is tracked only for channel textures.

Despite older help text saying “copies,” this command moves source assets.

## Consolidate Texture for Recipe

Select one or more `UMATextRecipe` assets and choose `Assets > UMA > Consolidate texture for recipe`.

This is a direct destination-folder workflow for copying overlay textures used by general text recipes. Review the completion dialog and inspect overlay references afterward, especially when the destination already contains same-named files.

## Consolidate Current Scene Assets

Open `UMA > Asset Management > Consolidate Current Scene Assets`.

The window discovers allowed dependencies referenced by objects in the active scene, restricted to a Source Folder and excluding configured Ignore Folders. Recipes, slots, and overlays are excluded because they have dedicated workflows. Eligible textures, models, sounds, materials, and prefabs are listed with the scene reference that caused their inclusion.

Checked assets are moved into category subfolders beneath Destination Folder. Moves preserve GUIDs. Existing destination names receive unique paths rather than being overwritten.

Use `Rescan` after changing the scene or source/ignore folders. The candidate list begins selected; inspect the Reason column before consolidating.

## Repair Text Recipe

Select a `UMATextRecipe` and choose `Assets > UMA > Repair Text Recipe`. The repair window shows packed slot identifiers and whether the Global Library resolves each one.

Missing slots can be replaced using similar indexed names. If several candidates exist, choose explicitly. The window also exposes packed disabled and placeholder flags. Save after review and test the resulting recipe on a generated avatar.

## Repair Overlays with Too Many Textures

Open `UMA > Textures > Repair Overlays with too many textures`.

This one-click maintenance action scans every indexed `OverlayDataAsset`. When an overlay has more texture entries than its `UMAMaterial` has channels, the tool truncates `textureList`, `textureNames`, and `overlayBlend` to the material channel count.

This is destructive to the extra channel entries. It preserves only the existing prefix. Overlays without a material are skipped. Commit before running it and review the reported overlay names.

## Convert Selected Textures to PNG

Select textures and choose `Assets > UMA > Convert selected textures to PNG`.

The tool converts readable copies to RGBA32 PNG and can:

- Overwrite an existing PNG.
- Keep the original and create a `_converted.png` result.
- Replace references in indexed overlays.

Unity's encoder does not expose compression-level or interlace settings. Review alpha, color space, normal-map encoding, and importer settings after conversion.

## Choosing the Right Tool

| Goal | Tool |
|---|---|
| Gather wardrobe texture dependencies while preserving GUID references | Consolidate Textures |
| Gather textures for general text recipes | Consolidate texture for recipe |
| Organize active-scene dependencies into category folders | Consolidate Current Scene Assets |
| Repair renamed or missing packed slot identifiers | Repair Text Recipe |
| Remove obsolete overlay channel entries beyond material count | Repair Overlays with too many textures |
| Create standard PNG files and optionally update overlays | Convert selected textures to PNG |

## Safe Workflow

1. Commit or back up the project.
2. Confirm the exact Project selection or active scene.
3. Process a small representative set.
4. Inspect moved paths and source-control changes.
5. Rebuild the Global Library when asset lookup changed.
6. Generate affected characters.
7. Run release asset validation before package export.
