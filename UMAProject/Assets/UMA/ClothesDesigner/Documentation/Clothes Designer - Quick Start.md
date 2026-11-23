# Clothes Designer quick start

The Clothes Designer makes clothes for a UMA race from sewing patterns. You draw the flat pieces, sew them, drape them on the race's body, pin and pull them into shape, and bake a slot, an overlay and a wardrobe recipe that any character of that race can wear. The baked clothes are ordinary UMA content and work in a game without this plugin.

## Make your first vest (five minutes)

1. Choose **UMA > Clothes Designer > Create Clothes on Race...**, pick a race (for example *Human Female 3.0*) and press **Create Clothes**. The Vertex Editor opens on that race's default body, standing in **Race Default**, the pose its meshes were modelled in. Nothing needs to be in the scene.
2. In the **Pattern** section of the panel, press **Samples** and choose **Sleeveless Vest**. The front and back are laid round the body. To move a piece first, use the **Place** tool in Cloth Tools: drag the piece round the body or up and down.
3. In the **Fit** section (or on the **Cloth Tools** toolbar in the Scene view), press **Simulate**. The seams close (*Sewing*), then the cloth falls onto the shoulders (*Settling*). The simulation stops by itself when the cloth is still (*Settled*).
4. In **Publish**, give the clothes a **Name**, check the **Material** and **Wardrobe Slot**, and press **Bake and Try On**. A second character of the race appears beside the body, wearing the vest through the normal wardrobe, and the Scene view turns to it (**View Fitting Body** and **View Try-On** switch between the two). Press **Arms Raised** and set **Arm Angle** to check it with the arms lifted; the shoulders lift with them, as in an animation.
5. Press **Save Fit**. The first time you choose where the pattern and the fit are saved.

The baked slot, overlay, UV check texture and wardrobe recipe are in the **Folder** shown in Publish (by default `Assets/UMAProjectData/ClothesDesigner/Output`).

## Start from a sample

**Samples** in the Pattern section lists the sleeveless vest and twenty more: T-Shirt, Long-Sleeve Tee, Tank Top, Crop Top, Polo Shirt, Oxford Shirt, Hoodie, Short Pants, Jeans, Sweatpants, Leggings, Boxer Shorts, Pleated Skirt, A-Line Skirt, Pencil Skirt, Circle Skirt, Sheath Dress, Sundress, Socks and Beanie. Each is drafted for the default *Human Female 3.0* and sized for other bodies by its pattern's sizes (chest, waist, hips, arm, leg and head). On a body far from that one (the default male, for example) tops fit more loosely and skirts sit lower. The same patterns are saved as assets in **Clothes Designer Examples** (`Assets/UMA/ClothesDesignerExamples/Patterns`).

## Start from modelled clothes

**From Clothes** in the Pattern section takes a wardrobe recipe the race can wear and makes a pattern from it: each UV island becomes a piece, at its true size, turned upright and placed round the body part it is worn on. The islands are sewn where they meet, and free edges become openings. Folds modelled into the mesh are measured and taken out of the pieces' size, so the simulation can make its own. The pieces keep the clothes' UVs, so their textures still fit. Press **Convert**; what was found is listed under it.

Game clothes are not cut like sewing patterns: expect slivers, islands that do not line up with real seams, and stretches the converter cannot pair (it says how many). Tidy the result on the Pattern Canvas (sew what is open, join or delete small pieces) before simulating. A pattern made by the Clothes Designer itself converts back to the same pieces and seams.

## Other ways to start

Select a race, a scene character, a slot or a wardrobe recipe and choose **Editors > Plugins > Clothes Designer** above its inspector:

| You selected | What opens |
| --- | --- |
| A race | The designer on that race's default body. |
| A scene character | The designer on the default body of the character's race. The character in your scene is not changed. |
| A clothing slot or wardrobe recipe | **Fit Existing Slot** for that clothing, on a race that wears it (you are asked when the clothing does not say). See *Clothes Designer - Fit Existing Slot*. |

Opening the Vertex Editor in the usual way (**Open Vertex Editor** on a character) never starts in the designer. Inside the Vertex Editor, **Clothes Designer** is the button on the second row of the workflow bar, under Sculpt, Vertex Paint, Advanced and Blendshapes.

## The panel, top to bottom

- **Race, Pose, Body**: the body the clothes are fitted to and its chest, waist and hip measurements (taken round the body with the arms left out).
- **Pattern**: the pattern being fitted. *New* starts a blank one, *Samples* one of the sample patterns, *Pattern Canvas* opens the 2D window to draw, sew, cut and merge pieces, add darts and pleats, and draw lines, folds, holes, tacks, buttons and parts inside pieces (see *Clothes Designer - Pattern Canvas*). *From Clothes* converts modelled clothes (above). A `*` means unsaved changes.
- **Fit**: Simulate / Pause, Step (one frame), Reset (back to the pieces laid out, unsewn), **Keep Drape** (remember this shape in the fit: reopening it restores these folds). The same four are on the Cloth Tools toolbar in the Scene view (play/pause, step, rewind and save icons). The solver line says whether the GPU or the CPU runs the drape; **Use CPU Solver** switches. **Final Quality** uses more substeps. **Show** colours the cloth by its texture, its stretch or its distance from the body. **Fold onto Itself** keeps a piece from passing through itself. **Worn Underneath** puts clothes on the body to drape these over. **Mesh Level** sets how fine the cloth mesh is; the vertex and triangle counts are shown under it.
- **Cloth Tools**: **Place** (click a piece and drag it round the body or up and down; Shift-drag moves it out from the body; its Body Part, Faces, top, Distance, Across, Lay Flat and Freeze are shown to type in), **Grab** (drag the cloth; Escape puts it back; one drag is one Undo step), **Pin** and **Erase Pin**, with a radius and strength, and **Measure** (the distance between two points, and how far round the cloth and the body are). Pins can follow the body or hold a point in space. The same four tools are on the **Cloth Tools** toolbar in the Scene view (move, hand, pin and eraser icons; hover one for what it does), shown while the cloth is ready. Close it from the Scene view's overlay menu if you prefer the panel; it comes back the next time the designer opens.
- **Fit Report**: open seams, cloth inside the body, stretch, and the time per frame.
- **Publish**: name, material, wardrobe slot, folder, *Hide Body Underneath* (a mesh hide for fully covered body triangles), *UV Check Texture*, **Inside Facing** and **Rim** (cloth inside the openings, so one-sided materials do not show through), **Slot Mesh Level** and **Normal Map from Drape** (drape at a fine Mesh Level, bake a lighter slot that keeps the small folds in a normal map), **Full Thickness**, **LOD Slots**, **Buttons and Parts**, **Topstitching**, and **Bake and Try On**. The try-on can play an animation clip.
- **Save and Open**: Save Pattern, Save Pattern As, Save Fit, Save Fit As, Open Fit.

The Vertex Editor window shows more: the body's measurements, **Body Shape (DNA)** to fit on another shape, **Sizes** with what each size works out to on this body and its allowance, **Fabric** (each piece's fabric, plus this fit's damping and clearance) and **Piece Placement**. A piece's fabric and placement are also in the Pattern Canvas's side panel.

## Good to know

- Clothes are weighted on the body exactly as shown. Fit on the race's default shape for clothes every character can wear; for another body shape, change the DNA, drape again and save a fit for that shape.
- Fit at *Draft* or *Medium* and raise the Mesh Level before baking: the drape is carried over to the finer mesh. For small folds on a light slot, drape at a fine level and set a coarser **Slot Mesh Level** with **Normal Map from Drape**.
- The baked clothes are single-sided cloth. **Inside Facing** (on by default) lines the openings so a one-sided material still shows cloth there; or choose a material that draws both faces.
- Baking again updates the same slot, overlay and recipe. Their GUIDs stay, so recipes, libraries and scenes that use them keep working. Textures painted on the overlay and anything added to the recipe are kept; edits made to the slot's mesh are replaced.
- Leaving the Vertex Editor with unsaved work asks whether to save the pattern and fit.

## If something goes wrong

- **"A body for the race could not be built."** The race has no base recipe, or the recipe refers to slots that are not in the UMA Global Library.
- **Some cloth starts inside the body.** The pieces are moved further out automatically; if the message stays, move the piece with the **Place** tool, or raise *Above Shoulders* or *Distance*.
- **A seam twists or gathers.** Open the Pattern Canvas: dotted lines join the sewn ends. If they cross, select the edge and press **Reverse**.
- **"... leaves the texture" or "... overlap in the texture".** The pattern was redrawn larger. In the Pattern Canvas, UVs tab, press **Repack UVs**.
- **The Simulate button runs on the CPU and is slow.** This graphics card cannot run compute shaders, or *Use CPU Solver* is on. Lower the Mesh Level while fitting.
