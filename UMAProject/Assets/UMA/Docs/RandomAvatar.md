# UMA Random Avatar Generation

UMA provides random-avatar components for generating characters, crowds, and randomized wardrobe. The current UMA 3 starter content includes both the original `UMARandomAvatar` and the more flexible `UMARandomAvatarV2`.

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

## Original UMARandomAvatar

`UMARandomAvatar` generates characters from one or more randomizers.

Important controls include:

- `Randomizers`
- Character prefab
- Parent object
- Placeholder display
- Grid generation and dimensions
- Grid spacing and random offset
- Random rotation
- Generated-name prefix
- Generated-character event

Use it for straightforward random crowd generation and existing projects built around its API.

## UMARandomAvatarV2

V2 separates character randomization from wardrobe randomization.

It provides:

- Character Randomizers
- Wardrobe Randomizers
- `Keep Existing Race`
- `Keep Existing Wardrobe`
- `Generate` or `Use Existing` mode
- Grid and sequential-generation controls
- Randomize all, character only, or wardrobe only

Use `Use Existing` when the scene already contains DCAs and only their appearance should be randomized.

Use `Generate` when the component should instantiate the character prefab.

## Build a Randomizer

1. Create or select a `UMARandomizer`.
2. Add the races it may choose.
3. Add compatible wardrobe recipes or collections for each race.
4. Configure shared colors.
5. Configure DNA ranges.
6. Adjust chance values.
7. Save and test many results.

An item with a higher chance value is selected more often relative to the total chance values in its choice group.

Do not assume random selection produces a good outfit. Use wardrobe regions, incompatible recipes, suppression, and curated choice sets to prevent combinations that conflict visually.

## Character and Wardrobe Randomization

V2 can randomize:

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
