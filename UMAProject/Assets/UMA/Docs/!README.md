# UMA — Unity Multipurpose Avatar

UMA is an open-source character creation and customization system for Unity. It assembles optimized characters from reusable races, meshes, textures, materials, wardrobe recipes, DNA, and animation data.

UMA can be used for player characters, crowds, character creators, modular NPCs, and runtime customization. Its authoring tools support artists and technical artists while its runtime systems handle mesh generation, texture compositing, wardrobe changes, DNA-driven shapes, LOD, and asset loading.

## Start Here

New and returning users should begin with these two guides:

1. [What's New in UMA 3](!WhatsNewInUMA3.md) — new features, workflow changes, compatibility notes, and improvements since the initial UMA 3 release.
2. [Getting Started with UMA 3](GettingStarted.md) — configure UMA, verify the Global Library and generator, and create your first character.

Artists creating new content should continue with:

- [Content Creation](ContentCreation.md) — practical Blender and Maya workflows for preparing meshes, textures, slots, overlays, and wardrobe.
- [UMA Tools for Blender](UMAToolsForBlender.md) — Blender validation, rigging, weight, UDIM, import, and dependable UMA FBX export workflows.
- [Creating a New Race](CreatingANewRace.md) — the complete race-authoring process from source model through runtime validation.
- [Overlay Painter](OverlayPainter.md) — non-destructive 3D/2D texture painting, layers, material channels, paths, effects, and recipe-ready export.
- [Overlay Painter Generators and Filters](OverlayPainterGeneratorsAndFilters.md) — artist workflows, material recipes, detailed controls, and troubleshooting for every included procedural plugin.

You can open these guides inside Unity from `UMA > View Documentation`.

## How UMA Fits Together

- A `RaceData` asset defines a character type, its base recipe, skeleton, DNA, animation options, and compatibility.
- `SlotDataAsset` assets provide skinned meshes such as bodies, clothing, hair, and accessories.
- `OverlayDataAsset` assets provide textures, colors, masks, and material-channel data.
- Wardrobe recipes combine slots and overlays into reusable wearable items.
- DNA changes character proportions and can drive bones, blendshapes, and other modifiers.
- The UMA Generator composites textures, builds materials, combines meshes, and produces the final animated character.
- `DynamicCharacterAvatar` provides the main runtime workflow for changing race, wardrobe, DNA, and colors and for saving or loading characters.

## Documentation Contents

### Setup and core workflow

- [What's New in UMA 3](!WhatsNewInUMA3.md)
- [Getting Started](GettingStarted.md)
- [Dynamic Character Avatar](DynamicCharacterAvatar.md)
- [UMA Generator Setup](UMAGeneratorSetup.md)
- [UMA Asset Indexer and Global Library](UMAAssetIndexer.md)
- [Icon Creator and Thumbnail Sprite Atlases](IconCreator.md)
- [Addressables](Addressables.md)

### Artist content creation

- [Content Creation](ContentCreation.md)
- [UMA Tools for Blender](UMAToolsForBlender.md)
- [Creating a New Race](CreatingANewRace.md)
- [RaceData](RaceData.md)
- [SlotDataAsset](SlotDataAsset.md)
- [OverlayDataAsset](OverlayDataAsset.md)
- [Examine Overlays](ExamineOverlays.md)
- [Examine Slots](ExamineSlots.md)
- [Overlay Painter](OverlayPainter.md)
- [Overlay Painter Generators and Filters](OverlayPainterGeneratorsAndFilters.md)
- [Texture Utilities](TextureUtilities.md)
- [Weight Touchup](WeightTouchup.md)
- [Wardrobe Recipe Editor](WardrobeRecipeEditor.md)
- [Examine Wearables](ExamineWearables.md)
- [UMA Materials](UMAMaterial.md)
- [Renderer Assets and Cloth](RendererAssetsAndCloth.md)
- [Textures, UDIMs, and Texture Arrays](Textures-UDIM-Arrays.md)

### Shape, fitting, and visibility

- [DNA Creation Guide](DNACreationGuide.md)
- [New DNA System](NewDNASystem.md)
- [Mesh Modifiers](MeshModifiers.md)
- [Mesh Modifier Sculpting](MeshModifierSculpting.md)
- [Clothing Conformer](ClothingConformer.md)
- [Mesh Hide Assets](MeshHideAssets.md)
- [Decals](Decals.md)

### Generation, animation, and performance

- [Mesh Combiners](MeshCombiners.md)
- [Incremental Mesh Combiner](IncrementalMeshCombiner.md)
- [UMA Simple LOD](UMASimpleLOD.md)
- [Random Avatars](RandomAvatar.md)
- [Bone Animators](BoneAnimators.md)
- [Dynamic Expression Player](DynamicExpressionPlayer.md)

### Editor workflow

- [UMA Editor Utilities](UMAEditorUtilities.md)
- [Examine Wearables](ExamineWearables.md)
- [Examine Overlays](ExamineOverlays.md)
- [Examine Slots](ExamineSlots.md)
- [Project Asset Batch Utilities](AssetMenuBatchUtilities.md)
- [Asset Consolidation and Repair](AssetConsolidationAndRepair.md)
- [Asset Discovery Utilities](AssetDiscoveryUtilities.md)
- [Race Utilities](RaceUtilities.md)
- [Pose Tools](PoseTools.md)
- [Animation Utilities](AnimationUtilities.md)
- [Prefab and Scene-Building Tools](PrefabAndSceneBuildingTools.md)
- [Testing and Release Utilities](TestingAndReleaseUtilities.md)
- [Release Asset Validation](ReleaseAssetValidation.md)
- [UMA Task List](UMATaskList.md)

### Engineering and implementation notes

- [Incremental Mesh Combiner Baseline](Plans/IncrementalMeshCombinerBaseline.md)
- [Dynamic Character Build Optimization Plan](Plans/DynamicCharacterBuildOptimizationPlan.md)
- [Mesh Modifier Cloth Brush Plan](Plans/MeshModifierClothBrushPlan.md)
- [Wardrobe Recipe Graph Production Readiness](Plans/UMAWardrobeRecipeGraphProductionReadiness.md)

## Project Layout

- `Assets/UMA/Core` contains the shared runtime, editor tools, default generator, and default avatar prefab.
- `Assets/UMA/SRP` initially contains the URP and HDRP bootstrap installers. After the required Welcome-window selection, it contains the selected pipeline's shaders, materials, environment assets, textures, content manifest, and (for source-tree installs) both bootstrap installers.
- `Assets/UMA/UMA3` contains editable UMA 3 races, wearables, demonstrations, and sample content installed by `UMA3Content.unitypackage`.
- `Assets/UMA/UMA2` contains optional editable legacy races and compatible content installed by `UMA2Content.unitypackage`.
- `Assets/UMA/Docs` contains the current documentation.

Projects that do not need the supplied UMA 3 sample races or content can remove `Assets/UMA/UMA3`. Shared Core and SRP dependencies are kept outside that folder.

## Open Source and License

UMA is free and open-source software released under the [MIT License](https://github.com/umasteeringgroup/UMA/blob/master/LICENSE). The license allows UMA to be used, modified, and distributed in commercial and non-commercial projects, subject to its terms.

Third-party packages, shaders, samples, and media included with a project may have their own license notices. Review those notices before redistributing third-party content.

## Community and Support

UMA is a community-driven project. Questions, workflow discussion, bug reports, examples, and contributions are welcome.

- [Join the UMA Discord](https://discord.gg/KdteVKd) to talk with other UMA users and contributors.
- [Visit the UMA GitHub repository](https://github.com/umasteeringgroup/UMA) for source code, issues, and contributions.
- [Read the UMA Wiki](https://github.com/umasteeringgroup/UMA/wiki) for additional community-maintained information.

When asking for help, include your Unity version, render pipeline, UMA version or branch, relevant console errors, and the steps needed to reproduce the problem.
