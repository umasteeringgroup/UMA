# UMA Hair Cards

UMA Hair Cards is a guide-driven, non-destructive hair-card authoring system for Unity 6.3 and newer. The editable `HairGroomAsset` is the source of truth; generated meshes and UMA assets are deterministic bake outputs.

## Quick start

1. Select a readable `Mesh`, a generated `DynamicCharacterAvatar`, or an existing `HairGroomAsset`.
2. Choose **UMA > Hair Cards > Open Hair Card Stage**.
3. In **Growth**, paint the Growth Area or select source vertices and apply the selection to a map.
4. In **Guides**, preview deterministic guide generation and accept the result. Hand-place extra guides where the silhouette needs control.
5. In **Groom**, use Comb, Grab, Smooth, Length, Cut, Width, Clump, Part, and Freeze. Position and width edits are stored on sculpt layers.
6. In **Cards**, assign a ribbon or tapered-tube profile, atlas, and children-per-guide settings.
7. In **Optimize**, author LOD card fractions and sample counts.
8. In **Validate & Bake**, run a dry run, resolve blocking errors, and bake Unity/UMA assets.

The stage autosaves the groom and keeps a recovery snapshot under `Assets/UMAProjectData/HairCards/Recovery`.

## Authoring model

- Groups separate coverage, volume, detail, flyaway, short, facial, brow, lash, and custom hair.
- Surface anchors use source asset identity, submesh, triangle, and barycentric coordinates. A topology fingerprint prevents silent root movement after source changes.
- Growth maps are per-source-vertex scalar fields. Included channels cover Growth Area, Density, Length, Flow, Lift, Width, Clump, child count, profile blend, and LOD importance.
- Guides are authored splines. Child cards are deterministic interpolation results and are never hidden editable state.
- Sculpt layers store guide point position, width, and roll deltas.
- Ordered guide/child modifiers include resample, length, width, smoothing, lift/gravity, flow, clump, parting, curl, wave, noise, twist, helper following, projection, collision, and mirroring.
- Helpers and constraints are embedded, stable-ID data. Curve rails, attractors, repulsors, cages, collision shapes, part lines, braid rails, and other helper roles share one model.
- Profiles generate flat double- or single-sided ribbons and tapered polygonal tubes with 3–12 sides.
- Atlas profiles provide weighted UV regions, flips, textures, and preview materials.

## Play-mode API

No service, local model, or provider is required. Generation is deterministic C# geometry code and works in a player build.

```csharp
using UMA.HairCards.Runtime;

HairGroomRuntimeAPI.GeneratedHair generated =
    HairGroomRuntimeAPI.Generate(groomAsset, lodLevel: 0);

HairGroomRuntimeAPI.ApplyTo(generated, meshFilter, meshRenderer, fallbackMaterial);

// Dispose when replacing or removing the generated mesh.
generated.Dispose();
```

For component-driven use, add `HairGroomRuntimeComponent` beside a `MeshFilter` and `MeshRenderer`, assign a groom, and call `Regenerate()` or `SetLodLevel()` as needed.

## Bake outputs

The bake pipeline evaluates and validates everything before it writes. Existing assets are updated in place so references remain stable.

- Unity Mesh assets for LOD 0 and every configured additional LOD.
- Closest-scalp-vertex skin weights and source bind poses when the source exposes compatible weights.
- `SlotDataAsset` containing the generated geometry.
- `OverlayDataAsset`, either by reusing an assigned overlay or from an assigned UMA material and the first available hair atlas.
- `UMAWardrobeRecipe` when a RaceData, wardrobe slot, UMA slot, and overlay are available.
- Optional UMA global-library registration.

The generated UMA slot supports multiple mesh submeshes, but a production hairstyle should keep material count low for draw-call efficiency. Always equip the baked recipe on representative avatars and inspect deformation before release.

## Shortcuts

Shortcuts are registered with Unity's Shortcut Manager and can be remapped:

- `Q`: Select
- `P`: Paint Growth
- `C`: Comb
- `G`: Grab
- `S`: Smooth
- `Shift+R`: Rebuild Preview

Scene navigation retains Unity's Alt-based controls.

## Assemblies

- `UMA.HairCards.Core`: versioned data, evaluation, generation, geometry, UVs, skin-weight transfer, and validation.
- `UMA.HairCards.Runtime`: player-facing API and component.
- `UMA.HairCards.Editor`: stage, workspace, commands, recovery, diagnostics, and UMA bake pipeline.
- `UMA.HairCards.Editor.Tests`: deterministic data, generation, meshing, validation, skinning, and runtime API tests.

See [HairCardsManualQA.md](QA/HairCardsManualQA.md) for the release checklist and `Assets/UMA/Plans/HairSystem.MD` for the full product design.
