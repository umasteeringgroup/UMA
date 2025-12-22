using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    [Serializable]
    public class DecalRTStampAsset : ScriptableObject
    {
        [Serializable]
        public class SlotStamp
        {
            public string slotName;           // Identifies the target slot in the recipe at apply time
            public int slotHash;            // Cached hash of slotName for faster matching
            public string umaMaterialName;    // Helpful for matching generated materials
            public Vector2[] normBaseUV;      // UV0 normalized to [0..1] within SlotData.UVArea at record time
            public Vector2[] overlayUV;       // UV1 used for overlay sampling [0..1]
            public int[] triangles;           // Triangle indices for this slot-local mesh
            public int[] triOrdinals;         // Global ordinals (indices within the original selected triangle list) for each triangle
            public Rect recordedUVArea;       // The UVArea at the time the stamp was created
            public bool debugDontUse;       // If true, this slot is ignored at apply time

			// TODO: Remove this field in future versions	
			// TODO: 
			[NonSerialized]
			public Dictionary<int, int> HashToFrame = new Dictionary<int, int>();

			public override string ToString()
            {
                string ignored = debugDontUse ? "(ignored)" : "";
                return $"{slotName} {ignored}";
            }
            public bool SlotHasOverlay(UMAData umaData, int overlayHash)
            {
                SlotData sd = umaData.GetSlotByHash (slotHash);
                if (sd != null)
                {
                    if (sd.hasOverlay(overlayHash))
                        return true;
                }
                return false;
            }
        }

        public string overlayName;            // OverlayDataAsset.name used for the source stamp
		public int overlayNameHash;          // Cached hash of overlayName for fast matching at replay time
        public int bleedPixels;               // default dilation at record time
        public bool forceLinearSampling;      // default sampling mode at record time
        public bool invertY;                  // if true, Y is inverted during stamping (normalized space)
        public List<SlotStamp> slots = new List<SlotStamp>(8);
    }
}
