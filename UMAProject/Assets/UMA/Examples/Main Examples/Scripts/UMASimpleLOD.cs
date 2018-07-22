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

		public int CurrentLOD {  get { return _currentLOD; } }
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
			while (lodDistance != 0 && cameraDistance > lodDistanceStep)
			{
				lodDistanceStep *= 2;
				atlasResolutionScale *= 0.5f;
				++currentLevel;
			}
			_currentLOD = currentLevel;

			if (_umaData.atlasResolutionScale != atlasResolutionScale)
			{
				_umaData.atlasResolutionScale = atlasResolutionScale;
				bool changedSlots = ProcessRecipe(currentLevel);
				_umaData.Dirty(changedSlots, true, changedSlots);
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
					if (slotName != slot.slotName && UMAContext.Instance.HasSlot(slotName))
					{
						_umaData.umaRecipe.slotDataList[i] = UMAContext.Instance.InstantiateSlot(slotName, slot.GetOverlayList());
						changedSlots = true;
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