# Textures, UDIMs, and Texture2DArray Usage

This document explains UMA's UDIM and Texture2DArray workflow, how to build arrays, and how shaders consume them.

For image adjustment, alpha repair, splitting, detail generation, and normal editing, see [Texture Utilities](TextureUtilities.md).

## UDIM Source Layout
- UDIM tiles typically use a base name with a numeric suffix: `name.1001`, `name.1002`, etc.
- UMA expects tile order to match ascending UDIM index (1001, 1002, ...). By default we assume 10 tiles per row when computing array slice indices in shaders.

## Building Texture2DArray Assets
Editor tool: `Assets/UMA/Editor/TextureArrayCreator.cs`
- Menu: Assets > Create > Texture2DArray From Selection
  - Sorts selected textures by UDIM number
  - Writes a Texture2DArray asset named from the first tile's base name (without UDIM number) into the same folder
- Menu: Tools > Textures > Build Normal Texture2DArray From Selection
  - Validates textures are imported as Normal Map, linear (sRGB off), not crunched, and identical size/format/mip count
  - Creates a GPU-format-matching `Texture2DArray` with mips and copies data via `Graphics.CopyTexture`
  - Saves to the same folder, base name without UDIM numbers

Notes
- Arrays are order-dependent. Ensure correct UDIM sorting.
- Normal maps may be BC5 (RG) or DXT5nm (AG swizzle); the shader exposes `_NormalArrayEncoding` to choose.
- Increase `anisoLevel` and use trilinear filtering for normal arrays.

## Shader Consumption
See `Assets/SourceShaders/SRPShaders/Opaque/UMA_DiffuseNormalThicknessOcclusion_Gloss_UDIM.surfshader`.
- UVs are split into UDIM tile UV (frac) and tile index computed from floor(UV) and `_UDIMCols` (default 10).
- Sampling uses `SAMPLE_TEXTURE2D_ARRAY_GRAD` with explicit gradients to stabilize mips across seams.
- Normal unpack supports:
  - RG/BC5: XY in red/green
  - AG/DXT5nm: X in alpha, Y in green
  - Auto heuristic fallback

Material Properties
- `_BaseMapArray`, `_BumpMapArray`, `_ThicknessMapArray`, `_OcclusionMapArray`, `_GlossMapArray`
- `_UDIMCols` (default 10)
- `_NormalArrayEncoding` (RG_BC5 / AG_DXT5nm / Auto)
- `_NormalStrength`

## Troubleshooting
- Seams visible or different lighting across tiles
  - Verify `_UDIMCols` matches your layout (usually 10)
  - Ensure array slice order matches UDIM numbering
  - Confirm `_NormalArrayEncoding` matches your normal compression
  - Use the provided shader (GRAD sampling) to avoid LOD mismatch at seams
- "Completely wrong" normals
  - Encoding mismatch; switch `_NormalArrayEncoding`
- Blur on edges
  - Raise `anisoLevel` on arrays; ensure wrap mode = Repeat
