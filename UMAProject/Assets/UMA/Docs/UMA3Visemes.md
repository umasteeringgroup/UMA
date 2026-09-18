# UMA 3 viseme poses

These bone-based speech shapes follow the 15-shape order in the [Meta viseme reference](https://developers.meta.com/horizon/documentation/unity/audio-ovrlipsync-viseme-reference/). The full and mild versions were authored against its animated reference and still-image grid. They use the UMA 3 jaw, lip and tongue bones, not blendshapes or an audio SDK dependency.

## Where they are

- **Full expression group:** `Assets/UMA/UMA3/Races/Expressions/DynamicExpressionSet/UMA 30 Expression Set_ExpressionGroup.asset`
- **Mild expression group:** the adjacent `UMA 30 Expression Set_ExpressionGroup_Mild.asset`
- **Poses and DNA:** `Assets/UMA/UMA3/Races/Expressions/Visemes/Full` and `Visemes/Mild`

For example, PP has `viseme_PP_pose` and `viseme_PP_DNA`; the mild assets are `viseme_PP_mild_pose` and `viseme_PP_mild_DNA`. Both groups use the expression ID **`viseme_PP`**. Mild assets do not require a different lip-sync mapping.

The existing full group's asset identity and non-viseme entries are retained. Both standard Human Male 3.0 and Human Female 3.0 races already reference it. The mild group initially copies those same non-viseme definitions and substitutes its own speech DNA.

| Meta index | Expression ID | Main articulation |
| --- | --- | --- |
| 0 | `viseme_sil` | Relaxed rest with very slight lip separation; no pursing |
| 1 | `viseme_PP` | Lip seal and light compression |
| 2 | `viseme_FF` | Lower lip raised and retracted toward upper incisors |
| 3 | `viseme_TH` | Tongue brought forward into the dental gap |
| 4 | `viseme_DD` | Tongue tip raised behind the upper teeth |
| 5 | `viseme_kk` | Tongue body raised and retracted |
| 6 | `viseme_CH` | Narrowed, projecting lips with teeth exposed |
| 7 | `viseme_SS` | Small dental gap and lightly spread lips |
| 8 | `viseme_nn` | Higher tongue-tip contact with a narrower mouth |
| 9 | `viseme_RR` | Lip rounding and tongue retraction/curl |
| 10 | `viseme_aa` | Broad jaw opening |
| 11 | `viseme_E` | Moderate opening and lip spread |
| 12 | `viseme_I` | Spread lips with a clear vowel opening |
| 13 | `viseme_O` | Rounded open mouth |
| 14 | `viseme_U` | Small, visibly open, forward-pursed vowel |

Mild is separately authored: articulation such as PP's seal is retained while excursions and jaw openings are reduced. It is not a uniform 50% version of the full set.

## Preview on your character

1. Generate a standard UMA 3 character and select its **Dynamic Expression Player** component.
2. Leave **Expression Group Override** empty to use the race's full group, or assign the `_Mild` group for subtler speech.
3. Click **Rebind Group and Avatar** if you changed the group.
4. In **Transient Preview**, raise one `viseme_…` slider from 0 to 1. Return it to 0 before trying another, or click **Reset Manual Preview**.

Keep **Override Mecanim Jaw** enabled for these jaw poses. Head and neck overrides are not required. The player explicitly recognizes UMA's lip/tongue bones so broad legacy head-pose metadata cannot accidentally suppress them. Explicit generic-bone mappings still take precedence.

The sliders preview expression DNA without baking it into the character's identity DNA. Do **not** add the same speech DNA to the race's body-DNA collection as well: that would mix transient speech with character construction.

## Driving the set

Values are **0 = no contribution, 1 = the authored target**. Existing legacy expression channels may have a 0.5 neutral, but these new viseme channels all have a 0 neutral.

The Meta index is not the expression-group index: the group also contains its existing expressions. Resolve the stable IDs instead. Feed a complete frame of weights, resetting unused speech channels to zero; setting `viseme_sil` to 1 alone does not clear a previously active vowel.

This example has no dependency on a particular lip-sync package. Call it with your provider's 15 weights, in the order above:

```csharp
using System;
using UMA;
using UnityEngine;

public static class SpeechWeights
{
    static readonly string[] Ids = {
        "viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH", "viseme_DD",
        "viseme_kk", "viseme_CH", "viseme_SS", "viseme_nn", "viseme_RR",
        "viseme_aa", "viseme_E", "viseme_I", "viseme_O", "viseme_U"
    };

    static float Weight(float value) => float.IsNaN(value) || float.IsInfinity(value)
        ? 0f : Mathf.Clamp01(value);

    public static void Apply(DynamicExpressionPlayer player, float[] weights)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (weights == null || weights.Length != Ids.Length)
            throw new ArgumentException("Expected 15 viseme weights.", nameof(weights));

        float sum = 0f;
        for (int i = 0; i < Ids.Length; i++) sum += Weight(weights[i]);
        float divisor = Mathf.Max(1f, sum);
        player.BeginExpressionBatch();
        try
        {
            for (int i = 0; i < Ids.Length; i++)
                player.SetExpression(Ids[i], Weight(weights[i]) / divisor);
        }
        finally { player.EndExpressionBatch(); }
    }
}
```

Do not drive every viseme at full strength simultaneously. Weighted poses layer in stable order; bounding the total weight avoids exaggerated accumulated jaw/lip displacement. Silence contributes a subtle relaxed-rest opening when weighted in. Leave separate jaw/mouth preview channels neutral while evaluating these shapes. Expressions such as smiles may be layered deliberately, but can change tooth/lip contact.

For a one-off synchronous editor/test evaluation, call `EvaluateExpressionsNow()` followed by `ApplyRigExpressionsNow()`. In normal play, the player's update/animation lanes perform application and restoration; do not add an extra manual pose application every frame.

To change strength sets, assign the alternate group to `expressionGroupOverride`, call `Rebind()`, and supply the next weight frame. Reference that group from a serialized field or asset used by your build; it is not loaded by name from Resources.

## Authoring and maintenance

The pose assets are editable normally. Jaw rotations are converted from the anatomical opening axis into the mandible's actual local rotation frame. Lip translations are converted through each bone's parent transform; tongue-tip rotation has the opposite bending sense to the jaw. No world-axis rotations are stored in the resulting assets.

**UMA → Expressions → Rebuild UMA 3 Viseme Assets…** regenerates the supplied presets and asks for confirmation. It replaces the generated viseme pose/DNA edits, preserves their GUIDs, and preserves non-viseme entries in each group. Back up custom viseme edits before rebuilding. Preset specifications are in `UMAVisemePoseBuilder.cs`, with translation distances in millimetres and rotations in degrees.

`sil` is an authored relaxed-rest pose, identical in the full and mild sets: a 0.75-degree jaw release with unpursed lips and no tongue gesture. It produces a very small visible lip parting on the standard avatars. Set `viseme_sil` to 1 and clear the other speech weights to use this rest shape. Setting every expression to 0 still restores the character's original neutral; the pose does not alter character DNA or force a permanent opening. Silence is deliberately different from PP's closed-lip seal.

## Validation and limitations

The set is checked on the standard female and male UMA 3 head/inner-mouth meshes with their race morphs and DNA, using front and three-quarter renders of both strengths. The review render explicitly CPU-skins each pose so successive batch images cannot reuse stale skinned-mesh state. Review sheets are in the adjacent `VisemeReview` folder.

`UMAVisemePoseTests` checks all 30 pose/DNA pairs, asset links, anatomical movement directions, full/mild group assignment, facial versus Mecanim joint protection, mixed weights, rotated avatars, and repeated application/reset without drift. The existing expression-player regression tests are run alongside it.

`UMAVisemeSurfaceTests` additionally skins the actual standard male/female face and inner-mouth meshes and depth-rasterizes them on the CPU. It measures exposed inner-mouth area, central opening and width for every full/mild viseme. This catches a sealed lip surface even when the bone pivots moved correctly. U must remain open while pursed; O and CH must narrow relative to spread shapes; PP must retain its seal. These tests reject the original closed-U presets on both avatars in both strengths. They run without a GPU or a particular render pipeline; visual reference review remains necessary for artistic quality.

These are anatomical approximations for the standard UMA 3 rig, not exact reproductions of the reference person's face or audio recognition. Character-specific facial DNA, teeth proportions, missing mouth slots, extreme weights, or a different rig can require adjustments. The intentionally simple review lighting makes mouth geometry easy to inspect; it is not a final skin-lighting preset. Real-time speech timing and recognition are supplied by your lip-sync provider.
