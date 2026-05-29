# What's New in UMA 3

UMA 3 is the next major branch of UMA. It is currently represented by the repository's `develop` branch and has not yet been merged into the default branch. This document is written for users who want to know what they can do differently in UMA 3, what to try first, and what to watch for when moving content from UMA 2.

Scope of this document:
- Compared `origin/develop` against `origin/master` on May 27, 2026.
- `origin/master` is the repository's default branch in this checkout, so it is treated here as the current main-line baseline.
- The merge base used by Git was `1ab11b099730cfa78259aba6f875cdec230ca766`.
- The branch diff is very large and includes many generated assets, imported assets, scene changes, and project cleanup. This document focuses on user-facing changes.

## At A Glance

UMA 3 is a broad modernization of UMA's content, authoring tools, runtime APIs, and sample scenes.

The biggest changes for most users are:
- A new UMA 3 content set under `Assets/UMA/UMA3`.
- A new set of UMA 3 sample scenes covering character creation, random characters, decals, save/load, DNA sliders, item equipping, Timeline, photobooth tooling, and runtime construction.
- A rewritten DNA path that can drive blendshapes, bone transforms, bone poses, mesh modifiers, overlay UVs, and shared colors through a unified set of DNA effects.
- Better wardrobe authoring, including overlay positioning in the recipe editor and placeholder/wildcard slots for overlay-only recipes.
- A cleaner wearable item API on `DynamicCharacterAvatar` for setting, appending, removing, clearing, and querying equipped items.
- New and improved mesh hide, mesh modifier, decal, texture utility, UDIM, and ShaderGraph workflows.
- Better runtime setup and validation, including generator creation/help in the Welcome window and a stronger recommendation to rebuild the UMA Library after importing a beta update.

## Start Here

After importing or updating UMA 3:

1. Open the UMA Welcome window.
2. Rebuild the UMA Library so the asset index reflects the new folder layout and content.
3. Open one of the UMA 3 sample scenes in `Assets/UMA/UMA3/Scenes`.
4. Try the Character Creator scene first if you want an end-user view of the new wardrobe, DNA, and color workflows.
5. Try the Decals scene if you are evaluating the new decal system.
6. Try the construction and save/load scenes if you are integrating UMA into runtime code.

Useful entry scenes include:
- `Assets/UMA/UMA3/Scenes/U3-Character Creator.unity`
- `Assets/UMA/UMA3/Scenes/U3-How to equip items.unity`
- `Assets/UMA/UMA3/Scenes/U3-How to Use a Slider to control DNA.unity`
- `Assets/UMA/UMA3/Scenes/U3-How to Load and Save a DCA to a string.unity`
- `Assets/UMA/UMA3/Scenes/U3-Generating Random Characters.unity`
- `Assets/UMA/UMA3/Scenes/U3-Decals.unity`
- `Assets/UMA/UMA3/Scenes/U3-Integrating with Timeline.unity`
- `Assets/UMA/UMA3/Scenes/U3-Tools-Photobooth.unity`
- `Assets/UMA/UMA3/Scenes/U3-Ragdolls and Shooting Example.unity`
- `Assets/UMA/UMA3/Scenes/U3-How to Construct a DCA from scratch.unity`
- `Assets/UMA/UMA3/Scenes/U3-How to Construct and load a DCA from a prefab.unity`

## New UMA 3 Content Library

UMA 3 adds a new content tree at `Assets/UMA/UMA3`. This is the main place to look for the new beta-era content and examples.

Important folders include:
- `Animation`: UMA 3 pose and idle animation assets.
- `Colors`: shared color tables for eyes, hair, lips, makeup, skin, and tattoos.
- `Decals`: decal sample assets, overlays, materials, and demo content.
- `DNA`: body, face, pose, and race DNA assets for the new DNA workflows.
- `Documentation`: UMA 3 PDFs plus archived UMA 2 documentation.
- `Expressions`: expression-related content.
- `FBX`: imported model sources and supporting assets.
- `Getting Started`: starter prefab content.
- `MeshModifiers`: mesh modifier examples.
- `Physics`: physics and secondary motion content.
- `Races`: UMA 3 race assets.
- `RandomCharacters`: random-character setup assets.
- `Scenes`: UMA 3 sample scenes.
- `Settings`: UMA 3 settings assets.
- `Textures`: UMA 3 texture content.
- `Wearables`: the new UMA 3 wardrobe and wearable item library.

The `Wearables` folder contains a much larger wardrobe set than the old samples, including hair and beards, wardrobe items, hide assets, icons, medieval items, underlayers, sportswear, shirts, shoes, skirts, pants, and color-variant clothing.

## Better First-Run And Project Setup

The Welcome window now presents UMA 3 Beta guidance and a dedicated What's New section. It directs users to rebuild the UMA Library after importing a new beta update and provides checks for common setup problems.

User impact:
- First-time setup is clearer.
- Missing or inactive generators are easier to detect and fix.
- The Welcome window can help add or activate an UMA Generator and set safer generator defaults.
- UMA 3 is moving toward less manual generator setup; the branch notes call out runtime generator creation as a major direction.

When updating from an earlier UMA 3 beta, rebuild the library before testing scenes or judging missing content. Many systems rely on the asset index to locate recipes, races, overlays, slots, addressable metadata, and generated content.

## New DNA System

UMA 3 introduces a new DNA architecture alongside legacy DNA support. `RaceData` now includes a `useNewDNA` flag and a `DNACollection`, and the new runtime DNA code lives under `Assets/UMA/Core/Scripts/NewDNA`.

The new DNA system is built around DNA groups, DNA instances, curves, and effects. Effects can drive multiple kinds of changes:
- Blendshape effects.
- Bone pose effects.
- Bone rotation, scale, translation, and transform effects.
- Mesh modifier effects.
- Overlay UV transform effects.
- Shared color effects.
- Shared color channel and property effects.

User impact:
- Artists and technical artists can build more varied characters from one model and one race family.
- Runtime developers get a more flexible customization model than old fixed DNA converters.
- DNA can now participate in visual systems beyond only classic morph and bone changes.
- The new UMA 3 DNA assets under `Assets/UMA/UMA3/DNA` provide examples for body, face, pose, and race-driven setup.

Relevant files and folders:
- `Assets/UMA/Core/Scripts/NewDNA`
- `Assets/UMA/UMA3/DNA`
- `Assets/UMA/UMA3/Documentation/Working with the new DNA.pdf`
- `Assets/UMA/Core/StandardAssets/UMA/Scripts/RaceData.cs`

## Race And Model Authoring Improvements

UMA 3 includes a new UMA model direction with blendshape and race-generation support. The goal is to allow more races and body variations to share one base model and a more consistent authoring pipeline.

User-facing improvements include:
- Race-based baked blendshape support.
- Race-level manual renderer bounds for cases where generated bounds are not stable enough.
- New race assets in the UMA 3 content folder.
- Cross-compatible race workflows continue to be important for wardrobe reuse.

Migration note: old `RaceData.backwardsCompatibleWith` is deprecated. Use Cross Compatible Races instead. The code still contains migration support for older assets, but new work should use the cross-compatibility settings exposed by RaceData and related inspectors.

## Wardrobe And Wearable Item Workflow

UMA 3 continues to center normal player-facing equipment around wardrobe recipes, but the authoring workflow is much stronger.

Major improvements include:
- Overlay positioning tools in the Wardrobe Recipe Editor.
- Alignment dialogs for moving and matching overlay rectangles.
- Placeholder slots that are not backed by a `SlotDataAsset`.
- Wildcard and placeholder slot matching by tags.
- Race restrictions for wildcard and placeholder behavior.
- Better matching criteria and slot inspection in the recipe editor.
- Cleaner shared color handling for skin, hair, fabric, makeup, tattoo, and dye channels.
- Better support for mesh hide assets, mesh hide collections, and mesh modifiers on wardrobe recipes.

Placeholder slots are especially important. They let a recipe carry overlays without adding a rendered mesh. At build time, UMA matches the placeholder's tags against real target slots and applies the overlays there. This is useful for tattoos, makeup, decals, scars, dirt layers, fabric details, and any overlay-only item that should attach to different body or clothing variants.

For details, see:
- `Docs/WardrobeRecipeEditor.md`
- `Docs/DynamicCharacterAvatar.md`
- `Docs/SlotDataAsset.md`
- `Docs/OverlayDataAsset.md`

## Wearable Item Runtime API

`DynamicCharacterAvatar` now has a clearer wearable item API for runtime inventory, character creator, and equipment screens.

Useful methods include:
- `SetWearableItem(UMAWardrobeRecipe)`: equip or replace the main wearable for its wardrobe slot.
- `AppendWearableItem(UMAWardrobeRecipe)`: stack an additive wearable on the same slot.
- `RemoveWearableItem(UMAWardrobeRecipe, bool removeAllMatching = false)`: remove one wearable or all matching instances.
- `ClearWearableItems(string region, bool clearWardrobeCollectionItems = true)`: clear a wardrobe region and optionally remove collection-provided items too.
- `GetAppendedWearableItems(string region)`: get additive wardrobe recipes stacked on a region.
- `GetAppendedItems(string region)`: get additive text recipes stacked on a region.

User impact:
- Character creator UI code can work with wearable items directly instead of manually juggling slot dictionaries.
- Additive items such as tattoos, makeup, decals, accessories, and layered detail recipes are easier to stack.
- Inventory systems can distinguish replacing a base item from appending a secondary item.

For a deeper runtime guide, see `Docs/DynamicCharacterAvatar.md`.

## Mesh Hide, Mesh Modifiers, And Vertex Editing

UMA 3 includes substantial work on mesh hiding and mesh modification workflows.

Mesh hide improvements include:
- `MeshHideAssetCollection` support.
- Compression-related updates.
- Better editor workflows for selecting and painting hidden geometry.
- Raycast occlusion tools in the face/editor stage for generating hide data from visible slot occlusion.
- Better validation around submeshes and LOD data.

Mesh modifier improvements include:
- New and updated mesh modifier editor paths.
- Mesh modifier examples under `Assets/UMA/UMA3/MeshModifiers`.
- Integration with the new DNA effect system through mesh modifier DNA effects.
- Documentation in `Assets/UMA/UMA3/Documentation/Instruction Manual for Using Mesh Modifiers with UMA.pdf`.

User impact:
- Clothing can hide covered body triangles more reliably.
- Content creators have better tools for fixing clipping and deformation problems.
- DNA, wardrobe recipes, and mesh modifiers can work together more cleanly.

For details, see `Docs/MeshHideAssets.md`.

## Decal System Improvements

UMA 3 adds a much more complete decal workflow. The branch includes both slot-based decals and RenderTexture-based decal stamping.

Highlights include:
- `DecalSlotBuilder` for building decal slots from selected triangles.
- `DecalRenderTexture` for stamping overlay textures into UMA-generated render textures.
- `DecalRTStampAsset` and `DecalRTStampSlot` for saving and replaying RenderTexture decal stamps.
- Decal dilation shaders to reduce visible edges.
- Better handling for slot groups when replaying stamps or matching decal targets.
- A dedicated UMA 3 decal scene and decal sample assets.

User impact:
- You can build gameplay-facing tattoos, scars, wounds, marks, makeup, or painted details more directly.
- RenderTexture decals can be saved and restored instead of being purely temporary runtime state.
- The sample scene gives a practical place to test placement, scale, rotation, dilation, and target matching.

For details, see:
- `Docs/Decals.md`
- `Assets/UMA/Core/Decals`
- `Assets/UMA/UMA3/Scenes/U3-Decals.unity`

## Textures, UDIMs, ShaderGraphs, And Materials

UMA 3 adds new texture and rendering workflows for modern Unity projects.

Highlights include:
- UDIM support in slot builder workflows.
- Texture2DArray generation and documentation.
- UMA 3 ShaderGraph assets for URP and HDRP skin, hair, and lit materials.
- Updated shader packages and UMA material assets.
- Multiple RenderTexture format support in the texture processing path.
- Updated shared color tables and color lookup workflows.

User impact:
- Artists can use larger, multi-tile texture layouts when needed.
- Technical artists can author modern ShaderGraph materials for UMA 3 content.
- Runtime builds can handle more texture format combinations.
- Shared colors are easier to standardize across skin, hair, eyes, makeup, tattoos, and wardrobe items.

Relevant files and docs:
- `Docs/Textures-UDIM-Arrays.md`
- `Assets/UMA/Core/ShaderGraphs`
- `Assets/SourceShaders`
- `Assets/UMA/UMA3/Colors`
- `Assets/UMA/Core/StandardAssets/UMA/Scripts/TextureProcessPro.cs`

## Performance And Runtime Generation

The UMA 3 branch includes many runtime and editor performance improvements. Most users will feel these as faster or more stable character builds, especially when working with many slots, overlays, blendshapes, or generated assets.

Highlights include:
- Job/Burst-aware mesh combining code paths.
- Array pooling in mesh-combining hot paths to reduce garbage collection pressure.
- A Mesh API combiner option in UMA settings.
- Texture merge capacity reuse and RenderTexture format handling.
- Runtime generator setup and validation improvements.
- Better behavior around renderer regeneration and renderer bounds.

User impact:
- Character generation should be more robust in complex scenes.
- Large wardrobe stacks and high-overlay characters are better supported.
- Runtime projects have more settings to tune generation and combiner behavior.

Because performance depends heavily on project content, platform, pipeline, and settings, test your own scenes before making release decisions.

## Addressables And Asset Indexing

Addressables support continues to be part of the UMA 3 workflow.

Highlights include:
- Recipe and dependency export to Addressables.
- Label and group generation through the `SingleGroupGenerator` editor plugin.
- Texture stripping and restoration metadata for overlays.
- Asset index entries that can track addressable flags, addresses, labels, and groups.
- Runtime preloading paths that can resume character builds after addressable assets are available.

User impact:
- Large UMA libraries can be streamed or split into downloadable groups.
- Overlay textures and recipe dependencies can be made addressable while still being discoverable by UMA.
- Rebuilding the UMA Library is more important after moving content or changing addressable settings.

For details, see:
- `Docs/Addressables.md`
- `Docs/UMAAssetIndexer.md`

## Sample UI And Runtime Examples

UMA 3 includes a new sample UI path under `Assets/UMA/UMA3/Scenes/Scripts`.

Examples include:
- `NewUMAGUI`: character creator style UI.
- `ConstructDCAFromScratch`: build a DCA through code.
- `ConstructDCAFromAPrefab`: construct and load from a prefab path.
- `SaveAndLoadSample`: save and load a DCA to a string.
- `SliderController`, `DNAEffector`, and `ColorEffector`: drive DNA and colors from UI.
- `ItemEffector`: equip and change items from UI.
- `SceneLoader`: move between sample scenes.
- `LODDisplay`, `StatDisplayer`, and `HUDFPS`: sample diagnostics.

User impact:
- New users have more complete examples to copy from.
- Runtime developers can see practical code paths for common character creator tasks.
- UI implementers can follow the sample selector interfaces for DNA, color, and item choices.

## Documentation Added Or Updated

The branch includes new docs in `Docs` plus UMA 3 PDFs under `Assets/UMA/UMA3/Documentation`.

Start with:
- `Docs/DynamicCharacterAvatar.md`
- `Docs/WardrobeRecipeEditor.md`
- `Docs/SlotDataAsset.md`
- `Docs/OverlayDataAsset.md`
- `Docs/RaceData.md`
- `Docs/MeshHideAssets.md`
- `Docs/Decals.md`
- `Docs/Textures-UDIM-Arrays.md`
- `Docs/Addressables.md`
- `Docs/UMAAssetIndexer.md`

Also see:
- `Assets/UMA/UMA3/Documentation/Working with the new DNA.pdf`
- `Assets/UMA/UMA3/Documentation/Instruction Manual for Using Mesh Modifiers with UMA.pdf`

## Migration Notes From UMA 2

UMA 3 is a beta branch and contains significant asset, API, and workflow changes. When upgrading a project, treat the upgrade as a migration, not a small patch.

Recommended migration checklist:

1. Back up your project before importing UMA 3.
2. Import or switch to UMA 3.
3. Rebuild the UMA Library.
4. Open the Welcome window and run its checks.
5. Test the UMA 3 sample scenes before migrating your own content.
6. Review custom races for new DNA settings, cross-compatible race settings, and renderer bounds.
7. Review custom wardrobe recipes for placeholder slots, matching criteria, shared colors, mesh hides, and overlay positioning.
8. Review custom code that referenced old library/context APIs.
9. Review custom DNA converters and decide whether to keep legacy DNA or migrate to the new DNA collection/effect system.
10. Recheck Addressables labels and groups if your project streams UMA content.
11. Reimport or update custom shaders and materials if you are moving to the UMA 3 ShaderGraph or SRP material workflows.

Compatibility notes:
- `UMAContext` is removed in UMA 3. The asset indexer contains emulation-style helper functions for older lookup patterns, but new code should use the current asset indexer and settings paths.
- `RaceData.backwardsCompatibleWith` is deprecated. Use Cross Compatible Races.
- Legacy DNA can still exist, but new UMA 3 races can opt into the new DNA system with `useNewDNA` and a `DNACollection`.
- UMA 2 and UMA 3 content are split more clearly. Check paths before assuming old sample locations still apply.
- Some branch changes are beta cleanup: removed temp/test content, moved examples, updated packages, and refreshed generated assets.

## What To Try First By Role

For character artists:
- Open `U3-Character Creator.unity`.
- Inspect shared color tables in `Assets/UMA/UMA3/Colors`.
- Open wardrobe recipes under `Assets/UMA/UMA3/Wearables`.
- Try overlay positioning in the Wardrobe Recipe Editor.
- Review placeholder slots for overlay-only items.

For technical artists:
- Review `Assets/UMA/UMA3/DNA` and the new DNA PDF.
- Try the DNA slider sample scene.
- Review ShaderGraphs in `Assets/UMA/Core/ShaderGraphs`.
- Review mesh modifiers and mesh hide workflows.
- Try the decal scene and compare slot decals with RenderTexture decals.

For runtime developers:
- Read `Docs/DynamicCharacterAvatar.md`.
- Try the DCA-from-scratch and prefab construction scenes.
- Try the save/load scene.
- Use the wearable item API instead of manually editing slot dictionaries.
- Review Addressables docs if your game streams characters or cosmetics.

For teams upgrading from UMA 2:
- Keep UMA 2 content backed up.
- Migrate one race and one wardrobe set at a time.
- Rebuild the library after each major content move.
- Test race changes, wardrobe changes, shared colors, DNA, mesh hides, and save/load before migrating the next content group.

## Bottom Line

UMA 3 is not just a content refresh. It updates how UMA characters are authored, customized, rendered, equipped, indexed, and demonstrated. The branch is especially important if you need better character creator workflows, more flexible DNA, modern render-pipeline materials, advanced decals, placeholder/wildcard wardrobe recipes, or a cleaner runtime API for wearable items.
