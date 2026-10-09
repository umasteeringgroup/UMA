# Clothing finishes and pipeline additions — validation

Validated on 2026-10-06 in the current `C:\GitHub\UMA\UMAProject` checkout,
using its running Unity 6000.3.18f1 Editor and installed UMA NextGen 3.1f1
UMASettings asset. The installed version was read through Unity serialization.

## Automated results

All 126 focused Edit Mode tests passed, with no skipped tests:

| Suite | Passed | Coverage |
| --- | ---: | --- |
| ClothingFinishAndGeometryTests | 11 | Geometry AO/thickness, nonuniform scale, open surfaces, draft-cache upgrades/cancellation, mirrored/stacked UV contours, mask holes, shared material coverage, output switches, underlying normal/height preservation and authored blend preservation |
| TexturePaintClothingPresetTests | 1 | All seven bundled presets apply and regenerate their complete stacks through the plugin host |
| TexturePaintPbrImportTests | 10 | Filename suggestions, packed channels, DirectX normals, real asset import, alpha coverage, color/data settings, enabled/full-opacity defaults, original-file preservation and overwrite refusal |
| Weathering normal regression suite | 21 | Existing weathering generators consuming underlying normal/height detail, including CPU/GPU paths and exclusion of later layers |
| Normal Control regression suite | 10 | Neutral values, normal combination, strength, packing and data-channel behavior |
| Color correctness regression suite | 73 | Color-space contracts, generator/brush/path output and CPU/GPU coverage |

Machine-readable reports are in `Build/Validation/ClothingFinishes/` in this checkout.
An integration test exposed missing numeric bounds on the new material-profile enum;
those bounds were corrected and the seven-preset regeneration test passed afterward.

The importer and automatic-seam UI Toolkit trees were also constructed successfully
in the running Editor. All seven preset assets and their thumbnails were generated
through Unity asset APIs. Representative cotton, leather and coated-fabric thumbnails
were inspected visually.

## Limits and manual acceptance

Tests use synthetic meshes/materials and temporary owned assets. They do not establish
visual acceptance or production-resolution performance on the user's saved pants
document. No material layers were added to that document during validation.

Manual acceptance should cover:

1. Rebuild cached mesh maps, then regenerate stale layers on the saved pants project.
2. Apply each finish preset, inspect scale/color/roughness under scene lighting, and
   confirm Normal Control detail on top of existing normal maps.
3. Age a print using its actual coverage mask, including holes and soft alpha edges.
4. Select relevant UV or mask contours, create seams, and edit the resulting paths.
5. Import a production PBR set, review channel assignments and normal convention,
   then apply the generated material preset.

Automatic mesh maps currently use only the target mesh. High-to-low cages and other
slot occluders remain future work. Print aging reveals a chosen backing color rather
than reconstructing arbitrary hidden fabric layers. Seam offsets are UV distances;
large insets on concave contours can require manual cleanup. Existing explicitly
authored Normal Control blend modes are preserved rather than silently migrated.
