# UMA Decal System

Overview of the runtime decal workflow, data structures, and best practices.

## Components
- `DecalSlotBuilder`: Core API for projecting decals into a new `SlotDataAsset` built from a selection of triangles.
- `DecalRTStamp*`: RenderTexture-based stamping path (optional) with dilation and editor tooling.

## DecalSlotBuilder Highlights
- Projects a selection cylinder at a mesh hit point and selects triangles by radius, depth, and facing.
- Bakes a new `UMAMeshData` for a slot with UVs projected around the hit direction.
- Preserves blendshapes and cloth coefficients where possible by mapping back to source slots.
- Caches mapping from original combined vertex index to decal vertex index for incremental edits.

Key Options (`DecalBuildOptions`)
- `useHitNormalForProjection`: use surface normal as projection axis
- `cylinderDepth`, `backOffset`: axial selection parameters
- `layerMask`, `maxDistance`, `facingThreshold`

Editing/Updates
- `RemoveTrianglesFromLastDecal`: remove selected tri ordinals
- `ApplyAddRemoveToLastDecal`: add combined-triangle indices and/or remove ordinals, rebuilds mesh

Best Practices
- Keep `anisoLevel` on textures reasonable; set mipmap bias if decal textures are sharp
- Use `draw gizmos` to debug selection volume
- Always release temporary meshes with `UMAUtils.DestroySceneObject`

## RT Stamping Notes
- The RT stamp path supports alpha dilation and controlled sampling, ideal for paint-like decals
- Use provided editors under UMA/Decals/*
