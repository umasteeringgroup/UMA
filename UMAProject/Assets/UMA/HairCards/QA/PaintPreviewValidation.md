# Paint-preview recovery regression

## Failure reproduced

The reported symptom was orange painting disappearing after mouse release and
cursor exit, with Undo recovering the display but removing the latest stroke.
Normal vertex/texture stroke completion alone did not erase the serialized map
in the regression. A separate preview-state failure reproduced the orange-to-blue
disappearance on the GPU while leaving every paint value intact:

- The hidden texture-map shader did not declare its material properties.
- Preview textures are intentionally `HideAndDontSave`, so restoring a material's
  editor-serialized state can clear its temporary texture reference.
- The preview cache treated an unchanged paint revision as proof the entire GPU
  view was valid. It could skip restoring that reference or recreating resources.

Shader declarations alone were not sufficient: temporary texture references are
still intentionally excluded from serialization. The fix declares the properties,
rebinds existing textures even on a cached update, recreates lost preview resources,
and checks the visible map during idle editor updates, including off-viewport.
Binding-only recovery does not reupload the texture or rebuild the groom. It does
not edit the map, dirty the groom, or add/remove an Undo operation.

This reproduces a matching preview failure, not evidence that every possible
mouse-exit disappearance has the same cause. If the colored overlay disappears
entirely rather than turning blue, or saved values change, investigate that case
separately instead of assuming this preview-cache failure.

## Regression coverage

Six new cases verify:

- Vertex and texture paint survive stroke release, repeated cursor-exit cleanup,
  rebuilds, and Undo/Redo.
- Restoring the active texture-preview material loses its temporary texture
  reference, then idle recovery restores the actual orange GPU output without
  changing the map's serialized contents or revision.
- Lost material, texture, mesh, or shader parameters recover on an otherwise
  cached update; GPU color and original paint remain correct.

An uncorrected GPU reproduction read blue (R approximately 0.008) instead of
orange after material restoration. The corrected focused run passed all five
selected tests, including existing paint/Undo/preview tests.

Full source import, compilation and release gate on Unity **6000.3.18f1**, on
2026-09-16: **658 passed, 0 failed, 0 skipped**. Metadata preflight covered 99
Hair Cards C# scripts. Results:
`tmp/HairCardsSourceValidation/Logs/HairCardsReleaseGate/8be12f8fb7fc47d3b2510f04f636d91d/editmode.xml`.

Tests run in the existing isolated validation project. No user groom, sample,
paint map, texture asset or material asset is migrated or rewritten by this fix.
