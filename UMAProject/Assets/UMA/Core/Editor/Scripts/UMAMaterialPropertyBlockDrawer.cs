using UnityEngine;
using UnityEditor;
using UMA.Editors;

/// <summary>
/// This partial class implements the editor specific functions for the properties.
/// </summary>

namespace UMA
{
    public static class UMAMaterialPropertyBlockDrawer
    {
        private class TemplatePropertyEntry
        {
            public bool selected = true;
            public UMAProperty property;
            public string typeName;
        }

        private class TemplateMaterialEntry
        {
            public int materialInstanceID;
            public string materialName;
            public bool expanded = true;
            public readonly System.Collections.Generic.List<TemplatePropertyEntry> properties = new System.Collections.Generic.List<TemplatePropertyEntry>();
        }

        static int TypeIndex = 0;
        static bool templateMaterialExpanded = false;
        static readonly System.Collections.Generic.List<TemplateMaterialEntry> templateMaterials = new System.Collections.Generic.List<TemplateMaterialEntry>();



        /// <summary>
        /// Performs editing on a UMAMaterialPropertyBlock. Returns true if changed, false if not changed
        /// </summary>
        /// <param name="umpb">UMAMaterialPropertyBlock</param>
        /// <returns></returns>
        public static bool OnGUI(UMAMaterialPropertyBlock umpb)
        {
            UMAMaterialPropertyBlock.CheckInitialize();
            GUILayout.Space(5);

            bool changed = false;
            EditorGUI.BeginChangeCheck();               

            GUIHelper.BeginVerticalPadded(10, new Color(0.65f, 0.675f, 1f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shader Properties",GUILayout.ExpandWidth(true));
            GUILayout.Label("Always Update",GUILayout.ExpandWidth(false));
            umpb.alwaysUpdate = GUILayout.Toggle(umpb.alwaysUpdate, "",GUILayout.ExpandWidth(false));
            GUILayout.Label("Parms Only", GUILayout.ExpandWidth(false));
            umpb.alwaysUpdateParms = GUILayout.Toggle(umpb.alwaysUpdateParms, "", GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            templateMaterialExpanded = GUIHelper.FoldoutBar(templateMaterialExpanded, "Template Material");
            if (templateMaterialExpanded)
            {
                DrawTemplateMaterialSection(umpb);
            }



            GUILayout.BeginHorizontal();

            TypeIndex = EditorGUILayout.Popup(TypeIndex, UMAMaterialPropertyBlock.PropertyTypeStrings);
            if (GUILayout.Button("Add Type"))
            {
                umpb.AddProperty(UMAMaterialPropertyBlock.availableTypes[TypeIndex], UMAMaterialPropertyBlock.PropertyTypeStrings[TypeIndex]);
            }

            GUILayout.EndHorizontal(); 


            bool dark = false;
            UMAProperty delme = null;

            if (umpb.shaderProperties != null)
            {
                foreach (UMAProperty up in umpb.shaderProperties)
                {
                    if (up == null)
                    {
                        continue;
                    }

                    GUIHelper.BeginVerticalIndented(3, new Color(0.75f, 0.75f, 1f));
                    if (dark) 
                    {
                        GUIHelper.BeginVerticalPadded(5, new Color(0.85f, 0.85f, 1f));
                        dark = false;
                    }
                    else
                    {
                        GUIHelper.BeginVerticalPadded(5, new Color(0.65f, 0.65f, 0.9f));
                        dark = true;
                    }

                    if (up.OnGUI())
                    {
                        delme = up;
                    }

                    GUIHelper.EndVerticalPadded(5);

                    GUIHelper.EndVerticalIndented();
                }
                if (delme != null)
                {
                    umpb.shaderProperties.Remove(delme);
                }
            }
            GUIHelper.EndVerticalPadded(5);
            GUILayout.Space(5);
            changed = EditorGUI.EndChangeCheck();
            return changed;
        }

        private static void DrawTemplateMaterialSection(UMAMaterialPropertyBlock umpb)
        {
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", GUILayout.Width(100)))
            {
                SetAllTemplatePropertySelections(true);
            }
            if (GUILayout.Button("Clear All", GUILayout.Width(100)))
            {
                SetAllTemplatePropertySelections(false);
            }
            EditorGUILayout.EndHorizontal();

            Rect dropArea = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop SkinnedMeshRenderer or Material here to copy materials");

            Event evt = Event.current;
            if (dropArea.Contains(evt.mousePosition))
            {
                SkinnedMeshRenderer droppedRenderer = null;
                Material droppedMaterial = null;

                for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                {
                    Object draggedObject = DragAndDrop.objectReferences[i];
                    droppedMaterial = draggedObject as Material;
                    if (droppedMaterial != null)
                    {
                        break;
                    }

                    droppedRenderer = draggedObject as SkinnedMeshRenderer;
                    if (droppedRenderer == null)
                    {
                        GameObject draggedGameObject = draggedObject as GameObject;
                        if (draggedGameObject != null)
                        {
                            droppedRenderer = draggedGameObject.GetComponent<SkinnedMeshRenderer>();
                        }
                    }

                    if (droppedRenderer != null)
                    {
                        break;
                    }
                }

                if (evt.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = (droppedRenderer != null || droppedMaterial != null) ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    evt.Use();
                }
                else if (evt.type == EventType.DragPerform)
                {
                    if (droppedRenderer != null || droppedMaterial != null)
                    {
                        DragAndDrop.AcceptDrag();
                        if (droppedRenderer != null)
                        {
                            CacheTemplateMaterials(droppedRenderer.sharedMaterials);
                        }
                        else
                        {
                            CacheTemplateMaterials(new[] { droppedMaterial });
                        }
                    }
                    evt.Use();
                }
            }

            EditorGUILayout.Space();

            int removeMaterialIndex = -1;

            for (int i = 0; i < templateMaterials.Count; i++)
            {
                TemplateMaterialEntry entry = templateMaterials[i];
                if (entry == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                entry.expanded = EditorGUILayout.Foldout(entry.expanded, entry.materialName, true);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    removeMaterialIndex = i;
                }
                EditorGUILayout.EndHorizontal();

                if (removeMaterialIndex == i)
                {
                    continue;
                }

                if (!entry.expanded)
                {
                    continue;
                }

                GUIHelper.BeginVerticalPadded(5, new Color(0.65f, 0.65f, 0.9f));
                for (int j = 0; j < entry.properties.Count; j++)
                {
                    TemplatePropertyEntry propertyEntry = entry.properties[j];
                    if (propertyEntry == null || propertyEntry.property == null)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    propertyEntry.selected = EditorGUILayout.Toggle(propertyEntry.selected, GUILayout.Width(20));
                    EditorGUILayout.LabelField(propertyEntry.property.name, GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField(propertyEntry.typeName, GUILayout.Width(140));
                    EditorGUILayout.EndHorizontal();
                }
                GUIHelper.EndVerticalPadded(5);
            }

            if (removeMaterialIndex >= 0)
            {
                templateMaterials.RemoveAt(removeMaterialIndex);
            }

            using (new EditorGUI.DisabledScope(templateMaterials.Count == 0))
            {
                if (GUILayout.Button("Copy selected material properties"))
                {
                    CopySelectedTemplateMaterialProperties(umpb);
                }
            }

            GUIHelper.EndVerticalPadded(5);
        }

        private static void CacheTemplateMaterials(Material[] materials)
        {
            templateMaterials.Clear();
            if (materials == null || materials.Length == 0)
            {
                return;
            }

            var addedMaterials = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    continue;
                }

                int materialInstanceID = material.GetInstanceID();
                if (!addedMaterials.Add(materialInstanceID))
                {
                    continue;
                }

                TemplateMaterialEntry entry = new TemplateMaterialEntry();
                entry.materialInstanceID = materialInstanceID;
                entry.materialName = material.name;
                BuildTemplatePropertiesForMaterial(material, entry.properties);
                templateMaterials.Add(entry);
            }
        }

        private static void BuildTemplatePropertiesForMaterial(Material material, System.Collections.Generic.List<TemplatePropertyEntry> entries)
        {
            entries.Clear();
            if (material == null || material.shader == null)
            {
                return;
            }

            Shader shader = material.shader;
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                UnityEngine.Rendering.ShaderPropertyType propertyType = shader.GetPropertyType(i);
                if (!IsSupportedTemplatePropertyType(propertyType))
                {
                    continue;
                }

                string propertyName = shader.GetPropertyName(i);
                UMAProperty property = CreateTemplateProperty(material, propertyName, propertyType);
                if (property == null)
                {
                    continue;
                }

                TemplatePropertyEntry entry = new TemplatePropertyEntry();
                entry.property = property;
                entry.typeName = property.GetType().Name;
                entries.Add(entry);
            }
        }

        private static bool IsSupportedTemplatePropertyType(UnityEngine.Rendering.ShaderPropertyType propertyType)
        {
            return propertyType == UnityEngine.Rendering.ShaderPropertyType.Color
                || propertyType == UnityEngine.Rendering.ShaderPropertyType.Vector
                || propertyType == UnityEngine.Rendering.ShaderPropertyType.Float
                || propertyType == UnityEngine.Rendering.ShaderPropertyType.Range
                || propertyType == UnityEngine.Rendering.ShaderPropertyType.Int;
        }

        private static UMAProperty CreateTemplateProperty(Material material, string propertyName, UnityEngine.Rendering.ShaderPropertyType propertyType)
        {
            if (propertyType == UnityEngine.Rendering.ShaderPropertyType.Color)
            {
                return new UMAColorProperty() { name = propertyName, Value = material.GetColor(propertyName) };
            }
            if (propertyType == UnityEngine.Rendering.ShaderPropertyType.Vector)
            {
                return new UMAVectorProperty() { name = propertyName, Value = material.GetVector(propertyName) };
            }
            if (propertyType == UnityEngine.Rendering.ShaderPropertyType.Int)
            {
                return new UMAIntProperty() { name = propertyName, Value = material.GetInt(propertyName) };
            }
            if (propertyType == UnityEngine.Rendering.ShaderPropertyType.Float || propertyType == UnityEngine.Rendering.ShaderPropertyType.Range)
            {
                return new UMAFloatProperty() { name = propertyName, Value = material.GetFloat(propertyName) };
            }

            return null;
        }

        private static void CopySelectedTemplateMaterialProperties(UMAMaterialPropertyBlock umpb)
        {
            for (int i = 0; i < templateMaterials.Count; i++)
            {
                TemplateMaterialEntry materialEntry = templateMaterials[i];
                if (materialEntry == null)
                {
                    continue;
                }

                for (int j = 0; j < materialEntry.properties.Count; j++)
                {
                    TemplatePropertyEntry propertyEntry = materialEntry.properties[j];
                    if (propertyEntry == null || !propertyEntry.selected || propertyEntry.property == null)
                    {
                        continue;
                    }

                    umpb.SetProperty(propertyEntry.property.Clone());
                }
            }
        }

        private static void SetAllTemplatePropertySelections(bool selected)
        {
            for (int i = 0; i < templateMaterials.Count; i++)
            {
                TemplateMaterialEntry materialEntry = templateMaterials[i];
                if (materialEntry == null)
                {
                    continue;
                }

                for (int j = 0; j < materialEntry.properties.Count; j++)
                {
                    TemplatePropertyEntry propertyEntry = materialEntry.properties[j];
                    if (propertyEntry != null)
                    {
                        propertyEntry.selected = selected;
                    }
                }
            }
        }
    }
}