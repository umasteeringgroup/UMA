using UnityEngine;
using UnityEditor;
using UMA;
using System.Collections.Generic;

[CustomEditor(typeof(UMACrowdRandomSet))]
public class UMACrowdRandomSetEditor : Editor
{
	private void DropAreaGUI(Rect dropArea)
	{
		var evt = Event.current;

		if (evt.type == EventType.DragUpdated)
		{
			if (dropArea.Contains(evt.mousePosition))
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
			}
		}

		if (evt.type == EventType.DragPerform)
		{
			if (dropArea.Contains(evt.mousePosition))
			{
				DragAndDrop.AcceptDrag();

				UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
				var slots = new List<SlotData>();
				var overlays = new List<OverlayData>();

				for (int i = 0; i < draggedObjects.Length; i++)
				{
					if (draggedObjects[i])
					{
						SlotData tempSlotData = draggedObjects[i] as SlotData;
						if (tempSlotData)
						{
							slots.Add(tempSlotData);
						}

						OverlayData tempOverlayData = draggedObjects[i] as OverlayData;
						if (tempOverlayData)
						{
							overlays.Add(tempOverlayData);
						}
					}
				}
				if (slots.Count > 0 && overlays.Count > 0)
				{
					var randomSet = target as UMACrowdRandomSet;
					var crowdSlotElement = new UMACrowdRandomSet.CrowdSlotElement();
					crowdSlotElement.possibleSlots = new UMACrowdRandomSet.CrowdSlotData[slots.Count];
					for (int i = 0; i < slots.Count; i++)
					{
						var crowdSlotData = new UMACrowdRandomSet.CrowdSlotData();
						crowdSlotData.slotID = slots[i].slotName;
						crowdSlotData.overlayElements = new UMACrowdRandomSet.CrowdOverlayElement[overlays.Count];
						for(int j = 0; j < overlays.Count; j++)
						{
							var crowdOverlayElement = new UMACrowdRandomSet.CrowdOverlayElement();
							crowdOverlayElement.possibleOverlays = new UMACrowdRandomSet.CrowdOverlayData[]
							{
								new UMACrowdRandomSet.CrowdOverlayData() { maxRGB = Color.white, minRGB = Color.white, overlayID = overlays[j].overlayName }
							};
							crowdSlotData.overlayElements[j] = crowdOverlayElement;
						}
						crowdSlotElement.possibleSlots[i] = crowdSlotData;
					}
					ArrayUtility.Add(ref randomSet.data.slotElements, crowdSlotElement);
					EditorUtility.SetDirty(randomSet);
					AssetDatabase.SaveAssets();
				}
			}
		}
	}
	
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();
		GUILayout.Space(20);
		Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
		GUI.Box(dropArea, "Drag Slot and Overlay pairs here");
		GUILayout.Space(20);

		DropAreaGUI(dropArea);
	}
	
}
