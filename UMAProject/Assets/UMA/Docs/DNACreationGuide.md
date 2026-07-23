# DNA Creation Guide for Artists

The UMA 3 DNA system lets artists build character customization sliders without writing code. A DNA slider can change bones, apply a pose, drive a blendshape, reshape a mesh, move an overlay, or adjust colors and material properties. One slider can control several of these effects at the same time.

The simplest way to think about the system is:

- A **DNA Group** is a section in the character customizer, such as `Body`, `Face`, or `Pose`.
- A **DNA** asset is one slider, such as `Height`, `Arm Length`, or `Nose Size`.
- A **DNA Effect** is one thing that slider changes.

For example, an `Arm Length` DNA can contain two Bone Scale effects: one for the left arm and one for the right arm. Moving one slider changes both sides together.

This is **live DNA**. The values belong to the current character and can be adjusted directly in the `DynamicCharacterAvatar` Inspector. They are not a separate list of values that is only copied onto the character when it first loads.

---

## Before You Start

Prepare a scene where you can see the result while you work:

1. Open a scene containing a `DynamicCharacterAvatar` (DCA).
2. Select the DCA and make sure it builds successfully.
3. Use a neutral A-pose or T-pose while making body and face DNA.
4. Confirm the DCA is using the race you want to edit.
5. Keep the Scene view visible beside the Inspector.

For bone effects, having a built DCA in the scene enables the `Pick Bone...` menu. It also lets the DNA editor rebuild the character as you change settings.

The UMA 3 sample DNA is under:

`Assets/UMA/UMA3/DNA/`

Useful examples include:

- `Body/height.asset`: one Bone Scale effect on the `Position` bone.
- `Body/armLength.asset`: matching Bone Scale effects for `LeftArm` and `RightArm`.
- `Face/noseSize.asset`: more than one bone effect controlled by one slider.
- `MaleBody.asset` and `FemaleBody.asset`: complete DNA Groups assigned to the `Body` area.

Inspect these assets before building a similar slider from scratch.

---

## Plan the Slider First

Before creating assets, decide what the slider should do at three positions:

| Slider position | Example result |
|---|---|
| 0 | Shortest arms |
| 0.5 | Original model |
| 1 | Longest arms |

Most body and face DNA uses `0.5` as the neutral value. This lets the slider move in two directions from the original model.

Some DNA is better as a one-way slider:

| Slider position | Example result |
|---|---|
| 0 | No pointed-ear blendshape |
| 1 | Full pointed-ear blendshape |

For one-way sliders, a default of `0` is usually easier to understand.

Also decide whether the DNA belongs under `Body`, `Face`, `Pose`, or another customizer section. This determines which DNA Group should contain it.

---

## Step 1: Create a DNA Group

A DNA Group collects related sliders and controls where they appear in the customizer.

1. In the Project window, right-click in your DNA folder.
2. Choose `Create -> UMA -> DNA -> DNA Group`.
3. Give the asset a clear name, such as `MyRaceBodyDNA` or `MyRaceFaceDNA`.
4. Select the new group.
5. Set `DNA Area` to the customizer section name you want, such as `Body`, `Face`, or `Pose`.
6. Leave `Max Total for Area` at `0` for normal groups.

### DNA Area

`DNA Area` is the heading artists and players see when editing a character. DNA Groups with the same area name appear under the same type of heading, so use consistent spelling and capitalization.

Good area names are short:

- `Body`
- `Face`
- `Head`
- `Ears`
- `Pose`
- `Fantasy Features`

### Max Total for Area

Leave this at `0` unless the sliders in the group are competing for a limited total.

For example, a stylized race might have three feature sliders that should never add up to more than `1`. Set `Max Total for Area` to `1`. Raising one slider then reduces other non-zero sliders in that group.

This reduction is shared evenly. It is not weighted, so do not use this setting for normal body or face groups unless that behavior is intentional.

---

## Step 2: Create a DNA Asset

Each DNA asset becomes one character slider.

1. In the Project window, right-click in your DNA folder.
2. Choose `Create -> UMA -> DNA -> DNA Item`.
3. Rename the asset immediately, for example `earSize`.
4. Select it to open the `DNA Editor`.
5. Set `Display Name` to the artist-facing label, for example `Ear Size`.
6. Add a useful `Description`, such as `Changes the size of both outer ears`.
7. Set `Default Value`.

### Naming Rules

The Unity asset name is the permanent identity of the DNA. Use a unique and stable name.

- Good asset name: `earSize`
- Good display name: `Ear Size`
- Avoid spaces and punctuation in the asset name.
- Do not reuse the same asset name in two groups on the same race.
- Avoid renaming a DNA after characters or recipes have been saved with it.

Use `Display Name` when you want to change what artists see without changing the DNA identity.

### Choosing the Default Value

- Use `0.5` when the original model is the center of a smaller-to-larger or left-to-right range.
- Use `0` when the effect should be off by default and increase in one direction.
- Test the default on a freshly built character, not only on a character whose DNA has already been edited.

---

## Step 3: Add the First Effect

The `DNA Editor` has an `Add New Effect Settings` section.

1. Expand `Add New Effect Settings`.
2. Choose an `Effect Type`.
3. Fill in that effect's fields.
4. Give it a useful `Effect Name`. For mirrored effects, names such as `Left Ear` and `Right Ear` are helpful.
5. Set the `Curve`, `Min`, and `Max` values.
6. Click `Add Effect`.

The effect appears under `Existing Effects`.

Use the controls in `Existing Effects` to:

- Expand or collapse effects.
- Enable or disable an effect while testing.
- Duplicate an effect, which is useful for left and right bones.
- Remove an effect.
- Select several effects and copy a curve to all selected effects.
- Select several Bone Scale effects and copy a scale factor to them.
- Turn on `Show Help` for a short description in the Inspector.

You can also drag another DNA asset onto `Drop DNA here to copy the effects to this DNA`. This replaces the current effect list with copies from the dropped DNA and is useful when creating a close variation.

---

## Understanding Curve, Min, and Max

Every effect receives the DNA slider value from `0` to `1`.

The **Curve** changes how quickly the effect responds. Think of it as the feel of the slider:

- A straight diagonal line gives an even response.
- A slow start keeps the effect subtle near the low end.
- A steep finish makes the strongest change happen near `1`.
- A flat section creates a dead zone where the effect does not change.

`Min` and `Max` set the output range used by the effect.

### Centered Body and Face DNA

For a neutral value of `0.5`:

- Use a curve that passes through the middle.
- Set `Min = -1` and `Max = 1`.
- At `0.5`, the mapped result is `0`, so the effect is neutral.

This is the normal setup for Bone Scale, Bone Translate, and Bone Rotate effects that need smaller and larger values around the original model.

### One-Way DNA

For an effect that is off at `0` and fully on at `1`:

- Use a straight curve from bottom-left to top-right.
- Set `Min = 0` and `Max = 1`.

This is common for blendshapes, mesh modifiers, color changes, and special features.

### Template Curves

Create reusable curve presets with:

`Create -> UMA -> DNA -> DNA Curve Mapping`

Assign one to `Template Curve` on an effect. Its curve, Min, and Max are copied into the effect. The effect does not keep a live link to the template, so later changes to the template do not update existing effects.

---

## Effect Quick Reference

| Effect | Use it for |
|---|---|
| `Bone Scale` | Making a bone region longer, shorter, wider, or thinner. |
| `Bone Translate` | Moving a bone in local X, Y, or Z. |
| `Bone Rotate` | Turning a bone around an axis. |
| `Bone Transform` | Combining position, rotation, and scale on one bone. |
| `Bone Pose` | Applying a prepared `UMABonePose` to several bones. |
| `Blend Shape` | Driving a named blendshape on the generated character. |
| `Mesh Modifier` | Applying an artist-authored mesh sculpt or vertex adjustment. |
| `Overlay UV Transform` | Moving, scaling, or rotating a named overlay. |
| `Shared Color` | Blending or combining a complete shared color. |
| `Shared Color Channel` | Changing one red, green, blue, or alpha component. |
| `Shared Color Property` | Driving a shader color or float property through a shared color. |

---

## How to Set Up Each Effect

### Bone Scale

Use Bone Scale for proportions such as limb length, head width, hand size, or torso thickness.

1. Choose `DNAEffect_BoneScale`.
2. Click `Pick Bone...` and select a bone from the built DCA.
3. Set `Scale Factor` for X, Y, and Z.
4. Use `Uniform` when all three axes should match.
5. For centered DNA, start with `Min = -1`, `Max = 1`, and `Default Value = 0.5`.
6. Click `Add Effect` and test the DNA on the character.

The scale factor controls how strongly each axis responds. Start with small values such as `0.1` or `0.2`. Large values can distort skinning and attached clothing.

For matching left and right limbs:

1. Create the left effect.
2. Click `Duplicate`.
3. Change the duplicate's bone to the right-side bone.
4. Keep both curves and scale factors the same.

Use `Select Bone in Hierarchy` to inspect the chosen bone on the character.

### Bone Translate

Use Bone Translate to change spacing or position, such as eye spacing, brow height, jaw position, or shoulder position.

1. Choose `DNAEffect_BoneTranslate`.
2. Pick the target bone.
3. Enter a small `Translation` vector.
4. Use `Min = -1`, `Max = 1` for movement around a neutral center.
5. Test both ends of the slider.

Translation uses the bone's local axes. A positive direction may not match world-space right, up, or forward. Use small values and watch the Scene view.

### Bone Rotate

Use Bone Rotate for ear angle, eye angle, jaw tilt, or other directional changes.

1. Choose `DNAEffect_BoneRotate`.
2. Pick the target bone.
3. Set `Rotation Axis`, such as `(1,0,0)`, `(0,1,0)`, or `(0,0,1)`.
4. Set `Rotation Angle` in degrees.
5. Use a centered range for two-way rotation or `0..1` for one-way rotation.

Start with a small angle such as 5 to 15 degrees. For mirrored bones, the right side may need the opposite axis or a negative angle.

### Bone Transform

Use Bone Transform when one DNA effect needs position, rotation, and scale together on one bone.

1. Choose `DNAEffect_BoneTransform`.
2. Pick the bone.
3. Enter the local `Position`, `Rotation`, and `Scale` values.
4. Test at several slider positions.

This effect is less forgiving than the separate Scale, Translate, and Rotate effects. Bone values are local to the rig, and several transforms can interact. Prefer separate effects when you only need one kind of change.

### Bone Pose

Use Bone Pose when a prepared `UMABonePose` should affect several bones at once. It is useful for race posture, foot angle, stylized proportions, or coordinated facial structure changes.

1. Create or choose a `UMABonePose` asset.
2. Choose `DNAEffect_BonePose`.
3. Assign the asset to `Bone Pose`.
4. Set `Is Base Pose` only when the pose should establish the starting skeleton shape before other DNA effects.
5. Set the curve and range to control pose strength.

Leave `Is Base Pose` off for an ordinary pose that should layer with the rest of the DNA. When several base poses touch the same bones, their order matters, so test the combined result carefully.

### Blend Shape

Use Blend Shape when the deformation already exists as a blendshape on the source mesh.

1. Choose `DNAEffect_BlendShape`.
2. Enter the exact `Blend Shape Name` from the mesh.
3. For a normal 0-to-100 percent blend, use `Min = 0`, `Max = 1`.
4. Test the character and all intended LODs.

The name must match exactly. The blendshape must survive UMA mesh generation, and every LOD that should respond needs a compatible blendshape.

### Mesh Modifier

Use Mesh Modifier for an artist-authored vertex sculpt, clothing correction, asymmetrical feature, or detail that is not convenient to build with bones.

1. Create and test the `MeshModifier` asset first.
2. Choose `DNAEffect_MeshModifier`.
3. Assign it to `Mesh Modifier`.
4. Use `Min = 0`, `Max = 1` for a one-way sculpt.
5. Use the curve to delay the sculpt until the upper part of the slider if needed.

Mesh modifiers require mesh regeneration and are heavier than simple bone effects. Confirm that the modifier targets the correct source slots and test it with wardrobe items.

### Overlay UV Transform

Use Overlay UV Transform to reposition tattoos, makeup, scars, decals, or pattern overlays.

1. Choose `DNAEffect_OverlayUVTransform`.
2. Enter the exact `Overlay Name`.
3. Set `Offset`, `Scale`, and `Rotation`.
4. Use the curve and Min/Max to control the strength.

Only the first matching overlay is changed. The overlay must be present in the character's final recipe. Test the low end carefully: the Scale values are multiplied by the mapped DNA strength, so values near zero can shrink the overlay dramatically.

### Shared Color

Use Shared Color to blend between colors or combine a color with an existing shared color such as skin, hair, or eyes.

1. Choose `DNAEffect_SharedColor`.
2. Enter the exact `Shared Color Name` used by the character.
3. Set `From Color` and `To Color`.
4. Choose a `Combination Method`.
5. Set `Texture Number` to the material channel you want.
6. Choose `Base Multiplier` or `Additive` for `Color Type`.

Combination methods:

- `Range`: blends from `From Color` to `To Color`. This is the clearest choice for most artist controls.
- `Additive`: adds more of `To Color` as the slider increases.
- `Subtractive`: subtracts more of `To Color` as the slider increases.
- `Multiply`: multiplies the colors together and can darken quickly.
- `Replace`: replaces the result with a scaled version of `To Color`.

`Base Multiplier` colorizes or darkens the texture. `Additive` brightens or adds color. Keep color values controlled and test under the project's normal lighting.

### Shared Color Channel

Use Shared Color Channel when only one component must change, such as opacity or the red component of a packed mask.

1. Choose `DNAEffect_SharedColorChannel`.
2. Enter `Shared Color Name`.
3. Choose `Red`, `Green`, `Blue`, or `Alpha`.
4. Set `Component Value`.
5. Set `Texture Number` and `Color Type`.

This replaces the chosen component while preserving the other three. It is best for materials designed around channel controls; it is less intuitive for ordinary color picking.

### Shared Color Property

Use Shared Color Property to drive a shader property stored with a shared color.

1. Choose `DNAEffect_SharedColorProperty`.
2. Enter the exact `Shared Color Name`.
3. Enter the exact shader `Property Name`.
4. Choose `Color`, `Float`, or `Both` for `Parameter Type`.
5. For Color, set `Zero Color Value` and `One Color Value`.
6. For Float, set the value reached at full effect.

Property names are case-sensitive. Confirm the shader exposes the property and that the UMA material supports it. The standard shader color parameter is `_Color`.

---

## Step 4: Add the DNA to Its Group

After the DNA asset has at least one effect:

1. Select the DNA Group asset.
2. Drag the DNA asset onto `Drag & Drop DNA assets here to add to group`.
3. Expand its entry to confirm the effect summary.
4. Click `Inspect` to reopen the DNA asset when it needs changes.
5. Click `Save Now` after a large editing session.

The group editor ignores duplicate asset references and sorts dropped DNA assets by name.

Use `Rebuild Characters` when an already open character has not picked up a group or DNA asset change.

---

## Step 5: Assign Groups to RaceData

The race decides which DNA sliders are available to its characters.

1. Select the race's `RaceData` asset.
2. Enable `Use New DNA System`.
3. Expand `DNA Collection`.
4. Drag your DNA Group assets onto `Drag DNA Groups Here`.
5. Use the `I` button beside a group to inspect it.
6. Use `X` to remove a group from the race.
7. Save the project.

Only assign the groups that make sense for that race. Two races may share a group if they use compatible bone names, blendshapes, overlays, colors, and mesh modifiers. Otherwise, make race-specific groups or DNA assets.

When `Use New DNA System` is enabled, the race uses the new DNA Collection instead of legacy DNA converters. Do not try to combine the two authoring systems on the same race.

---

## Step 6: Add and Edit Live DNA on a Character

Select a built DCA that uses the race.

1. Expand `Customization`.
2. Expand `Live DNA`.
3. Under `Add New DNA`, choose a `Group`.
4. Choose a `DNA` from that group.
5. Click `Add DNA Instance`.
6. Open the group's foldout and move the new slider.

The Live DNA controls work on the current character:

- The slider changes the character and rebuilds the necessary parts.
- The checkbox enables or disables that DNA on the character.
- `Def` returns the slider to the DNA asset's Default Value.
- `Edit` opens the source DNA asset so you can adjust its effects.
- `X` removes that DNA instance from the character.
- `Enable All` and `Disable All` are useful for comparing groups.
- `Force Full Rebuild` is useful when editing an effect that changes meshes, textures, or recipe content.

When the DNA asset is opened through the DCA's `Edit` button, changes to its effects can regenerate the character automatically. This is the best way to tune an effect: keep the DCA visible, edit the effect, and move the live slider through its full range.

### Live DNA Is Not Preloaded DNA

In the legacy workflow, Predefined DNA was a list of values applied during initial character creation. In the new system, the DCA Inspector labels this area `Live DNA` because you are editing the character's current DNA instances.

This means you can:

- Add a DNA slider to the current character.
- Change its value immediately.
- Disable it temporarily without deleting it.
- Open the DNA asset directly from the character.
- Save the character or recipe with the adjusted DNA values.

If the Inspector warns about old Predefined DNA, use `Clear old Predefined DNA` before continuing with the new system.

---

## Multi-Effect Example: Pointed Ear Shape

This example creates one `Pointed Ears` slider with four effects:

- Scale the left ear.
- Scale the right ear.
- Rotate the left ear outward.
- Rotate the right ear outward.

Use the actual ear bone names from your DCA. The example names below are placeholders.

### 1. Create the DNA

1. Create a DNA Item named `pointedEars`.
2. Set `Display Name` to `Pointed Ears`.
3. Set `Description` to `Lengthens and angles both ears`.
4. Set `Default Value` to `0` if the normal race has round ears, or `0.5` if it should support both rounded and pointed extremes.

### 2. Add Left Ear Scale

1. Choose `DNAEffect_BoneScale`.
2. Pick the left ear bone.
3. Set `Effect Name` to `Left Ear Length`.
4. Start with `Scale Factor = (0.15, 0.35, 0.15)` and adjust for the bone's local axes.
5. Use a straight curve with `Min = 0`, `Max = 1`.
6. Click `Add Effect`.

### 3. Duplicate for the Right Ear

1. Expand `Left Ear Length` under Existing Effects.
2. Click `Duplicate`.
3. Change the duplicate to the right ear bone.
4. Rename it `Right Ear Length`.

### 4. Add Left Ear Rotation

1. Choose `DNAEffect_BoneRotate`.
2. Pick the left ear bone.
3. Set `Effect Name` to `Left Ear Angle`.
4. Set the rotation axis that points correctly for this rig.
5. Start with `Rotation Angle = 10`.
6. Use the same curve, Min, and Max as the scale effects.
7. Click `Add Effect`.

### 5. Duplicate for the Right Ear

1. Duplicate `Left Ear Angle`.
2. Change the bone to the right ear.
3. Rename it `Right Ear Angle`.
4. Reverse the rotation axis or angle if the right ear turns inward instead of outward.

### 6. Test the Complete DNA

1. Add `pointedEars` to the appropriate Face or Ears DNA Group.
2. Make sure that group is assigned to the race.
3. Add `pointedEars` in the DCA's `Live DNA` section.
4. Test values at `0`, `0.25`, `0.5`, `0.75`, and `1`.
5. Check the character from front, side, and back.
6. Test hair, headwear, and all intended LODs.

This is the main strength of the new DNA system: one artist-facing slider can coordinate as many effects as the design needs.

---

## Artist Testing Checklist

Test every DNA before it is approved:

- The asset has a clear Display Name and Description.
- The default value produces the intended base character.
- The slider looks acceptable at 0, 0.5, and 1.
- The curve does not create a sudden jump.
- Mirrored bones move symmetrically.
- The character looks correct from all sides.
- Clothing still fits at the extreme values.
- Skinning remains acceptable during animation.
- Blendshapes exist on every supported LOD.
- Bone names exist on every race sharing the DNA.
- Overlay and shared color names match exactly.
- Mesh modifiers target the correct slots.
- The DNA appears under the intended customizer section.
- Saving and reloading the character preserves the value.

Extreme DNA combinations are often more important than testing one slider alone. Test height with leg length, shoulder width with arm width, and facial sizes together.

---

## Common Problems

### The DNA Does Not Appear on the Character

Check that:

1. The DNA asset was added to a DNA Group.
2. The DNA Group was added to the race's DNA Collection.
3. `Use New DNA System` is enabled on the RaceData.
4. The DCA was rebuilt after the race or group changed.
5. You selected the correct Group under `Live DNA -> Add New DNA`.

### The Slider Appears but Nothing Changes

Check that:

- The effect is enabled.
- The DNA value is different from its Default Value.
- Min and Max are not both zero.
- The curve is not flat at zero.
- The target bone, blendshape, overlay, or shared color name is correct.
- The required mesh modifier, pose, or material property is assigned.

### The Neutral Character Changes Shape

For centered DNA, confirm:

- `Default Value = 0.5`.
- The curve passes through the middle.
- `Min = -1` and `Max = 1`.

Also check whether another DNA effect touches the same bone.

### Left and Right Sides Do Not Match

- Confirm both sides use the same curve and strength.
- Confirm each effect targets the correct bone.
- Remember that mirrored rotation axes may need opposite signs.
- Use `Copy to Selected` for matching curves and Bone Scale factors.

### A Bone Cannot Be Picked

- Build the DCA first.
- Keep a DCA selected or present in the open scene.
- Clear the text in `Bone Name` if it is filtering the picker list.
- Use `Select Bone in Hierarchy` to confirm an existing assignment.

### A Color Effect Does Nothing

- Confirm the shared color already exists on the DCA.
- Match its name exactly.
- Confirm the Texture Number is used by the UMA material.
- Check whether `Base Multiplier` or `Additive` is correct for the material.
- Test under neutral lighting before judging subtle color changes.

### A Blendshape Works on One Character but Not Another

- Confirm both source meshes use the same blendshape name.
- Confirm the DCA is loading blendshapes.
- Confirm the blendshape was not baked out.
- Check every LOD and every relevant slot.

### The DNA Appears as Unknown

The character has a saved DNA name that the current race no longer knows. This usually happens after a DNA asset was renamed or removed from the race's groups. Restore the original asset name or remove the unknown entry and add the replacement DNA.

---

## Recommended Folder Layout

Keep DNA assets organized by purpose and race:

```text
MyCharacter/
  DNA/
    Groups/
      MyRaceBody.asset
      MyRaceFace.asset
      MyRacePose.asset
    Body/
      height.asset
      armLength.asset
      shoulderWidth.asset
    Face/
      noseSize.asset
      earSize.asset
      jawWidth.asset
    Curves/
      CenteredLinear.asset
      SoftUpperRange.asset
```

Groups can reference DNA assets from any folder. The folder structure is for artist clarity and source control, not runtime behavior.

---

## See Also

- `Assets/UMA/Docs/NewDNASystem.md`: technical architecture and full runtime reference.
- `Assets/UMA/Docs/RaceData.md`: RaceData overview.
- `Assets/UMA/Docs/DynamicCharacterAvatar.md`: DCA customization and build behavior.
- `Assets/UMA/Docs/MeshModifierSculpting.md`: creating mesh modifiers for sculpt-based DNA.
- `Assets/UMA/UMA3/DNA/`: shipped UMA 3 DNA examples.