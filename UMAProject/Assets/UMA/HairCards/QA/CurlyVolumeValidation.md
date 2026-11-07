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

Updated Balanced geometry: **1,209 cards, 83,660 vertices, 81,242 triangles**.
That is **59.1% fewer triangles** than the initial 198,570-triangle sample, with
the same card population and procedural curl settings. The baked reference has
76,360 triangles / 1,133 cards; the remaining difference is principally our 6.7%
larger card population, not excessive rows per card.

Zero degenerate triangles, zero detected card-frame flips, zero unmet sampling
targets at Full, and no generation warnings/rejected guide influences. Balanced
uses a 2-mm ribbon shape-error target, 18-degree shading-frame error and 63-segment
per-card ceiling. The existing 96-point profile is an upper limit, not a count to
fill. LOD1 and LOD2 reduce density to 65% / 35% and cap samples at 36 / 20; tighter
LOD caps can intentionally produce sampling-limit advisories.

The mesh reducer measures centerline, left/right edges, camber and shading-frame
interpolation error. It selects nonuniform rows, retaining reference frames rather
than recalculating them from coarse chords. Unstable reference bends are refined
selectively. Arc-length UVs and width evaluation use the retained rows' actual
positions along the curve. Vertex RGBA root holds remain segment-based, followed
by a distance-based fade. Ordinary profiles and PointySwept do not use this path.

Ringlets exposes Economy/Balanced/Close-up presets, error controls, segment caps,
last-built polygon counts and cap warnings. The profile explains when Ringlets
owns sampling. Advanced construction resolution is separated from mesh detail.

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

Reduction regression run: **507 passed, 0 failed, 0 skipped**, including source
import/compilation, shape invariance, value snapshots and pooled-curve reset,
LOD caps and advisories, distance-based UV/vertex RGBA, twisting ribbon edges,
serialized quality settings, packaged discovery and saved-versus-generated mesh
consistency. The sample also asserts an 85,000-triangle ceiling and zero flips /
unmet Full sampling targets. Main-project metadata preflight passed (72 C# files).
The user's open Editor was not closed or used as the validation instance.

Regression result: `tmp/HairCardsSourceValidation/Logs/curly-reduction-tests-2d90a03ce543445bb40a49c922f9ec2a/tests.xml`.
Visual build log: `tmp/HairCardsSourceValidation/Logs/curly-reduction-final-ba8cedf2a55e4baab435251896579b2a/unity.log`.
Final packaging release gate: **507 passed, 0 failed, 0 skipped**.
Result: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/67837cca452543849bb194993fc9d5d0/editmode.xml`.
All 35 PointySwept resource/metadata files retain their pre-change SHA-256 hashes.

## Performance and limits

On this development machine, six warmed full-detail evaluate + mesh updates took
approximately **527–536 ms** in the isolated Editor. Evaluation was 194–202 ms;
mesh construction including reduction was 327–338 ms. Initial evaluation alone
took about 645 ms. These are CPU Editor rebuild measurements, not GPU frame times.
Reduction adds CPU fitting work compared with the original 440–459-ms uniform
build; it reduces stored/rendered geometry, not every rebuild's CPU cost. An
initial implementation took about 0.9 seconds; using the existing detailed curve,
selective frame refinement and reused buffers reduced that overhead. The fitting
marker is `HairCards.ReduceRibbon`.

Reusable evaluation/mesh workspaces and existing debounced end-of-stroke updates
remain in effect. Use Draft/reduced LODs while shaping; this is not a 60-Hz
full-groom simulation. The saved card mesh fell from about 36.9 MB to 15.3 MB in
the project's current Unity serialization format.

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
