#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

public enum UMATaskCategory
{
    General,
    Architecture,
    CoreRuntime,
    DNA,
    DynamicCharacterSystem,
    RaceAndAvatar,
    RecipesAndWardrobe,
    SlotsAndOverlays,
    MeshModifiers,
    MeshCombiners,
    TextureMerge,
    MaterialsAndShaders,
    Rendering,
    Animation,
    Expressions,
    Physics,
    LODAndPerformance,
    AssetIndexer,
    Addressables,
    Serialization,
    EditorTools,
    SampleCode,
    Documentation,
    Testing,
    BuildAndPackaging,
    Models,
    Clothing,
    Hair,
    Textures,
    Rigging,
    ArtAnimation
}

public enum UMATaskStatus
{
    New,
    InProcess,
    Cancelled,
    Done
}

public sealed class UMATaskItem : ScriptableObject
{
    public const string DateFormat = "yyyy-MM-dd";

    [SerializeField] private string _taskDate =
        DateTime.Today.ToString(DateFormat, CultureInfo.InvariantCulture);
    [SerializeField] private UMATaskCategory _category =
        UMATaskCategory.General;
    [SerializeField] private string _title = "New UMA Task";
    [SerializeField] private UMATaskStatus _status = UMATaskStatus.New;
    [SerializeField, TextArea(5, 20)] private string _description;
    [SerializeField] private List<UnityEngine.Object> _objectReferences =
        new List<UnityEngine.Object>();

    public string TaskDate
    {
        get => _taskDate;
        set => _taskDate = value;
    }

    public UMATaskCategory Category
    {
        get => _category;
        set => _category = value;
    }

    public string Title
    {
        get => _title;
        set => _title = value;
    }

    public UMATaskStatus Status
    {
        get => _status;
        set => _status = value;
    }

    public string Description
    {
        get => _description;
        set => _description = value;
    }

    public List<UnityEngine.Object> ObjectReferences => _objectReferences;

    public bool TryGetDate(out DateTime date)
    {
        return DateTime.TryParseExact(
            _taskDate,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    public void SetDate(DateTime date)
    {
        _taskDate = date.Date.ToString(
            DateFormat, CultureInfo.InvariantCulture);
    }

    private void OnValidate()
    {
        if (_objectReferences == null)
            _objectReferences = new List<UnityEngine.Object>();
    }
}

public static class UMATaskListStorage
{
    public const string TaskFolder = UMA.UMAPathUtility.TaskRoot;

    public static event Action TasksChanged;

    public static IReadOnlyList<UMATaskItem> LoadTasks()
    {
        EnsureTaskFolder();
        string[] guids = AssetDatabase.FindAssets(
            "t:UMATaskItem", new[] { TaskFolder });
        List<UMATaskItem> tasks = new List<UMATaskItem>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            UMATaskItem task =
                AssetDatabase.LoadAssetAtPath<UMATaskItem>(path);
            if (task != null) tasks.Add(task);
        }
        tasks.Sort(CompareTasks);
        return tasks;
    }

    public static UMATaskItem CreateNewTaskAsset()
    {
        EnsureTaskFolder();
        UMATaskItem asset = ScriptableObject.CreateInstance<UMATaskItem>();
        string path = AssetDatabase.GenerateUniqueAssetPath(
            TaskFolder + "/UMATaskItem.asset");
        AssetDatabase.CreateAsset(asset, path);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        NotifyChanged();
        return asset;
    }

    public static void SaveStatus(
        UMATaskItem task,
        UMATaskStatus status)
    {
        if (task == null) return;
        Undo.RecordObject(task, "Change UMA Task Status");
        task.Status = status;
        EditorUtility.SetDirty(task);
        AssetDatabase.SaveAssetIfDirty(task);
        NotifyChanged();
    }

    public static void NotifyChanged()
    {
        TasksChanged?.Invoke();
    }

    public static void EnsureTaskFolder()
    {
        if (AssetDatabase.IsValidFolder(TaskFolder)) return;
        string[] parts = TaskFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static int CompareTasks(UMATaskItem left, UMATaskItem right)
    {
        int date = string.Compare(
            left != null ? left.TaskDate : string.Empty,
            right != null ? right.TaskDate : string.Empty,
            StringComparison.Ordinal);
        if (date != 0) return date;
        return string.Compare(
            left != null ? left.Title : string.Empty,
            right != null ? right.Title : string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

}
#endif
