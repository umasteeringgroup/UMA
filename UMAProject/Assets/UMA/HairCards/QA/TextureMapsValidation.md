# Texture-backed grooming maps and ShortHairPart validation

Validated on 2026-09-15 with Unity 6000.3.18f1. The implementation uses the existing
Hair Properties controls, with no additional editing window. Destructive conversion
uses a confirmation dialog and supports Undo.

## Scope and design

- Each map can retain vertex storage or use sparse, single-channel texture tiles
  in dedicated coordinates on the original source triangles. Character UV overlap
  and seams do not limit paint placement. Source geometry, skin weights and guide
  anchors are not subdivided or replaced.
- Detail levels are 8, 16, 32 and 64 intervals per face edge. Only edited faces
  allocate detailed tiles. Unedited faces retain their original vertex field.
- Painting, mirrored painting, hardness, Shift erase, smoothing, fill, inversion,
  cut/paste, selection conversion, save/reload and Undo use the texture data.
  CPU generation and the displayed GPU overlay use the same triangular filter.
- Guide and child generation sample inside faces. A painted island can generate
  hair even when all three original corner values are zero. Hairline distance,
  inward flow and connected regions are evaluated at texture resolution.
- Source topology changes invalidate texture maps rather than silently assigning
  the paint to unrelated faces. Validation blocks an invalid bake.
- Scalp vertex shading remains vertex-based. It is conservatively contained at
  precise painted boundaries to avoid coloring the forehead across a coarse face.
  This is not a high-resolution scalp color texture feature.

## Regression gate

The isolated project imported and compiled the actual Hair Cards source and passed
**557 EditMode tests, zero failures and zero skips**, including 21 new texture-map
cases. Fresh results:

`tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/4bd3564bff174333bdeacf0c77a7c561/editmode.xml`

Coverage includes filtering, sparse conversion, brush hardness, mirror, locks and
visibility, smoothing across UV seams, intra-face growth, root-sampled child maps,
cached surface-field invalidation, serialization, Undo, clipboard isolation, preview
mesh/shader, selection/focus integration, inherited settings without inherited paint,
invalid topology and conservative scalp shading. Existing grooming, modifier,
source binding, bone-centered camera focus and other sample tests also pass.

After installation, the actual main project also imported and compiled the source
successfully. Its full run passed **556 of 557 tests**, with no skips. All new
texture tests and the three rebuilt ShortHairPart sample cases passed. The one
failure was `PackagedCurlyExampleHasEditableRingletsAndSelfContainedResources`:
the existing main-project Curly groom uses `TurnsPerStrand`, while that canonical
sample test expects `DistanceBetweenTurns`. Its SHA-256 matches the pre-task
baseline, so this run did not change or revert that user-edited asset. The test
was not weakened or skipped to produce a green result.

Main-project results:
`Logs/HairCardsReleaseGate/960e079ff69e4d6d98800c07a0be2c73/editmode.xml`

## Rebuilt sample

`Assets/UMAProjectData/HairCards/Examples/ShortHairPart` replaces the old
RegularHaircut example. Classic, Relaxed and Close Cut each retain 39 authored
guides in two groups. Their growth footprints were reconstructed from smooth
surface regions, not simply upsampled from the coarse vertex masks. Tight surface
projection excludes the ears. Both growth maps use 32 intervals per face edge:
137 detailed crown faces and 94 detailed side faces, approximately 0.96 MiB of
texels per groom. Native groom files are approximately 4.4 MB including other data.

| Variant | LOD0 unique triangles | LOD1 | LOD2 |
|---|---:|---:|---:|
| Classic | 19,784 | 9,606 | 3,424 |
| Relaxed | 19,768 | 9,720 | 3,544 |
| Close Cut | 19,792 | 9,666 | 3,416 |

All nine saved meshes are checked against freshly evaluated grooms. The geometry
budget excludes the body and extra shader passes. Hybrid rendering retains its
second color pass; it does not make the draw cost equal to one pass. The Close Cut
clearance regression remains below the existing 1 mm penetration tolerance.

Actual URP images were reviewed from both sides, front, rear and a three-quarter
view, together with each growth overlay. The ear leakage and coarse triangular
forehead shading were removed. Packaged preview images, three-LOD prefabs and the
review scene were rebuilt. Review images are in `tmp/ShortHairPartReview`.

## Performance measurement

Measured in a graphics-enabled isolated Editor on Classic, with a shared evaluation
workspace and the rebuilt approximately 20,000-triangle sample:

- Cold evaluation plus card-mesh construction: **1,568.6 ms**.
- Immediate cached evaluation plus card-mesh construction: **194.2 ms**.
- Mean CPU texture-paint operation over 100 warmed dabs: **2.008 ms**, with
  **zero managed bytes allocated per dab** in that loop.

These are scoped measurements, not guaranteed frame rates. The paint measurement
excludes full Editor event handling, Undo serialization and GPU atlas upload.
Higher texture resolution and larger painted areas increase memory and cold
surface-field construction cost. No jobs/threading rewrite was introduced.

Measurement log:
`tmp/HairCardsSourceValidation/Logs/short-part-589e5dfce00044a494e47fd5ca863153/unity.log`

## Installation safety

Verified the main Unity project was closed before replacement. All 100 installed
sample files were hash-verified against the tested isolated package. All 52 valid
asset/resource GUIDs and the sample-folder GUID were retained. An existing empty
41-byte, YAML-header-only Grid panels Ribbon asset was not installed; it has no
serialized object and remains in the original backup.

The complete original folder and its metadata are recoverable at:
`tmp/ShortHairPartOriginalBackup-5d5db35574c4412e9afbaac369cf82a4`

SHA-256 checks confirmed all 179 files in PointySwept, CurlyVolume and BraidedBun
were unchanged from the start of this task, including pre-existing user edits.

See the [Short Hair Part guide](../ShortHairPartGuide.md) and
[precise texture painting workflow](../README.md#precise-texture-painting).
