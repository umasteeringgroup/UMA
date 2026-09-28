# UMA Random Avatar Generation

UMA provides random-avatar components for generating characters, crowds, and randomized wardrobe. Use `UMARandomAvatar` for new setups. It combines separate character and wardrobe randomization, existing-avatar targets, pooled appearances, completed NPC builds, and spawning budgets. `UMARandomAvatarV2` remains available for compatibility with existing scenes.

## Starter Prefabs

Use the examples in:

`Assets/UMA/UMA3/Getting Started`

- `UMARandomGeneratedCharacter.prefab`
- `UMARandomGeneratedCharacterV2.prefab`

The scene `U3-Generating Random Characters` demonstrates the workflow.

## UMARandomizer

A `UMARandomizer` is the reusable asset that describes possible character results.

It can contain:

- Supported races
- Race-specific wardrobe choices
- Wardrobe collections
- Shared-color ranges
- DNA ranges
- Probability or chance values
- Definition metadata

Create focused randomizers instead of one enormous list. For example:

- Civilian adults
- Guards
- Shopkeepers
- Background crowd

This makes art direction and probability tuning easier.

## UMARandomAvatar

`UMARandomAvatar` generates characters from one or more randomizers.

Important controls include:

- `Randomizers`
- Character prefab
- Parent object
- Placeholder display
- Grid generation and dimensions
- Maximum Unique Characters (default `0`, unlimited)
- Grid spacing and random offset
- Random rotation
- Generated-name prefix
- Generated-character event

Choose **Targets**:

- **Generate** instantiates the configured prefab, as a single character or a grid.
- **Use Existing** targets the **Scene Avatars** list and never instantiates or owns those objects.

**Run on Start** is enabled by default. Existing avatars that have already built are randomized immediately; others are randomized once when their first build finishes. Disable Run on Start for manual control.

**Character Randomizers** retains the original serialized `Randomizers` field and its race, DNA, color, and slot settings. The optional **Wardrobe Randomizers** list applies a second, race-compatible clothing pass. Leave it empty to retain the original single-list setup. For a wardrobe-only reroll with no separate wardrobe list, a matching definition from Character Randomizers supplies slots and colors without changing race or DNA.

In Play mode, use **Reroll Character**, **Reroll Wardrobe**, or **Reroll All**. Character rerolls still apply character slots such as hair; configure the character and wardrobe lists with the regions each should control. Partial rerolls replace only selected regions, preserving unrelated items. A full reroll clears existing regular items, additive items, and collections before applying the new setup unless **Keep Unselected Wardrobe** is enabled. A selected empty/None item explicitly removes that region.

**Keep Existing Race** restricts selections to the current race. **Keep Unselected Wardrobe** preserves items outside selected regions; it does not prevent selected regions from changing. These preservation options bypass complete-setup pooling to avoid replacing one avatar's preserved state with another's. Existing-avatar and partial rerolls also bypass the pool.

**Generate Characters** adds another generated batch; **Destroy Generated Characters** removes only objects owned by this controller. **Spawn Budget** spreads instantiation and setup across frames. Zero is synchronous. A full crowd reroll clears and repopulates its appearance pool; partial rerolls clear stale pooled appearances.

For scripts, `Randomize(avatar)` retains its original data-only, pool-aware behavior. `Randomize(avatar, randChar, randWardrobe)` edits selected data without building or using the pool. `RandomizeAndBuild(...)` applies and builds, and `RandomizeAll(...)` rerolls the current target list. The three parameterless button methods are suitable for UnityEvents.

### Limit the crowd's unique setups

Set **Maximum Unique Characters** on `UMARandomAvatar` to, for example, `8` for an
81-character crowd. The first eight characters are randomized normally. Each remaining
character randomly chooses one of those saved setups: the same race, DNA, wardrobe,
additive recipes and colors, but its own position, skeleton, Animator and live state.
`0` (or a negative value supplied by code) preserves independent randomization for every
character and stores no pool. Random choices can naturally coincide; the option caps
the number of saved setups, not guarantees that all of them look different.

The pool stores setup data, not avatars, generated meshes or textures. Enable **Cache and
Reuse (NPCs)** on the character prefab, or use the resource monitor's **Restart crowd:
reuse ON**, to enable actual resource sharing. The limit itself does not force reuse on
or bypass safety checks such as per-avatar atlas callbacks. LOD, atlas layout and other
build inputs must still match.

Individual `GenerateRandomCharacter` calls use the same pool. Destroying the generated
crowd clears it. `GenerateCharacters(true)` recreates it from the initial random sequence,
so OFF/ON comparisons use the same setups and selections. After editing the randomizer
at runtime, use **Clear Character Setup Pool** in the component's context menu (or call
`ClearCharacterSetupPool()`) to roll new setups for future characters. Existing characters
are unchanged. Reducing the maximum keeps only the first N pool entries for future
selection; increasing it lets future spawns add entries up to the new maximum.

### Different hair and skin tints on matching setups

Shader-only color parameters can differ while meshes and atlas textures remain shared.
UMA's material reuse already permits different material instances to reference the same
atlas. Keep compositor colors, channel masks and texture inputs identical; vary only the
surface shader's tint properties. Do not change a shared material directly: use a
renderer property block, or `UMAResourceLeaseOwner.MakeMaterialsUnique(renderer)` before
editing material properties. Use the shader's actual property names and update every
relevant pass/material. Renderer property blocks need to be reapplied after a rebuild
replaces the renderer. Unity's [MaterialPropertyBlock documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MaterialPropertyBlock.html)
notes that property blocks are incompatible with the SRP Batcher, so profile that approach
against separate tint materials that share the same textures.

An ordinary randomizer shared-color entry may contain **both** atlas tint/masks and shader
parameters. Rerolling the whole entry is not automatically texture-safe. The current setup
pool deliberately preserves all colors; independent shader-only color variation is a
separate customization, not another unique base setup.

## Migrating UMARandomAvatarV2

V2 uses the same randomization implementation and weighted selection fixes as the unified controller. Its component type and serialized fields remain intact so existing scenes and references continue to load.

In Edit mode, select a V2 component and click **Copy to unified UMARandomAvatar and disable V2**. This undoable action copies randomizers, targets, preservation flags, prefab/grid settings, and the generated-character event. It leaves V2 disabled with a reference to the unified controller, so calls to its generation, destruction, reroll, and wardrobe-toggle methods forward to the replacement. Do not add another unified controller first: migration is disabled if one already exists on the object.

The old **Sequential** option only changed setup ordering and did not spread generation across frames. The unified controller sets up each spawned character in turn; use **Spawn Budget** for actual frame budgeting. Migration leaves the budget at zero and pooling disabled until configured. Deterministic results from the old biased weighting algorithm are not preserved.

## Build a Randomizer

1. Create or select a `UMARandomizer`.
2. Add the races it may choose.
3. Add compatible wardrobe recipes or collections for each race.
4. Configure shared colors.
5. Configure DNA ranges.
6. Adjust chance values.
7. Save and test many results.

Race definitions and wardrobe items use one cumulative weighted roll per selection. Only positive weights participate. Null entries and empty color tables are skipped; an unmatched race or an entirely zero-weight group leaves that part unchanged. Global colors apply only when enabled and are overridden by race-specific and then selected-slot colors.

Do not assume random selection produces a good outfit. Use wardrobe regions, incompatible recipes, suppression, and curated choice sets to prevent combinations that conflict visually.

## Character and Wardrobe Randomization

The unified controller can randomize:

- Both character and wardrobe
- Character properties while keeping wardrobe
- Wardrobe while keeping the current race and body

This is useful for a character creator where the player can reroll clothing without losing body settings.

## Grid and Crowd Generation

Grid generation creates several characters around the component.

Before generating a large crowd:

- Use a modest atlas size.
- Select an appropriate mesh combiner.
- Limit generator conversions per frame.
- Use generator inter-frame delay when GPU work needs spacing.
- Add `UMASimpleLOD` when the crowd needs distance scaling.
- Stagger LOD checks.
- Profile a player build, not only the editor.

Random generation schedules DCA builds; it does not guarantee that every completed avatar becomes visible on a different frame. Completion cadence depends on the generator, combiner, and publication steps.

## Walking Crowds: Speed and Idle

`UMADynamicCharacterAvatar-LOD-walker.prefab` adds `RandomCharacterWalker` for bounded wandering,
pauses, crowd avoidance, and shooter/ragdoll reactions. It is a sample movement controller,
not a navigation or pathfinding system.

The three speed controls have different jobs:

- **Animation Playback Speed** controls the rate of the animation and its root translation.
  `1` is normal speed. The sample prefab now uses `1` instead of the old `0.3` slow-motion setting.
  Existing scene overrides and custom prefabs retain their chosen values; set them to `1` if needed.
- **In Place Movement Speed** is travel in meters per second at normal playback, used for clips
  without authored translation. It scales with playback speed and eases in with the animation's
  damped Speed parameter.
- **Walking Animation Speed** is the value sent to the Animator's **Speed** parameter to select
  a state/blend. It does not set playback speed or meters per second. A controller with only
  Idle and Run states will still select Run; provide a walking clip/state if walking is desired.

**Locomotion Source** can be Auto, Root Motion, or In Place. Auto distinguishes root translation
from tiny animation drift at **normal playback speed**, so slowing playback does not itself
switch to fallback movement. Choose Root Motion for deliberately slow or variable root-motion
clips; this mode never substitutes fallback travel. Choose In Place to always use the configured
travel speed. Root Motion requires an appropriately imported clip and Animator setup.

When a walker wants to move but is blocked, it measures requested versus achieved travel.
Dynamic rigidbodies are sampled on the physics clock; transform-driven walkers measure accepted
root translation. Crowd separation, vertical movement, and render interpolation do not count
as forward walking progress. A legitimate slow walk is not forced into idle by a fixed speed cutoff.

Under **Stall Recovery**, Progress Check Interval and Stalled Checks Before Recovery determine
how long sustained blockage must last before idling (defaults: two 1-second checks after startup
grace). Blocked/Resumed Progress Ratio provide separate thresholds to avoid flicker; Minimum
Progress Distance caps the blocked threshold while preserving the gap to the resumed threshold.
Blocked Retry Interval controls the idle wait before
an explicit attempt to move again. Wanderers turn for that retry; pursuers keep facing their target.
Movement Start Grace Time allows animation startup before progress is judged. An Animator Speed
float and a transition to idle when it reaches zero are required to display the idle animation.

Pausing, disabling/re-enabling, changing Animator controllers, and recovering from ragdoll reset
old progress samples. Call `ResetMovementTracking()` after externally teleporting an enabled
walker; it resets blockage/physics tracking but deliberately keeps the original wander origin.

## Reproducible Testing

For performance comparisons:

1. Use the same randomizer assets.
2. Use repeatable initial random sequencing where available.
3. Reset generator timing.
4. Destroy or reset previous generated characters.
5. Restart crowd generation.
6. Capture statistics after one-time initialization has already occurred.

This separates content variance and shader initialization from actual steady-state generation cost.

## Art-Direction Tips

- Keep color ranges believable for each material.
- Use curated hair and brow combinations.
- Separate formal, casual, armor, and fantasy collections.
- Restrict race-specific items to compatible races.
- Use wardrobe collections for coordinated outfits.
- Avoid random DNA extremes that cause clothing failure.
- Include occasional choices by lowering their relative chance, not by duplicating assets.

## Troubleshooting

### Nothing generates

Verify the character prefab, randomizer list, Generator Prefab in UMA Settings, and Global Library index.

### Characters are missing clothing

The recipes may not support the chosen race, may not be indexed, or may be suppressed by another item.

### Every character looks similar

Increase meaningful choices in the randomizer and inspect chance values. Adding many duplicate variants does not create useful visual diversity.

### Characters appear all at once

The generator and combiner may process work incrementally while renderers are published near the same time. Review queue settings, multi-step budget, and generator statistics.

### The crowd causes frame spikes

Reduce per-frame queue limits, use the incremental combiner, space work with inter-frame delay, reduce atlas size, and verify that shader initialization is not being measured as steady-state generation.

## Related Guides

- [GettingStarted.md](GettingStarted.md)
- [DynamicCharacterAvatar.md](DynamicCharacterAvatar.md)
- [UMAGeneratorSetup.md](UMAGeneratorSetup.md)
- [UMASimpleLOD.md](UMASimpleLOD.md)
- [MeshCombiners.md](MeshCombiners.md)
