# Sprite material browser

Open **View > Sprite Materials…** in Overlay Painter, or **Window > UMA > Overlay Painter Materials**.
Choose a **Category**, then a **Sprite Set**. The grid flows to fit the dock and scrolls vertically;
**Size** changes thumbnail size. Hover over a preview to see the material's name.
Fill Material shows spheres; Projection and Path show alpha-cutout cubes rotated 45° on
each axis. Switching categories selects the corresponding cached preview automatically.

Drag a sphere onto the currently selected paint target in the 3D Scene view:

| Selected category | Drop result |
| --- | --- |
| Fill Material | A new Fill layer with the selected material's channel sprites |
| Projection | A new Projection layer, placed and aligned to the surface at the drop point |
| Path | A new ribbon Path layer with its first point anchored at the drop point; Shift-click to continue |

The selected category determines the action; there is no separate layer-type selector. Other
visible slots are reference geometry and do not accept drops. All compatible channel sprites,
including inversion settings, are assigned together through the painter's existing layer workflow.
Creation participates in the normal document history and linked UDIM layer handling.

Dropped Fill layers start with shared X/Y tiling of `2048 / sprite width`, rounded to the
nearest whole number (minimum 1). For example, a 512-pixel sprite starts at 4 and a
1024-pixel sprite at 2. Width comes from the Albedo sprite, or the first available channel
when there is no Albedo, rather than from the full atlas. Tiling remains editable afterward.

## Sprite sets and categories

Select an `OverlayPainterSpriteSet` asset to edit its **Categories**. A set can belong to more than
one category. The fixed usage names are **Fill Material**, **Projection**, and **Path**, exposed in
code as `OverlayPainterSpriteSet.UsageTypes` and individual string constants. Existing sets default
to Fill Material.

Each channel can use a sliced **Sprite Sheet**, or an ordered **Sprites** list for individual files.
When the list is populated, it takes precedence over the sheet. Keep corresponding materials in
the same order in every channel. Names and thumbnails use this same order.

The optional Examples package includes **OPS Armor Stamps and Corners**, containing all 46 OPS
armor decorations and corners in the Armor Stamps folder. It belongs to **Projection** and **Path**
and supplies **Normal Control**. It creates relief without replacing the underlying Albedo.
Use **UMA > Overlay Painter > Examples > Create Armor Stamp Sprite Set** to rebuild its membership
after adding more OPS sprites.

## Sphere and cube thumbnails

Each material has stored 256-pixel sphere and cube thumbnails rendered with the installed
**Universal Render Pipeline/Lit** shader. The preview combines Albedo, Normal, Metallic, Roughness
(converted to smoothness), Ambient Occlusion, Emission and Normal Control height. Channel inversion
is honored. A neutral surface is used when no Albedo is supplied, so height-only stamps remain
visible. Skin-specific masks and other custom channels have no equivalent in URP Lit.

Cube previews use URP Lit's alpha clipping. Coverage comes from Albedo alpha, or the first
available channel's alpha for height-only stamps.
This reveals transparent openings without displaying a solid rectangular background.

Thumbnails use a lit preview scene with a studio reflection probe. They do not modify source
textures, import settings, the current scene or its lighting. They are cached as subassets of the
sprite set. Source changes are detected when the set is viewed; rendering is queued one thumbnail
per editor update. Existing thumbnails remain visible while replacements are generated.

Use **Rebuild Thumbnails** in the browser, **Rebuild Sphere and Cube Thumbnails** in the set inspector, or
**UMA > Overlay Painter > Rebuild Sprite Material Thumbnails** for every installed set.
URP Lit must be installed for thumbnail generation. Cached thumbnails remain usable without it.

## Default layout

The default compact layout places Materials between the left Layers/Properties stack and the
Scene view. **Overlay Painter Material** is open and selected in the Properties tab group and
follows the current paint target. Existing saved layouts are retained; reset to the default layout
to adopt the new arrangement, then save it if desired. The Materials window also participates in
Save Layout / Restore Layout.
