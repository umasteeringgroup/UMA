# Testing and Release Utilities

UMA's Testing menu combines targeted content checks, the Unity Test Runner, performance baselines, and package-boundary validation.

## Race Smoke Test

Open `UMA > Testing > Race Smoke Test...`. Select a `RaceData` for a focused check, or enable
`Test All Indexed Races` and run the complete UMA Asset Index. The all-races run is sorted by race
name, labels every result with its race, continues after individual failures, and can be cancelled
between races.

The default smoke test validates:

- RaceData structure.
- Global Library lookup and duplicate-name resolution.
- Base recipe slots, slot mesh data, overlays, material references, and channel-array consistency.
- A temporary generated avatar, including skeleton, renderers, mesh vertices, triangle topology, bones, and bindposes.

The report separates errors, warnings, and passes. `Copy Results` copies a shareable report; `Log Results` writes it to the Console.

This is a smoke test, not a visual regression test. It cannot detect poor skinning, texture seams, clipping, or incorrect artistic results.

## Run UMA Editor Tests

`UMA > Testing > Run UMA Editor Tests` runs EditMode tests with the `UMA` category through Unity's Test Runner API. Use `Open Unity Test Runner` to inspect individual results, rerun selected fixtures, or use the normal Unity test interface.

Run the full editor suite after changing serialized asset formats, recipe loading, mesh generation, editor repair utilities, or release rules.

## Generation Baseline

Open `UMA > Testing > Generation Baseline...` while using a controlled crowd-generation scene.

Capture requires Play Mode and the active UMA generator. The tool records frame time distribution, queue size, average mesh-update time, peak allocated memory, graphics-driver memory, observed avatar count, and duration. `Stop When Queue Drains` ends capture when generation work completes.

Reports include mean, median, P95, P99, and maximum frame time and can be saved or copied. Default reports are written beneath `ProfilerCaptures` unless another location is chosen.

For blendshape stress baselines, use the race named by the window and enable Load BlendShapes. Keep hardware, Unity version, scene, avatar count, generator settings, and build state consistent when comparing captures.

See [Incremental Mesh Combiner](IncrementalMeshCombiner.md) for the established baseline workflow.

## Release Asset Validation

`UMA > Testing > Release Asset Validation...` verifies the UMA3 and UMA2 package boundaries, writes structured JSON, and offers guarded move/copy/retarget actions.

See [Release Asset Validation](ReleaseAssetValidation.md) before using `Auto`, `Universal`, `Copy`, or `Delete Source`.

## Recommended Release Gate

1. Enable `Test All Indexed Races` in Race Smoke Test and run the complete indexed set.
2. Run all UMA EditMode tests.
3. Capture the applicable generation baseline and compare it with the accepted baseline.
4. Run Release Asset Validation until it passes.
5. Export UMA3 and UMA2 packages separately.
6. Import each into a clean Unity project with only its permitted dependencies.
7. Generate representative races, wardrobe combinations, materials, and render pipelines.

Automated tests reduce risk but do not replace clean-project package import or visual review.
