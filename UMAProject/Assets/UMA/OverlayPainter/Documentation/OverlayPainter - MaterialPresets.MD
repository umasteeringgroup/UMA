# Overlay Painter - Material Presets

Last reviewed: August 30, 2026.

Overlay Painter Material Presets are reusable, artist-authored layer stacks. Build a surface
treatment once, preserve its editable layers and procedural settings, then apply independent copies
to other compatible paint targets.

A preset can contain one layer, a complete group and its descendants, or the entire active layer
stack. It can combine color, roughness, metallic, ambient occlusion, normal detail, masks, paths,
effects, raster painting, Fill layers, and generator or filter plugins in one reusable asset.

For the complete painting workspace, masking, path, save, and export workflow, see
[Overlay Painter](OverlayPainter.md). For the available procedural tools, see
[Overlay Painter Generators and Filters](OverlayPainterGeneratorsAndFilters.md).

## Contents

- [The essential idea](#the-essential-idea)
- [Material Presets compared with other assets](#material-presets-compared-with-other-assets)
- [Quick start](#quick-start)
- [Plan a reusable material](#plan-a-reusable-material)
- [Create a Material Preset](#create-a-material-preset)
- [What a Material Preset saves](#what-a-material-preset-saves)
- [What a Material Preset does not save](#what-a-material-preset-does-not-save)
- [Apply a Material Preset](#apply-a-material-preset)
- [What happens during application](#what-happens-during-application)
- [Compatibility and portability](#compatibility-and-portability)
- [Work with an applied preset](#work-with-an-applied-preset)
- [Update and version presets](#update-and-version-presets)
- [Package a preset for distribution](#package-a-preset-for-distribution)
- [Artist-friendly stack design](#artist-friendly-stack-design)
- [Production examples](#production-examples)
- [Best practices](#best-practices)
- [Troubleshooting](#troubleshooting)
- [Release checklist](#release-checklist)

--------------------------------------------------------------------------------

## The essential idea

Think of a Material Preset as a reusable recipe for a surface treatment, not as a flattened texture.

For example, a worn leather preset might contain:

1. A Fill layer for the base leather color and roughness.
2. A procedural grain layer that adds normal and roughness variation.
3. A color-variation layer.
4. An edge-wear generator that uses the destination mesh.
5. A dirt layer masked into creases.
6. A final group mask and group-opacity control.

Saving that group as a Material Preset preserves the editable structure. Applying it creates new
layers on the selected target. The artist can then change the color, reduce the wear, repaint the
mask, regenerate procedural layers, or remove individual parts without changing the source preset.

Material Presets are deliberately not live instances. Applying the same preset twice creates two
independent sets of layers with new identities. Editing either applied copy does not edit the preset,
and updating the preset later does not silently change existing artwork.

--------------------------------------------------------------------------------

## Material Presets compared with other assets

| Asset or feature | What it stores | Use it for |
| --- | --- | --- |
| **BrushPreset** | Brush shape, stamp, size, spacing, flow, randomization, and stroke behavior | Reusing how a stroke is painted |
| **Material Preset** | One layer, a group subtree, or a complete editable layer stack | Reusing what a surface is made from |
| **TexturePaintDocument** | The complete working document, target bindings, base state, editable layers, and workspace state | Continuing a specific painting project |
| **OverlayDataAsset export** | Final texture outputs and UMA overlay metadata | Putting the finished result into a recipe or production character |
| **Packaged Material Preset** | A Material Preset plus packageable referenced assets embedded into one `.asset` | Sharing a reusable material with another project or artist |

A BrushPreset can be one ingredient inside a Paint layer. A Material Preset can preserve that Paint
layer, its authored pixels, its source references, its mask, and the other layers that make up the
material. A paint document remains the authoritative editable project; a Material Preset is a
portable selection copied out of that project.

--------------------------------------------------------------------------------

## Quick start

### Save the complete active stack

1. Open Overlay Painter and select the intended paint target.
2. Finish and review the active layer stack in both 2D and 3D.
3. Regenerate any stale Plugin layers or plugin-generated masks.
4. Choose **File > Material Preset > Save Entire Stack...**.
5. Save the new `.asset` below `Assets`.
6. Select the created preset and add a useful display name, description, category, tags, author, and
   thumbnail in its Inspector.

### Apply the preset

1. Open the destination character, slot, or saved paint document in Overlay Painter.
2. Select the logical paint target that should receive the material.
3. Choose **File > Material Preset > Apply...**.
4. Select a `TexturePaintMaterialPreset` in the object picker.
5. Read any compatibility warnings, then choose **Apply** if the transfer is appropriate.
6. Inspect the newly created wrapper group in 2D and 3D.
7. Save the paint document when satisfied.

The dropdown beside **+ Paint** or **+ Fill**, and the selected layer's row menu, also provide
**Apply Material Preset...**.

--------------------------------------------------------------------------------

## Plan a reusable material

The most important Material Preset decision is its scope. Decide whether the preset describes a
general material, an artistic treatment, or target-specific artwork.

### Save one layer when

- The layer is a useful building block by itself.
- A generator has a well-defined purpose such as pores, cloth grain, dust, or color breakup.
- A Fill layer carries a coordinated set of reusable channel sources.
- The receiving artist should decide where the layer belongs in an existing stack.

Saving a child layer by itself does not pull in its parent group or siblings. It becomes a standalone
template layer when applied.

### Save a group when

- Several layers only make sense together.
- Group opacity, blend, visibility, or masking is part of the artistic result.
- The stack should arrive as one manageable material component.
- You want a clean boundary between the reusable material and target-specific finishing layers.

Saving a group includes its complete descendant subtree, including nested groups.

### Save the entire stack when

- The whole active target is the intended reusable look.
- The layer order from base treatment through final wear is important.
- There are no unrelated tests, guides, hidden experiments, or target-specific cleanup layers.

Before saving an entire stack, remove temporary work and give every important layer a descriptive
name. Hidden layers are still part of the stack and are saved with their current visibility state.

### Separate portable and target-specific work

A strong production pattern is to split a material into two presets:

- A broadly reusable base preset built from Fill layers, triplanar sources, and procedural
  generators.
- A target-specific finish preset containing painted masks, paths, fitted graphics, or hand-painted
  wear for one UV family.

This makes the reusable portion safer to apply to new garments while keeping carefully placed art
from appearing on the wrong UV islands.

--------------------------------------------------------------------------------

## Create a Material Preset

Overlay Painter provides the save command in several locations.

### From the File menu

Use **File > Material Preset > Save Entire Stack...** to save all layers on the active target. The
command is disabled when there is no active target or the target has no layers.

### From a layer row

Open the row menu for the desired layer or group, then choose one of these commands:

- **Preset > Save Layer as Material Preset...**
- **Preset > Save Group as Material Preset...**
- **Preset > Save Entire Stack as Material Preset...**

The Group command includes every descendant of that group. The Layer command saves only that layer.

### Choose the asset path

The save dialog creates a `TexturePaintMaterialPreset` `.asset` below `Assets`. Overlay Painter
suggests a name based on the active target, layer, or group. After saving, the new asset is selected
and highlighted in the Project window.

If the selected path already contains a Material Preset, Overlay Painter asks whether to update it.
Confirming replaces the saved stack and increments the preset revision. If a different asset type
occupies that path, Overlay Painter does not overwrite it.

### Add artist-facing metadata

Select the preset in the Project window and use its Inspector to add:

- **Display Name**: the name artists should see and the name used for the applied wrapper group.
- **Description**: what the preset does, which materials it expects, and important usage notes.
- **Category**: a broad family such as `Skin`, `Fabric`, `Metal`, `Damage`, or `Creature`.
- **Tags**: cataloging terms such as `leather`, `worn`, `brown`, `hero`, or `body-uv-v3`.
- **Thumbnail**: a representative preview rendered on a neutral, readable model.
- **Author**: the artist or team responsible for the preset.

Creation and modification timestamps, source information, channel requirements, plugin
dependencies, portability flags, and revision information are recorded with the asset. A thumbnail
is not generated automatically; assign one that communicates the result clearly.

--------------------------------------------------------------------------------

## What a Material Preset saves

A Material Preset preserves the data needed to reconstruct editable copies of the selected layers.

| Content | Saved behavior |
| --- | --- |
| Layer hierarchy | Groups, nested groups, child relationships, and composition order |
| Layer state | Name, type, visibility, opacity, and blend mode |
| Authored channels | Semantic channel identity, per-channel opacity/blend settings, source settings, and compressed cached pixels |
| Paint layers | Paint settings, brush/source references, stored stroke records, and authored channel pixels |
| Fill layers | Per-channel sources, colors, projection mode, tiling, offset, rotation, and cached output |
| Path layers | Editable spline, path settings, sources, effects, and cached channel output |
| Plugin layers | Plugin id, saved version, parameters, declared dependencies, stale state, and cached output |
| Masks | Base value, painted mask pixels, source settings, mask effects, mask plugin id/version, and plugin parameters |
| Effects | Ordered layer effects and their channel-specific settings |
| Source references | Referenced textures, sprites, overlays, brush presets, materials, and fonts used by saved settings |
| Source fingerprint | Material name, slot names, mesh/topology/UV signatures, channels, and portability information used for compatibility guidance |

Raster pixels and masks are compressed in the preset. Cached output is important because it keeps
the last successful appearance available when a source or plugin cannot be regenerated on another
machine.

--------------------------------------------------------------------------------

## What a Material Preset does not save

A Material Preset is intentionally smaller in scope than a complete paint document.

It does not save:

- The destination character, slot, renderer, material instance, or live scene object.
- The immutable base textures beneath the Overlay Painter layer stack.
- The active camera, window layout, selected tool, or other workspace state.
- Undo and Redo history.
- A live connection that automatically updates applied copies.
- Plugin implementations, editor assemblies, or shader code.
- External source assets inside an ordinary source preset. Those remain referenced project assets
  until the preset is packaged.
- Final exported `OverlayDataAsset` content or recipe assignments.

Target-specific layer and logical-target identities are removed during capture. Fresh identities are
created every time the preset is applied, which prevents one application from owning or modifying
another.

--------------------------------------------------------------------------------

## Apply a Material Preset

### Choose the destination deliberately

Before applying, select the correct artist-facing logical target. On a multi-slot or UDIM target,
Overlay Painter applies the preset across the physical texture sets that belong to that logical
target. Do not rely only on what is currently framed in the Scene view; confirm the target name in
the target selector.

### Open the Material Preset picker

Use any of these routes:

- **File > Material Preset > Apply...**
- The dropdown beside **+ Paint**, then **Apply Material Preset...**
- The dropdown beside **+ Fill**, then **Apply Material Preset...**
- A layer row menu, then **Preset > Apply Material Preset...**

Select the desired `TexturePaintMaterialPreset` asset in Unity's object picker.

### Read the compatibility report

Overlay Painter evaluates the preset before creating layers. A blocking error prevents application.
Warnings explain content that can still be applied with limitations, such as:

- A channel that the destination material does not support.
- Raster content being transferred to a different UV layout.
- A missing plugin or a plugin-version difference.
- A missing referenced Texture, Sprite, or `OverlayDataAsset`.

Read the complete message before continuing. A warning is not automatically a failure, but it should
change what you inspect after application.

### Let procedural work finish

Applying a complex preset can regenerate Fill and Plugin layers. A progress window reports the work
and allows cancellation. Avoid changing targets or closing Overlay Painter until it completes.

If application is canceled or a generator fails, Overlay Painter removes everything created by that
application. A successful application is registered as one Undo/Redo operation.

--------------------------------------------------------------------------------

## What happens during application

Overlay Painter performs the following operations:

1. It resolves the selected logical target and all of its physical texture sets.
2. It checks supported semantic channels, UV compatibility, source references, and plugins.
3. It creates a wrapper group named after the preset.
4. It creates fresh independent copies of the saved hierarchy inside that group.
5. It maps saved semantic channels to channels supported by each destination material.
6. It scales cached raster pixels and masks to the destination working resolution.
7. It regenerates Fill layers when their sources are available.
8. It reruns available layer and mask plugins in saved bottom-to-top composition order.
9. It retains cached output and marks procedural content stale when a required plugin is unavailable.
10. It records preset id and revision provenance on the created layers.

The order of procedural regeneration matters. A filter reads the composite beneath its own layer, so
the preset is regenerated from lower layers toward higher layers. Preserve intentional filter order
when creating the source stack.

Unsupported channels are skipped in the ordinary artist workflow. For example, an Albedo/Roughness/
Normal preset can still be applied to a material without a Roughness channel, but that part of the
look will not be present. Always evaluate the final result under production lighting.

--------------------------------------------------------------------------------

## Compatibility and portability

The preset Inspector summarizes portability using one or more flags.

| Portability | Meaning for artists |
| --- | --- |
| **Portable** | The saved stack has no known UV-, mesh-, or plugin-dependent content. Artistic fit must still be checked. |
| **UV Dependent** | The preset contains Paint layers, Path layers, or raster masks whose placement follows the saved UV space. |
| **Mesh Dependent** | A saved plugin requires destination mesh maps such as curvature, ambient occlusion, thickness, world position, or surface normals. |
| **Requires Plugin** | A layer or mask references an Overlay Painter plugin. The plugin is needed to regenerate the procedural result. |

A preset can have several flags at once.

### Channel compatibility

Material Presets use semantic channels such as Albedo, Roughness, Metallic, Ambient Occlusion,
Normal, or Normal Control. The destination `UMAMaterial` decides which semantic channels exist and
how they are packed into physical textures.

Best results come from targets with correctly configured **Overlay Painter Channel Layout** data.
Applying a preset does not repair an incorrect channel layout. Verify custom shaders and packed maps
before judging a preset.

### UV-dependent content

Painted pixels, painted masks, and fitted paths transfer in UV space. When the destination UV
signature differs, the same pixel positions may land on different body parts, garment panels, or UV
islands. Overlay Painter warns about the mismatch but cannot infer the artist's intended relocation.

For content that must travel across unrelated UV layouts, prefer procedural placement, triplanar
Fill layers, or a destination-specific mask painted after application.

### Mesh-dependent content

Mesh-map generators rerun using the destination surface when the required plugin is installed. This
is powerful for edge wear, cavity dirt, skin variation, and other geometry-aware treatments, but the
result can differ substantially on another silhouette or topology. Inspect the complete model,
especially thin pieces, overlapping surfaces, seams, and high-curvature details.

### Plugin compatibility

The preset records each plugin id and saved version. When the plugin is available, Overlay Painter
reruns it using the saved parameters. A different installed version produces a warning because the
algorithm or defaults may have changed.

When a plugin is missing, the last cached pixels remain visible and the layer or mask is marked
stale. This preserves an appearance fallback, but the receiving artist cannot regenerate that layer
until the plugin is installed.

### Resolution changes

Cached pixels and masks are resized to the destination working resolution. This makes a preset usable
between, for example, 2K and 4K targets, but resizing cannot create missing detail. Fine raster work
saved at low resolution may soften at higher resolution; high-resolution presets can also be large.

--------------------------------------------------------------------------------

## Work with an applied preset

An applied preset arrives inside a named wrapper group. Treat that group as the local instance of the
material.

Useful first adjustments include:

- Reduce group opacity to blend the whole treatment with the existing material.
- Add or repaint the group mask to control regional placement.
- Open individual Fill layers and change colors, textures, or projection settings.
- Tune per-channel opacity when color is correct but roughness or normal detail is too strong.
- Regenerate Plugin layers after changing their parameters or lower layers.
- Hide layers one at a time to understand how the preset is constructed.
- Add target-specific paint and cleanup above the preset group.

Applied layers display **Material Preset Revision** provenance in their properties. Use **Select** to
locate the source preset asset when it is available in the project.

Because the applied copy is independent, these changes do not alter the source preset. Save the paint
document to make the applied and edited layers durable.

--------------------------------------------------------------------------------

## Update and version presets

### Update an existing preset

To replace a preset's saved content:

1. Open the authoritative source paint document.
2. Make and review the changes.
3. Regenerate all procedural layers and masks.
4. Use the appropriate save command and choose the existing preset path.
5. Confirm **Update Material Preset**.

The preset revision increments. Existing applied copies retain their original content and recorded
revision. They are not automatically synchronized.

### Decide between an update and a new preset

Update the existing preset for:

- Corrections that preserve the same artistic purpose.
- Improved defaults or cleaner layer organization.
- Better cached output from the same compatible plugin behavior.

Create a new preset for:

- A materially different look.
- A breaking UV-family or shader-family change.
- A different channel contract.
- A result that existing productions may need to keep unchanged.

Use clear version or family terms when compatibility matters, such as `Hero Skin Subtle v2`,
`Leather - HDRP MaskMap`, or `Body UV3 - Shaved Stubble`.

### Replace an applied copy

To adopt a newer preset revision in a paint document, apply the newer preset again, compare the two
wrapper groups, transfer any local masks or adjustments, then delete the older group. This explicit
workflow avoids silently changing approved art.

--------------------------------------------------------------------------------

## Package a preset for distribution

An ordinary Material Preset references source assets elsewhere in the project. That is convenient
while authoring, but those references can break when only the preset `.asset` is copied.

Packaging creates a new self-contained Material Preset asset and embeds packageable dependencies,
including:

- The preset thumbnail.
- Textures and Sprites.
- `OverlayDataAsset` sources.
- Brush presets.
- `UMAMaterial` assets.
- Unity Materials.
- Fonts.
- Externalized preset pixel payloads, which are inlined into the package.

Repeated references to the same asset share one embedded copy.

### Create a package

1. Save and validate the source Material Preset.
2. Select it in the Project window.
3. Review its metadata, layer count, channels, portability, and plugin dependencies.
4. Click **Package** in the Inspector. A packaged preset uses **Repackage** for creating another
   package.
5. Choose a new, unused `.asset` path below `Assets`.
6. Inspect the resulting asset's package summary and external-dependency warning.
7. Test the packaged preset through Overlay Painter's ordinary Material Preset picker.

Packaging creates a new asset with its own preset identity; it does not overwrite the source preset.
Keep the source preset and its paint document as the editable masters.

### Dependencies that remain external

Plugin implementations and shaders remain project-level prerequisites. Unsupported reference types
also remain external. The packaged preset Inspector lists external dependencies that could not be
embedded.

A package is therefore self-contained with respect to its embedded art assets, not necessarily its
rendering code. Install the required UMA content, plugins, shaders, and render-pipeline support in the
destination project before validating it.

### Source control and delivery

For an ordinary source preset, commit the preset and every referenced source asset with their Unity
metadata. For a packaged preset, the single `.asset` contains its embedded art dependencies, but the
destination project still needs every listed plugin and shader prerequisite.

Test a delivery in a clean project or content branch. A package that works only because the authoring
project still contains loose sources has not been fully validated.

--------------------------------------------------------------------------------

## Artist-friendly stack design

### Use one clear root group

Build reusable material content under one descriptively named group whenever practical. This makes
the group easy to save, apply, mask, compare, and remove.

Good names describe purpose rather than implementation:

- `Waxed Canvas - Worn`
- `Skin - Shaved Stubble`
- `Steel - Painted and Chipped`
- `Creature - Dry Scales`

Avoid names such as `Group 4`, `Generator Copy`, or `Final Final`.

### Order layers by material logic

A dependable order is:

1. Base color and broad material response.
2. Primary surface structure.
3. Medium-scale variation.
4. Geometry-aware wear or buildup.
5. Localized dirt, damage, or biological detail.
6. Final grading and subtle breakup.

Filters must sit above the content they read. If a filter should affect only one component, place the
component and filter inside their own group rather than letting it process the entire material below.

### Keep channels physically coherent

Coordinate channels so they describe the same surface event. A scratch that brightens Albedo may
also reduce roughness, alter Normal Control, and expose metal, depending on the material. A skin pore
usually needs subtle normal and roughness response rather than a dark painted dot.

Do not add channels merely because they are available. Unnecessary channels make presets larger,
increase compatibility warnings, and make the material harder to art-direct.

### Make intensity easy to control

Use group opacity for a global amount control and per-channel opacity for physical balancing. Give
major features their own layers so the receiving artist can reduce or disable them without editing
pixels.

### Preserve a good fallback

Regenerate every Plugin layer and plugin-generated mask before saving. The cached result becomes the
fallback when the plugin is missing or a source cannot be regenerated. Never release a preset whose
visible cache is stale, failed, or from an obsolete parameter state.

--------------------------------------------------------------------------------

## Production examples

### Reusable fabric material

A broadly reusable fabric preset might use:

1. A multi-channel Fill layer for base color and roughness.
2. **Cloth Texture** for weave and normal response.
3. A low-opacity color variation layer.
4. **Fabric Fuzz** for fiber response.
5. A mesh-aware dirt or edge-wear layer.

Keep fitted logos, seams, and garment-specific grime outside the base preset. Add them in a second
group after applying the material.

### Facial or shaved-head stubble

A stubble preset can combine **Stubble Maker**, beard or scalp shadow, redness, spots, and a group
mask. Procedural hair parameters may transfer broadly, but a hand-painted face/scalp placement mask
is UV dependent. Name the supported body and UV family in the description and tags.

Apply it to a compatible skin material, then tune Albedo, roughness or smoothness, and normal strength
under the actual skin shader and lighting. Keep redness on its own layer so it can be reduced for
different skin tones.

### Scar or wound treatment

A scar preset can contain an editable Path layer plus Albedo, Normal Control, and smoothness or
roughness changes. Path placement and painted masks are target dependent, so treat the preset as a
starting construction rather than guaranteed placement.

After application, edit the path on the destination surface, check deformation areas, and inspect
the result under grazing light.

### Painted and chipped metal

Use a base metal response, a paint Fill group, mesh-aware edge damage, exposed metal, rust, and dirt.
Keep metallic values physically meaningful: intact paint and rust are normally non-metallic, while
exposed substrate metal is metallic. Let roughness, normals, and AO carry surface response instead of
baking dramatic lighting into Albedo.

--------------------------------------------------------------------------------

## Best practices

- Save from an authoritative paint document, not an unsaved experiment.
- Review the material in both the 2D UV view and 3D production lighting.
- Regenerate all stale Plugin layers and masks before capture.
- Remove hidden scratch layers and unused channels before saving an entire stack.
- Use a descriptive root group, layer names, display name, description, category, tags, and thumbnail.
- State the intended shader family, body/garment UV family, and required plugins in the description.
- Prefer Fill, triplanar, and procedural layers for broadly portable materials.
- Keep painted masks, paths, fitted decals, and UV-specific cleanup in separate target-family presets.
- Test at the lowest and highest working resolutions you intend to support.
- Test on a target with the same channel layout and on at least one representative alternate target.
- Check normal convention, roughness/smoothness meaning, and packed-map channel mappings.
- Use group and per-channel opacity to expose useful artistic controls.
- Keep filters directly above the content they are intended to read.
- Treat plugin-version warnings as a reason to revalidate the appearance.
- Update an existing preset only when its artistic contract remains compatible.
- Keep the source document and source preset; distribute a separately tested packaged preset.
- Save the destination paint document after applying and customizing a preset.

--------------------------------------------------------------------------------

## Troubleshooting

### Save Entire Stack is disabled

There is no active target or the active target has no layers. Select the intended paint target and
create or restore at least one layer.

### The preset does not appear in the picker

Confirm that it is a saved `TexturePaintMaterialPreset` asset below `Assets`, not a paint document,
BrushPreset, exported overlay, or ordinary Unity Material. Allow Unity to finish importing the asset.

### A channel was skipped

The destination material does not expose that semantic channel. Inspect the destination
`UMAMaterial` and its **Overlay Painter Channel Layout**. If the material intentionally lacks the
channel, the skipped result is expected.

### Painted details appear on the wrong surface region

The destination uses a different UV layout. Raster paint, masks, and path output retain UV-space
placement. Apply the preset to the intended UV family, repaint the placement mask, or rebuild the
feature with a more portable procedural or triplanar approach.

### The material looks different on another mesh

Mesh-aware generators use the destination geometry and mesh maps, so edge wear, cavities, scale, and
silhouette response can change. This may be correct procedural behavior rather than a transfer error.
Tune the generator on the destination and check for plugin-version differences.

### A Plugin layer is stale after application

The plugin is missing, failed, or could not be regenerated. Overlay Painter retains the saved cached
output. Install the required plugin and compatible version, inspect its parameters, then regenerate.

### A Fill layer reports a missing source

The preset references a Texture, Sprite, or `OverlayDataAsset` that is no longer available. Cached
pixels are retained when possible, but the layer cannot regenerate correctly. Restore the source or
use a packaged preset containing it.

### Applying was canceled or failed

Material Preset application is transactional. Overlay Painter removes the wrapper and all layers
created by the incomplete operation. Resolve the warning or generator error and apply again.

### Updating a preset did not update existing layers

This is intentional. Applied stacks are independent copies. Apply the new revision, compare it with
the old wrapper group, transfer local edits, and remove the older copy when approved.

### The preset asset is unexpectedly large

Material Presets include compressed raster pixels and masks for every saved authored channel. Large
working resolutions, many raster layers, and multiple cached procedural channels increase size.
Remove unused content, split a monolithic stack into focused presets, and author at the lowest
resolution that preserves the required detail.

### Packaging fails

Confirm that the source preset is a saved asset and the destination is a new `.asset` path below
`Assets`. Packaging does not overwrite an existing asset. Review the Console for a dependency that
could not be cloned, then remove, replace, or deliberately leave that dependency external.

### A packaged preset still reports requirements

Packaging embeds supported art assets, not plugin implementations or shader code. Install the listed
external prerequisites in the destination project.

--------------------------------------------------------------------------------

## Release checklist

Before sharing a Material Preset:

- [ ] The stack has a clear root group and readable layer names.
- [ ] Hidden experiments and unused channels were removed.
- [ ] All generators and plugin masks were regenerated successfully.
- [ ] Cached output matches the current parameters.
- [ ] Albedo and data channels are physically coherent.
- [ ] Display name, description, category, tags, author, and thumbnail are complete.
- [ ] Intended materials, UV families, render pipeline, and plugin requirements are documented.
- [ ] Same-layout and alternate-target compatibility were tested.
- [ ] Supported working resolutions were tested.
- [ ] The source preset revision is intentional.
- [ ] The package was created from the approved source preset.
- [ ] External plugin and shader requirements were reviewed.
- [ ] The packaged preset was applied successfully in a clean destination project or content branch.
