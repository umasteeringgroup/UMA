# Hair Animation: An Artist's Guide

UMA hair can sway on its own: the tips trail behind when the character walks, swing when it turns or nods, settle when it stops, and lean and flutter in the wind. This guide shows how to set a hair slot up for it, how to give it the vertex colors it needs when it has none, and which settings suit which hair.

The sway is light by design. It bends the hair in the shader, so it costs almost nothing, adds no bones and needs no physics. It does not collide with the body and does not simulate individual strands. For long, heavy motion such as a swinging braid or a cape, use [Bone Animators](BoneAnimators.md) on bones in the slot instead.

## How It Works, in One Minute

Every vertex of the hair needs to know where it sits along its strand: **0 at the root**, where the card grows from the scalp, and **1 at the tip**. That is stored in the slot's **vertex colors**, black at the root and white at the tip, or in the hair texture's **ID Map**.

A **Hair Control** component on the character watches the head. When the head moves, turns, or the wind blows, it works out how much the hair should swing, and the hair shader bends each strand by that amount. The root stays put and the bend grows towards the tip, so strands curve the way real hair does, and they keep their length.

## What You Need

- A **URP** project. The sway is part of the URP hair shader, `UMA3_HairShader_URP`, used by the `UMA3Hair` UMA material. Hair on other shaders does not sway.
- A hair **slot** with normals, tangents, and root-to-tip information: vertex colors, or an ID Map.
- **Hair Control Ready** ticked on that slot.
- The **Hair Control** component on the character.

The UMA 3 hair slots in `UMA/UMA3/Wearables/HairAndBeards/Hair*` are already set up. For those, only the last step is needed.

## Quick Start

1. Select the character (the object with the **DynamicCharacterAvatar**).
2. **Add Component > UMA > Hair Control**.
3. Make sure the hair slot it wears has **Hair Control Ready** ticked. Select the slot asset and look under **Hair Control** in its Inspector.
4. Press Play and move or turn the character. The hair swings.

The sway only runs in Play mode. In Edit mode the hair stays at rest.

## Checking a Slot's Vertex Colors

Before marking a new hair slot ready, check that its vertex colors run from root to tip:

1. Select the character and, in the **Editing tools** section of its Inspector, click **Open Vertex Editor (Sculpt, Paint, Mesh Modifiers)**.
2. In the **Slots** panel, press **S** next to the hair slot. This makes it the only slot you edit, and frames it in the Scene view.
3. Tick **Vertex Colors** under **Original Materials**. The model is now drawn with its vertex colors only, from both sides.

Good hair looks black at the scalp and fades smoothly to white at the tips, on every card. If the hair is one flat color, all black, or patchy, it needs new colors.

## Making Root-to-Tip Colors

The Vertex Editor can make the colors for you. It finds where each card meets the head and walks along the card to its far end.

1. Open the Vertex Editor on the character, and press **S** next to the hair slot in the **Slots** panel, as above.
2. Click **Vertex Paint** at the top of the tools panel.
3. Open the **Hair Root to Tip** section.
4. Under **Head Slots**, tick the slots the hair grows from. **Find Head Slots** picks them for you: slots named head, face or scalp, or else the slots the hair lies on. On a UMA 3 character the head is part of the body, as two tiles, `UMA30_Body_UDIM1003` (the cranium) and `UMA30_Body_UDIM1001` (the face). Tick both.
5. Leave **Unclear Cards** on **Texture Top** unless you know your hair texture is drawn differently (see below).
6. Click **Recalculate Vertex Colors from Head**.

The box below the button says how many cards it colored and how:

- **grow from where they go into the head**: the card's root end dips into the head. These are almost always right.
- **from their end nearer the head**: neither end goes into the head, but one is clearly nearer. These are almost always right too.
- **by Texture Top (check these)**: both ends are about as near the head, as with hair lying flat along the skull. The root is taken to be at the top of the hair texture, as hair textures are drawn. Look these over.

On the UMA 3 hair, between 0 and 6 percent of the cards come out the wrong way round. Long hair is usually all right; hair whose tips tuck in against the jaw or neck is the most likely to need a few fixes.

### Unclear Cards

This decides the root of a card whose two ends are about as near the head:

| Choice | Root is | Use it when |
| --- | --- | --- |
| **Texture Top** (default) | the end at the top of the hair texture | almost always; hair textures are drawn with the root at the top |
| **Higher End** | the higher end | hanging hair whose texture is drawn upside down. Wrong for hair that sticks up |
| **Nearer Head** | the end nearer the head, however small the difference | hair textures with no fixed direction |

### Fixing Cards by Hand

A card that came out white at the head and black at the tip is the wrong way round. To fix it:

1. In the Vertex Editor, click **Advanced** at the top of the tools panel (Define Vertex Set).
2. Select the root vertices of the card, the ones at the scalp end, and in **Hair Root to Tip** press **Set Root**. Black dots mark them.
3. Select its tip vertices and press **Set Tip**. White dots mark them.
4. Press **Interpolate Colors from Root to Tip**. Only that card is recolored, and the box says "1 card colored".

You can mark the roots and tips of several cards at once and fix them together. Selecting one vertex at each end works; selecting the whole root edge and the whole tip edge gives the cleanest result.

### Touching Up with the Paint Brush

For small touch-ups, use **Vertex Paint**'s **Paint** brush with **Blend** set to **Replace**. Paint black where the hair must not move and white where it should move most; grey is in between. The **Smooth** brush evens out a patchy gradient.

### Saving the Colors

- **Save Colors to SlotDataAsset** (in **Hair Root to Tip**) writes the colors into the slot asset. Every recipe and character that uses the slot gets them. This is usually what you want for hair.
- **Save as MeshModifier** keeps them in a Mesh Modifier instead, without changing the slot.

Work under source control, so a saved slot can be restored if needed.

## Making Colors in Blender or Another 3D Package

If you build the hair yourself, you can paint the colors before you export:

- Add a vertex color layer (a Color Attribute in Blender).
- Paint each card **black at the root and white at the tip**, in a smooth gradient along the card. Only the red channel is read, so grey is fine.
- Export with vertex colors, and build the slot as usual. The colors come through in the slot.

## Using the ID Map Instead

If the hair texture has an ID Map whose channels run from root to tip, the slot can use that instead of vertex colors. Set **Hair Control Source** on the slot:

- **ID Map Root**: the red channel, which is white at the root. The UMA hair atlas is drawn this way.
- **ID Map Tip**: the green channel, which is white at the tip.

The ID Map is read with the slot's own texture coordinates, so this works when the hair texture is not atlased together with other textures.

## The Slot Settings

Select the hair slot asset; the settings are under **Hair Control** in its Inspector.

| Setting | What it does |
| --- | --- |
| **Hair Control Ready** | Lets this slot sway. Off by default. |
| **Hair Control Source** | Where the root-to-tip value comes from: **Vertex Color**, **ID Map Root** or **ID Map Tip**. |
| **Hair Control Start** | Where along the strand the movement starts, from 0 (the scalp) to 1 (the tip). Hair before this point does not move. |
| **Hair Control Angle** | How far the strand turns at full sway, in degrees. |
| **Hair Control Length** | The length of a strand in meters. Leave it at **0** to have it measured from the mesh, which is right for nearly all hair. |

### How Far the Tips Travel

The strand bends from the **Start** point, and only the part after it moves. At full sway, the tip travels sideways by roughly:

| Angle | Tip travel, as part of the moving length |
| --- | --- |
| 10° | about 1/11 |
| 20° | about 1/6 |
| 30° | about 1/4 |
| 45° | about 3/8 |
| 60° | about 1/2 |

Example: on 60 cm long hair with **Start** 0.5, the moving part is 30 cm. At 20° the tips travel about 5 cm; at 45° about 11 cm.

Full sway is reached only in strong wind or fast movement. A walk gives about a quarter to a third of it, a run most of it.

This is why short hair needs a **larger** angle than long hair: on a 5 cm spike with **Start** 0.5, only 2.5 cm moves, and even at 45° the tip travels under 1 cm.

### Good Starting Points

| Hair | Strand length (UMA 3 examples) | Start | Angle |
| --- | --- | --- | --- |
| Very short, spiky, crops | 2 to 7 cm (Hair_VeryShort, Hair_Pointy, Hair_LeftPart, Hair_MessyPomp, HairAnimeMessy) | 0.4 to 0.6 | 35° to 50° |
| Medium, buns, ponytails, pigtails | 15 to 30 cm (HairPonytail, Hair_Bun, HairPigtails, Hair_Poofy, Hair_Bob) | 0.2 to 0.4 | 20° to 35° |
| Long | 35 to 60 cm (Hair_Straight, Hair_CurveUnder, Hair_LongSwept, Hair_PulledBack) | 0.1 to 0.3 | 10° to 25° |

For your own hair, measure a typical card from root to tip in your 3D package to find its row.

The UMA 3 hair ships with Hair_Pointy's settings (**Start** 0.547, **Angle** 44.7°), tuned for short spikes. They look fine on most hair; lower the angle on the long styles if they swing more than you want.

## The Hair Control Component

The component sets how the hair reacts. Its defaults suit a typical character.

| Setting | Default | What it does |
| --- | --- | --- |
| **Head Bone Name** | `Head` | The bone the hair follows. |
| **Center Offset** | 9 cm up | The center of the hair volume, for the shader's Volume Shading. |
| **Sway** | on | Turns the sway on and off for this character. |
| **Movement Sway** | 0.2 | How much the hair trails when the head moves: sway per meter per second. |
| **Turn Sway** | 0.15 | How much the hair swings when the head turns: sway per radian per second. Half a circle a second gives about half sway. |
| **Wind Sway** | 0.5 | How much the hair leans per unit of wind. |
| **Wind** | none | Wind for this character only, as a direction and strength. 1 is a clear breeze. |
| **Use Wind Zones** | on | Adds the scene's Wind Zones (their **Main** value). |
| **Gustiness** | 0.5 | How much the wind rises, falls and veers. 0 is a steady wind that only leans the hair. |
| **Spring Frequency** | 1.5 | How fast the hair swings back, in swings per second. |
| **Damping** | 0.4 | How quickly a swing dies out. Low values keep swinging; 1 settles without swinging back. |
| **Ripple** | 0.3 | A wave that runs from scalp to tip while the hair moves. 0 moves each card in one piece. |
| **Ripple Frequency** | 1.5 | Waves per second. |

### Suggested Styles

These are starting points; adjust to taste.

| Style | Movement Sway | Turn Sway | Spring Frequency | Damping | Ripple |
| --- | --- | --- | --- | --- | --- |
| Subtle, realistic | 0.15 | 0.1 | 1.8 | 0.6 | 0.2 |
| Default | 0.2 | 0.15 | 1.5 | 0.4 | 0.3 |
| Bouncy, stylized | 0.3 | 0.25 | 1.2 | 0.25 | 0.4 |

For a windy scene, add a **Wind Zone** (**GameObject > 3D Object > Wind Zone**) with a **Main** of 1 to 2, or set **Wind** on the character, and raise **Gustiness** to about 0.7.

### Wind from a Script

`UMAHairControl.GlobalWind` is added to every Hair Control in the scene:

```csharp
// A breeze from the left, for every character.
UMA.UMAHairControl.GlobalWind = new Vector3(1.5f, 0f, 0f);
```

Wind Zones are looked up once a second. After adding or removing one from a script, call `UMA.UMAHairControl.RefreshWindZones()` to see the change at once.

## Things to Know

- **Slots that share a material are one piece.** UMA builds all slots with the same hair material into one part of the mesh, and the shader gets one set of settings for it. Every slot in that part must be **Hair Control Ready**; slots that share it use the first slot's settings.
- **Two-pass hair** moves as one; both passes get the same values.
- **Each character has its own sway**, even when characters share a generated material.
- **Cost:** one small calculation per character per frame, and a few values per hair material. Characters with Hair Control are drawn outside the SRP Batcher for their hair, because the values are set per renderer. For crowds, set **Disable Hair Control at LOD** on [UMASimpleLOD](UMASimpleLOD.md) so distant characters turn it off.
- **Teleporting** a character, or snapping it to a new rotation, does not throw the hair around.

## Troubleshooting

**The hair does not move at all.**
- Check that you are in Play mode.
- Check that the character has the **Hair Control** component, with **Sway** ticked.
- Check that the hair slot has **Hair Control Ready** ticked, and that **Hair Control Angle** is above 0.
- Check that the hair material uses `UMA3_HairShader_URP`.
- Look in the Console for one of the messages below.

**"the hair of slot '...' does not sway, because its strands could not be measured"**
The slot's vertex colors do not run from root to tip (they are one flat color, for example), or it has no normals or tangents. Make the colors as described in [Making Root-to-Tip Colors](#making-root-to-tip-colors).

**"the hair does not sway on material '...', because slot '...' is built into the same submesh and is not Hair Control Ready"**
Another slot shares the hair's material. Mark that slot ready too, with colors that keep it still where it must not move, or give it another UMA material.

**"the cards of slot '...' do not run the same way in their tangent frames"**
The hair's cards are laid out in different directions in the texture, so some of them will bend the wrong way. It still sways; check it, and rotate the odd cards' UVs in your 3D package if it shows.

**"no bone named 'Head' in the hair renderer's skeleton"**
The character's head bone has another name. Set **Head Bone Name** on the component.

**The scalp or the base of the hair moves.**
Raise **Hair Control Start**, or check the vertex colors: the roots must be black.

**Some cards swing the wrong way, or swing from the tip.**
Those cards are the wrong way round. Fix them by hand; see [Fixing Cards by Hand](#fixing-cards-by-hand).

**The hair swings too much, or too little.**
Change **Hair Control Angle** on the slot for this hair, or **Movement Sway**, **Turn Sway** and **Wind Sway** on the component for this character. See [How Far the Tips Travel](#how-far-the-tips-travel).

**The hair keeps swinging after the character stops.**
Raise **Damping**.

## See Also

- [Mesh Modifiers](MeshModifiers.md): the Vertex Editor and Vertex Paint.
- [Bone Animators](BoneAnimators.md): bone-based motion for long or heavy hair and clothing.
- [SlotDataAsset](SlotDataAsset.md): slot settings.
