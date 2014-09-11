using System;
using UnityEngine;

namespace UMA
{
    public class UMAAssetCollection : ScriptableObject
    {
        public RaceData[] raceData;
        public SlotData[] slotData;
        public OverlayData[] overlayData;
		public UMACrowdRandomSet[] randomSets;

		public virtual void AddToLibraries(OverlayLibraryBase overlayLibrary, SlotLibraryBase slotLibrary, RaceLibraryBase raceLibrary, UMACrowd crowd)
		{
			if (overlayLibrary != null && overlayData.Length > 0)
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(overlayLibrary, "Added Asset Collection");
#endif
				for (int i = 0; i < overlayData.Length; i++)
				{
					overlayLibrary.AddOverlay(overlayData[i]);
				}
			}
			if (slotLibrary != null && slotData.Length > 0)
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(slotLibrary, "Added Asset Collection");
#endif
				for (int i = 0; i < slotData.Length; i++)
				{
					slotLibrary.AddSlot(slotData[i]);
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

			if (crowd != null && randomSets.Length > 0)
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(crowd, "Added Asset Collection");
#endif
				var newPool = new UMACrowdRandomSet[crowd.randomPool.Length + randomSets.Length];
				Array.Copy(crowd.randomPool, newPool, crowd.randomPool.Length);
				for (int i = 0; i < randomSets.Length; i++)
				{
					newPool[crowd.randomPool.Length + i] = randomSets[i];
				}
			}
		}
	}
}
