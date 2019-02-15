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
	/// <param name="target">The target object to inspect</param>
	/// <param name="revertProjectSelection">If true reverts the object selected in the project to the original selection. Otherwise selects the target object</param>
	public static EditorWindow InspectTarget(Object target, bool revertProjectSelection = false)
    {
        var prevSelection = Selection.activeObject;

        // Get a reference to the `InspectorWindow` type object
        var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        // Create an InspectorWindow instance
        var inspectorInstance = ScriptableObject.CreateInstance(inspectorType) as EditorWindow;
		// Get a ref to the "locked" property, which will lock the state of the inspector to the current inspected target
		var isLocked = inspectorType.GetProperty("isLocked", BindingFlags.Instance | BindingFlags.Public);
		// Invoke `isLocked` setter method passing 'false' to UNlock the inspector
		isLocked.GetSetMethod().Invoke(inspectorInstance, new object[] { false });
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
		
		// Invoke `isLocked` setter method passing 'true' to lock the inspector
		isLocked.GetSetMethod().Invoke(inspectorInstance, new object[] { true });
		// Finally revert back to the previous selection so that other inspectors continue to inspect whatever they were inspecting...
		Selection.activeObject = prevSelection;
		if (revertProjectSelection)
			EditorGUIUtility.PingObject(prevSelection);
		return inspectorInstance;
	}
	/// <summary>
	/// Returns an array of editors for the specified inspectorWindow. 
	/// CAUTION: This will now return the correct array straight after InspectTarget is called.
	/// You need to wait for the inspector windows to repaint, and/or keep checking this array until it contains the expected editor for the expected target
	/// </summary>
	/// <param name="inspectorWindow"></param>
	/// <returns></returns>
	public static Editor[] GetInspectorsEditors(EditorWindow inspectorWindow)
	{
		Editor[] editors = new Editor[0];
		var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
		if (inspectorWindow.GetType() != inspectorType)
		{
			Debug.LogWarning("The supplied window was not an InspectorWindow");
			return null;
		}
		var activeEditorTrackerPInfo = inspectorType.GetProperty("tracker", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		//Unity 2018.3 changed the get method to private so pass true along with the request for it
		var activeEditorTracker = activeEditorTrackerPInfo.GetGetMethod(true).Invoke(inspectorWindow, new object[0]);
		if (((ActiveEditorTracker)activeEditorTracker) != null)
		{
			editors = ((ActiveEditorTracker)activeEditorTracker).activeEditors;
		}
		return editors;
	}

	private static System.Reflection.MethodInfo m_RepaintInspectors = null;

	/// <summary>
	/// Repaints all Inspector Windows. I some circumstances popup windows dont repaint immediately. Calling this forces them to do so
	/// </summary>
	public static void RepaintAllInspectors()
	{
		if (m_RepaintInspectors == null)
		{
			var inspWin = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
			m_RepaintInspectors = inspWin.GetMethod("RepaintAllInspectors", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
		}
		m_RepaintInspectors.Invoke(null, null);
	}
}