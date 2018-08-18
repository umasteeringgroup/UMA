using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Reflection;

public static class InspectorUtlity 
{
    /// <summary>
    /// Creates a new inspector window instance and locks it to inspect the specified target
    /// </summary>
    public static void InspectTarget(Object target)
    {
        var prevSelection = Selection.activeObject;

        // Get a reference to the `InspectorWindow` type object
        var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        // Create an InspectorWindow instance
        var inspectorInstance = ScriptableObject.CreateInstance(inspectorType) as EditorWindow;
        // We display it - currently, it will inspect whatever gameObject is currently selected
        // So we need to find a way to let it inspect/aim at our target GO that we passed
        // For that we do a simple trick:
        // 1- Cache the current selected gameObject
        // 2- Set the current selection to our target GO (so now all inspectors are targeting it)
        // 3- Lock our created inspector to that target
        // 4- Fallback to our previous selection
        inspectorInstance.Show();

        // Set the selection to GO we want to inspect
        // Selection.activeGameObject = target;
        Selection.instanceIDs = new int[] { target.GetInstanceID() };
        // Get a ref to the "locked" property, which will lock the state of the inspector to the current inspected target
        var isLocked = inspectorType.GetProperty("isLocked", BindingFlags.Instance | BindingFlags.Public);
        // Invoke `isLocked` setter method passing 'true' to lock the inspector
        isLocked.GetSetMethod().Invoke(inspectorInstance, new object[] { true });
        // Finally revert back to the previous selection so that other inspectors continue to inspect whatever they were inspecting...
        Selection.activeObject = prevSelection;
    }
}