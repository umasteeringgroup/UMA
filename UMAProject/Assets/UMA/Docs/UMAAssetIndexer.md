# UMA Asset Indexer and Global Library

The UMA Asset Indexer is the project-wide catalog used to find races, slots, overlays, recipes, materials, DNA assets, controllers, and other UMA content. Artists work with it through the `Global Library` window.

Open:

`UMA > Global Library`

The serialized index is stored as the `Resources/AssetIndexer` asset and accessed at runtime through `UMAAssetIndexer.Instance`.

## Why Indexing Matters

UMA recipes store many references by name or lightweight identifiers. The Global Library resolves those references into actual assets.

An asset that works while directly selected in the editor can still fail in a build if it is not indexed or included through Resources or Addressables.

## Add New Content

### Drag and drop

Drag an asset or folder onto:

`Drag indexable assets here to ADD them to the index`

Folders are scanned recursively for supported UMA asset types.

### Project context menu

Select assets or folders in the Project window and use:

`Assets > Add selected assets to UMA global library`

### Slot Builder

Enable `Add To Global Library` when creating slots, overlays, and recipes.

### Recipe warnings

UMA editors may warn when a recipe contains unindexed content. Use the offered add action, then confirm the item appears in the Global Library.

## Remove Content from the Index

Drag assets onto the remove area or select indexed rows and use `Items > Remove Selected`.

Removing an item from the index does not normally delete the source asset.

`Permanently delete Selected` is destructive and deletes project assets. Use it only when deletion is intentional and source control is available.

## Read the Global Library

The window groups items by type. Depending on the current mode and installed features, columns may show:

- Asset name and type
- Source path
- GUID
- Addressable state
- Group and labels
- Keep or ignore flags
- Loaded or reference state

Use filtering and type selection to find missing or duplicate items.

`Inspect` opens the asset inspector. `Ping` or selection actions locate the asset in the Project window.

## Keep, Ignore, and Local Labels

### Keep

The Keep flag prevents cleanup tools from treating an intentionally retained item as orphaned.

Use it for content loaded by custom code or content that cannot be discovered from ordinary recipe dependency analysis.

### Ignore

Ignored items remain outside normal UMA indexing behavior. Use this for backups, templates, and source assets that should not become runtime UMA content.

### Force Keep and Label Local Files

Some UMA assets expose build flags directly in their inspectors. These flags participate in Resources and Addressables preparation. Use them only as part of a defined loading strategy.

## Rebuild and Repair

Open:

`UMA > Global Library Maintenance`

Close the main Global Library window when the maintenance tool requests it.

### Global Library Filters

Open `UMA > Global Library Filters` to restrict project scans by UMA asset type.

Choose an indexed type and add one or more project paths. When a type has filters, rebuild scans accept only asset paths containing at least one of that type's filter strings. Types with no configured filter continue using their normal project-wide search behavior.

Filters are persisted on the asset index and affect later rebuilds. Adding a filter does not immediately remove existing entries; run a rebuild when the configured scope should become authoritative.

Use `Browse` to choose a folder under the current project's `Assets` directory. `Remove` removes only that type/path rule. Be careful with broad substring filters and similarly named folders, because matching is path-string based.

Recommended uses:

- Restrict optional content types to package roots.
- Exclude large source-art areas by indexing only production folders.
- Keep UMA2 and UMA3 content discovery predictable in a development project.

Before changing filters, back up the index and record the existing rules. After rebuilding, verify representative assets of every filtered type.

### Rebuild Library From Project

This clears and recreates the index by scanning the configured project folders and types.

Use it:

- After first importing UMA
- After a large package or branch change
- After moving many UMA assets
- When the index is clearly incomplete

The variant that includes text assets is only needed when the project intentionally indexes those files.

### Repair and Remove Invalid Items

This removes entries whose source assets can no longer be resolved, then rebuilds lookup dictionaries.

Use it after deleting or moving assets outside normal Unity operations.

### Remove Duplicate Serialized Items

Use this when analysis reports duplicate index entries.

### Rebuild Dictionaries

This recreates runtime lookup dictionaries from the serialized item list without rescanning the entire project.

Use it when serialized entries appear correct but lookup behavior is stale.

### Clean Added Types

This removes nonstandard indexed types and their items. Do not use it if the project intentionally registered custom asset types.

### Clear Cached References

Releases Unity object references held by the index without removing serialized index entries. Use it to reduce stale in-memory references or allow unused assets to unload. Runtime name and GUID entries remain in the index.

### Refresh Cached References

Reloads object references for non-addressable indexed items. Use it after project assets were reimported or references were cleared. Addressable loading state is managed separately.

### Save Asset Index

Forces the current serialized index state to disk. Most maintenance operations save automatically, but this is useful after controlled programmatic or inspector changes.

### Backup and Restore

`Backup Asset Index...` writes a `.bak` snapshot outside or inside the project at the location you choose. It backs up index data, not the referenced project assets.

`Restore Asset Index...` replaces the current serialized index with the selected backup. Close the Global Library first and verify that the backed-up asset paths and GUIDs still exist in the current project revision.

### Empty Asset Index

The Danger Zone command removes every serialized index item but does not delete project assets. It cannot be undone through Unity Undo. Use a backup first, then rebuild from the project when the empty state is not intended to remain.

## Routine Artist Workflow

When adding one clothing item:

1. Create the slot, overlay, and wardrobe recipe.
2. Add them through Slot Builder or the Project context menu.
3. Open Global Library.
4. Filter for each asset name.
5. Inspect the wardrobe recipe and verify race compatibility.
6. Build a DCA.
7. Test again after a full library rebuild before release.

## Texture and Overlay Changes

Overlay assets can be cached by the index and recipe systems. UMA's overlay inspector and texture utilities notify the index when an overlay changes.

When a texture was replaced on disk and a rebuild still uses an older overlay reference:

1. Reimport the texture.
2. Select or reimport the `OverlayDataAsset`.
3. Use the overlay editor's update workflow.
4. Rebuild the character.
5. Rebuild dictionaries or repair the index only if the reference still remains stale.

Avoid rebuilding the entire library for every texture paint iteration.

## Addressables

When Addressables are enabled, index items also store group, address, label, and load-state metadata.

The Global Library Addressables menu can:

- Generate optimized groups
- Generate the final single group
- Prepare a build
- Run post-build material fixup
- Reset stripped shaders
- Remove addressable state
- Find or remove orphaned slots and overlays

Addressables operations can modify UMA assets and Addressables settings. Follow [Addressables.md](Addressables.md) and keep the project under source control.

## Build Inclusion

Before a player build, every required asset must be reachable through:

- Resources references
- Addressables
- Another explicit project reference

The index alone is not a magic build-inclusion system. Build preparation creates or manages the required references according to the selected UMA settings.

## Useful Runtime APIs

- `GetAssetItem<T>(name)`
- `GetAsset<T>(name or hash)`
- `GetAllAssets<T>()`
- `ProcessNewItem(object)`
- `Preload(...)`

Runtime code should not mutate the global index casually. Prefer established loading and preload paths.

## Troubleshooting

### Content works in the editor but not in a build

Verify the race, recipe, slots, overlays, materials, and textures are indexed and included through Resources or Addressables.

### A renamed asset cannot be found

Repair the index. Also check references stored by logical UMA name; renaming the file does not always update an internal asset name.

### The library reports missing assets

Use `Repair and Remove Invalid Items`, then inspect the affected recipes.

### Duplicate names return the wrong content

UMA name lookups should be unique within an indexed type. Rename one asset and rebuild the index.

### Rebuild removed intentionally loaded content

Set the appropriate Keep or Force Keep flag, or add an explicit recipe or loading reference.

### The Global Library is slow

Avoid full rebuilds for routine edits. Add only changed content, close large expanded groups, and use dictionary rebuilds when a project rescan is unnecessary.

## Related Guides

- [GettingStarted.md](GettingStarted.md)
- [Addressables.md](Addressables.md)
- [ContentCreation.md](ContentCreation.md)
- [DynamicCharacterAvatar.md](DynamicCharacterAvatar.md)
