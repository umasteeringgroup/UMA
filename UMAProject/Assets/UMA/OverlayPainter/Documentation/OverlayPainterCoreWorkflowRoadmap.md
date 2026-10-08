# OverlayPainter core workflow parity roadmap

Scope: the major PBR texture-authoring workflows in ArmorPaint and Substance Painter,
with UMA material semantics, slot reconstruction, and UDIM support carried through every workflow.
This is a source-and-documentation gap assessment, not a certification of feature parity.
Implementation priorities below track remaining work. Items 1 and 3 have received the implementation update recorded below; other gaps remain recommendations.

## Implementation update: items 1 and 3

Implemented an ordered mask stack with 45 input/filter/generator kinds, per-entry blending and opacity,
smart-mask recipes with embedded textures, live dependency-checked layer inputs and persisted fallback
caches. Painted corrections remain an independent raster input; additional independent painted inputs
can be supplied by referenced Paint layers. Mesh generators retain the existing quick mesh-map estimates.

[Shared anatomical and garment-region masks](AnatomicalRegionMasks.md) now provide bone-relative
knees, elbows, armpits, cuffs, collar, lips, shoulders, waist and seat envelopes. Region Placement
uses the same mask for every generator channel, with shared profile assets and pose-aware joint sectors.

Projection now exposes pinned surface warp controls with 3 x 3/5 x 5/9 x 9 grids, explicit refit/reset, and
shared channel deformation. Cylindrical projection supports partial/full arcs and continuous Rectangle
fade at a full wrap. A movable/scalable/rotatable camera stencil clips interactive 3D brush coverage.
Existing world-space placement, independent size axes, rotation-ring behavior, and gesture history remain.
See `OverlayPainter/README.md` for artist controls and practical limits. The original gap descriptions
below record the baseline used to scope this work.

## Existing foundation

The current checkout already implements multi-channel 2D/3D painting, fill layers, groups,
editable grayscale masks, layer effects, paths/ribbons, planar and automatically wrapped
projections, flat/triplanar fills, logical UDIM targets and tile overrides, shared mirror/radial
symmetry, selections and saved regions, live layer references and linked instances,
procedural generators/filters, reusable packaged material stacks, document recovery,
and semantic packed exports. These capabilities should be extended and regression-tested.

Relevant source: `OverlayPainter/README.md`, `Runtime/TextureStore.cs`,
`Runtime/TexturePaintDocument.cs`, `Runtime/TexturePaintAuthoringData.cs`,
`Editor/TexturePaintDocumentStorage.cs`, and the plugin implementations.

## 1. Composable masks and reusable smart masks

Current limitation: `TexturePaintLayerMask` owns one raster target and one plugin definition.
`TexturePaintLayerMaskEffects` has fixed noise and texture-overlay options; it is not an ordered
stack of independent mask inputs and filters. Material presets already package full layer stacks.
The former `TexturePaintMaskPreset.cs` is intentionally empty and must not be mistaken for a
current reusable smart-mask system.

Add ordered paint, fill, generator, filter, and reference entries inside a layer/group mask.
Each entry needs enablement, opacity, appropriate blending, reordering, and independent settings.
Keep painted corrections independent of regenerated dirt/wear or other procedural inputs.
Save/load the complete mask recipe as an asset; offer useful cavity dirt, edge wear, dust,
position-gradient, and material-ID examples using the existing generators where appropriate.

Acceptance: combine cavity dirt, noise, curves, and a hand-painted exclusion; changing any
input updates the result while preserving the authored correction. Reuse the recipe on a
second target and across UDIM tiles. Undo, save/reopen, and export reproduce the same mask.
Migrate existing masks without changing their appearance.

## 2. Production mesh-map baking

Current limitation: `TexturePaintBaker` flattens/readbacks textures for export. Procedural mesh
maps are available, but `ProceduralMeshMapBuilder` estimates AO from concavity and thickness
from ray distance to mesh bounds. These are useful generator inputs, not complete ray-sampled
occlusion/thickness or high-to-low mesh baking.

Add explicit source/target meshes, high-to-low normal and height transfer, cage/ray-distance
controls, matching by mesh/slot name, configurable occluder groups, anti-aliasing, and UV padding.
Produce real AO and thickness alongside curvature, position, world normals, and selectable ID maps.
Allow importing external mesh maps and inspect/rebuild each map independently. Cache and
invalidate by the geometry, transforms, UVs, bake settings, and relevant source data.

Acceptance: bake a raised detail into a low-poly target's normal/height maps; separate nearby
clothing pieces without cross-baking; demonstrate actual occlusion and thin/thick regions;
cancel without replacing valid maps; use the results in generators on multiple UDIM tiles.
Retain the current quick estimates as explicitly labeled options where useful.

## 3. Projection and stencil completeness

Current limitation: the projection layer can fit a nine-point wrapped patch, but its individual
points are not exposed as editable warp controls. Brush-source images are not a movable
camera-space stencil workflow. Fill projection currently offers Flat and Triplanar.

Add an editable warp grid with surface snapping, pinned points, reset/refit controls, and
additional grid divisions when needed. Add a camera-space stencil that can be moved, scaled,
and rotated while painting through it. Add cylindrical projection for continuous limb/tube patterns.
Preserve the user's established one-projection-per-layer, world-fixed placement, independent
X/Y/Z resizing, circular rotation interaction, and shared placement across channel sources.

Acceptance: fit a nail, place a tattoo over an elbow, stencil only selected portions of an image,
and wrap a patterned band around a limb. Albedo/normal/roughness align, fades work across tiles,
and edits remain interactive and undoable.

## 4. Complete material inputs and height workflow

Current foundation: channel sources and packed UMA export already exist; Normal Control modifies
normals but is a painter-owned auxiliary channel rather than a general exported height map.

Add PBR texture-set import that proposes channel assignments from filenames, understands packed
maps, previews the proposed mapping, and lets the artist correct ambiguous assignments. Expand
material preset discovery and preview. Add authored height with defined units/midpoint, blending,
normal derivation, and high-precision export; expose displacement preview when supported by the
chosen material. Keep Normal Control's existing behavior compatible.

Acceptance: import a common PBR folder, correct its mapping once, reuse it for painting/fills/
projections, and export correct linear/color data. Height survives editing and high-precision
export independently of its derived normal result.

## 5. Resolution and asset revision workflows

Current limitation: working resolution is chosen during setup and export can resize output.
The UV-change recovery path retains stroke/path definitions but resets masks and cannot simply
restore raster content onto a changed UV layout. This is not full paint-preserving reprojection.

Add safe working-resolution changes with an explicit policy for raster resampling versus
procedural regeneration/stroke replay. Add mesh/UV update comparison and texture transfer with
preview, diagnostics for unmatched regions, and reversible application. Never claim that upscaling
creates missing raster detail or that arbitrary topology changes can transfer perfectly.

Acceptance: change 1K to 4K, regenerate procedural detail at 4K, retain raster work predictably,
and recover the original with Undo. Transfer a painted asset to revised UVs with a before/after
preview and clear reporting of unresolved areas.

## 6. Visual procedural material and brush authoring

Current foundation: typed plugin parameters and coordinated multi-channel generators are available.
An artist-facing material/brush node editor is a separate missing workflow.

Provide reusable graphs for images, coordinates, noise, math, blending, ramps, mesh maps,
references, and semantic PBR outputs. Brush graphs should expose pressure, direction, and
randomization inputs. Reuse the existing channel contracts, generator host, and GPU execution
where possible. Support Unity 6.3; do not base this work on editor APIs requiring a newer minimum.

Acceptance: an artist creates a procedural material and directional brush without C#;
previews update interactively; graphs save, package, migrate, and fail with actionable diagnostics.

## 7. Material inspection and workflow polish

Audit and expand the existing Unity material preview into an intentional look-development workflow:
HDRI/lighting presets, environment rotation/exposure, reliable channel inspection, and clear
normal/height diagnostics. Preserve the actual UMA shader preview and validate export consistency.
Finish common keyboard/tablet interactions and asset-library discovery alongside the above milestones.

Acceptance: inspect roughness/normal/height under several lighting conditions, compare source
and composite output, and verify exported assets under the same target shader.

## Completion standard

For each milestone, validate an actual artist workflow on clothing and skin examples, plus
multi-channel and UDIM variants. Save/reopen and export must match the preview. Undo/Redo,
Escape/cancellation, source updates, missing inputs, and old-document migration must be safe.
Measure representative 2K/4K interactive performance and memory rather than relying on the
presence of a control or one synthetic test. Existing projection/path workflows remain covered.

A full node system, ray-traced presentation viewport, automatic UV tools, physics brushes, and
external proprietary material-format interoperability have separate costs and dependencies.
They should be tracked explicitly; completion of the core milestones must not be described as
complete product-for-product parity.

## Primary references

- [ArmorPaint manual: painting, materials, brushes, layers, baking, and export](https://armorpaint.org/manual)
- [ArmorPaint: procedural material/brush nodes and GPU workflows](https://armorpaint.org/)
- [Substance Painter: smart materials and masks](https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/features/smart-materials-and-masks)
- [Substance Painter: mesh-map baking settings](https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/baking/mesh-map-settings)
- [Substance Painter: editable warp projection](https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/painting/fill-projections/warp-projection)
