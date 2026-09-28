# AAA Hair Card Authoring System Plan

The proposed system is a dedicated `HairCardStage : PreviewSceneStage`: a non-destructive, guide-driven grooming environment that produces optimized, skinned UMA hair-card meshes and LODs.

It should feel like sculpting hair, not manually modeling hundreds of strips.

## 1. Product vision

The artist workflow should be:

1. Open Hair Card Stage from a generated `DynamicCharacterAvatar`, head/scalp slot, or existing `HairGroomAsset`.
2. Select or paint the scalp regions where hair may grow.
3. Paint density, length, direction, lift, width, clumping, and other attributes.
4. Draw, place, or procedurally generate a manageable number of guide splines.
5. Generate a configurable number of child strands/cards from each guide.
6. Comb and sculpt the guides and generated hair directly in the Scene view.
7. Use helper curves, meshes, volumes, part lines, and collision objects to form the hairstyle.
8. Choose flat ribbons, tapered polygonal cones/tubes, or future custom profiles.
9. Assign or bake hair-atlas textures and UV regions.
10. Generate and inspect LODs.
11. Validate intersections, coverage, topology, UVs, skinning, and overdraw.
12. Bake the groom into standard Unity meshes, UMA slots, overlays/materials, and wardrobe recipes.

The authoring groom remains editable. Generated meshes are disposable outputs that can always be deterministically rebuilt.

This follows the strongest ideas from existing systems:

- Hair Tool uses 3D curves, guide meshes, deformers, input masks, automatic UV handling, profiles, texture baking, and reusable presets. [Hair Tool documentation](https://joseconseco.github.io/HairTool_3_Documentation/)
- XGen combines brushes, sculpt layers, masks, and an ordered modifier stack. Its brushes include density, length, cut, width, twist, comb, grab, smooth, noise, clump, part, and freeze. [XGen grooming tools](https://help.autodesk.com/cloudhelp/2022/ENU/Maya-CharEffEnvBuild/files/GUID-A00D16BD-9ECC-4C63-BAB5-B0278BF596BB.htm)
- Houdini generates cards by clustering nearby curves, calculating width and orientation, and matching cards to suitable texture strips. [Houdini Hair Card Generate](https://www.sidefx.com/docs/houdini/nodes/sop/haircardgen.html)
- Unreal's production workflow divides hair into coverage, mid, top/detail, flyaway, and short-hair groups, with separate geometry and texture budgets. [Unreal Hair Card Generator workflow](https://dev.epicgames.com/documentation/unreal-engine/creating-hair-cards-and-lods-using-hair-card-generator)

## 2. Fit with the current UMA project

The system should be a new module, not another mode added to the already large vertex or texture-paint stage classes.

Useful existing patterns include:

- `VertexEditorStage.cs`: preview-stage lifecycle, avatar setup, vertex selection, sculpting, painting, symmetry, and slot interaction.
- `TexturePaintStageWindow.cs`: robust stage lifecycle, workspace panels, autosave, recovery, warnings, and spline editing.
- `MeshReconstructor.cs`: surface reconstruction, raycasting, topology connectivity, UV lookup, and closest-surface queries.
- `WorldSpaceStrokeSampler.cs`: stabilized, pressure-aware, world-space stroke sampling.
- `TexturePaintDocument.cs`: versioned document and recovery patterns.
- `TexturePaintTypes.cs`: world-space splines, control points, surface anchors, per-point width, roll, flow, and tangent modes.

The hair tool should reuse or extract neutral geometry and input concepts, but must not depend on the Overlay Painter's texture-specific controller or document model.

Recommended layout:

```text
Assets/UMA/HairCards/
    Runtime/
    Editor/
        Stage/
        Workspace/
        Tools/
        Export/
        Validation/
    Core/
        Geometry/
        Groom/
        Modifiers/
        Meshing/
        Skinning/
        LOD/
    Shaders/
    Tests/
    QA/
    Samples/
```

Assemblies:

- `UMA.HairCards.Core`
- `UMA.HairCards.Runtime`
- `UMA.HairCards.Editor`
- `UMA.HairCards.Tests`
- `UMA.HairCards.Editor.Tests`

## 3. Core architecture

```text
HairCardStage
    └── HairGroomController
          ├── SurfaceBindingService
          ├── MaskFieldService
          ├── GuideSplineEngine
          ├── GroomModifierEvaluator
          ├── ChildStrandGenerator
          ├── HelperConstraintSolver
          ├── CollisionSolver
          ├── CardMeshGenerator
          ├── HairAtlasMapper
          ├── HairSkinningBuilder
          ├── HairLodGenerator
          ├── HairValidator
          └── UmaHairExporter
```

The stage owns only lifecycle and interaction. Algorithms, state, UI, and exporting remain separate and testable. This prevents the new stage from becoming another several-hundred-thousand-byte monolith.

Unity's `PreviewSceneStage` is the correct foundation and is explicitly designed for isolated editing contexts. [Unity PreviewSceneStage API](https://docs.unity3d.com/ja/current/ScriptReference/SceneManagement.PreviewSceneStage.html)

## 4. Authoring data model

### `HairGroomAsset`

The source-of-truth `ScriptableObject` should contain:

- Schema version and migration history.
- Stable groom UUID.
- Source avatar/race/slot binding.
- Source mesh geometry, topology, UV, skeleton, and material fingerprints.
- Hair groups.
- Surface masks and vector fields.
- Guide splines.
- Sculpt layers.
- Modifier stacks.
- Child-generation settings.
- Card profile settings.
- Helper bindings.
- Collision settings.
- Hair-atlas profile.
- Skinning settings.
- LOD profiles.
- Bake/export records.
- Last validation report.
- Deterministic seeds.

Large guide and mask arrays should live in versioned binary sidecars with checksums, while the ScriptableObject keeps readable metadata and references. This will make undo, source control, autosave, and migrations manageable.

### Stable identity

Persistence identity must never be derived from process-local Unity object identifiers.

Use:

- `Guid.NewGuid().ToString("N")` for groom, group, guide, modifier, layer, and helper IDs.
- Asset GUIDs for project assets.
- Serialized `GlobalObjectId` values for scene helper bindings.
- An explicit `HairHelperId` component with a serialized UUID for helpers that may be duplicated or moved.
- UMA slot names plus source asset GUIDs and mesh fingerprints for surface ownership.
- Deterministic hashes for generated artifacts.

### Surface anchors

Each root stores a durable `HairSurfaceAnchor`:

- Source slot stable ID.
- Source submesh and triangle.
- Barycentric coordinate.
- Surface normal and tangent.
- Normal offset.
- Optional UV.
- Topological-region ID.
- Source topology fingerprint.
- Fallback closest-surface information.

Triangle and barycentric data are fast when topology matches. When the mesh fingerprint changes, the tool enters repair mode, reprojects roots, assigns confidence scores, and reports ambiguous or failed bindings instead of silently moving hair.

## 5. Hair groups and layers

The tool should encourage layered construction:

| Default group | Purpose |
|---|---|
| Coverage | Wide, low-cost cards that hide the scalp |
| Mid volume | Establishes the hairstyle's large forms |
| Top/detail | Narrower cards, visible clumps, and silhouette detail |
| Flyaways | Sparse isolated cards |
| Short hair | Hairline, sideburns, beard, fuzz, and transitional strands |
| Custom | Braids, ponytails, eyelashes, eyebrows, accessories |

Every group has independent visibility, lock state, masks, guides, modifiers, child count, profile, material, UV policy, collision settings, and LOD budgets.

Groups also act as interpolation barriers. Guides must not interpolate across a part line or between unrelated hairstyle regions merely because their roots are spatially close.

## 6. Defining the growth area

Both requested approaches should be supported.

### Vertex and polygon selection

- Point, box, lasso, and paint selection.
- Vertex, triangle, connected-region, UV-island, and entire-slot selection.
- Add, subtract, replace, grow, shrink, invert, feather, and smooth.
- Front-facing only, visible only, through-selection, or connected-only.
- Convert selection to a named mask or hair group.
- Import existing vertex colors or texture masks.

### Surface painting

The scalp supports named scalar and vector channels:

- `Region`
- `Density`
- `Length`
- `RootWidth`
- `TipWidth`
- `Lift`
- `Clump`
- `Frizz`
- `Curl`
- `Freeze`
- `CollisionWeight`
- `ChildCountMultiplier`
- `CardProfileBlend`
- `LODImportance`
- `FlowDirection`, stored as a tangent-space vector

Brushes should use geodesic or topology-aware distance so a stroke on one side of an ear or folded surface does not leak through to a nearby disconnected surface.

Mask operations should include fill, blur, sharpen, grow, shrink, remap curve, multiply, min/max, noise, and import/export.

## 7. Guides and child strands

### Guide creation

Artists can create guides by:

- Drawing directly in 3D.
- Drawing on the surface and lifting the result.
- Clicking a root and dragging a tip.
- Placing individual guides.
- Automatically distributing guides in a painted region.
- Duplicating, mirroring, interpolating, or extending existing guides.
- Converting selected mesh edges or helper curves.
- Generating guides from the painted flow field.
- Copying guides between compatible UMA races or scalp meshes.

Guides should use cubic Bezier or Catmull-Rom data with:

- Root surface anchor.
- Control points.
- Per-point radius/width.
- Roll/twist.
- Stiffness and pin weights.
- Freeze weights.
- Profile scale.
- Group membership.
- Persistent stable ID.

### Child count

Each guide has:

- `Child Strands`: exact integer count.
- Group default plus per-guide override.
- Painted multiplier.
- Include/exclude guide itself as an output card.
- Root spread radius.
- Root distribution shape.
- Length, width, roll, offset, and shape variation.
- Clumping strength.
- Seed.
- Interpolation mode.

For example, a guide with four children normally produces five card centerlines: the guide plus four procedural children.

Children are generated deterministically and are normally not serialized. The same source groom and seed must produce byte-equivalent topology and equivalent floating-point output on the supported editor platform.

### Child interpolation

Child shapes should be derived from multiple nearby guides, not merely copied from the nearest guide.

Recommended method:

1. Generate roots using topology-aware blue-noise/Poisson sampling.
2. Find nearby guides in the same group and region.
3. Measure root distance geodesically across the scalp.
4. Interpolate normalized guide shape, length, width, roll, and other attributes.
5. Preserve the child's own anchored root frame.
6. Apply clump, variation, modifier, helper, and collision results.
7. Reject or repair self-crossing and invalid curves.

Interpolation modes:

- Nearest guide.
- Weighted nearest guides.
- Region-aware barycentric interpolation.
- Flow-field aligned.
- Clump-parent hierarchy.
- Explicit parent guide.

## 8. Sculpting and combing

There should be two complementary grooming domains.

### Surface-flow grooming

A Comb Flow brush paints tangent-space direction onto the scalp. It influences:

- Automatic guide generation.
- Root direction.
- Short-hair lay.
- Child interpolation.
- Part and swirl behavior.

This is ideal for short hair, beards, eyebrows, animal fur, and establishing a first-pass hairstyle.

### Three-dimensional curve grooming

The main AAA brush set should include:

- Select
- Freeze/unfreeze
- Add/place
- Delete/density
- Comb
- Grab
- Smooth/relax
- Straighten
- Lengthen/shorten
- Cut
- Extend
- Width
- Taper
- Lift/puff
- Flatten
- Clump
- Part
- Twist/roll
- Curl/coil
- Wave
- Noise/frizz
- Repel
- Collision push-out
- Project to helper
- Mirror/symmetrize
- Resample
- Simplify by angle

Common settings:

- Radius, strength, hardness, falloff, spacing, and stabilization.
- Pressure affects size, strength, length, or width.
- Guide/child/both target.
- Root, tip, or full-strand influence.
- Root-to-tip influence curve.
- Preserve length.
- Preserve volume.
- Connected group only.
- Visible only.
- Collision enabled.
- X symmetry or custom symmetry helper.
- Accumulating or absolute behavior.

Root locking is mandatory by default. Brushes should affect an arc-length region of the curve, not merely world-space CVs, to provide predictable behavior on curves with different resolutions.

## 9. Sculpt layers and modifier stack

Destructive edits are insufficient for production iteration. The groom should evaluate as:

```text
Anchored base guides
→ Sculpt layers
→ Ordered group modifiers
→ Child generation/interpolation
→ Per-child modifiers
→ Helper constraints
→ Collision and push-out
→ Card meshing
→ UV, skinning, LOD, and validation
```

Sculpt layers support:

- Name, visibility, lock, opacity, and mask.
- Additive or override blending.
- Per-point positional, width, roll, and length deltas.
- Duplicate, merge, reorder, mute, and bake.

Modifier types:

- Resample
- Simplify
- Length
- Width/taper
- Smooth
- Flow align
- Lift
- Clump and secondary clump
- Part
- Curl/coil
- Wave
- Noise/frizz
- Twist
- Gravity/drape
- Helper follow
- Surface projection
- Collision
- Push-out
- Mirror
- Trim by mesh
- LOD reduction

Every modifier should have a mask, root-to-tip ramp, deterministic seed where applicable, and an interactive enable/disable switch.

## 10. Helper object system

Helpers should be first-class authoring entities, not arbitrary transforms referenced by fragile hierarchy paths.

### Helper types

- Curve rail
- Chain/line
- Part line
- Surface mesh
- Guide grid
- Sculpt cage
- Attractor
- Repulsor
- Plane
- Collision mesh
- Sphere/capsule/box collision volume
- Bun or volume target
- Braid rail
- Ponytail binding ring
- Symmetry object
- Bone-chain preview

### Constraints

A guide or group may contain ordered constraints:

- Attach root
- Follow curve
- Track tip
- Conform to surface
- Cage deform
- Attract/repel
- Maintain distance
- Aim
- Match roll
- Preserve length
- Collision
- Mirror
- Blend between helpers

Each constraint exposes:

- Weight.
- Root-to-tip ramp.
- Group or painted mask.
- Offset.
- Twist.
- Local/world alignment.
- Projection direction.
- Preserve-length toggle.
- Live, cached, or baked evaluation.

A helper can be embedded in the groom or linked externally. Missing external helpers produce explicit validation errors. Baking can either preserve the dependency or bake it into sculpt-layer deltas.

## 11. Card profiles and geometry

### Flat ribbon

Required controls:

- Width curve from root to tip.
- Per-point width paint.
- One or more width subdivisions.
- Camber/roundness.
- Root and tip taper.
- Roll/twist.
- Root offset from scalp.
- Centered, left-biased, or right-biased centerline.
- Optional root flare.
- Optional end cap.
- Authored normals or surface-blended root normals.

### Tapered polygonal cone/tube

Required controls:

- Configurable side count, minimum three.
- Root and tip radius.
- Radius curve.
- Open or capped root/tip.
- Smooth or hard profile normals.
- Per-point ellipticity and flattening.
- Roll/twist.
- Adaptive longitudinal subdivisions.

This profile serves stylized clumps, dreadlocks, braids, whiskers, eyelashes, and anime hair spikes in addition to conventional cards.

### Extensible future profiles

The profile interface should later permit:

- Cross cards.
- Double ribbons.
- Custom mesh profiles.
- Braids.
- Feather profiles.
- Shell layers.
- Mixed profiles within a group.

### Frame generation

Card orientation must use rotation-minimizing/parallel-transport frames rather than repeated `LookRotation` calculations. This avoids sudden 180-degree ribbon flips.

The mesher should:

1. Resample by arc length or adaptively by curvature.
2. Calculate a stable initial root frame.
3. Parallel-transport the frame down the spline.
4. Apply authored roll.
5. Generate the chosen profile.
6. Calculate explicit normals and tangents.
7. Generate UV and vertex attributes.
8. Reject NaNs, zero-area faces, and degenerate segments.

Unity's writable `MeshData` APIs support job-friendly mesh construction and should be used for final buffers. [Unity writable MeshData API](https://docs.unity3d.com/ja/current/ScriptReference/Mesh.AllocateWritableMeshData.html)

## 12. UVs, textures, and materials

### Hair atlas profile

A `HairAtlasProfile` describes named UV regions:

- Dense strands.
- Medium strands.
- Sparse strands.
- Clumps.
- Flyaways.
- Short hairs.
- Braids or tubes.
- Root-heavy and tip-heavy variants.

Each region stores:

- UV rectangle.
- Aspect range.
- Density and category tags.
- Allow mirroring.
- Padding.
- Root and tip orientation.
- Preferred card width/length range.
- Material index.

Cards can be assigned manually or automatically by aspect ratio, group, density, and visibility. Houdini uses a similar aspect-matching strategy to limit texture stretching. [Houdini hair-card workflow](https://www.sidefx.com/docs/houdini/fur/haircards.html)

### Texture maps

The complete baker should support:

- Base color.
- Alpha/coverage.
- Normal.
- Depth.
- Ambient occlusion.
- Root-to-tip gradient.
- Tangent/flow.
- Strand or clump ID.
- Random variation.
- Roughness.
- Optional packed output profiles.

The first production milestone can use existing UMA hair atlases. Procedural atlas baking should be a later milestone because it is effectively a second substantial tool.

An "Open Atlas in Overlay Painter" command can hand generated textures to the existing painter for touch-up without coupling the hair document to the painter's internal data model.

## 13. Skinning and UMA output

### Root skinning

Root weights should be barycentrically interpolated from the source scalp vertices.

Three modes are useful:

- `Root Rigid`: propagate root weights down the whole card; appropriate for short hair.
- `Surface Follow`: sample nearby source-surface weights along the card's attachment region.
- `Generated Bones`: blend from scalp weights into generated guide-chain bones for long hair and secondary motion.

Weights must be normalized, validated, and written using current `BoneWeight1` data.

### Generated bones

Optional generated rigs should support:

- One chain per selected guide or clump.
- Shared chains across nearby cards.
- Configurable bone spacing and maximum bone count.
- Root attachment to the UMA skeleton.
- Automatic card weighting along normalized strand length.
- Preview jiggle or gravity.
- Bake to regular skeleton/skin data.

Runtime physics is a separate layer. The core hair creator must remain useful without a custom runtime component.

### UMA export

The exporter should create:

- Mesh assets for each LOD.
- `SlotDataAsset` outputs.
- Required overlay/material assets.
- Optional `UMAWardrobeRecipe`.
- Optional generated bone definitions.
- Provenance record pointing back to the source groom.
- Generation hash and settings summary.
- Validation report.

Generated assets should go into a dedicated folder and be replaced through Unity serialization APIs. Original slots, imported meshes, and native/binary Unity assets must never be text-edited or overwritten.

## 14. LOD and game optimization

LOD must be designed into the groom rather than added after authoring.

Per-group LOD controls:

- Child/card count.
- Guide importance.
- Curve simplification tolerance.
- Longitudinal segments.
- Ribbon width subdivisions.
- Cone side count.
- Flyaway removal.
- Card merging.
- Profile substitution.
- Texture-region reuse.
- Maximum screen-space error.

Recommended LOD behavior:

- LOD0: full authored shape and silhouette.
- LOD1: fewer children and curve segments.
- LOD2: retain coverage and major clumps, remove most flyaways.
- LOD3: simplified shell/coverage cards.
- Optional billboard or hairstyle-cap impostor.

The budget panel should report:

- Guides and generated cards.
- Vertices and triangles by group and LOD.
- Bone count.
- Texture memory.
- Estimated transparent overdraw.
- Average cards per screen tile.
- Scalp coverage.
- Intersection counts.
- Number of unique atlas regions.

Transparent overdraw is often more important than raw triangle count, so the validator should include camera-based overdraw heatmaps.

## 15. Stage user interface

The UI must make a complex procedural system feel like a short sequence of understandable artistic tasks. It should borrow UMA's established stage lifecycle, compact workspace, native Scene-view overlays, status messages, autosave, and recovery conventions while avoiding a wall of permanent panels.

### UX goals and rules

1. **Show the next useful action.** Empty states and blockers always provide a direct action such as **Paint Growth Area**, **Generate Guides**, or **Choose Atlas**.
2. **Organize by artist task, not subsystem.** UVs, modifiers, skinning, helpers, and LOD internals appear when relevant instead of becoming equal top-level modes.
3. **Keep the viewport primary.** Common grooming requires the Scene view, a compact tool rail, and one contextual inspector; secondary panels open only when needed.
4. **Use progressive disclosure.** Essential controls are visible first. Advanced sections, per-guide overrides, solver details, and debug values are collapsed by default.
5. **Make procedural ownership obvious.** The UI distinguishes authored guides, generated children, and baked cards. Generated results are never presented as directly editable source data.
6. **Preview before committing.** Large generation, conversion, rebinding, LOD, and bake operations show a cancellable preview or summary.
7. **Prefer inline recovery over modal interruption.** Use actionable banners, status messages, and validation entries. Reserve confirmation dialogs for destructive, non-undoable, or external-asset overwrite operations.
8. **Preserve context.** Switching steps, preview modes, or groups must not discard selection, camera, active layer, or unfinished numeric edits.
9. **Stay responsive.** Interaction uses guide/coarse-card previews and clearly labels stale or reduced-quality results.
10. **Use one term for one concept.** Tooltips and documentation use the vocabulary defined below consistently.

### User-facing vocabulary

| Term | Meaning in the UI |
|---|---|
| Groom | The editable `HairGroomAsset` and all of its authoring data |
| Group | A logical hair layer such as Coverage, Mid, Detail, Flyaway, or Brows |
| Growth Area | The surface region where a group may place guide roots or generated children |
| Growth Map | A named painted field such as Density, Length, Flow, Lift, Width, or Clump |
| Guide | An authored editable spline that defines hair shape |
| Child | A procedural centerline generated from nearby guides; not directly edited by default |
| Card | Renderable ribbon or polygonal tube geometry generated from a guide or child |
| Sculpt Layer | Non-destructive guide-shape edits |
| Modifier | An ordered procedural operation evaluated on a group |
| Helper | A curve, cage, surface, volume, or other constraint target |
| Profile | The card cross-section, width/taper behavior, and meshing rules |
| Atlas Region | A usable strip/region of a hair texture atlas |
| Bake | Transactional creation or update of Unity/UMA output assets |

Avoid using **strand** alone in actionable labels because it may mean a guide, child centerline, or rendered card. Labels should say exactly which object is affected.

### Task-oriented workflow navigator

Replace the original nine peer modes with seven workflow steps:

1. **Setup** — source character/scalp, groom groups, presets, symmetry, and collision source.
2. **Growth** — growth areas and painted attribute maps.
3. **Guides** — create, generate, select, and edit authored guide splines.
4. **Groom** — sculpt layers, grooming brushes, modifiers, helpers, and constraints.
5. **Cards** — children, card profiles, orientation, atlas assignment, UVs, and mesh preview.
6. **Optimize** — skinning, LODs, budgets, coverage, intersections, and overdraw.
7. **Validate & Bake** — release checks and UMA output.

The navigator is a horizontal breadcrumb/step bar in the main workspace header and a compact dropdown when width is limited. It is not a blocking wizard after setup: experienced artists may jump between steps. Each step shows one of four states with both icon and text/tooltip:

- **Ready** — prerequisites exist.
- **Incomplete** — work has not been done yet.
- **Attention** — usable but warnings exist.
- **Blocked** — a required dependency is missing or invalid.

Hovering a step explains its current state. Clicking a blocked step opens the step and focuses its first resolution action rather than displaying a generic error.

### Workspace model

Use the Unity 6.3 compact-workspace pattern already established by Overlay Painter:

- A dedicated isolated Scene view for the groom.
- Native Scene-view toolbar overlays for step, tool, preview, selection, symmetry, and quality controls.
- A dockable **Hair Groom Workspace** for Explorer and contextual Properties.
- An optional dockable **Hair Shelf** for presets, profiles, atlases, helpers, and recent assets.
- An optional dockable **Hair Diagnostics** window for validation, budgets, and bake output.

Do not implement a private replacement Scene view inside one monolithic custom window. Ordinary Unity navigation, gizmos, overlays, focus behavior, docking, and shortcut management should continue to work.

Recommended default compact layout:

```text
+--------------------------------------------------------------------------------+
| Groom / Document | Setup > Growth > Guides > Groom > Cards > Optimize > Bake  |
+----------------------+------------------------------------------+---------------+
| GROOM EXPLORER       | SCENE VIEW                               | CONTEXT       |
| Groups               | Native hair toolbar overlays             | Active Tool   |
| Sculpt Layers        |                                          | Selection     |
| Modifiers            | Avatar, guides, helpers, and cards       | Properties    |
| Helpers              |                                          | Advanced      |
| LODs / Outputs       | Cursor, warnings, and preview badges     |               |
+----------------------+------------------------------------------+---------------+
| OPTIONAL SHELF: Presets | Profiles | Atlas Regions | Helpers | Recent/Favorites |
+--------------------------------------------------------------------------------+
| Status | Selection summary | Evaluation state | Triangles/overdraw | Save state   |
+--------------------------------------------------------------------------------+
```

At narrow widths, Explorer and Context become tabs in one dock rather than shrinking the Scene view below a useful size. The Shelf collapses to a button. Layout changes are stored per user, not in the shared groom asset.

### Global header and persistent controls

The header remains stable across every step and contains:

- Groom asset name, dirty indicator, and source avatar/race summary.
- Workflow navigator.
- Undo/redo using Unity's commands.
- Save and **Save As**.
- Autosave/recovery state with a clickable detail tooltip.
- Preview quality: **Interactive**, **Balanced**, or **Final**.
- Evaluation pause/resume.
- Validate badge showing error/warning counts.
- **Bake** as the final primary action; disabled only by true blocking errors.
- Help menu: Quick Start, current-step help, shortcuts, terminology, samples, and report diagnostics.
- Exit Stage.

Save, Validate, Bake, and Exit remain text-labelled at ordinary widths. Do not reduce irreversible or high-impact actions to ambiguous icons.

### New Groom and recovery entry flow

Recovery is resolved before a new-groom wizard appears. Opening from an existing `HairGroomAsset` skips creation and restores its last safe document state.

Opening from a DCA, race, head, or scalp launches a short **New Hair Groom** wizard:

1. **Source** — show the chosen avatar, race, head slot, scalp candidate, scale, and skeleton. Auto-select only when the choice is unambiguous.
2. **Create** — groom name and project-owned destination, with collision-safe asset naming.
3. **Starting Point** — Blank, Short Hair, Medium/Long, Ponytail/Bun, Coils/Curls, Facial Hair, Brows/Lashes, or a saved groom preset.
4. **Defaults** — symmetry, default groups, collision surface, atlas/profile preset, and preview quality.
5. **Review** — assets/references to create and any warnings.

The wizard supports **Create Groom** and **Create and Start Quick Guide**. Presets initialize editable parameters and never hide generated dependencies. Cancel leaves the source avatar unchanged.

### Groom Explorer

The Explorer is the persistent structural view. Its default hierarchy is:

```text
Groom
  Groups
    Coverage
      Growth Maps
      Guides
      Sculpt Layers
      Modifiers
      Helpers & Constraints
      Card Setup
    Mid
    Detail
    Flyaways
  Shared Helpers
  LODs
  Outputs
```

The hierarchy is filtered by the current workflow step so irrelevant detail is initially hidden, with **Show All** available. It supports search, type filters, drag reorder where evaluation permits, multi-selection, rename, duplicate, move to group, and context menus.

Each group row shows:

- Color chip plus name.
- Visibility, solo, and lock.
- Active/edit target indicator.
- Guide and generated-card count badges.
- Dirty/evaluating indicator.
- Warning/error badge.
- LOD importance.

Group color is a navigation aid, not the only identity signal. Rows retain names and icons in group-color preview.

Selection follows a clear rule: one **active** item owns the Context panel; any additional selected items receive compatible multi-edit operations. Locked groups may be inspected but not changed. Hidden groups are not modified by viewport tools unless an explicit **Affect Hidden** advanced option is enabled.

### Context panel and property hierarchy

The right Context panel changes with the active tool and selection but keeps a predictable order:

1. **Action summary** — one sentence explaining what the tool affects.
2. **Quick controls** — the 3–6 values most often changed during a stroke or operation.
3. **Target** — active group, sculpt layer/map, guide/child/both domain, visible-only, and root/tip range.
4. **Selection properties** — mixed values shown explicitly for multi-selection.
5. **Advanced** — falloff curves, solver settings, deterministic seed, preservation options, and debug information.
6. **Help and warnings** — contextual examples and direct fixes.

Property rows support reset-to-inherited, reset-to-default, copy/paste, numeric scrubbing, precise entry, and tooltips with units and valid ranges. Group defaults and per-guide overrides use visible inheritance markers:

- Hollow marker: inherited.
- Filled marker: explicit override.
- Mixed marker: multi-selection differs.

Changing an inherited value edits the group default unless the user explicitly chooses **Create Overrides**. This prevents accidental hundreds-of-guide overrides.

### Scene-view hair toolbar

Use multiple compact native overlays rather than one oversized strip:

- **Workflow** — current step and previous/next.
- **Tools** — context-sensitive tool icons, names in expanded form, and a searchable tool picker.
- **Selection** — object type filter and selection method.
- **Brush** — radius, strength, hardness, target range, and active layer/map.
- **Symmetry** — off/X/custom helper, with a visible plane toggle.
- **Preview** — Guides, Children, Cards, shading/debug view, and LOD.
- **Quality & Evaluation** — quality tier, pause, dirty state, and rebuild.

The tool overlay shows only tools valid for the current step. A searchable **All Hair Tools** palette exposes advanced tools without permanently displaying thirty icons.

### Setup step

Setup should answer: “What am I grooming, and how is it organized?”

Primary cards/actions:

- Source avatar, race, head/scalp slot, and material summary.
- **Change Scalp Source** with rebind preview and rollback.
- World scale and strand/card unit display.
- Collision source with **Use Scalp**, **Add Head/Body**, and **Add Collider**.
- Symmetry mode and visible plane.
- Group preset picker and **Add Group**.
- Atlas/profile defaults, allowed to remain unassigned until Cards.
- Binding health summary: Exact, Reprojectable, Attention, or Broken.

An empty groom displays a large **Create First Group** action. Recommended group presets explain their intended role and budget without enforcing a naming scheme.

### Growth step

Growth combines region definition and surface attribute painting.

The left step panel shows group-local maps with thumbnails, visibility, lock, and range legend. The essential maps are **Growth Area**, **Density**, **Flow**, and **Length**; Lift, Width, Clump, Child Count, Profile Blend, and LOD Importance appear under **Add Map**.

Region tools:

- Select vertices/polygons.
- Paint Add, Subtract, Smooth, and Replace.
- Lasso, polygon fill, connected fill, grow, shrink, invert, and clear.
- Convert selection to Growth Area and Growth Area to selection.
- Copy/mirror a growth area between groups or compatible scalp surfaces.

Map painting shows a surface legend and the current numeric value at the cursor. Flow displays tangent arrows at adaptive density; arrows thin while painting so they do not obscure the surface. The Context panel provides **Initialize Flow From...** options: scalp UV direction, selected guide, part line, helper curve, gravity/up vector, or mirrored side.

The first-time empty state offers:

1. **Paint Growth Area**.
2. **Use Selected Scalp Polygons**.
3. **Fill Whole Scalp** with preview and confirmation.

Map operations use a non-modal preview drawer for fill, blur, sharpen, grow/shrink, remap, noise, and import. **Apply** creates one undo step; **Cancel** restores the exact prior map.

### Guides step

Guides should answer: “Where are the authored controls, and do they describe the style?”

The primary tool set is deliberately small:

- **Select/Edit** — select guides or control points and use constrained gizmos.
- **Draw Guide** — draw in 3D, on surface, or root-to-tip.
- **Place Guide** — click a root and drag direction/length.
- **Generate Guides** — deterministic distribution from Growth Area and maps.
- **Duplicate/Mirror**.
- **Delete**.

**Generate Guides** opens a preview drawer with:

- Target group and Growth Area.
- Count or spacing mode.
- Direction source: Flow map, neighboring guides, helper, or surface normal plus bias.
- Length/width map use.
- Distribution seed.
- Minimum root distance and boundary padding.
- Symmetry policy.
- Collision-aware initial lift.
- Live count, spacing, and uncovered-area indicators.

Preview guides use a distinct dashed style. **Accept as Guides** creates authored splines in one undoable command; **Cancel** removes the preview. Regeneration never silently replaces hand-edited guides. The user must choose **Add**, **Replace Generated Only**, or **Replace All**.

Guide display uses root markers, direction ticks, and optional control points. Roots change icon when their surface anchor is exact, reprojected, or broken. Selected guides are outlined and widened rather than identified only by color.

Selection granularity is explicit:

- Group.
- Guide.
- Control Point.
- Root.
- Tip.

Clicking a generated child selects/highlights its parent guides and offers **Promote Child to Guide**; it does not create an invisible direct override. Double-click frames a guide. The Details area reports guide count, average spacing, uncovered Growth Area, invalid roots, and length distribution.

### Groom step

Groom should feel like sculpting, with a focused essential shelf and advanced tools searchable.

Default essential brushes:

- Comb.
- Grab.
- Smooth.
- Length/Cut.
- Width/Taper.
- Lift/Flatten.
- Clump.
- Part.
- Freeze.

Advanced tools such as Curl, Coil, Wave, Noise, Twist, Repel, Project to Helper, Collision Push-Out, Resample, Simplify, and Symmetrize live in the searchable tool palette and may be pinned to Favorites.

The brush Context panel always shows:

- Radius, strength, hardness, falloff, spacing, and stabilization.
- Target: selected guides, active group, visible groups, or all unlocked groups.
- Domain: guides, coarse children preview, or both where valid.
- Along-guide range with a root-to-tip ramp.
- Preserve length, preserve volume, collision, and symmetry.
- Pressure routing.
- Active sculpt layer and optional mask.

Root locking is on by default and visible in the toolbar. Temporarily unlocking roots requires a clearly labelled toggle and warning because it can change surface binding. An affected-root preview appears before the stroke begins.

During a stroke, the viewport displays:

- Radius and falloff rings.
- Stroke direction arrow when relevant.
- Active tool, target group, sculpt layer, and symmetry state near the cursor.
- Estimated affected guide count.
- Root-to-tip affected segment.
- Collision or constraint hits.

Holding a temporary modifier can invert compatible tools, such as Lengthen to Shorten or Lift to Flatten. Smooth may be temporarily invoked during another brush, but shortcut behavior must be discoverable in the tooltip and remappable.

#### Sculpt layers

The Sculpt Layers panel behaves like a compact non-destructive layer stack:

- Add, rename, duplicate, delete, reorder, merge, and bake.
- Visibility, lock, opacity, blend mode, and layer mask.
- Active-layer highlight and changed-guide count.
- Before/after and solo.
- Clear selected-guide deltas or entire layer.

Painting always states the destination layer. Attempting to groom a locked layer presents **Unlock**, **Choose Another Layer**, and **Create New Layer** inline actions.

#### Modifier stack

Modifiers appear below Sculpt Layers for the active group. The stack displays evaluation dividers:

```text
AUTHORED GUIDES
  Sculpt Layers
  Guide Modifiers
GENERATED CHILDREN
  Child Modifiers
CONSTRAINTS & COLLISION
MESH OUTPUT
```

**Add Modifier** opens a searchable categorized menu. Drag reorder is allowed only within valid evaluation domains; an invalid drop snaps back and explains why. Each row shows enabled, name/type, mask, seed where applicable, warning state, and a small result summary. Selecting a modifier displays essential controls first, its root-to-tip ramp, then advanced settings.

The stack supports mute, solo-to-stage, duplicate, copy/paste settings, save preset, bake to sculpt layer, and compare before/after. Expensive modifiers show cached/live state and may be temporarily switched to **On Release** evaluation.

#### Helpers and constraints

Helpers are created from a context menu or Shelf thumbnails. Creation immediately activates an appropriate placement gizmo and shows a short instruction in the status bar.

The helper row shows embedded/external ownership, visibility, lock, dependent group count, and missing/broken state. Dragging a helper onto a group offers valid constraint types. The Constraints panel uses ordered rows with weight, root-to-tip ramp, mask, offset, live/cached/baked state, and **Select Helper**.

Viewport helper shapes use consistent icons and line styles. Hair, helper, and collision selection filters prevent accidental selection through dense cards. Missing external helpers display a persistent actionable banner with **Locate**, **Replace**, **Bake Last Result**, or **Remove Constraint**.

### Cards step

Cards should answer: “What renderable geometry will this groom generate?”

The step is organized into four contextual sections:

1. **Children** — count, include guide, root spread, interpolation, variation, clump, and seed.
2. **Profile & Meshing** — ribbon or tapered polygonal tube, side count, width, taper, roll/orientation, sampling, and tessellation.
3. **Atlas & UV** — atlas profile, compatible regions, assignment weights, randomization, root/tip orientation, and padding.
4. **Mesh Preview** — guide/child/card comparison and topology diagnostics.

Child count is presented as **Children per Guide** with an adjacent plain-language result: “50 guides × (1 guide card + 3 children) = approximately 200 cards.” Painted multipliers and rejected roots update the actual result below the estimate.

The Profile picker uses thumbnail previews of the cross-section and a live enlarged cross-section gizmo on the selected card. Ribbon and polygonal-tube settings remain separate so irrelevant controls disappear. Side count changes display the vertex/triangle cost immediately.

Orientation controls include automatic rotation-minimizing frames, surface-aligned roots, camera-facing bias where supported, guide roll, and manual roll painting. **Show Frames** overlays normal/binormal ticks and flags sudden flips.

The Atlas panel uses a visual strip shelf rather than a raw index popup. Each region thumbnail shows name, texture direction, usable UV bounds, tags, and selection weight. Filters include atlas, tag, width class, opacity/density class, favorites, and compatible only. Unassigned cards render with a conspicuous checker/debug material and a direct **Assign Atlas Region** action.

An optional dockable 2D Atlas view shows selected card UVs over the source textures, direction arrows, padding, and out-of-bounds warnings. Selecting a card in 3D highlights its UVs and assigned region; selecting a region highlights affected cards.

Generated children and card meshes rebuild at coarse quality while a value is dragged. The status bar reads **Interactive Preview** until the final-quality result replaces it. Stale results retain the last valid mesh with a visible **Rebuilding** badge rather than disappearing or flashing.

### Optimize step

Optimize combines skinning, LODs, performance budgets, and problem inspection.

#### Skinning

The default view explains the automatic result before exposing manual details:

- Root weight source and transfer status.
- Generated-bone mode off/on.
- Bone count and platform budget.
- Weight normalization and missing-bone errors.
- **Preview Motion** presets for head turn, nod, wind sway, and optional generated-bone movement.

Skin weights are visualized with a legend and selected-bone isolation. Manual corrections occur through a focused weight tool and never rewrite the source scalp asset. **Reset From Scalp** and **Regenerate Hair Bones** show affected outputs before applying.

#### LOD workspace

LODs appear as a horizontal comparison strip or list:

- LOD name/index and screen threshold.
- Guide/card/triangle/bone counts.
- Profile simplification and atlas policy.
- Validation badge.
- Lock and regenerate.

A shared camera and turntable keep comparisons visually aligned. The artist can solo one LOD, show two side-by-side, or scrub through thresholds. Editing LOD0 exposes **Propagate Compatible Settings** rather than silently overwriting hand-tuned lower LODs.

#### Budget dashboard

The dashboard reports current and target values by group and LOD:

- Guides, generated cards, vertices, and triangles.
- Bones.
- Texture memory and atlas regions.
- Estimated transparent overdraw.
- Coverage and exposed-scalp score.
- Intersections and invalid geometry.

Budgets use labelled bars with target markers and numbers. Green/yellow/red is supplemented by icons and text. Clicking a metric switches to its diagnostic view and selects the responsible groups/cards.

### Validate & Bake step

Validation is a working list, not a final modal dialog.

The Diagnostics panel groups findings into **Errors**, **Warnings**, **Optimization**, and **Info**. Each finding contains:

- Short problem statement.
- Affected group/guide/card/LOD count.
- **Select**, **Frame**, and **Isolate**.
- Explanation and likely visual/runtime impact.
- One or more fixes when safe.
- Suppression only for explicitly suppressible warnings, with a stored reason.

Safe fixes preview their scope and create one undo step. Destructive or heuristic fixes require explicit confirmation. **Validate Changed**, **Validate Active Group**, and **Validate All** keep iteration fast. Validation results carry the groom revision they describe and become visibly stale after relevant edits.

The Bake panel uses a review-first transaction:

1. Output names and project-owned destination.
2. Selected groups/LODs and target recipe/races.
3. Mesh, `SlotDataAsset`, overlay/material, wardrobe recipe, and optional helper/bone outputs.
4. Existing assets that will be updated or newly created.
5. Blocking errors and acknowledged warnings.
6. Estimated time and output size.

**Dry Run** performs generation and validation without committing assets. **Bake** writes transactionally with progress and cancellation. Failure retains the previous valid outputs. Success shows created/updated assets with **Ping**, **Open**, **Equip on Preview Avatar**, and **Rebuild** actions.

### Selection and edit model

Selection filters prevent ambiguity in dense hairstyles:

- Scalp vertices/polygons.
- Guides.
- Guide control points.
- Guide roots/tips.
- Helpers.
- Generated children/cards for inspection only.
- Bones/weights.

Methods include click, additive/subtractive click, box, lasso, brush, select linked, select group, grow/shrink, invert, and select by attribute/validation issue. **Visible Only** is the default. X-ray selection is explicit and visibly indicated.

Selection feedback must distinguish hover, selected, active, frozen, locked, invalid, and generated states using line weight/icon/pattern as well as color. A selection summary in the status bar reads, for example, “12 guides selected in Mid · 3 locked skipped.”

Multi-edit applies only compatible properties and reports skipped objects. Direct manipulation observes standard Unity transform conventions where applicable. Soft selection shows its falloff along guide arc length rather than as an unexplained world-space radius.

### Preview and diagnostic views

The Preview overlay separates **Geometry** from **Analysis**.

Geometry views:

- Guides only.
- Guides + coarse children.
- Children centerlines.
- Final cards.
- Cards + selected guides.
- Source scalp/avatar.
- LOD comparison.

Analysis views:

- Group colors.
- Growth Area and each Growth Map.
- Flow direction.
- Guide spacing and coverage.
- Clump assignment.
- Freeze and sculpt-layer masks.
- Card frames, normals, winding, and backfaces.
- Atlas-region assignment and UV stretch.
- Skin weights and generated bones.
- Collision/intersection.
- Exposed scalp/coverage.
- Transparent overdraw.
- Validation severity.

Every analysis view provides a legend and a one-click return to the prior material view. Debug views must not change saved authoring data.

### Input and shortcuts

Follow standard Unity navigation and do not intercept Alt-based Scene-view camera controls. All hair shortcuts must be registered with Unity's Shortcut Manager, scoped to the active `HairCardStage`, searchable, and remappable.

Recommended defaults, subject to conflict testing:

| Input | Action |
|---|---|
| `Q`, `W`, `E`, `R` | Select and standard transform tools where valid |
| `F` | Frame active selection |
| `B` | Activate last-used hair brush |
| `Shift+B` | Open searchable hair-tool palette |
| `[` / `]` | Decrease/increase brush radius |
| `Shift+[` / `Shift+]` | Decrease/increase hardness |
| `Ctrl`/`Cmd` + `Z` | Undo |
| `Ctrl`/`Cmd` + `Shift` + `Z` | Redo using platform convention |
| `Delete`/`Backspace` | Delete selected authored objects with undo |
| `F2` | Rename active Explorer item |
| `H` | Hide selected; `Shift+H` isolate; `Alt+H` show all where conflict-free |
| `M` | Toggle configured symmetry |
| `1`–`7` | Switch workflow step only when the Scene view owns keyboard focus and no text field is active |
| `Esc` | Cancel current preview/stroke/placement, then exit sub-mode; never close the stage on first press |

Brush modifiers should use a small consistent vocabulary: invert compatible action, temporary smooth, and precision/slow mode. Exact defaults require tablet and shortcut-conflict testing. Tooltips must show current bindings rather than hard-coded text.

Tablet support includes pressure routing to any one or combination of strength, radius, width, or length; a pressure curve; tilt where useful; stabilization; and a mouse-equivalent fallback. No core action may require a middle or right mouse button without an alternate UI control.

### Feedback, errors, and long operations

Use three feedback levels:

1. **Cursor/viewport feedback** for immediate targets and effects.
2. **Status/toast feedback** for successful commands and recoverable no-op explanations.
3. **Persistent banners/Diagnostics** for conditions requiring action.

Messages should say what happened and how to proceed: “No guides changed; Mid is locked. Unlock Mid or target another group.” Avoid generic “Operation failed.”

Background evaluation displays the affected stage, progress when measurable, and Cancel. New edits cancel superseded work through generation tokens. The UI never presents stale statistics as current; stale badges include the revision or say **Waiting for Rebuild**.

Modal dialogs are limited to:

- Closing with unsaved/unrecoverable work.
- Discarding recovery data.
- Rebinding or replacing source topology when rollback is not automatic.
- Deleting shared/external dependencies.
- Overwriting assets outside the normal transactional update contract.

### Onboarding and embedded help

The system should be learnable without requiring a separate manual before the first result.

- First launch offers a non-modal **Quick Groom** checklist that follows the seven workflow steps.
- Empty states use short explanations and one primary action.
- The first use of Growth painting, guide generation, grooming, helpers, LOD, and Bake shows a dismissible coach mark.
- **What is this?** help in every Context section opens a concise explanation, visual example, and related shortcut.
- Tooltips state action, target, shortcut, and important modifier behavior.
- A searchable command/tool palette also searches help topics.
- Sample grooms include a short style, layered long style, coils/curls, ponytail/helper example, facial hair, and brows/lashes.
- **Reset Workspace** restores panel layout and overlays without changing groom data.

The Quick Groom checklist records completion per user, not in the shared asset. It can be reopened at any time and must never block advanced navigation.

### Accessibility and localization

- Use text plus icons for workflow, validation, lock, visibility, and ownership states.
- Never communicate group, heatmap, or severity solely by hue; provide patterns, contours, labels, or symbols.
- Support Unity editor scaling and test at 100%, 150%, and 200% UI scale.
- Maintain useful layouts at 1280×720 and large 4K displays.
- Use minimum practical hit targets of 20–24 logical pixels for dense editor controls and larger targets in tablet-oriented overlays.
- Preserve visible keyboard focus and sensible tab order in docked panels.
- Respect high-contrast editor themes and avoid low-alpha text.
- Provide reduced viewport animation/flash options for rebuild transitions and selection pulses.
- Keep user-facing strings localizable; do not construct essential sentences from fragmented labels.
- Do not rely on hover alone; every tooltip/help action must have a keyboard-accessible route.

### Workspace, selection, and document persistence

Separate shared groom data from personal UI state:

Stored in `HairGroomAsset`:

- Groups, maps, guides, layers, modifiers, helpers, profiles, LODs, bake settings, and suppressions.

Stored per-user/per-project workspace state:

- Dock layout, panel widths, collapsed sections, shelf visibility, recent/favorite tools, analysis view, and UI scale preferences.

Stored in recoverable session state when useful:

- Active workflow step, active group/layer/map/tool, selection stable IDs, Scene camera, preview quality, and unfinished safe numeric edits.

Transient hover, hot-control state, generated preview buffers, and active strokes are never serialized as authoritative data. Restoring a session with missing selections falls back to the nearest valid group and explains what could not be restored.

### UI command architecture

Every meaningful action should route through a command layer with:

- Stable command ID and localized label.
- Enable/disable predicate and reason.
- Undo transaction name and scope.
- Dirty-dependency declaration.
- Optional preview/apply/cancel lifecycle.
- Menu, context-menu, toolbar, shortcut, and command-palette entry points as appropriate.
- Structured success, no-op, warning, and failure result.

This prevents the viewport, Explorer, shortcuts, and inspector from implementing subtly different versions of the same operation. UI code should bind to controller/view-model state and must not own meshing, grooming, persistence, or export algorithms.

### UX acceptance criteria and usability testing

Before artist-usable alpha:

- A first-time UMA user can create a groom, paint a Growth Area, generate guides, perform Comb/Grab/Smooth, and preview ribbons in 15 minutes using only embedded guidance.
- An experienced artist can repeat the same flow in under 5 minutes without forced wizard steps.
- A user can identify the active group, target layer/map, preview quality, save state, and blocking validation count from the default workspace at a glance.
- Generated children cannot be accidentally edited as if they were source guides.
- Every blocking empty state has a direct resolution action.
- Ninety percent of ordinary grooming actions require no modal dialog.
- Undo/redo, save/reopen, domain reload, and recovery retain the active artistic context.
- A simple groom can be completed with mouse only, keyboard only for panel/command navigation, and pen plus visible UI controls.

Usability studies should include first-time Unity/UMA users, experienced character artists, technical artists, mouse users, and pen-display users. Test short, long, curly/coiled, tied, facial-hair, and brows/lashes workflows. Record task completion, time, undo frequency, mis-targeted strokes, mode errors, undiscovered controls, and points where users leave the viewport unnecessarily.

Expensive child/card regeneration should use a coarse interactive preview while dragging, followed by a full-quality evaluation on release. The quality transition, dirty state, and final completion must always be visible.

## 16. Performance design

The authoritative groom evaluation should be CPU/Burst deterministic, with GPU acceleration used for display and optional brush queries. This keeps headless baking and automated tests possible.

Core strategies:

- `NativeArray`-based guide and card buffers.
- Burst jobs for sampling, interpolation, modifiers, collision, and mesh generation.
- A topology-aware surface BVH or spatial grid built from `Mesh.AcquireReadOnlyMeshData`.
- Spatial hashes for curve brush queries.
- Dirty dependency graph: only affected groups and pipeline stages rebuild.
- Progressive preview quality.
- Background jobs with cancellation and generation tokens.
- Zero recurring managed allocations during brush strokes after warm-up.
- Delta-based undo rather than complete groom snapshots.
- Cached rotation-minimizing frames and arc-length tables.
- Child data generated on demand.

Initial benchmark target:

- 100k-triangle source avatar.
- 2,000 guides.
- Four children per guide.
- 8,000-10,000 generated cards.
- 8-16 curve samples per card.

Provisional budgets on a defined reference workstation:

- Guide-only brush interaction: under 16 ms p95.
- Coarse child preview: under 33 ms p95.
- Incremental card rebuild for affected group: under 100 ms.
- Full production rebuild: under 5 seconds.
- No per-frame managed allocation after warm-up.
- Default undo budget: 512 MB, configurable.
- Stage open and document restore: under 3 seconds for the benchmark groom.

These targets should be finalized during the technical spike.

## 17. Undo, persistence, and recovery

Every user operation must support undo:

- Mask strokes.
- Guide creation/deletion.
- Comb/sculpt strokes.
- Helper edits.
- Modifier changes.
- Group operations.
- Profile and UV changes.
- LOD settings.
- Bake settings.

Use sparse deltas:

- Changed mask indices and previous values.
- Changed guide/CV IDs and previous attributes.
- Structural records for add/delete/reorder.
- Before/after settings for small objects.

Persistence requirements:

- Autosave after meaningful changes.
- Recovery document after an editor or domain-reload failure.
- Safe close prompts.
- Schema migrations.
- CRC/checksum for binary chunks.
- Missing-resource and topology-change diagnostics.
- Exit or safely suspend on compilation and play-mode transitions, following the existing stages.

## 18. Validation and release gates

### Geometry validation

- No NaNs or infinities.
- No zero-length segments.
- No degenerate triangles.
- Correct index bounds.
- Consistent winding.
- Stable frame orientation.
- Valid normals and tangents.
- Valid UV ranges and atlas padding.
- Root and tip width limits.
- Correct 16/32-bit index selection.

### Groom validation

- All roots anchored.
- No accidental interpolation across group/part barriers.
- Maximum scalp penetration.
- Maximum helper/collision penetration.
- Detached-card detection.
- Excessive curve curvature.
- Frame-flip detection.
- Scalp coverage holes.
- Excessive duplicate cards.
- Missing helper dependencies.

### UMA validation

- Weights sum to one.
- Valid bone indices and bind poses.
- Required UMA material and overlay data present.
- Generated slots rebuild on reference races.
- Recipe can equip and remove the hair.
- LOD assets use consistent materials and skeletons.
- No modification of source slot assets.

### Automated test scenes

At minimum:

- Short buzz cut.
- Parted short hair.
- Long straight hair.
- Curly hair.
- Dense coiled hair.
- Bob with coverage and silhouette groups.
- Ponytail driven by a helper curve.
- Bun driven by a volume/cage.
- Beard and mustache.
- Eyelashes/eyebrows.
- Tapered-cone anime hair.
- Mirrored and asymmetric styles.
- Topology-change/rebind case.
- Multiple UMA races and body shapes.

### UI and workflow validation

Automated editor tests should cover:

- New Groom wizard source selection, preset initialization, cancellation, and collision-safe naming.
- Recovery choice before creation/open and exact restoration of safe session context.
- Workflow-step readiness, attention, and blocker resolution actions.
- Command enablement reasons, undo names, dirty dependencies, and equivalent toolbar/menu/shortcut execution.
- Explorer search, active versus selected item, locking, visibility, solo, drag reorder, and stable-ID selection restoration.
- Group defaults versus per-guide overrides and multi-edit mixed values.
- Generated-child inspection and **Promote Child to Guide** without hidden direct overrides.
- Preview/apply/cancel operations restoring the exact previous groom on cancel.
- Interactive/coarse preview changing to current final-quality output without stale statistics.
- Validation issue **Select**, **Frame**, **Isolate**, and safe-fix actions.
- Transactional Bake review, dry run, cancel, rollback, and successful asset links.
- Workspace state remaining per user while groom data remains deterministic and shareable.
- Shortcut scoping so text fields and ordinary Scene-view navigation are not intercepted.
- Domain reload, assembly compilation, stage close, and undo/redo during long evaluation.

Manual UI matrices should include:

- 1280×720, 1920×1080, ultrawide, and 4K displays.
- 100%, 150%, and 200% editor UI scaling.
- Light/dark and high-contrast conditions.
- Mouse, trackpad, pen tablet, pen display, and keyboard panel navigation.
- Narrow dock layouts, floating panels, reset workspace, and multiple monitors.
- Color-vision deficiency simulation for group, heatmap, and severity views.
- First-run guided flow and experienced no-wizard flow.
- Large production groom responsiveness with rapid step/tool/group switching.

The artist-usable alpha gate includes moderated usability sessions and the task-time/error targets from section 15. A feature is not complete merely because its algorithm is callable; it must be reachable through the command layer, undoable, discoverable, and accompanied by useful empty/error states.

A release gate should run unit, integration, persistence, deterministic bake, geometry, visual-golden, performance, UI automation, accessibility, and usability tests.

## 19. Implementation roadmap

| Phase | Scope | Exit criterion |
|---|---|---|
| 0. Technical spike | Surface anchors, spline frames, Burst card mesher, UMA export experiment | One guide produces a correctly anchored and skinned ribbon/cone |
| 1. Stage and UX foundation | Stage lifecycle, command layer, compact workspace, workflow navigator, Explorer/Context shell, New Groom wizard, status, autosave/recovery, workspace persistence, source binding | A new user can create/open/recover a groom, understand the next action, save/reload, and close safely |
| 2. Growth and Guides | Growth step, selection/filtering, map painting/legends, guide tools, generation preview, essential empty states | Artist can define a scalp region, generate editable guides, and understand authored versus generated data |
| 3. Children and Groom | Deterministic children, essential brush shelf, Context controls, cursors, interactive quality, Comb/Grab/Smooth/Length/Cut/Width/Clump/Part/Freeze | A basic hairstyle can be sculpted responsively with clear targets and undo |
| 4. Cards and mesh | Children panel, profile thumbnails, flat ribbon, tapered cone, stable frames, adaptive tessellation, atlas shelf/2D view, UV mapping, mesh diagnostics | Production-valid card meshes can be generated and their cost/assignment understood |
| 5. Layers and modifiers | Sculpt Layers UI, evaluation-domain stack, searchable modifiers, curl, noise, twist, lift, resample, presets | Styling is non-destructive, discoverable, and repeatable |
| 6. Helpers and collision | Helper shelf/gizmos, dependency UI, ordered constraints, rails, cages, part lines, attractors, collision, gravity/drape | Ponytail, bun, and constrained styles can be authored and broken dependencies repaired |
| 7. Optimize, Validate, and Bake | Skinning workspace, bones, LOD comparison, budget dashboard, actionable diagnostics, transactional Bake, slot/recipe output | Hair equips correctly and ships as optimized UMA assets through the complete UI workflow |
| 8. Texture pipeline | Atlas profiles, automatic regions, map baker, Overlay Painter handoff | Complete game-ready texture sets can be produced |
| 9. Production hardening | Performance, visual QA, migrations, onboarding/help, accessibility, usability studies, docs, samples, extensibility API | Release gate and section 15 UX acceptance criteria pass on all target styles |

A useful alpha should emerge after Phase 4 and must satisfy the first-run and experienced-user task tests in section 15. A production beta requires the end-to-end Phase 7 workflow. The complete AAA target requires Phase 9.

For a realistic team of two tools engineers, one technical artist, and part-time QA, estimate roughly:

- Vertical slice: 6-8 weeks.
- Artist-usable alpha: 4-5 months.
- Production beta: 7-9 months.
- Hardened AAA toolset including texture baking and extensive QA: 10-12 months.

## 20. Recommended first vertical slice

The first implementation should prove the entire pipeline with minimal breadth:

1. Open `HairCardStage` from a generated DCA and complete the New Groom wizard.
2. Use the Setup step to confirm the head/scalp slot, collision surface, one group, and symmetry.
3. Use the Growth step to paint one Growth Area and one Flow map with visible legends.
4. Use the Guides step to preview and accept 50 guide splines.
5. Confirm authored guides and generated children are visually and behaviorally distinct.
6. Set three children per guide and show the estimated/actual card count.
7. Use the Groom step with Comb, Grab, Smooth, Cut, and Width on one visible active sculpt layer.
8. Add one curve helper, assign a constraint, and use scalp collision.
9. Use the Cards step to switch between flat ribbon and configurable-sided tapered cone.
10. Assign regions from an existing UMA hair atlas using thumbnail selection and 2D/3D highlighting.
11. Preview the final card mesh with Interactive/Final quality state and one diagnostic view.
12. Use Optimize to transfer scalp bone weights and view the triangle/overdraw budget.
13. Run validation, select/frame one issue, and apply one safe fix.
14. Review a Bake dry run, then transactionally bake a Mesh, `SlotDataAsset`, overlay/material, and wardrobe recipe.
15. Save, close, reopen, restore active context, edit, and deterministically rebake.
16. Exercise undo/redo, cancel a generation preview, reset the workspace, and recover an interrupted session.
17. Complete the first-run flow within the section 15 usability target and rerun it without guided steps as an experienced flow.

That slice tests the risky parts--surface binding, grooming interaction, child interpolation, stable card orientation, skinning, persistence, and UMA export--before expanding the brush library or texture baker.

## Definition of done

The system is complete when an artist can create, revise, validate, and bake a game-ready hairstyle without leaving Unity; the groom remains editable; regeneration is deterministic; all roots survive supported UMA character changes or produce actionable repair warnings; all baked assets equip correctly; LOD and overdraw budgets are visible; stable serialized identity is used throughout; and the workflow passes its automated, accessibility, and moderated-usability release gates on Unity 6.3+.
