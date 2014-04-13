using UnityEngine;
using UnityEditor;
using UMA;

[CustomEditor(typeof(UMAAssetCollection))]
public class UMAAssetCollectionEditor : Editor 
{
	public override void OnInspectorGUI()
	{
		if (GUILayout.Button("Add to Scene"))
		{
			var collection = target as UMAAssetCollection;
			var overlayLibrary = UnityEngine.Object.FindObjectOfType<OverlayLibraryBase>();
			if (overlayLibrary != null)
			{
				Undo.RecordObject(overlayLibrary, "Added Asset Collection");
				foreach (var overlayData in collection.overlayData)
				{
					overlayLibrary.AddOverlay(overlayData);
				}
			}
			var slotLibrary = UnityEngine.Object.FindObjectOfType<SlotLibraryBase>();
			if (slotLibrary != null)
			{
				Undo.RecordObject(slotLibrary, "Added Asset Collection");
				foreach (var slotData in collection.slotData)
				{
					slotLibrary.AddSlot(slotData);
				}
			}
			var raceLibrary = UnityEngine.Object.FindObjectOfType<RaceLibraryBase>();
			if (raceLibrary != null)
			{
				Undo.RecordObject(raceLibrary, "Added Asset Collection");
				foreach (var raceData in collection.raceData)
				{
					raceLibrary.AddRace(raceData);
				}
			}

			var crowd = UnityEngine.Object.FindObjectOfType<UMACrowd>();
			if (crowd != null)
			{
				Undo.RecordObject(crowd, "Added Asset Collection");
				foreach (var randomSet in collection.randomSets)
				{
					if (ArrayUtility.IndexOf(crowd.randomPool, randomSet) < 0)
					{
						ArrayUtility.Add(ref crowd.randomPool, randomSet);
					}
				}
			}
		}
		base.OnInspectorGUI();
	}
}
