# OverlayPainter: Visual Authoring and Pipeline Integration

Status: future project proposal. This document does not claim that graph authoring,
high-to-low baking, texture transfer, or OCIO integration has been implemented.

## Purpose

Let artists create and share procedural materials without writing C#, while keeping
the existing layer, mask, path, generator and material-preset workflows. Improve the
reliability of moving material data between OverlayPainter and external tools.

Target Unity 6.3 or newer and the installed UMA version in the current checkout.
Baseline for this plan: Unity 6000.3.18f1, installed UMA NextGen 3.1f1.

## Existing foundations

- Plugin API v2 provides declared inputs/outputs, immutable snapshots, cancellation,
  progress, memory budgets and atomic layer replacement.
- Masks already have ordered procedural and image operations, references and presets.
- Material presets already serialize reusable layer stacks and plugin dependencies.
- Normal Control supplies height-derived normal detail; it is not yet a general
  displacement pipeline with physical height units.
- The current implementation adds geometry-ray AO/thickness, clothing finishes,
  editable boundary paths, and reviewed PBR texture-set import.
- The UMA installation/plugin API manages optional package ownership and documentation.
  It is a different API from the OverlayPainter rendering plugin API v2. Preserve that
  separation: installing a graph extension must not become a core UMA dependency.

## Artist workflow

1. Create a Material Graph or Mask Graph asset from OverlayPainter.
2. Choose a starter template or turn a supported existing mask stack into a graph.
3. Connect texture, coordinate, mesh-map, lower-layer and procedural inputs to operations.
4. Inspect a node in isolation, including coverage and numerical range.
5. Expose a small set of named artist controls, with units, defaults and useful ranges.
6. Apply the asset as a normal generator layer or mask. Path layers can consume graph
   materials without giving up their editable curves.
7. Package the graph, textures and dependencies, or save a material-stack preset that
   uses it. Reopening the project must reproduce the same result.

## Scope and boundaries

First release: mask graphs and bounded PBR material graphs, using existing operations.
No arbitrary C# execution, dependency downloads, or unrestricted shader injection in
shared graph assets. A graph is data, not an editor script.

Keep the familiar parameter inspector available. Artists using a graph should not
need to understand its internal nodes. Existing generators remain usable and editable.
Do not rebuild every generator as nodes before shipping the initial graph feature.

## Data model

Use a versioned ScriptableObject containing stable GUIDs for graph, node, port and
connection identity. Do not use object instance IDs. Store graph layout independently
of evaluation data so moving nodes does not invalidate generated textures.

Node type IDs and versions are registered by optional extensions. A missing extension
must retain its node data, graph connections and last valid output; show a clear missing
dependency diagnostic. Migrations must preserve unknown fields and support Undo.

Typed ports:

| Type | Contract |
| --- | --- |
| Scalar | Numerical data with optional range and physical units |
| Color | Linear working RGB; authoring colors explicitly decoded from sRGB |
| Coverage | Independent 0–1 coverage, never inferred from arbitrary data channels |
| Normal | Decoded tangent-space vector with an explicit normal convention at import |
| Height | Signed relative height plus declared scale; conversion is explicit |
| Coordinates | UV, world, object, or path coordinates, including orientation/scale |
| Mesh signal | Position, normal, curvature, AO, thickness, surface/island identity |
| Material | Albedo, roughness, metallic, AO, normal, height, emission and coverage |

Graph output declares channels and required source/mesh maps. Reuse host snapshot
contracts and dependency validation. A texture-set graph never silently obtains pixels
from a future layer or creates a cyclic layer-reference dependency.

## Initial node library

Start with constant, exposed parameter, texture/SpriteSet sample, UV/world/path
coordinates, transforms, mesh-map input, lower-layer input and layer reference.

Reuse noise, gradient, remap/curve, arithmetic, blend, threshold, warp, blur, morphology,
distance and mask operations already available. Add explicit Material Blend,
Normal Combine, Height to Normal, Coverage and Material Output nodes.

Material Blend uses one shared coverage field across channels. Normals use a documented
normal-combination method rather than color interpolation. Height blending distinguishes
additive relief from replacement. Never apply layer opacity or masks twice.

Useful first templates: wet cloth, edge-worn leather, printed fabric aging, cavity dirt,
and a material restricted to a reusable garment-region mask.

## Evaluation and performance

Compile the acyclic graph into a typed execution plan; validate dimensions, channels,
units, missing inputs, cycles and memory limits before allocating outputs. Cache keys
include operation version, inputs, parameters, source-layer revisions, mesh/UV fingerprint,
projection settings, resolution and seed. The cache must distinguish previews from full
generation and discard work whose input revision has changed.

Retain immutable snapshots and worker execution for CPU operations. Upload textures,
create Unity objects, and commit results on the main thread. GPU kernels may be added
for suitable operations, but every supported operation needs documented numerical
tolerances and a reliable reference implementation.

Support bounded tiles with declared sampling halos for blur, distance and normal
derivatives. Account for intermediate buffers, channel precision, texture uploads and
UDIM peers in the memory budget. Reuse buffers whose consumers have completed.

Debounced draft previews may display while full output is pending, but they must be
clearly identified, never saved/exported as full output, and never replace the last good
result on failure. Decide whether to project drafts onto the character only after
measuring responsiveness and evaluating flicker/LOD effects; it is not a prerequisite.

Benchmark current behavior first on the saved pants project and a multi-slot character:
128/256 drafts, 1K/2K/4K output, several UDIM counts, cold/warm caches, cancellation and
repeat parameter edits. Record hardware, installed UMA and Unity versions, peak memory,
time-to-preview, full generation time and main-thread stalls. Set release budgets from
those measurements rather than claiming parity with another application.

## Editor architecture

Prototype the graph UI against Unity 6.3 before selecting a framework. Do not depend
on Graph Toolkit APIs that require a newer Unity version. A small UI Toolkit graph
surface or an optional newer-version adapter are candidates; keep the serialized graph
and evaluator independent of the chosen UI implementation.

Provide search, creation menu, keyboard navigation, Undo/Redo, copy/paste with new IDs,
grouping, comments, port-type feedback and a resizable node preview. Node selection
shows the parameter inspector. Graph diagnostics identify the specific node/port and
offer a useful next action. Avoid creating separate graph and layer versions of the
same material controls.

## Pipeline integration workstreams

### A. Material interchange

Extend the new reviewed PBR importer with named importer presets, naming convention
rules, batch material discovery, configurable packing and explicit color-space metadata.
Keep normal convention, roughness/smoothness and coverage visible in the review screen.
Support channel-by-channel round-trip verification. Investigate an optional SBSAR bridge
separately, including runtime redistribution/licensing and platform support; do not make
the core plugin depend on it.

### B. Production baking

The geometry-ray map improvement is a foundation, not the complete baking system.
Add optional high-resolution source meshes, low-resolution targets, cages, ray distances,
skew correction, matched slot/object groups and inclusion/exclusion of occluders. Expose
quality controls and external-map overrides with provenance and stale-map detection.
Consider bent normals and physical height output. Test self-intersections, thin/open
surfaces, nonuniform transforms, mirrored UVs and normal conventions.

### C. Mesh/UV revision and texture transfer

Store source geometry/UV fingerprints and a reproducible transfer recipe. Reproject
painted pixels, masks and all material channels to a revised target mesh, with distance
and orientation constraints. Distinguish definition regeneration from pixel transfer.
Preview transfer coverage/errors; commit transactionally with Undo. Preserve the old
document if correspondence is incomplete or the user cancels.

### D. Color management and displacement

Keep linear/data-channel correctness mandatory. Assess optional OCIO configuration,
working/display transforms, import/export transforms and viewport matching. Record
color metadata in document and preset assets. Data maps must bypass color transforms.

Define a physical height/displacement contract independently of Normal Control's current
relative relief behavior. Include neutral value, units, range, precision and conversion.
Do not silently reinterpret existing projects or turn shader displacement on by default.

### E. UMA semantic regions

An optional region provider supplies masks for knees, elbows, cuffs, collars, armpits,
lips and other meaningful character/garment areas. Prefer slot metadata and authored
region assets with explicit overrides. A missing region reports a diagnostic rather
than applying the effect to the whole surface. Graphs and existing generators consume
the same provider. This is a distinct extension, not anatomy hard-coded into each effect.

## Delivery stages and acceptance

1. **Contracts and prototype:** typed model, stable IDs, graph validation and CPU evaluator;
   headless deterministic tests; Unity 6.3 UI feasibility confirmed.
2. **Mask graphs:** existing stack operations and layer references; stack-to-graph conversion
   matches output within documented tolerance; save/reload, Undo and missing-node recovery.
3. **Material graphs:** shared coverage, all supported channels, normal/height composition,
   exposed controls, preset packaging, CPU/GPU comparisons and dependency invalidation.
4. **Artist usability:** starter graphs, diagnostic previews, discoverable controls, keyboard
   workflow and documented performance on actual UMA projects.
5. **Pipeline extensions:** independently shippable baking, PBR interchange, texture transfer
   and color-management milestones; no requirement to finish all before graph release.

Release gates include flat-normal identity, neutral-height identity, no change outside
coverage, metallic/roughness independence from height, no seams at tile boundaries,
determinism across tile size, stale result rejection, cancellation without data loss,
memory-budget failure before allocation, and optional-plugin uninstall/reinstall recovery.

## Starting the later project

Begin with a single mask graph reproducing an existing cavity-dirt mask. Prove source
tracking, linear/data semantics, cancellation, save/reload and parameter exposure before
expanding the node library. Select the graph UI only after the Unity 6.3 prototype.
The first review should contain a working graph asset, evaluation tests, a comparison
against the existing mask stack, and measured performance—not only an editor mockup.
