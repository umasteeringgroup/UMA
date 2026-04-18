# DynamicCharacterAvatar (DCA)

`DynamicCharacterAvatar` is UMA’s high-level runtime character component. It manages race selection, wardrobe (wearables and collections), DNA, shared colors, animation/expression linking, and full character build/update. This guide covers lifecycle, data flow, events, APIs, and best practices.

Useful namespaces
- `UMA.CharacterSystem`
- Key classes: `DynamicCharacterAvatar`, `UMATextRecipe`, `UMAWardrobeRecipe`, `UMAWardrobeCollection`, `RaceData`, `OverlayColorData`, `UMADnaBase`

--------------------------------------------------------------------------------

## Lifecycle and Initialization

- Attach `DynamicCharacterAvatar` to a GameObject to own UMA state and build the avatar.
- `Start()` calls `InitialStartup()` which:
  - Initializes UMA data (`InitializeAvatar`, generator references, events)
  - Resolves active race (`activeRace.SetRaceData()`)
  - If BuildCharacterEnabled is true, either:
    - Builds from component settings (race + default wardrobe + colors), or
    - Loads a starting recipe from file/string if configured (see Load section).
- In the Editor (not Play mode), if `editorTimeGeneration` is true, `GenerateSingleUMA()` previews the UMA.

Build triggers
- Set properties (race/wardrobe/colors/DNA), then either let DCA rebuild automatically or set `BuildCharacterEnabled=false` to batch changes and set it back to `true`.
- Programmatic rebuild: `BuildCharacter()` or mark dirty via `ForceUpdate(dnaDirty, textureDirty, meshDirty)`.

Renderer visibility
- `hide` toggles all `SkinnedMeshRenderer`s.
- `leanHiding` frees generated textures when hidden (Play mode), recreates on show.

--------------------------------------------------------------------------------

## Core Data Model

- Race
  - `activeRace.name` (string) ? resolves to `RaceData` (`activeRace.data`/`racedata`).
  - Base recipe: `RaceData.baseRaceRecipe` is merged with wardrobe/additive recipes to produce the final character.
- Wardrobe (wearables)
  - Base set: `_wardrobeRecipes: Dictionary<string, UMATextRecipe>` (one per wardrobe slot).
  - Additive stack: `_additiveRecipes: Dictionary<string, List<UMATextRecipe>>` (stacked additional items per slot).
  - Collections: `_wardrobeCollections: Dictionary<string, UMAWardrobeCollection>`; collections provide multi-slot wardrobe sets per race.
- DNA
  - Current DNA lives in `UMAData.UMARecipe` (created/managed during build).
  - `predefinedDNA` (component-level initial overrides), `overrideDNA` (wardrobe-applied temporary overrides), `savedDNA` (stores original values during override sequence).
- Shared Colors (`characterColors: ColorValueList`)
  - Name-keyed array of `OverlayColorData` (channels + optional material property block data).

--------------------------------------------------------------------------------

## Events

- `RecipeUpdated(UMAData)`: raised just before generation; last chance to tweak the merged recipe.
- `WardrobeAdded(UMAData, UMAWardrobeRecipe)`, `WardrobeRemoved(UMAData, UMAWardrobeRecipe)`
- `CharacterStart(DynamicCharacterAvatar)`
- `BuildCharacterBegun(UMAData)`
- `SlotsHidden(List<SlotData>)`: slots hidden by Hides/suppressions/wildcards.
- `WardrobeSuppressed(List<UMATextRecipe>)`: recipes suppressed by other items.
- UMAData integration: `OnCharacterBegun`, `OnCharacterDnaUpdated` are used to apply/restore override DNA.

--------------------------------------------------------------------------------

## Changing Race

- `ChangeRace(string racename, ChangeRaceOptions opts=useDefaults, bool force=false)`
  - Options: keepDNA, keepWardrobe, keepBodyColors. When not kept, component defaults or base race defaults are applied.
  - Caching: if `cacheCurrentState` is true, the previous race configuration is saved and restored when switching back.
- `RecreateAnimatorOnRaceChange` may reconstruct Animator; `rebuildSkeleton`/`alwaysRebuildSkeleton` manage skeleton reconstruction; `forceRebindAnimator` triggers rebind.

--------------------------------------------------------------------------------

## Wardrobe (Wearables) API

- Set or append wearables
  - `SetWearableItem(UMAWardrobeRecipe)` or `SetSlot(UMATextRecipe)` for base slot assignment.
  - `AppendWearableItem(UMAWardrobeRecipe)` to stack additive items on a slot.
- Remove
  - `RemoveWearableItem(UMAWardrobeRecipe utr, bool all=false)` or `RemoveWearableItem(UMATextRecipe, all)`
  - `ClearWearableItems(string slot)`, `ClearSlots()` for all, or `ClearSlot(string)`.
- Get
  - `GetWearableItem(string slot)` returns the base item; `GetAppendedWearableItems(string)` returns appended items.
- Wardrobe Collections
  - `LoadWardrobeCollection(string name|UMAWardrobeCollection)` adds collection and loads its race-specific set.
  - `UnloadWardrobeCollection(string name)`, `UnloadWardrobeCollectionGroup(string group)`, `UnloadAllWardrobeCollections()`
  - `ReapplyWardrobeCollections()` reapplies collection items to empty slots after changes.
- Suppression and Hides
  - Wardrobe recipes can list `suppressWardrobeSlots`, `Hides`, `HideTags`; DCA computes final hidden slots and overlay removals.
  - Mesh Hide (triangle masks): DCA aggregates `MeshHideAsset` references across recipes per slot and rebuilds hide masks.
- Cross-compatibility
  - If a recipe is only cross-compatible with the active race, overlays may be moved to an equivalent base slot; full slot replacement is avoided to keep base geometry consistent.
- Swap & Wildcard slots
  - Swap slots (`isSwapSlot`) can replace slots with matching `swapTag`; wildcards (`isWildCardSlot`) can add their overlays to other slots by tag.

--------------------------------------------------------------------------------

## DNA Management

- Query/edit DNA
  - `GetDNA()` returns name?`DnaSetter` map; `SetDNA(name, value, rebuild)` to change.
  - `GetDefaultDNA()` returns race default values; `GetDNAValues()` returns name?float map.
- Applying external DNA
  - `predefinedDNA`: applied each build (unless `keepPredefinedDNA` is true; then it persists for subsequent builds).
  - Wardrobe `OverrideDNA`: temporarily applied at build begin (`SetAndSaveOverrideDNA`) and restored afterward (`RestoreOverrideDna`).
- Blendshape loading controls
  - `loadBlendShapes`, `loadOnlyUsedBlendshapes`, `loadBlendshapeNormals`, `loadBlendshapeTangents`, `loadAllFrames`.
  - `SetFilteredBlendshapes(DnaDef[])` limits blendshapes based on active DNA and race DNA?blendshape mapping.

--------------------------------------------------------------------------------

## Shared Colors

- Component list: `characterColors.Colors` (`ColorValue` extends `OverlayColorData`).
- Set/update
  - `SetColor(name, OverlayColorData)`, `SetRawColor(name, OverlayColorData)`, `SetColorValue(name, Color)`
  - `UpdateColors(triggerDirty=false)`: sync component colors into current recipe and update per-overlay property blocks.
- Load/restore
  - `LoadBodyColors(colors, apply)`, `LoadWardrobeColors(colors, apply)` split by whether color names exist in base race recipe.
  - `RestoreCachedBodyColors(apply, fullRestore)`, `RestoreCachedWardrobeColors(apply, fullRestore)` from startup cache.
- Save behavior
  - `ensureSharedColors` forces all component colors into the saved recipe.

--------------------------------------------------------------------------------

## Animator and Expressions

- `SetAnimatorController(bool addAnimator)` selects controller from `raceAnimationControllers` based on active race or cross-compatible races.
- `SetExpressionSet(bool addPlayer)` attaches `UMAExpressionPlayer` and assigns `RaceData.expressionSet` (Humanoid only). `InitializeExpressionPlayer` re-initializes after build.

--------------------------------------------------------------------------------

## Load/Save (Recipes)

- Load options
  - `LoadOptions`: loadRace, loadDNA, loadWardrobe, loadBodyColors, loadWardrobeColors.
  - `SetLoadString(string)`, `SetLoadFilename(filename, loadPathType)`; `DoLoad()` processes according to `loadPathType`.
  - `LoadFromRecipe(UMATextRecipe, LoadOptions)`, `LoadFromRecipeString(string, LoadOptions, ClearWardrobe=false)`.
  - `LoadAvatarDefinition(AvatarDefinition, ...)` or from JSON string; supports partial loads and DNA optimization.
- Save options
  - `SaveOptions`: saveDNA, saveWardrobe, saveColors, saveAnimator.
  - `GetCurrentRecipe(bool backwardsCompatible=false)` returns a DCS recipe string; 
  - Partial saves: `GetCurrentWardrobeRecipe`, `GetCurrentColorsRecipe`, `GetCurrentDNARecipe`.
  - `DoSave(bool saveAsAsset=false, string path="", SaveOptions opts=useDefaults)` writes a DCS recipe (as .asset or .txt).

Compatibility formats
- Legacy UMATextRecipe (non-DCA) is supported; DCA’s DCS recipes are smaller and preserve Wardrobe.

--------------------------------------------------------------------------------

## Build Pipeline (Overview)

1) Aggregate recipes
   - Base `RaceData.baseRaceRecipe` + `_wardrobeRecipes` + `_additiveRecipes` + `umaAdditionalRecipes`.
   - Respect suppressions (wardrobe slots), hides, hide tags; collect MeshHide assets.
2) DNA & Colors pre-process
   - `predefinedDNA` applied; wardrobe `OverrideDNA` temporarily applied; `UpdateColors()`
3) Slot post-processing
   - Wildcards, swap slots, cross-compat equivalents, hidden slots removed.
4) Merge & Generate
   - Merge overlays and slots; set alt materials; run MeshHide masks; optional smoosh/clipping plane processing.
   - Rebuild skeleton/animator as needed; generate with `UMAGenerator`.

Optimization guards
- Editor: `editorTimeGeneration` provides preview.
- `DCA_OPTIMIZED` (optional define) reduces allocations and re-builds when wardrobe/race unchanged.

--------------------------------------------------------------------------------

## Addressables

- When Addressables are enabled, `LoadCharacter()` may enqueue Addressables preload and resume build upon completion to ensure referenced assets are loaded.
- `DelayUnload` controls delayed handle unload after swaps.

--------------------------------------------------------------------------------

## Common Tasks (API Cheatsheet)

- Change race:
  - `avatar.ChangeRace("HumanFemale", DynamicCharacterAvatar.ChangeRaceOptions.keepDNA | keepWardrobe);`
- Set wardrobe:
  - `avatar.SetSlot("Chest", "TShirt_Blue");`
  - `avatar.AppendWearableItem(uwrHat);`
  - `avatar.ClearSlot("Chest");`
- Colors:
  - `avatar.SetColorValue("Skin", new Color(1,0.8f,0.7f));`
  - `avatar.UpdateColors(true);`
- DNA:
  - `avatar.SetDNA("height", 0.65f, rebuild:true);`
- Save/Load:
  - `var recipe = avatar.GetCurrentRecipe();`
  - `avatar.LoadFromRecipeString(recipe);`
- Build control:
  - `avatar.BuildCharacterEnabled = false;` // batch edits
  - ... set race/wardrobe/colors/DNA ...
  - `avatar.BuildCharacterEnabled = true;`

--------------------------------------------------------------------------------

## Troubleshooting

- Pink or missing mesh
  - Verify UMAMaterial and shader bindings; ensure generator set on scene.
- Wardrobe not applying
  - Check `compatibleRaces` and active race; verify slot name and conflicts with suppression/hides.
- Seams/overlay order
  - Overlays on a slot are resorted (cutouts drawn last); ensure intended blend modes.
- Animator/expressions not working
  - Confirm race animator mapping; Humanoid required for expressions; `SetAnimatorController` and `SetExpressionSet` are called on build.
- DNA not changing
  - If using override DNA (from wearables), edits may be restored post-build; adjust `predefinedDNA` or modify after `CharacterUpdated`.

--------------------------------------------------------------------------------

## Editor Notes

- `GenerateSingleUMA()` previews the character (Editor only), with optional placeholder meshes.
- Inspector fields support race selection, wardrobe defaults, race animation mappings, colors, and save/load paths.

--------------------------------------------------------------------------------

## See Also

- `Docs/RaceData.md` — races, DNA converters, renderer bounds
- `Docs/OverlayDataAsset.md`, `Docs/SlotDataAsset.md` — content assets used by wardrobe/recipes
- `Docs/Textures-UDIM-Arrays.md` — UDIM arrays for advanced texture layouts
- `Docs/Addressables.md` — UMA Addressables support
