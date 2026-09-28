# Natural Short Hair Part validation

## Scope

Two additional examples: `ShortHairPart_Natural_HairGroom` and
`ShortHairPart_FrontFlipNatural_HairGroom`. Existing Classic / Relaxed / Close Cut
remain available; the previously validated scalp-clearance update was installed
after Unity closed. Other hairstyles were not regenerated or overwritten.

- Opt-in strand-aligned 2D Noise: rotation-minimizing frame, independent transverse
  amplitudes, physical noise scale, deterministic clump/individual blending,
  smooth root fade. Original Noise behavior is unchanged by default.
- Surface Bend: source-local segment rotation with a separate bend-angle profile.
  Normal-based frame rather than a fixed world direction. Root anchoring, freeze,
  stiffness and original segment lengths remain authoritative.
- Frame scratch arrays are reused. Capturing lambdas were removed from shared
  modifier dispatch and mask lookup; otherwise even unrelated modifiers incurred
  per-strand allocations. Width/Twist behavior remains unchanged.
- Existing node/property UI, Active toggle, masks, serialization and Undo are used.

## Samples and visual review

Actual URP renders, not generated illustrations, were reviewed from front, both
sides, rear and three-quarter views, with a Classic comparison under identical
lighting. A second art pass strengthened the visible waves, increased crown/side
length and softened both highlight lobes. Final outputs include both editable
grooms, independent materials/profiles, three-LOD prefabs, preview images and a
dedicated comparison scene. The forelock has a repaintable texture mask; sparse
tiles are limited to growth-bearing faces rather than the entire front of the head.

Each new style has 1,532 cards at LOD0: **19,384 unique triangles**.
LOD1: **9,950**. LOD2: **3,144**. No duplicate backfaces, zero detected frame flips,
zero degenerate triangles. Hybrid adds a second color draw of those same faces.

The crown requests 900 cards / 9 profile points, the side 650 / 5. Both use 24
construction points independently of rendered tessellation. Final card clearance
is 0.6 mm with zero root embed. The source, growth boundaries and 39 authored guides
are retained. Crown/side length multipliers are 1.28 / 1.22, with an additional
masked 1.3 at the front in the Flip variant.

## Regression gate

The actual Core/Runtime/Editor sources are imported and compiled in the isolated
Unity **6000.3.18f1** validation project, not substituted with prebuilt Hair Cards
assemblies. Tests cover deterministic regeneration, segment lengths, fixed roots,
interior frozen points, stiffness, root fade, source rotation/translation, independent
noise axes, shared clump seeds, LOD-independent shape, copying/serialization/Undo,
property-panel drawing for selected tree nodes, and warm allocation bounds.

All five ShortHairPart samples are rebuilt at all three LODs; saved vertices and
indices must match evaluation. Dense barycentric samples of card faces must remain
outside the source within 0.1 mm, and every sample remains under its LOD0 budget.
The rest of the hair regression suite checks the existing styles/features.

Final release gate: **597 passed, 0 failed, 0 skipped**, with source compilation.
Results: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/280bf6412b6f4a9283b5835d765f2f96/editmode.xml`.
The allocation regression now passes its fewer-than-100 warm allocation-events
bound across 100 guides / 400 children with both new deformation modes enabled.
Two earlier freeze/falloff assertions were corrected to test falloff on a free
chain: a pinned interior point can legitimately prevent all motion in its prefix.
The separate frozen-point assertions remain in place.

## Measured authoring costs

Final warmed, pooled runs on this workstation (8 refreshes after warmup):

| Variant | Evaluation | Cached mesh update | Full cached preview |
|---|---:|---:|---:|
| Natural | 298 ms | 37 ms | 332 ms |
| Front Flip Natural | 310 ms | 37 ms | 348 ms |

With the new Noise/Bend modifiers bypassed, evaluation was about 127–128 ms;
their full stack therefore adds approximately 171–182 ms. The frame math removes
unnecessary native quaternion/roll calls, and the allocation regression guards
against per-card scratch/closure objects. Profiler markers are
`UMA.HairCards.StrandNoise2D` and `UMA.HairCards.SurfaceBend`.

Cold LOD0 evaluation plus mesh generation/clearance took about 2.9–3.0 seconds.
An unchanged cached preview is **not** a cold rebuild or a guarantee of 60 Hz
grooming. A topology/global-shape change can invalidate clearance for many cards.
No new jobs/threading system was introduced. Timings:
`tmp/HairCardsSourceValidation/Logs/natural-profile-52caa6d3f5f146f3a3ddd636d9058629.log`.

The two sample sets were copied with their Unity-generated metadata, retaining all
GUID links. The installer refuses a locked main project or a conflicting existing
file and byte-verifies the copy. Human-readable resource labels were normalized
after the gate; geometry and modifier settings were not changed by that naming pass.

## Limits

This is an authoring workflow, not runtime simulated hair or an automatic match
to a reference photograph. Segment constraints can reduce requested noise/lift.
Non-uniform object scale changes the displayed angles of source-local shapes.
Full clearance recomputation remains much slower than an unchanged cached mesh
refresh; bake once for runtime use. Check extreme edits, animation, low LODs,
hybrid transparency sorting and your game's lighting separately.
