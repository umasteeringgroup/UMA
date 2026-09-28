using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>Editor-only presentation preference. Never changes the inspected asset.</summary>
    public sealed class UMAInspectorView
    {
        private readonly string key;
        private bool initialized;
        private bool advanced;
        private SerializedObject owner;
        private readonly Dictionary<string, SerializedProperty> properties = new Dictionary<string, SerializedProperty>();
        private readonly Dictionary<string, string> sectionHelpKeys = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> sectionHelpStates = new Dictionary<string, bool>();
        private static readonly GUIContent[] Modes = { new GUIContent("Standard View"), new GUIContent("Advanced View") };
        private static readonly GUIContent HelpToggle = new GUIContent("Help", "Show a description of every option in this section.");
        private static readonly Dictionary<string, GUIContent> Labels = new Dictionary<string, GUIContent>();
        private static Texture2D panelTexture;
        private static GUIStyle panelStyle;
        private static bool proSkin;

        public UMAInspectorView(Type inspectedType) { key = PreferenceKey(inspectedType); }
        public static string PreferenceKey(Type inspectedType) => "UMA.InspectorView." + inspectedType.FullName;
        public bool Advanced => advanced;

        public bool DrawSelector()
        {
            // Refresh at Layout only: another locked Inspector can change the same preference.
            if (!initialized || Event.current.type == EventType.Layout)
            {
                advanced = EditorPrefs.GetBool(key, false);
                initialized = true;
            }
            bool changedBefore = GUI.changed;
            int selected = GUILayout.Toolbar(advanced ? 1 : 0, Modes, GUILayout.Height(25));
            GUI.changed = changedBefore;
            if (selected != (advanced ? 1 : 0))
            {
                EditorPrefs.SetBool(key, selected == 1);
                GUI.FocusControl(null);
                // The content tree has changed; do not reuse the previous Layout's controls.
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.Space(5);
            return advanced;
        }

        public SerializedProperty Property(SerializedObject serialized, string path)
        {
            if (!ReferenceEquals(owner, serialized)) { properties.Clear(); owner = serialized; }
            if (!properties.TryGetValue(path, out var property))
                properties[path] = property = serialized.FindProperty(path);
            return property;
        }

        public void Field(SerializedObject serialized, string path, string label = null)
        {
            var property = Property(serialized, path);
            if (property == null) return;
            if (label == null) EditorGUILayout.PropertyField(property, true);
            else
            {
                var content = Label(label);
                content.tooltip = property.tooltip;
                EditorGUILayout.PropertyField(property, content, true);
            }
        }

        public static GUIContent Label(string text)
        {
            if (!Labels.TryGetValue(text, out var label)) Labels[text] = label = new GUIContent(text);
            return label;
        }

        public static IDisposable Section(string title, bool enabled = true)
        {
            return enabled ? new PanelScope(title) : null;
        }

        /// <summary>
        /// Draws a standard inspector section with an editor-only, persistent Help toggle.
        /// The preference is scoped to both the inspected type and the section, and never
        /// changes or dirties the inspected asset.
        /// </summary>
        public IDisposable Section(string title, string helpText, bool enabled = true, string sectionId = null)
        {
            if (!enabled) return null;

            string id = string.IsNullOrEmpty(sectionId) ? title : sectionId;
            if (!sectionHelpKeys.TryGetValue(id, out string helpKey))
                sectionHelpKeys[id] = helpKey = key + ".Help." + id;

            if (!sectionHelpStates.TryGetValue(id, out bool showHelp) ||
                Event.current == null || Event.current.type == EventType.Layout)
            {
                showHelp = EditorPrefs.GetBool(helpKey, false);
                sectionHelpStates[id] = showHelp;
            }

            return new PanelScope(title, helpText, showHelp, this, id, helpKey);
        }

        private sealed class PanelScope : IDisposable
        {
            private readonly EditorGUILayout.VerticalScope scope;
            internal PanelScope(string title)
            {
                scope = new EditorGUILayout.VerticalScope(PanelStyle);
                if (!string.IsNullOrEmpty(title)) EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            }

            internal PanelScope(string title, string helpText, bool showHelp, UMAInspectorView owner, string sectionId, string helpKey)
            {
                scope = new EditorGUILayout.VerticalScope(PanelStyle);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!string.IsNullOrEmpty(title))
                        EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                    bool changedBefore = GUI.changed;
                    bool requested = EditorGUILayout.ToggleLeft(HelpToggle, showHelp, GUILayout.Width(52));
                    GUI.changed = changedBefore;
                    if (requested != showHelp)
                    {
                        owner.sectionHelpStates[sectionId] = requested;
                        EditorPrefs.SetBool(helpKey, requested);
                    }
                }
                // Keep the current event's control tree stable. A changed toggle is picked up
                // on the next Layout event, avoiding GUILayout mismatches without ExitGUI.
                if (showHelp && !string.IsNullOrEmpty(helpText))
                    EditorGUILayout.HelpBox(helpText, MessageType.Info);
            }
            public void Dispose() { scope.Dispose(); }
        }

        public static GUIStyle PanelStyle
        {
            get
            {
                if (panelStyle != null && panelTexture != null && proSkin == EditorGUIUtility.isProSkin) return panelStyle;
                ReleaseStyles();
                proSkin = EditorGUIUtility.isProSkin;
                // Nine-sliced rounded rectangle: one tiny editor-only texture, not one per repaint.
                const int size = 24;
                var pixels = new Color[size * size];
                Color fill = proSkin ? new Color(.28f, .28f, .28f) : new Color(.70f, .70f, .70f);
                Color border = new Color(.23f, .52f, .79f);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Max(Mathf.Abs(x + .5f - 12) - 5, 0);
                        float dy = Mathf.Max(Mathf.Abs(y + .5f - 12) - 5, 0);
                        float distance = Mathf.Sqrt(dx * dx + dy * dy) - 6;
                        Color pixel = Color.Lerp(fill, border, Mathf.Clamp01(distance + 1.5f));
                        pixel.a = Mathf.Clamp01(.5f - distance);
                        pixels[y * size + x] = pixel;
                    }
                panelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "UMA Inspector Section", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                panelTexture.SetPixels(pixels);
                panelTexture.Apply(false, true);
                panelStyle = new GUIStyle
                {
                    // IMGUI foldout arrows and reorderable-list headers extend left of the
                    // allocated field rect. Reserve that overhang in addition to the visible
                    // inner gutter; ordinary box padding alone lets them paint over the border.
                    border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(30, 18, 10, 10),
                    margin = new RectOffset(0, 0, 4, 7), normal = { background = panelTexture }
                };
                AssemblyReloadEvents.beforeAssemblyReload -= ReleaseStyles;
                AssemblyReloadEvents.beforeAssemblyReload += ReleaseStyles;
                EditorApplication.quitting -= ReleaseStyles;
                EditorApplication.quitting += ReleaseStyles;
                return panelStyle;
            }
        }

        private static void ReleaseStyles()
        {
            if (panelTexture != null) UnityEngine.Object.DestroyImmediate(panelTexture);
            panelTexture = null;
            panelStyle = null;
        }
    }
}
