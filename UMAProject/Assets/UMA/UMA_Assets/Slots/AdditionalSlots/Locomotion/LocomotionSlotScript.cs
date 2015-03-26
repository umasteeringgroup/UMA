using UnityEngine;
using System.Collections;

namespace UMA
{
	public class LocomotionSlotScript : MonoBehaviour 
	{
		public void OnDnaApplied(UMAData umaData)
		{
			var locomotion = umaData.GetComponent<Locomotion>();
			if (locomotion == null)
				umaData.gameObject.AddComponent<Locomotion>();
		}
	}
}
