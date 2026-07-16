# Mesh Combiners

UMA provides three mesh-combining strategies that control how slot geometry is assembled into the final `SkinnedMeshRenderer`: Default, Jobified, and Default Bone Baking. Each makes different trade-offs between performance, flexibility, and runtime cost. You can switch between them with **UMA -> Tools -> Mesh Combiner Switcher**.

---

## UMADefaultMeshCombiner

The Default Combiner is the original, fully-managed implementation. It performs all mesh combination on the main thread using standard C# without Unity Jobs, Burst, or unsafe code.

### Highlights

- **Pure managed code.** Every step — building combine instances, merging vertex buffers, applying UV atlasing, and assigning materials — runs in plain C#. There is no dependency on the Burst compiler or the Jobs system, so the combiner works in any Unity configuration without additional package setup.
- **Reliable fallback.** Because it avoids advanced features, the Default Combiner is the most predictable option. If a slot, overlay, or material setup produces unexpected results under another combiner, switching back to Default is a useful diagnostic step.
- **Full feature parity.** It supports legacy cloth components, second-pass materials, UV recalculation, mesh modifiers, mesh hide masks, and blendshape baking.
- **Easier to debug.** Stack traces are straightforward and inline; you can step through the entire combine process with a standard debugger.

### Trade-offs

- **Single-threaded.** The entire mesh build runs on one thread, so characters with many high-vertex slots can cause noticeable generation pauses in the editor and at runtime.
- **Higher per-character cost.** No batching or parallelisation means each character pays the full CPU cost independently.

### Best For

- Projects that do not use the Burst compiler.
- Editor workflows where you need to debug the combine pipeline directly.
- Scenarios with a low number of characters or very simple recipes.

---

## UMAJobifiedMeshCombiner

The Jobified Combiner replaces the legacy combine path with Unity's C# Job System and Burst-compiled kernels. It also uses Unity's modern Mesh API (`MeshData` / `MeshDataArray`) on supported Unity versions for further performance gains.

### Highlights

- **Burst-accelerated.** Vertex skinning, normal/tangent recalculation, and buffer copies are compiled to highly optimised native code via Burst. This reduces per-character build time substantially, especially for dense meshes.
- **Parallel mesh construction.** Slot data is processed in parallel where possible, letting multi-core CPUs combine many slots simultaneously.
- **Unsafe code paths.** The combiner uses `unsafe` pointer access and `NativeArray` for direct memory manipulation, avoiding the overhead of managed array bounds checks.
- **Same feature set as Default.** Despite the different internals, the Jobified Combiner supports cloth, second-pass materials, UV atlasing, mesh modifiers, and mesh hide masks. There is no loss of functionality.

### Trade-offs

- **Requires Burst and the Jobs package.** The combiner will not function correctly if the Burst compiler is missing or disabled.
- **Harder to debug.** Burst-compiled code does not support standard managed debugging. Errors inside Burst kernels can be more difficult to trace.
- **Readable mesh requirement.** The MeshAPI path currently requires readable meshes, which can increase memory usage compared to the Default path.

### Best For

- Projects already using Burst for other systems.
- Scenes with many characters or frequently rebuilt avatars where build time matters.
- Targeting platforms with multiple CPU cores (desktop, console).

---

## UMADefaultBoneBakingMeshCombiner

The Default Bone Baking Combiner takes a fundamentally different approach: instead of preserving every bone for runtime skinning, it bakes unused bone weights directly into vertex positions at build time. Only bones needed for animation are kept. It derives from `UMADefaultMeshCombiner`, so it shares the Default combiner's renderer, material, UV, mesh-modifier, mesh-hide, and blendshape-source pipeline.

### Highlights

- **Bone reduction.** By default, the combiner preserves Mecanim humanoid bones and removes all others from the runtime skeleton. Vertices that were weighted to the removed bones have their positions pre-transformed (baked) so the final mesh is visually identical.
- **Lower runtime skinning cost.** The GPU evaluates fewer bone matrices per vertex, which reduces vertex shader work. This is especially beneficial on mobile platforms and in scenes with many characters.
- **Additional animated bones per slot.** Each `SlotDataAsset` can specify an **Unbaked Animated Bones** list. Bones named in that list are preserved even if they are not part of the standard humanoid set. This lets you keep tail, hair, cape, or weapon bones that need to animate.
- **Custom skeleton management.** The combiner uses `UMAImprovedSkeleton`, a subclass of `UMASkeleton`, which only creates real Unity `Transform` objects for preserved bones. Non-preserved bones exist as cache data only, saving hierarchy overhead.
- **Default-combiner renderer support.** It supports multiple renderer assets and the Default combiner's material handling, rather than forcing all generated geometry into a single renderer.
- **Mesh-only rebuild safety.** The baking pass reads cached post-DNA matrices without copying the rest pose over a live animated hierarchy. A mesh rebuild therefore does not reset the current animation pose.
- **Works with blendshapes and mesh modifiers.** The combiner applies DNA, mesh modifiers, and blendshape baking before the bone-baking pass, so all deformation is captured in the final vertex positions.

### Trade-offs

- **Not compatible with runtime re-parenting or dynamic bone attachments.** Because non-preserved bones have no live Transforms, you cannot attach objects or effects to them at runtime.
- **Requires a rebuild to change animation bones.** If you later decide a previously baked bone should animate, the character must be regenerated.
- **Initial build cost.** The baking process is more expensive than a single-frame Default or Jobified build because it computes the final posed vertex positions. However, this cost is paid once at generation time, not per frame.

### Best For

- Static or semi-static characters (NPCs, background crowds).
- Mobile and lower-end platforms where GPU skinning cost is a bottleneck.
- Characters whose animated bone set is known at authoring time and does not change dynamically.

---

## UMABoneBakingMeshCombiner (Compatibility)

`UMABoneBakingMeshCombiner` is retained for existing prefabs, scenes, and scripts. It now subclasses `UMADefaultBoneBakingMeshCombiner`, so it has the same behavior and feature support. Use `UMADefaultBoneBakingMeshCombiner` for new generator setups; use the compatibility component only when preserving an existing serialized component type matters.

---

## Choosing a Combiner

| Combiner | Runtime Skinning | Build Speed | Best Use Case |
|---|---|---|---|
| **Default** | Full skeleton | Moderate (single-threaded) | Debugging, no Burst setups, simple recipes |
| **Jobified** | Full skeleton | Fast (parallel, Burst) | Many characters, frequent rebuilds, desktop/console |
| **Default Bone Baking** | Reduced skeleton | Slower build, faster runtime | NPCs, mobile, static/semi-static characters |

Switching combiners requires a full character rebuild. Existing generated characters will not reflect the change until they are regenerated.
