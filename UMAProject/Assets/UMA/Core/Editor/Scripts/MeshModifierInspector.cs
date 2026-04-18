using System;
using System.Collections.Generic;
using System.Linq;
using UMA;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    [CustomEditor(typeof(MeshModifier))]
    public class MeshModifierInspector : Editor
    {
        private SerializedProperty _runtimeModifiersProp;
        private SerializedProperty _editorModifiersProp;
        private SerializedProperty _splitDiagnosticsProp;
        private GUIStyle _headerStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _boxedStyle;

        private bool _showRuntimeModifiers = true;
        private bool _showEditorModifiers = false;
        private bool _showSplitDiagnostics = true;

        // Persist foldout states per asset instance id (not across domain reload, but stable during play/compiles)
        private static readonly Dictionary<int, List<bool>> FoldoutStates = new Dictionary<int, List<bool>>();
        private static readonly Dictionary<int, List<bool>> VertexFoldoutStates = new Dictionary<int, List<bool>>();
        private static readonly Dictionary<int, List<bool>> EditorFoldoutStates = new Dictionary<int, List<bool>>();
        private static readonly Dictionary<int, List<bool>> EditorVertexFoldoutStates = new Dictionary<int, List<bool>>();

        private int _instanceId;

        private void OnEnable()
        {
            _instanceId = target != null ? target.GetInstanceID() : 0; 
            AcquireProperties();
            EnsureFoldoutList();
        }

        private void AcquireProperties()
        {
            if (serializedObject != null)
            {
                _runtimeModifiersProp = serializedObject.FindProperty("runtimeModifiers");
                _editorModifiersProp = serializedObject.FindProperty("editorModifiers");
                _splitDiagnosticsProp = serializedObject.FindProperty("splitDiagnostics");
            }
        }

        private void EnsureStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            }
            if (_foldoutStyle == null)
            {
                _foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            }
            if (_boxedStyle == null)
            {
                _boxedStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(6, 6, 4, 6) };
            }
        }

        private void EnsureFoldoutList()
        {
            int size = 0;
            if (_runtimeModifiersProp != null)
            {
                size = _runtimeModifiersProp.arraySize;
            }
            EnsureFoldoutListFor(size);
        }

        private void EnsureFoldoutListFor(int size)
        {
            if (!FoldoutStates.ContainsKey(_instanceId))
            {
                FoldoutStates[_instanceId] = new List<bool>();
            }
            if (!VertexFoldoutStates.ContainsKey(_instanceId))
            {
                VertexFoldoutStates[_instanceId] = new List<bool>();
            }
            if (!EditorFoldoutStates.ContainsKey(_instanceId))
            {
                EditorFoldoutStates[_instanceId] = new List<bool>();
            }
            if (!EditorVertexFoldoutStates.ContainsKey(_instanceId))
            {
                EditorVertexFoldoutStates[_instanceId] = new List<bool>();
            }

            var modifierList = FoldoutStates[_instanceId];
            while (modifierList.Count < size) modifierList.Add(false);
            if (modifierList.Count > size) modifierList.RemoveRange(size, modifierList.Count - size);

            var vertexList = VertexFoldoutStates[_instanceId];
            while (vertexList.Count < size) vertexList.Add(false);
            if (vertexList.Count > size) vertexList.RemoveRange(size, vertexList.Count - size);
        }

        private void EnsureEditorFoldoutListFor(int size)
        {
            if (!EditorFoldoutStates.ContainsKey(_instanceId))
            {
                EditorFoldoutStates[_instanceId] = new List<bool>();
            }
            if (!EditorVertexFoldoutStates.ContainsKey(_instanceId))
            {
                EditorVertexFoldoutStates[_instanceId] = new List<bool>();
            }

            var modifierList = EditorFoldoutStates[_instanceId];
            while (modifierList.Count < size) modifierList.Add(false);
            if (modifierList.Count > size) modifierList.RemoveRange(size, modifierList.Count - size);

            var vertexList = EditorVertexFoldoutStates[_instanceId];
            while (vertexList.Count < size) vertexList.Add(false);
            if (vertexList.Count > size) vertexList.RemoveRange(size, vertexList.Count - size);
        }

        public override void OnInspectorGUI()
        {
            if (target == null) return; // Asset deleted or in transient state
            EnsureStyles();
            // Show compile/build status & protect modifications
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorGUILayout.HelpBox("Unity is compiling scripts... Editing disabled.", MessageType.Info);
                Repaint();
                return;
            }

            try
            {
                serializedObject.Update(); // May throw if in assembly reload edge case
            }
            catch
            {
                return; // bail out safely
            }

            // Reacquire property if lost (domain reload or layout rebuild)
            if (_runtimeModifiersProp == null)
            {
                AcquireProperties();
            }

            EditorGUILayout.LabelField("Mesh Modifier", _headerStyle);
            EditorGUILayout.Space(2);

            if (_runtimeModifiersProp == null)
            {
                EditorGUILayout.HelpBox("'runtimeModifiers' list not found on object.", MessageType.Error);
                return;
            }

            // Guard against null list in the underlying object (can happen after domain reload if list not serialized yet)
            var mm = target as MeshModifier;
            if (mm != null && mm.runtimeModifiers == null)
            {
                Undo.RecordObject(mm, "Initialize Modifiers List");
                mm.runtimeModifiers = new System.Collections.Generic.List<MeshModifier.Modifier>();
                EditorUtility.SetDirty(mm);
                serializedObject.Update();
            }

            int runtimeStackCount = mm != null && mm.runtimeModifiers != null ? mm.runtimeModifiers.Count : 0;
            int editorStackCount = mm != null && mm.EditorModifiers != null ? mm.EditorModifiers.Count : 0;
            int totalStackCount = runtimeStackCount + editorStackCount;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Modifier Stack Counts", EditorStyles.boldLabel);
                EditorGUILayout.IntField("Runtime Stacks", runtimeStackCount);
                EditorGUILayout.IntField("Editor Stacks", editorStackCount);
                EditorGUILayout.IntField("All Stacks", totalStackCount);
            }

            EnsureFoldoutList();

            using (new EditorGUILayout.VerticalScope(_boxedStyle))
            {
                _showSplitDiagnostics = EditorGUILayout.Foldout(_showSplitDiagnostics, "Split Diagnostics", true, _foldoutStyle);
                if (_showSplitDiagnostics)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (_splitDiagnosticsProp == null)
                        {
                            EditorGUILayout.HelpBox("No split diagnostics available.", MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(_splitDiagnosticsProp, new GUIContent(""));
                        }
                    }
                }

                _showEditorModifiers = EditorGUILayout.Foldout(_showEditorModifiers, "Editor Modifiers", true, _foldoutStyle);
                if (_showEditorModifiers)
                {
                    DrawModifierList(mm, _editorModifiersProp, "editor", allowAddRemove: false);
                }

                _showRuntimeModifiers = EditorGUILayout.Foldout(_showRuntimeModifiers, "Runtime Modifiers", true, _foldoutStyle);
                if (_showRuntimeModifiers)
                {
                    DrawModifierList(mm, _runtimeModifiersProp, "runtime", allowAddRemove: true);
                }
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                if (mm != null) EditorUtility.SetDirty(mm);
            }
        }

        private void AddModifier()
        {
            if (_runtimeModifiersProp == null) return;
            int newIndex = _runtimeModifiersProp.arraySize;
            _runtimeModifiersProp.InsertArrayElementAtIndex(newIndex);
            var newElement = _runtimeModifiersProp.GetArrayElementAtIndex(newIndex);
            // Clear string fields & set defaults
            var slotName = newElement.FindPropertyRelative("SlotName");
            if (slotName != null) slotName.stringValue = string.Empty;
            var dnaName = newElement.FindPropertyRelative("DNAName");
            if (dnaName != null) dnaName.stringValue = string.Empty;
            var scale = newElement.FindPropertyRelative("Scale");
            if (scale != null) scale.floatValue = 1f;
            EnsureFoldoutListFor(_runtimeModifiersProp.arraySize);
            if (FoldoutStates.TryGetValue(_instanceId, out var list))
            {
                while (list.Count < _runtimeModifiersProp.arraySize) list.Add(false);
                if (list.Count > 0) list[list.Count - 1] = false;
            }
            if (VertexFoldoutStates.TryGetValue(_instanceId, out var vertexList))
            {
                while (vertexList.Count < _runtimeModifiersProp.arraySize) vertexList.Add(false);
                if (vertexList.Count > 0) vertexList[vertexList.Count - 1] = false;
            }
        }

        private void RemoveModifierAt(int index)
        {
            if (_runtimeModifiersProp == null || index < 0 || index >= _runtimeModifiersProp.arraySize) return;
            _runtimeModifiersProp.DeleteArrayElementAtIndex(index);
            EnsureFoldoutListFor(_runtimeModifiersProp.arraySize);
        }

        private void DrawModifierList(MeshModifier mm, SerializedProperty listProp, string labelPrefix, bool allowAddRemove)
        {
            if (listProp == null)
            {
                EditorGUILayout.HelpBox($"'{labelPrefix}' modifiers list not available on this object.", MessageType.Info);
                return;
            }

            if (listProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("None.", MessageType.Info);
            }

            bool isEditor = labelPrefix == "editor";
            if (isEditor)
            {
                EnsureEditorFoldoutListFor(listProp.arraySize);
            }
            else
            {
                EnsureFoldoutListFor(listProp.arraySize);
            }

            var foldoutDict = isEditor ? EditorFoldoutStates : FoldoutStates;

            for (int i = 0; i < listProp.arraySize; i++)
            {
                var foldouts = foldoutDict[_instanceId];
                if (i >= foldouts.Count) foldouts.Add(false);

                var element = listProp.GetArrayElementAtIndex(i);
                string slotName = element.FindPropertyRelative("SlotName")?.stringValue ?? "(Unnamed)";
                string name = element.FindPropertyRelative("ModifierName")?.stringValue;
                if (string.IsNullOrEmpty(name))
                {
                    name = slotName;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    foldouts[i] = EditorGUILayout.Foldout(foldouts[i], $"{labelPrefix} {i + 1}: {name}", true, _foldoutStyle);

                    GUILayout.FlexibleSpace();

                    if (allowAddRemove)
                    {
                        if (GUILayout.Button(new GUIContent("x", "Remove this modifier"), GUILayout.Width(20)))
                        {
                            if (labelPrefix == "runtime")
                            {
                                RemoveModifierAt(i);
                            }
                            break;
                        }
                    }
                }

                if (!foldouts[i])
                {
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.indentLevel++;
                    var slotNameProp = element.FindPropertyRelative("SlotName");
                    var dnaNameProp = element.FindPropertyRelative("DNAName");
                    var scaleProp = element.FindPropertyRelative("Scale");
#if UNITY_EDITOR
                    var modName = element.FindPropertyRelative("ModifierName");
                    if (modName != null)
                    {
                        EditorGUILayout.PropertyField(modName, new GUIContent("Modifier Name"));
                    }
#endif
                    if (slotNameProp != null)
                    {
                        EditorGUILayout.PropertyField(slotNameProp, new GUIContent("Slot Name"));
                    }
                    if (dnaNameProp != null)
                    {
                        EditorGUILayout.PropertyField(dnaNameProp, new GUIContent("DNA Name"));
                    }
                    if (scaleProp != null)
                    {
                        float newScale = EditorGUILayout.Slider(new GUIContent("Scale"), scaleProp.floatValue, 0f, 5f);
                        if (!Mathf.Approximately(newScale, scaleProp.floatValue))
                        {
                            scaleProp.floatValue = Mathf.Clamp(newScale, 0f, 100f);
                        }
                    }

                    if (isEditor)
                    {
                        DrawEditorModifierDetails(mm, i);
                    }
                    else
                    {
                        DrawModifierDetails(mm, i);
                    }

                    EditorGUI.indentLevel--;
                }
            }

            if (allowAddRemove)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Modifier"))
                    {
                        AddModifier();
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawModifierDetails(MeshModifier meshModifier, int index)
        {
            if (meshModifier == null || meshModifier.runtimeModifiers == null || index < 0 || index >= meshModifier.runtimeModifiers.Count)
            {
                return;
            }

            var modifier = meshModifier.runtimeModifiers[index];
            if (modifier == null)
            {
                EditorGUILayout.HelpBox("Modifier data is null.", MessageType.Warning);
                return;
            }

            string collectionType = modifier.adjustments != null ? modifier.adjustments.GetType().Name : "None";
            string templateType = modifier.TemplateAdjustment != null ? modifier.TemplateAdjustment.GetType().Name : "None";
            int adjustmentCount = modifier.adjustments != null && modifier.adjustments.vertexAdjustments != null ? modifier.adjustments.vertexAdjustments.Count : 0;

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Adjustment Collection", collectionType);
            EditorGUILayout.LabelField("Template Type", templateType);
            EditorGUILayout.IntField("Adjustment Count", adjustmentCount);
            EditorGUILayout.Toggle("Keep As Is", modifier.keepAsIs);
            EditorGUILayout.Toggle("Manually Modified", modifier.manuallyModified);
            EditorGUILayout.Toggle("Temporary", modifier.isTemporary);

            if (!VertexFoldoutStates.TryGetValue(_instanceId, out var vertexFoldouts))
            {
                return;
            }

            while (vertexFoldouts.Count <= index)
            {
                vertexFoldouts.Add(false);
            }

            vertexFoldouts[index] = EditorGUILayout.Foldout(vertexFoldouts[index], "Vertices", true);
            if (!vertexFoldouts[index])
            {
                return;
            }

            if (adjustmentCount == 0)
            {
                EditorGUILayout.LabelField("None");
                return;
            }

            var vertices = modifier.adjustments.vertexAdjustments;
            var groupedBySlot = vertices.GroupBy(x => x != null ? x.slotName : string.Empty);
            foreach (var group in groupedBySlot)
            {
                string slotName = string.IsNullOrEmpty(group.Key) ? "(No Slot)" : group.Key;
                EditorGUILayout.LabelField($"Slot: {slotName} ({group.Count()})", EditorStyles.miniBoldLabel);

                foreach (var adjustment in group)
                {
                    if (adjustment == null)
                    {
                        EditorGUILayout.LabelField("- <null>");
                        continue;
                    }

                    EditorGUILayout.LabelField($"- v:{adjustment.vertexIndex} [{adjustment.GetType().Name}]");
                }
            }
        }

        private void DrawEditorModifierDetails(MeshModifier meshModifier, int index)
        {
            if (meshModifier == null || meshModifier.EditorModifiers == null || index < 0 || index >= meshModifier.EditorModifiers.Count)
            {
                return;
            }

            var modifier = meshModifier.EditorModifiers[index];
            if (modifier == null)
            {
                EditorGUILayout.HelpBox("Modifier data is null.", MessageType.Warning);
                return;
            }

            string collectionType = modifier.adjustments != null ? modifier.adjustments.GetType().Name : "None";
            string templateType = modifier.TemplateAdjustment != null ? modifier.TemplateAdjustment.GetType().Name : "None";
            int adjustmentCount = modifier.adjustments != null && modifier.adjustments.vertexAdjustments != null ? modifier.adjustments.vertexAdjustments.Count : 0;

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Adjustment Collection", collectionType);
            EditorGUILayout.LabelField("Template Type", templateType);
            EditorGUILayout.IntField("Adjustment Count", adjustmentCount);
            EditorGUILayout.Toggle("Keep As Is", modifier.keepAsIs);
            EditorGUILayout.Toggle("Manually Modified", modifier.manuallyModified);
            EditorGUILayout.Toggle("Temporary", modifier.isTemporary);

            if (!EditorVertexFoldoutStates.TryGetValue(_instanceId, out var vertexFoldouts))
            {
                return;
            }

            while (vertexFoldouts.Count <= index)
            {
                vertexFoldouts.Add(false);
            }

            vertexFoldouts[index] = EditorGUILayout.Foldout(vertexFoldouts[index], "Vertices", true);
            if (!vertexFoldouts[index])
            {
                return;
            }

            if (adjustmentCount == 0)
            {
                EditorGUILayout.LabelField("None");
                return;
            }

            var vertices = modifier.adjustments.vertexAdjustments;
            // For editor modifiers, group by slotName stored on each adjustment (legacy path)
            // or use the modifier's SlotName if adjustments don't have individual slot names
            string modifierSlotName = modifier.SlotName;
            var groupedBySlot = vertices.GroupBy(x => {
                if (x == null) return string.Empty;
                // Use adjustment's slotName if available, otherwise fall back to modifier's SlotName
                return !string.IsNullOrEmpty(x.slotName) ? x.slotName : modifierSlotName;
            });

            foreach (var group in groupedBySlot)
            {
                string slotName = string.IsNullOrEmpty(group.Key) ? "(No Slot)" : group.Key;
                EditorGUILayout.LabelField($"Slot: {slotName} ({group.Count()})", EditorStyles.miniBoldLabel);

                foreach (var adjustment in group)
                {
                    if (adjustment == null)
                    {
                        EditorGUILayout.LabelField("- <null>");
                        continue;
                    }

                    EditorGUILayout.LabelField($"- v:{adjustment.vertexIndex} [{adjustment.GetType().Name}]");
                }
            }
        }
    }
}