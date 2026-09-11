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

### Saved source mesh and repairing older grooms

Grooms created from generated characters/renderers now store a private, readable **source mesh snapshot inside the groom asset**. It preserves vertex order, submeshes, normals, UVs, skin weights and bind poses so painted maps and guide roots remain bound to the exact original geometry. Animation blendshapes are omitted to reduce storage; the original character is untouched. The snapshot is saved once, not duplicated during grooming or each Save. Existing imported mesh assets remain external references and are not copied. Opening or saving an older groom upgrades a still-live temporary source automatically.

Reopening the groom no longer requires the original character to regenerate. When its recorded original character is available in a saved, open scene, the stage can reconnect its character preview; otherwise the saved source surface remains usable.

If an older groom already shows **Missing** for Source Mesh, opening first tries its embedded snapshot or recorded original source. When automatic recovery is unavailable, select the groom in the Inspector, choose the original readable mesh or generated character/renderer in **Restore Source From**, and click **Restore Source**. Use the original race, wardrobe and LOD: repair rejects different vertex/triangle topology and does not silently rebind your hair. A generated replacement is then saved as a private snapshot. The groom's stable source identity, surviving maps, guides and layers are retained. Loading a missing-source groom no longer clears its paint arrays; paint already overwritten by an older version requires a recovery snapshot or backup.

The Hair Groom workspace and the Scene view work together. Use the seven tabs across the workspace in order. The current tab now selects a suitable Scene tool automatically, so entering Guides or Groom does not leave the Growth paint tool active.

The left-side **Preview & Display** section is the single location for preview mode, Full/Draft quality, **Rebuild Card Preview**, card wireframe, root handles and size, guide and child spline visibility, selected control points, freeze mask, scene depth, authoring surface, character preview, and helpers. It stays available across all workflow tabs. Avatar part filters remain directly below it under **Avatar Visibility**.

Turn off **Show card wireframe** for a clean material preview: this hides the yellow UV-set card highlights and Unity's selected-card outline without hiding cards or disabling card picking. Guide/child spline visibility remains independent. This is display-only and does not alter baked output; a Scene view explicitly set to a global wireframe draw mode still uses that mode.

**Show child splines** hides only the faint dotted lines in **Guides And Children** mode; it does not remove child cards or rebuild geometry. **Include children in preview** separately controls child evaluation for both line and card previews. Disable it for a lighter, guide-only preview. Enable **Use scene depth (Z-buffer)** to hide roots, splines, control points, and generated children behind the character; disable it for an X-ray view. All these settings are stage-only and never change the groom or baked output.

## 2. Isolate the scalp and paint Growth Area

### Remembered settings and defaults

**Erase is session-only and starts off whenever you open a grooming stage or restart/reload Unity.** The shared Reverse / Shorten / Unfreeze brush mode is also reset; previously saved Erase settings are ignored. Other brush settings remain persistent.

While painting a growth map, **hold Shift to erase temporarily**, including midway through a drag. Release Shift to return to the selected mode; if Erase was already selected, it remains selected. Normal and mirrored brushes use the same temporary mode, and changing direction within a stroke remains one Undo operation. This does not reverse grooming tools. Shift + right-drag still adjusts radius/hardness, and Shift + brackets still adjusts hardness.

The Scene view shows contextual helper instructions at the **top-left, below the hair toolbar**, including the active PAINT/ERASE mode and map, temporary erase, mirror, brush-size/hardness and navigation shortcuts. The Erase button and brush color also reflect temporary erase without saving it as your selected mode.

Editor options are saved automatically while idle, on **Save**, and when closing the workspace/stage. Reopening the same groom restores its brushes, root influence, gravity options, mirrors, guide-generation parameters, display/visibility options, workflow/tool selection, and workspace layout options. UV-preview channel, checker colors, background, snapping, outlines, units, zoom/pan, and selection are remembered too. Idle persistence does not run during a grooming stroke. These local preferences survive Unity restarts in `UserSettings/UMA.HairCards.Preferences.asset`; they do not affect baked output or need to be shared with the groom.

Authored options remain in their regular assets: the groom, Card Profile, Atlas Profile, and assigned material. **Save** and idle autosave include dirty card/atlas profiles and assigned materials, including albedo/normal/mask references and UV sets. Existing grooms always keep their own asset setup. Move or rename an asset through Unity and its references remain valid.

New grooms inherit the last-used active group's card profile, atlas and texture/material assignments, UV-set selection, children, root inset, group display/role options, and the groom's LOD, bake and symmetry settings. Card/atlas profiles are copied into new private assets next to the new groom, so editing those profiles does not change an older groom. Texture and material assets remain shared references. Source mesh, painted maps, guides, sculpt layers, modifiers and helpers are **not** copied; the new bake output name is derived from the new groom to avoid targeting the previous groom's output. Missing/deleted profile assets fall back to fresh defaults rather than stale references.

Click **Reset to defaults** below the toolbar to reveal three independent controls inside this workspace:

- **Reset editor options** clears remembered tool, display, generation, workspace and UV-preview options across this project's grooms, and the remembered groom-creation folder. It does not alter authored hair or card assets.
- **Reset card setup** shows an inline confirmation. It restores the active group's default private card/atlas profiles (empty texture/material assignments and a full-atlas UV set), child/root/card-display options, and this groom's LOD, bake and symmetry defaults. Guides, paint, sculpt layers, modifiers, helpers and old assets remain intact. Other groups keep their own card resources. Undo restores the former assignments/settings. Locked groups must be unlocked first.
- **Forget new-groom defaults** clears the remembered setup used to initialize future grooms without changing the current groom. It stays forgotten until you change the setup again or use a different setup.

### Paint the current area

Open **Growth**.

When the groom was opened from an avatar, use **Avatar Visibility** on the left to hide geometry that should not receive hair. You can filter and hide by recipe, UDIM group, or slot. **Only** is useful for isolating a scalp or head slot. Hidden parts are excluded from painting, vertex selection, and manual guide placement.

Click **Focus current area** at the top of **Avatar Visibility** to frame the current group's non-zero **Growth Area** and its influencing bone positions. The orbit pivot stays on the character's vertical axis at the region's height. Bounds expand symmetrically left/right and front/back to contain the painted surface and bones, with a small margin; painting only half the head will not pull the orbit center toward that side. The bounds use the displayed authoring pose. This always uses Growth Area, even when another map is selected, and does not change visibility or the groom. Standalone meshes use their local vertical axis and bind-pose bones when available, otherwise the painted surface alone. An empty area leaves the camera unchanged.

Directly below it, **Focus Head** and **Focus Neck** place the orbit pivot **5 cm above** the corresponding bone in the displayed character pose, with the Scene camera **0.45 meters from that pivot**. The upward framing offset is in stage space (and is disabled in builds defining `NoFudge`). They keep the current viewing angle and perspective/orthographic mode, require no paint, and do not change the groom or visibility. A button is disabled when its bone is unavailable. Humanoid bone references are preferred, with UMA skeleton/exact Head or Neck names used for generic rigs.

Select **Growth Area** in Growth Maps, then:

1. Click **Visible 0** to clear only the currently visible source region if necessary.
2. Select **Paint Growth Area**.
3. Set **Paint Value** to `1`, then adjust Brush Radius, Hardness, and Strength. Hardness matches Overlay Painter: the inner ring receives full strength and then falls off linearly to zero at the outer ring. Enable **Mirror X** (or press `M`) to paint both sides across the source mesh's local `X = 0` plane.
4. Move over the visible surface until the cyan brush circle appears.
5. Left-drag to paint. Enable **Erase** to remove growth. Alt-drag retains normal Scene view orbiting.

The Growth overlay is blue at zero and moves through purple to orange as strength increases. The character's regular textures may remain visible beneath the translucent overlay; orange on the intended scalp is the important result. With **Mirror X** enabled, a second brush ring previews the opposite footprint on the posed character. Vertices on or near the centerline receive one brush application, not doubled strength.

Hardness `0` produces a gradual fade from the brush center to its edge; hardness `1` fills the footprint uniformly. Overlapping samples within one stroke retain that falloff instead of repeatedly hardening the edge. Release and paint another stroke to build up coverage; Erase uses the same soft falloff. To compare hardness settings, start on a cleared patch—painting softly over an already full mask does not reduce it. Growth maps are stored per source vertex, so very coarse meshes limit how finely a soft edge can be represented.

For a fast block-in, isolate the scalp first and click **Visible 1**. Avoid **Fill Entire Source 1** on a full combined character unless you truly want guides eligible everywhere.

As an alternative to brushing, choose **Select Vertices**, click or drag over triangles, and use **Selection -> Map**. Shift adds and Ctrl/Cmd subtracts. Grow, Shrink, Invert, and Clear refine the selection.

### Copy, cut and paste map values

Select any map in **Growth Maps** to expose **Copy**, **Cut**, and **Paste** under that map's Operations heading. Copy snapshots its vertex values without changing the source. Cut snapshots them and resets the source to its default value (for example, Growth Area becomes `0`, Density and Length become `1`); it does not delete the map. Select the destination map, then Paste to replace its values. Cut and Paste each support Undo/Redo.

These operations affect the **entire map**, including hidden slots and unselected vertices. Paste preserves the destination map's identity, name, type, range, default, visibility and lock settings, and clamps copied values to its allowed range. Values are copied directly, not normalized between different ranges. You can transfer between map types or groups, or between grooms bound to the same source mesh identity and matching vertex topology. Incompatible sources/topology are rejected rather than resampled silently.

Locked maps/groups can be copied, but must be unlocked before Cut or Paste. Paste remains available for repeated use. The dedicated map clipboard survives script reloads and workspace switches within the current Unity editor session; it is cleared when Unity exits and does not replace the OS text clipboard. The clipboard label identifies the source map and vertex count; disabled-button tooltips explain incompatibilities.

## 3. Generate and accept guides

Open **Guides**. The workspace reports how many source vertices have a non-zero Growth Area. Automatic generation is disabled until that count is greater than zero.

For automatic guides:

1. Set **Guide Count**, **Points per Guide**, **Default Length**, **Minimum Spacing**, and **Root Uniformity**. Start with 50-150 guides, low minimum spacing, and uniformity 0.75–1 for even coverage.
2. Click **1. Preview N Generated Guides**.
3. Inspect the temporary cyan dashed splines in the Scene view. If fewer guides are placed than requested, expand the Growth Area or reduce Minimum Spacing.
4. Click **2. Accept N as Guides**.

Preview is intentionally non-destructive. The dashed splines do not become editable and are not available to Groom until **Accept** is clicked. The Authored Guides count confirms acceptance.

**Replace Generated Only** refreshes previously generated guides while preserving manually authored guides. **Cancel Preview** discards only the temporary preview.

### Tune guide spacing

In **Guides → Automatic Guide Generation**:

- **Root Uniformity** controls how evenly new roots cover the painted surface. `0` uses the original random placement; `0.75` is the default; `1` compares the most candidates and favors filling gaps. Higher values take longer to preview. This makes spacing more even without arranging roots in a rigid grid.
- **Minimum Spacing** sets a hard minimum source-mesh-space distance between roots in the new preview. It prevents close pairs but does not by itself fill empty gaps. Start low and increase gradually. Too much spacing for the requested count produces fewer guides and a warning; the generator never silently relaxes it.
- **Guide Count** controls the target population. **Seed** changes the pattern while the same seed, settings, mesh, and paint reproduce the same root positions.
- **Growth Area** and **Density** remain active. For even coverage, paint Density evenly. Higher-density areas intentionally receive more roots; uniformity does not override that distribution.

Change settings, click **Preview** again, and inspect before accepting. Changing generation settings clears an old temporary preview so it cannot accidentally be accepted with stale settings. Existing authored guides are not moved automatically. To regenerate a previous generated layout, use **Replace Generated Only** after previewing; this replaces those guides, so save first if you want to keep their grooming. Repeated **Accept** adds another batch and can overlap previous batches—the spacing check is within the new batch, not against existing authored guides.

For manual control:

- **Place Guide**: click the source surface to place a guide along its normal.
- **Draw Guide**: drag over the source surface to draw a guide.
- **Select / Edit**: select a guide or control point and use the Scene position handle. Handles follow the displayed groom and write to a sculpt layer; they do not alter the original guide. Roots and fully frozen points are anchored, and other segment lengths remain fixed.

Unlock the active group before generating, accepting, placing, or editing guides.

## 4. Groom and style the guides

Open **Groom**. The Comb tool becomes active automatically. Solid colored curves are authored guides; faint dotted curves are generated children in **Guides And Children** preview mode when **Include children in preview** and **Show child splines** are enabled in the left-side **Preview & Display** section.

Once you have generated a card preview, returning to **Groom** automatically shows cards while idle. Starting a stroke temporarily hides the cards so you can see the moving guides; releasing the mouse rebuilds and displays the updated cards on the next editor update. Releasing **Gravity Settle** refreshes them the same way. Use the Scene view **Preview** menu to select **Guides** or **Guides And Children** when you prefer to groom without the card mesh.

Preview refreshes reuse the mesh, evaluated curves, card metadata, and posed-guide lookups instead of recreating them after every stroke. UV-only edits still update UVs without regenerating the curves, and wireframe highlights reuse their edge topology while following the updated vertices. No quality reduction is applied in **Full** mode. For exceptionally dense grooms, choose **Draft** under **Preview & Display** for faster iteration, then return to **Full** to inspect the complete result; release baking remains full quality.

Select a brush, move over any part of a guide until the cyan brush circle appears, then left-drag. Brush influence is evaluated against complete displayed spline segments, so sparse control points and resampling modifiers do not create dead zones between points:

**Through Depth** is the default edit scope. It treats the camera-facing circle as a projected grooming cylinder, so overlapping guide layers under the cursor move together. Choose **Depth Volume** when you need a depth-isolated 3D brush.

- **Comb** changes flow in the screen-space drag direction without stretching guides.
- **Grab** moves nearby guide points while preserving segment lengths.
- **Smooth** relaxes uneven curves without shrinking them.
- **Length** grows guides; **Shorten (instead of lengthen)** reverses it. This tool deliberately bypasses segment-length preservation, while keeping the root and fully frozen points fixed. The change is stored on the sculpt layer.
- **Cut** is a slice gesture: drag a line across the Scene view. The camera and line define a finite cutting plane. Every crossed guide keeps its root side, gains an exact interpolated tip at the intersection, and discards everything beyond it. Enable **Mirror Slice Across X** (or press `M`) to apply the same cut across source-local `X = 0`.
- **Width** widens guides; Reverse / Erase narrows them.
- **Clump** pulls nearby guides together.
- **Part** pushes guides away from the brush center.
- **Freeze** protects points from other brushes; **Unfreeze** removes protection, including from fully frozen points. The automatic mask view uses cyan for editable regions and pink for frozen ones. Enable **Show freeze mask** under Preview & Display to keep it visible with other tools. The length solver respects frozen anchors; a slice that would move or delete a frozen tip-side anchor is skipped.

Comb motion stays on a camera-facing plane for the duration of each stroke. This prevents a small mouse movement from turning into a large depth jump when the cursor crosses guides on the front, side, or back of the head. Comb, Grab, Smooth, Clump, and Part preserve every segment; use **Length** or **Cut** when the silhouette should actually become shorter or longer.

If a groom was saved after an older brush stretched its guides, click **Repair Existing Stretch (New Layer)**. The repair keeps the current directions, restores the authored segment lengths, and writes the correction to a new layer. This also normalizes intentional Length-tool edits, so hide or remove the repair layer if those length changes should remain.

**Root Influence** in **Groom** adjusts bending response near the attachment for Comb, Grab, Smooth, Clump, Part, and Gravity. At `0`, an extra protection ramp reduces movement near the base and fades to the tool's normal strength at the tip. At `1` (the default), each tool retains its original response, including Comb's own root-to-tip falloff and gravity stiffness. Brush falloff and freeze protection still apply. The root attachment itself always stays fixed, and normal bending tools still preserve segment lengths. This is a tool setting for subsequent strokes, not a retroactive change to existing sculpt layers. Length, Cut, Width, Freeze/Unfreeze, and direct control-point handles are unaffected.

Under **Gravity Settle**, set Strength and Card Separation, then press and hold **Hold to Apply Gravity**. Roots remain locked and every segment retains its length. Card Separation fans the falling guides along the scalp with stable per-guide variation so neighboring cards retain air instead of collapsing into one sheet. The entire hold is one undo operation and is written to the active sculpt layer.

Enable **Collide with scalp / body** to keep settling guides outside the source surface. **Surface clearance** adds a small gap. The collision surface includes hidden body slots in the authoring pose and needs outward-facing triangle winding. This is a guide-level constraint, not cloth/card-to-card collision. Roots and frozen anchors take priority when clearance cannot be satisfied without stretching; a conflicting step is held back. Use a smaller clearance near tight folds or pinned tips.

### Selection and editing feedback

The **Edit scope** menu offers **Through Depth**, **Visible Hair**, **Selected Guides Only**, and **Depth Volume**. Through Depth reaches overlapping guides under the projected brush; Visible Hair excludes control points occluded by the visible source surface; Depth Volume uses a local 3D radius. These affect edit reach independently of the Preview & Display Z-buffer switch. Hovering shows affected guides in teal/orange with an editable-guide count. Frozen controls and unselected guides in Selected Guides Only are excluded from that count.

The guide library is available in **Guides** and the **Guide selection & isolation** foldout in **Groom**. Search by name, page through every guide, use row checkboxes or Shift/Ctrl/Cmd-click to toggle selection, and use **Select matching** for the filtered results. Bulk enable, disable, freeze, unfreeze, duplicate, and delete act on the selected guides; deletion has an inline confirmation. **Isolate** displays and edits only that selection. Isolation is preview-only and never removes guides from release output.

While dragging a Cut slice, red segments mark exactly which tips will be discarded, intersection markers locate the new ends, and a count appears beside the line. **Mirror X** includes both sides in the same preview and counts a guide only once. Frozen tip anchors are protected. Release applies the cut; Escape cancels it.

Brush edits are written to the active visible, unlocked Sculpt Layer. If none is usable, the system creates one automatically. Use **+ Sculpt Layer** for separate passes, such as Base Flow, Silhouette, and Flyaways. Layer visibility, lock, opacity, and blend mode are non-destructive controls.

Point edits use the same layer rules as brushes. An active layer completely hidden by an opaque Override layer is replaced as the editing destination by a new top sculpt layer, leaving the protected layers unchanged. Partially overridden layers compensate for their effective opacity so the point does not jump back after release.

### Unified layer stack

In **Groom → Layer Stack**, each sculpt layer owns a collapsible list of modifiers. Click a layer row for **Layer Properties**, or an indented modifier row for **Modifier Properties** in the same box below the stack. Only one item is selected. Collapsing a layer containing the selected modifier selects its layer instead. Selection and expanded/collapsed state are remembered for that groom, not copied into new grooms.

Use **+ Sculpt Layer** to add a pass, then **+ Modifier** to add a procedural operation to the selected layer (or the owner of the selected modifier). The new modifier is selected and its layer expands. Brush strokes still go into that owner layer when a modifier is selected; the existing visibility, lock and effective-opacity rules determine whether a new editable sculpt layer is needed.

Layers evaluate **bottom to top**. Within a layer, sculpt offsets run first, then its modifiers **top to bottom**. A modifier deforms the accumulated hair at that point in the stack, not just that layer's offsets. Thus moving a layer moves its whole procedural pass, and a modifier-only layer is useful for final adjustments. Children inherit evaluated guide shapes, then receive child-domain modifiers in stack order. Modifiers can affect Guides, Children, or both; helper-dependent modifiers expose a Helper field.

**V** hides the layer's sculpting and all its modifiers. **S** solos that layer and its modifiers for preview without changing release output. **L** locks both sculpting and modifier edits. Layer **Opacity** scales sculpt offsets and modifier blend weights; **Sculpt blend** affects offsets only. Discrete Resample/Simplify sample counts remain discrete whenever their effective weight is nonzero.

The property box provides **Move up**, **Move down**, **Duplicate**, and **Remove** for the selected item. Layer duplication includes independent copies of its modifiers and influence curves; removing a layer removes its modifiers too. Modifier moves stay within their owner layer. Structural changes, settings and enable/visibility changes are undoable.

Opening an older groom automatically moves its former group-wide modifiers into an **Imported Modifiers** layer, above its existing sculpt layers, retaining modifier IDs, order and the normal evaluated result. This migration is undoable. If you undo it (or external code adds legacy group modifiers), an inline **Move legacy modifiers into a layer** button makes them editable again; repainting does not silently redo the migration. Runtime evaluation continues to support unmigrated grooms.

**Flow Align** rotates each strand segment toward **Source-local direction**, reconstructing the strand from its unchanged root and preserving every input segment length. **Alignment (0–1)** controls the rotation amount, multiplied by Blend weight and the root-to-tip influence curve (sampled at each segment's end). At full alignment, blend and influence, the strand points along the chosen direction; zero alignment, zero blend or a zero direction has no effect. Width and roll are unchanged. Direction is relative to the source mesh, not the Scene camera, and the result follows the character's authoring pose.

With **Guides And Children**, guide deformations and width/roll changes are applied once and children inherit them through weighted interpolation. They are not applied a second time to children (which previously doubled scaling/settling and mirrored hair back). **Children** applies only to generated children. The legacy per-card LOD setting is applied to both domains because children do not inherit card-resolution metadata. Older Flow Align amounts are alignment strengths, not translation distances: an existing `0.1` means 10% alignment, not a 10 cm offset.

### Gravity modifier and coordinate spaces

Add **Gravity** to a layer for a repeatable, non-destructive settle. **Settle duration** is 0–5 seconds; **Gravity strength**, **Root Influence**, **Card separation**, **Collide with source mesh**, and **Surface clearance** control the result. New Gravity modifiers copy the current hold-button settings. They then own those settings: changing brush settings later does not silently change an existing modifier.

Gravity shares the hold button's segment settling, stiffness/freeze response, length constraints and surface collision solver. Roots are anchored, fully frozen controls are protected, and authored segment lengths are retained. Lower Root Influence slows bending near the base; the tip retains full influence. Stiffness and Root Influence control settling response, not an absolute final-angle limit. Strength, duration, layer opacity and the modifier's root-to-tip influence combine to control the settle. Zero duration, zero strength, zero influence or zero world gravity produces no change, including no collision-only displacement.

**Use world gravity** is on by default. It uses the direction of `Physics.gravity`, converts through the object's full transform and, in the authoring stage, each guide's posed transform. Parent rotation, nonuniform/negative scale and object translation are handled; translation never enters the direction calculation. Disable world gravity for an explicit **Local gravity override** that follows the strand's object/pose. Root normals used for separation use the inverse-transpose normal transform. The artist strength controls the rate; the magnitude of `Physics.gravity` is not an extra strength multiplier.

Rotations are calculated in world space, then reconstructed at the original **source-local segment lengths**, matching the grooming length rule. This is an authored settle, not a physical world-length simulation under changing nonuniform scale. Clearance is in the collision mesh's local units. Collision uses the full authoring surface when available in the stage, and the groom's source mesh in standalone generation. Roots, frozen anchors and length constraints take priority where clearance cannot be satisfied.

Every evaluation starts from the incoming layer shape and simulates the saved duration at bounded 60 Hz steps. Rebuilding does not accumulate gravity. Long durations with collision on dense grooms cost more; begin with the default 0.5 seconds. The collision BVH and solver scratch are reused. This is not continuous runtime physics: call **Regenerate** after changing the object's transform or gravity if you want a new settle.

`HairGroomRuntimeComponent` automatically supplies its hierarchy's `localToWorldMatrix`. Code using `HairGroomRuntimeAPI.Generate` should pass `sourceToWorld: destination.transform.localToWorldMatrix`; direct evaluator callers can supply `HairEvaluationOptions.sourceToWorld`, `worldGravity`, and an optional per-guide `guideToSourcePose`. Output vertices remain source-local. Without an object/pose context, evaluation uses the canonical identity frame. Validation and all bake LODs invoked for the open groom use the stage's gravity context without inheriting preview visibility, isolation or solo filters; standalone bakes use the canonical frame.

### Modifier behavior reference

| Modifier | Behavior and invariants |
| --- | --- |
| Resample / Simplify | Change control-point resolution while retaining endpoints; approximate the original polyline, so length can change. Stiffness and freeze are interpolated with the other attributes. Counts are discrete, not a geometric blend. |
| Length | Intentionally scales distance from the pinned root; frozen controls stay fixed. Does not apply the bending length constraint. |
| Width / Twist | Change width or cross-section roll, not centerline positions. Root-to-tip influence is supported. |
| Smooth / Flow Align / Lift / Part | Bend with pinned roots and preserved segment lengths. Root Influence and root-to-tip influence control response. Part uses the groom symmetry-plane position and its source-local direction as the plane normal. |
| Clump | Bends toward the group's shared authored tip center, or a selected helper position, instead of merely straightening each guide around its own axis. Preserves roots and lengths. |
| Curl / Wave / Noise | Root-anchored, length-preserving shape changes. Noise is seeded and repeatable. Add Resample before Curl/Wave for at least four control points per cycle. |
| Helper Follow | Follows a curve relative to each strand's own root. Follow strength, blend and influence all work. Roots and lengths are preserved; missing/non-curve helpers show a warning. |
| Collision / Push Out | Solve against supported sphere, box, capsule or plane helpers. Surface offset is clearance; blend and influence control response independently. Samples along segments are checked, and anchors/lengths take priority. Partial blend intentionally allows partial clearance. |
| Surface Projection | Pins the root and projects other non-frozen points onto the existing nearest-vertex source-surface approximation. Projection intentionally changes length; it is not the triangle-based gravity collision solver. |
| Mirror | Reflects the whole strand, including its root and root normal, around the groom symmetry plane. Does not duplicate hair. Partial blending passes through the plane and can compress the shape. |
| Trim By Mesh | Truncates at the first source-mesh intersection, retaining the root side. Cut offset moves the cut toward the root; blend softens the removed length. Frozen tips are protected. It no longer performs a push-out. |
| LOD Reduction (legacy) | Reduces samples per card; use Optimize for density and distance-based LOD controls. |

After upgrading, rebuild and review existing procedural layers: root-moving/stretching effects and double application to children are intentionally corrected. Gravity amount now means seconds, Helper Follow amount now controls follow strength, and Collision/Push Out surface offset now means actual clearance rather than multiplying blend strength. The original guides and sculpt-layer data are not rewritten.

The current group/painting layer is shown above the brushes and in the Scene toolbar. Modifier properties show only the selected operation's type-specific controls, such as amplitude, cycles, phase, scale and root-to-tip influence where supported.

For helper-driven placement:

1. Add an embedded **Curve Rail** or **Collider**, or bind a scene GameObject as a curve rail.
2. Select and position the helper in the Scene view.
3. Click **Constrain Active Group to Helper**.
4. Adjust the constraint type and weight. Remove it with **X** if no longer needed.

## 5. Build the cards

Open **Cards**.

Choose a Card Profile and expand **Card geometry** to adjust its shape. New grooms receive a default ribbon profile; **Create Default Ribbon Profile** repairs a missing profile. Textures and UV sets can be configured before adding guides.

**Use profile sampling** chooses the sampling source for the current LOD. When enabled, each group uses its Card Profile's sample count. Otherwise, edit the active **LOD samples** override. The effective points-per-card count is shown underneath; inactive profile sampling is disabled instead of silently ignored. Existing grooms retain their LOD overrides until you choose profile sampling.

- **Ribbon** creates flat cards. Root Width, Tip Width, Samples per Card, and Generate Backfaces control the mesh.
- **Tapered Tube** creates a polygonal tapered strand. Set Tube Sides from 3 to 12.

Root Width and Tip Width blend gradually along the entire card, not just its final segment. Equal widths produce a constant-width ribbon (or tube); zero Tip Width tapers to a point, and a wider tip gradually flares outward. The profile's Width Along Card curve shapes this blend. Width sculpting, width modifiers, painted width scale, and child width variation apply on top. Existing guides' original automatic taper is compensated during meshing, so changing the profile does not require regenerating guides. Preview, runtime generation, and baked meshes use the same width calculation.

**Root Embed (mm)** under **Cards → Card geometry** tucks generated card bases inward along their root surface normal. Start with **1–3 mm**; `0` disables embedding (the default), and the range is 0–20 mm. The offset smoothly fades to zero over the first 20% of the card's original arc length. It applies to guide cards and generated children, ribbons and tubes, in preview, runtime generation, and bake. Guides, surface anchors, sculpt layers, and guide lengths remain untouched; repeated rebuilds do not accumulate the offset. Unlike shared profile width settings, embedding belongs only to the active group and is saved with the groom and undoable. It is an inset from the guide/card root, not a collision projection: floating roots may need more depth or root reprojection; outward-facing source normals are required. Coarse cards approximate the fade using their available samples.

**Root-to-tip vertex colors** under **Card geometry** writes a per-row RGBA gradient into the mesh's vertex colors. Set **Root Vertex Color (RGBA)** and **Tip Vertex Color (RGBA)**, then **Solid Root Segments**. With 6 samples (5 segments) and a value of 2, rows 0, 1 and 2 are exactly the root color; rows 3 and 4 blend by one-third and two-thirds; row 5 is exactly the tip color. `0` starts fading immediately. All vertices across a ribbon row or tube ring receive the same value, including alpha. This works on guides and children in preview, runtime generation and baked LODs.

The solid segment count is clamped per LOD/Draft resolution to leave at least one fade segment, preserving the tip color even at very low resolution. For a vertex-animation mask, try white RGB with root alpha `0`, tip alpha `1`, and 2 solid root segments. Your shader must read the mesh **COLOR** channel: writing vertex alpha does not automatically enable transparency or animation. The gradient replaces the group's display color in the mesh only while enabled; disabled (the default) preserves existing group-color output. These settings belong to the Card Profile, are Undoable, and are inherited by new grooms with last-used settings. **Reset card setup** restores a default, disabled gradient in a new private profile.

Expand **Child population & variation** to set Children per Guide, root spread, clump, length/width/roll variation, interpolation, and seed. The estimate explains the output:

**Weighted Nearest** blends each child's shape and root orientation from up to four surrounding guides. Weights are calculated at the child's generated root—not at its nominal parent—so children between guides transition smoothly toward whichever guides are spatially closer. Use **Explicit Parent** when a child must follow only the guide that spawned it.

`guides x (children per guide + optional guide card) = approximate card count`

### Define and assign atlas UV sets

Under **Texture & UV Setup**, assign an Atlas Profile or click **Create Atlas Profile**. Assign the **Albedo Atlas** and **First Pass Material** (formerly Card Material). Optionally assign a **Second Pass Material** directly below it to draw the same cards again, like UMAMaterial's second pass. Normal and mask textures are under **Additional texture maps**. If Albedo Atlas is empty, the canvas previews the first-pass material's base texture when available.

Both passes share the same vertices, UVs, vertex colors and atlas bindings, but keep their own shader, alpha/depth/blend settings and render queue. Use a later render queue for the second pass to guarantee first-then-second ordering; the UI warns if its queue is equal or earlier. A common setup is an alpha-clipped first pass followed by a transparent edge pass. Leaving Second Pass empty preserves single-pass rendering. Assigning/removing it is undoable, and both material references are saved and inherited by new grooms. Reset card setup clears both references without deleting material assets.

The Scene, inline material-rendered card preview and runtime generation render both passes. Baked meshes retain the extra draw submeshes; vertices and index buffers are shared, so card/vertex/triangle geometry counts are unchanged, although draw calls and shading cost increase. Primary material slots come first, followed by optional second-pass slots in the same atlas order. **UMA slot/recipe rendering uses the bake UMAMaterial's own Second Pass**: assign the matching material there (or on the overlay template's UMAMaterial). Release validation warns about mismatches; the hair editor does not silently change shared UMA assets or add an extra UMA pass.

The Scene and material-rendered card previews, and runtime generation, apply the atlas textures to private, reusable material instances; neither shared pass material (including a Shader Graph's default material) is edited. Assigned Albedo Atlas textures use the atlas UVs directly, without inherited material tiling/offset. Empty texture channels inherit each material's settings. Groups with different atlas profiles keep separate texture bindings even when they share a material. The materials must still support alpha clipping or transparency; the canvas's Color + Alpha switch does not enable shader transparency.

Directly below the pass materials, select a **Shared Color Table**, then a **Shared Color** from that table. Selecting a color copies its supported UMA shader properties (including color, float, vector and texture parameters) to both assigned pass materials, using the exact property names without overlay-number suffixes. Each shader receives only properties it supports; assigning the same material to both passes edits it only once. The color swatch is informational; channel-mask tints are not guessed as shader parameters. An empty table, missing material or a color with no compatible shader properties is explained inline.

Unlike the automatic atlas binding described above, this is an explicit **edit to the assigned materials**, with Undo support: other objects sharing those materials also change. Only specified, supported parameters are copied. Clearing the selector does not reset previous material values; use Undo to revert. **Apply Shared Color Shader Parameters** reapplies the selection after you edit the table. Assigning a different pass material also applies the selected color. Table and entry selection are saved in the Atlas Profile and inherited by new grooms; applied material parameters are saved with the workspace and used by generated/baked hair. Explicit atlas textures still take precedence in the atlas bindings. This is a copy/apply operation, not a live link to the table at runtime.

Each named **UV Set** is a rectangle on the atlas. Everything is edited directly in the Cards tab; no separate UV editor window is needed:

- **+ Draw Set** or **+ Add Set** starts drawing a new rectangle on the canvas.
- Select a numbered list entry or canvas outline. Drag the rectangle to move it; drag its four corner handles to resize it. **Redraw** replaces the selected rectangle with a fresh drag.
- **Remove** deletes the selected set and removes its assignments from groups in this groom that share the atlas. Undo restores both. Delete also works when the canvas has keyboard focus; Escape cancels a drag without changing the asset.
- **Normalized UV / Pixels** switches exact `X`, `Y`, `W`, and `H` entry. Both use a bottom-left origin. **Snap to pixels** aligns canvas edits to texture pixels.
- **Selection Weight** controls how often the set is chosen relative to other eligible sets.
- **Flip U** and **Flip V** reverse the card texture inside the set.
- **Duplicate** copies a set with a new stable ID, preserving its rectangle, flips, weight, and tags. Unlike drawing an identical rectangle, duplication intentionally creates a separate set.
- **Frame selected (F)** centers the current rectangle. With canvas focus, arrow keys nudge by one texture pixel (Shift: ten); Ctrl/Cmd+D duplicates. Without a texture, nudges use 0.001 UV units.
- **Tags** describe the strip, for example `wide`, `flyaway`, `dense`, or `edge`.

The scrollable set list sits beside the canvas in a wide workspace and below it in a narrow one. Mouse-wheel zooms around the pointer, middle-drag pans, and **Fit** resets the view. Hide **UV outlines** for an unobstructed texture preview. Drawing an identical rectangle selects the existing set. Names and list positions are labels; assignments use stable IDs, so renaming does not break them. UV definitions belong to the shared Atlas Profile: editing them affects every groom that uses that profile.

**Color + Alpha** is the default preview, composited over Photoshop-style light/dark gray checkers. **Color** shows RGB without transparency; **Alpha** shows the texture's alpha mask. Expand **Preview background** to adjust both checker colors and square size, switch to a solid background, or reset the checker defaults. These are preview-only settings; transparency on the generated cards is controlled by the Card Material.

Each UV list entry includes a texture thumbnail. Selecting a set highlights generated cards that use it. With the **Select** scene tool, clicking a generated card identifies its group and UV set and selects that set in the open workspace. The **Material-rendered card** section previews the selected UV set with the assigned material, profile taper/backfaces, a turn slider, and Front/Back buttons. Its width is illustrative; the Scene preview remains the final geometry reference.

Use **Make profile unique** or **Make atlas unique** to save a private resource next to the groom and assign it only to the active group. Atlas copies receive independent UV IDs and preserve the group's selected-set mapping. Texture and material assets remain shared; these buttons do not duplicate or modify those assets.

Choose one assignment mode for the active hair group:

- **Use all** lets every generated card choose from every set in the profile.
- **Use checked** enables the checkboxes beside the numbered sets. For example, check Sets 2, 3, and 7 to restrict this group to `{2, 3, 7}`. Newly drawn sets are automatically checked for the active group.

Each guide card and child card then chooses one eligible set using its stable groom seed and the selection weights. The choice is random-looking but deterministic: rebuilding the same groom keeps the same card-to-set assignment. Different hair groups can use different subsets of the same atlas profile.

In the left-side **Preview & Display** section, set Preview mode to **Cards**, or click **Rebuild Card Preview** to switch to Cards and rebuild. UV changes queue a preview refresh after a drag finishes. An atlas is optional unless **Require Atlas** is enabled; without one, cards use full-range UVs and the fallback preview material. **Use checked** with no valid checked set is a validation error.

**Preview quality** in the left panel offers **Full** or **Draft**. Draft caps preview cards at 1,500 and lengthwise samples at eight, while preserving the authored guide display. Validation and baking always evaluate full release quality, regardless of draft quality, isolation, or solo. The status reads **Updating preview…** while work is queued. UV-only edits reuse existing mesh geometry; geometry-only edits reuse evaluated curves; guide-only modes do not build card meshes. Expensive updates wait until a stroke or workspace drag ends.

## 6. Optimize

Open **Optimize** and watch the live guide, card, vertex, and triangle counts. Set target budgets and configure each LOD:

- Card Fraction reduces the population deterministically.
- Use profile sampling selects each group's profile, or disable it to set a shared LOD sample override that reduces lengthwise geometry.
- Maximum Tube Sides limits tube cost.
- Screen Height and the reserved Reduction enum are disabled: automatic screen-height switching, group merging, and impostors are not currently implemented. Active reduction is deterministic importance-based card thinning.

Inspect every LOD and test deformation on representative equipped avatars before release.

## 7. Validate and bake

Open **Validate & Bake**.

1. Click **Validate All Exported LODs** and resolve every blocking error. This checks the release output, including hidden-but-bake-enabled groups and children, independently of the current preview. Every exported LOD has its own count summary and output issues.
2. Configure the output folder and asset name.
3. Choose the Unity Mesh, UMA SlotDataAsset, OverlayDataAsset, Wardrobe Recipe, and library options needed by the project.
4. Assign the UMA Material, existing Overlay, compatible Race, and Wardrobe Slot when creating those UMA outputs.
5. Use **Select guide** or **Go to setting** on an issue to navigate directly to its source. **Create profile** and **Create atlas** are scoped, undoable fixes for missing resources; locked groups are protected.
6. Click **Dry Run (all output)** to validate without writing output assets. A settings change marks the previous release report as outdated.
7. Click **Bake** when validation is clear. Bake always repeats full release validation and stops before writing output if any exported LOD has a blocking error.

**Save** preserves the groom and dirty linked card profiles and atlases, including an atlas/profile edited through the workspace and then unassigned. It does not save unrelated project assets. The header shows unsaved changes, queued saves, or the last save time. Shared profile/atlas edits affect their other users too. Autosave checks every 30 seconds, waits for strokes, gravity holds, UV drags, text editing, and other workspace gestures to finish, and requires a brief idle period. Unchanged assets do not trigger another recovery write. Recovery snapshots live under `Assets/UMAProjectData/HairCards/Recovery`.

The Setup tab exposes the working **Mirror growth paint X** and **Mirror slice cut X** switches. These mirror across source-local X = 0; there is no global comb/Grab symmetry switch. Procedural mirroring is available as a Mirror modifier.

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
- Move over the displayed guide curve until the brush circle appears. Every spline segment inside the radius contributes influence to its authored controls; a control point does not need to sit inside the circle.
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
- `M`: Toggle X mirroring for Growth paint or the active Cut slice
- `C`: Comb (also enters Groom)
- `G`: Grab (also enters Groom)
- `S`: Smooth (also enters Groom)
- `Shift+R`: Rebuild Preview
- `[` / `]`: Decrease/increase brush radius
- `Shift+[` / `Shift+]`: Decrease/increase brush hardness
- `Shift` + right-drag: Horizontal movement changes brush radius; vertical movement changes hardness. The outer brush circle and inner falloff circle update live, anchored where the drag began (including the mirrored growth brush).
- `Alt` + mouse: normal Scene view navigation

Shortcuts can be remapped in Unity's Shortcut Manager. The `?` button in the Hair Groom header opens this guide.
