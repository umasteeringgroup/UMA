using UnityEngine;
using System.Collections;

namespace UMA.Examples
{
    public class Locomotion : MonoBehaviour
    {

        protected Animator animator;
        public float DirectionDampTime = .25f;

        void Start()
        {
            animator = GetComponent<Animator>();

            if (animator == null) return;
            if (animator.layerCount >= 2)
                animator.SetLayerWeight(1, 1);
        }

        void Update()
        {
            if (animator)
            {
                float h = Input.GetAxis("Horizontal");
                float v = Input.GetAxis("Vertical");

                animator.SetFloat("Speed", h * h + v * v);
                animator.SetFloat("Direction", h, DirectionDampTime, Time.deltaTime);
            }
            else
            {
                animator = GetComponent<Animator>();
            }
        }


        void OnCollisionEnter(Collision collision)
        {
            if (Debug.isDebugBuild)
                Debug.Log(collision.collider.name + ":" + name);
        }
    }
}
