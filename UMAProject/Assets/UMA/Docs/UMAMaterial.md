# UMA Materials

A `UMAMaterial` connects UMA content to a Unity material and shader. It tells UMA whether textures are composited, how texture channels map to shader properties, and which generated texture settings to use.

Slots and overlays that are intended to work together must use compatible UMA Materials.

## UMA Material and Unity Material

These are different assets:

- The Unity material contains the shader and its default properties.
- The `UMAMaterial` wraps that material with UMA generation rules.

Create one with:

`Assets > Create > UMA > Core > Material`

Assign the ordinary Unity material to `Default Material`. HDRP content can also provide the HDRP material fields shown by the inspector.

## Material Types

### Atlas

UMA composites compatible slots and overlays into texture atlases. Each material channel produces its own atlas texture.

Use Atlas when:

- Several body or clothing slots should share generated textures.
- Overlay layering and colorization are required.
- Reducing material and draw-call count is important.

Atlas generation costs temporary render texture memory and GPU work. Atlas resolution and fitting behavior are controlled by the generator.

### No Atlas

The inspector calls this `No Atlas`. UMA still composites overlay layers and colors, but generates one texture per channel instead of packing several slot regions into an atlas.

Use it when:

- The content needs overlay compositing but not atlas packing.
- A slot should retain a full texture layout.
- Atlas UV packing is undesirable.

### Use Existing Material

UMA uses the assigned material directly. There are no UMA texture channels, layer compositing, or generated texture colorization.

Use it for:

- Materials already authored as complete Unity materials
- Specialized shader workflows
- Content that should not create per-character textures

Changing the shared source material affects every renderer that uses it unless the project creates a separate material instance.

### Use Existing Textures

UMA creates a material and assigns overlay textures directly to the matching properties. It does not composite overlay layers. Overlay color channel zero can tint the result.

Use it when:

- Texture generation should be skipped.
- Each slot already has final textures.
- Layered overlays are not required.

## Generated Texture Settings

Atlas and No Atlas materials expose:

- `Generate Mip Maps`
- `Mip Map Bias`
- `Aniso Level`
- `Texture Filter Mode`
- `Mask with Current Color`
- `Mask Multiplier`

Mipmaps improve downsampling stability and usually belong on character textures. They increase texture memory. Test the result on the final platform and shader.

`Mask with Current Color` fills transparent surroundings with a related color before compositing. This can reduce dark or bright fringes around alpha-blended hair and similar assets.

## Texture Channels

Each channel describes one generated or assigned texture.

Important fields include:

- `Channel Type`
- `Texture Format`
- `Material Property Name`
- `Convert RenderTexture`
- `Texture Compression`
- `Down Sample`
- `Use Existing Texture For Channel`
- `NonShader Texture`

The channel order is also the order used by every assigned `OverlayDataAsset`.

### Channel types

- `Texture`: ordinary texture data.
- `NormalMap`: normal-map compositing with the correct neutral background.
- `MaterialColor`: material color rather than a generated texture.
- `TintedTexture`: direct texture assignment with overlay tinting.
- `DiffuseTexture`: diffuse/base-color texture processing.
- `DetailNormalMap`: detail normal processing.

Choose the type that matches both the source data and the shader's expectations.

### Material Property Name

Enter the exact shader property, such as `_BaseMap`, `_BumpMap`, or another project property. The inspector warns when the property is not present on the assigned shader.

Property names are case-sensitive.

### Down Sample

Downsampling reduces the generated resolution for a channel. This is useful when a mask or secondary map does not need the same detail as albedo or normals.

Test it at the intended camera distance. Small masks can tolerate more reduction than high-frequency normal maps.

### Convert RenderTexture

This converts the generated render texture into a `Texture2D`, allowing compression and CPU-side texture use. Conversion adds generation time and allocation cost.

Leave it off unless the channel needs conversion or compression.

### Use Existing Texture For Channel

For supported channel types on Atlas or No Atlas materials, this bypasses compositing and assigns an overlay texture directly.

UMA skips:

- Render texture allocation
- Overlay compositing
- Post-processing
- Downsampling
- Compression for that channel

Later overlay textures overwrite earlier textures assigned to the same material property.

This is useful for an independent channel that should remain untouched, but it changes the layering model. Do not enable it on a channel that needs several overlays blended together.

### NonShader Texture

A non-shader texture participates in UMA texture generation but is not assigned to a shader property. It can support UV lookup or project-specific runtime data.

Leave the material property name empty for a non-shader channel.

## Overlay Compatibility

An overlay using a UMA Material should have:

- The same number of texture entries as the material channels, unless it is intentionally an additional partial overlay.
- Textures in the same channel order.
- Correct import types, especially for normal maps.
- Blend modes appropriate for each channel.

The `OverlayDataAsset` inspector labels each entry with the UMA Material property name and warns about count mismatches.

## Slot Grouping

During generation, compatible slots that use the same UMA Material and renderer configuration can be grouped. Accidentally assigning a duplicate UMA Material asset with different settings can split renderers or atlases.

Reuse a deliberate material definition for content that should combine.

## Shared Colors and Shader Properties

Shared colors can tint overlay channels and can also carry material property-block values. The UMA Material's shader parameter and keyword authoring tools support those workflows.

Use shared material properties when a value should change per avatar without generating a new texture, such as:

- Skin shader parameters
- Wetness
- Emission strength
- Detail intensity

See [WardrobeRecipeEditor.md](WardrobeRecipeEditor.md) for shared-color authoring.

## Render Pipeline Materials

Import the appropriate UMA shader package for Built-in, URP, or HDRP. Verify that:

- The Unity material uses a valid shader for the active pipeline.
- The UMA Material references the correct pipeline material.
- Every channel property exists on that shader.
- Default textures and keywords are appropriate.

Pink output indicates a shader or render-pipeline problem, not an overlay compositing problem.

## Performance Guidance

- Atlas: best batching potential, highest generation work.
- No Atlas: retains compositing without atlas packing.
- Use Existing Textures: avoids compositing but may use more materials or renderers.
- Use Existing Material: lowest UMA texture work, least per-avatar texture customization.
- Downsample secondary channels when quality permits.
- Avoid converting render textures unless required.
- Use direct existing channels only when the channel does not need layered composition.
- Keep material variants intentional so compatible slots combine.

See [UMAGeneratorSetup.md](UMAGeneratorSetup.md).

## Troubleshooting

### A texture channel stays on the template material

Check the exact material property name, channel count, overlay texture entry, and material type. Confirm that the channel is not an unsupported direct-existing configuration.

### The normal map looks flat or incorrect

Use a `NormalMap` or `DetailNormalMap` channel as appropriate and import the source as a Unity normal map.

### Mipmaps are black or disappear at distance

Verify generator conversion settings, UMA Material mipmap generation, render texture conversion, and the shader's sampling path.

### Several overlays do not blend

`Use Existing Textures` and direct-existing channels do not provide the same compositing behavior as generated channels.

### Similar slots produce separate renderers

Compare UMA Material assets, Unity material references, renderer assets, and material settings. They must be compatible to combine.

## Related Guides

- [OverlayDataAsset.md](OverlayDataAsset.md)
- [SlotDataAsset.md](SlotDataAsset.md)
- [ContentCreation.md](ContentCreation.md)
- [UMAGeneratorSetup.md](UMAGeneratorSetup.md)
