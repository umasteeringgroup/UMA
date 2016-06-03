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
					go.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

					var lod = go.AddComponent<UMASimpleLOD>();
					lod.lodDistance = lodDistance;
					lod.umaData = go.GetComponent<UMAData>();
					lod.Update();
					lod.umaData.CharacterCreated.AddListener(CharacterCreated);

					// Add the display prefab
					GameObject tm = (GameObject)GameObject.Instantiate(LODDisplayPrefab, go.transform.position, go.transform.rotation);
					tm.transform.SetParent(go.transform);
					tm.transform.localPosition = new Vector3(0, 2f, 0f);
					tm.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

					lod.lodDisplay = tm.GetComponent<TextMesh>();
				}
			}
		}

		void CharacterCreated(UMAData umaData)
		{
			isBuilding = false;
		}
	}
}