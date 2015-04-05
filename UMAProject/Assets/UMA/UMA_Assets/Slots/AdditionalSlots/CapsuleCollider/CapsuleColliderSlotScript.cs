using UnityEngine;
using System.Collections;

namespace UMA
{
	/// <summary>
	/// Auxillary slot which adds a CapsuleCollider and Rigidbody to a newly created character.
	/// </summary>
	public class CapsuleColliderSlotScript : MonoBehaviour 
	{
		public void OnDnaApplied(UMAData umaData)
		{
			var umaDna = umaData.GetDna<UMADnaHumanoid>();
			if (umaDna == null)
			{
				Debug.LogError("Failed to add Capsule Collider to: " + umaData.name);
				return;
			}

			var rigid = umaData.gameObject.GetComponent<Rigidbody>();
			if (rigid == null)
			{
				rigid = umaData.gameObject.AddComponent<Rigidbody>();
			}
			rigid.constraints = RigidbodyConstraints.FreezeRotation;
			rigid.mass = umaData.characterMass;

			var capsule = umaData.gameObject.GetComponent<CapsuleCollider>();
			if (capsule == null)
			{
				capsule = umaData.gameObject.AddComponent<CapsuleCollider>();
			}
			capsule.radius = umaData.characterRadius;
			capsule.height = umaData.characterHeight;
			capsule.center = new Vector3(0, capsule.height * 0.5f - 0.04f, 0);
		}
	}
}