# Face and coating examples

Install the main Overlay Painter plugin first. This is its separate Examples companion; tests are in the separate Tests companion.

The twelve editable presets live in the main plugin at `Assets/UMA/OverlayPainter/Presets/Face and Coatings`. Apply them with the Material Preset browser. Cosmetic stacks belong on a rigged skin target; coating stacks work on ordinary surface targets. Every cosmetic, foundation, coating and residue layer remains editable.

See the plugin documentation **Makeup and Coating Damage** for workflow and control meanings.

## What is included

- Six 2K cosmetic texture sets and 1024px face captures on the actual Human Female 3.0 mesh, plus Original Skin for comparison. These demonstrate fitting to one UMA face, not guaranteed segmentation of every DNA variation.
- Six coating materials rendered on a curved URP panel, with their Albedo, Roughness, Metallic, Normal Control, Normal and AO outputs. These use the complete production material-preset and compositor path, including the residue mask.
- Timing reports. Face sampling times exclude export/upload; coating times include host setup, map generation, all preset layers and GPU readback. Test reports are in `OverlayPainterTests/Validation/MakeupCoatings`.

The PNGs are reference outputs; use the editable presets to generate textures for your own UVs. Normal Control uses neutral gray 0.5, raised values above it and recesses below it. Roughness is linear data; the material's smoothness is its inverse. Transparent makeup albedo uses straight color, and its matching roughness image carries the same coverage.

To repeat face captures, build a character in a Unity scene, then choose **UMA > Overlay Painter > Examples > Capture Makeup on Current UMA Face**. Capture creates temporary copies of the mesh and materials, preserving the scene character. The current helper expects an UMA Skin material. Existing base makeup, eyes, hair and skin belong to that scene character and are not supplied by the makeup generator.

Validated in the current main project with UMA 3.1f3 and Unity 6000.3.18f1. Custom faces can require boundary fitting and painted masks. These texture effects do not displace the mesh; peeling rims cannot create actual overhanging geometry.
