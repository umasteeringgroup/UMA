# UMA Project Documentation

This workspace contains the UMA (Unity Multipurpose Avatar) core, content, and supporting systems. This documentation provides a concise, practical guide to how the major systems work, which assets they use, and how those assets are consumed at edit-time and runtime.

Contents
- What's New in UMA 3
- Core concepts and workflow
- Getting started with DynamicCharacterAvatar (DCA)
- How DCA builds characters
- Wardrobe Recipe Editor
- Mesh Modifier sculpting
- Texture Arrays and UDIMs
- Addressables generation (SingleGroupGenerator)
- Decal system
- UMA Asset Indexer
- RaceData and DNA
- DNA creation for artists
- Shader notes (UDIM + normal arrays)

## Core Concepts and Workflow
- UMA builds characters at runtime from `RaceData`, `SlotDataAsset` (meshes), and `OverlayDataAsset` (textures/material data).
- The generator (e.g., `UMAGeneratorPro`/`UMAGeneratorBuiltin`) combines slots and overlays into atlases via `TextureMerge` and produces a skinned mesh with materials.
- Indexing: The `UMAAssetIndexer` (a ScriptableObject under `Resources/AssetIndexer`) tracks all UMA content in the project. Editor tooling and runtime systems use it to resolve assets by name/type.
- Addressables (optional): UMA can export recipes and dependencies into Addressables for streaming/load-on-demand.

Recommended Validation
1. Open Examples: `Assets/UMA/Examples/SceneLoader/SceneLoader.unity`.
2. Play the DCS demo scene to verify DNA sliders, wardrobe, and race switching work without errors.

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

See `Docs/DynamicCharacterAvatar.md` for a deep dive (lifecycle, events, wardrobe collections, colors, DNA, Addressables flow, troubleshooting).

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
- Prefab Maker: UMA > Prefab Maker - bake a character to a non-UMA prefab.
- Texture Combiner: UMA > Texture Channel Combiner - author/pack channels into single textures.

## Important Folders
- `Assets/UMA/Core/`: Core runtime and editor code.
- `Assets/UMA/Content/`: Shared UMA content (races, slots, overlays, UMAMaterials).
- `Assets/UMA/Examples/`: Samples and demo scenes.
- `Assets/SourceShaders/`: BetterShaders authoring sources for UMA shader variants.
- `Docs/`: This documentation.

## Support Matrix
- Unity 6.3
- .NET Framework 4.7.1
- C# 9

For detailed topics, see the dedicated docs:
- `WhatsNewInUMA3.md`
- `DynamicCharacterAvatar.md`
- `WardrobeRecipeEditor.md`
- `MeshModifierSculpting.md`
- `Textures-UDIM-Arrays.md`
- `Addressables.md`
- `Decals.md`
- `UMAAssetIndexer.md`
- `RaceData.md`
- `DNACreationGuide.md`
- `NewDNASystem.md`
- `Shaders-UDIM.md`
