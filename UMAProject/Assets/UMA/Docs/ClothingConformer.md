# UMA Clothing Conformer

Add `UMAClothingConformer` to a fully built `UMADynamicAvatar` or `UMAData` root. The component lists active slots after the avatar has been generated.

1. Check the clothing slots to conform. Optionally check only the body slots that should be used as the binding surface; otherwise every non-clothing slot is used.
2. Select **Bind Selected Slots**. A `ClothingBindData` asset is created for each clothing slot, containing triangle barycentrics, four-vertex fallback weights, normal offset, and topology hashes.
3. Change the body's blendshape or DNA and select **Conform Selected Slots**. Preview updates the selected slot range immediately on a temporary clone of UMA's generated `SkinnedMeshRenderer` mesh. It also records a UMA per-slot vertex override for a later rebuild, while preserving blendshape weights that were set directly on the renderer.
4. Save the result as new slot assets or as a slot blendshape. Existing shared slot assets are copied before a blendshape is added when another active slot on the avatar uses the same asset.

Use `Additional Normal Offset` to move every conformed clothing vertex along the side of its mapped body surface where the garment was originally bound. Positive values create more clearance; negative values pull the garment closer to the body. Collision correction uses that same per-vertex direction, so it remains correct for body slots with inward-wound triangles.

The conformer detects base and clothing topology changes and refuses to use an invalid mapping. Use **Rebind Selected Slots** after changing a slot mesh. Magenta points in the Scene view indicate vertices that were too far from the chosen body surface to bind.

`Preserve Welded Seams` is enabled by default. It detects nearly coincident UV- or hard-normal-split vertices that are not connected by a triangle edge and gives them a shared conform displacement. This keeps texture-island seams closed without merging their UVs or normals. Increase `Welded Seam Tolerance` only slightly when an exporter has introduced tiny positional differences at a seam.

The menu command **UMA > Clothing Conformer > Demo Bind First Clothing Slot On Selected UMA** configures a built UMA with its first active slot as surface and second as clothing, then runs Bind and Conform. It is intended as a quick smoke test for projects that already have a body-and-clothing avatar scene.
