# Card profile UI and root opacity validation

## Changes

- Hair Properties exposes **Width along card** below Root Width / Tip Width,
  with explicit axis/value meaning, effective endpoint widths and a warning for
  transitions shorter than one sampled segment. The Card Profile asset Inspector
  has grouped, plain-language geometry, sampling and RGBA controls.
- Groom Inspector groups, helpers, LODs and other named list items use authored
  names instead of stable IDs. Internal identifiers are read-only. Serialization
  identities and existing profile curves are unchanged.
- Both URP hair shaders honor root/tip tint alpha, multiplied by base alpha,
  optional vertex alpha and root opacity fade. Atlas alpha density no longer
  amplifies these user opacity factors.
- Hybrid cores exclude partially transparent strand regions from color and depth
  passes, leaving their soft opacity to the fringe. Standalone Cutout retains its
  existing threshold/dither behavior. Alpha Blended remains a lit transparent pass.
- Existing Hybrid pairs work in the stage through owned preview copies without
  dirtying shared materials. **Sync fringe from first pass** explicitly persists
  the corrected pair for exports. Validation warns when that step is needed.

## Verification

Unity **6000.3.18f1**, isolated source validation project, 2026-09-16:
**652 passed, 0 failed, 0 skipped**. The release gate imported and compiled the
actual source; metadata preflight found 98 Hair Cards C# scripts.

Result file:
`tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/225aad5556ab4180bdd2d35656d8f05f/editmode.xml`

Eight new test cases cover named labels/renaming without ID changes, serialized
width-curve editing and Undo, both asset Inspectors drawing at 360/800-pixel widths
without mutation or GUI errors, private preview correction versus explicit
shared-material sync/Undo, and GPU alpha blending for both Soft and Hybrid at
1x/4x MSAA. GPU checks exercise a root-to-tip alpha ramp, reversed atlas UVs,
root fade, Hybrid depth normals, coverage, and combined base/vertex alpha.
Existing shader compilation, standalone alpha coverage, rendering modes,
geometry, authoring interaction and saved-sample regressions also passed.

A focused run passed 18 tests before the full gate. An initial narrow test filter
did not initialize the URP pipeline before direct command-buffer shader tests;
including the existing Scene-view initialization test resolved that harness
condition. The full suite initializes the pipeline through its normal editor
tests. Shader results above are from initialized URP, not a fallback pipeline.

SHA-256 comparison confirmed **all 379 example files unchanged** during this
task, including user-edited grooms, profiles, materials, meshes and textures.
No example regeneration, curve reset or automatic material migration was run.

## Limits

Soft/Hybrid root regions still use transparent sorting; intersecting cards can
show ordering artifacts. Hybrid uses two draws and requires both materials in
exports. Existing material pairs require the explicit sync action described
above outside the stage. This work exposes the saved width curve; it does not
add new tessellation or automatically change a user's hairstyle. Automated UI
checks confirm rendering/layout execution, not a manual visual sign-off of
every hairstyle.
