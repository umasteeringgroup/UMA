#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UMA;

namespace UMAEditor
{
	[CustomEditor(typeof(SlotDataAsset))]
    public class SlotDataAssetInspector : Editor
    {
        public override void OnInspectorGUI()
        {
			base.OnInspectorGUI();

			foreach (var t in targets)
			{
				var slotDataAsset = t as SlotDataAsset;
				if (slotDataAsset != null)
				{
					if (slotDataAsset.animatedBoneHashes.Length != slotDataAsset.animatedBoneNames.Length)
					{
						slotDataAsset.animatedBoneHashes = new int[slotDataAsset.animatedBoneNames.Length];
						for (int i = 0; i < slotDataAsset.animatedBoneNames.Length; i++)
						{
							slotDataAsset.animatedBoneHashes[i] = UMASkeleton.StringToHash(slotDataAsset.animatedBoneNames[i]);
						}
						EditorUtility.SetDirty(slotDataAsset);
						AssetDatabase.SaveAssets();
					}
				}
			}

			GUILayout.Space(20);
			Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
			GUI.Box(dropArea, "Drag Overlays here");
			GUILayout.Space(20);

			DropAreaGUI(dropArea);
        }

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
					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
					for (int i = 0; i < draggedObjects.Length; i++)
					{
						if (draggedObjects[i])
						{
							var go = draggedObjects[i] as GameObject;
							if (go != null)
							{
								AddAnimatedBone(go.name);
							}
						}
					}
					AssetDatabase.SaveAssets();
				}
			}
		}

		private void AddAnimatedBone(string animatedBone)
		{
			var hash = UMASkeleton.StringToHash(animatedBone);
			foreach (var t in targets)
			{
				var slotDataAsset = t as SlotDataAsset;
				if (slotDataAsset != null)
				{
					ArrayUtility.Add(ref slotDataAsset.animatedBoneNames, animatedBone);
					ArrayUtility.Add(ref slotDataAsset.animatedBoneHashes, hash);
					EditorUtility.SetDirty(slotDataAsset);
				}
			}			
		}
    }
}
#endif
