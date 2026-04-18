using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Auxillary slot which adds a CapsuleCollider and Rigidbody to a newly created character.
    /// </summary>
    public class CapsuleColliderSlotScript : MonoBehaviour
	{
		public bool overrideMass = false;
		public bool overrideConstraints = false;
		public bool overrideDimensions = true;

		public void OnDnaApplied(UMAData umaData)
		{
			var rigid = umaData.gameObject.GetComponent<Rigidbody>();
			if (rigid == null)
			{
				rigid = umaData.gameObject.AddComponent<Rigidbody>();
                rigid.constraints = RigidbodyConstraints.FreezeRotation;
                rigid.mass = umaData.characterMass;
            }

            if ( overrideConstraints)
            {
				rigid.constraints = RigidbodyConstraints.FreezeRotation;
            }
			if (overrideMass)
			{
				rigid.mass = umaData.characterMass;
			}

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
                if (umaData.umaRecipe.raceData.useManualRendererBounds)
                {
					var bounds = umaData.umaRecipe.raceData.manualRendererBounds;

					var skinnedMeshRenderer = umaData.GetRenderer(0) as SkinnedMeshRenderer;
					if (skinnedMeshRenderer != null)
					{
						Bounds smrbounds = skinnedMeshRenderer.bounds;

						//Debug.Log($"SkinnedMeshRenderer Bounds: {smrbounds.size}");
						//Debug.Log($"SkinnedMeshRenderer Bounds Center: {smrbounds.center}");

						Vector3 localCenter = umaData.gameObject.transform.InverseTransformPoint(smrbounds.center);

                        capsule.radius = smrbounds.size.z * 0.6f;
						capsule.height = smrbounds.size.y;
						capsule.center = localCenter;
                        //Debug.Log($"Capsule Collider: {capsule.center} center");
                    }
					else
					{
						capsule.radius = bounds.z * 1.2f;
						capsule.height = bounds.y;
						capsule.center = new Vector3(0, capsule.height / 2, 0);
					}
					//Debug.Log($"Capsule Collider: {capsule.height} height; {capsule.radius} radius; {capsule.center} center");
				}

                if (overrideDimensions)
				{
                    capsule.radius = umaData.characterRadius;
                    capsule.height = umaData.characterHeight;
                    capsule.center = new Vector3(0, capsule.height / 2, 0);
                }
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
					if (overrideDimensions)
					{
                        box.size = umaRenderer.bounds.size;
                        box.center = umaRenderer.bounds.center;
                    }
				}
			}
		}
	}
}