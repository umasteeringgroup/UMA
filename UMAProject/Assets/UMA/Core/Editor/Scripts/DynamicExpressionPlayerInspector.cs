#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UMA;
using UMA.PoseTools;
using System.IO;
using System.Collections.Generic;

[CustomEditor(typeof(DynamicExpressionPlayer))]
public class DynamicExpressionPlayerInspector : Editor
{
    private DynamicExpressionPlayer _player;
    private bool _showExpressions = true;
    private Vector2 _scroll;
    private bool _autoSimulate;
    private GUIStyle dropBoxStyle = null;
    private GUIStyle _dropBoxStyle
    {
        get 
        {
            if (dropBoxStyle == null)
            {
                dropBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Italic
                };
            }
            return dropBoxStyle;
        }
    }

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

        serializedObject.Update();
        DrawCoreControls();
        EditorGUILayout.Space();
        DrawSimulationControls();
        EditorGUILayout.Space();
        DrawDestinationFolder();
        EditorGUILayout.Space();
        DrawDropZone();
        EditorGUILayout.Space();
        DrawExpressions();
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCoreControls()
    {
        EditorGUILayout.LabelField("Dynamic Expression Player", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _player.EnableSaccades = EditorGUILayout.Toggle("Enable Saccades", _player.EnableSaccades);
        if (_player.EnableSaccades)
        {
            EditorGUI.indentLevel++;
            _player.SaccadeIntervalMin = EditorGUILayout.FloatField("Saccade Interval Min", _player.SaccadeIntervalMin);
            _player.SaccadeIntervalMax = EditorGUILayout.FloatField("Saccade Interval Max", _player.SaccadeIntervalMax);
            _player.SaccadeMaxOffsetDeg = EditorGUILayout.FloatField("Saccade Max Offset Deg", _player.SaccadeMaxOffsetDeg);
            _player.SaccadeVerticalBias = EditorGUILayout.Slider("Saccade Vertical Bias", _player.SaccadeVerticalBias, 0f, 1f);
            EditorGUI.indentLevel--;
        }
        _player.EnableBlinking = EditorGUILayout.Toggle("Enable Blinking", _player.EnableBlinking);
        if (_player.EnableBlinking)
        {
            EditorGUI.indentLevel++;
            _player.BlinkIntervalMin = EditorGUILayout.FloatField("Blink Interval Min", _player.BlinkIntervalMin);
            _player.BlinkIntervalMax = EditorGUILayout.FloatField("Blink Interval Max", _player.BlinkIntervalMax);
            _player.BlinkDuration = EditorGUILayout.FloatField("Blink Duration", _player.BlinkDuration);
            EditorGUI.indentLevel--;
        }
        _player.EnableLookAt = EditorGUILayout.Toggle("Enable LookAt", _player.EnableLookAt);
        if (_player.EnableLookAt)
        {
            EditorGUI.indentLevel++;
            _player.LookAtTarget = (Transform)EditorGUILayout.ObjectField("LookAt Target", _player.LookAtTarget, typeof(Transform), true);
            _player.LookAtMinDistance = EditorGUILayout.FloatField("LookAt Min Distance", _player.LookAtMinDistance);
            _player.LookAtMaxDistance = EditorGUILayout.FloatField("LookAt Max Distance", _player.LookAtMaxDistance);
            _player.HeadAssistStartAngle = EditorGUILayout.FloatField("Head Assist Start Angle", _player.HeadAssistStartAngle);
            _player.HeadAssistFullAngle = EditorGUILayout.FloatField("Head Assist Full Angle", _player.HeadAssistFullAngle);
            _player.GazeWeight = EditorGUILayout.Slider("Gaze Weight", _player.GazeWeight, 0f, 1f);
            _player.HeadWeight = EditorGUILayout.Slider("Head Weight", _player.HeadWeight, 0f, 1f);
            _player.BodyWeight = EditorGUILayout.Slider("Body Weight", _player.BodyWeight, 0f, 1f);
            _player.EyesWeight = EditorGUILayout.Slider("Eyes Weight", _player.EyesWeight, 0f, 1f);
            EditorGUI.indentLevel--;
        }
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

    private void DrawDestinationFolder()
    {
        EditorGUILayout.LabelField("DNA Creation Folder", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.TextField("Folder", _player.dnaCreationFolder);
        if (GUILayout.Button("Select Folder", GUILayout.Width(110)))
        {
            string abs = EditorUtility.OpenFolderPanel("Select DNA Destination Folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(abs))
            {
                string assetsAbs = Application.dataPath.Replace("\\", "/");
                string norm = abs.Replace("\\", "/");
                if (norm.StartsWith(assetsAbs))
                {
                    string rel = "Assets" + norm.Substring(assetsAbs.Length);
                    Undo.RecordObject(_player, "Change DNA Folder");
                    _player.dnaCreationFolder = rel;
                    EditorUtility.SetDirty(_player);
                    // Ensure AssetDatabase can see the folder immediately
                    if (!AssetDatabase.IsValidFolder(rel))
                    {
                        AssetDatabase.Refresh();
                        Repaint();
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Folder must be inside Assets.", "OK");
                }
            }
        }
        EditorGUILayout.EndHorizontal();
        if (!AssetDatabase.IsValidFolder(_player.dnaCreationFolder))
        {
            EditorGUILayout.HelpBox("Folder does not exist. A valid Assets subfolder is required for DNA creation.", MessageType.Warning);
        }
    }

    private void DrawDropZone()
    {
        Rect r = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(r, "Drag DNA or Bone Pose here to add expression", _dropBoxStyle);
        Event e = Event.current;
        if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && r.Contains(e.mousePosition))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is DNA dna)
                    {
                        AddDNAExpression(dna);
                    }
                    else if (obj is UMABonePose pose)
                    {
                        CreateDNAFromBonePose(pose);
                    }
                }
                e.Use();
            }
        }
    }

    private void AddDNAExpression(DNA dna)
    {
        if (dna == null) return;
        Undo.RecordObject(_player, "Add DNA Expression");
        var expr = new DynamicExpression
        {
            Name = string.IsNullOrEmpty(dna.displayName) ? dna.name : dna.displayName,
            ExpressionDNA = dna,
            ExpressionValue = new DNAInstance(dna.displayName, dna.defaultValue, null)
        };
        _player.Expressions.Add(expr);
        EditorUtility.SetDirty(_player);
    }

    private void CreateDNAFromBonePose(UMABonePose pose)
    {
        if (pose == null) return;
        if (!AssetDatabase.IsValidFolder(_player.dnaCreationFolder))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Cannot create DNA. Folder is invalid.", "OK");
            return;
        }
        string baseName = pose.name + "_DNA";
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(_player.dnaCreationFolder, baseName + ".asset"));
        var dna = ScriptableObject.CreateInstance<DNA>();
        dna.displayName = baseName;
        dna.defaultValue = 0.5f;
        dna.description = "Auto-generated DNA for bone pose: " + pose.name;
        var effect = new DNAEffect_BonePose { bonePose = pose };
        dna.effects.Add(effect);
        AssetDatabase.CreateAsset(dna, path);
        EditorUtility.SetDirty(dna);
        AssetDatabase.SaveAssets();
        AddDNAExpression(dna);
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
                Undo.RecordObject(_player, "Create DNAInstance");
                expr.ExpressionValue = new DNAInstance(expr.ExpressionDNA.displayName, expr.ExpressionDNA.defaultValue, null);
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

        if (GUILayout.Button("Add Empty Expression"))
        {
            Undo.RecordObject(_player, "Add Expression");
            list.Add(new DynamicExpression { Name = "NewExpression" });
            EditorUtility.SetDirty(_player);
        }
    }
}
#endif