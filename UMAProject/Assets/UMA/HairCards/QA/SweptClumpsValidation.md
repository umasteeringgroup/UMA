# Swept-clump implementation review — 2026-09-13

## Delivered workflow

The node tree remains the single navigator. Authored sculpt passes feed optional
surface-rooted clump populations, then independently rooted final cards. Each
population has its own ordered modifiers, Active toggle, properties, and temporary
Inspect Population / Show Mask / Final Hair controls. Existing sculpt tools,
children-per-guide generation, UV editing, vertex RGBA, two material passes,
skinning and baking remain available.

The swept preset, root-to-tip guide interchange, shader, scalp MeshModifier,
editable example and [hairstyle guide](../SweptClumpsGuide.md) are included.
No third-party hair-generation implementation was imported or executed, and the
source authoring file was not saved. The original diffuse atlas/import settings were preserved;
the example uses a private image copy with mipmaps, retaining the same UV layout.

## Validation performed

Implementation release gate: **461 passed, 0 failed, 0 skipped**, including
clean source import and compilation. Results:
`Logs/HairCardsReleaseGate/428fb5e1aa8d4158a45b8bd43254a7c8/editmode.xml`.

- Actual Unity 6.3.18f1 project source import and compilation, not substituted DLLs.
- Full Hair Cards EditMode suite, including shader-pass compilation, an actual
  alpha-render/readback check with and without alpha-to-coverage, length/root
  invariants, generated-root pose transforms, mask gating, caching, Undo,
  persistence, node operations and upstream-edit isolation.
- Native UMA slot round-trip of UV0, root-to-tip/strand data, parent-clump/mask
  data and vertex RGBA. Real UMA MeshModifier application changes scalp colors
  without modifying geometry or the original color array.
- The copied example was loaded and evaluated in the actual UMA project:
  **51 authored guides → 402 clumps → 4,122 cards; 73,449 vertices / 81,419 triangles**.
  Native mesh, profile, atlas, depth map, scalp modifier and scene material references
  were checked through Unity APIs.
- The included exporter transferred all 51 raw guides and 12,527 source vertices
  with automatic script execution disabled in the source application.
- Unity URP front/side/rear renders and interactive window inspection. Inspected
  clump colors, modifier properties, mask display and return to authored sculpting.
  Removed duplicate explanatory text from compact tree layouts; count entry now
  waits for Enter rather than rebuilding while dragging a 20,000-item slider.
- Fixed two integration defects found during review: external JSON rectangles
  must use explicit x/y/width/height fields, and ribbon triangle winding must agree
  with analytic outward normals. Both have regression tests.
- Actual-project testing exposed Gamma-only color thresholds, exact floating-point
  color comparisons, and a SceneView initialization race in older tests. Tests now
  retain their functional assertions across the project's Linear color space and
  existing window layout.
- The full log gate caught malformed example-folder/README GUIDs even though all
  461 tests passed. Corrected those sidecars and extended metadata preflight to
  include the packaged examples, missing sidecars and cross-project GUID collisions.

### Follow-up: Project browser folder visibility

After correcting the folder GUIDs, the existing Unity source database retained
stale folder associations. Fifteen example files loaded directly and retained
valid GUID mappings, but did not appear in `FindAssets` or the Project browser.
Reimporting each file did not repair this state. Moving the affected files through
a temporary folder and back using `AssetDatabase.MoveAsset` rebuilt the associations.
All **17 files** then appeared in folder queries, with every original asset GUID
and source-file SHA-256 unchanged. The empty temporary folder was removed.
Unity emitted native `newChildren.size() == childrenArray.size()` assertions while
updating the inconsistent tree during these moves; this was not a clean native
operation despite the successful discovery/byte-preservation checks. The temporary
startup repair script was removed. A fresh editor-session check is still required
to confirm that the repaired tree is stable after restarting Unity.

Added `PackagedHairExamplesAreDiscoverableAndLoadableWhenIncluded` to compare every
included example folder's on-disk files with its AssetDatabase search results,
direct loading, GUID round-trips and visibility flags. Source-only distributions
without the optional examples do not require their presence. This guards the
specific gap in the original validation: successful direct loading alone does not
prove that an asset is discoverable in the Project browser.

## Follow-up: Validate & Bake reference types — 2026-09-14

The bake settings deliberately store `UnityEngine.Object` to keep Core independent
of UMA assemblies. The editor incorrectly assumed the ObjectField type filter
also guaranteed the type of an existing saved assignment. An incompatible value
therefore threw `InvalidCastException` while drawing Validate & Bake.

The regression UI test reproduced the reported stack at workspace line 1083 before
the fix. All three fields now preserve the raw reference, show a type-specific
warning and allow explicit replacement/clearing. Validation blocks incompatible
references consumed by the selected output and only warns for unused assignments.
Issue navigation opens the Output node. Repainting must not clear or dirty settings.

Post-fix compilation and **467/467 tests passed, zero skips**, in the isolated
Unity 6.3 validation project using the current Hair Cards sources; the user's open
project was not closed. Results:
`tmp/HairCardsSourceValidation/Logs/bake-reference-green-032156f8575144628776aae2fa331b4f/tests.xml`.
The pre-fix reproducer is in the adjacent `bake-reference-red-da12fec77c5b42349688ad898dfaeb82` run.

## Follow-up: separate character binding — 2026-09-14

Added **Source & Setup → Bind Character / Race**. The weighted donor is saved
separately from the original authoring surface. Attaching preserves the source
mesh, painted maps, guides, sculpt passes and hair materials. It sets the selected
race as the bake's compatible race and clears old scalp modifier assignments so
they can be recreated against the real body slots. The confirmation explains
these changes; Undo and detaching retain the saved assets for recovery.

The workflow includes selectable body slots, coordinate presets, an alignment
preview and a distance gate. Saved donor geometry and bone data support slot
visibility, Head/Neck focus, barycentric root-based skinning and slot/LOD baking
without a scene character. Reverse surface correspondence transfers scalp shading
to actual slot-local vertex indices. Changed source geometry or donor slot geometry,
weights or bind poses invalidate the binding rather than silently reusing it.

Current sources compiled and **481/481 tests passed, zero skips**, in the isolated
Unity 6.3 validation project:
`tmp/HairCardsSourceValidation/Logs/binding-final-44aa51d1b26545eab6c5c6059a12331d/tests.xml`.
After the final UI and validation refinements, compilation and the **14/14 focused
binding tests passed, zero skips**:
`tmp/HairCardsSourceValidation/Logs/binding-final-focused-0e891b8b50bc4d73b19d1951f4534312/tests.xml`.
Coverage includes mismatched topology, variable bone influences, root anchoring,
coordinate transforms, scalp color correspondence, asset reload, Undo/Redo,
stage visibility/focus, and skinned LOD0/LOD1 plus a valid native UMA slot bake
with no scene character.

The integration fixture used the original PointySwept groom and five native
UMA30 body slots: **13,871 donor vertices, 229 bones, 4,122 cards**. The maximum
paint/guide-root distance was **0.001018595 m** using **Z Up To Y Up Flip Forward**.
All cards received root-based weights. Native slot assets were copied byte-for-byte
into the isolated project, never decoded or rewritten as text. This fixture loads
the real slots directly; it does not exercise a fully indexed race recipe or a
live, DNA-shaped character. The main project and its PointySwept assets were not
automatically rebound. A live animation and artistic alignment review remains
necessary for the character the user chooses.

The metadata/documentation preflight also passed for 67 scripts. This is distinct
from the compilation and test evidence above.

## Profiling

Measured in the Unity Editor on this workstation, D3D11 / RTX 5070 Ti. These are
editor CPU measurements, not target-device frame-rate guarantees.

| Operation | Measured result |
|---|---:|
| Cold full evaluation, uncached roots | about 1.29 seconds |
| Warm full evaluation | 151–169 ms |
| Warm mesh update, reusable mesh/buffers | 83–90 ms |
| Repeated complete warm evaluation + mesh update | 232–239 ms; median about 234 ms |
| Whole editor-frame managed allocation | about 245–307 KB |

Allocation values are from a valid **GC.Alloc ProfilerRecorder**, current thread,
summed per editor frame. They include editor/test-harness work and are not claimed
as generator-only allocations. `GC.GetAllocatedBytesForCurrentThread` returned
zero on this Unity Mono and was rejected as evidence.

Cached source fields, roots and compatible-neighbor weights survive guide combing.
Deleted population caches are pruned; missing explicitly assigned part maps stop
generation and report an actionable issue. Deformation still uses a fixed shape
lattice, with preview/LOD tessellation applied afterwards.

An additive alpha-layer diagnostic at 512², with the body depth-occluding hidden
hair, measured **8.18 surviving layers per covered pixel on average, maximum 52**.
Drawing a second pass measured **16.37 / 104**, with identical coverage. This is
potential hair-layer overdraw with hair depth writes disabled for counting, not a
GPU timestamp or the optimized opaque depth-rejection cost. Runtime GPU timings
still need a target-device player profile. The preset therefore uses one two-sided
alpha-clipped pass and no duplicate backface geometry.

## Remaining limits to review artistically

- This is an editable approximation of the reference's construction, not a
  pixel-identical material conversion or a claim of exceeding its quality.
- Full-quality regeneration is roughly a quarter second here, not a 30–60 Hz
  rebuild. Use Draft/Medium while working. These measurements identify interpolation,
  fine modifiers and meshing as the next jobs/vectorization candidates.
- One candidate influence is deliberately rejected in each reference population.
  Some generated side/nape samples can intersect the body (worst measured roughly
  13 mm); improve guide placement or use collision-aware shaping. Root frame
  transport is not a complete strand/body collision simulation.
- Hairline distance follows mesh edges; coarse source topology limits a very fine
  hairline mask. Adaptive sampling is a length/turning-angle budget heuristic, not
  a certified geometric-error bound.
- The packaged reference body remains unrigged and its original scalp slot binding
  is review-only. Use Bind Character / Race to attach a verified weighted donor
  and recreate the scalp modifier before production wardrobe output. Review the
  chosen character's alignment and deformation in animation.
- Windows were inspected at compact and wide sizes. Automated drag attempts did
  not verify a final docked layout; independent dockability is the existing Unity
  EditorWindow behavior, not a newly claimed custom docking implementation.

To repeat the full source gate with Unity closed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Assets/UMA/HairCards/QA/Run-HairCardsReleaseGate.ps1
```

Results are written to a new `Logs/HairCardsReleaseGate/<run-id>/` directory each
time. Metadata preflight alone is never treated as compilation or a test pass.
