# Eyebrow examples

Install the OverlayPainter parent plugin and this separate Examples companion. Tests are in their own companion package.

## Editable shell projects

`Projects/HDEyebrows Groomed.asset` and `Projects/HDEyebrows Bushy.asset` target **UMA30_Eyebrows_slot**, the floating shell used by **HDEyeBrows_Thin_Wardrobe**. Equip that wardrobe recipe on a Human Female 3.0 or Human Male 3.0 character, open OverlayPainter, select the eyebrow slot and load the project. Alternatively open the project asset for standalone shell editing.

Each project has two independently editable UV paths. The base is transparent; the existing Thin overlay is used as a material/slot reference rather than baked into the new artwork. Select an eyebrow layer to see its yellow curve and red points directly on the shell in Scene view. Click a point to show green curve handles and the blue width handle; drag the point on the shell to move it. These edits retain UV placement and also appear in the 2D view. Add or insert UV path points in the 2D view. Use File → Save As before customizing bundled examples. The two sides use different seeds, and Layer Depth Contrast shades underlying hairs darker.

The reference shell's UMA material declares only Albedo, so these projects author Albedo with transparency. The generator also produces Roughness, Normal Control and AO when the target UMA material declares the corresponding channels; use a PBR-capable target for those outputs. Do not place the new texture over the existing eyebrow texture: replace that overlay's artwork. Exported alphaMask must retain the hairs' transparency.

The builder at **UMA → Overlay Painter → Examples → Create Eyebrow Shell Projects** recreates these named documents and their validation PNGs. A built equipped character is needed for the character captures. The separate face capture command demonstrates skin placement on an UMA3 face, without modifying the scene character's material assets.

See [the artist guide](../../OverlayPainter/Documentation/EyebrowPathGenerator.md) for controls and coordinate conventions. The validation report accompanies these examples once generated.
