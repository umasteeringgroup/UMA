#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Holds the saved Scene View camera state and provides static Save/Restore methods
    /// used by the UMA Toolbar overlay.
    /// </summary>
    public static class UMAEditorTools
    {
        private static Vector3? savedPivot;
        private static Quaternion? savedRotation;
        private static float? savedSize;
        private static bool? savedOrthographic;

        /// <summary>
        /// True when a camera state has been saved (at least once).
        /// </summary>
        public static bool HasSavedState => savedPivot.HasValue;

        /// <summary>
        /// Saves the current Scene View camera position, rotation, size, and ortho mode.
        /// </summary>
        public static void SaveCameraState(SceneView sceneView)
        {
            if (sceneView == null)
            {
                sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    Debug.LogWarning("[UMAEditorTools] No active Scene View to save camera from.");
                    return;
                }
            }

            savedPivot = sceneView.pivot;
            savedRotation = sceneView.rotation;
            savedSize = sceneView.size;
            savedOrthographic = sceneView.orthographic;

            Debug.Log($"[UMAEditorTools] Scene View camera saved. Pivot: {savedPivot.Value}, Size: {savedSize.Value:F2}, Ortho: {savedOrthographic.Value}");
        }

        /// <summary>
        /// Restores the Scene View camera to the previously saved state.
        /// </summary>
        public static void RestoreCameraState(SceneView sceneView)
        {
            if (!HasSavedState)
            {
                Debug.LogWarning("[UMAEditorTools] No saved camera state to restore. Use the Save tool first.");
                return;
            }

            if (sceneView == null)
            {
                sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    Debug.LogWarning("[UMAEditorTools] No active Scene View to restore camera to.");
                    return;
                }
            }

            sceneView.pivot = savedPivot.Value;
            sceneView.rotation = savedRotation.Value;
            sceneView.size = savedSize.Value;
            sceneView.orthographic = savedOrthographic.Value;

            Debug.Log($"[UMAEditorTools] Scene View camera restored. Pivot: {savedPivot.Value}, Size: {savedSize.Value:F2}, Ortho: {savedOrthographic.Value}");
        }
    }
}
#endif
