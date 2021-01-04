using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class AssetBundleLoader : MonoBehaviour
    {
        public AssetBundle[] Bundles;
        private List<AssetBundle> loadedBundles = new List<AssetBundle>();

        // Start is called before the first frame update
        void Start()
        {
            LoadBundles();
        }


        public void LoadBundles()
        {
            foreach (AssetBundle ab in Bundles)
            {
                if (!loadedBundles.Contains(ab))
                {
                    UMAAssetIndexer.Instance.AddFromAssetBundle(ab);
                    loadedBundles.Add(ab);
                }
            }
        }

        public void UnloadBundles()
        {
           foreach(AssetBundle ab in loadedBundles)
            {
                UMAAssetIndexer.Instance.UnloadBundle(ab);
            }
        }
    }
}
