using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMACharacterSystem;


[CustomEditor(typeof(DynamicCharacterSystem))]
[CanEditMultipleObjects]
public class DynamicCharacterSystemEditor : Editor
{
	private SerializedObject m_Object;
	private DynamicCharacterSystem dCharacterSystem;
	string recipeInBundleToFind = "";

	public void OnEnable()
	{
		m_Object = new SerializedObject(target);
		dCharacterSystem = m_Object.targetObject as DynamicCharacterSystem;
	}

	public override void OnInspectorGUI(){
		Editor.DrawPropertiesExcluding (serializedObject, new string[0]);
		serializedObject.ApplyModifiedProperties ();
		SerializedProperty dynamicallyAddFromAssetBundles = serializedObject.FindProperty ("dynamicallyAddFromAssetBundles");
		if (Application.isPlaying && dynamicallyAddFromAssetBundles.boolValue) {
			EditorGUILayout.Space ();
			recipeInBundleToFind = EditorGUILayout.TextField (recipeInBundleToFind);
			if (GUILayout.Button ("Find Recipes's AssetBundle")) {
				if (recipeInBundleToFind != "")
					dCharacterSystem.GetOriginatingAssetBundle (recipeInBundleToFind);
			}
		}
	}
}
