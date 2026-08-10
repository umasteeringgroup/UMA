# DynamicCharacterAvatar

`DynamicCharacterAvatar` (DCA) is the main artist-facing component for building and changing an UMA character at runtime. It selects a race, equips wardrobe recipes, applies DNA and colors, and asks the active UMA Generator to produce the final skinned character.

Use a DCA when a character needs to be customized, saved, loaded, or rebuilt while the game is running. For a fixed background character that never changes, a prebuilt or baked character may be more appropriate.

For converting a generated avatar into a non-UMA prefab or creating slots from scene meshes, see [Prefab and Scene-Building Tools](PrefabAndSceneBuildingTools.md).

## Before You Start

Make sure the project has:

- A generator prefab assigned in **Edit > Project Settings > UMA > Generator**.
- The race, wardrobe recipes, slots, overlays, and animator resources available in the **Global Library**.
- A valid `RaceData` with a base race recipe.

Create an avatar from **GameObject > UMA > Create New Dynamic Character Avatar**, or add `DynamicCharacterAvatar` to an empty GameObject.

## First Character Workflow

1. Select the DCA GameObject.
2. Choose a **Race**.
3. Assign an animator controller or animator profile if the character should animate.
4. Add wardrobe recipes to the appropriate wardrobe slots.
5. Adjust DNA and shared colors as needed.
6. Leave **Build Character Enabled** on for normal use.
7. Enter Play mode and wait for the character update event before accessing generated renderers or materials.

The DCA resolves the selected content through the Global Library, assembles an `UMARecipe`, and queues it with the generator. Generation may complete over multiple frames, especially when using an incremental mesh combiner.

## Race

The race determines the base recipe, skeleton, available DNA, wardrobe regions, compatible wardrobe, and default character setup.

Changing race is more than changing a body mesh. The DCA may need to:

- Replace the base slots and overlays.
- Re-evaluate wardrobe compatibility.
- Reapply compatible DNA and colors.
- Select race-specific animation or expression data.
- Rebuild the complete character.

Test race changes with all important wardrobe combinations. Cross-compatible recipes still depend on well-matched meshes, overlays, and wardrobe region mappings.

See [RaceData](RaceData.md) for race authoring and compatibility setup.

## Wardrobe and Wearables

Wardrobe recipes are the normal way to dress a DCA. Each recipe targets a wardrobe slot such as `Chest`, `Legs`, or `Hair`, as defined by the race.

Artist workflow:

1. Confirm the wardrobe recipe supports the selected race or a compatible race.
2. Assign it to its intended wardrobe slot.
3. Check whether it suppresses or replaces another wardrobe region.
4. Build the avatar and inspect seams, clipping, overlay alignment, and animation.
5. Test important DNA extremes, not only the default body.

At runtime, use the wearable APIs rather than editing the generated recipe by hand:

- `SetWearableItem(...)` replaces the wearable in a wardrobe slot.
- `AppendWearableItem(...)` adds a wearable without replacing an existing entry when the recipe supports that workflow.
- `SetSlot(...)` can set recipe content by name.
- Clear or remove wardrobe through the DCA wardrobe APIs, then rebuild.

Wardrobe collections can apply coordinated groups of recipes. They are useful for complete outfits, but individual recipes still need correct race compatibility and wardrobe-region behavior.

## DNA

DNA controls proportions, facial features, poses, and other race-defined effects. The visible controls come from the active race and its DNA configuration.

For art review:

- Test the minimum, default, and maximum useful range of important DNA values.
- Watch joints, shoulders, hips, hands, face deformation, and clothing intersections.
- Verify custom wardrobe follows the same skeleton and deformation expectations as the race.
- Check blendshapes and mesh modifiers if the race uses them.

When changing several DNA values from code, defer the build until all values are set. This avoids generating the same character repeatedly.

See [New DNA System](NewDNASystem.md) and [DNA Creation Guide](DNACreationGuide.md).

## Shared Colors

Shared colors let multiple overlays use the same color value. They are commonly used for skin, hair, eyes, and coordinated clothing tints.

Prefer shared colors when several overlays should change together. This keeps recipes smaller, makes customization more predictable, and avoids authoring duplicate color controls.

The shared color name must match what the content expects. If a color control appears to do nothing, verify:

- The overlay and material support tinting.
- The shared color name is correct.
- The target color or channel is enabled in the material.
- The avatar was rebuilt after the change.

## Animator and Expressions

The generated character can use an animator controller and the race's expression setup.

Confirm that:

- The race target matches the animation setup: Humanoid or Generic.
- The race T-pose is valid.
- The animator controller is appropriate for the race.
- The expression set is assigned when facial expression animation is required.
- Any dynamically animated bones are declared by the slot or animation system that needs them.

Changing an animator does not normally require texture regeneration. Race, skeleton, or mesh changes may require a complete rebuild.

## Build Character Enabled

**Build Character Enabled** controls whether the DCA is allowed to initiate character generation.

Leave it enabled for normal scene use. Turn it off temporarily when a setup script needs to make several changes before the first build:

1. Disable character building.
2. Set race, wardrobe, DNA, and colors.
3. Re-enable character building.
4. Request one build.

This prevents redundant work and reduces frame spikes when spawning or configuring many avatars.

## Build and Update Lifecycle

A DCA build is queued work, not an immediate mesh assignment. The broad flow is:

1. The DCA resolves race and recipe content.
2. Slots, overlays, DNA, and modifiers are assembled.
3. Textures are generated or reused.
4. Mesh data is combined.
5. The completed renderers, materials, skeleton, and animator state replace the previous generated result.

With the incremental combiner, the current mesh remains visible and animated while a replacement mesh is being prepared. Code should not assume a rebuild is complete in the same frame that requested it.

Use the avatar completion/update events for operations that need the finished renderers, materials, or bones.

## Events

The DCA and its `UMAData` expose events for key stages of character generation. Exact event choices depend on whether a system needs recipe data, texture completion, or the final renderer.

Practical rule:

- Use the character-created event for first-time initialization that only needs a generated character.
- Use the character-updated/completed event when accessing final renderers, meshes, materials, or bones after any rebuild.
- Expect update events to run more than once during the life of a customizable character.

Unsubscribe external listeners when their owning object is disabled or destroyed.

## Save and Load

For current DCA save data, use `AvatarDefinition`.

Useful APIs include:

```csharp
AvatarDefinition definition = avatar.GetAvatarDefinition(false);
string json = avatar.GetAvatarDefinitionString(false);

avatar.LoadAvatarDefinition(definition);
avatar.LoadAvatarDefinition(json);
```

The definition can contain the race, wardrobe, DNA, colors, and other avatar state. Decide which sections your game needs before saving.

After loading:

- Required content must be present in the Global Library or available through Addressables.
- Recipe and asset names must remain stable.
- Wait for the finished update event before using generated renderers.

`GetCurrentRecipe()` and the old recipe-string loading workflow are legacy APIs. Do not use them for new save systems.

## Runtime Changes

Group related changes before rebuilding:

1. Disable automatic building or suppress intermediate updates.
2. Change race, wardrobe, DNA, and colors.
3. Mark only the necessary parts dirty.
4. Request one final update.

Use `ForceUpdate(...)` or the appropriate update API when you know whether the change affects textures, mesh, skeleton, or shape. Requesting a full rebuild for a color-only change wastes work; requesting too little can leave stale output.

## Addressables

A DCA can request content through UMA's Addressables workflow when the project is configured for it.

For reliable builds:

- Give assets stable UMA names.
- Keep race and recipe labels consistent.
- Confirm the required assets are addressable or otherwise included in the player.
- Test from a player build, not only the editor.
- Handle the time between requesting content and receiving a completed character.

See [UMA Asset Indexer and Global Library](UMAAssetIndexer.md).

## Performance Guidance

- Stagger large crowds instead of asking every avatar to build in one frame.
- Configure the generator and mesh combiner for the target platform.
- Avoid oversized atlases and unnecessary overlay channels.
- Reuse wardrobe recipes, overlays, materials, and cached data where appropriate.
- Batch customization changes into one build.
- Use incremental generation when consistent frame time matters.
- Profile in a player build; editor timings for jobs and native data can be misleading.

See [UMA Generator Setup](UMAGeneratorSetup.md) for generator configuration and platform guidance.

## Destruction and Cleanup

Destroying a DCA during generation must cancel or abandon its pending work and release both pending and current generated resources owned by that avatar. Do not keep references to generated meshes, materials, or temporary textures after the avatar is destroyed.

Systems that spawn and despawn avatars rapidly should:

- Destroy or pool the complete avatar GameObject.
- Remove external event subscriptions.
- Avoid modifying a DCA after destruction has started.
- Let UMA own and clean up generated resources unless your code explicitly created them.

## Troubleshooting

### The avatar does not generate in Play mode

- Confirm a generator prefab is assigned in **UMA Settings**.
- Confirm **Build Character Enabled** is checked.
- Verify the selected race and its base recipe are in the Global Library.
- Check the Console for missing slots, overlays, recipes, or animator resources.

### The avatar only works when a generator is manually in the scene

Confirm the generator prefab in UMA Settings is a valid prefab with the required generator and mesh-combiner components. UMA should create a scene generator from this setting when one is needed.

### The avatar disappears during a rebuild

Use a current incremental combiner and generator implementation. The existing generated mesh should remain active until the replacement is ready. Also check custom code that disables renderers at build start.

### Wardrobe does not appear

- Verify the wardrobe recipe supports the active race.
- Check wardrobe slot and wardrobe region names.
- Confirm its slots and overlays are indexed.
- Check whether another recipe suppresses the same region.

### Changes appear in assets but not on the character

The Global Library or an existing overlay instance may still hold cached data. Reimport or refresh the edited asset through the appropriate UMA editor workflow, then rebuild the avatar.

### DNA controls are missing

Check the active race's new or legacy DNA setup, DNA collection, and base recipe. A DCA only exposes DNA supported by the current race.

### Generated materials or renderers are null

The build may still be running. Move that work to the character-updated/completed event.

## Related Guides

- [Getting Started](GettingStarted.md)
- [RaceData](RaceData.md)
- [SlotDataAsset](SlotDataAsset.md)
- [OverlayDataAsset](OverlayDataAsset.md)
- [UMA Asset Indexer and Global Library](UMAAssetIndexer.md)
- [UMA Generator Setup](UMAGeneratorSetup.md)
- [Content Creation](ContentCreation.md)
