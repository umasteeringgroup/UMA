#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class UMATaskListWindow : EditorWindow
{
    private const float DateWidth = 95f;
    private const float CategoryWidth = 155f;
    private const float StatusWidth = 105f;
    private const float EditWidth = 54f;

    private readonly List<UMATaskItem> _tasks = new List<UMATaskItem>();
    private Vector2 _scroll;

    [MenuItem("UMA/Task List", false, 40)]
    public static void Open()
    {
        UMATaskListWindow window =
            GetWindow<UMATaskListWindow>("UMA Task List");
        window.minSize = new Vector2(700f, 300f);
        window.Show();
    }

    private void OnEnable()
    {
        UMATaskListStorage.TasksChanged += Refresh;
        EditorApplication.projectChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        UMATaskListStorage.TasksChanged -= Refresh;
        EditorApplication.projectChanged -= Refresh;
    }

    private void OnFocus()
    {
        Refresh();
    }

    private void Refresh()
    {
        IReadOnlyList<UMATaskItem> tasks =
            UMATaskListStorage.LoadTasks();
        _tasks.Clear();
        for (int i = 0; i < tasks.Count; i++) _tasks.Add(tasks[i]);
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawHeader();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_tasks.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No UMA tasks have been created. Click Add Task to begin.",
                MessageType.Info);
        }
        else
        {
            for (int i = 0; i < _tasks.Count; i++)
                DrawTask(_tasks[i]);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(
            "UMA Tasks (" + _tasks.Count + ")",
            EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            Refresh();
        if (GUILayout.Button("Add Task", EditorStyles.toolbarButton))
            UMATaskItemEditorWindow.OpenNew();
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("Date", EditorStyles.boldLabel,
            GUILayout.Width(DateWidth));
        GUILayout.Label("Category", EditorStyles.boldLabel,
            GUILayout.Width(CategoryWidth));
        GUILayout.Label("Title", EditorStyles.boldLabel,
            GUILayout.MinWidth(180f), GUILayout.ExpandWidth(true));
        GUILayout.Label("Status", EditorStyles.boldLabel,
            GUILayout.Width(StatusWidth));
        GUILayout.Label(string.Empty, GUILayout.Width(EditWidth));
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawTask(UMATaskItem task)
    {
        if (task == null) return;
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label(task.TaskDate, GUILayout.Width(DateWidth));
        GUILayout.Label(
            ObjectNames.NicifyVariableName(task.Category.ToString()),
            GUILayout.Width(CategoryWidth));
        GUIContent title = new GUIContent(
            task.Title,
            string.IsNullOrWhiteSpace(task.Description)
                ? task.Title
                : task.Description);
        GUILayout.Label(title, GUILayout.MinWidth(180f),
            GUILayout.ExpandWidth(true));

        EditorGUI.BeginChangeCheck();
        UMATaskStatus status = (UMATaskStatus)EditorGUILayout.EnumPopup(
            task.Status, GUILayout.Width(StatusWidth));
        if (EditorGUI.EndChangeCheck())
            UMATaskListStorage.SaveStatus(task, status);

        if (GUILayout.Button("Edit", GUILayout.Width(EditWidth)))
            UMATaskItemEditorWindow.Open(task);
        EditorGUILayout.EndHorizontal();
    }
}
#endif
