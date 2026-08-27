# UMA 3 Dismemberment

UMA Dismemberment is a Unity 6.3+/UMA 3 runtime mesh slicer. It partitions every affected UMA renderer by skeleton influence, closes valid cut boundaries, creates a detached skeleton and renderer set, and leaves UMA-owned meshes untouched. Runtime-owned source clones are restored before UMA regenerates the avatar.

Use these documents according to the task:

- [Artist Setup and Production Guide](ARTIST_GUIDE.md) - complete setup, mesh authoring, cap materials, physics, gameplay integration, extension points, and troubleshooting.
- [Sample Scene Walkthrough](Samples/README.md) - what the supplied scene demonstrates and how to copy it into another scene.
- [Cap Material Notes](Samples/Materials/README.md) - the supplied cross-pipeline test material and replacement guidance.
- [Surface Bleeding Design](BLEEDING_FLUID_SYSTEM_PLAN.md) - the preserved architecture,
  performance contract, implementation phases, and acceptance criteria for runtime fluid decals.

## Quick setup

1. Add `UmaDismemberment` to the same GameObject as `DynamicCharacterAvatar`.
2. Assign `Samples/Materials/SliceFill` or a project cap material to **Fallback Material**.
3. Add the supported cuts to **Sliceable Human Bones, Cap UV and Physics**.
4. Tune each row's threshold and cap UV mapping against the final body and wardrobe.
5. If the detached part needs physics, assign its `UMAPhysicsElement` assets and enable **Ragdoll Dismembered Parts** on an `ExampleDismemberCallback` or equivalent listener.
6. Enable **Ragdoll Main Body** only on cuts that should incapacitate the surviving character. The character must already have a configured `UMAPhysicsAvatar`.
7. Call `TrySlice` only after UMA has generated the character and `IsReady` is true.

```csharp
using UMA.Dismemberment;
using UnityEngine;

if (dismemberment.IsReady &&
    !dismemberment.TrySlice(HumanBodyBones.LeftLowerArm,
        out UmaDismemberment.DismemberedInfo piece,
        out string failure))
{
    Debug.LogWarning(failure);
}
```

Use the `Transform` overload for generic rigs. Prefer `TrySlice` over the legacy `Slice` overloads because it returns a useful failure message.

## Per-cut decisions

Every humanoid row can control:

- **Threshold** - accumulated selected-subtree weight needed to move a triangle to the detached side.
- **Cap UV Mapping** - physically tiled UVs or a centered, non-tiling `0..1` fit for cross-section textures.
- **Detached Physics Mode** - Automatic, None, Rigid, or Articulated Ragdoll.
- **Trim Detached Rig** - compacts renderer bone palettes and removes cloned branches outside the retained rig.
- **Ragdoll Main Body** - activates the source character's existing UMA ragdoll after a successful cut.
- **Detached Physics Definitions** - one or more UMA physics assets used to build colliders and joints on the detached piece.

`Automatic` makes a single-definition piece rigid and a multi-definition piece articulated. Detached weights are always sanitized to the cut bone and its descendants, preventing a simulated arm or leg from stretching back toward the shoulder, spine, or hips.

When the sample callback successfully adds colliders to a detached piece, it disables the source `UMAPhysicsAvatar` colliders on that cut bone and its descendants. This prevents the invisible source limb colliders from continuing to collide as part of the main ragdoll. Their original enabled states are restored by Undo, reset, component shutdown, or UMA regeneration; unrelated gameplay colliders are not changed.

## Completion, undo, and ownership

`DismembermentCompleted` is the preferred extension point. It reports the detached root and cloned target bone, the corresponding source target bone, stable UMA bone hash, humanoid bone when available, all detached renderers, all modified source renderers, and whether main-body ragdoll was requested and activated. The legacy two-transform event is optional.

For a complete gameplay reset, use:

```csharp
if (!dismemberment.TryUndoDismemberment(out string failure))
    Debug.LogWarning(failure);
```

Undo restores the source meshes, destroys every tracked detached root and its child physics/gore objects, clears repeated-cut state, exits the source ragdoll, and rebuilds the current UMA recipe. `ResetDismemberment` is a lower-level API that does not perform the complete avatar/ragdoll rebuild; use it only when a custom lifecycle explicitly needs that behavior.

The default **Destroy Detached Pieces** rebuild policy removes detached pieces when UMA begins a new generation. **Keep Detached Pieces** preserves them across source-avatar regeneration; each root has a `DismemberedPiece` owner that releases its generated meshes when the root is destroyed.

## Geometry behavior

- Modern `BoneWeight1` streams and more than four influences per vertex are supported.
- A triangle follows the detached side when any vertex's accumulated selected-subtree weight exceeds the threshold.
- Valid manifold boundaries receive opposing caps. Concave loops are ear-clipped; open or branched boundaries fail when **Require Closed Caps** is enabled.
- Coincident UV, hard-normal, submesh, and compatible UMA-slot seam vertices are matched geometrically. **Seam Weld Tolerance** defaults to `0.0001` meters (0.1 mm).
- Existing cap submeshes are reused on later cuts.
- UV0-UV7, bind poses, modern weights, blend shapes, renderer settings, property blocks, materials, and blend-shape weights are preserved.
- **Meter Scaled Tiled** is the backward-compatible UV mode. **Centered Fit** centers each cap loop at `(0.5, 0.5)` and fits it inside the configured padded unit square without tiling.

## Limitations

The source mesh must be CPU-readable and use triangle topology. Cuts follow existing triangle edges; this is a topology partition, not an arbitrary plane intersection. Closed caps require a manifold cut boundary. Renderers with `Cloth` are rejected because changed topology would invalidate the cloth coefficients. The operation uses Unity mesh and GameObject APIs and must run on the Unity main thread.

Editor tests cover modern weights, closed and concave cap reconstruction, centered UVs, seam-split and multi-renderer body/armor cases, strict boundary rejection, repeated cuts, rebuild cleanup, detached rig trimming, rigid/articulated physics construction, and full undo.

## Runtime surface bleeding and fading decals

Assign a `UMASurfaceFluidProfile` to **Surface Fluid Profile** on
`ExampleDismemberCallback` to add flowing, settling, and fading blood to the particle effect.
The callback adds `UMARuntimeSurfaceDecalController` when needed and passes the rich cut result to
`StartBleed`. The result now includes `cutSurfaces`, containing ordered source UV loops rather than
an approximation at the cut bone.

The controller never changes `DecalRTStampSlot` or its permanent replay callbacks. It retains the
generated atlas as an immutable base, draws active effects into an owned output, and restores the
original material texture when no effect uses it. Normal simulation and fading do not call
`BuildCharacter` or `ForceUpdate`.

```csharp
RuntimeDecalHandle blood = surfaceDecals.StartBleed(result, bloodProfile);
surfaceDecals.StopFlow(blood); // stop injecting; existing fluid continues to settle
surfaceDecals.FadeNow(blood);  // begin its fade immediately
surfaceDecals.Clear(blood);    // remove only this independent effect layer
```

Use `AddFadeableStamp` for a `DecalRTStampAsset` that has not already been baked into the current
base. `AddPreviouslyBakedFadeableStamp` is the explicit legacy migration path: it requests one clean
UMA rebuild, registers the stamp dynamically after `CharacterUpdated`, and performs all subsequent
fade/clear work without another rebuild.

A successful regular RT decal can also seed fluid without any dismemberment event. Capture
`LastStamp` immediately after `CreateDecalLayer`, because the next successful decal replaces that
static cache. `StartBleedFromDecal` does not draw the bullet again; it uses the cached target-UV
triangles and recorded projection radius to create a small central emitter whose physical width
comes from **Emission Radius Meters**. It intentionally does not multiply this source by the bullet
alpha because many wound textures have a transparent center. The hit data supplies the bounded
fallback on platforms without compute support.

```csharp
DecalRenderTexture.DecalLayerResult? decal = DecalRenderTexture.CreateDecalLayer(
    avatar, shotRay, bulletRadius, 0f, 0f, avatar.umaData, bulletOverlay, decalOptions);

if (decal.HasValue && decal.Value.success)
{
    DecalRTStampAsset woundStamp = DecalRenderTexture.LastStamp;
    UMARuntimeSurfaceDecalController fluid =
        avatar.GetComponent<UMARuntimeSurfaceDecalController>() ??
        avatar.gameObject.AddComponent<UMARuntimeSurfaceDecalController>();
    RuntimeDecalHandle wound = fluid.AddPersistentStamp(woundStamp);
    RuntimeDecalHandle bleeding =
        fluid.StartBleedFromDecal(woundStamp, bloodProfile, decal.Value);
}
```

`AddPersistentStamp` keeps the multi-channel wound visible in the owned compositor and rebinds it
after an UMA atlas rebuild. Its handle can clear the wound independently. The fluid handle controls
only the bleeding; use `StopFlow`, `FadeNow`, or `Clear` on that handle. The fluid profile's **Source
Overlay** controls the blood appearance, while the bullet overlay controls the fixed wound.

### Surface cuts between two points

`UMASurfaceCutSystem` raycasts the current posed skinned meshes and routes a cut over connected mesh
topology. The atlas cut has a dark center, pink side irritation, metric width, soft outside edges,
and tapered endpoints. A `UMASurfaceCutProfile` also chooses how many fluid emitters are distributed
along the route and which `UMASurfaceFluidProfile` they use.

```csharp
if (surfaceCuts.TryGetSurfacePoint(startRay, out SurfaceCutPoint start) &&
    surfaceCuts.TryGetSurfacePoint(endRay, out SurfaceCutPoint end) &&
    surfaceCuts.TryCreateCut(start, end, cutProfile,
        out SurfaceCutResult cut, out string error))
{
    RuntimeDecalHandle wound = cut.CutHandle;
    RuntimeDecalHandle bleeding = cut.BleedHandle;
}
```

Both points must be on one generated renderer/material. Disconnected topology and large atlas-seam
jumps fail with a diagnostic instead of drawing across unrelated UV islands. The visual cut and
fluid use independent handles, so gameplay can clear the cut or stop/fade its bleeding separately.

GPU simulation is one context per generated atlas/material, with a shared posed surface field and
an independent lower-resolution state layer per handle. Independent layers make `Clear` predictable
without erasing another cut. Unsupported compute platforms use a bounded world-space trail with a
shared material and `MaterialPropertyBlock`; that fallback also never rebuilds UMA.

`IndependentDetachedPiece` creates a separate GPU context by cloning only the detached renderer's
affected generated material and restoring its clean base channel textures before binding owned
outputs. `DismemberedPieceMaterialOwner` releases the clone with the detached root. Source and shared
routes do not pay that cost.
