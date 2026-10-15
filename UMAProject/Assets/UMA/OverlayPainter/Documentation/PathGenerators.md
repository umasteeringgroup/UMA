# Path generators

Create a generated path from **+Path → Generators**, immediately after **Garments**. Place its points with Shift-click in the Scene view (3D path) or the 2D canvas (UV path). Edit the construction in **Overlay Painter Properties → Path Generator**. An existing path can also select a generator there.

The generator uses the complete path's distance and width. It follows curves, point widths, the layer's symmetry frame, masks, channel blending, and endpoint fades. Generator settings live on the path and participate in save/recovery, duplication, and undo/redo. Auto updates rerender edits; with Auto off, use the path's Update command or **Regenerate Layer** in Layer Preview. The preview shows a straight sample before points exist; the Scene view shows actual placement.

## Included generators

| Menu | Constructions |
| --- | --- |
| Skin | Scar and Wound |
| Hair | [Eyebrows](EyebrowPathGenerator.md): curve-driven growth, tapered hairs, layered color, shell and face placement |
| Tattoo | Curved line, inward hooks, spiral, tribal flame, tribal scroll |
| Text and Lettering | Text with selectable Font and font style |
| Stitching | Lockstitch, chainstitch, zigzag, overlock, cover looper, blind stitch, bar tack |
| Seams and Trim | Denim chainstitch hem, double turn hem, flat felled, mock felled, lapped, plain pressed open, French seam, bound edge, piping, overlocked edge, coverstitch hem, rolled hem, blind hem, raw frayed hem, topstitch, bar tack |
| Fasteners and Distress | Metal zipper, coil zipper, exposed threads, frayed tear |
| Wraps, Tape and Dressings | [Leather wrap, cloth bandage, grip tape, gauze and adhesive dressing](EyesWrapsAndEmbroidery.md) |
| Welds and Heat | [TIG, MIG, solder and brazed seams with heat tint and spatter](SurfaceDetails.md#welds--heat-discoloration) |
| Lace and Openwork | [Floral, Gothic and scalloped transparent lace trim](SurfaceDetails.md#lace--decorative-openwork) |

Stitch rows retain independent colors, widths, spacing, span, phase, relief, and placement. Seam constructions retain their cloth profile, puckering, roping, wear, roughness, and output controls. Zippers and linear tears now appear under Generators for paths; their projection versions remain under Garments. Pockets, panels, labels, folds, and broad wear remain garment constructions.

## Zipper hardware height and shine

New metal and coil zipper paths start with **Area Edge Fade = 0.001**, minimizing the generator's fade at the ends. This is separate from the path's Start/End Fade controls. Existing paths retain their saved value; reduce Area Edge Fade in the generator if an older zipper still fades with path fades set to zero.

Both metal and coil zippers offer **Zipper Style Preset → Apply Style**: Silver Interlock, Antique Brass, Gold Coil, Black Molded, Bronze Rounded, Silver Bars, and Gold Chevron. These are editable procedural starting points inspired by common zipper construction. Applying a style changes tooth design, tape, stitching, and hardware finish without moving the path/projection, moving the slider, changing endpoint finishes, or changing enabled output channels. **Saved Preset** can store your customized result.

Under **Tooth Design**, choose interlocking, rounded, bar, molded, chevron, or coil teeth. Construction Default keeps the original metal/coil appearance. Width, length, corner rounding, and bank stagger are independent: **Feature Spacing** sets repeat pitch, **Tooth Length Multiplier** sets tooth length within a repeat, and **Bank Stagger** offsets the two rows (50% alternates teeth; 0% aligns them). Very large teeth can intentionally overlap. **Zipper Width** scales all hardware while tape width stays independent.

**Tape & Edge Cords** controls full tape width, relief, weave spacing, color contrast and weave relief, with plain, twill, herringbone and ribbed patterns. Original retains the previous weave. Weave Spacing uses world units, or UV units on 2D paths; zero follows thread width for older projects. Unresolved weave detail fades to avoid aliasing. Edge cords have independent color, width, inset and height; zero height hides them. Tape and cords remain nonmetallic and use Cloth Roughness.

**Stitching** offers straight, zigzag and chain patterns, zero to three rows on each tape side, thread color, width, spacing, inset, stitch length, spread, relief multiplier and roughness. Rows follow tape width. Spread controls zigzag/chain width; zigzag joins span the full repeat. Existing zippers keep straight stitches and their original dimensions. These additions work in paths and projections, and write coordinated Albedo, Roughness, Metallic and Normal Control outputs. Fine weave and thread need sufficient texture resolution at the zipper's actual footprint.

Metal and coil zipper generators write **Metallic**, **Roughness** and **Normal Control** for their hardware. Under **Hardware Height & Shine**, teeth, stops, slider, pull and tang/attachment each have **Height Multiplier**, **Metallic** and **Smoothness** controls. Smoothness 1 writes roughness 0 (shiny), including the bevel; smoothness 0 is matte. The backing remains nonmetallic with its own Cloth Roughness. Coil teeth remain nonmetallic unless overridden.

Parts initially follow **Hardware Roughness**; edit Smoothness to override that part, or choose **Use Shared Finish** to restore the shared roughness and default metallic value. Stops, slider, pull and tang start raised more strongly than before. **Relief** and **3D Depth** control overall strength, while per-part multipliers control the relative height. High relief is smoothly limited rather than clipped to flat white. Height affects lighting, not mesh silhouette. Keep **Metallic**, **Roughness** and **Height (Normal Control)** enabled in Generated Channels and present in the layer. Normally leave Normal (Direct) off to avoid applying relief twice. These controls work for both path generators and projection garments.

## Scar and Wound

Damage Type includes healed scars, fresh cuts, burns, and stretch marks. Burns use an irregular mottled surface; stretch marks use a narrow recessed profile along the path.

Healing closes the opening, reduces its recessed depth, and changes tissue color and roughness. Opening, endpoint taper, edge irregularity, depth, edge lift, inflammation, and colors are independent controls. Enable Sutures for stitch count, thickness, span, angle, color, and skin puckering. Removed stitches leave puncture relief without thread. On closed paths, endpoint taper is omitted and the procedural variation repeats across the closing join.

**Wound Color Noise** adds seeded light/dark mottling to the tissue. **Wound Height Noise** adds raised and recessed tissue detail to Normal Control (or the direct Normal output); add the appropriate channel to see it. Both start at zero and leave coverage unchanged. **Wound Noise Scale** sets cells per path width: larger values give finer detail. The existing **Seed** controls the pattern, which stays aligned across channels and joins continuously on a closed path. Noise is reduced at the rim and applied before sutures, keeping thread color intact.

**Wound Roughness** and **Rim Roughness** independently control the interior and raised edge: 0 is smooth/shiny, 1 is rough. Existing wounds retain their old healing-adjusted finish until each roughness control is edited; an explicitly chosen roughness is independent of Healing.

**Suture Start Offset (%)** and **Suture End Offset (%)** reserve a percentage of total path length at each end. The chosen stitch count is redistributed within the remaining interval, with half a stitch spacing before the first center and after the last. Angled threads and puncture falloff may extend beyond their centers. Offsets also affect removed-stitch punctures and puckering. If they total 100% or more, no sutures are drawn. On a closed path, offsets refer to either side of the first point.

**Distance Randomness (%)** varies each stitch center along the wound: 100% allows movement up to 45% of the regular spacing in either direction, preserving stitch order and keeping centers inside the start/end interval. **Angle Randomness (degrees)** adds an independent positive or negative offset to Stitch Angle, with final angles limited to -75 to 75 degrees. Both default to zero, preserving existing stitches. The existing **Seed** controls both patterns consistently across regeneration and material channels. Thread, punctures, and puckering move together; variation also remains visible with Removed stitches.

**Suture Height** raises the thread independently of **Puncture Depth**, which recesses the skin at its entry points. Both write to **Normal Control**, using path-width-relative relief; add that channel to the layer to see the result. **End Sink Length (%)** sets how much of each individual thread descends at either end (0 disables the taper; 50 meets at its middle). **End Sink Curve** shapes the smooth transition: 1 is smoothstep, larger values keep more of the end low. This is separate from the offsets that place whole stitches along the wound.

**Puncture Size** sets the hole radius relative to thread thickness. **Puncture Shading** controls its Albedo darkening and Ambient Occlusion without changing depth, including darkening where thread sinks into the hole. **Skin Puckering** now creates raised gathered skin around a depressed center; use **Pucker Height** for its relief and **Pucker Radius / Width** for its extent. Zero Skin Puckering disables the pucker. Puncture and pucker controls remain active with **Removed (Punctures Only)**; thread controls affect only visible sutures.

Normal Control adds relief to the existing material normal. Direct Normal output is available if that channel is added. These are texture and normal effects: they do not cut the character mesh or change its silhouette.

Existing `com.uma.texturepaint.scar-wound` plugin layers retain their saved output and legacy generator, including guide textures and procedural scar distributions. New scars are created from the path menu. The legacy layer offers **Copy Settings to New Scar Path**, which copies tissue/material settings into a new editable path and preserves the original layer. Place the new path's points; a grayscale guide or scattered procedural scars cannot uniquely reconstruct a spline.

## Tattoo

Tattoo constructs solid-ink shapes with tapered curved outlines, inward hooks, spirals, and tribal motifs. It is separate from the text generator.

**Curved Line** and **Inward Hooks** let either end taper independently toward the centerline. Start/End Taper Length and Taper Curvature control that contour; the ink stays opaque inside it. Curl Start Inward and Curl End Inward are independent, with direction, radius, and turns. Line Bend adds a sweep inside the ribbon; the path's own Bezier curve controls its placement on the surface.

**Spiral** provides a tapered spiral. **Tribal Flame** and **Tribal Scroll** create branches on a curved spine, with sharp flame tips or curled scroll tips. New tribal paths enable **Procedural Arms**. Older saved tribal paths retain their fixed motif until you enable it; turning it off restores that construction.

Procedural tribal controls:

- **Arm Count:** zero to sixteen branches. Zero leaves the spine and any enabled end curls.
- **Arm Symmetry:** None alternates arms between sides; Mirror Across Path reflects the construction across its centerline; Half Turn pairs it with a 180-degree rotation. Symmetric modes expose **Arm Pairs** (zero to eight) and show the total arm count. Partners share their variation, including spine, curls and negative-space cuts.
- **Branch Start / End:** the region along the spine where branches attach, measured from zero to one. In Half Turn mode partners attach at the corresponding opposite-end positions.
- **Arm Length, Angle and Sweep:** branch reach, departure angle and curved profile. The complete silhouette is fitted into the path width with transparent padding; longer arms make the spine relatively smaller.
- **Arm Curl Direction:** inward, outward, clockwise, counterclockwise or alternating. Symmetry reflects/rotates the selected direction with each paired arm. Scrolls also expose Arm Curl Radius; Curl Turns controls their winding and the flame tips' sweep.
- **Variation / Seed:** deterministic changes to attachment position, length, angle, thickness and curls. **Next** advances the seed. Zero variation ignores the seed. Regeneration and save/reload reproduce the same design without changing Unity's global random sequence.
- **Spine Bend:** the central stroke's sweep. Symmetric modes include its reflected/rotated counterpart.

Line Thickness, Design Aspect, Taper Curvature, Curl Turns and Negative-space Cuts further adjust the construction. **Flip Across Path** reverses the entire motif; it does not add arms. Repeat a motif along the path, or combine multiple paths for larger bespoke designs.

The Albedo, Metallic, Roughness, Normal, Normal Control, Emission, and other outputs share the exact same silhouette, including transparent cutouts. Add channels in Layer Channels & Settings and edit their values. Ink alpha controls shared opacity; Raised / Engraved controls relief.

## Text and Lettering

Enter text and assign a Unity **Font** asset. With no assignment, the built-in LegacyRuntime font is used. Font style, ink color/alpha, letter proportions, inset, repeat count, and raised/engraved relief are editable. Unsupported glyphs report a useful error before replacing the existing output. Empty text clears the lettering.

Text runs along the path; its height follows the path width. Preserve Letter Proportions fits the text within that region; turn it off to stretch the text to the region. Multiline text, spaces, counters inside letters, and italic overhangs are included in the glyph mask.

**Flip X** mirrors the lettering left/right along the path; **Flip Y** mirrors it top/bottom across the path. Enable both to rotate the lettering 180 degrees. These controls correct UV orientation without moving the path or changing its points. They affect each repeated text block, the preview, and every material channel, including generated relief. Both default to off and are saved with the path.

Add any channel supported by the paint target under **Layer Channels & Settings → New Channel → Add Channel**. For example, add Metallic, expand it, and set Value to 1 for metallic lettering. Roughness, emission, thickness, masks, and other channels use the same glyph coverage as Albedo, even if Albedo output is disabled. Ink alpha changes the shared coverage. Channel opacity/contribution remain independent artistic controls. Existing text-tattoo paths retain their stable generator ID and reopen as Text and Lettering.

Albedo uses Ink Color. Normal and Normal Control use the relief settings. Other Tattoo channel values are edited in their channel entries. The generator never fills the transparent rectangle surrounding the letters.

Overlapping mirrored UVs still share texture pixels. Disabling layer symmetry does not separate shared UV islands; independent artwork on those surfaces requires distinct UV space.

## Layer effects on generated paths

Open **fx** on the layer to add effects. Stroke, inner/outer glow and shadows follow the visible generated artwork, including letter holes and tattoo cutouts. Procedural Stitch follows an inset contour of the artwork; adjust **Thread Width (px)**, **Stitch Length (px)**, **Edge Inset (px)** and **Contour Rows**. Keep the inset small enough to fit inside thin letters or tattoo arms. These effects update through compositing without rebuilding the path, and appear in the Layer Preview. Fine effects can be difficult to see in the reduced-size preview.

**Bevel Edge** shades the artwork's outer and inner contours. Set Light Color, Dark Color, Width, Smooth and Light Angle to control the relief. **Edge Fade** fades inward from those contours using Fade Width, Fade Curve and Level. Like the other effects, each targets the selected material channel. All eleven effect types work on generated path output and regular plugin layers, including their previews.

Ordinary ribbon paths retain their existing left/right edge effects, proportional stitch controls and side-fade controls.

## Extending the mode

Implement `ITexturePaintPathGenerator` in an optional addon and call `TexturePaintPathGenerators.Register` from an editor initialization hook. IDs must be stable and unique. `MenuPath` is relative to Generators; all registered generators automatically appear in that submenu and in the path's Generator selector. `CreateSettings` supplies `enabled=true`, its `generatorId`, and defaults, with addon-specific serialized data in `extensionJson`. The settings class itself defaults to disabled so old documents cannot accidentally enable a missing generator.

`Prepare` runs on the main thread once per rebuild, before the old raster is replaced. Return an `IDisposable` containing temporary resources. Preparation failure preserves the previous pixels and reports an error. `Bind` supplies material properties for each selected channel; `size.x` is full ribbon width and `size.y` is total length. The engine disposes prepared resources when the stroke ends, including cancellation.

For a texture-based implementation, return a `TexturePaintPathGeneratorRaster`. Supply a shared RGBA `Coverage` texture and optional channel images in `Channels`; call its `Bind` method from the generator's `Bind`. Image X covers the full path distance, and Y covers its width. Coverage alpha is authoritative for every channel. Missing images use that channel's selected color/value. Use linear textures for data channels and the appropriate Unity texture color-space flag for Albedo/Emission. Prefer Normal Control for relief that follows curved paths and mirrored tangent frames. Explicit Normal images must already be encoded in the destination tangent frame.

Raster resources own their textures by default. Set `OwnsTextures=false` when using persistent assets. Register an optional settings inspector in `TexturePaintPathGeneratorEditors.Inspectors[id]`; it edits a working copy inside the host's undo/change scope. Without a custom inspector, the host exposes the extension JSON. Unregister removed extensions; saved settings and cached output are retained when their implementation is unavailable.

The path API belongs to OverlayPainter's optional runtime/editor assemblies and introduces no dependency from UMA core onto the painter or its example generators.
