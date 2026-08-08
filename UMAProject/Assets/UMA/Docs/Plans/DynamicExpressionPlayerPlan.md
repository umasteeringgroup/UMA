# Robust DNA Expression Player Plan

> Implementation status: completed for Unity 6.3+. See
> `DynamicExpressionPlayer.md` for authoring, runtime APIs, migration, effect
> scheduling, and performance guidance. This document remains the architectural
> rationale and acceptance checklist.

## Implementation Verification

The completed implementation includes the race-linked expression group,
per-avatar source/value model, deterministic rig scheduler, cached blendshape
and material lanes, transient build provider, procedural roles, legacy
conversion/fallback, compatibility facade, validation, inspectors, examples,
profiling markers, and dependency discovery described below.

Automated verification on Unity 6.3 covers:

- 22 edit-mode tests for stable IDs, source priority/blending/batching,
  smoothing, independent avatar state, race rebinding (including recovery from
  an empty nonserialized `RaceSetter` cache), rig overlap and joint policy, all
  built-in phase declarations, complete transient build routing, dirty-flag
  aggregation and in-flight coalescing, role-based blink/gaze, blendshape and
  float/color/vector/texture property application, renderer replacement,
  validation, no-change indexed allocations, asset conversion round-trip,
  automatic legacy fallback, compatibility events and inspector selection, and
  exact primary/inverse pose application.
- 3 editor workflow tests for assigning an existing group, converting a
  race's saved legacy set beside its source asset, clearing legacy references,
  multi-race updates, and invalid conversion input.
- 4 play-mode tests for automatic race changes, disable/re-enable restoration,
  operation without an Animator, actual Animator Playable evaluation followed
  by protected/overridden `LateUpdate` rig behavior, and generated-humanoid
  saccade fallback/DNA eye-role ownership.

## Decision

Add an expression-specific group asset to `RaceData`, and make DNA the canonical
definition of every expression.

The robust design is a phased DNA player, not a permanent mixture of the old 51
pose pairs and DNA:

- An `UMAExpressionGroup` on the race defines the expressions available to that
  race and the roles of special expressions such as left blink and right blink.
- `DynamicExpressionPlayer` owns the transient values for those expressions.
- Rig effects are restored before Mecanim and applied after Mecanim in
  `LateUpdate`.
- Blendshapes and renderer properties use immediate runtime paths where possible.
- Recipe, texture, UV, and mesh effects are supplied to the normal UMA generation
  phases through a transient expression-DNA collection, with rebuild requests
  coalesced.
- Blinking, saccades, gaze, animation, Timeline, and gameplay code are value
  producers. They write to expression channels but do not define a separate
  expression format.

This retains the feature that made `UMAExpressionPlayer` reliable with animated
bones while allowing an expression to do much more than move bones.

## Goals

1. Remove the fixed requirement for 51 signed pose-pair channels.
2. Let any `DNA` asset define an expression using any supported DNA effects.
3. Support compound expressions. One smile DNA can move bones, set a blendshape,
   tint the cheeks, and raise a wrinkle shader property.
4. Preserve intentional expression overrides of Mecanim-controlled facial bones.
5. Keep blink, saccade, and gaze behavior, without hard-coded expression names.
6. Survive avatar generation, regeneration, race changes, renderer replacement,
   and component initialization in any normal order.
7. Avoid rebuilding the UMA on every animation frame.
8. Provide a migration and compatibility path for existing expression sets,
   animation clips, and scripts.
9. Keep all runtime state per avatar. Shared expression assets must never be
   modified by a player.

## Current Implementation Findings

The current `DynamicExpressionPlayer` is a useful prototype but is not a safe
foundation without restructuring.

### Effects that are silently missed

The player manually calls `PreApply`, `Apply`, and `PostApply`. Several important
effects do their work only in `AfterRecipeGenerated`, so they currently do
nothing when driven by the player:

| Effect | Current execution point | Expression requirement |
|---|---|---|
| Bone pose/rotate/scale/translate/transform | `Apply` | Post-Mecanim frame lane |
| Blendshape | `PostApply` | Immediate renderer lane and reapply after rebuild |
| Shared color | `AfterRecipeGenerated` | Build lane, or a new immediate shader lane |
| Shared color channel | `AfterRecipeGenerated` | Build lane |
| Shared color property | `AfterRecipeGenerated` | Build lane today; preferably an immediate shader lane |
| Overlay UV transform | `AfterRecipeGenerated` | Build lane |
| Mesh modifier | `AfterRecipeGenerated` | Mesh build lane |

`DNABuildType` describes what an effect changes, but it does not describe which
execution hook must be called. Inferring execution scheduling only from
`AreaEffect` is therefore not sufficient.

### Bone expressions do not layer correctly

The current player restores and applies only expressions whose value changed.
When two active expressions touch the same bone, restoring one expression can
erase the contribution of the other. An unchanged expression is not reapplied.
The correct behavior is to restore the union of expression-controlled bones once
and apply every active rig expression in a deterministic order every processed
frame.

### Mecanim can overwrite the result

Expression rig effects currently run in `Update`, while `LateUpdate` is empty.
Mecanim can evaluate afterward and replace those bone transforms. The legacy
player avoids this by restoring expression bones before animation and layering
expressions after animation.

### Lifecycle and identity are fragile

- State is keyed by display name, so duplicate or renamed expressions conflict.
- Replacing a DNA reference while retaining the same value may not reapply it.
- Disabled effects are not consistently skipped by the manual effect loop.
- `MeshModifiers` is not included in the current player's dirty handling.
- The cached `UMAData` can remain null if the head was cached before avatar
  generation completed. Note: A DynamicCharacterAvatar is an UMAData. So you can
  reliably get the component owner and cast to an UMAData.
- The player does not subscribe to character build events.
- A humanoid Animator, a head bone, and camera-distance processing currently gate
  all expression work, including expressions that only affect materials.
- Blink expressions are found through two exact string constants.
- Procedural saccades affect Animator look-at jitter rather than expression DNA.

## Asset Model

### `UMAExpressionGroup`

Create a `ScriptableObject` specifically for expressions. Add an optional
reference to it on `RaceData` next to the legacy `expressionSet`.

Do not use a plain `DNAGroup` as the complete expression asset. `DNAGroup` is
appropriate for general DNA categorization and value limits, but it has no
expression identity, procedural roles, source blending, or animation-override
metadata.

Proposed shape:

```csharp
public sealed class UMAExpressionGroup : ScriptableObject
{
    public List<UMAExpressionDefinition> expressions;
}

[Serializable]
public sealed class UMAExpressionDefinition
{
    public string id;
    public string displayName;
    public DNA dna;
    public ExpressionRole roles;
    public ExpressionJoint affectedJoints;
    public int priority;
    public ExpressionBlendMode blendMode;
    public float responseTime;
}
```

The exact type names can change during implementation, but the responsibilities
should remain separate from `DNAGroup`.

Each definition needs:

- A stable, case-insensitive ID used by scripts and animation. Do not use the
  list index or display label as identity.
- A `DNA` reference. The DNA asset remains the complete effect definition.
- A display name for UI.
- Zero or more semantic roles, such as `BlinkLeft`, `BlinkRight`,
  `EyeHorizontal`, `EyeVertical`, `Viseme`, `Emotion`, or `Custom`.
- A deterministic priority/order for overlapping effects.
- Optional input smoothing and blend behavior.
- Joint metadata for Mecanim policy and editor validation.
- Cached or calculated build capabilities from the DNA effects.

Use `DNA.defaultValue` as the neutral value unless the expression definition
later has a demonstrated need for an override. Expression input remains
normalized to `0..1`; effect curves and mappings define the useful range. This
also replaces a signed primary/inverse pose pair: one DNA can contain effects
whose curves act below and above its neutral value.

### Race ownership and overrides

Add:

```csharp
public UMAExpressionGroup expressionGroup;
```

to `RaceData`.

The player should resolve its group in this order:

1. Optional group override on `DynamicExpressionPlayer`.
2. The active race's `RaceData.expressionGroup`.
3. Optional legacy compatibility adapter if only `expressionSet` exists.

The override allows a specialized NPC or prefab to use a different expression
set without duplicating a race. Race changes must resolve a new group and retain
values only for stable IDs that exist in both groups.

Keep `RaceData.expressionSet` during migration. If both are present, the new
group wins and the inspector displays the choice explicitly.

### Validation

The group inspector and race validation should report:

- Null DNA references.
- Empty or duplicate stable IDs.
- Multiple entries claiming a role that must be unique.
- DNA effects that do not declare a supported expression execution phase.
- Missing referenced shared colors, blendshapes, mesh modifiers, or bones when
  the target race can be inspected.
- Build-lane effects configured for continuously animated inputs.
- Ambiguous priority for expressions that modify the same bones.
- A DNA default outside `0..1`.

Validation errors should prevent silent no-op behavior.

## Runtime Value Model

Create one runtime instance per expression definition. Do not put transient
facial expression values into the avatar's persistent body DNA collection and do
not mutate the group or DNA assets.

The player should provide:

```csharp
bool SetExpression(string id, float value,
    ExpressionSource source = ExpressionSource.Manual);
bool TryGetExpression(string id, out float value);
void ResetExpression(string id, ExpressionSource source);
void ResetAllExpressions(ExpressionSource source);
void BeginExpressionBatch();
void EndExpressionBatch();
```

Also expose:

- An allocation-free indexed API for animation systems.
- A names/IDs snapshot for tooling.
- A values snapshot for save/restore and network replication.
- An `ExpressionChanged` event containing ID, previous effective value, new
  effective value, and source.
- A group-changed/rebound event after race changes.

### Sources and combination

Keep independent values for at least:

1. Manual/gameplay API.
2. Animator, Timeline, or clip input.
3. Procedural blink.
4. Procedural saccade/gaze.

Calculate one effective value per expression. Source priority and combination
must be deterministic. The first implementation can use override-by-priority,
with optional additive or maximum blending per definition. Do not let the blink
timer directly overwrite a value owned by gameplay.

Smooth the effective value after source resolution. Batches should resolve
values and dirty flags once.

## Execution Architecture

DNA remains the single expression format, but the player evaluates it through
three lanes.

### 1. Frame lane: rig and blendshape

This lane is suitable for continuously animated values.

#### Rig scheduling

Use the same scheduling principle as the legacy expression player:

1. Before Mecanim evaluates, remove the previous expression contribution from
   the union of expression-controlled bones.
2. Let Mecanim produce the frame pose.
3. In `LateUpdate`, apply all active expression rig effects in group order and
   priority order.

Do not restore once per expression and do not apply only changed expressions.
Expressions can share bones, so all active rig effects must layer from one known
base every processed frame.

The implementation needs an effect contract that can enumerate affected bone
hashes. A bone-pose effect can provide all pose bones; individual transform
effects can provide their one target. Cache the union after binding and rebuild
it only when the group, race, skeleton, or DNA asset changes.

#### Mecanim joint policy

Retain player options equivalent to:

- Override eyes.
- Override jaw.
- Override neck.
- Override head.
- Override hands.

Apply the policy per affected bone, not by skipping an entire multi-bone DNA
expression when only one bone is protected. This likely requires either a
filtered `UMABonePose.ApplyPose` overload or an expression rig context that
rejects protected bone writes.

The order is important:

- A joint not overridden by expressions keeps Mecanim's result.
- A joint overridden by expressions starts from the intended restored/base
  value and receives every applicable expression contribution.
- Non-expression body animation must not be reset.

Generic rigs must be supported with configurable bone/joint classification; the
whole player must not depend on `Animator.avatar.isHuman`.

#### Blendshapes

Blendshape effects can update renderers immediately when effective values
change. Cache renderer and blendshape indices, invalidate them after every UMA
rebuild, and reapply all active values after `CharacterUpdated`.

### 2. Immediate renderer-property lane

Fast cheek color and wrinkle intensity should not require texture regeneration
when the shader can express the change through `MaterialPropertyBlock`.

Add a runtime-capable effect or expression-effect adapter that:

- Resolves the renderers/material indices associated with a shared color.
- Applies float, color, vector, or texture properties through renderer property
  blocks.
- Composes with UMA's existing material-property-block data instead of clearing
  unrelated properties.
- Rebinds and reapplies after renderer regeneration.
- Reports that it is safe for continuous frame updates.

The current `DNAEffect_SharedColorProperty` modifies recipe/shared-color data in
`AfterRecipeGenerated`; that is not by itself an immediate renderer operation.
It can retain its build behavior, but the expression system needs a distinct
runtime contract for high-frequency shader controls.

Recommended authoring:

- Wrinkle-map alpha/intensity: immediate float property.
- Blush strength or cheek tint: immediate color/float property where the shader
  supports it.
- A change that alters the composed atlas pixels: build lane.

### 3. UMA build lane

Use this lane for:

- Shared colors that participate in texture generation.
- Shared-color channels.
- Overlay UV transforms.
- Texture or atlas changes.
- Mesh modifiers and other mesh regeneration.
- Any future effect whose result exists only during recipe/generator processing.

The player should publish a transient expression DNA snapshot to the UMA build,
rather than manually invoking only part of a DNA's lifecycle. The normal build
must invoke expression DNA at the same stages as regular DNA:

1. `AfterRecipeGenerated`.
2. `PreApply`.
3. `Apply`.
4. `PostApply`.

Two implementation options should be prototyped:

- Add an explicit runtime-DNA-provider collection to
  `DynamicCharacterAvatar`/`UMAData`, with the expression player as a provider.
- Build a transient combined `DNAInstanceCollection` for generation while
  keeping expression values excluded from recipe serialization.

The provider approach is preferred because ownership and persistence remain
clear. It also permits future runtime systems other than expressions.

Build requests must be aggregated:

- OR dirty flags across all values changed in a frame or API batch.
- Permit only one pending rebuild.
- Debounce changes from sliders or animation.
- Do not request another build while one is in flight; merge it into a pending
  follow-up only if values changed during that build.
- Optionally quantize or apply hysteresis to expensive effects.
- Rate-limit mesh modifiers and other mesh-build expressions.

Mesh modifiers are inherently unsuitable for per-frame facial animation. They
remain useful for discrete or slow expression state changes, but animated
wrinkles and blush should prefer blendshapes and runtime shader properties.

## Explicit Effect Capabilities

Extend the DNA effect contract so scheduling is explicit. `DNABuildType` should
continue to describe dirtiness, while a separate capability describes execution.

One possible form is:

```csharp
[Flags]
public enum ExpressionEffectPhase
{
    None = 0,
    EarlyRestore = 1 << 0,
    LateRig = 1 << 1,
    LateBlendShape = 1 << 2,
    RuntimeMaterial = 1 << 3,
    BuildAfterRecipe = 1 << 4,
    BuildPreApply = 1 << 5,
    BuildApply = 1 << 6,
    BuildPostApply = 1 << 7
}
```

Interfaces could be used instead if they make effect implementations clearer:

- `IRuntimeExpressionEffect`
- `IExpressionRigEffect`
- `IExpressionBoneCollector`
- `IBuildExpressionEffect`

Existing effects can be adapted without breaking their current DNA build hooks.
An unsupported effect must produce a validation error instead of being called in
an arbitrary phase.

While doing this work, include `MeshModifiers` in
`DNAInstanceCollection.DNABuildType.All`; it is currently omitted.

## Procedural Blink, Saccade, and Gaze

Blinking, saccades, and gaze remain features of the player, but they become
producers of expression values.

### Blink

- Resolve left/right blink channels from `ExpressionRole`, not names.
- Keep timing, curve, interval, and enable controls.
- Write through the procedural source layer.
- Reset only the procedural source when a blink ends.
- Permit a shared bilateral blink expression or separate left/right expressions.

### Saccade and eye direction

Support two backends:

1. DNA eye-direction expressions identified by horizontal/vertical roles.
2. Animator look-at/eye bones when the group or avatar does not provide those
   DNA roles.

This permits stylized or non-humanoid races to animate eyes through blendshapes,
bones, or other DNA effects. The backend should be selected by capability, not
hard-coded race names.

### Gaze

Retain optional Animator IK look-at for body/head assistance. Gaze computation
can produce normalized eye-direction values for DNA and separately drive
Animator IK for head/body assistance. These are complementary outputs.

Missing camera, head, eye bones, target, or humanoid avatar should disable only
the affected procedural feature. Material, mesh, and other non-eye expressions
must continue to work.

## UMA Lifecycle

The player must subscribe and unsubscribe safely to:

- Avatar character-begun/build-begun events.
- `UMAData` character-updated events.
- Active race/group changes.

On build begin:

- Freeze a stable transient expression snapshot for that build.
- Invalidate renderer and skeleton bindings as appropriate.
- Avoid applying frame-lane effects to a skeleton being regenerated.

On character updated:

- Refresh `UMAData`, Animator, skeleton, head/eye, renderer, material, and
  blendshape caches.
- Rebuild affected-bone and joint-classification caches.
- Reapply every active immediate expression.
- Resume procedural processing.
- If a value changed during the completed build, schedule exactly one follow-up
  build with the merged dirty flags.

Initialization must be retryable. Caching one transform must never prevent the
player from discovering a newly created `UMAData` or renderer later.

When processing is disabled by distance or component state, define restoration
explicitly:

- Remove frame-lane rig contributions so bones are not left frozen.
- Preserve source values.
- Reapply current values when processing resumes.
- Do not abandon a required build-lane update.

## Editor and Authoring

Add:

- `Assets/Create/UMA/Expression Group`.
- A group inspector with drag-and-drop DNA assignment, stable ID generation,
  role selection, priority, effect summary, build-cost badge, and validation.
- A `RaceInspector` expression-group field alongside the legacy expression set.
- A `DynamicExpressionPlayer` inspector that previews the resolved race group
  and creates transient preview values without editing the shared asset.
- A diagnostic view showing each channel's source values, effective value,
  execution lanes, affected joints, and pending dirty flags.
- An editor preview path that uses the same scheduler as play mode wherever
  possible.

Continuous sliders for build-lane effects should display a cost warning and
debounce updates. The inspector must clearly distinguish a runtime shader
property from an atlas/texture rebuild.

## Migration and Compatibility

Create an editor conversion command:

`Convert UMAExpressionSet to UMAExpressionGroup`

For each old channel:

1. Preserve the old pose name as the stable ID.
2. Create or reuse a DNA asset.
3. Convert primary and inverse poses into DNA bone-pose effects with curves
   around a neutral value, or use two effects when needed for exact parity.
4. Map blink, eye-direction, and other known channels to semantic roles.
5. Copy the old Mecanim joint classification into the new definition.
6. Assign the generated group to the race only after validation succeeds.

Do not mutate or delete the old expression set automatically.

Provide a compatibility facade for a deprecation period:

- The legacy 51 property names forward to stable expression IDs.
- Legacy `Values` and expression-change events can be adapted.
- Existing animation clips can either bind through forwarding serialized fields
  or be converted by an editor tool.

The compatibility layer is an input adapter. It must not run the old bone-pose
player in parallel with the DNA rig lane.

## Implementation Phases

### Phase 0: Tests and prototype stabilization

- Add regression tests that expose current overlapping-bone, build-phase,
  lifecycle, and Mecanim overwrite failures.
- Confirm execution timing with both humanoid and generic Animators.
- Decide the runtime-DNA-provider hook through a small build-lane prototype.
- Document the neutral-value convention for converted pose pairs.

Exit criterion: a prototype changes one bone after Mecanim, one blendshape
without a build, one shared-color property through the intended path, and one
mesh modifier through exactly one build.

### Phase 1: Expression group and runtime values

- Implement `UMAExpressionGroup`, definition validation, and `RaceData` field.
- Implement stable-ID lookup and per-avatar runtime instances.
- Implement source layers, batching, smoothing, and change events.
- Update the race and player inspectors.

Exit criterion: group resolution and value APIs work across two races and two
avatars sharing the same group without shared-state leakage.

### Phase 2: Rig and blendshape frame lane

- Add affected-bone collection and deterministic ordering.
- Implement pre-Mecanim restore and post-Mecanim application.
- Implement per-bone Mecanim override policy.
- Add cached, rebuild-safe blendshape application.
- Remove Animator/head prerequisites from unrelated processing.

Exit criterion: overlapping expressions layer correctly, selected joints
override Mecanim, protected joints retain Mecanim, and values survive rebuilds.

### Phase 3: Build-lane integration

- Add the transient runtime-DNA provider/snapshot.
- Route every effect through its valid UMA lifecycle hook.
- Aggregate `DNABuildType` flags, including mesh modifiers.
- Add debounce, in-flight coalescing, and follow-up-build handling.

Exit criterion: shared colors, UV transforms, and mesh modifiers work without
persisting expression values into body DNA and without duplicate builds.

### Phase 4: Immediate material lane

- Add runtime material-property effect capability.
- Compose property blocks safely across UMA and expression writers.
- Cache targets and rebind after generation.
- Supply example cheek-tint and wrinkle-alpha DNA assets/scenes.

Exit criterion: animated cheek tint and wrinkle intensity run without an UMA
texture or mesh rebuild.

### Phase 5: Procedural behavior

- Convert blink to role-based source input.
- Add DNA-based saccade/eye direction with Animator fallback.
- Retain optional Animator IK head/body gaze assistance.
- Verify missing-feature fallbacks.

Exit criterion: blink and saccade work without fixed channel names on humanoid
and generic test races.

### Phase 6: Migration and release hardening

- Add the expression-set conversion tool and legacy input facade.
- Add race/index dependency discovery for expression groups and their DNA assets.
- Complete play-mode, editor, allocation, and performance tests.
- Add user documentation and upgrade notes.
- Deprecate, but do not yet remove, the old expression set/player.

## Test Matrix

### Unit tests

- Stable ID lookup is case-insensitive and rejects duplicates.
- Role uniqueness and null DNA validation.
- Source priority, blending, reset, smoothing, and batch behavior.
- Disabled effects are never executed.
- Phase routing for every built-in DNA effect.
- Dirty flags aggregate once and include `MeshModifiers`.
- Debounce and in-flight rebuild coalescing.
- Deterministic ordering for overlapping expressions.
- Mecanim filtering occurs per bone.

### Play-mode integration tests

- Player starts before and after DCA/UMA generation.
- Race changes replace the group and preserve matching stable IDs only.
- Active values reapply after renderer and skeleton replacement.
- Two expressions affect the same jaw/cheek bones.
- Animator head/jaw/eye motion is respected or overridden per setting.
- Blink uses roles with arbitrary channel names.
- Gaze works with and without humanoid eye bones.
- Non-eye expressions work with no camera, head, or Animator.
- Shared color/channel updates take the correct build path.
- Wrinkle and cheek runtime properties do not trigger generation.
- Mesh modifier changes schedule one mesh build.
- A value changed during generation produces at most one required follow-up.
- Multiple avatars using one group retain independent state.
- Disable, distance culling, re-enable, and object destruction leave no stale
  bone or event state.

### Performance requirements

- No managed allocations in the steady-state rig/blink/saccade frame loop.
- No UMA rebuild for rig, blendshape, or runtime material effects.
- No more than one build request per resolved expression batch.
- No repeated asset scans, string searches, or blendshape-name searches per
  frame.
- Profiling markers for source resolution, rig restore/apply, renderer updates,
  and expression-requested builds.

## Acceptance Criteria

The replacement is ready when:

1. An expression group assigned to a race is discovered automatically.
2. Expressions are addressed by stable IDs and are not limited to 51 channels.
3. One expression DNA can combine rig, blendshape, color, shader, and supported
   mesh effects.
4. Rig expressions reliably layer after Mecanim with per-joint override control.
5. Blink, saccade, and gaze operate as optional producers rather than a separate
   pose system.
6. Cheek tint and wrinkle strength can animate without rebuilding when authored
   as renderer properties.
7. Recipe/texture/mesh effects execute through the complete UMA build lifecycle
   and coalesce rebuilds.
8. Values survive ordinary UMA rebuilds and race transitions according to the
   documented ID policy.
9. Existing expression sets can be converted without destroying the source
   assets.
10. The automated test matrix passes on Unity 6.3 or newer.

## Recommended First Slice

Implement the smallest vertical slice before broad editor work:

1. A race-linked group with three entries: blink, smile, and wrinkle.
2. Blink DNA with a bone or blendshape effect.
3. Smile DNA with two overlapping bone effects to prove deterministic layering
   after Mecanim.
4. Wrinkle DNA with an immediate float shader property.
5. One mesh-modifier expression that proves build snapshotting and coalescing.

This slice tests every architectural boundary. If it succeeds, the remaining
work is expansion and migration rather than another redesign.
