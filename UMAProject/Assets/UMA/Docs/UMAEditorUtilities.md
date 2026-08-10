# UMA Editor Utilities

This page is the directory for UMA editor tools whose targeting, generated assets, or side effects are not obvious from their menu labels.

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
- [Animation Utilities](AnimationUtilities.md): curve stripping and binding-path migration.
- [Prefab and Scene-Building Tools](PrefabAndSceneBuildingTools.md): non-UMA prefab conversion, generated prefab saving, scene-mesh slot creation, and Bone Builder.

## Discovery and Global Library

- [Asset Discovery Utilities](AssetDiscoveryUtilities.md): Quick Finder, Favorites, component usage, material usage, and Tags Editor.
- [UMA Asset Indexer and Global Library](UMAAssetIndexer.md): indexing, filters, rebuild, repair, backup, and cached references.

## Before Running a Batch Utility

1. Read the utility's targeting section. “Selected,” “checked,” “visible,” and “all” are not interchangeable between windows.
2. Commit or back up the project before move, copy, delete, overwrite, conversion, or folder synchronization operations.
3. Test one representative asset before processing a filtered list or whole project.
4. Review generated backup and not-found folders before deleting them.
5. Rebuild the Global Library only when lookup data needs it.
6. Generate affected races and run release validation before exporting packages.
