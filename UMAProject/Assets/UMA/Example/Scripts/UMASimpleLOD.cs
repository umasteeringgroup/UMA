using UnityEngine;
using UMA;

namespace UMA.Examples
{
	public class UMASimpleLOD : MonoBehaviour
	{
		public UMAData umaData;
		public float lodDistance;
		public void Update()
		{
			float cameraDistance = (transform.position - Camera.main.transform.position).magnitude;
			float lodDistanceStep = lodDistance;
			float atlasResolutionScale = 1f;

			while (cameraDistance > lodDistanceStep)
			{
				lodDistanceStep *= 2;
				atlasResolutionScale *= 0.5f;
			}


			if (umaData.atlasResolutionScale != atlasResolutionScale)
			{
				umaData.atlasResolutionScale = atlasResolutionScale;
				umaData.Dirty(false, true, false);
			}
		}
	}
}