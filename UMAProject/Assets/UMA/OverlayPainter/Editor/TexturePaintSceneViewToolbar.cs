using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    [Overlay(typeof(SceneView), "Overlay Painter Scene", TexturePaintSceneOverlayVisibility.DefaultDisplay)]
    public sealed class TexturePaintSceneViewToolbarOverlay : ToolbarOverlay
    {
        private static readonly HashSet<TexturePaintSceneViewToolbarOverlay> Instances = new();
        private bool contextWasActive;

        public TexturePaintSceneViewToolbarOverlay() : base(
            TexturePaintSceneLightRotationSlider.Id,
            TexturePaintSceneCameraSeparator.Id,
            TexturePaintSceneCameraLabel.Id,
            TexturePaintSceneCameraLeftButton.Id,
            TexturePaintSceneCameraBackButton.Id,
            TexturePaintSceneCameraRightButton.Id) { }

        public override void OnCreated()
        {
            base.OnCreated();
            Instances.Add(this);
            contextWasActive = IsContextActive();
            displayed = contextWasActive;
            EditorApplication.update += UpdateContextVisibility;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= UpdateContextVisibility;
            Instances.Remove(this);
            base.OnWillBeDestroyed();
        }

        internal static void HideAllInstances()
        {
            foreach (var overlay in Instances)
            {
                if (overlay == null) continue;
                overlay.contextWasActive = false;
                overlay.displayed = false;
            }
        }

        internal static void RefreshAllInstances()
        {
            foreach (var overlay in Instances) overlay?.UpdateContextVisibility();
        }

        private void UpdateContextVisibility()
        {
            bool active = IsContextActive();
            if (!TexturePaintSceneOverlayVisibility.NeedsDisplayRefresh(active, contextWasActive, displayed)) return;
            contextWasActive = active;
            displayed = active;
        }

        private bool IsContextActive()
        {
            var stage = TexturePaintStageWindow.ActiveStage;
            return stage?.SceneToolbarReady == true && ReferenceEquals(StageUtility.GetCurrentStage(), stage) &&
                TexturePaintSceneOverlayVisibility.ShouldDisplayOn(containerWindow);
        }

        internal static void ApplyStyle(VisualElement element, string className)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(TexturePaintAssets.ResolveInstallAssetPath(
                "OverlayPainter/Editor/TexturePaintSceneViewToolbar.uss"));
            if (sheet != null) element.styleSheets.Add(sheet);
            element.AddToClassList(className);
        }

        internal static void OrbitCamera(SceneView view, float angle)
        {
            if (view == null) return;
            if (view.in2DMode) view.in2DMode = false;
            view.LookAt(view.pivot, Quaternion.AngleAxis(angle, Vector3.up) * view.rotation,
                view.size, view.orthographic, true);
            view.Repaint();
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneCameraSeparator : VisualElement
    {
        public const string Id = "UMA/OverlayPainterScene/CameraSeparator";
        public TexturePaintSceneCameraSeparator()
        {
            pickingMode = PickingMode.Ignore;
            TexturePaintSceneViewToolbarOverlay.ApplyStyle(this, "op-scene-separator");
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneCameraLabel : Label
    {
        public const string Id = "UMA/OverlayPainterScene/CameraLabel";
        public TexturePaintSceneCameraLabel() : base("Camera")
        {
            pickingMode = PickingMode.Ignore;
            TexturePaintSceneViewToolbarOverlay.ApplyStyle(this, "op-scene-camera-label");
        }
    }

    public abstract class TexturePaintSceneCameraButton : EditorToolbarButton, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }

        protected TexturePaintSceneCameraButton(string label, float angle)
        {
            text = label;
            tooltip = "Orbit the camera " + label + " degrees around the world Y axis at the current pivot.";
            TexturePaintSceneViewToolbarOverlay.ApplyStyle(this, "op-scene-camera-button");
            clicked += () =>
            {
                if (CanRotate()) TexturePaintSceneViewToolbarOverlay.OrbitCamera((SceneView)containerWindow, angle);
            };
            schedule.Execute(() => SetEnabled(CanRotate())).Every(250L);
            SetEnabled(false);
        }

        private bool CanRotate()
        {
            var stage = TexturePaintStageWindow.ActiveStage;
            return containerWindow is SceneView && stage?.SceneToolbarReady == true &&
                ReferenceEquals(StageUtility.GetCurrentStage(), stage);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneCameraLeftButton : TexturePaintSceneCameraButton
    {
        public const string Id = "UMA/OverlayPainterScene/CameraMinus90";
        public TexturePaintSceneCameraLeftButton() : base("-90", -90f) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneCameraBackButton : TexturePaintSceneCameraButton
    {
        public const string Id = "UMA/OverlayPainterScene/Camera180";
        public TexturePaintSceneCameraBackButton() : base("180", 180f) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class TexturePaintSceneCameraRightButton : TexturePaintSceneCameraButton
    {
        public const string Id = "UMA/OverlayPainterScene/CameraPlus90";
        public TexturePaintSceneCameraRightButton() : base("+90", 90f) { }
    }
}
