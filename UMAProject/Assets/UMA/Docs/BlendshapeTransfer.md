# Blendshape Transfer

**UMA > Tools > Slot Tools > Transfer Blend Shapes** gives slots the blendshapes of a race's body, so clothing follows the body when a shape is dialled in. It works on any number of slots at once and shows the result in the Scene view before anything is written.

Use it when a race's body has a blendshape (a build, a chest or belly shape, a face shape under a mask or helmet) and a slot that sits on that body does not: without the shape the slot stays where it was and the body pokes through it.

You can also open it on a selection: select slots or wardrobe recipes in the Project window, right-click, **UMA > Transfer Blend Shapes To Selection**.

## How it works

Everything is measured in *avatar space*: each slot is placed the way a character of the race wears it at rest, whatever pose or scale it was exported in.

1. **Bind.** Every vertex of a target slot is bound to the nearest point on the source surface (the triangle it lies over, and where on that triangle). Surfaces that face the same way as the vertex are preferred, so the inside of one leg is not bound to the other leg.
2. **Follow.** For each blendshape the vertex takes the movement of its bound point, blended between the triangle's corners. With **Follow the surface** its offset from the body also turns with the skin, so cloth keeps its distance where the shape tilts the skin.
3. **Fade.** The movement fades with distance from the body: full strength close to the skin, none beyond the fade distance. A cape hem or a hat brim stays put.
4. **Smooth.** The movement is smoothed over the slot's own surface, which removes the steps where neighbouring vertices are bound to different parts of the body.
5. **Keep clear.** A vertex that would end up under the moved body is pushed back out.
6. **Write.** The movement is written as a blendshape of the same name, in the slot's own mesh space, with normal and tangent deltas worked out from how the shape bends the slot. Every frame of a multi-frame shape is transferred.

Vertices that share a position (UV seams, hard edges) always move together, so seams stay closed. A shape that does not move a slot is not added to it: boots do not get a nose shape.

### Legacy slots

A slot built for the race's **Legacy Rig** (a UMA 2 slot worn by a UMA 3 race through [automatic conversion](SlotRigConversion.md#automatic-conversion)) is matched as characters wear it: converted with the race's Legacy Rig Profile. The blendshape is written into the legacy slot's own mesh space in such a way that conversion carries it to the right place, so nothing else has to change. The tool says so under each such slot.

Two things follow from that:

- The shapes fit the conversion as it is now. After changing the race's rig profile in a way that moves a slot, transfer its shapes again.
- A rig profile's entries for a single slot are found by slot name. A *copy* of a legacy slot has another name and does not get them, so it is converted differently and its shapes end up slightly off. The report warns when that applies; write to the slot itself, or add the same entries for the copy's name.

## The dialog

### Source

- **Race.** The race whose body carries the blendshapes. Its rig is also what the slots are placed on.
- **Match against.** *The body as the race builds it* matches slots to the body characters really wear, with the blendshapes the race bakes in (**Prebaked Blendshapes**). *The base slots* matches against the stored slots with no shape applied. Use the first for clothing fitted to the built body, the second for clothing fitted to the base mesh.
- **Source slots.** Which of the race's slots form the source surface. The body's skin slots are ticked by default; eyes, mouth, lashes and brows are not. **Other source slots** adds slots that are not part of the body, for example a shirt whose shapes a jacket should follow.
- **Blend shapes.** Tick the shapes to transfer. Each line shows how far the shape moves the body. **All**, **Live** and **None** act on the shapes shown by the filter. The shapes characters of the race can dial in are ticked to begin with, and **Live** ticks them again. Two kinds of shape are marked and start unticked:
  - *baked into \<race\>*: the race bakes the shape into its body (**Prebaked Blendshapes**). Clothing fitted to that body already has it; transfer it only to slots fitted to the base mesh.
  - *not kept by \<race\>*: the race removes the shape when it builds its body, because it is not in the race's **Unbaked Shapes To Include**. Characters of this race cannot dial it in, so clothing gains nothing from it on this race. Transfer it for other races that share the slots, or add it to that list first (see [Optimizing Blendshapes](OptimizingBlendshapes.MD)).

### Target slots

Add slots with **Add selected** (slots, the slots of selected recipes, the slots in selected folders), by dropping them on the drop area, or with **+**. The race's own body slots are never added. After a preview each slot shows what it got: how many shapes, the largest movement, how many of its vertices are in reach of the body, and whether any end up under the skin.

### Fit

| Setting | What it does |
| --- | --- |
| **Method** | *Follow the surface* keeps each vertex's place relative to the skin (recommended). *Copy the movement* takes the nearest body point's movement as it is. |
| **Strength** | Multiplies the movement. 1 follows the body exactly. |
| **Fade with distance** | Off: every vertex follows the body at full strength, however far away. |
| **Full strength within** | Vertices nearer to the body than this follow it fully. |
| **No movement beyond** | Vertices further than this do not move. In between the movement fades smoothly. |
| **Smoothing passes / Per pass** | How much the movement is evened out over the slot. More for loose garments, fewer for tight ones with fine detail. |
| **Keep clear of the body / Clearance** | Pushes a vertex back out where smoothing or the fade would leave it under the moved body. It restores the clearance, or the gap the vertex had at rest when that was smaller. |
| **Keep small parts rigid / Parts up to** | Moves each small separate piece (a button, a buckle, a stud) as one piece, so it is not stretched or bent. |
| **Normals and tangents** | *Recalculate* stores normal and tangent deltas so lighting follows the shape. *Positions only* stores none, which is smaller. |

Under **Advanced**: **Prefer facing surfaces** and its margin, **Ignore movement under** (smaller movement is stored as none, which keeps shapes sparse) and **Skip a shape under** (a shape that moves a slot less than this is not added to it).

### Output

- **A shape the slot already has.** *Replace it*, or *Keep the slot's own*.
- **Write to.** *The slots themselves* adds the shapes to the slot assets, so every recipe that uses them gets the shapes. *Copies of the slots* saves each slot as a new asset (with a name suffix, next to the original or in a folder, optionally added to the UMA library); recipes keep using the originals until you point them at the copies.

**Transfer** writes the shapes. It can be undone with Undo. **Remove the ticked shapes from the target slots** takes them off again; note that it removes any shape of those names, including ones a slot had before.

### Preview

**Preview** works everything out and draws it in the Scene view, without writing anything: the body and every target slot, moved by one shape.

- **Shape** and the arrows choose the shape; only shapes that move at least one target are listed.
- **Weight** dials the shape in; **Play** moves it back and forth.
- **Colours**: *Shaded* (each slot in its own colour), *Movement* (blue for none to red for the most), *Influence* (how much of the body's movement each vertex takes, which shows the fade), *Under the surface* (red where the shape puts a vertex under the body, orange where it already lay under the body before any shape).
- **Update as I change settings** recalculates shortly after any change, so the settings can be tuned while watching. Turn it off for many or heavy slots.
- **Position** moves the preview away from whatever is at the scene origin.

Orange spots in *Under the surface* are not caused by the transfer: the slot sits under the skin there at rest. On a character such spots are normally covered by a [Mesh Hide Asset](MeshHideAssets.md); to fix the fit itself, adjust the rig profile or the slot.

## Finishing a shape by hand

A transfer follows the body; it does not know where a garment should behave differently. To smooth or correct a shape with sculpt brushes, press **Sculpt '\<shape\>'**. The shape is the one being previewed, or the first ticked one. No Transfer is needed first, and no character has to be in the scene.

What opens is the Mesh Modifier editor in Sculpt mode:

- A temporary character of the race is built wearing the target slots. If the character selected in the Hierarchy is of that race and wears the target slots, it is used instead.
- The target slots are the only editable ones; the body is shown but locked, so brushes work against it. Change that in the **Slots** panel.
- The shape is loaded and shown fully dialled in, on the body and on the slots. A slot that already has the shape shows its own, with any earlier hand-made changes. A slot that does not shows the transfer as the dialog worked it out with the current settings; nothing has been written to it yet.

Sculpt, then press **Save to '\<shape\>'** in the Sculpt panel's **Edit Blendshape** section. That writes the shape to the slots: the previewed transfer plus your strokes. **Stop Editing**, or leaving the editor, drops whatever was not saved. See [Edit an Existing Blendshape](MeshModifierSculpting.md#edit-an-existing-blendshape). The same mode is available from the Mesh Modifier editor itself, for any blendshape on any slot a character wears.

The temporary character stands in the pose the slots were modelled in, the same pose the preview draws. A generated character normally does not: the generator puts it into the race's T-pose and then, when the T-pose asset holds a stored human pose, applies that through Unity's humanoid system. The limbs end up where they were modelled, but Unity spreads their twist over the bones its own way, and a thigh can be rolled 20 degrees or more about its length. Skin weighted to one limb barely shows it; clothing weighted to both legs is pulled apart and folds between them. That fold comes from the pose and the slot's bone weights, not from the blendshape, and a blendshape should not be sculpted to hide it: it would be wrong in every other pose. A character you select in the scene is used as it stands.

Where the fit left a pinch, fold or stretched patch, paint the mask over it, choose the **Heal** brush and press **Heal Masked**. That evens out how far the shape moves that area, so it returns to its modelled form (see [Heal](MeshModifierSculpting.md#heal)).

The preview in the Scene view is only a drawing of the result; it cannot be sculpted, and it is hidden while the editor is open.

Sculpting always saves to the slots themselves, also when **Write to** is set to copies. Transferring a shape again replaces it, hand-made changes included (unless **A shape the slot already has** is set to keep the slot's own). Sculpt last.

## After transferring

- Characters already in the scene pick the shapes up the next time they are built.
- A shape only does something on a race whose built body still carries it. A race with **Prebaked Blendshapes** keeps only the live shapes named in its **Unbaked Shapes To Include**; the dialog marks the others *not kept by \<race\>*.
- A character only loads the blendshapes its avatar asks for (see [Optimizing Blendshapes](OptimizingBlendshapes.MD)); the slot having the shape is not enough on its own.
- A wardrobe recipe is baked for a race's **Prebaked Blendshapes** the same way the body is, so a slot that has a prebaked shape gets it baked in. That is what you want for clothing fitted to the base mesh, and not for clothing fitted to the built body.

## Related tools

- The slot inspector's **Copy Blendshapes** copies shapes between two slots vertex to vertex. Use it for parts with matching geometry, such as a body split into pieces.
- The [Clothing Conformer](ClothingConformer.md) works on one built avatar: bind, change the body, conform, and save the single result. Use it to fit clothing to a body state that is not a blendshape.

## From code

```csharp
UMABlendShapeRigContext.TryCreate(race, out var context, out string problem);
UMABlendShapeSource source = UMABlendShapeSource.FromRace(context, UMABlendShapeBodyShape.AsTheRaceBuildsIt);

UMABlendShapeSlotSpace.TryCreate(slot, context, out var space, out problem);
UMABlendShapeTransferResult result = UMABlendShapeTransfer.Transfer(source, space, new[] { "MaleBody" }, new UMABlendShapeTransferSettings());

UMABlendShapeTransfer.Apply(result, slot, UMABlendShapeExistingMode.Replace);
AssetDatabase.SaveAssets();
```

`Transfer` writes nothing; `result` holds the blendshapes in the slot's mesh space, the avatar-space movement of every vertex, and the counts shown in the dialog.

## Tests

`UMABlendShapeTransferTests` (EditMode) covers binding, fading, following a tilting surface, seams, smoothing, clearance, rigid parts, mesh-space mapping, normal deltas, multi-frame shapes, several source slots, and writing to a slot.
