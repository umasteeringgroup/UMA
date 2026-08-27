# UMA Surface Bleeding and Fadeable Runtime Decals

## Purpose

This document is the implementation plan and design contract for flowing blood on UMA characters and for fading any runtime RenderTexture decal. The design is deliberately isolated from UMA's existing permanent `DecalRTStampSlot` workflow: existing decals must continue to composite exactly as they do today.

The system must:

- begin at the real dismemberment cut boundary;
- flow over the currently posed, skinned surface in world-space gravity;
- use meters for all physical distances (`1 Unity unit = 1 meter`);
- stop emitting, settle, and optionally fade without repeatedly rebuilding the UMA character;
- batch work and keep atlas updates, allocations, readbacks, and mip rebuilds to a minimum;
- support arbitrary fadeable runtime stamps in addition to blood;
- support a successful `DecalRenderTexture` stamp as a standalone fluid source, without requiring
  a dismemberment result or drawing the decal a second time;
- release every GPU and material resource when an avatar rebuilds or is destroyed;
- retain a safe non-compute fallback.

## Non-Negotiable Isolation Rule

The generated UMA atlas is treated as an immutable base while runtime effects are active. The runtime controller owns separate output RenderTextures for only the affected material channels. It composites the base plus dynamic effects into those outputs and temporarily binds the outputs to the generated material.

Normal flow and fading never call `BuildCharacter`, `ForceUpdate`, or mutate the source atlas. When the last effect completes, the original atlas texture is rebound. `DecalRTStampSlot` and `DecalRenderTexture.ApplySlotStamps` remain the permanent decal path and are not changed by the runtime simulation.

## Runtime API and Ownership

`UMARuntimeSurfaceDecalController` is a self-contained avatar component and the sole owner of runtime simulation state:

```csharp
RuntimeDecalHandle StartBleed(
    DismembermentResult cut,
    UMASurfaceFluidProfile profile);

RuntimeDecalHandle StartBleedFromDecal(
    DecalRTStampAsset stamp,
    UMASurfaceFluidProfile profile,
    DecalRenderTexture.DecalLayerResult decalResult);

RuntimeDecalHandle AddFadeableStamp(
    DecalRTStampAsset stamp,
    RuntimeDecalFadeSettings fade);

void StopFlow(RuntimeDecalHandle handle);
void FadeNow(RuntimeDecalHandle handle);
void Clear(RuntimeDecalHandle handle);
void ClearAll();
```

Handles use a monotonically increasing controller/session sequence. They never use Unity instance IDs.

The standalone decal overload rasterizes the cached target-UV triangles as the fluid injection
source. The stamp overlay's explicit alpha mask, or its first texture's alpha when no separate mask
exists, shapes emission. `DecalLayerResult` also supplies the hit point and normal for the bounded
non-compute fallback. The method does not stamp the bullet/wound again and does not alter the
permanent decal callback path.

One simulation context is shared per generated atlas/material. Each affected channel has at most one owned composited output. Effects are records within that context, not separate materials or full-size textures.

## Fluid Profile

`UMASurfaceFluidProfile` is a ScriptableObject using metric values and safe defaults. It contains:

- blood color and opacity;
- optional `OverlayDataAsset` source for compatible multi-channel appearance;
- channel participation (Albedo by default, optional normal/wetness-compatible channels);
- target slot and overlay groups;
- emission duration and rate;
- mobile lifetime, holding duration, and fade duration;
- viscosity, surface adhesion, lateral spread, pooling, per-meter trail deposition, evaporation,
  and minimum visible thickness;
- fall speed and maximum travel distance in meters;
- fractal breakup scale in meters, strength, octaves, and seed;
- simulation resolution cap, fixed step, maximum substeps, simulation rate, surface-field refresh rate, and composite rate;
- detached-piece routing (`SourceBody`, `SharedAtlas`, or `IndependentDetachedPiece`);
- optional fallback material for the non-RenderTexture trail renderer.

An optional advanced runtime material is only accepted when it implements the documented compositor pass contract. Arbitrary materials are not injected into the RT pipeline because their passes and channel semantics are unknowable.

## Cut Surface Contract

Dismemberment exposes the boundary information already found by the mesh cutter instead of discarding it:

```csharp
DismembermentCutSurface[] cutSurfaces;
```

Each surface identifies the source renderer and material/submesh, contains ordered UV boundary loops and loop ranges, UV bounds, a representative world-space origin/normal, and slot/overlay identity when it can be resolved. The data is copied into durable managed arrays before temporary mesh-building data is released.

The bleed injector rasterizes those loops into a compact UV-space source mask. If no valid loop or compatible generated material can be resolved, the controller records a diagnostic and uses the configured fallback instead of damaging an unrelated atlas.

## Posed Surface Flow Field

A GPU field pass draws the current `SkinnedMeshRenderer` in UV space:

- UV0 becomes clip-space output position;
- skinned world position and normal are written to field textures;
- projected world gravity yields the tangent flow direction;
- local derivatives provide meters-per-UV scale;
- validity and island information prevent motion through empty texels.

The field refreshes at 5-10 Hz while the pose changes, not every rendered frame. A throttled `BakeMesh` path supplies the same data when compute shaders or the required render formats are unavailable. There is no CPU texture readback in the normal path.

## Shallow-Film Simulation

The compute path uses lower-resolution ping-pong textures for:

- mobile film thickness;
- deposited/settled wetness;
- fluid age and remaining mobile life;
- surface flow, metric, validity, and seam links.

Each fixed update consists of:

1. inject fluid along the cut source mask while emitting;
2. calculate outward conservative flux from gravity, thickness gradient, viscosity, adhesion, and validity;
3. gather/apply flux in a second pass so mass is not update-order dependent;
4. add controlled lateral spread and pooling;
5. apply meter-scaled fractal breakup without allowing negative thickness;
6. deposit a frame-rate-independent residue as fluid travels, then fully deposit or expire fluid
   that reaches its travel/lifetime/boundary limits;
7. advance effect state and dirty bounds.

State proceeds through `Emitting -> Flowing -> Settling -> Holding -> Fading -> Complete`. Large frame gaps use fixed steps with a profile-defined substep cap so editor stalls cannot launch unstable simulations.

## UV Seams

Seam candidates are generated from duplicated boundary vertices with matching position, normal, and bone-weight signatures. Links are restricted to the same logical slot first. Cross-slot or cross-atlas transfer is opt-in and only occurs when an explicit compatible target is known. Unrelated layered clothing UV islands are never bridged merely because their UVs or positions overlap.

The compute simulation transfers edge flux across validated seam links. If links cannot be built, fluid deposits at that boundary; it does not jump through empty atlas space.

## Generic Fadeable Runtime Decals

Fadeable `DecalRTStampAsset` instances register their cached stamp geometry before drawing. The runtime compositor redraws them with their current opacity over the immutable base. This makes fade a cheap opacity/state change followed by a dirty-region composite.

Permanent `DecalRTStampSlot` stamps continue to be baked during UMA texture composition. A legacy stamp that is already destructively baked cannot be cleanly faded; opting it into dynamic fading requires one character texture rebuild to recover a clean base, after which it is registered with the runtime controller.

The same cached geometry can independently seed fluid through `StartBleedFromDecal`. A bullet wound
can be retained through `AddPersistentStamp` while its fluid is stopped, settled, faded, or cleared
through a separate handle. Decal emitters use the recorded projection radius to restrict injection
to the profile's meter-sized source instead of turning the complete wound graphic into a fluid blob.
The radial emitter is independent of the wound texture alpha so a transparent puncture center cannot
silence emission.

## Performance and Update Budget

Defaults are deliberately conservative:

- simulation resolution capped at 512 on each axis;
- simulation update at 20-30 Hz;
- composed output at 10-15 Hz while visibly changing;
- posed surface field at 5-10 Hz;
- one simulation context per atlas;
- one output texture per modified channel;
- Albedo only unless the profile opts into compatible extra channels;
- dirty rectangles for restoration/composite where the platform supports them;
- one mip generation after a composite batch or final settle, never once per effect;
- pooled command buffers, meshes, property blocks, lists, and render textures;
- no per-frame allocations, texture readbacks, UMA rebuilds, or per-effect materials.

Off-screen avatars may be stepped and composited at a reduced rate. Completed holding effects consume no simulation work. An empty controller consumes no GPU resources.

## UMA Lifecycle

- `CharacterBegun`: pause simulation, restore bound base textures, and release generation-specific outputs.
- `OnAtlasUpdated`: collect slot/overlay/channel metadata only; never simulate or replace the active atlas from this callback.
- `CharacterUpdated`: resolve final generated materials and immutable base atlases, recreate contexts, and replay surviving dynamic records when configured.
- undo/rebuild: clear active bleeding by default; an explicit persistence option may replay effects.
- disable/destroy: restore source material bindings and release every owned texture, mesh, buffer, command buffer, and material.

## Detached Pieces

Profiles select one of three routes:

- `SourceBody`: inject only into the source avatar.
- `SharedAtlas`: draw once into a shared generated atlas so source and detached renderers see the same result.
- `IndependentDetachedPiece`: clone only the detached renderer's required materials and runtime outputs, owned by `DismemberedPiece`, then simulate independently.

The independent mode is advanced because detached renderers normally share the source generated materials. Ownership must be explicit so undo and cleanup cannot leak cloned materials or RTs.

## Non-Compute Fallback

Unsupported platforms use a bounded skinned surface ribbon/trail:

- starts from the same cut origin;
- grows in gravity using a small fixed segment pool;
- uses a shared material plus `MaterialPropertyBlock` for color and fade;
- performs no UMA rebuild;
- has a strict segment/lifetime cap;
- may optionally settle into one dynamic RT stamp when RT drawing is supported.

The fallback is intentionally less physically accurate, but remains deterministic, bounded, and removable.

## Implementation Phases

1. Preserve this plan and add runtime settings/handle/profile types.
2. Implement the isolated atlas binding/compositor and generic dynamic stamp fade.
3. Extend the mesh builder and dismemberment result with durable cut-surface loops.
4. Add UV source-mask rasterization and atlas/material resolution.
5. Add posed GPU surface-field generation plus throttled fallback.
6. Add fixed-step conservative shallow-film compute simulation.
7. Add validated seam links, dirty bounds, batching, mip control, and diagnostics.
8. Integrate the component and profiles into dismemberment callbacks and sample documentation.
9. Add lifecycle, undo, detached-piece ownership, and non-compute fallback behavior.
10. Add unit/editor tests and compile every affected assembly.

## Acceptance Criteria

- Existing permanent UMA RT decals render identically with no runtime controller present.
- A controller with no active effects does not allocate RTs or replace material textures.
- Blood originates on the actual selected cut loop and follows gravity as the pose changes.
- Standalone fluid originates inside the successful decal footprint and respects the decal alpha.
- Distances and rates remain consistent when using Unity's one-unit-per-meter convention.
- Flow never crosses empty UV space or an unrelated clothing/overlay island.
- Normal flow/fade produces no UMA character rebuild and no CPU texture readback.
- Multiple effects on one atlas batch into shared simulation/composite resources.
- `StopFlow`, `FadeNow`, `Clear`, rebuild, undo, disable, and destruction all leave valid original atlas bindings.
- Permanent decals and dynamic decals coexist; clearing/fading a dynamic effect does not erase a permanent stamp.
- The compute-disabled fallback remains removable and does not rebuild UMA.
- Runtime/editor/sample assemblies compile cleanly and focused mesh/lifecycle/resource tests pass.
