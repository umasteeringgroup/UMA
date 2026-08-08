# SlotDataAsset

A `SlotDataAsset` is UMA's reusable mesh asset. Body sections, clothing, hair, accessories, and utility geometry all become slots before UMA can combine them into a generated character.

The asset stores captured mesh data rather than acting as a prefab renderer.

## Create a Slot

The recommended workflow is:

`UMA > Content Creation > Slots > Slot Builder`

1. Import a skinned FBX.
2. Assign its `SkinnedMeshRenderer` to `Slot Mesh`, or use batch processing.
3. Assign the destination folder.
4. Optionally assign a unified `Seams Mesh`.
5. Choose overlay, recipe, serialization, bone, UDIM, tangent, and LOD options.
6. Click `Verify Slot`.
7. Correct reported scale or UV problems.
8. Click `Create Slot`.

See [ContentCreation.md](ContentCreation.md).

`Assets > Create > UMA > Core > Custom Slot Asset` creates an empty custom asset, but it does not capture production mesh data for you.

## Update an Existing Slot

Enable **Find and update existing slot** in Slot Builder when a rebuilt FBX should refresh an established `SlotDataAsset` without moving it or creating a duplicate in the destination folder.

Slot Builder searches the project for the same UMA slot name. If an asset with that name also exists at the intended output path, that path is preferred. Otherwise, the matching asset can be updated wherever it currently resides. Duplicate UMA slot names are ambiguous; Slot Builder chooses deterministically, writes a warning to the Console, and reports the path it used. Rename duplicates before relying on this workflow.

For an existing non-UDIM slot, Slot Builder writes the newly captured mesh data into the existing asset. The asset identity and its existing authoring metadata remain in place, so recipes and other references continue to point to it. Mesh data, source-submesh information, generated LOD data, and other options produced by the current build are refreshed.

Use the update workflow when:

- An FBX was re-exported with corrected positions, normals, tangents, weights, or blendshapes.
- The slot asset already has recipe references or artist-authored metadata that should remain attached.
- Existing content must stay at its current project path.

Before updating, commit or back up the asset. A changed vertex count, order, triangle layout, submesh layout, or UV layout can invalidate Mesh Hide Assets, Mesh Modifiers, cloth coefficients, decals, and other indexed content even though the slot asset reference itself is preserved.

**Always recreate slots** is a different operation. When enabled, an asset at the intended target path is deleted and recreated instead of updated. It is disabled while **Find and update existing slot** is active. Use recreation when preserving the existing asset is not required, then verify references and metadata afterward.

After processing, inspect the **Slot Builder Results** window:

- **Replaced** is **Yes** when an existing slot asset was found and written instead of creating a new slot.
- **Written To** is the actual asset path, which may differ from the configured destination when project-wide name matching found an established slot elsewhere.
- **Inspect** opens the resulting asset and **Ping** locates it in the Project window.

Rebuild representative avatars after every in-place update and repeat all topology-dependent validation.

## Stable Slot Name

`slotName` is used by recipes, cross-compatibility mappings, LOD lookup, mesh hides, and Mesh Modifiers.

Choose a unique name and avoid changing it after content ships. The inspector's `Clear Legacy Name` action removes migration data only after old recipes no longer need it.

## Mesh Data

`UMAMeshData` contains:

- Positions, normals, and tangents
- UV sets and submeshes
- Bone weights and bind poses
- Bone hierarchy data
- Blendshapes
- Cloth coefficients
- Internal LOD triangle ranges

Use `View MeshData` to inspect captured data and `Validate` to detect incompatibilities.

Changing source topology and rebuilding the slot can invalidate Mesh Hide Assets, Mesh Modifiers, cloth data, and other vertex- or triangle-indexed assets.

## Material

Assign the `UMAMaterial` expected by the slot's overlays.

The material determines:

- Shader and template material
- Atlas or non-atlas behavior
- Texture channels
- Generated texture settings

Slots and overlays intended to combine need compatible material definitions.

See [UMAMaterial.md](UMAMaterial.md).

## Renderer Asset

A slot can assign a `UMARendererAsset` for renderer layers, shadows, motion vectors, cloth, and other generated-renderer settings.

Different renderer assets can split otherwise compatible slots into separate renderers.

See [RendererAssetsAndCloth.md](RendererAssetsAndCloth.md).

## Submeshes

`subMeshIndex` selects the captured submesh used by this slot. Batch Slot Builder can create separate slots from a multi-material source.

Verify every generated slot references the expected submesh and material. Do not assume mesh or material naming selected the intended part.

## Overlay Scale and Atlas Use

`overlayScale` controls the slot's default overlay scaling contribution.

`useAtlasOverlay` participates in atlas behavior. Keep it consistent with the material and recipe workflow.

Use generator and material settings for project-wide resolution policy rather than assigning arbitrary scale values per asset.

## Slot Group

`Slot Group` classifies slots that share a UV layout for systems such as decals.

Choose a group from UMA Settings or add a deliberate new group. Slots in one group should truly share the targeting layout expected by those tools.

## Races and Tags

Race and tag metadata supports:

- Recipe filtering
- Wildcard and swap-slot behavior
- Decal targeting
- Authoring tools
- Project-specific selection

Use consistent, documented tag names.

## Animated Bones

The Animated Bones selection tools preserve bones required for facial or secondary animation.

The inspector can filter and add common face areas such as:

- Eyes
- Eyelids
- Cheeks
- Lips
- Nose

Use `Unbaked Animated Bones` for bones that must remain available without being baked into generated motion.

Preserving unnecessary bones increases skeleton cost. Preserve only what animation and runtime components need.

## Blendshapes

The slot can contain live blendshape frames. The inspector can:

- Copy blendshapes
- Extract one blendshape to a Mesh Modifier
- Extract all blendshapes
- Bake selected blendshapes into new slot data

Use live blendshapes when weights change at runtime. Bake or extract permanent forms when the live frame is unnecessary.

See [MeshModifiers.md](MeshModifiers.md).

## LOD

The `LOD` foldout reports base triangle count and internal LOD ranges.

Slot Builder and Global Library tools can generate internal LOD ranges. `UMASimpleLOD` uses them to change triangle detail without the older full slot swap.

`maxLOD` can also allow the slot to disappear beyond a level.

Test combined body seams and clothing silhouette at every LOD.

See [UMASimpleLOD.md](UMASimpleLOD.md).

## Smooshing and Clipping

### Smooshable slots

Smooshing moves geometry to reduce clothing intersections according to the configured offsets and expansion.

Use `Save and Test Smoosh` to preview changes.

### Clipping-plane slots

A clipping-plane utility slot contributes clipping behavior rather than visible wearable geometry.

Configure its offset carefully and keep utility slots out of ordinary visual selections.

## Slot Events

A slot can expose lifecycle events through its assigned event object:

- Character begun
- Slot atlased
- DNA applied
- Slot processing begun
- Slot processed
- Character completed

Use these for specialized project behavior. Avoid expensive callbacks on every slot in a crowd.

## Slot Utilities

The inspector includes advanced utilities for:

- UV copying and generation
- Mirroring UVs
- Recalculating normals and tangents
- Conforming bind poses and vertices to a source slot
- Copying bone weights
- Copying normals
- Copying blendshapes
- glTF export

These operations modify production data. Work under source control and validate the character after each operation.

For small skinning corrections that are easiest to judge on an assembled, posed character, use [Weight Touchup](WeightTouchup.md).

## Cloth

Cloth coefficients are captured in the slot mesh data. The generated renderer receives cloth behavior through its renderer asset and assigned `UMAClothProperties`.

Topology changes require cloth data to be regenerated or verified.

## Runtime SlotData

At runtime, UMA creates a `SlotData` instance from the asset.

The instance adds:

- Overlay list
- Per-character scale and tags
- Alternate material and renderer overrides
- Mesh-hide masks
- Per-instance generation offsets and submesh information

Do not modify the shared `SlotDataAsset` when the change belongs to one generated character.

## Artist Validation Checklist

- `Validate` reports no mesh errors.
- The slot has the intended submesh.
- Material and overlay channels match.
- UVs are valid for the selected material type.
- Border normals and weights match neighboring body slots.
- Every weighted bone exists in the race.
- Blendshape names and frames are intentional.
- Mesh hides and modifiers still match topology.
- LOD ranges preserve boundaries.
- Renderer and cloth settings are correct.
- The slot and its recipe are indexed.

## Troubleshooting

### The mesh stretches toward the origin

The slot contains weights for a missing or incompatible bone.

### A seam appears between body slots

Rebuild using a unified seam-reference mesh and compare border positions, normals, tangents, UVs, and weights.

### Overlays do not appear

Check the slot and overlay UMA Materials, channel counts, and recipe overlay order.

### Mesh Hide Assets report incompatible triangle counts

The slot topology changed after the hide asset was authored. Rebuild the hide asset for the current slot.

### Blendshapes are missing

Verify they were captured into the slot and that the DCA enables blendshape loading.

## Related Guides

- [ContentCreation.md](ContentCreation.md)
- [OverlayDataAsset.md](OverlayDataAsset.md)
- [UMAMaterial.md](UMAMaterial.md)
- [RendererAssetsAndCloth.md](RendererAssetsAndCloth.md)
- [MeshHideAssets.md](MeshHideAssets.md)
- [MeshModifiers.md](MeshModifiers.md)
