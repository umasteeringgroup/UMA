#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UMA.EditorTools
{
    [CustomEditor(typeof(NewUMAGUI))]
    public class NewUMAGuiEditor : Editor
    {
        // ---- Foldout states (collapsed by default) ----
        private static bool _umaFoldout;
        private static bool _guiPrefabsFoldout;
        private static bool _cameraAnimFoldout;
        private static bool _testFoldout;
        private static bool _colorTablesFoldout;
        private static bool _dnaFoldout;
        private static bool _itemsFoldout;
        private static bool _containersFoldout;
        private static bool _buttonsFoldout;
        private static bool _miscFoldout;
        private static bool _timingFoldout;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeStatics()
        {
            _umaFoldout = false;
            _guiPrefabsFoldout = false;
            _cameraAnimFoldout = false;
            _testFoldout = false;
            _colorTablesFoldout = false;
            _dnaFoldout = false;
            _itemsFoldout = false;
            _containersFoldout = false;
            _buttonsFoldout = false;
            _miscFoldout = false;
            _timingFoldout = false;
        }

        // ---- Reorderable list cache ----
        private readonly Dictionary<string, ReorderableList> _listCache = new Dictionary<string, ReorderableList>();

        // ---- Serialized properties ----
        // UMA
        private SerializedProperty _avatarProp;
        private SerializedProperty _showConsoleProp;
        // GUI Prefabs
        private SerializedProperty _colorSelectorProp;
        private SerializedProperty _dnaAdjusterProp;
        private SerializedProperty _colorLabelProp;
        private SerializedProperty _gridContainerProp;
        private SerializedProperty _itemProp;
        private SerializedProperty _itemContainerProp;
        private SerializedProperty _infoTextProp;
        private SerializedProperty _logLabelProp;
        // Camera Animation
        private SerializedProperty _facePosProp;
        private SerializedProperty _legsPosProp;
        private SerializedProperty _bodyPosProp;
        private SerializedProperty _faceBoneNameProp;
        private SerializedProperty _legsBoneNameProp;
        private SerializedProperty _faceBoneOffsetProp;
        private SerializedProperty _legsBoneOffsetProp;
        private SerializedProperty _lerpSpeedProp;
        private SerializedProperty _lerpCurveProp;
        // Test
        private SerializedProperty _labelsProp;
        // Color Tables
        private SerializedProperty _faceColorsProp;
        private SerializedProperty _hairColorsProp;
        private SerializedProperty _legsColorsProp;
        private SerializedProperty _bodyColorsProp;
        // DNA
        private SerializedProperty _faceDNAProp;
        private SerializedProperty _hairDNAProp;
        private SerializedProperty _legsDNAProp;
        private SerializedProperty _bodyDNAProp;
        // Items
        private SerializedProperty _faceItemsProp;
        private SerializedProperty _hairItemsProp;
        private SerializedProperty _legsItemsProp;
        private SerializedProperty _bodyItemsProp;
        // Containers
        private SerializedProperty _dnaContainerProp;
        private SerializedProperty _itemsContainerProp;
        private SerializedProperty _logDetailContainerProp;
        // Buttons
        private SerializedProperty _faceButtonProp;
        private SerializedProperty _legsButtonProp;
        private SerializedProperty _bodyButtonProp;
        private SerializedProperty _hairButtonProp;
        private SerializedProperty _backButtonProp;
        private SerializedProperty _clearImageProp;
        private SerializedProperty _poseImageProp;
        // Misc
        private SerializedProperty _animatorsProp;
        private SerializedProperty _currentAnimatorProp;
        // Timing
        private SerializedProperty _showTimingButtonsProp;

        private void OnEnable()
        {
            // UMA
            _avatarProp = serializedObject.FindProperty("avatar");
            _showConsoleProp = serializedObject.FindProperty("showConsole");
            // GUI Prefabs
            _colorSelectorProp = serializedObject.FindProperty("ColorSelector");
            _dnaAdjusterProp = serializedObject.FindProperty("DNAAdjuster");
            _colorLabelProp = serializedObject.FindProperty("ColorLabel");
            _gridContainerProp = serializedObject.FindProperty("GridContainer");
            _itemProp = serializedObject.FindProperty("Item");
            _itemContainerProp = serializedObject.FindProperty("ItemContainer");
            _infoTextProp = serializedObject.FindProperty("InfoText");
            _logLabelProp = serializedObject.FindProperty("LogLabel");
            // Camera Animation
            _facePosProp = serializedObject.FindProperty("FacePos");
            _legsPosProp = serializedObject.FindProperty("LegsPos");
            _bodyPosProp = serializedObject.FindProperty("BodyPos");
            _faceBoneNameProp = serializedObject.FindProperty("FaceBoneName");
            _legsBoneNameProp = serializedObject.FindProperty("LegsBoneName");
            _faceBoneOffsetProp = serializedObject.FindProperty("FaceBoneOffset");
            _legsBoneOffsetProp = serializedObject.FindProperty("LegsBoneOffset");
            _lerpSpeedProp = serializedObject.FindProperty("lerpSpeed");
            _lerpCurveProp = serializedObject.FindProperty("lerpCurve");
            // Test
            _labelsProp = serializedObject.FindProperty("Labels");
            // Color Tables
            _faceColorsProp = serializedObject.FindProperty("FaceColors");
            _hairColorsProp = serializedObject.FindProperty("HairColors");
            _legsColorsProp = serializedObject.FindProperty("LegsColors");
            _bodyColorsProp = serializedObject.FindProperty("BodyColors");
            // DNA
            _faceDNAProp = serializedObject.FindProperty("FaceDNA");
            _hairDNAProp = serializedObject.FindProperty("HairDNA");
            _legsDNAProp = serializedObject.FindProperty("LegsDNA");
            _bodyDNAProp = serializedObject.FindProperty("BodyDNA");
            // Items
            _faceItemsProp = serializedObject.FindProperty("FaceItems");
            _hairItemsProp = serializedObject.FindProperty("HairItems");
            _legsItemsProp = serializedObject.FindProperty("LegsItems");
            _bodyItemsProp = serializedObject.FindProperty("BodyItems");
            // Containers
            _dnaContainerProp = serializedObject.FindProperty("DNAContainer");
            _itemsContainerProp = serializedObject.FindProperty("ItemsContainer");
            _logDetailContainerProp = serializedObject.FindProperty("LogDetailContainer");
            // Buttons
            _faceButtonProp = serializedObject.FindProperty("FaceButton");
            _legsButtonProp = serializedObject.FindProperty("LegsButton");
            _bodyButtonProp = serializedObject.FindProperty("BodyButton");
            _hairButtonProp = serializedObject.FindProperty("HairButton");
            _backButtonProp = serializedObject.FindProperty("BackButton");
            _clearImageProp = serializedObject.FindProperty("clearImage");
            _poseImageProp = serializedObject.FindProperty("PoseImage");
            // Misc
            _animatorsProp = serializedObject.FindProperty("Animators");
            _currentAnimatorProp = serializedObject.FindProperty("currentAnimator");
            // Timing
            _showTimingButtonsProp = serializedObject.FindProperty("showTimingButtons");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // --- Script field (read-only) ---
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"), true);
            }

            // --- UMA ---
            if (DrawFoldoutHeader("UMA", ref _umaFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_avatarProp);
                EditorGUILayout.PropertyField(_showConsoleProp);
                EditorGUI.indentLevel--;
            }

            // --- GUI Prefabs ---
            if (DrawFoldoutHeader("GUI Prefabs", ref _guiPrefabsFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_colorSelectorProp);
                EditorGUILayout.PropertyField(_dnaAdjusterProp);
                EditorGUILayout.PropertyField(_colorLabelProp);
                EditorGUILayout.PropertyField(_gridContainerProp);
                EditorGUILayout.PropertyField(_itemProp);
                EditorGUILayout.PropertyField(_itemContainerProp);
                EditorGUILayout.PropertyField(_infoTextProp);
                EditorGUILayout.PropertyField(_logLabelProp);
                EditorGUI.indentLevel--;
            }

            // --- Camera Animation ---
            if (DrawFoldoutHeader("Camera Animation", ref _cameraAnimFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_facePosProp);
                EditorGUILayout.PropertyField(_legsPosProp);
                EditorGUILayout.PropertyField(_bodyPosProp);
                EditorGUILayout.PropertyField(_faceBoneNameProp);
                EditorGUILayout.PropertyField(_legsBoneNameProp);
                EditorGUILayout.PropertyField(_faceBoneOffsetProp);
                EditorGUILayout.PropertyField(_legsBoneOffsetProp);
                EditorGUILayout.PropertyField(_lerpSpeedProp);
                EditorGUILayout.PropertyField(_lerpCurveProp);
                EditorGUI.indentLevel--;
            }

            // --- Test ---
            if (DrawFoldoutHeader("Test", ref _testFoldout))
            {
                DrawListWithButtons(_labelsProp, "Labels", typeof(string));
            }

            // --- Color Tables ---
            if (DrawFoldoutHeader("Color Tables", ref _colorTablesFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_faceColorsProp, "Face Colors", typeof(SharedColorTable));
                DrawListWithButtons(_hairColorsProp, "Hair Colors", typeof(SharedColorTable));
                DrawListWithButtons(_legsColorsProp, "Legs Colors", typeof(SharedColorTable));
                DrawListWithButtons(_bodyColorsProp, "Body Colors", typeof(SharedColorTable));
                EditorGUI.indentLevel--;
            }

            // --- DNA ---
            if (DrawFoldoutHeader("DNA", ref _dnaFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_faceDNAProp, "Face DNA", typeof(string));
                DrawListWithButtons(_hairDNAProp, "Hair DNA", typeof(string));
                DrawListWithButtons(_legsDNAProp, "Legs DNA", typeof(string));
                DrawListWithButtons(_bodyDNAProp, "Body DNA", typeof(string));
                EditorGUI.indentLevel--;
            }

            // --- Items ---
            if (DrawFoldoutHeader("Items", ref _itemsFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_faceItemsProp, "Face Items", typeof(UMAWardrobeRecipe));
                DrawListWithButtons(_hairItemsProp, "Hair Items", typeof(UMAWardrobeRecipe));
                DrawListWithButtons(_legsItemsProp, "Legs Items", typeof(UMAWardrobeRecipe));
                DrawListWithButtons(_bodyItemsProp, "Body Items", typeof(UMAWardrobeRecipe));
                EditorGUI.indentLevel--;
            }

            // --- Containers ---
            if (DrawFoldoutHeader("Containers", ref _containersFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_dnaContainerProp);
                EditorGUILayout.PropertyField(_itemsContainerProp);
                EditorGUILayout.PropertyField(_logDetailContainerProp);
                EditorGUI.indentLevel--;
            }

            // --- Buttons ---
            if (DrawFoldoutHeader("Buttons", ref _buttonsFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_faceButtonProp);
                EditorGUILayout.PropertyField(_legsButtonProp);
                EditorGUILayout.PropertyField(_bodyButtonProp);
                EditorGUILayout.PropertyField(_hairButtonProp);
                EditorGUILayout.PropertyField(_backButtonProp);
                EditorGUILayout.PropertyField(_clearImageProp);
                EditorGUILayout.PropertyField(_poseImageProp);
                EditorGUI.indentLevel--;
            }

            // --- Misc ---
            if (DrawFoldoutHeader("Misc", ref _miscFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_animatorsProp, "Animators", typeof(RuntimeAnimatorController));
                EditorGUILayout.PropertyField(_currentAnimatorProp);
                EditorGUI.indentLevel--;
            }

            // --- Timing ---
            if (DrawFoldoutHeader("Timing", ref _timingFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_showTimingButtonsProp, new GUIContent("Show Timing Buttons"));
                if (_showTimingButtonsProp.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "When enabled, three IMGUI buttons appear in the Game View " +
                        "allowing you to time builds with each mesh combiner (10 iterations each). " +
                        "Designed for testing in game builds.",
                        MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        #region Foldout header

        private static bool DrawFoldoutHeader(string title, ref bool foldout)
        {
            EditorGUILayout.Space(2);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            return foldout;
        }

        #endregion

        #region List drawing

        private void DrawListWithButtons(SerializedProperty listProp, string label, Type elementType)
        {
            if (listProp == null) return;

            ReorderableList list = GetOrCreateList(listProp, label);
            list.DoLayoutList();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Sort", GUILayout.Width(60)))
            {
                SortList(listProp, elementType, label);
            }

            if (GUILayout.Button("Remove Duplicates", GUILayout.Width(130)))
            {
                RemoveDuplicates(listProp, elementType, label);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private ReorderableList GetOrCreateList(SerializedProperty listProp, string label)
        {
            string key = listProp.propertyPath;
            if (_listCache.TryGetValue(key, out ReorderableList existing))
            {
                existing.serializedProperty = listProp;
                return existing;
            }

            var rl = new ReorderableList(serializedObject, listProp, true, true, true, true);
            rl.drawHeaderCallback = rect => EditorGUI.LabelField(rect, label);
            rl.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var prop = listProp.GetArrayElementAtIndex(index);
                if (prop == null) return;
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, prop, GUIContent.none);
            };
            rl.elementHeight = EditorGUIUtility.singleLineHeight + 2;
            _listCache[key] = rl;
            return rl;
        }

        #endregion

        #region Sort

        private void SortList(SerializedProperty listProp, Type elementType, string label)
        {
            int count = listProp.arraySize;
            if (count <= 1) return;

            if (elementType == typeof(string))
            {
                SortStringList(listProp, label);
            }
            else
            {
                SortObjectList(listProp, label);
            }
        }

        private void SortStringList(SerializedProperty listProp, string label)
        {
            int count = listProp.arraySize;
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = listProp.GetArrayElementAtIndex(i).stringValue ?? "";

            Array.Sort(values, StringComparer.OrdinalIgnoreCase);

            Undo.RecordObject(target, "Sort " + label);
            for (int i = 0; i < count; i++)
                listProp.GetArrayElementAtIndex(i).stringValue = values[i];

            ApplyAndRefresh();
        }

        private void SortObjectList(SerializedProperty listProp, string label)
        {
            int count = listProp.arraySize;
            var objects = new UnityEngine.Object[count];
            for (int i = 0; i < count; i++)
                objects[i] = listProp.GetArrayElementAtIndex(i).objectReferenceValue;

            Array.Sort(objects, (a, b) =>
            {
                string na = a != null ? a.name : "";
                string nb = b != null ? b.name : "";
                return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
            });

            Undo.RecordObject(target, "Sort " + label);
            listProp.arraySize = count;
            for (int i = 0; i < count; i++)
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];

            ApplyAndRefresh();
        }

        #endregion

        #region Remove Duplicates

        private void RemoveDuplicates(SerializedProperty listProp, Type elementType, string label)
        {
            if (listProp.arraySize <= 1) return;

            if (elementType == typeof(string))
            {
                RemoveStringDuplicates(listProp, label);
            }
            else
            {
                RemoveObjectDuplicates(listProp, label);
            }
        }

        private void RemoveStringDuplicates(SerializedProperty listProp, string label)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keep = new List<string>();

            for (int i = 0; i < listProp.arraySize; i++)
            {
                string val = listProp.GetArrayElementAtIndex(i).stringValue ?? "";
                if (seen.Add(val))
                    keep.Add(val);
            }

            if (keep.Count == listProp.arraySize) return;

            Undo.RecordObject(target, "Remove duplicates " + label);
            listProp.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++)
                listProp.GetArrayElementAtIndex(i).stringValue = keep[i];

            ApplyAndRefresh();
        }

        private void RemoveObjectDuplicates(SerializedProperty listProp, string label)
        {
            var seen = new HashSet<string>();
            var keep = new List<UnityEngine.Object>();

            for (int i = 0; i < listProp.arraySize; i++)
            {
                var obj = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                string key = GetDedupeKey(obj);
                if (seen.Add(key))
                    keep.Add(obj);
            }

            if (keep.Count == listProp.arraySize) return;

            Undo.RecordObject(target, "Remove duplicates " + label);
            listProp.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++)
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];

            ApplyAndRefresh();
        }

        private static string GetDedupeKey(UnityEngine.Object obj)
        {
            if (obj == null) return "__null__";
            string path = AssetDatabase.GetAssetPath(obj);
            return string.IsNullOrEmpty(path) ? obj.GetInstanceID().ToString() : path;
        }

        #endregion

        #region Utility

        private void ApplyAndRefresh()
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            GUI.changed = true;
            Repaint();
        }

        #endregion
    }
}
#endif