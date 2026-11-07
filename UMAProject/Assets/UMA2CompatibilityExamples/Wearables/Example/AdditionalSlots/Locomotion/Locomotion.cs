using UnityEngine;
using UnityEngine.InputSystem;

namespace UMA.Examples
{
    public class Locomotion : MonoBehaviour
    {

        protected Animator animator;
        private UMAPlayerActions controls;
        public float DirectionDampTime = .25f;

        private void Awake()
        {
            controls = new UMAPlayerActions();
        }

        private void OnEnable()
        {
            controls?.Enable();
        }

        private void OnDisable()
        {
            controls?.Disable();
        }

        private void OnDestroy()
        {
            controls?.Dispose();
            controls = null;
        }

        void Start()
        {
            animator = GetComponent<Animator>();

            if (animator == null)
            {
                return;
            }

            if (animator.layerCount >= 2)
            {
                animator.SetLayerWeight(1, 1);
            }
        }

        void Update()
        {
            if (animator)
            {
                Vector2 move = controls != null ? controls.Player.Move.ReadValue<Vector2>() : Vector2.zero;
                float h = move.x;
                float v = move.y;

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
            {
                Debug.Log(collision.collider.name + ":" + name);
            }
        }
    }
}
