using UnityEngine;
using UMA;
using UMA.CharacterSystem;
using System;

namespace UMA.Examples
{
	public class UMASimpleLOD : MonoBehaviour
	{
		public float lodDistance;

		[Tooltip("Look for LOD slots in the library.")]
		public bool swapSlots;
		[Tooltip("This value is subtracted from the slot LOD counter.")]
		public int lodOffset;
		[Tooltip("This is the max LOD to search for if the current LOD can't be found.")]
		public int maxLOD = 5;
      [Tooltip("The maximum scale reduction (8 means the texture can be reduced in half 8 times)")]
      public int maxReduction = 8;

      public int CurrentLOD {  get { return _currentLOD - lodOffset; } }
		private int _currentLOD = -1;

		private DynamicCharacterAvatar _avatar;
		private UMAData _umaData;
		private Transform _cameraTransform;

		private bool initialized = false;

		public void SetSwapSlots(bool swapSlots, int lodOffset)
		{
			this.lodOffset = lodOffset;
			this.swapSlots = swapSlots;
			bool changedSlots = ProcessRecipe(_currentLOD);
			if (changedSlots)
			{
				//var renderer = lodDisplay.GetComponent<Renderer>();
				//renderer.material.SetColor("_EmissionColor", Color.grey);
				_umaData.Dirty(true, true, true);
			}
		}

		public void Awake()
		{
			_currentLOD = -1;
      }

		public void OnEnable()
		{
			_avatar = GetComponent<DynamicCharacterAvatar>();
			if (_avatar != null)
			{
				_avatar.CharacterBegun.AddListener(CharacterBegun);
			}
			else
			{
				_umaData = GetComponent<UMAData>();
				if (_umaData != null)
					_umaData.CharacterCreated.AddListener(CharacterCreated);
			}

			//cache the camera transform for performance
			_cameraTransform = Camera.main.transform;
		}

		public void CharacterCreated(UMAData umaData)
		{
			initialized = true;
		}

		public void CharacterBegun(UMAData umaData)
		{
			initialized = true;
			PerformLodCheck();
		}

		public void Update()
		{
            if (!initialized)
                return;

            PerformLodCheck();
		}

		private void PerformLodCheck()
		{
			if (_umaData == null)
				_umaData = gameObject.GetComponent<UMAData>();

			if (_umaData == null)
				return;

			if (_umaData.umaRecipe == null)
				return;

			float cameraDistance = (transform.position - _cameraTransform.position).magnitude;
			float lodDistanceStep = lodDistance;
			float atlasResolutionScale = 1f;

			int currentLevel = 0;
         float maxReductionf = 1.0f / maxReduction;

         while (lodDistance != 0 && cameraDistance > lodDistanceStep)
			{
				lodDistanceStep *= 2;
				atlasResolutionScale *= 0.5f;
				++currentLevel;
			}
			_currentLOD = currentLevel;

         if (atlasResolutionScale < maxReductionf)
         {
            atlasResolutionScale = maxReductionf;
         }

         if (_umaData.atlasResolutionScale != atlasResolutionScale)
			{
				_umaData.atlasResolutionScale = atlasResolutionScale;
				bool changedSlots = ProcessRecipe(currentLevel);
				_umaData.Dirty(changedSlots, true, changedSlots);
			}
			else
			{
				if(_umaData.isMeshDirty)
				{
					ProcessRecipe(currentLevel);
				}
			}
		}

		private bool ProcessRecipe(int currentLevel)
		{
			bool changedSlots = false;

			if (_umaData.umaRecipe.slotDataList == null)
				return false;

			for (int i = 0; i < _umaData.umaRecipe.slotDataList.Length; i++)
			{
				var slot = _umaData.umaRecipe.slotDataList[i];
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

					bool slotFound = false;
					for (int k = (currentLevel - lodOffset); k >= 0; k--)
					{
						if (slotName != slot.slotName && UMAContext.Instance.HasSlot(slotName))
						{
							_umaData.umaRecipe.slotDataList[i] = UMAContext.Instance.InstantiateSlot(slotName, slot.GetOverlayList());
							slotFound = true;
							changedSlots = true;
							break;
						}
					}
					//If slot still not found when searching down lods, then let's trying searching up lods
					if(!slotFound)
					{
						for(int k = (currentLevel - lodOffset) + 1; k <= maxLOD; k++)
						{
							if (slotName != slot.slotName && UMAContext.Instance.HasSlot(slotName))
							{
								_umaData.umaRecipe.slotDataList[i] = UMAContext.Instance.InstantiateSlot(slotName, slot.GetOverlayList());
								slotFound = true;
								changedSlots = true;
								break;
							}
						}
					}
				}
			}

            //Reprocess mesh hide assets
            //Eventually, make this a function in DCA (UpdateMeshHideMasks) and replace correspond code in DCA.LoadCharacter too
            if (_avatar != null && changedSlots)
            {
                foreach (SlotData sd in _umaData.umaRecipe.slotDataList)
                {
                    if (_avatar.MeshHideDictionary.ContainsKey(sd.asset))
                    {   //If this slotDataAsset is found in the MeshHideDictionary then we need to supply the SlotData with the bitArray.
                        sd.meshHideMask = MeshHideAsset.GenerateMask(_avatar.MeshHideDictionary[sd.asset]);
                    }
                }
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_umaData);
#endif
			return changedSlots;
		}
	}
}