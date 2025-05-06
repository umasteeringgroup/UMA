using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// This class is used to hold a collection of DNA  
    /// </summary>
    [System.Serializable]
    public class DNACollection
    {
        public string DNAArea;
        public Dictionary<string, DNA> dnaDictionary = new Dictionary<string, DNA>();
    }
}