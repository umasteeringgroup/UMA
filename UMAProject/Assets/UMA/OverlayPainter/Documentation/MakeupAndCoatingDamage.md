# Makeup, face paint and coating damage

Requires Unity 6.3 or newer. These additions ship in the main **Overlay Painter** plugin. The **Examples** and **Tests** companions remain separate packages.

## Start with a finished look

1. Open Overlay Painter from a built DynamicCharacterAvatar. Select its skin/head paint target.
2. Open the Material Preset browser and choose **Face / Finished Looks**. Alternatively select a preset in `Assets/UMA/OverlayPainter/Presets/Face and Coatings` and use its apply workflow.
3. Apply Natural Rose, Evening Berry, Bronze Editorial, Gothic Velvet, War Paint, or Festival Color. Each cosmetic is a separate editable plugin layer. Your original skin remains underneath.
4. Select a cosmetic layer and adjust its boundaries to the character. Use the layer mask for precise local corrections; select Character left or Character right for one-sided makeup.
5. Regenerate after parameter edits. Existing layer previews, history, saved documents, duplication, recovery, and transparent overlay export use the normal Overlay Painter workflows.

The face presets are layer stacks, not flattened textures. Applying one does not modify the preset asset. To keep a variation, save a new Material Preset using the [material preset workflow](OverlayPainter%20-%20MaterialPresets.MD).

## Makeup & Face Paint

Choose **+Plugin > Generators > Face — Makeup & Face Paint** for an individual layer. The stable generator ID is `com.uma.texturepaint.makeup-face-paint`.

| Construction | Placement and controls |
| --- | --- |
| Lipstick | Lip landmarks, Cupid bow, opening protection, width, height, outward/vertical offset and tilt. |
| Eyeliner | Upper lid arc, lower-lid contribution, thickness and a tapered outer wing. |
| Eyeshadow | Soft pigment above the eyelid arc, adjustable reach and secondary-color gradient. |
| Blush | Soft cheek envelopes, with width, height, position and tilt. |
| War paint | Mirrored cheek stripes with adjustable placement and boundaries. |
| Masquerade | Colored eye surrounds with transparent eye openings. |
| Festival stripes | Diagonal cheek accents with two-color variation. |

**Coordinates.** The posed Head, LeftEye and RightEye landmarks establish the face frame. All distances are fractions of the distance between the eyes. Outward Offset moves both sides away from the nose; Vertical Offset follows the head's up axis. Rotation is an outward tilt in degrees. Width/Height scale the selected cosmetic around its feature center. This is independent of UV island orientation and avatar position, rotation and uniform scale.

Lip placement also uses UMA's `LipsSuperior`, `LipsInferior`, `LeftLips` and `RightLips` bones when present. If lip landmarks are absent, placement falls back to a proportion of eye distance. Missing head or eye landmarks produce an actionable error rather than arbitrary UV placement. The generator receives a detached anatomy snapshot; worker threads do not access live character transforms.

**Boundaries and symmetry.** Both (mirrored) uses matching placement and pigment noise. Bounded Asymmetry offsets the second side and allows a different pigment field; Seed changes the pigment variation. Side refers to the character, not the viewer. Use independent layers for fully different cosmetics on each side. Shared/overlapping destination UVs cannot contain different left/right pixels. Back-facing and out-of-face samples are rejected.

These are anatomical starting envelopes, not automatic segmentation of the visible lip or eyelid. DNA, expressions, unusual proportions and custom rigs can require offsets, boundary sizing and painted-mask refinement. Capture in a relaxed expression for reusable cosmetics. Strong expressions can move landmarks independently; validate the resulting overlay on the intended character. A standalone slot needs the relevant bone landmarks in its reconstructed anatomy.

**Coverage.** Boundary Softness antialiases the silhouette; Opacity scales the entire cosmetic. Refinement Mask multiplies its red channel by its alpha. Paint a normal layer mask for local removal or combine existing mask effects. Color, roughness and optional relief use the same geometric coverage, applied once. Transparent space contributes nothing to the underlying skin.

**Pigment and finish.** Pigment, Outer / Secondary Pigment, Gradient, Pigment Variation and Grain control color. Matte uses roughness 0.8, Satin 0.42, Gloss 0.12; Custom exposes the exact value. Fine variation is averaged when the output cannot resolve it. Changing finish or pigment color does not rearrange the cosmetic.

The default outputs are **Albedo and Roughness**. The existing skin normal is preserved. Optional **Normal Control** adds very small pigment relief; it does not also output a normal map, avoiding duplicate relief. Output switches are independent. Roughness affects shine without needing Normal Control. Add the relevant channels to a destination material that supports them.

Choose resolution for the actual face footprint in the atlas. A 2K texture with a small head island has far fewer than 2K texels across the face. Thin eyeliner benefits from 2K/4K outputs; softer boundaries are preferable to sharpening undersampled contours.

## Coating damage in existing generators

Print Aging and Metal — Rust, Oxidation & Corrosion now share a **Coating Damage** section. **Coating Damage Enable / Mix = 0** preserves their previous response, including existing projects.

- **Blistered paint:** raised closed domes and a smaller population of ruptured blisters revealing the backing.
- **Chipped enamel:** irregular exposed chips with narrow raised rims.
- **Peeling coated leather:** exposed backing with uneven lifted lips around the flake boundary.

Damage Cells controls feature frequency in the generator's existing coordinate space: UV units in UV mode, meters in World Position mode. Damaged Area controls how many candidate damage centers exist. It is not an exact percentage of final exposed texels. Boundary Distortion warps the chip outline, Damage Elongation stretches irregular flakes, and Ruptured Blister Fraction chooses how many domes expose the backing. Seed is deterministic. Neighboring damage shapes are combined continuously, avoiding hard cuts at procedural cell boundaries.

Intact Coating Color/Roughness and Exposed Substrate Color/Roughness/Metallic have independent material responses. Paint and leather coating are nonmetallic; an exposed metal substrate may be metallic. Raised Rim Height and Substrate Recess write positive and negative offsets around neutral 0.5 in **Normal Control**. Rim Width is measured in damage-cell units. These are texture relief effects, not displaced mesh geometry or loose flaps.

In corrosion, **Corrosion on Exposed Substrate** mixes the underlying rust result into exposed damage. Intact paint hides that corrosion. Existing curvature, normal-detail and AO inputs still determine the rust beneath the coating. Use masks to localize the painted surface.

Print Aging normally requires **Print / Coating Coverage Mask**. To make an entire coated surface, explicitly enable **Treat Entire Layer as Coating** and set Coating Damage Enable / Mix above zero. A supplied mask still restricts coverage. This opt-in keeps legacy unmasked print-aging operations from accidentally repainting a whole character.

Use Overlay blending for Normal Control when retaining underlying leather grain or skin relief. Ordinary color, roughness and metallic channels use Normal blending. The finished presets already set these channel defaults with opacity 1.

## Finished coating materials

The **Coatings / Finished Materials** category contains:

- Blistered Naval Paint
- Chipped Ivory Enamel
- Peeling Oxblood Leather
- Weathered Teal Enamel
- Aged Black Coated Canvas
- Salt-worn Brown Leather

Each combines a foundation, an independently editable coating-damage layer, and restrained surface residue with an editable Noise mask. Adjust the damage layer first, then tune the finishing pass or paint its mask. The six cosmetics plus six coating presets are installed with the main plugin. **UMA > Overlay Painter > Create Missing Face and Coating Presets** recreates missing factory assets without overwriting existing edited presets.

## Validation and practical limits

Validation uses the current main project, UMA **3.1f3**, Unity **6000.3.18f1**, on an Intel i7-12700K and NVIDIA RTX 5070 Ti. Actual Human Female 3.0 face renders, transparent albedo/roughness outputs, coating panels, and reports are supplied by the Examples companion under **Face and Coatings**. The live scene and its character materials are not modified by capture.

Face captures exercise the shared makeup sampler on the real posed mesh; separate transaction tests exercise the plugin host, shared output coverage and history. Coating panels apply the full material presets through the production host and GPU compositor. Panel lighting is illustrative and does not represent every target material or render pipeline. The focused tests are invoked directly with their NUnit setup/teardown in the live editor to preserve the open scene; the final run passed 38 cases with zero failures, including application of all twelve preset stacks. See the supplied report for exact cases, versions and results.

Only mesh maps declared by a generator are now computed. Makeup needs position, normal and surface coverage, so it avoids unnecessary AO/thickness ray casts and edge queries. A later generator requesting more maps upgrades the cache; cancellation retains the previous valid cache. The 2K face capture's map preparation fell from roughly 237 seconds to 4.3 seconds on this machine. Individual 2K cosmetic stacks took roughly 0.3–0.5 seconds for CPU sampling, excluding texture upload, rendering, export and host setup. The six 768px coating stacks took approximately 6.4–6.9 seconds each through the host, map bake, complete stack and GPU readback. Treat measurements as machine-specific, not a frame-time promise.

Close-up suitability still depends on the receiving UV footprint, landmark fit, lighting, base skin and artist refinement. Custom races and extreme expressions have not all been visually certified. Coating damage is an artistic procedural model, not a fracture simulation.
