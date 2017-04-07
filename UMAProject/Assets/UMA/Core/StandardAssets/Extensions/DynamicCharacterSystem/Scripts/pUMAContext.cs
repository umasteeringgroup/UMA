using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMACharacterSystem;

/// <summary>
/// Gloal container for various UMA objects in the scene. Marked as partial so the developer can add to this if necessary
/// </summary>
public partial class UMAContext : MonoBehaviour
{
	/// <summary>
	/// The DynamicCharacterSystem
	/// </summary>
	public DynamicCharacterSystemBase dynamicCharacterSystem;

#if UNITY_EDITOR
	public static GameObject CreateEditorContext()
	{
		GameObject EditorUMAContext = null;
		if (UnityEditor.BuildPipeline.isBuildingPlayer)
			return null;
		if (Application.isPlaying)
		{
			Debug.LogWarning("There was no UMAContext in this scene. Please add the UMA_DCS prefab to this scene before you try to generate an UMA.");
			return null;
		}
		Debug.Log("UMA Recipe Editor created an UMAEditorContext to enable editing. This will auto delete once you have finished editing your recipe or you add the UMA_DCS prefab to this scene.");
		//if there is already an EditorUMAContext use it
		if (UMAContext.FindInstance() != null)
		{
			if (UMAContext.FindInstance().gameObject.name == "UMAEditorContext")
			{
				EditorUMAContext = UMAContext.FindInstance().gameObject;
			}
			else if (UMAContext.FindInstance().gameObject.transform.parent.gameObject.name == "UMAEditorContext")
			{
				EditorUMAContext = UMAContext.FindInstance().gameObject.transform.parent.gameObject;
			}
		}
		else if (GameObject.Find("UMAEditorContext"))
		{
			EditorUMAContext = GameObject.Find("UMAEditorContext");
		}
		else
		{
			EditorUMAContext = new GameObject();
			EditorUMAContext.name = "UMAEditorContext";
		}
		//Make this GameObject not show up in the scene or save
		EditorUMAContext.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
		//if this gameobject does not contain an UMAContext add it - we have to call it UMAContext because UMAContext.FindInstance searches for that game object
		var thisUMAContext = UMAContext.Instance = EditorUMAContext.GetComponentInChildren<UMAContext>();
		if (UMAContext.Instance == null)
		{
			var thisUMAContextGO = new GameObject();
			thisUMAContextGO.name = "UMAContext";
			thisUMAContextGO.transform.parent = EditorUMAContext.transform;
			thisUMAContext = thisUMAContextGO.AddComponent<UMAContext>();
			UMAContext.Instance = thisUMAContext;
		}
		//we need to add the libraries as components of the game object too
		//and then set THOSE components to the umaContext component
		thisUMAContext.raceLibrary = thisUMAContext.gameObject.AddComponent<DynamicRaceLibrary>();
		(thisUMAContext.raceLibrary as DynamicRaceLibrary).dynamicallyAddFromResources = true;
		(thisUMAContext.raceLibrary as DynamicRaceLibrary).dynamicallyAddFromAssetBundles = true;
		thisUMAContext.overlayLibrary = thisUMAContext.gameObject.AddComponent<DynamicOverlayLibrary>();
		(thisUMAContext.overlayLibrary as DynamicOverlayLibrary).dynamicallyAddFromResources = true;
		(thisUMAContext.overlayLibrary as DynamicOverlayLibrary).dynamicallyAddFromAssetBundles = true;
		thisUMAContext.slotLibrary = thisUMAContext.gameObject.AddComponent<DynamicSlotLibrary>();
		(thisUMAContext.slotLibrary as DynamicSlotLibrary).dynamicallyAddFromResources = true;
		(thisUMAContext.slotLibrary as DynamicSlotLibrary).dynamicallyAddFromAssetBundles = true;
		thisUMAContext.dynamicCharacterSystem = thisUMAContext.gameObject.AddComponent<UMACharacterSystem.DynamicCharacterSystem>();
		(thisUMAContext.dynamicCharacterSystem as DynamicCharacterSystem).dynamicallyAddFromResources = true;
		(thisUMAContext.dynamicCharacterSystem as DynamicCharacterSystem).dynamicallyAddFromAssetBundles = true;
		var thisDAL = thisUMAContext.gameObject.AddComponent<DynamicAssetLoader>();
		DynamicAssetLoader.Instance = thisDAL;
		return EditorUMAContext;
	}
#endif
}
