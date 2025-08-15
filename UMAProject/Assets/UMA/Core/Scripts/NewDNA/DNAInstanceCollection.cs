using System;
using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// This class is used to hold a collection of DNA instances.
    /// </summary>
    [System.Serializable]
    public class DNAInstanceCollection 
    {
        [Flags]
        public enum DNABuildType
        {
            None,
            Texture,
            Mesh,
            Rig,
            BlendShape,
            SharedColors,
            Base = Mesh | Rig | Texture,
            All = Texture | Mesh | Rig | BlendShape | SharedColors
        }
#pragma warning disable CS0414
        private DNABuildType updateFlags = DNABuildType.None;
#pragma warning restore CS0414
        private DNACollection _DNACollection = null;

        public DNACollection dnaCollection
        {
            get
            {
                return _DNACollection;
            }
        }

        public DNA GetDNA(string dnaName)
        {
            if (_DNACollection == null)
            {
                Debug.LogError("DNACollection is not initialized. Please call Initialize() before accessing DNA.");
                return null;
            }
            if (_DNACollection.dnaDictionary.TryGetValue(dnaName, out DNA dna))
            {
                return dna;
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"DNA with name '{dnaName}' not found in the collection.");
#endif
                return null;
            }
        }

        public void Initialize(DNACollection collection)
        {
            _DNACollection=collection;
        }

        /// <summary>
        /// The list of DNA instances.
        /// </summary>
        public List<DNAInstance> dnaInstances = new List<DNAInstance>();
        /// <summary>
        /// Adds a new DNA instance to the collection.
        /// </summary>
        /// <param name="dnaInstance">The DNA instance to add.</param>
        public void AddDNAInstance(DNAInstance dnaInstance)
        {
            dnaInstances.Add(dnaInstance);
        }
        /// <summary>
        /// Removes a DNA instance from the collection.
        /// </summary>
        /// <param name="dnaInstance">The DNA instance to remove.</param>
        public void RemoveDNAInstance(DNAInstance dnaInstance)
        {
            dnaInstances.Remove(dnaInstance);
        }

        public void AfterRecipeGenerated(DynamicCharacterAvatar avatar)
        {
            DNABuildType updateFlags = DNABuildType.None;

            for (int i = 0; i < dnaInstances.Count; i++)
            {
                DNA dna = dnaCollection.dnaDictionary[dnaInstances[i].name];
                if (dnaInstances[i].value != dna.defaultValue)
                {
                    updateFlags |= dna.AfterRecipeGeneration(avatar, dnaInstances[i].value);
                }
            }
            //return updateFlags;
        }

        public void PreApply(DynamicCharacterAvatar avatar)
        {
            DNABuildType updateFlags = DNABuildType.None;
            
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                DNA dna = dnaCollection.dnaDictionary[dnaInstances[i].name];
                if (dnaInstances[i].value != dna.defaultValue)
                {
                    updateFlags |= dna.PreApply(avatar, dnaInstances[i].value);
                }
            }
            //return updateFlags;

        }

        public void Apply(DynamicCharacterAvatar umaData)
        {
            DNABuildType updateFlags = DNABuildType.None;
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                DNA dna = dnaCollection.dnaDictionary[dnaInstances[i].name];
                if (dnaInstances[i].value != dna.defaultValue)
                {
                    updateFlags |= dna.Apply(umaData, dnaInstances[i].value);
                }
            }
            //return updateFlags;

        }

        public void PostApply(DynamicCharacterAvatar umaData)
        {
            DNABuildType updateFlags = DNABuildType.None;
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                DNA dna = dnaCollection.dnaDictionary[dnaInstances[i].name];
                if (dnaInstances[i].value != dna.defaultValue)
                {
                    updateFlags |= dna.PostApply(umaData, dnaInstances[i].value);
                }
            }
            //return updateFlags;
        }
    }
}
