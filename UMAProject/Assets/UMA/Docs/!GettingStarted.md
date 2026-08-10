# Getting Started with UMA 3

This guide builds a first UMA 3 character using the current project settings and Global Library workflow.

## Before You Begin

Confirm the UMA package is present. The shared starter prefabs are in:

`Assets/UMA/Core/Defaults`

The folder contains:

- `UMA_GLIB`: generator and supporting scene setup
- `UMADynamicCharacterAvatar`: standard character prefab

Optional UMA 3 sample prefabs are in:

`Assets/UMA/UMA3/Getting Started`

That folder contains:

- `UMADynamicCharacterAvatar-LOD`: character prefab with LOD support
- `UMARandomGeneratedCharacter` and `UMARandomGeneratedCharacterV2`: random-generation examples
- `UMADefaultUtilityEnvironment`: simple preview environment

## Configure UMA Settings

Open:

`Edit > Project Settings > UMA`

In the Editor Settings section:

1. Assign the standard character prefab to `Character Prefab`.
2. Assign `UMA_GLIB` to `Generator Prefab`.
3. Confirm the UMA folder and shader folder point to the installed UMA locations.

The generator prefab is important even when it is not placed in every scene. At runtime, UMA can instantiate the configured generator when a DCA requests generation and no scene generator exists.

Do not intentionally keep several active generators unless the project has a tested custom architecture.

For generator performance settings, see [UMAGeneratorSetup.md](UMAGeneratorSetup.md).

## Build the Global Library

Open:

`UMA > Global Library`

The Global Library is UMA's project-wide asset index. It resolves races, recipes, slots, overlays, materials, DNA assets, and other UMA content.

For a new import or after large content changes:

1. Close the Global Library window if maintenance requests it.
2. Open `UMA > Global Library Maintenance`.
3. Select `Rebuild Library From Project`.
4. Reopen the Global Library and confirm races and recipes are listed.

For day-to-day content additions, drag the new assets or their folder onto the Global Library's add area, or select them in the Project window and use:

`Assets > Add selected assets to UMA global library`

See [UMAAssetIndexer.md](UMAAssetIndexer.md).

## Create the First Character

Use:

`GameObject > UMA > Create New Dynamic Character Avatar`

UMA creates a GameObject with `DynamicCharacterAvatar` and selects `Human Male 3.0` as the initial race.

Alternatively, drag `UMADynamicCharacterAvatar.prefab` into the scene.

Make sure:

- `Build Character Enabled` is on.
- The selected race exists in the Global Library.
- The character is in front of a camera.
- The scene has suitable lighting.
- The character is not below or inside the ground.

Press Play. If no generator was already loaded, UMA uses the Generator Prefab from UMA Settings.

## Edit the Character

The DCA inspector is the main artist-facing character control.

### Change race

Choose a race in the Active Race control. Wardrobe recipes only equip when they support that race or a configured cross-compatible race.

### Add wearable items

Add recipes to the default wardrobe or wearable controls. A wearable can contribute:

- Mesh slots
- Texture overlays
- Shared colors
- Mesh hides
- Mesh modifiers
- DNA overrides
- Suppression or replacement rules

### Change colors

Open the character colors section and edit shared colors such as skin, hair, and eyes. Shared colors may control ordinary tint channels or shader property blocks, depending on the recipe and material.

### Change DNA

Open the DNA customization section. DNA can drive bone changes, poses, blendshapes, mesh modifiers, overlay transforms, and shared colors.

See [DNACreationGuide.md](DNACreationGuide.md).

## Editor-Time Preview

Enable `Editor Time Generation` on a DCA to preview the generated character without entering Play mode.

Editor-time generation is useful for:

- Wardrobe authoring
- DNA setup
- Mesh modifiers
- Mesh hides
- Material and overlay checks

Generated editor previews consume memory and may rebuild after script reloads. Disable editor-time generation on characters that do not need a live preview.

## First Runtime Changes

Common DCA calls include:

```csharp
avatar.ChangeRace("Human Female 3.0");
avatar.SetSlot("Chest", "Example Shirt");
avatar.SetDNA("height", 0.65f, rebuild: true);
avatar.SetColorValue("Skin", new Color(0.9f, 0.72f, 0.62f));
avatar.UpdateColors(true);
```

To batch several changes into one build:

```csharp
avatar.BuildCharacterEnabled = false;

avatar.ChangeRace("Human Female 3.0");
avatar.SetSlot("Chest", "Example Shirt");
avatar.SetDNA("height", 0.65f);

avatar.BuildCharacterEnabled = true;
```

Generation is queued. Use character completion events when later code needs the finished renderer or mesh.

See [DynamicCharacterAvatar.md](DynamicCharacterAvatar.md).

## Save and Load

Use `AvatarDefinition` for current DCA save data:

```csharp
string json = avatar.GetAvatarDefinitionString(false, false);
avatar.LoadAvatarDefinition(json);
```

The older `GetCurrentRecipe()` and old recipe-string loading APIs remain only for compatibility and should not be used for new code.

## Recommended Example Scenes

Current UMA 3 examples are under:

`Assets/UMA/UMA3/Scenes`

Good starting points include:

- `U3-Character Creator`
- `U3-How to Construct a DCA from scratch`
- `U3-How to Construct and load a DCA from a prefab`
- `U3-How to equip items`
- `U3-How to Load and Save a DCA to a string`
- `U3-How to Use a Slider to control DNA`
- `U3-Generating Random Characters`

## Troubleshooting

### The character does not generate

Check:

1. `Build Character Enabled` is on.
2. UMA Settings has a valid Generator Prefab.
3. The race is indexed.
4. The race has a valid base recipe or FBX route.
5. The Console does not report missing slots, overlays, or shaders.

Adding `UMA_GLIB` manually can be used as a diagnostic, but a correctly configured Generator Prefab should make that unnecessary.

### The character works in the editor but not in a build

The content may not be included in Resources or Addressables. Verify that the race and all recipe dependencies are indexed, then review [Addressables.md](Addressables.md).

### The character is pink

The assigned Unity material or shader is missing for the active render pipeline. Import the correct UMA SRP package and verify the `UMAMaterial` template.

### Wardrobe does not appear

Check the recipe's compatible races, wardrobe region, material compatibility, suppression rules, and whether the recipe is indexed.

### DNA sliders are missing

Confirm the race enables the intended DNA system and has its DNA collection or legacy converters assigned.

## Next Steps

- [ContentCreation.md](ContentCreation.md)
- [WardrobeRecipeEditor.md](WardrobeRecipeEditor.md)
- [RaceData.md](RaceData.md)
- [UMAGeneratorSetup.md](UMAGeneratorSetup.md)
- [RandomAvatar.md](RandomAvatar.md)
