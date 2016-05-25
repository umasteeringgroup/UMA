using UMA;
using UnityEngine;

namespace UMA.Examples
{
	public class UMALODDemoScript : MonoBehaviour
	{
		public int characterCount;
		public float range;
		public float lodDistance;
		private bool isBuilding;
		UMACrowd crowd;
		void Start()
		{
			if (crowd == null)
				crowd = GetComponent<UMACrowd>();
		}

		void Update()
		{
			if (characterCount > 0)
			{
				if (!isBuilding)
				{
					characterCount--;
					isBuilding = true;
					crowd.ResetSpawnPos();
					var go = crowd.GenerateUMA(Random.Range(0, 2), transform.position + new Vector3(Random.Range(-range, range), 0, Random.Range(-range, range)));
					var lod = go.AddComponent<UMASimpleLOD>();
					lod.lodDistance = lodDistance;
					lod.umaData = go.GetComponent<UMAData>();
					lod.Update();
					lod.umaData.CharacterCreated.AddListener(CharacterCreated);
				}
			}
		}

		void CharacterCreated(UMAData umaData)
		{
			isBuilding = false;
		}
	}
}