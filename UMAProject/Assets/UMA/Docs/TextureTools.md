# Texture Tools

Texture Tools is the batch tool for many textures at once: it shows how UMA uses each texture, finds import settings that do not match that use, and changes import settings, sizes and file formats for the whole set. To edit the pixels of a texture, use [Texture Utilities](TextureUtilities.md) instead.

Open it by selecting textures in the Project window and right-clicking **UMA > Texture Tools**, or from **UMA > Textures > Texture Tools**. Textures and folders can also be dragged onto the window, or added with **Add Selection** (which includes textures inside selected folders).

## The texture list

Each row shows the texture's source size, texture type, sRGB or linear, maximum size, compression and file size, and on its third line the keywords it is used as. A red or yellow icon means a check found a problem; hover over it to read it. Click a row to show its details on the right; double-click to ping it in the Project window.

The tick box on each row decides which textures the tools change. **Every tool works on the ticked textures that the filter and search show** (the "targets"); the count is shown above the list. **All**, **None** and **Invert** change the ticks of the visible rows only. **Sort** orders the list by name, path, source size, file size or problems.

## Filter by overlay use

Texture Tools searches every `OverlayDataAsset` in the project for the listed textures. For each overlay that uses a texture, the position in the overlay's texture list is mapped to the channel at the same position in the overlay's UMA Material, and that channel's material property is the texture's **keyword** (for example `_BaseMap`, `_BumpMap` or `_MetallicGlossMap`). An overlay's alpha mask is listed as **Alpha Mask**. Textures that no overlay uses are listed as **(not used by any overlay)**.

A texture used by several overlays can have more than one keyword. That is allowed, and the texture is shown when any of its keywords is ticked in the filter. **Only** next to a keyword shows that keyword alone. **Rescan Overlays** searches again after overlays change. Only overlays that reference one of the listed textures are loaded.

## Details panel

For the focused texture: a preview (with an alpha view), the imported size, format, mipmap count and memory use, the source size and whether the source image has alpha, all import settings including platform overrides, every overlay that uses it (keyword, overlay, texture slot and UMA Material, with **Ping**), and the results of the checks, each with a **Fix** button when it can be fixed by an import setting. **Edit Pixels** opens the texture in Texture Utilities.

Below it, a summary of the targets counts their import settings (for example "sRGB: on ×12, off ×3") and their problems.

## Import Settings

Changes only the settings whose box is ticked; every other setting on each texture stays as it is. Textures whose ticked settings already match are not reimported. Available settings: Texture Type and Shape, sRGB, Alpha Source, Alpha Is Transparency, Read/Write, mipmaps (on/off, filter, streaming), Wrap Mode, Filter Mode, Aniso Level and Non-Power of 2.

Max Size, Resize Algorithm, Compression, Crunch and (for a platform) Format go to the chosen **Platform**. **Default** changes the default settings; any other platform is written as an override, and **Remove the override** turns that platform's override off. **Copy from Focused Texture** fills every value from the texture in the details panel and ticks the common settings, so one texture's settings can be copied to the rest.

**Apply and Reimport** writes the settings and reimports the changed textures as one batch. Source images are never modified.

## UMA Material Check

Compares each target's import settings with what its UMA Material channels expect, using the same channel layout as the UMA Material inspector:

- **Texture Type**: a channel that holds a normal map needs Normal map; any other channel must not be a Normal map, because Unity repacks normal-map channels.
- **sRGB**: color (albedo, emission) needs sRGB on. Data such as metallic, smoothness, occlusion, masks and detail maps needs sRGB off; with it on, the RGB values are read too low. This is the usual cause of "metallic does little and things look too shiny" after moving Standard-shader maps to URP. Alpha is never sRGB-converted.
- **Alpha Source**: a channel that keeps data in alpha (such as smoothness), or an alpha mask, needs alpha from the image. An overlay without an alpha mask blends with its first texture's alpha, so that alpha is kept too.
- **Alpha Is Transparency**: turned off where alpha holds data, because it changes RGB wherever alpha is near zero.
- Warnings that cannot be fixed by a setting: the source image has no alpha channel where alpha holds data, or the texture is used both as a normal map and as color, or as both color and linear data.

**Include suggestions** also lists information, such as Read/Write being on (a second copy of the texture in memory) or a source larger than its Max Size. **Fix N Problem(s)** applies every fixable result to the targets and reimports them. **Show Only Problem Textures** unticks the rest.

## Resize

Lowers Max Size to the chosen size (32 to 16384) and reimports. **This only sizes down**: limits already at or below the size, and sources already that small, are left alone. The Default setting and every enabled platform override above the size are lowered together. Source images, GUIDs and all other settings are kept; Unity does the resizing when it imports. The list previews each texture's change before you apply it.

## Convert to PNG

Writes 8-bit RGBA PNG files from each image's own pixels at full resolution, without compression, sRGB or normal-map processing, so the PNG matches the source image rather than the compressed, size-limited import. PNG files and HDR images (`.exr`, `.hdr`) are skipped; images with more than 8 bits per channel are reduced to 8 bits.

- **Replace in place** renames the image to `.png` with Unity (so its `.meta` goes with it) and replaces its contents with the PNG; the original image file is gone afterwards. The texture keeps its GUID and import settings, so every overlay, material and recipe that uses it still does. It is skipped when a PNG of that name already exists.
- **Create a PNG copy** writes `name.png` (or `name_converted.png` when that exists) next to the original, copies the original's import settings to it, and can point the overlays that use the original at the copy. The copies are added to the list and ticked; the originals stay listed, unticked.

## More

**Select in Project**, **Edit Pixels in Texture Utilities**, **Move Unused Textures** (see [Asset Consolidation and Repair](AssetConsolidationAndRepair.md)), **Copy CSV Report** or **Save CSV Report** (sizes, import settings, keywords, overlays and problems of every target), and **Tick Unused**, which ticks only the textures no overlay uses.

The **Results** panel records what each operation changed, skipped or failed; **Copy** puts it on the clipboard.
