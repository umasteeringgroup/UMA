# Bone Pose Editor: An Artist's Guide

The Bone Pose Editor lets you shape a reusable pose while looking at a built UMA character. You can move, rotate, and scale individual bones, work symmetrically, organize bones into groups, or use IK to position a limb before refining the result.

This guide starts with a small expression and grows into grouped finger posing, IK, pose merging, and export. It is intended for artists working in the Unity Editor; you do not need to write code.

If you already have a posed rig or an animation containing the shapes you want, start with the [Bone Pose Builder](BonePoseBuilder.md), then use this guide to inspect and refine its output. The [Pose Tools overview](PoseTools.md) lists the related tools.

## In this guide

- [Prepare a review character](#before-you-start-prepare-a-review-character)
- [Make a soft brow raise](#walkthrough-1-make-a-soft-brow-raise)
- [Individual bones and control reference](#working-comfortably-with-individual-bones)
- [Finding bones and whole-pose operations](#finding-bones-and-keeping-a-pose-readable)
- [Symmetry and opposite-side poses](#symmetry-and-opposite-side-poses)
- [Shape a relaxed hand with Bone Groups](#walkthrough-2-shape-a-relaxed-hand-with-bone-groups)
- [Reach toward a prop with IK](#walkthrough-3-reach-toward-a-prop-with-ik)
- [Source preview and Preview Target](#source-preview-and-preview-target)
- [Donor rigs and merging](#bring-in-another-rig-or-another-pose)
- [Export a static animation](#export-the-visible-pose-as-an-animation)
- [T-pose tools](#t-pose-tools-choose-the-right-destination)
- [Reset and cleanup reference](#resetting-and-cleaning-up-without-losing-your-intent)
- [Evaluate and hand off the pose](#evaluate-the-pose-as-an-artist)
- [Troubleshooting](#troubleshooting)

## What you are editing

A `UMABonePose` contains named bones and changes to their local position, rotation, and scale. It can describe an expression, a hand shape, a body adjustment, or another reusable rig change.

The pose asset is separate from the character used to preview it. Changing the asset can affect every setup that references that asset. Duplicate an existing pose before making a new artistic variant.

The Editor appears in the **Inspector when you select a bone-pose asset**. You do not open a separate window named Bone Pose Editor from the Pose Tools menu for ordinary pose editing.

| What you want to do | Where to begin |
|---|---|
| Make a new pose directly in Unity | Create a bone-pose asset, assign **Source UMA**, then add bones. |
| Refine an extracted pose | Select the Builder's output, duplicate it if necessary, and assign a review character. |
| Make a named set of bones easier to edit together | Use **Bone Groups** after adding those bones to the pose. |
| Reach toward a prop or reposition a limb | Enable **Use IK Editor**, then refine individual joints. |
| Change the race's underlying reference T-pose | Use the separate race T-pose editing workflow. See [Pose Tools](PoseTools.md#edit-a-characters-race-t-pose). |

Creating or editing a pose does not automatically make a character use it in gameplay. Previewing, assigning it to a consumer, and checking it during animation are separate steps.

## Before you start: prepare a review character

### Work with a built UMA

Use a character whose body and skeleton have already been generated. An empty avatar component without a generated skeleton does not provide the bones the Editor needs.

For a predictable first session:

1. Work in Edit mode.
2. Use a dedicated review character of the intended race.
3. Give it the body shape and clothing relevant to the pose.
4. Build it through its normal UMA workflow and confirm that its mesh is visible.
5. Pause other preview systems that continually drive its bones, such as an animation preview or expression preview.
6. Arrange the Scene view so you can see the region you will edit.

For facial work, remove distracting headgear and frame the face. For hand work, include a representative prop and inspect the palm side as well as the back of the hand. For body adjustments, include the clothing most likely to expose deformation problems.

The ordinary pose editor previews on the assigned source character's skeleton and can rebuild that character while preparing or restoring its preview. Use a review character whose temporary appearance changes are acceptable. The isolated preview described in the race T-pose guide is a different workflow.

### Understand the three neutral values

| Pose field | Value for no change | What to watch while editing |
|---|---|---|
| **Position** | `0, 0, 0` | Movement along the bone's parent-local axes. Small facial offsets can have a large visual effect. |
| **Rotation** | No added rotation, shown as zero angles | The joint's pivot and the shape of the skin around it. This is a rotation change, not its complete rest orientation. |
| **Scale** | `1, 1, 1` | Volume and proportion. `1.05, 1.05, 1.05` increases scale by five percent; `0, 0, 0` collapses it. |

The actual neutral bone may have nonzero position and rotation. Zero values in the pose mean “add no change to that bone,” not “move the joint to the scene origin.”

Parents affect their descendants. Rotating a shoulder can move the hand even when the hand has no entry in the pose. Adding an entry for every descendant is unnecessary unless you want an additional local change there.

## Walkthrough 1: Make a soft brow raise

Use this as a small first project. If your rig has different facial controls, substitute a pair of bones that visibly drive the area you want to shape.

### 1. Create and name the asset

In the Project window, select your pose folder and choose **Assets > Create > UMA > DNA > Bone Pose**. Name the new asset something descriptive, such as `Human_BrowRaise_Soft`.

The menu is under **DNA**, but the resulting bone-pose asset can also be used by expression and other pose systems. Its menu location does not assign it to a DNA slider.

If you are refining an existing asset, duplicate that asset instead. Keep the source and refined versions clearly named.

### 2. Assign Source UMA

Select the pose asset. In its Inspector, drag the built character from the Hierarchy into **Source UMA**.

**Find UMA in scene** can locate a character for you, but it does not ask which one you mean when several are present. Explicit assignment is easier to verify in a comparison scene.

Keep **Lock the bone pose editor** enabled while you work if you want scene interaction to stay with the pose editing session. This protects scene selection during editing; it does not make the asset read-only. Later, **Exit Bone Pose Editing** in the Scene view or unlocking the editor releases that protection.

### 3. Find the bones in the lower Bone List

There are two lists with different jobs:

| List | What it contains | What selection means |
|---|---|---|
| **Pose Bones**, higher in the Inspector | Bones already stored in this pose. | Row checkboxes choose group members and linked scene translation. Expanding a row exposes its values. |
| **Bone List**, lower in the Inspector | Available bones from the source skeleton. | Tree selection chooses bones to add to the pose. |

Use the lower list's search field to find the relevant facial bones. Expand branches if needed. Your rig's naming may use `Left...`/`Right...`, `_L`/`_R`, or a different convention; use the actual skeleton names rather than inventing names from the example.

Select the brow bone and choose **Add + Mirror** if the opposite bone follows a supported naming convention. Otherwise, select both intended bones and use **Add Selected**. The individual **Add Bone** dropdown is another way to add one bone at a time.

Return to **Pose Bones**. The new entries should be present with neutral values. Adding a bone does not, by itself, move the character.

### 4. Make the first change on one side

For your first adjustment, turn on **Disable Mirroring** so you can see exactly which bone is responsible for the deformation. Leave **Use IK Editor** off.

1. Expand one brow row.
2. Click **Focus** to bring that joint into view.
3. Under **Scene handles**, choose **Move**.
4. Drag a small amount in the direction that lifts the brow on this rig.
5. Orbit the Scene view and check the change from another angle.

You can also edit **Position** numerically. Numeric fields are editable without first pressing **Edit**; changing a value selects that bone for editing. Use small changes and Undo while learning the rig's local axes.

Observe the skin, not just the handle. A raised joint that pulls the eyelid or stretches the temple may be the wrong control, too large an offset, or a skin-weight issue.

### 5. Establish symmetry deliberately

Turn off **Disable Mirroring** when you are ready to edit both sides. Choose the appropriate **Mirror Axis** and make a small test adjustment. Check the opposite brow before continuing.

The available axis options are **X Axis**, **Y Axis (Legacy UMA)**, and **Z Axis**. Their usefulness depends on the rig's local orientation. Do not assume that the screen's horizontal direction identifies the correct setting.

When an opposite pose row is recognized, **Mirror** turns its mirroring on and **Mirroring** turns it off. Automatic mirroring can also create an opposite entry for supported names. Inspect the resulting bone list so you know what the pose now contains.

The two brows should now respond as intended. If they travel in unexpected directions, undo the test change and correct the mirror setup before sculpting the expression further. The full mirroring reference below explains naming and current limitations.

### 6. Evaluate the expression between neutral and full strength

Open **Preview**. **Preview Weight** controls how strongly the pose is applied to the source preview when you are not actively editing a bone or group.

An active bone/group edit displays the authored pose at full strength and disables the weight slider. End the active edit, and turn off IK if necessary, before trying to compare strengths. For an individual row, clicking **Editing** ends that row's edit; unlocking the editor also clears the active editing state. You can relock it afterward.

Compare three views:

- **0:** the current preview baseline without this pose's contribution.
- **0.5:** a useful intermediate expression.
- **1:** the complete authored expression.

The source preview baseline can include the character's existing setup. Weight zero does not promise a new neutral-DNA character or a reset of the race asset.

At each strength, check the brow silhouette, eyelid clearance, forehead pinching, and unintended cheek movement. Try both a close view and the distance at which the face will actually be seen in the game.

### 7. Finish the asset and leave the session

Pose edits change the selected asset. There is no separate ordinary-pose **Apply** button that commits a hidden draft. Save project assets through your normal Unity save workflow, then reselect the pose to review it.

Use **Exit Bone Pose Editing**, or unlock and leave the pose Inspector, when you want normal scene selection again. Ending the preview is not an instruction to discard the pose edits.

Finally, connect the pose to the intended expression or DNA setup and review it there. The closing sections explain that handoff.

## Working comfortably with individual bones

### Scene handles and numeric fields

Expand a row and choose **Move**, **Rotate**, or **Scale** under **Scene handles**. With the Scene view focused, Unity's **W**, **E**, and **R** tools also switch the active bone's handle. A shortcut typed into a text field will not act as a scene tool switch.

Use handles for visual exploration and numeric values for repeatable adjustments. Work from large structural changes to smaller refinements: shoulder before wrist, jaw before lower lip, finger base before fingertip.

Rotation fields display Euler angles for a stored rotation. Equivalent rotations can display differently, such as an angle near `360` instead of a small negative angle. Judge the orientation and use modest edits rather than expecting the numeric representation to remain identical after every operation.

### Per-bone controls

| Control | Artist use |
|---|---|
| Checkbox at the far left | Choose this bone for a group or linked Scene view translation. This is not the bone's enabled state. |
| Bone-name foldout | Show or hide Position, Rotation, Scale, and Scene handles. |
| **Focus** | Frame the corresponding source joint. It requires a matching source transform. |
| **P Target** | Frame the matching joint on the separately assigned Preview Target, when available. |
| **Disable / Enable** | Exclude or include this bone's contribution while retaining its stored values. Useful for comparing its effect. |
| **Edit / Editing** | Start or end individual-bone editing. |
| **Mirror / Mirroring** | Toggle the recognized opposite row's mirroring during an edit. |
| **Reset** | Return that row's pose values to neutral. Reset its partner separately when you want both sides neutral. |
| **O** next to a numeric channel | Reset that channel: zero Position, identity Rotation, or unit Scale. With mirroring active, the corresponding opposite channel can reset too. |
| **x** on a removable row | Remove that bone entry from the pose. It does not remove the bone from the skeleton. |

Disabling a bone is useful when you want to keep a candidate adjustment for comparison. Removing an entry is useful when it does not belong in the final pose. Resetting preserves an entry but removes its transform change.

### Scale with the deformation in view

For a small proportional adjustment, start with uniform scale. Non-uniform scale can distort animated joints and cause undesirable deformation as the hierarchy rotates. The Editor displays a warning when scale differs across axes.

Use non-uniform scaling only when it suits the rig, especially purpose-built adjustment bones, and inspect the result during the character's relevant animations. A pose that looks good in a still frame can stretch clothing or distort an elbow once the arm bends.

## Finding bones and keeping a pose readable

The upper **filter to bones containing:** field filters the displayed pose rows. **Fast filter** buttons provide shortcuts for `shoulder`, `arm`, `leg`, `spine`, `nose`, and `eye`. The adjacent **x** clears the filter.

The lower **Bone List** search filters the available skeleton tree. Its **Clear** button clears that search. It does not clear the pose. Tree selection and the upper row checkboxes serve different purposes.

**Bone Display Mode** controls the skeleton visualization in the Scene view: **Pose Bones**, **Filtered**, **All**, or **None**. Choose a view that lets you see the skin and the controls you need. It does not add or remove pose entries.

Several upper-list actions operate on the whole pose, even if a text filter hides some rows:

| Action | Effect |
|---|---|
| **Expand All / Collapse All** | Open or close all pose-row foldouts. |
| **Select All / Deselect All** | Set or clear the row checkboxes across the pose. |
| **Sort** | Reorder pose entries by bone name. |
| **Remove Unmodified** | Remove entries whose Position and Rotation are neutral and Scale is one. |
| **Reset All** | Set all stored bone transforms back to neutral without deleting their entries. |

Clear a filter and review the full list before a broad cleanup. In particular, **Remove Unmodified** does not mean “remove disabled bones.” A disabled entry can still contain a deliberate non-neutral adjustment.

## Symmetry and opposite-side poses

### Automatic pairing

The name-based helpers recognize **Left... / Right...** and **..._L / ..._R**. The skeleton context also attempts to identify mirrored transforms from hierarchy and local placement. Recognition is not a universal retargeting system for every naming scheme.

For a dependable symmetric workflow:

1. Add the intended pair and verify both names in **Pose Bones**.
2. Turn off **Disable Mirroring**.
3. Choose **Mirror Axis** for the rig.
4. Edit one bone and check whether the opposite row is identified.
5. Make a very small move or rotation and inspect both sides.
6. Only then continue the full adjustment.

For an asymmetric expression, turn on **Disable Mirroring** before editing the individual sides. Keep a symmetric source pose as a separate asset if you expect to return to it.

**Add + Mirror** adds counterpart entries; it is not a command to copy an existing finished pose to the other side. Adding entries and applying mirrored changes are different operations.

### Convert all Left/Right

**Convert all Left/Right** changes entries containing `Left` to `Right`, and entries containing `Right` to `Left`, while flipping their position/rotation changes according to **Mirror Axis**. It acts on the whole pose, including rows hidden by a filter.

Use it on a duplicate when turning a one-sided authored pose into an opposite-side variant. This particular command handles the literal `Left`/`Right` names; do not assume it supports every convention recognized by **Add + Mirror**. Check the destination names and deformation afterward.

### Mirror to opposite: current limitation

The visible **Mirror to opposite** button is a placeholder in the reviewed implementation. It does not currently create an opposite-side copy. Use paired editing with mirroring, or duplicate the asset and use **Convert all Left/Right** where its naming rules fit.

If the Editor reports **Mirroring plane not detected**, verify the actual pair and make a small test. **Mirror Axis** still controls mirrored pose differences when a mirrored entry is updated, but the message is a reason to inspect the result rather than assume pairing succeeded.

## Walkthrough 2: Shape a relaxed hand with Bone Groups

Groups are useful when several bones should receive a coordinated adjustment. A set of finger joints is a practical example: you can give them a shared curl, then refine the fingertips individually.

### 1. Add only the bones you intend to adjust

Start with a new or duplicated hand pose. Assign the built character as **Source UMA**, turn off IK, and decide whether this pose belongs to one hand or both.

In the lower skeleton list, select the finger joints you want and use **Add Selected**. For a first group, use a few joints whose local axes you understand. The thumb often needs a different treatment from the other fingers because its orientation and movement are different.

### 2. Create a named group

1. Expand **Pose Bones**.
2. Use **Deselect All** to remove old checkbox selections.
3. Check the rows that should belong to the group.
4. Press **Create bone group**.
5. Enter a name such as `LeftFingers_Relax`.

The group appears with a member count. Its definitions are saved in the bone-pose asset. The lower skeleton tree's highlighted selection alone does not establish group membership; the upper row checkboxes do.

### 3. Curl the group gently

Select the group and choose **Rotate**. Start with a small angle on the axis that curls these joints on this rig. Alternatively, adjust the group's numeric **Rotation**.

Watch the whole hand. Each member rotates around its own pivot and in its own local space. The group does not orbit all fingers around one shared pivot. If a joint bends sideways while another curls correctly, those local axes are different; refine that joint individually or use separate groups.

Parent-child inheritance still applies. If a finger's base and middle joint both receive a rotation, the fingertip reflects both changes. Start small and compare the palm-side silhouette to avoid an over-tight curl.

### 4. Refine outside the group

Click an individual bone's **Edit** or a **Scene handles** button to return to single-bone editing. Adjust spacing, fingertip clearance, and the thumb separately. Then reselect the group if you want another broad adjustment.

Groups keep existing differences between members. Their controls describe relative adjustments, starting from zero translation, zero rotation, and scale factors of one; they are not a readout of every member's absolute transform.

### 5. Update the membership

Use **Select members** to restore the group's checkboxes, change which rows are checked, then choose **Create bone group** again with the same name. An existing name is matched case-insensitively and its membership is replaced without resetting the bones' current transforms.

**Remove** beside a group removes its definition and leaves the bone transforms intact. It is not a way to undo the curl. Use Undo or edit/reset the affected transforms when you want to change the shape itself.

### Group behavior to remember

- Only explicitly listed members are edited, once each. Automatic mirroring and linked translation do not expand the group.
- Include both sides deliberately when a group should affect both sides.
- Disabled members' stored values can be edited by the group, but those members stay disabled.
- Missing members are skipped and their names are retained. Review a group after deleting pose entries.
- Groups can overlap. A bone shared by two groups receives the edits you make through each.
- Group scale factors must be positive.
- The first available member positions and orients the scene handle.
- Group creation, replacement, removal, and transforms support Undo/Redo.

The group is an authoring aid. Its changes are stored in the ordinary bone values; playback does not apply an extra group transform on top of those values.

### Linked translation without a named group

The same upper-list checkboxes also link Scene view translation while you edit an individual bone. This can help move a small set of controls together without defining a persistent group.

Treat those checkboxes as active editing selections, not just organizational marks. Clear them when you want an isolated move. Linked translation is not a general-purpose multi-bone rotation/scale tool; use a named group when you need that workflow. Parent-child motion still needs visual review.

## Walkthrough 3: Reach toward a prop with IK

IK lets you move an end joint and have its chain respond. Use it to establish a hand position or limb silhouette, then return to individual controls for the finishing pass.

### 1. Choose a manageable task

Start with a hand reaching toward a nearby prop. Keep the target within a natural range of the limb and frame the shoulder, elbow, and hand together. Turn on **Disable Mirroring** if you intend to pose only one arm.

Enable **Use IK Editor**. The Scene view shows round joint handles. **IK Handle Base Size** changes their visual size; it does not change the bones' influence or the pose strength.

Bones whose names contain `adjust` are excluded from the draggable IK chain. Use direct bone editing for those controls.

### 2. Limit how far the solve travels up the hierarchy

Without an explicit boundary, the tool chooses a natural branch/root boundary. If you need more control:

1. Enable **Affect down to:**.
2. Use **Boundary Quick Pick**, or **Boundary Filter** and **IK Boundary Bone**.
3. Choose a genuine ancestor of the joint you will drag, such as the intended arm-chain root.
4. Read the status message after moving the joint.

The boundary defines the root of the affected chain. It is not a target the hand should touch. If the selected boundary is not a valid ancestor, or is an adjustment bone, the tool reports that it is using the natural branch/root instead.

For an arm-only change, choose the relevant upper-arm/shoulder ancestor for that rig and verify that the torso stays as intended. For a broader body gesture, deliberately include a higher ancestor and inspect the additional movement.

### 3. Choose a movement plane

**Movement Plane** offers **Free**, **X/Z**, **Y/Z**, and **X/Y**. A plane constraint is useful when a front or side view makes depth hard to judge.

For constrained movement, **Plane Space** chooses **Global** scene axes or the dragged joint's **Local** axes. A global plane stays tied to the scene; a local plane follows the joint's orientation. It is unavailable for **Free** movement.

Choose a plane that fits the task, drag the hand handle, then inspect from another camera angle. A good screen-space placement can still put the hand in front of or behind the prop.

### 4. Refine the result

Watch the shoulder and elbow while dragging. Use several small movements rather than one large reach. IK establishes a chain solution; it does not guarantee an appealing elbow angle, natural wrist twist, or contact with the prop.

Turn off **Use IK Editor**, or choose a bone's **Move**, **Rotate**, or **Scale** scene-handle button, to return to direct editing. Most numeric pose controls are disabled while IK is active. Refine the wrist and finger rotations, check silhouette and intersections, then evaluate the pose at several strengths.

**Reset Skeleton** in the IK section resets the stored pose transforms and returns the source preview to its base pose. It is a broad reset of the pose, not merely a command to hide the IK handles.

## Source preview and Preview Target

**Source UMA** supplies the skeleton for editing and the direct pose preview. **Preview Target** is an optional `DynamicCharacterAvatar` to rebuild while you evaluate the result in a character setup.

| Preview control | What it does |
|---|---|
| **Preview Weight** | Changes the source pose-preview strength when no bone/group edit or IK mode is active. |
| **Preview Target** | Identifies an avatar for rebuild-based preview. |
| **Build Now** | Rebuilds that assigned target. |
| **Auto-Update Preview** | Requests target regeneration as pose edits occur. Turn it off if those rebuilds interrupt your editing. |
| **P Target** on a bone row | Frames the target's matching joint for comparison. |

Assigning a Preview Target does not automatically add this pose to that avatar's recipe, DNA, or expression setup. Configure the target to consume the pose through the intended system, then use rebuilds to review that configured result. If the target does not use the pose, rebuilding it will not establish that connection for you.

A helpful arrangement is one simple source character for authoring and another character with representative proportions and wardrobe for evaluation. Change one variable at a time when comparing them.

## Bring in another rig or another pose

### Generate Pose from Donor SMR

Use **Pose Generation** when you want a pose representing differences between the built source skeleton and a donor rig. **SMR** means **SkinnedMeshRenderer**: the mesh component that references the donor's bones. This workflow is useful when preparing rig adjustments for clothing remapping.

1. Create or duplicate a destination pose asset.
2. Assign the built character as **Source UMA**.
3. Arrange the donor rig in the reference state you intend to compare.
4. Expand **Pose Generation** and assign its renderer to **Donor SMR**.
5. Confirm that the donor has assigned bones and a compatible hierarchy.
6. Press **Generate Pose from Donor SMR**.
7. Read the Console's added/unmapped-bone report and inspect the pose on the source.

Generation replaces the destination's existing pose-bone list. Use a separate destination when preserving earlier authoring work. It compares current local bone transforms; an unintended animation frame or different reference orientation can become part of the result.

This creates bone adjustments. It does not transfer skin weights, rebuild the donor mesh, or guarantee compatible clothing. Continue the appropriate clothing workflow after checking the pose.

### Merge Bone Pose

Use **Merge Bone Pose** to copy entries from existing poses into the current asset:

1. Open the foldout.
2. Choose an asset in **Add Pose** and press **Add**, or use the list's drag-and-drop area.
3. Add the other source poses.
4. Use **^** and **v** to establish the intended order.
5. Press **Merge Pose**.
6. Review overlapping bones and the Console's updated/added counts.

The list is processed from top to bottom. If two sources contain the same bone, the later source replaces that bone's stored values in the destination. Bones absent from the incoming sources retain their existing destination entries.

For example, if `HandRelaxed` and `IndexPoint` both contain the index joints, place `IndexPoint` later when its values should win. Merge does not average their rotations or blend them at percentages. Use the [Bone Pose Mixer](PoseTools.md#bone-pose-mixer) for a weighted combination workflow.

Merge copies bone entries, including their enabled state. It is not a transfer of complete group definitions or every asset-level setting. **Clear** in the merge section clears the source list, not the current pose's bones; **X** removes an individual source from that list. Merge supports Undo.

## Export the visible pose as an animation

The **Save as animation** button is in the IK section. It creates an ordinary Unity `.anim` asset representing a static pose.

1. Assign a built **Source UMA** and establish the pose you want to export.
2. Ensure the desired pose bones are enabled and resolve to source transforms.
3. Enable **Use IK Editor** to expose **Save as animation**.
4. Inspect the visible pose again after switching modes. Do not assume a previously set preview/edit state is unchanged.
5. Press **Save as animation** and choose a project path.
6. Inspect the resulting clip on the intended hierarchy.

The export writes one key at time zero for local position, rotation, and scale on enabled pose bones with valid source transforms. The clip is assigned a frame rate of 60, but it does not contain a performance over 60 frames. It is a static pose snapshot, not the sequence of movements you made while authoring.

The clip uses hierarchy paths relative to a suitable source root. Applying it to another hierarchy may require compatible binding paths. The Console reports the root used for export.

Saving over an existing animation clip clears that clip's previous curves. Choose a new name when you want to preserve an existing animation. The resulting clip contains actual transform values, whereas a bone-pose asset contains differences; treat them as different deliverables.

## T-pose tools: choose the right destination

A race T-pose is a reference asset for the character's rig. An ordinary bone pose is an adjustment applied to a character. Decide which one you intend to change before using **TPose Tools**.

| Control | Result and intended use |
|---|---|
| **Extract and Edit Race T-Pose** | Starts a dedicated session with an isolated preview and explicit **Save to Race T-Pose**. Use this for deliberate reference-pose editing. |
| **Donor TPose** | Supplies the reference T-pose asset for the generation commands below. |
| **Generate TPose from this UMABonePose** | Creates a new T-pose based on the donor with this bone pose applied. |
| **Generate TPose from current source UMA pose** | Copies matching current source transforms in the Hips subtree into a clone of the donor T-pose. Requires a built source, a donor, and a resolvable Hips bone. |
| **Reset Race T-pose to Donor T-pose** | Assigns the donor T-pose to the source character's race and regenerates the character. This changes the race setup. |

Both generation commands open a project save dialog where you choose the destination and filename. Use a new name to preserve an earlier T-pose; choosing an existing T-pose path updates that asset. The Inspector's same-folder help text should not be read as a fixed output location. Follow the save dialog and the displayed result/status to locate your output.

Generating from the bone pose applies enabled entries with matching donor bone names. Generating from the current source copies matching bones under its Hips; donor bones outside that matching set remain as they were. Neither operation creates missing bones or changes the donor's hierarchy to fit a different rig.

For the complete isolated editing process, saving behavior, shared-race consequences, and **Copy from race**, follow [Edit a Character's Race T-Pose](PoseTools.md#edit-a-characters-race-t-pose). That workflow has an explicit draft/save boundary; ordinary bone-pose Inspector editing changes the selected pose asset directly.

## Resetting and cleaning up without losing your intent

Several controls use similar words but affect different things:

| Action | What changes |
|---|---|
| Channel **O** | That bone's position, rotation, or scale contribution returns to neutral; mirroring can affect the partner too. |
| Row **Reset** | That row's pose transforms return to neutral. |
| **Reset All** | Every pose entry returns to neutral; entries remain in the asset. |
| **Reset Skeleton** | Pose transforms are reset and the source skeleton preview is restored to its base. |
| **Reset UMA** | Restores the source preview's weights/skeleton and clears the Source UMA assignment. It is not a command to empty the pose asset. |
| **Remove Unmodified** | Neutral entries are removed from the pose. |
| **Remove Bone** or a row's **x** | Removes that entry from this pose. |
| Group **Remove** | Removes the group definition while retaining the bone values. |
| Merge-list **Clear** | Removes merge-source assignments. |
| Lower Bone List **Clear** | Clears the tree's search filter. |
| **Exit Bone Pose Editing** | Leaves scene editing/selection protection; it does not discard the asset's edits. |

Use Unity Undo as you make adjustments, and keep duplicate assets for named alternatives you want to compare later. Do not treat leaving the Inspector as a rollback operation.

## Evaluate the pose as an artist

### Look at shape, contact, and transition

For a face, review front, profile, and three-quarter views. Check eyelid contact, lip closure, teeth intersections, and whether the expression reads at game-camera distance. A large brow movement can be readable in a close-up but excessive during dialogue.

For a hand, look for natural knuckle arcs, finger spacing, thumb opposition, and a convincing relationship to the prop. Avoid checking only the view in which you placed the fingers.

For body adjustments, inspect silhouette and volume with representative clothing. Check a few relevant animated poses: overhead reach, bent elbow, seated hip, or a wide step, depending on what the asset changes.

### Test more than the endpoint

Review low, middle, and full strengths, plus other poses that will combine with this one. A beautiful full-strength expression may have an awkward halfway mouth shape. Two individually good poses may compete for the same joint.

Test the intended body shapes and races. Similar bone names do not guarantee identical pivots, local axes, mesh weights, or proportions. Name a specialized version clearly when the art needs it.

### Separate pose problems from rig problems

If moving the right bone still folds the mesh badly, examine skinning and joint placement. If the bone does not exist, a pose cannot manufacture its influence. If an Animator or expression system overwrites the result, establish which system should control those bones before adjusting the art to compensate.

## Make the pose usable by the rest of the project

**Mixer Pose** is a hint that makes the asset available to the Race Wizard's Pose Creator. It is not required for ordinary editing or pose generation and does not change runtime behavior by itself.

To put the asset into use:

1. Decide whether it represents an expression, a character-shape control, a rig correction, or an animation snapshot.
2. Assign it through that feature's authoring workflow.
3. Set the expected strength and neutral/reference behavior there.
4. Rebuild or refresh the character as that workflow requires.
5. Test with the intended animations and other influences active.
6. Confirm the character still uses the asset after leaving the pose editor.

Use [Dynamic Expression Player](DynamicExpressionPlayer.md) for expressions, [DNA Creation Guide](DNACreationGuide.md) and [New DNA System](NewDNASystem.md) for DNA-driven shape work, and [Animation Utilities](AnimationUtilities.md) for clip cleanup or binding-path changes.

For handoff, include the target race, the reference character, the expected strength range, and any bones that intentionally overlap another pose. Keep the original extraction or DCC source with the project when future regeneration is likely.

## Troubleshooting

| What you see | Likely reason | What to do |
|---|---|---|
| No editable skeleton or populated bone tree | The source is missing or has not generated its skeleton. | Assign a built UMA and check that its mesh and bones exist. |
| The wrong character responds | Automatic scene search chose another UMA. | Assign **Source UMA** explicitly. |
| A new entry makes no visible difference | Added entries start neutral, the bone is disabled, or it has no relevant skin influence. | Make a small deliberate change and inspect that bone's enabled state and rig influence. |
| Numeric controls are greyed out | IK is active. | Turn off **Use IK Editor** or select a direct Scene handles tool. |
| Preview Weight is greyed out | A bone/group edit or IK mode is active. | End the active edit; then compare strengths. |
| The character changes but the Preview Target does not | The target is not configured to consume the pose, or rebuilding is disabled. | Check the target's pose assignment, then **Build Now** or **Auto-Update Preview**. |
| A move affects several bones | Mirroring, linked checkboxes, a selected group, or parent-child inheritance. | Inspect the active group and checkboxes; use an isolated edit to identify the cause. |
| The other side moves the wrong way | Mirror axis or bone pairing does not fit the rig. | Undo the test move, check names and local axes, then try a small controlled mirrored edit. |
| **Mirror to opposite** appears to do nothing | It is currently a placeholder. | Use paired editing or the documented whole-pose Left/Right conversion workflow. |
| A group bends some fingers sideways | Members use different local axes. | Split the group or refine those joints individually. |
| A group operation seems too strong | Parent and child members both receive changes. | Reduce the adjustment or change the membership. |
| IK affects more of the body than expected | The natural chain boundary is broader than intended. | Choose a valid ancestor using **Affect down to:** and inspect the status. |
| An adjustment bone cannot be dragged in IK | Adjustment bones are excluded from the IK chain. | Use its direct Move/Rotate/Scale controls. |
| Merging lost an earlier bone adjustment | A later source contained the same bone. | Undo, reorder the sources, or use the Mixer for weighted combination. |
| The exported animation has no motion over time | It is a static pose export. | Use it as a key-pose reference or author an actual animation separately. |
| A reset affected hidden rows | Whole-pose actions are not limited by the visible text filter. | Undo if needed, clear the filter, and inspect the complete pose before a bulk operation. |
| Scene clicks keep returning to pose editing | Scene-selection protection is active. | Use **Exit Bone Pose Editing** or unlock the editor. |
| The pose disappears after leaving the editor | You were seeing an authoring preview only. | Assign the asset to the intended expression/DNA/pose consumer. |

## Version and source reference

This guide was checked against the UMA **3.1.5** source in `C:/GitHub/UMA/UMAProject`, configured for Unity **6000.3.26f1**. UMA supports Unity **6.3 and newer**. Check your installed controls when moving to another version.

Implementation references: [Bone Pose Editor](../Core/Editor/Scripts/UMABonePoseEditor.cs), [group controls](../Core/Editor/Scripts/UMABonePoseEditor.Groups.cs), [editor context](../Core/Editor/Scripts/UMABonePoseEditorContext.cs), and [bone-pose asset](../Core/StandardAssets/UMA/Scripts/UMABonePose.cs). Workflows and limitations were reviewed against these files; visual quality still needs evaluation on your own rig.
