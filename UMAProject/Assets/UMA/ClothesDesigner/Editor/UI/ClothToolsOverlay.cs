using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>The cloth tools' names and tooltips, shared by the Scene view toolbar and the panel.</summary>
    public static class ClothToolInfo
    {
        public static readonly ClothTool[] Order = { ClothTool.Place, ClothTool.Grab, ClothTool.Pin, ClothTool.ErasePin, ClothTool.Measure };

        public static string Name(ClothTool tool)
        {
            switch (tool)
            {
                case ClothTool.Place: return "Place";
                case ClothTool.Grab: return "Grab";
                case ClothTool.Pin: return "Pin";
                case ClothTool.Measure: return "Measure";
                default: return "Erase Pin";
            }
        }

        public static string Tooltip(ClothTool tool)
        {
            switch (tool)
            {
                case ClothTool.Place:
                    return "Place: click a piece, then drag it round the body or up and down. Shift-drag moves it out from or in towards the body. Escape puts it back.";
                case ClothTool.Grab:
                    return "Grab: drag the cloth; the cloth within the Radius follows the pointer. Release to let it settle; Escape puts it back.";
                case ClothTool.Pin:
                    return "Pin: click or drag on the cloth to hold it where it is. Pins are kept on the pattern, so they survive a new mesh level.";
                case ClothTool.Measure:
                    return "Measure: click two points on the cloth or the body for the distance between them; the panel also shows how far round the cloth and the body are at the first point's height.";
                default:
                    return "Erase Pin: click or drag on pinned cloth to release it.";
            }
        }

        /// <summary>The tools as panel buttons: the name, with the tooltip.</summary>
        public static GUIContent[] PanelContents()
        {
            var contents = new GUIContent[Order.Length];
            for (int i = 0; i < Order.Length; i++) contents[i] = new GUIContent(Name(Order[i]), Tooltip(Order[i]));
            return contents;
        }

        private static Texture2D pinIcon;

        /// <summary>The toolbar icon for a tool: Unity's move, hand and eraser icons, and a pin drawn here (Unity has none).</summary>
        public static Texture2D Icon(ClothTool tool)
        {
            switch (tool)
            {
                case ClothTool.Place: return BuiltIn("MoveTool");
                case ClothTool.Grab: return BuiltIn("ViewToolMove");
                case ClothTool.Pin: return PinIcon();
                case ClothTool.Measure: return BuiltIn("ScaleTool");
                default: return BuiltIn("Grid.EraserTool");
            }
        }

        private static Texture2D BuiltIn(string name)
        {
            // FindTexture returns null for a missing icon without logging, unlike IconContent.
            return EditorGUIUtility.FindTexture((EditorGUIUtility.isProSkin ? "d_" : "") + name) ?? EditorGUIUtility.FindTexture(name);
        }

        /// <summary>A push pin, head up and to the right, in the editor's icon grey.</summary>
        public static Texture2D PinIcon()
        {
            if (pinIcon != null) return pinIcon;
            const int size = 32;
            pinIcon = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Clothes Designer Pin Icon",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color ink = EditorGUIUtility.isProSkin ? new Color(0.77f, 0.77f, 0.77f) : new Color(0.24f, 0.24f, 0.24f);
            var pixels = new Color[size * size];
            Vector2 head = new Vector2(20.5f, 20.5f), tip = new Vector2(4f, 4f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    // Head: a disc. Needle: a line from the head to the tip. Covered by distance, anti-aliased.
                    float headCover = Mathf.Clamp01(7.5f - Vector2.Distance(p, head));
                    Vector2 along = head - tip;
                    float t = Mathf.Clamp01(Vector2.Dot(p - tip, along) / along.sqrMagnitude);
                    float needleCover = Mathf.Clamp01(1.75f - Vector2.Distance(p, tip + along * t));
                    float cover = Mathf.Max(headCover, needleCover);
                    pixels[y * size + x] = new Color(ink.r, ink.g, ink.b, cover);
                }
            }
            pinIcon.SetPixels(pixels);
            pinIcon.Apply(false, true);
            return pinIcon;
        }
    }

    /// <summary>
    /// The cloth tools on a Scene view toolbar, shown while the Clothes Designer is the active workflow of
    /// the open Vertex Editor and its cloth is ready. The same tools stay in the panel's Cloth Tools section.
    /// </summary>
    [Overlay(typeof(SceneView), Title, false)]
    public sealed class ClothToolsOverlay : ToolbarOverlay
    {
        public const string Title = "Cloth Tools";

        private bool contextWasActive;

        public ClothToolsOverlay() : base(
            PlaceClothToolToggle.Id, GrabClothToolToggle.Id, PinClothToolToggle.Id, ErasePinClothToolToggle.Id, MeasureClothToolToggle.Id,
            ClothToolbarGap.Id,
            ClothSimulateToggle.Id, ClothStepButton.Id, ClothResetButton.Id, ClothKeepDrapeButton.Id)
        {
        }

        /// <summary>The designer session the tools act on, or null when none is active and ready.</summary>
        public static DesignerSession ActiveSession
        {
            get
            {
                if (!(StageUtility.GetCurrentStage() is VertexEditorStage stage)) return null;
                if (!ReferenceEquals(stage.ActiveWorkflow, ClothesDesignerWorkflow.Instance)) return null;
                if (!ClothesDesignerWorkflow.Instance.TryGetSession(stage, out DesignerSession session)) return null;
                if (session.Mode != ClothesDesignerWorkflow.Mode.NewClothes || !session.HasPattern || !session.ClothReady) return null;
                return session;
            }
        }

        public override void OnCreated()
        {
            base.OnCreated();
            contextWasActive = ActiveSession != null;
            displayed = contextWasActive;
            EditorApplication.update += UpdateVisibility;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= UpdateVisibility;
            base.OnWillBeDestroyed();
        }

        private void UpdateVisibility()
        {
            // Shown and hidden only when the designer starts or stops, so a toolbar the user closed
            // while designing stays closed until the next time.
            bool active = ActiveSession != null;
            if (active == contextWasActive) return;
            contextWasActive = active;
            displayed = active;
        }
    }

    /// <summary>One cloth tool on the toolbar: on while it is the session's tool.</summary>
    public abstract class ClothToolToggle : EditorToolbarToggle
    {
        private readonly ClothTool tool;

        protected ClothToolToggle(ClothTool tool)
        {
            this.tool = tool;
            Texture2D image = ClothToolInfo.Icon(tool);
            if (image != null) icon = image;
            else text = ClothToolInfo.Name(tool);
            tooltip = ClothToolInfo.Tooltip(tool);
            this.RegisterValueChangedCallback(OnChanged);
            // The panel can change the tool too: follow it.
            schedule.Execute(Refresh).Every(150);
            Refresh();
        }

        private void OnChanged(ChangeEvent<bool> change)
        {
            DesignerSession session = ClothToolsOverlay.ActiveSession;
            if (session == null) { SetValueWithoutNotify(false); return; }
            if (change.newValue && session.Tool != tool)
            {
                session.Tool = tool;
                SceneView.RepaintAll();
            }
            // There is always a tool: clicking the current one keeps it on.
            SetValueWithoutNotify(session.Tool == tool);
        }

        private void Refresh()
        {
            DesignerSession session = ClothToolsOverlay.ActiveSession;
            SetEnabled(session != null);
            SetValueWithoutNotify(session != null && session.Tool == tool);
        }
    }

    /// <summary>A small gap between the tools and the simulation buttons.</summary>
    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class ClothToolbarGap : VisualElement
    {
        public const string Id = "UMA/ClothesDesigner/Gap";
        public ClothToolbarGap() { style.width = 8f; style.height = 8f; }
    }

    /// <summary>The Fit section's Simulate / Pause, Step, Reset and Keep Drape, on the toolbar.</summary>
    public static class ClothFitButtons
    {
        public const string SimulateTip = "Simulate: sew the seams and let the cloth fall onto the body. It stops by itself when the cloth is still.";
        public const string PauseTip = "Pause: stop the simulation here.";
        public const string StepTip = "Step: advance the simulation one frame.";
        public const string ResetTip = "Reset: put the pieces back where they were laid out, unsewn.";
        public const string KeepDrapeTip = "Keep Drape: save this shape in the fit, so reopening it restores these folds.";

        public static Texture2D Icon(string name)
        {
            return EditorGUIUtility.FindTexture((EditorGUIUtility.isProSkin ? "d_" : "") + name) ?? EditorGUIUtility.FindTexture(name);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class ClothSimulateToggle : EditorToolbarToggle
    {
        public const string Id = "UMA/ClothesDesigner/Simulate";

        public ClothSimulateToggle()
        {
            onIcon = ClothFitButtons.Icon("PauseButton");
            offIcon = ClothFitButtons.Icon("PlayButton");
            tooltip = ClothFitButtons.SimulateTip;
            this.RegisterValueChangedCallback(change =>
            {
                DesignerSession session = ClothToolsOverlay.ActiveSession;
                if (session != null && session.Running != change.newValue) session.SetRunning(change.newValue);
                Refresh();
            });
            schedule.Execute(Refresh).Every(150);
            Refresh();
        }

        private void Refresh()
        {
            DesignerSession session = ClothToolsOverlay.ActiveSession;
            SetEnabled(session != null);
            bool running = session != null && session.Running;
            SetValueWithoutNotify(running);
            tooltip = running ? ClothFitButtons.PauseTip : ClothFitButtons.SimulateTip;
        }
    }

    /// <summary>A toolbar button that acts on the active designer session.</summary>
    public abstract class ClothFitButton : EditorToolbarButton
    {
        protected ClothFitButton(string iconName, string label, string tip, Action<DesignerSession> act)
        {
            Texture2D image = ClothFitButtons.Icon(iconName);
            if (image != null) icon = image;
            else text = label;
            tooltip = tip;
            clicked += () =>
            {
                DesignerSession session = ClothToolsOverlay.ActiveSession;
                if (session != null) act(session);
            };
            schedule.Execute(() => SetEnabled(ClothToolsOverlay.ActiveSession != null)).Every(150);
            SetEnabled(ClothToolsOverlay.ActiveSession != null);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class ClothStepButton : ClothFitButton
    {
        public const string Id = "UMA/ClothesDesigner/Step";
        public ClothStepButton() : base("StepButton", "Step", ClothFitButtons.StepTip, s => s.StepOnce()) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class ClothResetButton : ClothFitButton
    {
        public const string Id = "UMA/ClothesDesigner/Reset";
        public ClothResetButton() : base("Animation.FirstKey", "Reset", ClothFitButtons.ResetTip, s => s.ResetToArrangement()) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class ClothKeepDrapeButton : ClothFitButton
    {
        public const string Id = "UMA/ClothesDesigner/KeepDrape";
        public ClothKeepDrapeButton() : base("SaveAs", "Keep Drape", ClothFitButtons.KeepDrapeTip, s => s.KeepDrape()) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class PlaceClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Place";
        public PlaceClothToolToggle() : base(ClothTool.Place) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class GrabClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Grab";
        public GrabClothToolToggle() : base(ClothTool.Grab) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class PinClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Pin";
        public PinClothToolToggle() : base(ClothTool.Pin) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class ErasePinClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/ErasePin";
        public ErasePinClothToolToggle() : base(ClothTool.ErasePin) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MeasureClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Measure";
        public MeasureClothToolToggle() : base(ClothTool.Measure) { }
    }
}
