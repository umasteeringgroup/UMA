# UMA Mesh Modifier Cloth Brush Implementation Plan

Status: Proposed implementation plan  
Last updated: 2026-07-20

## Objective

Add a topology-preserving Cloth brush to the Mesh Modifier sculpt workflow for shaping UMA clothing directly in the Unity Scene view.

The brush should produce believable folds and soft deformations while preserving UMA's existing authoring contract:

- Editing affects the generated preview mesh only until the user saves.
- Saving produces ordinary vertex deltas in a `MeshModifier`, or copies the result into a `SlotDataAsset` through the existing save commands.
- No cloth simulation state is required at runtime.
- Vertex count, triangle topology, UVs, skin weights, materials, and blendshape topology remain unchanged.
- Existing masks, falloff, X symmetry, Connected Only, All Slots seam welding, Undo, normals, bounds, and collider refresh behavior remain available.

The initial release should focus on local, controllable authoring rather than reproducing every feature of Blender's cloth simulator.

## Proposed Brush Modes

Add `Cloth` to `SculptTool` and expose a `SculptClothMode` selector.

### Initial modes

1. **Grab Cloth**
   - Pulls the simulated patch with the pointer.
   - Neighboring vertices follow through structural and bending constraints.
   - Primary use cases: loose sleeves, collars, hoods, skirts, capes, and baggy fabric.

2. **Pinch Point Cloth**
   - Pulls vertices radially toward the brush center.
   - Produces folds converging on one point.
   - Primary use cases: gathered fabric, buttons, ties, knots, and cinched areas.

3. **Pinch Folds Cloth**
   - Pinches from the two sides perpendicular to the stroke direction.
   - Produces parallel folds following the stroke.
   - Primary use cases: sleeve wrinkles, waist folds, trouser folds, and skirt pleats.

4. **Inflate/Deflate Cloth**
   - Applies pressure along the local surface normals.
   - Positive pressure creates loft; negative pressure collapses the patch.
   - Primary use cases: padded clothing, puffed sleeves, and spacing clothing away from the body.

5. **Expand/Contract Cloth**
   - Changes the target area of the simulated patch in the tangent plane.
   - Produces compression folds when contracting and softer spreading when expanding.

### Deferred cloth operations

Keep these out of the first release unless the initial solver makes them inexpensive:

- Whole-slot gravity/filter simulation.
- Boundary-only cloth bend and twist; the existing Boundary brush already covers direct versions.
- Cloth pose chains.
- Persistent runtime cloth components.
- Dynamic remeshing, tearing, sewing, or topology changes.
- Full self-collision.
- GPU simulation.

## User Experience

### Stroke lifecycle

Cloth should use the anchored-drag lifecycle already shared by Grab, Boundary, and Elastic Deform:

1. The user presses on an editable slot.
2. UMA locks the target slot, brush origin, surface frame, affected particle set, and collision inputs.
3. The pointer movement updates the brush force or target.
4. The local solver advances using fixed simulation steps while the pointer is held.
5. Releasing the pointer finalizes one Unity Undo operation, recalculates normals and bounds, refreshes picking data, and discards transient solver state.

This keeps Cloth compatible with the current Scene interaction model and leaves the non-cloth stroke paths unchanged.

### Proposed controls

Show these controls when `SculptTool.Cloth` is active:

- **Cloth Mode**: Grab, Pinch Point, Pinch Folds, Inflate, Expand/Contract.
- **Radius**: world-space simulated patch radius.
- **Effect %**: maximum brush force or target displacement.
- **Falloff**: scales brush force and transition pinning.
- **Stiffness**: resistance to edge stretching.
- **Bending**: resistance to folding between adjacent triangles.
- **Damping**: removes oscillation between solver steps.
- **Gravity**: optional local gravity contribution; default zero for predictable editing.
- **Collision**: Off, Body Only, or Visible Slots.
- **Collision Offset**: minimum cloth clearance from collision surfaces.
- **Pin Open Boundary**: prevents hems and cuffs from drifting unless directly forced.
- **Quality**: Preview, Balanced, or High; maps to substeps and solver iterations.
- **Settle**: runs a short simulation without adding pointer force.
- **Reset Stroke**: restores the mesh to the start of the current cloth stroke without leaving Sculpt mode.

Continue using the existing controls for masks, X symmetry, Connected Only, normal updates, and save targets.

Advanced numerical settings should be hidden behind an **Advanced** foldout. Most users should get stable results from the presets.

## Simulation Model

### Solver choice

Use a small, editor-only position-based dynamics solver, preferably XPBD for stiffness that remains reasonably consistent across timestep and iteration-count changes.

Reasons:

- The output is vertex positions, which matches the existing sculpt pipeline.
- Constraints are stable enough for interactive authoring.
- Local patches can be solved without simulating the complete character.
- Pinning, collisions, and brush targets map naturally to positional constraints.
- The solver can start as managed C# and move to Jobs/Burst later only if profiling justifies it.

Do not use Unity's runtime `Cloth` component. It introduces component lifecycle, renderer, coefficient, and runtime-state concerns that are unnecessary for an editor brush and difficult to isolate to a temporary local patch.

### Particle representation

Create an editor-only `SculptClothSolver` with reusable arrays or lists for:

- Current position.
- Previous position.
- Predicted position.
- Original stroke-start position.
- Velocity or Verlet history.
- Inverse mass.
- Brush influence.
- Pin weight.
- Active-slot local vertex index.
- Baked-mesh vertex index.
- Logical welded-particle index.

Coincident vertices created by UV, normal, or material splits should map to one logical particle. Scatter the solved logical position back to every coincident mesh vertex after each displayed update.

### Local patch selection

At mouse-down:

1. Find the hit vertex and connected component.
2. Traverse the slot adjacency graph using edge-length geodesic distance.
3. Include vertices within `Radius` plus a small transition band.
4. Mark vertices in the transition band as progressively pinned using the selected falloff.
5. Respect `Connected Only` during traversal.
6. Build constraints only for edges and adjacent triangles touching the active patch.

Geodesic selection prevents a brush from jumping across nearby but disconnected clothing layers. The transition band prevents a hard ring where simulated vertices meet untouched vertices.

### Constraints

Implement constraints in this order:

1. **Pin constraints**
   - Fully masked vertices have zero inverse mass.
   - Partially masked vertices receive proportionally stronger positional pinning.
   - Transition-band vertices are pinned progressively toward their stroke-start positions.
   - Optional open-boundary pinning adds another positional weight.

2. **Structural edge constraints**
   - Preserve stroke-start edge lengths.
   - Derive one constraint per unique topology edge.
   - Compliance is controlled by Stiffness.

3. **Bending constraints**
   - Prefer dihedral-angle constraints for pairs of triangles sharing an edge.
   - If the first prototype needs a simpler implementation, use distance constraints between opposite triangle vertices, then replace them after behavior is validated.
   - Compliance is controlled by Bending.

4. **Optional area constraints**
   - Preserve triangle area to reduce excessive local collapse.
   - Make this conditional if it adds noticeable cost or makes pinching too rigid.

5. **Brush constraints/forces**
   - Apply after prediction and before final collision projection.
   - Derive force strength from Effect %, brush falloff, mask, and pointer displacement.

6. **Collision constraints**
   - Project particles outside collision surfaces by Collision Offset.
   - Apply after structural and bending corrections during every solver iteration.

### Integration loop

Use a fixed simulation timestep independent of editor frame rate:

1. Accumulate elapsed editor time, clamped to avoid a large jump after a pause.
2. Run zero or more fixed substeps, with a hard maximum per Scene repaint.
3. Predict particle positions from velocity, damping, and optional gravity.
4. Apply the current brush target/force.
5. Iterate pin, structural, bend, area, symmetry, seam, and collision constraints.
6. Update velocity from corrected positions.
7. Scatter positions to the baked preview mesh.

Starting presets:

| Quality | Fixed step | Substeps per update | Constraint iterations |
|---|---:|---:|---:|
| Preview | 1/60 s | 1 | 3 |
| Balanced | 1/60 s | 2 | 6 |
| High | 1/90 s | 3 | 10 |

These are initial tuning values, not API guarantees. Profile representative UMA clothing before locking them down.

## Brush Force Definitions

### Grab Cloth

- Convert pointer movement through the existing camera-facing drag plane.
- Create a soft positional target at the brush center.
- Scale the target weight by radial falloff and mask.
- Let structural and bending constraints propagate the movement.
- Do not teleport all influenced particles directly to their final offset.

### Pinch Point Cloth

- Project the vector from each affected particle to the brush center onto the local tangent plane.
- Apply inward force along that direction.
- Optionally add a small normal displacement controlled by a future Depth parameter.
- Keep the center numerically stable when the tangent vector approaches zero.

### Pinch Folds Cloth

- Capture the surface tangent from the stroke direction.
- Compute the perpendicular tangent direction using the surface normal.
- Pull particles from both perpendicular sides toward the stroke centerline.
- Apply little or no force along the stroke direction.
- Stabilize the captured tangent during a stroke so folds do not flip on uneven triangles.

### Inflate/Deflate Cloth

- Apply signed pressure along averaged local normals.
- Recalculate or smoothly update normals between displayed solver updates.
- Preserve the stroke-start normal as a fallback when the live normal becomes degenerate.

### Expand/Contract Cloth

- Apply signed tangent-plane radial force around the brush center.
- Expansion moves particles outward; contraction moves them inward.
- Structural and area constraints convert contraction into folds instead of unrestricted collapse.

## Collision Design

### First-release collision scope

Support collisions against:

- The generated body/race slots.
- Optionally all other visible non-target slots.
- A configurable surface offset.

Do not include cloth self-collision in the first release. Clearly label this limitation in the UI and documentation.

### Collision data

Do not repeatedly recook the combined preview `MeshCollider` inside solver iterations. The collider contains both the target cloth and other slots, making it unsuitable for direct target-excluding particle projection.

Create a stroke-local `SculptClothCollisionSurface`:

- Collect triangles from the selected collision scope, excluding the active target slot.
- Store triangle positions and normals in the baked mesh's local space.
- Build a reusable 3D spatial hash or bounding-volume hierarchy at mouse-down.
- Query nearby triangles for each predicted cloth particle.
- Find the closest point on candidate triangles.
- Resolve only penetrations within Collision Offset.
- Use two-sided triangle distance, then choose the correction direction using the particle's stroke-start side when orientation is ambiguous.

Rebuild collision data only when the target slot changes, visible slots change, the preview mesh rebuilds, or a new stroke requires current geometry.

### Collision safeguards

- Clamp maximum correction per iteration.
- Reject non-finite triangle or particle data.
- Handle zero-area triangles without throwing.
- Avoid tunneling by limiting per-substep brush target movement.
- If collision generation fails, disable collision for the stroke and show a non-blocking warning rather than losing the sculpt session.

## Masks and Pinning

Reuse the existing sculpt mask as the primary cloth pin map:

- Mask `1`: fully pinned.
- Mask `0`: free, subject to patch-boundary pinning.
- Intermediate values: partial positional compliance and reduced brush force.

Do not store pinning or masks in the saved `MeshModifier`; they remain authoring aids.

Add optional **Pin Open Boundary** behavior:

- Detect per-slot topology boundaries using the existing edge-use-count logic.
- Exclude boundaries welded to another visible slot in All Slots mode.
- Pin only boundary particles inside the simulated patch.
- Allow the direct brush force to override some pinning when the pointer begins on the boundary, otherwise cuffs and hems would be impossible to move.

## Symmetry

Independent left and right cloth simulations can diverge through floating-point and collision differences. Implement explicit symmetric pairing for Cloth instead of relying only on mirrored force weights.

At stroke initialization:

1. Build or reuse a local-X mirror map from stroke-start positions.
2. Pair particles whose reflected positions match within tolerance.
3. Treat centerline particles as self-paired.
4. Apply mirrored brush forces to paired particles.
5. After each solver iteration, average each pair with the reflection of its partner.
6. Force centerline particle X to zero within tolerance.

When no valid partner exists, leave that particle unpaired and show no error. This preserves support for intentionally asymmetric garments.

Collision mode should use symmetric collision inputs when X symmetry is enabled. If the collision geometry is asymmetric, document that exact symmetry may conflict with collision and let collision win in the final projection pass.

## Coincident Vertices and Cross-Slot Seams

### Coincident vertices

- Collapse coincident active-slot vertices into one logical particle.
- Merge their topology neighbors when constructing constraints.
- Use the strongest mask/pin weight among coincident entries.
- Scatter one solved position back to every coincident entry.

### All Slots seams

- Continue using the current cross-slot seam groups.
- When an active cloth particle belongs to a welded cross-slot seam, write its final position to every seam member.
- Mark every affected slot as changed so `Save MeshModifier` includes all required deltas.
- Do not simulate the complete neighboring slot in the first release; only synchronize the welded seam.
- Add a later option for a multi-slot cloth patch only after single-slot simulation is stable and tested.

## Code Structure

Keep numerical simulation separate from Scene GUI and asset saving.

### New files

Proposed files:

- `Assets/UMA/Core/Editor/StageUtils/SculptClothSolver.cs`
  - Editor-only solver and constraint data.
  - No dependencies on `SceneView`, GUI, or asset saving.
- `Assets/UMA/Core/Editor/StageUtils/SculptClothCollisionSurface.cs`
  - Triangle extraction, acceleration structure, closest-point queries, and projection.
- `Assets/UMA/Core/Editor/Tests/SculptClothSolverTests.cs`
  - Deterministic unit tests for the numerical core.
- `Assets/UMA/Core/Editor/Tests/SculptClothCollisionTests.cs`
  - Collision and degeneracy tests.

### Existing files

Update:

- `VertexEditorStage.cs`
  - Add enums and serialized controls.
  - Create and dispose stroke-local solver state.
  - Translate Scene input into brush targets.
  - Scatter results to `BakedMesh` and existing slot state.
  - Preserve Undo, masks, symmetry, seams, and save behavior.
- `MeshModifierSculpting.md`
  - Document Cloth modes, controls, workflows, limitations, and troubleshooting.
- `WelcomeToUMA.cs`
  - Replace the reserved-Cloth wording once the feature ships.

### API boundary

The solver should expose a small interface similar to:

```csharp
Initialize(SculptClothInput input);
SetBrushTarget(SculptClothBrushTarget target);
Step(float fixedDeltaTime, int iterations);
CopySolvedPositions(Vector3[] destination);
ResetToStrokeStart();
Dispose();
```

Use structs and reusable buffers where practical. Avoid exposing `VertexEditorStage` internals to the solver.

## Implementation Phases

### Phase 0: Fixtures and measurement

- Create representative test meshes: quad grid, cylinder sleeve, skirt strip, disconnected double layer, coincident split seam, and degenerate triangles.
- Record active vertex counts for representative UMA garments.
- Add lightweight editor profiling around patch construction, constraint construction, each solver step, collision queries, mesh upload, normal recalculation, and collider refresh.
- Define a reproducible Scene-view interaction test character and wardrobe set.

Exit criteria:

- Baseline sculpt frame cost is known.
- Test meshes cover open boundaries, folds, disconnected surfaces, and welded duplicates.

### Phase 1: Pure local cloth solver

- Implement logical particles and topology-edge extraction.
- Implement Verlet or velocity prediction with damping.
- Implement XPBD structural constraints.
- Implement pin and transition-band constraints.
- Add simple bending constraints.
- Implement Reset and deterministic fixed-step execution.
- Add pure unit tests before Scene integration.

Exit criteria:

- A pinned quad patch returns to stable rest without NaNs.
- Edge stretch remains within the target tolerance under a controlled pull.
- Results are repeatable for the same input and fixed timestep.

### Phase 2: Grab Cloth integration

- Add `SculptTool.Cloth` and `SculptClothMode.Grab`.
- Connect the solver to the anchored-drag lifecycle.
- Build patches by geodesic radius.
- Apply masks and transition pinning.
- Scatter coincident vertices and synchronize cross-slot seams.
- Support Undo, Reset Stroke, normal refresh, bounds, picking, modifier save, and slot save.

Exit criteria:

- Grab Cloth can form soft folds on a sleeve/skirt test asset.
- One gesture is one Undo operation.
- Saved MeshModifiers reproduce the preview deformation.

### Phase 3: Remaining force modes

- Add Pinch Point.
- Add Pinch Folds with a stabilized stroke tangent.
- Add Inflate/Deflate.
- Add Expand/Contract.
- Tune defaults and tooltips using the test garments.

Exit criteria:

- Each mode has a visually distinct, predictable deformation.
- Masks, Connected Only, and falloff affect every mode consistently.

### Phase 4: Collision

- Implement target-excluding collision triangle extraction.
- Add the acceleration structure.
- Add closest-point projection and collision offset.
- Add Body Only and Visible Slots scopes.
- Add failure warnings and degenerate-geometry safeguards.

Exit criteria:

- Cloth remains outside the body within the configured offset during typical pulls and settling.
- Target cloth does not collide with its own source triangles.
- Collision stays inside the interactive performance budget on representative garments.

### Phase 5: Symmetry, seam, and boundary hardening

- Add explicit cloth mirror pairing and per-iteration symmetry projection.
- Verify centerline behavior.
- Add Pin Open Boundary.
- Verify All Slots welded seam propagation and multi-slot modifier saving.
- Test asymmetric garments and incomplete mirror maps.

Exit criteria:

- Symmetric fixtures remain mirrored within tolerance.
- Internal welded slot seams do not separate.
- Exposed boundaries can be pinned or intentionally manipulated.

### Phase 6: UX, performance, and documentation

- Add quality presets and Advanced controls.
- Add Settle and Reset Stroke.
- Eliminate per-iteration managed allocations.
- Profile dense garments and establish warnings/caps for oversized patches.
- Update documentation and troubleshooting.
- Run the complete editor test suite and manual regression matrix.

Exit criteria:

- Balanced quality remains interactive on the agreed reference hardware and garment set.
- No existing sculpt brush behavior changes.
- User documentation matches the shipped controls and limitations.

## Test Plan

### Solver unit tests

- Pinned particles do not move.
- Fully free particles respond to gravity and brush force.
- Partial masks reduce displacement monotonically.
- Structural constraints limit edge stretch.
- Bending stiffness reduces fold angle changes.
- Damping reduces kinetic energy without introducing drift.
- Fixed input produces deterministic output within tolerance.
- Zero-length edges and degenerate triangles do not create NaN or infinity.
- Reset restores exact stroke-start positions.
- Every Cloth mode produces the expected signed movement.

### Collision tests

- A particle outside a plane remains unchanged.
- A penetrating particle is projected to Collision Offset.
- Two-sided triangles resolve using the stroke-start side.
- Degenerate triangles are ignored safely.
- Collision queries exclude active-slot triangles.
- Spatial acceleration returns the same closest surface as a brute-force fixture.

### Stage integration tests

- Mouse-down creates one solver and locks one target slot.
- Mouse-up disposes transient state and commits one Undo group.
- Interrupted or cancelled strokes restore a valid editor state.
- Masks pin the intended vertices.
- Connected Only does not include a nearby disconnected layer.
- Coincident split vertices remain welded.
- X symmetry preserves mirrored pairs.
- All Slots cross-slot seams remain welded.
- Normals, bounds, collider, and cached baked-mesh data refresh after completion.
- Save MeshModifier includes every changed slot and only nonzero deltas.
- Base-slot and new-slot save paths reproduce the solved preview.

### Manual matrix

Test at least:

- Low-, medium-, and high-density garments.
- Sleeves, trousers, skirt, cape/coat, collar, and layered pocket geometry.
- Open and closed meshes.
- Symmetric and asymmetric meshes.
- Original materials and pastel preview materials.
- Wireframe on and off.
- All falloff modes.
- Masks at 0, partial, and 1.
- X symmetry on and off.
- Collision Off, Body Only, and Visible Slots.
- Each quality preset.
- Undo/Redo across several cloth and non-cloth strokes.

## Performance Targets

Initial targets on the agreed reference editor machine:

- Patch and constraint construction: under 20 ms for 5,000 active logical particles.
- Balanced solver update: under 12 ms for 5,000 particles without collision.
- Balanced solver update with body collision: under 20 ms for 5,000 particles.
- No managed allocations inside the fixed-step iteration loop after initialization.
- Hard cap or confirmation dialog above a configurable active-particle threshold.
- Scene interaction should remain responsive even when the solver cannot consume every accumulated fixed step; drop excess accumulated time instead of spiraling.

Treat these as provisional until Phase 0 produces real garment measurements.

## Failure Handling

- Validate all particle positions after each substep.
- If non-finite data appears, stop the simulation, restore the stroke-start mesh, and show an actionable error.
- Clamp timestep, pointer-target movement, velocity, and collision correction.
- If topology changes during a stroke, cancel and rebuild the sculpt session.
- If collision acceleration data is unavailable, continue with collision disabled and warn once.
- Always clear progress UI, temporary meshes, native containers, and event hooks through `try/finally` or deterministic disposal.
- Ensure assembly reload, play-mode entry, stage closing, and compilation dispose active solver state safely.

## Risks and Mitigations

### Dense meshes may not remain interactive

Mitigation:

- Simulate only a geodesic local patch.
- Collapse coincident vertices.
- Reuse buffers and constraints.
- Provide quality presets and active-particle warnings.
- Move the pure solver to Jobs/Burst only after profiling identifies the numerical loop as the bottleneck.

### Collision can dominate runtime

Mitigation:

- Exclude the target slot up front.
- Build one stroke-local acceleration structure.
- Query only nearby triangle cells/nodes.
- Allow collision to be disabled or restricted to body slots.

### Cloth can stretch or explode

Mitigation:

- Use fixed timesteps, XPBD compliance, velocity damping, correction clamps, and maximum substeps.
- Pin the transition ring.
- Reject non-finite state and restore safely.

### Symmetry and collision may disagree

Mitigation:

- Project symmetry during constraint iterations.
- Apply collision last.
- Document that asymmetric collision geometry can produce small final asymmetry.

### Saved results may animate poorly

Mitigation:

- Preserve topology and skin weights.
- Document that the result must be tested in representative poses and DNA ranges.
- Include animated validation in the manual acceptance matrix.

## Acceptance Criteria

The Cloth brush is complete when:

- Grab, Pinch Point, Pinch Folds, Inflate/Deflate, and Expand/Contract are usable from the Sculpt panel.
- The simulation is local, topology-preserving, deterministic for fixed inputs, and stable against invalid geometry.
- Masks provide full and partial pinning.
- Connected Only prevents cross-layer influence.
- Body and visible-slot collision scopes work without colliding against the active cloth itself.
- Coincident vertices and All Slots welded seams remain synchronized.
- X symmetry works through explicit particle pairing.
- One gesture creates one Undo operation, and interrupted strokes clean up safely.
- Saving through every existing sculpt save path reproduces the preview result without new runtime data formats.
- Balanced quality meets the agreed interactive performance budget on representative UMA garments.
- Existing Add, Remove, Smooth, Grab, Crease, Pinch, Plane, Boundary, and Elastic Deform behavior remains unchanged.
- Automated tests and the manual garment matrix pass.
- `MeshModifierSculpting.md` documents controls, workflows, limits, and troubleshooting.

## Recommended Delivery Order

Deliver the work as reviewable changes rather than one monolithic patch:

1. Pure solver, fixtures, and unit tests.
2. Grab Cloth Scene integration.
3. Remaining Cloth force modes.
4. Collision acceleration and projection.
5. Symmetry, seam, and open-boundary hardening.
6. Quality presets, performance cleanup, integration tests, and documentation.

Each change should compile and leave existing sculpt modes usable. Do not begin optimization with Jobs/Burst until the managed implementation is correct, tested, and measured.
