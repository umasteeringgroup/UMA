# Saved workspace layout validation

Validated 2026-10-06 in `C:\GitHub\UMA\UMAProject`, Unity 6000.3.18f1,
installed UMA NextGen 3.1f1 (read from the current UMASettings asset through Unity).

Four focused Edit Mode tests passed:

- Native Unity dynamic layout creates the stacked Layers/Properties hierarchy.
  Capturing real pane parents finds the owning window. Restoring the generated layout
  preserves its split proportions and selected Brush tab.
- Saving and reading preserves window geometry and selected tabs. A subsequent save
  replaces the single snapshot; an invalid replacement leaves the existing file intact.
- Deep layouts round-trip through the flat node format without Unity's recursive
  serialization limit. Cyclic saved data is rejected.
- The default definition places Properties underneath Layers/Brush and keeps Scene/2D
  in the right-hand tab group.

Reports: `Build/Validation/OverlayPainterLayout/`. Tests used temporary owned native
containers and files; they did not replace the user's saved layout. The final source
compiled in the running project. Documentation describes painter-only restoration when
panels have been docked among unrelated Unity windows.
