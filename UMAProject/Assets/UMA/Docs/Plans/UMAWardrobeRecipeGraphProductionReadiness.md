# UMA Wardrobe Recipe Graph Editor Production Readiness Plan

## Status

Deferred until the project is opened under Unity 6.4 or newer.

This review was performed against the source while the project was using Unity 6000.3.18. The graph editor is wrapped in `UNITY_6000_4_OR_NEWER`, so it was excluded from the current compilation and could only be reviewed statically. Production readiness must be verified in a Unity 6.4 editor and CI environment.

Primary implementation file:

- `Assets/UMA/Core/Editor/Scripts/UMAWardrobeRecipeGraphEditorWindow.cs`

Standard editor used for comparison:

- `Assets/UMA/Core/Editor/Extensions/DynamicCharacterSystem/UMAWardrobeRecipeEditor.cs`
- `Assets/UMA/Core/Editor/Extensions/DynamicCharacterSystem/pWardrobeRecipeEditor.cs`
- `Assets/UMA/Core/Editor/Scripts/UMAWardrobeRecipeEditor.CopyValuesExtension.cs`

## Current Coverage

The graph editor already provides substantial functionality:

- Slot, overlay, shared-color, output, and note nodes.
- Editable overlay-to-slot and shared-color-to-overlay connections.
- Slot and overlay detail editing through the existing `SlotEditor` and `OverlayEditor`.
- Compatible races and per-race thumbnails.
- Wardrobe region, suppressed regions, hidden base slots, replaced slots, and incompatible recipes.
- Shared colors, recipe DNA, and override DNA.
- Hide tags, mesh hide assets, mesh hide collections, and mesh modifiers through serialized fields.
- Drag and drop for slots, overlays, races, recipes, and folders.
- Save, Save As, Refresh, Auto Save, and graph layout storage.
- An embedded Legacy Inspector tab that provides access to the standard editor.

The editor is a strong prototype, but it should remain preview/experimental until the release blockers below are resolved.

## P0: Release Blockers

### 1. Compile and Test Under Unity 6.4

The current Unity 6000.3 project does not compile the graph editor.

Tasks:

- Open a dedicated branch or test project under the targeted Unity 6.4 version.
- Confirm the `Unity.GraphToolkit.Editor` API used by the graph declarations is available and stable.
- Add the Unity 6.4 editor to CI so the file is always compiled.
- Determine whether the four Graph Toolkit node declarations are intended to back a real Graph Toolkit graph. The current window draws its graph manually with IMGUI and does not otherwise use those declarations.
- Either adopt Graph Toolkit for the actual graph model/view or remove the unused dependency and version gate.

Acceptance criteria:

- The graph editor compiles with zero errors under the minimum supported Unity version.
- Its context menu opens for a selected `UMAWardrobeRecipe`.
- CI compiles and runs its editor tests.

### 2. Make Save, Discard, Undo, and Redo Transaction-Safe

The editor modifies a detached `UMAData.UMARecipe` while Undo records the `UMAWardrobeRecipe` asset. This is unreliable when Auto Save is disabled. Refreshing, changing the selected recipe, or closing the window can also discard the detached recipe without prompting.

Tasks:

- Introduce a single edit transaction API used by every graph and inspector mutation.
- Register Undo before any asset or recipe mutation.
- Ensure a recipe edit updates an Undo-tracked serialized state during the same Undo group, even when disk Auto Save is disabled.
- Subscribe to `Undo.undoRedoPerformed` and reload the working recipe after Undo or Redo.
- Detect external asset changes and avoid overwriting a newer standard-inspector edit with stale graph data.
- Use Unity's unsaved-changes window support where available, or provide equivalent Save/Discard/Cancel prompts when:
  - Refresh is clicked.
  - A different recipe is selected.
  - The window closes or reloads.
  - The Legacy Inspector requests a reload.
- Clearly distinguish "asset changed in memory" from "asset written to disk" if Auto Save remains configurable.

Acceptance criteria:

- Every graph operation can be undone and redone.
- Undo immediately refreshes the graph and inspector.
- Auto Save on and off produce equivalent Undo history.
- No pending edit is silently lost when refreshing, switching assets, recompiling, or closing the window.
- Editing in the Legacy Inspector cannot discard pending native graph edits.

### 3. Correct Overlay Connection Semantics

Connecting an overlay that is already used by a slot currently assigns the entire source overlay list to the target slot. This can silently replace the target slot's existing overlays. Cross-slot reconnection similarly replaces the target list and does not honor the requested insertion index.

Tasks:

- Define separate operations for:
  - Move one overlay.
  - Copy or share one overlay.
  - Share an entire overlay stack.
  - Replace an existing target stack.
- Do not overwrite a non-empty target stack without an explicit confirmation.
- Preserve displaced overlays as detached nodes or leave the operation unchanged when canceled.
- Make connection labels or context actions describe whether an overlay or a complete shared stack is being connected.
- Ensure moving an overlay within or between slots honors the selected port index.
- Handle slots that already share the same overlay list without mutating both unexpectedly.

Acceptance criteria:

- Connecting one overlay never removes unrelated target overlays.
- Sharing a complete stack is explicit and visually identifiable.
- Canceling or dropping an invalid connection is non-destructive.
- Move, copy, share, disconnect, and delete behavior is covered by tests for both independent and shared stacks.

### 4. Handle Missing and Corrupt Recipe Data Safely

The graph can display warnings for missing assets, but selecting those nodes may instantiate `SlotEditor` or `OverlayEditor`, whose constructors dereference the missing asset. Null slots and overlays can also be skipped, leaving no native repair path.

Tasks:

- Never construct `SlotEditor` or `OverlayEditor` for an invalid node.
- Provide a safe missing-reference inspector with Replace, Remove, and Locate actions.
- Represent null slot and overlay entries as explicit error nodes or validation entries.
- Add native `Remove Nulls` and repair commands.
- Catch inspector drawing failures at the node level so one corrupt entry does not break the complete window.

Acceptance criteria:

- Recipes with missing, unindexed, null, or material-less assets always open.
- Every invalid entry can be repaired or removed without using the Legacy Inspector.
- No invalid node selection throws an exception.

## P1: Native Feature Parity

The Legacy Inspector currently provides fallback access to these features. They should be integrated into the native Recipe tab before the graph replaces the standard editor.

### Thumbnail Generation

Add native controls for:

- `thumbnailFromTexture`
- `thumbnailRect`
- A useful preview of the generated thumbnail crop when practical.

### Copy Values

Port "Copy Values from another Wardrobe Recipe" into the graph Recipe tab, including the existing selective options for races, wardrobe region, hidden slots, replaced slot, and suppressed regions.

### Recipe Plugins and Addressables

- Initialize and draw `IUMARecipePlugin` sections in the graph editor.
- Forward plugin lifecycle methods correctly.
- Expose the standard addressables fields and actions when `UMA_ADDRESSABLES` is enabled:
  - Alternate label.
  - Resources Only.
  - Force Keep.
  - Label Local Files.
- Preserve the addressables rebuild prompts used by the standard editor.

### Race-Aware Wardrobe and Base-Slot Choices

The graph currently unions the wardrobe regions from all valid compatible races. It should retain the standard editor's compatibility awareness.

Tasks:

- Identify regions supported by all compatible races versus only some races.
- Warn or block when a selected wardrobe region is unsupported by one or more compatible races.
- Port baked-race and cross-compatible base-slot resolution for Hides and Replaces.
- Preserve valid existing values even when a race is temporarily missing, but display a clear warning.
- Show which race contributes each region or base slot when compatibility is partial.

### Mesh Modifications

Replace or supplement the generic serialized arrays with the standard workflow:

- Add Mesh Hide Asset.
- Add Mesh Hide Asset Collection.
- Add Mesh Modifier.
- Drag and drop assets and collections.
- Duplicate prevention.
- Ping and Inspect actions.
- Slot-target diagnostics.
- Safe removal of null entries.

### Recipe Cleanup and Bulk Actions

Add native equivalents for:

- Clear Recipe, with confirmation.
- Remove Nulls.
- Select all slots.
- Select all overlays.
- Bulk delete where safe.
- Optional duplicate node/slot detection.

## P1: Validation

Add a recipe-level validation panel with errors, warnings, and click-to-select actions.

At minimum, validate:

- No compatible races.
- Missing or duplicate compatible races.
- Wardrobe region is `None` or unsupported by a compatible race.
- No slots in the recipe.
- Null slot or overlay entries.
- Missing or unindexed slot and overlay assets.
- Overlay without a UMA material.
- Placeholder slot without tags.
- Invalid Hides, Replaces, or suppressed wardrobe-region values.
- Incompatible recipe nulls, duplicates, or self-reference.
- Shared colors with null entries, duplicate names, or invalid channel data.
- Mesh hide assets that do not target a recipe slot.
- Missing race thumbnails where the project expects them.

Saving should remain possible for warnings. Blocking structural errors should require an explicit "Save Anyway" action if saving them is necessary for repair workflows.

## P2: Performance and Maintainability

### Rebuild Only When Dirty

`BuildGraph()` currently runs during every `OnGUI` event and is also called by many mutation and save paths.

Tasks:

- Introduce graph-data and layout dirty flags.
- Rebuild nodes and edges only when recipe structure changes.
- Repaint without rebuilding for selection, hover, pan, and zoom.
- Avoid repeated asset-index lookups during repaint.

### Cache Detail Editors

Cache `SlotEditor` and `OverlayEditor` instances by stable node identity instead of constructing them during every inspector draw. Invalidate the cache when the backing slot, overlay, or recipe is reloaded.

### Improve Graph Navigation

- Make Frame calculate the bounds of all visible nodes and zoom to fit.
- Add Frame Selected.
- Add an Auto Layout or Reset Layout command.
- Consider keyboard shortcuts for Delete, Duplicate, Frame, Save, and node search.
- Show a legend explaining connection colors and shared-stack behavior.

### Stabilize and Share Layout Data

Layouts and notes currently live in `EditorPrefs`, making them local to one workstation and unavailable to source control.

Choose and document one model:

- Personal layout: keep `EditorPrefs`, label notes as personal, and clean stale keys.
- Shared layout: store graph metadata in a separate editor-only asset or subasset that can be committed without changing runtime recipe data.

Stable node identifiers should not depend solely on slot/overlay indexes and names, because reordering can invalidate saved positions.

### Split the Editor into Testable Components

The implementation is currently one large editor file. Suggested boundaries:

- Recipe edit transaction and save controller.
- Graph model builder.
- Overlay connection operations.
- Recipe validator.
- Layout repository.
- Graph canvas view.
- Recipe and selection inspectors.

Connection and mutation operations should be plain testable methods rather than being coupled directly to IMGUI events.

## Unity 6.4 Test Matrix

### Serialization and Round Trips

- Open and save an unchanged recipe; serialized recipe content remains equivalent.
- Perform each graph operation, save, reload, and compare against the expected `UMAData.UMARecipe`.
- Save As preserves both packed recipe data and all `UMAWardrobeRecipe` fields.
- Native and Legacy Inspector edits round-trip without overwriting each other.

### Undo and Lifecycle

- Undo and redo every add, remove, reorder, connect, disconnect, metadata, DNA, and shared-color operation.
- Repeat with Auto Save enabled and disabled.
- Test Refresh, recipe switching, window close, script reload, and Unity restart with pending changes.
- Test external edits made by the standard inspector while the graph is open.

### Overlay Connections

- Move an overlay within one slot.
- Move an overlay between slots.
- Share one overlay.
- Share a complete overlay stack.
- Connect to empty and non-empty target slots.
- Disconnect from independent and shared stacks.
- Delete an overlay used once and used by multiple slots.
- Cancel every operation and confirm that no data changes.

### Invalid Data

- Null slot.
- Slot with missing asset.
- Null overlay.
- Overlay with missing asset.
- Overlay without material.
- Unindexed race, slot, and overlay.
- Null and duplicate shared colors.
- Invalid placeholder slot.
- Invalid mesh hide and modifier references.

### Race Compatibility

- One compatible race.
- Multiple races with identical wardrobe regions.
- Multiple races with only partially overlapping regions.
- Missing compatible race assets.
- Cross-compatible and baked races.
- Hides and Replaces values that require baked-slot resolution.

### Scale and Responsiveness

- Small recipe.
- Large recipe with many slots, overlays, and shared colors.
- Shared overlay stacks used by many slots.
- Profile allocations during repaint, pan, zoom, selection, and inspector editing.

## Suggested Implementation Order

1. Establish Unity 6.4 compilation and CI.
2. Add edit transactions, Undo/Redo synchronization, and unsaved-change prompts.
3. Correct and test overlay connection semantics.
4. Add safe handling for corrupt and missing data.
5. Add validation and native cleanup actions.
6. Port race-aware slot logic and missing standard-editor fields.
7. Integrate plugins, addressables, copy-values, and mesh-modification workflows.
8. Optimize graph rebuilding and editor caching.
9. Improve graph navigation and layout persistence.
10. Remove the experimental label only after the complete test matrix passes.

