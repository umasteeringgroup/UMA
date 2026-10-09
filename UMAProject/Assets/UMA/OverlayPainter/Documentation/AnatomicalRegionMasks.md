# Shared anatomical and garment-region masks

Use **Properties → Region Placement → Add Region Mask** on a wear, dirt, sweat, skin,
paint, fill, or path layer. Choose a named region, then tune its bone-relative envelope.
Add another region to include it in the same group: knees plus elbows covers both,
without revealing areas you painted out. Each member keeps its own size, offset and side.
The mask clips every output channel together, including metallic, roughness, normal
maps and Normal Control. It works with both UV and world/triplanar generators.

These regions come from the reconstructed skeleton and mesh positions; they do not
depend on the direction or rotation of the UV islands. Reopening a character captures
its current skeleton and baked pose again. Standalone slots use their stored bone
hierarchy in canonical character space, with the same additional preview rotation
as the mesh. Import-space mesh corrections are not reapplied to the bones.

## Included regions

| Region | Placement |
| --- | --- |
| Knees | Outer joint surface, away from the bend; 120-degree sector by default. |
| Elbows | Outer joint surface, away from the bend; 120-degree sector by default. |
| Armpits | Soft envelope below and inward from each upper-arm root. |
| Wrist Cuffs | Ring around each wrist, slightly toward the forearm. |
| Ankle Cuffs | Ring around each ankle, slightly toward the lower leg. |
| Collar | Neck ring. |
| Lips | Envelope around lip bones; head-relative estimate if no lip bones exist. |
| Shoulders | Soft envelope above each upper-arm root. |
| Waist | Hip-width envelope raised toward the waist. |
| Seat | Rear hip envelope. |

Knee and elbow direction is calculated from the upper segment, joint and lower segment.
When a joint is nearly straight, the mask uses the skeleton's reference anatomical
frame transported into the baked pose. This frame is derived from bone positions,
so imported Z-up bind poses work too. The default covers approximately the outer
third of the circumference, with soft edges; the inner bend stays protected.

## Envelope controls

- **Side:** Both, Left or Right for paired regions.
- **Envelope Size:** X scales lateral width, Y scales length along the bone, Z scales depth.
- **Envelope Offset:** Uses that same local frame, in multiples of the resolved segment length.
- **Around Bone:** Rotates the envelope's outward direction around the segment.
- **Edge Feather:** Controls the transition inside the envelope boundary.
- **Joint Coverage:** Controls knee/elbow circumference coverage; 120 degrees is one third.
- **Advanced: Bone Overrides:** Optional exact anchor, parent and child names for custom skeletons.
  Leave these blank for automatic matching. A misspelled override produces a diagnostic
  instead of silently using a different bone.

Enable **Show Envelopes in Scene** with Region Placement expanded to see outlines and
the outward direction. Loose garments may need larger width/depth than close-fitting
clothing. These controls describe anatomical placement, not recognition of a garment's
actual hem, neckline or tailoring; a short sleeve's cuff needs an envelope offset toward
the elbow. Bone directions cannot determine a unique bend plane for every straight
custom joint, so adjust Around Bone when the rig uses a different convention.

Missing required bones produce black coverage and a diagnostic in Properties. They
never silently apply the effect everywhere. Lip placement without facial bones is
explicitly identified as an estimate.

## Share tuned envelopes

Choose **Save Shared Region Profile...** or create an asset with
**Assets → Create → UMA → Overlay Painter → Region Profile**. Assign the same profile
to several layers. Dimensions, offsets, falloff and custom bone names come from that
profile; Side remains a per-layer choice. Editing the profile refreshes all layers using
it in the open workspace. Profile changes also invalidate the mask cache during
composition, so exports and regenerated output use the updated envelope.
The profile inspector provides collapsible regions, hides advanced bone names by default,
warns about duplicate entries, and can add missing default regions. If a selected region
is absent from a profile, Properties warns and shows the local fallback settings.
Use **Make Local Copy** to preserve the current envelopes while disconnecting that group
from its profile. Clearing the profile field also preserves the current settings.

For example, use Knees on an Edge Wear layer, Armpits on Wetness & Sweat, and Ankle Cuffs
on Dirtify. They can share one clothing profile while preserving their own generation
settings. No selection needs to be painted repeatedly. A saved material preset or
document keeps these region settings and references.

## Mask stack and hand corrections

Region Placement adds an **Anatomical Region** group to the layer's mask stack; it
preserves any existing mask. Regions inside a group use the maximum of their coverage
(a union), then the default Multiply blend restricts existing coverage once with the
combined result. Adding another region uses the last enabled group and its shared profile.
Groups collapse to keep the Properties panel manageable. Use **Remove First Region**
or **Remove Additional Region** to remove a member; the group's Remove button removes
the whole group.

Enable **Separate Mask Group** only when you want a separate restriction. Two separate
Multiply groups intersect: a knees-only group followed by an elbows-only group will
normally hide everything. Existing saved stacks retain their original blend behavior.
The same effect is available under **Mask Effects & Smart Masks → New Effect**.
Use **Add Region to This Group** there to extend a union. For advanced stack editing,
use Max to union separate groups, Multiply to intersect, or Subtract to protect a region.
For a union, put your other restrictions and **Painted Mask** after the region entries
so those restrictions apply to the whole union. White reveals; black hides.

The optional **OverlayPainter Examples** companion contains Standard Anatomical Regions,
Loose Garment Regions and one smart-mask recipe for each named region under
`Assets/UMA/OverlayPainterExamples/Region Masks`. Select a recipe in the mask's
**Recipe Asset** field and use Replace Effects or Append Effects. The main plugin
works without installing these examples. Regression tests are in the separate
OverlayPainter Tests companion.

## UV and geometry limits

Overlapping UV triangles use the union of all their region coverage, independent of
triangle order. Truly mirrored UVs share texture pixels: selecting only one physical
side cannot prevent the same pixels from appearing on the other side. Separate the
UVs or use separate materials/tiles if the two sides need different artwork.

Masks are cached per surface and settings at the compositing resolution, up to 4096
pixels per dimension. Region baking does not require an AO or thickness bake. The
skeleton snapshot belongs to the same frozen geometry as the painter preview;
moving the source avatar after opening the workspace does not alter that preview.

## Developer access

`TexturePaintAnatomicalMask.Resolve` returns envelopes and a diagnostic from a
`TexturePaintAnatomy` snapshot. `Build` rasterizes them onto a `ReconstructedSurface`;
`BuildRegions` unions several region settings in one rasterization. An anatomical
mask effect's `ResolveRegions` applies its shared profile and per-member Side choices.
The caller owns the texture returned by either Build method. `TextureSet.GetAnatomicalRegionMask` exposes
the set-owned cached mask for a region effect; callers must not destroy it. Capture
and texture creation use Unity objects and run on the main thread. Pure envelope
evaluation can run on worker threads after the snapshot is captured.

Generators normally need no special integration: the host's common layer mask applies
the region to all their channels. Generators which need anatomical coverage internally
can obtain a cached mask on the main thread and pass copied pixels through their normal
read-only input contract. Do not access live bone transforms or Texture2D APIs from a worker.
