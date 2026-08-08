# Overlay Painter: Release-Phase Implementation Plan

**Status:** Phases 0–4 implemented; Manual Checkpoint 3 in progress; Phases 5–7 rebaselined for closure
**Prepared:** 2026-08-04; rebaselined 2026-08-07
**Unity baseline:** Unity 6.3 and newer  
**Source review:** `OVERLAY_PAINTER_RELEASE_PHASE_PLAN.md` plus the approved product decisions dated 2026-08-04

## 1. Purpose

This document converts the release-phase audit and the approved decisions into an executable implementation plan. It supersedes the unresolved recommendations in Sections 4 and 5 of the release-phase plan where the approved decisions differ from them.

The implementation must deliver a non-destructive Overlay Painter document, a standalone slot-first workflow that never needs an avatar, deterministic UMA material/channel packing, and exported `OverlayDataAsset` assets that are registered in the UMA library and ready for use in recipes.

This is an implementation plan only. It does not authorize feature code changes by itself.

## 2. Approved product contract

These requirements are locked unless a later decision explicitly changes them.

### 2.1 Editing ownership and output

- The paint document is the editable, non-destructive project source of truth.
- Export never flattens, replaces, saves, marks clean, or otherwise changes the current document.
- Export creates physical textures in the format and component packing required by the selected `UMAMaterial` and its shader contract.
- Export creates correctly configured `OverlayDataAsset` output, using the selected `UMAMaterial`, with its `textureList`, channel order, names, and material fields ready for use in a recipe.
- Every exported `OverlayDataAsset` is added to the UMA global library/index and verified as discoverable there.
- Exported asset names are based on the source texture name plus a user-visible export identifier. Deterministic fallbacks are used when no source texture exists.
- The current/source `OverlayDataAsset` and its source textures are never changed by default.
- Overwriting a source `OverlayDataAsset` is a separate, explicit export mode with a conflict preview, confirmation, backup, and failure rollback.
- This phase does not automatically change the current avatar, recipe, wardrobe recipe, or source overlay reference.

### 2.2 Document lifecycle

- Opening the stage starts a temporary, recoverable session.
- Opening or canceling before an edit dirties no scene, avatar, recipe, slot, material, or overlay. Once edited, the temporary session writes a configurable project recovery asset and data files.
- **Save As** creates the first project document asset at a location chosen by the user.
- A saved document remains editable and supports continued non-destructive work.
- Temporary and saved sessions both have crash recovery. The temporary recovery location defaults to `Assets/UMA/Temp`, is configurable in UMA Settings, and should be excluded from source control and builds.

### 2.3 Slot-first launch

- Slot-first launch is required for shipping.
- The `SlotDataAsset` inspector contains an **Open in Overlay Painter** action.
- This action must not search for, generate, or require a `DynamicCharacterAvatar`.
- Setup asks the user to choose either an `UMAMaterial` or an `OverlayDataAsset`.
- Selecting an `UMAMaterial` creates an editable white default Fill layer for the first physical material texture. The user may modify or delete it.
- Selecting an `OverlayDataAsset` uses its textures as immutable base sources and uses its `UMAMaterial` as the authoritative material.
- Selecting any member of a UDIM group resolves every member by exact `udimGroupId`, opens them together, and presents them as one logical target.
- Non-UDIM slots remain individual logical targets.
- UDIM membership, painting, selection, framing, saving, and exporting are logically unified while physical meshes and textures remain separate backend resources.

### 2.4 Supported pipelines and graphics APIs

- Certified render pipelines: URP and HDRP.
- Certified graphics APIs: D3D11, D3D12, Vulkan, and Metal.
- D3D11 defines the minimum feature baseline.
- The actual shader/material selected through the `UMAMaterial` is used for preview and capability validation.
- Built-in Render Pipeline is not a certified release target for this phase. If it remains functional, it is labeled best-effort rather than silently implied to be supported.
- Addressables are not required for the primary export contract. They may remain an optional post-export integration if isolated and tested.

## 3. Verified current-state deltas

| Area | Current state | Required change |
|---|---|---|
| Stage launch | `TexturePaintStageWindow.ShowStage` and reconstruction require a generated `DynamicCharacterAvatar`. | Add a standalone slot launch context and reconstruction path. The slot entry point must never enter avatar discovery. |
| Document creation | `TexturePaintDocumentStorage.OpenOrCreate` immediately creates permanent assets below the tool's `Documents` folder. | Replace initial permanent document creation with a transient session, configurable temporary recovery asset, and explicit Save As. |
| UDIM metadata | `SlotDataAsset` has group ID/name/tile fields and logical-target reconstruction already groups generated slots. | Add deterministic project-asset group resolution and standalone reconstruction for every group member. |
| Channel semantics | Editor-only `MaterialChannel.textureChannelLayout` supports Automatic and Custom R/G/B/A semantic mappings. `TextureStore` uses the effective layout. | Use this contract in export preflight and physical packing; add output/import format metadata that the current layout does not describe. |
| Export | Physical/logical textures and an overlay can be created, but options still imply recipe updates and material overrides. | Make physical UMA output the release path, remove implicit recipe mutation, use the selected material, and make source overwrite exceptional. |
| UMA library | Exported overlays are not guaranteed to be registered and verified in `UMAAssetIndexer`. | Add library registration to the export transaction and fail visibly if the final overlay is not discoverable. |
| Performance | Saves perform full-resolution synchronous readback/compression and painting still has identified allocation/stall risks. | Implement the persistence and paint-loop work from the release audit before certification. |

## 4. Target architecture

```text
SlotDataAsset inspector
        |
        v
Standalone setup (UMAMaterial OR OverlayDataAsset)
        |
        v
UDIM group resolver ----> validated slot/material/source manifest
        |
        v
Standalone mesh reconstruction + material preview
        |
        v
Temporary recoverable session ---- Save As ----> project paint document
        |
        +---- continued non-destructive editing
        |
        v
Export preflight ----> physical texture packing ----> OverlayDataAsset output
        |                                              |
        +---- no document mutation                     v
                                                UMA library registration
```

The avatar-backed entry point may remain for existing users, but it is a separate launch-context implementation. It must not be invoked, searched, or synthesized by the slot-first path.

## 5. Core data and service contracts

### 5.1 Launch context

Introduce one validated launch-context model with explicit variants rather than adding unrelated `ShowStage` parameters.

The standalone slot variant stores:

- Selected slot asset GUID.
- Resolved slot GUIDs, slot names, UDIM group ID, and sorted tile numbers.
- Chosen source mode: `UMAMaterial` or `OverlayDataAsset`.
- Selected `UMAMaterial` GUID.
- Source `OverlayDataAsset` GUID per physical member when applicable.
- Requested initial logical target/channel and frame-on-open behavior.
- Temporary, recovered, or existing-document request.
- Working resolution policy.
- Stable source mesh/material/texture dependency fingerprints for stale-context detection.

Validation completes before stage navigation or asset mutation. Canceling setup has no side effects.

### 5.2 Session and document model

Split the concepts currently combined by `TexturePaintDocumentStorage`:

- `TexturePaintSession`: transient live editing state and resource ownership.
- `TexturePaintDocument`: saved project asset containing stable launch context, layer graph, settings, and persisted paint data.
- `TexturePaintRecoveryStore`: project-scoped `painter_recovery.asset` plus content-addressed data assets in the UMA Settings recovery folder.
- `TexturePaintDocumentPersistence`: incremental asynchronous serializer shared by recovery and saved documents.

The UI exposes `Temporary`, `Recovered`, `Saved`, `Modified`, `Saving`, and `Recovery failed` states. Save As uses `EditorUtility.SaveFilePanelInProject`; no hardcoded package/tool document folder is used.

### 5.3 Material capability and output contract

Create an editor service that compiles an immutable capability/export descriptor from the selected `UMAMaterial`, active pipeline material, shader properties, and every `MaterialChannel`.

For each physical material channel, the descriptor contains:

- UMA material-channel index and shader property name.
- Effective R/G/B/A semantics from `GetTextureChannelLayout`.
- Supported painter logical channels and semantic neutral values.
- Component inversion/conversion rules, including roughness/smoothness and normal convention.
- Source texture, source name, working resolution, and output resolution.
- Output image encoding and bit depth.
- Color space, texture importer type, alpha policy, mip policy, compression, and platform overrides.
- Shader-property existence and pipeline/API capability diagnostics.

The existing editor-only channel layout describes packing but does not yet define file encoding or importer setup. Add an editor-only Automatic/Custom export-settings block to `UMAMaterial.MaterialChannel`. Automatic settings are inferred from channel type, render-texture format, and effective semantics; Custom settings remain editable for custom shaders.

The preflight UI shows the compiled result as a physical-texture table before export. Property-name heuristics are allowed only in Automatic mode and are never allowed to silently override a Custom layout.

### 5.4 Export result set and UDIM consequence

A non-UDIM export creates one ready-to-use `OverlayDataAsset`.

Under UMA's current data model, an `OverlayDataAsset` contains one texture array for one physical slot usage; it does not encode different texture arrays for several UDIM member slots. Therefore, one logical UDIM export must create one `OverlayDataAsset` per physical UDIM member, while the UI presents them as one **Export Result Set**. Every member overlay uses the common `UMAMaterial`, contains that member's packed textures, is registered in the UMA library, and is named with its UDIM tile.

If the desired product contract is literally one asset for an entire multi-slot UDIM group, that requires a new composite overlay/bundle asset plus recipe integration and must be approved as a core UMA schema change before export implementation begins. The implementation must not hide several tiles inside an incompatible single `OverlayDataAsset`.

## 6. Phased implementation

### Phase 0 — freeze contracts and establish baselines

**Goal:** Convert the approved decisions into testable contracts and retain trustworthy before/after evidence.

Work:

1. Add the decisions in Section 2 to release-gate documentation and tracked work items.
2. Capture current output goldens for low-alpha paint, flat/texture/overlay fill, seams, UDIM boundaries, masks, normal channels, and export packing.
3. Capture stage-open, warm-paint, first-touch, autosave, close, export, memory, and allocation baselines at 2K and 4K.
4. Define reference URP and HDRP materials for every supported semantic layout, including URP/HDRP mask and detail maps.
5. Freeze provisional limits for working resolution, UDIM member count, layers, history, and GPU memory after profiling.
6. Repair release-gate result classification so compilation, infrastructure, timeout, crash, and assertion failures cannot be mistaken for success.

Deliverables:

- Approved data contracts and test fixtures.
- Baseline timing/memory report.
- Known-good visual and packed-texture goldens.
- Reliable clean-project release gate.

**Manual checkpoint 0:** Approve the per-UDIM-member overlay result model, material export metadata, naming contract, and performance budgets. Stop if any of these change the persistent schema or export transaction.

### Phase 1 — temporary sessions, Save As, and recovery

**Implementation status (2026-08-04):** Implemented; awaiting Manual Checkpoint 1 validation in Unity.

**Goal:** Make opening safe and make documents usable without blocking the editor or changing source content.

Work:

1. Introduce the session, document-persistence, and recovery services from Section 5.2.
2. Stop calling `OpenOrCreate` during stage initialization.
3. Create a temporary session in memory and start recovery only after successful reconstruction and an edit.
4. Store recovery as `painter_recovery.asset` with sibling content-addressed data assets in the folder configured in UMA Settings (default `Assets/UMA/Temp`), with schema version, checksums, last-complete revision, and source-context fingerprint.
5. Persist dirty targets/layers/tiles only. Stage bounded `AsyncGPUReadback` work across editor frames and compress off the main thread.
6. Add Save As, choose-project-path UI, existing-path validation, progress, cancellation boundaries, and failure recovery.
7. After Save As, persist ongoing edits incrementally to the project document; recovery remains supplemental and is retired only after a confirmed complete save.
8. Add a recovery chooser on launch when a compatible interrupted session exists: Recover, Discard, or Cancel.
9. Migrate existing documents without overwriting them; retain a backup and report unsupported schema versions clearly.
10. Ensure closing the controls window follows the existing stage-close confirmation contract and cannot orphan a session.

Acceptance:

- Opening and canceling before an edit create no recovery asset and dirty no scene/source object. Editing can create or update the configured temporary recovery assets.
- Save As is the first action that creates a permanent document asset chosen by the user.
- A forced Unity termination during recovery/save restores the last complete revision.
- Painting is not interrupted by autosave; no persistence main-thread slice exceeds the frozen budget.
- Exporting leaves document identity, revision, dirty state, undo history, and serialized bytes unchanged.

**Manual checkpoint 1:** Validate new temporary, recovered, Save As, saved/reopened, failed-save, and close-with-pending-save flows before changing reconstruction.

### Phase 2 — material capability and physical packing contract

**Implementation status (2026-08-04):** Implemented and covered by focused Unity EditMode tests; final URP/HDRP project-matrix certification remains part of the release gate.

**Goal:** Make shader/material interpretation deterministic before standalone slot previews or export depend on it.

Work:

1. Add editor-only per-`MaterialChannel` output/import settings with Automatic and Custom modes.
2. Build the material capability service and a reusable validation report.
3. Select the active URP/HDRP material through the `UMAMaterial`; never substitute a Standard/built-in shader in a certified workflow.
4. Validate shader properties, channel layout conflicts, unsupported multi-semantic components, missing textures, color space, texture formats, and compute/UAV support.
5. Route painter logical-channel creation, semantic defaults, preview binding, and export packing through the same compiled descriptor.
6. Add an inspector preview for each `MaterialChannel`: property, R/G/B/A meanings, output encoding, importer type, color space, and warnings.
7. Add explicit Custom authoring for shaders that cannot be inferred safely.
8. Preserve serialized compatibility for existing `UMAMaterial` assets; Automatic defaults must not alter runtime serialization outside the editor-only block.

Acceptance:

- One descriptor drives reconstruction, editing, preview, and export; these paths cannot disagree about channel meanings.
- Standard URP/HDRP Lit materials infer their documented mask/detail layouts correctly.
- Custom mappings round-trip and override inference.
- Unsupported shaders fail setup with actionable diagnostics before the stage opens.
- Tests verify inference without depending only on property-name string guesses.

### Phase 3 — standalone slot and UDIM reconstruction

**Implementation status (2026-08-04):** Implemented; Manual Checkpoint 2 passed.

**Goal:** Ship the required slot-first workflow with no avatar dependency.

Work:

1. Add **Open in Overlay Painter** to the existing Slot Utilities area of the `SlotDataAsset` inspector.
2. Add a standalone setup window with mutually exclusive `UMAMaterial` and `OverlayDataAsset` source modes, capability summary, resolution, and Open/Cancel actions.
3. Implement a UDIM resolver that finds all `SlotDataAsset` assets with the exact group ID, sorts by tile, and rejects duplicate tiles, missing members, empty IDs, and incompatible metadata.
4. Build `MeshReconstructor.ReconstructSlotGroup` (or an isolated equivalent) directly from `UMAMeshData`. Preserve vertices, indices, submeshes, normals, tangents, UVs, colors, bounds, and aligned slot-space transforms.
5. Create preview `MeshFilter`/`MeshRenderer`/collider objects without a skeleton or avatar. Use cloned editor preview materials derived from the selected `UMAMaterial`; never mutate the material asset.
6. Build the same logical-target, physical-surface, texture-set, hit-testing, selection, and frame-bounds contracts used by painting.
7. Lock a UDIM group as one logical target in ordinary UI. Keep a diagnostic member/tile view without enabling accidental independent painting.
8. In `UMAMaterial` source mode, create `Default White` as a real, editable flat Fill layer affecting the logical components stored in the first physical material channel. Initialize other required components to semantic neutral values and warn if the first channel is not a color/albedo channel.
9. In `OverlayDataAsset` source mode, validate its material and channel count and retain its textures as immutable base references until first edit.
10. For a UDIM group, expose a member-source table after the initial overlay selection. Seed the selected member with the chosen overlay; allow one compatible source overlay per other member, and use clearly labeled semantic-neutral bases for missing members. Do not guess companion overlays silently.
11. Serialize the complete standalone context into recovery and saved documents using stable asset GUIDs and source fingerprints.
12. Keep avatar-backed launch functioning through its own launch-context variant, but share downstream session and painting services.

Acceptance:

- The slot button opens the selected non-UDIM slot without finding or generating an avatar.
- Selecting one UDIM member loads, selects, frames, paints, saves, and exports the complete group as one logical unit.
- Cancel, invalid setup, and failed reconstruction leave the current stage and assets unchanged.
- Selected material, shader, base textures, channel catalog, and bounds are visible and correct.
- Source asset changes are detected on reopen and produce refresh/rebind choices rather than silent changes.
- A material-only session opens with the required removable white Fill layer.

**Manual checkpoint 2:** Validate representative body, clothing, non-UDIM, and multi-tile UDIM slot assets in URP and HDRP. Confirm alignment, camera framing, material preview, source texture orientation, layer setup, seam painting, and the per-member overlay-source workflow before implementing final export.

### Phase 4 — transactional export and UMA library output

**Implementation status (2026-08-07):** Implemented; flattened non-UDIM and UDIM export has passed the current manual texture inspection, including native source reconstruction and normal-map output. Active-avatar geometry is split back into native slot surfaces, UVs are restored from each `SlotDataAsset`, and channel sources are rebuilt at native base-texture resolution from the original `MaterialFragment` overlay stack. `resultingAtlasList` is prohibited as an Overlay Painter source. Imported and transiently packed normal inputs are decoded to linear tangent-space RGB before compositing/export. The new **Runtime Overlay (Transparent)** mode and generated `OverlayDataAsset.alphaMask` still require one real-recipe runtime validation before Manual Checkpoint 3 is complete. Do not treat automated export coverage as a substitute for that recipe validation.

Automated checkpoint: the focused Unity 6.3/D3D11 export suite passes 6/6, including default source preservation, UMA index lookup, cancellation rollback, overwrite-source restoration, deterministic naming, and whole-group UDIM expansion. Descriptor precision and physical packed-map round trips pass 4/4 in the release integration suite.

**Goal:** Produce deterministic, recipe-ready UMA overlay assets without changing the document or source assets.

Work:

1. Replace the primary export plan with one physical output texture per `UMAMaterial.MaterialChannel`, in exact material-channel order.
2. Pack R/G/B/A using the compiled material descriptor. Apply semantic conversions only once and fill unused/missing components with declared neutral values.
3. Stage GPU readback, encoding, file writes, imports, overlay creation, and library registration with progress and cancellation between safe boundaries.
4. Require or prominently expose an **Export Identifier**. Default it from the document/session name and a short timestamp, but let the user edit it.
5. Use this naming policy:
   - Texture: `<SourceTextureStem>_<Identifier>`.
   - Texture fallback: `<SlotOrGroup>_<MaterialChannelName>_<Identifier>`.
   - UDIM texture: append `_<TileNumber>` before the extension.
   - Overlay: `<SlotOrGroup>_<Identifier>[_<TileNumber>]_Overlay.asset`.
   - Sanitize names and preview every final path before mutation.
6. Configure importers from the material export descriptor, not from a global physical-texture assumption. Reimport once per asset and avoid `SaveAssets` inside per-texture loops.
7. Create a new `OverlayDataAsset` by default. Set its exact selected `UMAMaterial`, material name, physical texture array, texture names, rect, and required channel/blend data.
8. For UDIM, create and display one member overlay per tile as a single Export Result Set.
9. Register every completed overlay through the supported `UMAAssetIndexer` API, persist the index, and verify lookup by type/name before reporting success.
10. Remove/disable implicit recipe/avatar updates and default material-override creation from the release workflow. They may only return later as separate, explicitly scoped features.
11. Implement **Overwrite Source Overlay** as a separate mode available only when the source is a persistent overlay. Show the exact asset and textures affected, require confirmation, create recoverable backups, use Undo where Unity asset semantics allow it, and restore all affected files/index entries on failure.
12. Make the export transaction failure-atomic from the user's perspective. Preflight all paths and conflicts first; stage new files; import and validate; create overlays; register; then commit the result. Clean temporary artifacts on success or failure.
13. Store export history outside the paint document, or in non-document editor state, so export cannot mutate the editable document.
14. Provide two explicit, non-mutating content modes. **Flattened Composite** exports the reconstructed native-resolution source plus visible authored layers. **Runtime Overlay (Transparent)** composites only visible layer/group content over transparent temporary targets, emits only authored physical material channels, generates a union coverage texture, assigns it to `OverlayDataAsset.alphaMask`, and prohibits source-overlay overwrite.

Acceptance:

- Exported physical textures decode to the previewed R/G/B/A values within the approved tolerance.
- A newly exported overlay is discoverable in the UMA library and can be selected in a recipe without repair.
- Reopening a recipe with the overlay reproduces the Overlay Painter preview.
- Default export leaves every source asset byte-identical.
- Runtime-overlay export contains no reconstructed base pixels, assigns its generated coverage texture to `OverlayDataAsset.alphaMask`, leaves untouched material channels null, and blends correctly when added after the intended UMA base overlay.
- Export leaves the document revision, dirty state, content hash, and Undo stack unchanged.
- Cancel/failure leaves no half-configured overlays, broken index entries, or unreported temporary files.
- Explicit overwrite changes only the preflight-listed assets and restores them on injected failure.

**Manual checkpoint 3:** Export non-UDIM and multi-tile UDIM results, add them to real recipes, rebuild characters, restart Unity, and verify library persistence, material assignment, texture orientation, packing, naming, and source-asset preservation.

Blocking source-quality invariant: an active-avatar session may use generated material metadata to locate slots, overlay stacks, and shader contracts, but it must never read `GeneratedMaterial.resultingAtlasList`. Every editable base channel must come from an original overlay-stack reconstruction at the slot's native base-texture resolution.

### Phase 5 — measured performance and resource closure

**Implementation status (2026-08-07):** Partially implemented. Dirty-region synchronization, sparse exact-format history, bounded coverage, asynchronous persistence readback, batched path dispatch, cached mesh maps, latency counters, configurable budgets, and lifecycle tests are present. The work below is the remaining closure scope and supersedes the broader Phase 5 wording in older readiness plans.

**Goal:** Measure the current feature-complete editor, freeze one authoritative performance contract, and close the remaining interactive and resource hot paths without weakening correctness.

#### Phase 5A — rebaseline and freeze budgets

1. Complete Manual Checkpoint 3 before performance refactoring changes export or resource ownership assumptions.
2. Name the reference Windows and macOS hardware, Unity patch version, pipeline, graphics API, project, texture formats, and workload fixtures used for performance sign-off.
3. Measure these scenarios independently at 2K and 4K where applicable:
   - Single-channel and three-channel freehand painting.
   - Mask painting, polygon fill, and UV-island fill.
   - First spline segment, later point insertion, point dragging, width changes, and layer-effect changes.
   - Nested groups, multi-channel layers, and distance-based layer effects.
   - Same-island, physical-slot, and UDIM-boundary strokes.
   - 2D window open and closed, target switching, undo/redo, autosave, Save As, and repeated open/close.
4. Capture editor main-thread time, render-thread/GPU time where available, managed allocation, dispatch count, copied/composited/packed pixels, persistence work, live GPU bytes, cache hits/misses, and resource high-water marks.
5. Resolve the conflicting provisional budgets in older plans and record one approved P95/P99/maximum, allocation, memory, and persistence budget table in this document and the release gate. Until that table is approved, numeric thresholds are diagnostic rather than release sign-off.

#### Phase 5B — interactive hot-path optimization

1. Batch freehand stroke samples produced during one editor update and preserve deterministic spacing, pressure, fade, taper, splatter, randomization, projection, and cross-surface routing.
2. Coalesce layer composition, physical packing, material binding, texture-change notification, 2D invalidation, and window repainting to at most once per dirty target/channel/editor update unless correctness requires an explicit immediate flush.
3. Retain deferred spline rerasterization while dragging or changing continuous controls. Commit history and perform the expensive final rerasterization once at the interaction boundary.
4. Cache all compute kernel IDs and shader capability decisions outside dispatch paths. Cache or incrementally update flattened layer/effect evaluation data instead of rebuilding it for every stamp.
5. Reuse per-stroke grouping/contact lists, sample buffers, stamp arrays, and compute buffers. Remove owned warm-frame allocations, including diagnostic/UI allocations that occur while painting.
6. Keep geometry/island masks and distance-effect fields lazy, reusable, bounded, revision-aware, and instrumented. Prewarm only when profiling shows a first-touch pause that cannot be hidden safely.

#### Phase 5C — resource and fallback closure

1. Allocate editable base copies, channel composites, packed physical targets, layer targets, masks, tangent/seam maps, procedural maps, and isolated previews only when the active material or operation requires them.
2. Add centralized owned-resource accounting in bytes, including render textures, textures, compute buffers, history, coverage, cached maps, effect distance fields, and persistence staging. Expose current and peak values in diagnostics.
3. Add explicit cache budgets and deterministic eviction. Pool history tiles and transient GPU resources only where profiling shows a measurable benefit and ownership remains unambiguous.
4. Replace full-target synchronous CPU paint fallback readbacks with dirty-region/tile work, or explicitly remove that fallback from certified configurations if parity and precision cannot be guaranteed.
5. Keep color sampling bounded to the requested texel and avoid allocating a new full-resolution intermediate. Reuse or asynchronously stage small readback resources when it improves measured interaction latency.
6. Profile standalone reconstruction by asset loading, mesh conversion, collider/spatial data, logical-target catalog, support maps, material capability compilation, and source binding. Cache only stable, fingerprinted results.
7. Prove deterministic disposal on target/document change, 2D window close, stage close, failed initialization, canceled persistence/export, assembly reload, and editor shutdown.

Acceptance:

- Every approved workload meets the frozen P95/P99/maximum budgets on its named reference hardware.
- TexturePaint-owned code allocates 0 managed bytes during a warm steady paint update, excluding explicitly documented Unity-owned allocations.
- Freehand and spline interaction perform no redundant compose/pack/bind/repaint cycle within one editor update.
- UDIM boundary cost remains within the approved multiplier of the equivalent same-island workload.
- Closing the 2D window removes its recurring update/repaint work and does not change 3D painting results.
- No monotonic GPU/managed growth appears across 20 open/close, target-switch, mask-edit, effect-edit, and undo/redo cycles.
- Certified GPU paths and any retained CPU fallback produce results within the approved precision tolerance.

**Manual checkpoint 4:** Review the frozen budget table and profiler/GPU-capture evidence for every Phase 5A workload. Any unexplained recurring spike, full-target hot-path readback, warm owned allocation, or resource growth is a NO-GO.

### Phase 6 — correctness, workflow, and usability hardening

**Implementation status (2026-08-08):** Most originally advertised editing work is implemented, including vector-aware normals, the painter-owned Normal Control height workflow, multi-channel layers and sources, grayscale layer masks, group compositing, layer effects, fill transforms, sprite-set workflows, synchronized 2D/3D editing, persistent workspace state, and contextual controls. This phase is now a hardening pass rather than a feature-construction phase.

**Goal:** Close ambiguity, parity, combination coverage, and presentation defects without adding another broad feature wave.

Work:

1. Prove vector-correct normal painting/composition/export for supported blends and explicitly disable any normal blend or merge operation that cannot be evaluated exactly.
2. Establish GPU/CPU parity for every retained fallback. If a fallback cannot preserve format, color space, masks, multi-channel behavior, or blending, fail with an actionable capability message instead of silently changing results.
3. Freeze one source/fill/sprite/path/persistence/export Y-orientation contract across every certified API and add round-trip goldens for it.
4. Add combination coverage for low alpha, UV wrap/seams, physical-slot and UDIM boundaries, nested groups, group masks, multi-channel sources, fill transforms, layer effects, mask effects, clone, projection, symmetry, undo/redo, Save As/reopen, and fill/spline invalidation.
5. Use one vocabulary throughout code-facing UI and documentation: Overlay Painter, logical target, UDIM group, physical slot, material channel, logical channel, layer channel, layer mask, document, and export result set.
6. Keep settings owned by the relevant brush, layer, layer channel, fill channel, spline, mask, or effect. Hide or disable inapplicable controls with an actionable explanation.
7. Complete the persistent context header so it can expose slot/group, tiles, material, pipeline, sources, working resolution, document state, current target, current layer/mask mode, and current channel without overwhelming narrow layouts.
8. Preserve independent 2D and 3D Before/view state, mask-mode labeling, Pick, wireframe, navigation, and editing behavior. Verify all tool modes in both views.
9. Complete keyboard, tooltip, focus/hot-control, Alt-navigation, narrow-dock, high-DPI, multi-monitor, error, warning, destructive-confirmation, and empty-state passes.
10. Choose and document one assembly/domain-reload contract: either restore/reopen the active workspace automatically, or exit safely and present a clear recovery/reopen path. Test the chosen behavior in a clean editor process.
11. Ensure controls-window, UV-window, stage, compilation, and Unity shutdown behavior cannot orphan resources or silently lose a temporary or saved session.
12. Certify Normal Control as an automatically available grayscale auxiliary channel whenever Normal
    exists: neutral preservation, raised/recessed orientation, vector-normal composition, group and
    layer parity, exact save/recovery, effective 2D/3D preview, flattened export, authored-overlay
    flat-relative normal output and alpha coverage, and single OpenGL/DirectX boundary conversion.

Acceptance:

- Automated goldens and integration tests pass for every supported operation and required combination, not only isolated features.
- UI never implies that an independent UDIM member can be painted or exported outside its logical group.
- Unsupported material/channel/tool/blend combinations are disabled before mutation with an actionable reason.
- Mask mode exposes scalar grayscale painting and mask-only effects, never ordinary material-channel or sprite/overlay source selection.
- Normal Control never binds to a shader property or exports as an independent texture; every preview
  and export path consumes the same normalized effective normal result.
- New and experienced users can complete slot-open, paint, mask, path, Save As, reopen, export, and recipe-use workflows without undocumented steps.
- The selected domain-reload behavior is deterministic, recoverable, documented, and covered by an isolated-process test.

### Phase 7 — pipeline/API certification and release candidate

**Implementation status (2026-08-07):** The local Unity 6.3/D3D11 gate passes with non-zero EditMode and PlayMode suites. This is foundational evidence, not completion of the required pipeline/API matrix.

**Goal:** Produce a clean Unity 6.3+ release candidate with an honest, reproducible support matrix and no development artifacts in the release payload.

Work:

1. Run preflight, EditMode, GPU golden, integration, stress, recovery, export, fresh-project, and isolated-process lifecycle tests in separate URP and HDRP matrix projects.
2. Certify D3D11, D3D12, Vulkan, and Metal on appropriate named hardware/agents. D3D11 remains the minimum capability configuration.
3. Validate shader compilation, kernels, UAV/precision formats, color space, normal conventions, importer settings, packing, layer/effect output, exported overlay assets, recipe use, and editor restart per matrix cell.
4. Run the complete slot-first-to-recipe workflow in clean Unity 6.3 URP and HDRP projects. The imported release must contain no generated documents, recovery data, exports, logs, local goldens, or user content.
5. Audit assembly boundaries, optional Addressables isolation, asset paths/GUID lookup, Plugin API v2 diagnostics, package size, `.meta` files, licenses, samples, and uninstall/reinstall behavior.
6. Re-run source-byte preservation and failure-injection cases for default export and explicit source overwrite on the release candidate.
7. Update and cross-check README, Overlay Painter guide, slot-first guide, document/recovery guide, material-channel authoring guide, export/overwrite guide, support matrix, performance limits, migration notes, troubleshooting, changelog, and known issues. Mark older planning documents historical or synchronize them so they cannot contradict this plan.

Release gate:

- Manual Checkpoints 3 and 4 are signed off with retained evidence.
- No open P0/P1 correctness, data-loss, performance, lifecycle, or export-library defect remains.
- Every required URP/HDRP and graphics-API matrix cell passes or the release is NO-GO.
- GPU tests execute on compute-capable release hardware; a skipped GPU suite is not passing evidence.
- Fresh-project slot-first to recipe use passes after an editor restart.
- No source asset is modified in the default workflow, and explicit overwrite rollback passes failure injection.
- No unexplained release-gate process failure, recurring allocation, resource delta, or frame spike remains.

## 7. Test matrix

### 7.1 Required platform matrix

| Pipeline | D3D11 | D3D12 | Vulkan | Metal |
|---|---:|---:|---:|---:|
| URP | Required | Required | Required | Required on macOS |
| HDRP | Required | Required | Required | Required on supported Metal hardware |

Each cell covers stage open, slot reconstruction, same-island and boundary painting, fills, masks, Save As/recovery, physical packing, overlay creation, library lookup, recipe use, and editor restart. Platform-specific exceptions require a product decision and documented support-matrix change; they cannot be silently skipped.

### 7.2 Core automated suites

- Launch-context validation with no avatar references or searches.
- UDIM exact-group resolution, sorting, duplicate/missing tile handling, and non-UDIM isolation.
- `UMAMeshData` conversion and bounds/topology fidelity.
- Material layout inference/custom overrides and export/import descriptor compilation.
- Semantic neutral values and first-channel white Fill behavior.
- Source overlay channel compatibility and per-member UDIM source binding.
- Temporary session, Save As, incremental persistence, corruption, migration, and crash recovery.
- Document non-mutation across successful, canceled, failed, and overwrite exports.
- Naming, collision policy, packing, encoding, importing, overlay configuration, rollback, and UMA library registration.
- Low-alpha, orientation, normal, seam, wrap, slot, UDIM, fill, mask, clone, undo/redo, and 2D-open/closed GPU goldens.
- Nested-group, group-mask, multi-channel-source, layer-effect, mask-effect, fill-transform, spline-invalidation, and save/reopen combination tests.
- Normal Control neutral/raised/recessed/vector goldens, automatic-channel and grayscale-source tests,
  document/recovery round trips, grouped previews, flattened physical normals, and authored-overlay
  normal-delta/coverage tests.
- Freehand per-update batching, coalesced compose/pack/bind/repaint, deferred spline rerasterization, and deterministic random brush evolution.
- Resource ownership, GPU-byte accounting, disposal, cache eviction, plugin filtering, dirty-region fallback, and warm-frame allocation assertions.
- Isolated-process coverage for the approved assembly/domain-reload behavior and recovery/reopen result.

### 7.3 Required manual scenarios

- Non-UDIM slot + `UMAMaterial` + default white Fill.
- Non-UDIM slot + source `OverlayDataAsset`.
- UDIM member + material-only sources.
- UDIM member + compatible per-member source overlays.
- Temporary work recovered after forced termination.
- Save As, close, reopen from document asset, continue editing, and export multiple identifiers.
- Default export followed by recipe use and source-byte comparison.
- Explicit overwrite with confirmation, injected failure, and restoration.
- URP/HDRP switching and unsupported/custom shader diagnostics.
- Narrow/wide docks, 2D window open/closed, mask/path/fill tool modes, Alt navigation, stage-close cancellation, high DPI, multi-monitor, domain reload, and editor restart.
- Phase 5A profiler workloads at 2K/4K with retained main-thread, GPU, allocation, dispatch, dirty-pixel, persistence, and owned-resource evidence.

## 8. Migration and compatibility

- Bump the document schema only when the session context and incremental storage format are finalized.
- Existing document assets are opened read-only until migration succeeds to a new asset or backup-backed replacement chosen by the user.
- Existing `UMAMaterial` assets default editor-only channel layout and export settings to Automatic, preserving runtime behavior.
- Existing avatar-first launch remains available but adopts the new temporary session, persistence, material capability, and export services.
- Existing export templates are migrated to non-destructive defaults. Recipe update and material override flags are not honored silently by the release workflow.
- Current source overlays and textures require no migration.
- Temporary recovery assets, generated documents, exports, test logs, and goldens remain outside release package content by validation rule.
- `OVERLAY_PAINTER_IMPLEMENTATION_PLAN.md` is the authoritative active release roadmap. Older readiness and release-phase plans are historical inputs unless explicitly synchronized with this document.

## 9. Work-item breakdown and dependencies

| ID | Priority | Work item | Depends on |
|---|---|---|---|
| TMP-CON-101 | P0 | Freeze launch, session, material, export, and UDIM result contracts | None |
| TMP-REL-101 | P0 | Reliable baseline/release-gate classification | None |
| TMP-DOC-101 | P0 | Transient session and explicit Save As | TMP-CON-101 |
| TMP-DOC-102 | P0 | Incremental async persistence and recovery asset | TMP-DOC-101 |
| TMP-DOC-103 | P0 | Document schema migration and source fingerprints | TMP-DOC-102 |
| TMP-MAT-101 | P0 | Material capability/export descriptor | TMP-CON-101 |
| TMP-MAT-102 | P0 | Editor-only channel output/import metadata and inspector | TMP-MAT-101 |
| TMP-SLOT-101 | P0 | Standalone launch context and SlotDataAsset inspector action | TMP-DOC-101, TMP-MAT-101 |
| TMP-SLOT-102 | P0 | Deterministic UDIM asset resolver | TMP-SLOT-101 |
| TMP-SLOT-103 | P0 | Standalone UMAMeshData reconstruction and logical catalog | TMP-SLOT-102 |
| TMP-SLOT-104 | P0 | Material/overlay setup and per-member source binding | TMP-SLOT-103, TMP-MAT-102 |
| TMP-EXP-101 | P0 | Physical export plan, packing, encoding, and naming | TMP-MAT-102, TMP-DOC-102 |
| TMP-EXP-102 | P0 | Overlay creation and UMA library registration | TMP-EXP-101, TMP-SLOT-104 |
| TMP-EXP-103 | P0 | Atomic transaction, rollback, and explicit overwrite | TMP-EXP-102 |
| TMP-PERF-100 | P0 | Freeze reference hardware, workloads, and authoritative performance/resource budgets | TMP-EXP-103, Manual Checkpoint 3 |
| TMP-PERF-101 | P1 | Lazy editable/composite/packed/map/mask/effect resources and centralized GPU-byte accounting | TMP-PERF-100, TMP-SLOT-103 |
| TMP-PERF-102 | P1 | Per-update freehand batching and coalesced compose/pack/bind/repaint | TMP-PERF-100, TMP-PERF-101 |
| TMP-PERF-103 | P1 | Kernel/graph/discovery caching and transient collection/GPU-resource reuse | TMP-PERF-102 |
| TMP-PERF-104 | P1 | Dirty-region CPU fallback, bounded color sampling, and reconstruction profiling | TMP-PERF-101 |
| TMP-PERF-105 | P0 | Profiler evidence and Manual Checkpoint 4 sign-off | TMP-PERF-101, TMP-PERF-102, TMP-PERF-103, TMP-PERF-104 |
| TMP-COR-101 | P1 | Normal/blend/orientation/fallback correctness and combination goldens | TMP-MAT-101, TMP-PERF-105 |
| TMP-UX-101 | P1 | Context, document, UDIM, layer/mask/effect, input, and error-state hardening | TMP-SLOT-104, TMP-EXP-103, TMP-PERF-105 |
| TMP-LIFE-101 | P0 | Approve and verify assembly/domain-reload and window/stage shutdown contracts | TMP-DOC-102, TMP-PERF-105 |
| TMP-TEST-101 | P0 | Automated release matrix and failure injection | All feature items incrementally |
| TMP-REL-102 | P0 | Fresh-project platform certification and release docs | All preceding items |

P0 here means required for the approved shipping contract, not merely a severity estimate.

## 10. Implementation stop points

Implementation should deliberately pause for user validation at these points:

1. **Checkpoint 0 — contracts:** confirm per-member UDIM overlay output, material export metadata, and naming.
2. **Checkpoint 1 — documents:** validate temporary/recovery/Save As before slot reconstruction changes.
3. **Checkpoint 2 — standalone slots:** passed; retain the mesh alignment, materials, source overlays, targets, and UDIM evidence.
4. **Checkpoint 3 — exported UMA assets:** in progress; validate real recipe use and library persistence before performance refactoring.
5. **Checkpoint 4 — performance:** approve the final budget table and validate profiler/GPU/resource evidence before correctness/UI freeze.
6. **Release candidate:** validate every required pipeline/API cell and final clean-project workflow.

Any data loss, source mutation in default mode, unrecoverable save, incorrect channel packing, incomplete UDIM output, library registration failure, or unexplained recurring paint spike is a NO-GO at every checkpoint.

## 11. Definition of done

The release phase is complete only when all of the following are true:

- A slot can open directly from its inspector with no avatar discovery or generation.
- A UDIM member always opens and behaves as one logical group with every physical member included.
- A temporary session is recoverable through the configured `painter_recovery.asset`; Save As creates an independent permanent project document that reopens safely.
- Material capability, preview, painting, packing, and importer configuration use one verified shader/material descriptor.
- The material-only path creates the editable white default Fill for the first physical texture.
- The overlay path preloads compatible base textures without eager destructive copies.
- Export changes neither the document nor source assets by default.
- Export produces correctly named physical textures and recipe-ready overlay asset(s), registered and persistent in the UMA library.
- Explicit source overwrite is isolated, confirmed, backed up, and failure-safe.
- Approved 2K/4K interaction, persistence, allocation, and resource budgets pass on named reference hardware with retained evidence.
- Warm painting performs no redundant per-update composite/pack/bind/repaint work and no TexturePaint-owned managed allocation.
- The assembly/domain-reload behavior is explicit, deterministic, recoverable, tested, and documented.
- URP/HDRP pass on D3D11, D3D12, Vulkan, and Metal as specified.
- Performance, memory, correctness, usability, recovery, migration, packaging, and documentation gates all pass in clean Unity 6.3+ projects.

## 12. Deferred follow-on

Runtime project replay, multi-project composition, and transient UMA overlay creation are intentionally deferred until this release phase is complete. Their isolated implementation plan is [Runtime Replay and Overlay Composition](RUNTIME_REPLAY_IMPLEMENTATION_PLAN.md). That plan does not add requirements to the current release definition of done.
