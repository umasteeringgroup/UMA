# UMA Renderer Assets and Cloth

`UMARendererAsset` stores settings that UMA applies when it creates a `SkinnedMeshRenderer`. It also associates a renderer with optional UMA cloth properties.

Use renderer assets when different parts of a character need distinct renderer behavior, lighting settings, cloth components, or normal-recalculation options.

## Create a Renderer Asset

Use:

`Assets > Create > UMA > Rendering > Renderer Asset`

Assign it to:

- A `SlotDataAsset`
- A runtime slot override
- A DCA renderer manager
- The generator's default renderer asset

A slot-specific renderer asset takes precedence for content that must be separated from the default renderer configuration.

## Renderer Settings

A renderer asset can control:

- Renderer name
- GameObject layer
- Rendering layer mask
- Renderer priority
- Update When Offscreen
- Skinned Motion Vectors
- Motion Vector mode
- Dynamic occlusion
- Shadow casting
- Shadow receiving

Use these values intentionally. Splitting content across different renderer assets can prevent slots from combining into the same renderer.

## Build Options

When Burst support is enabled, a renderer asset can request normal and tangent recalculation with a chosen angle. It can also limit blendshape participation by name where supported.

Recalculation adds build work. Prefer correct source normals unless runtime deformation genuinely requires it.

## Create Cloth Properties

Create a `UMAClothProperties` asset and configure the Unity Cloth values that should be applied to the generated renderer.

Assign that cloth-properties asset to the `Cloth` section of the `UMARendererAsset`.

The renderer asset is the bridge between the generated UMA renderer and the cloth configuration. Cloth properties are not selected through the UMA Material.

## Assign Cloth to a Slot

1. Create a renderer asset for the cloth renderer.
2. Assign the `UMAClothProperties`.
3. Configure shadows, motion vectors, and offscreen updates.
4. Assign the renderer asset to every slot that should share that cloth renderer.
5. Ensure the source slot contains valid cloth skinning coefficients.
6. Build a character and inspect the generated Cloth component.

Slots with different renderer assets may produce separate generated renderers. That is expected when they need different cloth behavior.

## Multiple Cloth Areas

Use separate renderer assets when different garments need different cloth settings. For example, a cape and a skirt may require different constraints and motion behavior.

Be aware that every additional generated renderer and Cloth component adds runtime cost.

## Cloth Authoring Guidance

- Paint stable constraints near attachment points.
- Avoid large unconstrained regions with insufficient topology.
- Keep triangle quality suitable for simulation.
- Test the most extreme body DNA that the garment supports.
- Test fast animation, teleportation, and character disable/enable.
- Verify collision setup on the generated character.
- Profile on the target platform.

UMA combines the source cloth coefficients into the generated mesh. Changing topology after cloth data is authored can invalidate the mapping.

## Default Renderer Asset

The generator has a default renderer asset used when the character or slot does not specify another one.

Use the default for common settings. Create slot-specific renderer assets only for actual differences such as cloth, layers, motion vectors, or shadows.

See [UMAGeneratorSetup.md](UMAGeneratorSetup.md).

## Performance

Cloth can be expensive because it adds per-frame simulation after character generation.

For mobile and large crowds:

- Use cloth only on important characters.
- Reduce simulated vertex count.
- Disable unnecessary offscreen updates.
- Limit collision complexity.
- Use simpler motion on background characters.
- Consider a bone animator or shader motion when full cloth is unnecessary.

## Troubleshooting

### No Cloth component is created

Check that the slot resolves to the intended renderer asset, that the renderer asset has cloth properties, and that the source slot contains cloth coefficient data.

### Several garments merge when they should use different cloth

Assign different renderer assets so the generator creates separate renderer groups.

### Too many renderers are created

Several otherwise compatible slots use different renderer assets or material configurations. Consolidate settings where possible.

### Cloth explodes or collapses

Check scale, skinning, topology, constraints, collision, and the neutral fit. Confirm that the source cloth data still matches the slot topology.

### Recalculated normals add generation cost

Disable renderer-asset normal recalculation unless the visual result requires it, or move the correction into the source content.

## Related Guides

- [SlotDataAsset.md](SlotDataAsset.md)
- [ContentCreation.md](ContentCreation.md)
- [UMAGeneratorSetup.md](UMAGeneratorSetup.md)
- [BoneAnimators.md](BoneAnimators.md)
