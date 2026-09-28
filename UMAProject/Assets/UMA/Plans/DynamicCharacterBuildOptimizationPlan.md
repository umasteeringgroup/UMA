# Dynamic Character Build Optimization Plan

## Objective

Improve the performance of the initial character build path from
`DynamicCharacterAvatar.BuildCharacter` through the end of
`DynamicCharacterAvatar.LoadCharacter`.

This work is limited to procedural improvements: eliminating repeated scans,
nested loops, repeated normalization, unnecessary allocations, and duplicate
work. It does not include mesh combining, texture generation, or other generator
work performed after `LoadCharacter` returns.

## Clean-Build Requirement

Every build must remain clean and independent.

- Start from the base race recipe and the inputs supplied for the current build.
- Create fresh merged slots, overlays, colors, DNA, modifiers, and mesh-hide state.
- Do not introduce caches of derived build data between builds.
- Temporary indexes and workspaces may be used only during the current build.
- Discard all temporary build state when `LoadCharacter` returns.
- Do not mutate source recipe assets or retain references that allow one build to
  affect another.

Existing asset-loading behavior does not need to be changed as part of this
optimization, but the plan must not expand or depend on cross-build recipe-result
caching.

## Primary Finding

The most likely general slowdown is recipe merging in `UMAData.UMARecipe`.

- `MergeSlot` scans the complete destination slot array to find an existing slot.
- The destination slot array grows through `Array.Resize` for every new slot.
- The shared-color array grows through `Array.Resize` for every new color.
- Wardrobe merging requests matching-overlay normalization.
- `MergeSlot` can consequently call `MergeMatchingOverlays` after every newly
  inserted wardrobe slot.
- `MergeMatchingOverlays` compares slot overlay lists across the complete recipe.

As the wardrobe grows, this can approach cubic behavior. Batching recipe merging
and normalizing matching overlays once is the highest-priority optimization.

## Phase 1: Measurement and Equivalence Tests

Before changing behavior, add conditional profiler markers around these stages:

1. Character initialization and wardrobe selection.
2. Base recipe unpacking.
3. Additional recipe merging.
4. Wardrobe recipe merging.
5. Cross-compatible and slot post-processing.
6. Replacement recipes.
7. Mesh-hide processing and mask generation.
8. Overlay ordering.
9. Smoosh processing.
10. DNA initialization and restoration.
11. Color processing.
12. Mesh modifiers and final dirty-state setup.

Collect at least:

- Total wall-clock time through the end of `LoadCharacter`.
- Median and high-percentile build times across repeated clean builds.
- GC allocation bytes.
- Slot, overlay, shared-color, and recipe counts.
- Calls to `Merge`, `MergeSlot`, and `MergeMatchingOverlays`.
- Slot-array and shared-color-array resize counts.
- Smoosh target, clipping-plane, and raycast counts.
- Addressables preload latency separately from CPU recipe assembly.

The current `UMA_DCA_TIMING` definition and unconditional stopwatch work should
be replaced with opt-in profiling so timing instrumentation does not become
permanent build overhead.

Create an equivalence test that compares the old and optimized final recipes:

- Slot names and order.
- Overlay names and order.
- Shared overlay references where sharing is required.
- Shared-color names, values, and property data.
- DNA types, names, and values.
- Mesh-hide masks.
- Mesh modifiers.
- Suppressed, hidden, wildcard, cross-compatible, and replaced slots.
- Build events and event ordering.
- Dirty flags and race state at the end of `LoadCharacter`.

## Phase 2: Low-Risk Cleanup

Apply and benchmark these changes independently:

- Remove the duplicate
  `SuppressSlotsStrings.AddRange(forceSuppressedWardrobeSlots)` call.
- Use temporary `HashSet<string>` collections for membership tests involving
  suppressed slots, hidden slots, hide tags, and compatible races.
- Retain ordered lists wherever ordering is observable.
- Replace `ContainsKey` followed by dictionary indexing with `TryGetValue`.
- Call `GetComponent<Animator>()` once in `LoadCharacter`.
- Run `SetUMADataOptions()` once per build instead of in both `BuildCharacter`
  and `LoadCharacter`.
- Remove the duplicate blendshape-option assignment.
- Verify whether `umaRecipe.SetRace(currentRaceData)` is redundant immediately
  after loading the base race recipe; remove it only after equivalence testing.
- Pre-size temporary lists using known wardrobe, additional-recipe, slot, and
  overlay counts.
- Remove the unused `Smooshables` allocation.
- Make smoosh bounds and editor diagnostics opt-in rather than part of the
  default build path.

## Phase 3: Batch Recipe Merging

Refactor `UMARecipe.Merge` and `UMARecipe.MergeSlot` around a temporary
per-build workspace, provisionally named `UMARecipeBuildContext`.

The workspace should contain:

- A growable, pre-sized slot list.
- Slot lookup by slot asset.
- Placeholder-slot lookup by placeholder name.
- Shared-color lookup by color name.
- Estimated final slot and shared-color capacities.
- Temporary data used to canonicalize matching overlay lists.

The batch merge should:

1. Initialize the workspace from the freshly loaded base recipe.
2. Merge additional and wardrobe recipes in their existing order.
3. Locate existing slots through the per-build index rather than a complete
   destination scan.
4. Append new slots and shared colors without resizing an array for each item.
5. Preserve existing overlay merge, shared-color, `dontSerialize`, race-filter,
   and recipe-origin semantics.
6. Defer `MergeMatchingOverlays` until all additional and wardrobe recipes have
   been merged.
7. Convert the temporary lists to the final recipe arrays once.
8. Discard the entire workspace before `LoadCharacter` returns.

Mutable slot, overlay, and color data must still be copied. Source recipe assets
must not be modified by a build.

### Overlay Matching Correctness Prerequisite

`UMARecipe.OverlayListsMatch` currently appears to use `list2[i]` inside its
inner `j` loop. This should be reviewed and tested as a separate correctness
change before optimized overlay canonicalization relies on it. Do not combine
that behavioral correction invisibly with the batching performance change.

## Phase 4: Indexed Slot-Processing Pipeline

After recipe merging, construct a temporary slot index containing:

- Slot by name.
- Slot by asset.
- Slots grouped by tag.
- Overlay base-name mappings.
- The set of slots with usable mesh data.

Use explicit mutation barriers:

1. Build the first index after merging.
2. Perform compatibility, wildcard, suppression, swap, and replacement work.
3. Build a final index after slot mutations.
4. Use the final index for mesh hides, colors, smoosh discovery, and modifiers.

This is safer than attempting to maintain one mutable index through every recipe
mutation.

Use the index to improve these methods:

### `FixCrossCompatibleSlots`

- Replace repeated equivalent-slot searches with direct slot-name lookup.
- Use a hidden-slot set for membership checks.
- Avoid removing and rebuilding the slot array more than once.

### `PostProcessSlots`

- Group swap targets by name or tag instead of scanning all slots for every swap.
- Use tag indexes for wildcard expansion.
- Combine suppression, hiding, and final slot collection into the fewest safe
  passes.
- Preserve final slot order.

### `ReplaceSlot`

- Unpack each replacement recipe once during the current build.
- Find replacement targets by indexed slot name.
- Build the overlay base-name index once for propagation.
- Apply all replacements, then perform one final slot pass.

Replacement recipes must not be retained after the build.

## Phase 5: Consolidate Wardrobe Analysis

`BuildCharacter` currently examines recipes in separate passes for suppression,
race compatibility, hiding, DNA overrides, tags, and mesh hides.

Create a temporary `RecipeBuildInfo` for every selected recipe containing:

- Race compatibility result.
- Cross-compatible race information.
- Wardrobe slot.
- Suppressed slots.
- Hidden slots and tags.
- Replacement status.
- DNA overrides.
- Mesh-hide assets.
- Additive recipe ordering information.

Compute this information once and consume it during recipe assembly. This avoids
repeated compatibility calls and repeated traversal of recipe metadata.

## Phase 6: Expensive Optional Features

### Smoosh Processing

For builds that use clipping or smoosh slots:

- Group work by smoosh target.
- Create and bake one temporary collider for each unique target during the build.
- Fetch managed vertices and triangles once per target.
- Process all relevant clipping planes against the same temporary collider.
- Precompute clipping-plane data once per plane.
- Explicitly destroy the temporary mesh, collider, and GameObject before
  `LoadCharacter` returns.

Do not turn the existing static smoosh structures into a cross-build mesh cache.

### Color Processing

- Combine the two shared-color passes while preserving `alwaysUpdate` and
  `alwaysUpdateParms` precedence.
- Skip the overlay-property scan when no overlay has property data.
- Avoid repeated calls to retrieve the same overlay list.
- Preserve character-color override behavior exactly.

### Mesh Modifiers

- Use `TryGetValue` for modifier and DNA lookup.
- Iterate modifiers associated with present mesh slots instead of scanning every
  slot when the modifier set is small.
- Preserve replacement-recipe and new-DNA modifier ordering.

### Mesh Hides

- Deduplicate mesh-hide assets using temporary per-slot sets.
- Materialize the existing lists only after collection completes.
- Audit the repeated `UpdateMeshHideMasks` call at mesh-combiner startup.
- Establish one owner for initial mask generation. Prefer keeping it in
  `LoadCharacter` if masks are part of that method's completion contract.

## Phase 7: Addressables Entry Path

Measure Addressables preload latency separately because asset I/O may dominate
the elapsed time but is not equivalent to CPU recipe-processing cost.

Consider splitting `LoadCharacter` into a lightweight preload wrapper and a
`LoadCharacterCore` method. After preload completion, call the core directly so
entry checks, argument packaging, and setup are not repeated.

Do not retain merged recipes or build products between Addressables builds.

## Validation Matrix

Run equivalence and performance tests for:

- Base race with no wardrobe.
- Small and large wardrobes.
- Additive recipes.
- Cross-compatible recipes.
- Suppressed and force-suppressed wardrobe slots.
- Wildcard, placeholder, and swap slots.
- Replacement recipes.
- Mesh-hide assets and multiple LODs.
- Shared colors and overlay transform properties.
- Clipping and smoosh slots.
- Legacy DNA and the new DNA system.
- DNA restoration on and off.
- FBX route builds.
- Addressables builds.
- Editor and player builds.

For benchmarks, use a fresh avatar or explicitly reset all generated build state
between samples. A faster result is acceptable only when the final recipe and
observable events remain equivalent.

## Recommended Implementation Order

1. Add profiling and recipe-equivalence tests.
2. Apply low-risk cleanup.
3. Implement the batch recipe merge workspace.
4. Implement the indexed slot-processing pipeline.
5. Consolidate wardrobe analysis.
6. Optimize smoosh, colors, mesh hides, and modifiers.
7. Clean up the Addressables entry path.

Keep each phase in a separate change and benchmark it independently. Retain a
change only when it produces a measurable clean-build improvement without an
unexpected recipe or event difference.

## Expected Priority

1. **Highest expected impact:** Batch recipe merging and defer matching-overlay
   normalization.
2. **High expected impact:** Replace repeated slot scans with temporary per-build
   indexes.
3. **High impact for affected characters:** Reuse smoosh collider setup within a
   single build.
4. **Moderate expected impact:** Consolidate wardrobe analysis and post-processing.
5. **Smaller cumulative impact:** Remove duplicate setup, dictionary lookups,
   avoidable allocations, and permanent timing overhead.
