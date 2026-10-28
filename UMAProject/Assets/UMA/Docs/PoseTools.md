# Pose Tools

UMA's Pose Tools help you author reusable expressions, hand shapes, body adjustments, and rig reference poses. Start with the character and the visual change you want, then choose the tool that fits your source material.

The two main authoring tools have their own detailed artist guides:

- [Bone Pose Builder: An Artist's Guide](BonePoseBuilder.md) walks from neutral and posed rigs to a finished pose asset, then through animation-frame extraction, pose-set XML, Clear poses, validation, and troubleshooting.
- [Bone Pose Editor: An Artist's Guide](BonePoseEditor.md) walks through a facial pose, grouped finger posing, IK, symmetry, previewing, merging, animation export, and using the result in a character setup.

## Choose Your Workflow

| Your starting point or goal | Tool to use |
|---|---|
| You already posed a rig in Blender, Maya, or Unity and have a matching neutral rig. | **Bone Pose Builder > Pose Source**. |
| You have an animation containing named key poses at known frames. | **Bone Pose Builder > Animation Source**. Read its current extraction limitations before batching. |
| You want to shape a pose directly while looking at a built UMA. | **Bone Pose Editor**, shown when a bone-pose asset is selected. |
| Several bones need coordinated local adjustments. | **Bone Groups** in the Bone Pose Editor. |
| A hand or foot needs to reach a position before you refine its joints. | **Use IK Editor** in the Bone Pose Editor. |
| You want to combine existing poses at chosen strengths. | **Bone Pose Mixer**. |
| You need to correct the race's underlying reference T-pose. | **Edit Selected Character Race T-Pose**, or the Editor's **TPose Tools**. |

A `UMABonePose` stores changes to named bones; an `UmaTPose` stores a rig reference pose; an `AnimationClip` stores animation curves. Keep their purposes separate when choosing what to save. A bone pose also needs an expression, DNA, or other consumer to apply it outside the authoring preview.

## Bone Pose Builder

Open `UMA > Tools > Pose Tools > Bone Pose Builder`.

Use the Builder to bring poses you have already authored into UMA. For a first attempt, make one small, recognizable change on a copy of your neutral rig, such as lifting a brow or curling a finger.

1. Assign the neutral rig root to **Base Prefab** and a dedicated destination to **Pose Folder**.
2. Expand **Pose Source** and assign the corresponding posed asset to **Pose Rig**.
3. Give the result a descriptive **ID**, then press **Build Pose**.
4. Select the generated asset to open the Bone Pose Editor in the Inspector.
5. Assign a built **Source UMA** and compare the shape at zero, partial, and full preview strength.

The two rigs need matching bone names and compatible parent relationships, local orientations, and import settings. The pose records differences from the reference you supplied; it cannot decide which differences were intentional. Keep generated outputs distinguishable from versions you refine by hand, because rebuilding the same ID in the same folder targets the same asset path.

For a pose library, **Animation Source** supplies the clip and an **ID / Frame** list. **Save Pose Set** writes that list to XML, and **Load Pose Set** replaces it with a saved list. **Clear poses**, beneath **Build Poses**, empties the rows while leaving source assignments and generated assets in place. The empty list survives reopening the window; use **+** to begin again.

Animation extraction reads stored transform curves relative to the clip's time-zero reference. It does not capture an evaluated Animator/IK performance. The current implementation also has rotation-only and reference-conversion limitations, so validate one sample before building a library. Follow the [complete Builder walkthrough and current limitations](BonePoseBuilder.md) for source preparation, supported inputs, frame numbering, output naming, and troubleshooting.

## Bone Pose Editor

Select a `UMABonePose` asset to display its editor in the Inspector. To start a new one, choose **Assets > Create > UMA > DNA > Bone Pose** in your pose folder.

Use a built review character of the intended race. **Source UMA** supplies its skeleton for editing and preview; an unbuilt avatar cannot supply an editable bone list. A separate **Preview Target** is optional and supports rebuild-based review of an already configured character setup.

1. Select or create your pose asset, then assign **Source UMA**.
2. In the lower **Bone List**, find the bones that drive the area you want to shape. Choose **Add Selected**, **Add + Mirror**, or the individual **Add Bone** control.
3. In the upper **Pose Bones** list, expand a row and choose **Move**, **Rotate**, or **Scale** under **Scene handles**. You can also enter numeric values directly.
4. Begin with small changes. Position zero, no rotation change, and Scale one are the neutral pose values.
5. Check **Disable Mirroring** and **Mirror Axis** before a paired edit. Evaluate both sides after a small test move.
6. End the active bone/group edit and leave IK mode before adjusting **Preview Weight**. Review the silhouette and deformation at several strengths and camera angles.
7. Save the pose asset, leave scene editing, and connect it to its intended expression or DNA setup.

The upper row checkboxes choose group members and linked Scene view translation; they do not enable or disable the bones. **Disable / Enable** on a row controls whether its stored contribution is applied. The lower tree's selection chooses bones to add. Learning these three different controls prevents accidental multi-bone edits.

The Editor also supports named groups, an IK posing mode, donor-rig comparison, and static animation export. **Merge Pose** copies source entries in list order, with later values winning on overlapping bones; use the Mixer for weighted combinations. The visible **Mirror to opposite** command is currently a placeholder. The [complete Editor guide](BonePoseEditor.md) explains the working alternatives and includes separate walkthroughs for a facial pose, a relaxed hand, and a reach toward a prop.

## Bone Pose Converter

Open `UMA > Tools > Pose Tools > Bone Pose Converter`.

Configure independent axis mappings and inversion for position and rotation, validate the mapping, then drag `UMABonePose` assets into the queue. Conversion modifies each queued pose.

The converter creates a backup the first time and uses that backup as the source for later reconversions, preventing repeated conversion drift. `Restore` returns a pose from its backup. Use `Self-Test Axis Mapping` and inspect representative bones before converting a full library.

## Bone Pose Mixer

Open `UMA > Tools > Pose Tools > Bone Pose Mixer`.

Assign a rig prefab and add component poses with blend percentages. The tool can limit output to selected component bones and provides Left, Right, and Mirror helpers. `Build` creates a new combined `UMABonePose` in the selected folder.

Component order and percentages affect the result. Test mirrored naming conventions on the target rig.

## Bone Pose DNA Extractor

Open `UMA > Tools > Pose Tools > Bone Pose DNA Extractor`.

This advanced migration tool samples a generated UMA across DNA values and creates bone-pose data for a selected DNA converter. It expects a generated `UMAData`, resolvable race converters, and an output folder. The process creates temporary avatars and may generate many assets.

Use it for migrating converter-driven bone changes into pose-based data, not as a routine DNA editor. Run it on a clean branch and verify every generated pose.

## Expression Extractor

Open `UMA > Tools > Pose Tools > Expression Extractor`.

The window manages a GameObject, animation clip, expression folder, and named frame list, with load/save support for expression-set XML.

The current `Build Expressions` implementation samples and logs configured frames but does not yet create final `UMABonePose` expression assets. Treat it as a diagnostic and setup utility, not a complete production extractor.

## Bone Groups

For a step-by-step hand example and a reference covering membership, pivots, local axes, and linked translation, see [Shape a relaxed hand with Bone Groups](BonePoseEditor.md#walkthrough-2-shape-a-relaxed-hand-with-bone-groups).

In the Bone Pose editor, check bones in the upper pose list, then choose **Create bone group** and enter a name. Groups are saved in the bone-pose asset. Entering an existing name (case-insensitive) replaces its membership without changing the bones' current transforms. **Select members** restores a group's checkboxes for editing its membership; create it again with the same name to replace it.

Click a group to select it, then use its numeric controls or **Move**, **Rotate**, and **Scale** scene handles. Unity's W/E/R shortcuts also switch tools. Translation adds the same local offset to each member, rotation composes the same local rotation on each member, and scale multiplies each member's scale. Existing differences between bones are retained. Each bone uses its own pivot; the group does not orbit around a common center. Parent-child members still inherit transforms normally through their skeleton hierarchy. The first available member positions and orients the scene handle.

Group controls start at zero translation, zero rotation, and scale factors of one. They track relative adjustments, not the absolute transform of every member. Scale factors must be positive. Only explicitly listed members are edited, once each; automatic mirroring and linked translation do not expand the group. Disabled members' stored values are edited but remain disabled. Missing members are skipped and their names retained. Groups can overlap.

Group creation, replacement, removal, and transforms support Undo/Redo. Removing a group keeps the member transforms. Selecting or editing an individual bone returns to single-bone editing. Groups contain authoring metadata only: their changes are stored in the ordinary bone pose values, so playback never applies them twice. In a temporary race T-pose editing session, saving the T-pose saves the resulting transforms; group definitions belong to the temporary session, not the `UmaTPose` format.

## Extract T-Pose

Use `UMA > Tools > Pose Tools > Extract T-Pose`, `Assets > UMA > Extract T-Pose`, or the Animator/UMA avatar context command.

The tool extracts humanoid pose data from a selected model Animator or generated avatar and creates an `UmaTPose` asset, normally named with a `_TPose` suffix. Verify the Animator avatar is valid and the model is in the intended reference pose.

## Edit a Character's Race T-Pose

In a `UMABonePose` inspector, assign the built character as **Source UMA**, expand **TPose Tools**, and choose **Extract and Edit Race T-Pose**. Alternatively, select a built character and use **UMA > Tools > Pose Tools > Edit Selected Character Race T-Pose**; this does not require an existing bone-pose asset.

The window creates a temporary bone pose and an editable preview beside the source character. **Frame Preview** locates it in the Scene view. Expand a bone and choose **Move**, **Rotate**, or **Scale** under **Scene handles** to activate its gizmo. Unity's W/E/R tools also switch the active bone's gizmo. Numeric fields can be edited directly without first clicking Edit, including on a mirrored partner. Choosing a Scene handles button exits IK mode; otherwise turn off IK Editor before editing numeric values. Mirroring and IK remain available. The temporary pose starts with zero position offsets, identity rotations, and scale multipliers of one relative to the race's stored T-pose, independently of character DNA and animation. Resetting offsets returns to that extracted T-pose. Only bones belonging to the original T-pose can be saved; unmatched T-pose bones are retained unchanged.

**Save to Race T-Pose** writes back to the existing `UmaTPose` asset using Unity serialization. It preserves the asset reference, humanoid bone mapping, limits, stretch/twist settings, jaw setting, extracted human-pose metadata, and unmatched bones. Saves support Undo/Redo and refuse to overwrite a T-pose that changed outside the editing session. Multiple saves do not accumulate offsets.

**Cancel / Close** offers Save, Discard, or Cancel for pending edits. Closing disposes the temporary pose, skeleton, meshes, and materials. Script reloads recreate the preview and restore the draft against its original baseline. Finish the session before entering Play mode. The source character, Animator, meshes, and ordinary bone-pose assets are not modified by this preview. All races referencing the saved T-pose will use it on their next build; the editor does not automatically rebuild existing characters. Review the new reference pose with the race's animations after rebuilding.

This is an Edit-mode tool for built UMA characters with their own skeleton. External skeletons are rejected. The preview uses the character's existing mesh, blend-shape weights, and material textures with race T-pose bone transforms; it is not a new neutral-DNA mesh bake.

### Copy another race's T-pose

While editing a race T-pose, select a **Source race** and click **Copy from race**. This replaces the current draft with that race's local position, rotation, and scale for matching bone names. Continue editing the preview, then use **Save to Race T-Pose** to save to the original destination shown at the top. The source race is never modified. Humanoid mappings and other destination T-pose metadata remain those of the original race; this operation copies transforms, not skeleton topology or humanoid retargeting settings.

Destination bones missing from the source reset to their original session baseline. Source-only bones are ignored. Matching destination bones absent from the preview are copied too and included when saving; the status reports these separately. Bone group membership is retained, with its adjustment controls reset. Copy supports Undo/Redo and draft recovery after script reloads. Invalid or incompatible source poses leave the draft unchanged.

## Set Clip Generic and Set Clip Legacy

These menu commands toggle the selected `AnimationClip.legacy` flag off or on. They do not retarget animation curves or change hierarchy paths. Verify the clip in its intended Animator or legacy Animation component afterward.

## Pose Validation Checklist

1. Confirm source and target bone names and hierarchy.
2. Use dedicated output folders and source control.
3. Inspect a representative transform before batch conversion.
4. Test left/right mirroring explicitly.
5. Validate generated assets on all intended races.
6. Keep original animation, prefab, and pose sources until release verification passes.
