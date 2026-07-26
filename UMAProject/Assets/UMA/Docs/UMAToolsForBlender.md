# UMA Tools for Blender

UMA Tools is a Blender sidebar add-on for preparing meshes, rigs, weights, UDIM materials, and FBX files for UMA. It gathers the most common UMA cleanup and export tasks into one panel, checks for several problems that can break a slot in Unity, and exports with a consistent UMA FBX preset.

For an artist, the main benefit is repeatability. Instead of remembering a long list of Blender settings for every asset, you can use the same validation and export path for bodies, clothing, hair, and accessories.

The supplied script declares support for Blender 4.5 and appears in:

`3D Viewport > Sidebar (N) > UMA Tools`

## Get and Install UMA Tools

Download the latest `UMAToolsNPanel.py` from the UMA Content Pack:

[UMA Content Pack 3.0 Scripts on GitHub](https://github.com/umasteeringgroup/content-pack/tree/master/ContentPack_3.0/Scripts)

To install it:

1. Download `UMAToolsNPanel.py` from the GitHub folder.
2. In Blender, open `Edit > Preferences > Add-ons`.
3. Open the add-on menu and choose `Install from Disk`.
4. Select `UMAToolsNPanel.py`.
5. Enable the `UMA Tools` add-on.
6. Return to the 3D Viewport and press `N` to open the sidebar.
7. Select the `UMA Tools` tab.

The current tool version is displayed at the top of the panel. When updating the script, confirm that the version shown in Blender matches the version you downloaded.

## Why Use It

UMA content depends on details that are easy to overlook in a general-purpose Blender workflow:

- Meshes must use the expected armature and bone names.
- The armature hierarchy must have the UMA root structure.
- Objects should not carry accidental transform offsets into the FBX.
- The armature should be in its neutral pose.
- Weight data must be normalized, intentional, and attached to valid bones.
- FBX axes, scale handling, object types, modifiers, and animation options must be consistent.

UMA Tools addresses those risks in two ways:

1. **Error checking** reports common scene problems before export and provides focused repair actions.
2. **UMA Export** uses a fixed FBX configuration so different artists and assets do not depend on remembered exporter settings.

This makes the Blender-to-UMA handoff far more predictable. The tool guarantees the settings and repairs it performs; it cannot guarantee artistic quality, correct topology, complete weights, clean UVs, or compatibility with the wrong race. Always test the resulting slot against the intended UMA race in Unity.

## Recommended Workflow

For the most reliable result:

1. Work against the exact UMA race body and skeleton that the asset must support.
2. Complete modeling, UVs, skinning, and any required blendshapes.
3. Open **Error checking**.
4. Disable **Visible Only** when preparing a complete scene export so hidden UMA meshes and armatures are also checked.
5. Click **Check for Errors**.
6. Resolve every relevant report item.
7. Check the mesh at useful animation poses, then return all armatures to the neutral pose.
8. Run **Check for Errors** again.
9. Select only the required mesh and armature when practical.
10. Use **Export FBX (Selected)**.
11. Import the FBX into Unity and verify it with UMA Slot Builder.

The error checker is not automatically run by the export buttons. A clean validation pass followed by UMA Export is the intended workflow.

## Error Checking

The **Error checking** section is the preflight area. It checks either visible UMA objects or all mesh and armature objects in the scene.

### What Check for Errors detects

**Check for Errors** reports:

- **Unapplied transforms** on mesh or armature objects.
- **Armatures with active pose transforms** instead of the rest pose.
- **Incorrect armature object name**. The UMA armature object must be named `Root`.
- **Incorrect top-level hierarchy**. The armature must have exactly one top-level bone named `Global`.
- **Incorrect Global child**. `Global` must have exactly one child named `Position`.
- **Meshes without an Armature modifier**.

These checks target failures that commonly cause rotated, offset, unskinned, or incorrectly rooted slots after import into Unity.

### Error-checking controls

- **Visible Only**: skips hidden mesh and armature objects. This is useful while working on one part of a large file. Turn it off for final scene validation.
- **Check for Errors**: scans the selected scope and replaces the report with the current results.
- **Select All**: selects every mesh and armature in the scene and makes the first armature active.
- **Apply Transforms**: applies location, rotation, and scale to the selected mesh and armature objects that need it. It does not apply an armature pose as the new rest pose.
- **Insert Global/Position bones**: validates or creates the `Global > Position` root chain on the active armature. Existing top-level bones are placed under `Position` when a new chain is required.
- **Fix all missing Armature**: adds an Armature modifier to every mesh reported as missing one and assigns the scene armature object named `Root`.
- **Fix all transform items**: applies transforms to every transform problem in the current report.
- **Report**: lists each detected problem. Run the check again after manual corrections to confirm that the report is clean.

### What the checker does not validate

The checker is deliberately focused. It does not prove that:

- Every vertex is weighted.
- A mesh uses only bones available to the target race.
- Weight deformation is visually correct.
- UVs, normals, tangents, materials, or blendshapes are production-ready.
- A garment fits every DNA shape.
- The Unity model importer and Slot Builder settings are correct.

Use the report as a strong export preflight, then complete the visual and Unity-side checks described in [Content Creation](ContentCreation.md).

## Rigging and Weights

This section provides common skinning cleanup operations.

- **Reset pose transforms**: clears pose transforms on every armature in the scene and returns the bones to their rest-pose transforms. Use this before the final error check and export.
- **Copy Weights Mirrored**: copies weights from selected vertices to exact mirrored vertices across the local X axis. Vertex groups beginning with `Left` and `Right` are mapped to their opposite names. Vertices without an exact mirror are skipped.
- **Remove negligible weights**: removes selected-mesh weight assignments at or below `0.001`. This clears tiny accidental influences that can retain unnecessary bones or complicate skinning.
- **Source**: chooses the reference mesh used by **Copy weights to all selected**.
- **Smooth weights**: performs a gentle neighbor-based smoothing pass after copied weights are applied while preserving each vertex's original total weight.
- **Mapping**: chooses how Blender's Data Transfer operation finds corresponding source vertices.
- **Copy weights to all selected**: clears the target meshes' existing vertex groups, creates the source's used groups, transfers the weights, optionally smooths them, and removes empty groups.

### Weight mapping choices

- **Topology**: use when source and target have identical topology and vertex order.
- **Nearest Vertex**: maps each target to the closest source vertex.
- **Nearest Edge Vertex**: uses the closest vertex of the closest edge.
- **Nearest Edge Interpolated**: interpolates from the closest point on the closest edge.
- **Nearest Face Vertex**: uses the closest vertex of the closest face.
- **Nearest Face Interpolated**: interpolates from the nearest point on the nearest face. This is the default and a useful starting point for fitted clothing.
- **Projected Face Interpolated**: projects along normals to find a source face and interpolates its weights.

Weight transfer is a starting point. Test shoulders, elbows, hips, knees, loose folds, layered clothing, and extreme race DNA after copying.

## Ponytail Weights

The **Ponytail Weights** tools create distance-based weights for ponytails, hair strands, tails, or similar bone chains. The same controls also appear in a separate, collapsible **Ponytail Weights** panel in the UMA Tools sidebar.

1. Select the desired bones from an armature in Object, Edit, or Pose mode.
2. Click **Add Selected Bones**.
3. Select the target mesh.
4. In Edit mode, select only the vertices to process. If no vertices are selected, the entire mesh is processed.
5. Adjust **Smooth Factor**.
6. Click **Calculate weights for bones**.

Actions and controls:

- **Add Selected Bones**: adds the selected bone names without duplicates.
- **Remove Selected**: removes the highlighted bone from the list.
- **Clear List**: empties the bone list.
- **Smooth Factor**: controls the Gaussian distance blend. `0` assigns each vertex to its nearest listed bone; `1` produces a broad blend across the listed bones.
- **Calculate weights for bones**: creates or updates groups for the listed bones, removes processed vertices from groups outside that list, and assigns normalized distance-based weights.

Because calculation removes other influences from the processed vertices, select only the intended hair or tail region when the mesh also contains vertices that need body-bone weights.

## Parenting

- **Set Parent (Object)**: parents the selected objects to the active object while keeping their current world transform.
- **Clear Parent (Keep Transform)**: removes the parent while preserving the object's visible transform.
- **Clear Parent Inverse**: clears the parent-inverse matrix without removing the parent.
- **Apply All Transforms**: applies location, rotation, and scale to every selected object.
- **Generate Layers and Apply Data Transfer**: generates the required data layout for each existing Data Transfer modifier on selected meshes, then applies the modifiers.

Applying a Data Transfer modifier makes its result part of the mesh. Save a working copy before applying modifiers that you may need to tune later.

## Utilities

- **Prepend** and **Append**: define text to add before or after selected mesh-object names.
- **Process rename on selected**: renames selected meshes. It does not add the same prefix or suffix twice.
- **Remove empty vertex groups**: deletes groups that have no meaningful weights on the selected meshes.
- **Normalize Selected**: scales the active mesh's selected vertex weights so each weighted vertex totals `1`.
- **Normalize All**: normalizes every weighted vertex on the active mesh.

Vertices without any weights are reported and skipped by the normalization actions.

## Editing Tools

- **Select edge loops**: switches the active mesh to edge selection as needed and invokes Blender's edge-loop selection. Use it to quickly select garment borders, seam loops, and deformation loops.

## Vertex Group Quick Select

This section keeps a short working list of important vertex groups.

- **Select all vertexes**: selects every vertex on the active mesh.
- **Unselect all vertexes**: clears the current vertex selection.
- **Add current vertex group**: adds the active vertex group to the quick-select list.
- **Select**: makes the listed group active and selects its assigned vertices.
- **Opposite**: switches to the matching `Left` or `Right` group.
- **X**: removes the entry from the quick-select list without deleting the actual vertex group.

This is useful when alternating between paired limb groups or repeatedly refining a small set of deformation areas.

## 3D Cursor

- **Move to Origin**: places the 3D Cursor at world position `0, 0, 0`.
- **Align with Object**: places the cursor at the active object's world position.

These actions make origin, pivot, and alignment work more repeatable when preparing symmetrical assets or additional rig parts.

## UDIM Tools

### Split UDIMS into separate textures

For each selected mesh, this action:

- Finds faces by UV tile using the active UV set.
- Creates a material copy for each used UDIM tile.
- Reassigns faces to their tile-specific material.
- Replaces tiled image nodes with the corresponding individual tile image where the file can be found.
- Inserts the UV mapping offset needed for that tile.
- Reports faces that cross tile boundaries and tiled images whose files could not be found.

A face spanning more than one UDIM tile is assigned on a best-effort basis from its average UV position. Correct such faces before relying on the split result.

### Reset to UDIM

This restores faces to the original materials recorded by the split action, removes unused material slots, and deletes generated split materials that no longer have users.

Both actions support Undo, but save the Blender file before a large material conversion.

## UMA Import

UMA Import provides repeatable transforms and material setup for OBJ or FBX reference meshes.

- **Material**: optional material assigned to all polygons on imported meshes.
- **Scale**: scale applied to each imported mesh before transforms are applied. The default is `0.17, 0.17, 0.18`.
- **Rotation**: rotation applied before transforms are applied. The default is `0, 0, 0`.
- **Location**: object location assigned after transforms are applied. The default is `0, 0.089, 0.113`.
- **Copy from current**: copies scale, rotation, and location from the active object into the import settings.
- **Paste to current**: assigns the stored import transforms to the active object without applying them.
- **Import Wavefront OBJ**: imports an OBJ, applies the configured material and transforms, applies smooth shading, and selects the imported meshes.
- **Import FBX**: imports an FBX and performs the same UMA material, transform, and smooth-shading setup on its imported meshes.

The supplied defaults are workflow conveniences, not universal unit conversion values. Use **Copy from current** when an existing correctly aligned UMA reference is available.

## UMA Export

The exporter is the final consistency step. It uses the same FBX settings every time instead of relying on Blender's last-used export options.

### Export actions

- **UMA 2 Format**: switches both export actions to the legacy UMA 2 axis and bone-axis convention. Leave it disabled for the current UMA 3 convention.
- **Export FBX (All)**: exports the scene's supported object types.
- **Export FBX (Selected)**: exports only the current selection and stops if nothing is selected.

When the `.blend` file has been saved, the default filenames are placed beside it as `<BlendName>_all.fbx` or `<BlendName>_selected.fbx`. You can choose another destination in the file browser.

### Fixed UMA export settings

Both export actions use these settings:

- Global scale `1.0`
- Apply unit scale enabled
- Apply Scalings set to `FBX All`
- Mesh, armature, and empty objects only
- Mesh modifiers and render modifier settings enabled
- Animation baking disabled
- Armature node type set to `Null`
- Leaf bones enabled
- Custom properties disabled
- Textures copied and embedded
- Cameras and lights excluded

The current UMA 3 axis preset is:

- Forward: `-Z`
- Up: `Y`
- Primary Bone Axis: `Y`
- Secondary Bone Axis: `X`

With **UMA 2 Format** enabled, the preset becomes:

- Forward: `Z`
- Up: `Y`
- Primary Bone Axis: `X`
- Secondary Bone Axis: `-Y`

### Why this export path is dependable

The exporter removes variation in scale handling, axes, included object types, modifier evaluation, armature representation, animation, and texture packaging. When every contributor uses the same tool version, a passing preflight and the same export button produce a consistent FBX handoff.

For a final asset:

1. Return the rig to its neutral pose.
2. Run **Check for Errors** with the correct visibility scope.
3. Resolve the complete report.
4. Select the required skinned mesh and complete armature.
5. Use **Export FBX (Selected)** unless the entire scene is deliberately part of the asset.
6. Verify scale, orientation, root bone, bone list, UVs, and deformation after Unity imports the FBX.
7. Click `Verify Slot` in UMA Slot Builder before creating the production `SlotDataAsset`.

That sequence gives artists a repeatable, low-risk route into UMA. It prevents the common technical setup mistakes while preserving the necessary final visual review.

## Outliner Shortcuts

Right-clicking an object in the Blender Outliner adds UMA shortcuts:

- **Duplicate Objects**: duplicates the clicked object and selects the duplicate.
- **Toggle On**: shows the clicked object and hides the other objects in the same collection, which is useful for isolating wardrobe pieces.
- **Apply All Transforms**: applies transforms to the selected objects.
- **Generate Layers and Apply Data Transfer**: prepares and applies Data Transfer modifiers on selected meshes.
- **Set Parent (Object)**, **Clear Parent (Keep Transform)**, and **Clear Parent Inverse**: provide the same parenting actions as the sidebar.
- **Export FBX (All)** and **Export FBX (Selected)**: open the same UMA export workflows from the Outliner.

## Final Checklist

Before sending an asset to Unity:

- The asset was built against the intended UMA race.
- The armature object is named `Root`.
- The root chain is `Global > Position`.
- The rig is in its neutral pose.
- Every mesh has the correct Armature modifier.
- Object transforms are intentional and the error report is clean.
- Weights are normalized and visually tested.
- Required meshes and bones are selected.
- The correct UMA 3 or UMA 2 export format is chosen.
- The FBX was exported with UMA Tools.
- The imported mesh passes UMA Slot Builder verification.

For the full mesh, texture, slot, overlay, and wardrobe workflow, continue with [UMA Content Creation](ContentCreation.md).
