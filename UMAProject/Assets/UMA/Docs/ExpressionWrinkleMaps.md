# Expression Wrinkle Maps: An Artist's Guide

Expression wrinkles are the creases that appear only when a face moves: forehead lines when the brows rise, frown lines
between the brows, crow's feet in a smile, lip lines in a pucker. UMA adds them with a pair of textures and a skin shader
that fades each area of the face in and out with the character's expressions. The face at rest stays smooth.

This guide explains how the wrinkle maps work, every menu that makes or changes them, and the two ways to edit them by
hand: in an image editor, or in [Overlay Painter](../OverlayPainter/Documentation/ExpressionWrinkleMaps.md).

**Requirements:** Unity 6.3 or newer, URP, the UMA 3 base content (the UMA 3 head is `UMA30_Body_UDIM1001`).
Overlay Painter is an optional plugin; the image-editor workflow needs only UMA.

## Contents

- [How wrinkle maps work](#how-wrinkle-maps-work)
- [The files](#the-files)
- [Turning wrinkles on](#turning-wrinkles-on)
- [Looking at the wrinkles](#looking-at-the-wrinkles)
- [Menu reference](#menu-reference)
- [Editing the wrinkle map](#editing-the-wrinkle-map)
- [Editing in an image editor](#editing-in-an-image-editor)
- [Editing in Overlay Painter](#editing-in-overlay-painter)
- [Using an edited map](#using-an-edited-map)
- [Height and value reference](#height-and-value-reference)
- [Troubleshooting](#troubleshooting)
- [Technical notes](#technical-notes)

---

## How wrinkle maps work

The shader **UMA/URP/Realistic Skin SSS Wrinkles** is Realistic Skin SSS with two extra textures:

| Texture | What it holds |
|---|---|
| **Wrinkle map** (`_WrinkleMap`) | The wrinkles themselves. **Red and green** tilt the surface (a normal map *on top of* the skin's own normal map, 0.5 = flat). **Blue** is crease depth: it darkens the light inside creases. **Alpha** is flush: compressed skin turns slightly redder. |
| **Zone map** (`_WrinkleControlMap`) | Which part of the face each texel belongs to. The face is divided into 15 **zones**, and every wrinkle belongs to one zone. |

Each zone has a **weight** from −1 to 1:

- **0** (the face at rest): the zone's wrinkles are invisible.
- **Positive** (the skin compresses): the wrinkles appear. 1 shows them at full depth.
- **Negative** (the skin stretches): the wrinkles stay hidden and the skin's own pores and fine texture smooth out a
  little (**Stretch Smoothing** on the material).

With every weight at 0 the shader skips all wrinkle work and renders exactly like Realistic Skin SSS.

The weights come from the character's facial expressions: an **Expression Profile** says, for example, that *left brow
up* raises the left forehead zone and *brows in* raises the frown lines. The **UMA Expression Wrinkles** component reads
the expressions every frame and sets the weights.

### The zones

Left and right are the character's own sides.

| Id | Zone | Typical expression |
|---|---|---|
| 1 / 2 | Forehead left / right | Brows up (surprise). The two halves meet at the middle, so raising both brows gives continuous lines. |
| 3 | Glabella (vertical frown lines) | Brows in, frown |
| 4 | Nose root (horizontal lines at the top of the nose) | Brows down, sneer |
| 5 | Bunny lines (sides of the nose) | Sneer, upper lip up |
| 6 / 7 | Crow's feet and lower lid left / right | Squint, smile |
| 8 / 9 | Nasolabial fold left / right | Smile |
| 10 / 11 | Mouth corner left / right | Smile, frown, pucker |
| 12 | Lip lines | Pucker, visemes U, CH, O |
| 13 | Chin | Pucker, frown, lower lip up |
| 14 / 15 | Neck left / right | Reserved (the UMA 3 neck is not on the head texture) |

A wrinkle shows **only inside its zone**, at that zone's weight. Paint a new line outside every zone and it never
appears.

### Where the maps come from

The UMA 3 maps are generated, not painted: **UMA > Skin > Bake Procedural Wrinkle Maps** lays out every line as a curve
from facial landmarks on the head mesh and computes its relief in 3D, so nothing breaks at UV seams. The same bake
writes the **wrinkle height**, a grayscale picture of the relief, next to the map. Hand editing always works on that
height: the red and green of the wrinkle map are *slopes* computed from it, which cannot be painted directly.

---

## The files

| File | Purpose |
|---|---|
| `UMA3/Textures/UMA3_Wrinkles.1001.png` | The UMA 3 wrinkle map (RG slope, B crease, A flush). Linear, BC7. |
| `UMA3/Textures/UMA3_WrinkleZones.1001.png` | The UMA 3 zone map. Linear, **uncompressed** (zone ids must stay exact). |
| `UMA3/Textures/UMA3_Wrinkles.1001_height.png` | The editable wrinkle height the map was made from. 16-bit grayscale, linear, uncompressed. **Editor only**: nothing references it, so it never goes into a build. |
| `UMA3/Overlays/UMA30_ExpressionWrinkles_UDIM1001` | The wrinkle overlay: the two maps in the wrinkle channels and nothing in the others, so it changes only the wrinkles. |
| `SRP/Shaders/Skin/Examples/UMA3_SkinRealisticSSS_Wrinkles` | The UMA Material (and Material) used for the head: the Natural skin on the wrinkle shader. Its two wrinkle channels have **Never Compress** set. |
| `UMA3/Races/Expressions/UMA 30 Wrinkle Profile` | Which expression moves which zone, for the UMA 3 expression set. |

Every wrinkle map keeps its height next to it under the same name plus `_height`: the bake, the source import and
Overlay Painter's wrinkle export all write it. Keep the two together when you copy or move a map.

---

## Turning wrinkles on

### On one avatar

1. Select a UMA 3 avatar (a DynamicCharacterAvatar) in the scene.
2. Choose **UMA > Skin > Add Expression Wrinkles to Selected Avatar**.

This adds the **UMA Expression Wrinkles** component and rebuilds the avatar. It also creates the UMA 3 wrinkle assets
if they are missing. The component's fields:

| Field | Meaning |
|---|---|
| **Wrinkle Overlay** | The overlay with the wrinkle maps. Empty: the Global Library's `UMA30_ExpressionWrinkles_UDIM1001`. Point it at your own exported overlay to use edited maps. |
| **Wrinkle Material** | Optional. A UMA Material for the head with the wrinkle channels (for a skin other than Natural; see below). Empty: the overlay's UMA Material. |
| **Head Slots** | Slots that get the wrinkle overlay. The UMA 3 head, or a race's baked copy of it, by default. |
| **Expression Profile** | Drives the zone weights from expressions every frame. Clear it to set the weights by hand on the component or from code (`SetWeight`). |

The component never changes slot, overlay or recipe assets: it adds the overlay to the head at build time.

### On every avatar

**Project Settings > UMA > Expression Wrinkles**:

- **Per Avatar** (default): only avatars with the component.
- **All Avatars**: every DynamicCharacterAvatar gets the component when it builds, with the settings' **Wrinkle Overlay**
  and **Wrinkle Profile**. The added component is never saved into scenes or prefabs.
- **Off**: no avatar gets wrinkle maps, not even one with the component.

**Welcome to UMA > Configure UMA** sets this too: **Enable wrinkle maps** (with Maximum Fidelity) chooses All Avatars;
**Remove wrinkle maps** (with Maximum Speed) chooses Off.

### With your own skin

Select your skin's UMA Material and choose **Assets > UMA > Skin > Create Expression Wrinkles Copy**. Set the copy as the
component's **Wrinkle Material**.

### Cost

Two 2048 × 2048 uncompressed RGBA atlases with mipmaps on the head, about 43 MB of GPU memory per character that has
wrinkles. A face at rest skips the wrinkle work in the shader.

---

## Looking at the wrinkles

The wrinkle material's **Expression Wrinkles** section:

| Control | What it does |
|---|---|
| **Wrinkle Depth** | Scales every wrinkle's slope (default 1; the UMA 3 maps were tuned at 1.5 for dramatic lines). |
| **Crease Darkening** | How much the crease depth (blue) darkens light inside creases. |
| **Crease Flush** / **Flush Color** | How much and how the flush (alpha) reddens compressed skin. |
| **Stretch Smoothing** | How much a negative weight smooths the skin's own normal map and pores. |
| **Wrinkle Zone Weights** | One slider per zone, −1 to 1. On a character, the component overrides these every frame. |
| **Wrinkle View** | A debug view: **Lit** (normal), **Zones** (each zone in its own colour), **Amount** (how strongly each texel wrinkles right now), **Wrinkle Normal** (the wrinkle slope alone) and **Crease Depth**. |

To inspect the maps without a character: put the wrinkle material on a head, set a few zone weights to 1, and switch
Wrinkle View to **Zones** to see where each zone is.

---

## Menu reference

| Menu | What it does |
|---|---|
| **UMA > Skin > Bake Procedural Wrinkle Maps (UMA 3 Head)** | Regenerates `UMA3_Wrinkles.1001.png`, `UMA3_WrinkleZones.1001.png` and `UMA3_Wrinkles.1001_height.png` from the built-in landmarks (about 30 s). **Replaces hand edits** in those files: it asks first. |
| **UMA > Skin > Add Expression Wrinkles to Selected Avatar** | Adds the UMA Expression Wrinkles component to the selected avatar (see above). |
| **UMA > Skin > Export Wrinkle Map Sources (UMA 3 Head)** | Writes editable images of the procedural bake to a folder you choose: height, crease, flush and a zone guide. |
| **UMA > Skin > Import Wrinkle Map Sources (UMA 3 Head)** | Rebuilds `UMA3_Wrinkles.1001.png` (and its height) from those images. The zone map is not changed. |
| **UMA > Skin > Auto-Import Wrinkle Map Sources (UMA 3 Head)** | A toggle (ticked when on). While on, saving one of the source images rebuilds the wrinkle map automatically, with no dialog. |
| **Assets > UMA > Skin > Create Expression Wrinkles Copy** | On a skin UMA Material: makes a copy on the wrinkle shader with the two wrinkle channels. |
| **Assets > Create > UMA > Rendering > Expression Wrinkle Profile** | A new, empty expression profile. Copy `UMA 30 Wrinkle Profile` instead to start from the UMA 3 rules. |

---

## Editing the wrinkle map

Choose the workflow that suits the change:

| You want to | Use |
|---|---|
| Paint on the 3D head, see the result live in the real shader, use layers, masks, symmetry and brushes | [Overlay Painter](#editing-in-overlay-painter) |
| Work in Photoshop, Krita, GIMP or another image editor | [An image editor](#editing-in-an-image-editor) |
| Go back to the procedural lines | **Bake Procedural Wrinkle Maps** (replaces your edits) |

Both editing workflows change only the **wrinkle map** (relief, crease and flush). Zones are generated with the maps and
are not edited by hand; paint new lines inside the existing zones.

---

## Editing in an image editor

### 1. Export the sources

Choose **UMA > Skin > Export Wrinkle Map Sources (UMA 3 Head)** and pick a folder. The default is `WrinkleSources` in the
project folder, *outside* `Assets`, so the images are not imported or shipped. It takes about 30 seconds, then the
folder opens.

| File | Contents | Format |
|---|---|---|
| `UMA3_Wrinkles.1001_height.png` | Wrinkle **height**. Mid gray is flat, darker is a crease, lighter is raised skin. Black is −2.5 mm, white +2.5 mm. The UMA 3 lines use about −1.2 to +2.1 mm. | 16-bit grayscale |
| `UMA3_Wrinkles.1001_crease.png` | **Crease depth** (the map's blue). Black is none. | 16-bit grayscale |
| `UMA3_Wrinkles.1001_flush.png` | **Flush** (the map's alpha). Black is none. | 16-bit grayscale |
| `UMA3_Wrinkles.1001_guide.png` | A **reference only**: each zone tinted in its own colour, shaded by the height so you can see the existing lines. Never imported. | 8-bit colour |

### 2. Edit

Open the height image, and the guide as a reference layer on top (set it to Multiply or lower its opacity, and hide it
before saving). Then:

- **Add a line:** paint a soft, slightly darker stroke inside a tinted zone. A real wrinkle is a narrow valley (about
  1 mm wide on the face, 6 to 8 texels at 2048) with gentle shoulders. A soft round brush, 4 to 8 px, at low opacity,
  built up in passes, gives the right profile.
- **Deepen a line:** paint over it with a darker value or use Burn.
- **Remove a line:** paint over it with the surrounding gray (clone or heal works well).
- **Crease and flush:** paint the crease image where you dug valleys (a narrow light stroke along the bottom of each
  line), and the flush image where squeezed skin should redden. Keep both subtle.

Rules that keep the import correct:

- **Keep 16 bits per channel** for the height. At 8 bits the slopes come out in visible steps; the import warns you.
- Keep the **size** (2048 × 2048) and the file names. Gray or RGB images are both fine (the first channel is read).
- Do not use **alpha** in these images, and do not save interlaced PNGs.
- Stay inside the black-to-white range. Pure black or white means the height was cut off; the import reports how many
  texels touch the limits.
- Very steep edges (a hard 1-pixel step from black to white) are clipped to the steepest slope the map can store.
  Soft edges look better anyway.

### 3. Import

Choose **UMA > Skin > Import Wrinkle Map Sources (UMA 3 Head)** and pick the folder. `UMA3_Wrinkles.1001.png` and its
height are rebuilt (a few seconds). The Console reports the size and anything clipped.

### Automatic import

Turn on **UMA > Skin > Auto-Import Wrinkle Map Sources (UMA 3 Head)**. While it is ticked, Unity watches the folder you
last exported to or imported from. Each time you save the height, crease or flush image there, the wrinkle map is
rebuilt as soon as Unity has focus, with no dialog. Keep Unity and the image editor side by side, save, and watch the
avatar update.

The toggle remembers its state. Exporting fresh sources resets the watcher, so the new files are not imported until you
change them.

### Notes

- Rebaking the procedural maps replaces an imported map. Keep your source images and import them again afterwards.
- Lines finer than one texel come out a little softer than in the procedural bake, because the slope is taken from
  neighbouring texels. Importing the unedited sources gives a mean difference of about 1/255 from the bake.

---

## Editing in Overlay Painter

Overlay Painter edits the wrinkle map directly on the 3D head with the real wrinkle shader. It adds three channels to the
wrinkle material: **Wrinkle Height**, **Wrinkle Crease** and **Wrinkle Flush**, with the same meaning and scale as the
images above, and its **Wrinkle Maps Only** export writes a new wrinkle overlay. Edits are layers, so they can be
masked, mirrored, toggled and revised; untouched parts of the map stay exactly as they were.

See [Painting Expression Wrinkle Maps in Overlay Painter](../OverlayPainter/Documentation/ExpressionWrinkleMaps.md) for
the step-by-step workflow.

---

## Using an edited map

| Where the edit went | How avatars get it |
|---|---|
| The shipped `UMA3_Wrinkles.1001.png` (image-editor import, or Overlay Painter with **Overwrite Source Overlay**) | Nothing to do: every avatar using `UMA30_ExpressionWrinkles_UDIM1001` gets it on its next build. |
| A new overlay (Overlay Painter's normal export) | Set it as the **Wrinkle Overlay** on an avatar's UMA Expression Wrinkles component, or in **Project Settings > UMA > Expression Wrinkles** for All Avatars. Overlay Painter's export window can do either for you. |

Rebuild the avatar (or enter Play mode) to see the change.

---

## Height and value reference

| Value | Meaning |
|---|---|
| Height gray 0.5 | Flat (no change from the skin). |
| Height gray 0.3 | 1 mm below flat: a deep forehead line. |
| Height gray 0.45 | 0.25 mm below flat: a fine line. |
| Height black / white | −2.5 mm / +2.5 mm (the limits). |
| Crease 0.5 | Half the darkening of the deepest crease. The UMA 3 lines reach 1 at their deepest. |
| Wrinkle map red, green 0.5 | Flat slope. |
| Zone weight 1 / −1 | Full wrinkles / full stretch. |

One texel of the UMA 3 head texture is about 0.16 mm on the face at 2048 × 2048.

---

## Troubleshooting

**A new line does not show.** It is outside every zone, or its zone's weight is 0. Check with Wrinkle View **Zones**, and
raise that zone's weight on the material or make the matching expression.

**A line shows only on one side of the forehead.** Each forehead half follows its own brow. Raising one brow also gives
the other half a little (0.2) so lines fade across the middle; raise both brows to see a line at full strength across.

**The import says the height image has 8 bits.** Save it as 16 bits per channel (Photoshop: Image > Mode > 16 Bits/Channel
before saving; GIMP: Image > Precision > 16-bit; Krita: 16-bit integer).

**"texels are steeper than the wrinkle map can store".** Some edges go from dark to light in one or two pixels. Soften
them.

**Auto-import does nothing.** Check the menu is ticked, the files keep their exported names, and you saved into the
folder you last exported to or imported from (the Console names it when you turn the toggle on).

**Bake Procedural Wrinkle Maps lost my edits.** It always replaces the files. Import your sources again, or re-export
from your Overlay Painter document.

**Wrinkles look blocky or zones flicker at a distance.** The wrinkle channels must stay uncompressed in the atlas: check
**Never Compress** on the two wrinkle channels of the head's UMA Material, and that the zone map texture is imported
uncompressed.

---

## Technical notes

- **Channel meanings.** UMA Material layouts know the wrinkle textures: `_WrinkleMap` is red/green **Wrinkle Slope**,
  blue **Wrinkle Crease**, alpha **Wrinkle Flush**; `_WrinkleControlMap` is **Wrinkle Zone** in all four. The automatic
  output settings import the zone map uncompressed and the wrinkle map high-quality compressed, both linear.
- **Height to slope.** For each texel, the height change across one texel to the right and one texel down is turned into
  a slope along the texel's own tangent and bitangent, solved from the slot mesh's surface derivatives
  (`UMAWrinkleSourceMaps.BuildSlopeFrame`). Mirrored UV islands therefore come out right. Padding texels outside the UV
  islands copy their nearest island texel, as in the bake.
- **Edits are added, not re-derived.** Overlay Painter and `UMAWrinkleSourceMaps.AddSlopes` add the slope of the *change*
  in height to the existing map, so texels the artist did not touch keep their exact values. Overlay Painter does this on
  the GPU (`ChannelPack.compute`, `CSApplyWrinkleHeight`) and matches the CPU reference to within 3/255.
- **Exact sources.** Overlay Painter reads wrinkle textures losslessly from their PNG files rather than from the
  block-compressed import, so an untouched session exports the map byte for byte.
- **Code.** `UMAWrinkleMapBaker` (bake, `ExportSources`, `ImportSources`, `HeightSourcePath`, `WriteHeightSource`),
  `UMAWrinkleSourceMaps` (height/slope maths), `UMAWrinkleSourceWatcher` (auto-import), `UMAPngReader` (16-bit PNG
  reading; Unity's `LoadImage` reduces PNGs to 8 bits).
- **Tests.** `UMAWrinkleSourceMapTests` and `UMAWrinkleMapTests` (core), `TexturePaintWrinkleMapTests` (Overlay Painter
  Tests companion).

See also: [Realistic Skin README](../SRP/Shaders/Skin/README.md#expression-wrinkles) (shader details),
[UMA Materials](UMAMaterial.md), [Overlay Painter](../OverlayPainter/Documentation/OverlayPainter.md).
