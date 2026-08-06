# Overlay Painter: Runtime Replay and Overlay Composition Plan

**Status:** Deferred follow-on; planning only  
**Prepared:** 2026-08-05  
**Unity baseline:** Unity 6.3 and newer  
**Dependency:** Complete and release-gate `OVERLAY_PAINTER_IMPLEMENTATION_PLAN.md` before beginning this plan

## 1. Purpose

Add a supported runtime API that can load one or more prepared Overlay Painter project documents in a player, reproduce their texture results, combine compatible projects in a caller-defined order, and return transient UMA overlays ready to add to a runtime recipe.

This work is deliberately outside the current Overlay Painter release phase. It must not broaden or delay the editor authoring, persistence, export, performance, or release work already defined in `OVERLAY_PAINTER_IMPLEMENTATION_PLAN.md`.

## 2. Product contract

The runtime feature must:

- Accept saved Overlay Painter project documents that were explicitly prepared for runtime use.
- Work without `UnityEditor`, `AssetDatabase`, texture importers, editor shader inspection, or writable project assets.
- Reproduce the saved raster result deterministically from cached project data. Replaying individual input events is not required for the first release.
- Combine compatible projects by appending their layer contributions in caller-specified order.
- Preserve logical-channel semantics and produce physical textures in exact `UMAMaterial.MaterialChannel` order and packing.
- Treat every UDIM group as one logical input and return one correctly configured transient overlay per physical UDIM member.
- Return in-memory `Texture2D`, `OverlayDataAsset`, and `OverlayData` objects. Runtime replay does not create persistent Unity assets.
- Leave the source documents, source textures, materials, overlays, avatar, and recipe unchanged unless the caller explicitly applies the returned overlays.
- Expose deterministic validation errors before expensive GPU work begins.
- Own and dispose every generated runtime resource through an explicit result handle.

The first runtime release will support URP and HDRP on D3D11, D3D12, Vulkan, and Metal, matching the editor tool's certified matrix.

## 3. Current foundation and gaps

### 3.1 Runtime-capable foundation

- `TexturePaintDocument` and its surface, layer, mask, stroke, and compressed-pixel models are in `UMA.TexturePaint.Runtime`.
- `TexturePaintStageController`, `MeshReconstructor`, `TextureStore`, `TextureLayerCompositor`, fill generation, channel packing, and `TexturePaintBaker` are runtime code.
- `OverlayDataAsset` can be instantiated in memory, populated with textures and an `UMAMaterial`, and wrapped by `OverlayData` at runtime.
- The Runtime assembly has no platform exclusion and already references `UMA_Core`.

### 3.2 Required gaps

- Document checksum verification, decompression, target matching, and pixel restoration are currently coupled to editor-only document storage.
- There is no public runtime project loader or replay/combine service.
- The authoritative material layout/output descriptor is editor-only. Runtime fallback inference is not sufficient for deterministic custom packing.
- Saved documents do not yet carry a frozen, runtime-safe physical-output contract.
- There is no runtime resource asset that supplies required compute and raster shaders without `AssetDatabase` paths.
- There is no transient overlay factory, ownership handle, or recipe application helper.
- There is no defined policy for combining project bases, base edits, duplicate layers, incompatible surfaces, or different resolutions.
- Synchronous texture readback can stall a player and is unsuitable as the default runtime completion path.
- There are no Player tests for runtime document inclusion, Addressables loading, replay, packing, overlay use, cancellation, or disposal.

## 4. Architectural decisions

### 4.1 Prepared runtime projects

A normal authoring document remains the non-destructive source of truth. A document becomes eligible for runtime replay only after an editor-side preparation step validates it and writes a runtime manifest into or beside the document.

The prepared manifest must contain only runtime-serializable data and direct Unity object references. It must not contain asset paths or GUID-only dependencies.

Each prepared manifest records:

- Document ID, revision ID, schema version, and a preparation-format version.
- Direct references to the required `UMAMaterial`, slots, UDIM members, source overlays, source textures, brushes, masks, and pixel `TextAsset` blobs.
- Stable logical-target IDs and ordered physical member IDs.
- Geometry, topology, UV, material, and immutable-base fingerprints.
- Per-surface resolution and texture format.
- Ordered physical material channels.
- For every physical RGBA component: logical semantic, source component, inversion, neutral value, and whether it is authored or preserved.
- Color-space, normal-convention, and precision requirements needed during runtime baking.
- Required shader feature flags and minimum GPU capabilities.
- Layer ordering, group ownership, masks, opacity, blend mode, and cached raster references.
- A checksum covering the manifest and all referenced pixel payloads.

Preparation is refreshed when the document revision or any referenced source dependency changes. Builds fail validation when a referenced runtime project is stale.

### 4.2 Replay means restoring raster state

Version 1 runtime replay restores cached base/layer pixels and composites them. It does not re-run mouse strokes, spline tessellation, procedural plugins, or editor import operations.

This choice provides deterministic results and prevents runtime output from changing when brush, projection, plugin, or shader implementations evolve after a project was authored.

Stroke and spline metadata may remain in the document for future topology-aware rerasterization, but they are not part of the first runtime replay contract.

### 4.3 Runtime material contract

Do not perform general shader introspection in a player and do not move editor importer settings into runtime assemblies.

The editor preparation step compiles the current `UMAMaterial` descriptor into a compact runtime channel contract. Runtime code consumes that frozen contract to create logical targets and pack physical outputs. The manifest fingerprint ensures the contract still matches the referenced material and shader.

### 4.4 Project combination semantics

Projects are combined in request order. Layers from each project retain their internal order and are namespaced by document ID so logical layer IDs cannot collide.

All projects in one operation must resolve to the same logical slot or exact UDIM group, use compatible geometry/UV fingerprints, and share an equivalent physical material contract.

The initial base policy is strict:

- All projects must have the same immutable-base fingerprint.
- The common base is restored once.
- Project layer contributions are appended in request order.
- Documents containing direct base-channel edits are rejected unless every project has byte-identical base pixels.
- A later API version may add explicit `UseFirstProjectBase` or externally supplied base policies; no ambiguous behavior is inferred silently.

Project-level opacity and enable/disable controls are supported. Project-level blend mode is deferred because applying one blend mode uniformly to heterogeneous logical channels can be semantically incorrect.

### 4.5 Transient UMA output

Runtime output is never registered with `UMAAssetIndexer` as a persistent project asset. The result owns transient objects:

- One physical `Texture2D` per `UMAMaterial.MaterialChannel` and UDIM member.
- One transient `OverlayDataAsset` per ordinary slot or UDIM tile.
- Optional `OverlayData` instances constructed from those assets.
- Temporary render textures, materials, buffers, and readback state used during replay.

The result implements `IDisposable`. Disposal destroys only objects created by the operation and never destroys referenced project inputs.

## 5. Proposed public runtime API

Names may be refined during the API-contract checkpoint, but responsibilities should remain stable.

```csharp
public sealed class OverlayPainterRuntimeResources : ScriptableObject
{
    public ComputeShader layerComposite;
    public ComputeShader channelPack;
    public Shader fillGenerator;
    public Shader runtimeCopy;
}

public sealed class OverlayPainterProjectInput
{
    public TexturePaintDocument document;
    public bool enabled = true;
    public float opacity = 1f;
}

public sealed class OverlayPainterReplayRequest
{
    public IReadOnlyList<OverlayPainterProjectInput> projects;
    public OverlayPainterRuntimeResources resources;
    public int outputResolution;
    public bool generateMipMaps = true;
    public bool createOverlayDataInstances = true;
}

public static class OverlayPainterRuntime
{
    public static OverlayPainterValidationResult Validate(
        OverlayPainterReplayRequest request);

    public static Task<OverlayPainterReplayResult> ReplayAsync(
        OverlayPainterReplayRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class OverlayPainterReplayResult : IDisposable
{
    public IReadOnlyList<OverlayPainterRuntimeOverlay> overlays;
    public IReadOnlyList<OverlayPainterRuntimeDiagnostic> diagnostics;
}

public sealed class OverlayPainterRuntimeOverlay
{
    public SlotDataAsset slot;
    public int udimTile;
    public OverlayDataAsset asset;
    public OverlayData overlay;
    public IReadOnlyList<Texture2D> textures;
}
```

Additional API requirements:

- `Validate` performs no texture allocation or GPU dispatch.
- `ReplayAsync` must be called from Unity's main thread; internal compression/checksum work may use worker threads.
- Cancellation is honored between decompression, upload, composition, packing, readback, and overlay creation stages.
- Progress is exposed through an optional callback or `IProgress<OverlayPainterRuntimeProgress>`.
- Diagnostics use stable codes and severities suitable for UI, logs, and automated tests.
- The core replay API does not mutate a recipe. A separate UMA extension may apply returned `OverlayData` instances explicitly.

## 6. Runtime processing pipeline

```text
Load document(s) through direct reference / Addressables / caller
        |
        v
Validate preparation version, checksum, dependencies, target, base, and material contract
        |
        v
Create one logical runtime texture set for the ordinary slot or UDIM group
        |
        v
Restore the common base and cached layer pixels
        |
        v
Append compatible project layers in request order
        |
        v
Composite logical channels on GPU
        |
        v
Pack physical UMAMaterial channels from the frozen runtime contract
        |
        v
Async GPU readback -> Texture2D creation -> optional mip generation
        |
        v
Create transient OverlayDataAsset(s) and OverlayData instance(s)
        |
        v
Return disposable result; caller may add overlays to a runtime recipe
```

No stage, preview scene, editor window, `AssetDatabase`, importer, or disk write participates in this pipeline.

## 7. Work phases

### Phase 0 — contract and spike

- Freeze the prepared-manifest schema, combination/base policy, ownership rules, and public API shape.
- Prove that a transient `OverlayDataAsset` containing generated `Texture2D` objects can be added to a real runtime UMA recipe and survives repeated avatar rebuilds while its result handle remains alive.
- Prove URP and HDRP physical channel assignment with one simple albedo project and one packed mask-map project.
- Measure synchronous and asynchronous readback on 1K, 2K, and 4K outputs.

**Checkpoint:** approve API, transient-object lifetime, and exact meaning of multi-project combination before production implementation.

### Phase 1 — shared document codec

- Move checksum verification, Deflate decompression, raw texture validation, and pixel upload into a runtime-safe codec.
- Keep editor capture, file creation, AssetDatabase bookkeeping, and recovery transactions editor-only.
- Add corruption, decompression-bomb, unsupported-format, truncated-data, and checksum tests.
- Enforce configured limits for dimensions, channel count, layer count, and uncompressed bytes before allocation.

**Checkpoint:** load and restore a prepared document's cached pixels in PlayMode without editor storage classes.

### Phase 2 — runtime material manifest and preparation

- Define serializable runtime physical-channel/component mappings.
- Compile the editor `UMAMaterial` descriptor into the runtime manifest.
- Store direct dependency references and fingerprints.
- Add **Prepare for Runtime Replay** and stale-status reporting to the document inspector or project settings workflow.
- Add a build validator for referenced but stale/unprepared documents.
- Ensure Addressables and AssetBundle dependency collection includes every pixel blob and referenced source asset.

**Checkpoint:** inspect prepared URP/HDRP manifests and verify byte-for-byte agreement with editor export plans.

### Phase 3 — single-project replay

- Implement validation and runtime texture-set allocation.
- Restore cached base and layer pixels.
- Restore or consume prepared layer masks without editor dependencies.
- Composite logical channels and pack physical channels from the frozen contract.
- Add cancellation, progress, structured diagnostics, and failure-safe cleanup.

**Checkpoint:** compare runtime replay textures against editor export goldens for the same project.

### Phase 4 — multi-project combination

- Validate exact target, UDIM membership, base fingerprint, and material-contract compatibility.
- Namespace imported layer IDs by document ID.
- Append project layers in caller order with project enable/opacity.
- Reject conflicting base edits and unsupported procedural/plugin content with actionable diagnostics.
- Guarantee that a failed later project leaves no partial output or leaked resources.

**Checkpoint:** combine two and three independently authored projects and confirm order-dependent results against editor reference composites.

### Phase 5 — runtime UMA overlay factory

- Create physical `Texture2D` outputs in material-channel order.
- Create transient `OverlayDataAsset` and `OverlayData` instances with correct material, names, texture arrays, blend arrays, tags, and UDIM tile association.
- Add an optional recipe application extension that requires an explicit target slot/recipe.
- Define ownership when the avatar retains `OverlayData` after result disposal; require transfer or cloning rather than hidden lifetime extension.
- Verify repeated avatar rebuild, removal, replacement, and disposal behavior.

**Checkpoint:** apply ordinary and UDIM runtime overlays to representative UMA recipes and rebuild avatars repeatedly without missing textures or leaks.

### Phase 6 — performance, caching, and build delivery

- Use `AsyncGPUReadback` where supported and provide a clearly diagnosed fallback.
- Cache decompressed immutable blobs and prepared GPU inputs by document revision/checksum.
- Bound cache memory and expose explicit eviction.
- Avoid reconstructing preview GameObjects and colliders when a lightweight headless replay surface is sufficient.
- Add `OverlayPainterRuntimeLibrary` for direct-reference projects and document Addressables loading examples.
- Strip editor authoring/recovery content while retaining explicitly referenced prepared runtime projects.
- Profile main-thread time, GPU time, allocations, peak memory, and disposal.

**Checkpoint:** approve performance budgets and cache behavior on target desktop hardware.

### Phase 7 — platform certification and documentation

- Run Player tests under URP and HDRP on D3D11, D3D12, Vulkan, and Metal.
- Validate domain/subsystem reset, scene changes, application shutdown, graphics-device loss where testable, and canceled operations.
- Publish runtime API documentation, Addressables example, recipe application example, lifetime rules, and diagnostics reference.
- Add runtime replay to the release gate only after the feature is independently shippable.

## 8. Validation matrix

Automated tests must cover:

- Prepared-manifest migration and stale detection.
- Direct-reference and Addressables project loading.
- Checksum, truncation, malicious-size, missing dependency, and unsupported format failures.
- Single-project replay matching editor-export golden textures.
- Flat and triplanar fills, paint layers, groups, masks, blend modes, channel opacity, normals, and packed maps.
- Project ordering, enable/opacity, duplicate IDs, base conflicts, incompatible slots, UVs, materials, and resolutions.
- Ordinary slots and multi-tile UDIM groups.
- URP metallic/smoothness and HDRP mask/detail packing.
- Cancellation at every processing stage with zero surviving owned resources.
- Repeated replay/cache hit/disposal cycles and bounded memory.
- Runtime overlay recipe application and repeated avatar rebuild.
- Player builds using D3D11, D3D12, Vulkan, and Metal.

Manual validation must include:

- Combining clothing material projects such as fabric base, wear, dirt, and decals in different orders.
- Loading the same projects from Addressables after scene startup.
- Applying generated overlays, changing wardrobe, rebuilding the avatar, removing the overlays, and disposing the result.
- 1K, 2K, and 4K outputs with profiler captures.
- Missing or stale project content producing a useful error rather than white textures, partial overlays, or silent fallback packing.

## 9. Performance and resource budgets

Budgets must be measured and approved during Phase 0, then enforced by tests. At minimum:

- No unbounded allocation based on document-provided dimensions or byte counts.
- No per-frame work after a replay operation completes.
- No synchronous GPU readback on the normal supported path.
- No leaked `Texture2D`, `RenderTexture`, `Material`, `ComputeBuffer`, transient `OverlayDataAsset`, or reconstruction object after cancellation/failure/disposal.
- A documented peak-memory estimate is available before dispatch and validation can reject requests exceeding a caller-supplied budget.
- Cache entries are keyed by document revision, manifest checksum, resolution, graphics format, and material contract.

## 10. Packaging and loading rules

- A Unity player cannot load an arbitrary project `.asset` by filesystem path. Projects must be included by direct serialized reference, Resources, AssetBundle, Addressables, or a future external interchange format.
- Addressables are recommended for optional/customization content but are not required by the replay core.
- Recovery assets and their data folders are never runtime project inputs and remain excluded from builds.
- Prepared project dependencies must be explicit so Unity build stripping cannot remove shaders, materials, textures, slots, or data blobs.
- Runtime shaders are supplied by `OverlayPainterRuntimeResources`; no string asset path lookup is allowed.
- External/user-downloaded project files are deferred until a versioned, bounded, non-Unity-object interchange format and security review exist.

## 11. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Editor and runtime packing diverge | Freeze the editor-resolved physical contract in the prepared manifest and compare against editor export goldens. |
| Runtime projects become stale | Fingerprint document revision and dependencies; block preparation/build/replay with stable diagnostics. |
| Combining bases produces undefined results | Require an identical base fingerprint and reject conflicting base edits in API v1. |
| Generated textures are destroyed while UMA still uses them | Make ownership transfer explicit and test avatar rebuild/removal/disposal sequences. |
| Large projects stall or exhaust memory | Preflight sizes, async readback, cancellation, bounded caches, and caller memory budgets. |
| Plugins are unavailable in players | Replay cached raster pixels; do not execute authoring plugins in API v1. |
| UDIM members are combined or ordered incorrectly | Store exact group/member IDs and tile numbers in the manifest and validate the complete group. |
| Shader variants are stripped | Reference shaders through a runtime resources asset and validate required variants in Player tests. |

## 12. Definition of done

Runtime replay is complete only when:

- A prepared Overlay Painter project can be loaded in a clean player without any editor assembly.
- One project reproduces editor export output within the approved per-channel tolerance.
- Multiple compatible projects combine in caller-defined order under the documented base policy.
- Physical textures exactly follow the frozen `UMAMaterial` channel/component contract.
- Ordinary and UDIM results create valid transient UMA overlays and can be applied to runtime recipes.
- The caller can cancel and dispose operations without leaks or partial recipe mutation.
- Direct-reference and Addressables workflows are documented and tested.
- URP/HDRP pass on the required D3D11, D3D12, Vulkan, and Metal matrix.
- Performance and memory budgets pass on target hardware.
- The current editor Overlay Painter release remains behaviorally and structurally unaffected.

