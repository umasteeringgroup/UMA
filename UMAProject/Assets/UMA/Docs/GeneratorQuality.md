# Generator quality profiles

UMA 3.0f6 / Unity 6.3 and newer.

## Quick start

1. Create assets with **Assets > Create > UMA > Quality > Generator Quality Profile**. Create one for each desired tier, such as Desktop High and Mobile Low.
2. Check only the settings that tier should override. Unchecked settings inherit the generator baseline (or a lower-priority profile). Checking an asset reference and choosing None explicitly clears it; leave required generator dependencies inherited unless replacing them.
3. Create **Generator Quality Settings** from the same menu. Assign a default profile and add platform/quality mappings. Quality names match **Project Settings > Quality**, not numeric indices.
4. Add **Generator Quality Controller** to a scene object. Assign the generator and settings asset. Leaving Generator empty uses UMA's current indexed generator. Alternatively, assign a fixed Profile.
5. Choose how existing characters change: **New Builds Only**, **Gradual**, or **Immediate**. Gradual is usually appropriate for an in-game options menu; Immediate is intended for loading screens.

The profile and mapping assets are reusable data, not scene components. `UMAGeneratorOverride` can reference the same profile, preserving its role as a scene-specific override. Its legacy controls remain available when no profile is assigned. Existing scenes do not need migration.

## Resolution and precedence

Mapping selection chooses one profile in this order:

- Exact runtime platform and quality name.
- Platform-specific row with Any quality level.
- Any-platform row with the quality name.
- Any-platform / Any-quality row, then the default profile.

The first matching row wins ties. The Inspector warns about duplicate mappings. Unknown/renamed quality names fall back normally. In the Editor, the current desktop player platform is used unless Preview Platform is enabled; player builds always use their actual runtime platform.

Layers apply over the generator's captured baseline, lowest priority first. Controllers default to priority 0; scene overrides default to 100. Equal priorities use activation order, not refresh order. Disabling a layer reveals the current lower layers; disabling the last one restores the baseline. Hardware scaling is resolved afresh, never compounded on every switch. Zero/unknown memory sizes are not treated as low-memory hardware.

## What profiles control

- Atlas resolution, initial scale, fitting, source-UV cropping and padding.
- RenderTexture versus Texture2D output, async conversion, mipmaps, filtering, anisotropy, mip bias and maximum output dimension. Size limits preserve aspect ratio and do not edit source textures.
- Incremental work budget, iterations, inter-frame delay, upload queue budget and existing garbage-collection options.
- Existing `UMASimpleLOD` distance/detail/slot/texture policies and expression/bone-animator LOD thresholds. A profile does not add an LOD component.
- Optional character atlas scale, mesh readability and blendshape loading policy. These can affect animation features; leave them inherited unless the tier intentionally changes them.
- Renderer shadow, motion-vector and skin-weight settings. Explicit slot renderer assets are respected unless Override Explicit Renderer Settings is enabled. Renderer policies do not modify shared meshes or renderer assets.
- Mesh/texture reuse for characters that explicitly enable **Inherit Generator Reuse Policy**. Existing per-character reuse choices remain authoritative by default.
- Generator dependency assets and editor-preview settings for specialized configurations.

The character's **Inherit Generator Quality** controls its detail/LOD/renderer policies and automatic rebuild enrollment. It does not exempt that character from generator-wide atlas/build settings. UMA's normal character generation uses its indexed generator; use a separate generation setup when a character requires a different global build policy.

A mesh-combiner override should reference a component on a dedicated prefab. The quality manager instantiates it under the generator, reuses that instance while needed, and destroys owned instances when its quality scope ends. It never runs a combiner on the prefab asset itself.

## Runtime switching and safety

The controller watches Unity's quality level and resolves its name again when it changes. For scripted changes:

```csharp
QualitySettings.SetQualityLevel(targetQualityIndex);
// The controller notices on its next update, or apply immediately:
qualityController.Refresh();

// A fixed profile bypasses the mapping:
qualityController.SetProfile(mobileProfile, GeneratorQualityRebuild.Gradual);
// Return to platform/quality mapping:
qualityController.SetProfile(null, GeneratorQualityRebuild.Gradual);
```

Settings wait until the current incremental/synchronous build and outstanding GPU atlas copies finish. Existing work is not cancelled merely to change quality. Rapid requests coalesce to the newest effective settings. A change requested inside a build callback cannot replace dependencies halfway through that build.

New Builds Only leaves existing output alone until the character is built again. Gradual enqueues at most Rebuilds Per Frame characters each frame, then the normal generator budget processes them. Immediate starts synchronous character rebuilds at the next safe boundary and can hitch; asynchronous texture uploads may still finish later. Scheduling-only edits do not rebuild characters. Removing an override uses its last selected rebuild mode to restore existing characters too.

Profiles and source materials/textures remain unchanged. Generated texture keys include effective dimensions, mipmaps, compression and sampling; profile names/IDs are not cache identity. Equivalent output from different profiles can share. Different output cannot reuse an incompatible texture. Old shared outputs remain alive until their owners release them; switching quality does not flush the resource cache.

## Runtime compression

Compression is explicitly opt-in, not a promise of background compression. It uses Unity's runtime Texture2D compression and can add main-thread work. Only supported block-aligned 8-bit RGBA converted outputs are eligible; persistent RenderTextures, HDR channels, unsupported devices and incompatible dimensions stay uncompressed. Source/imported textures are not changed. Benchmark on each target device before enabling it, especially for characters generated during gameplay. Fast and High Quality select the encoder quality, not the atlas resolution.

## Validation

`GeneratorQualityTests` exercises mapping precedence, serialization/inheritance, layered restoration, disabled controllers, hardware scaling, deferred active builds and readbacks, gradual restoration, character/renderer policies, texture output identity and source immutability. Existing atlas/mesh reuse and incremental-generator suites cover the unchanged build backends. These tests do not replace device-specific memory/performance measurement or checking LOD appearance in your game.
