#if UNITY_EDITOR
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DynamicExpressionLegacyAdapter))]
public sealed class DynamicExpressionLegacyAdapterInspector : Editor
{
    private bool _showLegacyChannels = true;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty script = serializedObject.FindProperty("m_Script");
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(script);

        EditorGUILayout.HelpBox(
            "Compatibility adapter for clips and scripts authored against " +
            "the legacy 51 signed expression channels. It forwards values " +
            "to DynamicExpressionPlayer and never applies the old pose set.",
            MessageType.Info);

        DrawProperty("target", "Target Player");
        DrawProperty("forwardEveryFrame", "Forward Every Frame");
        DrawProperty("ExpressionChanged", "Expression Changed");

        DynamicExpressionLegacyAdapter adapter =
            (DynamicExpressionLegacyAdapter)target;
        DynamicExpressionPlayer resolvedTarget = adapter.target != null
            ? adapter.target
            : adapter.GetComponent<DynamicExpressionPlayer>();
        if (resolvedTarget == null)
        {
            EditorGUILayout.HelpBox(
                "No DynamicExpressionPlayer was found. Assign Target Player " +
                "or add DynamicExpressionPlayer to this GameObject.",
                MessageType.Warning);
        }
        else if (adapter.target == null)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Resolved Target", resolvedTarget,
                    typeof(DynamicExpressionPlayer), true);
        }

        EditorGUILayout.Space();
        _showLegacyChannels = EditorGUILayout.Foldout(_showLegacyChannels,
            "Legacy Channels (-1 to 1)", true);
        if (_showLegacyChannels)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < ExpressionPlayer.PoseCount; i++)
            {
                string channel = ExpressionPlayer.PoseNames[i];
                SerializedProperty property =
                    serializedObject.FindProperty(channel);
                if (property != null)
                {
                    property.floatValue = EditorGUILayout.Slider(
                        ObjectNames.NicifyVariableName(channel),
                        property.floatValue, -1f, 1f);
                }
            }
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Legacy Channels"))
        {
            Undo.RecordObject(adapter, "Reset Legacy Expression Channels");
            adapter.Values = new float[ExpressionPlayer.PoseCount];
            EditorUtility.SetDirty(adapter);
            if (Application.isPlaying) adapter.ForwardValues(true);
        }
        using (new EditorGUI.DisabledScope(resolvedTarget == null))
        {
            if (GUILayout.Button("Forward Values Now"))
            {
                adapter.ForwardValues(true);
                resolvedTarget.EvaluateExpressionsNow();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawProperty(string name, string label)
    {
        SerializedProperty property = serializedObject.FindProperty(name);
        if (property != null)
            EditorGUILayout.PropertyField(property, new GUIContent(label),
                true);
    }
}
#endif
