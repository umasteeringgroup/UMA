using UnityEngine;
using UnityEngine.InputSystem;

namespace UMA.Dynamics.Examples
{
    [DisallowMultipleComponent]
    public class CursorLock : MonoBehaviour
    {
        private const float EngageButtonWidth = 240f;
        private const float EngageButtonHeight = 80f;

        public bool IsMouseCaptured =>
            Cursor.lockState == CursorLockMode.Locked;

        void OnEnable()
        {
            // Always begin in a definite released state. This also runs on each
            // Play Mode transition when domain and scene reload are disabled.
            if (Application.isPlaying)
            {
                ReleaseMouse();
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            // Losing application focus also requires an explicit Engage click
            // before gameplay resumes. Never request capture from a focus event.
            if (!hasFocus)
            {
                ReleaseMouse();
            }
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReleaseMouse();
            }
        }

        void OnGUI()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                return;
            }

            Rect engageRect = new Rect(
                (Screen.width - EngageButtonWidth) * 0.5f,
                (Screen.height - EngageButtonHeight) * 0.5f,
                EngageButtonWidth,
                EngageButtonHeight);
            GUIStyle engageStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold
            };
            if (GUI.Button(engageRect, "Engage!", engageStyle))
            {
                Engage();
            }
        }

        void OnDisable()
        {
            ReleaseMouse();
        }

        public void Engage()
        {
            RestoreAllRagdolls();
            LockMouse();
        }

        public void ReleaseMouse()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LockMouse()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void RestoreAllRagdolls()
        {
            UMAPhysicsAvatar[] avatars =
                UMAObjectUtility.FindObjectsByType<UMAPhysicsAvatar>(
                    FindObjectsInactive.Exclude);
            for (int i = 0; i < avatars.Length; i++)
            {
                UMAPhysicsAvatar avatar = avatars[i];
                if (avatar != null && avatar.ragdolled)
                {
                    avatar.ragdolled = false;
                }
            }
        }
    }
}
