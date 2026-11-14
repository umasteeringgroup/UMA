# UMA Renderer Assets and Cloth

`UMARendererAsset` stores settings that UMA applies when it creates a `SkinnedMeshRenderer`. It can also hold optional UMA cloth properties that tune the cloth on that renderer.

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

Recalculation adds build work, so prefer correct source normals unless runtime deformation genuinely requires it. The cost is lower than in earlier releases: the step is about five times faster, it is skipped when neither the mesh data nor the participating blendshape weights changed (a texture-only rebuild, for example), and with **Reuse Identical Meshes** and the jobified combiner an avatar keeps its own mesh when a rebuild would produce the same one. See [Atlas and mesh reuse](AtlasAndMeshReuse.md).

## Where Cloth Comes From

A slot carries cloth when it has cloth skinning coefficients (**Cloth Skinning Serialized** on the `SlotDataAsset`). They are copied from a Unity `Cloth` component on the source mesh when the slot is built. A generated renderer gets a Unity `Cloth` component whenever the slots built into it carry coefficients, with every mesh combiner.

The renderer asset's cloth properties only tune that cloth. Without them, the cloth runs on Unity's default Cloth settings. A renderer asset with cloth properties does not add cloth to slots that have no coefficients.

To remove cloth from a slot, clear all of its **Cloth Skinning Serialized** entries.

## Create Cloth Properties

Create a `UMAClothProperties` asset and configure the Unity Cloth values that should be applied to the generated renderer.

Assign that cloth-properties asset to the `Cloth` section of the `UMARendererAsset`.

The renderer asset is the bridge between the generated UMA renderer and the cloth configuration. Cloth properties are not selected through the UMA Material.

## Assign Cloth to a Slot

1. Make sure the source slot contains valid cloth skinning coefficients (build it from a mesh with a `Cloth` component).
2. Create a renderer asset for the cloth renderer.
3. Optionally assign `UMAClothProperties` to tune the cloth; without them Unity's defaults are used.
4. Configure shadows, motion vectors, and offscreen updates.
5. Assign the renderer asset to every slot that should share that cloth renderer.
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
- Consider a bone animator or shader motion when full cloth is unnecessary. For hair, the **UMA > Hair Control** component sways hair in the shader; see [Hair Animation](HairAnimation.md).

## Troubleshooting

### No Cloth component is created

Check that the source slot contains cloth coefficient data (**Cloth Skinning Serialized**). Cloth properties on the renderer asset are not needed for the component to be created; they only tune it.

### A renderer has cloth it should not have

One of the slots built into that renderer carries cloth coefficients. Give the garment that should not simulate its own renderer asset, or clear that slot's **Cloth Skinning Serialized** entries.

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
