using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>
    /// This class is used to hold a collection of DNA.
    /// It contains a list of DNACollections and provides a dictionary for quick access to DNA by name.
    /// </summary>
    [System.Serializable]
    public class DNACollection
    {
        [Tooltip("The list of DNA groups that this collection contains. Each group can contain multiple DNA types.")]
        public List<UMA.DNAGroup> DNAGroups = new();

        public Dictionary<string, DNA> dnaDictionary
        {
            get
            {
                if (DNADictionary.Count == 0 )
                {
                    LoadDictionary();
                }
                return DNADictionary;
            }
        }

        public void Reset()
        {
            DNADictionary.Clear();
        }

        public void LoadDictionary()
        {
            DNADictionary.Clear();
            foreach (var collection in DNAGroups)
            {
                if (collection != null && collection.dnaList != null)
                {
                    foreach (var dna in collection.dnaList)
                    {
                        if (!DNADictionary.ContainsKey(dna.dnaName))
                        {
                            DNADictionary.Add(dna.name, dna);
                        }
                    }
                }
            }
        }

        private Dictionary<string, DNA> DNADictionary = new Dictionary<string, DNA>();
    }
}
