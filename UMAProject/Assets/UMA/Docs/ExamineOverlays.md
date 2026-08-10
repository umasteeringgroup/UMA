# Examine Overlays

The Examine Overlays window is a batch inspection and maintenance tool for `OverlayDataAsset` assets. It can identify incomplete overlays, inspect and replace channel textures, validate dependency locations, assign an `UMAMaterial`, relink textures by filename, and synchronize overlay assets into another folder while retaining backups.

For overlay fields, channel ordering, blend behavior, and ordinary single-asset authoring, see [OverlayDataAsset](OverlayDataAsset.md). For material channel definitions, see [UMA Materials](UMAMaterial.md).

--------------------------------------------------------------------------------

## Opening the Window

1. Select one or more `OverlayDataAsset` assets in the Project window.
2. Choose `Assets > UMA > Examine Overlays`.

The menu item is enabled only when the Project selection contains an overlay asset.

The window initially loads the selected overlays and sorts them by name. Changing the Project selection does not update the window automatically. Click `Refresh` after changing the Project selection to replace the loaded set with the overlays that are currently selected in the Project window.

The window remembers its foldout states, filter, material, folder paths, and texture-search options between sessions. It does not use the stored settings as permission to modify assets; an operation runs only when its button is clicked.

--------------------------------------------------------------------------------

## Understand the Four Selection Scopes

Several controls use different meanings of “selected.” Confirm the scope before running a batch operation.

| Scope | How it is chosen | What uses it |
|---|---|---|
| Loaded overlays | Project selection when the window opens or when `Refresh` is clicked | Folder validation, `Assign UMAMaterial to selected`, and texture relinking |
| Current overlay | Click an overlay name; the row shows a `>` marker | Texture details and `Process Selected Overlay` |
| Checked overlays | Use the checkbox at the left of a row | `Remove Selected` only |
| Filtered overlays | Choose `all`, `complete`, or `incomplete` above the list | Visible rows, `Select All`, `Deselect All`, `Assign UMAMaterial to ALL`, and `Process All Overlays` |

Important consequences:

- Row checkboxes do not target material assignment, validation, relinking, or Update Folder processing.
- `Assign UMAMaterial to selected` means all overlays loaded from the Project selection, including rows hidden by the current filter.
- `Assign UMAMaterial to ALL` means all overlays in the current filtered list.
- `Replace textures in selected overlays` processes all loaded overlays, not only checked rows.
- `Process Selected Overlay` means the single current row.
- `Process All Overlays` means all overlays passing the current filter.

`Select All` and `Deselect All` only change row checkboxes. They therefore affect `Remove Selected`, not the asset-editing operations.

--------------------------------------------------------------------------------

## Overlay List and Status

The left pane lists the loaded overlays. Click an overlay name to make it current and show its textures in the right pane.

Status values are:

- `Complete`: the overlay has an `UMAMaterial`, has at least one texture entry, and none of its texture entries are null.
- `missing textures`: its texture list is empty or contains at least one null entry.
- `missing UMAMaterial`: its material reference is null.
- `missing textures and UMAT`: both conditions are present.
- `folder error`: validation found a texture, alpha mask, or `UMAMaterial` outside the permitted folders.

The complete/incomplete status is intentionally a basic reference check. It does not prove that:

- Texture order matches the material channels.
- Texture dimensions or import settings are correct.
- The material is appropriate for the slot using the overlay.
- The optional alpha mask is assigned.
- The overlay produces the intended runtime result.

Use the `all`, `complete`, or `incomplete` filter to limit the displayed rows. `Review` opens the dependency-location errors for an overlay after validation. `Inspect` opens the normal Unity Inspector.

### Remove Controls

- `Remove Selected` removes checked, currently filtered rows from this window's loaded set.
- The `x` at the end of a row removes that one overlay from the window.

Neither control deletes, moves, or modifies the source `OverlayDataAsset`. Click `Refresh` to rebuild the loaded set from the current Project selection.

--------------------------------------------------------------------------------

## Inspect and Replace Individual Textures

The right pane displays every entry in the current overlay's `textureList`. When its `UMAMaterial` resolves, the heading uses the corresponding material property name. Otherwise it displays `Texture 0`, `Texture 1`, and so on.

Assigning a different texture in this pane immediately changes and saves the current `OverlayDataAsset`. The operation registers Unity Undo.

This is direct asset editing. Every recipe or slot that uses the overlay sees the new texture reference. Verify the channel index against the overlay's `UMAMaterial` before changing it.

--------------------------------------------------------------------------------

## Validate Folder

Folder validation checks whether each loaded overlay's dependencies are located under one of up to two permitted folders.

1. Assign `Folder 1` and, if needed, `Folder 2`. Both must be under the project's `Assets` folder.
2. Click `Validate`.
3. Inspect the summary.
4. Click `Review` beside any row showing `folder error`.

The check includes:

- The overlay's `UMAMaterial`.
- Its alpha mask, when assigned.
- Every non-null entry in its texture list.

An asset is accepted when it is directly in either selected folder or anywhere beneath it. Folder 2 is useful when package-specific overlays are allowed to reference shared UMA materials or textures stored in a second root.

Validation processes every loaded overlay, including rows hidden by the complete/incomplete filter. It does not move or modify any asset.

The validator ignores null dependencies because missing references are reported by the overlay status instead. Use both the status and folder validation when preparing a package.

--------------------------------------------------------------------------------

## Assign an UMAMaterial

Open `Utilities`, assign the target `UMAMaterial`, and choose one of the two actions:

- `Assign UMAMaterial to selected` updates every overlay loaded from the Project selection.
- `Assign UMAMaterial to ALL` updates every overlay currently passing the complete/incomplete filter.

The operation updates both the material reference and the stored material name, registers Unity Undo, and saves the changed overlay assets.

This changes shared `OverlayDataAsset` assets. Every recipe using an affected overlay receives the new material. The operation does not reorder textures, resize the texture list, or verify that existing channel entries match the new material. Review channel compatibility before running it across many overlays.

--------------------------------------------------------------------------------

## Relink Textures by Filename

`Relink Textures` replaces existing texture references with textures found in another folder. This is useful after copying, restoring, or reorganizing a texture collection while retaining the original filenames.

1. Select a `Texture Folder` under `Assets`.
2. Enable `Include subfolders` when the search should be recursive.
3. Leave `Skip if already same asset` enabled for ordinary repair work.
4. Click `Replace textures in selected overlays`.
5. Review both the replacement summary and the list of names that could not be found.

Despite the button wording, this operation processes every overlay loaded into the window. Row checkboxes and the complete/incomplete filter do not limit it.

### Matching Rules

- Matching uses the source texture's filename without its extension.
- Matching is case-insensitive.
- The operation checks ordinary channel textures and the overlay alpha mask.
- Null texture entries have no filename and are not filled automatically.
- Replaced channel entries update the overlay's stored texture name.

When more than one searched texture has the same base filename, extension preference is:

1. PNG
2. JPG or JPEG
3. TGA
4. TIF or TIFF
5. Other texture formats

Avoid keeping multiple candidates with the same base name and extension in the search scope. Their choice may depend on Asset Database search order.

`Include subfolders` off restricts candidates to the exact selected folder. Unity's asset search is recursive internally, but the tool discards results from child folders in this mode.

The operation registers Undo for each processed overlay and saves changed assets. It does not copy texture files; it changes overlay references to existing assets in the selected folder.

--------------------------------------------------------------------------------

## Update Folder

Update Folder synchronizes selected source overlay assets into an existing folder tree by matching `OverlayDataAsset` filenames. It is intended for controlled content-update and package-maintenance workflows.

This operation moves and copies project assets. Commit or back up the project before using it.

1. Select a valid destination root under `Assets`.
2. Use the all/complete/incomplete filter to establish the intended list.
3. Click one overlay name and choose `Process Selected Overlay`, or choose `Process All Overlays` for every overlay in the filtered list.
4. Review the counts and any error dialog.

### When a Matching Filename Is Found

The tool searches the destination root and all child folders for `OverlayDataAsset` files with the same filename as the source overlay. It excludes its own `backup` and `not found` folders.

For every matching destination asset other than the source itself, the tool:

1. Creates a `backup` folder under the destination root when needed.
2. Reproduces the duplicate's relative subfolder beneath `backup`.
3. Moves the existing destination asset into that backup location, using a unique filename if necessary.
4. Copies the source overlay asset into the destination asset's former path.

Because the existing asset is moved through Unity's Asset Database, references to its GUID continue to follow the moved backup asset. Copying the source into the old pathname does not automatically redirect those references to the new copy. Update Folder synchronizes files by path; it is not a reference-rebinding operation.

### When No Matching Filename Is Found

The tool creates `not found` under the destination root and copies the source overlay there. A unique filename is generated if a file with that name already exists.

### Important Limitations

- Matching is by full asset filename, not the overlay runtime name or GUID.
- Every matching duplicate under the destination root is processed.
- The operation copies the `OverlayDataAsset`; it does not copy its referenced textures or `UMAMaterial`.
- It does not update recipes to reference the copied overlay.
- It does not remove the source overlay.
- `Process All Overlays` uses the current filter but ignores row checkboxes.
- The operation saves and refreshes the Asset Database after processing.

Use folder validation or release asset validation after synchronization to confirm dependency locations and references.

--------------------------------------------------------------------------------

## Undo, Source Control, and Recovery

Direct texture changes, material assignment, and texture relinking register Unity Undo and save modified overlay assets.

Update Folder performs Asset Database move and copy operations. Do not rely on a long Undo chain to recover a large folder synchronization. Use source control or a project backup, inspect the generated `backup` and `not found` folders, and verify references before deleting either folder.

Recommended safety sequence:

1. Commit or shelve current work.
2. Process one overlay first.
3. Inspect the destination, backup, and references.
4. Run the operation on the filtered list only after the sample result is correct.
5. Validate dependencies and generate representative avatars.
6. Remove backup assets only after confirming that no project references still point to them.

--------------------------------------------------------------------------------

## Recommended Workflows

### Find Incomplete Overlays

1. Select an overlay folder in the Project window and search for `t:OverlayDataAsset` if needed.
2. Open Examine Overlays.
3. Set the filter to `incomplete`.
4. Inspect missing textures or materials.
5. Use direct texture editing, material assignment, or relinking as appropriate.

### Validate a Self-Contained Package

1. Load the package's overlays into the window.
2. Set Folder 1 to the package root.
3. Set Folder 2 to the permitted shared UMA root, if the package is allowed to depend on it.
4. Validate and review every folder error.
5. Run release asset validation before exporting the package.

### Relink a Copied Texture Set

1. Make sure replacement files retain their original base filenames.
2. Load only the overlays intended for repair.
3. Select the replacement texture folder.
4. Process one small overlay set first.
5. Inspect the channel assignments and generated characters before processing more assets.

--------------------------------------------------------------------------------

## Troubleshooting

### The Examine Overlays Menu Is Disabled

Select one or more `OverlayDataAsset` assets in the Project window.

### Refresh Removed My Previous List

Refresh intentionally replaces the window contents with the current Project selection. Reselect the intended overlay assets and click Refresh again.

### Checked Rows Were Not Modified

Checkboxes target only `Remove Selected`. Consult the selection-scope table above to determine whether an operation uses all loaded overlays, the current row, or the filtered list.

### An Overlay Is Marked Complete but Is Still Wrong

Complete means only that a material and non-null texture entries exist. Inspect channel order, material compatibility, texture import settings, and runtime use separately.

### Relink Chose the Wrong Texture

Look for duplicate base filenames in the selected texture folder and its included subfolders. PNG has priority over JPG, TGA, TIFF, and other formats, but duplicate candidates of the same preferred format should be removed or renamed.

### Relink Did Not Fill an Empty Channel

A null channel has no source filename to match. Assign it directly in the right pane or repair the overlay in its Inspector.

### A Recipe Still References the Backup Overlay

Update Folder moves the original destination asset into `backup`, preserving its GUID. Existing references therefore continue to point to that moved asset. Update the recipe reference explicitly if it should use the copied replacement.

### Folder Validation Reports a Shared Material

Assign the permitted shared content root as Folder 2, or move/copy and intentionally relink the dependency into the package. Do not duplicate shared assets solely to silence validation unless the package is intended to be self-contained.
