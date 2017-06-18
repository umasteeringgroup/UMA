#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(SlotDataAsset))]
    public class SlotDataAssetInspector : Editor
    {
		//allow for delayed saving so typing in a field does not trigger save with every keystroke
		private float lastActionTime = 0;
		private bool doSave = false;

		void OnEnable()
		{
			EditorApplication.update += DoDelayedSave;
		}

		void OnDestroy()
		{
			EditorApplication.update -= DoDelayedSave;
		}

		void DoDelayedSave()
		{
			if (doSave && Time.realtimeSinceStartup > (lastActionTime + 0.5f))
			{
				doSave = false;
				Debug.Log("Saved SlotDataAsset lastActionTime = " + lastActionTime + " realTime = " + Time.realtimeSinceStartup);
				lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssets();
			}
		}
		public override void OnInspectorGUI()
        {
			if (lastActionTime == 0)
				lastActionTime = Time.realtimeSinceStartup;

			EditorGUI.BeginChangeCheck();
			base.OnInspectorGUI();
			if (EditorGUI.EndChangeCheck())
			{
				lastActionTime = Time.realtimeSinceStartup;
				doSave = true;
			}

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
						//DelayedSave here too?
						EditorUtility.SetDirty(slotDataAsset);
						AssetDatabase.SaveAssets();
					}
				}
			}

            GUILayout.Space(20);
            Rect updateDropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(updateDropArea, "Drag SkinnedMeshRenderers here to update the slot meshData.");
            GUILayout.Space(10);
            UpdateSlotDropAreaGUI(updateDropArea);

			GUILayout.Space(10);
			Rect boneDropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
			GUI.Box(boneDropArea, "Drag Bone Transforms here to add their names to the Animated Bone Names.\nSo the power tools will preserve them!");
			GUILayout.Space(10);
			AnimatedBoneDropAreaGUI(boneDropArea);
        }

        private void AnimatedBoneDropAreaGUI(Rect dropArea)
        {
            GameObject obj = DropAreaGUI(dropArea);
            if (obj != null)
                AddAnimatedBone(obj.name);
        }

        private void UpdateSlotDropAreaGUI(Rect dropArea)
        {
            GameObject obj = DropAreaGUI(dropArea);
            if (obj != null)
            {
                SkinnedMeshRenderer skinnedMesh = obj.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMesh != null)
                {
                    Debug.Log("Updating SlotDataAsset with SkinnedMeshRenderer...");
                    UpdateSlotData(skinnedMesh);
                    Debug.Log("Update Complete!");
                }
                else
                    EditorUtility.DisplayDialog("Error", "No skinned mesh renderer found!", "Ok");
            }
                
        }

        private GameObject DropAreaGUI(Rect dropArea)
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
                                return go;
							}
						}
					}
					AssetDatabase.SaveAssets();
				}
			}
            return null;
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

        private void UpdateSlotData(SkinnedMeshRenderer skinnedMesh)
        {
            SlotDataAsset slot = target as SlotDataAsset;

            string existingRootBone = slot.meshData.RootBoneName;

            UMASlotProcessingUtil.UpdateSlotData(slot, skinnedMesh, slot.material, null, existingRootBone);
        }
    }
}
#endif
