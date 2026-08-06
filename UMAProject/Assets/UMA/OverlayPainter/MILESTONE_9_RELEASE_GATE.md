# Milestone 9 — QA and Release Engineering

Milestone 9 converts Overlay Painter from a feature-complete editor into a release-gated system. The implementation adds production-shader reference tests, integration round trips, scale/lifecycle coverage, and one command that produces CI-readable evidence.

## Implemented release gates

### GPU reference images

`TexturePaintGpuGoldenTests` drives `PaintingEngine` with the shipped compute shaders. It compares complete floating-point output images with independent reference implementations for:

- Paint with Normal, Multiply, Add, Subtract, Screen, and Overlay blending.
- Erase, Blur, Smear, Clone, Dodge, Burn, and Normal Touchup.
- RGB falloff and source-over alpha, including hardness, flow, strength, and source alpha.
- Multi-slot strokes backed by separate texture sets.
- Continuous-path batching, bounded dispatch count, dirty-pixel work, and preview latency.

The comparison uses maximum and mean tolerances so it remains portable across supported GPUs while still catching a one-texel seam, an alpha regression, a wrong kernel, or a blend equation change. A failure writes expected, actual, and amplified-difference PNGs under `Temp/TexturePaintGoldenFailures`.

### Integration and round trips

EditMode integration tests exercise real asset and GPU paths:

- Ordered layer composition with visibility, layer opacity, per-channel opacity, and source-over alpha.
- Document save/reopen and restore of base pixels, layer pixels, masks, spline nodes/controls, active layer data, and plugin v2 provenance.
- 8-bit PNG, 16-bit PNG, and half-float EXR export/import precision.
- Custom semantic packed-map export and component inversion.
- Stable surface identities for multiple slots sharing a material and slots using separate materials.
- Transactional cancellation and UMA overlay/material/reference generation from the existing exporter suite.

Exported assets are imported uncompressed, without NPOT rescaling, and with source alpha retained. The Unity asset therefore reflects the bit depth and channel data promised by the export template.

### Runtime quality matrix

The runtime suite now includes:

- A 10,001-event long-stroke test with cumulative spacing and an interactive sampling budget.
- Sharp cubic curves with gap and direction/orientation assertions.
- Mirrored/overlapping UV disambiguation through preferred triangles.
- Real 1K, 2K, and 4K target allocation and release.
- ARGB32, ARGBHalf, and ARGBFloat precision checks.
- Repeated lifecycle baseline checks and sparse undo-memory pruning.
- URP, HDRP, and UMA keyword-to-logical-channel routing cases, including compiled physical packing descriptors.
- Cancellation after a plugin has queued work but before commit; no layer or undo record may escape the cancelled transaction.

Earlier milestone tests remain part of the gate for cross-slot footprint discovery, different UV densities, UV islands, layer ordering, path settings, packed-map behavior, map cache reference counting, dirty synchronization, coverage budgets, normal-vector correctness, and plugin isolation.

## Running the gate

Use `QA/Run-TexturePaintReleaseGate.cmd` (or its underlying PowerShell script). It performs preflight and launches EditMode and PlayMode tests in separate Unity processes. This gives each suite a clean assembly/domain state and prevents an already-open Overlay Painter stage from hiding lifecycle failures.

The script writes NUnit XML, Unity logs, preflight JSON, and combined JSON/Markdown summaries to `Logs/TexturePaintReleaseGate`, and returns non-zero on any failure or zero-test run. See [Release Gate](QA/RELEASE_GATE.md).

The dockable preflight window is available at **Window > UMA > Overlay Painter > Release Gate**. It validates Unity 6.3+, compute and precision formats, 4K support, all production shaders/kernels, required release assets, installed render pipelines, and the v2-only plugin boundary.

## Blocking policy

A release candidate requires:

1. Preflight with zero failures.
2. EditMode and PlayMode suites with zero failures and non-zero test counts.
3. GPU goldens executed on a compute-capable release machine; skipped GPU tests are not sufficient release evidence.
4. No unexplained new Unity warnings or owned texture/render-texture deltas.
5. Preserved Plugin API v2 isolation—no v1 compatibility surface and no direct plugin access to mutable store/UI objects.

URP and HDRP are the certified material pipelines and each must pass in its corresponding release-matrix project. Built-in/Standard is informational and best-effort only; it cannot satisfy pipeline certification.
