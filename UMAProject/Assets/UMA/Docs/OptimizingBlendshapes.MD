# Optimizing Blendshapes

**Do not put the entire blendshape library on every generated character.** Adding large numbers of blendshape frames to Unity meshes can make character generation very slow. A weight of zero does not remove a shape's data or recover the work spent loading it. Selective addition of blendshapes and the use of MeshModifiers for suitable customization are essential parts of a performant UMA content pipeline.

Keep the complete library in your authoring project. Give each runtime character only the shapes it needs: a small live set for animation, selected customization data for its appearance, and baked geometry where the result is fixed. Optimize the content before trying to hide its cost with a faster combiner.

This guide covers artists and developers using the slot-based UMA pipeline. It was checked against **UMA NextGen 3.1f1**, **Unity 6000.3.18f1**, in `C:\GitHub\UMA\UMAProject`. Unity 6.3 or newer is required. The supplied July–September 2026 Discord discussions informed the workflows; current source and assets determine the API and setting details below. The FBX race route has different source ownership; do not assume that slot-bake and generated-race-slot workflows apply to it unchanged.

## Choose how each shape should be represented

Decide how often a deformation must change, how many vertices it affects, and which garments must follow it.

| Requirement | Preferred starting point | Tradeoff |
| --- | --- | --- |
| Broad body proportions, limb length, overall stature | Bone DNA or a `UMABonePose` | Clothing with compatible skinning follows the rig; extreme changes still need corrective sculpting. |
| Blinks, visemes, expressions, frequently animated correctives | A deliberately small set of live blendshapes, or bone expressions | Changing a loaded weight needs no mesh rebuild, but retaining and loading the shapes costs memory and generation time. |
| Nose/ear details, permanent customization, occasional garment corrections | Sparse MeshModifiers driven by DNA or wardrobe | Stores selected vertex adjustments; changing the result requires mesh regeneration. |
| A finished character's fixed blendshape combination | Per-avatar blendshape baking | Removes those live frames from the output; source data and rebuild work may still be needed. |
| A fixed base form shared by a race | Race prebaking, optionally combined with a race T-pose | Can reuse generated race slots; retain only the live shapes that race needs. |
| An optional character-specific shape family | Additional Blendshape Slots containing small shape bakes | Loads shapes only with the selected bake, provided your content-loading setup also keeps unused bakes unloaded. |
| Discrete interchangeable parts, such as different ear meshes | Separate slots | Avoids continuous morph data, but introduces seam, fitting, wardrobe, and potentially renderer costs. |

A useful default is **bone-driven body proportions, MeshModifiers for infrequent local customization, and a small animated face set**. A hero face and a distant crowd character need different budgets. There is no universal safe number of shapes: vertex count, frame count, channels, renderer layout, platform, and generation frequency all matter.

## What blendshapes do well—and what they cost

Blendshapes provide precise authored silhouettes and local deformations that are difficult to obtain from a small skeleton. Artists can sculpt target shapes in a DCC, preview continuous transitions, and drive the result with UMA DNA. Matching shape names on body and clothing let one control affect both. Multiple frames can describe a nonlinear transition or an intermediate corrective.

Their costs belong to three different stages:

1. **Source loading and preparation:** slot/model assets hold names, frames, delta data, and preparation metadata. Keeping every bake referenced can retain this data even if the final mesh omits most shapes.
2. **Character generation:** UMA processes the selected sources and installs live frames into the generated Unity mesh. Unity mesh API application remains main-thread work in UMA's generation paths. More frames can cause longer builds and visible spikes.
3. **Animation and rendering:** active deformations add skinning work. Changing an already-loaded weight is different from building or loading its frames. Profile both separately.

Budget by **vertices × frames × stored delta channels**, not just the number of slider names. As a dense working-buffer estimate, 20,000 vertices × 100 one-frame shapes × three `Vector3` delta channels × 12 bytes is **72,000,000 bytes (about 68.7 MiB)**. Position-only arrays would be about 22.9 MiB. These are illustrative array sizes, not measurements of Unity's native/GPU storage or total project memory. Sparse data, absent channels, multi-frame shapes, temporary buffers, shared sources, and output mesh layout change the real cost.

A blendshape is not literally another complete mesh: it stores deformation deltas, not another set of all topology, UVs, materials, and bones. Nevertheless, a large dense library can become comparable to many meshes' worth of vertex data. UMA's sparse preparation improves suitable operations; it does not make a large live-shape library free.

Unity's [Mesh.AddBlendShapeFrame reference](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/mesh/addblendshapeframe) describes the vertex, normal, tangent, and frame inputs. Keep multiple frames only when their intermediate deformation is required.

## Artist workflow: author a library, ship selected sets

### Establish a stable mesh and naming contract

Keep a neutral mesh, its skeleton/rest pose, vertex order, and source files under version control. Shape bakes and extracted MeshModifiers depend on that identity. Equal vertex counts alone do not prove compatibility: reordering vertices, changing export splits, or altering the neutral basis can corrupt a deformation.

Use exact, consistent, case-sensitive shape names. A body and fitted shirt may intentionally share `BodyHeavy`; two unrelated controls should not. Some exporters prefix names with a deformer or object name. UMA's **Slot Blendshape Name Renamer** can repair slot names; update DNA, expressions, animation bindings, baking policies, and bake definitions when renaming a shape. Do not leave body and wardrobe names out of sync. See [UMA Editor Utilities](UMAEditorUtilities.md).

Test each shape at neutral, intermediate, and endpoint values, then in combinations. A pair of individually attractive sculpts can intersect when combined. Check eyes, mouth interior, eyelashes, joints, normals, tangents, and renderer bounds. Author matching LOD content; an LOD with different topology needs its own compatible deltas/modifiers.

### Separate shape families before importing everything

Classify the library into animated expressions, body customization, race defaults, optional identity/species shapes, and garment-specific correctives. Put only the required base and animation shapes on the normal production slots. Move optional live families into compatible additional slots; convert infrequent local changes into MeshModifiers.

For example, a game with 100 identity morphs does not need 100 identity frames on each NPC. A selected NPC can use its identity MeshModifier or its one selected identity bake, bake the finished identity where appropriate, and retain only its animation set.

In **UMA > Model to Race & Clothing...**, review the blendshape list deliberately. **All shapes initially start included.** Unchecking a shape removes its frames from the generated slots and excludes its generated DNA item. Turning off **Generate blendshape DNA** only suppresses slider creation; it does not remove selected shape geometry. See [Model to Race](ModelToRace.md).

Keep a full authoring version if it helps editing, but create a lean production version. Removing frames from the generated character and removing them from its loaded source assets are separate tasks.

### Make clothing follow the deformation

Bone changes generally move all compatibly skinned parts. A body blendshape moves only vertices carrying that shape; it does not automatically reshape arbitrary clothing or reposition skeleton bones.

For a live body morph, author or transfer a corresponding shape onto every garment that needs to follow it, using the same name and compatible weight range. A shirt and body do not need identical topology for their own matching named shapes. Identical vertex identity is required when attaching an additional shape bake to a particular target slot.

UMA's [Clothing Conformer](ClothingConformer.md) can bind selected clothing to the body, conform it after a body change, and save the result as a new slot or a slot blendshape. Use the neutral shape for binding, test the extrema, and manually correct difficult folds and joints. Rebind after topology changes. You can also transfer and refine shapes in Blender/Maya.

When a garment is modeled directly for a permanently baked race form, do not apply the same body morph again. Test race variants and wardrobe together; declaring cross-compatibility does not prove fit.

### Coordinate bones and shapes for large proportion changes

An adult-to-child shape that moves the mesh without moving joints will not produce a correct animated child rig. Use bone poses/DNA for the major proportions, then sculpt a corrective on that posed, skinned base. Avoid redefining the skeleton hierarchy midway through production.

One DNA can drive a Bone Pose effect and a Blendshape effect through coordinated curves. For an age range, an adult neutral can sit at the midpoint, with child and aged effects increasing toward opposite ends. Texture/color effects can follow their own curves. Ensure the shape was authored to cooperate with the bone pose, or the two changes may double-transform the result. Test walking, bending, facial motion, and fitted clothing throughout the range. See [DNA Creation Guide](DNACreationGuide.md) and [Pose Tools](PoseTools.md).

## Load optional shapes with Additional Blendshape Slots

An **Additional Blendshape Slot** is a normal `SlotDataAsset` used as a shape source for another slot. UMA reads its blendshape data without adding its geometry as another visible body or garment.

### Author and attach a bake

1. Duplicate the target's neutral source mesh without changing its vertex order or basis. Add only the optional shapes belonging to this bake. Give the bake a unique slot name.
2. Build it as a `SlotDataAsset`. For a split/UDIM body, create bakes matching the individual target slots; a whole-body bake cannot be attached to a smaller tile simply because both came from the same model.
3. Open the relevant recipe, expand the target slot, and open **Additional Blendshape Slots**. Use the recipe's Advanced editing view if the slot details are not visible in Standard View.
4. Assign the bake in **Add Slot**. The editor checks equal vertex counts, that the bake has shapes, and that it is not already listed. It does not prove vertex-order or neutral-basis compatibility.
5. Save the recipe. Enable **Load Blend Shapes** on the DCA and ensure the shapes survive any race bake policy. Build and inspect the generated renderer's shape names before creating DNA or expression controls.
6. Add matching bakes or other authored corrections for affected clothing. A bake attached to the body does not implicitly modify a shirt.

The recipe represents this with a source `SlotData` whose `blendShapeTargetSlot` names the target. Its nonempty target makes `isBlendShapeSource` true. The generator collects the source's shapes for that target and skips rendering the source geometry. No overlays are needed on the bake.

Do not repeat a shape name already supplied to the same target by its base slot or another bake. Current combining paths can reject duplicate source shapes. Sharing a name intentionally across different visible slots, such as a body and shirt, is a different use case.

### Select bakes at runtime

Package bakes in small recipes and load/equip only the required recipes through the project's normal UMA recipe/wardrobe loading workflow. A wardrobe region can select one identity bake; additive recipes can supply independent families. Keep the target slot present and use unique bake slot names so recipe merging does not collapse independent sources.

After changing the recipe selection, rebuild the character. If a source asset must be loaded asynchronously, wait for that load before requesting the build. For Addressables, ensure selected bakes and target assets are discoverable and retained for the build; release them through the project's normal lifetime management when no longer needed. A recipe reference is not itself an asynchronous streaming system. See [Addressables](Addressables.md).

For a custom recipe assembly callback, this is the core operation:

```csharp
using System;
using System.Collections.Generic;
using UMA;

public static class OptionalShapebake
{
    // Call while assembling the recipe, before generator source collection.
    // For a DynamicCharacterAvatar, use its RecipeUpdated event.
    // target must be the actual target SlotData in this build's recipe.
    public static void Add(UMAData data, SlotData target, SlotDataAsset bake)
    {
        if (data == null || target?.asset?.meshData == null || bake?.meshData == null)
            throw new ArgumentException("A built recipe target and loaded bake are required.");
        if (target.asset.meshData.vertexCount != bake.meshData.vertexCount ||
            bake.meshData.blendShapes == null || bake.meshData.blendShapes.Length == 0)
            throw new ArgumentException("The bake must match the target and contain shapes.");

        // Matching vertex order and neutral basis must be validated at authoring time.
        foreach (var existing in data.umaRecipe.slotDataList)
            if (existing != null && existing.isBlendShapeSource &&
                existing.asset == bake && existing.blendShapeTargetSlot == target.slotName)
                return;

        var source = new SlotData(bake) { blendShapeTargetSlot = target.slotName };
        source.SetOverlayList(new List<OverlayData>());
        data.umaRecipe.MergeSlot(source, false);
    }
}
```

Register a `RecipeUpdated` listener on the DCA, unsubscribe when its owner is disposed, and reapply the selected bake when the DCA reconstructs its recipe. This event runs after recipe assembly and before generation. Do not call `Dirty` recursively from that hook. Omit/remove the bake from the next assembled recipe to stop including it. Mutating a finished generated mesh with `AddBlendShapeFrame` bypasses this recipe lifecycle and will not reliably survive UMA regeneration.

Race prebaking can rename generated target slots. Resolve the actual target in the assembled recipe rather than assuming its source asset name is unchanged. Also validate bake deltas against the baked base: this mechanism does not retarget arbitrary shapes between different body forms.

## Convert customization to MeshModifiers

MeshModifiers are especially useful for small, sparse changes that are fixed during gameplay or edited occasionally. Their vertex adjustments are applied during generation; they do not need live Unity blendshape frames in the output. Dense full-body changes still have costs, and changing modifier values requires another mesh build.

1. Build an authoring avatar containing the required body and garment source slots.
2. Open its Mesh Modifier editor and choose **Blendshapes**.
3. Choose the source shape and the slots to extract. Extract and save the modifier.
4. Apply it through wardrobe or a DNA Mesh Modifier effect. The selected-slot inspector also offers **Extract to MeshModifier** / **Extract all**.
5. Compare the converted result against the source at the intended values, including clothing and shading.
6. Remove the equivalent live shape from production sources or bake/omit it from the output. Stop the old Blendshape DNA effect from driving the same deformation twice.

**Extraction is not automatically a lossless conversion of every blendshape.** The Mesh Modifier editor's extraction path currently uses the last source frame and records vertices with nonzero position deltas. Check multi-frame nonlinear shapes and normal/tangent-only changes separately. A one-frame positional corrective is a much simpler candidate than a complex animated facial sequence.

A modifier conversion alone does not remove the original library from a still-referenced source asset. Keep lean production slots if source loading and residency are the bottleneck. Retain the authoring originals for future edits.

For character-creator sliders, group changes and rebuild after the batch or throttle previews. Avoid regenerating a crowded scene for every pointer movement. For frame-by-frame expressions, prefer loaded blendshapes, bones, or supported runtime material effects. [Dynamic Expression Player](DynamicExpressionPlayer.md) applies live blendshape weights without a mesh rebuild; MeshModifier, shared-color, and UV effects use build-dependent paths. See [Mesh Modifiers](MeshModifiers.md) for the full authoring workflow.

## Bake a finished character while preserving animation shapes

Per-avatar baking adds a shape's weighted deltas into the generated base geometry and omits its live frames. It is useful after character creation, when identity/body customization is fixed but expressions still need to animate.

Maintain three explicit sets:

| Set | Example | Build policy |
| --- | --- | --- |
| Fixed appearance | Selected nose and body shape | Bake at the chosen weight. |
| Live animation | Blink, smile, required visemes | Keep unbaked, even when the current weight is zero. |
| Unused source shapes | Other identity options | Prefer not to load their source bake; otherwise bake at zero to omit their live frames. |

UMA's `SetBlendShape` accepts a **normalized weight**: `1.0` corresponds to Unity's weight `100`. If reading `SkinnedMeshRenderer.GetBlendShapeWeight`, divide by 100 before passing it to UMA. `SetBlendShapeData` only sets the bake policy; it does not capture the renderer's current weight.

The following helper applies a policy to an already initialized avatar. Its caller supplies every available source shape name—including additional bakes—and the selected normalized weights. Keep `avatar.loadBlendShapes = true` when using this mixed live/baked workflow. Disabling loading is not a portable “bake all” switch across combiners.

```csharp
using System.Collections.Generic;
using UMA;

public static class CharacterShapePolicy
{
    public static void Apply(UMAData data, IEnumerable<string> sourceNames,
        IDictionary<string, float> fixedWeights, ISet<string> liveNames)
    {
        foreach (string name in sourceNames)
        {
            bool live = liveNames.Contains(name);
            data.SetBlendShapeData(name, bake: !live, rebuild: false);
            if (!live)
            {
                float weight = fixedWeights.TryGetValue(name, out float value) ? value : 0f;
                data.SetBlendShape(name, weight, allowRebuild: false);
            }
        }
        data.Dirty(dnaDirty: false, textureDirty: false, meshDirty: true);
    }
}
```

Call this once after the authoring avatar is ready, or apply the policy before a queued build without requesting another build from inside the build callback. The helper leaves current live weights alone. It does not load assets, discover unavailable bakes, or serialize a policy into an avatar save file; persist the chosen bakes, fixed values, and live set in your game's character data and reapply them after loading.

Do not rely on current renderer indices as permanent IDs. UMA replaces meshes during regeneration, and shapes can disappear or change index. Store names, then resolve indices on the new renderer. Inspect all renderers, not only renderer 0, when capturing weights or auditing shape counts. If the same named shape has different weights on different renderers, decide the intended shared value before baking.

The new DNA Blendshape effect writes live renderer weights in its post-apply step; that is not a substitute for recording a bake value. Some legacy DNA conversion paths update baked values during generation. Establish which system owns each weight, stop competing animation/DNA writers when capturing a finished appearance, and verify the rebuilt result.

### Returning to an editable character

There is no inverse “unbake” operation on the finished mesh. Rebuild from the original source slots/bakes with the required names marked unbaked using `SetBlendShapeData(name, false, false)`, then reapply the saved editable values after generation. For a race-prebaked shape, an avatar-level unbaked flag cannot restore frames already removed from the generated race slot; use an appropriate source/race configuration.

Baking saves output frame storage and application work. It does not inherently unload the original slot assets, eliminate every preparation cost, or make future rebakes free. For frequently spawned fixed identities, compare lean preauthored slots, race baking, MeshModifiers, and resource reuse instead of assuming per-avatar baking solves every stage.

## Generate a race from blendshapes and bone poses

Race prebaking is for a form shared by all members of that race, such as a fixed body base or species silhouette. It differs from baking an individual character's nose/body choices on every build. UMA can create derived slot geometry and register it for reuse by subsequent builds of that race.

Use **Assets > UMA > Duplicate Race** for an existing race, or [Model to Race](ModelToRace.md) for a new source model. Give the race a unique runtime name and review its base recipe, T-pose, DNA, wardrobe compatibility, and expressions. A derived T-pose/Bone Pose setup supplies skeleton proportions; blendshape prebaking supplies the mesh correction. Baking a morph alone does not move joints.

In the RaceData inspector, expand **Race Generation**:

1. Add the fixed shape names and normalized values to **Prebaked Blendshapes**.
2. Add every shape that must remain adjustable to **Unbaked Shapes To Include**. This includes animation shapes that happen to be at zero.
3. Generate the race and inspect each output slot/renderer. Exercise the retained DNA and expressions with wardrobe equipped.
4. During authoring, **Force Rebuild Race Slots** can refresh derived geometry after changing bake/include settings. Turn it **off after testing** to allow reuse. It regenerates race slots each build when left enabled.
5. Run the [Race Smoke Test](ReleaseAssetValidation.md) and a target-platform build. Cross-compatibility metadata does not generate fitting corrections.

### Inclusion rules in this checkout

The include list is used when a slot is actually derived by race prebaking. It is not a universal filter for every slot entering the avatar. A slot with no matching requested bake shape is returned unchanged. With no prebake targets, this recipe path does not derive/filter new slots at all.

For a slot that is baked, an empty race include list retains **no** unbaked shapes. A nonempty list enables copying and uses case-sensitive .NET regular expressions:

| Entry | Meaning |
| --- | --- |
| `^ChubbyFace$` | Exactly this shape name. |
| `^(ChubbyFace|GauntFace)$` | Exactly these two names. |
| `^Eye.*` | Names beginning with `Eye`. |
| `.*` | All remaining unbaked shapes; useful for diagnosis, usually too broad for shipping. |

Anchor exact names and escape regular-expression punctuation in names when necessary. A plain name can also match a longer name containing it. **Use `.*`, not the older Discord suggestion `*`.** A bare `*` is invalid regex; the current runtime falls back to treating invalid patterns literally. Shapes listed for baking are removed before the include test, so listing a baked name here does not preserve another live copy of it.

Do not use a dummy bake entry as a blanket optimization recipe. Only slots containing a requested name enter the derived-slot path; verify coverage per slot. For optional libraries, selective source bakes and lean authored slots are more explicit.

### Human Male 3.0: the installed example

The sample asset `Assets/UMA/UMA3/Races/HumanMale30.asset` was inspected through Unity's asset API. In this checkout it contains:

| Setting | Installed value |
| --- | --- |
| Runtime race name | `Human Male 3.0` |
| Base recipe | `BaseRaceRecipe_HumanMale30 SSS.asset` |
| T-pose | `Assets/UMA/UMA3/TPose/MalePose_TPose.asset` |
| Prebaked Blendshapes | `MaleBody` = `1` |
| Unbaked Shapes To Include | `ChubbyFace`, `GauntFace` |
| Use New DNA | Enabled |
| Force Rebuild Race Slots | **Enabled in this checkout; disable in your production configuration after validation.** |

This demonstrates using the shared UMA 3 body source to create a male base with `MaleBody` baked into the relevant slots, while retaining selected facial customization shapes. The bake value is normalized 1.0, not 100. Its retention policy is intentionally much smaller than the entire source library.

This list does not promise that every renderer has exactly two shapes: only affected derived slots use that list, and other slots or bakes can contribute shapes. If your expression system needs additional live names, explicitly retain them and check the output. The sample is content you can study, not a reason to keep its testing override enabled or copy its shape list blindly.

## Developer controls and current limitations

Several controls sound similar but act at different stages:

| Control | What it actually changes |
| --- | --- |
| DCA **Load Blend Shapes** / `loadBlendShapes` | Enables normal blendshape processing. Keep enabled for a mixed live/baked character. |
| DCA **Load All Frames** / `loadAllFrames` | Keeps all frames; disabling it retains only the last live frame. This changes multi-frame interpolation, not just memory. |
| DCA **Load Blendshape Normals / Tangents** | Controls optional live delta channels. Test lighting before discarding them; omitted deltas are not automatically artist-authored replacements. |
| Additional Blendshape Slots | Selects extra source data for specific target slots through the recipe. |
| Race prebake/include lists | Produces filtered derived slots where a requested bake target exists. |
| `UMAData.SetBlendShapeData` and `SetBlendShape` | Per-avatar bake policy and normalized values; bake at zero to omit an unwanted live frame. |
| Disabled DNA control / zero weight | Stops or neutralizes a driver; does not itself remove source or output frames. |

**Do not rely on the legacy-looking filtering fields as a working allow-list in this version.** `loadOnlyUsedBlendshapes` is present but its inspector field is commented out and current build code does not consume it. The Avatar Definition optimization path populates `filteredBlendshapes` (including `forceKeepBlendshapes`), but the current mesh-combining paths do not consult that set to filter output. Use the source-selection, race-baking, and explicit bake policies above, and confirm actual renderer contents. This is a current implementation limitation, not a recommended design for filtering.

A complete retention policy must include required zero-weight expressions, correctives, and externally animated shapes. Inferring “needed” from nonzero DNA alone would be insufficient even with a functioning DNA-derived filter. DNA 0.5 is also not a universal proof of zero deformation: effect curves define the mapping.

Generator quality profiles can override blendshape loading, frames, and channels. Audit [Generator Quality](GeneratorQuality.md) when a platform build differs from the editor. Check LOD-specific settings, optional expression disabling, and the generated shape names at every relevant LOD. Reduced output shapes do not guarantee lower source residency if all source bakes remain loaded.

## Smooth generation after reducing the content

Use the [Jobified or Incremental Mesh Combiner](MeshCombiners.md) appropriate to the project. The [Incremental Mesh Combiner](IncrementalMeshCombiner.md) spreads work over updates, prepares eligible work off-thread, and keeps the existing character available while replacement work proceeds. It still applies live frames through Unity mesh calls on the main thread. The incremental compiler is appropriate for builds during gameplay, where they happen only occasionally and you don't want cpu spikes.  You should use the jobified combiner for scenes that require instant feedback - for example, a character creator or designer will feel much smoother using the jobified combiner. Note that all combiners are significantly faster in a build than they are in the editor. 

**Max Multi-Step Work (ms)** is a soft budget. One indivisible API call can exceed it, and spreading work does not reduce the library's memory or total processing requirement. Compare worst-frame time as well as total time until the avatar is ready. An incremental combiner is useful after selective loading; it is not permission to include every shape.

Current [Slot Preparation](SlotPreparation.md) stores validated metadata and affected-vertex information, including sparse multi-frame baking support. It preserves tiny nonzero deltas and optional-channel behavior. Prepare/save production slots in the editor where practical to reduce first-use work. Custom code changing source arrays must call `meshData.InvalidateBuildData()` after its edit batch; rebuild affected avatars afterward.

For crowds, [Generated Resource Reuse](GeneratedResourceReuse.md) can share matching generated meshes while keeping renderer blendshape weights independent. Different baked geometry can reduce sharing opportunities. Measure whether a few retained identity weights on a shared mesh or distinct baked meshes are better for a particular crowd; the answer depends on variation, memory, and active deformation costs. Never mutate a shared generated mesh directly to remove or add shapes.

## Measure a representative production workload

Build a small benchmark before committing to a large library. Use the actual project/version, target hardware, renderer grouping, LODs, garments, shape frames, and animation requirements. Old Discord timings are examples of particular content, not promised speedups or frame budgets.

Compare at least:

1. All source shapes live, as a diagnostic baseline.
2. Selected bakes and only the required live shapes.
3. The same appearance with fixed customization baked and unused shapes omitted.
4. The same appearance using MeshModifiers for suitable fixed changes.
5. A populated scene with the intended reuse and incremental-generation settings.

Record cold source loading, warm builds, worst main-thread frame, total build latency, peak/steady memory, generated shape/frame counts per renderer, and animation/rendering cost. Test wardrobe changes and repeated rebuilds as well as initial creation. Batch character-creator edits, stagger crowd spawning, and measure the target player build rather than extrapolating from editor timings alone.

Use UMA's generator/incremental statistics and resource usage monitor alongside the Unity Profiler. A lower generated mesh count does not prove lower total memory; source assets, temporary deltas, caches, and pending builds can still dominate. Confirm appearance and animation parity before accepting an optimization.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Hundreds of zero-weight shapes remain | Zero weight is not exclusion. Inspect source bakes, actual race bake coverage, and explicit per-avatar bake-at-zero policy. Do not assume a DNA toggle or `filteredBlendshapes` removed them. |
| A baked character resets or changes shape | Capture the actual weight, convert Unity 0–100 to UMA normalized units, set the bake value as well as its flag, and check competing DNA/animation writers. |
| Live shapes disappear after race generation | Add required names to Unbaked Shapes To Include, check regex matching, regenerate derived slots, and keep DCA loading enabled. A baked name cannot also be retained by the include list. |
| A bake produces scrambled vertices | Check vertex order, neutral basis, export splits, target slot, and LOD—not only vertex count. |
| A bake adds a second visible body | It was included as ordinary geometry. Attach it through Additional Blendshape Slots or set its target on the recipe's source SlotData. |
| A bake works until the next rebuild | Save it in the selected recipe or reapply it in the build hook; editing a finished mesh/recipe once is not persistent DCA configuration. |
| Body changes but clothing does not | The garment needs its own matching shape/modifier or corrected skinning. Names synchronize existing data; they do not create it. |
| Generation remains slow after baking | Check loaded source-library size, repeated race-slot rebuilding, preparation, textures, frame application, and build frequency. Profile stages separately. |
| Shading changes after optimization | Check removed normal/tangent deltas, last-frame-only settings, extraction limitations, recalculation, and shape combinations. |
| Expressions work in authoring but fail in gameplay | Keep all required animation names even at zero; audit race, quality profile, LOD, source selection, and post-build binding. |
| A converted modifier doubles the deformation | Remove/disable the original Blendshape effect and omit the equivalent live/baked contribution. |
| A race bake change seems ignored | Check cached derived slots and the actual target slot. Temporarily force rebuilding during authoring, verify, then disable that setting again. |

## Implementation references

For custom integrations, these source locations define the behavior described here:

- `Core/Editor/Scripts/Editors/SlotEditor.cs`: Additional Blendshape Slots authoring and validation.
- `Core/StandardAssets/UMA/Scripts/UMAGeneratorPro.cs`: collecting additional sources without rendering their geometry.
- `Core/StandardAssets/UMA/Scripts/SlotDataAsset.cs` (`BakeNewSlotData`) and `Core/Scripts/UMAPackedRecipeBase.cs`: derived race slots, include matching, and caching.
- `Core/StandardAssets/UMA/Scripts/UMAData.cs`: normalized weights and per-avatar bake policy.
- `Core/DynamicCharacterSystem/Scripts/DynamicCharacterAvatar.cs`: avatar settings and the current filtering limitation.
- `Core/Scripts/NewDNA/DNAEffect_BlendShape.cs`: live DNA weight application.
- `Core/Editor/Scripts/MeshModifierEditor.cs` (`ExtractBlendshapes`): extraction behavior.
- `Core/StandardAssets/UMA/Scripts/SkinnedMeshCombiner.cs`, `SkinnedMeshCombinerMeshAPI.cs`, and `SkinnedMeshCombinerIncrementalPreparation.cs`: combination and baking paths.

These paths are relative to the installed UMA root. See [RaceData](RaceData.md), [Race Utilities](RaceUtilities.md), [SlotDataAsset](SlotDataAsset.md), and [New DNA System](NewDNASystem.md) for the surrounding authoring and runtime workflows.
