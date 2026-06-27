#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Holds the saved Scene View camera state and provides static Save/Restore methods
    /// used by the EditorTool classes below.
    /// </summary>
    public static class UMAEditorTools
    {
        private static Vector3? savedPivot;
        private static Quaternion? savedRotation;
        private static float? savedSize;
        private static bool? savedOrthographic;

        internal static readonly GUIContent SaveCameraIcon = new GUIContent(
            EditorGUIUtility.IconContent("d_SceneViewCamera").image,
            "Save Scene View Camera");

        internal static readonly GUIContent RestoreCameraIcon = new GUIContent(
            EditorGUIUtility.IconContent("d_ViewToolOrbit").image,
            "Restore Scene View Camera");

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

    /// <summary>
    /// Editor tool: saves the current Scene View camera position.
    /// Appears in the Scene View toolbar.
    /// </summary>
    [EditorTool("Save Scene View Camera")]
    public class SaveSceneViewCameraTool : EditorTool
    {
        public override GUIContent toolbarIcon => UMAEditorTools.SaveCameraIcon;

        public override void OnActivated()
        {
            UMAEditorTools.SaveCameraState(SceneView.lastActiveSceneView);

            // Switch back to the previous tool so this acts as a one-shot command.
            EditorApplication.delayCall += () =>
            {
                ToolManager.RestorePreviousTool();
            };
        }
    }

    /// <summary>
    /// Editor tool: restores the Scene View camera to the last saved position.
    /// Appears in the Scene View toolbar. Shows a warning if no state has been saved yet.
    /// </summary>
    [EditorTool("Restore Scene View Camera")]
    public class RestoreSceneViewCameraTool : EditorTool
    {
        public override GUIContent toolbarIcon => UMAEditorTools.RestoreCameraIcon;

        public override void OnActivated()
        {
            UMAEditorTools.RestoreCameraState(SceneView.lastActiveSceneView);

            // Switch back to the previous tool so this acts as a one-shot command.
            EditorApplication.delayCall += () =>
            {
                ToolManager.RestorePreviousTool();
            };
        }
    }
}
#endif
