# UMA Decals: An Artist's Workflow Guide

UMA supports two decal workflows. Both let you place artwork on a generated character at runtime, but they produce different kinds of results:

| Decal type | What the artist sees | Best uses | How it is built |
| --- | --- | --- | --- |
| **Slot Decal** | A small piece of geometry following the character surface | Bullet holes, wounds, raised scars, patches, or effects that benefit from their own mesh | UMA projects onto nearby triangles and creates a new runtime slot containing those triangles |
| **RenderTexture Decal** | Artwork painted into the character's generated texture atlas | Tattoos, makeup, dirt, blood, body paint, bruises, and other flat surface detail | UMA records the affected triangles and replays the artwork into matching overlay RenderTextures whenever the atlas is rebuilt |

Use a Slot Decal when the mark should behave like a small mesh attached to the body. Use a RenderTexture Decal when the mark should feel painted into the character's surface.

The **U3-Ragdolls and Shooting Example** scene uses Slot Decals for its shooting effects. The **U3-Decals** scene is the main place to study and test both workflows.

## The targeting rule to understand first

RenderTexture Decals use two pieces of artist-authored metadata to decide where a saved stamp is allowed to replay:

1. The destination `OverlayDataAsset` must have an `OverlayGroup` matching the stamp's **Target Overlay Group**.
2. The destination `SlotDataAsset` must match the recorded slot by `SlotGroup`, or by `slotName` when the slot has no `SlotGroup`.

Both comparisons are exact and case-sensitive. `Skin`, `skin`, and `SKIN` are different names.

Think of these fields as an address:

- `OverlayGroup` answers **which kind of surface may receive the artwork?**
- `SlotGroup` answers **which equipped body or clothing region did the artist paint?**

For example, a stamp recorded on a face can have this address:

- Target Overlay Group: `Skin`
- Slot Group: `Face`

It can then replay on a different compatible face slot carrying the `Face` SlotGroup, but it will not replay on a `Body` slot even if that slot also contains a `Skin` overlay.

### OverlayGroup requirements

`OverlayGroup` is a field on an `OverlayDataAsset`. Add the chosen group to every destination overlay that should be eligible to receive the RenderTexture Decal.

For a skin decal workflow, a typical setup is:

| Destination overlay | OverlayGroup |
| --- | --- |
| Face skin/base overlay | `Skin` |
| Torso skin/base overlay | `Skin` |
| Hands skin/base overlay | `Skin` |
| Legs skin/base overlay | `Skin` |

Assigning the same `Skin` group to these overlays means they belong to the same artistic surface family. It does **not** mean that every skin slot receives every stamp; the SlotGroup or slot name provides the second part of the address.

The group must be on the actual overlay asset used by the character recipe. Entering `Skin` only in `CreateDecal.TargetOverlayGroup` is not enough. If the equipped overlay has a blank group, or a different group, it will not receive the stamp.

The **Source Decal Overlay** supplies the artwork. It is separate from the destination overlay and does not have to use the destination group. Set **Target Overlay Group** explicitly in `CreateDecal` so the intended destination is clear. The **Use Source Overlay Group** button is convenient only when the source and destination deliberately share the same group.

### SlotGroup requirements

`SlotGroup` is a field on a `SlotDataAsset`. Give each slot region a stable, descriptive identity such as `Face`, `Torso`, `Hands`, `Legs`, or `Feet`.

In the SlotDataAsset Inspector, type a new value into **Slot Group** and click **Add** to register it in the shared UMA Settings pick list. After that, the same name can be selected from the dropdown on other equivalent slots. **Clear** removes the value from the current slot; it does not rename other slots.

A SlotGroup must be unique among slots that can be equipped at the same time. This prevents one recorded face stamp from also matching another simultaneously equipped slot.

It is safe for alternative slots to share a group when only one alternative can be equipped at a time. For example, several mutually exclusive face slots can all use `Face`:

- Human male face: `Face`
- Human female face: `Face`
- Elf face: `Face`
- An LOD replacement for any of those faces: `Face`

Only one face is equipped, so `Face` still identifies one current destination. In contrast, do not give both a simultaneously equipped head slot and torso slot the `Body` SlotGroup. Use distinct values such as `Face` and `Torso`.

When a SlotGroup is blank, the decal system falls back to the exact `slotName`. That works, but it ties a saved stamp to that specific asset name. A well-planned SlotGroup lets equivalent or replacement slots share stamps.

> `OverlayGroup` and `SlotGroup` are not UMA tags. Tags remain useful for search, wardrobe rules, and other classification, but decal replay uses the group fields described above.

## Prepare decal artwork

The same source-art principles apply to both decal types.

1. Paint the decal on a transparent canvas. Keep the background fully transparent unless it is intentionally part of the mark.
2. Give the artwork enough transparent padding for filtering and mipmaps. Tight cropping can create colored fringes or clipped edges.
3. Clean nearly transparent pixels around the silhouette. Hidden RGB color in those pixels can produce halos after atlas filtering.
4. If the decal has multiple material channels, prepare every required texture at matching dimensions. For example, a color texture and a normal texture must line up perfectly.
5. Import color artwork using the appropriate color settings and import normal artwork as a normal map. Check the result in the project's active render pipeline and color space.
6. Create or duplicate an `OverlayDataAsset` for the decal artwork. Assign the textures in the same channel order expected by its `UMAMaterial`.
7. Give the overlay a unique, useful name such as `Decal_BulletHole`, `Decal_FaceTattoo_01`, or `Decal_MudSplash`.

Duplicating a known compatible overlay is often the quickest starting point because its `UMAMaterial`, channel list, and blend configuration already match the character material. Replace its textures and give the new asset a unique overlay name. Do not change a shared production overlay merely to turn it into a decal source.

For a simple color decal, the color texture's alpha can define the visible shape. An explicit alpha mask may also be assigned on the `OverlayDataAsset`. Check every material channel: a missing source texture for a channel is skipped, while an incorrectly ordered texture can put color or normal data into the wrong target.

## Common CreateDecal scene setup

Before choosing a decal type, prepare the placement tool:

1. Add or select a GameObject containing `CreateDecal`. The **U3-Decals** scene already provides a working example.
2. Assign **Avatar** to the `DynamicCharacterAvatar` that will receive the decal.
3. Assign **Orbit Camera** to the camera used for placement and inspection.
4. Enter Play Mode and wait for the UMA character to finish generating before placing a decal.
5. Use the Inspector or the runtime panel to choose **Slot Decal** or **RenderTexture**.
6. Set **Decal Radius** to the intended world-space size. Start small and increase it until the desired surface triangles are included.
7. Use **Fudge Radius** sparingly to catch triangles at the boundary of the projected area.
8. Set a rotation, or enable **Randomize Rotation** when variation is desirable.
9. Leave **Use Hit Normal for Projection** enabled for most curved body surfaces. Disable it only when a consistent camera/ray projection gives a better artistic result.

The placement camera uses the UMA character's generated mesh for its hit test, so placement follows the currently generated body and wardrobe rather than a separate proxy collider.

Runtime navigation is:

- Left click: place a decal.
- Right-click drag: orbit the camera.
- Shift + right-click drag: move the orbit focus vertically.
- Mouse wheel: zoom.

Clicks over the runtime controls are ignored for placement.

## Creating a Slot Decal

Slot Decals create a small skinned mesh from the character triangles beneath the brush. The new geometry uses the assigned decal overlay and is merged into the current UMA recipe. Because it is geometry, it can retain relevant skinning, blendshape, and cloth information from its source where mappings are available.

### Artist setup

1. Prepare a decal `OverlayDataAsset` as described above.
2. Make sure its `UMAMaterial` is compatible with the surface and shader you are targeting.
3. On `CreateDecal`, set **Decal Method** to **Slot Decal**.
4. Assign the artwork overlay to **Decal Overlay**.
5. Set **Slot Offset**. This pushes the generated decal mesh slightly along its normals to prevent it from occupying exactly the same depth as the skin.
6. Tune **Decal Radius**, **Fudge Radius**, projection, and rotation.
7. Enter Play Mode and left-click the character to place the decal.

If the mark flickers or disappears at certain angles, it is probably competing with the body surface. Increase **Slot Offset** gradually. If the decal visibly floats above the character, reduce the offset. Test extreme poses as well as the neutral pose.

### What happens when it is placed

`DecalSlotBuilder` finds the closest visible triangle under the pointer, selects nearby forward-facing triangles within the projection volume, and builds a new runtime `SlotDataAsset`. `CreateDecal` adds the decal overlay to that slot, applies the normal offset, merges the slot into the current recipe, and rebuilds the avatar.

A click that produces no decal usually means that no triangles met the radius, depth, or facing requirements. Increase the radius slightly, move the camera to face the surface more directly, or compare the result with **Use Hit Normal for Projection** enabled and disabled.

### Editing the last Slot Decal

Enable **Triangle Debug** to refine the last placed decal:

- Left click a triangle to toggle it. Red triangles are marked for removal; green triangles outside the current decal are marked for addition.
- Shift + left-click drag to paint the selection state across triangles.
- Ctrl + left-click drag to move the decal texture by adjusting the selected overlay UVs.
- Use **Select All**, **Invert**, and **Clear** for broad changes.
- Use **Undo** and **Redo** while refining the selection.
- Adjust rotation and scale in the edit panel.
- Choose **Apply Changes**, then exit edit mode when the silhouette is clean.

Triangle addition is supported for Slot Decals. It is not supported for RenderTexture Decals.

### Saving and production use

The `CreateDecal` placement path creates Slot Decals at runtime and merges them into the live recipe. This is ideal for impacts, wounds, and gameplay effects. They are not automatically converted into permanent project assets by the placement click. If a game must preserve them across sessions, its save system must preserve or reconstruct the generated decal/recipe state.

Open **U3-Ragdolls and Shooting Example** to see Slot Decals used as gameplay marks. Study its overlay, material, placement size, and offset, then substitute your own source artwork.

## Creating a RenderTexture Decal

RenderTexture Decals paint source overlay textures into UMA's generated atlas textures. The visible result becomes part of the character's composited surface, so it follows animation without adding visible mesh geometry.

This workflow has three kinds of assets:

- A **Source Decal Overlay**, containing the artwork to paint.
- One or more existing **destination overlays**, marked with the target `OverlayGroup`.
- A `DecalRTStampAsset`, containing the selected triangles, projected UVs, target OverlayGroup, and slot identity needed to replay the mark.

A `DecalRTStampSlot` stores the stamp references and listens for UMA atlas rebuilds. A small utility `SlotDataAsset` installs that component with the character recipe so the listener exists whenever the avatar is generated.

### Step 1: Prepare the destination assets

Do this before placing any stamps:

1. Decide which surface family the decal targets. For skin artwork, a clear choice is `Skin`.
2. Open every destination `OverlayDataAsset` that should receive this family of decals.
3. Set its **OverlayGroup** to the exact same value, for example `Skin`.
4. Confirm those overlay assets are actually present on the relevant slots in the recipe.
5. Open the corresponding `SlotDataAsset` assets and assign stable SlotGroups such as `Face`, `Torso`, `Hands`, and `Legs`.
6. Check the complete equipped character and make sure no two simultaneously equipped slots use the same SlotGroup.
7. Confirm the destination `UMAMaterial` uses composited texture channels. `UseExistingMaterial` and `UseExistingTextures` slots are not RenderTexture stamp targets because UMA does not build the required composited atlas RenderTextures for them.

For example:

| Equipped slot | SlotGroup | Overlay on that slot | OverlayGroup |
| --- | --- | --- | --- |
| Human face | `Face` | Human face skin | `Skin` |
| Human torso | `Torso` | Human torso skin | `Skin` |
| Human hands | `Hands` | Human hands skin | `Skin` |
| Shirt | `Shirt` | Cotton shirt base | `Cloth` |

A stamp recorded on `Skin` + `Face` will not affect the torso or shirt. A shirt paint stamp could instead target `Cloth` + `Shirt`.

### Step 2: Prepare the source decal overlay

1. Create an `OverlayDataAsset` containing the decal artwork.
2. Assign a `UMAMaterial` whose channel property names correspond to the destination channels you want to paint.
3. Populate its texture list in that material's channel order.
4. Assign an alpha mask when the color texture's alpha is not the desired coverage mask.
5. Add the asset to the UMA Asset Indexer if it is not already indexed, so a saved stamp can resolve the source by name when necessary.

The source overlay provides the pixel content; it is not itself the destination. A face tattoo source can therefore be named and grouped for organization however you prefer, while `CreateDecal.TargetOverlayGroup` explicitly says `Skin`.

### Step 3: Configure CreateDecal

1. Set **Decal Method** to **RenderTexture**.
2. Assign the artwork asset to **Source Decal Overlay**.
3. Enter the exact destination group in **Target Overlay Group**, such as `Skin`.
4. Assign a `DecalRTStampSlot` component to **Stamp Slot**.
5. Keep **Draw RenderTextures Immediately** disabled if it appears as a legacy option. The normal UMA rebuild and replay path is the reliable saved-stamp workflow.
6. Leave **RT Dilation** and **RT UV Expand** at their defaults in the current implementation. See [Edge coverage and dilation](#edge-coverage-and-dilation) for the present limitations of these controls.
7. Choose a **Rebuild Method**. `ForceTextures` is the normal starting point because the decal changes the generated textures rather than the character shape.

The `CreateDecal` Inspector reports missing source textures, materials, groups, and stamp-slot references. Resolve its red setup messages before entering Play Mode.

### Step 4: Prepare the live stamp holder

Add `DecalRTStampSlot` to a clean scene GameObject and assign that component to `CreateDecal.StampSlot`. During the current Play Mode session, `CreateDecal` uses this component to collect the generated stamp references and subscribe to the avatar's atlas events.

Treat this scene component as the working artist's palette. After the stamps look right, the save tool copies its configured contents into a prefab and utility slot for production use.

### Step 5: Place and edit stamps

1. Enter Play Mode and wait for the avatar textures to finish generating.
2. Confirm **Source Decal Overlay**, **Target Overlay Group**, and **Stamp Slot** in the runtime panel.
3. Set radius, rotation, and projection.
4. Left-click the intended surface.
5. `CreateDecal` creates a `DecalRTStampAsset`, adds it to the working stamp slot's `AutoRTDecals` set, and rebuilds the character textures.
6. In the Editor, generated stamp assets are created under `Assets/UMA/GeneratedDecalStamps`.
7. Refine the current stamp with Triangle Debug, rotation, scale, and UV movement before creating the production utility slot.

If the brush crosses a boundary between two slots, the stamp asset can contain one `SlotStamp` entry for each affected slot. Each entry keeps only that slot's triangles and identity. This is useful for a continuous mark across a seam, but it can be surprising if the brush was intended to stay entirely on one region. Use a smaller radius or edit away unwanted triangles.

The **Clear Stamp Assets** button removes all stamp references from the assigned `DecalRTStampSlot` after confirmation. It does not delete the `DecalRTStampAsset` files from the project.

### Step 6: Save the configured stamp set as a utility slot

Do this while the working `DecalRTStampSlot` contains the stamps you want to keep:

1. In the `CreateDecal` runtime panel, enter a unique **Slot Name**, for example `FaceAndBodyDecalStampManager`.
2. Click **Generate and Save a Slot**.
3. Choose a folder inside the project's `Assets` folder.
4. The tool saves a prefab copy of the working GameObject, including its `DecalRTStampSlot` and current stamp references.
5. It also creates a utility `SlotDataAsset` whose `SlotObject` references that prefab.
6. The utility slot's **Character Begun** event is connected to `DecalRTStampSlot.OnCharacterBegun` automatically.
7. Add the generated utility slot to the avatar's base recipe or to a wardrobe recipe that is always equipped when decals are needed.
8. Save the generated prefab, slot, stamp assets, and any recipe that references the utility slot before leaving the authoring session.

The utility slot does not need visible geometry. Its purpose is to carry the configured stamp component and its UMA event hookup into every generated character that should display those decals.

If you create the utility slot before the final stamps have been added, generate it again after editing or manually update the saved prefab's stamp references. The production prefab must reference the final `DecalRTStampAsset` files; a temporary Play Mode scene reference is not enough.

### Editing a RenderTexture stamp

Enable **Triangle Debug** after placement to edit the current stamp:

- Mark red triangles for removal.
- Shift-drag to paint the removal selection.
- Ctrl-drag to reposition the source artwork over the selected triangles.
- Adjust rotation and scale in the edit panel.
- Apply the changes and rebuild the textures to inspect the final atlas result.

Green triangle addition is intentionally unavailable for RenderTexture stamps. If the original stamp missed a large region, it is usually faster and cleaner to adjust the radius or camera angle and place it again.

## Art direction and tuning

### Radius and topology

The brush selects triangles, not pixels alone. On a dense face mesh, a small radius can create a smooth silhouette. On a low-density mesh, the same radius may produce a visibly angular edge. Judge the result on the final production topology and LODs.

A larger radius also increases the chance of crossing a seam or touching a nearby slot. Inspect the saved stamp's slot list when a mark appears in an unexpected region.

### Rotation and projection

Rotation is applied around the projected surface direction. Use deliberate rotation for symbols, lettering, makeup, and tattoos. Random rotation works well for dirt, scratches, blood spatter, and repeated impacts.

**Use Hit Normal for Projection** usually gives the most natural result on rounded anatomy. A view-aligned projection may be useful when placing a designed image from a carefully framed camera, but can distort around steep side surfaces.

### Edge coverage and dilation

RenderTexture atlases need a little coverage beyond visible UV island edges so filtering does not reveal dark seams. The current replay path expands stamped triangles by a fixed 0.75 pixels.

- **RT UV Expand** is exposed in `CreateDecal`, but that value is not currently stored in `DecalRTStampAsset`; replay uses the fixed 0.75-pixel value.
- **RT Dilation** records the requested bleed amount with the stamp for an optional final dilation pass. The final dilation call is currently disabled, so changing this value alone does not alter the replayed result.

Do not try to repair a source-art halo by raising these controls. Clean the transparent edge colors and padding first. If the fixed expansion causes bleeding into a neighboring UV island, the atlas needs more island spacing or the replay implementation needs a project-specific expansion adjustment. Always inspect the character at several mip levels and camera distances.

### Test the character, not only the texture

Review decals under the same lighting, shaders, animation, DNA extremes, wardrobe combinations, atlas resolution, and LOD settings used by the project. A clean flat texture preview does not reveal stretching around joints, UV seams, z-fighting, or group collisions.

## Troubleshooting

### A RenderTexture stamp asset is created, but nothing is visible

Check these in order:

1. The exact **Target Overlay Group** is present on the destination `OverlayDataAsset` used by the equipped recipe.
2. Capitalization matches exactly.
3. The destination overlay is actually on the clicked slot.
4. The clicked `SlotDataAsset` has the expected SlotGroup, or its exact slot name matches when SlotGroup is blank.
5. The source overlay contains a visible texture and usable alpha/mask.
6. The source overlay's `UMAMaterial` contains the same channel property being composited on the destination.
7. The destination material creates composited atlas RenderTextures and is not using `UseExistingMaterial` or `UseExistingTextures`.
8. A `DecalRTStampSlot` is assigned and its utility slot is equipped.
9. The utility slot's **Character Begun** event calls `DecalRTStampSlot.OnCharacterBegun`.
10. Force a texture rebuild after correcting the assets.

### A stamp appears on the wrong slot or on several slots

- Look for duplicate SlotGroups on slots equipped at the same time.
- Give those slots unique groups, such as `Face`, `Torso`, `Hands`, and `Legs`.
- Check whether the original brush radius crossed a slot seam and intentionally recorded multiple slot entries.
- Recreate stamps after changing the intended slot identity so each saved `SlotStamp` records the new group.

### A stamp works on one character but not an alternative character

- Give equivalent, mutually exclusive destination slots the same SlotGroup.
- Give their eligible destination overlays the same OverlayGroup.
- Ensure their materials expose compatible channel property names.
- Confirm their UV layout is suitable for the recorded projected region. Group matching allows an equivalent slot to be found; it cannot make unrelated topology or UVs visually identical.

### The decal has dark or bright fringes

- Clean transparent-edge RGB in the source texture.
- Add transparent padding around the artwork.
- Confirm the alpha mask is the intended texture.
- Inspect color-space and texture import settings.
- For RenderTexture Decals, remember that replay currently uses a fixed 0.75-pixel expansion and does not run the optional final dilation pass. Correct source padding and atlas island spacing first.

### A Slot Decal flickers or floats

- Increase **Slot Offset** to resolve z-fighting.
- Reduce it if the decal separates visibly from the surface.
- Test animated poses and DNA extremes before finalizing the value.

### A decal is distorted or selects the far side of a thin area

- Reduce radius and fudge radius.
- Face the camera more directly toward the desired surface.
- Toggle **Use Hit Normal for Projection** and compare the result.
- Use Triangle Debug to remove unwanted triangles.

## Final artist checklist

Before shipping a decal set, verify:

- The source art has clean transparency, sufficient padding, and correctly aligned material channels.
- The decal overlay uses a compatible `UMAMaterial` and unique overlay name.
- Every RenderTexture destination overlay has the intended exact `OverlayGroup`.
- Every relevant destination slot has a stable `SlotGroup`.
- SlotGroups are unique among all slots that can be equipped simultaneously.
- Mutually exclusive alternatives reuse a SlotGroup only when they represent the same artistic region.
- The RenderTexture utility slot is present in the character recipe and its event is wired.
- Saved stamp assets are referenced by the production `DecalRTStampSlot`.
- Slot Decal offset has been tested for z-fighting and floating.
- Radius, projection, seams, animation, DNA variation, wardrobe combinations, atlas resolution, and LODs have all been reviewed in context.

Once the group names are planned correctly, the workflow becomes predictable: `OverlayGroup` selects the surface family, `SlotGroup` selects the equipped region, and the source overlay supplies the art.
