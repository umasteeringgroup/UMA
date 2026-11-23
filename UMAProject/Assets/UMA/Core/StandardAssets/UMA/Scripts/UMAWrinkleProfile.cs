using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Maps facial expressions to expression wrinkle zone weights. Each rule adds to one zone in proportion to how far
    /// one expression has moved from its neutral value, with its own amount for each direction. A zone's weight is the
    /// sum of its rules, scaled by <see cref="strength"/> and clamped to -1 (stretched) .. 1 (compressed).
    /// Expressions are named as in the Expression Player (for example "leftBrowUp_Down"); a signed value of 1 is the
    /// first half of the name (brow up), -1 the second (brow down).
    /// </summary>
    [CreateAssetMenu(menuName = "UMA/Rendering/Expression Wrinkle Profile", fileName = "Expression Wrinkle Profile")]
    public class UMAWrinkleProfile : ScriptableObject
    {
        [Serializable]
        public struct Rule
        {
            [Tooltip("Expression name, as in the Expression Player.")]
            public string expression;
            public UMAWrinkleZone zone;
            [Tooltip("Zone weight added at the expression's full positive value (the first half of its name). Negative values stretch the zone.")]
            public float positive;
            [Tooltip("Zone weight added at the expression's full negative value (the second half of its name). Negative values stretch the zone.")]
            public float negative;

            public Rule(string expression, UMAWrinkleZone zone, float positive, float negative)
            {
                this.expression = expression;
                this.zone = zone;
                this.positive = positive;
                this.negative = negative;
            }

            /// <summary>This rule's contribution for a signed expression value in -1..1.</summary>
            public float Contribution(float signedValue)
            {
                return signedValue >= 0f ? signedValue * positive : -signedValue * negative;
            }
        }

        [Tooltip("Scales every zone weight before clamping.")]
        [Range(0f, 2f)]
        public float strength = 1f;

        public List<Rule> rules = new List<Rule>();

        /// <summary>
        /// Signed value of an expression: 0 at <paramref name="neutral"/>, 1 at the top of its range and -1 at the
        /// bottom. The Dynamic Expression Player keeps values in 0..1 around each expression's default.
        /// </summary>
        public static float Signed(float value, float neutral)
        {
            if (value >= neutral) return neutral < 1f ? Mathf.Clamp01((value - neutral) / (1f - neutral)) : 0f;
            return neutral > 0f ? Mathf.Max(-1f, (value - neutral) / neutral) : 0f;
        }

        /// <summary>
        /// Computes zone weights into <paramref name="weights"/> (index 0 is zone 1) from per-rule signed values
        /// (<paramref name="signedValues"/>[i] belongs to rules[i]).
        /// </summary>
        public void Evaluate(IReadOnlyList<float> signedValues, float[] weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            Array.Clear(weights, 0, weights.Length);
            if (rules != null && signedValues != null)
            {
                int count = Math.Min(rules.Count, signedValues.Count);
                for (int i = 0; i < count; i++)
                {
                    int zone = (int)rules[i].zone - 1;
                    if (zone < 0 || zone >= weights.Length) continue;
                    float value = signedValues[i];
                    if (value != 0f) weights[zone] += rules[i].Contribution(value);
                }
            }
            for (int i = 0; i < weights.Length; i++) weights[i] = Mathf.Clamp(weights[i] * strength, -1f, 1f);
        }

        /// <summary>Zone weights for signed expression values looked up by name (0 for names not given).</summary>
        public float[] Evaluate(IReadOnlyDictionary<string, float> signedValues)
        {
            var values = new float[rules != null ? rules.Count : 0];
            for (int i = 0; i < values.Length; i++)
            {
                float value;
                if (signedValues != null && rules[i].expression != null && signedValues.TryGetValue(rules[i].expression, out value))
                    values[i] = Mathf.Clamp(value, -1f, 1f);
            }
            var weights = new float[UMAWrinkleZones.ZoneCount];
            Evaluate(values, weights);
            return weights;
        }
    }
}
