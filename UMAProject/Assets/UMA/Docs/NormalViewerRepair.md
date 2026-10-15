# Repairing normals between slots

Select a character with **UMANormalViewer**, assign its built DynamicCharacterAvatar if necessary, and expand **Fix Normals** in the inspector. This tool runs in Edit mode on the equipped slots.

1. Check the slots to compare and click **Scan**. Position tolerance is measured in shared slot bind-pose mesh units; the mismatch threshold is an angle in degrees. Only vertices used by each slot's selected submesh are compared. UV duplicates at the same position count as one shared position.
2. Each pair of slots with mismatches receives its own box. **Average vertexes** averages their normal directions with equal weighting per slot; it does not move vertices. **Use Normals from** copies the chosen slot's normal direction to both sides.
3. Enable **Smooth normals** to soften the transition into adjacent triangles on both slots. Neighbour rings control distance along the topology, and strength controls the blend. Shared seam normals remain fixed. Distant vertices are unchanged.
4. Click **Perform fix** for each group to queue its settings. Changing those settings unqueues the group until you click it again.
5. **View** rebuilds the current recipe using temporary slot copies. Original assets and equipped-slot references remain unchanged; no files are saved. **Apply all changes** saves the affected slot assets and rebuilds the character. **Cancel** discards all queued fixes and rebuilds with the originals. Leaving the inspector or entering Play mode cancels an unsaved preview.

Race-baked temporary slots are traced through their recorded source slot to the persistent shared base asset. Apply verifies that the current race bake matches, then subtracts the bake's unchanged blendshape-normal contribution from the repair. This preserves the intended normal direction after rebaking; the stored base normal magnitude may differ from one to preserve that additive result. The shared base change affects other races too. Cached baked copies receive the same source correction, including on Undo/Redo. A temporary slot without a resolvable source can be previewed but cannot be saved.

Apply writes only the affected slot assets through Unity serialization and supports Unity Undo/Redo. After undoing an applied change, save the modified assets to persist the undo. Preview does not modify source normals, so an unrelated asset save cannot accidentally commit a preview.

The scanner compares coincident positions, not bone names or screen positions. Slots must share the same bind-pose coordinate system. It does not repair missing geometry, displaced seams, mismatched skin weights, or blendshape deltas. Coincident overlapping surfaces can also be reported; select only the intended neighbouring slots. Renderer settings or later mesh modifiers that recalculate normals may override the result.

At a three-way junction, incompatible queued repairs are rejected instead of choosing whichever group happened to run last. Choose compatible source directions or apply one group and rescan. Changing source geometry/normals after scanning also requires a new scan.
