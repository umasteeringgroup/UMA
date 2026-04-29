using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SceneNoteRenderer
{
    private const string MENU_PATH = "UMA/Notes/Show Notes";
    private static bool notesEnabled = true;
    private static bool initialized = false;
    private static GUIStyle titleStyle;
    private static GUIStyle infoStyle;

    static SceneNoteRenderer()
    {
        // Default ON
        notesEnabled = EditorPrefs.GetBool(MENU_PATH, true);
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
        if (!notesEnabled)
            return;

        EnsureInitialized();

        var notes = Object.FindObjectsByType<SceneNote>(FindObjectsSortMode.None);
        if (notes == null || notes.Length == 0)
            return;

        Handles.BeginGUI();

        foreach (var note in notes)
        {
            if (!note.Visible)
                continue;

            Vector3 worldPos = note.transform.position + note.Offset;
            Vector2 guiPos = HandleUtility.WorldToGUIPoint(worldPos);

            Rect rect = new Rect(
                guiPos.x - note.Size.x / 2,
                guiPos.y - note.Size.y / 2,
                note.Size.x,
                note.Size.y
            );

            // Background
            Color old = GUI.color;
            GUI.color = note.Color;
            GUI.Box(rect, GUIContent.none);
            GUI.color = old;

            // Text
            GUILayout.BeginArea(rect);
            GUILayout.Label("<b>" + note.Title + "</b>", titleStyle);
            GUILayout.Label(note.Info, infoStyle);
            GUILayout.EndArea();
        }

        Handles.EndGUI();
    }
}
