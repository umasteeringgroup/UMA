# Modifier coordinate audit

## Coordinate contract

All evaluated points remain in the groom's source-mesh local space. Posing/rendering happens after evaluation. `sourceToWorld` and optional per-root `guideToSourcePose` provide context for world gravity and surface-normal Lift; they do not translate output points into world space. A source-local direction is intentionally not camera-relative. Length preservation is in source-local units, not world units under changing nonuniform scale.

## Findings and fixes

- **Lift was using `modifier.vector.normalized` for every strand.** That was a fixed-axis bend, not a scalp-relative lift. Root Normal is now the default, with an optional closest-triangle normal field. The fixed vector is no longer exposed or used for Lift. Normal choice persists in modifier serialization and duplicates. Root/freeze/ramp/length rules are retained.
- A normal transforms by inverse transpose, while a displacement written back to a local curve transforms by inverse. Lift now applies both conversions through the object and per-guide pose, preserving an outward normal response under nonuniform scale, reflection and shear. Singular/non-finite transform contexts are skipped safely. Root Normal needs no new surface search; closest-face mode reuses the source surface BVH.
- **Externally linked helpers mixed a source-local position with world `lossyScale`, and converted orientation without accounting for source scale.** Their exact helper-to-source affine transform is now stored, used by Collision/Push Out and constraint collision, and used for the helper display. The snapshot includes parent scale, reflection, shear and the authoring pose. Embedded helpers still use their authored source-local TRS. Linked objects are moved in the source scene; the stage does not expose a competing position handle that the next sync would overwrite.
- Changed linked-helper snapshots/rail points mark the groom dirty and are refreshed on Save. Identical bindings do not dirty it repeatedly. A bound object without child rail points uses its fully transformed local-up segment, including scale/reflection.
- Gravity already used full object/pose vector conversion and inverse-transpose separation normals; its transform validity check was strengthened. No source-local operation was switched wholesale to world space.

## All modifier types reviewed

| Modifier | Frame and result |
| --- | --- |
| Resample, Simplify | Source-local arc length and point interpolation; no world/camera direction. |
| Length | Scales source-local offsets from the pinned root; intentionally changes length. |
| Width | Scalar width/profile scaling; no spatial conversion. |
| Smooth | Source-local neighbors; pinned roots and constrained segment lengths. |
| Flow Align | Explicit source-local vector; character/pose carries the evaluated result. |
| Lift | Root or nearest-face normal, normal-aware object/pose conversion, source-local target distance. |
| Clump | Source-local helper center or group tip center. Helper positions are now converted using the full relative frame. |
| Part | Source-local symmetry-plane point plus the modifier's source-local plane normal. |
| Curl, Wave | Source-local tangent/normal frame; cycles and phase are scalars. Degenerate frames use canonical source axes, not camera axes. |
| Noise | Deterministic source-axis displacement; the pattern moves with the groom. |
| Twist | Scalar roll around the evaluated strand tangent. |
| Gravity | World force (default), or explicit source-local override transformed through the object/pose. Separation uses a transformed surface normal. |
| Helper Follow | Stored source-local rail points; relative shape is anchored to each guide root. Points are already source-local, not helper-local offsets. |
| Surface Projection | Source-mesh local nearest-vertex approximation and local normal offset. Intentionally changes length; no posed-mesh/source-point mixing. |
| Collision, Push Out | Source-local points converted into the helper's local volume and back using its full affine transform. Clearance remains source-local. |
| Mirror | Source-local plane point/normal; reflects point positions, facing normals, root normal and roll. The plane follows the character. |
| Trim By Mesh | Source-local segment rays and source-mesh BVH distances; keeps the root-side curve. |
| LOD Reduction | Sample-count metadata; no spatial conversion. |
| Spline Flow | Source-anchored path positions/normals and source-local radius. Facing normals transform by inverse transpose when the preview pose is applied. |

Generated children receive combined guide/child modifiers once through guide interpolation. Children-only modifiers operate in the same source-local frame; this audit does not add a second transform or double evaluation.

## Verification scope

Automated coverage exercises all 22 modifier types in guide/child and children-only domains under translated, rotated, nonuniform, mirrored and sheared object/pose contexts. Translation must never leak into direction conversion. Source-defined modifiers must keep the same canonical output when only display context changes. Dedicated tests verify Lift's independent target calculation, opposite-side root normals, signed amount, nearest-face mode, in-place mesh changes, pinned/frozen anchors, Root Influence, serialization, and singular-transform rejection. Helper plane/sphere/box/capsule tests verify exact relative matrices, collision boundaries and serialized snapshots. Existing gravity world/local, runtime hierarchy and pose tests remain part of the release gate.

UI tests render both Lift normal choices at 320/650-pixel Properties widths. Automated tests do not certify the subjective production hairstyle or live external-object manipulation: perform the manual checks in [Hair Cards Manual QA](HairCardsManualQA.md) before release.

Final verification (2026-09-12): actual Unity **6000.3.18f1** source import/compilation and **436 tests passed, 0 failed, 0 skipped**, in 76.67 seconds. The isolated project compiles Hair Cards and its UMA_Core dependency from source. Metadata preflight covered 43 scripts; SHA-256 comparison matched all 94 C#/assembly-definition/import-metadata files to the working source. Results: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/1283165379604211b670db4728b14d14/editmode.xml`. No production groom asset was manually rewritten.
