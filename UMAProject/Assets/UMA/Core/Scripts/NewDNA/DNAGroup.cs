using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// This class is used to hold a collection of DNA  
    /// </summary>
    [System.Serializable]
    public class DNAGroup : ScriptableObject
    {
        [Tooltip("The area of the DNA, used for grouping")]
        public string DNAArea;
        [Tooltip("The list of DNA that this contains.")]
        public List<DNA> dnaList = new List<DNA>();
        [Tooltip("The maximum total value for this group.")]
        public float MaxTotalValue = 0.0f;

        public float GetDefaultValue(string dnaName)
        {
            var dna = dnaList.Find(d => d.name == dnaName);
            if (dna != null)
            {
                return dna.defaultValue;
            }
            return -99f; // Default to -99 if not found, or you could throw an exception or handle it differently
        }

#if UNITY_EDITOR
        // Editor-only persisted foldout state for inspectors
        [SerializeField, HideInInspector]
        public bool editorFoldout;

        [UnityEditor.MenuItem("Assets/Create/UMA/DNA/DNA Group")]
        public static void CreateDNAGroup()
        {
            UMA.CustomAssetUtility.CreateAsset<DNAGroup>();
        }
#endif
    }
}