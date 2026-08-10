using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SceneNoteRenderer
{
    private const string MENU_PATH = "UMA/Notes/Show Notes";
    private static bool notesEnabled = false;
    private static bool initialized = false;
    private static GUIStyle titleStyle;
    private static GUIStyle infoStyle;

    static SceneNoteRenderer()
    {
        // Notes are opt-in because even optimized editor overlays should not affect projects that
        // do not use them. Existing users retain their saved preference.
        notesEnabled = EditorPrefs.GetBool(MENU_PATH, false);
        SceneView.duringSceneGui += OnSceneGUI;

        // Ensure menu checkmark matches state
        Menu.SetChecked(MENU_PATH, notesEnabled);
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        titleStyle = new GUIStyle(EditorStyles.label) { richText = true, fontSize = 12, fontStyle = FontStyle.Bold };
        infoStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true, fontSize = 10 };

        // Set menu checkmark based on saved state
        Menu.SetChecked(MENU_PATH, notesEnabled);
        initialized = true;
    }


    [MenuItem(MENU_PATH)]
    private static void ToggleNotes()
    {
        notesEnabled = !notesEnabled;
        EditorPrefs.SetBool(MENU_PATH, notesEnabled);
        Menu.SetChecked(MENU_PATH, notesEnabled);

        // Repaint scene views
        SceneView.RepaintAll();
    }

    [MenuItem("UMA/Notes/Hide Notes")]
    private static void HideNotes()
    {
        notesEnabled = false;
        EditorPrefs.SetBool(MENU_PATH, false);
        Menu.SetChecked(MENU_PATH, false);
        SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView view)
    {
        if (!notesEnabled || view == null || view.camera == null ||
            Event.current == null || Event.current.type != EventType.Repaint)
            return;

        EnsureInitialized();

        var notes = SceneNote.ActiveNotes;
        if (notes == null || notes.Count == 0)
            return;

        Handles.BeginGUI();
        Color originalColor = GUI.color;
        try
        {
            float viewWidth = view.position.width;
            float viewHeight = view.position.height;
            foreach (SceneNote note in notes)
            {
                if (note == null || !note.isActiveAndEnabled || !note.Visible ||
                    !note.gameObject.scene.IsValid() || !note.gameObject.scene.isLoaded ||
                    SceneVisibilityManager.instance.IsHidden(note.gameObject)) continue;

                Vector3 worldPos = note.transform.position + note.Offset;
                Vector3 viewportPos = view.camera.WorldToViewportPoint(worldPos);
                if (viewportPos.z <= 0f) continue;

                Vector2 guiPos = HandleUtility.WorldToGUIPoint(worldPos);
                float width = Mathf.Max(1f, note.Size.x);
                float height = Mathf.Max(1f, note.Size.y);
                Rect rect = new Rect(guiPos.x - width * 0.5f, guiPos.y - height * 0.5f,
                    width, height);
                if (rect.xMax < 0f || rect.yMax < 0f || rect.xMin > viewWidth ||
                    rect.yMin > viewHeight) continue;

                GUI.color = note.Color;
                GUI.Box(rect, GUIContent.none);
                GUI.color = originalColor;

                const float padding = 4f;
                float titleHeight = titleStyle.lineHeight + 2f;
                Rect titleRect = new Rect(rect.x + padding, rect.y + padding,
                    Mathf.Max(0f, rect.width - padding * 2f), titleHeight);
                Rect infoRect = new Rect(titleRect.x, titleRect.yMax,
                    titleRect.width, Mathf.Max(0f, rect.yMax - padding - titleRect.yMax));
                GUI.Label(titleRect, note.Title ?? string.Empty, titleStyle);
                GUI.Label(infoRect, note.Info ?? string.Empty, infoStyle);
            }
        }
        finally
        {
            GUI.color = originalColor;
            Handles.EndGUI();
        }
    }
}
