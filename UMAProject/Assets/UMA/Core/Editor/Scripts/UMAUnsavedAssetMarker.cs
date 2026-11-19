using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Draws a small orange dot on the icon of every asset in the Project window that has changes in memory
    /// not yet written to disk (File > Save Project, Ctrl+S). Unity itself only marks an unsaved scene.
    /// Only assets Unity writes when it saves are marked (.asset, materials, prefabs, controllers and other
    /// Unity-serialized files): imported files such as textures, models and shaders can report changes that
    /// are never written. Turn it off with UMA > Asset Management > Mark Unsaved Assets in Project Window.
    /// </summary>
    [InitializeOnLoad]
    public static class UMAUnsavedAssetMarker
    {
        private const string PrefKey = "UMA.UnsavedAssetMarker.Enabled";
        private const string MenuPath = "UMA/Asset Management/Mark Unsaved Assets in Project Window";
        private const double PollSeconds = 0.5;

        // Rows drawn since the last poll, with the state they were drawn in. The Project window only
        // repaints on its own events, so a change of state is noticed here and a repaint asked for.
        private static readonly Dictionary<string, bool> drawn = new Dictionary<string, bool>();
        private static double nextPoll;
        private static GUIStyle dotStyle;
        private static readonly GUIContent dotContent = new GUIContent("●", "Unsaved changes: File > Save Project (Ctrl+S) writes them to disk.");

        static UMAUnsavedAssetMarker()
        {
            Apply();
        }

        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(PrefKey, true); }
            set
            {
                EditorPrefs.SetBool(PrefKey, value);
                Apply();
            }
        }

        [MenuItem(MenuPath, false, 200)]
        private static void ToggleMenu()
        {
            Enabled = !Enabled;
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleMenuValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void Apply()
        {
            // Remove first so a reload or a toggle never registers the callbacks twice.
            EditorApplication.projectWindowItemOnGUI -= DrawItem;
            EditorApplication.update -= Poll;
            drawn.Clear();
            if (Enabled)
            {
                EditorApplication.projectWindowItemOnGUI += DrawItem;
                EditorApplication.update += Poll;
            }
            EditorApplication.RepaintProjectWindow();
        }

        /// <summary>
        /// True when the asset at this path is loaded, has changes not yet written to disk, and is a file Unity
        /// writes when it saves assets. An asset that is not loaded has no unsaved changes, and it is not loaded here.
        /// </summary>
        public static bool IsUnsaved(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                return false;
            }
            if (!AssetDatabase.IsMainAssetAtPathLoaded(path))
            {
                return false;
            }
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null || !EditorUtility.IsDirty(asset))
            {
                return false;
            }
            return WrittenOnSave(path);
        }

        private static bool WrittenOnSave(string path)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                return false;
            }
            // Unity-serialized files (.asset, .mat, .controller, ...) report the base AssetImporter in Unity 6;
            // imported files use a subclass (TextureImporter, ModelImporter, ShaderImporter, ScriptedImporter, ...).
            System.Type importerType = importer.GetType();
            return importerType == typeof(AssetImporter) ||
                   importerType.Name == "NativeFormatImporter" || importerType.Name == "PrefabImporter";
        }

        private static void DrawItem(string guid, Rect rect)
        {
            if (string.IsNullOrEmpty(guid) || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            bool unsaved = IsUnsaved(AssetDatabase.GUIDToAssetPath(guid));
            drawn[guid] = unsaved;
            if (!unsaved)
            {
                return;
            }

            if (dotStyle == null)
            {
                dotStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };
                dotStyle.normal.textColor = new Color(1f, 0.55f, 0.1f);
            }

            // List view: on the lower left of the small icon. Grid view: on the upper left of the large icon.
            Rect dot = rect.height <= 22f
                ? new Rect(rect.x - 3f, rect.y + rect.height - 10f, 10f, 10f)
                : new Rect(rect.x + 1f, rect.y + 1f, 12f, 12f);
            GUI.Label(dot, dotContent, dotStyle);
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || drawn.Count == 0)
            {
                return;
            }
            nextPoll = EditorApplication.timeSinceStartup + PollSeconds;

            bool changed = false;
            foreach (KeyValuePair<string, bool> row in drawn)
            {
                if (IsUnsaved(AssetDatabase.GUIDToAssetPath(row.Key)) != row.Value)
                {
                    changed = true;
                    break;
                }
            }
            // The repaint lists the visible rows again, so rows that scrolled away are forgotten then.
            if (changed || drawn.Count > 400)
            {
                drawn.Clear();
                EditorApplication.RepaintProjectWindow();
            }
        }
    }
}
