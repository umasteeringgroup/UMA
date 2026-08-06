# Overlay Painter Release-Readiness Plan

## Assessment

Overlay Painter has a capable prototype foundation: generated-material reconstruction, slot-aware cross-material brush footprints, world-space brush sizing, GPU paint kernels with CPU fallbacks, normal-map touchup, editable surface-hugging Bezier splines, plugin interfaces, a dockable UI, and initial tests.

It is not yet release-ready. The highest priorities are a real persistent document and compositing model, continuous world-space stroke/path sampling, correct per-texel masks, format-preserving tiled history, channel-aware export, and measurable performance and lifecycle gates.

## Implementation status

Milestone 1 was implemented on 2026-08-02. The stage now owns a versioned `TexturePaintDocument`, stable surface identities, lossless editable-base/layer storage, persisted global and per-layer mask records, persisted spline brush/source settings, GPU bottom-to-top composition with dirty rectangles, fill and group layers, per-layer/per-channel controls, document-backed Undo/Redo for layer and mask edits, autosave/recovery state, and shared preview/export composites. Focused document-model tests and user documentation were added with the implementation.

Milestone 2 was implemented on 2026-08-02. Pointer and spline input now share a cumulative world-space arc-length sampler that carries spacing remainder across events and path segments. Persistent stroke records retain stable surface ID, triangle, barycentric coordinate, world normal, pressure, time, motion, slot, and UV-island data. Per-contact motion survives slot/material transitions for smear and path rotation. The stage adds stabilization, direction filtering, tablet pressure controls, projection depth, normal-angle and backface controls, surface reprojection, and cached per-texel slot/island/polygon geometry masks. Paint hardness/source alpha now use a single source-over coverage calculation on CPU and GPU.

Milestones 3 and 4 were implemented on 2026-08-02. Surface paths now retain barycentric anchors, use adaptive Bezier tessellation with cumulative world-space arc-length spacing, expose complete node/tangent/dynamics editing, and apply as stamps, gap-free strokes, ribbons, or fills with follow/fixed orientation, caps, closed-loop handling, cross-slot projection, X mirroring, and radial symmetry. Masks are first-class layer children with bitmap/painted/slot/polygon/island/ID/procedural workflows, scene selection tools, levels/feather/blur, and per-texel composition. Material bundles add channel locks and contribution, packed mask-map unpack/repack with smoothness inversion, vector-aware normals and convention export, linear/sRGB handling, UV dilation, and cached procedural geometry maps. Focused path, mask, channel, and mesh-map tests were added.

The findings below describe the baseline at review time. Findings 1 and 2 are addressed by Milestone 1; the remaining findings are routed to later milestones.

## Release-blocking findings

1. **Layers are snapshots, not a composite stack.** Visibility selects the highest available texture. Layer opacity and blend mode are stored but are not evaluated.
2. **Painted pixels are not persistent.** Recipe state restores layer metadata and splines, but not layer/base pixels or masks.
3. **Spline sampling is UV-distance based.** Sampling restarts per segment, uses the first triangle's scale, and cannot guarantee continuous coverage across UV density or material changes.
4. **Spline stamps do not follow the path.** `alignToStroke` is present but unused, and splines are limited to one texture set.
5. **Masks are incomplete.** Polygon and island masks are tested at the stamp center, while the GPU sees only one painted mask texture.
6. **Undo reads full textures synchronously.** It stores RGBA32 PNG snapshots, causing stalls and losing high-precision channel data.
7. **Each stamp copies the full render texture.** Tile dispatches therefore still incur texture-wide work.
8. **Several tools are incomplete.** Smear loses its motion vector across projected contacts, ordinary normal painting is not vector-aware, and erase writes channel defaults instead of removing current-layer data.
9. **Packed channel layouts are not modeled.** Metallic, roughness/smoothness, and AO can share one physical texture, but are treated as independent outputs.
10. **Export is not production-safe.** It always generates new PNG paths and has no output templates, overwrite policy, bit depth, padding, packing, or transactional recipe update.

## Milestone 1 — Production document model

Create a persistent, versioned `TexturePaintDocument` referenced by the avatar recipe. Bind texture sets using stable slot/material/mesh identifiers rather than reconstruction indexes.

The document must support:

- Paint, fill/material, spline, group, and mask layer types.
- Per-channel enablement, opacity, and blend modes.
- Persistent layer and editable-base pixels.
- Persistent mask data and spline brush/source settings.
- Bottom-to-top GPU composition shared by preview, bake, thumbnails, and export.
- Autosave, recovery, schema versioning, and migrations.
- Dirty-tile invalidation so non-destructive layers can rerasterize efficiently.

Acceptance criteria:

- Visibility, opacity, blend mode, reorder, duplicate, merge, and delete behave as true layers.
- Preview and baked output match.
- Closing and reopening Unity restores painted pixels, layers, masks, and splines.
- Layer and mask changes participate in undo.
- Layer deletion is undoable.

## Milestone 2 — Unified continuous stroke engine

- Record persistent 3D surface hits with surface ID, triangle, barycentric coordinate, normal, pressure, and time.
- Resample by cumulative world-space arc length across events, spline segments, slots, and materials.
- Carry spacing remainder across the entire stroke.
- Preserve motion vectors for smear and clone.
- Clip each affected texel against geometry and painted masks.
- Add lazy-mouse stabilization, tablet pressure, direction filtering, projection-depth, and backface controls.

## Milestone 3 — World-class surface paths

- Make paths surface-anchor based rather than UV-first.
- Add adaptive Bezier tessellation and arc-length lookup tables.
- Add insert, delete, multi-select, duplicate, copy/paste, reverse, corner, broken, smooth, and custom tangents.
- Interpolate pressure, width, flow, roll, color, and offset per point.
- Support Follow Path and fixed-axis stamp orientation.
- Add gap-free continuous stroke, paint-along-path, ribbon, filled, erase, smudge, blur, and normal-touchup modes.
- Support cross-slot/material paths, mirroring, radial symmetry, end caps, corners, and closed-loop spacing.

## Milestone 4 — Masks, materials, and channel correctness

- Make masks first-class layer children.
- Add white, black, bitmap, painted, slot, polygon, UV-island, ID, and procedural masks.
- Add click, box, lasso, grow, shrink, feather, blur, invert, and levels workflows.
- Paint complete material bundles with per-channel locks and contribution controls.
- Add shader-aware channel packing, roughness/smoothness inversion, normal convention selection, vector-aware normal blending, linear/sRGB correctness, and UV dilation.
- Cache position, world normal, curvature, AO, thickness, and ID mesh maps for procedural generators.

## Milestone 5 — Performance and precision

- Copy and compose only dirty tiles, including filter halos.
- Batch stroke samples into compute dispatches and use in-place kernels when safe.
- Store sparse coverage tiles instead of full-size per-stroke float textures.
- GPU-build and cache tangent/seam maps by mesh/UV hash.
- Use asynchronous GPU readback and format-preserving lossless tile deltas.
- Add memory budgets, cancellation, progress, and resource-leak checks.

Performance gates must be measured on agreed reference hardware. Suggested starting targets are p95 preview latency below 33 ms for 2K single-channel painting, below 50 ms for 4K four-channel painting, no ordinary-stroke stall above 100 ms, and undo work proportional to dirty area.

### Milestone 5 implementation status (2026-08-02)

Implemented:

- Dirty-rectangle ping-pong synchronization, layer composition, and packed-channel repacking, including brush/filter halos.
- In-place kernels for dependency-safe tools plus bounded multi-sample GPU dispatch for spline and path painting.
- Sparse 128-pixel capped-coverage tiles with a configurable allocation budget and sparse CPU fallback.
- GPU UV rasterization of vertex-normal/tangent maps when supported, CPU fallback, and reference-counted caching by complete mesh/UV/map hash. Seam and procedural map generation are cached and cancellation-aware.
- Sparse, exact-format undo tiles captured through asynchronous GPU readback when available and compressed losslessly; undo capacity and memory budget prune old groups.
- Cancellation/progress plumbing for long mesh-map work, rolling p95/maximum preview metrics, dirty-work counters, configurable undo/stroke budgets, and editor resource-baseline checks.

Automated precision/performance-contract tests cover sparse history, dirty synchronization, cache ownership, cancellation, and rolling latency statistics. The numeric 2K/4K latency gates remain hardware benchmarks: use the **Performance & Memory** panel on the agreed reference machine and record p95/max values before release sign-off.

## Milestone 6 — Export and UMA integration

- Replace direct export buttons with a template-driven export dialog.
- Support output selection, overwrite/version policies, resolution, bit depth, padding, packing, inversion, normal convention, deterministic filenames, preview, and transactional rollback.
- Create or update UMA overlays, material overrides, recipe references, and Addressables metadata.
- Detect topology/UV/material changes and rebind, reproject, or report orphaned content.

### Milestone 6 implementation status (2026-08-02)

Implemented:

- Replaced the three direct bake buttons with a dockable template-driven export window and a reusable production template asset.
- Added current/all-material scope, logical channel selection, shader packing, arbitrary RGBA packing rules, fail/overwrite/versioned policies, native or fixed output resolution, 8-bit PNG, 16-bit PNG, half-float EXR, padding, inversion, normal convention, and deterministic filename tokens.
- Added a resolved output/conflict preview and composited texture preview before commit.
- Made export transactional: outputs are encoded before mutation; overwritten files and UMA assets are snapshotted; cancellation or failure restores files, overlay/material assets, recipe state, and newly created Addressables metadata.
- Added creation/update of UMA `OverlayDataAsset` channel arrays, Unity material override assets, persisted recipe export records, template GUIDs, and optional Addressables entries.
- Added independent geometry, topology, UV, and material fingerprints. Exact content restores normally, compatible material changes rebind, UV-changed surface anchors remain available for rerasterization, and incompatible raster content remains in the document as a reported orphan rather than being silently lost.
- Added EditMode exporter integration tests and PlayMode tests for channel selection, state serialization, and fingerprint behavior.

## Milestone 7 — Plugin API v2

- Add API versioning, capability metadata, declared channels, parameter schemas, command transactions, undo, cancellation, dirty-tile integration, and diagnostics.
- Define safe brush, filter, generator, baker, importer, and exporter extension points.
- Prevent plugins from bypassing masks, history, color-space rules, or document persistence.

### Milestone 7 implementation status (2026-08-02)

Implemented:

- Removed the unused v1 API entirely and made API v2 the only discovery and execution contract.
- Added stable API/plugin versioning, reverse-DNS IDs, capability metadata, declared channel access, typed parameter schemas, range validation, duplicate rejection, and persisted parameter profiles.
- Added safe brush, filter, generator, baker, importer, exporter, and procedural-mask interfaces. Brush extensions can only modulate standard samples; other extensions see immutable copies and in-memory artifact/command contexts.
- Added sealed, bounded command transactions with preflight validation, copied payloads, cancellation, per-texel global/structural/painted masks, color/data validation, normalized normal vectors, non-destructive plugin layers, dirty-rectangle composition/packing, atomic rollback, dedicated undo/redo, and persisted plugin provenance.
- Added independent snapshot, command, artifact, and history budgets plus registration/execution diagnostics with timing, command, dirty-pixel, cancellation, and exception data.
- Rebuilt the plugin window around schemas and extension categories, artifact import/save, progress/cancel, diagnostics, and transaction undo/redo. Migrated both example plugins to v2.
- Added adversarial tests for mask bypass, undeclared channels, incorrect color spaces, cancellation, dirty bounds, normal precision, memory budgets, profile persistence, and undo/redo.

### Pre-Milestone 8 integrity gate

The v2 boundary was reviewed for mutation access, parameter validation, thread-late commands, memory exhaustion, partial failure, temporary-resource cleanup, mask enforcement, color/data correctness, vector normals, logical-channel packing, document persistence, and lifecycle disposal. Milestone 8 should not introduce any UI route that exposes mutable render targets or bypasses `PluginHost` transactions.

Integrity gate result: all four Overlay Painter assemblies compile under Unity 6.3, all 50 PlayMode tests pass, and all 4 EditMode exporter integration tests pass. Milestone 8 was not started as part of this gate.

## Milestone 8 — Usability redesign

- Use persistent tool, texture-set/slot, layer/path, properties, and asset-shelf regions.
- Add synchronized 3D and 2D UV views.
- Add thumbnails, folders, rename/duplicate/drag reorder, search, tags, favorites, recent brushes, drag-and-drop, and standard shortcuts.
- Show brush size, hardness, stamp, direction, and mirrored copies on the model.
- Add modifier-drag controls, channel solo, before/after, slot isolation, wireframe, and color/material sampling.
- Separate terminology into Source, Destination, Target, and Channels.

### Milestone 8 implementation status (2026-08-02)

Implemented:

- Replaced the monolithic inspector presentation with a persistent, resizable workspace containing a global toolbar, tool rail, Slot/Texture Set target navigator, synchronized UV canvas, Layer/Path stack, contextual Properties inspector, and Asset Shelf. Regions can be shown, hidden, resized, and reset; layout, UV navigation, display modes, shelf filters, favorites, recents, and custom brush order persist in recipe state schema v10.
- Added synchronized 2D UV painting through the existing projected world-space stroke path, including cross-slot footprint behavior, masks, active destination layers, continuous stroke sampling, clone-source selection, color/surface sampling, pan/zoom, logical-channel previews, UV wireframe, visible spline curves, point selection, surface-hugging point movement, and path creation.
- Added layer/path thumbnails, inline/context rename, duplicate, delete, merge, drag reorder, paint/fill/path/group creation, layer properties, channel locks/contribution/opacity/blend, and path dynamics in dedicated regions.
- Added a searchable brush shelf with shape/stamp thumbnails, folder filtering/creation, tags, favorites, recents, persistent drag order, Project-window drag-and-drop, texture-to-session-stamp conversion, rename, duplicate, and access to the full brush library.
- Added 3D brush feedback for outer size, hardness boundary, rotation/direction, stamp preview, and global-X mirror copy. Added Shift-right-drag size/hardness controls, bracket controls, standard tool/channel/document/layer shortcuts, channel solo, source-before comparison, selected-slot isolation, shaded wireframe, and an armed color/surface sampler.
- Standardized UI language around Source, Destination, Target, and Channels, defaulting new work to a non-destructive active-layer destination.
- Preserved the Milestone 7 integrity boundary: UI brush extensions still only modulate standard samples, and model extensions remain reachable only through `PluginHost` snapshot/artifact/command transactions. The workspace does not expose a mutable render target, layer collection, mask, or `TextureStore` to plugins.

Validation: all four Overlay Painter assemblies compile under Unity 6.3, all 51 PlayMode tests pass, and all 4 EditMode exporter integration tests pass.

## Milestone 9 — QA and release engineering

**Status: implemented.** Milestone 9 adds production-compute golden images for every core tool and blend mode, full document/layer/export round trips, long-stroke and sharp-path gates, multi-slot/UV/material/pipeline matrices, actual 1K–4K allocation, precision/memory/lifecycle/plugin-cancellation tests, editor preflight, and an isolated-process CI runner. See [Milestone 9 — QA and Release Engineering](MILESTONE_9_RELEASE_GATE.md) and [Release Gate](QA/RELEASE_GATE.md).

- Add GPU golden-image tests for tools and blend modes.
- Test path gaps, path orientation, cross-slot seams, layer composition, save/reopen, format precision, export round trips, domain reloads, resource leaks, plugin cancellation, long strokes, and memory/performance budgets.
- Validate one/many slots, shared/separate materials, packed maps, different UV densities, mirrored/overlapping UVs, 1K–4K textures, sharp curves, and supported UMA material pipelines.

## Release split

### Release-ready 1.0

1. Real composited layers.
2. Persistent painted data and autosave.
3. Unified continuous stroke sampling.
4. Gap-free Paint Along Path with Follow Path orientation.
5. Correct per-texel masks.
6. Format-preserving tiled undo.
7. Shader-aware packing and export.
8. Performance and lifecycle gates.
9. Focused contextual UI.
10. Integration and regression tests.

### World-class 1.x

- Ribbon and filled paths.
- 2D UV painting.
- Fill/material layers and groups.
- Mesh maps and procedural generators.
- Projection and stencil tools.
- Advanced dynamics and radial symmetry.
- Rich filter/plugin ecosystem.
- Reprojection after mesh or UV changes.
- UDIM support where UMA workflows require it.

## Recommended first vertical slice

1. Introduce `TexturePaintDocument`.
2. Implement two genuinely composited paint layers with opacity and Normal/Multiply blending.
3. Replace spline sampling with cumulative world-space arc-length sampling.
4. Add Follow Path rotation and a swept continuous-circle mode.
5. Persist layer tiles and spline definitions.
6. Prove a path has no gaps across a two-slot seam after save/reopen.
