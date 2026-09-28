# Swept clumps: from a few guides to finished hair

For Unity 6.3+ and URP. This workflow builds the PointySwept hairstyle:
a small set of authored guides, intermediate
clump guides, independently scalp-rooted cards, and separate shading controls.
All generation, grooming and shading are configured in UMA.

## Start here

For a ready-to-edit reference, open
`Assets/UMAProjectData/HairCards/Examples/PointySwept/Pointy_HairGroom.asset`.
The adjacent `Pointy_URP_Review.unity` is a standalone lit comparison scene;
save your current scene before opening it. Its README explains the unrigged
reference body and the review-only scalp slot binding.

For wardrobe and scalp Mesh Modifier output, open a groom from a **built UMA
character**, or use **Bind Character / Race** for an existing groom (see
[Attach an existing hairstyle](#attach-an-existing-hairstyle-to-a-uma-character)).
Open **Hair Nodes**, **Hair Properties**, and **Hair Preview
& Settings** from the UMA > Hair Cards menu. Dock Nodes beside the Scene view and
Properties on the right. Preview & Settings can share a tab with Properties.

Use the tree as the only navigator. Properties always edits the selected item.
The **Active** button bypasses an operation; selecting its name edits it. Collapsing
a branch does not bypass it. Confirmations are modal popups; operations support Undo.

```text
Growth / Density                 Where hair grows
Guides → Grooming / Spline Flow   Overall silhouette, sweep and facing
Generate Hair
  Primary Clumps                 Several hundred curved clump centerlines
    Soft clump bend              Broad coherent variation
  Fine Hair Cards                Several thousand surface-rooted strands
    Fine strand variation        Small independent detail
Hair Cards
  Geometry & Vertex Colors       Width, ribbon shape, segmentation, animation RGBA
  Materials & UVs                Atlas strips, shader, colors and optional second pass
Scalp Vertex Shading             Shade the existing body; no additional scalp cap
Optimize & LODs → Validate & Bake
```

## 1. Establish the hairline

Select **Growth / Density**. Paint the scalp, temple points and nape; leave the
forehead and ears clear. Zero means no growth; one means full density. A soft brush
edge produces a softer transition. The optional Density Multiplier is independent
thinning; leave it at one unless needed.

Rotate all the way around the head. Check the crown for accidental unpainted holes.
An incomplete mask can resemble an interpolation or shading defect. Use **Focus
current area** in Preview & Visibility for an axis-centered orbit. Enable scene
depth and turn off unnecessary roots, child splines and card wireframes.

## 2. Shape the sweep with a modest guide count

For this short swept style, start with approximately **50–80 guides**. In Guides,
increase Uniformity for even control coverage, preview, then accept the layout.
Guide count determines editing control, not final card density.

Add a Sculpt Pass. Comb the temples backward, lift and sweep the forelock, and
direct the crown into the back. Keep the nape shorter. **Length** and **Cut** can
change length; the positional tools preserve segments and root anchors.

Add **Spline Flow** under the pass to establish consistent direction and outward
card facing. Draw a few paths from roots toward ends, not one path for every card.
Use **Outward facing = 1** with **Follow direction = 0** if only orientation needs
correction. Use **Edit This Layer** before combing upstream of modifiers; return
to Final Preview afterward. Generated clumps/cards are deliberately bypassed while
editing the upstream layer, preventing downstream offsets from feeding back into it.

## 3. Apply the Swept Clumps preset

Select the group or **Generate Hair**, then **Swept Clumps Preset…** in Hair Nodes.
The confirmation explains what changes. The preset preserves guides, sculpt passes,
assigned textures and materials. It replaces generation settings and creates a
dedicated arched-ribbon profile; Undo restores the assignments/settings.

The starting recipe is approximately 430 clump guides feeding 4,500 cards. Counts
are full-density budgets for the painted footprint, not a guarantee: soft paint,
spacing, hairline thinning and rejected influences can reduce the output. Each
population's Properties shows its actual input/output count, timing and cache status.

Select **Primary Clumps**, then **Inspect Population** in the tree. Curves are
colored by their parent clump. Start with these controls:

| Control | What to use it for |
|---|---|
| Count at full density | More/smaller clumps versus fewer/bolder locks |
| Follow parent clump | Attraction toward the parent's entire curved centerline |
| Root → tip clumping | Open roots and stronger gathering toward the ends |
| Clump separation | Keep some root spacing even at strong clumping |
| Split tips | Relax clumping over the last 30% for broken-up ends |
| Root bending | Protect the base without moving the anchored root |
| Blended neighbors | Blend surrounding guide shapes, not just one guide |
| Follow scalp curvature | Transport nearby guide shapes into the new root's frame |

**Final Hair** returns to the complete result. Inspection is temporary: it does
not change saved display preferences or the bake. Selecting Growth or a Sculpt Pass
ends inspection. Add another clump stage only when you need a second scale of locks.
Reorder or duplicate stages in the tree; disabled stages pass the previous population
through to the next enabled stage.

## 4. Tune coverage, the hairline and natural variation

Select **Fine Hair Cards**. Keep three blended neighbors as a starting point.
Minimum spacing sets a lower bound between roots. Increasing Uniformity makes the
root distribution more even; it does not straighten the hairstyle.

Under **Root placement & guide influence**, Surface compatibility rejects guides
with incompatible root normals. Increase Influence radius only enough to reach
nearby compatible guides. A warning reports roots that cannot find one. For a part,
choose a painted **Part regions** map and paint opposite sides near zero and one.
Disconnected painted regions also prevent unintended cross-region interpolation.

Enable **Refine hairline**. A 20 mm transition, edge length 0.6, edge width 0.4 and
edge density 0.65 are useful starting values. Distance is measured along mesh edges
from the **painted growth boundary**, including on closed meshes. Coarse scalp
topology limits how finely vertex-painted boundaries can be resolved.

Use **Natural variation & flyaways** sparingly: about 8% length variation, 20% width
variation and 8° tilt for the swept preset. **Keep clumps related** shares variation
within a parent clump. A small flyaway fraction breaks up the silhouette without
turning every card into noise. Seeds are deterministic.

Population modifiers run after that population is generated. A broad Noise on
Primary Clumps shapes whole locks; a much smaller Noise on Fine Hair Cards adds
strand detail. Noise frequency is in cycles per source meter and samples continuous
arc length, so changing card tessellation does not reseed it.

For localized effects, expand a modifier's **Where this modifier applies**. Painted
mask, length, hairline distance and seeded variation multiply, then remap/invert.
**Show Mask** in the tree displays dark protected curves and bright affected curves.
A missing explicitly assigned map protects everything, even with Invert enabled.

## 5. Set up ribbons and the existing UMA atlas

In **Geometry & Vertex Colors**, start with a 10.1 mm root width, a small tip width,
two cross-width spans, roundness 0.2 and adaptive segments. Equal root/tip widths
make a true ribbon; the width envelope interpolates through the whole card.

The preset uses nine shape control points, independently of render tessellation.
Adaptive tessellation uses length and total turning angle to choose a row budget,
capped by profile/LOD samples. This is a budget heuristic, not a certified maximum
geometric-error simplifier. Increase the cap or tighten segment limits for tight curls.

**HairAtlasDiffuse_New and its variants share the same UV layout.** Use the
existing atlas; no rebake is needed. In Materials & UVs, **Use UMA Swept
Strips…** adds/selects the two long strips used by the supplied style. Existing UV
sets and the selected texture variant are preserved. Swap diffuse variants without
redrawing these sets. The imported reference has roots at V=1; strip Flip V maps
the tool's root-to-tip coordinates accordingly. Root/Tip shader colors remain
correct regardless of UV flips.

Create a **Swept Hair URP Material** if desired. This creates a new material and
preserves the old one. The shader shades both sides in one pass: disable duplicate
backface geometry. A second material pass remains available for deliberate effects,
but is not required for this shader and increases rendering cost.

The atlas canvas supports Color + Alpha, Color and Alpha views, checker backgrounds,
UV-set selection, add/remove, moving/resizing and pixel snapping in the same dialog.
If the atlas has no mipmaps, Properties offers **Enable Alpha-Preserving Mipmaps…**.
This requires confirmation because its import settings are shared with other users
of the texture. It never alters the source image pixels.

## 6. Color and light the hair

**Roots**, **Tips**, **Root color reach**, **Strand variation** and **Alpha cutoff**
are directly available under the card material in Hair Properties. Full lighting
controls are grouped in the material Inspector. Material edits affect every group
sharing that material; make it unique first for an independent hairstyle.

The shader provides shifted primary/secondary strand highlights, soft diffuse
lighting, backlight scattering, normal and AO maps, and independent shadow cutoff.
Use neutral lighting while choosing the colors; a strongly orange key light can
make blonde hair look copper. Increase root reach for deeper roots; lower highlight
strength if the locks look metallic. MSAA alpha coverage uses URP's sharpened
coverage rule; enable MSAA in the renderer for smoother strand edges.

| Data | Channel / purpose |
|---|---|
| Atlas texture | RGB shading/detail, A opacity; optional atlas-R depth shading |
| Optional separate depth atlas | R strand depth/contrast using the same UV0; useful with nearly white diffuse atlases |
| Normal / AO atlases | Normal map / occlusion R, same atlas rectangles |
| UV0 | Selected atlas rectangle, including flips |
| UV1 (`mesh.uv2`) | Normalized root-to-tip position, stable strand random |
| UV2 (`mesh.uv3`) | Stable parent-clump random, inspection mask |
| Tangent | Longitudinal strand direction for anisotropic highlights |
| Vertex RGBA | Artist/animation gradient; not silently reused for shader IDs |

Vertex RGB tint and vertex-alpha opacity are opt-in material controls. By default,
RGBA remains available for vertex animation. **Root Color Segments** holds rows 0
through N at root RGBA, then fades to tip RGBA. At reduced resolution the hold is
clamped to leave a final fade segment; review animation masks at each LOD.

## 7. Shade the existing scalp with a Mesh Modifier

For an imported groom such as PointySwept, first attach the real body with
**Source & Setup → Bind Character / Race** (see below). The supplied review-only
scalp modifier is not a production body-slot mapping.

Select **Scalp Vertex Shading**. Tune color, strength and hairline fade, then choose
**Create / Update & Apply Scalp Modifier**. Open the groom with its original built
UMA character so slot names and vertex ranges can be proven. A raw imported DCC
mesh is not sufficient proof of a production UMA slot mapping.

The preview uses an owned mesh copy. The saved source mesh is never recolored.
The generated standard UMA `MeshModifier` contains per-slot vertex-color adjustments;
all participating groups compose into one modifier. Existing vertex alpha is
preserved by default. No scalp cap or extra surface is generated.

The skin material must read vertex RGB to display these colors. The included
**Scalp Vertex Preview URP** shader demonstrates that contract; it is a review
material, not a replacement for the character's full skin shader.

Bake validates the slot binding, refreshes the modifier from current paint/settings
and attaches it to the generated wardrobe recipe. Disabling scalp shading and
rebaking removes this groom's modifier from that recipe; unrelated modifiers remain.
Reopen with the original character and rebind after source topology changes.

## 8. Check and bake

Inspect front, both sides, rear and grazing angles. Check ears, temples, crown and
nape. Look for unpainted holes before increasing card count. Use a small amount of
root embedding, not a large inset that hides the first card rows. For genuine
intersection issues, correct upstream guides or use the existing collision/gravity
controls; interpolation is not a full strand-body simulation.

Use Draft/Medium preview while grooming dense hair. Generation is deferred until
the stroke ends; material changes do not need a shape rebuild. Stable root and
neighbor caches are reused when only guide shapes change. Paint, topology, root
placement or eligibility changes invalidate the relevant caches. Warm stage timing
is shown in Properties; use Unity Profiler markers `HairCards.Evaluate`,
`HairCards.GeneratePopulation` and `HairCards.BuildMesh` for a complete update cost.

Run Validate & Bake, inspect a dry run, and review each output LOD. The second pass,
alpha overdraw, shadow maps and skinning cost matter in addition to triangle count.
No card count alone guarantees a target frame rate or a particular visual quality.

## Optional: import authored guides

The version-1 JSON interchange accepts authored root-to-tip paths and a source mesh.
`Tools/export_authored_guides.py` is an optional adapter for a host that provides
the `bpy` and `mathutils` Python APIs and raw CURVES data; it is not a standalone
Python exporter or a universal converter. It exports raw control paths, not evaluated
hair geometry or material graphs. Run it through that host's command line with
`--background --factory-startup --disable-autoexec`; see the example in the script.
Specify the body mesh, raw guide object and optional growth mesh. Pass the script's
`--help` after the host's `--` separator. Only open trusted source files.

In Unity choose **UMA > Hair Cards > Import Authored Guide File…**, also available
when Source & Setup is selected. Import creates a new groom and resource folder,
binds the paths to source triangles and applies the swept preset. The source JSON
and source authoring file are never changed. Production UMA recipe output is initially off
until the imported source has a proven slot/skinning workflow.

The JSON fields are `version: 1`, `name`, source `vertices`/`normals` (x/y/z), `uv`
(x/y), triangle indices, per-vertex `growth` in 0..1, and `guides` containing `name`
and ordered root-to-tip `points`. Positions use Unity axes and meters. Optional
`regions` use ordinary `x`, `y`, `width`, `height` fields in 0..1; `rootAtVOne`
controls strip flipping. Optional `atlasFile` is a PNG filename beside the JSON,
never a path. Without it, the preset reuses the existing UMA hair atlas.

## Attach an existing hairstyle to a UMA character

In **Hair Nodes**, select **Source & Setup**, then find **Bind Character / Race**
in Hair Properties. This does not replace the authoring surface: its vertex order,
paint, guide roots, sculpt history, generation settings and hair materials remain intact.

1. Assign a generated **DynamicCharacterAvatar**, or a **RaceData** asset.
   The character route uses its built body geometry and weights in character-local
   coordinates. The race route loads its Base Recipe slots in their rest geometry;
   it does not run DNA, animation, wardrobe generation or texture atlasing.
2. Keep the body/head slots checked. **Base race slots**, **All**, and **None** help
   select them. Exclude existing hair, clothing, eyes and accessories from the donor.
3. Press **Preview Alignment**. The cyan donor appears with the existing hair.
   Orbit around the head and inspect the crown, temples, ears and nape. Adjust the
   donor position, rotation and uniform scale if necessary, then preview again.
   For PointySwept with the native UMA30 body slots, select **Donor coordinates →
   Z Up To Y Up Flip Forward**. The source body uses Z-up bind coordinates and the
   imported groom uses Y-up coordinates; this preset also corrects normals/winding.
   A live character normally uses **As Saved** instead.
4. **Match tolerance** is the maximum allowed surface distance for painted vertices
   and guide roots. A mismatch blocks attachment and reports its worst distance.
   Correct the body/alignment rather than inflating the tolerance to hide a mismatch.
5. Press **Attach Validated Binding…** and confirm the popup. A new binding asset
   stores donor geometry, skin weights, bone names/hierarchy, bind poses, real slot
   references and a reverse surface mapping for scalp shading. The compatible bake
   race is updated. Older binding assets remain intact for Undo/recovery.
6. Recreate **Scalp Vertex Shading → Create / Update & Apply Scalp Modifier**.
   Attachment clears the old scalp modifier assignment, not the old modifier asset.
   Colors are projected onto the real slot vertices; no matching vertex order is assumed.
7. In **Validate & Bake**, enable the required mesh/slot/overlay/wardrobe outputs,
   check the material and race, then validate and bake. Imported examples leave
   production outputs off until you explicitly enable them.

Each card receives barycentrically blended body weights at its root, including
variable-influence weights. Its tips retain those influences instead of attaching
to nearby shoulders. Every output LOD is converted back into the donor's bind
coordinates (character-local pose coordinates for a built character).
The saved donor and skeleton support baking after closing the original scene.
Missing or modified body slots and changed authoring geometry require rebinding.

After attachment, **Show character preview**, slot/UDIM visibility and **Focus Head /
Focus Neck** work from the saved binding. The bound preview is neutral rest geometry
with scalp vertex shading, not a regenerated textured/animated avatar. Hidden donor
slots do not delete or hide the independent authoring surface's painted data.
Test the baked hairstyle on an equipped, animated UMA character before shipping.

**Detach Character Binding…** removes the assignment after confirmation; it does
not delete the binding asset, paint, guides or source mesh. Attachment and detachment
support Undo. Use a new binding to change race or body shape safely.
