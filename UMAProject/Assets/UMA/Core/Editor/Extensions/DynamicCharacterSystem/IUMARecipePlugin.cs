using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public interface IUMARecipePlugin  
{
	bool foldOut {
		get; set;
	}
	string GetSectionLabel();
	void OnEnable();
	void OnDestroy();
	void OnInspectorGUI(SerializedObject serializedObject);
}
