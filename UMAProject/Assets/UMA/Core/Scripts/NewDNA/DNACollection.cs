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


        public List<string> GetDNANames()
        {
            if (DNADictionary.Count == 0)
            {
                LoadDictionary();
            }
            return new List<string>(DNADictionary.Keys);
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
                        if (DNADictionary == null)
                        {
                            DNADictionary = new Dictionary<string, DNA>();
                        }
                        if (dna == null || string.IsNullOrEmpty(dna.name))
                            continue;
                        if (!DNADictionary.ContainsKey(dna.name))
                        {
                            DNADictionary.Add(dna.name, dna);
                        }
                    }
                }
            }
        }

        public DNAInstanceCollection GetDefaultDNA(RaceData race)
        {
            LoadDictionary();
            var dnaInstanceCollection = new DNAInstanceCollection();
            foreach (var dna in dnaDictionary.Values)
            {
                var dnaInstance = new DNAInstance(dna.name, dna.defaultValue);
                dnaInstanceCollection.AddDNAInstance(dnaInstance);
            }
            dnaInstanceCollection.Initialize(race.DNACollection);
            return dnaInstanceCollection;
        }

        private Dictionary<string, DNA> DNADictionary = new Dictionary<string, DNA>();
    }
}
