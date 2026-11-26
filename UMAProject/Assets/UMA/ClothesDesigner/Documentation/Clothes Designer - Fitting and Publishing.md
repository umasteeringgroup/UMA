# Clothes Designer: fitting and publishing

This is the reference for everything that happens on the body: the drape, placing pieces, fabric, the cloth tools, and what the bake makes. To learn the work step by step, start with [Tutorial 1: Your First Garment](Clothes%20Designer%20-%20Tutorial%201%20-%20Your%20First%20Garment.md); for a finished, game-ready garment see [Tutorial 6](Clothes%20Designer%20-%20Tutorial%206%20-%20Game-Ready%20Clothes.md). All guides are listed in [Start Here](!Clothes%20Designer%20-%20Start%20Here.md).

| To find | Go to |
| --- | --- |
| What Simulate does, the views, clothes worn underneath | [How the drape works](#how-the-drape-works), [Clothes worn underneath](#clothes-worn-underneath) |
| Where pieces start, and the Place tool | [Placing the pieces](#placing-the-pieces) |
| Fabric presets and what each value does | [Fabric](#fabric) |
| Belts, collars, cuffs and bands | [Belts and other free pieces](#belts-and-other-free-pieces), [Collars, cuffs and bands](#collars-cuffs-and-bands) |
| Every cloth tool | [Shaping the cloth](#shaping-the-cloth) |
| The numbers in the Fit Report | [Fit Report](#fit-report) |
| Publish, Bake and Try On, and what the bake makes | [Baking](#baking) |
| Openings, hats, topology, sharp edges, quads, normal maps | The sections after [Baking](#baking) |
| Which files are made and which a game needs | [Files](#files) |

## How the drape works

When you press **Simulate**, the pieces are laid round the body, a little larger than the torso and just above the shoulders. For the first moments gravity is off and the seams pull shut (*Sewing*). Then gravity comes in and the cloth settles onto the body (*Settling*). The cloth keeps the lengths of the flat pieces, so the ease you drafted shows as looseness on the body.

**Show Seams** (on by default) draws every seam in the Scene view in its Pattern Canvas colour: the two edges being sewn, and a thread from each point to the point it is sewn to. The threads shorten as the seam pulls shut and disappear once it is closed; the edges stay, so you can still see where the seams run. Lines behind the body or the cloth are drawn faintly. A thread that crosses another seam's thread usually means a seam needs **Reverse** on the Pattern Canvas.

The cloth collides with the whole body, arms included, keeping the **Clearance** (6 mm by default) from the skin.

Once the seams are sewn, pieces also keep apart from each other by the **Cloth Thickness** (2 mm by default, under *Simulation*; 0 turns it off), so a collar or a cuff lies on the cloth under it instead of passing through. While the pieces are sewing they may still pass each other, because laid-out pieces often overlap. Layers that start closer than the thickness ease apart over a few frames. Next to a seam the pieces sewn there are not pushed apart, nor are pieces meeting edge to edge. Pieces joined by a folded seam (a collar's leaf and stand) do not push each other apart: their fold holds them instead. With **Fold onto Itself** (on by default, Fit section) a piece also keeps the thickness from itself where it folds over (deep folds, gathers, a long skirt); points closer than 4 cm on the flat piece are neighbours and are left alone. Where pieces of different **Layers** cross (set on the Pattern Canvas), the higher one is moved outside the lower one, whichever side it was on when sewing ended. A push apart stops two layers closing in but never throws them apart (before, sheets laid closer than the thickness flew apart at about 6 mm a frame).

Layers also keep their order beyond the thickness, one way. Cloth within 3 cm under a piece on a higher layer is pushed back in under it, by up to 4 mm a step, and only that cloth moves: the higher layer is not pushed out by it. So a belt or a collar's leaf presses the cloth under it in instead of the cloth coming through. Where the cloth under a higher layer already lies against the body and cannot go further in, the higher layer is kept outside it instead, so the body holds both. Neither happens next to a seam joining the two pieces. Garments with more than one layer simulate about a fifth slower; garments on one layer are unchanged. Measured on Human Female 3.0: a dress 116 cm round the waist under a free belt had 115 of its 172 points at the belt outside it (up to 55 mm) without this, and 35 of 170 (up to 26 mm) with it, gathered to about 76 cm instead of 104 cm; the Polo Shirt's collar leaf no longer sinks into its stand, and turns down on Human Male 3.0.

**Show** colours the cloth: *Texture* (each piece's outline, grain and name), *Stretch* (how much longer the cloth is than the flat pattern: blue none, green 5%, yellow 10%, red 20% or more) or *Body Distance* (red inside the body, orange closer than the clearance, green at the clearance, blue 3 cm and more).

## Clothes worn underneath

**Worn Underneath** (Fit section) puts wardrobe recipes on the fitting body: pick one and press **Wear**. The cloth is laid round them and drapes over them, for a jacket over a shirt or a coat over armour. With **Size over Them** (on by default) the chest, waist and hips are measured round the worn clothes, so the sizes fit over them; off, the sizes follow the body itself. Measured on Human Female 3.0 wearing the medieval torso armour: the chest measured 115 cm round the armour (100 cm without), and the vest draped over it with its seams closed (0.8 mm at most) and nothing inside. **Remove** takes them off again. The fit remembers what was worn.

**Freeze** (in a piece's placement) holds the piece where it is while the rest is simulated: drape one layer, freeze it, then drape the next over it. The drape is kept.

**Show Back Faces** (on by default, next to **Show Seams**) draws the inside of the cloth as well as the outside, so the lining side of an open garment does not disappear. It changes only the preview, not the drape or the published slot.

The drape runs on the GPU. Where the graphics card cannot run compute shaders, the same simulation runs on the CPU; **Use CPU Solver** chooses it by hand. Both give the same result to within rounding until the cloth touches the body; after that, small differences can grow into a different fold, as with real cloth.

## Placing the pieces

Before sewing, each piece is laid round a part of the body, a little larger than it, facing out. Where it goes is its placement:

- **Body Part**: what it is laid round. *Torso* (an upright oval round the torso, and the legs below it for skirts and dresses), *Head* (hoods and hats), *Left Arm* / *Right Arm* (a cone along the arm from the shoulder joint that follows the arm's thickness, wide at the shoulder and narrow at the wrist, for sleeves and cuffs; see below) or *Left Leg* / *Right Leg* (a cylinder round the leg). A leg piece turns from the torso's oval at the hips into its leg's cylinder below the crotch, so the two halves of a pair of trousers start round their own legs and meet at the centre front and back.
- **Faces**: the direction round the part, in degrees. On the torso, head and legs 0 is the front, 180 the back and 90 the body's left. On an arm 0 is the top of the arm, 90 its front and 180 underneath.
- **Above ...** (the top): how far the piece's top starts above the part's top: above the shoulders (torso), above the top of the head (head), above the shoulder joint measured along the arm (arms; negative starts it down the arm), above the waist (legs; trousers that sit on the hips start at about -11).
- **Distance**: how much farther from the body than the other pieces it starts. Give a pocket, a band or anything sewn on top of another piece a few centimetres, so it starts outside it.
- **Across**: moves the piece sideways round its part (across it, when laid flat), towards its right as drawn. A piece is otherwise centred by its outline's middle, which for an L-shaped piece such as half a sock is not where it should sit.
- **Lay Flat**: lays the piece flat beside its part, facing out, instead of wrapped round it. The sample socks' halves are laid flat beside the foot, outside and inside, and sewn round it.
- **Tilt** (arms and legs): turns the tube the piece is wrapped on about its top. On an arm, positive lowers the cuff end below the arm; on a leg, it moves the hem end out from the other leg. It moves only as far as the limb still fits inside, so a close sleeve barely moves; give its end room with End Distance first.
- **End Distance** (arms and legs): how much farther out the far end (cuff, hem) starts than the top; negative brings it in. It changes evenly along the piece. A sleeve is never laid inside the clearance from the arm, or narrower than it is cut.

Sleeves are laid 1.5 cm off the arm, measured along its length, or wider where they are cut wider, so each one covers most of the way round and its underarm seam closes under the arm. A sleeve laid farther out covers less of the arm, and its seam can close above it, so that the arm ends up outside the sleeve. If a sleeve's edges would meet above the arm, the Fit section says so; bring its End Distance or Distance in, or tilt it down. Measured on Human Female 3.0 with a long-sleeve tee: before the cone, each sleeve was laid on one 9 cm radius and its cuff covered about a third of the way round. From the elbow down the sleeve closed over the top of the forearm, and stretched up to 306%. On the cone, both sleeves closed round the whole arm, with seams within 2.7 mm.

The body's arms, legs, head, crotch and their sizes come from the race's skeleton; a race without a humanoid skeleton lays every piece round the torso and says so.

With the **Place** tool (Cloth Tools), click a piece: it is outlined in yellow and its values are shown. Drag it, and the point you grabbed follows the pointer round the body and up or down (on an arm, a leg or the head, dragging across turns the piece round the part and dragging up or down moves it along); Shift-drag up or down to move it out or in. On an arm or a leg, Ctrl-drag (Cmd on a Mac) up or down tilts the piece, and Ctrl+Shift-drag moves its far end out or in. The pieces are shown laid out while you drag; the cloth is built again when you let go.

The selected piece's **layout guide** shows what it is laid on: cyan rings round the oval (torso, head), the cone (arm) or the tube (leg), every 5 cm, with a yellow line down the piece's middle. Its handles move it like the placement fields do:
- **Yellow dot**: turns it round the body or limb (Faces).
- **Top**: moves it up or down, or along an arm.
- **Distance**: moves it out or in.
- **End Distance** and **Tilt** (arms and legs, at the far end): move that end out or in, and tilt it.

Each handle drag is one Undo step. Turn the guide off with **Show Layout Guide**. A piece laid flat has no guide.

Selecting a piece in the Scene view also selects it on the Pattern Canvas, and selecting one on the canvas (or in its piece list) selects it in the Scene view. Escape during a drag puts the piece back; each drag is one Undo step. Moving a piece after simulating lays every piece out again, so simulate afterwards.

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
| Rib | Waistbands that hold heavy clothes up: firm, and grips the body as hard as the solver allows. Cut it shorter than the waist. | barely stretches |
| Fleece | Sweatshirt fleece: firm like cotton, softer, grips more. | firm, soft folds |
| Mail | Chain mail: does not stretch, very heavy, hangs straight in limp folds. The sample Hauberk uses it. | not tried on the vest |

- **Stretch**: 0 firm, 1 very elastic. Firm fabrics keep their length; an elastic piece sewn to a longer edge stretches to meet it. Clothes that hang from the hips (trousers, skirts) need to be held up: cut stretchy ones smaller than the body (the sample leggings are about 12% smaller round than the hips) and gather their waistbands, or they stretch past the hips under their own weight.
- **Clothes sliding down the hips** as the simulation goes on are held up by a firmer waistband more than by friction. Measured on the sample sweatpants (Human Female 3.0): with an Elastic band the waistband slid 11.4 cm down as the legs' weight came on; a firmer band alone saved 6 cm, friction 1 on the band alone 3 cm, and halving the legs' weight 4 cm. A Rib band with Fleece legs (friction 0.8) slid 2.3 cm (4 cm since cloth by an open seam stops gripping, below), and the sample now uses them. Friction cannot hold more than the band presses on the body, so a slack band slides whatever its friction. To hold clothes exactly where they are, **Pin** the waistband with *Pins Follow* set to *Body*.
- **Softness**: 0 stiff, 1 limp. Softer cloth folds more finely.
- **Weight** (g/m², as fabric is sold): heavier cloth hangs and stretches more under its own weight.
- **Friction**: how much it grips the body. Cloth within 1 cm of a seam that is still more than 2 mm open does not grip, whatever its friction, until the seam closes: a seam pulls its cloth across the body, which presses it in, and gripping that hard it locked seams open (measured on Human Female 3.0: the hoodie's underarms stayed 11.5 mm open, now 1.9 mm; the beanie's brim corners 5.1 mm, now none). A seam that has to wrap round a part of the body to close (a sock's sole seam, with the halves laid beside the foot) can still catch on it: lay the pieces so the seam's edges start past that part and only need to come together (the sample socks' halves are cut with a snug foot and laid with their soles below the foot; their sole seam had stayed 45 mm open, now 0.3 mm).
- **Stretch by Grain**: woven cloth gives more across the grain than along it, and most on the bias (diagonally); knits stretch most across. Turn it on to set **Across the Grain** and **On the Bias** apart from *Stretch* (along the grain). The piece's **Grain** on the Pattern Canvas sets the direction.

Change a fabric while the cloth is draped and it carries on from the same shape; nothing is laid out again. **Damping** (how quickly movement dies down), **Clearance** (the gap kept from the skin) and **Cloth Thickness** (the gap kept between pieces) belong to the fit, under *Simulation*.

Cotton, denim and leather look much alike when they only hang: the solver holds all firm cloth about as firm as it can, so they differ mainly in weight and grip. The difference shows when they are pulled or folded.

Very limp, slippery cloth (silk) hanging from the waist can slide over the hips: the solver does not hold its length well enough under the whole skirt's weight. Use it for pieces held at the shoulders (the sample sundress), or a firmer fabric such as wool for a full skirt (the sample circle skirt).

## Belts and other free pieces

A piece on a higher **Layer** that is sewn to nothing on another layer is a *free piece*: a belt worn over a dress, for example (tacks do not count as sewing it on). A free piece is laid out about 1 cm outside the cloth under it, keeping its own length, so it covers less of the way round and its own seams start open. Those seams, and its tacks to the cloth under it, close only after the rest is sewn, over about a second, while the pieces already keep apart: the piece tightens round the cloth under it and presses it in, instead of passing through it. Gravity comes in once they are closed (the Fit section says *Tightening free pieces* meanwhile).

Tack a belt to the dress at a few points near its ends, like belt loops, so it stays at the waist on a body whose waist is hardly narrower than its hips. The belt gathers the dress under it and keeps the gathers under it (the layers above): the **Belted Dress** sample's two loose panels, about 116 cm round the waist, are drawn in by a 66 cm belt to nearly half. A few folds may still show at the belt's top and bottom edges (on Human Female 3.0 the worst stood about 2 cm out). A belt only presses on the cloth, so where a gathered waist must be even all round, sew a waistband between the bodice and the skirt instead, as the **Elastic Waist Dress** does: its seams pull the cloth in.

## Collars, cuffs and bands

A shirt collar is cut in two parts, as a shirt maker cuts it (the sample **Oxford Shirt** has one):

- The **stand**, a band sewn along the neckline, with *Firm* interfacing and *Mesh Detail* 3. Cut it as a gentle arc (its ends rising 2 to 3 cm), not a rectangle: its top edge is then a few percent shorter than its neck edge, so sewn on it is a cone that hugs the neck. A rectangular band lies down at the front.
- The **leaf** (the collar you see), with *Light* interfacing and *Mesh Detail* 3, sewn to the stand's top edge on a **Folded** seam and drawn as it looks turned down: its neck edge along the top, its points hanging at both ends. Place it like the stand, with a Distance of 1 cm, so it starts turned down outside it.

Measured on the sample shirt, the cut, the fold and the layout make most of the shape; interfacing keeps the stand from crumpling (the mean fold between its triangles drops from 16 to 12 degrees) and the leaf turned under. Cuffs and waistbands take *Firm* interfacing and a Mesh Detail of 2 the same way. The sample shirt's front is one piece, so its collar stands open at the throat; a buttoned front (two fronts held by buttons) is not possible yet.
A band sewn round a part cut in two (a waistband on a front and a back, a cuff on a trouser leg's front and back hems) is best cut in two as well: a front half and a back half, each placed facing the piece it is sewn to and joined at the side seams. A band cut in one piece is laid as one arc on its part, its ends often far from the seam where they must meet; sewing drags them round the body and they can stop short. On the sample sweatpants, one-piece ankle cuffs stopped 33 to 37 mm short of closing at the outseam; cut in halves, every seam closes to within 0.8 mm.

## Shaping the cloth

Pick the tool on the **Cloth Tools** toolbar in the Scene view, or in the panel's Cloth Tools section; both show the same tool, and each button's tooltip says what it does. The toolbar also has the Fit section's Simulate / Pause, Step, Reset and Keep Drape.

With *Weights from Torso Only*, pieces not laid round an arm take their bone weights from the body without the arms, so a vest's side does not follow the arm next to it; pieces laid round an arm (sleeves) take theirs from the arms as well. This is decided piece by piece.

- **Size**: click a piece to select it: it gets three handles, like Unity's scale tool, on the cloth: a red one at each side and a green one at its hem. Drag a side handle out or in to make the piece wider or narrower, both sides moving by as much as you drag (the piece stays centred where it is laid), or the hem handle down or up to make it longer or shorter, the top staying where it is laid. You can also drag anywhere on the piece: the drag is read along the piece as it lies on the body (on a sleeve, down the arm is longer), and only the direction you moved further changes. **Shift** keeps the piece's proportions. A label at the pointer shows the new width and length and what else changes. The panel shows the selected piece's **Width** and **Length** as cut on this body, to type exact sizes. Each change is one Undo step.
  - **While you drag:** the simulation waits, and the cloth is shown with the pieces at their new size, worked out from the cloth as it hung when you pressed, by geometry alone. The handles and the piece's outline move with it. Pieces wrapped round a body part together at the same width (a front and a back sized by the chest, a sleeve round its arm, a belt's two halves) grow or shrink round that part, so their seams stay together; a piece widened on its own stretches across itself from its middle; a longer or shorter piece stretches down itself from its top; a piece sewn to a resized one (a cuff, a sleeve on a widened body) moves with it; and nothing is shown inside the body. Escape puts the cloth back as it was.
  - **On release:** the pattern changes, the cloth is built again from the drape as it hung and simulates; the new size is eased in, about a third of a percent of the piece's size a simulated frame (a fifth changed at once blew a free-hanging test tube apart; eased in, it came to the size asked for). The view keeps showing the preview until the simulation has caught up, then blends into it over half a second. A size typed in the panel eases in the same way. Cloth only laid out, not yet simulated, is laid out again at the new size and blends into it.
  - **Sizes:** where one of the pattern's sizes sets the width or length (see the Pattern Canvas's *Sizes*), scaling the outline would only be sized back, so that size changes instead: its allowance (this fit's own allowance when it has one), or its length when it is fixed. Every piece it sizes changes with it: widening a T-shirt's front widens its back, widening one sleeve widens both. The panel says which size sets each.
  - **Outline:** otherwise the piece's outline is scaled, with what is drawn on it (lines, darts, pleats, tacks, parts, pins and Fit Brush paint), about its middle across and its top. A linked mirror's original is resized and the mirror follows. With **Resize Matching Pieces** (on by default) the pieces that share a size with it are resized the same way, so the seams between them still match: the Belted Dress's two belt halves grow taller together. Off, only that piece changes, and the panel names any seam whose two sides no longer match (the longer side gathers when sewn).
  - A piece made larger may leave its place in the texture; the Fit messages then say to **Repack UVs** (Pattern Canvas, UVs tab).
  - **Measured** on Human Female 3.0: a draped T-shirt's front made 4 cm wider (its chest allowance 10 to 18 cm, the back with it) and the Belted Dress's belt made half again as tall each settled within about 2 s, seams within 2.6 mm and no cloth in the body, the same as draping them again from the start. The drag's preview takes about 15 to 25 ms when you press (finding the body under every point of the cloth, on every core) and 4 to 8 ms a step of the drag (2 to 5 ms of it working out which pieces change and by how much): a T-shirt's front 8 cm wider moves the front and back 2 to 3 cm out round the body, 15 cm longer moves its hem down 15 cm.
- **Grab**: press on the cloth and drag. The cloth around the point (within the Radius, following the cloth and its seams, not jumping to the other side of the body) follows the pointer while the rest stays simulated. Release to let it settle. Escape during the drag puts everything back. Each drag is one Undo step. **Grab One Layer** moves only the cloth on the layer of the piece you press on (its **Layer** on the Pattern Canvas) and holds the cloth of every other layer where it is while you drag: pull a bodice up to make slack above a band on a higher layer, and the band stays put. On release everything is simulated again. A plain grab reaches another layer only through a seam or a tack within the Radius: it takes a band sewn to the bodice along with it, but on the **Belted Dress**, whose belt is held only by belt loops near the side seams, pressing the front of the dress takes none of the belt either way, and the tight belt stays put by itself.
- **Pin**: click or drag on the cloth. Full strength holds it exactly; lower strength lets it move a little. *Pins Follow Body* pins move with the body when its shape changes; *Placement* pins hold a point in space. Pins are stored on the flat pattern, so they survive a new Mesh Level.
- **Roll**: lifts cloth that has sunk into, or lies flush with, the cloth behind it, such as a collar's leaf on its stand, where the two z-fight. Press on the cloth that belongs in front and drag over the place. The stroke keeps to the piece and side you pressed on, even where other cloth pokes through it. Under the brush it lifts that sheet out until it is the **Cloth Thickness** clear of the cloth behind (at least 1 mm), softly towards the brush's edge.
  - **What it leaves alone:** it stops where the cloth folds over or turns sharply (a fold line, a pleat's crease). Points sewn to other cloth stay put, and the sheet rises from them in a gentle slope, so a seam is not pulled open. Cloth within two rings of a seam is not treated as behind, because sewn pieces meet closer than any thickness there.
  - **Undo and simulating:** each stroke is one Undo step, and Escape puts the stroke back. Rolling does not start the simulation, so roll as a last step, then **Keep Drape**.
  - **Folded seams:** the simulation keeps pieces apart on whichever side they lie, except a folded seam's two pieces (a collar's leaf and stand), which it does not collide. Simulating again may let those settle back a little.
  - **Measured** on the Oxford Shirt sample, collar leaf over its stand: before rolling, 36 leaf points lay inside the stand and 11 within 1 mm of it. After one pass along the leaf, none did, the leaf moved at most 4.9 mm, the stand did not move, and 99% of the leaf's edges changed length by less than 10%. Two more simulated seconds brought 3 points back inside.
- **Fit Brush**: paints how the cloth fits, where you want it, with the Radius and Strength. Pick what it paints in the panel, or on the **Fit Tools** toolbar that floats just above the Cloth Tools toolbar while the brush is the tool (each mode with a dot in its paint colour); **Ctrl**-drag takes that paint off. While the brush is the tool the cloth shows its paint over the lit cloth, so its folds and where it stands off the body still show: unpainted cloth as it is lit, green hug, blue smooth, orange puff, purple slim, teal stiffen, brown weight. Each stroke is one Undo step, and the cloth simulates when you let go. The paint is kept in the fit on the flat pattern, as pins are, so it survives a new Mesh Level and the bake takes the drape it makes.
  - **Hug** cuts the painted cloth smaller (up to 15% at full strength) and draws it onto the body each frame, out by the clearance: a shrink-wrap for sleeves, a bodice or a waist. Measured on a loose hoodie on Human Female 3.0: hugged sleeves came in from 2.1 to 0.9 cm off the arm. Hugged cloth wrinkles where it gathers: paint Smooth over it.
  - **Smooth** flattens wrinkles and bumps (after hugging, round a collar, under the arms) and keeps the cloth where it is round the body: each point is drawn towards its neighbours along the cloth's normal, by how much more it bulges than they do. On the hoodie's front: wrinkles 35% smaller, the cloth no closer to the body.
  - **Puff** pushes the cloth out from the body, as padding or air would (the hoodie's front moved out 6 mm); **Slim** draws it in towards the body (in 22 mm, gathering as it comes in). Both add to the piece's own pressure.
  - **Stiffen** makes the cloth firmer where painted: its stretch and bend give up to a hundredth as much, and painted points are tied to the painted points two edges away, as firmly as firm interfacing at full strength (light interfacing at half). Use it for a firm cuff, band or edge without a separate interfaced piece. It shows on soft fabrics: a stiffened silk sundress held its A-line shape (hem 5 cm higher, 1.5 cm wider), a stiffened hem fell in fewer, larger folds. Cotton and heavier fabrics already bend as firmly as the simulation keeps stable, so stiffening them changes little.
  - **Weight** makes the cloth heavier where painted, up to four times its weight: a weighted hem that hangs straight, the heavy edge of a cape. A weighted sundress hem hung 4.8 cm lower in straighter folds. Weight pulls on whatever holds the clothes up: a skirt held only at the waist by friction (the circle skirt) slid off with a weighted hem.
  - Hug and Smooth read the cloth's position every other frame, so a painted cloth simulates a little slower (on the GPU, about 6 ms a frame for two hugged sleeves).
- **Measure**: click two points on the cloth or the body for the distance between them. The panel also shows how far round the cloth and the body are at the first point's height, and the ease between them.
- **Dart**: puts a dart on the cloth where it is worn. Press where the dart's point goes and release towards the edge it opens on (a click alone opens it on the nearest edge, with its usual point). **Shift**-drag makes a double-pointed dart from one point to the other. The dart is added to the piece as drawn (each point of the cloth knows where it is on its piece), so it shows on the Pattern Canvas and is edited there; on a linked mirror it goes on the original and the mirror follows. The cloth is built again keeping its drape and simulates, so the dart is sewn closed on the body. Undo takes it off.
- **Pleat**: click on the cloth near the edge a pleat folds from (a skirt's waist): a 2 cm knife pleat is added there, then sewn on the body as for a dart. Set its kind and depth on the Pattern Canvas.
- **Draw Piece**: click points round a new piece on the body's torso; click the first point again (or press **Enter**, or **Finish Piece** in the panel) to finish. **Backspace** takes the last point back, **Escape** starts again. The outline is unrolled round the torso (across by its length round the body, at the cloth's distance from it, up by height), added beside the other pieces on the canvas, and laid where it was drawn (its Faces and Below Shoulders are set). Its edges are straight and unsewn: shape and sew them on the Pattern Canvas. The tools need cloth on the body, so draw a piece once the pattern has at least one.
- **Body Region**: paint a region on the body anywhere (Shift-drag takes it off; with **Mirror** on, the other side is painted too), then press **Make Pieces**. The region is split by body part (head, torso, each arm and leg), each part that goes all the way round is cut open (at the back of the torso and head, under the arm, inside the leg), and each is unrolled flat as rigidly as it can be; where that would stretch the cloth more than 6% (a cap, a bodice over the bust) it is split into gores round the part instead. Edges where parts, gores or a cut meet are sewn; the rest are openings. **Ease** cuts the pieces larger (or, below 0, smaller for stretch fabrics), **Fabric** sets what they are made of. The pieces are ordinary pattern pieces, edited on the Pattern Canvas, and on the race they were painted on they are laid exactly where they were painted (on other races, round their part like any piece). Measured on Human Female 3.0: a band round the upper arm (mirrored) made two sleeves at 3% stretch; a skull cap made six gores at 3 to 5%; a short unitard (torso, tops of the arms and legs) made ten pieces, its torso in six gores at 7 to 11%, and draped with every seam within 0.7 mm and no cloth in the body. Small painted scraps on their own are left out, and the panel says how many.
- **Keep Drape**: remembers the current shape in the fit. Saving the fit and opening it later restores exactly these folds, paused. A kept drape is only restored on the same pattern and Mesh Level; after the pattern changes it is dropped with a message.

## Fit Report

- **Open seams**: the widest gap between sewn edges. Baking welds every seam shut whatever this says.
- **In the body**: cloth points below the skin.
- **Stretch**: how much longer the cloth's edges are than on the flat pattern. A few percent is normal at the shoulders.

## Body shape

Open **Body Shape (DNA)** in the Vertex Editor window and move a slider. The body is built again, the sizes are worked out for it, and the pieces are laid out round it ready to drape. The fit remembers the shape: opening it puts the body back in that shape.

Clothes are weighted on the body as shown, so clothes baked on a heavier shape fit that shape. For clothes that every character of the race can wear, fit on the race's default shape (**Reset Body Shape**).

## Baking

### Publish

**Publish** (at the end of the **Cloth Tools** toolbar in the Scene view, and **Publish...** at the top of the panel's Publish section) turns the drape into clothes in one step. It asks for:

- **Name**: the slot, overlay and wardrobe recipe are named after it (a new pattern starts with the pattern's name, words run together),
- **Wardrobe Region**: where the recipe goes on the character, from the race's own regions. It is asked for until the clothes have been published once, then the last choice is offered again,
- **Material** (the UMA material) and **Folder**.

The recipe is made compatible with the race of the character in the designer, shown in the window. Publish then bakes as **Bake and Try On** does (below), adds the slot, overlay and recipe to the Global Library, saves the pattern and fit (you choose where the first time) and shows the clothes on the try-on character. Publishing again under the same name updates the same assets, keeping their GUIDs. The other Publish settings (Inside Facing, LOD Slots, Hide Body Underneath and the rest) are set in the panel's Publish section.

With the **Overlay Painter** installed, the window also has **Publish and Open in Overlay Painter** (see [below](#publish-and-open-in-overlay-painter), and [Tutorial 4](Clothes%20Designer%20-%20Tutorial%204%20-%20Texturing%20with%20the%20Overlay%20Painter.md)).

### Bake and Try On

**Bake and Try On** bakes with the settings in the panel's Publish section, without asking for a name and without saving the pattern and fit. Once the clothes have been baked it reads **Bake Again and Try On**. The bake:

1. welds every seam to one position (each side keeps its own UVs),
2. gives every vertex its bone weights from the body by the fit's **Weighting** method (Fabric section, *Bone Weights*), with the shared methods described in [Copying Bone Weights to Clothes](../../Docs/WeightTransfer.md). **Robust Inpaint** (the default) copies the body where the cloth lies on it (within **Match Distance**, 4 cm, facing the same way within **Match Angle**, 35 degrees, and not deep inside it) and fills the rest in smoothly over the cloth, so a skirt's hem between the legs follows both legs and the hips instead of jumping to the nearer leg. **Tight Surface** copies the closest point everywhere; **Volumetric** samples the body's weights spread through the space round it (long skirts, gowns, capes); **Exact Match** copies body vertices at the same place; **Simulated Fit** drapes the clothes in seven test poses with the solver and fits the weights that reproduce how they hang (the bake takes a few seconds longer); **Auto** tries Tight Surface, Robust Inpaint and Volumetric in the test poses and keeps the one that pokes into the body and stretches least. **Max Bones** (4) and **Mirror Left/Right** apply to all of them. The bake messages say what the vertices got and, for Auto, how each method scored. Measured on Human Female 3.0 in the test poses, the old nearest-point weights stretched the A-line skirt's worst edge to 15 times its length and the circle skirt's to 24 times; with Robust Inpaint both stay under twice, and the T-shirt's deepest poke-through drops from 40 to 9 mm. Cloth lying on the body keeps the body's weights,
3. builds the slot mesh with normals shared across seams and UV seams, so no lighting seam shows,
4. writes the slot, an overlay with the chosen UMA material, and a wardrobe recipe for the race and wardrobe slot,
5. with *UV Check Texture*, writes a texture showing each piece's outline, 5 cm checks measured on the cloth, the grain and the name, so you can check the print lands right,
6. with *Inside Facing* and *Rim*, adds the facing inside the openings and the rim round them,
7. with *Hide Body Underneath*, makes a mesh hide for body triangles the clothes fully cover, keeping skin within 4 cm of the neck, armholes and hem visible so no holes show through the openings. This needs *Add to Global Library*.
8. with **Buttons and Parts**, adds the pattern's buttons (tacks marked Button) and parts to the slot. Each moves with the cloth under it (its bone weights) and takes the cloth's texture where it sits.
9. with **Topstitching**, draws the seams' topstitching (set on each seam on the Pattern Canvas) into `<Name>_Topstitch.png` and puts it on the slot as a second overlay, over the clothes' own, in the **Thread Colour**. It needs the wardrobe recipe.
10. with **Piece Looks**, bakes each look the pieces use (see the Pattern Canvas's *Looks*) into `<Name>_Look_<Look>_Color.png` (with `_Normal.png` and `_Mask.png` when the look has those textures) and an alpha mask of its pieces, `_Pieces.png`, and puts each on the slot as an overlay over the clothes' own, under the topstitching, in the look's colour or shared colour. It needs the wardrobe recipe.
11. with **LOD Slots** above 0, bakes coarser copies of the slot (`<Name>_LOD1`, `_LOD2` ...), each meshed half again as coarse as the one before (measured on the vest: 3,525 triangles, LOD1 1,495). A character with UMA's Simple LOD component swaps them in by distance; they are drawn with the same overlays.
12. with **Body Shapes**, fits the slot to each body shape in the list as well as the fit's own: one DNA at the top or bottom of its range. A hidden character of the race wears the slot, that DNA is changed, the slot is moved with the body under it and relaxed, and what the bones did not do is kept as a blendshape on the slot named after the DNA (`DNA_belly_Up`, `DNA_belly_Down`). See *Body shapes* below. It needs the wardrobe recipe.
13. with **Cloth Motion**, lets the clothes move at run time (see *Cloth that moves* below).
14. with **Full Thickness**, gives the whole garment a thickness instead of a facing at the openings: a turned-in copy of all of it, **Thickness** inside, joined to the outside at every opening. About twice the triangles: for thick cloth seen from both sides (coats, capes).
15. gives each piece with a **Thickness** of its own (Pattern Canvas) that thickness: a copy of the piece laid outward by it, joined to the cloth round its outline by a wall with crisp edges, so a belt or a waistband stands out from what it is sewn to and what lies under it stays under it. An edge sewn to another thick piece gets no wall, so a band cut in two halves bakes as one solid band. The drape is not changed; the designer draws the thick pieces with their thickness on the live cloth too.

With *Add to Global Library* off, nothing the bake writes is added to the library: UMA's automatic registration of new slots and overlays is told to leave them (their *No Auto Add* flag is set). With it on, the flag is cleared.

A try-on character of the race then wears the recipe through the normal wardrobe, beside the body, and the Scene view turns to it. **View Fitting Body** and **View Try-On** point the Scene view at either.

### Publish and Sculpt

**Publish and Sculpt** finishes the clothes by hand, for small problems not worth chasing in the simulation (a fold that z-fights, a corner of a pleat). After one question it:

1. bakes as **Bake and Try On** does,
2. saves the pattern and the fit, so the fit keeps what was baked (a pattern or fit never saved asks where; cancelling leaves you in the Clothes Designer with a message),
3. closes the Clothes Designer and opens the Vertex Editor's **Sculpt** on a character of the race wearing the baked slot, in the pose the clothes were draped in. Only the slot can be edited; the body is locked.

Sculpt works as it always does and saves into the slot. The clothes do not need to be in the Global Library: the slot and its overlay are put on the character directly.

Sculpting edits the baked slot itself, not the pattern or the fit. Baking again from the Clothes Designer replaces the slot's mesh, so sculpted changes are lost: sculpt last.

### Body shapes

UMA's DNA moves the clothes with the body's bones, but bones do not carry all of a body's shape: with a thinner body the bones leave the cloth standing off it, and in a few places a bigger one pushes through. Measured on Human Female 3.0 with four close-fitting samples (sheath dress, leggings, T-shirt, tank top) and nine body DNA at both ends of their range: the bones alone left the cloth 1 to 4 cm away from where it fits (on average over the garment; *upperWeight* at 0 the most), and put cloth inside the body at *belly* 0 (61 to 71 vertices) and, on leggings, *lowerWeight* and *lowerMuscle* at 1 (26 and 19). Fitted, none were inside except *belly* at 0 (21 to 36). Each shape takes about 1.7 seconds to fit.

Add a shape with **Add Body Shape** in the Bake panel (a DNA and *Top* or *Bottom*), then bake. The shapes are only blendshapes until a DNA drives them: **Drive Shapes from DNA** adds a blendshape effect to each DNA asset (0 at the DNA's middle, full at the end it was fitted to). It asks first, because the DNA assets are shared by every character of the race; every garment baked with the same shapes uses the same names, so one effect drives them all. A DNA asset with unsaved changes is left alone.

### Cloth that moves

With **Cloth Motion**, the clothes move at run time with Unity's Cloth. Cloth within **Held Within** of the body (1.5 cm) moves only with the bones; further away, it may swing from where the bones put it by **Swing per cm** times the cloth between it and where it is held (0.3: a hem 30 cm below the waistband swings up to 9 cm), at most **Most Swing**. The bake gives the slot these per-vertex limits and a renderer of its own (`<Name>_ClothRenderer`, with cloth settings from the fabric of its largest piece in `<Name>_ClothMotion`), so Unity simulates only the clothes, not the body. Checked on Human Female 3.0 with the A-line skirt: the character got a second renderer with Unity Cloth on it, 1,832 coefficients for its 1,832 cloth vertices, up to 5.6 cm of swing, 1,193 held. Unity Cloth simulates only in Play mode. To collide with the body, add UMA's **Physics Avatar** to the character: its colliders are given to the cloth. Baking again with Cloth Motion off takes the cloth off the slot.

### Publish and Open in Overlay Painter

**Publish and Open in Overlay Painter**, in the Publish window, publishes as **Publish** does, then closes the Clothes Designer and opens the **Overlay Painter** on the published slot and its overlay, to texture them. The pattern's trims (see the Pattern Canvas's *Trims*), if it has any, come in as path layers in a group named **Trim**, each drawn by its construction along the trim's place in the slot's UVs. The group is selected when the painter opens, so every trim shows; click one in the Scene view or the UV window to edit it (the group's *Edit Paths Together*, on by default). Paint, edit and save them in the Overlay Painter as usual. The button is shown only when the Overlay Painter is installed: it registers itself with UMA, so the Clothes Designer finds it without depending on it. (It replaces *Publish and Paint Trims*, which did the same for patterns with trims, from the panel's Publish section and without asking for the name and wardrobe region.)

**Try-On Animation** plays an animation clip on the try-on character: scrub it with **Time** or press **Play**, to see the clothes move. (Arms Raised does not apply while a clip is set.)

**Arms Raised** poses the try-on's arms at **Arm Angle**, measured from hanging at the sides: 90 is level, 180 straight up (150 by default). The arms move the way a body's do: the shoulder joint alone takes an arm to about level, and above the first 30 degrees the shoulder blades and collarbones turn too, a third of the movement (50 degrees with the arms straight up). The shoulders rise with the arms, so the armholes and shoulder seams move as they would in an animation.

Messages after baking point out vertices far from the body (check their weights in the Slot Weight Editor) and materials that hide back faces.

## Inside the openings: facing and rim

Baked clothes are one layer of cloth. With a material that hides back faces (most game materials), looking into the neck, an armhole or up under the hem shows straight through to whatever is behind.

- **Inside Facing** (on by default) adds a turned-in copy of the cloth inside every opening, facing inwards, to **Facing Depth** (5 cm by default) measured along the cloth from the opening. It carries on across sewn seams, so a hem's facing runs all the way round. The facing has the cloth's own UVs and bone weights, so it moves with the clothes and shows the same texture.
- **Rim** adds a narrow band round each opening joining the outside to the facing, so the edge shows the cloth's thickness (**Rim Thickness**, 3 mm by default). It works with or without the facing.

The facing adds triangles only near the openings (617 on the example vest at Medium, against 2,730 for the vest itself). With a double-sided material you do not need it. The normal map (below) is made for the outside; on the facing its folds are lit as if seen from outside.

## Hats and hoods: Holds Hair Down

UMA can squash ("smoosh") hair inside a hat when a character is built. **Holds Hair Down** (in Publish, for clothes with pieces laid round the head; on by default) sets that up: the bake publishes `<Name>_SmooshPlane`, a four-point clipping-plane slot across the openings of the head pieces (a beanie's brim, a hood's face), tags the clothes' slot `Smooshtarget`, and puts the plane in the wardrobe recipe. Hair is held down only if its slot is smooshable and tagged `Smooshable`: **Check Hair...** lists the hair slots in the Global Library that are not, and offers to fix them (it asks first, and saves only those slots). Measured with the Beanie on Human Female 3.0 and UMA's own smoosh: of three hair slots, the points poking through the beanie fell from 1,116 to 86, 2,083 to 141 and 1,699 to 130. This happens once, when the character is built; hoods that move as cloth, and hair that moves, are an upgrade planned in `Plans/RealtimeHoodSmooshPlan.md`.

## Mesh topology

**Mesh Topology** (in the Fit section, under Mesh Level; kept on the pattern) chooses how the inside of each piece is meshed:

- **Even** (the default): nearly equilateral triangles, the most even cloth for the simulation.
- **Grain Grid**: squares along each piece's grain and across it, each split in two, so the mesh's rows and columns follow the warp and the weft, and turn with a piece cut on the bias. Measured on the vest: about two thirds of a piece's inside edges run along or across the grain (the rest are the squares' diagonals), against under half for even triangles. It is cleaner to sculpt, weight and read, and each pair of triangles makes a quad. The outlines, seams, darts and lines are kept as edges either way.

The UVs come from the pattern, not the mesh, so changing the topology (or the Mesh Level) never moves a texture. The drape is carried over to the new mesh.

## Sharp seams and folds

UMA gives the copies of a vertex at one position (along a seam, or where two slots join) one shared normal, so the light runs smoothly across. A seam or fold can be marked **Sharp** on the Pattern Canvas instead (the seam's **Sharp**, a fold line's **Sharp**, a pleat's **Sharp Folds**): the baked slot then keeps a normal for each side of it, so it shows as a crisp edge (a welt, a panel line on leather, a pressed pleat's fold). Along a sharp fold inside a piece the slot is split, one copy of each fold vertex for each side; the copies stay at one position with the same weights and UVs, and move as one. The slot remembers its sharp edges (`SlotDataAsset.normalSides`), so normals worked out again later (the Vertex Editor, body-shape blendshapes, the slot inspector) keep them. Measured on the Pleated Skirt with sharp folds and a sharp side seam: 1,152 fold vertices were split, and 1,189 of the slot's 1,252 shared positions kept a normal for each side, on average 157 degrees apart (a pressed fold turns back on itself). Without, every shared position had one normal. The designer's live cloth still shows smooth shading; the bake is where the edges turn sharp.

## Quad slots

**Slot Topology** (in Publish, under Slot Mesh Level) chooses how the baked slot is meshed. The cloth is always simulated on triangles; a quad slot is made from the pattern at bake time, as tools such as Retopo Planes make one from a garment's flat layout, and takes its shape from the drape through the flat pattern.

- **Triangles** (the default): the mesh the cloth was simulated on (or made at the Slot Mesh Level).
- **Quads**: each piece meshed flat with a grid along its grain and a loop of quads along its outline, the triangles paired into quads wherever they make a good one (corners within 55 degrees of square). Measured on the 20 samples at 2 cm: 92 to 99% of the cloth in quads, a few triangles where the grid meets the outline loop.
- **All Quads**: meshed at twice the spacing, then every face split into quads (a quad into four, a triangle into three), so there are no triangles at all at about the same density.

Seams, darts, pleats and lines keep their edges, with a matched point on each side, so seams weld shut and their weights match as with triangles (checked on every sample: no point on a sewn edge is left unsewn). In the slot each quad is two triangles, split along its shorter diagonal on the draped cloth, so a fold across a quad bends along the diagonal that suits it. The bake message says how much is quads and how many inside points are not shared by four quads.

**Quad OBJ for Other Tools** (on by default with quads) also writes `<Name>_Quads.obj` beside the slot: the quads as draped, for Blender and other tools that keep quads. One object per piece, sewn points joined, the pattern's UVs, metres, Y up, X negated as usual for Unity meshes going to right-handed tools (so front faces stay front faces). Unity imports it too. Measured on the T-shirt on Human Female 3.0: 2,132 quads, the bake 1.9 seconds with the quads 0.3 of it.

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
| Fit (`ClothesFit`) | The race and body shape, allowances, damping and clearance, pins, Fit Brush paint, the kept drape, publish settings and what was baked. | No |
| Slot, overlay, texture, wardrobe recipe | Ordinary UMA content. | Yes |
| `<Name>_Quads.obj` | With a quad Slot Topology: the slot's quads as draped, for other tools. | No |
| `<Name>_SmooshPlane` slot | With Holds Hair Down on head clothes: the clipping plane UMA squashes hair under. In the wardrobe recipe. | Yes |

Patterns and fits are saved under `Assets/UMAProjectData/ClothesDesigner` by default, outside the plugin, so they survive removing and reinstalling it. They need the plugin to be opened again; the baked clothes do not.
