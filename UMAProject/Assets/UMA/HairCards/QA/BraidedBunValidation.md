# Braided bun validation

## Scope

Classic, Loose and Compact Copper use Painted Scalp populations and a Gather modifier for scalp coverage, a Bun helper for the wrapped volume and recessed tuck, and an independently editable Braid spline for the surrounding plait. The spline follows the Bun transform but can be moved/reshaped separately. Fine flyaways retain individual narrow-strip grids. There are six independently editable groups and three main shared controls.

The independent-spline update and its validation are documented in [Braid spline validation](BraidSplineValidation.md). Geometry counts below are unchanged by that update.

The saved Classic Growth / Density texture painting was preserved, not replaced with an analytic hairline. Both other variants start from that painting. The foundation links to the sweep's live Growth map: painting either Growth node edits the actual shared source. Expanding the painted area now creates candidates in that area; it is no longer limited to filtering fixed scalp panels. Optional Length/Width maps stay independent.

## Geometry measurements

| Variation | LOD0 triangles | LOD1 | LOD2 | LOD0 cards |
| --- | ---: | ---: | ---: | ---: |
| Classic | 72,084 | 48,320 | 29,918 | 1,988 |
| Loose | 73,572 | 50,810 | 32,126 | 2,000 |
| Compact Copper | 68,860 | 47,204 | 28,692 | 1,980 |

These are primary geometry counts. Hybrid rendering draws additional core/fringe passes; that repeated rendering cost is not included. All nine meshes have finite vertices, zero degenerate triangles and zero detected frame flips. Regression tests rebuild every variant/LOD and compare saved vertices, normals and triangle indices exactly. All remain below 75,000 triangles at LOD0.

Scalp clearance is 0.6 mm with no root embedding. Gather additionally conforms card facing and checks full-card penetration. The foundation uses 600 candidates and the sweep 1,000, subject to painting and spacing. LOD fractions are 1, 0.66 and 0.38; coverage compensation widens surviving scalp cards by up to 2x without moving their roots, paths or target slots. Flyaways remain 28/40/20 cards, using U 0.308-0.342 and V 0.12-0.985, approximately 1.2 mm wide tapering to 0.15 mm.

## Correctness and usability checks

- Roots and protected root prefixes remain anchored. Preserve mode retains incoming segment lengths and warns about unreachable targets; Extend To Reach permits growth explicitly.
- Independent root and arrival bend profiles keep their endpoint anchors. Frozen controls, stiffness/flexibility, root influence, modifier weight and masks remain applicable.
- Gather is source-local and invariant to the character's world transform. Mirroring uses the groom's symmetry plane and supports independent side selection.
- Connected surface routing avoids chords through the head. Continuous surface following removes visible mesh-edge channels; cached routes are invalidated when the source, target or root changes.
- Gather frame continuity prevents unintended flips near nearly normal root tangents, including after mesh resampling. This behavior is opt-in on gathered curves and inherited children; existing non-gathered styles retain their frame behavior.
- Painted roots respond to texture edits and linked map edits. Nested LOD survivors retain deterministic identities, centerlines and packed targets.
- Bun volume/tuck follow a shared Gather parent; the editable braid spline follows the Bun transform. Evaluation does not modify serialized helper geometry.
- Property rendering is exercised at 360 px and 900 px. Setup actions are Undo/Redo tested. Growth navigation opens the actual linked map. Procedural sources explain why authored-guide sculpting does not drive their population.

The IMGUI workflow remains within Hair Nodes, Hair Properties and existing scene handles. No extra EditorWindow was added. Select a Gather ring for position/orientation, endpoint radii and spacing; select its modifier for root/arrival bends, length behavior, offsets and masks. Select the Bun helper for volume/tuck placement and the Braid spline to move or reshape its surrounding plait. Mirrored duplication is a tree action rather than extra properties navigation.

## Performance

On the local validation machine, measured separately before the final LOD coverage change:

| Variation | Cold evaluation | Cached evaluation |
| --- | ---: | ---: |
| Classic | 11.25 s | 1.16 s |
| Loose | 10.94 s | 1.14 s |
| Compact Copper | 10.96 s | 1.16 s |

Final rebuild measurements including mesh creation were approximately 14.1-14.3 s for cold LOD0, 3.4-3.5 s for cached LOD1 and 2.6-2.7 s for cached LOD2. These are local editor timings, not a runtime target or cross-machine guarantee. Lower preview quality is recommended while adjusting dense setups. Moving a target invalidates its surface routes; changing only bend controls can reuse them. No jobs/threading rewrite was made.

## Gather/Bun baseline release checks

The Gather/Bun release, before the independent-spline update, was validated on 2026-09-16 using Unity 6000.3.18f1: **630 passed, 0 failed, 0 skipped**, including actual source import/compilation, editor tests and GPU rendering tests. See [Braid spline validation](BraidSplineValidation.md) for the newer release gate and sample installation.

Results: `tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/733b21c947f34f1b8e0e6d1f18c39093/editmode.xml`.

All 99 delivered C#/shader/HLSL files match the tested source copies (92 C# scripts). The full suite covers previous styles, binding, sculpting, maps, modifiers, materials and editor workflows as well as Gather/Bun. The main-project metadata preflight and scoped whitespace check passed.

The existing 157 BraidedBun files were backed up to `tmp/BraidedBunBeforeGather/BraidedBun` while Unity was closed. Native assets were updated through Unity serialization APIs, never decoded or edited as text. Sample installation preserves GUIDs and checks for intervening main-project edits before copying validated bytes.

Installation verified all 157 copied files byte-for-byte against the validated project, retained existing GUIDs and deleted nothing. SHA-256 comparison confirmed all 218 files in Pointy Swept, Curly Volume and Short Hair Part are unchanged.

## Visual review and production limits

Actual Unity URP renders were reviewed from front, both sides, rear, upper rear and three-quarter angles, plus lower LODs. Iteration corrected surface-routing channels, ring approach U-turns, frame flips and low-LOD coverage loss. Final coverage is improved and the painted temples/nape now drive actual roots.

This is not a claim of photographic parity with the reference photographs. Close-up hairlines can still show card-shaped/scalloped boundaries; density, atlas selection, root fade and painting need art direction for the intended camera distance. The samples use a full-volume tucked bun with a surrounding braid, not physical hair simulation.

Review prefabs/scene are static, unrigged authoring examples. Bind the intended avatar/race and transfer weights before shipping a wearable. Gather clearance is against the scalp, not hair-to-hair self-collision. Tight braids and crowded bundles can intersect. Hybrid transparency adds draw/overlap cost and should be tested under production lighting, sorting and target hardware. Preserve length and pinned controls can make a target unreachable; use explicit extension or adjust the design rather than expecting the helper to violate constraints.
