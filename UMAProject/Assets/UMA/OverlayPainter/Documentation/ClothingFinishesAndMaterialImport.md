# Clothing finishes, automatic seams and PBR import

## Wetness and sweat

Use **+ Plugin → Generators → Fabric — Wetness & Sweat** above the clothing material.
Choose Rain-soaked Cloth, Sweat Patches or Dry Salt Residue. Amount controls the extent
of the patches; Absorption darkens the existing fabric, Drying reduces saturation and
introduces residue at patch boundaries, and Fiber Wicking breaks up those boundaries.
Surface Roughness changes the wet response. Relief is deliberately subtle.

Use a Control Mask to localize the effect: white applies it, black protects the fabric.
Texture alpha also contributes to coverage. A normal layer mask can further restrict
the result. World mapping samples a solid procedural field; Flat uses the garment UVs.
Rotation turns the procedural pattern. These are procedural material effects, not fluid
simulation. For automatic anatomical placement, use **Properties → Region Placement →
Armpits → Add Region Mask** and tune a shared clothing profile. See
[Shared anatomical and garment-region masks](AnatomicalRegionMasks.md).

## Leather and coated fabric

Use **+ Plugin → Generators → Fabric — Leather & Coated Fabric**. Profiles include Soft
Leather, Worn Leather, Suede and Coated Fabric. Start with a bundled preset, then tune
Grain Size, Creases, Dye / Coating Wear, color and roughness. Edge and cavity influences
include the lower stack's combined normal detail as well as mesh-map information.

The finish writes coordinated albedo, roughness and Normal Control. Metallic is optional
and disabled by default because these clothing materials are normally dielectrics.

## Print aging

Use **+ Plugin → Generators → Fabric — Print Aging** above the printed artwork. Assign
a **Print Coverage Mask** with white artwork on a black or transparent background.
This mask is required: the generator does not guess which parts of an opaque garment
are printed ink. Include the artwork's holes and transparent areas in the mask.

Choose Cracked Screen Print, Faded Transfer or Peeling Rubber Print. Exposed Fabric
Color defines the backing revealed by damage. Match it to the clothing underneath.
This first version reveals a chosen backing color, not an arbitrary hidden material
stack. Crack Width, Color Fade, Amount and Relief control the aged result. All enabled
channels use the same mask coverage, including Metallic if enabled.

## Reusable material presets

The **Clothing Finishes** preset folder contains:

- Rain-soaked Navy Cotton
- Sweat-darkened Jersey
- Dried Salt on Charcoal Cotton
- Chestnut Soft Leather
- Weathered Brown Leather
- Sand Suede
- Black Coated Fabric

Apply these through **File → Material Preset → Apply**. Wet-cloth presets contain a
fabric foundation and a separate moisture/residue layer, so the finish can be masked
or tuned independently. The presets retain generator definitions and regenerate for
the target. Preview thumbnails are illustrative swatches; inspect the actual garment
under your scene lighting before judging scale or roughness.

**UMA → Overlay Painter → Create Missing Clothing Finish Presets** restores missing
examples without overwriting existing authored presets.

## Automatic editable seam paths

Open **+ Path → Generators → Automatic Seams from Boundaries**. Choose UV Island Borders
or Mask Contours, then select a stitch or seam construction. Mask contours include holes
and respect alpha. Adjust the threshold and choose the listed boundaries to convert.

Inset, path width and simplification are in UV units. Positive inset moves into the
selected region; sharp corners use a limited miter. A UV cut does not necessarily
represent a sewn seam, so select only the relevant contours. Stacked/mirrored duplicate
UV triangles are traced once.

Each selected contour creates a normal editable path layer. Its initial curve uses
straight segments to preserve corners; edit points, enable curve handles, or change
the path generator afterward. Path width inherits UV distortion; this is not a physical
millimeter seam offset. Large insets on narrow or concave regions may need manual edits.

## Importing a PBR texture set

Open **File → Import PBR Texture Set**, also available under **UMA → Overlay Painter**.

1. Choose a folder containing one material's texture set.
2. Review the proposed channel for every file. Unknown files remain Ignore.
3. Resolve duplicate assignments, and confirm whether normals use DirectX.
4. Save to a new asset name inside Assets.

The importer creates an OverlayPainterSpriteSet, a reusable Fill Material Preset and
a separate folder of imported maps. It copies source files and never changes the originals.
Color maps use sRGB; data and normal maps do not. Normal conversion flips green only
when requested. Smoothness is converted to roughness.

| Packing | Interpretation |
| --- | --- |
| ORM / ARM | R = occlusion, G = roughness, B = metallic |
| RMA | R = roughness, G = metallic, B = occlusion |
| Unity Mask | R = metallic, G = occlusion, A = smoothness; B is not imported |

Every output map uses the albedo's alpha coverage. Packed smoothness alpha is data,
not coverage. Without an albedo map, outputs are opaque. Imported height uses an EXR
Normal Control map to preserve numerical precision; ordinary material maps use PNG.
Height is interpreted as the existing relative Normal Control field, with 0.5 neutral,
not as world-unit displacement. Review or remap differently encoded source height first.

## Mesh-map accuracy and relief behavior

Mesh AO now samples geometry with a triangle acceleration structure. Thickness measures
the distance to actual opposing triangles in world units; an open surface without a hit
returns zero rather than an invented bounding-box thickness. The current automatic bake
uses the target surface mesh, 32 cosine-weighted AO samples and a radius relative to its
bounds. It does not yet provide high-to-low cage baking or neighboring-slot occluders.

Inspect maps through the scene toolbar's **Plugin Mesh Maps** previews. Use **Rebuild
Cached Maps**, then regenerate stale layers after upgrading. A draft-resolution cache
is upgraded when a higher-resolution bake is requested.

New generator Normal Control channels default to Overlay, so neutral gray preserves
existing relief. Explicitly authored blend modes on existing layers are retained. This
also means older layers set to Normal are not silently changed: choose Overlay when
you want additive detail. Normal maps and the combined height field continue through
the shared normal compositor. Albedo, roughness and metallic remain independent of
Normal Control; new channels start enabled at full opacity.
