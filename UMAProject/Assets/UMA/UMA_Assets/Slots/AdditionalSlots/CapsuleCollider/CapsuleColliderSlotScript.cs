using UnityEngine;
using System.Collections;

namespace UMA
{
	public class CapsuleColliderSlotScript : MonoBehaviour 
	{
		public void OnDnaApplied(UMAData umaData)
		{
			var umaDna = umaData.umaRecipe.GetDna<UMADnaHumanoid>();
			if (umaDna == null)
			{
				Debug.LogError("Failed to add Capsule Collider to: " + umaData.name);
				return;
			}

			var rigid = umaData.umaRoot.AddComponent<Rigidbody>();
			rigid.constraints = RigidbodyConstraints.FreezeRotation;
			rigid.mass = 60f;

			var capsule = umaData.umaRoot.AddComponent<CapsuleCollider>();
			capsule.radius = 0.25f;
			capsule.height = (umaDna.height + 0.5f) * 2f + 0.1f;
			capsule.center = new Vector3(0, capsule.height * 0.5f - 0.04f, 0);
		}
	}
}