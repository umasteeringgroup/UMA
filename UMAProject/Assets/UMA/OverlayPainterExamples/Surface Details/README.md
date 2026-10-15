# Surface detail examples

These editable presets accompany Overlay Painter's residue, deposition, weld and lace generators. They belong to the **Examples** companion package; the main plugin does not depend on them.

## Try the presets

- Apply one of the nine material presets to a plugin layer: Oily Fingerprints, Wiped Smudges, Dried Water Rings, Salt Crust, Peeled Adhesive, Fresh Blood, Dried Mud, Blue Paint or Oily Runoff.
- For welds and lace, add a path from **+Path > Generators > Welds and Heat** or **Lace and Openwork**. The seven construction preset assets can also be assigned through the garment preset controls.
- Draw weld paths along the seam. Reserve enough path width for the heat band, then adjust Bead Width / Ribbon and Feature Spacing independently.
- For lace, inspect both the material view and transparency. Openings reveal whatever is underneath; they do not cut holes in an opaque receiving mesh.
- Fingerprints and wipes default to roughness only. View them on a polished surface with a reflection or moving light; their subtle appearance is intentional.
- Use World XY or World ZY for drips that must follow world down across rotated UV islands. Place the patch using World Patch Span and Patch Offset, then mask the receiving surface.

The creation menu **UMA > Overlay Painter > Examples > Create Surface Detail Presets** recreates missing presets and preserves edited ones. Presets retain procedural settings.

## Visual evidence

The `Validation` folder contains actual generated channel images, checkerboard transparency views and URP/Lit material renders on a shallow curved panel. These are procedural output captures, not painted promotional images. `Residue-0` through `Residue-4` follow the residue order above; `Splash-0` through `Splash-3` are blood, mud, paint and oil.

Captures use a 768 x 768 footprint. Residue close-ups use one centered mark of radius 0.32; weld close-ups use a wider ribbon and proportionately enlarged bead spacing. Check the texture's real on-model footprint before selecting ridge or thread density. Fine holes and threads necessarily average together below a texel.

Material images composite generated color, metallic and roughness over a panel and derive normals from generated Normal Control. They are a visual inspection aid, not a replacement for checking the final exported UMA material. Large specular highlights and subtle stepped gradients in the close-up panel renders are visible limitations of this simple validation setup and finite height precision. The PNG data captures are previews; they do not replace the host's higher-precision height pipeline.

## Validation environment and scope

Tested in the current main checkout `C:/GitHub/UMA/UMAProject`, installed **UMA 3.1f3**, **Unity 6000.3.18f1**, on an Intel Core i7-12700K with NVIDIA RTX 5070 Ti and 128 GB RAM.

The companion tests check deterministic output, independent color edits, serialization, aligned channel alpha, optional relief, wet-to-dry behavior, spatial-culling bounds, world mapping across mirrored UVs, undo/redo, cancellation, native path/projection rendering, and genuine lace openings. The test category is `SurfaceDetailGenerators`. Reports are included here after the final validation run.

Tests were invoked with their NUnit setup/teardown in the running Editor, preserving the user's open unsaved scene. This was not a Unity Test Runner launch or a player build. Shared native-preset and quilt regression checks were also run.

## Performance and production limits

See `Validation/Benchmark.txt` for 1K/2K/4K core-sampling timings and `Validation/Timings.txt` for 768-pixel multi-channel/native-path measurements. Core sampling excludes texture upload, compositing and mesh-map baking; native-path figures include GPU readback. These are individual measurements, not frame-time guarantees.

World deposition is planar and artist-directed, not fluid simulation over arbitrary folds. Lace adds texture relief, not free-hanging geometry. Weld heat colors are artistic, not metallurgical predictions. The provided renders establish working outputs and recognizable constructions; final AAA art approval still requires inspection and tuning on the intended production asset, at its final texture footprint and lighting.

Full controls and mapping conventions are described in [Surface Details](../../OverlayPainter/Documentation/SurfaceDetails.md).
