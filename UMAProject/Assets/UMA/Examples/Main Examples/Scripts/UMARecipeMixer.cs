using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMA.Examples
{
	/// <summary>
	/// Merges multiple recipe fragments into a complete UMA recipe.
	/// </summary>
	public class UMARecipeMixer : MonoBehaviour
	{
		/// <summary>
		/// Options for recipe fragmentss to include from each section.
		/// </summary>
		public enum SelectionType
		{
			IncludeOne, 
			IncludeSome,
			IncludeAll,
			IncludeNone
		}

		/// <summary>
		/// Set of similar recipe fragments for potentail inclusion.
		/// </summary>
		[System.Serializable]
		public class RecipeSection
		{
			public string name;
			public SelectionType selectionRule = SelectionType.IncludeOne;
			public UMARecipeBase[] recipes;
		}

		/// <summary>
		/// The race of the merged recipe.
		/// </summary>
		public RaceData raceData;
		/// <summary>
		/// The recipe sections.
		/// </summary>
		public RecipeSection[] recipeSections;
		/// <summary>
		/// Additional non serialized recipe fragments to include in all recipes.
		/// </summary>
		public UMARecipeBase[] additionalRecipes;

		/// <summary>
		/// Fills in a UMA recipe with random partial fragments from the sections.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="context">Context.</param>
		public void FillUMARecipe(UMAData.UMARecipe umaRecipe, UMAContext context)
		{
			if (raceData == null)
			{
				Debug.LogWarning("Race Data must be set!");
				return;
			}
			umaRecipe.SetRace(raceData);

			int sectionCount = (recipeSections == null) ? 0 : recipeSections.Length;
			for (int i = 0; i < sectionCount; i++)
			{
				RecipeSection section = recipeSections[i];
				if ((section.recipes == null) || (section.recipes.Length == 0))
					continue;

				switch (section.selectionRule)
				{
					case SelectionType.IncludeNone:
						break;
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

		private void IncludeRecipe(UMARecipeBase recipe, UMAData.UMARecipe umaRecipe, UMAContext context, bool dontSerialize)
		{
			UMAData.UMARecipe cachedRecipe = recipe.GetCachedRecipe(context);
			umaRecipe.Merge(cachedRecipe, dontSerialize);
		}
	}
}
