# Short Hair Part — a side-parted style under 20,000 triangles

The example uses **Texture map (precise)** at **32 texels per face edge** for each
group's Growth / Density. Its crown and short-side footprints were rebuilt from
smooth surface regions, not enlarged from the old vertex mask. The source body,
39 authored guides and their root anchors are unchanged.

Select Growth / Density to inspect or repaint the true texture overlay. Zoom into
the temple, sideburns, part and nape: the painted edge can cross inside a source
polygon. Use a small hard brush for a clean edge, or lower hardness for a feathered
transition. Surface-generated clumps/cards update at the end of the stroke.
The full texture workflow, memory tradeoffs and conversion behavior are documented
in [Precise texture painting](README.md#precise-texture-painting).

## Open and explore

Choose **UMA → Hair Cards → Examples → Short Hair Part → Classic**.
The example is in `Assets/UMAProjectData/HairCards/Examples/ShortHairPart`.
Relaxed, Close Cut, Natural and Front Flip Natural are separate editable grooms, not destructive switches on
Classic. PointySwept and CurlyVolume remain separate and unchanged.

The reference style uses a raised, swept crown, a clean side part, a shorter
side panel, and a tapered rear. Its converted mesh has 5,351 four-segment ribbons
(42,808 triangles). This reconstruction retains the 39 authored centerlines and
uses fewer, wider ribbons with the same four-segment length resolution. Fine
fibers come from the alpha atlas, not individual geometry strands.

Two colored groups keep the part intact:

- **Crown and sweep:** 28 guides, including the crown, opposite side and rear.
- **Part-side taper:** 11 guides controlling the short side of the part.

Select a group to isolate its growth and guides. Under **Generate Hair**, inspect
**Soft direction clumps** to see the intermediate flow, then return to **Final
Hair** to see the cards. The groups never interpolate across each other's guides.

## Edit the shape

1. Start with Classic. Duplicate the groom and make its profile/material unique
   before making an independently shaded or differently tessellated version.
2. Select the appropriate group's sculpt pass to comb its guides. Preserve the
   separate flow directions along the part. Use the existing upstream layer edit
   workflow if modifiers are downstream; do not sculpt final modified geometry
   back into an earlier pass.
3. Select **Short hair part cards**. Adjust **Follow parent clump** for broader
   locks, **Clump separation** to keep them open, and **Split tips** to loosen the
   ends. Under **Natural variation & flyaways**, tune length, width, tilt and seed.
4. Use **Length finish** on the generated cards for a nondestructive trim. Classic
   and Relaxed use 1; Close Cut uses 0.88. This leaves the authored guides intact.
5. In **Preview & Visibility**, use Cards / Full and turn off guide/child splines
   and wireframe when judging coverage. Check both sides, the crown and rear.

Relaxed keeps the same footprint with less clumping, more tilt/length variation,
subtle extra noise and more flyaways. Close Cut shortens the generated silhouette
by 12%. The three examples own separate grooms, profiles and materials; only their
texture copies inside this sample folder are shared.

## Control the polygon budget

The figures in this section describe Classic / Relaxed / Close Cut. The Natural
variants redistribute the same budget to more segments on the longer crown;
see the following section for their exact setup.

Select **Geometry & Vertex Colors → Profile samples**. Five points make **four
length segments**, not five. These profiles have one span across their width,
no duplicate backfaces, and no adaptive sampling. The two-sided shader draws the
back of the same ribbon without duplicating triangles.

For these profiles:

`triangles = actual card count × (profile samples − 1) × 2`

So 2,500 cards at five points is 20,000 triangles. The generation panel shows the
active-LOD sampling ceiling per card and per group. Add both groups when budgeting the
whole hairstyle. Extra width spans or duplicate backfaces multiply the cost.

The sample requests 2,000 crown cards and 500 side cards. Growth, spacing and the
hairline can reduce the actual population. The maximum is 20,000 unique triangles
at five profile points. The saved bake settings include a 20,000-triangle warning budget.
This is **not an automatic decimator or hard clamp**: increasing painted density,
requested cards, profile samples or width spans can exceed it. Check validation
and rebuilt counts after editing. A conservative fully dense setup requests no
more than 2,500 cards across both groups.

The sample's lower LODs use 65% / 35% density and four / three profile points.
They preserve seeds and generation shape, reducing only the rendered population
and sampling. Each preview prefab has a configured three-level LODGroup.

## Natural variation and front flips

Open **UMA → Hair Cards → Examples → Short Hair Part → Natural** or
**Front Flip Natural**. These are additional grooms; the original three styles
remain available. The new variants own their profiles, atlas settings and both
materials. Their source body and texture images are shared, read-only resources.

**Natural** adds 28% crown length and 22% short-side length, broader clump motion,
two scales of strand-local irregularity, and a restrained crown lift. Length
variation and split tips break up uniform ends. These are artistic starting
settings, not an automatic guarantee of coverage on a different head.

**Front Flip Natural** adds another masked 1.3× length adjustment at the forelock
and an outward bend that eases back into the original sweep near the tip. The
mask fades to zero outside the front region. It does not move roots or expand
the Growth / Density footprint.

### Find the controls in the tree

Expand a group's **Generate Hair → Natural crown cards / Natural side cards**.
Select a modifier to show only its properties. Toggle its **Active** checkbox to
compare it against the incoming result; changes are non-destructive and undoable.

| Modifier | What to adjust |
|---|---|
| Natural length | Scale multiplier; 1 leaves incoming length unchanged |
| Soft crown volume | Bend angle and Root → tip bend profile |
| Forelock length (flip only) | Extra length inside the forelock mask |
| Front flip — rise then sweep | Bend angle, bend profile, Root Influence |
| Natural 2D waves | Sideways/outward amplitude, wavelength, shared clump noise |
| Fine irregularity | Small independent variation; keep amplitude low |

The clump population also has **Broad flowing locks**, so neighboring cards move
together before the smaller individual differences are added.

To reshape the forelock region, expand **Optional Maps** and select
**Forelock — soft front mask**. Paint it just like another texture-backed map.
It is a *modifier mask*, not a growth map: white receives full extra length/bend;
black keeps the natural style. On either forelock modifier, expand **Where this
modifier applies** to inspect its assigned mask, and use **Show Mask** in the
tree to see the affected strands. Both modifiers deliberately share the same map.

### Strand-aligned 2D Noise

Add a **Noise** modifier and enable **Strand-aligned 2D**. This is new opt-in
behavior; leaving it off preserves the original fixed-root-frame noise.

- **Sideways amplitude (mm)** bends across the strand plane.
- **Outward amplitude (mm)** bends out of that plane. Noise is signed and can
  dip inward too; enable **Prevent scalp penetration** on the finished cards.
- **Wavelength (mm)** controls the physical scale of the smooth random signal.
  Shorter wavelengths give finer bends. It is a noise scale, not a periodic curl.
- **Root fade** smoothly introduces displacement over the given fraction of
  strand length. **Root Influence** additionally protects bending near the base.
- **Shared clump noise** blends independent seeded variation toward the parent
  clump's signal. 0 is independent, 1 is shared; 0.5–0.8 gives coherent loose locks.
- **Seed** changes the pattern without depending on frame time or preview LOD.

The sample uses 32 mm broad waves, with 4.5 mm sideways / 1.8 mm outward amplitude
on the crown, reduced to 2.5 / 1 mm on the short side. Fine irregularity uses
14 mm noise scale and 0.6 / 0.3 mm amplitudes. The root fade is 25%. Distances
are in source-mesh units (millimeters assume the usual one-unit-per-meter setup).

Noise follows a rotation-minimizing frame along the *incoming* strand. It does not
add axial jitter, and card roll/facing edits do not change the centerline. Root
anchoring, frozen points, stiffness and segment-length constraints take priority
over the requested noise amplitude, so the final displacement can be smaller.

### Surface Bend and the flip shape

**Surface Bend** rotates segment directions toward the strand's transported
outward normal, rebuilding the chain from its fixed root. It is not translation
in object Y or world Y. **Bend angle** sets the scale in degrees; **Root → tip bend
profile** multiplies it along the strand (1 = full bend, 0 = original direction,
negative = return toward the scalp). The separate influence curve controls blend.

The flip starts around 48°, retains most lift through its first third, then falls
through 0 toward a small negative value at the tip. Increase **Forelock length**
before increasing bend for a taller silhouette. Lower **Root Influence** for a
protected base, or raise it for a more immediate lift; the root itself never moves.
All angles are authored in the source-local frame, so rotating the character
does not turn a forelock lift into a world-axis displacement.

### Resolution, coverage and finish

The new examples request 900 crown cards at nine points (eight segments), and
650 side cards at five points (four segments). The actual output is 1,532 cards
and **19,384 unique triangles** at LOD0. LOD1 uses 9,950 and LOD2 uses 3,144.
Hybrid's second color pass submits the same geometry again, not extra unique faces.
The crown has wider cards to compensate for its lower count. It has 24 shape
control points; this does not mean 24 rendered card rows.

If noise looks angular, first check **Advanced shape resolution → Shape control
points**, then **Geometry & Vertex Colors → Profile samples**. Enough shape points
are needed to construct a bend, and enough card segments to render it. Larger
amplitude is not a substitute for tessellation. Keep the combined budget in view.

Final **Prevent scalp penetration** is enabled at 0.6 mm on both groups, after
length, bend, noise, width and card framing. It does not alter the authored guides.
Extreme edits can still need additional segments or hand-adjusted flow; inspect
the front, both sides, part and rear after changing the style.

Both materials use softer primary/secondary highlights. Existing inline color
and finish controls remain available in **Materials & UVs**; lowering highlight
strength and smoothness softens sheen without losing alpha detail. This is not
an exact match to a particular lighting setup. Judge the result under your game's
lighting, and sync the fringe after editing the first-pass material directly.

## Build this type of haircut on your own character

1. Create a groom from the generated character so its source binding is available
   for weight copying. Paint the crown and short side into separate groups. Leave
   no unintended growth on ears or the forehead.
2. Author relatively few guides to establish the silhouette and part. Direct crown
   roots up and back, side roots backward/downward, and rear roots toward the nape.
   Add guides where direction changes sharply, rather than using density to fix flow.
3. On each group's node, choose **Short Hair Part Preset…**. The confirmation is a
   popup. It replaces generation settings but preserves guides, maps, sculpt passes,
   assigned textures and materials. Undo restores settings. This is a generation
   preset, not automatic head recognition or an automatic haircut of arbitrary guides.
4. Divide the preset's 2,500-card default between your groups. Each invocation sets
   up one group, not a groom-wide budget. Existing groom-wide LOD overrides are
   preserved: enable **Use profile sampling** on LOD0 in Geometry & Vertex Colors
   to use the new five-point profiles, or explicitly set the LOD override to five.
5. Select **Materials & UVs**, then **Use UMA Short Hair Strips…** in the tree actions
   when using HairAtlasDiffuse_New or a matching layout. The action adds/selects
   three short UV sets without deleting existing sets. Roots use the top of each set.
   An assigned atlas is preserved by the preset; do not use these coordinates with
   an unrelated texture.
6. Use **Refine hairline** to narrow/shorten cards near the painted boundary. Keep
   the transition small near the part; excessive edge shortening exposes a wide gap.
   Separate groups prevent cross-part blending. Alternatively, the existing **Part
   regions** map can separate guide influence inside one group; assign it to both
   the clump and final-card populations.

## Color, lighting and production use

### Keep fine strands: choose the rendering mode

In **Materials & UVs**, find **Strand rendering** below the material assignments.
Choose **Cutout**, **Alpha Blended** or **Hybrid**. The confirmation creates new
material assets while preserving colors and textures; it does not overwrite or
delete the previous materials. Undo restores the assignments. These controls
apply to the supplied UMA hair shaders, not arbitrary third-party shaders.

- **Cutout** is the lowest draw-cost option and writes depth. Lower **Alpha cutoff**
  to keep fainter fibers. MSAA alpha-to-coverage smooths its edges when available.
  **Soft coverage (dithered)** instead uses screen-door coverage; inspect it in
  motion because stippling can be visible.
- **Alpha Blended** uses the lit `UMA/Hair Cards/Soft Hair URP` shader. Its color
  pass uses atlas alpha without the hard cutoff and does not write depth.
  Overlapping transparent triangles can sort incorrectly; this is not
  order-independent transparency. Shadows still use **Shadow Alpha Cutoff**.
- **Hybrid**, used by these three samples, draws depth-writing cutout cores first,
  then a lit, alpha-blended fringe. It retains fine strands without requiring
  MSAA and reduces, but does not eliminate, transparency sorting artifacts.
  **Opaque core cutoff** controls the solid core; **Strand opacity** controls the
  fringe. Inline color/finish edits synchronize both materials. After editing
  the first material directly in its Inspector, use **Sync fringe from first pass**.

**Alpha density** boosts faint atlas fibers while keeping zero-alpha background
transparent. A value of 1 is neutral; these fine short-strand atlases use 2.5,
with a 0.45 opaque-core cutoff. Increase density before increasing card count
when the atlas is too faint. Judge coverage with guide and wireframe overlays off.

Hybrid shares vertex data but adds a second draw and its own index range. The
20,000-triangle target describes unique hair geometry, not rendering cost:
two color passes submit about 40,000 triangles at LOD0, plus shadow/depth work.
For UMA baking, configure the corresponding **UMAMaterial second pass**; assigning
two preview materials alone does not configure an unrelated UMAMaterial asset.

### Card/scalp clearance

In **Cards > Card geometry**, enable **Prevent scalp penetration** for each
group. **Card clearance (mm)** controls the gap; start with **0.5 mm** for short
hair and **Root Embed = 0 mm**. The Short Hair Part preset uses these settings.
Older grooms keep this option off until you enable it. Enable it on both the
crown and side groups when updating an existing ShortHairPart groom, then rebuild
the card preview and rebake any exported meshes.

This is a final mesh correction after modifiers, width, roll, embedding, and LOD
sampling. It checks cross-sections, edge midpoints and triangle interiors, moving
only card geometry that needs clearance. Authored guides/anchors, texture maps,
UVs, card count and triangle count are unchanged; adjusted card normals and
tangents are refreshed. Saved settings support Undo, last-used setup inheritance,
and Reset to defaults. The check adds mesh-build work, not per-frame runtime work.

Root Embed deliberately permits an inset fading over the first 20% of a card.
Keep it at zero when diagnosing bald patches in close hair. Close Cut also keeps
its existing **Scalp clearance lift** modifier to shape the outward bend; Lift
alone does not protect the final ribbon edges or low-LOD faces.

The collision surface and distances are source-local (one source unit = one
meter), so moving or rotating the character does not redirect the correction.
Use a readable, outward-facing authoring surface. This is a bounded authoring
check, not runtime cloth collision: inspect animation, extreme deformations,
deeply tangled cards and very coarse LODs on the production character. Alpha
settings cannot fix geometry that is inside the body.

The existing Swept Hair URP shader supports root/tip color, color reach, strand
variation, primary/secondary shine and roughness. Select **Materials & UVs** for
the inline controls. A matte finish is configured; texture alpha, depth shading
and normal detail are retained. Finely cut alpha edges still need testing with
your game's lighting, resolution, antialiasing and motion.

`ShortHairPart_URP_Review.unity` shows Classic; Relaxed and Close Cut are inactive sibling
objects. Enable one hairstyle at a time. The review body uses scalp vertex shading
and neutral skin, and is **not rigged**. The scene does not install URP or switch
your project settings; open it in a URP project. Preview prefabs contain hair only.

The readable body snapshot allows editing without the original scene. For a
production UMA asset, bind the correct character/race slots in Source & Setup,
copy weights, bake the scalp vertex-color Mesh Modifier if needed, and test head
and neck deformation. Do not mistake the static review prefab for a skinned UMA
wardrobe asset. After grooming edits, rebuild/bake to update saved meshes; the
groom stage's live card preview updates independently of these packaged prefabs.
