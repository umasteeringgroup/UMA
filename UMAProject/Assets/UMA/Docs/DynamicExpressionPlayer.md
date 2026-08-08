# Dynamic DNA Expression Player

`DynamicExpressionPlayer` drives expressions with ordinary UMA `DNA` assets.
An expression can combine bones, blendshapes, runtime shader properties, shared
colors, UV changes, and mesh modifiers. Values are transient per avatar and are
not written into the avatar's persistent body DNA recipe.

Unity 6.3 or newer is required.

## Setup

1. Create an `UMAExpressionGroup` with
   `Assets > Create > UMA > Expression Group`.
2. Add one definition per expression. Dragging DNA assets into the group
   inspector creates stable IDs automatically.
3. Assign the group to `RaceData.expressionGroup`.
4. Add `DynamicExpressionPlayer` to the same GameObject as the
   `DynamicCharacterAvatar`.

The player resolves its group in this order:

1. `DynamicExpressionPlayer.expressionGroupOverride`
2. The active race's `RaceData.expressionGroup`
3. A transient DNA conversion of the active race's legacy `expressionSet`
4. The player's legacy inline `Expressions` list

If both `RaceData.expressionGroup` and the old `expressionSet` are assigned, the
new expression group wins. The transient fallback is per player, is never
serialized, and never modifies the source expression set.

## Definitions and neutral values

The definition ID is case-insensitive and is the stable API, animation, save,
network, and migration identity. Display names are UI-only.

Inputs are normalized to `0..1`. `DNA.defaultValue` is neutral. Curves on each
effect determine what happens below and above neutral.

Converted legacy pose pairs use `0.5` as neutral:

- Legacy `-1` maps to DNA `0` and applies the inverse pose at full weight.
- Legacy `0` maps to DNA `0.5` and applies neither pose.
- Legacy `1` maps to DNA `1` and applies the primary pose at full weight.

Blink roles use the DNA default as open and the definition's
`blinkClosedValue` as fully closed. The default closed value is `0`, matching
converted legacy `Open_Close` channels; set it to `1` for DNA authored in the
opposite direction.

## Runtime API

```csharp
player.SetExpression("smile", 0.8f);
player.SetExpression("mouth_aa", 1f, ExpressionSource.Animation);

if (player.TryGetExpression("smile", out float smile))
{
    // replicate or save smile
}

player.ResetExpression("smile", ExpressionSource.Manual);
player.ResetAllExpressions(ExpressionSource.Animation);
```

Use `TryGetExpressionIndex` once and the indexed overloads for allocation-free
animation or networking code. `GetExpressionIds` and `GetValuesSnapshot`
provide explicit snapshots for tools, save data, and replication.

Use `BeginExpressionBatch`/`EndExpressionBatch` when changing multiple channels.
The batch resolves values and build dirtiness once.

`SetProceduralBlinkAmount` and `SetProceduralGazeDirection` expose the same
role-based producers used by the built-in timing/gaze logic. Custom player loops
can use `RestoreRigExpressionsNow` before animation and
`ApplyRigExpressionsAfterAnimationNow` afterward.

Sources are independent. Override mode resolves them in this order:

1. Manual/gameplay
2. Animation/Timeline
3. Procedural blink
4. Procedural gaze

Definitions may instead use additive or maximum source blending.

## Effect scheduling

`DNABuildType` says what becomes dirty. `ExpressionEffectPhase` says when an
effect is safe to execute.

| Lane | Effects | Rebuild |
|---|---|---|
| Early restore / late rig | Bone pose, rotate, translate, scale, transform | No |
| Immediate blendshape | Blendshape | No |
| Runtime material | `DNAEffect_RuntimeMaterialProperty` | No |
| Build after recipe | Shared colors, color channels/properties, overlay UVs, mesh modifiers | Yes |
| Build pre/apply/post | Future effects that explicitly declare those phases | Yes |

Rig bones are restored once before Mecanim and every active rig expression is
layered in deterministic priority/ID order during `LateUpdate`. The per-bone
eyes, jaw, neck, head, and hands switches determine whether expressions may
override Mecanim. Generic rigs use `genericBoneJoints` for classification.

Build effects are frozen into a transient snapshot. The player debounces
requests, merges dirty flags, rate-limits mesh changes, prevents duplicate
in-flight builds, and schedules one follow-up if a value changes during a
build. Expression values never enter recipe serialization.

## Runtime material properties

Use `DNAEffect_RuntimeMaterialProperty` for animated blush, wrinkle strength,
wrinkle textures, and similar shader controls. It writes through
`MaterialPropertyBlock` without clearing unrelated properties.

It supports float, color, vector, and texture parameters. A renderer name,
material index, and/or UMA shared-color name can restrict the target.

Use the older `DNAEffect_SharedColorProperty` when the property must participate
in UMA recipe/atlas generation. That is a build effect and should not be
animated every frame.

`Assets > Create > UMA > Expression Runtime Material Examples` creates a small
group with wrinkle-float and cheek-tint DNA assets that can be retargeted to a
project's shader property names.

## Blink, saccades, and gaze

Procedural features discover channels through roles rather than names:

- `BlinkLeft` and `BlinkRight` may point to separate definitions or one
  bilateral definition.
- Eye direction may use shared `EyeHorizontal`/`EyeVertical` roles or the
  left/right variants.
- If DNA eye-direction roles are absent, humanoid Animator look-at remains
  available for gaze assistance.

Missing cameras, targets, humanoid bones, or Animators disable only the affected
procedural output. Other expression lanes continue to run.

## Migrating legacy expression sets

Select a `UMAExpressionSet` and run:

`Assets > UMA > Convert UMAExpressionSet to UMAExpressionGroup`

The command:

- keeps all 51 legacy property names as stable IDs;
- creates one DNA asset per channel;
- converts primary and inverse poses with exact neutral curves;
- maps blink, sided eye direction, emotion, and other roles;
- copies the legacy Mecanim joint classification;
- leaves the original expression set untouched.

To update RaceData assets in one operation, select one or more races in the
Project window and run:

Right-click the selection and choose:

`UMA > Update To Dynamic Expression System`

Choose one existing Expression Group for all selected races, or enable
`Create Expression group from current race expression set`. Creation converts
each unique legacy set and saves the new group and DNA assets in the legacy
set's folder. Clicking OK assigns the chosen or created group and removes the
old Expression Set reference from each race.

Add `DynamicExpressionLegacyAdapter` when existing animation clips still target
the 51 fields inherited from `ExpressionPlayer`. The adapter forwards them to
the Animation source and never applies the old pose player, so the old and new
rig lanes cannot run in parallel. Its `Values` surface and
`UMAExpressionEvent` continue to use legacy signed `-1..1` values.

## Validation and diagnostics

The group inspector reports duplicate/empty IDs, null or misconfigured effects,
unsupported phases, duplicate unique roles, ambiguous same-priority bone
overlap, out-of-range defaults, and continuously driven build effects.

The player inspector shows resolved channels, per-source values, effective
values, execution lanes, and pending build flags. Its preview sliders change
only the component's transient runtime state; shared group and DNA assets are
not modified.

## Performance guidance

- Prefer bone, blendshape, or runtime material effects for animation.
- Reserve shared-color, texture, UV, and mesh effects for discrete/slow changes.
- Cache expression indices in high-frequency callers.
- Use batches for phoneme, emotion, or network frame updates.
- Do not put mesh modifiers on blink, gaze, or viseme roles.
