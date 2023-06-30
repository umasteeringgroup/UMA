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
		[Tooltip("This value is subtracted from the slot LOD counter.")]
		public int lodOffset = 0;

		UMACrowd crowd;
		void Start()
		{
			if (crowd == null)
				crowd = GetComponent<UMACrowd>();
		}

		void Update()
		{
			// Note: SwapSlots is now taken care of on UMASimpleLOD
			if (characterCount > 0)
			{
				characterCount--;
				crowd.ResetSpawnPos();
				var go = crowd.GenerateUMA(Random.Range(0, 2), transform.position + new Vector3(Random.Range(-range, range), 0, Random.Range(-range, range)));
				go.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

				var lod = go.AddComponent<UMASimpleLOD>();
				var display = go.AddComponent<LODDisplay>();
				display.LODDisplayPrefab = LODDisplayPrefab;

				lod.lodDistance = lodDistance;
				lod.swapSlots = swapSlots;
				lod.lodOffset = lodOffset;
				lod.Update();
			}
		}
	}
}