using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
    [System.Serializable]
    public class DynamicUMADnaAsset : ScriptableObject
    {
		public int dnaTypeHash;

        //falls back to UMADnaHumanoid
		public string[] Names = {};

        public DynamicUMADnaAsset() { }

    }
}
