#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UMA.EditorTools
{
    [CustomEditor(typeof(NewUMAGUI))]
    public class NewUMAGuiEditor : Editor
    {
        private const string FaceItemsPropertyName = "FaceItems";
        private const string HairItemsPropertyName = "HairItems";
        private const string LegsItemsPropertyName = "LegsItems";
        private const string BodyItemsPropertyName = "BodyItems";

        private static readonly string[] ItemPropertyNames =
        {
            FaceItemsPropertyName,
            HairItemsPropertyName,
            LegsItemsPropertyName,
            BodyItemsPropertyName,
        };

        private readonly Dictionary<string, ReorderableList> _itemLists = new Dictionary<string, ReorderableList>();

        private SerializedProperty _faceItemsProp;
        private SerializedProperty _hairItemsProp;
        private SerializedProperty _legsItemsProp;
        private SerializedProperty _bodyItemsProp;

        private void OnEnable()
        {
            _faceItemsProp = serializedObject.FindProperty(FaceItemsPropertyName);
            _hairItemsProp = serializedObject.FindProperty(HairItemsPropertyName);
            _legsItemsProp = serializedObject.FindProperty(LegsItemsPropertyName);
            _bodyItemsProp = serializedObject.FindProperty(BodyItemsPropertyName);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawInspectorWithCustomItems();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInspectorWithCustomItems()
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            bool itemsDrawn = false;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(iterator, true);
                    }

                    continue;
                }

                if (iterator.propertyPath == FaceItemsPropertyName)
                {
                    DrawItemsSection();
                    itemsDrawn = true;
                    continue;
                }

                if (IsItemProperty(iterator.propertyPath))
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            if (!itemsDrawn)
            {
                DrawItemsSection();
            }
        }

        private void DrawItemsSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Items", EditorStyles.boldLabel);
            DrawItemListBlock(_faceItemsProp, "Face Items");
            DrawItemListBlock(_hairItemsProp, "Hair Items");
            DrawItemListBlock(_legsItemsProp, "Legs Items");
            DrawItemListBlock(_bodyItemsProp, "Body Items");
        }

        private void DrawItemListBlock(SerializedProperty listProperty, string label)
        {
            if (listProperty == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(listProperty.arraySize <= 1))
                    {
                        if (GUILayout.Button("Sort", GUILayout.Width(70f)))
                        {
                            SortItems(listProperty.propertyPath, "Sort " + label);
                        }
                    }

                    using (new EditorGUI.DisabledScope(listProperty.arraySize == 0))
                    {
                        if (GUILayout.Button("Remove Duplicates", GUILayout.Width(140f)))
                        {
                            RemoveDuplicateItems(listProperty.propertyPath, "Remove duplicate " + label);
                        }
                    }
                }

                GetOrCreateList(listProperty, label).DoLayoutList();
                DrawDropArea(listProperty.propertyPath, label);
            }
        }

        private ReorderableList GetOrCreateList(SerializedProperty listProperty, string label)
        {
            if (_itemLists.TryGetValue(listProperty.propertyPath, out ReorderableList existingList))
            {
                existingList.serializedProperty = listProperty;
                return existingList;
            }

            string propertyPath = listProperty.propertyPath;
            ReorderableList list = new ReorderableList(serializedObject, listProperty, true, true, true, true);
            list.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, label);
            };
            list.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                SerializedProperty property = serializedObject.FindProperty(propertyPath);
                if (property == null || index < 0 || index >= property.arraySize)
                {
                    return;
                }

                rect.y += 2f;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, property.GetArrayElementAtIndex(index), GUIContent.none);
            };
            list.onAddCallback = (ReorderableList currentList) =>
            {
                SerializedProperty property = serializedObject.FindProperty(propertyPath);
                if (property == null)
                {
                    return;
                }

                Undo.RecordObject(target, "Add " + label);
                property.arraySize++;
                property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = null;
                ApplyAndRefresh();
            };
            list.onRemoveCallback = (ReorderableList currentList) =>
            {
                SerializedProperty property = serializedObject.FindProperty(propertyPath);
                if (property == null || currentList.index < 0 || currentList.index >= property.arraySize)
                {
                    return;
                }

                Undo.RecordObject(target, "Remove " + label);
                property.DeleteArrayElementAtIndex(currentList.index);
                ApplyAndRefresh();
            };

            _itemLists[propertyPath] = list;
            return list;
        }

        private void DrawDropArea(string propertyPath, string label)
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag & Drop UMAWardrobeRecipe assets here", EditorStyles.helpBox);
            HandleDragDrop(dropArea, propertyPath, label);
        }

        private void HandleDragDrop(Rect area, string propertyPath, string label)
        {
            Event currentEvent = Event.current;
            if (!area.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
            {
                return;
            }

            bool hasWardrobeRecipe = false;
            foreach (UnityEngine.Object droppedObject in DragAndDrop.objectReferences)
            {
                if (droppedObject is UMAWardrobeRecipe)
                {
                    hasWardrobeRecipe = true;
                    break;
                }
            }

            DragAndDrop.visualMode = hasWardrobeRecipe ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform && hasWardrobeRecipe)
            {
                DragAndDrop.AcceptDrag();

                SerializedProperty property = serializedObject.FindProperty(propertyPath);
                if (property != null)
                {
                    bool addedAny = false;
                    Undo.RecordObject(target, "Add dropped " + label);

                    foreach (UnityEngine.Object droppedObject in DragAndDrop.objectReferences)
                    {
                        UMAWardrobeRecipe recipe = droppedObject as UMAWardrobeRecipe;
                        if (recipe == null || ContainsRecipe(property, recipe))
                        {
                            continue;
                        }

                        property.arraySize++;
                        property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = recipe;
                        addedAny = true;
                    }

                    if (addedAny)
                    {
                        ApplyAndRefresh();
                    }
                }
            }

            currentEvent.Use();
        }

        private void SortItems(string propertyPath, string undoName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null || property.arraySize <= 1)
            {
                return;
            }

            List<UMAWardrobeRecipe> items = ReadRecipes(property);
            items.Sort(CompareRecipesByName);

            Undo.RecordObject(target, undoName);
            WriteRecipes(property, items);
            ApplyAndRefresh();
        }

        private void RemoveDuplicateItems(string propertyPath, string undoName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null || property.arraySize == 0)
            {
                return;
            }

            List<UMAWardrobeRecipe> uniqueItems = new List<UMAWardrobeRecipe>();
            HashSet<UMAWardrobeRecipe> seenRecipes = new HashSet<UMAWardrobeRecipe>();
            for (int index = 0; index < property.arraySize; index++)
            {
                UMAWardrobeRecipe recipe = property.GetArrayElementAtIndex(index).objectReferenceValue as UMAWardrobeRecipe;
                if (seenRecipes.Add(recipe))
                {
                    uniqueItems.Add(recipe);
                }
            }

            if (uniqueItems.Count == property.arraySize)
            {
                return;
            }

            Undo.RecordObject(target, undoName);
            WriteRecipes(property, uniqueItems);
            ApplyAndRefresh();
        }

        private static int CompareRecipesByName(UMAWardrobeRecipe left, UMAWardrobeRecipe right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name);
        }

        private static bool IsItemProperty(string propertyPath)
        {
            for (int index = 0; index < ItemPropertyNames.Length; index++)
            {
                if (ItemPropertyNames[index] == propertyPath)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<UMAWardrobeRecipe> ReadRecipes(SerializedProperty property)
        {
            List<UMAWardrobeRecipe> items = new List<UMAWardrobeRecipe>(property.arraySize);
            for (int index = 0; index < property.arraySize; index++)
            {
                items.Add(property.GetArrayElementAtIndex(index).objectReferenceValue as UMAWardrobeRecipe);
            }

            return items;
        }

        private static void WriteRecipes(SerializedProperty property, List<UMAWardrobeRecipe> items)
        {
            property.arraySize = items.Count;
            for (int index = 0; index < items.Count; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = items[index];
            }
        }

        private static bool ContainsRecipe(SerializedProperty property, UMAWardrobeRecipe recipe)
        {
            for (int index = 0; index < property.arraySize; index++)
            {
                if (property.GetArrayElementAtIndex(index).objectReferenceValue == recipe)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyAndRefresh()
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            GUI.changed = true;
            Repaint();
        }
    }
}
#endif