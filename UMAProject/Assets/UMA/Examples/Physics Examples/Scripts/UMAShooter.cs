using UnityEngine;
using System.Collections;
using UMA.Dynamics;

namespace UMA.Dynamics.Examples
{
	public class UMAShooter : MonoBehaviour
	{
		//Declare a member variables for distributing the impacts over several frames
		float impactEndTime=0;
		int hits = 0;
		Rigidbody impactTarget=null;
		Vector3 impact;
		public Camera currentCamera;
		public LayerMask layers;
		public AudioClip Bang;
		public float announcerDelay = 0.5f;

		public AudioClip KillingSpree;
		public AudioClip HeadShot;
		public AudioClip HadToHurt;
		public GameObject Blood;


		// Update is called once per frame
		void Update () {
			//if left mouse button clicked
			if (Input.GetKeyDown(KeyCode.Escape))
            {
				UMAPhysicsAvatar[] components = GameObject.FindObjectsOfType<UMAPhysicsAvatar>();
				foreach(var player in components)
                {
					if (player.ragdolled)
                    {
						player.ragdolled = false;
					}
                }
			}
			if (Input.GetMouseButtonDown(0))
			{
				AudioSource src = gameObject.GetComponent<AudioSource>();
				if (src != null)
                {
					src.PlayOneShot(Bang);
                }
				//Get a ray going from the camera through the mouse cursor
				Ray ray = currentCamera.ScreenPointToRay (new Vector3(Screen.width/2,Screen.height/2,0));

				
				//check if the ray hits a physic collider
				RaycastHit hit; //a local variable that will receive the hit info from the Raycast call below
				if (Physics.Raycast(ray,out hit, 100f, layers))
				{
					//check if the raycast target has a rigid body (belongs to the ragdoll)
					if (hit.rigidbody!=null)
					{
						
						Transform avatar = hit.rigidbody.transform.root; // this need to search more intelligently, only works 
						//find the RagdollHelper component and activate ragdolling
						UMAPhysicsAvatar player = avatar.GetComponent<UMAPhysicsAvatar>();
						//if(player == null)
						//	player = avatar.GetComponentInChildren<RagdollPlayer>();
						if(player)
                        {
							if (Blood != null)
							{
								GameObject bloodEmitter = GameObject.Instantiate(Blood, hit.point, Quaternion.identity);
							}
							if (!player.ragdolled)
                            {
								hits++;
								if (hits == 5)
								{
									StartCoroutine(PlayHit(KillingSpree));
								}
								else
								{
									AnnounceHit(hit);
								}
							}
							player.ragdolled = true;
                        }

                        //set the impact target to whatever the ray hit
                        impactTarget = hit.rigidbody;
						
						//impact direction also according to the ray
						impact = ray.direction * 2.0f;
						
						//impactTarget.AddForce(impact,ForceMode.VelocityChange);
						
						//the impact will be reapplied for the next 100ms
						//to make the connected objects follow even though the simulated body joints
						//might stretch
						impactEndTime=Time.time+0.1f;
					}
				}
			}
			
			if (Input.GetMouseButtonDown(1))
			{
				//Get a ray going from the camera through the mouse cursor
				Ray ray = currentCamera.ScreenPointToRay (new Vector3(Screen.width/2,Screen.height/2,0));
				
				//check if the ray hits a physic collider
				RaycastHit hit; //a local variable that will receive the hit info from the Raycast call below
				if (Physics.Raycast(ray,out hit, 100f, layers))
				{
					//check if the raycast target has a rigid body (belongs to the ragdoll)
					if (hit.rigidbody!=null)
					{
						
						Transform avatar = hit.rigidbody.transform.root; // this need to search more intelligently, only works 
						//find the RagdollHelper component and activate ragdolling
						UMAPhysicsAvatar player = avatar.GetComponent<UMAPhysicsAvatar>();
						if(player == null)
							player = avatar.GetComponentInChildren<UMAPhysicsAvatar>();
						
						if(player)
							player.ragdolled=false;
					}
				}
				
			}
			
			if (Input.GetMouseButtonDown(2))
			{
				//Get a ray going from the camera through the mouse cursor
				Ray ray = currentCamera.ScreenPointToRay (new Vector3(Screen.width/2,Screen.height/2,0));
				
				//check if the ray hits a physic collider
				RaycastHit hit; //a local variable that will receive the hit info from the Raycast call below
				if (Physics.Raycast(ray,out hit, 100f, layers))
				{
					//check if the raycast target has a rigid body (belongs to the ragdoll)
					if (hit.rigidbody!=null)
					{
						Transform avatar = hit.rigidbody.transform.root; // this need to search more intelligently, only works 
						//find the RagdollHelper component and activate ragdolling
						UMAPhysicsAvatar player = avatar.GetComponent<UMAPhysicsAvatar>();
						if(player == null)
							player = avatar.GetComponentInChildren<UMAPhysicsAvatar>();
						if(player)
						{
							StartCoroutine(TimedRagdoll(hit));
						}
						
						//set the impact target to whatever the ray hit
						impactTarget = hit.rigidbody;
						
						//impact direction also according to the ray
						impact = ray.direction * 1.0f;
						
						//impactTarget.AddForce(impact,ForceMode.VelocityChange);
						
						//the impact will be reapplied for the next 100ms
						//to make the connected objects follow even though the simulated body joints
						//might stretch
						impactEndTime=Time.time+0.1f;
					}
				}
			}
			
			//Check if we need to apply an impact
			if (Time.time<impactEndTime)
			{
				impactTarget.AddForce(impact,ForceMode.VelocityChange);
			}
		}

        private RaycastHit AnnounceHit(RaycastHit hit)
        {
            if (hit.rigidbody != null)
            {
                if (hit.rigidbody.gameObject.name.ToLower() == "head")
                {
					StartCoroutine(PlayHit(HeadShot));
                }
				if (hit.rigidbody.gameObject.name.ToLower() == "hips")
				{
					StartCoroutine(PlayHit(HadToHurt));
				}
			}
			return hit;
        }

		IEnumerator PlayHit(AudioClip clip)
        {
			yield return new WaitForSeconds(announcerDelay);
			AudioSource src = gameObject.GetComponent<AudioSource>();
			if (src != null)
			{
				src.PlayOneShot(clip);
			}
		}


		IEnumerator TimedRagdoll(RaycastHit hit)
		{
			Transform avatar = hit.rigidbody.transform.root; 			
			UMAPhysicsAvatar player = avatar.GetComponent<UMAPhysicsAvatar>();
			player.ragdolled=true;
			yield return new WaitForSeconds(0.1f);
			player.ragdolled=false;
		}
	}
}
