# Braided Bun: Gather, Bun and Braid Spline Helpers

Open **UMA > Hair Cards > Examples > Braided Bun** and choose Classic, Loose or Compact Copper.

1. Open a `*_HairGroom.asset`. Select **02 - Gathered sweep > Growth / Density** to paint the hairline. Roots now generate throughout the painted region, not just on fixed panels. The foundation shares this painting.
2. Select **Shared Helpers > Gather ring - scalp to bun** to position the tie. Rotate its Y arrow to change the approach direction.
3. Select **Gather to bun - dual bend** on the sweep population. Expand **Root & arrival bends (S-curve)** for separate root and arrival profiles/heights. Samples explicitly use **Extend To Reach**; Preserve mode keeps incoming segment lengths.
4. Select **Bun volume, tuck and braid** to shape the wrapped volume and tuck. It follows the gather ring.
5. Select **Braid spline - bun surround** under Shared Helpers. Drag the cyan spline or its move gizmo to move only the braid; click a dot to reshape it. The spline follows the bun but is independently editable.
6. Save; use **Validate & Bake** for output. Use **Exit** on the floating toolbar to leave grooming.

Six groups separate scalp foundation, gathered sweep, wrapped bun, recessed tuck, surrounding braid and thin flyaways. Classic has 28 wisps; Loose is larger/softer with 40; Compact Copper is smaller with 20. Flyaways use a narrow atlas strip and remain individually editable Wisp grids.

For braids elsewhere, use **+ Braid Group** in the tree. Braid spline properties support freeform editing, snapping the spine to a surface at an offset, and independent root/tip attachment to scalp or helpers. **Follow helper** parents the spline without moving its current shape. Bind scene objects as attachment helpers under Shared Helpers when needed. Full instructions are in the guide below.

The saved Classic texture-backed hairline was retained and used as the starting boundary for all three variants. Each variant owns its subsequent painting, profiles and materials. Optional styling maps are independent from the shared Growth map within a groom.

`BraidedBun_URP_Review.unity` provides neutral lighting; enable one variant at a time. Preview prefabs contain three generated LODs. These are static examples: bind an avatar/race and transfer weights before baking a wearable.

LOD0 geometry is 72,084 / 73,572 / 68,860 triangles for Classic / Loose / Compact. Lower preview quality reduces editing cost. First-time surface routing is substantially slower than cached reevaluation. Other example folders are unchanged.

Full instructions: `Assets/UMA/HairCards/BraidedBunGuide.md`.
Measurements, tests and limitations: `Assets/UMA/HairCards/QA/BraidedBunValidation.md`.
