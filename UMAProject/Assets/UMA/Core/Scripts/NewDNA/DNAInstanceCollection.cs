using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Holds and applies a collection of DNA instances against a DNACollection.
    /// Returns DNABuildType flags indicating what needs updating.
    /// </summary>
    [Serializable]
    public class DNAInstanceCollection
    {
        [Flags]
        public enum DNABuildType
        {
            None = 0,
            Texture = 1 << 0,
            Mesh = 1 << 1,
            Rig = 1 << 2,
            BlendShape = 1 << 3,
            SharedColors = 1 << 4,
            Base = Mesh | Rig | Texture,
            All = Texture | Mesh | Rig | BlendShape | SharedColors
        }

        private DNACollection _DNACollection;

        /// <summary>
        /// All DNA instances to process.
        /// Name should match an entry in dnaCollection.dnaDictionary.
        /// </summary>
        public List<DNAInstance> dnaInstances = new List<DNAInstance>();

        public DNACollection dnaCollection => _DNACollection;

        /// <summary>
        /// Ensure the collection is set and its dictionary is populated.
        /// </summary>
        public void Initialize(DNACollection collection)
        {
            _DNACollection = collection;
            if (_DNACollection == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("DNAInstanceCollection.Initialize called with null DNACollection.");
#endif
                return;
            }
            // Make sure dictionary exists and is populated.
            _DNACollection.LoadDictionary();
        }

        /// <summary>
        /// Get a DNA asset by name if it exists.
        /// </summary>
        public DNA GetDNA(string dnaName)
        {
            if (_DNACollection == null)
            {
                Debug.LogError("DNACollection is not initialized. Call Initialize() before accessing DNA.");
                return null;
            }
            if (string.IsNullOrEmpty(dnaName))
            {
#if UNITY_EDITOR
                Debug.LogWarning("GetDNA called with null or empty dnaName.");
#endif
                return null;
            }
            var dict = _DNACollection.dnaDictionary;
            if (dict != null && dict.TryGetValue(dnaName, out var dna))
            {
                return dna;
            }
#if UNITY_EDITOR
            Debug.LogWarning($"DNA '{dnaName}' not found in the collection dictionary.");
#endif
            return null;
        }

        /// <summary>
        /// Adds a DNA instance to the collection.
        /// </summary>
        public void AddDNAInstance(DNAInstance dnaInstance)
        {
            if (dnaInstance == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("AddDNAInstance called with null dnaInstance. Ignored.");
#endif
                return;
            }
            dnaInstances.Add(dnaInstance);
        }

        /// <summary>
        /// Removes a DNA instance from the collection.
        /// </summary>
        public void RemoveDNAInstance(DNAInstance dnaInstance)
        {
            if (dnaInstance == null) return;
            dnaInstances.Remove(dnaInstance);
        }

        /// <summary>
        /// Called after a recipe is generated, before application. Uses the avatar context.
        /// Returns flags indicating what systems need updating.
        /// </summary>
        public DNABuildType AfterRecipeGenerated(DynamicCharacterAvatar avatar)
        {
            DNABuildType flags = DNABuildType.None;

            if (!ValidateCollection())
            {
                return flags;
            }

            var dict = _DNACollection.dnaDictionary;
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                var inst = dnaInstances[i];
                if (inst == null || string.IsNullOrEmpty(inst.name)) continue;

                if (!dict.TryGetValue(inst.name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.name}' not found in collection during AfterRecipeGenerated.");
#endif
                    continue;
                }

                // If value differs from default, run effect
                if (ValueDiffers(inst.value, dna.defaultValue))
                {
                    flags |= dna.AfterRecipeGeneration(avatar, inst.value);
                }
            }
            return flags;
        }

        /// <summary>
        /// Pre-application pass on UMAData. Returns flags indicating required updates.
        /// </summary>
        public DNABuildType PreApply(UMAData umaData)
        {
            DNABuildType flags = DNABuildType.None;

            if (!ValidateCollection())
            {
                return flags;
            }

            var dict = _DNACollection.dnaDictionary;
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                var inst = dnaInstances[i];
                if (inst == null || string.IsNullOrEmpty(inst.name)) continue;

                if (!dict.TryGetValue(inst.name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.name}' not found in collection during PreApply.");
#endif
                    continue;
                }

                if (ValueDiffers(inst.value, dna.defaultValue))
                {
                    flags |= dna.PreApply(umaData, inst.value);
                }
            }
            return flags;
        }

        /// <summary>
        /// Application pass on UMAData. Returns flags indicating required updates.
        /// </summary>
        public DNABuildType Apply(UMAData umaData)
        {
            DNABuildType flags = DNABuildType.None;

            if (!ValidateCollection())
            {
                return flags;
            }

            var dict = _DNACollection.dnaDictionary;
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                var inst = dnaInstances[i];
                if (inst == null || string.IsNullOrEmpty(inst.name)) continue;

                if (!dict.TryGetValue(inst.name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.name}' not found in collection during Apply.");
#endif
                    continue;
                }

                if (ValueDiffers(inst.value, dna.defaultValue))
                {
                    flags |= dna.Apply(umaData, inst.value);
                }
            }
            return flags;
        }

        /// <summary>
        /// Post-application pass on UMAData. Returns flags indicating required updates.
        /// </summary>
        public DNABuildType PostApply(UMAData umaData)
        {
            DNABuildType flags = DNABuildType.None;

            if (!ValidateCollection())
            {
                return flags;
            }

            var dict = _DNACollection.dnaDictionary;
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                var inst = dnaInstances[i];
                if (inst == null || string.IsNullOrEmpty(inst.name)) continue;

                if (!dict.TryGetValue(inst.name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.name}' not found in collection during PostApply.");
#endif
                    continue;
                }

                if (ValueDiffers(inst.value, dna.defaultValue))
                {
                    flags |= dna.PostApply(umaData, inst.value);
                }
            }
            return flags;
        }

        private bool ValidateCollection()
        {
            if (_DNACollection == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("DNAInstanceCollection has no DNACollection assigned. Call Initialize().");
#endif
                return false;
            }
            if (_DNACollection.dnaDictionary == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("DNACollection.dnaDictionary is null. Ensure LoadDictionary() is called.");
#endif
                return false;
            }
            return true;
        }

        private static bool ValueDiffers(float a, float b)
        {
            return Mathf.Abs(a - b) > Mathf.Epsilon;
        }
    }
}