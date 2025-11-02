using UnityEditor;
using UnityEngine;
using UMA;
using System;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(DNAGroup))]
public class DNAGroupEditor : Editor
{
    private SerializedProperty dnaAreaProp;
    private SerializedProperty dnaListProp;

    private void OnEnable()
    {
        dnaAreaProp = serializedObject.FindProperty("DNAArea");
        dnaListProp = serializedObject.FindProperty("dnaList");
    }

    public override void OnInspectorGUI()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorGUILayout.HelpBox("Editor is compiling or updating. Please wait.", MessageType.Info);
            return;
        }

        serializedObject.Update();

        EditorGUILayout.PropertyField(dnaAreaProp, new GUIContent("DNA Area"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("DNA List", EditorStyles.boldLabel);

        if (dnaListProp == null)
        {
            EditorGUILayout.HelpBox("dnaList property not found.", MessageType.Error);
        }
        else
        {
            for (int i =0; i < dnaListProp.arraySize; i++)
            {
                SerializedProperty dnaProp = dnaListProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical("box");
                DrawDNAEntry(dnaProp, i);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove DNA", GUILayout.Width(100)))
                {
                    Undo.RecordObject(target, "Remove DNA from Group");
                    dnaListProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add DNA", GUILayout.Width(100)))
        {
            Undo.RecordObject(target, "Add DNA to Group");
            dnaListProp.arraySize++;
            // leave as null initially (user can assign or create)
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
        EditorGUILayout.EndHorizontal();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDNAEntry(SerializedProperty dnaProp, int index)
    {
        // Draw object field for the DNA reference first
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(dnaProp, new GUIContent($"DNA {index +1}"));
        if (dnaProp.objectReferenceValue != null)
        {
            if (GUILayout.Button("Ping", GUILayout.Width(50)))
            {
                EditorGUIUtility.PingObject(dnaProp.objectReferenceValue);
            }
        }
        EditorGUILayout.EndHorizontal();

        UnityEngine.Object dnaObj = dnaProp.objectReferenceValue;
        if (dnaObj == null)
        {
            EditorGUILayout.HelpBox("No DNA asset assigned.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create DNA Asset"))
            {
                CreateAndAssignDNAAsset(dnaProp);
            }
            EditorGUILayout.EndHorizontal();
            return;
        }

        // Inline edit the DNA asset
        var so = new SerializedObject(dnaObj);
        so.Update();
        SerializedProperty descriptionProp = so.FindProperty("description");
        SerializedProperty defaultValueProp = so.FindProperty("defaultValue");
        SerializedProperty effectsProp = so.FindProperty("effects");

        if (descriptionProp != null)
        {
            EditorGUILayout.PropertyField(descriptionProp, new GUIContent("Description"));
        }
        if (defaultValueProp != null)
        {
            defaultValueProp.floatValue = EditorGUILayout.Slider("Default Value", defaultValueProp.floatValue,0f,1f);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("DNA Effects", EditorStyles.boldLabel);
        DrawDNAEffectsList(so, effectsProp);

        if (so.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(dnaObj);
        }
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
        EditorUtility.SetDirty(target);
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

    private void DrawDNAEffectsList(SerializedObject dnaSO, SerializedProperty effectsProp)
    {
        if (effectsProp == null)
        {
            EditorGUILayout.HelpBox("Effects property not found on DNA.", MessageType.Warning);
            return;
        }

        // Draw existing effects
        for (int j =0; j < effectsProp.arraySize; j++)
        {
            SerializedProperty effectProp = effectsProp.GetArrayElementAtIndex(j);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Effect {j +1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(70)))
            {
                effectsProp.DeleteArrayElementAtIndex(j);
                dnaSO.ApplyModifiedProperties();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            // Support both SerializeReference and basic struct/class drawing
            if (effectProp.propertyType == SerializedPropertyType.ManagedReference)
            {
                string typeName = string.IsNullOrEmpty(effectProp.managedReferenceFullTypename) ? "(None)" : effectProp.managedReferenceFullTypename.Split(' ').Last();
                EditorGUILayout.LabelField("Type", typeName);
            }
            EditorGUILayout.PropertyField(effectProp, GUIContent.none, true);
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add DNAEffect"))
        {
            var types = GetDNAEffectTypes();
            if (types.Count ==0)
            {
                // Fallback: add a null entry so user can assign later if not using SerializeReference
                effectsProp.arraySize++;
                dnaSO.ApplyModifiedProperties();
            }
            else
            {
                var menu = new GenericMenu();
                foreach (var t in types)
                {
                    var typeLocal = t; // capture loop variable
                    menu.AddItem(new GUIContent(typeLocal.Name), false, () =>
                    {
                        effectsProp.arraySize++;
                        var elem = effectsProp.GetArrayElementAtIndex(effectsProp.arraySize -1);
                        if (elem.propertyType == SerializedPropertyType.ManagedReference)
                        {
                            elem.managedReferenceValue = Activator.CreateInstance(typeLocal);
                        }
                        // If not managed reference, leave as default/null
                        dnaSO.ApplyModifiedProperties();
                    });
                }
                menu.ShowAsContext();
            }
        }
        EditorGUILayout.EndHorizontal();
    }
}
