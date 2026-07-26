# UMA Project Documentation

This workspace contains the UMA (Unity Multipurpose Avatar) core, content, and supporting systems. This documentation provides a concise, practical guide to how the major systems work, which assets they use, and how those assets are consumed at edit-time and runtime.

Contents
- What's New in UMA 3
- Getting started with UMA
- Artist content creation
- Creating a new race from start to finish
- Core concepts and workflow
- Getting started with DynamicCharacterAvatar (DCA)
- How DCA builds characters
- DNA creation and the new DNA system
- Wardrobe Recipe Editor
- Race, slot, and overlay authoring
- Mesh Modifier sculpting
- UMA Materials
- LOD and random-avatar setup
- Renderer assets and cloth
- Mesh hiding and face editing
- Mesh combiners and bone baking
- UMA Generator setup and performance tuning
- Bone animators and secondary motion
- Clothing conformer
- Texture Arrays and UDIMs
- Addressables generation (SingleGroupGenerator)
- Decal system
- UMA Asset Indexer
- Clean-build performance planning

## Core Concepts and Workflow
- UMA builds characters at runtime from `RaceData`, `SlotDataAsset` (meshes), and `OverlayDataAsset` (textures/material data).
- The generator (e.g., `UMAGeneratorPro`/`UMAGeneratorBuiltin`) combines slots and overlays into atlases via `TextureMerge` and produces a skinned mesh with materials.
- Indexing: The `UMAAssetIndexer` (a ScriptableObject under `Resources/AssetIndexer`) tracks all UMA content in the project. Editor tooling and runtime systems use it to resolve assets by name/type.
- Addressables (optional): UMA can export recipes and dependencies into Addressables for streaming/load-on-demand.

Recommended Validation
1. Open `Assets/UMA/UMA3/Scenes/U3-Character Creator.unity`.
2. Enter Play mode and verify the character generates, DNA changes apply, and wardrobe items can be equipped.

New users and content artists should begin with [`GettingStarted.md`](GettingStarted.md) and [`ContentCreation.md`](ContentCreation.md).

## Getting Started with DynamicCharacterAvatar (DCA)
Use `DynamicCharacterAvatar` to build and control UMA characters at runtime.

- Create an avatar
  - Menu: GameObject > UMA > Create New Dynamic Character Avatar, or add the `DynamicCharacterAvatar` component to an empty GameObject.
  - Pick a race in the component (`activeRace`).
- First build
  - Keep `Build Character Enabled` checked to build automatically on Start.
  - Optionally add default wardrobe (clothes) in `Preload Wardrobe Recipes`.
  - Press Play and verify the avatar appears with the chosen race and wardrobe.
- Basic usage
  - Change race at runtime: `ChangeRace("HumanFemale")`.
  - Wear a wardrobe recipe: `SetSlot("Chest", "TShirt_Blue")` or `SetWearableItem(UMAWardrobeRecipe)`.
  - Adjust DNA: `SetDNA("height", 0.65f, rebuild:true)`.
  - Update shared colors: `SetColorValue("Skin", new Color(1,0.8f,0.7f)); UpdateColors(true);`
- Save/Load
  - Save current avatar to an AvatarDefinition string: `GetAvatarDefinitionString(false)`.
  - Load from an AvatarDefinition string: `LoadAvatarDefinition(avatarDefinitionString)`.
  - `GetCurrentRecipe()` and `LoadFromRecipeString(...)` are deprecated - use the AvatarDefinition APIs above instead.

See [`DynamicCharacterAvatar.md`](DynamicCharacterAvatar.md) for lifecycle, events, wardrobe, colors, DNA, saving, Addressables, and troubleshooting.

## How DCA Builds Characters
DCA composes the final character from your race and wardrobe selections and drives UMA's generator to produce meshes and textures.

- Inputs
  - Base Race Recipe from `RaceData` (skeleton, base slots/overlays, defaults)
  - Wardrobe recipes (one per wardrobe slot) + additive recipes (stacked)
  - Shared colors (skin, hair, etc.) and DNA values
- Merge pipeline
  - Resolve and merge slots/overlays
  - Apply Hides/suppressions, cross-compat equivalents, wildcard/swap slots
  - Texture atlas generation via `TextureMerge`
  - Mesh build/combine via `SkinnedMeshCombiner` (bones, submeshes, optionally blendshapes)
  - Animator/expression setup (per race), optional skeleton rebuild
- Addressables (optional)
  - If Addressables are used, DCA preloads assets by label/group and resumes build when loads complete.

Key scenes to test
- `U3-Character Creator` - a full character creation scene, shows how to let the end user customize an UMA.
- `U3-Ragdolls and Shooting Example` - Shows how to hit specific parts of the UMA, Ragdoll it, and revive it.

## Key Editor Windows
- Global Library (UMA Asset Indexer): UMA > Global Library - inspect and manage indexed content.
- Documentation Browser: UMA > View Documentation - open the dockable Markdown document list and select a guide for the UMA Markdown Viewer.
- Prefab Maker: UMA > Prefab Maker - bake a character to a non-UMA prefab.
- Texture Combiner: UMA > Texture Channel Combiner - author/pack channels into single textures.
- Mesh Combiner Switcher: UMA > Tools > Mesh Combiner Switcher - change and test the active mesh combiner.
- UMA Toolbar: use the Scene View's dockable UMA Toolbar for camera state, rebuild modes, combiner selection, skeleton display, diagnostics, and tools.
- Vertex and Face Editors: edit vertex selections, sculpt mesh modifiers, and paint mesh-hide faces from the stage tools.

## Important Folders
- `Assets/UMA/Core/`: Core runtime and editor code.
- `Assets/UMA/UMA3/`: Current UMA 3 races, slots, overlays, wearables, materials, settings, and scenes.
- `Assets/UMA/Examples/`: Samples and demo scenes.
- `Assets/SourceShaders/`: BetterShaders authoring sources for UMA shader variants.
- `Assets/UMA/Docs/`: Current Markdown documentation.

## Support Matrix
- Unity 6.3
- .NET Framework 4.7.1
- C# 9

For detailed topics, see the dedicated docs:
- `WhatsNewInUMA3.md`
- `GettingStarted.md`
- `ContentCreation.md`
- `CreatingANewRace.md`
- `DynamicCharacterAvatar.md`
- `WardrobeRecipeEditor.md`
- `RaceData.md`
- `SlotDataAsset.md`
- `OverlayDataAsset.md`
- `UMAMaterial.md`
- `DNACreationGuide.md`
- `NewDNASystem.md`
- `MeshCombiners.md`
- `MeshHideAssets.md`
- `MeshModifiers.md`
- `MeshModifierSculpting.md`
- `UMASimpleLOD.md`
- `RandomAvatar.md`
- `RendererAssetsAndCloth.md`
- `BoneAnimators.md`
- `ClothingConformer.md`
- `Textures-UDIM-Arrays.md`
- `Addressables.md`
- `Decals.md`
- `UMAAssetIndexer.md`
- `DynamicCharacterBuildOptimizationPlan.md`

## Documentation Map

The Markdown guides in this folder cover the main UMA 3 workflows:

- [`WhatsNewInUMA3.md`](WhatsNewInUMA3.md) - feature and migration summary.
- [`GettingStarted.md`](GettingStarted.md) - generator setup, Global Library setup, and first-avatar workflow.
- [`ContentCreation.md`](ContentCreation.md) - artist workflow from DCC export through slots, overlays, recipes, races, and validation.
- [`CreatingANewRace.md`](CreatingANewRace.md) - complete artist-facing race workflow from source model through runtime validation.
- [`DynamicCharacterAvatar.md`](DynamicCharacterAvatar.md) - DCA lifecycle, wardrobe, DNA, colors, saving, loading, and performance.
- [`WardrobeRecipeEditor.md`](WardrobeRecipeEditor.md) - authoring wearable recipes, overlays, placeholders, and wildcards.
- [`RaceData.md`](RaceData.md) - complete artist-facing race setup, compatibility, bounds, blendshapes, and DNA configuration.
- [`SlotDataAsset.md`](SlotDataAsset.md) - artist workflow for mesh slots, weights, materials, LODs, clipping, and blendshapes.
- [`OverlayDataAsset.md`](OverlayDataAsset.md) - artist workflow for overlay textures, channels, cropped details, colors, and cutouts.
- [`UMAMaterial.md`](UMAMaterial.md) - material types, shader channels, atlas behavior, and texture-reuse settings.
- [`DNACreationGuide.md`](DNACreationGuide.md) - artist-friendly DNA creation workflow.
- [`NewDNASystem.md`](NewDNASystem.md) - DNA groups, instances, curves, and effects.
- [`MeshCombiners.md`](MeshCombiners.md) - available mesh combiners, selection, and testing.
- [`UMAGeneratorSetup.md`](UMAGeneratorSetup.md) - generator prefab setup, parameter guidance, and platform performance profiles.
- [`MeshHideAssets.md`](MeshHideAssets.md) - creating and using mesh-hide assets and collections.
- [`MeshModifiers.md`](MeshModifiers.md) - mesh modifier concepts, authoring modes, recipes, DNA, and blendshapes.
- [`MeshModifierSculpting.md`](MeshModifierSculpting.md) - vertex editing and sculpt-mode mesh modifiers.
- [`UMASimpleLOD.md`](UMASimpleLOD.md) - runtime LOD setup and performance-aware artist guidance.
- [`RandomAvatar.md`](RandomAvatar.md) - random-character components, content pools, and repeatable testing.
- [`RendererAssetsAndCloth.md`](RendererAssetsAndCloth.md) - renderer settings, material routing, cloth preparation, and validation.
- [`BoneAnimators.md`](BoneAnimators.md) - bone animation, sampling, and secondary motion tools.
- [`ClothingConformer.md`](ClothingConformer.md) - conforming clothing meshes to UMA body meshes.
- [`Textures-UDIM-Arrays.md`](Textures-UDIM-Arrays.md) - UDIM, Texture2DArray, and shader workflows.
- [`Addressables.md`](Addressables.md) - exporting and loading UMA content with Addressables.
- [`Decals.md`](Decals.md) - slot decals and RenderTexture decal stamping.
- [`UMAAssetIndexer.md`](UMAAssetIndexer.md) - indexing, rebuilding, and troubleshooting UMA assets.
- [`DynamicCharacterBuildOptimizationPlan.md`](DynamicCharacterBuildOptimizationPlan.md) - planned clean-build performance improvements.
