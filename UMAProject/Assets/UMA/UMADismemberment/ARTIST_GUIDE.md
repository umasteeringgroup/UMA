# UMA Dismemberment: Artist Setup and Production Guide

This guide explains how to prepare characters and materials for the UMA 3 dismemberment system in Unity 6.3 or newer. It is written primarily for character artists, technical artists, and scene builders. A small integration section is included for the programmer who connects weapon hits or gameplay events to the system.

## What the system does

UMA Dismemberment separates the triangles influenced by a selected skeleton bone and its descendants. It creates:

- a modified source character with the selected triangles removed;
- a detached GameObject containing a cloned skeleton and every affected renderer;
- an optional cap surface on both sides of each valid cut;
- a completion event that can add physics, colliders, blood geometry, particles, audio, or gameplay behavior.

The cut is calculated from the generated UMA character at runtime. Every affected UMA renderer is considered, so body geometry, clothing, armor, and other skinned parts can separate together when their weights and topology support the same cut.

The system does **not** perform an arbitrary plane cut. It does not create new intersection points through triangles. A triangle is placed on either the character or the detached piece, and the cut follows existing triangle edges. The quality and location of the result therefore depend heavily on the authored topology and skin weights.

It also does not automatically add:

- rigidbodies, colliders, joints, or a detached-piece ragdoll;
- blood decals, particles, gore meshes, sounds, or damage logic;
- weapon-hit detection, health, AI reactions, or networking;
- permanent changes to the UMA recipe.

Those are deliberately application-specific and should be added after a successful cut through the **Dismemberment Completed** event.

## Where to find the feature

The main files are:

- [`Runtime/UmaDismemberment.cs`](Runtime/UmaDismemberment.cs) — component and public API;
- [`Samples/Scene/Example.unity`](Samples/Scene/Example.unity) — working sample scene;
- [`Samples/Materials/SliceFill.mat`](Samples/Materials/SliceFill.mat) — simple sample cap material;
- [`Samples/Materials/DismembermentCap.shader`](Samples/Materials/DismembermentCap.shader) — dependency-free unlit cap shader;
- [`Samples/Scripts/GUIDismemberment.cs`](Samples/Scripts/GUIDismemberment.cs) — UI button example;
- [`Samples/Scripts/ExampleDismemberCallback.cs`](Samples/Scripts/ExampleDismemberCallback.cs) — event, physics, and supplemental-gore example.

The sample cap is intentionally simple. It is useful for verifying geometry and UV scale, but most finished projects will replace it with a pipeline-native lit material.

## Five-minute setup

### 1. Prepare a generated UMA avatar

Start with a GameObject that has a `DynamicCharacterAvatar`. Configure its race, wardrobe, and animator normally.

The dismemberment component must be on the **same GameObject** as the `DynamicCharacterAvatar`. Add:

**Add Component > Uma Dismemberment**

The component requires a `DynamicCharacterAvatar`, so Unity will prevent an unsupported component arrangement.

### 2. Assign a cap material

In the **Cap** section:

1. Enable **Generate Caps**.
2. Assign `Samples/Materials/SliceFill.mat` to **Fallback Material**.
3. Leave **Require Closed Caps** enabled for the first test.
4. Leave **Cap UV Meters Per Tile** at `0.25`.

The Inspector displays an error if the resolved material is missing or its shader is unsupported by the currently active render pipeline.

### 3. Choose allowed bones

In **Bone Selection**:

1. Enable **Include Child Bones**.
2. Enable **Use Sliceable**.
3. Add a row to **Sliceable Human Bones**.
4. Choose an obvious test bone, such as `LeftLowerArm`.
5. Begin with a threshold of `0.5`.

Avoid duplicate bone entries. The Inspector warns about duplicates, and the first matching entry supplies the threshold.

### 4. Choose lifecycle behavior

Keep **Rebuild Policy** at **Destroy Detached Pieces** while setting up the character. This restores a predictable clean state whenever UMA regenerates the avatar.

### 5. Enter Play Mode and trigger the cut

Open the sample scene to test the supplied UI, or have the gameplay integration call `TrySlice`. Dismemberment must be requested after UMA has generated its renderers. In Play Mode, the component Inspector shows **Ready: Yes** when renderer data is available.

If the cut fails, the Inspector displays **Last Failure Reason** and the detailed failure message.

## Understanding the Inspector

### Cap

#### Generate Caps

When enabled, the system attempts to close both exposed sides of the cut. One cap faces outward from the remaining character and the opposing cap faces outward from the detached piece.

When disabled, the split can still succeed, but both sides are visibly hollow unless another gore mesh hides the opening.

#### Fallback Material

This is the default cap material. It is normally used for the Built-in Render Pipeline and whenever no exact pipeline override matches.

Always assign a valid fallback even when the project currently uses URP or HDRP. It provides a safe material if graphics settings or quality levels change to a pipeline asset that is not in the override list.

#### Pipeline Overrides

Each override contains:

- **Pipeline** — an exact `RenderPipelineAsset` reference;
- **Material** — the cap material to use while that exact asset is active.

The match is by asset identity, not merely by “URP” or “HDRP” type. If the project has separate Low, Medium, and High quality pipeline assets, add every pipeline asset that can become active. They may all reference the same material when appropriate.

The first exact match with a non-null material is used. If there is no match, the fallback material is used.

#### Require Closed Caps

Keep this enabled for production while validating content. If a renderer has an open, branched, or non-manifold cut boundary, the complete dismemberment request is rejected before any source renderer is changed. This avoids partially sliced characters and obvious holes.

Disable it only when an intentionally open result is acceptable or a separate gore mesh completely covers the cut. With it disabled, an invalid open/non-manifold boundary can continue without a generated cap for that renderer. A degenerate or self-intersecting closed boundary may still be impossible to triangulate and can still reject the cut.

#### Cap UV Meters Per Tile

This controls physical texture density using Unity's standard convention of one unit equaling one meter.

- `1.0` means one texture tile across one meter;
- `0.5` means two tiles per meter;
- `0.25` means four tiles per meter;
- `0.1` means ten tiles per meter.

The generated cap uses planar UV0 coordinates. Material texture tiling multiplies this result. For example, `0.25` meters per tile with a material tiling of `2` produces eight repeats per meter.

The calculation uses mesh-local positions. Characters and meshes should use a sensible real-world scale, normally scale `1,1,1`. A non-unit transform scale changes the apparent physical texture size.

#### Per-bone Cap UV Mapping

Each **Sliceable Human Bones** row has its own **Cap UV Mapping** selection:

- **Meter Scaled Tiled** is the backward-compatible default. It uses the physical mapping described above and allows the texture to repeat.
- **Centered Fit** does not tile. It maps the geometric area centroid of each generated cap loop to UV `(0.5, 0.5)`, scales both axes uniformly to preserve the cut's shape, and fits every UV inside the padded `0..1` square. This is intended for a meat cross-section texture with a bone or other feature in its center.

**Centered UV Padding** defaults to `0.02`, producing UVs inside `0.02..0.98` rather than touching the texture border. Padding helps prevent sampling the opposite edge when the texture uses bilinear filtering or an atlas. Use a clamp texture wrap mode for centered cap textures.

Body and armor shells can produce multiple independent cap loops. Centered mapping gives each loop its own complete centered UV fit, so each shell receives the full cross-section texture rather than sharing a single atlas region.

#### Seam Weld Tolerance

UMA meshes frequently duplicate a geometric vertex at UV seams, hard-normal seams, material boundaries, or compatible slot borders. Cap-loop detection treats vertices within this distance as the same topological point without welding or otherwise changing the generated UMA mesh.

The default is `0.0001` meters, or 0.1 mm under Unity's one-unit-per-meter convention. Increase it only enough to bridge a known export seam. An unnecessarily large value can merge nearby but unrelated surfaces, especially layered armor, straps, or thin shells. **Require Closed Caps** rejects unmatched, open, branched, or missing boundaries rather than accepting a visible hole.

### Bone Selection

#### Global Threshold

The threshold determines how much accumulated influence from the selected bone group a vertex needs before it can pull a triangle onto the detached piece.

The algorithm adds the weights from the selected bone and, when enabled, all included child bones. A triangle follows the detached side when **any one of its vertices** exceeds the threshold.

This has two important artistic consequences:

- lowering the threshold generally selects more mixed-weight geometry and makes the detached region larger;
- raising the threshold generally selects only strongly weighted geometry and makes the detached region smaller.

Because whole triangles move together, a large triangle with only one qualifying vertex can extend the cut farther than expected. Clean deformation topology around the intended cut zone matters more than very fine numerical adjustment.

Start at `0.5`, then test values in steps of approximately `0.05`. Do not tune the threshold until the final body and wardrobe meshes are being generated; different topology or weighting can move the boundary.

#### Use Sliceable

When enabled, `HumanBodyBones` requests are restricted to the **Sliceable Human Bones** list. This is the recommended production setting because it prevents unsupported or unattractive cuts.

When disabled, any valid mapped humanoid bone can be requested and the global threshold is used.

The restriction applies to the humanoid-bone API. A programmer using the generic `Transform` overload supplies the target transform and threshold directly.

#### Sliceable Human Bones

Each row contains a humanoid bone, its preferred threshold, and its cap UV mapping. The normal humanoid API uses these per-bone settings by default. Existing rows and newly added rows use **Meter Scaled Tiled** unless changed to **Centered Fit**.

Use the list as an approved art matrix. Only expose cuts that have been tested with:

- every supported race and body shape;
- all wardrobe categories that can cover the joint;
- extreme DNA values;
- the animation poses in which damage can occur;
- every supported render pipeline and quality level.

#### Include Child Bones

This normally should remain enabled. A lower-arm cut should carry the hand and finger influences; a lower-leg cut should carry the foot and toes.

If it is disabled, only the exact selected bone contributes to the weight test. Geometry strongly influenced by descendants may remain on the source character, producing disconnected or incomplete pieces. Disable it only for a deliberately authored special case.

### Lifecycle

#### Destroy Detached Pieces

This is the safest default. When UMA begins rebuilding the avatar, the original source renderers are restored, runtime-owned source meshes are released, repeated-cut tracking is cleared, and detached pieces are destroyed.

UMA can rebuild after recipe changes, wardrobe changes, DNA changes, or explicit regeneration. A cut is therefore a runtime visual state, not a permanent recipe edit.

#### Keep Detached Pieces

The source renderers are still restored before UMA regenerates, but detached pieces are allowed to remain. Use this for persistent debris or gameplay objects.

Your game is responsible for deciding when those pieces should be destroyed. Each detached root receives a `DismemberedPiece` component, which owns and releases the generated detached meshes when the piece is destroyed.

Disabling or destroying `UmaDismemberment` performs a full cleanup and destroys its tracked detached pieces.

### Events

#### Invoke Legacy Event

This enables the older two-transform event. It exists for compatibility with older integrations and supplies the detached root and cloned target bone.

#### Dismemberment Completed

Use this event for new work. It is invoked after every successful slice and provides:

- the detached root;
- the corresponding cloned target bone;
- the humanoid bone, when the humanoid API was used;
- a stable bone-name hash;
- every detached `SkinnedMeshRenderer`;
- every modified source `SkinnedMeshRenderer`.

The rich event is available regardless of the **Invoke Legacy Event** checkbox.

## Replacing the cap material or shader

The component does not require a particular shader name or set of material properties. It assigns the selected material to the generated cap submesh. Replacing the appearance is normally a material task; no dismemberment code change is required.

### Recommended: create a pipeline-native lit cap material

For URP:

1. Create a new Material outside the sample folder.
2. Select `Universal Render Pipeline/Lit` or a project Shader Graph.
3. Use an opaque surface.
4. Connect a seamless flesh, metal, wood, stone, or synthetic interior texture to Base Color.
5. Use UV0 for the cap texture.
6. Add a normal map and suitable smoothness/metalness when required.
7. Add every active `UniversalRenderPipelineAsset` and this material to **Pipeline Overrides**.

For HDRP, follow the same process with `HDRP/Lit` or an HDRP Shader Graph and add every active `HDRenderPipelineAsset` to the override list.

For the Built-in Render Pipeline, use a Standard or custom material in **Fallback Material**.

The cap vertices include generated normals and tangents, so normal-mapped lit materials can work correctly. UV0 contains the planar cap mapping. Other preserved vertex streams come from the original boundary vertices and should not be treated as newly authored cap maps.

### Replacing the supplied cross-pipeline shader

`DismembermentCap.shader` supplies an unlit opaque SubShader for Built-in, URP, and HDRP. Its properties are:

- `_MainTex` — cap texture;
- `_Color` — tint.

It is intended as a dependency-free fallback and geometry test. It is not a complete production skin, flesh, or shadowing shader.

To replace it safely:

1. Create your shader or Shader Graph in the project's own art folder. Do not edit the sample shader in place, because package updates may replace it.
2. Create a new material using that shader.
3. Configure the material as opaque unless the project deliberately wants transparency artifacts and sorting behavior.
4. Use UV0 for planar cap texturing.
5. Keep normal orientation and backface culling in mind. The system produces opposing cap faces with opposing normals, so conventional backface culling should display the correct face on each piece.
6. Assign the material as the fallback or as a pipeline override.
7. Enter Play Mode and verify that the Inspector resolves a supported material for the active pipeline.

Custom shaders do **not** have to use `_MainTex` or `_Color`; those names only belong to the supplied shader. The slicer never searches for or sets those properties.

### Texture-authoring advice

Use a seamless or low-directionality texture. Each disconnected cut loop chooses a tangent from its own boundary, so texture rotation can vary between cuts or separate loops. Highly directional fibers or veins may need a custom triplanar/world-space shader to look consistent.

Do not paint important details at a specific 0–1 UV location. Cap UVs are physical planar coordinates and are not normalized to fill the texture once.

For flesh, a convincing cap commonly combines:

- a dark red base with restrained value variation;
- a broad, low-frequency normal pattern rather than noisy high-frequency bumps;
- moderate or high roughness;
- optional subsurface or transmission appropriate to the active pipeline;
- blood decals, particles, or a supplemental gore mesh layered over the clean procedural cap.

For armor, robots, or props, author the cap as the material inside the shell: padding, metal thickness, cables, wood grain, stone, or internal machinery.

## Preparing meshes for clean cuts

### Author an intentional edge loop

The best results come from an edge loop around the intended sever location. The system cannot cut through the middle of a polygon, so sparse or diagonal topology produces a jagged boundary.

Place enough geometry around elbows, knees, wrists, ankles, necks, and armor joins to support both animation deformation and a visually plausible sever line.

### Keep the cut boundary manifold

Strict caps expect each cut-boundary vertex to connect to exactly two boundary neighbors, forming one or more closed loops. Common causes of failure include:

- open mesh borders crossing the cut;
- T-junctions or non-manifold edges;
- duplicated but unwelded vertices along the intended boundary;
- overlapping faces;
- zero-area triangles;
- self-intersecting projected loops;
- separate shells that meet visually but do not share topology.

A mesh can look closed in the viewport and still be topologically open. Run the DCC application's non-manifold, open-border, and degenerate-face checks before export.

UV seams, hard-normal seams, and material boundaries can duplicate vertices. Test intended dismemberment loops after the final Unity import because those splits can affect whether a cap boundary is recognized as continuous.

### Paint weights with the cut in mind

The selected side is based on accumulated skin weight, not vertex position. Use a controlled transition around the intended joint:

- distal geometry should be strongly weighted to the selected subtree;
- source-side geometry should remain below the chosen threshold;
- avoid isolated high-weight vertices on the wrong side;
- inspect clothing and armor weights, not only the naked body;
- test extreme poses for deformation quality before judging the cut.

Remember that one qualifying vertex moves its entire triangle to the detached side. A small weight-paint spike can create a long triangular notch.

### Coordinate body and wardrobe topology

The system slices every affected UMA renderer. A sleeve, glove, boot, or armor plate can therefore separate with the body, but each item is evaluated from its own weights and triangles.

For a coherent result:

- place garment cut loops near the corresponding body loop;
- use compatible weight transitions;
- avoid a large low-poly garment triangle spanning both sides of the joint;
- test layered wardrobe combinations for gaps and interpenetration;
- decide whether rigid accessories should be part of the cut, remain attached, or be handled as separate gameplay objects.

If any affected renderer has invalid required geometry while strict caps are enabled, the request is rejected without partially changing the character.

### Mesh import requirements

At runtime, every affected generated renderer mesh must:

- be CPU-readable (`Mesh.isReadable`);
- contain vertices and at least one submesh;
- use triangle topology for every submesh;
- contain consistent modern bone-weight data;
- use bones that belong to UMA's active skeleton hierarchy.

If a custom mesh pipeline uploads data as no longer readable, dismemberment cannot inspect it. Keep CPU access available for content that must be sliced.

Renderers with a Unity `Cloth` component are rejected. Changing topology invalidates cloth coefficients, and this system does not rebuild them automatically. Remove or disable that cloth workflow for sliceable garments, swap to a pre-authored damaged version, or implement a project-specific cloth rebuild.

## Adding physics and gore

The detached piece initially contains renderers and a cloned skeleton but no physics. The sample callback demonstrates the simplest setup:

1. listen to **Dismemberment Completed**;
2. add a `Rigidbody` to the detached root;
3. add one or more child colliders to form a compound collider;
4. optionally instantiate supplemental gore renderers and remap their bones;
5. apply force or torque from the hit;
6. spawn blood effects at the cloned target bone or the original cut location.

Collider sizes and centers are character-specific. The numeric values in `ExampleDismemberCallback` are sample values in meters, not universal defaults. Tune them per race, body part, and DNA range.

The sample callback's four **Gib** fields are optional supplemental gore renderers:

- **Gib Split** is a `SkinnedMeshRenderer` prefab instantiated on the detached piece;
- **Gib Split Material** optionally replaces that instance's material;
- **Gib Source** is a renderer prefab instantiated on the remaining body side;
- **Gib Source Material** optionally replaces that instance's material.

The callback remaps the prefab's bones to each generated skeleton by bone-name hash. These objects do not perform the cut and do not replace the procedural cap unless their geometry visually covers it. A null renderer creates nothing, and a null material keeps the renderer prefab's existing material. All four references are unassigned in the supplied example scene, so they have no effect until configured.

For a fully articulated detached ragdoll, create rigidbodies, colliders, and joints on the required cloned bones rather than only adding a Rigidbody to the root. Coordinate that with the project's existing ragdoll system to avoid duplicate bodies or joints.

If the character uses an `LODGroup`, note that the generated detached renderers are not automatically arranged into a new detached-piece LOD setup. Configure detached visibility or LOD behavior in the completion event.

## Connecting damage or UI

The component does not decide when a hit is lethal or which bone was struck. Gameplay code must make that decision and request a slice.

For a humanoid avatar, the preferred call is:

```csharp
using UMA.Dismemberment;
using UnityEngine;

if (!dismemberment.TrySlice(
        HumanBodyBones.LeftLowerArm,
        out UmaDismemberment.DismemberedInfo piece,
        out string failure))
{
    Debug.LogWarning(failure);
}
```

The default call:

- checks the **Sliceable Human Bones** list when **Use Sliceable** is enabled;
- uses the matching per-bone threshold;
- includes descendants when **Include Child Bones** is enabled;
- prevents the exact same bone from being sliced twice during one UMA generation.

For a generic, non-humanoid UMA rig, the programmer must pass a skeleton transform and threshold:

```csharp
bool sliced = dismemberment.TrySlice(
    targetBoneTransform,
    0.5f,
    out UmaDismemberment.DismemberedInfo piece,
    out string failure);
```

Do not call the humanoid overload on a generic Animator. It requires a valid humanoid avatar and `HumanBodyBones` mapping.

For a no-code scene demonstration, use the `GUIDismemberment` sample on a uGUI Button, assign the avatar or `UmaDismemberment` component, and choose **Bone To Slice**.

## Repeated cuts and reset behavior

By default, the same bone cannot be cut more than once during one UMA generation. Different bones can be requested, and later slices operate on the current already-modified source renderer meshes.

Practical consequences include:

- cutting a bone whose geometry has already left with an earlier piece can return **No Affected Geometry**;
- a later cut can reuse the existing cap material submesh instead of continually adding material slots;
- parent/child cut order can change which geometry remains available;
- every approved sequence should be tested, not only each cut in isolation.

`ResetDismemberment(true)` restores the original UMA-owned source meshes, clears repeated-cut tracking, and destroys detached pieces. In Play Mode, the component Inspector exposes **Reset Dismemberment** for testing.

## Performance and memory expectations

Dismemberment is a CPU runtime mesh operation. It reads bone weights and triangle indices, builds source and detached meshes, creates cap vertices, and clones the skeleton paths needed by affected renderers.

Use it as an event, not as a per-frame effect. Cost grows with:

- generated vertex and triangle count;
- number of affected body and wardrobe renderers;
- number and complexity of cap loops;
- number of simultaneous detached pieces;
- repeated cuts on the same character.

Both output meshes preserve the original vertex streams so that skinning, UVs, colors, blend shapes, and material behavior remain intact. This favors visual correctness but can temporarily consume significant memory on high-resolution characters. The component releases its runtime-owned meshes during reset, disable, destruction, and UMA regeneration according to the selected rebuild policy.

Profile representative fully dressed characters on the target platform. If large crowds can be dismembered, consider limits on active pieces, timed cleanup, simplified collision, reduced wardrobe complexity, or pre-authored debris for distant characters.

## Troubleshooting

### Ready says No / Not Initialized

UMA has not finished generating renderers, or the avatar has no usable Animator. Wait for character creation/update before requesting the cut. Confirm that the `UmaDismemberment` component is enabled on the same GameObject as the `DynamicCharacterAvatar`.

### Invalid Bone

For the humanoid API, confirm that:

- the Animator is humanoid;
- the Avatar is valid;
- the selected `HumanBodyBones` entry is mapped;
- `LastBone` was not selected.

For a generic rig, use the Transform overload with a transform from the active UMA skeleton.

### Bone Not Sliceable

Add the bone to **Sliceable Human Bones**, or disable **Use Sliceable** while testing. Re-enable the restriction before shipping unless arbitrary cuts are intentional.

### Already Dismembered

The exact bone has already been cut during this UMA generation. Reset dismemberment or regenerate the avatar before testing it again.

### Missing Cap Material

When **Generate Caps** is enabled, assign a fallback material with a supported shader. If using a pipeline override, confirm that its Pipeline field references the exact asset reported as active in the project's current graphics/quality configuration.

### Unsupported Renderer / Cloth

An affected renderer has a `Cloth` component. The operation is rejected because the changed topology no longer matches its cloth coefficients.

### No Affected Geometry

The selected bone group did not own any triangles above the threshold. Try:

- confirming the correct bone was selected;
- enabling **Include Child Bones**;
- lowering the threshold;
- checking the mesh's skin weights;
- confirming that an earlier cut did not already remove the geometry.

### Invalid Mesh: not CPU-readable

Keep the runtime-generated mesh readable. Review custom mesh combining, optimization, or upload code that may discard its CPU data.

### Invalid Mesh: open or non-manifold boundary

Inspect the cut loop in the DCC application. Look for open borders, unwelded duplicates, T-junctions, overlapping faces, and degenerate triangles. Temporarily disabling **Require Closed Caps** can confirm that the boundary is the problem, but it is not a substitute for repairing production geometry.

### Invalid Mesh: cap cannot be triangulated

The projected boundary is degenerate or self-intersecting. Simplify and regularize the intended edge loop. Very folded, doubled-back, or nearly collinear boundary geometry is difficult to cap reliably.

### The cut is too large, too small, or jagged

- Too large: raise the threshold and inspect isolated high subtree weights.
- Too small: lower the threshold and confirm child bones are included.
- Jagged: improve the edge flow and reduce long triangles around the joint.
- A triangular spike remains or disappears: inspect all three vertex weights on that triangle.

### Body and clothing cut at different heights

Their topology or weights differ. Align garment cut loops and weight transitions with the body, or hide the mismatch with an authored cuff, broken armor edge, or supplemental gore mesh.

### The cap is pink, invisible, or uses the wrong material

- Pink: the material shader is missing or incompatible with the active pipeline.
- Invisible from one side: verify shader culling and cap normals; use a temporary double-sided material only as a diagnostic.
- Wrong pipeline material: add the exact active pipeline asset to **Pipeline Overrides**.
- No material slot: confirm **Generate Caps** is enabled and the cut produced a valid boundary loop.

### Cap texture scale or direction is wrong

Set mesh transforms to a sensible unit scale, tune **Cap UV Meters Per Tile**, then tune material tiling. Use a seamless texture or a triplanar/world-space shader if varying loop orientation is visually unacceptable.

### Detached pieces disappear, remain forever, or return after changes

- Unexpected disappearance during an UMA rebuild: select **Keep Detached Pieces**.
- Pieces remain forever: destroy them through gameplay cleanup or use **Destroy Detached Pieces**.
- The character returns intact after a recipe/DNA rebuild: expected behavior; dismemberment is not stored in the UMA recipe.

## Production checklist

Before approving a sliceable character set, verify:

- `UmaDismemberment` and `DynamicCharacterAvatar` are on the same GameObject.
- The component reports **Ready: Yes** before gameplay can request a cut.
- Every approved humanoid bone appears once in **Sliceable Human Bones**.
- Per-bone thresholds have been tested on all supported races and DNA extremes.
- **Include Child Bones** produces complete detached limbs.
- Body and wardrobe meshes have readable triangle geometry.
- Intended cut boundaries are closed, manifold, and sufficiently dense.
- Cloth renderers have a defined alternative workflow.
- Fallback and every possible pipeline override material are assigned and supported.
- Cap UV density is correct at one Unity unit per meter.
- Cap materials are tested in Built-in, URP, or HDRP as applicable.
- Physics, colliders, VFX, audio, cleanup, and LOD behavior are handled by the completion event.
- Parent/child and repeated-cut sequences have been tested.
- UMA regeneration behavior matches the chosen rebuild policy.
- Fully dressed characters meet CPU time and memory budgets on the target hardware.

When a cut fails, use the component's **Last Failure Reason** and detailed message first. The operation is designed to fail without partially modifying source renderers, which makes those diagnostics safer to iterate on during content production.
