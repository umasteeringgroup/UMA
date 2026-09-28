# Braided bun: painted roots, gather ring and wrapped bun

Open **UMA → Hair Cards → Examples → Braided Bun → Classic**, **Loose**, or **Compact Copper**. Editable grooms, three-LOD preview prefabs, thumbnails and a neutral URP review scene are in `Assets/UMAProjectData/HairCards/Examples/BraidedBun`.

Pointy Swept, Curly Volume and Short Hair Part retain their existing settings and resources.

## Start here

1. Select **02 · Gathered sweep → Growth / Density** in Hair Nodes. Paint the hairline, temples, area above the ears and nape. **Expanding the painting now generates new scalp roots there.** The saved map uses Texture storage, at 32 texels per face edge.
2. Select **Shared Helpers → Gather ring - scalp to bun**. Move it to the tie position. Rotate its Y arrow to set the arrival/exit direction. Keep the entire ring outside the scalp.
3. Select the sweep population's **Gather to bun - dual bend** modifier. Adjust surface offset, tension and the two bend profiles. These samples explicitly use **Extend To Reach**, so procedural strands can reach the tie.
4. Select **Shared Helpers → Bun volume, tuck and braid** to shape the outer bun and recessed center. It follows the gather ring.
5. Select **Shared Helpers → Braid spline - bun surround** to move or reshape only the braid. Drag its cyan spline directly, or use the move gizmo in **Whole spline** mode. Click a dot for **Control point** editing. The spline follows the bun but remains independently editable; lowering it does not lower the bun.
6. Check front, both sides, rear and lower LODs. **Save** stores the authoring setup; **Validate & Bake** creates output. The floating toolbar's **Exit** leaves grooming.

The foundation's Growth node has an arrow to **Gathered sweep**. Selecting it opens the real shared painting, not a second map that has no effect. Optional styling maps and materials remain independent per group.

## What drives the style

| Group | Source | Purpose |
| --- | --- | --- |
| 01 · Scalp foundation | Painted Scalp + Gather | Close-fitting darker coverage |
| 02 · Gathered sweep | Painted Scalp + Gather | Visible scalp hair flowing to the tie |
| 03 · Full wrapped bun | Bun / Wrapped Volume | Rounded outer hair wrapping inward |
| 04 · Bun center tuck | Bun / Center Tuck | Recessed center coverage |
| 05 · Surrounding braid | Braid / editable spline | Three-bundle plait around the base |
| 06 · Fine flyaways | Individual wisp grids | Sparse thin stray hairs |

The main style has three shared controls: a Gather ring, a Bun volume and a separate Braid spline. Wisp grids stay independently editable. The bun is wrapped hair volume, not a second braided coil.

Classic has a full brown bun and 28 flyaways. Loose has a 12% larger bun, more relaxed scalp lift, warmer color and 40 flyaways. Compact Copper has a smaller bun and 20 flyaways. Each owns its groom, profiles, atlases and materials; only source textures are shared.

The rebuild retains the saved Classic hairline painting rather than repainting an analytic boundary. The other two variations start with that same painted boundary.

## Gather controls

On a sculpt pass or generated population, use the tree's **+ Gather Helper & Modifier** action. It creates a ring and bound modifier as one undoable setup. Existing authored guides can use Gather too; Painted Scalp is optional.

- **Gather target:** the shared ring. Local X/Z contains the packed endpoints; local Y sets the arrival and tail direction.
- **Gather mode:** *Tips* gathers strand ends. *Pass Through* places a tie partway along the strand and continues a tail beyond it.
- **Length behavior:** *Preserve* keeps incoming segment lengths. Unreachable targets produce warnings, not silent stretching. *Extend To Reach* explicitly permits longer hair; it does not automatically shorten excess hair. Start with short initial strands for procedural updos. Existing long guides may need Length/Cut first.
- **Tension:** high values hug the scalp route; lower values add relaxed lift. This is geometric shaping, not physics.
- **Lift-off along scalp path:** how long hair follows the surface before entering the bundle. It is an upper limit: a strand leaves earlier if following farther would overshoot its assigned slot and produce a U-turn.
- **Surface offset / Offset along strand:** source-local clearance and its variation along the route.

### Two bends for an S-shaped strand

Expand **Root & arrival bends (S-curve)**:

- **Protected root reach** leaves a prefix unchanged, measured as a fraction of incoming length or in meters. Root position is always anchored.
- **Root bend height / Root bend profile** control the first bend over the scalp. The profile spans the scalp-following section.
- **Arrival tangent reach** controls how far the approach tangent extends back from the ring.
- **Arrival bend height / Arrival bend profile** control the second bend from lift-off to the ring. Arrival height is signed, so the bend can reverse direction. Clearance can limit inward bends.
- **Flexibility** controls how strongly Gather may bend stiff controls. Fully frozen controls remain pinned. Blend weight, Root Influence, root-to-tip influence and modifier masks still apply.

Both profiles have endpoint-zero envelopes: adjusting a bend does not translate the root or tie. They produce an S-shaped transition without manually editing every strand. Available length and pinned controls still constrain possible shapes.

### Facing and clearance

**Follow connected scalp** routes around the source rather than taking a chord through the head. A welded connectivity graph handles UV seams; continuous surface following avoids locking normal paths onto mesh edges. Disconnected roots with no route to the target surface are reported and left unchanged.

**Face cards outward** conforms orientation on the scalp and transports it into the bundle. **Root → tie twist** adds intentional roll. **Prevent card-edge penetration** also checks the finished card width and faces, not only its centerline. This is scalp collision, not hair-to-hair self-collision.

Saved points/transforms use source-mesh local coordinates. Scene gizmos transform into the displayed character space. Moving or rotating the character must not change its authored hairstyle; source-local meter values scale with the source object.

Select a Gather helper to see its ellipse, Y arrow and a limited set of strand paths. Protected root prefixes are thicker. Green paths reach the ring; orange paths indicate a miss. Warnings report unreachable strands, insufficient spacing and disconnected routes. Check **Issues** before baking.

### Spacing, ponytails and pigtails

On the ring, set **Bundle radii X / Z**, **Minimum endpoint spacing**, and **Endpoint variation**. Slots pack deterministically. If the requested spacing cannot fit, enlarge the ring or reduce spacing/count. Spacing concerns centerline endpoints, not physical card-width collision.

For a ponytail, choose *Pass Through*, set **Tie position along strand**, and optionally assign a **Tail continuation** Curve Rail. Its first point is translated to each packed tie position. With no rail, the tail follows the Y arrow. Edit the rail under Shared Helpers; it is not simulated gravity.

For pigtails, use **Duplicate Mirrored Helper**, then bind a second Gather modifier to the copy. Set one modifier's **Roots on symmetry side** to *Positive* and the other's to *Negative*, relative to the groom's source-local symmetry plane. Use painted modifier masks for non-symmetric sections. Copies are independent, not live-linked. Mirroring a Bun detaches its old Gather parent; assign the mirrored parent explicitly.

## Creating another bun

Create a group, enable its generated population, and choose **Generate from → Painted Scalp**. Paint Growth / Density, set count/initial length and add Gather. No authored guides are required for this source. Additional coverage populations can select the same **Growth map group**.

Create a **Bun** helper under Shared Helpers. Bind a **Generate from → Bun** population to it. Add separate populations/groups sharing that helper for **Wrapped Volume**, **Center Tuck**, and **Surrounding Braid**, with independent materials and budgets. Select the Bun helper and use the tree's **Make Surrounding Braid Spline Editable** action to convert its procedural plait into a separate spline without altering the wrapped bun/tuck. This is undoable. The supplied samples have already been converted.

The helper controls:

- **Follow gather target / Offset from gather:** attach to the tie. Rotation becomes relative to the ring. With no parent, source-local position is used.
- **Bun ring radius, Wrapped volume thickness, Bun height:** overall volume.
- **Center tuck:** how far the recessed inner layer wraps inward.
- **Wrap sweep / Surface irregularity:** circumferential flow and departure from perfect symmetry.
- **Braid radius, height and overlap:** placement of a procedural surrounding ring, or the starting preset when extracting its spline. Once a separate spline exists these do not reshape it: edit that spline instead. Plait width/depth, bundle thickness, repeats, phase and weave direction remain on the braid population.
- **Wrap sectors:** procedural forming surfaces, not directly the rendered triangle count.

Grid Panels and standalone Braid Rails remain available. Their **Use painted root density** option only filters existing form roots; it cannot add positions. Use Painted Scalp when painting should expand the root region. Grid controls, soft movement, resampling, reverse, mirrored copies and rail-to-coil forming remain supported. Destructive operations use popup confirmations.

## Editable braid splines anywhere

Select an unlocked group in the tree, then use **+ Braid Group** to create a braid with an editable spline and its material/geometry population. Or create **+ Braid Spline** under Shared Helpers and bind it to an existing Braid population. Standalone Curve Rails retain their previous workflow; the new attachment UI is on Braid splines.

- **Whole spline:** drag the cyan line directly in the camera plane, or use the translation gizmo for axis-constrained movement. One line-drag is one Undo action; Escape cancels that drag. The spline moves immediately; expensive card rebuilding waits for line-drag release.
- **Control point:** click a dot, then move its gizmo. **Soft radius** moves nearby controls. Insert/remove points and reverse direction using the tree actions. Reversing also swaps root/tip attachment settings.
- **Follow helper:** optionally parent the spline to a Bun, Gather, attachment or other non-rail helper. Binding/unbinding retains the current freeform shape. It follows changes in the parent's position, rotation and scale, but can still be moved independently. It does not automatically resize when the parent's procedural radius/height parameters change.
- **Freeform:** leave spine snapping off and both endpoints Free. All control points are unrestricted.
- **Snap entire spine to surface:** project the interpolated path onto the scalp, not just its control points. **Spine offset (m)** follows smooth surface normals. Offset describes the center spine; allow additional room for braid thickness. This is not collision simulation for the complete braid/card volume.
- **Root / start attachment** and **Tip / end attachment:** independently choose Free, Surface or Helper. Surface uses its own endpoint offset. Helper uses a target and an offset in that helper's local coordinates. **Attachment blend reach** controls how far the endpoint displacement blends into the freeform controls.
- **Surface mesh override:** empty means the groom's source scalp. A different readable Mesh can be assigned with its source-local position, rotation and scale. This affects placement only; it does not replace the groom's authoring/weight-copy mesh. Negative scale is supported; zero scale is invalid. The snapshot transform is explicit, not a live reference to a scene MeshFilter.

Endpoint attachments take priority over spine projection; their blend reach creates a transition to an off-scalp helper. Whole-spline moves retain attached endpoints. Moving an endpoint attached to a helper changes its attachment offset. A surface-attached endpoint slides on the surface, with its normal offset controlled numerically.

For a ponytail braid, attach the root to a Gather ring and leave the tip free. For a scalp braid, enable spine snapping and choose an offset that accommodates its half-width/depth. For a braid between two controls, create **+ Attachment** helpers and bind each endpoint. **Bind Scene Object as Attachment** links a real scene object; the stage refreshes its saved source-relative transform as it moves. The saved snapshot remains usable when that scene object is absent. This authoring linkage is not a runtime animation rig.

**Duplicate Mirrored Form** copies the evaluated braid controls across the groom symmetry plane. The copy is freeform and detached: assign its own parent/endpoints if desired. Missing targets/surfaces produce validation messages instead of corrupting saved control points. The two spline ends are distinct, including on a wrapping bun braid; the small overlap hides its seam rather than forcing a cyclic endpoint constraint.

## Hairline, shading and flyaways

Growth / Density is 0 for no growth and 1 for full density. Intermediate paint thins candidates; count and spacing also limit coverage. Painting updates procedural roots after a stroke but never overwrites authored guides. **Paint storage → Texture** preserves the old vertex field; repaint or smooth to add detail inside triangles. Source topology, UVs and skin weights are unchanged.

Samples use Hybrid rendering for sweep/bun/braid, cutout for foundation and Alpha Blended for wisps. Tune primary/secondary specular strength and roughness for a less shiny finish. Color controls remain under Materials & UVs and in the material Inspector.

### Adjust pinched card ends

Select **02 · Gathered sweep → 5 · Hair Cards → Geometry & Vertex Colors**.
**Width along card** appears directly below Root Width and Tip Width. Click the
curve to edit its keys. Time 0 is the root and time 1 is the tip; a value of 1 uses
Root Width, and a value of 0 uses Tip Width. Intermediate values blend between
the two widths, so equal root/tip widths give a ribbon regardless of this curve.

For a full-width start, set the first key to **Time 0, Value 1**. A first key below
1 intentionally narrows the root. Closely spaced keys can create an abrupt pinch
when the card has too few segments: spread them apart or increase Profile samples
(which costs triangles). The UI shows effective endpoint widths and a warning for
transitions shorter than one segment. These controls also appear when selecting
the Card Profile asset in the Project window. Profiles may be shared; use
**Make profile unique** before changing only one group. No saved curve is reset
automatically.

### Soften roots without narrowing geometry

Select **02 · Gathered sweep → 5 · Hair Cards → Materials & UVs**:

1. Under **Strand rendering**, choose **Hybrid** or **Alpha Blended**. Plain Cutout
   clips at a threshold and cannot produce continuous soft transparency without
   dithering.
2. For an existing Hybrid pair, click **Sync fringe from first pass** once. This
   saves the core/fringe rendering setup for exported hair while retaining your
   colors and textures. The stage corrects its private preview copies already;
   validation warns if the shared materials still need this step. Inline color
   edits also sync the fringe automatically.
3. Under **Hair color**, lower the alpha channel of **Roots (RGBA)**, leaving
   **Tips (RGBA)** at the desired final opacity. **Root color reach** controls how
   far that root color and opacity blend extends. Alpha is now honored even when
   the vertex-color gradient is disabled.
4. **Root opacity fade** additionally fades from zero opacity over the first
   fraction of the strand. It uses strand position, not flipped atlas UVs, and
   does not consume vertex alpha. Start with a small reach and inspect close up.

Hybrid keeps the opaque core out of partially transparent root/tip regions so
its depth-writing pass cannot turn the soft fade into an opaque edge. Transparent
overlap can still have sorting artifacts. **Alpha density** strengthens atlas
fibers without raising your selected root/tip opacity. Material edits affect
every group using that material pair; Save retains the resource edits.

Flyaways use **Single fine wisp** (U 0.308–0.342, V 0.12–0.985), about 1.2 mm wide tapering to 0.15 mm. Each Wisp grid produces one card. Increasing cards per helper produces tufts rather than isolated hairs.

Scalp vertex shading uses the existing body's mesh-modifier workflow, not an extra cap. Static review prefabs are not rigged wearables. Attach the intended avatar/race in Source & Setup before weight transfer and wearable baking.

## Polygon budget and checks

Painted Scalp separates **Shape points per strand** from rendered **Profile samples / LOD**. Bun populations use **Length segments / card**. For one-sided ribbons: maximum triangles = cards × length segments × width spans × 2.

Adaptive form sampling concentrates segments at bends. Start around 12 segments per braid repeat; reduce density before sharply lowering crossing detail. Bun **Preserve coverage at LODs** distributes candidates and widens survivors. Painted Scalp also offers this option, widening survivors by up to 2x without moving roots, paths or gather slots. It is enabled on both sample scalp layers to avoid low-LOD gaps. Zero density removes all cards.

Surface routes and mesh buffers cache editing work. Moving the target or changing source geometry invalidates affected routes. The first full build costs more than reevaluation. See [validation results](QA/BraidedBunValidation.md) for counts, timing and test coverage.

Inspect scalp exposure, ear clearance, center coverage and silhouette from several angles at every LOD. Tight braid bends can self-intersect; reduce thickness or open the bend. Gather/Bun are deterministic geometry tools, not cloth/hair simulation or a general volumetric cage. Save a copy before major style changes; Undo covers helper and property edits.
