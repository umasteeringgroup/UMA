# Samples

- `SlotMaskExample.asset` is a mask-stack preset that restricts painting to reconstructed material surface 0.
- The procedural-noise brush and curvature model plugin are in `../Plugins/` and are discovered automatically.
- `../Brushes/DefaultBrushLibrary.asset` contains circle and square presets.

For the avatar input, use the generated UMA prefab at `Assets/UMA/Core/Defaults/UMADynamicCharacterAvatar.prefab` or any generated character already in your scene. Overlay Painter deliberately reconstructs the selected live avatar so wardrobe, DNA, renderer assets, UVs, and source overlays match the character being edited.
