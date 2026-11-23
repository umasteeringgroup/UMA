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

**Welcome to UMA → Configure UMA** switches between the two shaders for the selected skin UMAMaterials. **Maximum speed** assigns a Realistic Skin copy of each Realistic Skin SSS material, saved next to it as `<name>_Speed.mat`, and keeps the SSS material as an editor-only backup. **Maximum Fidelity** puts the SSS material back. Applying Speed again refreshes the copy from the SSS material, so tuning done on the SSS material carries over. Materials that use the expression wrinkle shader keep it, because wrinkles need SSS. The presets also set the generated skin textures' **Aniso Level**: 1 for speed, 8 for fidelity, which keeps pores sharp on skin seen at a grazing angle.

## Expression wrinkles

`UMA/URP/Realistic Skin SSS Wrinkles` (`UMA3SkinRealisticSSSWrinkles.shader`) is Realistic Skin SSS with expression wrinkles added. It reads every property of Realistic Skin SSS. The wrinkle code lives in `UMA3SkinWrinkles.hlsl` and is compiled only into this shader (`UMA_SKIN_WRINKLES`), so Realistic Skin SSS is unchanged.

Two textures drive it:

| Input | Contents / import |
|---|---|
| `_WrinkleMap` | RG wrinkle normal X / Y in tangent space (0.5 = flat), B crease depth, A flush. A *delta* on top of the skin normal map, so it works with any face normal map. **Linear** |
| `_WrinkleControlMap` | R zone id of layer A, G mask of layer A, B zone id of layer B, A mask of layer B. Ids are stored as id × 17. **Linear, uncompressed** |

Up to two zones can overlap on one texel. Each zone always uses the same layer, and the baker spreads every zone's id into the empty space around it, so ids only change where both masks are zero. Bilinear filtering, mipmaps and atlas scaling then never show the wrong zone.

Each zone has a weight on the material, **Wrinkle Zone Weights**, from −1 to 1. Positive values compress the skin and show the wrinkles. Negative values stretch it, which smooths the skin normal map and pores in that zone (**Stretch Smoothing**). With every weight at 0 the shader skips all wrinkle texture reads.

| Id | Zone | Id | Zone |
|---|---|---|---|
| 1 / 2 | Forehead left / right | 8 / 9 | Nasolabial fold left / right |
| 3 | Glabella (vertical frown lines) | 10 / 11 | Mouth corner left / right |
| 4 | Nose root (horizontal frown lines) | 12 | Lip lines |
| 5 | Bunny lines | 13 | Chin |
| 6 / 7 | Crow's feet and lower lid left / right | 14 / 15 | Neck left / right (reserved; the UMA 3 neck is not on the head tile) |

Left and right are the character's own sides. `UMAWrinkleZone` in code has the same numbers, and `UMAWrinkleZones.WeightProperty` gives each zone's property name.

The two forehead zones meet at the midline with no gap, on the same layer, so the glabella can overlap both of them. Their ids are consecutive, so filtering at the join can only show one of the two (`UMAWrinkleControlMapPacker.TouchingPairs`). Each forehead line is one curve across the forehead, so with both brows raised it runs straight through the middle, fading to 65% there. Raising one brow also gives the other half 0.2 in the UMA 3 profile, so its lines fade across the midline instead of stopping.

Other controls: **Wrinkle Depth** scales the wrinkle normals (default 1; the UMA 3 maps were tuned at 1.5, which gives deeper, more dramatic lines), **Crease Darkening** darkens ambient light inside creases, **Crease Flush** and **Flush Color** redden compressed skin slightly, **Wrinkle View** shows the zones, the wrinkle amount, the wrinkle normal or the crease depth.

The UMA 3 head's maps, `UMA3_Wrinkles.1001.png` and `UMA3_WrinkleZones.1001.png` in `UMA3/Textures`, are made procedurally by **UMA → Skin → Bake Procedural Wrinkle Maps (UMA 3 Head)**. Every wrinkle is a curve laid out from facial landmarks on the head mesh and unwrapped around a vertical axis. The baker computes heights from 3D surface position, so nothing opens at UV seams. Each wrinkle has a narrow valley between broad rolls that keep the skin's volume, and depth and width wander along each line. Lines break, taper and fork, and the two sides of the face use different random streams.

### Editing the wrinkle map by hand

Artist guides: [Expression Wrinkle Maps](../../../Docs/ExpressionWrinkleMaps.md) (how they work, every menu, the image-editor
workflow) and [Painting Expression Wrinkle Maps in Overlay Painter](../../../OverlayPainter/Documentation/ExpressionWrinkleMaps.md)
(paint on the 3D head, export a new wrinkle overlay). Every wrinkle map keeps its editable height next to it as
`<map>_height.png` (16-bit, editor only), written by the bake, the source import and Overlay Painter's wrinkle export.

The wrinkle map's RG holds slopes, which cannot be painted sensibly, so edit its sources instead:

1. **UMA → Skin → Export Wrinkle Map Sources (UMA 3 Head)** bakes the procedural wrinkles and writes four images to a folder you choose (by default `WrinkleSources` in the project folder, outside `Assets`, so they are not imported or shipped):
   - `UMA3_Wrinkles.1001_height.png`: wrinkle height, 16-bit grayscale. Mid gray is flat; black is −2.5 mm and white +2.5 mm (`UMAWrinkleSourceMaps.HeightRangeMM`). The UMA 3 bake uses −1.2 to +2.1 mm. Paint valleys darker and rolls lighter.
   - `UMA3_Wrinkles.1001_crease.png`: crease depth (the map's B), 16-bit grayscale, 0 = none. It darkens ambient light, so paint it where you paint valleys.
   - `UMA3_Wrinkles.1001_flush.png`: flush (the map's A), 16-bit grayscale.
   - `UMA3_Wrinkles.1001_guide.png`: a reference only. Each zone is tinted over the height, so you can see where the zones are.
2. Edit them in any image editor. Keep the size, and save the height with **16 bits per channel**. 8 bits give visible steps in the slopes.
3. **UMA → Skin → Import Wrinkle Map Sources (UMA 3 Head)** rebuilds `UMA3_Wrinkles.1001.png` (and its height) from the folder. With **UMA → Skin → Auto-Import Wrinkle Map Sources (UMA 3 Head)** ticked, saving one of the images rebuilds the map automatically. Each texel's normal is taken along its own tangent frame, so mirrored islands are correct. Padding around the UV islands is filled from the nearest island texel, as in the bake. The Console reports anything clipped at the height range or at the slope limit.

A wrinkle only shows inside its zone, at that zone's weight. The import does not change the zone map, so paint new lines inside the tinted areas of the guide. Gray and RGB images are both read (the first channel), as are 8-bit palette images. Interlaced PNGs are not.

The rebuilt map takes the slope from neighbouring texels, while the bake computes it from the curves themselves, so lines finer than a texel come out a little softer. Importing the unedited sources gives a mean difference of about 1/255 from the bake. **Bake Procedural Wrinkle Maps** replaces an imported map. Keep your source images, and import them again after a bake.

### On an UMA character

The maps go through the UMA atlas like any other texture:

- `Examples/UMA3_SkinRealisticSSS_Wrinkles` (UMA Material and Material) is the UMA 3 skin (Natural) on the wrinkle shader, with two more channels after the skin's own: `_WrinkleMap` and `_WrinkleControlMap`. Both are Texture channels with **Never Compress** set, so the generator's compression settings never touch them. Block compression moves zone ids off their levels and bends the wrinkle normals.
- `UMA3/Overlays/UMA30_ExpressionWrinkles_UDIM1001` carries the two maps in those channels and no texture in the others. As an additional overlay on the head it changes nothing but the wrinkle channels. Its UMA Material is the wrinkle skin above.
- The **UMA Expression Wrinkles** component (`UMAExpressionWrinkles`) puts them on a character. Each build, it adds the overlay to the head slot (`UMA30_Body_UDIM1001`, or a race's baked copy of it) and switches that slot to the overlay's UMA Material. No slot, overlay or recipe asset is changed. Only the head gets the wrinkle material; the rest of the skin keeps its own, so only the head's atlas carries the two extra channels.
- The zone weights are on the component, and `SetWeight(UMAWrinkleZone, float)` sets them from code (when no expression profile drives them). They are applied to the head's renderer material slot through a MaterialPropertyBlock. Characters that share a generated material therefore still wrinkle on their own. Weights set on the material asset are overridden.

To try it: select an UMA 3 avatar in the scene and choose **UMA → Skin → Add Expression Wrinkles to Selected Avatar**. This adds the component, which rebuilds the avatar, and makes the UMA 3 wrinkle assets first if they are missing. The wrinkles then follow the avatar's expressions (see below). To set the zone weights by hand instead, clear the component's **Expression Profile**.

### Driven by expressions

With an **Expression Profile** (`UMAWrinkleProfile`), the component sets the zone weights every frame from the character's expressions. It reads the Dynamic Expression Player, or a classic Expression Player when there is no dynamic one. Animation, the Expression Player's sliders, blinking and code all drive the wrinkles the same way.

A profile is a list of rules. Each rule names an expression, a zone, and the weight it adds at the expression's full positive value (the first half of its name, *brow up* in `leftBrowUp_Down`) and at its full negative value (*brow down*). A negative amount stretches the zone. A zone's weight is the sum of its rules times **Strength**, clamped to −1…1. The value of a Dynamic Expression Player expression is measured from that expression's own default, so the same profile works for both players.

`UMA3/Races/Expressions/UMA 30 Wrinkle Profile` covers the UMA 3 expression set:

| Expressions | Wrinkles |
|---|---|
| Brow up (left, right, middle) | Forehead; brow down stretches it and folds the glabella and nose root |
| Brows in | Glabella and nose root |
| Nose sneer, upper lip up | Bunny lines, nose root, upper nasolabial fold |
| Cheek squint, eye close | Crow's feet; a puffed cheek stretches the fold |
| Smile | Nasolabial fold, crow's feet, mouth corner; a frown folds the mouth corners and chin |
| Pucker, lower lip up | Lip lines, chin, mouth corners |
| Jaw open | Stretches chin, lips and folds |
| Happy, Jovial, Sad, Sour, Despair, Stupid and Yawn faces | Their own rules, as these presets move the face themselves |
| Visemes U, CH, O, RR, nn, kk, DD (lips pursed) | Lip lines, in proportion to how far each pulls the mouth corners in; U and CH also the chin and corners |
| Visemes PP, FF (lips pressed, lower lip tucked) | Chin, and lip lines for PP |
| Visemes E, I, SS (lips spread), aa (jaw open) | Stretch the lip lines and chin; spread lips deepen the folds slightly |

The viseme amounts were measured on the full-strength viseme poses; with the Mild group the lips move less, so the wrinkles are slightly stronger than the motion. Copy the asset to tune it, or create one with **Assets → Create → UMA → Rendering → Expression Wrinkle Profile**. The neck zones have no rules yet.

For the whole project, **Project Settings → UMA → Expression Wrinkles** has three choices:

- **Per Avatar** (default): only avatars with the component.
- **All Avatars:** every DynamicCharacterAvatar gets the component, with the settings' overlay and profile, when it builds. The added component is never saved into scenes or prefabs.
- **Off:** no avatar gets wrinkle maps, not even one with the component.

**Welcome to UMA → Configure UMA** sets this. **Enable wrinkle maps** with Maximum Fidelity chooses All Avatars, making the UMA 3 wrinkle assets first if needed. **Remove wrinkle maps** with Maximum speed chooses Off and clears the settings' overlay and profile, so they no longer pull the wrinkle textures into builds. Leaving the option unticked keeps the current choice.

For a skin other than Natural, select its UMA Material and choose **Assets → UMA → Skin → Create Expression Wrinkles Copy**. Then set the copy as the component's **Wrinkle Material**.

Disabling the component removes the weights at once. The head keeps the wrinkle material until the character is built again (**Rebuild Character** on the component, or `RebuildCharacter()`).

Cost: two 2048 × 2048 uncompressed RGBA atlases with mipmaps on the head page, about 43 MB of GPU memory per character that has wrinkles. With every weight at 0, the shader skips all wrinkle reads and renders exactly like Realistic Skin SSS.

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
| Pores larger on the body than on the face | Bake even detail UVs into the body slots; see [Even pore size across texture tiles](#even-pore-size-across-texture-tiles) |
| Pores on lips, nails, palms or other areas that should be smooth | Paint the packed mask's blue channel darker there; see [Detail coverage](#detail-coverage) |
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

All skin input maps share the base-map UV transform. Detail uses its own coordinates (see [Even pore size across texture tiles](#even-pore-size-across-texture-tiles)) times its independent XY tiling. Mesh vertex RGB multiplies the resulting albedo, including in the lightmapping pass. Skin is opaque; base-map/tint alpha is not cutout coverage.

The optional control map is a normal material texture, not a newly added UMA atlas channel. Use it only when it matches the final atlas UVs, or extend your UMAMaterial/overlays to pack that channel deliberately. The shipped examples need no additional maps.

## Detail coverage

The blue channel of `_MaskMap` (times the control map's alpha, when one is assigned) scales all three detail strengths: white shows the detail map in full, black hides it. The UMA 3 body masks (`UMA_F_SSS.AO.Detail.Gloss.1001`–`1005`) are painted by area:

| Area | Coverage |
|---|---|
| Fingernails, toenails, lips, areola and nipple, nostrils, ear canals, inner eye rim | 0 |
| Palms and soles | 0.08–0.10 |
| Ears | 0.12 |
| Groin, eyelids | 0.25–0.30 |
| Breast, toes, buttocks, inner thighs | 0.40–0.52 |
| Torso, legs, neck, arms, top of the foot | 0.68–0.74 |
| Face, back of the hand, knees, elbows | 0.80–0.92, rising to 1.0 on the nose and inner cheeks |

Coverage fades the albedo and smoothness detail toward the detail map's own average, not toward zero. The average is read from the map's smallest mip level, so the detail map needs mip maps. A map whose albedo channel is not centered on 0.5 (the shipped `SkinDetail_HDRPStyle` averages 0.68, which brightens skin by up to a third at full **Detail Albedo Strength**) therefore gives the same overall brightness under dark coverage as under white; without this, every low-coverage area would come out darker than the skin around it.

Deep creases also fade where the occlusion channel is dark (on the ears only the canal is cut, because their occlusion is dark in broad bands), and there is no detail where the texture is squeezed into less than half the UV space the rest of its tile gets (the inside of the nostrils and ear canals), because the pattern would be magnified there. The lips are found by their colour in the albedo, and the area extends about 0.7 mm past the lip border and into the corners of the mouth. The red channel is 0.45 on the nails, so they scatter less than skin, and 1 elsewhere. Smoothness (alpha) is lowered by about a quarter on the areola and nipple, which have no pore detail to break up their highlight. Occlusion, detail and scattering are evened out where UV islands and tiles meet: each side moves to the average of the two, fading out over 48 pixels, so ambient light and pore strength do not step at a seam. Lowering a detail strength on the material scales every area together; to change one area, paint the blue channel.

## Even pore size across texture tiles

**Detail Tiling** repeats the detail map over UV space, so a texture tile that covers more of the body shows larger pores. The UMA 3 body uses five tiles: the face tile covers 0.33 mesh units per UV unit, the arms 0.77, the torso 1.12 and the legs 1.22. From the first UV channel alone, pores on the torso and legs come out about 3.5 times larger than on the face.

Both Realistic Skin shaders, and the `UMA3_SkinShader_URP` graph, therefore read the detail map with the mesh's **fourth UV channel** (`uv4`, `TEXCOORD3`) when it holds baked detail coordinates. The second and third channels are left for lightmaps. Baked coordinates are never below 1. Where the fourth channel is missing (UMA fills it with (0,0)) or holds an ordinary 0..1 layout, the shader uses the first channel as before, so other meshes and older slots look unchanged.

- The UMA 3 body slots (`UMA30_Body_UDIM1001`–`1005`) ship with this channel baked. Races that share those slots get it too.
- For your own skin slots, select them in the Project window and choose **Assets → UMA → Skin → Bake Even Detail UVs On Selected Slots**, then rebuild the characters. **Remove Even Detail UVs From Selected Slots** takes the channel out again.
- The bake scales each UV island of the first channel as a whole, to 0.33 mesh units per UV unit, so no new seams appear and **Detail Tiling** keeps the pore size it gives on the UMA 3 face. It replaces whatever the slot had in its fourth UV channel. From code, use `UMASkinDetailUVUtility.BakeSlot` (it takes the surface per UV unit as an argument).
- Scaling whole islands is enough when the texture layout is even inside each island. Where it is not, use **Bake Even Detail UVs On Selected Slots (Unfold Islands)** (`UMASkinDetailUVUtility.BakeSlotRelaxed`). It also unfolds every island until each triangle is as close as possible to its shape and size on the mesh, giving densely modeled areas (nose, lips, eyes) more say than their surface. The island keeps its seams, so no new seam appears, but the detail coordinates no longer follow the texture layout and may overlap, which a tiled map does not show. The first UV channel is only read, so textures are not affected. A curved surface cannot be laid flat exactly: on a head made of one island the detail size still varies by about 1.3 to 1.45 times (from more than 3), and a nose keeps some stretch.
- The UMA 2 HumanFemale and HumanMale skin slots (33 slots: body, head, the modular head parts and the high-poly variants) ship baked this way. Their head layout gives the face two to three times less texture per surface than the neck. Both UMA 2 skin materials now use one **Detail Tiling** (16). Their head mask maps keep only about 15% detail coverage on the nose, where the unfolded layout still stretches.
- UMA only rescales the first UV channel when it packs textures into an atlas, so detail from the fourth channel also keeps its size when a skin slot shares an atlas.

In the `UMA3_SkinShader_URP` graph the choice is made by a small custom function (`SkinDetailUV`) in front of the detail map's **Tiling And Offset** node.

That graph also softens the whole normal map by its scattering: normal strength is 1 minus the scatter mask times **SSS Strength**. With a full scatter mask and **SSS Strength** 1, as the UMA 2 mask maps and several of the supplied skin colors have, that used to switch the main normal map and the detail normals off completely. The strength now never drops below one half; settings that left more than half are unchanged.

## Debugging skin in the Scene view

The **UMA Toolbar** has a **Skin View** menu (the texture icon). It shows one input or one part of the shading on every Realistic Skin SSS material in the open scenes:

| Group | Views |
|---|---|
| **Maps** | Each texture as stored: Albedo, Normal, the four channels of the packed mask, Tint Protection, the three parts of the detail map, and the control map |
| **Result** | What the shader makes of them: albedo after tint and detail, smoothness, and the shading normal |
| **Mesh** | Vertex normal, vertex tangent, vertex color, a UV checker, and a checker of the detail coordinates (one square per detail tile, so squares of one size mean pores of one size) |
| **Lit Without** | Normal rendering with one input switched off: the normal map, the detail map, occlusion, or the smoothness map |
| **Lighting** | Diffuse light, reflection, transmission, curvature, thickness, and the softened normal diffuse light uses |

To find which input causes a problem, go through **Lit Without**: the view in which the problem disappears names the input. If it stays in all four, look at **Mesh** and **Lighting**; a problem that is there in **Lighting > Diffuse Light** but in no map and no mesh view comes from the lights or their shadows.

How it works, and why it costs nothing at runtime:

- The views live in a second shader, `UMA/URP/Realistic Skin SSS Debug` (`UMA3SkinRealisticSSSDebug.shader`). It compiles the same code as the production shader with `UMA_SKIN_DEBUG_MAPS` defined, and reuses the production shader's shadow, depth, meta and motion-vector passes. The production shader never defines that symbol, so it contains none of the debug code and no extra texture reads.
- While a view other than **Lit** is chosen, the material instances in the scene (the ones UMA generates for each character, including after a rebuild) are put on the debug shader. **Lit** puts them back. In its own Lit view the debug shader renders exactly like the production shader.
- Material assets are never changed: a material asset used directly on a renderer is left alone, and the button's tooltip says how many were. To debug one, give it the debug shader by hand and set **Show** under **Debug Shader**, then set it back.
- The view lasts for the editor session. Before a scene is saved the materials are put back on the production shader, so the debug shader is not referenced by saved content and is not included in a build.

The debug shader keeps the production shader's inputs, so a material can be switched either way without losing values. The older **View** setting under Diagnostics still works on both shaders.

## Lighting and limitations

Use neutral lighting and sensible exposure while tuning. A bright environment or unbounded light intensity can still make any dielectric surface look washed out. Inspect the **View** diagnostic modes to separate albedo, normals, smoothness, occlusion, and scattering-mask problems from lighting problems.

This shader implements **local, curvature-aware diffusion**, not a screen-space or texture-space blur. It does not reproduce HDRP's diffusion across cast-shadow boundaries, measure thickness by ray tracing, or blur light between adjacent pixels. Transmission respects the light's shadowing rather than glowing through opaque occluders. For hero closeups requiring that full diffusion behavior, a dedicated scattering render pass would be a separate rendering feature.

Use **per-pixel additional lights** or Forward+ for full skin response to every light. URP's per-vertex additional-light option intentionally falls back to diffuse-only lighting for those vertex lights.

There is no extra full-screen pass or scene-color/depth copy. The shaded pass evaluates two specular lobes and two roughness-dependent environment reflections, plus six texture samples. This is a quality-oriented shader, not a claim of zero overhead: profile your intended GPU, resolution, avatar count, and light count. Depth and shadow passes omit skin shading.

Skin is one-sided. Use actual shell geometry for exposed interiors rather than disabling culling globally. This shader targets Shader Model 4.5-capable GPUs and 3D URP rendering, not the URP 2D renderer or HDRP. DOTS-specific material overrides are not implemented; ordinary SRP batching and GPU instancing are supported. GPU validation was performed on Windows/D3D11; other target graphics APIs still need platform testing.

## Validation

The accompanying `UMARealisticSkinTests` checks the original input contract, non-destructive copying, all three example UMAMaterial channel layouts, and GPU compilation of forward/clustered/shadow/lightmap/probe-volume/instancing variants and all seven passes. Run this fixture with a graphics device, **not `-nographics`**.

`UMARealisticSkinSSSTests` covers the SSS shader: every Realistic Skin input exists with the same type and default, switching a material's shader keeps its inputs, the lookup texture's import settings and shader binding, the lookup values (plain shading without scattering, energy conservation with it), the example assets, and the same pass and variant compilation.

`UMASkinDebugViewTests` covers the debug shader and the Skin View switch: the debug shader has every input of the production shader with the same type and default, the production shader never compiles the debug code, every view has its number in the shader, scene material instances move to the debug shader and back while material assets and other shaders are left alone, and all passes of the debug shader compile.

`UMASkinDetailUVTests` covers the detail UV bake: every island reaches the requested surface per UV unit and keeps its shape, no coordinate is below 1, an island that is uneven inside comes out even from the unfolding bake, a mirrored island stays mirrored, vertices split at one position stay in one island, only the fourth UV channel of a slot is written, and the five UMA 3 body slots carry the baked channel.

Development validation also renders the standard Human Female 3.0 and Human Male 3.0 with the same textures and lighting for the original, Natural, Matte, and Dewy materials. Lighting probes check unlit darkness, main/additional lights, and received shadows in Forward, Forward+, and Deferred.
