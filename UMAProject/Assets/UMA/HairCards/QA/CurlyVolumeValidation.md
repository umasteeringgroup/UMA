# Curly Volume validation

## Scope

Reviewed the supplied Curly object, its 90 authored centerlines and evaluated
geometry. The saved reference generates 1,133 cards (78,626 vertices / 76,360
triangles). Its characteristic shape uses a roughly 11.2 mm curl radius and
26.7 mm envelope distance per turn, variation between cards, and three long atlas
strips. The source scene was opened read-only in a background process with
embedded script auto-execution disabled. No external add-on code was copied.

Existing surface-root generation, weighted neighbors, growth/hairline controls,
sculpting, gravity, masks, atlas selection, scalp shading and LODs were reusable.
Missing capabilities added for this reconstruction:

- Ringlets with transported local frames, distance-based pitch or turns per
  strand, seeded radius/turn/phase/winding variation, clump coherence and facing.
- Automatic shape subdivision retaining original controls/frozen anchors, smooth
  centerlines, and explicit envelope-preserving versus length-preserving modes.
- Up to 256 profile samples, including compatible root vertex-color hold ranges.
- A non-destructive Curly preset, three-strip setup, inline matte/gloss controls,
  a separate editable example and menu entry, and an artist guide.
- Optional dithered coverage, disabled by default in the sample because stationary
  stippling is visible without suitable antialiasing.

## Sample and visual review

The sample contains 90 editable guides and its own source snapshot, atlas, profile,
material and texture copies. It has no dependency on PointySwept resources.
Generated cards, a static prefab, scalp-color preview mesh and a URP review scene
are included. Large derived meshes are saved through Unity serialization APIs.

Reviewed actual Unity renders from front, three-quarter, side and rear, plus
normal diagnostics and a higher-shine comparison. Iteration addressed root
orientation seams, insufficient curl tessellation, sharp centerline transitions,
atlas alpha coverage, unsupported disconnected growth islands, and saved-mesh
buffer refresh in the sample-building harness. Saved vertex positions are checked
against regenerated output; Project-browser discovery and direct loading are both
checked, not just file existence.

Final full-detail geometry: **1,209 cards, 200,988 vertices, 198,570 triangles**.
Zero degenerate triangles, zero detected card-frame flips, and no generation
warnings/rejected guide influences. The 96-sample cap with adaptive 18-degree /
9-mm segment targets is a close-up setting, not a mobile budget. LOD1 and LOD2
reduce density to 65% / 35% and cap samples at 36 / 20.

The material has primary/secondary shine of 0.10 / 0.035, with roughness 0.60 /
0.80. Colors remain independently editable. A two-sided single pass avoids
duplicate backface geometry. The owned atlas copies have alpha-preserving mipmaps;
shared source texture import settings were not changed.

## Test coverage

Validation uses Unity 6000.3.18f1 with graphics enabled in an isolated project,
compiling the actual Hair Cards sources. Coverage includes anchors and frozen
points, arc-length preservation, distance-based turns, deterministic evaluation,
LOD independence of procedural shape, finite geometry/normals, phase-frame
continuity, serialization/duplication, preset preservation, finish Undo, high-
resolution vertex RGBA, shader coverage rendering, coordinate-system regression,
and optional example discovery/resource/geometry consistency.

Final release gate: **502 passed, 0 failed, 0 skipped**, including source import /
compilation, packaged example discovery and regenerated-versus-saved Curly geometry.
Main-project metadata preflight also passed (70 C# source files). The user's open
Editor was not closed or used as the validation instance.

Local result: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/321747344be449d68371e55f6365d0a9/editmode.xml`.
Visual build log: `tmp/HairCardsSourceValidation/Logs/curly-final-assets-803b773e713e478dad58d83fee354703/unity.log`.
All **35 files** under PointySwept matched their pre-task SHA-256 hashes.

## Performance and limits

On this development machine, six warmed full-detail evaluate + mesh updates took
approximately **440–459 ms** in the isolated Editor. Evaluation was 193–205 ms;
mesh construction was 245–263 ms. These are CPU editor rebuild measurements, not
GPU frame times, and include a higher-detail hero mesh than the reference. The
initial evaluation alone took about 632 ms. Reusable evaluation/mesh workspaces
were used. Existing debounced end-of-stroke preview updates remain in effect.
Use Draft/reduced LODs while shaping; this is not a 60-Hz full-groom simulation.

This is an editable reconstruction of the reference's shape and curl vocabulary,
not a pixel-identical shader/lighting match or a certification of “AAA” quality.
Fine alpha strands and card shadows still need review in the target lighting,
resolution, antialiasing, motion and hardware. Dithered coverage has been tested
for clipping, but temporal-AA quality and cross-platform GPU performance have not
been certified. Tight curves can self-intersect; ringlets are a procedural shape
operation, not a strand-to-strand collision simulation.

The included body is an unrigged reference. Binding real race slots, copying skin
weights, creating the slot-bound scalp Mesh Modifier and checking animated
deformation remain production-specific steps. The supplied scene does not install
or switch your project's render pipeline. PointySwept is preserved separately.
