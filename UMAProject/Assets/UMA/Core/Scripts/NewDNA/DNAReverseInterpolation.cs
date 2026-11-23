using System;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Reverse interpolation for DNA: finds the DNA value (0..1) that gives a wanted result.
    /// <para>
    /// The forward direction is DNA value -> curve -> min/max mapping -> effect. The curve can be any
    /// shape, so it is inverted numerically: the 0..1 range is sampled, every interval whose ends lie
    /// on either side of the target is refined by bisection, and the root closest to a preferred value
    /// is kept. A monotonic curve has one root; a curve that turns back (for example 0 -> 1 -> 0) can
    /// have several, and then the one nearest the preferred value (normally the current DNA value) wins.
    /// Results are always clamped to 0..1. When no value in 0..1 reaches the target, the value whose
    /// result comes closest is returned and <see cref="Result.Reachable"/> is false.
    /// </para>
    /// See <see cref="DNA.ReverseInterpolate(float, float)"/> for the measurement form
    /// ("the character is 2.0 m at the default, which value makes it 1.6 m?").
    /// </summary>
    public static class DNAReverseInterpolation
    {
        /// <summary>Samples taken over 0..1 to find the intervals that hold a root.</summary>
        public const int DefaultSamples = 64;

        private const int BisectionSteps = 40;

        /// <summary>
        /// The answer of a reverse interpolation.
        /// </summary>
        public struct Result
        {
            /// <summary>
            /// False when the function could not be evaluated at all (for example a DNA with no
            /// effect that scales). <see cref="Value"/> then holds the base value unchanged.
            /// </summary>
            public bool Solvable;

            /// <summary>
            /// True when some DNA value in 0..1 gives the target. False when the target is out of
            /// reach; <see cref="Value"/> is then the value that comes closest (usually 0 or 1).
            /// </summary>
            public bool Reachable;

            /// <summary>The DNA value to set, always in 0..1.</summary>
            public float Value;

            /// <summary>What the function gives at <see cref="Value"/>. Equals the target when reachable.</summary>
            public float Achieved;

            /// <summary>The smallest result found over 0..1 (from the samples and the roots found).</summary>
            public float Minimum;

            /// <summary>The largest result found over 0..1 (from the samples and the roots found).</summary>
            public float Maximum;
        }

        /// <summary>
        /// Finds the DNA value in 0..1 at which <paramref name="resultAtValue"/> equals <paramref name="target"/>.
        /// </summary>
        /// <param name="resultAtValue">The forward function: DNA value in, result out. Called about
        /// <paramref name="samples"/> + 40 times per root interval, so keep it cheap.</param>
        /// <param name="target">The result wanted.</param>
        /// <param name="preferredValue">When several values reach the target, the one nearest this is
        /// returned. Pass the current DNA value so the answer stays on the same side of a curve's turn.</param>
        /// <param name="samples">How many intervals 0..1 is split into to look for roots. A curve that
        /// crosses the target twice within one interval can be missed; raise this for very wavy curves.</param>
        public static Result Solve(Func<float, float> resultAtValue, float target, float preferredValue = 0.5f, int samples = DefaultSamples)
        {
            if (resultAtValue == null)
            {
                throw new ArgumentNullException(nameof(resultAtValue));
            }
            samples = Mathf.Max(1, samples);
            preferredValue = Mathf.Clamp01(preferredValue);

            float tolerance = 1e-5f * Mathf.Max(1f, Mathf.Abs(target));
            var result = new Result
            {
                Solvable = true,
                Minimum = float.PositiveInfinity,
                Maximum = float.NegativeInfinity,
            };

            bool found = false;
            float bestValue = preferredValue;
            float bestAchieved = 0f;
            float closestValue = 0f;
            float closestAchieved = 0f;
            float closestError = float.PositiveInfinity;

            void Consider(float value, float achieved, float acceptError)
            {
                result.Minimum = Mathf.Min(result.Minimum, achieved);
                result.Maximum = Mathf.Max(result.Maximum, achieved);
                float error = Mathf.Abs(achieved - target);
                if (error < closestError ||
                    (error == closestError && Mathf.Abs(value - preferredValue) < Mathf.Abs(closestValue - preferredValue)))
                {
                    closestError = error;
                    closestValue = value;
                    closestAchieved = achieved;
                }
                if (error <= acceptError &&
                    (!found || Mathf.Abs(value - preferredValue) < Mathf.Abs(bestValue - preferredValue)))
                {
                    found = true;
                    bestValue = value;
                    bestAchieved = achieved;
                }
            }

            // The preferred value itself wins when it already gives the target (flat curve segments).
            Consider(preferredValue, resultAtValue(preferredValue), tolerance);

            float previousValue = 0f;
            float previousAchieved = resultAtValue(0f);
            Consider(previousValue, previousAchieved, tolerance);
            for (int i = 1; i <= samples; i++)
            {
                float value = (float)i / samples;
                float achieved = resultAtValue(value);
                Consider(value, achieved, tolerance);

                float a = previousAchieved - target;
                float b = achieved - target;
                if ((a < 0f && b > 0f) || (a > 0f && b < 0f))
                {
                    // The result crosses the target in this interval. Where the curve is continuous the
                    // bisection ends agree, and the root counts even if float steps keep it off by a hair;
                    // at a jump (stepped keys) they do not, and the target is not reached there.
                    float root = Bisect(resultAtValue, target, previousValue, value, a < 0f, out float gap);
                    float accept = gap <= 1e-3f * Mathf.Max(1f, Mathf.Abs(target)) ? Mathf.Max(tolerance, gap) : tolerance;
                    Consider(root, resultAtValue(root), accept);
                }

                previousValue = value;
                previousAchieved = achieved;
            }

            if (found)
            {
                result.Reachable = true;
                result.Value = bestValue;
                result.Achieved = bestAchieved;
            }
            else
            {
                result.Reachable = false;
                result.Value = closestValue;
                result.Achieved = closestAchieved;
            }
            return result;
        }

        /// <summary>
        /// Finds the DNA value for a measurement that is proportional to a scale factor, such as a
        /// character's height when the DNA scales the root bone.
        /// The model is measurement(v) = baseMeasurement * factor(v) / factor(baseValue).
        /// </summary>
        /// <param name="factorAtValue">The scale factor the DNA applies at a value.</param>
        /// <param name="baseMeasurement">The measurement taken with the DNA at <paramref name="baseValue"/>.</param>
        /// <param name="baseValue">The DNA value in use when <paramref name="baseMeasurement"/> was taken.</param>
        /// <param name="targetMeasurement">The measurement wanted.</param>
        public static Result SolveProportional(Func<float, float> factorAtValue, float baseMeasurement, float baseValue, float targetMeasurement)
        {
            if (factorAtValue == null)
            {
                throw new ArgumentNullException(nameof(factorAtValue));
            }
            baseValue = Mathf.Clamp01(baseValue);
            float baseFactor = factorAtValue(baseValue);
            if (Mathf.Approximately(baseFactor, 0f) || Mathf.Approximately(baseMeasurement, 0f))
            {
                // Nothing to scale from: the measurement cannot be predicted.
                return new Result
                {
                    Solvable = false,
                    Value = baseValue,
                    Achieved = baseMeasurement,
                    Minimum = baseMeasurement,
                    Maximum = baseMeasurement,
                };
            }
            float perFactor = baseMeasurement / baseFactor;
            return Solve(v => perFactor * factorAtValue(v), targetMeasurement, baseValue);
        }

        /// <summary>
        /// Finds the DNA value that makes an effect's mapped value (curve, then min/max) equal
        /// <paramref name="mappedValue"/>.
        /// </summary>
        public static Result SolveMappedValue(DNAEffect effect, float mappedValue, float preferredValue = 0.5f)
        {
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }
            return Solve(effect.GetMappedValue, mappedValue, preferredValue);
        }

        // lowBelow: the result at low is below the target (so at high it is above).
        // gap: how far apart the results at the two final ends are.
        private static float Bisect(Func<float, float> resultAtValue, float target, float low, float high, bool lowBelow, out float gap)
        {
            for (int i = 0; i < BisectionSteps; i++)
            {
                float mid = 0.5f * (low + high);
                if (mid <= low || mid >= high)
                {
                    break;
                }
                bool midBelow = resultAtValue(mid) < target;
                if (midBelow == lowBelow)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }
            // Pick whichever end is nearer the target.
            float lowResult = resultAtValue(low);
            float highResult = resultAtValue(high);
            gap = Mathf.Abs(highResult - lowResult);
            float lowError = Mathf.Abs(lowResult - target);
            float highError = Mathf.Abs(highResult - target);
            return lowError <= highError ? low : high;
        }
    }
}
