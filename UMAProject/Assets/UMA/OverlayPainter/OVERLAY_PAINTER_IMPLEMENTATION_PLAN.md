# Overlay Painter: Release-Phase Implementation Plan

**Status:** Ready for implementation review  
**Prepared:** 2026-08-04  
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

**Implementation status (2026-08-04):** Implemented; awaiting Manual Checkpoint 3 validation.

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

Acceptance:

- Exported physical textures decode to the previewed R/G/B/A values within the approved tolerance.
- A newly exported overlay is discoverable in the UMA library and can be selected in a recipe without repair.
- Reopening a recipe with the overlay reproduces the Overlay Painter preview.
- Default export leaves every source asset byte-identical.
- Export leaves the document revision, dirty state, content hash, and Undo stack unchanged.
- Cancel/failure leaves no half-configured overlays, broken index entries, or unreported temporary files.
- Explicit overwrite changes only the preflight-listed assets and restores them on injected failure.

**Manual checkpoint 3:** Export non-UDIM and multi-tile UDIM results, add them to real recipes, rebuild characters, restart Unity, and verify library persistence, material assignment, texture orientation, packing, naming, and source-asset preservation.

### Phase 5 — performance and resource closure

**Goal:** Meet the release-plan responsiveness and resource budgets with the new session and slot workflows.

Work:

1. Make editable texture copies, layer targets, tangent/seam maps, and masks demand-driven.
2. Batch stroke samples per editor frame and compose, pack, bind, and repaint once per dirty target/channel/frame.
3. Prewarm or GPU-generate geometry/island masks outside stroke events; bound and instrument every cache.
4. Pool history tiles, compute buffers, stamp arrays, and transient collections.
5. Cache compute kernel IDs, shader capability decisions, flattened layer graphs, and plugin discovery through editor `TypeCache`/registries.
6. Add centralized GPU byte accounting and deterministic disposal on target change, window/stage close, domain reload, failed initialization, and canceled export.
7. Make the 2D window consume shared preview resources and update only when visible or explicitly dirtied.
8. Profile standalone reconstruction separately by asset load, mesh conversion, collider, spatial index, target catalog, support maps, and source binding.
9. Move color sampling and any CPU fallback away from full-target synchronous readbacks.

Acceptance:

- Warm 2K stroke P95/P99 and 4K P95 meet the frozen Phase 0 budgets.
- UDIM boundary crossing is no more than the approved multiple of a same-island stroke.
- TexturePaint-owned code allocates 0 managed bytes per warm steady paint frame.
- Closing the 2D window does not introduce recurring painting work or stutter.
- No monotonic GPU/managed growth appears across 20 open/close, target-switch, mask-edit, and undo/redo cycles.

**Manual checkpoint 4:** Repeat the torso, low-alpha, long-stroke, first-touch, UDIM-boundary, 2D-closed, and repeated-open stress cases with Unity Profiler and GPU capture evidence.

### Phase 6 — correctness and usability closure

**Goal:** Remove ambiguous behavior and close all advertised editing paths.

Work:

1. Complete vector-correct normal composition or restrict unsupported normal blend modes explicitly.
2. Establish GPU/CPU blend parity and eliminate silent source fallbacks.
3. Freeze one source/fill/export Y-orientation contract across every supported API.
4. Validate low-alpha painting, UV wrap, UV seam, physical slot boundary, UDIM boundary, masks, clone, projection, symmetry, undo, and fill invalidation.
5. Use one vocabulary: Overlay Painter, logical target, UDIM group, physical slot, material channel, logical channel, document, and export result.
6. Keep layer settings per layer and hide controls that do not apply to the selected layer/source/fill type.
7. Show a persistent context header with slot/group, tiles, material, pipeline, sources, working resolution, document state, and current target/channel.
8. Preserve independent 2D and 3D Before/view state; keep Pick and wireframe controls in the 2D window.
9. Complete keyboard, tooltip, narrow-dock, high-DPI, multi-monitor, error, warning, and empty-state passes.
10. Ensure window close behavior cannot orphan the stage or lose a temporary/saved session silently.

Acceptance:

- Automated goldens pass for all supported operations and boundary combinations.
- UI never implies that an independent UDIM member can be painted or exported outside its logical group.
- Unsupported material/channel/tool combinations are disabled with an actionable reason.
- New and experienced users can complete slot-open, paint, Save As, reopen, and export-to-recipe workflows without undocumented steps.

### Phase 7 — pipeline/API certification and release candidate

**Goal:** Produce a clean Unity 6.3+ release candidate with an honest support matrix.

Work:

1. Run EditMode, GPU golden, integration, stress, recovery, export, and fresh-project tests for URP and HDRP.
2. Certify D3D11, D3D12, Vulkan, and Metal on appropriate hardware/agents. D3D11 remains the minimum capability configuration.
3. Validate shader compilation, compute kernels, UAV formats, color space, normal conventions, importer settings, and exported recipe output per matrix cell.
4. Import into clean Unity 6.3 URP and HDRP projects with no generated documents, recovery data, exports, logs, or local goldens in the package.
5. Audit assembly boundaries, optional Addressables integration, asset paths/GUID lookup, plugin API diagnostics, package size, `.meta` files, licenses, and uninstall/reinstall.
6. Update README, slot-first guide, document/recovery guide, material-channel authoring guide, export/overwrite guide, support matrix, performance limits, migration notes, troubleshooting, changelog, and known issues.

Release gate:

- No open P0/P1 correctness, data-loss, performance, or export-library defects.
- Every required URP/HDRP and API matrix cell passes or the release is NO-GO.
- Fresh-project slot-first to recipe workflow passes after an editor restart.
- No source asset is modified in the default workflow.
- No unexplained release-gate process failure or frame spike remains.

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
- Resource ownership, disposal, cache eviction, plugin filtering, and warm-frame allocation assertions.

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
- Narrow/wide docks, 2D window open/closed, stage-close cancellation, high DPI, and editor restart.

## 8. Migration and compatibility

- Bump the document schema only when the session context and incremental storage format are finalized.
- Existing document assets are opened read-only until migration succeeds to a new asset or backup-backed replacement chosen by the user.
- Existing `UMAMaterial` assets default editor-only channel layout and export settings to Automatic, preserving runtime behavior.
- Existing avatar-first launch remains available but adopts the new temporary session, persistence, material capability, and export services.
- Existing export templates are migrated to non-destructive defaults. Recipe update and material override flags are not honored silently by the release workflow.
- Current source overlays and textures require no migration.
- Temporary recovery assets, generated documents, exports, test logs, and goldens remain outside release package content by validation rule.

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
| TMP-PERF-101 | P1 | Lazy resources and centralized accounting | TMP-SLOT-103 |
| TMP-PERF-102 | P1 | Per-frame stroke batching and mask/history/cache work | TMP-PERF-101 |
| TMP-COR-101 | P1 | Normal/blend/orientation/fallback correctness | TMP-MAT-101 |
| TMP-UX-101 | P1 | Context, document, UDIM, layer, and export UI closure | TMP-SLOT-104, TMP-EXP-103 |
| TMP-TEST-101 | P0 | Automated release matrix and failure injection | All feature items incrementally |
| TMP-REL-102 | P0 | Fresh-project platform certification and release docs | All preceding items |

P0 here means required for the approved shipping contract, not merely a severity estimate.

## 10. Implementation stop points

Implementation should deliberately pause for user validation at these points:

1. **Checkpoint 0 — contracts:** confirm per-member UDIM overlay output, material export metadata, and naming.
2. **Checkpoint 1 — documents:** validate temporary/recovery/Save As before slot reconstruction changes.
3. **Checkpoint 2 — standalone slots:** validate mesh alignment, materials, source overlays, targets, and UDIM painting before export.
4. **Checkpoint 3 — exported UMA assets:** validate real recipe use and library persistence before performance refactoring.
5. **Checkpoint 4 — performance:** validate profiler evidence before correctness/UI freeze.
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
- URP/HDRP pass on D3D11, D3D12, Vulkan, and Metal as specified.
- Performance, memory, correctness, usability, recovery, migration, packaging, and documentation gates all pass in clean Unity 6.3+ projects.

## 12. Deferred follow-on

Runtime project replay, multi-project composition, and transient UMA overlay creation are intentionally deferred until this release phase is complete. Their isolated implementation plan is [Runtime Replay and Overlay Composition](RUNTIME_REPLAY_IMPLEMENTATION_PLAN.md). That plan does not add requirements to the current release definition of done.
