# U3-GoreExample sample scene

`Scene/U3-GoreExample.unity` is a compact integration example for the UMA 3 runtime dismemberment system. It demonstrates a generated UMA character, bone-specific uGUI cut actions, procedural caps, optional detached physics and blood, fatal versus nonfatal cuts, and restoration of the original avatar.

For mesh-authoring and production guidance, see the [Artist Setup and Production Guide](../ARTIST_GUIDE.md).

## What is in the scene

The example is organized around four pieces:

- A generated UMA avatar with `DynamicCharacterAvatar` and `UmaDismemberment` on the same GameObject.
- uGUI Buttons using `GUIDismemberment`. The supplied layout demonstrates neck, left/right upper-arm, and left/right lower-leg requests; each Button targets one configured `HumanBodyBones` value and calls `TrySlice`.
- `ExampleDismemberCallback`, which listens to `DismembermentCompleted` and can add detached physics, a separation impulse, blood particles, and optional supplemental gore renderers.
- A U3-Decals-style click handler installed by `ExampleDismemberCallback`: left-clicking the
  generated character stamps the sample bullet wound into its RenderTextures and immediately starts
  standalone surface bleeding from the center of the same cached UV footprint. The wound decal is
  retained as a persistent compositor layer while the meter-scaled fluid source seeps downward and
  deposits a trail. It does not require an overlay group; assign **Bullet Target Overlay Group** only
  if another caller also stores the stamp in a `DecalRTStampSlot` for normal atlas-event replay.
- A Shift-drag surface-cut gesture. Hold Shift and press the left mouse button for the start, drag
  across adjacent generated body, face, head, clothing, or armor surfaces, and release for the end.
  Normal left-click still places the bleeding bullet decal. A thin red line previews the drag in
  Game view using an always-visible unlit pass for Built-in, URP, and HDRP. The cut has a
  tapered dark-red center, pink irritated edges, and several independent bleeds along its surface
  route. Right-click or Escape cancels a drag.
- An undo action using `UndoDismemberments`, which restores the avatar rather than merely hiding the detached pieces.

The camera, lights, floor, and UI are ordinary scene presentation. The important reusable setup is on the avatar and Button actions.

## Before entering Play Mode

1. Confirm that the scene's UMA race, wardrobe, animator controller, and shared UMA libraries are available.
2. Select the avatar and verify that `UmaDismemberment` resolves a supported **Fallback Material** or **Pipeline Override**.
3. Expand **Sliceable Human Bones, Cap UV and Physics** and inspect the threshold, UV mapping, physics mode, rig trimming, main-body ragdoll, and definitions for every exposed cut.
4. If main-body ragdoll is enabled for a cut, verify that the avatar has a working `UMAPhysicsAvatar` and full-body UMA physics recipe before testing dismemberment.
5. If detached physics is enabled, verify that **Ragdoll Layer** collides with the floor and props in **Project Settings > Physics**. Collider dimensions in `UMAPhysicsElement` assets assume one Unity unit is one meter.
6. Enter Play Mode and wait until the `UmaDismemberment` Inspector reports **Ready: Yes**.
7. Left-click directly on the character to place a bullet wound and start surface bleeding. Hold
   Shift, press the left mouse button on the character, drag, and release to create a surface cut.
   Assign **Bullet Fluid Profile** to override the puncture preset and optionally assign
   `Profiles/Realistic Surface Cut` to **Surface Cut Profile**. UI clicks are ignored.

If the project is set to the new Input System only, the scene `EventSystem` should use `InputSystemUIInputModule`. The sample cut and undo scripts use `Button.onClick` and do not poll either legacy `Input` or `Keyboard` directly, so they also work with custom UI that invokes the same public methods.

## Following one cut

When a cut Button is pressed:

1. `GUIDismemberment` resolves the intended `UmaDismemberment` and calls the humanoid `TrySlice` overload.
2. The slicer validates readiness, the allowed-bone row, cap material, CPU-readable triangle data, and cut boundaries.
3. It calculates all affected body and wardrobe renderers before committing any source change.
4. It creates owned source meshes, a detached skeleton, detached renderers, opposing caps for
   `Skin` body surfaces, 10 cm two-sided interior bands for clothing, and a `DismemberedPiece` owner.
5. Detached weights above the cut subtree are removed and normalized. If **Trim Detached Rig** is enabled, the detached bone palettes and cloned skeleton are then compacted.
6. If **Ragdoll Main Body** is enabled, the source character's existing `UMAPhysicsAvatar` is activated after the cut commits.
7. `DismembermentCompleted` is invoked. The sample callback can now construct detached physics, disable the matching source-ragdoll colliders, apply its view-directed impulse, spawn blood, and attach optional gore renderers.

The cut either commits for all affected renderers or fails without leaving a partially modified character. Inspect **Last Failure Reason** and the detailed warning when a Button appears to do nothing.

## Configuring the detached piece

Enable **Ragdoll Dismembered Parts** on `ExampleDismemberCallback`, then configure each humanoid cut on `UmaDismemberment`:

- **Automatic** creates a rigid compound piece from one distinct definition and an articulated partial ragdoll from multiple retained definitions.
- **Rigid** creates one root Rigidbody with compound colliders. This is stable for a head, lower limb, or armor piece.
- **Articulated Ragdoll** creates one Rigidbody per definition and joins included parent/child bodies.
- **None** suppresses both definition-based physics and the callback's legacy simple fallback for that cut.

Assign only definitions that belong to the detached side. Definitions above the cut subtree are filtered automatically. Repeated references to the same asset are collapsed, but different definitions targeting the same bone are rejected as ambiguous.

The callback fields have these roles:

- **Fallback Physics Definitions** supports legacy scenes and generic `Transform` cuts. A humanoid row with non-null per-cut definitions takes precedence.
- **Ragdoll Layer** is assigned to generated detached bodies and colliders.
- **View Camera** supplies the separation direction. `Camera.main` is used when empty.
- **Separation Impulse** defaults to `0.5` kilogram-meters per second and pushes each free detached root along the camera view through the character.
- **Blood Particle Emitter** is instantiated at the cut bone. The prefab should play on awake and clean itself up.
- **Surface Fluid Profile** starts non-destructive GPU blood at the actual UV cut boundary. It is
  optional and complements the initial particle spray.
- **Add Physics** is a legacy one-Rigidbody/one-SphereCollider fallback when definition-based construction is not used or fails.
- **Gib Split** and **Gib Split Material** add an optional skinned gore renderer to the detached side.
- **Gib Source** and **Gib Source Material** add an optional skinned gore renderer to the surviving side.

The Gib renderers are supplemental geometry. They are not placed on the procedural cap automatically and they do not perform the cut. The callback remaps their skeleton references by stable UMA bone-name hash.

After the callback successfully adds one or more detached colliders, it calls `SuspendSourceRagdollColliders` for the original source cut bone. Only colliders registered to the source character's `UMAPhysicsAvatar` and located on that bone or its descendants are disabled. This removes duplicate/invisible collision from the surviving ragdoll without disabling unrelated hitboxes or controller colliders. Full Undo, reset, component shutdown, and the next UMA generation restore their previous enabled states.

## Fatal and nonfatal cuts

**Ragdoll Main Body** is a per-row gameplay decision, not an anatomical assumption made by the slicer. A hand or lower-arm loss can remain nonfatal, while a head or upper-leg loss can incapacitate the character. When enabled, the result reports both `mainBodyRagdollRequested` and `mainBodyRagdollActivated`, so gameplay code can distinguish configuration intent from successful ragdoll activation.

The option reuses the character's normal UMA ragdoll event path. AI or movement systems already listening for UMA ragdoll state, such as the ragdoll sample's walker, stop and resume through their existing subscriptions. It does not create a full-body physics recipe; configure `UMAPhysicsAvatar` first.

## Undoing the example

`UndoDismemberments.Undo()` calls `TryUndoDismemberment` on its explicit component, its assigned avatar, or the only active dismemberment component in the scene. Assign the target explicitly when more than one UMA character exists.

Full undo:

- restores the original source meshes;
- destroys all tracked detached roots and their child colliders, ragdolls, and gore objects;
- clears repeated-cut tracking;
- exits the source character's ragdoll;
- rebuilds the current UMA recipe by default.

The rebuild is important because it restores a coherent skeleton, animation, and full-body physics state. Disable **Rebuild Avatar** only when another system owns the regeneration sequence.

## Copying the sample into another scene

1. Add `UmaDismemberment` to the target avatar GameObject.
2. Copy the cap, bone-row, and lifecycle settings that are valid for that avatar's race and wardrobe. Do not assume the sample thresholds fit different topology.
3. Add `ExampleDismemberCallback`, or copy its event-listener pattern into a project-owned component.
4. Configure the target character's full-body ragdoll separately if any cut uses **Ragdoll Main Body**.
5. Assign per-cut detached physics definitions and confirm the physics collision matrix.
6. Connect gameplay or UI to `TrySlice`; keep an explicit component reference in multi-character scenes.
7. Add an undo/revive action only if the game supports restoration. Prefer `TryUndoDismemberment` over the lower-level `ResetDismemberment` API.
8. Test the final race, DNA extremes, wardrobe combinations, animation poses, and intended cut order.

For surface blood, create a `UMASurfaceFluidProfile` and assign it to the callback. The sample adds
`UMARuntimeSurfaceDecalController` automatically only when the profile is assigned. Existing
`DecalRTStampSlot` components do not need to be removed or reconfigured: their permanent decals are
already present in the immutable base atlas copied by the runtime compositor.

`Profiles/Realistic Blood Surface Fluid` is a ready-to-tune, Albedo-only metric starting point. It
is deliberately not assigned in the scene, so importing the addon does not change the sample's
existing rendering or allocate runtime textures until an artist opts in.

`Profiles/Realistic Surface Cut` supplies an 8 mm tapered cut and references the realistic blood
profile. Emitters are spaced an average of 2.5 cm apart with 30% interval variation, so longer cuts
automatically receive more bleed sources. Each emitter also varies by 25% in fall speed and 30% in
radius, producing streams with distinct widths and travel rates. The reusable runtime API is
`UMASurfaceCutSystem`: call
`TryGetSurfacePoint` for both rays, then `TryCreateProjectedCut` for mouse input so the committed cut
matches the straight preview while wrapping over the posed surface. Projected cuts may cross adjacent
UMA slots and generated materials (for example, face to head or skin to jacket). The system samples
the visible surface and writes one cut portion per material atlas under a single result handle. Small
slot-border gaps are bridged, but a route that leaves the visible character or jumps across a large
world-space depth discontinuity is rejected instead of drawing through unrelated geometry. The
non-camera `TryCreateCut` topology API remains intentionally limited to one renderer/material.

The blood profile's **Target Overlay Groups** list is empty by default, so a multi-slot cut can
bleed across both skin and cut clothing such as a jacket. Add `Skin` to that list when an effect
should emit only from UMA's default base-skin overlay group.

Generated `NoAtlas` skin textures are supported directly. When head, torso, and limb slots share
the same `UMAMaterial` definition but have separate generated textures, the sample binds each cut
surface by its exact renderer/submesh output. It does not use the shared material name as a shortcut.

Do not edit the sample callback as the long-term project integration. Copy the pattern into the project's namespace so package updates do not overwrite gameplay-specific behavior. The completion result is designed to let that code add audio, decals, scoring, inventory drops, networking messages, timed cleanup, or custom physics without changing the slicer.

## Common sample issues

- **Button does nothing:** wait for **Ready: Yes**, check the Button target, and read **Last Failure Reason**.
- **Cap is pink:** assign a material whose shader supports the active render pipeline asset.
- **A cap is missing:** repair the authored loop or inspect **Seam Weld Tolerance**; keep **Require Closed Caps** enabled while validating content.
- **The detached arm stretches toward the torso:** keep the current weight sanitation code, enable **Trim Detached Rig**, and use only cut-subtree physics definitions.
- **Detached pieces fall through the floor:** confirm the generated ragdoll layer collides with the floor layer and that the floor has a non-trigger Collider.
- **Main body does not ragdoll:** add/configure `UMAPhysicsAvatar` and its UMA physics recipe before relying on the per-cut option.
- **Undo targets the wrong character:** assign `UndoDismemberments.dismemberment` explicitly.
- **Surface cut mouse-up fails:** begin and release the Shift-drag on the same connected body or
  armor material. A cut intentionally does not bridge separate renderers, submeshes, or large
  atlas seams.
- **The intact character returns after a wardrobe or DNA change:** this is expected; cuts are runtime state and are not stored in the UMA recipe. Store and replay an ordered cut list if the game needs persistence.
