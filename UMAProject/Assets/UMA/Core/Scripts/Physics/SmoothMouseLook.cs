using UnityEngine;

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
        [Min(0f)] public float sensitivityX = 150f;
        [Min(0f)] public float sensitivityY = 150f;
        [Tooltip("Multiply input by deltaTime to keep movement frame-rate independent.")]
        public bool useDeltaTime = true;
        [Tooltip("Invert vertical look direction")] public bool invertY = false;

        [Header("Limits (degrees)")]
        public float minimumX = -360f;
        public float maximumX = 360f;
        public float minimumY = -60f;
        public float maximumY = 60f;

        [Header("Smoothing (seconds)")]
        [Min(0f)] public float smoothTimeX = 0.05f;
        [Min(0f)] public float smoothTimeY = 0.05f;

        [Header("Cursor")]
        public bool lockAndHideCursor = false;

        // State
        private float targetYaw;   // around Y (left/right)
        private float targetPitch; // around X (up/down)
        private float currentYaw;
        private float currentPitch;
        private float yawVelocity;   // SmoothDamp velocity storage
        private float pitchVelocity; // SmoothDamp velocity storage

        private Quaternion originalRotation;
        private Quaternion parentRotation;
        private Transform parentTransform;

        private void OnEnable()
        {
            // Cache baseline rotations
            originalRotation = transform.localRotation;
            parentTransform = transform.parent;
            parentRotation = parentTransform != null ? parentTransform.localRotation : Quaternion.identity;

            // Initialize angles to current orientation
            // Decompose local rotations to yaw/pitch relative to originals
            currentYaw = targetYaw = 0f;
            currentPitch = targetPitch = 0f;

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.freezeRotation = true;
            }

            if (lockAndHideCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            if (lockAndHideCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            float dt = useDeltaTime ? Time.deltaTime : 1f;

            // Read raw mouse delta for responsiveness
            float mouseX = Input.GetAxisRaw("Mouse X");
            float mouseY = Input.GetAxisRaw("Mouse Y");
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

            // Clamp to limits (limits are absolute around the initial orientation)
            targetYaw = ClampAngle(targetYaw, minimumX, maximumX);
            targetPitch = ClampAngle(targetPitch, minimumY, maximumY);

            // Smooth towards targets (use angle-aware damping for yaw to wrap cleanly across 360)
            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, smoothTimeX, Mathf.Infinity, Time.deltaTime);
            currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref pitchVelocity, smoothTimeY, Mathf.Infinity, Time.deltaTime);

            // Apply rotations
            // Pitch is applied locally (camera up/down) relative to original local rotation
            // Yaw is applied to parent if available (character facing), otherwise applied to self before pitch
            Quaternion yQuaternion = Quaternion.AngleAxis(currentPitch, Vector3.left);
            Quaternion xQuaternion = Quaternion.AngleAxis(currentYaw, Vector3.up);

            if (axes == RotationAxes.MouseX)
            {
                if (parentTransform != null)
                {
                    parentTransform.localRotation = parentRotation * xQuaternion;
                }
                else
                {
                    transform.localRotation = originalRotation * xQuaternion;
                }
                return;
            }

            if (axes == RotationAxes.MouseY)
            {
                transform.localRotation = originalRotation * yQuaternion;
                return;
            }

            // MouseXAndY
            transform.localRotation = originalRotation * yQuaternion;
            if (parentTransform != null)
            {
                parentTransform.localRotation = parentRotation * xQuaternion;
            }
            else
            {
                // Fallback: apply yaw to self if no parent
                transform.localRotation = originalRotation * xQuaternion * yQuaternion;
            }
        }

        public static float ClampAngle(float angle, float min, float max)
        {
            // Normalize to [-180, 180]
            angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
            // If limits span >= 360, effectively no clamp
            if (max - min >= 360f) return angle;
            return Mathf.Clamp(angle, min, max);
        }
    }
}
