# Path generators

Create a generated path from **+Path → Generators**, immediately after **Garments**. Place its points with Shift-click in the Scene view (3D path) or the 2D canvas (UV path). Edit the construction in **Overlay Painter Properties → Path Generator**. An existing path can also select a generator there.

The generator uses the complete path's distance and width. It follows curves, point widths, the layer's symmetry frame, masks, channel blending, and endpoint fades. Generator settings live on the path and participate in save/recovery, duplication, and undo/redo. Auto updates rerender edits; with Auto off, use the path's Update command or **Regenerate Layer** in Layer Preview. The preview shows a straight sample before points exist; the Scene view shows actual placement.

## Included generators

| Menu | Constructions |
| --- | --- |
| Skin | Scar and Wound |
| Tattoo | Curved line, inward hooks, spiral, tribal flame, tribal scroll |
| Text and Lettering | Text with selectable Font and font style |
| Stitching | Lockstitch, chainstitch, zigzag, overlock, cover looper, blind stitch, bar tack |
| Seams and Trim | Denim chainstitch hem, double turn hem, flat felled, mock felled, lapped, plain pressed open, French seam, bound edge, piping, overlocked edge, coverstitch hem, rolled hem, blind hem, raw frayed hem, topstitch, bar tack |
| Fasteners and Distress | Metal zipper, coil zipper, exposed threads, frayed tear |

Stitch rows retain independent colors, widths, spacing, span, phase, relief, and placement. Seam constructions retain their cloth profile, puckering, roping, wear, roughness, and output controls. Zippers and linear tears now appear under Generators for paths; their projection versions remain under Garments. Pockets, panels, labels, folds, and broad wear remain garment constructions.

## Scar and Wound

Damage Type includes healed scars, fresh cuts, burns, and stretch marks. Burns use an irregular mottled surface; stretch marks use a narrow recessed profile along the path.

Healing closes the opening, reduces its recessed depth, and changes tissue color and roughness. Opening, endpoint taper, edge irregularity, depth, edge lift, inflammation, and colors are independent controls. Enable Sutures for stitch count, thickness, span, angle, color, and skin puckering. Removed stitches leave puncture relief without thread. On closed paths, endpoint taper is omitted and the procedural variation repeats across the closing join.

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
