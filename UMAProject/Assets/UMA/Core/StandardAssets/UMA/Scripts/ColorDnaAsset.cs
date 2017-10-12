using UnityEngine;
using System.Collections;

namespace UMA
{
    public class ColorDnaAsset : ScriptableObject
    {
        [System.Serializable]
        public class DNAColorSet
        {
            public string dnaEntryName;
            public string overlayEntryName;
            [Tooltip("Color Channel: For example PBR, 0 = Albedo, 1 = Normal, 2 = Metallic")]
            public int colorChannel = 0;
            public Color32 minColor = new Color(1,1,1,0);
            public Color32 maxColor = new Color(1,1,1,1);
        }

        public int dnaTypeHash;
       
        public DNAColorSet[] colorSets;

        void OnEnable()
        {
            if (colorSets == null)
            {
                colorSets = new DNAColorSet[0];
            }
        }
    	
        #if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/DNA/Color DNA")]
        public static void CreateColorDnaAsset()
        {
            UMA.CustomAssetUtility.CreateAsset<ColorDnaAsset>();
        }
        #endif
    }
}
