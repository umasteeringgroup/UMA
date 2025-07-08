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
        public List<DNAEffect> effects = new List<DNAEffect>();

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/DNA/DNA Item")]
        public static void CreateDNA()
        {
            UMA.CustomAssetUtility.CreateAsset<DNA>();
        }
#endif

        public DNABuildType PreApply(DynamicCharacterAvatar avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                updateFlags |= effect.AreaEffect;
                effect.PreApply(avatar, this, value);
            }
            return updateFlags;
        }
        public DNABuildType Apply(DynamicCharacterAvatar avatar, float value)
        {
            DNABuildType updateFlags = DNABuildType.None;
            foreach (var effect in effects)
            {
                updateFlags |= effect.AreaEffect;
                effect.Apply(avatar, this, value);
            }
            return updateFlags;
        }
        public DNABuildType PostApply(DynamicCharacterAvatar avatar, float value)
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