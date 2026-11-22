# Painting Expression Wrinkle Maps in Overlay Painter

Overlay Painter can paint the expression wrinkles of the UMA 3 head (and any head that uses the wrinkle skin shader)
directly in 3D, with the real wrinkle shader in the preview. You add, deepen, soften or remove lines on layers, then
export a new wrinkle overlay that avatars use straight away.

New to wrinkle maps? Read [Expression Wrinkle Maps: An Artist's Guide](../../Docs/ExpressionWrinkleMaps.md) first. It
explains zones, zone weights, the files and the menus. This page covers only the Overlay Painter workflow.

## Contents

- [What you need](#what-you-need)
- [The three wrinkle channels](#the-three-wrinkle-channels)
- [1. Open the head](#1-open-the-head)
- [2. Make the wrinkles visible](#2-make-the-wrinkles-visible)
- [3. Paint](#3-paint)
- [4. Check your work](#4-check-your-work)
- [5. Export](#5-export)
- [6. Use the result](#6-use-the-result)
- [Keep editing later](#keep-editing-later)
- [Techniques](#techniques)
- [Troubleshooting](#troubleshooting)
- [How it works](#how-it-works)

---

## What you need

- URP, compute shader support (any desktop GPU).
- The UMA 3 head slot `UMA30_Body_UDIM1001` (`Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM1001_slot.asset`).
- The wrinkle overlay `UMA30_ExpressionWrinkles_UDIM1001` (`Assets/UMA/UMA3/Overlays`), or an overlay you exported from
  it before. If the wrinkle assets are missing, choose **UMA > Skin > Add Expression Wrinkles to Selected Avatar** once on
  any UMA 3 avatar; it creates them.

---

## The three wrinkle channels

When a surface uses the wrinkle skin material, the **Paint / Preview Channel** list gains three channels:

| Channel | What you paint | Values |
|---|---|---|
| **Wrinkle Height** | The relief of the wrinkles, as a height picture. | Mid gray (0.5) is flat. Darker digs a crease, lighter raises skin. Black is −2.5 mm, white +2.5 mm. A deep forehead line is about 0.3 (−1 mm); a fine line about 0.45. |
| **Wrinkle Crease** | How much light darkens inside each crease (the wrinkle map's blue). | 0 none, 1 the deepest crease. |
| **Wrinkle Flush** | Where squeezed skin turns slightly redder (the wrinkle map's alpha). | 0 none. Keep it subtle. |

What happens to each:

- **Wrinkle Height** is Overlay Painter's own data, like Normal Control. It is turned into the red and green *slope* of
  the wrinkle map, texel by texel, along the head's real tangent frames. You never paint the slope directly.
- The session starts with the height the map was made from: the `_height` texture kept next to the wrinkle map. So the
  existing lines are visible in the Wrinkle Height channel and can be traced, deepened or painted out.
- Only your **changes** to the height are added to the map. Everything you do not touch stays exactly as it was, down to
  the last bit.
- **Wrinkle Crease** and **Wrinkle Flush** are ordinary grayscale channels taken from the existing map.
- The **zone map is never painted**. A line only shows inside a zone (see [Techniques](#paint-inside-the-zones)).

Other tiles of the UMA 3 body (1002 to 1005) have no wrinkle map, so they get no Wrinkle Height. A note under the
channel picker says so if you select a wrinkle channel there.

---

## 1. Open the head

There are two ways. Both edit the same wrinkle map.

### A. From the head slot (recommended)

The head opens with the wrinkle textures only and a plain white skin, which shows relief clearly, like a clay render.

1. In the Project window, select `UMA30_Body_UDIM1001_slot`.
2. In the Inspector, under **Editors > Plugins**, click **Overlay Painter**.
3. Set **Source** to **Overlay Data Asset**.
4. Drag `UMA30_ExpressionWrinkles_UDIM1001` (or your own exported wrinkle overlay) into **Selected Tile Overlay**.
5. The head is a UDIM tile, so the window lists the body's other tiles under **UDIM Member Sources**. Leave them empty
   ("Neutral base"); they have no wrinkles.
6. Keep **Working Resolution** at **2048** (the wrinkle map's size).
7. Click **Open**.

### B. From an avatar with wrinkles

The head opens with its real skin, so you can judge the wrinkles on the finished face.

1. Give a UMA 3 avatar in the scene wrinkles: **UMA > Skin > Add Expression Wrinkles to Selected Avatar** (or use
   **All Avatars** in Project Settings).
2. Select the avatar. In the Inspector, under **Editors > Plugins**, click **Overlay Painter**.
3. Choose a pose (**Build in Default Pose** is easiest for the face).
4. In the Target region, select the body (`UMA30_Body`, 5 UDIM tiles).

The wrinkle map is read from the wrinkle overlay behind the avatar, not from the atlas.

On an avatar, only the **head tile (1001)** uses the wrinkle material; the other body tiles keep the ordinary skin. The
layer panel follows the *active tile*, so while a torso tile is active the **New Channel** list has no wrinkle channels.
**Layer Channels & Settings** then shows a note with an **Edit Wrinkles on Tile 1001** button: click it. You can also
expand **Paint Target** and click the 1001 row, or simply paint a stroke on the face. Wrinkle channels are added only to
the head tile's part of a layer; the torso tiles are left as they are. A brush stroke on a wrinkle channel likewise
paints the head tile only: where it passes over the torso it does nothing.

---

## 2. Make the wrinkles visible

The wrinkle shader shows nothing on a face at rest, so Overlay Painter sets every **zone weight to 1** in its preview
when the session opens. The face looks as if every expression were made at once. This changes only the preview, never
the exported material.

To change what you see, open the preview material: **View > Preview Material** (or **Window > UMA > Overlay Painter
Material**). In its **Expression Wrinkles** section:

- **Wrinkle Zone Weights**: set a zone to 0 to hide it, or to −1 to see the stretched skin. Showing only the zone you are
  working on avoids confusion.
- **Wrinkle Depth**: exaggerate (1.5 to 2) while painting fine lines, then set it back.
- **Wrinkle View**:
  - **Zones** colours each zone, so you can see where a line will show.
  - **Wrinkle Normal** shows the relief without the skin.
  - **Crease Depth** shows the blue channel.
  - Return to **Lit** to judge the look.

**Reset to Source** restores the material's own values (zone weights back to 0).

In the 2D view, select **Wrinkle Height** to see the height picture. **Solo in 3D** shows the selected channel on the
model without shading.

---

## 3. Paint

1. Click **+ Paint** in the layer stack and name the layer for what it does, for example `Forehead extra line`.
2. In **Layer Channels & Settings**, set **New Channel** to **Wrinkle Height** and click **Add Channel**. Add
   **Wrinkle Crease** too if the new line should darken.
3. On a Paint or Path layer, a new Wrinkle Height channel uses the **Overlay** blend: painting adds to the wrinkles
   below it, and mid gray changes nothing. Keep it unless you want the layer to replace the height underneath (Normal).
   Plugin (filter) and Fill layers start with **Normal**, because they write a complete height.
4. Set **Paint / Preview Channel** to **Wrinkle Height**.
5. Under the channel's **Source**, choose **Color** and set **Source Value**. Use 0.35 to 0.45 to dig, 0.55 to 0.65 to
   raise. With Overlay, darker than 0.5 always deepens and lighter always raises.
6. Choose the **Paint** tool and a soft round brush. A good start for the UMA 3 head at 2048:
   - **Size** 6 to 12 px, **Hardness** 0, **Flow** 20 to 40 %.
   - **Spacing** small, **Stabilization** on for long lines.
7. Paint along the skin in the Scene view, or in the UV canvas.
8. Switch **Paint / Preview Channel** to **Wrinkle Crease**. Paint a thin, light stroke along the bottom of the new valley.
   Use **Source Value** 0.3 to 0.6, about half the width of the height stroke.
9. Optionally paint **Wrinkle Flush** where the skin bunches (0.2 to 0.4).

Everything else in Overlay Painter works on these channels: layer opacity, masks, **Erase**, **Blur** (softens a line),
**Smear**, **Clone**, **Dodge** / **Burn** (raise / deepen the height), Fill and Path layers, and texture or sprite
sources (for example a hand-drawn wrinkle stamp).

---

## 4. Check your work

- Toggle the layer off and on to see your change alone.
- Set the zone weight of the area you painted to 0 and back to 1 in the Preview Material. A wrinkle must disappear at 0;
  if it does not, it was painted in the skin, not in a wrinkle channel.
- Switch **Wrinkle View** to **Zones** and make sure every new line lies inside the right zone.
- Look from several angles with a low light. Wrinkles read best at a grazing angle.
- Save the paint document (**File > Save**) so you can come back to the layers later. Exporting does not save it.

---

## 5. Export

1. Choose **File > Export...** (or **Export** on the toolbar).
2. Set **Export Mode** to **Wrinkle Maps Only**. A session opened from a wrinkle overlay starts in this mode.
3. Enter an **Export Identifier**, for example `ExtraForeheadLine`. It is added to every file name.
4. Check **Output Folder**. The default is `Assets/UMAProjectData/OverlayPainter/Generated`.
5. Optional, so avatars use the result at once:
   - **Use on <avatar name>** (when you opened from an avatar): sets the avatar's UMA Expression Wrinkles **Wrinkle
     Overlay** to the new overlay, adding the component if needed. It supports Undo. Rebuild the avatar to see it.
   - **Use for All Avatars (UMA Settings)**: makes the new overlay the project's wrinkle overlay, used when Expression
     Wrinkles is **All Avatars**. It is not saved if UMA Settings already has unsaved changes; the status line says so.
6. Read **Resolved Outputs**: two textures (the wrinkle map and the zone map) and one overlay. Tiles without wrinkles are
   listed as warnings ("has no wrinkle map ... so it is not exported").
7. Click **Export**.

Export creates, in the output folder:

| File | What it is |
|---|---|
| `UMA3_Wrinkles.1001_<identifier>_1001.png` | The new wrinkle map: your Wrinkle Height as red/green slope, Wrinkle Crease in blue, Wrinkle Flush in alpha. Linear, high-quality compressed. |
| `UMA3_Wrinkles.1001_<identifier>_1001_height.png` | The editable height of the new map (16-bit, linear, uncompressed). Editor only. It lets the next session start from your edits. |
| `UMA3_WrinkleZones.1001_<identifier>_1001.png` | The zone map, copied exactly. Linear, uncompressed. |
| `UMA30_Body_<identifier>_1001_Overlay.asset` | The new wrinkle overlay: the two maps in the wrinkle channels and **nothing** in the skin channels, so it changes only the wrinkles. It is added to the Global Library. |

The export is a transaction: if anything fails or you cancel, every file and library entry is rolled back.

### Replacing the shipped wrinkle map instead

Under **Advanced**, **Overwrite Source Overlay** writes into the source overlay's own textures. Opened from
`UMA30_ExpressionWrinkles_UDIM1001`, that replaces `UMA3_Wrinkles.1001.png` and its height file, and every avatar gets the
change without changing any setting. A second confirmation lists every file that will be replaced. Prefer a new export
while you experiment. Note that **UMA > Skin > Bake Procedural Wrinkle Maps** overwrites those files again.

### Other export modes

- **Flattened Composite** writes every channel of the material, skin included, which is rarely what you want for
  wrinkles. A warning in the plan points to Wrinkle Maps Only.
- **Runtime Overlay (Transparent)** leaves the wrinkle textures out.

---

## 6. Use the result

If you ticked one of the **Use ...** options, the avatar or the project already uses the new overlay. Otherwise:

- **One avatar:** select it and drag the exported overlay into **Wrinkle Overlay** on its UMA Expression Wrinkles
  component.
- **All avatars:** **Project Settings > UMA > Expression Wrinkles**: set **Wrinkle Overlay** to the exported overlay, and
  the mode to **All Avatars**.

Rebuild the avatar (or enter Play mode) and make the expressions: raise the brows for forehead lines, frown for the
glabella, smile for crow's feet and folds, pucker for lip lines.

---

## Keep editing later

- Reopen the saved paint document (double-click it) to continue with your layers.
- Or start a new session from the **exported overlay** (step 1A with your overlay in **Selected Tile Overlay**). Its
  `_height` file is found automatically, so Wrinkle Height starts from your last export.

---

## Techniques

### Paint inside the zones

A line shows only where its zone's mask is. The zones fade at their edges, so a line there fades too. Use
**Wrinkle View > Zones** while painting, or the guide image from **UMA > Skin > Export Wrinkle Map Sources** as a
Reference layer. To show wrinkles where no zone exists, the zones themselves must be changed, which is not part of
Overlay Painter. The forehead zones cover most of the forehead; the two halves meet at the middle.

### A convincing wrinkle profile

A skin fold is a narrow valley between two soft, slightly raised rolls:

1. Paint the valley with a dark value (0.35 to 0.42) and a small soft brush.
2. Optionally, paint the rolls beside it with a light value (0.53 to 0.56) and a brush about three times wider, at low
   flow.
3. Vary the depth along the line (lighter pressure or a second pass), let it taper at both ends, and break long lines
   once or twice. Perfectly even lines look drawn.
4. Add Wrinkle Crease only along the deepest part.

### Smooth existing wrinkles without touching other layers

The Blur tool works on the active layer's own pixels, so on a new, empty layer it has nothing to blur. To soften the
wrinkles that are already there (the base, or layers below) non-destructively, use a **filter layer**: it reads
everything below it and writes the smoothed result into its own layer.

1. Make the head tile active (see [step 1](#b-from-an-avatar-with-wrinkles)).
2. Choose **Add Layer > Plugin** and place the new layer at the top of the stack, or directly above what it should smooth.
3. In the layer properties, select **Blur, Sharpen & Detail**.
4. Set **Source Channel** and **Output Channel** both to **Wrinkle Height**, **Operation** to **Gaussian Blur**, and
   **Radius** to 2 to 4 px. A larger radius widens and flattens the lines more.
5. Click **Generate**.
6. To smooth only some lines, add a mask: in the layer row's **⋮** menu, choose **Mask > Add Black Mask**. Then paint
   white over the edges you want softened.
7. Use the layer's **Opacity** as the strength, and toggle the layer to compare.
8. If you change the layers below it, click **Regenerate**.

The layer only replaces the height where it is visible, so the other layers, the base and the rest of the map stay as
they are. Smoothing the height also softens the matching crease darkening only if you run the same filter on
**Wrinkle Crease**: add a second filter layer for it.

To smooth by hand with the brush instead, work on a copy:

1. Click **+ Paint** and put the new layer above the layers that hold the wrinkles.
2. Right-click the layer and choose **Copy from Below > Wrinkle Height**. The layer now holds the whole relief, and
   nothing looks different yet.
3. Pick the **Blur** tool and brush over the edges to soften. Turn **Cap Per Stroke** off so a stroke can build up.
4. Hide the layer to compare with the original, or lower its opacity to blend between the two.

The layers below and the base are not changed. Repeat with **Wrinkle Crease** if the crease darkening should soften
too. A copied Wrinkle Height channel uses the **Normal** blend, so on this layer mid gray means flat skin, not
"no change": use Blur, Smear, Clone, Dodge and Burn on it, and paint new lines on a separate Paint layer above.

### Start a layer from the existing overlay or wrinkles

To edit what is already there on a layer of its own, rather than painting on top of it:

- **Crease and flush:** choose **Add Layer > Fill > From Overlay...** and pick the wrinkle overlay
  (`UMA30_ExpressionWrinkles_UDIM1001`, or your exported one). The Fill layer gets Wrinkle Crease and Wrinkle Flush
  from the overlay. The zone map is never offered: it is not paint. On the UDIM body the other tiles have no wrinkle
  overlay, so they get **No contribution** automatically. For the skin, picking `UMA30_Body_UDIM1001` fills every tile
  with its own `UMA30_Body_UDIM100x` overlay.
- **The relief (Wrinkle Height):** the overlay holds slopes, not height, so take the height from the `_height` texture
  next to the wrinkle map:
  1. Make the head tile active (see [step 1](#b-from-an-avatar-with-wrinkles)).
  2. Click **+ Paint** to add an empty Paint layer.
  3. Right-click it and choose **Copy from Below > Wrinkle Height**. The layer now holds the whole relief that is in
     effect under it (the session's base height plus any layers below) as editable pixels. The wrinkle map does not
     change, because the height did not change.
  4. Edit that layer with **Blur** (softens edges), **Dodge** and **Burn** (raise and deepen), **Smear** and **Clone**
     (move skin around). Toggling the layer compares with the original; its opacity blends between the two.

  To start from a height file instead (another head's, or an older export), add **Fill > Empty Fill Layer**, add
  **Wrinkle Height** to it, set its **Source > Type** to **Texture**, assign the `_height` file, and then click
  **Convert to Paint Layer** in the Fill layer's properties.

Keep this copy layer at the bottom of your wrinkle layers. A layer under it is hidden by it, because the copy replaces
the height with the Normal blend.

### Remove a procedural line

Paint over it with **Source Value** 0.5 on a layer set to the **Normal** blend, or use **Clone** from nearby smooth skin.
Because only changes are applied, the line disappears from the slope. Also paint its crease to 0 in **Wrinkle Crease**.

### Symmetry

Faces are rarely perfectly symmetrical. For roughly paired lines, turn on the layer's **Mirror X**
(**Properties > Symmetry**), then break the symmetry with a second, unmirrored layer of small variations.

### Wrinkle Height versus Normal and Normal Control

Wrinkle Height changes the *expression* wrinkles, which come and go with the face. **Normal** and **Normal Control**
change the skin's permanent normal map, which is always visible. Use Normal Control for scars, pores and permanent
age lines, and Wrinkle Height for lines that should appear only when the face moves.

---

## Troubleshooting

**The New Channel list has no wrinkle channels (opened from an avatar).** A torso tile is active. Click **Edit Wrinkles
on Tile 1001** under Layer Channels & Settings, or select the 1001 row under Paint Target.

**Blurring on my new layer does nothing.** The Blur tool only blurs the active layer's own pixels. Use the filter layer in
[Smooth existing wrinkles](#smooth-existing-wrinkles-without-touching-other-layers).

**There is no Wrinkle Height channel.**
- The surface does not use the wrinkle material: open with the wrinkle overlay (step 1A) or an avatar with wrinkles (1B).
- You are on a tile without a wrinkle map (1002 to 1005): select the head.
- Select a wrinkle channel to see the reason under the channel picker.

**I paint, but nothing changes on the head.** Check the active channel is **Wrinkle Height**, the layer has a Wrinkle
Height channel, the zone's weight is above 0 in the Preview Material, and the stroke is inside a zone.

**Blur, Smear or Clone does nothing on my new layer.** These tools change the pixels the layer already has, and a new
Paint layer has none. Right-click the layer and choose **Copy from Below > Wrinkle Height** first (the Brush panel
offers the same button), then brush. See
[Smooth existing wrinkles](#smooth-existing-wrinkles-without-touching-other-layers).

**Painting makes a wide flat dent rather than a line.** The brush is too large or too hard, or the layer channel uses the
**Normal** blend with a dark value. Use a smaller soft brush, or the Overlay blend.

**The exported overlay changes the skin colour.** It was exported as a **Flattened Composite**. Export again with
**Wrinkle Maps Only**.

**Nothing changes on the avatar after export.** The avatar still uses the old overlay: tick **Use on <avatar>** when
exporting, or set **Wrinkle Overlay** on its component, then rebuild it.

**Wrinkle Height started flat although the map has lines.** The map has no `_height` file next to it. This happens with a
map copied without its height, or one made by another tool. The existing lines are still in the map and still shown.
Your new strokes are added to them, but you cannot trace or erase the old lines in the height. To get a height, run
**Bake Procedural Wrinkle Maps** for the UMA 3 maps, or re-export.

**"Wrinkle Height needs compute shaders."** The graphics device does not support compute shaders (for example when Unity
runs in batch mode with no graphics). Use a desktop GPU.

---

## How it works

- The wrinkle UMA Material's layout tells Overlay Painter what each component means. `_WrinkleMap` has red/green
  **Wrinkle Slope**, blue **Wrinkle Crease** and alpha **Wrinkle Flush**. `_WrinkleControlMap` is **Wrinkle Zone**
  data, kept exactly and never shown as a paint channel.
- For the wrinkle map, Overlay Painter builds the slot mesh's per-texel tangent frame with UMA's wrinkle baker. On every
  preview update, a compute kernel adds the slope of the Wrinkle Height *change* to the source map's red and green.
  That is the same maths the baker and the image-editor import use, matching to within 3/255.
- Wrinkle textures are read losslessly from their PNG files, not from the block-compressed import, so untouched texels
  export unchanged. The zone map is copied byte for byte.
- The Wrinkle Height composite is exported as a 16-bit `_height` texture beside the new wrinkle map, inside the same
  export transaction.
- Tests: `TexturePaintWrinkleMapTests` (Overlay Painter Tests companion) opens the UMA 3 head, paints a crease, exports
  Wrinkle Maps Only, and checks every output against the source and the CPU reference.
