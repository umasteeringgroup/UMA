# Overlay Painter: Release Phase Review and Plan

**Status:** Planning only — no implementation is authorized by this document  
**Review date:** 2026-08-04  
**Unity baseline:** Unity 6.3 and newer  
**Current recommendation:** **NO-GO for release** until the P0 gates in this plan pass

## 1. Purpose

This document reviews the current Overlay Painter stage in four areas:

1. Performance and resource use.
2. Usability and workflow completeness.
3. Missing or insufficiently specified features.
4. Release readiness, testing, packaging, and supportability.

It also defines the slot-first launch workflow requested for this phase, including how a slot obtains its UMA material, overlays, textures, avatar context, and logical UDIM target.

This is an editable implementation plan. Findings marked **Confirmed** were traced to current code or artifacts. Budgets are **provisional** until Phase 0 records a baseline on agreed reference hardware.

## 2. Executive assessment

The core painting workflow is substantially functional: logical UDIM targets reconstruct correctly, painting crosses UDIM and UV boundaries, layers and fill sources exist, the 2D view is dockable, target framing exists, and recent artifact fixes have been manually validated. The remaining risk is concentrated in lifecycle and production behavior rather than the basic brush result.

The stage should not ship in its current state for these reasons:

- **P0 — persistence can block the editor and produce extremely large project assets.** Current saves synchronously read, copy, compress, and serialize full-resolution base and layer textures. Two existing documents occupy about **415 MiB** in `Assets/UMA/OverlayPainter/Documents`.
- **P0 — GPU resources are allocated too eagerly.** The store creates minimum channels and multiple full-resolution render surfaces even when a material does not use those channels. Per-surface tangent/seam support maps are also built eagerly.
- **P0 — the current release gate is stale and reports failure.** Its 2026-08-02 summary reports all listed tests passing but an overall `FAIL`, so the actual process/infrastructure failure is unresolved. It also predates the recent UDIM, fill, layer-state, and docked-window changes.
- **P0 — product ownership is not fully defined.** Export creates assets and records them in stage state, but the intended update/apply behavior for an UMA recipe, wardrobe item, avatar, or source overlay is not yet explicit.
- **P1 — intermittent painting spikes have several credible hot paths.** First-use geometry masks, per-stamp composition/packing, temporary allocations, kernel lookup, and resource churn need measurement and consolidation.
- **P1 — slot-first launch has no deterministic standalone context.** A `SlotDataAsset` contains geometry and UDIM metadata, but not enough information to reconstruct the actual material, overlays, base textures, or recipe state by itself.
- **P1 — recent behavior lacks a focused regression matrix.** Slot/UDIM fill, texture orientation, per-layer settings, window lifecycle, closed-2D painting, persistence, migration, and graphics API parity need explicit coverage.

The recommended sequence is to establish instrumentation and product decisions first, fix persistence and memory second, add slot-first launching third, then close correctness/usability gaps and run a clean release candidate gate.

## 3. Current architecture and evidence snapshot

| Area | Current behavior | Assessment |
|---|---|---|
| Stage entry | `TexturePaintStageWindow.ShowStage` accepts a generated `DynamicCharacterAvatar`; the DCA inspector is the primary launcher. | Avatar-backed only; no slot-first launch contract. |
| Stage startup | The stage loads compute shaders, reconstructs rendered geometry, initializes a default 2048 store, creates/opens a document, and opens its editor windows. | Too much synchronous/eager work; opening can modify project/scene state. |
| Slot material | Runtime `SlotData.material` comes from an alternate material, the first overlay's material, or a fallback. | A raw `SlotDataAsset` does not deterministically identify the paint material. |
| Logical targets | Reconstructed surfaces are grouped into paint targets, including UDIM composites. | Good foundation for slot-first focus and composite selection. |
| Paint dispatch | A sample may capture history, prepare a geometry mask, dispatch compute, composite layers, pack physical channels, bind previews, and repaint UI. | Correct functional chain, but too much work can occur per stamp. |
| Layer composition | Dirty rectangles are composited through visible layers. | Needs cached layer graph, normal-specific rules, and mask-cache eviction. |
| History | Before/after GPU render-texture tiles are retained under a memory budget. | Better than full snapshots, but tile allocation/reuse needs profiling. Documentation is stale. |
| Fill layers | Flat and triplanar generated textures are cached per layer/source/settings. | Functionally useful; invalidation, orientation, and UDIM tests are incomplete. |
| Documents | Full channel/layer images are read to CPU and Deflate-compressed during save. Documents are stored under the tool's `Assets` folder. | Release blocker for responsiveness, size, source control, and package cleanliness. |
| Export | Textures and UMA-related assets can be created/imported, with records retained in stage state. | Recipe/avatar application semantics need to be made explicit and tested. |
| Plugin discovery | Loaded assemblies are scanned and plugin types instantiated during initialization. | Cache/type-filter work is needed; editor test types previously surfaced in discovery. |
| Release gate | Existing gate reports 26/26 EditMode and 72/72 PlayMode passing, but overall `FAIL`. | Not a valid release signal until exit/infrastructure handling is diagnosed. |

Primary review locations:

- `Assets/UMA/OverlayPainter/Runtime/`
- `Assets/UMA/OverlayPainter/Editor/`
- `Assets/UMA/OverlayPainter/Tests/`
- `Assets/UMA/OverlayPainter/README.md`
- `Assets/UMA/OverlayPainter/RELEASE_READINESS_PLAN.md`
- `Assets/UMA/OverlayPainter/MILESTONE_9_RELEASE_GATE.md`
- `Assets/UMA/OverlayPainter/QA/RELEASE_GATE.md`
- `Logs/TexturePaintReleaseGate/release-gate-summary.md`

The older readiness documents are useful historical baselines, but several statements no longer describe current code. In particular, history storage and recipe-reference export behavior must be documented from the final implementation, not copied forward.

## 4. Product decisions required before implementation

These decisions affect storage formats, launch APIs, UI, and acceptance tests. Resolve them at the first manual checkpoint.

### 4.1 Editing ownership and final output

Choose and clearly label what a document edits and what **Commit/Export** does:

- A non-destructive paint document only.
- New texture and `OverlayDataAsset` assets, without changing a recipe.
- A new or duplicated UMA material/overlay configuration.
- The selected recipe or wardrobe recipe.
- The generated avatar instance only.
- An existing source overlay/texture in place.

Recommended release behavior:

1. Painting remains non-destructive until an explicit commit/export.
2. The commit screen names every asset to be created or replaced.
3. Applying to a recipe/avatar is a separate explicit option with Undo and a backup/duplicate recommendation.
4. Existing source assets are never overwritten by default.

### 4.2 Document lifecycle

Decide whether stage launch opens:

- **Temporary session** — a recoverable working asset may be created in the UMA Settings recovery folder after editing; no permanent document exists until Save As.
- **New document** — user chooses a project or user-data location.
- **Existing document** — opened through a chooser/recent list.

Recommended default: temporary/recoverable session with a visible **Save As** action. Recovery uses `painter_recovery.asset` and sibling data files in the configurable recovery folder (default `Assets/UMA/Temp`); teams should exclude that folder from source control. Simply opening the stage must not dirty the scene/recipe or modify source assets.

### 4.3 Slot-first scope

Recommended scope for this phase:

- Ship the **avatar-backed slot launch** workflow.
- Include a context resolver and chooser when multiple valid avatars/recipes exist.
- Include an honest fallback wizard when no generated avatar supplies the slot context.
- Do not silently guess an UMA material or fabricate missing overlays/textures.
- Treat a UDIM slot group as one logical selection while retaining its physical slots/textures on the backend.

### 4.4 Required material pipelines and platforms

Record the supported matrix for the first release:

- Built-in, URP, and HDRP status.
- D3D11, D3D12, Vulkan, and Metal status.
- Required UMA shader/property conventions.
- Whether Addressables is mandatory or an optional integration.
- Maximum supported texture resolution, UDIM tile count, layer count, and active target count.

## 5. Slot-first launch design

### 5.1 Do we need to select an `UMAMaterial` in advance?

**Not when the slot is launched in the context of a concrete generated avatar or recipe.** That context already resolves the actual `SlotData`, overlays, UMA material, generated material grouping, base textures, scale/transform, and UDIM companions. It should be authoritative.

**A raw `SlotDataAsset` alone is insufficient.** It owns geometry and UDIM metadata, but the material generally comes from the concrete slot's alternate material or its overlay stack. A manually selected `UMAMaterial` still does not supply the base overlay textures or recipe state. Therefore, a material picker should be a fallback in a paint-context wizard, not a mandatory first step.

### 5.2 Context resolution order

When **Open in Overlay Painter…** is invoked from a slot:

1. Use an explicitly supplied generated avatar/recipe context.
2. Otherwise find open, generated DCAs that contain the selected slot asset or its UDIM group.
3. If exactly one match exists, offer it as the default and open it.
4. If multiple matches exist, show a chooser with avatar, race, recipe, overlay stack, UMA material, and target-resolution summary.
5. If no generated avatar matches, offer selected compatible recipe/wardrobe/overlay contexts.
6. If unresolved, open a context wizard that requests enough information to build a truthful preview:
   - Race/base recipe or preview avatar.
   - Slot/UDIM group.
   - Overlay stack or source `OverlayDataAsset`.
   - UMA material only when still ambiguous.
   - Preview resolution and document mode.
7. Refuse to open with a clear explanation if required data is still missing.

### 5.3 Launch contract

Add a single launch-context model rather than more unrelated `ShowStage` overload logic. It should carry stable identifiers where possible:

- Generated avatar or preview-avatar context.
- Source recipe/wardrobe asset, when relevant.
- Requested slot asset GUID/source slot identifier.
- Requested UDIM group identifier and member slots.
- Optional overlay and UMA material override chosen by the user.
- Initial logical target/channel.
- New, temporary, or existing document request.
- Requested frame/focus behavior.

The stage should validate the context before changing stage, scene, or document state. If the avatar must build, show progress and continue only after a successful generation event. Do not call `GenerateNow` when current generated data is already valid.

### 5.4 Slot inspector and related entry points

Add the launch action to the existing Slot Utilities area of the `SlotDataAsset` inspector. Also plan equivalent contextual actions from:

- A concrete slot/overlay entry in a recipe or wardrobe editor.
- A selected generated avatar's slot list.
- A logical target/UDIM group browser, if one is exposed outside the stage.

The initial target selection must:

- Select the whole UDIM composite when the source slot belongs to a UDIM group.
- Select only the individual slot for a non-UDIM slot.
- Frame the target bounds in Scene view.
- Show the resolved material, overlay stack, channels, tiles, and document ownership in the UI.
- Allow changing context before the first edit without losing data.

### 5.5 Slot-first acceptance criteria

- Launching from a slot contained by one generated avatar reaches the correct logical target without asking for a material.
- Multiple context matches never select one silently.
- A UDIM member opens the composite target and paints all enabled member tiles.
- A non-UDIM slot opens only that target.
- The displayed UMA material and overlays match generated UMA data.
- No-match launch explains why a raw slot is insufficient and offers a usable context wizard.
- Canceling the chooser/wizard leaves the current stage, scene, and assets unchanged.
- Rebuilding the avatar either refreshes the context safely or presents a conflict/rebuild prompt.

## 6. Performance plan

### 6.1 P0 — replace blocking, full-document persistence

**Confirmed behavior:** save captures every base channel and layer at full resolution using render-target readback, raw managed copies, and optimal Deflate compression on the editor thread. Autosave and close can trigger this path. Current generated document assets are approximately 371 MB and 65 MB.

Plan:

- Move generated/user documents out of the tool/package folder by default.
- Remove generated documents from the release payload and add ignore/package validation rules.
- Track dirty targets, channels, layers, and tiles; persist only changed content.
- Use staged `AsyncGPUReadback` with bounded work per editor frame.
- Compress away from the editor thread using measured speed/size settings.
- Avoid redundant storage of unchanged source/base textures; store source references plus deltas where appropriate.
- Write atomically through a journal or temporary artifact, then swap after validation.
- Add recovery for interrupted or corrupted saves.
- Never start a heavy save while a stroke is active.
- Show save state, progress, last successful save, document size, errors, and cancellation/close behavior.
- Define document schema/version migration and backup rules before changing the format.

Acceptance:

- Continuous painting is not interrupted by autosave.
- No persistence slice blocks the editor main thread longer than the calibrated per-frame budget.
- A small edit grows stored data in proportion to changed regions, not total document resolution/layer count.
- Closing with a save in progress gives explicit wait/cancel/discard choices and cannot corrupt the last good document.
- A recovery test restores the last complete state after forced termination during save.

### 6.2 P0 — make texture and support-map allocation demand driven

**Confirmed behavior:** the store ensures a minimum set of channels and generally creates front, back, and composite render surfaces. Reconstructed surfaces also build full-resolution tangent/normal/seam support maps. At 2048 ARGB32, three full surfaces are roughly 48 MiB before layers and auxiliary maps.

Plan:

- Build an explicit channel capability map from the resolved UMA material/shader.
- Create only supported, visible, or touched channels.
- Reference the immutable source texture until first modification instead of cloning it eagerly.
- Allocate layer targets only for channels the layer actually affects.
- Build tangent-space/seam maps only when a normal/tangent operation needs them.
- Share or cache immutable reconstruction maps by mesh/UV/version where safe.
- Add centralized GPU resource ownership, byte accounting, release telemetry, and an LRU/budget policy.
- Make working resolution explicit per target/document; do not silently default missing channels to 2048.
- Verify disposal on target switch, document close, stage close, domain reload, and failed initialization.

Acceptance:

- Opening an albedo-only material does not allocate normal/metallic/roughness/AO/emission paint surfaces.
- Untouched channels retain source references and allocate no editable copy.
- GPU memory returns to the expected baseline after closing the stage and after repeated open/close cycles.
- Resource telemetry identifies every live texture/buffer by owner and purpose in development builds.

### 6.3 P1 — consolidate work per editor frame

**Confirmed behavior:** a sample can capture history, create a mask, dispatch, composite all visible layers, pack channels, bind previews, and schedule repaint. Some batching exists, but freehand work can still repeat much of this per stamp.

Plan:

- Queue stroke samples for the current editor frame.
- Group by logical target/channel/projection and dispatch in batches.
- Union dirty rectangles and composite/pack/bind once per affected target/channel per frame.
- Maintain a small maximum input-to-preview latency so batching never feels delayed.
- Cache compute kernel IDs and capability decisions at shader initialization.
- Pool stamp arrays, dictionaries/lists, and compute buffers.
- Cache a flattened visible-layer render graph, including parent opacity/mask state.
- Replace global repaint calls with dirty-window/dirty-view invalidation where possible.
- Audit hot-path IMGUI allocations and avoid sorting/allocating metrics during painting.

Acceptance:

- Warm steady-state painting produces zero managed allocations per paint frame, excluding Unity internals that are documented and unavoidable.
- Composition and physical packing occur once per dirty target/channel per editor frame.
- Crossing a UDIM boundary does not produce a meaningful spike compared with an equivalent same-island stroke.

### 6.4 P1 — eliminate first-touch mask stalls and cache growth

**Confirmed behavior:** geometry masks may be rasterized on the CPU at full target resolution on first use. Layer mask cache keys include revisions, but superseded revisions are not immediately evicted.

Plan:

- Prewarm the selected target's required geometry/island mask after selection, with visible preparation state.
- Prefer a GPU-generated/cached triangle, island, or target-ID texture over per-pixel CPU rasterization.
- If CPU generation remains, make it cancelable and incremental, and never run it in a stroke event.
- Explicitly invalidate and destroy superseded mask-cache entries.
- Bound mask cache memory and report hits, misses, generation time, and bytes.
- Add stress tests for repeated mask editing and switching targets.

### 6.5 P1 — pool history resources

- Reuse fixed-size GPU history tiles rather than repeatedly creating/destroying render textures.
- Make history memory budget, tile size, and eviction observable.
- Verify that a truncated undo history is clearly communicated.
- Benchmark long strokes, many short strokes, undo/redo churn, and multi-UDIM strokes.

### 6.6 P1 — stage reconstruction and plugin discovery

- Measure reconstruction by avatar bake, mesh extraction, collider creation, spatial index, support maps, and target catalog.
- Display progress after 100 ms and support cancellation without leaving an orphaned stage.
- Cache reconstruction by generated-avatar build/version where safe.
- Detect avatar rebuild/source changes and invalidate only affected targets.
- Replace repeated whole-AppDomain plugin scanning with a cached editor registry/`TypeCache` strategy.
- Exclude abstract, generic, test-only, and editor-only types from runtime discovery as applicable.
- Validate constructors and API versions before instantiation and keep one concise diagnostic per bad plugin.

### 6.7 P1 — export/commit pipeline

- Stage GPU readback, image encoding, asset import, and UMA asset creation with progress and cancellation boundaries.
- Avoid repeated `SaveAssets`, synchronous imports, and preview rebuilds inside per-texture loops.
- Validate all output paths and conflicts before beginning mutation.
- Make the commit atomic from the user's perspective: assets only, or assets plus recipe application, with rollback/Undo on failure.

### 6.8 P2 — CPU fallback and sampling

- Replace full-texture CPU fallback readback and `Color[]` copies per stamp with tile-local operations, or explicitly limit unsupported tools/resolutions.
- Do not silently substitute a solid color when a source/stamp texture is unreadable.
- Cache or asynchronously read 2D color sampling rather than performing synchronous render-target readback for each pick.
- Establish GPU/CPU parity tests, including every supported blend mode.

## 7. Performance instrumentation and provisional budgets

Phase 0 must add/confirm measurement before optimization. Record main-thread time, render-thread/GPU time where available, allocations, dispatch count, dirty pixels, live GPU bytes, cache hits, and persistence bytes.

Instrument these named regions:

- Input/contact query and sampling.
- History capture.
- Geometry/island mask acquisition.
- Stroke upload and compute dispatch.
- Layer composition.
- Physical channel packing.
- Preview binding and Scene/2D repaint.
- Reconstruction.
- Fill generation.
- Save/readback/compression/serialization.
- Export/readback/encoding/import/UMA asset creation.

Provisional release budgets, to be calibrated and then frozen in Phase 0:

| Scenario | Initial target |
|---|---|
| Warm 2K steady stroke | P95 editor main-thread paint work <= 8 ms; P99 <= 16 ms; no prepared-frame spike > 33 ms |
| Warm 4K steady stroke | P95 <= 16 ms on reference hardware |
| UDIM boundary crossing | P95 no more than 1.5x equivalent same-island stroke |
| Managed allocation | 0 B per warm steady paint frame from TexturePaint-owned code |
| Autosave | No main-thread slice > 8 ms; never interrupts an active stroke |
| Target selection | UI responds/progress appears within 100 ms; expensive preparation is cancelable |
| Stage open | Warm P95 <= 2 s; cold P95 <= 5 s, excluding a required avatar generation clearly shown to the user |
| Stage close | Immediate when clean; explicit progress/choice when dirty or saving |
| Resource lifecycle | No monotonic GPU/managed growth over 20 open/close and target-switch cycles |

The existing broad thresholds such as 1000 ms paint P95 or 5000 ms long-stroke completion are smoke-test limits, not release-quality responsiveness gates.

## 8. Usability plan

### 8.1 Establish one vocabulary

Use consistent names in window titles, menu items, documentation, and messages:

- Product/workspace: **Overlay Painter**.
- Main controls window: **Overlay Painter**.
- Image window: **Overlay Painter 2D**.
- Logical selectable unit: **Paint Target**.
- Physical UMA geometry entry: **Slot**.
- UDIM composite: **UDIM Paint Target**, with member slots/tiles shown as details.

Rename source labels to describe user-visible behavior. For example, internal enum names such as `SourceTexture` and `SourceOverlay` should not surface as ambiguous UI terms if they mean **Base Texture** and **Active Layer**. Remove or implement stale source modes rather than leaving unreachable concepts.

### 8.2 Make context and scope continuously visible

The header should show:

- Avatar/recipe/document.
- Current logical target.
- UDIM group and member tile count, when applicable.
- UMA material/shader.
- Current channel and layer.
- Temporary/saved/dirty/autosaving/recovery state.

Target selection should frame the target, while a separate Frame button remains available. Changing a layer must immediately restore and display that layer's applicable source/fill/blend/mask settings. Hide settings that do not apply to the selected layer/source/channel.

### 8.3 Separate 3D and 2D view state

- Keep Pick, wireframe, 2D Before, pan, zoom, and clone-source controls in the 2D window.
- Keep 3D Before and Scene-specific visualization in the main/Scene workflow.
- Label Before state with its scope and make both states independently persistent per session.
- Closing the 2D window must not change painting cost or correctness.
- Closing the main window must keep the existing guarded stage-close behavior and cannot orphan the stage.

### 8.4 Clarify source, fill, and layer behavior

- One Source section supplies brush and fill layers.
- Fill layers retain per-layer source, flat/triplanar mode, tiling X/Y, projection settings, blend, opacity, and masks.
- Flat fill maps directly in UV space with a documented orientation.
- Triplanar exposes scale/tiling, rotation/offset if supported, hard/cross-fade choice, blend sharpness, and object/world/local projection space.
- Overlay sources show exactly which overlay and channel are sampled.
- Invalid or unavailable sources show a reason and a repair action.
- Normal-channel layers expose only blend operations with defined vector behavior.

### 8.5 Improve session safety

- Do not dirty the scene, avatar, recipe, or source assets just by opening.
- Prompt before discarding unsaved work, with document size and last recovery time.
- Surface autosave failures immediately but non-destructively.
- Detect source avatar/recipe/texture changes and offer reload, preserve-as-copy, or cancel.
- Restore windows and context after domain reload without silently rebuilding over unsaved state.

### 8.6 Accessibility and layout pass

- Test keyboard traversal and shortcuts without stealing Scene navigation input.
- Provide text/tooltips for icon-only controls.
- Verify narrow docks, high-DPI scaling, long asset names, and multi-monitor layouts.
- Avoid color-only status communication; use icons/text for masks, dirty state, warnings, and target membership.
- Add useful empty states for no avatar, no target, unsupported channel, missing shader, and unresolved slot context.

## 9. Correctness and missed-feature audit

### 9.1 Must resolve for release

- Define and implement vector-correct normal-layer composition, or restrict unsupported normal blend modes.
- Complete GPU/CPU blend parity; current fallback behavior must cover every advertised blend mode.
- Define erase behavior on base content versus non-destructive layers.
- Replace material/channel property-name guessing with a documented material adapter/capability contract, retaining heuristics only as an explicit fallback.
- Define source texture orientation once and test flat fill, brush stamps, overlays, export, and every supported graphics API.
- Define fill-cache invalidation when source assets, importer settings, projection settings, or generator versions change.
- Refresh safely when an avatar rebuild changes slots, overlays, materials, resolution, or generated renderers.
- Validate clone, projection, symmetry, masks, and undo across multiple UDIM tiles and resolutions.
- Make export's recipe/avatar update behavior match its documentation and UI.

### 9.2 Important feature gaps to schedule or explicitly defer

- Standalone slot preview context that does not require an already generated avatar.
- Material adapter authoring UI for custom UMA shaders.
- Document conflict resolution/merge when source textures or recipes change externally.
- Resolution conversion/resampling with predictable filtering and normal handling.
- Per-target resolution and mixed-resolution UDIM policy.
- Layer/group duplication across compatible targets.
- Import/export of reusable layer stacks or presets.
- Brush/source/fill preset management.
- Diagnostic bundle export containing settings, timings, capabilities, and redacted logs.

Anything deferred must be labeled as such in UI/docs; it must not appear partially available or silently degrade.

## 10. Test plan

### 10.1 Unit/EditMode coverage

- Launch-context validation and slot/material/overlay resolution order.
- Single, multiple, missing, and stale slot contexts.
- UDIM group membership and logical-target selection.
- Material capability/channel mapping.
- Per-layer setting persistence and UI view-model refresh.
- Fill-cache keys, invalidation, and generator-version migration.
- All blend modes on GPU and fallback paths.
- Vector-normal composition.
- Document dirty-region tracking, incremental serialization, migration, corruption detection, and recovery.
- Resource ownership/disposal and cache eviction.
- Plugin discovery filtering, constructor diagnostics, and API versioning.
- Export plan validation and recipe/application transaction behavior.

### 10.2 GPU golden/integration coverage

- Same-island painting at low alpha with colors that expose precision/blend artifacts.
- UV seam, wrap seam, slot boundary, UDIM boundary, and combinations of them.
- Flat texture/overlay fill orientation and separate X/Y tiling.
- Triplanar hard and cross-fade modes on curved and hard-edged meshes.
- Multi-tile UDIM fill from color, texture, and overlay sources.
- Masks, parent opacity, blend modes, erase, clone, normals, and undo/redo.
- 2D window open versus closed must produce byte-equivalent paint output.
- D3D11, D3D12, Vulkan, and Metal golden tolerances where supported.
- Built-in, URP, and HDRP material/channel matrices where supported.

### 10.3 Performance/stress coverage

- 2K and 4K, one and multiple UDIM tiles, shallow and deep layer stacks.
- Many short strokes, one long stroke, boundary-heavy stroke, rapid target switching.
- First touch versus warmed target.
- Mask edit churn and undo/redo churn.
- Autosave during idle immediately after a large stroke.
- Repeated open/close, domain reload, failed initialization, and canceled reconstruction.
- Export of all enabled channels with addressable and non-addressable variants.
- GPU memory, managed memory, GC allocations, cache growth, and frame-time percentiles.

### 10.4 Manual workflow matrix

- Launch from DCA, slot inspector, recipe/wardrobe slot, and existing document.
- One/multiple/no slot-context matches.
- UDIM and non-UDIM targets.
- Temporary session, Save As, recovery, export assets only, and apply to recipe/avatar.
- Main window and 2D window dock/undock/close/reopen behavior.
- Unsupported material/channel and missing-source recovery.
- High-DPI, narrow dock, multiple monitors, and keyboard-only navigation where practical.

## 11. Release engineering plan

### 11.1 Make the release gate trustworthy

- Diagnose why Unity returned failure when XML reported all tests passing.
- Distinguish assertion failures from licensing, project-lock, crash, timeout, compilation, and runner/infrastructure failures.
- Preserve the relevant Unity log tail and root-cause classification in the summary.
- Treat missing/incomplete XML as an infrastructure failure, not a zero-test pass.
- Run from a clean checkout/copy with Unity closed and no generated user documents.
- Fail on unexpected modified/generated files after tests.
- Include the recent fill shader and all required assets in preflight.
- Remove hardcoded assumptions that the tool always lives at `Assets/UMA/OverlayPainter` if package relocation is intended.

### 11.2 Packaging and dependency audit

- Move shaders/assets through GUID- or package-relative lookup rather than fragile hardcoded paths.
- Keep documents, autosaves, recovery files, exports, logs, and GPU goldens out of the package/tool source by default.
- Decide whether Addressables is required. If optional, isolate it in an integration assembly with suitable version/feature definitions.
- Decide whether UMA Core Editor must directly reference the TexturePaint editor assembly or should discover the optional integration.
- Verify asmdef platform/editor constraints, API visibility, namespace stability, and plugin API compatibility.
- Validate import into a fresh Unity 6.3 project with documented UMA dependencies.
- Audit `.meta` completeness, sample content, third-party licenses/notices, default assets, and package size.

### 11.3 Documentation deliverables

- Update README architecture and current limitations from actual code.
- Write slot-first, avatar-first, document, fill/triplanar, 2D, export/apply, recovery, and custom-material workflows.
- Publish a supported material/pipeline/API matrix.
- Document performance expectations and recommended resolution/layer limits.
- Document document-schema upgrades and backups.
- Add troubleshooting for plugin diagnostics, missing channels, unsupported sources, save/recovery, and release-gate failures.
- Add changelog, semantic/package version, migration notes, and known issues.

## 12. Phased execution plan

### Phase 0 — baseline, decisions, and reproducibility

**Goal:** Freeze product semantics and obtain trustworthy measurements before changing architecture.

Work:

- Resolve the decisions in Section 4.
- Define reference machines, pipelines, graphics APIs, content sets, and provisional limits.
- Add/confirm named performance instrumentation and diagnostic capture.
- Reproduce document size/save stalls, stage-open allocation, first-touch spike, warm stutter, and export cost.
- Diagnose the current release-gate exit-code failure.
- Record current output goldens before architecture changes.

Deliverables:

- Signed decision table.
- Baseline timing/memory/document-size report.
- Repro scenes/assets that can be included safely as tests or samples.
- A release gate that reports its real failure cause.

**Manual checkpoint 0:** Approve editing ownership, document behavior, slot-first scope, supported matrix, and calibrated budgets. Stop here if any decision changes the storage or launch architecture.

### Phase 1 — persistence and resource blockers

**Goal:** Remove the largest editor-stall, data-size, and GPU-memory risks.

Work:

- Implement incremental/asynchronous document persistence and recovery.
- Relocate user documents and remove generated documents from shipping content.
- Implement lazy channels, lazy editable copies, lazy layer targets, and lazy tangent support maps.
- Add resource accounting, deterministic disposal, and cache eviction.
- Pool history tiles and validate undo-budget behavior.

Exit criteria:

- Persistence and allocation acceptance criteria in Sections 6.1 and 6.2 pass.
- Existing paint goldens remain within approved tolerances.
- Schema migration and recovery tests pass from representative existing documents.

**Manual checkpoint 1:** Test long low-alpha strokes, autosave, close/reopen/recovery, target switching, and GPU memory in the Unity Profiler. Do not proceed if painting or saved output changes unexpectedly.

### Phase 2 — slot-first launch and context safety

**Goal:** Make slot-first entry a supported workflow without guessing materials or losing UMA context.

Work:

- Add the launch-context contract and resolver.
- Add the Slot Utilities launch action and concrete recipe/avatar entry points.
- Add multi-match chooser and no-match context wizard.
- Select/frame the logical UDIM composite or non-UDIM target automatically.
- Display resolved avatar/recipe/material/overlay/document ownership.
- Handle avatar generation, rebuild, cancellation, stale context, and no-match errors safely.

Exit criteria:

- All acceptance criteria in Section 5.5 pass.
- Opening/canceling does not dirty project or scene state.
- Slot-first and DCA-first routes create equivalent target/material/channel catalogs for the same generated avatar.

**Manual checkpoint 2:** Validate representative body, clothing, non-UDIM, and multi-tile UDIM slots, including multiple avatars using the same slot with different overlays/materials.

### Phase 3 — paint-loop performance and workflow polish

**Goal:** Eliminate intermittent stutter and make the common workflow predictable.

Work:

- Batch samples and consolidate compose/pack/preview per frame.
- Prewarm or move geometry masks to GPU; bound mask caches.
- Cache kernels/layer graph and pool transient resources.
- Optimize reconstruction, plugin discovery, color sampling, and export staging.
- Complete vocabulary, context header, state/error/empty UI, layer-specific settings, 2D/3D scope, and accessibility/layout pass.

Exit criteria:

- Calibrated paint-loop budgets pass at 2K and 4K.
- The 2D window being closed has no measurable recurring paint penalty.
- Repeated mask edits and target switches show no monotonic memory growth.
- All common tasks can be completed without hidden settings or ambiguous ownership.

**Manual checkpoint 3:** Conduct a timed usability pass with new and experienced UMA users and repeat the original torso/UDIM stress scenarios in Profiler and GPU capture tools.

### Phase 4 — correctness and feature closure

**Goal:** Close advertised behavior gaps and explicitly defer non-release features.

Work:

- Complete normal/blend/fallback/orientation/invalidation correctness.
- Finalize material adapter and unsupported-channel behavior.
- Finalize export/apply transaction and source-conflict handling.
- Complete automated matrices in Section 10.
- Mark deferred features in UI/docs and remove unreachable or misleading modes.

Exit criteria:

- GPU goldens pass across the supported pipeline/API matrix.
- Exported assets reload and reproduce previewed output.
- Recipe/avatar application is Undo-safe and failure-atomic.
- No P0/P1 correctness item remains open.

### Phase 5 — release candidate and documentation

**Goal:** Produce a clean, reproducible, supportable release candidate.

Work:

- Complete packaging/dependency changes and documentation.
- Import and test in fresh Unity 6.3 projects for every supported pipeline.
- Run the release gate from clean isolated workspaces on the supported platform/API matrix.
- Perform package-content, license, size, API, migration, and generated-artifact audits.
- Freeze version/changelog/known issues and create the release candidate.

Exit criteria:

- Release gate reports PASS with no unexplained process failures.
- Fresh-project import, compile, tests, sample workflow, and uninstall/reinstall pass.
- No documents, recovery data, exports, logs, or user-specific assets ship.
- Documentation matches current behavior and limitations.
- Final manual sign-off checklist is complete.

**Manual checkpoint 5:** Release-candidate acceptance. Any paint corruption, data loss, unrecoverable save failure, unexplained frame spike above the frozen budget, or ambiguous destructive export returns the phase to NO-GO.

## 13. Suggested tracked work items

| ID | Priority | Work item |
|---|---|---|
| TMP-PERF-001 | P0 | Incremental asynchronous document persistence and recovery |
| TMP-PERF-002 | P0 | Lazy channel/layer/support-map allocation and GPU memory accounting |
| TMP-REL-001 | P0 | Diagnose and repair release-gate process result handling |
| TMP-PROD-001 | P0 | Freeze document ownership and export/apply semantics |
| TMP-SLOT-001 | P1 | Launch-context model and validation |
| TMP-SLOT-002 | P1 | Slot inspector entry, resolver, chooser, and fallback wizard |
| TMP-SLOT-003 | P1 | UDIM composite focus/frame and avatar rebuild handling |
| TMP-PERF-003 | P1 | Per-frame sample batching and compose/pack consolidation |
| TMP-PERF-004 | P1 | Geometry-mask prewarm/GPU path and mask-cache eviction |
| TMP-PERF-005 | P1 | Reconstruction, plugin, history, and transient-resource optimization |
| TMP-UX-001 | P1 | Vocabulary, context/ownership header, source/layer UI cleanup |
| TMP-UX-002 | P1 | Session safety, errors, empty states, and dock-window lifecycle |
| TMP-COR-001 | P1 | Normal composition, blend parity, erase semantics, and Y orientation |
| TMP-COR-002 | P1 | Material adapter/channel capability contract |
| TMP-EXP-001 | P1 | Staged transactional export and recipe/avatar application |
| TMP-TEST-001 | P1 | Recent UDIM/fill/window/layer-state regression matrix |
| TMP-REL-002 | P1 | Package paths, generated-content cleanup, and optional dependency design |
| TMP-DOC-001 | P1 | Current user, material, migration, performance, and troubleshooting docs |
| TMP-PERF-006 | P2 | Tile-local CPU fallback and asynchronous 2D sampling |

## 14. Release checklist

### Product and data safety

- [ ] Document ownership and commit/export semantics are approved.
- [ ] Opening/canceling the stage is non-destructive.
- [ ] Autosave is incremental, non-blocking, observable, and recoverable.
- [ ] Existing document migration is tested with backups.
- [ ] Export/apply is explicit, validated, Undo-safe, and failure-atomic.

### Slot-first workflow

- [ ] Avatar-backed slot launch requires no advance material selection.
- [ ] Multiple matches use a chooser; no match uses an honest context wizard.
- [ ] UDIM members select one composite target; non-UDIM slots remain independent.
- [ ] Material, overlays, channels, and source context match generated UMA data.
- [ ] Avatar rebuild/stale context behavior is safe and tested.

### Performance

- [ ] Frozen 2K/4K paint budgets pass.
- [ ] UDIM crossings do not spike materially.
- [ ] Steady painting has no TexturePaint-owned managed allocations.
- [ ] Unused channels/maps allocate no editable resources.
- [ ] Save/export do not monopolize the editor thread.
- [ ] No repeated-open, target-switch, mask-edit, or undo memory growth.

### Correctness and usability

- [ ] Low-alpha, fill, seam, wrap, slot, and UDIM goldens pass.
- [ ] Normal and blend behavior is defined on all supported paths.
- [ ] Layer-specific controls refresh and irrelevant settings are hidden.
- [ ] 2D and 3D Before/visualization state is independent and clear.
- [ ] Closing either window cannot orphan the stage or lose work silently.
- [ ] UI handles missing/unsupported/stale contexts with actionable messages.

### Release engineering

- [ ] Clean release gate passes with correctly classified process results.
- [ ] Supported render pipelines and graphics APIs pass.
- [ ] Fresh Unity 6.3 project import and sample workflow pass.
- [ ] Optional/required dependency policy is enforced by assembly/package layout.
- [ ] No generated user documents, recovery files, exports, logs, or goldens ship.
- [ ] README, API docs, migration, changelog, and known issues match the candidate.

## 15. Explicit non-goals until approved

Unless promoted through the Phase 0 decision checkpoint, this plan does not assume:

- Runtime/in-player painting support.
- Support for Unity versions older than 6.3.
- Silent modification of original textures, overlays, recipes, or wardrobe assets.
- Automatic material guessing from a raw `SlotDataAsset`.
- Unlimited resolutions, UDIM tile counts, layers, history, or GPU memory.
- Support for arbitrary custom shaders without a material capability adapter.

## 16. Recommended immediate next action

Begin **Phase 0 only**. First resolve the four product decisions in Section 4, remove the current generated document assets from any proposed release payload without deleting the user's working copies, and capture a reproducible performance/resource baseline. Implementation of storage or slot-first APIs should wait until the ownership, context, supported-matrix, and migration decisions are signed off at Manual Checkpoint 0.
