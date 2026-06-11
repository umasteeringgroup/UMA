using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA
{
    [CustomEditor(typeof(UMALattice))]
    public class UMALatticeEditor : Editor
    {
        private UMALattice _lattice;
        private bool _sceneGuiRegistered;
        private bool _previewHiddenByCtrl;

        // --- inspector ---
        public override void OnInspectorGUI()
        {
            _lattice = (UMALattice)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("size"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("offset"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("cuts"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("damping"));
            bool dampingChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("curveMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("customCurve"));
            bool curveChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useGlobalDeformation"));
            bool globalChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("drawLattice"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("handleRadius"));
            EditorGUILayout.HelpBox("Shift-drag a selected handle to move its whole cut without deforming the mesh.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetFilter"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetRenderer"));
            bool targetChanged = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();

            if (dampingChanged)
            {
                _lattice.DeformTarget();
                EditorUtility.SetDirty(_lattice);
                SceneView.RepaintAll();
            }

            if (curveChanged)
            {
                _lattice.DeformTarget();
                EditorUtility.SetDirty(_lattice);
                SceneView.RepaintAll();
            }

            if (globalChanged)
            {
                _lattice.DeformTarget();
                EditorUtility.SetDirty(_lattice);
                SceneView.RepaintAll();
            }

            if (targetChanged)
            {
                Undo.RecordObject(_lattice, "Set Lattice Target");
                _lattice.CenterOnTarget();
                EditorUtility.SetDirty(_lattice);
                SceneView.RepaintAll();
            }

            DrawSlotSelection();
            DrawEffectors();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"Control Points: {_lattice.ControlPointCount}  |  Selected: {(_lattice.selectedHandleIndices.Count >0 ? string.Join(",", _lattice.selectedHandleIndices) : "none")}", EditorStyles.miniLabel);

            if (GUILayout.Button("Center Lattice on Target"))
            {
                Undo.RecordObject(_lattice, "Center Lattice");
                _lattice.CenterOnTarget();
                EditorUtility.SetDirty(_lattice);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Reset Control Points"))
            {
                Undo.RecordObject(_lattice, "Reset Lattice Control Points");
                _lattice.ResetControlPoints();
                _lattice.DeformTarget();
                EditorUtility.SetDirty(_lattice);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Restore Target Mesh"))
            {
                _lattice.RestoreTarget();
            }
        }

        private void DrawSlotSelection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("UMA Slot Filter", EditorStyles.boldLabel);

            SlotData[] slots = _lattice.GetUMASlots();
            if (slots == null || slots.Length == 0)
            {
                EditorGUILayout.HelpBox("No UMA slotDataList is available yet. With no slot selected, the full baked mesh is deformed.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(string.IsNullOrEmpty(_lattice.SelectedSlotName) ? "No slot checked: full baked mesh" : "Checked slot: " + _lattice.SelectedSlotName, EditorStyles.miniLabel);

            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                string slotName = UMALattice.GetSlotSelectionName(slot);
                bool hasMesh = slot != null && slot.asset != null && !UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData);
                int vertexCount = hasMesh ? slot.asset.meshData.vertexCount : 0;
                string label = string.IsNullOrEmpty(slotName) ? $"Slot {i}" : slotName;
                label = $"{label}  [{slot?.vertexOffset ?? -1}, {vertexCount}]";
                if (!hasMesh) label += " (no mesh)";

                using (new EditorGUI.DisabledScope(!hasMesh))
                {
                    bool wasChecked = hasMesh && _lattice.IsSelectedSlot(slot);
                    EditorGUI.BeginChangeCheck();
                    bool isChecked = EditorGUILayout.ToggleLeft(label, wasChecked);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_lattice, isChecked ? "Select UMA Lattice Slot" : "Clear UMA Lattice Slot");
                        _lattice.SetSelectedSlotName(isChecked ? slotName : string.Empty);
                        EditorUtility.SetDirty(_lattice);
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        private void DrawEffectors()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("UMA Effectors", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Effectors are child GameObjects. Select one to use the standard Move, Rotate, and Scale tools.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Box"))
                CreateEffector(UMAEffectorShape.Box);
            if (GUILayout.Button("Add Sphere"))
                CreateEffector(UMAEffectorShape.Sphere);
            if (GUILayout.Button("Add Capsule"))
                CreateEffector(UMAEffectorShape.Capsule);
            EditorGUILayout.EndHorizontal();

            _lattice.RefreshEffectorsFromChildren();
            UMAEffector[] effectors = _lattice.Effectors;
            if (effectors == null || effectors.Length == 0)
            {
                EditorGUILayout.HelpBox("No UMA effectors exist yet.", MessageType.Info);
                return;
            }

            for (int i = 0; i < effectors.Length; i++)
            {
                UMAEffector effector = effectors[i];
                if (effector == null)
                {
                    EditorGUILayout.HelpBox($"Missing effector at index {i}.", MessageType.Warning);
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    bool enabled = GUILayout.Toggle(effector.enabled, GUIContent.none, GUILayout.Width(18));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(effector, "Toggle UMA Effector");
                        effector.enabled = enabled;
                        EditorUtility.SetDirty(effector);
                        _lattice.DeformTarget();
                        SceneView.RepaintAll();
                    }

                    if (effector.mode == UMAEffectorMode.ScaleAlongNormal)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUI.BeginChangeCheck();
                        bool axisX = (effector.axisMask & UMAEffectorAxisMask.X) != 0;
                        bool axisY = (effector.axisMask & UMAEffectorAxisMask.Y) != 0;
                        bool axisZ = (effector.axisMask & UMAEffectorAxisMask.Z) != 0;

                        axisX = GUILayout.Toggle(axisX, "X", EditorStyles.miniButtonLeft, GUILayout.Width(36));
                        axisY = GUILayout.Toggle(axisY, "Y", EditorStyles.miniButtonMid, GUILayout.Width(36));
                        axisZ = GUILayout.Toggle(axisZ, "Z", EditorStyles.miniButtonRight, GUILayout.Width(36));

                        if (EditorGUI.EndChangeCheck())
                        {
                            UMAEffectorAxisMask newMask = UMAEffectorAxisMask.None;
                            if (axisX) newMask |= UMAEffectorAxisMask.X;
                            if (axisY) newMask |= UMAEffectorAxisMask.Y;
                            if (axisZ) newMask |= UMAEffectorAxisMask.Z;
                            Undo.RecordObject(effector, "Change UMA Effector Axes");
                            effector.axisMask = newMask;
                            EditorUtility.SetDirty(effector);
                            _lattice.DeformTarget();
                            SceneView.RepaintAll();
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUILayout.DelayedTextField(effector.gameObject.name);
                    if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(newName) && newName != effector.gameObject.name)
                    {
                        Undo.RecordObject(effector.gameObject, "Rename UMA Effector");
                        effector.gameObject.name = newName;
                        EditorUtility.SetDirty(effector.gameObject);
                    }

                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                    {
                        Selection.activeGameObject = effector.gameObject;
                        EditorGUIUtility.PingObject(effector.gameObject);
                    }

                    if (GUILayout.Button("Mirror", GUILayout.Width(60)))
                    {
                        MirrorEffector(effector);
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    if (GUILayout.Button("Delete", GUILayout.Width(60)))
                    {
                        DeleteEffector(effector);
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField($"{effector.shape} | {effector.mode} | Amount {effector.amount:0.0000} | {(effector.accumulate ? "Accumulate" : "Replace")} | {(effector.simulateVertexMerging ? "Merge Verts" : "No Merge")}", EditorStyles.miniLabel);
                }
            }
        }

        private void CreateEffector(UMAEffectorShape shape)
        {
            Undo.IncrementCurrentGroup();
            GameObject effectorObject = new GameObject(GetUniqueEffectorName(shape.ToString()));
            Undo.RegisterCreatedObjectUndo(effectorObject, "Create UMA Effector");
            Undo.SetTransformParent(effectorObject.transform, _lattice.transform, "Parent UMA Effector");

            effectorObject.transform.localPosition = _lattice.offset + (_lattice.size * 0.5f);
            effectorObject.transform.localRotation = Quaternion.identity;
            effectorObject.transform.localScale = Vector3.one;

            UMAEffector effector = Undo.AddComponent<UMAEffector>(effectorObject);
            effector.shape = shape;
            effector.mode = UMAEffectorMode.ScaleAlongNormal;
            effector.amount = 0.001f;
            effector.accumulate = true;

            _lattice.RefreshEffectorsFromChildren();
            _lattice.DeformTarget();
            EditorUtility.SetDirty(_lattice);
            Selection.activeGameObject = effectorObject;
            SceneView.RepaintAll();
        }

        private void DeleteEffector(UMAEffector effector)
        {
            if (effector == null)
                return;

            Undo.DestroyObjectImmediate(effector.gameObject);
            _lattice.RefreshEffectorsFromChildren();
            _lattice.DeformTarget();
            EditorUtility.SetDirty(_lattice);
            SceneView.RepaintAll();
        }

        private void MirrorEffector(UMAEffector sourceEffector)
        {
            if (sourceEffector == null)
                return;

            Undo.IncrementCurrentGroup();
            GameObject mirroredObject = Object.Instantiate(sourceEffector.gameObject);
            Undo.RegisterCreatedObjectUndo(mirroredObject, "Mirror UMA Effector");
            Undo.SetTransformParent(mirroredObject.transform, sourceEffector.transform.parent, "Parent Mirrored UMA Effector");

            Vector3 mirroredLocalPosition = sourceEffector.transform.localPosition;
            mirroredLocalPosition.x = -mirroredLocalPosition.x;
            mirroredObject.transform.localPosition = mirroredLocalPosition;
            mirroredObject.transform.localRotation = MirrorXAxis(sourceEffector.transform.localRotation);
            mirroredObject.transform.localScale = sourceEffector.transform.localScale;
            mirroredObject.name = GetMirroredEffectorName(sourceEffector.gameObject.name);

            _lattice.RefreshEffectorsFromChildren();
            _lattice.DeformTarget();
            EditorUtility.SetDirty(_lattice);
            Selection.activeGameObject = _lattice != null ? _lattice.gameObject : sourceEffector.transform.parent != null ? sourceEffector.transform.parent.gameObject : null;
            SceneView.RepaintAll();
        }

        private string GetUniqueEffectorName(string baseName)
        {
            string candidate = baseName + " Effector";
            HashSet<string> usedNames = new HashSet<string>();
            Transform root = _lattice.transform;
            for (int i = 0; i < root.childCount; i++)
                usedNames.Add(root.GetChild(i).name);

            if (!usedNames.Contains(candidate))
                return candidate;

            int suffix = 2;
            while (usedNames.Contains(candidate + " " + suffix))
                suffix++;

            return candidate + " " + suffix;
        }

        private string GetMirroredEffectorName(string sourceName)
        {
            const string effectorSuffix = " Effector";
            string baseName = sourceName.EndsWith(effectorSuffix)
                ? sourceName.Substring(0, sourceName.Length - effectorSuffix.Length)
                : sourceName;

            string candidate = baseName + " Mirror";
            HashSet<string> usedNames = new HashSet<string>();
            Transform root = _lattice.transform;
            for (int i = 0; i < root.childCount; i++)
                usedNames.Add(root.GetChild(i).name);

            if (!usedNames.Contains(candidate))
                return candidate;

            int suffix = 2;
            while (usedNames.Contains(candidate + " " + suffix))
                suffix++;

            return candidate + " " + suffix;
        }

        private static Quaternion MirrorXAxis(Quaternion rotation)
        {
            rotation.x *= -1f;
            rotation.w *= -1f;
            return rotation;
        }

        // --- scene view ---
        private void OnEnable()
        {
            _lattice = (UMALattice)target;
            RegisterSceneGui();
            RegisterUpdate();
        }

        private void OnDisable()
        {
            UnregisterUpdate();
            if (_lattice != null)
                _lattice.SetPreviewVisible(true);
            UnregisterSceneGui();
        }

        private void RegisterUpdate()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void UnregisterUpdate()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (_sceneGuiRegistered)
                SceneView.RepaintAll();
        }

        private void RegisterSceneGui()
        {
            if (_sceneGuiRegistered) return;
            SceneView.duringSceneGui += DoSceneGUI;
            _sceneGuiRegistered = true;
        }

        private void UnregisterSceneGui()
        {
            if (!_sceneGuiRegistered) return;
            SceneView.duringSceneGui -= DoSceneGUI;
            _sceneGuiRegistered = false;
        }

        private void DoSceneGUI(SceneView sceneView)
        {
            if (_lattice == null || !_lattice.drawLattice) return;

            SyncPreviewVisibility(Event.current);

            var grid = _lattice.ControlPointGrid;
            int total = _lattice.ControlPointCount;
            if (total == 0) return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            // 1. Draw wireframe edges
            DrawWireframe(grid);

            // 2. Draw handles at each control point
            DrawHandles(grid, total);

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        }

        private void SyncPreviewVisibility(Event currentEvent)
        {
            if (_lattice == null)
                return;

            bool ctrlHeld = currentEvent != null && currentEvent.control;
            if (ctrlHeld == _previewHiddenByCtrl)
                return;

            _previewHiddenByCtrl = ctrlHeld;
            _lattice.SetPreviewVisible(!ctrlHeld);
            SceneView.RepaintAll();
        }

        private void DrawWireframe(Vector3Int grid)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.35f);

            // X-direction edges
            for (int iz = 0; iz < grid.z; iz++)
            for (int iy = 0; iy < grid.y; iy++)
            for (int ix = 0; ix < grid.x - 1; ix++)
            {
                Vector3 a = LatticeToWorld(_lattice.GetControlPoint(_lattice.FlatIndex(ix, iy, iz)));
                Vector3 b = LatticeToWorld(_lattice.GetControlPoint(_lattice.FlatIndex(ix + 1, iy, iz)));
                Handles.DrawLine(a, b);
            }

            // Y-direction edges
            for (int iz = 0; iz < grid.z; iz++)
            for (int ix = 0; ix < grid.x; ix++)
            for (int iy = 0; iy < grid.y - 1; iy++)
            {
                Vector3 a = LatticeToWorld(_lattice.GetControlPoint(_lattice.FlatIndex(ix, iy, iz)));
                Vector3 b = LatticeToWorld(_lattice.GetControlPoint(_lattice.FlatIndex(ix, iy + 1, iz)));
                Handles.DrawLine(a, b);
            }

            // Z-direction edges
            for (int iy = 0; iy < grid.y; iy++)
            for (int ix = 0; ix < grid.x; ix++)
            for (int iz = 0; iz < grid.z - 1; iz++)
            {
                Vector3 a = LatticeToWorld(_lattice.GetControlPoint(_lattice.FlatIndex(ix, iy, iz)));
                Vector3 b = LatticeToWorld(_lattice.GetControlPoint(_lattice.FlatIndex(ix, iy, iz + 1)));
                Handles.DrawLine(a, b);
            }
        }

        private void DrawHandles(Vector3Int grid, int total)
        {
            float r = _lattice.handleRadius;
            bool shiftHeld = (Event.current != null && Event.current.shift); 

            for (int i = 0; i < total; i++)
            {
                Vector3 worldPos = LatticeToWorld(_lattice.GetControlPoint(i));
                bool isSelected = _lattice.selectedHandleIndices.Contains(i);

                // Sphere cap — clickable to select this handle
                Handles.color = isSelected ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0.3f, 0.7f, 1f, 0.9f);
                if (Handles.Button(worldPos, Quaternion.identity, r, r * 1.3f, Handles.SphereHandleCap))
                {
                    if (shiftHeld)
                    {
                        // Shift-click toggles this handle in the selection
                        if (isSelected) _lattice.selectedHandleIndices.Remove(i);
                        else _lattice.selectedHandleIndices.Add(i);
                    }
                    else
                    {
                        // Regular click replaces selection
                        _lattice.selectedHandleIndices.Clear();
                        _lattice.selectedHandleIndices.Add(i);
                    }
                    Repaint();
                    SceneView.RepaintAll();
                }
            }

            // Show a single position manipulator at the centroid of all selected handles
            if (_lattice.selectedHandleIndices.Count > 0)
            {
                Vector3 centroidWorld = Vector3.zero;
                for (int si = 0; si < _lattice.selectedHandleIndices.Count; si++)
                {
                    int idx = _lattice.selectedHandleIndices[si];
                    centroidWorld += LatticeToWorld(_lattice.GetControlPoint(idx));
                }
                centroidWorld /= _lattice.selectedHandleIndices.Count;

                EditorGUI.BeginChangeCheck();
                Vector3 newCentroidWorld = Handles.PositionHandle(centroidWorld, _lattice.transform.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 deltaWorld = newCentroidWorld - centroidWorld;
                    Undo.RecordObject(_lattice, shiftHeld ? "Move Lattice Cut" : "Move Lattice Control Points");

                    if (shiftHeld)
                    {
                        _lattice.MoveCuts(_lattice.selectedHandleIndices, WorldToLatticeVector(deltaWorld), deform: false);
                    }
                    else
                    {
                        for (int si = 0; si < _lattice.selectedHandleIndices.Count; si++)
                        {
                            int idx = _lattice.selectedHandleIndices[si];
                            Vector3 curWorld = LatticeToWorld(_lattice.GetControlPoint(idx));
                            _lattice.SetControlPoint(idx, WorldToLattice(curWorld + deltaWorld), deform: false);
                        }
                        _lattice.DeformTarget();
                    }
                    EditorUtility.SetDirty(_lattice);
                    Repaint();
                    SceneView.RepaintAll();
                }
            }
        }

        private Vector3 LatticeToWorld(Vector3 localPos)
        {
            return _lattice.transform.TransformPoint(localPos);
        }

        private Vector3 WorldToLattice(Vector3 worldPos)
        {
            return _lattice.transform.InverseTransformPoint(worldPos);
        }

        private Vector3 WorldToLatticeVector(Vector3 worldVector)
        {
            return _lattice.transform.InverseTransformVector(worldVector);
        }
    }
}
