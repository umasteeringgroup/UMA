# Icon Creator and Thumbnail Sprite Atlases

UMA's Icon Creator generates wardrobe thumbnails for the active race and can optionally group those thumbnails into Sprite Atlas V2 assets.

The Icon Creator and atlas builder are separate operations:

- Thumbnail generation works without Sprite Atlases.
- Atlas generation is opt-in and requires Sprite Atlas V2.
- Generated PNG files and atlas assets are project content. They are not required for the Icon Creator code itself to compile or run.

## Configure the Icon Creator

Open a scene containing an `IconCreator` component and assign:

- A `DynamicCharacterAvatar` with an active race.
- One or more cameras with target Render Textures. In Unity 6.3, each camera output texture must have
  a Depth Stencil Format; a 24-bit depth/stencil buffer is sufficient for Icon Creator capture.
- The wardrobe regions handled by each camera.
- A Root Folder inside the project's `Assets` folder.
- The desired Icon Dimensions.

The Root Folder controls both thumbnail output and atlas scope. For example:

`Assets/UMA/UMA3/Wearables/Icons`

Thumbnails are written beneath that folder using the following layout:

`<Root Folder>/<Wardrobe Region>/<Race Name>`

## Generate Thumbnails

Use `Render Now` to generate the selected wardrobe region or `Generate All Icons` to process every region available to the active avatar.

Camera-rendered thumbnails use Icon Dimensions for their final pixel size. Capture Supersampling temporarily renders the assigned camera at a higher resolution and downsamples the result. This improves edge quality without creating additional project assets. Temporary Render Textures are released after each capture.

Texture-derived thumbnails preserve the source crop dimensions by default. Enable Resize Texture Derived Thumbnails when those crops should instead be resampled to Icon Dimensions.

### File names and references

When a recipe already references a thumbnail in the target output folder, Icon Creator overwrites that file in place. The existing Unity `.meta` file and asset GUID are preserved.

When no thumbnail exists in the target folder, Icon Creator creates a deterministic name using the recipe name and the first eight characters of the recipe asset GUID. The GUID suffix prevents recipes with identical names from overwriting one another. Subsequent runs preserve and overwrite the newly assigned thumbnail path.

Before overwriting imported thumbnail files, Icon Creator temporarily releases managed Sprite Atlas and Sprite references. Recipe references are restored after generation and saved only when restoration succeeds.

## Generate Sprite Atlases

Sprite Atlas generation is optional. To enable it:

1. Open `Edit > Project Settings > Editor`.
2. Under `Sprite Atlas`, set Mode to `Sprite Atlas V2 - Enabled`.
3. Select the GameObject containing `IconCreator`.
4. In the Icon Creator inspector, select `Rebuild Thumbnail Atlases`.

`Sprite Atlas V2 - Enabled for Builds` only activates packed atlas textures during builds. Icon Creator requires `Sprite Atlas V2 - Enabled` so the atlas can also be inspected and validated in the Editor. If that mode is not enabled, the atlas builder stops without changing assets and displays an error explaining the required setting.

The atlas builder does not support or create Sprite Atlas V1 assets.

## Atlas Scope and Grouping

The builder scans wardrobe recipe thumbnail references, but includes a Sprite only when its source asset is physically beneath the configured Root Folder. References to thumbnails elsewhere in the project are left unchanged and are not added to generated atlases.

Included Sprites are grouped by:

- Race name
- Wardrobe region

Generated atlases are written to:

`<Root Folder>/SpriteAtlases`

Atlas names use this pattern:

`UMAIcons_<Race>_<Wardrobe Region>.spriteatlasv2`

Rebuilding an existing group replaces its packable list while preserving the atlas asset path and GUID. Atlases for groups that no longer contain eligible thumbnails have their packable lists cleared.

## Atlas Import Settings

Generated atlases use the following defaults:

- Included in builds
- Rotation disabled
- Padding: 4 pixels
- Maximum texture size: 2048
- Bilinear filtering
- Mipmaps disabled
- sRGB enabled
- Compressed using the default platform settings

The original thumbnail Sprites remain atlas packables and remain referenced by wardrobe recipes. The builder does not delete source PNG files.

## Conflicts

A Sprite can belong to only one generated race-and-region group. If recipes attempt to assign the same Sprite to conflicting groups, the first deterministic assignment is retained and a warning is logged.

The builder also warns when an eligible Sprite is already included by a Sprite Atlas V2 asset outside the managed `SpriteAtlases` folder. External atlases are never modified automatically.

## Recommended Regeneration Workflow

1. Configure the active race, cameras, Render Textures, and Icon Dimensions.
2. Generate one region and inspect representative thumbnails.
3. Generate all required regions for each supported race.
4. Confirm recipe thumbnail references point beneath the configured Root Folder.
5. Enable `Sprite Atlas V2 - Enabled` if atlasing is desired.
6. Rebuild Thumbnail Atlases.
7. Inspect the atlas pack previews and test the character creator UI.
8. Make a player build and confirm the expected atlases are included.

Generated atlases can be removed and rebuilt from the current recipe thumbnail references. Normally keep existing thumbnail PNGs and their `.meta` files: deleting a referenced thumbnail can cause Icon Creator to create a new deterministic filename and assign a new recipe reference. Review generated asset changes before committing them to source control.

## Troubleshooting

### Render Graph reports that the output Render Texture needs a depth buffer

Select every Render Texture assigned to an Icon Creator camera and set **Depth Stencil Format** to a
supported non-None format. A 24-bit depth/stencil format is sufficient for thumbnail capture. Icon
Creator also gives its temporary supersampled camera target a depth/stencil attachment automatically.

### Atlas generation reports that Sprite Atlas V2 is required

Set `Edit > Project Settings > Editor > Sprite Atlas > Mode` to `Sprite Atlas V2 - Enabled`. Thumbnail generation remains available when atlasing is disabled.

### A recipe thumbnail is not included in an atlas

Check that:

- The recipe has a Sprite assigned for the intended race.
- The recipe has a wardrobe region.
- The Sprite asset is beneath the configured Root Folder.
- The Sprite is not already assigned to a conflicting generated or external atlas.

### A regenerated thumbnail receives a GUID suffix

The recipe did not reference an existing thumbnail inside its target output folder. The suffix provides a stable, collision-resistant name for the new file. Later runs overwrite that assigned path.

### Thumbnail replacement fails on Windows

Close inspectors or external programs displaying the PNG and retry. Icon Creator releases its managed Unity references and retries transient file-sharing failures, but another process can still hold an exclusive file handle.
