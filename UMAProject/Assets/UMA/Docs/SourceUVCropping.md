# Source-UV cropping

Source-UV cropping saves generated atlas space when a slot uses only part of its overlay textures. It never crops source texture assets or rewrites source mesh UVs. It is separate from trimming unused space around the completed atlas.

## Enable it

1. On the UMA generator, expand **Atlas Settings** and enable **Enable Source UV Cropping**. It defaults off to preserve existing projects.
2. For custom rendering shaders, select the **UMAMaterial** and enable **Supports Source UV Cropping** only after confirming that **all passes use unchanged UV0** for the generated channels. This includes UMA shader graphs: they require explicit opt-in, rather than guessing from their names. Standard, Standard (Specular setup), and URP Lit, Simple Lit, and Unlit are recognized automatically.
3. Rebuild the character's textures **and mesh**. Existing generated characters are not automatically rebuilt when these settings change.

**Source UV Crop Padding** is measured in source texels, using the smallest contributing texture dimensions. The default is 8 and the minimum is 4. Bounds are rounded outward. Increase padding if filtering/mipmaps reveal seams. Padding is not a guarantee against bleeding at arbitrarily low mip levels or severe atlas downscaling; disable cropping for affected content if necessary.

## Shared overlays and slots

Each slot contributes prepared bounds from the triangle indices of its selected source submesh, including every stored LOD. Unreferenced vertices do not expand these bounds. A shared atlas rectangle uses the union of **all participating slots' bounds**, plus padding. Slots using opposite halves can therefore require the full texture. The same overlay asset can participate in different crops on different characters without changing that asset.

The entire overlay stack is composed in its original coordinate system, then clipped to the crop. Placed overlay rectangles, alpha masks, and normal/data channels keep the same source sampling. Generated mesh UVs and SlotData.UVArea use the corresponding offset and scale.

## Overrides and safe fallback

- **SlotDataAsset → Source UV Cropping → Disabled** keeps its whole shared rectangle uncropped.
- **OverlayDataAsset → Allow Source UV Cropping** can be unchecked to protect every region using that overlay.
- **UMAMaterial → Supports Source UV Cropping** is a sampling contract, not an override of the other safety checks. Do not enable it for UV animation, parallax, shader-driven displacement of UVs, non-UV0 sampling, or other arbitrary sampling outside the slot bounds.
- **Generator → Enable Source UV Cropping** switches the optimization off globally.

Any participant's veto disables cropping for the **entire shared rectangle**, not just that slot. Independent atlas regions remain eligible.

Automatic fallback retains full source textures for out-of-range/tiled or degenerate UV bounds, alternate slot UV sets, atlas-overlay slot remapping, mesh modifiers, transformed overlays, cutouts/advanced blend modes, unsupported channel types, custom atlas compositors, atlas post-processes, unsafe material texture scale/offset, non-color overlay shader properties, and texture/slot processing callbacks. AtlasUpdated listeners currently disable cropping for the material; empty events do not. Missing or invalid mesh preparation also falls back safely if it cannot be rebuilt.

The standard normal-map swizzle post-process is supported because it only rearranges channels within each pixel. Other atlas post-processes keep the uncropped path.

Code that changes shader sampling after generation must disable cropping before rebuilding. Arbitrary runtime shader behavior cannot be inferred automatically.

## Preparation, caching, and compatibility

UV bounds are stored in version 2 slot preparation metadata: one rectangle per source submesh, not a duplicate vertex stream. Old slots load normally and missing/stale metadata is rebuilt through the existing preparation/invalidation path. Saving the converted slot persists the metadata. No asset migration command is required.

Custom code editing mesh arrays must continue to call `meshData.InvalidateBuildData()` (or the existing resource-reuse invalidation API). Supported editor paths already use that invalidation mechanism. Crop bounds themselves are derived data and are not an editing surface.

The effective crop is included in generated mesh reuse keys and early atlas reuse keys; resolved texture commands include their clip bounds. Identical crops can share outputs. Different crops cannot accidentally reuse the same UV mapping or atlas content. Material color parameters can still differ independently where existing reuse rules allow it.

Savings depend on UV coverage, padding, packing, and the final atlas dimensions. A tighter packed region does not necessarily reduce the final power-of-two atlas allocation. Cropping has no effect on source texture import memory or disk size.
