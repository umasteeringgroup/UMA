# Animation Utilities

UMA provides small editor commands for stripping transform curves and renaming animation binding paths. They create or modify `AnimationClip` assets, so keep source clips under version control.

## Create Clean Animation

Select one or more `AnimationClip` assets and choose `UMA > Animation > Create Clean Animation`.

For each selected clip, UMA creates a uniquely named copy in the same folder and removes all local position and local scale curves. Other editor curves, including rotation and non-transform properties, are retained.

Use this for animations where root/bone translation and scale must not override UMA's generated skeleton proportions. It is more aggressive than the non-scale command.

## Create Non-Scale Animation

Select clips and choose `UMA > Animation > Create Non-Scale Animation`.

This creates a uniquely named copy while removing local scale curves only. Local position, rotation, and other curves remain.

Use it when the animation's translations are intentional but scale curves conflict with DNA or race proportions.

## Animation Clip Target Renamer

Open `UMA > Animation > Animation Clip Target Renamer`.

Add animation clips, then inspect their unique binding paths. The utility can:

- Edit individual paths.
- Prepend a hierarchy prefix.
- Remove characters from the left.
- Replace text across paths.

`Refresh` reloads curves from the assigned clips and discards un-applied path edits. `Apply` clears and rebuilds the clips' editor curves at the edited paths, modifying the original assets rather than creating copies.

The renamer handles editor curve bindings. Validate object-reference curves and specialized imported-clip data separately. Use duplicated clips for risky hierarchy migrations.

## Recommended Workflow

1. Duplicate or commit the source clips.
2. Decide whether position curves should remain.
3. Run clean/non-scale conversion on a representative clip.
4. Preview it on multiple UMA body shapes.
5. For path renaming, change one hierarchy segment and apply.
6. Inspect missing bindings and animation events before batch processing.
