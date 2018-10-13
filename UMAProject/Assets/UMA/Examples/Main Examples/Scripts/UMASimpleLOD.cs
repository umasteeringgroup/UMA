using UnityEngine;
using UMA;
using System;

namespace UMA.Examples
{
	public class UMASimpleLOD : MonoBehaviour
	{
		public UMAData umaData;
		public float lodDistance;
		public TextMesh lodDisplay;
		private int lodLevel;
		[Tooltip("Look for LOD slots in the library.")]
		public bool swapSlots;
		[Tooltip("This value is subtracted from the slot LOD counter.")]
		public int lodOffset;

		public void SetSwapSlots(bool swapSlots, int lodOffset)
		{
			this.lodOffset = lodOffset;
			this.swapSlots = swapSlots;
			bool changedSlots = ProcessRecipe(lodLevel);
			if (changedSlots)
			{
				var renderer = lodDisplay.GetComponent<Renderer>();
				renderer.material.SetColor("_EmissionColor", Color.grey);
				umaData.Dirty(true, true, true);
			}
		}

		public void Awake()
		{
			lodLevel = -1;
		}

		public void Update()
		{
			if (umaData == null)
				umaData = gameObject.GetComponent<UMAData>();

			if (umaData == null)
				return;

			float cameraDistance = (transform.position - Camera.main.transform.position).magnitude;
			float lodDistanceStep = lodDistance;
			float atlasResolutionScale = 1f;

			int currentLevel = 0;
			while (lodDistance !=0 && cameraDistance > lodDistanceStep)
			{
				lodDistanceStep *= 2;
				atlasResolutionScale *= 0.5f;
				++currentLevel;
			}

			if (umaData.atlasResolutionScale != atlasResolutionScale)
			{
				umaData.atlasResolutionScale = atlasResolutionScale;
				bool changedSlots = ProcessRecipe(currentLevel);
				umaData.Dirty(changedSlots, true, changedSlots);
			}

			if (lodDisplay != null )
			{
				if (lodLevel != currentLevel)
				{
					lodLevel = currentLevel;
					lodDisplay.text = string.Format("LOD #{0}", lodLevel);
					var renderer = lodDisplay.GetComponent<Renderer>();
					renderer.material.SetColor("_EmissionColor", Color.grey);
				}
				var delta = transform.position-Camera.main.transform.position;
				delta.y = 0;
				lodDisplay.transform.rotation = Quaternion.LookRotation(delta, Vector3.up);
			}
		}

		public void CharacterUpdated(UMAData data)
		{
			if (lodDisplay != null)
			{
				var renderer = lodDisplay.GetComponent<Renderer>();
				renderer.material.SetColor("_EmissionColor", Color.white);
			}
		}

		private bool ProcessRecipe(int currentLevel)
		{
			bool changedSlots = false;

			if (umaData.umaRecipe.slotDataList == null)
				return false;

			for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
			{
				var slot = umaData.umaRecipe.slotDataList[i];
				if (slot != null)
				{
					var slotName = slot.slotName;
					var lodIndex = slotName.IndexOf("_LOD");
					if (lodIndex >= 0)
					{
						slotName = slotName.Substring(0, lodIndex);
					}
					if (currentLevel - lodOffset >= 0 && swapSlots)
					{
						slotName = string.Format("{0}_LOD{1}", slotName, currentLevel - lodOffset);
					}
					if (slotName != slot.slotName && UMAContext.Instance.HasSlot(slotName))
					{
						umaData.umaRecipe.slotDataList[i] = UMAContext.Instance.InstantiateSlot(slotName, slot.GetOverlayList());
						changedSlots = true;
					}
				}
			}
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(umaData);
#endif
			return changedSlots;
		}
	}
}