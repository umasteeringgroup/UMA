using UMA;
using UnityEngine;

namespace UMA.Examples
{
	public class UMALODDemoScript : MonoBehaviour
	{
		public int characterCount;
		public float range;
		public float lodDistance;
		public GameObject LODDisplayPrefab;
		[Tooltip("Look for LOD slots in the library.")]
		public bool swapSlots = true;
		private bool _swapSlots = true;
		[Tooltip("This value is subtracted from the slot LOD counter.")]
		public int lodOffset = 2;
		private int _lodOffset = 2;

		private bool isBuilding;
		UMACrowd crowd;
		void Start()
		{
			if (crowd == null)
				crowd = GetComponent<UMACrowd>();
		}

		void Update()
		{
			if (swapSlots != _swapSlots || lodOffset != _lodOffset)
			{
				_swapSlots = swapSlots;
				_lodOffset = lodOffset;
				var lods = transform.GetComponentsInChildren<UMASimpleLOD>();
				foreach(var lod in lods)
				{
					lod.SetSwapSlots(_swapSlots, _lodOffset);
				}
			}
			if (characterCount > 0)
			{
				if (!isBuilding)
				{
					characterCount--;
					isBuilding = true;
					crowd.ResetSpawnPos();
					var go = crowd.GenerateUMA(Random.Range(0, 2), transform.position + new Vector3(Random.Range(-range, range), 0, Random.Range(-range, range)));
					go.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

					// Add the display prefab
					GameObject tm = (GameObject)GameObject.Instantiate(LODDisplayPrefab, go.transform.position, go.transform.rotation);
					tm.transform.SetParent(go.transform);
					tm.transform.localPosition = new Vector3(0, 2f, 0f);
					tm.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

					var lod = go.AddComponent<UMASimpleLOD>();
					lod.lodDistance = lodDistance;
					lod.lodDisplay = tm.GetComponent<TextMesh>();
					lod.umaData = go.GetComponent<UMAData>();
					lod.umaData.CharacterUpdated.AddListener(lod.CharacterUpdated);
					lod.umaData.CharacterCreated.AddListener(CharacterCreated);
					lod.swapSlots = swapSlots;
					lod.lodOffset = lodOffset;
					lod.Update();
				}
			}
		}

		void CharacterCreated(UMAData umaData)
		{
			isBuilding = false;
		}
	}
}