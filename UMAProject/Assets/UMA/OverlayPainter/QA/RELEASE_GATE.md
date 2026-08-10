# Overlay Painter Release Gate

Milestone 9 provides a repeatable release gate for Overlay Painter. It runs environment and asset preflight checks, then launches the EditMode and PlayMode suites in separate Unity processes. The separate processes validate clean assembly loading and persisted state across a domain boundary instead of relying on the state of an already-open editor.

## Run locally or in CI

From the Unity project root:

```bat
Assets\UMA\TexturePaintStage\QA\Run-TexturePaintReleaseGate.cmd
```

The wrapper works when Windows' default PowerShell execution policy blocks direct `.ps1` invocation. The underlying script reads the editor version from `ProjectSettings/ProjectVersion.txt`. Pass `-UnityPath` to select another Unity 6.3+ executable and `-OutputDirectory` to redirect artifacts. It returns a non-zero exit code if preflight fails, either suite has a failure or skip, a suite runs zero tests, or Unity exits abnormally.

Results are written to `Logs/TexturePaintReleaseGate` (the project `Temp` folder is intentionally avoided because Unity can clear it between clean-process phases):

- `preflight.json`
- `editmode-results.xml` and `playmode-results.xml`
- one Unity log per phase
- `release-gate-summary.json` and `release-gate-summary.md`

In the editor, open **Window > UMA > Overlay Painter > Release Gate** for the same preflight checks. GPU golden-image mismatches emit expected, actual, and amplified-difference PNG files to `Temp/TexturePaintGoldenFailures`.

## Blocking matrix

| Area | Blocking validation |
|---|---|
| GPU tools | Paint, erase, blur, smear, clone, dodge, burn, and normal touchup execute the production kernels and match independent reference images. |
| Blend modes | Normal, Multiply, Add, Subtract, Screen, and Overlay match reference RGB and source-over alpha. |
| Paths | Sharp Beziers remain gap-free, carry direction/orientation, batch dispatches, and survive document reopen. 2D and 3D spline domains remain exclusive; rerasterizing width/effects replaces stale pixels; orange anchor, green curve, and blue width handles retain selection after adjustment; point deletion repairs selection safely. |
| Slots and UVs | One/many selected texture sets, cross-slot footprint discovery, different UV density, islands, mirrored UVs, and overlapping UV disambiguation. Ordinary 2D strokes rasterize directly in normalized UV space and remain stable on very small or thin geometry. |
| Layers | Ordered visibility, opacity, per-channel opacity, spline content, masks, Image Adjustments, independent per-layer Normal Control Height Strength, and plugin provenance. |
| Brushes | Preset/session transfer and persistence cover shape, sources, size, flow, random rotation/size, Splatter Distance, deterministic Random Strength, Fade, Taper, and pressure composition. |
| Plugins | API v2 discovery, independent read/write declarations, bounded immutable logical-channel snapshots, write-only channel metadata, compact and float tile commands, immutable non-readable parameter textures, and requested world/normal/signed-curvature/AO/thickness/ID maps. Persistent Plugin layers must read only their logical-target composite below the stack position, cache multi-channel output, retain typed parameters/texture references, work in groups and with masks/effects, mark dependencies stale, survive a missing implementation, replace atomically, retain the previous cache on cancel/failure, and round-trip through Undo/Redo and document recovery. Signed curvature separates concave and convex geometry; Agify combines it with composed normal detail and produces geometry-clipped, strip-buffered multi-channel weathering. |
| Persistence | Lossless base/layer pixels, document identity, save/reopen, channel-specific Normal Control strength, brush/path Random Strength, state serialization, and clean-process test execution. |
| Export | 8-bit PNG, 16-bit PNG, half-float EXR, semantic packed maps, tangent-space RGB normal reconstruction, Runtime Overlay alpha generation, transactional cancellation, and asset/reference creation. Generated-character source reconstruction uses original native-resolution overlay inputs and never the generated atlas. |
| Scale | Actual 1K, 2K, and 4K target allocation/release; sparse history and coverage budgets; bounded dirty-pixel work. |
| Lifecycle | Repeated target/map/store disposal, shared tangent-map reference counting, plugin/export cancellation, no stale active-process state after export completion, and no leaked owned render textures. |
| Pipelines | URP and HDRP Lit shaders in their corresponding release-matrix projects; the compiled descriptor resolves UMA meanings, documented packed-map conventions, output encoding, and importer settings. Projects without Addressables compile with Mark Addressable isolated behind `UMA_ADDRESSABLES`. Built-in/Standard is not certified. |

Weathering plugin release validation includes Agify fractal boundaries plus Dirtify and Edge Wear
detection size/level, spread, UV-island-safe sampling, multi-octave breakup, and geometry-clipped
strip output.

Production generator/filter validation also covers all Quilt/Embroidery/Perforation/Atlas Scatter
modes and coordinated optional outputs; Text block, Font/style persistence, Custom-ribbon following,
and grayscale group-mask execution; and Stylization Kuwahara quality, RGB/luminance/palette
quantization, deterministic dithering, toon edges, cancellation, native-resolution output, and mask
compatibility.

The pipeline not active in the current matrix project may be reported as not applicable, but release certification requires separate successful URP and HDRP runs. Missing compute support is blocking for a release-machine run because GPU reference tests cannot execute; CPU fallbacks remain covered by the runtime suite.
