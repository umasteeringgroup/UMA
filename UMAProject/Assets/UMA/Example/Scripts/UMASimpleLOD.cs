using UnityEngine;
using UMA;

namespace UMA.Examples
{
	public class UMASimpleLOD : MonoBehaviour
	{
		public UMAData umaData;
		public float lodDistance;
		public TextMesh lodDisplay;
		private int lodLevel;

		public void Awake()
		{
			lodLevel = -1;
		}

		public void Update()
		{
			float cameraDistance = (transform.position - Camera.main.transform.position).magnitude;
			float lodDistanceStep = lodDistance;
			float atlasResolutionScale = 1f;

			int currentLevel = 0;
			while (cameraDistance > lodDistanceStep)
			{
				lodDistanceStep *= 2;
				atlasResolutionScale *= 0.5f;
				++currentLevel;
			}


			if (umaData.atlasResolutionScale != atlasResolutionScale)
			{
				umaData.atlasResolutionScale = atlasResolutionScale;
				umaData.Dirty(false, true, false);
			}

			if (lodDisplay != null && lodLevel != currentLevel)
			{
				lodLevel = currentLevel;
				lodDisplay.text = "LOD #" + lodLevel.ToString();
			}
		}
	}
}