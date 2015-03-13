using UnityEngine;
using System.Collections;

namespace UMA.PoseTools
{
	public class LocomotionSlotScript : MonoBehaviour 
	{
		public void OnDnaApplied(UMAData umaData)
		{
			var locomotion = umaData.umaRoot.GetComponent<Locomotion>();
			if (locomotion == null)
				umaData.umaRoot.AddComponent<Locomotion>();
		}
	}
}