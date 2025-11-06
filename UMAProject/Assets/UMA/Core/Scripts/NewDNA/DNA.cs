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

        public DNABuildType PreApply(UMAData avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
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
                updateFlags |= effect.AreaEffect;
                effect.PostApply(avatar, this, value);
            }
            return updateFlags;
        }
    }
}