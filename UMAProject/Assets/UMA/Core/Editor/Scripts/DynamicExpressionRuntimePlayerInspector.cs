#if UNITY_EDITOR
using UMA;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DynamicExpressionPlayer))]
public sealed class DynamicExpressionRuntimePlayerInspector : Editor
{
    private readonly UMAInspectorView inspectorView =
        new UMAInspectorView(typeof(DynamicExpressionPlayer));
    private bool _showDiagnostics = true;
    private bool _showPreview = true;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (inspectorView.Section("Expression setup",
            "Expression Group Override replaces the active race's expression group for this player. When it is empty, the race group is used. Legacy Inline Expressions are only used when no group can be resolved; keep them for older content, but use an Expression Group for new compound DNA, blendshape, material, and bone effects."))
        {
            inspectorView.Field(serializedObject, "expressionGroupOverride", "Expression Group Override");
            inspectorView.Field(serializedObject, "Expressions", "Legacy Inline Expressions");
        }

        using (inspectorView.Section("Rig ownership",
            "Mecanim Override options let the expression player control the listed joint families after animation. Disable an override when the Animator or another runtime system must retain ownership. Generic Bone Joints classifies bones on Generic or non-humanoid rigs so expression effects can restore and apply them correctly."))
        {
            inspectorView.Field(serializedObject, "overrideMecanimEyes", "Override Mecanim Eyes");
            inspectorView.Field(serializedObject, "overrideMecanimJaw", "Override Mecanim Jaw");
            inspectorView.Field(serializedObject, "overrideMecanimNeck", "Override Mecanim Neck");
            inspectorView.Field(serializedObject, "overrideMecanimHead", "Override Mecanim Head");
            inspectorView.Field(serializedObject, "overrideMecanimHands", "Override Mecanim Hands");
            inspectorView.Field(serializedObject, "genericBoneJoints", "Generic Bone Joints");
        }

        using (inspectorView.Section("Natural head motion",
            "Natural Head Motion is active only while Override Mecanim Head is enabled. Idle Degrees adds restrained background movement. Speech Degrees adds small nods timed from active Viseme roles and their transitions. Expression Degrees adds slower movement from emotion and custom channels. Frequency controls the idle/expressive pace, and Ease Time prevents starts and stops from snapping. Authored head expressions and Animator look-at automatically reduce this procedural layer so intentional performances remain in control."))
        {
            SerializedProperty headOverride = inspectorView.Property(
                serializedObject, "overrideMecanimHead");
            inspectorView.Field(serializedObject,
                "EnableNaturalHeadMotion", "Enable Natural Head Motion");
            SerializedProperty enabled = inspectorView.Property(
                serializedObject, "EnableNaturalHeadMotion");
            if (!headOverride.boolValue)
                EditorGUILayout.HelpBox(
                    "Enable Override Mecanim Head in Rig ownership to apply " +
                    "natural head motion.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!enabled.boolValue ||
                !headOverride.boolValue))
            {
                inspectorView.Field(serializedObject,
                    "HeadMotionIdleDegrees", "Idle Movement (Degrees)");
                inspectorView.Field(serializedObject,
                    "HeadMotionSpeechDegrees", "Speech Movement (Degrees)");
                inspectorView.Field(serializedObject,
                    "HeadMotionExpressionDegrees",
                    "Expression Movement (Degrees)");
                inspectorView.Field(serializedObject,
                    "HeadMotionFrequency", "Movement Frequency");
                inspectorView.Field(serializedObject,
                    "HeadMotionEaseTime", "Ease Time");
            }
        }

        using (inspectorView.Section("Procedural eye behavior",
            "Saccades add small, irregular eye movements. Interval Min and Max set the delay range, Max Offset limits angular travel, and Vertical Bias reduces vertical motion relative to horizontal motion. Blinking generates automatic blink expressions; its interval range controls frequency, Duration controls the cycle length, and Blink Curve shapes eyelid closure over that cycle."))
        {
            inspectorView.Field(serializedObject, "EnableSaccades", "Enable Saccades");
            using (new EditorGUI.DisabledScope(!inspectorView.Property(serializedObject, "EnableSaccades").boolValue))
            {
                inspectorView.Field(serializedObject, "SaccadeIntervalMin", "Saccade Interval Min");
                inspectorView.Field(serializedObject, "SaccadeIntervalMax", "Saccade Interval Max");
                inspectorView.Field(serializedObject, "SaccadeMaxOffsetDeg", "Saccade Max Offset (Degrees)");
                inspectorView.Field(serializedObject, "SaccadeVerticalBias", "Saccade Vertical Bias");
            }
            inspectorView.Field(serializedObject, "EnableBlinking", "Enable Blinking");
            using (new EditorGUI.DisabledScope(!inspectorView.Property(serializedObject, "EnableBlinking").boolValue))
            {
                inspectorView.Field(serializedObject, "BlinkIntervalMin", "Blink Interval Min");
                inspectorView.Field(serializedObject, "BlinkIntervalMax", "Blink Interval Max");
                inspectorView.Field(serializedObject, "BlinkDuration", "Blink Duration");
                inspectorView.Field(serializedObject, "BlinkCurve", "Blink Curve");
            }
        }

        using (inspectorView.Section("Gaze and look-at",
            "Enable Look At aims the eyes and optionally assists with the head and body toward Look At Target. Min and Max Distance gate activation. Eye Max Angle limits eye-only travel; Head Assist Start and Full Angle blend in head motion. Gaze, Head, Body, and Eyes Weight set their contributions, while Clamp Vertical Angle limits steep up/down targets."))
        {
            inspectorView.Field(serializedObject, "EnableLookAt", "Enable Look At");
            using (new EditorGUI.DisabledScope(!inspectorView.Property(serializedObject, "EnableLookAt").boolValue))
            {
                inspectorView.Field(serializedObject, "LookAtTarget", "Look At Target");
                inspectorView.Field(serializedObject, "LookAtMaxDistance", "Maximum Distance");
                inspectorView.Field(serializedObject, "LookAtMinDistance", "Minimum Distance");
                inspectorView.Field(serializedObject, "EyeMaxAngle", "Eye Maximum Angle");
                inspectorView.Field(serializedObject, "HeadAssistStartAngle", "Head Assist Start Angle");
                inspectorView.Field(serializedObject, "HeadAssistFullAngle", "Head Assist Full Angle");
                inspectorView.Field(serializedObject, "GazeWeight", "Gaze Weight");
                inspectorView.Field(serializedObject, "HeadWeight", "Head Weight");
                inspectorView.Field(serializedObject, "BodyWeight", "Body Weight");
                inspectorView.Field(serializedObject, "EyesWeight", "Eyes Weight");
                inspectorView.Field(serializedObject, "ClampVerticalAngle", "Clamp Vertical Angle");
            }
        }

        using (inspectorView.Section("Processing and events",
            "Process Distance skips expression work beyond the camera distance; zero disables distance culling. Build Debounce coalesces rapid DNA-driven changes before requesting a build. Mesh Build Minimum Interval limits repeated mesh-affecting rebuilds. Expression Changed reports effective channel changes, and Group Rebound fires whenever the resolved group is rebound to the avatar."))
        {
            inspectorView.Field(serializedObject, "processDistance", "Process Distance");
            inspectorView.Field(serializedObject, "buildDebounceSeconds", "Build Debounce (Seconds)");
            inspectorView.Field(serializedObject, "meshBuildMinimumInterval", "Mesh Build Minimum Interval");
            inspectorView.Field(serializedObject, "ExpressionChanged", "Expression Changed");
            inspectorView.Field(serializedObject, "GroupRebound", "Group Rebound");
        }
        serializedObject.ApplyModifiedProperties();

        DynamicExpressionPlayer player =
            (DynamicExpressionPlayer)target;

        using (inspectorView.Section("Resolved configuration",
            "Resolved Group is the group currently bound after applying the override and active-race fallback rules. Pending Build shows that rapid expression changes have been coalesced and are waiting for the indicated UMA build phase. Rebind Group and Avatar refreshes all group, race, skeleton, renderer, and expression bindings."))
        {
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

            if (GUILayout.Button("Rebind Group and Avatar"))
            {
                player.Rebind();
                EditorUtility.SetDirty(player);
            }
        }

        using (inspectorView.Section("Transient preview",
            "Preview sliders apply the Manual source temporarily so expressions can be inspected without changing the expression assets. Channels containing build-phase effects may regenerate the UMA mesh or textures after the debounce delay. Reset Manual Preview clears only manual preview values; other active sources remain."))
        {
            _showPreview = EditorGUILayout.Foldout(_showPreview,
                "Show Preview Controls", true);
            if (_showPreview) DrawPreview(player);
        }

        using (inspectorView.Section("Runtime diagnostics",
            "Diagnostics shows each resolved channel's effective value, participating effect phases, affected joint families, and every currently active input source. Use it to identify whether manual input, animation, lip sync, or procedural blinking is winning source resolution."))
        {
            _showDiagnostics = EditorGUILayout.Foldout(_showDiagnostics,
                "Show Runtime Channels", true);
            if (_showDiagnostics)
            {
                DrawHeadMotionDiagnostics(player);
                DrawDiagnostics(player);
            }
        }
    }

    private static void DrawHeadMotionDiagnostics(
        DynamicExpressionPlayer player)
    {
        EditorGUILayout.LabelField("Natural Head Motion",
            EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField("Applied",
            player.NaturalHeadMotionActive ? "Yes" : "No",
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Offset (Degrees)",
            player.NaturalHeadMotionOffset.ToString("F3"),
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Speech / Expression Energy",
            player.NaturalHeadMotionSpeechEnergy.ToString("0.000") +
            " / " +
            player.NaturalHeadMotionExpressionEnergy.ToString("0.000"),
            EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(2f);
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
