# UMA Realistic Skin — URP

A separate, opt-in skin shader for Unity 6.3 / URP 17.3 and newer. The original `UMA3_SkinShader_URP` graph, materials, overlays, and races are not replaced.

## Try it

1. Select your existing **UMA3_SkinShader_URP Material or UMAMaterial** in the Project window.
2. Choose **Assets → UMA → Skin → Create Realistic URP Copy**. This creates new assets alongside the selection and preserves your textures, tint, tiling, remaps, and UMA atlas-channel definitions. It does not modify your avatar automatically.
3. For an ordinary renderer, assign the new Material. For a generated UMA avatar, assign the new **UMAMaterial** through your usual slot/overlay or material-override workflow; changing a generated renderer's material alone can be lost on the next rebuild. Keep the material assignment consistent across the skin slots and overlays that share an atlas.
4. Rebuild the avatar. Tune the **new Material** referenced by the UMAMaterial, not its generated per-avatar copy.

Alternatively, start with a matching Material/UMAMaterial pair in `Examples`:

- **Natural:** balanced, restrained skin and oil highlights.
- **Matte:** broader, quieter highlights for dry skin.
- **Dewy:** stronger, narrower surface-oil highlights. This is not a metallic or clear-plastic coating.

The example materials are **atlas templates**. UMA supplies the actual character textures when building its atlas; they are not pre-baked face textures. The sample pore detail uses the existing `SkinDetail_HDRPStyle` texture.

## What changed

The previous graph mixes a main-light-dependent effect into its base color. This shader keeps the albedo independent of lighting and evaluates its diffusion approximation and reflections for each pixel light.

- Two GGX reflection lobes, **blended** instead of adding two full-strength highlights. The index of refraction controls dielectric reflectivity.
- RGB diffusion widths, with red spreading further than green and blue. Geometric curvature is measured in world space; the diffusion radius is expressed in millimeters assuming one Unity unit is one meter.
- Independent pore strength for diffuse lighting: fine pores can remain crisp in reflections without making the entire face look rough or dirty.
- Thin-area backlighting controlled by the existing red mask channel and an optional extra control map.
- Normal-variance specular filtering to reduce tiny sparkling highlights at distance.
- Main/additional-light attenuation and shadows, light layers and cookies, ambient occlusion, reflection probes, lightmaps, probe volumes, fog, LOD crossfade, instancing, normal/depth/shadow passes, lightmapping, and motion-vector passes.
- Forward-only lighting pass, allowing the custom skin lighting to run with Forward, Forward+, or a Deferred renderer.

## Controls that matter most

| Want to change | Controls |
|---|---|
| Plastic-looking skin | Lower **Oil Reflection Blend**; lower the maximum **Smoothness Range**; try Matte |
| Softer transition into shade | Increase **Scattering Strength** slightly, then **Broad Diffusion** |
| Diffusion on small curved features | **Scattering Radius in Millimeters**; start around 2–3 mm |
| Excessive redness/waxy silhouette | Reduce diffusion strength/radius; avoid saturating Scattering Color |
| Stronger ears/backlighting | **Backlight Transmission Strength**, plus the control map's blue channel; use a real rear light |
| Pores too strong | Reduce **Skin Normal Strength** or **Detail Normal Strength**; for diffuse only, reduce **Pore Detail in Diffuse Lighting** |
| Oily forehead, less oily cheeks | Paint the control map's green channel; white allows the full oil blend, black disables that lobe |
| Tint seems to do nothing | **Tint Protection Strength = 0** disables the existing UMA tint-protection mask. White mask pixels protect the original albedo from tint |

**Skin Tint is multiplicative**, not a replacement albedo. Root material color/texture changes can also be overridden by UMA shared colors or material property blocks during a build.

The copy command preserves your existing scalar values. It does not silently apply a preset. For the newer preset finish, use an example material or copy its reflection and detail controls deliberately.

## Texture contract

All existing public graph input property names remain available:

| Input | Contents / import |
|---|---|
| `_BaseMap` | Albedo; sRGB |
| `_Base_Color` | Existing UMA tint color (the underscore is intentional) |
| `_BumpMap` | Normal; Unity **Normal map** import |
| `_MaskMap` | R scattering/thickness authoring signal, G ambient occlusion, B detail coverage, A smoothness; **linear** |
| `_Skinmask` | Existing per-channel tint-protection mask; its RGB channels blend between tinted and original albedo, matching this URP graph |
| `_DetailNormalMap` | UMA/HDRP-style packed detail: R albedo detail, G normal Y, B smoothness detail, A normal X; **Default texture, linear**, not Normal map import |
| `_SkinControlMap` | Optional: R diffusion, G oil reflection, B transmission, A pore coverage; **linear**. White is neutral; black suppresses that effect |

The legacy `_DetailNormalMapScale` name actually controls **detail albedo**, not normal strength. The Inspector now labels it accordingly. `_Detail_Normal_Scale` controls the pore normals, and `_Detail_Gloss_Scale` controls smoothness detail. Detail channels are centered around 0.5; the packed normal uses **AG**, not RG.

The red mask is reused as the existing graph's positive scattering signal: higher values permit more scattering/transmission. It is **not** converted to measured physical thickness or interpreted as metallic. Do not supply an ordinary metallic mask here.

All skin input maps share the base-map UV transform. Detail uses that UV plus its independent XY tiling. Mesh vertex RGB multiplies the resulting albedo, including in the lightmapping pass. Skin is opaque; base-map/tint alpha is not cutout coverage.

The optional control map is a normal material texture, not a newly added UMA atlas channel. Use it only when it matches the final atlas UVs, or extend your UMAMaterial/overlays to pack that channel deliberately. The shipped examples need no additional maps.

## Lighting and limitations

Use neutral lighting and sensible exposure while tuning. A bright environment or unbounded light intensity can still make any dielectric surface look washed out. Inspect the **View** diagnostic modes to separate albedo, normals, smoothness, occlusion, and scattering-mask problems from lighting problems.

This shader implements **local, curvature-aware diffusion**, not a screen-space or texture-space blur. It does not reproduce HDRP's diffusion across cast-shadow boundaries, measure thickness by ray tracing, or blur light between adjacent pixels. Transmission respects the light's shadowing rather than glowing through opaque occluders. For hero closeups requiring that full diffusion behavior, a dedicated scattering render pass would be a separate rendering feature.

Use **per-pixel additional lights** or Forward+ for full skin response to every light. URP's per-vertex additional-light option intentionally falls back to diffuse-only lighting for those vertex lights.

There is no extra full-screen pass or scene-color/depth copy. The shaded pass evaluates two specular lobes and two roughness-dependent environment reflections, plus six texture samples. This is a quality-oriented shader, not a claim of zero overhead: profile your intended GPU, resolution, avatar count, and light count. Depth and shadow passes omit skin shading.

Skin is one-sided. Use actual shell geometry for exposed interiors rather than disabling culling globally. This shader targets Shader Model 4.5-capable GPUs and 3D URP rendering, not the URP 2D renderer or HDRP. DOTS-specific material overrides are not implemented; ordinary SRP batching and GPU instancing are supported. GPU validation was performed on Windows/D3D11; other target graphics APIs still need platform testing.

## Validation

The accompanying `UMARealisticSkinTests` checks the original input contract, non-destructive copying, all three example UMAMaterial channel layouts, and GPU compilation of forward/clustered/shadow/lightmap/probe-volume/instancing variants and all seven passes. Run this fixture with a graphics device, **not `-nographics`**.

Development validation also renders the standard Human Female 3.0 and Human Male 3.0 with the same textures and lighting for the original, Natural, Matte, and Dewy materials. Lighting probes check unlit darkness, main/additional lights, and received shadows in Forward, Forward+, and Deferred.
