# Eyebrows

Choose **+Path Ã¢â€ â€™ Generators Ã¢â€ â€™ Hair Ã¢â€ â€™ Eyebrows**. Draw an **open** path from the inner head beside the nose to the outer tail. Start with Natural or Groomed, set the path width to the brow's overall width, and adjust density, hair length, strand thickness, slant, color, and seed. The Scene view labels the selected path's head and tail in both world and UV authoring modes. **Reverse** changes growth direction without moving points; **Flip Across** swaps its upper and lower sides when UV orientation requires it. World-space paths automatically keep the upper growth side facing world up, whether drawn on the left or right brow. The correction is applied once to the source groom, so mirrored copies retain the same anatomical direction. Flip Across remains a manual override; existing UV projects keep their authored orientation. For a character posed sideways or upside down, use the override and path roll as needed.

The generator makes individual curved hairs with transparent gaps, coordinated roughness and rounded Normal Control relief. It does not add geometry. Use Normal Control to combine relief with the skin's existing normal; adding a second generated normal would apply the same relief twice. The receiving UMA material must support a normal map to display relief.

## Eyebrow shells and face skin

When entering from a character that already wears eyebrow textures, turn **Original Overlay (base)** **OFF** at the bottom of the Layers list before authoring replacement eyebrows. This hides the original artwork across channels while preserving the source assets and your new layers. It supports undo and is saved with your project.

UMA3's **HDEyeBrows_Thin_Wardrobe** equips **UMA30_Eyebrows_slot** with **HDEyeBrows_Thin_Overlay** in the Eyebrows wardrobe slot. This is a shell above the face, not the face texture. Paint the shell target when replacing those eyebrows. Its two sides have separate UV space.

The separate **OverlayPainter Examples** companion includes **Eyebrows/Projects/HDEyebrows Groomed.asset** and **HDEyebrows Bushy.asset**. These are editable OverlayPainter documents with two UV paths and transparent base textures. Open a document to work on the standalone shell. On an equipped character, select that slot and load the document through the painter's File menu. The UV placement survives face DNA changes. Select the layer to see its yellow path and red points on the 3D shell. Click a point to expose green curve handles and a blue width handle; drag a point on the shell to reposition it. The same UV path remains editable in the 2D window, where you can also add or insert points. Each side has its own seed and settings.

The example builder is available at **UMA Ã¢â€ â€™ Overlay Painter Ã¢â€ â€™ Examples Ã¢â€ â€™ Create Eyebrow Shell Projects**. It recreates the named example documents, so save your customized versions elsewhere first. It can also capture the shell on a currently built UMA3 character. Generated material ownership must be available after script reload; rebuild the character from its existing recipe if reconstruction reports missing ownership.

For skin-painted eyebrows, select the face skin target instead. Avoid leaving an existing textured eyebrow shell over the new artwork. The face capture example uses the actual built UMA mesh in an isolated preview and leaves the character's materials and recipe unchanged.

## Coordinates and scale

| Domain | Meaning |
| --- | --- |
| **u** | Normalized arc length along the complete brow: head 0, tail 1 |
| **v** | Position across the root region: lower 0, upper 1 |
| **s** | Normalized arc length along an individual hair: root 0, tip 1 |

Geometry is generated in units of the **base full path width**. For example, hair length 0.5 is half that width; thickness 0.01 is one percent. Path length determines the local aspect ratio. UV paths use normalized UV units, so the mesh's UV distortion still affects their physical appearance. World paths use surface-world distances. Changing texture resolution changes sampling, not strand identities or geometry. Existing point widths stretch the local ribbon and its hairs; use them for placement, then use the generator's upper/lower profiles for root distribution.

The path is the primary placement guide. Length Scale, Rotation, Slant, Center Offset, Arch and Tail Drop operate inside that guide. Presets preserve the path itself; **Apply Shape** controls whether preset shape settings are copied. Overall brow slant and local **Hair Slant** are separate controls.

Upper and lower profiles define the **root region**, not a hard clipping mask for every strand. Groomed containment bends hairs towards that region; stray allowance and flyaways permit controlled extensions. Transparent padding expands the ribbon to contain those hairs without shrinking the brow. Keep enough room between a shell's UV boundaries and the groom: texture generation cannot extend the physical shell.

## Curve controls

Expand an artistic parameter to choose Constant, Start/End, Start/Middle/End (movable midpoint), Full Curve, From Start, From End, or Region. The displayed domain identifies whether the curve follows **u**, **v**, or **s**. For strand profiles, start/end mean root/tip. Region has separate entrance and exit curves. From Start/End provide linear regional influences.

For quantities such as fullness, length, thickness and density, the curve **multiplies** the base value. For angles, it **adds degrees**. Constant leaves the base value unchanged. Randomness-amplitude curves are separate from the value curves. Weighted curve tangents and wrapping are stored in the path's JSON and preserved by duplication. The three domains are sampled over 0Ã¢â‚¬â€œ1; wrapping is preserved for editing, but the domain itself is clamped.

Evaluation order is: path metric Ã¢â€ â€™ root density and shape Ã¢â€ â€™ blended growth field Ã¢â€ â€™ strand length/bend/clumping/waviness Ã¢â€ â€™ root-to-tip thickness and opacity Ã¢â€ â€™ shared coverage Ã¢â€ â€™ material shading. Invalid negative physical quantities are clamped. Work limits reject oversized requests while preserving the previous layer output.

Examples:

- Thin outer tail: lower the end of Upper, Lower and Fullness over **u**.
- Upright head and swept body: Head Angle around 75Ã‚Â°, Body Angle around 25Ã‚Â°, then Arch/Tail towards 0Ã‚Â°.
- Disorder only at the tail: use a From End or Region curve on Direction Variation.
- Fine upper edge: reduce Length Across and Thickness Across at **v = 1**.
- Soft roots and sharp tips: keep Root Fade short and gentle, use a long Tip Taper with a sharper curve, and retain a zero endpoint in Strand Width.

## Distribution, grooming and appearance

**Target Count** requests the main population count; secondary and baby populations are additional fractions. Density profiles shape *where* those hairs are accepted. **Density Per Square Width** derives the main count from the root region's area in squared base-width units. Zero density produces no hairs. Extremely restrictive profiles may prevent the requested count being reached within the bounded candidate budget.

**Master seed** is in the basic generator controls directly below **Main color**; Advanced controls are not required. Change it to generate a different deterministic set of hairs. The same seed and settings reproduce the same result.

Roots use a progressive balanced sequence with bounded jitter. Candidate IDs and independent random streams keep color edits from rearranging roots and keep existing IDs as count increases. Changing acceptance profiles can replace rejected strands. Broad correlated variation adds regional differences; individual variation adds smaller irregularities. The generator never changes Unity's global random state.

Head, body, arch and tail growth blend smoothly. Upper and lower growth bands converge around an adjustable region. Main, secondary and baby hairs have independent population fractions and relative size. Clumping bends strands towards guides while keeping roots fixed; Preserve Length compensates for that bending. Slits provide position, width, angle and feathering and cut both root acceptance and visible hairs.

**Layer Depth Contrast** makes lower hairs darker and upper hairs lighter. Strands have stable depth order and are composited back-to-front. This works together with restrained per-hair color variation and root-to-tip color. Height uses a maximum rounded strand surface, so crossings do not accumulate exaggerated relief. Gray fraction adds occasional lighter hairs.

## Symmetry

Enable **Symmetry Enabled > Mirror X** on the path to draw both brows. World-space copies attach to the selected slot's surface, including a posed or slightly asymmetric eyebrow shell. The source path stays editable and retains its placement. Attachment is bounded by the path's scale; use **Frame Origin**, **Plane Rotation**, or **Edit Origin in Scene** if the character is substantially offset or turned relative to the mirror plane. Reference slots cannot receive the mirrored paint. With Auto Update off, press **Update** after changing symmetry.

Exact Mirror reuses the groom. Linked Shape / Independent Hairs generates a bounded alternate random variant for reflected ribbon copies while retaining shared shape controls. Independent uses the full alternate seed. Set Asymmetry to zero for identical hair geometry. To author different shapes, colors or density on the two sides, use two separate paths, as the shell examples do.

Reflection preserves head/tail and cross-brow semantics through ribbon coordinates. Coincident UV triangles on different mesh regions are detected and reported. Arbitrary partial UV overlaps are not exhaustively detected: overlapping destination texels cannot hold independent left/right textures, regardless of the symmetry setting.

## Preview, outputs and diagnostics

With Auto Update enabled, procedural paths regenerate **two seconds after the last interactive edit**, including point placement. Each added point restarts the timer; the path handles update immediately. Explicit Update bypasses this delay. Turn Auto off for deliberate edits and use Update or **Regenerate Layer**. Draft and final sampling use the same groom; the Layer Preview is a straight sample, while the Scene view shows the actual path.

Albedo, Roughness, Normal Control and optional AO use the same strand geometry, ordering and coverage. Background alpha is zero; empty Normal Control is neutral 0.5 and roughness/AO are neutral 1. Colors are shaded in linear light and stored as straight color; coverage is applied once. Add supported channels through Layer Channels & Settings. The reference shell's UMA material declares only Albedo, so the bundled shell projects author Albedo with transparency. Use a target UMA material with the corresponding PBR channel declarations for the other outputs.

Diagnostics include root region/outline, roots, density, growth, strand IDs, clumps, coverage, height and root-to-tip. Growth encodes **local** 2D direction in R/G as `(direction + 1) / 2`; it is a grooming diagnostic, **not** a material-ready tangent-space flow map. It is not transformed into the destination UV tangent frame. Dedicated direction/ID data export channels are not registered by this version; diagnostic Albedo can be exported for inspection.

Export through OverlayPainter's existing transparent UMA overlay workflow, retaining alphaMask coverage. Save the OverlayPainter document as the editable procedural source. An exported PNG does not contain path settings.

## Limits and implementation

Open paths are required. Closed and zero-length paths report an error without replacing valid output. Duplicate path points are tolerated. Geometry is independent of resolution; raster dimensions follow the destination footprint and quality setting, with a configurable maximum. Total hairs are capped at 6,000, raster area at four million pixels, and cached coverage fragments at four million. Long/thick hairs at extreme resolution can reach the work limit.

The implementation separates cached groom construction, coverage rasterization and material shading. Segment bounding boxes bound pixel work; analytic cross-section coverage retains fine hairs. Cubic hairs use 48 geometry subdivisions followed by arc-length resampling. This is currently fixed subdivision, not adaptive geometric refinement. Coverage rasterization runs in bounded batches on up to eight worker threads. Material shading uses independent pixel tiles, retaining stable per-pixel strand order and layered color. Curves are sampled before dispatch; workers do not create or upload Unity textures. The host still waits for preparation to finish, and texture upload/compositing remain on the main thread, so this is not a fully asynchronous build. The two-second placement delay avoids interrupting a sequence of point additions.

`ITexturePaintContextualPathGenerator` optionally extends the existing Prepare lifecycle with path dimensions, target resolution, footprint, closure, UV context and cancellation. Existing generators remain compatible. Prepared textures are reused across channel Bind calls and disposed after use. `ITexturePaintPaddedPathRaster` extends the ribbon without altering the artist's path. Versioned `extensionJson` retains settings even when the generator is unavailable; unsupported future versions preserve the last output.

Examples and focused integration tests ship in the separate OverlayPainter Examples and Tests companions. See the example README and validation report for the actual tested Unity/UMA versions, performance measurements and visual limitations. A rendered example is evidence to inspect, not a blanket guarantee of close-up quality on every face or UV layout.
