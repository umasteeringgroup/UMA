using UnityEngine;
using System.Collections.Generic;
using UMA;

public abstract class OverlayLibraryBase : MonoBehaviour
{
	public abstract void AddOverlayAsset(OverlayDataAsset overlay);
	public abstract OverlayData InstantiateOverlay(string name);
	public abstract OverlayData InstantiateOverlay(int nameHash);
	public abstract OverlayData InstantiateOverlay(string name, Color color);
	public abstract OverlayData InstantiateOverlay(int nameHash, Color color);
	
	public abstract OverlayDataAsset[] GetAllOverlayAssets();
	[System.Obsolete("OverlayLibrary.GetAllOverlays() use OverlayLibrary.GetAllOverlayAssets() instead.", false)]
	public virtual OverlayData[] GetAllOverlays()
	{
		throw new System.NotImplementedException();
	}

	[System.Obsolete("OverlayLibrary.AddOverlay(OverlayData overlay) is obsolete use OverlayLibrary.AddOverlay(OverlayDataAsset overlay) instead.", false)]
	public virtual void AddOverlay(OverlayData overlay)
	{
		throw new System.NotImplementedException();
	}



	public abstract void UpdateDictionary();
    public abstract void ValidateDictionary();
}
