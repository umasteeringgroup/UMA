# Armor decoration stamps

46 individual, non-tileable white stamps for OverlayPainter. Search the Project window for
`OPS- t:Sprite`, or `OPS-Corner- t:Sprite` for the 17 corner decorations.

Each decoration uses one 1024 x 1024 RGBA PNG. Visible pixels are pure white; empty source pixels
are transparent black (RGBA 0,0,0,0). Smooth alpha edges and clear internal openings define the
stamp. There are no separate albedo, roughness, metallic, or normal maps.

## Use

1. Add a Paint or Projection layer and choose one of the `OPS-` sprites as its texture/sprite source.
2. Use Albedo for painted markings, tinting the white stamp with the channel multiplier.
3. Use the same sprite in Normal Control for raised or recessed armor decoration. Start with a
   modest Height Strength, then use Invert Height for engraving. A small Blur amount can soften
   the edge where needed; avoid excessive height on tiny details.
4. Add metallic or roughness channels when the stamped region also needs a different finish.
   These stamps supply the shape; the layer supplies the material values.

The icons are flat stamps, not sculpted relief maps. Normal Control provides the edge relief;
the PNG does not encode a rounded 3D volume inside each white shape.

## Corners and 45-degree accents

All `OPS-Corner-` stamps have a lower-left elbow, arms extending up and right, and an open
upper-right area. Their alpha is mirrored exactly across the inward diagonal so the horizontal
and vertical arms match. Rotate by 45 degrees to create a balanced V-shaped accent; additional
90-degree rotations put it above, below, or beside a central icon. Use a separate layer for the
corner and the central stamp to adjust their size and spacing independently.
Corners have extra transparent padding so rotating their texture 45 degrees does not clip the
outer tips. Their artwork spans up to 640 pixels within the 1024-pixel canvas; other stamps span
up to 840 pixels. Increase the corner layer's size if you want it to surround a larger emblem.

The corner collection includes acanthus, laurel, ivy, oak, fern, Celtic square/curved knots,
Celtic braid and triskele, French lily, and French rococo.

Six fuller designs named `OPS-Corner-Filled-` extend ornament into the triangular interior:
Scroll Plate, Celtic Knot, Ivy Cluster, Acanthus Fan, French Rocaille, and Palmette Rosette.
Use these for broad corner plates or substantial accents around a central emblem. They retain
transparent openings and the same matching arms and rotation padding as the lighter corners.

## Import and distribution

Imported as centered, full-rectangle Single sprites, with input alpha, bilinear filtering,
Clamp wrapping, no mipmaps, and uncompressed textures. Alpha Is Transparency is enabled for
Unity's transparent-edge color dilation; the source PNG keeps transparent-black empty pixels.
The full rectangle retains padding and a consistent pivot when used by a projection.

These assets are located inside the separate **Overlay Painter Examples** companion package.
The package builder includes this folder on the next examples-package build.

## Artwork and references

Artwork was generated individually with the built-in image generation tool, then cleaned and
normalized with a user-approved scripted alpha pass. Corner arms were matched across the diagonal.
`Prompts.json` records the production prompt and each subject for future variations.

ArtStation was consulted for the kinds of ornaments useful on armor: heraldic animals, wreaths,
floral scrollwork, and interlaced Celtic decoration. No marketplace artwork was downloaded or
incorporated into the textures. Reference pages:

- [Medieval heraldic alpha collection](https://www.artstation.com/marketplace/p/6YgYl/7000-hand-painted-alpha-medieval-heraldic-symbols-elements-and-patterns-mega-pack-vol-7)
- [Celtic ornaments and knots](https://navidpro.artstation.com/store/6YL5y/115-celtic-ornament-symbols-knots-4k-alpha_pattern-pack)

The Prussian eagle is a decorative historical-style interpretation, not a certified reproduction
of any particular dated coat of arms.
