# Bone Pose Builder: An Artist's Guide

The Bone Pose Builder turns an authored rig pose into a reusable UMA bone-pose asset. Use it when you have already shaped the pose in Blender, Maya, another animation package, or a prepared Unity rig, and want to bring that work into UMA.

This guide walks through one pose first, then a library extracted from animation frames. For shaping poses directly on a character in Unity, continue with the [Bone Pose Editor artist's guide](BonePoseEditor.md). [Pose Tools](PoseTools.md) explains how these tools fit alongside the mixer, converters, and T-pose tools.

## In this guide

- [What a bone pose stores](#read-the-pose-as-a-change-from-a-reference)
- [Prepare your rigs and output folder](#prepare-your-source-material)
- [Build one pose from two rigs](#walkthrough-1-build-a-pose-from-two-rigs)
- [Extract a library from animation frames](#walkthrough-2-extract-a-pose-library-from-an-animation)
- [Current extraction limits](#current-extraction-limits-check-these-before-batching)
- [Clear poses and remembered state](#clear-poses-and-remembered-window-state)
- [Control reference](#builder-control-reference)
- [Troubleshooting](#troubleshooting-by-what-you-see)
- [Finish and hand off a library](#finish-and-hand-off-a-useful-pose-library)

## What you will make

The output is a `UMABonePose` asset: a collection of named bones and the changes to apply to them. A pose might lift eyebrows, curl fingers, open a jaw, adjust shoulders, or change a character's proportions.

The asset stores a shape, not an animation performance. It does not contain a timeline, an easing curve, or instructions to play itself. Another system supplies its strength and timing. A facial-expression setup, a DNA effect, or another pose consumer can use the same authored asset.

The Builder has two input routes:

| What you have | Builder section | What it produces |
|---|---|---|
| A neutral rig and a matching posed rig | **Pose Source** | One bone-pose asset representing their differences. |
| A clip containing several poses at known frames | **Animation Source** | One bone-pose asset per named frame. Read the current extraction limits before using this route. |

For a first exercise, use **Pose Source** and make a small, easy-to-recognize change. An eyebrow lift or a modest finger curl is easier to evaluate than a full-body pose.

## Read the pose as a change from a reference

Imagine the neutral rig as your clean drawing and the pose as the marks you add to it. The reference determines what counts as a change.

If the neutral rig already has raised shoulders, the Builder cannot know that those shoulders were supposed to be lower. It records the difference between the two inputs you supply. The same applies to an accidental wrist twist, a changed bone scale, or a rig imported with different settings.

When you inspect the result, the bone values have these meanings:

| Value | Neutral value in a bone pose | Artist interpretation |
|---|---|---|
| **Position** | `0, 0, 0` | No extra local movement. A value adds an offset relative to the bone's parent. |
| **Rotation** | No rotation change; displayed as zero angles | No extra local turn. This is not the bone's complete rest rotation. |
| **Scale** | `1, 1, 1` | Keep the original scale. `1.05` is a five-percent increase on that axis; zero is not a reset. |

These are local-space changes. A bone's local axes can differ from the scene axes, especially on mirrored limbs. Use the character's silhouette and deformation to judge a result rather than expecting every rig to use the same numeric sign.

## Prepare your source material

### Choose a specific target character

Start with the race and skeleton that will actually use the pose. Keep a built character available in Unity for review. Include the face, hands, or clothing that matters to the result.

A pose depends on more than names. Bone orientation, parent relationships, neutral proportions, and skin weights all affect its appearance. A successful pose on one race is a useful starting point for another race, not proof that the result transfers unchanged.

### Keep the neutral and posed rigs comparable

1. Make a neutral reference from the intended rig.
2. Duplicate that rig for posing. Give the reference and posed versions distinct asset names.
3. Preserve bone names and parent relationships.
4. Keep the same import scale, orientation, and root organization on both versions.
5. Pose the deformation bones that drive the mesh. If you animate control objects or constraints in a DCC package, bake the resulting motion to those bones before export.
6. Import both versions and check the actual bone transforms in Unity.

The single-pose route matches bones by name. Avoid repeated bone names in different branches: a name such as `Joint` repeated throughout a rig is not a dependable address for a pose. A shared humanoid mapping does not substitute for matching the bone names used by these assets.

Use separate neutral and posed prefab assets, or imported rig assets that expose the required hierarchy. **Pose Rig** is an asset field; it does not accept an arbitrary scene-only transform. If your posed result exists only in a scene, save an appropriate prefab containing that result first. Merely selecting a frame in an animation preview does not establish that the prefab asset stores those transforms.

### Set up an output folder

Create a folder such as `Assets/MyGame/Characters/Human/Poses/Face`. Keep source rigs and working animations in separate folders so it is obvious which assets are generated.

Choose short, descriptive pose IDs, for example:

- `Human_BrowRaise_Soft`
- `Human_JawOpen`
- `Human_HandRelaxed_Left`

The ID becomes the output filename. Use filename-safe names, and keep each ID unique within its output folder. Building the same ID again targets the same `.asset` path; the Builder does not generate a numbered version for you. Use another ID or another output folder when making an alternative you want to compare.

## Walkthrough 1: Build a pose from two rigs

### 1. Make a readable test pose

For this example, lift the brow bones a little on the posed rig while leaving the neutral rig untouched. Keep the head, neck, and root unchanged. Review the face from the front and from a three-quarter angle.

You should be able to answer, “Which bones did I change?” before building. That gives you a simple way to recognize unwanted entries later.

If your race has no dedicated brow bones, use a small change to a bone that exists, such as a finger joint. A bone pose cannot create a deformation control that is absent from the skeleton or missing skin weights.

### 2. Open the Builder

Choose **UMA > Tools > Pose Tools > Bone Pose Builder**. The window title is **Pose Builder**.

Assign the neutral rig's appropriate root to **Base Prefab**. Choose the root that contains the bones you want compared, not a mesh object with no bone descendants. Drag your destination folder from the Project window into **Pose Folder**.

The two sections below serve different workflows. Expand **Pose Source** for this exercise.

### 3. Assign the posed version

Set **Pose Rig** to the corresponding root in the posed asset. Enter `Human_BrowRaise_Soft` in **ID**, or use your project's naming convention.

Before proceeding, confirm:

- **Base Prefab** points to the neutral version.
- **Pose Rig** points to the posed version.
- Both roots contain the intended matching bones.
- **Pose Folder** is the folder where you want the result.
- **ID** names this result, rather than an older pose you meant to preserve.

**Build Pose** is unavailable until a base, a pose rig, and an ID are assigned. Assign an explicit output folder even if you used another workflow in this window earlier; do not depend on a remembered animation clip to determine the destination.

### 4. Build and inspect the asset

Press **Build Pose**. Find the new asset in **Pose Folder**, then select it.

The Bone Pose Editor appears in the Inspector. Expand **Pose Bones** and examine the names. In this example, the changed brow bones should be recognizable. If the list includes the root, hips, and many unrelated joints, stop and compare the two source rigs before refining the expression.

An empty result can be correct if both rigs are identical. If you expected a change, check that you assigned the posed asset rather than another copy of the neutral one. Look in the Console for unmatched bone messages.

### 5. Review it on the intended UMA

1. Keep the new pose asset selected.
2. In its Inspector, assign the built review character to **Source UMA**.
3. Open **Preview** and inspect the pose at full strength.
4. End any active bone or group edit before adjusting **Preview Weight**.
5. Compare weight `0`, a middle value such as `0.5`, and weight `1`.
6. Check the face from the front, side, and three-quarter views.

At zero strength you should see the editor's current preview baseline. At full strength you should recognize the intended brow lift. The halfway shape should also be useful: a pose that looks acceptable only at its endpoint may still be unsuitable for a facial slider.

Check that the eyelids do not open unintentionally, that the forehead does not pinch, and that the brows do not pull the nose or cheeks in an unwanted way. Those observations tell you whether to change the pose, the rig, or the skinning.

### 6. Decide where the final edit belongs

If the exported rig is wrong, correct it at its source and rebuild. If the extracted pose is a useful starting point and you want a Unity-specific variation, duplicate the result and refine that copy in the [Bone Pose Editor](BonePoseEditor.md).

Keep generated and hand-refined versions distinguishable. Otherwise, a later build to the same output path can replace your refinement.

## Walkthrough 2: Extract a pose library from an animation

This route is intended for an animation that acts as a pose sheet: one reference pose followed by identifiable samples. It reads stored transform curves at the frames you name. It does not play the Animator and capture everything that happens on screen.

### Current extraction limits: check these before batching

The UMA 3.1.5 implementation reviewed for this guide has important restrictions:

| Input or situation | What this means for your work |
|---|---|
| Humanoid muscle animation, runtime IK, or constraints | Playing correctly in Unity is not enough. The Builder reads bone transform curves, not the evaluated Animator performance. Prepare a baked transform clip or use the posed-rig workflow. |
| Rotation curves | The reader handles local quaternion rotation channels. Euler-only rotation curves are not handled by this reader. Ask your technical artist to confirm the baked clip's curve representation if extraction loses rotations. |
| **Base Prefab** assigned | Rotation-only changes can be omitted by the current change-detection code. The animation route also passes already calculated differences through another reference conversion, so offsets can differ from the intended sample. Treat this route as requiring careful validation, not an unattended production batch. |
| No **Base Prefab** assigned | The Builder visits bones found in the clip's position curves. A bone with only rotation or scale curves can be missed. A complete position/rotation/scale bake is needed for this route. |
| Blendshapes, animated materials, or other non-bone properties | They are not converted into bone-pose entries. Author those effects through the appropriate expression, DNA, or mesh tools. |

For a pose that fails this check, use **Pose Source** with a saved posed rig, or author it directly in the Bone Pose Editor. Changing a clip's Legacy flag alone does not bake missing curves or convert an Animator performance into the required data.

### 1. Prepare a neutral first frame and a frame sheet

The animation route takes time zero of the selected clip as its reference. Put the intended neutral shape there. If the clip begins with a smile, the extracted poses are changes from that smile.

Make each pose easy to find. For example, for a clip sampled at 30 frames per second:

| Clip frame | Time | Intended sample | Suggested ID |
|---|---|---|---|
| 0 | 0 seconds | Neutral reference | Usually no output row needed. |
| 15 | 0.5 seconds | Soft brow lift | `Human_BrowRaise_Soft` |
| 30 | 1 second | Strong brow lift | `Human_BrowRaise_Strong` |
| 45 | 1.5 seconds | Brow draw-in | `Human_BrowDrawIn` |

These are example frame choices, not preset controls in UMA. Make sure the selected clip actually extends to every frame in your list. Hold each target pose clearly while authoring, and sample the intended held frame rather than an interpolated transition.

Frame numbers are relative to the selected Unity clip. If an imported take was trimmed into a subclip, its first frame is the start of that subclip, not necessarily the original DCC timeline's frame zero. The Builder calculates sample time as `frame / clip frame rate`.

### 2. Check the clip against its rig

Preview the imported clip and verify the neutral reference and every target sample. Confirm that its bone paths match the rig root you intend to use. For example, `Armature/Hips/Spine` and `Hips/Spine` are different paths when measured from different roots.

The Builder is not a retargeter. If the clip belongs to another hierarchy, correct the source or its bindings before extraction. [Animation Utilities](AnimationUtilities.md) describes the binding-path tools; changing paths still does not make incompatible joint orientations equivalent.

If using a complete transform bake without **Base Prefab**, the Builder derives each output bone name from the final segment of its curve path. Distinct paths ending in the same bone name remain a problem.

### 3. Set up one sample first

1. Open **Animation Source**.
2. Assign the clip to **Pose Animation**.
3. Assign **Pose Folder** explicitly.
4. Decide whether this is a rig-matched extraction with **Base Prefab**, or a fully baked clip-only extraction with that field empty. Use the limitations above to make that choice.
5. Add one row using **+**, if needed.
6. Enter a meaningful **ID** and its clip-relative **Frame**.
7. Press **Build Poses**.

The button becomes available when a clip is assigned and at least one row has a nonempty ID. That is a basic readiness check, not confirmation that the frames, bindings, or source curves are valid.

### 4. Validate the sample before extending the list

Open the result in the Bone Pose Editor and repeat the review from the single-pose walkthrough. Compare it to the intended clip sample, especially the bones that only rotate.

Inspect both the visible shape and the bone list. A missing brow bone may be easy to spot, while a missing jaw rotation can make an entire expression appear weak. Unexpected shifts at the hips or finger bases can indicate a reference or offset problem.

If extraction is wrong, resolve the cause before adding dozens of rows. The current limitations table is part of this decision. Do not compensate for a broken extraction by adding arbitrary translations just to make a bone appear in the result.

### 5. Build the remaining rows

Once your chosen source and extraction route produce correct results, add the remaining ID/frame pairs with **+**. Use the **-** button on an individual row to remove that row. Then press **Build Poses** to process the list.

Rows with empty IDs are skipped with a warning. Frames before the clip begins or after it ends are also skipped. Review the Console and verify that the output folder contains every expected asset; a partially successful batch still needs attention.

### 6. Save the frame list as a pose set

Use **Save Pose Set** to write the ID/frame list to XML. Keep that file with the source clip or in a clearly named authoring folder.

The XML stores the row data. It does not package the animation, neutral rig, output folder, or generated pose assets. When handing it to another artist, include those dependencies and state the expected clip frame rate and neutral reference.

**Load Pose Set** replaces the current rows with the saved list. It does not append the list and does not automatically assign the right clip. Check **Pose Animation**, **Base Prefab**, and **Pose Folder** after loading.

## Clear poses and remembered window state

At the bottom of **Animation Source**, beneath **Build Poses**, **Clear poses** removes every animation row and returns the list's scroll position to the top. The empty list is remembered when the window is reopened. Press **+** to start another list, or **Load Pose Set** to restore saved rows.

This clears the extraction list only. It does not delete generated `UMABonePose` assets, change the animation clip, or clear the rig and output-folder assignments. Save a pose-set XML first if you want a reusable copy of the rows; the clear action has no separate recovery dialog.

The window also remembers its asset assignments, foldouts, and row state in editor preferences. That is a convenience for your workstation. Use XML and the source assets for a reproducible handoff to another person or project.

## Builder control reference

| Control | What you use it for |
|---|---|
| **Base Prefab** | Neutral comparison rig for **Pose Source**; bone hierarchy/path reference for the rig-based animation route. |
| **Pose Folder** | Destination for generated `.asset` files. Prefer a dedicated project folder. |
| **Debug Options** | Additional diagnostics for a selected bone and pose ID during rig-based animation extraction. |
| **Enable Debug Mode** | Turns those diagnostics on. It does not change the authored result. |
| **Debug Bone / Debug Pose** | Identify the bone name and pose ID to inspect in the Console. |
| **Pose Source > Pose Rig** | The asset containing the desired posed skeleton. |
| **Pose Source > ID** | Name of the single output pose. |
| **Build Pose** | Compare the two assigned rigs and create the single result. |
| **Animation Source > Pose Animation** | Clip supplying the reference and sampled frames. |
| **ID / Frame** | Output name and sample frame for one animation row. |
| **+ / -** | Add a blank row or remove that row. |
| **Load Pose Set / Save Pose Set** | Replace or export the ID/frame list as XML. |
| **Build Poses** | Process the animation rows using their IDs and frames. |
| **Clear poses** | Empty the animation row list. |

## Troubleshooting by what you see

| Symptom | Check first | Next action |
|---|---|---|
| **Build Pose** is greyed out | Base, posed asset, and ID. | Assign all three. Use an asset for **Pose Rig**. |
| **Build Poses** is greyed out | Clip assignment and a nonempty row ID. | Add a named row; then validate its frame separately. |
| No output where you expected it | **Pose Folder**, ID, Console messages. | Assign the destination explicitly and build one uniquely named sample. |
| A previous result changed after building | Reused output folder and ID. | Treat that pair as the asset's destination; use another name for a variant. |
| The output has no bones | Identical rigs, wrong sample frame, unhandled curves, or failed matches. | Check one visibly changed bone through the source and extraction process. |
| Translation appears, but rotation is missing | Current animation extraction limits. | Use a saved posed rig or the Bone Pose Editor for that pose. |
| The clip plays correctly but extraction is incomplete | Humanoid/constraint-driven animation or Euler-only curves. | Confirm a complete supported transform bake with your technical artist. |
| Many unrelated bones change | Mismatched neutral rigs, transforms, import settings, or clip baseline. | Compare the inputs before editing the generated list. |
| The pose shifts or rotates joints unexpectedly | Local axes, parent hierarchy, and the rig-based animation offset limitation. | Validate against a single known bone; switch to another authoring route if the extraction is incorrect. |
| The pose looks different on another race | Bone orientation, proportions, naming, and weights. | Make and name a race-specific version if needed. |
| Clearing the list did not remove assets | The list and generated assets are separate. | Manage unwanted output assets in the Project window deliberately. |

## Finish and hand off a useful pose library

Review each pose at low, middle, and full strength on the intended character. Look for a readable silhouette, natural pivots, comfortable skin deformation, and useful transitions. Check expressions with the eyes and mouth in relevant neighboring states, and check hand poses with the intended props.

Deliver the pose assets together with the source rig or clip, the pose-set XML if used, the target race, and a note identifying the neutral reference. Mark which assets are generated and which have been refined by hand.

Creating the asset does not automatically connect it to gameplay. Continue with [Dynamic Expression Player](DynamicExpressionPlayer.md) for expressions, [DNA Creation Guide](DNACreationGuide.md) and [New DNA System](NewDNASystem.md) for character-shape controls, or [Bone Pose Editor](BonePoseEditor.md) for further authoring and review.

## Version and source reference

This guide was checked against the UMA **3.1.5** source in `C:/GitHub/UMA/UMAProject`, configured for Unity **6000.3.26f1**. UMA supports Unity **6.3 and newer**. The animation extraction restrictions above describe this checkout; recheck them when upgrading.

Implementation references: [Builder](../Core/Editor/Scripts/UMABonePoseBuildWindow.cs), [bone-pose asset](../Core/StandardAssets/UMA/Scripts/UMABonePose.cs), and [Editor](../Core/Editor/Scripts/UMABonePoseEditor.cs). These are source-reviewed instructions, not a claim that every source rig and clip has been tested.
