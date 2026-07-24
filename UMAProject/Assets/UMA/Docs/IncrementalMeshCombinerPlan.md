# UMA Incremental Mesh Combiner Implementation Plan

Status: Implementation in progress  
Last updated: 2026-07-24

## Implementation Status

- Phase 1 complete: semantic output fixtures and crowd baseline capture tooling.
- Phase 2 complete: optional multi-step contracts, deterministic time slices,
  lifecycle semantics, and contract tests.
- Phase 3 complete: resumable generator scheduler, queue ownership,
  cancellation, and synchronous-combiner compatibility.
- Phase 4 complete: shared soft deadline, generator override and inspector
  support, overrun metrics, and deterministic scheduler tests.
- Phase 5 complete: separate incremental combiner, immutable per-renderer
  plans, persistent pending MeshData ownership, non-blocking job polling,
  detached base-mesh generation, synchronous compatibility, and basic parity
  tests.
- Phase 6 complete: base-mesh application and renderer finalization are
  separate budgeted units; renderer hierarchies, slot placement, atlas
  metadata, generated-material references, and second-pass material ownership
  publish in one final transaction and roll back on cancellation or failure.
- Phase 7 complete: each detached mesh remains in process while one reusable
  delta-buffer set prepares frames on a worker thread; every
  `AddBlendShapeFrame` call is a separate budgetable unit, with deterministic
  shape/frame ordering, synchronous compatibility, timings, counters, and
  multi-frame parity coverage.
- Phase 8 complete: the incremental component now covers multiple and empty
  renderers, renderer assets, atlas and non-atlas UVs, shared MeshData channel
  paths, second-pass materials, mesh hides/internal LOD, vertex overrides,
  jobified and managed modifier fallbacks, 16/32-bit indices, blendshape
  options, cloth, bounds, slot metadata, generated-material references, and
  DNA updater publication. Focused parity/scheduler coverage passes 24/24.
- Phase 9 complete: generation request versioning prevents stale builds from
  clearing newer dirty flags; active builds restart after safe non-blocking
  cancellation, combiner changes are detected, shutdown/removal cleanup is
  idempotent, and cleanup continues after individual disposal errors.
- Phase 10 implementation complete: Profiler markers, generator lifecycle and
  latency diagnostics, baseline-report counters, opt-in setup guidance,
  blendshape stress instructions, and rollout checklists are available.
  Crowd and platform acceptance captures remain hardware-specific and must be
  recorded on each shipping target.

Current Unity `6000.3.18f1` verification:

- Generator scheduler: 13/13 passing.
- Focused incremental combiner: 5/5 passing.
- Full multi-step category: 21/21 passing.
- Broad MeshCombiner category: 43/47 passing. The remaining four are the
  recorded pre-existing bone-baking reflection and synthetic bind-pose
  failures; no incremental combiner test fails.

## Objective

Create a new, opt-in UMA mesh combiner that can amortize mesh generation over
multiple frames while moving suitable CPU work to Unity Jobs and Burst.

The generator will give multi-step mesh generation a configurable soft
main-thread time budget. Existing mesh combiners and synchronous generation
paths must continue to behave as they do today.

The new combiner must eventually support everything provided by
`UMADefaultMeshCombiner`, including:

- Multiple renderers.
- Renderer assets and renderer regeneration.
- Atlas, non-atlas, and shared-rectangle materials.
- Mesh-hide assets and internal LOD.
- Vertex overrides and mesh modifiers.
- Bone weights, bind poses, and skeleton assignment.
- Blendshape frames, baking, normals, and tangents.
- Cloth.
- Slot metadata, DNA updaters, bounds, and generation events.

The primary performance goal is to reduce worst-case main-thread work per frame
without making total avatar generation unnecessarily slow.

## Compatibility Requirements

Keep the existing `UMAMeshCombiner.UpdateUMAMesh` API unchanged. Add an optional
multi-step interface implemented only by the new combiner.

The intended API shape is:

```csharp
public interface IUMAMultiStepMeshCombiner
{
    IUMAMeshCombineOperation BeginUpdateUMAMesh(
        bool updatedAtlas,
        UMAData umaData,
        int atlasResolution);
}
```

The returned operation should support:

```csharp
public interface IUMAMeshCombineOperation : IDisposable
{
    UMAMeshCombineStepResult Step(UMAMeshCombineTimeSlice timeSlice);
    void Cancel();

    string StageName { get; }
    float Progress { get; }
    bool HasPendingJobs { get; }
    UMAMeshCombineStatus Status { get; }
    Exception Error { get; }
}
```

`UMAMeshCombineStepResult` and `UMAMeshCombineStatus` distinguish:

- In progress.
- Waiting for asynchronous work.
- Completed.
- Failed.
- Cancelled.

`UMAMeshCombineTimeSlice` carries one shared soft deadline. A zero budget and
the default value are unlimited. It accepts an injectable monotonic timestamp
provider so scheduler tests do not depend on real elapsed time.

`InProgress` means another bounded unit may run immediately while time remains.
`WaitingForAsync` tells the generator to yield and poll during a later update
instead of completing an unfinished job on the main thread.

Existing mesh combiners continue through the current synchronous path. Direct
calls from editor utilities, `GenerateSingleUMA`, and `UpdateSlots` must still
complete synchronously.

The new combiner will implement its inherited synchronous `UpdateUMAMesh` by
running its operation to completion. This preserves compatibility with callers
that invoke mesh combination outside the generator's queued `Work` path.

## Generator State Machine

The current generation path treats mesh construction as one atomic operation.
Multi-step generation requires a resumable per-UMA state machine:

```text
Queued
  |
  v
Validate and initialize
  |
  v
Preprocess, events, and DNA
  |
  v
Generate textures
  |
  v
Begin mesh operation
  |
  v
Schedule jobs <----> Wait without blocking
  |
  v
Apply mesh chunks and blendshape frames
  |
  v
Commit completed renderers
  |
  v
Bounds, physics, avatar, and completion events
  |
  v
Clean
```

The queued UMA remains active until every required stage succeeds. It must not
be removed from the dirty queue or moved to the clean list while its mesh
operation is incomplete.

Initially, use this state machine only when the selected combiner implements
`IUMAMultiStepMeshCombiner`. Preserve the current synchronous generation path
for all existing combiners.

## `OnDirtyUpdate` Refactor

Split `OnDirtyUpdate` into focused start, continuation, and finalization
functions:

```csharp
private UMAMeshCombineStatus StartDirtyUpdate(
    UMAMeshCombineTimeSlice timeSlice);
private UMAMeshCombineStatus ContinueDirtyUpdate(
    UMAMeshCombineTimeSlice timeSlice);
private void CompleteDirtyUpdate(UMAData completedData);
private void FailDirtyUpdate(Exception exception);
private void CancelDirtyUpdate();
```

Responsibilities:

- `StartDirtyUpdate` selects the next UMA and captures its request version.
- Existing combiners run through the unchanged synchronous generation path.
- Multi-step combiners create an active generation operation.
- `ContinueDirtyUpdate` advances the active operation within the available
  main-thread time budget.
- `CompleteDirtyUpdate` performs queue transitions only after the entire avatar
  build has succeeded.
- Failure and cancellation dispose resources without falsely marking the UMA
  clean.

Extract shared finalization helpers where practical so synchronous and
multi-step generation have the same cleanup and event semantics.

## `Work` Behavior

`Work` should:

1. Advance an existing multi-step operation.
2. Poll asynchronous jobs without blocking.
3. Continue immediately when a job finishes and time remains.
4. Start another UMA only when no operation is active.
5. Apply `InterFrameDelay` when starting the next UMA, not when advancing an
   active UMA.
6. Continue servicing other required generator work, such as pending
   render-texture-to-CPU operations, without allowing one category to starve
   another.

Queue and status APIs must account for the active operation:

- `IsIdle` remains false while an operation or its jobs are active.
- `QueueSize` includes the active UMA.
- `updateProcessing` reports the active UMA.
- Removing an active UMA cancels its operation safely.
- Disabling or destroying the generator cancels and cleans up active work.

`processAllPending` must not bypass the time budget for the incremental
combiner. `IterationCount` can continue to control conventional synchronous
processing and how many UMAs are started or completed, but it must not force an
incomplete multi-step operation to finish.

## Main-Thread Time Budget

Add one generator setting:

```csharp
[Min(0)]
public float MaxMultiStepWorkMilliseconds = 2.0f;
```

Recommended semantics:

- The value limits multi-step main-thread work during one `Work` call.
- `0` means unlimited.
- The limit is soft because Unity API calls cannot be interrupted.
- Do not start another atomic step after reaching the deadline.
- Measure and report any atomic step that exceeds the remaining budget.
- If work finishes early, continue during the same frame while budget remains.
- Do not introduce an artificial one-stage-per-frame delay.

Update:

- `UMAGeneratorBuiltin`.
- `UMAGeneratorBuiltinEditor`.
- `UMAGeneratorOverride`.
- `UMAGeneratorOverrideEditor`.
- Override capture, application, validation, and restoration.

Use an injectable clock in scheduler tests. Do not make timing tests depend on
real frame timing or `Stopwatch` precision.

## New Incremental Combiner

Create a separate component and operation implementation:

- `UMAIncrementalMeshCombiner`.
- `UMAIncrementalMeshCombineOperation`.
- Immutable combine-plan structures.
- Incremental mesh job and resource-container types.

The new combiner should derive directly from `UMAMeshCombiner`. It may share
stateless validation and mesh-math helpers, but it must not depend on mutable
state owned by the default or existing jobified combiner.

The operation owns all temporary meshes, native buffers, job handles, staged
renderer data, and cleanup state. Native allocations that can survive multiple
frames must use a suitable persistent lifetime rather than
`Allocator.TempJob`.

## Immutable Combine Plan

Before scheduling worker jobs, capture a stable main-thread snapshot containing:

- Active slots and source mesh data.
- Renderer assignments.
- Generated materials and atlas rectangles.
- Mesh-hide masks and LOD selection.
- Bone and bind-pose mappings.
- Blendshape metadata and settings.
- Vertex overrides.
- Mesh modifiers.
- Cloth and renderer settings.

Unity objects, transforms, meshes, renderers, materials, and ScriptableObjects
must only be accessed on the main thread. Jobs should receive blittable
descriptors and native buffers.

Complete callbacks that can affect the combine inputs before finalizing the
snapshot. Once jobs are scheduled, changes to the live recipe must not mutate
their inputs.

## Background Base-Mesh Construction

Move suitable work into Burst-compatible jobs:

- Vertex stream copying.
- Normal and tangent copying.
- Colors and UV channels.
- UV atlas transformation.
- Bone remapping and bone-weight construction.
- Masked triangle generation.
- Internal LOD index generation.
- Supported sparse vertex modifiers.
- Baked blendshape deltas.
- Bounds reduction.

Schedule independent renderer and channel jobs early so they can execute
concurrently.

The main thread should check `JobHandle.IsCompleted` before calling `Complete`.
Ordinary processing must not stall the main thread waiting for unfinished jobs.

Jobs cannot safely throw Unity-facing exceptions. Return validation state
through native result data, then create an actionable error on the main thread.

## Main-Thread Mesh Application

Several operations must remain on the main thread:

- Creating Unity `Mesh` objects.
- Applying writable mesh data.
- Calling `AddBlendShapeFrame`.
- Assigning renderer meshes, bones, materials, and bounds.
- Creating or configuring cloth and other components.
- Uploading mesh data to the graphics driver.

Perform these as bounded atomic units. Stop before starting the next unit when
the budget is exhausted.

The budget cannot preempt an in-progress Unity API call. Profiler diagnostics
must identify atomic calls that regularly exceed the configured limit.

## Atomic Presentation and Rollback

Do not progressively dismantle the currently displayed avatar.

Build detached destination meshes and staged renderer data. When renderer
regeneration is required, prepare a disabled replacement hierarchy.

Only after the complete operation succeeds should the final commit:

- Swap meshes or renderer hierarchies.
- Assign materials and bones.
- Apply bounds and cloth settings.
- Update slot renderer, submesh, and vertex-offset metadata.
- Update generated-material renderer references.
- Apply DNA updater and final renderer metadata.
- Destroy or release the replaced generated renderer state.

If generation fails or is cancelled before commit, the previous avatar remains
visible and valid.

The final swap is intentionally atomic from UMA's perspective, although the
individual Unity calls still execute serially on the main thread.

## Incremental Blendshape Pipeline

`Mesh.AddBlendShapeFrame` is an especially expensive main-thread call. The
incremental combiner must not load every blendshape in one generator step.

Each detached output mesh remains a mesh-in-process across frames. Apply its
base vertex and index data first, then retain the mesh and a blendshape cursor
containing:

- Output renderer index.
- Blendshape index.
- Frame index within the blendshape.
- Prepared delta-buffer ownership.
- Preparation job handle.
- Whether the frame is ready to apply.

Do not assign the mesh to the live renderer, upload it, make it non-readable, or
discard its source buffers until every required blendshape frame has been
successfully added.

Blendshape processing should use a bounded producer/consumer pipeline:

1. A background job assembles the complete delta buffers for one blendshape
   frame.
2. The operation yields while the job is running.
3. On a later `Work` call, the generator checks the job without blocking.
4. If the job is complete and budget remains, the main thread calls
   `AddBlendShapeFrame` once on the retained mesh-in-process.
5. The operation advances its blendshape/frame cursor and yields when the
   main-thread deadline is reached.
6. The buffers are reused to prepare the next frame.

One `AddBlendShapeFrame` call is the smallest supported application unit. Unity
requires the complete vertex-delta frame and does not expose a supported API for
adding part of one frame. A single unusually large frame can therefore exceed
the soft budget, but the generator must yield immediately afterward instead of
loading the remaining frames during the same update.

When a completed preparation job and sufficient budget are available, apply the
frame immediately; do not require an unnecessary additional frame. Conversely,
never call `JobHandle.Complete` on an unfinished preparation job just to keep
the pipeline moving.

Keep at most one or two prepared frames in memory. Preparing every frame at once
could replace a frame-time problem with an unacceptable native-memory spike.

Use reusable buffers sized to the output mesh vertex count:

- Prefer a supported span/native-data path when the active Unity version and UMA
  compilation settings allow it.
- Otherwise copy completed native results into pooled managed arrays required by
  the available `AddBlendShapeFrame` overload.
- Pass `null` or an empty span for normals and tangents when the source shape
  does not contain them.
- Do not allocate new full-size managed arrays for every frame.

Preserve Unity's required frame ordering. All frames for one shape must be added
in deterministic increasing frame order before advancing to the next shape.

If a frame fails validation or `AddBlendShapeFrame` throws, abandon and destroy
the staged mesh. Do not commit a mesh containing only part of its required
blendshape set.

Preserve:

- Shape names and deterministic ordering.
- Multiple frames and frame weights.
- Vertex, normal, and tangent deltas.
- Blendshape baking.
- Slot blendshape sources.
- Existing blendshape filtering and settings.

Profile preparation separately from `AddBlendShapeFrame`. Jobifying delta
assembly can reduce CPU preparation cost, but it cannot make Unity's internal
blendshape copy asynchronous. The main benefit of the retained mesh-in-process
is that those Unity calls are distributed across frames without rebuilding the
base mesh.

## Mesh Modifier Handling

Known additive sparse modifiers can use a jobified path when their behavior is
fully represented by immutable native input.

Arbitrary custom `MeshModifier.Process` implementations remain bounded
main-thread operations because they can contain managed code and access Unity
objects.

Unsupported modifiers must use a correct synchronous fallback rather than being
silently skipped.

## Re-Dirtying and Mutable Recipes

An UMA can become dirty again while its current operation is active. Add a
request-generation number:

- Increment it whenever the UMA is dirtied.
- Capture it when generation starts.
- If a newer request arrives, mark the active build for rerun or cancellation at
  a safe boundary.
- Completion of an older request must not clear dirty flags belonging to a
  newer request.

If the combiner, recipe, source asset, or other required input changes while an
operation is running, cancel or finish safely and requeue using a fresh
snapshot.

Begin with one active UMA operation per generator. Multiple simultaneous UMA
operations would increase memory use and risk conflicts with shared generator
and texture-pipeline state. Keep operation state self-contained so bounded
multi-UMA pipelining can be considered later.

## Events and Animator State

Preserve existing observable ordering:

- Character begun events fire once.
- Slot and atlas callbacks occur in their expected order.
- DNA and post-processing occur at the corresponding generation stages.
- Completion events fire once and only after successful commit.
- Cancelled or failed builds do not fire normal completion events.

Do not leave the generator's global `FreezeTime` state enabled across several
frames. Capture animator state per operation and scope any freeze behavior to
the stages that require it.

## Cancellation and Cleanup

Cancellation must be deterministic and idempotent.

Handle:

- UMA removal.
- Combiner changes.
- Recipe changes.
- Destroyed `UMAData`.
- Generator disable or destruction.
- Scene unload.
- Play Mode exit.
- Domain reload.
- Job validation failures.
- Exceptions during application or commit.

Native memory cannot be disposed while a job is using it. Normal cancellation
should poll completion or schedule dependent disposal. Shutdown may complete
outstanding handles as a final safety measure to guarantee valid cleanup.

Dispose resources exactly once. Cleanup errors should not prevent remaining
resources from being released.

## Feature-Parity Matrix

Verify the new combiner against `UMADefaultMeshCombiner` for:

- Single and multiple renderers.
- Renderer assets, defaults, and regeneration.
- Atlas, non-atlas, and shared-rectangle materials.
- Multiple UV sets and atlas UV remapping.
- Second-pass materials and compositing.
- Existing-texture channels.
- Mesh-hide masks and internal LOD.
- Vertex overrides and modifier fallbacks.
- Every supported vertex channel.
- Bone weights, bind poses, and skeleton assignment.
- 16-bit and 32-bit indices.
- Blendshape sources, frames, weights, normals, tangents, and baking.
- Race blendshapes.
- Cloth and cloth properties.
- Bounds and empty-renderer cleanup.
- Slot events and generated slot metadata.
- DNA updaters.

## Known Limitations

The first implementation should document these constraints:

- The millisecond limit is a soft main-thread budget.
- Unity API calls and graphics uploads cannot be preempted.
- Texture generation remains a potentially expensive atomic generator stage
  unless it is later converted to the same resumable-task model.
- Moving CPU preparation to jobs does not move mesh upload or renderer mutation
  off the main thread.
- Total avatar latency may increase slightly in exchange for lower frame-time
  spikes.

## Profiling and Diagnostics

Add profiler markers for:

- Main-thread milliseconds consumed per frame.
- Atomic budget overruns and the responsible operation.
- Current generation and combination stage.
- Background job duration.
- Time spent waiting for jobs.
- Mesh application and upload time.
- Blendshape-frame preparation time.
- Individual `AddBlendShapeFrame` duration and budget overruns.
- Blendshape frames prepared and applied per generator update.
- Native temporary-memory high-water mark.
- Total avatar generation latency.
- Cancellation and restart counts.

These measurements must distinguish a combiner that lowers frame spikes while
remaining fast from one that simply spreads generation over too many frames.

## Test Plan

### Generator scheduler tests

Use a fake incremental combiner with controllable stages and an injectable
clock.

Verify:

- The UMA remains queued until the operation completes.
- The configured time budget is honored.
- Blendshape application resumes at the correct renderer, shape, and frame.
- No more frames are added after an `AddBlendShapeFrame` call consumes the
  remaining budget.
- Jobs are polled without blocking.
- Remaining budget is used when work completes early.
- Events fire once and in the correct order.
- `InterFrameDelay`, `IterationCount`, and `processAllPending` have the intended
  behavior.
- Queue size and idle state include active work.
- Removing the active UMA cancels safely.
- Re-dirtying produces a later generation request.
- Existing synchronous combiners are unchanged.
- The new combiner's direct synchronous path completes before returning.

### Mesh-output parity tests

Compare output with the default combiner:

- Vertices, normals, tangents, colors, and UV0 through UV4.
- Triangle indices, submeshes, and LOD.
- Bone weights, bind poses, and assigned bones.
- Bounds.
- Blendshape names, ordering, frame weights, and deltas.
- Materials and renderer assignments.
- Slot offsets, submesh indexes, and renderer metadata.
- Cloth configuration.

Cover:

- One-slot fast paths.
- Multiple slots and renderers.
- Meshes above 65,535 vertices.
- Empty renderers.
- Mesh-hide masks.
- Atlas and non-atlas materials.
- Existing-texture channels.
- Second-pass materials.
- Custom modifiers and vertex overrides.
- Every blendshape option.
- Baked and race blendshapes.
- Cloth.

### Failure and lifecycle tests

Inject failures during every operation stage and verify:

- No native resource leaks.
- No `TempJob` lifetime warnings.
- No partially committed renderer state.
- No false completion events.
- The old avatar remains valid.
- A new request can run successfully afterward.

Test UMA destruction, combiner switching, generator shutdown, scene unloading,
Play Mode exit, and domain reload.

### Crowd and platform validation

Run the 96-avatar crowd scene and record:

- Median, 95th, and 99th percentile frame time.
- Worst main-thread combination step.
- Total time until all avatars are ready.
- Peak managed and native memory.
- Mesh upload and graphics-driver spikes.
- Job utilization.
- Cancellation or restart counts.
- Output parity with the existing combiner.

Test:

- Burst enabled and disabled.
- Mono and IL2CPP.
- Representative mobile, PC, and console graphics APIs.
- Editor and player builds.

## Implementation Phases

### Phase 1: Baseline and fixtures

- Capture default and jobified combiner output.
- Record crowd-scene performance and memory.
- Create parity fixtures for all major features.
- Use the `Human Female 3.0` race as the primary blendshape stress fixture
  because it has the largest supplied blendshape set.
- Enable `DynamicCharacterAvatar.loadBlendShapes`; use all frames, normals, and
  tangents for the full worst-case blendshape baseline.

Exit criteria:

- Repeatable output and performance baselines exist.

### Phase 2: Optional multi-step contracts

- Add the optional combiner and operation interfaces.
- Add a fake multi-step combiner for scheduler tests.
- Leave the existing abstract combiner API unchanged.

Exit criteria:

- Fake multi-step work runs while existing combiners remain synchronous.

### Phase 3: Generator scheduler

- Split `OnDirtyUpdate`.
- Add active-operation state.
- Update `Work`, queue reporting, cancellation, and finalization.
- Preserve the current synchronous generation path.

Exit criteria:

- No UMA leaves the dirty queue early, and existing generator tests still pass.

### Phase 4: Time budget and override support

- Add `MaxMultiStepWorkMilliseconds`.
- Integrate the deadline with generator scheduling.
- Update generator and override inspectors.
- Add deterministic budget tests.

Exit criteria:

- The scheduler honors the soft budget and reports unavoidable atomic overruns.

### Phase 5: Incremental base mesh

- Implement immutable combine plans.
- Add persistent native resource ownership.
- Schedule base geometry, mask, LOD, UV, bone, and bounds jobs.
- Generate detached meshes.

Exit criteria:

- Basic avatars match default-combiner geometry.

### Phase 6: Budgeted application and atomic commit

- Apply detached meshes incrementally.
- Stage renderer state.
- Commit only after successful generation.
- Preserve the previous avatar on failure.

Exit criteria:

- Cancellation at every pre-commit stage leaves the displayed avatar unchanged.

### Phase 7: Blendshapes

- Add background preparation for individual blendshape frames.
- Retain each detached mesh-in-process across generator updates.
- Add at most the blendshape frames allowed by the remaining main-thread budget.
- Resume from the stored renderer, shape, and frame cursor on the next update.
- Reuse bounded native buffers.
- Use pooled managed fallback buffers where the Unity API requires arrays.
- Delay upload, non-readable conversion, and renderer assignment until all
  frames have been added.

Exit criteria:

- Blendshape metadata and deltas match the default combiner.
- A character with many blendshape frames requires multiple `Work` calls rather
  than adding all frames during one frame.
- Cancelling between frames destroys the incomplete staged mesh and leaves the
  current renderer unchanged.
- No full-size per-frame managed allocations occur after the reusable buffers
  have been initialized.

### Phase 8: Complete feature parity

- Add material, multi-renderer, renderer-asset, modifier, cloth, metadata, DNA,
  bounds, and event behavior.
- Add correct fallback paths for unsupported managed modifiers.

Exit criteria:

- The complete parity matrix passes.

### Phase 9: Lifecycle hardening

- Add request versioning and re-dirty handling.
- Add combiner-switch, removal, shutdown, domain-reload, and failure cleanup.
- Add rollback and disposal tests.

Exit criteria:

- Fault injection produces no leaks, false completion, or corrupted renderer
  state.

### Phase 10: Profiling and rollout

- Add profiling markers and diagnostics.
- Run platform tests and the 96-avatar crowd soak.
- Document configuration and known limitations.
- Release the combiner as opt-in.

Exit criteria:

- Output parity, stability, memory, total generation time, and frame-time goals
  are accepted on target platforms.

## Recommended Delivery Order

Deliver the work as reviewable changes:

1. Baseline fixtures and performance captures.
2. Optional operation contracts and fake scheduler tests.
3. Generator `OnDirtyUpdate` and `Work` refactor.
4. Generator budget and override integration.
5. Incremental base-mesh jobs and detached mesh staging.
6. Atomic renderer commit and rollback.
7. Incremental blendshape pipeline.
8. Remaining default-combiner feature parity.
9. Cancellation, re-dirtying, and lifecycle hardening.
10. Profiling, crowd soak, platform validation, and documentation.

Keep the new combiner opt-in until all compatibility and performance gates pass.
Do not change the default combiner or make the incremental combiner the default
as part of the initial implementation.
