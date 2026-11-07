using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace UMA.TexturePaint.Editor
{
    internal sealed class TexturePaintGettingStartedWindow : EditorWindow
    {
        internal const string SuppressPreference = "UMA.OverlayPainter.HideInitialHelp";

#if UNITY_EDITOR_WIN
        private IntPtr nativeWindow;
        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        private void OnEnable()
        {
            EditorApplication.focusChanged += UpdateTopmost;
            EditorApplication.delayCall += InitializeTopmost;
        }

        private void OnDisable()
        {
            EditorApplication.focusChanged -= UpdateTopmost;
            EditorApplication.delayCall -= InitializeTopmost;
            if (nativeWindow != IntPtr.Zero && IsWindow(nativeWindow))
                SetTopmost(false);
            nativeWindow = IntPtr.Zero;
        }

        private void InitializeTopmost()
        {
            if (this != null) UpdateTopmost(EditorApplication.isFocused);
        }

        private void UpdateTopmost(bool applicationFocused)
        {
            if (nativeWindow == IntPtr.Zero || !IsWindow(nativeWindow))
            {
                nativeWindow = IntPtr.Zero;
                uint editorProcessId;
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    editorProcessId = (uint)process.Id;
                var title = new StringBuilder(256);
                EnumWindows((handle, parameter) =>
                {
                    GetWindowThreadProcessId(handle, out uint processId);
                    if (processId != editorProcessId) return true;
                    GetWindowText(handle, title, title.Capacity);
                    if (title.ToString() != titleContent.text) return true;
                    nativeWindow = handle;
                    return false;
                }, IntPtr.Zero);
            }
            if (nativeWindow != IntPtr.Zero) SetTopmost(applicationFocused);
        }

        private void SetTopmost(bool topmost)
        {
            const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010, NoOwnerZOrder = 0x0200;
            SetWindowPos(nativeWindow, new IntPtr(topmost ? -1 : -2), 0, 0, 0, 0,
                NoSize | NoMove | NoActivate | NoOwnerZOrder);
        }
#endif

        internal static void ScheduleFor(TexturePaintStageWindow stage)
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(SuppressPreference, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (stage != null && TexturePaintStageWindow.ActiveStage == stage &&
                    stage.Controller != null && !EditorPrefs.GetBool(SuppressPreference, false))
                    ShowWindow();
            };
        }

        [MenuItem("UMA/Overlay Painter/Show initial help dialog")]
        public static void ShowWindow()
        {
            // Manual reopening deliberately ignores the automatic-show preference.
            bool alreadyOpen = HasOpenInstances<TexturePaintGettingStartedWindow>();
            var window = GetWindow<TexturePaintGettingStartedWindow>(true, "Getting Started — Overlay Painter", true);
            window.minSize = new Vector2(520, 400);
            if (!alreadyOpen)
                window.position = new Rect(window.position.x, window.position.y, 580, 660);
            window.ShowUtility();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("painter-help");
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(TexturePaintAssets.ResolveInstallAssetPath(
                "OverlayPainter/Editor/TexturePaintGettingStartedWindow.uss"));
            if (stylesheet != null) root.styleSheets.Add(stylesheet);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "help-content" };
            scroll.AddToClassList("painter-help-content");
            root.Add(scroll);
            AddText(scroll, "Welcome to Overlay Painter", "painter-help-title");
            AddText(scroll, "Keep this guide open while you try the steps below.");
            AddText(scroll, "1. Choose what to paint", "painter-help-heading");
            AddText(scroll, "Select the Paint Target tab and choose the target you want to paint on. Only the current target receives your painting.");
            AddText(scroll, "2. Show useful reference targets", "painter-help-heading");
            AddText(scroll, "Click Show on any other targets you want visible in the paint scene. These are references, not paint targets. You can show or hide them at any time.");
            AddText(scroll, "3. Build your artwork with layers", "painter-help-heading");
            AddText(scroll, "Open Layers / Paths and add layers. Select a layer to edit its settings in Properties. Use its ON / OFF button to compare the result.");
            AddText(scroll, "Paint — draw directly with a brush.\nFill — cover the target with a color, texture or material.\nPath — follow an editable curve with details such as stitches or eyebrows.\nProjection — place an image or decal on the surface.\nPlugin — generate procedural details such as dirt or edge wear.\nGroup — organize related layers.");
            AddText(scroll, "4. Use masks to control where a layer appears", "painter-help-heading");
            AddText(scroll, "A mask hides parts of a layer without erasing its artwork: white shows, black hides, and gray shows partially. Paint a mask or add mask effects to control its coverage.");
            AddText(scroll, "To start from scratch on an equipped slot, turn off Original Overlay (base) at the bottom of the layer list.");
            AddText(scroll, "Save As keeps your editable project. Export creates textures for use in UMA. Reopen this guide from View > Show initial help dialog.", "painter-help-note");

            var footer = new VisualElement();
            footer.AddToClassList("painter-help-footer");
            root.Add(footer);
            footer.Add(new Button(Close) { text = "Close dialog", name = "close-help" });
            footer.Add(new Button(() =>
            {
                EditorPrefs.SetBool(SuppressPreference, true);
                Close();
            }) { text = "Close and do not show again", name = "suppress-help" });
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                Close();
                evt.StopPropagation();
            });
        }

        private static void AddText(VisualElement parent, string text, string className = null)
        {
            var label = new Label(text);
            label.AddToClassList("painter-help-text");
            if (className != null) label.AddToClassList(className);
            parent.Add(label);
        }
    }
}
