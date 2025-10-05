#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UMA;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UMA.EditorTools
{
    [CustomEditor(typeof(DecalRTStampSlot))]
    public class DecalRTStampSlotEditor : Editor
    {
        private SerializedProperty _overlayStampsProp;
        private SerializedProperty _enableDebugProp;

        private List<string> _overlayNames = new List<string>();
        private List<OverlayDataAsset> _overlayAssets = new List<OverlayDataAsset>();

        // Race lookup for base-recipe import
        private List<string> _raceNames = new List<string>();
        private List<RaceData> _raceAssets = new List<RaceData>();
        private int _selectedRaceIndex = -1;

        // one reorderable list per overlay set path
        private readonly Dictionary<string, ReorderableList> _stampLists = new Dictionary<string, ReorderableList>();
        // foldout state per element
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        private GUIContent upArrow = new GUIContent("▲", "Move Up");
        private GUIContent downArrow = new GUIContent("▼", "Move Down");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_overlayStampsProp == null)
            {
                _overlayStampsProp = serializedObject.FindProperty("overlayStamps");
                _enableDebugProp = serializedObject.FindProperty("enableDebug");
                RefreshOverlayCatalog();
                RefreshRaceCatalog();
            }

            DrawHeaderToolbar();

            // EditorGUILayout.PropertyField(_enableDebugProp, new GUIContent("Enable Debug"));
            // EditorGUILayout.Space(6);
            DrawOverlaySets();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeaderToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Add Overlay Set", EditorStyles.toolbarButton))
                {
                    Undo.RecordObject(target, "Add Overlay Set");
                    _overlayStampsProp.arraySize++;
                    var elem = _overlayStampsProp.GetArrayElementAtIndex(_overlayStampsProp.arraySize - 1);
                    InitializeBlankSet(elem); // ensure new set starts empty (Unity duplicates last element by default)
                    EnsureSetArrays(elem);
                    ApplyAndRepaint(clearCaches: true);
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh Overlays", EditorStyles.toolbarButton))
                {
                    RefreshOverlayCatalog();
                }
                if (GUILayout.Button("Refresh Races", EditorStyles.toolbarButton))
                {
                    RefreshRaceCatalog();
                }
            }
        }

        private void DrawOverlaySets()
        {
            if (_overlayStampsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No overlay sets configured. Click 'Add Overlay Set' to create one.", MessageType.Info);
                return;
            }

            for (int i = 0; i < _overlayStampsProp.arraySize; i++)
            {
                var setProp = _overlayStampsProp.GetArrayElementAtIndex(i);
                EnsureSetArrays(setProp);

                var nameProp = setProp.FindPropertyRelative("name");
                var overlaysProp = setProp.FindPropertyRelative("overlays");
                var overlayNamesProp = setProp.FindPropertyRelative("overlayNames");
                var stampsProp = setProp.FindPropertyRelative("stamps");

                // Header prefers the user-editable name; falls back to overlay asset names or default
                string header = GetOverlaySetHeader(nameProp, overlaysProp, i);

                var foldoutKey = setProp.propertyPath;
                bool expanded = GetFoldout(foldoutKey);

                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        expanded = EditorGUILayout.Foldout(expanded, header, true);
                        SetFoldout(foldoutKey, expanded);

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button(upArrow, GUILayout.Width(22)))
                        {
                            if (i > 0)
                            {
                                _overlayStampsProp.MoveArrayElement(i, i - 1);
                                ApplyAndRepaint(clearCaches: true);
                                GUIUtility.ExitGUI();
                            }
                        }
                        if (GUILayout.Button(downArrow, GUILayout.Width(22)))
                        {
                            if (i < _overlayStampsProp.arraySize - 1)
                            {
                                _overlayStampsProp.MoveArrayElement(i, i + 1);
                                ApplyAndRepaint(clearCaches: true);
                                GUIUtility.ExitGUI();
                            }
                        }
                        if (GUILayout.Button("X", GUILayout.Width(22)))
                        {
                            Undo.RecordObject(target, "Remove Overlay Set");
                            _overlayStampsProp.DeleteArrayElementAtIndex(i);
                            ApplyAndRepaint(clearCaches: true);
                            GUIUtility.ExitGUI();
                        }
                    }

                    if (!expanded) continue;

                    EditorGUI.indentLevel++;

                    // Editable name
                    EditorGUILayout.PropertyField(nameProp, new GUIContent("Name"));

                    // Overlays UI
                    EditorGUILayout.LabelField("Overlays", EditorStyles.boldLabel);
                    DrawOverlayPicker(overlaysProp, overlayNamesProp);

                    EditorGUILayout.Space(4);

                    // Stamps UI
                    EditorGUILayout.LabelField("Stamps", EditorStyles.boldLabel);

                    var list = GetOrCreateStampsList(setProp.propertyPath, stampsProp);
                    list.DoLayoutList();

                    DrawStampDropArea(stampsProp);

                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawOverlayPicker(SerializedProperty overlaysProp, SerializedProperty overlayNamesProp)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_overlayAssets.Count == 0);
                int choice = EditorGUILayout.Popup(new GUIContent("Add Overlay"), -1, _overlayNames.ToArray());
                EditorGUI.EndDisabledGroup();

                if (choice >= 0 && choice < _overlayAssets.Count)
                {
                    Undo.RecordObject(target, "Add Overlay");
                    overlaysProp.arraySize++;
                    overlaysProp.GetArrayElementAtIndex(overlaysProp.arraySize - 1).objectReferenceValue = _overlayAssets[choice];
                    ApplyAndRepaint();
                }

                if (GUILayout.Button("Add By Name", GUILayout.Width(110)))
                {
                    Undo.RecordObject(target, "Add Overlay Name");
                    overlayNamesProp.arraySize++;
                    overlayNamesProp.GetArrayElementAtIndex(overlayNamesProp.arraySize - 1).stringValue = "";
                    ApplyAndRepaint();
                }
            }

            // From Race base recipe
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_raceAssets.Count == 0);
                _selectedRaceIndex = EditorGUILayout.Popup(new GUIContent("Race"), _selectedRaceIndex, _raceNames.ToArray());
                bool canAdd = (_selectedRaceIndex >= 0 && _selectedRaceIndex < _raceAssets.Count);
                using (new EditorGUI.DisabledScope(!canAdd))
                {
                    if (GUILayout.Button("Add Base Overlays", GUILayout.Width(150)))
                    {
                        var race = _raceAssets[_selectedRaceIndex];
                        AddBaseRaceOverlaysToSet(race, overlaysProp);
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            // Current overlay assets
            EditorGUILayout.HelpBox("Important: only add the last overlay in the overlay stack that you want the decal to affect. Adding overlays in the same stack will cause double stamping.", MessageType.Warning);
            int removeIdx = -1;
            for (int i = 0; i < overlaysProp.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(overlaysProp.GetArrayElementAtIndex(i), GUIContent.none);
                    if (GUILayout.Button("-", GUILayout.Width(22))) { removeIdx = i; }
                }
            }
            if (removeIdx >= 0)
            {
                Undo.RecordObject(target, "Remove Overlay");
                overlaysProp.DeleteArrayElementAtIndex(removeIdx);
                ApplyAndRepaint();
            }

            // Current overlay names
            removeIdx = -1;
            for (int i = 0; i < overlayNamesProp.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var sp = overlayNamesProp.GetArrayElementAtIndex(i);
                    sp.stringValue = EditorGUILayout.TextField(sp.stringValue);
                    if (GUILayout.Button("-", GUILayout.Width(22))) { removeIdx = i; }
                }
            }
            if (removeIdx >= 0)
            {
                Undo.RecordObject(target, "Remove Overlay Name");
                overlayNamesProp.DeleteArrayElementAtIndex(removeIdx);
                ApplyAndRepaint();
            }

            // Drag & drop overlays area
            var drop = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drag & Drop Overlays Here", EditorStyles.helpBox);
            HandleDragDrop(drop, (obj) =>
            {
                var oda = obj as OverlayDataAsset;
                if (oda == null) return false;
                Undo.RecordObject(target, "Add Overlay");
                overlaysProp.arraySize++;
                overlaysProp.GetArrayElementAtIndex(overlaysProp.arraySize - 1).objectReferenceValue = oda;
                ApplyAndRepaint();
                return true;
            });
        }

        private ReorderableList GetOrCreateStampsList(string key, SerializedProperty stampsProp)
        {
            if (_stampLists.TryGetValue(key, out var list))
            {
                // ensure it points to current property
                list.serializedProperty = stampsProp;
                return list;
            }

            list = new ReorderableList(serializedObject, stampsProp, true, true, true, true);
            list.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "DecalRTStampAssets");
            };
            list.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                rect.height = EditorGUIUtility.singleLineHeight;
                rect.y += 2;
                var elem = stampsProp.GetArrayElementAtIndex(index);
                EditorGUI.PropertyField(rect, elem, GUIContent.none);
            };
            list.onAddCallback = (ReorderableList l) =>
            {
                Undo.RecordObject(target, "Add Stamp");
                stampsProp.arraySize++;
                stampsProp.GetArrayElementAtIndex(stampsProp.arraySize - 1).objectReferenceValue = null;
                ApplyAndRepaint();
            };
            list.onRemoveCallback = (ReorderableList l) =>
            {
                if (l.index >= 0 && l.index < stampsProp.arraySize)
                {
                    Undo.RecordObject(target, "Remove Stamp");
                    stampsProp.DeleteArrayElementAtIndex(l.index);
                    ApplyAndRepaint();
                }
            };

            _stampLists[key] = list;
            return list;
        }

        private void DrawStampDropArea(SerializedProperty stampsProp)
        {
            var drop = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drag & Drop DecalRTStampAsset Here", EditorStyles.helpBox);

            HandleDragDrop(drop, (obj) =>
            {
                var stamp = obj as DecalRTStampAsset;
                if (stamp == null) return false;
                Undo.RecordObject(target, "Add Stamp");
                stampsProp.arraySize++;
                stampsProp.GetArrayElementAtIndex(stampsProp.arraySize - 1).objectReferenceValue = stamp;
                ApplyAndRepaint();
                return true;
            });
        }

        private void HandleDragDrop(Rect area, Func<UnityEngine.Object, bool> accept)
        {
            var evt = Event.current;
            if (!area.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    bool any = false;
                    foreach (var o in DragAndDrop.objectReferences)
                    {
                        any |= accept(o);
                    }
                    if (any)
                    {
                        serializedObject.ApplyModifiedProperties();
                        GUI.changed = true;
                        Repaint();
                        EditorUtility.SetDirty(target);
                    }
                }
                evt.Use();
            }
        }

        private void EnsureSetArrays(SerializedProperty setProp)
        {
            if (setProp == null) return;
            var overlaysProp = setProp.FindPropertyRelative("overlays");
            var overlayNamesProp = setProp.FindPropertyRelative("overlayNames");
            var stampsProp = setProp.FindPropertyRelative("stamps");
            if (overlaysProp == null) setProp.serializedObject.Update();
            if (overlayNamesProp == null) setProp.serializedObject.Update();
            if (stampsProp == null) setProp.serializedObject.Update();
        }

        // Explicitly clear & init a newly added set so it doesn't clone the previous element's data
        private void InitializeBlankSet(SerializedProperty setProp)
        {
            if (setProp == null) return;
            var nameProp = setProp.FindPropertyRelative("name");
            if (nameProp != null) nameProp.stringValue = string.Empty;
            var overlaysProp = setProp.FindPropertyRelative("overlays");
            if (overlaysProp != null) overlaysProp.arraySize = 0;
            var overlayNamesProp = setProp.FindPropertyRelative("overlayNames");
            if (overlayNamesProp != null) overlayNamesProp.arraySize = 0;
            var stampsProp = setProp.FindPropertyRelative("stamps");
            if (stampsProp != null) stampsProp.arraySize = 0;
        }

        private bool GetFoldout(string key)
        {
            if (_foldouts.TryGetValue(key, out var v)) return v;
            return true;
        }

        private void SetFoldout(string key, bool v)
        {
            _foldouts[key] = v;
        }

        private void RefreshOverlayCatalog()
        {
            _overlayNames.Clear();
            _overlayAssets.Clear();

            // Prefer UMAAssetIndexer for stable ordering; fallback to AssetDatabase
            try
            {
                var idx = UMAAssetIndexer.Instance;
                if (idx != null)
                {
                    var all = idx.GetAllAssets<OverlayDataAsset>();
                    if (all != null)
                    {
                        foreach (var a in all)
                        {
                            if (a == null) continue;
                            _overlayAssets.Add(a);
                            _overlayNames.Add(string.IsNullOrEmpty(a.overlayName) ? a.name : a.overlayName);
                        }
                    }
                }
            }
            catch { }

#if UNITY_EDITOR
            if (_overlayAssets.Count == 0)
            {
                var guids = AssetDatabase.FindAssets("t:OverlayDataAsset");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var a = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(path);
                    if (a == null) continue;
                    _overlayAssets.Add(a);
                    _overlayNames.Add(string.IsNullOrEmpty(a.overlayName) ? a.name : a.overlayName);
                }
            }
#endif

            // Sort by name
            var zipped = _overlayAssets.Zip(_overlayNames, (a, n) => new { a, n }).OrderBy(z => z.n, StringComparer.Ordinal).ToList();
            _overlayAssets = zipped.Select(z => z.a).ToList();
            _overlayNames = zipped.Select(z => z.n).ToList();
        }

        private void RefreshRaceCatalog()
        {
            _raceNames.Clear();
            _raceAssets.Clear();

            try
            {
                var idx = UMAAssetIndexer.Instance;
                if (idx != null)
                {
                    var all = idx.GetAllAssets<RaceData>();
                    if (all != null)
                    {
                        foreach (var r in all)
                        {
                            if (r == null) continue;
                            _raceAssets.Add(r);
                            _raceNames.Add(string.IsNullOrEmpty(r.raceName) ? r.name : r.raceName);
                        }
                    }
                }
            }
            catch { }

#if UNITY_EDITOR
            if (_raceAssets.Count == 0)
            {
                var guids = AssetDatabase.FindAssets("t:RaceData");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var r = AssetDatabase.LoadAssetAtPath<RaceData>(path);
                    if (r == null) continue;
                    _raceAssets.Add(r);
                    _raceNames.Add(string.IsNullOrEmpty(r.raceName) ? r.name : r.raceName);
                }
            }
#endif
            // Sort by raceName (or name fallback)
            var zipped = _raceAssets.Zip(_raceNames, (r, n) => new { r, n }).OrderBy(z => z.n, StringComparer.Ordinal).ToList();
            _raceAssets = zipped.Select(z => z.r).ToList();
            _raceNames = zipped.Select(z => z.n).ToList();

            // keep selection valid
            if (_selectedRaceIndex >= _raceAssets.Count) _selectedRaceIndex = -1;
        }

        private void AddBaseRaceOverlaysToSet(RaceData race, SerializedProperty overlaysProp)
        {
            if (race == null)
            {
                EditorUtility.DisplayDialog("Add Base Overlays", "No race selected.", "OK");
                return;
            }
            if (race.baseRaceRecipe == null)
            {
                EditorUtility.DisplayDialog("Add Base Overlays", $"Race '{race.raceName}' has no baseRaceRecipe.", "OK");
                return;
            }

            UMAData.UMARecipe recipe = null;
            try
            {
                recipe = race.baseRaceRecipe.GetCachedRecipe(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to unpack base recipe for race '{race.raceName}': {ex.Message}", race);
                return;
            }
            if (recipe == null)
            {
                EditorUtility.DisplayDialog("Add Base Overlays", "Failed to get base recipe.", "OK");
                return;
            }

            // Build existing set for duplicate avoidance
            var existing = new HashSet<OverlayDataAsset>();
            for (int i = 0; i < overlaysProp.arraySize; i++)
            {
                var oda = overlaysProp.GetArrayElementAtIndex(i).objectReferenceValue as OverlayDataAsset;
                if (oda != null) existing.Add(oda);
            }

            var toAdd = new List<OverlayDataAsset>();
            var slots = recipe.GetAllSlots();
            for (int si = 0; si < slots.Length; si++)
            {
                var slot = slots[si];
                if (slot == null) continue;
                var overlayList = slot.GetOverlayList();
                for (int oi = 0; oi < overlayList.Count; oi++)
                {
                    var ov = overlayList[oi];
                    var asset = ov != null ? ov.asset : null;
                    if (asset == null) continue;
                    if (existing.Contains(asset)) continue;
                    existing.Add(asset);
                    toAdd.Add(asset);
                }
            }

            if (toAdd.Count == 0)
            {
                EditorUtility.DisplayDialog("Add Base Overlays", "No new overlays found to add.", "OK");
                return;
            }

            Undo.RecordObject(target, "Add Base Race Overlays");
            int startSize = overlaysProp.arraySize;
            overlaysProp.arraySize += toAdd.Count;
            for (int i = 0; i < toAdd.Count; i++)
            {
                overlaysProp.GetArrayElementAtIndex(startSize + i).objectReferenceValue = toAdd[i];
            }
            ApplyAndRepaint();

            EditorUtility.DisplayDialog("Add Base Overlays", $"Added {toAdd.Count} overlay(s) from race '{race.raceName}'.", "OK");
        }

        private void ApplyAndRepaint(bool clearCaches = false)
        {
            serializedObject.ApplyModifiedProperties();
            if (clearCaches)
            {
                // property paths change after reorder/delete, avoid stale lists
                _stampLists.Clear();
            }
            EditorUtility.SetDirty(target);
            Repaint();
        }

        // Build header preferring the user-specified set name, else fall back to overlay asset names
        private string GetOverlaySetHeader(SerializedProperty nameProp, SerializedProperty overlaysProp, int index)
        {
            var customName = nameProp != null ? nameProp.stringValue : null;
            if (!string.IsNullOrWhiteSpace(customName))
                return customName.Trim();

            return BuildHeaderFromAssetNames(overlaysProp, index);
        }

        // Build header using OverlayDataAsset.name as a fallback
        private string BuildHeaderFromAssetNames(SerializedProperty overlaysProp, int index)
        {
            var names = new List<string>();
            if (overlaysProp != null)
            {
                for (int i = 0; i < overlaysProp.arraySize; i++)
                {
                    var oda = overlaysProp.GetArrayElementAtIndex(i).objectReferenceValue as OverlayDataAsset;
                    if (oda != null && !string.IsNullOrEmpty(oda.name))
                    {
                        names.Add(oda.name);
                    }
                }
            }

            if (names.Count == 0)
                return $"Overlay Set {index + 1}";

            return names.Count > 3
                ? string.Join(", ", names.Take(3)) + "…"
                : string.Join(", ", names);
        }
    }
}
#endif
