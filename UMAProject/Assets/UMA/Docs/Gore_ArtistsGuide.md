# UMA Dismemberment: Artist Setup and Production Guide

This guide explains how to prepare characters, materials, physics, and gameplay integration for the UMA 3 dismemberment system in Unity 6.3 or newer. It is written for character artists, technical artists, scene builders, and programmers who connect weapon hits or project-specific behavior to the system.

For a shorter component checklist, start with the [package README](README.md). For a guided tour of the supplied buttons, avatar, callback, physics, and undo action, use the [sample scene walkthrough](Samples/README.md).

## Quick navigation

- [Five-minute setup](#five-minute-setup)
- [Inspector reference](#understanding-the-inspector)
- [Cap materials and shaders](#replacing-the-cap-material-or-shader)
- [Mesh and wardrobe preparation](#preparing-meshes-for-clean-cuts)
- [Detached physics, main-body ragdoll, blood, and gibs](#adding-physics-and-gore)
- [Damage and UI calls](#connecting-damage-or-ui)
- [Custom gameplay and system extensions](#integrating-custom-gameplay-and-system-extensions)
- [Undo, rebuilds, and repeated cuts](#repeated-cuts-undo-and-rebuild-behavior)
- [Troubleshooting](#troubleshooting)

## What the system does

UMA Dismemberment separates the triangles influenced by a selected skeleton bone and its descendants. It creates:

- a modified source character with the selected triangles removed;
- a detached GameObject containing a cloned skeleton and every affected renderer;
- an optional cap surface on both sides of each valid cut;
- a completion event that can add physics, colliders, blood geometry, particles, audio, or gameplay behavior.

The cut is calculated from the generated UMA character at runtime. Every affected UMA renderer is considered, so body geometry, clothing, armor, and other skinned parts can separate together when their weights and topology support the same cut.

The system does **not** perform an arbitrary plane cut. It does not create new intersection points through triangles. A triangle is placed on either the character or the detached piece, and the cut follows existing triangle edges. The quality and location of the result therefore depend heavily on the authored topology and skin weights.

The core slicer also does not decide or author:

- blood decals, particles, gore meshes, sounds, or damage logic;
- weapon-hit detection, health, AI reactions, or networking;
- permanent changes to the UMA recipe.

Those are deliberately application-specific and should be added after a successful cut through the **Dismemberment Completed** event. The supplied callback shows how to construct detached rigid or articulated physics from UMA physics definitions, apply a separation impulse, and spawn blood or supplemental gore. A per-cut option can activate an already configured full-body `UMAPhysicsAvatar`, but the slicer does not create that source-character physics recipe.

## Where to find the feature

The main files are:

- [`Runtime/UmaDismemberment.cs`](Runtime/UmaDismemberment.cs) — component and public API;
- [`Samples/Scene/U3-GoreExample.unity`](Samples/Scene/U3-GoreExample.unity) — working sample scene;
- [`Samples/Materials/SliceFill.mat`](Samples/Materials/SliceFill.mat) — simple sample cap material;
- [`Samples/Materials/DismembermentCap.shader`](Samples/Materials/DismembermentCap.shader) — dependency-free unlit cap shader;
- [`Samples/Scripts/GUIDismemberment.cs`](Samples/Scripts/GUIDismemberment.cs) — UI button example;
- [`Samples/Scripts/UndoDismemberments.cs`](Samples/Scripts/UndoDismemberments.cs) — complete undo/rebuild UI example;
- [`Samples/Scripts/ExampleDismemberCallback.cs`](Samples/Scripts/ExampleDismemberCallback.cs) — event, physics, blood, impulse, and supplemental-gore example;
- [`Runtime/DismemberedRagdollBuilder.cs`](Runtime/DismemberedRagdollBuilder.cs) — reusable detached rigid/articulated physics builder;
- [`Tests/Editor/DismembermentMeshBuilderTests.cs`](Tests/Editor/DismembermentMeshBuilderTests.cs) — geometry, rig, physics, lifecycle, and undo examples expressed as tests.

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
6. Leave **Cap UV Mapping** at **Meter Scaled Tiled** for the first geometry test.
7. Leave **Detached Physics Mode** at **Automatic**, **Trim Detached Rig** off, and **Ragdoll Main Body** off until the visual cut is correct.

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

Keep this enabled for production while validating content. If a cap-eligible body surface has an
open, branched, or non-manifold cut boundary, the complete dismemberment request is rejected before
any source renderer is changed. This avoids partially sliced characters and obvious anatomical
holes. Clothing boundaries are not subject to this closed-cap requirement: authored garment hems,
open shells, and imperfect clothing seams can still be cut and use the local two-sided interior band.

Disable it only when an intentionally open result is acceptable or a separate gore mesh completely covers the cut. With it disabled, an invalid open/non-manifold boundary can continue without a generated cap for that renderer. A degenerate or self-intersecting closed boundary may still be impossible to triangulate and can still reject the cut.

#### Body-only caps and clothing interiors

Keep **Cap Only Body Parts** enabled for the normal character setup. A boundary receives the meat
cap only when its originating slot contains an overlay whose group is listed in **Body Overlay
Groups**. This remains a per-slot decision when UMA packs skin and clothing into the same material
or atlas. The default is `Skin`, UMA's standard base-skin overlay group. Add project-specific skin,
creature-body, or prosthetic groups when those surfaces should also expose the cap. Disable this
option to restore the legacy behavior that caps every affected surface.

All other generated surfaces are treated as clothing. They remain open at the cut and receive
reversed interior-facing triangles only within **Clothing Double Sided Depth Meters** of the cut
edge. The default `0.1` creates a 10 cm interior band without changing the garment material or
making the complete garment double-sided. Set it to zero to leave clothing single-sided.

**Clothing Cut Smoothing** averages nearby garment weights and then requires two of a triangle's
three vertices to exceed the cut threshold. This removes isolated triangles caused by a single
slightly misweighted vertex. The default `0.5` is a conservative correction; lower it to preserve
more of the authored boundary or raise it when a garment has visibly noisy weights.

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

Body shells can produce multiple independent cap loops. Centered mapping gives each eligible loop
its own complete centered UV fit. Clothing loops do not use the cap while **Cap Only Body Parts** is
enabled.

#### Seam Weld Tolerance

UMA meshes frequently duplicate a geometric vertex at UV seams, hard-normal seams, material boundaries, or compatible slot borders. Cap-loop detection treats vertices within this distance as the same topological point without welding or otherwise changing the generated UMA mesh.

The default is `0.0001` meters, or 0.1 mm under Unity's one-unit-per-meter convention. Increase it only enough to bridge a known export seam. An unnecessarily large value can merge nearby but unrelated surfaces, especially layered armor, straps, or thin shells. **Require Closed Caps** rejects unmatched, open, branched, or missing boundaries rather than accepting a visible hole.

### Bone Selection

#### Global Threshold

The threshold determines how much accumulated influence from the selected bone group a vertex needs before it can pull a triangle onto the detached piece.

The algorithm adds the weights from the selected bone and, when enabled, all included child bones.
Anatomical/body triangles retain the inclusive rule: a triangle follows the detached side when any
one vertex exceeds the threshold. Clothing uses the smoothed two-of-three rule described above.

This has two important artistic consequences:

- lowering the threshold generally selects more mixed-weight geometry and makes the detached region larger;
- raising the threshold generally selects only strongly weighted geometry and makes the detached region smaller.

Because whole triangles move together, coarse topology can still make a cut angular. Clothing
smoothing removes isolated single-vertex spikes, but it cannot add geometry; clean deformation
topology around the intended cut zone remains important.

Start at `0.5`, then test values in steps of approximately `0.05`. Do not tune the threshold until the final body and wardrobe meshes are being generated; different topology or weighting can move the boundary.

#### Use Sliceable

When enabled, `HumanBodyBones` requests are restricted to the **Sliceable Human Bones** list. This is the recommended production setting because it prevents unsupported or unattractive cuts.

When disabled, any valid mapped humanoid bone can be requested and the global threshold is used.

The restriction applies to the humanoid-bone API. A programmer using the generic `Transform` overload supplies the target transform and threshold directly.

#### Sliceable Human Bones

Each row is the complete policy for one humanoid cut. It contains:

- the `HumanBodyBones` target and preferred weight threshold;
- **Cap UV Mapping** and **Centered UV Padding**;
- **Detached Physics Mode**;
- **Trim Detached Rig**;
- **Ragdoll Main Body**;
- one or more **Detached Physics Definitions**.

The normal humanoid API uses these per-bone settings by default. Existing rows and newly added rows use **Meter Scaled Tiled**, **Automatic** detached physics, no rig trimming, and no main-body ragdoll unless changed explicitly. Physics definitions are interpreted by a completion listener such as `ExampleDismemberCallback`; merely assigning them does not add physics unless that integration is enabled.

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
- the corresponding original source target bone;
- the humanoid bone, when the humanoid API was used;
- a stable bone-name hash;
- every detached `SkinnedMeshRenderer`;
- every modified source `SkinnedMeshRenderer`.

The rich event is available regardless of the **Invoke Legacy Event** checkbox.

The `DismembermentResult` also reports `mainBodyRagdollRequested` and `mainBodyRagdollActivated`. This lets gameplay distinguish a fatal-cut policy from a missing or failed `UMAPhysicsAvatar`. A successful mesh cut is still successful when the requested main-body ragdoll cannot activate.

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

For **Meter Scaled Tiled**, do not paint an important feature at a specific `0..1` location: its cap UVs are physical planar coordinates and may repeat. **Centered Fit** is specifically intended for a localized cross-section image. Put the bone or central feature near texture UV `(0.5, 0.5)`, keep important pixels inside the configured padding, and use Clamp wrap mode so the opposite edge cannot repeat into the cap.

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

The detached piece initially contains renderers and a cloned skeleton but no physics. `ExampleDismemberCallback` can build a detached ragdoll from the same `UMAPhysicsElement` assets used by the **U3-Ragdolls and Shooting Example** scene.

Enable **Ragdoll Dismembered Parts** on the callback. Then expand each entry under **Sliceable Human Bones, Cap UV and Physics** and assign that cut's **Detached Physics Definitions**. Humanoid cuts resolve this list automatically from `DismembermentResult.humanBone`; separate callback components are not required for the arms and legs.

Every detached mesh is reweighted as part of the cut. Influences above the cut, such as Spine, Shoulder, or Hips on a severed limb, are removed. Remaining cut-bone and descendant influences are normalized; a boundary vertex with no remaining influence is assigned entirely to the cut bone. This prevents a moving physics limb from stretching back toward stationary cloned ancestors. The source-side mesh keeps its original weights.

Choose **Detached Physics Mode** per cut:

- **Automatic** is recommended. One non-null definition creates a rigid piece; two or more create an articulated ragdoll.
- **Rigid** creates one Rigidbody on the detached root. Every definition still contributes its authored collider, but the child colliders form one compound body and no joints are created. This is the most stable choice for armor and lower limbs.
- **Articulated Ragdoll** creates a Rigidbody for every definition and connects included parent/child definitions with `CharacterJoint`s.
- **None** suppresses detached physics for that cut, including the legacy simple-physics fallback.

**Trim Detached Rig** optionally removes unreferenced entries and bind poses from each detached renderer, then removes cloned skeleton branches outside the cut. It keeps the Global-to-cut transform path and the complete subtree below the cut, so the configured physics definitions and supplemental gore still have the relevant transforms. Trimming is an optimization and is not required to prevent stretching.

Enable **Ragdoll Main Body** when that specific cut should incapacitate the character. A hand or lower-arm cut can leave it disabled, while a head, upper-leg, or other fatal cut can enable it. The option activates the surviving character's existing `UMAPhysicsAvatar` only after the mesh cut has completed successfully. The normal UMA ragdoll events are used, so systems such as the sample `RandomCharacterWalker` receive the same ragdoll notification and stop controlling the character. This option does not construct a full-body ragdoll by itself: the character must already have a configured UMA physics recipe and `UMAPhysicsAvatar`. If it does not, the cut still succeeds and a warning explains why the main body could not be ragdolled.

Use definitions for the severed side of the cut:

- **Head:** `U3HeadStandard`, or `U3HeadHD` for the HD rig;
- **Lower arm:** the matching ForeArm definition; HD can also include the matching Hand definition;
- **Upper arm:** the matching Arm and ForeArm definitions; HD can also include Hand;
- **Lower leg:** the matching Leg definition; HD can also include the matching Foot definition;
- **Upper leg:** the matching UpLeg and Leg definitions; HD can also include Foot.

Do not mix Standard and HD definitions in the same row. Their target names may overlap, but their collider dimensions and joint relationships are authored for different rigs.

Definitions above the selected cut are ignored automatically. For example, a `LeftUpperArm` cut can use Arm, ForeArm, and Hand definitions, but a Shoulder definition is not added to the detached physics rig. Exact repeated references to the same asset are also collapsed; two different assets targeting the same retained bone remain an error because the intended collider and joint settings would be ambiguous.

Articulated definition behavior is as follows:

- for a head cut, assigning only `U3HeadStandard` creates the `Head` body and its configured collider;
- for a limb, assign every definition that should articulate on the detached piece, in any list order;
- if a definition's **Parent Bone** is not included, that body becomes a free ragdoll root instead of being joined back to the living character;
- if the parent is included, the builder creates the same `CharacterJoint` axes and limits used by UMA's normal ragdoll builder.

Each definition must target a unique bone that exists in the detached skeleton. If any assigned non-null definition is invalid or targets a missing bone, no partial ragdoll is left behind. This makes configuration mistakes visible without leaving half-created colliders or joints.

Set **Ragdoll Layer** to the physics layer used by the project's UMA ragdolls. Verify that this layer collides with floors, platforms, props, and any gameplay layers the detached piece must hit. Collider dimensions and centers in `UMAPhysicsElement` are Unity meters; Unity's standard scale is one unit per meter.

**Separation Impulse** is a small impulse in kilogram-meters per second. Its default is `0.5`. The callback applies it to each free root body along the view direction so the piece separates visibly from the source. Assign **View Camera** for deterministic gameplay cameras, or leave it empty to use `Camera.main`. Set the impulse to zero to disable it.

Assign a particle-system prefab or another self-running effect prefab to **Blood Particle Emitter**. The callback instantiates it at the cut bone with the same behavior used by the ragdoll shooting sample. The supplied U3 `Blood` prefab has **Play On Awake** enabled and destroys its GameObject after it stops. Custom prefabs should likewise start themselves and clean themselves up, or include a separate lifetime component.

If **Ragdoll Dismembered Parts** is disabled, **Add Physics** retains the older simple fallback: one Rigidbody on the detached root and one sample SphereCollider on the cut bone. If configured rigid or articulated construction fails and **Add Physics** is enabled, this simple setup is also used as a visible fallback. Prefer physics definitions for production because they keep collider sizes and joint limits in the same reusable assets as the full-character ragdoll scene.

The callback's own **Fallback Physics Definitions** field remains as a compatibility fallback for an older scene or a generic `Transform` cut that has no humanoid sliceable-bone row. For a configured humanoid cut, the row's **Detached Physics Definitions** take precedence. **Bone Name** likewise remains the legacy filter when no per-cut definitions are assigned.

The sample callback's four **Gib** fields are optional supplemental gore renderers:

- **Gib Split** is a `SkinnedMeshRenderer` prefab instantiated on the detached piece;
- **Gib Split Material** optionally replaces that instance's material;
- **Gib Source** is a renderer prefab instantiated on the remaining body side;
- **Gib Source Material** optionally replaces that instance's material.

The callback remaps the prefab's bones to each generated skeleton by bone-name hash. These objects do not perform the cut and do not replace the procedural cap unless their geometry visually covers it. A null renderer creates nothing, and a null material keeps the renderer prefab's existing material. All four references are unassigned in the supplied example scene, so they have no effect until configured.

The detached rig deliberately does not add `UMAPhysicsAvatar`: that component expects a live `UMAData` avatar and manages a complete character ragdoll. The lightweight builder reuses its `UMAPhysicsElement` data and joint semantics without coupling a severed skeleton to the source avatar's lifecycle.

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

If the project uses only the new Input System, place an `InputSystemUIInputModule` on the scene `EventSystem`. The sample cut and undo components receive uGUI `Button.onClick` callbacks and do not poll the legacy `Input` API.

## Integrating custom gameplay and system extensions

### Prefer a project-owned completion listener

Keep hit detection, health, inventory drops, scoring, VFX, audio, AI, and networking outside `UmaDismemberment`. Add a project-owned component beside it and subscribe to `DismembermentCompleted`. This avoids editing a sample file and makes package updates much easier to merge.

```csharp
using UMA.Dismemberment;
using UnityEngine;

public sealed class CharacterDismembermentEffects : MonoBehaviour
{
    [SerializeField] private UmaDismemberment dismemberment;
    [SerializeField] private GameObject cutEffectPrefab;

    private void OnEnable()
    {
        if (dismemberment == null)
            dismemberment = GetComponent<UmaDismemberment>();
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.AddListener(OnDismembered);
    }

    private void OnDisable()
    {
        if (dismemberment != null)
            dismemberment.DismembermentCompleted.RemoveListener(OnDismembered);
    }

    private void OnDismembered(DismembermentResult result)
    {
        if (result == null || result.root == null) return;

        if (cutEffectPrefab != null)
        {
            Vector3 position = result.targetBone != null
                ? result.targetBone.position : result.root.position;
            Instantiate(cutEffectPrefab, position, Quaternion.identity, result.root);
        }

        if (result.mainBodyRagdollRequested &&
            !result.mainBodyRagdollActivated)
        {
            Debug.LogWarning("The cut succeeded, but the main-body ragdoll did not activate.",
                this);
        }
    }
}
```

Parent detached-side custom objects to `result.root` when they should be removed with that piece and by full undo. Parent source-side additions to an appropriate source transform only when their lifecycle should follow the source avatar. An unparented particle, decal, audio object, or pooled effect remains the custom system's responsibility and is not automatically found by undo.

Do not modify or destroy the meshes in `sourceRenderers` or `detachedRenderers` unless the project deliberately takes over their complete ownership. `UmaDismemberment` and `DismemberedPiece` track the generated meshes and release them during rebuild, undo, disable, or destruction.

### Reusing or replacing detached physics

`ExampleDismemberCallback` is an example, not a required runtime dependency. Copy its listener pattern into the project's namespace and remove features the project does not use. Custom integrations can call:

- `DismemberedRagdollBuilder.FilterDefinitionsForCutSubtree` to discard definitions above the severed side;
- `DismemberedRagdollBuilder.ResolvePhysicsMode` to apply Automatic/explicit selection;
- `DismemberedRagdollBuilder.TryBuildRigid` for one stable compound body;
- `DismemberedRagdollBuilder.TryBuild` for an articulated partial ragdoll.

Both builders return a `DismemberedRagdollBuildResult` containing created rigidbodies, free root bodies, colliders, and joints. `ApplyImpulse` affects only the free root bodies. Check the returned error and avoid adding a second Rigidbody or collider fallback unless the builder has failed cleanly.

When the sample callback successfully creates detached colliders, it calls `SuspendSourceRagdollColliders(result.sourceTargetBone)`. This disables only `UMAPhysicsAvatar`-owned colliders on the original cut bone and its descendants, so invisible colliders from the severed side no longer collide as part of the main-character ragdoll. Other gameplay colliders are not touched. The component remembers each collider's enabled state and restores it during full Undo, lower-level reset, component shutdown, or UMA regeneration.

Use `TryGetBoneSettings(result.humanBone, out settings)` to retrieve the per-cut definitions, physics mode, rig-trimming policy, UV mode, and main-body policy used by a humanoid row. A generic `Transform` cut reports `HumanBodyBones.LastBone`, so the project must supply its own generic-rig settings.

`result.targetBone` is the cloned bone inside the detached hierarchy. `result.sourceTargetBone` is the corresponding original bone on the surviving character. Use the detached transform for piece-local effects and physics; use the source transform for source-side effects or collider suspension.

### Mapping hits to cuts

A weapon system normally resolves the hit collider or Rigidbody back to an Animator bone, maps that transform to an approved `HumanBodyBones` entry, applies health/lethality rules, and then calls `TrySlice`. Do not call the slicer for every projectile contact: first decide that the hit actually severs the part.

Keep explicit references to the hit character's `UmaDismemberment`. Scene-wide searches are acceptable in the one-avatar sample but are ambiguous and wasteful in a crowd. The sample undo action intentionally refuses to choose when it finds multiple candidates.

### Saving, loading, and networking

Dismemberment is not serialized into an UMA recipe. To persist it, save an ordered list of successful cuts and replay that list after the regenerated avatar reports that it is ready. Order matters because a parent cut can remove geometry needed by a later child cut. Save any game-specific fatal/nonfatal state separately rather than inferring it from a reconstructed mesh.

For a networked game, make the authoritative side decide whether the cut succeeds and replicate the avatar recipe/version plus the ordered approved bone cuts. Rebuild the meshes locally from the same generated UMA content instead of attempting to serialize Unity `Mesh` instances or detached GameObjects over the network. Replicate physics state separately if exact detached-piece motion matters.

### When changing the slicer itself is justified

Most visual changes need only a cap material, supplemental renderer, particle, or completion listener. Most physics changes belong in a custom callback using the public builder. Change the core mesh builder only when the project needs different geometry ownership, triangle partitioning, cap construction, or vertex-stream output.

When making such a change:

1. Keep UMA-owned input meshes read-only and create owned outputs.
2. Preserve all vertex streams, modern weights, bind poses, materials, renderer settings, and blend-shape values unless the new contract explicitly says otherwise.
3. Calculate every affected renderer before committing any source change, so a failure cannot leave a half-cut character.
4. Use stable UMA bone names or hashes for cross-skeleton matching; do not use Unity instance IDs.
5. Add focused editor tests for body and armor, seam-split loops, concave loops, modern weights, repeated cuts, rebuild cleanup, and undo.
6. Keep Unity `Mesh`, `GameObject`, renderer, and physics creation on the Unity main thread. Pure data preparation may be redesigned for jobs, but the current public operation is synchronous and main-thread only.

## Repeated cuts, undo, and rebuild behavior

By default, the same bone cannot be cut more than once during one UMA generation. Different bones can be requested, and later slices operate on the current already-modified source renderer meshes.

Practical consequences include:

- cutting a bone whose geometry has already left with an earlier piece can return **No Affected Geometry**;
- a later cut can reuse the existing cap material submesh instead of continually adding material slots;
- parent/child cut order can change which geometry remains available;
- every approved sequence should be tested, not only each cut in isolation.

Use `TryUndoDismemberment` for a complete gameplay or test reset:

```csharp
if (!dismemberment.TryUndoDismemberment(out string failure))
    Debug.LogWarning(failure);
```

It immediately restores the source renderer meshes, destroys every tracked detached root and its child physics/gore objects, clears repeated-cut tracking, exits the source `UMAPhysicsAvatar` ragdoll, and requests a rebuild of the current UMA recipe. The Play Mode Inspector button is **Undo Dismemberment**. `UndoDismemberments` exposes the same workflow to uGUI and should receive an explicit avatar/component reference in a multi-character scene.

Pass `rebuildAvatar: false` only when another system owns the regeneration sequence. Mesh restoration and detached-piece cleanup still occur, but no `DynamicCharacterAvatar.BuildCharacter` request is made.

`ResetDismemberment(true)` is the lower-level lifecycle API. It restores owned source meshes, clears repeated-cut tracking, and destroys detached pieces, but it does not perform the complete main-body unragdoll and UMA rebuild sequence. It is useful inside a custom regeneration flow; it is not the normal player-facing Undo operation.

Unity destroys GameObjects at the end of the frame in Play Mode, so detached roots can compare as pending-destroy immediately after Undo even though their generated meshes and tracked state have already been reset. Avoid reusing references to an old `DismembermentResult` after undo or avatar regeneration.

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

### The detached piece has no physics

- Enable **Ragdoll Dismembered Parts** on the completion callback.
- Assign at least one non-null per-cut **Detached Physics Definition** for the humanoid cut, or configure the callback's fallback list for a generic/legacy cut.
- Confirm that every retained definition's **Bone Name** exists in the detached cut subtree.
- Do not assign two different definitions targeting the same retained bone.
- Use **Rigid** to diagnose an unstable articulated chain.
- Read the builder warning. Failed construction rolls back its partial physics components before the legacy fallback is considered.

### The detached arm or leg stretches toward the body

Use the current slicer weight sanitation and avoid replacing detached weights in a custom callback. Influences above the cut subtree are removed automatically. Enable **Trim Detached Rig** to compact the bone palette and cloned hierarchy, and ensure supplemental Gib renderers are skinned to the retained cut subtree. A custom renderer that remains weighted to Shoulder, Spine, or Hips can still stretch even when the generated body/armor renderers are correct.

### Detached physics falls through the floor or collides incorrectly

- Verify **Ragdoll Layer** and the layer collision matrix in **Project Settings > Physics**.
- Confirm that the floor has an enabled, non-trigger Collider with adequate thickness.
- Check each `UMAPhysicsElement` collider size and center at one Unity unit per meter.
- Look for a project callback that changes the detached hierarchy's layer after the sample builder runs.
- Do not add overlapping fallback Rigidbody configurations after a successful definition-based build.

### Ragdoll Main Body is enabled but the character stays animated

The cut can succeed even when main-body ragdoll activation cannot. Confirm that the same character has a generated `UMAPhysicsAvatar` and valid full-body UMA physics recipe. Inspect the warning and the completion result's `mainBodyRagdollRequested` and `mainBodyRagdollActivated` values. The per-cut option activates existing physics; it does not build the source character's ragdoll definitions.

### Undo does not completely restore the character

Use `TryUndoDismemberment`, the Inspector's **Undo Dismemberment** button, or `UndoDismemberments.Undo`. Do not substitute `ResetDismemberment` for the full workflow. Keep **Rebuild Avatar** enabled unless another system rebuilds the current recipe. Objects parented under tracked detached roots are removed; unparented VFX, decals, or pooled objects must be cleaned up by their owning systems.

If the scene contains several UMA characters, assign the exact `UmaDismemberment` reference to the undo action. The sample intentionally refuses to guess among multiple candidates.

### A saved character loads intact

Cuts are not UMA recipe data. Save the ordered successful bone cuts as game state, wait for the rebuilt avatar to become ready, and replay them in the same order. Also restore health, fatal state, source ragdoll, detached physics, and cosmetic effects according to the game's own save format.

## Production checklist

Before approving a sliceable character set, verify:

- `UmaDismemberment` and `DynamicCharacterAvatar` are on the same GameObject.
- The component reports **Ready: Yes** before gameplay can request a cut.
- Every approved humanoid bone appears once in **Sliceable Human Bones**.
- Per-bone thresholds have been tested on all supported races and DNA extremes.
- Every row has the intended UV mode, detached physics mode, rig trimming, main-body fatality policy, and definitions.
- **Include Child Bones** produces complete detached limbs.
- Body and wardrobe meshes have readable triangle geometry.
- Intended cut boundaries are closed, manifold, and sufficiently dense.
- Cloth renderers have a defined alternative workflow.
- Fallback and every possible pipeline override material are assigned and supported.
- Meter-scaled cap UV density is correct at one Unity unit per meter; centered cap textures use Clamp and keep features inside the configured padding.
- Cap materials are tested in Built-in, URP, or HDRP as applicable.
- Detached physics definitions target unique bones in the retained subtree, use the correct Standard/HD rig, and collide on the intended layer.
- Source `UMAPhysicsAvatar` colliders below a severed cut are suspended when detached colliders are created and restored by Undo.
- Every **Ragdoll Main Body** cut has a working source `UMAPhysicsAvatar`, and nonfatal cuts leave it disabled.
- Physics, VFX, audio, cleanup, LOD, damage, and AI behavior are handled by a project-owned completion listener.
- Parent/child and repeated-cut sequences have been tested.
- UMA regeneration behavior matches the chosen rebuild policy.
- Full undo restores meshes, removes detached child objects, exits ragdoll, rebuilds the current recipe, and targets the correct character in a multi-avatar scene.
- Saved/networked dismemberment state records and replays cut order rather than serializing generated meshes.
- New Input System-only UI scenes use `InputSystemUIInputModule`.
- Fully dressed characters meet CPU time and memory budgets on the target hardware.

When a cut fails, use the component's **Last Failure Reason** and detailed message first. The operation is designed to fail without partially modifying source renderers, which makes those diagnostics safer to iterate on during content production.

## Flowing blood on a cut

The flowing system is optional. Particles are still useful for the initial spray; the runtime
surface fluid is the blood that remains attached to and travels over the character.

### Artist setup

1. Create **Assets > Create > UMA > Dismemberment > Surface Fluid Profile**.
2. Assign the profile to **Surface Fluid Profile** on `ExampleDismemberCallback`, or pass it from
   your own `DismembermentCompleted` listener.
3. Keep **Channels** at **Albedo** for the lowest cost. Add **Normal** only when the character's
   `UMAMaterial` has a compatible normal channel. Wetness also requires an exact dedicated
   **Wetness Material Property Name**; packed mask maps are never guessed or overwritten.
4. Tune physical dimensions in meters. A default cut source width of `0.003` is 3 mm; a breakup
   scale of `0.025` is 2.5 cm. **Trail Deposition Per Meter** controls how much moving fluid remains
   behind as a wet trail; `3` deposits about 45 percent over 20 cm. Do not compensate for an
   incorrectly scaled character here.
5. Choose routing. **Source Body** isolates detached material instances on the piece so only the
   survivor receives the live generated texture. **Shared Atlas** intentionally shows the same result on both
   renderers. **Independent Detached Piece** clones only the affected detached generated material,
   restores its immutable base channel bindings, and gives that renderer an independently owned GPU
   compositor and simulation. Its material clone is released with `DismemberedPiece`.
6. Test several poses, including upside down. Flow is projected from world gravity into the posed
   skinned surface field; it is not a fixed downward direction in UV space.

Both `Atlas` and `NoAtlas` generated UMA materials are supported. A `NoAtlas` body may have separate
generated textures for its head, torso, arms, and other slots even when they all share one
`UMAMaterial` definition. The fluid controller resolves each cut boundary by its renderer and exact
generated material/submesh index, so those textures remain independent. Slot identity is used only
as a rebuild-safe fallback; an ambiguous shared `UMAMaterial` name is never guessed.

The optional **Source Overlay** can supply the corresponding UMA channel textures and a shared alpha
mask. This is the supported material-like input for the generated-texture compositor. Arbitrary materials are only
appropriate for **Fallback Trail Material**, because an arbitrary shader does not define which pass
or channel semantics an atlas compositor should use.

### Timing and performance

The defaults cap the simulation at 512 pixels, simulate near 24 Hz, refresh the posed field near
8 Hz, and composite near 12 Hz. Holding effects stop simulation and do not recompose until they
start fading. Off-screen effects reduce their rates. Several cuts on one generated texture share its expensive
surface field, injection target, seam map, command buffer, and final outputs; each handle retains an
independent film state so clearing one cut cannot erase another.

Keep Albedo-only profiles for crowds. Increase resolution only when a close-up visibly needs a
narrower stream. Higher generated-texture resolution does not require equal simulation resolution: the film is
upsampled during the final composite. Mips are generated once after an affected output batch.

### Bleeding from a bullet or standalone RT decal

The surface fluid does not require a cut. Any successful `DecalRenderTexture.CreateDecalLayer`
operation can become an emitter:

```csharp
DecalRenderTexture.DecalLayerResult? result = DecalRenderTexture.CreateDecalLayer(
    avatar, shotRay, bulletRadius, 0f, 0f, avatar.umaData, bulletOverlay, options);

if (result.HasValue && result.Value.success)
{
    DecalRTStampAsset stamp = DecalRenderTexture.LastStamp;
    UMARuntimeSurfaceDecalController controller =
        avatar.GetComponent<UMARuntimeSurfaceDecalController>() ??
        avatar.gameObject.AddComponent<UMARuntimeSurfaceDecalController>();
    RuntimeDecalHandle wound = controller.AddPersistentStamp(stamp);
    RuntimeDecalHandle blood =
        controller.StartBleedFromDecal(stamp, bloodProfile, result.Value);
}
```

Capture `LastStamp` before placing another decal. The static cache always describes only the most
recent successful stamp. `AddPersistentStamp` registers the fixed multi-channel wound in the runtime
compositor and rebinds it after atlas rebuilds. The separate fluid call reuses the bullet's cached
target-UV geometry and projection radius, then injects a meter-sized center defined by **Emission
Radius Meters**. The emitter does not use the wound texture's alpha because its center may be
transparent. The `UMASurfaceFluidProfile` supplies the blood
color/material, trail deposition, meter-based motion, lifetime, and fade. Its slot/overlay target
filters are honored.

The overload that takes only a stamp is suitable when compute is guaranteed. Pass the complete
`DecalLayerResult` in normal gameplay so the non-compute fallback has the world hit point and normal.
The two returned handles are independent: stopping, fading, or clearing blood does not erase the
wound. The persistent wound rebinds after a full UMA rebuild. Fluid survives a rebuild only when
**Persist Across Avatar Rebuild** is enabled on its profile.

### Drawing a surface cut between two points

Create **Assets > Create > UMA > Dismemberment > Surface Cut Profile**. Set its full width in meters,
choose the dark center and pink side colors, and adjust the center, edge softness, and endpoint
taper fractions. **Bleed Spacing Meters** sets the average physical distance between emitters, so a
longer cut automatically bleeds from more locations. **Bleed Spacing Variation** randomly shortens
or lengthens each interval by the selected fraction, while **Bleed Spacing Seed** controls the local
random sequence without changing Unity's global random state. **Bleed Speed Variation** gives each
stream its own multiplier around the fluid profile's fall speed; that multiplier travels with the
mobile fluid and blends by fluid mass if streams meet. **Bleed Size Variation** changes each source's
emission radius, so larger sources create broader, fuller drips. Set spacing to zero for a dry cut.
**Bleed End Inset** keeps sources away from the tapered tips. Assign a Surface Fluid Profile to
control their base color, speed, radius, trail deposition, holding time, and fade, or leave it empty
for the runtime blood defaults. The system bounds unusually dense settings to 128 sources per cut.
Leave the fluid profile's **Target Overlay Groups** empty to bleed from every affected surface,
including jackets or armor. Populate it with one or more overlay-group names to emit only from
matching surfaces; matching follows the same empty-means-all convention as RenderTexture decals.
Use `Skin` to restrict a profile to UMA's default base-skin overlay group.

In the sample, normal left-click places a bleeding bullet wound. To make a cut, hold Shift and press
the left mouse button on the character, drag over the surface, and release. A thin red Game-view
line shows the pending cut from mouse-down to the current cursor. Right-click or Escape cancels the
pending drag. `TryCreateProjectedCut` supports adjacent UMA renderer, slot, and material boundaries,
including face-to-head and skin-to-clothing cuts. The system refreshes the stored triangle anchors
against the current animated pose on release, densely samples the straight drag over every visible
generated UMA surface, and writes a separate atlas-safe cut portion for each material. Those portions
share one cut handle and their bleed sources retain the material underneath each source. Up to four
missing four-pixel samples may bridge a narrow slot-border gap. A larger screen gap or a world-space
depth discontinuity is rejected so the cut cannot jump to unrelated geometry. The non-camera
`TryCreateCut` topology API remains a one-renderer/material operation; use `TryCreateProjectedCut`
for interactive cross-slot cuts.

### Fadeable runtime decals

`AddFadeableStamp` uses cached `DecalRTStampAsset` geometry and redraws it over the clean immutable
base with changing opacity. Permanent `DecalRTStampSlot` decals remain part of that base, so fading
a dynamic stamp or blood layer never removes them. Do not register a stamp dynamically after it was
already baked or it will appear twice. Use `AddPreviouslyBakedFadeableStamp` for that situation; its
single explicit rebuild obtains a clean base before dynamic registration.

### Diagnostics and debugging

`UMARuntimeSurfaceDecalController.Diagnostics` explains unmatched materials, missing loops, or GPU
fallbacks. `GetDebugTexture` exposes the composited output, posed world-position field, projected
surface flow, current injection mask, seam links, and mobile film state for scene/debug tooling.
Seams are linked only between matching position, normal, bone-weight signatures inside the same
generated material submesh. An unresolved edge deposits fluid instead of jumping to unrelated
clothing or another UV island.

The controller releases and restores its textures on component shutdown, destruction, UMA
`CharacterBegun`, undo, and normal completion. Active blood clears on dismemberment reset unless its
profile explicitly selects **Persist Across Avatar Rebuild**. Persistent effects resolve the new
generated material by durable cut metadata and rebuild their GPU state after `CharacterUpdated`.
Independent detached-piece effects never persist through a source-avatar rebuild because their
owned renderer/material lifetime is the detached root, not the regenerated avatar.

### Fluid troubleshooting

- **Only particles appear:** inspect controller diagnostics. The cut may have no closed boundary,
  the material may have no compatible texture channel, or compute may be unavailable.
- **The line is magenta:** assign a supported **Fallback Trail Material** or ensure the packaged
  hidden fallback shader is included.
- **A temporary red line appears:** the generated texture target could not be used, so the bounded
  geometry fallback is active. It follows the affected character or detached part and removes
  itself using **Fallback Holding Duration**, **Fallback Fade Duration**, and **Fallback Maximum
  Lifetime**. Check controller diagnostics to correct the underlying target when GPU bleeding was
  expected.
- **A repeated cut reports a vertex-stride mismatch while the character still renders:** make sure
  the current runtime code is present. Dismemberment invalidates the controller's hidden
  surface-field command before replacing a skinned mesh, waits one frame for Unity's deformation
  buffers to adopt the new layout, and does not call `BakeMesh` in the replacement callback.
- **Blood appears on the detached piece unexpectedly:** use **Source Body**. Use **Shared Atlas**
  only when sharing is intentional.
- **A stream stops at a seam:** verify the duplicated vertices share position, normal, and weights.
  Stopping is the safe fallback; cross-slot transfer is never guessed.
- **Blood appears on the head instead of a cut arm or torso:** confirm the cut surface reports the
  intended submesh in controller diagnostics. `NoAtlas` outputs that share one `UMAMaterial` must be
  resolved by generated material index or slot identity, never by the shared material name alone.
- **Blood is too fast or too broad:** check character scale first, then tune meter-based speed,
  source radius, viscosity, adhesion, and spread.
- **A permanent decal disappeared:** this is not expected. The dynamic controller copies the final
  generated atlas after permanent callbacks. Record the profile, material property, and rebuild
  sequence and inspect whether another system replaced that material texture after composition.
