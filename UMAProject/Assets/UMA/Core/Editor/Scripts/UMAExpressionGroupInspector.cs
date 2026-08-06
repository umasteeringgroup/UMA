#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UMA;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(UMAExpressionGroup))]
public sealed class UMAExpressionGroupInspector : Editor
{
    private ReorderableList _list;
    private readonly List<ExpressionValidationMessage> _validation =
        new List<ExpressionValidationMessage>();

    private void OnEnable()
    {
        SerializedProperty expressions =
            serializedObject.FindProperty("expressions");
        _list = new ReorderableList(serializedObject, expressions,
            true, true, true, true);
        _list.drawHeaderCallback = rect =>
            EditorGUI.LabelField(rect, "Expression Definitions");
        _list.elementHeightCallback = index =>
            EditorGUI.GetPropertyHeight(
                expressions.GetArrayElementAtIndex(index), true) + 28f;
        _list.drawElementCallback = DrawElement;
        _list.onAddCallback = AddElement;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDropZone();
        _list.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
        DrawValidation();
    }

    private void DrawElement(Rect rect, int index, bool active, bool focused)
    {
        SerializedProperty element =
            _list.serializedProperty.GetArrayElementAtIndex(index);
        Rect propertyRect = new Rect(rect.x, rect.y + 2f, rect.width,
            EditorGUI.GetPropertyHeight(element, true));
        EditorGUI.PropertyField(propertyRect, element,
            new GUIContent("Expression " + index), true);

        DNA dna = element.FindPropertyRelative("dna").objectReferenceValue
            as DNA;
        Rect summary = new Rect(rect.x, propertyRect.yMax + 2f,
            rect.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(summary, GetEffectSummary(dna),
            GetCostStyle(dna));
    }

    private void AddElement(ReorderableList list)
    {
        int index = list.serializedProperty.arraySize;
        list.serializedProperty.InsertArrayElementAtIndex(index);
        SerializedProperty item =
            list.serializedProperty.GetArrayElementAtIndex(index);
        item.FindPropertyRelative("id").stringValue =
            MakeUniqueId("expression");
        item.FindPropertyRelative("displayName").stringValue =
            "Expression";
        item.FindPropertyRelative("dna").objectReferenceValue = null;
        item.FindPropertyRelative("roles").intValue =
            (int)ExpressionRole.Custom;
        item.FindPropertyRelative("affectedJoints").intValue =
            (int)ExpressionJoint.Other;
        item.FindPropertyRelative("priority").intValue = index;
        item.FindPropertyRelative("blendMode").enumValueIndex =
            (int)ExpressionBlendMode.Override;
        item.FindPropertyRelative("responseTime").floatValue = 0f;
        item.FindPropertyRelative("blinkClosedValue").floatValue = 0f;
    }

    private void DrawDropZone()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 42f,
            GUILayout.ExpandWidth(true));
        GUI.Box(rect, "Drop DNA assets to add expressions",
            EditorStyles.helpBox);
        Event current = Event.current;
        if (!rect.Contains(current.mousePosition) ||
            (current.type != EventType.DragUpdated &&
             current.type != EventType.DragPerform)) return;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (current.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (UnityEngine.Object dropped in
                DragAndDrop.objectReferences)
                if (dropped is DNA dna) AddDNA(dna);
        }
        current.Use();
    }

    private void AddDNA(DNA dna)
    {
        int index = _list.serializedProperty.arraySize;
        _list.serializedProperty.InsertArrayElementAtIndex(index);
        SerializedProperty item =
            _list.serializedProperty.GetArrayElementAtIndex(index);
        string display = !string.IsNullOrWhiteSpace(dna.displayName)
            ? dna.displayName : dna.name;
        item.FindPropertyRelative("id").stringValue =
            MakeUniqueId(MakeStableId(display));
        item.FindPropertyRelative("displayName").stringValue = display;
        item.FindPropertyRelative("dna").objectReferenceValue = dna;
        item.FindPropertyRelative("roles").intValue =
            (int)ExpressionRole.Custom;
        item.FindPropertyRelative("affectedJoints").intValue =
            (int)ExpressionJoint.Other;
        item.FindPropertyRelative("priority").intValue = index;
        item.FindPropertyRelative("blendMode").enumValueIndex =
            (int)ExpressionBlendMode.Override;
        item.FindPropertyRelative("responseTime").floatValue = 0f;
        item.FindPropertyRelative("blinkClosedValue").floatValue = 0f;
    }

    private void DrawValidation()
    {
        UMAExpressionGroup group = (UMAExpressionGroup)target;
        bool valid = group.Validate(_validation);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(valid ? "Validation: Ready" :
            "Validation: Errors", EditorStyles.boldLabel);
        for (int i = 0; i < _validation.Count; i++)
        {
            ExpressionValidationMessage message = _validation[i];
            MessageType type = message.severity ==
                ExpressionValidationSeverity.Error ? MessageType.Error :
                message.severity == ExpressionValidationSeverity.Warning
                    ? MessageType.Warning : MessageType.Info;
            string prefix = message.expressionIndex >= 0
                ? "[" + message.expressionIndex + "] " : string.Empty;
            EditorGUILayout.HelpBox(prefix + message.message, type);
        }
    }

    private string MakeUniqueId(string requested)
    {
        string candidate = requested;
        int suffix = 2;
        while (SerializedListContainsId(candidate))
            candidate = requested + "_" + suffix++;
        return candidate;
    }

    private bool SerializedListContainsId(string candidate)
    {
        SerializedProperty list = _list.serializedProperty;
        for (int i = 0; i < list.arraySize; i++)
        {
            string existing = list.GetArrayElementAtIndex(i)
                .FindPropertyRelative("id").stringValue;
            if (string.Equals(existing, candidate,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string MakeStableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "expression";
        char[] result = value.Trim().ToCharArray();
        for (int i = 0; i < result.Length; i++)
            if (!char.IsLetterOrDigit(result[i]) && result[i] != '_')
                result[i] = '_';
        return new string(result);
    }

    private static string GetEffectSummary(DNA dna)
    {
        if (dna == null) return "Missing DNA";
        ExpressionEffectPhase phases = ExpressionEffectPhase.None;
        if (dna.effects != null)
            for (int i = 0; i < dna.effects.Count; i++)
                if (dna.effects[i] != null && dna.effects[i].enabled)
                    phases |= dna.effects[i].ExpressionPhases;
        return dna.effects?.Count + " effect(s) - " + phases;
    }

    private static GUIStyle GetCostStyle(DNA dna)
    {
        if (dna?.effects == null) return EditorStyles.miniLabel;
        for (int i = 0; i < dna.effects.Count; i++)
            if (dna.effects[i] != null &&
                dna.effects[i].RequiresExpressionBuild)
                return EditorStyles.miniBoldLabel;
        return EditorStyles.miniLabel;
    }
}
#endif
