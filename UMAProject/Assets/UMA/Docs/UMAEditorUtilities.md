# UMA Editor Utilities

This page is the directory for UMA editor tools whose targeting, generated assets, or side effects are not obvious from their menu labels.

## Configure UMA

Open **UMA > Welcome to UMA > Configure UMA**. The navigation highlights the displayed page with bold text and an accent bar. Review the selected skin and hair UMAMaterials, then apply Maximum Speed or Maximum Fidelity. These presets save generator-prefab and material changes and request **Rebuild all editor UMA** for eligible characters in the active scene.

See [UMA Generator Setup](UMAGeneratorSetup.md) for hair-material backup/restoration, settings preserved by the presets, runtime versus editor atlas resolution, and scene/quality overrides.

## Shared Color Tables

Select a Shared Color Table asset to paste color settings copied from an avatar. In Advanced View, **Copy All Checked** and **Paste/Update** transfer named sets of colors, updating matching names and creating missing entries. See [Shared Colors in Dynamic Character Avatar](DynamicCharacterAvatar.md) for the single-color and bulk workflows.

## Batch Asset Inspection

- [Examine Wearables](ExamineWearables.md): assign wardrobe regions, add hides and suppressions, repair packed slots, update materials, and validate recipe dependencies.
- [Examine Overlays](ExamineOverlays.md): completeness checks, dependency validation, material assignment, filename relinking, and folder synchronization.
- [Examine Slots](ExamineSlots.md): batch slot fields, serialized replacement, and backup-based folder synchronization.

## Texture and Asset Maintenance

- [Texture Utilities](TextureUtilities.md): adjustments, alpha repair, touchup, procedural details, splitting, resizing, and normal editing.
- [Asset Consolidation and Repair](AssetConsolidationAndRepair.md): wardrobe textures, scene dependencies, packed recipe repair, overlay channel repair, and PNG conversion.
- [Project Asset Batch Utilities](AssetMenuBatchUtilities.md): context-sensitive generators and migrations under `Assets > UMA`.

## Release and Testing

- [Testing and Release Utilities](TestingAndReleaseUtilities.md): race smoke tests, UMA EditMode tests, performance baselines, and the recommended release gate.
- [Release Asset Validation](ReleaseAssetValidation.md): UMA2/UMA3 package rules, JSON report, and repair actions.

## Race, Pose, Animation, and Construction

- [Race Utilities](RaceUtilities.md): race duplication, compatible-race batches, T-pose generation, and base-overlay material migration.
- [Pose Tools](PoseTools.md): bone-pose building, conversion, mixing, DNA extraction, expression extraction, and T-pose extraction.
- [Bone Pose Builder: An Artist's Guide](BonePoseBuilder.md): complete posed-rig and animation-sampling walkthroughs, frame-list management, and extraction troubleshooting.
- [Bone Pose Editor: An Artist's Guide](BonePoseEditor.md): hands-on pose authoring, mirroring, bone groups, IK, previewing, merging, and export.
- [Animation Utilities](AnimationUtilities.md): curve stripping and binding-path migration.
- [Prefab and Scene-Building Tools](PrefabAndSceneBuildingTools.md): non-UMA prefab conversion, generated prefab saving, scene-mesh slot creation, and Bone Builder.

## Discovery and Global Library

In the Plans browser, use the selector at the top to choose a subfolder or **All folders**. Each folder includes its nested plans, and nested choices show their full folder hierarchy. The selection survives refreshes and switching between Plans and Documentation; a removed folder falls back to **All folders**. Documentation continues to use the same selector for UMA and plugin sources.

- [Asset Discovery Utilities](AssetDiscoveryUtilities.md): Quick Finder, Favorites, component usage, material usage, and Tags Editor.
- [UMA Asset Indexer and Global Library](UMAAssetIndexer.md): indexing, filters, rebuild, repair, backup, and cached references.

## Before Running a Batch Utility

1. Read the utility's targeting section. “Selected,” “checked,” “visible,” and “all” are not interchangeable between windows.
2. Commit or back up the project before move, copy, delete, overwrite, conversion, or folder synchronization operations.
3. Test one representative asset before processing a filtered list or whole project.
4. Review generated backup and not-found folders before deleting them.
5. Rebuild the Global Library only when lookup data needs it.
6. Generate affected races and run release validation before exporting packages.
