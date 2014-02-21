using UnityEngine;
using System.Collections.Generic;
using UMA;

public abstract class SlotLibraryBase : MonoBehaviour 
{	
    public abstract void AddSlot(SlotData slot);
	public abstract SlotData InstantiateSlot(string name);
	public abstract SlotData InstantiateSlot(int nameHash);
	public abstract SlotData InstantiateSlot(string name, List<OverlayData> overlayList);
	public abstract SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList);

	public abstract void UpdateDictionary();
    public abstract void ValidateDictionary();
}
