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
    }
}
