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
			var rigid = umaData.gameObject.GetComponent<Rigidbody>();
			if (rigid == null)
			{
				rigid = umaData.gameObject.AddComponent<Rigidbody>();
			}
			rigid.constraints = RigidbodyConstraints.FreezeRotation;
			rigid.mass = umaData.characterMass;

			CapsuleCollider capsule = umaData.gameObject.GetComponent<CapsuleCollider>();
			BoxCollider box = umaData.gameObject.GetComponent<BoxCollider>();

			if(umaData.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid)
			{
				if (capsule == null)
				{
					capsule = umaData.gameObject.AddComponent<CapsuleCollider>();
				}
				if( box != null )
				{
					Destroy(box);
				}

				capsule.radius = umaData.characterRadius;
				capsule.height = umaData.characterHeight;
				capsule.center = new Vector3(0, capsule.height / 2, 0);
			}
			else
			{
				if (box == null)
				{
					box = umaData.gameObject.AddComponent<BoxCollider>();
				}
				if(capsule != null)
				{
					Destroy(capsule);
				}

				//with skycar this capsule collider makes no sense so we need the bounds to figure out what the size of the box collider should be
				//we will assume that renderer 0 is the base renderer
				var umaRenderer = umaData.GetRenderer(0);
				if (umaRenderer != null)
				{
					box.size = umaRenderer.bounds.size;
					box.center = umaRenderer.bounds.center;
				}
			}
		}
	}
}