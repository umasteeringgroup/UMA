# UMA Generator Setup and Performance Tuning

The UMA Generator is the point where an assembled character recipe becomes renderable geometry and textures. For a Technical Artist, it is also one of the most important places to control character build time, temporary memory, final texture memory, and frame-time spikes.

This guide explains how to set up the generator prefab and tune the current UMA Generator controls for mobile, PC, and console projects. The recommendations are starting points rather than universal limits. Always validate them on representative content and target hardware.

## Overview of the UMA Generator

An UMA character begins as data: a race, a recipe, slots, overlays, colors, DNA, renderer settings, and optional modifiers. The generator consumes that data and produces the objects Unity renders and animates.

At a high level, the generator:

1. Receives a dirty `UMAData` from a `DynamicCharacterAvatar` or another UMA workflow.
2. Validates and prepares the active recipe.
3. Applies DNA, slot rules, mesh hides, and other pre-generation changes.
4. Groups compatible slots and overlays by `UMAMaterial` and renderer.
5. Packs and merges texture channels when generated textures are required.
6. Combines slot meshes into one or more `SkinnedMeshRenderer` meshes.
7. Updates the skeleton, avatar, materials, and generation events.

The generator is therefore both a queue manager and an execution point for texture generation, mesh combination, and avatar updates. A setting that looks texture-specific can affect frame responsiveness because it changes the amount of work and temporary memory required during this sequence.

### How slots, overlays, materials, and texture merging interact

- A `SlotDataAsset` contributes mesh data and identifies its `UMAMaterial`.
- An `OverlayDataAsset` contributes textures, blending instructions, and color data.
- The `UMAMaterial` defines the shader, texture channels, channel formats, mipmap behavior, and whether textures are atlased, generated separately, or reused.
- `TextureMerge` draws each base overlay and additional overlay into the generated texture channels.
- The selected `UMAMeshCombiner` combines the visible slot geometry and applies the atlas UV transforms.
- The generator assigns the generated materials and textures to the resulting renderers.

The generator does not make poor source content inexpensive. Large source textures, excessive overlay stacks, many incompatible materials, dense slot meshes, and frequent full rebuilds all increase the work it must perform.

### Why UMA Settings needs a Generator Prefab

UMA obtains its shared generator through the `UMAAssetIndexer`. If no active `UMAGenerator` is already available, the indexer instantiates the prefab assigned to **Project Settings > UMA > Generator Prefab**.

That prefab is required because it defines the runtime generator configuration, including:

- The `UMAGenerator` component and its queue, atlas, conversion, and memory settings.
- The mesh combiner used to create final renderer meshes.
- The `TextureMerge` asset used to draw overlay stacks.
- Default renderer and overlay assets used when character content does not provide them.

If the Generator Prefab is missing, UMA cannot reliably create its shared generator when an avatar requests one. A generator placed manually in one scene does not replace the need for a valid project-level prefab in builds or scenes where that object is absent.

## Defining the Generator Prefab

The supplied generator is the safest starting point for a project-specific configuration. Duplicate it before making platform-specific changes so UMA package updates do not overwrite your setup.

### Locate or create the prefab

1. Locate the supplied generator prefab. In this project it is `Assets/UMA/UMA3/Getting Started/UMA_GLIB.prefab`.
2. Duplicate the prefab into a project-owned folder, such as `Assets/Project/UMA/Runtime`.
3. Give the duplicate a platform or quality-oriented name, such as `UMA_Generator_Mobile` or `UMA_Generator_Desktop`.
4. Open the duplicate in Prefab Mode.
5. Confirm that the generator and mesh combiner references are valid before changing performance settings.

To build a prefab from an empty GameObject instead, add the required components, assign their references, drag the GameObject into the Project window, and test it in a clean scene before assigning it globally.

### Required components and references

The generator prefab needs:

- **UMAGenerator**: manages the dirty-avatar queue and coordinates texture, mesh, skeleton, and avatar generation.
- **A UMAMeshCombiner component**: normally `UMAJobifiedMeshCombiner`, `UMADefaultMeshCombiner`, or `UMADefaultBoneBakingMeshCombiner`.
- **Mesh Combiner reference**: the `UMAGenerator` component must reference the combiner component on the prefab.
- **Texture Merge reference**: the generator must reference a valid `TextureMerge` asset.

The default renderer and default overlay assets are optional fallbacks, but assigning the intended project assets prevents unexpected behavior when a slot or character does not specify them.

See [MeshCombiners.md](MeshCombiners.md) for the feature and performance differences between the available combiners.

### Assign the prefab in UMA Settings

1. Open **Edit > Project Settings**.
2. Select **UMA**.
3. Find **Generator Prefab**.
4. Assign the project-owned generator prefab.
5. Confirm that **Texture Merger** also references the intended `TextureMerge` asset.
6. Save the project and enter Play mode in a clean test scene.
7. Generate at least one avatar and confirm that the Console contains no missing generator, texture merger, or mesh combiner errors.

### Runtime behavior

UMA first uses an active generator already registered or present in the scene. If none is available, the Asset Indexer creates one from the Generator Prefab. Avatars then add their dirty `UMAData` to that generator's queue.

The generator processes a limited number of queued avatars per frame unless **Process All Pending** is enabled. Texture conversion can also have a separate apply queue. These controls let you trade total completion time against frame responsiveness.

Changing generator values does not retroactively resize textures or rebuild meshes that have already been generated. Regenerate the affected avatars after changing settings that alter atlases, materials, or mesh output.

### Default and custom generator prefabs

The supplied prefab is a tested general-purpose configuration and includes a compatible mesh combiner. A custom prefab uses the same UMA pipeline; it simply changes the component selection, references, or parameter values.

A custom prefab is appropriate when the project needs:

- Different atlas sizes or generation budgets for device tiers.
- A Jobified, Default, or Bone Baking mesh combiner selected for a specific use case.
- Project-specific renderer and fallback overlay assets.
- Different conversion behavior for unified-memory mobile devices and discrete-memory PC or console hardware.
- A controlled prefab that can be tested and versioned independently of UMA sample content.

Do not remove required references merely to make the prefab smaller. A missing texture merger or mesh combiner prevents the corresponding generation stage from completing correctly.

## Understanding Memory Cost

Atlas cost grows with the square of the resolution. For an uncompressed four-byte channel, the base mip level alone is approximately:

| Atlas size | Approximate memory per channel | Four channels, base levels |
|---|---:|---:|
| 512 × 512 | 1 MiB | 4 MiB |
| 1024 × 1024 | 4 MiB | 16 MiB |
| 2048 × 2048 | 16 MiB | 64 MiB |
| 4096 × 4096 | 64 MiB | 256 MiB |

A complete mip chain adds roughly one third more texture memory for common two-dimensional textures. Formats such as `R8`, `RG16`, and platform compression change the final number, but the square-law relationship remains. During generation, source textures, generated RenderTextures, copied Texture2D data, readback buffers, and the previous atlas may overlap temporarily, so peak memory can be considerably higher than the final atlas footprint.

## Generator Parameters

The following sections match the groups in the `UMAGenerator` Inspector. The platform values are practical starting points for a typical customizable humanoid. Dense characters, unusual shader channel counts, or large crowds require separate measurement.

### Atlas Settings

#### Fit Atlas

When enabled, UMA reduces packed texture content when the requested fragments do not fit in the nominal atlas area.

- **Memory:** keeps the generated result within the configured atlas dimensions.
- **Generation speed:** overflow fitting adds packing or rescaling work, but is normally preferable to an invalid layout.
- **Responsiveness:** reduces the risk of a failed or unexpectedly oversized generation, although difficult recipes can take longer to fit.
- **Starting point:** enable on all platforms. Fix consistently overflowing content instead of relying on fitting for every avatar.

#### Sharper Fit Textures

When fitting reduces texture content, this option uses a higher mip level while downsampling to retain more visible detail.

- **Memory:** does not materially change the final atlas allocation.
- **Generation speed:** can add sampling work during a fitted merge.
- **Responsiveness:** usually a small cost compared with the quality benefit.
- **Starting point:** enable for mobile, PC, and console unless profiling identifies the fitting pass as a bottleneck.

#### Atlas Overflow Fit Method

This controls how UMA responds when packed regions do not fit.

- **Decrease Resolution:** repeatedly reduces resolution using **Fit Percentage Decrease**. It is predictable and is a good mobile choice.
- **Best Fit Square:** calculates a square-oriented fit and is a balanced general-purpose choice.
- **Multiple Heuristics:** tries multiple packing approaches and selects a result. It can improve packing efficiency at the cost of more CPU work.

Recommended starting points:

- **Mobile low-end and mid-range:** Decrease Resolution or Best Fit Square.
- **Mobile high-end:** Best Fit Square.
- **PC and console:** Best Fit Square; test Multiple Heuristics for content with irregular atlas regions.

#### Fit Percentage Decrease

This is the scale step used by Decrease Resolution. A value of `0.5` makes an aggressive reduction; a value closer to `0.9` preserves more resolution but can require more fitting attempts.

- **Memory:** the configured atlas allocation remains fixed, but source regions use fewer pixels.
- **Generation speed:** smaller steps can require more attempts.
- **Responsiveness:** `0.5` is usually more predictable during gameplay.
- **Starting point:** `0.5` on mobile, `0.5–0.75` on PC and console after visual testing.

#### Convert Mip Maps

This controls mip creation when a generated RenderTexture is converted to a Texture2D. It is not the only mipmap control in the pipeline; persistent RenderTexture atlases use the `UMAMaterial` **Generate Mip Maps** setting.

- **Memory:** a full mip chain commonly adds about 33 percent to the texture allocation.
- **Generation speed:** creating or regenerating mips adds work.
- **Responsiveness:** mips reduce distant shimmer and improve sampling stability, often avoiding visible quality changes as an avatar moves away.
- **Starting point:** enable when converting on mobile and console; enable on PC unless the shader or camera usage demonstrably does not need mips.

#### Atlas Resolution

This is the nominal square size used for generated atlases. It has the largest direct effect on texture memory.

- **Memory:** doubling the dimension quadruples the base texture memory.
- **Generation speed:** larger atlases require more pixels to clear, merge, copy, and upload.
- **Responsiveness:** oversized atlases increase generation spikes and asynchronous completion time.
- **Mobile low-end:** `512`.
- **Mobile mid-range:** `1024`.
- **Mobile high-end:** `1024`, with `2048` reserved for a small number of hero avatars.
- **PC:** `2048` is a useful starting point; use `4096` only after memory and visual validation.
- **Console:** `2048` is a conservative starting point; use platform budgets and camera distance to justify larger atlases.

### There is no Max Textures per Atlas parameter

UMA does not expose a numeric **Max Textures per Atlas** generator setting. Atlas contents are determined by:

- Compatible `UMAMaterial` and renderer groupings.
- The number and size of slot material fragments.
- Each overlay stack and its rectangles.
- The channel list on the `UMAMaterial`.
- Atlas resolution, scale factor, and fitting behavior.

If an atlas contains too much content, reduce overlay count or resolution, remove unused channels, split content intentionally across different materials or renderers, or select lower-cost recipe variants. Adding more material groups can reduce packing pressure but usually increases renderer materials and draw calls, so measure both sides of the trade-off.

### Conversion Settings

#### Convert Render Texture

UMA normally draws generated texture channels into RenderTextures. When this option is enabled, UMA copies supported generated channels into Texture2D objects.

- **Memory:** conversion can temporarily hold the source RenderTexture, destination Texture2D, and readback data at the same time. This is often the largest generation-time texture spike.
- **Generation speed:** adds copy, possible GPU readback, Texture2D creation, mip generation, and upload work.
- **Responsiveness:** synchronous conversion can stall; asynchronous conversion spreads the work but extends the period of peak memory.
- **Mobile:** normally disable on unified-memory devices unless a later system specifically requires Texture2D data.
- **PC and console:** disable when RenderTextures are acceptable. Enable only for a measured requirement such as saving or CPU-side modification.

A `UMAMaterial` channel can also request conversion. Review channel-level **Convert Render Texture** values when the generator-level option is disabled but copies still occur.

#### Use Async Conversion

When conversion is required, this uses asynchronous GPU readback and queues completed Texture2D applications.

- **Memory:** source and destination resources can coexist longer while the request is pending.
- **Generation speed:** total work remains, but GPU/CPU synchronization is reduced.
- **Responsiveness:** generally smoother than waiting for a blocking copy.
- **Mobile:** enable only when conversion is enabled and asynchronous readback is stable on target devices.
- **PC and console:** enable when conversion is enabled; profile the pending-copy count and peak memory.

This is asynchronous texture conversion, not a switch that makes the entire UMA build asynchronous.

#### Async Mip Regen

This serialized option is exposed by the current Inspector for compatibility with generator configurations. In the current texture-conversion path, mip creation is driven by **Convert Mip Maps** and the source texture's mip state; the conversion code does not branch on **Async Mip Regen**.

- **Memory, speed, and responsiveness:** no independent effect in the current implementation.
- **Starting point:** keep it aligned with **Convert Mip Maps** for configuration clarity, but do not count it as a separate optimization control.

### Generation Settings

#### Max Queued Conversions Per Frame

This limits how many completed asynchronous texture copies UMA applies during one generator update.

- **Memory:** lower values can leave more completed copies queued for longer; higher values drain temporary resources sooner.
- **Generation speed:** higher values finish a conversion backlog faster.
- **Responsiveness:** lower values reduce per-frame apply work; higher values can create an upload spike.
- **Mobile low-end:** `1–2`.
- **Mobile mid/high:** `2–4`.
- **PC and console:** `4–8`, adjusted from frame-time and queue measurements.

#### Initial Scale Factor

UMA divides requested texture dimensions by this value before atlas generation. A value of `1` keeps full requested size; `2` halves each dimension and therefore reduces the pixel area to approximately one quarter.

- **Memory:** higher values substantially reduce generated texture memory.
- **Generation speed:** fewer pixels merge and upload faster.
- **Responsiveness:** higher values reduce texture-generation spikes at the cost of detail.
- **Mobile low-end:** `2–4`.
- **Mobile mid-range:** `2`.
- **Mobile high-end, PC, and console:** `1–2` depending on camera distance and atlas size.

#### Iteration Count

This is the number of queued UMA updates the generator attempts to process per frame.

- **Memory:** more simultaneous completions can increase short-term allocation and cleanup pressure.
- **Generation speed:** higher values empty a crowd-generation queue sooner.
- **Responsiveness:** higher values increase the chance of a long frame because each avatar generation still performs substantial work.
- **Mobile:** `1`.
- **PC:** `1–2`; increase only for loading screens or measured headroom.
- **Console:** `1–2` based on frame budget.

#### Inter Frame Delay

This is the number of complete generator frames UMA waits after processing one queued avatar before it processes the next avatar. A value of `0` preserves the normal **Iteration Count** behavior. Values above `0` limit generation to one avatar on each eligible frame, even when **Iteration Count** is higher or **Process All Pending** is enabled. Queued texture conversions can continue to be applied during the waiting frames.

- **Memory:** longer delays give temporary generation resources and completed conversions more time to settle between avatars.
- **Generation speed:** every added delay frame increases the time required to empty a crowd-generation queue.
- **Responsiveness:** values of `1–3` can smooth repeated atlas and mesh-generation pressure when creating crowds during gameplay.
- **Mobile:** begin with `1–2` for runtime crowd generation and profile on the target device.
- **PC and console:** use `0` when generation fits the frame budget; try `1` when repeated GPU or render-thread pressure is visible.

#### Collect Garbage

This serialized option remains visible for compatibility with existing generator configurations. The current generator work loop does not branch on **Collect Garbage**; player-side scheduled collection is controlled by the mesh-build counter and **Garbage Collection Rate**.

- **Memory, speed, and responsiveness:** changing this checkbox alone has no independent effect in the current implementation.
- **Starting point:** leave it at the prefab default and tune **Garbage Collection Rate**. Treat explicit loading-screen cleanup as a project-level policy.

#### Garbage Collection Rate

This controls how many mesh or rig builds occur before the generator requests a collection. Texture-only and rig-adjustment work do not advance the same counter.

- **Memory:** lower values reclaim managed garbage more often.
- **Generation speed:** frequent collections reduce throughput.
- **Responsiveness:** low values can create repeated hitches; high values allow a larger managed-memory envelope.
- **Mobile:** begin around `32–128` and profile. Avoid `0` for continuous in-game customization.
- **PC and console:** begin around `64–128`, or coordinate collection with loading screens.

#### Process All Pending

When enabled, the generator attempts to process the current dirty queue in the same frame instead of respecting **Iteration Count**. A non-zero **Inter Frame Delay** takes precedence and restricts the generator to one UMA on each eligible frame.

- **Memory:** several avatar builds can overlap cleanup and increase peak allocations.
- **Generation speed:** minimizes the number of frames needed to finish a queue.
- **Responsiveness:** can produce a severe frame spike.
- **Mobile:** disable during gameplay.
- **PC and console:** disable during gameplay; enable only for controlled loading screens or offline generation.

#### Save and Restore Ignored Items

This preserves objects marked through UMA's ignored-item workflow while the skeleton is rebuilt.

- **Memory and speed:** requires extra tracking and restore work.
- **Responsiveness:** normally a small cost, but it scales with the preserved hierarchy.
- **Starting point:** disable unless the avatar actually contains ignored mounted objects or components that must survive skeleton reconstruction.

#### Show In Hierarchy

This controls whether an automatically created generator is hidden in the Unity hierarchy.

- **Memory, speed, and responsiveness:** no meaningful generation benefit.
- **Starting point:** disable in production; enable temporarily for debugging.

### Runtime Tuning Settings

#### Automatic Scaling

At generator startup, UMA compares `SystemInfo.systemMemorySize` and `SystemInfo.graphicsMemorySize` with the two cutoff values. If either reported value is below its cutoff, UMA doubles **Initial Scale Factor** and halves **Atlas Resolution** once. It then disables Automatic Scaling so the adjustment is not repeated.

- **Memory:** provides a coarse one-step reduction before avatars generate.
- **Generation speed:** the reduced atlas and increased scale factor lower texture work.
- **Responsiveness:** helps lower-tier devices avoid large startup builds.
- **Starting point:** useful for broad device families, but replace or supplement it with project-owned quality tiers when hardware reporting is unreliable.

Automatic Scaling uses reported total system and graphics memory. It does not monitor live free memory or react continuously to changing memory pressure.

#### GPU Memory Cutoff and System Memory Cutoff

These values are the startup thresholds used by Automatic Scaling.

- **Mobile example:** start with a `1024` atlas and scale factor `1`; use a system cutoff of `4096 MB` and a graphics cutoff of `1024 MB`. A device below either reported threshold starts at a `512` atlas and scale factor `2`.
- **PC example:** use a graphics cutoff near the lowest supported discrete-GPU tier, then validate integrated GPUs separately.
- **Console:** fixed hardware normally benefits from an explicit tested configuration instead of automatic tier detection.

On unified-memory mobile hardware, reported graphics memory may not describe the budget available to the application. Treat device testing and platform memory warnings as authoritative.

### Edit Time Settings

#### Editor Atlas Resolution

This is the atlas size used for edit-time generation.

- **Memory:** lower values reduce scene-editing and serialization pressure.
- **Generation speed:** lower values make repeated Inspector rebuilds faster.
- **Starting point:** `512–1024` for routine authoring, even when the runtime atlas is larger.

#### Editor Initial Scale Factor

This is the edit-time equivalent of **Initial Scale Factor**.

- **Memory and speed:** higher values reduce editor preview texture cost.
- **Starting point:** `2–4` for routine authoring; temporarily use `1` only for final texture inspection.

These settings affect previews, not the final runtime quality configuration.

### Advanced Settings

#### Apply Inline

When asynchronous conversion completes, this applies the result immediately instead of leaving it for the generator's per-frame conversion queue.

- **Memory:** releases pending source resources sooner.
- **Generation speed:** reduces queue latency.
- **Responsiveness:** can move upload and apply work into an unpredictable callback frame.
- **Starting point:** disable for normal gameplay. Enable only after platform testing shows that immediate application is preferable.

#### Default Renderer Asset

This supplies renderer settings when the character, slot, or renderer manager does not specify another `UMARendererAsset`.

- **Memory and speed:** renderer settings can affect materials, bounds, shadows, and renderer count indirectly.
- **Starting point:** assign a project-approved renderer asset instead of relying on accidental content defaults.

#### Default Overlay Asset

This is a fallback when a mesh slot has no overlay.

- **Memory and speed:** the fallback participates in normal material and texture processing.
- **Starting point:** assign a lightweight diagnostic/default overlay. Do not use it as a substitute for correctly authored recipes.

#### Always Regenerate Renderers

This recreates renderers during updates even when UMA could otherwise retain them. It is needed when attached objects or external renderer state require a fresh renderer.

- **Memory and speed:** increases allocations and setup work.
- **Responsiveness:** can turn partial updates into more expensive rebuilds.
- **Starting point:** disable on all platforms unless the project has a verified renderer-attachment requirement.

#### Use 32 Bit Buffers

This forces 32-bit mesh index buffers. UMA also uses 32-bit indices when the generated vertex count exceeds the 16-bit limit.

- **Memory:** index buffers are larger than 16-bit equivalents.
- **Generation speed:** slightly more data is written and uploaded.
- **Starting point:** disable when all generated meshes remain below 65,536 vertices; enable when large combined meshes require it. Validate worst-case wardrobe combinations, not only the base body.

#### Texture Merge

This references the `TextureMerge` asset containing the materials and shaders UMA uses to draw overlay stacks.

- **Memory and speed:** the reference itself is not a quality tier, but an invalid or incompatible merger prevents texture generation.
- **Starting point:** use the supplied compatible asset unless the project intentionally maintains a tested custom texture-merging implementation.

#### Mesh Combiner

This selects the component that assembles slot geometry.

- **Default:** predictable, managed, and easy to debug; slower for dense or frequent builds.
- **Jobified:** uses Jobs/Burst and is normally the best starting point for frequent builds on modern multicore hardware.
- **Default Bone Baking:** spends more time at generation to reduce the runtime skeleton and skinning cost; useful for crowds and known animation-bone sets.

For mobile crowds, Bone Baking can reduce steady-state GPU and Transform cost. For customizable player characters, Jobified is usually the first configuration to test. Use Default as a compatibility and debugging baseline.

The opt-in **UMA Incremental Mesh Combiner** is intended for avatars whose mesh
generation or blendshape loading causes visible frame spikes. It advances one
staged mesh over multiple generator updates using the generator's **Max
Multi-Step Work (ms)** soft budget. See
[IncrementalMeshCombiner.md](IncrementalMeshCombiner.md) for setup, profiling,
blendshape stress testing, and rollout guidance.

## Texture Merging, Reuse, and Caching

Several important performance choices are authored outside the generator Inspector.

### UMAMaterial merge modes

| UMAMaterial type | Behavior | Practical use |
|---|---|---|
| **Atlas** | Packs compatible material fragments into generated atlases. | Customizable body and clothing that benefit from fewer material regions. |
| **NoAtlas** | Generates textures but does not combine compatible fragments into one shared atlas layout. | Content that needs generated overlay blending but must retain separate texture regions. |
| **UseExistingTextures** | Uses existing source textures instead of generating atlas textures. | Fixed texture sets that do not need per-avatar overlay merging. |
| **UseExistingMaterial** | Assigns the existing Unity material rather than instantiating a generated material. | Truly shared, effectively immutable materials. |

Atlas mode reduces material fragmentation but costs merge time and per-avatar generated textures. Existing texture or material modes can be much cheaper when the content does not require unique compositing.

### Texture reuse

UMA can reuse a previous generated texture result during a compatible rebuild. Persistent RenderTextures are reused when dimensions, format, and mip behavior match; synchronous Texture2D conversion can also reuse a compatible previous Texture2D.

This reuse is automatic rather than controlled by a **Texture Reuse** generator toggle. Avoid changing atlas size, formats, material channels, or mip settings unnecessarily between rebuilds, because incompatible output requires a new allocation.

For fixed content, `UseExistingTextures` avoids generation more directly than hoping a rebuilt atlas can be reused.

### Overlay reference reuse

UMA recipes can merge matching overlay references, and shared colors let several overlays use the same named color data. `OverlayDataAsset.dontMergeDuplicates` prevents duplicate merging when a project explicitly needs separate overlay instances.

There is no generator-level **Overlay Cache** size setting. For efficient content:

- Reuse the same `OverlayDataAsset` where the artwork and blend behavior are identical.
- Use shared colors for skin, hair, and repeated tint groups rather than duplicating equivalent color data.
- Keep **Dont Merge Duplicates** disabled unless separate instances are required.
- Avoid creating visually identical overlay assets for every wardrobe recipe.

Shared colors reduce duplicated recipe state and make color updates consistent. They do not eliminate the need to regenerate an atlas when a visible merged color changes.

### Mesh caching

There is no generator Inspector switch for a persistent cross-avatar mesh cache. UMA combines the active slot meshes for the generated avatar. To reduce repeated mesh work:

- Avoid marking the mesh dirty for color-only or texture-only changes.
- Do not rebuild an unchanged avatar every frame.
- Use baked prefabs for characters that no longer require UMA customization.
- Use LOD slots and mesh hides to reduce geometry before combination.
- Select the Jobified combiner for frequent builds, or Bone Baking where lower steady-state skinning cost is more important.

### Material reuse

Atlas, NoAtlas, and UseExistingTextures workflows normally create per-generated-material instances so UMA can assign textures and parameters safely. `UseExistingMaterial` shares the referenced Unity material directly.

There is no general **Material Cache** generator toggle. Use `UseExistingMaterial` only when avatars truly can share the same material state; per-avatar changes to a shared material can affect every renderer using it.

## Runtime Tuning Based on Memory

### Where memory is allocated

An avatar build can involve several overlapping categories:

- Source slot mesh arrays and source overlay textures.
- Recipe, overlay, and color working data.
- Atlas packing structures and texture-merge draw data.
- Temporary or persistent RenderTextures for every generated texture channel.
- Optional Texture2D destinations and asynchronous GPU readback buffers.
- Combined vertex, index, bone-weight, blendshape, and material data.
- The previous generated mesh or textures until replacement and cleanup complete.

Peak memory is therefore more important than final renderer memory. Profile the moment several avatars generate together, not only the settled scene.

### Use Automatic Scaling for a coarse startup tier

A practical low-memory policy is:

1. Start from atlas resolution `1024` and Initial Scale Factor `1`.
2. Enable **Automatic Scaling**.
3. Set **System Memory Cutoff** to `4096 MB`.
4. Set **GPU Memory Cutoff** to `1024 MB`.
5. On devices reported below either threshold, UMA starts with atlas resolution `512` and scale factor `2`.

This is a one-time startup adjustment, not live memory-pressure management.

### Use UMAGeneratorOverride for scene or quality-tier tuning

Add `UMAGeneratorOverride` to a GameObject that loads before the avatars it should affect. The component contains the same editable groups as the generator Inspector.

When enabled, it:

1. Gets the current UMA Generator.
2. Saves the generator's current parameter values.
3. Applies the override values.
4. Restores the saved values when the override GameObject is disabled or destroyed, provided the generator still exists.

Null object references on the override are ignored. Leaving **Default Renderer Asset**, **Default Overlay Asset**, **Texture Merge**, or **Mesh Combiner** unassigned preserves the generator's current reference rather than replacing it with null.

This is useful for a character creator, crowd scene, photo mode, or low-memory gameplay area that needs a different generation budget. Regenerate avatars if the changed settings must affect content that was already built.

### Detecting live memory pressure

UMA does not expose a live free-memory monitor. Use project-level platform signals and profiling:

- Unity Memory Profiler captures for representative devices.
- `ProfilerRecorder` or project telemetry for allocated memory and frame timing.
- `Application.lowMemory` as an urgent mobile warning.
- Platform-specific memory budget APIs where available.
- `SystemInfo.systemMemorySize` and `SystemInfo.graphicsMemorySize` for coarse startup classification, not precise live availability.

When pressure is detected, avoid immediately rebuilding every visible avatar. Choose a controlled response:

- Stop accepting new customization requests temporarily.
- Lower the settings used for future avatars.
- Switch newly generated NPCs to lower-resolution recipe or overlay variants.
- Regenerate off-screen avatars in small batches.
- Release unused avatars and Addressable content before requesting a collection.

### Practical memory policies

- **Below a 4096 MB system-memory tier:** use a `512` atlas, Initial Scale Factor `2`, one avatar iteration per frame, and no RenderTexture-to-Texture2D conversion unless required.
- **Memory warning received:** pause generation, unload unused content, then resume with a low-memory `UMAGeneratorOverride` profile for future builds.
- **Overlay-heavy NPCs:** switch to recipes that reuse shared overlays and colors, remove optional decals, and use lower-resolution overlay assets.
- **Large crowds:** spawn and generate in batches instead of activating the entire population in one frame.
- **Character creator:** allow higher quality while only one avatar is present, then regenerate at gameplay quality before leaving the scene if the runtime budget is lower.

Switching to shared overlays is an authoring or recipe decision, not a generator cache switch. Prepare constrained variants before runtime so the application can select them without reconstructing content definitions under pressure.

### Avoiding mobile GPU spikes

- Keep **Process All Pending** disabled.
- Keep **Iteration Count** at `1` during gameplay.
- Add an **Inter Frame Delay** of `1–2` when crowd generation still creates repeated render-thread or GPU spikes.
- Avoid copying RenderTextures on unified-memory devices.
- If copying is mandatory, use asynchronous conversion and a low **Max Queued Conversions Per Frame**.
- Do not generate several high-resolution, multi-channel atlases in one frame.
- Prewarm or generate important avatars during loading and camera transitions.
- Keep shader channel formats as compact as the visual result permits.
- Reduce optional normal, detail, mask, or decal channels for distant NPC tiers.

## Ramifications of Copying Textures

UMA copies a generated RenderTexture to a Texture2D when **Convert Render Texture** is enabled globally or when an individual `UMAMaterial` channel requests conversion.

### Why copies cost memory

During conversion, UMA may temporarily retain:

- The source RenderTexture used for merging.
- The asynchronous readback or copy data.
- The destination Texture2D and its mip chain.
- A previous generated texture until the replacement is installed.

At 2048 resolution with several channels, this overlap can consume tens or hundreds of MiB. Asynchronous conversion improves synchronization behavior but does not make the memory free; it can extend the time during which both source and destination exist.

### GPU upload and frame-time cost

- A direct GPU copy still moves substantial texture data.
- A readback path transfers data from GPU-visible storage to CPU-visible memory before applying it to a Texture2D.
- Applying the Texture2D uploads or finalizes texture data and can generate mipmaps.
- Applying many completed conversions in one frame can create an upload spike, which is why **Max Queued Conversions Per Frame** matters.

### Garbage collection impact

Texture objects are Unity engine objects, but the surrounding arrays, requests, queue entries, and generation working data add managed allocations. Frequent regeneration also makes more old data eligible for cleanup. Forced managed collection does not replace correct destruction and release of Unity texture resources.

### Minimize texture duplication

- Leave **Convert Render Texture** disabled unless a later workflow needs Texture2D data.
- Review per-channel conversion flags in every production `UMAMaterial`.
- Use `UseExistingTextures` for fixed texture sets that do not require compositing.
- Use `UseExistingMaterial` only for truly shared material state.
- Reuse `OverlayDataAsset` references instead of duplicating equivalent overlay assets.
- Use shared colors for repeated tint groups.
- Avoid rebuilding textures for changes that affect only DNA or mesh state.
- Keep atlas dimensions and channel formats stable so compatible previous results can be reused.

## Mipmap Creation and Performance

UMA has two relevant mipmap controls:

- **UMAMaterial > Generate Mip Maps** controls mip usage for generated RenderTexture atlases that remain RenderTextures.
- **UMAGenerator > Convert Mip Maps** controls mip creation for generated textures converted to Texture2D.

The active path determines which setting matters. Check both when comparing editor, mobile, and desktop results.

### Benefits and costs

- **GPU memory:** a complete mip chain commonly adds about one third to the base allocation.
- **Generation:** mip creation adds processing and can extend texture conversion time.
- **Sampling quality:** mips reduce shimmer, crawling detail, and unstable downsampling at distance.
- **Runtime bandwidth:** appropriate mip selection can reduce texture sampling bandwidth because distant pixels use smaller levels.

### Platform recommendations

- **Mobile:** usually enable mipmaps for world-space avatars. Stable downsampling and lower sampling bandwidth are normally worth the memory. Consider disabling them only for UI portraits or textures always displayed near 1:1 resolution.
- **PC:** enable for normal third-person, first-person, and crowd use. Disabling can be reasonable for a fixed-resolution portrait renderer or a shader path that does not sample the generated texture conventionally.
- **Console:** enable for consistent LOD and camera-distance transitions. Validate mip bias and anisotropic filtering with the target renderer pipeline.

Do not disable mipmaps simply to reduce generation time without checking motion. Shimmering clothing and skin detail is often more objectionable than a small generation improvement.

## Best Practices for Technical Artists

### Profile the complete build

- Use the Unity Profiler Timeline to identify validation, texture processing, mesh updates, skeleton updates, and event cost.
- Expand the generator's **Statistics** foldout during Play mode for UMA's generation timing counters.
- Capture Memory Profiler snapshots before generation, at peak queue activity, and after cleanup.
- Test the worst valid wardrobe combination, not only the base race.
- Profile on device; editor timing and desktop memory behavior do not represent mobile hardware.

### Reduce avatar generation time

- Use the Jobified combiner when Burst and the Jobs system are available and validated.
- Keep **Iteration Count** low during gameplay and generate crowds over several frames.
- Avoid **Process All Pending** outside loading screens.
- Do not request a texture rebuild for a DNA-only change unless texture data actually changed.
- Remove unused material channels and unnecessary overlays.
- Use lower atlas resolution or a higher Initial Scale Factor for background characters.

### Minimize texture memory

- Begin with the smallest atlas that survives the closest expected camera view.
- Budget every generated channel, not only the diffuse channel.
- Use compact channel formats where the shader and quality target permit.
- Keep optional decals, normals, and detail maps out of low-tier recipes.
- Avoid conversion to Texture2D on unified-memory devices.
- Use mipmaps deliberately and include their cost in the budget.

### Author slots and overlays for fast merging

- Keep overlay rectangles and source texture dimensions appropriate to their visible importance.
- Reuse compatible `UMAMaterial` assets so content can share atlas groups.
- Reuse matching overlay assets and shared colors.
- Avoid long stacks of nearly transparent overlays when the result can be baked into a single source asset.
- Use `UseExistingTextures` or `UseExistingMaterial` for content that does not need unique compositing.
- Use mesh hides or appropriate geometry variants so invisible body triangles are not carried into the final mesh.

### Prepare content by platform

- **Mobile:** author lower-resolution overlays, reduced channel sets, lighter LOD meshes, and constrained wardrobe combinations.
- **PC:** retain higher-detail source assets but still cap worst-case atlas and channel counts.
- **Console:** build one explicit configuration per hardware target and validate memory during rapid character swaps and streaming.
- Keep platform variants visually comparable so quality scaling does not change identity-critical colors or silhouettes unexpectedly.

### Common pitfalls

- Selecting a 4096 atlas because the source art is 4096, without measuring the final on-screen pixel density.
- Treating asynchronous conversion as a way to reduce memory rather than a way to reduce synchronization stalls.
- Enabling **Process All Pending** for a crowd during active gameplay.
- Copying every generated RenderTexture to Texture2D without a consumer that requires it.
- Duplicating overlays and materials that could be shared.
- Forcing 32-bit buffers when no generated mesh approaches the 16-bit vertex limit.
- Rebuilding textures when only color-independent DNA or animation state changed.
- Assuming total device memory is the same as memory currently available to the application.

## Example Configurations

These profiles assume world-space avatars with ordinary skin, clothing, normal, and mask channels. Adjust them for camera distance, renderer pipeline, art direction, and concurrent avatar count.

### Mobile Low-End

| Area | Starting configuration |
|---|---|
| Atlas size | `512` |
| Initial Scale Factor | `2`, increase to `4` for distant crowds |
| Merge mode | Atlas for customizable characters; UseExistingTextures for fixed NPC materials |
| Mipmaps | Enabled on the active RenderTexture or conversion path |
| Overlay reuse | Matching overlay assets and shared colors; omit optional decals/detail overlays |
| Mesh combining | Default Bone Baking for constrained NPCs; Jobified for customizable characters if Burst is validated |
| Generation queue | Iteration Count `1`, Process All Pending disabled |
| Async generation | Keep queued generation at one avatar per frame; leave texture conversion disabled unless required |
| Conversion apply budget | `1–2` if conversion is unavoidable |
| Garbage collection | Rate `64–128`, with loading-screen cleanup preferred; Collect Garbage is compatibility-only in the current work loop |

### Mobile High-End

| Area | Starting configuration |
|---|---|
| Atlas size | `1024`; reserve `2048` for one hero avatar after measurement |
| Initial Scale Factor | `1–2` |
| Merge mode | Atlas for customizable body and wardrobe; existing textures for fixed accessories |
| Mipmaps | Enabled |
| Overlay reuse | Shared assets and colors; moderate decal stack |
| Mesh combining | Jobified; Bone Baking for large NPC populations |
| Generation queue | Iteration Count `1`, Process All Pending disabled |
| Async generation | Async texture conversion enabled only when copying is required |
| Conversion apply budget | `2–4` |
| Garbage collection | Rate `64–128`; schedule major cleanup outside interaction |

### PC

| Area | Starting configuration |
|---|---|
| Atlas size | `2048`; justify `4096` with close-up quality and memory captures |
| Initial Scale Factor | `1` |
| Merge mode | Atlas for unique characters; existing textures/materials for fixed shared content |
| Mipmaps | Enabled for world-space avatars; optional for fixed portraits after testing |
| Overlay reuse | Shared source assets and colors; profile worst-case customization layers |
| Mesh combining | Jobified |
| Generation queue | Iteration Count `1–2`, Process All Pending only during loading |
| Async generation | Async conversion enabled if Texture2D output is required; otherwise retain RenderTextures |
| Conversion apply budget | `4–8` |
| Garbage collection | Rate `64–128` or explicit loading-screen cleanup |

### Console

| Area | Starting configuration |
|---|---|
| Atlas size | `2048`, adjusted to the fixed platform memory budget |
| Initial Scale Factor | `1` for hero characters, `2` for crowds |
| Merge mode | Atlas for customizable characters; existing resources for fixed uniforms or NPC sets |
| Mipmaps | Enabled for consistent camera and LOD transitions |
| Overlay reuse | Shared overlay assets and colors; controlled maximum customization stack |
| Mesh combining | Jobified; evaluate Bone Baking for crowds |
| Generation queue | Iteration Count `1–2`, Process All Pending disabled during gameplay |
| Async generation | Async conversion when copying is required and validated on the target graphics API |
| Conversion apply budget | `4–8`, tuned to frame-time headroom |
| Garbage collection | Coordinate with transitions, streaming pauses, or other known safe points |

## Final Summary

The UMA Generator determines how quickly recipe data becomes a rendered character and how much temporary and final memory that process consumes. Atlas resolution, texture channel count, conversion behavior, queue budgets, overlay complexity, and mesh-combiner choice all contribute to the result.

For most projects, the strongest improvements come from a few disciplined choices:

- Assign and validate a project-owned Generator Prefab in UMA Settings.
- Use the smallest atlas and channel set that meets the visual target.
- Keep gameplay generation queued and bounded rather than processing every avatar in one frame.
- Avoid RenderTexture copies unless another workflow needs Texture2D data.
- Reuse overlays, shared colors, materials, and fixed textures where the content permits.
- Select the mesh combiner according to build frequency and steady-state runtime cost.
- Profile peak generation memory and frame time on each target platform.

Correct generator configuration is essential on modern hardware because UMA generation crosses CPU work, GPU work, managed allocations, Unity engine objects, and content streaming. A measured platform profile gives artists predictable visual quality while keeping customization responsive and within memory budget.
