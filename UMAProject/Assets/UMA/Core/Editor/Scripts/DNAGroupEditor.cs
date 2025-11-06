using UnityEditor;
using UnityEngine;
using UMA;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using UMA.CharacterSystem;
using UMA.Editors;

[CustomEditor(typeof(DNAGroup))]
public class DNAGroupEditor : Editor
{
    private SerializedProperty dnaAreaProp;
    private SerializedProperty dnaListProp;
    private bool _changedThisGUI;
    private bool _pendingSave;
    private float _lastActionTime;
    private const float SaveDelaySeconds =0.5f;

    private void OnEnable()
    {
        dnaAreaProp = serializedObject.FindProperty("DNAArea");
        dnaListProp = serializedObject.FindProperty("dnaList");
        EditorApplication.update += DelayedSaveTick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= DelayedSaveTick;
    }

    private void MarkGroupDirtyAndQueueSave(UnityEngine.Object alsoDirty = null)
    {
        // Mark the owning DNAGroup asset dirty and optionally another related object
        if (alsoDirty != null)
        {
            EditorUtility.SetDirty(alsoDirty);
        }
        EditorUtility.SetDirty(target);
        _pendingSave = true;
        _lastActionTime = Time.realtimeSinceStartup;
    }

    private void DelayedSaveTick()
    {
        if (_pendingSave && (Time.realtimeSinceStartup - _lastActionTime) >= SaveDelaySeconds)
        {
            _pendingSave = false;
            AssetDatabase.SaveAssets();
        }
    }

    private static bool[] _foldoutStates = new bool[0];

    public override void OnInspectorGUI()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorGUILayout.HelpBox("Editor is compiling or updating. Please wait.", MessageType.Info);
            return;
        }

        serializedObject.Update();
        _changedThisGUI = false;

        GUILayout.Label("DNA Group Editor", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Ping DNAGroup Asset", GUILayout.Width(150)))
        {
            EditorGUIUtility.PingObject(target);
        }
        if (GUILayout.Button("Save Now", GUILayout.Width(100)))
        {
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
        if (GUILayout.Button("Rebuild Characters", GUILayout.Width(150)))
        {
            UMAAssetIndexer.RebuildAllUMAS();
        }
        GUILayout.EndHorizontal();
        EditorGUILayout.Space();
        DrawDragAndDropArea();
        EditorGUILayout.Space();


        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(dnaAreaProp, new GUIContent("DNA Area"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            _changedThisGUI = true;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("DNA List", EditorStyles.boldLabel);

        if (dnaListProp == null)
        {
            EditorGUILayout.HelpBox("dnaList property not found.", MessageType.Error);
        }
        else
        {
            if (_foldoutStates.Length != dnaListProp.arraySize)
            {
                _foldoutStates = new bool[dnaListProp.arraySize];
            }
            for (int i = 0; i < dnaListProp.arraySize; i++)
            {
                SerializedProperty dnaProp = dnaListProp.GetArrayElementAtIndex(i);
                DNA dnaObj = dnaProp.objectReferenceValue as DNA;

                string header = dnaObj != null ? dnaObj.name : $"DNA {i}";
                GUIHelper.FoldoutBarButton(ref _foldoutStates[i], header, "ping", out bool pressed, out bool delete);
                if (delete)
                {
                    Undo.RecordObject(target, "Remove DNA from Group");
                    dnaListProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    _changedThisGUI = true;
                    MarkGroupDirtyAndQueueSave();
                    break;
                }
                if (pressed)
                {
                    if (dnaObj != null)
                    {
                        InspectorUtlity.InspectTarget(dnaObj);
                    }
                }
                bool isExpanded = _foldoutStates[i];
                if (isExpanded)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    DrawDNAEntry(dnaProp, i);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        // Final apply and persistence if anything changed in this GUI pass
        if (_changedThisGUI)
        {
            serializedObject.ApplyModifiedProperties();
            MarkGroupDirtyAndQueueSave();
        }
        else
        {
            serializedObject.ApplyModifiedProperties();
        }
    }

    private void DrawDragAndDropArea()
    {
        // Visual box for drag & drop
        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 48.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag & Drop DNA assets here to add to group", EditorStyles.helpBox);

        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition))
            return;

        // Determine if any dragged objects are DNA
        bool hasDNA = false;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            var objs = DragAndDrop.objectReferences;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] is DNA)
                {
                    hasDNA = true;
                    break;
                }
            }

            if (hasDNA)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    // Build a set of existing DNA assets to avoid duplicates
                    HashSet<UnityEngine.Object> existing = new HashSet<UnityEngine.Object>();
                    for (int i = 0; i < dnaListProp.arraySize; i++)
                    {
                        var e = dnaListProp.GetArrayElementAtIndex(i);
                        if (e != null && e.objectReferenceValue != null)
                        {
                            existing.Add(e.objectReferenceValue);
                        }
                    }

                    bool anyAdded = false;
                    for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                    {
                        var obj = DragAndDrop.objectReferences[i];
                        if (obj is DNA dna && !existing.Contains(dna))
                        {
                            Undo.RecordObject(target, "Add DNA (Drag & Drop)");
                            int newIndex = dnaListProp.arraySize;
                            dnaListProp.arraySize++;
                            var elem = dnaListProp.GetArrayElementAtIndex(newIndex);
                            elem.objectReferenceValue = dna;
                            existing.Add(dna);
                            anyAdded = true;
                        }
                    }

                    if (anyAdded)
                    {
                        serializedObject.ApplyModifiedProperties();
                        _changedThisGUI = true;
                        MarkGroupDirtyAndQueueSave();
                    }

                    evt.Use();
                }
                else
                {
                    // Drag is over the box
                    evt.Use();
                }
            }
        }
    }

    private void DrawDNAEntry(SerializedProperty dnaProp, int index)
    {
        // DNA asset field
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(dnaProp, new GUIContent("DNA Asset"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            _changedThisGUI = true;
            MarkGroupDirtyAndQueueSave(dnaProp.objectReferenceValue);
        }

        var dnaObj = dnaProp.objectReferenceValue as DNA;
        if (dnaObj == null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create New DNA", GUILayout.Width(140)))
            {
                CreateAndAssignDNAAsset(dnaProp);
            }
            EditorGUILayout.EndHorizontal();
            return;
        }

        // Show DNA name
        EditorGUILayout.LabelField("DNA Name", dnaObj.name);

        // Effects list (read-only summary: Effect Name + Type)
        using (new EditorGUI.IndentLevelScope())
        {
            var dnaSO = new SerializedObject(dnaObj);
            var effectsProp = dnaSO.FindProperty("effects");

            if (effectsProp == null)
            {
                EditorGUILayout.HelpBox("Effects property not found on DNA.", MessageType.Error);
                return;
            }

            int count = effectsProp.isArray ? effectsProp.arraySize : 0;
            EditorGUILayout.LabelField("Effects", EditorStyles.boldLabel);
            if (count == 0)
            {
                EditorGUILayout.LabelField("(no effects)");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    SerializedProperty effProp = effectsProp.GetArrayElementAtIndex(i);
                    string effectName = string.Empty;
                    try
                    {
                        var nameProp = effProp.FindPropertyRelative("EffectName");
                        if (nameProp != null)
                        {
                            effectName = nameProp.stringValue;
                        }
                    }
                    catch { }

                    string typeName = effProp.managedReferenceFullTypename;
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        // format: AssemblyName TypeNamespace.TypeName
                        int lastDot = typeName.LastIndexOf('.');
                        if (lastDot >= 0 && lastDot < typeName.Length - 1)
                        {
                            typeName = typeName.Substring(lastDot + 1);
                        }
                    }
                    else if (effProp.objectReferenceValue != null)
                    {
                        typeName = effProp.objectReferenceValue.GetType().Name;
                    }
                    else
                    {
                        typeName = "Unknown";
                    }

                    string left = string.IsNullOrEmpty(effectName) ? "(unnamed effect)" : effectName;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(left, GUILayout.MinWidth(100));
                    EditorGUILayout.LabelField(typeName, GUILayout.MinWidth(80));
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        // Utility buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Ping", GUILayout.Width(60)))
        {
            EditorGUIUtility.PingObject(dnaObj);
        }
        if (GUILayout.Button("Inspect", GUILayout.Width(60)))
        {
            InspectorUtlity.InspectTarget(dnaObj);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void CreateAndAssignDNAAsset(SerializedProperty dnaProp)
    {
        string assetPath = GetTargetAssetPath();
        string fileName = "New DNA.asset";
        string path = EditorUtility.SaveFilePanelInProject("Create DNA", fileName, "asset", "Select a location for the new DNA asset", assetPath);
        if (string.IsNullOrEmpty(path)) return;

        var dna = ScriptableObject.CreateInstance<DNA>();
        dna.name = System.IO.Path.GetFileNameWithoutExtension(path);
        dna.description = "";
        dna.defaultValue =0.5f;

        AssetDatabase.CreateAsset(dna, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        dnaProp.objectReferenceValue = dna;
        serializedObject.ApplyModifiedProperties();
        _changedThisGUI = true;
        MarkGroupDirtyAndQueueSave(dna);
        EditorGUIUtility.PingObject(dna);
    }

    private string GetTargetAssetPath()
    {
        string path = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrEmpty(path))
        {
            path = "Assets";
        }
        else if (!System.IO.Directory.Exists(path))
        {
            path = System.IO.Path.GetDirectoryName(path);
        }
        return path;
    }

    private static List<Type> _cachedEffectTypes;
    private static List<Type> GetDNAEffectTypes()
    {
        if (_cachedEffectTypes != null) return _cachedEffectTypes;
        var list = new List<Type>();
        var baseType = typeof(DNAEffect);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            for (int i =0; i < types.Length; i++)
            {
                var t = types[i];
                if (t == null || t.IsAbstract) continue;
                if (baseType.IsAssignableFrom(t))
                {
                    list.Add(t);
                }
            }
        }
        // Sort by name for a stable menu
        list = list.OrderBy(t => t.Name).ToList();
        _cachedEffectTypes = list;
        return _cachedEffectTypes;
    }
}
