#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UMA;
using UMA.Editors;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(UMAExpressionGroup))]
public sealed class UMAExpressionGroupInspector : Editor
{
    private readonly UMAInspectorView inspectorView =
        new UMAInspectorView(typeof(UMAExpressionGroup));
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
        bool advanced = inspectorView.DrawSelector();
        using (inspectorView.Section("Expression definitions",
            "Each definition gives an expression a stable ID, artist-facing name and DNA asset. Roles coordinate automatic systems such as visemes, blinking, gaze and emotion. Affected Joints declares which rig families the expression owns so Mecanim override policy can be enforced."))
        {
            DrawDropZone();
            if (advanced) _list.DoLayoutList();
            else DrawStandardDefinitions();
        }
        using (inspectorView.Section("Effects and performance",
            "The summary reports the number of DNA effects and their execution phases. Late Rig, blendshape and runtime-material effects are inexpensive frame lanes. Bold summaries contain effects that request an UMA build; use those sparingly for continuously animated expressions."))
            DrawPerformanceSummary();
        serializedObject.ApplyModifiedProperties();
        using (inspectorView.Section("Validation",
            "Validation checks stable IDs, duplicate procedural roles, missing DNA, incompatible effect configuration and ownership metadata. Resolve errors before assigning the group to a race; warnings identify behavior that may be intentional but deserves review."))
            DrawValidation();
    }

    private void DrawStandardDefinitions()
    {
        SerializedProperty list = _list.serializedProperty;
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Expression " + (i + 1),
                        EditorStyles.boldLabel);
                    if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                    {
                        list.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
                EditorGUILayout.PropertyField(item.FindPropertyRelative("id"),
                    new GUIContent("Stable ID"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative(
                    "displayName"), new GUIContent("Display Name"));
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(item.FindPropertyRelative(
                        "dna"), new GUIContent("DNA"));
                    UnityEngine.Object dna = item.FindPropertyRelative("dna")
                        .objectReferenceValue;
                    using (new EditorGUI.DisabledScope(dna == null))
                        if (GUILayout.Button("Inspect", GUILayout.Width(58f)))
                            Selection.activeObject = dna;
                }
                EditorGUILayout.PropertyField(item.FindPropertyRelative(
                    "roles"), new GUIContent("Behavior Roles"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative(
                    "affectedJoints"), new GUIContent("Joint Ownership"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative(
                    "responseTime"), new GUIContent("Response Time"));
                DNA dnaAsset = item.FindPropertyRelative("dna")
                    .objectReferenceValue as DNA;
                EditorGUILayout.LabelField(GetEffectSummary(dnaAsset),
                    GetCostStyle(dnaAsset));
            }
        }
        if (GUILayout.Button("Add Expression")) AddElement(_list);
    }

    private void DrawPerformanceSummary()
    {
        SerializedProperty list = _list.serializedProperty;
        int runtime = 0;
        int rebuilding = 0;
        int missing = 0;
        for (int i = 0; i < list.arraySize; i++)
        {
            DNA dna = list.GetArrayElementAtIndex(i)
                .FindPropertyRelative("dna").objectReferenceValue as DNA;
            if (dna == null) { missing++; continue; }
            bool builds = false;
            if (dna.effects != null)
                for (int e = 0; e < dna.effects.Count; e++)
                    builds |= dna.effects[e] != null &&
                        dna.effects[e].enabled &&
                        dna.effects[e].RequiresExpressionBuild;
            if (builds) rebuilding++; else runtime++;
        }
        EditorGUILayout.LabelField("Runtime-only definitions", runtime.ToString());
        EditorGUILayout.LabelField("Build-requesting definitions",
            rebuilding.ToString());
        EditorGUILayout.LabelField("Missing DNA", missing.ToString());
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
