using System;
using UnityEngine;

namespace UMA
{
	/// <summary>
	/// A package class holding additional races, slots, and/or overlays.
	/// </summary>
    public class UMAAssetCollection : ScriptableObject
    {
        public RaceData[] raceData;
        public SlotDataAsset[] slotData;
        public OverlayDataAsset[] overlayData;

		public virtual void AddToLibraries(OverlayLibraryBase overlayLibrary, SlotLibraryBase slotLibrary, RaceLibraryBase raceLibrary)
		{
			if (overlayLibrary != null && overlayData.Length > 0)
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(overlayLibrary, "Added Asset Collection");
#endif
				for (int i = 0; i < overlayData.Length; i++)
				{
					overlayLibrary.AddOverlayAsset(overlayData[i]);
				}
			}
			if (slotLibrary != null && slotData.Length > 0)
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(slotLibrary, "Added Asset Collection");
#endif
				for (int i = 0; i < slotData.Length; i++)
				{
					slotLibrary.AddSlotAsset(slotData[i]);
				}

			}
			if (raceLibrary != null && raceData.Length > 0) 
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(raceLibrary, "Added Asset Collection");
#endif
				for (int i = 0; i < raceData.Length; i++)
				{
					raceLibrary.AddRace(raceData[i]);
				}
			}
		}
	}
}
