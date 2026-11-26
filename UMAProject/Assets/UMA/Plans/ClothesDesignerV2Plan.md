# UMA Clothes Designer V2 plan

The next steps after the first release (see `ClothesDesignerPlan.md`), asked for by the maintainer on 2026-11-24 after the review of Simply Cloth Studio 2.2.1 (gaps O4, O12, O14 and O15 in the first plan), plus sharp seams and edges.

Status: items 1 to 6 built and checked by script in the Editor on 2026-11-24 (see Results); they await the maintainer's hands-on validation. Item 7: the full EditMode regression ran on 2026-11-24 (4,892 of 4,923 passed; none of the 25 failures is in V2 code). The PlayMode suites were left out at the maintainer's choice.

## Order of work

| # | Item | From |
| --- | --- | --- |
| 1 | Eight new samples: Bra, Swimsuit, Briefs, Face Mask, Hauberk, Leather Armor, Tunic, Coif | O4 |
| 2 | A sample browser with pictures of the pattern and of the draped result | O4 |
| 3 | Painted stiffness and weight on the Fit Brush | O15 |
| 4 | Cloth from a body region anywhere | O14 |
| 5 | A smoosh plane published with hats and hoods, and the slots set up for it | O12 |
| 6 | Sharp seams and sharp edges | Maintainer, 2026-11-24 |
| 7 | Tests, documents, and a full regression at the end | |

Run-time smooshing for hoods is a separate upgrade: `RealtimeHoodSmooshPlan.md`.

Testing rules for this work (maintainer, 2026-11-24): never stall the Editor; while developing, run only the tests for the code being changed; run a full regression once everything is done.

## 1. New samples

Each is drafted for Human Female 3.0, like the first twenty, and sized for other bodies by its **Sizes**. Each is added to `SamplePatterns.All` with a category, and shipped in `ClothesDesignerExamples/Patterns`.

| Sample | Category | Pieces | Fabric | Shows |
| --- | --- | --- | --- | --- |
| Bra | Underwear and swim | 2 cups (a dart each), 2 band halves, 2 straps | Spandex cups, Elastic band and straps | Small shaped pieces, a band that grips under the bust, straps from the cups over the shoulders to the back band |
| Swimsuit | Underwear and swim | front, back (to the crotch, high-cut legs), crotch seam | Spandex | One piece from the shoulders through the crotch, cut smaller than the body so it grips |
| Briefs | Underwear and swim | front, back, elastic waistband (no separate gusset: the front and back meet under the crotch) | Spandex, Elastic band | Leg openings that follow the leg crease, the back bowed out over the seat |
| Face Mask | Accessories | 2 front halves (a curved centre seam over the nose and chin), a band round the back of the head | Cotton, Elastic band | A shaped mask on the face, held by a band round the head |
| Hauberk | Medieval | front, back (to the knee, slit front and back from the hem to the crotch), 2 long sleeves | Mail (new preset) | Very heavy, limp cloth hanging from the shoulders |
| Leather Armor | Medieval | front, back (to the hip), 2 shoulder guards, 4 hanging tassets | Leather, firm interfacing on the guards | Stiff pieces; tassets hanging from the hem on short seams |
| Tunic | Medieval | front, back (to the knee, flaring), 2 long sleeves | Wool | A medieval T-tunic with a deep V neck slit and flare from the hips |
| Coif | Medieval | 2 halves sewn over the crown from forehead to nape | Cotton | A close cap covering the ears, framing the face, held by the head's shape |

A **Mail** fabric preset is added for the hauberk: no stretch, very limp (bends like silk), very heavy (4 kg per m², real riveted mail is 6 to 8), with low friction.

**Accepted when** each new sample drapes on Human Female 3.0 (and on Human Male 3.0 for the unisex ones) with no cloth inside the body after 400 frames, every seam closed within 3 mm and every opening where it was drafted. The measurements go in the results below.

## 2. Sample browser

**Samples** in the Pattern section opens a browser window instead of a menu.

- A grid of tiles, one per sample. Each tile shows two pictures: the pattern (the pieces filled in their own colours, with outlines) and the drape on a grey body. Under them: the name, piece count, fabrics, and the description as a tooltip.
- Categories along the top (All, Tops, Bottoms, Dresses and skirts, Underwear and swim, Accessories, Medieval), and a search field.
- Clicking a tile selects it; **Load** (or a double-click) loads it into the open designer: the same unsaved-work question as before, then the view frames the cloth.
- The pattern pictures are drawn when the browser opens (a few milliseconds each) and cached.
- The drape pictures:
  - Shipped pictures, rendered on Human Female 3.0, are in `ClothesDesignerExamples/Previews`, so the browser shows them at once.
  - **Render on This Body** drapes every sample on the open designer's body and keeps the pictures in `Library/UMAClothesDesigner/SamplePreviews/<race>/`. The browser then shows those for that race.
  - Rendering runs in the background, a few simulation frames per Editor update, within a budget of about 10 ms per update, so the Editor never stalls. Tiles fill in as each finishes.
  - Pictures are rendered with `PreviewRenderUtility`: the body as a grey mesh made from the measured body surface, and the drape with the designer's cloth material. Nothing is added to the stage or to the maintainer's scenes.

**Accepted when** the browser opens in under 200 ms with all pictures cached, and rendering all 28 samples in the background never holds the Editor for more than 50 ms at a time.

## 3. Painted stiffness and weight (O15)

Two more Fit Brush modes, beside Hug, Smooth, Puff, Slim and Erase:

- **Stiffen** makes the cloth firmer where painted: both the stretch and the bend compliance of every constraint between painted points are multiplied by `10^(-2 s)`, so full paint is 100 times stiffer. Use it for a firm cuff or collar band without a separate interfaced piece.
- **Weight** makes the cloth heavier where painted: the point's mass is multiplied by `1 + 3 w`, up to four times. Use it for a weighted hem or the hang of a heavy edge.
- Both are kept in the fit with the other paint (`FitPaintPoint.stiff`, `FitPaintPoint.weight`), on the flat pattern, so they survive a new mesh level.
- Both are applied when the cloth model is built (constraint compliance and inverse mass are already per constraint and per point), so neither solver changes. A stroke rebuilds the model as Hug does.
- Painted colours: stiffen in teal, weight in brown. Erase clears every channel.

**Accepted when** a stiffened cuff band keeps its tube shape against an unpainted one (fold angle), a weighted hem hangs lower and sways less after a nudge, and the GPU and CPU solvers agree before contact.

## 4. Cloth from a body region anywhere (O14)

A **Body Region** cloth tool beside Draw Piece.

- **Painting.** Brush the region onto the body. Body triangles under the brush are added; Shift (or Erase) removes them. Mirror across the body's middle is on by default. The region is drawn on the body as a tint.
- **Make Pieces** turns the region into pattern pieces:
  1. **Split by body part:** each triangle goes to the head, torso, either arm or either leg by its vertices' bone weights. Where two parts meet becomes a seam.
  2. **Cut tubes open:** a part of the region that goes all the way round (a sleeve, a leg, a waist band) is cut along a seam line: the centre back on the torso and head, under the arm, inside the leg. The cut's two sides are sewn together.
  3. **Flatten** each part, offset out from the body by the clearance, with ARAP (as-rigid-as-possible: a cotangent Laplacian solved by conjugate gradients, started from a harmonic map with the boundary on a circle). The area distortion is measured.
  4. **Split into gores** where flattening stretches the cloth more than 6%: the part is cut into 2 to 6 sectors round its axis, sewn together (a skull cap becomes gores like the beanie, a bodice front gets princess-like panels).
  5. The flat outline is simplified to pattern points (edges kept within 2 mm of the flattened boundary). Edges where the region ended are openings; edges where parts, gores or cuts meet are sewn.
  6. **Ease:** a slider from -15% (cut smaller, for stretch fabrics) to +20% scales each piece.
  7. **Layout:** each piece is placed round its part (angle and height from where it was painted), like the samples, so the pattern works on other bodies too.
- New pieces are ordinary pattern data, edited on the Pattern Canvas.

**Accepted on Human Female 3.0 when:**

| Region | Expected result |
| --- | --- |
| A sleeve | One piece with its own seam, under 6% distortion |
| A skull cap | Gores |
| A bodysuit (torso, arms and legs) | One or more pieces per part, every seam's two sides within 2% of each other in length |

Each drapes with no cloth in the body.

## 5. Smoosh plane and slot setup (O12)

UMA squashes hair under a hat at build time (`DynamicCharacterAvatar.SmooshSlotPhysics`). This needs three things:

- a clipping-plane slot: four vertices, `isClippingPlane`;
- the hat slot tagged `Smooshtarget`;
- hair slots marked `isSmooshable` and tagged `Smooshable`.

Hair points on the far side of the plane are moved towards the plane's centre until they are inside the hat's mesh.

- **Publish** gets **Holds Hair Down**, on by default for garments laid on the head (beanie, coif, hood):
  - The bake makes `<Slot>_SmooshPlane`, a quad on the plane of the opening round the face and neck. The plane is fitted through the opening's points by least squares; the quad covers the opening plus 2 cm. It is in the slot's bind space, with `isClippingPlane` set.
  - The bake adds the `Smooshtarget` tag to the garment slot, and adds the plane slot to the wardrobe recipe with the smoosh distance and over-smoosh defaults.
- The designer's Publish panel lists the project's hair slots that are not smooshable, with a button that marks them (asks first, saves only those slots).
- **Accepted when** a character wearing the beanie and a smooshable hair keeps every hair vertex inside the beanie that was outside it before (counted), and a hood does the same.

The run-time upgrade, for hoods that move as cloth and hair that moves, is `RealtimeHoodSmooshPlan.md`.

## 6. Sharp seams and sharp edges

UMA's rule is that vertices at one position ("battle buddies") get one shared normal. The maintainer asked on 2026-11-24 for an exception: an edge marked sharp keeps a normal for each side. Its vertices still stay at one position and move as one.

- **Pattern data:**
  - `PatternSeam.sharp`, for a crisp seam (a welt, a panel line on leather armour).
  - `PatternLine.sharp`, for a crease or a panel line inside a piece.
  - `PatternPleat.sharpFolds`, for the folds of pressed pleats.
- **Canvas:** a Sharp toggle in the seam, line and pleat properties. Sharp edges are drawn with a short double tick so they can be told apart.
- **Mesh:**
  - Seams already have a vertex copy on each piece.
  - Along a sharp line or fold inside a piece, the bake splits the vertices: one copy for each side, at the same position, with the same weights and UVs.
  - Every vertex gets a "normal side" id; buddies with different ids get their own normals.
- **Core:**
  - `UMABuddyNormals` gets optional per-vertex side ids. Buddies stay one position group (moved and tracked as one) but each side gets the normal of its own faces.
  - The slot keeps the side ids (a new `SlotDataAsset` field, empty for every existing slot), so the Vertex Editor, refits, body-shape blendshapes and the slot inspector keep sharp edges sharp.
  - `AGENTS.md`'s battle-buddy rule gets this exception written in.
- **Preview:** the designer's live cloth shows sharp edges too.
- **Accepted when:**
  - A pleated skirt with sharp folds has, at each fold, two normals that differ by the fold's angle, with both copies at one position.
  - A sculpt stroke in the Vertex Editor keeps them sharp.
  - Every existing slot's normals are unchanged (no side ids means one normal, as before).
  - `UMABuddyNormalsTests` covers both cases.

## 7. Tests, documents and regression

- New tests per item: the samples drape (fast checks on the capsule body; full drapes in the Editor); the browser's pattern pictures and preview cache; paint stiffness and weight on the model and in a drape; region splitting, cutting, flattening and gores on the capsule; the smoosh plane's fit and recipe setup; `UMABuddyNormals` side ids, and a sharp bake.
- Documents: the Clothes Designer pages (samples, browser, Fit Brush, Body Region, Publish), the Examples README, `CHANGELOG.md`.
- A full regression once all items are done: every test assembly, one fixture per asynchronous run, in the background.

## Defaults the work proceeds on

| Decision | Default |
| --- | --- |
| "Cap" | A coif, the medieval linen cap |
| Mail weight | 4 kg per m² |
| Shipped preview body | Human Female 3.0 |
| Where sharp edges live on a slot | A per-vertex side id array on `SlotDataAsset`, empty when no edge is sharp |
| Body Region distortion before splitting into gores | 6% |

## Results (2026-11-24)

Checked by script in the open Editor (Unity 6000.3.26f1, UMA 3.1.6), on Human Female 3.0 unless said otherwise.

**1. Samples.** All eight drape with no cloth in the body after 400 frames; seams close within 0.6 mm, except the set-in sleeves' underarm corner (Hauberk 2.9 mm, Tunic 3.2 mm, the same as the T-Shirt's 2.6 and Long-Sleeve Tee's 3.1 mm; the bake welds it). The coif needed a chin strap: without one it rode back onto the crown. On Human Male 3.0 the Briefs, Face Mask, Leather Armor and Coif drape cleanly; the Hauberk and Tunic leave 4 to 8 points under the arms, as the T-Shirt and Hoodie do (his arms touch his sides in the modelled pose). Shipped in `ClothesDesignerExamples/Patterns`; the **Mail** preset is 4 kg per m², limp, no stretch.

**2. Sample browser.** Opens in 19 to 31 ms; a repaint takes at most 80 ms while the pattern pictures are drawn (a few per repaint), then less. 29 drape pictures ship in `ClothesDesignerExamples/Previews` (rendered on Human Female 3.0 with `PreviewRenderUtility`, from the front, or from behind when most of the cloth faces back: the coif). **Render on This Body**: pictures are draped one mesh level coarser than the pattern's own (pleated patterns at their own level) with quick UVs; building a drape is split into three parts done on three Editor updates, and frames are queued four at a time, 80 ms apart. Measured: the longest update was 0.23 s (taking the picture), the heaviest build part about 0.25 s (laying out the Oxford Shirt), so the plan's 50 ms was not met for those two steps. Small head pieces cost about 10 ms of GPU time a frame (the dense head mesh), which is why the frames are spaced.

**3. Painted stiffness and weight.** Stiffen ties painted points to the painted points two edges away in every direction (interfacing only ties up and down a piece; tied that way, a stiffened skirt kept its folds). Whole silk Sundress stiffened: hem 5.1 cm higher, 1.5 cm wider (holds an A-line); its hem band only: fold spread 3.85 to 4.63 cm (fewer, larger folds). Cotton barely changes: its bending is already at the floor that keeps fine meshes stable. Weight on the Sundress's lowest 15 cm (96 to 154 g): the hem hangs 4.8 cm lower in straighter folds. A weighted hem pulled the friction-held Circle Skirt off the hips.

**4. Body Region.** Measured from regions picked on the body by script: a sleeve on the upper arm, one piece at 4.3% stretch; a skull cap, six gores at 3 to 5%; a short unitard (torso, the tops of the arms and legs), ten pieces (the torso in six gores at 7 to 11%, the rest under 6%), draped with every seam within 0.7 mm, no cloth in the body, 1.9% mean stretch. Through the tool itself (painting a band round the upper arm, mirrored): two sleeves at 3% stretch, laid about 1 cm off the arms, draped with closed seams. What made it work:
- Pieces are laid exactly where they were painted (`BodyLayout` on the piece, on the race it was painted on); laid round their part instead, the unitard's torso gores could not reach between the legs (63 mm seam gaps, 20 points in the body).
- Gore edges follow the body's triangles, so they are smoothed and simplified (within 0.5 cm); ragged gores are tidied (small scraps join the gore round them), and a wedge that catches two separate stretches makes two pieces.
- The painted region is tidied (notches and pinholes filled, loose triangles dropped), points where triangles meet at only a corner are split, and small separate scraps of a part are left out (the panel says how many).
Left: the unitard's front gores still stretch 11% over the bust (darts would take that up).

**5. Holds Hair Down.** The Beanie bakes with `<slot>_SmooshPlane` (a clipping plane across the brim, normal into the hat, no flipping in the recipe), the slot tagged `Smooshtarget`, and the plane in the wardrobe recipe. With UMA's own `SmooshSlotPhysics`, the hair points outside the beanie fell from 1,116 to 86, 2,083 to 141 and 1,699 to 130 on three hair slots. 59 hair slots are in the Global Library; 5 were smooshable. **Check Hair...** lists the rest and offers to fix them. Run-time smooshing: `RealtimeHoodSmooshPlan.md`.

**6. Sharp seams and edges.** Pleated Skirt baked with sharp folds and a sharp side seam: 1,152 fold vertices split; 1,189 of 1,252 shared positions kept a normal for each side (mean 157 degrees apart: pressed folds turn back on themselves). Baked without, all 194 shared positions had one normal. Core: `UMABuddyNormals.Sides`, `SlotDataAsset.normalSides` (copied with the slot), `SlotMeshOptions.NormalSides`; the Vertex Editor, the slot inspector's Recalculate Normals, slot processing and the designer's body-shape refit pass a slot's sides. `AGENTS.md`'s battle-buddy rule has the exception written in. Left: the designer's live cloth is still shaded smooth (the baked slot is sharp).

**7. Tests.** New: `DesignerV2Tests` (13: samples, pictures, stiffness and weight, unrolling a tube, a dome and a flat patch, body layout, the hair plane, sharp folds and seams) and three sharp-edge cases in `UMABuddyNormalsTests`; `SamplePatternsTests` and `ClothesDesignerWorkflowTests` updated for 28 samples and 11 tools. Full regression: see below.

**Full regression (2026-11-24, 15:01 to 15:38).** Every EditMode fixture of the 7 EditMode test assemblies (283 fixtures, 4,923 tests), run inside the open Editor from `EditorApplication.update` without the Test Runner (starting it could have written the maintainer's unsaved scene and assets). Each test had NUnit's own test context and the Test Framework's log check. `[UnityTest]` coroutines ran one step per Editor update, and `async` tests were polled. **4,892 passed, 25 failed, 6 skipped.** All 27 Clothes Designer fixtures passed (225 tests), as did `UMABuddyNormalsTests` (12) and the Vertex Editor suites. None of the 25 failures is in code changed for V2:
- 5 (ModelToRace, IconCreatorSpriteAtlas, SlotBuilderSettings, SlotProcessingUtil blend-shape prefix, UMA 2 legacy recipe hook): Unity logged "Cannot open file … .meta for write" for a file the test had just created; another process holds new files for a moment (the log has the same error earlier in the day and a sharing violation on `Assembly-CSharp.csproj`). Re-run alone, four of those fixtures passed and a different one (ShaderPackage) hit it; the same hold left some test folders behind, which were moved to the Recycle Bin.
- 1 (`ModelToRaceProjectMenuTests`): the test calls `Assets/UMA/Model To Race/Clothing...`; commit 0980fa707 renamed the menu to `Model To Race && Clothing...`.
- 3 (`GeneratorQualityTests`): the generator queue holds 3 (or 1) more characters than expected, also when the fixture runs alone. Its helper makes the test generator the library's global one, so characters outside the test can join the queue. Not yet checked in the Test Runner's empty scene.
- 1 (`UMAGpuTextureCompressionTests`): the project's generator has the GPU compression switch on.
- 5 (`UMAMatureFaceContentTests`): content. The recipe's tags lack "Head", and the mask edge differs by 0.55 and 1.38 levels (limit 0.25).
- 1 (`UMAPackageReadinessTests`): `SRP/HDRPSetup/UMAHDRPSetup.cs` compiles into Assembly-CSharp.
- 9 (`UMARaceSmokeTests`, 8 `UMA2CompatibilityTests` races): UMA 2 overlay assets whose `textureNames`/`overlayBlend` lengths do not match `textureList`.

Skipped: 5 tests that enter play mode, and 1 that only runs inside a Test Runner run. Not run: the PlayMode assemblies (`UMA_Core_Runtime_Tests`, `UMA.TexturePaint.Tests`), which need the Test Runner. Afterwards `AssetIndexer.asset`, the open scene, its Baking Set and `EditorBuildSettings` were byte-identical to their backups. The longest single tests were 82 s (UMA 2 companion archives), 75 s (package builder), about 40 s (Hair Card buns), 31 s (`DartsAndPleatsTests.PleatLine_StopsWhere…`, which walks all 28 samples) and 22 s (`SamplePatternsTests.SaveAll…`).
