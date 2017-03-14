using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif
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
	
	public bool saveCrowd = false;
	private string saveFolderPath;

	public SharedColorTable[] sharedColors;

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

#if UNITY_EDITOR
		if (saveCrowd)
		{
			saveFolderPath = "Saved Crowd " + System.DateTime.Now.ToString().Replace('/', '_');
			saveFolderPath = saveFolderPath.Replace(':', '_');
			string folderGUID = AssetDatabase.CreateFolder("Assets", saveFolderPath);
			saveFolderPath = AssetDatabase.GUIDToAssetPath(folderGUID);
			Debug.LogWarning("Saving all generated recipes into: " + saveFolderPath);
		}
#endif
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
				RandomizeAll();
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
		newGO.transform.localPosition = umaPos;
		newGO.transform.localRotation = Quaternion.identity;

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

		RandomizeRecipe(umaData);
		RandomizeDNA(umaData);

		if (animationController != null)
		{
			umaAvatar.animationController = animationController;
		}
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
	
	public virtual void RandomizeRecipe(UMAData umaData)
	{
		UMARecipeMixer mixer = recipeMixers[Random.Range(0, recipeMixers.Length)];
		
		mixer.FillUMARecipe(umaData.umaRecipe, context);

		OverlayColorData[] recipeColors = umaData.umaRecipe.sharedColors;
		if ((recipeColors != null) && (recipeColors.Length > 0))
		{
			foreach (var sharedColor in sharedColors)
			{
				if (sharedColor == null) continue;
				int index = Random.Range(0, sharedColor.colors.Length);
				for (int i = 0; i < recipeColors.Length; i++)
				{
					if (recipeColors[i].name == sharedColor.sharedColorName)
					{
						recipeColors[i].color = sharedColor.colors[index].color;
					}
				}
			}
		}

		// This is a HACK - maybe there should be a clean way
		// of removing a conflicting slot via the recipe?
		int maleJeansIndex = -1;
		int maleLegsIndex = -1;
		SlotData[] slots = umaData.umaRecipe.GetAllSlots();
		for (int i = 0; i < slots.Length; i++)
		{
			SlotData slot = slots[i];
			if (slot == null) continue;
			if (slot.asset.name == null) continue;

			if (slot.asset.slotName == "MaleJeans01") maleJeansIndex = i;
			else if (slot.asset.slotName == "MaleLegs") maleLegsIndex = i;
		}
		if ((maleJeansIndex >= 0) && (maleLegsIndex >= 0))
		{
			umaData.umaRecipe.SetSlot(maleLegsIndex, null);
		}

#if UNITY_EDITOR
		if (saveCrowd)
		{
			SaveRecipe(umaData, context);
		}
#endif
	}

#if UNITY_EDITOR
	protected void SaveRecipe(UMAData umaData, UMAContext context)
	{
		string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(saveFolderPath, umaData.umaRecipe.raceData.raceName + ".asset"));
		var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
		asset.Save(umaData.umaRecipe, context);
		AssetDatabase.CreateAsset(asset, assetPath);
		AssetDatabase.SaveAssets();
	}
#endif

	public virtual void RandomizeDNA(UMAData umaData)
	{
		RaceData race = umaData.umaRecipe.GetRace();
		if ((race != null) && (race.dnaRanges != null))
		{
			foreach (DNARangeAsset dnaRange in race.dnaRanges)
			{
				dnaRange.RandomizeDNA(umaData);
			}
		}
	}
	
	public virtual void RandomizeDNAGaussian(UMAData umaData)
	{
		RaceData race = umaData.umaRecipe.GetRace();
		if ((race != null) && (race.dnaRanges != null))
		{
			foreach (DNARangeAsset dnaRange in race.dnaRanges)
			{
				dnaRange.RandomizeDNAGaussian(umaData);
			}
		}
	}

	public void RandomizeAll()
	{
		if (generating)
		{
			Debug.LogWarning("Can't randomize while generating.");
			return;
		}
		
		int childCount = gameObject.transform.childCount;
		for (int i = 0; i < childCount; i++)
		{
			Transform child = gameObject.transform.GetChild(i);
			UMADynamicAvatar umaAvatar = child.gameObject.GetComponent<UMADynamicAvatar>();
			if (umaAvatar == null) continue;

			UMAData umaData = umaAvatar.umaData;
			umaData.umaRecipe = new UMAData.UMARecipe();

			RandomizeRecipe(umaData);
			RandomizeDNA(umaData);
			
			if (animationController != null)
			{
				umaAvatar.animationController = animationController;
			}
			umaAvatar.Show();
		}
	}
}
