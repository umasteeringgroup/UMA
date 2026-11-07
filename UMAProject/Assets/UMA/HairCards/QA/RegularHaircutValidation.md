# Regular Haircut validation

Historical report. The example is now named ShortHairPart and uses precise
texture-backed growth maps. See [current validation](TextureMapsValidation.md)
for the rebuilt sample, current triangle counts and regression results.

## Reference and design

Inspected the supplied RegularHaircut scalp, 39 raw centerlines and its converted
mesh with embedded script execution disabled. The source file was not saved.
The converted mesh contains 5,351 independent ribbons, each with ten vertices,
four quads and eight triangles: 42,808 triangles total. It uses three short atlas
regions and two scalp islands to preserve the side part.

The existing surface populations, weighted guide interpolation, independent
groups, clumping, noise, length modifiers, hairline controls, ribbon profiles,
atlas setup, scalp vertex shading and URP shader support this style. No new
runtime modifier, geometry schema, or replacement evaluation path was needed.

Added an opt-in Regular Haircut generation preset, additive short UV-set setup,
three example menu entries, and a per-group active-LOD triangle ceiling in
the existing IMGUI properties. Presets preserve authored guides/maps/sculpting
and assigned atlas/material resources and groom-wide LOD overrides, use popup
confirmation and support Undo.
The count readout explains the actual Geometry & Vertex Colors control rather
than adding another navigation system.

## Sample

Classic, Relaxed and Close Cut retain 28 crown/rear/opposite-side guides and 11
part-side guides in separate groups. They have independent grooms, ribbon
profiles and materials, plus owned texture copies inside RegularHaircut. Each
variant includes a three-LOD hair-only preview prefab. The review scene enables
Classic and disables the other two; the body is an unrigged reference with scalp
vertex shading.

Four length segments, one width span and no duplicated backfaces provide eight
unique triangles per card. The coverage update below adds a second material draw,
not extra vertices or cards. Requested density is split
2,100 / 650 between the groups; painted growth and hairline acceptance reduce
the actual count below 2,500. The 20,000-triangle bake budget is advisory, not an
automatic cap on future edits.

| Variant | LOD0 triangles | LOD1 triangles | LOD2 triangles |
|---|---:|---:|---:|
| Classic | 19,744 | 9,582 | 3,424 |
| Relaxed | 19,744 | 9,684 | 3,540 |
| Close Cut | 19,768 | 9,690 | 3,400 |

All nine meshes have zero detected frame flips and zero degenerate triangles.
No generated roots have rejected guide influences. LOD0 is approximately 54%
smaller than the converted reference, achieved by reducing card population,
not by presenting a lower LOD as the top level.

## Verification

Unity 6000.3.18f1 compiled the actual sources with graphics enabled in the existing
isolated validation project. The first complete gate passed **512 tests, zero
failures or skips**, including preset preservation and Undo, additive/idempotent
UV setup, all three variants' LOD budgets, resource ownership, and regenerated
versus saved geometry. Existing source import, rendering, modifier, grooming and
Curly/Pointy checks remain in the suite.

Initial gate: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/660ce67d7c23412983df252c65815944/editmode.xml`.
Final packaging gate: **512 passed, zero failures, zero skips**.
Result: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/a954ba467f014419819ade42fdcc794c/editmode.xml`.
Main-project metadata preflight passed (74 C# files). All 88 packaged sample files
match the tested isolated copies byte-for-byte. All 70 PointySwept/CurlyVolume
resource and metadata files retain their pre-task SHA-256 hashes, including the
user's edited Curly groom. The original reference file was never modified.

Reviewed actual Unity renders from front, both sides, rear and three-quarter,
all three variants, and lower-LOD coverage. Iteration softened the initially
overly harsh depth/color contrast and widened hairline coverage near the part.
The final crown and side use independent profiles so adjusting one does not
unexpectedly change the other. Mesh UV dimensions are retained during updates.

## Limits

This is an editable reconstruction of the style, not identical generated roots
or pixel-identical shading. The roughly halved card population changes fine
overlap, and four segments deliberately limit curvature detail. Lower LODs need
distance-appropriate review; their close-up coverage is not equivalent to LOD0.
Fine alpha strands need target-hardware antialiasing, motion and lighting tests.

The sample does not change the main project's render-pipeline settings. The
neutral review body is not a skinned UMA character. Binding real race slots,
weight copying, producing a scalp Mesh Modifier and validating animation remain
production steps documented in the guide. Saved preview meshes are not live
links: rebake them after edits. The stage's live preview remains available.

The Unity CLI installer was unavailable under the execution policy. With the
user's approval and the main Editor closed, generation/testing used the installed
Editor directly in the isolated project. Assets were authored through Unity
serialization APIs; binary assets were never decoded or text-rewritten.

## Coverage and Close Cut correction (September 2026)

The sparse-coverage report reproduced at 650 x 650 without MSAA. The short-strand
atlas contains faint alpha fibers, many of which were rejected by a 0.3 cutoff.
Rendering alone cannot repair the separate scalp-intersection problem: before
correction, Close Cut had 51 mesh vertices and 51 evaluated centerline points more
than 1.5 mm below the nearest surface tangent plane, with a minimum of -2.37 mm.

The cutout shader now applies coverage once: a pipeline-aware MSAA path, opaque
accepted texels outside that path, or threshold-aware screen-door coverage.
Zero-alpha texels remain discarded even with a zero cutoff. Alpha density defaults
to 1, leaving existing material alpha unchanged unless the user adjusts it.

The new Soft Hair URP shader shares the existing lighting and color controls but
uses alpha blending without color-pass hard clipping or depth writing. The
Hybrid setup assigns an AlphaTest-queue core and Transparent-queue fringe through
the existing two-material path. Both passes share vertices, but use independent
index ranges: Unity 6.3 warns that overlapping submesh index ranges are undefined.
The primary casts shadows; the fringe does not cast a duplicate shadow.

Classic, Relaxed and Close Cut now use Hybrid, density 2.5 and core cutoff 0.45.
Close Cut additionally removes its 1 mm root embedding and appends a 6 mm
closest-surface-normal Lift to each generated-card population after shortening.
Roots stay anchored, segment lengths stay fixed and authored guides are untouched.
The resulting LOD0 minimum is approximately -0.57 mm, with no mesh vertices or
centerline points deeper than -1.5 mm. A test checks all three saved/rebuilt LODs
against a -1 mm vertex tolerance, allowing shallow root/ribbon-edge contact.
Classic and Relaxed geometry is unchanged. All nine unique triangle counts in
the table above are unchanged. Hybrid roughly doubles color-pass triangle
submission and adds transparency overdraw; it is not a free quality setting.

Regression coverage includes rendering modes, assignment Undo, retained source
materials/colors/textures, fringe synchronization, independent index ranges,
constant-alpha GPU readback at 1x/4x MSAA, zero-alpha rejection, sample bounds,
saved-versus-rebuilt geometry and existing hairstyle checks. Actual URP renders
were reviewed from the side and rear at 1x/4x MSAA, plus front three-quarter
previews for all three variants. Native assets were modified through Unity APIs
in the isolated project. Main-project render-pipeline settings were not changed.

Final coverage packaging gate: **535 passed, zero failures, zero skips**, using
Unity 6000.3.18f1 with graphics enabled and URP assigned in the isolated project.
Both shaders' forward, shadow and clustered-light variants are included.
Result: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/5e2a702b4ded4df0a3c3ab7acac95a6f/editmode.xml`.
The preliminary GPU gate correctly failed when the diagnostic harness left the
isolated project without a render pipeline; assigning its persistent review URP
asset fixed that test-environment issue. The main project was never switched.

Packaging checks: all **100** RegularHaircut files match the verified isolated
copies byte-for-byte; all **179** PointySwept, CurlyVolume and BraidedBun files
retain their pre-update SHA-256 hashes. Original Regular Haircut material assets
are preserved alongside the new core/fringe pairs. Main metadata preflight passed
for 78 Hair Cards scripts. A recoverable pre-update sample copy is at
`tmp/RegularCoverageBackup-d2803db418804a05b287d37a59c34152/RegularHaircut`.
