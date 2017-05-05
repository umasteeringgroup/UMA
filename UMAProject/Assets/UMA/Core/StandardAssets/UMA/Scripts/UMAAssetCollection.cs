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

		public virtual void AddToContext(UMAContext context)
		{
			if (context == null)
				return;

			if (overlayData.Length > 0)
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(context, "Added overlays from asset collection");
#endif
				for (int i = 0; i < overlayData.Length; i++)
				{
					context.AddOverlayAsset(overlayData[i]);
				}
			}
			if (slotData.Length > 0)
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(context, "Added slots from asset collection");
#endif
				for (int i = 0; i < slotData.Length; i++)
				{
					context.AddSlotAsset(slotData[i]);
				}

			}
			if (raceData.Length > 0) 
			{
#if UNITY_EDITOR
				UnityEditor.Undo.RecordObject(context, "Added races from asset collection");
#endif
				for (int i = 0; i < raceData.Length; i++)
				{
					context.AddRace(raceData[i]);
				}
			}
		}
	}
}
