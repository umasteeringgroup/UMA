using System;
using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    [System.Serializable]
    public class DNA : ScriptableObject
    {
        [Tooltip("The description of the DNA, used to provide information about it.")]
        public string description;
        [Tooltip("DNA Default Value. Must be in the range of 0..1")]
        public float defaultValue = 0.5f; // Default value. Can be overriden in the inspector. Must be in the 0..1 range.
        [Tooltip("The list of DNA effects that this DNA applies. Each effect can modify the character in different ways.")]
        [SerializeReference]
        public List<DNAEffect> effects = new List<DNAEffect>();

        public string displayName;

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/DNA/DNA Item")]
        public static void CreateDNA()
        {
            UMA.CustomAssetUtility.CreateAsset<DNA>();
        }
#endif

        public string GetDisplayName()
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return name;
            }
            return displayName;
        }

        /// <summary>
        /// Reverse interpolation: the DNA value (0..1) that turns <paramref name="baseMeasurement"/>,
        /// taken with this DNA at its <see cref="defaultValue"/>, into <paramref name="projectedMeasurement"/>.
        /// See <see cref="SolveReverseInterpolation"/> for the assumptions, and to learn whether the
        /// target was in reach (this returns the closest value, 0 or 1, when it is not).
        /// </summary>
        /// <example>
        /// A character is 2.0 m tall with height at its default of 0.5. To make it 1.6 m:
        /// <code>float value = heightDna.ReverseInterpolate(2.0f, 1.6f);</code>
        /// </example>
        /// <param name="baseMeasurement">The measurement now (height, width, ...), any unit.</param>
        /// <param name="projectedMeasurement">The measurement wanted, same unit.</param>
        public float ReverseInterpolate(float baseMeasurement, float projectedMeasurement)
        {
            return SolveReverseInterpolation(baseMeasurement, projectedMeasurement, defaultValue).Value;
        }

        /// <summary>
        /// Reverse interpolation from a measurement taken with this DNA at <paramref name="baseValue"/>
        /// (for example the character's current value rather than the default).
        /// See <see cref="ReverseInterpolate(float, float)"/>.
        /// </summary>
        public float ReverseInterpolate(float baseMeasurement, float projectedMeasurement, float baseValue)
        {
            return SolveReverseInterpolation(baseMeasurement, projectedMeasurement, baseValue).Value;
        }

        /// <summary>
        /// Reverse interpolation with the full answer: the value, whether the target is in reach,
        /// the measurement that value gives, and the range the DNA can reach from this base.
        /// <para>
        /// Assumptions: the measurement is proportional to the scale this DNA's effects apply, i.e.
        /// measurement(v) = baseMeasurement * factor(v) / factor(baseValue), where factor is the product of
        /// the scale factors of the enabled effects (<see cref="DNAEffect.TryGetScaleFactor"/>; bone scale
        /// and bone transform effects report one). That holds when the scaled bone carries the whole
        /// measured span and its pivot is where the measurement starts: UMA 3 height scales the
        /// Position bone, which every bone hangs from, at the feet. It does not hold for offsets
        /// (bone translate, the position part of a bone transform), blendshapes, or several effects on
        /// bones along the measured span; use <see cref="DNAReverseInterpolation.Solve"/> with your own
        /// forward function for those.
        /// </para>
        /// <para>
        /// The curve and min/max mapping are inverted numerically, so any curve shape works. When a curve
        /// turns back and two values reach the target, the one nearest <paramref name="baseValue"/> is returned.
        /// </para>
        /// </summary>
        /// <param name="baseMeasurement">The measurement taken with this DNA at <paramref name="baseValue"/>.</param>
        /// <param name="projectedMeasurement">The measurement wanted.</param>
        /// <param name="baseValue">The DNA value in use when <paramref name="baseMeasurement"/> was taken.</param>
        /// <param name="axis">Which scale axis the measurement runs along, in the scaled bone's local
        /// space (0 = x, 1 = y, 2 = z). Y is up for the UMA 3 Position bone.</param>
        public DNAReverseInterpolation.Result SolveReverseInterpolation(float baseMeasurement, float projectedMeasurement, float baseValue, int axis = 1)
        {
            if (axis < 0 || axis > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(axis), "axis must be 0 (x), 1 (y) or 2 (z).");
            }
            if (!TryGetScaleFactor(baseValue, out _))
            {
                return new DNAReverseInterpolation.Result
                {
                    Solvable = false,
                    Value = Mathf.Clamp01(baseValue),
                    Achieved = baseMeasurement,
                    Minimum = baseMeasurement,
                    Maximum = baseMeasurement,
                };
            }
            return DNAReverseInterpolation.SolveProportional(
                v =>
                {
                    TryGetScaleFactor(v, out Vector3 factor);
                    return factor[axis];
                },
                baseMeasurement, baseValue, projectedMeasurement);
        }

        /// <summary>
        /// The combined scale factor of the enabled effects that scale (the product of each one's
        /// <see cref="DNAEffect.TryGetScaleFactor"/>). False when no effect scales anything.
        /// </summary>
        /// <param name="value">Raw DNA value, 0..1.</param>
        public bool TryGetScaleFactor(float value, out Vector3 factor)
        {
            factor = Vector3.one;
            bool any = false;
            foreach (var effect in effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }
                if (effect.TryGetScaleFactor(value, out Vector3 effectFactor))
                {
                    factor = Vector3.Scale(factor, effectFactor);
                    any = true;
                }
            }
            return any;
        }

        public DNABuildType GetBuildType()
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }
                updateFlags |= effect.AreaEffect;
            }
            return updateFlags;
        }
        
        /// <summary>
        /// True when an effect takes part in a pass limited to the given build types.
        /// DNABuildType.All means no limit, so effects that report no build type still run.
        /// </summary>
        private static bool InBuildTypes(DNAEffect effect, DNABuildType only)
        {
            return only == DNABuildType.All || (effect.AreaEffect & only) != 0;
        }

        /// <param name="atDefaultValue">True when value is this DNA's default. Only effects that ask for it run then.</param>
        /// <param name="only">Limits the pass to effects that report one of these build types.</param>
        public DNABuildType AfterRecipeGeneration(UMAData avatar, float value, bool atDefaultValue = false, DNABuildType only = DNABuildType.All)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }
                if (atDefaultValue && !effect.AppliesAtDefaultValue)
                {
                    continue;
                }
                if (!InBuildTypes(effect, only))
                {
                    continue;
                }
                updateFlags |= effect.AreaEffect;
                effect.AfterRecipeGenerated(avatar, this, value);
            }
            return updateFlags;
        }

        public void Restore(UMAData avatar, float value)
        {
            foreach (var effect in effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }
                effect.Restore(avatar, this, value);
            }
        }

        public DNABuildType PreApply(UMAData avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }
                updateFlags |= effect.AreaEffect;
                effect.PreApply(avatar, this, value);
            }
            return updateFlags;
        }
        public DNABuildType Apply(UMAData avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }
                updateFlags |= effect.AreaEffect;
                effect.Apply(avatar, this, value);
            }
            return updateFlags;
        }

        public DNABuildType ApplyBaseBonePoseEffects(UMAData avatar, float value)
        {
            return ApplyFilteredEffects(avatar, value, effect => effect is DNAEffect_BonePose bonePose && bonePose.isBasePose);
        }

        public DNABuildType ApplyNonBaseEffects(UMAData avatar, float value)
        {
            return ApplyFilteredEffects(avatar, value, effect => !(effect is DNAEffect_BonePose bonePose) || !bonePose.isBasePose);
        }

        private DNABuildType ApplyFilteredEffects(UMAData avatar, float value, Func<DNAEffect, bool> predicate)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (effect == null || !predicate(effect))
                {
                    continue;
                }
                if (!effect.enabled)
                {
                    continue;
                }

                updateFlags |= effect.AreaEffect;
                effect.Apply(avatar, this, value);
            }
            return updateFlags;
        }

        /// <param name="only">Limits the pass to effects that report one of these build types.</param>
        public DNABuildType PostApply(UMAData avatar, float value, DNABuildType only = DNABuildType.All)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }
                if (!InBuildTypes(effect, only))
                {
                    continue;
                }
                updateFlags |= effect.AreaEffect;
                effect.PostApply(avatar, this, value);
            }
            return updateFlags;
        }
    }
}
