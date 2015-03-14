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
	public UMARecipeBase[] umaAdditionalRecipes;
	public UMAGeneratorBase umaGenerator;
	public RuntimeAnimatorController animationController;
	[NonSerialized]
	[System.Obsolete("UMAAvatarBase.umaChild is obsolete use UMAData.umaRoot instead", false)]
	public GameObject umaChild;
	protected RaceData umaRace = null;

	public UMADataEvent CharacterCreated;
	public UMADataEvent CharacterDestroyed;
	public UMADataEvent CharacterUpdated;

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
				if (umaGenerator != null && !umaGenerator.gameObject.activeInHierarchy)
				{
					Debug.LogError("Invalid UMA Generator on Avatar.", gameObject);
					Debug.LogError("UMA generators must be active scene objects!", umaGenerator.gameObject);
					umaGenerator = null;
				}
			}
		}
		if (umaGenerator != null)
		{
			umaData.umaGenerator = umaGenerator;
		}
		if (CharacterCreated != null) umaData.CharacterCreated = CharacterCreated;
		if (CharacterDestroyed != null) umaData.CharacterDestroyed = CharacterDestroyed;
		if (CharacterUpdated != null) umaData.CharacterUpdated = CharacterUpdated;
	}

	public virtual void Load(UMARecipeBase umaRecipe)
	{
		Load(umaRecipe, null);
	}

	public virtual void Load(UMARecipeBase umaRecipe, params UMARecipeBase[] umaAdditionalRecipes)
	{
		if (umaRecipe == null) return;
		Profiler.BeginSample("Load");

		this.umaRecipe = umaRecipe;

		umaRecipe.Load(umaData.umaRecipe, context);
		umaData.AddAdditionalRecipes(umaAdditionalRecipes, context);

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
		if (animationController != null)
		{
			umaData.animationController = animationController;
//				umaData.animator.runtimeAnimatorController = animationController;
		}
		umaData.umaGenerator = umaGenerator;

		umaData.Dirty(true, true, true);
	}

	public virtual void Hide()
	{
		if (umaData != null)
		{
			umaData.cleanTextures();
			umaData.cleanMesh(true);
			umaData.cleanAvatar();
			Destroy(umaData.umaRoot);
			umaData.umaRoot = null;
			umaData.myRenderer = null;
			umaData.animator = null;
			umaData.firstBake = true;
			umaData.ClearBoneData();
		}
		umaRace = null;
	}

	public virtual void Show()
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
