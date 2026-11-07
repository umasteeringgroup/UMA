# Tileable PBR Material Sprite Sets

For lit sphere previews and drag-and-drop layer creation, open the [Sprite Material Browser](SpriteMaterialBrowser.md).

Use **+Fill ▾ > Add from Sprite Set...**, choose a set ending in **Tileable PBR**, then choose a tile. All five material channels are assigned together. Choose Flat for UV tiling or Triplanar for surface projection. Adjust the fill's shared tiling to change the material scale. These sets also work with **+Path ▾ > Add from Sprite Set...** and the Quilt generator's Sprite Set input.

The library adds **112 original materials in thirteen sets** to the existing Cloth and Leather sets. Each material has a 1024 × 1024 tile, with matching sprite identities, rectangles and ordering across every channel. Most sets contain nine materials in a 3120 × 3120 atlas; Interlinked Chain Mail has four in a 2080 × 2080 atlas. Both use eight pixels of wrapped padding around each tile. The individual sprites tile; repeating the entire atlas is not the intended workflow.

The original 36 materials use **very minor aging**. Even the named rust, peeling, acid-wash and abraded variants keep their base material dominant: faint finish variation and sparse, tiny marks instead of large pale patches or damage islands. Add stronger wear with the aging, corrosion and mask generators. Material names and sprite identities are retained so existing layers keep their references.

## Contents

| Set | Materials |
| --- | --- |
| Aged Metals | Flaking Iron Rust; Blackened Forged Steel; Pitted Steel; Tarnished Brass; Verdigris Copper; Oxidized Bronze; Weathered Zinc; Chipped Industrial Paint; Oiled Brushed Steel |
| Woven Fabrics | Natural Slub Linen; Heavy Duck Canvas; Navy Cotton Twill; Olive Herringbone; Charcoal Wool Tweed; Taupe Basket Weave; Red Heritage Tartan; Black Ripstop; Cream Rib Knit |
| Aged Leather | Saddle Pebble Leather; Black Fine Grain; Worn Cognac Hide; Cracked Oxblood; Sand Suede; Black Patent; Distressed Buffalo Hide; Peeling Coated Leather; Embossed Reptile Leather |
| Denim | Raw Indigo Twill; Washed Mid Blue; Light Stonewash; Acid Wash Indigo; Black Denim; Faded Charcoal; Ecru Denim; Heavy Slub Indigo; Abraded Denim |
| Rubber and Elastomers | Smooth Vulcanized Rubber; Fine Pebble Rubber; Stipple Grip Rubber; Rounded Rib Rubber; Diamond Grip Rubber; Smooth Silicone; Neoprene Foam; Closed Cell EVA; Crosshatch Elastomer |
| Plastics and Coatings | Smooth ABS; Molded PBT Grain; Gloss Polycarbonate; Satin Polypropylene; Soft Touch Polymer; Fine Powder Coat; Gloss Enamel; Satin Ceramic Coating; Textured Molded Nylon |
| Knits and Soft Clothing | Fine Cotton Jersey; Classic Stockinette; Chunky Wool Knit; Braided Cable Knit; Double Rib Knit; Waffle Knit; Interlock Cotton; Moss Stitch; Fisherman Rib |
| Chainmail and Scale Armor | Steel Rings on Chestnut Leather; Brass Rings on Oxblood Leather; Steel Rings on Black Leather; Flat Rings on Brown Leather; Fine Fish Scale; Pointed Scale Armor; Scalloped Scale Armor; Laced Lamellar; Riveted Brigandine |
| Interlinked Chain Mail | Interlinked Round Steel; Riveted Flat Steel Chain; Fine Polished Steel Chain; Dark Oiled Steel Chain |
| Technical Weaves and Composites | Carbon Twill; Plain Carbon Weave; Forged Carbon Composite; Aramid Plain Weave; Carbon Aramid Hybrid; Ballistic Nylon; Seatbelt Webbing; Spacer Sports Mesh; Coated Glass Fiber |
| Brocade and Decorative Fabrics | Burgundy Damask; Navy Floral Jacquard; Olive Diamond Brocade; Ivory Paisley; Teal Rosette Brocade; Climbing Vine Jacquard; Gold Thread Arabesque; Silver Geometric Brocade; Indigo Wave Brocade |
| Wood and Natural Materials | Straight Grain Oak; Oiled Walnut; Open Grain Ash; Pale Maple; Ribbon Mahogany; Natural Bamboo; Dark Stained Oak; Natural Cork; Satin Beech |
| Bone Horn and Keratin | Clean Ivory Bone; Porous Bone; Warm Aged Bone; Amber Polished Horn; Black Horn; Striated Ivory; Dark Claw Keratin; Hoof Keratin; Pearly Enamel |

## Channels and import

The first four tiles in **Chainmail and Scale Armor** are opaque leather-backed ring mail,
with individual metal rings held down by paired retaining stitches. Their original imported
sprite names and IDs remain intact so existing layers update in place; the browser shows
the new descriptive names. **Interlinked Chain Mail** is a separate openwork set with
alternating ring planes, over-under crossings, rounded or riveted links, and transparent
holes. Neither set has added wear. Both ship sphere and cutout-cube browser previews.

Mail normals are baked from the visible ring surfaces. They avoid the artificial normal
cliffs produced by differentiating height at overlapping links. Height remains available
as an alternative; do not combine it with the supplied Normal for the same relief.

| Source | Convention |
| --- | --- |
| Albedo | sRGB straight color; chainmail and sports mesh include coverage alpha; no directional lighting baked into the image |
| Normal | Linear tangent-space RGB, OpenGL / positive green Y; normals derived from the same periodic height field |
| Roughness | Linear grayscale: black shiny, white rough. OverlayPainter packs smoothness as needed. |
| Metallic | Linear grayscale; dielectrics are zero; armor hardware and metallic decorative threads use metal masks. Rust, patina and painted areas reduce exposed-metal response. |
| AmbientOcclusion | Linear grayscale, one is neutral; restrained occlusion in weave gaps and crevices |
| Height (separate alternative) | 16-bit grayscale PNG source, sliced identically; not included in the five-channel Sprite Set |

All sheets are imported as Multiple sprites with no resizing, no compression and no mipmaps. Data sheets have sRGB disabled. Normal sprites deliberately remain ordinary linear RGB sprite textures so OverlayPainter can extract them without platform-specific normal-map packing. The source PNG height has 16-bit precision; Unity's Sprite importer can quantize the imported height texture according to its format. Use an appropriate high-precision importer when exporting height for another tool.

To use **Normal Control** instead of the supplied normal: remove/disable this fill's Normal channel, add Normal Control, and assign the corresponding sprite from `Height.png`. Begin with a low height strength and increase to taste. Do not apply both representations of the same relief. These are surface textures, not displaced geometry.

The atlas sheets are clamped; OverlayPainter extracts each sprite as a repeatable source. Padding wraps from the opposite side of the same tile, preventing adjacent material colors entering its filter footprint. Keep all channel tiling and projection settings synchronized. Most tiles are opaque, including Abraded Denim. Round/flat chainmail and Spacer Sports Mesh have antialiased transparent openings with identical coverage in all five maps. These openings reveal the layers below. To reveal actual empty space on a shell, use a transparent/alpha-clipped destination material and omit an opaque base overlay. Height is a separate grayscale source and does not carry alpha; use the Albedo silhouette as the layer mask if selecting that alternative.

## Clean material expansion

The eight additional sets provide **72 materials**. Rubber, plastics/coatings, knits, armor, technical weaves/composites, and decorative fabrics contain **no wear**. Wood/natural and bone/horn/keratin contain only sparse 1–3 texel micro marks, covering less than 0.2% of a tile. Their pores, grain, weave, and constituent composite flakes are material structure, not damage. Larger chips, cloudy corrosion, stains, and distressed patches are intentionally absent.

Each set has distinct surface construction and finish rather than just color swaps. Examples include paired yarn loops, ribs and cable patterns; tilted round and flattened mail rings; layered scales and leather-backed rivets; twill, aramid, webbing and open mesh; repeated floral/geometric motifs; and longitudinal wood/horn grain. Set a sensible physical repeat size using the shared fill tiling before adding wear generators.

The material catalog records the deterministic seed, normal strength, transparency, metallic policy, and measured micro-wear area per new tile. **Expansion Library Overview.png** shows the Albedo swatches; **Expansion PBR Overview.png** and the individual **PBR Catalog** sheets show all 72 materials rendered with their coordinated maps in Unity URP. Repeat sheets demonstrate wraparound. Albedo thumbnails deliberately do not bake the highlights needed to judge relief.

These textures cannot supply parallax, silhouette displacement, true ring geometry, fiber fuzz, anisotropic highlights, refraction or subsurface scattering by themselves. Their relief is suitable for textured surfaces; use geometry and an appropriate shader when those effects matter. Horn, silicone and polycarbonate here are opaque surface finishes. Metallic samples need environmental reflections in the destination scene.

## Regeneration and verification

Run the Python scripts from the Unity project root with numpy and Pillow installed:

```
python "Assets/UMA/OverlayPainterExamples/Tileable Materials/Authoring/generate_materials.py"
python "Assets/UMA/OverlayPainterExamples/Tileable Materials/Authoring/generate_expansion.py"
python "Assets/UMA/OverlayPainterExamples/Tileable Materials/Authoring/generate_mail.py"
python "Assets/UMA/OverlayPainterExamples/Tileable Materials/Authoring/validate_materials.py"
```

The first script regenerates the original four sets; the second regenerates the eight new sets. Both preserve other catalog families. `generate_expansion.py --family "Knits and Soft Clothing"` rebuilds one family. Keep the existing 1024px tile size when updating an installed catalog. Use the supplied `ImportSpriteSets.cs.txt` Unity editor script to import/slice through the supported Sprite Editor APIs; it preserves sprite IDs by name and updates Sprite Set assets without editing serialized asset files manually. The normal sprite importer keeps raw RGB, linear data and the shared coverage alpha intact.

Source validation checks all six maps, wrapped gutters, seam transitions, height-derived or geometry-surface normals as appropriate, transparency alignment, dielectric masks, and the wear policy. Unity regression tests check all thirteen sets' import settings, ordering, channel alignment, sprite resolution and wrapped gutters, plus openwork alpha. Test source is shipped only in the Tests companion; authoring scripts, images and reports are shipped only in Examples. Rebuild the OverlayPainter, Examples and Tests packages after changing maps or import metadata.

## Coverage and references

ArtStation's [30 Denim Fabric Smart Material Bundle](https://www.artstation.com/marketplace/p/xKwvj/30-denim-fabric-smart-material-bundle-4k-pbr-texture-vol-01), [Javad Rajabzade's fabric and leather collections](https://javad-rajabzade.artstation.com/projects/lDq85O), and [Marc Obiols' hard-surface collection](https://marcobiols.artstation.com/store/Apxe/sp-smart-materials-hard-surface-110-smaterials) informed the category coverage: denim washes, canvas, linen, twill, tartan, suede, damaged leather, oxidation and painted metals. Marketplace ratings are a popularity signal, not verified sales rankings.

These are original procedurally authored assets. No commercial textures or marketplace pack contents were copied. They are fixed material tiles rather than copies of those products' Substance graphs. Velvet, satin, fur, sequins and anisotropic brushed-metal shading require additional shader behavior and are not claimed here. Any tile can reveal repetition over very large areas; layer masks and generators can add larger, individually placed wear when wanted.

Assets live in `Assets/UMA/OverlayPainter/Textures/Tileable Materials`. Catalog images, repeat examples, validation reports and the deterministic Python authoring source are in the separate **OverlayPainter Examples** package. Regression tests are in **OverlayPainter Tests**. Python/numpy/Pillow are needed only to regenerate the sources, not to use the installed sprite sets.
