# Overlay Painter Plugin API v2

This is the extension-author reference. For artist-facing instructions covering every included
generator and filter, see
[Overlay Painter Generators and Filters](../Docs/OverlayPainterGeneratorsAndFilters.md).

Plugin API v2 is the only supported extension API. Plugins never receive `TextureStore`, `EditableTextureTarget`, `RenderTexture`, live layers, or live editable layer masks. Compatible commands may receive an immutable mask snapshot and submit transactional mask tiles through the host.

## Registration contract

Implement `ITexturePaintExtensionV2` through one or more focused extension points:

- `ITexturePaintBrushV2` — modifies color, opacity, size, rotation, or skip state for a standard brush sample.
- `ITexturePaintFilterV2` — reads immutable channel snapshots and submits tile commands.
- `ITexturePaintGeneratorV2` — creates content through tile commands.
- `ITexturePaintBakerV2` — converts immutable snapshots into an in-memory artifact.
- `ITexturePaintImporterV2` — converts an in-memory artifact into tile commands.
- `ITexturePaintExporterV2` — converts immutable snapshots into an in-memory artifact; the host UI owns the destination path.

Every plugin supplies a `TexturePaintPluginDescriptor` with:

- A stable lowercase reverse-DNS ID.
- Plugin and API versions.
- Exact capabilities.
- Every channel it may write (`declaredChannels`) and every channel it must snapshot (`readChannels`).
- The exact procedural mesh maps it requires when using `ReadsMeshMaps`.
- A typed parameter schema with unique IDs and valid ranges.
- The supported destinations (`LayerContent`, `LayerMask`, or both). `LayerContent` is the compatibility default.

Discovery rejects duplicate IDs, unsupported API versions, missing channel declarations, capability mismatches, and invalid parameter schemas.

## Safe pixel workflow

Filters, generators, and importers receive `TexturePaintCommandContextV2`:

1. Read source data through `context.source.Get(surfaceId, channel)`. The returned `TexturePaintReadOnlyImage` owns a copy, not a live texture. Use `GetChannelInfo` when only native output dimensions and color-space metadata are needed.
2. Periodically check `context.cancellationToken` and report bounded progress.
3. Submit one or more rectangular updates with `WriteTile`, or use `WriteTileCompact` for bounded RGBA8 output.
4. Return from the task. The context is then sealed and cannot accept late background writes.

Example:

```csharp
public Task ExecuteAsync(TexturePaintCommandContextV2 context)
{
    foreach (string surfaceId in context.source.surfaceIds)
    {
        TexturePaintReadOnlyImage source = context.source.Get(surfaceId, TexturePaintChannel.Roughness);
        if (source == null) continue;
        Color[] pixels = source.CopyPixels();
        // Modify the copied data only.
        context.WriteTile(surfaceId, TexturePaintChannel.Roughness,
            new RectInt(0, 0, source.width, source.height), pixels,
            TexturePaintPluginColorSpace.Data, TexturePaintPluginBlend.Replace);
    }
    return Task.CompletedTask;
}
```

The host validates every command before the first mutation, copies submitted buffers, enforces declared channels and bounds, clips writes to reconstructed mesh coverage, creates or updates non-destructive layer data, updates only dirty rectangles, recomposes/re-packs logical channels, and records an undoable transaction. Cancellation or any exception retains the previous complete layer or mask state.

Albedo, Emission, and Skin Color Mask accept Linear or SRGB payloads and are canonicalized to linear working values. Normal, Metallic, Roughness, AO, Thickness, Detail Mask, Normal Control, and Custom data require `Data`. Normal commands require `Replace` and are vector-normalized by the host. Normal Control and the other scalar channels are constrained to grayscale after every plugin blend. Plugins cannot write directly to packed physical textures, and Normal Control is never exposed as an independent material texture or export output.

`declaredChannels` grants write permission. `readChannels` selects immutable input snapshots, so a
multi-channel generator does not copy every output before it starts. Leaving `readChannels` empty
retains the original behavior and snapshots all declared channels. Set
`channelSnapshotMaximumResolution` to bound the longest side of channel inputs while native output
dimensions remain available through `GetChannelInfo`; zero requests native resolution. All current
logical channels are supported, including Skin Color Mask, Thickness, Detail Mask, and Normal Control.
`WriteTile` preserves float/HDR values, while `WriteTileCompact` copies an RGBA8 payload and charges
four bytes per pixel to the command budget. Large generators should submit compact output in strips.
Built-in generators that create a fresh `Color32[]` for a command may instead use
`WriteTileCompactOwned` (or `WriteMaskTileCompactOwned`) and relinquish that array to the context.
The owned form avoids a second full-tile copy; the caller must never read or modify the array after
submitting it. Third-party plugins should keep using the copying methods unless ownership transfer is
explicitly safe.

Parameterized filters that read only one selected channel should implement
`ITexturePaintDynamicChannelUsageV2`. `ResolveReadChannels` may return a subset of the descriptor's
declared read contract for that execution. This prevents a Roughness-only adjustment from copying
every 4K channel while preserving discovery-time safety.

### Layer-mask target

Set `supportedTargets` to include `TexturePaintPluginTarget.LayerMask` only when the command has
meaningful grayscale behavior. During mask execution, `context.target` is `LayerMask` and
`context.source.GetMask(surfaceId)` returns an immutable `TexturePaintReadOnlyMask`. Submit grayscale
RGBA8 strips with `context.WriteMaskTileCompact`; the host derives luminance, forces RGB equal and
alpha to one, clips to mesh coverage, and swaps the result atomically. A plugin cannot obtain or
mutate the live mask target.

Mask plugin ID, version, typed parameters, stale/error state, and pixels persist in the document.
The Overlay Painter hides material-channel selectors for a mask execution. Compatible filters are
selected in **Layer Mask mode > Active Layer > Mask Filter / Generator**. Painting can continue over
the generated result afterward.

```csharp
private static readonly TexturePaintPluginDescriptor descriptor = new TexturePaintPluginDescriptor
{
    // Other registration fields...
    supportedTargets = TexturePaintPluginTarget.All
};

public Task ExecuteAsync(TexturePaintCommandContextV2 context)
{
    foreach (string surfaceId in context.source.surfaceIds)
    {
        if (context.target != TexturePaintPluginTarget.LayerMask) continue;
        TexturePaintReadOnlyMask source = context.source.GetMask(surfaceId);
        if (source == null) continue;
        Color32[] result = BuildGrayscaleMask(source, context.parameters);
        context.WriteMaskTileCompact(surfaceId,
            new RectInt(0, 0, source.width, source.height), result);
    }
    return Task.CompletedTask;
}
```

A pure generator may ignore the source values and use only its dimensions. A filter should transform
the snapshot. Both use the same cancellation, progress, memory-budget, geometry-clipping and atomic
commit rules.

## Immutable mesh maps

Add `TexturePaintPluginCapability.ReadsMeshMaps` and specify `requiredMeshMaps` on the descriptor.
If an older plugin declares `ReadsMeshMaps` without a request mask, the host supplies all maps.
Requested maps are generated lazily, cached by the texture set, copied into the transaction snapshot,
and counted against the snapshot memory budget.

```csharp
private static readonly TexturePaintPluginDescriptor descriptor = new TexturePaintPluginDescriptor
{
    // ...id, name, version, channels...
    capabilities = TexturePaintPluginCapability.Generator |
                   TexturePaintPluginCapability.ReadsMeshMaps,
    declaredChannels = TexturePaintChannelMask.Albedo |
                       TexturePaintChannelMask.Roughness,
    readChannels = TexturePaintChannelMask.Normal |
                   TexturePaintChannelMask.AmbientOcclusion,
    channelSnapshotMaximumResolution = 2048,
    requiredMeshMaps = TexturePaintMeshMapMask.WorldPosition |
                       TexturePaintMeshMapMask.WorldNormal |
                       TexturePaintMeshMapMask.SignedCurvature
};

TexturePaintReadOnlyMeshMap curvature =
    context.GetMeshMap(surfaceId, TexturePaintMeshMap.SignedCurvature);
```

Available maps are:

| Map | Encoding and use |
|---|---|
| `WorldPosition` | Linear RGB world coordinates; alpha is geometry coverage. |
| `WorldNormal` | RGB normal encoded from -1..1 to 0..1; alpha is coverage. |
| `SignedCurvature` | Grayscale: 0.5 flat, below 0.5 concave, above 0.5 convex. |
| `AmbientOcclusion` | Grayscale concavity-accessibility estimate; 1 is exposed. |
| `Thickness` | Linear grayscale geometry thickness estimate. |
| `SurfaceId` | Float components contain triangle, reconstructed-surface, and UV-island IDs; alpha is coverage. |

The signed curvature map is geometry-derived. A generator can also read the declared composed Normal
channel and calculate texture-scale curvature from tangent-normal derivatives. The included Agify
generator combines both signals and rejects normal derivative samples that cross UV-island IDs.

## Parameters and persistence

Use `TexturePaintPluginParameterDefinition` for Float, Integer, Boolean, Color, String, Texture,
Sprite, Font, MultilineString, Enum, Curve, and StripeList controls. A Font value is a persistent Unity `Font` asset
reference; capture its glyph atlas and metrics on the main thread before background pixel synthesis.
`Curve` stores an independent `AnimationCurve` per
layer or mask. `StripeList` stores an ordered collection of enabled horizontal or vertical stripes,
each with repeat-cell position, width, edge softness, opacity, and color. The host validates a maximum
of 64 entries and deep-clones every entry for profiles, layer persistence, and Undo/Redo. `Header` creates
a presentation-only, persistent collapsible section and is deliberately omitted from the serialized
parameter value set. The shared editor renders the schema automatically. Texture parameters are
GPU-snapshotted before execution and accessed with `context.GetTextureParameter(id)`, so a plugin
never depends on the source asset's Read/Write importer setting. Sprite parameters use the same call,
but the immutable snapshot contains only the Sprite's atlas rectangle; this prevents neighboring
sprites from leaking into tiled sampling. Generator/filter parameters are
stored per Plugin layer or mask as typed values, including Unity texture references; profile state
remains available to the other plugin workflows.

## Included production filters

The examples assembly includes six production filters. Add a **Plugin layer**, select the filter,
choose its source/output channels, adjust parameters, and click **Generate** or **Regenerate**.

- **Levels & Curves** — input/output black and white, gamma, an editable master curve, luminance-only
  hue preservation, and Amount.
- **Normal & Height Toolkit** — normal strength, Z reconstruction/normalization, green-channel
  convention flip, grayscale height-to-normal conversion, Reoriented Normal Mapping (RNM) detail
  combination with independent tiling/offset, and per-layer Normal Control strength.
- **Blur, Sharpen & Detail** — Gaussian, directional, bilateral and median filtering, Unsharp Mask,
  and High Pass. Radius is expressed in destination pixels.
- **Channel Operations** — invert, clamp/remap, grayscale, component shuffle, three-stop gradient
  map, tolerant color replacement, and seeded fractal color variation. Source and output may differ.
- **Morphology & Distance** — island-safe dilate, erode, feather, choke, outline, signed distance,
  edge detection, and bevel-height output. It requests `SurfaceId` so expansion never crosses UV
  islands.

The sixth filter, **Stylization, Kuwahara & Quantization**
(`com.uma.texturepaint.filter.stylization`), supplies edge-preserving Kuwahara,
RGB/luminance/custom-palette quantization, ordered dithering, and toon bands with texture-space edge
ink.

Levels & Curves, Blur/Sharpen/Detail, Channel Operations, and Morphology/Distance also support the
Layer Mask target. Stylization also supports Layer Mask. Normal & Height remains a material-channel filter because tangent normals do not
have a meaningful mask representation.

## Generator and filter layers

Create a **Plugin layer** with **+ Plugin**, choose a generator or filter from its properties, configure
the schema, and click **Generate**. A layer execution receives only its logical target surfaces and
reads the composed channels below its stack position. Output is cached in the same layer and is
replaced atomically on regeneration. Cancel or failure keeps the previous cache; Undo/Redo swaps the
complete previous and generated states.

Plugin layers use ordinary layer opacity, per-channel opacity/blend, groups, masks, effects,
persistence, and export. Changing plugin parameters or moving the layer marks it stale. Editing a
lower layer marks dependent Plugin layers above it stale. If the implementation is missing after a
document is reopened, cached output stays visible and the layer reports the missing stable plugin ID.
The manager window intentionally does not run generator/filter commands; it remains responsible for
discovery diagnostics and plugin categories that are not layers.

## Agify reference generator

Add a Plugin layer, choose **Agify — Dirt & Edge Wear**, configure it, and click **Generate**. It demonstrates:

- Signed concave dirt and convex wear selection.
- Composed tangent-normal detail added to geometry curvature.
- Source AO plus generated cavity accessibility.
- Optional Dirt/Wear textures and grayscale masks in UV or world-triplanar projection.
- Deterministic breakup, optional multi-octave fractal edge displacement, and independent material
  response controls.
- Albedo, Roughness, AO, Metallic, and Normal Control output on supported targets.
- Cancellation, progress, geometry clipping, provenance, persistence, and transaction Undo/Redo.
- Bounded 2K input snapshots plus strip-based compact output, allowing native 4K targets without
  retaining multiple full-resolution float buffers.

Agify bakes its procedural selection into generated channel alpha. Add a normal editable layer mask
to the resulting plugin layer for subsequent hand-painted refinement.

The example assembly also includes two focused generators built on the same signed-curvature,
composed-Normal, AO, UV-island, and strip-output contract:

- `DirtifyGeneratorPlugin` expands concave/AO gap selection using adjustable pixel radius, detection
  level, and spread, then applies configurable fractal breakup levels and dirt material outputs.
- `EdgeWearGeneratorPlugin` expands convex selection using independent edge radius, detection level,
  and spread, applies the same fractal controls, and writes exposed wear material outputs.

Their stable IDs are `com.uma.texturepaint.dirtify` and `com.uma.texturepaint.edgewear`.

### GPU generator contract

A generator with a compute implementation can also implement `ITexturePaintGpuGeneratorV2`, return
its kernel name through `GpuKernelName`, and declare `GpuAccelerated`. The host then keeps procedural
mesh maps and composed source channels on the GPU, binds schema parameters by their stable IDs, and
renders directly into the Plugin layer. If compute shaders or the kernel are unavailable, the normal
`ExecuteAsync` implementation remains the CPU fallback.

Standard compute bindings include `_MeshWorldPosition`, `_MeshWorldNormal`,
`_MeshSignedCurvature`, `_MeshAmbientOcclusion`, `_MeshThickness`, `_MeshSurfaceId`,
`_SourceNormal`, and `_SourceAO`. Schema parameters are bound as `_P_<parameterId>`; texture
parameters also receive `_HasP_<parameterId>`. The kernel writes `_Output`, uses `_OutputSize`, and
branches on `_OutputChannel`. Every GPU execution remains an atomic, undoable Plugin-layer
transaction and is subject to the same declared-channel and target-channel restrictions.

## Production material generators

The example assembly includes eleven deterministic material generators. CPU fallbacks run their
procedural synthesis away from the editor thread, report progress, honor cancellation between tile
rows, and commit through one atomic plugin-layer transaction. Dirtify, Edge Wear, and Dripping
Corrosion use the direct GPU generator path when compute shaders are available:

| Generator | Stable ID | Principal outputs |
| --- | --- | --- |
| Cloth Texture | `com.uma.texturepaint.cloth-texture` | Albedo, Roughness, Normal Control |
| Quilt, Embroidery, Perforation & Atlas Scatter | `com.uma.texturepaint.textile-surface` | Albedo, Roughness, Metallic, AO, Normal Control, or Layer Mask |
| Text | `com.uma.texturepaint.text-generator` | Albedo, Normal Control, Roughness, Metallic, or Layer Mask |
| Fabric Fuzz & Fiber Fray | `com.uma.texturepaint.fabric-fuzz` | Albedo, Roughness, Normal Control, Detail Mask |
| Rust, Oxidation & Corrosion | `com.uma.texturepaint.rust-corrosion` | Albedo, Roughness, Metallic, AO, Normal Control |
| Dripping Corrosion | `com.uma.texturepaint.dripping-corrosion` | Albedo, Roughness, Metallic, AO, Normal Control |
| Surface Noise & Micro Detail | `com.uma.texturepaint.surface-micro-detail` | Albedo, Roughness, Normal Control, Detail Mask |
| Veins & Subdermal Skin | `com.uma.texturepaint.veins-subdermal` | Albedo, Skin Color Mask, Roughness, Thickness, Normal Control, Detail Mask |
| Scar, Wound & Skin Damage | `com.uma.texturepaint.scar-wound` | Albedo, Skin Color Mask, Roughness, Thickness, Normal Control |
| Creature Skin Variation | `com.uma.texturepaint.creature-skin` | Albedo, Skin Color Mask, Roughness, Thickness, Normal Control, Detail Mask |
| Combat Scratches & Dents | `com.uma.texturepaint.scratch-dent` | Albedo, Roughness, Metallic, AO, Normal Control |

Each generator limits itself to channels actually present on the current target. A shared optional
Control Mask parameter supplies a grayscale generation boundary; an ordinary editable layer mask can
still be added afterward for non-destructive art direction.

Cloth Texture is a write-only procedural generator: it queries destination dimensions but does not
copy the existing composite. Select one of 16 weave constructions, set thread scale/aspect/rotation
and surface response, then enable only the Albedo, Roughness, and Normal Control outputs needed by the
material. Add vertical and horizontal stripe entries in visual stacking order; Position and Width are
fractions of the repeat cell, while Vertical/Horizontal Repeats control the number of cells across the
UV tile. Combining both directions produces plaid. A Sprite can be repeated across the whole cloth,
inside stripes, or outside stripes, with warp, weft, or diagonal direction plus independent tiling,
aspect, rotation, and offset. Color Fade uses broad fractal regions biased by thread crowns/fibers and
can also alter roughness and flatten Normal Control in worn areas.

The textile-surface generator keeps Quilt, Embroidery, Perforation, and regular-grid Atlas Scatter in
one persistent schema. It writes coordinated material channels or grayscale layer/group-mask
coverage. Texture and atlas inputs use immutable parameter snapshots.

The Text generator captures a selected Font atlas and glyph metrics on the main thread, then performs
block placement or background ribbon warping. Ribbon mode dynamically reads the composed Custom
channel below the Plugin layer: an editable Path/Ribbon supplies a white/gray guide, and Text extracts
a smoothed centerline from that immutable snapshot. Layer Mask execution writes grayscale glyph
coverage so one text shape can drive a Group material stack.

The scar generator demonstrates a guided multi-layer workflow. Put a Paint or Path layer below the
Plugin layer, author a white shape in its **Custom** channel, choose **Custom Ribbon Channel** for the
plugin's Guide Source, and regenerate. Changes below mark the plugin stale. The Custom composite is
read-only input and produces synchronized skin color, microsurface, thickness, and height response.
An automatic procedural guide and a grayscale parameter texture are alternatives. Because the guide
is a regular ribbon layer, the established spline point/width/tangent tools provide precise scar art
direction without giving the plugin mutable access to path internals.

The combat-damage generator is a feature-morphology reference: it synthesizes bounded impact bowls,
crater rims, point pings, finite tapered cuts, side burrs, and parallel glancing tracks, then blends
three world projections by the mesh World Normal. Signed Curvature biases coating loss toward convex
armor edges and AO can add embedded grime. Painted/coated mode limits Metallic output to chipped
regions; bare-metal mode preserves metal response throughout the generated damage coverage.

## Budgets, cancellation, and diagnostics

`PluginHost` independently budgets immutable snapshots, queued commands, and artifacts. Command count is capped, payload sizes are checked, and cancellation is checked during snapshot and commit work. The plugin must cooperate during its own asynchronous work.

The host reports registration, duration, command count, dirty pixels, cancellation, and exception diagnostics. Plugin exceptions are isolated from the stage and never leave partially replaced layer output.

See `ExampleBrushPlugin`, `ExampleModelPlugin`, and `AgifyGeneratorPlugin` for working v2 examples.
