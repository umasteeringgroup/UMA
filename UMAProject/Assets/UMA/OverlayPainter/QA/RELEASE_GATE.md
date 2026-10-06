# Overlay Painter Release Gate

Milestone 9 provides a repeatable release gate for Overlay Painter. It runs environment and asset preflight checks, then launches the EditMode and PlayMode suites in separate Unity processes. The separate processes validate clean assembly loading and persisted state across a domain boundary instead of relying on the state of an already-open editor.

## Run locally or in CI

From the Unity project root:

```bat
Assets\UMA\OverlayPainter\QA\Run-TexturePaintReleaseGate.cmd
```

The wrapper works when Windows' default PowerShell execution policy blocks direct `.ps1` invocation. The underlying script reads the editor version from `ProjectSettings/ProjectVersion.txt`. Pass `-UnityPath` to select another Unity 6.3+ executable and `-OutputDirectory` to redirect artifacts. It returns a non-zero exit code if preflight fails, either suite has a failure or skip, a suite runs zero tests, or Unity exits abnormally.

Results are written to `Logs/TexturePaintReleaseGate` (the project `Temp` folder is intentionally avoided because Unity can clear it between clean-process phases):

- `preflight.json`
- `editmode-results.xml` and `playmode-results.xml`
- one Unity log per phase
- `release-gate-summary.json` and `release-gate-summary.md`

In the editor, open **Window > UMA > Overlay Painter > Release Gate** for the same preflight checks. GPU golden-image mismatches emit expected, actual, and amplified-difference PNG files to `Temp/TexturePaintGoldenFailures`.

## Blocking matrix

| Area | Blocking validation |
|---|---|
| GPU tools | Paint, erase, blur, smear, clone, dodge, burn, and normal touchup execute the production kernels and match independent reference images. |
| Blend modes | Normal, Multiply, Add, Subtract, Screen, and Overlay match reference RGB and source-over alpha. |
| Paths | Sharp Beziers remain gap-free, carry direction/orientation, batch dispatches, and survive document reopen. 2D and 3D spline domains remain exclusive; rerasterizing width/effects replaces stale pixels; orange anchor, green curve, and blue width handles retain selection after adjustment; point deletion repairs selection safely. |
| Slots and UVs | One/many selected texture sets, cross-slot footprint discovery, different UV density, islands, mirrored UVs, and overlapping UV disambiguation. Linked members synchronize valid channel sources, fall back from legacy empty Texture sources, report missing writable targets, retain gesture capture across bounded seam misses, filter center rays to selected slots, and honor backface queries consistently. Triangle-restricted batches preserve per-stamp slot/triangle ownership and single-owned shared edges while crossing UDIM boundaries in either stroke direction. Ordinary 2D strokes rasterize directly in normalized UV space and remain stable on very small or thin geometry. |
| Layers | Ordered visibility, opacity, per-channel opacity, spline content, masks, Image Adjustments, independent per-layer Normal Control Height Strength, and plugin provenance. |
| Brushes | Preset/session transfer and persistence cover shape, sources, size, flow, random rotation/size, Splatter Distance, deterministic Random Strength, Fade, Taper, and pressure composition. |
| Workspace | Overlay Painter Compact View defaults on and creates one floating 40/60 workspace with Layers/Brush tabs on the left and dedicated Scene/2D tabs on the right; Layers and Scene start selected, the selected target is framed, geometry persists, and both reset commands rebuild the default arrangement. Its 3D, Path, and painting overlays register hidden, display only while the stage is active and only on the compact workspace's Scene instance, and repair stale serialized visibility restored by Unity so fresh projects and other Scene windows remain clean. Disabling the UMA setting retains the independent dockable-window workflow, and failure of Unity's validated dynamic-layout entry point falls back once without blocking stage opening. The Layers window owns targets, layers/paths, and properties without reserving shelf space. The Brush window is the sole Asset Shelf owner, keeps the shelf show/hide state and divider height persistent, and remains usable at compact dock widths. Escape exits Layer Mask mode from the Layers, Brush, 2D, or Scene input surface, commits an in-progress mask stroke, and consumes the key before Unity stage navigation can close Overlay Painter; Geometry Fill cancellation retains first-Escape priority. The native Scene-view painting toolbar owns all 13 former palette controls, stays synchronized with stage state, and replaces/auto-closes the legacy dockable toolbar. Character launches give priority to a target whose visible name contains the standalone word Body, ahead of restored target state, and enable Isolate for fresh or legacy state; the restored target is used only when no such Body target exists, while an explicit current visibility choice is retained. Standalone launches remain unchanged. |
| Plugins | API v2 discovery, independent read/write declarations, bounded immutable logical-channel snapshots, write-only channel metadata, compact and float tile commands, immutable non-readable parameter textures, and requested world/normal/signed-curvature/AO/thickness/ID maps. Persistent Plugin layers must read only their logical-target composite below the stack position, cache multi-channel output, retain typed parameters/texture references, work in groups and with masks/effects, mark dependencies stale, survive a missing implementation, replace atomically, retain the previous cache on cancel/failure, and round-trip through Undo/Redo and document recovery. Signed curvature separates concave and convex geometry; Agify combines it with composed normal detail and produces geometry-clipped, strip-buffered multi-channel weathering. |
| Persistence | Lossless base/layer pixels, document identity, save/reopen, channel-specific Normal Control strength, brush/path Random Strength, state serialization, configurable automatic-recovery enablement, idle/minimum-interval scheduling, and clean-process test execution. An explicitly opened saved document is the default when a compatible recovery also exists; recovery can be chosen instead but cannot silently replace the requested document. UMA generated-material nonces do not alter new surface ids; a legacy generated-material rebind with unchanged UVs but reordered topology must restore the authored layer and black mask exactly. |
| Export | 8-bit PNG, 16-bit PNG, half-float EXR, semantic packed maps, tangent-space RGB normal reconstruction, Runtime Overlay alpha generation, transactional cancellation, and asset/reference creation. Generated-character source reconstruction uses original native-resolution overlay inputs and never the generated atlas. |
| Scale | Actual 1K, 2K, and 4K target allocation/release; sparse history and coverage budgets; one history capture per touched target tile; event-level dirty-tile composite/preview updates; bounded per-tile raster batches and dirty-pixel work with mirroring enabled. |
| Import diagnostics | Recoverable material-source and duplicate-geometry conditions open without a modal, retain stable codes/severity/material/slot scope, and show clickable warning icons on affected logical targets, UDIM members, and texture-set rows. Unmapped records use the target-toolbar warning icon. The passive Scene-view notice lasts ten seconds, fades during its final 2.5 seconds, and never consumes paint input. Unsafe reconstruction failures remain blocking. |
| Lifecycle | Repeated target/map/store disposal, shared tangent-map reference counting, plugin/export cancellation, no stale active-process state after export completion, and no leaked owned render textures. The full-width Scene-view shutdown button follows Save/Discard/Cancel semantics; compilation, assembly reload, Play Mode, ordinary close, and failed stage opening synchronously hide the painting, 3D-control, and Path toolbars. |
| Pipelines | URP and HDRP Lit shaders in their corresponding release-matrix projects; the compiled descriptor resolves UMA meanings, documented packed-map conventions, output encoding, and importer settings. Projects without Addressables compile with Mark Addressable isolated behind `UMA_ADDRESSABLES`. Built-in/Standard is not certified. |

Weathering plugin release validation includes Agify fractal boundaries plus Dirtify and Edge Wear
detection size/level, spread, UV-island-safe sampling, multi-octave breakup, and geometry-clipped
strip output.

Production generator/filter validation also covers all Quilt/Embroidery/Perforation/Atlas Scatter
modes and coordinated optional outputs; Text block, Font/style persistence, Custom-ribbon following,
and grayscale group-mask execution; Stubble Maker facial/scalp profiles, downward strand controls,
transparent multi-channel coverage, placement, shadow, redness, rash, pimples, and spots; and
Stylization Kuwahara quality, RGB/luminance/palette
quantization, deterministic dithering, toon edges, cancellation, native-resolution output, and mask
compatibility.

The pipeline not active in the current matrix project may be reported as not applicable, but release certification requires separate successful URP and HDRP runs. Missing compute support is blocking for a release-machine run because GPU reference tests cannot execute; CPU fallbacks remain covered by the runtime suite.

### Quilt atlas and fabric finish validation — 2026-10-04

Validated current checkout `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed
UMASettings `UMA NextGen 3.1f1` (verified Unity YAML). Main-project compilation reports no errors
or warnings. The main editor's unsaved scene was preserved; rendering tests ran in
`tmp/UMAPluginCoreValidation` with byte-verified copies of the current painter and test sources,
the same Unity version, and the same verified installed UMASettings label.

- 25 PlayMode tests passed: quilt profiles, selected/random atlas tiles aligned to square and
  diamond panels, one stable tile per panel independent of scatter-grid controls, atlas-border
  sampling, preserved puff/stitch height and seam AO, missing-atlas cache preservation,
  standalone scatter output, cloth output/sprite cropping, and logical-layer propagation of
  empty plugin output settings across UDIM members before their first generation.
- 17 EditMode tests passed: active-mode parameter visibility, shared quilt/scatter atlas input,
  fabric-detail roughness and relief blending through first generation and Undo/Redo,
  parameter editing, and draft previews.
- Results: `Library/atlas-fabric-PlayMode.xml` and `Library/atlas-fabric-EditMode.xml` in the
  validation project. These are targeted regressions, not full pipeline release certification.

An initial broad `Cloth` test-name filter also selected unrelated core mesh-combiner tests;
two failed because the isolated project lacks their `UMAIgnore` tag. The final runs select
the painter fixtures explicitly. No user scene, material, or garment document was rewritten.

### Quilt Sprite Set validation - 2026-10-04

Validated `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed UMASettings
`UMA NextGen 3.1f1` (YAML header and actual value verified before each test run).
Main-project compilation: zero errors and warnings. Tests used
`tmp/UMAPluginCoreValidation` with hash-verified current painter/test sources and matching
Unity and installed UMASettings versions, preserving the main editor's unsaved scene.

- 25 PlayMode regressions passed, including all 11 Sprite Set channel outputs, inverted
  smoothness, retained puff height, deterministic enabled-only tiles across square/diamond/wave
  panels, shared tile choices across channels, selection cloning/serialization, invalid sheet
  counts and selections, plus existing quilt and atlas behavior.
- 20 EditMode regressions passed, including albedo picker click toggles, leaving the shared asset
  unchanged, native document reload retaining the set reference and enabled indices, draft
  rendering from the supplied Leather Sprite Set, parameter visibility, and existing preview/UI tests.
- Result files: `Library/atlas-fabric-PlayMode.xml` and `Library/atlas-fabric-EditMode.xml` in the
  validation project. The picker test exercises its actual IMGUI body in a utility window;
  it does not automate the operating system's modal-window blocking behavior.
- Changed painter/test sources pass `git diff --check`. The generated root solution has existing
  CRLF whitespace diagnostics and was not rewritten for this feature.

### Cloth color and authoring review - 2026-10-04

Reviewed the live Fabric Surface Detail layer in `C:\GitHub\UMA\UMAProject`. Albedo was enabled;
the dark brown base was mixing with a lighter cross-thread color. The layer's height blend was
Normal. The generator now preserves dye hue during fiber brightness variation and encodes compact
Albedo in sRGB for better dark-color precision. The host continues to operate in linear space.
The inspector exposes Fabric Color and a single-dye shortcut, provides an undoable relief-blend
action, and new cloth layers default to Overlay height blending. Flat/UV drafts offer a labeled
centered cloth close-up; authored scale and output are unchanged by preview zoom.

Validation used Unity `6000.3.18f1` and installed UMASettings `UMA NextGen 3.1f1`, verified from
the current project's Unity YAML asset before tests. Main-project compilation: zero errors and
warnings. Tests ran in `tmp/UMAPluginCoreValidation` after hash-verifying synchronized current
painter/test sources and matching Unity/UMA versions:

- 21 PlayMode tests passed: dark and saturated colors, all 16 weave profiles, UV/triplanar mapping,
  stripe/sprite output, anti-aliasing, and independent depth/shading controls.
- 29 EditMode tests passed: cloth close-up, unchanged authored parameters, enabling color and
  regenerating with preserved Overlay height settings through Undo/Redo, existing previews,
  parameter UI, and effective-normal composition.
- Visually inspected `Library/cloth-review.png` in the validation project: a blue dyed plain-weave
  swatch retains its hue and shows yarn contact shading in close-up.
- Source formatting check passed. Cached user layers need Regenerate to adopt the new color output;
  existing authored palettes and blend settings are not silently rewritten.

### Cloth triplanar stripe blending and controls - 2026-10-04

The live Fabric Surface Detail layer used World Triplanar with the generator's fixed fourth-power
crossfade. This mixes separate stripe placements around a curved leg. Cloth Texture 1.4.0 adds a
deterministic Crisp / Dominant Axis mode that selects the same plane for Albedo, Roughness and
Normal Control. Cross Fade now exposes offset and sharpness, with stable weights at high exponents.
World mapping size, position and rotation transform the coordinates, sampling footprint and blend
normal consistently. Mapping controls appear only in World Triplanar; hidden controls retain values.
Existing layers retain the original crossfade defaults until edited and regenerated.

Validated against `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed UMASettings
`UMA NextGen 3.1f1` (YAML header and actual version checked). Main compilation reported zero errors
and warnings. Tests used `tmp/UMAPluginCoreValidation`, matching Unity/UMA versions and hash-verified
current painter/test sources, to avoid disturbing the open editor scene:

- 26 PlayMode tests passed, including ghost-stripe reproduction and removal across curved surface
  directions, consistent channel selection, blend offset/sharpness, equal diagonal weights at
  maximum sharpness, mapping scale/translation/rotation on all axes, and unchanged UV generation.
  Existing dye-color, weave, antialiasing and material-response regressions also passed.
- 30 EditMode tests passed, covering conditional mapping controls, previews, parameter editing,
  Undo/Redo and normal composition.
- Changed-source formatting check passed. The user's actual pants were not visually re-rendered
  after the change; the crisp mode's projection joins remain a documented limitation.

### Whole-cloth and individual stripe rotation - 2026-10-04

Cloth Texture 1.5.0 adds a top-level Rotation for the complete fabric. Sampling and weave
antialiasing include that angle in UV and triplanar modes. The existing saved weave-only rotation
keeps its parameter ID and is labeled Weave Rotation; Stripe Layout Rotation remains independent.
Each stripe now stores an optional angle. Legacy definitions resolve their direction to 0 degrees
(horizontal) or 90 degrees (vertical). Direction preset buttons reset the angle; the renderer uses
the angle rather than the direction enum. Angles are in the plaid repeat grid. Custom rotations
survive parameter cloning and JSON serialization; invalid non-finite angles are rejected by the host.

Verified the current checkout's Unity `6000.3.18f1` and YAML UMASettings `UMA NextGen 3.1f1` before
validation. Main-project compilation had zero errors/warnings. In `tmp/UMAPluginCoreValidation`,
with matching versions and hash-verified current painter/test sources:

- 33 PlayMode tests passed: complete channel rotation, 45-degree world-plane rotation, independent
  diagonal stripe angles, cardinal presets, legacy deserialization, saved-angle and clone isolation,
  plus existing cloth, triplanar, color and weave regressions.
- 30 EditMode tests passed: stripe creation buttons and their default angles, parameter controls,
  preview generation, Undo/Redo and normal composition.
- Changed-source formatting checks passed. No existing authored rotation values were rewritten.

### Cloth pattern sprite visibility - 2026-10-04

The live Fabric Surface Detail layer had `imperfections_5` assigned, Pattern Opacity 0 and Use Sprite
Color disabled. Enabled opacity and sprite RGB through the existing undoable parameter-edit path,
regenerated the layer and confirmed its cached output was current before recompilation.

Cloth Texture 1.5.1 places opacity and color controls beside Pattern Sprite. Fresh defaults use
opacity 1 and sprite RGB. First sprite assignment enables an older zero-opacity layer; replacing
an existing sprite preserves authored opacity/tint choices. Existing assigned patterns with zero
opacity show an explanation and Enable Pattern action. Color-disabled layers explain which output
setting is needed. Explicit zero opacity continues to disable every pattern contribution.

Validated against current checkout `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, and installed
YAML UMASettings `UMA NextGen 3.1f1`. Main compilation reported zero errors/warnings. The synchronized,
hash-verified validation project covered 33 distinct PlayMode tests across the cloth suite and a
focused sprite/color rerun, plus 31 passing EditMode tests. Coverage includes real atlas-sprite
capture from fresh defaults, intentional disabling, pattern control placement, cloned parameter
editing, existing cloth rendering, previews and normal composition. The sprite disabling test reads
the second command's new output layer rather than the previous command's cached layer.
Changed-source formatting checks passed.

### Scene navigation after recovery or interrupted gestures - 2026-10-04

Alt navigation now yields before reconstruction/save guards, raycasts, layer checks and tool input.
The initial Alt key event and Layout are included so capture is released before Unity's navigation
mouse-down. The handoff clears painter-owned paint, spline, brush-adjustment, projection, stencil,
region and symmetry captures even when their gesture flags are false. It never consumes the event
or clears another owner's control. Symmetry gizmos now track and release their native handle
capture. Completed stencil movement remains undoable when Alt interrupts the drag. Startup after
state restoration also resets transient interactions; close releases stencil/symmetry captures.

Verified against `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed YAML UMASettings
`UMA NextGen 3.1f1`. Main-project compilation passed with zero errors/warnings. In the synchronized,
hash-verified `tmp/UMAPluginCoreValidation` project, all 36 selected EditMode tests passed: real IMGUI
dispatch with stale captures and missing reconstruction, all three Alt mouse buttons, Alt key-down,
Layout/Repaint, Unity-control preservation, stencil undo, existing projection handles and layer
preview interactions. Changed-source formatting checks passed.

The exact reported pants-session recovery sequence was not reproduced interactively. Regression
tests inject the stale ownership states and verify the actual OnSceneGUI handoff without requiring
a layer selection change; they do not assert camera rotation from a recovered pants document.

### Creature generator UV boundary coverage - 2026-10-04

The live pants creature layer had 8,416 transparent pixels inside its 2,048-square geometry mask;
all were rejected by the 1,024-square SurfaceId map. Mesh-map rasterization used width/height minus
one with texel-center sampling, shrinking coverage. It now uses full dimensions and a three-texel
guard band sampled from the nearest point on the UV triangle. Real triangle samples take priority
over padding, including reversed winding and neighboring islands. All map channels receive the same
valid boundary samples; final output remains clipped by the destination geometry mask. Organic
generator versions advance so older generated caches can be identified as outdated.

Verified in `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed YAML UMASettings
`UMA NextGen 3.1f1`. Main-project compilation passed with zero errors/warnings. A read-only capture
of the live pants mesh (3,856 triangles) was checked again using the fixed builder: all 1,985,445
covered output pixels passed SurfaceId coverage, and none mixed world-position samples with empty
map data. This checks the reported mesh and output resolution, not an interactive visual comparison
of a regenerated material. Existing layer caches require regeneration.

All 58 selected PlayMode regressions passed in the synchronized, hash-verified
`tmp/UMAPluginCoreValidation` project. These include exact texel-center positions across a full UV
rectangle, reversed winding, diagonal boundaries at equal and higher output resolutions, all six
creature channels in UV/world modes, geometry masking, organic generation and cloth/rust mapping.
The creature test explicitly sets Skin Mask Strength to one when checking opaque channel coverage.

### Path symmetry diagnosis and Custom scar guides - 2026-10-05

The recovered pants document's Path Layer 9 has all mirror axes disabled and one radial copy.
The previously captured pants mesh contains 1,500 opposite-side triangle pairs sharing identical
UV triangles. One authored texture stripe therefore appears on both legs without a symmetry copy;
independent left/right results require separate texture coordinates. No mesh or document was edited.

The same document exposes only Albedo, Normal, Metallic, Roughness and Normal Control. The scar
generator's documented Custom Ribbon Channel workflow was inaccessible because Add Channel only
offers channels present on the texture set. TextureStore now initializes a black, linear Custom
guide target at the Albedo resolution (or another available target's resolution). An existing
material-provided Custom target is preserved. The new target has no material property or physical
UMA channel index; the shader's declared channel descriptor remains unchanged. Reopen an older
painter session to initialize it, then add Custom to the Path, use a white Color source, keep the
guide enabled, and regenerate a Scar plugin above it using Custom Ribbon Channel.

Validation targets the current `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1` and installed YAML
UMASettings `UMA NextGen 3.1f1`. The focused Custom-guide integration fixture uses real TextureStore
initialization, GPU layer composition and Scar generator execution. It verifies localized output
from a visible path, no output from empty/hidden paths, matching channel dimensions, linear data,
and unchanged physical material/export descriptors. All three focused EditMode cases passed in the
synchronized, hash-verified validation project; the main project compiled with zero errors/warnings.
It does not exercise the export writer.

An initial broader isolated run was blocked by existing fixture dependencies: the export fixture
requires URP Lit, absent from that validation project, and the path-symmetry fixture encountered
an unrelated Unity ObjectNames call in the stage window's static initializer. Those failures were
not treated as passing tests or changed as part of this fix; focused guide checks have their own
pipeline-independent fixture.

### Separate dockable Properties window - 2026-10-05

Properties now renders in `TexturePaintPropertiesWindow`, sharing the stage's selected layer,
scroll position, parameter editing, shortcuts, path regeneration and undo handling. The Layers
window uses its full content height for the layer/path list. View > Open Properties and
Window > UMA > Overlay Painter Properties open/focus the new window. The compact layout's left
tab group contains Layers, Brush and Properties. Fallback windows also prefer that tab group on
creation; reopening an existing window preserves its current placement. Layout rebuilds close the
old Properties host and all selection/progress repaint paths include it. Legacy document visibility
fields remain serializable but no longer hide the independent Properties window.

Validated against `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed YAML UMASettings
`UMA NextGen 3.1f1`. Main compilation passed with zero errors/warnings and changed-source formatting
checks passed. All four focused EditMode tests passed in the synchronized, hash-verified validation
project: compact grouping, actual Unity dock-parent sharing, duplicate prevention, independent close,
preservation of a floating window, and the existing compact workspace/Scene-host contract. These
checks exercise real EditorWindows; they do not claim visual testing of every property control.

### Path generators, scar migration, tattoo shapes and lettering - 2026-10-05

Added an extensible path generator mode with 34 built-in constructions, all under
`+Path > Generators` after `Garments`. Scar/Wound uses path distance, width, healing,
relief and optional sutures. Existing scar plugin layers remain loadable and offer a
non-destructive settings copy into a new path. Text and Lettering uses a selectable
Unity Font; Tattoo has independently tapered/curled ends and spiral, tribal flame
and tribal scroll shapes. Seven stitch patterns, sixteen seam constructions, and
four zipper/distress constructions use the same path generator mode. Existing
image, garment and seam paths remain compatible.

Validated against the current `C:\GitHub\UMA\UMAProject` checkout, Unity
`6000.3.18f1`, and the installed YAML UMASettings `UMA NextGen 3.1f1`. Package
archive version remains `3.0.4`; it is not the installed UMA version. The final
EditMode run passed **165 tests, 0 failed, 0 skipped** in the synchronized,
hash-verified validation project. Suites: TexturePaintPathGeneratorTests,
TexturePaintPathGeneratorEditorTests, TexturePaintHemSeamTests,
TexturePaintPathSymmetryTests, and TexturePaintLayerPreviewTests.

Checks include all 34 GPU generators, UV/world path rebuilding and pixel/settings
undo/redo, menu placement, all eleven channel alpha masks, metallic/scalar values,
text without Albedo output, empty text, font persistence, negative-space silhouettes,
unclipped preset masks, independent taper/curl controls, healing and suture relief,
legacy scar settings, missing extensions and failed preparation preserving pixels,
prepared resource disposal, previews, existing seams, and symmetry. Tattoo mask PNGs
were visually inspected for tips, curls and negative space. An HLSL reserved-word
error and EditorPrefs access during EditorWindow type initialization were fixed
during validation. These checks use generated test surfaces and EditorWindows;
they do not claim an interactive visual pass on the user's recovered pants session.

Main-project compilation reported zero C# errors/warnings before the final control
refinements; the final synchronized suite compiled and exercised the refinements.

### Parameterized tribal tattoos - 2026-10-05

Tribal Flame and Tribal Scroll now build procedural branches with zero to sixteen
arms, attachment start/end, length, angle, sweep, curl direction/radius, spine bend,
mirrored or half-turn pairs, and seeded variation. Symmetry applies to the complete
construction, including cutouts and optional end curls. A local integer hash makes
variation reproducible without consuming Unity's random state. The complete contour
is fitted before rasterization to retain transparent gutters at extreme settings.
Saved fixed motifs remain unchanged until Procedural Arms is enabled; new tribal
paths enable it by default. The existing shared mask still drives every material channel.

Validated against `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed YAML
UMASettings `UMA NextGen 3.1f1`. Main-project recompilation reported zero errors and
warnings. The synchronized, hash-verified validation project passed **84 EditMode
tests, 0 failed, 0 skipped** in TexturePaintPathGeneratorTests and
TexturePaintPathGeneratorEditorTests. Coverage includes every new shape control on
both tribal designs, deterministic/disabled variation, complete paired symmetry,
extreme-setting gutters, legacy preservation, serialization, UV/world path edits and
pixel/settings undo/redo, and all eleven material-channel masks. Procedural Flame
and Scroll raster previews were visually inspected. The test surfaces do not replace
an interactive visual check on a recovered user character.

### Text and Lettering X/Y flips - 2026-10-05

Added independent Flip X and Flip Y controls beneath Font Style. X mirrors the
glyph image along the path; Y mirrors it across the path; together they rotate it
180 degrees. Signed sampling scales also flip texture derivatives, so preview,
repeated lettering, shared material coverage and generated relief stay aligned.
The flags default to false, clone with path settings and survive native document
save/reload without changing path points.

Validated against `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed YAML
UMASettings `UMA NextGen 3.1f1`. Main compilation reported zero errors/warnings.
The final focused run passed **10 tests, 0 failed, 0 skipped** in the synchronized,
hash-verified validation project: X-only, Y-only and both flips on UV/world paths
with repeated asymmetric text and all eleven channel masks, Normal Control relief,
three preview orientations, and native Font/text/flip persistence. Existing generator
and editor integration checks also passed in the preceding broader run. The six
initial path-flip cases edited a stale settings reference after BeginStroke's snapshot;
the fixture was corrected to edit the current context before the successful rerun.

### Generated-path contour effects - 2026-10-05

Generated paths previously evaluated Stroke, glows, shadows and Procedural Stitch
against the ribbon edges, missing inset glyphs and motifs. They now retain an
unstyled source raster and composite those effects from its alpha contour. Contour
stitches provide pixel-based thread width, length, inset and single/double rows.
Ordinary ribbons retain their original edge behavior. Effect edits recompose
without regenerating the path; the draft preview includes the contour effects and
invalidates when settings change. Edge Fade and Bevel Edge remain ribbon effects.

Validated against `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed YAML
UMASettings `UMA NextGen 3.1f1`. Main-project compilation reported zero errors and
warnings. The synchronized, hash-verified validation checkout passed **111 tests**
for path generators, editor integration, contour-effect previews, raw-pixel and
transparency preservation, effect ordering/opacity, immediate edits and document
settings persistence. A separate **7-test** run passed ordinary ribbon stroke,
glow, shadow, bevel and stitching regressions in world and UV modes. Both runs had
zero failures and skips. Rendered Stroke, Inner Glow and Procedural Stitch samples
on the letter O were inspected, including its inner contour. These checks do not
replace an interactive check of the user's recovered session.

### Complete contour effects on paint and plugin layers - 2026-10-05

Bevel Edge and Edge Fade now participate in the shared alpha-contour compositor.
Bevel shades opposing contour normals with light/dark colors and an editable light
angle, preserving glyph holes and coverage. Fade removes the layer contribution
toward its edges, revealing the original backdrop and applying layer opacity only
once. Generated paths no longer bake either effect into ribbon geometry. Paint,
plugin and generated-path layers expose all eleven effect types; ordinary ribbons
retain their existing edge controls. Plugin draft previews now apply the same
effect stack, and changes invalidate their cache. Bevel angle clones and persists
with the document.

Verified project `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed YAML
UMASettings `UMA NextGen 3.1f1`. Main compilation completed with zero errors and
warnings. The synchronized/hash-verified validation checkout passed **170 tests**
covering path generators, plugin previews, editor integration, effect ordering,
opacity, persistence and ordinary ribbon regressions. A **36-test** run
confirmed all **11 effects across all 34 registered path generators**, with
identical results for the same source pixels on Paint and Plugin layer kinds,
plus directional bevel lighting and fade/backdrop composition. A final **50-test**
rerun passed the full effect matrix and previews after smoothing the bevel's
distance-field normals to remove raster-edge sparkle. All runs had zero
failures/skips. Inspected the rendered bevel, including the glyph's inner contour;
the user's recovered session has not been visually checked.

### Connected subdermal veins - 2026-10-05

Replaced the two crossing periodic line families with seeded, curved vessel trees.
Daughter vessels attach to their parent, taper toward their tips, and use staggered
junctions and bounded corridors to avoid grid crossings. Subpixel coverage softens
thin tips. Vein Network Density retains the existing saved scale parameter; the
new Vein Width defaults to 1 for older layers. The vein plugin version is 1.1.0.

Validated in the running main project `C:\GitHub\UMA\UMAProject`, Unity
`6000.3.18f1`, installed UMASettings `UMA NextGen 3.1f1`. Compilation completed
with zero errors/warnings. All **5 vein field tests** and **13 plugin preview
tests** passed: seeded repeatability, independent width/branch controls, direction,
absence of enclosed grid cells across three seeds, matching albedo/thickness
response in UV and world projection, full plugin-layer generation and undo.
Inspected the generated 512-pixel vessel sample at
`Library/VeinGeneratorQA/branching-veins.png`; the user's recovered asset has not
been visually checked. Regenerate cached layers to adopt the new network.

### Skin bruise continuity and pore relief - 2026-10-05

Skin generator 1.2.0 evaluates every neighboring seeded bruise to its own soft
boundary instead of switching intensity at nearest-cell boundaries. Pores use a
broader continuous profile to intersect the surface more reliably. Normal Control
retains float tile precision; overlay height is unpremultiplied around neutral
gray so coverage applies once. The skin output uses straight-alpha replacement.
Optional Vein Surface Height and Bruise Swelling default to zero and control
surface relief separately from subsurface color/thickness. Precise height tiles
use the existing GPU plugin commit kernel, with the CPU fallback retained.

Validated in `C:\GitHub\UMA\UMAProject`, installed UMASettings **UMA NextGen 3.1f1**,
Unity **6000.3.18f1**. Compilation had zero errors/warnings. Passed **3 bruise-field
continuity tests**, **19 plugin preview/integration tests**, and **10 Normal Control
tests**, with no failures or skips. Skin integration checks cover UV/world
projection, overlay/full skin, sub-byte height, 8-bit and half-float normal outputs,
preserved source normals, normalized combined normals, and undo. Inspected
`Library/SkinGeneratorQA/continuous-bruises.png`; the user's recovered asset was
not modified or visually validated. Regenerate cached skin layers to adopt the fix.

## Rust follows underlying normal detail - 2026-10-05

Rust/Corrosion 1.2.0 reads the combined Normal and Ambient Occlusion channels
below its layer, in addition to mesh curvature/AO. Signed tangent-normal
divergence separates concave bevel feet from convex crowns. Normal Control
relief is included by the existing below-layer normal composition. Sampling
rejects neighbors outside the texture or on another UV island. Normal Detail
Influence and Normal Detail Radius tune the response. Channel snapshots are
capped at 2048 to leave room for mesh maps within the host snapshot budget;
generated output still uses the destination resolution.

Validated in `C:\GitHub\UMA\UMAProject`, installed UMASettings **UMA NextGen 3.1f1**,
Unity **6000.3.18f1**. Compilation had zero errors/warnings. All **24 plugin
preview/integration tests passed**, including five rust cases covering painted
normals, Normal Control relief, underlying AO, UV/world generation, draft/full
output, concave versus convex targeting, zero influence, and deterministic
regeneration excluding self/higher layers. The user's pants document was not
modified or visually validated. Regenerate existing cached rust layers.

## Surface-aware generator normal audit - 2026-10-05

See [Normal detail audit](NORMAL_DETAIL_AUDIT.md) for the full inventory. Edge Wear,
Dirtify, Agify, Dripping Corrosion, fabric fraying, and combat damage now share
guarded normal-derived curvature sampling with rust. GPU plugin regeneration
captures lower-layer Normal/AO rather than the whole stack. Normal-based detail
participates in weathering spread; below-layer height sampling preserves precision.
Generators that create independent patterns retain their existing placement behavior.

Validated in `C:\GitHub\UMA\UMAProject`, installed UMASettings **UMA NextGen 3.1f1**,
Unity **6000.3.18f1**. Zero compilation errors/warnings. Passed **128 editor tests**
(21 weathering, 24 previews, 73 color correctness, 10 Normal Control). Runtime API
validation passed 86 cases initially plus the updated input-contract case on its
targeted rerun, covering all 87 cases. No skips or unresolved failures. Tests use
synthetic surfaces; the user's saved pants document was not modified or visually
validated. Regenerate affected cached layers and tune Normal Detail Influence/
Radius; saved influence values remain intact.

## Stubble Maker full-area coverage - 2026-10-06

New Stubble Maker layers default to Entire Unmasked Area. Rectangle and Ellipse
retain their serialized values and remain optional additional boundaries. Control
masks and target mesh coverage still clip all generated material channels.

Validated in `C:\GitHub\UMA\UMAProject`, installed UMASettings **UMA NextGen 3.1f1**,
Unity **6000.3.18f1**. Compilation had zero errors/warnings. All **5 Stubble Maker
PlayMode tests passed**, covering default full coverage, black/white control mask
clipping, saved rectangle/ellipse placement, mesh footprint clipping, and the
existing transparent multi-channel output test. Existing saved layers retain
their placement; select Coverage > Entire Unmasked Area and regenerate to change it.

## Stubble Maker world growth direction - 2026-10-06

Growth Direction now defaults to World Down, using World Position, World Normal,
and Surface ID maps. A local surface-metric solve handles rotated, mirrored,
skewed and stretched UVs. World forward is the fallback on horizontal surfaces.
Root-anchored strands use spatial bins; sampling stays on the root's UV island.
UV Direction retains the original algorithm and avoids geometry snapshots.
Existing follicle redness remains attached to roots in both modes.

Validated in `C:\GitHub\UMA\UMAProject`, installed UMASettings **UMA NextGen 3.1f1**,
Unity **6000.3.18f1**. Compilation had zero errors/warnings. All **15 Stubble
PlayMode tests** and **1 stubble editor draft-preview test** passed. Coverage
includes surface metric/rotation, mirrored and rectangular UVs, horizontal and
island-boundary behavior, generated strand orientation, root redness, manual UV
mode, deterministic previews, masks, and all generated channels. Tests use
synthetic surfaces; the user's saved project was not changed. Regenerate existing
layers to use World Down; select UV Direction to retain the older alignment.

## Stubble signed relief and follicle pimples - 2026-10-06

Stubble Maker 1.3.0 writes float Normal Control with straight-alpha tiles and
coverage applied once to displacement around neutral gray. Hair shafts have a
rounded raised cross-section. Follicle Depth/Radius add shallow root depressions,
independent of redness. Pimples now select hair follicles, replacing their pore
depression with a conical elevation and a lighter, configurable center color.
Root-feature bounds cover both direction modes, including large pimples.

Validated in `C:\GitHub\UMA\UMAProject`, installed UMASettings **UMA NextGen 3.1f1**,
Unity **6000.3.18f1**. Compilation had zero errors/warnings. All **20 focused cases**
passed: 15 PlayMode stubble cases (14 initially, then the float-budget-adjusted
case on rerun), four new CPU/GPU upload and world/UV relief integration cases,
and the automatic draft preview test. Checks cover raised/recessed height,
sub-byte relief, conical falloff, light centers, half-opacity scaling, final
normal-map perturbation, untouched source normals, and zero-relief restoration.
The saved user project was not modified; regenerate existing cached layers.

## Regenerate all stale layers from Layer Preview - 2026-10-06

Layer Preview now includes **Regenerate all stale layers**, available regardless
of the selected layer type. The action covers the current document's stale
plugin layers and generated masks, including hidden layers, with progress and
cancellation. It processes the live stack bottom-to-top, rechecks invalidated
dependents after each commit, and uses existing logical-peer generation and
undo operations. Automatic linked refresh waits until the batch finishes.
Failure or cancellation stops the batch while retaining the remaining work.

Validated in `C:\GitHub\UMA\UMAProject`, installed UMASettings **UMA NextGen 3.1f1**,
Unity **6000.3.18f1**. Four focused editor tests passed for live replacement and
ordering, hidden layers/masks, logical peers, failures/cancellation, and no-op
loop prevention. Compilation completed without errors or warnings.
