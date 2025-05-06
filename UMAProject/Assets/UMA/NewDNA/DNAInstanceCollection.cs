using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// This class is used to hold a collection of DNA instances.
    /// </summary>
    [System.Serializable]
    public class DNAInstanceCollection 
    {
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

    }
}
