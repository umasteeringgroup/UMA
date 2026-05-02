using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
	[CustomEditor(typeof(SharedColorTable))]
	public class SharedColorTableEditor : Editor 
	{
        private class DonorMaterialPropertySelection
        {
            public string Name;
            public ShaderPropertyType Type;
            public bool Selected;
        }

        private Material donorMaterial;
        private SkinnedMeshRenderer donorRenderer;
        private int donorRendererMaterialIndex;
            private static OverlayColorData[] RemoveColorAt(OverlayColorData[] colors, int indexToRemove)
            {
                OverlayColorData[] result = new OverlayColorData[colors.Length - 1];
                int resultIndex = 0;
                for (int colorIndex = 0; colorIndex < colors.Length; colorIndex++)
                {
                    if (colorIndex == indexToRemove)
                    {
                        continue;
                    }

                    result[resultIndex++] = colors[colorIndex];
                }

                return result;
            }

            private static OverlayColorData[] AppendColor(OverlayColorData[] colors, OverlayColorData color)
            {
                OverlayColorData[] result = new OverlayColorData[colors.Length + 1];
                for (int colorIndex = 0; colorIndex < colors.Length; colorIndex++)
                {
                    result[colorIndex] = colors[colorIndex];
                }

                result[colors.Length] = color;
                return result;
            }
        private bool donorPropertiesFoldout;
        private bool applyMaterialsFoldout;
        private bool mainDonorFoldout;
        private List<DonorMaterialPropertySelection> donorPropertySelections = new List<DonorMaterialPropertySelection>();
        private List<int> applyMaterialIndices = new List<int>();

        public override void OnInspectorGUI()
        {
            SharedColorTable sct = target as SharedColorTable;
            if (sct == null)
            {
                return;
            }

            serializedObject.Update();

            if (sct.colors == null)
            {
                sct.colors = new OverlayColorData[0];
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sharedColorName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("channelCount"));
            mainDonorFoldout = EditorGUILayout.Foldout(mainDonorFoldout, "Donor Material / Preview", true);
            if (mainDonorFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
                DrawDonorMaterialSection(sct);
                GUIHelper.EndVerticalPadded();
            }

            applyMaterialsFoldout = EditorGUILayout.Foldout(applyMaterialsFoldout, "Apply to these materials", true);
            if (applyMaterialsFoldout)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
                if (donorRenderer != null)
                {
                    DrawApplyMaterialsSection(sct);
                }
                else
                {
                    EditorGUILayout.HelpBox("Assign a Source Renderer to select materials to apply colors to.", MessageType.Info);
                }
                GUIHelper.EndVerticalPadded();
            }

            EditorGUILayout.LabelField("Shared Color Table", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Expand All"))
            {
                ExpandAllColors(serializedObject.FindProperty("colors"));
            }
            if (GUILayout.Button("Collapse All"))
            {
                CollapseAllColors(serializedObject.FindProperty("colors"));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add New Color"))
            {
                AddNewColor(sct);
            }

            using (new EditorGUI.DisabledScope(GetSelectedOverlayColorData(sct) == null))
            {
                if (GUILayout.Button("Duplicate Selected Color"))
                {
                    DuplicateSelectedColor(sct);
                }
            }
            EditorGUILayout.EndHorizontal();

            SerializedProperty colorsProperty = serializedObject.FindProperty("colors");
            bool hasDeletes = false;
            int moveUpIndex = -1;
            int moveDownIndex = -1;
            int newlySelectedIndex = -1;
            int firstSelectedIndex = -1;
            int selectedCount = 0;
            for (int i = 0; i < sct.colors.Length; i++)
            {
                var c = colorsProperty.GetArrayElementAtIndex(i);
                var showSelected = c.FindPropertyRelative("showSelected");
                var isSelected = c.FindPropertyRelative("isSelected");
                bool wasSelected = isSelected.boolValue;
                showSelected.boolValue = true;
                EditorGUILayout.PropertyField(c, true);
                if (!wasSelected && isSelected.boolValue)
                {
                    newlySelectedIndex = i;
                }

                if (isSelected.boolValue)
                {
                    selectedCount++;
                    if (firstSelectedIndex < 0)
                    {
                        firstSelectedIndex = i;
                    }
                }

                var deleteThis = c.FindPropertyRelative("deleteThis");
                var moveUpThis = c.FindPropertyRelative("moveUpThis");
                var moveDownThis = c.FindPropertyRelative("moveDownThis");
                if (deleteThis.boolValue == true)
                {
                    hasDeletes = true;
                }

                if (moveUpThis.boolValue)
                {
                    moveUpIndex = i;
                }

                if (moveDownThis.boolValue)
                {
                    moveDownIndex = i;
                }
            }

            if (selectedCount > 1 || newlySelectedIndex >= 0)
            {
                int selectedIndexToKeep = newlySelectedIndex >= 0 ? newlySelectedIndex : firstSelectedIndex;
                SetExclusiveSelectedColor(colorsProperty, selectedIndexToKeep);
            }


            serializedObject.ApplyModifiedProperties();

            bool movedColor = false;
            if (moveUpIndex > 0)
            {
                MoveColor(sct, moveUpIndex, moveUpIndex - 1);
                movedColor = true;
            }
            else if (moveDownIndex >= 0 && moveDownIndex < sct.colors.Length - 1)
            {
                MoveColor(sct, moveDownIndex, moveDownIndex + 1);
                movedColor = true;
            }

            if (movedColor)
            {
                serializedObject.Update();
            }

            if (hasDeletes)
            {
                serializedObject.Update();
                for (int i = 0; i < sct.colors.Length; i++)
                {
                    var c = colorsProperty.GetArrayElementAtIndex(i);
                    var deleteThis = c.FindPropertyRelative("deleteThis");
                    if (deleteThis.boolValue == true)
                    {
                        sct.colors = RemoveColorAt(sct.colors, i);
                        colorsProperty.DeleteArrayElementAtIndex(i);
                        i--;
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }
        }

        private void MoveColor(SharedColorTable sharedColorTable, int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || toIndex < 0 || fromIndex >= sharedColorTable.colors.Length || toIndex >= sharedColorTable.colors.Length || fromIndex == toIndex)
            {
                return;
            }

            Undo.RecordObject(sharedColorTable, "Move Shared Color");

            OverlayColorData movingColor = sharedColorTable.colors[fromIndex];
            OverlayColorData targetColor = sharedColorTable.colors[toIndex];
            sharedColorTable.colors[fromIndex] = targetColor;
            sharedColorTable.colors[toIndex] = movingColor;

#if UNITY_EDITOR
            if (movingColor != null)
            {
                movingColor.moveUpThis = false;
                movingColor.moveDownThis = false;
            }

            if (targetColor != null)
            {
                targetColor.moveUpThis = false;
                targetColor.moveDownThis = false;
            }
#endif

            EditorUtility.SetDirty(sharedColorTable);
        }

        private void AddNewColor(SharedColorTable sharedColorTable)
        {
            Undo.RecordObject(sharedColorTable, "Add Shared Color");

            OverlayColorData newColor = new OverlayColorData(sharedColorTable.channelCount);
            newColor.name = "New Color";
                sharedColorTable.colors = AppendColor(sharedColorTable.colors, newColor);

            serializedObject.Update();
            CollapseColorsExcept(serializedObject.FindProperty("colors"), -1);
        }

        private void DuplicateSelectedColor(SharedColorTable sharedColorTable)
        {
            OverlayColorData selectedColor = GetSelectedOverlayColorData(sharedColorTable);
            if (selectedColor == null)
            {
                return;
            }

            Undo.RecordObject(sharedColorTable, "Duplicate Shared Color");

            OverlayColorData duplicatedColor = selectedColor.Clone();
#if UNITY_EDITOR
            duplicatedColor.isSelected = true;
            duplicatedColor.showSelected = true;
            duplicatedColor.deleteThis = false;
#endif
            sharedColorTable.colors = AppendColor(sharedColorTable.colors, duplicatedColor);

            serializedObject.Update();
            SerializedProperty colorsProperty = serializedObject.FindProperty("colors");
            int duplicatedIndex = colorsProperty.arraySize - 1;
            SetExclusiveSelectedColor(colorsProperty, duplicatedIndex);
            CollapseColorsExcept(colorsProperty, duplicatedIndex);
        }

        private void DrawDonorMaterialSection(SharedColorTable sharedColorTable)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Select a donor material from the scene to copy material parameters. You can drag a Material directly into Donor Material, or assign a SkinnedMeshRenderer from the scene and pick one of its shared material slots. When a source renderer is assigned, use Apply to these materials to add or remove renderer materials that should receive the selected color's parameters.", MessageType.Info);

            SkinnedMeshRenderer newDonorRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Renderer", donorRenderer, typeof(SkinnedMeshRenderer), true);
            if (newDonorRenderer != donorRenderer)
            {
                donorRenderer = newDonorRenderer;
                donorRendererMaterialIndex = 0;
                applyMaterialIndices.Clear();
                SyncDonorMaterialFromRenderer();
            }

            DrawDonorRendererMaterialPicker();

            Material newDonorMaterial = (Material)EditorGUILayout.ObjectField("Donor Material", donorMaterial, typeof(Material), true);
            if (newDonorMaterial != donorMaterial)
            {
                donorMaterial = newDonorMaterial;
            }

            SyncDonorPropertySelections(donorMaterial);

            donorPropertiesFoldout = EditorGUILayout.Foldout(donorPropertiesFoldout, "Select donor properties", true);
            if (donorPropertiesFoldout)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All"))
                {
                    for (int i = 0; i < donorPropertySelections.Count; i++)
                    {
                        donorPropertySelections[i].Selected = true;
                    }
                }
                if (GUILayout.Button("Clear Selection"))
                {
                    for (int i = 0; i < donorPropertySelections.Count; i++)
                    {
                        donorPropertySelections[i].Selected = false;
                    }
                }
                GUILayout.EndHorizontal();
                using (new EditorGUI.DisabledScope(donorMaterial == null || donorPropertySelections.Count == 0))
                {
                    for (int i = 0; i < donorPropertySelections.Count; i++)
                    {
                        DonorMaterialPropertySelection selection = donorPropertySelections[i];
                        selection.Selected = EditorGUILayout.ToggleLeft($"{selection.Name} ({selection.Type})", selection.Selected);
                    }
                }

                if (donorMaterial != null && donorPropertySelections.Count == 0)
                {
                    EditorGUILayout.HelpBox("The donor material shader does not expose any supported Color, Float, or Int properties.", MessageType.Warning);
                }
            }



            using (new EditorGUI.DisabledScope(!CanCopySelected(sharedColorTable)))
            {
                if (GUILayout.Button("Copy Material Parameters to selected"))
                {
                    CopySelectedMaterialParameters(sharedColorTable, false);
                }
                if (GUILayout.Button("Copy Material Parameters to All"))
                {
                    CopySelectedMaterialParameters(sharedColorTable, true);
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawApplyMaterialsSection(SharedColorTable sharedColorTable)
        {

            if (!applyMaterialsFoldout)
            {
                return;
            }

            Material[] rendererMaterials = donorRenderer.sharedMaterials;
            PruneApplyMaterialIndices(rendererMaterials);

            if (rendererMaterials == null || rendererMaterials.Length == 0 || !HasSelectableRendererMaterials(rendererMaterials))
            {
                EditorGUILayout.HelpBox("The selected renderer has no usable shared materials.", MessageType.Warning);
                return;
            }

            for (int i = 0; i < applyMaterialIndices.Count; i++)
            {
                DrawApplyMaterialEntry(rendererMaterials, i);
            }

            using (new EditorGUI.DisabledScope(applyMaterialIndices.Count >= GetSelectableRendererMaterialCount(rendererMaterials)))
            {
                if (GUILayout.Button("Add Material"))
                {
                    AddFirstAvailableApplyMaterial(rendererMaterials);
                }
            }

            using (new EditorGUI.DisabledScope(!CanApplySelectedColorToMaterials(sharedColorTable)))
            {
                if (GUILayout.Button("Apply parameters from selected color to materials"))
                {
                    ApplySelectedColorParametersToMaterials(sharedColorTable, rendererMaterials);
                }
            }
        }

        private void DrawApplyMaterialEntry(Material[] rendererMaterials, int listIndex)
        {
            int currentMaterialIndex = applyMaterialIndices[listIndex];
            List<int> availableIndices = GetAvailableApplyMaterialIndices(rendererMaterials, currentMaterialIndex);
            string[] options = new string[availableIndices.Count];
            int selectedOptionIndex = 0;

            for (int i = 0; i < availableIndices.Count; i++)
            {
                int materialIndex = availableIndices[i];
                Material material = rendererMaterials[materialIndex];
                options[i] = $"{materialIndex}: {material.name}";
                if (materialIndex == currentMaterialIndex)
                {
                    selectedOptionIndex = i;
                }
            }

            EditorGUILayout.BeginHorizontal();
            int newOptionIndex = EditorGUILayout.Popup($"Material {listIndex + 1}", selectedOptionIndex, options);
            if (newOptionIndex >= 0 && newOptionIndex < availableIndices.Count)
            {
                applyMaterialIndices[listIndex] = availableIndices[newOptionIndex];
            }

            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                applyMaterialIndices.RemoveAt(listIndex);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDonorRendererMaterialPicker()
        {
            Material[] rendererMaterials = donorRenderer != null ? donorRenderer.sharedMaterials : null;
            bool hasRendererMaterials = rendererMaterials != null && rendererMaterials.Length > 0;

            using (new EditorGUI.DisabledScope(!hasRendererMaterials))
            {
                if (!hasRendererMaterials)
                {
                    EditorGUILayout.Popup("Renderer Material", 0, new[] { "No materials available" });
                    return;
                }

                if (donorRendererMaterialIndex >= rendererMaterials.Length)
                {
                    donorRendererMaterialIndex = 0;
                }

                string[] materialOptions = new string[rendererMaterials.Length];
                for (int i = 0; i < rendererMaterials.Length; i++)
                {
                    Material material = rendererMaterials[i];
                    materialOptions[i] = material != null ? $"{i}: {material.name}" : $"{i}: <None>";
                }

                int newIndex = EditorGUILayout.Popup("Renderer Material", donorRendererMaterialIndex, materialOptions);
                if (newIndex != donorRendererMaterialIndex)
                {
                    donorRendererMaterialIndex = newIndex;
                    SyncDonorMaterialFromRenderer();
                }
            }
        }

        private void SyncDonorMaterialFromRenderer()
        {
            if (donorRenderer == null)
            {
                donorMaterial = null;
                applyMaterialIndices.Clear();
                return;
            }

            Material[] rendererMaterials = donorRenderer.sharedMaterials;
            if (rendererMaterials == null || rendererMaterials.Length == 0)
            {
                donorMaterial = null;
                donorRendererMaterialIndex = 0;
                return;
            }

            if (donorRendererMaterialIndex < 0 || donorRendererMaterialIndex >= rendererMaterials.Length)
            {
                donorRendererMaterialIndex = 0;
            }

            donorMaterial = rendererMaterials[donorRendererMaterialIndex];
        }

        private void SetExclusiveSelectedColor(SerializedProperty colorsProperty, int selectedIndexToKeep)
        {
            if (selectedIndexToKeep < 0)
            {
                return;
            }

            for (int i = 0; i < colorsProperty.arraySize; i++)
            {
                SerializedProperty colorProperty = colorsProperty.GetArrayElementAtIndex(i);
                SerializedProperty isSelected = colorProperty.FindPropertyRelative("isSelected");
                isSelected.boolValue = i == selectedIndexToKeep;
            }
        }

        private void CollapseColorsExcept(SerializedProperty colorsProperty, int expandedIndexToKeep)
        {
            for (int i = 0; i < colorsProperty.arraySize; i++)
            {
                SerializedProperty colorProperty = colorsProperty.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = colorProperty.FindPropertyRelative("name");
                nameProperty.isExpanded = i == expandedIndexToKeep;
            }
        }

        private void ExpandAllColors(SerializedProperty colorsProperty)
        {
            if (colorsProperty == null)
            {
                return;
            }

            for (int i = 0; i < colorsProperty.arraySize; i++)
            {
                SerializedProperty colorProperty = colorsProperty.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = colorProperty.FindPropertyRelative("name");
                nameProperty.isExpanded = true;
            }
        }

        private void CollapseAllColors(SerializedProperty colorsProperty)
        {
            CollapseColorsExcept(colorsProperty, -1);
        }

        private void SyncDonorPropertySelections(Material material)
        {
            Dictionary<string, bool> previousSelections = new Dictionary<string, bool>();
            for (int i = 0; i < donorPropertySelections.Count; i++)
            {
                DonorMaterialPropertySelection selection = donorPropertySelections[i];
                previousSelections[BuildSelectionKey(selection.Name, selection.Type)] = selection.Selected;
            }

            donorPropertySelections.Clear();

            if (material == null || material.shader == null)
            {
                return;
            }

            Shader shader = material.shader;
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                ShaderPropertyType propertyType = shader.GetPropertyType(i);
                if (!IsSupportedPropertyType(propertyType))
                {
                    continue;
                }

                string propertyName = shader.GetPropertyName(i);
                string key = BuildSelectionKey(propertyName, propertyType);
                donorPropertySelections.Add(new DonorMaterialPropertySelection
                {
                    Name = propertyName,
                    Type = propertyType,
                    Selected = previousSelections.TryGetValue(key, out bool selected) ? selected : true
                });
            }
        }

        private bool CanCopySelected(SharedColorTable sharedColorTable)
        {
            if (donorMaterial == null || donorPropertySelections.Count == 0)
            {
                return false;
            }

            bool hasSelectedProperties = false;
            for (int i = 0; i < donorPropertySelections.Count; i++)
            {
                if (donorPropertySelections[i].Selected)
                {
                    hasSelectedProperties = true;
                    break;
                }
            }

            if (!hasSelectedProperties || sharedColorTable.colors == null)
            {
                return false;
            }

            for (int i = 0; i < sharedColorTable.colors.Length; i++)
            {
                OverlayColorData color = sharedColorTable.colors[i];
                if (color != null && color.isSelected)
                {
                    return true;
                }
            }

            return false;
        }

        private void CopySelectedMaterialParameters(SharedColorTable sharedColorTable, bool CopyToAll)
        {
            Undo.RecordObject(sharedColorTable, "Copy Shared Color Material Parameters");

            for (int i = 0; i < sharedColorTable.colors.Length; i++)
            {
                OverlayColorData overlayColorData = sharedColorTable.colors[i];
                if (overlayColorData == null || (!overlayColorData.isSelected && !CopyToAll))
                {
                    continue;
                }

                if (overlayColorData.PropertyBlock == null)
                {
                    overlayColorData.PropertyBlock = new UMAMaterialPropertyBlock();
                }

                for (int j = 0; j < donorPropertySelections.Count; j++)
                {
                    DonorMaterialPropertySelection selection = donorPropertySelections[j];
                    if (!selection.Selected || !donorMaterial.HasProperty(selection.Name))
                    {
                        continue;
                    }

                    switch (selection.Type)
                    {
                        case ShaderPropertyType.Color:
                            overlayColorData.PropertyBlock.SetProperty(new UMAColorProperty { name = selection.Name, Value = donorMaterial.GetColor(selection.Name) });
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            overlayColorData.PropertyBlock.SetProperty(new UMAFloatProperty { name = selection.Name, Value = donorMaterial.GetFloat(selection.Name) });
                            break;
                        case ShaderPropertyType.Int:
                            overlayColorData.PropertyBlock.SetProperty(new UMAIntProperty { name = selection.Name, Value = donorMaterial.GetInt(selection.Name) });
                            break;
                    }
                }
            }

            EditorUtility.SetDirty(sharedColorTable);
            AssetDatabase.SaveAssets();
            serializedObject.Update();
        }

        private bool CanApplySelectedColorToMaterials(SharedColorTable sharedColorTable)
        {
            OverlayColorData selectedColor = GetSelectedOverlayColorData(sharedColorTable);
            return selectedColor != null
                && selectedColor.PropertyBlock != null
                && selectedColor.PropertyBlock.shaderProperties != null
                && selectedColor.PropertyBlock.shaderProperties.Count > 0
                && applyMaterialIndices.Count > 0;
        }

        private OverlayColorData GetSelectedOverlayColorData(SharedColorTable sharedColorTable)
        {
            if (sharedColorTable.colors == null)
            {
                return null;
            }

            for (int i = 0; i < sharedColorTable.colors.Length; i++)
            {
                OverlayColorData color = sharedColorTable.colors[i];
                if (color != null && color.isSelected)
                {
                    return color;
                }
            }

            return null;
        }

        private void ApplySelectedColorParametersToMaterials(SharedColorTable sharedColorTable, Material[] rendererMaterials)
        {
            OverlayColorData selectedColor = GetSelectedOverlayColorData(sharedColorTable);
            if (selectedColor == null || selectedColor.PropertyBlock == null || selectedColor.PropertyBlock.shaderProperties == null)
            {
                return;
            }

            List<Object> materialsToUpdate = new List<Object>();
            for (int i = 0; i < applyMaterialIndices.Count; i++)
            {
                int materialIndex = applyMaterialIndices[i];
                if (materialIndex < 0 || materialIndex >= rendererMaterials.Length)
                {
                    continue;
                }

                Material material = rendererMaterials[materialIndex];
                if (material != null)
                {
                    materialsToUpdate.Add(material);
                }
            }

            if (materialsToUpdate.Count == 0)
            {
                return;
            }

            Undo.RecordObjects(materialsToUpdate.ToArray(), "Apply Shared Color Parameters To Materials");
            for (int i = 0; i < materialsToUpdate.Count; i++)
            {
                Material material = materialsToUpdate[i] as Material;
                if (material == null)
                {
                    continue;
                }

                for (int j = 0; j < selectedColor.PropertyBlock.shaderProperties.Count; j++)
                {
                    UMAProperty property = selectedColor.PropertyBlock.shaderProperties[j];
                    if (property != null)
                    {
                        property.Apply(material, -1);
                    }
                }

                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
        }

        private void PruneApplyMaterialIndices(Material[] rendererMaterials)
        {
            HashSet<int> seen = new HashSet<int>();
            for (int i = applyMaterialIndices.Count - 1; i >= 0; i--)
            {
                int materialIndex = applyMaterialIndices[i];
                if (rendererMaterials == null
                    || materialIndex < 0
                    || materialIndex >= rendererMaterials.Length
                    || rendererMaterials[materialIndex] == null
                    || !seen.Add(materialIndex))
                {
                    applyMaterialIndices.RemoveAt(i);
                }
            }
        }

        private void AddFirstAvailableApplyMaterial(Material[] rendererMaterials)
        {
            for (int i = 0; i < rendererMaterials.Length; i++)
            {
                if (rendererMaterials[i] != null && !applyMaterialIndices.Contains(i))
                {
                    applyMaterialIndices.Add(i);
                    return;
                }
            }
        }

        private List<int> GetAvailableApplyMaterialIndices(Material[] rendererMaterials, int currentMaterialIndex)
        {
            List<int> availableIndices = new List<int>();
            for (int i = 0; i < rendererMaterials.Length; i++)
            {
                if (rendererMaterials[i] == null)
                {
                    continue;
                }

                if (i == currentMaterialIndex || !applyMaterialIndices.Contains(i))
                {
                    availableIndices.Add(i);
                }
            }

            return availableIndices;
        }

        private bool HasSelectableRendererMaterials(Material[] rendererMaterials)
        {
            return GetSelectableRendererMaterialCount(rendererMaterials) > 0;
        }

        private int GetSelectableRendererMaterialCount(Material[] rendererMaterials)
        {
            int count = 0;
            for (int i = 0; i < rendererMaterials.Length; i++)
            {
                if (rendererMaterials[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsSupportedPropertyType(ShaderPropertyType propertyType)
        {
            bool supported = propertyType == ShaderPropertyType.Color
                || propertyType == ShaderPropertyType.Float
                || propertyType == ShaderPropertyType.Int
                || propertyType == ShaderPropertyType.Range;
            return supported;
        }

        private static string BuildSelectionKey(string propertyName, ShaderPropertyType propertyType)
        {
            return propertyName + ":" + propertyType;
        }
    }
}

