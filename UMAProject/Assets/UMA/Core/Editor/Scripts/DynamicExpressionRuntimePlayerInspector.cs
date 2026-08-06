#if UNITY_EDITOR
using UMA;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DynamicExpressionPlayer))]
public sealed class DynamicExpressionRuntimePlayerInspector : Editor
{
    private bool _showDiagnostics = true;
    private bool _showPreview = true;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        DynamicExpressionPlayer player =
            (DynamicExpressionPlayer)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Resolved Configuration",
            EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Resolved Group",
                player.ResolvedGroup, typeof(UMAExpressionGroup), false);
        if (player.UsingTransientLegacyExpressionSet)
            EditorGUILayout.HelpBox(
                "Using a per-player transient conversion of the race's " +
                "legacy Expression Set. Convert it to an Expression Group " +
                "to author compound DNA effects.",
                MessageType.Info);

        if (player.expressionGroupOverride == null &&
            player.ResolvedGroup == null &&
            player.ExpressionCount == 0)
            EditorGUILayout.HelpBox(
                "No group is resolved. Assign an override, set the active " +
                "race's Expression Group, or use the legacy inline list.",
                MessageType.Warning);
        if (player.HasPendingBuild)
            EditorGUILayout.HelpBox("A coalesced expression build is pending: " +
                player.PendingBuildType, MessageType.Info);

        _showPreview = EditorGUILayout.Foldout(_showPreview,
            "Transient Preview", true);
        if (_showPreview) DrawPreview(player);

        _showDiagnostics = EditorGUILayout.Foldout(_showDiagnostics,
            "Runtime Diagnostics", true);
        if (_showDiagnostics) DrawDiagnostics(player);

        if (GUILayout.Button("Rebind Group and Avatar"))
        {
            player.Rebind();
            EditorUtility.SetDirty(player);
        }
    }

    private static void DrawPreview(DynamicExpressionPlayer player)
    {
        if (player.ExpressionCount == 0)
        {
            EditorGUILayout.HelpBox("No resolved channels.", MessageType.None);
            return;
        }
        const ExpressionEffectPhase buildPhases =
            ExpressionEffectPhase.BuildAfterRecipe |
            ExpressionEffectPhase.BuildPreApply |
            ExpressionEffectPhase.BuildApply |
            ExpressionEffectPhase.BuildPostApply;
        for (int i = 0; i < player.ExpressionCount; i++)
            if ((player.GetExpressionPhases(i) & buildPhases) != 0)
            {
                EditorGUILayout.HelpBox(
                    "One or more preview channels require an UMA build. " +
                    "Slider changes are debounced and may regenerate " +
                    "textures or meshes.",
                    MessageType.Warning);
                break;
            }
        for (int i = 0; i < player.ExpressionCount; i++)
        {
            player.TryGetExpression(i, out float value);
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider(
                player.GetExpressionId(i), value, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                player.SetExpression(i, next, ExpressionSource.Manual);
                player.EditorSimulateOnce();
                SceneView.RepaintAll();
            }
        }
        if (GUILayout.Button("Reset Manual Preview"))
        {
            player.ResetAllExpressions(ExpressionSource.Manual);
            player.EditorSimulateOnce();
        }
    }

    private static void DrawDiagnostics(DynamicExpressionPlayer player)
    {
        if (player.ExpressionCount == 0) return;
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("ID", EditorStyles.miniBoldLabel);
        GUILayout.Label("Value", EditorStyles.miniBoldLabel,
            GUILayout.Width(45));
        GUILayout.Label("Lanes", EditorStyles.miniBoldLabel,
            GUILayout.Width(145));
        GUILayout.Label("Joints", EditorStyles.miniBoldLabel,
            GUILayout.Width(70));
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < player.ExpressionCount; i++)
        {
            player.TryGetExpression(i, out float value);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(player.GetExpressionId(i));
            GUILayout.Label(value.ToString("0.000"), GUILayout.Width(45));
            GUILayout.Label(player.GetExpressionPhases(i).ToString(),
                EditorStyles.miniLabel, GUILayout.Width(145));
            GUILayout.Label(
                player.GetExpressionAffectedJoints(i).ToString(),
                EditorStyles.miniLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            for (ExpressionSource source = ExpressionSource.Manual;
                 source <= ExpressionSource.ProceduralBlink; source++)
            {
                if (player.TryGetSourceValue(i, source,
                    out float sourceValue, out bool active) && active)
                    EditorGUILayout.LabelField(source.ToString(),
                        sourceValue.ToString("0.000"),
                        EditorStyles.miniLabel);
            }
            EditorGUI.indentLevel--;
        }
    }
}
#endif
