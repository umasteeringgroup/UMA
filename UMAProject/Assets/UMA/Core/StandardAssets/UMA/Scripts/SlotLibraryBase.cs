using UnityEngine;
using System.Collections.Generic;
using System;

namespace UMA
{
	/// <summary>
	/// Base class for UMA slot libraries.
	/// </summary>
	public abstract class SlotLibraryBase : MonoBehaviour 
	{
		public virtual void AddSlotAsset(SlotDataAsset slot) { throw new NotFiniteNumberException(); }
		public virtual SlotDataAsset[] GetAllSlotAssets() { throw new NotFiniteNumberException(); }
		public abstract SlotData InstantiateSlot(string name);
		public abstract SlotData InstantiateSlot(int nameHash);
		public abstract SlotData InstantiateSlot(string name, List<OverlayData> overlayList);
		public abstract SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList);
		public virtual bool HasSlot(string name) { throw new NotImplementedException(); }
		public virtual bool HasSlot(int nameHash) { throw new NotImplementedException(); }

		public abstract void UpdateDictionary();
		public abstract void ValidateDictionary();
	}
}
