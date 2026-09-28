# Model to UMA: guided race and clothing creation

Open **UMA → Model to Race & Clothing…**. The wizard uses UMA's existing Slot Builder; it does not introduce another runtime character builder.

## 1. Source

You can also right-click a skinned model or prefab in the Project window and choose **UMA → Model To Race → Clothing...**. This opens the wizard on Source with the complete model root selected, even when you selected a child of the model. Select one model at a time.

Choose **New Race** or **Clothing For Existing Race**, then select the complete model root (prefab/FBX or a saved scene object). Include the skeleton and all skinned renderers beneath that root. Use the rest pose, not a currently animated pose. Enable **Read/Write** in the model importer if validation requests it.

The source root's placement is removed; the character is built in model-local coordinates. Renderer transforms, bind poses, normals, tangents, mirrored mesh winding and selected blendshape frames are converted together. Bone names must be unique because UMA identifies bones by name. Mirrored/sheared bone rest transforms need to be applied in the source authoring tool first.

Select an existing output folder inside Assets, a name, and optionally an animator controller. **Register in UMA Global Library** defaults on. An optional DynamicCharacterAvatar prefab is created with the new race and starting wardrobe configured. It needs an UMA context/generator in the scene, like other UMA avatars.

Every import creates a **new folder**. Repeating an import does not replace an earlier race or update existing characters. Source meshes, model import settings, materials and prefabs are not modified.

## 2. Parts and wardrobe

Each non-empty material submesh is a separate row. This lets a model containing body and equipment in the same SkinnedMeshRenderer produce separate body slots and clothing recipes. Lower LOD renderers start skipped; review these assignments.

- **Body:** goes into the new race's base recipe.
- **Clothing:** becomes a wardrobe item. Choose its **outfit name** and **wardrobe region**.
- **Skip:** produces no geometry.

Pieces sharing an outfit name are grouped into one wardrobe recipe; they must use the same region. Different outfits in the same region are alternatives. Only the first enabled outfit per region is equipped on the generated prefab. For a new race, the region list is editable. For existing races, the wizard uses that race's supported regions.

Each included part creates a SlotDataAsset and OverlayDataAsset. Source materials are copied into `UseExistingMaterial` UMAMaterial wrappers, retaining their shaders, property values, textures and UVs. Shared source materials share a wrapper within this import. Texture assets are referenced, not duplicated. This does **not** automatically atlas textures, convert shaders, or create body-hiding triangle masks. After import, use UMA's material tools for an atlased material workflow and Mesh Hide tools for clothing that needs body occlusion.

### Clothing for an existing race

Clothing must already fit that race, with compatible skinning and rest transforms. The wizard checks bone names and rest poses against the target TPose. A matching bone name alone is not evidence of compatible skinning. If the checks fail, export against the correct rest rig or use **Scene Mesh Slot Builder** to transfer weights first. This workflow does not automatically fit or retarget arbitrary garments.

The existing base recipe/body is never replaced. Bone DNA is normally already supplied by the race, so the window switches bone-DNA generation off in clothing mode.

## 3. DNA and blendshapes

### Bone DNA

Each checked bone gets an UMA 3 DNA item with a Bone Scale effect. At DNA **0.5**, the original scale is preserved. With a local scale range of `(0.2, 0.2, 0.2)`, the endpoints are 80% and 120% size. Scaling is in the bone's own coordinates, not an assumed world up axis; children inherit it.

Change local X/Y/Z ranges for more specific controls, or deselect bones that should not have sliders. These are automatically generated starting controls, not anatomically authored facial expressions or independent limb-length controls. You can refine the generated DNA assets using UMA's normal DNA tools.

### Blendshapes

**All blendshapes start included.** Unchecking one removes its frames from the generated slots and excludes its DNA item. Included shapes retain all frames. Unchecking **Generate blendshape DNA** retains selected geometry without creating sliders.

Names shared by several included parts share one slider. Expand **Weight range & default** to choose the source weight range and neutral value. UI DNA values are normalized 0–1 and map to that range. The first included renderer supplies the default for a shared shape; review it if parts use different current weights. The generated avatar has blendshape loading enabled when any are included.

For an existing race, **Attach DNA to existing race** is an explicit, confirmed change. It adds the generated groups and selected unbaked-shape names without replacing existing groups. With this off, the groups are created as standalone assets for review/manual attachment. Legacy-DNA races are not automatically migrated. Avoid adding several controls that drive the same bone/shape unless that is intentional.

## 4. Review and create

Review roles, output counts, wardrobe assignments and validation messages. Fix errors before creating. On failure, the new import folder is removed and an explicitly modified target race is restored; existing imports and source assets are not replaced.

After creation:

1. Drag the generated avatar prefab into a scene with UMA's normal context/generator.
2. Check the rest silhouette against the original model, then play a suitable animation. Humanoid source avatars retain their human mapping; otherwise the race uses a Generic avatar.
3. Exercise bone and blendshape sliders at their default and endpoints. Confirm intended deformations, especially any inherited scales.
4. Swap wardrobe recipes by region. Add body-hiding masks where appropriate.
5. Test the result in a player build using your project's normal UMA content inclusion/Addressables workflow.

Static/unskinned meshes, cloth simulation components, source scripts, constraints, LOD chains, animation clips/controllers and expression authoring are not automatically converted. Mesh/texture generation, resource reuse, DNA evaluation and wardrobe loading continue through the normal UMA backend.
