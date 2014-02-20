using UnityEngine;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Collections;
using System.Collections.Generic;
using UMA;

public abstract class UMAAvatarBase : MonoBehaviour {

	public UMAContext context;
	public UMAData umaData;
	public UMARecipeBase umaRecipe;
	public UMAGeneratorBase umaGenerator;
	public RuntimeAnimatorController animationController;
	[NonSerialized]
	public GameObject umaChild;
	private RaceData umaRace = null;
	
	public virtual void Start()
	{
		Initialize();
	}
	public void Initialize()
	{
		if (context == null)
		{
			context = UMAContext.FindInstance();
		}
		
		if (umaData == null)
		{
			umaData = GetComponent<UMAData>();
			if (umaData == null)
			{
				umaData = gameObject.AddComponent<UMAData>();
//				umaData.umaGenerator = umaGenerator ?? umaData.umaGenerator;
				if (umaGenerator != null) umaData.umaGenerator = umaGenerator;
			}
		}
	}

	public void Load(UMARecipeBase umaRecipe)
	{
		if (umaRecipe == null) return;
		Profiler.BeginSample("Load");
		this.umaRecipe = umaRecipe;
		umaRecipe.Load(umaData.umaRecipe, context);
		if (umaRace != umaData.umaRecipe.raceData)
		{
			UpdateNewRace();
		}
		else
		{
			UpdateSameRace();
		}
		Profiler.EndSample();
	}

	public void UpdateSameRace()
	{
		umaData.Dirty(true, true, true);
	}

	public void UpdateNewRace()
	{
		umaRace = umaData.umaRecipe.raceData;
		var position = transform.position;
		var rotation = transform.rotation;
		if (umaChild != null)
		{
			umaData.cleanMesh(false);
			umaData.firstBake = true;
			position = umaData.umaRoot.transform.position;
			rotation = umaData.umaRoot.transform.rotation;
			Destroy(umaChild);
		}
		umaChild = Instantiate(umaData.umaRecipe.raceData.racePrefab) as GameObject;
		umaChild.transform.parent = transform;
		UMAData newUMA = umaChild.GetComponentInChildren<UMAData>();
		umaData.Assign(newUMA);
		umaData.umaGenerator = umaGenerator;
		umaData.umaRoot.transform.position = position;
		umaData.umaRoot.transform.rotation = rotation;
//		umaData.animationController = animationController ?? newUMA.animationController;
		umaData.animationController = animationController;
		if (animationController == null) umaData.animationController = newUMA.animationController;

		newUMA.animator = null;
		DestroyImmediate(newUMA);

		umaData.myRenderer.enabled = false;
		umaData.Dirty(true, true, true);
	}

	public void Hide()
	{
		Destroy(umaChild);
		umaChild = null;
		umaData.umaRoot = null;
		umaData.myRenderer = null;
		umaData.animator = null;
		umaData._hasUpdatedBefore = false;
		umaData.firstBake = true;
		umaData.ClearBoneData();
		umaRace = null;
	}

	public void Show()
	{
		if (umaRecipe != null)
		{
			Load(umaRecipe);
		}
		else
		{
			if (umaRace != umaData.umaRecipe.raceData)
			{
				UpdateNewRace();
			}
			else
			{
				UpdateSameRace();
			}
		}
	}

	void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.white;
		Gizmos.DrawCube(transform.position, new Vector3(0.6f, 0.2f, 0.6f));
	}
}
