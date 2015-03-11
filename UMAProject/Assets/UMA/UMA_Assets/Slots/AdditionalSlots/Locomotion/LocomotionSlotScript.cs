using UnityEngine;
using System.Collections;

namespace UMA.PoseTools
{
	public class LocomotionSlotScript : MonoBehaviour 
	{
		public void OnDnaApplied(UMAData umaData)
		{
			umaData.umaRoot.AddComponent<Locomotion>();
		}
	}
}