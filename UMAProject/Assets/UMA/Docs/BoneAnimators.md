# UMA Bone Animators

Bone animators are `ScriptableObject` assets (derived from `BaseUpdatedObject`) that you
assign to a `SlotDataAsset`. At runtime UMA finds them, initializes them against the
skeleton, and drives them each physics step. They handle hair sway, jiggle physics, cloth
simulation, twist chains, and other per-bone animation effects without requiring custom
MonoBehaviours in your character prefab.

---

## How they all work (generic)

### The runtime pipeline

```
Character built
      │
      ▼
UMAData.SetupEmbeddedPhysics()
   iterates every SlotData in the recipe
   iterates each slot's animatedBones[] array
      │
      ├─ animator.Initialize(umaData, slot)
      │     finds bones by name, adds components, caches state
      │
      ▼
Every FixedUpdate frame:
   UMAData.FixedUpdate()
      └─ animator.FixedUpdate()  →  animator.DoUpdate(umaData, step)
```

### Steps to use any animator

1. **Create the asset** — right-click in the Project window, navigate to
   **Create → UMA → Physics → *AnimatorName***. A `.asset` file appears.

2. **Configure it** — select the asset and fill in the Inspector fields (anchor bone
   names, physics parameters, etc.).

3. **Assign to a slot** — select the `SlotDataAsset` that provides the bones you want
   to animate. In its Inspector, locate the **Animated Bones** array (under the
   *Animated Bones Selection* foldout). Click **+** and drag your animator asset into
   the new element.

4. **(Optional) protect from baking** — if you use the *Bone Baking Mesh Combiner*,
   add the bone names in any chains to the slot's **Unbaked Animated Bones** list so the
   combiner does not bake those bones out of the rig.

5. **Generate a character** — the animator initializes automatically.

> **Tip:** If an animator logs `Anchor bone '...' not found in skeleton`, double-check
> that the bone name matches exactly what is in the skeleton/rig.

---

## Individual animator reference

### SwayBoneAnimator

**Menu path:** `Assets → Create → UMA → Physics → SwayBoneAnimator`

Legacy bone-chain jiggler for ponytails, hair, tails. Simpler but less realistic than
`UMAChainJiggleAnimator`.

| Field | Description |
|---|---|
| `AnchorBoneName` | Root bone of the chain. All children are recursively animated. |
| `Inertia` (0–1) | How much force persists each step. Lower = settles faster. |
| `Limit` (1–2) | Maximum stretch multiplier from rest position. |
| `Elasticity` (1–4) | How strongly the chain pulls back during movement. |

At runtime this adds `SwayRootBone` to the anchor and `SwayBone` components to each
child bone. The physics model is simple: it tracks world-position deltas, applies them
as force, and clamps to a distance limit. There is no spring return force, no gravity,
and rotation is disabled.

**When to use:** Lightweight sway for short chains. For longer/more realistic chains,
prefer `UMAChainJiggleAnimator`.

---

### UMAChainJiggleAnimator

**Menu path:** `Assets → Create → UMA → Physics → UMAChainJiggleAnimator`

A particle-chain solver for ponytails, tails, ropes, skirts, and
hanging clothing bones. Uses the same stable integrator as `UMA_JiggleBreasts`.

| Field | Default | Description |
|---|---|---|
| `Chains` | empty | One entry per independent chain. Use one entry for a single chain, or two entries for pigtails, left and right hair chains, paired skirt chains, etc. Each entry has an `AnchorBoneName` and optional excluded bone names. |
| **Terminal Bones** | | |
| `End Length` | 0.15 | Virtual tip length so the last real bone can rotate. Set to 0 if your chain already has end bones. |
| `End Offset` | (0,0,0) | Explicit local offset for the virtual tip. Zero auto-follows the parent-to-leaf direction. |
| **Physics** | | |
| `Stiffness` (0–1) | 0.15 | Spring strength pulling particles back to rest. |
| `Mass` (0.001–5) | 0.9 | Resistance to acceleration. |
| `Damping` (0–1) | 0.15 | Velocity reduction per step. Higher settles faster. |
| `Gravity` (0–2) | 0.1 | Downward world-space acceleration. |
| `Inertia` (0–1) | 0.65 | How much parent movement pushes particles. |
| `Max Distance` | 0.35 | Maximum world-space distance from rest target. |
| `Constraint Iterations` (1–8) | 3 | Length-constraint passes. Use 2–4 for longer chains. |
| **Bone Output** | | |
| `Rotation Weight` (0–1) | 1 | How much bones rotate toward simulated child positions. Use **1** for normal chains. |
| `Position Weight` (0–1) | 0 | Direct joint translation. Use **0** for skinned bone chains. |
| **Freeze Axes** | | |
| `Freeze X / Y / Z` | off | Prevent movement on that world axis. |

The inspector includes an **Add Chain From Slot** helper. Drop or assign a
`SlotDataAsset`, choose an anchor bone from the slot's UMA bones, then click
**Add a chain for this bone** to append a `Chains` entry.

**Key difference from SwayBoneAnimator:** This simulates particles, enforces bone
lengths, provides spring-back and gravity, and rotates bones naturally. It is the
recommended replacement for most use-cases.

---

### DynamicBoneAnimator

**Menu path:** `Assets → Create → UMA → Physics → DynamicBoneAnimator`

Wrapper for the third-party **DynamicBone** asset. The DynamicBone integration is
disabled by default (`#if false`). Currently falls back to adding `SwayRootBone`
components instead.

| Field | Description |
|---|---|
| `Animated Root Bone Names` | Array of bone names to animate. |
| `Exceptions` | Bone names (and children) to exclude. |
| `Reduce Effect` (0–1) | Scales the inertia/reduce-effect parameter. |

**Note:** To use actual DynamicBone, change `#if false` to `#if true` in the source and
ensure the DynamicBone package is imported.

---

### MC2BoneAnimator

**Menu path:** `Assets → Create → UMA → Physics → MC2BoneAnimator`

Wrapper for the third-party **Magica Cloth 2** asset, bone-cloth mode.

| Field | Description |
|---|---|
| `Animated Root Bone Names` | Array of root bones to animate. |
| `Preset File` | Optional Magica Cloth 2 JSON preset. |
| `Bone To Exclude Names` | Bones marked as **Fixed** (VertexAttribute.Invalid) so they don't move. |

**Requirements:**
- Magica Cloth 2 package imported.
- `MAGICACLOTH2` scripting define symbol added in Player Settings.
- MC2 asmdef added to `UMA_Core.asmdef` references.

---

### MC2ClothAnimator

**Menu path:** `Assets → Create → UMA → Physics → MC2ClothAnimator`

Wrapper for Magica Cloth 2, **mesh-cloth** mode. Attaches to a renderer's mesh rather
than individual bones.

| Field | Description |
|---|---|
| `MC PaintMap` | Magica Cloth 2 paint-map texture (Red=Fixed, Green=Move, Black=Ignore). Must have **Read/Write** enabled. |
| `Preset File` | Optional Magica Cloth 2 JSON preset. |

**Requirements:** Same as MC2BoneAnimator plus a valid renderer assigned to the slot.

---

### TwistBoneAnimator

**Menu path:** `Assets → Create → UMA → Physics → TwistBoneAnimator`

Drives chain-twist bones based on a driver bone's rotation. Common use: forearm twist
bones that rotate when the hand rotates.

| Field | Description |
|---|---|
| `Driver Bone Name` | Bone that drives the twist (e.g. `l_hand`). |
| `Driver Axis` | Which axis of the driver bone to track (X, Y, or Z). |
| `Twist Bones` | List of twist bones, each with a bone name and twist ratio (0–1). |
| `Debug Mode` | Logs registration details to the console. |

Each twist bone's `Twist Ratio` controls how much of the driver's rotation it inherits.
Ratio 0.5 on the mid-forearm twist bone means it rotates half as much as the hand.

At runtime this registers with a `TwistBoneManager` on the UMA Generator GameObject.

---

### ShoulderControllerAnimator

**Menu path:** `Assets → Create → UMA → Physics → Shoulder Controller Animator`

Redistributes part of the animated upper-arm motion into the clavicle/shoulder and
then solves the arm back to its Animator-produced endpoint. This improves shoulder
silhouettes without translating bones or changing the intended hand position.

| Field | Default | Description |
|---|---|---|
| `Shoulder Bone Name` | *(required)* | Clavicle/shoulder bone that receives the procedural rotation. |
| `Arm Bone Name` | *(required)* | Upper-arm descendant driven by that shoulder. |
| `Lower Arm Bone Name` | empty | Optional override. Otherwise the Humanoid mapping or hierarchy is used. |
| `Hand Bone Name` | empty | Optional override. Otherwise the Humanoid mapping or hierarchy is used. |
| `Torso Reference Bone Name` | empty | Optional chest reference used to construct anatomical axes. |
| `Opposite Shoulder Bone Name` | empty | Optional opposite-side reference that improves the right-axis estimate. |
| `Side` | Auto | Uses Humanoid mappings, bone-name tokens, then the generated hierarchy to identify left or right. |
| `Endpoint Mode` | Hand When Available | Preserves the hand position with a two-bone solve, falling back to the upper-arm endpoint when necessary. |
| `Overall Effect` | 1 | Master influence for all shoulder channels. |
| `Elevation / Protraction / Retraction / Posterior Roll Effect` | varies | Per-channel 0–1 influence. |
| `Maximum ... Degrees` | varies | Rotation limit for each channel. |
| `... Response` | built-in curves | Maps the final arm direction to each channel's response. |
| `Endpoint Tolerance` | 0.0005 | Maximum world-space endpoint error. Influence is reduced when the requested shoulder motion is unreachable. |
| `Damping Half Life` | 0 | Optional temporal smoothing. Zero evaluates directly from the current animated pose. |
| `Preserve Hand Rotation` | on | Restores the Animator-produced hand world rotation after solving. |

The controller runs in `LateUpdate`, after normal Animator evaluation and before the
UMA twist-bone manager. A custom IK pipeline can disable `Automatic Update` on the
generated character's `ShoulderControllerRuntime` and call `EvaluateNow()` after its
own final IK pass.

#### Coordinate-space contract

- Arm-direction analysis and endpoint solving use the final generated world-space
  transforms.
- `Root`, `Global`, `Position`, external skeleton roots, DNA adjustments, imported
  bone roll, and left/right hierarchy differences are therefore already included.
- `RaceData.FixupRotations` is not applied a second time.
- Anatomical right/up/forward axes are rebuilt as an orthonormal basis from the
  generated torso and shoulder positions. Negative-scale/reflected ancestry is
  detected for diagnostics but is not allowed to reflect the rotation basis.
- Shoulder and compensating arm rotations are written through the actual parent
  hierarchy. Minimal from-to corrections retain the animated arm twist.

Use one asset for each independently configured shoulder. Add the shoulder, upper arm,
lower arm, and hand to **Unbaked Animated Bones** if Bone Baking Mesh Combiner is
enabled.

---

### PelvisControllerAnimator

**Menu path:** `Assets → Create → UMA → Physics → Pelvis Controller Animator`

Redistributes bilateral leg motion into the single Hips/pelvis bone, stabilizes the
spine independently, and solves both legs back toward their desired endpoints. It
runs before `ShoulderControllerRuntime`, allowing the shoulder solver to observe the
final corrected torso frame.

The Hips, upper-leg, lower-leg, foot, toe, spine, and upper-body reference names can
be overridden for Generic rigs. Empty fields use Humanoid mappings where available.
Only one Pelvis Controller may drive a character's Hips transform.

#### Pelvis channels

| Field | Description |
|---|---|
| `Stride Rotation Effect` | Transverse pelvis rotation driven by the normalized difference between left and right stride. |
| `Obliquity Effect` | Pelvic hike/drop driven by the optional planted-foot support difference. |
| `Pelvic Tilt Effect` | Optional sagittal correction driven by common forward/back leg motion. It defaults to off. |
| `Torso ... Follow` | Per-axis amount of the new pelvis correction inherited by the upper spine. Zero preserves the Animator-produced upper-torso world orientation. |
| `Animated Endpoint Preservation` | Foot preservation used with Foot IK disabled. It defaults to one. |
| `Swing Foot Preservation` | Soft endpoint constraint for a non-planted foot when Foot IK is enabled. |
| `Plant Threshold / Foot Lock Hysteresis` | Determines when a provider foot becomes or remains the support foot without contact-weight chatter. |
| `Airborne Effect` | Pelvis correction retained when an active Foot IK source reports both feet airborne. It defaults to zero. |
| `Minimum Knee Flexion` | Prevents the two-bone solve from locking a knee perfectly straight. |

The requested pelvis correction is limited to the largest contiguous influence
reachable by both legs. Animated knee positions are retained as bend-plane poles, and
foot/toe world rotations can be restored after solving.

#### Optional Foot IK

Foot IK defaults to `None`. In this mode the controller requires no Animator IK Pass,
Humanoid avatar, ground raycasts, or provider. It preserves the final animated foot
poses according to `Animated Endpoint Preservation`.

| Mode | Behavior |
|---|---|
| `None` | No IK-pass work. Works with Humanoid and Generic rigs. |
| `Automatic` | Uses an `IUMAFootIKProvider` when found, otherwise captures a current Unity Humanoid IK pass when available. |
| `Unity Humanoid Post Solve` | Captures goals and weights in `OnAnimatorIK`, then preserves Unity's solved feet during the pelvis correction in `LateUpdate`. |
| `Goal Provider` | Consumes world-space goals from an `IUMAFootIKProvider`. On a Humanoid rig, goals can be submitted to Unity during `OnAnimatorIK`; Generic rigs are solved directly. |
| `External Post Solve` | Treats the current externally solved foot transforms as hard constraints. The external solver must run before the Pelvis Controller. |

An `IUMAFootIKProvider` supplies optional position/rotation weights, planted weight,
knee hint, and ground normal for each foot. Unity Humanoid IK modes require **IK Pass**
on the selected Animator Controller layer. Custom pipelines can disable
`AutomaticUpdate` on `PelvisControllerRuntime` and call `EvaluateNow()` after their
foot targets or external solve are ready.

The controller follows the same coordinate-space contract as the Shoulder Controller:
all analysis and solving uses final world transforms, reflected ancestry is detected
without reflecting the anatomical basis, and `RaceData.FixupRotations` is not applied
again.

Add Hips, both complete leg chains, and the configured spine bones to
**Unbaked Animated Bones** when Bone Baking Mesh Combiner is enabled.

---

### UnityJointAnimator

**Menu path:** `Assets → Create → UMA → Physics → UnityJointAnimator`

Sets up Unity physics joints (Rigidbody + CharacterJoint + SphereCollider) on a chain of
bones. The last bone acts as a pendulum with gravity.

| Field | Default | Description |
|---|---|---|
| `AnchorBoneName` | *(required)* | Root anchor bone. |
| `Swing Bone Names` | *(required)* | Chain of bones that will swing. Last bone is the pendulum. |
| `Swing Mass` | 1.0 | Mass of swing bone rigidbodies. |
| `Swing Drag` | 0.6 | Drag on swing rigidbodies. |
| `Swing Angular Drag` | 0.6 | Angular drag on swing rigidbodies. |
| `Swing Radius` | 0.04 | Sphere collider radius for swing bones. |
| `Anchor Collider Radius` | 0.09 | Sphere collider radius for anchor. |
| `Anchor Offset` | (0.06, 0, −0.09) | Collider offset from anchor bone. |
| `Bone Layer` | 8 | Physics layer assigned to the bones. |
| `Freeze Positions` | off | Constrain rigidbodies to rotation only (pendulum mode). |
| `Apply Global Forces` | on | Apply world-movement forces to the pendulum. |
| `Min/Max Global Force` | 0.1 / 1.0 | Force range from world movement. |
| `Force Multiplier` | 100 | Movement-to-force scale. |

**Note:** This uses real Unity physics, so bones need appropriate collision layers and
the scene must have a physics setup. Best for heavy, pendulum-like chains.

---

### UnitySpringJointAnimator

**Menu path:** `Assets → Create → UMA → Physics → UnitySpringJointAnimator`

Creates Unity `Rigidbody` and `SpringJoint` components directly on an UMA bone
hierarchy. Each animated bone is connected to the preceding bone, while the top-level
anchor remains kinematic and follows the character animation. The result reacts to
gravity, character movement, and optional collisions using Unity's built-in physics
solver.

Use this animator for hair, tails, hanging accessories, and other chains that should
stretch and settle like springs. For animation-driven jiggle without Unity
rigidbodies, use `UMAChainJiggleAnimator` instead.

#### Defining chains

| Field | Default | Description |
|---|---|---|
| `Chains` | empty | Multi-chain configuration. Add one entry for each independent chain, such as the left and right sides of pigtails. When this list contains entries, the legacy single-chain fields are ignored. |
| `Chains > Anchor Bone Name` | *(required)* | Root and connection point for the chain. It is kinematic unless it is also an animated child of another configured chain. |
| `Chains > Spring Bone Names` | empty | Optional ordered list of animated bones. Leave empty to discover registered descendants automatically. Explicit bones must be descendants of the anchor. |
| `Chains > Excluded Bone Names` | empty | Subtrees skipped during automatic discovery. This is useful when an anchor has helper or unrelated child branches. |
| `Anchor Bone Name` | empty | Legacy single-chain anchor. Used only when `Chains` is empty. Existing assets that only specify this field continue to work. |
| `Swing Bone Names` | empty | Optional ordered bone list for the legacy anchor. Leave empty for automatic descendant discovery. |
| `Max Depth` | 0 | Maximum hierarchy depth processed below each anchor. Zero processes the complete hierarchy. |
| `Registered Bones Only` | on | Limits automatic discovery to transforms registered in the UMA skeleton. Keep enabled for normal UMA content. Disable only when a chain intentionally uses unregistered helper transforms. |

The inspector includes an **Add Chain From Slot** helper. Drop or assign a
`SlotDataAsset`, choose an anchor from its UMA bones, and click
**Add a chain for this bone**. You can then add explicit spring bones or exclusions to
the new chain entry if automatic discovery is not appropriate.

#### Spring settings

| Field | Default | Description |
|---|---|---|
| `Spring` | 50 | Force that pulls a segment back toward its initial distance from the preceding body. Higher values make the chain firmer and faster to return. |
| `Damper` | 5 | Resistance to oscillation. Increase this if the chain continues bouncing for too long. |
| `Min Distance` | 0 | Minimum-distance offset Unity applies relative to the segment's initial distance. |
| `Max Distance` | 0 | Maximum-distance offset Unity applies relative to the segment's initial distance. |
| `Tolerance` | 0.025 | Distance error accepted by Unity's joint solver. |
| `Enable Connected Body Collision` | off | Allows adjacent bodies connected by a spring to collide. Leave off for most hair and accessory chains to reduce instability. |
| `Enable Preprocessing` | on | Enables Unity's joint preprocessing. Disable only when diagnosing an unstable or impossible joint configuration. |

`SpringJoint` attempts to preserve the distance between the two rigidbodies when the
joint starts. `Min Distance` and `Max Distance` are evaluated relative to that initial
separation; they are not absolute bone lengths.

#### Rigidbody settings

| Field | Default | Description |
|---|---|---|
| `Bone Mass` | 0.1 | Mass assigned to rigidbodies created on animated bones. |
| `Linear Damping` | 0.15 | Slows translational motion. |
| `Angular Damping` | 0.15 | Slows rotational motion. |
| `Use Gravity` | on | Applies the project Physics gravity to animated bones. |
| `Interpolate` | on | Interpolates dynamic rigidbodies for smoother rendered motion. |
| `Collision Detection` | Discrete | Collision mode for animated rigidbodies. Use a continuous mode only for fast chains with colliders. |
| `Bone Constraints` | None | Optional Rigidbody position or rotation constraints. |
| `Max Angular Velocity` | 20 | Angular-velocity limit for animator-created rigidbodies. Zero keeps Unity's project default. |
| `Max Depenetration Velocity` | 3 | Maximum speed used to separate overlapping colliders. Zero keeps Unity's project default. |

#### Collider and layer settings

| Field | Default | Description |
|---|---|---|
| `Add Bone Colliders` | off | Adds an owned `SphereCollider` to each animated bone. Enable only when the chain needs physical collision. |
| `Bone Collider Radius` | 0.025 | Radius of animated-bone colliders. |
| `Add Anchor Colliders` | off | Adds an owned `SphereCollider` to fixed chain anchors. |
| `Anchor Collider Radius` | 0.04 | Radius of anchor colliders. |
| `Anchor Collider Center` | (0,0,0) | Local offset of anchor colliders. |
| `Bone Layer` | -1 | Layer assigned to configured objects. `-1` preserves the layer already on each bone. Use a dedicated physics layer when colliders are enabled. |

#### Runtime behavior and safe rebuilding

- Physics components created by the animator are marked as animator-owned. Rebuilding
  the same UMA reuses those components instead of adding duplicates.
- If a configured chain becomes shorter or an exclusion is added, stale owned joints,
  rigidbodies, and colliders are removed.
- Existing artist-authored rigidbodies are used as connection points without changing
  their mass, gravity, damping, or constraints.
- Existing colliders are not modified. Optional colliders created by the animator are
  tracked separately.
- Changing `Bone Layer` back to `-1`, or removing a bone from the chain, restores the
  original layer.
- Destroying the UMA destroys both current skeleton components and any spring physics
  attached to those bones.

#### Practical setup

1. Create the animator asset and assign it to the slot that supplies the spring bones.
2. Add a chain and select the bone immediately above the first bone that should move
   as its anchor.
3. Start with automatic discovery. Use an explicit ordered list when the hierarchy
   contains multiple branches, or add exclusions for branches that should remain
   animation-driven.
4. Leave colliders disabled while tuning the motion. Adjust `Spring`, `Damper`, mass,
   and gravity first.
5. If collision is needed, enable bone colliders, assign a dedicated `Bone Layer`, and
   configure the Physics collision matrix so hair or accessories only collide with the
   intended character or environment layers.
6. Add all animated chain bones to **Unbaked Animated Bones** when using the Bone
   Baking Mesh Combiner.

> **Tuning tip:** If a chain stretches too far, raise `Spring` before raising mass. If
> it jitters, increase `Damper`, keep connected-body collision disabled, and verify
> that collider radii do not overlap in the rest pose.

---

## Quick-reference table

| Animator | Best for | Physics model |
|---|---|---|
| **UMAChainJiggleAnimator** | Ponytails, tails, ropes, skirts | Particle chain + length constraints |
| **SwayBoneAnimator** | Lightweight hair sway (legacy) | Position-delta force + clamp |
| **DynamicBoneAnimator** | DynamicBone users (disabled by default) | DynamicBone (or SwayBone fallback) |
| **MC2BoneAnimator** | Magica Cloth 2 bone cloth | Magica Cloth 2 solver |
| **MC2ClothAnimator** | Magica Cloth 2 mesh cloth | Magica Cloth 2 solver |
| **TwistBoneAnimator** | Forearm/calf twist chains | Driver → ratio inheritance |
| **UnityJointAnimator** | Pendulum chains (real physics) | Rigidbody + CharacterJoint |
| **UnitySpringJointAnimator** | Springy hair, tails, and accessories using Unity physics | Rigidbody + SpringJoint |

---

## Creating your own animator

1. Create a class that extends `BaseUpdatedObject`.
2. Add the `[MenuItem("Assets/Create/UMA/Physics/YourAnimator")]` attribute.
3. Override `Initialize(UMAData, SlotData)` — find bones via
   `umaData.skeleton.GetBoneTransform(name)`, add components, cache state. Set
   `initialized = true`.
4. Override `DoUpdate(UMAData, float step)` — drive your components each physics step.
5. Place the script under `Assets/UMA/Core/Scripts/Physics/BoneAnimations/` so it
   compiles into `UMA_Core`.

See `SwayBoneAnimator.cs` or `UMAChainJiggleAnimator.cs` for complete examples.
