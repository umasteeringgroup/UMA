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
        public static readonly ClothTool[] Order = { ClothTool.Place, ClothTool.Size, ClothTool.Grab, ClothTool.Pin, ClothTool.ErasePin, ClothTool.FitBrush, ClothTool.Roll, ClothTool.Measure, ClothTool.Dart, ClothTool.Pleat, ClothTool.DrawPiece, ClothTool.BodyRegion };

        public static string Name(ClothTool tool)
        {
            switch (tool)
            {
                case ClothTool.Place: return "Place";
                case ClothTool.Grab: return "Grab";
                case ClothTool.Pin: return "Pin";
                case ClothTool.Measure: return "Measure";
                case ClothTool.Roll: return "Roll";
                case ClothTool.Dart: return "Dart";
                case ClothTool.Pleat: return "Pleat";
                case ClothTool.DrawPiece: return "Draw Piece";
                case ClothTool.FitBrush: return "Fit Brush";
                case ClothTool.BodyRegion: return "Body Region";
                case ClothTool.Size: return "Size";
                default: return "Erase Pin";
            }
        }

        public static string Tooltip(ClothTool tool)
        {
            switch (tool)
            {
                case ClothTool.Place:
                    return "Place: click a piece, then drag it round the body or up and down. Shift-drag moves it out from or in towards the body. Escape puts it back.";
                case ClothTool.Size:
                    return "Size: click a piece for its handles. Drag a side handle out or in to make it wider or narrower (both sides move), or the hem handle down or up to make it longer or shorter; or drag anywhere on the piece, across or down it. The cloth shows the new size as you drag; release to keep it (it then settles into it), Escape puts it back. Shift keeps its proportions. Where one of the pattern's sizes sets the width or length (a chest, both sleeves), that size changes, with every piece it sizes; otherwise the pieces that share a size with it (a belt's other half) are resized with it. The panel takes exact widths and lengths.";
                case ClothTool.Grab:
                    return "Grab: drag the cloth; the cloth within the Radius follows the pointer. Release to let it settle; Escape puts it back.";
                case ClothTool.Pin:
                    return "Pin: click or drag on the cloth to hold it where it is. Pins are kept on the pattern, so they survive a new mesh level.";
                case ClothTool.Roll:
                    return "Roll: press on the cloth that belongs in front and drag over where it sinks into, or lies flush with, the cloth behind it (a collar's leaf on its stand, a pleat's fold). That cloth is lifted out until it is the Cloth Thickness clear. Each stroke is one Undo step; it does not start the simulation.";
                case ClothTool.Measure:
                    return "Measure: click two points on the cloth or the body for the distance between them; the panel also shows how far round the cloth and the body are at the first point's height.";
                case ClothTool.Dart:
                    return "Dart: press on the cloth where the dart's point goes and release towards the edge it opens on (a click alone opens it on the nearest edge). Shift-drag for a double-pointed dart from one point to the other. The cloth keeps its drape and simulates, so the dart is sewn on the body.";
                case ClothTool.Pleat:
                    return "Pleat: click on the cloth near the edge the pleat folds from (a skirt's waist). The cloth keeps its drape and simulates, so the pleat is folded on the body.";
                case ClothTool.DrawPiece:
                    return "Draw Piece: click points round a new piece on the body's torso; click the first point again or press Enter to finish. Backspace takes the last point back, Escape starts again. The piece is unrolled round the body and laid where it was drawn.";
                case ClothTool.BodyRegion:
                    return "Body Region: paint a region on the body anywhere (Shift-drag takes it off; the other side is painted too while Mirror is on), then Make Pieces: it is split by body part, cut open where it goes round (at the back, under the arm, inside the leg), unrolled flat, split into gores where it would stretch more than " + (RegionUnfold.MaxDistortion * 100f).ToString("0") + "%, and sewn where the parts meet. The pieces are laid where they were painted.";
                case ClothTool.FitBrush:
                    return "Fit Brush: paint where the cloth hugs the body (cut smaller and drawn onto it), smooths out, puffs up or slims in. Ctrl-drag takes the paint off. Each stroke is one Undo step; the cloth simulates when you let go. The paint is kept on the pattern, so it survives a new mesh level.";
                default:
                    return "Erase Pin: click or drag on pinned cloth to release it.";
            }
        }

        /// <summary>
        /// Whether a toolbar of the designer's is shown, given whether its context (the designer, the Fit Brush) is active
        /// now and was at the last check, and whether it is shown: shown or hidden when the context starts or stops; left as
        /// the user set it while the context lasts (a toolbar closed while designing stays closed); and always hidden
        /// outside it. A Scene view opened as the designer closed (the Overlay Painter's, after Publish and Open in Overlay
        /// Painter) could otherwise keep the toolbar shown, copied from the view that had it, with no change of context
        /// to hide it again.
        /// </summary>
        public static bool ToolbarShown(bool active, bool wasActive, bool displayed) => active != wasActive ? active : active && displayed;

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
                // A small square in the corner of a large one, with an arrow from one to the other.
                case ClothTool.Size: return LineIcon("Size", new[] { new[] { new Vector2(4f, 4f), new Vector2(28f, 4f), new Vector2(28f, 28f), new Vector2(4f, 28f), new Vector2(4f, 4f) }, new[] { new Vector2(4f, 14f), new Vector2(14f, 14f), new Vector2(14f, 4f) }, new[] { new Vector2(16f, 16f), new Vector2(24f, 24f) }, new[] { new Vector2(24f, 24f), new Vector2(24f, 18f) }, new[] { new Vector2(24f, 24f), new Vector2(18f, 24f) } });
                case ClothTool.Grab: return BuiltIn("ViewToolMove");
                case ClothTool.Pin: return PinIcon();
                case ClothTool.Measure: return TapeMeasureIcon();
                case ClothTool.Roll: return BuiltIn("Grid.PaintTool") ?? BuiltIn("ViewToolOrbit");
                // Unity has no icons for these: a dart's V, a pleat's zigzag, and a pencil.
                case ClothTool.Dart: return LineIcon("Dart", new[] { new[] { new Vector2(6f, 26f), new Vector2(16f, 6f), new Vector2(26f, 26f) }, new[] { new Vector2(3f, 26f), new Vector2(29f, 26f) } });
                case ClothTool.Pleat: return LineIcon("Pleat", new[] { new[] { new Vector2(3f, 8f), new Vector2(10f, 24f), new Vector2(16f, 8f), new Vector2(22f, 24f), new Vector2(29f, 8f) } });
                case ClothTool.DrawPiece: return LineIcon("DrawPiece", new[] { new[] { new Vector2(6f, 6f), new Vector2(26f, 26f) }, new[] { new Vector2(6f, 6f), new Vector2(4f, 12f) }, new[] { new Vector2(6f, 6f), new Vector2(12f, 4f) }, new[] { new Vector2(22f, 28f), new Vector2(28f, 22f) } });
                // A paint brush: the handle, and the bristles' rounded tip.
                case ClothTool.BodyRegion: return LineIcon("BodyRegion", new[] { new[] { new Vector2(13f, 3f), new Vector2(19f, 3f), new Vector2(19f, 9f), new Vector2(13f, 9f), new Vector2(13f, 3f) }, new[] { new Vector2(8f, 29f), new Vector2(10f, 12f), new Vector2(22f, 12f), new Vector2(24f, 29f) }, new[] { new Vector2(10f, 17f), new Vector2(22f, 17f) }, new[] { new Vector2(9f, 22f), new Vector2(23f, 22f) } });
                case ClothTool.FitBrush: return LineIcon("FitBrush", new[] { new[] { new Vector2(28f, 28f), new Vector2(15f, 15f) }, new[] { new Vector2(15f, 15f), new Vector2(9f, 6f), new Vector2(4f, 4f), new Vector2(6f, 9f), new Vector2(15f, 15f) }, new[] { new Vector2(17f, 13f), new Vector2(13f, 17f) } });
                default: return BuiltIn("Grid.EraserTool");
            }
        }

        /// <summary>
        /// A tape measure: a round case with its winding hub, and the tape pulled out to the right, with ticks and the hook at
        /// its end. (Unity's scale icon, used before, looked too like the Size tool's.)
        /// </summary>
        public static Texture2D TapeMeasureIcon()
        {
            var lines = new System.Collections.Generic.List<Vector2[]>
            {
                Circle(new Vector2(12f, 17f), 10f, 24),
                Circle(new Vector2(12f, 17f), 3f, 10),
                // The tape: leaving the case at its foot, out to the right.
                new[] { new Vector2(12f, 7f), new Vector2(30f, 7f), new Vector2(30f, 12f), new Vector2(21f, 12f) },
                // Ticks, and the hook turned up at the end.
                new[] { new Vector2(24f, 7f), new Vector2(24f, 9.5f) },
                new[] { new Vector2(27f, 7f), new Vector2(27f, 9.5f) },
                new[] { new Vector2(30f, 12f), new Vector2(30f, 15f) }
            };
            return LineIcon("TapeMeasure", lines.ToArray());
        }

        private static Vector2[] Circle(Vector2 centre, float radius, int sides)
        {
            var points = new Vector2[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                points[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
            return points;
        }

        private static Texture2D BuiltIn(string name)
        {
            // FindTexture returns null for a missing icon without logging, unlike IconContent.
            return EditorGUIUtility.FindTexture((EditorGUIUtility.isProSkin ? "d_" : "") + name) ?? EditorGUIUtility.FindTexture(name);
        }

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> lineIcons = new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>A 32 px icon of anti-aliased lines (pixel coordinates, y up), in the editor's icon grey.</summary>
        public static Texture2D LineIcon(string name, Vector2[][] lines)
        {
            if (lineIcons.TryGetValue(name, out Texture2D cached) && cached != null) return cached;
            const int size = 32;
            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Clothes Designer " + name + " Icon",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color ink = EditorGUIUtility.isProSkin ? new Color(0.77f, 0.77f, 0.77f) : new Color(0.24f, 0.24f, 0.24f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float cover = 0f;
                    foreach (Vector2[] line in lines)
                    {
                        for (int i = 1; i < line.Length; i++)
                        {
                            Vector2 a = line[i - 1], along = line[i] - a;
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, along) / Mathf.Max(1e-6f, along.sqrMagnitude));
                            cover = Mathf.Max(cover, Mathf.Clamp01(1.9f - Vector2.Distance(p, a + along * t)));
                        }
                    }
                    pixels[y * size + x] = new Color(ink.r, ink.g, ink.b, cover);
                }
            }
            icon.SetPixels(pixels);
            icon.Apply(false, true);
            lineIcons[name] = icon;
            return icon;
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
            PlaceClothToolToggle.Id, SizeClothToolToggle.Id, GrabClothToolToggle.Id, PinClothToolToggle.Id, ErasePinClothToolToggle.Id, FitBrushClothToolToggle.Id, RollClothToolToggle.Id, MeasureClothToolToggle.Id,
            DartClothToolToggle.Id, PleatClothToolToggle.Id, DrawPieceClothToolToggle.Id, BodyRegionClothToolToggle.Id,
            ClothToolbarGap.Id,
            ClothSimulateToggle.Id, ClothStepButton.Id, ClothResetButton.Id, ClothKeepDrapeButton.Id, ClothPublishButton.Id)
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
            // Shown and hidden when the designer starts or stops, so a toolbar the user closed while designing stays
            // closed until the next time; and never shown outside the designer.
            bool active = ActiveSession != null;
            bool show = ClothToolInfo.ToolbarShown(active, contextWasActive, displayed);
            contextWasActive = active;
            if (show != displayed) displayed = show;
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

    /// <summary>Publish, at the end of the toolbar: asks for the name and wardrobe region, then publishes the clothes.</summary>
    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class ClothPublishButton : EditorToolbarButton
    {
        public const string Id = "UMA/ClothesDesigner/Publish";

        public ClothPublishButton()
        {
            text = "Publish";
            tooltip = PublishClothesWindow.Tip;
            clicked += () =>
            {
                DesignerSession session = ClothToolsOverlay.ActiveSession;
                if (session != null) PublishClothesWindow.AskAndPublish(session);
            };
            schedule.Execute(() => SetEnabled(ClothToolsOverlay.ActiveSession != null)).Every(150);
            SetEnabled(ClothToolsOverlay.ActiveSession != null);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class PlaceClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Place";
        public PlaceClothToolToggle() : base(ClothTool.Place) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class SizeClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Size";
        public SizeClothToolToggle() : base(ClothTool.Size) { }
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
    public sealed class FitBrushClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/FitBrush";
        public FitBrushClothToolToggle() : base(ClothTool.FitBrush) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class RollClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Roll";
        public RollClothToolToggle() : base(ClothTool.Roll) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MeasureClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Measure";
        public MeasureClothToolToggle() : base(ClothTool.Measure) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class DartClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Dart";
        public DartClothToolToggle() : base(ClothTool.Dart) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class PleatClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/Pleat";
        public PleatClothToolToggle() : base(ClothTool.Pleat) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class DrawPieceClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/DrawPiece";
        public DrawPieceClothToolToggle() : base(ClothTool.DrawPiece) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class BodyRegionClothToolToggle : ClothToolToggle
    {
        public const string Id = "UMA/ClothesDesigner/BodyRegion";
        public BodyRegionClothToolToggle() : base(ClothTool.BodyRegion) { }
    }
}
