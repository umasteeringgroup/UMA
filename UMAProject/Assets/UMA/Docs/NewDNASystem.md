# New DNA System (UMA 3) — DNAGroups, DNA, and DNAEffects

UMA 3 introduces a new live DNA system that replaces `DynamicDNAConverterController` / `DynamicDNAPlugin` converters. Instead of pre-baked vertex data, DNA is a set of `ScriptableObject` assets that drive the character at runtime through a unified effect pipeline: bone transforms, poses, blendshapes, mesh modifiers, overlay UVs, and shared colors.

This document covers creation, assignment, effect reference, runtime behavior, and authoring gotchas.

Namespaces: `UMA`, `UMA.CharacterSystem`  
Key files: `Assets/UMA/Core/Scripts/NewDNA/`

---

## Overview — Live DNA vs Legacy DNA

**Legacy DNA:** `RaceData.dnaConverterList` held converter controllers that executed during generation to deform a pre-built mesh. DNA was baked into the combined mesh and required a full rebuild to change.

**New DNA:**
- `RaceData.useNewDNA = true` switches the race to the new path.
- `RaceData.DNACollection` holds a list of `DNAGroup` assets. Each group holds `DNA` assets.
- At runtime `DNAInstanceCollection` is built from the collection. Each `DNAInstance` is a name + value (0..1) + parent group.
- Effects are evaluated live every build:
  - `AfterRecipeGenerated(avatar)` — modifies the merged recipe before generation (shared colors, overlay transforms, mesh modifiers).
  - `PreApply / Apply / PostApply(umaData)` — modifies the live skeleton and renderers after the skeleton is built.
  - `Restore` — restores only the bones touched by an effect to the post-DNA baseline.
- `DNABuildType` flags (`None, Texture, Mesh, Rig, BlendShape, SharedColors, MeshModifiers, Base, All`) tell the pipeline what dirty work is needed.
- Because effects act on the live skeleton and materials, sliders in the DCA inspector feel immediate. You do not need to rebake slot meshes for most changes.

---

## Core Concepts

### DNAGroup

`DNAGroup : ScriptableObject`

| Field | Type | Description |
|---|---|---|
| `DNAArea` | `string` | Grouping/category shown in the customizer. Examples: `Body`, `Face`, `Arms`, `Legacy`. Used by `DnaSetter.Category` and DCA foldouts. |
| `dnaList` | `List<DNA>` | DNA assets that belong to this group. Order is display order. |
| `MaxTotalValue` | `float` | If > ~0.0001, the customizer enforces a cap on the sum of values in this group. When you raise one DNA above the cap, other non-zero DNAs in the same group are reduced equally until `sum <= MaxTotalValue`. Leave 0 for no cap. |

Menu: `Assets → Create → UMA → DNA → DNA Group`

**DNAGroup Editor (Inspector):**
- Drag-and-drop area: drop DNA assets to add them. Duplicates are ignored, list is auto-sorted by name.
- Per-entry foldout: asset field, effect summary (name + type), inspect button, delete.
- Buttons: `Ping DNAGroup Asset`, `Save Now`, `Rebuild Characters` (calls `UMAAssetIndexer.RebuildAllUMAS()`).

### DNA

`DNA : ScriptableObject`

| Field | Type | Description |
|---|---|---|
| `name` (Object name) | `string` | **Identity**. The file/asset name is the dictionary key. `DNACollection` indexes by `dna.name`. Must be unique across all groups added to a race. |
| `displayName` | `string` | Optional nice name for UI. If empty, `name` is used. |
| `description` | `string` | Tooltip / docs for artists. |
| `defaultValue` | `float` 0..1 | Value used when no instance overrides it. `GetDefaultDNA()` copies this. Values that equal default are skipped during Apply for performance. |
| `effects` | `List<DNAEffect>` (`[SerializeReference]`) | Polymorphic list of effect objects. Multiple effects per DNA allowed. |

Menu: `Assets → Create → UMA → DNA → DNA Item`

**Persistence:**
- `GetBuildType()` ORs `AreaEffect` from all effects.
- Runtime execution order inside one DNA: `AfterRecipeGeneration`, `Restore` (on rebuild), `PreApply`, `Apply` (base poses first, then others), `PostApply`.

### DNACollection and DNAInstance / DNAInstanceCollection

`DNACollection` is a serializable container on `RaceData`:

```
RaceData.DNACollection.DNAGroups = List<DNAGroup>
```

Methods:
- `LoadDictionary()` — rebuilds `dnaDictionary: name -> DNA`.
- `GetDNANames()` — all keys.
- `HasDNA(name)` — check exists.
- `GetDefaultDNA(race)` — creates a `DNAInstanceCollection` filled with instances using `defaultValue` and linked parent group.
- `Reset()` — clears cached dictionary.

`DNAInstance`: `{ Name, Value 0..1, enabled bool, parentGroup DNAGroup }`

`DNAInstanceCollection`:
- `dnaInstances: List<DNAInstance>` — flat list to process.
- `dnaGroupInstances: Dictionary<DNAGroup, List<DNAInstance>>` cache rebuilt by `LoadInstanceGroup()`.
- Provides `GetValues()/SetValues()`, `GetNames()`, `GetDNAInstancesByGroup()`, `GetUnknownDNAInstances()` (instances whose name is not in current `DNACollection`), `GetDNAByGroup()`.
- Pipeline entry points returning `DNABuildType` flags:
  - `AfterRecipeGenerated(DynamicCharacterAvatar)` — after recipe merge, before combiner.
  - `PreApply(UMAData)`
  - `Apply(UMAData)` — runs `ApplyBaseBonePoseEffects` (all `DNAEffect_BonePose` where `isBasePose==true`) first, then `ApplyNonBaseEffects`.
  - `PostApply(UMAData)` — blendshape application etc.
  - `Initialize(DNACollection)` — must be called before use.

---

## DNAEffect Base Class

`DNAEffect` is abstract, `[Serializable]`.

**Core mapping:**

```
raw value (0..1, from DNAInstance)
    ↓ curve.Evaluate(raw)  — AnimationCurve, default linear 0,0.5,1
    ↓ minMapping + evaluated * (maxMapping-minMapping)
    = GetMappedValue()
```

| Field | Type | Description |
|---|---|---|
| `EffectName` | `string` | Freeform label for this effect instance inside a DNA. |
| `enabled` | `bool` | Skip if false. |
| `curve` | `AnimationCurve` | Remap 0..1. Use flat center at 0.5 for "no effect in middle". Buttons: `Reset` (linear), `Copy to Selected`, `Set Linear`. |
| `minMapping` / `maxMapping` | `float` | Output range. For bone effects typical -1..1. For colors 0..1, for translation -0.1..0.1 etc. |
| `DNACurve _TemplateCurve` (editor only) | `DNACurve` | Object field to load a reusable template: asset type `DNACurve` (`Curve`, `minMapping`, `maxMapping`, `Description`). Menu `Assets/Create/UMA/DNA/DNA Curve Mapping`. When assigned, copies min/max/curve into this effect (not saved as ref). |
| `expanded / selected / showHelp` | bool | Editor foldout state. |

Virtual hooks:
- `AreaEffect : DNABuildType` — what this effect dirties.
- `AfterRecipeGenerated(UMAData, DNA, float)`
- `Restore(UMAData, DNA, float)`
- `PreApply`
- `Apply`
- `PostApply`
- `DoGui` (editor)

---

## Effect Reference

### DNAEffect_BonePose

Applies a `UMABonePose` asset scaled by mapped value.

| Field | Type | Notes |
|---|---|---|
| `bonePose` | `UMABonePose` | Required. Pose is array of position/rotation/scale deltas. |
| `isBasePose` | `bool` | If true, applied during `ApplyBaseBonePoseEffects` phase — right after skeleton reset, before all non-base rig effects. Use for A-pose / T-pose correction or base skeleton shape. If false, applied in normal phase and layers on top. |

`AreaEffect = Rig`  
`Restore` restores only bones in the pose via `skeleton.Restore(hash)`.

**Gotcha:** Order matters. Two base poses compound. Keep base poses limited to one per area or ensure they target disjoint bones.

### DNAEffect_BoneTranslate

Local-space translation.

| Field | Description |
|---|---|
| `BoneName` | Skeleton bone name. Case-sensitive. Hash-cached in editor. |
| `Translation` | `Vector3` vector scaled by mapped value and added to current position. |

Formula: `skeleton.SetPosition(hash, currentPos + Translation * mapped)`.  
`AreaEffect = Rig` / `Restore` restores bone.

**Typical mapping:** `min=-1, max=1`, curve with midpoint 0.5 = no move.

### DNAEffect_BoneRotate

Single-axis rotation.

| Field | Description |
|---|---|
| `BoneName` | Target bone. |
| `RotationAxis` | Local axis, e.g. `Vector3.up`. Does not need to be normalized but should be. |
| `RotationAngle` | Degrees applied when mapped=1. Final angle = `RotationAngle * mapped`. |

Formula: `currentRotation * Quaternion.AngleAxis(angle, axis)`.  
`AreaEffect = Rig`.

### DNAEffect_BoneScale

Scale factor.

| Field | Description |
|---|---|
| `BoneName` | Target bone. |
| `ScaleFactor` | Per-axis multiplier. Result scale = `currentScale * (1 + ScaleFactor * mapped)`. |

If you want 50% larger at mapped=1, set `ScaleFactor=(0.5,0.5,0.5)`.  
`AreaEffect = Rig`. Use `HasBone` guard.

### DNAEffect_BoneTransform

Absolute lerp toward target transform.

| Field | Description |
|---|---|
| `boneName` | Target. |
| `Position` | Target local position. |
| `Rotation` | Target local euler degrees. |
| `Scale` | Target local scale. |

Current code lerps: `current + (current - target)*mapped` for pos/scale and `Slerp(current, quatTarget*current, mapped)` for rot. Intent is absolute blending; author target values by copying the bone's desired transform in pose tool and using a 0..1 curve.  
`AreaEffect = Rig`. Best for pose-like DNA that needs precise end points.

### DNAEffect_BlendShape

Sets blendshape weight across all renderers.

| Field | Description |
|---|---|
| `BlendShapeName` | Exact blendshape name in the combined mesh. |

Execution: `PostApply` — after mesh built. Loops `avatar.GetRenderers()`, gets `sharedMesh.GetBlendShapeIndex(name)`, calls `SetBlendShapeWeight(index, mapped*100)`.  
`AreaEffect = BlendShape`. Mapping typical `min=0,max=1` if you want 0..100 weight.

### DNAEffect_MeshModifier

Injects vertex sculpting.

| Field | Description |
|---|---|
| `meshModifier` | `MeshModifier` asset containing `Scale/Translate` operations per slot. |

Executes in `AfterRecipeGenerated` via `avatar.AddMeshModifiers(meshModifier.GetScaledRuntimeModifiers(mapped))`.  
`AreaEffect = MeshModifiers`. Modifiers participate in `Default/Jobified/BoneBaking` combiners. Requires `useNewDNA` race; mesh modifiers must target valid `SlotDataAsset.sourceSlot` names.

### DNAEffect_OverlayUVTransform

UV rectangle transform for an overlay.

| Field | Description |
|---|---|
| `overlayName` | Overlay name to find with `umaRecipe.FindFirstOverlay(name)` after merge. Case-sensitive. |
| `offset` | `Vector2` multiplied by mapped. |
| `scale` | `Vector2` multiplied by mapped. |
| `rotation` | Float 0..360 multiplied by mapped. |

Sets `overlay.Translate`, `Scale`, `Rotation`.  
`AreaEffect = Texture`. Useful for sliding tattoos, makeup, or scaling detail overlays.

### DNAEffect_SharedColor

Full color lerp + combine into a shared color channel.

| Field | Description |
|---|---|
| `sharedColorName` | Name from `characterColors` / `SharedColorTable`, e.g. `Skin`, `Hair`, `Eyes`. |
| `FromColor` / `ToColor` | Color endpoints. |
| `colorCombineMethod` | `Range`: `From + (To-From)*v` (lerp). `Additive`: `From + To*v`. `Subtractive`: `From - To*v`. `Multiply`: `FromColor` scaled by `ToColor * v` (`col *= ToColor * v` with starting `From`). `Replace`: `To * v`. |
| `TextureNumber` | Channel index in `OverlayColorData` (0 = base). |
| `colorType` | `BaseMultiplier` (colorizes/darkens) or `Additive` (brightens/add overlay). Maps to `OverlayColorData.SetColor(index, isAdditive, col)` |

`AreaEffect = Texture`. Runs in `AfterRecipeGenerated`. Requires color to exist on DCA; will `EnsureChannels`.

### DNAEffect_SharedColorChannel

Writes a single R/G/B/A component of a shared color.

| Field | Description |
|---|---|
| `SharedColorName` | e.g. `Skin` |
| `TextureNumber` | Channel index |
| `colorType` | `BaseMultiplier` / `Additive` |
| `colorComponent` | `Red, Green, Blue, Alpha` |
| `ChannelValue` | Multiplier `0..1` applied: final component = `mapped * ChannelValue` |

Overwrites only that component, preserving others.  
`AreaEffect = Texture`. Runs in `AfterRecipeGenerated` (despite calling base `PreApply` internally).

### DNAEffect_SharedColorProperty

Writes into `OverlayColorData.PropertyBlock` for shader customization (e.g. `_Color`, tiling, custom float).

| Field | Description |
|---|---|
| `sharedColorName` | Target shared color |
| `propertyName` | Property in `UMAMaterialPropertyBlock`, case-sensitive, e.g. `_Metallic`, `_EmissiveColor` |
| `parameterType` | `[Flags]` `Color`, `Float`, `Both` |
| `zeroColorValue` / `oneColorValue` | Color lerp endpoints when `Color` flag set: `Color.Lerp(zero, one, mapped)` |
| `floatValue` | When `Float` flag set: `property Value = mapped * floatValue`. Inspector label shows "Zero Float Value" but current code multiplies only. |

Creates `UMAColorProperty` / `UMAFloatProperty` if missing.  
`AreaEffect = Texture`.

---

## Authoring Workflow

### 1. Create a DNAGroup

1. In Project, `Create → UMA → DNA → DNA Group`.
2. Name asset, e.g. `BodyGroup`. Set `DNAArea = Body`.
3. Leave `MaxTotalValue = 0` unless you want capped sliders (e.g. muscle vs fat exclusive).
4. Optionally drag existing `DNA` assets into the drop box at top of its Inspector.

### 2. Create DNA

1. `Create → UMA → DNA → DNA Item`.
2. Rename asset to the exact DNA name you want to reference, e.g. `height`, `armLength`, `belly`. **Asset name = DNA ID.**
3. Set `defaultValue` (0..1). 0.5 is neutral conventionally.
4. Add description.
5. In `Effects` list, click `+` and pick effect type from dropdown (all `DNAEffect_*` subtypes).
6. Configure `Effect Name`, curve, `minMapping`, `maxMapping`.

**Curves:** Use `AnimationCurve`. Default linear 0→1. For bidirectional DNA where 0.5 = neutral, add keys `(0,0)`, `(0.5,0.5)`, `(1,1)` and set min=-1 max=1, so 0.5 maps to 0 movement. For color, keep 0..1 linear.

**Template Curves:** Create `Create → UMA → DNA → DNA Curve Mapping` asset storing reusable curve + min/max. Drag into effect's `Template Curve` field to copy once.

### 3. Add Effects to DNA

You can stack effects:

- For a height DNA: `BoneScale` on `Position` bone (Y up) + `BoneTranslate` on calf bones + `BlendShape` for face scaling.
- For skin tone DNA: `SharedColor` with `Range` from pale to dark.
- For muscle DNA: `MeshModifier` + `BoneScale`.

Each effect's `enabled` toggle lets you A/B test.

### 4. Put Groups on RaceData

1. Select `RaceData` asset (e.g. `HumanMale`, `HumanFemale`).
2. Enable `useNewDNA` in Inspector top.
3. Find `DNACollection` foldout. Expand `DNAGroups` and increase size, assign your `DNAGroup` assets.
4. Ensure `disableDNAConverters` is not conflicting (new path ignores converters).
5. Save. If you use UMA Asset Indexer, rebuild index (`UMA → Rebuild Index`) so validation picks it up.

Validation (`Window → UMA → Race Validation`): warns if `DNACollection` null or empty.

### 5. See DNA in Customizer (DCA)

`DynamicCharacterAvatar` inspector:

- `Customization` foldout → groups split by `DNAArea`. Inside each area, sliders per DNA sorted by stored order (Group Editor auto-sorts by name on drop).
- Slider calls `DnaSetter.Set(val)` which updates `DNAInstance.Value`, enforces `MaxTotalValue` if set, and marks DCA dirty.
- In Play mode, use top buttons `Full Build / Textures / DNA / Mesh` under `Force Regenerate` to force passes without full rebuild. `DNA` button calls `ForceUpdate(true,false,false)`.

Added DNA via code also appears after next `GetDefaultDNA` initialization or if you call `LoadInstanceGroup`.

---

## Live DNA Behavior

- **Build-time vs Live:** `AfterRecipeGenerated` touches `UMARecipe` (colors, overlays, meshModifiers) — this requires texture/mesh regeneration. `Apply` touches skeleton after `UMASkeleton` is reset to TPose + base poses — this is Rig dirty only and cheaper. BlendShape is PostApply touching SMRs.
- **Restore:** Before each Apply, UMA restores only affected bones via `skeleton.Restore(hash)` to post-DNA baseline, so DNA is not additive across frames.
- **ValueDiffers optimization:** If `Math.Abs(Value-defaultValue) <= Epsilon`, effect is skipped. To always enforce (e.g. for setup), set default far from neutral or set DNA to slightly off default then back.
- **Blendshape loading:** DCA has `loadBlendShapes`, `loadOnlyUsedBlendshapes` etc. New DNA BlendShape effect looks up by name on combined mesh — ensure mesh has that shape and DCA is configured to load it.

---

## Programming API

```csharp
// Get current DNA as setters (includes live group)
var setters = avatar.GetDNA(); // Dictionary<string, DnaSetter>

// Set by name
avatar.SetDNA("height", 0.75f, rebuild:true);

// Get raw values
var values = avatar.GetDNAValues(); // Dictionary<string,float>

// Direct access to instance collection
var newDNA = avatar.umaData.dnaInstanceCollection;
var inst = newDNA.dnaInstances.Find(i=>i.Name=="height");
inst.Value = 0.8f;
avatar.ForceUpdate(true,false,false);

// Add new group at runtime (advanced)
// Ensure collection initialized
avatar.activeRace.data.DNACollection.DNAGroups.Add(myGroup);
avatar.activeRace.data.DNACollection.LoadDictionary();
avatar.umaData.dnaInstanceCollection.Initialize(avatar.activeRace.data.DNACollection);
```

`DnaSetter.Set(float)` also handles `MaxTotalValue` redistribution automatically.

---

## Multi-Effect Example — `Muscularity` DNA

Goal: One slider that:
- Scales upper arm bones outwards (bicep bulge)
- Translates chest forward slightly
- Applies a chest blendshape `chest_muscular`
- Tints skin shared color slightly more saturated
- Adds mesh modifier for forearm vein detail at high values via curve

Setup:

1. Create `DNAGroup` asset `UpperBodyGroup`, `DNAArea=Body`.
2. Create `DNA` asset named `muscularity`, `defaultValue=0.5`, `displayName=Muscularity`.
3. Effects list (Add 5):

**Effect 0 - Left Upper Arm Scale**
- Type `DNAEffect_BoneScale`
- `EffectName = L UpperArm Bulge`
- `BoneName = l_upperarm`
- `ScaleFactor = (0.3, 0.2, 0.3)` — extra girth.
- Curve linear, `min= -1, max=1` — midpoint 0.5 neutral.

**Effect 1 - Chest Translate**
- Type `DNAEffect_BoneTranslate`
- `BoneName = chest`
- `Translation = (0,0,0.05)` — slight forward.
- `min=0, max=1`.

**Effect 2 - BlendShape**
- Type `DNAEffect_BlendShape`
- `BlendShapeName = chest_muscular`
- `min=0, max=1` — 0 off, 1 = 100% weight.

**Effect 3 - Skin Saturation via SharedColor**
- Type `DNAEffect_SharedColor`
- `sharedColorName = Skin`
- `TextureNumber = 0`
- `colorType = BaseMultiplier`
- `colorCombineMethod = Range`
- `FromColor = (1,1,1,1)` (no change), `ToColor = (1.1,0.95,0.9,1)` slightly warmer.
- Curve: ease in after 0.7 so tint only at high muscularity — edit curve keys (0=0,0.7=0.2,1=1).

**Effect 4 - Vein Detail MeshModifier**
- Type `DNAEffect_MeshModifier`
- `meshModifier = ForearmVeins_MeshModifier` (your MeshModifier asset).
- Curve: keep 0 until 0.8, then ramp to 1 `(0,0),(0.8,0),(1,1)`, `min=0,max=1`.

4. Save, drag `muscularity` DNA into `UpperBodyGroup`.
5. Assign `UpperBodyGroup` to `RaceData.DNACollection`.
6. Build DCA. `Body → muscularity` slider now drives all 5 at once.

You can duplicate this DNA, rename to `muscularity_female`, adjust `ScaleFactor` etc., and add to a Female group for race-specific tuning while keeping same workflow.

---

## Gotchas & What to Look For

- **Asset name = DNA key.** Renaming file renames key. If you rename after characters saved, old recipes will have `Unknown DNA` instances. Use `GetUnknownDNAInstances()` to detect. Keep names stable, use `displayName` for UI.
- **Missing parentGroup.** Hand-built `DNAInstance` without group won't enforce `MaxTotalValue` and won't appear in grouped UI. `GetDefaultDNA` sets it; if constructing manually, assign.
- **Bone names must match skeleton exactly.** Check `TPose` or generated skeleton hierarchy. Logs won't always error, just skip if `HasBone` false (some effects check, some don't).
- **BlendShape names must exist on all LODs you use.** If missing on lower LOD, no error, just silent skip.
- **`defaultValue` skipped optimization.** If your effect should run even at default (e.g. color always set), set default slightly outside range or ensure `ValueDiffers`. Better: make default 0.5 neutral and handle neutral in curve min/max=0 mapping.
- **MaxTotalValue redistribution is equal among other non-zero.** Not weighted. If over cap, all other non-zero in group reduced equally, may go to 0. Plan groups accordingly.
- **Curve evaluation before min/max.** Don't double-map. If you want output -0.05..0.05, set `min=-0.05,max=0.05` with linear curve 0..1.
- **SharedColor must exist.** `GetColor(name)` returns null if not in `characterColors`. Ensure DCA's `characterColors` contains entry.
- **TextureNumber / channelCount.** Code does `EnsureChannels(TextureNumber+1)`, but material may not have that many textures. Keep to valid range.
- **Overlay name case-sensitive** and must be present in merged recipe. For wildcard/placeholder overlays, name resolves after match.
- **BasePose vs non-base.** Only `BonePose` has `isBasePose`. If you have two base poses affecting same bone, last wins. Order by group list order + instance list order.
- **MeshModifier scaling:** `GetScaledRuntimeModifiers(mapped)` scales operations. If your modifier asset not set up with source slots matching `SlotDataAsset.sourceSlot`, it will be ignored.
- **Saving:** Since groups are `ScriptableObject`, changing `dnaList` dirties asset. Editor code auto-saves after 0.5s delay. If you batch edit, click `Save Now` or `AssetDatabase.SaveAssets`.
- **Addressables:** DNA assets are regular assets; no special handling. Ensure they are included in build / addressable group if race loads from addressables.
- **Old DNA coexistence:** If `useNewDNA` false, new system ignored. If true, legacy converters are disabled (`dnaConverterList` returns null). Don't mix.
- **Performance:** 50+ active DNA with many bone effects is cheap (Rig only). 50+ with many `SharedColor` / `MeshModifier` cause Texture/Mesh dirty and are heavier. Profile.

---

## Troubleshooting

- **Slider does nothing:** Check race `useNewDNA`, collection contains group, group contains DNA, DNA enabled, effect enabled, bone/shape/color name exists, value differs from `defaultValue`, Min/Max not zero.
- **Color not changing:** Verify `sharedColorName` matches `characterColors` entry (e.g. `Skin`), check `TextureNumber` valid, material uses shared color.
- **Blendshape not moving:** Verify mesh actually contains shape, `loadBlendShapes` true on DCA, and that shape not baked out via `PrebakedBlendshapes` / `UnbakedShapesToInclude`.
- **DNA appears under "Unknown":** Race's `DNACollection.dnaDictionary` doesn't contain that name. Rebuild dictionary `LoadDictionary()`, ensure group added, index rebuilt.
- **MaxTotalValue not working:** Only works via `DnaSetter.Set()` in customizer. Direct value assignment to `DNAInstance.Value` bypasses redistribution — call setter or implement own logic.
- **Changes not saving:** `DNAGroup` editor does delayed save. Close Unity without save prompt loses pending changes? Click `Save Now`.

---

## See Also

- `Assets/UMA/Docs/DynamicCharacterAvatar.md` — build pipeline, wardrobe API.
- `Assets/UMA/Docs/RaceData.md` — race fields, manual renderer bounds.
- `Assets/UMA/Docs/SlotDataAsset.md` — mesh data, DNA fields on slot.
- `Assets/UMA/Docs/MeshHideAssets.md` — face editor stage (distinct from DNA).
- `Assets/UMA/Core/Scripts/NewDNA/` — source for all effects.
