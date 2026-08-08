#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class UMATaskItemEditorWindow : EditorWindow
{
    [SerializeField] private UMATaskItem _task;
    private SerializedObject _serializedTask;
    private Vector2 _scroll;

    public static void OpenNew()
    {
        UMATaskItem task = UMATaskListStorage.CreateNewTaskAsset();
        OpenWindow(task, "Add UMA Task");
        Selection.activeObject = task;
        EditorGUIUtility.PingObject(task);
    }

    public static void Open(UMATaskItem task)
    {
        if (task == null) return;
        OpenWindow(task, "Edit UMA Task");
    }

    private static void OpenWindow(UMATaskItem task, string title)
    {
        UMATaskItemEditorWindow window =
            CreateInstance<UMATaskItemEditorWindow>();
        window.titleContent = new GUIContent(title);
        window._task = task;
        window.Initialize();
        window.Show();
    }

    private void Initialize()
    {
        minSize = new Vector2(520f, 500f);
        if (_task != null)
            _serializedTask = new SerializedObject(_task);
    }

    private void OnEnable()
    {
        if (_serializedTask == null && _task != null)
            _serializedTask = new SerializedObject(_task);
    }

    private void OnGUI()
    {
        if (_task == null)
        {
            EditorGUILayout.HelpBox(
                "The task being edited is no longer available.",
                MessageType.Error);
            return;
        }
        if (_serializedTask == null) Initialize();

        _serializedTask.Update();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        SerializedProperty title =
            _serializedTask.FindProperty("_title");
        SerializedProperty date =
            _serializedTask.FindProperty("_taskDate");
        SerializedProperty category =
            _serializedTask.FindProperty("_category");
        SerializedProperty status =
            _serializedTask.FindProperty("_status");
        SerializedProperty description =
            _serializedTask.FindProperty("_description");
        SerializedProperty references =
            _serializedTask.FindProperty("_objectReferences");

        EditorGUILayout.LabelField(
            "Edit UMA Task", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(title, new GUIContent("Title"));
        date.stringValue = EditorGUILayout.DelayedTextField(
            new GUIContent(
                "Date",
                "Date in " + UMATaskItem.DateFormat + " format."),
            date.stringValue);
        EditorGUILayout.PropertyField(
            category, new GUIContent("Category"));
        EditorGUILayout.PropertyField(status, new GUIContent("Status"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Description", EditorStyles.boldLabel);
        description.stringValue = EditorGUILayout.TextArea(
            description.stringValue ?? string.Empty,
            GUILayout.MinHeight(140f));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(
            references,
            new GUIContent("Object References"),
            true);
        EditorGUILayout.EndScrollView();

        if (_serializedTask.ApplyModifiedProperties())
        {
            //EditorUtility.SetDirty(_task);
            //AssetDatabase.SaveAssetIfDirty(_task);
            //UMATaskListStorage.NotifyChanged();
        }

        if (string.IsNullOrWhiteSpace(_task.Title))
            EditorGUILayout.HelpBox(
                "A task title is required.", MessageType.Error);
        if (!_task.TryGetDate(out _))
            EditorGUILayout.HelpBox(
                "Enter a valid date in " + UMATaskItem.DateFormat +
                " format.",
                MessageType.Error);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save", GUILayout.Width(90f)))
        {
            if (string.IsNullOrWhiteSpace(_task.Title))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Task Title",
                    "A task title is required.",
                    "OK");
                return;
            }
            if (!_task.TryGetDate(out _))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Task Date",
                    "Enter a valid date in " + UMATaskItem.DateFormat +
                    " format.",
                    "OK");
                return;
            }
            EditorUtility.SetDirty(_task);
            AssetDatabase.SaveAssetIfDirty(_task);
            UMATaskListStorage.NotifyChanged();
            Close();
            GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("Close", GUILayout.Width(90f)))
        {
            Close();
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
