using UnityEditor;
using UnityEngine;
using UMA;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UMA.Editors;
using UnityEngine.SceneManagement;
using UMA.CharacterSystem;

[CustomEditor(typeof(DNA))]
public class DNAEditor : Editor
{
    SerializedProperty nameProp;
    SerializedProperty descriptionProp;
    SerializedProperty defaultValueProp;
    SerializedProperty effectsProp;

    // For adding new DNAEffect
    private int selectedEffectTypeIndex = 0;
    private DNAEffect newEffectInstance = null;
    private Type[] effectTypes;
    private string[] effectTypeNames;
    private bool editorExpanded = true;
    private bool initialized = false;
    private const string PrefKey_AddNewExpanded = "UMA.DNAEditor.AddNewEffectExpanded";

    private void OnEnable()
    {
        // Load persisted UI state
        editorExpanded = EditorPrefs.GetBool(PrefKey_AddNewExpanded, true);
    }

    private void OnDisable()
    {
        // Save UI state
        EditorPrefs.SetBool(PrefKey_AddNewExpanded, editorExpanded);
    }

    void Initialize()
    {
        if (initialized) return;
        initialized = true;
        // Find all non-abstract, non-generic subclasses of DNAEffect
        var baseType = typeof(DNAEffect);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var typesList = new List<Type>();
        var namesList = new List<string>();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            if (types == null) continue;
            foreach (var t in types)
            {
                if (t == null) continue;
                if (baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericType && t.GetConstructor(Type.EmptyTypes) != null)
                {
                    typesList.Add(t);
                    namesList.Add(t.Name);
                }
            }
        }
        effectTypes = typesList.ToArray();
        effectTypeNames = namesList.ToArray();
    }

    public override void OnInspectorGUI()
    {
        Initialize();
        DNA targetDNA = target as DNA;

        serializedObject.Update();

        GUILayout.Label("DNA Editor", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Ping DNA Asset", GUILayout.Width(150)))
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
        targetDNA.description = EditorGUILayout.DelayedTextField("Description", targetDNA.description);
        targetDNA.defaultValue = EditorGUILayout.Slider("Default Value", targetDNA.defaultValue, 0f, 1f);
        EditorGUILayout.Space();


        // Foldout for Add New Effect with persistence
        bool prevExpanded = editorExpanded;
        editorExpanded = GUIHelper.FoldoutBar(editorExpanded, "Add New Effect Settings");
        if (editorExpanded != prevExpanded)
        {
            EditorPrefs.SetBool(PrefKey_AddNewExpanded, editorExpanded);
        }
        if (editorExpanded)
        {
            ShowAddNew(targetDNA);
        }

        // Draw existing effects
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Existing Effects", EditorStyles.boldLabel);
        int deleteme = -1;
        for (int i = 0; i < targetDNA.effects.Count; i++)
        {
            deleteme = ShowEffect(targetDNA, deleteme, i);
        }
        if (deleteme >= 0)
        {
            targetDNA.effects.RemoveAt(deleteme);
            serializedObject.Update();
        }

        EditorGUILayout.Space();


        serializedObject.ApplyModifiedProperties();
    }

    private int ShowEffect(DNA targetDNA, int deleteme, int i)
    {
        var effect = targetDNA.effects[i]; 
        if (effect == null)
        {
            // draw "effect is null" message. Give option to remove it
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Effect at index {i} is null. Remove it?", EditorStyles.boldLabel);
            if (GUILayout.Button("Remove"))
            {
                deleteme = i;
            }
            EditorGUILayout.EndVertical();
            return deleteme;
        }
        bool deletemeFlag = false;
        effect.expanded = GUIHelper.FoldoutBarWithDelete(effect.expanded, effect.title, out deletemeFlag);

        if (deletemeFlag)
        {
            deleteme = i; // Mark this index for deletion
            return deleteme;
        }
        // Draw the effect's GUI
        if (effect.expanded)
        {
            GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{effect.GetType().Name}: {effect.EffectName}", EditorStyles.boldLabel);
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                effectsProp.DeleteArrayElementAtIndex(i);
                deleteme = i; // Mark this index for deletion
            }
            EditorGUILayout.EndHorizontal();
            // Draw the effect's GUI
            effect.DoGui(true);
            GUIHelper.EndVerticalPadded();
        }
        return deleteme;
    }

    private void ShowAddNew(DNA targetDNA)
    {
        GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
        EditorGUILayout.LabelField("Add the New Effect", EditorStyles.boldLabel);

        if (effectTypes.Length > 0)
        {
            selectedEffectTypeIndex = EditorGUILayout.Popup("Effect Type", selectedEffectTypeIndex, effectTypeNames);

            // Create a new instance if needed or if type changed
            Type selectedType = effectTypes[selectedEffectTypeIndex];
            if (newEffectInstance == null || newEffectInstance.GetType() != selectedType)
            {
                newEffectInstance = (DNAEffect)Activator.CreateInstance(selectedType);
            }

            // Draw fields for the new effect instance
            if (newEffectInstance != null)
            {

                newEffectInstance.DoGui(true);

                if (GUILayout.Button("Add Effect"))
                {
                    targetDNA.effects.Add(newEffectInstance);
                    newEffectInstance = (DNAEffect)Activator.CreateInstance(selectedType); // reset for next add
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No DNAEffect types found.", MessageType.Warning);
        }
        GUIHelper.EndVerticalPadded();
    }

    // Helper to deep clone a DNAEffect (since Unity doesn't serialize managedReferenceValue directly from a temp object)
    private DNAEffect CloneDNAEffect(DNAEffect effect)
    {
        var type = effect.GetType();
        var clone = (DNAEffect)Activator.CreateInstance(type);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.IsNotSerialized) continue;
            field.SetValue(clone, field.GetValue(effect));
        }
        return clone;
    }
}