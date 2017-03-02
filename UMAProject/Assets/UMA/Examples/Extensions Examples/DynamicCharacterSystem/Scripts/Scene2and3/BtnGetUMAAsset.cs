using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using UMA;
using UMAAssetBundleManager;

public class BtnGetUMAAsset : MonoBehaviour {

    public enum UMAAssetType  { Race, Slot, Overlay, Recipe};

    public string assetName = "";
    public UMAAssetType umaAssetType = UMAAssetType.Race;
    public UnityEvent assetLoaded;
    //bool assetLoading = false;
    
    public void GetAsset()
    {
        if(umaAssetType == UMAAssetType.Race)
        {

        }
        else if (umaAssetType == UMAAssetType.Slot)
        {

        }
        else if (umaAssetType == UMAAssetType.Overlay)
        {

        }
        else if (umaAssetType == UMAAssetType.Recipe)
        {

        }
    }
	
	// Update is called once per frame
	void Update () {
	
	}
}
