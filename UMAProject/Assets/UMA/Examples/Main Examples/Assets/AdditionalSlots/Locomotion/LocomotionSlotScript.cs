using UnityEngine;
using System.Collections;
using UMA;

namespace UMA.Examples
{
	/// <summary>
	/// Auxillary slot which adds a Locomotion component to a newly created character.
	/// </summary>
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
