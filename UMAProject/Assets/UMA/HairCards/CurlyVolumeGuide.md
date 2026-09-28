# Curly Volume: editable ringlets

For Unity 6.3+ and URP. The CurlyVolume sample is a separate hairstyle; PointySwept
is unchanged. It contains the 90 authored centerlines from the supplied reference,
a painted growth footprint, procedural ringlets, three atlas strips, a matte hair
material, and a standalone review scene. No external add-on is required in Unity.

## Open and explore

Choose **UMA → Hair Cards → Examples → Curly Volume**, or open
`Assets/UMAProjectData/HairCards/Examples/CurlyVolume/Curly_HairGroom.asset`.
Use **Hair Nodes** to select an item and **Hair Properties** to edit it. In
**Hair Preview & Settings**, choose Cards / Full and hide guide splines and card
wireframe for a clean material preview. The review scene is a static lighting
comparison, not the editable groom; open the groom asset to change the hairstyle.

The short route through the tree is:

```text
Guides / Grooming                 Broad silhouette, sweep and hairline
Generate Hair
  Curly Hair Cards               Coverage, spacing and variation
    Volume envelope              Overall length/volume multiplier
    Loose centerline variation   Break up repeated silhouettes
    Ringlets — shape & variation Curl radius, turns, facing and variation
Hair Cards
  Geometry & Vertex Colors       Ribbon width and render resolution
  Materials & UVs                Three strip selections, colors and finish
Scalp Vertex Shading              Darken the existing scalp under the hair
Optimize & LODs                   Distance-dependent budgets
```

To use the construction on your own character, paint Growth / Density and comb
the broad centerlines first. Select the group or Generate Hair and apply **Curly
Volume Preset…** in the tree. The confirmation lists the changes. It replaces
generation settings and creates a dedicated ribbon profile, preserving guides,
sculpt passes, textures and any assigned material. Undo restores the settings.
For an existing atlas, select Materials & UVs and **Use UMA Curly Strips…**.
This adds/selects the three long strips without deleting your other UV sets.

## Shape first, curls second

Comb the broad envelope in the upstream Sculpt Pass. When prompted, use **Edit
This Layer**: downstream ringlets are temporarily bypassed, so you never sculpt
their procedural offsets back into the original guides. Return to Final Preview
to see the curls. Use a finishing sculpt layer only for intentional edits to the
evaluated result; it is not a substitute for changing the curl controls.

The Ringlets modifier follows each incoming centerline with a transported frame;
it does not use a fixed world direction. The root stays anchored and frozen
original points are retained during automatic subdivision. Root Influence and
the root-to-tip influence curve still apply.

| Control | Artist-facing effect |
|---|---|
| Curl radius | Size of the loops, displayed in source-local millimeters; try 8–14 |
| Turns per strand | Number of loops along the envelope; start around 3 |
| Distance per turn | In Distance Between Turns mode, keep the same curl pitch on short and long hair; the sample starts near 27 mm |
| Straight root reach | Smoothly introduce curls near the scalp instead of an abrupt offset |
| Tip radius multiplier | Tighten or open the ends relative to the root-side curls |
| Radius / turn variation | Break up repeated, identical coils |
| Start-angle variation | Stagger loops so adjacent cards do not line up |
| Reverse curl fraction | Mix the two winding directions |
| Keep clumps related | Share variation with the parent centerline |
| Face around curl | Wrap ribbon facing around each loop, avoiding flat-looking coils |
| Shape points per turn | Automatically supply the geometry needed to describe a curl |
| Smooth centerline | Round transitions between input controls in envelope mode, retaining the controls themselves |

**Keep Envelope** adds arc length around the incoming silhouette. This is an
intentional detail-generation mode, not guide stretching. **Preserve Strand
Length** keeps the incoming arc length and therefore contracts the silhouette as
curls tighten. Neither mode rewrites authored guides. For general length-preserving
waving, the original Curl and Wave modifiers remain available.

Paint a modifier mask if only some areas should curl. “Where this modifier applies”
also supports length and hairline filters. Use **Show Mask** or **Inspect Population**
in the tree to understand the result. Seeded variation is stable across rebuilds
and render LOD changes. Changing generation density can change which roots exist.

For gravity, apply the existing Gravity modifier to the envelope before Ringlets
for a hanging silhouette, or after it to settle the curled result. World gravity
uses the character's transform; Ringlets itself is source-local and has no hidden
world-axis sag. Roots/frozen points take priority over a desired deformation.

## Make the finish softer

In **Materials & UVs → Surface finish**, choose **Matte**, **Natural** or **Glossy**.
These change only the two highlight strengths and roughness values. Colors, maps,
alpha cutoff and lighting/scattering remain untouched. Presets and individual
sliders support Undo. A shared material changes everywhere it is used; duplicate
the material first for a separate hairstyle variation.

Higher roughness broadens a highlight; it does not by itself remove shine. Reduce
both **Primary shine** and **Secondary shine** for a dry finish. Zero on both
removes both direct-light specular lobes. Root/tip colors and backlit scattering
still contribute to brightness. Full lighting controls remain in the material
Inspector. The sample uses low shine and warm root/tip colors, not metallic hair.

**Soft coverage (dithered)** is an optional alternative to solid cutouts. Strand
Opacity controls fine, strand-specific stippling in the color, depth, normal and
shadow passes while retaining depth writes. It does not need sorting, but can look
grainy without temporal antialiasing and must be reviewed in motion. The sample
defaults to clean MSAA cutouts; enabling soft coverage is an explicit artistic
choice, separate from the Matte finish.

## Sampling and performance

Select **Generate Hair → Curly Hair Cards → Ringlets → Curl mesh detail** to tune
the output polygons. **Error-based ribbon sampling** preserves the detailed
procedural curve, then removes unnecessary mesh rows. It checks the centerline,
both ribbon edges, roundness and shading frames—not just the curve's length.
Retained rows use frames from the detailed curve so coarse chords do not introduce
new ribbon twists. Tight, unstable reference bends are refined locally.

| Mesh preset | Shape error | Facing error | Maximum segments/card |
|---|---:|---:|---:|
| Economy | 2.5 mm | 25° | 47 |
| Balanced | 2 mm | 18° | 63 |
| Close-up | 1 mm | 12° | 127 |

These buttons change mesh-quality settings only. Radius, curl spacing, variation,
guides and seeds stay unchanged. The sample uses **Balanced**, approximately
81,000 triangles instead of the original 198,570, without reducing card density.

- **Shape error (mm):** higher values generally reduce polygons; lower values
  follow the detailed ribbon edges more closely. Distances are source-local.
- **Facing error:** limits deviation of the interpolated shading frame. Use tighter
  values for strong highlights or obvious ribbon rotation.
- **Maximum segments / card:** a ceiling, not a target to fill. The active LOD or
  profile can impose a lower ceiling. The panel shows the last rebuilt group's
  card/triangle counts and warns when a cap prevents meeting the error targets.

The profile's old length/angle count estimate is bypassed for optimized ribbons;
its sample cap still applies. For example, Close-up allows 127 segments but a
96-point profile allows only 95. Increase **Profile samples** if you need the
higher ceiling. Disabling error-based sampling restores the profile's usual
uniform/adaptive-count mode. Tubes continue to use profile sampling.

Root/tip endpoints, arc-length UVs, widths and facing are retained in the reduced
mesh. Vertex RGB/alpha still holds the requested number of root segments, then
fades by distance to the tip. Masks, roots and frozen points in the evaluated
curves are not edited by mesh reduction.

**Advanced curve construction → Shape points per turn** is different: it controls
the procedural reference curve, not the output polygon budget. Normally leave it
at 12. Ringlets retains original controls/frozen anchors while adding up to 256
shape points, with at most 20 turns per strand. Changing construction quality can
change fine shape; changing Curl mesh detail cannot.

Judge the result in Cards / Full from several angles. Draft and lower LODs
deliberately impose smaller budgets and may show a sampling-limit advisory. At
distance, reduce density and sampling together, checking scalp coverage. Error
targets are measured against the sampled reference, not a guarantee of invisible
pixel error at every camera distance.

The material is two-sided in one pass. Leave duplicate backface geometry and the
second material pass off unless deliberately required. The sample's atlas copies
use mipmaps and alpha coverage preservation; original shared texture importers
are not modified. This reduces distant sparkle but does not eliminate all temporal
aliasing from very fine alpha strands.

## Attach to a production character

The saved authoring body is an unrigged geometric reference, not proof of a slot
mapping. Use [Bind Character / Race](SweptClumpsGuide.md#attach-an-existing-hairstyle-to-a-uma-character)
to attach the intended body's slots and skeleton without replacing the hairstyle.
For native UMA30 body slots, start with **Z Up To Y Up Flip Forward**, preview the
alignment, then attach. Recreate the scalp Mesh Modifier against the real slots
and enable the desired outputs in Validate & Bake. Review animation deformation
before shipping. The sample deliberately does not assume which race you will use.
