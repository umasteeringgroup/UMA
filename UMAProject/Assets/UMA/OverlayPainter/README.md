# UMA Overlay Painter

Overlay Painter is a Unity 6.3+ preview stage for painting generated `DynamicCharacterAvatar` materials without the color multipliers used while UMA composes overlays. It reconstructs every generated renderer into one static preview mesh per material, preserving the baked geometry, source UVs, source material, and generated texture set.

## Open the stage

### From a slot (recommended for authoring)

1. Select one `SlotDataAsset` and expand **Slot Utilities** in its Inspector.
2. Click **Open in Overlay Painter**.
3. Choose either an `UMAMaterial` or an `OverlayDataAsset`, review the material capability summary and working resolution, then click **Open**.
4. For a UDIM slot, review the tile/member source table. The complete exact-ID group opens, frames, selects, and paints as one logical target; individual tiles are diagnostic members, not independent paint targets.

This path reconstructs the slot directly from `UMAMeshData` and never searches for or generates an avatar. An `UMAMaterial` source starts with a removable **Default White** flat Fill layer and semantic-neutral bases. An `OverlayDataAsset` source uses that overlay's material and textures as immutable bases. For UDIM groups, assign compatible overlays explicitly per member; unassigned members receive semantic-neutral bases rather than guessed companion overlays.

### From an avatar

1. Put a `DynamicCharacterAvatar` in an open scene and generate it.
2. Select the avatar and expand **Utilities** in its Inspector.
3. Under **Overlay Painter**, click **Open Overlay Painter**.
4. Select one or more slots, select OverlayData when using the Overlay source, then choose a target channel, tool, and brush. Paint directly in the 3D Scene view.

The controls open in a standard dockable **Overlay Painter** workspace. It has a global toolbar, tool rail, synchronized UV canvas, brush Asset Shelf, and a space-saving tab group that switches between the searchable Paint Target navigator and the combined Layer/Path stack with contextual Properties inspector. Brush and Stroke/Projection controls live in the separate dockable **Overlay Painter Brush** window. Regions can be hidden or restored from **Layout**, and workspace selections persist in the avatar recipe state. Drag the tabs beside the Scene view for a synchronized painting layout. The windows can also be reopened from **Window > UMA** while the stage is active. See [Milestone 8 Workspace](MILESTONE_8_WORKSPACE.md).

The paint source uses a three-way **Texture / Overlay / Color** selector. The Texture tab accepts either a complete Texture2D or an individual Sprite from a sprite sheet. A Sprite is extracted into a cached temporary texture while its Sprite asset remains the persisted layer source; neither the Sprite nor its sheet is modified. Extraction is channel-aware: changing the active channel selects or creates the matching cached texture. Normal-channel Sprite regions, raw RGB normal textures, and textures imported with Unity's Normal Map importer are converted to linear, normalized tangent-space RGB before painting. The selected OpenGL/DirectX convention describes the source; DirectX green is flipped into Overlay Painter's canonical OpenGL working representation before vector blending. Cache entries are separated by channel and convention, so the same Sprite can safely be used as both color and normal data. Texture samples the selected image inside each brush stamp, Color uses the selected solid color, and Overlay provides an **OverlayData** popup aggregated from the selected slots. Overlay textures are routed together to Albedo, Normal, Metallic, Roughness, AO, or Emission according to the overlay material's UMA channel keywords. The separate Paint Target control chooses whether those results affect the editable base copy or a non-destructive layer. Source assets are never changed in place.

The layer toolbar uses split **+ Paint** and **+ Fill** buttons. Their main areas retain the quick empty-layer actions. The Paint menu can create an empty layer or create one from a Sprite Set, then assigns the selected sprite's matching material sheets to every supported layer channel. The Fill menu can create the current Fill or select any compatible `OverlayDataAsset`. An Overlay-backed Fill adds every compatible logical material channel, uses `alphaMask` (or the first texture's alpha) as common coverage, and carries supported UMA blend modes into the corresponding channel settings. **Rasterize to Paint Layer** in the Fill layer's row menu bakes all generated channels and effective mask into editable pixels and removes the Overlay source relationship.

## Painting

- Channels: Albedo, Normal, Metallic, Roughness, Ambient Occlusion, Emission, Skin Color Mask,
  Thickness, Detail Mask, Normal Control, and a custom channel. Normal Control is an automatically
  available painter-owned grayscale height modifier whenever Normal is supported: neutral gray is
  unchanged, dark recesses, and light raises. It is combined into the effective normal for both 3D
  preview and export, but is never bound to the material or exported as a standalone texture. The
  full toolbar automatically becomes a channel dropdown when a material exposes more than seven
  logical channels.
- Tools: paint, erase, blur, smear, clone, dodge, burn, normal touchup, and a brush-plugin mode.
- **Cap Update Per Stroke** accumulates each target texel against its stroke-start color. Hardness and the brush falloff define the maximum coverage at that location, while Flow controls how quickly paint builds toward it. Repeated stamps therefore preserve a Photoshop-style soft perimeter instead of filling every grazed edge to 100%; a pass closer to the brush center can still increase that texel's coverage.
- Hardness attenuates both color and stored layer alpha. Source alpha is combined with brush coverage once using source-over math, so partially transparent paint is not unintentionally squared.
- 3D strokes and world-authored paths are resampled by cumulative world-space arc length. The 2D canvas paints directly in normalized UV space and resamples on that texture plane; it never round-trips ordinary brush stamps through mesh triangles. Hold **Shift** when beginning a freehand stroke to lock it horizontally or vertically on the active view plane; the first meaningful drag delta chooses the axis for the complete stroke. Spacing remainder carries across editor events, and pressure, time, direction, and the appropriate world or UV anchors are retained in persistent stroke records.
- Stabilization, direction filtering, pressure-to-flow, pressure-to-size, projection depth, surface-normal angle, and backface controls are available with the brush settings. **Follow Stroke** rotates directional stamps along the filtered path.
- Brushes: circle, square, or texture stamp with size, hardness, flow, spacing, rotation, blend mode, and optional global-X mirroring. Splatter supports an adjustable offset radius and optional **Random Strength**, which independently varies each scattered stamp from zero to the current paint strength. Brush assets and per-Paint/Path snapshots retain all randomization settings.
- Every Paint, Fill, Path, or Group layer can own one editable grayscale layer mask. Add a black or white mask from the layer-row menu, click its thumbnail to enter **Layer Mask mode**, and paint it in either the 2D or 3D view with the regular brush tools. Mask painting is deliberately channel-free and grayscale-only: **Mask Value** selects a scalar from black (hidden) to white (visible), and texture, sprite, material-channel, and OverlayData paint sources are unavailable. Both views display a Layer Mask label; **Solo Mask** previews the effective mask on the model. **Layer Mask Noise** and **Layer Mask Texture Overlay** provide non-destructive texture variation without changing the mask brush source. **Fill Polygon** and **Fill UV Island** are ordinary paint operations on Paint layers and masks. Groups apply opacity, blend mode, and their mask once to the isolated child composite.
- Slot targets: one or more reconstructed UMA slots can be selected together. In 3D, brush application is based on the full world-space footprint rather than only the center ray: every selected slot and UV island intersected by the brush receives a projected stamp, including slots backed by different materials before the cursor center crosses their border. Per-triangle slot ownership excludes unselected geometry, and a cached spatial triangle grid keeps footprint queries local. In 2D, the active texture set is painted directly wherever the normalized-UV brush lies; ordinary brush movement and rasterization do not query or clip against the mesh.
- Geometry clipping: projected 3D contacts use a cached per-texel UV geometry selector so rectangular update tiles cannot leak into unrelated polygons. The dedicated **Polygon Fill** and **UV Island Fill** buttons intentionally resolve mesh ownership in both views, including Layer Mask mode; ordinary 2D brush strokes do not.
- Surface paths: click **Create Spline Layer** to create and activate a dedicated layer, then choose **Spline Space: 2D Texture** or **3D Surface** in Properties. A path has exactly one editing and raster domain; input from the other view is ignored instead of silently converting the path. New paths default to 3D Surface. For a 3D path, **Shift+Click** the model to append a surface node and use the Scene view to select, insert, move, and adjust controls. It interpolates in world space, remains continuous across UV seams and UDIM tiles, and resolves UVs from projected surface samples only while rasterizing. Projection prefers the connected polygon strip containing each control, so a long strap or belt segment does not fall onto a nearby disconnected layer of the same mesh. Separate reconstructed surfaces remain eligible at real slot and UDIM transitions. A 2D path is authored and rasterized directly in normalized texture space without model-overlap ambiguity. Selecting one of its points in the 2D canvas exposes only that point's complete orange/green/blue adjustment setup on the model as a positioning aid; the 3D view does not draw or convert the 2D spline. Both views show green incoming/outgoing handles for reshaping the selected Bezier point and a distinct blue perpendicular handle for changing its width percentage directly. The point remains selected after moving or adjusting a handle; deleting it selects a surviving neighbor or clears selection when no points remain. Both 2D and 3D ribbons use the same intrinsic across/along renderer, so edge-specific strokes, shadows, glows, bevels, stitches, and edge fade behave consistently in either domain. **Ctrl+Click** (**Command+Click** on macOS) within 8 screen pixels of the editable spline splits the nearest segment; clicking farther away does not insert. Nodes support insert/delete, multi-select dynamics, copy/paste, reverse, corner/smooth/broken/custom/straight tangents, and per-point pressure, width percentage, flow, roll, color, and offset. Right-click a point in either editing view for linear handles, deletion, and width presets. Apply as stamps, a gap-free continuous stroke, complete source-image ribbon tiles fitted edge-to-edge, or a closed fill, with Follow Path/fixed-axis orientation, caps, mirroring, radial symmetry, and any paint/erase/blur/smear/normal-touchup tool.

When an enabled Path layer is active, the Scene view shows a separate **Overlay Painter Path** toolbar. Disabling the layer hides its raster result and all 2D/3D spline authoring overlays; re-enable it to edit. **Standard** mode provides topology, anchor movement, curve handles, and width handles; **Move** exposes only selectable/movable anchors; **Adjust** locks anchor positions and exposes curve and width adjustment. These modes constrain direct viewport gestures only: Closed Path and every point/path action in the toolbar remain available in Standard, Move, and Adjust. The toolbar owns those commands so they are not duplicated in Properties. **Auto Update** is enabled for new and legacy paths and rebuilds after edits using the coalesced GPU path pipeline. Disable it to make any number of topology, position, tangent, or width edits without rerasterizing; **Update** explicitly rebuilds every affected channel and path effect when ready. The edit mode and Auto Update choice are stored per Path layer.

3D spline controls and samples remain surface-projected. A 2D spline's raster and stored curve stay on the texture plane; only the selected point's temporary Scene-view adjustment handles are projected onto the model.

Stroke kernels copy, compose, and repack only dirty tiles, including each filter's required halo. Safe paint operations run in place, and spline/path samples are submitted in bounded GPU batches so continuous paths do not require one dispatch per stamp. Capped stroke coverage is allocated as sparse 128-pixel tiles instead of full-size float textures. When compute shaders are unavailable or a configured memory budget would be exceeded, the same brush and normal math runs through a sparse CPU fallback.

Undo/redo stores only touched tiles. Before/after pixels are captured through asynchronous GPU readback where supported, kept in the target's exact graphics format, and losslessly Deflate-compressed. Undo work and memory therefore scale with painted area rather than total texture size. The **Performance & Memory** panel exposes rolling preview p95/maximum latency, dirty copy/compose counts, compute/fallback counts, undo and active-stroke memory, configurable budgets, and a resource-baseline leak check.

In the 3D view, brush size is evaluated through a per-triangle UV-to-world metric rather than a scalar UV radius. Circle, square, and stamp brushes therefore retain their world-space dimensions across slot seams with different UV density, rotation, or anisotropic scaling; conservative UV bounds keep the complete projected footprint inside the updated tile. In the 2D view, the same size value is a stable normalized-UV radius, so the cursor and painted footprint remain fixed while moving across triangles of any world-space size.

## Normal touchup

On stage initialization, each material mesh is rasterized in UV space to create interpolated vertex-normal and tangent maps on the GPU when supported, with a deterministic CPU fallback. Coincident vertices with discontinuous UVs create a seam lookup map. These maps are reference-counted and shared by a hash that includes mesh positions, normals, tangents, topology, UVs, dimensions, and seam width. An on-demand, bounded-resolution procedural cache supplies world position, world normal, signed curvature, concavity accessibility, thickness, and triangle/surface/island ID maps to masks and model plugins without adding startup cost to every material. Signed curvature uses 0.5 for flat, values below 0.5 for concave regions, and values above 0.5 for convex regions. Long map builds accept cancellation and progress reporting. The normal-touchup kernel converts the painted tangent-space normal to model space, bends it toward the interpolated vertex normal, optionally blends its seam partner, then converts it back to tangent space. Ordinary normal painting is vector-aware on both GPU and CPU, and export supports OpenGL or DirectX green-channel convention.

## Brushes and plugins

The Asset Shelf discovers every `BrushPreset`, renders stamp/shape thumbnails, and supports search across name, folder, and comma-separated tags. It includes folder filtering/creation, favorites, recents, rename, duplicate, custom drag order, Project-window drag-and-drop, and texture-to-session-stamp drop. Open **Brush Library** to create a `BrushLibrary`, add/remove presets, and import/export preset JSON. Two starter assets live in `Brushes/`. **Auto Fade** and **Auto Taper** keep the live stroke at full flow and size while dragging, then rerasterize it on release so the selected envelope spans the completed stroke from start to end; non-auto Fade and Taper continue to use World Length.

Plugin API v2 provides versioned brush, filter, generator, baker, importer, and exporter extension
points. Discovery validates stable IDs, capabilities, content/mask targets, read/write channels,
requested mesh maps, snapshot limits, and typed schemas including curves, sprites, stripe lists, and
Font assets. Plugins receive immutable snapshots and bounded output contexts rather than live
textures or the `TextureStore`; the host enforces channel semantics, grayscale masks, persistence,
cancellation, Undo/Redo, and diagnostics. Generators and filters are persistent Plugin layers and can
also target compatible layer/group masks. The production filters are **Levels & Curves**, **Normal &
Height Toolkit**, **Blur, Sharpen & Detail**, **Channel Operations**, island-safe **Morphology &
Distance**, and **Stylization, Kuwahara & Quantization**. See [Plugin API v2](PLUGIN_API_V2.md).

GPU-capable generators use the standard compute-generator contract to consume cached mesh maps
directly on the GPU, avoiding channel snapshots, per-pixel CPU synthesis, and tile-copy overhead.
Built-in CPU fallbacks process independent rows in parallel and transfer ownership of completed
compact tiles to the host, so compute-less systems retain the same transactional behavior.

Artists should also read [Overlay Painter Generators and Filters](../Docs/OverlayPainterGeneratorsAndFilters.md),
which covers every included generator/filter, practical material stacks, parameter strategy, and common
failure modes.

The typed parameter schema also supports sprites, ordered stripe lists, and Unity Font assets.

Agify also offers optional multi-octave fractal boundary breakup. The focused **Dirtify — Gap Dirt**
and **Edge Wear** generators add independent detection size, level, spread, and fractal controls for
cavity dirt and convex wear. Both use the GPU generator path when compute shaders are available.

**Dripping Corrosion** combines exposed-edge and concave-valley detection with occlusion,
world-gravity trail tracing, multi-octave breakup, pits, and raised oxide crust. Spread, drip length,
drip width, breakup size, and pit size are expressed in meters using Unity's 1 unit = 1 meter scale.
It writes coordinated Albedo, Roughness, Metallic, Ambient Occlusion, and Normal Control channels.

Eleven production material generators are also included as first-class Plugin layers: **Cloth Texture**,
**Quilt, Embroidery, Perforation & Atlas Scatter**, **Text**, **Fabric Fuzz & Fiber Fray**, **Rust,
Oxidation & Corrosion**, **Surface Noise & Micro Detail**, **Veins & Subdermal Skin**, **Scar, Wound &
Skin Damage**, **Creature Skin Variation**, and **Combat Scratches & Dents**.
They use deterministic
world-space synthesis, signed curvature/AO/thickness/surface maps, native-resolution tile output,
grayscale control textures, layer masks, cancellation, progress, persistence, and Undo/Redo. Their
properties are divided into persistent collapsible sections. Output is coordinated across the
material channels supported by the target instead of producing an isolated color texture.

**Cloth Texture** builds cotton/plain, knit, twill, corduroy, herringbone, denim, canvas, linen,
satin, basket, houndstooth, leno, dobby, pile, crepe, and jacquard-inspired constructions. It can
write Albedo, Roughness, and neutral-gray Normal Control independently. Its ordered repeatable stripe
list supports mixed horizontal and vertical bands for pinstripes and plaid; an optional cropped Sprite
adds colored or tinted motifs in a chosen weave direction. Thread-aware wear fades color, changes
roughness, and flattens exposed thread crowns without erasing the weave structure.

**Quilt, Embroidery, Perforation & Atlas Scatter** supplies padded/sewn panels, directional
texture-driven embroidery, punched/recessed layouts, and deterministic randomized cells from a
regular atlas. It coordinates Albedo, Roughness, Metallic, AO, and Normal Control or creates
grayscale layer/group mask coverage. **Text** renders an optional Unity Font in block layout or along
an editable Custom-channel Path/Ribbon guide, with independent Albedo, Normal Control, Roughness,
Metallic, or mask-only output. **Stylization, Kuwahara & Quantization** adds edge-preserving painterly
abstraction, RGB/luminance/palette reduction, ordered dithering, and toon bands/edges.

For a directed scar, create a Paint or Path layer beneath the Plugin layer, add a **Custom** channel,
paint or draw the white guide on black, then choose **Custom Ribbon Channel** as the scar's Guide
Source. The Custom channel is an authoring guide and is not a shader texture. Regenerating the plugin
turns the guide into coordinated Albedo/Skin Color Mask, Roughness, Thickness, and Normal Control.
Automatic lines and an imported grayscale Guide Texture are also available. Skin generators can
either build a complete skin surface or add sparse biological variation over the existing stack;
freckles shrink and fade at UV-island and painted control-mask boundaries. The human skin system
also coordinates branching vessels, staged bruises, pigment variation, mottling, capillary redness,
pores, localized oiliness, fine wrinkles, and subsurface thickness breakup. Surface Micro Detail
provides distinct Perlin, Cell, Voronoi, Ridged, and Hybrid noise families.

**Combat Scratches & Dents** builds armor damage from recognizable physical events rather than a
uniform grunge field: clustered eased dent bowls, irregular displaced rims, nested weapon-tip pings,
finite tapered gouges, raised burr lips, broken glancing scrape bundles, coating chips, and
curvature-biased edge wear. Bare-metal and painted/coated workflows coordinate Albedo, Roughness,
Metallic, AO, and Normal Control. The Wear History preset ranges from Light Skirmish to
Battle-Ruined while every distribution, size, depth, direction, material, and exposed-edge control
remains independently editable.

## Layers, documents, and persistence

Grayscale channels in the 2D window provide a **Brightness / Contrast Curve**. Its horizontal axis is the original grayscale value and its vertical axis is the amount of Brightness and Contrast applied. The default diagonal leaves black untouched and progressively increases the adjustment toward white. The curve is stored per channel and participates in Undo/Redo, preview, document persistence, and export.

On a Fill layer, **Use Transform For All Channels** appears on the first channel. It makes that channel the master for X/Y tiling, X/Y offset, and rotation, immediately synchronizes the remaining channels, and locks their transform controls until sharing is disabled.

Paint, fill, spline, Plugin, and group layers are independent transparent surfaces composited from bottom to top on the GPU. Layers can be hidden, reordered, renamed, duplicated, merged, deleted, and adjusted globally or per channel with visibility, paint lock, opacity, and blend controls. Plugin layers expose cached output channels rather than painting/source controls and cannot be merged down. Each authored channel card owns its source and includes an undoable Remove operation. Texture and overlay sources on Fill layers provide independent X/Y tiling, X/Y offset, and rotation controls for both Flat and Triplanar projection. Paint and spline channels additionally expose Channel Paint Strength for scaling newly authored pixels; Fill layers do not use or show it. The 2D window provides non-destructive Brightness, Contrast, Hue, Vibrance, Saturation, and RGB Color Balance for the currently displayed channel; values persist per channel, participate in Undo/Redo, and feed both preview and export. Chromatic controls are disabled for grayscale data and all image adjustments are disabled for encoded normal vectors. The layer-row **fx** button opens an ordered, multi-instance stack of non-destructive Stroke, Inner/Outer Shadow, Inner/Outer Glow, Color Overlay, Texture Overlay, Image Adjustments, ribbon-specific effects, and mask-only effects when a mask is present. Effects can target only channels authored by the layer. **Image Adjustments** changes Saturation, Brightness, Contrast, and Hue on one selected channel, with an independent Amount control; Hue and Saturation are disabled for grayscale channels. Layer/channel opacity and masks are applied once to the isolated paint-plus-effects result, so translucent art and falloffs do not become solid through repeated passes. Widths and shadow offsets are measured in destination pixels, shadow/glow falloff uses an editable curve, and every conventional effect has an independent Level control. **Texture Overlay** clips two optional repeating textures to existing coverage; each source has independent X/Y destination-UV tiling, offset, center rotation, blend mode, opacity, and RGBA color multiplier. Texture 1 is combined first and Texture 2 second. On a ribbon, effect instances are evaluated as ordered projection passes from the traveler's long-edge coordinates. Inner/Outer Shadow and Inner/Outer Glow can target **Left**, **Right**, or **Both** edges without wrapping across beginning/end caps. **Bevel Edge** assigns a light or dark treatment and offset to either long edge. **Procedural Stitch** generates single or double dashed thread rows. Ribbon path properties can replace the first and final complete tiles with separate Beginning/End textures or sprites; closed ribbons ignore them. Group rows use a folder icon and support nested groups. Drag a layer or complete group subtree onto a folder, click to collapse/expand, or select a group before creating a child. Children remain contiguous; Remove from Group moves the complete subtree above its former parent. Group visibility, opacity, blend, and mask apply once to the isolated descendant composite. Duplicating a group deep-copies every descendant. Deleting a group warns and deletes every descendant. Merge Down is limited to adjacent Normal-blend siblings because other blends cannot be flattened exactly without a backdrop. Structural edits, channel edits, masks, effects, and channel adjustments participate in Undo/Redo, with continuous slider gestures coalesced into one history step.

Reusable **Material Preset** assets save a whole layer stack, one layer, or a complete group subtree, including hierarchy, all authored channels, compressed raster pixels, Fill projections, masks, effects, paths, plugin ids/versions, parameters, and cached procedural output. Use **File > Material Preset**, either add-layer dropdown, or a layer row's **Preset** menu to save or apply one. Application targets the currently selected logical paint target, maps semantic channels supported by each destination material, scales cached pixels to the working resolution, warns before transferring UV-dependent content to a different layout, and creates a named wrapper group. Available layer and mask generators rerun sequentially from the bottom of the saved stack to the top so each filter sees the regenerated composite below it. Missing plugins retain their cached result and remain stale. Cancellation or generator failure rolls back the complete application, and a successful application is one Undo/Redo step. Applied layers are independent copies but retain the source preset id and revision for provenance.

Select a Material Preset asset and click **Package** in its Inspector to create a single distributable `.asset`. Packaging embeds copies of the thumbnail, textures, sprites, OverlayDataAssets, brush presets, UMA materials, Unity materials, fonts, and externalized raster payloads used by the stack, then rewrites the preset to those embedded copies. Plugin implementations and shaders remain project prerequisites and are listed on the packaged asset. A packaged preset appears in the same Material Preset picker and applies in Overlay Painter exactly like a regular preset; the original loose texture assets are not required.

Opening the stage starts a temporary session without changing the character, scene, recipe, source overlay, or source textures. After an edit, recovery creates `painter_recovery.asset` and a sibling `painter_recovery Data` folder in the recovery folder configured under **Project Settings > UMA > Editor Settings**. The default folder is `Assets/UMA/Temp`. The next compatible launch offers Recover, Discard, and Cancel choices. **Save As** creates a permanent, versioned `TexturePaintDocument` wherever the user chooses below `Assets`, with compressed pixel blobs in a sibling `<Document Name> Data` folder. Double-click a compatible document asset in the Project window to open it directly, or use **File > Load Document** in an active Overlay Painter stage. Later recovery and document saves reuse unchanged content-addressed data assets and write only changed texture targets. GPU capture uses asynchronous readback and compression runs away from the editor thread. Closing while the document is modified or a matching recovery asset exists offers **Save**, **Discard**, and **Cancel**. Save commits the project document before deleting recovery, Discard deletes recovery and closes without saving, and Cancel leaves both the editor session and recovery untouched. If a background autosave is already running when Close is requested, the window closes immediately and that in-flight save becomes the final close-save; the stage exits automatically after its durable commit. Export completion restarts the autosave debounce so a long asset import cannot unexpectedly begin a document capture while the export and painter windows are being closed.

For an active avatar, generated material metadata is used only to locate source slots, their original overlay stacks, and the material shader contract. Each generated material surface is separated back into native slot surfaces, UVs are restored from the corresponding `SlotDataAsset`, and channel composites are rebuilt from the original overlays at the native base-texture resolution. Overlay Painter never uses `GeneratedMaterial.resultingAtlasList` as editable source data; the generated atlas may already be resized, filtered, and normal-swizzled and is therefore unsuitable for lossless authoring.

The document losslessly persists editable base pixels, layer pixels, masks, splines, per-layer source/brush settings, and workspace state. Surfaces bind through stable material/slot/mesh identities rather than temporary reconstruction indexes. Export reads either the live flattened composite or a temporary authored-layers-only composite, according to the selected export mode, without saving or changing the document, avatar, recipe, or scene. Recovery assets are temporary project content and should not be shipped. To keep the default recovery location local, add both `/Assets/UMA/Temp/` and `/Assets/UMA/Temp.meta` to the project's source-control ignore file. If the recovery folder setting is changed, ignore the configured folder and its `.meta` file instead.

Only one `painter_recovery.asset` is active in the configured folder. It records its source context and is offered only to a matching Overlay Painter launch. A later temporary session can replace recovery belonging to another context. **Save As** creates the permanent editable document; deleting or ignoring the recovery folder does not remove saved documents or exported overlays/textures.

Shader-aware material targets compile the active URP or HDRP material selected by the `UMAMaterial` into one capability descriptor before the stage opens. That descriptor validates shader properties, RGBA meanings, source textures, formats, color space, importer rules, and GPU packing support, then drives logical-channel creation, preview binding, and export. Packed maps are unpacked into their declared logical channels and repacked into their documented physical layouts. Smoothness is inverted into the Roughness editing convention. Skin Color Mask remains RGBA color data, while Thickness and Detail Mask are editable scalar data. Linear data remains linear, color data remains sRGB, and albedo export dilates RGB beyond transparent UV borders to prevent mip seams. Built-in/Standard is not part of the certified workflow.

Normal Control is document-owned auxiliary data and deliberately does not participate in material
capability discovery or physical packing as an independent channel. A matching-resolution neutral
gray target is added automatically beside every logical Normal target. The selected Normal preview,
group preview, physical material binding, flattened bake, and authored-overlay bake all consume the
same derived effective normal. Authored-overlay export derives a flat-relative normal delta and
includes its gradient coverage in `OverlayDataAsset.alphaMask`. Documents and recovery snapshots
retain its pixels and per-layer sources. **Height Strength** is stored independently on each
layer's Normal Control channel, so changing a paint, fill, or path layer does not rescale other
height contributions. Sample radius and height inversion remain shared conversion settings for the
texture target.

Each `UMAMaterial.MaterialChannel` has editor-only **Overlay Painter Channel Layout** and **Physical Output / Import** sections. Automatic mode follows known URP/HDRP conventions and material settings; Custom mode provides explicit RGBA mappings, PNG/EXR encoding, importer type, color space, alpha, normal convention, mipmap, compression, filtering, anisotropy, maximum-size controls, and optional TextureImporter platform overrides. The export window previews the resolved contract and actionable diagnostics before writing files.

**Export Textures & UMA Assets...** opens a dockable, descriptor-driven export window organized into Content, Naming and Location, Texture Output, Advanced, optional Material Contract Details, and the resolved output plan. Enter the required **Export Identifier** and choose **Flattened Composite** or **Runtime Overlay (Transparent)**. Flattened Composite is the default and exports the reconstructed character texture plus visible painter layers. Runtime Overlay excludes the reconstructed base and direct base painting, composites only visible authored layers/groups into temporary transparent textures, exports only material channels with authored content, generates a grayscale RGBA coverage texture, and assigns that texture to `OverlayDataAsset.alphaMask` for UMA runtime blending. The coverage mask is the union of visible authored channel alpha after layer opacity, group opacity, masks, and effects. Runtime Overlay always creates separate assets and cannot overwrite a source overlay. Release export writes physical textures in `UMAMaterial.MaterialChannel` order using each channel's descriptor for RGBA packing, semantic conversion, PNG/EXR encoding, color space, normal convention, and importer settings. Names use the persistent source texture stem when available, fall back to the slot/group and material-channel name, and append the UDIM tile number for tile outputs. No export-template asset is required: session defaults provide current/all-material scope, overwrite/fail/versioned conflict policy, native or fixed resolution, and edge padding. When the Addressables package and `UMA_ADDRESSABLES` scripting define are both enabled, **Mark Addressable** can also register the exported assets in the default group. The core Overlay Painter assemblies do not reference Addressables, so projects without that optional package compile and export normally. Use **Save Overrides as Template** only when those output choices should be reused. Albedo padding is performed on the GPU, preserves the original alpha, and reports/cancels between passes even for large outputs.

Generated-character export reconstructs the source from the character's original slot and overlay
textures at their native working resolution; it never reads the UMA generated atlas. Normal output
is rebuilt as ordinary tangent-space RGB from logical vector data before the requested OpenGL or
DirectX convention and importer settings are applied.

Export is a transaction: every texture is baked and encoded into staging before commit, existing files/importers/UMA assets are snapshotted, and newly created files, overlays, UMA index entries, and—when enabled—Addressables entries are rolled back together on cancellation or failure. A new recipe-ready `OverlayDataAsset` is created and registered with `UMAAssetIndexer` for each ordinary slot or UDIM tile, and library lookup is verified before success is reported. Export never creates material overrides, updates a recipe/avatar, saves the paint document, or stores history in it. **Overwrite Source Overlay** is a separate destructive mode available only for persistent source overlays; it previews every affected asset, requires confirmation, and restores backups on failure.

Documents fingerprint geometry, topology, UVs, and material bindings independently. Stable matches restore exactly; compatible material or reconstruction changes rebind automatically. When topology still matches but UVs change, surface-anchored strokes and spline paths are retained for rerasterization while stale raster pixels are preserved as explicitly orphaned document content. Layer masks retain their base value, grayscale paint value, and procedural effects but reset stale pixel-space painting to the base value. Removed or incompatible surfaces are reported in the export window instead of being silently discarded or painted onto the wrong mesh.

## Tests

Run `QA/Run-TexturePaintReleaseGate.cmd` for the blocking release suite, or open **Window > UMA > Overlay Painter > Release Gate** for editor preflight. The runner launches `UMA.TexturePaint.Editor.Tests` and `UMA.TexturePaint.Tests` in clean Unity processes and writes NUnit XML, logs, and JSON/Markdown summaries under `Logs/TexturePaintReleaseGate`. Tests cover production-GPU reference images for all core tools and blend modes, multi-slot painting, layer composition, document save/reopen, persistent workspace and asset-shelf state, export round trips and packed maps, adaptive gap-free paths, 2D/3D point-handle editing and selection repair, masks, per-layer Normal Control strength, deterministic brush randomization, barycentric/UV mapping, different UV densities, mirrored/overlapping UVs, 1K–4K targets, format precision, sparse undo/redo, lifecycle leaks, transactional cancellation, plugin isolation, material pipelines, and memory/performance budgets. See [Milestone 9 Release Gate](MILESTONE_9_RELEASE_GATE.md). For an interactive smoke test, use any existing generated UMA sample avatar; the stage intentionally consumes the selected avatar instead of shipping a duplicate race-specific prefab.

## Extension points

- `TextureStore` owns material/channel targets and ordered layers.
- `PaintingEngine` owns stroke dispatch, CPU fallbacks, and history.
- A transient internal geometry selection is used for projection and polygon/UV-island fill operations; user-authored visibility is owned only by each layer's editable grayscale mask.
- `MeshReconstructor` provides per-material meshes, colliders, triangle islands, and mirror raycasts.
- `TexturePaintStageController` is editor-UI agnostic and can be driven by another tool or custom stage.
