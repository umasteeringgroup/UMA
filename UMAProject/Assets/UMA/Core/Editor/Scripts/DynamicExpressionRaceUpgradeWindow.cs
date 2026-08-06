#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UMA;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;

public static class DynamicExpressionRaceUpgradeUtility
{
    public static Dictionary<RaceData, UMAExpressionGroup> UpdateRaces(
        IReadOnlyList<RaceData> races,
        UMAExpressionGroup selectedGroup,
        bool createFromCurrentExpressionSet)
    {
        List<RaceData> targets = GetUniqueRaces(races);
        string error = GetValidationError(targets, selectedGroup,
            createFromCurrentExpressionSet);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException(error);

        Dictionary<UMAExpressionSet, UMAExpressionGroup> converted =
            new Dictionary<UMAExpressionSet, UMAExpressionGroup>();
        if (createFromCurrentExpressionSet)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                UMAExpressionSet source = targets[i].expressionSet;
                if (converted.ContainsKey(source)) continue;
                string sourcePath = AssetDatabase.GetAssetPath(source);
                string destination = Path.GetDirectoryName(sourcePath)
                    ?.Replace('\\', '/');
                UMAExpressionSetConverter.ConversionResult result =
                    UMAExpressionSetConverter.ConvertToAssets(
                        source, destination);
                converted.Add(source, result.group);
            }
        }

        UnityEngine.Object[] undoTargets =
            new UnityEngine.Object[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            undoTargets[i] = targets[i];
        Undo.RecordObjects(undoTargets,
            "Update Races to Dynamic Expression System");

        Dictionary<RaceData, UMAExpressionGroup> assignments =
            new Dictionary<RaceData, UMAExpressionGroup>();
        for (int i = 0; i < targets.Count; i++)
        {
            RaceData race = targets[i];
            UMAExpressionGroup group = createFromCurrentExpressionSet
                ? converted[race.expressionSet]
                : selectedGroup;
            race.expressionGroup = group;
            race.expressionSet = null;
            EditorUtility.SetDirty(race);
            assignments.Add(race, group);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return assignments;
    }

    public static string GetValidationError(
        IReadOnlyList<RaceData> races,
        UMAExpressionGroup selectedGroup,
        bool createFromCurrentExpressionSet)
    {
        List<RaceData> targets = GetUniqueRaces(races);
        if (targets.Count == 0)
            return "Select at least one RaceData asset.";

        if (createFromCurrentExpressionSet && selectedGroup != null)
            return "Choose an existing Expression Group or create one, not both.";
        if (!createFromCurrentExpressionSet && selectedGroup == null)
            return "Select an Expression Group or enable creation from the current Expression Set.";

        if (createFromCurrentExpressionSet)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                RaceData race = targets[i];
                if (race.expressionSet == null)
                    return "Race '" + race.name +
                        "' has no current Expression Set to convert.";
                string sourcePath =
                    AssetDatabase.GetAssetPath(race.expressionSet);
                if (string.IsNullOrEmpty(sourcePath))
                    return "The Expression Set on race '" + race.name +
                        "' must be saved as an asset before it can be converted.";
                string folder = Path.GetDirectoryName(sourcePath)
                    ?.Replace('\\', '/');
                if (string.IsNullOrEmpty(folder) ||
                    !AssetDatabase.IsValidFolder(folder))
                    return "Could not resolve the asset folder for Expression Set '" +
                        race.expressionSet.name + "'.";
            }
        }
        else
        {
            List<ExpressionValidationMessage> validation =
                new List<ExpressionValidationMessage>();
            if (!selectedGroup.Validate(validation))
            {
                for (int i = 0; i < validation.Count; i++)
                    if (validation[i].severity ==
                        ExpressionValidationSeverity.Error)
                        return "Expression Group is invalid: " +
                            validation[i].message;
                return "The selected Expression Group is invalid.";
            }
        }

        return null;
    }

    private static List<RaceData> GetUniqueRaces(
        IReadOnlyList<RaceData> races)
    {
        List<RaceData> result = new List<RaceData>();
        if (races == null) return result;
        HashSet<RaceData> found = new HashSet<RaceData>();
        for (int i = 0; i < races.Count; i++)
        {
            RaceData race = races[i];
            if (race != null && found.Add(race)) result.Add(race);
        }
        return result;
    }
}

public sealed class DynamicExpressionRaceUpgradeWindow : EditorWindow
{
    [SerializeField] private List<RaceData> _races = new List<RaceData>();
    [SerializeField] private UMAExpressionGroup _expressionGroup;
    [SerializeField] private bool _createFromCurrentExpressionSet;
    private Vector2 _scroll;

    [MenuItem(
        "Assets/UMA/Update To Dynamic Expression System",
        false,
        210)]
    private static void Open()
    {
        RaceData[] selected =
            Selection.GetFiltered<RaceData>(SelectionMode.Assets);
        DynamicExpressionRaceUpgradeWindow window =
            GetWindow<DynamicExpressionRaceUpgradeWindow>(
                true, "Update Expressions", true);
        window._races.Clear();
        window._races.AddRange(selected);
        window._expressionGroup = null;
        window._createFromCurrentExpressionSet = false;
        window.minSize = new Vector2(480f, 300f);
        window.Show();
    }

    [MenuItem(
        "Assets/UMA/Update To Dynamic Expression System",
        true)]
    private static bool CanOpen()
    {
        return Selection.GetFiltered<RaceData>(
            SelectionMode.Assets).Length > 0;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "Update to Dynamic Expression System",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "The selected races will receive an Expression Group and their " +
            "legacy Expression Set references will be removed.",
            MessageType.Warning);

        DrawSelectedRaces();
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        UMAExpressionGroup nextGroup =
            EditorGUILayout.ObjectField(
                "Expression Group",
                _expressionGroup,
                typeof(UMAExpressionGroup),
                false) as UMAExpressionGroup;
        if (EditorGUI.EndChangeCheck())
        {
            _expressionGroup = nextGroup;
            if (_expressionGroup != null)
                _createFromCurrentExpressionSet = false;
        }

        EditorGUI.BeginChangeCheck();
        bool create = EditorGUILayout.ToggleLeft(
            "Create Expression group from current race expression set",
            _createFromCurrentExpressionSet);
        if (EditorGUI.EndChangeCheck())
        {
            _createFromCurrentExpressionSet = create;
            if (create) _expressionGroup = null;
        }

        if (_createFromCurrentExpressionSet)
        {
            EditorGUILayout.HelpBox(
                "Each unique current Expression Set will be converted. The " +
                "new group and DNA assets will be saved beside that set.",
                MessageType.Info);
        }

        string error = DynamicExpressionRaceUpgradeUtility.GetValidationError(
            _races, _expressionGroup, _createFromCurrentExpressionSet);
        if (!string.IsNullOrEmpty(error))
            EditorGUILayout.HelpBox(error, MessageType.Error);

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
        {
            Close();
            GUIUtility.ExitGUI();
        }
        using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(error)))
        {
            if (GUILayout.Button("OK", GUILayout.Width(90f)))
            {
                if (ApplyUpdate()) GUIUtility.ExitGUI();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSelectedRaces()
    {
        EditorGUILayout.LabelField(
            "Selected RaceData (" + _races.Count + ")",
            EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(
            _scroll, GUILayout.Height(Mathf.Min(120f, 24f * _races.Count)));
        using (new EditorGUI.DisabledScope(true))
        {
            for (int i = 0; i < _races.Count; i++)
            {
                RaceData race = _races[i];
                EditorGUILayout.ObjectField(
                    race != null ? race.name : "Missing RaceData",
                    race, typeof(RaceData), false);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private bool ApplyUpdate()
    {
        try
        {
            Dictionary<RaceData, UMAExpressionGroup> assignments =
                DynamicExpressionRaceUpgradeUtility.UpdateRaces(
                    _races, _expressionGroup,
                    _createFromCurrentExpressionSet);
            if (_createFromCurrentExpressionSet && assignments.Count == 1)
            {
                foreach (UMAExpressionGroup group in assignments.Values)
                {
                    Selection.activeObject = group;
                    EditorGUIUtility.PingObject(group);
                    break;
                }
            }
            Close();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Expression Upgrade Failed",
                exception.Message,
                "OK");
            return false;
        }
    }
}
#endif
