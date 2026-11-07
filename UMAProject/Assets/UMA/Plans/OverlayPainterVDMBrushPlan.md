# OverlayPainter VDM Brush Implementation Plan

Status: Proposed; implementation has not started  
Updated: 2026-10-21  
Target: Unity 6.3+; current UMA checkout  
Repository destination: `Assets/UMA/Plans/OverlayPainterVDMBrushPlan.md`

## Objective and release boundary

Support vector displacement map (VDM) brush assets on OverlayPainter **Projection** and **Path** layers.

- **Phase 1:** simulate surface relief by writing signed height, encoded around neutral gray, into the existing **Normal Control channel** of those layers. Use the existing effective-normal preview and export pipeline. Do not change geometry.
- **Phase 2:** evaluate the same saved VDM assets and placements as full vector displacement on geometry, with results saved as a **blendshape**, **SlotDataAsset**, or **MeshModifier**.

Phase 2 compatibility is a Phase 1 acceptance requirement. The saved authoring data must retain the original three displacement components, decoding convention, amplitude, coverage, placement, and target identity. A derived grayscale texture is a disposable preview cache, never the authoritative brush asset.

Normal Control is a channel in the current implementation, not a new layer kind. VDM should be an optional source on existing Projection/Path layers, with no new freehand Paint, Fill, or polygon workflow in Phase 1.

## What Phase 1 can represent

A VDM contains directional displacement. Blender's Draw brush supports shapes with overhangs; its VDM Brush Baker produces OpenEXR images. Phase 1 deliberately uses only the displacement along the brush's local normal. It ignores tangential displacement, so it cannot reproduce overhangs, silhouette changes, displaced lateral detail, self-occlusion, or collisions. The product label should be **VDM Relief (Normal Control)** and explain that geometry output arrives in Phase 2. See the [Blender Draw brush manual](https://docs.blender.org/UATEST/manual/en/4.5/sculpt_paint/sculpting/brushes/draw.html) and [VDM Brush Baker](https://extensions.blender.org/add-ons/vdm-brush-baker/).

This is a component-extraction approximation, not a render of the fully displaced VDM surface into a height map. Rasterizing that surface would introduce a different conversion, occlusion rules, and treatment of folds; it is outside Phase 1.

## Current implementation and integration points

Paths below are relative to `Assets/UMA/` and refer to the current working checkout inspected for this plan.

| Area | Existing code and implications |
| --- | --- |
| Projection authoring | `OverlayPainter/Runtime/TexturePaintProjection.cs`: `TexturePaintProjectionSettings` and per-channel sources; planar, wrapped, and cylindrical mapping; placement, flips, fade, depth and visibility restrictions. Extend this model rather than creating another projection tool. |
| Projection rendering | `OverlayPainter/Runtime/TexturePaintProjectionRenderer.cs` and `OverlayPainter/Shaders/ProjectionLayer.shader`: already generate Normal Control and give textured height its own alpha. Symmetry overlaps use strongest coverage rather than doubling height. |
| Path authoring | `OverlayPainter/Runtime/TexturePaintDocument.cs`: `TexturePaintSplineSettings` stores brush/source, spacing, rotation, deterministic flip seed, caps, fades, symmetry, and generators. `TexturePaintPathMode` includes Stamps, Continuous, Ribbon, and Filled. |
| Path rendering | `OverlayPainter/Runtime/PaintingEngine.cs`, `WorldSpaceStrokeSampler.cs`, `OverlayPainter/Shaders/StrokeRasterize.compute`, and `RibbonProjection.shader`. Ribbon coverage can be taken from Albedo and applied across active channels; VDM height needs an explicit independent-coverage route. |
| Source extraction | `TexturePaintSpriteSource` in `OverlayPainter/Runtime/TexturePaintTypes.cs`, plus `SourceExtract.shader`: grayscale extraction currently uses RGB luminance; unblurred extraction uses ARGB32/RGBA32. Passing raw VDM through this path would mix directions and lose signed/HDR precision. |
| Normal Control | `OverlayPainter/Runtime/TextureStore.cs`, `TextureLayerCompositor.cs`, `OverlayPainter/Shaders/LayerComposite.compute`, and `ChannelPack.compute`: neutral 0.5; per-layer Height Strength, Invert Height, and Sample Radius; gradient-derived normals; half-float channel allocation when supported. Keep these semantics. |
| Persistence and history | `TexturePaintDocument.cs`, editor `TexturePaintDocumentStorage.cs`, `TexturePaintDocumentPersistence.cs`, and `TexturePaintLightweightHistory.cs`: update source serialization, deep cloning, dirty checks, recovery, linked layers, and undo together. |
| Geometry output precedent | `Core/Editor/StageUtils/VertexEditorStage.cs` already has `SaveSculptModifier`, `SaveSculptAsBlendshape`, `TryBuildSculptBlendshape`, `SaveSculptToBaseSlot`, and `SaveSculptToNewSlot`. `Core/Editor/Scripts/MeshModifierEditor.cs` builds and saves modifier snapshots. These are integration precedents, not yet a public VDM export API. |
| Geometry payloads | `Core/StandardAssets/UMA/Scripts/MeshModifier.cs` stores per-slot modifiers. `Core/Scripts/VertexAdjustmentCollection.cs` includes `VertexDeltaAdjustment` and `VertexBlendshapeAdjustment`. Existing sculpt blendshape output uses `UMABlendShape`/`UMABlendFrame`. |

## Shared architecture required in Phase 1

### 1. Preserve a vector source asset

Introduce a dedicated `TexturePaintVDMBrush` ScriptableObject (proposed name). Keep this independent of the current freehand `BrushPreset`; Projection and Path should share it without implicitly enabling VDM everywhere a paint brush is selectable.

Store:

- Original VDM texture reference, stable asset GUID/local file ID, source dependency hash, and schema/decoder version.
- An explicit decoding profile: RGB swizzle, axis signs, neutral vector, component scale, and coordinate-space declaration.
- Canonical brush-local axes **U, V, N** and canonical displacement units. Prefer brush-relative units with one explicit uniform reference-size scale; stretching the footprint must not silently stretch vector amplitudes independently on each axis.
- Coverage mode: explicit mask, valid source alpha, or footprint/falloff. Store mask reference and its interpretation independently of displacement.
- Suggested placement size and displacement amplitude, tags, and a generated shaded thumbnail. These are defaults; existing layers retain their saved overrides.

Start with a validated **Blender sculpt VDM brush** profile and a configurable custom profile. Do not infer a profile from the `.exr` extension, filename, or colorful appearance. Do not assume that a Blender material-node tangent convention is the sculpt-brush convention. Blender documents distinct object/tangent interpretations in its [Vector Displacement node manual](https://docs.blender.org/manual/ko/latest/render/shader_nodes/displacement/vector_displacement.html); this is a reason to test conventions, not proof of the brush baker's channel mapping.

Use synthetic +U, +V, +N, negative-N, and asymmetric real baker samples to establish swizzle, signs, vertical image orientation, neutral value, and scale. Record the baker/version used. Unknown object/world-space VDMs should require an explicit supported conversion profile or be rejected with an actionable explanation.

### 2. Store an editable VDM application on each supported layer

Add an optional, versioned `TexturePaintVDMApplicationSettings` to Projection and Path settings (proposed name). Include the brush reference, decode-profile snapshot/version, source revision, amplitude, coverage overrides, relief conversion scale, and future output selection. Existing projection transforms or spline points remain the placement authority.

The Phase 1 output is fixed to `Relief`; reserve versioned evolution for `Geometry` and an explicitly managed combined output. Keep geometry displacement amplitude separate from Normal Control's **Height Strength** and pixel-based **Sample Radius**. Neither of those normal-rendering controls is a physical geometry distance.

Define how asset changes affect saved layers: detect a changed source hash, invalidate derived caches, and explicitly regenerate/update the application. Preserve the decode settings snapshot so a later decoder-default change cannot reinterpret old documents silently. Missing assets should show a diagnostic and retain saved raster preview when available; they must not silently become a different brush.

Update all deep-copy, duplicate, link/instance, save/load, autosave/recovery, undo/redo, and change-detection paths. Older documents with no VDM settings must render identically. Avoid enum renumbering and retain legacy projection channel-source migration.

### 3. Separate decoding, placement, and output

Use three reusable contracts, with names finalized during implementation:

1. **Decode source:** sample raw data and return signed canonical `(dU, dV, dN)` plus independent coverage. Never normalize the displacement vector as if it were a normal.
2. **Evaluate placement:** map a surface sample to brush UV, a deterministic local frame `(U, V, N)`, scale, coverage, target identity, and application/stamp ID. Reuse projection restrictions and path sampling, including seeded variation and symmetry.
3. **Write output:** Phase 1 emits Normal Control; Phase 2 emits slot-local vertex deltas. Output writers must not own import conventions or invent different placement rules.

Phase 1 does not need a complete geometry evaluator or a general plugin framework. It does need explicit, testable source/frame contracts and persistent input data. Prefer shared math and small CPU reference evaluators with GPU equivalents; test parity on representative placements to prevent shader-only conventions from blocking Phase 2.

## Phase 1 implementation

### 1A. VDM import and source validation

- Treat EXR as linear data with sRGB disabled and Texture Type Default, not Normal Map. Select signed RGBAHalf/RGBAFloat-compatible storage explicitly; do not assume Unity's automatic HDR/compression choice preserves negative values.
- Validate the actual imported format, platform overrides, finite values, resolution, and precision using the signed test fixture. Avoid lossy compression and unintended resizing. Start without mipmaps; add signed-data-aware filtering only after validation.
- Prefer a dedicated imported copy or explicit fix action if the original texture has other users. Do not globally reconfigure arbitrary existing EXRs.
- Do not infer coverage from displacement magnitude or luminance: zero displacement can be a valid opaque sample, and EXR alpha may be absent or uniformly one. With no mask/usable alpha, use the placement footprint and edge falloff.
- Preserve the original source asset. Generate a disposable height preview/cache keyed by source revision, decode profile, conversion settings, and output resolution. Do not recalculate or synchronously read back a large EXR for every path dab.
- Avoid permanent CPU-readable copies unless required by the chosen reference/fallback evaluator. Dispose caches on invalidation and account for domain reload.

Deliverable: verified signed vector decoding, a usable brush asset, and import diagnostics before renderer integration.

### 1B. Height conversion and compositing contract

For raw RGB `c`, decode using saved offset/scale and axis mapping:

```text
d = axisMapping((c - neutralVector) * componentScale)  // U,V,N; signed
h = displacementAmplitude * d.N
H = 0.5 + reliefEncodingScale * h
normalControlSource = (H, H, H, coverage)
```

`reliefEncodingScale` is a saved conversion into Normal Control units, not a claim of physically calibrated bump shading. A zero source is exactly neutral; negative height remains negative relative to 0.5. Do not use RGB luminance, vector length, absolute value, per-image auto-normalization, or early `saturate(H)`. Global auto-normalization would make unrelated brushes change strength according to their extrema.

Route decoded height around the existing ordinary grayscale extraction path, or extend extraction with an explicit VDM mode and floating-point output. Audit every intermediate, source multiplier, compositor blend, raster cache, and persistence encoding for precision loss or clamping. `TexturePaintPixelData` has a format field; verify the actual capture and blob round trip, not just its declaration. If signed float support is unavailable, disable VDM relief with a diagnostic rather than silently using an 8-bit path.

The source is straight height with separate alpha. Apply opacity, masks, projection fade, and path coverage once through the existing renderer/compositor contract; do not pre-multiply height by the same coverage and then multiply it again. Transparent pixels must not carve dark seams or flatten underlying height. Use neutral height in padding and coverage-aware filtering.

Start with the existing Normal layer/channel blend behavior, and preserve its visible stack semantics. If additive relief is exposed, implement it around neutral (`destination + weight * (H - 0.5)`), not by adding 0.5-backed grayscale values. Texture blend modes are not automatically geometry blend modes; persist a separate geometry composition policy for Phase 2.

Keep Height Strength, Invert Height, and Sample Radius in the existing Normal Control controls. VDM decoding/amplitude and those controls must not apply the same scale or inversion twice.

### 1C. Projection layer support

- Add a VDM source selector to Projection properties and route its generated height to Normal Control. Preserve any unrelated authored channels on the layer.
- Reuse planar, wrapped, and cylindrical mapping, width/height, rotation, depth limits, front-face/first-surface/connected-surface restrictions, region selection, fade, and symmetry.
- VDM coverage belongs to its height source; Albedo alpha must not redefine it. Extend the existing independent-height-alpha route to recognize VDM sources.
- Flipping an image changes sample coordinates. Retain the corresponding U/V frame-reflection information for Phase 2 even though Phase 1 reads only N. Do not borrow normal-map green-flip handling for displacement data.
- Rebuild on VDM source/settings changes and preserve linked projection placements. Keep symmetry union behavior so coincident mirrored copies do not amplify relief.

### 1D. Path layer support

Deliver **Stamps** first: repeated VDM applications with existing spacing, size, rotation, tangent alignment, deterministic variation, taper, fade, depth and angle restrictions. Each stamp must retain or reproducibly derive its frame and stable sequence ID for geometry replay.

Include **Ribbon** as the second Phase 1 path milestone: reuse the existing along/across source mapping and repeat/join behavior for a continuous relief strip. Ensure the VDM coverage route bypasses Albedo-derived ribbon coverage. Verify joins, caps, short paths, and closed-loop frame continuity. Beginning/end sources must either have their own VDM decoding descriptors or be explicitly unavailable for VDM; do not interpret arbitrary color endcaps as vectors.

Do not silently map VDM into every Path mode. **Continuous** and **Filled** are outside the initial VDM support matrix unless separately implemented and tested. Existing path generators, garment and hem-seam construction should remain separate source choices; switching to VDM must be explicit and reversible.

Define overlap consistently: symmetry copies union coverage; ordinary repeated stamps retain a documented deterministic composition order. Ribbon crossfades blend decoded signed heights and coverage, not raw encoded vectors. Persist that distinction so geometry replay can adopt a deliberate policy.

### 1E. Preview, persistence, export, and discoverability

- Expose VDM import/create, thumbnail, profile, mask, amplitude, and relief controls only in supported Projection/Path contexts. Show disabled-mode explanations instead of silently converting the layer to Paint.
- Normal Control preview shows grayscale height; Normal preview and 3D preview use the existing effective normal output. VDM alone must not author Albedo, Roughness, or other channels.
- Save original source references and complete applications alongside the normal document data. Cached relief may be discarded/rebuilt without losing Phase 2 inputs.
- Reuse flattened normal export and transparent runtime-overlay normal export. Phase 1 exports no geometry and does not add a new physical runtime Normal Control texture.
- Rasterizing/flattening a VDM layer is destructive to its editable placement. Preserve a source-layer backup or clearly identify the resulting raster as ineligible for future full-vector geometry replay. Never advertise reconstruction of full VDM from flattened height.

## Phase 2: geometry displacement and three output adapters

### 2A. Evaluate against a stable reference surface

Add a geometry backend that samples the original VDM and the saved applications. For an orthonormal placement frame:

```text
deltaWorld = coverage * geometryScale * (d.U * U + d.V * V + d.N * N)
deltaSlot = convertDisplacementToSlotSpace(deltaWorld)
```

`geometryScale` follows the saved amplitude and canonical-unit policy. UV footprint stretching, brush-relative size, nonuniform object transforms, reflected frames, and local-to-world conversion need separate, tested handling. Displacement vectors use the appropriate linear position transform; normals use the corresponding normal transform. Do not treat a world-space delta as a Slot-local delta.

Evaluate from an immutable authoring baseline. Rebuilding a preview must not repeatedly add displacement to already displaced vertices. Define path frames with continuous orientation and deterministic twist, including sharp turns and closed loops. Evaluate masks, visibility and connected-surface restrictions on that baseline to avoid feedback during rebuild.

Phase 1's channel effects and arbitrary texture blend modes may not have geometry equivalents. Define a supported subset (placement coverage, opacity, explicit masks, amplitude, deterministic vector composition), and report unsupported effects before baking. Normal Control Height Strength/Sample Radius remain shading-only controls. Do not claim pixel-perfect appearance parity between a height approximation and full-vector geometry.

### 2B. Map preview geometry back to source slots

OverlayPainter works with reconstructed/combined surfaces; output assets require source slot identity and original vertex ordering. Establish and validate an explicit map from each preview vertex to source asset GUID, slot occurrence, original vertex index, and reference topology signature. A UV coordinate or transient object ID is insufficient, especially with shared/mirrored UVs and multiple slots.

Inspect `OverlayPainter/Runtime/MeshReconstructor*` and the existing `VertexEditorStage` slot mappings before choosing the adapter boundary. Reuse valid mappings, but do not assume combined renderer indices equal source slot indices.

Require a known authoring pose and reference state. Distinguish rest mesh, active DNA/modifiers, and current blendshape weights. Either evaluate in a supported rest/neutral authoring space or implement a verified inverse mapping from posed preview to slot space; a renderer transform alone cannot undo skinning. The existing sculpt save code computes preview-baseline deltas, so its coordinate-space assumptions must be validated before reuse with OverlayPainter's stage.

Preserve vertex count/order, triangles, UVs, bones, bind poses, skin weights, submeshes, and unrelated blendshapes. Recalculate affected normals/tangents under a documented policy and refresh bounds/collider/preview caches. Weld duplicated seam vertices only when they represent the same intended surface; avoid joining adjacent but independent garments.

Geometry detail is limited by mesh density. Provide an undersampling diagnostic; do not promise Blender-like overhang detail on a coarse UMA mesh. Automatic subdivision/remeshing is outside Phase 2 because it breaks the topology contract required by these outputs.

### 2C. Share a geometry result and adapt it to existing save workflows

Create an editor-side result containing per-slot baseline identity, vertex position deltas, optional normal/tangent deltas, changed-vertex mask, and provenance. Extract reusable, independently testable save helpers from the existing sculpt workflow where appropriate instead of depending on private window methods or duplicating asset mutation code.

| Output | Required behavior |
| --- | --- |
| Blendshape | Create a named `UMABlendShape` with a 100%-weight `UMABlendFrame` on each affected source slot, using deltas against the agreed reference. Preserve unrelated shapes/frames. Handle name collisions explicitly and reject conflicting results when multiple slot occurrences share the same asset. Verify 0% reproduces baseline and 100% reproduces preview after avatar regeneration. |
| Slot | Create a new SlotDataAsset by default, copying its settings and mesh data and applying verified deltas. Reuse slot naming, `PrepareForAssetPath`, and UMA indexing behavior. Offer base-slot overwrite only as an explicit existing-style save action. Preserve unrelated data and validate topology immediately before writing. |
| MeshModifier | Build per-slot delta adjustments using the existing modifier representation and runtime split/build lifecycle. Use position and normal/tangent adjustment types as appropriate; verify their scale semantics. Confirm modifier scale 0/1 and any supported DNA scaling reproduce baseline/result after regeneration. |

Validate every destination before multi-slot writes, group Undo, and define failure recovery so one invalid slot does not leave a partial bake. Rebuild the avatar from saved assets to verify actual runtime behavior rather than treating an editor preview as proof.

When a full geometry bake includes relief already shown through Normal Control, disable the corresponding simulated relief contribution by default or explicitly bake only a residual normal detail. Do not apply the same feature twice. Keep the source VDM application/document available for later edits and rebaking.

## Delivery order and release gates

| Milestone | Work | Exit gate |
| --- | --- | --- |
| 1A | Signed fixtures, Blender profile, import validation, persistent VDM asset/application contracts | Confirmed channel/sign/orientation mapping; negative and HDR values survive import; full vector source survives save/load. |
| 1B | Floating-point height converter and Projection integration | Neutral, bump, pit, independent alpha, transforms and masks render correctly; normal export matches preview. |
| 1C | Path Stamps, then Ribbon | Deterministic replay, coverage, joins/caps, symmetry, undo, and document reload pass. |
| 1D | Persistence/history audit, UI, documentation, examples, regression tests | Phase 1 feature complete; saved applications demonstrably contain all inputs needed for future vector replay. |
| 2A | Reference-space geometry evaluator and source-slot mapping | Signed U/V/N displacements match analytic fixtures; topology and pose assumptions verified. |
| 2B | Shared bake result and blendshape/Slot/MeshModifier adapters | Each saved output regenerates to match preview; Undo, conflicts, and multi-slot failures behave correctly. |
| 2C | Geometry authoring UX, density diagnostics, double-application prevention | End-to-end Phase 1 document upgrade and geometry bake validated. |

The largest Phase 1 uncertainties are source-convention verification, precision through existing extraction/persistence, and Path coverage. The largest Phase 2 uncertainty is converting reconstructed preview geometry back to the correct source-slot reference space. Resolve these through small vertical slices before expanding UI or adding brush-library content.

## Validation plan

Extend the existing companion tests under `Assets/UMA/OverlayPainterTests`; ship fixtures/examples through the separate OverlayPainter Examples companion. Follow the project's plugin packaging rule, including companion entries beneath the parent in the installer. Do not place new tests/examples inside the main optional plugin package.

### Phase 1 acceptance

- Synthetic neutral, +N, -N, +U-only, +V-only, asymmetric and signed-HDR EXRs. Pure lateral displacement produces no Phase 1 relief; its full vector data survives serialization.
- Default Blender profile verified against at least one known baker export and controlled axis fixtures. Unsupported profiles produce diagnostics.
- Zero amplitude and neutral input leave the effective normal unchanged; positive/negative features produce opposite slopes. Source alpha and masks are applied once; transparent edges do not flatten underlying detail.
- Rotated/flipped planar, wrapped, cylindrical projections; connected/first-surface restrictions; mirrored overlap without doubled relief.
- Path Stamps and Ribbon: open/closed paths, short segments, variable width, repeat seams, endcaps, deterministic seeds, fades, symmetry, and independent height coverage with transparent Albedo.
- Float source, generated height, undo snapshot, document blob, recovery, duplicate/link, reload, source reimport, missing source, and cache rebuild checks. Include values outside 0..1 to catch accidental clamping.
- Regression suites: `TexturePaintNormalControlTests`, `TexturePaintProjectionTests`, `TexturePaintPathMirroringTests`, `TexturePaintPathSymmetryTests`, `TexturePaintPathFadeTests`, and `TexturePaintDocumentPersistenceTests`.
- Both flattened and transparent-overlay normal exports; unrelated channels and pre-VDM documents remain unchanged.
- Measure cache creation time, peak texture memory, and interactive rebuild cost on representative 1K/2K/4K assets and long paths. Rebuild should scale with changed inputs, not reread the full EXR per dab.

### Phase 2 acceptance

- Analytic U/V/N vector fixtures, transformed/mirrored placement frames, nonuniform object scale, and CPU/GPU evaluation agreement.
- Cross-slot seams, duplicate UVs, shared slot assets, hidden/removed vertices, topology changes, active DNA/modifiers/blendshapes, and pose restrictions.
- Repeated preview rebuilds are idempotent; cancel/undo restores baseline; stale mappings block saves.
- Saved blendshape at 0/100, new and overwritten Slot, and MeshModifier at 0/1 reproduce expected geometry after avatar regeneration while preserving unrelated data.
- A document created in Phase 1 opens in Phase 2 and produces full-vector geometry from its retained source and settings without manual brush replacement or re-placement.

Before executing implementation tests, verify the installed `UMASettings` asset and actual Unity version in the current project and record both. This plan was produced by code inspection; no VDM prototype, import experiment, geometry conversion, or test execution has been performed. The Editor connection previously reported Unity 6000.3.26f1; that is not a substitute for verifying the test environment when implementation starts.

## Decisions to close during the first implementation slice

1. Confirm the Blender sculpt baker profile and canonical scale from a real fixture; keep configurable profiles for other conventions.
2. Confirm the floating-point source/persistence route and supported graphics backends; avoid silently degrading signed data.
3. Finalize the shared placement-result contract and minimal metadata needed to recover source-slot identity in Phase 2.
4. Specify stamp overlap and geometry composition separately from ordinary texture blend modes.
5. Confirm the supported reference pose and whether the existing sculpt output helpers can accept a shared delta payload without a stage-specific assumption.

These are engineering validation tasks, not reasons to discard Phase 2 inputs or postpone their data model until after Phase 1 ships.


## Phase 1 implementation record - 2026-10-21

Implemented in Unity **6000.3.26f1**, with the installed UMASettings reporting **3.1.5**. The earlier statement that no prototype/tests had been run describes the planning stage; this record supersedes it for Phase 1.

- Added the versioned VDM brush asset and per-application snapshot retaining raw signed RGB, source GUID/revision, decode axes, neutral/scale, image flip, amplitude, and relief gain. Existing Projection/Path placement remains authoritative. Phase 2 can consume full-vector `Decode`/`Displacement`; no geometry output exists yet.
- Added dedicated EXR import as linear uncompressed RGBAFloat, floating-point height conversion around 0.5, explicit coverage, source validation, and bounded decoded-source caching.
- Integrated Normal Control VDM sources into Projection and Path Stamps/Ribbon, source editing, duplication, undo signatures, native document persistence, and existing normal export/composition. Invalid/stale sources report an error. Other layer/mode combinations are not exposed.
- Added an original procedural **Sculpted Core VDM** example and reproducible generator in the Examples companion. It does not read external input images or meshes. Removed the copyrighted project-imported reference; original Downloads files were left unchanged.
- Updated usage documentation and the main, tests, and examples package manifests. New tests remain in the Tests companion.

### Verification

| Suite | Passed |
|---|---|
| VDM | 20/20 |
| Normal Control | 12/12 |
| Projection | 139/139 |
| Path (including mirroring, symmetry, fading and generators) | 262/262 |
| Document persistence | 21/21 |

The initial broad path run reported 260/262 while Editor compilation and two timed-out diagnostic commands interfered with it; its detail list was incomplete. The clean rerun above is the recorded acceptance result.

The original 512 x 512 EXR was imported as linear RGBAFloat and decoded through the actual height shader. With gain 4 its height range is 0.38852936 to 0.8096947, with transparent boundary coverage. The signed source retains independent lateral components. C# compilation and the touched-file whitespace check passed.

### Remaining validation and Phase 2 work

This verifies Phase 1 on the current Editor/graphics backend. A 1K/2K/4K memory/performance matrix, additional graphics backends, and vendor-specific EXR convention coverage remain follow-up validation. The preview PNG is a shaded illustration, not a displaced-avatar screenshot. Geometry frame evaluation, physical vector overlap composition, source-slot mapping, pose/topology checks, and blendshape/Slot/MeshModifier output remain Phase 2 tasks as specified above. Preserve editable VDM layers and their raw EXRs for that work.
