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

Stub for a spring-joint-based animator. Currently only resolves the anchor bone.

| Field | Description |
|---|---|
| `AnchorBoneName` | Root anchor bone. |

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
| **UnitySpringJointAnimator** | Stub / placeholder | — |

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
