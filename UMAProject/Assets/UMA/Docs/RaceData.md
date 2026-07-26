# RaceData

A `RaceData` asset defines a UMA character family. It connects the base body, skeleton, pose, DNA, wardrobe regions, expressions, bounds, and cross-compatible races used to build a character.

In UMA terminology, a race is a technical compatibility group. It does not need to represent a narrative species. Two characters belong to different UMA races when they require different base geometry, skeleton behavior, DNA, or wardrobe mapping.

## What a Race Provides

A production race normally supplies:

- A base recipe or FBX-route renderer
- Humanoid or Generic animation configuration
- A T-pose when required
- DNA groups or legacy DNA converters
- Wardrobe regions
- Cross-compatibility rules
- Expression setup
- Renderer bounds
- Blendshape generation rules
- Thumbnails and tags

Create a RaceData asset with:

`Assets > Create > UMA > Core > RaceData`

## Recommended Creation Workflow

1. Prepare and import the neutral body and skeleton.
2. Create the body slots, overlays, and base recipe.
3. Create the `RaceData`.
4. Choose Humanoid or Generic.
5. Assign the T-pose and expression set.
6. Assign the base recipe, or configure the FBX route.
7. Define wardrobe regions.
8. Enable and configure the new DNA system.
9. Configure blendshape generation.
10. Add cross-compatible races where required.
11. Add thumbnails and tags.
12. Click `Validate RaceData`.
13. Add the race to the Global Library and build a DCA.

See [ContentCreation.md](ContentCreation.md) for the modeling and Slot Builder workflow.

For a complete guided workflow from the source model through player-build testing, see [Creating a New Race](CreatingANewRace.md).

## Race Name

`raceName` is the stable identifier stored in recipes, AvatarDefinitions, and index lookups.

Choose it before content ships. Renaming a race after save data and wardrobe assets exist can break references by name. The inspector may retain a legacy name temporarily for old recipes; clear it after migration is complete.

## UMA Target

Choose:

- `Humanoid` for Unity Humanoid animation and UMA expression support.
- `Generic` for non-humanoid rigs or custom animation hierarchies.

For a Generic race, set the root motion transform when the animation setup requires it.

## T-Pose

The T-pose records the skeleton reference used by UMA and Unity animation setup.

For Humanoid races:

1. Import the full race model with its skeleton.
2. Configure the model's Unity avatar.
3. Select the model and use `Assets > UMA > Extract T-Pose`.
4. Assign the generated asset to `T-Pose`.

You can also use `Extract UMA T-Pose` from an Animator component's context menu.

Extract the pose once from the correct neutral skeleton. Do not use a T-pose from a race with a different hierarchy.

## Expression Set

Assign an `UMAExpressionSet` when the race uses `UMAExpressionPlayer`. Expressions require compatible bones or blendshape behavior and are normally used with Humanoid races.

Test blink, jaw, eye direction, and extreme expressions after the race has generated.

## Fixup Rotations

`Fixup Rotations` should normally remain enabled for Blender-authored FBX slots. If a race or wearable rotates incorrectly, verify the DCC transforms and FBX axes before using this setting as a general repair.

## Base Definition

UMA supports two base-definition routes.

### Base Race Recipe

The traditional route uses `baseRaceRecipe`.

The recipe defines:

- Base body slots
- Base overlays
- Shared colors
- Default DNA values
- Utility slots

Use this route when the body should be assembled from ordinary UMA slots and overlays.

The base recipe should represent the unclothed neutral race. Wardrobe is layered over it by the DCA.

### FBX Route

Enable `Use FBX Route` when the race should preserve a source `SkinnedMeshRenderer` as its base body.

Configure:

- `Base FBX Renderer`
- FBX base mesh-hide bindings

The FBX route changes how base geometry is supplied. Cross-compatibility slot mapping based on a base recipe is disabled while this route is active.

Use the FBX route deliberately; do not enable it simply because the source began as an FBX.

## Renderer Bounds

Generated renderer bounds determine whether Unity culls the character.

Enable `Use Manual Renderer Bounds` when automatic bounds cause meshes to disappear during:

- Tall or wide DNA extremes
- Large animations
- Hair or accessories extending far from the body
- Unusual root or Position-bone scaling

Set:

- `Manual Bounds (Extents)`
- `Manual Bounds Center`

Use the inspector's Copy and Paste buttons to transfer tested bounds between related races.

Keep bounds large enough for real motion but not unnecessarily enormous, because oversized bounds reduce culling effectiveness.

## DNA

### New DNA system

Enable `Use New DNA System` and assign the `DNACollection` used by the race.

The collection contains DNA groups and their active instances. DNA effects can drive:

- Bone translation, rotation, and scale
- Bone transforms and poses
- Blendshapes
- Mesh Modifiers
- Overlay UV transforms
- Shared colors and shader properties

See [DNACreationGuide.md](DNACreationGuide.md) and [NewDNASystem.md](NewDNASystem.md).

### Legacy DNA

When the new system is disabled, the race uses:

- Dynamic DNA converter controllers
- DNA range assets
- Optional converter disabling

Keep this path for migrated races that still require it. Use the new DNA system for new UMA 3 race authoring unless the project has a specific legacy dependency.

## Race Generation and Blendshapes

The `Race Generation` foldout controls blendshape handling.

### Force Rebuild Race Slots

This is a design-time testing option. It forces race slots to rebuild each generation and should not remain enabled in production without a measured reason.

### Prebaked Blendshapes

Prebaking applies selected blendshape values into race geometry during generation.

Use prebaking when:

- A race always needs a particular shape.
- The live blendshape is not required afterward.
- Baking reduces runtime blendshape complexity.

The inspector can inspect base-recipe blendshapes and extract them to Mesh Modifiers.

### Unbaked Shapes To Include

Use this list to preserve selected live blendshapes. Entries can use the supported matching behavior shown by the inspector.

Balance flexibility against generation time and mesh memory. Hero characters may retain more shapes than crowds.

## Wardrobe Regions

Wardrobe regions define the categories the race can equip. Typical regions include:

- Hair
- Head
- Face
- Chest
- Hands
- Legs
- Feet

Recipes use these names to determine ownership and replacement. Region naming must be consistent between RaceData and wardrobe recipes.

Changing region names after content exists can orphan or misclassify wardrobe.

## Cross Compatibility

Cross compatibility allows this race to use wardrobe authored for another race.

Add a compatible race in `Cross Compatibility Settings`, then map equivalent base slots.

For each mapping:

- `This Race's Slot` is the slot on the current race.
- `Compatible Race's Slot` is the corresponding source-race slot.
- `Overlays Match` determines whether overlays authored for the source slot can transfer.

Example: a high-resolution chest slot can map to another race's standard chest slot so wardrobe hides and overlay-only recipes resolve correctly.

Do not enable overlay compatibility when the two slots use different UV layouts. The clothing mesh may fit while the transferred texture does not.

Test:

- Mesh wearables
- Overlay-only wearables
- Hidden base slots
- Mesh Hide Assets
- Shared colors
- Extreme DNA

The old `backwardsCompatibleWith` list exists only for migration. Use the current Cross Compatibility Settings for new work.

## Race Thumbnails

Assign:

- Full-body thumbnail
- Face thumbnail
- Optional wardrobe-region thumbnails

Use consistent framing, lighting, and background across races so character-creator UI remains coherent.

## Tags

Tags classify races for project tools and runtime filtering. Use a controlled naming convention and avoid near-duplicate tags.

## Character Dimensions

Race height, radius, and mass support character and physics setup. Treat them as defaults for systems that consume them; they do not replace renderer bounds or collider authoring.

## Index and Build Options

- `No Auto Add` prevents bulk project scans from automatically indexing the race.
- `Force Keep` prevents build cleanup from treating the race as an unused item.
- `Label Local Files` participates in Addressables labeling behavior.

Most races should remain indexable. Use exclusion flags only when content ownership and loading are understood.

## Validate the Race

Click `Validate RaceData` after important changes.

Validation should be part of the workflow after:

- Changing the base recipe
- Switching the FBX route
- Replacing the T-pose
- Editing blendshape rules
- Changing wardrobe regions
- Editing cross compatibility
- Migrating DNA

Also test the race on a DCA in both editor-time generation and Play mode.

## Artist Test Checklist

- The neutral body generates without warnings.
- Humanoid animation binds correctly.
- Expressions affect the intended face.
- DNA is neutral at its default values.
- Extreme DNA does not leave the renderer bounds.
- Every wardrobe region accepts the correct recipes.
- Cross-compatible clothing fits and hides the correct body slots.
- Overlay-only cross-compatible recipes use matching UVs.
- Blendshapes included by the race are present.
- The race works after rebuilding the Global Library.
- The race works in a player build.

## Troubleshooting

### The race does not appear in a DCA

Add it to the Global Library and verify that `No Auto Add` is not excluding it.

### The race generates without a body

Check the base recipe or FBX route, then verify all base slots and overlays are indexed.

### Animation is twisted

Verify the imported avatar, T-pose, bone hierarchy, rest pose, and `Fixup Rotations`.

### DNA controls are missing

Confirm `Use New DNA System` and the DNA collection, or verify the legacy converter list.

### Cross-compatible overlays are misaligned

Disable `Overlays Match` for slot pairs that do not share the same UV layout.

### The character disappears during animation

Configure and test manual renderer bounds.

## Related Guides

- [ContentCreation.md](ContentCreation.md)
- [CreatingANewRace.md](CreatingANewRace.md)
- [GettingStarted.md](GettingStarted.md)
- [DynamicCharacterAvatar.md](DynamicCharacterAvatar.md)
- [DNACreationGuide.md](DNACreationGuide.md)
- [WardrobeRecipeEditor.md](WardrobeRecipeEditor.md)
- [UMAAssetIndexer.md](UMAAssetIndexer.md)
