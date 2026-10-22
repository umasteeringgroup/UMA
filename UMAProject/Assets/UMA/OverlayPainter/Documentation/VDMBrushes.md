# VDM relief brushes

VDM Relief uses the signed normal component of a vector displacement map to author the Normal Control channel on Projection and Path layers. Geometry and silhouettes do not change. The original RGB vector texture and decode profile are retained for future geometry output.

## Import and use

1. Choose **Assets > Create > UMA > Overlay Painter > Import VDM EXR**. Select the EXR and save the brush asset. The importer creates a dedicated copy, using linear, uncompressed RGBAFloat data with no mipmaps. It does not modify the source file.
2. Create/select a **Projection** or **Path** layer and add its **Normal Control** channel. The target material must support Normal.
3. In **Layer Channels & Settings > Normal Control > Source**, select **VDM Relief**, then assign the VDM Brush.
4. Place the projection or edit the path. VDM paths support **Stamps** and **Ribbon**; Continuous and Filled modes preserve the existing output and report that a supported mode is required.
5. Adjust **Displacement Amplitude** and **Relief Gain**. Then use the existing **Height Strength**, **Invert Height**, and **Sample Radius** controls for the shading response. Inspect the result in the Normal channel or material preview.

The default profile interprets signed RGB as local U, V, N, with zero as neutral. The original **Sculpted Core VDM** example is in `Assets/UMA/OverlayPainterExamples/VDM`. It contains four staggered pairs of abdominal pads, oblique ridges, and a navel depression. It is generated entirely from analytic functions, without reading external images or meshes. The supplied copyrighted reference is not distributed or used as an example.

Assign **Sculpted Core VDM.asset**, leave Displacement Amplitude at 1, and try Relief Gain 4 with Height Strength 16 to start. Adjust strength to the placement size and texture resolution. The accompanying Height PNG encodes `0.5 + 4 * N`; the Preview PNG is a shaded illustration. Use the EXR brush asset for signed vector data. Regenerate an independent copy with **UMA > Overlay Painter > Examples > Create Procedural Abdominal VDM**. The generator lives in the separate Examples companion package.

Brush assets expose neutral offset, component scale, U/V/N axis mappings, and image Y flip in the Inspector for other explicit conventions. A layer snapshots the profile when assigning the brush. After changing a profile or reimporting its vector texture, use **Refresh Source Snapshot** on each affected layer to accept the revision. A missing or changed source does not silently become a different brush.

## Coverage and precision

- **Footprint:** the image rectangle is covered; existing projection/path edge fades supply softness. Zero displacement is valid covered data.
- **Source Alpha:** use EXR alpha. Many VDM files have alpha equal to one everywhere.
- **Mask Red:** use a separate linear mask's red channel. Assign a data texture with sRGB disabled.

VDM height uses its own coverage, independent of Albedo. Ribbon image endcaps are ignored for the VDM channel because ordinary images do not define a vector profile. Existing geometric caps and path fades still apply. Decoded height stays floating point around neutral 0.5; values outside 0..1 are preserved. The source is never converted through RGB luminance or normal-map unpacking.

Use Normal channel blending initially. Ordinary texture Add/Subtract modes keep their existing texture semantics; they are not physical vector composition modes. Height Strength and Relief Gain affect the simulated appearance and must not be interpreted as a world-space displacement distance.

## Saving and export

Documents, recovery snapshots, duplication, and source Undo retain the VDM reference, original vector texture, decode snapshot, amplitude, and existing placement/path data. Height rasters are caches. Normal export follows the existing flattened-normal and transparent-overlay workflows; there is no additional runtime VDM dependency for the exported normal texture.

Keep the editable source document/layers for Phase 2. Rasterizing, merging, or flattening removes procedural placement information; the resulting Paint layer cannot recover full vector displacement from height. Duplicate the editable layer before destructive raster operations.

## Scope

Phase 1 discards lateral displacement only for rendering the height approximation. It does not reconstruct overhangs, self-occlusion, or displaced silhouettes. No mesh, blendshape, Slot, or MeshModifier output is implemented yet. The preserved RGB source and explicit frame-independent decoder are the inputs for that later geometry backend.

Signed float render textures are required. VDM sources are deliberately unavailable on freehand Paint, Fill, layer masks, and generated garment/hem/path-generator outputs. Examples and tests belong in their separate companion packages; personal imported brushes belong in project data.
