# Asset Discovery Utilities

UMA's Asset Management and Editors menus contain small tools for locating scene objects, organizing frequently used assets, inspecting material usage, and applying Unity labels.

## Quick Finder

Open `UMA > Asset Management > Quick Finder`.

`Add current` stores each selected scene GameObject using its scene identity and hierarchy path. It also captures the current Scene view camera state. Clicking an entry finds and selects the object and restores the stored Scene view.

Entries persist in editor preferences, not in a project asset. They are personal editor bookmarks and are not shared through source control. Renaming or reparenting an object can break its path; add it again after hierarchy changes.

The `x` removes only the bookmark.

## Favorites

Open `UMA > Asset Management > Favorites`, or select assets and choose `Assets > Add Selected Assets to UMA Favorites`.

Favorites are stored in `UMAFavoriteList` project assets, so teams can share them through source control. A favorite entry can open, ping, or inspect its asset. Removing an entry changes only the list.

Deleting a favorite category from the category header deletes the `UMAFavoriteList` asset itself. It does not delete the assets referenced by that list.

## Find Component Usages

Open `UMA > Asset Management > Find Component Usages` and assign a `MonoScript` whose class derives from `MonoBehaviour`.

The window lists loaded objects found through `Resources.FindObjectsOfTypeAll`. Clicking a result selects its GameObject.

This is not a guaranteed full-project prefab scan. Assets that Unity has not loaded may not appear. Use Unity dependency search or a dedicated serialized-project scan when absence must be proven.

## Find UMAMaterial in Overlays

Open `UMA > Asset Management > Find UMAMaterial in Overlays`, or select `UMAMaterial` assets and choose `Assets > UMA > Find Selected UMAMaterials in Overlays`.

The window scans overlays, optionally beneath a selected folder, groups them by material, and provides Ping and Inspect actions. It does not change materials or overlays.

This is useful before deleting or migrating a material and for determining whether a material change affects shared overlays.

## Find Texture Usage in Materials

Select one `Texture2D` asset in the Project window, then choose `Assets > Find Usage in Material`
from the main menu or the asset's context menu.

The results window scans project materials and material sub-assets for exact references to the
selected texture. It includes resolved texture properties inherited by Material Variants and saved
references belonging to shader properties that are currently hidden or no longer exposed. Every
matching material appears on its own row with its asset path and two actions:

- `Ping` highlights the material in the Project window without replacing the current selection.
- `Inspect` opens a separate locked Inspector for the material.

Use `Refresh` after changing a material assignment. Large searches show cancelable progress; a
canceled search keeps and labels the partial results found so far.

## Find Unused Materials and Shaders

Open `UMA > Asset Management > Find Unused Materials and Shaders`. This dockable window
finds standalone **UMAMaterial**, **Material**, and **Shader** assets that have no detected
usage references. UMA Global Library registration alone does **not** count as usage.
Scanning does not move, delete, save, or change assets.

1. Optionally assign a **Search Folder** inside `Assets`; leave it empty to search all of
   `Assets`. References are checked across the whole project and packages, even when the
   candidate folder is narrow.
2. Click **Scan / Refresh**. The scan runs incrementally and can be canceled. Do not edit
   project assets or scenes during a scan; changes invalidate its results.
3. Use the type buttons and name/path search to filter the grid. Turn off **Unused only**
   to include kept assets. Click an asset name or its status to see reference/protection
   details; **Inspect** selects and pings the asset in Unity. **Unused (indexed)** means the
   asset is registered in an UMA index but has no detected usage references. It is still
   eligible for cleanup; click its status to see the affected indexes.
4. Check individual unused assets or use **Check visible unused**. Nothing is checked by
   default. Filtering does not clear existing checks: the checked count includes hidden
   rows. **Clear checks** clears the entire selection.
5. Click **Delete checked...**. The window runs a fresh reference scan and then shows a
   confirmation popup. If any checked asset has become used, protected, moved, or missing,
   the entire request is canceled without deleting anything. All affected UMA indexes must
   be writable and saved; resolve unsaved changes or checkout permissions before retrying.
6. Confirm **Move to Trash** only after reviewing the assets. Unity moves the checked asset
   files and their metadata to the system trash and removes their matching UMA index entries.
   This is **not a Ctrl+Z Undo operation**; recovery depends on the system trash or source
   control. If restoring an asset from trash, re-register it in the Global Library as needed
   (or restore the corresponding index change from source control). A result popup reports
   the number moved and any failures. Scan again after cleanup.

### What “unused” means

This is a conservative incoming-reference search, **not** a build-size or build-reachability
report. An asset is kept if a project asset uses it, including disabled scenes,
unused prefabs, overlays (including stripped `materialName` references), materials, and both
UMAMaterial passes. UMA index registrations are explicitly excluded, whether stored as an
object reference, GUID, path, or name alone. A chain such as unused UMAMaterial
→ Material → Shader is cleaned in stages: initially only the UMAMaterial is eligible. Its
Material may become eligible on the next scan; the Shader may become eligible after that.
The tool never cascades deletions automatically.

Additional protections include Resources, Editor Default Resources, StreamingAssets,
AssetBundle entries (including folder assignments), Addressables GUID/folder registrations
when that package is present, project settings, preloaded assets, open scenes/Prefab Mode,
and unsaved asset references. Literal `Shader.Find("Shader/Name")` calls in C# source also
keep matching shaders. Read-only files/metadata, files containing sub-assets, and linked
files/folders are not eligible for deletion. Package/built-in assets and embedded model
materials are never deletion candidates.

Deleting a registered asset removes its matching entries from every discovered UMA index
and its live name/GUID caches, using Unity serialization (including binary index assets).
Only affected indexes are saved; unrelated entries are not healed, renamed, or removed.
Index edits are saved before the asset is moved to trash. If moving fails, those edits are
restored and saved; any restoration failure is reported explicitly. Strong identities take
precedence over names, so a same-named asset with another GUID keeps its registration. If an
old name-only registration is ambiguous, cleanup is blocked until that ambiguity is resolved.

No static scan can prove that an asset is unused by computed runtime names, external code,
custom GUID registries, or externally supplied content. Review those usages yourself before
confirming deletion and keep a source-control backup. Protected does not necessarily mean
the asset is used in a build; it means the tool cannot safely offer it for deletion.

Canceled, failed, or stale scans cannot authorize deletion. The window rechecks selected
assets before showing the confirmation and checks identity, content, type, and write safety
again before moving each file.

## Tags Editor

Open `UMA > Editors > Tags Editor`.

`Set UMA Tags` applies Unity Asset Database labels such as `UMA_<friendly type>` to loaded UMA asset types. `Clear UMA Tags` removes those labels. The process can take several minutes and saves/refreshes the Asset Database.

These are Unity asset labels used for editor organization. They are not the same as SlotDataAsset or OverlayDataAsset runtime matching tags.

## Global Library Discovery

The main Global Library, Filters, Maintenance, and Project context add command are documented in [UMA Asset Indexer and Global Library](UMAAssetIndexer.md).

## Choosing a Discovery Tool

| Need | Tool |
|---|---|
| Return to scene objects and camera viewpoints | Quick Finder |
| Share curated project-asset lists | Favorites |
| Find loaded instances of a MonoBehaviour type | Find Component Usages |
| Find overlays using an UMAMaterial | Find UMAMaterial in Overlays |
| Find materials using a Texture2D | Find Usage in Material |
| Review and remove unreferenced UMAMaterials, Materials, and Shaders | Find Unused Materials and Shaders |
| Add or clear Unity labels for UMA asset types | Tags Editor |
| Find indexed runtime UMA assets | Global Library |
