# What's New in UMA 3

## What's New Since the UMA 3.02 Release

UMA 3.04 is the cumulative release from UMA 3.02 forward. It includes everything previously prepared for the unreleased 3.03 together with the package, sample, reliability, and release-readiness work completed afterward; 3.03 is not a separate upgrade prerequisite.

### New Authoring and Animation Tools

- Added Overlay Painter, with synchronized 2D and 3D painting, brushes, paths, masks, layer groups, channel-aware compositing, normal generation from height data, and texture/overlay export.
- Expanded Overlay Painter with persistent production Generator and Filter layers, including cloth, organic and metal weathering, Quilt, Embroidery, Perforation, Atlas Scatter, multi-channel or mask-only Text with editable ribbon guides, and accelerated Kuwahara, quantization, and toon stylization.
- Improved Overlay Painter persistence, recovery, history, plugin transactions, export validation, normal controls, and optional 2D Sprite grid integration. Package installations keep writable documents, exports, and recovery data in the project rather than modifying the installed package.
- Added the Dynamic Expression Player, providing race-specific expression groups, layered input sources, procedural eye and blink support, runtime bone, blendshape, and material effects, plus legacy migration and diagnostics.
- Improved Icon Creator with deterministic thumbnail generation, supersampled camera captures, stable asset paths and GUIDs, safer overwrites, and optional Sprite Atlas V2 generation organized by race and wardrobe region.
- Added posed-character skinning-weight touchup, including brush-based vertex selection, weight visualization, normalization, preview, and saving changes to the source SlotDataAsset.
- Expanded Slot Builder and UDIM workflows with in-place slot updates, preserved metadata, improved seam and weld handling, UDIM inspection, and direct Overlay Painter access for slots and UDIM groups.
- Added the Pelvis Controller Animator and expanded secondary animation tooling and tests for pelvis, shoulder, torso, and leg correction.
- Added and documented new asset-discovery, consolidation, animation, pose, race, prefab and scene, texture, slot, overlay, and wearable utilities.

### Package Manager and Project Data

- Added full Unity Package Manager support for installing UMA from `Packages/com.umasteeringgroup.uma`, while retaining the traditional `Assets/UMA` source-development layout.
- Added `com.umasteeringgroup.uma2` as an optional legacy-content package. Its version tracks the base UMA package and its only package dependency is `com.umasteeringgroup.uma`.
- Added package-location-independent asset resolution so settings, the Generator Prefab, Global Library, shaders, documentation, Welcome scenes, defaults, and other installed resources load correctly from either `Assets` or `Packages`.
- Added cached UMA settings resolution. Project Settings, generation, the Welcome Window, and editor tools now share the correct active settings without repeatedly loading the asset during character generation.
- Added project-owned writable data under `Assets/UMAProjectData` for package installs, including settings overrides, the Global Library, generated assets, Overlay Painter documents and recovery data, tasks, captures, and test artifacts. Installed package content remains read-only and update-safe.
- Reorganized assembly definitions and corrected duplicate GUIDs introduced by moved bridge files. Addressables, FBX Exporter, 2D Sprite, and Test Framework integrations are isolated behind dependency-neutral bridges or constrained assemblies, preventing duplicate assemblies and missing-package compilation failures.
- Removed HDRP as a forced consumer dependency. The UPM package installs the URP/core shader dependencies it uses, while HDRP support remains optional instead of being pulled into URP projects.
- Added a Package Dependencies window for traditional `Assets/UMA` imports and clearer guidance for required and optional integrations.
- Prevented generated Input System wrappers and editor scripts from creating duplicate types or leaking into consumer `Assembly-CSharp` assemblies.
- Improved Global Library duplicate resolution so valid package assets win over stale, null, or legacy duplicates, including core assets such as the Capsule Collider slot.

### Welcome Window and Editor Reliability

- Updated the Welcome Window to display the active installed UMA version and settings, open sample scenes from either supported install location, and open this file in the Documentation Browser from **What's New**.
- Restored **Recompile Shaders** with package-aware shader discovery and material repair.
- Reworked **Scan Scene** to check current UMA 3 setup problems such as settings and Generator Prefab validity, unresolved avatar races or starting recipes, disabled generation, missing generated meshes, and broken materials or shaders.
- Reworked **Scan Project** to validate the Global Library, races, slots, overlays, materials, text and wardrobe recipes, and wardrobe collections without rewriting content.
- Improved editor resilience during imports and domain reloads, including null-target handling in the Global Library inspector, balanced custom inspector layout scopes, and stable object/session identifiers.
- Disabled **Load File On Start** by default for newly configured Dynamic Character Avatars while retaining it for intentionally configured recipe-loading workflows.

### Runtime, Rendering, and Sample Content

- Improved WebGL and no-Burst support, added optional UMA Toolbar visibility, and expanded DNA cleanup, validation, and editor diagnostics.
- Corrected Dynamic Expression Player saccades so both eyes glance together instead of mirroring into crossed or splayed poses, and corrected runtime animation-curve test setup for non-Legacy clips.
- Improved texture and editor reliability, including transparent atlas prefill, overlay-level base-color multiplication, wardrobe-recipe cloning and conversion, project-item status display, and broader automated test coverage.
- Corrected transient physics and jiggle-prefab serialization and reserialized affected assets to prevent inconsistent Prefab Importer results.
- Updated UMA3 sample scenes and prefabs to use only UMA3 or shared Core assets. Legacy UMA2 references were removed, animations and controllers were repaired, and the samples now work when the optional UMA2 folder is not installed.
- Fixed Random Avatar setup so generated characters receive a valid Animator Controller before building, and kept generated LOD meshes readable when runtime LOD processing needs to rewrite indices.
- Added a root-motion Random Character Walker to the random-generation sample, with bounded wandering, pauses, speed-driven animation, character avoidance, crowd separation, and stall recovery. The Challenger locomotion controller now uses `Chal_Idle` and `Chal_Walk`.
- Refreshed sample materials, renderer assets, races, recipes, T-poses, DNA poses, physics definitions, and pipeline-specific content for reliable clean-project imports.

### Validation and Test Coverage

- Added Release Asset Validation with structured reports and guided repair actions for missing references, stale serialized GUIDs, misplaced dependencies, and non-applicable material properties.
- Added explicit boundary checks for UMA3 and optional UMA2 sample assets. Each sample set may reference only its own folder and permitted shared Core content, and all tests run when UMA2 is absent.
- Added package-readiness, assembly-dependency, GUID-location, settings and Generator Prefab, Global Library, scene-reference, race smoke, renderer, thumbnail, and repair-utility tests.
- Added clean-package checks that guard against duplicate assembly definitions, duplicate GUIDs, accidental package writes, forced optional integrations, legacy sample dependencies, and consumer-project type conflicts.
- Standardized the supported editor baseline on Unity 6.3 and newer.

## What's New Since the UMA 3.01 Release

- Added the Incremental Mesh Combiner, which spreads expensive mesh generation across frames while keeping the current character visible and animated until its replacement mesh is ready.
- Moved more mesh preparation, blendshape processing, source analysis, and mesh-modifier work off the main thread, with successful validation caching to reduce repeated work.
- Added detailed incremental-generation statistics, including per-step average and maximum times, budget overruns, runtime timing, and CSV export for crowd profiling.
- Improved generator reliability, including automatic scene-generator creation, safe cleanup when an avatar is destroyed during generation, and better toolbar and combiner-switching support.
- Completed the Unity Spring Joint Animator and expanded the bone-animation documentation and tests.
- Improved the crowd and character-creator samples, including restartable crowd profiling, timing resets, camera navigation fixes, and generator diagnostics.
- Reorganized shared Core and SRP assets so the optional `Assets/UMA/UMA3` sample and content folder can be removed without breaking retained UMA systems.
- Refreshed the documentation browser, artist-facing guides, shader assignments, materials, and release folder layout.

## Changes Since the Initial UMA 3.0 Release

- Added faster jobified and bone-baking mesh combiners, parallel processing, and generation optimizations.
- Introduced MeshModifier sculpting, multiple brush modes, AutoSculpt, clothing-wide sculpting, and blendshape export.
- Added the Clothing Conformer workflow for fitting garments to modified characters.
- Expanded decal authoring with RenderTexture decals, stamp editing, documentation, diagnostics, and guided scene instructions.
- Improved HDRP and Shader Graph support, including automated HDRP project setup and material repair.
- Added the UMA Toolbar, diagnostics, documentation browser, recipe graph editor, and streamlined menus.
- Fixed texture leaks, atlas and mesh-only builds, bindposes, DNA accumulation, LOD behavior, recipe saving, and skeleton stability.
- Updated compatibility for recent Unity 6 releases and refreshed sample content, hair, eyebrows, lighting, and scenes.

UMA 3 is the next major branch of UMA. It is currently represented by the repository's `develop` branch and has not yet been merged into the default branch. This document is written for users who want to know what they can do differently in UMA 3, what to try first, and what to watch for when moving content from UMA 2.

Scope of this document:
- The original branch comparison was made on May 27, 2026. The historical long-form notes below cover the follow-up work through July 16, while the cumulative release summary above continues through the current package baseline.
- `origin/master` is the repository's default branch in this checkout, so it is treated here as the current main-line baseline.
- The merge base used by Git was `1ab11b099730cfa78259aba6f875cdec230ca766`.
- The branch diff is very large and includes generated assets, imported assets, scene changes, and project cleanup. This document summarizes the user-facing code, tooling, workflow, documentation, content, and reliability changes.

## Updates Since the Original May 29 Snapshot

The following changes landed after the first version of this document and are included in the current UMA 3 branch through July 16, 2026.

### Bone Baking And Mesh Combiners

- Added the `UMADefaultBoneBakingMeshCombiner`, derived from the default combiner pipeline, with `UMABoneBakingMeshCombiner` retained as a compatibility component for existing scenes.
- Added shared retargeting, mesh-building, and improved-skeleton support for baking bone influence into generated meshes while preserving explicitly unbaked animated bones.
- Added parallel/jobified bone-baking skinning through `BoneBakingSkinningJobContext` and the retargeting path.
- Fixed accumulated bindposes, DNA accumulation, rig-only rebuild state, hips rotation preservation, and skeleton recovery when switching between baked and normal combiners.
- Fixed atlas UV preservation for mesh-only and rig-only builds, including bone-baking and jobified combiner paths.
- Added second-pass material support and improved jobified-combiner pass-two behavior.
- Added bone-baking skeleton tests covering default and compatibility combiners, rig-only UV behavior, bindposes, DNA effects, preserved bones, and jobified paths.
- Added an editor combiner switcher and exposed both bone-baking combiners in the combiner tests, editor UI, `NewUMAGUI`, documentation, and the UMA Toolbar.
- Expanded `NewUMAGUI` timing tests to cover all supported combiners and added a controlled option to load blendshapes normally or bake every available blendshape at value `0.5`.

### UMA Toolbar And Diagnostics

- Added a dockable Scene View overlay named **UMA Toolbar**.
- Moved Scene View camera save/restore actions into the toolbar.
- Added selected-UMA full, rig/DNA-only, mesh-only, and texture-only rebuild actions, plus a rebuild-all-editor-UMAs action.
- Added active-combiner selection, UMA focus targets, skeleton and bone-name display, automatic editor-generation pause, and a tools menu.
- Added selected-UMA diagnostics showing race, dirty flags, skeleton and generated-mesh counts, active combiner, generator timings, and rebuild timing.
- Added runtime-data inspection and expanded diagnostics access from UMA editor tools.

### Texture Lifetime, Atlas Reliability, And Resource Cleanup

- Added names and ownership metadata to UMA atlas `RenderTexture` instances so textures can be associated with a character, atlas, material, and channel.
- Added the **UMA Render Textures** diagnostics window, grouped by character, with refresh, auto-refresh, selection, orphan detection, and release/destroy actions.
- Improved cleanup of temporary, persistent, pending-readback, and replaced atlas textures to reduce leaks during repeated builds.
- Added safer texture saver and normalizer cleanup paths and improved RenderTexture-to-CPU cleanup accounting.
- Added support for forcing selected texture channels to use existing atlas textures to reduce VRAM use.
- Fixed material parameter initialization when existing textures are used, normal-map handling for already-swizzled data, and several SRP texture/material edge cases.
- Organized SRP-specific materials and shader assets and added HDRP diffuse ShaderGraph support alongside the existing URP workflows.

### DNA, Skeleton, And Pose Authoring

- Added and expanded bone-pose conversion, build, mixing, and editing workflows, including IK editing and improved pose diagnostics.
- Added bone animator samples and improved bone visualizer tooling.
- Fixed DNA accumulation in bone-baking builds and corrected several bone-scale, bone-transform, and DNA effect interactions.
- Fixed empty DNA lists becoming null during avatar-definition unmarshalling.
- Expanded the new DNA documentation and artist-facing DNA creation guide.

### Mesh Hiding, Vertex Editing, And Sculpting

- Added mesh-modifier sculpt mode with Add, Remove, Smooth, brush shape, radius, falloff, symmetry, and normal-update options.
- Added mesh-hide face selection and raycast-occlusion workflows, including better submesh and LOD validation.
- Updated ignore-tag handling across mesh-hide, overlay, generator, skeleton, and mounted-item paths.
- Corrected suppressed-overlay behavior so suppression is applied to the intended overlay data.
- Added prominent top-right Close buttons to the vertex and face editor stages with Save/Discard/Cancel prompts for unsaved selections and sculpt previews.
- Expanded the artist-facing `MeshHideAssets.md` guide from the MeshHideAssets manual and added separate documentation for mesh combiners and mesh-modifier sculpting.

### Clothing Conformer And Secondary Motion

- Added the Clothing Conformer runtime component, bind-data assets, mesh-conforming utilities, editor, sample bind assets, and editor tests.
- Added and corrected chain-jiggler and secondary-motion workflows, including new UMA chain-jiggle animation support and sample content.
- Updated clothing and sample character scenes to exercise conforming and secondary motion.

### Wardrobe, Recipe, And Editor Workflow Improvements

- Added a wardrobe recipe graph editor for Unity 6.4 and newer.
- Improved recipe auto-save behavior and corrected cases where recipe changes were not persisted.
- Improved static-load clearing so repeated editor and runtime loads do not retain stale generated state.
- Improved slot and recipe inspectors, race tooling, overlay inspection, shared-color editing, mesh information viewing, and slot normal normalization.
- Added asset-index cleanup utilities and consolidated asset cleanup into a guided dialog.
- Consolidated UMA menu items and expanded editor test, smoke-test, and tool entry points.
- Improved Simple LOD rebuild behavior and generated-mesh cleanup across combiners.

### Content, Materials, And Samples

- Added and refreshed UMA 3 race content, including Half-Orc updates, female-face fixes, baked T-poses, pose assets, hair and wardrobe content, and updated sample scenes.
- Added or refreshed sample scenes for bone animators, clothing conforming, mesh combiners, save/load, character creation, and runtime construction.
- Updated UMA 3 SRP materials, shader packages, hair and skin assets, and release-facing material defaults.

### New And Updated Documentation

Since the initial snapshot, the documentation set now also includes:

- `Docs/ContentCreation.md`
- `Docs/CreatingANewRace.md`
- `Docs/GettingStarted.md`
- `Docs/BoneAnimators.md`
- `Docs/ClothingConformer.md`
- `Docs/DNACreationGuide.md`
- `Docs/DynamicCharacterAvatar.md`
- `Docs/MeshCombiners.md`
- `Docs/MeshModifiers.md`
- `Docs/MeshModifierSculpting.md`
- `Docs/NewDNASystem.md`
- `Docs/OverlayDataAsset.md`
- `Docs/RaceData.md`
- `Docs/RandomAvatar.md`
- `Docs/RendererAssetsAndCloth.md`
- `Docs/SlotDataAsset.md`
- `Docs/UMAAssetIndexer.md`
- `Docs/UMAMaterial.md`
- `Docs/UMASimpleLOD.md`
- `Docs/DynamicCharacterBuildOptimizationPlan.md`

The README documentation map was expanded to link every current Markdown guide in `Assets/UMA/Docs`.

## At A Glance

UMA 3 is a broad modernization of UMA's content, authoring tools, runtime APIs, and sample scenes.

The biggest changes for most users are:
- A new UMA 3 content set under `Assets/UMA/UMA3`.
- A new set of UMA 3 sample scenes covering character creation, random characters, decals, save/load, DNA sliders, item equipping, Timeline, photobooth tooling, and runtime construction.
- A rewritten DNA path that can drive blendshapes, bone transforms, bone poses, mesh modifiers, overlay UVs, and shared colors through a unified set of DNA effects.
- Better wardrobe authoring, including overlay positioning in the recipe editor and placeholder/wildcard slots for overlay-only recipes.
- A cleaner wearable item API on `DynamicCharacterAvatar` for setting, appending, removing, clearing, and querying equipped items.
- New and improved mesh hide, mesh modifier, decal, texture utility, UDIM, and ShaderGraph workflows.
- Better runtime setup and validation, including generator creation/help in the Welcome window and a stronger recommendation to rebuild the UMA Library after importing an UMA update.

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

UMA 3 adds a new content tree at `Assets/UMA/UMA3`. This is the main place to look for the new UMA 3-era content and examples.

Important folders include:
- `Animation`: UMA 3 pose and idle animation assets.
- `Colors`: shared color tables for eyes, hair, lips, makeup, skin, and tattoos.
- `Decals`: decal sample assets, overlays, materials, and demo content.
- `DNA`: body, face, pose, and race DNA assets for the new DNA workflows.
- `Documentation`: archived source documentation retained for reference. Current guides are Markdown files in `Assets/UMA/Docs`.
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

The Welcome window now presents UMA 3 guidance and a dedicated What's New section. It directs users to rebuild the UMA Library after importing a new update and provides checks for common setup problems.

User impact:
- First-time setup is clearer.
- Missing or inactive generators are easier to detect and fix.
- The Welcome window can help add or activate an UMA Generator and set safer generator defaults.
- UMA 3 is moving toward less manual generator setup; the branch notes call out runtime generator creation as a major direction.

When updating from an earlier UMA 3 release, rebuild the library before testing scenes or judging missing content. Many systems rely on the asset index to locate recipes, races, overlays, slots, addressable metadata, and generated content.

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
- `Docs/NewDNASystem.md`
- `Docs/DNACreationGuide.md`
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
- Artist and technical-artist workflows in `Docs/MeshModifiers.md` and `Docs/MeshModifierSculpting.md`.

User impact:
- Clothing can hide covered body triangles more reliably.
- Content creators have better tools for fixing clipping and deformation problems.
- DNA, wardrobe recipes, and mesh modifiers can work together more cleanly.

For details, see `Docs/MeshHideAssets.md`, `Docs/MeshModifiers.md`, and `Docs/MeshModifierSculpting.md`.

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
- Parallel bone baking and improved jobified mesh-combiner pass-two processing.
- Array pooling in mesh-combining hot paths to reduce garbage collection pressure.
- A Mesh API combiner option in UMA settings.
- Texture merge capacity reuse and RenderTexture format handling.
- Clean rig-only and mesh-only rebuild behavior for atlas UVs, bindposes, DNA, and skeleton transforms.
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

Current UMA documentation is maintained as Markdown in `Assets/UMA/Docs`. The older PDF and UMA 2 documentation folders are archival sources rather than the active manuals.

Start with:
- `Docs/WhatsNewInUMA3.md`
- `Docs/GettingStarted.md`
- `Docs/ContentCreation.md`
- `Docs/CreatingANewRace.md`
- `Docs/DynamicCharacterAvatar.md`
- `Docs/WardrobeRecipeEditor.md`
- `Docs/SlotDataAsset.md`
- `Docs/OverlayDataAsset.md`
- `Docs/RaceData.md`
- `Docs/UMAMaterial.md`
- `Docs/DNACreationGuide.md`
- `Docs/NewDNASystem.md`
- `Docs/MeshCombiners.md`
- `Docs/MeshHideAssets.md`
- `Docs/MeshModifiers.md`
- `Docs/MeshModifierSculpting.md`
- `Docs/UMASimpleLOD.md`
- `Docs/RandomAvatar.md`
- `Docs/RendererAssetsAndCloth.md`
- `Docs/BoneAnimators.md`
- `Docs/ClothingConformer.md`
- `Docs/Decals.md`
- `Docs/Textures-UDIM-Arrays.md`
- `Docs/Addressables.md`
- `Docs/UMAAssetIndexer.md`
- `Docs/DynamicCharacterBuildOptimizationPlan.md`

## Migration Notes From UMA 2

UMA 3 is contains significant asset, API, and workflow changes. When upgrading a project, treat the upgrade as a migration, not a small patch.

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
- Some branch changes are cleanup from beta process: removed temp/test content, moved examples, updated packages, and refreshed generated assets.

## What To Try First By Role

For character artists:
- Open `U3-Character Creator.unity`.
- Inspect shared color tables in `Assets/UMA/UMA3/Colors`.
- Open wardrobe recipes under `Assets/UMA/UMA3/Wearables`.
- Try overlay positioning in the Wardrobe Recipe Editor.
- Review placeholder slots for overlay-only items.

For technical artists:
- Review `Assets/UMA/UMA3/DNA`, `Docs/DNACreationGuide.md`, and `Docs/NewDNASystem.md`.
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
