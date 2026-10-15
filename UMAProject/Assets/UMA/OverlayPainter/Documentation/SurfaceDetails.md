# Surface residue, welds, deposition and lace

These constructions are part of Overlay Painter. Editable presets and validation images are in the separate **Overlay Painter Examples** companion; automated tests are in **Overlay Painter Tests**. The current feature set requires Unity 6.3 or newer.

## Fingerprints, Smudges & Surface Residue

Choose **+Plugin → Generators → Fingerprints, Smudges & Surface Residue**. The default writes **Roughness only**. This lets an oily mark disturb a polished reflection without repainting armor, glass or leather. Enable Output Albedo for colored residue and Output Normal Control for subtle thickness. Output Metallic is optional and replaces metallic with zero only inside the mark; leave it off for very thin films on metal.

Five constructions are available:

- **Fingerprints:** whorl, loop and arch friction ridges; ridge count, partial contact, pores and bounded distortion. Ridge count changes detail without resizing the print.
- **Wiped smudges:** finite directional films with curved wipe streaks and uneven contact.
- **Dried water rings:** irregular evaporation outlines, secondary rings and fine deposits.
- **Salt deposits:** broken crystal deposits along an evaporation front. Use the Salt Crust preset for roughness, color and relief together.
- **Adhesive residue:** torn rectangular contact patches with peel breakup and raised remnants.

Mark Count adds deterministic marks around Center. Placement Scatter controls the spread; Mark Radius controls their size. Rotation and Wipe Stretch describe the marks, while Shape Variation and Contact Breakup introduce different kinds of irregularity. Seed changes their arrangement. Color and material changes keep the placement stable. Residue Roughness is the target roughness inside coverage, not an additive adjustment to the existing value.

Use the Placement Mask or an ordinary painted layer mask to keep marks on selected surfaces. A fingerprint needs enough final texels to resolve its ridges: fewer ridges or a larger mark is better than sharpening an undersampled print. At lower resolution the renderer averages fine ridges instead of allowing alternating black/white aliasing.

## Splashes, Smears & Drips

Choose **+Plugin → Generators → Splashes, Smears & Drips**. Blood, mud, paint and oil share a deterministic deposition model: irregular impact pools, tendrils, satellite droplets, directional wiping and runoff. The presets change color and wet/dry material response; all controls remain editable.

Impact Direction and Impact Stretch shape the impact. Satellite Droplets controls the surrounding drops. Smear Length and Smear Direction add drag marks independently of the impact angle. Drip Length and Runoff Direction control trails with rounded terminal drops. Smear/drip lengths are multiples of Mark Radius.

**Drying** preserves the deposit's silhouette while moving from Wet Roughness to Dry Roughness, changing pigment appearance and reducing liquid thickness. Thin edges dry ahead of thicker centers. Normal Control Relief is deliberately subtle for blood and oil; mud can stand higher. Metallic writes zero within coverage so opaque blood or paint does not inherit a metallic substrate. Switch outputs off when that is inappropriate.

### Mapping and gravity

- **UV patch:** Center and Radius are fractions of the 0–1 UV square. Down means UV -V. Use this for flat assets, deliberate artwork placement, or an unwrapped patch whose orientation you know.
- **World XY:** samples World Position X/Y. Zero runoff angle means world -Y, regardless of UV island rotation.
- **World ZY:** samples Z/Y, also with world -Y runoff.
- **World XZ:** horizontal planar deposition; runoff means a chosen direction on that plane, not gravity on a horizontal surface.

World Patch Span is in meters. Patch Offset is in patch units: the sampled coordinate is `world-plane position / span - offset`. Center then places the impact inside this square. Use a layer mask to isolate the receiving side of a model: planar mapping can affect multiple surfaces sharing the same projected coordinates. Out-of-patch positions are transparent. World modes require baked World Position and Surface ID maps and report a missing-map error rather than silently changing to UV mapping.

This is an artist-directed surface texture generator, not a fluid solver. Drips do not navigate around arbitrary folds or obstacles. World XY/ZY solve UV-orientation problems, while masks and additional layers provide precise placement on complicated geometry.

## Welds & Heat Discoloration

Choose **+Path → Generators → Welds and Heat**. TIG weld, MIG weld, solder seam and brazed seam are also available as native garment projections.

The path is the seam center. Path Width reserves space for the bead, surrounding heat tint and spatter. **Bead Width / Ribbon** controls the seam's width inside that region. **Feature Spacing** is bead pitch in meters on a 3D path (UV units on a 2D path). Bead Ripple gives overlapping crescent fronts; Irregularity varies their edge and spacing appearance, with a deterministic Seed.

Relief Height and 3D Depth control raised metal. Heat Tint and Heat Spread form the surrounding straw/purple/blue discoloration band, separately from Soot and Spatter. Seam Metal Color and Seam Roughness control the bead. Solder uses a smooth, clean profile; brazing uses a brass-colored seam. The tint is an artistic approximation of heat discoloration, not a temperature or alloy simulation.

Albedo, Metallic, Roughness, AO and Normal Control are coordinated. Direct Normal is off by default to avoid applying relief twice. Heat tint has neutral height outside the bead; spatter has its own rounded relief. Path masks, symmetry, endpoint fades, undo and saved presets use the existing native construction workflow.

## Lace & Decorative Openwork

Choose **+Path → Generators → Lace and Openwork** for Floral, Gothic or Scalloped Lace. Use a projection to apply an openwork patch.

Open spaces have actual zero alpha across the material outputs. They expose the layer below; they are not black or white painted holes. Thread Width controls the raised yarn; Border Scalloping shapes the outer trim. Netting adds fine diagonal connecting threads. Yarn Twist varies the thread surface. Thread Color and Thread Roughness are independent of relief.

Feature Spacing controls longitudinal repetition. The minimum motif pitch is 90% of ribbon width so reducing spacing cannot squash flowers and arches into bars. Closed paths fit a whole number of motifs around the loop. Path Width changes the overall trim width; point widths allow local tapering. Inspect at the final atlas texel size: tiny threads average into partial coverage and may need a wider trim or a larger output texture.

The generated lace is material detail on an existing surface, not floating geometry. For a visibly free-hanging lace silhouette, use a mesh/overlay supporting alpha and export the coverage using Overlay Painter's normal transparent overlay workflow.

## Presets, saved settings and validation

Install the Examples companion, then use **UMA → Overlay Painter → Examples → Create Surface Detail Presets** to recreate missing presets without replacing edited assets. Assets are under `Assets/UMA/OverlayPainterExamples/Surface Details`.

Material presets retain plugin IDs and typed parameter payloads. Native path/projection presets retain construction settings and use the existing document, history and symmetry code. Generators use local deterministic streams, never Unity's global random state. CPU material work runs on workers; native path constructions run in the existing GPU shaders. Outputs share geometry and coverage. Normal Control uses full-precision queued pixels for subtle film relief.

The test category is **SurfaceDetailGenerators**. Validation images and the measured results are supplied with the Examples companion; development reports are under `Build/Validation/SurfaceDetails`. Visual quality depends on actual footprint resolution, the receiving material and lighting. The examples demonstrate the implementation; they are not a claim that every parameter combination meets a production art target.

Reference products informed the requested feature categories: [High-Detail Smart Masks](https://www.artstation.com/marketplace/p/bp7Ar/high-detail-smart-masks-substance-painter), [Welds Tool & Filter](https://substancetools.artstation.com/projects/o6GoB), [Splashes Generator](https://www.artstation.com/marketplace/p/KXPpN/splashes-generator-for-substance-designer-or-substance-painter) and [Victorian & Gothic Lace](https://www.artstation.com/marketplace/p/A3Dm2/victorian-gothic-lace-pack-56-pbr-materials-4k-textures-unlimited-generator). No commercial files are included; these patterns are implemented procedurally in UMA.
