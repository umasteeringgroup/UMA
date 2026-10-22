# Sculpted Core VDM

An original procedural abdominal-relief example for OverlayPainter. Four staggered pairs of rounded pads, oblique ridges, a midline groove, and a small navel depression are evaluated from analytic functions. No source image, scan, commercial VDM, or mesh data is read by the generator.

- `Sculpted Core VDM.asset`: ready-to-use brush profile, signed RGB = local U/V/N, zero neutral, Source Alpha coverage.
- `Sculpted Core VDM.exr`: 512 x 512 signed float vector displacement. Small lateral offsets are retained for future geometry evaluation; Phase 1 uses only N.
- `Sculpted Core VDM Height.png`: linear grayscale illustration, `0.5 + 4 * N`. The EXR remains the authoritative vector source.
- `Sculpted Core VDM Preview.png`: shaded illustration of the analytic relief.

In a Projection or Path layer, select Normal Control > Source > VDM Relief and assign the brush. Try amplitude 1, Relief Gain 4, and Height Strength 16; adjust to placement size. Path modes supported are Stamps and Ribbon.

Regenerate through **UMA > Overlay Painter > Examples > Create Procedural Abdominal VDM**. The generator `Editor/ProceduralVDMExample.cs` creates uniquely named assets when the example already exists. Modify its analytic functions to make new shapes.

This example is part of the optional OverlayPainter Examples companion package. It has no dependency on the copyrighted reference supplied during development.


## Bodybuilder muscle-map set

See [Bodybuilder muscle maps](Bodybuilder/README.md) for the original image-generated abdomen, pecs, and back-spine maps, including their grayscale PNGs, signed EXRs, and VDM brush assets.
