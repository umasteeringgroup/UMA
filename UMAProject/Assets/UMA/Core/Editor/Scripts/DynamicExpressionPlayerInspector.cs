#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UMA;

[CustomEditor(typeof(DynamicExpressionPlayer))]
public class DynamicExpressionPlayerInspector : Editor
{
    private DynamicExpressionPlayer _player;
    private bool _showExpressions = true;
    private Vector2 _scroll;
    private bool _autoSimulate;

    void OnEnable()
    {
        _player = target as DynamicExpressionPlayer;
    }

    public override void OnInspectorGUI()
    {
        if (_player == null)
        {
            base.OnInspectorGUI();
            return;
        }

        DrawCoreControls();
        EditorGUILayout.Space();
        DrawSimulationControls();
        EditorGUILayout.Space();
        DrawExpressions();
    }

    private void DrawCoreControls()
    {
        EditorGUILayout.LabelField("Dynamic Expression Player", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _player.EnableSaccades = EditorGUILayout.Toggle("Enable Saccades", _player.EnableSaccades);
        _player.EnableBlinking = EditorGUILayout.Toggle("Enable Blinking", _player.EnableBlinking);
        _player.EnableLookAt = EditorGUILayout.Toggle("Enable LookAt", _player.EnableLookAt);
        _player.LookAtTarget = (Transform)EditorGUILayout.ObjectField("LookAt Target", _player.LookAtTarget, typeof(Transform), true);
        _player.processDistance = EditorGUILayout.FloatField("Process Distance", _player.processDistance);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_player, "Change DynamicExpressionPlayer Settings");
            EditorUtility.SetDirty(_player);
            _player.EditorSimulateOnce();
        }
    }

    private void DrawSimulationControls()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Simulate Once", GUILayout.Width(120)))
        {
            _player.EditorSimulateOnce();
        }
        _autoSimulate = EditorGUILayout.ToggleLeft("Auto Simulate", _autoSimulate, GUILayout.Width(120));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (_autoSimulate && Event.current.type == EventType.Repaint)
        {
            _player.EditorSimulateOnce();
        }
    }

    private void DrawExpressions()
    {
        _showExpressions = EditorGUILayout.Foldout(_showExpressions, "Expressions", true);
        if (!_showExpressions) return;

        var list = _player.Expressions;
        if (list == null)
        {
            EditorGUILayout.HelpBox("No expression list.", MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(260));
        for (int i = 0; i < list.Count; i++)
        {
            var expr = list[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (expr == null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{i}] <null>");
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    Undo.RecordObject(_player, "Remove Expression");
                    list.RemoveAt(i);
                    EditorUtility.SetDirty(_player);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                continue;
            }

            EditorGUILayout.BeginHorizontal();
            expr.Name = EditorGUILayout.TextField("Name", expr.Name);
            if (GUILayout.Button("Remove", GUILayout.Width(70)))
            {
                Undo.RecordObject(_player, "Remove Expression");
                list.RemoveAt(i);
                EditorUtility.SetDirty(_player);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            expr.ExpressionDNA = (DNA)EditorGUILayout.ObjectField("DNA Asset", expr.ExpressionDNA, typeof(DNA), false);
            if (expr.ExpressionDNA != null && expr.ExpressionValue == null)
            {
                // Create a DNAInstance with default value
                Undo.RecordObject(_player, "Create DNAInstance");
                expr.ExpressionValue = new DNAInstance(expr.ExpressionDNA.displayName, expr.ExpressionDNA.defaultValue);
                EditorUtility.SetDirty(_player);
            }

            if (expr.ExpressionValue != null)
            {
                float prev = expr.ExpressionValue.Value;
                float newVal = EditorGUILayout.Slider("Value", prev, 0f, 1f);
                if (!Mathf.Approximately(prev, newVal))
                {
                    Undo.RecordObject(_player, "Change Expression Value");
                    expr.ExpressionValue.Value = newVal;
                    EditorUtility.SetDirty(_player);
                    _player.EditorSimulateOnce();
                }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    Undo.RecordObject(_player, "Reset Expression Value");
                    expr.ExpressionValue.Value = expr.ExpressionDNA != null ? expr.ExpressionDNA.defaultValue : 0.5f;
                    EditorUtility.SetDirty(_player);
                    _player.EditorSimulateOnce();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a DNA asset to enable value control.", MessageType.None);
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_player);
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Add Expression"))
        {
            Undo.RecordObject(_player, "Add Expression");
            list.Add(new DynamicExpression { Name = "NewExpression" });
            EditorUtility.SetDirty(_player);
        }
    }
}
#endif