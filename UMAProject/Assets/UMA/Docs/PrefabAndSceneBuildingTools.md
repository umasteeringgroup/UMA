# Prefab and Scene-Building Tools

These tools convert generated characters, scene meshes, or avatar definitions into reusable Unity assets. They have different goals and should not be treated as interchangeable exporters.

## Prefab Maker

Open `UMA > Content Creation > Prefabs > Prefab Maker`.

Prefab Maker converts a generated UMA avatar into a non-UMA prefab. The resulting object is inexpensive to reuse, but dynamic UMA functionality is removed.

Options include:

- `Unswizzle Normals`: writes normals in a conventional form and marks them appropriately on import.
- `Add Standalone DNA`: retains adjustable DNA through a standalone component, but still requires UMA runtime code.
- Mesh export mode: saved mesh assets, glTF package output, or FBX when the required exporter is installed.
- `Replace Existing UMA`: removes the source UMA from the scene and places the result.
- Prefab name and output base folder.

glTF output does not become the prefab's imported model automatically because Unity has no built-in glTF importer; the generated prefab still uses saved mesh assets.

## Save Character Prefabs

Select generated UMA avatars and choose `UMA > Content Creation > Prefabs > Save Character Prefabs`.

The command prompts for a prefab path and saves the generated character. It skips avatars already part of prefab instances. This is a faster, less configurable route than Prefab Maker.

## Scene Mesh Slot Builder

Open `UMA > Content Creation > Slots > Scene Mesh Slot Builder`.

This tool creates a `SlotDataAsset` from a mesh already present in the scene and maps it to a generated target DCA. It accepts skinned renderers or MeshFilter/MeshRenderer sources, can select or combine source submeshes, and can reweight vertices against the target surface.

- Normal mode transfers weights using the closest target surface.
- `Skip reweight` assigns a selected manual bone.
- Normal and tangent controls can clear or regenerate data.
- Output can be added to the UMA Global Library.
- Optional wardrobe creation generates a recipe and can reuse an existing overlay or create one from source material textures.

Generate the target DCA first and inspect target renderer selection carefully when a character has multiple renderers. Always test deformation across animations after automatic reweighting.

## Bone Builder

Open `UMA > Content Creation > Bones > Bone Builder`, use the DynamicCharacterAvatar context command, or choose the GameObject UMA menu command.

Bone Builder creates the race bone hierarchy for an UMA object without requiring a normal character generation pass. A non-DCA UMA object also requires a base recipe. `Remove UMAData` removes temporary generation data afterward and is normally recommended.

Do not run Bone Builder in Play Mode. It creates scene transforms, not a complete exported character or slot.

## Choosing a Tool

| Goal | Tool |
|---|---|
| Freeze a generated avatar into a configurable reusable prefab | Prefab Maker |
| Quickly save selected generated avatars | Save Character Prefabs |
| Turn scene geometry into a weighted UMA slot and optional wardrobe recipe | Scene Mesh Slot Builder |
| Create an UMA bone hierarchy on a scene object | Bone Builder |

## Verification

Inspect generated meshes, materials, normal import settings, prefab references, Global Library entries, and animation deformation. Generated assets should also pass release validation before packaging.
