# UMA Realistic Skin — URP

A separate, opt-in skin shader for Unity 6.3 / URP 17.3 and newer. The original `UMA3_SkinShader_URP` graph, materials, overlays, and races are not replaced.

## Try it

1. Select your existing **UMA3_SkinShader_URP Material or UMAMaterial** in the Project window.
2. Choose **Assets → UMA → Skin → Create Realistic URP Copy**. This creates new assets alongside the selection and preserves your textures, tint, tiling, remaps, and UMA atlas-channel definitions. It does not modify your avatar automatically.
3. For an ordinary renderer, assign the new Material. For a generated UMA avatar, assign the new **UMAMaterial** through your usual slot/overlay or material-override workflow; changing a generated renderer's material alone can be lost on the next rebuild. Keep the material assignment consistent across the skin slots and overlays that share an atlas.
4. Rebuild the avatar. Tune the **new Material** referenced by the UMAMaterial, not its generated per-avatar copy.

Alternatively, start with a matching Material/UMAMaterial pair in `Examples`:

- **Natural:** balanced, restrained skin and oil highlights. Uses [Realistic Skin SSS](#realistic-skin-sss); its UMAMaterial shares the `UMA3_SkinRealisticSSS_Natural` material.
- **Matte:** broader, quieter highlights for dry skin. Uses [Realistic Skin SSS](#realistic-skin-sss).
- **Dewy:** stronger, narrower surface-oil highlights. This is not a metallic or clear-plastic coating. Uses Realistic Skin.

To compare the two shaders on one look, change the **Shader** of a copied example material; both read the same properties.

The example materials are **atlas templates**. UMA supplies the actual character textures when building its atlas; they are not pre-baked face textures. The sample pore detail uses the existing `SkinDetail_HDRPStyle` texture.

## Realistic Skin SSS

`UMA/URP/Realistic Skin SSS` (`UMA3SkinRealisticSSS.shader`) is a second, higher quality version of this shader with subsurface scattering. It reads the same textures and every property of `UMA/URP/Realistic Skin`, so it can replace it on an existing material.

To use it, do one of these:

- Change the **Shader** of an existing Realistic Skin material to **UMA/URP/Realistic Skin SSS**. All inputs keep their values.
- Select a UMA3 URP skin Material or UMAMaterial and choose **Assets → UMA → Skin → Create Realistic SSS URP Copy**.
- Start from `Examples/UMA3_SkinRealisticSSS_Natural` (Material and UMAMaterial), or from the Natural or Matte example, which use this shader.

What it adds over Realistic Skin:

- **Diffusion from a measured profile.** Diffuse light uses the normalized Burley diffusion profile (the one HDRP uses), integrated in advance over curved surfaces and stored in a lookup texture. Each color channel has its own scattering distance, so red travels furthest. On small curved features (nostrils, lips, ear rims, fingers) light wraps past the terminator; on broad areas it stays close to plain shading, as on real skin.
- **Wrinkles soften in diffuse light, not in reflections.** Each color channel reads the normal map with a footprint as wide as its scattering, measured in millimeters on the surface. Fine lines blur and turn slightly red in diffuse light while highlights keep full detail.
- **Red shadow edges.** Inside a shadow's soft edge, red reaches further into the dark than green and blue.
- **Transmission that respects what is in the way.** The thickness of the body between a light and the surface is measured in that light's shadow map (directional, spot and point lights). Ears and other thin parts glow when lit from behind, also with shadows enabled, and stay dark when something thick blocks the light. Realistic Skin loses its backlighting when the light casts shadows and lets it through the whole head when it does not.
- **Full GGX reflections** for both lobes with Smith visibility and Schlick Fresnel, an environment term fitted to GGX (no bright plastic rim at grazing angles), horizon and occlusion-based reflection occlusion.
- **Colored occlusion.** Creases darken red less than blue, as light bouncing inside them picks up the skin's color.

Additional controls:

| Control | Effect |
|---|---|
| **Curvature Response** | Scales how strongly surface curvature widens diffusion. 0 leaves only Broad Diffusion |
| **Wrinkle Softening in Diffuse Lighting** | Scales the softening of the normal map in diffuse light. 0 keeps wrinkles crisp |
| **Shadow Edge Scattering** | Red bleed inside shadow edges. 0 gives ordinary shadows |
| **Occlusion Color Bleed** | 0 gives neutral gray occlusion |
| **Minimum Thickness in Millimeters** | Transmission never treats a part as thinner than this. Raise it to dim and redden backlit ears |
| **View** | Adds Curvature, Transmission, Diffuse Light, Reflection, Diffuse Normal and Thickness |

Existing controls that mean something more specific here:

- **Scattering Radius in Millimeters** is the distance that holds about three quarters of the scattered light, for the brightest channel of **Scattering Color**. The color sets how far each channel travels relative to the others; it does not tint the skin. The defaults (2.5 mm, color 1 / 0.38 / 0.22) give profile distances of about 0.76 / 0.29 / 0.17 mm for red / green / blue.
- **Scattering Strength** blends between plain shading (0) and full scattering (1). The example uses 1.
- **Backlight Transmission Strength**: 0.25 is the physically scaled amount; higher values exaggerate.
- **Backlight Focus** brightens transmission when looking toward the light.
- **Broad Diffusion** adds the same softening everywhere, independent of curvature.

The lookup texture `UMA3SkinScatterLUT.exr` belongs to the shader (it is assigned in the shader's import settings, not on materials). **UMA → Build → Rebuild Skin Scatter Lookup Texture** recomputes it. If it is missing, the shader falls back to wrap lighting.

Limits of this version:

- Thickness for transmission is only as fine as the light's shadow map. With the main light, close-ups use the nearest cascade and read ears and fingers well; at a distance, or with low shadow resolution, thin parts read thicker or thinner than they are. Lights without shadows use an estimate from surface curvature instead, which lights rims and edges but not the flat part of an ear.
- Diffusion is still computed per pixel. Light does not blur between neighboring pixels as it does in HDRP's screen-space pass, so a hard shadow edge cast across flat skin gets its red edge from the shadow's own soft edge, not from a true blur.
- Cost per pixel: three more normal-map reads than Realistic Skin, and per light three lookup reads (six inside a shadow edge). With transmission enabled, nine shadow-map reads for the main light and five for each additional light that casts shadows. Set **Backlight Transmission Strength** to 0 to skip the shadow-map reads.

## Release notes

For **3.1f6 / 3.1.6**, the changes since 3.1f5 are the new [Realistic Skin SSS](#realistic-skin-sss) shader and its lookup texture, the Natural and Matte examples moving to it, and corrected UMA 3 skin tint masks, so the face, scalp, neck, shoulders and hips no longer show a seam under a dark Skin Tint. See the [3.1f6 changelog](../../../CHANGELOG.md#shading-content-and-documentation).

For **3.1f5 / 3.1.5**, the changes since 3.1f3 were the material-wide Metallic control described below and the expanded 0–2 Tint Protection Strength range. The Realistic Skin lighting model itself predates both releases. See the [3.1f5 changelog](../../../CHANGELOG.md#shading-content-and-documentation-1).

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
| Metallic skin or a metallic character finish | **Metallic (Entire Material)** under Surface Reflection: 0 keeps normal skin, 1 gives fully metallic reflections; intermediate values blend the response |
| Plastic-looking skin | Lower **Oil Reflection Blend**; lower the maximum **Smoothness Range**; try Matte |
| Softer transition into shade | Increase **Scattering Strength** slightly, then **Broad Diffusion** |
| Diffusion on small curved features | **Scattering Radius in Millimeters**; start around 2–3 mm |
| Excessive redness/waxy silhouette | Reduce diffusion strength/radius; avoid saturating Scattering Color |
| Stronger ears/backlighting | **Backlight Transmission Strength**, plus the control map's blue channel; use a real rear light |
| Pores too strong | Reduce **Skin Normal Strength** or **Detail Normal Strength**; for diffuse only, reduce **Pore Detail in Diffuse Lighting** |
| Oily forehead, less oily cheeks | Paint the control map's green channel; white allows the full oil blend, black disables that lobe |
| Tint seems to do nothing | **Tint Protection Strength = 0** disables the existing UMA tint-protection mask. White mask pixels protect the original albedo from tint |

**Skin Tint is multiplicative**, not a replacement albedo. Root material color/texture changes can also be overridden by UMA shared colors or material property blocks during a build.

**Metallic is one material-wide float**, defaults to zero, and requires no texture or additional UMA channel. It applies to both reflection lobes and direct/environment lighting. Increasing it reduces diffuse lighting, diffusion and transmission contributions; fully metallic surfaces use albedo-colored reflections and have no diffuse contribution. Smoothness remains independently controlled by Smoothness Range and Oil Smoothness. The packed mask's red channel remains scattering, not metallic.

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

`UMARealisticSkinSSSTests` covers the SSS shader: every Realistic Skin input exists with the same type and default, switching a material's shader keeps its inputs, the lookup texture's import settings and shader binding, the lookup values (plain shading without scattering, energy conservation with it), the example assets, and the same pass and variant compilation.

Development validation also renders the standard Human Female 3.0 and Human Male 3.0 with the same textures and lighting for the original, Natural, Matte, and Dewy materials. Lighting probes check unlit darkness, main/additional lights, and received shadows in Forward, Forward+, and Deferred.
