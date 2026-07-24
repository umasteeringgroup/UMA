# UMA Incremental Mesh Combiner Baseline

Status: Phase 1 capture procedure  
Last updated: 2026-07-24

## Purpose

This baseline records the output and performance of UMA's existing mesh
combiners before the incremental mesh combiner changes the generator workflow.

Use the same fixtures and capture procedure after each implementation phase.
A performance improvement is acceptable only when generated mesh output remains
equivalent.

## Automated Output Fixtures

Run the UMA EditMode tests from:

- **UMA > Testing > Run UMA Editor Tests**, or
- Unity's Test Runner using the `IncrementalMeshBaseline` category.

The phase-one fixtures compare the managed combine result used by
`UMADefaultMeshCombiner` with the current MeshData/jobified result.

The initial fixtures cover:

- Positions, normals, tangents, colors, and UV0 through UV3.
- Bone weights and bind poses.
- Multiple submeshes.
- Multiple blendshapes and blendshape frames.
- Blendshape vertex, normal, and tangent deltas.
- Multiple slots.
- Mesh-hide triangle masks.
- 16-bit and 32-bit output index formats.

The tests produce snapshot hashes in the Test Runner output. The semantic
array-by-array comparison is authoritative; hashes are diagnostic identifiers
and are not expected to be identical when snapshot labels differ.

The existing jobified tests separately lock down its generated fallback tangent
for a source with missing tangent data. That fallback differs from the managed
combiner's historical zero fill, so it is recorded as an existing behavioral
difference rather than treated as output parity.

Additional fixtures will be added as later phases implement atlas remapping,
renderer staging, modifiers, cloth, and full generator integration.

## Crowd Performance Capture

For the blendshape stress baseline, use the **Human Female 3.0** race. It has
the largest blendshape set in the supplied UMA races.

On every Dynamic Character Avatar used by this baseline:

- Set **Race** to **Human Female 3.0**.
- Enable **Load BlendShapes**.
- Enable **Load All Frames** when measuring the worst-case frame-loading path.
- Keep blendshape normals and tangents enabled when testing full feature parity.

Open:

**UMA > Testing > Generation Baseline...**

Then:

1. Open the crowd scene.
2. Enter Play Mode.
3. Assign the active UMA Generator if it was not found automatically.
4. Set **Expected UMA Count** to `96`.
5. Verify the window reports that every **Human Female 3.0** DCA loads
   blendshapes.
6. Press **Start Capture** immediately before triggering crowd generation.
7. Generate the crowd using the normal scene workflow.
8. Let the capture stop after the generator queue drains, or press
   **Stop and Save** after the final character is complete.

Reports are written to the project's `ProfilerCaptures` directory as formatted
JSON. Use **Save Report As...** to place another copy elsewhere.

## Recorded Configuration

Each report identifies:

- Unity version and active scene.
- CPU, memory, GPU, and graphics API.
- Generator and mesh-combiner types.
- Atlas resolution.
- Generator iteration count and inter-frame delay.
- Process-all-pending state.
- Render-texture conversion and mipmap settings.
- Maximum queued texture conversions per frame.
- Generated-mesh readability and renderer-regeneration settings.

Do not compare two captures unless these values and the crowd content are
equivalent.

## Recorded Performance

Each report includes:

- Total capture duration.
- Sampled frame count.
- Mean, median, 95th percentile, 99th percentile, and maximum main-thread frame
  time.
- Peak generator queue depth.
- Start, end, and peak allocated memory.
- Peak reserved, Mono, managed, and graphics-driver memory.
- Existing generator validation, preprocessing, texture, mesh, skeleton,
  blendshape, and event timing-counter deltas.
- Incremental budget overruns, worker waits, restarts, cancellations, failures,
  successful-operation latency, and prepared/applied blendshape-frame counts.
- Average texture-processing, mesh-update, and skeleton-update time.
- Raw frame-time samples for later comparison.
- Human Female 3.0 DCA count and Load BlendShapes validation.
- Total blendshape and blendshape-frame counts found on the generated meshes.

The window prefers Unity's **Main Thread** profiler counter. If that recorder is
unavailable, it logs a warning and uses unscaled frame time.

## Repeatability Rules

- Use the same scene and avatar count.
- Use Human Female 3.0 with Load BlendShapes enabled for blendshape stress
  comparisons.
- Use the same character recipes or random seed.
- Use the same generator prefab and settings.
- Use the same mesh combiner.
- Disable unrelated profiling tools and editor windows.
- Use the same build target and graphics API.
- Capture from a clean Play Mode entry.
- Allow shader compilation and asset import to finish before recording.
- Run at least three captures and retain the median run.
- Record Editor and Player results separately.

## Phase 1 Acceptance

Phase 1 is complete when:

- The `IncrementalMeshBaseline` EditMode tests pass.
- A 96-avatar report can be captured without changing generator behavior.
- The report identifies generator settings and contains frame-time, memory, and
  generator-stage metrics.
- The captured JSON can be retained and compared with later implementation
  phases.

## Current Automated Baseline

On Unity `6000.3.18f1`, the focused `IncrementalMeshBaseline` run currently
passes both phase-one fixtures.

The broader UMA category currently reports 27 passing tests and 5 failures
outside the new baseline fixture:

- Three existing jobified/bone tests emit or encounter current skeleton/reflection
  errors.
- Two indexed races fail the existing race smoke-test requirements.

These results are recorded as the current repository baseline. Phase 1 does not
change the generator or existing mesh combiners to address unrelated failures.

The 96-avatar performance numbers remain scene- and hardware-specific. Capture
and retain those JSON reports from the actual crowd scene rather than committing
values recorded from a different scene or machine.
