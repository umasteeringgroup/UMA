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
| Add or clear Unity labels for UMA asset types | Tags Editor |
| Find indexed runtime UMA assets | Global Library |
