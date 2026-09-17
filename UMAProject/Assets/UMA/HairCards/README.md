# UMA Hair Cards

UMA Hair Cards is a guide- and form-driven, non-destructive hair-card authoring system for Unity 6.3 and newer. The editable `HairGroomAsset` is the source of truth; generated meshes and UMA assets are deterministic bake outputs.

For tied-up styles, see the [Gather and Braided Bun guide](BraidedBunGuide.md).
**Gather** provides scalp-following paths, anchored roots, independent root/arrival bend
curves for S-shaped transitions, packed tie rings, pass-through tails, side selection,
mirrored helpers and outward card facing. Length preservation is the default; extending
strands to reach a tie is an explicit option. A linked **Bun** helper drives wrapped
volume and center tuck. The surrounding plait can be extracted to an independently
editable **Braid spline** that follows the bun. Braid splines support direct line dragging,
control-point editing, surface offsets and independent root/tip attachments to scalp or helpers.
**Painted Scalp** generation creates roots directly from painting without authored guides;
unlike filtering fixed grid roots, painting a larger area creates hair in the new region.
The Braided Bun examples use this workflow and retain the saved texture hairline.

For the swept hairstyle, start with the [Swept Clumps guide](SweptClumpsGuide.md).
It covers surface-rooted clump/card populations, local modifier masks, arched ribbons,
the UMA atlas layout, the URP strand shader, and scalp vertex-color Mesh Modifiers.
See the [swept validation report](QA/SweptClumpsValidation.md) for measured costs
and remaining production limitations.

For an editable curly hairstyle, start with the [Curly Volume guide](CurlyVolumeGuide.md).
It covers Ringlets, distance-based curl spacing, automatic shape resolution,
three-strip UV setup and the inline Matte / Natural / Glossy finish controls.
Ringlets also exposes **Curl mesh detail**: error-based ribbon reduction with
Economy/Balanced/Close-up presets, shape/facing tolerances, segment caps and preview
polygon counts. It reduces mesh density without editing the procedural curls.
Open installed examples from **UMA > Hair Cards > Examples**. See the
[Curly validation report](QA/CurlyVolumeValidation.md) for its checks and limits.

For a short, side-parted style under 20,000 triangles at LOD0, see the
[Short Hair Part guide](ShortHairPartGuide.md). Classic, Relaxed, Close Cut,
Natural and Front Flip Natural
include separate editable crown/side groups and three-LOD preview prefabs. The
Short Hair Part preset, additive short-atlas setup and per-group polygon ceiling
readout reuse the existing generation system without changing older grooms.

**Noise → Strand-aligned 2D** follows each strand's transported frame, with
separate sideways/outward amplitudes, physical wavelength, root fade and shared
clump motion. It is opt-in; existing Noise modifiers retain their original behavior.
**Surface Bend** adds a root-to-tip angle profile for lift-and-return shapes such
as a forelock. Both keep roots and segment lengths fixed, respect freeze/stiffness,
and accept painted masks. Use Length first when a shape needs additional reach;
enable final card scalp clearance after deformation. See
[Natural variation and front flips](ShortHairPartGuide.md#natural-variation-and-front-flips).

**Materials & UVs > Strand rendering** offers **Cutout**, **Alpha Blended** and
**Hybrid** for the UMA hair shaders. Hybrid adds a lit soft fringe to depth-writing
strand cores; **Alpha density** retains faint atlas fibers without filling the
transparent background. See [rendering modes and scalp clearance](ShortHairPartGuide.md#keep-fine-strands-choose-the-rendering-mode)
for tuning, second-pass cost, sorting limits and card/scalp clearance.

**Roots (RGBA)** and **Tips (RGBA)** now use their alpha channels for opacity,
independently of vertex colors. **Root color reach** controls the color/opacity
transition; **Root opacity fade** adds a fade from transparent at the very start.
Use Alpha Blended or Hybrid for soft transparency; plain Cutout still has a hard
threshold unless dithered. For older Hybrid materials, click **Sync fringe from
first pass** once to save the corrected core/fringe setup for baked/exported hair.
The stage already corrects its private preview materials without changing your
shared assets. Sync preserves colors and textures but updates the shared pair's
rendering settings. Alpha density boosts atlas fibers, not your chosen opacity.

Select **your group → 5 · Hair Cards → Geometry & Vertex Colors** to edit
**Width along card**, directly below Root Width and Tip Width. Horizontal time
runs from root (0) to tip (1); curve value 1 selects Root Width and value 0 selects
Tip Width. This is a blend between widths, not a width multiplier. The UI reports
the resulting endpoint widths and warns about transitions shorter than a mesh
segment. The Card Profile asset Inspector exposes the same curve with grouped,
plain-language settings. Groom Inspector list entries show group/helper/LOD names
instead of internal IDs; identifiers remain read-only.

For short hair sinking into the scalp, select **Cards > Card geometry** and
enable **Prevent scalp penetration** on each affected group. Start with
**Card clearance = 0.5 mm** and **Root Embed = 0 mm**. The final mesh check covers
ribbon width and sampled face interiors as well as centerlines, without adding
triangles or modifying guides. It applies in preview and bake; rebake existing
exports. It is opt-in for existing grooms and enabled by the Short Hair Part preset.
See [clearance details and limitations](ShortHairPartGuide.md#cardscalp-clearance).

For panel-shaped hair and rail-driven plaits, see the [Braided Bun guide](BraidedBunGuide.md).
Classic, Loose and Compact Copper include a full wrapped bun, a separate surrounding
three-bundle braid, gathered scalp coverage, narrow-strip flyaways, texture-backed
hairline maps and three-LOD preview prefabs.
Create a Grid Panel Group or Braid Group from Hair Nodes; edit its control points
under Shared Helpers. Existing scalp-driven generation remains the default.
Forms can opt into **Use painted root density** to mask roots with Growth / Density.
Grid **Root start variation** staggers the hairline; material **Root opacity fade**
softens card starts without consuming vertex alpha. These controls default off.
Grid **Preserve coverage at LODs** avoids holes from random thinning by distributing
survivors and modestly widening them. It is opt-in and leaves full-detail shape unchanged.

To attach an existing groom to real UMA body slots for skinning, use **Source & Setup →
Bind Character / Race**. [The binding workflow](SweptClumpsGuide.md#attach-an-existing-hairstyle-to-a-uma-character)
preserves authored hair and saves a separate weighted donor and skeleton.

## Node workspace

To leave grooming, click **Exit** immediately after **Save** on the floating
Scene-view toolbar, or **Hair Nodes > Exit Stage**. Closing the stage finishes
the current edit, saves the groom/resources and restores Unity's normal tools.
It returns to the previous stage; it does not close your docked Hair windows.
In narrow Scene views, secondary toolbar controls collapse to keep Save and Exit
visible. The toolbar, status line and help text fit the camera viewport, leaving
space for docked toolbars on both sides. Long status text is clipped within its
row; hover to read the full text. The complete brush controls remain in Hair Properties.

Opening a Hair Card Stage opens three independently dockable Unity editor windows: **Hair Nodes**, **Hair Properties**, and **Hair Preview & Settings**. Hair Nodes is the single workflow navigator: select a typed node to edit its parameters in Properties. Creation, duplication, ordering, removal, validation navigation and stage commands live in the tree window. Properties has no workflow shortcuts, next/back links or second node picker; it retains the selected item's settings and editing actions. The separate Preview & Settings window has **Preview & Visibility** and **Settings** tabs for display, avatar filters, camera focus and resets. Node selection never switches those tabs. Confirmations use popups.

The node tree references existing groom data directly. It does not regenerate guides or replace assets when opened. Each group's branch contains Growth / Density, Optional Maps, Guides, Grooming (sculpt passes and their modifiers), Constraints, Generate Hair (optional clump populations and final cards), Hair Cards (Geometry & Vertex Colors; Materials & UVs), and Scalp Vertex Shading. Shared Helpers, Optimize & LODs, and Validate & Bake have groom-level nodes.

Select a sculpt pass before grooming or adding a modifier. The Grooming branch is a collection, not an implicit sculpt target. Sculpt passes and their modifiers appear in **forward evaluation order, top to bottom**. Dragging or the Hair Nodes **↑ / ↓** buttons only reorder compatible siblings. Pass moves carry their modifiers. This reverses the old stack's visual presentation, not the saved data or the evaluated result. The **Active** toggle bypasses an applicable node independently of selection; disabled parents and zero opacity still suppress their operations.

### Sculpt safely around modifiers

After Spline Flow (or another modifier), use **Add Finishing Sculpt Layer** in Hair Nodes to refine the visible result. It adds an additive pass after the guide stack, including group modifiers and helper constraints, without baking or overwriting earlier work.

To revise an earlier pass, select it and choose **Edit This Layer**. Earlier operations remain active; the selected pass's own modifiers and everything later in that group are temporarily bypassed. The tree marks `[editing]` / `[bypassed]`, and the Scene banner identifies the edit point. Guides, hit-testing, child previews and cards all use that same geometry. **Return to Final Preview** restores the full stack. Selecting another node also leaves this temporary mode; saved Active/visibility flags never change. A terminal unmodified pass can enter editing directly because its shape already matches the final preview. Locked/inactive/zero-opacity passes never silently redirect strokes to a new pass.

Sculpt deltas use the evaluated point count at their own layer, including after resampling. Comb, Grab, Smooth, Clump, Part, handles and gravity preserve edit-stage segment lengths and anchors; Length and layer-local Cut intentionally change length. Cut no longer truncates the underlying authored guide or rewrites other layers. Empty Override passes initialize from their incoming shape on first edit. Edit-point state is transient and never affects saved assets, runtime evaluation or baking.

Use the Nodes search to locate an operation; matches retain their parent context. Optional Maps starts collapsed. Node selection and tree state are groom-local preferences. Reopen any window from **UMA > Hair Cards > Hair Nodes / Hair Properties / Hair Preview & Settings**. Closing a panel does not close the stage or other panels. Existing visibility preferences are preserved in the new window.

## Quick start

1. Select a readable `Mesh`, generated `DynamicCharacterAvatar`, or existing `HairGroomAsset`; choose **UMA > Hair Cards > Open Hair Card Stage**.
2. Select **Source & Setup** to inspect binding. Open **Preview & Visibility** to isolate and focus the scalp.
3. Select the group's **Growth / Density** node and paint: 0 means no growth; 1 means full density. Optional Maps contains the independent multiplier and styling maps.
4. Select **Guides**, preview deterministic generation, then Accept or Replace Generated Only. Preview is non-destructive; add manual guides as needed.
5. Select **Grooming**, add a **Sculpt Pass**, and use Comb, Grab, Smooth, Length, Cut, Width, Clump, Part, Freeze, Erase or the gravity hold. Cut is a finite camera-projected slice with optional mirroring. Length and Cut intentionally change length; positional grooming and gravity preserve segments.

**Erase** is the full-width button below the sculpt brush grid. Left-drag its red circle to delete whole guides touched anywhere along their displayed curves. Radius and Edit scope determine which guides are removed; Hardness, Strength, Reverse and Root Influence do not apply. Frozen points (even partially frozen ones) protect the entire guide. Erase removes authored guides and their deltas from every sculpt pass in the active group; generated children/cards refresh after release. This is not a layer-local cut, so hiding the pass cannot restore deleted guides—use Undo, which restores the whole stroke. Erase can target the final modifier result without entering upstream editing. It requires a selected, active, unlocked sculpt pass and is not restored as the active brush when reopening a groom.
6. Add modifiers beneath a pass; select one to edit its properties. **Spline Flow** draws directional surface paths for hair flow and card facing.
7. Select **Generate Hair**. Apply **Swept Clumps Preset…** in the tree for independently surface-rooted clumps/cards, or leave Surface generation off to retain children-per-guide. Select **Hair Cards** for resources, then Geometry and Materials & UVs for detailed setup.
8. Select **Optimize & LODs**, then **Validate & Bake**. Resolve blockers, dry-run, and bake UMA assets.

Full user guide: [Node Workflow & Reference](../Docs/Hair%20Cards%20-%20Quick%20Start.md).
QA checklist: [Hair Cards Manual QA](QA/HairCardsManualQA.md).

## Authoring model

- Groups separate coverage, volume, detail, flyaway, short, facial, brow, lash, and custom hair.
- Surface anchors use source asset identity, submesh, triangle, and barycentric coordinates. A topology fingerprint prevents silent root movement after source changes.
- Growth maps can use vertex fields or precise, texture-backed face tiles, without changing source topology or UVs. **Growth / Density** is the primary map. An optional Density Multiplier and Length, Flow, Lift, Width, Clump, child count, profile blend, and LOD importance maps are advanced controls.
- Guides are authored splines. Child cards are deterministic interpolation results and are never hidden editable state.
- Sculpt layers store guide point position, width, and roll deltas.
- Ordered guide/child modifiers include resample, length, width, smoothing, lift/gravity, flow, clump, parting, curl, wave, noise, twist, helper following, projection, collision, and mirroring.
- Helpers and constraints are embedded, stable-ID data. Curve rails, attractors, repulsors, cages, collision shapes, part lines, braid rails, and other helper roles share one model.
- Profiles generate flat or arched ribbons (1–4 cross-width spans), optional adaptive length/turning-based tessellation, and tapered polygonal tubes with 3–12 sides. Explicit second-pass materials remain supported.
- Atlas profiles provide weighted UV regions, flips, textures, and preview materials.

## Modifier coordinates

**Modifier coordinates:** curves, Flow Align directions, Part/Mirror planes, flow paths and helpers are authored in source-mesh local space, not camera space. Gravity explicitly converts world forces through the object and guide pose. Lift defaults to each root's outward normal, with a **Closest Surface Normal** option; it converts normals by inverse transpose before writing source-local displacement. Lift distance is in source units and does not override pinned roots, frozen points or segment lengths. Linked helpers retain an exact source-relative affine snapshot, including scaled/mirrored/sheared parents, for collision and baked/runtime evaluation. See [the complete modifier coordinate audit](QA/HairModifierCoordinateReview.md).

## Growth and density

### Precise texture painting

Select a map in Hair Nodes, then choose **Paint storage → Texture map (precise)** in Hair Properties.
The conversion popup preserves current paint. **Texels per face edge** controls detail: 16 is a
good starting point; 32 suits hairlines; 64 is for particularly large source polygons. Detail is
per original triangle, not a global atlas size. Only edited faces allocate detailed texels, and
the panel reports their memory use. Small brushes still need to cover at least one texel; raise
detail if a very small brush misses on a large polygon. Higher settings cost more memory and
increase the first generation rebuild after painting; subsequent grooming reuses cached fields.

The paint coordinates are independent of the character's UVs, including mirrored/overlapping UVs.
Painting evaluates brush distance on the surface, across face and UV boundaries. The Scene overlay,
guide placement, surface-generated clumps/cards, styling masks, and hairline refinement all sample
the same texture field. Original mesh triangles, skin weights, guide anchors and card tessellation
are unchanged. This is texture resolution, **not** authoring-mesh subdivision.

Brush hardness/strength, mirror, temporary Shift erase, visible fills, invert, smoothing, Undo,
and map cut/copy/paste work in both modes. Selection → Map blends the selected original vertices'
influence over texels; Map → Selection selects source faces containing qualifying texels, even
when their original vertices are unpainted. Focus current area includes those faces' bones.
Texels are saved inside the groom and included in duplication/recovery; no external texture file
or readable character UV texture is required. New grooms inherit the saved storage/detail choices,
but never the previous groom's paint. Resetting last-used setup defaults clears that inheritance.

Conversion does not invent a new hairline: repaint or smooth an existing jagged vertex boundary.
Reducing detail, or converting back to Vertex, shows a warning because it discards detail; Undo
restores it. Atlas precision controls **where roots grow**, not where the entire ribbon lies.
Use **Refine hairline** width/length/lean controls and groom inward where broad cards overhang the
boundary. Scalp vertex-color shading remains limited to the output body mesh's own vertices.
With texture maps it shades conservatively inside fully painted faces, so a coarse vertex-color
triangle cannot extend a dark stain into the unpainted forehead or part.

### Density and placement

Use **Growth / Density** as the single everyday paint map. Leave **Density Multiplier (optional)**
at its default `1`. Its controls, reset-to-1 button, and other styling maps are under
**Optional Maps**. If the multiplier contains paint, the primary map's Properties reports it even
when the optional branch is collapsed. Selecting a map opens its properties and activates painting.
Collapsing its selected parent moves selection to the collection and leaves paint mode, so a hidden
map cannot remain paint-active. Tree state is saved per groom and clears with **Reset editor options**.

In **Guides**, **Guides at Full Density** is a budget for the *current painted footprint*, not the
entire source mesh and not a fixed guides-per-square-meter value. The target is the budget multiplied
by the surface-area-weighted average of `Growth / Density × Density Multiplier`, rounded to the nearest
whole guide (half rounds up). For example, a budget of 100 generates a target of 100 at uniform paint
1, 50 at 0.5, and 25 at 0.5 with a 0.5 multiplier. Zero generates no guides. Minimum Spacing and attempt
limits can reduce the actual placement below that target; the preview reports all three numbers.

The footprint includes nondegenerate source triangles (texture sampling cells in Texture mode) touched by nonzero primary paint. Completely
unpainted body triangles are excluded, while multiplier-zero triangles remain in the footprint and
contribute zero density. Soft edges reduce average density. The budget is recalibrated to the current
footprint each preview: expanding or completely erasing parts of the region changes its reference
area. This avoids diluting scalp density by the area of an entire combined character. The exact
integral of the two interpolated maps is used, rather than averaging vertex counts; finer mesh
tessellation does not change the result for the same continuous field.

Both density maps use values in `[0, 1]`. The optional multiplier only attenuates the primary paint;
`1` leaves it alone and `0` suppresses it. It is useful for independently thinning a crown without
repainting a hairline, but it is not required. Map visibility controls the overlay, not the map's effect.
**Focus current area** continues to frame the primary painted region, independently of the multiplier.

Paint never moves or deletes authored guides. **Preview Density-Adjusted Guides**, then
**Replace Generated Only**, refreshes generated guides; **Accept** adds another batch. Replacement
removes those guides' previous grooming, so save before replacing a styled layout. With **Surface
generation enabled**, paint also drives independent clump/card roots and refreshes their footprint
after the stroke. With Surface generation off, children inherit the accepted guide layout;
**Children per Guide** and its map control fill. Paint/source/full rebuild changes discard stale
temporary previews. No legacy fixed-count guide-generation mode is retained.

## Spline-driven flow and card facing

In **Hair Nodes → Grooming**, select a sculpt pass, choose **+ Modifier → Spline Flow**,
then select the modifier row. All path controls live in its property box; no extra window is needed.
Unlike **Flow Align** (one constant direction), Spline Flow defines a surface-following direction
field and an outward-facing ribbon frame.

1. Click **Draw New**, then drag **on the head from the part/root area toward the desired ends**.
   A line and arrows appear while drawing; green marks START, orange marks END. Release to apply
   the cards. Draw a few paths around the hairstyle, all pointing in the intended root-to-tip direction.
2. Select a path in the inline list and choose **Edit Points**. Drag a point to slide it along the
   visible source surface. Shift-click near a segment to insert a point; Delete or the inline button
   removes the selected point (at least two remain). **Reverse** swaps the flow direction.
3. **Influence radius** controls which hair roots a path reaches, in source-local units. Influence
   fades at the radius boundary. **Nearby splines** blends up to four nearest paths using distance
   weights, not one winning guide or all nearby control points. Adding points to a path does not
   increase that path's weight. Paths on the opposite-facing side of the source are excluded.
4. **Follow direction** steers the guide segments along those paths. Roots stay fixed and every
   guide segment retains its incoming length; freeze, stiffness, Root Influence, the root-to-tip
   ramp and layer opacity remain respected. Paths guide directions, not positions: no attraction
   to the lines or shared endpoints. Past a path's end, its final direction continues. Add **Resample**
   before Spline Flow if coarse guides cannot represent a tight turn.
5. **Outward facing** orients card fronts using blended scalp normals. Set **Follow direction = 0**
   and **Outward facing = 1** to orient an already groomed hairstyle without reshaping it. For hair
   standing perpendicular to the scalp, path-forward supplies the otherwise ambiguous facing.
   **Facing bank** rotates the reference face; authored roll, Twist and child roll variation remain
   offsets. Set child roll variation to zero for consistent ribbon facing. The texture's root-to-tip
   UV direction is still configured in its atlas UV set, not by reversing the flow spline.
6. **Lift from surface** adds lift to the flow direction (default 10°). This is not a collision
   solver: for very tight curves or body contact, put Collision/Gravity after flow and inspect the
   result. Parallel paths retain root spacing, but converging paths can intentionally converge.

**Mirror new paths** creates an independent copy across the groom's configured symmetry plane
(X by default), reattaching every point to the visible source mesh. **Mirror** copies an existing
path. If a complete, nearby matching surface cannot be found, no partial mirrored path is created
and the status bar explains why. Mirrored paths can be edited independently; they are not linked.
Duplicate/reverse/remove, path edits and mirrored drawing support Undo. Escape, leaving the Scene
view or switching tools cancels an unfinished gesture without changing saved paths. Paths stop at
surface gaps rather than joining unrelated body parts. **Done** returns to Comb.

**Point spacing** sets the detail of newly drawn paths (maximum 256 points each). Editing temporarily
shows the active modifier's paths regardless of helper visibility; otherwise its **Show paths when
not editing** option and **Preview & Visibility → Show helpers** control them. The existing scene-depth
toggle applies to paths and handles. Guide picking handles are suppressed while editing flow paths
so they cannot steal the gesture. Path data and modifier parameters are stored in the groom asset;
duplicated modifiers/layers own independent copies.

Paths are anchored by source mesh, triangle and barycentric coordinates. The field evaluates in
source-local space, and posed previews transform its positions and facing normals with the character,
not the camera. Guide-domain changes pass to children through the existing weighted interpolation;
**Guides And Children** applies them once. **Children** applies only to generated children.
Runtime generation and baked meshes use the same evaluation and frame construction.

Performance: each field is prepared once per evaluation with cached source channels and reusable
storage. Nearby-path searches run once per strand with bounds rejection; subsequent points use
arc-distance sampling of the chosen paths. Profiler markers are `HairCards.SplineFlow.BuildField`
and `HairCards.SplineFlow.Apply`. Existing grooms without Spline Flow keep the original frame behavior.

## Play-mode API

No service, local model, or provider is required. Generation is deterministic C# geometry code and works in a player build.

```csharp
using UMA.HairCards.Runtime;

HairGroomRuntimeAPI.GeneratedHair generated =
    HairGroomRuntimeAPI.Generate(groomAsset, lodLevel: 0);

HairGroomRuntimeAPI.ApplyTo(generated, meshFilter, meshRenderer, fallbackMaterial);

// Dispose when replacing or removing the generated mesh.
generated.Dispose();
```

For component-driven use, add `HairGroomRuntimeComponent` beside a `MeshFilter` and `MeshRenderer`, assign a groom, and call `Regenerate()` or `SetLodLevel()` as needed.

## Bake outputs

The bake pipeline evaluates and validates everything before it writes. Existing assets are updated in place so references remain stable.

**UMA Material** in Validate & Bake requires an `UMAMaterial` configuration asset,
not the Unity `Material` used to render the cards. Existing Overlay requires an
`OverlayDataAsset`, and Compatible Race requires `RaceData`. An incompatible saved
assignment is shown with a warning and is preserved until you replace or clear it.
Validation blocks outputs that would use that invalid reference; unused UMA
references do not block a mesh-only bake.

- Unity Mesh assets for LOD 0 and every configured additional LOD.
- Closest-scalp-vertex skin weights and source bind poses when the source exposes compatible weights.
- `SlotDataAsset` containing the generated geometry.
- `OverlayDataAsset`, either by reusing an assigned overlay or from an assigned UMA material and the first available hair atlas.
- `UMAWardrobeRecipe` when a RaceData, wardrobe slot, UMA slot, and overlay are available.
- Optional UMA global-library registration.

The generated UMA slot supports multiple mesh submeshes, but a production hairstyle should keep material count low for draw-call efficiency. Always equip the baked recipe on representative avatars and inspect deformation before release.

## Shortcuts

Shortcuts are registered with Unity's Shortcut Manager and can be remapped:

- `Q`: Select
- `P`: Paint Growth
- `M`: Toggle X mirror for Growth paint or Cut
- `C`: Comb
- `G`: Grab
- `S`: Smooth
- `Shift+R`: Rebuild Preview

Scene navigation retains Unity's Alt-based controls.

## Assemblies

- `UMA.HairCards.Core`: versioned data, evaluation, generation, geometry, UVs, skin-weight transfer, and validation.
- `UMA.HairCards.Runtime`: player-facing API and component.
- `UMA.HairCards.Editor`: stage, workspace, commands, recovery, diagnostics, and UMA bake pipeline.
- `UMA.HairCards.Editor.Tests`: deterministic data, generation, meshing, validation, skinning, and runtime API tests.

See [HairCardsManualQA.md](QA/HairCardsManualQA.md) for the release checklist and `Assets/UMA/Plans/HairSystem.MD` for the full product design.
