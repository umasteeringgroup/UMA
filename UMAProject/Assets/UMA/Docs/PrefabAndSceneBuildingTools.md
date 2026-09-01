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

## Convert a Positioned Prefab to an Identity Root

Select one or more regular Prefab Assets or Prefab Variants in the Project window and choose `Assets > UMA > Convert
Positioned Prefab to Identity Root`. The same batch command is available at `UMA > Asset Management
> Convert Positioned Prefabs to Identity Roots`.

Use this for mounted-item Prefabs whose root stores the hand-fitting position, rotation, or scale.
For each non-identity Prefab named `Item.prefab`, the converter:

1. Copies the original asset to `Item_positioned.prefab`. This recovery copy receives a new GUID.
2. Rewrites `Item.prefab` at its existing path, so its original GUID remains unchanged.
3. Adds a new `Item` root with local position `(0,0,0)`, identity rotation, and scale `(1,1,1)`.
4. Places the unpacked old hierarchy beneath it as `Item_positioned`, retaining the old local
   transform and contents.

When the source is a Prefab Variant, the converter materializes its inherited hierarchy and applied
overrides before building the wrapper. `Item.prefab` therefore becomes a regular Prefab while
keeping the Variant asset's GUID. The recovery copy remains a Variant with a new GUID, so the
original base relationship is still available for reference or recovery.

The wrapper does not depend on the `_positioned.prefab` copy. Existing scene, recipe, and asset
references to the original Prefab GUID therefore continue to resolve to `Item.prefab`, while its
visible contents retain their mounting offset beneath the new root.

Before completing the conversion, the utility compares the Prefab-owned local identifiers for its
GameObjects and Components. A mismatch triggers an automatic restoration attempt instead of
silently breaking component overrides or serialized references. The utility also refuses to
overwrite an existing `_positioned.prefab`, skips roots already at identity, and does not convert
Model Prefabs or UI Prefabs with a `RectTransform` root.

The conversion is not registered with Unity Undo. Commit or back up the project first, convert one
representative mounted item, and verify its existing scene instances before processing a large
selection.

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
| Preserve a mounted Prefab offset beneath a Unity-compliant identity root | Convert Positioned Prefab to Identity Root |
| Turn scene geometry into a weighted UMA slot and optional wardrobe recipe | Scene Mesh Slot Builder |
| Create an UMA bone hierarchy on a scene object | Bone Builder |

## Verification

Inspect generated meshes, materials, normal import settings, prefab references, Global Library entries, and animation deformation. Generated assets should also pass release validation before packaging.
