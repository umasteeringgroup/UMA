# Generated resource reuse for NPCs

Enable **Dynamic Character Avatar > Advanced Options > Cache and Reuse (NPCs)** before building a crowd:

- **Share generated meshes**: reuse matching generated geometry. Each character still has its own skeleton, renderer, Animator, pose, cloth component and live blendshape weights.
- **Share atlases / matching materials**: reuse identical compositor outputs, independently for each atlas channel. Share material instances only when their configured inputs match. Different surface parameters do **not** prevent texture sharing.

Both options default to off. They are per character, not a project-wide change. The same flags are available on `UMAData` for non-DCA callers.

```csharp
avatar.reuseGeneratedMeshes = true;
avatar.reuseGeneratedTextures = true;
avatar.BuildCharacter();
```

Changes apply on the next build. Turning reuse off does not detach an already-visible output until it is rebuilt.

## Compare random crowd runs in the Inspector

Open **U3-Generating Random Characters**, select **GeneratorParms**, and enter Play mode.
The **UMA Resource Usage Monitor** component starts the sample with reuse **OFF**.
After it says **Run complete**, click **Restart crowd: reuse ON**. The old crowd is destroyed,
its resources are released, and the same initial random sequence is replayed. These buttons
change runtime avatar flags, not prefab assets. Use **Restart crowd: reuse OFF** again to check
warm-up effects. The Inspector retains the last completed OFF and ON results for comparison.

The monitor shows:

- Mesh and texture stage main-thread milliseconds, total generator work, and wall time until the queue/readbacks settle.
- Mesh and atlas cache hits, in-progress joins, and published shared outputs. Lease retains are not cache hits.
- Cache bypass counts and the latest reason, so disabled/unsupported sharing is distinguishable from a cache miss. A per-avatar atlas callback can require private textures even when reuse is enabled.
- Unique live meshes/textures, uses across avatars, deduplicated memory, and memory avoided by live sharing.
- Queue/readback work and completed/spawned avatar counts to distinguish completed runs from incomplete ones.

Completed runs log automatically. **Log report**, **Copy JSON**, and **Save JSON** are available at
runtime; files go to `Application.persistentDataPath`. **Reset capture** only resets this monitor's
baseline, not the generator, cache, or crowd. Initial reuse settings are editable before Play;
the restart buttons deliberately apply the new mode only to the replacement crowd.

Memory uses Unity's [native object memory measurement](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Profiling.Profiler.GetRuntimeMemorySizeLong.html),
not total process RAM or a separate GPU allocation census. It covers the current meshes and atlas
output references of the monitored UMAData objects, including inactive pooled avatars. Source
compositor textures, intermediate buffers, and renderers whose UMAData has been stripped are not
included. A texture referenced by multiple passes/channels counts once per avatar and once globally.
Unavailable sizes are flagged rather than treated as proof that an object uses no memory.
The avoided-memory figure is the hypothetical cost of one copy per avatar, not an allocation trace.

Mesh time includes preprocessing and discarded incremental work; texture time measures the generator
stage (including lookup/compositing submission), not GPU execution or asynchronous readback latency.
Worker CPU is not summed. Wall time includes frame scheduling and readback waits. Generator timings
cover the assigned generator; cache counters are process-wide deltas since the capture began, while
memory is scoped to the configured crowd/root. Avoid unrelated avatar generation during comparison.

The monitor also breaks out **Atlas preparation**, **Atlas lookup + binding**, **Atlas generation**,
and **Early atlas hits**, in both the current capture and OFF/ON comparison. Preparation includes
layout, UV updates and drawing-material setup. Lookup includes input signatures, cache acquisition
and binding a hit; generation includes render-target allocation, drawing, postprocessing, and
conversion submission on misses. These are substeps, not extra time to add to Texture stage.
Other bookkeeping and DNA pre-apply are still included in the total texture-stage timer.

Ordinary UMA compositing checks a compact signature before preparing drawing materials. The
signature covers ordered overlays, layout, transforms, color multipliers/additives, transparent
prefill, source texture contents/revisions/sampling, shaders, postprocesses and output settings.
Warm signature construction reuses scratch storage; only new cache entries freeze a copy.
Hashes select buckets; complete signatures still determine equality. Hits do not rewrite shared
texture sampling settings. Atlas layout is still prepared by the existing builder.

Advanced blending, cutouts, custom shaders, nonstandard drawing-template settings and custom
postprocesses retain the detailed resolved-command path. Dependency hashes and shader-property
layouts are cached, with Editor import/project/Undo invalidation. Runtime texture update counters
and sampler settings are checked, and `SetDependencyRevision` remains required for externally
written inputs/hidden dependencies. `InvalidateTextureInputs()` invalidates cached texture input
metadata without destroying resources still owned by existing avatars.

Memory sampling is deferred until generation finishes by default, to avoid distorting the timing
comparison. The UI Toolkit Inspector refreshes twice a second; it does not scan resources on every
repaint. Memory sampling cost is shown separately. Enable **Sample Memory During Generation** only
when you need intermediate snapshots. The last-run timings freeze at completion; the current memory
census continues to track releases. Exact randomized appearances may rarely match, so zero reuse
can be a legitimate result. For a repeated-outfit stress test, constrain the randomizer's wardrobe,
DNA and atlas colors; matching outfit names alone do not guarantee identical mesh or atlas inputs.

## What is reused

The cache sits around the existing builders; it does not replace recipe assembly, DNA evaluation or the rendering system.

| Output | Match includes | Does not require a match |
| --- | --- | --- |
| Mesh | Actual slot geometry, modifiers, masks, LOD, skin weights, bind poses, baked blendshapes, atlas UV layout, output options and submesh/pass layout | Atlas pixels/colors, current bone pose, live expression weights |
| Atlas channel | Ordered resolved compositor commands, input texture identities/revisions, colors, masks, transforms, blend operations, postprocessing, dimensions, format, mipmaps and sampling settings | Surface shader parameters, material instance identity, skeleton or pose |
| Material | Template identity/revision, shader, effective properties, keywords, passes, known rendering tags, UMA property inputs and bound textures | Other materials using the same atlas |

Exact serialized descriptions are compared, not just hashes. Input textures are identified within the current session; different source texture objects are conservatively considered different even when their pixels happen to match. This avoids reading back entire input images merely to look for duplicates.

Incremental, jobified and standard/default mesh combiners support reuse. The default combiner's managed and MeshData modes have separate identities. Custom subclasses retaining custom combine behavior use their normal private path.

Matching pending incremental mesh requests reserve one entry. The first requester builds; followers bind the completed result. Cancelling the producer releases its jobs and promotes a waiting requester. Atlas drawing already runs synchronously on the main thread: its completed render target is immediately reusable, and matching requests also share a pending GPU-to-Texture2D conversion. Conversion does not depend on the first avatar staying alive.

This avoids duplicate mesh generation and atlas rendering/conversion for matching characters. Per-instance recipe resolution, skeleton setup, renderer binding and temporary preparation still happen. It is **not** a promise of zero allocations per spawn, nor does it remove per-character animation, skinning or draw-call costs. Measure player-build frame time and memory on the target platform; do not infer an FPS multiplier from cache hit counts.

## Ownership and cleanup

`UMAGeneratedResourceCache.Shared` owns generated resources through counted leases. Renderers retain their own leases, independently of transient build metadata. Resources are released when the last lease goes away; the cache does not retain unused outputs indefinitely. An asynchronous readback is also an owner until it completes or is safely cleaned up.

- Replacing or destroying one avatar does not free another avatar's shared output.
- Disabled/pooled renderers keep their references. Destroy the avatar to release them.
- Stripping `UMAData` for a static character leaves renderer-owned resources alive.
- Cloning a completed generated/static renderer acquires new leases for shared output; explicitly private resources are copied. Do not clone a non-shared atlas while its private readback is still pending.
- Rebuild/cancellation cleanup releases pending reservations and staging renderer ownership.
- Before an Editor assembly reload, visible shared outputs become renderer-owned private copies; managed leases are then released. A subsequent build can rejoin the cache.
- Subsystem/session reset does not destroy outputs still held by surviving renderers when scene reload is disabled.

Do not manually `Destroy`, clear, modify, or release a shared mesh, material or texture. Do not keep generated resources after their owning renderers have been destroyed without your own explicit retained lease.

## Per-character edits

Prefer `MaterialPropertyBlock` for renderer-specific shader overrides. It lets matching materials remain shared. UMA's runtime expression material effects already use property blocks.

For direct edits to generated output, detach it first:

```csharp
var renderer = avatar.umaData.GetRenderer(0);
Mesh privateMesh = UMAResourceLeaseOwner.MakeMeshUnique(renderer);
UMAResourceLeaseOwner.MakeMaterialsUnique(renderer);
Texture privateTexture = UMAResourceLeaseOwner.MakeTextureUnique(renderer, 0, "_BaseMap");
```

The helpers only copy shared generated resources; source asset textures are not copied implicitly. `MakeTextureUnique` may wait for outstanding GPU readbacks because it is an explicit editing operation. These are per-renderer output edits, not recipe changes: a subsequent rebuild replaces them. Continue to author persistent appearance through slots, DNA, overlays and shared colors.

## Runtime inputs and extension code

Mesh lookups first capture a cheap signature of the small output-affecting settings (slot
names, modifier scales, LOD, submesh mapping, triangle masks, UV layout, included blendshape
names, baked values and frame/normal/tangent options). A different signature rejects the
candidate immediately, without constructing the detailed geometry/modifier key. Unique
requests can finish and publish without constructing that detailed key either. Publication
still verifies the captured settings, source references and invalidation revision.

Only a potential match resolves the detailed keys and verifies exact equality before sharing
or joining an in-progress build. A matching signature alone never permits reuse. Large vertex,
skin-weight and blendshape arrays are **not scanned, serialized or hashed for every NPC**.
The first detailed check fingerprints a source once with a native 128-bit hash and an immutable
snapshot; later checks reference that fingerprint. Hashes select buckets, with exact comparison
to resolve collisions. Scaled copies of an unchanged modifier
share its adjustment fingerprint, while its current scale is always part of the character key.
Fresh race-slot bake copies are identified by their original source fingerprint and captured
bake values, included-shape patterns and normal-smoothing settings; their full copied buffers
are not scanned again. Explicit invalidation revokes that provenance after in-place edits.
Live, non-baked facial expression weights are renderer-local and do not prevent mesh sharing.

Source fingerprints have the lifetime of their source data or live output keys (not every NPC).
The weak fingerprint tables do not keep unloaded source objects alive. An unresolved live
entry retains its source geometry/adjustment data until verification or final release, but
never its avatar, renderer, recipe or build operation. Disposed leases do not retain keys.
Fingerprints/snapshots are managed source-cache memory, separate from the generated native
mesh/texture memory reported by the monitor. `UMA.Reuse.MeshInputLookup` is available in the CPU
Profiler and includes signature creation, cache lookup and any candidate verification.
Distinguish unique misses, first-time candidate verification and warm hits when benchmarking.

Editor slot validation, dirty source assets, reimports/project changes and Undo/Redo invalidate
the source cache automatically. Replacing a complete `UMAMeshData` object is detected. Runtime
scripts that edit public source arrays or fields **in place must notify UMA before rebuilding**:

```csharp
slotAsset.meshData.vertices[0] = newPosition;
UMAResourceReuse.InvalidateMeshSource(slotAsset.meshData);
// For in-place modifier adjustment edits, or several aliased source edits:
UMAResourceReuse.InvalidateMeshInputs();
avatar.BuildCharacter();
```

The invalidation covers aliased/shared source buffers and modifier copies, and rebuilds their
fingerprints lazily. It does not destroy outputs still used by other characters. Do not mutate
source data during an asynchronous build without invalidating it. Merely changing a modifier's
scale, baked weights, included shapes, LOD or atlas layout needs no explicit notification: those
small per-character parameters are read on every request.

Imported texture revisions and actual mesh contents participate in matching. For textures written at runtime, explicitly register a revision and increment it after every write:

```csharp
UMAResourceReuse.SetDependencyRevision(runtimeMask, ++maskRevision);
```

Unregistered runtime render textures and textures without a dependable content revision bypass atlas reuse. Built-in UMA atlas shaders are supported. A custom deterministic atlas shader must opt in with `SetDependencyRevision(shader, revision)`; its revision must change when dependencies not represented by its material properties change. Do not register time-dependent or per-camera-dependent compositors as deterministic.

`AtlasUpdated` events with no listeners allow atlas reuse, including serialized empty events and events whose last runtime listener was removed. Registered callbacks (Inspector or runtime) still bypass atlas reuse because they can stamp arbitrary data into an atlas. A subscribed decal component can have no stamps and still count as a listener; this check does not attempt to predict what callbacks do. Listener detection uses a cached delegate to Unity's internal call count (preserved for stripped players); if that API is unavailable, non-null events conservatively bypass reuse. Custom/buffer material properties that cannot be described safely keep separate materials, without vetoing otherwise safe atlas sharing. Register a template material revision after changing hidden custom material state. Extension callbacks that directly mutate generated outputs must use the copy-before-edit APIs or disable reuse for that character.

If shader sampling, source assets or renderer resources are deliberately mutated outside these contracts, UMA cannot infer their lifetime or every hidden dependency. Rebuild the affected characters after changing source revisions.

## Diagnostics and tests

Use **Log Cache Diagnostics** in the DCA's Advanced Options. Counters distinguish completed hits, misses, pending joins and published outputs. Reference counts include renderer, build, material and readback leases, not just avatar count. The status line explains conservative bypasses.

Tests cover first-stage rejects without detailed work, equal signatures with unequal geometry, edits during pending builds, lazy-key cleanup, exact key equality, counted ownership, concurrent mesh requests, cancelled producers, pixel parity, color/revision/layout misses, different material parameters sharing one atlas, a 30-avatar shared-output crowd, independent bones and expression weights, both conversion modes, producer destruction during readback, pooling, static conversion, explicit private edits, renderer destruction and assembly-reload detachment. Existing combiner and scheduler tests remain part of the regression run.
