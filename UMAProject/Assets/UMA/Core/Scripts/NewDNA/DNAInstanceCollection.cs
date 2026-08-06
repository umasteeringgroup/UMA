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
            MeshModifiers = 1 << 5,
            Base = Mesh | Rig | Texture,
            All = Texture | Mesh | Rig | BlendShape | SharedColors | MeshModifiers
        }

        private DNACollection _DNACollection;
        [NonSerialized]
        public Dictionary<DNAGroup, List<DNAInstance>> dnaGroupInstances = new Dictionary<DNAGroup, List<DNAInstance>>();
        
        [NonSerialized]
        public Dictionary<string, DNAInstance> dnaInstanceDictionary = null;


        public Dictionary<string, DNAInstance> ToDictionary()
        {
            var dict = new Dictionary<string, DNAInstance>(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in dnaInstances)
            {
                if (instance != null && !string.IsNullOrEmpty(instance.Name))
                {
                    if (!dict.ContainsKey(instance.Name))
                    {
                        dict.Add(instance.Name, instance);
                    }
                }
            }
            return dict;
        }


        public DNAInstanceCollection Clone()
                    {
            DNAInstanceCollection clone = new DNAInstanceCollection();
            clone.Initialize(this._DNACollection);
            foreach (var instance in this.dnaInstances)
            {
                if (instance != null)
                {
                    clone.dnaInstances.Add(instance.Clone());
                }
            }
            return clone;
        }

        public float[] GetValues()
            {
            float[] values = new float[dnaInstances.Count];
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                values[i] = dnaInstances[i].Value;
            }
            return values;
        }

        public void SetValues(float[] values)
        {
            int count = Math.Min(values.Length, dnaInstances.Count);
            for (int i = 0; i < count; i++)
            {
                dnaInstances[i].Value = values[i];
            }
        }

        public string[] GetNames()
        {
            string[] names = new string[dnaInstances.Count];
            for (int i = 0; i < dnaInstances.Count; i++)
            {
                names[i] = dnaInstances[i].Name;
            }
            return names;
        }

        public List<DNAInstance> GetDNAInstancesByGroup(DNAGroup group)
        {
            LoadInstanceGroup();
            if (dnaGroupInstances.TryGetValue(group, out var instances))
            {
                return instances;
            }
            return new List<DNAInstance>();
        }

        public List<DNAInstance> GetUnknownDNAInstances()
        {
            LoadInstanceGroup();
            var unknowns = new List<DNAInstance>();
            var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in dnaGroupInstances)
            {
                var instances = kvp.Value;
                foreach (var inst in instances)
                {
                    if (inst != null && !string.IsNullOrEmpty(inst.Name))
                    {
                        knownNames.Add(inst.Name);
                    }
                }
            }
            foreach (var inst in dnaInstances)
            {
                if (inst != null && !string.IsNullOrEmpty(inst.Name) && !knownNames.Contains(inst.Name))
                {
                    unknowns.Add(inst);
                }
            }
            return unknowns;
        }

        public List<DNAGroup> GetAllDNAGroups()
        {
            LoadInstanceGroup();
            var groups = new List<DNAGroup>(dnaGroupInstances.Keys);
            return groups;
        }

        public List<DNAInstance> GetAllDNAInstances()
        {
            return dnaInstances;
        }

        public int GetDNAInstanceCountByGroup(DNAGroup group)
        {
            LoadInstanceGroup();
            if (dnaGroupInstances.TryGetValue(group, out var instances))
            {
                return instances.Count;
            }
            return 0;
        }

        public int GetUnknownDNAInstanceCount()
        {
            var unknowns = GetUnknownDNAInstances();
            return unknowns.Count;
        }

        public int GetTotalDNAInstanceCount()
        {
            return dnaInstances.Count;
        }

        public int GetDNAGroupCount()
        {
            LoadInstanceGroup();
            return dnaGroupInstances.Count;
        }

        public Dictionary<DNAGroup, List<DNAInstance>> GetDNAByGroup()
        {
            LoadInstanceGroup();
            return dnaGroupInstances;
        }

        public void ClearAll()
        {
            dnaInstances.Clear();
            dnaGroupInstances.Clear();
        }

        public float GetDNAInstanceValue(string dnaName)
        {
            foreach (var instance in dnaInstances)
            {
                if (instance != null && string.Equals(instance.Name, dnaName, StringComparison.OrdinalIgnoreCase))
                {
                    return instance.Value;
                }
            }
            return 0f; // or some default value
        }


        public void LoadInstanceGroup()
        {
            // Rebuild the cache of group -> instances from current data
            dnaGroupInstances.Clear();

            if (_DNACollection == null || _DNACollection.DNAGroups == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("DNAInstanceCollection.LoadInstanceGroup called without an initialized DNACollection. Call Initialize() first.");
#endif
                return;
            }

            // Ensure the dictionary in the collection is populated
            _DNACollection.LoadDictionary();

            var groups = _DNACollection.DNAGroups;

            // Build a lookup of dna name -> group for fast assignment
            var nameToGroup = new Dictionary<string, DNAGroup>(StringComparer.OrdinalIgnoreCase);

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var grp = groups[gi];
                if (grp == null) continue;

                // Ensure key for every group (even if empty)
                if (!dnaGroupInstances.ContainsKey(grp))
                {
                    dnaGroupInstances.Add(grp, new List<DNAInstance>());
                }

                var list = grp.dnaList;
                if (list == null) continue;

                for (int di = 0; di < list.Count; di++)
                {
                    var dna = list[di];
                    if (dna == null) continue;
                    var n = dna.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    if (!nameToGroup.ContainsKey(n))
                    {
                        nameToGroup.Add(n, grp);
                    }
                }
            }

            // Distribute instances into their owning groups
            if (dnaInstances != null)
            {
                for (int i = 0; i < dnaInstances.Count; i++)
                {
                    var inst = dnaInstances[i];
                    if (inst == null) continue;
                    if (string.IsNullOrEmpty(inst.Name)) continue;

                    DNAGroup grp;
                    if (nameToGroup.TryGetValue(inst.Name, out grp) && grp != null)
                    {
                        List<DNAInstance> bucket;
                        if (!dnaGroupInstances.TryGetValue(grp, out bucket))
                        {
                            bucket = new List<DNAInstance>();
                            dnaGroupInstances.Add(grp, bucket);
                        }
                        bucket.Add(inst);
                    }
                    else
                    {
                        // Unknown DNA names are ignored here; callers may group them separately as "Unknown".
                    }
                }
            }
        }

        public int InstanceCount
        {
            get
            {
                return dnaInstances?.Count ?? 0;
            }
        }

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
#if UNITY_EDITOR
            
#endif
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

            // Not found - maybe added /removed at runtime or after dictionary load.
            // find the DNA in the collection list and reload the dictionary.
            _DNACollection.LoadDictionary();
            if (dict != null && dict.TryGetValue(dnaName, out dna))
            {
                return dna;
            }

#if UNITY_EDITOR
            //Debug.LogWarning($"DNA '{dnaName}' not found in the collection dictionary.");
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
                if (inst == null || string.IsNullOrEmpty(inst.Name) || inst.enabled == false) continue;

                if (!dict.TryGetValue(inst.Name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    //Debug.LogWarning($"DNA '{inst.Name}' not found in collection during AfterRecipeGenerated.");
#endif
                    continue;
                }

                // If value differs from default, run effect
                if (ValueDiffers(inst.Value, dna.defaultValue))
                {
                    flags |= dna.AfterRecipeGeneration(avatar, inst.Value);
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
                if (inst == null || string.IsNullOrEmpty(inst.Name) || inst.enabled == false) continue;

                if (!dict.TryGetValue(inst.Name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.Name}' not found in collection during PreApply.");
#endif
                    continue;
                }

                if (ValueDiffers(inst.Value, dna.defaultValue))
                {
                    flags |= dna.PreApply(umaData, inst.Value);
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
                if (inst == null || string.IsNullOrEmpty(inst.Name) || inst.enabled == false) continue;

                if (!dict.TryGetValue(inst.Name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.Name}' not found in collection during Apply.");
#endif
                    continue;
                }

                if (ValueDiffers(inst.Value, dna.defaultValue))
                {
                    flags |= dna.ApplyBaseBonePoseEffects(umaData, inst.Value);
                }
            }

            for (int i = 0; i < dnaInstances.Count; i++)
            {
                var inst = dnaInstances[i];
                if (inst == null || string.IsNullOrEmpty(inst.Name) || inst.enabled == false) continue;

                if (!dict.TryGetValue(inst.Name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.Name}' not found in collection during Apply.");
#endif
                    continue;
                }

                if (ValueDiffers(inst.Value, dna.defaultValue))
                {
                    flags |= dna.ApplyNonBaseEffects(umaData, inst.Value);
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
                if (inst == null || string.IsNullOrEmpty(inst.Name) || inst.enabled == false) continue;

                if (!dict.TryGetValue(inst.Name, out var dna) || dna == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"DNA '{inst.Name}' not found in collection during PostApply.");
#endif
                    continue;
                }

                if (ValueDiffers(inst.Value, dna.defaultValue))
                {
                    flags |= dna.PostApply(umaData, inst.Value);
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
