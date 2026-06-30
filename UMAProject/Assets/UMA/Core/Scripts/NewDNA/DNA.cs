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

        public DNABuildType GetBuildType()
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                updateFlags |= effect.AreaEffect;
            }
            return updateFlags;
        }
        
        public DNABuildType AfterRecipeGeneration(UMAData avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                updateFlags |= effect.AreaEffect;
                effect.AfterRecipeGenerated(avatar, this, value);
            }
            return updateFlags;
        }

        public void Restore(UMAData avatar, float value)
        {
            foreach (var effect in effects)
            {
                effect.Restore(avatar, this, value);
            }
        }

        public DNABuildType PreApply(UMAData avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (!effect.enabled)
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
                if (!effect.enabled)
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

        public DNABuildType PostApply(UMAData avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                if (!effect.enabled)
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