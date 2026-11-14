# Getting Started with UMA 3

UMA builds characters from parts. You choose a body type, dress it in clothing, adjust its shape and colors, and UMA assembles one optimized character mesh. Nothing is modeled by hand in Unity: you edit settings, and UMA rebuilds the character for you.

This guide takes you from a fresh install to your first customized character. You do not need to write code. A short section for programmers is at the end.

## Words you will see

| Term | What it means |
|---|---|
| **Race** | The base body: its mesh, skeleton, shape controls and which clothing fits it. `Human Male 3.0` and `Human Female 3.0` are the standard UMA 3 races. |
| **Slot** | One piece of mesh, such as a head, torso, or a shirt. |
| **Overlay** | A texture layer painted onto a slot, such as skin, makeup, or a logo. |
| **Wardrobe recipe** | A wearable item, such as a shirt or hairstyle. It adds slots and overlays, and can hide the body underneath. |
| **Shared color** | A named color, such as Skin, Hair or Eyes, that tints every part using that name. |
| **DNA** | Sliders that change the body's shape: height, head size, nose width, and so on. |
| **Dynamic Character Avatar (DCA)** | The component on a GameObject that holds all the choices above and builds the character. |
| **Global Library** | UMA's catalog of every race, recipe, slot and overlay in your project. UMA can only use what is listed there. |
| **Generator** | The behind-the-scenes object that builds characters. UMA creates it for you when one is needed. |

## Step 1: Install UMA's render pipeline support and content

UMA's materials depend on your render pipeline, so install the matching support first.

1. Open **UMA > Welcome to UMA**. It also opens by itself when a project loads, unless you turned that off.
2. Select **Install SRP Package** (later named **Render Pipeline Support**) on the left.
3. Click **Install UMA URP Support** or **Install UMA HDRP Support**, whichever matches your project. Install only one.
4. On the same page, click **Install UMA 3 Content...**. This adds the races, clothing, materials and example scenes.
5. Optional: open **Plugins** to add extras such as Overlay Painter or the Hair Card Editor.

UMA rebuilds the Global Library after installing content. For details, including updating later, see [Package Installation](PackageInstallation.md).

## Step 2: Check the project settings

Open **Edit > Project Settings > UMA** and find **Editor Settings**.

- **Character Prefab** should be `UMADynamicCharacterAvatar`.
- **Generator Prefab** should be `UMA_GLIB`.

Both prefabs are in `<UMA folder>/Core/Defaults`. **UMA Folder** shows where UMA is installed; it is read-only and detected automatically. **Show UMA Toolbar** turns the UMA Toolbar in the Scene view on or off.

You do not need to place a generator in your scenes. When a character needs building, UMA creates one from the **Generator Prefab**. Avoid keeping several generators in one scene.

### Choose speed or fidelity

Open **UMA > Welcome to UMA > Configure UMA** and click one of:

- **Setup UMA for maximum speed**: combines skin textures into atlases and uses faster hair materials. Good for games with many characters.
- **Setup UMA for maximum fidelity**: keeps textures separate for the best quality. Good for cinematics and close-ups.

Characters already in the open scene rebuild to show the change. Your atlas sizes and quality settings are kept. To fine-tune quality and performance, see [Configure UMA and Generator Setup](UMAGeneratorSetup.md).

## Step 3: Make sure the Global Library knows your content

Open **UMA > Global Library**. You should see races and recipes listed.

**When you add new content** (a new shirt, hairstyle, or race), add it to the library in one of these ways:

- Drag the assets or their folder onto the Global Library window's drop area.
- Select them in the Project window, right-click, and choose **Add selected assets to UMA global library**.
- Select them and choose **UMA > Asset Management > Add Selected Assets to Global Library**.

**If things are missing after a large import**, rebuild the whole library:

1. Close the Global Library window.
2. Open **UMA > Global Library Maintenance**.
3. Click **Rebuild Library From Project**.
4. Reopen the Global Library and check that your races and recipes are listed.

See [The Global Library](UMAAssetIndexer.md) for searching, filtering and repair tools.

## Step 4: Create your first character

Choose **GameObject > UMA > Create New Dynamic Character Avatar**. You can also click **Create UMA Character** under **Getting Started** in the Welcome window.

UMA adds a GameObject named `New Dynamic Character Avatar`, selects it, and sets its race to `Human Male 3.0`. **Editor Time Generation** is on by default, so the character appears in the Scene view without pressing Play.

If you can't see it:

- Press **F** in the Scene view to frame the selected character.
- Make sure the scene has a light, and that the character is not under the ground.

You can also drag a ready-made character prefab into the scene: `UMADynamicCharacterAvatar` in `<UMA folder>/Core/Defaults`, or the LOD variants in `<UMA folder>/UMA3/Getting Started`. The same folder has `UMADefaultUtilityEnvironment`, a simple floor-and-light preview set.

## Step 5: Customize the character

Select the character. Its Inspector is where you do almost everything. The tabs at the top switch between:

- **Standard View**: the everyday controls, described below.
- **Advanced View**: every option, including animation, events, and build settings.

Each section has a **Help** button that shows a description of every option in that section.

The Standard View sections are:

| Section | What you do there |
|---|---|
| **Character & race** | Pick the race. Clothing that doesn't support the new race is not equipped. |
| **Editing tools** | Open the editors that work on the built character: the **Vertex Editor** (sculpt, vertex paint, Mesh Modifiers), the **Face Editor** (hide the body under clothing), **Touchup Weights**, and **Create Slot From Scene Mesh**. They need **Editor Time Generation** on. |
| **Generation & avatar definition** | Turn **Editor Time Generation** on or off, **Regenerate Avatar**, and save or load the character's look. |
| **Default wardrobe** | Choose the clothing for each wardrobe region (hair, chest, legs, feet, and so on). Use **Inspect** to open a recipe. |
| **Colors** | Edit the shared colors, such as skin, hair and eyes. Click **Edit** for the full color settings, where **Copy** copies them for pasting elsewhere. |
| **Body shape & DNA** | Drag the DNA sliders to change the body and face. |
| **Randomize** | Pick a random-avatar setup and click **Randomize** for a quick variation. |
| **Presets** | Save the current look as a preset (**Create...**), or **Apply** a saved one. |
| **Animation** | Choose the **Default Animator Controller**, or a different controller per race. |
| **Generation & resource sharing** | Add utility recipes such as colliders, and choose whether this character shares identical meshes and atlases with others or follows the generator's quality profile. |

Changes rebuild the character automatically while **Editor Time Generation** is on.

### Clothing and races

A wardrobe recipe only equips when it supports the character's race (or a race the recipe is marked compatible with). If a shirt disappears after changing race, it was made for a different body.

A wardrobe recipe can do more than add a mesh. It can also tint shared colors, hide body faces under clothing, reshape the body to fit, and adjust DNA. See [Wardrobe Recipe Editor](WardrobeRecipeEditor.md).

### Editor Time Generation and memory

Characters built in the editor use memory and rebuild after script changes. Turn **Editor Time Generation** off on characters you are not currently working on. The UMA Toolbar's pause button stops editor generation for every character at once.

## The UMA Toolbar

The **UMA Toolbar** sits in the Scene view. Its buttons:

- save and restore the Scene camera
- rebuild the selected characters, or all of them
- switch the mesh combiner
- focus on or select parts of the character
- show the selected character's skeleton
- pause editor-time generation
- open diagnostics and the UMA tools menu

Hover a button for its name. The toolbar hides itself inside UMA editing stages, such as the Mesh Hide or Mesh Modifier editors, and returns when you leave them. Turn it off entirely with **Show UMA Toolbar** in the project settings.

## Example scenes

After installing render pipeline support (Step 1), example scenes are in `Assets/UMA/SRP/Samples/Scenes`. Good ones to open first:

- **U3-Character Creator**: a complete in-game character creator.
- **U3-How to equip items**: changing clothing.
- **U3-How to Use a Slider to control DNA**: driving body shape from UI.
- **U3-Generating Random Characters**: crowds of random characters.
- **U3-How to Construct a DCA from scratch** and **U3-How to Construct and load a DCA from a prefab**: setting up characters.
- **U3-How to Load and Save a DCA to a string**: saving a character's look.
- **U3-Decals**, **U3-Ragdolls and Shooting Example** and **U3-Integrating with Timeline**: decals, ragdolls, and characters in Timeline.
- Also included: **U3-Car Scene**, **U3-Sandbox** and **U3-Tools-Photobooth** (recipe icon generation; see [Photobooth Icon Generation](PhotoboothIconGeneration.MD)).

The Welcome window's **Example Scenes** page lists them too.

## What to do next

| I want to... | Read |
|---|---|
| Make my own clothing | [Content Creation](ContentCreation.md), then [Wardrobe Recipe Editor](WardrobeRecipeEditor.md) |
| Stop the body poking through clothing | [Mesh Hide Assets](MeshHideAssets.md) |
| Reshape or sculpt a slot to fit | [Mesh Modifiers](MeshModifiers.md) and [Sculpting Mesh Modifiers](MeshModifierSculpting.md) |
| Add or change body-shape sliders | [DNA Creation Guide](DNACreationGuide.md) |
| Create a new race | [Creating a New Race](CreatingANewRace.md) and [Race Data](RaceData.md) |
| Add tattoos, scars or decals | [Decals](Decals.md) |
| Make hair sway | [Hair Animation](HairAnimation.md) |
| Use UMA 2 clothing on UMA 3 characters | [Slot Rig Conversion](SlotRigConversion.md) |
| Make clothing follow body shapes | [Blendshape Transfer](BlendshapeTransfer.md) |
| Generate random characters | [Random Avatars](RandomAvatar.md) |
| Improve quality or performance | [Configure UMA and Generator Setup](UMAGeneratorSetup.md) |
| Ship characters in a build | [Addressables](Addressables.md) |

## Troubleshooting

### The character doesn't appear

1. Check that **Editor Time Generation** is on, or press Play.
2. Switch to **Advanced View**, open the **Build behaviour** panel, and check that **Build Character Enabled** is on.
3. Check that the race is listed in the Global Library. If not, rebuild the library (Step 3).
4. Check that **Generator Prefab** is set in the project settings (Step 2).
5. Look in the Console for messages about missing slots, overlays or shaders.

### The character is pink

Its materials don't match your render pipeline. Install the matching UMA URP or HDRP support from the Welcome window (Step 1).

### A piece of clothing doesn't show up

- Does the recipe support this race? Open it with **Inspect** and check its compatible races.
- Is the recipe in the Global Library?
- Is another item hiding or replacing it? Some recipes hide others in the same region.

### The DNA sliders are missing

The race has no DNA set up for this system. Open the race asset and check its DNA settings. See [Race Data](RaceData.md).

### It works in the editor but not in a build

Your content may not be included in the build. Make sure everything is in the Global Library, then see [Addressables](Addressables.md).

## For programmers

The Dynamic Character Avatar can be changed from scripts. Generation is queued, so wait for the character's events before reading its finished mesh.

```csharp
// Batch several changes into one build.
avatar.BuildCharacterEnabled = false;
avatar.ChangeRace("Human Female 3.0");
avatar.SetSlot("Chest", "YourShirtRecipeName");
avatar.SetDNA("height", 0.65f);
avatar.BuildCharacterEnabled = true;

// Change a shared color.
var skin = new OverlayColorData(3);
skin.color = new Color(0.9f, 0.72f, 0.62f);
avatar.SetRawColor("Skin", skin);
```

Save and load a character's look as an Avatar Definition:

```csharp
string saved = avatar.GetAvatarDefinitionString(false);
avatar.LoadAvatarDefinition(saved);
```

`SetColor` and `SetColorValue` are obsolete; use `SetRawColor`. The older `GetCurrentRecipe()` and recipe-string loading remain only for compatibility. See [Dynamic Character Avatar](DynamicCharacterAvatar.md) for the full API and events.
