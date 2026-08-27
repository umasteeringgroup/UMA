# UMA 3 Dismemberment

This package is the Unity 6.3+/UMA 3 migration of the original UMA dismemberment sample. It slices every affected UMA renderer without modifying meshes owned by UMA, creates a detached skeleton and renderer set, and restores owned source meshes before UMA regenerates the avatar.

For the complete artist workflow, mesh-authoring guidance, cap shader replacement, physics setup, and troubleshooting, see [UMA Dismemberment: Artist Setup and Production Guide](ARTIST_GUIDE.md).

## Setup

1. Add `UmaDismemberment` to the same GameObject as `DynamicCharacterAvatar`.
2. Assign `Samples/Materials/SliceFill` or your own fallback cap material.
3. If a render pipeline needs a different shader, add an exact `RenderPipelineAsset`/material pair to **Pipeline Slice Fill Overrides**.
4. Configure the global threshold or the per-bone **Sliceable Human Bones** list.
5. Call `TrySlice` after UMA has generated the character.

```csharp
using UMA.Dismemberment;

if (!dismemberment.TrySlice(HumanBodyBones.LeftLowerArm,
    out UmaDismemberment.DismemberedInfo piece, out string failure))
{
    Debug.LogWarning(failure);
}
```

Use the `Transform` overload for generic rigs. `DismembermentCompleted` returns every affected source and detached renderer; the legacy `DismemberedEvent` and `Slice` overloads remain available.

## UMA lifecycle and ownership

- The component listens to `CharacterBegun`, `CharacterCreated`, and `CharacterUpdated`.
- UMA-owned meshes are read only. Sliced source meshes and detached meshes are component-owned clones.
- The default rebuild policy destroys detached pieces and restores source renderers when generation begins. **Keep Detached Pieces** leaves pieces alive, but their meshes remain owned by `DismemberedPiece` and are released with it.
- Repeated-slice tracking uses stable UMA bone-name hashes and resets for each generation.
- All renderers returned by `UMAData.GetRenderers()` are considered. Renderers without affected geometry are left alone.
- `Cloth` renderers are rejected with a diagnostic because topology changes require rebuilt cloth coefficients.

## Geometry behavior

- Modern `BoneWeight1` streams are used, including more than four influences per vertex.
- A triangle follows the detached side when any vertex's accumulated selected-subtree weight exceeds the threshold. This preserves the original tool's selection behavior.
- Valid manifold boundaries receive opposing caps on the source and detached meshes. Concave boundaries are ear-clipped; open or branched boundaries fail when **Require Closed Caps** is enabled.
- Boundary detection geometrically welds coincident UV, hard-normal, submesh, and compatible UMA-slot seam vertices without changing the authored mesh streams. **Seam Weld Tolerance** defaults to `0.0001` meters (0.1 mm).
- When caps are required, a split that produces attached and detached triangles but no reconstructable boundary is rejected instead of silently leaving a hole.
- Existing cap submeshes are reused on later cuts instead of adding a material slot each time.
- Vertex attributes, UV0-UV7, bind poses, modern weights, blend shapes, renderer settings, property blocks, materials, and blend-shape weights are preserved.
- Each sliceable bone selects its cap UV mapping. **Meter Scaled Tiled** is the backward-compatible default and uses **Cap UV Meters Per Tile** (`0.25` by default). **Centered Fit** maps the cap's area centroid to `(0.5, 0.5)`, preserves its aspect ratio, and fits it inside the per-bone padded unit square without tiling.

## Limitations

The source mesh must be CPU-readable and use triangle topology. Cuts follow existing triangle edges; this is a topology partition, not arbitrary plane intersection. Closed caps require a manifold cut boundary. Physics and colliders are application-specific and should be added in a completion-event listener; see the sample callback.

Editor tests cover modern weights, cap orientation/reuse, strict boundary rejection, multi-renderer slicing, and UMA rebuild cleanup.
