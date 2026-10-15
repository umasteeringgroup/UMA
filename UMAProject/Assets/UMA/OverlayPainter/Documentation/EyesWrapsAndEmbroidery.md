# Eyes, wraps and advanced embroidery

These generators are included in Overlay Painter. Install **Overlay Painter Examples** for editable material presets and eye projects; install **Overlay Painter Tests** for the regression tests. Both are separate companions beneath Overlay Painter in the UMA Welcome plugin page.

## Eyes — Iris & Sclera

Select the eye paint target, then choose **+Plugin → Generators → Eyes — Iris & Sclera**. Start with Blue, Brown, Green, Hazel or Gray, adjust the Iris Color Multiplier, then set the pupil, fiber detail, inner color ring, limbal border and sclera veins. Roughness is independent for iris and sclera. Seed changes the organic detail without changing placement.

**Placement:** the supplied `UMA30_Eyes_slot` has overlapping left/right UVs centered at (0.5, 0.5). The UMA 3 placement mode uses that center; Iris Radius remains adjustable to match the desired visible iris size. Both eyes necessarily share the same texture detail. Custom centered UV exposes the center; Custom paired UV assumes one eye in each horizontal half of the texture and remaps each half to a square local UV domain. Paired Eye Variation only affects that separate-UV layout. It cannot create independent eyes on overlapping UMA eye UVs. Use separate slots/overlays or a unique eye unwrap for heterochromia.

Round, vertical-slit and horizontal-slit pupils are available. Radius and vein width are in local UV units; the pupil size is relative to the iris. Texture resolution changes sampling, not the underlying pattern. Fibers and vein edges are filtered as they approach pixel size.

The generator writes Albedo, Roughness and Normal Control, with switches for each output. **Relief is limited to the iris**; pupil and sclera remain neutral at 0.5. It does not write a direct Normal channel or generate a cornea. Keep the character's separate cornea geometry/material: painting eye detail onto the cornea would defeat its optical shading. These are surface textures, not an eye-refraction shader.

Overlay Painter offers channels supported by the selected UMA material. The stock UMA eye material exposes only Albedo: its specialized eye shader supplies the other shading. To author/export roughness and iris relief, use a material exposing Normal and Roughness/Smoothness. The supplied eye projects use the separate UMA Diffuse/Normal/Metallic authoring material for this purpose; they do not modify the installed eye slot or its original material.

Enable **Transparent Outside Iris** to retain the existing sclera beneath an iris overlay. All outputs use the same coverage. In mask mode the generator writes that coverage as grayscale. Export through the standard Overlay Painter workflow; the procedural layer remains editable in the saved document.

## Wraps, Tape and Dressings

Choose **+Path → Generators → Wraps, Tape and Dressings**, or **+Projection → Garments → Wraps, Tape & Dressings**. The five constructions are Leather Wrap, Cloth Bandage, Grip Tape, Gauze Wrap and Adhesive Dressing. Use a projection for a localized adhesive dressing; paths follow curved limb, garment or equipment surfaces.

- **Path Width / projection dimensions:** the overall area receiving the construction.
- **Feature Spacing:** unoverlapped strip width, in world units (UV units for a 2D path).
- **Strip Overlap:** reduces the advance between strips. **Winding Angle** tilts their joining edges across the ribbon.
- **Wrinkles / Lifted Edges:** separate small surface folds from overlap lips. Relief Height and 3D Depth control their strength.
- **Weave / Grain Spacing:** thread or surface-grain size. Gauze retains openings; increasing overlap partially closes them.
- **Edge Fraying:** irregular outer edges. **Staining** uses the separately editable Stain Color.
- **Adhesive Residue:** partial coverage and a roughness change around the outside of the footprint.

Adhesive Dressing has rounded adhesive wings, perforations and a raised gauze pad. Strip Overlap and Winding Angle are hidden because a dressing is one patch. Area Edge Fade defaults to 0.001. Standard path endpoint fades, masks, symmetry and output controls still apply.

These are texture constructions. They do not wrap new mesh around a limb or change its silhouette. Place a path around the surface to follow it; choose a mesh bandage when visible thickness or loose hanging ends are required. Closed paths fit an integer number of strip repeats around the loop.

## Advanced embroidery

Choose **Quilt, Embroidery, Perforation & Atlas Scatter**, set Mode to **Embroidery**, and select **Satin + contour border** or **Tatami + contour border** under Stitch Construction. Legacy directional fill remains the default so saved embroidery retains its existing behavior.

Use a white motif on black or transparent background as **Pattern Texture**. Alpha times luminance defines the motif. Without a texture, advanced modes generate a circular practice patch. Pattern Repeats and Offset place the motif; the main Rotation turns the complete construction. Advanced output is transparent outside the motif, so add a fabric layer underneath.

**Satin** uses continuous threads across the region. **Tatami** divides the fill into staggered stitches with editable length. Thread Density sets the number of threads per motif tile; Tatami Stitch Length and Contour Border Width are fractions of one motif tile. Borders follow outer boundaries and holes, with an independent thread color and raised profile. Zero border width disables them.

For multiple fill regions, supply a **Thread Region Map (RGB)** aligned to the motif. Red, green and blue areas have independent angles and colors; black uses the main thread. Mixed colors blend the controls. Import the region map as linear data (sRGB off). Thread angles describe an axis, so 0 and 180 degrees are equivalent.

Layered Thread Color Variation gives deterministic variation between stitches, with lighter crowns above darker gaps. Color edits do not rearrange stitches. Height, roughness, AO and albedo come from the same thread sample and use shared alpha. Normal Control supplies relief; do not add a second direct Normal output for the same relief.

The contour preparation is bounded to a maximum 1024-square motif field, reused across channels. Very tiny motifs and acute corners may need a larger source motif, a narrower border or fewer threads. This is material generation, not an embroidery-machine stitch-file exporter.

## Examples and validation

With the Examples companion installed, use **UMA → Overlay Painter → Examples → Create Eye, Wrap and Embroidery Presets**. This adds missing examples without replacing edited assets. **Create UMA Eye Projects** creates Blue and Hazel projects for the supplied UMA eye slot. Material presets can be applied with the existing material-preset workflow; garment presets can be assigned in the path/projection construction controls.

Examples are stored in `Assets/UMA/OverlayPainterExamples/Artist Generators`. The crest artwork and masks are original procedural examples. ArtStation products informed the feature comparison; no commercial product files are required or included.

The focused test category is **ArtistGenerators**. It exercises seed determinism, color/geometry independence, shared versus separate eye UV semantics, neutral sclera relief, transparency, save/clone behavior, cancellation, host undo, and native path/projection output alignment. Validation reports and captures from development are under `Build/Validation/ArtistGenerators` in the development checkout.
