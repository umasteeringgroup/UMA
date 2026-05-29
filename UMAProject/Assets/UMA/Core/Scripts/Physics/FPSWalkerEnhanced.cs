using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using System;

namespace UMA.Dynamics.Examples
{
    [RequireComponent(typeof(CharacterController))]
    public class FPSWalkerEnhanced : MonoBehaviour
    {
        private UMAPlayerActions controls;

        private bool runPressed;
        private bool crouchPressed;

        [Header("Speeds")]
        public float walkSpeed = 6.0f;
        public float runSpeed = 11.0f;
        public float crouchSpeedMultiplier = 0.5f;

        [Header("Movement")]
        public bool limitDiagonalSpeed = true;
        public float accelTime = 0.08f;
        public float decelTime = 0.12f;
        public float airControl = 0.2f;

        [Header("Run & Jump")]
        public bool toggleRun = false;
        public float jumpSpeed = 8.0f;
        public float gravity = 20.0f;
        public float terminalVelocity = 55.0f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.1f;
        public float fallingDamageThreshold = 10.0f;

        [Header("Slopes & Sliding")]
        public bool slideWhenOverSlopeLimit = false;
        public bool slideOnTaggedObjects = false;
        public float slideSpeed = 12.0f;
        public float antiBumpFactor = .75f;

        [Header("Anti-bhop (legacy)")]
        public int antiBunnyHopFactor = 1;

        [Header("Crouch")]
        public bool enableCrouch = true;
        public bool toggleCrouch = false;
        public float crouchHeight = 1.0f;
        public float crouchTransitionTime = 0.1f;

        [Header("Camera FX (optional)")]
        public Camera playerCamera;
        public float sprintFOVKick = 6f;
        public float fovLerpTime = 0.2f;
        public float headBobAmplitude = 0.02f;
        public float headBobFrequency = 10f;
        public float stepInterval = 2.2f;

        [Header("Events")]
        public UnityEvent OnJump;
        public UnityEvent OnLanded;
        [Serializable] public class FloatEvent : UnityEvent<float> { }
        public FloatEvent OnFallDamage;
        public UnityEvent OnFootstep;
        public UnityEvent OnCrouchStart;
        public UnityEvent OnCrouchEnd;

        // Private state
        private Vector3 moveVelocity;
        private float verticalVelocity;
        private bool grounded = false;
        private CharacterController controller;
        private Transform myTransform;
        private float speed;
        private RaycastHit hit;
        private float fallStartLevel;
        private bool falling;
        private float slideLimit;
        private float rayDistance;
        private Vector3 contactPoint;
        private int jumpTimer;
        private float lastGroundedTime;
        private float lastJumpPressedTime;
        private bool isCrouching;
        private float standingHeight;
        private Vector3 standingCenter;
        private float baseFOV;
        private Vector3 camLocalBase;
        private float headBobTimer;
        private float stepCycle;

        // -------------------------
        // NEW INPUT SYSTEM SETUP
        // -------------------------
        void Awake()
        {
            controls = new UMAPlayerActions();

            controls.Player.Run.performed += ctx => runPressed = true;
            controls.Player.Crouch.performed += ctx => crouchPressed = true;

            //controls.Player.Shoot.performed += ctx => Debug.Log("Shoot action performed!");


            controls.Player.Jump.performed += ctx =>
            {
                lastJumpPressedTime = Time.time;
            };
        }

        void OnEnable() => controls.Enable();
        void OnDisable() => controls.Disable();

        void Start()
        {
            controller = GetComponent<CharacterController>();
            myTransform = transform;
            speed = walkSpeed;
            rayDistance = controller.height * .5f + controller.radius;
            slideLimit = controller.slopeLimit - .1f;
            jumpTimer = antiBunnyHopFactor; 
            standingHeight = controller.height;
            standingCenter = controller.center;

            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>();

            if (playerCamera != null)
            {
                baseFOV = playerCamera.fieldOfView;
                camLocalBase = playerCamera.transform.localPosition;
            }
        }

        // -------------------------------------------------------
        // FULL NEW-INPUT-SYSTEM FIXEDUPDATE
        // -------------------------------------------------------
        void FixedUpdate()
        {
            Vector2 currentMoveInput = controls.Player.Move.ReadValue<Vector2>();
            bool runHeld = controls.Player.Run.ReadValue<float>() > 0.5f;
            float inputX = currentMoveInput.x;
            float inputY = currentMoveInput.y;
            float inputModifyFactor =
                (inputX != 0 && inputY != 0 && limitDiagonalSpeed) ? 0.7071f : 1f;

            bool sliding = false;

            if (grounded)
            {
                if (Physics.Raycast(myTransform.position, Vector3.down, out hit, rayDistance))
                {
                    if (Vector3.Angle(hit.normal, Vector3.up) > slideLimit) sliding = true;
                }
                else
                {
                    Physics.Raycast(contactPoint + Vector3.up, Vector3.down, out hit);
                    if (Vector3.Angle(hit.normal, Vector3.up) > slideLimit) sliding = true;
                }

                if (falling)
                {
                    falling = false;
                    float fallDistance = fallStartLevel - myTransform.position.y;
                    if (fallDistance > fallingDamageThreshold)
                        FallingDamageAlert(fallDistance);

                    OnLanded?.Invoke();
                }

                if (!toggleRun)
                    speed = runHeld ? runSpeed : walkSpeed;

                Vector3 inputDirLocal = new Vector3(inputX * inputModifyFactor, 0f, inputY * inputModifyFactor);
                Vector3 inputDirWorld = playerCamera != null
                    ? (playerCamera.transform.right * inputX) + (playerCamera.transform.forward * inputY)
                    : myTransform.TransformDirection(inputDirLocal);

                inputDirWorld.y = 0f;
                inputDirWorld.Normalize();


                float targetSpeed = speed * (isCrouching ? crouchSpeedMultiplier : 1f);

                Vector3 targetVel = inputDirWorld * (inputDirLocal.sqrMagnitude > 0f ? targetSpeed : 0f);

                float t = (targetVel.sqrMagnitude > 0.001f) ? accelTime : decelTime;
                float accel = (t <= 0.0001f) ? float.PositiveInfinity : (1f / t);
                moveVelocity = Vector3.MoveTowards(moveVelocity, targetVel, accel * Time.fixedDeltaTime * targetSpeed);

                if ((sliding && slideWhenOverSlopeLimit) ||
                    (slideOnTaggedObjects && hit.collider != null && hit.collider.CompareTag("Slide")))
                {
                    Vector3 hitNormal = hit.normal;
                    Vector3 slideDir = new Vector3(hitNormal.x, -hitNormal.y, hitNormal.z);
                    Vector3.OrthoNormalize(ref hitNormal, ref slideDir);
                    moveVelocity = slideDir * slideSpeed;
                }

                bool jumpHeld = controls.Player.Jump.ReadValue<float>() > 0.5f;
                if (!jumpHeld)
                    jumpTimer++;

                bool canJump =
                    (Time.time - lastGroundedTime) <= coyoteTime &&
                    (Time.time - lastJumpPressedTime) <= jumpBufferTime &&
                    jumpTimer >= antiBunnyHopFactor;

                if (canJump)
                {
                    verticalVelocity = jumpSpeed;
                    jumpTimer = 0;
                    lastJumpPressedTime = -999f;
                    OnJump?.Invoke();
                }
                else if (verticalVelocity < 0f)
                {
                    verticalVelocity = -antiBumpFactor;
                }
            }
            else
            {
                if (!falling)
                {
                    falling = true;
                    fallStartLevel = myTransform.position.y;
                }

                if (airControl > 0f)
                {
                    Vector3 inputDirLocal = new Vector3(inputX * inputModifyFactor, 0f, inputY * inputModifyFactor);
                    Vector3 inputDirWorld = myTransform.TransformDirection(inputDirLocal).normalized;
                    Vector3 desired = inputDirWorld * (inputDirLocal.sqrMagnitude > 0f ? speed : 0f);
                    moveVelocity = Vector3.Lerp(moveVelocity, desired, airControl * Time.fixedDeltaTime);
                }
            }

            verticalVelocity -= gravity * Time.fixedDeltaTime;
            if (verticalVelocity < -terminalVelocity)
                verticalVelocity = -terminalVelocity;

            Vector3 motion = new Vector3(moveVelocity.x, verticalVelocity, moveVelocity.z) * Time.fixedDeltaTime;
            CollisionFlags flags = controller.Move(motion);

            bool wasGrounded = grounded;
            grounded = (flags & CollisionFlags.Below) != 0 || controller.isGrounded;

            if (grounded)
            {
                lastGroundedTime = Time.time;
                if (!wasGrounded && verticalVelocity < 0f)
                    verticalVelocity = -antiBumpFactor;
            }
        }

        // -------------------------------------------------------
        // UPDATE (new input system)
        // -------------------------------------------------------
        void Update()
        {
            bool crouchHeld = controls.Player.Crouch.ReadValue<float>() > 0.5f;

            if (toggleRun && grounded && runPressed)
            {
                speed = Mathf.Approximately(speed, walkSpeed) ? runSpeed : walkSpeed;
                runPressed = false;
            }

            if (enableCrouch)
            {
                if (toggleCrouch)
                {
                    if (crouchPressed)
                    {
                        SetCrouch(!isCrouching);
                        crouchPressed = false;
                    }
                }
                else
                {
                    SetCrouch(crouchHeld);
                }
            }

            if (playerCamera != null)
            {
                float targetFOV = baseFOV +
                    ((Mathf.Approximately(speed, runSpeed) && !isCrouching) ? sprintFOVKick : 0f);

                playerCamera.fieldOfView =
                    Mathf.Lerp(playerCamera.fieldOfView, targetFOV,
                    Time.deltaTime / Mathf.Max(0.0001f, fovLerpTime));

                Vector3 camLocal = camLocalBase;
                Vector2 horizVel = new Vector2(moveVelocity.x, moveVelocity.z);
                float moveAmount = Mathf.Clamp01(horizVel.magnitude / Mathf.Max(0.01f, runSpeed));

                if (grounded && moveAmount > 0.01f)
                {
                    headBobTimer += Time.deltaTime *
                        Mathf.Lerp(headBobFrequency * 0.6f, headBobFrequency, moveAmount);

                    float bob = Mathf.Sin(headBobTimer * Mathf.PI * 2f) *
                        headBobAmplitude * moveAmount * (isCrouching ? 0.5f : 1f);

                    camLocal.y += bob;

                    if (OnFootstep != null)
                    {
                        stepCycle += horizVel.magnitude * Time.deltaTime;
                        if (stepCycle >= stepInterval)
                        {
                            stepCycle = 0f;
                            OnFootstep.Invoke();
                        }
                    }
                }
                else
                {
                    headBobTimer = 0f;
                    stepCycle = 0f;
                }

                playerCamera.transform.localPosition =
                    Vector3.Lerp(playerCamera.transform.localPosition, camLocal, 0.25f);
            }
        }

        void SetCrouch(bool crouch)
        {
            if (!enableCrouch) return;
            if (crouch == isCrouching) return;

            isCrouching = crouch;

            float targetHeight = isCrouching ? crouchHeight : standingHeight;
            Vector3 targetCenter = isCrouching
                ? new Vector3(standingCenter.x, crouchHeight * 0.5f, standingCenter.z)
                : standingCenter;

            StopAllCoroutines();
            StartCoroutine(CrouchRoutine(targetHeight, targetCenter));

            if (isCrouching) OnCrouchStart?.Invoke();
            else OnCrouchEnd?.Invoke();
        }

        System.Collections.IEnumerator CrouchRoutine(float targetHeight, Vector3 targetCenter)
        {
            float startHeight = controller.height;
            Vector3 startCenter = controller.center;
            float t = 0f;
            float duration = Mathf.Max(0.0001f, crouchTransitionTime);

            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                controller.height = Mathf.Lerp(startHeight, targetHeight, t);
                controller.center = Vector3.Lerp(startCenter, targetCenter, t);
                yield return null;
            }

            controller.height = targetHeight;
            controller.center = targetCenter;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            contactPoint = hit.point;
        }

        void FallingDamageAlert(float fallDistance)
        {
            OnFallDamage?.Invoke(fallDistance);
            if (OnFallDamage == null)
                Debug.Log("Ouch! Fell " + fallDistance + " units!");
        }
    }
}
