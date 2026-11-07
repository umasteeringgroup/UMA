# Hair Cards Manual QA

## Lift and modifier coordinates

- Rebuild the reported groom with Lift on the same pass. Positive Lift should lift left/right/front/back hair away from its scalp attachment, not push all guides rearward. Compare Root Normal and Closest Surface Normal. Check negative amount, zero amount, Root Influence 0/1, freeze masks, layer opacity, Undo and save/reopen. Roots and segment lengths must remain unchanged.
- Repeat with translated/rotated parents, uniform and nonuniform scale, a mirrored parent, and the character's authoring pose. Root/surface normals must remain outward and orbiting the camera must not change any modifier result. Check guide/child domains and full/baked/runtime outputs.
- Bind helpers beneath differently scaled/rotated parents. Confirm displayed collider boundaries match Collision/Push Out, including mirrored planes and ellipsoidal spheres. Move linked helpers through their original scene objects; their stage handles must be display-only. Reopen/bake to check the stored relative transform. Embedded-helper manipulation must still work.
- Read the full frame-by-frame modifier inventory in [Modifier coordinate audit](HairModifierCoordinateReview.md). Standalone generation must pass the destination transform for world gravity and scale-aware Lift; identity is the documented fallback when no context is supplied.

## Whole-guide Erase brush

- Select a sculpt pass, then **Erase** below the brush grid. Confirm a red radius circle and red affected-guide highlights, including with the freeze-mask display enabled. The properties and Scene toolbar should show Radius/Scope, not irrelevant hardness, strength, reverse or root-influence controls. Top-left help must describe whole-guide deletion and Undo.
- Erase by touching tips, roots, and the middle of sparse segments. Repeat with root handles and selected control points visible: selection caps must not steal the erase gesture. Start both over hair and in empty space. Rapid drags must not leave gaps between mouse events. Whole guides disappear immediately; releasing anywhere or switching tools ends the stroke. Undo restores the complete stroke, including all per-guide layer deltas; Redo deletes it again.
- Repeat with Through Depth, Visible Hair, Depth Volume, Selected Guides Only and isolated selection. Back-side guides must survive Visible Hair, but be reachable with Through Depth. A partial or full freeze on any point protects that entire guide. Locked/inactive groups or sculpt passes, modifier nodes and collection nodes must not delete guides.
- Erase from a final Spline Flow preview and from **Edit This Layer**. Neither may change modifier settings or the current edit boundary. Explain to artists that this removes authored guides across the group's sculpt passes, not just one pass's edits; hiding a pass cannot restore them.
- With child splines/cards already generated, erase some guides and then the last guide. There must be no deleted-guide ghosts during the stroke or while the release rebuild is pending. After rebuilding, confirm affected children/cards are gone and other groups are unchanged. Undo to recover the groom.
- Reopen the groom and create a new groom: Erase must not be rearmed. Confirm radius and other non-destructive brush preferences still persist. Review the shelf and help text in narrow/wide docked Properties windows.

Run this matrix on Unity 6.3 or newer before a release. The authoring UI is **Hair Nodes + Hair Properties + Hair Preview & Settings**, not the former tabbed workspace. “Layer” in engine tests means the serialized sculpt pass; “stack order” means forward evaluation order in the tree.

## Required automated release gate

Run `Assets/UMA/HairCards/QA/Run-HairCardsReleaseGate.ps1 -PreflightOnly` for a read-only metadata check while Unity is open. Before release, close Unity and run `Assets/UMA/HairCards/QA/Run-HairCardsReleaseGate.ps1` against this project (or a complete clean checkout). This imports and compiles the actual sources with Unity, then runs all Hair Cards EditMode tests, including the source-import test. Do not substitute prebuilt Hair Cards DLLs or a hand-maintained C# project file for this gate.

The gate rejects missing/malformed script metadata, GUID collisions, compile/import errors, absent or stale test results, zero tests and skipped tests. The source-import test verifies that every Hair Cards script was imported and included in its expected Unity assembly. Logs and fresh test XML are saved under `Logs/HairCardsReleaseGate`. A metadata-only check or isolated behavior test run must be reported as such, not as a passing source integration/release check.

## Node workspace and no-loss audit

- Open a groom: all three dockable windows appear. Dock Nodes at 320 px, Properties at 480 px, and Preview & Settings at 360 px, then test wide layouts. Close/reopen each independently; the stage and selected node must remain usable. Properties must contain only node properties/tools, never the old Preview/Settings tabs. Keep Settings selected while clicking several nodes and confirm its tab and scroll position do not change. Close Properties and verify Preview & Settings remains usable.
- Visit every node: Source, Group, primary map, Optional Maps and each mask, Guides, Grooming, each Sculpt Pass and every modifier type, Constraints and individual constraints, Children, Hair Cards, Geometry & Vertex Colors, Materials & UVs, Shared Helpers and each helper, Optimize & LODs, Validate & Bake. Confirm unchanged controls remain available, with one selected node's properties at a time.
- Test search with operation, map and group names; matching nodes retain their ancestors. Optional Maps is collapsed initially. Expand/Collapse and selection should never leave an inaccessible selected node. Reopen the groom and verify selection, search and foldouts persist without becoming defaults for another groom.
- Reorder passes and modifiers by sibling drag and ↑/↓. Verify top-to-bottom visual order matches evaluation; invalid reparenting, cross-group and locked drops must be rejected. Undo/Redo restores IDs/order and the correct selected inspector.
- Toggle Active without selecting a node; the effect updates, settings remain intact, and parent visibility/opacity is respected. Rename and duplicate passes/modifiers; duplicated mutable data must be independent. Remove uses a popup and Undo.
- Preview & Visibility and Settings must be reachable in the separate Hair Preview & Settings window from any node. Camera-focus functions, source/character/slot visibility, guide/child/root displays, depth, card wireframe and Full/Draft must not be lost or confused with a node's Active state. Verify old visibility filters/foldouts survive the move and the new window keeps its own tab/scroll preferences. Reset editor options must include all three windows without moving their docks; reset card setup must retain its popup and active-group scope.
- Selecting different nodes or just opening the tree must not mutate authored maps, guides, profiles or modifier values. Generation remains Preview → Accept/Replace, never implicit.
- Use only tree selection to navigate: there must be no Paint/Guides/Sculpt/Cards shortcut row, Continue/Go to buttons, child-node buttons, helper picker, or old layer stack in Properties. Select Grooming itself and confirm no previous pass remains paint-active; select an actual sculpt pass to restore combing. Check Scene tool changes, active sculpt target feedback, Shift temporary erase, gravity release when focus changes, and UV drag cancellation when changing nodes.
- Exercise the tree's creation controls for groups, sculpt passes, modifiers, optional maps, helpers/bound Scene objects, and helper constraints. Verify the new item is selected and revealed. Test duplicate/reorder/remove and locked states, popup cancellation, Undo/Redo, and group scope. Validate & Bake shows the report/repair actions in Properties; Issues in Hair Nodes must locate the relevant setting or guide. Frame/Save/Help/Exit Stage must remain available in Hair Nodes with Properties closed.
- After all automated tests pass, review usability again: inspect empty-state guidance, locked/disabled explanations, search discoverability, missing resources, group scope, popup wording, docking, ordering affordances and documentation paths. Fix findings and rerun affected tests before handoff.

## Core workflow

### Sculpt edit-point regression

- On a copy of a groom, add Spline Flow to a sculpt pass, draw flow paths, then select the owning pass. Full-result combing must be blocked until an edit choice is made. Check the tree and Scene banner explain both choices; Properties contains only the selected node's tools/settings.
- Choose **Edit This Layer**. Earlier passes/modifiers remain active; this pass's modifiers, later passes, legacy group modifiers and constraints are marked temporarily bypassed. Orbit and perform at least 40 successive comb strokes. Guides, hit-testing and idle cards must agree, without alternating jumps or stretched segments. The bypass stays between strokes.
- Return to Final Preview. Verify every saved Active/visibility flag and modifier parameter is unchanged. Select a modifier and verify the full result is shown but brush write-back to its owner is blocked. Reopen and ensure temporary edit-point state is not restored.
- Choose **Add Finishing Sculpt Layer**. Before painting, its shape must exactly match the final input, including constraints. Repeat combing after Resample, Spline Flow and gravity. Test both additive and Override at 0.25/1 opacity; the first Override stroke must not jump. Test very low/zero opacity, locked/inactive passes, no-op clicks and Undo/Redo without silent layer creation.
- Test Grab, Smooth, Clump, Part, point handles and held gravity at both upstream and finishing edit points. Confirm root influence, fixed roots, frozen anchors, object/guide transforms and segment lengths. Length/Shorten intentionally change lengths. Slice and mirrored slice write only to the active layer; hiding it restores the uncut input and authored guide point counts never change.
- Compare child and card previews in both edit/final modes, Full/Draft, other visible groups, isolated-guide scope, and release evaluation. No stale final-result children or cards should remain over the editing shape while a rebuild is pending. Runtime/bake must always evaluate the full stack.
- Test Nodes at 320 px and wide docks. Editing/bypassed labels must be understandable and must not look like saved Active toggles. Check Scene banner controls at narrow widths, return-to-final, node changes, and tree reordering; pre-constraint and finishing passes must not reorder across their boundary.

### General checks

- Open from a readable Mesh, generated DynamicCharacterAvatar, and existing HairGroomAsset.
- Open from an avatar whose root or skeleton is rotated/translated away from source-mesh orientation; confirm the painted surface, generated/authored guides, children, helpers, card preview, handles, and groom brushes all remain registered to the baked character.
- Cancel new-groom save and confirm no source object or asset changes.
- Move through every workflow node and confirm its scoped Scene tool activates: Growth cannot keep painting in Guides, Groom, Cards, Optimize, or Validate & Bake.
- Paint and erase Growth Area with mouse and pen; confirm Alt navigation remains available.
- Add Flow Align on a posed groom with guide cards and children. Use an oblique source-local direction and compare Alignment 0, 0.5 and 1. Roots must remain anchored, input segment lengths preserved, and card counts unchanged. Guides And Children must not create an extra-offset or extra-rotated child layer; Children affects children only. Test opposite/zero directions, a root-to-tip ramp, disabled/zero weight, modifier reordering, Undo/Redo and repeated preview rebuilds. Compare Full preview/runtime/bake output. Existing amount 0.1 now means 10% alignment, not a translation.
- On a dense scalp (including 2–10 mm triangles), view the surface obliquely and orbit while hovering/painting Growth Area. The primary radius and hardness rings must follow the interpolated posed surface normal, not face the camera, and match the mirrored cursor's surface alignment. Repeat Shift+right-drag resizing on rotated/non-uniformly scaled characters and meshes without normals. Groom comb circles should remain camera-facing by design.
- Enable Growth **Mirror X** from both the workspace and Scene toolbar, then toggle it with `M`. Confirm paint and erase affect both sides across source-local `X = 0`, the mirrored cursor follows a rotated/posed avatar, hidden slots remain excluded, and centerline vertices do not receive double strength.
- Confirm Growth and Groom brushes match Overlay Painter: the inner hardness ring receives full strength, the edge falls off linearly, Shift+right-drag changes radius horizontally and hardness vertically, brackets change radius, and Shift+brackets change hardness.
- Confirm the translucent blue-to-orange Growth overlay updates over the posed authoring surface while the character materials remain visible beneath it.
- Select triangles, add/subtract selection, grow, shrink, invert, clear, and convert between selection and map.
- Preview the same guide seed twice and confirm root positions match.
- In Guides → Automatic Guide Generation, compare Root Uniformity 0, 0.75, and 1 at the same count/seed with low Minimum Spacing. Higher uniformity should reduce close pairs and uneven gaps. Confirm Minimum Spacing is still enforced, impossible count/spacing combinations warn rather than relaxing spacing, and zero Growth Area gets no roots. Paint a Density contrast and verify denser regions still get more guides. Changing a generation setting must clear the old dashed preview without changing accepted guides; Preview then Accept/Replace remains explicit. Automated tests measure nearest-neighbor spacing variation over multiple seeds and exercise spatial-index snapshot/tail boundaries.
- Confirm preview guides are dashed and temporary, then accept, replace generated-only, and cancel generation without deleting hand-authored guides.
- Place a guide, move its control points, delete it, undo, and redo.
- In Guides and Groom, select guide rows and click Delete selected once: the guides and their sculpt deltas disappear and preview cards refresh. Undo/Redo restores/removes them as one action. With a search filter, disabled guides, and isolation enabled, Remove all must confirm the full current-group count and remove that group's guides only. Cancel must change nothing; maps, layers, modifiers, card setup, and other groups must remain. Test a locked group, no selection, multiple pages, and switching groups while confirmation is open.
- Acquire the brush at guide control points and midway along deliberately sparse segments. Confirm the entire displayed segment contributes control influence, every nearby guide inside the radius responds, resampling modifiers do not create dead zones, and locked layers/groups do not change. In **Through Depth**, verify projected guide layers move together; in **Depth Volume**, verify the brush uses a depth-isolated 3D volume.
- Comb from front, side, and back views across overlapping guides. Confirm the stroke remains continuous through depth changes, roots stay fixed, and every segment retains its pre-stroke length for Comb, Grab, Smooth, Clump, and Part.
- Perform at least ten consecutive Comb strokes, including strokes that begin near root handles and selected control points. Confirm every stroke acquires immediately, remains smooth through MouseDown/Drag/Up, and no alternating stroke is lost or delayed.
- Select **Cut** and drag short, long, horizontal, vertical, and diagonal slice lines from perspective and orthographic views. Confirm only guides whose projected curve crosses the finite line are cut, the root side remains, the new tip is exactly interpolated, all sculpt-layer arrays stay aligned, and one undo restores the full slice.
- Enable **Mirror Slice Across X** from the Groom panel and Scene toolbar, then toggle it with `M`. Confirm one gesture trims the matching source-local X side without double-cutting centerline guides.
- In the **Preview & Visibility** page, hide/show root handles, guide splines, child splines, and selected control points independently. Sweep Root Handle Size from 0.1 to 4 and confirm it affects only handle display and selection—not guide geometry or baked output.
- At narrow and wide workspace sizes, switch through all main node types and confirm preview mode, quality, rebuild, child inclusion, guide display, scene depth, authoring surface, character preview, and helpers remain in Hair Preview & Settings → Preview & Visibility. Setup, Cards, Properties, and the Scene toolbar must not duplicate those controls. In Guides And Children mode, hide both guide and child splines; confirm the lines disappear. Switch to Cards and verify hiding child splines did not remove child cards. Turning off Include children in preview should remove them from previews only, never the release output.
- Deliberately stretch a guide through test data, run **Repair Existing Stretch**, and confirm its current directions are retained, authored segment lengths are restored on a new layer, and one undo removes the repair.
- Hold **Apply Gravity** briefly and through a full settle. Confirm release stops immediately, one undo restores the whole hold, roots and segment lengths remain fixed, and Card Separation prevents neighboring cards from collapsing into a single sheet.
- Create curve and collision helpers, move them, add constraints, break a helper reference, and verify validation reports it.
- Preview ribbon and 3-, 6-, and 12-sided tapered tube profiles.
- On existing guides created with a zero-width tip, set Root Width and Tip Width equal: every segment should be a uniform ribbon/tube, with no widened final segment. Test zero, narrower, equal, and wider Tip Width and a zero Root Width; all transitions should be gradual and finite. Repeat with nonuniform guide spacing, children, width variation/modifiers, positive and negative width sculpting, a custom width curve, and after slicing. Profile edits must update the current preview without regenerating guides; compare runtime/baked mesh widths to preview.
- Assign an alpha atlas and the default material from `UMA3_HairShader_URP.shadergraph` (with no texture assigned on that material). Check that both Scene cards and the inline material-rendered card show strand cutouts, not solid rectangles. Change the atlas texture and undo without remeshing; clear it to restore the material's base texture. Confirm the shared material remains unchanged. Give two groups different atlas profiles with the same material and verify each keeps its own texture. Rebuild and reopen the stage repeatedly to check preview material cleanup.
- Create numbered UV areas, select a non-contiguous subset such as 2, 3, and 7 for one group, and confirm every generated card stays inside those rectangles.
- Rebuild the same groom repeatedly and confirm weighted UV-area assignment is deterministic; change the child seed and confirm the distribution changes.
- Rename and reorder atlas areas and confirm group assignments survive by stable area ID. Delete every selected area and confirm validation reports `MissingAtlasRegion`.
- Switch every LOD repeatedly and confirm deterministic counts and stable material order.

## Persistence and safety

- Change every category of editor options (brush, root influence, gravity, mirrors, guide generation, visibility/slot filters, preview mode/quality, handles/splines/wireframe, workspace foldouts, UV alpha/background/snap/units/pan/zoom). Save/close/reopen and restart Unity. Confirm each groom restores its own settings and a new groom inherits the last-used defaults without inheriting painted values, guides, sculpt layers or selections from another groom.
- Assign albedo/normal/mask/material, edit UV sets and card/child/LOD/bake settings, then create a new groom. Confirm private card/atlas copies, retained texture/material references, and a fresh bake output name. Rename/move source assets and repeat after restarting Unity; deleted references must fall back safely. Check material Inspector edits are saved.
- Expand **Reset to defaults**. Editor reset must restore defaults without touching authored hair or textures. Cancel setup reset and confirm nothing changes; confirm it and verify new private default resources, empty texture/material assignments, retained guides/paint/layers/old assets, and working Undo. Other groups' resources must remain unchanged. **Forget new-groom defaults** must remain forgotten on subsequent idle saves until setup changes.

- In **Preview & Display**, switch **Show card wireframe** off/on while a UV set is highlighted and while the card renderer is selected. Off must hide card outlines, not card materials or guide lines; card picking and UV-set selection must still work, without mesh regeneration. Also repeat after a stroke and a preview rebuild.
- In **Groom**, compare **Root Influence** 0 and 1 with Comb, Grab, Smooth, Clump, Part, and Gravity. Higher influence bends the base more readily; roots and segment lengths stay fixed, and frozen points stay protected. Length/Shorten, Cut, Width, and Freeze/Unfreeze must not change behavior with this setting.
- Set **Root Embed (mm)** to 0, 1, 3, and 20 under **Cards → Card geometry**. Guide and child card bases should tuck inward with a smooth fade, not shift the entire card or authored guides. Check ribbon/tube, sparse/dense sampling, transformed authoring poses, multiple groups sharing a profile, undo/redo, save/reopen, and baked output. Repeated rebuilding must not accumulate inset; 0 must restore the original mesh.

- Click **Focus Head** and **Focus Neck** directly below **Focus current area**. Confirm the pivot is 5 cm above the corresponding preview-pose bone in stage space, with the camera 0.45 meters from that pivot, without changing viewing angle, projection, visibility, or groom data. The offset is omitted when `NoFudge` is defined. Repeated focus must not accumulate the offset. Test perspective/orthographic, tall/wide views, changed FOV, no paint, moved/rotated/scaled characters, and animated source characters. Missing bones disable only their own button; Head_End/NeckTwist must not count as Head/Neck.

- Paint a small head patch, then click **Focus current area** under Avatar Visibility. Confirm framing contains the influencing bones and painted surface, not unrelated body bones; retain the current viewing angle. Repeat with only the left half, right half, front, and a quarter of the head painted: the orbit pivot must remain on the character's vertical axis, with symmetric bounds expanding to fit the region. Repeat on a moved/rotated/scaled avatar with an offset renderer in an authored pose, a single-bone patch, a soft edge, another group, and a standalone unskinned mesh. Select Density or Length and confirm focus still uses that group's Growth Area. Erase all Growth Area and confirm the camera stays put with an explanatory status. Focusing must not change visibility, selection, paint, guides, or baked output.

- Save, close the stage, reopen, and verify groups, active data, maps, guides, layers, modifiers, helpers, constraints, profiles, LODs, and bake settings.
- Force a script recompile while the stage is open and confirm it exits safely.
- Enter Play mode while the stage is open and confirm it exits safely.
- Restore the autosave recovery snapshot and verify one undo step restores the prior groom.
- Change source triangle order and verify baking is blocked by topology validation.
- Make an output asset read-only or force an invalid output folder and verify the prior baked output remains usable.

## Runtime and UMA

- Generate with `HairGroomRuntimeAPI` in Play mode without network access or provider configuration.
- Regenerate and dispose repeatedly; verify generated meshes do not accumulate.
- Exercise all LOD levels through `HairGroomRuntimeComponent.SetLodLevel`.
- Bake a Unity mesh, SlotDataAsset, existing-overlay recipe, generated-overlay recipe, and all LOD meshes.
- Confirm updating a bake preserves existing asset references.
- Equip the recipe on every declared compatible race and inspect head, neck, and facial animation.
- Inspect UVs, atlas flips, alpha, tangent-space normals, material count, bounds, and backface behavior.

## Scale and accessibility

- Test 100%, 150%, and 200% editor scaling at narrow and wide dock widths.
- Complete the core workflow with mouse only and pen plus visible controls.
- Navigate task controls with keyboard focus and remap all registered shortcuts.
- Verify information is not conveyed by group color alone: selected guides must also be wider/outlined and validation must include text.

## Expanded grooming / card workflow regression pass

- On a cleared Growth Area map, compare hardness 0/0.5/1 with the same radius and strength. Slowly retrace one stroke without releasing: its edge must stay soft, independent of mouse sample count. Move the brush center across an edge vertex to increase its coverage; release and start another stroke to build up. Repeat with Erase, reduced strength, overlapping mirrored footprints, locked maps, and Undo/Redo. Confirm the overlay tracks fractional vertex weights rather than quickly saturating to full paint.
- Before accepting generated guides, check the bright dotted candidate preview with Z-buffer on/off. Repeat after acceptance with Guides and Children enabled. All three guide paths must hide behind the head consistently; front lines and exposed tips remain visible. `StageDottedGuidePreviewsRespectDepthToggle` exercises the actual stage drawing scope in perspective/orthographic views and reproduced the missing candidate-preview depth scope before the fix.
- Shift+right-drag over guides and the growth surface. Confirm the actual brush/falloff circles stay anchored and resize live, matching normal painting (including source/pose scale and mirrored growth). Drag beyond the silhouette and start in empty space; circles must not disappear or jump. Release, Escape, Alt-navigation and lost capture must remove the adjustment preview without painting or adding an undo stroke.
- With Preview & Display Z-buffer enabled, orbit an opaque head in perspective and orthographic Scene views. Back-side splines and root dots must disappear behind the head; front guides and tips extending beyond its silhouette remain visible. Repeat with the scalp hidden, avatar hidden, slot visibility toggled, selected controls, freeze colors and generated children. X-ray must restore the hidden guides. The depth-only guide pass must not alter preview colors; standard alpha-tested textures must retain their holes and transparent surfaces must not become solid occluders. Automated pixel-readback coverage exercises actual AA handles/root dots against a cleared depth target.
- Compare Through Depth, Visible Hair, Selected Guides Only, and Depth Volume from front and side views. Confirm hover colors/counts agree with the editable guides, including frozen tips and empty selections.
- Search and page beyond 200 guides. Toggle multiple rows and Scene roots, isolate, perform each bulk action, and Undo. Confirm isolation never filters a release bake.
- Rename, duplicate, reorder, solo, and remove sculpt layers. Check locked-layer controls and opaque Override layers. Reorder/remove modifiers and verify type-specific controls change the expected parameter.
- Drag a mirrored slice without releasing. Compare red discarded tips and count with the result after release; test Escape, finite-line misses, centerline guides, and protected frozen tips.
- Settle against a correctly wound scalp/body with collision off/on and different clearances. Check roots, full freeze, every segment length, tight bends, and posed avatars. Verify hidden slots still participate in collision.
- Nudge and duplicate UV sets, frame a narrow set, click a card to select its UV set, and select a UV set to highlight its cards. Repeat for multiple materials and both ribbon/tube profiles.
- Check the inline card material preview with transparent and alpha-clipped materials, front/back, tapered profiles, and the project's render pipeline. It must stay in the existing Cards dialog.
- Make shared profiles and atlases unique. Confirm only the active group is reassigned, selected UV IDs are remapped, texture/material references stay shared, and Undo restores references.
- Compare Draft and Full previews and bake counts. Check UV-only updates keep the same Mesh, display toggles do not rebuild geometry, and guide-only modes allocate no card meshes.
- Keep a UV drag, a slider drag, and text editing active across the autosave interval. Confirm Save queues, no write stalls the gesture, and an idle save occurs once; leave unchanged for another interval and confirm no recovery rewrite.

## Shared color parameters and vertex-color gradients

- In Cards, assign a material, then a Shared Color Table immediately beneath it. Select an entry with matching shader parameters. Confirm the source material, Scene preview and inline material preview update; Undo/Redo restores/reapplies the source material values. Confirm unsupported properties are skipped and the table is not edited.
- Test empty tables, entries without shader properties, missing material, and an invalid/removed entry index. Reapply after editing the table. Change the assigned material with a valid selection and confirm the color is applied. Clearing the selection must not silently reset material values. Verify the inline shared-material warning.
- Enable root-to-tip vertex colors on both ribbon and tube profiles. Use 6 samples, red with alpha 0 at the root, blue with alpha 1 at the tip, and 2 solid root segments. Rows 0–2 must be exactly root RGBA; rows 3–5 must form a linear fade to exact tip RGBA. Check children and guide cards, both sides, root embedding, and repeated pooled updates. Use a shader that reads vertex colors to inspect visually.
- Test 0 solid segments and an oversized hold count at Full, Draft and all baked LODs, including 2-sample cards. Keep at least one fade segment. Disabling the gradient restores the group's previous mesh color without changing guide display colors.
- Save/reopen, create a new groom using last-used settings, and Reset card setup. Table/entry, profile RGBA and hold count should persist/inherit; reset creates default resources without altering shared source resources. Verify baked meshes retain the COLOR channel (including alpha), and material parameter edits are saved.

## Performance captures

Benchmark at minimum:

- 250, 1,000, and 5,000 authored guides.
- 1, 4, 8, and 16 children per guide.
- 8, 12, and 24 samples per card.
- Ribbon and 3-, 6-, and 12-sided tubes.
- 50k, 100k, and 250k source vertices while painting.

Record stage open, brush p50/p95, preview rebuild, validation, LOD switch, bake duration, managed allocations, generated vertex/triangle count, material count, and peak memory.

### Card preview refresh optimization (2026-09-09)

Same-machine before/after measurements in isolated Unity **6000.3.18f1**, Windows EditMode, Debug assemblies. Seeded ribbon fixtures use **four children per guide**, **12 samples**, backfaces, **Full** quality and an active authoring pose. Values are median synchronous `HairCardStage.RebuildNow` times after warm-up, including evaluation, pose conversion, meshing/upload, brush-cache refresh, and preview validation. GC collection is outside timing; allocations are measured separately with the current-thread `GC.Alloc` recorder. No jobs/threading rewrite or automatic quality reduction was used.

| Authored guides | Total cards | Before | After | Allocation events before / after |
| ---: | ---: | ---: | ---: | ---: |
| 256 | 1,280 | 62.764 ms | 36.131 ms | 18,028 / 32 |
| 1,000 | 5,000 | 502.638 ms | 142.267 ms | 70,122 / 36 |
| 5,000 | 25,000 | 9,105.056 ms | 764.465 ms | 350,138 / 40 |

The dominant quadratic cost was `HairAuthoringPose.MatrixForGuide`: each child and displayed guide scanned the groom for its parent. The standalone pose benchmark fell from **7,125.212 ms to 57.535 ms** at 5,000 guides, even using fresh output curves. The stage also reuses posed output curves. The original and final mesh fingerprints match for all three fixtures: `29A0B7109919E270`, `F04647F82A521752`, `7CCD948FC3F3C352`.

Changes and ownership:

- Refresh pose matrices once per evaluation, with constant-time guide-ID lookup for children and Scene view drawing. Refresh bindings after edits/reorders/removals; the posed source mesh remains a captured snapshot.
- The stage uses opt-in `HairGroomEvaluator.EvaluateInto` and `HairCardMeshGenerator.Update` APIs. These mutate caller-owned preview results and reuse curve objects, point lists, card spans, metadata buffers and a dynamic Unity mesh. Do not retain their curves/spans as historical snapshots. The original `Evaluate` and `Build` APIs still produce independent outputs for baking/export.
- Width curves are sampled once per profile/resolution/build. Tube angles are calculated once per card, not once per vertex. Root-map lookup no longer creates a capturing delegate for every root.
- UV highlighting reuses edge indices when the exact selected triangle sequence is unchanged, updates line positions after deformation, and skips generation when wireframe display is off. Changed topology/selection rebuilds the edges. No approximate hashes are used for cache validity.
- Removed curve objects are released from preview pools. Closing the stage releases workspaces, posed/evaluation results, highlight buffers, and the native mesh. Empty output clears the mesh bounds as well as geometry.

Run EditMode category **`HairPreviewRefreshProfile`** to reproduce the stage and separate evaluation/pose captures. Profiler markers now include `HairCards.RefreshPreview`, `HairCards.PosePreview`, `HairCards.RefreshHighlight`, and `HairCards.ValidatePreview`, alongside the existing core markers. Final full regression run: **177/177 passed**, build: **0 warnings / 0 errors**. Added coverage includes repeated strokes/Gravity on the same mesh, pooled output versus fresh evaluation, profile and UV changes, ribbon/tube changes, empty output, guide reorder/removal, pose rebinding, highlight cache invalidation and warm allocation counts.

These are synthetic refresh timings, **not Scene view frame-rate guarantees**: the source is a small triangle fixture with no production avatar materials/modifier stack and no selected UV highlight in the timing case. Initial allocation, large source meshes, complex modifiers, many materials and Scene view drawing add cost. The 25,000-card Full case remains sub-second, not single-frame. Use the existing Draft preview for exceptionally dense interactive work; baking stays full quality. Profile an actual posed groom before committing to a larger jobs/Burst rewrite.

Manual checks on a representative avatar: generate cards, comb several successive strokes and hold/release Gravity; ensure there is one idle refresh per stroke and none during the stroke. Switch Full/Draft, LOD and card/guide modes; cut, Undo/Redo, add/remove/reorder guides and groups, change atlas/material and width profiles. Compare against reopening the stage. Toggle UV outlines and card wireframe while grooming; check both positions and selection after topology changes. Profile p50/p95 end-of-stroke latency and retained memory with the actual source mesh and materials.

### Dense-search optimization capture (item 11, 2026-09-08)

Measured in an isolated Unity 6000.3.18f1 Windows EditMode project using the same seeded fixtures and Debug assemblies before/after. Each timing is the median of three synchronous operations after a warm-up; collection occurs outside the timed interval. These are component timings, not end-to-end Scene view frame times or release-build guarantees. No jobs/threading rewrite was made.

| Workload | Before | After, fresh workspace | After, retained workspace |
| --- | ---: | ---: | ---: |
| 1,000 guides × 8 children, 8 samples | 2,617.868 ms | 104.427 ms | 108.663 ms |
| 5,000 guides × 8 children, 8 samples | 62,383.022 ms | 625.310 ms | 620.185 ms |
| Surface projection: 128 guides × 8 points, 50,000 source vertices | 1,307.763 ms | 141.205 ms | 14.069 ms |
| 5,000 ribbons × 12 samples | 92.035 ms | 93.532 ms | 90.878 ms |
| 5,000 six-sided tubes × 12 samples | 252.356 ms | 225.161 ms | 222.982 ms |

Managed allocation **event counts**, measured in a separate operation with Unity's `GC.Alloc` recorder:

| Workload | Before | After, fresh workspace | After, retained workspace |
| --- | ---: | ---: | ---: |
| 1,000 guides × 8 children | 90,038 | 55,098 | 53,030 |
| 5,000 guides × 8 children | 450,040 | 275,113 | 265,032 |
| Surface projection | 2,719 | 1,863 | 1,815 |
| 5,000 ribbons | 30,117 | 126 | 8 |
| 5,000 tubes | 30,128 | 137 | 8 |

Counts are not allocated bytes: this editor's `GC.GetAllocatedBytesForCurrentThread()` returned zero, so it was rejected as a measurement source. The recorder follows Unity's [current-thread allocation capture pattern](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html); a positive-allocation control also checks the zero-allocation search/resample test. Mesh rows exclude editing metadata for an apples-to-apples comparison. Retained ribbon meshing **with** editing metadata took 97.238 ms and 5,036 allocations (including independently owned card spans and UV data).

All five pre/post geometry fingerprints matched exactly: child points, widths, rolls and root normals; mesh positions, normals, tangents, UVs and triangle indices. Ribbons showed essentially unchanged CPU cost; their improvement is allocation pressure, not a claimed frame-time speedup. Evaluation still allocates caller-owned output curves, point lists and identifiers deliberately, so retaining a previous result is safe.

Implementation and reproduction:

- `HairPointSpatialIndex` is an exact, reusable k-d tree. Nearest results keep distance/original-index ordering, including ties and coincident roots. Child interpolation still blends the same four neighbors with the same inverse-distance weights. Parent-only modes do not build an index.
- `HairEvaluationWorkspace` caches source vertices, normals and lazily requested submesh triangles once per pass. It refreshes channels on the next evaluation, even when the same Mesh is edited in place with the same vertex count. The projection tree rebuilds only when its exact position snapshot changes. Projection remains nearest-vertex based, not triangle projection.
- The stage retains evaluation/mesh workspaces and per-guide display/brush buffers. Scratch is reused; native output meshes and evaluation results remain independently owned. Removed/filtered guides are pruned from display/brush caches. Stage cleanup releases retained buffers.
- Run EditMode categories `HairDenseProfile` (fresh workspace) and `HairDenseCachedProfile` (retained workspace). Each emits a `HAIR_PROFILE` line with median milliseconds, allocation count and geometry fingerprint. The full verification run passed **119/119 tests**.
- Unity Profiler markers: `HairCards.Evaluate`, `HairCards.GenerateChildren`, `HairCards.ReadSourceMesh`, `HairCards.SurfaceProjection`, `HairCards.UpdatePointIndex`, and `HairCards.BuildMesh`.
- Before considering jobs: capture a representative posed avatar with its actual modifiers/materials, end-of-stroke rebuild p50/p95, authoring-pose transformation, validation, mesh upload and peak retained memory. Synthetic search wins do not remove those remaining costs.

Manual cache regression checks: comb several strokes; move/reorder/remove guides; switch groups, LOD, Draft/Full and isolation; slice to change sample counts; edit source positions/normals/topology in place; Undo/Redo each. Compare against a freshly opened stage, confirm removed guides disappear, then close the stage and verify its retained memory is released.

## Modifier correctness and gravity spaces

- Compare a Gravity modifier against the hold button for the same duration/strength, root influence, separation and collision settings. Compare guide positions, pinned roots, frozen tips and every authored segment length. Rebuild repeatedly; the modifier must not accumulate simulation time.
- Rotate the source object and its parent, apply nonuniform and negative scale, and use a posed guide frame. World gravity must continue pointing along Physics.gravity, while a local override follows the object/pose. Translation must have no effect on the gravity direction. Repeat via the runtime component and runtime API with a supplied matrix.
- Check zero duration, strength, blend, influence and Physics.gravity, plus singular transforms: no NaNs or unintended collision-only movement. Confirm new modifiers copy hold settings and saved gravity settings survive serialization/duplication/Undo.
- Bend with Smooth, Flow Align, Lift, Part, Clump, Curl, Wave, Noise and Helper Follow. Roots and segment lengths must stay fixed; root influence and zero influence ramps must work. Test Curl/Wave with adequate resampling.
- Set Length/Width to 2 and compare Guides to Guides And Children: children must not receive a second scaling. Repeat for Mirror, Gravity, Curl and Twist. Children-only effects must leave guides alone.
- Collision/Push Out: verify zero clearance still pushes out at full blend, partial blend changes response, and zero influence does nothing. Test sphere, box, capsule and plane helpers; missing/unsupported helpers should show useful warnings. Roots/frozen anchors/lengths take priority over impossible clearance.
- Trim By Mesh must keep the root-side prefix at its first source-mesh intersection, not push the strand outward. Test partial weight, cut offset, no intersections, frozen tips and Undo.
- Confirm Curve/Gravity help and controls render in the unified property box at narrow/wide workspace widths. Check all release LODs use the open stage's gravity frame but do not inherit solo/isolation/draft filtering.

## Source-mesh persistence and repair

- Create a groom from a generated UMA avatar, paint several maps and groom its guides. Save, close Unity, reopen without the character scene loaded, and open the groom from its Inspector. The readable embedded source and all authored data must remain available.
- Reopen with the original saved character scene loaded. Verify character preview, skin pose and visibility controls still work. Regenerate/delete the original character and confirm its mesh cannot mutate/destroy the saved source snapshot.
- Repeated Save/reopen must not add mesh copies. Imported mesh sources stay external references. Generated snapshots retain indices, weights, bind poses and UVs but omit animation blendshapes. The Project asset must remain a HairGroomAsset, not switch its main object to Mesh.
- Open an older groom with its temporary source still alive; opening/Save should persist it. For an already-missing source, verify map arrays are retained, then restore from the original generated character/renderer or mesh through the Inspector.
- Try a same-vertex-count mesh with different triangle order, an unreadable mesh, and an unrelated object: repair must fail without changing paint, guides or source identity. A missing original scene should produce repair guidance, never pick an arbitrary scene character.

## Two-pass card materials

- In Cards, assign First Pass Material and optional Second Pass Material in the existing Texture & UV Setup area. Verify both Scene and inline card previews draw both, with separate cutoff/blend settings and shared atlas textures/vertex colors. Test built-in and the project's active SRP shaders.
- Use a later render queue for the second pass; equal/earlier queues should show a warning, not silently modify either material. Test alpha-clipped coverage followed by transparent edges from front/back views.
- Add/remove/replace second-pass materials, Undo/Redo, reopen the groom, create a groom inheriting defaults, and reset card setup. Single-pass grooms retain their existing appearance.
- Test multiple atlas groups, selection/highlights on either pass, LODs and runtime generation. Both passes must follow the same deformed card; geometry counts stay unchanged, with one extra draw per enabled second-pass atlas.
- Apply shared-color parameters and Undo: both materials restore their previous values, unsupported properties are ignored, and shared references are edited only once. Preview/runtime texture binding does not modify either shared material.
- Bake meshes and a UMA slot/recipe. Meshes retain second-pass draw submeshes; the UMA renderer uses its UMAMaterial Second Pass. A mismatch should be reported during release validation; assigning the matching UMA material must not produce a third/fourth pass.

## Growth-map painting and clipboard

- Start on the Growth / Density node. Expand Optional Maps for Density Multiplier and styling maps. Select a map to paint it, collapse its parent, and verify selection moves to the collection and painting stops. Selecting the map again must reactivate painting. Reopen and reset preferences to test foldout persistence/defaults.
- With Minimum Spacing 0 and a full-density budget of 100, paint the same footprint uniformly at 1, 0.5 and 0.25: preview targets and counts should be 100, 50 and 25. Multiply 0.5 primary paint by 0.5 optional density: expect 25. Zero in either map should suppress growth. Try a budget of 1 with tiny paint: zero is valid, with an explanatory message.
- Paint soft edges and nonuniform densities on unevenly tessellated meshes and combined character meshes. Unpainted body area must not dilute the scalp budget. Preview must separate the full budget, area-weighted average density, adjusted target and actual placed count; excessive Minimum Spacing explains any further shortfall.
- Paint the optional multiplier, return to the primary map and check its non-neutral status remains visible. Reset it to 1 from Optional Maps and Undo/Redo. Visibility V must hide only the overlay, not disable its effect. Test locks and map Copy/Cut/Paste from both primary and optional maps.
- Change paint/Undo after preparing a guide preview: the old preview must be discarded rather than accepted with stale density. Existing authored guides/cards must remain unchanged until a fresh preview is accepted/replaced. Verify Replace Generated Only preserves manual guides and that Accept intentionally adds another batch.

- While painting, press/release either Shift key before and during a stroke. Verify PAINT/ERASE hints, Erase button and both mirrored brush colors agree; one Undo restores the whole paint/erase/paint drag. Selected Erase stays selected after release.
- Leave the Scene view, cancel with Escape, switch tools, and return without Shift held: temporary Erase must not stick or affect Comb/Length/Freeze. Shift + right-drag and Shift + brackets must retain their brush-adjustment behavior. Check top-left helper wrapping at narrow and wide Scene view sizes.

- In Growth, select Growth Area, Density, Length and a custom/flow map. Confirm Copy/Cut/Paste appear in the selected map's Operations area at narrow/wide window sizes.
- Copy leaves all source data unchanged. Cut restores the source default, not always zero; paste into another map/type/group preserves destination metadata and clamps values to its range. Hidden/unselected vertices are included.
- Undo/Redo Cut and Paste separately, paste repeatedly, and edit the source/destination after copying; the clipboard must remain an independent snapshot.
- Copy from locked maps/groups is allowed. Cut/Paste on locked targets are disabled and command APIs reject them. Same-count meshes with different identities or changed triangle topology must not accept a paste.
- Cut, trigger a script reload, and paste afterward; clipboard data must survive. Switch to another groom on the same source mesh and verify transfer; the OS text clipboard should remain untouched.

## Spline Flow

- Add Spline Flow to a layer; test the property box at 760px and 1200px. Draw several scalp paths with guides/cards visible. Verify the live line, arrows, green START and orange END. Release updates cards, not every sampled mouse event.
- Try Follow direction 0 / Outward facing 1 on an existing groom, then both at 1. Orbit from front/side/back; facing must not depend on the camera. Check upright hair, flat hair, bank ±90°, child roll variation 0/nonzero, and single/double-sided cards.
- Compare one versus four nearby paths, sparse versus dense points on the same line, radius boundary fades, opposite scalp sides, reversed paths, opposing parting paths, short paths under long hair, and curved paths. Roots/guide segment lengths stay fixed and hair does not collapse onto the paths' positions.
- Edit points on the surface, insert with Shift-click, delete down to two points, reverse/duplicate/remove, draw with Mirror new paths, and mirror an existing path. Inspect surface anchors on asymmetric heads and hidden slots. A failed mirror must keep the original without adding a broken partial path.
- Cancel a draw/point edit with Escape, Alt navigation, switching layers/tools or leaving the Scene window; no partial edits persist. Undo/Redo a complete gesture, mirrored pair, reverse, deletion and duplicate independently. Lock a layer/group, disable the modifier/path, hide or solo a layer and change opacity/root influence/freeze/stiffness.
- Test paths with scene depth enabled/disabled. Guide root/control handles must not intercept flow gestures. Turn Show helpers off: edit paths stay visible while editing, then obey visibility on Done. Reopen the groom and duplicate its modifier/layer; all path settings persist and copies do not alias.
- Pose/rotate/nonuniformly scale a source character, generate children and multiple LODs, run runtime generation and bake. Verify consistent flow/facing, normals and both material passes. For tight scalp bends, inspect contacts and place collision/settling after flow as needed.
- Profile 256 guides with 16 children each and several paths. Check `HairCards.SplineFlow.BuildField` runs once per modifier/evaluation and no per-strand managed allocations appear after warm-up. Rebuild after modifying source vertices/normals and after reversing/deleting a path to check cache invalidation.

## Sculpt-pass and modifier nodes

- Open a groom with sculpting and group modifiers. Confirm the same hair result and an Imported Modifiers pass at the end of evaluation order. Undo the import; repaint must not reapply it. Select Unorganized modifiers and explicitly import, then Undo/Redo.
- Add layers and modifiers. Select each row: only that row highlights and only its properties appear. Collapse a selected modifier's parent: its layer becomes selected. Reopen the groom and verify selection/foldouts persist.
- Test Nodes at 320px/500px and Properties at 480px/1000px, with enough modifiers to scroll. Selection must be revealed in Nodes and edited in the separate dockable Properties window. Materials & UVs must use the full available Properties width, without reserving room for the removed explorer panel.
- Reorder passes and modifiers; both lists run top-to-bottom, with pass sculpting preceding its modifiers. Hide/solo a pass and verify its modifiers follow for both guide cards and children. Test opacity 0, 0.5 and 1; lock a pass and check modifier controls are disabled.
- Duplicate a layer: changing its copied modifier/ramp must not alter the original. Duplicate/remove a modifier; remove an entire layer; Undo/Redo each. A removed modifier must not remain in the properties box.
- Put Resample before a later additive/override sculpt layer; confirm valid points and correct tip offsets. Rebuild, change LOD, and compare runtime/baked output. Groom with a modifier selected: the owning layer remains the painting destination under the existing editable-layer rules.
