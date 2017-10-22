using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class SimplePlayerMove : NetworkBehaviour 
{	
    [SerializeField] float _movingTurnSpeed = 360;
    [SerializeField] float _stationaryTurnSpeed = 180;

    private float _forwardAmount;
    private float _turnAmount;

    Rigidbody m_Rigidbody;
    Animator m_Animator;

    void Start()
    {
        m_Animator = GetComponent<Animator>();
        m_Rigidbody = GetComponent<Rigidbody>();
    }

	void FixedUpdate () 
    {
        if (!isLocalPlayer)
            return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        if (v < 0) { v = 0; }

        Vector3 move = v*transform.forward + h*transform.right;

        if (move.magnitude > 1f) move.Normalize();
        move = transform.InverseTransformDirection(move);
        move = Vector3.ProjectOnPlane(move, Vector3.up);
        _turnAmount = Mathf.Atan2(move.x, move.z);
        _forwardAmount = move.z;

        ApplyExtraTurnRotation();

        // send input and other state parameters to the animator
        UpdateAnimator(move);
	}

    void ApplyExtraTurnRotation()
    {
        // help the character turn faster (this is in addition to root rotation in the animation)
        float turnSpeed = Mathf.Lerp(_stationaryTurnSpeed, _movingTurnSpeed, _forwardAmount);
        transform.Rotate(0, _turnAmount * turnSpeed * Time.deltaTime, 0);
    }

    void UpdateAnimator(Vector3 move)
    {
        // update the animator parameters
        m_Animator.SetFloat("Forward", _forwardAmount, 0.1f, Time.deltaTime);
        m_Animator.SetFloat("Turn", _turnAmount, 0.1f, Time.deltaTime);
    }

    public void OnAnimatorMove()
    {
        // we implement this function to override the default root motion.
        // this allows us to modify the positional speed before it's applied.
        if (Time.deltaTime > 0)
        {
            Vector3 v = (m_Animator.deltaPosition) / Time.deltaTime;

            // we preserve the existing y part of the current velocity.
            v.y = m_Rigidbody.velocity.y;
            m_Rigidbody.velocity = v;
        }
    }
}
