using UnityEngine;
using System.Collections;

namespace UMA
{
    [System.Serializable]
    public abstract class DynamicUMADnaBase : UMADnaBase
    {

        #region Fields
        public DynamicUMADnaAsset _dnaAsset;

        public string dnaAssetName;
        //bool to make the recipeEditor save if the DNAAsset was updated
        [System.NonSerialized]
        public bool didDnaAssetUpdate = false;
        //bool to make the recipeEditor save if the DNATypeHash was updated
        [System.NonSerialized]
        public bool didDnaTypeHashUpdate = false;

        public float[] _values = new float[0];
        public string[] _names = new string[0];
        #endregion

        #region Properties
        public abstract DynamicUMADnaAsset dnaAsset { get; set; }

        public abstract override int Count { get; }
        public abstract override float[] Values
        {
            get; set;
        }

        public abstract override string[] Names
        {
            get;
        }
		#endregion
		#region METHODS
		public abstract float GetValue(string dnaName, bool failSilently = false);
        public abstract override float GetValue(int idx);
        public abstract void SetValue(string name, float value);
        public abstract override void SetValue(int idx, float value);
        public abstract int ImportUMADnaValues(UMADnaBase umaDna);

        public virtual void SetDnaTypeHash(int typeHash)
        {
            base.dnaTypeHash = typeHash;
        }

        /// <summary>
        /// Method for finding a DynamicUMADnaAsset by name. This can happen when a recipe tries to load load an asset based on an instance ID that may have changed or if the Asset is in an AssetBundle and was not available when the dna was loaded
        /// </summary>
        /// <param name="dnaAssetName"></param>
        /// <param name="dynamicallyAddFromResources"></param>
        /// <param name="dynamicallyAddFromAssetBundles"></param>
        public virtual void FindMissingDnaAsset(string _dnaAssetName, bool dynamicallyAddFromResources = true, bool dynamicallyAddFromAssetBundles = false)
        {
            DynamicUMADnaAsset[] foundAssets = Resources.LoadAll<DynamicUMADnaAsset>("");
            for (int i = 0; i < foundAssets.Length; i++)
            {
                if (foundAssets[i].name == _dnaAssetName)
                {
                    dnaAsset = foundAssets[i];
                    didDnaAssetUpdate = true;
                    break;
                }
            }
            if (didDnaAssetUpdate == false)
            {
                Debug.LogWarning("DynamicUMADnaBase could not find DNAAsset " + _dnaAssetName + "!");
            }
        }
        public virtual void SetMissingDnaAsset(DynamicUMADnaAsset[] foundAssets)
        {
            //we can only use one
            if(foundAssets.Length > 0)
            dnaAsset = foundAssets[0];
        }
		#endregion
	}
}
