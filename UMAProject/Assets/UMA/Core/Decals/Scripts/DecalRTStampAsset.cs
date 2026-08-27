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
            public string slotName;           // Used for replay matching only when slotGroup is empty
            public string slotGroup;          // Preferred replay identity when available
            public int slotHash;               // Cached identity retained for editor/debug data; replay does not fall back to it
            public string umaMaterialName;    // Helpful for matching generated materials
            public Vector2[] normBaseUV;      // UV0 normalized to [0..1] within SlotData.UVArea at record time
            public Vector2[] overlayUV;       // UV1 used for overlay sampling [0..1]
            public int[] triangles;           // Triangle indices for this slot-local mesh
#if UNITY_EDITOR
			public int[] triOrdinals;         // Global ordinals (indices within the original selected triangle list) for each triangle
			public int[] slotRelativeTriangles; // Triangle indices relative to SlotDataAsset.meshData (for editor debug/edit)
#endif
            public Rect recordedUVArea;       // The UVArea at the time the stamp was created
            public bool debugDontUse;       // If true, this slot is ignored at apply time

			// TODO: Remove this field in future versions	
			// TODO: 
			[NonSerialized]
			public Dictionary<int, int> HashToFrame = new Dictionary<int, int>();

			public override string ToString()
            {
                string ignored = debugDontUse ? "(ignored)" : "";
                if (!string.IsNullOrEmpty(slotGroup))
                {
                    return $"{slotGroup} {ignored}";
                }
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

        [Tooltip("Overlay group that receives this stamp when UMA rebuilds the atlas.")]
        public string overlayGroup;           // target group used for replay
        [Tooltip("Overlay whose textures are sampled by this stamp. This can be different from the target overlay group.")]
        public OverlayDataAsset sourceOverlay;
        [Tooltip("Indexer name used to restore the source overlay when a direct asset reference is unavailable (for example after JSON restore).")]
        public string sourceOverlayName;
        [Min(0f), Tooltip("World-space projection radius used to create this stamp. One Unity " +
            "unit is one meter. Runtime fluid emitters use it to convert their metric source " +
            "radius into decal UV space.")]
        public float projectionRadiusMeters;
        public int bleedPixels;               // default dilation at record time
        public bool forceLinearSampling;      // default sampling mode at record time
        public bool invertY;                  // if true, Y is inverted during stamping (normalized space)
        public List<SlotStamp> slots = new List<SlotStamp>(8);
    }
}
