# Examine Slots

The Examine Slots window batch-edits and synchronizes `SlotDataAsset` assets. It can update overlay scale, tags, wildcard settings, and wildcard races; copy serialized slot data into matching assets; or synchronize slot files into another folder with backups.

For ordinary slot authoring, see [SlotDataAsset](SlotDataAsset.md). For creating slots from imported models, see [Content Creation](ContentCreation.md).

## Open and Select Slots

1. Select one or more `SlotDataAsset` assets in the Project window.
2. Choose `Assets > UMA > Examine Slots`.

Every loaded slot starts checked. `Refresh` replaces the list with the current Project selection and checks every newly loaded slot. `Select All` and `Deselect All` control which slots receive `Apply Updates`, `Replace Slots In Folder`, or `Process Selected Slot`.

The sort control can order rows by Unity asset name or runtime `slotName`. Sorting preserves checkbox state.

## Apply Slot Updates

Enable only the changes intended for the checked slots, then click `Apply Updates`.

- `Set OverlayScale` replaces `overlayScale` with the entered value.
- `Add Tags` parses comma- or semicolon-separated values and adds them without removing existing tags. Duplicate comparisons are case-insensitive.
- `Set Wildcard` explicitly enables or disables `isWildCardSlot`.
- `Add Wildcard Races` adds race names without removing existing entries. Duplicate comparisons are case-insensitive.

`Set UMAMaterial` is currently non-operational because `SlotDataAsset` has no direct material field in this implementation. Assign materials through overlays or recipe slot material overrides instead.

Changes register Unity Undo and are saved immediately. Adding tags or races is additive; it is not a replace operation.

## Replace Slots In Folder

This operation copies the complete serialized content of checked source slots into matching `SlotDataAsset` assets beneath `Destination Folder`.

A destination slot matches when either its Unity asset name or runtime `slotName` matches either corresponding name on the source, case-insensitively. The search is recursive. The source asset itself is skipped.

The destination asset keeps its path and GUID, but its serialized slot content is overwritten with the source slot. This can change mesh data, names, tags, races, scale, wildcard state, and other slot fields. After replacement, UMA rebuilds the Global Library.

Use source control and test one slot first. This action is appropriate when a package already contains referenced destination slots whose data must be refreshed without changing their GUIDs.

## Copy Slots To Folder

This foldout synchronizes by exact `SlotDataAsset` filename.

- `Process Selected Slot` processes the checked rows.
- `Process All Slots` processes every slot loaded in the window, regardless of checkboxes.

For each same-filename asset found recursively under the chosen root, the existing destination is moved into `backup`, preserving its relative subfolder, and the source slot is copied into the old destination path. The `backup` and `Not found` trees are excluded from subsequent matching.

When no match exists, the source is copied into `Not found`. Unique paths prevent an existing backup or not-found file from being overwritten.

Moving the destination into `backup` preserves its GUID, so existing references continue to follow the backup asset. The copy placed at the original pathname has the source content but a different asset identity. This is path-oriented synchronization; it does not retarget recipe references.

## Recommended Safety Workflow

1. Commit or back up the project.
2. Verify which slots are checked; they begin checked by default.
3. Apply ordinary field updates separately from folder synchronization.
4. Process one representative slot.
5. Inspect destination and backup assets and test their recipes.
6. Run release asset validation before removing backups.

## Troubleshooting

### Material Assignment Did Nothing

The material toggle is intentionally harmless in the current implementation. `SlotDataAsset` does not store the material directly. Update the relevant `OverlayDataAsset`, recipe slot override, or `UMAMaterial` assignment.

### More Slots Changed Than Expected

Slots start checked. `Process All Slots` ignores checkboxes, while `Apply Updates`, `Replace Slots In Folder`, and `Process Selected Slot` use them.

### Recipes Still Reference a Backup Slot

Folder synchronization moves the old destination asset and preserves its GUID. References therefore move with it. Explicitly retarget recipes when they should use the copied source slot instead.
