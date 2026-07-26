# Creating a New Race

This guide takes an artist or technical artist through creating an UMA race from the first DCC decision to a tested runtime character.

In UMA, a race is a technical character family. It defines which body, skeleton, DNA, wardrobe regions, expressions, blendshapes, and compatibility rules belong together. A race can represent a species, body type, robot, creature, or simply a character family that needs different geometry.

The easiest successful workflow is:

1. Make the neutral body work.
2. Make it animate.
3. Add DNA.
4. Add wardrobe and compatibility.
5. Optimize only after the complete race is reliable.

Do not try to solve every system at once. A race that cannot generate in its neutral form will be much harder to diagnose after DNA, blendshapes, clothing, and Addressables are added.

## Choose the Right Starting Point

There are three practical ways to begin.

| Starting point | Use it when | Main advantage | Important limitation |
| --- | --- | --- | --- |
| Duplicate an existing race | The new race uses a related skeleton, body layout, or wardrobe family | Preserves a known working setup | The copied slots and settings still need to be reviewed |
| Build a slot-based race from scratch | You have a new body but want standard UMA slots, overlays, recipes, and cross compatibility | Most flexible UMA workflow | Requires complete slot and recipe authoring |
| Use the FBX route | The base body must remain a preserved source `SkinnedMeshRenderer` | Keeps the source renderer together | Base-recipe slot mapping for cross compatibility is unavailable |

For most human or humanoid variants, duplicating the closest working race is the safest route. For a truly new body family, use the slot-based workflow.

## Before You Begin

Collect the following:

- The neutral body mesh
- The complete skeleton
- A unified body mesh for seam-reference work, when the body will be split into slots
- Base textures
- The intended shader and `UMAMaterial`
- Any required blendshapes
- A list of wardrobe regions
- A plan for DNA
- A compatible animator controller
- A target platform and texture budget

Create a clean content folder before generating assets. One useful structure is:

```text
MyRace/
  FBX/
  Materials/
  Textures/
  Slots/
  Overlays/
  Recipes/
  DNA/
  Thumbnails/
```

Choose the race name before recipes or save data use it. UMA resolves many assets by their logical name, so late renaming can break content.

## Workflow A: Duplicate a Related Race

Use this workflow when the new character is derived from a working UMA race.

1. Select the source `RaceData` in the Project window.
2. Choose `Assets > UMA > Duplicate Race`.
3. Enter the **New Race Name**.
4. Enter the **New Base Race Recipe Name**.
5. Review the target asset paths.
6. Select any races that should be cross compatible.
7. Choose the blendshapes that belong to the new race and set required default values.
8. Decide whether to generate a T-pose from the source T-pose and selected mixer poses.
9. Review the summary and click **Create**.

The wizard creates the race setup and duplicates the base recipe when one exists. It does not create a new body mesh or new textures. Replace or edit the duplicated recipe content deliberately.

After duplication, review every inherited setting:

- Base recipe
- T-pose
- DNA collection or legacy DNA converters
- Expression set
- Wardrobe regions
- Cross compatibility
- Blendshape rules
- Renderer bounds
- Tags and thumbnails

Do not assume an inherited setting is correct only because the neutral character generates.

Continue at [Import and Verify the Model](#import-and-verify-the-model) when new body geometry is involved, or at [Create the RaceData](#create-the-racedata) when the duplicated body content is already correct.

## Workflow B: Build a Race From Scratch

The remaining sections describe the complete slot-based workflow. Artists using the duplication route should still use them as a review checklist.

## Prepare the Body in a DCC Package

Work from a clean neutral pose.

### Skeleton

- Preserve the complete hierarchy expected by the race.
- Preserve all joint or bone names.
- Keep required root objects such as `Global` and `Position`.
- Do not freeze, reorient, or zero a bound skeleton to make its transforms look cleaner.
- Make sure every vertex influence exists in the exported hierarchy.

For a Humanoid race, the skeleton must also map cleanly to Unity's Humanoid avatar.

### Body slots

Plan how wardrobe will replace or hide the body. Common body slots include:

- Head
- Torso
- Arms
- Hands
- Hips
- Legs
- Feet
- Eyes
- Mouth or teeth

Slot boundaries should follow practical clothing and hiding regions. Avoid placing a seam through an area that bends heavily unless the adjoining slots have matching positions, normals, tangents, weights, UVs, and DNA behavior.

Keep a unified copy of the complete body. Slot Builder can use it as a **Seams Mesh** so separated body slots receive consistent border normals and tangents.

### UVs and textures

- Keep atlas UVs in the `0..1` range.
- Use compatible UV layouts where overlays must transfer between slots or races.
- Leave padding around UV islands for mipmaps and atlas filtering.
- Decide the required material channels before painting final textures.
- Keep normal maps, masks, and packed maps aligned with the albedo.

If compatible races use different UV layouts, their clothing meshes may still be compatible, but their body overlays should not be marked as matching.

### Skin weights

- Bind the body to the final skeleton.
- Normalize all skin weights.
- Remove unintended influences.
- Match the project's Unity skin-weight quality.
- Test shoulders, elbows, wrists, hips, knees, ankles, neck, jaw, and eyes.

For Maya and Blender-specific authoring and FBX export steps, see [Content Creation](ContentCreation.md).

### Blendshapes

If the race uses blendshapes:

- Finalize topology before authoring them.
- Keep vertex order unchanged.
- Use stable, intentional names.
- Decide which shapes are baked into the race and which remain live.
- Give compatible wardrobe slots matching shapes when they need to follow the body.

Do not keep every source blendshape by default. Live blendshapes increase generated mesh data and can make character generation substantially slower.

## Export the Model

Export:

- The complete required skeleton
- Every base-body `SkinnedMeshRenderer`
- The unified seam-reference mesh when it is a separate object
- Required blendshapes

Do not export:

- Cameras
- Lights
- Unused meshes
- Test animation
- Duplicate skeletons

Return the body to its neutral or bind pose before export.

Use one tested scale and axis convention for the whole race. Do not compensate for different exports with a different Unity scale on every body slot.

## Import and Verify the Model

Import the FBX into Unity, then inspect it before opening any UMA tools.

1. Confirm the model has the expected scale and orientation.
2. Confirm each body part is a `SkinnedMeshRenderer`.
3. Inspect the root bone and bone list.
4. Check that no vertices stretch toward the origin.
5. Confirm the neutral pose matches the DCC scene.
6. Confirm blendshape names and frames are present.
7. Configure the Unity **Rig** tab as Humanoid or Generic.
8. Apply the importer settings.
9. Place the model in a simple scene and test a representative animation.

If the imported source model is wrong, stop and repair the FBX. Slot Builder should not be used to hide a scale, orientation, hierarchy, or skinning problem.

## Extract the T-Pose

A Humanoid race normally needs a `UmaTPose`.

1. Configure and apply the model's Rig settings.
2. Select the model asset in the Project window.
3. Choose `Assets > UMA > Extract T-Pose`.
4. Save the generated T-pose with the race content.

You can also use `UMA > Tools > Pose Tools > Extract T-Pose` for the scene-based workflow.

Extract the T-pose from the final race skeleton. Do not borrow one from a race with different joint orientations or hierarchy.

Generic races may use a custom root motion transform rather than Humanoid setup.

## Prepare the UMA Material

Create or reuse the `UMAMaterial` that describes the race shader.

Create one with:

`Assets > Create > UMA > Core > Material`

Configure:

- Material type
- Unity material and shader
- Texture property names
- Channel types
- Overlay blend behavior
- Mipmap behavior
- Atlas or No Atlas use

The material channel order controls the texture order on every overlay that uses it.

For example:

1. Base map or albedo
2. Normal map
3. Mask map
4. Skin mask or another shader-specific channel

See [UMA Material](UMAMaterial.md) before creating production overlays.

## Create the Body Slots

Open:

`UMA > Content Creation > Slots > Slot Builder`

For a base race:

1. Assign the unified body to **Seams Mesh** when using separated body slots.
2. Choose the **Slot Destination Folder**.
3. Enable **Is Base Race Recipe**.
4. Enable **Create Overlay** if Slot Builder should create the initial overlay assets.
5. Enable **Create Base Recipe**.
6. Enable **Add To Global Library**.
7. Enable **Binary Serialization** for large or blendshape-heavy meshes.
8. Add helper bone names to **Keep Bones Containing** when they are required but not directly weighted.
9. Drop the FBX into the batch area or process each renderer through **Single Slot Processing**.
10. Click **Verify Slot** before creating each slot.
11. Process the selected body slots.

The base recipe created from a batch includes the generated slots and their generated overlays.

After processing, inspect each `SlotDataAsset`:

- Stable slot name
- Mesh and submeshes
- Correct `UMAMaterial`
- Renderer asset
- Root bone
- Animated or helper bones
- Blendshapes
- Tags and slot group

Build the body again after changing a slot. A slot that looks correct only as the original FBX is not yet proven to work in UMA.

## Finish the Base Overlays

Open every generated `OverlayDataAsset`.

For each overlay:

1. Give it a stable, unique overlay name.
2. Assign the `UMAMaterial` used by the slot.
3. Add textures in the material's exact channel order.
4. Confirm normal and mask import settings.
5. Assign shared colors such as skin, eyes, or hair where appropriate.
6. Check the overlay rect.
7. Add useful tags.

The first overlay on a base slot establishes the surface and should normally contain all required channels.

If several body slots share one texture layout, they can use aligned overlays and shared colors. Check seams at lower atlas resolutions and with mipmaps enabled.

See [OverlayDataAsset](OverlayDataAsset.md).

## Finish the Base Recipe

Open the generated `UMATextRecipe`.

The base recipe should contain:

- Every required body slot
- The base overlay for each slot
- Default shared colors
- Neutral DNA values
- Required utility slots

Keep clothing and optional accessories out of the base recipe. Those belong in wardrobe recipes or preload wardrobe.

Generate a test character from the base recipe before proceeding. At this stage, the goal is a neutral, unclothed, correctly shaded body.

## Create the RaceData

Create the asset with:

`Assets > Create > UMA > Core > RaceData`

The RaceData asset name is its runtime race name. Rename it before recipes, presets, or save data use it.

Configure the following.

### Animation

- Set **UMA Target** to Humanoid or Generic.
- For Generic, enter the required **Root Motion Transform**.
- Assign the race **T-Pose**.
- Assign an **Expression Set** when the race uses UMA expressions.
- Enable **Fixup Rotations** for Blender-authored slots when required.

### Base definition

For the normal slot workflow:

- Leave **Use FBX Route** disabled.
- Assign the base `UMATextRecipe` to **Base Race Recipe**.

For the FBX route:

- Enable **Use FBX Route**.
- Assign **Base FBX Renderer**.
- Configure any FBX base mesh-hide bindings.

Do not enable the FBX route merely because the source asset is an FBX. Almost all slot-based races begin as FBX files.

### DNA

For new UMA 3 content:

1. Enable **Use New DNA System**.
2. Add the required groups and instances to **DNACollection**.
3. Start with a small number of important controls.
4. Keep the neutral body at the intended default values.
5. Test one effect type at a time.

DNA effects can drive bones, poses, blendshapes, mesh modifiers, overlay UVs, shared colors, and shader properties.

It is easier to diagnose a race if the neutral body works before DNA is enabled. See [DNA Creation Guide](DNACreationGuide.md) and [New DNA System](NewDNASystem.md).

### Blendshape generation

Open **Race Generation**.

- Use **Prebaked Blendshapes** for shapes that should be permanently applied to the generated geometry.
- Use **Unbaked Shapes To Include** for selected shapes that must remain live.
- Add only the exact names or regular-expression patterns needed by the finished character.
- Leave **Force Rebuild Race Slots** off outside temporary design testing.

The DCA also needs **Load BlendShapes** enabled when the finished character must retain live blendshapes.

### Wardrobe regions

Add the categories this race can equip, such as:

- Hair
- Head
- Face
- Chest
- Hands
- Legs
- Feet

Use the same spelling and capitalization in wardrobe recipes. Treat these names as production identifiers once content exists.

### Renderer bounds

Begin with automatic bounds. Enable **Use Manual Renderer Bounds** only when the character is incorrectly culled during:

- Extreme DNA
- Large animation
- Tall hair or accessories
- Unusual root or `Position` bone scaling

Set **Manual Bounds (Extents)** and **Manual Bounds Center** from measured character motion. Excessively large bounds reduce useful culling.

### Dimensions, thumbnails, and tags

Set the race's approximate height, radius, and mass for systems that consume them.

Add:

- Full-body thumbnail
- Face thumbnail
- Optional wardrobe-region thumbnails
- Project tags

Use consistent framing and lighting across race thumbnails.

## Configure Cross Compatibility

Cross compatibility lets the new race use wardrobe authored for another race.

Use it only after the new race works by itself.

1. Open **Cross Compatibility Settings**.
2. Drag or select a compatible race.
3. Map each new base slot to the equivalent base slot on the compatible race.
4. Enable **Overlays Match** only when both slots use the same UV layout.
5. Save the RaceData.

Test more than the clothing mesh:

- Recipes that hide base slots
- Overlay-only recipes
- Mesh Hide Assets
- Shared colors
- Additive wardrobe
- Extreme DNA

Cross-compatibility slot mapping requires a base race recipe and is disabled for the FBX route.

## Add the Race to the Global Library

Select the `RaceData` and use:

`Assets > Add selected assets to UMA global library`

Also confirm that the following are indexed:

- Base recipe
- Body slots
- Base overlays
- DNA assets
- Expression set
- Required animator resources
- Wardrobe recipes used for testing

Open `UMA > Global Library` and search for the race name.

See [UMA Asset Indexer and Global Library](UMAAssetIndexer.md).

## Build the First DCA

Create a test avatar with:

`GameObject > UMA > Create New Dynamic Character Avatar`

Then:

1. Select the new race.
2. Assign a compatible animator controller or race animator setup.
3. Leave **Build Character Enabled** on.
4. Begin with no optional wardrobe.
5. Generate the neutral character.
6. Enter Play mode and confirm it generates again at runtime.

Do not begin by testing a complete outfit. A plain body makes missing slots, bad overlays, incorrect bounds, and material problems much easier to see.

## Test in Layers

Use this order.

### 1. Neutral body

- All body slots appear.
- Materials and texture channels are correct.
- Seams are clean.
- Scale and orientation are correct.

### 2. Animation

- Humanoid or Generic animation binds.
- Feet remain grounded as expected.
- Joints bend correctly.
- Eyes, jaw, fingers, and helper bones behave correctly.

### 3. DNA

- Default values preserve the neutral design.
- Useful minimum and maximum values work.
- Body slots stay joined.
- Bounds contain the deformed character.

### 4. Expressions and blendshapes

- Expressions affect the intended bones or shapes.
- Required live blendshapes are present.
- Prebaked shapes are not needlessly retained.

### 5. Wardrobe

- Every wardrobe region accepts the correct recipes.
- Base-body hiding is correct.
- Clothing survives animation and DNA extremes.

### 6. Cross compatibility

- Compatible clothing fits.
- Equivalent slots hide correctly.
- Overlay transfer occurs only on matching UVs.

### 7. Performance

- Atlas size matches the target platform.
- Unused blendshapes are removed.
- Overlay and material count is reasonable.
- Generation is profiled in a player build.

## Validate and Ship

On the `RaceData`, click **Validate RaceData** and resolve every error.

Before release:

1. Rebuild or repair the Global Library.
2. Restart the test scene.
3. Generate the race in Play mode.
4. Test a clean player build.
5. Test Addressables loading if used.
6. Test the lowest supported quality and skin-weight settings.
7. Test representative mobile, PC, or console hardware.
8. Save a representative `AvatarDefinition` and load it again.

The race is complete only when it works without relying on editor-only asset discovery.

## Common Problems

### The race is not in the DCA race list

- Add the RaceData to the Global Library.
- Check **No Auto Add**.
- Search for a duplicate race name.

### The race generates without a body

- Assign the base recipe or FBX renderer.
- Check that the base slots and overlays are indexed.
- Open the base recipe and confirm its slots are not null.

### The body is rotated or the animation is twisted

- Recheck FBX axes and transforms.
- Re-extract the T-pose from the final skeleton.
- Check joint orientation and hierarchy.
- Review **Fixup Rotations** for Blender content.

### Vertices stretch to the origin

The mesh contains a weight for a bone that was not exported or cannot be found. Repair the skin weights and export the complete required hierarchy.

### Body slots have visible seams

Rebuild them with a unified **Seams Mesh**, then compare border positions, normals, tangents, weights, UVs, and DNA effects.

### DNA controls do nothing

- Enable the correct DNA system.
- Add the required DNA collection groups and instances.
- Confirm the effects target this race's exact slots, bones, overlays, or shared colors.

### Clothing appears but hides the wrong body part

Check wardrobe-region names, recipe suppression, equivalent slot mappings, and Mesh Hide Asset compatibility.

### The character disappears during animation or DNA changes

Measure the complete motion and configure manual renderer bounds.

### The race works in the editor but not in a player

The required assets are probably not included through Resources, Addressables, or another explicit reference. Review the Global Library and build-loading setup.

## Final Artist Checklist

- The race name is final and unique.
- The neutral model, skeleton, UVs, and weights are clean.
- The T-pose came from the final skeleton.
- Every base slot validates.
- Every overlay matches its `UMAMaterial`.
- The base recipe contains only required race content.
- The RaceData points to the correct base definition.
- DNA defaults preserve the intended neutral body.
- Live and prebaked blendshapes are intentional.
- Wardrobe-region names are consistent.
- Cross-compatible slot and overlay mappings are tested.
- Renderer bounds contain real animation and DNA.
- The race and dependencies are in the Global Library.
- The race generates in a clean player build.

## Related Guides

- [Content Creation](ContentCreation.md)
- [RaceData](RaceData.md)
- [Getting Started](GettingStarted.md)
- [DynamicCharacterAvatar](DynamicCharacterAvatar.md)
- [SlotDataAsset](SlotDataAsset.md)
- [OverlayDataAsset](OverlayDataAsset.md)
- [UMA Material](UMAMaterial.md)
- [DNA Creation Guide](DNACreationGuide.md)
- [New DNA System](NewDNASystem.md)
- [Wardrobe Recipe Editor](WardrobeRecipeEditor.md)
- [UMA Generator Setup](UMAGeneratorSetup.md)
- [UMA Asset Indexer and Global Library](UMAAssetIndexer.md)
