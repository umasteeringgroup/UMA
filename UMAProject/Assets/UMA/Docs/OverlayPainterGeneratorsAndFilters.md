# Overlay Painter Generators and Filters

This guide explains Overlay Painter's generators and filters from an artist's perspective. It focuses
on choosing the right tool, building a dependable layer stack, understanding the material channels a
tool changes, and avoiding results that look procedural, noisy, or physically inconsistent.

For the complete painting, layer, mask, spline, save, and export workflow, see
[Overlay Painter](OverlayPainter.md). Plugin developers should use the
[Plugin API v2 reference](../OverlayPainter/PLUGIN_API_V2.md).

## Contents

- [The essential mental model](#the-essential-mental-model)
- [Generator selection at a glance](#generator-selection-at-a-glance)
- [Cloth Texture](#cloth-texture)
- [Quilt, Embroidery, Perforation, and Atlas Scatter](#quilt-embroidery-perforation--atlas-scatter)
- [Text](#text)
- [Fabric Fuzz, Fiber, and Fray](#fabric--fuzz-fiber--fray)
- [Rust, Oxidation, and Corrosion](#metal--rust-oxidation--corrosion)
- [Dripping Corrosion](#dripping-corrosion)
- [Pores, Scratches, and Micro Detail](#surface--pores-scratches--micro-detail)
- [Veins, Bruising, and Subdermal Variation](#skin--veins-bruising--subdermal-variation)
- [Stubble Maker](#skin--stubble-maker)
- [Scar, Wound, and Stretch Marks](#skin--scar-wound--stretch-marks)
- [Creature Scales and Skin Variation](#creature--scales--skin-variation)
- [Combat Scratches and Dents](#metal--combat-scratches--dents)
- [Agify, Dirtify, Edge Wear, and AO Variation](#agify--dirt--edge-wear)
- [Working with filters](#working-with-filters)
- [Stylization, Kuwahara, and Quantization](#stylization-kuwahara--quantization)
- [Production layer recipes](#production-layer-recipes)
- [Troubleshooting](#troubleshooting)

## The essential mental model

Generators and filters live on **Plugin layers**. Click **+ Plugin**, choose the plugin in the layer
properties, set its controls, and click **Generate**. The completed result is cached as ordinary layer
content. It remains visible, saves with the project, exports normally, and can use layer opacity,
per-channel blending, a layer mask, groups, and layer effects.

The separate **Plugins** window is a discovery and diagnostics manager; it does not execute generators
or filters. Run them from a Plugin layer's properties. Generated cache pixels are not directly painted
like a Paint layer. Paint beneath or above the Plugin layer, or select its mask thumbnail and paint the
mask to reveal/hide the generated result.

A **generator** creates material information procedurally. It may use the model's world position,
normal, curvature, ambient occlusion, thickness, UV islands, or composed texture channels. Examples
include cloth weave, rust, skin variation, and combat damage.

Generators write only channels that exist on the current target. A listed output is a capability, not
a promise that every material has that channel. Cloth Texture additionally provides explicit output
toggles; the other production generators skip unavailable declared channels automatically.

A **filter** transforms an existing channel or mask. A filter always reads the composite **below its
own Plugin layer**. It does not read its previous cached result, so regenerating does not repeatedly
blur, sharpen, or grade the same pixels. Place a filter immediately above the content it should
process.

When a source layer below a Plugin layer changes, or when plugin parameters change, the Plugin layer
becomes **Stale**. Its last successful cache remains visible until **Regenerate** succeeds. Canceling
or failing a generation also keeps the previous cache intact.

### A dependable working method

1. Confirm the target material exposes the channels the result needs. A plugin cannot write a channel
   that the current material does not have.
2. Build and approve large forms first: base color, major panels, broad skin regions, or the principal
   weave.
3. Add a Plugin layer above the content it should complement or process.
4. Start with one feature at a time. Temporarily disable or reduce unrelated sections while judging
   scale, placement, and silhouette.
5. Judge the result in both 2D and 3D. The 2D view exposes repetition and UV discontinuities; the 3D
   view reveals physical scale, curvature placement, roughness, and normal response.
6. Use Plugin-layer opacity for a quick global reduction. Use per-channel opacity when color is right
   but roughness or height is too strong.
7. Add a black or white layer mask for hand-painted art direction after the procedural result is close.
8. Regenerate after changing parameters or lower layers. Save once the cache is current.

### Channels artists should recognize

| Channel | Artistic meaning | Common mistake |
| --- | --- | --- |
| Albedo | Base color without lighting baked into it | Making cavities black and highlights white instead of letting AO, roughness, and normals do their work |
| Roughness | High values are broad/dry; low values are tight/shiny | Treating it as brightness rather than microsurface response |
| Metallic | Whether the surface behaves as metal | Giving rust, dirt, skin, or cloth a metallic value |
| Ambient Occlusion | Contact/cavity darkening signal | Using it as a replacement for believable dirt color or height |
| Normal Control | Neutral-gray height-like modifier combined with the Normal map | Expecting it to look like an RGB normal map; neutral is 0.5 gray, dark recesses, light raises |
| Skin Color Mask | Shader-specific skin tint/variation control | Assuming every shader consumes it; verify the UMA material mapping |
| Thickness | Subsurface/transmission thickness control | Expecting visible change from a shader that does not use thickness |
| Detail Mask | Shader or workflow detail-selection channel | Assuming it has a universal visual meaning across materials |
| Custom | Authoring/guide channel, such as a ribbon guide for scars | Expecting it to export or render as a standard material channel |

Normal Control is intentionally separate from the RGB Normal channel. Multiple Normal Control layers
compose like other grayscale material layers, and the resulting height modification is applied to the
displayed and exported normal. Keep most values close to neutral gray; extreme black and white create
very steep, often implausible surface changes.

### Projection, scale, and seams

Use **World Triplanar** projection for dirt, rust, pores, scratches, and organic breakup that should
cross UV seams and retain a consistent physical scale. It blends projections using the model's world
normal. Use **UV** projection for authored layouts, aligned fabric, decals, or a texture that was made
for the target UVs.

World projection is only as stable as the model's scale and pose. If two objects have dramatically
different import scale, the same Pattern Scale will not look physically identical. UV projection is
only as consistent as texel density; differently scaled UV islands can make one sleeve's fibers twice
as large as another's.

Pixel controls such as scar width, morphology radius, gap size, and edge size are measured at the
current output resolution. Doubling texture resolution roughly halves their apparent physical width
unless the pixel value is also doubled.

### Control texture, layer mask, and generated coverage

These are related but different:

- A generator's **Control Mask** or **Guide Texture** is an immutable input sampled while generating.
  It is useful for a prepared placement map or a reusable asset.
- The Plugin layer's **layer mask** is editable afterward with all mask-painting tools. It is the best
  choice for final hand correction and localized art direction.
- Generated coverage is the alpha built by the generator itself from curvature, noise, guides, or
  features. It is cached in the output and remains editable through layer compositing.

Texture and Sprite parameters are GPU-snapshotted and do not require Read/Write import. Sprite inputs
sample only their Sprite rectangle, even when stored in an atlas.

## Generator selection at a glance

| Goal | Start with | Common companion |
| --- | --- | --- |
| Woven shirt, sweater, denim, plaid, canvas | Cloth Texture | Fabric Fuzz & Fiber Fray |
| Quilted padding, stitched motifs, punched panels, scattered atlas decals | Quilt, Embroidery, Perforation & Atlas Scatter | Fabric Fuzz, Edge Wear, or Dirtify |
| Labels, lettering, embossed type, ribbon-following words, text-shaped group materials | Text | A Custom-channel Path/Ribbon guide or group mask |
| Loose fibers, pilling, worn cloth edges | Fabric Fuzz & Fiber Fray | Cloth Texture |
| General aged prop with both grime and chipped edges | Agify — Dirt & Edge Wear | Hand-painted layer mask |
| Dirt only in seams and cavities | Dirtify — Gap Dirt | Surface Micro Detail |
| Paint loss only on exposed edges | Edge Wear | Rust or Combat Scratches & Dents |
| Corroded metal with pits, flakes, and runoff | Metal — Rust, Oxidation & Corrosion | Dirtify |
| Pores, fine scratches, generic microsurface | Surface — Pores, Scratches & Micro Detail | Levels & Curves |
| Realistic skin variation, vessels, bruises, freckles | Skin — Veins, Bruising & Subdermal Variation | Surface Micro Detail at low strength |
| Beard shadow, facial stubble, or a freshly shaved head | Skin — Stubble Maker | A painted layer mask for hairline/cheek cleanup |
| Directed or procedural scars and wounds | Skin — Scar, Wound & Stretch Marks | A Custom-channel Path guide |
| Scales, amphibian skin, spots, age variation | Creature — Scales & Skin Variation | Surface Micro Detail |
| Dents, pings, gouges, chipped armor | Metal — Combat Scratches & Dents | Edge Wear, Dirtify, or Rust |
| Lightweight fine AO breakup | AO Variation Generator | Levels & Curves on AO |

### Shared controls on the seven material generators

Fabric Fuzz, Rust/Corrosion, Surface Micro Detail, Veins/Subdermal, Scar/Wound, Creature Skin, and
Combat Scratches/Dents share a **Coverage & Placement** section.

- **Projection** selects UV or World Triplanar. This controls the procedural feature coordinates; the
  optional Control Mask itself is sampled in target UV space.
- **Pattern Scale** controls base feature frequency. Higher values generally create more, smaller
  features. Its physical appearance depends on UV density in UV mode and model scale in world mode.
- **Seed** changes deterministic arrangement without changing the selected artistic family. A saved
  seed regenerates identically from the same inputs.
- **Overall Amount** multiplies generated coverage across every output channel. Use it for a coherent
  first reduction; use per-channel opacity when only color, roughness, height, or another output needs
  adjustment.
- **Control Mask** is an optional grayscale texture multiplied into generation. White allows the
  effect, black excludes it, gray attenuates it, and texture alpha also contributes. Use the editable
  Plugin-layer mask for painting corrections after generation.

Set Projection and Pattern Scale before detailed material controls. Changing them later changes the
location and apparent size of nearly every procedural feature.

## Cloth Texture

**Best for:** shirts, sweaters, trousers, uniforms, denim, canvas equipment, woven accessories,
single-color garments, stripes, plaid, and repeated fabric motifs.

**Outputs:** optional Albedo, Roughness, and Normal Control. At least one output must remain enabled.

Cloth Texture builds the actual weave and material response. It is different from Fabric Fuzz, which
adds loose fibers, pilling, and frayed edges over an existing cloth surface. A strong fabric stack
usually places Cloth Texture first and a restrained Fabric Fuzz layer above it.

### Choosing a weave

| Weave | Use it for | Artistic character |
| --- | --- | --- |
| Cotton / Plain | Shirts, sheets, basic woven cloth | Balanced one-over-one structure |
| Knit | Sweaters, jersey, soft knitted panels | Looping, visibly directional construction |
| Twill | Workwear and diagonal woven fabric | Regular diagonal ribs |
| Corduroy | Trousers, jackets, upholstery | Strong parallel raised wales |
| Herringbone | Tailoring and decorative workwear | Alternating chevron twill direction |
| Denim | Jeans and heavy work clothing | Warp-dominant twill with contrasting cross-thread |
| Canvas | Bags, tents, heavy garments | Coarse, broad plain weave |
| Linen | Shirts and natural cloth | Irregular slubs and uneven thread response |
| Satin | Linings and formal fabric | Long smooth floats and lower roughness |
| Basket | Heavy woven cloth | Threads grouped into broader over-under blocks |
| Houndstooth | Graphic suiting | Twill structure with tooth-like two-color organization |
| Leno | Open, gauze-like fabric | Paired crossing warp threads |
| Dobby | Small geometric woven motifs | Repeating raised and lowered thread figures |
| Pile | Terry, fleece-like, or looped fabric | Raised loops and soft tufts |
| Crepe | Textured dress fabric | Twisted, irregular crinkle |
| Jacquard | Decorative woven motifs | Larger figured weave; use a Pattern Sprite for exact authored artwork |

### Building the base fabric

Choose the weave, then set **Threads / UV** while looking at the garment in 3D. This is the most
important scale control. Do not judge it only in the 2D texture: a weave that looks attractive at
100% zoom may be the size of rope on the character.

Use **Thread Aspect** to correct stretched UVs or create deliberately elongated construction.
**Fabric Rotation** aligns the physical weave independently from stripes and motifs. **Weave
Definition** controls over-under separation; **Thread Roundness** changes the crown profile; and
**Thread Irregularity** breaks sterile computer-perfect spacing. Natural cloth usually benefits from
some irregularity, but too much makes threads look melted or tangled.

**Base Color** and **Cross-Thread Color** can be close for dyed solid fabric or clearly different for
denim and shot fabric. **Cross-Thread Amount** decides how much the second thread system contributes.
For a clean single-color shirt, use neighboring colors rather than identical colors; a small warm or
cool shift preserves structure without reading as a pattern.

### Surface response

Set **Normal Control Height** by silhouette and lighting, not by the grayscale thumbnail. Cloth weave
is microgeometry, so the useful range is generally subtle. Use **Base Roughness** for the overall fiber
finish and **Weave Roughness** for differences between thread faces and gaps. Satin and polished worn
cloth should be smoother; canvas, wool, and damaged fibers should be rougher.

**Fiber Color Variation**, **Fiber Height**, and **Fiber Roughness** add fine breakup below the main
weave. Raise them only after the principal thread scale is correct. If these dominate at normal camera
distance, the fabric will sparkle or crawl during animation.

### Stripes and plaid

Vertical and horizontal stripes are ordered entries. Each entry supplies direction, color, repeat-cell
position, width, edge softness, and opacity. **Vertical Repeats** and **Horizontal Repeats** specify how
many repeat cells cross the UV tile. Position and Width are fractions of one cell, not pixels.

Later stripe entries blend over earlier entries. Put broad foundation bands first, then narrow accent
lines. A typical plaid might use a broad low-opacity red vertical stripe, a narrow dark vertical line,
a broad blue horizontal stripe, and a final thin light horizontal accent. Use **Stripe Rotation** to
rotate the complete layout without changing the weave direction. A stripe list supports up to 64
entries, which is ample for complex tartan-like repeats while keeping generation and UI manageable.

Gotcha: stripes follow texture UV space. They can change direction or width across mirrored, rotated,
or differently scaled islands. For tailored alignment across separate garment pieces, prepare the UVs
for that layout or use masks/separate Plugin layers per panel.

### Pattern Sprite

The optional Sprite can cover the whole fabric, only the stripe regions, or everything outside the
stripes. Direction can follow warp, weft, or either diagonal, with independent repeats, aspect,
rotation, and X/Y offset. Enable **Use Sprite Color** for a colored motif. Leave it disabled to treat a
grayscale/alpha Sprite as a tintable pattern using **Pattern Color**.

**Pattern Height** embosses or recesses the design in Normal Control; **Pattern Roughness** changes its
surface finish. Printed ink normally needs little or no height and a modest roughness change. A woven
jacquard motif can justify more height because the pattern is structural.

### Thread-aware wear

**Color Fade** reveals the Faded Color in broad fractal regions. **Wear Region Scale**, **Wear Level**,
and **Wear Breakup** establish the size and boundary of those regions. Direction can be isotropic or
stretched vertically, horizontally, or diagonally.

**Follow Weave** biases wear by exposed thread crowns. **Worn Fiber Contrast** reveals finer fiber
variation inside the fade. **Worn Roughness Change** can make abrasion polished or make damaged fibers
rougher, and **Worn Thread Flattening** reduces Normal Control height where threads have been worn
down. These material changes are what keep fading from looking like flat airbrushed color.

### Useful cloth recipes

- **Solid cotton shirt:** Cotton/Plain, high thread count, neighboring thread colors, moderate
  roughness, low height, no stripes, very low wear.
- **Denim:** Denim weave, blue warp and pale cross-thread, medium definition, high thread count,
  restrained directional fade, slight roughness reduction and thread flattening in worn zones.
- **Cable-like sweater base:** Knit, larger visible loops, higher height, high roughness, then a Fabric
  Fuzz layer for loose fibers. Use a Pattern Sprite if an exact cable layout is required.
- **Plaid shirt:** Plain or twill weave, build broad stripe entries first and narrow accents last, keep
  stripe colors slightly blended with the base, then add low thread-aware wear.
- **Printed fabric:** Generate a restrained weave, add a Pattern Sprite with low or zero height, and
  adjust Pattern Roughness to represent ink, dye, or woven decoration.

## Quilt, Embroidery, Perforation & Atlas Scatter

**Best for:** padded armor and jackets, upholstered panels, stitched logos, decorative thread,
ventilated leather or metal, punched speaker-like surfaces, scales/patches/rivets/decals stored in a
regular texture atlas, and repeatable surface dressing.

**Outputs:** optional Albedo, Roughness, Metallic, Ambient Occlusion, and Normal Control. In Layer
Mask mode it writes the generated feature coverage as grayscale instead of material channels.

This is a four-mode production generator. Switching **Mode** does not discard the controls of the
other modes, which makes comparison safe. Establish **Pattern Scale**, **Aspect**, and **Rotation**
first. The pattern is UV based: inspect seams and texel density in 2D before polishing micro-detail.

### Quilt

Quilt builds a padded cell, its recessed seam, and broken individual stitches as related signals.
Choose Square, Diamond, or Wave Channels. **Puff Height** and **Puff Roundness** define the pillow;
**Seam Depth** recesses the channels. Set **Stitches / Cell** before **Stitch Duty** and **Stitch
Width**. A short duty value leaves visible thread gaps; a value near one looks like an uninterrupted
cord.

Use a restrained Normal Control height and let AO supply contact depth. Very dark Albedo seams plus
strong AO plus deep Normal Control will triple the same cue. For cloth, Metallic should normally be
off. For quilted or stamped metal, enable Metallic and reduce Puff Height to avoid an inflatable
look. A second Fabric Fuzz layer can soften cloth crowns without altering the approved quilt scale.

### Embroidery

Assign a **Pattern Texture** whose alpha/luminance contains the motif. The texture is captured by the
plugin and does not need Read/Write enabled. **Pattern Repeats**, threshold, and X/Y offset place it.
The motif is then filled with directional satin-thread ridges. **Thread Direction** should agree with
how a real embroiderer would turn stitches around the form; one direction across a complex logo can
look machine-flat, while a few separate masked layers can give major regions different directions.

Thread Density controls visible strands, **Thread Breakup** prevents a perfect synthetic edge,
**Thread Sheen** lowers roughness on thread crowns, and Embroidery Height raises the thread. Judge
height in grazing 3D light. If the motif looks like molded plastic, lower height and increase fine
thread density rather than increasing pattern contrast.

### Perforation

Perforation provides Grid, Hex/Staggered, and Organic Jitter layouts. **Hole Radius** controls how
much of each repeat is recessed; **Bevel Width** and **Bevel Height** create the rolled/punched lip;
**Hole Depth**, Interior Roughness, Recess Color, and AO establish the cavity. **Position Jitter** is
useful for worn leather but should remain low on machined metal.

The generator shades a perforation; it does not delete mesh triangles or create real through-holes.
For an actual cutout, generate the same coverage into a mask and route/export that mask to an
alpha-clipped shader workflow. Confirm that the runtime material supports alpha clipping before
depending on silhouette holes. At ordinary viewing distance, a dark recess plus AO and Normal
Control is usually cheaper and more stable.

### Atlas Scatter

Use a regular grid atlas with **Atlas Columns** and **Atlas Rows**. Alpha defines every stamp;
**Use Luminance as Mask** can additionally suppress dark pixels. Candidate Scatter Columns/Rows set
distribution density, while **Density** decides which deterministic candidates are occupied.
Stamp Size, Size Variation, Rotation Variation, and Position Jitter remove obvious repetition. The
seed changes selection and transforms without changing the art family.

Enable **Use Atlas Color** for authored decals or turn it off to colorize all cells with Thread/Stamp
Color. Tint Variation is intentionally per stamp. Roughness Change, Metallic Change, Stamp Height,
and AO Strength coordinate the material response. Rivets typically want some Metallic and height;
paint flakes want little/no Metallic and a roughness change; leaves or mud clods usually want no
Metallic. Atlas cells must have padding to prevent bilinear sampling from borrowing a neighboring
cell at their edges.

### Using any mode as a group material mask

Add a black mask to a Group, click its mask thumbnail, choose this generator under **Mask Filter /
Generator**, and Generate Mask. The result is editable grayscale coverage. Put separate Fill or
Plugin layers inside the group for Albedo, Roughness, Metallic, and Normal Control. This is the clean
choice when one quilt/stitch/perforation/scatter shape should drive a more elaborate multi-layer
material than the generator's direct output values.

Gotchas:

- Modes share common scale/color controls, but their artistic units differ. Recheck scale after
  switching modes.
- UV repetition can reveal seams. Use a layer mask or align UV islands when the pattern is authored.
- Perforation is not topology. A mask only becomes a visible hole when the export/runtime shader uses
  it for alpha clipping.
- Keep atlas cell dimensions and padding consistent. Unequal cells require separate atlases.
- Disable outputs you do not want; hidden extra channels make later diagnosis harder.

## Text

**Best for:** garment labels, serial numbers, signage, embossed/debossed lettering, metallic foil,
roughness-only print, ribbon/belt text, and text-shaped group materials.

**Outputs:** optional Albedo, Normal Control, Roughness, and Metallic. In Layer Mask mode it writes
only grayscale glyph coverage, so one text shape can reveal an entire material stack in a Group.

Enter Text, select an optional Unity **Font** asset, set Font Size and Font Style, then choose Block or
Follow Custom Ribbon. If Font is empty, Overlay Painter uses Unity's built-in Legacy Runtime font.
Font Size is requested in pixels and therefore depends on output resolution. Extra Weight expands a
face beyond its native Bold style. Outline Width expands coverage; use a separate Text layer when the
outline needs a different color/material from the face. Multiline text supports left, center, and
right alignment in Block mode.

### Block text

Set Block X/Y in normalized UV coordinates and Rotation in degrees. The text is rendered at native
target resolution, so inspect the final export size before approving thin strokes. A 2 px line that
looks crisp at 4K may collapse at 1K. Use font weight or a small outline instead of relying on texture
filtering to rescue sub-pixel features.

Enable only the physical channels the lettering needs:

- Albedo supplies the selected Color.
- Normal Control uses Height around neutral gray. Positive values raise and negative values recess.
- Roughness writes Roughness Value only under glyph coverage.
- Metallic writes Metallic Value only under glyph coverage. Printed ink on cloth is not metal;
  stamped foil or exposed metal may be.

The layer remains a procedural Plugin layer. Edit the wording, font, size, style, placement, output
values, or mask and Regenerate; save/reopen retains the Font reference and all settings.

### Text on a ribbon

Create a Path/Ribbon layer **below** the Text Plugin layer. Add a **Custom** channel to the path and
draw a white/gray ribbon on black. In Text choose **Follow Custom Ribbon**. The generator extracts a
smoothed centerline and width from the composed Custom guide, then warps glyphs along it. The path
remains the positioning tool: edit its points/width, rerender it, and Regenerate Text.

**Guide Threshold** rejects weak Custom values. Ribbon Padding keeps letters away from sides and
ends. **Fit To Ribbon Length** prevents long wording from overrunning the guide. The raster guide is
unambiguous for a normal strap, belt, piping line, or gently curving ribbon. A guide that folds over
itself in UV space, branches, or doubles back sharply can have more than one center for the same
principal-axis position; split that wording into separate guide/text layers.

The Custom guide is an authoring channel, not a visible shader channel. Hide its display contribution
as appropriate, but keep the guide layer below Text so regeneration can read it. If no Custom guide
is available, ribbon mode has nothing to follow.

### Text as a group mask

For one text shape controlling several materials:

1. Create a Group and put Fill/Plugin layers for the desired Albedo, Roughness, Metallic, and Normal
   Control inside it.
2. Add a black mask to the Group and select the mask thumbnail.
3. Choose Text in **Mask Filter / Generator**, configure it, and Generate Mask.
4. Paint grayscale corrections on the mask normally. Regenerating replaces the generated mask, so
   preserve hand edits on a separate parent/child mask arrangement or finish generation first.

Mask text is grayscale by design. Font color and material-output toggles are ignored in mask mode.

Gotchas:

- Font assets must be included in the project/package that reopens the document.
- Dynamic font atlases are captured before background generation; plugins never read them from a
  worker thread.
- Font Size is pixel based. Revisit it after changing texture resolution.
- Shadow is coverage-only. Use separate Text layers for independently colored/material shadows.
- Ribbon following reads the composite Custom channel below the Plugin layer, so unrelated Custom
  content can confuse the guide. Mask or isolate the guide.

## Fabric — Fuzz, Fiber & Fray

**Best for:** cloth finishing, wool, fleece, velvet, worn denim, lint, pilling, and broken garment
edges.

**Outputs:** Albedo, Roughness, Normal Control, and Detail Mask where those channels exist.

This generator does not replace a cloth weave. Place it above Cloth Texture or a painted/fill fabric
base. Begin with **Fabric Family** because Cotton, Wool, Denim, Velvet, and Synthetic bias the response,
then tune manually.

**Fuzz Density** controls coverage. **Fiber Frequency** controls the number of fine directional bands,
and **Fiber Direction** aligns them. **Direction Variation** adds waviness and cross-fiber breakup.
Judge direction in 3D: UV distortion can make a mathematically straight field look bent on the model.

**Edge Fray Amount** uses signed curvature to concentrate loose fibers on exposed geometry. It works
best on a mesh whose relevant garment edges are represented by geometry. It cannot infer a printed
seam or a painted panel edge that has no curvature. Use a layer mask when only cuffs, hems, elbows, or
knees should fray.

**Pilling Clusters** controls coverage of fiber balls; **Pill Size** controls their cellular frequency.
Pilling is usually localized by contact and wear. A uniform high value across an entire garment reads
as procedural noise, so combine it with a hand-painted mask.

**Fiber Color** should normally be close to the underlying textile. **Pill / Fray Color** can be lighter
for raised, abraded fibers or darker for damp/matted wear. Keep **Color Strength** restrained so the
base fabric remains recognizable. **Fiber Roughness** and **Fiber Height** establish the physical
response; if height creates sparkling outlines or lumpy cloth, reduce it before reducing density.

Gotchas:

- Curvature-driven fray can affect any convex fold, not only a cut cloth boundary. Mask unwanted
  folds rather than forcing Edge Fray Amount so low that true edges disappear.
- Micro-fibers need enough output resolution. At low resolution, raise apparent fiber size or omit
  the finest layer instead of generating unstable one-pixel noise.
- Combine with Cloth Texture in separate Plugin layers. This gives independent opacity and masks for
  weave versus surface aging.

## Metal — Rust, Oxidation & Corrosion

**Best for:** ferrous armor, weapons, tools, industrial props, old fasteners, and layered corrosion.

**Outputs:** Albedo, Roughness, Metallic, Ambient Occlusion, and Normal Control.

Set the broad distribution first. **Rust Spread** establishes overall oxidation, **Cavity
Concentration** gathers corrosion in concave and occluded areas, and **Edge Concentration** adds
oxidation around exposed convex edges. New damage often rusts at chips and edges; long-term stored
objects often accumulate more corrosion in gaps and water traps.

Use **Pitting** and **Pit Scale** for recessed corrosion, then set **Pit Depth**. Use **Flaking** and
**Flake Scale** for raised oxide, then set **Flake Height**. Pits and flakes should not share exactly the
same scale; real corrosion contains a hierarchy of broad stains, medium flakes, and fine cavities.

**Gravity Streaking**, **Streak Length**, and **Streak Frequency** create runoff. Set **Gravity
Direction** to the object's intended orientation. A weapon displayed horizontally but worn vertically
may need the direction chosen for its in-world use rather than its current editor pose.

**Fresh Oxide**, **Deep Oxide**, and **Streak Color** form the color hierarchy. Avoid one saturated
orange everywhere. Deep pits should generally be darker; exposed fresh oxide may be brighter; runoff
is often thinner and less opaque. **Oxide Roughness** should be high, **Residual Metallic** should be
low, and **Pit AO** should reinforce only deep corrosion. Rust itself is not metallic.

Recommended stack:

1. Painted or bare metal base.
2. Combat Scratches & Dents or Edge Wear to expose and deform metal.
3. Rust to oxidize damaged/cavity regions.
4. Dirtify for compacted grime that is not part of the oxide.
5. Separate masks for narrative areas such as wet lower panels or protected interiors.

Gotchas:

- Rust on aluminum, clean stainless steel, plastic, or fantasy non-ferrous materials is physically
  wrong unless the material story explicitly calls for a coating or contamination.
- Excess Pit Depth and Flake Height create a rocky surface. Establish color and roughness first, then
  add enough Normal Control to catch grazing light.
- World projection reduces UV seams, but runoff direction still needs to make sense for the prop.

## Dripping Corrosion

**Best for:** realistic water-driven oxidation beneath panel edges, seams, fasteners, recessed joins,
and exposed damage.

**Outputs:** Albedo, Roughness, Metallic, Ambient Occlusion, and Normal Control.

This generator detects convex exposed edges and concave or occluded valleys, broadens those sources
by **Corrosion Spread**, and traces eligible sources along the configured world-space gravity vector.
The default `(0, -1, 0)` follows standard Unity gravity. **Drip Length**, **Drip Width**, corrosion
spread, breakup size, and pit size are all meters under Unity's 1 unit = 1 meter convention.

Start with the physical scale controls. A small prop usually needs millimeter-scale drip widths and
pit sizes; architectural metal can use centimeter-scale breakup and longer trails. Then adjust Edge
and Valley Amount independently. Multi-octave breakup controls the broad oxide boundary, while the
separate pit field drives recessed Normal Control and AO. Crust Height raises dry flakes without
turning every covered pixel into a bump.

When compute shaders are available, Dripping Corrosion runs directly against GPU-resident mesh maps.
Its parallel CPU implementation is retained as a fallback. Dirtify and Edge Wear use the same GPU
path; other built-in CPU generators now parallelize independent rows and avoid redundant tile copies.

## Surface — Pores, Scratches & Micro Detail

**Best for:** subtle material breakup on skin, leather, plastic, stone, painted metal, wood finish, and
surfaces that otherwise look digitally perfect.

**Outputs:** Albedo, Roughness, Normal Control, and Detail Mask.

Choose the **Noise Type** by character: Perlin is soft and cloudy, Cell is more particulate, Voronoi
emphasizes cellular regions, Ridged creates crease-like structure, and Hybrid combines several kinds.
**Noise Levels** controls fractal octaves, **Fine Detail Strength** controls how much smaller octaves
contribute, and **Noise Amount** controls the overall base breakup.

The **Pores** section adds cellular recesses. **Pore Amount** is visibility, **Pore Density** is how
many qualify, **Pore Scale** is frequency, **Pore Size** is radius, and **Pore Depth** controls Normal
Control. Skin pores are small and shallow; leather pores can be broader; stone vesicles can be sparse
and deep. Do not use one setting for every material.

The **Directional Scratches** section provides amount, density, spacing/frequency, finite length,
width, depth, direction, and randomness. Direction should tell a manufacturing or wear story: brushed
metal follows machining, floor scratches follow traffic, and polished armor scratches follow blows
and cleaning.

**Noise Tint** and **Color Strength** should remain subtle for most materials. The most convincing
micro-detail often lives primarily in Roughness and Normal Control. **Base Roughness**, **Roughness
Variation**, and **Noise Height** coordinate that response.

Gotchas:

- This generator can quickly become a uniform “noise filter.” Use lower global amount and a mask to
  create quiet areas.
- Micro-detail above skin generators should be much weaker than the larger mottling, veins, and
  wrinkles below it.
- Fine one-pixel scratches may alias. Judge them at the final texture resolution and normal viewing
  distance, including animation.

## Skin — Veins, Bruising & Subdermal Variation

**Best for:** realistic human skin, creatures with human-like skin, undead, bruised characters,
subdermal vessels, freckles, pores, circulation variation, and age/detail passes.

**Outputs:** Albedo, Skin Color Mask, Roughness, Thickness, Normal Control, and Detail Mask.

Choose **Overlay Existing Skin** when the character already has a good skin foundation. This writes
sparse biological variation over the material below. Choose **Full Skin Layer** when the generator
should supply a continuous base color, roughness, and thickness. Full mode is useful for procedural
characters and tests, but a hand-authored hero skin will usually use Overlay mode.

### Skin foundation

Set **Base Skin Color**, **Mottle Color**, and **Redness Color** to a believable palette for the
character. These colors describe different biological signals, not simply dark, medium, and light.
Mottling can lean cool or desaturated; capillary redness should remain compatible with the base tone.

**Albedo Strength** controls visible color contribution. **Skin Mask Strength** controls the shader's
Skin Color Mask response and matters only when the material/shader consumes that channel. **Skin
Roughness** and **Base Thickness** establish the foundation in Full Skin mode.

### Veins and capillaries

Enable **Veins**, then set **Vein Intensity** before changing the network. **Vein Thickness** controls
network frequency and apparent width, **Vein Branching** introduces secondary vessels, and **Vein
Direction** gives the network a primary flow. **Vein Color** should normally be a subdued cool value,
not saturated blue. **Vein Depth** reduces the thickness response below vessels.

Veins should not be equally visible everywhere. Use a layer mask to favor temples, hands, inner arms,
feet, breasts, or thin creature membranes. Keep them quieter through thick tissue and heavily
pigmented regions.

### Bruising

Enable **Bruises**, then use **Bruise Amount**, **Bruise Spacing**, and **Bruise Size** to place
clusters. **Bruise Age** moves the palette from fresh red-purple toward healing green, yellow, and
brown. For story-driven injuries, generate the material character and then use a black layer mask to
paint in only the required areas.

Avoid scattering bruises uniformly over a character. Their placement should relate to contact,
impact, restraints, combat, or medical condition. Different bruise ages can be created with multiple
Plugin layers and separate masks.

### Mottling, spots, and freckles

**Mottling** supplies low-frequency variation. **Subdermal Spots**, Spot Scale, and Spot Size add
smaller surface or under-skin regions. Two spot colors plus **Spot Color Randomness** prevent every
mark from having identical pigment. **Spot Height** should be near zero for flat pigmentation and
raised only for an actual surface feature.

Freckles have independent amount, scale, and size. **Freckle Edge Fade (px)** shrinks and fades them
near the generated or painted boundary so clipped circles do not reveal the mask edge. This works best
when the control region has enough room for a natural transition.

### Pores, circulation, oil, wrinkles, and thickness

Pores use amount, scale, and depth. **Capillary Redness** introduces broad circulation variation.
**Oiliness** lowers roughness in selected procedural zones; **Oil Zone Scale** sets their size. Keep
oiliness localized and restrained or the character will look coated in plastic.

Fine wrinkles use amount, scale, and depth. They are a microsurface layer, not a replacement for
sculpted facial folds. **Thickness Variation** adds broad subsurface breakup.

Gotchas:

- Skin Color Mask and Thickness require a material shader that maps and uses them. If those channels
  show thumbnails but do not affect 3D shading, inspect the UMA material/channel mapping and shader.
- Veins, pores, freckles, and wrinkles all compete at high frequency. Approve each at normal camera
  distance and leave some areas quiet.
- Full Skin mode can cover authored color below it. Use Overlay mode or lower Albedo/channel opacity
  when the source skin should remain dominant.
- Different body regions often need different masks or separate Plugin layers rather than one global
  setting.

## Skin — Stubble Maker

**Best for:** beard and moustache stubble, five-o'clock shadow, shaved scalps, close-clipped hair,
follicle redness, razor irritation, pimples, and small pigment spots over an existing skin texture.

**Outputs:** Albedo, Roughness, Normal Control, Skin Color Mask, and Detail Mask. Every output is
feature-alpha-only: Stubble Maker never writes an opaque base skin. This is important when the result
will become an UMA overlay rather than a replacement skin.

### Facial hair and shaved-head profiles

Choose **Facial Hair** for the measurements exactly as authored. Choose **Shaved Head** to derive a
shorter, slightly finer, denser, and straighter result from the same controls. **Custom / Neutral**
retains the authored measurements without the scalp bias and is useful when the placement is neither
a face nor scalp.

Zero **Direction from Down** points toward texture-space down. Increase or decrease it to follow the
target UV layout. **Direction Variation** adds controlled angular differences; **Curvature** bends
the middle of individual hairs. Keep both restrained for recently shaved areas. Longer facial
stubble tolerates more variation.

**Length (px)** and **Width (px)** are destination-pixel measurements, so review them at the intended
export resolution. **Density** changes both population and spacing. **Tip Taper** controls the
root-to-tip silhouette, while Hair Opacity and Hair Roughness separate dark coverage from material
response. Hair Height writes a raised Normal Control value rather than an RGB normal.

### Randomness and placement

**Random Position X/Y** offsets each root by a bounded pixel amount. Length Variation, Width
Variation, Color Variation, Direction Variation, and Curvature use the saved Seed, so regeneration is
stable. Start with low values; maxing every random control produces fuzz rather than clipped stubble.

Placement is a rectangle or ellipse with normalized Center X/Y and Width/Height, pixel edge feather,
and rotation. It is deliberately UV-based because face and scalp hairlines are usually art-directed
against a specific skin layout. Use the optional Control Mask for a prepared beard/scalp map, or add
an editable layer mask afterward for cheeks, lips, nostrils, ears, eyebrows, and the final hairline.

### Shadows, redness, rash, pimples, and spots

**Stubble Shadow** places a soft color around and under each strand. A cool desaturated blue-gray can
suggest beard shadow beneath lighter skin; darker skin usually needs a subtler, palette-matched
value. Shadow Spread and Shadow Offset Down control softness and direction without baking scene
lighting into the base skin.

**Shaving Redness** is localized around deterministic follicle roots. Redness Radius and Redness
Color can represent a fresh razor pass or inflamed follicles. The separate **Rash** controls create
larger soft clusters. Pimples and pigment spots have independent amount, spacing, size, color, and
height; pigment spots should normally keep Spot Height near neutral.

These skin effects share the placement, edge feather, Control Mask, and Overall Amount. They can be
set to zero independently, leaving a hair-only alpha overlay.

### Saving as an UMA overlay

1. Generate Stubble Maker on a Plugin layer above the existing skin.
2. Inspect channel thumbnails and confirm empty areas remain transparent rather than skin-colored.
3. Refine the beard or scalp boundary with the Plugin layer's mask if needed.
4. Choose **Export Textures & UMA Assets...** and **Runtime Overlay (Transparent)**.
5. Enter an Export Identifier and export. Overlay Painter excludes the reconstructed base, unions
   authored channel alpha into the grayscale coverage texture, and assigns it to the new
   `OverlayDataAsset.alphaMask`.

Do not choose Flattened Composite for a reusable stubble overlay; that mode includes the underlying
skin. Saving the paint document preserves editability, while exporting creates the runtime overlay.

Gotchas:

- Facial and scalp UV islands may point in different texture directions. Use separate Plugin layers,
  placement regions, and masks when one direction cannot suit both.
- A one-pixel hair at low resolution can alias or disappear after scaling. Author and judge at the
  final overlay resolution.
- Strong black Albedo plus strong shadow double-darkens the beard. Establish strand color first,
  then add just enough shadow to join the follicles visually.
- Normal Control, Skin Color Mask, and Detail Mask only affect shaders/materials that consume them;
  alpha and Albedo still export normally.

## Skin — Scar, Wound & Stretch Marks

**Best for:** healed scars, fresh cuts, burns, stretch marks, surgical marks, creature wounds, and
precisely directed ribbon/path scars.

**Outputs:** Albedo, Skin Color Mask, Roughness, Thickness, and Normal Control.

### Choosing a guide source

- **Procedural Scars** automatically creates distributed scar lines. Use it for background damage or
  rapid variation.
- **Custom Ribbon Channel** reads the composed Custom channel below the Plugin layer. This is the best
  option for an art-directed hero scar.
- **Guide Texture** uses an assigned grayscale texture as the centerline/placement source.

For a ribbon-guided scar:

1. Create a Paint or Path layer below the scar Plugin layer.
2. Add a **Custom** channel to that layer.
3. Paint or draw the guide as white on black. A Path layer gives editable points, tangents, widths,
   2D/3D placement, and ribbon controls.
4. Select **Custom Ribbon Channel** in the scar Plugin layer.
5. Set **Scar Width (px)** and **Guide Threshold**, then Generate.
6. Edit the guide below and Regenerate whenever the shape changes.

The Custom channel is an authoring guide. It is not expected to shade like Albedo or export as a
standard runtime material texture.

### Damage type and placement

**Damage Type** selects Healed Scar, Fresh Cut, Burn, or Stretch Marks. It shapes the starting
material response but leaves manual controls available. Procedural mode adds frequency, direction,
path randomness, and edge-irregularity controls.

**Scar Width** is a pixel-space sampling radius around the guide. It changes with texture resolution.
**Guide Threshold** rejects weak guide values. If a soft guide disappears, lower the threshold; if
gray noise produces unintended scars, raise it.

### Age, color, and inflammation

**Scar Age** blends fresh damage toward mature healed tissue. Fresh Interior, Healed Interior, Scar
Sides, and Inflammation Color let the center, rim, and irritated halo respond independently.
**Inflammation Amount** should be high for a fresh or infected wound and low for an old pale scar.
Skin Mask Strength controls the shader-specific tint channel.

### Shape and material response

**Raised / Recessed** determines whether the center rises or sinks. **Center Height** is its magnitude;
**Rim Height** controls raised side tissue. Fresh cuts often have a recessed or open center with raised
inflamed sides. Hypertrophic and keloid scars can be raised. Mature flat scars should use subtle
Normal Control.

**Inside Roughness** controls the center; lower values make it shinier or wetter. **Side Roughness**
controls the rim, and **Healed Roughness** controls mature tissue. **Scar Thickness** and **Thickness
Change** modify the subsurface response through the center.

Gotchas:

- The plugin reads the composite below it. Put the Custom guide below the Plugin layer, not above it.
- A guide that is wider than the desired scar can create a broad plateau. Treat it as a centerline and
  let Scar Width build the tissue around it.
- Do not combine deep recession, high rim height, and very dark Albedo unless a severe open wound is
  intended. The signals reinforce one another quickly.
- Use multiple layers for injuries with different ages or material character.

## Creature — Scales & Skin Variation

**Best for:** reptiles, dragons, amphibians, monsters, alien skin, pebbled hide, armored plates,
freckles, mottling, age spots, and subsurface blotches.

**Outputs:** Albedo, Skin Color Mask, Roughness, Thickness, Normal Control, and Detail Mask.

Choose **Overlay Existing Skin** to add scales and variation over an authored creature surface.
Choose **Full Skin Layer** to generate continuous color and material response.

### Scale construction

**Scale Family** offers Reptile Plates, Pebbled, Overlapping Scales, Dragon/Armored, and Amphibian.
Set **Scale Size** in 3D first, then **Scale Amount** and **Scale Border**. Scale Size is a frequency:
higher values generally produce more, smaller cells. **Per-Scale Color Variation** gives deterministic
variation without random flicker.

Use **Scale Height** for faces and **Border Depth** for gaps. **Border Roughness** and **Scale Gloss**
separate dry recessed borders from smoother faces. **Scale Thickness** can reduce the subsurface
response of armored faces.

### Pigment and biological variation

Mottling works at medium scale. Subsurface Blotches and Blotch Scale establish broad under-skin
variation. Freckles and age spots have independent amount, scale, and size. Two spot colors and Spot
Color Randomness avoid cloned marks, while Freckle Edge Fade softens painted boundaries.

Primary Skin, Scale Variation, and Subsurface Blotch colors should form one palette. Highly unrelated
colors quickly produce camouflage-like noise unless that is intentional. Skin Mask Strength,
Roughness, Base Thickness, Blotch Thickness, and Detail Mask coordinate the shader response.

Useful approaches:

- **Young reptile:** smoother larger scales, narrow borders, low age spots, moderate gloss.
- **Old dragon:** broad armored scale family, stronger border depth, lower scale gloss, more age spots,
  rougher borders, and restrained cavity dirt on a separate Dirtify layer.
- **Amphibian:** lower scale amount, broader blotches and mottling, smoother roughness, strong
  thickness variation, little border depth.
- **Alien skin:** use Overlay mode over a painted palette, then use masks to assign different scale
  families with separate Plugin layers.

Gotchas:

- One cellular scale field cannot follow every anatomical flow. Use separate masked layers for face,
  torso, limbs, and armored regions when direction or size must change.
- Excess border depth makes clean cells look like masonry. Add irregular color and roughness before
  deepening every gap.
- Skin Color Mask and Thickness remain shader-dependent.

## Metal — Combat Scratches & Dents

**Best for:** armor, helmets, shields, weapons, vehicles, metal props, chipped coatings, impact bowls,
projectile pings, gouges, burrs, and glancing scrape bundles.

**Outputs:** Albedo, Roughness, Metallic, Ambient Occlusion, and Normal Control.

This generator creates feature-based damage rather than uniform noise. Start with **Wear History**:
Light Skirmish, Campaign Worn, Veteran, or Battle-Ruined. Treat it as a starting profile, then tune the
individual features. Choose **Bare Metal** or **Painted / Coated** so metallic output tells the correct
material story.

### Dents and pings

**Dent Amount** is intensity and **Dent Frequency** is occurrence. Minimum and Maximum Dent Size
create a useful range; avoid setting them nearly equal unless the manufacturing story produces
uniform impacts. **Dent Depth** recesses the bowl. **Dent Irregularity** breaks circles, while Raised
Dent Rim and Dent Rim Height create displaced metal shoulders.

Pings are small hard impacts, often nested with broader damage. Set Ping Amount, Size, Depth, Crater
Rim, and Rim Height. Very deep pings with large rims look like volcanic craters; keep their physical
scale appropriate to arrows, bullets, tools, or weapon tips.

### Gouges, scratches, and burrs

Scratch Amount and Frequency control occurrence. Minimum/Maximum Length prevent every mark from being
identical. Gouge Width and Depth shape the core; Dominant Strike Direction and Direction Variation
create a believable family of blows. **Travel Breakup** prevents perfect continuous lines. Raised Burr
Amount and Burr Height produce displaced lips beside deep cuts.

Glancing Scrapes create parallel track bundles. Scratches per Scrape and Scrape Spread control the
bundle. Convex Edge Concentration adds damage to exposed edges; Paint Chipping exposes the underlying
metal in impacts, gouges, scrapes, and edge wear.

### Material response

Exposed Metal, Recess/Embedded Grime, and Polished Burr colors distinguish fresh faces, compacted
damage, and burnished lips. Color Contribution determines how strongly these cover the finish.
Exposed, Recess, and Burr Roughness should differ: fresh/burnished metal is often smoother, while
embedded grime is rougher. Exposed Metallic should be high for real exposed metal. Deep Damage AO and
Embedded Grime reinforce cavities.

Gotchas:

- Use Painted/Coated for paint chipping. Bare Metal should not suddenly become metallic only inside a
  scratch because it was already metal.
- Convex Edge Concentration is broad procedural wear. If the story calls for one struck edge, mask it.
- Damage features are projected procedurally, not physically simulated against a specific opponent.
  Use a Path/paint layer for signature hero scratches and this generator for supporting wear.
- Add rust after damage if exposed metal should have oxidized; add a clean bright Edge Wear layer above
  rust for very recent abrasion.

## Agify — Dirt & Edge Wear

**Best for:** fast whole-model aging that coordinates cavity dirt and convex wear in one layer.

**Outputs:** Albedo, Roughness, Metallic, Ambient Occlusion, and Normal Control where available.

Agify combines signed geometry curvature, high-frequency curvature derived from the composed Normal
map, source/generated AO, optional projected textures, and deterministic fractal breakup. Use it when
dirt and wear should share one aging pass. Use Dirtify and Edge Wear as separate layers when they need
different masks, projection scale, ordering, or art direction.

### Establishing detection

Choose UV or World Triplanar projection and set **Texture Scale** for optional textures and masks.
**Curvature Contrast** sharpens or broadens both concave and convex selections. **Normal Detail
Influence** includes details from the composed Normal map; raise it when engraved or stamped normal
features should collect dirt/wear, lower it when fine normal noise triggers speckled selections.

**AO / Cavity Influence** strengthens dirt in occluded regions. **Procedural Breakup** and Breakup
Scale fragment broad coverage. **Fractal Edge**, levels, and persistence displace boundaries with
progressively finer damage. Set Fractal Edge to zero while judging curvature placement, then introduce
breakup.

### Dirt side

Dirt Amount controls coverage. Dirt Color multiplies the optional Dirt Texture, while Dirt Mask limits
placement. Dirt Roughness, Dirt AO, and Dirt Height establish a coordinated deposit. Dry dust is often
bright, rough, and shallow; wet grease may be darker, smoother, and have little height; packed mud can
be rough and raised.

### Wear side

Wear Amount controls convex chipping. Wear Color multiplies the optional Wear Texture, and Wear Mask
limits it. Wear Roughness defines the exposed finish. Exposed Metallic should be high only if a coating
has revealed real metal. Chip Depth recesses the worn coating through Normal Control.

Gotchas:

- Agify is convenient but couples dirt and wear projection/breakup. Split into Dirtify and Edge Wear
  when one side looks right and the other cannot be tuned without compromise.
- Normal Detail Influence reads the composed Normal channel below the plugin. A noisy normal can
  create noisy curvature.
- Fractal Edge is boundary displacement, not the same as broad Procedural Breakup. Use both
  deliberately.
- Optional texture alpha participates in coverage. An unexpectedly transparent texture can make the
  generator appear weak or empty.

## Dirtify — Gap Dirt

**Best for:** isolated cavity dirt, seam grime, dust in recesses, packed material around fasteners,
and controlled gap accumulation.

**Outputs:** Albedo, Roughness, Ambient Occlusion, and Normal Control.

**Gap Detection Level** sets how strong a concave/AO feature must be. Raise it to keep only the deepest
gaps. **Gap Size (px)** is the neighborhood radius used to find nearby features. **Dirt Spread** decides
how strongly a detected gap expands through that radius. **Dirt Level** is final coverage.

**AO / Cavity Level** adds source and generated AO to curvature detection. **Normal Detail Influence**
lets grooves from the composed Normal map participate. Fractal Breakup, Scale, Levels, Level Strength,
and Fractal Edge control islands and their boundary.

Dirt Color multiplies an optional projected Dirt Texture. Dirt Mask is an additional projected
grayscale limiter. Dirt Roughness, Dirt AO, and Dirt Height write the material response.

A good tuning order is Detection Level, Gap Size, Spread, Level, then breakup. Starting with heavy
breakup hides whether the underlying cavity selection is correct.

Gotchas:

- Gap Size is resolution-dependent pixels, while Texture Scale belongs to UV/world projection.
- Dirt will collect in every qualifying cavity, including details that are clean by design. Use a
  layer mask for protected, polished, or frequently handled regions.
- Large Spread can turn seam dirt into a general wash. Lower Spread before lowering overall Level if
  the centers are correct but the reach is excessive.

## Edge Wear

**Best for:** chipped paint, polished convex edges, rubbed corners, exposed metal, and abrasion on
armor or props.

**Outputs:** Albedo, Roughness, Metallic, and Normal Control.

**Edge Detection Level** restricts the result to stronger convex curvature. **Edge Size (px)** searches
around the detected feature, **Wear Spread** controls reach, and **Wear Level** controls coverage.
**Cavity Exclusion** prevents wear from spreading into protected concave regions.

Normal Detail Influence can recognize small edge detail from the composed Normal channel. Fractal
Breakup controls irregular survival across the selected region; Fractal Edge damages the boundary.
Wear Color multiplies an optional projected texture, and Wear Mask limits placement. Wear Roughness,
Exposed Metallic, and Wear Depth establish the revealed material.

Gotchas:

- Curvature finds exposed shape, not human contact. Handles, knees, elbows, buckles, and frequently
  touched areas may need additional painted masks even when they are not the sharpest convex edges.
- High Exposed Metallic is correct for chipped paint over metal and wrong for worn leather, wood, or
  plastic.
- A low Detection Level plus large Spread produces a generic outline. Increase Detection Level first,
  then tune Size and Spread.

## AO Variation Generator

**Best for:** lightweight deterministic fine breakup in an existing Ambient Occlusion channel and as
a simple Plugin API example.

**Output:** Ambient Occlusion only.

**Strength** blends from neutral white toward Perlin variation. **Frequency** controls the size of the
noise. The result multiplies the composed AO below the Plugin layer, so white leaves the source
unchanged and darker values add occlusion variation.

This is intentionally simpler than the production material generators. Use it for subtle breakup, not
for directional dirt, curvature-aware cavities, or a complete weathering material. Dirtify and Agify
are better choices when placement must follow geometry.

Gotchas:

- The target material must expose Ambient Occlusion.
- Strong AO noise can look like baked dirt and can double-darken physically lit cavities. Keep it
  restrained.
- Because it multiplies AO, it cannot brighten values already dark in the source.

## Working with filters

Create a Plugin layer immediately above the material content to process, select the filter, choose the
source and output channels, and Generate. The cached filter result is a normal layer. Its opacity is a
convenient final Amount control, and its mask can localize the correction without changing the source.

Filters read the full composite below their stack position. They do not read only the currently
selected layer, and they do not feed their old cache back into themselves. To chain filters, stack them
in order:

1. Source artwork or generated material.
2. First filter Plugin layer.
3. Second filter Plugin layer, which reads the first filter's composite result.

If the source and output channels differ, confirm the destination channel exists on the target
material. The filter result is constrained to the destination channel's semantics: grayscale data
stays grayscale and Normal output is normalized.

Levels & Curves, Blur/Sharpen/Detail, Channel Operations, and Morphology/Distance can also operate on
the active layer mask. Select the mask thumbnail, open **Mask Filter / Generator**, choose the filter,
and click **Generate Mask**. Source/output channel selectors are hidden in Mask mode because the mask
itself is both source and destination. Normal & Height Toolkit is intentionally unavailable for masks.

## Levels & Curves

**Best for:** tonal correction, contrast, remapping data ranges, balancing generated channels,
brightening/darkening masks, and reducing a procedural result without regenerating its source.

**Sources/outputs:** any available material channel; also supports Layer Mask mode.

**Input Black** maps all values at or below that point toward black. **Input White** maps values at or
above that point toward white. Bringing them inward increases contrast and clips the extremes.
**Gamma** adjusts midtones: values above 1 brighten the middle range; values below 1 darken it.
**Output Black** and **Output White** compress the final range without changing where the source
thresholds occur.

The **Master Curve** is evaluated after input levels and gamma, before output levels. Raise the curve
to brighten a range, lower it to darken, and use a gentle S-curve for contrast. **Amount** blends the
processed result with the input.

Enable **Preserve Hue / Adjust Luminance** when grading Albedo, Emission, or another color channel
without independently reshaping red, green, and blue. Leave it disabled for scalar maps and when an
RGB channel-by-channel color shift is intentional.

Artist uses:

- Tighten a broad Roughness generator without changing its pattern.
- Raise the black point of Normal Control so recesses become shallower.
- Increase contrast in a grayscale mask before Morphology.
- Reduce faded Albedo while preserving hue.
- Compress AO so it never reaches pure black.

Gotchas:

- Input Black at or above Input White creates an effectively collapsed range. Keep a meaningful gap.
- Strong clipping destroys recoverable texture information. Use the Plugin layer's non-destructive
  placement and keep the source below it.
- Preserve Hue can amplify color noise when the original luminance is extremely close to black.
- On Normal maps, use Normal & Height Toolkit instead; ordinary levels can break vector meaning even
  though output normalization may constrain later operations.

## Normal & Height Toolkit

**Best for:** correcting normal-map intensity or convention, deriving a tangent normal from grayscale
height, combining detail normals properly, repairing Z, and scaling Normal Control around neutral
gray.

**Source:** selectable material channel. **Output:** automatically Normal for normal operations or
Normal Control for Normal Control Strength. This filter does not support Layer Mask mode.

### Normal Strength

Scales the tangent X/Y components and renormalizes the vector. Values above 1 strengthen the apparent
slope; values between 0 and 1 soften it. A negative value reverses X and Y direction and should be used
only for a deliberate convention/effect. This does not create missing detail; it changes the slope of
detail already present.

### Reconstruct Z

Rebuilds the blue/Z component from X and Y and normalizes the result. Use it when a normal texture has
an invalid, flattened, or incorrectly stored blue channel. It cannot recover X/Y information that was
already lost.

### Flip Green (Y)

Converts between opposing tangent-space Y conventions. Use it when bumps appear as dents and dents as
bumps predominantly in the vertical direction. The operation itself flips Y. The separate **Flip Green
(Y)** checkbox can also flip applicable operations. For the dedicated Flip Green operation, that
checkbox is ignored so the filter does not apply an accidental second flip.

### Height to Normal

Reads source luminance as height and calculates a tangent-space Normal. **Strength** controls slope;
negative strength reverses raised and recessed interpretation. **Height Sample Radius** is measured in
output pixels: a larger radius responds to broader forms and suppresses very fine noise. Enable Flip Y
if the destination convention requires it.

Values below one pixel still use a one-pixel minimum sampling step.

Choose a grayscale source such as Normal Control, Custom, Detail Mask, or a prepared Albedo-derived
height. If the source is colored, luminance determines height.

### Combine Detail Normal (RNM)

Combines the source normal with an assigned Detail Normal using Reoriented Normal Mapping instead of
naive RGB addition. Set Detail Strength, independent X/Y tiling, X/Y offsets, and Detail Flip Y. RNM
preserves the base surface direction while orienting the detail relative to it.

Use a genuine tangent-space RGB normal as Detail Normal. A grayscale height image is not a normal; run
Height to Normal first if necessary. Check the import/convention by testing a known raised circular
feature before applying it across a character.

### Normal Control Strength

Scales grayscale height around neutral 0.5. Values above 1 increase both light raises and dark
recesses; values between 0 and 1 pull the map toward neutral. Negative values reverse raised and
recessed response. Select **Normal Control** as Source Channel for this operation.

Gotchas:

- If Normal Control Strength appears to do nothing or produces nonsense, verify Source Channel is
  Normal Control rather than the default Normal channel.
- Flipping Y twice returns to the original convention.
- Very high strength amplifies compression artifacts and seams.
- Height to Normal differentiates neighboring pixels. A flat constant height produces a neutral
  normal regardless of how bright it is.
- Normal Control is modified for live display/export; it is not itself an RGB normal texture.

## Blur, Sharpen & Detail

**Best for:** softening paint, denoising masks, preserving edges while smoothing, removing isolated
specks, sharpening authored maps, extracting detail, and directional streaking.

**Sources/outputs:** any available material channel; also supports Layer Mask mode.

**Radius (px)** controls spatial reach. **Amount** controls blend or strength. Direction is used by
Directional Blur, and **Edge Preservation** is used by Bilateral Blur.

### Gaussian Blur

Smooths in all directions with weighted sampling. Use it to soften hard paint, broaden a mask, reduce
fine noise, or make a procedural transition less digital. Large radii can erase seam and feature
definition.

### Directional Blur

Blurs along the specified angle. It is useful for motion-like wear, brushed finishes, streaked dirt,
or softening a pattern in one direction while retaining cross-direction detail.

Direction is texture-space. It may not align consistently across rotated UV islands. For physically
world-aligned streaks, prefer a generator's world-projection/direction controls.

### Bilateral Blur

Smooths similar neighboring colors while rejecting neighbors across stronger differences. Use
**Edge Preservation** to determine how readily unlike samples are excluded. Smaller values preserve
more edges; larger values allow broader smoothing across differences.

This is useful for denoising color or scalar maps without washing across every boundary. It is not a
substitute for UV-island-aware morphology when expansion must never cross adjacent islands.

### Median

Replaces a sample with the median luminance from a neighborhood. It is effective for salt-and-pepper
specks and isolated outliers. Radius changes sample spacing rather than creating an arbitrarily large
sample kernel, so use it as a cleanup tool rather than a broad blur. Median returns the selected
neighborhood median directly; the general Amount control does not blend this operation.

### Unsharp Mask

Adds the difference between the source and a blurred version back into the source. Radius determines
the detail scale; Amount controls enhancement. Start below Amount 1 and judge at normal viewing
distance. Strong unsharp settings produce halos and exaggerate texture compression.

### High Pass

Outputs detail around neutral 0.5 by subtracting a blurred version from the source. Radius determines
which frequencies remain and Amount controls contrast. High Pass is useful for deriving detail masks,
micro-height foundations, or an overlay that will be blended later.

Gotchas:

- Blur and sharpen are pixel-space. Their physical size changes with texture resolution and texel
  density.
- Filtering a Normal channel renormalizes output, but specialized Normal operations usually give more
  predictable results.
- High Pass is not final Albedo by itself; it is a neutral-centered detail representation.
- A very large Radius on a high-resolution target is computationally heavier and can flatten useful
  structure.

## Channel Operations

**Best for:** deriving one channel from another, grayscale conversion, packing/unpacking components,
inversion, tonal remapping, gradient coloring, selective color replacement, and subtle color breakup.

**Sources/outputs:** any available material channel; also supports Layer Mask mode.

**Amount** blends the operation with the source. When Source and Output Channel differ, the result is
constrained to the output type.

### Invert

Inverts RGB while preserving alpha. Use it to reverse Roughness/Gloss-like data, invert masks, or
reverse a grayscale control. Be certain the source convention actually needs inversion; some shaders
call a smoothness component “gloss” while the Overlay Painter channel is Roughness.

### Clamp / Remap

Maps Input Minimum/Maximum to Output Minimum/Maximum and clamps outside the range. Use it to isolate a
band of values, increase data contrast, keep Roughness away from extremes, or remap a mask into a
narrower useful range.

### Grayscale

Converts source RGB to perceptual luminance. Use it to derive a mask, Roughness starting point, Detail
Mask, or height guide from color art. Luminance is not physical material inference: a bright painted
region is not automatically smoother, thicker, or higher.

### Channel Shuffle

Each output component can come from source Red, Green, Blue, Alpha, Luminance, Zero, or One. Use it to
extract a packed map component, copy alpha into grayscale RGB, create opaque alpha, or reorganize data
before export.

When the destination is a grayscale channel, channel constraints collapse the result to its scalar
meaning. For complex RGB packing, choose an appropriate color/custom destination and verify the
export template.

### Gradient Map

Maps source luminance through Shadow, Midtone, and Highlight colors. It is useful for recoloring
grayscale dirt, stylized skin, heat/discoloration maps, or procedural masks into controlled Albedo.
Alpha remains from the input.

### Color Replace

Finds a color by RGB distance and blends toward Replace Color. **Tolerance** defines the accepted
range; **Softness** fades the boundary beyond it. Start with low tolerance and raise it until the
intended family is selected. High tolerance can replace unrelated colors of similar brightness.

### Color Variation

Adds deterministic multi-octave variation. **Variation** controls magnitude, **Scale** controls region
size/frequency, and **Seed** changes the arrangement. It adds the same variation direction to RGB, so
it behaves primarily as light/dark breakup rather than independent hue noise.

Gotchas:

- Deriving Roughness, Thickness, or height from Albedo is an artistic shortcut, not a physically
  guaranteed conversion. Refine the result afterward.
- Channel Shuffle operates on the selected source only; it does not combine components from several
  different source channels in one pass.
- Invert on an RGB Normal map is not a valid normal-convention conversion. Use Normal & Height Toolkit.
- Color Replace uses RGB distance, not semantic material regions. A mask gives more reliable hero
  placement.

## Morphology & Distance

**Best for:** growing or shrinking masks, feathering selections, creating outlines, finding edges,
building distance fields, and deriving bevel-like Normal Control from an existing shape.

**Sources/outputs:** any available material channel, with Normal Control as the default output; also
supports Layer Mask mode.

This filter thresholds source luminance into inside/outside regions, calculates pixel distance, and
uses the Surface ID mesh map to keep operations within the same UV island. Nearby islands cannot bleed
into one another even if they are only a few texels apart.

**Source Threshold** defines inside. **Distance / Radius (px)** controls reach. **Softness (px)**
controls transition width. **Invert Result** reverses the completed output.

### Dilate

Expands the white/inside region outward by Radius, with Softness at its new boundary. Use it to grow a
painted mask, create padding, or spread a generated feature.

### Erode

Contracts the white/inside region. Use it to remove fragile edges, eliminate tiny islands, or pull a
mask away from a boundary.

### Feather

Creates a soft transition across both sides of the original threshold boundary. Use it to remove hard
mask cutoffs without greatly changing the perceived center.

### Choke

Contracts the white region using mask/compositing terminology. In the current filter it shares the
same contraction profile as Erode; use either name according to the workflow being documented.

### Outline

Creates a band around the source boundary. Radius sets band reach and Softness controls its falloff.
Use it for borders, seam guides, edge accents, or as input to another generator/filter.

### Signed Distance

Stores the boundary at neutral gray, with opposite gradients inside and outside over the selected
radius. It is useful as a reusable procedural control, for custom shader work, or as a basis for bevel
and expansion effects.

### Edge Detect

Produces a narrow boundary signal. Softness controls edge width. Use it to derive seams, isolate
paint boundaries, or feed a later blur/levels pass.

### Bevel Height

Creates a neutral-centered height profile around the boundary. **Bevel Strength** sets direction and
magnitude; negative values reverse the bevel. Output to Normal Control for live normal modification,
or to another grayscale channel as an intermediate.

Gotchas:

- Radius and Softness are pixels, not world units.
- Island safety prevents cross-island bleeding but does not understand semantic garment regions
  inside one island.
- Thresholding discards continuous differences when deciding inside/outside. Use Levels first if the
  source boundary is too noisy or ambiguous.
- Very large radius on a small island saturates the entire region.
- Morphology treats source luminance as shape. Colored Albedo may need Grayscale or a prepared mask
  first.

## Stylization, Kuwahara & Quantization

**Best for:** painterly abstraction, hand-painted game art, posterized color, constrained palettes,
comic/toon bands, pixel-art-like dithering, simplifying noisy source imagery, and stylized masks.

**Sources/outputs:** any available channel; also supports Layer Mask mode. Scalar outputs are
constrained back to grayscale and Normal output remains subject to channel semantics.

Place the filter immediately above the source content and choose a Source and Destination channel.
Use **Amount** to mix the result with the original. Preserve Alpha should normally stay enabled for
Albedo decals and authored transparent content. In mask mode, source/destination selectors are
ignored and the result remains grayscale.

### Kuwahara Painterly

Kuwahara divides the neighborhood into directional sectors and selects the most coherent sector.
Unlike an ordinary blur, it smooths variation inside regions while protecting major boundaries. It
is excellent for turning photographic noise into broad brush-like shapes before a color reduction.

Start with Radius 3-7 px at final resolution. **Preview** evaluates four broad sectors,
**Production** evaluates eight directional sectors, and **Ultra** evaluates twelve broad and
center-biased sectors. Radius changes physical reach; Quality changes directional fidelity. A
tile-local summed-area implementation keeps cost effectively independent of radius. Detail
Preservation mixes source micro-detail back after selection.
Edge Sensitivity protects unlike regions. Increase it when colors bleed over silhouettes; decrease it
when the result remains too literal.

Kuwahara is deliberately more expensive than point filters. Work at Preview while placing a layer,
then switch to Production for approval. Ultra is for close-up hero maps with a visible benefit, not a
default. A huge radius cannot manufacture intentional brush design; combine moderate Kuwahara with
palette reduction or paint corrections.

### RGB and luminance quantization

**RGB Quantization** bands red, green, and blue independently. It can create graphic chromatic steps
but may introduce colors not present in the source. Levels controls bands per component. Gamma shifts
band density toward shadows/highlights, and RGB Band Bias changes where values cross a band.

**Luminance Quantization** bands perceived value. With Preserve Hue enabled, RGB is scaled together,
which usually produces cleaner painted materials. Disable Preserve Hue for a deliberate grayscale
result. Luminance quantization is also useful on Roughness, AO, Normal Control, and masks where the
data is already scalar.

### Custom palette

Set Active Colors from 2-8 and edit those palette slots. Each source pixel is assigned to the nearest
active color using a perceptual value/opponent-color distance. Arrange the palette from structural
darks through midtones to highlights. Include material-specific accent colors only if the source
should retain them. A palette is not a lighting bake: do not force black shadows and white highlights
into Albedo simply because they look dramatic in 2D.

For consistent assets, copy the same palette settings between Plugin layers/documents. Apply a small
Kuwahara pass before palette reduction when photographic noise produces isolated palette specks.

### Dithered quantization

Dithered Quantization adds a deterministic 4x4 ordered pattern before RGB banding. Dither Strength
controls how strongly neighboring pixels alternate. It is stable across regeneration and avoids the
temporal noise of random dithering. Judge it at 100% zoom with texture filtering representative of
runtime. Mipmaps and compression can soften or recolor a one-pixel pattern.

Use it intentionally: it works for retro/graphic art and smooth low-band gradients, but it is rarely
appropriate for realistic Roughness or Normal Control because the alternating material signal can
sparkle under motion.

### Toon bands and edges

Toon mode quantizes luminance while sampling source gradients for linework. Edge Width is the pixel
sample distance; Edge Threshold selects the required contrast; Edge Softness controls transition;
Edge Opacity and Edge Color control ink. Use a dark colored ink rather than absolute black when the
art direction calls for softer integration.

Source gradients include texture detail, not only geometry silhouettes. If every pore becomes ink,
run a small Kuwahara/blur layer below, raise Edge Threshold, or use a layer mask to limit linework.
For silhouette lines that must respond to camera/depth, use a runtime rendering solution; this filter
bakes texture-space edges.

Gotchas:

- Ordinary stylization is not a valid tangent-normal conversion. Use Normal & Height Toolkit for
  normal strength, convention, reconstruction, RNM, or height-to-normal work.
- Pixel radii change apparent physical size when export resolution changes.
- Palette/quantization can expose UV seams when islands contain different source values.
- Dithering is resolution-, mip-, filtering-, and compression-sensitive.
- Filters read the composite below themselves. Moving the Plugin layer changes the source.

## Production layer recipes

These are starting structures, not rigid rules. Separate Plugin layers preserve independent masks,
opacity, blend settings, and regeneration.

### Clean woven garment

1. Cloth Texture for weave, color, Roughness, and Normal Control.
2. Optional Pattern Sprite or ordered stripes within Cloth Texture.
3. Fabric Fuzz at low density and height.
4. Levels & Curves on Roughness if the finished material is too chalky or shiny.
5. Hand-painted layer mask on Fuzz to favor seams, cuffs, elbows, and hems.

### Worn denim

1. Cloth Texture using Denim with contrasting warp/cross-thread colors.
2. Thread-aware directional wear with moderate flattening.
3. Fabric Fuzz for pale raised fibers and localized pilling.
4. Edge Wear only if the garment mesh has meaningful convex seam/hem curvature; otherwise use a
   painted mask or Path layer for worn edges.
5. Surface Micro Detail at low strength for supporting scratches/fibers.

### Painted battle armor

1. Painted/coated metal base.
2. Combat Scratches & Dents in Painted/Coated mode.
3. Edge Wear for continuous convex abrasion.
4. Rust for older exposed damage; mask areas that remain clean or recently polished.
5. Dirtify for packed grime in joints and recesses.
6. Levels & Curves filters to balance Metallic, Roughness, or AO without regenerating feature layout.

### Old but non-combat metal prop

1. Metal base.
2. Surface Micro Detail for manufacturing scratches at a controlled direction.
3. Rust with stronger cavity concentration and runoff, lower combat-like edge damage.
4. Dirtify for dust/grease distinct from corrosion.
5. A hand-painted mask for handling zones that remain cleaner and smoother.

### Realistic human skin

1. Authored base skin, or Veins/Subdermal in Full Skin mode for a procedural foundation.
2. Veins/Subdermal in Overlay mode for mottling, circulation, oil, spots, and freckles.
3. Separate Veins/Subdermal layers for bruises of different ages, each with its own mask.
4. Scar/Wound layers guided by Custom-channel Paths.
5. Surface Micro Detail at restrained settings for final pores or fine wrinkles.
6. Per-channel opacity adjustments so Albedo, Roughness, Thickness, and Normal Control remain balanced.

### Creature hero material

1. Painted color foundation or Creature Skin in Full mode.
2. Separate masked Creature Skin layers for different scale families and sizes.
3. Veins/Subdermal for thin membranes or humanoid regions if appropriate.
4. Scar/Wound with ribbon guides for hero injuries.
5. Dirtify in scale gaps and Surface Micro Detail for fine breakup.

### Building a reusable procedural mask

1. Paint or generate a grayscale source below a filter Plugin layer.
2. Channel Operations > Grayscale if the source is color.
3. Levels & Curves to establish a clean threshold range.
4. Morphology > Dilate/Erode/Feather/Outline as required.
5. Blur/Sharpen for final softness or edge definition.
6. Use the resulting Plugin layer as a visible grayscale authoring channel, or run compatible filters
   directly in Layer Mask mode.

## Troubleshooting

### The generator produces no visible result

- Confirm the plugin has been generated and is not merely configured.
- Confirm the Plugin layer and its output channels are visible and have nonzero opacity.
- Confirm the target material exposes at least one declared output channel.
- Check whether a black layer mask, black Control Mask, transparent input texture, or very high guide
  threshold removes all coverage.
- For curvature tools, confirm the geometry has the concave/convex features being requested.
- For a filter, confirm the source content is below the Plugin layer and Source Channel is correct.
- Check whether another layer above it fully covers the result.

### The result changed after editing a lower layer

That is expected. The Plugin layer becomes Stale because its inputs changed. The previous cache stays
visible until Regenerate completes. Regenerate from the lowest dependent Plugin layer upward.

### The pattern has seams or changes scale

- Try World Triplanar for generators that support it.
- Inspect UV island rotation and texel density when UV alignment matters.
- Confirm mirrored islands are acceptable for directional patterns and text-like motifs.
- For Cloth Texture stripes, remember the layout is UV-space and repeat-cell based.
- Use separate masked Plugin layers when panels need different direction or scale.

### Normal detail looks inverted

- Determine whether the RGB Normal green/Y convention is wrong and use Flip Green.
- For Height to Normal or Normal Control, reverse Strength only when raised/recessed interpretation is
  wrong.
- Do not invert RGB Normal as ordinary color.
- Confirm the shader and exported texture use the same normal convention.

### Roughness does not affect the 3D view

- Verify the current UMA material maps the Overlay Painter Roughness channel to the shader property or
  packed component the shader actually consumes.
- Confirm the material copied from the character retains the relevant shader parameters.
- Check per-channel visibility, opacity, and blending.
- Remember that lighting and reflection environment strongly affect how roughness reads.

### Skin Color Mask, Thickness, or Detail Mask seems inactive

These are material/shader-dependent channels. The generator can author them, but the active shader must
map and use them. Inspect the UMA material channel definition and the character material. A valid
thumbnail proves data exists; it does not prove the shader consumes it.

### The result looks uniformly procedural

- Reduce Overall Amount or Plugin-layer opacity.
- Increase feature-size variation and separate large, medium, and fine scales.
- Turn off sections and reintroduce them one at a time.
- Add quiet areas with a layer mask.
- Use different seeds on separate masked layers rather than one high-intensity global pass.
- Make color, roughness, height, AO, and metallic tell the same physical story.

### Generation is slow

- High-resolution multi-channel targets require more work than one grayscale channel.
- Large-radius morphology and complex generators process native target resolution.
- Disable optional Cloth outputs that are not needed.
- Work at a representative resolution, approve placement, then regenerate final resolution.
- Cancel safely if the settings are clearly wrong; the previous cache remains intact.

### The layer is Stale immediately after moving it

Plugin output depends on stack position because it reads the composite below itself. Moving it changes
that input, so Stale is correct. Regenerate at the new position.

## Final quality checklist

Before export or beta review:

- Inspect every generated material at normal gameplay distance and in close-up.
- Rotate the light and model to judge Roughness and Normal Control, not only Albedo.
- Check UV seams, mirrored islands, and boundaries between logical/UDIM targets.
- Solo or inspect each output channel for unintended extremes.
- Confirm metallic materials and nonmetal materials remain physically distinct.
- Confirm Skin Color Mask, Thickness, Detail Mask, and packed outputs are actually mapped by the shader.
- Verify every Plugin layer is current rather than Stale.
- Save/reopen the document and confirm cached output, masks, guide textures, Sprites, stripe lists, and
  parameters restore correctly.
- Run a representative export and inspect the exported textures with their intended import settings.
