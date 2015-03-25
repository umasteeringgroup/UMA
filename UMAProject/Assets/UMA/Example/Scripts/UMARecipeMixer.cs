using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

public class UMARecipeMixer : MonoBehaviour
{

	public enum SelectionType
	{
		IncludeOne, 
		IncludeSome,
		IncludeAll	
	}

	[System.Serializable]
	public class RecipeSection
	{
		public string name;
		public SelectionType selectionRule = SelectionType.IncludeOne;
		public UMARecipeBase[] recipes;
	}

	public RaceData raceData;
	public OverlayColorData[] sharedColors;
	public RecipeSection[] recipeSections;
	public UMARecipeBase[] additionalRecipes;

	public void FillUMARecipe(UMAData.UMARecipe umaRecipe, UMAContext context)
	{
		if (raceData == null)
		{
			Debug.LogWarning("Race Data must be set!");
			return;
		}
		umaRecipe.SetRace(raceData);
		umaRecipe.sharedColors = sharedColors;

		int sectionCount = (recipeSections == null) ? 0 : recipeSections.Length;
		for (int i = 0; i < sectionCount; i++)
		{
			RecipeSection section = recipeSections[i];
			if ((section.recipes == null) || (section.recipes.Length == 0))
				continue;

			switch (section.selectionRule)
			{
				case SelectionType.IncludeAll:
					for (int j = 0; j < section.recipes.Length; j++)
					{
						IncludeRecipe(section.recipes[j], umaRecipe, context, false);
					}
					break;
				case SelectionType.IncludeSome:
					float chance = 1f / (float)(section.recipes.Length + 1);
					for (int j = 0; j < section.recipes.Length; j++)
					{
						if (Random.value < chance)
						{
							IncludeRecipe(section.recipes[j], umaRecipe, context, false);
						}
					}
					break;
				case SelectionType.IncludeOne:
				default:
					int index = Random.Range(0, section.recipes.Length);
					IncludeRecipe(section.recipes[index], umaRecipe, context, false);
					break;
			}
		}

		for (int i = 0; i < additionalRecipes.Length; i++)
		{
			IncludeRecipe(additionalRecipes[i], umaRecipe, context, true);
		}
	}

	private void IncludeRecipe(UMARecipeBase recipe, UMAData.UMARecipe umaRecipe, UMAContext context, bool additional)
	{
		UMAData.UMARecipe cachedRecipe = recipe.GetCachedRecipe(context);
		umaRecipe.Merge(cachedRecipe, additional);
	}
}
