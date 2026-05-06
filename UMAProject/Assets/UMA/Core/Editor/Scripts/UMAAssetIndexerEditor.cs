using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    [CustomEditor(typeof(UMAAssetIndexer))]
    public class UMAAssetIndexerEditor : Editor
    {
        private const string PrefPrefix = "UMA.UMAAssetIndexerEditor.";
        private const string AllTypesLabel = "All Types";

        private static readonly Dictionary<string, bool> TopLevelFoldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> SerializedItemFoldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);

        private readonly List<FieldBinding> _scalarFields = new List<FieldBinding>();
        private readonly List<FieldBinding> _arrayFields = new List<FieldBinding>();

        private Vector2 _serializedItemsScroll;
        private string _serializedItemsTypeFilter = string.Empty;
        private string _serializedItemsNameFilter = string.Empty;

        private sealed class FieldBinding
        {
            public string Name;
            public SerializedProperty Property;
            public GUIContent Label;
        }

        private void OnEnable()
        {
            RebuildFieldBindings();
            LoadFilterState();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            RebuildFieldBindings();

            DrawScalarFields();

            if (_scalarFields.Count > 0 && _arrayFields.Count > 0)
            {
                EditorGUILayout.Space();
            }

            DrawArrayFields();

            serializedObject.ApplyModifiedProperties();
        }

        private void RebuildFieldBindings()
        {
            _scalarFields.Clear();
            _arrayFields.Clear();

            FieldInfo[] fields = typeof(UMAAssetIndexer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                SerializedProperty property = serializedObject.FindProperty(field.Name);
                if (property == null)
                {
                    continue;
                }

                FieldBinding binding = new FieldBinding
                {
                    Name = field.Name,
                    Property = property,
                    Label = new GUIContent(ObjectNames.NicifyVariableName(field.Name))
                };

                if (IsArrayProperty(property))
                {
                    _arrayFields.Add(binding);
                }
                else
                {
                    _scalarFields.Add(binding);
                }
            }
        }

        private static bool IsArrayProperty(SerializedProperty property)
        {
            return property != null && property.isArray && property.propertyType != SerializedPropertyType.String;
        }

        private void DrawScalarFields()
        {
            for (int i = 0; i < _scalarFields.Count; i++)
            {
                FieldBinding binding = _scalarFields[i];
                if (binding.Property == null)
                {
                    continue;
                }

                EditorGUILayout.PropertyField(binding.Property, binding.Label, true);
            }
        }

        private void DrawArrayFields()
        {
            for (int i = 0; i < _arrayFields.Count; i++)
            {
                FieldBinding binding = _arrayFields[i];
                SerializedProperty property = binding.Property;
                if (property == null)
                {
                    continue;
                }

                string foldoutKey = "top." + property.propertyPath;
                bool expanded = GetTopLevelFoldoutState(foldoutKey);
                string title = binding.Label.text + " (" + property.arraySize + ")";
                bool newExpanded = EditorGUILayout.Foldout(expanded, title, true);
                if (newExpanded != expanded)
                {
                    SetTopLevelFoldoutState(foldoutKey, newExpanded);
                }

                if (!newExpanded)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (binding.Name == nameof(UMAAssetIndexer.SerializedItems))
                {
                    DrawSerializedItemsSection(property);
                }
                else
                {
                    DrawStandardArraySection(property);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }
        }

        private void DrawStandardArraySection(SerializedProperty property)
        {
            EditorGUI.BeginChangeCheck();
            int newSize = EditorGUILayout.IntField("Size", property.arraySize);
            if (EditorGUI.EndChangeCheck())
            {
                property.arraySize = Mathf.Max(0, newSize);
            }

            int removeIndex = -1;
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Element " + i, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(element, true);
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
            {
                property.DeleteArrayElementAtIndex(removeIndex);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add Element", GUILayout.Width(110f)))
            {
                property.arraySize++;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSerializedItemsSection(SerializedProperty property)
        {
            List<string> typeOptions = BuildSerializedItemTypeOptions(property);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            int selectedTypeIndex = Mathf.Max(0, typeOptions.IndexOf(string.IsNullOrEmpty(_serializedItemsTypeFilter) ? AllTypesLabel : _serializedItemsTypeFilter));
            selectedTypeIndex = EditorGUILayout.Popup("Type", selectedTypeIndex, typeOptions.ToArray());
            string selectedType = typeOptions[selectedTypeIndex];
            _serializedItemsTypeFilter = selectedType == AllTypesLabel ? string.Empty : selectedType;
            _serializedItemsNameFilter = EditorGUILayout.TextField("Name", _serializedItemsNameFilter ?? string.Empty);
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                _serializedItemsTypeFilter = string.Empty;
                _serializedItemsNameFilter = string.Empty;
                SaveFilterState();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                SaveFilterState();
            }

            int visibleCount = 0;
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                if (SerializedItemMatchesFilter(element))
                {
                    visibleCount++;
                }
            }

            EditorGUILayout.LabelField("Visible Items", visibleCount + " / " + property.arraySize);

            if (property.arraySize == 0)
            {
                EditorGUILayout.HelpBox("SerializedItems is empty.", MessageType.Info);
                return;
            }

            if (visibleCount == 0)
            {
                EditorGUILayout.HelpBox("No SerializedItems match the current filter.", MessageType.Info);
                return;
            }

            _serializedItemsScroll = EditorGUILayout.BeginScrollView(_serializedItemsScroll, GUILayout.MinHeight(180f), GUILayout.MaxHeight(480f));
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                if (!SerializedItemMatchesFilter(element))
                {
                    continue;
                }

                string baseTypeName = GetSerializedItemChildString(element, "_BaseTypeName");
                string assetName = GetSerializedItemChildString(element, "_Name");
                string itemTitle = string.IsNullOrEmpty(baseTypeName)
                    ? (string.IsNullOrEmpty(assetName) ? "Element " + i : assetName)
                    : baseTypeName + ": " + (string.IsNullOrEmpty(assetName) ? ("Element " + i) : assetName);

                string itemFoldoutKey = "item." + property.propertyPath + "." + i + "." + baseTypeName + "." + assetName;
                bool expanded = GetSerializedItemFoldoutState(itemFoldoutKey);
                bool newExpanded = EditorGUILayout.Foldout(expanded, itemTitle, true);
                if (newExpanded != expanded)
                {
                    SetSerializedItemFoldoutState(itemFoldoutKey, newExpanded);
                }

                if (!newExpanded)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawDirectChildren(element);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private static List<string> BuildSerializedItemTypeOptions(SerializedProperty property)
        {
            List<string> options = new List<string>();
            options.Add(AllTypesLabel);

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                string baseTypeName = GetSerializedItemChildString(element, "_BaseTypeName");
                if (string.IsNullOrEmpty(baseTypeName) || !seen.Add(baseTypeName))
                {
                    continue;
                }

                options.Add(baseTypeName);
            }

            if (options.Count > 1)
            {
                options.Sort(1, options.Count - 1, StringComparer.OrdinalIgnoreCase);
            }
            return options;
        }

        private bool SerializedItemMatchesFilter(SerializedProperty element)
        {
            string baseTypeName = GetSerializedItemChildString(element, "_BaseTypeName");
            string assetName = GetSerializedItemChildString(element, "_Name");

            if (!string.IsNullOrEmpty(_serializedItemsTypeFilter)
                && !string.Equals(baseTypeName, _serializedItemsTypeFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_serializedItemsNameFilter)
                && (string.IsNullOrEmpty(assetName)
                    || assetName.IndexOf(_serializedItemsNameFilter, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            return true;
        }

        private static string GetSerializedItemChildString(SerializedProperty element, string childName)
        {
            if (element == null)
            {
                return string.Empty;
            }

            SerializedProperty child = element.FindPropertyRelative(childName);
            return child != null ? child.stringValue : string.Empty;
        }

        private static void DrawDirectChildren(SerializedProperty property)
        {
            SerializedProperty child = property.Copy();
            SerializedProperty end = child.GetEndProperty();
            int parentDepth = child.depth;
            bool enterChildren = true;

            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                if (child.depth == parentDepth + 1)
                {
                    EditorGUILayout.PropertyField(child, true);
                }
                enterChildren = false;
            }
        }

        private void LoadFilterState()
        {
            string keyRoot = GetTargetKeyRoot();
            _serializedItemsTypeFilter = EditorPrefs.GetString(PrefPrefix + keyRoot + ".SerializedItems.TypeFilter", string.Empty);
            _serializedItemsNameFilter = EditorPrefs.GetString(PrefPrefix + keyRoot + ".SerializedItems.NameFilter", string.Empty);
        }

        private void SaveFilterState()
        {
            string keyRoot = GetTargetKeyRoot();
            EditorPrefs.SetString(PrefPrefix + keyRoot + ".SerializedItems.TypeFilter", _serializedItemsTypeFilter ?? string.Empty);
            EditorPrefs.SetString(PrefPrefix + keyRoot + ".SerializedItems.NameFilter", _serializedItemsNameFilter ?? string.Empty);
        }

        private bool GetTopLevelFoldoutState(string key)
        {
            return GetFoldoutState(TopLevelFoldoutStates, key);
        }

        private void SetTopLevelFoldoutState(string key, bool value)
        {
            SetFoldoutState(TopLevelFoldoutStates, key, value);
        }

        private bool GetSerializedItemFoldoutState(string key)
        {
            return GetFoldoutState(SerializedItemFoldoutStates, key);
        }

        private void SetSerializedItemFoldoutState(string key, bool value)
        {
            SetFoldoutState(SerializedItemFoldoutStates, key, value);
        }

        private bool GetFoldoutState(Dictionary<string, bool> cache, string key)
        {
            if (cache.TryGetValue(key, out bool cachedValue))
            {
                return cachedValue;
            }

            bool value = EditorPrefs.GetBool(PrefPrefix + GetTargetKeyRoot() + "." + key, false);
            cache[key] = value;
            return value;
        }

        private void SetFoldoutState(Dictionary<string, bool> cache, string key, bool value)
        {
            cache[key] = value;
            EditorPrefs.SetBool(PrefPrefix + GetTargetKeyRoot() + "." + key, value);
        }

        private string GetTargetKeyRoot()
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return assetPath.Replace('\\', '/');
            }

            return target != null ? target.GetEntityId().ToString() : "UMAAssetIndexer";
        }
    }
}