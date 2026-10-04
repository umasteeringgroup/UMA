# Overlay Painter

Last reviewed: October 2, 2026. Updated against the current Overlay Painter implementation.

Overlay Painter is UMA's non-destructive surface-painting workspace for creating texture details directly on a reconstructed UMA slot or generated character. It combines a 3D paint view, a synchronized 2D UV canvas, material-aware channels, editable layers, surface paths, world-space projections, composable masks, and recipe-ready export.

Use it for work such as:

- Skin details, makeup, scars, tattoos, dirt, and damage.
- Clothing graphics, fabric variation, seams, piping, and stitches.
- Painted metallic, roughness, ambient-occlusion, normal, and emission details.
- Repeating materials and coordinated multi-channel texture sets.
- Details that cross UV seams, slot boundaries, or UDIM tiles.
- New `OverlayDataAsset` content that is ready to add to a recipe.

Overlay Painter does not paint into the selected source textures. It works in a temporary or saved `TexturePaintDocument`, then exports new textures and UMA overlay assets when requested. The source slot, source overlay, avatar, and recipe remain unchanged unless the separate destructive **Overwrite Source Overlay** export mode is deliberately enabled.

Related docs:

- [Overlay Painter Material Presets](OverlayPainter%20-%20MaterialPresets.MD) for saving, applying,
  versioning, and packaging reusable layer stacks.
- [Overlay Painter Generators and Filters](OverlayPainterGeneratorsAndFilters.md) for the included procedural plugins and their controls.
- [UMA Materials](UMAMaterial.md) for shader properties, channel layouts, packing, and output settings.
- [OverlayDataAsset](OverlayDataAsset.md) for ordinary UMA overlay authoring and recipe use.
- [SlotDataAsset](SlotDataAsset.md) for slots, source meshes, UVs, and UDIM metadata.
- [Wardrobe Recipe Editor](WardrobeRecipeEditor.md) for adding an exported overlay to wearable content.
- [Textures, UDIMs, and Texture Arrays](Textures-UDIM-Arrays.md) for the wider UMA UDIM workflow.

### Recent authoring workflows

The changes from the past few days are covered in the following sections:

- [Replacing individual UDIM tiles](#replacing-individual-udim-tiles): shared Fill sources, per-tile overrides, and transparent tiles.
- [Projection Layers](#projection-layers): per-channel sources, click placement, independent axis sizing, circular rotation, depth, surface wrapping, pinned warp grids, and cylindrical wrapping.
- [Clothing detail generators](#clothing-detail-generators): one construction selector with 48 path presets or 32 projection presets for folds, wear, pockets, hardware, damage, labels, hems, and seams.
- [Hem / Seam generator](#hem--seam-generator): garment construction, roping, thread rows, and protected seam masks.
- [Path fading and curves](#path-fading-and-curves): side and endpoint fades up to 200%, with independent opacity curves.
- [Composable mask effects](#composable-mask-effects): 44 inputs, adjustments, filters, and generators, plus reusable [smart-mask recipes](#smart-mask-recipes).
- [Camera-Space Painting Stencil](#camera-space-painting-stencil): position an image in the 3D view and paint through its coverage.
- [Live References and Linked Instances](#live-references-and-linked-instances), [Selections and Reusable Regions](#selections-and-reusable-regions), and [Layer Symmetry](#layer-symmetry).

For remaining workflow gaps and implementation scope, see the [core workflow roadmap](OverlayPainterCoreWorkflowRoadmap.md). The roadmap is not a claim of complete feature parity with other painting applications.

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
- **Source**: the texture, sprite, UMA overlay, or solid color stored on one authored layer channel and supplied to a paint, fill, path, or projection operation.
- **Destination**: the editable base or non-destructive layer that receives the result.

For example, a brush can use a tattoo `Sprite` as its **Source**, paint the **Albedo** channel on the torso **Target**, and write into a new Paint layer as its **Destination**.

A channel's source does not determine the destination, and selecting the Paint / Preview Channel does not automatically create that channel on every layer.

--------------------------------------------------------------------------------

## Open Overlay Painter

### From a SlotDataAsset

This is the recommended path when creating a new reusable overlay.

1. Select one `SlotDataAsset` in the Project window.
2. In Standard View, open **Validation & painting** and find **Open in Overlay Painter**. In Advanced View, it is directly below the **Validate**, **View MeshData**, and **Clear Errors** row.
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

By default, Overlay Painter opens its custom stage in a dedicated floating compact workspace. The
left side is a tab group containing **Overlay Painter Layers** and **Overlay Painter Brush**; the
right side is a tab group containing a dedicated **Scene** view and **Overlay Painter 2D**. A
resizable divider separates the two sides. Layers and Scene are selected initially, the active Body
target is framed, and the floating window remembers its size and screen position.

The project-wide **Overlay Painter Compact View** setting under **Project Settings > UMA > Editor
Settings** controls this behavior. Disable it to retain the traditional workflow: Layers, Brush, and
2D open as independent dockable windows and painting uses the existing Scene view. Painting tools
appear as the **Overlay Painter Toolbar** overlay. In Compact View, Overlay Painter binds its 3D,
Path, and painting toolbars exclusively to the dedicated docked Scene tab and hides those overlays
from every other Scene window.

### Global and 2D toolbars

The floating **Layer Preview** panel stays visible while you scroll through Properties. It appears in the 3D Scene view and the **Overlay Painter 2D** window and follows the active layer, mask generator, garment construction, or plugin brush. Drag its title bar to reposition it. Use the Scene view overlay controls to collapse or hide it; in the 2D window, use the title-bar collapse button. The 128 × 128 draft refreshes as parameters change, including when their section is collapsed. **Channel**, **Lit Surface**, and supported filter comparison views remain in the preview panel. For plugin layers and mask plugins, **Regenerate Layer** sits below **Refresh Preview** and applies the current settings at full resolution, using the same action as the plugin configuration's Regenerate button. It is disabled while generation or a document save is running.

The global toolbar contains the most frequent document and preview actions, while the 2D canvas has its own compact view toolbar:

- File commands for New Document, Load Document, Save, Save As, Revert, Export, Clear All, and Close.
- Undo and Redo.
- Save or Save As.
- Export.
- Open or focus the 2D UV and Brush windows.
- Solo the active channel.
- Compare against the source-before state.
- Isolate selected slots.
- Show the UV wireframe in the 2D canvas.
- Arm the 2D color sampler.
- Open layout controls.
- **Shutdown Overlay Painter** in the Scene-view toolbar. This full-width button uses the normal
  close flow, including Save, Discard, and Cancel when the document has unsaved work.

**Solo** is useful for inspecting raw channel values. **Before** is useful for comparing the complete shaded source with the edited result. They serve different preview purposes and are not a replacement for checking the exported material.

Starting compilation, an assembly reload, or Play Mode while Overlay Painter is active closes its
custom stage and synchronously hides all Overlay Painter Scene-view toolbars. This prevents a
nonfunctional painting, 3D-control, or Path toolbar from remaining after the painting context has
shut down.

### Scene-view painting toolbar

The **Overlay Painter Toolbar** is a native Scene-view toolbar overlay, not a separate dockable
window. It contains direct icon controls for Paint, Erase, Blur, Smear, Clone, Dodge, Burn, Normal
Touch-up, Plugin Brush, Polygon Fill, UV Island Fill, and Path authoring, plus shortcut help. Its
buttons stay synchronized with keyboard shortcuts and the compact Tool dropdown in the 3D controls
overlay. Unity can dock, reorient, collapse, or move the toolbar using the standard Scene-view
overlay controls. It appears only while an active Overlay Painter stage can paint.

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

Every character-launched session gives startup priority to a logical target whose visible name
contains the standalone word **Body**, and enables **Isolate** by default for a fresh or legacy
workspace. A saved target is used only when no visibly named Body target exists; an explicit current
Isolate choice is still restored. Standalone slot sessions keep their existing target and visibility
defaults.

#### Import warning indicators

Recoverable reconstruction conditions are retained as structured import warnings instead of being shown in a modal dialog every time the stage opens. Overlay Painter associates each record with its exact slot names, logical target IDs, and reconstructed texture surfaces.

- A yellow warning icon appears beside every affected logical target.
- Expanding a UDIM target shows the same indicator on each affected member row.
- The **Texture Sets** tab shows the indicator beside each affected physical texture set.
- A warning that cannot be associated with reconstructed geometry appears beside the target search controls as an unmapped import item.
- Clicking an icon opens a scrollable detail window containing the stable warning code, severity, material, slots, and full explanation.

The Scene view shows **Import completed with warnings. Click a warning icon beside a paint target for details.** along the bottom for about ten seconds. It remains fully visible for 7.5 seconds and fades over the final 2.5 seconds; it does not consume painting input. Recoverable warnings never need to be dismissed. A reconstruction or material-capability failure that would make painting unsafe is still blocking and prevents the stage from opening.

### UV canvas

The UV canvas shows the active logical channel and uses the same target, layer, per-channel sources, brush, masks, and undo history as the 3D view.

Use it to:

- Inspect exact UV placement.
- Paint directly in normalized texture space without raycasting back through the 3D mesh.
- View the UV wireframe.
- Pan and zoom.
- Set a clone source.
- Create and edit path points.
- Check details around UV seams and island edges.

Ordinary 2D brush strokes resample and rasterize on the texture plane. They do not run 3D triangle
projection or geometry clipping, so the cursor and painted footprint stay stable even on very small
or thin geometry. Polygon Fill and UV Island Fill intentionally query mesh ownership because their
purpose is to select geometry-defined regions.

The 2D view is synchronized, but **Spline Space** remains authoritative: a 2D Texture path uses the
UV domain, while a 3D Surface path uses the model surface. Input in the non-authoritative view is
ignored. This distinction matters around seams and repeated UVs.

### Layer / Path region

This region contains the ordered Paint, Fill, Path, Projection, Reference, Group, and plugin-created layers. It provides thumbnails, visibility, drag reordering, grouping, renaming, duplication, merging, effects, and deletion.

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
- Path.
- Plugins.
- Document.
- Performance and Memory.

Brush and Stroke/Projection properties are in **Overlay Painter Brush**, above the Asset Shelf.

### Asset Shelf

The Asset Shelf is part of the separate **Overlay Painter Brush** window, directly below the Brush and Stroke/Projection controls. Use the **Asset Shelf** toolbar button in that window or `Tab` to show or hide it, and drag the horizontal divider to resize it. The Layers window does not contain or reserve space for the shelf.

The shelf finds `BrushPreset` assets and supports thumbnails, search, folders, comma-separated tags, favorites, recents, custom ordering, rename, duplicate, and Project-window drag and drop. Its filters and selected brush remain synchronized with the rest of the Overlay Painter session.

Use **Layout > Reset Workspace** to reset the internal panel visibility and dimensions. When Compact
View is enabled, use **Layout > Reset Compact View** or **Window > UMA > Reset Overlay Painter
Compact View** to restore the floating window's default size, position, 40/60 split, tab groups, and
initial Layers/Scene selection.

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
- **Height Strength** is stored on each authored layer's Normal Control channel and scales only that
  layer's generated slope. Changing it on one Paint, Fill, or Path layer does not rescale any other
  Normal Control layer.
- **Sample Radius** controls the neighboring texel distance used for the gradient, and **Invert
  Height** reverses raised and recessed interpretation. These two conversion settings remain shared
  by the texture target so every Normal Control layer uses the same sampling convention.

Normal Control is a full layer channel. It can be authored with Paint, Fill, Path, Polygon Fill, UV
Island Fill, groups, layer masks, and layer effects. Color and texture input is constrained to
grayscale. It accepts a scalar value, `Texture2D`, or `Sprite` source. It does not offer an
`OverlayDataAsset` source because Normal Control deliberately has no material/overlay texture slot.
Select **Normal Control** to inspect or paint the height field; select **Normal** to inspect
the effective normal after Normal Control has been combined with the ordinary normal stack. The 3D
material preview always receives that effective normal.

The document saves the Normal Control base, every layer-channel texture and source, each channel's
Height Strength, and the target's Sample Radius/Invert Height settings. Older documents that do not
contain a per-channel Height Strength initially inherit their saved target strength; the value
becomes independent as soon as that layer channel is edited. **Flattened Composite** export bakes
the result into the physical normal output. **Runtime Overlay (Transparent)** converts authored
Normal Control content into a
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

Sources are stored per authored layer channel. In Properties, open the active layer's **Layer Channels & Settings**, expand the required channel, then choose **Texture**, **Overlay**, or **Color** under **Source > Type**. Expand a channel foldout to select it as the Paint / Preview Channel and edit its source controls. Each channel retains its own settings when another foldout is selected.

Sources are sampled into brush stamps or generated layer content. They are not modified in place. A multi-channel layer can use a different source type and asset for every channel.

### Texture source

Texture source mode accepts either a complete `Texture2D` or one `Sprite` from a sprite sheet.

#### Complete Texture2D

Assign **Texture** when the entire image is one brush, fill, path, or projection source.

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
4. Paint, fill, apply the path, or place the projection.

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

On ribbons and Flat/Triplanar Fill projections, the Albedo sprite's transparency supplies coverage for every channel, including transparent holes and soft edges. Coverage follows the Albedo crop and projection transforms, and ribbon tile flips and crossfades. Overlay sources use their explicit alpha mask when present, otherwise albedo alpha. Other maps can retain their own data alpha; partial coverage preserves their visible values and normal directions.

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

#### Replacing individual UDIM tiles

A UDIM group keeps one shared layer stack. Select a Fill layer and use **UDIM Sources** in its properties to assign sources per tile:

- **Use shared source** follows the ordinary Fill source controls.
- **Override** accepts a **Tile Overlay** for all compatible material channels. Use **Texture Channel** and the channel's **Texture** field to replace individual channels; a channel texture takes priority over the tile overlay. With neither assigned, the channel inherits the shared source. Channels absent from an assigned overlay contribute nothing.
- **No contribution** makes this Fill transparent on that tile, revealing the layers below.

For a head-only replacement, assign the new head texture to tile 1001 and set the other tiles to **No contribution**. Use Flat projection, tiling `(1, 1)`, zero offset/rotation, full opacity, and Normal blend for an ordinary opaque replacement. Source alpha still controls coverage.

Tile overlays use their own alpha mask, or the first texture's alpha when no explicit mask exists. Channel texture overrides use their own alpha. Existing editable layer masks still apply. Automatic coverage from an Overlay-backed Fill is moved into generated channel alpha when editing tile sources so the original overlay's coverage is not applied again.

Shared source and projection edits preserve tile assignments. Layer order, visibility, and opacity remain shared. Tile assignments survive undo/redo, duplication, saving/reopening, and per-tile export; switching to **Use shared source** keeps the override assignments available for later reuse. Rasterizing a Fill bakes each tile's effective result into its Paint layer.

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

Path layers store editable 2D texture-space or 3D surface-space curves and render them procedurally.

Use them for:

- Seams and piping.
- Stitches and laces.
- Straps, stripes, and trim.
- Repeated decals along a route.
- Controlled scars, cracks, or painted lines.

Path layers are described in detail under [Surface Paths](#surface-paths).

### Projection layer basics

Use a Projection layer for one editable placement of a tattoo, fingernail, patch, or other detail. Every channel has its own source while placement, shape, depth, and fade stay aligned. Planar, Wrapped, and Cylindrical modes cover flat decals, fitted surface patches, and limb wraps. See [Projection Layers](#projection-layers) for the complete workflow.

### Reference layers

Use a Reference layer to reuse another layer's evaluated output or mask without copying its pixels. Named anchors and linked instances let related material details update together. See [Live References and Linked Instances](#live-references-and-linked-instances).

### Group layers

Groups organize Paint, Fill, Path, Projection, Reference, Plugin, and nested Group layers.

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
- Groups do not use ordinary material-channel layer effects. Their masks can use the complete composable mask-effect stack, smart-mask recipes, and legacy noise/texture effects.
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
- **Merge Group to Paint Layer** on a group.
- **Merge Selected** for marked layers or groups.
- **Remove from Group**.
- **Delete** or `Delete`.

**Merge Down** bakes two adjacent sibling layers into one Paint layer, including cached Plugin output, masks, and supported effects across every authored channel. Both layers and their channel overrides must use **Normal** blend. Plugin generators and filters are not rerun during the merge.

**Merge Group to Paint Layer** bakes all descendants, including nested groups, into one Paint layer. The replacement retains the group's name, visibility, opacity, blend, and an independent editable mask. The group's local mask effects remain editable; masks derived from layer references are baked. Child masks and effects become pixels. Combinations that cannot retain their appearance are rejected with a reason.

To combine several groups or layers, **Ctrl-click** (Windows) or **Cmd-click** (macOS) each desired row. Blue outlines mark the merge selection; the active painting layer and Properties panel stay unchanged. Choose **Merge Selected** in the selection bar, the **Merge** menu, or a row menu. Mark adjacent siblings under the same parent; each selected group includes its complete subtree. Selected roots must use Normal blend, though their children may use other blends. An ordinary row click, **Clear**, or **Escape** in the Layers panel clears the marks. A successful merge selects the resulting Paint layer.

Merges preserve all authored channels and apply atomically across linked UDIM tiles. Undo restores the original layers, groups, and active layer; Redo restores the merged result. Finish running Plugin generation or saving before merging. Changes that would break references from other layers are refused. Some backdrop-dependent effects and normal-strength combinations must be merged inside a containing group. Keep a duplicate if the original procedural settings will be needed after closing the editing session.

Layer structure changes participate in Undo and Redo.

To reuse one layer, a complete group subtree, or the entire stack on other paint targets, save it as
a Material Preset. See [Overlay Painter Material Presets](OverlayPainter%20-%20MaterialPresets.MD)
for creation, compatibility, application, versioning, packaging, and production best practices.

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
- **Channel Paint Strength**: scales how strongly new brush input is deposited on Paint and Path layers. Fill and Projection layers do not use this brush control.
- **Channel Opacity**: scales this channel during composition.
- **Channel Blend**: chooses the blend behavior for this channel.
- **Height Strength** on Normal Control: scales only this layer channel's contribution to the
  effective normal. Sample Radius and Invert Height remain target-level conversion settings.
- Source and source-specific settings.

Use **New Channel** and **Add Channel** to add another channel supported by the active material. Channels already authored by the layer are omitted from the dropdown. Expanding a channel foldout selects it as the Paint / Preview Channel. The current channel is marked **Active Preview Channel**; each foldout exposes its own source and composition settings.

Use **Remove Channel** inside a channel foldout to delete that channel's texture and settings. Overlay Painter asks for confirmation and keeps the operation undoable. Effects targeting the removed channel are retargeted to the layer's first remaining channel, or disabled when no channel remains, so the effect stack never contains an enabled invisible target.

Use channel controls for a coordinated material layer whose Albedo should remain strong while its Normal or Roughness contribution is reduced.

**Lock Painting** is per channel, not a complete layer lock. Check every authored channel before assuming a layer is protected.

--------------------------------------------------------------------------------

## Live References and Linked Instances

### Name and reference a layer

1. Select the source layer and expand **Properties > Active Layer > References and Instances**.
2. Give it an **Anchor Name** that describes the output, such as `Stitch Coverage` or `Worn Edges`.
3. Click **Reference This Layer** to create a live Reference layer.
4. Choose **Content Source**, **Read**, **Source Channel**, and **Output Channel** as needed. Color reads use the source color; alpha, luminance, RGB components, and mask reads provide coverage for **Tint**.
5. Adjust the reference's **UV Scale**, **UV Offset**, and **UV Rotation** independently.

The source picker identifies the material, slot or tile, stack position, and anchor name. Source effects, channel opacity, visibility, and masks are evaluated before the reference. Referencing a group reads its isolated child composite, excluding the base texture.

Every layer also exposes **Live Mask Source**. It multiplies the layer's painted/procedural mask by the chosen source. To combine several sources with independent blend modes and ordering, use **Layer Reference** entries in the [mask-effect stack](#composable-mask-effects).

### Create a linked instance

**Create Instance** shares source content while retaining local placement and masking:

| Source type | What remains local to the instance |
|---|---|
| Fill | UV scale, offset, rotation, layer opacity, and painted mask |
| Projection | World placement, size, depth, flips, wrapped control points/pins or cylinder settings, layer opacity, and painted mask |
| Paint, Path, Plugin, Group | Reference-layer UV transforms, opacity, and painted mask; content follows the source's evaluated UV output |

Edit the source to change shared imagery and material settings. Use **Make Independent** to remove the dependency while preserving the current result. A duplicated layer is an independent copy; a linked instance continues to follow its source.

### Dependencies, UDIMs, and persistence

References update in dependency order. A missing source or circular dependency shows a diagnostic and retains the last cached result. Repair the source reference to resume live updates. Documents and recovery retain cached reference pixels and masks, including mask-stack inputs.

Within a logical UDIM target, a reference resolves to the corresponding local tile member. A cross-target reference uses the explicitly selected source surface. Duplicating a group or applying a material preset remaps references between the copied layers. A reference to a layer outside the saved preset still needs that source to be available.

Downstream Plugin layers and procedural masks refresh when linked inputs change. These derived updates preserve the artist's Undo history. Save and export wait for active gestures and pending linked updates before capturing their result.

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

The popup is an ordered effect stack. Use the arrow buttons to reorder entries, **Add** to create another instance of any supported effect, and **Ã—** to remove an entry. Multiple instances are supported. An effect can target only a channel actually authored by that layer; add the channel first if it is not listed. Paint and effects are evaluated as one isolated layer result, then the layer and channel opacity are applied once. This prevents a mask or partial opacity from being multiplied again for every effect pass.

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

When a layer or group has a mask, the same **fx** popup includes its composable mask-effect stack. It also retains the two legacy mask-only effects, which never modify material channels directly:

- **Layer Mask Noise** generates deterministic grayscale noise with Seed, X/Y Tiling, X/Y Offset, Detail, Balance, Contrast, Invert, Combine, and Opacity controls.
- **Layer Mask Texture Overlay** combines a texture's Luminance, Red, Green, Blue, or Alpha component with the mask. It provides independent X/Y Tiling, X/Y Offset, Rotation, Invert, Combine, and Opacity controls.

The mask starts from its painted raster or Starting Value, applies any enabled legacy Noise and Texture Overlay, then evaluates the new stack from top to bottom. The mask thumbnail, previews, and export share this effective result. Use **Convert Legacy Effects to Stack** to make the old effects reorderable; see [Composable mask effects](#composable-mask-effects).

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

### Image Adjustments

Image Adjustments non-destructively changes one authored channel after the layer's pixels are
generated and before layer/channel opacity is applied.

- **Saturation** ranges from 0% to 200%; 100% is unchanged.
- **Brightness** adds or removes up to 100% brightness.
- **Contrast** adjusts values around the channel midpoint from -100% to 100%.
- **Hue** rotates color by -180 to 180 degrees.
- **Amount** blends between the unadjusted and adjusted channel.

Hue and Saturation are disabled for grayscale channels such as Roughness, AO, Thickness, Detail
Mask, and Normal Control. Brightness and Contrast remain available. Add separate Image Adjustments
instances when different channels or correction stages need independent settings.

### Effects on normals and data channels

An effect can target a material channel, but not every visual effect makes physical sense on every channel. Review Normal, Roughness, Metallic, and AO results in channel solo and in the shaded preview.

Distance-field effects are cached while editing and rebuilt from completed layer coverage after a stroke. Large effect widths on high-resolution layers can cost more to update than an ordinary paint layer.

For ordinary non-ribbon layers, the authored pixels are composited first and enabled effects then run in the visible stack order. Stroke and outer effects use the original authored-and-masked boundary rather than the growing shadow/glow result; Stroke begins outside that boundary at zero offset but can be moved inward or outward, and inner/color/texture effects remain clipped without inflating alpha. Ribbon-local entries run as ordered projection passes from the ribbon's own long-edge coordinates. Color Overlay and Texture Overlay remain channel-composite effects.

--------------------------------------------------------------------------------

## Ribbon-Specific Effects

The following effects use a ribbon path's cross-section and distance along the path. They are evaluated only when the Path layer's mode is **Ribbon**.

Left and Right are defined relative to travel from the first path point to the last. Reversing the path swaps the practical side orientation.

### Edge Fade

Edge Fade reduces ribbon opacity toward its long edges. Its current controls are **Side Fade (%)**, **Fade Ramp (%)**, and **Side Curve**. The selected Path's **Fading** section edits the same enabled Edge Fade effect, regardless of the displayed material channel.

- **Side Fade (%)** ranges from 0 to 200. At 100, the fade extends from each side edge to the centerline; at 200, it spans the full width and also fades the center.
- **Fade Ramp (%)** controls how much of that fade distance is used for the transition. Zero produces an immediate cutout; 100 uses the full distance.
- **Side Curve** controls opacity through the transition. The horizontal axis runs from interior on the left to edge on the right; height is opacity, from 0 to 1.

The fade follows the ribbon cross-section. Source texture rotation, mesh UV orientation, seams, and UDIM tiles do not rotate it. Source alpha is preserved when the path regenerates after moving a control point.

Start and end fades are separate path controls. See [Path fading and curves](#path-fading-and-curves) for their ranges, curve direction, and a zipper example.

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

## Projection Layers

A Projection layer holds **one projection**, fixed in world space while it remains editable. Use separate layers or linked instances for separate placements. It does not attach its authoring handles to later character pose or shape changes. Exported results are ordinary textures mapped to the model's UVs.

### Create a projection with Albedo and Normal

1. Select the logical paint target and click **+ Projection**, or use **Layer > New Projection Layer**.
2. The new projection is placed on an available target surface and aligned to its normal. If no surface is available, click the model to place it.
3. In **Layer Channels & Settings**, add or expand **Albedo**, choose **Texture**, and assign the image or Sprite.
4. Use **New Channel > Normal > Add Channel**, expand Normal, and assign its matching normal texture or Sprite. Set its source **Convention** correctly.
5. Click the desired location on the model. The projection moves there and aligns to the new surface normal.
6. Use the axis handles to size it, trace the ring to rotate it, and adjust depth to include the intended surface.

Each channel has its own Texture/Sprite, Overlay, or Color source, enabled state, inversion, opacity, and blend settings. The same placement, wrapping, and fade apply to all channels. Albedo alpha supplies their common silhouette; without Albedo, the first assigned map supplies it. Normal maps are reoriented into the receiving surface's tangent space.

Old single-source and overlay-wide projections retain their maps when opened and can be edited through these per-channel controls. Selecting the Paint / Preview Channel changes what you inspect; it does not replace the other assigned maps.

### Mirror a pocket or other asymmetric image

In Projection properties, use **Texture Mirroring (All Channels)**:

- **Flip X (Left/Right)** mirrors the source images horizontally. Use this to turn a right-hand pocket image into a left-hand pocket.
- **Flip Y (Up/Down)** mirrors the source images vertically. Both flips can be enabled together.

These are the projection's local image axes, so they follow its rotation. The same flip applies to every authored channel, including Albedo, Normal, Roughness, Metallic, AO, Emission, Normal Control, and custom channels. Source alpha and Alpha Outline fade mirror with the imagery. Normal-map vector directions are corrected as well as their pixel positions, keeping raised and recessed detail consistent.

For matching pockets, duplicate the Projection layer or use **Create Instance**, place the second projection on the other side, and enable Flip X on that layer. Linked instances keep their own flip settings while sharing the source images. Projection size, placement, and wrapped control points remain unchanged. Existing painted layer masks remain in destination UV space.

Flips work with Texture, Sprite, and Overlay sources in Planar, Wrapped, and Cylindrical modes. They are undoable and persist with the projection; source texture assets are not modified. **Invert** on a channel changes its values and is separate from image mirroring.

### Position, size, rotate, and set depth

**Gizmo Size (%)** in Projection properties scales the axis handles, center Move handle, rotation-ring thickness and spacing, and warp points with their hit areas. It defaults to **200%** (twice the original size) and adjusts from **25% to 500%**. **Reset** restores 200%. The choice is remembered for your editor across sessions and applies to all projections; changing it does not resize or regenerate the projection or add an Undo step.

With **Edit Warp** off, use these Scene-view controls:

| Control | Behavior |
|---|---|
| Click the model | Move the projection to that point and align it to the face normal; preserve size and transport its existing spin to the new normal |
| Center Move handle | Drag across the model to update position and surface-normal alignment continuously; preserve size and spin |
| X handle | Resize only along the projection plane's X axis |
| Y handle | Resize only along the projection plane's Y axis |
| Z Depth handle | Set maximum projection distance on either side of the placement surface |
| Rotation ring | Follow the cursor around the circle to rotate about the surface normal; complete turns and reversals are supported |
| Escape during a drag | Cancel that drag and restore its starting result |

Moving toward or away from the ring's center does not spin the image. **Lock Numeric Aspect** affects only the **Width (X)** and **Height (Y)** fields; X and Y gizmos always resize independently. **Match Source Aspect** restores the source image's proportions. Numeric dimensions are world units.

The cyan footprint and front/back outlines show the whole projection volume. Depth includes nearby raised and recessed polygons, with pixels outside the volume clipped. Older saved projectors retain their original centered volume until repositioned.

Drag the white **Move** handle at the center to slide the projection along the target surface. It follows the same rules as click placement, including refitting Wrapped patches and resetting their pins. A missed surface keeps the last valid position; moving back onto the model continues the drag. Click-to-place remains available away from the handles.

Each placement click or completed handle drag is one Undo step. Projection edits replace the previous output on every affected member of the logical target, including UDIM boundaries. Geometry and channel textures are reused during manipulation; supported GPUs calculate first-surface visibility without a CPU visibility rebuild. The drag uses the full configured texture and visibility resolution, with the same quality on release. Devices without floating-point blending use the CPU visibility fallback.

### Planar projection and visibility

**Planar** projects straight through the footprint onto the model. It gathers intersecting polygons, rejects back-facing polygons when requested, and clips the remaining fragments to the footprint and depth volume. Rejected fragments do not erase valid projection pixels from other polygons sharing a UV edge.

- **Front Faces Only** excludes polygons facing away from the projector.
- **Connected Surface Only** restricts coverage to the placed surface's connected region.
- First-surface visibility is enabled by default. **Project Through** includes hidden surfaces within the depth volume. Disable Connected Surface Only too when deliberately reaching disconnected mesh pieces.
- **Visibility Quality** sets the visibility sampling resolution. **Surface Tolerance** is a world-space allowance for that comparison.
- **Regenerate Projection** explicitly rebuilds the current definition.

Use the smallest depth that covers the intended surface. Overlapping UVs still share texture pixels: projection cannot store different final colors for two faces using exactly the same UV texel.

### Wrapped projection and Edit Warp

Use **Wrapped** for a detail that needs to follow a curved elbow, nail, or other surface. It uses a smooth interpolated patch shared by all channels.

1. Click **Edit Warp** on a placed Planar projection to switch to Wrapped and fit an initial patch, or choose Wrapped and use **Fit to Surface / Refit**.
2. Enable **Edit Warp** to expose the control grid. It starts with **3 x 3** points.
3. Drag an individual point along the model surface.
4. **Shift-click** a point to pin or unpin it. Pinned points appear orange and stay fixed during Refit or Reset.
5. Use **Control Grid** to choose **3 x 3**, **5 x 5**, or **9 x 9** when more local control is needed.
6. Turn Edit Warp off to return to whole-projection click placement, Move dragging, sizing, and rotation.

Subdivision preserves the patch's shape and matching pins. Reducing the grid removes intermediate controls and their pins; Undo restores them. **Unpin All** releases the pins. **Reset Unpinned Points** restores the unpinned controls to the default grid; use **Fit to Surface / Refit** to fit them onto the model again.

Refit searches within the depth volume and reports how many points were fitted or retained. Increase depth when the intended surface cannot be reached. Resizing or rotating transforms the existing patch; refit explicitly when the new footprint needs to conform again. Clicking a new location resets the pins and fits the patch at that location. Pins preserve the fitted authoring shape; they do not attach the patch to an animated character.

Warp edits support Undo/Redo, saving/recovery, material presets, and linked projection instances. Albedo, Normal, and all other channels use the same deformation.

### Cylindrical wrapping

Choose **Cylindrical** for a sleeve band, tattoo, or other wrap around a limb or tube. The cylinder axis follows the projection's Y direction, and the front of the cylinder touches the clicked surface.

- Width becomes **Arc Length (X)**. For a complete wrap it is the circumference: radius = arc length / (2 * pi).
- **Cylinder Arc** ranges from 10 to 360 degrees. Use less than 360 for a partial wrap.
- **Height (Y)** and **Depth (Z)** remain world-unit dimensions.
- All channels share the same cylinder and angle.

At 360 degrees, Rectangle edge fade affects the top and bottom without creating a faded vertical seam. The source image must itself tile horizontally for its image seam to disappear. Ellipse and Alpha Outline retain their source-shaped fades. Edit Warp is available for Wrapped mode, not Cylindrical mode.

### Projection fading

| Edge Fade | Use |
|---|---|
| None | Keep the source alpha without an extra footprint fade |
| Ellipse | Round or oval fade; circular when width equals height |
| Rectangle | Fade inward from the footprint border |
| Alpha Outline | Fade from the source silhouette, including holes; the silhouette boundary is measured at 50% alpha |

**Fade Width** and **Fade Curve** shape the edge transition. **Depth Fade** softens both depth boundaries. **Angle Fade Starts** and **Angle Fade Ends** soften the facing-angle limit independently. Source alpha is applied once, so a partially transparent source does not become darker from duplicate coverage multiplication.

For additional local correction, add a layer mask and use the regular mask painting or composable mask effects. These act after the generated projection coverage.

### Saving, reuse, and rasterizing

Projection layers support layer/channel opacity and blend, effects, painted masks, duplication, references, Undo/Redo, documents, and recovery. Material presets retain the world-space definition and cached output. If the original surface region is unavailable on another target, the cached output remains until you place the projection again.

**Rasterize to Paint Layer** in the layer row menu bakes the generated channels and effective mask into editable pixels and removes the editable projector. Export also uses the generated texture result, but does not require rasterizing the authoring layer. Exported textures follow the model normally through its UVs.

Use [Create Symmetry Instances](#layer-symmetry) for mirrored or radial copies while keeping one editable projection per layer.

--------------------------------------------------------------------------------

## Brushes and Stroke Controls

### Brush shapes

- **Circle**: general soft or hard painting.
- **Square**: block shapes and directional hard-edged work.
- **Stamp**: uses either a Texture2D or a Sprite. A Sprite uses only its authored sprite region, so a single item from an atlas can be used directly as the brush shape. Assigning one stamp source clears the other.

### Size

In the 3D view, brush size is evaluated in world space using the contacted triangle's UV-to-world
metric. This keeps a stroke's physical size more consistent across seams, rotated islands, and
different UV densities. In the 2D view, the same control is evaluated directly on the normalized
texture plane; no triangle metric or model projection changes the cursor footprint.

Use the UV canvas to inspect exact texture placement and texel quality. Use the 3D view when the
intended physical scale on the model is authoritative.

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

**Random Size Variation** changes the complete effective size of every sampled stamp. **Shrink (%)** and **Grow (%)** define the range below and above the authored brush size; both default to 30%. This is world-space size for a 3D stroke and normalized texture-plane size for a direct 2D stroke. The generated variation is retained by mirrored and projected copies of that stamp so seams and symmetry remain coherent.

**Splatter** randomly offsets each sampled paint stamp within a disk around the stroke. **Splatter Distance (%)** sets the disk radius from 1% to 200% of that stamp's effective brush size in the active stroke domain, after pressure and random size variation. The offset is deterministic for the stamp, remains tangent to the painted surface or in the 2D texture plane, and is shared by projected and mirrored copies so seams and symmetry stay coherent.

**Random Strength** is available only while Splatter is enabled. It gives each scattered stamp an
independent effective strength between zero and the current paint strength. The result multiplies
Flow, Fade, pressure, and layer/channel paint strength rather than replacing them. Its deterministic
per-stroke/per-stamp sequence makes repaint, undo/redo, save/reopen, mirrored copies, and projected
copies reproduce the same variation.

**Fade** lowers stamp alpha linearly as the current freehand stroke advances. **Taper** reduces the complete effective stamp size over the same distance. **World Length** is measured along a 3D stroke in model world units and along a direct 2D stroke in normalized texture-plane units; its untouched default tracks three times the current brush size. The first stamp is full strength and size, and the envelope reaches zero at the configured length. A new stroke restarts the envelope. When enabled, the existing Pressure Affects Flow and Pressure Affects Size controls multiply Fade and Taper respectively, so tablet pressure remains part of the result.

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

## Camera-Space Painting Stencil

A painting stencil is an image fixed to the current 3D view that limits brush coverage. Brush and layer-channel sources still supply the painted color and material values. Use a Projection layer when you want the image itself to remain an editable placed layer.

1. Open **Overlay Painter Brush > PAINTING STENCIL**.
2. Assign **Image**, choose **Mask Channel** (luminance, R, G, B, or alpha), and enable **Enable Stencil**. Use **Invert** to reverse its coverage.
3. Click **Center and Match Image Aspect** for a centered guide with the image's proportions.
4. Enable **Edit Stencil in 3D View** to position it.
5. Turn editing off and paint through the stencil onto the model.

| Gesture while editing the stencil | Result |
|---|---|
| Drag | Move the guide |
| Shift-drag around its center | Rotate the guide |
| Ctrl/Cmd-drag vertically | Scale the guide |
| Escape | Cancel the current drag, or exit editing when no drag is active |

**Viewport Center** and **Viewport Size** use viewport fractions; **Rotation** is also editable numerically. **Preview Opacity** changes the guide's visibility only, not paint coverage. Stencil edits are undoable.

The stencil constrains interactive 3D brush tools across all painted channels and layer masks, including erase, blur, and normal touch-up where that tool is applicable. It does not affect the 2D canvas, procedural paths, or existing Fill/Projection content. Coverage is projected once per surface/resolution per stroke and reused across channels. Stroke records capture the stencil and camera transform; the completed painted pixels save and reopen normally.

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

### Generator plugins

For detailed artist workflows, recommended stacks, control-by-control guidance, and troubleshooting
for every included generator and filter, see
[Overlay Painter Generators and Filters](OverlayPainterGeneratorsAndFilters.md).

Generators and filters are persistent **Plugin layers**, not commands applied to a Paint layer. Click
**+ Plugin**, select a plugin in the layer properties, adjust its layer-specific parameters, and click
**Generate**. The generated pixels are cached in that layer, so ordinary compositing, visibility,
opacity, blend mode, layer masks, groups, effects, Save/reopen, and export continue to work even when
the plugin is not installed. The layer is marked **Stale** when its parameters, position, or relevant
content below it changes; click **Regenerate** to replace its cache atomically.

**Generate/Regenerate** and the pending-change notice appear above the parameters, with another
apply button below long forms. Plugins that support spatial mapping expose **Fill Type** at the top:
**Flat (UV)** follows the mesh UVs; **Triplanar (World)** uses world coordinates. This changes the
plugin's own mapping. UV-only image filters and generators do not inherit a Fill layer's projection.
Opening a parameter section does not change the layer. Newly introduced controls receive their
plugin defaults when older documents load, while saved values, including intentional zeros, remain intact.

The host snapshots only the plugin's declared channels from the composite below that Plugin layer,
plus requested mesh maps and texture parameters. It exposes write-only channel dimensions without
copying their pixels and commits all logical-target outputs as one Undo/Redo transaction. A canceled
or failed run retains the last good cache. A missing plugin retains visible cached output and presents
an actionable missing-plugin message in the layer properties. The Plugins window remains the manager
for discovery diagnostics and non-layer plugin categories; generators and filters are selected on a
Plugin layer.

The included **Agify â€” Dirt & Edge Wear** generator is the reference mesh-map workflow. It provides:

- Concave dirt derived from signed curvature and source/generated AO.
- Convex edge wear derived from signed curvature.
- Additional high-frequency signed curvature calculated from the composed tangent-space Normal map.
- UV or seamless world-triplanar projection for optional Dirt Texture, Dirt Mask, Wear Texture, and
  Wear Mask inputs.
- Deterministic breakup, optional multi-level Fractal Edge boundary displacement, curvature contrast,
  AO influence, and independent dirt/wear amounts. Existing Agify layers retain smooth boundaries
  until Fractal Edge is raised from zero.
- Coordinated Albedo, Roughness, Ambient Occlusion, Metallic, and Normal Control output wherever the
  target material exposes those channels.
- Bounded source snapshots and compact strip output so native 2K-4K texture targets stay within the
  plugin transaction memory budgets.

Agify's texture masks are baked into generated channel coverage. Add an ordinary editable layer mask
to the Plugin layer when further hand-painted art direction is needed. The generated pixels, plugin
ID/version, typed parameters (including texture references), cache state, masks, effects, and channel
settings persist with the paint document.

Two focused generators provide more direct art control than the combined Agify workflow:

- **Dirtify â€” Gap Dirt** detects concave curvature and source/generated AO. **Gap Size** controls the
  neighborhood radius, **Gap Detection Level** controls which cavities qualify, and **Dirt Spread**
  controls how strongly nearby gaps expand into the surrounding surface. Fractal Breakup, Scale,
  Levels, Level Strength, and Fractal Edge independently control breakup from broad islands down to
  fine boundary damage.
- **Edge Wear** detects convex signed curvature while excluding cavities. **Edge Size**, **Edge
  Detection Level**, and **Wear Spread** control the width and reach of the worn region. It exposes
  the same multi-level fractal controls plus worn color/texture/mask, roughness, metallic, and depth.

Size is measured in pixels at the current output-channel resolution. Spread controls the strength of
the neighboring feature over that radius; a zero spread leaves only the detected gap or edge itself.
Both generators reject neighborhood and Normal-detail samples that cross UV-island IDs.

#### Cloth Texture generator

Add a Plugin layer, choose **Cloth Texture**, and click **Generate** after configuring the fabric. The
generator is intended for shirts, sweaters, trousers, denim, canvas gear, and other cloth surfaces.
It produces native-resolution material data without reading or baking the existing character atlas.

Choose **Fill Type** before setting the weave scale. Flat uses UV coordinates; Triplanar blends
world-space projections across the surface. Fabric, thread, pattern-tint, and worn colors and their
contribution controls are grouped under **Colors**. Stripe colors stay beside each stripe's placement
controls. Click **Regenerate** after editing these settings to update the visible result.

1. Under **Output Channels**, enable any combination of **Albedo**, **Roughness**, and **Normal
   Control**. All three are optional, but at least one must remain enabled.
2. Under **Fabric Weave**, select Cotton/Plain, Knit, Twill, Corduroy, Herringbone, Denim, Canvas,
   Linen, Satin, Basket, Houndstooth, Leno, Dobby, Pile, Crepe, or Jacquard. Set Thread Repeats first,
   then adjust aspect, fabric rotation, thread roundness, definition, and irregularity.
3. Under **Surface Response**, tune the neutral-gray Normal Control height, base/weave roughness,
   and fine fiber variation. Normal Control is combined with the Normal map for live display and
   export; it is not a shader texture by itself. **3D Depth** scales weave and motif relief;
   **Relief Shading** independently controls contact darkening between yarns in Albedo.
4. Under **Stripes / Plaid**, set the vertical and horizontal repeat-cell counts and add any number of
   stripe entries. Each entry has direction, color, position, width, edge softness, opacity, reorder,
   and delete controls. Entries later in the list blend over earlier entries. Use only vertical entries
   for pinstripes, only horizontal entries for bands, or both for plaid.
5. Optionally assign a **Pattern Sprite**. Choose Whole Fabric, Inside Stripes, or Outside Stripes;
   align it to warp, weft, or either diagonal; then set tiling, aspect, rotation, and X/Y offset. Enable
   Use Sprite Color for authored RGB motifs, or leave it disabled to tint a grayscale/alpha motif.
   Pattern Height and Pattern Roughness coordinate the motif across material channels.
6. Under **Thread-Aware Color Wear**, raise Color Fade and choose a faded color. Region scale, level,
   breakup, and direction define broad wear. Follow Weave and Worn Fiber Contrast keep the fade tied
   to thread structure; Worn Roughness Change and Worn Thread Flattening supply the physical response.

The Sprite input is cropped to its Sprite rectangle even when it belongs to an atlas, and it does not
require Read/Write import. Cloth settings, ordered stripe definitions, Sprite reference, cached output,
and all optional-output choices persist with the Plugin layer and participate in Undo/Redo.

#### Quilt, Embroidery, Perforation & Atlas Scatter generator

This four-mode Plugin layer produces optional Albedo, Roughness, Metallic, Ambient Occlusion, and
Normal Control, or grayscale coverage when run as a layer/group mask generator.

- **Quilt** coordinates padded cells, recessed seams, broken stitches, AO, and thread color.
- **Embroidery** fills an alpha/luminance Pattern Texture with directional thread ridges, breakup,
  sheen, color, and raised Normal Control.
- **Perforation** provides grid, staggered/hex, and organic punched-hole layouts with adjustable
  radius, bevel, lip, recess, roughness, and AO. It shades holes but does not alter mesh topology.
- **Atlas Scatter** samples deterministic cells from a regular rows/columns atlas and varies density,
  position, size, rotation, tint, and material response. Pad cells to avoid bilinear bleed.

#### Text generator

Text has an optional Font asset, pixel Font Size, style, color, spacing, alignment, weight, outline,
shadow, and placement. It can write Albedo, Normal Control, Roughness, and Metallic independently. In
mask mode it writes only grayscale glyph coverage, which can reveal a coordinated Group material.

Block mode positions text in UV space. **Follow Custom Ribbon** reads a white/gray ribbon from the
composed Custom channel below the Plugin layer, extracts a smoothed centerline, and warps text along
it. Create an editable Path/Ribbon below Text, make it write Custom, then edit the path and Regenerate.
Guide Threshold, Ribbon Padding, and Fit To Ribbon Length control extraction. Avoid branched,
self-overlapping, or sharply doubled-back guides.

### Production filters

Filters use the same persistent Plugin-layer workflow as generators. Place the Plugin layer directly
above the content it should adjust, select the filter, choose **Source Channel** and **Output
Channel**, then click **Generate**. Because the cached result is a normal layer, layer opacity is an
additional non-destructive filter-strength control. Regenerate after changing the source composite.

- **Levels & Curves** provides Input Black/White, Gamma, Output Black/White, a master curve and
  Amount. Enable **Preserve Hue / Adjust Luminance** when grading Albedo or Emission without shifting
  chroma. Disable it for independent RGB curve work or scalar maps.
- **Normal & Height Toolkit** adjusts tangent-normal strength, reconstructs Z, flips the green/Y
  convention, converts a grayscale Height or Normal Control source into a Normal channel, combines
  a tiled/offset detail normal through Reoriented Normal Mapping (RNM), or scales Normal Control
  around neutral gray. Height Sample Radius is measured in output pixels. Use a negative Height
  Strength to reverse raised and recessed detail.
- **Blur, Sharpen & Detail** includes Gaussian Blur, Directional Blur, edge-preserving Bilateral Blur,
  Median cleanup, Unsharp Mask, and High Pass. Radius is in destination pixels; Direction uses
  degrees. Bilateral Edge Preservation controls how strongly unlike neighboring values are rejected.
- **Channel Operations** can Invert, Clamp/Remap, convert to Grayscale, shuffle RGBA/luminance/constant
  components, apply a three-stop Gradient Map, replace a color with tolerance and softness, or add
  deterministic fractal Color Variation. Choosing different source and output channels is useful for
  deriving a Roughness, Detail Mask, or Normal Control foundation from existing artwork.
- **Morphology & Distance** performs UV-island-safe Dilate, Erode, Feather, Choke, Outline, Signed
  Distance, Edge Detect, and Bevel Height. Radius and Softness are pixel distances. The filter uses
  the Surface ID mesh map, so an expansion cannot bleed from one UV island into a nearby island.
- **Stylization, Kuwahara & Quantization** provides edge-preserving 4/8/12-sector Kuwahara abstraction
  with tile-local summed-area acceleration, RGB or luminance banding, a 2-8 color palette, deterministic
  ordered dithering, and toon bands with texture-space edge ink. It supports grayscale Mask mode.

For repeatable results, keep source and output on separate Plugin layers when a filter feeds another
filter. A filter reads the composite below its own stack position; it never reads its previous cache,
so regeneration does not accumulate damage.

--------------------------------------------------------------------------------

## Masks

Each Paint, Fill, Path, Projection, Reference, Plugin, or Group layer can own zero or one editable grayscale mask. White reveals the layer, black hides it, and gray produces partial contribution. Its painted raster, ordered mask effects, and optional live inputs combine into one effective mask used by preview and export. Reusable selections are separate painting constraints; see [Selections and Reusable Regions](#selections-and-reusable-regions).

### Add, select, and remove a mask

1. Open the layer row's `â‹®` menu.
2. Choose **Mask > Add Black Mask** or **Mask > Add White Mask**.
3. Click the new grayscale thumbnail beside the layer thumbnail to enter **LAYER MASK MODE**.
4. Paint in either the 2D UV canvas or 3D Scene view.
5. Click the main layer thumbnail or row, or press **Escape**, to leave Mask Mode. Escape finishes an active mask stroke first; if a geometry fill is armed, the first Escape cancels that tool and the next exits Mask Mode.
6. Use **Mask > Remove Mask** when the layer should return to unmasked contribution.

A black mask starts with a white Mask Value so the first stroke reveals content. A white mask starts with a black Mask Value so the first stroke hides content. **Erase** restores the mask's original black or white creation value.

The 2D canvas shows the effective grayscale mask while Mask Mode is active. Both the 2D canvas and Scene view display a prominent **LAYER MASK** label. The Scene view remains a shaded composite by default; enable **Solo Mask** to place the grayscale mask directly on the 3D model for inspection.

### Paint a mask

Mask Mode uses the ordinary Paint, Erase, Blur, Smear, Clone, Dodge, Burn, and compatible Plugin Brush tools. The result is always normalized back to grayscale with opaque storage alpha. Normal Touchup is disabled because a mask has no tangent-space normal meaning.

Mask strokes use the same target projection, brush shape, stamp, pressure, stabilization, geometry clipping, Undo, Redo, save, recovery, and logical-target behavior as material-channel strokes.

The active layer exposes only a scalar **Mask Value** from 0 (black) to 1 (white). Mask Mode has no material-channel selector and cannot use a Texture, Sprite, OverlayData source, or layer-channel overlay. Brush shape and stamp alpha still control the stroke footprint, but the deposited value is always grayscale. The Mask Value survives layer duplication, document save/reopen, and crash recovery.

### Composable mask effects

Select the mask and expand **Mask Effects & Smart Masks** in Properties, or open the layer's **fx** popup. Unlike material-channel effects such as a colored glow, these effects operate on the grayscale mask and therefore gate every channel of the layer or the group's complete composite.

1. Leave **Start From Painted Mask** enabled to begin with the editable mask pixels. Disable it to begin with **Starting Value** instead.
2. Choose **New Effect** and click **Add**.
3. Expand the entry and set its name, **Blend**, **Opacity**, and effect-specific controls. **Invert Result** reverses that entry's output.
4. Use **Up** and **Down** to change evaluation order, **+** to duplicate, or **X** to remove. Each entry can be disabled independently.
5. Inspect the effective result with **Solo Mask**.

Effects evaluate **top to bottom**, starting with the chosen initial mask. Source entries generate a value; filters process the accumulated result. An entry's blend combines its result with that accumulation, and its opacity controls the contribution. At full opacity, **Replace** substitutes the entry's result; **Multiply** restricts it to the existing coverage. The complete blend list is Replace, Multiply, Add, Subtract, Screen, Overlay, Min, Max, Difference, Soft Light, and Divide.

Ordering matters: Noise followed by Levels then Blur differs from Noise followed by Blur then Levels. A later Replace source can overwrite an earlier effect. Collapsing a foldout changes only the UI and does not create an Undo step.

#### Available effects

The stack provides 44 effect kinds:

| Family | Effects and purpose |
|---|---|
| Basic inputs | **Fill** supplies a constant; **Texture** reads luminance/R/G/B/alpha; **Painted Mask** reads the original editable raster; **Layer Reference** reads another layer, channel component, or mask |
| Noise and cells | **Noise**, **Turbulence**, **Voronoi**, **Cells** generate seeded variation and cell patterns |
| Geometric patterns | **Gradient**, **Radial Gradient**, **Stripes**, **Checker**, **Dots** generate controllable UV patterns |
| Tonal adjustments | **Invert**, **Levels**, **Curves**, **Brightness Contrast**, **Gamma** remap the accumulated grayscale values |
| Range and segmentation | **Threshold** with softness, **Posterize**, **Clamp**, **Remap**, **Smoothstep** control bands, limits, and transitions |
| Smoothing and detail | **Blur** (Gaussian), **Directional Blur**, **Sharpen**, **High Pass** soften or isolate detail |
| Shape and edges | **Dilate** grows white regions; **Erode** shrinks them; **Outline** extracts an exterior border; **Edge Detect** identifies changes in the mask |
| Boundary distance | **Distance** creates a normalized signed-distance mask around **Boundary Threshold**; **Feather** softens that boundary |
| Distortion | **Transform** moves/scales/rotates the accumulated mask in UV space; **Warp** displaces it with noise |
| Mesh inputs | **Curvature**, **Ambient Occlusion**, **Thickness**, **World Normal**, **World Position**, **Mesh ID** derive coverage from the reconstructed mesh or an assigned map |
| Material-wear generators | **Edge Wear**, **Cavity Dirt**, **Dust** combine mesh information with grunge variation |

Filter **Radius (pixels)** is measured at the working texture resolution and ranges up to 128 pixels. Spatial operations use half-float GPU intermediates and update the affected surrounding region, so moving or painting an input also updates its blur, growth, transform, or feather. These are texture-space filters; they do not promise a continuous kernel across disconnected UV borders or separate UDIM textures.

**Curves** maps input grayscale on the horizontal axis to output grayscale on the vertical axis. This differs from a path's fade curve, whose horizontal axis represents distance toward an edge or endpoint.

Mesh effects accept **Map Override (optional)** for imported maps. Built-in Ambient Occlusion uses a quick concavity/accessibility estimate, and Thickness uses an estimate from mesh bounds. These are not production ray-traced or high-to-low mesh bakes. Curvature uses 0.5 for flat, darker values for concavity, and lighter values for convexity. **Mesh ID** uses **ID Value** with Red for triangle ID, Green for surface ID, or Blue for UV-island ID. World-direction effects expose **World Direction**; World Position also exposes input-range controls.

#### Keep painted corrections independent

To generate dirt that you can erase locally without repainting its procedural source:

1. Add a **white** layer mask.
2. Disable **Start From Painted Mask**.
3. Add a generator such as **Cavity Dirt** with Replace, then add Levels or Curves to tune it.
4. Add **Painted Mask** last with **Multiply**.
5. Paint black on the layer's mask thumbnail to exclude dirt, or white to restore the generator's coverage.

Painting always edits the original raster input. The stack does not flatten back into those pixels. If a later Replace source seems to ignore your painting, move a Painted Mask entry after it and use Multiply. For several independent painted inputs, create separate Paint layers and read them through Layer Reference entries; one mask does not contain multiple independent paint rasters.

#### Live inputs and older masks

**Layer Reference** entries can read regular layers, Plugin output, named anchors, channel components, or another layer's mask. Their dependencies are checked for cycles. Missing or circular sources show a diagnostic and retain cached input where available, including after document reload. Logical UDIM references resolve to the local tile member; duplicated groups and material presets remap internal links.

Older **Layer Mask Noise** and **Layer Mask Texture Overlay** settings keep their appearance and evaluate before the new stack. Use **Convert Legacy Effects to Stack** to turn them into ordinary reorderable entries while preserving their values.

### Smart-mask recipes

Under **Mask Effects & Smart Masks > Smart Masks**, choose an **Example** and click **Use Example Recipe**. Included recipes cover cavity dirt, edge wear, dust, position gradient, grunge, soft border, spots, and scratches. Tune their ordinary stack entries after applying them. Examples end with a Multiply Painted Mask entry: start with a white mask for full procedural coverage, or paint white into an existing black mask to reveal it.

**Save Recipe...** creates a reusable `TexturePaintMaskPreset` Smart Mask asset and embeds its texture inputs. Select a **Recipe Asset** and choose:

- **Replace Effects** to replace the current recipe and its starting settings.
- **Append Effects** to add the recipe's entries after the existing stack, preserving the destination's starting settings.

Neither operation replaces the destination's painted corrections. Appended effects retain their stored blend modes: a Replace entry still replaces the accumulated result at that point. Recipes are ordered effect lists, not isolated nested subgraphs.

A recipe with layer references still needs those source layers. Use a Material Preset to carry the mask and its related source layers together. Stack settings survive mask copy/paste, layer duplication, Undo/Redo, saving, and recovery.

### Filter or generate a mask

For an editable sequence that you can reorder later, use the mask-effect stack above. The existing **Mask Filter / Generator** workflow instead generates replacement mask pixels in a single operation.

While the mask thumbnail is selected, expand **Active Layer > Mask Filter / Generator**. The list
contains only plugins that explicitly support a grayscale Layer Mask target. Choose one, adjust its
parameters, and click **Generate Mask**. Material source/output channel selectors are hidden because
the input and output are the selected mask itself.

The host snapshots the current mask, generates a replacement in private memory, clips it to the
surface, forces it to opaque grayscale, and swaps the completed result into every logical target as
one Undo step. Canceling or encountering an error leaves the previous mask untouched. The selected
plugin, version, parameters and generated pixels survive Save/reopen and recovery. You can paint,
fill polygons, or fill UV islands over the result afterward.

The mask-compatible built-ins are **Levels & Curves**, **Blur, Sharpen & Detail**, **Channel
Operations**, and **Morphology & Distance**. A typical procedural mask workflow is: create a black
or white mask, use Morphology & Distance to establish or widen a boundary, use Blur/Sharpen/Detail to
soften or clean it, use Levels & Curves to restore contrast, then hand-paint exceptions. Normal &
Height Toolkit is intentionally unavailable because a tangent-space normal operation has no valid
grayscale-mask interpretation.

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

For projected 3D painting, Overlay Painter applies structural geometry clipping automatically. Each
contacted slot and UV island receives a per-texel geometry mask so a rectangular texture update does
not leak into unrelated polygons. Polygon Fill and UV Island Fill use the same geometry ownership in
both views. Ordinary 2D brush strokes paint directly in normalized UV space and do not use this
projection mask.

This automatic clipping is separate from artist-created masks.

--------------------------------------------------------------------------------

## Selections and Reusable Regions

Open **Properties > Selection** to constrain where new painting is allowed without changing existing pixels.

1. Choose **Rectangle** or **Lasso** and draw in the UV canvas or Scene view. Choose **Material** or **UV Island** to pick the corresponding geometry under the cursor.
2. Choose **Combine**: Replace, Add, Subtract, or Intersect.
3. In the Scene view, leave **Select Through** off for visible surfaces only; enable it when the region should reach hidden geometry too.
4. Click **Return to Painting**, press Escape in the viewport, or select a brush tool. The selection remains active until cleared.

A Scene-view reminder identifies active selection constraints. **Clear All Selections** clears every tile, including when the currently displayed tile is unrestricted. A click without a drag leaves an existing rectangle/lasso selection intact. With no previous selection, Subtract cuts from unrestricted coverage; Add and Intersect start with the new region.

Selections constrain brush tools, geometry fills, and path/ribbon painting, including mask painting. They do not continuously clip existing Fill or Projection layers. Use **Selection to Layer Mask** to turn the selected region into coverage for existing layer content. This replaces the selected layer's mask pixels and resets its procedural mask effects on the current material/tile; Undo restores the previous mask.

**Grow / Shrink (px)** ranges from -64 to 64 and **Feather (px)** from 0 to 64. Click **Apply Grow / Feather** to apply them. Invert and clear controls are also available. **Save Region** stores a named region for the current material/tile; click its name to restore it. Active and saved regions persist in documents and recovery. A multi-tile Scene selection is one Undo step.

Scene rectangle/lasso shapes use a 512-pixel screen-space raster and 1024-pixel visibility sampling before projection to each target's native UV resolution. Inspect fine edges in the UV view when precise texel boundaries matter. Material selection identifies the reconstructed material surface or UDIM member. Changed UV layouts invalidate UV-space regions. If stored selection data is damaged, painting is blocked with a diagnostic until that selection is cleared or replaced.

--------------------------------------------------------------------------------

## Layer Symmetry

Each layer owns its symmetry settings. Select a layer and use **Properties > Symmetry** to choose X/Y/Z mirror planes and radial copies. Move or rotate its frame numerically, or enable **Edit Origin in Scene**. A new layer starts with symmetry off; changing one layer does not change another layer's settings.

The Scene view X mirror button directly controls the selected layer's **Mirror X** option. Turning it on also enables that layer's symmetry. These settings support Undo/Redo and save with the layer. On paths, Auto Update rebuilds the result after changes; otherwise use Apply. Older documents retain their effective symmetry when migrated, and hidden brush settings cannot re-enable an axis you turned off.

UV symmetry uses the texture center plus the frame's XY offset, its Z rotation, and radial rotation around the UV plane normal. Reflections preserve image orientation. On paint layers, symmetry affects new strokes; changing it does not rewrite existing painted pixels.

For a placed Projection layer, **Create Symmetry Instances** creates separate linked layers at the symmetric placements in one Undo step. The operation leaves the stack unchanged if any placement cannot be generated. Each copy remains one editable world-space projection, including its wrapped control points and pins.

Later edits to the symmetry frame affect new placements. Existing projection instances retain their own transforms. Change the source projection to update shared imagery and material settings, or use **Make Independent** to break that link.

--------------------------------------------------------------------------------

## Surface Paths

Paths are editable curves authored either directly on the texture plane or on the reconstructed
surface.

### Create a Path layer

1. Click **+ Path** or **Create Spline Layer**.
2. Select the Path tool.
3. In Properties, choose **Spline Space: 2D Texture** or **3D Surface**.
4. `Shift+Click` the matching UV canvas or Scene view to append points.
5. Adjust points and controls.
6. Choose the path mode, configure the source on each Layer Channel, then choose the Paint / Preview Channel, brush, and projection settings.
7. Leave **Auto Update** on for live regeneration, or use **Update** on the Path toolbar when ready.

### Insert and edit points

- `Shift+Click`: append a point.
- `Ctrl+Click`, or `Command+Click` on macOS, within 8 screen pixels of the visible curve: insert a point into the nearest segment.
- Click or drag without those modifiers: select, move, or adjust an existing point or control.

Clicking too far from the visible curve does not insert into it.

### Editing modes and regeneration

The Scene view shows the **Overlay Painter Path** toolbar while an enabled Path layer is active. **Standard** permits anchor movement, curve handles, and width handles. **Move** exposes only anchor selection/movement; **Adjust** locks the anchors and exposes curve/width controls. These modes constrain viewport gestures, not toolbar actions such as Closed Path, Reverse, or point commands.

**Auto Update** is enabled for new and legacy paths. Turn it off to edit several points without rerasterizing, then click **Update** to rebuild every affected channel and path effect. The edit mode and Auto Update choice are stored per layer. Hiding a Path layer hides its raster output and authoring overlays; re-enable it to edit.

### 3D and 2D path domains

Set **Spline Space** in the selected Path layer's Properties to either **2D Texture** or **3D Surface**.
A path has one authoritative editing and raster domain; adding or editing from the other view does
not silently convert it. New paths default to 3D Surface.

A 3D Surface path is authored in the Scene view, follows the reconstructed surface, and resolves UVs
when rasterized. This allows continuity across UV seams and UDIM members. Its surface corridor
prefers connected polygon strips, helping a long segment remain on a strap or belt instead of
jumping to nearby disconnected geometry.

A 2D Texture path is authored and rasterized directly in normalized UV space. This avoids model
overlap ambiguity and keeps its pixels, curve, and effects entirely texture-domain. Selecting one of
its points in the 2D canvas also exposes that point's complete adjustment setup in the Scene view as
a positioning aid; Overlay Painter does not draw the complete 2D spline in 3D or convert it to a 3D
path.

Choose the authoring domain based on intent:

- Use 3D for seams, straps, scars, and lines that should follow the object across UV boundaries.
- Use 2D for artwork whose exact texture-space route is authoritative.

For a selected point in either editing view:

- The orange anchor handle moves the point.
- Green incoming and outgoing handles edit the Bezier curve.
- The blue perpendicular handle changes that point's width percentage.
- Right-click the point for **Straight Handles**, **Delete Point**, and width presets.

After moving an anchor or adjusting a green or blue handle, the point remains selected so several
changes can be made without reselecting it. Deleting a selected point selects a surviving neighbor;
deleting the final point clears the selection safely.

### Path modes

- **Stamps**: places discrete brush stamps along the curve.
- **Continuous**: creates a gap-free brush stroke.
- **Ribbon**: fits complete source-image tiles edge-to-edge along a variable-width strip.
- **Filled**: fills the path-defined shape.

### Clothing detail generators

Open the arrow beside **+ Projection** or **+ Path**, then choose **Garment > family > preset**.
Projection is useful for a pocket, knee folds, a patch, or an individual fastener. Path is useful for
a zipper, waistband, row of fasteners, or a curved strip of wear. Existing Path and Projection layers
can enable **Garment Generator > Generate Garment Detail** in Properties, then choose a grouped
**Construction Preset**. Paths offer 48 presets: the 32 constructions below plus the 16
**Hems & Seams** constructions. Projections offer the 32 constructions below. Only the selected
construction's controls are shown. These are native procedural sources on those layers; they do
not require a Plugin layer or an input image.

| Family | Presets | Main controls |
|---|---|---|
| Wrinkles & Tension Folds | Tension Folds, Compression Folds, Elbow Knee Folds, Cuff Gather, Pleats | Tension origin, fan spread, taper, spacing, relief, rotation, irregularity, seed |
| Denim Wash & Garment Wear | Thigh Fade, Hip Whiskers, Knee Honeycombs, Pocket Wear, Dirty Cuffs | Raised wear, recess darkening, dirt, area fade, optional live fold/protection inputs |
| Pockets & Garment Panels | Patch Pocket, Welt Pocket, Pocket Flap, Waistband, Reinforcement Panel | Opening, raised profile, edge wear, stitching, optional replacement cloth color |
| Zippers, Closures & Hardware | Metal Zipper, Coil Zipper, Buttonhole, Sewn Button, Snap, Rivet, Eyelet | Tooth pitch, tape/metal color, slider position, separation, fastener count, thread and roughness |
| Distressing & Repairs | Abrasion, Exposed Threads, Frayed Tear, Darned Repair, Repair Patch | Damage, exposed yarn spacing, fraying, repair colors, stitches and relief |
| Labels, Patches & Prints | Woven Label, Leather Patch, Printed Logo, Rubber Badge, Embroidered Patch | Logo texture/sprite, size, ink colors, cracking, raised/recessed detail, edge stitching |

All generated channels share placement, deformation, coverage, and seed. Projection details use the
existing click/Move placement, independent X/Y scaling, rotation ring, depth, wrapping, warp grids,
and all-channel X/Y flips. Path details use a continuous Ribbon source with the existing control points,
width handles, side/end fade curves, Auto Update, geometry clipping, and UV/UDIM destinations. The
image tile-mirroring and image join-crossfade controls do not apply to procedural garment sources.
Selecting a construction activates that generator and disables the other path generator. Existing
hem/seam documents open with their construction selected. Switching preserves path placement,
width, fades, and assigned image sources, and supports Undo/Redo.

**Ribbon Width** defines a path's working strip, with a slider and an exact numeric field for every
construction, including hems and seams; projection Width/Height define the generated rectangle.
Feature Spacing, Relief Height, Thread Width, and Stitch Spacing use world units on a 3D surface and
UV units on a 2D path. On a meter-scale model, 0.0035 is 3.5 mm. Stitch inset, opening, fade, tension
origin, and logo size are relative to the generated area. Adjust the placement to fit the garment
before tuning small details. **Amount = 0** clears the contribution. **Seed** is repeatable.

**Wear & Material Response** adjusts grayscale darkening/lightening and material finish. Keep the
layer blend mode **Normal** to preserve the underlying fabric through grayscale shading. For pockets,
**Replace Cloth Color = 0** retains the fabric under the panel; increasing it adds the chosen color.
Color details such as thread, hardware, and label backing use their own colors. **Stitching** supports
up to three inset rows on applicable constructions; the **Hems & Seams** family provides the
more extensive eight-row stitch and seam system.

**3D Depth** (0–4) scales physical relief: 0 is flat, 1 is the normal setting, and higher values
increase depth without changing the construction's width or spacing. **Relief Shading** (0–3)
independently scales relief shading in the generated material; 0 removes that shading and 1 is
the normal setting. These controls are independent for hems, seams, and non-zipper constructions.
Zippers retain their combined depth-and-tone response to **3D Depth**, with **Relief Shading** as
an additional shading multiplier. Wear-only presets and Printed Logo show shading without a depth control.
Feature-specific Contact Shading, Recess Darkening, and relief controls remain available for finer
adjustments. Cloth Texture, Quilt/Embroidery/Perforation/Atlas Scatter, and Fabric Fuzz Plugin layers
also provide independent **3D Depth** and **Relief Shading** controls.

Stitches have rounded thread relief, seated ends, and localized contact shading in Albedo and AO.
**Recess Darkening** also shades opaque generated tape, thread, and hardware. Zipper teeth interlock
below the slider and separate above it according to Opening / Separation; zero separation keeps
the chain closed. Slider and pull dimensions follow the strip width, so extending a path or
projection does not stretch the hardware. Pulls include an attachment bridge and an open cutout.
Buttons, snaps, rivets, and eyelets use distinct raised and recessed profiles. Fine yarn and surface
grain fade as they become too small to resolve; inspect details at the intended output resolution.

**Generated Channels** selects Albedo, Normal, Ambient Occlusion, Roughness, Metallic,
Height (Normal Control), and Masks (Custom RGB). Only channels supported by the target are generated;
unavailable outputs are listed in Properties. Relief presets default to **Height (Normal Control)**
on and **Normal (Direct)** off. Height is neutral at 0.5; the final normal pass merges its derived
relief with the existing fabric normal using reoriented normal mapping (RNM). Wear-only presets
leave both relief outputs off. Tune the height output with **Layer Channels & Settings > Normal
Control > Height Strength**; **Normal Strength** applies only to optional direct Normal output.
Direct Normal output currently uses ordinary layer blending, so it can replace existing normal
detail. Enabling both outputs applies the relief twice. Height and direct-normal strength settings
use different scales; switching an existing layer to height may require retuning Height Strength.
Choosing a built-in Construction Preset applies these recommended relief outputs. Existing saved
layers and custom preset assets retain their explicit output choices when loaded. Evaluated fold
references also include the source's Height Strength; keep it nonzero when using that height to drive wear. Custom RGB stores **R = wear, G = protection, B = recess/AO**, suitable for
composable mask references and the wear generator below. Output channels still have the ordinary
layer enabled/opacity/blend controls. Turning an output off removes its old generated contribution.

#### Wear that follows folds and seams

On a **Projection** wear generator, open **Live Fold & Seam Inputs**:

1. Enable **Height (Normal Control)** on the wrinkle source layer. Select that layer as **Fold Height**,
   choose its Normal Control channel and **Read Red**. The wear generator interprets 0.5 as neutral,
   higher values as raised cloth, and lower values as recessed cloth.
2. Enable **Masks (Custom RGB)** on a Hem/Seam or garment source. Select it as **Seam Protection**,
   choose Custom and **Read Green**. A painted or procedural mask can also provide protection.
3. Tune Raised / Edge Wear and Seam Protection. Raised folds receive more wear; protected seams and
   valleys retain more of the underlying color. Recess Darkening and Dirt add darker variation.

Source edits regenerate the wear automatically. Source channel effects, masks, visibility and opacity
are evaluated before a channel input is read; **Read Mask** reads the source mask itself. Group sources
use the isolated group output. Logical UDIM links resolve to the matching local member; an explicit
cross-target source uses that source's UV image. Missing channels, missing layers, and circular inputs
report an error and retain cached output. Internal links follow group duplication and material presets.
Live input controls are currently provided on Projection wear layers. Path wear uses its own procedural
pattern; use a Projection wear layer over a path seam when it needs those live inputs.

#### Logos and reusable garment presets

For Labels, assign **Logo / Motif** or **Logo Sprite**. The sprite takes precedence and retains its
cropping and alpha. Use Image Colors preserves the artwork's colors; otherwise Ink Color supplies
the color. With no image, a diamond emblem previews the material. Prepared lettering can come from
the existing Text generator. Embroidered Patch adds directional thread ridges; the existing
**Quilt, Embroidery, Perforation & Atlas Scatter** Plugin remains available for whole-surface textiles.

For the 32 constructions shared by paths and projections, **Save Preset...** creates a Garment
Generator Preset asset; **Saved Preset > Load Preset** reuses it on another path or projection.
These presets retain construction settings and logo asset references;
document-specific fold/seam links are deliberately omitted. Use a Material Preset containing the
source layers as well when the live dependency graph should travel together. Documents and recovery
retain the complete generated definition and cached channels. Edits support Undo/Redo, duplication,
layer masks and effects, ordinary exports, and **Rasterize to Paint Layer**.

These generators create texture shading, relief, and silhouettes within their placement. They do not
change garment geometry, make a physical pocket, cut a mesh hole, simulate draping, or add loose-thread
meshes. Use geometry for changes to the outer silhouette. Fine thread and tooth spacing also needs
enough texture pixels to resolve it.

### Hem / Seam generator

Use **+ Path > Garment > Hems & Seams** and choose a construction preset. For an existing Path layer,
open **Path Properties > Garment Generator**, select a **Hems & Seams** entry in
**Construction Preset**, and turn on **Generate Garment Detail**.
Place and edit the path using the usual 3D surface or 2D texture path controls. The generator
uses Ribbon mode and follows variable point widths, curves, surface projection, geometry
selection, symmetry, and UV/UDIM destinations. It requires no source image.

New preset paths start at a full width of 0.025 world units (2.5 cm on a meter-scale garment), with full opacity and no inherited endpoint fades. Existing paths retain their dimensions and fades when generation is enabled.

**Ribbon Width**, available as a slider and an exact numeric field, is the complete ribbon width,
including its shading margin: world units for
a 3D path, normalized UV units for a 2D path. Fold widths, stitch positions, thread thickness,
and stitch spacing are percentages of this width. Point Width remains a multiplier on the
ribbon width. **Construction Offset** moves the folds and rows together inside the ribbon;
**Mirror Across Path** reverses the whole construction, including the normal slope.

| Preset | Starting construction |
|---|---|
| Denim Chainstitch Hem | Folded hem, diagonal roping, faded raised cloth, protected recesses, one exterior needle row |
| Double Turn Hem | Folded band and an offset lockstitch row, with lighter wear and bunching |
| Flat Felled | Raised folded band with two parallel topstitch rows |
| Mock Felled | Shallower lapped fold with two offset rows |
| Lapped | Overlapping fold and one topstitch row |
| Plain Pressed Open | Central seam recess with the allowances suggested on either side |
| French Seam | Narrow enclosed ridge, without exposed topstitching |
| Bound Edge | Folded edge band with two edge rows |
| Piping | Rounded raised cord profile |
| Overlocked Edge | Lapped edge with repeating edge loops and a needle line |
| Coverstitch Hem | Folded hem with two parallel exterior needle rows |
| Rolled Hem | Narrow rounded fold with a fine stitch row |
| Blind Hem | Folded hem with small visible stitch catches |
| Raw Frayed Hem | Raw edge with irregular pale fiber detail |
| Topstitch | A stitch row without a broad cloth fold |
| Bar Tack | Dense crosswise stitches; use a short path for pocket/corner reinforcement |

Presets are editable starting points, not an exhaustive sewing-standard catalog or a cloth
simulation. Changing the preset replaces the construction controls and rows, while retaining
which output channels are selected. A construction can have up to eight independently
colored stitch rows. Set **Cloth Profile** to None for thread-only work; remove all rows for
cloth-only folds or seams. Add a row on each side of the center for double/triple topstitching,
with separate colors or phases if desired.

**Roping & Cloth Bunching** controls the amplitude, ridges per width, diagonal slant,
irregularity, and deterministic cloth seed. **Stitch Puckering** adds smaller bunching at
needle intervals. **Edge Fraying** adds pale irregular fibers inside the strip. Closed paths
fit whole roping and stitch cycles around the loop; variation does not restart at a UV tile
or at a channel boundary.

**Wear, Recesses & Finish** separates grayscale cloth aging from colored thread:

- **Recess Darkening** darkens fold recesses, individual stitch contacts, and needle holes. Folded
  and lapped profiles shade the exposed cloth beneath the overlap; Mirror Across Path reverses
  that side together with the construction. Piping retains contact shading on both sides.
- **Raised Cloth Wear** lightens exposed ridges and fold edges.
- **Protected Seam / Newness** suppresses this wear near stitches and in recesses, retaining
  a darker, less worn appearance. It does not restore fabric detail already removed from
  the material underneath.
- **Ambient Occlusion** controls the separate AO output.
- **Cloth Roughness / Thread Roughness** specify the finish of those regions.
- **Relief / Width** supplies the height field. The default height output uses **Layer Channels & Settings > Normal Control > Height Strength**. **Normal Strength** is shown only for optional direct Normal output.
- **3D Depth** scales the cloth and thread height together; **Relief Shading** independently scales fold and stitch contact shading. Both default to 1, and neither changes ribbon width or stitch spacing.

Each stitch row has an enabled toggle, **Thread Color** (including alpha), position, thread
width, stitch spacing, length, phase, and relief. Patterns are **Lockstitch**, **Chainstitch**,
**Zigzag**, **Overlock**, **Cover Looper**, **Blind**, and **Bar Tack**. Loop/crosswise patterns
also expose a span. Later rows cross over earlier rows. For coverstitch undersides, add a
Cover Looper row below the exterior needle rows. A chainstitched jean hem normally shows a
straight needle line on its outside; select Chainstitch to depict its looped underside.

**Generated Channels** selects the outputs:

| Output | Meaning |
|---|---|
| Albedo | Grayscale darkening/wear over the underlying fabric, plus colored thread |
| Normal (Direct) | Optional normal-map output in the destination tangent frame; ordinary layer blending can replace existing normal detail |
| Ambient Occlusion | Darkening concentrated in recesses and stitch holes |
| Roughness | Cloth and thread roughness inside the generated coverage |
| Height (Normal Control) | Default relief output around neutral 0.5; merges folds, roping, thread and needle holes with the fabric normal through RNM |
| Masks (Custom RGB) | R: wear; G: protected seam/newness; B: recess mask, for channel references in mask stacks |

Only channels supported by the target receive output; the inspector lists unavailable ones.
Albedo, Height (Normal Control), AO, and Roughness are selected initially. Normal (Direct) is
off. Choosing a built-in seam preset restores these relief-output defaults; loading existing
saved layers retains their choices. Keep only one relief output enabled to avoid doubling bump shading. The channel section retains enable, opacity,
blend, and preview controls; generator outputs are chosen in Path properties instead of
assigning source images. Deselecting all outputs clears the generated path.

Use **Normal** layer/channel blending for the intended cloth shading. Albedo is encoded as
transparent grayscale adjustments and colored thread, so a blue denim weave remains blue
denim beneath the seam. Neutral areas stay transparent; no snapshot of the underlying
material is baked into the layer. The separate roughness output sets the local finish rather
than preserving every underlying roughness texel; turn it off when that detail should win.

Existing Side/Start/End fades and curves still apply. Image mirroring, endpoint images, and
image-tile crossfades are hidden while generation is enabled because this is a continuous
procedural construction; use Mirror Across Path for its orientation. Assigned image sources
remain saved for when generation is disabled. All settings and stitch rows participate in
Auto Update, manual Update, Undo/Redo, duplication, document save/load, and linked path
synchronization. Legacy paths keep their image rendering until generation is explicitly enabled.

The effect changes textures and normals. It does not displace the garment silhouette, cut a
raw edge into geometry, simulate sewing tension, or grow fibers outside the mesh. Fine
thread needs enough texture resolution; inspect it at the final export resolution.

Reference construction and appearance were reviewed against [Coats' seam classification](https://cdn.coats.com/wp-content/uploads/Seam-Types.pdf),
[the Sewing & Craft Alliance flat-fell guide](https://www.sewing.org/files/guidelines/11_330_flat_fell_seams.pdf),
and [Raleigh Denim's roping reference](https://raleighdenim.com/pages/union-special).
The distinction between seam construction, exterior needle lines, underside loops, and
raised/recessed denim wear guides the independent controls above.

### Path fading and curves

Select the Path layer and open **Path Properties > Fading**:

| Control | Range and meaning |
|---|---|
| Side Fade (%) in Ribbon mode | 0-200; 0 gives hard sides, 100 reaches the centerline from each edge, 200 spans the full width and fades the center too |
| Start Fade (%) | 0-200% of the complete path length, measured inward from the start |
| End Fade (%) | 0-200% of the complete path length, measured inward from the end |
| Side Curve | Opacity profile across the ribbon's side fade |
| Start Curve / End Curve | Independent opacity profiles for the two ends |

Zero disables the corresponding fade. Start/end fade lengths apply to the whole path, never each repeated image tile. Values above 100 fade across the entire path and affect the opposite end too. Closed paths disable endpoint fades.

For each fade curve, the left end is the interior and the right end is the side edge or path endpoint. Height is opacity: 0 transparent, 1 opaque. Lower the curve to remove more backing or reduce opacity across the fade. **Reset** restores the default smooth profile. This lets the side fade reach all the way through a ribbon rather than leaving an unavoidable opaque center strip.

If the layer has an enabled **Edge Fade** effect, Path properties edit that effect's **Side Fade**, **Fade Ramp**, and **Side Curve**. Otherwise, saved brush hardness supplies the first 100% of side fade and additional ribbon fade extends it to 200%; legacy paths retain their saved appearance. Non-ribbon paths keep Side Fade as 0-100% brush softness and also support the start/end curves.

For a zipper ribbon, raise Side Fade to 100 to feather the fabric backing toward the center. Increase it toward 200 or lower Side Curve if backing is still too visible. Tune Start and End independently to keep the zipper teeth while blending the endpoints. Source alpha and these fade controls are retained after moving points, manual Update, Undo/Redo, and save/reopen.

Ribbon mode builds a continuous strip and repeats complete source images without internal stamp edges. Brush Size sets the nominal tile width and length; point width and curve shape deform the strip. Optional Beginning and End sources replace the first and last complete tiles with the same source orientation. Use endpoint fades when those complete tiles also need a soft transition.

### Alternating and seeded texture mirroring

Under Path properties, **Texture Mirroring (All Channels)** provides independent **Flip X (Left/Right)** and **Flip Y (Up/Down)** choices:

| Choice | Result |
|---|---|
| Off | Keep the original orientation |
| Every Tile | Mirror every ribbon tile or path stamp |
| Alternate | Keep the first unchanged, flip the second, and repeat |
| Random | Choose whether to mirror each tile/stamp with a 50% chance |

When either axis is Random, **Flip Seed** controls the repeatable pattern. X and Y use independent choices; combine Alternate X with Random Y if desired. The same seed and tile/stamp index produce the same result after regeneration, reload, and Undo/Redo. Changing path length or brush size/spacing can change the number and positions of repeats. The pattern follows path order and continues across triangles and UDIMs; it does not restart for each channel or texture tile.

X/Y refer to the source image's axes, so they follow its orientation along the path. All texture channels use the same flip decisions, with normal-map vector directions corrected. Source alpha flips with its image. Fades, ribbon geometry, and masks stay in their existing coordinates. Source assets remain unchanged.

Ribbon mode flips complete image tiles, including optional Beginning and End tiles, in both 2D and 3D. Stamps and Continuous modes flip each Texture/Sprite stamp. In these stamped modes, Overlay sources retain their destination UV mapping. Filled mode applies the sequence to its boundary stamps; the interior has no path sequence. Closed ribbons repeat from the first tile; an odd tile count may produce a matching pair at the closing seam when Alternate is selected.

### Crossfading ribbon joins

For Ribbon paths, open **Path Properties > Ribbon Tile Joins** and enable **Crossfade Joins**. **Join Overlap (%)** sets the transition width from 0 to 100% of the fitted tile spacing. It starts at 20% when enabled; 0 gives the original hard join. For example, 20% places half the blend zone on each side of the join.

The outgoing image fades out while the incoming image fades in with the inverse smooth fade. Their weights add up to one: opaque images stay opaque through the join. Transparent source pixels blend without leaking their hidden colors, and normal vectors are blended and normalized. The same transition applies to every channel, including alternating or seeded flips; each image keeps its own flip orientation through the overlap.

Images extend into their neighbors to create the overlap. Tile count, repeat spacing, ribbon geometry, and the ends of the path stay in place. Beginning and End images crossfade with their neighbors on open paths; a single open tile has no join. Closed ribbons also blend the last tile into the first, including a one-tile loop. Side Fade and the whole-path Start/End Fade controls still apply separately.

Crossfade Joins defaults off to preserve existing artwork. The toggle and overlap amount save with the Path layer, support Undo/Redo, and follow Auto Update. This option is available in Ribbon mode in both 2D and 3D; ordinary stamp overlap still uses brush spacing and softness.

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

Tangent modes include Corner, Smooth, Broken, Custom, and straight/linear handles. Paths also support
insert, delete, multi-select, copy, paste, reverse, mirroring, and radial symmetry. The point width
value and blue handle both edit the same percentage, from a narrow local section to a widened one.

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
- Splatter, Splatter Distance, and Random Strength.
- Fade, Taper, and their shared world-space length.
- Search tags.

Use clear names and comma-separated tags such as `skin, pores, subtle` or `cloth, stitch, trim`.

Selecting a preset copies its paint settings into the current editable session brush. Adjusting brush controls therefore does not silently change the shared asset. Use **Update Brush Asset with Current Settings...** to write the current shape, stamp source, size, hardness, flow, spacing, rotation, blend, mirror, follow, randomization, Splatter Distance, Random Strength, fade, taper, and evolution length settings back to the selected preset. Overlay Painter shows a confirmation warning before the asset is changed; shelf tags remain unchanged.

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

The same settings panel controls the startup workspace and background-save timing:

- **Overlay Painter Compact View** defaults on. It opens one floating workspace with Layers/Brush
  tabs on the left and Scene/2D tabs on the right. Disable it to open the three Overlay Painter
  panels independently and use the existing Scene view.

- **Enable Automatic Recovery** defaults on. It controls periodic temporary-session recovery and
  autosave of modified permanent `TexturePaintDocument` assets. Turning it off does not remove
  manual Save, Save As, or the unsaved-changes prompt when closing.
- **Recovery Idle Delay (seconds)** defaults to **120**. The countdown restarts after each edit, so
  a save does not begin in the middle of a continuous painting session.
- **Minimum Save Interval (seconds)** defaults to **300**. Even when the document repeatedly becomes
  idle, no new background save begins until this interval has elapsed since the previous save.

The later of the idle deadline and minimum-interval deadline wins. Capture uses asynchronous GPU
readback and background compression, but Unity must synchronously import and commit changed recovery
assets at the end; that final step can briefly pause the editor. Increase the idle delay or minimum
interval if large, multi-channel documents make those commits disruptive. The workspace status line
shows **Updating recovery asset**, **Writing recovery asset**, or the corresponding project-document
message while persistence is active.

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

If you explicitly double-click or otherwise open a saved `TexturePaintDocument` while compatible
recovery exists, the choices change to **Open `<document>`**, **Recover Instead**, and **Cancel
Opening**. Opening the requested document is the default and discards the older recovery. Recovery
is loaded only when **Recover Instead** is chosen, so an empty or older recovery snapshot cannot
silently replace the saved document you asked to open.

### Save As

Use **Save As** to create the first permanent `TexturePaintDocument` below `Assets`.

The document stores:

- Editable base pixels.
- Layer pixels and metadata.
- Per-channel source type, texture or `Sprite` reference, overlay reference, color, inversion, Normal convention, and Fill X/Y tiling, X/Y offset, rotation, and shared-transform state.
- Per-channel Enabled, Lock Painting, Channel Paint Strength, Channel Opacity, Channel Blend, and
  Normal Control Height Strength settings.
- Editable mask pixels, black/white base value, grayscale Mask Value, ordered mask effects, smart-mask settings, and cached layer-reference inputs.
- Ordered layer effects, target channels, amounts or levels, curves, textures, and transforms,
  including channel-specific Image Adjustments.
- Paths, point dynamics, side/start/end fade distances and curves, and path editor/Auto Update settings.
- Per-tile Fill source overrides and No contribution choices.
- Projection per-channel sources, placement, size, depth, fades, mode, wrapped grid/pins, and cylindrical settings.
- Named anchors, live content/mask references, linked instances, and their cached output.
- Active selections, named regions, and each layer's symmetry settings.
- Stroke records, including stencil and camera snapshots for stencil-painted strokes.
- Brush and source settings, including Splatter Distance and Random Strength for Paint and Path
  snapshots.
- Plugin provenance.
- Workspace state.
- Stable surface identities and source fingerprints.

Pixel data is stored in a sibling `<Document Name> Data` folder. Keep that folder with the document asset.

UMA regenerates material instance names with a random `_Genb_<number>` component. Overlay Painter
excludes that nonce from new surface identities. When opening an older document whose id included the
nonce, it conservatively rebinds each surface using slot ownership, UV/topology fingerprints, UMA
material identity, and its saved renderer/submesh location. An unchanged UV layout restores layer and
mask pixels exactly even if reconstruction reordered triangle indices; a changed UV layout follows the
document's rerasterization and mask-reset safeguards.

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

**Mark Addressable** is compiled and shown only when `UMA_ADDRESSABLES` is defined and the optional
Addressables integration is available. Overlay Painter's core assemblies contain no unconditional
Addressables references, so projects without Addressables continue to compile and export normally.

The `UMAMaterial` descriptor, not the template, controls physical channel order, component packing, PNG or EXR encoding, color space, normal convention, and importer settings.

Use **Save Overrides as Template** only when output-folder and policy choices should be reused. A template is not required for ordinary export.

### Export content

- **Flattened Composite** exports the reconstructed source plus visible base and layer edits. Normal
  Control is evaluated against the composed Normal channel before physical packing.
- **Runtime Overlay (Transparent)** excludes the reconstructed source and direct base edits. It
  exports visible authored layers and groups as a recipe-ready alpha-bearing overlay. Normal Control
  is converted to a flat-relative normal contribution, and its gradient footprint participates in
  the generated coverage mask.

For a generated-character session, the reconstructed source comes from the character's original
slot and overlay texture inputs at their native working resolution. Export never reads the UMA
generated atlas as source imagery; this avoids baking atlas resizing, compression, or runtime merge
artifacts back into the new textures.

`UseExistingTextures` UMA materials are the non-composited exception. Because they have no native
overlay composite to reconstruct, Overlay Painter reads each declared channel from the material
currently assigned to the generated character and identifies it as **UMAMaterial Source Textures
(Read Only)**. The affected target and texture-set rows receive an import warning icon. These textures are used only as the base
for preview and flattened export; painting does not modify the material's source texture assets.
When the UMA material uses a second render pass, Overlay Painter imports only the first-pass slot
geometry and attaches an informational import record to the affected target indicating that the
duplicate second-pass submesh was skipped.

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

Normal export reconstructs ordinary tangent-space RGB from Overlay Painter's logical vector data,
then applies the material descriptor's OpenGL or DirectX convention at the physical output boundary.
It does not copy Unity's imported or platform-packed normal-map bytes into the exported image.

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

Use Path layers for long seams, trim, piping, stitches, and repeated details. Tune their side and endpoint fade curves at the intended working resolution. Use Projection layers for individual details, Wrapped/Edit Warp for local surface fitting, and Cylindrical for limb wraps. Keep these layers editable until the final look is approved.

### 7. Mask instead of erasing repeatedly

A layer mask preserves the original material pixels and makes edge revision easier. Combine generator, texture, adjustment, and filter entries in **Mask Effects & Smart Masks**; keep hand-painted corrections as a final Painted Mask input. Use **Fill Polygon**, **Fill UV Island**, or **Selection to Layer Mask** when geometry defines the boundary. Save useful effect stacks as Smart Mask recipes and related layer groups as Material Presets.

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

### A projection is invisible or has missing polygons

- Expand **Layer Channels & Settings** and verify the intended channel has a source, is Enabled, and has nonzero Channel Opacity. Inspect Albedo alpha because it supplies common coverage.
- Turn off **Edit Warp** and click the target surface to place and align the projection. Check the cyan footprint and both depth limits.
- Increase **Depth (Z)** if the surface protrudes beyond the volume. For Wrapped mode, use **Fit to Surface / Refit** and inspect the fitted/retained point count.
- Check Angle Fade, layer/group masks, and any live mask inputs that could remove coverage.
- For thin gaps on an otherwise visible surface, inspect normals and try a higher Visibility Quality or suitable Surface Tolerance. Enable Project Through only when hidden polygons should also receive the image; disable Connected Surface Only if those polygons are a separate mesh piece.
- Use **Regenerate Projection** to rebuild the current definition. Overlapping UVs still share pixels and cannot hold independent projected images.

### Projection placement or sizing is not responding

- Turn **Edit Warp** off to restore whole-projector controls; while it is on, drag the patch's surface points instead.
- Turn **Edit Stencil in 3D View** off if stencil editing is capturing the drag.
- Drag X or Y for independent plane-axis sizing. **Lock Numeric Aspect** applies only to numeric size fields.
- Trace around the rotation ring; a radial drag toward its center does not change the angle.

### Ribbon backing remains visible or fades seem unchanged

- Open **Path Properties > Fading**. Side Fade can exceed 100; try up to 200 and shape **Side Curve** to reduce the remaining backing.
- If an enabled Edge Fade effect exists, those controls edit that effect. Check **Fade Ramp (%)** as well as distance.
- Start/End Fade spans the full path length. Closed paths intentionally have no endpoint fades.
- With **Auto Update** off, click **Update** in the Path toolbar after editing.
- Fade curves store opacity: high values retain coverage, low values remove it. Source alpha still participates.

### Painting does not change a procedural mask

- Painting edits the original mask raster. A later Replace source may replace that input in the effective result.
- Add **Painted Mask** with Multiply after the procedural entries, and use a white original mask for hand-painted black exclusions.
- Use **Solo Mask** and disable effects one at a time to find the entry controlling coverage.
- Check Live Mask Source and Layer Reference diagnostics if the mask depends on another layer.

### Painting is unexpectedly restricted

- Look for the active-selection reminder. **Return to Painting** leaves the selection active; use **Properties > Selection > Clear All Selections** to remove all tile constraints.
- Check whether **Enable Stencil** is on. Preview Opacity only hides the guide; disabling the stencil removes its coverage restriction.
- If the stencil appears but no paint is deposited, exit **Edit Stencil in 3D View** before painting.

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

- Start look development at an appropriate working resolution; doubling both dimensions quadruples the texel count.
- Reduce very large effect widths or mask-filter radii.
- Hide unneeded effects while painting.
- Check the **Performance & Memory** panel for fallback counts and latency.

Projection manipulation reuses geometry and channel targets and uses GPU visibility where supported. If it is still slow, inspect the working resolution, active channel count, Visibility Quality, and downstream effects/references. A large spatial mask stack may need broader recomposition than the directly edited pixels. Performance depends on the target and hardware; it is not a fixed frame-rate guarantee.

### A long freehand stroke becomes progressively slower

- Enable **Performance & Memory > Stroke Diagnostics**, choose **Capture Next Stroke**, and repeat
  the stroke. Diagnostics are opt-in and add no per-stroke capture work while disabled.
- Freehand contacts from one raw input event are rasterized in bounded destination-tile batches.
  Normal and mirrored queries still test every selected slot/UDIM member; batching begins only after
  the contacts and their exact triangle ownership have been accepted.
- **History include calls** should remain close to **captured tiles**, not paint operations. Each
  target tile keeps the pixels from immediately before its first write for the complete stroke.
- Composite and preview-binding work should scale with dirty tiles per input event, not accepted
  contacts. Triangle-restricted stamps in a destination tile share a compute dispatch while retaining
  per-stamp UV triangle boundaries and original order.
- Compare early/middle/late input p95, compute dispatches, composed pixels, and managed allocations.
  Use the matching `OverlayPainter.Stroke.*` Unity Profiler markers and GPU Profiler when CPU
  submission is low but an input event still waits on the graphics queue.

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
| Append a Path point in its editing domain | `Shift+Click` |
| Insert into a Path segment within 8 screen pixels | `Ctrl/Cmd+Click` |
| Pin/unpin a Wrapped projection control in Edit Warp | `Shift+Click` on the point |
| Move / rotate / scale an editable 3D stencil | Drag / `Shift+Drag` around center / `Ctrl/Cmd+Drag` vertically |
| Leave Layer Mask mode or selection editing; cancel projection/stencil drag | `Esc`, according to the active editing mode |
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
- Layer names, groups, masks, paths, projections, and anchor names are understandable to another artist.
- Projection channels align, wrapped/cylindrical seams have been inspected, and fade curves produce the intended coverage.
- Mask-effect order and painted corrections have been reviewed with Solo Mask.
- References resolve without missing-source or cycle diagnostics; pending linked updates have completed.
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
