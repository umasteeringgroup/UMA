# Pose Tools

UMA's Pose Tools create, combine, convert, or extract T-poses and `UMABonePose` data. These tools operate on asset-space transforms; commit source assets before conversion.

## Bone Pose Builder

Open `UMA > Tools > Pose Tools > Bone Pose Builder`.

The builder compares a Base Prefab with a posed rig and creates `UMABonePose` assets containing their transform differences. It can build one pose or sample named frames from an animation clip. Pose-set XML files store the frame/name list for repeatable batch extraction.

Use rigs with matching hierarchies and stable bone names. Build output into a dedicated Pose Folder and inspect the resulting bone list before runtime use.

## Bone Pose Converter

Open `UMA > Tools > Pose Tools > Bone Pose Converter`.

Configure independent axis mappings and inversion for position and rotation, validate the mapping, then drag `UMABonePose` assets into the queue. Conversion modifies each queued pose.

The converter creates a backup the first time and uses that backup as the source for later reconversions, preventing repeated conversion drift. `Restore` returns a pose from its backup. Use `Self-Test Axis Mapping` and inspect representative bones before converting a full library.

## Bone Pose Mixer

Open `UMA > Tools > Pose Tools > Bone Pose Mixer`.

Assign a rig prefab and add component poses with blend percentages. The tool can limit output to selected component bones and provides Left, Right, and Mirror helpers. `Build` creates a new combined `UMABonePose` in the selected folder.

Component order and percentages affect the result. Test mirrored naming conventions on the target rig.

## Bone Pose DNA Extractor

Open `UMA > Tools > Pose Tools > Bone Pose DNA Extractor`.

This advanced migration tool samples a generated UMA across DNA values and creates bone-pose data for a selected DNA converter. It expects a generated `UMAData`, resolvable race converters, and an output folder. The process creates temporary avatars and may generate many assets.

Use it for migrating converter-driven bone changes into pose-based data, not as a routine DNA editor. Run it on a clean branch and verify every generated pose.

## Expression Extractor

Open `UMA > Tools > Pose Tools > Expression Extractor`.

The window manages a GameObject, animation clip, expression folder, and named frame list, with load/save support for expression-set XML.

The current `Build Expressions` implementation samples and logs configured frames but does not yet create final `UMABonePose` expression assets. Treat it as a diagnostic and setup utility, not a complete production extractor.

## Extract T-Pose

Use `UMA > Tools > Pose Tools > Extract T-Pose`, `Assets > UMA > Extract T-Pose`, or the Animator/UMA avatar context command.

The tool extracts humanoid pose data from a selected model Animator or generated avatar and creates an `UmaTPose` asset, normally named with a `_TPose` suffix. Verify the Animator avatar is valid and the model is in the intended reference pose.

## Set Clip Generic and Set Clip Legacy

These menu commands toggle the selected `AnimationClip.legacy` flag off or on. They do not retarget animation curves or change hierarchy paths. Verify the clip in its intended Animator or legacy Animation component afterward.

## Pose Validation Checklist

1. Confirm source and target bone names and hierarchy.
2. Use dedicated output folders and source control.
3. Inspect a representative transform before batch conversion.
4. Test left/right mirroring explicitly.
5. Validate generated assets on all intended races.
6. Keep original animation, prefab, and pose sources until release verification passes.
