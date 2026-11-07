# Eyebrow validation — UMA 3.1f3

Validated in `C:/GitHub/UMA/UMAProject`, using the installed UMASettings asset (UMA **3.1f3**) and **Unity 6000.3.18f1**. Hardware: Intel Core i7-12700K and NVIDIA GeForce RTX 5070 Ti. These results describe this checkout and machine, not minimum hardware guarantees.

## Working examples

The two editable documents in [Projects](../Projects) target `UMA30_Eyebrows_slot`, using `HDEyeBrows_Thin_Wardrobe` as the slot/material reference:

- [Groomed project](../Projects/HDEyebrows%20Groomed.asset), [transparent Albedo](../Projects/HDEyebrows%20Groomed%20Albedo.png), [equipped UMA capture](../Projects/HDEyebrows%20Groomed%20on%20UMA.png).
- [Bushy project](../Projects/HDEyebrows%20Bushy.asset), [transparent Albedo](../Projects/HDEyebrows%20Bushy%20Albedo.png), [equipped UMA capture](../Projects/HDEyebrows%20Bushy%20on%20UMA.png).

Both documents were saved, restored onto the equipped shell of a built **Human Female 3.0** character, and rendered using the restored layer pixels. Each has two editable UV paths with independent seeds. They use the shell's native **2048 × 512** texture dimensions. The shell's reference UMA material declares only Albedo: these examples demonstrate transparency and layered color, not PBR relief on that material. The wardrobe recipe also supports Human Male 3.0; that race has not received separate visual validation here.

Natural, Arched, and Bushy face examples were also generated through the production curved ribbon renderer at **4096 × 4096** destination resolution. See the `*-Face.png`, `*-Closeup.png`, and `*-Transparent-Albedo.png` files alongside this report. Their final measured path footprint was approximately **715 pixels**. Natural/Arched contain 682 strands, Bushy 994. These are actual UMA renders, not concept images. Closeups intentionally magnify the final texture and may crop the outermost tails; the face images show the full placement.

## Automated checks

[Tests.txt](Tests.txt) records **101 passing cases, zero failures**: eyebrow generation and rendering, existing path-generator/editor regressions, transparent overlay export, and document persistence. NUnit test methods were invoked in the live Editor with setup/teardown where applicable; this was a focused run, not the entire project's Unity Test Runner suite. Compilation completed without errors; the ribbon shader reported no errors.

Coverage includes deterministic geometry, independent appearance streams, depth order, density identity retention, weighted curve serialization, migration, missing generators, undo/redo, in-place curve invalidation, mirrored variants, direction reversal, resolution independence, shared channel alpha, transparency-preserving mipmaps, cancellation/disposal, failure preserving previous output, arc-length sampling, and standalone-to-equipped document restoration after vertex reordering. Existing export checks verify transparent authored overlays and alphaMask assignment, Normal Control export, and RGB padding without alpha expansion.

## Measured preparation performance

[Performance.txt](Performance.txt) contains raw timings. Each cold run cleared the generator caches; each warm run immediately repeated the same preparation. This is one sample per condition, not a statistical benchmark. Timings include groom generation, coverage, shading and texture upload; they exclude the subsequent host ribbon draw and file export.

The benchmark used the Natural preset, **682 strands**, a 0.009 × 0.045 local path metric, and a representative face footprint of approximately **609 pixels at 4K**. This footprint differs from the final face capture above after character rebuilding. Destination resolution increases sampling while retaining the groom.

| Destination | Quality | Local raster | Cold | Warm |
| --- | --- | --- | --- | --- |
| 1K | Draft | 172 × 36 | 606 ms | 36 ms |
| 1K | Final | 344 × 72 | 701 ms | 47 ms |
| 2K | Draft | 344 × 72 | 734 ms | 43 ms |
| 2K | Final | 687 × 143 | 975 ms | 105 ms |
| 4K | Draft | 384 × 80 | 675 ms | 44 ms |
| 4K | Final | 1374 × 286 | 1397 ms | 285 ms |

## Remaining limits

Follow-up: UV paths now display their full curve and all point handles in Scene view without requiring a 2D point selection. [ScenePathTests.txt](ScenePathTests.txt) records two passing focused checks for surface projection before selection and UV-preserving edits; [ScenePathEnvironment.txt](ScenePathEnvironment.txt) records the installed project versions. Compilation passed. These checks exercise geometry/editing, not a simulated mouse-drag UI test.

The captures demonstrate readable hair gaps, head/body/tail growth, fine taper, curved placement and visibly different presets. They do **not** establish universal AAA acceptance. Very close views of the shell remain limited by its texture footprint, transparency sorting and the character's material/lighting. Artist review on the shipping face, camera distance and lighting is still needed. Validate other races and extreme DNA shapes separately.

Generation is synchronous and cold high-resolution preparation can visibly pause the Editor. Hair geometry uses fixed 48-step cubic subdivision followed by arc-length resampling, rather than adaptive geometric subdivision. Coincident mirrored UV triangles are detected; arbitrary partial UV overlap is not exhaustively detected. Independent left/right shape requires two paths. Flow/strand-ID/root-to-tip views are diagnostic Albedo exports, not registered material-ready directional channels. Limits cap total hairs, raster area and coverage fragments; closed or degenerate paths preserve the last valid output and report an error.

See [EyebrowPathGenerator.md](../../../OverlayPainter/Documentation/EyebrowPathGenerator.md) for coordinate definitions, controls and export instructions.
