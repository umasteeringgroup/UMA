using UnityEngine;
using UnityEngine.InputSystem;

namespace UMA.Dynamics.Examples
{
    [AddComponentMenu("Camera-Control/Smooth Mouse Look")]
    [DisallowMultipleComponent]
    public class SmoothMouseLook : MonoBehaviour
    {
        public enum RotationAxes { MouseXAndY = 0, MouseX = 1, MouseY = 2 }

        [Header("Mode")]
        public RotationAxes axes = RotationAxes.MouseXAndY;

        [Header("Sensitivity")]
        public float sensitivityX = 150f;
        public float sensitivityY = 150f;
        public bool useDeltaTime = true;
        public bool invertY = false;

        [Header("Limits (degrees)")]
        public float minimumX = -360f;
        public float maximumX = 360f;
        public float minimumY = -60f;
        public float maximumY = 60f;

        [Header("Smoothing (seconds)")]
        public bool enableSmoothing = true;
        public float smoothTimeX = 0.05f;
        public float smoothTimeY = 0.05f;

        [Header("Cursor")]
        public bool lockAndHideCursor = false;

        // Input System
        private UMAPlayerActions controls;
        private Vector2 lookInput;
        private CursorLock cursorLock;

        // State
        private float targetYaw;
        private float targetPitch;
        private float currentYaw;
        private float currentPitch;
        private float yawVelocity;
        private float pitchVelocity;

        private Quaternion originalRotation;
        private Quaternion parentRotation;
        private Transform parentTransform;

        private void Awake()
        {
            controls = new UMAPlayerActions();
            cursorLock = FindFirstObjectByType<CursorLock>(
                FindObjectsInactive.Exclude);

            controls.Player.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
            controls.Player.Look.canceled += ctx => lookInput = Vector2.zero;
        }

        private void OnEnable()
        {
            controls.Enable();

            originalRotation = transform.localRotation;
            parentTransform = transform.parent;
            parentRotation = parentTransform != null ? parentTransform.localRotation : Quaternion.identity;

            currentYaw = targetYaw = 0f;
            currentPitch = targetPitch = 0f;

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
                rb.freezeRotation = true;

            if (lockAndHideCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            controls.Disable();

            if (lockAndHideCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            if (cursorLock != null && !cursorLock.IsMouseCaptured)
            {
                lookInput = Vector2.zero;
                return;
            }

            //Debug.Log($"Look input: {lookInput}");

            float dt = useDeltaTime ? Time.deltaTime : 1f;

            float mouseX = lookInput.x;
            float mouseY = lookInput.y;
            if (invertY) mouseY = -mouseY;

            switch (axes)
            {
                case RotationAxes.MouseXAndY:
                    targetYaw += mouseX * sensitivityX * dt;
                    targetPitch += mouseY * sensitivityY * dt;
                    break;

                case RotationAxes.MouseX:
                    targetYaw += mouseX * sensitivityX * dt;
                    break;

                case RotationAxes.MouseY:
                    targetPitch += mouseY * sensitivityY * dt;
                    break;
            }

            targetYaw = ClampAngle(targetYaw, minimumX, maximumX);
            targetPitch = ClampAngle(targetPitch, minimumY, maximumY);

            if (enableSmoothing)
            {
                currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, smoothTimeX, Mathf.Infinity, Time.deltaTime);
                currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref pitchVelocity, smoothTimeY, Mathf.Infinity, Time.deltaTime);
            }
            else
            {
                yawVelocity = 0f;
                pitchVelocity = 0f;
                currentYaw = targetYaw;
                currentPitch = targetPitch;
            }

            Quaternion yQuaternion = Quaternion.AngleAxis(currentPitch, Vector3.left);
            Quaternion xQuaternion = Quaternion.AngleAxis(currentYaw, Vector3.up);

            if (axes == RotationAxes.MouseX)
            {
                if (parentTransform != null)
                    parentTransform.localRotation = parentRotation * xQuaternion;
                else
                    transform.localRotation = originalRotation * xQuaternion;

                return;
            }

            if (axes == RotationAxes.MouseY)
            {
                transform.localRotation = originalRotation * yQuaternion;
                return;
            }

            // MouseXAndY
            transform.localRotation = originalRotation * xQuaternion * yQuaternion;
        }

        public static float ClampAngle(float angle, float min, float max)
        {
            angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
            if (max - min >= 360f) return angle;
            return Mathf.Clamp(angle, min, max);
        }
    }
}
