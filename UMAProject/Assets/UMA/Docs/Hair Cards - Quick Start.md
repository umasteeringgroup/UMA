# Hair Cards - Quick Start

UMA Hair Cards is a guide-driven system for authoring game-ready hair. The normal workflow is:

**Growth Area -> Guide Preview -> Accept Guides -> Groom -> Cards -> Optimize -> Validate & Bake**

The editable `HairGroomAsset` is the source. Card meshes and UMA assets are generated outputs that can be rebuilt.

## 1. Open the Hair Card System

For a generated UMA character:

1. Select the `DynamicCharacterAvatar`.
2. In its Inspector, open **Utilities**.
3. Under **Hair Cards**, click **Open Hair Card System**.
4. Choose a location and name for the new `HairGroomAsset`.

You can also select a readable Mesh, a `HairGroomAsset`, or a generated avatar and choose **UMA > Hair Cards > Open Hair Card Stage**.

The Hair Groom workspace and the Scene view work together. Use the seven tabs across the workspace in order. The current tab now selects a suitable Scene tool automatically, so entering Guides or Groom does not leave the Growth paint tool active.

## 2. Isolate the scalp and paint Growth Area

Open **Growth**.

When the groom was opened from an avatar, use **Avatar Visibility** on the left to hide geometry that should not receive hair. You can filter and hide by recipe, UDIM group, or slot. **Only** is useful for isolating a scalp or head slot. Hidden parts are excluded from painting, vertex selection, and manual guide placement.

Select **Growth Area** in Growth Maps, then:

1. Click **Visible 0** to clear only the currently visible source region if necessary.
2. Select **Paint Growth Area**.
3. Set **Paint Value** to `1`, then adjust Brush Radius, Hardness, and Strength. Hardness matches Overlay Painter: the inner ring receives full strength and then falls off linearly to zero at the outer ring. Enable **Mirror X** (or press `M`) to paint both sides across the source mesh's local `X = 0` plane.
4. Move over the visible surface until the cyan brush circle appears.
5. Left-drag to paint. Enable **Erase** to remove growth. Alt-drag retains normal Scene view orbiting.

The Growth overlay is blue at zero and moves through purple to orange as strength increases. The character's regular textures may remain visible beneath the translucent overlay; orange on the intended scalp is the important result. With **Mirror X** enabled, a second brush ring previews the opposite footprint on the posed character. Vertices on or near the centerline receive one brush application, not doubled strength.

For a fast block-in, isolate the scalp first and click **Visible 1**. Avoid **Fill Entire Source 1** on a full combined character unless you truly want guides eligible everywhere.

As an alternative to brushing, choose **Select Vertices**, click or drag over triangles, and use **Selection -> Map**. Shift adds and Ctrl/Cmd subtracts. Grow, Shrink, Invert, and Clear refine the selection.

## 3. Generate and accept guides

Open **Guides**. The workspace reports how many source vertices have a non-zero Growth Area. Automatic generation is disabled until that count is greater than zero.

For automatic guides:

1. Set **Guide Count**, **Points per Guide**, **Default Length**, and **Minimum Spacing**. Start with 50-150 guides and low spacing while learning.
2. Click **1. Preview N Generated Guides**.
3. Inspect the temporary cyan dashed splines in the Scene view. If fewer guides are placed than requested, expand the Growth Area or reduce Minimum Spacing.
4. Click **2. Accept N as Guides**.

Preview is intentionally non-destructive. The dashed splines do not become editable and are not available to Groom until **Accept** is clicked. The Authored Guides count confirms acceptance.

**Replace Generated Only** refreshes previously generated guides while preserving manually authored guides. **Cancel Preview** discards only the temporary preview.

For manual control:

- **Place Guide**: click the source surface to place a guide along its normal.
- **Draw Guide**: drag over the source surface to draw a guide.
- **Select / Edit**: select a guide or control point and use the Scene position handle.

Unlock the active group before generating, accepting, placing, or editing guides.

## 4. Groom and style the guides

Open **Groom**. The Comb tool becomes active automatically. Solid colored curves are authored guides; faint dotted curves are generated children when **Show Children** is enabled.

Select a brush, move over any part of a guide until the cyan brush circle appears, then left-drag:

- **Comb** changes flow in the drag direction.
- **Grab** moves nearby guide points.
- **Smooth** relaxes uneven curves.
- **Length** grows guides; Reverse / Erase shortens them.
- **Cut** trims at the brushed point.
- **Width** widens guides; Reverse / Erase narrows them.
- **Clump** pulls nearby guides together.
- **Part** pushes guides away from the brush center.
- **Freeze** protects points from other brushes; Reverse / Erase unfreezes them.

Brush edits are written to the active visible, unlocked Sculpt Layer. If none is usable, the system creates one automatically. Use **+ Sculpt Layer** for separate passes, such as Base Flow, Silhouette, and Flyaways. Layer visibility, lock, opacity, and blend mode are non-destructive controls.

The Modifier Stack applies repeatable procedural changes. Modifiers can affect Guides, Children, or both. Helper-dependent modifiers expose a Helper field.

For helper-driven placement:

1. Add an embedded **Curve Rail** or **Collider**, or bind a scene GameObject as a curve rail.
2. Select and position the helper in the Scene view.
3. Click **Constrain Active Group to Helper**.
4. Adjust the constraint type and weight. Remove it with **X** if no longer needed.

## 5. Build the cards

Open **Cards**.

Choose a Card Profile. New grooms receive a default ribbon profile; **Create Default Ribbon Profile** repairs a missing profile.

- **Ribbon** creates flat cards. Root Width, Tip Width, Samples per Card, and Generate Backfaces control the mesh.
- **Tapered Tube** creates a polygonal tapered strand. Set Tube Sides from 3 to 12.

Under **Child Cards**, set Children per Guide, root spread, clump, length/width/roll variation, interpolation, and seed. The estimate explains the output:

`guides x (children per guide + optional guide card) = approximate card count`

### Define and assign atlas UV areas

Under **Atlas & UV Areas**, assign an Atlas Profile or click **Create Atlas Profile**. Assign the atlas textures and the material used by the generated cards.

Each numbered **UV Area** is a normalized rectangle on the atlas:

- **UV Rectangle** uses `X`, `Y`, `W`, and `H` in the 0-1 UV range.
- **Selection Weight** controls how often the area is chosen relative to the other eligible areas.
- **Flip U** and **Flip V** reverse the card texture inside that area.
- **Tags** describe the strip, for example `wide`, `flyaway`, `dense`, or `edge`.

The atlas preview outlines every numbered area over the Albedo Atlas. Click **Open UV Area Editor** for the large interactive editor:

1. Click **Draw New Area**, then left-drag a rectangle directly over the atlas.
2. Click any existing outline or numbered list entry to select it.
3. Click **Redraw Selected** to replace that area's rectangle by dragging again.
4. Keep the complete **Defined UV Areas** list visible while drawing. Every area has its own colored, numbered outline and exact normalized coordinates.

The editor refuses to create a nearly identical rectangle: it selects the matching existing area and displays a notification instead. It also warns when an atlas profile already contains nearly identical definitions. Click **+ UV Area** in the main Cards panel when exact numeric entry is more convenient. The area number is its visible position in the profile; the groom stores a stable internal ID, so renaming or reordering areas does not break assignments.

Choose one assignment mode for the active hair group:

- **Use All UV Areas** lets every generated card choose from every area in the profile.
- **Use Selected UV Areas** enables the checkboxes beside the numbered areas. For example, check Areas 2, 3, and 7 to restrict this group to `{2, 3, 7}`.

Each guide card and child card then chooses one eligible area using its stable groom seed and the area weights. The choice is random-looking but deterministic: rebuilding the same groom keeps the same card-to-area assignment. Different hair groups can use different subsets of the same atlas profile.

Set Preview to **Cards** and click **Rebuild Card Preview** to inspect the result. An atlas is optional unless **Require Atlas** is enabled; without one, cards use full-range UVs and the fallback preview material. **Selected UV Areas** with no valid checked area is a validation error.

## 6. Optimize

Open **Optimize** and watch the live guide, card, vertex, and triangle counts. Set target budgets and configure each LOD:

- Card Fraction reduces the population deterministically.
- Samples per Card reduces lengthwise geometry.
- Maximum Tube Sides limits tube cost.
- Screen Height controls the intended transition point.

Inspect every LOD and test deformation on representative equipped avatars before release.

## 7. Validate and bake

Open **Validate & Bake**.

1. Click **Validate All** and resolve every blocking error.
2. Configure the output folder and asset name.
3. Choose the Unity Mesh, UMA SlotDataAsset, OverlayDataAsset, Wardrobe Recipe, and library options needed by the project.
4. Assign the UMA Material, existing Overlay, compatible Race, and Wardrobe Slot when creating those UMA outputs.
5. Click **Dry Run**. It evaluates guides, children, modifiers, constraints, cards, budgets, roots, and mesh validity without writing output assets.
6. Click **Bake** when validation is clear.

Save preserves the groom. The stage also keeps a recovery snapshot under `Assets/UMAProjectData/HairCards/Recovery`.

## Troubleshooting

**The brush appears but the Growth Area does not change**

- Confirm the Growth tab and Growth Area map are active.
- Confirm the map is visible (`V`) and unlocked (`L` is off).
- Set Paint Value above zero, Strength above zero, and disable Erase.
- Make sure at least one relevant source slot is visible.
- Look for the translucent blue-to-orange overlay, not a replacement of the character material.

**I changed tabs but it still paints**

This is corrected by scoped workflow tools. Growth uses Paint/Select, Guides uses Select/Place/Draw, Groom uses grooming brushes, and output tabs use Select. If an old stage was already open during a script update, close and reopen the Hair Card Stage once.

**Preview generates zero guides**

- Return to Growth and confirm the Guides tab reports non-zero Growth Area vertices.
- Unlock the active group.
- Reduce Minimum Spacing.
- Increase the painted region.
- Confirm the source mesh has Read/Write enabled.

**Dashed guides are visible, but Groom says there are no guides**

The generation preview is temporary. Return to Guides and click **2. Accept N as Guides**. The Authored Guides count must be greater than zero.

**The Groom brush does nothing**

- Confirm guides were accepted and the active group is visible, enabled, and unlocked.
- Move over the displayed guide curve until the brush circle appears; the whole segment is pickable, not only its control points.
- Confirm the active Sculpt Layer is visible and unlocked.
- Increase Radius enough to reach nearby guide points.
- Drag the mouse; Comb and Grab need movement after the initial click.

**No cards appear**

- Assign or create a Card Profile.
- Ensure Children per Guide is above zero or Include Guide Card is enabled.
- Set Preview to Cards and rebuild.
- Check that the group is visible, enabled, and included in the bake.

## Shortcuts

- `Q`: Select
- `P`: Paint Growth (also returns to Growth)
- `M`: Toggle Growth painting X mirror
- `C`: Comb (also enters Groom)
- `G`: Grab (also enters Groom)
- `S`: Smooth (also enters Groom)
- `Shift+R`: Rebuild Preview
- `[` / `]`: Decrease/increase brush radius
- `Shift+[` / `Shift+]`: Decrease/increase brush hardness
- `Shift` + right-drag: Horizontal movement changes brush radius; vertical movement changes hardness
- `Alt` + mouse: normal Scene view navigation

Shortcuts can be remapped in Unity's Shortcut Manager. The `?` button in the Hair Groom header opens this guide.
