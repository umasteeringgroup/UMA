using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

public class UMARecipeCrowd : MonoBehaviour
{
	public UMAContext context;
	public UMAGeneratorBase generator;
	public RuntimeAnimatorController animationController;

	public float atlasScale = 0.5f;

	public bool hideWhileGenerating;
	public bool stressTest;
	public Vector2 crowdSize;

	public float space = 1f;
	private int spawnX;
	private int spawnY;
	private bool generating = false;

	public UMARecipeMixer[] recipeMixers;

	public UMADataEvent CharacterCreated;
	public UMADataEvent CharacterDestroyed;
	public UMADataEvent CharacterUpdated;

	void Awake()
	{
		if (space <= 0)
			space = 1f;

		if (atlasScale > 1f)
			atlasScale = 1f;
		if (atlasScale < 0.0625f)
			atlasScale = 0.0625f;

		if ((crowdSize.x > 0) && (crowdSize.y > 0))
			generating = true;
	}

	void Update()
	{
		if (generator.IsIdle())
		{
			if (generating)
			{
				GenerateOneCharacter();
			}
			else if (stressTest)
			{
//				RandomizeAll();
			}
		}
	}

	void CharacterCreatedCallback(UMAData umaData)
	{
		if (hideWhileGenerating)
		{
			if (umaData.animator != null)
				umaData.animator.enabled = false;
			if (umaData.myRenderer != null)
				umaData.myRenderer.enabled = false;
		}
	}
	
	public GameObject GenerateOneCharacter()
	{
		if ((recipeMixers == null) || (recipeMixers.Length == 0))
			return null;

		Vector3 umaPos = new Vector3((spawnX - crowdSize.x / 2f) * space, 0f, (spawnY - crowdSize.y / 2f) * space);

		if (spawnY < crowdSize.y)
		{
			spawnX++;
			if (spawnX >= crowdSize.x)
			{
				spawnX = 0;
				spawnY++;
			}
		}
		else
		{
			if (hideWhileGenerating)
			{
				UMAData[] generatedCrowd = GetComponentsInChildren<UMAData>();
				foreach (UMAData generatedData in generatedCrowd)
				{
					if (generatedData.animator != null)
						generatedData.animator.enabled = true;
					if (generatedData.myRenderer != null)
						generatedData.myRenderer.enabled = true;
				}
			}
			spawnX = 0;
			spawnY = 0;
			generating = false;
			return null;
		}

		GameObject newGO = new GameObject("Generated Character");
		newGO.transform.parent = transform;
		newGO.transform.position = umaPos;

		UMADynamicAvatar umaAvatar = newGO.AddComponent<UMADynamicAvatar>();
		umaAvatar.context = context;
		umaAvatar.umaGenerator = generator;
		umaAvatar.Initialize();
		UMAData umaData = umaAvatar.umaData;
		umaData.atlasResolutionScale = atlasScale;
		umaData.CharacterCreated = new UMADataEvent(CharacterCreated);
		umaData.OnCharacterCreated += CharacterCreatedCallback;
		umaData.CharacterDestroyed = new UMADataEvent(CharacterDestroyed);
		umaData.CharacterUpdated = new UMADataEvent(CharacterUpdated);

		int mixer = Random.Range(0, recipeMixers.Length);
		recipeMixers[mixer].FillUMARecipe(umaData.umaRecipe, context);

		if (animationController != null)
		{
			umaAvatar.animationController = animationController;
		}

		RandomizeDNA(umaAvatar);
		umaAvatar.Show();

		return newGO;
	}

	public void ReplaceAll()
	{
		if (generating)
		{
			Debug.LogWarning("Can't replace while generating.");
			return;
		}

		int childCount = gameObject.transform.childCount;
		while(--childCount >= 0)
		{
			Transform child = gameObject.transform.GetChild(childCount);
			Destroy(child.gameObject);
		}

		generating = true;
	}
	
	public virtual void RandomizeDNA(UMADynamicAvatar umaAvatar)
	{
		RaceData race = umaAvatar.umaData.umaRecipe.GetRace();
		if ((race != null) && (race.dnaRanges != null))
		{
			foreach (DNARangeAsset dnaRange in race.dnaRanges)
			{
				dnaRange.RandomizeDNA(umaAvatar.umaData);
			}
		}
	}
	
	public virtual void RandomizeDNAGaussian(UMADynamicAvatar umaAvatar)
	{
		RaceData race = umaAvatar.umaData.umaRecipe.GetRace();
		if ((race != null) && (race.dnaRanges != null))
		{
			foreach (DNARangeAsset dnaRange in race.dnaRanges)
			{
				dnaRange.RandomizeDNAGaussian(umaAvatar.umaData);
			}
		}
	}

//	public void RandomizeAll()
//	{
//		if (generating)
//		{
//			Debug.LogWarning("Can't randomize while generating.");
//			return;
//		}
//		
//		int childCount = gameObject.transform.childCount;
//		for (int i = 0; i < childCount; i++)
//		{
//			Transform child = gameObject.transform.GetChild(i);
//			UMADynamicAvatar umaDynamicAvatar = child.gameObject.GetComponent<UMADynamicAvatar>();
//			if (umaDynamicAvatar == null)
//			{
//				Debug.Log("Couldn't find dynamic avatar on child: " + child.gameObject.name);
//				continue;
//			}
//			umaData = umaDynamicAvatar.umaData;
//			var umaRecipe = umaDynamicAvatar.umaData.umaRecipe;
//			UMACrowdRandomSet.CrowdRaceData race = null;
//			
//			if (randomPool != null && randomPool.Length > 0)
//			{
//				int randomResult = Random.Range(0, randomPool.Length);
//				race = randomPool[randomResult].data;
//				umaRecipe.SetRace(GetRaceLibrary().GetRace(race.raceID));
//			}
//			else
//			{
//				if (Random.value < 0.5f)
//				{
//					umaRecipe.SetRace(GetRaceLibrary().GetRace("HumanMale"));
//				}
//				else
//				{
//					umaRecipe.SetRace(GetRaceLibrary().GetRace("HumanFemale"));
//				}
//			}
//			
//			if (animationController != null)
//			{
//				umaDynamicAvatar.animationController = animationController;
//			}
//			umaDynamicAvatar.Show();
//		}
//	}
}
