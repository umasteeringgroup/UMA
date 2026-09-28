# Avatar presets

In a DynamicCharacterAvatar inspector, open **Standard View > Presets > Create**.
Choose the DNA, named colors and wardrobe recipes to include. All begin selected;
each category has All/None controls. The popup snapshots the current appearance
when opened, including color shader parameters.

Choose **Draw face icon in Scene View**, frame the face, and drag a square
around it. The selection is always constrained to equal width and height; keep
the entire square inside the view. Alt-drag retains Scene View navigation; Escape cancels capture.
The capture is the visible Scene View image, so hide unwanted objects and selection
outlines before capturing. Save writes a 256x256 PNG beside the preset asset.
Recapturing while editing creates a new PNG; the old PNG is retained for other users.

Assign a preset in the avatar's Presets section and choose Apply. A preset with a
race changes the avatar to that RaceData before applying its selected values.
Unselected DNA, colors and wardrobe regions remain unchanged when the race stays
the same. Included wardrobe recipes use UMA's normal slot assignment. A legacy
preset without a race requires an avatar with an initialized race.
Edit uses the selected source avatar's current values; it does not apply the preset
first. The preset asset inspector also offers the authoring popup.

## Runtime

Reference the asset from your component so Unity includes it in the build:

```csharp
public UMA.CharacterSystem.UMAPreset preset;
public UMA.CharacterSystem.DynamicCharacterAvatar avatar;

// The preset changes to its RaceData when necessary, then applies its values.
public void ApplySelection() => preset.ApplyTo(avatar);
```

`ApplyTo(avatar)` merges the selected values and requests one rebuild.
Use `avatar.InitializeFromPreset(preset)` or `preset.ApplyTo(avatar, false)`
to merge without rebuilding, then build once after other changes.
Do not apply with rebuilding from every CharacterCreated/CharacterUpdated callback;
that can cause a rebuild loop.

`Icon` is a runtime Texture2D, suitable for a RawImage or UI Toolkit Image.
In the sample character creator, assign assets under the NewUMAGUI inspector's
**Presets** foldout. The information tab groups them under each RaceData Friendly Name,
using the same item grid as the wardrobe tabs. Click an icon to change race when
needed and apply the preset; hover displays its name. Presets without icons show
their names.
`Definition` stores the selected AvatarDefinition values. Wardrobe asset references
keep selected recipes reachable in builds. The target race and its content must
still be available through the normal UMA setup.

UMAPreset is now a ScriptableObject: use CreateInstance<UMAPreset>() rather than
new UMAPreset() in code. Existing .umapreset JSON can still be loaded with
InitializeFromPreset(string); the editor Load Preset command retains that import.
Use the new popup to save imported characters as modern preset assets.

## Importing old presets

Use **UMA > Presets > Import Legacy Preset...** to convert a `.umapreset` JSON file
directly to a new asset, without changing a scene avatar. The preset asset inspector
also has **Import legacy .umapreset...** to replace its DNA, colors and wardrobe
while keeping its existing race and icon (with confirmation and Undo).
Disabled legacy wardrobe entries are excluded. Missing or ambiguous recipe assets
must be resolved before import; the original file is never changed.

Legacy files contain no race or icon. Newly converted presets therefore have no
race restriction or icon; choose an appropriate avatar when applying them. To add
an icon, apply the converted preset to that avatar, then open Edit in the preset
popup and capture its face. Saving there also records the avatar's race.

The last successfully used preset-save folder is remembered per project and user,
including after restarting Unity. New saves and conversions start there. If the
folder no longer exists, the default is `Assets`.
