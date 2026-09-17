# Editable braid spline validation

## Delivered behavior

Braid rails now support direct scene-line selection/dragging, whole-spline translation, individual control points and soft movement. Placement is non-destructive: freeform, whole-spine surface offset, or independent surface/helper attachments at root and tip. Attachment blend reach shapes the transition. Optional helper parenting preserves the existing shape when binding/unbinding and follows subsequent transform changes. Existing Curve Rail editing remains unchanged.

Surface queries use interpolated normals and inverse-transpose normal transformation for scaled/rotated mesh overrides. Offsets are measured in source-space meters. No source mesh, topology or weight data is modified. Missing targets/meshes produce warnings and retain usable freeform controls. A helper target can be embedded or linked to a scene object through the existing persistent helper binding system; live authoring refresh polls external transforms, while bake/runtime evaluation uses the saved snapshot.

Line drags defer card regeneration until release, are grouped into one Undo operation, and cancel on Escape. Stage close/focus loss releases the gesture. The UI remains in the existing node/properties windows. A Bun helper explains when its braid has been extracted, so the old procedural radius/height controls are not mistaken for the editable spline controls.

## Sample migration

Classic, Loose and Compact Copper each now have **Braid spline - bun surround**, a 17-control-point rail parented to the Bun helper. The braid population uses Braid rather than Bun/Surrounding Braid. The generated helper identity is retained, preserving deterministic strand seeds and LOD selection. Small spline-shape changes are expected from replacing the internal 65-point ring with editable controls.

The migration verified that painting and all five non-braid groups were unchanged. All nine meshes rebuilt with zero detected frame flips or degenerate triangles and no evaluation warnings. Triangle counts remain:

| Sample | LOD0 | LOD1 | LOD2 |
| --- | ---: | ---: | ---: |
| Classic | 72,084 | 48,320 | 29,918 |
| Loose | 73,572 | 50,810 | 32,126 |
| Compact Copper | 68,860 | 47,204 | 28,692 |

Generation log: `tmp/braid-spline-samples-02.log`. Actual Unity URP renders were inspected for Classic side/rear, Loose side/rear and Compact LOD2; the surrounding-braid structure and coverage were retained. This update is not a change to scalp shading or a claim of photographic parity.

## Regression coverage

The suite checks freeform compatibility/nonmutation, world/source coordinate invariance, scaled/rotated mesh offsets, root-only/tip-only/both surface attachments, helper following, attachment/spine combinations, parent bind/detach continuity, source removal/recovery, missing targets, reverse bindings, extraction/Undo, narrow/wide property rendering, and direct scene picking/dragging, grouped Undo and Escape cancellation. Existing sample tests rebuild every saved LOD exactly.

Final validation on 2026-09-16 using Unity 6000.3.18f1: **644 passed, 0 failed, 0 skipped**, including actual source import/compilation, geometry and saved-sample regressions, editor interactions and GPU rendering tests.

Results: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/269cdb415d624a08aafdcaed80c2a287/editmode.xml`. All 102 delivered C#/shader/HLSL files match the tested copies (95 C# scripts). Main-project metadata preflight and scoped whitespace checks passed.

Installation verified all 157 sample files byte-for-byte against the validated project, retained existing GUIDs and deleted nothing. The original main-project files were checked against the backup before replacement; only this task's README edit differed. SHA-256 checks confirmed all 218 files in Pointy Swept, Curly Volume and Short Hair Part were unchanged.

## Limits

Surface offset locates the spine, not the braid's outer cards. Allow enough room for bundle thickness; this is not whole-braid self-collision or a physics solver. Closest-surface snapping can jump across disconnected or closely overlapping mesh parts; use a dedicated surface or endpoint-only attachment for such cases. Parent transform changes move the spline, while changes to procedural Bun radius/height do not automatically reshape its independent control points. Surface mesh overrides use an explicit source-local transform, not a live scene MeshFilter binding. Scene-object helper attachments are an authoring linkage, not a runtime rig.

Existing native samples are backed up under `tmp/BraidedBunBeforeSpline_20260916_130922/BraidedBun`. No native asset is edited as text; generation and saving use Unity serialization APIs, followed by verified byte-for-byte installation while Unity is closed.
