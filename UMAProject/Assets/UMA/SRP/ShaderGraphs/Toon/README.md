# UMA Toon Shader Graph

Two editable graphs support **Unity 6.3 / Shader Graph 17.3**, with both **URP and HDRP targets in each graph**:

- **UMA/Toon/UMA3_Toon**: toon surface, normal mapping, color grading and ink patterns.
- **UMA/Toon/UMA3_ToonOutline**: outline drawn as an expanded backface shell.

## Use with UMA

Duplicate `UMA3_Toon_UMAMaterial.asset` and the two example Materials before making character-specific changes. Assign the toon Material as the main Material and the outline Material as the second pass. The supplied UMAMaterial has both its default and HDRP references configured.

The atlas channels are `_MainTex` (DiffuseTexture) and `_BumpMap` (NormalMap). Existing overlays must supply compatible channels. The outline reads `_MainTex` alpha, so it uses the same atlas cutouts. Match **Alpha Cutoff** on the main and outline Materials. Set cutoff to zero for completely opaque surfaces.

For a regular MeshRenderer or SkinnedMeshRenderer, draw the same mesh with the toon Material and then the outline Material. Each submesh needs both draws; simply appending one Material is sufficient only for a single-submesh renderer.

The folder also includes crosshatch and halftone Material presets and a three-band ramp texture. The base preset uses threshold shading; enable **Use Ramp** to try the supplied ramp.

## Toon lighting

| Control | Purpose |
| --- | --- |
| Shadow Threshold | Transition from shadow to midtone; default 0.35. |
| Highlight Threshold | Transition from midtone to light; default 0.7. |
| Band Softness | Softens both transitions; near zero gives hard cel bands. |
| Shadow Tint / Midtone Tint | Color multipliers for the two darker bands. |
| Use Ramp | Set to 1 to replace the threshold bands with the Shading Ramp texture. |
| Shading Ramp | Horizontal RGB ramp: shadow on the left, light on the right. Sampled through its vertical center. Use Clamp wrapping; Point filtering gives hard bands, Bilinear gives softer transitions. |
| Shadow Strength | Amount of real-time shadow-map attenuation. |
| Ambient Fill | Artist-controlled fill color, including when no lights illuminate the surface. |
| Light Intensity | Multiplier on the direct-light result. |

Directional, point and spot lights contribute to the custom lighting. URP supports Forward and Forward+ light loops; configure additional lights **per pixel**. HDRP light intensities are evaluated with camera exposure. Light layers are respected. Cast shadows and alpha clipping use the pipeline's generated passes.

This is a stylized raster lighting model. Ambient Fill replaces baked GI and probe/sky diffuse lighting; reflection probes, PBR specular, area lights, ray-traced lighting and screen-space contact shadows are not evaluated. In HDRP use shadow maps for shadows on these surfaces. Render-pipeline exposure and post-processing can further change the final appearance.

## Outline

Set **Outline Size (World Units)** and **Outline Color** on the outline Material. The default width is 0.003 world units. Zero width hides the external silhouette. This is a geometry outline, so its apparent pixel width decreases with distance.

The shell follows mesh normals. Smooth normals produce continuous outlines; split normals, open geometry and disconnected pieces can create gaps or overlaps. The outline uses a separate draw and adds rendering cost. The supplied outline Material has its ShadowCaster pass disabled to avoid widening the character's shadow. On UMA, keep source-UV cropping disabled for these materials when using screen patterns or the expanded outline.

## Ink patterns

**Crosshatch Strength** and **Halftone Strength** are independent 0–1 controls. Both default to zero and can be combined. Pattern coverage increases in darker lighting. **Ink Color** sets their color.

- **Pattern Scale**: repetition density.
- **Pattern Angle (Degrees)**: rotates the pattern.
- **Hatch Line Width**: thickness within each repeating cell.
- **Pattern Space**: 0 uses mesh/atlas UV0; 1 uses screen coordinates with aspect correction. Screen space is useful for consistent comic-print styling. UV patterns move with the surface and their density depends on atlas layout.

Patterns are procedural and derivative-antialiased; no pattern textures are needed. Very fine patterns can still shimmer at distance or with temporal effects.

## Color grading

Grading is applied after toon lighting and before ink:

1. **Hue Shift (Turns)**: -0.5 to 0.5; zero preserves the original hue.
2. **Saturation**: 0 is grayscale; 1 preserves saturation; larger values increase it.
3. **Contrast**: centered on 0.5; 1 preserves contrast.
4. **Posterization**: 0 or 1 disables it; 2 and above selects the number of steps per RGB channel. Enabled posterization clamps to 0–1 before quantization.

The neutral defaults are hue 0, saturation 1, contrast 1 and posterization 0.

## Editing

Open either `.shadergraph` to edit the graph and exposed properties. The custom functions are in `UMA3_Toon.hlsl` and `UMA3_ToonOutline.hlsl`. Keep both graph targets enabled. The main URP target retains lighting variants; the HDRP target enables shadow-matte infrastructure with a transparent Shadow Tint, allowing the custom function to apply shadows per light without multiplying the final surface a second time.
