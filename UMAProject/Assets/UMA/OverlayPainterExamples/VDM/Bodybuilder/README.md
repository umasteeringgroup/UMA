# Bodybuilder muscle maps

Three original image-generated relief maps: **Abdomen**, **Pecs**, and **Spine**. Each includes its grayscale source PNG, a signed floating-point EXR, and a ready-to-use OverlayPainter VDM brush asset. They belong to the optional OverlayPainter Examples companion.

## Use in OverlayPainter

1. Create a Projection layer and enable Normal Control for a material that supports normals.
2. Choose **Normal Control > Source > VDM Relief** and assign the corresponding `Bodybuilder ... VDM.asset`.
3. Place the abdomen on the front torso, position the pecs above it, and use a separate projection from behind for the spine. Use separate layers so each placement and strength remains editable. These are independent stamps, not a prealigned full-body UV atlas.
4. Start with **Displacement Amplitude 1**, **Relief Gain 4**, and **Height Strength 8-16**. Adjust strength and projection scale to the character. Use projection edge fading and a layer mask where stamps overlap.
5. For a Path layer, use Stamps or Ribbon mode. Source Alpha coverage is configured on each brush.

## Data and provenance

All three source PNGs are original images generated with the built-in image-generation tool. No commercial sample was included or used as a source. Pecs retain finer sculpted muscle detail; abdomen and spine use smoother forms. Prompts are included alongside this document.

These are artistic scalar-height interpretations, not measured anatomy or full lateral vector-displacement captures. The PNG files are preserved byte-for-byte and imported as linear uncompressed data with no mipmaps. Each is 1254 x 1254 pixels. Do not mark them as normal-map textures.

The companion EXRs encode `R = U = 0`, `G = V = 0`, and `B = N = (source grayscale - neutralGray) / 4`. Grayscale uses Unity Color.grayscale. `neutralGray` is the mean of the outermost pixel perimeter, recorded in `Encoding.json`. This compensates for the generated background being close to, but not exactly, 0.5. The EXRs are linear RGBAFloat with zero displacement as neutral.

Alpha is independent rectangular edge coverage: `t = clamp01(min(u, 1-u, v, 1-v) / 0.04)`, `alpha = t*t*(3-2*t)`. It fades the outer four percent of the image; it is not a silhouette mask. Height is not premultiplied. Use a layer mask or projection fading for tighter anatomical coverage.

With Relief Gain 4 the rendered Normal Control height is `0.5 + source grayscale - neutralGray`. These maps can later supply normal-axis geometry displacement; they contain no lateral displacement or overhang information.

## Import validation

Created and verified in Unity 6000.3.26f1. All three brush assets resolved through the OverlayPainter VDM shader as finite floating-point height, retained source dimensions, and had zero alpha at their boundary corner. The source PNG hashes match the delivered images. No implementation code was changed for this asset addition.
