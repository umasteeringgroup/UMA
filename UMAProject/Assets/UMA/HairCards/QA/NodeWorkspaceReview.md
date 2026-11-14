# Node workspace — preservation and usability review

## Groom Outliner rebuild — 2026-11-09

The IMGUI node list was rebuilt as the **Groom Outliner**, a UI Toolkit `MultiColumnTreeView`. The groom data, evaluation order, selection keys and saved per-groom preferences are unchanged.

- **Window:** toolbar (**Add ▾**, **Issues (N)**, **⋮**), a step bar (Setup, Growth, Guides, Groom, Cards, LODs, Bake), search, and **On** / **Lock** columns. Rows show reference badges (`mask:`, `helper:`, `continues:`, `growth:`, `N forms`, `used by N`, `finishing`) and temporary states (`editing`, `bypassed`, `inspecting`, `mask shown`, `solo`). Step numbers were removed from item names.
- **Actions:** item management moved to the right-click menu and **Add ▾**: create, Active, Locked, Move Earlier/Later, Duplicate, Remove…, helper constraints, mirrored helpers, form bindings and Organize. Sibling drag and drop and the keys Delete, Ctrl/Cmd+D, Alt+↑/↓ and Ctrl/Cmd+F are also available. Editing tools moved to the selected item's Properties: Edit This Layer / Return to Final Preview, Add Finishing Sculpt Layer, the Generate Hair presets, Inspect Population / Show Mask / Final Hair, Import Authored Guide File and form tools. Helpers and maps list their users under **Used By**.
- **Code:** one `HairGroomNodeHandler` per node kind owns its Active/Lock state, ordering, duplicate/remove rules, Properties drawer and menu items. `HairGroomNodeCommands` holds the commands and menus; `HairGroomNodeReferences` holds badges and usages. The tree rebuilds only when its structure signature changes, so painting does not rebuild it.
- **Not changed:** multiple groups (each ponytail, braid or bun as its own group) and shared growth maps work as before. Legacy "Unorganized modifiers" are still migrated only by the explicit Organize command.
- **Verification:** Unity 6000.3.26f1, UMA 3.1.5, main project. **676 Hair Cards EditMode tests passed, zero failures.** New `HairOutlinerTests` check that the item structure of all 10 packaged example grooms matches the pre-rewrite fingerprints, along with step selection, menus for every node kind, the structure signature, the Lock column, references, drop rules and the live window.
- **Manual checks still needed:** the outliner's look at narrow docks, drag and drop with a real mouse, and where context menus open.

## Whole-guide Erase review — 2026-09-12

- Added a distinct **Erase** button below the sculpt brush grid, also routed through the Scene tool selector. Red brush/hover feedback and top-left help distinguish whole-guide deletion from layer-local Cut and the existing reverse/paint-erase toggle. Properties and Scene toolbar hide inapplicable hardness/strength/reverse controls. Radius/scope remain available; saved preferences never rearm Erase on reopening.
- Targets displayed guide segments, including the final modifier result without an inverse deformation or edit-point change. Swept segment tests cover fast drags, and an empty-space start uses the scalp or Scene focus depth to establish the stroke plane. Selection/isolation and Visible Hair/Depth Volume scopes apply; any partial/full frozen point protects its whole guide. Root/control caps remain visible when requested but cannot capture input from Erase.
- Batched deletion removes authored guides and their deltas across the active group's sculpt passes. Other groups/modifiers are untouched. The brush retains reusable hit buffers, removes deleted display/selection entries immediately, registers one complete-groom Undo per stroke, and defers child/card regeneration to release. This is intentionally not a layer-local edit; the UI and guide explain that hiding the sculpt pass cannot undo deletion.
- Verification: actual Unity **6000.3.18f1** source import/compilation and **377 tests passed, 0 failed, 0 skipped** (76.97 s) in the isolated source-validation project. Metadata preflight covered 41 scripts; SHA-256 comparison matched all 90 C#/assembly-definition/import-metadata files to the working source. Results: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/6d95f173694e4b37a95252b9b9d0192a/editmode.xml`.
- The 24 added cases cover swept/degenerate hits, multi-dab Undo/Redo and delta cleanup, freeze protection, scope/isolation, surface occlusion between sparse points with transformed objects, final Spline Flow targeting, invalid/locked targets, safe startup preferences, empty-space stroke lifecycle, and Properties rendering at 320/650 px. Stroke tests call the same stage routines as Scene input; they do not certify OS mouse delivery or subjective production-Scene behavior. Those checks remain in the manual QA checklist. The production groom asset was not rewritten by this implementation.

## Sculpt edit-point safety review — 2026-09-12

The brush previously edited final evaluated positions but wrote those displacements before the active pass's modifiers. Length checks inside the temporary brush buffers could pass while re-evaluation stretched the input. The fix gives sculpting an explicit evaluation boundary and stores offsets at that boundary's control resolution. Empty Override passes capture their incoming shape on first touch, avoiding a separate first-stroke discontinuity.

- **Add Finishing Sculpt Layer** receives the completed guide stack, including group modifiers and constraints. **Edit This Layer** preserves earlier operations and temporarily bypasses its own modifiers and everything later in that group. **Return to Final Preview** and selecting another node leave edit mode without altering saved Active/visibility flags.
- The tree remains the single node navigator. Its actions and the Scene banner expose the safe editing choices; Properties contains only the selected node's settings/tools. Unsafe/locked sculpt targets show an explanation instead of silently creating another pass. Editing/bypassed status prefixes remain visible before long node names. Narrow Scene banners stack their action buttons.
- Guides, controls, hit-testing, children and idle card previews share the same boundary. Pending final-result previews are hidden on transition. Undo invalidates the brush cache and ends stale gestures. Temporary state is explicitly nonserialized, including during Unity hot reload.
- Finishing passes appear after Constraints and reorder within their own section. Cut is layer-local and does not truncate authored guides or other layers. The optional existing-stretch repair now writes after the full stack and skips frozen/out-of-scope guides; it is not automatic restoration of lost styling.
- Post-implementation usability checks exercised Nodes at 320/540 px and Properties at 650 px in final preview, upstream editing, return-to-final and finishing-layer states. Automated GUI rendering passed; the content-specific artist/Scene checks in the manual checklist remain appropriate for a real production hairstyle.

Final verification: Unity **6000.3.18f1**, actual Hair Cards sources imported and compiled in the isolated `tmp/HairCardsSourceValidation` project. **353 tests passed, 0 failed, 0 skipped** (72.62 s). Metadata preflight covered 39 C# scripts. SHA-256 comparison confirmed all **86 C#/assembly-definition/import-metadata files** matched the production workspace files tested. Results: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/46f1a8728ffe4b4da727a66e0caed567/editmode.xml`.

New regressions include 40 successive multi-dab comb strokes with Spline Flow, partial-opacity additive/Override layers, earlier resampling and changing sample counts, all length-preserving shape brushes, handles, layer-local cut/Undo, gravity/frozen anchors, constraints/other groups/child-domain cutoff, final-preview write protection, no-op/non-finite input, and transient edit state. The existing dense-flow benchmark evaluated 4,352 cards / 256 guides / 8 paths at a 47.02 ms median with zero mean managed allocation after warm-up on this test machine (not an end-to-end production-frame guarantee).

`Assets/temp/MaleHuman_HairGroom.asset` and user material/recovery assets were not modified. The previously documented `HairAtlasProfileAsset` script-filename warning is unrelated and remains outside this change.

## Scope and architecture

Groom Outliner is a typed, cached view over the existing groom and the single workflow navigator. Hair Properties is an independently dockable, properties-only editor window following the stage-owned selection. Hair Preview & Settings is a third independent window with Preview & Visibility and Settings tabs. No guide generation, asset conversion, sculpt migration or evaluation rewrite is performed merely by opening or browsing the tree.

Sculpt passes are the existing serialized sculpt layers. They now appear in forward evaluation order (top to bottom), followed by their owned modifiers. Dragging only reorders compatible siblings. Resource branches and shared helpers are not extra evaluation passes.

## Functionality inventory

| Existing capability | Location in the Groom Outliner workspace |
| --- | --- |
| Source, topology, reprojection, source/character recovery | Source & Setup; existing groom Inspector repair controls |
| Group name, role, color, visibility, inclusion, locking, add/remove | Group row and Properties; **Add ▾ → Group**, right-click **Remove…**, **On** / **Lock** columns |
| Growth/density painting, all optional maps, mask display/lock, map clipboard | Growth / Density and Optional Maps → individual map |
| Mirroring, temporary Shift erase, falloff, vertex selection and map operations | Selected map properties and contextual Scene controls |
| Guide distribution, deterministic preview, Accept/Replace/Cancel, manual placement | Guides |
| Guide search, pagination, selection, isolation, batch edits, selected/all deletion | Guides → Authored Guides; Grooming/pass → Guide selection & isolation |
| Comb, Grab, Smooth, Length, Cut, Width, Clump, Part, Freeze, root influence | Grooming or selected Sculpt Pass |
| Hold-to-settle gravity, collision, clearance, separation, stretch repair | Grooming or selected Sculpt Pass |
| Pass opacity, blend, visibility, lock, preview-only Solo; Edit This Layer; finishing layers | Sculpt Pass properties; **On** / **Lock** columns |
| Modifier parameters, Active, duplicate/remove, ordering, domains and helper binding | Modifier properties; duplicate/remove and sibling ordering in the Groom Outliner |
| Spline Flow direction/facing and path drawing/editing/mirroring | Spline Flow modifier properties; contextual Scene controls |
| Embedded/bound helpers and group constraints | **Add ▾ → Helper**, right-click Shared Helpers, Constraints or a helper; select individual rows to edit; **Used By** in helper Properties |
| Child population, surrounding-guide interpolation, variation, presets, population inspection | Generate Hair and its population rows (presets, Inspect Population and Show Mask in Properties) |
| Card/atlas references, making resources unique, preview build | Hair Cards and its resource property panels |
| Shape, taper, sampling, backfaces, root embedding, vertex RGBA | Hair Cards → Geometry & Vertex Colors |
| Two material passes, shared color table, texture maps, UV-set management and alpha/checker previews | Hair Cards → Materials & UVs |
| LOD budgets, optimization, validation, issue navigation, dry run and output | Optimize & LODs; Validate & Bake |
| Wireframe, root/spline/child display, depth, avatar filters and camera focus | Hair Preview & Settings → Preview & Visibility |
| Saved settings, reset controls, save/recovery behavior | Hair Preview & Settings → Settings; existing stage/asset persistence |

## Single-navigator usability follow-up

The tree and Properties previously duplicated workflow navigation. The current UI uses one ownership rule: select and manage nodes in the Groom Outliner; edit the selected item's parameters and data in Hair Properties.

- Removed the Properties workflow shortcut row, stage toolbar, next/back buttons, child-node links, helper selector and dormant map/layer navigators.
- The Groom Outliner owns group/pass/modifier/map creation, helper creation/binding, constraint creation, duplicate/reorder/remove (right-click and **Add ▾**), and Frame/Save/Help/Exit Stage (**⋮**). Editing state (Edit This Layer, inspection previews, presets, atlas strips, form tools) belongs to the selected item's Properties. Validation's Issues menu locates settings or guides; the report and scoped repair actions stay in Properties.
- Preserved painting, map clipboard/fill/reset, guide generation and editing, sculpt brushes/gravity, UV editing, geometry/material controls, validation and baking. Density Multiplier reset is on the selected multiplier's Properties. Constraint helper binding is editable without navigating away.
- Collection nodes provide counts and guidance. Grooming and Optional Maps do not leave a hidden previous child paint-active; selecting the actual pass/map establishes the editing target.
- Review covered empty states, selection feedback, locked commands, create/duplicate selection, popup cancellation, Undo and the tree-only route to every former destination. Automated window rendering still exercises every node/modifier at narrow/wide widths. Production artist/visual sign-off remains a manual check.
- Verification: Unity 6000.3.18f1 source import and compilation, metadata preflight, and **332 tests passed with zero failures/skips** in `tmp/HairCardsSourceValidation`. No prebuilt Hair Cards DLLs were used. The new tests enforce navigation ownership and cover tree creation, ordering, removal, Undo and helpers/constraints.

## Initial node-workspace review (historical)

The complete 319-test suite passed before this usability re-review. Review method: source/UI-path audit plus Unity EditMode window-rendering and behavior tests. This is not a human usability study or an artistic sign-off on a production groom.

Findings addressed:

- Added keyboard tree navigation and Ctrl/Cmd+F search focus.
- Newly selected/created/moved nodes are revealed in the tree; filters that would conceal a new selection are cleared.
- Reselecting a node returns from Preview & Visibility to its properties.
- The two windows receive non-overlapping first-use placements and remain independently dockable/reopenable.
- Optional-map collections leave paint mode; selecting an actual map restores painting. Hidden optional maps cannot remain the paint-active node after collapsing their parent.
- Modifier properties identify their owner and link directly to that sculpt pass. Automatically created sculpt destinations update node selection immediately.
- Removed redundant inner geometry/child foldouts from their dedicated node panels.
- Corrected atlas sizing to use the full Properties panel instead of subtracting space for the removed explorer column.
- Wrapped status text for narrow docks, added missing-profile guidance, and aligned next-step links with the node workflow.
- Generated-card selection opens Materials & UVs; validation links open the relevant resource properties.
- Kept popup confirmations, scoped removal, Undo/Redo, locked-state feedback and explicit guide-generation acceptance.

## Import regression and verification correction

The original build and 327 tests below used prebuilt assemblies in an isolated project. They did **not** validate Unity source import. A 33-character GUID in `HairGroomNodeWindow.cs.meta` caused the real project's Asset Database to ignore that source and report five missing-type compile errors. The GUID is now corrected to 32 hexadecimal characters.

`Assets/UMA/HairCards/QA/Run-HairCardsReleaseGate.ps1` now checks metadata first, then requires a source-project Unity compilation and test run. A new source-import regression test checks every Hair Cards script against Unity's assembly inputs and resolves the node window's imported class. Prebuilt-DLL-only tests can no longer satisfy the full release gate. The original 327-test result is behavior/UI coverage, not proof that the project imported successfully. The gate lives under version-controlled QA rather than the ignored Build output directory.

## Independent Preview & Settings follow-up

- Moved every preview/display/avatar visibility control into the third dockable window; Properties has no Preview/Settings tabs or hidden window dependency.
- Node selection leaves the new window's tab and independent tab scroll positions unchanged. All three windows have menu/toolbar entry points and can close independently.
- Moved visibility search/foldout preferences without copying the Properties window's placement. Existing stage-owned display settings remain unchanged. Settings keeps all three reset actions, popup confirmation for card setup, and an explicit active-group label.
- Updated the user guide, README and manual docking checklist. Rendering coverage exercises Preview & Settings at 360/600 pixels alongside Nodes and Properties, both tabs, no-stage state, and closing Properties while preview controls remain open.
- Verification: Unity 6000.3.18f1 imported and compiled the actual Hair Cards sources, metadata and assembly definitions in the disposable `tmp/HairCardsSourceValidation` project, with its UMA_Core dependency also compiled from source. All **329 tests passed**, zero failures/skips; the source-import regression and new preference/window tests passed. No prebuilt Hair Cards DLLs were used. Production editor/assets were not modified by this test run.
- Separate pre-existing diagnostic observed during asset-persistence tests: `No script asset for HairAtlasProfileAsset`. The type is declared in `HairCardProfiles.cs`; verify script association and reload behavior in a separate asset-serialization follow-up. This UI change does not alter profile serialization. Automated tests passing is not proof that every editor diagnostic or production-asset concern is resolved.

## Original isolated verification

- Full post-review Unity suite: 327 tests passed, zero failures.
- Build: zero errors and warnings.
- Coverage includes typed-node identity and read-only browsing, every modifier type, selection persistence/fallback, sibling ordering and invalid drops, Undo, locks, filter ancestry, keyboard navigation, safe map selection, resource navigation and sculpt-target tracking.
- Two-window rendering exercises Nodes at 320/500 pixels and Properties at 480/1000 pixels, including Preview & Visibility and Settings. The final UI test also assigns an atlas/material to exercise the real UV canvas and material-preview controls and checks that browsing does not change groom, atlas or card-profile data.
- Tests run in the isolated HairGroomPreviewTests project. Production groom assets, user material edits and existing recovery assets were not rewritten by this task.

Before release, also perform the content-specific checks in [Hair Cards Manual QA](HairCardsManualQA.md), particularly dense hairstyles, unusual avatar poses, actual artist docking layouts, and material-specific transparency. Automated rendering validates control paths and layout execution, not subjective appearance on every display.
