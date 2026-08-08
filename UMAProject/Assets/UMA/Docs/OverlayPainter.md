# Overlay Painter

Overlay Painter is UMA's non-destructive surface-painting workspace for creating texture details directly on a reconstructed UMA slot or generated character. It combines a 3D paint view, a synchronized 2D UV canvas, material-aware channels, editable layers, surface paths, masks, and recipe-ready export.

Use it for work such as:

- Skin details, makeup, scars, tattoos, dirt, and damage.
- Clothing graphics, fabric variation, seams, piping, and stitches.
- Painted metallic, roughness, ambient-occlusion, normal, and emission details.
- Repeating materials and coordinated multi-channel texture sets.
- Details that cross UV seams, slot boundaries, or UDIM tiles.
- New `OverlayDataAsset` content that is ready to add to a recipe.

Overlay Painter does not paint into the selected source textures. It works in a temporary or saved `TexturePaintDocument`, then exports new textures and UMA overlay assets when requested. The source slot, source overlay, avatar, and recipe remain unchanged unless the separate destructive **Overwrite Source Overlay** export mode is deliberately enabled.

Related docs:

- [UMA Materials](UMAMaterial.md) for shader properties, channel layouts, packing, and output settings.
- [OverlayDataAsset](OverlayDataAsset.md) for ordinary UMA overlay authoring and recipe use.
- [SlotDataAsset](SlotDataAsset.md) for slots, source meshes, UVs, and UDIM metadata.
- [Wardrobe Recipe Editor](WardrobeRecipeEditor.md) for adding an exported overlay to wearable content.
- [Textures, UDIMs, and Texture Arrays](Textures-UDIM-Arrays.md) for the wider UMA UDIM workflow.

--------------------------------------------------------------------------------

## Before You Start

### Supported Unity and render pipelines

Overlay Painter requires Unity 6.3 or newer. Its certified material workflows are URP and HDRP. Built-in/Standard materials are not part of the certified workflow, even if a particular material happens to preview successfully.

The selected `UMAMaterial` must resolve to a valid material for the active render pipeline. Its texture channels must identify real shader properties and provide a usable **Overlay Painter Channel Layout**. Overlay Painter validates this before the stage opens.

### Prepare the target content

For the most predictable result, confirm that:

- The `SlotDataAsset` contains valid mesh data and the intended UV layout.
- The `UMAMaterial` uses the shader that will be used in production.
- Every physical texture property is represented by the correct UMA material channel.
- Packed maps have the correct R, G, B, and A meanings in **Overlay Painter Channel Layout**.
- Custom shader channels have explicit **Physical Output / Import** settings when automatic inference is not sufficient.
- Source overlays use that same compatible `UMAMaterial` and channel order.
- Normal textures use a known OpenGL or DirectX convention.
- Sprite sheets are imported as **Sprite (2D and UI)** with **Sprite Mode** set to **Multiple** when individual sprites will be used.

See [UMA Materials](UMAMaterial.md) before painting a custom Shader Graph or hand-written shader. A wrong channel layout can make a visually plausible preview export incorrect packed data.

### Choose a working resolution deliberately

Working resolution affects brush detail, effect widths, document size, save time, and GPU memory. Use the lowest resolution that preserves the intended final detail.

Practical starting points:

- `1024`: small accessories, masks, distant characters, and quick iteration.
- `2048`: a useful general-purpose starting point for hero clothing and body details.
- `4096`: close-up assets that have enough source detail and UV area to justify it.

Doubling both dimensions creates four times as many pixels. A multi-channel, multi-layer 4K document can therefore consume much more memory than a 2K document.

--------------------------------------------------------------------------------

## Understand the Four Key Terms

Overlay Painter uses four related concepts. Keeping them separate prevents most workflow mistakes.

- **Target**: the slot, group of slots, or logical UDIM group that receives a stroke.
- **Channel**: the material meaning being edited, such as Albedo, Normal, Metallic, Roughness, Ambient Occlusion, Emission, Skin Color Mask, Thickness, or Detail Mask.
- **Source**: the texture, sprite, UMA overlay, or solid color stored on one authored layer channel and supplied to a paint, fill, or path operation.
- **Destination**: the editable base or non-destructive layer that receives the result.

For example, a brush can use a tattoo `Sprite` as its **Source**, paint the **Albedo** channel on the torso **Target**, and write into a new Paint layer as its **Destination**.

A channel's source does not determine the destination, and selecting the Paint / Preview Channel does not automatically create that channel on every layer.

--------------------------------------------------------------------------------

## Open Overlay Painter

### From a SlotDataAsset

This is the recommended path when creating a new reusable overlay.

1. Select one `SlotDataAsset` in the Project window.
2. Near the top of its Inspector, find **Open in Overlay Painter** directly below the **Validate**, **View MeshData**, and **Clear Errors** row. It is not inside a utility foldout.
3. Click **Open in Overlay Painter**. The button is enabled only when exactly one slot with valid mesh data is selected.
4. Choose either an `UMAMaterial` or an `OverlayDataAsset` as the starting source.
5. Review the material capability summary and working resolution.
6. If this is a UDIM slot, review the member and source table.
7. Click **Open**.

The slot is reconstructed directly from its `UMAMeshData`. No scene avatar or skeleton is required.

#### Starting with an UMAMaterial

Choose an `UMAMaterial` when creating a surface from a neutral starting point.

- Overlay Painter creates semantic-neutral base channels.
- It adds a removable **Default White** Fill layer to the first suitable physical material channel.
- The Fill layer is ordinary editable document content. It can be recolored, hidden, or deleted.
- If the first physical channel is not albedo/color, the first editable logical component is used and a warning is reported.

This path is useful for a new garment, a blank decal target, or a material whose complete look will be built in the paint document.

#### Starting with an OverlayDataAsset

Choose an `OverlayDataAsset` when an existing overlay is the visual base.

- Its `UMAMaterial` becomes authoritative.
- Its textures become immutable base sources.
- Painting and layers are created above editable copies; the original asset and textures are not modified.
- Export creates new assets by default rather than replacing the source overlay.

This path is useful for adding wear, graphics, tint variation, normal detail, or packed-map changes to an existing overlay.

### From a generated DynamicCharacterAvatar

Use this path when the important context is the assembled character and its current slots or overlays.

1. Place a `DynamicCharacterAvatar` in an open scene.
2. Generate the avatar successfully.
3. Select it and expand **Utilities** in its Inspector.
4. Find **Overlay Painter**.
5. Click **Open Overlay Painter**.
6. Select one or more slots in the Target region.
7. Choose a layer, configure its per-channel sources, then choose the Paint / Preview Channel, tool, and brush.

Overlay Painter cannot open a DCA from Prefab Mode. Exit Prefab Mode and use a generated avatar in an ordinary open scene.

### Open a saved paint document

A saved `TexturePaintDocument` can be reopened by double-clicking it in the Project window. An active Overlay Painter stage can also use **File > Load Document**.

Documents are tied to stable source identities and fingerprints. If source geometry, UVs, material bindings, resolution, or standalone orientation settings changed, Overlay Painter may offer a controlled rebind or report content that can no longer be applied safely.

### Window menu

**Window > UMA > Overlay Painter** opens or focuses the dockable controls window. It does not create a paint target by itself. Start a new standalone session from a `SlotDataAsset`, or start an assembled-character session from a generated `DynamicCharacterAvatar`.

### UDIM targets

Selecting any valid member of a UDIM group opens the complete exact-ID group as one logical target.

- Members are resolved by exact `udimGroupId` and ordered by tile number.
- The group frames, selects, paints, saves, and exports as one artist-facing target.
- Individual member rows are diagnostic. They are not independent paint targets during ordinary group painting.
- Each physical tile still owns separate textures and eventually exports its own `OverlayDataAsset`.

When starting from overlays, assign a compatible source overlay for each member in the setup table. Overlay Painter does not guess companion overlays. A member without an assigned source receives semantic-neutral bases.

When opening from a generated character, each preview material is cloned from the live generated
material so its shader parameters, colors, keywords, and render state are retained. For a UDIM
target, the first member by tile order is the canonical material-parameter source and those
parameters are applied to every member. Each member still keeps its own reconstructed,
native-resolution texture inputs.

--------------------------------------------------------------------------------

## The Workspace

Overlay Painter opens a standard dockable **Overlay Painter** controls window around its custom stage. Dock it beside the Scene view for simultaneous 3D and 2D work.

### Global and 2D toolbars

The global toolbar contains the most frequent document and preview actions, while the 2D canvas has its own compact view toolbar:

- File commands for New Document, Load Document, Save, Save As, Revert, Export, Clear All, and Close.
- Undo and Redo.
- Save or Save As.
- Export.
- Open or focus the 2D UV window and show or hide the Asset Shelf.
- Solo the active channel.
- Compare against the source-before state.
- Isolate selected slots.
- Show the UV wireframe in the 2D canvas.
- Arm the 2D color sampler.
- Open layout controls.

**Solo** is useful for inspecting raw channel values. **Before** is useful for comparing the complete shaded source with the edited result. They serve different preview purposes and are not a replacement for checking the exported material.

### Tool rail

The tool rail selects Paint, Erase, Blur, Smear, Clone, Dodge, Burn, Normal Touchup, Plugin Brush, Polygon Fill, UV Island Fill, or Path editing. Polygon Fill and UV Island Fill use sprite-sheet icons 11 and 12 and are available for ordinary Paint layers and Layer Mask mode.

Tool selection does not choose a layer. Always confirm the active layer and channel after changing tools.

### Target region

The Target region contains:

- Searchable slot selection.
- Multi-slot selection.
- Logical UDIM grouping.
- Texture Set thumbnails.

Select every slot that should be allowed to receive the brush footprint. Geometry belonging to unselected slots is excluded even when it is under the brush.

### UV canvas

The UV canvas shows the active logical channel and uses the same target, layer, per-channel sources, brush, masks, and undo history as the 3D view.

Use it to:

- Inspect exact UV placement.
- Paint in 2D while retaining surface-aware dispatch.
- View the UV wireframe.
- Pan and zoom.
- Set a clone source.
- Create and edit path points.
- Check details around UV seams and island edges.

The 2D view is synchronized, but a path authored in 2D intentionally uses the UV domain. A path authored on the model uses the 3D surface domain. This distinction matters around seams and repeated UVs.

### Layer / Path region

This region contains the ordered Paint, Fill, Path, Group, and plugin-created layers. It provides thumbnails, visibility, drag reordering, grouping, renaming, duplication, merging, effects, and deletion.

The **Layers / Paths** toggle is a list filter:

- **Layers** shows the complete compositing stack, including groups and their children.
- **Paths** shows only Path layers and omits the **+ Group** button.
- Switching tabs does not enable, disable, reorder, or change composition. Non-Path layers continue to contribute in 2D and 3D while the Paths filter is selected.

### Properties region

Properties are contextual. The available sections can include:

- Destination.
- Channels, including the **Paint / Preview Channel**, Solo, and Before controls.
- Active Layer, including Fill or Path settings when applicable.
- Layer Channels. Every authored channel has its own source, Enabled state, paint lock, paint strength, opacity, and blend mode.
- Brush.
- Path.
- Stroke and Projection.
- Plugins.
- Document.
- Performance and Memory.

### Asset Shelf

The Asset Shelf finds `BrushPreset` assets and supports thumbnails, search, folders, comma-separated tags, favorites, recents, custom ordering, rename, duplicate, and Project-window drag and drop.

Use **Layout > Reset Workspace** if panels have been hidden, moved to an unusable size, or need to return to their defaults.

--------------------------------------------------------------------------------

## Your First Paint Layer

1. Select the intended slot or logical target.
2. Select **Albedo** as the active channel.
3. Click **+ Paint** in the layer stack.
4. Rename the layer for its purpose, such as `Chest Logo`.
5. Under **Active Layer > Layer Channels > Albedo**, choose **Color**, **Texture**, or **Overlay** under **Source > Type**.
6. Select the Paint tool.
7. Choose a brush from the Asset Shelf or adjust the session brush.
8. Paint in the Scene view or UV canvas.
9. Toggle the layer off and on to confirm that the change is isolated.
10. Save the document before beginning a large second operation.

For production work, create separate layers for details that may need different opacity, blending, masks, effects, or revision. Avoid placing an entire asset's work on one Paint layer merely because the brush can do so.

New Paint, Fill, and Path layers start visible and are configured for Albedo by default when Albedo is supported by the material. The layer row appends its authored channels after the name, for example `Chest Logo: Albedo, Normal, Roughness`.

--------------------------------------------------------------------------------

## Select and Inspect Channels

Overlay Painter exposes logical meanings rather than forcing artists to edit packed R, G, B, and A components directly.

### Albedo

Base color. Albedo is color data and is exported according to the material descriptor's color-space and alpha contract.

Use it for diffuse color, printed designs, makeup, tattoos, dirt color, and fabric color variation.

### Normal

Tangent-space normal detail. Normal painting is vector-aware rather than ordinary RGB blending.

Use it for pores, stitching relief, embossed designs, wrinkles, scratches, and small surface deformation that should not change the mesh silhouette.

### Normal Control

Normal Control is Overlay Painter's grayscale height modifier for the Normal channel. It is created
automatically whenever a target supports Normal, but it is painter-owned auxiliary data: it is not a
shader property, an `UMAMaterial.MaterialChannel`, or a separate exported runtime texture.

- `0.5` gray is neutral and leaves the composed normal unchanged.
- Values below `0.5` recess the surface; values above `0.5` raise it.
- Height gradients bend the normal. A constant dark or light area has no slope in its interior, so
  only its transitions produce visible normal detail.
- **Height Strength** scales the generated slope, **Sample Radius** controls the neighboring texel
  distance used for the gradient, and **Invert Height** reverses raised and recessed interpretation.

Normal Control is a full layer channel. It can be authored with Paint, Fill, Path, Polygon Fill, UV
Island Fill, groups, layer masks, and layer effects. Color and texture input is constrained to
grayscale. It accepts a scalar value, `Texture2D`, or `Sprite` source. It does not offer an
`OverlayDataAsset` source because Normal Control deliberately has no material/overlay texture slot.
Select **Normal Control** to inspect or paint the height field; select **Normal** to inspect
the effective normal after Normal Control has been combined with the ordinary normal stack. The 3D
material preview always receives that effective normal.

The document saves the Normal Control base, every layer-channel texture and source, and the
strength/radius/invert settings. **Flattened Composite** export bakes the result into the physical
normal output. **Runtime Overlay (Transparent)** converts authored Normal Control content into a
flat-relative normal delta and adds its affected pixels to overlay coverage, so the exported UMA
overlay changes the runtime normal without carrying an internal Normal Control texture. Overlay
Painter calculates in its canonical OpenGL convention and performs any requested DirectX green
conversion only at the physical export boundary.

### Metallic

Controls which areas behave as metal in a metallic workflow. It may occupy one component of a packed mask map.

Use hard or carefully feathered values according to the material. Unintended gray metallic values can create physically ambiguous surfaces.

### Roughness

Controls micro-surface scattering in Overlay Painter's logical roughness convention. Smoothness-based shaders are unpacked to Roughness for editing and inverted again during repacking and export.

- Darker roughness values are smoother.
- Lighter roughness values are rougher.

Always judge roughness under representative lighting and reflections.

### Ambient Occlusion

Controls localized occlusion data. It is usually linear data and may be packed into a mask texture.

Use it to reinforce small creases or cavities, not to paint arbitrary dark shading into albedo.

### Emission

Controls emitted color. The final brightness also depends on the shader and its emission intensity settings.

### Custom

Represents a project-specific channel made available by the material capability descriptor. Its meaning and import rules must be defined by the custom material workflow.

### Skin Color Mask

An RGBA skin-variation channel. RGB stores the color toward which the base skin is shifted, while
alpha controls the amount and direction of the variation used by the skin shader. It is treated as
color data, remains fully editable in layers, Fill, Paint, Path, Sprite Sets, save/recovery, preview,
and export, and is written back to the physical texture property declared by the `UMAMaterial`.

### Thickness

A scalar material-data channel used by shaders for subsurface scattering or thickness response.
For the UMA3 skin shader, red in `SSS.AO.Detail.Gloss Map` is exposed as Thickness.

### Detail Mask

A scalar material-data channel controlling where shader detail is applied. For the UMA3 skin
shader, blue in `SSS.AO.Detail.Gloss Map` is exposed as Detail Mask.

The UMA3 `SSS.AO.Detail.Gloss Map` contract is R=Thickness/SSS, G=Ambient Occlusion,
B=Detail Mask, and A=Smoothness. Overlay Painter presents A as Roughness and performs the
Smoothness inversion during unpacking and repacking.

### Channel availability

The active material decides which logical channels exist. If **The active target has no matching logical channel** appears, changing the channel selector cannot create unsupported material data. Correct the `UMAMaterial` channel layout or select a compatible target.

--------------------------------------------------------------------------------

## Choose a Destination

The destination determines where the result is stored.

### Active Layer

Use **Active Layer** for ordinary production work.

- Paint remains isolated from the base.
- Visibility, opacity, blend mode, masks, and effects remain editable.
- Deleting the layer removes its contribution.
- Layer changes can be saved and reopened in the document.

Paint layers always own their strokes. The Properties region identifies the layer that will receive them.

### Editable Base

Some operations can target the editable base copy.

- This changes the document's base pixels directly.
- It still does not modify the source texture asset.
- It is less flexible than a layer because there is no independent layer to hide, reorder, or restyle.

Use base edits for intentional corrections that truly belong to the new baseline. Use layers for art-direction decisions and details that may change.

--------------------------------------------------------------------------------

## Choose a Source

Sources are stored per authored layer channel. In the docked workspace, open **Active Layer > Layer Channels**, find the required channel card, then choose **Texture**, **Overlay**, or **Color** under **Source > Type**. The source controls are directly editable on every channel card. **Edit** only makes that card the active Paint / Preview Channel.

Sources are sampled into brush stamps or generated layer content. They are not modified in place. A multi-channel layer can use a different source type and asset for every channel.

### Texture source

Texture source mode accepts either a complete `Texture2D` or one `Sprite` from a sprite sheet.

#### Complete Texture2D

Assign **Texture** when the entire image is one brush, fill, or path source.

Typical uses:

- A full fabric weave.
- A grunge or wear texture.
- A normal detail stamp.
- A complete ribbon tile.

#### Individual Sprite

Assign **Sprite** when one region of a larger sprite sheet is the desired source.

- Overlay Painter extracts the sprite rectangle into a cached temporary texture.
- The `Sprite` remains the persisted source reference.
- Neither the sprite nor its sheet is modified.
- The cache is separated by channel, normal convention, and inversion state.

This is useful for libraries of logos, tattoos, seams, fasteners, scars, and coordinated material tiles.

If a texture and sprite compete for the same source, selecting one clears the other. Treat them as mutually exclusive choices.

#### Normal sources

On a Normal channel card, set **Convention** to describe that channel's source image:

- **OpenGL**: positive Y is stored in the green channel.
- **DirectX**: the green direction is opposite OpenGL.

Overlay Painter converts DirectX input to its canonical OpenGL working representation before vector blending. It also converts raw RGB normal images, sprite regions, and textures imported with Unity's Normal Map importer into linear normalized tangent-space data.

The convention describes the source. Export convention is controlled separately by the `UMAMaterial` output contract.

### Overlay source

Overlay source mode samples an `OverlayDataAsset` through one authored channel card. The card's logical channel determines which material data is resolved from the overlay.

1. Select **Overlay** in Source.
2. Assign the source `OverlayDataAsset` in **Overlay**.
3. Repeat on other authored channel cards when the layer should sample several logical channels from the same or different overlays.
4. Paint, fill, or apply the path.

Overlay textures are routed to Albedo, Normal, Metallic, Roughness, Ambient Occlusion, Emission,
Skin Color Mask, Thickness, Detail Mask, or Custom according to the overlay's `UMAMaterial`
channel layout. This is not a simple texture-list guess.

Important behavior:

- On a multi-member target, Overlay Painter resolves the selected source against every member and attempts to bind the corresponding overlay source for each one.
- If no matching source exists for one member, the operation reports that member instead of silently painting the wrong overlay.
- The selected overlay's own UMA recipe blend settings are not the same as Overlay Painter layer blend modes. The painter samples the source data, then the document layer controls how the result is composited.

Overlay source mode is especially useful when the same brush or path should carry coordinated albedo, normal, and mask-map information.

A Paint or Path operation dispatches to every authored layer channel that has a valid source. Each channel uses its own source, lock, and Channel Paint Strength. The **Paint / Preview Channel** chooses the channel shown in the UV canvas and used by channel-specific tools; it is not a single-channel switch for an otherwise multi-channel Paint or Path stroke. Use **Lock Painting** or set Channel Paint Strength to zero when an authored channel must not receive new marks.

### Color source

Color source mode supplies a solid RGBA color.

Use it for:

- Flat paint and masks.
- Tint blocks.
- Metallic, roughness, or AO values when a scalar appearance is needed.
- Emission color.

On data channels, use the value field deliberately. A visually pleasant picker color does not automatically represent a physically useful metallic, roughness, or AO value.

### Invert

When available, **Invert** applies one-minus to source RGB while preserving alpha coverage. It is useful for paired black/white masks or inverse roughness-style source art.

Do not use source inversion as a substitute for defining Smoothness correctly in the material layout. Overlay Painter already converts Smoothness to logical Roughness when the descriptor says it should.

--------------------------------------------------------------------------------

## Use Sprite Sets

An `OverlayPainterSpriteSet` groups matching sprite sheets by logical material channel. One selected sprite index can then assign coordinated Albedo, Normal, Roughness, Metallic, AO, or Emission sources to one layer.

This is one of the fastest ways to build an artist library of complete material motifs.

### Example sprite set

A `Leather Stitches` set might contain:

- An Albedo sprite sheet with thread color and alpha.
- A Normal sprite sheet with raised thread normals.
- A Roughness sprite sheet with thread gloss variation.
- An Ambient Occlusion sprite sheet with a small contact shadow.

Sprite index 0 on every sheet must describe the same stitch design, as must index 1, index 2, and so on.

### Create a Sprite Set

1. In the Project window, choose **Assets > Create > UMA > Overlay Painter > Sprite Set**.
2. Give the asset a descriptive name.
3. Set **Set Name** to the artist-facing library name.
4. Add one Sprite Sheet entry per material channel.
5. For each entry, choose the logical **Channel** and assign its sprite-sheet texture.
6. Enable **Inverted** only when that sheet's RGB values intentionally need one-minus conversion.
7. Optionally fill **Sprite Names** with artist-friendly labels in matching index order.

Do not add two sheets for the same channel. Duplicate channel entries are skipped during assignment.

### Prepare matching sheets

Every configured sheet must:

- Be sliced into individual `Sprite` sub-assets.
- Contain the desired sprite index.
- Use the same conceptual order as the other sheets.
- Align corresponding art within each sprite rectangle.
- Use suitable color-space and source conventions for its channel.

The picker only exposes the common count across all configured sheets. If Albedo contains 12 sprites and Normal contains 10, only the first 10 coordinated entries are available.

#### Slice and tune a sprite sheet

Select one or more source textures in the Project window and choose **Assets > UMA > Set Sprite Grid Options**. Set the common column, row, and initial inset values, then choose an adjustment scope:

The setup window uses a fixed-width, scrolling control column on the left and a resizable live-preview column on the right. Resize the window to give the preview more room; it automatically uses the largest area that fits the sprite aspect ratio.

- **All Sprites** applies the same inset, horizontal/vertical offset, and tile-fix settings to every sprite.
- **Individual Sprites** retains a separate profile for every sprite while you move back and forth through the sheet. Each profile has independently editable **X1 (Left)**, **Y1 (Bottom)**, **X2 (Right)**, and **Y2 (Top)** insets, horizontal and vertical offsets, and tile-fix settings.

The live preview and final Unity sprite rectangles use the same profile data. An invalid adjustment that would remove the whole sprite or move it outside the source texture is reported without changing the source file. Optional tile fixes rewrite source pixels only inside each adjusted sprite rectangle; slicing alone changes importer metadata only. When **Make seamlessly tileable** is enabled, **Seam Blend Area (%)** controls how far the correction reaches inward from every edge. Reduce it to preserve more of the sprite center; the value is retained independently for each sprite profile.

To reuse a completed setup on another coordinated sheet, assign the completed texture under **Copy Existing Setup** and click **Copy from this sprite sheet**. Sheets configured by this utility retain versioned setup metadata, so the copy restores the grid, common and per-sprite insets, horizontal and vertical offsets, adjustment scope, sprite areas, and every tile-fix setting. Existing sheets created before this metadata was available can still copy their Unity sprite rectangles; the window reports that tile-fix settings were unavailable. Matching source and destination dimensions are recommended, and the window warns when they differ.

### Sprite ordering

Overlay Painter orders sprites predictably:

1. A trailing numeric suffix such as `_0`, `_1`, `_2`, or `_10` is sorted numerically.
2. Sprites without such suffixes are sorted by sheet position, top-to-bottom and left-to-right.
3. Names provide the final tie-break.

For team libraries, use consistent numeric suffixes on every sheet. This remains reliable if Unity's visual slicing layout changes.

### Assign from a Sprite Set

1. Select a Paint, Fill, or Path layer.
2. Click **Add from Sprite Set** in the layer/channel properties.
3. Select the set in the left column.
4. Select one sprite in the right column.
5. For a Fill layer, set the initial X and Y tiling.
6. Click **Add**.

Overlay Painter adds or updates one layer channel for each valid sheet, preserving the selected `Sprite` reference and channel-specific source settings. Fill layers regenerate immediately. Existing paths are queued for reapplication when their sources change.

On Paint and Path layers, the next operation applies the coordinated sources together to every unlocked channel with nonzero Channel Paint Strength. On Fill layers, each assigned channel uses the initial X and Y tiling from the picker; both values default to `1`.

The operation reports sheets that could not be assigned, including:

- A missing sheet texture.
- A duplicate logical channel.
- A sprite index missing from one sheet.
- A channel not supported by the target material.

### Sprite Set best practices

- Keep every sheet's sprite rectangles identical in size and alignment.
- Use the same suffix numbering on all sheets.
- Store normal sheets in a documented convention.
- A Sprite Set sheet does not store its own normal convention. Set the active Normal source convention before assignment, or edit the resulting Normal layer channel afterward.
- Preview the set on a neutral material before using it across many assets.
- Separate fundamentally different shader families into different Sprite Sets.
- Keep source alpha clean; it controls stamp or layer coverage.

--------------------------------------------------------------------------------

## Work with Layers

Layers are independent transparent surfaces composited from bottom to top. A higher row contributes after the rows below it.

### Paint layers

Use Paint layers for freehand strokes and tool operations.

- Channels are created when needed.
- Each channel can have its own source and controls.
- Erasing removes content from the active layer rather than changing lower layers.
- A layer can carry several coordinated channels.

Good practice: separate artwork by purpose, not by every individual stroke. `Logo`, `Wear`, `Edge Dirt`, and `Normal Stitching` are more maintainable than `Layer 1` through `Layer 25`.

### Fill layers

Use Fill layers for generated coverage over the target surface.

Fill sources can be:

- Solid Color.
- A complete Texture2D.
- An individual Sprite.
- An Overlay source.
- Multiple coordinated channels assigned from a Sprite Set.

Every Fill channel has independent X/Y tiling, X/Y offset, and rotation. Enable **Use Transform For All Channels** on the first authored channel to make it the transform master; the other channels update to match and their transform controls remain locked until sharing is disabled.

#### Flat projection

**Flat** uses the mesh UVs. X and Y tiling repeat in destination UV space.

Use it when:

- The source was authored for the target UVs.
- Direction and scale must follow the UV layout.
- A repeating weave or graphic should align in texture space.

Watch for visible scale or direction changes between UV islands.

#### Triplanar projection

**Triplanar** projects from world-space axes and blends according to the surface normal.

Use it when:

- A repeating material should cross UV seams more naturally.
- UV scale varies too much for a flat repeat.
- Stone, dirt, cloth grain, or procedural wear should feel object-space based.

Triplanar controls include projection blend behavior, blend offset, and sharpness. A hard blend gives crisp axis changes; crossfade reduces axis seams but can soften high-frequency texture detail.

Fill generation adds a small gutter around covered UV islands for stable compositing. Export padding is a separate final-output operation.

### Path layers

Path layers store editable surface-anchored curves and render them procedurally.

Use them for:

- Seams and piping.
- Stitches and laces.
- Straps, stripes, and trim.
- Repeated decals along a route.
- Controlled scars, cracks, or painted lines.

Path layers are described in detail under [Surface Paths](#surface-paths).

### Group layers

Groups organize Paint, Fill, Path, and nested Group layers.

- Drag a layer by its handle and drop it directly on a group's folder icon.
- Click the folder icon to collapse or expand the children.
- Selecting a group before creating a layer creates the new layer inside that group.
- Dragging one group onto another nests the complete source subtree. A group cannot be dropped into itself or one of its descendants.
- Use **Remove from Group** from the child row menu to return it to the root.
- Group children remain a contiguous block directly below the group row. A root layer cannot be inserted between the group and its children.
- **Remove from Group** moves the former child above the group so it cannot split the group block.
- Group visibility hides all children.
- Group opacity and blend mode apply once to the isolated child composite, not separately to each child.
- Selecting a group shows the composite of its children in the 2D UV canvas. The 3D view continues to show the complete visible layer stack regardless of which layer or group is selected.
- Groups do not contain material paint channels. A group can own an editable layer mask, and that mask gates the combined result of all children as one unit.
- Groups do not use ordinary material-channel layer effects. Their masks can use Layer Mask Noise and Layer Mask Texture Overlay.
- Deleting a group deletes all of its descendants. The confirmation names the group, reports the child count, and explains that Undo can restore them.
- Duplicating a group deep-copies its complete subtree, including masks and channel pixels, with independent hierarchy and procedural ownership identities.

### Manage the stack

Layer rows provide:

- Visibility.
- Thumbnail.
- A second grayscale mask thumbnail when the layer or group has a mask. Click it to enter Layer Mask mode; click the main thumbnail or row to return to material-channel editing.
- Name and type.
- Opacity and blend mode when space permits.
- Drag reorder.
- An **fx** effects button.
- A row menu.
- Delete.

Common operations include:

- **Rename** or `F2`.
- **Duplicate** or `Ctrl/Cmd+D`.
- **Merge Down**.
- **Remove from Group**.
- **Delete** or `Delete`.

Merge Down is a flattening operation. It bakes the visible results of two adjacent sibling layers, including their masks and effects, into merged pixels; the merged layer does not keep editable mask or effect state. It is available only when both layers and all authored channel overrides use **Normal** blend. Non-Normal blends depend on the backdrop and therefore cannot be flattened exactly into a reusable transparent layer. Duplicate the document or layers first when independent editability may still be needed.

Layer structure changes participate in Undo and Redo.

--------------------------------------------------------------------------------

## Layer and Channel Controls

### Layer visibility

Hidden layers do not contribute to the composite. Use visibility for comparison and variant testing instead of repeatedly deleting and recreating work.

### Layer opacity

Layer opacity scales the complete layer contribution across its authored channels. A parent group's opacity also multiplies its children.

### Layer blend mode

The layer blend mode controls how layer RGB combines with the existing destination. Alpha, layer opacity, channel opacity, and masks still control coverage.

Changing **Blend** on the layer changes the layer-level fallback only. It never overwrites authored **Channel Blend** values. This keeps independent channel tuning intact when a layer is renamed or its opacity is adjusted.

### Per-channel controls

Each authored channel can expose:

- **Enabled**: includes or excludes the channel from composition.
- **Lock Painting**: prevents brush operations from writing to that channel.
- **Channel Paint Strength**: scales how strongly new brush input is deposited on Paint and Path layers. Fill layers do not use or show this control.
- **Channel Opacity**: scales this channel during composition.
- **Channel Blend**: chooses the blend behavior for this channel.
- Source and source-specific settings.

Use **New Channel** and **Add Channel** to add another channel supported by the active material. Channels already authored by the layer are omitted from the dropdown. The **Edit / Active** button selects the Paint / Preview Channel; all channel cards remain visible and independently editable.

Use **Remove** on a channel card to delete that channel's texture and settings. Overlay Painter asks for confirmation and keeps the operation undoable. Effects targeting the removed channel are retargeted to the layer's first remaining channel, or disabled when no channel remains, so the effect stack never contains an enabled invisible target.

Use channel controls for a coordinated material layer whose Albedo should remain strong while its Normal or Roughness contribution is reduced.

**Lock Painting** is per channel, not a complete layer lock. Check every authored channel before assuming a layer is protected.

--------------------------------------------------------------------------------

## Layer Blend Modes

Overlay Painter supports six blend modes. These are document-layer blend modes and are separate from `OverlayDataAsset.overlayBlend` used by UMA's ordinary texture merger.

| Blend mode | What it does | Artist use |
|---|---|---|
| **Normal** | Uses source RGB directly within the layer's alpha coverage. | Most paint, decals, normal content, and masks. |
| **Multiply** | Multiplies the existing value by the source. White has little effect; darker values darken. | Dirt, stains, cavity color, fabric print darkening. |
| **Add** | Adds source values to the existing value. Results can saturate. | Emission, highlights, bright data accents. |
| **Subtract** | Subtracts the source from the existing value. Results can clamp at zero. | Controlled darkening or reducing scalar data. |
| **Screen** | Brightens by multiplying the inverse values. Black has little effect. | Soft lightening, faded paint, bright surface variation. |
| **Overlay** | Multiplies darker destination regions and screens lighter destination regions. | Contrast-rich color texture and stylized variation. |

### Blend-mode guidance for data channels

Metallic, Roughness, AO, Thickness, Detail Mask, and other packed-map components are numeric material data, not ordinary color artwork. A blend mode that looks familiar in an image editor may create physically undesirable intermediate values.

Recommended approach:

- Start with **Normal** for data channels.
- Reduce **Channel Opacity** when a softer contribution is needed.
- Use Add or Subtract only when the numeric direction is intentional.
- Inspect packed channels with **Solo** and validate the shaded material under useful lighting.
- Use vector-aware normal behavior rather than treating a normal map as ordinary RGB color.

### Opacity and Channel Paint Strength are different

- **Channel Paint Strength** changes how strongly new brush marks are written into that channel.
- **Channel Opacity** changes how the already-authored channel is composited.
- **Layer Opacity** changes the complete layer contribution.
- **Group Opacity** scales all child layers.

When tuning an existing result, prefer opacity. When preventing future strokes from depositing too strongly, tune Channel Paint Strength, Flow, or Strength.

--------------------------------------------------------------------------------

## Layer Effects

Click the row's **fx** button to open non-destructive effects. A blue **fx** indicator means at least one effect is enabled.

Effects are calculated during composition. They do not permanently paint their result into the source layer, so their settings remain editable.

The popup is an ordered effect stack. Use the arrow buttons to reorder entries, **Add** to create another instance of any supported effect, and **×** to remove an entry. Multiple instances are supported. An effect can target only a channel actually authored by that layer; add the channel first if it is not listed. Paint and effects are evaluated as one isolated layer result, then the layer and channel opacity are applied once. This prevents a mask or partial opacity from being multiplied again for every effect pass.

Layer effects require compute shaders and support for RGFloat and RFloat render textures. The effects popup reports a warning when the current graphics environment cannot evaluate them.

### Common effect controls

Conventional effects provide controls appropriate to their type, including:

- **Enabled**.
- Target material **Channel**.
- **Level** for the effect's overall contribution.
- Color and blend controls where applicable.
- Width and shadow offsets in destination pixels.
- An editable falloff curve for shadows and glows.

Effect widths are measured in destination pixels. The same numeric width looks physically smaller on a higher-resolution texture and larger on a lower-resolution texture.

When a layer or group has a mask, the same **fx** popup also contains two mask-only effects. They never modify material channels directly:

- **Layer Mask Noise** generates deterministic grayscale noise with Seed, X/Y Tiling, X/Y Offset, Detail, Balance, Contrast, Invert, Combine, and Opacity controls.
- **Layer Mask Texture Overlay** combines a texture's Luminance, Red, Green, Blue, or Alpha component with the mask. It provides independent X/Y Tiling, X/Y Offset, Rotation, Invert, Combine, and Opacity controls.

Mask effects are evaluated non-destructively in a fixed order: editable base mask, Layer Mask Noise, then Layer Mask Texture Overlay. The mask thumbnail and both previews show this effective result.

### Stroke

Stroke creates an outline around the layer's existing coverage.

Use it for:

- Borders around decals or printed graphics.
- Piping-like accents.
- A controlled halo in a data channel.
- Increasing separation from the underlying material.

Adjust the target channel, color, width, offset, smoothness or falloff, and Level. **Offset** moves the complete stroke band across the authored edge: `0` places it immediately outside, negative values pull it inward, and positive values push it farther outward. Keep the width appropriate for final texture resolution.

### Inner Shadow

Inner Shadow shades inward from the layer edge.

Use it for recessed marks, inset patches, stamped shapes, and additional contact depth. On a ribbon, it can target the Left, Right, or Both long edges.

### Outer Shadow

Outer Shadow shades outside the layer's covered edge.

Use it for raised patches, decals, labels, and contact shadows. Keep albedo shadows subtle when the material lighting should provide most of the depth.

On a ribbon, the effect can target Left, Right, or Both long edges and does not wrap around the start or end caps.

### Inner Glow

Inner Glow colors inward from the edge.

Use it for luminous borders, worn edge color, soft inset highlights, or controlled channel transitions.

### Outer Glow

Outer Glow colors outward from the layer edge.

Use it for emission halos, soft painted bleed, or stylized separation. An albedo glow is not a substitute for Emission when the surface should actually emit light.

Ribbon glows can target Left, Right, or Both long edges without wrapping around the caps.

### Color Overlay

Color Overlay applies a color treatment clipped to the layer's existing coverage.

Use it to:

- Recolor a monochrome stamp.
- Test colorways without repainting.
- Apply one channel-specific material value across existing art.

Choose the target channel deliberately. A white color overlay on Roughness does not mean the same thing as white on Albedo.

### Texture Overlay

Texture Overlay clips up to two repeating textures to the layer's existing coverage.

Each texture has independent:

- Source texture.
- X and Y destination-UV tiling.
- X and Y destination-UV offset.
- Rotation around the texture center.
- Blend mode.
- Opacity.
- RGBA color multiplier.

Texture 1 is combined first, then Texture 2. The texture orientation follows destination UVs, not a ribbon's direction of travel.

Use it for:

- Adding weave only inside a garment panel layer.
- Adding scratches inside a painted metal area.
- Combining a broad material texture with a finer detail texture.

### Effects on normals and data channels

An effect can target a material channel, but not every visual effect makes physical sense on every channel. Review Normal, Roughness, Metallic, and AO results in channel solo and in the shaded preview.

Distance-field effects are cached while editing and rebuilt from completed layer coverage after a stroke. Large effect widths on high-resolution layers can cost more to update than an ordinary paint layer.

For ordinary non-ribbon layers, the authored pixels are composited first and enabled effects then run in the visible stack order. Stroke and outer effects use the original authored-and-masked boundary rather than the growing shadow/glow result; Stroke begins outside that boundary at zero offset but can be moved inward or outward, and inner/color/texture effects remain clipped without inflating alpha. Ribbon-local entries run as ordered projection passes from the ribbon's own long-edge coordinates. Color Overlay and Texture Overlay remain channel-composite effects.

--------------------------------------------------------------------------------

## Ribbon-Specific Effects

The following effects use a ribbon path's cross-section and distance along the path. They are evaluated only when the Path layer's mode is **Ribbon**.

Left and Right are defined relative to travel from the first path point to the last. Reversing the path swaps the practical side orientation.

### Edge Fade

Edge Fade reduces ribbon opacity toward its long edges.

- **Fade Begins** is measured from the centerline across the normalized half-width.
- **Fade Size** controls how much of the remaining center-to-edge distance is used to reach transparency.
- A Fade Size of zero cuts out immediately at the Fade Begins position.
- A Fade Size of 100 reaches transparency at the side edge.

The fade follows the world-space ribbon cross-section. Source texture rotation, mesh UV orientation, seams, and UDIM tiles do not rotate the fade.

Use it for paint stripes, soft scars, makeup lines, worn edges, and cloth trim that should feather at the sides.

### Bevel Edge

Bevel Edge assigns light and dark treatments to the ribbon's long edges.

Controls include:

- Left, Right, or Both edge targeting.
- Light and dark colors.
- Edge width and smoothness.
- Independent Light or Dark choice for each side.
- Independent pixel offset for each side.
- Level.

Use it to suggest raised piping, inset grooves, folded trim, or a directional bevel. For a consistent raised result, assign the light and dark sides according to the intended lighting convention and path direction.

### Procedural Stitch

Procedural Stitch generates dashed thread rows along one or both ribbon edges.

Controls include:

- Left, Right, or Both sides.
- Thread color.
- Single or double rows.
- Thread thickness.
- Stitch length.
- Edge inset.
- Level.

The gap uses the same length as the stitch. Stitch placement follows path distance rather than source texture orientation.

Use a coordinated Albedo, Normal, Roughness, and AO Sprite Set on the ribbon when the thread needs more material information than the procedural color alone provides.

### Ribbon endpoint sources

Ribbon paths can replace the first and last complete repeated tiles with separate **Beginning** and **End** textures or sprites. Closed ribbons ignore endpoint sources.

Use endpoint art for strap ends, zipper stops, seam caps, cable connectors, or ornamental line endings.

--------------------------------------------------------------------------------

## Brushes and Stroke Controls

### Brush shapes

- **Circle**: general soft or hard painting.
- **Square**: block shapes and directional hard-edged work.
- **Stamp**: uses either a Texture2D or a Sprite. A Sprite uses only its authored sprite region, so a single item from an atlas can be used directly as the brush shape. Assigning one stamp source clears the other.

### Size

Brush size is evaluated in world space using the contacted triangle's UV-to-world metric. This keeps a stroke's physical size more consistent across seams, rotated islands, and different UV densities.

Use the UV canvas to inspect resulting texel quality, but judge the brush's intended scale on the 3D surface.

### Hardness

Hardness controls the falloff toward the brush edge. It affects both color contribution and stored layer alpha.

- Lower hardness creates a broader soft perimeter.
- Higher hardness keeps more of the stamp near full strength.

### Flow

Flow controls how quickly repeated samples build toward the allowed coverage. It is not the same as final layer opacity.

### Strength

Strength scales the operation's overall effect. Blur, Smear, Dodge, Burn, and Normal Touchup especially benefit from controlled strength rather than repeated full-power passes.

### Spacing

Spacing determines the distance between brush samples relative to brush size. Low spacing gives smoother continuous marks but costs more processing. High spacing reveals individual stamps.

### Rotation and Follow Stroke

Rotation sets the stamp angle. **Follow Stroke** rotates directional stamps along the filtered direction of travel.

Use direction smoothing when a directional stamp turns too abruptly on small pointer movements.

**Random Rotation** gives every sampled paint stamp an independent 0-360 degree rotation. It is unavailable while Follow Stroke is enabled because Follow Stroke owns the stamp orientation.

**Random Size Variation** changes the complete effective world-space size of every sampled stamp. **Shrink (%)** and **Grow (%)** define the range below and above the authored brush size; both default to 30%. The generated variation is retained by mirrored and projected copies of that stamp so seams and symmetry remain coherent.

**Splatter** randomly offsets each sampled paint stamp within a disk around the stroke. **Splatter Distance (%)** sets the disk radius from 1% to 200% of that stamp's effective world-space brush size, after pressure and random size variation. The offset is deterministic for the stamp, remains tangent to the painted surface, and is shared by projected and mirrored copies so seams and symmetry stay coherent.

**Fade** lowers stamp alpha linearly as the current freehand stroke advances. **Taper** reduces the complete effective world-space stamp size over the same distance. **World Length** is measured along the sampled stroke in model world units; its untouched default tracks three times the current brush size. The first stamp is full strength and size, and the envelope reaches zero at the configured length. A new stroke restarts the envelope. When enabled, the existing Pressure Affects Flow and Pressure Affects Size controls multiply Fade and Taper respectively, so tablet pressure remains part of the result.

### Cap Per Stroke

With **Cap Per Stroke** enabled, each target texel accumulates against its color at the beginning of that stroke. Hardness and falloff define the maximum local coverage, while Flow controls how quickly the stroke approaches it.

This produces a soft perimeter that behaves more like a desktop paint application. Repeated samples grazing the same edge do not automatically force it to full opacity, but moving closer to the brush center can still increase coverage.

### Stabilization

Stabilization smooths noisy pointer input. Higher values produce steadier lines but feel less immediate.

### Pressure

- **Pressure to Flow** scales deposited strength with tablet pressure.
- **Pressure to Size** scales brush size with tablet pressure.

Test the tablet driver and Unity pressure input before relying on pressure for a production-critical line.

### Projection controls

- **Projection Depth** controls how far the brush footprint searches through the surface.
- **Normal Angle Limit** restricts paint on surfaces facing too far away from the contacted orientation.
- **Paint Backfaces** allows or blocks back-facing triangles.

Use a lower depth and a stricter normal angle around thin clothing, fingers, lips, straps, or nearby body parts to avoid painting through to an unintended surface.

### Mirror Global X

Global-X mirroring creates a symmetric counterpart around the stage's global X plane.

It is useful for paired details on symmetrically positioned geometry. It is not UV mirroring and does not guarantee useful results on asymmetrical meshes or off-center accessories.

--------------------------------------------------------------------------------

## Painting Tools

### Paint

Deposits the selected Texture, Overlay, or Color source. It respects source alpha, brush falloff, Flow, Strength, masks, projection, layer controls, and blend mode.

### Erase

Removes content from the active layer. It does not erase lower layers or the original source asset.

### Blur

Softens neighboring values on the active destination. Use it sparingly on normals and packed material data; excessive blur can reduce vector quality or create physically vague scalar values.

### Smear

Drags existing values along the stroke direction. A smooth, continuous gesture gives a more predictable motion vector than disconnected clicks.

### Clone

Samples from one surface location and paints relative content elsewhere. Set the clone source first, then paint the destination. Verify the source on the UV canvas when repeated or mirrored UVs make the 3D relationship ambiguous.

### Dodge and Burn

Dodge brightens and Burn darkens. They operate on numeric channel values, so their meaning changes by channel.

- On Albedo, they resemble lightening and darkening.
- On Roughness, a value change affects smoothness rather than brightness.
- On Metallic or AO, they alter material data.

### Normal Touchup

Normal Touchup bends the painted tangent-space normal toward the mesh's interpolated vertex normal and can blend across known seam partners.

Use it to reduce visible normal seams, soften an overly strong normal stamp, or make detail follow the underlying surface more naturally. It cannot repair incorrect mesh tangents, broken UVs, or an incorrectly declared source convention.

### Plugin Brush

Plugin Brush exposes compatible Plugin API v2 brushes. Plugin parameters and declared channels are validated by the host. Committed plugin work remains subject to masks, channel rules, document persistence, and Undo.

--------------------------------------------------------------------------------

## Masks

Each Paint, Fill, Path, or Group layer can own zero or one editable grayscale mask. White reveals the layer, black hides it, and gray produces partial contribution. There are no document-wide artist masks and no separate Masks properties panel.

### Add, select, and remove a mask

1. Open the layer row's `⋮` menu.
2. Choose **Mask > Add Black Mask** or **Mask > Add White Mask**.
3. Click the new grayscale thumbnail beside the layer thumbnail to enter **LAYER MASK MODE**.
4. Paint in either the 2D UV canvas or 3D Scene view.
5. Click the main layer thumbnail or row to leave Mask Mode.
6. Use **Mask > Remove Mask** when the layer should return to unmasked contribution.

A black mask starts with a white Mask Value so the first stroke reveals content. A white mask starts with a black Mask Value so the first stroke hides content. **Erase** restores the mask's original black or white creation value.

The 2D canvas shows the effective grayscale mask while Mask Mode is active. Both the 2D canvas and Scene view display a prominent **LAYER MASK** label. The Scene view remains a shaded composite by default; enable **Solo Mask** to place the grayscale mask directly on the 3D model for inspection.

### Paint a mask

Mask Mode uses the ordinary Paint, Erase, Blur, Smear, Clone, Dodge, Burn, and compatible Plugin Brush tools. The result is always normalized back to grayscale with opaque storage alpha. Normal Touchup is disabled because a mask has no tangent-space normal meaning.

Mask strokes use the same target projection, brush shape, stamp, pressure, stabilization, geometry clipping, Undo, Redo, save, recovery, and logical-target behavior as material-channel strokes.

The active layer exposes only a scalar **Mask Value** from 0 (black) to 1 (white). Mask Mode has no material-channel selector and cannot use a Texture, Sprite, OverlayData source, or layer-channel overlay. Brush shape and stamp alpha still control the stroke footprint, but the deposited value is always grayscale. The Mask Value survives layer duplication, document save/reopen, and crash recovery.

### Fill a polygon or UV island

**Fill Polygon** and **Fill UV Island** are paint operations under **Stroke & Projection**. Arm one, then click in the 2D or 3D view. The command writes the current material paint color to a regular Paint layer, or the current Mask Value while in Mask Mode. Press `Esc` to cancel the armed fill tool.

These commands write pixels and participate in Undo; they do not create persistent structural mask entries. Use them on a mask when a polygon or island should control visibility, or on an ordinary layer when the region itself should receive material paint.

### Mask storage and lifecycle

The editable base mask and its effects are document-owned data. They are captured by normal saves and recovery snapshots and restored without loose texture assets under `Assets/UMA/OverlayPainter/Masks`.

- Duplicate makes an independent GPU copy of the mask.
- Group masks gate the group's combined child composite as one unit.
- Merge Down bakes the visible masked result and removes editable mask state from the merged layer.
- Deleting a layer or group deletes its mask; Undo can restore it.
- Clear All disposes every layer mask with the rest of the authored document state.
- If compatible topology is rebound to a changed UV layout, the mask keeps its black/white base value, grayscale Mask Value, and non-destructive effects, but its stale pixel-space painting is reset to the base value. This prevents old mask texels from hiding unrelated geometry in the new UV layout.

### Geometry clipping

Overlay Painter also applies structural geometry clipping automatically. Each contacted slot and UV island receives a per-texel geometry mask so a rectangular texture update does not leak into unrelated polygons.

This automatic clipping is separate from artist-created masks.

--------------------------------------------------------------------------------

## Surface Paths

Paths are editable curves anchored to the reconstructed surface.

### Create a Path layer

1. Click **+ Path** or **Create Spline Layer**.
2. Select the Path tool.
3. `Shift+Click` the model or UV canvas to append points.
4. Adjust points and controls.
5. Choose the path mode, configure the source on each Layer Channel, then choose the Paint / Preview Channel, brush, and projection settings.
6. Apply the path.

### Insert and edit points

- `Shift+Click`: append a point.
- `Ctrl+Click`, or `Command+Click` on macOS, within 8 screen pixels of the visible curve: insert a point into the nearest segment.
- Click or drag without those modifiers: select, move, or adjust an existing point or control.

Clicking too far from the visible curve does not insert into it.

### 3D and 2D path domains

A path authored in the Scene view follows the 3D surface and resolves UVs when rasterized. This allows continuity across UV seams and UDIM members.

A path authored or moved in the UV canvas uses the 2D UV domain. The 2D preview splits world-space paths at UV discontinuities instead of drawing a misleading line across the texture.

Choose the authoring domain based on intent:

- Use 3D for seams, straps, scars, and lines that should follow the object across UV boundaries.
- Use 2D for artwork whose exact texture-space route is authoritative.

### Path modes

- **Stamps**: places discrete brush stamps along the curve.
- **Continuous**: creates a gap-free brush stroke.
- **Ribbon**: fits complete source-image tiles edge-to-edge along a variable-width strip.
- **Filled**: fills the path-defined shape.

### Path orientation and caps

Stamp and continuous paths can follow the path direction or use a fixed orientation. Applicable modes provide start and end cap choices.

Ribbon layers can use separate Beginning and End tile sources. Closed ribbons ignore endpoint tiles.

### Point dynamics

Path points can store:

- Pressure.
- Width.
- Flow.
- Roll.
- Color.
- Surface offset.
- Tangent mode.

Tangent modes include Corner, Smooth, Broken, and Custom. Paths also support insert, delete, multi-select, copy, paste, reverse, mirroring, and radial symmetry.

### Paths across seams and UDIM tiles

Scene-authored paths use surface anchors and a cached shortest-surface corridor. This helps prevent projection from jumping to another nearby limb or unrelated surface at a slot or UDIM boundary.

Inspect narrow crossings and close parallel surfaces before final export. No automatic surface search can infer artistic intent when two candidate surfaces are effectively coincident.

--------------------------------------------------------------------------------

## Brush Presets and Libraries

### Create a BrushPreset

Use **Assets > Create > UMA > Overlay Painter > Brush Preset**.

A preset stores:

- Circle, Square, or Stamp shape.
- Stamp Texture2D or Sprite source.
- Size.
- Hardness.
- Flow.
- Spacing.
- Rotation.
- Blend mode.
- Mirror Stroke.
- Follow Stroke.
- Random Rotation.
- Random Size Variation, including independent shrink and grow percentages.
- Splatter and Splatter Distance.
- Fade, Taper, and their shared world-space length.
- Search tags.

Use clear names and comma-separated tags such as `skin, pores, subtle` or `cloth, stitch, trim`.

Selecting a preset copies its paint settings into the current editable session brush. Adjusting brush controls therefore does not silently change the shared asset. Use **Update Brush Asset with Current Settings...** to write the current shape, stamp source, size, hardness, flow, spacing, rotation, blend, mirror, follow, randomization, splatter, fade, taper, and evolution length settings back to the selected preset. Overlay Painter shows a confirmation warning before the asset is changed; shelf tags remain unchanged.

Assign the active **Brush Library** and use **Save Current Settings to New Brush...** to name and create a new preset from the session settings. The new `.asset` is added to that library, saved in the same project folder as the library asset, selected in the Asset Shelf, and revealed in the Project window. Overlay Painter reports the complete saved path when creation finishes. The active library is retained with the document's editor state and is also passed into the full Brush Library editor.

### Asset Shelf workflow

- Search by name, folder, or tag.
- Favorite production brushes.
- Use recents during an active task.
- Drag a Texture2D or Sprite from the Project window to create a session stamp.
- Duplicate a stable preset before making a materially different brush.

### Brush Library

Open **Brush Library** to create a `BrushLibrary`, add or remove presets, and import or export preset JSON.

The Brush Library window and BrushLibrary asset inspector include a **Drop Sprite Sheet Here** pad. Drop a Texture2D imported with Sprite sub-assets, or any Sprite from that sheet, to create one Stamp brush for every Sprite. New brushes are stored beside the BrushLibrary asset and named `<Sprite Sheet Name> 1`, `<Sprite Sheet Name> 2`, and so on. Sprites already represented by a brush in that library are skipped.

Brush assets are reusable settings. The paint document stores the settings and source references needed to reproduce its own layers.

--------------------------------------------------------------------------------

## Save, Recovery, and Document Ownership

### Temporary sessions

Opening Overlay Painter starts a temporary session unless an existing saved document was opened.

- Opening alone does not change the avatar, scene, recipe, source overlay, or source textures.
- After an edit, Overlay Painter writes recovery data.
- The workspace reports whether the session is Temporary, Recovered, Saved, Modified, Saving, or failed to save.

### Recovery location

Recovery uses:

- `painter_recovery.asset`.
- A sibling `painter_recovery Data` folder.

The default location is `Assets/UMA/Temp`. Configure **Overlay Painter Recovery Folder** under **Project Settings > UMA**.

For the default location, ignore both of these in source control:

```text
/Assets/UMA/Temp/
/Assets/UMA/Temp.meta
```

If the setting uses another folder, ignore that folder and its `.meta` file instead.

Only one painter recovery asset is active in the configured folder. It records its source context and is offered only to a matching launch. A later temporary session for another context can replace older unmatched recovery.

### Recover, Discard, or Cancel

When compatible recovery exists, Overlay Painter offers:

- **Recover**: open the last complete recovery snapshot.
- **Discard**: delete recovery and start fresh.
- **Cancel**: leave recovery untouched and stop opening.

### Save As

Use **Save As** to create the first permanent `TexturePaintDocument` below `Assets`.

The document stores:

- Editable base pixels.
- Layer pixels and metadata.
- Per-channel source type, texture or `Sprite` reference, overlay reference, color, inversion, Normal convention, and Fill X/Y tiling, X/Y offset, rotation, and shared-transform state.
- Per-channel Enabled, Lock Painting, Channel Paint Strength, Channel Opacity, and Channel Blend settings.
- Editable mask pixels, black/white base value, grayscale Mask Value, and mask-only effects.
- Paths and point dynamics.
- Brush and source settings.
- Plugin provenance.
- Workspace state.
- Stable surface identities and source fingerprints.

Pixel data is stored in a sibling `<Document Name> Data` folder. Keep that folder with the document asset.

Recovery uses the same layer-channel and mask serialization as a permanent document. Recovering a compatible session restores every material-channel source, including `Sprite` selections and Fill tiling/offset/rotation, plus the mask's grayscale Mask Value, effects, and rendered pixels.

### Save

After Save As, Save updates the existing project document. Unchanged content-addressed data is reused where possible.

### Closing with unsaved work

Closing a modified stage offers Save, Discard, and Cancel.

- **Save** commits the project document before deleting matching recovery.
- **Discard** deletes matching recovery and closes without saving the current edits.
- **Cancel** leaves the stage and recovery untouched.

Do not delete recovery manually while an active save is in progress.

--------------------------------------------------------------------------------

## Export Textures and UMA Assets

Saving preserves the editable paint project. Exporting creates runtime-ready physical textures and `OverlayDataAsset` assets. They are separate operations.

### Open export

Click **Export Textures & UMA Assets...**.

The dockable export window shows:

- Material capability diagnostics.
- Resolved physical texture channels.
- R, G, B, and A packing.
- File encoding and importer settings.
- Output texture and overlay paths.
- Slot or UDIM binding reports.
- Conflicts that must be resolved before writing.

### Required Export Identifier

Enter a clear **Export Identifier**. It is appended to generated texture and overlay names.

Use identifiers that distinguish the artistic variant, such as:

- `BlueDenim`
- `BattleDamage`
- `GoldTrim`
- `FaceTattoo03`

### Output choices

Session defaults and optional templates provide:

- Current-material or all-material scope.
- Output folder.
- Fail, overwrite, or versioned name-conflict policy.
- Native or fixed resolution.
- Albedo padding.
- Optional Addressables registration.

The `UMAMaterial` descriptor, not the template, controls physical channel order, component packing, PNG or EXR encoding, color space, normal convention, and importer settings.

Use **Save Overrides as Template** only when output-folder and policy choices should be reused. A template is not required for ordinary export.

### Export content

- **Flattened Composite** exports the reconstructed source plus visible base and layer edits. Normal
  Control is evaluated against the composed Normal channel before physical packing.
- **Runtime Overlay (Transparent)** excludes the reconstructed source and direct base edits. It
  exports visible authored layers and groups as a recipe-ready alpha-bearing overlay. Normal Control
  is converted to a flat-relative normal contribution, and its gradient footprint participates in
  the generated coverage mask.

Normal Control never appears as a standalone physical texture in either mode. Its only export result
is the change it produces in a material-declared Normal output.

### What export creates

For an ordinary slot, export creates:

- One physical texture per `UMAMaterial.MaterialChannel`.
- One configured `OverlayDataAsset` using those textures in channel order.
- Registration in `UMAAssetIndexer`, verified before success is reported.

For a UDIM group, export creates one `OverlayDataAsset` per physical member or tile and presents them as one result set. UMA's ordinary `OverlayDataAsset` model does not store several independent tile texture arrays in one asset.

### Packed maps

Overlay Painter repacks logical channels into the physical layout declared by the material.

Examples include:

- HDRP `_MaskMap` components.
- URP `_MetallicGlossMap` components.
- Roughness converted back to Smoothness where required.
- Output normal green convention.

Do not manually swap channels after export unless the material contract is also changed.

### Albedo padding

Albedo padding extends RGB beyond transparent UV borders while preserving original alpha. This reduces mip and filtering seams.

Padding cannot repair inadequate source resolution, incorrect UVs, or a texture imported with the wrong alpha settings.

### Transaction safety

Export stages and validates every output before commit. Existing files, importers, UMA assets, index entries, and optional Addressables entries are snapshotted and rolled back together on cancellation or failure.

Ordinary export does not:

- Save or flatten the paint document.
- Change its dirty state or history.
- Apply the overlay to a recipe.
- Change the avatar.
- Replace the source overlay or source textures.
- Create a material override.

### Overwrite Source Overlay

**Overwrite Source Overlay** is an advanced destructive mode available only when persistent compatible source overlays exist.

- The window lists every affected asset.
- A second confirmation is required.
- Backups are restored if the transaction fails.

Prefer a versioned export during development and source-control review. Use overwrite only when replacing the source assets is the explicit production decision.

### Add the result to a recipe

After successful export:

1. Open the intended base or wardrobe recipe.
2. Find the target slot.
3. Add the exported `OverlayDataAsset` to its overlay stack.
4. Keep the base overlay first when the recipe requires one.
5. Configure shared color or ordinary UMA overlay blend behavior if needed.
6. Save the recipe.
7. Rebuild a DCA and review the result under representative lighting.

For UDIM content, add the corresponding exported overlay to each physical member slot according to the project's UDIM recipe setup.

--------------------------------------------------------------------------------

## Recommended Production Workflow

### 1. Validate the material first

Open the `UMAMaterial` Inspector and verify channel meanings, shader properties, packing, color space, importer type, and normal convention before authoring detailed work.

### 2. Start from the simplest truthful source

- Use `UMAMaterial` for a neutral new surface.
- Use `OverlayDataAsset` when modifying an existing look.
- Use a generated DCA when the assembled character context matters.

### 3. Establish the base appearance

Set the base color or material Fill layers first. Check the result in representative lighting before adding small detail.

### 4. Organize by artistic purpose

Create named groups such as:

- `Base Material`
- `Construction Detail`
- `Graphics`
- `Wear and Dirt`
- `Normals`
- `Emission`

### 5. Use coordinated sources

Use Overlay sources or Sprite Sets when one motif needs matching Albedo, Normal, Roughness, Metallic, and AO information. This reduces channel drift between separately placed details.

### 6. Build reusable paths

Use Path layers for long seams, trim, piping, stitches, and repeated details. Keep them editable until the final look is approved.

### 7. Mask instead of erasing repeatedly

A layer mask preserves the original material pixels and makes edge revision easier. Add a black or white mask from the layer menu, click its thumbnail, and paint it in Mask Mode. Use **Fill Polygon** or **Fill UV Island** when geometry provides the desired boundary, and use Layer Mask Texture Overlay when an existing grayscale texture should modulate the result.

### 8. Review in several modes

- Shaded 3D preview.
- Active channel Solo.
- UV canvas with wireframe.
- Source Before comparison.
- Close and gameplay camera distances.
- Representative neutral and dramatic lighting.

### 9. Save milestones

Create a permanent document early. Save before merging layers, rebinding changed sources, or making broad effect changes.

### 10. Export a versioned candidate

Use a distinct Export Identifier and versioned conflict policy. Add the result to a test recipe and regenerate the character before replacing approved production assets.

--------------------------------------------------------------------------------

## Common Gotchas

### The source, layer, and target are independent

Assigning a source to one layer channel does not create or select a different destination layer. Selecting a layer does not select every target slot. Check the target, active layer, and authored channel sources before a long operation.

### The active channel can differ from a layer's other channels

A multi-channel layer can contain Albedo, Normal, and Roughness with different sources and controls. The active Paint / Preview Channel determines the UV canvas and channel-specific tool context. It does not hide the other channel cards or prevent a multi-channel Paint or Path operation from writing them.

### Layer blend is not UMA overlay blend

Overlay Painter layer modes control document composition. An exported `OverlayDataAsset` can later have ordinary UMA recipe blend settings. Do not expect changing one to rewrite the other.

### Source alpha controls coverage

Unexpected hard rectangles often come from an opaque source background, not the brush. Inspect source alpha and sprite slicing.

### Normal convention mistakes can look like lighting errors

Inverted green makes raised detail appear recessed under some lighting directions. Correct the source convention instead of compensating with color or arbitrary channel inversion.

### Roughness is always the logical editing convention

Do not paint Smoothness values into Roughness merely because the physical output stores Smoothness. The material descriptor performs the inversion.

### Fill tiling and Texture Overlay tiling use destination UVs

Flat fills and Texture Overlay effects follow destination UV orientation. Use Triplanar or a world-space ribbon workflow when UV orientation should not control the pattern.

### Effect widths are pixels

A 12-pixel stroke is proportionally different at 1K and 4K. Choose final or representative resolution before tuning effects.

### Ribbon Left and Right depend on path direction

Reverse the path or swap side settings when bevel, shadow, glow, or stitches appear on the opposite edge.

### Closed ribbons do not use endpoint art

Beginning and End sources are ignored for a closed loop.

### Sprite Sets use the shortest sheet

One undersized or unsliced sheet reduces the available coordinated sprite count for the complete set.

### Duplicate Sprite Set channels are ignored

Each Sprite Set should have no more than one sheet for a logical channel.

### Multi-slot painting requires selection

The brush can discover every contacted selected slot, even across material boundaries, but it does not paint unselected geometry.

### Projection can reach nearby surfaces

Thin or closely layered geometry may receive paint if projection depth and angle limits are too permissive. Tighten them before painting cuffs, lips, eyelids, fingers, straps, or layered clothing.

### Mirrored or overlapping UVs are intentional duplicates

Painting one UV location can affect geometry sharing that texture space. Use the 3D target, masks, and preferred surface behavior to disambiguate where possible, but a shared UV cannot store two independent pixel results in one texture.

### Groups have no material channels, but they can have masks

Select a child Paint, Fill, or Path layer before painting material data. Group visibility and opacity affect children, and a Group has no material channels or ordinary channel effects. A Group can own a layer mask; click its mask thumbnail to paint the grayscale mask that gates the combined child result.

Group membership is structural: children stay together immediately below their group. Removing a child moves it above the group, and deleting a group deletes every descendant after confirmation.

### Merge Down reduces editability

Merge only after independent sources, masks, paths, and effects no longer need separate revision.

### Save and Export are different

Save preserves the editable document. Export creates runtime assets. Export does not mark the document saved.

### Recovery is not a permanent document

Recovery is a temporary safety mechanism and can be replaced by another session context. Use Save As for work that must be retained.

### Export does not edit a recipe

The exported overlay is indexed and recipe-ready, but it is not automatically inserted into a character or wardrobe recipe.

--------------------------------------------------------------------------------

## Troubleshooting

### Overlay Painter will not open from an avatar

- Exit Prefab Mode.
- Confirm the DCA is in an open scene.
- Generate it successfully before opening Overlay Painter.
- Resolve any UMA generation errors first.

### Material preflight fails

- Confirm the project uses URP or HDRP.
- Confirm the selected `UMAMaterial` resolves the active pipeline material.
- Check every Material Property Name against the shader.
- Review **Overlay Painter Channel Layout**.
- Correct ambiguous or unsupported custom packed channels.
- Check whether required compute packing is supported by the current graphics environment.

### The brush paints nothing

- Select a target slot.
- Select a Paint layer for material painting, or select any existing layer/group mask thumbnail for Mask Mode.
- Confirm the active channel exists on the material.
- Confirm that channel is Enabled and not locked.
- Choose a valid Texture, Sprite, Overlay, or Color source for Paint.
- Check layer, channel, and group opacity.
- Inspect the effective layer mask and its mask-only effects. White reveals; black hides.
- Check projection depth, normal angle, and backface settings.

### A source overlay is unavailable

- Confirm it belongs to the selected slot context.
- Confirm its `UMAMaterial` is compatible.
- For a multi-member or UDIM target, assign compatible source overlays per member.
- Rebuild the UMA Global Library if the project asset is not indexed correctly.

### Sprite Set shows no selectable sprites

- Confirm every configured sheet is assigned.
- Import every sheet as multiple Sprites.
- Confirm each sheet has at least one sliced Sprite.
- Remove empty or duplicate channel entries.
- Use **Refresh** in the picker after changing assets.

### Sprite Set channels do not align

- Match sprite suffix numbering across sheets.
- Match sprite rectangles and art placement.
- Confirm every sheet uses the same conceptual index order.
- Check Normal convention and data-channel import settings.

### Normal detail looks inverted or dented

- Switch the source **Convention** between OpenGL and DirectX.
- Confirm whether the texture is raw RGB normal data or imported as a Unity Normal Map.
- Do not apply ordinary RGB inversion to fix only the green axis.
- Verify the export normal convention in the `UMAMaterial` output contract.

### Roughness looks opposite after export

- Inspect the material's physical layout.
- Confirm the physical component is declared as Smoothness when appropriate.
- Paint logical Roughness values in Overlay Painter.
- Do not add a second manual inversion during export or texture post-processing.

### Albedo layers or thumbnails turn black while inspecting Roughness

- The Paint / Preview Channel controls the UV canvas and channel preview; it does not change which channel data an Albedo-only layer owns.
- A layer-row thumbnail should fall back to one of that layer's own authored channels when it has no texture for the active preview channel.
- Use **Solo in 3D** to inspect raw Roughness values. Turn Solo off to judge Roughness through the material shader and scene lighting.
- Roughness changes the shaded response, not the base color. Make sure the `UMAMaterial` channel layout maps logical Roughness or Smoothness to the texture property actually sampled by the active URP or HDRP shader.

### A visible layer does not appear in 2D or 3D

- Confirm the layer row reports `ON`, its parent group is visible, and layer opacity is above zero.
- Confirm the required channel card is Enabled and Channel Opacity is above zero.
- Select the layer's authored channel as the Paint / Preview Channel when inspecting the UV canvas.
- Remember that a Roughness-, Metallic-, AO-, or Normal-only layer changes material response rather than Albedo color.
- The 3D view composites all visible layers. Selecting a group shows its children as a 2D composite; selecting a child shows the applicable channel without removing other layers from the 3D composite.

### Paint appears on the wrong surface

- Deselect unrelated slots.
- Reduce Projection Depth.
- Tighten Normal Angle Limit.
- Disable Paint Backfaces.
- Add a layer mask and use **Fill Polygon** or **Fill UV Island** to write the boundary into it.
- Inspect overlapping UVs in the UV canvas.

### A path jumps or crosses a seam incorrectly

- Confirm whether the path is in the 3D surface or 2D UV domain.
- Add points that clarify the intended route around close surfaces.
- Inspect source geometry and UDIM seam metadata.
- Rebind or reproject after intentional source changes.

### Layer effects update slowly

- Reduce working resolution during look development.
- Reduce very large effect widths.
- Hide unneeded effects while painting.
- Check the **Performance & Memory** panel for fallback counts and latency.

### Recovery is not offered

- Recovery is offered only to a matching avatar or standalone source context.
- Confirm the configured recovery folder.
- Another temporary context may have replaced the single active recovery asset.
- Open the permanent document instead when one was saved.

### Export is blocked

- Enter a non-empty Export Identifier.
- Resolve all material capability errors.
- Choose a valid folder below `Assets`.
- Review name conflicts and the selected conflict policy.
- Resolve duplicate UMA overlay names in the Global Library.
- Review orphaned or mismatched surface reports.

### Exported content does not appear on a character

- Confirm the exported overlay is indexed.
- Add it to the correct recipe slot.
- Confirm slot and overlay `UMAMaterial` compatibility.
- For UDIM content, add the matching result to each member slot.
- Save the recipe and rebuild the DCA.

--------------------------------------------------------------------------------

## Keyboard Shortcuts

| Action | Shortcut |
|---|---|
| Paint | `B` |
| Erase | `E` |
| Blur | `U` |
| Smear | `K` |
| Clone | `C` |
| Dodge | `O` |
| Burn | `Shift+O` |
| Normal Touchup | `N` |
| Plugin Brush | `P` |
| Arm color sampler in the 2D canvas | `I`, then click |
| Toggle global-X mirror | `M` |
| Toggle Asset Shelf | `Tab` |
| Select logical channels | `1` through `7` |
| Decrease or increase brush size | `[` or `]` |
| Decrease or increase hardness | `Shift+[` or `Shift+]` |
| Adjust size and hardness interactively | `Shift+Right Drag` |
| New temporary document | `Ctrl/Cmd+N` |
| Load document | `Ctrl/Cmd+O` |
| Undo | `Ctrl/Cmd+Z` |
| Redo | `Ctrl/Cmd+Shift+Z` |
| Redo alternative | `Ctrl/Cmd+Y` |
| Save | `Ctrl/Cmd+S` |
| Save As | `Ctrl/Cmd+Shift+S` |
| Duplicate layer | `Ctrl/Cmd+D` |
| Select all points on active Path | `Ctrl/Cmd+A` |
| Copy active Path | `Ctrl/Cmd+C` |
| Paste Path as a new layer | `Ctrl/Cmd+V` |
| Rename layer | `F2` |
| Delete layer | `Delete` or `Backspace` |
| Cancel an armed Polygon/UV Island fill, 2D sampler, or UV stroke | `Esc` |

--------------------------------------------------------------------------------

## Artist Release Checklist

Before approving an exported overlay:

- The active `UMAMaterial` and shader match the target render pipeline.
- Physical channel layouts and output settings are correct.
- Every painted logical channel has been reviewed in Solo mode.
- Normal sources and export use the intended conventions.
- Roughness/Smoothness conversion has been checked.
- Sprite Set channels align and use the same conceptual indices.
- Layer names, groups, masks, and paths are understandable to another artist.
- No important work exists only in temporary recovery.
- The permanent document and its data folder are saved together.
- The result has been checked in the 3D view and UV canvas.
- Seams, mirrored UVs, overlapping UVs, and UDIM boundaries have been inspected.
- The result has been checked at gameplay distance and target texture resolution.
- Layer effects have been judged at final resolution.
- Export preflight contains no unresolved errors.
- A versioned candidate export has been tested before destructive overwrite.
- Exported overlays are present in the UMA Global Library.
- Exported overlays have been added to the correct recipe slots.
- A rebuilt DCA has been reviewed under representative lighting and quality settings.
