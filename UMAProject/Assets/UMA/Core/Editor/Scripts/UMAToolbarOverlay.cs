using UMA;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Dockable Scene View toolbar for UMA-specific editor commands.
    /// </summary>
    [Overlay(typeof(SceneView), "UMA Toolbar", true)]
    public sealed class UMAToolbarOverlay : ToolbarOverlay
    {
        public UMAToolbarOverlay() : base(
            SaveSceneViewCameraButton.Id,
            RestoreSceneViewCameraButton.Id,
            RebuildAllUMAsButton.Id)
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class SaveSceneViewCameraButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/SaveSceneViewCamera";

        public SaveSceneViewCameraButton()
        {
            icon = EditorGUIUtility.IconContent("d_SceneViewCamera").image as Texture2D;
            tooltip = "Save Scene View Camera";
            clicked += () => UMAEditorTools.SaveCameraState(SceneView.lastActiveSceneView);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class RestoreSceneViewCameraButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/RestoreSceneViewCamera";

        public RestoreSceneViewCameraButton()
        {
            icon = EditorGUIUtility.IconContent("d_ViewToolOrbit").image as Texture2D;
            tooltip = "Restore Scene View Camera";
            clicked += () => UMAEditorTools.RestoreCameraState(SceneView.lastActiveSceneView);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class RebuildAllUMAsButton : EditorToolbarButton
    {
        public const string Id = "UMA/Toolbar/RebuildAllUMAs";

        public RebuildAllUMAsButton()
        {
            icon = EditorGUIUtility.IconContent("d_Avatar Icon").image as Texture2D;
            tooltip = "Rebuild all UMAs";
            clicked += RebuildAllUMAs;
        }

        private static void RebuildAllUMAs()
        {
            if (!EditorApplication.isPlaying)
            {
                UMAGeneratorBuiltinEditor.RebuildAllEditorUMA();
            }
        }
    }
}
