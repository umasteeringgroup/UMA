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
            public string umaMaterialName;    // Helpful for matching generated materials
            public Vector2[] normBaseUV;      // UV0 normalized to [0..1] within SlotData.UVArea at record time
            public Vector2[] overlayUV;       // UV1 used for overlay sampling [0..1]
            public int[] triangles;           // Triangle indices for this slot-local mesh
            public int[] triOrdinals;         // Global ordinals (indices within the original selected triangle list) for each triangle
        }

        public string overlayName;            // OverlayDataAsset.name used for the source stamp
        public int bleedPixels;               // default dilation at record time
        public bool forceLinearSampling;      // default sampling mode at record time
        public List<SlotStamp> slots = new List<SlotStamp>(8);
    }
}
