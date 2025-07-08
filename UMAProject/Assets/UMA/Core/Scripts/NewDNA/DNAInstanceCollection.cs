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
            All = Texture | Mesh | Rig
        }

        public DNABuildType updateFlags = DNABuildType.None;

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

        public void PreApply(DNACollection theCollection, DynamicCharacterAvatar avatar)
        {
            DNABuildType updateFlags = DNABuildType.None;
            
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                if (!dnaInstances[i].isDefault)
                {
                    DNA dna = theCollection.dnaDictionary[dnaInstances[i].name];
                    updateFlags |= dna.PreApply(avatar, dnaInstances[i].value);
                }
            }
        }

        public void Apply(DNACollection theCollection, UMAData umaData)
        {

        }

        public void PostApply(DNACollection theCollection, UMAData umaData)
        {
        }
    }
}
