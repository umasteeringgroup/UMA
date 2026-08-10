# Texture Utilities

Texture Utilities is an editor-only texture workbench for inspection, alpha repair, color adjustment, procedural details, and normal-map editing. Open it from `UMA > Textures > Texture Utilities`, or select one or more textures and choose `Assets > UMA > Open in Texture Utilities`.

The editor works on an RGBA32 working copy. A `*` beside the dimensions indicates unsaved baked changes.

## Loading, Queue, and Saving

- Assign a Project `Texture2D`, load an image from disk, or drag Project textures into the preview.
- Multiple Project textures form a queue. Each queued texture retains its working state while you switch between rows.
- `Save As PNG...` writes the current result to a new PNG.
- `Quick Save and Overwrite` is enabled only when the current source has a safe writable target.
- `Load From Disk` creates an editor working copy; it does not import the file automatically as a Unity asset.

Color adjustments are live preview values until `Apply` bakes them. Tool operations such as alpha fill, touchup, gradients, and details modify the working texture and mark it dirty.

## Preview and Background

`Fit` shows the full image. `Magnify` supports 25–800% zoom and middle-mouse panning. `100%` and `Center` reset the magnified view.

The Background section can display a second texture scaled to match width or height. `Combine On Save` composites the background into saved output; simply showing a background does not alter the working texture.

## Adjustments and Presets

Brightness, contrast, saturation, and hue affect the preview. `Apply` bakes them into the current texture. `Reset` restores the working texture to its unmodified source state, so save needed edits first.

`Auto-match` estimates adjustments relative to the assigned background. It is a starting point, not a color-managed material conversion.

Additional operations preserve alpha unless stated otherwise:

- `Invert Colors` inverts RGB.
- `Alpha From Luminance` can lower existing alpha for pixels below the cutoff.
- `Fill with White` or `Fill with Black` replaces RGB.
- Adjustment presets save and reapply parameter values, not image pixels.

## Split Texture

Splits the source vertically, horizontally, or into four quadrants. The source is unchanged and pieces are saved as numbered PNG files.

## Adjust Texture

This tool provides:

- Preview-only quadrant visibility.
- Resize, optional detail preservation, sharpening, and blur.
- Experimental Hairify processing.
- Alpha generation from grayscale, with optional solid-color defringing.
- Normal-map generation from luminance with adjustable strength.

Black becomes transparent and white becomes opaque when generating alpha from grayscale. Defringe replaces RGB while retaining generated alpha to reduce colored borders.

## Alpha Gradient

Replaces alpha while preserving RGB. Linear mode fades from a selected opaque edge. Radial mode has independent horizontal and vertical solid areas, gradient lengths, and center offsets. Values whose solid and gradient ranges exceed 100% are clamped.

Gradient changes apply live to the working texture.

## Alpha Fill

Expands nearby opaque RGB into transparent padding without changing the original alpha. Use it to reduce mipmap seams and colored edge fringes. Radius controls search distance; Alpha Threshold determines which pixels receive padding color.

## Touchup

Touchup currently provides alpha erasing with round, square, or grayscale bitmap brushes. Brush size is measured in source texture pixels. For bitmap brushes, black has no effect and white has full effect.

Drag directly on the preview. Fit and Magnify map the input back to texture coordinates.

## Add Details

Define a closed Bezier area on the preview and add deterministic procedural spots or blush while preserving alpha.

- Seed makes results repeatable.
- Edge Falloff softens the effect near the boundary.
- Spots control color, density, size, and variation.
- Blush controls color and opacity.
- Mirror Area mirrors the editable region; Mirror Effect applies the result on the opposite side.

Apply operations bake into the working texture. Use a source-control copy for skin textures that cannot be easily recreated.

## Normal Indent

Normal Indent loads an albedo for reference and a normal map as a separately editable working normal. The output is saved as a new PNG.

Decode modes handle raw RGB normals and packed alpha/green-style normals; `Auto` attempts to choose. Verify the preview before editing because an incorrect decode mode produces invalid directions.

Modes:

- `Path Indent` applies pressure along an editable curve, with width, end fade, taper, and independent side softness.
- `Filled Noise` applies procedural normal variation inside a closed shape with falloff, scale, octaves, seed, and pressure.

Both modes support mirrored application. Preview lighting and its draggable handle are inspection aids only. `Undo Last Apply` reverses the last normal operation; `Reset Normal` restores the loaded normal source.

Albedo and normal dimensions may differ, but curve coordinates follow normal-map dimensions. Matching dimensions are recommended.

## Saving and Import Settings

PNG output contains ordinary image data. After importing a normal result, set the Unity importer and UMA channel settings appropriate for that normal convention. Do not assume the editor preview changes the destination import type.

For source assets that Unity has internally packed as normal maps, use the correct decode path before saving RGB output.

## Recommended Workflow

1. Duplicate or commit the source texture.
2. Load one representative image.
3. Choose Fit or Magnify and inspect alpha against the checkerboard.
4. Apply one class of operation at a time.
5. Save to a new PNG before using overwrite.
6. Import the result with explicit sRGB, alpha, and normal settings.
7. Test mipmaps and material output on a generated UMA.

## Troubleshooting

### Preview Changed but Save Did Not

Brightness, contrast, saturation, and hue remain preview-only until `Apply`. Background display is also preview-only unless `Combine On Save` is enabled.

### Reset Removed Several Edits

Reset restores the working texture to its source, not merely the latest slider change. Save intermediate work to separate files.

### Normal Output Looks Pink, Orange, or Flat

Check whether the input was raw RGB or Unity-packed normal data and select the matching decode mode. Also verify that the exported PNG is inspected as a default texture when checking raw RGB values.

### Brush Size Feels Different When Zoomed

Touchup size is measured in source pixels, while the preview can be scaled or magnified.
