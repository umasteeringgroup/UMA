# UMA Simple LOD

`UMASimpleLOD` adjusts avatar texture and slot detail by camera distance. It can resize generated textures, switch slot LODs, drop slots at their configured maximum LOD, or use internal per-slot triangle ranges.

## Add LOD to a Character

Add `UMASimpleLOD` to a GameObject containing a `DynamicCharacterAvatar`, or start with:

`Assets/UMA/UMA3/Getting Started/UMADynamicCharacterAvatar-LOD.prefab`

Test LOD only after the character generates correctly at full quality.

## Distance Controls

- `LOD Distance`: distance of the first LOD transition.
- `Distance Multiplier`: multiplies the threshold for each later level.
- `LOD Offset`: shifts the selected slot LOD.
- `Max LOD`: highest LOD level searched.

With distance `5` and multiplier `2`, thresholds progress approximately through `5`, `10`, `20`, `40`, and `80`.

## Texture LOD

Enable `Use Texture Resize` to reduce generated texture scale at distance.

`Max Reduction` limits how many halving steps are allowed. A reduction of three levels uses approximately one-eighth of the original width and height at the far level.

Texture resizing requires a character texture rebuild. Avoid very frequent checks across a large crowd.

## Slot LOD

Enable `Swap Slots` to use slot LODs.

UMA supports:

- Internal LOD ranges stored in a `SlotDataAsset`
- LOD slot lookup where compatible LOD assets are available
- Slot dropping based on the SlotDataAsset maximum LOD

Enable `Use Internal Mesh LOD` when slots contain generated internal LOD triangle ranges. This path changes triangle ranges without the older full slot-replacement workflow and is normally faster.

Create internal ranges in Slot Builder with `Generate Slot LODs`, or use the Global Library slot LOD tools.

## Slot Dropping

Enable `Use Slot Dropping` to remove slots after their configured `maxLOD`.

Use slot dropping for details that can disappear completely:

- Small jewelry
- Hidden underlayers
- Fine accessories
- Distant facial details

Do not drop a slot when its disappearance opens the body or changes the silhouette unexpectedly.

## Hysteresis

LOD thresholds can flicker when a character moves back and forth across the boundary.

Use:

- `Use Percentage Buffer` and `Buffer Percent` for a threshold-relative buffer.
- `Buffer Zone` for a fixed world-space buffer.

A percentage buffer is easier to scale across several distance levels.

## Update Frequency

- `Min Check`: minimum time between distance checks.
- `Check Range`: random additional delay.

The random range spreads crowd updates so every avatar does not rebuild on the same frame.

Increase these values for large crowds or slow-moving characters. Reduce them only when fast camera movement makes delayed transitions obvious.

## Manual Processing

Enable `Disable Automated Processing` when another system controls LOD decisions.

Call the manual LOD check from a camera manager, visibility system, or crowd scheduler. This is useful when the project already budgets character work centrally.

## Editor Preview

Enable `Editor Override LOD` and set `Editor Forced LOD` to inspect a specific level without moving the camera.

Check:

- Silhouette
- Open seams
- Missing coverage
- Hair and accessory disappearance
- Texture readability
- Material changes

Disable the override before runtime testing.

## Authoring Slot LODs

For each slot:

1. Preserve the overall silhouette at early LODs.
2. Preserve open boundary edges to reduce seams.
3. Test combined body sections, not isolated slots.
4. Keep UVs and materials compatible.
5. Configure the slot's maximum LOD intentionally.
6. Inspect the `LOD` foldout in the SlotDataAsset inspector.

The Slot Builder can use Unity's LOD generator or UMA's custom path, depending on the selected options.

## Crowd Guidance

- Stagger checks with `Check Range`.
- Prefer internal mesh LOD when available.
- Avoid texture rebuilding every frame.
- Use slot dropping for small details.
- Keep the nearest characters at full quality.
- Profile generated texture memory as well as triangle count.
- Combine LOD with generator queue and inter-frame controls.

## Troubleshooting

### LOD never changes

Confirm the component is enabled, automated processing is not disabled, the camera can be resolved, and the character has completed its first generation.

### Slots do not swap

Enable `Swap Slots` and verify that the slot contains internal LOD ranges or compatible LOD assets.

### A body hole appears

The slot was dropped too early, the LOD lost a boundary, or matching body slots transition inconsistently.

### The crowd rebuilds at once

Increase `Min Check` and `Check Range`, and ensure characters did not all receive identical update scheduling.

### Texture quality drops too aggressively

Increase LOD distance, reduce the distance multiplier effect, lower `Max Reduction`, or disable texture resizing for hero characters.

## Related Guides

- [SlotDataAsset.md](SlotDataAsset.md)
- [ContentCreation.md](ContentCreation.md)
- [UMAGeneratorSetup.md](UMAGeneratorSetup.md)
- [MeshCombiners.md](MeshCombiners.md)
