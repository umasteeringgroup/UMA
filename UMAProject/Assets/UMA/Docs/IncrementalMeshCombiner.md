# UMA Incremental Mesh Combiner

The UMA Incremental Mesh Combiner is an opt-in mesh generation path for projects
that need to create complex avatars without concentrating all mesh work into one
frame. It is intended for runtime customization, crowds, and other situations
where a smooth frame rate matters more than completing every avatar in the
fewest possible frames.

The combiner produces the same UMA renderer content as the default mesh
combiner, but retains a mesh-in-process and advances it through bounded steps.
Suitable CPU preparation runs through Jobs or worker tasks. Renderer pipelines
advance independently, so UMA can apply a completed renderer while another
renderer's Jobs are still running. Unity mesh API calls, renderer changes, and
graphics uploads remain on the main thread.

## When to Use It

Use the incremental combiner when profiling shows mesh combination or
blendshape loading causing visible main-thread spikes.

Good candidates include:

- Runtime character creators.
- Crowds generated while gameplay is active.
- Avatars with many slots, renderers, or blendshapes.
- Mobile projects with a narrow per-frame CPU budget.

Keep the existing combiner when avatars are built only behind a loading screen,
or when the shortest possible total generation latency is more important than
frame pacing.

## Generator Prefab Setup

The incremental combiner is deliberately not selected automatically.

1. Duplicate the project's current UMA Generator prefab.
2. Open the duplicate in Prefab Mode.
3. Add **UMA Incremental Mesh Combiner** to the generator GameObject.
4. On **UMAGenerator**, assign that new component to **Mesh Combiner**.
5. Set **Max Multi-Step Work (ms)** to the desired soft main-thread budget.
6. Assign the duplicate in **Project Settings > UMA > Generator Prefab**.
7. Generate a representative avatar and compare its renderer, materials,
   blendshapes, cloth, and bounds with the existing combiner before rollout.

The old combiner component may remain on the prefab for easy comparison, but
only the component assigned to **Mesh Combiner** is used.

## Main-Thread Budget

**Max Multi-Step Work (ms)** is a soft budget shared by incremental mesh work
during one generator `Work` call.

- `0` means unlimited and completes the incremental operation synchronously.
- A value above `0` lets the generator yield between safe operation steps.
- A step that has already begun is allowed to finish, so an individual Unity
  API call can exceed the budget.

Practical starting points:

| Target | Starting budget |
|---|---:|
| Low-end mobile | `0.5–1.0 ms` |
| Mid/high-end mobile | `1.0–2.0 ms` |
| PC | `2.0–4.0 ms` |
| Console | `1.0–3.0 ms` |

Start at `2 ms`, profile the worst avatar, then tune against the actual frame
budget. A smaller value generally lowers spikes but increases the number of
frames before an avatar is ready.

**Inter Frame Delay** controls the spacing between complete UMAs. It does not
pause an active incremental operation, because active Jobs and staged resources
must continue to be polled and advanced.

## Blendshape Generation

Blendshape delta preparation runs one frame at a time on a worker task. Two
reusable delta-buffer sets allow the worker to prepare the next frame while
Unity adds the current frame. Each `Mesh.AddBlendShapeFrame` call remains a
separate main-thread step, allowing a mesh with many blendshapes to remain in
process across generator updates.

For the supplied worst-case test:

1. Use the **Human Female 3.0** race.
2. Enable **Load BlendShapes** on the `DynamicCharacterAvatar`.
3. Enable all frames, normals, and tangents when testing full feature parity.
4. Compare blendshape names, frame weights, and visible deformation with the
   existing combiner.

Disabling **Load BlendShapes** on the DCA bypasses this stress path and does not
provide a useful blendshape performance comparison.

## Mesh Modifiers

Additive vertex-delta modifier stacks use the jobified modifier path. UMA
validates and snapshots their managed inputs on the main thread, then uses
worker Jobs to:

- Calculate scaled vertex deltas.
- Sort and combine adjustments that affect the same vertex.
- Apply the compacted deltas.
- Recalculate normals and tangents for affected sources.

Sources are independent work items during surface-frame recalculation, allowing
slots with modifiers to run concurrently where worker capacity is available.
This is particularly useful for recipes containing many modifiers or thousands
of individual vertex adjustments.

Custom, derived, or order-dependent adjustment collections continue to use
their managed `Process` implementation. UMA cannot move arbitrary custom code
to a worker safely because it may use Unity APIs, mutate shared objects, or
depend on adjustment order. Use plain `VertexDeltaAdjustmentCollection`
instances when a modifier should qualify for the jobified path.

## Atomic Publication and Rebuilds

The new renderers and meshes are staged away from the visible avatar. UMA
publishes renderer hierarchy, slot metadata, generated-material references, and
the finished meshes together only after all stages succeed.

Renderer finalization is also divided into separate budgeted units for native
mesh and bone binding, material assignment and upload, and cloth and hierarchy
setup. These operations are completed on the detached renderer before the
small publication transaction.

If generation is cancelled or fails before publication:

- The staged renderer is discarded.
- The currently displayed avatar remains valid.
- Slot and generated-material metadata are restored.
- Pending native resources are released only after their Jobs are safe.

If the avatar becomes dirty again while generation is active, UMA versions the
request, cancels the stale operation at a safe boundary, and rebuilds from a
fresh snapshot. An older completion cannot clear flags belonging to the newer
request.

## Profiling

Use Unity Profiler's Timeline view and search for:

- `UMA.Generator.MultiStep.AtomicStep`
- `UMA.Generator.MultiStep.Finalize`
- `UMA.Generator.MultiStep.CancelOrRestart`
- `UMA.IncrementalMesh.BuildPlan`
- `UMA.IncrementalMesh.ScheduleRenderer`
- `UMA.IncrementalMesh.PollJobs`
- `UMA.IncrementalMesh.ApplyBaseMesh`
- `UMA.IncrementalMesh.AddBlendShapeFrame`
- `UMA.IncrementalMesh.BlendShape.PrepareFrame`
- `UMA.IncrementalMesh.FinalizeRenderer`
- `UMA.IncrementalMesh.Commit`

The generator also records runtime diagnostics for:

- Last and maximum atomic step time.
- Atomic budget-overrun count.
- Waiting-for-worker count.
- Restart, cancellation, and failure counts.
- Last and maximum successful multi-step generation latency.
- Active stage and progress.

Open **UMA > Testing > Generation Baseline...** to record the generator
configuration, frame percentiles, memory, queue depth, and these diagnostics.
For crowd testing, generate the same 96 avatars with the existing and
incremental combiners and retain at least three captures of each configuration.

## Rollout Checklist

- Confirm semantic mesh parity with the existing combiner.
- Test Atlas, NoAtlas, existing-texture, and second-pass materials used by the
  project.
- Test mesh hides, modifiers, multiple renderers, cloth, and empty renderer
  groups where applicable.
- Test `Load BlendShapes` both enabled and disabled.
- Exercise avatar removal, recipe changes, and scene transitions during a build.
- Profile Editor and Player separately.
- Validate Burst enabled and disabled.
- Validate Mono and IL2CPP where the target supports them.
- Run target hardware tests for every graphics API the project ships.

## Known Limitations

- The budget is soft; Unity API calls and GPU uploads cannot be preempted.
- Texture generation is still an atomic generator stage and can remain the
  dominant spike for texture-heavy avatars.
- `AddBlendShapeFrame`, mesh upload, and renderer mutation must execute on the
  main thread.
- Lowering the budget can increase total avatar latency.
- One incremental UMA operation is active per generator. This bounds temporary
  memory and avoids conflicts with shared texture-generation state.
- Platform performance acceptance still requires Player captures on the target
  hardware; Editor results are not a substitute.

The incremental combiner should remain opt-in until the project's output,
memory, total generation time, and worst-frame goals have been accepted on all
target platforms.
