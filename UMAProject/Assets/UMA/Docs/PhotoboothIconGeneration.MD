# Photobooth Recipe Icon Generation

The UMA Photobooth scene creates race-specific thumbnail sprites for wardrobe recipes. It is intended for technical artists and character artists who need a consistent set of icons for character creators, inventory screens, or wardrobe browsers.

The tool can process one wardrobe region at a time, such as `Hair`, `Chest`, or `Feet`, or generate icons for every recipe available to the selected race. For each recipe, it saves a PNG, imports it as a Unity Sprite, and assigns that Sprite to the recipe's thumbnail entry for the active race.

## Photobooth Scene

Open:

`Assets/UMA/UMA3/Scenes/U3-Tools-Photobooth.unity`

The important scene objects are:

- `DynamicAvatar1`: the character used to build and display each recipe.
- `IconCreator`: the component that controls output, region-to-camera assignments, preview sizing, and generation.
- Region cameras such as `HeadCam`, `ChestCam`, `HandsCam`, `LegsCam`, `FeetCam`, `BodyCam`, and `FaceCam`.
- The `Key`, `Fill`, and `Back` lights, which establish the icon lighting.

The supplied camera target textures are 128 by 128 pixels.

## Before Entering Play Mode

### 1. Select the Race

Set the character to the race whose recipe icons you want to generate before entering Play Mode:

1. Select `DynamicAvatar1` in the Hierarchy.
2. In the Dynamic Character Avatar inspector, find `Active Race`.
3. Choose the required race, for example `Human Female 3.0` or `Human Male 3.0`.
4. Allow the editor-time character generation to finish.
5. Save the scene if this should remain the Photobooth's default race.

Use `Refresh Race List` if a newly created race is not present in the `Active Race` menu. The race and its recipes must be available through the UMA Asset Indexer. The selected RaceData supplies the region names shown by the Photobooth, while the index supplies the recipes available in each region.

The selected race matters in three ways:

- Only recipes available to that race, including applicable cross-compatible recipes, are processed.
- The RaceData's wardrobe regions populate the runtime `Region to Render` menu.
- Each generated Sprite is assigned to the recipe as the thumbnail for this specific race. Existing thumbnails for other races are preserved.

### 2. Establish the Character's Presentation

Treat `DynamicAvatar1` as the neutral presentation character for the batch:

- Set DNA, skin, colors, and other presentation choices before entering Play Mode.
- Review any preload or default wardrobe recipes.
- Avoid unrelated wardrobe pieces that obscure the item being photographed.

The generator replaces the recipe in the region currently being processed. Wardrobe in other regions can remain visible, so the starting character should be intentionally styled. A neutral base character generally produces the most reusable catalog icons.

### 3. Check the Icon Creator

Select `IconCreator` in the Hierarchy and review its inspector.

#### Root Folder

`Root Folder` is the parent folder used for generated icons. The supplied scene uses:

`Assets/UMA/UMA3/Wearables/Icons`

Keep the output inside the project's `Assets` folder. Unity can only import and automatically assign a generated image when it resolves to an asset-relative path.

The final folder structure is:

`<Root Folder>/<Region>/<Race>/<Sequence>_<Recipe Name>.png`

For example:

`Assets/UMA/UMA3/Wearables/Icons/Hair/Human Female 3.0/1_Hair_Bob_Recipe.png`

The sequence number is based on the recipe's current order within that region. Adding or reordering indexed content can change the number used on a later run. A file with the same generated path is overwritten; older files with different sequence names are not removed automatically.

`Browse` opens a folder picker. An empty Root Folder writes region folders directly below `Assets`.

#### Camera Regions

Each Camera Region entry connects one capture camera to one or more wardrobe regions.

- `Expand All` and `Collapse All` open or close every camera entry.
- `Validate Regions` compares the selected race's region list with the configured cameras and reports any regions that have no camera.
- `Camera` is the camera used to photograph the assigned regions.
- `Regions` lists the wardrobe regions handled by that camera.
- `Add Region` adds another region from the active RaceData.
- `X` removes a region from an entry.
- `Remove` deletes the entire camera entry.
- `Add Camera Region` creates a new, empty camera mapping.

Avoid assigning the same region to more than one camera. If duplicate mappings exist, the first matching entry in the list is used.

The supplied scene maps regions as follows:

| Camera | Regions |
| --- | --- |
| `BodyCam` | `Tattoo`, `Waist` |
| `FeetCam` | `Feet` |
| `HeadCam` | `Helmet`, `Ears`, `Hair` |
| `ChestCam` | `Shoulders`, `Chest`, `Arms`, `TopUnderlayer`, `BottomUnderlayer` |
| `HandsCam` | `Hands` |
| `LegsCam` | `Legs` |
| `FaceCam` | `Face`, `Complexion`, `Scar`, `Eyebrows`, `Beard`, `Eyes` |

Custom races may use different region names. Add every custom region to an appropriate camera and run `Validate Regions` before generating a full set.

#### Other Icon Creator Controls

| Control | Purpose |
| --- | --- |
| `Avatar` | The Dynamic Character Avatar whose race, appearance, and available recipes are used. In the supplied scene this is `DynamicAvatar1`. |
| `Icon Dimensions` | A setup field currently retained on the component. Camera-captured image dimensions come from the assigned camera's target Render Texture, not this field. |
| `Preview Size` | Width and height of each camera preview in the Game view. It does not change the saved image resolution. |
| `Scroll Area Height` | Adds vertical space around the horizontally scrolling camera preview strip. |
| `Current Camera Index` | A retained setup field that is not required by the current region-based workflow. |
| `Brightness` | Brightens or darkens texture-generated thumbnails from -1 to 1. It does not affect camera captures. |
| `Contrast` | Adjusts texture-generated thumbnail contrast from -1 to 1. It does not affect camera captures. |

To change the resolution of camera-generated icons, change the target Render Texture used by the applicable region camera. Keep the target textures consistent across cameras if the UI expects a uniform icon size.

## Framing and Art Direction

Before a production batch, enter Play Mode and inspect the camera previews at the top of the Game view.

For framing changes:

1. Exit Play Mode.
2. Select the relevant region camera.
3. Adjust its transform, Field of View, and target Render Texture as needed.
4. Adjust the `Key`, `Fill`, and `Back` lights for readable form and material response.
5. Enter Play Mode again and check the preview.

Aim for consistent scale, padding, view direction, and lighting across related regions. Leave enough margin for hair, hats, shoulder armor, wide sleeves, skirts, and boots that extend beyond the base body silhouette.

Make scene and camera changes outside Play Mode so Unity does not discard them when Play Mode ends.

## Generating Icons

Enter Play Mode and use the controls in the Game view.

The preview strip shows each configured camera, its current Render Texture, and the regions assigned to it. Scroll horizontally when all previews do not fit.

### Generate One Region

Use this for testing art direction or updating one category:

1. Open the `Region to Render` dropdown.
2. Select a region such as `Hair`, `Chest`, or `Feet`.
3. Click `Render Now`.
4. Allow the batch to finish before starting another render.
5. Inspect the generated folder and several updated recipes before processing more regions.

`Render Now` processes every available `UMAWardrobeRecipe` in the selected region. It does not render only the wardrobe item currently worn by the avatar.

### Generate Every Region

Use this after the race, cameras, lighting, and output folder have been validated:

1. Click `Generate All Icons`.
2. Do not click either generation button again while the batch is running.
3. Allow UMA to build and capture every available recipe for the active race.
4. Review the Console for missing recipe data, textures, cameras, or target textures.
5. Exit Play Mode after file and recipe updates have completed.

`Generate All Icons` iterates all recipe regions returned by the UMA Asset Indexer for the active race. A region without a configured camera cannot produce camera-captured icons, so validate custom race mappings first.

For a large library, generate one region as a proof batch before using `Generate All Icons`.

## What Happens During Generation

For a normal camera-generated wardrobe recipe, the Photobooth:

1. Clears the current item in the region.
2. Equips the recipe on `DynamicAvatar1`.
3. Builds the UMA character and waits for the result to be displayed.
4. Finds the camera assigned to the recipe's region.
5. Reads the camera's target Render Texture.
6. encodes the captured pixels as a PNG in the region/race output folder.
7. imports the PNG into Unity.
8. sets the Texture Type to `Sprite (2D and UI)` with a single Sprite.
9. disables mipmaps and enables alpha-as-transparency.
10. adds or updates the recipe's thumbnail record for the active race.
11. stores both the Sprite reference and the asset-relative filename on the recipe.
12. marks and saves the recipe asset.

This means the generated result includes both a new or updated image asset and a serialized change to each processed recipe. When using source control, include:

- The generated `.png` files.
- Their Unity `.meta` files.
- The modified `UMAWardrobeRecipe` `.asset` files.

Open a processed recipe and check `Race Settings > Thumbnails` to verify the Sprite for the intended race.

## Texture-Generated Thumbnails

Some recipes, particularly texture-driven face details, can generate their thumbnail from a source texture instead of photographing the avatar.

On the wardrobe recipe:

- Enable `Generate from Texture?`.
- Set `Thumbnail Rect (if generating from texture)`.

`Thumbnail Rect` uses normalized texture coordinates:

- `X` and `Y` set the lower-left corner of the crop.
- `W` and `H` set the crop size.
- Values are normally between 0 and 1.

For example, `X 0.25`, `Y 0.25`, `W 0.5`, `H 0.5` crops the centered half of the source texture.

For this mode, the Photobooth:

1. finds the first recipe slot that contains overlays.
2. uses the first texture from that slot's first overlay.
3. crops it using `Thumbnail Rect`.
4. converts the cropped RGB result to grayscale.
5. applies the Icon Creator's `Brightness` and `Contrast`.
6. preserves the source alpha.
7. saves, imports, and assigns the result in the same way as a camera-generated icon.

The texture-generated output keeps the cropped texture's pixel dimensions; it is not resized to the camera Render Texture size. Use a consistent crop size when uniform UI dimensions are required.

If a texture-generated thumbnail fails, check that the recipe contains a valid slot, overlay, and first texture. The Photobooth reports missing data in the Console.

## Quality-Control Checklist

Before committing a generated icon set, check:

- The correct race was active before Play Mode.
- The intended recipes are indexed and compatible with that race.
- Every race region has a camera mapping.
- Similar item categories have consistent framing and silhouette scale.
- Transparent edges are clean and do not clip.
- Hair, helmets, wide shoulders, hands, skirts, and footwear fit inside the frame.
- Material values remain readable at the final UI display size.
- Texture-generated icons use an intentional crop and consistent dimensions.
- Each recipe contains the thumbnail under the correct race.
- Generated PNGs, `.meta` files, and modified recipe assets are included in source control.

## Troubleshooting

### The Region Menu Is Empty

- Exit Play Mode and set `DynamicAvatar1 > Active Race`.
- Confirm that the RaceData is indexed and has wardrobe regions.
- Confirm that the `IconCreator > Avatar` field references `DynamicAvatar1`.

### A Recipe Was Not Generated

- Confirm that it is a `UMAWardrobeRecipe`.
- Confirm that its Wardrobe Slot matches the intended region.
- Confirm that it is indexed.
- Confirm that it is compatible or cross-compatible with the active race.
- For texture generation, confirm that the recipe contains a valid overlay texture.

### A Region Does Not Produce Camera Icons

- Run `Validate Regions`.
- Assign the region to a camera.
- Confirm that the camera GameObject is active.
- Confirm that the camera has a target Render Texture.

### The Icon Is Framed Incorrectly

Adjust the camera assigned to that region, not the Main Camera. The saved PNG comes from the region camera's target Render Texture.

### The PNG Exists but the Recipe Has No Sprite

Keep `Root Folder` inside `Assets`. Files written outside `Assets` cannot be imported and assigned as Unity assets by this workflow.

### Icons Include Unwanted Clothing or Styling

Review `DynamicAvatar1` DNA, colors, preload wardrobe recipes, and race defaults before entering Play Mode. Generation changes the target region; the rest of the presentation character can remain visible.

### Old or Duplicate-Looking PNGs Remain

Sequence prefixes can change when the indexed recipe order changes. The generator overwrites exact path matches but does not remove obsolete filenames. Compare the current recipe Sprite references before removing stale files and their `.meta` files.
