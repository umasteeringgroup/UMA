using UnityEngine;
using System.Collections;

public class Locomotion : MonoBehaviour {

	protected Animator animator;
	public float DirectionDampTime = .25f;
	public bool ApplyGravity = true; 
	private float m_VerticalSpeed = 0;

	void Start () 
	{
		animator = GetComponent<Animator>();
		
		if(animator.layerCount >= 2)
			animator.SetLayerWeight(1, 1);
	}
		
	void Update () 
	{
		if (animator)
		{		
      		float h = Input.GetAxis("Horizontal");
        	float v = Input.GetAxis("Vertical");
			
			animator.SetFloat("Speed", h*h+v*v);
            animator.SetFloat("Direction", h, DirectionDampTime, Time.deltaTime);	
		}else{
			animator = GetComponent<Animator>();
		}
	}

	void OnAvatarMove()
	{
		CharacterController controller = GetComponent<CharacterController>();

		if (controller && animator)
		{

			Vector3 deltaPosition = animator.deltaPosition;
			if(ApplyGravity)
			{			
				m_VerticalSpeed += Physics.gravity.y * Time.deltaTime;						
				deltaPosition.y = m_VerticalSpeed * Time.deltaTime;
			}
			if (controller.Move(deltaPosition) == CollisionFlags.Below) m_VerticalSpeed = 0;			
			transform.rotation = animator.rootRotation;
		}
	}     
}
