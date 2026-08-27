# UMA 3 Dismemberment

This package is the Unity 6.3+/UMA 3 migration of the original UMA dismemberment sample. It slices every affected UMA renderer without modifying meshes owned by UMA, creates a detached skeleton and renderer set, and restores owned source meshes before UMA regenerates the avatar.

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
- Existing cap submeshes are reused on later cuts instead of adding a material slot each time.
- Vertex attributes, UV0-UV7, bind poses, modern weights, blend shapes, renderer settings, property blocks, materials, and blend-shape weights are preserved.
- Unity's physical convention is used: one unit is one meter. **Cap UV Meters Per Tile** defaults to `0.25`, or four texture tiles per meter before material tiling.

## Limitations

The source mesh must be CPU-readable and use triangle topology. Cuts follow existing triangle edges; this is a topology partition, not arbitrary plane intersection. Closed caps require a manifold cut boundary. Physics and colliders are application-specific and should be added in a completion-event listener; see the sample callback.

Editor tests cover modern weights, cap orientation/reuse, strict boundary rejection, multi-renderer slicing, and UMA rebuild cleanup.
