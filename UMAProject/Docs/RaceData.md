# RaceData

`RaceData` defines a UMA "race": base pose, DNA converters, renderer bounds, and supporting data.

## Fields
- `raceName`: identifier used in recipes and systems
- `TPose`: reference rig pose (required for Humanoid)
- `dnaConverterList`: list of `DynamicDNAConverterController` assets
- `dnaRanges`: optional limits per converter for different races
- `useManualRendererBounds`, `manualRendererBounds`, `manualRendererBoundsCenter`: override SkinnedMeshRenderer bounds
- `umaTarget`: Humanoid or Generic
- `expressionSet`: optional facial rig config
- `tags`: string tags to filter races

## Methods
- `Validate()`: ensures required data (e.g., TPose for Humanoid)
- `GetDNANames()`: aggregated names from dna converters
- `ResetDNA()`: calls Reset on plugins in the converters
- `GetConverters(UMADnaBase)`: returns the active converter list

## Editor/Runtime Notes
- When `useManualRendererBounds` is enabled, UMA generator applies the extents to each SkinnedMeshRenderer; useful to avoid view-culling when avatar pose/animations exceed bounds.
- Disabling DNA converters can temporarily speed up generation for tests.
- Set `noAutoAdd` to stop this race from being indexed by bulk scans.
