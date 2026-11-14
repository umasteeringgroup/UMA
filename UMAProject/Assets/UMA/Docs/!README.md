# UMA — Unity Multipurpose Avatar

UMA 3 is an open-source character creation and customization system for Unity. It assembles optimized characters from reusable races, meshes, textures, materials, wardrobe recipes, DNA, and animation data.

UMA can be used for player characters, crowds, character creators, modular NPCs, and runtime customization. Its authoring tools support artists and technical artists while its runtime systems handle mesh generation, texture compositing, wardrobe changes, DNA-driven shapes, LOD, and asset loading.

## Current release

**UMA 3.1f6 (package version 3.1.6)** supports **Unity 6.3 and newer**. Its highlights are hair that sways ([Hair Animation](HairAnimation.md)), UMA 2 clothing on UMA 3 characters ([Slot Rig Conversion](SlotRigConversion.md)), body blend shapes transferred to clothing ([Blendshape Transfer](BlendshapeTransfer.md)), and an opt-in subsurface scattering skin shader. For the update from 3.1f5 / 3.1.5, use the [What's New overview](!WhatsNewInUMA3.md#uma-31f6--changes-since-31f5) and [complete release changelog](../CHANGELOG.md#31f6-package-version-316); the [3.1f5 overview](!WhatsNewInUMA3.md#uma-31f5--changes-since-31f3) covers the update from 3.1f3. Feature guides below describe the current workflows; the changelog owns the version-by-version history.

## Start Here

New and returning users should begin with these two guides:

1. [What's New in UMA 3](!WhatsNewInUMA3.md) — new features, workflow changes, compatibility notes, and improvements since the initial UMA 3 release.
2. [Getting Started with UMA 3](!GettingStarted.md) — configure UMA, verify the Global Library and generator, and create your first character.

Artists creating new content should continue with:

- [Content Creation](ContentCreation.md) — practical Blender and Maya workflows for preparing meshes, textures, slots, overlays, and wardrobe.
- [UMA Tools for Blender](UMAToolsForBlender.md) — Blender validation, rigging, weight, UDIM, import, and dependable UMA FBX export workflows.
- [Creating a New Race](CreatingANewRace.md) — the complete race-authoring process from source model through runtime validation.
- [Overlay Painter](../OverlayPainter/Documentation/OverlayPainter.md) — non-destructive 3D/2D painting, per-channel projections and surface warping, UDIM fills, path fade curves, composable masks, stencils, linked layers, selections, symmetry, and recipe-ready export.
- [Overlay Painter Generators and Filters](../OverlayPainter/Documentation/OverlayPainterGeneratorsAndFilters.md) — artist workflows, material recipes, detailed controls, and troubleshooting for every included procedural plugin.

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
- [Getting Started](!GettingStarted.md)
- [Dynamic Character Avatar](DynamicCharacterAvatar.md)
- [Installing UMA as a Unity Package](PackageInstallation.md) — Package Manager and source-tree installs, render pipeline support, and content packages.
- [Official Plugins](UMAPlugins.md) — install, update and remove optional plugins and their Examples and Tests companions.
- [Configure UMA and Generator Setup](UMAGeneratorSetup.md) — Maximum Speed/Fidelity presets, editor rebuilds, atlas sizes, and fitting methods.
- [Generator Quality Profiles](GeneratorQuality.md)
- [UMA Asset Indexer and Global Library](UMAAssetIndexer.md)
- [Avatar Presets](AvatarPresets.md)
- [Icon Creator and Thumbnail Sprite Atlases](IconCreator.md)
- [Photobooth Recipe Icon Generation](PhotoboothIconGeneration.MD)
- [Addressables](Addressables.md)

### Artist content creation

- [Content Creation](ContentCreation.md)
- [UMA Tools for Blender](UMAToolsForBlender.md)
- [Bone Pose Builder: An Artist's Guide](BonePoseBuilder.md) - turn posed rigs and supported animation samples into reusable poses.
- [Bone Pose Editor: An Artist's Guide](BonePoseEditor.md) - shape poses on a built character with direct controls, symmetry, groups, and IK.
- [Creating a New Race](CreatingANewRace.md)
- [Model to UMA: Guided Race and Clothing Creation](ModelToRace.md)
- [RaceData](RaceData.md)
- [SlotDataAsset](SlotDataAsset.md)
- [OverlayDataAsset](OverlayDataAsset.md)
- [Examine Overlays](ExamineOverlays.md)
- [Examine Slots](ExamineSlots.md)
- [Overlay Painter](../OverlayPainter/Documentation/OverlayPainter.md)
- [Overlay Painter Generators and Filters](../OverlayPainter/Documentation/OverlayPainterGeneratorsAndFilters.md)
- [Texture Utilities](TextureUtilities.md)
- [Weight Touchup](WeightTouchup.md)
- [Hair Animation: An Artist's Guide](HairAnimation.md) - make UMA hair sway, give it root-to-tip vertex colors, and choose settings for short, medium and long hair.
- [Normal Viewer Repair](NormalViewerRepair.md) — scan, preview, and repair normals across slot boundaries.
- [Wardrobe Recipe Editor](WardrobeRecipeEditor.md)
- [Examine Wearables](ExamineWearables.md)
- [UMA Materials](UMAMaterial.md)
- [Renderer Assets and Cloth](RendererAssetsAndCloth.md)
- [Textures, UDIMs, and Texture Arrays](Textures-UDIM-Arrays.md)
- [Realistic Skin and Realistic Skin SSS (URP)](../SRP/Shaders/Skin/README.md)
- [Hair Cards Quick Start](../HairCards/Documentation/Hair%20Cards%20-%20Quick%20Start.md) — optional Hair Card Editor plugin.
- [Dismemberment Artist Guide](../UMADismemberment/Documentation/ArtistGuide.md) — optional Dismemberment plugin.

### Shape, fitting, and visibility

- [Optimizing Blendshapes](OptimizingBlendshapes.MD) — selective shape loading, MeshModifiers, baking, race generation, and performance.
- [DNA Creation Guide](DNACreationGuide.md)
- [New DNA System](NewDNASystem.md)
- [DNA Override Utility Slot](UMADnaOverrideSlot.md)
- [UMA 3 Viseme Poses](UMA3Visemes.md)
- [Mesh Modifiers](MeshModifiers.md)
- [Mesh Modifier Sculpting](MeshModifierSculpting.md)
- [Clothing Conformer](ClothingConformer.md)
- [Blendshape Transfer](BlendshapeTransfer.md)
- [Slot Rig Conversion](SlotRigConversion.md)
- [Mesh Hide Assets](MeshHideAssets.md)
- [Decals](Decals.md)

### Generation, animation, and performance

- [Atlas and Mesh Reuse](AtlasAndMeshReuse.md) - setup, practical sharing examples, and troubleshooting.
- [Source-UV Cropping](SourceUVCropping.md)
- [Automatic Slot Preparation](SlotPreparation.md)
- [Completed NPC Builds](NPCBuilds.md)
- [Rig Lifetime and Bone Cleanup](BoneLifecycle.md)
- [Generated Resource Reuse](GeneratedResourceReuse.md) - implementation details and profiling.
- [Mesh Combiners](MeshCombiners.md)
- [Incremental Mesh Combiner](IncrementalMeshCombiner.md)
- [UMA Simple LOD](UMASimpleLOD.md)
- [Random Avatars](RandomAvatar.md)
- [Bone Animators](BoneAnimators.md)
- [Dynamic Expression Player](DynamicExpressionPlayer.md)
- [Ragdoll rest](../Core/StandardAssets/Extensions/UMAPhysics/README.md) — freeze settled ragdolls and wake them before new forces.

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
- [Shader Package Manager](ShaderPackageManager.md)
- [Animation Utilities](AnimationUtilities.md)
- [Prefab and Scene-Building Tools](PrefabAndSceneBuildingTools.md)
- [Testing and Release Utilities](TestingAndReleaseUtilities.md)
- [Release Asset Validation](ReleaseAssetValidation.md)
- [UMA Task List](UMATaskList.md)

### Engineering and implementation notes

- [Mesh Modifier Cloth Brush Plan](../Plans/MeshModifierClothBrushPlan.md)
- [Wardrobe Recipe Graph Production Readiness](../Plans/UMAWardrobeRecipeGraphProductionReadiness.md)

## Project Layout

- `Assets/UMA/Core` contains the shared runtime, editor tools, default generator, and default avatar prefab.
- `Assets/UMA/SRP` initially contains the URP and HDRP bootstrap installers. After the required Welcome-window selection, it contains the selected pipeline's shaders, materials, environment assets, textures, content manifest, and (for source-tree installs) both bootstrap installers.
- `Assets/UMA/UMA3` contains editable UMA 3 races, wearables, demonstrations, and sample content installed by `UMA3Content.unitypackage`.
- `Assets/UMA2` contains legacy races and compatible content supplied by the optional **UMA2Compatibility** plugin. Install it from Welcome's Plugins page; see [official plugins](UMAPlugins.md).
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
