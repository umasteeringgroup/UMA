using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>The Fit Brush's paint modes: names, tooltips, colours and icons, shared by the panel and the Fit Tools toolbar.</summary>
    public static class FitPaintInfo
    {
        /// <summary>The modes in the order their buttons are shown (Erase last).</summary>
        public static readonly FitPaintMode[] Order =
            { FitPaintMode.Hug, FitPaintMode.Smooth, FitPaintMode.Puff, FitPaintMode.Slim, FitPaintMode.Stiffen, FitPaintMode.Weight, FitPaintMode.Erase };

        /// <summary>The colour the brush's ring shows while taking paint off (Erase, or Ctrl-drag).</summary>
        public static readonly Color EraseColor = new Color(1f, 0.35f, 0.35f);

        public static string Name(FitPaintMode mode)
        {
            switch (mode)
            {
                case FitPaintMode.Hug: return "Hug";
                case FitPaintMode.Smooth: return "Smooth";
                case FitPaintMode.Puff: return "Puff";
                case FitPaintMode.Slim: return "Slim";
                case FitPaintMode.Stiffen: return "Stiffen";
                case FitPaintMode.Weight: return "Weight";
                default: return "Erase";
            }
        }

        public static string Tooltip(FitPaintMode mode)
        {
            switch (mode)
            {
                case FitPaintMode.Hug: return "Cut the cloth up to " + (FitPaint.MaxHugShrink * 100f).ToString("0") + "% smaller and draw it onto the body, as a shrink-wrap: sleeves, a bodice, a waist.";
                case FitPaintMode.Smooth: return "Flatten wrinkles and bumps (after hugging, round a collar, under the arms). The cloth keeps its size.";
                case FitPaintMode.Puff: return "Push the cloth out from the body, as padding or air would.";
                case FitPaintMode.Slim: return "Draw the cloth in towards the body.";
                case FitPaintMode.Stiffen: return "Make the cloth firmer: it stretches and bends less, up to about as firm as firm interfacing (a cuff, a band or a collar edge without a separate piece).";
                case FitPaintMode.Weight: return "Make the cloth heavier, up to " + (1f + FitPaint.MaxExtraWeight).ToString("0") + " times its weight: a weighted hem that hangs straight, the heavy edge of a cape.";
                default: return "Take every kind of paint off.";
            }
        }

        /// <summary>The colour the cloth turns where the mode is painted at full strength (Erase: the brush's red).</summary>
        public static Color PaintColor(FitPaintMode mode)
        {
            if (mode == FitPaintMode.Erase) return EraseColor;
            return FitPaint.Color(mode == FitPaintMode.Hug ? 1f : 0f, mode == FitPaintMode.Smooth ? 1f : 0f,
                mode == FitPaintMode.Puff ? 1f : mode == FitPaintMode.Slim ? -1f : 0f,
                mode == FitPaintMode.Stiffen ? 1f : 0f, mode == FitPaintMode.Weight ? 1f : 0f);
        }

        /// <summary>The modes as panel buttons, in <see cref="Order"/>: the name, with the tooltip.</summary>
        public static GUIContent[] PanelContents()
        {
            var contents = new GUIContent[Order.Length];
            for (int i = 0; i < Order.Length; i++) contents[i] = new GUIContent(Name(Order[i]), Tooltip(Order[i]));
            return contents;
        }

        private static readonly Dictionary<FitPaintMode, Texture2D> icons = new Dictionary<FitPaintMode, Texture2D>();

        /// <summary>A 32 px dot in the mode's paint colour, with a thin rim in the editor's icon grey so pale colours still show.</summary>
        public static Texture2D Icon(FitPaintMode mode)
        {
            if (icons.TryGetValue(mode, out Texture2D cached) && cached != null) return cached;
            const int size = 32;
            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Clothes Designer Fit " + Name(mode) + " Icon",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color fill = PaintColor(mode);
            Color rim = EditorGUIUtility.isProSkin ? new Color(0.77f, 0.77f, 0.77f) : new Color(0.24f, 0.24f, 0.24f);
            var pixels = new Color[size * size];
            Vector2 centre = new Vector2(size * 0.5f, size * 0.5f);
            const float radius = 11f, rimWidth = 1.6f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                    float cover = Mathf.Clamp01(radius + 0.5f - d);
                    float inner = Mathf.Clamp01(radius - rimWidth + 0.5f - d);
                    Color c = Color.Lerp(rim, fill, inner);
                    pixels[y * size + x] = new Color(c.r, c.g, c.b, cover);
                }
            }
            icon.SetPixels(pixels);
            icon.Apply(false, true);
            icons[mode] = icon;
            return icon;
        }
    }

    /// <summary>
    /// The Fit Brush's paint modes on a small Scene view toolbar of their own, shown while the Fit Brush is the
    /// Clothes Designer's tool and floated just above the Cloth Tools toolbar. The same modes stay in the panel.
    /// </summary>
    [Overlay(typeof(SceneView), Title, false)]
    public sealed class FitToolsOverlay : ToolbarOverlay
    {
        public const string Title = "Fit Tools";
        /// <summary>The space left between this toolbar and the Cloth Tools toolbar under it (GUI points).</summary>
        private const float Gap = 4f;

        private bool wasShown;
        private int placeTries;

        public FitToolsOverlay() : base(
            HugFitToolToggle.Id, SmoothFitToolToggle.Id, PuffFitToolToggle.Id, SlimFitToolToggle.Id,
            StiffenFitToolToggle.Id, WeightFitToolToggle.Id, EraseFitToolToggle.Id)
        {
        }

        /// <summary>The designer session while its tool is the Fit Brush, or null.</summary>
        public static DesignerSession BrushSession
        {
            get
            {
                DesignerSession session = ClothToolsOverlay.ActiveSession;
                return session != null && session.Tool == ClothTool.FitBrush ? session : null;
            }
        }

        public override void OnCreated()
        {
            base.OnCreated();
            wasShown = BrushSession != null;
            displayed = wasShown;
            EditorApplication.update += UpdateVisibility;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= UpdateVisibility;
            base.OnWillBeDestroyed();
        }

        private void UpdateVisibility()
        {
            // Shown when the Fit Brush becomes the tool and hidden when it stops being, so it is only there
            // while it can be used; never shown without it (see ClothToolInfo.ToolbarShown).
            bool active = BrushSession != null;
            bool started = active && !wasShown;
            bool show = ClothToolInfo.ToolbarShown(active, wasShown, displayed);
            wasShown = active;
            if (show != displayed) displayed = show;
            if (started) PlaceAboveClothTools();
        }

        /// <summary>
        /// Floats the toolbar just above the Cloth Tools toolbar, left edges lined up, once both are laid out
        /// (a few tries, a frame or two apart). Left where it is when the Cloth Tools toolbar is not shown.
        /// </summary>
        private void PlaceAboveClothTools()
        {
            placeTries = 12;
            rootVisualElement.schedule.Execute(TryPlace).Every(30).Until(() => placeTries <= 0);
        }

        private void TryPlace()
        {
            placeTries--;
            if (!displayed || containerWindow == null) { placeTries = 0; return; }
            if (!containerWindow.TryGetOverlay(ClothToolsOverlay.Title, out Overlay tools) || !tools.displayed) { placeTries = 0; return; }
            // Docked toolbars cannot be placed: float it first, and measure on a later try.
            if (!floating) { Undock(); return; }
            Rect mine = rootVisualElement.worldBound, theirs = tools.rootVisualElement.worldBound;
            if (float.IsNaN(mine.height) || mine.height < 1f || float.IsNaN(theirs.height) || theirs.height < 1f) return;
            // The floating position is measured from the overlay canvas's corner; find it from this toolbar.
            Vector2 canvasCorner = mine.position - floatingPosition;
            floatingPosition = new Vector2(theirs.xMin, theirs.yMin - mine.height - Gap) - canvasCorner;
            placeTries = 0;
        }
    }

    /// <summary>One paint mode on the Fit Tools toolbar: on while it is the session's paint mode.</summary>
    public abstract class FitPaintModeToggle : EditorToolbarToggle
    {
        private readonly FitPaintMode mode;

        protected FitPaintModeToggle(FitPaintMode mode)
        {
            this.mode = mode;
            text = FitPaintInfo.Name(mode);
            icon = FitPaintInfo.Icon(mode);
            tooltip = FitPaintInfo.Name(mode) + ": " + FitPaintInfo.Tooltip(mode) + (mode != FitPaintMode.Erase ? " Ctrl-drag takes this paint off." : "");
            this.RegisterValueChangedCallback(OnChanged);
            // The panel can change the mode too: follow it.
            schedule.Execute(Refresh).Every(150);
            Refresh();
        }

        public FitPaintMode Mode => mode;

        private void OnChanged(ChangeEvent<bool> change)
        {
            DesignerSession session = FitToolsOverlay.BrushSession;
            if (session == null) { SetValueWithoutNotify(false); return; }
            if (change.newValue && session.PaintMode != mode)
            {
                session.PaintMode = mode;
                SceneView.RepaintAll();
            }
            // There is always a mode: clicking the current one keeps it on.
            SetValueWithoutNotify(session.PaintMode == mode);
        }

        private void Refresh()
        {
            DesignerSession session = FitToolsOverlay.BrushSession;
            SetEnabled(session != null);
            SetValueWithoutNotify(session != null && session.PaintMode == mode);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class HugFitToolToggle : FitPaintModeToggle
    {
        public const string Id = "UMA/ClothesDesigner/Fit/Hug";
        public HugFitToolToggle() : base(FitPaintMode.Hug) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class SmoothFitToolToggle : FitPaintModeToggle
    {
        public const string Id = "UMA/ClothesDesigner/Fit/Smooth";
        public SmoothFitToolToggle() : base(FitPaintMode.Smooth) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class PuffFitToolToggle : FitPaintModeToggle
    {
        public const string Id = "UMA/ClothesDesigner/Fit/Puff";
        public PuffFitToolToggle() : base(FitPaintMode.Puff) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class SlimFitToolToggle : FitPaintModeToggle
    {
        public const string Id = "UMA/ClothesDesigner/Fit/Slim";
        public SlimFitToolToggle() : base(FitPaintMode.Slim) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class StiffenFitToolToggle : FitPaintModeToggle
    {
        public const string Id = "UMA/ClothesDesigner/Fit/Stiffen";
        public StiffenFitToolToggle() : base(FitPaintMode.Stiffen) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class WeightFitToolToggle : FitPaintModeToggle
    {
        public const string Id = "UMA/ClothesDesigner/Fit/Weight";
        public WeightFitToolToggle() : base(FitPaintMode.Weight) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class EraseFitToolToggle : FitPaintModeToggle
    {
        public const string Id = "UMA/ClothesDesigner/Fit/Erase";
        public EraseFitToolToggle() : base(FitPaintMode.Erase) { }
    }
}
