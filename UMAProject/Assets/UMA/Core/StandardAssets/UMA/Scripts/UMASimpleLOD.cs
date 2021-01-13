using UnityEngine;
using UMA;
using UMA.CharacterSystem;
using System;

namespace UMA.Examples
{
	public class UMASimpleLOD : MonoBehaviour
	{
		[Tooltip("The distance to step to another LOD")]
		[Range(0.01f, 100f)]
		public float lodDistance = 5.0f;

		[Tooltip("Look for LOD slots in the library.")]
		public bool swapSlots;
		[Tooltip("This value is subtracted from the slot LOD counter.")]
		public int lodOffset;
		[Tooltip("This is the max LOD to search for if the current LOD can't be found.")]
		public int maxLOD = 5;
		[Tooltip("The maximum scale reduction (8 means the texture can be reduced in half 8 times)")]
		public int maxReduction = 8;
		[Tooltip("Allow the system to drop slots based on the SlotDataAsset MaxLOD")]
		public bool useSlotDropping;
		[Tooltip("How much of a movement buffer before triggering an LOD change again. This is to stop thrashing at edges 4.99->5.0->4.99, etc")]
		public float BufferZone = 0.5f;


		public int CurrentLOD { get { return _currentLOD - lodOffset; } }
		private int _currentLOD = -1;
		private float lastDist = 0.0f;
		private float NextTime = 0.0f;
		[Tooltip("How much time must pass before this is checked again. Default = 0.5 seconds")]
		public float MinCheck =  0.5f;
		[Tooltip("Random Variance in time (added to MinCheck) so that everything doesn't trigger at the same time. Default = 0.25 seconds")]
		public float CheckRange = 0.25f;

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
			if (!_cameraTransform)
			{
				_cameraTransform = Camera.main.transform;
				if (!_cameraTransform)
				{
					Debug.LogError("Unable to find main camera!!!");
					return;
				}
			}
			initialized = true;
		}

		public void CharacterBegun(UMAData umaData)
		{
			if (!_cameraTransform)
			{
				_cameraTransform = Camera.main.transform;
				if (!_cameraTransform)
				{
					Debug.LogError("Unable to find main camera!!!");
					return;
				}
			}
			initialized = true;
			PerformLodCheck();
		}

		public void Update()
		{

			if (!initialized)
				return;

			if (Time.time > NextTime)
			{
				PerformLodCheck();
				NextTime = Time.time + MinCheck;
				if (CheckRange > 0.0f)
                {
					NextTime += UnityEngine.Random.Range(0.0f, CheckRange);
				}
			}
		}

		private void PerformLodCheck()
		{
			if (_umaData == null)
				_umaData = gameObject.GetComponent<UMAData>();

			if (_umaData == null)
				return;

			if (_umaData.umaRecipe == null)
				return;

			if (lodDistance < 0)
			{ 
				return;
			}

			if (!_cameraTransform)
			{
				_cameraTransform = Camera.main.transform;
				if (!_cameraTransform)
				{
					Debug.LogError("Unable to find main camera!");
					return;
				}
			}

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
			if (_currentLOD != currentLevel)
			{
					lastDist = cameraDistance;
					_currentLOD = currentLevel;
			}

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
				if (_umaData.isMeshDirty)
				{
					if (ProcessRecipe(currentLevel))
					{
						_umaData.Dirty(true, true, true);
					}
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
					if (useSlotDropping)
					{
						// mark the slots as dirty if one is over the limit.
						if (slot.MaxLod > -1 && _currentLOD > slot.MaxLod)
						{
							// Only trigger this the first time, so we only force a rebuild
							// once (or possibly later if slots change...)
							if (!slot.Suppressed)
							{
								changedSlots = true;
							}
							slot.Suppressed = true;
						}
						else
						{
							if (slot.Suppressed)
							{
								changedSlots = true;
							}
							slot.Suppressed = false;
						}
						
					}

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
						if (slotName != slot.slotName && UMAContextBase.Instance.HasSlot(slotName))
						{
							_umaData.umaRecipe.slotDataList[i] = UMAContextBase.Instance.InstantiateSlot(slotName, slot.GetOverlayList());
							slotFound = true;
							changedSlots = true;
							break;
						}
					}
					//If slot still not found when searching down lods, then let's trying searching up lods
					if (!slotFound)
					{
						for (int k = (currentLevel - lodOffset) + 1; k <= maxLOD; k++)
						{
							if (slotName != slot.slotName && UMAContextBase.Instance.HasSlot(slotName))
							{
								_umaData.umaRecipe.slotDataList[i] = UMAContextBase.Instance.InstantiateSlot(slotName, slot.GetOverlayList());
								slotFound = true;
								changedSlots = true;
								break;
							}
						}
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