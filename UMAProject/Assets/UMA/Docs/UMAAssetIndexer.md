# UMA Asset Indexer

The UMA Asset Indexer (`Assets/UMA/Core/Scripts/UMAAssetIndexer.cs`) is a central registry of UMA assets.

## What it stores
- Typed dictionaries of `AssetItem` records covering slots, overlays, races, recipes, UMAMaterials, etc.
- GUID-based lookup table for quick resolution by asset GUID.
- Race recipe cross-reference for wardrobe queries.

## Lifecycle
- Serialized as `Resources/AssetIndexer` ScriptableObject; loaded by `UMAAssetIndexer.Instance`.
- At editor time, rebuilds and repairs on demand (e.g., after domain reloads, asset moves).
- At runtime, holds references or addressable metadata depending on build settings.

## Key APIs
- `GetAssetItem<T>(name)`, `GetAsset<T>(name/hash)`
- `GetAssetItems(UMAPackedRecipeBase recipe)`: resolves dependent slots/overlays for a recipe
- `ProcessNewItem(obj, isAddressable, keepLoaded)`: adds/updates an index entry and references
- `PrepareBuild`, `RepairAndCleanup`, `RebuildLibrary`

## Addressables Integration
- Index items track addressable flags, address, labels, and group names.
- `UMAAddressablesSupport.Instance` bridges index items to Addressables.

## Editor Tips
- UMA > Global Library window exposes index state, counts, and bulk operations
- `AddEverything(false)` indexes all non-text assets in the project
- Use `IsIndexedType` to respect UMA's supported types
