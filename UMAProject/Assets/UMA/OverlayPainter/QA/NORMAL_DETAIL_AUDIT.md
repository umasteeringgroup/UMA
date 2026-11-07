# Generator normal-detail audit

Reviewed on 2026-10-05 in `C:\GitHub\UMA\UMAProject`, Unity 6000.3.18f1,
installed UMASettings **UMA NextGen 3.1f1**.

## Findings and changes

| Generator | Finding | Result |
| --- | --- | --- |
| Edge Wear | Read normals, but spread ignored them; GPU regeneration read the whole stack. | Normal-derived convex edges participate in detection and spread; GPU input stops below the destination layer. |
| Dirtify | Same spread and GPU boundary issues as Edge Wear. | Normal-derived cavities guide gap dirt and spread on CPU and GPU. |
| Agify | Already read underlying normals, but used a fixed single-texel radius and could wrap across texture edges. | Shared guarded curvature sampling, adjustable radius, wider influence range. |
| Dripping Corrosion | Read AO but only used mesh curvature for edges and valleys. | Combined lower normals seed corrosion, spread, and drip tracing on CPU and GPU. |
| Fabric Fuzz / Fiber / Fray | Fraying only considered mesh curvature. | Underlying normal and Normal Control features guide edge fraying. |
| Combat Scratches & Dents | Edge concentration and cavity grime only considered mesh maps. | Normal-derived exposed edges and cavities, plus underlying AO, affect weathering. |
| Rust / Oxidation / Corrosion | Fixed in the preceding change. | Uses the shared normal sampler and is included in regression coverage. |

The normal snapshot includes lower-layer Normal Control through the existing slope conversion
and normal combination. Temporary height composition now preserves the height channel's precision
even when the destination Normal texture is 8-bit. GPU generators capture separate temporary
normal/AO inputs without changing visibility, material bindings, or the user's layer settings.
Snapshots exclude the destination layer and everything above it, including on repeated regeneration.

Normal Detail Radius uses pixels at a reference resolution of 2048, independently of procedural
texture frequency. Sampling rejects out-of-bounds and unrelated UV-island neighbors. Normal Detail
Influence can be set to zero for mesh-only curvature. Existing saved influence values are retained;
new defaults use influence 8 and radius 4. Existing layers need regeneration after upgrading or
after changes below them. A 128-pixel draft cannot reliably show fine lettering/bevels.

## Other plugin families reviewed

Cloth weave and its thread-aware wear use the generated weave structure; quilt/atlas construction,
stubble, skin/veins, creature scales, surface micro-detail, and noise generate their own patterns.
Their placement is not defined as detection of pre-existing cavities or convex edges, so no new
normal-driven distribution was introduced. Normal outputs and Normal Control continue through the
shared layer compositor. Scar, text, tattoo, stitching, and garment path generators use path or guide
geometry; color, channel, blur, and morphology filters use their selected input channels and masks.
They likewise do not need implicit normal-curvature input for their existing operations.

## Regression coverage

`TexturePaintWeatheringNormalTests` covers all seven generators with both a normal-map input and
a Normal Control input on a flat mesh. Edge Wear, Dirtify, and Dripping Corrosion also run on GPU.
Checks require visible response, deterministic regeneration, exclusion of higher layers, respect for
lower-layer opacity, zero-influence behavior, and normal-derived spreading. A separate boundary
case rejects false curvature from the opposite texture edge or another UV island.

Validation: clean compilation; **21 weathering normal tests**, **24 plugin preview tests**,
**73 color-correctness tests**, and **10 Normal Control tests** passed without skips. The runtime
Plugin API suite passed 86 of 87 initially; its obsolete no-input assertion was updated to require
Normal for fabric fuzz and Normal/AO for rust and combat damage. That updated case passed on its
targeted rerun, covering all **87 runtime API cases**. No product behavior was bypassed to satisfy
the old assertion.

The user's saved pants document is not modified by this audit. Tests use isolated synthetic surfaces.
