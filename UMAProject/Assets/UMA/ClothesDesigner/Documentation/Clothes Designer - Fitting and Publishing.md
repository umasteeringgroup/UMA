# Clothes Designer: fitting and publishing

## How the drape works

When you press **Simulate**, the pieces are laid round the body, a little larger than the torso and just above the shoulders. For the first moments gravity is off and the seams pull shut (*Sewing*). Then gravity comes in and the cloth settles onto the body (*Settling*). The cloth keeps the lengths of the flat pieces, so the ease you drafted shows as looseness on the body.

**Show Seams** (on by default) draws every seam in the Scene view in its Pattern Canvas colour: the two edges being sewn, and a thread from each point to the point it is sewn to. The threads shorten as the seam pulls shut and disappear once it is closed; the edges stay, so you can still see where the seams run. Lines behind the body or the cloth are drawn faintly. A thread that crosses another seam's thread usually means a seam needs **Reverse** on the Pattern Canvas.

The cloth collides with the whole body, arms included, keeping the **Clearance** (6 mm by default) from the skin. It does not collide with itself; a garment with layers or deep folds may need pins.

The drape runs on the GPU. Where the graphics card cannot run compute shaders, the same simulation runs on the CPU; **Use CPU Solver** chooses it by hand. Both give the same result to within rounding until the cloth touches the body; after that, small differences can grow into a different fold, as with real cloth.

## Placing the pieces

Before sewing, each piece is laid round a part of the body, a little larger than it, facing out. Where it goes is its placement:

- **Body Part**: what it is laid round. *Torso* (an upright oval round the torso, and the legs below it for skirts and dresses), *Head* (hoods and hats), *Left Arm* / *Right Arm* (a cylinder along the arm from the shoulder joint, for sleeves and cuffs) or *Left Leg* / *Right Leg* (a cylinder round the leg). A leg piece turns from the torso's oval at the hips into its leg's cylinder below the crotch, so the two halves of a pair of trousers start round their own legs and meet at the centre front and back.
- **Faces**: the direction round the part, in degrees. On the torso, head and legs 0 is the front, 180 the back and 90 the body's left. On an arm 0 is the top of the arm, 90 its front and 180 underneath.
- **Above ...** (the top): how far the piece's top starts above the part's top: above the shoulders (torso), above the top of the head (head), above the shoulder joint measured along the arm (arms; negative starts it down the arm), above the waist (legs; trousers that sit on the hips start at about -11).
- **Distance**: how much farther from the body than the other pieces it starts. Give a pocket, a band or anything sewn on top of another piece a few centimetres, so it starts outside it.
- **Across**: moves the piece sideways round its part (across it, when laid flat), towards its right as drawn. A piece is otherwise centred by its outline's middle, which for an L-shaped piece such as half a sock is not where it should sit.
- **Lay Flat**: lays the piece flat beside its part, facing out, instead of wrapped round it. The sample socks' halves are laid flat beside the foot, outside and inside, and sewn round it.

The body's arms, legs, head, crotch and their sizes come from the race's skeleton; a race without a humanoid skeleton lays every piece round the torso and says so.

With the **Place** tool (Cloth Tools), click a piece: it is outlined in yellow and its values are shown. Drag it, and the point you grabbed follows the pointer round the body and up or down (on an arm, a leg or the head, dragging across turns the piece round the part and dragging up or down moves it along); Shift-drag up or down to move it out or in. The pieces are shown laid out while you drag; the cloth is built again when you let go. Escape during a drag puts the piece back; each drag is one Undo step. Moving a piece after simulating lays every piece out again, so simulate afterwards.

Placement is saved with the pattern. A fit made before this version may have its own placement for a piece, which wins; the panel says so and **Use the Pattern's Placement** removes it.

## Fabric

Each piece has its own fabric, saved with the pattern, so a cotton body can have an elastic band, a jersey collar or leather patches. Choose a preset, or open the piece's fabric to change its values (it then shows *Custom*):

| Preset | Use it for | On the example vest |
| --- | --- | --- |
| Cotton | Shirts and plain weaves; the default. | firm, 0.7% stretch hanging |
| Denim | Jeans and jackets: heavy and stiff. | firm, heavier |
| Leather | Very stiff and heavy, grips the body. | firm, heaviest |
| Wool | Suiting and knitwear: folds more softly. | firm, softer folds |
| Silk | Light, limp and slippery. | finest folds |
| Jersey | T-shirt knit. | stretches a little |
| Elastic | Bands, cuffs and ribbing, sewn shorter than the edge they join. | stretches to meet a longer edge |
| Spandex | Tight-fitting pieces, cut smaller than the body. | the stretchiest |

- **Stretch**: 0 firm, 1 very elastic. Firm fabrics keep their length; an elastic piece sewn to a longer edge stretches to meet it. Clothes that hang from the hips (trousers, skirts) need to be held up: cut stretchy ones smaller than the body (the sample leggings are about 12% smaller round than the hips) and gather their waistbands, or they stretch past the hips under their own weight.
- **Softness**: 0 stiff, 1 limp. Softer cloth folds more finely.
- **Weight** (g/m², as fabric is sold): heavier cloth hangs and stretches more under its own weight.
- **Friction**: how much it grips the body.

Change a fabric while the cloth is draped and it carries on from the same shape; nothing is laid out again. **Damping** (how quickly movement dies down) and **Clearance** (the gap kept from the skin) belong to the fit, under *Simulation*.

Cotton, denim and leather look much alike when they only hang: the solver holds all firm cloth about as firm as it can, so they differ mainly in weight and grip. The difference shows when they are pulled or folded.

Very limp, slippery cloth (silk) hanging from the waist can slide over the hips: the solver does not hold its length well enough under the whole skirt's weight. Use it for pieces held at the shoulders (the sample sundress), or a firmer fabric such as wool for a full skirt (the sample circle skirt).

## Shaping the cloth

Pick the tool on the **Cloth Tools** toolbar in the Scene view, or in the panel's Cloth Tools section; both show the same tool, and each button's tooltip says what it does. The toolbar also has the Fit section's Simulate / Pause, Step, Reset and Keep Drape.

Clothes with a piece laid round an arm (sleeves) take their bone weights from the arms as well as the torso, whatever *Weights from Torso Only* says.

- **Grab**: press on the cloth and drag. The cloth around the point (within the Radius, following the cloth and its seams, not jumping to the other side of the body) follows the pointer while the rest stays simulated. Release to let it settle. Escape during the drag puts everything back. Each drag is one Undo step.
- **Pin**: click or drag on the cloth. Full strength holds it exactly; lower strength lets it move a little. *Pins Follow Body* pins move with the body when its shape changes; *Placement* pins hold a point in space. Pins are stored on the flat pattern, so they survive a new Mesh Level.
- **Keep Drape**: remembers the current shape in the fit. Saving the fit and opening it later restores exactly these folds, paused. A kept drape is only restored on the same pattern and Mesh Level; after the pattern changes it is dropped with a message.

## Fit Report

- **Open seams**: the widest gap between sewn edges. Baking welds every seam shut whatever this says.
- **In the body**: cloth points below the skin.
- **Stretch**: how much longer the cloth's edges are than on the flat pattern. A few percent is normal at the shoulders.

## Body shape

Open **Body Shape (DNA)** in the Vertex Editor window and move a slider. The body is built again, the sizes are worked out for it, and the pieces are laid out round it ready to drape. The fit remembers the shape: opening it puts the body back in that shape.

Clothes are weighted on the body as shown, so clothes baked on a heavier shape fit that shape. For clothes that every character of the race can wear, fit on the race's default shape (**Reset Body Shape**).

## Baking

**Bake and Try On**:

1. welds every seam to one position (each side keeps its own UVs),
2. gives every vertex the bone weights of the nearest point on the body (*Weights from Torso Only* leaves the arms out, right for a vest; turn it off for sleeves),
3. builds the slot mesh with normals shared across seams and UV seams, so no lighting seam shows,
4. writes the slot, an overlay with the chosen UMA material, and a wardrobe recipe for the race and wardrobe slot,
5. with *UV Check Texture*, writes a texture showing each piece's outline, 5 cm checks measured on the cloth, the grain and the name, so you can check the print lands right,
6. with *Inside Facing* and *Rim*, adds the facing inside the openings and the rim round them,
7. with *Hide Body Underneath*, makes a mesh hide for body triangles the clothes fully cover, keeping skin within 4 cm of the neck, armholes and hem visible so no holes show through the openings. This needs *Add to Global Library*.

A try-on character of the race then wears the recipe through the normal wardrobe, beside the body, and the Scene view turns to it. **View Fitting Body** and **View Try-On** point the Scene view at either.

**Arms Raised** poses the try-on's arms at **Arm Angle**, measured from hanging at the sides: 90 is level, 180 straight up (150 by default). The arms move the way a body's do: the shoulder joint alone takes an arm to about level, and above the first 30 degrees the shoulder blades and collarbones turn too, a third of the movement (50 degrees with the arms straight up). The shoulders rise with the arms, so the armholes and shoulder seams move as they would in an animation.

Messages after baking point out vertices far from the body (check their weights in the Slot Weight Editor) and materials that hide back faces.

## Inside the openings: facing and rim

Baked clothes are one layer of cloth. With a material that hides back faces (most game materials), looking into the neck, an armhole or up under the hem shows straight through to whatever is behind.

- **Inside Facing** (on by default) adds a turned-in copy of the cloth inside every opening, facing inwards, to **Facing Depth** (5 cm by default) measured along the cloth from the opening. It carries on across sewn seams, so a hem's facing runs all the way round. The facing has the cloth's own UVs and bone weights, so it moves with the clothes and shows the same texture.
- **Rim** adds a narrow band round each opening joining the outside to the facing, so the edge shows the cloth's thickness (**Rim Thickness**, 3 mm by default). It works with or without the facing.

The facing adds triangles only near the openings (617 on the example vest at Medium, against 2,730 for the vest itself). With a double-sided material you do not need it. The normal map (below) is made for the outside; on the facing its folds are lit as if seen from outside.

## Fine folds on a light slot

Small folds need a fine mesh, but a fine mesh makes a heavy slot. Drape fine, bake coarse:

1. In the Fit section, set **Mesh Level** fine: *Very Fine* (1 cm), or *Custom* down to 0.3 cm (5 mm works well). The solver takes more steps a frame at finer levels, so the cloth stays as firm as at 2 cm. A 5 mm vest is about 23,000 points and still drapes in real time on the GPU, and stops by itself when it is still, like a coarse one. Very stiff fabric is softened slightly in bending on fine meshes (the finer the mesh, the more), because cloth that stiff cannot be simulated stably at that fineness: it would tremble and never settle. Folds this fine come out in the normal map anyway.
2. **Simulate** and shape the drape.
3. In Publish, set **Slot Mesh Level** to the level the slot should have, for example *Medium* (2 cm). The slot is meshed at that level and takes its shape from the drape: its points sit on the fine cloth.
4. Leave **Normal Map from Drape** on. The bake compares the fine cloth with the slot at every texel and writes the difference as a normal map (`<Name>_DrapeNormal.png`, imported as a normal map), so the slot still shows the small folds. **Normal Map Size** defaults to the texture size on the Pattern Canvas's UVs tab.

The normal map goes in the overlay's normal map channel when that channel is empty or holds an earlier baked map; a normal map you chose yourself is never replaced (the bake says so and leaves the new map beside the slot). The message after baking says how far the drape's surface turns from the slot's on average, which is how much detail the map carries.

Every mesh level of a pattern shares one UV layout, so a painted texture lines up whatever level the slot is baked at.

## Baking again

The same slot, overlay and recipe are updated; their GUIDs stay. Textures on the overlay and anything you added to the recipe (mesh hides, tags, other races) are kept. The slot's mesh is replaced, so edits made to the slot directly are lost; you are asked first.

## Files

| Asset | What it holds | Needed in a game |
| --- | --- | --- |
| Pattern (`ClothesPattern`) | The flat pieces with their fabric and placement, seams, sizes, mesh level and UV layout. | No |
| Fit (`ClothesFit`) | The race and body shape, allowances, damping and clearance, pins, the kept drape, publish settings and what was baked. | No |
| Slot, overlay, texture, wardrobe recipe | Ordinary UMA content. | Yes |

Patterns and fits are saved under `Assets/UMAProjectData/ClothesDesigner` by default, outside the plugin, so they survive removing and reinstalling it. They need the plugin to be opened again; the baked clothes do not.
