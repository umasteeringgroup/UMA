# Natural Stubble examples and validation

Stubble Maker 2.0 adds a Natural strand model for short facial and scalp hair. It uses curved,
individually varied hairs, minimum-distance root placement, regional density variation and a
facial growth field. Albedo, Roughness and Normal Control come from the same groom. Existing
saved layers retain Legacy rendering until you change **Strand Model** to **Natural**.

## Try the presets

1. Open Overlay Painter on a built character and select its face paint target.
2. Apply a material preset from **Skin / Stubble**: **Five O Clock**, **Natural Short**,
   **Coarse Stubble**, or **Salt and Pepper**. These add a procedural layer; they do not replace
   the existing skin. If the assets are missing, use **UMA > Overlay Painter > Create Missing
   Stubble Presets**.
3. Restrict coverage with a layer mask or Control Mask. Exclude lips, nostrils, eyes and unwanted
   cheek/neck areas. The facial growth field controls direction, not the beard boundary.
4. Adjust Root Spacing, Density, Length, Width and Seed, then regenerate. Gray Hair Fraction,
   Fine Hair Fraction and Clipped Tip Fraction control the mixed population. Keep relief subtle.
5. Save the painter document to retain procedural controls. For a reusable UMA overlay, export
   **Runtime Overlay (Transparent)**; the painter creates the shared alphaMask.

Dimensions use a fixed 2048-pixel Reference Resolution on the longest texture axis. Increasing
output resolution improves sampling without intentionally changing the groom. Changing Reference
Resolution or Root Spacing changes its scale. Changing color does not rearrange roots. Density
thins a stable potential population, so surviving roots keep their positions.

The full control reference is in
[Stubble Maker](../../OverlayPainter/Documentation/OverlayPainterGeneratorsAndFilters.md#skin--stubble-maker).

## Included files

- `Stubble-Preset-Comparison.png`: four actual Unity face renders, assembled without retouching.
- `*-2048-Face.png` and `*-2048-Closeup.png`: each preset on an actual UMA face.
- `Natural-Short-4096-*.png`: the same preset rendered from a 4K face texture.
- `*-Albedo.png`: transparent sRGB color, with straight alpha.
- `*-Roughness.png`: linear roughness and coverage. This is roughness, not smoothness.
- `*-NormalControl.exr`: linear floating-point height centered on 0.5, with coverage. This is
  a height source for Normal Control, not a tangent-space normal texture.
- `*-Parameters.json`: the settings used for each render.
- `*.txt`: the measured generation time and project/hardware information for that case.

The exported channels use the captured face UVs and a simple lower-face display mask. They are
examples, not universal beard masks or ready-made wardrobe recipes. Use the procedural presets
and an authored mask for another face. The color, roughness and height imports are configured
separately; do not enable sRGB on the data channels.

## Reproduce the renders

Select an already built UMA 3 character and choose **UMA > Overlay Painter > Examples > Render
Stubble On Selected UMA Face**. The builder snapshots the mesh/materials without rebuilding or
editing the avatar. It writes four 2K cases and one 4K case into this folder, overwriting the
corresponding example files. Generation and capture take time; the example batch displays progress.

The builder expects UMA 3 Head/RightEyebrowMiddle bones, the UMA skin shader, an accessible front
cheek and the complete OverlayPainter Examples package. It is an example renderer, not a general
head-fitting tool. Production authoring remains in Overlay Painter. Examples and Tests are shipped
in their existing separate companion packages; neither is required by the main stubble presets.

## Validation results

Tested in `C:/GitHub/UMA/UMAProject`, using the installed **UMA 3.1f3** settings asset and
**Unity 6000.3.18f1**. Hardware: **Intel Core i7-12700K**, **NVIDIA GeForce RTX 5070 Ti**.
The renderer uses parallel CPU sampling; GPU hardware also affects the face preview.

| Preset | Face texture | Plugin generation |
| --- | --- | --- |
| Five O Clock | 2048 x 2048 | 3,675 ms |
| Natural Short | 2048 x 2048 | 3,463 ms |
| Coarse Stubble | 2048 x 2048 | 4,384 ms |
| Salt and Pepper | 2048 x 2048 | 3,033 ms |
| Natural Short | 4096 x 4096 | 11,028 ms |

These are individual measured runs, not benchmark averages. They exclude mesh-map preparation,
host texture upload, export and face capture. The complete validation capture batch took about
101 seconds. The rendered avatar was **Human Female 3.0** with its existing skin material. Its
skin, lips, lighting and facial shape were not replaced to improve the result. Closeups and face
views were inspected, including the 4K comparison. No generated or painted-over beauty images
are used as proof of the procedural output.

**34 focused tests passed: 19 Edit Mode and 15 Play Mode.** Coverage includes deterministic groom
geometry, resolution independence, independent appearance streams, stable density thinning,
minimum root separation, parameter cloning/JSON, migration, preset placement preservation,
channel coverage/roughness, cancellation, CPU/GPU dark-color upload, undo/redo, draft previews,
legacy relief, rotated/mirrored/skewed UV direction and island-edge fallbacks.

## Reference comparison and limits

Real stubble references included a
[three-day beard macro](https://www.mactechnews.de/gallery/picture/3-Tage-Bart-Makro-39068.html)
and [close-up shaved stubble](https://commons.wikimedia.org/wiki/File:Beard_stubble_close_up.jpg).
The useful cues were mixed clipped lengths and thicknesses, visible skin between follicles,
regional growth directions and restrained follicle shading. Those informed the changes away
from regularly spaced, uniform straight marks.

Marketplace comparisons used the public descriptions and galleries for
[HairTG Surface: Facial Hair Edition](https://olif.artstation.com/store/OAnX/hairtg-surface-facial-hair-edition)
and [Facial Hair Toolkit](https://superhivemarket.com/products/facial-hair-toolkit).
Their paid tools were not executed and no marketplace assets were copied.

This remains a texture-based effect. It cannot supply hair silhouettes or parallax at grazing
angles, and very fine hairs can disappear under mipmapping or compression. Use geometry/cards
for longer facial hair or macro shots that require those effects. The anatomical flow is a
blended approximation, not a fitted follicle scan; artist-authored masks and grooming are still
needed for a particular person. Overlapping UVs share detail. These examples demonstrate the
current output; they do not establish photographic equivalence under every face and lighting setup.
