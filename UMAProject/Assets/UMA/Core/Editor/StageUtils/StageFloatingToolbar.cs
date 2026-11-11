using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// A floating, draggable column of icon buttons for the editing stages' Scene views.
    /// Drag the grip to move it; double-click the grip to dock it beside the left panel
    /// again. The position is remembered in EditorPrefs.
    ///
    /// Icons are PNGs named "{prefix}{name}.png", with an optional "{prefix}{name}@2x.png"
    /// used on high-DPI displays. They are white glyphs, tinted for the editor skin.
    /// </summary>
    public sealed class StageFloatingToolbar
    {
        public const float ButtonSize = 28f;
        public const float Gap = 2f;
        public const float Padding = 4f;
        public const float GripHeight = 12f;
        public const float SeparatorHeight = 9f;

        private static readonly int GripHash = "UMAStageFloatingToolbarGrip".GetHashCode();
        private static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoadedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        private static GUIStyle buttonStyle;

        private readonly string positionPref;

        /// <summary>
        /// The file name prefix of this toolbar's icons. A stage that shows different tool
        /// sets in one toolbar (such as Sculpt and Vertex Paint) can change it before drawing.
        /// </summary>
        public string IconPrefix { get; set; }
        private Vector2 position = new Vector2(float.NaN, float.NaN);
        private bool positionLoaded;
        private Vector2 dragOffset;

        public StageFloatingToolbar(string positionPref, string iconPrefix)
        {
            this.positionPref = positionPref;
            IconPrefix = iconPrefix;
        }

        /// <summary>Where the toolbar is, in Scene view GUI coordinates.</summary>
        public Rect Rect { get; private set; }

        /// <summary>The x position of the buttons inside the toolbar.</summary>
        public float ButtonX => Rect.x + Padding;

        public static float Width => ButtonSize + (Padding * 2f);

        public static Color IconTint => EditorGUIUtility.isProSkin
            ? new Color(0.86f, 0.86f, 0.86f)
            : new Color(0.15f, 0.15f, 0.15f);

        public static float HeightFor(int buttons, int separators) =>
            GripHeight + Padding + (buttons * (ButtonSize + Gap)) + (separators * SeparatorHeight) + Padding;

        public bool Contains(Vector2 point) => Rect.Contains(point);

        /// <summary>
        /// Places the toolbar: where the user dragged it, or docked to the right of
        /// <paramref name="dockBeside"/>. Kept inside <paramref name="viewport"/>.
        /// </summary>
        public void Layout(Rect viewport, Rect dockBeside, float height, float margin)
        {
            LoadPosition();
            Vector2 at = float.IsNaN(position.x)
                ? new Vector2(dockBeside.xMax + margin, dockBeside.y)
                : position;
            at.x = Mathf.Clamp(at.x, viewport.x, Mathf.Max(viewport.x, viewport.xMax - Width - margin));
            at.y = Mathf.Clamp(at.y, viewport.y, Mathf.Max(viewport.y, viewport.yMax - height - margin));
            Rect = new Rect(at.x, at.y, Width, height);
        }

        /// <summary>Draws the background and grip and handles dragging. Returns the y of the first button.</summary>
        public float BeginFrame()
        {
            EnsureStyles();
            Rect rect = Rect;
            Event e = Event.current;
            bool pro = EditorGUIUtility.isProSkin;

            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, pro ? new Color(0.19f, 0.19f, 0.19f, 0.96f) : new Color(0.80f, 0.80f, 0.80f, 0.96f));
                Color edge = pro ? new Color(0f, 0f, 0f, 0.6f) : new Color(0.55f, 0.55f, 0.55f, 1f);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), edge);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), edge);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), edge);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), edge);
            }

            Rect grip = new Rect(rect.x, rect.y, rect.width, GripHeight);
            EditorGUIUtility.AddCursorRect(grip, MouseCursor.Pan);
            if (e.type == EventType.Repaint)
            {
                Color gripColor = pro ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f);
                for (int i = 0; i < 2; i++)
                {
                    EditorGUI.DrawRect(new Rect(grip.x + 9f, grip.y + 4f + (i * 3f), grip.width - 18f, 1f), gripColor);
                }
            }

            int gripId = GUIUtility.GetControlID(GripHash, FocusType.Passive, grip);
            switch (e.GetTypeForControl(gripId))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && grip.Contains(e.mousePosition))
                    {
                        if (e.clickCount == 2)
                        {
                            position = new Vector2(float.NaN, float.NaN);
                            SavePosition();
                        }
                        else
                        {
                            GUIUtility.hotControl = gripId;
                            dragOffset = e.mousePosition - rect.position;
                        }
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == gripId)
                    {
                        position = e.mousePosition - dragOffset;
                        e.Use();
                        SceneView.RepaintAll();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == gripId)
                    {
                        GUIUtility.hotControl = 0;
                        SavePosition();
                        e.Use();
                    }
                    break;
            }

            return grip.yMax + Padding;
        }

        /// <summary>
        /// Call after the last button: swallows clicks that start on the toolbar's background
        /// so they do not reach the Scene view tools underneath. Mouse-up is left alone, so a
        /// stroke released over the toolbar still ends.
        /// </summary>
        public void EndFrame()
        {
            Event e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.ScrollWheel) &&
                Rect.Contains(e.mousePosition))
            {
                e.Use();
            }
        }

        public void Separator(ref float y)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(
                    new Rect(ButtonX + 3f, y + (SeparatorHeight * 0.5f) - 1f, ButtonSize - 6f, 1f),
                    EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.2f) : new Color(0f, 0f, 0f, 0.25f));
            }
            y += SeparatorHeight;
        }

        /// <summary>Draws a toggle button and moves <paramref name="y"/> down. Returns true when it was clicked.</summary>
        public bool Toggle(ref float y, string icon, string label, string tooltip, bool on) =>
            Toggle(ref y, GetIcon(icon), label, tooltip, on);

        /// <summary>A toggle button with an icon from another set.</summary>
        public bool Toggle(ref float y, Texture2D icon, string label, string tooltip, bool on)
        {
            EnsureStyles();
            Rect rect = new Rect(ButtonX, y, ButtonSize, ButtonSize);
            y += ButtonSize + Gap;
            Color oldContent = GUI.contentColor;
            GUI.contentColor = IconTint;
            bool now = GUI.Toggle(rect, on, Content(icon, label, tooltip), buttonStyle);
            GUI.contentColor = oldContent;
            return now != on;
        }

        /// <summary>Draws an action button and moves <paramref name="y"/> down. Returns true when it was clicked.</summary>
        public bool Button(ref float y, string icon, string label, string tooltip) =>
            Button(ref y, GetIcon(icon), label, tooltip);

        /// <summary>An action button with an icon from another set, such as the shared UMAStage_ icons.</summary>
        public bool Button(ref float y, Texture2D icon, string label, string tooltip)
        {
            EnsureStyles();
            Rect rect = new Rect(ButtonX, y, ButtonSize, ButtonSize);
            y += ButtonSize + Gap;
            Color oldContent = GUI.contentColor;
            GUI.contentColor = IconTint;
            bool clicked = GUI.Button(rect, Content(icon, label, tooltip), buttonStyle);
            GUI.contentColor = oldContent;
            return clicked;
        }

        /// <summary>The prefix of the icons every stage toolbar shares, such as Frame.</summary>
        public const string SharedIconPrefix = "UMAStage_";

        /// <summary>This toolbar's icon by name (without its prefix), or null when it is missing.</summary>
        public Texture2D GetIcon(string name) => GetIcon(IconPrefix, name);

        public static Texture2D GetIcon(string prefix, string name)
        {
            if (!LoadedPrefixes.Contains(prefix))
            {
                LoadedPrefixes.Add(prefix);
                foreach (string guid in AssetDatabase.FindAssets(prefix + " t:Texture2D"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = Path.GetFileNameWithoutExtension(path);
                    if (file.StartsWith(prefix, StringComparison.Ordinal) && !IconCache.ContainsKey(file))
                    {
                        IconCache[file] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    }
                }
            }

            string key = prefix + name;
            if (EditorGUIUtility.pixelsPerPoint >= 1.5f &&
                IconCache.TryGetValue(key + "@2x", out Texture2D large) && large != null)
            {
                return large;
            }
            return IconCache.TryGetValue(key, out Texture2D icon) ? icon : null;
        }

        private GUIContent Content(string icon, string label, string tooltip) => Content(GetIcon(icon), label, tooltip);

        private static GUIContent Content(Texture2D texture, string label, string tooltip)
        {
            return texture != null
                ? new GUIContent(texture, label + "\n" + tooltip)
                : new GUIContent(label.Substring(0, Mathf.Min(2, label.Length)), label + "\n" + tooltip);
        }

        private static void EnsureStyles()
        {
            if (buttonStyle != null)
            {
                return;
            }
            GUIStyle baseStyle = GUI.skin.FindStyle("Command") ?? EditorStyles.miniButton;
            buttonStyle = new GUIStyle(baseStyle)
            {
                fixedWidth = ButtonSize,
                fixedHeight = ButtonSize,
                padding = new RectOffset(4, 4, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                imagePosition = ImagePosition.ImageOnly,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
            };
        }

        private void LoadPosition()
        {
            if (positionLoaded)
            {
                return;
            }
            positionLoaded = true;
            string[] parts = EditorPrefs.GetString(positionPref, string.Empty).Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                position = new Vector2(x, y);
            }
        }

        private void SavePosition()
        {
            if (float.IsNaN(position.x))
            {
                EditorPrefs.DeleteKey(positionPref);
                return;
            }
            EditorPrefs.SetString(
                positionPref,
                position.x.ToString(CultureInfo.InvariantCulture) + "," + position.y.ToString(CultureInfo.InvariantCulture));
        }
    }
}
